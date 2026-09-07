using NUnit.Framework;
using UnityEngine;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Pins the weapon-weight audio selection added in the 2026-09-06 weapon rework (weapon-audio pass):
    /// <see cref="WeaponAudio"/> reads the roster's existing <see cref="WeaponData.hitStopSeconds"/> field
    /// to choose a light/mid/heavy swing+hit pair, so a dagger and a hammer never share a "connected"
    /// sound. This cannot prove the mix sounds right (that needs ears — see the audit report), only that
    /// the selection is deterministic, in-range, and matches the shipped roster's actual data.
    /// </summary>
    public class WeaponAudioTests
    {
        static WeaponData Make(float hitStopSeconds)
        {
            var w = ScriptableObject.CreateInstance<WeaponData>();
            w.hitStopSeconds = hitStopSeconds;
            return w;
        }

        [Test]
        public void NullWeaponFallsBackToTheMidWeightDefault()
        {
            Assert.AreEqual(Sfx.Swing, WeaponAudio.SwingSfx(null));
            Assert.AreEqual(Sfx.Hit, WeaponAudio.HitSfx(null));
        }

        [Test]
        public void ALightWeaponGetsTheLightPair()
        {
            var w = Make(0.03f); // shipped Dagger value
            Assert.AreEqual(Sfx.SwingLight, WeaponAudio.SwingSfx(w));
            Assert.AreEqual(Sfx.HitLight, WeaponAudio.HitSfx(w));
        }

        [Test]
        public void AMidWeightWeaponKeepsTheOriginalPair()
        {
            var w = Make(0.06f); // shipped Sword value
            Assert.AreEqual(Sfx.Swing, WeaponAudio.SwingSfx(w));
            Assert.AreEqual(Sfx.Hit, WeaponAudio.HitSfx(w));
        }

        [Test]
        public void AHeavyWeaponGetsTheHeavyPair()
        {
            var w = Make(0.11f); // shipped Hammer value
            Assert.AreEqual(Sfx.SwingHeavy, WeaponAudio.SwingSfx(w));
            Assert.AreEqual(Sfx.HitHeavy, WeaponAudio.HitSfx(w));
        }

        [Test]
        public void TheBandsDoNotOverlap()
        {
            // If a future retune ever moved HeavyMin below or onto LightMax, every weapon between them
            // would become ambiguous depending on evaluation order. Keep them strictly separated.
            Assert.Less(WeaponAudio.LightMax, WeaponAudio.HeavyMin);
        }

        [Test]
        public void EveryNewSfxValueRoundTripsThroughSynthesisAndTheMixTable()
        {
            // Belt-and-braces alongside AudioTests' exhaustive sweep (which already iterates every enum
            // value): name the four new members explicitly so a future rename of one is caught here too.
            Sfx[] added = { Sfx.SwingLight, Sfx.SwingHeavy, Sfx.HitLight, Sfx.HitHeavy };
            foreach (var s in added)
            {
                Assert.DoesNotThrow(() => ProceduralSfx.Build(s));
                Assert.IsTrue(AudioManager.HasExplicitTrim(s), "Sfx." + s + " missing from the mix table");
            }
        }
    }
}
