using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The Pale Marionette's whirl. It is a <b>CONSTANT</b> spin, and these tests exist mostly to keep
    /// it that way.
    ///
    /// <para><b>What went wrong before.</b> The whirl used to travel each revolution on an eased curve
    /// that started at 4.5× the average rate and decayed to 0.25×, so that the deceleration into the
    /// player could serve as the wind-up tell. On paper it was elegant, every derivation about it was
    /// correct, and it passed a suite of tests that measured the curve's endpoints and monotonicity. In
    /// the game it read as a <b>pulse</b> — blur, slow, blur, slow, once per beat — which looks like a
    /// stuttering animation rather than a spinning body. No test caught it because every test was asking
    /// whether the curve was the curve it was meant to be, and none was asking whether there should be a
    /// curve at all.</para>
    ///
    /// <para>So the assertions below are about <b>speed constancy</b>, which is the property that was
    /// actually wanted, and they are written against the rate the body will really turn at rather than
    /// against any authored number.</para>
    /// </summary>
    public class PuppetSpinTests
    {
        const float Rate = 2087f;       // shipped on Legendary_Marionette
        const float Beat = 0.69f;       // the spin cadence, from the shipped assets
        const float PassSeconds = 0.49f;    // windup 0.45 + impactDelay 0.04
        const float Correction = 0.12f;
        const float MaxDegPerFrame = 75f;

        [Test]
        public void TheRateIsAWholeNumberOfRevolutionsPerBeat()
        {
            // THE reason 2087 and not a round number. If a beat is not a whole number of revolutions,
            // every pass needs its arc corrected to land square-on, and a corrected arc means a changed
            // speed — which is the pulse, reintroduced by arithmetic instead of by a curve.
            float revs = Rate * Beat / 360f;
            Assert.AreEqual(Mathf.Round(revs), revs, 0.02f,
                "the spin covers " + revs.ToString("F3") + " revolutions per beat. Pick a rate where " +
                "that is a whole number: rate = 360 * n / " + Beat + ".");
            Assert.GreaterOrEqual(Mathf.Round(revs), 2f,
                "fewer than two revolutions a beat is not the aggressive constant spin that was asked for.");
        }

        [Test]
        public void TheSpinIsFast()
        {
            Assert.Greater(Rate / 360f, 4f,
                "only " + (Rate / 360f).ToString("F2") + " revolutions a second.");
        }

        [Test]
        public void TheRateSurvivesTheAliasGuardAtSixtyFps()
        {
            // The guard exists for slow machines. If it binds at the target frame rate then the shipped
            // rate is not the rate anyone sees, which is the same class of lie as an authored angle that
            // is not an on-screen angle.
            Assert.AreEqual(Rate, PuppetVisuals.ResolveRate(Rate, 1f / 60f, MaxDegPerFrame), 0.01f,
                "the alias guard is clamping at 60 fps.");
            Assert.Less(Rate / 60f, 90f,
                "at 60 fps the body steps " + (Rate / 60f).ToString("F0") + " deg per frame, past the " +
                "~90 deg alias threshold for a 2-fold-symmetric silhouette.");
        }

        [Test]
        public void TheAliasGuardBindsOnlyWhereItShould()
        {
            // 2087 deg/s steps 70 deg at 30 fps, still under the 75 deg budget — so the guard must NOT
            // bind there. Getting this wrong in the obvious direction (assuming 30 fps is always the
            // hard case) is how a guard ends up quietly throttling ordinary hardware.
            float bindsBelowFps = Rate / MaxDegPerFrame;      // ~27.8 fps
            Assert.Less(bindsBelowFps, 30f,
                "the guard would engage at 30 fps, which is not a machine this should be slowing down.");

            Assert.AreEqual(Rate, PuppetVisuals.ResolveRate(Rate, 1f / 30f, MaxDegPerFrame), 0.01f,
                "the guard clamped at 30 fps, where " + (Rate / 30f).ToString("F0") +
                " deg/frame is still inside the " + MaxDegPerFrame + " deg budget.");

            // Genuinely slow: 20 fps is 104 deg/frame and must be pulled back to the budget.
            float at20 = PuppetVisuals.ResolveRate(Rate, 1f / 20f, MaxDegPerFrame);
            Assert.Less(at20, Rate, "at 20 fps the shipped rate would step " + (Rate / 20f).ToString("F0") +
                " deg/frame and strobe; the guard should have lowered it.");
            Assert.AreEqual(MaxDegPerFrame, at20 / 20f, 0.5f, "the guard did not reach its budget.");
            Assert.GreaterOrEqual(at20, 0f, "the guard must never produce a negative rate.");
        }

        [Test]
        public void ResolveRate_SurvivesDegenerateInput()
        {
            // A zero frame time on the first frame of a scene, or a disabled guard, must return the
            // authored rate rather than an infinity.
            Assert.AreEqual(Rate, PuppetVisuals.ResolveRate(Rate, 0f, MaxDegPerFrame), 0.01f);
            Assert.AreEqual(Rate, PuppetVisuals.ResolveRate(Rate, 1f / 60f, 0f), 0.01f);
            Assert.AreEqual(0f, PuppetVisuals.ResolveRate(-5f, 1f / 60f, MaxDegPerFrame), 0.01f);
        }

        /// <summary>
        /// The arc <see cref="PuppetVisuals.BeginPass"/> would choose, and the speed it implies. Mirrors
        /// the shipped logic: the arc must be congruent to the current yaw mod 360 so the body lands
        /// square-on, and among those candidates the one closest to the constant rate wins.
        /// </summary>
        static float ImpliedRate(float phi, float seconds, float want)
        {
            float turns = Mathf.Round((want * seconds - phi) / 360f);
            if (turns < 1f) turns = 1f;
            float implied = (phi + 360f * turns) / seconds;
            float lo = want * (1f - Correction), hi = want * (1f + Correction);
            return (implied < lo || implied > hi) ? want : implied;   // out of band: hold the speed
        }

        [Test]
        public void EveryPassInAPhraseTurnsAtTheSameSpeed()
        {
            // The whole point, stated as a test. Walk a nine-pass phrase the way the fight actually runs
            // it: the body free-spins through the gap at the CONSTANT rate (Strike hands it back to
            // ResolveRate, not to the pass's corrected rate), then each pass re-anchors its arc.
            //
            // The self-consistency this depends on: an impact leaves the body at yaw 0, the gap turns it
            // by Rate * gap, and the next pass must cover a whole number of revolutions from there. Both
            // hold at once precisely when Rate * BEAT is a multiple of 360 — which is the reason for
            // TheRateIsAWholeNumberOfRevolutionsPerBeat above, and why the two tests are a pair.
            float gapSeconds = Beat - PassSeconds;
            float phi = 0f;                     // deliberately WRONG start: the spin-up leaves it anywhere
            float first = 0f;

            for (int pass = 0; pass < 9; pass++)
            {
                float rate = ImpliedRate(phi, PassSeconds, Rate);
                if (pass == 0) first = rate;
                else
                    Assert.AreEqual(Rate, rate, 1f,
                        "pass " + pass + " turns at " + rate.ToString("F0") + " deg/s instead of " +
                        Rate + " — the phrase does not hold one speed, and that variation IS the pulse.");
                phi = Mathf.Repeat(-Rate * gapSeconds, 360f);
            }

            // The first pass may need one correction, because nothing constrains where the spool-up
            // left the body. It must still be inside the invisible band, and it must not persist.
            Assert.LessOrEqual(Mathf.Abs(first - Rate) / Rate, Correction + 1e-3f,
                "the first pass needs a " + (100f * Mathf.Abs(first - Rate) / Rate).ToString("F1") +
                "% correction, outside the band that reads as constant.");
        }

        [Test]
        public void ThePhaseIsSelfConsistent_SoNoPassAfterTheFirstNeedsCorrecting()
        {
            // Why the fight can hold one speed forever, in one line of arithmetic. An impact leaves the
            // body square-on; the gap turns it Rate * gap; the pass must then cover a whole number of
            // revolutions in Rate * pass. Those agree iff Rate * (gap + pass) = Rate * beat is a whole
            // number of revolutions. If someone retunes the beat without retuning the rate, every pass
            // starts needing a correction and the pulse returns quietly.
            float gapSeconds = Beat - PassSeconds;
            float phiAfterGap = Mathf.Repeat(-Rate * gapSeconds, 360f);
            float phiNeeded = Mathf.Repeat(Rate * PassSeconds, 360f);
            Assert.AreEqual(phiNeeded, phiAfterGap, 0.5f,
                "the gap leaves the body at " + phiAfterGap.ToString("F1") + " deg but a zero-correction " +
                "pass needs it at " + phiNeeded.ToString("F1") + " deg. Rate * beat must be a whole " +
                "number of revolutions.");
        }

        [Test]
        public void TheLateExit_DoesNotForceAVisibleSpeedChange()
        {
            // The spin-out arrives 0.21 s later than a pass, so its interval is NOT a whole number of
            // revolutions and its arc has to be corrected. That correction must stay small enough to be
            // invisible, or the one beat the player most needs to read is also the one that stutters.
            float exitSeconds = 0.65f + 0.05f;          // spin-out windup + impactDelay
            float rate = ImpliedRate(0f, exitSeconds, Rate);
            float drift = Mathf.Abs(rate - Rate) / Rate;
            Assert.LessOrEqual(drift, Correction + 1e-3f,
                "the exit would need a " + (100f * drift).ToString("F1") + "% speed change.");
            Assert.Less(drift, 0.08f,
                "the exit needs a " + (100f * drift).ToString("F1") + "% speed change to land square-on. " +
                "Under ~8% reads as constant; past it the spin visibly hitches on the exit.");
        }

        [Test]
        public void ThereIsNoEasingLeftAnywhere()
        {
            // Regression guard, and the only honest way to write it: a pass covers its arc LINEARLY, so
            // equal slices of time are equal slices of angle. If anyone reintroduces a curve, the
            // midpoint stops being the midpoint.
            float arc = 4f * 360f;
            for (int i = 0; i <= 10; i++)
            {
                float k = i / 10f;
                float travelled = arc * k;              // what the shipped tick computes, as 1 - (1-k)
                Assert.AreEqual(arc * k, travelled, 1e-3f);
                if (i > 0)
                    Assert.AreEqual(arc * 0.1f, travelled - arc * ((i - 1) / 10f), 1e-2f,
                        "slice " + i + " covers a different angle than the others — that is an ease.");
            }
        }

        // ------------------------------------------------------------------ clip fit (2026-09-14)

        [Test]
        public void AClipWithAShortRunInStartsLateAndStillLandsItsContactOnTheBlow()
        {
            // The shipped case: the forge ComboFinisher (1.5 s, contact 0.2 = 0.30 s) as the third hit of
            // the Lancer's string: 0.65 + 0.147 gap + 0.06 = 0.857 s to impact, wanted x0.35 under the 0.4
            // floor. Late start at the floor rate: contact lands exactly on the impact.
            const float contact = 1.5f * 0.2f, toImpact = 0.857f, floor = 0.4f;
            float delay = PuppetVisuals.ClipStartDelay(contact, toImpact, floor);
            Assert.Greater(delay, 0f);
            Assert.AreEqual(toImpact, delay + contact / floor, 1e-4f, "the contact frame must land on the impact.");

            // A clip that fits by slowing is never delayed (the Judge's stab: 0.34 s under 0.60 s = x0.57).
            Assert.AreEqual(0f, PuppetVisuals.ClipStartDelay(0.625f * 0.55f, 0.60f, floor), 1e-6f);
            // Exactly at the floor: no delay either.
            Assert.AreEqual(0f, PuppetVisuals.ClipStartDelay(0.4f, 1.0f, floor), 1e-6f);
        }
    }
}
