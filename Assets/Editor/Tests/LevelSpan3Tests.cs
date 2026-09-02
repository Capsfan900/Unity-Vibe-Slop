using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;
using R = VibeGame1.EditorTools.LevelSpan3Report;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>Span 3 — the two wall-run lines of The Long Span, flown.</b>
    ///
    /// <para><c>LevelSpan1Tests</c> is the pattern; this holds the third span to the same bar. Every
    /// assertion is on the geometry in <c>Level_01_Level.asset</c> with the constants off the shipped
    /// <c>Player.prefab</c>, through the same <c>WallRunMath</c> the motor runs.</para>
    ///
    /// <para>The bar: a line counts when a sprint entry has at least three clean arriving routes AND the
    /// longest of them spends at least a second on the wall. A landing reached only by hopping off at
    /// 0.2 s is a wall jump in a costume, and the wall is not doing its job.</para>
    ///
    /// <para>What passing means: the arithmetic says the run exists, is long, is gated and rejoins. Nobody
    /// has run either wall.</para>
    /// </summary>
    public class LevelSpan3Tests
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
            yield return new TestCaseData("T3_Wall_Pillars", "T3_Entry").SetName("Wall_T3_Wall_Pillars");
            yield return new TestCaseData("T3_Wall_Span", "T3_Wall_Landing_S").SetName("Wall_T3_Wall_Span");
        }

        /// <summary>A sprint entry covers 13.5–17.6 m of wall. A wall shorter than that cannot host a run.</summary>
        [Test, TestCaseSource(nameof(Walls))]
        public void TheWallIsAsLongAsTheRunItHosts(string wall, string launch)
        {
            var w = Box(wall);
            Assert.GreaterOrEqual(w.max.z - w.min.z, R.MinWallLength,
                wall + " is " + (w.max.z - w.min.z).ToString("0.0") + " m; size walls to the run, not to the gap");
            var held = A.MeasureWallRun(profile, profile.groundSpeed, true);
            Assert.GreaterOrEqual(w.max.z - w.min.z, held.distance * 0.75f,
                "a held sprint run (" + held.distance.ToString("0.0") + " m) overruns most of " + wall);
        }

        /// <summary>The face has to exist at running height: the wall must top out well above the
        /// capsule at entry, or the entry test finds no face while you are still rising.</summary>
        [Test, TestCaseSource(nameof(Walls))]
        public void TheWallTopsOutAboveTheEntryHeight(string wall, string launch)
        {
            var w = Box(wall);
            float apex = Box(launch).max.y + profile.jumpHeight;
            Assert.Greater(w.max.y, apex + profile.capsuleRadius,
                wall + " tops out at " + w.max.y + " but a jump off " + launch + " peaks at " + apex);
        }

        /// <summary>A wall standing ON a platform eats its run-up (the buttress rule). Both walls, and both
        /// landings, stand in the void beside the course.</summary>
        [Test]
        public void TheWallsAndLandingsStandOffEveryDeck()
        {
            foreach (var tile in new[] { "T3_Wall_Pillars", "T3_Wall_Span", "T3_Wall_Landing_S" })
            {
                var w = Box(tile);
                foreach (var b in boxes)
                {
                    if (b.name == tile || !b.name.StartsWith("T3_")) continue;
                    if (b.name.StartsWith("T3_Wall")) continue;
                    bool overlapXZ = w.min.x < b.max.x && w.max.x > b.min.x && w.min.z < b.max.z && w.max.z > b.min.z;
                    Assert.IsFalse(overlapXZ, tile + " overlaps " + b.name + " in plan; it must hang beside the course, not on it");
                }
            }
        }

        /// <summary>Both faces are run on the west side; nothing the LINE passes — wall start to landing
        /// end, since a bleeding run falls past the wall's end — may reach under the run line.</summary>
        static IEnumerable<TestCaseData> ThingsBesideTheLines()
        {
            yield return new TestCaseData("T3_Wall_Pillars", "T3_Wall_Landing_S", "T3_Pillar_1").SetName("Beside_T3_Wall_Pillars_T3_Pillar_1");
            yield return new TestCaseData("T3_Wall_Pillars", "T3_Wall_Landing_S", "T3_Pillar_2").SetName("Beside_T3_Wall_Pillars_T3_Pillar_2");
            yield return new TestCaseData("T3_Wall_Pillars", "T3_Wall_Landing_S", "T3_Pillar_3").SetName("Beside_T3_Wall_Pillars_T3_Pillar_3");
            yield return new TestCaseData("T3_Wall_Pillars", "T3_Wall_Landing_S", "T3_Pillar_4").SetName("Beside_T3_Wall_Pillars_T3_Pillar_4");
            yield return new TestCaseData("T3_Wall_Span", "T3_Step_1", "T3_Obelisk_E1").SetName("Beside_T3_Wall_Span_T3_Obelisk_E1");
            yield return new TestCaseData("T3_Wall_Span", "T3_Step_1", "T3_Obelisk_E2").SetName("Beside_T3_Wall_Span_T3_Obelisk_E2");
            yield return new TestCaseData("T3_Wall_Span", "T3_Step_1", "T3_Recovery_E1").SetName("Beside_T3_Wall_Span_T3_Recovery_E1");
            yield return new TestCaseData("T3_Wall_Span", "T3_Step_1", "T3_Recovery_E2").SetName("Beside_T3_Wall_Span_T3_Recovery_E2");
        }

        [Test, TestCaseSource(nameof(ThingsBesideTheLines))]
        public void NothingReachesUnderTheRunLine(string wall, string landing, string thing)
        {
            var w = Box(wall); var l = Box(landing); var b = Box(thing);
            Assert.Less(b.min.z, l.max.z, thing + " is not beside the " + wall + " line");
            Assert.Greater(b.max.z, w.min.z, thing + " is not beside the " + wall + " line");
            Assert.Less(b.max.x, w.min.x - 2f * profile.capsuleRadius,
                thing + " reaches under the run line of " + wall + "; a bleeding run would clip it instead of falling clear");
        }

        // ------------------------------------------------------------------ the lines

        static IEnumerable<TestCaseData> PrimaryLines()
        {
            foreach (var l in R.WallRunLines)
                if (l.mustBeLong)
                    yield return new TestCaseData(l).SetName("Line_" + l.from + "_via_" + l.wall + "_to_" + l.to);
        }

        static A.WallRunVerdict Fly(LevelSpan1Report.WallRunLine l, float speed)
        {
            return A.AnalyzeWallRunGap(boxes, l.from, l.wall, l.to, profile, speed, floorY, l.launchLo, l.launchHi);
        }

        /// <summary>THE test. A sprint entry mounts, runs for at least a second, and lands — three ways.</summary>
        [Test, TestCaseSource(nameof(PrimaryLines))]
        public void ASprintEntryRunsTheWallAndArrives(LevelSpan1Report.WallRunLine l)
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
        public void ASlideJumpEntryIsRewardedNotPunished(LevelSpan1Report.WallRunLine l)
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
        public void TheLandingNeedsTheWall(LevelSpan1Report.WallRunLine l)
        {
            var b = A.AnalyzeHop(boxes, l.from, l.to, profile, profile.groundSpeed, floorY, WithBraking);
            Assert.IsFalse(b.exists, l.from + " -> " + l.to + " is reachable by the base kit; the wall is decoration: " + b.Summary());
            var s = A.AnalyzeHop(boxes, l.from, l.to, profile, profile.SlideJumpSpeed, floorY, WithBraking);
            Assert.IsFalse(s.exists, l.from + " -> " + l.to + " is reachable by a slide-jump; the wall is decoration: " + s.Summary());
        }

        /// <summary>And the landing must rejoin the course with a plain hop, or it is a second gate.</summary>
        [Test, TestCaseSource(nameof(PrimaryLines))]
        public void TheLandingRejoinsTheCourse(LevelSpan1Report.WallRunLine l)
        {
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
        public void TheLandingSitsPastTheEndOfTheWall(LevelSpan1Report.WallRunLine l)
        {
            var w = Box(l.wall); var t = Box(l.to);
            Assert.GreaterOrEqual(t.max.z, w.max.z, l.to + " ends before " + l.wall + " does; a run overshoots it");
            Assert.Less(t.min.z, w.max.z + 12f, l.to + " starts more than an exit arc past the end of " + l.wall);
            float runLineX = w.min.x - profile.capsuleRadius;
            float throwReach = profile.WallRun.exitPushSpeed * 0.65f;
            Assert.GreaterOrEqual(t.max.x, runLineX - throwReach,
                l.to + " lies " + (runLineX - t.max.x).ToString("0.0") + " m out from the run line, beyond the exit throw");
        }

        // ------------------------------------------------------------------ what the lines skip

        [Test]
        public void ThePillarLineSkipsTheFourPillars()
        {
            var w = Box("T3_Wall_Pillars"); var pad = Box("T3_Wall_Landing_S");
            // The wall runs beside the first two pillars and the landing lies beside the last two; the
            // line as a whole starts before the first and reaches past the fourth.
            foreach (var s in new[] { "T3_Pillar_1", "T3_Pillar_2", "T3_Pillar_3", "T3_Pillar_4" })
            {
                var b = Box(s);
                Assert.Less(b.min.z, pad.max.z, s + " is not beside the line");
                Assert.Greater(b.max.z, w.min.z, s + " is not beside the line");
            }
            Assert.Less(w.min.z, Box("T3_Pillar_1").min.z, "the wall must begin before the first pillar");
            // Skipping the fourth pillar means the landing lies beside it and rejoins the DECK, not the pillar.
            Assert.Greater(pad.max.z, Box("T3_Pillar_4").min.z, "the landing must reach beside the fourth pillar or the line has not skipped it");
            Assert.AreEqual("T3_Span", R.WallRunLines[0].rejoin, "the pillar line must rejoin the deck, past the fourth pillar");
        }

        [Test]
        public void TheSpanLineSkipsTheFightAndTheLintel()
        {
            var w = Box("T3_Wall_Span"); var lintel = Box("T3_Fallen_Lintel"); var step = Box("T3_Step_1");
            foreach (var sp in def.spawns)
            {
                if (sp.name != "Spawn_T3_Grunt" && sp.name != "Spawn_T3_Heavy") continue;
                Assert.Greater(sp.position.z, w.min.z, sp.name + " stands before the wall begins");
                Assert.Less(sp.position.z, w.max.z, sp.name + " stands past the end of the wall");
            }
            Assert.Greater(w.min.z, lintel.min.z - 12f, "the wall must begin beside the deck, not a span before it");
            Assert.Greater(step.min.z, lintel.max.z, "the landing must sit past the lintel or the line has not skipped it");
            Assert.Greater(w.min.z, Box("T3_Span").min.z - 2f, "the span wall must start beside the deck, not before it");
        }

        /// <summary>
        /// The pillar line's landing is where the span line takes off: the two chain into one right-hand
        /// route. The pad has to sit under the span wall's face, and the wall has to begin where the pad
        /// ends — a gap between them is a jump the analyser has not flown.
        /// </summary>
        [Test]
        public void TheTwoLinesChain()
        {
            var pad = Box("T3_Wall_Landing_S"); var w = Box("T3_Wall_Span");
            Assert.GreaterOrEqual(pad.max.x, w.min.x, "the pad does not reach the span wall's face");
            Assert.LessOrEqual(w.min.z - pad.max.z, 1f, "the span wall begins " + (w.min.z - pad.max.z).ToString("0.0") + " m past the pad");
            Assert.Less(pad.max.z, w.min.z, "the pad runs under the span wall; the wall would stand on it");
            var link = R.WallRunLines[1];
            Assert.AreEqual(R.WallRunLines[0].to, link.from, "the span line must take off from the pillar line's landing");
        }

        /// <summary>The exit arc's far wall: the second east obelisk stands beside the run's last third,
        /// so it has to be past where a 1.2 s run from a mid-pad mount leaves, and inside the wall's length
        /// so a run that does hit it was a run, not a jump.</summary>
        [Test]
        public void TheSecondObeliskIsTheLeaveWindowsFarEdge()
        {
            var w = Box("T3_Wall_Span"); var ob = Box("T3_Obelisk_E2");
            Assert.Greater(ob.min.z, w.min.z + 12f, "the obelisk stands inside the first second of the run");
            Assert.Less(ob.max.z, w.max.z, "the obelisk stands past the wall's end, where an exit cannot hit it");
        }

        /// <summary>The lintel still costs time, never access: the walls and pads change nothing about it.</summary>
        [Test]
        public void TheLintelStillCostsTimeNeverAccess()
        {
            var v = A.CheckLintel(boxes, "T3_Fallen_Lintel", "T3_Span", profile);
            Assert.IsTrue(v.exists && v.slideFits && v.standingBlocked && v.jumpable && v.spansTheDeck,
                v.Summary("T3_Fallen_Lintel", "T3_Span"));
        }

        /// <summary>The south landing must not touch the deck or the fourth pillar: a merged box changes a silhouette.</summary>
        [Test]
        public void TheSouthLandingClearsTheDeck()
        {
            var pad = Box("T3_Wall_Landing_S");
            Assert.GreaterOrEqual(pad.min.x, Box("T3_Span").max.x, "T3_Wall_Landing_S touches T3_Span");
            Assert.GreaterOrEqual(pad.min.x, Box("T3_Pillar_4").max.x + 1f, "T3_Wall_Landing_S crowds T3_Pillar_4");
        }

        // ------------------------------------------------------------------ the base route is intact

        /// <summary>
        /// The hops the walls run beside are still clean from most take-off points — not merely from
        /// three. LevelArcClearanceTests holds the floor at three for the whole level; the floors here were
        /// measured by LevelSpan3Report with the walls in place and are held so a later tile cannot narrow
        /// them silently.
        /// </summary>
        static IEnumerable<TestCaseData> NeighbouringHops()
        {
            yield return new TestCaseData("T3_Entry", "T3_Pillar_1", 10).SetName("Hop_T3_Entry_to_T3_Pillar_1");
            yield return new TestCaseData("T3_Pillar_1", "T3_Pillar_2", 20).SetName("Hop_T3_Pillar_1_to_T3_Pillar_2");
            yield return new TestCaseData("T3_Pillar_2", "T3_Pillar_3", 20).SetName("Hop_T3_Pillar_2_to_T3_Pillar_3");
            yield return new TestCaseData("T3_Pillar_3", "T3_Pillar_4", 20).SetName("Hop_T3_Pillar_3_to_T3_Pillar_4");
            yield return new TestCaseData("T3_Pillar_4", "T3_Span", 20).SetName("Hop_T3_Pillar_4_to_T3_Span");
            yield return new TestCaseData("T3_Span", "T3_Step_1", 15).SetName("Hop_T3_Span_to_T3_Step_1");
            yield return new TestCaseData("T3_Step_1", "T3_Step_2", 20).SetName("Hop_T3_Step_1_to_T3_Step_2");
            yield return new TestCaseData("T3_Step_2", "T3_Step_3", 20).SetName("Hop_T3_Step_2_to_T3_Step_3");
        }

        [Test, TestCaseSource(nameof(NeighbouringHops))]
        public void TheWallsDoNotNarrowTheBaseHopsBesideThem(string from, string to, int minLaunchPoints)
        {
            var v = A.AnalyzeHop(boxes, from, to, profile, profile.groundSpeed, floorY);
            TestContext.Out.WriteLine(v.Summary());
            Assert.IsTrue(v.exists, v.Summary());
            Assert.GreaterOrEqual(v.cleanLaunchPoints, minLaunchPoints, v.Summary());
            Assert.Greater(v.bestClearance, 1.0f, "the best line is scraping something: " + v.Summary());
        }
    }
}
