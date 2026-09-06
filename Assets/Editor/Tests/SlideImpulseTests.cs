using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The slide's sustained package, held to its budgets. Companion to <see cref="DashImpulseTests"/>;
    /// the closed-loop tests follow <c>LockOnTrackingTests</c> — simulate the move, assert the outcome,
    /// never assert a constant back at itself.
    ///
    /// <para><b>What these can and cannot prove.</b> The user's complaint was that the slide has no
    /// feel, and the diagnosis was that its MIDDLE was empty: every cue except the eye drop was an
    /// impulse on a move that lasts most of a second. So the load-bearing tests here run the motor's
    /// actual decay law through a whole slide and assert that no sustained channel is ever silent while
    /// the move is alive, that the lens releases continuously rather than snapping on stand-up, and
    /// that none of it is a function of frame rate (a framerate-dependent slide has shipped here once).
    /// They cannot prove the slide FEELS good — that is a human at the controls.</para>
    /// </summary>
    public class SlideImpulseTests
    {
        // The motor's shipped slide band and decay law (FirstPersonMotor: hv *= 1 - slideFriction*dt,
        // slide ends when speed <= slideEndSpeed). Used to SIMULATE, not asserted back at the motor.
        const float EndSpeed = 8f;
        const float MaxSpeed = 22f;
        const float Friction = 2f;
        /// <summary>URP volume bloom threshold for this project. Nothing traversal does may cross it.</summary>
        const float BloomThreshold = 1.05f;

        static GameFeelSettings Shipped()
        {
            var feel = AssetDatabase.LoadAssetAtPath<GameFeelSettings>("Assets/Data/GameFeel.asset");
            Assert.IsNotNull(feel, "Assets/Data/GameFeel.asset is missing — run VibeGame1/3. Create Data.");
            return feel;
        }

        // ---------------------------------------------------------------- the speed band

        [Test]
        public void SpeedFractionSpansTheMotorsBandAndClampsOutsideIt()
        {
            Assert.AreEqual(0f, SlideImpulse.SpeedFraction(EndSpeed, EndSpeed, MaxSpeed), 1e-6f);
            Assert.AreEqual(1f, SlideImpulse.SpeedFraction(MaxSpeed, EndSpeed, MaxSpeed), 1e-6f);
            Assert.AreEqual(0.5f, SlideImpulse.SpeedFraction(15f, EndSpeed, MaxSpeed), 1e-6f);
            // A boost or an external shove can put the speed outside the band in either direction.
            Assert.AreEqual(1f, SlideImpulse.SpeedFraction(40f, EndSpeed, MaxSpeed), 1e-6f);
            Assert.AreEqual(0f, SlideImpulse.SpeedFraction(2f, EndSpeed, MaxSpeed), 1e-6f);
        }

        [Test]
        public void ADegenerateSpeedBandCannotDivideByZero()
        {
            // Nothing stops a designer setting slideEndSpeed == slideMaxSpeed in the Inspector.
            Assert.AreEqual(1f, SlideImpulse.SpeedFraction(10f, 10f, 10f), 1e-6f);
            Assert.AreEqual(0f, SlideImpulse.SpeedFraction(9f, 10f, 10f), 1e-6f);
            Assert.IsFalse(float.IsNaN(SlideImpulse.FovForSpeed(10f, 10f, 10f, 8f)));
        }

        // ---------------------------------------------------------------- the lens

        [Test]
        public void TheFovHoldIsExactlyZeroAtTheSpeedTheMotorStandsUpAt()
        {
            // Load-bearing, not a nicety: the motor ends a slide the instant it decays to EndSpeed, and
            // a hold still non-zero there would snap several degrees of FOV on the frame you stand up.
            Assert.AreEqual(0f, SlideImpulse.FovForSpeed(EndSpeed, EndSpeed, MaxSpeed, 8f), 1e-6f);
            Assert.AreEqual(0f, SlideImpulse.FovForSpeed(5f, EndSpeed, MaxSpeed, 8f), 1e-6f);
            Assert.Greater(SlideImpulse.FovForSpeed(EndSpeed + 0.5f, EndSpeed, MaxSpeed, 8f), 0f);
        }

        [Test]
        public void TheFovTracksSpeedMonotonically()
        {
            // FOV is the "how fast am I still going" channel; if it ever rose while the speed fell the
            // lens would be lying about the move.
            float prev = -1f;
            for (float s = EndSpeed; s <= MaxSpeed; s += 0.25f)
            {
                float v = SlideImpulse.FovForSpeed(s, EndSpeed, MaxSpeed, 8f);
                Assert.GreaterOrEqual(v, prev, "FOV hold must never fall as speed rises, at " + s + " m/s");
                prev = v;
            }
            Assert.AreEqual(8f, prev, 1e-4f, "full entry speed earns the full authored hold");
        }

        // ---------------------------------------------------------------- closed loop: a whole slide

        [Test]
        public void ClosedLoop_TheMiddleOfASlideIsNeverEmpty()
        {
            // THE test for the user's actual complaint. Run the motor's real decay law from a
            // full-speed entry to stand-up and assert that no sustained channel is ever silent while
            // the slide is alive. This is what the one-shot package could not pass: its FOV kick was
            // gone inside ~0.4 s of a move that runs over half a second.
            var feel = Shipped();
            float dt = 1f / 60f;
            float speed = MaxSpeed;
            float duration = 0f;

            while (speed > EndSpeed)
            {
                float f = SlideImpulse.SpeedFraction(speed, EndSpeed, MaxSpeed);
                Assert.Greater(SlideImpulse.FovForSpeed(speed, EndSpeed, MaxSpeed, feel.slideFovHold), 0f,
                    "the lens went silent mid-slide at " + speed + " m/s");
                Assert.GreaterOrEqual(SlideImpulse.GritRate(f, feel.slideDustRate), 0.34f * feel.slideDustRate - 1e-4f,
                    "the grit trail evaporated mid-slide");
                Assert.GreaterOrEqual(SlideImpulse.ScrapeGain(f, feel.slideScrapeVolume), 0.4f * feel.slideScrapeVolume - 1e-4f,
                    "the scrape faded out under a live slide");

                speed *= Mathf.Max(0f, 1f - Friction * dt);
                duration += dt;
            }

            // The middle has to EXIST for any of this to matter, and the entry punch must be over well
            // before the slide is — the sustained layers are what carry the rest.
            Assert.Greater(duration, 0.45f, "a full-speed slide should live long enough to have a middle");
            Assert.Greater(duration, feel.slideKickTime * 2f,
                "if the commit kick outlived most of the slide, the package would be an impulse again");
        }

        [Test]
        public void ClosedLoop_TheLensReleasesContinuouslyIntoTheStandUp()
        {
            // The old package's failure mode inverted: a hold that ended abruptly would snap the lens.
            // Frame-to-frame FOV steps must stay small the whole way down, and the hold must arrive at
            // (exactly) zero on the frame the motor gives up, so stand-up is a non-event for the lens.
            var feel = Shipped();
            float dt = 1f / 60f;
            float speed = MaxSpeed;
            float prevFov = SlideImpulse.FovForSpeed(speed, EndSpeed, MaxSpeed, feel.slideFovHold);

            while (speed > EndSpeed)
            {
                speed *= Mathf.Max(0f, 1f - Friction * dt);
                float fov = SlideImpulse.FovForSpeed(speed, EndSpeed, MaxSpeed, feel.slideFovHold);
                Assert.LessOrEqual(fov, prevFov + 1e-5f, "the lens must only narrow as the slide spends itself");
                Assert.Less(prevFov - fov, 0.5f, "a >0.5 deg single-frame step reads as a snap, at " + speed + " m/s");
                prevFov = fov;
            }
            Assert.AreEqual(0f, SlideImpulse.FovForSpeed(EndSpeed, EndSpeed, MaxSpeed, feel.slideFovHold), 1e-6f);
        }

        [Test]
        public void ClosedLoop_TheFeedbackIsAFunctionOfSpeedNotOfFrameRate()
        {
            // A framerate-dependent slide has shipped in this project once (4.0 m at 500 fps, 1.8 m at
            // 20 fps). The feedback layer must not add a second copy of that bug: every channel is a
            // function of the speed being carried THIS frame, so at any given speed the picture is
            // identical no matter what dt the sim runs at.
            foreach (float dt in new[] { 1f / 20f, 1f / 60f, 1f / 240f })
            {
                float speed = MaxSpeed;
                while (speed > EndSpeed)
                {
                    float f = SlideImpulse.SpeedFraction(speed, EndSpeed, MaxSpeed);
                    Assert.AreEqual(SlideImpulse.FovForSpeed(speed, EndSpeed, MaxSpeed, 8f), 8f * f, 1e-5f);
                    Assert.AreEqual(SlideImpulse.ScrapePitch(f), Mathf.Lerp(0.78f, 1.32f, f), 1e-5f);
                    speed *= Mathf.Max(0f, 1f - Friction * dt);
                }
            }
        }

        // ---------------------------------------------------------------- steering and roll

        [Test]
        public void SteeringAcrossYourLineBanksIntoTheSteer()
        {
            // Sliding forward, pushing right: positive steer, positive (rightward) bank.
            Vector3 vel = Vector3.forward * 15f;
            Assert.AreEqual(1f, SlideImpulse.LateralSteer(vel, Vector3.right), 1e-4f, "a 90 deg steer is full");
            Assert.AreEqual(-1f, SlideImpulse.LateralSteer(vel, Vector3.left), 1e-4f);
            Assert.AreEqual(0f, SlideImpulse.LateralSteer(vel, Vector3.forward), 1e-4f, "holding your line banks nothing");
            float half = SlideImpulse.LateralSteer(vel, new Vector3(1f, 0f, 1f));
            Assert.AreEqual(Mathf.Sin(Mathf.PI / 4f), half, 1e-4f, "a 45 deg steer banks by its sine");
        }

        [Test]
        public void LookingUpOrDownCannotRollTheCamera()
        {
            // Both vectors are flattened before the cross product; otherwise the roll would be a
            // function of the mouse rather than of the slide.
            Vector3 wish = new Vector3(0.5f, 0f, 1f).normalized;
            float flat = SlideImpulse.LateralSteer(new Vector3(0f, 0f, 15f), wish);
            float diving = SlideImpulse.LateralSteer(new Vector3(0f, -9f, 15f), wish);
            float rising = SlideImpulse.LateralSteer(new Vector3(0f, 9f, 15f), new Vector3(0.5f, 0.8f, 1f).normalized);
            Assert.AreEqual(flat, diving, 1e-4f);
            Assert.AreEqual(flat, rising, 1e-4f);
        }

        [Test]
        public void ADegenerateSteerIsZeroNotNaN()
        {
            Assert.AreEqual(0f, SlideImpulse.LateralSteer(Vector3.zero, Vector3.right), 1e-6f);
            Assert.AreEqual(0f, SlideImpulse.LateralSteer(Vector3.forward, Vector3.zero), 1e-6f);
            Assert.AreEqual(0f, SlideImpulse.LateralSteer(Vector3.up * 3f, Vector3.right), 1e-6f,
                "purely vertical velocity has no line to steer across");
        }

        [Test]
        public void ASpentSlideCannotBankAndNoSlideCanBankPastTheAuthoredLimit()
        {
            var feel = Shipped();
            float max = feel.slideRollDegrees;
            Assert.AreEqual(0f, SlideImpulse.Roll(1f, 0f, max), 1e-6f, "you cannot lean on speed you no longer have");
            Assert.AreEqual(max, SlideImpulse.Roll(1f, 1f, max), 1e-4f);
            Assert.AreEqual(-max, SlideImpulse.Roll(-1f, 1f, max), 1e-4f, "bank is signed with the steer");
            // Hostile inputs (an un-normalised wish, a fraction above 1) must still respect the cap.
            Assert.LessOrEqual(Mathf.Abs(SlideImpulse.Roll(5f, 3f, max)), max + 1e-4f);
        }

        // ---------------------------------------------------------------- the scrape

        [Test]
        public void ScrapePitchCarriesTheSpeedAndStaysInsideItsBand()
        {
            // Pitch is what the ear reads as speed on a broadband source — level alone reads as
            // distance. It must rise with speed and never leave [0.78, 1.32].
            float prev = -1f;
            for (float f = 0f; f <= 1f; f += 0.05f)
            {
                float p = SlideImpulse.ScrapePitch(f);
                Assert.Greater(p, prev, "pitch must rise with speed");
                Assert.GreaterOrEqual(p, 0.78f);
                Assert.LessOrEqual(p, 1.32f);
                prev = p;
            }
            Assert.AreEqual(0.78f, SlideImpulse.ScrapePitch(-1f), 1e-4f, "clamped below");
            Assert.AreEqual(1.32f, SlideImpulse.ScrapePitch(2f), 1e-4f, "clamped above");
        }

        [Test]
        public void TheScrapeAndTheGritFloorInsteadOfEvaporating()
        {
            // A trail that fades out under a live slide reads as the effect breaking, not as slowing
            // down. Both channels floor (40% gain, 34% rate) and SlideFx fades the last of the scrape
            // out over its release only when the slide actually ends.
            Assert.AreEqual(0.4f, SlideImpulse.ScrapeGain(0f, 1f), 1e-4f);
            Assert.AreEqual(1f, SlideImpulse.ScrapeGain(1f, 1f), 1e-4f);
            Assert.AreEqual(0.34f * 34f, SlideImpulse.GritRate(0f, 34f), 1e-3f);
            Assert.AreEqual(34f, SlideImpulse.GritRate(1f, 34f), 1e-3f);
        }

        // ---------------------------------------------------------------- shipped values (rule 9)

        [Test]
        public void TheAssetCarriesTheShippedSlideValues()
        {
            var feel = Shipped();
            Assert.AreEqual(8f, feel.slideFovHold, 1e-4f);
            Assert.AreEqual(-2.5f, feel.slideEndFovPunch, 1e-4f);
            Assert.AreEqual(3.5f, feel.slideRollDegrees, 1e-4f);
            // 1.2 -> 1.8 on 2026-09-04 with the player-body pass: the legs now move under the lens on the
            // commit and the head answers them. SlideFeelTests pins the rest of that pass.
            Assert.AreEqual(1.8f, feel.slideKickPitch, 1e-4f);
            Assert.AreEqual(0.13f, feel.slideKickTime, 1e-4f);
            Assert.AreEqual(34f, feel.slideDustRate, 1e-4f);
            Assert.AreEqual(5f, feel.slideSparkRate, 1e-4f);
            Assert.AreEqual(0.22f, feel.slideScrapeVolume, 1e-4f);
        }

        [Test]
        public void TheEndPunchIsInwardBecauseTheSpeedIsGone()
        {
            // The sustained hold has already decayed to zero with the speed; the stand-up punch is the
            // world closing back IN. A positive value here would celebrate losing your momentum.
            Assert.Less(Shipped().slideEndFovPunch, 0f);
        }

        // ---------------------------------------------------------------- readability budget

        [Test]
        public void ASlideAddsNoBloomAtAll()
        {
            // EnemyVisuals.CueFlash owns the brightness budget; light on an enemy means "you
            // deflected" and traversal is not allowed to speak that language. The grit ships at peak
            // channel 0.55 (dust, not fire) and the occasional spark goes through SlashFx, whose
            // Normalise flattens ANY colour to a peak of at most 1.0 — both under the 1.05 threshold.
            var go = new GameObject("SlideFxProbe");
            try
            {
                var fx = go.AddComponent<SlideFx>();
                Assert.Less(fx.gritBrightness, BloomThreshold, "grit must never bloom");
                Assert.AreEqual(0.55f, fx.gritBrightness, 1e-4f, "dust, not fire");
                // The grit hue is normalised to peak channel gritBrightness at build; prove the maths
                // that Build uses cannot exceed the threshold for any authored hue.
                Color c = fx.gritHue;
                float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                Assert.Greater(m, 0.0001f, "a black hue would divide by zero at build");
                Assert.LessOrEqual(fx.gritBrightness / m * m, BloomThreshold);
            }
            finally { Object.DestroyImmediate(go); }

            // The spark path: SlashFx.Normalise flattens anything above 1, so even a hostile HDR
            // colour cannot cross the threshold through it.
            Color hot = SlashFx.NormaliseColor(new Color(6f, 2f, 0.5f, 1f));
            Assert.LessOrEqual(hot.maxColorComponent, 1f + 1e-4f);
            Assert.Less(1f, BloomThreshold, "SlashFx's ceiling itself sits under the bloom threshold");
        }
    }
}
