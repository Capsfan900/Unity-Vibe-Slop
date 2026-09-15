using System.Collections.Generic;
using System.IO;
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
    /// Pins the additive sandbox-only Cinder Judge, built the V18 way. Every number below is read off
    /// the shipped ASSET or prefab (rule 9), never off a field initialiser; the storm's arithmetic --
    /// the one thing this body adds to the roster -- is held against the player's own bars.
    /// </summary>
    public class CinderJudgeDataTests
    {
        const string Fbx = "Assets/Enemies/CinderJudge.fbx";
        const string Manifest = "Assets/Enemies/CinderJudge.clips.json";
        const string Provenance = "Assets/Enemies/CinderJudge.provenance.txt";
        const string PrefabPath = "Assets/Prefabs/Legendary_CinderJudge.prefab";
        const float CueLead = 0.28f;              // EnemyController.cueLead
        const float PlayerPostureMax = 100f;      // PlayerPosture.Max default; ComputeMax adds stat upgrades on top
        const float UnblockablePostureMult = 1.5f; // PlayerCombat.ReceiveAttack: postureMult for an unblockable

        static readonly string[] AttackNames =
        {
            "CinderJudge_Jab2", "CinderJudge_Swing", "CinderJudge_ComboFinisher", "CinderJudge_Stab",
            "CinderJudge_Kick", "CinderJudge_Heavy", "CinderJudge_ShoulderCharge",
            CinderJudgeAuthoring.StormAttackName,
            CinderJudgeAuthoring.ShieldRaiseAttackName, CinderJudgeAuthoring.ShieldBashAttackName,
        };

        static EnemyData Data() => AssetDatabase.LoadAssetAtPath<EnemyData>(
            EnemyPaths.Data(CinderJudgeAuthoring.EnemyName));

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
        public void SourceArt_IsTheApprovedCinderJudgeExport()
        {
            Assert.AreEqual(CinderJudgeAuthoring.SourceFbxSha256, Sha256(Fbx));
            Assert.IsTrue(File.Exists(Manifest));
            string prov = File.ReadAllText(Provenance);
            StringAssert.Contains(CinderJudgeAuthoring.SourceFbxSha256, prov);
            StringAssert.Contains(CinderJudgeAuthoring.SourceManifestSha256, prov);
        }

        [Test]
        public void Manifest_IsExactlyTheApprovedFifteenStates()
        {
            CollectionAssert.AreEqual(CinderJudgeAuthoring.ClipAllowlist, ForgeClipSplitter.ClipNames(Fbx));
            // The user's move list, plus the two reactions PuppetVisuals requires (Hit, Death). Nothing
            // from the larger source export -- no Block, IdleCombat, Spawn, Jab1, Hook, Uppercut,
            // LightAttack, Stance or Backstep -- may creep in with a re-export.
            Assert.AreEqual(17, CinderJudgeAuthoring.ClipAllowlist.Length);
            CollectionAssert.DoesNotContain(ForgeClipSplitter.ClipsWithEvent(Fbx, "OnAttackHit"), "Roar",
                "Roar is a roar, not a strike; 4b must bake its explicit storm profile, not an invented event.");
            float roar;
            Assert.IsTrue(CinderJudgeAuthoring.TryExplicitContact(CinderJudgeAuthoring.EnemyName, "Roar", out roar));
            Assert.AreEqual(0.40f, roar, 0.0001f);
            Assert.AreEqual(0.40f, ForgeClipSplitter.ReadEventNormalizedTime(Fbx, "Roar", 0f, "OnRoar"), 0.0001f,
                "the explicit anchor must be the source's own OnRoar moment.");
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
            CollectionAssert.AreEquivalent(CinderJudgeAuthoring.ClipAllowlist, importedNames);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                PuppetAnimatorFactory.ControllerDir + "/Legendary_CinderJudge_Animator.controller");
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
            CollectionAssert.AreEquivalent(CinderJudgeAuthoring.ClipAllowlist, stateNames);
        }

        // ------------------------------------------------------------------ data

        [Test]
        public void Data_IsSeparateAndHeavierThanV18()
        {
            var d = Data();
            Assert.IsNotNull(d, "run VibeGame1/3. Create Data");
            Assert.AreEqual("THE CINDER JUDGE", d.displayName);
            Assert.AreEqual(240f, d.maxHP, 0.001f);
            Assert.AreEqual(200f, d.maxPosture, 0.001f);
            Assert.AreEqual(5f, d.postureRegen, 0.001f);
            Assert.AreEqual(3.5f, d.staggerSeconds, 0.001f);
            Assert.AreEqual(4.6f, d.moveSpeed, 0.001f);
            Assert.AreEqual(2.4f, d.preferredRange, 0.001f);
            Assert.AreEqual(18f, d.aggroRange, 0.001f);
            Assert.AreEqual(0.80f, d.aggression, 0.001f);
            Assert.AreEqual(1f, d.scale, 0.001f);
            Assert.IsFalse(d.shootsProjectiles);
            Assert.IsFalse(d.rangedOnly);
            Assert.AreNotSame(d, AssetDatabase.LoadAssetAtPath<EnemyData>(
                EnemyPaths.Data(FlurryBrawlerV18Authoring.EnemyName)));

            // The spice, held: heavier on both bars and slower on the floor than V18.
            var v18 = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(FlurryBrawlerV18Authoring.EnemyName));
            if (v18 != null)
            {
                Assert.Greater(d.maxHP, v18.maxHP);
                Assert.Greater(d.maxPosture, v18.maxPosture);
                Assert.Less(d.moveSpeed, v18.moveSpeed);
                Assert.Greater(d.preferredRange, v18.preferredRange);
            }

            var names = new HashSet<string>();
            foreach (var h in EveryHit()) names.Add(h.name);
            CollectionAssert.AreEquivalent(AttackNames, names);
        }

        [Test]
        public void AttackTimingsAndTravel_AreTheApprovedProfile()
        {
            AssertAttack("CinderJudge_Jab2", "Jab2", 0.50f, 0.05f, 0.16f, 0.70f, 0f);
            AssertAttack("CinderJudge_Swing", "AttackSwing", 0.60f, 0.05f, 0.20f, 0.80f, 0f);
            AssertAttack("CinderJudge_ComboFinisher", "ComboFinisher", 0.70f, 0.06f, 0.20f, 1.30f, 0f);
            AssertAttack("CinderJudge_Stab", "AttackStab", 0.55f, 0.05f, 0.16f, 0.70f, 0f);
            AssertAttack("CinderJudge_Kick", "AttackKick", 0.65f, 0.05f, 0.22f, 0.90f, 0f);
            AssertAttack("CinderJudge_Heavy", "HeavyAttack", 1.05f, 0.08f, 0.30f, 1.40f, 0.75f);
            AssertAttack("CinderJudge_ShoulderCharge", "ShoulderCharge", 1.00f, 0.08f, 0.28f, 1.10f, 3.32f);
            AssertAttack(CinderJudgeAuthoring.StormAttackName, "Roar", 1.60f, 0.45f, 3.20f, 3.20f, 0f);

            Assert.IsTrue(Atk("CinderJudge_Kick").unblockable);
            Assert.IsTrue(Atk("CinderJudge_ShoulderCharge").unblockable);
            Assert.IsFalse(Atk("CinderJudge_Heavy").unblockable, "the heavy is the big parry payoff, not a red.");
            Assert.AreEqual(1.9f, Atk("CinderJudge_Heavy").parryPostureMultiplier, 0.001f);

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
            // (the Heavy's 1.14 m to the right) and backward steps (Jab2) have no lunge channel.
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
                        forward.ToString("F2") + " m forward. The art and the data have drifted apart.");
                }
                else
                {
                    Assert.AreEqual(0f, h.lungeDistance, 0.001f,
                        h.name + ": '" + h.clip + "' does not travel in the art, so the enemy must not either.");
                }
            }
        }

        // ------------------------------------------------------------------ the storm

        [Test]
        public void Storm_IsOneScheduledContact_ThenUnblockableTicks_OnTheCylinderTheTornadoDraws()
        {
            var storm = Atk(CinderJudgeAuthoring.StormAttackName);
            Assert.IsNotNull(storm, "run VibeGame1/3. Create Data");
            Assert.IsTrue(storm.unblockable, "a tick is a place, not a swing: parry must never answer it.");
            Assert.AreEqual(360f, storm.coneDeg, 0.001f, "the brain's one contact must not care where you stand.");
            Assert.AreEqual(6f, storm.damage, 0.001f, "damage is PER TICK.");
            Assert.AreEqual(0f, storm.lungeDistance, 0.001f);
            Assert.IsFalse(storm.windupPose.authored, "the lift is StormRoot's, never LungeRoot's pose.");

            var zone = Prefab().GetComponent<CinderJudgeStorm>();
            Assert.IsNotNull(zone, "run VibeGame1/4b. Build Mini-Bosses");
            Assert.AreEqual(CinderJudgeAuthoring.StormAttackName, zone.stormAttack);
            Assert.AreEqual(0.30f, zone.tickInterval, 0.001f);
            Assert.AreEqual(3.6f, zone.radius, 0.001f);
            Assert.AreEqual(4.5f, zone.height, 0.001f);
            Assert.AreEqual(0.6f, zone.floorSlack, 0.001f);
            // EnemyController.DoImpact adds 0.5 m of slack to range: the brain's contact and the
            // component's ticks must test the SAME circle, and the tornado is drawn at that radius.
            Assert.AreEqual(zone.radius, storm.range + 0.5f, 0.001f);
            Assert.Greater(zone.radius, Data().preferredRange, "fighting distance must be INSIDE the ring.");

            int uses = 0;
            MovesetEntry entry = null;
            foreach (var e in Data().moveset.entries)
            {
                foreach (var hit in e.combo.hits)
                    if (hit == storm) { uses++; entry = e; }
            }
            Assert.AreEqual(1, uses, "the storm is one standalone EnemyController schedule.");
            Assert.AreEqual(1, entry.combo.hits.Length);
            Assert.AreEqual(1.0f, entry.weight, 0.001f);
            Assert.AreEqual(0f, entry.minRange, 0.001f);
            Assert.AreEqual(4.5f, entry.maxRange, 0.001f);
            Assert.AreEqual(14f, entry.cooldown, 0.001f);
        }

        [Test]
        public void Aegis_IsARaiseThenBashCombo_WithAReadableTellAndABreakablePunish()
        {
            var raise = Atk(CinderJudgeAuthoring.ShieldRaiseAttackName);
            var bash = Atk(CinderJudgeAuthoring.ShieldBashAttackName);
            Assert.IsNotNull(raise); Assert.IsNotNull(bash);
            Assert.AreEqual("ShieldRaise", raise.clip);
            Assert.AreEqual("ShieldBash", bash.clip);
            Assert.AreEqual(0f, raise.damage, 0.001f, "the raise is a stance: it never lands a blow");
            Assert.AreEqual(0f, raise.range, 0.001f);
            Assert.GreaterOrEqual(raise.windup, 0.45f, "the arms coming up IS the tell");
            Assert.IsFalse(bash.unblockable, "the bash must be parryable: a Perfect on it is how the shield breaks");
            Assert.Greater(bash.damage, 0f);

            MovesetEntry entry = null;
            foreach (var e in Data().moveset.entries)
                if (e.combo.hits.Length == 2 && e.combo.hits[0] == raise && e.combo.hits[1] == bash) entry = e;
            Assert.IsNotNull(entry, "raise then bash is one schedule");
            Assert.Greater(entry.cooldown, 8f, "a signature, not a loop");

            var shield = Prefab().GetComponent<CinderJudgeShield>();
            Assert.IsNotNull(shield, "CinderJudgeShield on the root");
            Assert.AreEqual(raise.name, shield.raiseAttack);
            Assert.AreEqual(bash.name, shield.bashAttack);
            Assert.AreEqual(18f, shield.recoilPosture, 0.001f);
            Assert.AreEqual(0.35f, shield.shatterPostureFraction, 0.001f);
            Assert.Less(shield.domeColor.maxColorComponent, 1.05f, "the dome stays under bloom at rest");
            Assert.IsTrue(Prefab().GetComponent<IPlayerHitDeflector>() != null);
        }

        [Test]
        public void Aegis_HoldsOnlyThroughTheRaiseAndTheBashWindup()
        {
            const string r = "R", b = "B";
            Assert.IsTrue(CinderJudgeShield.HoldsShield(true, EnemyController.State.Windup, r, r, b));
            Assert.IsTrue(CinderJudgeShield.HoldsShield(true, EnemyController.State.Strike, r, r, b));
            Assert.IsTrue(CinderJudgeShield.HoldsShield(true, EnemyController.State.Windup, b, r, b));
            Assert.IsFalse(CinderJudgeShield.HoldsShield(true, EnemyController.State.Strike, b, r, b), "dropped for the shove itself");
            Assert.IsFalse(CinderJudgeShield.HoldsShield(true, EnemyController.State.Recover, r, r, b));
            Assert.IsFalse(CinderJudgeShield.HoldsShield(false, EnemyController.State.Strike, r, r, b));
            Assert.IsFalse(CinderJudgeShield.HoldsShield(true, EnemyController.State.Strike, "CinderJudge_Swing", r, b));
        }

        [Test]
        public void Storm_WorstCase_IsLethalToStandIn_ButNeverAStaggerMachine()
        {
            var storm = Atk(CinderJudgeAuthoring.StormAttackName);
            var zone = Prefab().GetComponent<CinderJudgeStorm>();
            int ticks = CinderJudgeStorm.TicksAfterImpact(storm.strikeDuration, zone.tickInterval);
            Assert.AreEqual(10, ticks, "3.20 / 0.30 -> ten ticks after the brain's impact");
            float worst = CinderJudgeStorm.MaxDamage(storm.damage, storm.strikeDuration, zone.tickInterval);
            Assert.AreEqual(66f, worst, 0.001f);

            // Standing in it the whole time must HURT (a real fraction of a bar) without breaking the
            // player's posture on its own: the storm is "leave", not "stagger, then die".
            var stats = Stats();
            float hitPostureMult = stats != null ? stats.hitPostureMultiplier : 0.5f;
            float posture = (1 + ticks) * storm.damage * hitPostureMult * UnblockablePostureMult;
            Assert.Less(posture, PlayerPostureMax, "eleven ticks would break the player's posture by themselves.");
            Assert.Greater(worst, 40f, "the storm must be worth leaving.");
            Assert.Less(worst, 100f, "the storm must be survivable from full health if you are slow.");

            // The real opening after the landing, once aggression compresses recovery.
            float opening = storm.recovery * Mathf.Lerp(1f, 0.35f, Data().aggression);
            Assert.GreaterOrEqual(opening, 1.5f, "the landing is the punish: at least 1.5 s on the floor.");
            Assert.Greater(opening, Atk("CinderJudge_Heavy").recovery * Mathf.Lerp(1f, 0.35f, Data().aggression),
                "the storm's opening must be the biggest the Judge gives.");
        }

        [TestCase(0f, 0f, 0f, true)]
        [TestCase(3.59f, 0f, 0f, true)]
        [TestCase(3.61f, 0f, 0f, false)]
        [TestCase(2.5f, 0f, 2.5f, true)]     // 3.54 m diagonal: inside
        [TestCase(2.6f, 0f, 2.6f, false)]    // 3.68 m diagonal: outside
        [TestCase(0f, 4.4f, 0f, true)]       // a jump inside the ring is still inside the ring
        [TestCase(0f, 4.6f, 0f, false)]
        [TestCase(0f, -0.5f, 0f, true)]      // one step down a kerb is not out
        [TestCase(0f, -0.7f, 0f, false)]
        public void Storm_CylinderTest_IsADiscAndABand(float dx, float dy, float dz, bool inside)
        {
            var center = new Vector3(136f, 0f, 20f);
            Assert.AreEqual(inside, CinderJudgeStorm.InsideCylinder(center, center + new Vector3(dx, dy, dz), 3.6f, 4.5f, 0.6f));
        }

        [Test]
        public void StormLift_RisesToImpact_HoldsThroughTheSpin_AndTouchesDownAtStrikeEnd()
        {
            const float rise = 10f, impact = 10.45f, descent = 13.30f, end = 13.65f, h = 2.4f;
            Assert.AreEqual(0f, CinderJudgeVisuals.StormLift(9.9f, rise, impact, descent, end, h), 0.0001f);
            Assert.AreEqual(0f, CinderJudgeVisuals.StormLift(rise, rise, impact, descent, end, h), 0.0001f);
            Assert.AreEqual(h, CinderJudgeVisuals.StormLift(impact, rise, impact, descent, end, h), 0.0001f,
                "the body must be at full height on the brain's impact frame: tick zero lands from the air.");
            Assert.AreEqual(h, CinderJudgeVisuals.StormLift(12f, rise, impact, descent, end, h), 0.0001f);
            Assert.AreEqual(h, CinderJudgeVisuals.StormLift(descent, rise, impact, descent, end, h), 0.0001f);
            float mid = CinderJudgeVisuals.StormLift((descent + end) * 0.5f, rise, impact, descent, end, h);
            Assert.That(mid, Is.InRange(0.1f, h - 0.1f));
            Assert.AreEqual(0f, CinderJudgeVisuals.StormLift(end, rise, impact, descent, end, h), 0.0001f,
                "touchdown must equal the strike's end, which is when the ticks stop and recovery begins.");
            Assert.AreEqual(0f, CinderJudgeVisuals.StormLift(end + 1f, rise, impact, descent, end, h), 0.0001f);
        }

        [TestCase(0f, 0.35f, 0f)]
        [TestCase(90f, 0.35f, 270f / 0.35f)]
        [TestCase(359f, 0.35f, 1f / 0.35f)]
        [TestCase(720f, 0.35f, 0f)]
        [TestCase(1000f, 0.5f, 80f / 0.5f)]
        public void SquaringRate_UnwindsForwardToTheNextFullTurn(float yaw, float seconds, float expected)
        {
            Assert.AreEqual(expected, CinderJudgeVisuals.SquaringRate(yaw, seconds), 0.01f);
            Assert.GreaterOrEqual(CinderJudgeVisuals.SquaringRate(yaw, seconds), 0f, "never reverses");
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void PresentationReanchor_ConservesRemainingClip(float frame)
        {
            const float now = 10f, oldImpact = 10.20f, oldSpeed = 1.7f;
            float actualImpact = oldImpact + frame;
            float speed = CinderJudgeVisuals.RetimedSpeed(oldSpeed, oldImpact, actualImpact, now);
            Assert.AreEqual(oldSpeed * (oldImpact - now), speed * (actualImpact - now), 0.0001f);
            Assert.AreEqual(actualImpact - 0.12f,
                CinderJudgeVisuals.ReanchoredDeadline(oldImpact - 0.12f, oldImpact, actualImpact), 0.0001f);
        }

        // ------------------------------------------------------------------ prefab

        [Test]
        public void Prefab_IsAnOrdinaryEnemyController_WithTheJudgesProfileAndStormRoot()
        {
            var p = Prefab();
            Assert.IsNotNull(p, "run VibeGame1/4a. Split Forge Animation Clips, then 4b. Build Mini-Bosses");
            Assert.IsNotNull(p.GetComponent<EnemyController>());
            Assert.IsNull(p.GetComponent<BossController>());
            Assert.IsNull(p.GetComponentInChildren<ProjectileShooter>(true));
            Assert.IsNull(p.GetComponentInChildren<SentryBurst>(true));
            Assert.IsNull(p.GetComponentInChildren<ProjectileVolleySequence>(true));
            Assert.IsNull(p.GetComponentInChildren<ParrySurge>(true));
            Assert.IsNull(p.GetComponentInChildren<EmberAura>(true),
                "CinderJudgeVisuals is the aura's only writer; an EmberAura would be a second one.");
            Assert.IsNull(p.GetComponentInChildren<EnemyWeaponTrail>(true));
            Assert.IsNotNull(p.GetComponent<CinderJudgeStorm>(), "the ticking zone lives on the root beside the brain.");

            var v = p.GetComponentInChildren<CinderJudgeVisuals>(true);
            Assert.IsNotNull(v);
            Assert.AreEqual("Idle", v.clipIdle);
            Assert.AreEqual("HeavyAttack", v.clipHeavy);
            Assert.AreEqual("Roar", v.roarClip);
            Assert.AreEqual("Jump", v.jumpClip);
            Assert.AreEqual(CinderJudgeAuthoring.StormAttackName, v.stormAttack);
            Assert.IsTrue(string.IsNullOrEmpty(v.spinAttackPrefix));
            Assert.IsNotNull(v.travelRoot);
            Assert.IsFalse(v.animator.applyRootMotion);

            // StormRoot sits between LungeRoot and SpinRoot: four transforms, four writers.
            Assert.IsNotNull(v.stormRoot, "4b did not insert StormRoot.");
            Assert.AreEqual("StormRoot", v.stormRoot.name);
            Assert.AreSame(v.lungeRoot, v.stormRoot.parent);
            Assert.AreSame(v.stormRoot, v.spinRoot.parent);

            // The storm profile, rebuilt onto the prefab (rule 9).
            Assert.AreEqual(2.4f, v.stormFloatHeight, 0.001f);
            Assert.AreEqual(0.35f, v.stormDescendSeconds, 0.001f);
            Assert.AreEqual(540f, v.stormSpinDegPerSec, 0.001f);
            Assert.AreEqual(0.28f, v.jumpTakeoffNormalized, 0.001f);
            Assert.AreEqual(0.56f, v.jumpApexNormalized, 0.001f);
            Assert.AreEqual(0.85f, v.jumpLandNormalized, 0.001f);
            Assert.GreaterOrEqual(v.stormArcInterval, LightningEffect.BundleSeconds,
                "two bundles alive at once would be four point lights.");
            Assert.LessOrEqual(v.stormGlow, 0.60f, "EmberAura's documented ceiling before the parry read suffers.");
            Assert.Less(v.stormCoreIntensity, 3.0f, "the tornado may never out-bloom the alert tell (3.0).");
            Assert.LessOrEqual(v.stormDescendSeconds, Atk(CinderJudgeAuthoring.StormAttackName).strikeDuration * 0.25f,
                "the descent is the tail of the spin, not most of it.");

            // The storm's apex must be reachable inside the rise without clamping the clip.
            float jumpLen = ClipNamed("Jump").length;
            float segment = (v.jumpApexNormalized - v.jumpTakeoffNormalized) * jumpLen;
            float speed = segment / Atk(CinderJudgeAuthoring.StormAttackName).impactDelay;
            Assert.That(speed, Is.InRange(v.minClipSpeed, v.maxClipSpeed));

            int roar = v.IndexOfNamedClip("Roar");
            Assert.GreaterOrEqual(roar, 0, "4b did not bake the explicit Roar profile.");
            Assert.AreEqual(0.40f, v.namedClipHits[roar], 0.0001f);
            foreach (var clipName in new[] { "Jab2", "ShoulderCharge", "HeavyAttack", "ComboFinisher", "AttackSwing", "AttackStab", "AttackKick" })
                Assert.GreaterOrEqual(v.IndexOfNamedClip(clipName), 0, clipName + " has no baked contact.");
        }

        // ------------------------------------------------------------------ sandbox

        [Test]
        public void SandboxPad_HasRoomForTheChargeAndTheStorm_AndNeverPullsV18()
        {
            Vector3 p = SandboxBuilder.CinderJudgePadPosition;
            Vector3 v18 = SandboxBuilder.FlurryBrawlerV18PadPosition;
            const float halfWidth = 2.75f;
            const float stormRadius = 3.6f;
            float reach = Mathf.Max(halfWidth, stormRadius);
            // Footprint: the storm ring around the pad, plus the south charge lane down to the wake
            // switch, expanded by a player capsule.
            float minX = p.x - reach, maxX = p.x + reach;
            float minZ = p.z - SandboxBuilder.CinderJudgeWakeOffset - 0.9f, maxZ = p.z + reach;
            Assert.GreaterOrEqual(minX, SandboxBuilder.YardMinX);
            Assert.LessOrEqual(maxX, SandboxBuilder.YardMaxX);
            Assert.GreaterOrEqual(minZ, -SandboxBuilder.YardHalfZ);
            Assert.LessOrEqual(maxZ, SandboxBuilder.YardHalfZ);
            foreach (var b in SandboxBuilder.YardLayout())
            {
                if (b.kind == SandboxBuilder.YardKind.Floor ||
                    b.kind == SandboxBuilder.YardKind.Stripe) continue;
                bool overlap = minX < b.MaxX && maxX > b.MinX && minZ < b.MaxZ && maxZ > b.MinZ;
                Assert.IsFalse(overlap, "Cinder Judge footprint overlaps " + b.name);
            }

            // Not on V18's pad or its charge lane.
            float v18MinX = v18.x - halfWidth, v18MaxX = v18.x + halfWidth;
            float v18MinZ = v18.z - 6.5f - 0.9f, v18MaxZ = v18.z + halfWidth;
            Assert.IsFalse(minX < v18MaxX && maxX > v18MinX && minZ < v18MaxZ && maxZ > v18MinZ,
                "the Judge's footprint overlaps V18's pad or charge lane.");

            // Neither body can be pulled into the other's fight: pad centres further apart than both
            // aggro ranges (the shipped data, not a literal).
            float apart = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(v18.x, 0f, v18.z));
            Assert.Greater(apart, Data().aggroRange);
            var v18Data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(FlurryBrawlerV18Authoring.EnemyName));
            if (v18Data != null) Assert.Greater(apart, v18Data.aggroRange);

            // The wake switch stands outside the charge reach and the storm ring.
            var shoulder = Atk("CinderJudge_ShoulderCharge");
            float chargeReach = shoulder.lungeDistance + shoulder.range + 0.5f;
            Assert.Greater(SandboxBuilder.CinderJudgeWakeOffset, chargeReach);
            Assert.Greater(SandboxBuilder.CinderJudgeWakeOffset, stormRadius + 0.9f);

            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity", OpenSceneMode.Additive);
            try
            {
                GameObject spawn = null, wake = null, v18Spawn = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "Spawn_Legendary_CinderJudge") spawn = t.gameObject;
                        else if (t.name == "Wake_Legendary_CinderJudge") wake = t.gameObject;
                        else if (t.name == "Spawn_Legendary_FlurryBrawlerV18") v18Spawn = t.gameObject;
                    }
                }

                Assert.IsNotNull(spawn, "run VibeGame1/7. Build Sandbox Scene");
                Assert.IsNotNull(spawn.GetComponent<EnemySpawner>());
                Assert.IsNotNull(wake, "Cinder Judge wake switch missing from the rebuilt Sandbox scene.");
                Assert.IsNotNull(wake.GetComponent<SandboxEnemySwitch>());
                Assert.IsNotNull(v18Spawn, "the separate V18 fixture was removed.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void IsTheT4DuoPartner()
        {
            // Duo realms 2026-09-14 (LevelDefinitionAuthoring.RealmPartners): the Judge fights beside V18
            // in the T4 realm before the Warden.
            var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Data/Levels/Level_01_Level.asset");
            Assert.IsNotNull(level);
            SpawnDef seat = null;
            foreach (var s in level.spawns) if (s != null && s.name == "Spawn_Legendary_T4_Duo") seat = s;
            Assert.IsNotNull(seat, "Spawn_Legendary_T4_Duo missing from Level_01");
            Assert.AreEqual("Legendary_CinderJudge", seat.prefabKey);
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
