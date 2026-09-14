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
    public class LevelSpan3Tests
    {
        LevelDefinition def;
        A.MoveProfile profile;

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
        public void RedSpanShipsAsFourWideLandingsAndThreeExitTerraces()
        {
            AssertDeck("T3_Pillar_1", new Vector3(0f,15.5f,266f), new Vector3(14f,12f,8f));
            AssertDeck("T3_Pillar_2", new Vector3(4f,16.5f,275f), new Vector3(16f,12f,8f));
            AssertDeck("T3_Pillar_3", new Vector3(-4f,17.5f,284f), new Vector3(16f,12f,8f));
            AssertDeck("T3_Pillar_4", new Vector3(0f,18.5f,293f), new Vector3(14f,12f,8f));
            AssertDeck("T3_Span", new Vector3(0f,24f,305f), new Vector3(16f,1f,14f));
            AssertDeck("T3_Step_1", new Vector3(3f,25f,316f), new Vector3(16f,1f,6f));
            AssertDeck("T3_Step_2", new Vector3(-3f,26.5f,323f), new Vector3(16f,1f,6f));
        }

        [Test]
        public void PostsAndOuterWallLinesAreRestored_AndTheLintelIsGone()
        {
            string[] restored = { "T3_Obelisk_W1", "T3_Obelisk_W2", "T3_Obelisk_E1",
                "T3_Obelisk_E2", "T3_Recovery_W1", "T3_Recovery_W2", "T3_Recovery_E1", "T3_Recovery_E2",
                "T3_Wall_Pillars", "T3_Wall_Landing_S", "T3_Wall_Span" };
            // The span's slide-under lintel (T3_Fallen_Lintel) was retired on 2026-09-13: the 16 m water
            // runway is read and skated end to end with nothing across its middle.
            Assert.IsFalse(def.platforms.Any(p => p.name == "T3_Fallen_Lintel"), "the span's slide gate is retired");
            var canonicalEntry = LevelDefinitionAuthoring.OpenCourseDecks.Single(p => p.name == "T3_Pillar_1");
            var appliedEntry = def.platforms.Single(p => p.name == "T3_Pillar_1");
            Vector3 sectionOffset = appliedEntry.center - canonicalEntry.center;
            foreach (string name in restored)
            {
                var want = LevelDefinitionAuthoring.HybridCourseStructures.Single(s => s.name == name);
                var got = def.platforms.Single(p => p.name == name);
                Assert.That(Vector3.Distance(got.center, want.center + sectionOffset), Is.LessThan(0.001f), name + " center");
                Assert.That(Vector3.Distance(got.size, want.size), Is.LessThan(0.001f), name + " size");
            }

            // Posts and wall-run pieces stay outside the wide middle so they add vertical choices without
            // narrowing the base chain; nothing crosses the lane.
            foreach (string name in restored)
            {
                var p = def.platforms.Single(x => x.name == name);
                float nearestX = Mathf.Abs(p.center.x) - p.size.x * 0.5f;
                Assert.GreaterOrEqual(nearestX, 4f,
                    name + " reaches into the red span's broad central lane instead of staying on its shoulder");
            }
        }

        [Test]
        public void FourBalloonArcIsACompleteOptionalBranchToTheSpan()
        {
            var arc = def.balloons.Where(b => b.name.StartsWith("T3_Arc_"))
                                  .OrderBy(b => b.name).ToArray();
            Assert.AreEqual(LevelDefinitionAuthoring.T3Arc.Length, arc.Length);
            Assert.AreEqual(4, arc.Length, "the restored T3 branch is a four-pop aerial sentence");

            var canonicalPillar = LevelDefinitionAuthoring.OpenCourseDecks.Single(p => p.name == "T3_Pillar_1");
            var appliedPillar = def.platforms.Single(p => p.name == "T3_Pillar_1");
            Vector3 sectionOffset = appliedPillar.center - canonicalPillar.center;
            for (int i = 0; i < arc.Length; i++)
            {
                Assert.AreEqual("T3_Arc_" + (i + 1), arc[i].name);
                Assert.That(Vector3.Distance(arc[i].position, LevelDefinitionAuthoring.T3Arc[i] + sectionOffset),
                    Is.LessThan(0.001f), arc[i].name + " position");
                Assert.AreEqual(LevelDefinitionAuthoring.BalloonLaunch, arc[i].launchSpeed, 0.001f);
                Assert.AreEqual(LevelDefinitionAuthoring.BalloonRadius, arc[i].radius, 0.001f);
                Assert.AreEqual(LevelDefinitionAuthoring.BalloonRespawn, arc[i].respawnSeconds, 0.001f);
            }

            var verdict = T.AnalyzeChain(A.BoxesFrom(def), arc, "T3_Pillar_1", "T3_Span",
                                         profile, def.killZone.center.y);
            Assert.IsTrue(verdict.complete, verdict.Summary());
        }

        [Test]
        public void LandingChainIsReachableAtBaseRunSpeed()
        {
            var boxes = A.BoxesFrom(def);
            string[] route = { "T3_Pillar_1", "T3_Pillar_2", "T3_Pillar_3", "T3_Pillar_4",
                               "T3_Span", "T3_Step_1", "T3_Step_2" };
            for (int i = 0; i + 1 < route.Length; i++)
            {
                var hop = A.AnalyzeHop(boxes, route[i], route[i + 1], profile, profile.groundSpeed, def.killZone.center.y);
                Assert.IsTrue(hop.exists, hop.Summary());
                Assert.GreaterOrEqual(hop.cleanLaunchPoints, 3, hop.Summary());
            }
        }

        [Test]
        public void GruntAndHeavyHaveClearInBandMainLineShots()
        {
            var boxes = A.BoxesFrom(def);
            foreach (var perch in LevelDefinitionAuthoring.Perches.Where(p => p.name.StartsWith("T3_")))
            {
                var verdict = T.AnalyzeShooter(boxes, perch.name, perch.spawn, perch.covers.Split(','), 10f, 32f);
                Assert.AreEqual(perch.covers.Split(',').Length, verdict.covered.Count, verdict.Summary());
            }
        }

        [Test]
        public void BlueSentryIsVisibleFromTheFirstPillarBeforeThePillarLine()
        {
            var boxes = A.BoxesFrom(def);
            var authoredPerch = def.platforms.Single(p => p.name == "T3_Perch_W");
            Assert.That(Vector3.Distance(authoredPerch.center, new Vector3(-18f, 23.5f, 290f)), Is.LessThan(0.001f),
                "the canonical red-span perch must receive T3's +70 m solar translation");
            int perchIndex = A.IndexOf(boxes, "T3_Perch_W");
            int deckIndex = A.IndexOf(boxes, "T3_Pillar_1");
            Assert.GreaterOrEqual(perchIndex, 0);
            Assert.GreaterOrEqual(deckIndex, 0);
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
                "Pillar 1 must see the blue sentry muzzle before committing to the elevated pillar line");
        }

        void AssertDeck(string name, Vector3 center, Vector3 size)
        {
            var p = def.platforms.Single(x => x.name == name);
            Assert.That(Vector3.Distance(p.center, center), Is.LessThan(0.001f), name + " center");
            Assert.That(Vector3.Distance(p.size, size), Is.LessThan(0.001f), name + " size");
        }
    }
}
