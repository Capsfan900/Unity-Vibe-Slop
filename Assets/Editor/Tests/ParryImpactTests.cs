using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The deflect's impact package, held to its budgets.
    ///
    /// <para><b>What these can and cannot prove.</b> They prove the SHAPES — that the camera kick rises
    /// fast and returns to exactly zero, that the hitstop's onset is still binary and only its release
    /// is stepped, that the whole package costs less world time than the numbers it must not disturb,
    /// and that the shipped asset carries the shipped values rather than the code defaults (rule 9).
    /// They cannot prove that a deflect FEELS better. That judgement is a human at the controls, and
    /// nothing in this file is a substitute for it.</para>
    ///
    /// <para>The three constraints every assertion here defends:</para>
    /// <list type="number">
    ///   <item>the perfect window is 0.13 s and nothing may add input latency to it;</item>
    ///   <item>the enemy cue must stay the loudest event in the frame;</item>
    ///   <item>light on an enemy means "you deflected", never "an attack is happening".</item>
    /// </list>
    /// </summary>
    public class ParryImpactTests
    {
        const float PerfectWindow = 0.13f;   // PlayerStatsData.parryPerfectWindow
        const float CueLead = 0.28f;         // ~0.20 + perfectWindow/2, see ANIMATION-VFX section 2
        const float Frame = 1f / 60f;

        static GameFeelSettings Shipped()
        {
            var feel = AssetDatabase.LoadAssetAtPath<GameFeelSettings>("Assets/Data/GameFeel.asset");
            Assert.IsNotNull(feel, "Assets/Data/GameFeel.asset is missing — run VibeGame1/3. Create Data.");
            return feel;
        }

        // ---------------------------------------------------------------- camera kick envelope

        [Test]
        public void KickStartsAndEndsAtExactlyZero()
        {
            // A kick that does not return to zero is a permanent camera offset, and ShakeRoot sits
            // between the look pivot and the lens: any residue is a crooked view for the rest of the run.
            Assert.AreEqual(0f, ParryImpulse.KickCurve(0f, ParryImpulse.KickAttackFraction), 1e-6f);
            Assert.AreEqual(0f, ParryImpulse.KickCurve(1f, ParryImpulse.KickAttackFraction), 1e-6f);
            Assert.AreEqual(0f, ParryImpulse.KickCurve(1.5f, ParryImpulse.KickAttackFraction), 1e-6f);
            Assert.AreEqual(0f, ParryImpulse.KickCurve(-0.2f, ParryImpulse.KickAttackFraction), 1e-6f);
        }

        [Test]
        public void KickPeaksAtTheEndOfItsAttackAndOnlyThere()
        {
            float a = ParryImpulse.KickAttackFraction;
            Assert.AreEqual(1f, ParryImpulse.KickCurve(a, a), 1e-4f, "peak must be exactly the authored amplitude");

            float prev = -1f;
            for (float t = 0f; t <= a; t += a / 40f)      // monotone up through the attack
            {
                float v = ParryImpulse.KickCurve(t, a);
                Assert.GreaterOrEqual(v, prev - 1e-5f, "kick must not stutter on the way out");
                prev = v;
            }
            prev = 2f;
            for (float t = a; t <= 1f; t += 0.01f)        // monotone down through the settle
            {
                float v = ParryImpulse.KickCurve(t, a);
                Assert.LessOrEqual(v, prev + 1e-5f, "kick must not bounce on the way back");
                prev = v;
            }
        }

        [Test]
        public void MostOfTheKickIsDeliveredInTheFirstFrame()
        {
            // The whole reason for an ease-OUT rise: the blow has to be IN the first frame the player
            // sees, or the eye reads a camera drift instead of an impact. At the shipped 0.16 s
            // lifetime one frame is t = 0.0104, i.e. 6.5% of the life and 36% of the attack leg.
            var feel = Shipped();
            float oneFrame = Frame / feel.parryKickTime;
            float v = ParryImpulse.KickCurve(oneFrame, ParryImpulse.KickAttackFraction);
            Assert.Greater(v, 0.55f, "less than 55% of the kick in frame one reads as a drift, not a hit");
        }

        [Test]
        public void KickIsOverBeforeTheNextParryCue()
        {
            // Constraint 1, restated as geometry: the camera must be dead still again before the next
            // "parry now" signal, or the player is asked to time a 0.13 s window through a moving frame.
            var feel = Shipped();
            Assert.Less(feel.parryKickTime, CueLead,
                "the kick must expire inside the cue lead (~0.28 s)");
            Assert.Less(feel.parryKickTime, PerfectWindow + 0.05f,
                "and it should be roughly window-length, not a lingering sway");
        }

        // ---------------------------------------------------------------- kick direction

        [Test]
        public void FrontalBlowHasNoYawAndNoRoll()
        {
            var k = ParryImpulse.FromBlow(Vector3.forward, 1.6f, 1.1f, 1.3f, 0.035f);
            Assert.AreEqual(0f, k.euler.y, 1e-5f, "a blow straight ahead exerts no sideways force");
            Assert.AreEqual(0f, k.euler.z, 1e-5f);
            Assert.AreEqual(0f, k.offset.x, 1e-5f);
            // ...but it still lands: the pitch and the sink are direction-independent.
            Assert.Less(k.euler.x, 0f, "the view must still pitch UP on a frontal deflect");
            Assert.Less(k.offset.y, 0f, "the head must still sink");
        }

        [Test]
        public void KickTurnsTheViewAwayFromTheBlow()
        {
            // Attacker on the player's RIGHT (+x in camera space). Unity yaw is positive to the right,
            // so turning away is a NEGATIVE yaw, and the head slides to the left (-x).
            var right = ParryImpulse.FromBlow(Vector3.right, 1.6f, 1.1f, 1.3f, 0.035f);
            Assert.Less(right.euler.y, 0f, "a blow from the right must yaw the view left");
            Assert.Less(right.offset.x, 0f, "and shove the head left");
            Assert.Greater(right.euler.z, 0f, "roll signs with the blow");

            var left = ParryImpulse.FromBlow(Vector3.left, 1.6f, 1.1f, 1.3f, 0.035f);
            Assert.AreEqual(-right.euler.y, left.euler.y, 1e-5f, "the two sides must be exact mirrors");
            Assert.AreEqual(-right.euler.z, left.euler.z, 1e-5f);
            Assert.AreEqual(-right.offset.x, left.offset.x, 1e-6f);
            Assert.AreEqual(right.euler.x, left.euler.x, 1e-6f, "pitch is not directional");
        }

        [Test]
        public void KickIsBoundedFarBelowTheAimBudget()
        {
            // Constraint 1 again, on the other axis. The camera is the aim source, so a big kick would
            // cost the player their next swing. At 95 deg FOV, 1.6 deg of pitch is 1.7% of screen
            // height — visible as force, invisible as a handicap.
            var feel = Shipped();
            float worst = Mathf.Abs(feel.parryKickPitch) + Mathf.Abs(feel.parryKickYaw);
            Assert.Less(worst, 4f, "combined pitch+yaw over 4 deg starts to cost the player their aim");
            Assert.Less(Mathf.Abs(feel.parryKickOffset), 0.08f, "over 8 cm of head travel reads as a stumble");
        }

        [Test]
        public void KickIsSmallerThanTheHitTakenShake()
        {
            // Readability ladder: a deflect you WON must not shove the camera harder than a hit you
            // ATE. Big() is 0.30 amplitude; the kick's positional peak is 3.5 cm.
            var feel = Shipped();
            Assert.Less(feel.parryKickOffset, feel.shakeBigAmp,
                "a won deflect must not out-shove a hit taken");
        }

        // ---------------------------------------------------------------- hitstop shape (gap 3.4)

        [Test]
        public void HitStopOnsetIsStillBinary()
        {
            // The whole verdict on gap 3.4 in one assertion: there is NO ramp INTO the freeze. Sampled
            // at every frame boundary from the instant of contact, the scale is the freeze scale and
            // nothing else. A ramp in would replace the one frame the eye can call "the hit" with a
            // smear, and this game's deflect is built on that frame.
            var feel = Shipped();
            for (float t = 0f; t < feel.parryHitStop - 1e-4f; t += Frame * 0.25f)
            {
                float s = ParryImpulse.WorldScaleAt(t, feel.parryHitStop, feel.hitStopScale,
                                                    feel.parryHitStopRelease, feel.parryHitStopReleaseScale);
                Assert.AreEqual(feel.hitStopScale, s, 1e-5f, "onset must be a step, never a ramp");
            }
            Assert.AreEqual(1f, ParryImpulse.WorldScaleAt(-0.001f, feel.parryHitStop, feel.hitStopScale,
                                                          feel.parryHitStopRelease, feel.parryHitStopReleaseScale), 1e-5f);
        }

        [Test]
        public void HitStopReleaseIsStrictlyMonotoneBackToRealTime()
        {
            var feel = Shipped();
            float prev = 0f;
            float total = feel.parryHitStop + feel.parryHitStopRelease;
            for (float t = feel.parryHitStop; t <= total + 0.05f; t += 0.001f)
            {
                float s = ParryImpulse.WorldScaleAt(t, feel.parryHitStop, feel.hitStopScale,
                                                    feel.parryHitStopRelease, feel.parryHitStopReleaseScale);
                Assert.GreaterOrEqual(s, prev - 1e-5f, "the world must never slow down again on the way out");
                prev = s;
            }
            Assert.AreEqual(1f, ParryImpulse.WorldScaleAt(total + 1e-4f, feel.parryHitStop, feel.hitStopScale,
                                                          feel.parryHitStopRelease, feel.parryHitStopReleaseScale), 1e-5f,
                            "and it must actually reach real time, not asymptote at it");
        }

        [Test]
        public void ReleaseStepsAreDistinguishableAndOrdered()
        {
            var feel = Shipped();
            float second = ParryImpulse.SecondStepScale(feel.parryHitStopReleaseScale);
            Assert.Greater(second, feel.parryHitStopReleaseScale + 0.1f,
                "two steps closer than 0.1 in scale are one step wearing a hat");
            Assert.Less(second, 1f);

            // Each step must last at least one frame or it is a number nobody ever renders.
            float first = feel.parryHitStopRelease * ParryImpulse.ReleaseFirstStepFraction;
            Assert.GreaterOrEqual(first, Frame, "first release step is shorter than a frame");
            Assert.GreaterOrEqual(feel.parryHitStopRelease - first, Frame, "second release step is shorter than a frame");
        }

        [Test]
        public void ZeroReleaseRestoresTheOldPureBinaryHitStopExactly()
        {
            // The escape hatch has to be real, or "prototype it and be prepared to keep what exists"
            // is not something the project can actually act on.
            Assert.AreEqual(0.02f, ParryImpulse.WorldScaleAt(0.05f, 0.09f, 0.02f, 0f, 0.45f), 1e-6f);
            Assert.AreEqual(1f, ParryImpulse.WorldScaleAt(0.09f, 0.09f, 0.02f, 0f, 0.45f), 1e-6f);
            Assert.AreEqual(0f, ParryImpulse.LostWorldSeconds(0.09f, 0.02f, 0f, 0.45f)
                              - 0.09f * 0.98f, 1e-6f);
        }

        [Test]
        public void WholeHitStopCostsLessWorldTimeThanOnePerfectWindow()
        {
            // The budget that keeps constraint 1 true. The player runs at 1.0 throughout (rule 1), so
            // this figure is exactly how far the rest of the fight — the next cue included — is pushed
            // back in REAL time. Everything in the world shifts by the same amount, so no relative
            // timing changes; the ceiling is here so the shift can never grow into a stall.
            var feel = Shipped();
            float lost = ParryImpulse.LostWorldSeconds(feel.parryHitStop, feel.hitStopScale,
                                                       feel.parryHitStopRelease, feel.parryHitStopReleaseScale);
            Assert.Less(lost, PerfectWindow, "the whole freeze must cost less than one perfect window");

            float added = lost - feel.parryHitStop * (1f - feel.hitStopScale);
            Assert.Less(added, 0.04f, "the release tail alone must stay under 40 ms of world time");
            Assert.Greater(added, 0.015f, "under 15 ms and the tail is not worth the complexity");
        }

        [Test]
        public void HitStopStaircaseMatchesWhatTimeScaleControllerWillActuallyResolve()
        {
            // ParryImpact issues TWO overlapping requests and relies on TimeScaleController resolving
            // overlaps by taking the SMALLEST scale. This replays that resolution independently and
            // checks it reproduces WorldScaleAt — otherwise the tested shape and the shipped shape are
            // two different things, which is the exact failure mode this suite exists to catch.
            var feel = Shipped();
            float freeze = feel.parryHitStop, fs = feel.hitStopScale;
            float rt = feel.parryHitStopRelease, rs = feel.parryHitStopReleaseScale;
            float first = rt * ParryImpulse.ReleaseFirstStepFraction;
            float second = ParryImpulse.SecondStepScale(rs);

            for (float t = 0f; t < freeze + rt + 0.02f; t += 0.0005f)
            {
                float resolved = 1f;
                if (t < freeze) resolved = Mathf.Min(resolved, fs);                 // PlayerCombat's freeze
                if (t < freeze + first) resolved = Mathf.Min(resolved, rs);         // ParryImpact step 1
                if (t < freeze + rt) resolved = Mathf.Min(resolved, second);        // ParryImpact step 2
                Assert.AreEqual(ParryImpulse.WorldScaleAt(t, freeze, fs, rt, rs), resolved, 1e-5f,
                                "min-wins composition diverged from the modelled staircase at t=" + t);
            }
        }

        // ---------------------------------------------------------------- audio

        [Test]
        public void DeflectAudioLayersAreSpectrallySeparatedAndQuiet()
        {
            var layers = ParryImpulse.DeflectLayers;
            Assert.AreEqual(2, layers.Length, "two voices around the existing Sfx.Parry, no more");

            float high = 0f, low = float.MaxValue, sum = 0f;
            for (int i = 0; i < layers.Length; i++)
            {
                Assert.Greater(layers[i].volume, 0f);
                Assert.Greater(layers[i].pitch, 0f);
                Assert.Greater(layers[i].jitter, 0f, "a layer with no jitter machine-guns in a flurry");
                high = Mathf.Max(high, layers[i].pitch);
                low = Mathf.Min(low, layers[i].pitch);
                sum += layers[i].volume;
                // Constraint: these must sit UNDER the Sfx.Parry PlayerCombat plays at unity gain.
                Assert.Less(layers[i].volume, 1f, "a layer must never out-shout the deflect itself");
            }
            Assert.Greater(high / low, 3f, "under a ~1.5 octave spread the layers just sound like flanging");
            Assert.Less(sum, 1f, "layers are spectral width, not volume");
        }

        [Test]
        public void DeflectAudioUsesOnlyExistingSfxEntries()
        {
            // Rule 7: Sfx enum names are folder names under Resources/Audio/Sfx and are append-only.
            // A layer referencing an entry that does not exist is a silent no-op at runtime, which is
            // exactly the kind of failure a batch test can catch and a playtest cannot.
            foreach (var l in ParryImpulse.DeflectLayers)
                Assert.IsTrue(System.Enum.IsDefined(typeof(Sfx), l.sfx), "unknown Sfx " + l.sfx);
        }

        // ---------------------------------------------------------------- rule 9

        [Test]
        public void ShippedAssetCarriesTheShippedValuesNotTheCodeDefaults()
        {
            // Rule 9. GameFeel.asset predates every field below, so an un-rerun DataFactory would leave
            // them all at zero and the entire package would silently do nothing.
            var feel = Shipped();
            Assert.AreEqual(1.6f, feel.parryKickPitch, 1e-4f);
            Assert.AreEqual(1.1f, feel.parryKickYaw, 1e-4f);
            Assert.AreEqual(1.3f, feel.parryKickRoll, 1e-4f);
            Assert.AreEqual(0.035f, feel.parryKickOffset, 1e-5f);
            Assert.AreEqual(0.16f, feel.parryKickTime, 1e-4f);
            Assert.AreEqual(-2.2f, feel.parryFovPunch, 1e-4f);
            Assert.AreEqual(0.07f, feel.parryHitStopRelease, 1e-4f);
            Assert.AreEqual(0.45f, feel.parryHitStopReleaseScale, 1e-4f);
            Assert.IsTrue(feel.parryLayeredAudio);
            // Untouched by this work, asserted so a later edit cannot move them under it.
            Assert.AreEqual(0.09f, feel.parryHitStop, 1e-4f);
            Assert.AreEqual(0.02f, feel.hitStopScale, 1e-4f);
        }

        [Test]
        public void FovPunchIsInwardAndSmallerThanADash()
        {
            var feel = Shipped();
            Assert.Less(feel.parryFovPunch, 0f, "a deflect punches IN; widening reads as being shoved back");
            Assert.Less(Mathf.Abs(feel.parryFovPunch), feel.dashFovKick,
                "the deflect must not warp the lens harder than a dash");
        }
    }
}
