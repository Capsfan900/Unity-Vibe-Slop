using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The MATH of a dash's impact, with no Unity objects in it, so every shape is unit tested rather
    /// than asserted in prose. <see cref="DashFx"/> is the thin layer that hands these numbers to the
    /// camera and the streak geometry. Same arrangement as <see cref="ParryImpulse"/> /
    /// <c>ParryImpact</c>, deliberately.
    ///
    /// <para><b>What a dash IS.</b> An instant displacement — a punctuation mark, 0.16 s of it. Anything
    /// that ramps is wasted on it, so every shape here is front-loaded: the kick peaks inside two frames
    /// and the streaks are at full alpha on the frame they appear and only ever fade.</para>
    ///
    /// <para><b>Force, not light.</b> Nothing here brightens the frame past the bloom threshold. The
    /// streaks ship at channel 0.90, which is BELOW the 1.05 threshold, so a dash contributes exactly
    /// zero bloom and cannot compete with <c>EnemyVisuals.CueFlash</c> — the loudest event in any frame
    /// is still "an attack is parryable now". A dash gets rotation, translation, FOV and air instead.</para>
    ///
    /// <para><b>No yaw, on purpose.</b> A dash is very often the approach to a swing. Roll is free
    /// readability (it never moves the aim vector) and pitch returns to zero, but a yaw kick would drag
    /// the reticle off the thing you dashed at for the exact 0.14 s you are lining it up. See
    /// <see cref="FromDash"/>.</para>
    /// </summary>
    public static class DashImpulse
    {
        /// <summary>
        /// Fraction of the kick's life spent travelling out. Shorter than a deflect's 0.18 because a
        /// dash has no wind-up to hide behind: 0.14 of a 0.14 s kick is 20 ms, a frame and a bit at
        /// 60 Hz. Any slower and the camera reads as drifting rather than as being yanked.
        /// </summary>
        public const float KickAttackFraction = 0.14f;

        /// <summary>
        /// Turn "you were thrown THAT way" into a camera impulse.
        ///
        /// <para><paramref name="dirLocal"/> is the unit dash direction in CAMERA space (x right,
        /// z forward). The camera is displaced OPPOSITE the travel and recovers: for the first 20 ms the
        /// lens is left behind, then it catches up. That lag is the whole read — a camera that
        /// teleports with the body reports no acceleration at all, which is precisely why the dash felt
        /// like a position edit.</para>
        ///
        /// <para>Pitch scales with the FORWARD component (a forward dash lifts the view as you surge;
        /// a pure strafe has no pitch, honestly). Roll scales with the LATERAL component and banks INTO
        /// the dash, like a body leaning through a turn. Yaw is always zero — see the class note.</para>
        /// </summary>
        public static ParryImpulse.Kick FromDash(Vector3 dirLocal, float pitchDeg, float rollDeg, float offsetMetres)
        {
            Vector3 d = dirLocal.sqrMagnitude > 1e-6f ? dirLocal.normalized : Vector3.forward;
            float lateral = Mathf.Clamp(d.x, -1f, 1f);
            float forward = Mathf.Clamp(d.z, -1f, 1f);

            ParryImpulse.Kick k;
            // Negative euler.x pitches the view UP in Unity. A backward dash therefore dips it, which is
            // correct: you are being pulled away from what you are looking at.
            k.euler = new Vector3(-pitchDeg * forward, 0f, lateral * rollDeg);
            // Left behind, in the plane only. No vertical component: a dash is horizontal by
            // construction (the motor zeroes vel.y), so a y offset would be inventing a force.
            k.offset = new Vector3(-d.x, 0f, -d.z) * offsetMetres;
            return k;
        }

        /// <summary>
        /// Streak opacity <paramref name="t01"/> through the streak's life. FULL on frame one, quadratic
        /// out — the same falloff <see cref="CameraShake"/> and <see cref="ParryImpulse.KickCurve"/>
        /// already use, so the whole dash package dies together instead of one layer outliving another.
        /// Exactly zero outside [0,1) so a streak can never be left on screen.
        /// </summary>
        public static float StreakAlpha(float t01)
        {
            if (t01 < 0f || t01 >= 1f) return 0f;
            float u = 1f - t01;
            return u * u;
        }

        /// <summary>
        /// Where streak <paramref name="i"/> of <paramref name="count"/> sits around the view axis, in
        /// radians. Evenly spaced plus a fixed per-index offset: an even fan is a starburst decal and
        /// reads as a UI element, a jittered one reads as air.
        /// </summary>
        public static float StreakAngle(int i, int count, float jitter01)
        {
            if (count <= 0) return 0f;
            float step = Mathf.PI * 2f / count;
            return i * step + (jitter01 - 0.5f) * step * 0.8f;
        }

        /// <summary>
        /// Screen-space flow direction for the streak at angle <paramref name="angle"/>, given a dash
        /// direction in camera space.
        ///
        /// <para>A forward dash makes the world flow RADIALLY outward from the centre; a strafe makes it
        /// flow sideways across the frame. Real dashes are a mix, so the two are blended by the
        /// magnitudes of the forward and lateral components and renormalised. Always unit length (or
        /// the radial fallback), so the caller never has to special-case a degenerate dash.</para>
        /// </summary>
        public static Vector2 StreakFlow(float angle, Vector3 dirLocal)
        {
            Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector3 d = dirLocal.sqrMagnitude > 1e-6f ? dirLocal.normalized : Vector3.forward;
            // World flows OPPOSITE the travel: dash right, the frame streams left.
            Vector2 lateral = new Vector2(-d.x, -d.y);
            Vector2 v = radial * Mathf.Abs(d.z) + lateral * Mathf.Sqrt(d.x * d.x + d.y * d.y);
            if (v.sqrMagnitude < 1e-6f) return radial;
            return v.normalized;
        }
    }
}
