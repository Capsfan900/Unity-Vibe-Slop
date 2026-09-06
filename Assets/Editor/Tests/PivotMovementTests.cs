using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The 2026-09-04 pivot's first three traversal pieces — balloons, water and the grapple exit
    /// burst — pinned in numbers before anyone stands on them.
    ///
    /// <para>Three layers, like <c>AirFeelTests</c>: the pure arithmetic in <see cref="TraversalMath"/>
    /// (no scene), the SHIPPED values on the prefabs and the feel asset (hard rule 9 — and, per the
    /// ENGINEERING-LOG's lesson, the asset checks grep the YAML for the key first and Ignore until the
    /// generators have actually run, because a missing key deserialises to the field initialiser and
    /// passes a value test on exactly the broken case), and the level-definition round trip through the
    /// exporter.</para>
    /// </summary>
    public class PivotMovementTests
    {
        const float Eps = 0.001f;

        // ---- balloon --------------------------------------------------------------------------------

        [Test]
        public void ALaunchReplacesTheVerticalAndKeepsTheHorizontal()
        {
            var v = TraversalMath.Launch(new Vector3(9f, -18f, 4f), 14f);
            Assert.AreEqual(9f, v.x, Eps); Assert.AreEqual(4f, v.z, Eps);
            Assert.AreEqual(14f, v.y, Eps, "a capped jump: the fall into the orb is discarded, not added");
            var rising = TraversalMath.Launch(new Vector3(0f, 25f, 0f), 14f);
            Assert.AreEqual(14f, rising.y, Eps, "rising into it is capped too, so the height is authorable");
            Assert.AreEqual(0f, TraversalMath.Launch(Vector3.zero, -5f).y, Eps, "a negative launch is clamped");
        }

        [Test]
        public void APopTrimsTheCarryToTheCap_SoTheNextOrbIsAimable()
        {
            // 2026-09-05: a 22 m/s dash carried through orb 1 sailed 16 m past orb 2. The carry is capped;
            // the re-armed dash is what covers the gap.
            var v = TraversalMath.Launch(new Vector3(22f, 0f, 0f), 11f, 9f);
            Assert.AreEqual(9f, new Vector2(v.x, v.z).magnitude, Eps);
            Assert.AreEqual(11f, v.y, Eps);
            var slow = TraversalMath.Launch(new Vector3(3f, 0f, 4f), 11f, 9f);
            Assert.AreEqual(5f, new Vector2(slow.x, slow.z).magnitude, Eps, "under the cap nothing is trimmed");
            var motor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (motor == null) Assert.Ignore("Player.prefab not built yet");
            var fpm = motor.GetComponent<FirstPersonMotor>();
            Assert.Greater(fpm.launchCarryCap, 0f, "launchCarryCap not shipped (rule 9)");
            Assert.Less(fpm.launchCarryCap, fpm.dashSpeed, "the cap must be below the dash or it trims nothing");
        }

        [Test]
        public void ADashGoesThroughAnOrb_AnyoneElseIsLaunched()
        {
            Assert.IsTrue(TraversalMath.DashesThrough(true));
            Assert.IsFalse(TraversalMath.DashesThrough(false));
        }

        // ---- water ----------------------------------------------------------------------------------

        [Test]
        public void WaterLiftsAWalkerToTheFloorSpeedAlongTheStick()
        {
            Vector3 rel = Vector3.forward * 5f;
            for (int i = 0; i < 200; i++) rel = TraversalMath.WaterStep(rel, Vector3.forward, 14.85f, 30f, 1f / 60f);
            Assert.AreEqual(14.85f, rel.magnitude, 0.01f);
            Assert.Greater(Vector3.Dot(rel.normalized, Vector3.forward), 0.999f);
        }

        [Test]
        public void WaterNeverSlowsAnyone()
        {
            // Faster than the floor: kept exactly, no friction, no overspeed decay.
            Vector3 rel = Vector3.forward * 20f;
            for (int i = 0; i < 100; i++) rel = TraversalMath.WaterStep(rel, Vector3.forward, 14.85f, 30f, 1f / 60f);
            Assert.AreEqual(20f, rel.magnitude, Eps);
            // No stick: the heading is held and the speed is lifted to the floor, never dropped.
            rel = Vector3.right * 9f;
            for (int i = 0; i < 100; i++) rel = TraversalMath.WaterStep(rel, Vector3.zero, 14.85f, 30f, 1f / 60f);
            Assert.AreEqual(14.85f, rel.magnitude, 0.01f);
            Assert.Greater(Vector3.Dot(rel.normalized, Vector3.right), 0.999f);
        }

        [Test]
        public void StillWaterDoesNotPullAStationaryBodyIn()
        {
            var rel = TraversalMath.WaterStep(Vector3.zero, Vector3.zero, 14.85f, 30f, 1f / 60f);
            Assert.AreEqual(0f, rel.magnitude, Eps, "water carries you; it does not start you moving");
        }

        [Test]
        public void TurningOnWaterIsASkate_RateLimitedBelowTheGround()
        {
            // One frame of a hard 90 deg turn at the water's 30 m/s^2 moves the velocity by at most
            // accel x dt; the ground's 90 would move it three times as far.
            Vector3 rel = Vector3.forward * 14.85f;
            var next = TraversalMath.WaterStep(rel, Vector3.right, 14.85f, 30f, 1f / 60f);
            Assert.LessOrEqual((next - rel).magnitude, 30f / 60f + Eps);
            Assert.Greater((next - rel).magnitude, 0.4f, "it must still turn");
        }

        [Test]
        public void TheFlowIsAConveyorAddedOnTop()
        {
            var flow = TraversalMath.Flow(new Vector3(2f, 5f, 0f), 6f);
            Assert.AreEqual(6f, flow.magnitude, Eps, "normalised and flattened");
            Assert.AreEqual(0f, flow.y, Eps);
            Assert.AreEqual(Vector3.zero, TraversalMath.Flow(Vector3.zero, 6f), "zero direction = still water");
            Assert.AreEqual(Vector3.zero, TraversalMath.Flow(Vector3.right, 0f));
            var world = TraversalMath.WaterVelocity(Vector3.forward * 14.85f, flow);
            Assert.AreEqual(6f, world.x, Eps); Assert.AreEqual(14.85f, world.z, Eps); Assert.AreEqual(0f, world.y, Eps);
        }

        // ---- the grapple burst ----------------------------------------------------------------------

        [Test]
        public void TheBurstIsFasterThanADash_AndExactlyTheSpeedCeiling()
        {
            Assert.AreEqual(27.5f, TraversalMath.BurstSpeed(22f, 1.25f), Eps,
                "22 x 1.25 = 27.5 = maxHorizontalSpeed: the burst is the fastest legal thing, and nothing faster");
            Assert.AreEqual(0f, TraversalMath.BurstSpeed(22f, -1f), Eps);
        }

        [Test]
        public void TheBurstWindowClosesOnTheMotorClock()
        {
            Assert.IsTrue(TraversalMath.BurstOpen(10f, 10.3f));
            Assert.IsFalse(TraversalMath.BurstOpen(10.3f, 10.3f));
            Assert.IsFalse(TraversalMath.BurstOpen(0f, -99f), "-99 is the closed sentinel");
        }

        // ---- shipped values -------------------------------------------------------------------------

        static bool YamlHas(string assetPath, string key)
        {
            if (!File.Exists(assetPath)) return false;
            return File.ReadAllText(assetPath).Contains(key);
        }

        [Test]
        public void ThePlayerPrefabShipsTheTraversalNumbers()
        {
            const string path = "Assets/Prefabs/Player.prefab";
            if (!YamlHas(path, "waterSpeedScale:"))
                Assert.Ignore("Player.prefab has not been rebuilt since the pivot — run VibeGame1/4. Build Prefabs. " +
                              "(A missing key deserialises to the field initialiser, so asserting values now would prove nothing.)");
            var motor = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<FirstPersonMotor>();
            Assert.AreEqual(1.35f, motor.waterSpeedScale, Eps);
            Assert.AreEqual(30f, motor.waterAccel, Eps);
            Assert.AreEqual(0.15f, motor.waterGrace, Eps);
            Assert.AreEqual(0.30f, motor.pullBurstWindow, Eps);
            Assert.AreEqual(1.25f, motor.pullBurstMultiplier, Eps);
            Assert.AreEqual(3f, motor.pullBurstHold, Eps);
            Assert.AreEqual(motor.maxHorizontalSpeed, TraversalMath.BurstSpeed(motor.dashSpeed, motor.pullBurstMultiplier), 0.01f,
                "the burst must land exactly on the speed ceiling; above it the ceiling clips it, below it it is just a dash");
            Assert.Less(motor.groundSpeed * motor.waterSpeedScale, motor.dashSpeed,
                "the water floor must stay under a dash or the air kit stops mattering on water");
        }

        [Test]
        public void TheBalloonPrefabShipsItsNumbers_OnInteractable_AsATrigger()
        {
            const string path = "Assets/Prefabs/Balloon.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Assert.Ignore("Balloon.prefab is not built yet — run VibeGame1/4. Build Prefabs.");
            var b = prefab.GetComponent<Balloon>();
            Assert.IsNotNull(b, "no Balloon component resolves — a missing script is silent at runtime");
            Assert.AreEqual(11f, b.launchSpeed, Eps);
            Assert.AreEqual(2.5f, b.respawnSeconds, Eps);
            Assert.AreEqual(1.1f, b.radius, Eps);
            Assert.IsNotNull(b.visual, "no visual to hide on a pop");
            var col = prefab.GetComponent<SphereCollider>();
            Assert.IsNotNull(col); Assert.IsTrue(col.isTrigger); Assert.AreEqual(1.1f, col.radius, Eps);
            Assert.AreEqual(Layers.Interactable, prefab.layer, "off Default, so the NavMesh bake and the wall casts ignore it");
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                StringAssert.StartsWith("Universal Render Pipeline/", r.sharedMaterial.shader.name, r.name + " is not URP");
            Assert.Less(b.popColor.maxColorComponent, 1.05f, "the orb's own colour stays under the bloom threshold");
        }

        [Test]
        public void TheFeelAssetShipsTheTraversalKicks()
        {
            const string path = "Assets/Data/GameFeel.asset";
            if (!YamlHas(path, "balloonFovKick:"))
                Assert.Ignore("GameFeel.asset has not been regenerated since the pivot — run VibeGame1/3. Create Data.");
            var feel = AssetDatabase.LoadAssetAtPath<GameFeelSettings>(path);
            Assert.AreEqual(5f, feel.balloonFovKick, Eps);
            Assert.AreEqual(1.5f, feel.balloonKickPitch, Eps);
            Assert.AreEqual(6f, feel.burstFovKick, Eps);
            Assert.AreEqual(3f, feel.waterEnterFovKick, Eps);
            Assert.AreEqual(26f, feel.waterSprayRate, Eps);
            Assert.AreEqual(0.14f, feel.waterHissVolume, Eps);
            Assert.Greater(feel.balloonFovKick, feel.jumpFovKickOrDefault(), "a launch is bigger than a jump");
            Assert.Less(feel.balloonFovKick, feel.dashFovKick, "and smaller than a dash: it is not a punctuation mark");
        }

        // ---- the definition round trip --------------------------------------------------------------

        [Test]
        public void WaterAndBalloonsSurviveTheExporter()
        {
            var root = new GameObject("Level_RoundTrip");
            try
            {
                var water = TraversalBuilders.BuildWater("Water_Test", new Vector3(10f, 0.02f, -4f), new Vector3(12f, 0.04f, 5f),
                                                         new Vector3(0f, 0f, 1f), 6f, null, root.transform);
                Assert.IsNotNull(water.GetComponent<WaterVolume>());
                Assert.AreEqual(Layers.Interactable, water.layer);
                var trig = water.GetComponent<BoxCollider>();
                Assert.IsTrue(trig.isTrigger);
                Assert.AreEqual(0.04f + 0.35f, trig.size.y, Eps, "the trigger reaches boostHeight above the sheet");
                foreach (var c in water.GetComponentsInChildren<Collider>())
                    Assert.AreSame(trig, c, "the sheet must have no solid collider — the floor under it is what you stand on");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Balloon.prefab");
                GameObject orb = null;
                if (prefab != null)
                    orb = TraversalBuilders.BuildBalloon(prefab, "Balloon_Test", new Vector3(3f, 4f, 5f), 16f, 1.5f, 0.9f, root.transform);

                var def = ScriptableObject.CreateInstance<LevelDefinition>();
                try
                {
                    LevelDefinitionExporter.ExportInto(root, def);
                    Assert.AreEqual(1, def.waters.Length, "the water sheet was not exported");
                    var w = def.waters[0];
                    Assert.AreEqual("Water_Test", w.name);
                    Assert.AreEqual(new Vector3(10f, 0.02f, -4f), w.center);
                    Assert.AreEqual(new Vector3(12f, 0.04f, 5f), w.size);
                    Assert.AreEqual(6f, w.flowSpeed, Eps);
                    Assert.Greater(Vector3.Dot(w.flowDirection.normalized, Vector3.forward), 0.999f);
                    Assert.AreEqual(0, def.platforms.Length, "the sheet's surface mesh must not be exported as geometry");

                    if (orb != null)
                    {
                        Assert.AreEqual(1, def.balloons.Length, "the balloon was not exported");
                        var b = def.balloons[0];
                        Assert.AreEqual("Balloon_Test", b.name);
                        Assert.AreEqual(new Vector3(3f, 4f, 5f), b.position);
                        Assert.AreEqual(16f, b.launchSpeed, Eps);
                        Assert.AreEqual(1.5f, b.respawnSeconds, Eps);
                        Assert.AreEqual(0.9f, b.radius, Eps);
                        Assert.AreEqual(0.9f, orb.GetComponent<SphereCollider>().radius, Eps, "the placed radius must reach the collider");
                    }
                }
                finally { Object.DestroyImmediate(def); }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }

    static class GameFeelSettingsTestExtensions
    {
        /// <summary>The jump's FOV kick lives on PlayerFeedback (2.5 by initialiser), not on the feel asset.</summary>
        public static float jumpFovKickOrDefault(this GameFeelSettings feel) { return 2.5f; }
    }
}
