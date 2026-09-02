using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The dash's impact package, held to its budgets.
    ///
    /// <para><b>What these can and cannot prove.</b> They prove the SHAPES — that the streak fade starts
    /// full and reaches exactly zero, that the camera kick is honestly directional (no roll on a forward
    /// dash, no pitch on a strafe, and never any yaw), that the flow field always produces a usable unit
    /// vector, and that the shipped asset carries the shipped values rather than the code defaults
    /// (rule 9). They also prove the readability budget: the streaks sit under the bloom threshold, so a
    /// dash adds no glow to the frame at all.</para>
    ///
    /// <para>They cannot prove that a dash FEELS better. That judgement is a human at the controls.</para>
    /// </summary>
    public class DashImpulseTests
    {
        /// <summary>FirstPersonMotor.dashDuration. The kick must be over before the dash is.</summary>
        const float DashDuration = 0.16f;
        /// <summary>URP volume bloom threshold for this project. Nothing traversal does may cross it.</summary>
        const float BloomThreshold = 1.05f;

        static GameFeelSettings Shipped()
        {
            var feel = AssetDatabase.LoadAssetAtPath<GameFeelSettings>("Assets/Data/GameFeel.asset");
            Assert.IsNotNull(feel, "Assets/Data/GameFeel.asset is missing — run VibeGame1/3. Create Data.");
            return feel;
        }

        // ---------------------------------------------------------------- streak fade

        [Test]
        public void StreakAlphaIsFullOnFrameOneAndExactlyZeroAfter()
        {
            // A dash is a punctuation mark: anything that ramps in is wasted on 0.22 s. And a streak
            // that does not reach exactly zero is a line left permanently across the frame.
            Assert.AreEqual(1f, DashImpulse.StreakAlpha(0f), 1e-6f);
            Assert.AreEqual(0f, DashImpulse.StreakAlpha(1f), 1e-6f);
            Assert.AreEqual(0f, DashImpulse.StreakAlpha(1.4f), 1e-6f);
            Assert.AreEqual(0f, DashImpulse.StreakAlpha(-0.3f), 1e-6f);
        }

        [Test]
        public void StreakAlphaOnlyEverDecreases()
        {
            float prev = 2f;
            for (float t = 0f; t < 1f; t += 0.01f)
            {
                float v = DashImpulse.StreakAlpha(t);
                Assert.Less(v, prev, "streak alpha must be monotone decreasing at t=" + t);
                Assert.GreaterOrEqual(v, 0f);
                prev = v;
            }
        }

        // ---------------------------------------------------------------- camera kick

        [Test]
        public void AForwardDashHasNoRollAndAStrafeHasNoPitch()
        {
            // The point of the kick is that it is SPECIFIC. If a strafe pitched the view it would be
            // reporting a force that is not there, and the dash would go back to being a generic
            // "something happened to the camera" — which is exactly what was wrong with it.
            var fwd = DashImpulse.FromDash(Vector3.forward, 0.9f, 1.4f, 0.06f);
            Assert.AreEqual(0f, fwd.euler.z, 1e-5f, "a forward dash has no lateral component to bank into");
            Assert.AreEqual(-0.9f, fwd.euler.x, 1e-5f, "negative euler.x lifts the view");

            var right = DashImpulse.FromDash(Vector3.right, 0.9f, 1.4f, 0.06f);
            Assert.AreEqual(0f, right.euler.x, 1e-5f, "a pure strafe has no pitch in it");
            Assert.AreEqual(1.4f, right.euler.z, 1e-5f, "and banks its full roll");
        }

        [Test]
        public void TheKickNeverYaws()
        {
            // Load-bearing, not incidental. A dash is very often the approach to a swing; roll is free
            // because it does not move the aim vector, but a yaw kick would drag the reticle off the
            // thing you dashed at for the exact 0.14 s you are lining it up.
            for (float a = 0f; a < Mathf.PI * 2f; a += 0.21f)
            {
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Assert.AreEqual(0f, DashImpulse.FromDash(d, 0.9f, 1.4f, 0.06f).euler.y, 1e-6f);
            }
        }

        [Test]
        public void TheLensIsLeftBehindTheBodyByExactlyTheAuthoredDistance()
        {
            for (float a = 0f; a < Mathf.PI * 2f; a += 0.37f)
            {
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var k = DashImpulse.FromDash(d, 0.9f, 1.4f, 0.06f);
                Assert.AreEqual(0.06f, k.offset.magnitude, 1e-4f, "offset magnitude is the authored metres");
                Assert.Less(Vector3.Dot(k.offset.normalized, d), -0.999f,
                    "the lens must lag OPPOSITE the travel — that lag IS the acceleration read");
                Assert.AreEqual(0f, k.offset.y, 1e-6f, "a dash is horizontal; a vertical offset invents a force");
            }
        }

        [Test]
        public void ADegenerateDashDirectionStillProducesAFiniteKick()
        {
            // The motor falls back to flattened forward when there is no input, but a zero vector can
            // still reach here through a stagger-scaled wish. NaN in a camera transform is unrecoverable.
            var k = DashImpulse.FromDash(Vector3.zero, 0.9f, 1.4f, 0.06f);
            Assert.IsFalse(float.IsNaN(k.euler.x) || float.IsNaN(k.euler.z) || float.IsNaN(k.offset.x));
            Assert.AreEqual(0.06f, k.offset.magnitude, 1e-4f);
        }

        [Test]
        public void KickIsOverBeforeTheDashIs()
        {
            var feel = Shipped();
            Assert.Less(feel.dashKickTime, DashDuration,
                "the camera must be dead still again before the displacement finishes, or the settle " +
                "reads as the landing rather than as the dash");
            Assert.Less(DashImpulse.KickAttackFraction * feel.dashKickTime, 2f / 60f,
                "the attack must land inside two frames at 60 Hz or it reads as drift, not as a yank");
        }

        // ---------------------------------------------------------------- streak geometry

        [Test]
        public void StreakFlowIsAlwaysAUsableUnitVector()
        {
            var dirs = new[]
            {
                Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
                new Vector3(0.7f, 0f, 0.7f), new Vector3(-0.3f, 0f, 0.95f), Vector3.zero
            };
            foreach (var d in dirs)
                for (int i = 0; i < 12; i++)
                {
                    var f = DashImpulse.StreakFlow(DashImpulse.StreakAngle(i, 12, 0.5f), d);
                    Assert.AreEqual(1f, f.magnitude, 1e-4f, "flow must be unit length for dir " + d);
                }
        }

        [Test]
        public void AForwardDashStreamsRadiallyAndAStrafeStreamsAcrossTheFrame()
        {
            // This is the whole reason the geometry is rebuilt per dash instead of being a static
            // starburst: a strafe and a lunge do not look the same out of a window.
            for (int i = 0; i < 12; i++)
            {
                float a = DashImpulse.StreakAngle(i, 12, 0.5f);
                var radial = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

                var fwd = DashImpulse.StreakFlow(a, Vector3.forward);
                Assert.AreEqual(0f, (fwd - radial).magnitude, 1e-4f, "a forward dash flows straight out");

                var right = DashImpulse.StreakFlow(a, Vector3.right);
                Assert.Less(right.x, -0.99f, "dash right, the frame streams LEFT");
            }
        }

        [Test]
        public void StreakAnglesCoverTheWholeCircleAndAreNotAPerfectFan()
        {
            // An even fan reads as a UI decal. The jitter is what makes it read as air.
            const int N = 12;
            float even = DashImpulse.StreakAngle(3, N, 0.5f);
            Assert.AreEqual(3f * Mathf.PI * 2f / N, even, 1e-5f, "jitter 0.5 is the un-jittered position");
            Assert.Less(DashImpulse.StreakAngle(3, N, 0f), even);
            Assert.Greater(DashImpulse.StreakAngle(3, N, 1f), even);
            // and the jitter can never let one streak cross into its neighbour's slot
            float step = Mathf.PI * 2f / N;
            Assert.Less(Mathf.Abs(DashImpulse.StreakAngle(3, N, 1f) - even), step * 0.5f);
        }

        // ---------------------------------------------------------------- shipped values (rule 9)

        [Test]
        public void TheAssetCarriesTheShippedDashValues()
        {
            var feel = Shipped();
            Assert.AreEqual(0.9f, feel.dashKickPitch, 1e-4f);
            Assert.AreEqual(1.4f, feel.dashKickRoll, 1e-4f);
            Assert.AreEqual(0.06f, feel.dashKickOffset, 1e-5f);
            Assert.AreEqual(0.14f, feel.dashKickTime, 1e-4f);
            Assert.AreEqual(0.35f, feel.dashChromatic, 1e-4f);
            Assert.AreEqual(12, feel.dashStreakCount);
            Assert.AreEqual(0.22f, feel.dashStreakSeconds, 1e-4f);
            Assert.AreEqual(0.85f, feel.dashStreakAlpha, 1e-4f);
            Assert.AreEqual(0.90f, feel.dashStreakBrightness, 1e-4f);
            // Untouched by this work, asserted so a later edit cannot move it under the package.
            Assert.AreEqual(8f, feel.dashFovKick, 1e-4f);
        }

        [Test]
        public void ADashAddsNoBloomAtAll()
        {
            // The readability contract. EnemyVisuals.CueFlash owns the brightness budget and light on an
            // enemy means "you deflected"; traversal is not allowed to speak that language. The dash's
            // whole visual is UNDER the threshold, so it contributes literally zero to the bloom buffer.
            var feel = Shipped();
            Assert.Less(feel.dashStreakBrightness, BloomThreshold,
                "dash streaks must not bloom — legible beats loud, and this project has shipped a " +
                "0.55-alpha flash that shredded the frame");
            Assert.LessOrEqual(feel.dashStreakAlpha * feel.dashStreakBrightness, BloomThreshold);
        }

        [Test]
        public void TheDashPackageIsForceNotLight()
        {
            // Every lever the dash gained is motion or air. If someone later adds a brightness field and
            // wires it here, this test is the thing that should be re-argued rather than deleted.
            var feel = Shipped();
            Assert.Greater(feel.dashKickOffset, 0f, "the acceleration read must exist");
            Assert.Greater(feel.dashKickRoll, 0f);
            Assert.Greater(feel.dashStreakCount, 0, "zero streaks silently removes the whole picture");
        }
    }
}
