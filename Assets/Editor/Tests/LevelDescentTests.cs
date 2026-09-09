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

        [Test] public void ShippedStartIsOnTheOpeningCrestBeforeTheEntireOriginalLevel()
        {
            var entry = def.platforms.Single(p => p.name == "T0_Entry");
            var opening = def.ramps.Single(r => r.name == "T0_Ramp_Descent");
            var originalStart = def.platforms.Single(p => p.name == "Ground_Start");
            var pedestal = def.pedestals.Single(p => p.name == "WandPedestal_Start");
            Assert.That(def.playerStart.x, Is.InRange(entry.center.x - entry.size.x / 2f + 1f,
                entry.center.x + entry.size.x / 2f - 1f));
            Assert.That(def.playerStart.z, Is.InRange(entry.center.z - entry.size.z / 2f + 1f,
                opening.basePosition.z - 1f));
            Assert.That(def.playerStart.y, Is.EqualTo(entry.center.y + entry.size.y / 2f + 0.3f).Within(0.001f));
            Assert.That(def.playerStartYaw, Is.EqualTo(opening.yaw));
            Assert.That(opening.TopPosition.z, Is.LessThan(originalStart.center.z - originalStart.size.z / 2f));
            Assert.That(pedestal.groundPosition.y, Is.EqualTo(entry.center.y + entry.size.y / 2f).Within(0.001f));
            Assert.That(pedestal.groundPosition.z, Is.EqualTo(def.playerStart.z).Within(0.001f));
            Assert.That(Mathf.Abs(pedestal.groundPosition.x - def.playerStart.x), Is.InRange(2f, 4f));
            Assert.That(def.checkpoints.Min(c => c.position.z), Is.GreaterThan(opening.TopPosition.z),
                "The opening must not begin beyond an existing checkpoint.");
            Assert.That(def.killZone.center.z - def.killZone.size.z / 2f,
                Is.LessThan(entry.center.z - entry.size.z / 2f - 10f));
        }

        /// <summary>
        /// The hill's job after the last parry. The five-beat ladder ends around progress 97; everything
        /// below that is the exhale, where a 1.60x surge at ~24 m/s is actually felt before the run-out
        /// hands the player to the unchanged Ground_Start. Authored as a floor, not an exact number.
        /// </summary>
        [Test] public void OpeningLeavesAnUnopposedPayoffStraightBelowTheLastPerch()
        {
            var ramp = def.ramps.Single(r => r.name == "T0_Ramp_Descent");
            var perches = def.spawns.Where(s => s.name.StartsWith("Spawn_T0_Surge_")).ToArray();
            Assert.That(perches.Length, Is.EqualTo(5));
            float lowest = perches.Max(s => s.position.z);
            Assert.That(ramp.TopPosition.z - lowest, Is.GreaterThanOrEqualTo(28f),
                "no room below the last perch to feel the surge before the level flattens");
            foreach (var s in perches)
                Assert.That(s.position.z, Is.GreaterThan(ramp.basePosition.z),
                    s.name + " sits above the crest lip, off the hill");
        }

        [Test] public void ShippedOpeningHasContinuousFullWidthJoinsIntoTheOriginalStart()
        {
            var ramp = def.ramps.Single(r => r.name == "T0_Ramp_Descent");
            var entry = def.platforms.Single(p => p.name == "T0_Entry");
            var runOut = def.platforms.Single(p => p.name == "T0_RunOut");
            var originalStart = def.platforms.Single(p => p.name == "Ground_Start");
            // 2026-09-07: lengthened at the top, 120 -> 144 m of run, 30 -> 36 m of drop. The grade and
            // the bottom of the hill are both fixed, so the whole extension lands above the lip.
            Assert.That(ramp.run, Is.EqualTo(144f));
            Assert.That(ramp.rise, Is.EqualTo(-36f));
            Assert.That(ramp.width, Is.EqualTo(14f));
            Assert.That(ramp.yaw, Is.Zero);
            Assert.That(ramp.TopPosition.z, Is.EqualTo(-15.8f).Within(0.001f), "the bottom of the hill is pinned");
            Assert.That(ramp.TopPosition.y, Is.EqualTo(0f).Within(0.001f), "the bottom of the hill is pinned");
            Assert.That(entry.center.y + entry.size.y / 2f, Is.EqualTo(ramp.basePosition.y).Within(0.001f));
            Assert.That(entry.center.z + entry.size.z / 2f - ramp.basePosition.z, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(ramp.TopPosition.z - (runOut.center.z - runOut.size.z / 2f), Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(runOut.center.y + runOut.size.y / 2f, Is.EqualTo(ramp.TopPosition.y).Within(0.001f));
            Assert.That(runOut.center.y + runOut.size.y / 2f,
                Is.EqualTo(originalStart.center.y + originalStart.size.y / 2f).Within(0.001f));
            Assert.That(runOut.center.z + runOut.size.z / 2f - (originalStart.center.z - originalStart.size.z / 2f),
                Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(entry.size.x, Is.GreaterThanOrEqualTo(ramp.width));
            Assert.That(runOut.size.x, Is.GreaterThanOrEqualTo(ramp.width));
            Assert.That(originalStart.size.x, Is.GreaterThanOrEqualTo(runOut.size.x));
            Assert.That(entry.center.x, Is.EqualTo(ramp.basePosition.x));
            Assert.That(runOut.center.x, Is.EqualTo(ramp.TopPosition.x));
            Assert.That(originalStart.center.x, Is.EqualTo(runOut.center.x));
        }

        [Test] public void OpeningMigrationRepairsTheLateSpawnWithoutMovingTheExistingCourse()
        {
            var originalPlatforms = def.platforms.Where(p => !p.name.StartsWith("T0_")).ToArray();
            var platformData = originalPlatforms.Select(p => JsonUtility.ToJson(p)).ToArray();
            var existingRamps = def.ramps.Where(r => r.name != "T0_Ramp_Descent").ToArray();
            var rampData = existingRamps.Select(r => JsonUtility.ToJson(r)).ToArray();
            var checkpointData = def.checkpoints.Select(c => JsonUtility.ToJson(c)).ToArray();
            var spawnData = def.spawns.Select(s => JsonUtility.ToJson(s)).ToArray();
            def.playerStart = new Vector3(0f, 28.3f, 297.5f);
            LevelDefinitionAuthoring.ApplyOpeningDescent(def);
            Assert.That(def.playerStart.z, Is.LessThan(-8f));
            CollectionAssert.AreEqual(platformData, def.platforms.Where(p => !p.name.StartsWith("T0_"))
                .Select(p => JsonUtility.ToJson(p)).ToArray());
            CollectionAssert.AreEqual(rampData, def.ramps.Where(r => r.name != "T0_Ramp_Descent")
                .Select(r => JsonUtility.ToJson(r)).ToArray());
            CollectionAssert.AreEqual(checkpointData, def.checkpoints.Select(c => JsonUtility.ToJson(c)).ToArray());
            CollectionAssert.AreEqual(spawnData, def.spawns.Select(s => JsonUtility.ToJson(s)).ToArray());
            string once = JsonUtility.ToJson(def);
            LevelDefinitionAuthoring.ApplyOpeningDescent(def);
            Assert.AreEqual(once, JsonUtility.ToJson(def));
        }

        [Test] public void ShippedLevelHasTwoLargeDownhillRampsOnOppositeSidesOfTheOriginalCourse()
        {
            var descents = def.ramps.Where(r => r.rise < 0f && r.run >= 30f).OrderBy(r => r.basePosition.z).ToArray();
            Assert.That(descents.Length, Is.EqualTo(2));
            Assert.That(descents[0].name, Is.EqualTo("T0_Ramp_Descent"));
            Assert.That(descents[1].name, Is.EqualTo("T4_Ramp_Descent"));
            Assert.That(descents[0].TopPosition.z, Is.LessThan(-8f));
            Assert.That(descents[1].basePosition, Is.EqualTo(new Vector3(0f, 28f, 394.8f)),
                "the final descent moves with the widened T4/boss section");
        }

        [Test] public void SolarBossSpawnerKeepsItsHistoricalAuthoringAnchorAcrossRework()
        {
            var boss = def.spawns.Single(s => s.name == "Spawn_Boss");
            string historicalAnchor = JsonUtility.ToJson(boss);
            LevelDefinitionAuthoring.Apply(def);
            var arena = def.arenas.Single(a => a.gateName == "Boss_Gate");
            Assert.AreEqual(historicalAnchor, JsonUtility.ToJson(def.spawns.Single(s => s.name == "Spawn_Boss")),
                "the builder relocates this marker into the solar realm; spacing migrations must not drift it");
            Assert.AreEqual("Spawn_Boss", arena.solarRealm.enemySpawnerName);
        }

        [Test] public void ShippedBossApproachStopsOutsideThePortalSun()
        {
            var approach = def.platforms.Single(p => p.name == "Boss_Approach");
            var gate = def.arenas.Single(a => a.gateName == "Boss_Gate");
            float approachEnd = approach.center.z + approach.size.z * 0.5f;
            float shellNear = gate.solarRealm.exteriorCenter.z - gate.solarRealm.visualRadius;
            Assert.GreaterOrEqual(shellNear - approachEnd, 1f,
                "the boss approach must stop before the visible sun rather than disappearing inside it");
            Assert.Less(gate.gateClosedPosition.z,
                gate.solarRealm.exteriorCenter.z - gate.solarRealm.exteriorRadius,
                "the doorway stays on the approach side of the physical portal boundary");
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
            Assert.That(deck.size.z, Is.EqualTo(11.4f).Within(0.001f));
            Assert.That(entry.size.x, Is.GreaterThanOrEqualTo(ramp.width));
            Assert.That(deck.size.x, Is.GreaterThanOrEqualTo(ramp.width));
            Assert.That(ramp.basePosition.y, Is.EqualTo(entry.center.y + entry.size.y / 2f).Within(0.001f));
            Assert.That(entry.center.z + entry.size.z / 2f - ramp.basePosition.z, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(ramp.TopPosition.z - (deck.center.z - deck.size.z / 2f), Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(ramp.TopPosition.y, Is.EqualTo(deck.center.y + deck.size.y / 2f).Within(0.001f));
            Assert.That(checkpoint.position.y, Is.EqualTo(ramp.TopPosition.y).Within(0.001f));
            Assert.That(checkpoint.position.z, Is.GreaterThan(ramp.TopPosition.z));
            Assert.That(checkpoint.position.z, Is.LessThan(arena.gateClosedPosition.z));
            Assert.That(arena.solarRealm.enemySpawnerName, Is.EqualTo(boss.name));
            Vector2 realmDelta = new Vector2(arena.solarRealm.enemySpawnPosition.x - arena.solarRealm.realmCenter.x,
                                             arena.solarRealm.enemySpawnPosition.z - arena.solarRealm.realmCenter.z);
            Assert.That(realmDelta.magnitude, Is.LessThan(arena.solarRealm.realmFloorRadius),
                "the physical boss spawn belongs inside the disconnected realm, not beyond the route trigger");
            Assert.That(def.killZone.center.z + def.killZone.size.z / 2f,
                Is.GreaterThan(arena.solarRealm.exteriorCenter.z + arena.solarRealm.visualRadius + 30f));
        }

        [Test] public void ReportReadsTheShippedSlopeAndDetectsAnAddedBoltObstruction()
        {
            var ramp = def.ramps.Single(r => r.name == "T4_Ramp_Descent");
            Assert.That(LevelDescentReport.SurfaceY(ramp, ramp.basePosition + ramp.Heading * 24f), Is.EqualTo(22f).Within(0.001f));
            ramp.basePosition += Vector3.up * 3f;
            Assert.That(LevelDescentReport.SurfaceY(ramp, ramp.basePosition + ramp.Heading * 24f), Is.EqualTo(25f).Within(0.001f));
            ramp.basePosition -= Vector3.up * 3f;
            Vector3 midpoint = ramp.basePosition + ramp.Heading * (ramp.run * 0.5f);
            midpoint.y = LevelDescentReport.SurfaceY(ramp, midpoint);
            var blocker = new PlatformDef { name = "Test_BoltObstruction", center = midpoint + Vector3.right * 4f,
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
