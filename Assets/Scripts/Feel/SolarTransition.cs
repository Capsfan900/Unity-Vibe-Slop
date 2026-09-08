using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The presentation half of a solar portal crossing: how the sun's shell parts on the approach, how
    /// its light closes over the eye, and the CUT that hides the teleport frame.
    ///
    /// <para><b>Why the cut is shaped this way.</b> A star does not fade you out — it overexposes you.
    /// So the cover is a bleach in the portal's own theme colour lifted toward white
    /// (<see cref="HotTint"/>), and the reveal resolves back to the saturated theme
    /// (<see cref="SettleTint"/>), which is also the colour of the light in the realm you land in. The
    /// cover is INSTANT and the reveal is the smooth part, which is the shape the ask names.</para>
    ///
    /// <para><b>Why there is an approach ramp at all.</b> The drawn sun (visual radius 22-31 m) is far
    /// bigger than the gameplay trigger (12-18 m), so the camera is inside the drawn shell for 10-13 m
    /// before the transport fires. A sphere with <c>Cull Back</c> renders NOTHING from inside, so the
    /// old behaviour was a hard pop: an opaque sun one frame, the bare court the next. <see cref="ShellFade"/>
    /// dissolves the shell over the last ~6.4 m of the approach and <see cref="Wash"/> picks the
    /// coverage back up on the eye, so the membrane parts instead of vanishing.</para>
    ///
    /// <para><b>The wash cannot lie.</b> It is a pure function of camera distance and is therefore
    /// reversible — a player who turns around simply walks back out of the glow. Only the CUT is
    /// irreversible, and it is fired from <see cref="SolarArenaPortal"/> only after a teleport has
    /// already succeeded, in the same synchronous call.</para>
    ///
    /// <para>Nothing here touches <c>Time.timeScale</c> (hard rule 1), the Input System (hard rule 2) or
    /// combat (hard rule 3). Every timer is unscaled, so a cut always completes — including through a
    /// death, a hitstop or a pause — and there is no handle that could be leaked.</para>
    /// </summary>
    public static class SolarTransition
    {
        // ---------------- the cut ----------------

        /// <summary>Wall-clock beat the cover stays fully opaque. ~5-6 frames at 60 Hz.</summary>
        public const float HoldSeconds = 0.09f;

        /// <summary>
        /// RENDERED frames the cover must survive, independent of the clock. A wall-clock hold is not a
        /// frame budget: on a hitching frame 0.09 s can elapse inside a single Update, which would let
        /// the reveal begin before the destination has ever been drawn. Three presented frames is the
        /// floor no matter how slow the machine is.
        /// </summary>
        public const int MinHoldFrames = 3;

        /// <summary>Reveal length. Long enough to read as a dissolve, short enough not to tax a run.</summary>
        public const float RevealSeconds = 0.42f;

        /// <summary>Fraction of the reveal over which the bleach resolves from white-hot to the theme hue.</summary>
        public const float ColourSettleFraction = 0.40f;

        /// <summary>How far the theme colour is lifted toward white for the hot end of the cut.</summary>
        public const float HotLift = 0.75f;

        // ---------------- the approach ----------------

        /// <summary>Metres OUTSIDE the drawn surface at which the shell begins to part.</summary>
        public const float MembraneBand = 7f;

        /// <summary>
        /// Metres outside the drawn surface at which the shell is fully gone. Deliberately positive: the
        /// shell must be at zero BEFORE the camera can reach the surface, or one fast frame renders the
        /// sphere from inside — which is the exact artefact this pass exists to remove.
        /// </summary>
        public const float MembraneClear = 0.6f;

        /// <summary>Screen coverage the wash has reached by the time the shell has finished parting.</summary>
        public const float MembraneWash = 0.42f;

        /// <summary>Coverage at the gameplay trigger, so the CUT is a 0.08 step rather than a jump.</summary>
        public const float WashMax = 0.92f;

        /// <summary>
        /// Shapes the inward climb. High on purpose: the wash must stay low across most of the descent so
        /// the player can still see where they are flying, and rush only in the last few metres.
        /// </summary>
        public const float WashExponent = 3.5f;

        // ---------------- curves ----------------

        /// <summary>
        /// Opacity multiplier for the exterior shell, 1 far away and 0 before the camera reaches it.
        /// Feeds <c>_Fade</c> on VibeGame1/Solar Arena, which scales the FINAL colour and alpha — scaling
        /// <c>_Alpha</c> alone would leave the shader's independent rim term glowing at 0.22 forever.
        /// </summary>
        public static float ShellFade(float cameraDistance, float visualRadius)
        {
            float outer = visualRadius + MembraneBand;
            float inner = visualRadius + MembraneClear;
            if (cameraDistance >= outer) return 1f;
            if (cameraDistance <= inner) return 0f;
            float u = (cameraDistance - inner) / Mathf.Max(0.0001f, outer - inner);
            return u * u * (3f - 2f * u);
        }

        /// <summary>
        /// Screen coverage from the sun's own light: 0 outside the membrane band, <see cref="MembraneWash"/>
        /// at the drawn surface, <see cref="WashMax"/> at the gameplay trigger. Continuous at the seam.
        /// </summary>
        public static float Wash(float cameraDistance, float visualRadius, float triggerRadius)
        {
            if (visualRadius <= 0f) return 0f;
            float trigger = Mathf.Clamp(triggerRadius, 0.01f, visualRadius - 0.01f);
            float outer = visualRadius + MembraneBand;
            if (cameraDistance >= outer) return 0f;
            if (cameraDistance > visualRadius)
            {
                float m = (outer - cameraDistance) / MembraneBand;
                return MembraneWash * (m * m * (3f - 2f * m));
            }
            float n = Mathf.Clamp01((visualRadius - cameraDistance) / (visualRadius - trigger));
            return Mathf.Lerp(MembraneWash, WashMax, Mathf.Pow(n, WashExponent));
        }

        // ---------------- colour ----------------

        /// <summary>
        /// The saturated theme hue — the same colour as the point light inside the matching realm, so the
        /// cut resolves into the colour of the room it opens on. Mirrors
        /// <c>LevelDefinitionBuilder.ThemeColor</c>; both read the shipped <c>themeMaterialKey</c>.
        /// </summary>
        public static Color SettleTint(string themeKey)
        {
            if (themeKey == "SolarGold") return new Color(0.85f, 0.68f, 0.16f);
            if (themeKey == "SolarAzure") return new Color(0.20f, 0.42f, 1f);
            if (themeKey == "SolarGhost") return new Color(0.25f, 0.88f, 0.48f);
            return new Color(0.21f, 0.86f, 0.93f);
        }

        /// <summary>
        /// The bleached end of the same hue. Never above 1.0 in any channel: this draws on the HUD canvas
        /// and the UI does not bloom.
        /// </summary>
        public static Color HotTint(string themeKey)
        {
            Color c = Color.Lerp(SettleTint(themeKey), Color.white, HotLift);
            return new Color(Mathf.Min(1f, c.r), Mathf.Min(1f, c.g), Mathf.Min(1f, c.b), 1f);
        }

        /// <summary>
        /// Wash colour at a given coverage: saturated when the glow is thin, bleached as it closes, so the
        /// approach hands off to the cut without a colour step.
        /// </summary>
        public static Color WashTint(string themeKey, float amount)
        {
            return Color.Lerp(SettleTint(themeKey), HotTint(themeKey), Mathf.Clamp01(amount));
        }

        // ---------------- the call ----------------

        /// <summary>
        /// Covers the screen NOW, in the calling frame. Call it only after the transport has already
        /// happened: Unity renders no frame inside a callback, so a cover applied in the same call as the
        /// teleport can neither reveal the destination early nor play without a teleport behind it.
        /// </summary>
        public static void Cut(string themeKey)
        {
            var flash = ScreenFlash.I;
            if (flash == null) return;
            flash.Curtain(HotTint(themeKey), SettleTint(themeKey), HoldSeconds, MinHoldFrames,
                          RevealSeconds, ColourSettleFraction);
        }
    }
}
