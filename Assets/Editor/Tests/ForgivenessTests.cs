using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The forgiveness laws (MOVEMENT-PRINCIPLES rule 4), pure. Corner correction may only act inside
    /// its margin and only while rising; a near-miss catch only on a floor, only while falling, only
    /// with speed toward it; the lift is the same width of time at any frame rate.
    /// </summary>
    public class ForgivenessTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void CornerCorrection_OnlyInsideTheMargin_AndOnlyWhileRising()
        {
            Assert.IsTrue(ForgivenessMath.CornerCorrects(0.18f, 0.18f, 5f), "a clear capsule at the margin corrects");
            Assert.IsTrue(ForgivenessMath.CornerCorrects(0.05f, 0.18f, 5f));
            Assert.IsFalse(ForgivenessMath.CornerCorrects(0f, 0.18f, 5f), "no clear offset found = a ceiling, not a corner");
            Assert.IsFalse(ForgivenessMath.CornerCorrects(0.30f, 0.18f, 5f), "beyond the margin would add reach");
            Assert.IsFalse(ForgivenessMath.CornerCorrects(0.10f, 0.18f, -2f), "never while falling");
            Assert.IsFalse(ForgivenessMath.CornerCorrects(0.10f, 0f, 5f), "disabled by a zero margin");
        }

        [Test]
        public void LedgeCatch_OnlyAFloorJustAboveTheFeet_WhileFallingWithSpeed()
        {
            Assert.IsTrue(ForgivenessMath.LedgeCatches(1.0f, 1.15f, 0.22f, -3f, 1f, 0.7f, 6f, 1.5f), "0.15 m under the top, falling, moving: catch");
            Assert.IsTrue(ForgivenessMath.LedgeCatches(1.0f, 1.0f, 0.22f, -3f, 1f, 0.7f, 6f, 1.5f), "exactly at the top still catches");
            Assert.IsFalse(ForgivenessMath.LedgeCatches(1.0f, 1.5f, 0.22f, -3f, 1f, 0.7f, 6f, 1.5f), "0.5 m under is a miss, not a near miss");
            Assert.IsFalse(ForgivenessMath.LedgeCatches(1.0f, 0.9f, 0.22f, -3f, 1f, 0.7f, 6f, 1.5f), "a top below the feet is an ordinary landing");
            Assert.IsFalse(ForgivenessMath.LedgeCatches(1.0f, 1.15f, 0.22f, 2f, 1f, 0.7f, 6f, 1.5f), "never while rising");
            Assert.IsFalse(ForgivenessMath.LedgeCatches(1.0f, 1.15f, 0.22f, -3f, 0.05f, 0.7f, 6f, 1.5f), "a wall side (normal.y ~0) never catches");
            Assert.IsFalse(ForgivenessMath.LedgeCatches(1.0f, 1.15f, 0.22f, -3f, 1f, 0.7f, 0.5f, 1.5f), "a standing drop is a drop");
        }

        [Test]
        public void TheLiftIsTheSameAtAnyFrameRate()
        {
            foreach (float fps in new[] { 20f, 60f, 240f })
            {
                float dt = 1f / fps, remaining = 0.20f, t = 0f;
                while (remaining > 1e-5f && t < 2f)
                {
                    float step = ForgivenessMath.NudgeStep(remaining, 6f, dt);
                    Assert.LessOrEqual(step, 6f * dt + Eps, "a frame may not lift more than the rate allows");
                    remaining -= step; t += dt;
                }
                Assert.AreEqual(0.20f / 6f, t, 1f / fps + Eps, "0.20 m at 6 m/s takes ~0.033 s at " + fps + " fps");
            }
            Assert.AreEqual(0f, ForgivenessMath.NudgeStep(0f, 6f, 0.016f), Eps);
            Assert.AreEqual(0f, ForgivenessMath.NudgeStep(-0.1f, 6f, 0.016f), Eps, "never a downward nudge");
        }

        [Test]
        public void TheShippedValuesAreOnThePrefab()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (p == null) Assert.Ignore("Player.prefab not built");
            var yaml = System.IO.File.ReadAllText("Assets/Prefabs/Player.prefab");
            if (!yaml.Contains("cornerCorrectionMetres:")) Assert.Ignore("run VibeGame1/4. Build Prefabs first (rule 9)");
            var m = p.GetComponent<FirstPersonMotor>();
            Assert.That(m.cornerCorrectionMetres, Is.InRange(0.05f, 0.3f), "corner margin must be a few centimetres, never reach");
            Assert.That(m.ledgeCatchMetres, Is.InRange(0.05f, 0.35f));
            Assert.Greater(m.ledgeCatchLiftSpeed, 0f);
            Assert.Greater(m.ledgeCatchMinSpeed, 0f);
        }
    }
}
