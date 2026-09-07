using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class LevelDescentTests
    {
        LevelDefinition def;
        [SetUp] public void Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            Assert.IsNotNull(asset, "Level_01 asset missing");
            def = Object.Instantiate(asset);
        }
        [TearDown] public void Clean() { Object.DestroyImmediate(def); }

        [Test] public void ReworkDoesNotMoveBossOrDuplicateEncounterOnRepeat()
        {
            LevelDefinitionAuthoring.Apply(def);
            string once = JsonUtility.ToJson(def);
            LevelDefinitionAuthoring.Apply(def);
            Assert.AreEqual(once, JsonUtility.ToJson(def));
        }

        [Test] public void ShippedDescentHasClearSlidingAndStandingBoltLines()
        {
            Assert.IsEmpty(LevelDescentReport.Failures(def));
        }

        [Test] public void MigratingTheOriginalBossLocationMatchesARepeatedRework()
        {
            LevelDefinitionAuthoring.Apply(def);
            string expected = JsonUtility.ToJson(def);
            // Recreate the pre-descent arena as a unit, then exercise the first migration again.
            var originalOffset = new Vector3(0f, 12f, -70f);
            foreach (var p in def.platforms.Where(p => p.name.Contains("Boss"))) p.center += originalOffset;
            foreach (var s in def.spawns.Where(s => s.name == "Spawn_Boss")) s.position += originalOffset;
            foreach (var p in def.pickups.Where(p => p.name.Contains("Boss"))) p.position += originalOffset;
            foreach (var t in def.torches.Where(t => t.name.StartsWith("Torch_Boss_"))) t.basePosition += originalOffset;
            var arena = def.arenas.Single(a => a.gateName == "Boss_Gate");
            arena.gateOpenPosition += originalOffset;
            arena.gateClosedPosition += originalOffset;
            arena.triggerPosition += originalOffset;
            LevelDefinitionAuthoring.Apply(def);
            Assert.AreEqual(expected, JsonUtility.ToJson(def));
        }

        [Test] public void ShippedBossSouthWallsStillEncloseTheMovedDoorway()
        {
            var arena = def.platforms.Single(p => p.name == "Boss_Arena");
            var gate = def.arenas.Single(a => a.gateName == "Boss_Gate");
            foreach (string side in new[] { "L", "R" })
            {
                var wall = def.platforms.Single(p => p.name == "Wall_Boss_S_" + side);
                Assert.That(wall.center.y - wall.size.y / 2f,
                    Is.EqualTo(arena.center.y + arena.size.y / 2f).Within(0.001f), side + " wall floor");
                Assert.That(wall.center.z + wall.size.z / 2f,
                    Is.EqualTo(arena.center.z - arena.size.z / 2f).Within(0.001f), side + " wall front");
                float innerEdge = Mathf.Abs(wall.center.x) - wall.size.x / 2f;
                Assert.That(innerEdge, Is.EqualTo(gate.gateSize.x / 2f).Within(0.001f), side + " doorway edge");
            }
        }

        [Test] public void RunOutCheckpointGateAndBossMoveTogether()
        {
            var ramp = def.ramps.Single(r => r.name == "T4_Ramp_Descent");
            var deck = def.platforms.Single(p => p.name == "Boss_Approach");
            var checkpoint = def.checkpoints.Single(c => c.name == "Checkpoint_4");
            var arena = def.arenas.Single(a => a.gateName == "Boss_Gate");
            var boss = def.spawns.Single(s => s.name == "Spawn_Boss");
            var entry = def.platforms.Single(p => p.name == "T4_Entry");
            Assert.That(ramp.run, Is.EqualTo(48f).Within(0.001f));
            Assert.That(ramp.rise, Is.EqualTo(-12f).Within(0.001f));
            Assert.That(ramp.width, Is.EqualTo(10f).Within(0.001f));
            Assert.That(ramp.yaw, Is.EqualTo(0f).Within(0.001f));
            Assert.That(deck.size.z, Is.EqualTo(24.4f).Within(0.001f));
            Assert.That(entry.size.x, Is.GreaterThanOrEqualTo(ramp.width));
            Assert.That(deck.size.x, Is.GreaterThanOrEqualTo(ramp.width));
            Assert.That(ramp.basePosition.y, Is.EqualTo(entry.center.y + entry.size.y / 2f).Within(0.001f));
            Assert.That(entry.center.z + entry.size.z / 2f - ramp.basePosition.z, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(ramp.TopPosition.z - (deck.center.z - deck.size.z / 2f), Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(ramp.TopPosition.y, Is.EqualTo(deck.center.y + deck.size.y / 2f).Within(0.001f));
            Assert.That(checkpoint.position.y, Is.EqualTo(ramp.TopPosition.y).Within(0.001f));
            Assert.That(checkpoint.position.z, Is.GreaterThan(ramp.TopPosition.z));
            Assert.That(checkpoint.position.z, Is.LessThan(arena.gateClosedPosition.z));
            Assert.That(boss.position.z, Is.GreaterThan(arena.triggerPosition.z));
            Assert.That(boss.position.y, Is.EqualTo(ramp.TopPosition.y + 0.1f).Within(0.01f));
            Assert.That(def.killZone.center.z + def.killZone.size.z / 2f, Is.GreaterThan(boss.position.z + 30f));
        }

        [Test] public void ReportReadsTheShippedSlopeAndDetectsAnAddedBoltObstruction()
        {
            var ramp = def.ramps.Single(r => r.name == "T4_Ramp_Descent");
            Assert.That(LevelDescentReport.SurfaceY(ramp, ramp.basePosition + ramp.Heading * 24f), Is.EqualTo(22f).Within(0.001f));
            ramp.basePosition += Vector3.up * 3f;
            Assert.That(LevelDescentReport.SurfaceY(ramp, ramp.basePosition + ramp.Heading * 24f), Is.EqualTo(25f).Within(0.001f));
            ramp.basePosition -= Vector3.up * 3f;
            var blocker = new PlatformDef { name = "Test_BoltObstruction", center = new Vector3(4f, 24f, 310f),
                size = new Vector3(1f, 20f, 50f) };
            def.platforms = def.platforms.Concat(new[] { blocker }).ToArray();
            Assert.That(LevelDescentReport.Failures(def), Has.Some.Contains("Test_BoltObstruction"));
        }

        [Test] public void ReportSkipsLevelsWithoutTheOptionalDescent()
        {
            def.ramps = new RampDef[0];
            StringAssert.Contains("not authored", LevelDescentReport.Build(def));
        }

        [Test] public void SlabIntersectionSeesSlopeButNotEmptySpaceAboveIt()
        {
            var ramp = new RampDef { basePosition = Vector3.zero, run = 10f, rise = -5f };
            Assert.IsTrue(LevelDescentReport.Intersects(new Vector3(0, 2, 5), new Vector3(0, -4, 5), ramp.BoxCenter, ramp.BoxScale, ramp.Rotation));
            Assert.IsFalse(LevelDescentReport.Intersects(new Vector3(-3, 1, 5), new Vector3(3, 1, 5), ramp.BoxCenter, ramp.BoxScale, ramp.Rotation));
        }
    }
}
