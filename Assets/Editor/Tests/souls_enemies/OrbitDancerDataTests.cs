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
    /// Pins the additive sandbox Orbit Dancer, built the V18 way. Every number below is read off the
    /// shipped ASSET or prefab (rule 9), never off a field initialiser; the discs' arithmetic -- the one
    /// thing this body adds to the roster -- is held against the player's own bars and against hers.
    /// </summary>
    public class OrbitDancerDataTests
    {
        /// <summary>A fist clip that does not travel may still ship a short cue-bound step so the blow reaches
        /// (Fable spatial spec, 2026-09-14). Anything longer must be the art's own travel.</summary>
        const float CueStepAllowance = 0.6f;

        const string Fbx = "Assets/Enemies/OrbitDancer.fbx";
        const string Manifest = "Assets/Enemies/OrbitDancer.clips.json";
        const string Provenance = "Assets/Enemies/OrbitDancer.provenance.txt";
        const string PrefabPath = "Assets/Prefabs/Legendary_OrbitDancer.prefab";
        const float CueLead = 0.28f;              // EnemyController.cueLead == Projectile.CueLead
        const float PlayerPostureMax = 100f;      // PlayerPosture.Max default; ComputeMax adds stat upgrades on top

        static readonly string[] AttackNames =
        {
            "OrbitDancer_Jab2", "OrbitDancer_Swing", "OrbitDancer_ComboFinisher", "OrbitDancer_Stab",
            "OrbitDancer_Kick", "OrbitDancer_Heavy", "OrbitDancer_ShoulderCharge",
            OrbitDancerAuthoring.DiscThrowAttackName, OrbitDancerAuthoring.SpinThrowAttackName,
            "OrbitDancer_HeavyHeld",
        };

        static EnemyData Data() => AssetDatabase.LoadAssetAtPath<EnemyData>(
            EnemyPaths.Data(OrbitDancerAuthoring.EnemyName));

        static EnemyData Judge() => AssetDatabase.LoadAssetAtPath<EnemyData>(
            EnemyPaths.Data(CinderJudgeAuthoring.EnemyName));

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
        public void SourceArt_IsTheApprovedOrbitDancerExport()
        {
            Assert.AreEqual(OrbitDancerAuthoring.SourceFbxSha256, Sha256(Fbx));
            Assert.IsTrue(File.Exists(Manifest));
            string prov = File.ReadAllText(Provenance);
            StringAssert.Contains(OrbitDancerAuthoring.SourceFbxSha256, prov);
            StringAssert.Contains(OrbitDancerAuthoring.SourceManifestSha256, prov);
        }

        [Test]
        public void Manifest_IsExactlyTheApprovedSeventeenStates()
        {
            CollectionAssert.AreEqual(OrbitDancerAuthoring.ClipAllowlist, ForgeClipSplitter.ClipNames(Fbx));
            // V18's base kit, the two reactions PuppetVisuals requires (Hit, Death), and the two throws
            // generated for this body. Nothing from the larger source export -- no IdleCombat,
            // AttackOverhead, Block or Spawn -- may creep in with a re-export.
            Assert.AreEqual(17, OrbitDancerAuthoring.ClipAllowlist.Length);
            CollectionAssert.Contains(OrbitDancerAuthoring.ClipAllowlist, "DiscThrow");
            CollectionAssert.Contains(OrbitDancerAuthoring.ClipAllowlist, "SpinThrow");
            CollectionAssert.DoesNotContain(OrbitDancerAuthoring.ClipAllowlist, "Block");
            CollectionAssert.DoesNotContain(OrbitDancerAuthoring.ClipAllowlist, "IdleCombat");

            // Both throws carry the source's own release frame, so 4b bakes the manifest and no explicit
            // profile is needed (unlike the Judge's Roar).
            var withHit = ForgeClipSplitter.ClipsWithEvent(Fbx, "OnAttackHit");
            CollectionAssert.Contains(withHit, "DiscThrow");
            CollectionAssert.Contains(withHit, "SpinThrow");
            Assert.AreEqual(0.478f, ForgeClipSplitter.ReadEventNormalizedTime(Fbx, "DiscThrow", 0f, "OnAttackHit"), 0.001f);
            Assert.AreEqual(0.474f, ForgeClipSplitter.ReadEventNormalizedTime(Fbx, "SpinThrow", 0f, "OnAttackHit"), 0.001f);
            float unused;
            foreach (var clip in OrbitDancerAuthoring.ClipAllowlist)
                Assert.IsFalse(OrbitDancerAuthoring.TryExplicitContact(OrbitDancerAuthoring.EnemyName, clip, out unused),
                    clip + ": the Dancer has no explicit contact profile; every named clip ships its own OnAttackHit.");
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
            CollectionAssert.AreEquivalent(OrbitDancerAuthoring.ClipAllowlist, importedNames);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                PuppetAnimatorFactory.ControllerDir + "/Legendary_OrbitDancer_Animator.controller");
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
            CollectionAssert.AreEquivalent(OrbitDancerAuthoring.ClipAllowlist, stateNames);
        }

        // ------------------------------------------------------------------ data

        [Test]
        public void Data_IsSeparate_LighterAndFasterThanTheJudge()
        {
            var d = Data();
            Assert.IsNotNull(d, "run VibeGame1/3. Create Data");
            Assert.AreEqual("THE ORBIT DANCER", d.displayName);
            Assert.AreEqual(170f, d.maxHP, 0.001f);
            Assert.AreEqual(150f, d.maxPosture, 0.001f);
            Assert.AreEqual(6f, d.postureRegen, 0.001f);
            Assert.AreEqual(3.0f, d.staggerSeconds, 0.001f);
            Assert.AreEqual(5.4f, d.moveSpeed, 0.001f);
            Assert.AreEqual(2.2f, d.preferredRange, 0.001f);
            Assert.AreEqual(18f, d.aggroRange, 0.001f);
            Assert.AreEqual(0.85f, d.aggression, 0.001f);
            Assert.AreEqual(0.55f, d.strafeSpeedMultiplier, 0.001f, "she circles: the walls are hers.");
            Assert.AreEqual(1f, d.scale, 0.001f);
            Assert.AreEqual(0.7f, d.flaskPunishChance, 0.001f);
            // A SOULS duel with a projectile it throws itself: never a sentry.
            Assert.IsFalse(d.shootsProjectiles, "no ProjectileShooter, no metronome, no perch wake");
            Assert.IsFalse(d.rangedOnly);
            Assert.AreNotSame(d, V18());
            Assert.AreNotSame(d, Judge());

            // The spice, held: the glass cannon of the three, and the fastest on the floor.
            var judge = Judge();
            if (judge != null)
            {
                Assert.Less(d.maxHP, judge.maxHP);
                Assert.Less(d.maxPosture, judge.maxPosture);
                Assert.Greater(d.moveSpeed, judge.moveSpeed);
                Assert.Less(d.preferredRange, judge.preferredRange);
                Assert.Greater(d.strafeSpeedMultiplier, judge.strafeSpeedMultiplier);
                // Every shared blow winds up SHORTER than the Judge's same blow: her tempo is the spice.
                foreach (var suffix in new[] { "Jab2", "Swing", "ComboFinisher", "Stab", "Kick", "Heavy", "ShoulderCharge" })
                {
                    var hers = Atk("OrbitDancer_" + suffix);
                    var his = Atk("CinderJudge_" + suffix);
                    if (hers == null || his == null) continue;
                    Assert.Less(hers.windup, his.windup, suffix + ": the Dancer must be quicker than the Judge.");
                    Assert.LessOrEqual(hers.damage, his.damage, suffix + ": and never hit harder.");
                }
            }
            var v18 = V18();
            if (v18 != null)
            {
                Assert.Less(d.maxHP, v18.maxHP);
                Assert.GreaterOrEqual(d.moveSpeed, v18.moveSpeed);
                Assert.Greater(d.strafeSpeedMultiplier, v18.strafeSpeedMultiplier);
            }

            var names = new HashSet<string>();
            foreach (var h in EveryHit()) names.Add(h.name);
            CollectionAssert.AreEquivalent(AttackNames, names);
            CollectionAssert.DoesNotContain(names, OrbitDancerAuthoring.DiscAttackName,
                "the disc's attack is carried by the disc, never scheduled by the brain.");
        }

        [Test]
        public void AttackTimingsAndTravel_AreTheApprovedProfile()
        {
            AssertAttack("OrbitDancer_Jab2", "Jab2", 0.45f, 0.05f, 0.14f, 0.55f, 0.5f);
            AssertAttack("OrbitDancer_Swing", "AttackSwing", 0.50f, 0.05f, 0.18f, 0.65f, 0.5f);
            AssertAttack("OrbitDancer_ComboFinisher", "ComboFinisher", 0.60f, 0.06f, 0.18f, 1.10f, 0f);
            AssertAttack("OrbitDancer_Stab", "AttackStab", 0.50f, 0.05f, 0.14f, 0.60f, 0.5f);
            AssertAttack("OrbitDancer_Kick", "AttackKick", 0.55f, 0.05f, 0.20f, 0.80f, 0.5f);
            AssertAttack("OrbitDancer_Heavy", "HeavyAttack", 0.95f, 0.08f, 0.28f, 1.30f, 0.75f);
            AssertAttack("OrbitDancer_ShoulderCharge", "ShoulderCharge", 0.90f, 0.08f, 0.26f, 1.00f, 3.32f);
            AssertAttack(OrbitDancerAuthoring.DiscThrowAttackName, "DiscThrow", 0.70f, 0.06f, 0.20f, 0.90f, 0f);
            AssertAttack(OrbitDancerAuthoring.SpinThrowAttackName, "SpinThrow", 0.60f, 0.05f, 0.22f, 0.95f, 0f);

            Assert.IsTrue(Atk("OrbitDancer_Kick").unblockable);
            Assert.IsTrue(Atk("OrbitDancer_ShoulderCharge").unblockable);
            Assert.IsFalse(Atk("OrbitDancer_Heavy").unblockable, "the heavy is the big parry payoff, not a red.");
            Assert.AreEqual(1.9f, Atk("OrbitDancer_Heavy").parryPostureMultiplier, 0.001f);
            Assert.IsFalse(Atk(OrbitDancerAuthoring.SpinThrowAttackName).unblockable, "the whirl is blue: parry it, then turn.");
            Assert.IsFalse(Atk(OrbitDancerAuthoring.DiscThrowAttackName).unblockable);

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
            // (the Heavy's 1.14 m to the right), backward steps (Jab2, DiscThrow's 0.46 m back) have no
            // lunge channel. The Judge's Unity number (3.32 on ShoulderCharge, Blender 3.12) is shipped
            // on the same array; if THIS import reads differently the message below says what to ship.
            foreach (var h in EveryHit())
            {
                var clip = ClipNamed(h.clip);
                if (clip == null) continue;   // reported by the import test
                if (!ForgeClipSplitter.ClipIsGenerated(Fbx, h.clip)) continue;
                float forward = SampledHipsForwardTravel(clip);
                if (ForgeClipSplitter.ClipTravels(Fbx, h.clip))
                {
                    Assert.That(h.lungeDistance - Mathf.Max(0f, forward), Is.InRange(-0.15f, 0.15f + (forward < 0.15f ? CueStepAllowance : 0f)),
                        h.name + ": lungeDistance " + h.lungeDistance + " but '" + h.clip + "' walks the Hips " +
                        forward.ToString("F2") + " m forward in Unity. Ship " + Mathf.Max(0f, forward).ToString("F2") + ".");
                }
                else
                {
                    Assert.That(h.lungeDistance, Is.InRange(0f, CueStepAllowance),
                        h.name + ": '" + h.clip + "' does not travel in the art, so the enemy must not either.");
                }
            }
        }

        // ------------------------------------------------------------------ the discs

        [Test]
        public void Discs_AreProjectilesCarryingTheDiscAttack_OnTheDancersOwnNumbers()
        {
            var d = Data();
            Assert.IsNotNull(d, "run VibeGame1/3. Create Data");
            var disc = Atk(OrbitDancerAuthoring.DiscAttackName);
            Assert.IsNotNull(disc);
            Assert.AreSame(disc, d.projectileAttack, "a BouncingDisc arrives with EnemyData.projectileAttack.");
            Assert.AreEqual(14f, disc.damage, 0.001f, "a jab's worth per disc (12 -> 14, 2026-09-14)");
            Assert.IsFalse(disc.unblockable, "a disc is Block or Perfect, never a red.");
            Assert.AreEqual(1.2f, disc.parryPostureMultiplier, 0.001f);
            Assert.AreEqual(16f, d.projectileSpeed, 0.001f, "half a sentry bolt, for a duel inside 3-12 m");
            Assert.AreEqual(0f, d.projectileHomingDegPerSec, 0.001f, "straight lines only: a bounce is geometry you can read.");
            Assert.AreEqual(0.6f, d.projectileLead, 0.001f);
            Assert.AreEqual(20f, d.parriedProjectileDamage, 0.001f);
            Assert.AreEqual(26f, d.parriedProjectilePosture, 0.001f);
            Assert.AreEqual(2.5f, d.parrySpeedGain, 0.001f, "a nudge, not the sentries' span boost");
            Assert.AreEqual(0.6f, d.projectileTrailScale, 0.001f);
            Assert.AreEqual(0.7f, d.projectileCueScale, 0.001f);
            Assert.AreEqual(0, d.perfectBurstParriesToDestroy);
        }

        [Test]
        public void Throws_AreOneScheduleEach_TheVolleyLandsNothingItself_TheWhirlIsAFullCircle()
        {
            var volley = Atk(OrbitDancerAuthoring.DiscThrowAttackName);
            Assert.IsNotNull(volley, "run VibeGame1/3. Create Data");
            // The brain's DoImpact can never land the volley: the discs ARE the attack.
            Assert.AreEqual(0f, volley.range, 0.001f);
            Assert.AreEqual(0f, volley.coneDeg, 0.001f);
            Assert.AreEqual(0f, volley.damage, 0.001f);
            Assert.IsFalse(volley.windupPose.authored, "the clip carries the silhouette; nothing is posed on LungeRoot.");

            var whirl = Atk(OrbitDancerAuthoring.SpinThrowAttackName);
            Assert.AreEqual(360f, whirl.coneDeg, 0.001f, "she turns a full circle with the arm out: behind her is not an answer.");
            Assert.AreEqual(2.6f, whirl.range, 0.001f);
            Assert.AreEqual(18f, whirl.damage, 0.001f);
            Assert.Greater(whirl.range + 0.5f, Data().preferredRange, "the whirl must reach from her preferred stand.");

            MovesetEntry volleyEntry = null, whirlEntry = null, chain = null;
            int volleyUses = 0;
            foreach (var e in Data().moveset.entries)
            {
                if (e.combo.hits.Length == 1 && e.combo.hits[0] == volley) { Assert.IsNull(volleyEntry); volleyEntry = e; }
                if (e.combo.hits.Length == 1 && e.combo.hits[0] == whirl) { Assert.IsNull(whirlEntry); whirlEntry = e; }
                if (e.combo.hits.Length == 2 && e.combo.hits[1] == whirl) { Assert.IsNull(chain); chain = e; }
                foreach (var hit in e.combo.hits) if (hit == volley) volleyUses++;
            }
            Assert.AreEqual(1, volleyUses, "the volley is one standalone EnemyController schedule, never inside a string.");
            Assert.IsNotNull(volleyEntry, "the volley entry");
            Assert.AreEqual(2.6f, volleyEntry.weight, 0.001f);
            Assert.AreEqual(4.5f, volleyEntry.minRange, 0.001f);
            Assert.AreEqual(12f, volleyEntry.maxRange, 0.001f);
            Assert.AreEqual(4f, volleyEntry.cooldown, 0.001f);
            Assert.LessOrEqual(volleyEntry.maxRange, Data().aggroRange, "a far band beyond aggro can never fire.");
            // The nearest direct throw at full speed is exactly one cue lead of flight; LaunchSpeed slows
            // it further, so no disc ever arrives inside its own cue.
            Assert.GreaterOrEqual(volleyEntry.minRange / Data().projectileSpeed, CueLead - 0.001f);

            Assert.IsNotNull(whirlEntry, "the standalone whirl");
            Assert.AreEqual(0f, whirlEntry.minRange, 0.001f);
            Assert.AreEqual(3.2f, whirlEntry.maxRange, 0.001f);
            Assert.AreEqual(5f, whirlEntry.cooldown, 0.001f);

            Assert.IsNotNull(chain, "jab-two into SPIN THROW replaces the Judge's red chain");
            Assert.AreSame(Atk("OrbitDancer_Jab2"), chain.combo.hits[0]);
            Assert.AreEqual(7f, chain.cooldown, 0.001f);
        }

        [Test]
        public void Discs_WorstCase_HurtsButNeverEndsEitherFighterByItself()
        {
            var d = Data();
            var disc = Atk(OrbitDancerAuthoring.DiscAttackName);
            var launcher = Prefab().GetComponent<OrbitDancerDiscs>();
            Assert.IsNotNull(launcher, "run VibeGame1/4b. Build Mini-Bosses");

            // A whole volley unguarded: three jabs' worth, never a bar, never a posture break by itself.
            var stats = Stats();
            float hitPostureMult = stats != null ? stats.hitPostureMultiplier : 0.5f;
            float worstHealth = launcher.volleyCount * disc.damage;
            float worstPosture = launcher.volleyCount * disc.damage * hitPostureMult;
            Assert.AreEqual(42f, worstHealth, 0.001f);
            Assert.Less(worstHealth, 100f, "a volley must be survivable from full health");
            Assert.Greater(worstHealth, 25f, "and must be worth answering");
            Assert.Less(worstPosture, PlayerPostureMax);

            // A whole volley Perfect-reflected into her: half of both bars, never all of either.
            float reflectHealth = launcher.volleyCount * d.parriedProjectileDamage;
            float reflectPosture = launcher.volleyCount * d.parriedProjectilePosture;
            Assert.AreEqual(60f, reflectHealth, 0.001f);
            Assert.AreEqual(78f, reflectPosture, 0.001f);
            Assert.Less(reflectHealth, d.maxHP, "one volley reflected must not kill her outright");
            Assert.Less(reflectPosture, d.maxPosture, "nor break her outright");
            Assert.GreaterOrEqual(reflectHealth, d.maxHP * 0.25f, "but the reflect must be her worst idea");
            Assert.GreaterOrEqual(reflectPosture, d.maxPosture * 0.25f);

            // The cap admits a whole volley, and a disc never outlives the volley's cooldown.
            Assert.GreaterOrEqual(launcher.liveCap, launcher.volleyCount);
            Assert.GreaterOrEqual(launcher.liveCap, launcher.whirlCount);
            MovesetEntry volleyEntry = null;
            foreach (var e in d.moveset.entries)
                if (e.combo.hits.Length == 1 && e.combo.hits[0].name == OrbitDancerAuthoring.DiscThrowAttackName) volleyEntry = e;
            Assert.LessOrEqual(launcher.discLifetime, volleyEntry.cooldown, "discs from one volley are gone before the next.");
            // Three bounces at the data speed inside the lifetime cross a 30 m realm and back.
            Assert.GreaterOrEqual(launcher.discLifetime * d.projectileSpeed, 60f);
        }

        // ------------------------------------------------------------------ the ricochet maths

        [Test]
        public void Ricochet_MirrorsAcrossTheNormal_AndAlwaysLeavesTheSurface()
        {
            Vector3 wall = new Vector3(-1f, 0f, 0f);
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(-1f, 0f, 0f), BouncingDisc.Ricochet(new Vector3(1f, 0f, 0f), wall)), 0.0001f);
            Vector3 angled = BouncingDisc.Ricochet(new Vector3(1f, 0f, 1f).normalized, wall);
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(-1f, 0f, 1f).normalized, angled), 0.0001f);
            Assert.AreEqual(1f, angled.magnitude, 0.0001f);
            // A graze that reflects to a line along the wall is pushed off it.
            Vector3 graze = BouncingDisc.Ricochet(new Vector3(0f, 0f, 1f), wall);
            Assert.Greater(Vector3.Dot(graze, wall), 0.04f, "never re-hits the wall it just left");
            Assert.AreEqual(1f, graze.magnitude, 0.0001f);
        }

        [TestCase(0, 3, true)]
        [TestCase(2, 3, true)]
        [TestCase(3, 3, false)]
        [TestCase(4, 3, false)]
        [TestCase(0, 0, false)]
        public void CanBounce_IsStrictlyUnderTheCap(int soFar, int cap, bool expected)
        {
            Assert.AreEqual(expected, BouncingDisc.CanBounce(soFar, cap));
        }

        [TestCase(3, 0, 4, 3)]
        [TestCase(3, 1, 4, 3)]
        [TestCase(3, 2, 4, 2)]
        [TestCase(3, 4, 4, 0)]
        [TestCase(3, 9, 4, 0)]
        [TestCase(2, 3, 4, 1)]
        [TestCase(0, 0, 4, 0)]
        public void LaunchCount_FillsOnlyTheRoomUnderTheGlobalCap(int wanted, int live, int cap, int expected)
        {
            Assert.AreEqual(expected, OrbitDancerDiscs.LaunchCount(wanted, live, cap));
        }

        [Test]
        public void BankShot_AimsAtTheMirrorImageAcrossAWall_AndOnlyAWall()
        {
            Vector3 chest = new Vector3(5f, 1.2f, 0f);
            Vector3 mirror = OrbitDancerDiscs.MirrorAcrossPlane(chest, new Vector3(8f, 0f, 3f), new Vector3(-1f, 0f, 0f));
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(11f, 1.2f, 0f), mirror), 0.0001f);
            // Un-normalised and flipped normals mirror the same plane.
            Assert.AreEqual(0f, Vector3.Distance(mirror,
                OrbitDancerDiscs.MirrorAcrossPlane(chest, new Vector3(8f, 5f, -2f), new Vector3(3f, 0f, 0f))), 0.0001f);

            Assert.IsTrue(OrbitDancerDiscs.IsBankable(new Vector3(-1f, 0f, 0f), 0.35f));
            Assert.IsTrue(OrbitDancerDiscs.IsBankable(new Vector3(0.7f, 0.2f, 0.7f), 0.35f));
            Assert.IsFalse(OrbitDancerDiscs.IsBankable(Vector3.up, 0.35f), "a floor is never a bank");
            Assert.IsFalse(OrbitDancerDiscs.IsBankable(Vector3.down, 0.35f), "nor a ceiling");
            Assert.IsFalse(OrbitDancerDiscs.IsBankable(Vector3.zero, 0.35f));
        }

        [Test]
        public void Fan_FallsBackToAlternatingSides_CentreFirst()
        {
            Vector3 to = new Vector3(0f, -0.05f, 1f);
            Assert.AreEqual(0f, Vector3.Angle(to, OrbitDancerDiscs.FanDirection(to, 0, 30f)), 0.001f);
            Vector3 right = OrbitDancerDiscs.FanDirection(to, 1, 30f);
            Vector3 left = OrbitDancerDiscs.FanDirection(to, 2, 30f);
            // The fan is a YAW about world up; on an aim pitched slightly down the 3D angle reads a hair
            // under the yaw (29.96 for 30), so the tolerance is 0.1 deg, not 0.01.
            Assert.AreEqual(30f, Vector3.Angle(to, right), 0.1f);
            Assert.AreEqual(30f, Vector3.Angle(to, left), 0.1f);
            Assert.Greater(right.x, 0f); Assert.Less(left.x, 0f);
            Assert.AreEqual(60f, Vector3.Angle(to, OrbitDancerDiscs.FanDirection(to, 3, 30f)), 0.1f);
            Assert.AreEqual(1f, right.magnitude, 0.0001f);
        }

        [TestCase(4.5f, 16f, 0.40f, 11.25f)]   // the volley's nearest band edge: slowed to lead + margin
        [TestCase(6.4f, 16f, 0.40f, 16f)]      // from here on the data speed ships
        [TestCase(12f, 16f, 0.75f, 16f)]
        public void PathSpeed_NeverArrivesInsideTheCueLeadPlusMargin(float path, float speed, float flight, float expected)
        {
            float launch = OrbitDancerDiscs.PathSpeed(path, speed, CueLead, 0.12f);
            Assert.AreEqual(expected, launch, 0.001f);
            Assert.AreEqual(flight, path / launch, 0.001f);
            Assert.GreaterOrEqual(path / launch, CueLead + 0.12f - 0.0001f);
        }

        [TestCase(0f, 0.55f, 24f, 0.55f)]
        [TestCase(12f, 0.55f, 24f, 0.275f)]
        [TestCase(24f, 0.55f, 24f, 0f)]
        [TestCase(40f, 0.55f, 24f, 0f)]
        [TestCase(5f, 0.55f, 0f, 0.55f)]
        public void Ping_FallsOffWithDistanceToTheEar(float dist, float vol, float range, float expected)
        {
            Assert.AreEqual(expected, BouncingDisc.PingVolume(dist, vol, range), 0.0001f);
        }

        [TestCase(3, 0, true, 3)]
        [TestCase(3, 2, true, 1)]
        [TestCase(3, 3, true, 0)]
        [TestCase(3, 7, true, 0)]
        [TestCase(3, 7, false, 3)]
        public void Satellites_AreSpentByThrownDiscs(int ring, int thrown, bool follow, int expected)
        {
            Assert.AreEqual(expected, OrbitDancerVisuals.VisibleSatellites(ring, thrown, follow));
        }

        [Test]
        public void Satellites_SitEvenlyOnTheRing()
        {
            Vector3 a = OrbitDancerVisuals.SatelliteLocal(0, 3, 0f, 0.85f, 0f, 1f, 0f);
            Vector3 b = OrbitDancerVisuals.SatelliteLocal(1, 3, 0f, 0.85f, 0f, 1f, 0f);
            Vector3 c = OrbitDancerVisuals.SatelliteLocal(2, 3, 0f, 0.85f, 0f, 1f, 0f);
            foreach (var p in new[] { a, b, c }) Assert.AreEqual(0.85f, new Vector2(p.x, p.z).magnitude, 0.0001f);
            Assert.AreEqual(120f, Vector3.Angle(a, b), 0.01f);
            Assert.AreEqual(120f, Vector3.Angle(b, c), 0.01f);
            Assert.AreEqual(0f, a.y, 0.0001f, "no bob when the amplitude is zero");
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void PresentationReanchor_ConservesRemainingClip(float frame)
        {
            const float now = 10f, oldImpact = 10.20f, oldSpeed = 1.7f;
            float actualImpact = oldImpact + frame;
            float speed = OrbitDancerVisuals.RetimedSpeed(oldSpeed, oldImpact, actualImpact, now);
            Assert.AreEqual(oldSpeed * (oldImpact - now), speed * (actualImpact - now), 0.0001f);
            Assert.AreEqual(actualImpact - 0.12f,
                OrbitDancerVisuals.ReanchoredDeadline(oldImpact - 0.12f, oldImpact, actualImpact), 0.0001f);
        }

        [Test]
        public void TheOneProjectileHook_IsAVirtualWorldContact_AndTheDiscIsAProjectile()
        {
            // The ricochet is a SUBCLASS of the shared bolt: every sentry keeps the default (spend on
            // the wall). The hook is the smallest possible surface: one protected virtual, one
            // protected re-cue, two read-only getters.
            Assert.IsTrue(typeof(Projectile).IsAssignableFrom(typeof(BouncingDisc)));
            var hook = typeof(Projectile).GetMethod("OnWorldContact", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(hook, "Projectile.OnWorldContact(RaycastHit)");
            Assert.IsTrue(hook.IsVirtual && hook.IsFamily, "protected virtual");
            Assert.AreEqual(typeof(bool), hook.ReturnType);
            var recue = typeof(Projectile).GetMethod("Redirect", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(recue, "Projectile.Redirect(Vector3, Vector3)");
            Assert.IsTrue(recue.IsFamily && !recue.IsVirtual);
            Assert.IsNotNull(typeof(Projectile).GetProperty("Direction"));
            Assert.IsNotNull(typeof(Projectile).GetProperty("Speed"));
            // The subclass never hides the base's Unity messages (Update owns the flight, OnDestroy the registry).
            Assert.IsNull(typeof(BouncingDisc).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));
            Assert.IsNull(typeof(BouncingDisc).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        }

        // ------------------------------------------------------------------ prefab

        [Test]
        public void Prefab_IsAnOrdinaryEnemyController_WithTheDancersProfile_TheLauncher_AndTheRing()
        {
            var p = Prefab();
            Assert.IsNotNull(p, "run VibeGame1/4a. Split Forge Animation Clips, then 4b. Build Mini-Bosses");
            Assert.IsNotNull(p.GetComponent<EnemyController>());
            Assert.IsNull(p.GetComponent<BossController>());
            Assert.IsNull(p.GetComponentInChildren<ProjectileShooter>(true), "she throws by hand, on the brain's clock: no metronome.");
            Assert.IsNull(p.GetComponentInChildren<SentryBurst>(true));
            Assert.IsNull(p.GetComponentInChildren<ProjectileVolleySequence>(true));
            Assert.IsNull(p.GetComponentInChildren<ParrySurge>(true));
            Assert.IsNull(p.GetComponentInChildren<EmberAura>(true),
                "OrbitDancerVisuals is the aura's only writer; an EmberAura would be a second one.");
            Assert.IsNull(p.GetComponentInChildren<EnemyWeaponTrail>(true));
            Assert.IsNull(p.GetComponentInChildren<CinderJudgeStorm>(true));

            var launcher = p.GetComponent<OrbitDancerDiscs>();
            Assert.IsNotNull(launcher, "the launcher lives on the root beside the brain.");
            Assert.AreEqual(OrbitDancerAuthoring.DiscThrowAttackName, launcher.discThrowAttack);
            Assert.AreEqual(OrbitDancerAuthoring.SpinThrowAttackName, launcher.spinThrowAttack);
            Assert.AreEqual(3, launcher.volleyCount);
            Assert.AreEqual(3, launcher.whirlCount);
            Assert.AreEqual(4, launcher.liveCap);
            Assert.AreEqual(4, launcher.maxBounces);
            Assert.AreEqual(4f, launcher.discLifetime, 0.001f);
            Assert.AreEqual(900f, launcher.spinDegPerSec, 0.001f);
            Assert.AreEqual(0.60f, launcher.discDiameter, 0.001f);
            Assert.AreEqual(0.12f, launcher.launchMargin, 0.001f);
            Assert.AreEqual(30f, launcher.fanDeg, 0.001f);
            Assert.AreEqual(20f, launcher.bankRange, 0.001f);
            CollectionAssert.AreEqual(new[] { 40f, 65f, 90f }, launcher.bankProbeDeg);
            Assert.AreEqual(0.35f, launcher.maxBankIncidenceY, 0.001f);
            Assert.AreEqual("RightHand", launcher.muzzleBone);
            Assert.AreEqual(0.55f, launcher.pingVolume, 0.001f);
            Assert.AreEqual(24f, launcher.pingRange, 0.001f);
            Assert.Less(launcher.rimColor.maxColorComponent, Projectile.HotCorePeak,
                "the disc rim may never out-bloom the sentry bolt, the brightest thing in the game.");
            Assert.Greater(launcher.rimColor.maxColorComponent, 1.05f, "but it does bloom: an edge of light in a dark realm.");
            Assert.Less(launcher.spinDegPerSec / 60f, 30f, "under 30 deg a frame at 60 fps: a spin, not a strobe.");
            // The muzzle bone exists on the rig, so the disc really leaves the hand.
            bool hand = false;
            foreach (var t in p.GetComponentsInChildren<Transform>(true)) if (t.name == launcher.muzzleBone) hand = true;
            Assert.IsTrue(hand, "no '" + launcher.muzzleBone + "' bone on the rig");

            var v = p.GetComponentInChildren<OrbitDancerVisuals>(true);
            Assert.IsNotNull(v);
            Assert.AreEqual("Idle", v.clipIdle);
            Assert.AreEqual("HeavyAttack", v.clipHeavy);
            Assert.AreEqual("SpinThrow", v.entranceClip);
            Assert.AreEqual("ShoulderCharge", v.shoulderChargeClip);
            Assert.AreEqual(OrbitDancerAuthoring.DiscThrowAttackName, v.discThrowAttack);
            Assert.AreEqual(OrbitDancerAuthoring.SpinThrowAttackName, v.spinThrowAttack);
            Assert.AreEqual(0.35f, v.hitHoldSeconds, 0.001f, "the fluidity pass's cap on a held pose");
            Assert.IsTrue(string.IsNullOrEmpty(v.spinAttackPrefix));
            Assert.IsNotNull(v.travelRoot);
            Assert.IsFalse(v.animator.applyRootMotion);
            Assert.LessOrEqual(v.throwGlow, 0.60f, "EmberAura's documented ceiling before the parry read suffers.");
            Assert.LessOrEqual(v.glowAtBreak, v.throwGlow);
            Assert.Greater(v.footstepDust.a, 0f, "footfall dust, like the other two forge bodies of the fluidity pass");

            // The ring: OrbitRoot under LungeRoot (rides the lean), a sibling of SpinRoot, three discs.
            Assert.IsNotNull(v.orbitRoot, "4b did not build OrbitRoot.");
            Assert.AreEqual("OrbitRoot", v.orbitRoot.name);
            Assert.AreSame(v.lungeRoot, v.orbitRoot.parent);
            Assert.AreNotSame(v.spinRoot, v.orbitRoot);
            Assert.AreEqual(3, v.satelliteCount);
            Assert.AreEqual(3, v.orbitRoot.childCount);
            Assert.AreEqual(0.85f, v.orbitRadius, 0.001f);
            Assert.AreEqual(1.25f, v.orbitRoot.localPosition.y, 0.001f);
            Assert.AreEqual(140f, v.orbitDegPerSec, 0.001f);
            Assert.IsTrue(v.satellitesFollowThrownDiscs);
            for (int i = 0; i < v.orbitRoot.childCount; i++)
            {
                var r = v.orbitRoot.GetChild(i).GetComponent<Renderer>();
                Assert.IsNotNull(r);
                Assert.AreEqual("M_NeonCyan", r.sharedMaterial.name, "an existing under-threshold emissive; no new material");
            }

            // Both throws bake their contact: DiscThrow is airborne in the manifest (0.696-0.913), so 4b
            // keeps the source's 0.478 release outright; SpinThrow's anchor is measured or kept.
            int disc = v.IndexOfNamedClip("DiscThrow");
            Assert.GreaterOrEqual(disc, 0, "4b did not bake DiscThrow.");
            Assert.AreEqual(0.478f, v.namedClipHits[disc], 0.001f);
            int spin = v.IndexOfNamedClip("SpinThrow");
            Assert.GreaterOrEqual(spin, 0, "4b did not bake SpinThrow.");
            Assert.That(v.namedClipHits[spin], Is.InRange(0.2f, 0.8f));
            foreach (var clipName in new[] { "Jab2", "ShoulderCharge", "HeavyAttack", "ComboFinisher", "AttackSwing", "AttackStab", "AttackKick" })
                Assert.GreaterOrEqual(v.IndexOfNamedClip(clipName), 0, clipName + " has no baked contact.");
        }

        // ------------------------------------------------------------------ sandbox

        [Test]
        public void SandboxPad_HasRoomForTheChargeAndTheWalls_AndPullsNeitherNeighbour()
        {
            Vector3 p = SandboxBuilder.OrbitDancerPadPosition;
            Vector3 v18 = SandboxBuilder.FlurryBrawlerV18PadPosition;
            Vector3 judge = SandboxBuilder.CinderJudgePadPosition;
            const float halfWidth = 2.75f;
            var whirl = Atk(OrbitDancerAuthoring.SpinThrowAttackName);
            float reach = Mathf.Max(halfWidth, whirl.range + 0.5f);
            // Footprint: the whirl around the pad, plus the south charge lane down to the wake switch,
            // expanded by a player capsule.
            float minX = p.x - reach, maxX = p.x + reach;
            float minZ = p.z - SandboxBuilder.OrbitDancerWakeOffset - 0.9f, maxZ = p.z + reach;
            Assert.GreaterOrEqual(minX, SandboxBuilder.YardMinX);
            Assert.LessOrEqual(maxX, SandboxBuilder.YardMaxX);
            Assert.GreaterOrEqual(minZ, -SandboxBuilder.YardHalfZ);
            Assert.LessOrEqual(maxZ, SandboxBuilder.YardHalfZ);
            SandboxBuilder.YardBox? nearestWall = null;
            foreach (var b in SandboxBuilder.YardLayout())
            {
                if (b.kind == SandboxBuilder.YardKind.Floor ||
                    b.kind == SandboxBuilder.YardKind.Stripe) continue;
                bool overlap = minX < b.MaxX && maxX > b.MinX && minZ < b.MaxZ && maxZ > b.MinZ;
                Assert.IsFalse(overlap, "Orbit Dancer footprint overlaps " + b.name);
                if (b.kind == SandboxBuilder.YardKind.Wall && (nearestWall == null || b.MinX < nearestWall.Value.MinX))
                    nearestWall = b;
                // Never in a tower tier's drop zone (MovementYardTests' contract, applied to the pad).
                if (b.kind == SandboxBuilder.YardKind.Tier)
                    Assert.IsFalse(minX >= b.MaxX - 0.001f && maxZ > b.MinZ && minZ < b.MaxZ,
                        "the pad stands in " + b.name + "'s drop zone");
            }
            // The water lane is a trigger, not a solid: the footprint must still stay off it.
            foreach (var b in SandboxBuilder.YardLayout())
                if (b.kind == SandboxBuilder.YardKind.Water)
                    Assert.GreaterOrEqual(minZ, b.MaxZ, "the charge lane runs into the water");
            // The long walls are the bank surfaces: inside the launcher's probe range from the pad.
            Assert.IsTrue(nearestWall.HasValue);
            var launcher = Prefab().GetComponent<OrbitDancerDiscs>();
            Assert.LessOrEqual(nearestWall.Value.MinX - p.x, launcher.bankRange,
                "no wall inside bankRange: the sandbox would never show a bank shot");

            // Neither standalone neighbour can be pulled into her fight: pad centres further apart than
            // every aggro range (the shipped data, not a literal).
            foreach (var other in new[] { v18, judge })
            {
                float apart = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(other.x, 0f, other.z));
                Assert.Greater(apart, Data().aggroRange);
            }
            if (V18() != null) Assert.Greater(Vector3.Distance(p, v18), V18().aggroRange);
            if (Judge() != null) Assert.Greater(Vector3.Distance(p, judge), Judge().aggroRange);
            // And the yard spawn cannot wake her.
            Assert.Greater(Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(SandboxBuilder.YardSpawnPos.x, 0f, SandboxBuilder.YardSpawnPos.z)),
                Data().aggroRange);

            // The wake switch stands outside the charge reach and the whirl.
            var shoulder = Atk("OrbitDancer_ShoulderCharge");
            float chargeReach = shoulder.lungeDistance + shoulder.range + 0.5f;
            Assert.Greater(SandboxBuilder.OrbitDancerWakeOffset, chargeReach);
            Assert.Greater(SandboxBuilder.OrbitDancerWakeOffset, whirl.range + 0.5f + 0.9f);

            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity", OpenSceneMode.Additive);
            try
            {
                GameObject spawn = null, wake = null, judgeSpawn = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "Spawn_Legendary_OrbitDancer") spawn = t.gameObject;
                        else if (t.name == "Wake_Legendary_OrbitDancer") wake = t.gameObject;
                        else if (t.name == "Spawn_Legendary_CinderJudge") judgeSpawn = t.gameObject;
                    }
                }

                Assert.IsNotNull(spawn, "run VibeGame1/7. Build Sandbox Scene");
                Assert.IsNotNull(spawn.GetComponent<EnemySpawner>());
                Assert.IsNotNull(wake, "Orbit Dancer wake switch missing from the rebuilt Sandbox scene.");
                Assert.IsNotNull(wake.GetComponent<SandboxEnemySwitch>());
                Assert.IsNotNull(judgeSpawn, "the Cinder Judge fixture was removed.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void IsTheT2RealmBoss()
        {
            // Boss roster seated 2026-09-13 (LevelDefinitionAuthoring.SeatBossRoster): the T2 realm's (duo realms 2026-09-14)
            // spawner name stays the contract, the occupant is this body.
            var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Data/Levels/Level_01_Level.asset");
            Assert.IsNotNull(level);
            SpawnDef seat = null;
            foreach (var s in level.spawns) if (s != null && s.name == "Spawn_Legendary_Knight") seat = s;
            Assert.IsNotNull(seat, "Spawn_Legendary_Knight missing from Level_01");
            Assert.AreEqual("Legendary_OrbitDancer", seat.prefabKey);
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
