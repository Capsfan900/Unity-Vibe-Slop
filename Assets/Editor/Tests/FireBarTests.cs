using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The PYRE meter's fire. The maths is pure (<see cref="FireBarMath"/>) so the invariants that
    /// matter — bounded heat, a kick that dies, a ramp that never crosses the 1.05 bloom cap, flames
    /// that never leave the bar's rect — are asserted without a scene. The prefab test reads the
    /// SHIPPED HUD and Ignores until <c>5. Build HUD</c> has run.
    /// </summary>
    public class FireBarTests
    {
        const float BloomCap = 1.05f;
        static readonly Color Ember = new Color(0.55f, 0.10f, 0.02f, 1f);
        static readonly Color Flame = new Color(1.00f, 0.45f, 0.08f, 1f);
        static readonly Color Core = new Color(1.00f, 0.92f, 0.70f, 1f);

        [Test]
        public void Heat_RisesWithTheCharge_AndIsBounded()
        {
            float last = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float h = FireBarMath.Heat(i / 20f, 0.8f, 0f);
                Assert.GreaterOrEqual(h, last, "heat fell as the meter rose at " + i / 20f);
                Assert.That(h, Is.InRange(0f, 1f));
                last = h;
            }
            Assert.AreEqual(0f, FireBarMath.Heat(0f, 0.8f, 0f), 1e-5f, "an empty meter must be cold.");
            Assert.AreEqual(1f, FireBarMath.Heat(1f, 0.8f, 0f), 1e-5f, "a full meter is full heat.");
            Assert.LessOrEqual(FireBarMath.Heat(1f, 0.8f, 0.5f), 1f, "a pulse on a full meter must not exceed 1.");
            Assert.LessOrEqual(FireBarMath.Heat(2f, 0.8f, 0f), 1f, "an over-full input is clamped.");
        }

        [Test]
        public void Kick_DecaysToExactlyZero_AndNeverGoesNegative()
        {
            float k = 1f;
            float t = 0f;
            while (k > 0f && t < 5f) { k = FireBarMath.DecayKick(k, 4f, 1f / 60f); t += 1f / 60f; }
            Assert.AreEqual(0f, k, "the kick never snapped to zero.");
            Assert.Less(t, 2.5f, "a 4/s kick took " + t + " s to die; it should be gone inside a second or two.");
            Assert.AreEqual(0f, FireBarMath.DecayKick(-0.3f, 4f, 0.016f), "a negative kick must clamp to zero.");
            Assert.AreEqual(0.5f, FireBarMath.DecayKick(0.5f, 4f, 0f), 1e-6f, "a zero step must not change the kick.");
        }

        [Test]
        public void TheRamp_NeverCrossesTheBloomCap()
        {
            for (int i = 0; i <= 100; i++)
            {
                Color c = FireBarMath.Ramp(i / 100f, Ember, Flame, Core);
                Assert.LessOrEqual(Mathf.Max(c.r, Mathf.Max(c.g, c.b)), 1f,
                    "ramp sample " + i / 100f + " = " + c + " is over 1.0; the HUD must never bloom (cap " + BloomCap + ").");
            }
            // And it is a ramp: the hot end is brighter than the cold end.
            Color cold = FireBarMath.Ramp(0f, Ember, Flame, Core), hot = FireBarMath.Ramp(1f, Ember, Flame, Core);
            Assert.Greater(hot.r + hot.g + hot.b, cold.r + cold.g + cold.b, "the ramp does not get hotter.");
        }

        [Test]
        public void TheFullPulse_IsAPulseNotABloom()
        {
            for (int i = 0; i < 60; i++)
            {
                float p = FireBarMath.FullPulse(i / 60f, 1.4f, 0.15f);
                Assert.That(p, Is.InRange(0f, 0.15f));
            }
            Assert.LessOrEqual(FireBarMath.FullPulse(0.3f, 1.4f, 5f), 0.5f, "the pulse amount is capped at 0.5.");
        }

        [Test]
        public void Flames_NeverLeaveTheBar()
        {
            // reach is the fraction of the headroom a tongue may use; 1 is the top of the quad.
            for (int h = 0; h <= 10; h++)
                for (int n = 0; n <= 10; n++)
                {
                    float r = FireBarMath.Reach(h / 10f, n / 10f, 0.5f, false);
                    Assert.That(r, Is.InRange(0f, 1f), "reach out of the rect at heat " + h / 10f + " noise " + n / 10f);
                    Assert.That(FireBarMath.Reach(h / 10f, n / 10f, 1f, true), Is.InRange(0f, 1f));
                }
            // A cold fire barely licks; a full one roars.
            Assert.Less(FireBarMath.Reach(0f, 0.8f, 0.5f, false), FireBarMath.Reach(1f, 0.8f, 0.5f, false));
            Assert.GreaterOrEqual(FireBarMath.Reach(1f, 0.5f, 0.5f, true), 0.55f, "the full band should stand well above the bar.");
        }

        [Test]
        public void ThePrefab_CarriesTheFireOnThePyreBar()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HUD.prefab");
            Assert.IsNotNull(hud, "HUD.prefab missing — run VibeGame1/5. Build HUD");
            FireBarView fire = null;
            foreach (var f in hud.GetComponentsInChildren<FireBarView>(true))
                if (f.name == "PyreBar") fire = f;
            if (fire == null)
                Assert.Ignore("PyreBar has no FireBarView yet — run VibeGame1/5. Build HUD (HudExtensions.ApplyPyreFire).");

            Assert.IsNotNull(fire.GetComponent<BarView>(), "the fire rides a BarView; the extent is still anchor-driven.");
            Assert.IsNotNull(fire.flames, "no Flames image bound.");
            Assert.IsNull(fire.flames.sprite, "the fire is procedural; a sprite would be sampled as _MainTex and tint it.");
            Assert.IsFalse(fire.flames.raycastTarget);
            var mat = fire.flames.material;
            Assert.IsNotNull(mat, "the Flames image has no material.");
            Assert.AreEqual("VibeGame1/UI/FireBar", mat.shader.name);
            foreach (var key in new[] { "_Ember", "_Flame", "_Core" })
            {
                Color c = mat.GetColor(key);
                Assert.LessOrEqual(Mathf.Max(c.r, Mathf.Max(c.g, c.b)), 1f, key + " on M_PyreFire is over 1.0 — the HUD must not bloom.");
            }
            float barTop = mat.GetFloat("_BarTop");
            Assert.That(barTop, Is.InRange(0.4f, 0.75f), "_BarTop " + barTop + ": the flame headroom is not a small, bounded overshoot.");

            // The quad is taller than the bar by the headroom, and the fill under it is a dark ember.
            var flamesRt = fire.flames.rectTransform;
            Assert.Greater(flamesRt.offsetMax.y, 4f, "the Flames quad has no headroom above the bar.");
            var bar = fire.GetComponent<BarView>();
            Assert.Less(bar.fillColor.maxColorComponent, 0.5f, "the base fill under the fire should be a dark ember, not a bright bar.");
            Assert.IsFalse(bar.pulseWhenFull, "the BarView's white pulse would fight the fire's roaring band.");
            Assert.Greater(fire.kickDecay, 0f); Assert.That(fire.fullPulseAmount, Is.InRange(0f, 0.5f));
        }
    }
}
