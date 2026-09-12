using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;
using T = VibeGame1.EditorTools.LevelTraversalAnalyzer;

namespace VibeGame1.Tests
{
    public class LevelTraversalTests
    {
        LevelDefinition def;
        A.MoveProfile profile;

        [SetUp]
        public void Load()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            Assert.IsNotNull(shipped);
            def = Object.Instantiate(shipped);
            LevelDefinitionAuthoring.Apply(def);
            string error;
            Assert.IsTrue(A.TryLoadProfile(out profile, out error), error);
        }
        [TearDown] public void Clean() { Object.DestroyImmediate(def); }

        [Test]
        public void CompleteAuthoringPassIsIdempotent()
        {
            string once = EditorJsonUtility.ToJson(def, true);
            LevelDefinitionAuthoring.Apply(def);
            Assert.AreEqual(once, EditorJsonUtility.ToJson(def, true));
        }

        [Test]
        public void EveryProjectileSpawnStandsOnItsNamedPerch()
        {
            var boxes = A.BoxesFrom(def);
            foreach (var perchDef in LevelDefinitionAuthoring.Perches)
            {
                var perch = boxes.Single(b => b.name == perchDef.name);
                var spawn = def.spawns.Single(s => s.name == perchDef.spawn);
                Assert.That(spawn.position.x, Is.InRange(perch.min.x, perch.max.x), spawn.name);
                Assert.That(spawn.position.z, Is.InRange(perch.min.z, perch.max.z), spawn.name);
                Assert.AreEqual(perch.max.y + 0.1f, spawn.position.y, 0.01f, spawn.name);
            }
        }

        [Test]
        public void WaterLinesAreContainedAndPointDownTheirDecks()
        {
            var boxes = A.BoxesFrom(def);
            Assert.AreEqual(3, def.waters.Length);
            foreach (var water in def.waters)
            {
                var verdict = T.AnalyzeWater(boxes, water);
                Assert.IsTrue(verdict.onADeck, verdict.Summary());
                Assert.IsTrue(verdict.insideDeck, verdict.Summary());
                Assert.IsTrue(verdict.flowIsUnit, verdict.Summary());
            }
            var turn = def.waters.Single(w => w.name == "T3_Water_Turn");
            var step = boxes.Single(b => b.name == "T3_Step_1");
            Vector3 toward = step.Center - turn.center; toward.y = 0f;
            Assert.Less(Vector3.Angle(turn.flowDirection, toward), 25f);
        }

        [Test]
        public void OpeningSurgesRunOnceThenTheHeavyPairLoops()
        {
            var sequence = def.projectileSequences.Single(s => s.name == "T0_SurgeVolley");
            Assert.AreEqual(7, sequence.spawnerNames.Length);
            Assert.AreEqual(5, sequence.repeatFromIndex);
            StringAssert.StartsWith("Spawn_T0_Reliquary_", sequence.spawnerNames[5]);
            StringAssert.StartsWith("Spawn_T0_Reliquary_", sequence.spawnerNames[6]);
        }

        [Test]
        public void RetiredWallSurgeKeysAreMigratedByRole()
        {
            Assert.IsFalse(def.pickups.Any(p => p.itemKey == "WallSurge"));
            Assert.AreEqual("Rebound", def.pickups.Single(p => p.name == "Pickup_Surge_Spawn").itemKey);
            Assert.AreEqual("DeflectSigil", def.pickups.Single(p => p.name == "Pickup_T1_Surge").itemKey);
            Assert.AreEqual("Rebound", def.pickups.Single(p => p.name == "Pickup_T2_Surge").itemKey);
            Assert.AreEqual("DeflectSigil", def.pickups.Single(p => p.name == "Pickup_T3_Surge").itemKey);
            Assert.AreEqual("Rebound", def.pickups.Single(p => p.name == "Pickup_T3_Surge_2").itemKey);
        }

        [Test]
        public void ChallengeRoutesKeepTheExistingFlareSpawnersAndTimingAnchors()
        {
            Assert.AreEqual(3, def.challengeRoutes.Length);
            var t1 = def.challengeRoutes.Single(route => route.routeId == "T1_Challenge_Flare");
            var t2 = def.challengeRoutes.Single(route => route.routeId == "T2_Challenge_Flare");
            var t3 = def.challengeRoutes.Single(route => route.routeId == "T3_Challenge_Flare");

            CollectionAssert.AreEqual(new[] { "Spawn_T1_GruntA", "Spawn_T1_GruntB" }, t1.sourceSpawnerNames);
            CollectionAssert.AreEqual(new[] { "Spawn_T2_GruntB", "Spawn_T2_GruntA" }, t2.sourceSpawnerNames);
            CollectionAssert.AreEqual(new[] { "Spawn_T3_Grunt" }, t3.sourceSpawnerNames);
            Assert.AreEqual(new Vector3(0f, 5f, 37f), t1.entryCenter);
            Assert.AreEqual(new Vector3(42f, 16f, 10f), t1.entrySize);
            Assert.AreEqual(new Vector3(0f, 7f, 70f), t1.rejoinCenter);
            Assert.AreEqual(new Vector3(30f, 20f, 10f), t1.rejoinSize);
            Assert.AreEqual(new Vector3(0f, 13f, 149f), t2.entryCenter);
            Assert.AreEqual(new Vector3(52f, 32f, 12f), t2.entrySize);
            Assert.AreEqual(new Vector3(0f, 22f, 198f), t2.rejoinCenter);
            Assert.AreEqual(new Vector3(32f, 24f, 12f), t2.rejoinSize);
            Assert.AreEqual(new Vector3(0f, 25f, 276f), t3.entryCenter);
            Assert.AreEqual(new Vector3(42f, 28f, 12f), t3.entrySize);
            Assert.AreEqual(new Vector3(0f, 30f, 339f), t3.rejoinCenter);
            Assert.AreEqual(new Vector3(32f, 24f, 12f), t3.rejoinSize);
            Assert.AreEqual("T1.ChallengeRoute.01", t1.meta.objectId);
            Assert.AreEqual("T2.ChallengeRoute.01", t2.meta.objectId);
            Assert.AreEqual("T3.ChallengeRoute.01", t3.meta.objectId);
        }
    }
}
