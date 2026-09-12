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
    public class LevelSpan2Tests
    {
        LevelDefinition def;
        A.MoveProfile profile;
        static readonly Vector3[] Centers =
        {
            new Vector3(14f,4.5f,151.375f), new Vector3(18f,6f,162f), new Vector3(2f,7.5f,170.5f),
            new Vector3(-13f,9f,162f), new Vector3(-16f,10.5f,151f), new Vector3(0f,12f,145f),
            new Vector3(16f,13.5f,151f), new Vector3(18f,15f,162f), new Vector3(3f,16.5f,170.5f),
            new Vector3(16f,18f,179f), new Vector3(0f,19.5f,183.5f),
        };
        static readonly Vector3[] Sizes =
        {
            new Vector3(12f,1f,10f), new Vector3(10f,1f,8f), new Vector3(18f,1f,8f),
            new Vector3(12f,1f,8f), new Vector3(10f,1f,9f), new Vector3(18f,1f,10f),
            new Vector3(12f,1f,9f), new Vector3(10f,1f,8f), new Vector3(18f,1f,8f),
            new Vector3(12f,1f,8f), new Vector3(18f,1f,5f),
        };

        [SetUp]
        public void Load()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            def = Object.Instantiate(shipped);
            LevelDefinitionAuthoring.Apply(def);
            string error;
            Assert.IsTrue(A.TryLoadProfile(out profile, out error), error);
        }
        [TearDown] public void Clean() { Object.DestroyImmediate(def); }

        [Test]
        public void DoubleSwitchbackShipsAsElevenFullTerraces()
        {
            for (int i = 0; i < 11; i++)
            {
                var p = def.platforms.Single(x => x.name == "T2_L" + (i + 1));
                Assert.That(Vector3.Distance(p.center, Centers[i]), Is.LessThan(0.001f), p.name + " center");
                Assert.That(Vector3.Distance(p.size, Sizes[i]), Is.LessThan(0.001f), p.name + " size");
                Assert.GreaterOrEqual(p.size.x, 10f, p.name + " is too narrow to redirect at speed");
            }
        }

        [Test]
        public void TowerChimneyAndOuterWallLinesAreRestored()
        {
            string[] restored = { "T2_Tower", "T2_Buttress", "T2_Wall_East", "T2_Wall_Landing_East",
                                  "T2_Wall_West", "T2_Wall_Landing_West" };
            var canonicalEntry = LevelDefinitionAuthoring.OpenCourseDecks.Single(p => p.name == "T2_L1");
            var appliedEntry = def.platforms.Single(p => p.name == "T2_L1");
            Vector3 sectionOffset = appliedEntry.center - canonicalEntry.center;
            foreach (string name in restored)
            {
                var want = LevelDefinitionAuthoring.HybridCourseStructures.Single(s => s.name == name);
                var got = def.platforms.Single(p => p.name == name);
                Assert.That(Vector3.Distance(got.center, want.center + sectionOffset), Is.LessThan(0.001f), name + " center");
                Assert.That(Vector3.Distance(got.size, want.size), Is.LessThan(0.001f), name + " size");
            }

            foreach (string name in new[] { "T2_Wall_East", "T2_Wall_Landing_East",
                                             "T2_Wall_West", "T2_Wall_Landing_West" })
            {
                var p = def.platforms.Single(x => x.name == name);
                float nearestX = Mathf.Abs(p.center.x) - p.size.x * 0.5f;
                Assert.GreaterOrEqual(nearestX, 7f,
                    name + " reaches into the spiral's broad central crossover instead of staying outside it");
            }
        }

        [Test]
        public void TerraceChainIsReachableAtBaseRunSpeed()
        {
            var boxes = A.BoxesFrom(def);
            for (int i = 1; i < 11; i++)
            {
                var hop = A.AnalyzeHop(boxes, "T2_L" + i, "T2_L" + (i + 1), profile,
                                       profile.groundSpeed, def.killZone.center.y);
                Assert.IsTrue(hop.exists, hop.Summary());
                Assert.GreaterOrEqual(hop.cleanLaunchPoints, 3, hop.Summary());
            }
        }

        [Test]
        public void LowerAndUpperSentriesEachCrossTheirAssignedTerraces()
        {
            var boxes = A.BoxesFrom(def);
            var stats = AssetDatabase.LoadAssetAtPath<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            Assert.IsNotNull(stats);
            System.Func<string, string[]> routeOf = deck =>
            {
                int i = int.Parse(deck.Substring(4)) - 1;
                string previous = i > 0 ? "T2_L" + i : null;
                string next = i < 10 ? "T2_L" + (i + 2) : null;
                return previous == null && next == null ? null : new[] { previous, next };
            };
            foreach (var perch in LevelDefinitionAuthoring.Perches.Where(p => p.name.StartsWith("T2_")))
            {
                var verdict = T.AnalyzeShooterPlacement(boxes, perch.name, perch.spawn, perch.covers.Split(','),
                    10f, 32f, stats.facingConeDeg, routeOf);
                Assert.AreEqual(perch.covers.Split(',').Length, verdict.covered.Count, verdict.Summary());
                Assert.AreEqual(0, verdict.forcedLookAway.Count, verdict.Summary());
            }
        }

        [Test]
        public void SentriesAreVisibleBeforeTheirLowerAndUpperSwitchbackCommitments()
        {
            var boxes = A.BoxesFrom(def);
            var lower = def.platforms.Single(p => p.name == "T2_Perch_W");
            var upper = def.platforms.Single(p => p.name == "T2_Perch_E");
            Assert.That(Vector3.Distance(lower.center, new Vector3(20f, 8f, 174f)), Is.LessThan(0.001f),
                "the canonical lower perch must receive T2's +38 m solar translation");
            Assert.That(Vector3.Distance(upper.center, new Vector3(21f, 20f, 185f)), Is.LessThan(0.001f),
                "the canonical upper perch must receive T2's +38 m solar translation");
            AssertMuzzleVisible(boxes, "T2_Perch_W", "T2_L1");
            AssertMuzzleVisible(boxes, "T2_Perch_W", "T2_L2");
            AssertMuzzleVisible(boxes, "T2_Perch_E", "T2_L8");
            AssertMuzzleVisible(boxes, "T2_Perch_E", "T2_L9");
        }

        [Test]
        public void TowerAndButtressLeaveTheEntrySightlineAndTerraceClear()
        {
            var tower = def.platforms.Single(p => p.name == "T2_Tower");
            var buttress = def.platforms.Single(p => p.name == "T2_Buttress");
            var lowerEast = def.platforms.Single(p => p.name == "T2_L2");
            Assert.That(tower.size.x, Is.EqualTo(4f).Within(0.001f), "the tower may be iconic, not a six-metre blindfold");
            Assert.That(tower.size.y, Is.EqualTo(20f).Within(0.001f), "the core keeps its vertical identity without covering the entry");
            Assert.LessOrEqual(buttress.center.x + buttress.size.x * .5f, lowerEast.center.x - lowerEast.size.x * .5f,
                "the chimney buttress must stay beside T2_L2 instead of creating an obstructing stacked underside");
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
    }
}
