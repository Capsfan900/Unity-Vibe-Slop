using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Picks a swing/hit <see cref="Sfx"/> by weapon WEIGHT, not by weapon name — so a new weapon gets
    /// weight-appropriate audio automatically, by where its existing data already sits, never by adding
    /// a per-weapon switch (rule: content is data, not code).
    ///
    /// <para><see cref="WeaponData.hitStopSeconds"/> is the field that already encodes "how much force
    /// this weapon commits" — Dagger 0.03, Sword 0.06, Hammer 0.11 in the shipped roster — because a
    /// heavier weapon already earns a longer hitstop. Reusing it here means the sword sits in the middle
    /// band and keeps the original <see cref="Sfx.Swing"/> / <see cref="Sfx.Hit"/> as the mid-weight
    /// default, while the dagger and hammer get their own layered light/heavy variants
    /// (<c>ProceduralSfx.SwingLight/SwingHeavy/HitLight/HitHeavy</c>). 2026-09-06, weapon rework —
    /// weapon-audio pass.</para>
    /// </summary>
    public static class WeaponAudio
    {
        /// <summary>At or below this hitStopSeconds, a weapon is LIGHT (dagger territory).</summary>
        public const float LightMax = 0.045f;

        /// <summary>At or above this hitStopSeconds, a weapon is HEAVY (hammer territory).</summary>
        public const float HeavyMin = 0.08f;

        public static Sfx SwingSfx(WeaponData w)
        {
            if (w == null) return Sfx.Swing;
            if (w.hitStopSeconds <= LightMax) return Sfx.SwingLight;
            if (w.hitStopSeconds >= HeavyMin) return Sfx.SwingHeavy;
            return Sfx.Swing;
        }

        public static Sfx HitSfx(WeaponData w)
        {
            if (w == null) return Sfx.Hit;
            if (w.hitStopSeconds <= LightMax) return Sfx.HitLight;
            if (w.hitStopSeconds >= HeavyMin) return Sfx.HitHeavy;
            return Sfx.Hit;
        }
    }
}
