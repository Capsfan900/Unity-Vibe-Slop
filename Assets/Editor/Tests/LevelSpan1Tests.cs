using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;
using R = VibeGame1.EditorTools.LevelSpan1Report;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>Span 1 — the two wall-run lines between the start pad and the Ninja arena, flown.</b>
    ///
    /// <para><c>LevelArcClearanceTests</c> proves the BASELINE route is still clean hop by hop; these prove
    /// the parkour laid alongside it does what it claims. Every assertion is on the geometry in
    /// <c>Level_01_Level.asset</c> with the constants off the shipped <c>Player.prefab</c>, through the
    /// same <c>WallRunMath</c> the motor runs.</para>
    ///
    /// <para>The bar, from ENGINEERING-LOG ("author against <c>longest</c>, not <c>best</c>"): a line
    /// counts when a sprint entry has at least three clean arriving routes AND the longest of them spends
    /// at least a second on the wall. A landing reached only by hopping off at 0.2 s is a wall jump in a
    /// costume, and the wall is not doing its job.</para>
    ///
    /// <para>What passing means: the arithmetic says the run exists, is long, is gated and rejoins. Nobody
    /// has run either wall. The analyser models fixed inputs — one aim, one speed, one leave time — and
    /// cannot say how forgiving or how fun a run is.</para>
    /// </summary>
    [Category("LevelLines")]  // slow: simulates the motor along the level lines; excluded by VibeGame1/Run Quick EditMode Tests
    public class LevelSpan1Tests
    {
        static LevelDefinition def;
        static List<A.Box> boxes;
        static A.MoveProfile profile;
        static float floorY;
        static readonly A.AirControl[] WithBraking = { A.AirControl.None, A.AirControl.Brake };

        [OneTimeSetUp]
        public void Load()
        {
            def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelArcReport.DefaultLevel);
            Assert.IsNotNull(def, "Level_01_Level.asset is missing");
            string err;
            Assert.IsTrue(A.TryLoadProfile(out profile, out err), err);
            boxes = A.BoxesFrom(def);
            floorY = def.killZone.center.y;
        }

        static A.Box Box(string name)
        {
            int i = A.IndexOf(boxes, name);
            Assert.GreaterOrEqual(i, 0, name + " is not in the level");
            return boxes[i];
        }

        // ------------------------------------------------------------------ the walls themselves

        static IEnumerable<TestCaseData> Walls()
        {
            yield return new TestCaseData("T1_Wall_Start").SetName("Wall_T1_Wall_Start");
            yield return new TestCaseData("T1_Wall_Causeway").SetName("Wall_T1_Wall_Causeway");
        }

        /// <summary>A sprint entry covers 13.5–17.6 m of wall. A wall shorter than that cannot host a run.</summary>
        [Test, TestCaseSource(nameof(Walls))]
        public void TheWallIsAsLongAsTheRunItHosts(string wall)
        {
            var w = Box(wall);
            Assert.GreaterOrEqual(w.max.z - w.min.z, R.MinWallLength,
                wall + " is " + (w.max.z - w.min.z).ToString("0.0") + " m; size walls to the run, not to the gap");
            // The contract is the RELEASED run (stick off: 14.4 m at 1.75 s). Holding forward now tops up
            // toward wallRunTopSpeed 13.75 and covers ~24 m — that is the payoff for committing, and a
            // wall does not have to be sized to the payoff, only to the loan.
            var released = A.MeasureWallRun(profile, profile.groundSpeed, false);
            Assert.GreaterOrEqual(w.max.z - w.min.z, released.distance * 0.9f,
                "a released sprint run (" + released.distance.ToString("0.0") + " m) overruns most of " + wall);
        }

        /// <summary>The face has to exist at running height: the wall must top out well above the
        /// capsule at entry, or the entry test finds no face while you are still rising.</summary>
        [Test, TestCaseSource(nameof(Walls))]
        public void TheWallTopsOutAboveTheEntryHeight(string wall)
        {
            var w = Box(wall);
            string launch = wall == "T1_Wall_Start" ? "Ground_Start" : "T1_Stone_4";
            float apex = Box(launch).max.y + profile.jumpHeight;
            Assert.Greater(w.max.y, apex + profile.capsuleRadius,
                wall + " tops out at " + w.max.y + " but a jump off " + launch + " peaks at " + apex);
        }

        [Test]
        public void TheWallsStandOffEveryDeck()
        {
            // A wall standing ON a platform eats its run-up (the buttress rule). Both walls stand in the void.
            foreach (var wall in new[] { "T1_Wall_Start", "T1_Wall_Causeway" })
            {
                var w = Box(wall);
                foreach (var b in boxes)
                {
                    if (b.name == wall || !b.name.StartsWith("T1_") && b.name != "Ground_Start") continue;
                    if (b.name.StartsWith("T1_Wall")) continue;
                    bool overlapXZ = w.min.x < b.max.x && w.max.x > b.min.x && w.min.z < b.max.z && w.max.z > b.min.z;
                    Assert.IsFalse(overlapXZ, wall + " overlaps " + b.name + " in plan; it must hang beside the course, not on it");
                }
            }
        }

        // ------------------------------------------------------------------ the lines

        static IEnumerable<TestCaseData> PrimaryLines()
        {
            foreach (var l in R.WallRunLines)
                if (l.mustBeLong)
                    yield return new TestCaseData(l).SetName("Line_" + l.from + "_via_" + l.wall + "_to_" + l.to);
        }

        static A.WallRunVerdict Fly(R.WallRunLine l, float speed)
        {
            return A.AnalyzeWallRunGap(boxes, l.from, l.wall, l.to, profile, speed, floorY, l.launchLo, l.launchHi);
        }

        /// <summary>THE test. A sprint entry mounts, runs for at least a second, and lands — three ways.</summary>
        [Test, TestCaseSource(nameof(PrimaryLines))]
        public void ASprintEntryRunsTheWallAndArrives(R.WallRunLine l)
        {
            var v = Fly(l, profile.groundSpeed);
            TestContext.Out.WriteLine(v.Summary());
            Assert.IsTrue(v.anyEntry, "the wall cannot be mounted from " + l.from + ": " + v.Summary());
            Assert.IsTrue(v.exists, v.Summary());
            Assert.GreaterOrEqual(v.cleanRoutes, 3,
                "the crossing exists only as a pixel-perfect line, which is a trap, not a route: " + v.Summary());
            Assert.GreaterOrEqual(v.longest.runDuration, 1.0f,
                "every arriving route leaves the wall almost at once — the landing is reached by a wall JUMP " +
                "in a costume, not by a run: " + v.Summary());
        }

        /// <summary>Speed is rewarded: a slide-jump entry must find at least as many routes as a sprint.</summary>
        [Test, TestCaseSource(nameof(PrimaryLines))]
        public void ASlideJumpEntryIsRewardedNotPunished(R.WallRunLine l)
        {
            var sprint = Fly(l, profile.groundSpeed);
            var slide = Fly(l, profile.SlideJumpSpeed);
            TestContext.Out.WriteLine(slide.Summary());
            Assert.IsTrue(slide.exists, slide.Summary());
            Assert.GreaterOrEqual(slide.cleanRoutes, sprint.cleanRoutes,
                "arriving faster must not close the line: sprint " + sprint.cleanRoutes + " routes, slide-jump " + slide.cleanRoutes);
            Assert.GreaterOrEqual(slide.longest.runDuration, 1.0f, slide.Summary());
        }

        /// <summary>The landing must be out of reach WITHOUT the wall, or the tech buys nothing (CheckHopIsGated).</summary>
        [Test, TestCaseSource(nameof(PrimaryLines))]
        public void TheLandingNeedsTheWall(R.WallRunLine l)
        {
            var b = A.AnalyzeHop(boxes, l.from, l.to, profile, profile.groundSpeed, floorY, WithBraking);
            Assert.IsFalse(b.exists, l.from + " -> " + l.to + " is reachable by the base kit; the wall is decoration: " + b.Summary());
            var s = A.AnalyzeHop(boxes, l.from, l.to, profile, profile.SlideJumpSpeed, floorY, WithBraking);
            Assert.IsFalse(s.exists, l.from + " -> " + l.to + " is reachable by a slide-jump; the wall is decoration: " + s.Summary());
        }

        /// <summary>And the landing must rejoin the course with a plain hop, or it is a second gate.</summary>
        [Test, TestCaseSource(nameof(PrimaryLines))]
        public void TheLandingRejoinsTheCourse(R.WallRunLine l)
        {
            if (string.IsNullOrEmpty(l.rejoin))
            {
                Assert.GreaterOrEqual(A.IndexOf(boxes, l.to), 0,
                    "a portal-feeding wall line must still end on its authored landing");
                return;
            }
            var v = A.AnalyzeHop(boxes, l.to, l.rejoin, profile, profile.groundSpeed, floorY);
            Assert.IsTrue(v.exists, "the exit from a fast line is NOT a second gate: " + v.Summary());
            Assert.GreaterOrEqual(v.cleanLaunchPoints, 3, v.Summary());
        }

        /// <summary>
        /// Landings go DOWN THE LINE, past the face's end, near its plane: the exit throws you along the
        /// wall and 7 m/s out, so a pad that starts short of the wall's end can only be reached by hopping
        /// off early. The wall must also end before the landing, or the run collides with it.
        /// </summary>
        [Test, TestCaseSource(nameof(PrimaryLines))]
        public void TheLandingSitsPastTheEndOfTheWall(R.WallRunLine l)
        {
            var w = Box(l.wall); var t = Box(l.to);
            Assert.GreaterOrEqual(t.max.z, w.max.z, l.to + " ends before " + l.wall + " does; a run overshoots it");
            Assert.Less(t.min.z, w.max.z + 12f, l.to + " starts more than an exit arc past the end of " + l.wall);
            // Both walls stand on the east and are run on their west face; the exit throws 7 m/s west for
            // roughly 0.6 s. A pad whose near edge is further out than that is reached by nothing.
            float runLineX = w.min.x - profile.capsuleRadius;
            float throwReach = profile.WallRun.exitPushSpeed * 0.65f;
            Assert.GreaterOrEqual(t.max.x, runLineX - throwReach,
                l.to + " lies " + (runLineX - t.max.x).ToString("0.0") + " m out from the run line, beyond the exit throw");
        }

        // ------------------------------------------------------------------ what the lines skip

        [Test]
        public void TheOpeningLineSkipsTheFourStones()
        {
            var w = Box("T1_Wall_Start");
            foreach (var s in new[] { "T1_Stone_1", "T1_Stone_2", "T1_Stone_3", "T1_Fast_1" })
            {
                var b = Box(s);
                Assert.Less(b.min.z, w.max.z, s + " is not beside the wall");
                Assert.Greater(b.max.z, w.min.z, s + " is not beside the wall");
                Assert.Less(b.max.x, w.min.x - 2f * profile.capsuleRadius,
                    s + " reaches under the run line; a bleeding run would clip its edge instead of falling clear");
            }
        }

        [Test]
        public void TheCausewayLineSkipsTheGruntsAndTheLintel()
        {
            var w = Box("T1_Wall_Causeway"); var lintel = Box("T1_Fallen_Obelisk"); var pad = Box("T1_Wall_Landing");
            foreach (var sp in def.spawns)
            {
                if (!sp.name.StartsWith("Spawn_T1_Grunt")) continue;
                Assert.Greater(sp.position.z, w.min.z, sp.name + " stands before the wall begins");
                Assert.Less(sp.position.z, pad.max.z, sp.name + " stands past the landing");
            }
            Assert.Less(lintel.max.x, pad.min.x, "the landing must sit beside the lintel, not under it");
            Assert.Greater(pad.max.z, lintel.max.z, "the landing must reach past the lintel or the line has not skipped it");
        }

        /// <summary>The landing pad must not touch the causeway's east rail: a merged box changes the rail's silhouette.</summary>
        [Test]
        public void TheLandingClearsTheRail()
        {
            var pad = Box("T1_Wall_Landing"); var rail = Box("T1_Rail_R");
            Assert.GreaterOrEqual(pad.min.x, rail.max.x, "T1_Wall_Landing overlaps T1_Rail_R");
        }

        // ------------------------------------------------------------------ the base route is intact

        /// <summary>
        /// The four hops the opening wall runs beside, and the two the causeway wall runs beside, are still
        /// clean from most take-off points — not merely from three. LevelArcClearanceTests holds the floor
        /// at three for the whole level; the teaching span is held higher on purpose.
        /// </summary>
        static IEnumerable<TestCaseData> NeighbouringHops()
        {
            yield return new TestCaseData("Ground_Start", "T1_Stone_1", 15).SetName("Hop_Ground_Start_to_T1_Stone_1");
            yield return new TestCaseData("T1_Stone_1", "T1_Stone_2", 15).SetName("Hop_T1_Stone_1_to_T1_Stone_2");
            yield return new TestCaseData("T1_Stone_2", "T1_Stone_3", 12).SetName("Hop_T1_Stone_2_to_T1_Stone_3");
            yield return new TestCaseData("T1_Stone_3", "T1_Stone_4", 15).SetName("Hop_T1_Stone_3_to_T1_Stone_4");
            yield return new TestCaseData("T1_Stone_4", "T1_Causeway", 15).SetName("Hop_T1_Stone_4_to_T1_Causeway");
            yield return new TestCaseData("T1_Fast_1", "T1_Stone_4", 8).SetName("Hop_T1_Fast_1_to_T1_Stone_4");
        }

        /// <summary>The level with every wall-run wall and landing removed: the hop as it was before the walls.</summary>
        static List<A.Box> WithoutWalls(List<A.Box> all)
        {
            var list = new List<A.Box>();
            foreach (var b in all) if (b.name.IndexOf("_Wall", System.StringComparison.Ordinal) < 0) list.Add(b);
            return list;
        }

        [Test, TestCaseSource(nameof(NeighbouringHops))]
        public void TheWallsDoNotNarrowTheBaseHopsBesideThem(string from, string to, int minLaunchPoints)
        {
            var v = A.AnalyzeHop(boxes, from, to, profile, profile.groundSpeed, floorY);
            var bare = A.AnalyzeHop(WithoutWalls(boxes), from, to, profile, profile.groundSpeed, floorY);
            TestContext.Out.WriteLine(v.Summary());
            Assert.IsTrue(v.exists, v.Summary());
            // The floor is the same hop WITHOUT its walls under the shipped physics: the walls may not cost
            // a single launch point. The literal is what the hop was worth under the 2026-09-03 morning
            // physics and is kept as a ceiling on the demand, because the evening's heavier fall
            // (fallGravityMultiplier 1.5, AirFeelTests) shrinks every hop in the level, walls or not.
            Assert.GreaterOrEqual(v.cleanLaunchPoints, Mathf.Min(minLaunchPoints, bare.cleanLaunchPoints),
                "the walls narrowed the hop: with " + v.cleanLaunchPoints + ", without " + bare.cleanLaunchPoints + " -- " + v.Summary());
            Assert.GreaterOrEqual(v.cleanLaunchPoints, 3, "under three launch points the hop is a trick, not a hop: " + v.Summary());
            // chiefObstruction is deliberately NOT asserted: it names whatever the over-thrown arcs hit
            // most often, and a wall standing a metre past a stone is always that thing. The launch-point
            // floor above is the assertion that says whether the hop is still a hop.
            Assert.Greater(v.bestClearance, 1.0f, "the best line is scraping something: " + v.Summary());
        }

        /// <summary>The slide-jump line onto T1_Fast_1 still exists and is still gated — the opening wall sits beside it.</summary>
        [Test]
        public void TheFast1SlideJumpLineIsUntouched()
        {
            var with = A.AnalyzeHop(boxes, "T1_Stone_1", "T1_Fast_1", profile, profile.SlideJumpSpeed, floorY, WithBraking);
            Assert.IsTrue(with.exists, with.Summary());
            var without = A.AnalyzeHop(boxes, "T1_Stone_1", "T1_Fast_1", profile, profile.groundSpeed, floorY);
            Assert.IsFalse(without.exists, without.Summary());
        }
    }
}
