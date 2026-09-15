using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Pins the additive sandbox Seraph Lancer, built the V18 way. Every number below is read off the
    /// shipped ASSET or prefab (rule 9), never off a field initialiser; the javelins' arithmetic -- the
    /// one thing this body adds to the roster -- is held against the player's own bars, against his, and
    /// against the parry's facing rule, because he is the roster's FIRST boss and its projectile lesson.
    /// </summary>
    public class SeraphLancerDataTests
    {
        const string Fbx = "Assets/Enemies/SeraphLancer.fbx";
        const string Manifest = "Assets/Enemies/SeraphLancer.clips.json";
        const string Provenance = "Assets/Enemies/SeraphLancer.provenance.txt";
        const string PrefabPath = "Assets/Prefabs/Legendary_SeraphLancer.prefab";
        const float CueLead = 0.28f;              // EnemyController.cueLead == Projectile.CueLead
        const float PlayerPostureMax = 100f;      // PlayerPosture.Max default; ComputeMax adds stat upgrades on top
        const float PlayerChestHeight = 1.2f;     // Projectile.Chest: the point every bolt is aimed at
        const float ThrowHandHeight = 1.61f;      // RightHand at the JavelinThrow peak (measure_forge_fbx handY), the muzzle
        const float ParryBeatFloor = 0.69f;       // the parry contract's floor (the Marionette's beat)
        const float CameraPitchClamp = 89f;       // PlayerLook clamps pitch to +/-89

        static readonly string[] AttackNames =
        {
            "SeraphLancer_Jab2", "SeraphLancer_Swing", "SeraphLancer_ComboFinisher", "SeraphLancer_Stab",
            "SeraphLancer_Kick", "SeraphLancer_Heavy", "SeraphLancer_ShoulderCharge",
            SeraphLancerAuthoring.SkyVerdictAttackName,
        };

        static EnemyData Data() => AssetDatabase.LoadAssetAtPath<EnemyData>(
            EnemyPaths.Data(SeraphLancerAuthoring.EnemyName));

        static EnemyData Judge() => AssetDatabase.LoadAssetAtPath<EnemyData>(
            EnemyPaths.Data(CinderJudgeAuthoring.EnemyName));

        static EnemyData Dancer() => AssetDatabase.LoadAssetAtPath<EnemyData>(
            EnemyPaths.Data(OrbitDancerAuthoring.EnemyName));

        static EnemyData V18() => AssetDatabase.LoadAssetAtPath<EnemyData>(
            EnemyPaths.Data(FlurryBrawlerV18Authoring.EnemyName));

        static EnemyAttackData Atk(string name) => AssetDatabase.LoadAssetAtPath<EnemyAttackData>(
            "Assets/Data/Attacks/" + name + ".asset");

        static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        static PlayerStatsData Stats() => AssetDatabase.LoadAssetAtPath<PlayerStatsData>(
            "Assets/Data/PlayerStats.asset");

        static IEnumerable<EnemyAttackData> EveryHit()
        {
            var seen = new HashSet<EnemyAttackData>();
            var data = Data();
            if (data == null) yield break;
            foreach (var combo in data.ResolveCombos())
                foreach (var hit in combo.hits)
                    if (hit != null && seen.Add(hit)) yield return hit;
        }

        static MovesetEntry VerdictEntry()
        {
            foreach (var e in Data().moveset.entries)
                if (e.combo.hits.Length == 1 && e.combo.hits[0].name == SeraphLancerAuthoring.SkyVerdictAttackName) return e;
            return null;
        }

        static AnimationClip ClipNamed(string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx))
            {
                var c = o as AnimationClip;
                if (c != null && c.name == name) return c;
            }
            return null;
        }

        // ------------------------------------------------------------------ source art

        [Test]
        public void SourceArt_IsTheApprovedSeraphLancerExport()
        {
            Assert.AreEqual(SeraphLancerAuthoring.SourceFbxSha256, Sha256(Fbx));
            Assert.IsTrue(File.Exists(Manifest));
            string prov = File.ReadAllText(Provenance);
            StringAssert.Contains(SeraphLancerAuthoring.SourceFbxSha256, prov);
            StringAssert.Contains(SeraphLancerAuthoring.SourceManifestSha256, prov);
            StringAssert.Contains("seed 43", prov, "the final describe's seed is provenance: five attempts went into it.");
        }

        [Test]
        public void Manifest_IsExactlyTheApprovedSeventeenStates()
        {
            CollectionAssert.AreEqual(SeraphLancerAuthoring.ClipAllowlist, ForgeClipSplitter.ClipNames(Fbx));
            // V18's base kit, the two reactions PuppetVisuals requires (Hit, Death), the throw and the
            // hover pose generated for this body. Nothing from the larger source export -- no IdleCombat,
            // AttackOverhead, Block or Spawn -- may creep in with a re-export.
            Assert.AreEqual(17, SeraphLancerAuthoring.ClipAllowlist.Length);
            CollectionAssert.Contains(SeraphLancerAuthoring.ClipAllowlist, "JavelinThrow");
            CollectionAssert.Contains(SeraphLancerAuthoring.ClipAllowlist, "HoverHold");
            CollectionAssert.Contains(SeraphLancerAuthoring.ClipAllowlist, "Jump");
            CollectionAssert.DoesNotContain(SeraphLancerAuthoring.ClipAllowlist, "Block");
            CollectionAssert.DoesNotContain(SeraphLancerAuthoring.ClipAllowlist, "IdleCombat");

            // The throw carries the source's own release frame, so 4b bakes the manifest and no explicit
            // profile is needed (unlike the Judge's Roar). HoverHold is a held pose with no contact and
            // is named by no attack.
            var withHit = ForgeClipSplitter.ClipsWithEvent(Fbx, "OnAttackHit");
            CollectionAssert.Contains(withHit, "JavelinThrow");
            CollectionAssert.DoesNotContain(withHit, "HoverHold");
            Assert.AreEqual(0.696f, ForgeClipSplitter.ReadEventNormalizedTime(Fbx, "JavelinThrow", 0f, "OnAttackHit"), 0.001f);
            float unused;
            foreach (var clip in SeraphLancerAuthoring.ClipAllowlist)
                Assert.IsFalse(SeraphLancerAuthoring.TryExplicitContact(SeraphLancerAuthoring.EnemyName, clip, out unused),
                    clip + ": the Lancer has no explicit contact profile; the one named generated clip ships its own OnAttackHit.");
        }

        [Test]
        public void ImportedRigAndController_AreExactlyTheNonemptyEventlessAllowlist()
        {
            var importer = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            Assert.IsNotNull(importer);
            Assert.AreEqual(ModelImporterAnimationType.Generic, importer.animationType);
            Assert.AreEqual(17, importer.clipAnimations.Length,
                "run VibeGame1/4a. Split Forge Animation Clips");
            foreach (var c in importer.clipAnimations)
                Assert.AreEqual(0, c.events.Length, c.name + " has gameplay AnimationEvents.");

            var imported = PuppetAnimatorFactory.ClipsIn(Fbx);
            Assert.AreEqual(17, imported.Count);
            var importedNames = new List<string>();
            foreach (var c in imported)
            {
                importedNames.Add(c.name);
                Assert.IsFalse(c.empty, c.name + " imported empty.");
                Assert.AreEqual(0, AnimationUtility.GetAnimationEvents(c).Length,
                    c.name + " has a runtime AnimationEvent.");
            }
            CollectionAssert.AreEquivalent(SeraphLancerAuthoring.ClipAllowlist, importedNames);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                PuppetAnimatorFactory.ControllerDir + "/Legendary_SeraphLancer_Animator.controller");
            Assert.IsNotNull(controller, "run VibeGame1/4b. Build Mini-Bosses");
            var states = controller.layers[0].stateMachine.states;
            Assert.AreEqual(17, states.Length);
            var stateNames = new List<string>();
            foreach (var child in states)
            {
                stateNames.Add(child.state.name);
                var clip = child.state.motion as AnimationClip;
                Assert.IsNotNull(clip, child.state.name + " has no AnimationClip motion.");
                Assert.IsFalse(clip.empty, child.state.name + " points to an empty clip.");
            }
            CollectionAssert.AreEquivalent(SeraphLancerAuthoring.ClipAllowlist, stateNames);
        }

        // ------------------------------------------------------------------ data

        [Test]
        public void Data_IsSeparate_TheReadableFirstBoss()
        {
            var d = Data();
            Assert.IsNotNull(d, "run VibeGame1/3. Create Data");
            Assert.AreEqual("THE SERAPH LANCER", d.displayName);
            Assert.AreEqual(200f, d.maxHP, 0.001f);
            Assert.AreEqual(170f, d.maxPosture, 0.001f);
            Assert.AreEqual(6f, d.postureRegen, 0.001f);
            Assert.AreEqual(3.2f, d.staggerSeconds, 0.001f);
            Assert.AreEqual(4.8f, d.moveSpeed, 0.001f);
            Assert.AreEqual(2.6f, d.preferredRange, 0.001f);
            Assert.AreEqual(18f, d.aggroRange, 0.001f);
            Assert.AreEqual(0.70f, d.aggression, 0.001f);
            Assert.AreEqual(0.40f, d.strafeSpeedMultiplier, 0.001f);
            Assert.AreEqual(1f, d.scale, 0.001f);
            Assert.AreEqual(0.5f, d.flaskPunishChance, 0.001f, "the first boss punishes a flask half the time");
            // A SOULS duel with a projectile he throws himself: never a sentry.
            Assert.IsFalse(d.shootsProjectiles, "no ProjectileShooter, no metronome, no perch wake");
            Assert.IsFalse(d.rangedOnly);
            Assert.AreNotSame(d, V18());
            Assert.AreNotSame(d, Judge());
            Assert.AreNotSame(d, Dancer());

            // The spice, held: the readable one. Lower aggression than any of the three, and every
            // shared blow winds up no faster than the Dancer's and (the lance apart) no slower than the
            // Judge's; he never hits harder than the Judge.
            var judge = Judge();
            var dancer = Dancer();
            if (judge != null)
            {
                Assert.Less(d.maxHP, judge.maxHP);
                Assert.Less(d.aggression, judge.aggression);
                Assert.Greater(d.preferredRange, judge.preferredRange);
                foreach (var suffix in new[] { "Jab2", "Swing", "ComboFinisher", "Kick", "Heavy", "ShoulderCharge" })
                {
                    var his = Atk("SeraphLancer_" + suffix);
                    var judges = Atk("CinderJudge_" + suffix);
                    if (his == null || judges == null) continue;
                    Assert.LessOrEqual(his.windup, judges.windup, suffix + ": the Lancer is never slower than the Judge.");
                    Assert.LessOrEqual(his.damage, judges.damage, suffix + ": and never hits harder.");
                }
                // The one exception: the lance reaches further than the Judge's stab, so it tells longer.
                Assert.Greater(Atk("SeraphLancer_Stab").range, Atk("CinderJudge_Stab").range);
                Assert.GreaterOrEqual(Atk("SeraphLancer_Stab").windup, Atk("CinderJudge_Stab").windup);
            }
            if (dancer != null)
            {
                Assert.Less(d.aggression, dancer.aggression);
                Assert.Less(d.moveSpeed, dancer.moveSpeed);
                foreach (var suffix in new[] { "Jab2", "Swing", "ComboFinisher", "Stab", "Kick", "Heavy", "ShoulderCharge" })
                {
                    var his = Atk("SeraphLancer_" + suffix);
                    var hers = Atk("OrbitDancer_" + suffix);
                    if (his == null || hers == null) continue;
                    Assert.GreaterOrEqual(his.windup, hers.windup, suffix + ": the first boss must read at least as slowly as the T3.");
                }
                Assert.Less(d.projectileSpeed, dancer.projectileSpeed, "T1's javelin is slower than T3's disc.");
            }
            var v18 = V18();
            if (v18 != null)
            {
                Assert.Greater(d.maxHP, v18.maxHP);
                Assert.Greater(d.preferredRange, v18.preferredRange);
            }

            var names = new HashSet<string>();
            foreach (var h in EveryHit()) names.Add(h.name);
            CollectionAssert.AreEquivalent(AttackNames, names);
            CollectionAssert.DoesNotContain(names, SeraphLancerAuthoring.JavelinAttackName,
                "the javelin's attack is carried by the javelin, never scheduled by the brain.");
        }

        [Test]
        public void AttackTimingsAndTravel_AreTheApprovedProfile()
        {
            AssertAttack("SeraphLancer_Jab2", "Jab2", 0.50f, 0.05f, 0.16f, 0.65f, 0f);
            AssertAttack("SeraphLancer_Swing", "AttackSwing", 0.55f, 0.05f, 0.18f, 0.72f, 0f);
            AssertAttack("SeraphLancer_ComboFinisher", "ComboFinisher", 0.65f, 0.06f, 0.20f, 1.20f, 0f);
            AssertAttack("SeraphLancer_Stab", "AttackStab", 0.60f, 0.05f, 0.16f, 0.80f, 0f);
            AssertAttack("SeraphLancer_Kick", "AttackKick", 0.60f, 0.05f, 0.20f, 0.85f, 0f);
            AssertAttack("SeraphLancer_Heavy", "HeavyAttack", 1.05f, 0.08f, 0.30f, 1.40f, 0.75f);
            AssertAttack("SeraphLancer_ShoulderCharge", "ShoulderCharge", 0.95f, 0.08f, 0.27f, 1.05f, 3.32f);
            AssertAttack(SeraphLancerAuthoring.SkyVerdictAttackName, "JavelinThrow", 1.10f, 0.90f, 2.75f, 2.40f, 0f);

            // ONE red, and it is the far-band shoulder. The kick is blue: T1 has no unblockable inside
            // arm's reach.
            Assert.IsTrue(Atk("SeraphLancer_ShoulderCharge").unblockable);
            foreach (var h in EveryHit())
                if (h.name != "SeraphLancer_ShoulderCharge")
                    Assert.IsFalse(h.unblockable, h.name + ": the shoulder is the Lancer's only red.");
            Assert.AreEqual(1.9f, Atk("SeraphLancer_Heavy").parryPostureMultiplier, 0.001f);
            Assert.AreEqual(1.2f, Atk("SeraphLancer_Kick").parryPostureMultiplier, 0.001f, "the blue kick pays a little extra on a Perfect");

            // The lance: the roster's longest thrust on its narrowest line.
            var stab = Atk("SeraphLancer_Stab");
            Assert.AreEqual(2.9f, stab.range, 0.001f);
            Assert.AreEqual(35f, stab.coneDeg, 0.001f);
            Assert.Greater(stab.range + 0.5f, Data().preferredRange, "the lance must reach from his preferred stand.");

            // No wind-up under the 0.45 s floor, and every impact cued before it lands.
            foreach (var h in EveryHit())
            {
                Assert.GreaterOrEqual(h.windup, 0.45f, h.name);
                Assert.Greater(h.windup + h.impactDelay, CueLead, h.name + " cannot be cued before impact.");
            }
        }

        [Test]
        public void EveryLungeIsTheClipsOwnForwardTravel()
        {
            // The Halberdier rule: a generated clip marked root.motion walks the Hips inside the pose,
            // TravelRoot cancels that, and the FORWARD component ships as lungeDistance. Sideways drift
            // (the Heavy's 1.14 m to the right), backward steps (Jab2) and the throw's 0.09 m weight
            // shift have no lunge channel. The Judge's Unity number (3.32 on ShoulderCharge, Blender 3.12)
            // is shipped on the same array; if THIS import reads differently the message below says what
            // to ship.
            foreach (var h in EveryHit())
            {
                var clip = ClipNamed(h.clip);
                if (clip == null) continue;   // reported by the import test
                if (!ForgeClipSplitter.ClipIsGenerated(Fbx, h.clip)) continue;
                float forward = SampledHipsForwardTravel(clip);
                if (ForgeClipSplitter.ClipTravels(Fbx, h.clip))
                {
                    Assert.AreEqual(Mathf.Max(0f, forward), h.lungeDistance, 0.15f,
                        h.name + ": lungeDistance " + h.lungeDistance + " but '" + h.clip + "' walks the Hips " +
                        forward.ToString("F2") + " m forward in Unity. Ship " + Mathf.Max(0f, forward).ToString("F2") + ".");
                }
                else
                {
                    Assert.AreEqual(0f, h.lungeDistance, 0.001f,
                        h.name + ": '" + h.clip + "' does not travel in the art, so the enemy must not either.");
                }
            }
        }

        // ------------------------------------------------------------------ the javelins

        [Test]
        public void Javelins_AreProjectilesCarryingTheJavelinAttack_OnTheLancersOwnNumbers()
        {
            var d = Data();
            Assert.IsNotNull(d, "run VibeGame1/3. Create Data");
            var javelin = Atk(SeraphLancerAuthoring.JavelinAttackName);
            Assert.IsNotNull(javelin);
            Assert.AreSame(javelin, d.projectileAttack, "a Javelin arrives with EnemyData.projectileAttack.");
            Assert.AreEqual(14f, javelin.damage, 0.001f, "the Judge's jab per javelin");
            Assert.IsFalse(javelin.unblockable, "a javelin is Block or Perfect, never a red: he is the teaching boss.");
            Assert.AreEqual(1.2f, javelin.parryPostureMultiplier, 0.001f);
            Assert.AreEqual(15f, d.projectileSpeed, 0.001f, "a shade under the Dancer's disc; half a sentry bolt");
            Assert.AreEqual(40f, d.projectileHomingDegPerSec, 0.001f, "low homing: it arrives at a sidestep, a dash still dodges");
            Assert.Less(d.projectileHomingDegPerSec * 1f, 45f, "never a curve you cannot read: under 45 deg over a whole second of flight");
            Assert.AreEqual(0.7f, d.projectileLead, 0.001f);
            Assert.AreEqual(22f, d.parriedProjectileDamage, 0.001f);
            Assert.AreEqual(30f, d.parriedProjectilePosture, 0.001f);
            Assert.AreEqual(2.5f, d.parrySpeedGain, 0.001f, "a nudge, not the sentries' span boost");
            Assert.AreEqual(0.5f, d.projectileTrailScale, 0.001f);
            Assert.AreEqual(0.7f, d.projectileCueScale, 0.001f);
            Assert.AreEqual(0, d.perfectBurstParriesToDestroy);
            Assert.Less(d.projectileSpeed, 17.6f, "under the Projectile Encounter Report's mid route-speed gate");
        }

        [Test]
        public void SkyVerdict_IsOneSchedule_WhoseContactIsNothing_AndWhoseBandIsBeyondTheCue()
        {
            var verdict = Atk(SeraphLancerAuthoring.SkyVerdictAttackName);
            Assert.IsNotNull(verdict, "run VibeGame1/3. Create Data");
            // The brain's DoImpact can never land the verdict: the javelins ARE the attack.
            Assert.AreEqual(0f, verdict.range, 0.001f);
            Assert.AreEqual(0f, verdict.coneDeg, 0.001f);
            Assert.AreEqual(0f, verdict.damage, 0.001f);
            Assert.IsFalse(verdict.unblockable);
            Assert.IsFalse(verdict.windupPose.authored, "the clip carries the silhouette; nothing is posed on LungeRoot.");

            int verdictUses = 0;
            foreach (var e in Data().moveset.entries)
                foreach (var hit in e.combo.hits) if (hit == verdict) verdictUses++;
            Assert.AreEqual(1, verdictUses, "the verdict is one standalone EnemyController schedule, never inside a string.");
            var entry = VerdictEntry();
            Assert.IsNotNull(entry, "the verdict entry");
            Assert.AreEqual(2.0f, entry.weight, 0.001f);
            Assert.AreEqual(4.5f, entry.minRange, 0.001f);
            Assert.AreEqual(14f, entry.maxRange, 0.001f);
            Assert.AreEqual(10f, entry.cooldown, 0.001f);
            Assert.LessOrEqual(entry.maxRange, Data().aggroRange, "a far band beyond aggro can never fire.");
            // The nearest throw at full speed is more than one cue lead of flight; LaunchSpeed slows it
            // further, so no javelin ever arrives inside its own cue.
            Assert.GreaterOrEqual(entry.minRange / Data().projectileSpeed, CueLead - 0.001f);

            // The chain that replaces the Judge's red one: jab-two into the lance, both blue.
            MovesetEntry chain = null;
            foreach (var e in Data().moveset.entries)
                if (e.combo.hits.Length == 2 && e.combo.hits[1] == Atk("SeraphLancer_Stab")) { Assert.IsNull(chain); chain = e; }
            Assert.IsNotNull(chain, "jab-two into LANCE THRUST replaces the Judge's red chain");
            Assert.AreSame(Atk("SeraphLancer_Jab2"), chain.combo.hits[0]);
            Assert.IsFalse(chain.combo.hits[0].unblockable);
            Assert.IsFalse(chain.combo.hits[1].unblockable);
            Assert.AreEqual(6f, chain.cooldown, 0.001f);
        }

        [Test]
        public void Verdict_StrikeCoversEveryReleaseAndTheDescent_AndTheRecoveryIsThePunish()
        {
            var verdict = Atk(SeraphLancerAuthoring.SkyVerdictAttackName);
            var launcher = Prefab().GetComponent<SeraphLancerJavelins>();
            var v = Prefab().GetComponentInChildren<SeraphLancerVisuals>(true);
            Assert.IsNotNull(launcher, "run VibeGame1/4b. Build Mini-Bosses");
            Assert.IsNotNull(v);

            // Releases at impact + n x cadence for n < count; the last must be inside the strike, and the
            // descent must begin after it (the last javelin is in the air while he lands).
            float lastRelease = SeraphLancerJavelins.ThrowTime(0f, launcher.javelinsPerVerdict - 1, launcher.javelinCadence);
            Assert.AreEqual(2.20f, lastRelease, 0.001f);
            float descentAt = verdict.strikeDuration - v.hoverDescendSeconds;
            Assert.Greater(descentAt, lastRelease, "the descent must not begin before the last release");
            Assert.Less(descentAt - lastRelease, launcher.javelinCadence, "and the strike must not hang in the air past one cadence after it");

            // The cadence leaves more than the parry contract's floor between arrivals, and more than
            // the clip's own release plus follow-through (so HoverHold has a breath between throws).
            Assert.GreaterOrEqual(launcher.javelinCadence, ParryBeatFloor);
            int throwClip = v.IndexOfNamedClip("JavelinThrow");
            Assert.GreaterOrEqual(throwClip, 0, "4b did not bake JavelinThrow.");
            float release = v.namedClipLengths[throwClip] * v.namedClipHits[throwClip];
            Assert.Less(release + v.throwFollowThroughSeconds, launcher.javelinCadence,
                "the throw clip (" + release.ToString("F2") + " s to release) must fit inside one cadence with its follow-through");
            // The first arm-back begins after the hover pose is up: impactDelay covers the release lead.
            Assert.Greater(verdict.impactDelay, release, "the first JavelinThrow must start after the apex, not during the rise");

            // The punish is real: the recovery after aggression is longer than the Heavy's.
            float agg = Data().aggression;
            float realRecovery = verdict.recovery * Mathf.Lerp(1f, 0.35f, agg);
            float heavyRecovery = Atk("SeraphLancer_Heavy").recovery * Mathf.Lerp(1f, 0.35f, agg);
            Assert.Greater(realRecovery, heavyRecovery);
            Assert.Greater(realRecovery, 1.2f, "at least a run-in and a swing");
        }

        [Test]
        public void Javelins_WorstCase_HurtsButNeverEndsEitherFighterByItself()
        {
            var d = Data();
            var javelin = Atk(SeraphLancerAuthoring.JavelinAttackName);
            var launcher = Prefab().GetComponent<SeraphLancerJavelins>();
            Assert.IsNotNull(launcher, "run VibeGame1/4b. Build Mini-Bosses");

            // A whole verdict unguarded: three of the Judge's jabs, never a bar, never a posture break.
            var stats = Stats();
            float hitPostureMult = stats != null ? stats.hitPostureMultiplier : 0.5f;
            float worstHealth = launcher.javelinsPerVerdict * javelin.damage;
            float worstPosture = launcher.javelinsPerVerdict * javelin.damage * hitPostureMult;
            Assert.AreEqual(42f, worstHealth, 0.001f);
            Assert.Less(worstHealth, 100f, "a verdict must be survivable from full health");
            Assert.Greater(worstHealth, 25f, "and must be worth answering");
            Assert.Less(worstPosture, PlayerPostureMax, "never a stagger machine");
            // Nothing he throws is unblockable.
            Assert.IsFalse(javelin.unblockable);

            // A whole verdict Perfect-reflected into him: a third of him, half his bar, never all of either.
            float reflectHealth = launcher.javelinsPerVerdict * d.parriedProjectileDamage;
            float reflectPosture = launcher.javelinsPerVerdict * d.parriedProjectilePosture;
            Assert.AreEqual(66f, reflectHealth, 0.001f);
            Assert.AreEqual(90f, reflectPosture, 0.001f);
            Assert.Less(reflectHealth, d.maxHP, "one verdict reflected must not kill him outright");
            Assert.Less(reflectPosture, d.maxPosture, "nor break him outright");
            Assert.GreaterOrEqual(reflectHealth, d.maxHP * 0.25f, "but the reflect must be his worst idea");
            Assert.GreaterOrEqual(reflectPosture, d.maxPosture * 0.25f);

            // The cap admits a whole verdict plus one flying back; no javelin outlives the cooldown.
            Assert.GreaterOrEqual(launcher.liveCap, launcher.javelinsPerVerdict + 1);
            var entry = VerdictEntry();
            float lastRelease = SeraphLancerJavelins.ThrowTime(0f, launcher.javelinsPerVerdict - 1, launcher.javelinCadence);
            Assert.Less(lastRelease + launcher.javelinLifetime, entry.cooldown, "every javelin of one verdict is gone before the next.");
            // A javelin lives long enough to cross the band and come back reflected at 1.4x.
            Assert.GreaterOrEqual(launcher.javelinLifetime * d.projectileSpeed, entry.maxRange * 2f);
        }

        [Test]
        public void HoverGeometry_KeepsEveryJavelinParryable_AndTheLessonIsLookUp()
        {
            var v = Prefab().GetComponentInChildren<SeraphLancerVisuals>(true);
            Assert.IsNotNull(v, "run VibeGame1/4b. Build Mini-Bosses");
            var entry = VerdictEntry();
            float cone = Stats() != null ? Stats().facingConeDeg : 75f;

            // The airborne muzzle: the throw hand at full lift, over a chest at 1.2 m.
            float muzzleY = v.hoverHeight + ThrowHandHeight;
            float drop = muzzleY - PlayerChestHeight;
            Assert.AreEqual(3.0f, v.hoverHeight, 0.001f);
            Assert.Greater(drop, 2.5f, "the javelin must plainly come DOWN");

            // At the band's near edge the line pitches under 45 degrees: a readable diagonal, well
            // inside the camera's reach; at the far edge it is a shallow line. Never a plunge.
            float nearPitch = SeraphLancerJavelins.PitchDeg(new Vector3(0f, muzzleY, 0f), new Vector3(entry.minRange, PlayerChestHeight, 0f));
            float farPitch = SeraphLancerJavelins.PitchDeg(new Vector3(0f, muzzleY, 0f), new Vector3(entry.maxRange, PlayerChestHeight, 0f));
            Assert.Less(nearPitch, 45f, "near edge pitch " + nearPitch.ToString("F1"));
            Assert.Greater(nearPitch, 25f, "but steep enough that 'look up' is the read");
            Assert.Less(farPitch, 20f);
            Assert.Less(nearPitch, CameraPitchClamp);

            // ParryMath judges facing on the FLAT bearing (the javelin's travel, not the perch), so a
            // player who has turned to him is facing, one who has not is hit -- at every distance the
            // band allows and even one step under him, until the line is within 0.1 of vertical, where
            // the rule calls the source degenerate and the parry is always allowed. Nothing he throws is
            // ever unparryable.
            foreach (float horizontal in new[] { entry.minRange, 8f, entry.maxRange, 2f, 1f })
            {
                Vector3 from = new Vector3(0f, muzzleY, 0f);
                Vector3 chest = new Vector3(horizontal, PlayerChestHeight, 0f);
                Vector3 travel = (chest - from).normalized;
                Vector3 source = ParryMath.SourceDirection(travel, from, chest);
                Assert.Greater(new Vector3(source.x, 0f, source.z).sqrMagnitude, 0.01f,
                    "at " + horizontal + " m the flat bearing is real, so the facing veto is judged");
                Assert.IsTrue(ParryMath.IsFacing(new Vector3(-1f, 0f, 0f), source, cone), "facing him parries at " + horizontal + " m");
                Assert.IsFalse(ParryMath.IsFacing(new Vector3(1f, 0f, 0f), source, cone), "a turned back is hit at " + horizontal + " m");
            }
            {
                // Directly beneath him: the flat component collapses, the rule calls it degenerate, the
                // parry is allowed from any facing. Still parryable.
                Vector3 from = new Vector3(0f, muzzleY, 0f);
                Vector3 chest = new Vector3(0.2f, PlayerChestHeight, 0f);
                Vector3 source = ParryMath.SourceDirection((chest - from).normalized, from, chest);
                Assert.IsTrue(ParryMath.IsFacing(new Vector3(1f, 0f, 0f), source, cone));
            }

            // The pitch helper itself.
            Assert.AreEqual(45f, SeraphLancerJavelins.PitchDeg(Vector3.zero, new Vector3(1f, -1f, 0f)), 0.001f);
            Assert.AreEqual(0f, SeraphLancerJavelins.PitchDeg(Vector3.zero, new Vector3(1f, 0f, 0f)), 0.001f);
            Assert.Less(SeraphLancerJavelins.PitchDeg(Vector3.zero, new Vector3(1f, 1f, 0f)), 0f, "upward is negative");
        }

        // ------------------------------------------------------------------ the launcher and staging maths

        [TestCase(1, 0, 4, 1)]
        [TestCase(1, 3, 4, 1)]
        [TestCase(1, 4, 4, 0)]
        [TestCase(1, 9, 4, 0)]
        [TestCase(0, 0, 4, 0)]
        public void LaunchCount_FillsOnlyTheRoomUnderTheGlobalCap(int wanted, int live, int cap, int expected)
        {
            Assert.AreEqual(expected, SeraphLancerJavelins.LaunchCount(wanted, live, cap));
        }

        [TestCase(0, 1.1f, 10f)]
        [TestCase(1, 1.1f, 11.1f)]
        [TestCase(2, 1.1f, 12.2f)]
        [TestCase(-1, 1.1f, 10f)]
        public void ThrowTime_IsWholeCadencesOffTheImpact(int index, float cadence, float expected)
        {
            Assert.AreEqual(expected, SeraphLancerJavelins.ThrowTime(10f, index, cadence), 0.0001f);
        }

        [TestCase(4.5f, 15f, 0.40f, 11.25f)]   // the band's near edge: slowed to lead + margin
        [TestCase(6.0f, 15f, 0.40f, 15f)]      // from here on the data speed ships
        [TestCase(14f, 15f, 0.9333f, 15f)]
        public void PathSpeed_NeverArrivesInsideTheCueLeadPlusMargin(float path, float speed, float flight, float expected)
        {
            float launch = SeraphLancerJavelins.PathSpeed(path, speed, CueLead, 0.12f);
            Assert.AreEqual(expected, launch, 0.001f);
            Assert.AreEqual(flight, path / launch, 0.001f);
            Assert.GreaterOrEqual(path / launch, CueLead + 0.12f - 0.0001f);
        }

        [Test]
        public void HoverLift_RisesHoldsAndDescends_OnTheStrikesOwnDeadlines()
        {
            const float rise = 10.6f, apex = 11.1f, descent = 13.4f, end = 13.85f, h = 3f;
            Assert.AreEqual(0f, SeraphLancerVisuals.HoverLift(10.0f, rise, apex, descent, end, h), 0.0001f);
            Assert.AreEqual(0f, SeraphLancerVisuals.HoverLift(rise, rise, apex, descent, end, h), 0.0001f);
            Assert.AreEqual(h * 0.5f, SeraphLancerVisuals.HoverLift((rise + apex) * 0.5f, rise, apex, descent, end, h), 0.0001f);
            Assert.AreEqual(h, SeraphLancerVisuals.HoverLift(apex, rise, apex, descent, end, h), 0.0001f);
            Assert.AreEqual(h, SeraphLancerVisuals.HoverLift(12.5f, rise, apex, descent, end, h), 0.0001f);
            Assert.AreEqual(h * 0.5f, SeraphLancerVisuals.HoverLift((descent + end) * 0.5f, rise, apex, descent, end, h), 0.0001f);
            Assert.AreEqual(0f, SeraphLancerVisuals.HoverLift(end, rise, apex, descent, end, h), 0.0001f);
            // Monotone through the rise and the descent.
            float prev = -1f;
            for (float t = rise; t <= apex; t += 0.05f)
            {
                float l = SeraphLancerVisuals.HoverLift(t, rise, apex, descent, end, h);
                Assert.GreaterOrEqual(l, prev); prev = l;
            }
        }

        [Test]
        public void DiveLift_PeaksAtTheSwitch_AndIsOnTheFloorAtTheImpact()
        {
            const float start = 5f, peak = 5.58f, impact = 6.13f, h = 0.8f;
            Assert.AreEqual(0f, SeraphLancerVisuals.DiveLift(start, start, peak, impact, h), 0.0001f);
            Assert.AreEqual(h, SeraphLancerVisuals.DiveLift(peak, start, peak, impact, h), 0.0001f);
            Assert.AreEqual(0f, SeraphLancerVisuals.DiveLift(impact, start, peak, impact, h), 0.0001f);
            Assert.AreEqual(0f, SeraphLancerVisuals.DiveLift(impact + 1f, start, peak, impact, h), 0.0001f);
            Assert.AreEqual(h * 0.5f, SeraphLancerVisuals.DiveLift((start + peak) * 0.5f, start, peak, impact, h), 0.0001f);
        }

        [Test]
        public void Wings_UnfoldFromTheTakeoff_HoldThroughTheHover_AndFoldForTheDescent()
        {
            const float open = 10.6f, unfold = 0.35f, close = 13.4f, fold = 0.25f;
            Assert.AreEqual(0f, SeraphLancerVisuals.WingSpread(10.5f, open, unfold, close, fold), 0.0001f);
            Assert.AreEqual(0.5f, SeraphLancerVisuals.WingSpread(open + unfold * 0.5f, open, unfold, close, fold), 0.0001f);
            Assert.AreEqual(1f, SeraphLancerVisuals.WingSpread(12f, open, unfold, close, fold), 0.0001f);
            Assert.AreEqual(0.5f, SeraphLancerVisuals.WingSpread(close + fold * 0.5f, open, unfold, close, fold), 0.0001f);
            Assert.AreEqual(0f, SeraphLancerVisuals.WingSpread(close + fold, open, unfold, close, fold), 0.0001f);
            // Every feather points out to its side, upward, and back.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++)
                {
                    Vector3 d = SeraphLancerVisuals.FeatherDirection(i, 3, side, 18f, 28f, 25f);
                    Assert.AreEqual(1f, d.magnitude, 0.0001f);
                    Assert.AreEqual(side > 0, d.x > 0f, "side");
                    Assert.Greater(d.y, 0f, "upward");
                    Assert.Less(d.z, 0f, "raked back");
                }
            Assert.Greater(SeraphLancerVisuals.FeatherDirection(2, 3, 1f, 18f, 28f, 25f).y,
                           SeraphLancerVisuals.FeatherDirection(0, 3, 1f, 18f, 28f, 25f).y, "the fan rises outward");
        }

        [Test]
        public void TrackingYaw_TurnsTheShortWayAtTheRate_AndSquaresToZero()
        {
            Assert.AreEqual(90f, SeraphLancerVisuals.DesiredTrackYaw(Vector3.forward, Vector3.right), 0.001f);
            Assert.AreEqual(-90f, SeraphLancerVisuals.DesiredTrackYaw(Vector3.forward, Vector3.left), 0.001f);
            Assert.AreEqual(0f, SeraphLancerVisuals.DesiredTrackYaw(Vector3.forward, Vector3.up), 0.001f, "degenerate: no turn");
            // Vertical offset is ignored: a player under him is judged flat.
            Assert.AreEqual(90f, SeraphLancerVisuals.DesiredTrackYaw(Vector3.forward, new Vector3(1f, -4f, 0f)), 0.001f);
            Assert.AreEqual(2f, SeraphLancerVisuals.TrackingYawStep(0f, 90f, 120f, 1f / 60f), 0.001f);
            Assert.AreEqual(90f, SeraphLancerVisuals.TrackingYawStep(89f, 90f, 120f, 1f / 60f), 0.001f, "never overshoots");
            // -172 to 170 is 18 degrees the NEGATIVE way round (through -180), not 342 the positive way.
            Assert.AreEqual(-174f, SeraphLancerVisuals.TrackingYawStep(-172f, 170f, 120f, 1f / 60f), 0.001f, "the short way round");
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void PresentationReanchor_ConservesRemainingClip(float frame)
        {
            const float now = 10f, oldImpact = 10.20f, oldSpeed = 1.7f;
            float actualImpact = oldImpact + frame;
            float speed = SeraphLancerVisuals.RetimedSpeed(oldSpeed, oldImpact, actualImpact, now);
            Assert.AreEqual(oldSpeed * (oldImpact - now), speed * (actualImpact - now), 0.0001f);
            Assert.AreEqual(actualImpact - 0.12f,
                SeraphLancerVisuals.ReanchoredDeadline(oldImpact - 0.12f, oldImpact, actualImpact), 0.0001f);
        }

        [Test]
        public void Relic_HoldsThenShrinks_AndTheTipCools()
        {
            Assert.AreEqual(1f, JavelinRelic.Shrink(0f, 1.5f, 0.7f), 0.0001f);
            Assert.AreEqual(1f, JavelinRelic.Shrink(1.05f, 1.5f, 0.7f), 0.0001f);
            Assert.AreEqual(0.5f, JavelinRelic.Shrink(1.275f, 1.5f, 0.7f), 0.0001f);
            Assert.AreEqual(0f, JavelinRelic.Shrink(1.5f, 1.5f, 0.7f), 0.0001f);
            Assert.AreEqual(0f, JavelinRelic.Shrink(9f, 1.5f, 0.7f), 0.0001f);
            Color hot = new Color(1.45f, 1.05f, 0.4f, 1f);
            Assert.AreEqual(hot, JavelinRelic.Cooling(hot, 0f, 1.5f));
            Assert.AreEqual(Color.black.r, JavelinRelic.Cooling(hot, 1.5f, 1.5f).r, 0.0001f);
            // The shaft keeps its world size while the tip flares 1.6x.
            Vector3 counter = Javelin.CounterScale(new Vector3(0.1f, 2f, 0.1f), 0.48f, 0.30f);
            Assert.AreEqual(2f * 0.30f / 0.48f, counter.y, 0.0001f);
            Assert.AreEqual(0f, Vector3.Angle(Vector3.right, Javelin.Heading(Vector3.right) * Vector3.forward), 0.001f);
            Assert.AreEqual(Quaternion.identity, Javelin.Heading(Vector3.zero));
            Assert.AreEqual(0.25f, Javelin.PingVolume(12f, 0.5f, 24f), 0.0001f);
        }

        [Test]
        public void TheJavelinIsAProjectile_ThroughTheExistingHooks_AndHidesNoUnityMessage()
        {
            // The javelin is a SUBCLASS of the shared bolt on the hook the Dancer's disc already added:
            // OnWorldContact (here: plant a relic, return false = spend on the wall as every sentry bolt
            // does). No Projectile.cs change was needed for it.
            Assert.IsTrue(typeof(Projectile).IsAssignableFrom(typeof(Javelin)));
            var hook = typeof(Javelin).GetMethod("OnWorldContact", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.IsNotNull(hook, "Javelin overrides Projectile.OnWorldContact");
            Assert.IsNull(typeof(Javelin).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));
            Assert.IsNull(typeof(Javelin).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));
            Assert.IsNull(typeof(Javelin).GetMethod("Redirect", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                "a javelin never re-lines itself: no ricochet, no curve after a wall");
        }

        // ------------------------------------------------------------------ prefab

        [Test]
        public void Prefab_IsAnOrdinaryEnemyController_WithTheLancersProfile_TheLauncher_TheHoverRootAndTheWings()
        {
            var p = Prefab();
            Assert.IsNotNull(p, "run VibeGame1/4a. Split Forge Animation Clips, then 4b. Build Mini-Bosses");
            Assert.IsNotNull(p.GetComponent<EnemyController>());
            Assert.IsNull(p.GetComponent<BossController>());
            Assert.IsNull(p.GetComponentInChildren<ProjectileShooter>(true), "he throws by hand, on the brain's clock: no metronome.");
            Assert.IsNull(p.GetComponentInChildren<SentryBurst>(true));
            Assert.IsNull(p.GetComponentInChildren<ProjectileVolleySequence>(true));
            Assert.IsNull(p.GetComponentInChildren<ParrySurge>(true));
            Assert.IsNull(p.GetComponentInChildren<EmberAura>(true),
                "SeraphLancerVisuals is the aura's only writer; an EmberAura would be a second one.");
            Assert.IsNull(p.GetComponentInChildren<EnemyWeaponTrail>(true));
            Assert.IsNull(p.GetComponentInChildren<CinderJudgeStorm>(true));
            Assert.IsNull(p.GetComponentInChildren<OrbitDancerDiscs>(true));

            var launcher = p.GetComponent<SeraphLancerJavelins>();
            Assert.IsNotNull(launcher, "the launcher lives on the root beside the brain.");
            Assert.AreEqual(SeraphLancerAuthoring.SkyVerdictAttackName, launcher.skyVerdictAttack);
            Assert.AreEqual(3, launcher.javelinsPerVerdict);
            Assert.AreEqual(1.10f, launcher.javelinCadence, 0.001f);
            Assert.AreEqual(4, launcher.liveCap);
            Assert.AreEqual(5f, launcher.javelinLifetime, 0.001f);
            Assert.AreEqual(1.6f, launcher.javelinLength, 0.001f);
            Assert.AreEqual(0.05f, launcher.shaftDiameter, 0.001f);
            Assert.AreEqual(0.30f, launcher.tipSize, 0.001f);
            Assert.AreEqual(0.12f, launcher.launchMargin, 0.001f);
            Assert.AreEqual("RightHand", launcher.muzzleBone);
            Assert.AreEqual(1.6f, launcher.muzzleFallbackHeight, 0.001f);
            Assert.AreEqual(1.5f, launcher.relicSeconds, 0.001f);
            Assert.AreEqual(0.7f, launcher.relicHoldFraction, 0.001f);
            Assert.AreEqual(0.5f, launcher.stuckVolume, 0.001f);
            Assert.AreEqual(24f, launcher.stuckRange, 0.001f);
            Assert.Less(launcher.tipColor.maxColorComponent, Projectile.HotCorePeak,
                "the javelin tip may never out-bloom the sentry bolt, the brightest thing in the game.");
            Assert.Greater(launcher.tipColor.maxColorComponent, 1.25f, "but it is louder than the Dancer's disc rim: the first lesson is the louder one.");
            Assert.Greater(launcher.tipColor.maxColorComponent, 1.05f);
            Assert.Less(launcher.shaftColor.maxColorComponent, 1f, "the shaft is lit metal, never a glow");
            // The muzzle bone exists on the rig, so the javelin really leaves the hand.
            bool hand = false;
            foreach (var t in p.GetComponentsInChildren<Transform>(true)) if (t.name == launcher.muzzleBone) hand = true;
            Assert.IsTrue(hand, "no '" + launcher.muzzleBone + "' bone on the rig");

            var v = p.GetComponentInChildren<SeraphLancerVisuals>(true);
            Assert.IsNotNull(v);
            Assert.AreEqual("Idle", v.clipIdle);
            Assert.AreEqual("HeavyAttack", v.clipHeavy);
            Assert.AreEqual("Jump", v.jumpClip);
            Assert.AreEqual("HoverHold", v.hoverClip);
            Assert.AreEqual("JavelinThrow", v.throwClip);
            Assert.AreEqual("ShoulderCharge", v.shoulderChargeClip);
            Assert.AreEqual(SeraphLancerAuthoring.SkyVerdictAttackName, v.skyVerdictAttack);
            Assert.AreEqual("SeraphLancer_Heavy", v.heavyAttack);
            Assert.AreEqual("SeraphLancer_ShoulderCharge", v.shoulderChargeAttack);
            Assert.AreEqual(0.35f, v.hitHoldSeconds, 0.001f, "the fluidity pass's cap on a held pose");
            Assert.AreEqual(3.0f, v.hoverHeight, 0.001f);
            Assert.AreEqual(0.45f, v.hoverDescendSeconds, 0.001f);
            Assert.AreEqual(0.22f, v.hoverFallSeconds, 0.001f);
            Assert.AreEqual(120f, v.hoverTrackDegPerSec, 0.001f);
            Assert.AreEqual(0.28f, v.jumpTakeoffNormalized, 0.001f);
            Assert.AreEqual(0.56f, v.jumpApexNormalized, 0.001f);
            Assert.AreEqual(0.85f, v.jumpLandNormalized, 0.001f);
            Assert.AreEqual(0.25f, v.throwFollowThroughSeconds, 0.001f);
            Assert.AreEqual(0.8f, v.diveHopHeight, 0.001f);
            Assert.Less(v.diveHopHeight, v.hoverHeight * 0.5f, "a hop, unmistakably not the rise");
            Assert.AreEqual(3, v.feathersPerWing);
            Assert.AreEqual(1.6f, v.wingLength, 0.001f);
            Assert.AreEqual(0.35f, v.wingUnfoldSeconds, 0.001f);
            Assert.AreEqual(0.25f, v.wingFoldSeconds, 0.001f);
            Assert.AreEqual(0.55f, v.wingAlpha, 0.001f);
            Assert.LessOrEqual(SlashFx.NormaliseColor(v.wingHue).maxColorComponent, 1f, "the wings are normalised under the bloom threshold");
            Assert.IsTrue(string.IsNullOrEmpty(v.spinAttackPrefix));
            Assert.IsNotNull(v.travelRoot);
            Assert.IsFalse(v.animator.applyRootMotion);
            Assert.LessOrEqual(v.hoverGlow, 0.60f, "EmberAura's documented ceiling before the parry read suffers.");
            Assert.LessOrEqual(v.glowAtBreak, v.hoverGlow);
            Assert.LessOrEqual(v.verdigrisHot.maxColorComponent, 1.0f, "the aura colour is under the bloom threshold before SetAura scales it down further");
            Assert.Greater(v.footstepDust.a, 0f, "footfall dust, like the other forge bodies of the fluidity pass");

            // HoverRoot between LungeRoot and SpinRoot (the StormRoot pattern); WingRoot under it.
            Assert.IsNotNull(v.hoverRoot, "4b did not insert HoverRoot.");
            Assert.AreEqual("HoverRoot", v.hoverRoot.name);
            Assert.AreSame(v.lungeRoot, v.hoverRoot.parent);
            Assert.AreSame(v.hoverRoot, v.spinRoot.parent);
            Assert.AreNotSame(v.spinRoot, v.hoverRoot);
            Assert.IsNotNull(v.wingRoot, "4b did not build WingRoot.");
            Assert.AreEqual("WingRoot", v.wingRoot.name);
            Assert.AreSame(v.hoverRoot, v.wingRoot.parent);
            Assert.AreEqual(0, v.wingRoot.childCount, "the feathers are runtime meshes built at Setup, not prefab content");
            Assert.AreEqual(1.42f, v.wingRoot.localPosition.y, 0.001f);

            // The throw bakes its contact: JavelinThrow is a generated ground clip, so 4b MEASURES its
            // anchor on the imported clip (the furthest reach of the hand) or keeps the manifest's 0.696;
            // either way it must sit in the throw's release, past the halfway arm-back.
            int throwClip = v.IndexOfNamedClip("JavelinThrow");
            Assert.GreaterOrEqual(throwClip, 0, "4b did not bake JavelinThrow.");
            Assert.That(v.namedClipHits[throwClip], Is.InRange(0.5f, 0.85f));
            Assert.Less(v.IndexOfNamedClip("HoverHold"), 0, "HoverHold has no contact and is named by no attack");
            foreach (var clipName in new[] { "Jab2", "ShoulderCharge", "HeavyAttack", "ComboFinisher", "AttackSwing", "AttackStab", "AttackKick" })
                Assert.GreaterOrEqual(v.IndexOfNamedClip(clipName), 0, clipName + " has no baked contact.");
        }

        // ------------------------------------------------------------------ sandbox

        [Test]
        public void SandboxPad_HasRoomForTheChargeTheLanceAndTheHover_AndPullsNoNeighbour()
        {
            Vector3 p = SandboxBuilder.SeraphLancerPadPosition;
            Vector3 v18 = SandboxBuilder.FlurryBrawlerV18PadPosition;
            Vector3 judge = SandboxBuilder.CinderJudgePadPosition;
            Vector3 dancer = SandboxBuilder.OrbitDancerPadPosition;
            const float halfWidth = 2.75f;
            var stab = Atk("SeraphLancer_Stab");
            float reach = Mathf.Max(halfWidth, stab.range + 0.5f);
            // Footprint: the lance around the pad, plus the NORTH charge lane up to the wake switch,
            // expanded by a player capsule.
            float minX = p.x - reach, maxX = p.x + reach;
            float minZ = p.z - reach, maxZ = p.z + SandboxBuilder.SeraphLancerWakeOffset + 0.9f;
            Assert.GreaterOrEqual(minX, SandboxBuilder.YardMinX);
            Assert.LessOrEqual(maxX, SandboxBuilder.YardMaxX);
            Assert.GreaterOrEqual(minZ, -SandboxBuilder.YardHalfZ);
            Assert.LessOrEqual(maxZ, SandboxBuilder.YardHalfZ);
            // South of the doorway line, so the doorway still looks down open floor.
            Assert.Less(maxZ, -SandboxBuilder.YardDoorHalfWidth);
            SandboxBuilder.YardBox? runway = null;
            foreach (var b in SandboxBuilder.YardLayout())
            {
                if (b.kind == SandboxBuilder.YardKind.Floor ||
                    b.kind == SandboxBuilder.YardKind.Stripe) continue;
                if (b.kind == SandboxBuilder.YardKind.Runway) runway = b;
                bool overlap = minX < b.MaxX && maxX > b.MinX && minZ < b.MaxZ && maxZ > b.MinZ;
                Assert.IsFalse(overlap, "Seraph Lancer footprint overlaps " + b.name);
                // Never in a tower tier's drop zone (MovementYardTests' contract, applied to the pad).
                if (b.kind == SandboxBuilder.YardKind.Tier)
                    Assert.IsFalse(minX >= b.MaxX - 0.001f && maxZ > b.MinZ && minZ < b.MaxZ,
                        "the pad stands in " + b.name + "'s drop zone");
                // Nothing TALL inside the hover column: he rises 3 m plus a body above the pad. A 1 m
                // kerb is not in the column; a 2 m ladder pad, a step, a tier, a wall or the runway is.
                if (b.kind != SandboxBuilder.YardKind.Water && b.kind != SandboxBuilder.YardKind.Balloon && b.Top > 1.5f)
                    Assert.IsFalse(minX - 2f < b.MaxX && maxX + 2f > b.MinX && p.z - reach - 2f < b.MaxZ && p.z + reach + 2f > b.MinZ,
                        b.name + " stands inside the hover's clearance");
            }
            // The runway's 30 m run-off (MovementYardTests) stays clear: the footprint is outside its z band.
            Assert.IsTrue(runway.HasValue);
            Assert.IsTrue(maxZ < runway.Value.MinZ || minZ > runway.Value.MaxZ || minX > runway.Value.MaxX + 30f,
                "the pad sits inside the runway's 30 m run-off");

            // No standalone neighbour can be pulled into his fight: pad centres further apart than
            // every aggro range (the shipped data, not a literal).
            foreach (var other in new[] { v18, judge, dancer })
            {
                float apart = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(other.x, 0f, other.z));
                Assert.Greater(apart, Data().aggroRange);
            }
            if (V18() != null) Assert.Greater(Vector3.Distance(p, v18), V18().aggroRange);
            if (Judge() != null) Assert.Greater(Vector3.Distance(p, judge), Judge().aggroRange);
            if (Dancer() != null) Assert.Greater(Vector3.Distance(p, dancer), Dancer().aggroRange);
            // And the yard spawn cannot wake him.
            Assert.Greater(Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(SandboxBuilder.YardSpawnPos.x, 0f, SandboxBuilder.YardSpawnPos.z)),
                Data().aggroRange);

            // The wake switch stands outside the charge reach and the lance.
            var shoulder = Atk("SeraphLancer_ShoulderCharge");
            float chargeReach = shoulder.lungeDistance + shoulder.range + 0.5f;
            Assert.Greater(SandboxBuilder.SeraphLancerWakeOffset, chargeReach);
            Assert.Greater(SandboxBuilder.SeraphLancerWakeOffset, stab.range + 0.5f + 0.9f);
            // The verdict's band north of the pad is open floor: the far edge is still on the yard.
            var entry = VerdictEntry();
            Assert.LessOrEqual(p.z + entry.maxRange, SandboxBuilder.YardHalfZ);

            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity", OpenSceneMode.Additive);
            try
            {
                GameObject spawn = null, wake = null, dancerSpawn = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "Spawn_Legendary_SeraphLancer") spawn = t.gameObject;
                        else if (t.name == "Wake_Legendary_SeraphLancer") wake = t.gameObject;
                        else if (t.name == "Spawn_Legendary_OrbitDancer") dancerSpawn = t.gameObject;
                    }
                }

                Assert.IsNotNull(spawn, "run VibeGame1/7. Build Sandbox Scene");
                Assert.IsNotNull(spawn.GetComponent<EnemySpawner>());
                Assert.AreEqual(0f, Vector3.Angle(spawn.transform.forward, Vector3.forward), 0.5f, "he faces NORTH: the lane and the switch are north of the pad");
                Assert.IsNotNull(wake, "Seraph Lancer wake switch missing from the rebuilt Sandbox scene.");
                Assert.IsNotNull(wake.GetComponent<SandboxEnemySwitch>());
                Assert.Greater(wake.transform.position.z, spawn.transform.position.z, "the switch is north of the spawner");
                Assert.IsNotNull(dancerSpawn, "the Orbit Dancer fixture was removed.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void IsTheT1RealmBoss()
        {
            // Boss roster seated 2026-09-13 (LevelDefinitionAuthoring.SeatBossRoster): the T1 realm's
            // spawner name stays the contract, the occupant is this body.
            var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Data/Levels/Level_01_Level.asset");
            Assert.IsNotNull(level);
            SpawnDef seat = null;
            foreach (var s in level.spawns) if (s != null && s.name == "Spawn_Legendary_Ninja") seat = s;
            Assert.IsNotNull(seat, "Spawn_Legendary_Ninja missing from Level_01");
            Assert.AreEqual("Legendary_SeraphLancer", seat.prefabKey);
        }

        // ------------------------------------------------------------------ helpers

        static void AssertAttack(string name, string clip, float windup, float delay,
                                 float strike, float recovery, float lunge)
        {
            var a = Atk(name);
            Assert.IsNotNull(a, name + " missing; run VibeGame1/3. Create Data");
            Assert.AreEqual(clip, a.clip);
            Assert.AreEqual(windup, a.windup, 0.001f, name);
            Assert.AreEqual(delay, a.impactDelay, 0.001f, name);
            Assert.AreEqual(strike, a.strikeDuration, 0.001f, name);
            Assert.AreEqual(recovery, a.recovery, 0.001f, name);
            Assert.AreEqual(lunge, a.lungeDistance, 0.001f, name);
        }

        static float SampledHipsForwardTravel(AnimationClip clip)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                Transform hips = null;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Hips") hips = t;
                Assert.IsNotNull(hips, "no Hips bone in " + Fbx);
                clip.SampleAnimation(go, 0f);
                Vector3 a = go.transform.InverseTransformPoint(hips.position);
                clip.SampleAnimation(go, clip.length);
                Vector3 b = go.transform.InverseTransformPoint(hips.position);
                return b.z - a.z;
            }
            finally { Object.DestroyImmediate(go); }
        }

        static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] bytes = sha.ComputeHash(stream);
                return System.BitConverter.ToString(bytes).Replace("-", "");
            }
        }
    }
}
