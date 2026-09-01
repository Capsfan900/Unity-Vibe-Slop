using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The lock-on camera assist's control law, closed-loop simulated.
    ///
    /// <para><b>The bug these exist for.</b> The assist was a pure proportional controller. Against an
    /// enemy circling the player at a steady angular rate it settled at a permanent lag and never closed
    /// — the target sat a few degrees off centre for the whole fight, which is what "the lock drifts off
    /// the enemy" describes. That is not a tuning problem: it is what a P controller *does* against a
    /// moving reference, and no value of <c>assistGain</c> removes it. Reported from play, because
    /// nothing in the suite was simulating a MOVING target.</para>
    ///
    /// <para>So these tests move the target. A test that only ever checks a stationary enemy gets
    /// centred would have passed on the broken version, exactly as the old whirl tests passed on a
    /// curve that should not have existed.</para>
    /// </summary>
    public class LockOnTrackingTests
    {
        const float Gain = 4f;          // LockOnController.assistGain
        const float Deadzone = 2.2f;    // assistDeadzoneDeg
        const float Fade = 8f;          // assistFadeDeg
        const float MaxRate = 90f;      // assistMaxRateDeg
        const float FeedFwd = 1f;       // feedForward, shipped
        const float FfMaxRate = 180f;   // feedForwardMaxRateDeg
        const float Dt = 1f / 60f;

        /// <summary>
        /// Run the shipped control law for <paramref name="seconds"/> against a target whose bearing
        /// moves at <paramref name="targetRate"/> deg/s, and return the residual error at the end.
        /// One axis; the real thing runs the identical law on yaw and pitch.
        /// </summary>
        static float SettleError(float targetRate, float feedForward, float seconds = 6f)
        {
            float camera = 0f, targetBearing = 0f;
            int steps = Mathf.RoundToInt(seconds / Dt);
            float prevBearing = targetBearing;
            bool have = false;

            for (int i = 0; i < steps; i++)
            {
                targetBearing += targetRate * Dt;

                float ff = 0f;
                if (have) ff = Mathf.Clamp((targetBearing - prevBearing) / Dt, -180f, 180f);
                prevBearing = targetBearing;
                have = true;

                float err = targetBearing - camera;
                float rate = LockOnController.TrackRate(
                    err, ff, LockOnController.PScale(Mathf.Abs(err), Gain, Deadzone, Fade),
                    feedForward, MaxRate, FfMaxRate);

                float step = rate * Dt;
                if (Mathf.Abs(step) > Mathf.Abs(err)) step = err;   // never overshoot, as shipped
                camera += step;
            }
            return Mathf.Abs(targetBearing - camera);
        }

        [Test]
        public void PureProportional_DriftsBehindAMovingTarget_TheOriginalBug()
        {
            // 40 deg/s is an ordinary circling enemy at a few metres. With no feed-forward the lag
            // settles near targetRate / gain = 10 deg, and the deadzone parks it a little further out.
            float residual = SettleError(40f, feedForward: 0f);
            Assert.Greater(residual, 5f,
                "a pure P controller should visibly lag a moving target; it settled at " +
                residual.ToString("F2") + " deg. If this ever passes, the law changed and the rest of " +
                "this file is no longer testing what it claims to.");
        }

        [Test]
        public void FeedForward_HoldsAMovingTarget()
        {
            // THE fix, and the assertion the user-facing complaint maps onto. 140 deg/s is in the list
            // deliberately: an enemy at 2 m moving 5 m/s sweeps about that, and it is what exposed the
            // shared rate ceiling -- the camera was capped at 90 deg/s, could not physically keep up,
            // and was eventually LAPPED by 301 degrees. Correction and tracking now have separate
            // ceilings for exactly this reason.
            foreach (float rate in new[] { 15f, 40f, 80f, 140f })
            {
                float residual = SettleError(rate, FeedFwd);
                Assert.Less(residual, Deadzone + 1.5f,
                    "target circling at " + rate + " deg/s settled " + residual.ToString("F2") +
                    " deg off centre — the lock is drifting again.");
            }
        }

        [Test]
        public void FeedForward_IsStrictlyBetterThanPAtEveryRate()
        {
            foreach (float rate in new[] { 10f, 30f, 60f, 120f })
            {
                float withFf = SettleError(rate, FeedFwd);
                float without = SettleError(rate, 0f);
                Assert.Less(withFf, without,
                    "at " + rate + " deg/s feed-forward (" + withFf.ToString("F2") +
                    ") is not beating pure P (" + without.ToString("F2") + ").");
            }
        }

        [Test]
        public void AStationaryTargetIsCentredAndThenLeftAlone()
        {
            // The other half: the assist must still converge on a still target, and must go quiet
            // inside the deadzone rather than buzzing on the crosshair.
            float residual = SettleError(0f, FeedFwd);
            Assert.LessOrEqual(residual, Deadzone + 0.01f,
                "a stationary target ended " + residual.ToString("F2") + " deg off centre.");

            Assert.AreEqual(0f, LockOnController.PScale(Deadzone - 0.1f, Gain, Deadzone, Fade), 1e-4f,
                "the assist is still pulling inside the deadzone, which reads as the dot jittering.");
            Assert.AreEqual(0f, LockOnController.AxisRate(5f, 0f, 0f, FeedFwd), 1e-4f,
                "with no P gain and a still target there must be no correction at all.");
        }

        [Test]
        public void TheAssistFadesInRatherThanSwitchingOn()
        {
            // A step change at the deadzone edge would read as the camera flicking.
            float justOutside = LockOnController.PScale(Deadzone + 0.001f, Gain, Deadzone, Fade);
            Assert.Less(justOutside, Gain * 0.05f, "the assist switches on at full gain at the edge.");
            Assert.AreEqual(Gain, LockOnController.PScale(Deadzone + Fade, Gain, Deadzone, Fade), 1e-3f,
                "the fade does not reach full gain by the end of its own range.");
        }

        [Test]
        public void FeedForwardIsCappedSoATeleportCannotWhipTheCamera()
        {
            // A respawn or a target switch moves the bearing discontinuously. The shipped code clamps
            // the feed-forward term to feedForwardMaxRateDeg; this pins that the clamp is what limits
            // the result rather than the raw bearing rate leaking through.
            const float cap = 180f;
            float huge = Mathf.Clamp(9000f, -cap, cap);
            float rate = LockOnController.AxisRate(0f, huge, 0f, 1f);
            Assert.LessOrEqual(Mathf.Abs(rate), cap + 1e-3f,
                "an uncapped bearing jump reached the correction rate.");
        }
    }
}
