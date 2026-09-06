using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>The wall run, as arithmetic.</b>
    ///
    /// <para>The decision logic and every integral of the mechanic live in <see cref="WallRunMath"/> as
    /// pure static functions precisely so they can be tested here instead of asserted in prose. Entry is
    /// four conditions, the loan is a gravity curve, and the exit is one impulse; each of those is a
    /// number that can be wrong, so each of them is checked.</para>
    ///
    /// <para><b>The bug these exist against.</b> This project has already shipped one frame-rate-dependent
    /// movement move — a slide that covered 4.0 m at 500 fps and 1.8 m at 20 fps, from the same press, and
    /// nobody caught it because every measurement was taken at one framerate. So the central test here is
    /// not "does a wall run work", it is <see cref="TheRunIsIdenticalAtEveryFramerate"/>: the same run
    /// integrated at six timesteps from 2 ms to 83 ms, asserted to land within a centimetre.</para>
    ///
    /// <para>What none of this proves: that any of it FEELS like Apex. Nobody has run on a wall.</para>
    /// </summary>
    public class WallRunMechanicTests
    {
        /// <summary>The shipped tuning, written here as literals ON PURPOSE. WallRunTunablesTests asserts
        /// this same list against Player.prefab, so if the two ever disagree one of them fails loudly
        /// rather than both quietly drifting.</summary>
        static WallRunMath.Params Shipped()
        {
            var p = new WallRunMath.Params();
            p.gravity = -30f;
            p.minEntrySpeed = 6f;
            p.maxEntryFallSpeed = 9f;
            p.maxApproachCos = 0.80f;
            p.minLookAlongCos = -0.05f;
            p.maxDuration = 1.75f;
            p.gravityStartScale = 0.10f;
            p.gravityEndScale = 0.60f;
            p.entryUpSpeed = 3f;
            p.speedDecay = 0.35f;
            p.minSustainSpeed = 4f;
            p.accel = 14f;
            p.topSpeed = 13.75f;
            p.maxSpeed = 22f;
            p.exitUpSpeed = 10f;
            p.exitPushSpeed = 7f;
            p.exitTangentBoost = 4f;
            return p;
        }

        // A wall whose face lies in the x/z plane at x = 0, with the player on the -x side: outward
        // normal -x, so a run along it travels in +z.
        static readonly Vector3 Normal = new Vector3(-1f, 0f, 0f);

        // ------------------------------------------------------------------ entry

        [Test]
        public void AProperApproach_Enters()
        {
            Vector3 runDir; WallRunReject why;
            bool ok = WallRunMath.CanEnter(new Vector3(-1f, 1f, 11f), Normal, Vector3.forward,
                                           Shipped(), out runDir, out why);
            Assert.IsTrue(ok, "a sprint along the face, looking along it, was refused: " + why);
            Assert.AreEqual(0f, runDir.x, 1e-4f, "the run direction still has an into-the-wall component");
            Assert.AreEqual(1f, runDir.z, 1e-4f);
        }

        [Test]
        public void MerelyTouchingAWallIsNotAWallRun()
        {
            // THE point of the entry rules. Drifting along a face at a walking pace is the accident the
            // mechanic must not fire on, or every corridor in the game becomes sticky.
            Vector3 runDir; WallRunReject why;
            Assert.IsFalse(WallRunMath.CanEnter(new Vector3(0f, 0f, 3f), Normal, Vector3.forward,
                                                Shipped(), out runDir, out why));
            Assert.AreEqual(WallRunReject.TooSlow, why);
        }

        [Test]
        public void RunningIntoTheFaceIsACollisionNotARun()
        {
            // Straight at the wall at 20 m/s. Fast, but the wrong kind of fast.
            Vector3 runDir; WallRunReject why;
            Assert.IsFalse(WallRunMath.CanEnter(new Vector3(20f, 0f, 0f), Normal, Vector3.right,
                                                Shipped(), out runDir, out why));
            Assert.AreEqual(WallRunReject.WrongApproach, why);
        }

        [Test]
        public void TheApproachGateSitsWhereTheTooltipSaysItDoes()
        {
            var p = Shipped();
            // maxApproachCos 0.80 is 53.1 deg off the wall plane (retuned from 33: a sprint-jump taken
            // at 45 deg toward a wall is the NORMAL way a first-person player arrives at one). Check both
            // sides of that edge with a speed high enough that the TooSlow gate can never be the one
            // answering.
            foreach (var probe in new[] { new { deg = 50f, want = true }, new { deg = 60f, want = false } })
            {
                float rad = probe.deg * Mathf.Deg2Rad;
                // Into the wall (+x is into it, since the normal is -x) at `deg` off the face.
                Vector3 v = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * 18f;
                Vector3 runDir; WallRunReject why;
                bool ok = WallRunMath.CanEnter(v, Normal, new Vector3(0f, 0f, 1f), p, out runDir, out why);
                Assert.AreEqual(probe.want, ok,
                    probe.deg + " deg off the face gave " + ok + " (" + why + "); the gate is not at 53 deg.");
            }
        }

        [Test]
        public void APlummetDoesNotCatchTheWall()
        {
            Vector3 runDir; WallRunReject why;
            Assert.IsFalse(WallRunMath.CanEnter(new Vector3(0f, -14f, 11f), Normal, Vector3.forward,
                                                Shipped(), out runDir, out why));
            Assert.AreEqual(WallRunReject.FallingTooFast, why,
                "a 14 m/s fall should be refused by the fall gate, not by something else");

            // ...but an ordinary descent still does. The gate bounds a rescue, it does not forbid one.
            Assert.IsTrue(WallRunMath.CanEnter(new Vector3(0f, -6f, 11f), Normal, Vector3.forward,
                                               Shipped(), out runDir, out why), why.ToString());
        }

        [Test]
        public void IntentIsRequired_LookingBackwardsRefusesTheRun()
        {
            Vector3 runDir; WallRunReject why;
            Assert.IsFalse(WallRunMath.CanEnter(new Vector3(0f, 0f, 11f), Normal, new Vector3(0f, 0f, -1f),
                                                Shipped(), out runDir, out why));
            Assert.AreEqual(WallRunReject.LookingAway, why);

            // Glancing 60 deg off the run is fine — this is a first person game and heads move.
            Vector3 glance = new Vector3(Mathf.Sin(60f * Mathf.Deg2Rad), 0f, Mathf.Cos(60f * Mathf.Deg2Rad));
            Assert.IsTrue(WallRunMath.CanEnter(new Vector3(0f, 0f, 11f), Normal, glance,
                                               Shipped(), out runDir, out why), why.ToString());
        }

        [Test]
        public void EntryKeepsSpeedAlongTheWallAndFloorsTheRise()
        {
            var p = Shipped();
            // The WHOLE horizontal speed turns down the run: 4 into the wall and 11 along it arrive as
            // 11.7 along it. The wall catches and redirects; it does not bill the approach angle.
            Vector3 v = WallRunMath.Enter(new Vector3(-4f, -5f, 11f), Vector3.forward, p);
            Assert.AreEqual(Mathf.Sqrt(16f + 121f), v.z, 1e-3f, "horizontal speed was not redirected down the run on entry");
            Assert.AreEqual(0f, v.x, 1e-3f, "the into-the-wall component survived entry");
            Assert.AreEqual(p.entryUpSpeed, v.y, 1e-3f, "the catch did not floor vertical speed");

            // A FLOOR, not an add: arriving already rising faster keeps what it had.
            Assert.AreEqual(9f, WallRunMath.Enter(new Vector3(0f, 9f, 11f), Vector3.forward, p).y, 1e-3f);
        }

        // ------------------------------------------------------------------ the loan

        [Test]
        public void GravityRampsFromNearlyFreeToNearlyRealAcrossTheRun()
        {
            var p = Shipped();
            Assert.AreEqual(0.10f, WallRunMath.GravityScale(0f, p), 1e-4f);
            Assert.AreEqual(0.60f, WallRunMath.GravityScale(p.maxDuration, p), 1e-4f);
            Assert.AreEqual(0.60f, WallRunMath.GravityScale(99f, p), 1e-4f, "the ramp must clamp, not run on");

            // Squared, not linear: at the halfway point it is still only a quarter of the way up. That
            // is what makes the first half of a run feel free and the end feel like being dropped.
            float mid = WallRunMath.GravityScale(p.maxDuration * 0.5f, p);
            Assert.AreEqual(0.10f + 0.25f * 0.5f, mid, 1e-3f);
            Assert.Less(mid, 0.5f * (0.10f + 0.60f), "the ramp has gone linear; re-read why it is squared");
        }

        /// <summary>Integrate a whole run at one timestep and report what it was worth.</summary>
        static void Simulate(float dt, WallRunMath.Params p, bool holdForward,
                             out float duration, out float distance, out float netHeight,
                             out float rise, out float endSpeed)
        {
            Vector3 runDir = Vector3.forward;
            Vector3 v = WallRunMath.Enter(new Vector3(0f, 0f, 11f), runDir, p);
            Vector3 pos = Vector3.zero;
            duration = 0f;
            rise = 0f;

            for (int i = 0; i < 20000; i++)
            {
                Vector3 disp; float used;
                v = WallRunMath.Advance(v, runDir, duration, holdForward, p, dt, out disp, out used);
                if (used <= 0f) break;
                pos += disp;
                duration += used;
                if (pos.y > rise) rise = pos.y;
                WallRunEnd why;
                if (WallRunMath.ShouldEnd(duration, Mathf.Abs(v.z), p, out why)) break;
            }
            distance = pos.z;
            netHeight = pos.y;
            endSpeed = v.z;
        }

        [Test]
        public void TheRunIsIdenticalAtEveryFramerate()
        {
            // THE test. 500, 144, 90, 60, 30 and 12 fps. The slide bug was a 2.2 m spread across a
            // 25x range in dt; anything above a centimetre here is the same class of defect.
            var p = Shipped();
            float[] steps = { 1f / 500f, 1f / 144f, 1f / 90f, 1f / 60f, 1f / 30f, 1f / 12f };

            float d0 = 0f, h0 = 0f, t0 = 0f, s0 = 0f, r0 = 0f;
            for (int i = 0; i < steps.Length; i++)
            {
                float dur, dist, net, rise, end;
                Simulate(steps[i], p, false, out dur, out dist, out net, out rise, out end);
                if (i == 0) { d0 = dist; h0 = net; t0 = dur; s0 = end; r0 = rise; continue; }

                string at = " at dt = " + steps[i].ToString("F4") + " s (" + Mathf.RoundToInt(1f / steps[i]) + " fps)";
                Assert.AreEqual(t0, dur, 0.005f, "duration moved" + at);
                Assert.AreEqual(d0, dist, 0.01f, "distance moved (" + d0.ToString("F3") + " vs " + dist.ToString("F3") + ")" + at);
                Assert.AreEqual(h0, net, 0.01f, "net height moved (" + h0.ToString("F3") + " vs " + net.ToString("F3") + ")" + at);
                Assert.AreEqual(r0, rise, 0.01f, "peak rise moved" + at);
                Assert.AreEqual(s0, end, 0.02f, "exit speed moved" + at);
            }
        }

        [Test]
        public void TheRunIsShortEnoughToBeALoanAndLongEnoughToBeAMove()
        {
            var p = Shipped();
            float dur, dist, net, rise, end;
            Simulate(1f / 60f, p, false, out dur, out dist, out net, out rise, out end);

            Assert.AreEqual(p.maxDuration, dur, 0.02f,
                "an 11 m/s entry should be capped by TIME, not bleed out early — otherwise the duration " +
                "field is decorative. Ended after " + dur.ToString("F2") + " s.");
            Assert.Greater(dist, 12f, "a full run covers less than 12 m of wall; corridors would be tiny");
            Assert.Less(dist, 20f, "a full run covers over 20 m; that is a floor, not a wall");
            Assert.Greater(rise, 0.8f, "the catch buys no visible height, so nothing is being borrowed");
            Assert.Less(rise, 2.5f, "the run out-climbs a jump (2.4 m), which makes jumping pointless");
            Assert.Less(net, 0f, "the run ends level with or above where it started — that is a floor");
            Assert.Greater(end, p.minSustainSpeed,
                "the run bled below its own sustain floor before time ran out; the two numbers disagree");
        }

        [Test]
        public void HoldingForwardExtendsTheRunButNeverOutrunsTheWallsTopSpeed()
        {
            var p = Shipped();
            float dA, distA, netA, riseA, endA;
            float dB, distB, netB, riseB, endB;
            Simulate(1f / 60f, p, false, out dA, out distA, out netA, out riseA, out endA);
            Simulate(1f / 60f, p, true, out dB, out distB, out netB, out riseB, out endB);

            Assert.Greater(distB, distA, "holding forward on the wall does nothing at all");
            Assert.LessOrEqual(endB, p.topSpeed + 1e-3f,
                "the top-up accelerated past wallRunTopSpeed (1.25x a sprint: the wall IS faster than the floor, by exactly that much and no more)");
        }

        [Test]
        public void ASlowEntryBleedsOutBeforeTheTimerDoes()
        {
            // Entering at barely the minimum, with the stick released, should end on DECAY rather than on
            // the clock — the two end conditions must both be reachable or one of them is dead code.
            var p = Shipped();
            Vector3 runDir = Vector3.forward;
            Vector3 v = WallRunMath.Enter(new Vector3(0f, 0f, p.minEntrySpeed), runDir, p);
            float t = 0f;
            WallRunEnd why = WallRunEnd.None;
            for (int i = 0; i < 5000; i++)
            {
                Vector3 disp; float used;
                v = WallRunMath.Advance(v, runDir, t, false, p, 1f / 60f, out disp, out used);
                if (used <= 0f) { why = WallRunEnd.Expired; break; }
                t += used;
                if (WallRunMath.ShouldEnd(t, Mathf.Abs(v.z), p, out why)) break;
            }
            Assert.AreEqual(WallRunEnd.Decayed, why,
                "a minimum-speed entry rode the full clock; wallRunMinSustainSpeed is unreachable and " +
                "therefore not doing anything.");
        }

        // ------------------------------------------------------------------ the exit

        [Test]
        public void TheExitThrowsYouDownTheLineNotJustOffTheWall()
        {
            var p = Shipped();
            Vector3 v = WallRunMath.Exit(new Vector3(0f, -4f, 9f), Normal, Vector3.forward, p, 22f);

            Assert.AreEqual(9f + p.exitTangentBoost, v.z, 1e-3f, "the forward boost is missing");
            Assert.AreEqual(-p.exitPushSpeed, v.x, 1e-3f, "the push off the face is missing or backwards");
            Assert.AreEqual(p.exitUpSpeed, v.y, 1e-3f, "the exit did not set a fixed rise");

            // The signature property: more of the exit goes ALONG the wall than away from it. A wall jump
            // is the other way round (push 12 vs whatever you carried), and that is the whole difference.
            Assert.Greater(Mathf.Abs(v.z), Mathf.Abs(v.x) * 1.5f,
                "the run exit is throwing you sideways off the wall like an ordinary wall jump");
        }

        [Test]
        public void TheExitCannotBeLaunderedIntoUnboundedSpeed()
        {
            var p = Shipped();
            // Arrive at the clamp already, then ask for more.
            Vector3 v = WallRunMath.Exit(new Vector3(0f, 0f, 22f), Normal, Vector3.forward, p, 22f);
            float horizontal = new Vector2(v.x, v.z).magnitude;
            Assert.LessOrEqual(horizontal, 22f + 1e-3f,
                "the exit exceeded dashSpeed (" + horizontal.ToString("F2") + " m/s); a wall would be a " +
                "speed generator and every authored gap downstream would be wrong");
        }

        [Test]
        public void TheExitJumpClearsMoreGroundThanTheRunItLeaves()
        {
            // The reason to jump off rather than ride the run out. Measured, because "it should feel
            // rewarding" is not a number.
            var p = Shipped();
            Vector3 v = WallRunMath.Exit(new Vector3(0f, 0f, 11f), Normal, Vector3.forward, p, 22f);
            float airtime = 2f * v.y / -p.gravity;      // back to the height it left at
            float reach = new Vector2(v.x, v.z).magnitude * airtime;
            Assert.Greater(reach, 8f,
                "a run exit covers only " + reach.ToString("F1") + " m before returning to its own height; " +
                "that is less than an ordinary jump and nobody would ever take it");
        }

        // ------------------------------------------------------------------ re-entry

        [Test]
        public void TheSameFaceCannotBeReRunButTheOppositeOneCan()
        {
            const float limit = 0.85f;   // sameWallCosineLimit, shipped
            Vector3 west = new Vector3(-1f, 0f, 0f);
            Vector3 east = new Vector3(1f, 0f, 0f);
            Vector3 north = new Vector3(0f, 0f, 1f);

            Assert.IsTrue(WallRunMath.IsSameWall(west, west, limit),
                "the face just left is being offered again — one wall would be an infinite corridor");
            Assert.IsFalse(WallRunMath.IsSameWall(west, east, limit), "a facing pair must alternate");
            Assert.IsFalse(WallRunMath.IsSameWall(west, north, limit), "a 90 deg corner must chain");

            // 30 deg apart is still the same wall (0.85 ~ 32 deg); 40 deg is a different one.
            Vector3 near = new Vector3(-Mathf.Cos(30f * Mathf.Deg2Rad), 0f, Mathf.Sin(30f * Mathf.Deg2Rad));
            Vector3 far = new Vector3(-Mathf.Cos(40f * Mathf.Deg2Rad), 0f, Mathf.Sin(40f * Mathf.Deg2Rad));
            Assert.IsTrue(WallRunMath.IsSameWall(west, near, limit));
            Assert.IsFalse(WallRunMath.IsSameWall(west, far, limit));
        }

        // ------------------------------------------------------------------ the camera

        [Test]
        public void CameraRollCannotMoveTheAimVector()
        {
            // PlayerLook applies the wall-run lean as the Z term of the pivot's local euler. Unity
            // composes Quaternion.Euler(x, y, z) as Ry * Rx * Rz, and Rz leaves +Z fixed — so rolling is
            // the ONE camera effect that provably cannot disturb where the player is aiming. This test
            // is that proof, and it is why roll was chosen over a positional lean.
            foreach (float pitch in new[] { -60f, -20f, 0f, 35f, 80f })
            {
                Vector3 straight = Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward;
                foreach (float roll in new[] { -20f, -13f, 7f, 13f, 20f })
                {
                    Vector3 rolled = Quaternion.Euler(pitch, 0f, roll) * Vector3.forward;
                    Assert.Less((straight - rolled).magnitude, 1e-4f,
                        "roll " + roll + " at pitch " + pitch + " moved the aim vector by " +
                        (straight - rolled).magnitude.ToString("F6"));
                }
            }
        }
    }
}
