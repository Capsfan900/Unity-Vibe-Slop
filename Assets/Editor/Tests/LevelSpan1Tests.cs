using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;
using T = VibeGame1.EditorTools.LevelTraversalAnalyzer;

namespace VibeGame1.Tests
{
    [Category("LevelLines")]
    public class LevelSpan1Tests
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
        public void MainLineIsFiveBroadLandingsAndAnOpenWaterShoulder()
        {
            AssertDeck("T1_Stone_1", new Vector3(0f, -0.5f, 13f), new Vector3(12f, 1f, 8f));
            AssertDeck("T1_Stone_2", new Vector3(4f, 0f, 22f), new Vector3(12f, 1f, 8f));
            AssertDeck("T1_Stone_3", new Vector3(-4f, 0.5f, 31f), new Vector3(12f, 1f, 8f));
            AssertDeck("T1_Stone_4", new Vector3(0f, 1f, 40.5f), new Vector3(14f, 1f, 9f));
            AssertDeck("T1_Causeway", new Vector3(0f, 1.5f, 51.6f), new Vector3(14f, 1f, 12.8f));
            AssertDeck("T1_Fast_1", new Vector3(10f, 0f, 25f), new Vector3(8f, 1f, 12f));
        }

        [Test]
        public void WaterShoulderAndWestSentryKeepTheirAuditedOwnershipAndClearance()
        {
            var boxes = A.BoxesFrom(def);
            var water = def.waters.Single(w => w.name == "T1_Water_Fast");
            var waterVerdict = T.AnalyzeWater(boxes, water);
            Assert.AreEqual("T1_Fast_1", waterVerdict.deck,
                "the sheet centre must not alias the coplanar edge of T1_Stone_2");
            Assert.IsTrue(waterVerdict.insideDeck, waterVerdict.Summary());

            var west = LevelDefinitionAuthoring.Perches.Single(p => p.name == "T1_Perch_W");
            Assert.That(Vector3.Distance(west.center, new Vector3(-23f, 3.5f, 58.5f)), Is.LessThan(0.001f),
                "the west perch must remain a forward flank outside the cyan sun silhouette");
            var east = LevelDefinitionAuthoring.Perches.Single(p => p.name == "T1_Perch_E");
            Assert.That(Vector3.Distance(east.center, new Vector3(4f, 3.5f, 62f)), Is.LessThan(0.001f),
                "the east perch must remain ahead of the runway with both restored wall faces clear");
        }

        [Test]
        public void HybridSilhouetteIsRestoredBesideTheOpenLine()
        {
            string[] restored = { "T1_Rail_L", "T1_Rail_R", "T1_Obelisk_W", "T1_Fallen_Obelisk",
                                  "T1_Wall_Start", "T1_Wall_Causeway", "T1_Wall_Landing" };
            foreach (string name in restored)
            {
                var want = LevelDefinitionAuthoring.HybridCourseStructures.Single(s => s.name == name);
                var got = def.platforms.Single(p => p.name == name);
                Assert.That(Vector3.Distance(got.center, want.center), Is.LessThan(0.001f), name + " center");
                Assert.That(Vector3.Distance(got.size, want.size), Is.LessThan(0.001f), name + " size");
            }

            // The low rails and alternate wall line frame the broad route instead of reclaiming its
            // middle. The fallen obelisk is deliberately excluded: it is the authored slide-under gate.
            foreach (string name in new[] { "T1_Rail_L", "T1_Rail_R", "T1_Obelisk_W",
                                             "T1_Wall_Start", "T1_Wall_Causeway", "T1_Wall_Landing" })
                AssertOutsideCenterStrip(name, 3f);
        }

        [Test]
        public void EveryMainHopHasSeveralGroundSpeedLaunchPoints()
        {
            var boxes = A.BoxesFrom(def);
            string[] route = { "T1_Stone_1", "T1_Stone_2", "T1_Stone_3", "T1_Stone_4", "T1_Causeway" };
            for (int i = 0; i + 1 < route.Length; i++)
            {
                var hop = A.AnalyzeHop(boxes, route[i], route[i + 1], profile, profile.groundSpeed, def.killZone.center.y);
                Assert.IsTrue(hop.exists, hop.Summary());
                Assert.GreaterOrEqual(hop.cleanLaunchPoints, 3, hop.Summary());
            }
        }

        [Test]
        public void BothSentriesHaveClearInBandShotsAcrossTheMainRunway()
        {
            var boxes = A.BoxesFrom(def);
            var stats = AssetDatabase.LoadAssetAtPath<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            Assert.IsNotNull(stats);
            System.Func<string, string[]> routeOf = deck =>
            {
                string previous = null, next = null;
                foreach (var link in LevelArcReport.BaseRoute)
                {
                    if (link.to == deck) previous = link.from;
                    if (link.from == deck) next = link.to;
                }
                return previous == null && next == null ? null : new[] { previous, next };
            };
            foreach (var perch in LevelDefinitionAuthoring.Perches.Where(p => p.name.StartsWith("T1_")))
            {
                var verdict = T.AnalyzeShooterPlacement(boxes, perch.name, perch.spawn,
                    perch.covers.Split(','), 10f, 32f, stats.facingConeDeg, routeOf);
                Assert.AreEqual(perch.covers.Split(',').Length, verdict.covered.Count, verdict.Summary());
                Assert.AreEqual(0, verdict.forcedLookAway.Count,
                    "an integrated parry-route shot must be answerable without turning away from movement: " + verdict.Summary());
            }
        }

        void AssertDeck(string name, Vector3 center, Vector3 size)
        {
            var p = def.platforms.Single(x => x.name == name);
            Assert.That(Vector3.Distance(p.center, center), Is.LessThan(0.001f), name + " center");
            Assert.That(Vector3.Distance(p.size, size), Is.LessThan(0.001f), name + " size");
        }

        void AssertOutsideCenterStrip(string name, float halfWidth)
        {
            var p = def.platforms.Single(x => x.name == name);
            float nearestX = Mathf.Abs(p.center.x) - p.size.x * 0.5f;
            Assert.GreaterOrEqual(nearestX, halfWidth,
                name + " reaches into the broad central lane instead of staying on its shoulder");
        }
    }
}
