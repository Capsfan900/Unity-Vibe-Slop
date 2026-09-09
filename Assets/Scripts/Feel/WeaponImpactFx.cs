using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// THE MELEE HIT CONFIRM — the most repeated payoff in the game, and until this pass the only
    /// impact in the project that did not go through <see cref="SlashFx"/>.
    ///
    /// <para><b>What it replaced, and why that had to go.</b> <c>WeaponController.SpawnSpark</c> built a
    /// fresh <c>GameObject.CreatePrimitive(Sphere)</c> per hit — a collider created and destroyed, a new
    /// <c>MaterialPropertyBlock</c> and a new component, none of it pooled — and drew a round blob that
    /// GREW from 0.35 m to 0.85 m while it faded, at <c>neon * 4</c>. That last number is the headline:
    /// the hammer's hue peaks at 0.878, so an ordinary chip hit rendered at <b>3.51</b> — brighter than
    /// the unblockable alert tell (3.00), brighter than the deathblow mark (2.60), and brighter than the
    /// deflect flash (3.2) that is supposed to be the loudest moment in the game. A routine hit was
    /// out-shouting every alarm on screen, so the loudness hierarchy carried no information.
    /// It also broke all three of <see cref="SlashFx"/>'s own stated rules in one object: it read through
    /// BRIGHTNESS rather than shape, it EXPANDED as a blob, and it carried no DIRECTION.</para>
    ///
    /// <para><b>What it is now.</b> Three pooled <see cref="SlashFx"/> primitives, every one of them
    /// normalised to a peak channel of exactly 1.0 — under the 1.05 bloom threshold, so the hit is
    /// legible without ever competing with a tell. The read comes from shape, direction and SIZE, and
    /// size is where the weapon's weight is spoken:</para>
    ///
    /// <list type="bullet">
    ///   <item><b>Flare</b> at the contact point — the punctuation. 0.26 m for the needle, 0.42 m for
    ///   the maul, both under the deflect's own 0.5 m flare.</item>
    ///   <item><b>Spark fan</b> thrown BACK out of the wound toward the player, the same rule
    ///   <c>PlayerCombat.SparkAt</c> uses for a blow landing on you ("sparks fly back the way the blow
    ///   came from"). The maul throws MORE debris SLOWER into a WIDER cone: <c>SlashFx</c> pulls sparks
    ///   with gravity at −16 m/s², so a slower spark visibly falls in a heavier arc. The needle's are
    ///   few, fast and tight — a flick.</item>
    ///   <item><b>Shockwave ring</b>, and ONLY at the heavy end of the ladder. Sekiro's own split is the
    ///   precedent: a deflect gets more sparks AND a shockwave where a block gets only sparks, so the
    ///   ring is the reserved instrument for the biggest impact. Here it is the maul's alone.</item>
    /// </list>
    ///
    /// <para><b>The hierarchy this restores, loudest first:</b> deflect (3.2, warm bone-white, on the
    /// enemy's body) &gt; alert tell (3.0) &gt; deathblow mark (2.6) &gt; every ordinary contact — guard,
    /// block and this — at 1.0. A hit you must react to blooms; a hit you landed does not.</para>
    ///
    /// <para><b>Truthfulness.</b> The finisher bump fires only on the last step of the combo, which is
    /// the step that actually carries <c>WeaponData.ComboMultiplier</c>'s largest value — the same real
    /// state the audio already pitches down for. Nothing here plays for a state that did not happen.</para>
    /// </summary>
    public static class WeaponImpactFx
    {
        // ---------------------------------------------------------------- the mass curve

        /// <summary>Swing time of the lightest weapon in the shipped ladder (Rosethorn, 0.22 s). The
        /// bottom of the mass curve. Data, not a duplicate: this only interprets what the ladder says.</summary>
        public const float LightSwing = 0.22f;
        /// <summary>Swing time of the heaviest weapon in the shipped ladder (Verdigris, 0.86 s).</summary>
        public const float HeavySwing = 0.86f;

        /// <summary>
        /// 0 = needle, 1 = maul, read off the ONE number that already ranks the roster by commitment:
        /// <c>attackDuration</c>. Deriving weight from shipped data rather than adding a "weight" field
        /// means a retuned weapon's impact retunes with it and can never disagree with how it swings.
        /// Sword lands at 0.34 — nearer the dagger than the hammer, which is what a 0.44 s generalist is.
        /// </summary>
        public static float Mass(WeaponData w)
        {
            if (w == null) return 0.5f;
            return Mathf.Clamp01(Mathf.InverseLerp(LightSwing, HeavySwing, w.attackDuration));
        }

        // ---------------------------------------------------------------- shape constants

        /// <summary>Peak HDR channel of everything this class draws. <see cref="SlashFx"/> normalises
        /// every colour it is handed to exactly 1.0, so a hit confirm CANNOT bloom by construction —
        /// the same structural guarantee the sky uses, rather than a number a later pass can raise.</summary>
        public const float PeakChannel = 1.0f;

        /// <summary>Contact flare, metres, at the two ends of the ladder. The heavy end is held BELOW
        /// the deflect's own flare (<c>EnemyVisuals</c> fires 0.5 m on a parry) so that no routine hit
        /// is the biggest flash in a fight. The maul's extra weight is spoken by the RING and by the
        /// debris, not by out-sizing the payoff the whole game is built on.</summary>
        public const float FlareSizeLight = 0.26f;
        public const float FlareSizeHeavy = 0.42f;
        /// <summary>Contact flare life. Short at both ends — punctuation, never a lingering glow.</summary>
        public const float FlareSecondsLight = 0.07f;
        public const float FlareSecondsHeavy = 0.13f;

        /// <summary>Debris count. The maul throws more of it, because more mass moved.</summary>
        public const int SparkCountLight = 7;
        public const int SparkCountHeavy = 13;
        /// <summary>Debris speed, m/s. INVERTED on purpose: SlashFx sparks fall at −16 m/s², so slower
        /// debris draws a visibly heavier arc. Fast tight sparks read as a flick, slow falling ones as
        /// a slab landing.</summary>
        public const float SparkSpeedLight = 9.0f;
        public const float SparkSpeedHeavy = 6.0f;
        /// <summary>Cone half-angle, degrees. A needle punctures; a maul sprays.</summary>
        public const float SparkSpreadLight = 22f;
        public const float SparkSpreadHeavy = 62f;

        /// <summary>Mass at or above which a hit earns a shockwave ring. Only the maul clears it
        /// (hammer 1.00, sword 0.34, dagger 0.00). The ring is the reserved "this was big" instrument;
        /// giving it to everything would make it mean nothing.</summary>
        public const float ShockwaveMass = 0.5f;
        /// <summary>Ring radius, metres, at full mass. Read at the maul's ~2.5 m contact distance, so it
        /// is far above the ~0.05 m sub-pixel floor (standing rule 4).</summary>
        public const float ShockwaveRadius = 1.10f;
        /// <summary>Ring life — longer than the flash it accompanies, so the wave outlives the bang.
        /// Same shape SentryBurst uses for the detonation.</summary>
        public const float ShockwaveSeconds = 0.20f;

        /// <summary>How much bigger the last step of a combo lands. Modest: it is a real state worth
        /// marking, not a second alarm.</summary>
        public const float FinisherScale = 1.25f;
        /// <summary>The finisher still sits below the deflect's 0.50 m contact flare. A multiplier
        /// alone let the maul reach 0.525 m and silently invert the spatial payoff hierarchy.</summary>
        public const float FlareSizeCeiling = 0.48f;
        /// <summary>Extra debris on the combo finisher.</summary>
        public const int FinisherSparkBonus = 3;

        /// <summary>Upward bias on the debris cone so the fan arcs into view instead of straight at the
        /// lens. Matches <c>PlayerCombat.SparkAt</c>'s 0.45.</summary>
        public const float SparkRise = 0.45f;

        // ---------------------------------------------------------------- readouts (tested)

        public static float FlareSize(WeaponData w, bool finisher)
        {
            float s = Mathf.Lerp(FlareSizeLight, FlareSizeHeavy, Mass(w));
            return Mathf.Min(finisher ? s * FinisherScale : s, FlareSizeCeiling);
        }

        public static float FlareSeconds(WeaponData w) =>
            Mathf.Lerp(FlareSecondsLight, FlareSecondsHeavy, Mass(w));

        public static int SparkCount(WeaponData w, bool finisher)
        {
            int n = Mathf.RoundToInt(Mathf.Lerp(SparkCountLight, SparkCountHeavy, Mass(w)));
            return finisher ? n + FinisherSparkBonus : n;
        }

        public static float SparkSpeed(WeaponData w) =>
            Mathf.Lerp(SparkSpeedLight, SparkSpeedHeavy, Mass(w));

        public static float SparkSpread(WeaponData w) =>
            Mathf.Lerp(SparkSpreadLight, SparkSpreadHeavy, Mass(w));

        public static bool HasShockwave(WeaponData w) => Mass(w) >= ShockwaveMass;

        // ---------------------------------------------------------------- the effect

        /// <summary>
        /// Draw one melee contact. <paramref name="point"/> is the surface point the blade met (never
        /// the enemy's centre — an effect drawn anywhere but where it happened reads as an explosion
        /// with no author, the lesson the riposte pass paid for). <paramref name="swingDir"/> is the
        /// direction the blow travelled; debris comes back out along it.
        /// </summary>
        public static void Hit(Vector3 point, Vector3 swingDir, WeaponData w, bool finisher)
        {
            Vector3 fwd = swingDir.sqrMagnitude > 1e-6f ? swingDir.normalized : Vector3.forward;
            Color hue = w != null ? w.neon : Color.white;

            SlashFx.Flare(point, hue, FlareSize(w, finisher), FlareSeconds(w));

            Vector3 back = (-fwd + Vector3.up * SparkRise).normalized;
            SlashFx.Sparks(point, back, hue, SparkCount(w, finisher), SparkSpeed(w), SparkSpread(w));

            // The wave lies in the SCREEN plane (normal pointing back at the lens), so its whole
            // circumference is visible from where the player is standing rather than edge-on.
            if (HasShockwave(w))
                SlashFx.Ring(point, -fwd, hue, ShockwaveRadius, ShockwaveSeconds);
        }
    }
}
