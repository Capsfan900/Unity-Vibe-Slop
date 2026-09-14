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
    /// Pins the additive sandbox-only V18 comparison body. These tests deliberately use V18-prefixed
    /// paths and never load or assert through the existing v15 Flurry Brawler assets.
    /// </summary>
    public class FlurryBrawlerV18DataTests
    {
        const string Fbx = "Assets/Enemies/FlurryBrawlerV18.fbx";
        const string Manifest = "Assets/Enemies/FlurryBrawlerV18.clips.json";
        const string PrefabPath = "Assets/Prefabs/Legendary_FlurryBrawlerV18.prefab";

        static readonly string[] AttackNames =
        {
            "BrawlerV18_Swing", "BrawlerV18_Overhead", "BrawlerV18_Stab", "BrawlerV18_Kick",
            "BrawlerV18_Jab2", "BrawlerV18_Dash", "BrawlerV18_ShoulderCharge",
            "BrawlerV18_LevitateClap", "BrawlerV18_Combo2", "BrawlerV18_Grab",
        };

        static EnemyData Data() => AssetDatabase.LoadAssetAtPath<EnemyData>(
            EnemyPaths.Data(FlurryBrawlerV18Authoring.EnemyName));

        static EnemyAttackData Atk(string name) => AssetDatabase.LoadAssetAtPath<EnemyAttackData>(
            "Assets/Data/Attacks/" + name + ".asset");

        static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        static IEnumerable<EnemyAttackData> EveryHit()
        {
            var seen = new HashSet<EnemyAttackData>();
            var data = Data();
            if (data == null) yield break;
            foreach (var combo in data.ResolveCombos())
                foreach (var hit in combo.hits)
                    if (hit != null && seen.Add(hit)) yield return hit;
        }

        [Test]
        public void SourceArt_IsTheApprovedV18Export()
        {
            Assert.AreEqual(FlurryBrawlerV18Authoring.SourceFbxSha256, Sha256(Fbx));
            Assert.IsTrue(File.Exists(Manifest));
            StringAssert.Contains(FlurryBrawlerV18Authoring.SourceFbxSha256,
                File.ReadAllText("Assets/Enemies/FlurryBrawlerV18.provenance.txt"));
            StringAssert.Contains(FlurryBrawlerV18Authoring.SourceManifestSha256,
                File.ReadAllText("Assets/Enemies/FlurryBrawlerV18.provenance.txt"));
        }

        [Test]
        public void Manifest_IsExactlyTheApprovedEighteenStates()
        {
            CollectionAssert.AreEqual(FlurryBrawlerV18Authoring.ClipAllowlist,
                ForgeClipSplitter.ClipNames(Fbx));
            CollectionAssert.DoesNotContain(
                ForgeClipSplitter.ClipsWithEvent(Fbx, "OnAttackHit"), "Dash",
                "Dash is eventless source art; 4b must use its explicit profile, not an invented event.");
            float dash;
            Assert.IsTrue(FlurryBrawlerV18Authoring.TryExplicitContact(
                FlurryBrawlerV18Authoring.EnemyName, "Dash", out dash));
            Assert.AreEqual(0.60f, dash, 0.0001f);
            float combo;
            Assert.IsTrue(FlurryBrawlerV18Authoring.TryExplicitContact(
                FlurryBrawlerV18Authoring.EnemyName, "Combo2", out combo));
            Assert.AreEqual(0.671f, combo, 0.0001f);
        }

        [Test]
        public void ImportedRigAndController_AreExactlyTheNonemptyEventlessAllowlist()
        {
            var importer = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            Assert.IsNotNull(importer);
            Assert.AreEqual(ModelImporterAnimationType.Generic, importer.animationType);
            Assert.AreEqual(18, importer.clipAnimations.Length,
                "run VibeGame1/4a. Split Forge Animation Clips");
            foreach (var c in importer.clipAnimations)
                Assert.AreEqual(0, c.events.Length, c.name + " has gameplay AnimationEvents.");

            var imported = PuppetAnimatorFactory.ClipsIn(Fbx);
            Assert.AreEqual(18, imported.Count);
            var importedNames = new List<string>();
            foreach (var c in imported)
            {
                importedNames.Add(c.name);
                Assert.IsFalse(c.empty, c.name + " imported empty.");
                Assert.AreEqual(0, AnimationUtility.GetAnimationEvents(c).Length,
                    c.name + " has a runtime AnimationEvent.");
            }
            CollectionAssert.AreEquivalent(FlurryBrawlerV18Authoring.ClipAllowlist, importedNames);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                PuppetAnimatorFactory.ControllerDir + "/Legendary_FlurryBrawlerV18_Animator.controller");
            Assert.IsNotNull(controller, "run VibeGame1/4b. Build Mini-Bosses");
            var states = controller.layers[0].stateMachine.states;
            Assert.AreEqual(18, states.Length);
            var stateNames = new List<string>();
            foreach (var child in states)
            {
                stateNames.Add(child.state.name);
                var clip = child.state.motion as AnimationClip;
                Assert.IsNotNull(clip, child.state.name + " has no AnimationClip motion.");
                Assert.IsFalse(clip.empty, child.state.name + " points to an empty clip.");
            }
            CollectionAssert.AreEquivalent(FlurryBrawlerV18Authoring.ClipAllowlist, stateNames);
        }

        [Test]
        public void Data_IsSeparateAndKeepsTheApprovedInitialVitals()
        {
            var d = Data();
            Assert.IsNotNull(d, "run VibeGame1/3. Create Data");
            Assert.AreEqual("THE FLURRY BRAWLER V18 (TEST)", d.displayName);
            Assert.AreEqual(190f, d.maxHP, 0.001f);
            Assert.AreEqual(160f, d.maxPosture, 0.001f);
            Assert.AreEqual(4f, d.staggerSeconds, 0.001f);
            Assert.AreEqual(1f, d.scale, 0.001f);
            Assert.IsFalse(d.shootsProjectiles);
            Assert.IsFalse(d.rangedOnly);
            Assert.AreNotSame(d, AssetDatabase.LoadAssetAtPath<EnemyData>(
                EnemyPaths.Data("Legendary_FlurryBrawler")));

            var names = new HashSet<string>();
            foreach (var h in EveryHit()) names.Add(h.name);
            CollectionAssert.AreEquivalent(AttackNames, names);
        }

        [Test]
        public void AttackTimingsAndTravel_AreTheApprovedV18Profile()
        {
            AssertAttack("BrawlerV18_Swing", "AttackSwing", 0.55f, 0.05f, 0.20f, 0.75f, 0f);
            AssertAttack("BrawlerV18_Overhead", "AttackOverhead", 0.95f, 0.08f, 0.28f, 1.00f, 0f);
            AssertAttack("BrawlerV18_Stab", "AttackStab", 0.55f, 0.05f, 0.16f, 0.70f, 0f);
            AssertAttack("BrawlerV18_Kick", "AttackKick", 0.65f, 0.05f, 0.22f, 0.90f, 0f);
            AssertAttack("BrawlerV18_Jab2", "Jab2", 0.50f, 0.05f, 0.16f, 0.70f, 0f);
            AssertAttack("BrawlerV18_Dash", "Dash", 0.60f, 0.05f, 0.22f, 1.00f, 1.77f);
            AssertAttack("BrawlerV18_ShoulderCharge", "ShoulderCharge", 1.00f, 0.08f, 0.28f, 1.10f, 4.12f);
            AssertAttack("BrawlerV18_LevitateClap", "Clap", 2.00f, 0.20f, 0.30f, 1.50f, 0f);
            AssertAttack("BrawlerV18_Combo2", "Combo2", 1.50f, 0.05f, 0.20f, 4.60f, 0f);
        }

        [Test]
        public void Clap_IsOneWideScheduledImpact_WithAnExactTouchdown()
        {
            var clap = Atk("BrawlerV18_LevitateClap");
            Assert.IsNotNull(clap);
            Assert.AreEqual(180f, clap.coneDeg, 0.001f);
            Assert.GreaterOrEqual(clap.range, 3.5f);
            Assert.IsTrue(clap.windupPose.authored);
            Assert.AreEqual(1.60f, clap.windupPose.bodyOffset.y, 0.001f);
            Assert.AreEqual(clap.impactDelay,
                (clap.impactDelay + clap.strikeDuration) * 0.40f, 0.0001f,
                "EnemyVisuals descends during the first 40% of Strike; touchdown must equal impact.");

            int clapUses = 0;
            foreach (var entry in Data().moveset.entries)
                foreach (var hit in entry.combo.hits)
                    if (hit == clap) clapUses++;
            Assert.AreEqual(1, clapUses, "Clap must remain one EnemyController-scheduled impact.");
        }

        [Test]
        public void Combo2_IsOneBlockableContact_AndKeepsItsAuthoredTail()
        {
            var combo = Atk("BrawlerV18_Combo2");
            Assert.IsNotNull(combo);
            Assert.IsFalse(combo.unblockable);
            Assert.AreEqual(2.50f, combo.range, 0.001f);
            Assert.AreEqual(70f, combo.coneDeg, 0.001f);
            Assert.AreEqual(20f, combo.damage, 0.001f);
            Assert.AreEqual(0.25f, combo.comboGap, 0.001f);
            Assert.AreEqual(1.25f, combo.parryPostureMultiplier, 0.001f);

            MovesetEntry found = null;
            int uses = 0;
            foreach (var entry in Data().moveset.entries)
            {
                foreach (var hit in entry.combo.hits)
                    if (hit == combo) uses++;
                if (entry.combo.hits.Length == 1 && entry.combo.hits[0] == combo) found = entry;
            }
            Assert.AreEqual(1, uses, "Combo2 may resolve exactly one EnemyController impact.");
            Assert.IsNotNull(found, "Combo2 must be a standalone performance, never an inferred multihit.");
            Assert.AreEqual(0.45f, found.weight, 0.001f);
            Assert.AreEqual(0f, found.minRange, 0.001f);
            Assert.AreEqual(2.70f, found.maxRange, 0.001f);
            Assert.AreEqual(8f, found.cooldown, 0.001f);

            var v = Prefab().GetComponentInChildren<FlurryBrawlerV18Visuals>(true);
            int i = v.IndexOfNamedClip("Combo2");
            Assert.GreaterOrEqual(i, 0);
            float clipLength = v.namedClipLengths[i];
            float contact = v.namedClipHits[i];
            float speed = clipLength * contact / (combo.windup + combo.impactDelay);
            Assert.That(speed, Is.InRange(2.7f, 2.9f));
            Assert.That(speed, Is.InRange(v.minClipSpeed, v.maxClipSpeed));
            float expectedTail = clipLength * (1f - contact) +
                                 FlurryBrawlerV18Visuals.ComboTailSafetySeconds;
            Assert.AreEqual(expectedTail,
                FlurryBrawlerV18Visuals.ComboTailSeconds(clipLength, contact), 0.0001f);
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void PresentationReanchor_ConservesRemainingClipAndClampsClapAtContact(float frame)
        {
            const float now = 10f;
            const float oldImpact = 10.20f;
            float actualImpact = oldImpact + frame;
            const float oldSpeed = 1.7f;
            float speed = FlurryBrawlerV18Visuals.RetimedSpeed(
                oldSpeed, oldImpact, actualImpact, now);
            Assert.AreEqual(oldSpeed * (oldImpact - now),
                speed * (actualImpact - now), 0.0001f);
            Assert.AreEqual(actualImpact - 0.12f,
                FlurryBrawlerV18Visuals.ReanchoredDeadline(
                    oldImpact - 0.12f, oldImpact, actualImpact), 0.0001f);
            Assert.AreEqual(0f, FlurryBrawlerV18Visuals.ClapLiftOffset(
                actualImpact + frame, 8f, 10f, true, now, actualImpact, 1.6f), 0.0001f);
            Vector3 grounded = FlurryBrawlerV18Visuals.ClampToGroundY(
                new Vector3(0.4f, 1.6f, -0.2f), 0.03f);
            Assert.AreEqual(new Vector3(0.4f, 0.03f, -0.2f), grounded,
                "impact callback order must not leave Clap above its authored ground.");
        }

        [Test]
        public void Prefab_UsesOnlyTheV18PresentationAndAnOrdinaryEnemyController()
        {
            var p = Prefab();
            Assert.IsNotNull(p, "run VibeGame1/4a. Split Forge Animation Clips, then 4b. Build Mini-Bosses");
            Assert.IsNotNull(p.GetComponent<EnemyController>());
            Assert.IsNull(p.GetComponent<BossController>());
            Assert.IsNull(p.GetComponentInChildren<ProjectileShooter>(true));
            Assert.IsNull(p.GetComponentInChildren<SentryBurst>(true));
            Assert.IsNull(p.GetComponentInChildren<ProjectileVolleySequence>(true));
            Assert.IsNull(p.GetComponentInChildren<ParrySurge>(true));
            var v = p.GetComponentInChildren<FlurryBrawlerV18Visuals>(true);
            Assert.IsNotNull(v);
            Assert.AreEqual("Idle", v.clipIdle);
            Assert.AreEqual("Jump", v.jumpClip);
            Assert.AreEqual("Block", v.blockClip);
            Assert.IsTrue(string.IsNullOrEmpty(v.spinAttackPrefix));
            Assert.IsNull(p.GetComponentInChildren<EnemyWeaponTrail>(true));
            Assert.IsNotNull(v.travelRoot);
            Assert.IsFalse(v.animator.applyRootMotion);
            int dash = v.IndexOfNamedClip("Dash");
            Assert.GreaterOrEqual(dash, 0, "4b did not bake the explicit eventless Dash profile.");
            Assert.AreEqual(0.60f, v.namedClipHits[dash], 0.0001f);
            int combo = v.IndexOfNamedClip("Combo2");
            Assert.GreaterOrEqual(combo, 0, "4b did not bake the one-contact Combo2 profile.");
            Assert.AreEqual(0.671f, v.namedClipHits[combo], 0.0001f);
        }

        [Test]
        public void SandboxPad_HasAClearChargeLane_AndDoesNotReplaceV15()
        {
            Vector3 p = SandboxBuilder.FlurryBrawlerV18PadPosition;
            const float halfWidth = 2.75f;
            // Pad plus the 6.5 m south approach to the wake switch/player, expanded by a capsule.
            float minX = p.x - halfWidth, maxX = p.x + halfWidth;
            float minZ = p.z - 6.5f - 0.9f, maxZ = p.z + halfWidth;
            Assert.GreaterOrEqual(minX, SandboxBuilder.YardMinX);
            Assert.LessOrEqual(maxX, SandboxBuilder.YardMaxX);
            Assert.GreaterOrEqual(minZ, -SandboxBuilder.YardHalfZ);
            Assert.LessOrEqual(maxZ, SandboxBuilder.YardHalfZ);
            foreach (var b in SandboxBuilder.YardLayout())
            {
                if (b.kind == SandboxBuilder.YardKind.Floor ||
                    b.kind == SandboxBuilder.YardKind.Stripe) continue;
                bool overlap = minX < b.MaxX && maxX > b.MinX && minZ < b.MaxZ && maxZ > b.MinZ;
                Assert.IsFalse(overlap, "V18 charge lane overlaps " + b.name);
            }

            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity", OpenSceneMode.Additive);
            try
            {
                GameObject v18Spawn = null, v18Wake = null, v15Spawn = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "Spawn_Legendary_FlurryBrawlerV18") v18Spawn = t.gameObject;
                        else if (t.name == "Wake_Legendary_FlurryBrawlerV18") v18Wake = t.gameObject;
                        else if (t.name == "Spawn_Legendary_FlurryBrawler") v15Spawn = t.gameObject;
                    }
                }

                Assert.IsNotNull(v18Spawn, "run VibeGame1/7. Build Sandbox Scene");
                Assert.IsNotNull(v18Spawn.GetComponent<EnemySpawner>());
                Assert.IsNotNull(v18Wake, "V18 wake switch missing from rebuilt Sandbox scene.");
                Assert.IsNotNull(v18Wake.GetComponent<SandboxEnemySwitch>());
                Assert.IsNotNull(v15Spawn, "the separate v15 fixture was removed.");
                Assert.IsNotNull(v15Spawn.GetComponent<EnemySpawner>());
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

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
