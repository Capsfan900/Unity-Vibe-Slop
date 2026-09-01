using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The Pyre discharge, proved without a scene.
    ///
    /// <para>Everything asserted here is PURE geometry or a shipped constant. The two claims that
    /// actually matter visually — "the bolts start exactly at the weapon and end exactly inside the
    /// enemy" and "the mist reaches the enemy before it fades" — are both arithmetic, so they can be
    /// proved rather than eyeballed. A bundle whose channels merely pass NEAR both ends reads as
    /// unrelated sparks, and that failure is invisible in a screenshot at 60 fps.</para>
    /// </summary>
    public class PyreArcTests
    {
        static readonly Vector3 From = new Vector3(1.2f, 1.55f, -0.4f);
        static readonly Vector3 To = new Vector3(4.9f, 1.10f, 5.3f);

        static Vector3[] Strand(int strand, float spread, float jitter, System.Random rng, int points = LightningEffect.BundlePoints)
        {
            var buf = new Vector3[points];
            LightningEffect.FillStrand(From, To, strand, LightningEffect.BundleStrands, spread, jitter, 0.37f, rng, buf);
            return buf;
        }

        /// <summary>Perpendicular distance from a point to the From→To line.</summary>
        static float Deviation(Vector3 p)
        {
            Vector3 axis = To - From;
            Vector3 d = axis.normalized;
            Vector3 v = p - From;
            return Vector3.ProjectOnPlane(v, d).magnitude;
        }

        // ---- anchoring: the whole effect rests on this -----------------------------------------

        [Test]
        public void EveryStrandStartsExactlyAtTheWeapon()
        {
            for (int s = 0; s < LightningEffect.BundleStrands; s++)
                Assert.AreEqual(From, Strand(s, 0.4f, 0.2f, new System.Random(s))[0], "strand " + s);
        }

        [Test]
        public void EveryStrandEndsExactlyInTheTarget()
        {
            for (int s = 0; s < LightningEffect.BundleStrands; s++)
            {
                var pts = Strand(s, 0.4f, 0.2f, new System.Random(s));
                Assert.AreEqual(To, pts[pts.Length - 1], "strand " + s);
            }
        }

        [Test]
        public void StrandFillsTheWholeBuffer()
        {
            var pts = Strand(2, 0.3f, 0.1f, null, 9);
            Assert.AreEqual(9, pts.Length);
            for (int i = 0; i < pts.Length; i++)
                Assert.IsFalse(float.IsNaN(pts[i].x) || float.IsNaN(pts[i].y) || float.IsNaN(pts[i].z));
        }

        [Test]
        public void TooSmallABufferIsIgnoredRatherThanThrowing()
        {
            var one = new Vector3[1];
            Assert.DoesNotThrow(() => LightningEffect.FillStrand(From, To, 0, 5, 0.3f, 0.1f, 0f, null, one));
            Assert.DoesNotThrow(() => LightningEffect.FillStrand(From, To, 0, 5, 0.3f, 0.1f, 0f, null, null));
        }

        [Test]
        public void ZeroLengthSpanDoesNotProduceNaN()
        {
            var buf = new Vector3[LightningEffect.BundlePoints];
            LightningEffect.FillStrand(From, From, 1, 5, 0.3f, 0.1f, 0f, null, buf);
            foreach (var p in buf)
                Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z));
        }

        // ---- shape ------------------------------------------------------------------------------

        [Test]
        public void SpineIsDeadStraightWithNoSpreadAndNoJitter()
        {
            foreach (var p in Strand(0, 0f, 0f, null))
                Assert.Less(Deviation(p), 1e-4f);
        }

        [Test]
        public void BraidStrandBowsOutByExactlyTheSpreadAtMidSpan()
        {
            // 14 points: index 6 and 7 straddle the middle. sin(k*pi) there is ~0.993.
            var pts = Strand(1, 0.5f, 0f, null);
            float mid = Deviation(pts[6]);
            Assert.Greater(mid, 0.5f * 0.95f);
            Assert.LessOrEqual(mid, 0.5f + 1e-4f);
        }

        [Test]
        public void NoPointEverLeavesTheBraidRadius()
        {
            // Pure braid: the lateral offset is spread * sin(k*pi), so spread is a hard ceiling.
            foreach (var p in Strand(3, 0.42f, 0f, null))
                Assert.LessOrEqual(Deviation(p), 0.42f + 1e-4f);
        }

        [Test]
        public void JitterStaysInsideItsOwnBudget()
        {
            // Worst case is the braid bow plus both jitter axes at full swing.
            const float spread = 0.4f, jitter = 0.25f;
            float ceiling = spread + jitter * Mathf.Sqrt(2f) + 1e-3f;
            var rng = new System.Random(9001);
            for (int s = 0; s < LightningEffect.BundleStrands; s++)
                foreach (var p in Strand(s, s == 0 ? 0f : spread, jitter, rng))
                    Assert.LessOrEqual(Deviation(p), ceiling);
        }

        [Test]
        public void TheChannelTapersToNothingAtBothEnds()
        {
            var pts = Strand(2, 0.5f, 0.25f, new System.Random(7));
            // Second and second-to-last points sit at sin(pi/13) ~ 0.24 of full deviation.
            Assert.Less(Deviation(pts[1]), 0.5f * 0.45f);
            Assert.Less(Deviation(pts[pts.Length - 2]), 0.5f * 0.45f);
        }

        [Test]
        public void ProgressAlongTheSpanIsStrictlyMonotone()
        {
            // Every offset is perpendicular, so a channel never doubles back on itself — which is what
            // stops a jagged bolt reading as a scribble.
            var pts = Strand(4, 0.45f, 0.3f, new System.Random(1234));
            Vector3 d = (To - From).normalized;
            float prev = float.NegativeInfinity;
            foreach (var p in pts)
            {
                float along = Vector3.Dot(p - From, d);
                Assert.Greater(along, prev);
                prev = along;
            }
        }

        [Test]
        public void OppositeStrandsSitOnOppositeSidesOfTheSpine()
        {
            // Strands 1 and 3 of 5 are half a turn apart, so at the same point they are ~2*spread
            // apart. This is the difference between a rope and five copies of the same line.
            const float spread = 0.5f;
            var a = Strand(1, spread, 0f, null);
            var b = Strand(3, spread, 0f, null);
            Assert.Greater(Vector3.Distance(a[7], b[7]), spread * 1.8f);
        }

        // ---- shipped feel constants (rule 9: a value nobody asserts is a value that drifts) -------

        [Test] public void BundleHasFiveStrands() => Assert.AreEqual(5, LightningEffect.BundleStrands);
        [Test] public void BundleHasFourteenPointsPerStrand() => Assert.AreEqual(14, LightningEffect.BundlePoints);
        [Test] public void BundleLivesForABitOverAThirdOfASecond() => Assert.AreEqual(0.34f, LightningEffect.BundleSeconds, 1e-5f);
        [Test] public void BundleCracklesEightTimes() => Assert.AreEqual(8, LightningEffect.BundleCrackles);

        [Test]
        public void TheFringeSpendsTheHdrIntensityAndKeepsTheHue()
        {
            // A wand ships its colour at ~2.6x. Every additive channel over 1 clips to white, so a
            // bundle drawn entirely at full intensity is five white wires and the wand's colour is
            // nowhere on screen — which is exactly what the first capture of this effect showed.
            var wand = new Color(0.50f, 0.83f, 1.00f) * 2.6f;
            var fringe = LightningEffect.FringeOf(wand);

            Assert.Less(fringe.maxColorComponent, 1.0f, "fringe must not clip");
            Assert.Greater(fringe.maxColorComponent, 0.5f, "fringe must still be bright enough to see");
            // Hue preserved: the channel ratios are untouched.
            Assert.AreEqual(wand.r / wand.b, fringe.r / fringe.b, 1e-4f);
            Assert.AreEqual(wand.g / wand.b, fringe.g / fringe.b, 1e-4f);
        }

        [Test]
        public void FringeKeepsAlphaAndHandlesBlack()
        {
            var c = LightningEffect.FringeOf(new Color(0f, 0f, 0f, 0.5f));
            Assert.AreEqual(0.5f, c.a, 1e-5f);
            Assert.AreEqual(0f, c.maxColorComponent, 1e-5f);
        }

        [Test]
        public void TheCrackleEndsBeforeTheBundleDoes()
        {
            // Otherwise the effect is cut off at full brightness instead of fading, which pops.
            float hold = LightningEffect.BundleCrackles * LightningEffect.BundleCrackleInterval;
            Assert.Less(hold, LightningEffect.BundleSeconds);
            Assert.Greater(LightningEffect.BundleSeconds - hold, 0.05f);
        }

        [Test]
        public void SpreadAndJitterScaleWithSpanButAreClamped()
        {
            Assert.AreEqual(0.06f, LightningEffect.BundleSpread(0.1f), 1e-5f);    // floor
            Assert.AreEqual(0.55f, LightningEffect.BundleSpread(50f), 1e-5f);     // ceiling
            Assert.AreEqual(0.075f * 4f, LightningEffect.BundleSpread(4f), 1e-5f);

            Assert.AreEqual(0.035f, LightningEffect.BundleJitter(0.1f), 1e-5f);
            Assert.AreEqual(0.30f, LightningEffect.BundleJitter(50f), 1e-5f);
            Assert.AreEqual(0.045f * 4f, LightningEffect.BundleJitter(4f), 1e-5f);
        }

        [Test]
        public void TheBundleIsNeverWiderThanItIsLong()
        {
            // A 1 m discharge with a 0.55 m braid radius would be a ball, not an arrow.
            for (float d = 0.5f; d < 30f; d += 0.5f)
                Assert.Less(LightningEffect.BundleSpread(d) * 2f, d, "span " + d);
        }

        // ---- the misty half ---------------------------------------------------------------------

        [Test]
        public void MistSpeedIsSolvedSoAMoteExactlyCrossesTheGap()
        {
            foreach (float d in new[] { 0.5f, 2f, 7.5f, 14f })
                Assert.AreEqual(d, PyreMist.SpeedFor(d) * PyreMist.Flight, 1e-4f, "span " + d);
        }

        [Test]
        public void EvenTheSlowestMoteArrivesInTheEnemy()
        {
            // The speed spread is what makes the flow arrive over a window rather than as a wall. It
            // must not be so wide that the slow tail dies in mid-air, which reads as the effect
            // stopping short of the target.
            const float d = 8f;
            float slowest = PyreMist.SpeedFor(d) * PyreMist.SpeedMin * PyreMist.Flight;
            Assert.Greater(slowest / d, 0.8f);
            float fastest = PyreMist.SpeedFor(d) * PyreMist.SpeedMax * PyreMist.Flight;
            Assert.Less(fastest / d, 1.25f);
        }

        [Test]
        public void MistNegativeOrZeroSpanIsHarmless()
        {
            Assert.AreEqual(0f, PyreMist.SpeedFor(0f));
            Assert.AreEqual(0f, PyreMist.SpeedFor(-3f));
        }

        [Test]
        public void TheMistLandsWithTheBoltsRatherThanAfterThem()
        {
            Assert.LessOrEqual(PyreMist.Flight, LightningEffect.BundleSeconds + 1e-5f);
            Assert.Greater(PyreMist.Flight, LightningEffect.BundleSeconds * 0.6f);
        }

        [Test] public void MistBurstIsThirtyFourMotes() => Assert.AreEqual(34, PyreMist.BurstCount);
        [Test] public void MistFlightIsThreeTenths() => Assert.AreEqual(0.30f, PyreMist.Flight, 1e-5f);
        [Test] public void MistConeIsNarrow() => Assert.AreEqual(7f, PyreMist.ConeAngle, 1e-5f);

        // ---- the entry point --------------------------------------------------------------------

        [Test]
        public void ADegenerateSpanCastsNothingAndDoesNotThrow()
        {
            // Called every riposte and every super hit; a zero-length span (weapon tip already inside
            // the victim) must be a no-op, not a NaN LineRenderer or a divide by zero.
            Assert.DoesNotThrow(() => PyreArc.Cast(From, From, Color.white, 1f));
            Assert.DoesNotThrow(() => PyreArc.Cast(From, From + Vector3.up * (PyreArc.MinSpan * 0.5f), Color.white, 1f));
        }

        [Test] public void MinSpanIsAboutAHandsWidth() => Assert.AreEqual(0.35f, PyreArc.MinSpan, 1e-5f);
    }
}
