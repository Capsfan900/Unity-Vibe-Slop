using System.Linq;
using System.Collections.Generic;
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
            Assert.That(Vector3.Distance(west.center, new Vector3(-24f, 4.5f, 60f)), Is.LessThan(0.001f),
                "the west perch must remain a raised forward flank outside the cyan sun silhouette");
            var east = LevelDefinitionAuthoring.Perches.Single(p => p.name == "T1_Perch_E");
            Assert.That(Vector3.Distance(east.center, new Vector3(6.8f, 5f, 62f)), Is.LessThan(0.001f),
                "the east perch stays raised on the east shoulder, ahead of the runway");
        }

        [Test]
        public void HybridSilhouetteIsRestoredBesideTheOpenLine()
        {
            string[] restored = { "T1_Rail_L", "T1_Rail_R", "T1_Obelisk_W",
                                  "T1_Wall_Start", "T1_Wall_Causeway", "T1_Wall_Landing" };
            foreach (string name in restored)
            {
                var want = LevelDefinitionAuthoring.HybridCourseStructures.Single(s => s.name == name);
                var got = def.platforms.Single(p => p.name == name);
                Assert.That(Vector3.Distance(got.center, want.center), Is.LessThan(0.001f), name + " center");
                Assert.That(Vector3.Distance(got.size, want.size), Is.LessThan(0.001f), name + " size");
            }

            // The low rails and alternate wall line frame the broad route instead of reclaiming its
            // middle. Nothing may cross the causeway's middle any more: the slide-under gate
            // (T1_Fallen_Obelisk) was retired on 2026-09-13.
            foreach (string name in restored) AssertOutsideCenterStrip(name, 3f);
            Assert.IsFalse(def.platforms.Any(p => p.name == "T1_Fallen_Obelisk"), "the causeway's slide gate is retired");
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

        [Test]
        public void BothSentriesAreVisibleFromStone4BeforeTheCausewayCommitment()
        {
            var boxes = A.BoxesFrom(def);
            AssertMuzzleVisible(boxes, "T1_Perch_W", "T1_Stone_4");
            AssertMuzzleVisible(boxes, "T1_Perch_E", "T1_Stone_4");
        }

        static void AssertMuzzleVisible(IList<A.Box> boxes, string perchName, string approachDeck)
        {
            int perchIndex = A.IndexOf(boxes, perchName);
            int deckIndex = A.IndexOf(boxes, approachDeck);
            Assert.GreaterOrEqual(perchIndex, 0, perchName + " missing");
            Assert.GreaterOrEqual(deckIndex, 0, approachDeck + " missing");
            var perch = boxes[perchIndex];
            var deck = boxes[deckIndex];
            Vector3 eye = new Vector3((deck.min.x + deck.max.x) * .5f, deck.max.y + 1.7f,
                                      (deck.min.z + deck.max.z) * .5f);
            Vector3 muzzle = new Vector3((perch.min.x + perch.max.x) * .5f, perch.max.y + 1.5f,
                                         (perch.min.z + perch.max.z) * .5f);
            var blockers = new List<A.Box>();
            for (int i = 0; i < boxes.Count; i++)
                if (i != perchIndex && i != deckIndex) blockers.Add(boxes[i]);
            Assert.IsTrue(T.LineClear(eye, muzzle, blockers),
                approachDeck + " must see " + perchName + "'s muzzle before route commitment");
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
