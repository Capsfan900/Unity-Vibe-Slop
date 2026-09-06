using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// 2026-09-06 VFX pass ("physics simulations and effects"): the death mist rose as a rigid column,
    /// decelerated only by a velocity clamp. This pins the damped turbulence added on top of that so the
    /// rise reads as curling smoke rather than a solid pushed shape, without changing how far or how fast
    /// the burst reads at a glance.
    /// </summary>
    public class DeathMistPhysicsTests
    {
        [Test]
        public void TheMistHasDampedTurbulence()
        {
            var noise = DeathMist.PeekNoiseModuleForTests();
            Assert.IsTrue(noise.enabled, "the rise must not be laminar -- some turbulence has to break up the column");
            Assert.IsTrue(noise.damping, "an undamped noise field on a 0.55-1.15 s burst either never settles or pops in visibly");
            Assert.AreEqual(DeathMist.MistNoiseStrength, noise.strength.constant, 1e-4f);
            Assert.Greater(DeathMist.MistNoiseStrength, 0f);
            // Strong enough to read, not so strong it overwhelms the burst's own outward punch (limit
            // velocity over lifetime clamps that to 0.7 m/s).
            Assert.Less(DeathMist.MistNoiseStrength, 0.5f, "turbulence must stay a texture on the rise, not replace it");
            Assert.Greater(DeathMist.MistNoiseFrequency, 0f);
            Assert.AreEqual(ParticleSystemNoiseQuality.Low, noise.quality, "1D noise is plenty at this particle count; higher quality is wasted cost");
        }
    }
}
