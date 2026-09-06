using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>The sandbox wall-run gauntlet, flown before anyone stands on it.</b>
    ///
    /// <para>SandboxBuilder.BuildWallRunGauntlet claims two properties for its geometry: that the landing
    /// pad is reachable by running the face, and that it is NOT reachable any other way — which is the
    /// property that makes the pad a test of the mechanic rather than a decoration. Claims about geometry
    /// are exactly what <see cref="A.AnalyzeWallRunGap"/> exists to settle, so both are settled here, with
    /// the same code the motor runs and the constants off the shipped <c>Player.prefab</c>.</para>
    ///
    /// <para>The boxes are the builder's literals, restated. That is deliberate and mirrors
    /// <c>WallRunMechanicTests.Shipped()</c>: if someone moves a pad in <c>SandboxBuilder</c> and does not
    /// move it here, this file keeps proving the OLD layout and the mismatch is visible in the diff;
    /// nothing can drift silently. This is also the worked example for the level-restructure task: build
    /// a box list, call <c>AnalyzeWallRunGap(boxes, from, wall, to, profile, maxSpeed, floorY)</c>, and
    /// read <c>exists</c> / <c>Summary()</c>.</para>
    ///
    /// <para>What passing means: a clean line exists in the arithmetic. Nobody has run this wall.</para>
    /// </summary>
    public class WallRunGauntletTests
    {
        static List<A.Box> boxes;
        static A.MoveProfile profile;
        const float FloorY = -3f;   // below the sandbox floor: falling past it is a failed route

        [OneTimeSetUp]
        public void Load()
        {
            string err;
            Assert.IsTrue(A.TryLoadProfile(out profile, out err), err);

            // SandboxBuilder.BuildWallRunGauntlet's literals, plus the floor they stand on.
            boxes = new List<A.Box>
            {
                new A.Box("Sandbox_Floor",   new Vector3(0f, -0.5f, 0f),     new Vector3(60f, 1f, 60f)),
                new A.Box("Wall_N",          new Vector3(0f, 1.5f, 30f),     new Vector3(60f, 3f, 0.5f)),
                new A.Box("WallRun_Launch",  new Vector3(2f, 1.0f, 8f),      new Vector3(5f, 1f, 5f)),
                new A.Box("WallRun_Face",    new Vector3(6f, 4f, 16.5f),     new Vector3(1.2f, 8f, 13f)),
                new A.Box("WallRun_Landing", new Vector3(2f, 3.5f, 26.5f),   new Vector3(6f, 1f, 5f)),
                new A.Box("WallRun_Corner",  new Vector3(-7f, 4f, 23.85f),   new Vector3(1.2f, 8f, 11.7f)),
            };
        }

        [Test]
        public void TheLandingPad_IsReachableByRunningTheFace()
        {
            var v = A.AnalyzeWallRunGap(boxes, "WallRun_Launch", "WallRun_Face", "WallRun_Landing",
                                        profile, profile.groundSpeed, FloorY);
            TestContext.Out.WriteLine(v.Summary());
            Assert.IsTrue(v.exists, v.Summary());
            Assert.GreaterOrEqual(v.cleanRoutes, 3,
                "the crossing exists but only as a pixel-perfect line, which is a trap, not a route: "
                + v.Summary());
            Assert.GreaterOrEqual(v.longest.runDuration, 1.0f,
                "every arriving route leaves the wall almost at once — the pad is reached by a wall " +
                "JUMP in a costume, not by a run: " + v.Summary());
        }

        [Test]
        public void TheLandingPad_IsOutOfReachOfASlideJumpToo()
        {
            var v = A.AnalyzeHop(boxes, "WallRun_Launch", "WallRun_Landing",
                                 profile, profile.SlideJumpSpeed, FloorY);
            Assert.IsFalse(v.exists,
                "a slide-jump skips the wall entirely; the gauntlet no longer isolates the mechanic: "
                + v.Summary());
        }

        [Test]
        public void TheLandingPad_IsOutOfReachOfAPlainJump()
        {
            // The base kit, no wall: 12.5 m of separation with a 2.5 m rise must NOT be jumpable, or the
            // gauntlet proves nothing about wall running.
            var v = A.AnalyzeHop(boxes, "WallRun_Launch", "WallRun_Landing",
                                 profile, profile.groundSpeed, FloorY);
            Assert.IsFalse(v.exists,
                "the gauntlet gap is crossable WITHOUT the wall; it no longer tests the mechanic: "
                + v.Summary());
        }

        [Test]
        public void TheFaceIsAsLongAsTheRunItHosts()
        {
            // The builder sized the face at 14.5 m so the wall ends at the moment a released-stick run
            // does (14.4 m) and the landing pad is what comes next. Hold that against the real envelope:
            // a released run must reach the end of the face (within a metre), and a held run must overrun
            // it — otherwise the pad is reachable without ever committing to the wall.
            var released = A.MeasureWallRun(profile, profile.groundSpeed, false);
            var held = A.MeasureWallRun(profile, profile.groundSpeed, true);
            TestContext.Out.WriteLine("released: " + released.Summary());
            TestContext.Out.WriteLine("held:     " + held.Summary());

            Assert.AreEqual(profile.WallRun.maxDuration, released.duration, 0.02f,
                "a sprint entry no longer rides the full clock — retune broke the fast case");
            const float face = 14.5f;   // 2026-09-03: 1.75 s clock, released run 14.4 m (was 13 m / 13.5 m)
            Assert.AreEqual(face, released.distance, 1.0f, "a released run no longer ends at the end of the face");
            Assert.Greater(held.distance, face, "a held run does not even reach the end of the face");
        }

        [Test]
        public void AMinimumEntryDropsYouOnTheFace_NotPastIt()
        {
            // The other half of the retune, measured on this geometry: scrape onto the wall at the
            // minimum and the run ends by DECAY, well short of the face's far end. This is the
            // "slow entry gets a short run" feedback, as metres of this actual wall.
            var env = A.MeasureWallRun(profile, profile.WallRun.minEntrySpeed, false);
            TestContext.Out.WriteLine("minimum entry: " + env.Summary());
            Assert.AreEqual(WallRunEnd.Decayed, env.ended,
                "a minimum entry did not bleed out: " + env.Summary());
            Assert.Less(env.distance, 13f * 0.6f,
                "a minimum entry still covers most of the face; the short-run feedback is illegible");
        }
    }
}
