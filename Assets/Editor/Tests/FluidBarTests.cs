using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The fluid health / stamina bars: the slosh maths (pure), and the shipped HUD prefab's wiring.
    ///
    /// <para>What cannot be tested here, said plainly: whether the shader compiles under URP and what it looks
    /// like. A shader that fails to compile renders MAGENTA and nothing in EditMode sees it — the prefab checks
    /// below only prove the component, the shader reference and the numbers are on the asset. A human look in
    /// the editor after <c>5. Build HUD</c> is the verification.</para>
    /// </summary>
    public class FluidBarTests
    {
        const string HudPath = "Assets/Prefabs/HUD.prefab";

        static GameObject Hud() => AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        static FluidBarView Fluid(string barName)
        {
            var hud = Hud();
            if (hud == null) return null;
            foreach (var f in hud.GetComponentsInChildren<FluidBarView>(true))
                if (f.name == barName) return f;
            return null;
        }

        // ------------------------------------------------------------------ the maths

        [Test]
        public void ZeroAcceleration_IsZeroTilt()
        {
            Assert.AreEqual(0f, FluidSlosh.TiltTarget(0f, 0.012f, 0.15f));
            float tilt = 0f, vel = 0f;
            for (int i = 0; i < 300; i++) FluidSlosh.Step(ref tilt, ref vel, 0f, 2.5f, 0.35f, 0.15f, 1f / 60f);
            Assert.AreEqual(0f, tilt, 1e-6f, "a still player must leave the surface level.");
        }

        [Test]
        public void TheTargetIsClampedHard_AndTheStateNeverExceedsTheCap()
        {
            // A dash landing is ~100 m/s^2 for a frame; at 0.012 gain that would be 1.2 of the bar
            // height — the cap holds it to 0.15, on the target and on the state.
            Assert.AreEqual(-0.15f, FluidSlosh.TiltTarget(100f, 0.012f, 0.15f), 1e-6f);
            Assert.AreEqual(0.15f, FluidSlosh.TiltTarget(-100f, 0.012f, 0.15f), 1e-6f);
            float tilt = 0f, vel = 0f;
            for (int i = 0; i < 120; i++)
            {
                FluidSlosh.Step(ref tilt, ref vel, i < 10 ? -0.15f : 0f, 2.5f, 0.35f, 0.15f, 1f / 60f);
                Assert.LessOrEqual(Mathf.Abs(tilt), 0.15f + 1e-6f, "frame " + i + ": tilt " + tilt + " over the cap.");
            }
        }

        [Test]
        public void SignConvention_LiquidPilesUpOppositeTheAcceleration()
        {
            // Accelerate right → the surface is higher on the LEFT → negative tilt in the shader's
            // (x - 0.5) convention. This is the one number a player would notice if it were backwards.
            Assert.Less(FluidSlosh.TiltTarget(10f, 0.012f, 0.15f), 0f);
            Assert.Greater(FluidSlosh.TiltTarget(-10f, 0.012f, 0.15f), 0f);
        }

        [Test]
        public void TheTiltSettlesInsideHalfASecond_AtAnyFrameRate()
        {
            // "Just enough": a lean under acceleration, then a ring-down that is over before the next
            // move. Driven for 0.2 s at the cap, then released; must be under 10% of the cap by 0.5 s
            // after release, at 20, 60 and 240 fps, and land on the same picture at all three.
            float[] rates = { 20f, 60f, 240f };
            float[] atHalf = new float[rates.Length];
            for (int r = 0; r < rates.Length; r++)
            {
                float dt = 1f / rates[r];
                float tilt = 0f, vel = 0f, t = 0f;
                while (t < 0.2f) { FluidSlosh.Step(ref tilt, ref vel, -0.15f, 2.5f, 0.35f, 0.15f, dt); t += dt; }
                float peak = Mathf.Abs(tilt);
                Assert.Greater(peak, 0.05f, rates[r] + " fps: the surface barely leaned (" + peak + ").");
                t = 0f;
                while (t < 0.5f) { FluidSlosh.Step(ref tilt, ref vel, 0f, 2.5f, 0.35f, 0.15f, dt); t += dt; }
                atHalf[r] = tilt;
                Assert.Less(Mathf.Abs(tilt), 0.015f, rates[r] + " fps: still " + tilt + " 0.5 s after release.");
            }
            Assert.AreEqual(atHalf[1], atHalf[0], 0.01f, "20 fps rings differently from 60.");
            Assert.AreEqual(atHalf[1], atHalf[2], 0.01f, "240 fps rings differently from 60.");
        }

        [Test]
        public void ALandingDipsTheLevel_ButAKerbDoesNot()
        {
            Assert.AreEqual(0f, FluidSlosh.LandingDip(1f, 22f, 0.10f), "stepping off a kerb should not slosh.");
            Assert.AreEqual(0.10f, FluidSlosh.LandingDip(22f, 22f, 0.10f), 1e-6f);
            Assert.AreEqual(0.10f, FluidSlosh.LandingDip(60f, 22f, 0.10f), 1e-6f, "the dip must cap at the full-speed value.");
            Assert.Greater(FluidSlosh.LandingDip(11f, 22f, 0.10f), 0.04f);
        }

        // ------------------------------------------------------------------ the shipped prefab

        [Test]
        public void TheHudCarriesFluidHealthAndStaminaBars()
        {
            if (Hud() == null) Assert.Ignore("HUD.prefab missing — run VibeGame1/5. Build HUD.");
            var health = Fluid("HealthBar");
            var stamina = Fluid("StaminaBar");
            if (health == null && stamina == null)
                Assert.Ignore("No FluidBarView on the HUD yet — run VibeGame1/5. Build HUD (HudExtensions.ApplyFluidBars).");

            foreach (var f in new[] { health, stamina })
            {
                Assert.IsNotNull(f, "one of the two bars lost its fluid pass.");
                Assert.IsNotNull(f.shader, f.name + ": no shader reference; the bar would be a flat fill.");
                Assert.AreEqual("VibeGame1/UI/FluidBar", f.shader.name, f.name + " carries the wrong shader.");
                Assert.LessOrEqual(f.glow, 1f, f.name + ": glow over 1.0 — the meniscus must never bloom.");
                Assert.Greater(f.aspect, 5f, f.name + ": aspect " + f.aspect + " is not a bar's.");
                Assert.LessOrEqual(f.maxTilt, 0.25f, f.name + ": the tilt cap is not 'just enough' any more.");
                Assert.Greater(f.tiltGain, 0f, f.name + ": no jostle at all.");

                var bar = f.GetComponent<BarView>();
                Assert.IsNotNull(bar);
                Assert.IsNotNull(bar.fill);
                Assert.IsNull(bar.fill.sprite, f.name + ": the fill still has a sprite; the shader's UV contract needs a null-sprite Simple image.");
                Assert.AreEqual(Image.Type.Simple, bar.fill.type, f.name + ": the fill is not Simple.");
            }
            // The stamina bar keeps its ticks (drawn over the liquid), and the health bar its ghost.
            Assert.IsNotNull(Fluid("HealthBar").GetComponent<BarView>().ghost, "the health bar lost its ghost.");
        }
    }
}
