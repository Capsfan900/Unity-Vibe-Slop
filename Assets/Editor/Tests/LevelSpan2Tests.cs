using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;
using S1 = VibeGame1.EditorTools.LevelSpan1Report;
using R = VibeGame1.EditorTools.LevelSpan2Report;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>Span 2 — the two wall-run lines up The Ascent, flown.</b>
    ///
    /// <para>The same bar as <c>LevelSpan1Tests</c>, on the same geometry file with the same shipped
    /// constants: a sprint entry has at least three clean arriving routes and the longest of them spends
    /// a second on the wall; the landing needs the wall; the landing rejoins the spiral with a plain hop.
    /// Plus what is particular to the Ascent — the spiral's own hops beside each wall are untouched,
    /// and so is the buttress chimney that already lives on the tower's flank.</para>
    ///
    /// <para>What passing means: the arithmetic says the runs exist, are long, are gated and rejoin.
    /// Nobody has run either line.</para>
    /// </summary>
    public class LevelSpan2Tests
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

        static bool OverlapXZ(A.Box a, A.Box b)
        {
            return a.min.x < b.max.x && a.max.x > b.min.x && a.min.z < b.max.z && a.max.z > b.min.z;
        }

        // ------------------------------------------------------------------ the walls themselves

        /// <summary>wall, the ledge it is mounted from, the ledges that stand beside its run line.</summary>
        static IEnumerable<TestCaseData> Walls()
        {
            yield return new TestCaseData("T2_Wall_East", "T2_L1", new[] { "T2_L1", "T2_L2", "T2_Wall_Landing_East" }).SetName("Wall_T2_Wall_East");
            yield return new TestCaseData("T2_Wall_West", "T2_L4", new[] { "T2_L4", "T2_L5", "T2_Wall_Landing_West" }).SetName("Wall_T2_Wall_West");
        }

        /// <summary>A sprint entry covers 13.5–17.6 m of wall. A wall shorter than that cannot host a run.</summary>
        [Test, TestCaseSource(nameof(Walls))]
        public void TheWallIsAsLongAsTheRunItHosts(string wall, string launch, string[] beside)
        {
            var w = Box(wall);
            Assert.GreaterOrEqual(w.max.z - w.min.z, S1.MinWallLength,
                wall + " is " + (w.max.z - w.min.z).ToString("0.0") + " m; size walls to the run, not to the gap");
            var held = A.MeasureWallRun(profile, profile.groundSpeed, true);
            Assert.GreaterOrEqual(w.max.z - w.min.z, held.distance * 0.75f,
                "a held sprint run (" + held.distance.ToString("0.0") + " m) overruns most of " + wall);
        }

        /// <summary>The face has to exist at running height: above the jump apex off the mount ledge,
        /// and below the feet of a run that has ridden the wall out.</summary>
        [Test, TestCaseSource(nameof(Walls))]
        public void TheWallSpansTheRunHeight(string wall, string launch, string[] beside)
        {
            var w = Box(wall);
            float apex = Box(launch).max.y + profile.jumpHeight;
            Assert.Greater(w.max.y, apex + profile.capsuleRadius,
                wall + " tops out at " + w.max.y + " but a jump off " + launch + " peaks at " + apex);
            var held = A.MeasureWallRun(profile, profile.groundSpeed, true);
            Assert.Less(w.min.y, Box(launch).max.y + held.netHeight,
                wall + "'s foot is above where a ridden-out run off " + launch + " ends; the run loses the wall early");
        }

        /// <summary>A wall standing ON a ledge eats its run-up (the buttress rule). Both stand in the void.</summary>
        [Test, TestCaseSource(nameof(Walls))]
        public void TheWallStandsOffEveryLedge(string wall, string launch, string[] beside)
        {
            var w = Box(wall);
            foreach (var b in boxes)
            {
                if (b.name == wall || !b.name.StartsWith("T2_")) continue;
                Assert.IsFalse(OverlapXZ(w, b), wall + " overlaps " + b.name + " in plan; it must hang beside the spiral, not on it");
            }
        }

        /// <summary>The ledge the wall is mounted from must hug it: a deck more than ~2 m off the run line
        /// cannot reach the face before the arc drops below the wall (T2_Entry, 6.5 m off, never mounts).</summary>
        [Test, TestCaseSource(nameof(Walls))]
        public void TheMountLedgeHugsTheWall(string wall, string launch, string[] beside)
        {
            var w = Box(wall); var l = Box(launch);
            float lateral = w.min.x > l.max.x ? w.min.x - l.max.x : l.min.x - w.max.x;
            Assert.LessOrEqual(lateral, 2f, launch + " stands " + lateral.ToString("0.0") + " m off " + wall + "; it cannot be mounted from there");
            Assert.Greater(lateral, 2f * profile.capsuleRadius, launch + " reaches under the run line of " + wall);
        }

        /// <summary>The ledges beside the run line must stop short of it, or a bleeding run clips a ledge
        /// edge instead of falling clear — the same rule the teaching span holds for its stones. The pads
        /// too: a pad across the run line stops the run dead (the analyser was blind to that once), and a
        /// ride-out is meant to fall past the pad, visibly, as the teaching span's opening wall does.</summary>
        [Test, TestCaseSource(nameof(Walls))]
        public void TheLedgesBesideTheWallStayClearOfTheRunLine(string wall, string launch, string[] beside)
        {
            var w = Box(wall);
            bool east = w.min.x > 0f;
            foreach (var s in beside)
            {
                var b = Box(s);
                if (east) Assert.Less(b.max.x, w.min.x - 2f * profile.capsuleRadius, s + " reaches under the run line of " + wall);
                else Assert.Greater(b.min.x, w.max.x + 2f * profile.capsuleRadius, s + " reaches under the run line of " + wall);
                Assert.Less(b.min.z, w.max.z, s + " is not beside " + wall);
                Assert.Greater(b.max.z, w.min.z, s + " is not beside " + wall);
            }
        }

        // ------------------------------------------------------------------ the lines

        static IEnumerable<TestCaseData> PrimaryLines()
        {
            foreach (var l in R.WallRunLines)
                if (l.mustBeLong)
                    yield return new TestCaseData(l).SetName("Line_" + l.from + "_via_" + l.wall + "_to_" + l.to);
        }

        static A.WallRunVerdict Fly(S1.WallRunLine l, float speed)
        {
            return A.AnalyzeWallRunGap(boxes, l.from, l.wall, l.to, profile, speed, floorY, l.launchLo, l.launchHi);
        }

        /// <summary>THE test. A sprint entry mounts, runs for at least a second, and lands — three ways.</summary>
        [Test, TestCaseSource(nameof(PrimaryLines))]
        public void ASprintEntryRunsTheWallAndArrives(S1.WallRunLine l)
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
        public void ASlideJumpEntryIsRewardedNotPunished(S1.WallRunLine l)
        {
            var sprint = Fly(l, profile.groundSpeed);
            var slide = Fly(l, profile.SlideJumpSpeed);
            TestContext.Out.WriteLine(slide.Summary());
            Assert.IsTrue(slide.exists, slide.Summary());
            Assert.GreaterOrEqual(slide.cleanRoutes, sprint.cleanRoutes,
                "arriving faster must not close the line: sprint " + sprint.cleanRoutes + " routes, slide-jump " + slide.cleanRoutes);
            Assert.GreaterOrEqual(slide.longest.runDuration, 1.0f, slide.Summary());
        }

        /// <summary>The landing must be out of reach WITHOUT the wall, or the tech buys nothing.</summary>
        [Test, TestCaseSource(nameof(PrimaryLines))]
        public void TheLandingNeedsTheWall(S1.WallRunLine l)
        {
            var b = A.AnalyzeHop(boxes, l.from, l.to, profile, profile.groundSpeed, floorY, WithBraking);
            Assert.IsFalse(b.exists, l.from + " -> " + l.to + " is reachable by the base kit; the wall is decoration: " + b.Summary());
            var s = A.AnalyzeHop(boxes, l.from, l.to, profile, profile.SlideJumpSpeed, floorY, WithBraking);
            Assert.IsFalse(s.exists, l.from + " -> " + l.to + " is reachable by a slide-jump; the wall is decoration: " + s.Summary());
        }

        /// <summary>And the landing must rejoin the spiral with a plain hop, or it is a second gate.</summary>
        [Test, TestCaseSource(nameof(PrimaryLines))]
        public void TheLandingRejoinsTheSpiral(S1.WallRunLine l)
        {
            var v = A.AnalyzeHop(boxes, l.to, l.rejoin, profile, profile.groundSpeed, floorY);
            Assert.IsTrue(v.exists, "the exit from a fast line is NOT a second gate: " + v.Summary());
            Assert.GreaterOrEqual(v.cleanLaunchPoints, 3, v.Summary());
        }

        /// <summary>
        /// Landings go DOWN THE LINE, near the face's plane: the exit throws you along the wall and 7 m/s
        /// out. On the Ascent each pad sits beside its wall's far end rather than past it — a run that
        /// rides the wall out drops straight onto it — so the bound is on the outward offset, and on the
        /// pad reaching at least to the wall's end in the run direction.
        /// </summary>
        [Test, TestCaseSource(nameof(PrimaryLines))]
        public void TheLandingSitsAtTheEndOfTheWall(S1.WallRunLine l)
        {
            var w = Box(l.wall); var t = Box(l.to); var from = Box(l.from);
            bool north = t.max.z > from.max.z;
            if (north)
            {
                Assert.GreaterOrEqual(t.max.z, w.max.z, l.to + " ends before " + l.wall + " does; a ride-out overshoots it");
                Assert.Less(t.min.z, w.max.z + 12f, l.to + " starts more than an exit arc past the end of " + l.wall);
            }
            else
            {
                Assert.LessOrEqual(t.min.z, w.min.z, l.to + " ends before " + l.wall + " does; a ride-out overshoots it");
                Assert.Greater(t.max.z, w.min.z - 12f, l.to + " starts more than an exit arc past the end of " + l.wall);
            }
            bool east = w.min.x > 0f;
            float runLineX = east ? w.min.x - profile.capsuleRadius : w.max.x + profile.capsuleRadius;
            float throwReach = profile.WallRun.exitPushSpeed * 0.65f;
            if (east) Assert.GreaterOrEqual(t.max.x, runLineX - throwReach, l.to + " lies beyond the exit throw from " + l.wall);
            else Assert.LessOrEqual(t.min.x, runLineX + throwReach, l.to + " lies beyond the exit throw from " + l.wall);
        }

        // ------------------------------------------------------------------ what the lines skip

        /// <summary>Each line skips the ledge between its mount and its rejoin, and lands level with the rejoin.</summary>
        static IEnumerable<TestCaseData> Skips()
        {
            yield return new TestCaseData("T2_Wall_East", "T2_Wall_Landing_East", "T2_L2", "T2_L3").SetName("Skip_East_T2_L2");
            yield return new TestCaseData("T2_Wall_West", "T2_Wall_Landing_West", "T2_L5", "T2_L6").SetName("Skip_West_T2_L5");
        }

        [Test, TestCaseSource(nameof(Skips))]
        public void TheLineSkipsALedgeAndLandsLevelWithTheNext(string wall, string pad, string skipped, string rejoin)
        {
            var w = Box(wall); var p = Box(pad); var s = Box(skipped); var r = Box(rejoin);
            Assert.Less(s.min.z, w.max.z, skipped + " is not beside " + wall);
            Assert.Greater(s.max.z, w.min.z, skipped + " is not beside " + wall);
            Assert.AreEqual(r.max.y, p.max.y, 0.01f, pad + " is meant to sit level with " + rejoin);
            Assert.Greater(p.max.y, s.max.y, pad + " is not above the ledge it skips");
            Assert.IsFalse(OverlapXZ(p, s), pad + " overlaps " + skipped);
            Assert.IsFalse(OverlapXZ(p, r), pad + " overlaps " + rejoin + "; a merged box changes the ledge's silhouette");
        }

        /// <summary>The west line's purpose is the grunt on T2_L5: it must stand beside the wall, between the
        /// mount and the landing, so a run passes it without touching its ledge.</summary>
        [Test]
        public void TheWestLineSkipsTheSecondGrunt()
        {
            var w = Box("T2_Wall_West"); var pad = Box("T2_Wall_Landing_West"); var mount = Box("T2_L4");
            bool found = false;
            foreach (var sp in def.spawns)
            {
                if (sp.name != "Spawn_T2_GruntB") continue;
                found = true;
                Assert.Less(sp.position.z, mount.min.z, "the grunt stands at or past the mount ledge");
                Assert.Greater(sp.position.z, pad.max.z, "the grunt stands past the landing; the line has not skipped it");
                Assert.Greater(sp.position.x, w.max.x, "the grunt stands on the wrong side of the wall");
            }
            Assert.IsTrue(found, "Spawn_T2_GruntB is gone; the west line's purpose changed");
        }

        /// <summary>The west pad hangs over the entry pad's flank, eight metres up. It must not touch it in plan.</summary>
        [Test]
        public void TheWestPadClearsTheEntryPad()
        {
            Assert.IsFalse(OverlapXZ(Box("T2_Wall_Landing_West"), Box("T2_Entry")), "T2_Wall_Landing_West overlaps T2_Entry in plan");
        }

        // ------------------------------------------------------------------ the spiral is intact

        /// <summary>
        /// The hops the walls run beside are still clean from most take-off points — not merely from
        /// three. LevelArcClearanceTests holds the floor at three for the whole level.
        /// </summary>
        static IEnumerable<TestCaseData> NeighbouringHops()
        {
            yield return new TestCaseData("T2_Entry", "T2_L1", 6).SetName("Hop_T2_Entry_to_T2_L1");
            yield return new TestCaseData("T2_L1", "T2_L2", 12).SetName("Hop_T2_L1_to_T2_L2");
            yield return new TestCaseData("T2_L2", "T2_L3", 11).SetName("Hop_T2_L2_to_T2_L3");
            yield return new TestCaseData("T2_L3", "T2_L4", 11).SetName("Hop_T2_L3_to_T2_L4");
            yield return new TestCaseData("T2_L4", "T2_L5", 12).SetName("Hop_T2_L4_to_T2_L5");
            yield return new TestCaseData("T2_L5", "T2_L6", 12).SetName("Hop_T2_L5_to_T2_L6");
            yield return new TestCaseData("T2_L6", "T2_L7", 12).SetName("Hop_T2_L6_to_T2_L7");
            yield return new TestCaseData("T2_L7", "T2_L8", 12).SetName("Hop_T2_L7_to_T2_L8");
        }

        [Test, TestCaseSource(nameof(NeighbouringHops))]
        public void TheWallsDoNotNarrowTheSpiralBesideThem(string from, string to, int minLaunchPoints)
        {
            var v = A.AnalyzeHop(boxes, from, to, profile, profile.groundSpeed, floorY);
            TestContext.Out.WriteLine(v.Summary());
            Assert.IsTrue(v.exists, v.Summary());
            Assert.GreaterOrEqual(v.cleanLaunchPoints, minLaunchPoints, v.Summary());
            Assert.Greater(v.bestClearance, 1.0f, "the best line is scraping something: " + v.Summary());
        }

        /// <summary>The buttress chimney on the tower's flank is a separate line and stays one:
        /// still a climbable chimney, and T2_L8 still out of reach of the base kit from T2_L2.</summary>
        [Test]
        public void TheButtressChimneyIsUntouched()
        {
            var chimney = A.MeasureChimney(boxes, "T2_Tower", "T2_Buttress", profile);
            Assert.IsTrue(chimney.valid, "chimney geometry: " + chimney.reason);
            var v = A.AnalyzeHop(boxes, "T2_L2", "T2_L8", profile, profile.groundSpeed, floorY, WithBraking);
            Assert.IsFalse(v.exists, "T2_L2 -> T2_L8 must still need the wall jump: " + v.Summary());
            var curtain = Box("T2_Wall_East"); var fin = Box("T2_Buttress");
            Assert.Greater(curtain.min.x, fin.max.x + 4f, "the curtain crowds the buttress chimney");
        }
    }
}
