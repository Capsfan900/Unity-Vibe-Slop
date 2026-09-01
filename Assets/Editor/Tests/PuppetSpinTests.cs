using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The Pale Marionette's whirl maths — <see cref="PuppetVisuals.Ease"/> and
    /// <see cref="PuppetVisuals.ResolvePeak"/>. Pure functions, so they belong here rather than in a
    /// play-mode suite, and they are worth pinning for one specific reason.
    ///
    /// <para><b>The bug these exist for.</b> The arrival curve used to be
    /// <c>w(1-(1-k)³) + (1-w)k</c> with <c>w = clamp01((peak-1)/2)</c>. That saturates at
    /// <c>peak = 3</c>: every authored value from 3 up to the Inspector's own maximum of 4 produced a
    /// byte-identical curve. The field could be raised and the body would not turn one degree faster,
    /// with nothing in the console and nothing in a test — the same class of trap as an authored angle
    /// that is not an on-screen angle, which cost this project eleven wind-up poses. So the peak is
    /// asserted here as a MEASURED derivative of the curve, never as the value that was passed in.</para>
    /// </summary>
    public class PuppetSpinTests
    {
        const float Peak = 4.5f;    // shipped on Legendary_Marionette
        const float Tail = 0.25f;

        /// <summary>Numeric derivative, which is the whole point: measure the curve, do not trust the input.</summary>
        static float Slope(float k, float peak, float tail, float h = 1e-4f)
        {
            float a = Mathf.Clamp01(k - h * 0.5f), b = Mathf.Clamp01(k + h * 0.5f);
            return (PuppetVisuals.Ease(b, peak, tail) - PuppetVisuals.Ease(a, peak, tail)) / (b - a);
        }

        [Test]
        public void Ease_HitsBothEndpointsExactly()
        {
            // f(0) = 0 and f(1) = 1 are not cosmetic. f(1) != 1 means the body is NOT square-on to the
            // player at the impact instant, which is the one invariant the whirl has.
            Assert.AreEqual(0f, PuppetVisuals.Ease(0f, Peak, Tail), 1e-5f);
            Assert.AreEqual(1f, PuppetVisuals.Ease(1f, Peak, Tail), 1e-5f);
        }

        [Test]
        public void Ease_StartsAtThePeakAndArrivesAtTheTail()
        {
            // Both endpoints mean literally what the field names say, as multiples of the average rate.
            Assert.AreEqual(Peak, Slope(0f, Peak, Tail), 0.02f,
                "the curve does not actually start at " + Peak + "x the average rate.");
            Assert.AreEqual(Tail, Slope(1f, Peak, Tail), 0.02f,
                "the curve does not actually arrive at " + Tail + "x the average rate.");
        }

        [Test]
        public void Ease_IsMonotone_SoTheBodyNeverTravelsBackwards()
        {
            // A non-monotone arrival curve would rotate the puppet backwards mid-pass, which reads as a
            // second, different move rather than as one continuous arrival.
            float prev = -1f;
            for (int i = 0; i <= 400; i++)
            {
                float v = PuppetVisuals.Ease(i / 400f, Peak, Tail);
                Assert.GreaterOrEqual(v, prev, "Ease went backwards at k=" + (i / 400f));
                prev = v;
            }
        }

        [Test]
        public void Ease_Decelerates_TheWholeWay()
        {
            // The deceleration IS the wind-up. If the curve ever sped up on approach, the cue would
            // land on a body that was getting FASTER, and the tell would say the opposite of the truth.
            for (int i = 1; i <= 40; i++)
            {
                float k0 = (i - 1) / 40f, k1 = i / 40f;
                Assert.LessOrEqual(Slope(k1, Peak, Tail), Slope(k0, Peak, Tail) + 1e-3f,
                    "the whirl accelerated between k=" + k0 + " and k=" + k1 + ".");
            }
        }

        [Test]
        public void Ease_ReducesExactlyToTheOldCubic_AtTheOldValues()
        {
            // The new form is a widening of the reachable range, NOT a re-tune of the shape: at the
            // values that shipped before (peak 2.5, tail 0.25) both give m = 0.75, p = 3. Asserting
            // this is what makes it safe to say the fight got faster BECAUSE of the authored numbers
            // rather than because the curve quietly changed underneath them.
            for (int i = 0; i <= 100; i++)
            {
                float k = i / 100f, inv = 1f - k;
                float old = 0.75f * (1f - inv * inv * inv) + 0.25f * k;
                Assert.AreEqual(old, PuppetVisuals.Ease(k, 2.5f, 0.25f), 1e-4f, "diverged at k=" + k);
            }
        }

        [Test]
        public void Ease_ActuallyRespondsAboveThreeX_TheRegressionThatMotivatedThisFile()
        {
            // THE test. Under the old cubic these two were byte-identical, because w clamped at 1.
            float atThree = Slope(0f, 3f, Tail);
            float atShipped = Slope(0f, Peak, Tail);
            Assert.Greater(atShipped, atThree + 1f,
                "peak " + Peak + " starts at " + atShipped.ToString("F2") + "x and peak 3 starts at " +
                atThree.ToString("F2") + "x — the curve has stopped responding above 3x again, so the " +
                "Inspector value is decorative and the whirl is silently back to the slow version.");

            // And the range is genuinely open at the top of the Inspector's slider, so nobody authors
            // a value the maths cannot deliver.
            Assert.AreEqual(8f, Slope(0f, 8f, Tail), 0.05f);
        }

        [Test]
        public void ResolvePeak_IsSlackAtSixtyFps_AndBindsAtThirty()
        {
            // One revolution in the shipped 0.49 s pass.
            float average = 360f / 0.49f;    // ~735 deg/s

            float at60 = PuppetVisuals.ResolvePeak(Peak, Tail, average, 1f / 60f, 75f);
            Assert.AreEqual(Peak, at60, 1e-3f,
                "the alias guard is clamping on hardware that is hitting the target frame rate, which " +
                "means the shipped peak is not the peak anyone sees.");

            float at30 = PuppetVisuals.ResolvePeak(Peak, Tail, average, 1f / 30f, 75f);
            Assert.Less(at30, Peak,
                "at 30 fps the shipped peak would step " + (Peak * average / 30f).ToString("F0") +
                " deg per frame and strobe; the guard should have lowered it.");
            Assert.GreaterOrEqual(at30, 1f, "the guard must never flatten the turn past uniform.");
            Assert.LessOrEqual(at30 * average / 30f, 75.5f, "the guard did not actually reach its budget.");
        }

        [Test]
        public void ResolvePeak_NeverGoesBelowUniform_AndSurvivesDegenerateInput()
        {
            // Below 1 the "deceleration" would make the arrival slower than a plain constant spin —
            // a worse read than the aliasing it is avoiding. And a zero/NaN-adjacent frame time on the
            // first frame of a scene must not produce a nonsense curve.
            Assert.GreaterOrEqual(PuppetVisuals.ResolvePeak(Peak, Tail, 5000f, 1f / 10f, 75f), 1f);
            Assert.GreaterOrEqual(PuppetVisuals.ResolvePeak(Peak, Tail, 0f, 1f / 60f, 75f), 1f);
            Assert.GreaterOrEqual(PuppetVisuals.ResolvePeak(Peak, Tail, 735f, 0f, 75f), 1f);
            Assert.GreaterOrEqual(PuppetVisuals.ResolvePeak(Peak, Tail, 735f, 1f / 60f, 0f), 1f);
        }

        [Test]
        public void TheCueLandsWithTheBodyStillVisiblyOffAndSlowing()
        {
            // The claim the DataFactory comment makes, checked rather than asserted in prose: at the
            // cue the body must still be far enough round that "it is coming around at you" is a real
            // read, and it must be slowing, so the flash and the silhouette say the same thing.
            const float cueLead = 0.28f;
            float passSeconds = 0.45f + 0.04f;          // windup + impactDelay
            float k = (passSeconds - cueLead) / passSeconds;

            float travelled = PuppetVisuals.Ease(k, Peak, Tail);
            float degreesOff = 360f * (1f - travelled);
            Assert.That(degreesOff, Is.InRange(40f, 110f),
                "at the cue the body is " + degreesOff.ToString("F0") + " deg from alignment. Under ~40 " +
                "it is already facing you and the cue is the only tell left; over ~110 it is still in " +
                "the blur and the cue reads as unrelated to the body.");

            Assert.Less(Slope(k, Peak, Tail), 1f,
                "at the cue the body is still turning faster than its own average, so it does not yet " +
                "read as decelerating — and the deceleration is supposed to BE the wind-up.");
        }
    }
}
