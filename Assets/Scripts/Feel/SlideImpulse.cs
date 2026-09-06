using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The MATH of a slide, with no Unity objects in it. <see cref="SlideFx"/> is the thin layer that
    /// hands these numbers to the camera, the grit and the scrape. Same arrangement as
    /// <see cref="ParryImpulse"/> and <see cref="DashImpulse"/>.
    ///
    /// <para><b>A slide is the hard one, because it is SUSTAINED.</b> A dash is a punctuation mark and a
    /// single impulse covers it. A slide has a beginning, a middle and an end, and the middle is where
    /// this move was reading as nothing at all: the entry fired one FOV kick that had decayed to zero
    /// inside 0.4 s of a move that lasts up to 0.9 s, so for most of a slide the world said nothing
    /// while the player was low and travelling at 20 m/s.</para>
    ///
    /// <para><b>So everything in the middle tracks ACTUAL SPEED</b> rather than firing once. FOV, scrape
    /// gain, scrape pitch and grit rate are all functions of the speed you are carrying this frame, which
    /// makes the slide's decay visible and audible: the world narrows back in and quietens as the move
    /// spends itself. <see cref="FovForSpeed"/> is deliberately built to reach exactly zero at
    /// <c>slideEndSpeed</c> — the speed the motor ends a slide at — so standing up never snaps the
    /// lens.</para>
    ///
    /// <para><b>Force, not light.</b> Nothing here brightens anything; <see cref="SlideFx"/> ships its
    /// grit below the 1.05 bloom threshold. Emission is spoken for in this project: on an enemy it means
    /// "you deflected", and traversal must never out-shout that.</para>
    /// </summary>
    public static class SlideImpulse
    {
        /// <summary>
        /// Normalised speed inside the slide's own band: 0 at the speed the motor gives up at, 1 at the
        /// speed cap it clamps entry to. Everything else here is a function of this.
        /// </summary>
        public static float SpeedFraction(float speed, float endSpeed, float maxSpeed)
        {
            float span = maxSpeed - endSpeed;
            if (span <= 0.0001f) return speed >= maxSpeed ? 1f : 0f;
            return Mathf.Clamp01((speed - endSpeed) / span);
        }

        /// <summary>
        /// SUSTAINED FOV widening while sliding, in degrees. Held for the whole move (see
        /// <c>CameraFX.FovHold</c>) rather than kicked once, and it decays with the speed rather than
        /// with a timer, so the lens is doing the same thing the legs are.
        ///
        /// <para>Zero at <paramref name="endSpeed"/> by construction. That is not a nicety: the motor
        /// ends a slide the instant it decays to that speed, and a hold that was still non-zero there
        /// would snap several degrees of FOV on the frame you stand up.</para>
        /// </summary>
        public static float FovForSpeed(float speed, float endSpeed, float maxSpeed, float maxHold)
        {
            return maxHold * SpeedFraction(speed, endSpeed, maxSpeed);
        }

        /// <summary>
        /// How hard the player is steering ACROSS their own line, signed, in [-1, 1]. The sine of the
        /// angle from velocity to wish about world up: 0 when you are holding your line, ±1 at 90°.
        ///
        /// <para>Both vectors are flattened first, so looking up or down cannot roll the camera — that
        /// would make the roll a function of the mouse rather than of the slide.</para>
        /// </summary>
        public static float LateralSteer(Vector3 velocity, Vector3 wish)
        {
            Vector3 v = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 w = new Vector3(wish.x, 0f, wish.z);
            if (v.sqrMagnitude < 1e-6f || w.sqrMagnitude < 1e-6f) return 0f;
            v.Normalize();
            w.Normalize();
            // y of the cross product is the signed sine about up: positive when wish is to the right.
            return Mathf.Clamp(Vector3.Cross(v, w).y, -1f, 1f);
        }

        /// <summary>
        /// Degrees of camera ROLL while sliding, banking into the steer.
        ///
        /// <para>Roll is the cheapest readability in the project's whole toolbox: it never moves the aim
        /// vector, so it can be the loudest part of a sustained effect at zero cost to the next parry or
        /// the next swing. <paramref name="speedFraction"/> scales it, so a slide that has spent itself
        /// banks less — you cannot lean on nothing.</para>
        /// </summary>
        public static float Roll(float lateralSteer, float speedFraction, float maxDeg)
        {
            return Mathf.Clamp(lateralSteer, -1f, 1f) * Mathf.Clamp01(speedFraction) * maxDeg;
        }

        /// <summary>
        /// Gain of the sustained scrape loop. Never reaches zero while the slide is alive — a scrape
        /// that fades out under you reads as the effect breaking, not as slowing down — so it floors at
        /// 40% and <see cref="SlideFx"/> fades the last of it out over 0.12 s when the slide ends.
        /// </summary>
        public static float ScrapeGain(float speedFraction, float maxGain)
        {
            return maxGain * (0.4f + 0.6f * Mathf.Clamp01(speedFraction));
        }

        /// <summary>
        /// Playback rate of the scrape loop. Pitch is what the ear actually reads as speed on a
        /// broadband noise source — level alone is read as distance. Just under an octave of travel.
        /// </summary>
        public static float ScrapePitch(float speedFraction)
        {
            return Mathf.Lerp(0.78f, 1.32f, Mathf.Clamp01(speedFraction));
        }

        /// <summary>
        /// Grit particles per second. Floors at a third of the peak rate for the same reason the scrape
        /// does: the trail must not evaporate while you are still moving.
        /// </summary>
        public static float GritRate(float speedFraction, float peakRate)
        {
            return peakRate * (0.34f + 0.66f * Mathf.Clamp01(speedFraction));
        }

        /// <summary>
        /// Amplitude of the HELD camera rumble while sliding, metres. Quadratic in speed, so it is the
        /// first sustained channel to go quiet as the slide spends itself: a floor read through the
        /// body at 20 m/s is a rattle, at 9 m/s it is nothing, and unlike the scrape it has no floor —
        /// a rumble that never stops is a broken camera. Zero at the end speed by construction.
        /// </summary>
        public static float RumbleAmplitude(float speedFraction, float maxMetres)
        {
            float f = Mathf.Clamp01(speedFraction);
            return maxMetres * f * f;
        }

        /// <summary>
        /// One step of a damped harmonic spring, CLOSED FORM (the standard Juckett solution), so the
        /// result at any time is the same whatever the frame rate — an explicit spring at these
        /// stiffnesses blows up at 20 fps, and a frame-rate-dependent slide has shipped here once.
        /// <paramref name="omega"/> is the angular frequency (2π × Hz), <paramref name="zeta"/> the
        /// damping ratio: under 1 overshoots (see <see cref="OvershootFraction"/>), 1 is critical, over
        /// 1 crawls. Used for the eye's plop onto the floor and the legs' throw-out.
        /// </summary>
        public static void Spring(float x, float v, float target, float omega, float zeta, float dt,
                                  out float nx, out float nv)
        {
            const float Eps = 0.0001f;
            if (dt <= 0f || omega <= Eps) { nx = x; nv = v; return; }
            if (zeta < 0f) zeta = 0f;

            float pp, pv, vp, vv;
            if (zeta > 1f + Eps)
            {
                float za = -omega * zeta;
                float zb = omega * Mathf.Sqrt(zeta * zeta - 1f);
                float z1 = za - zb, z2 = za + zb;
                float e1 = Mathf.Exp(z1 * dt), e2 = Mathf.Exp(z2 * dt);
                float inv2zb = 1f / (2f * zb);
                float e1o = e1 * inv2zb, e2o = e2 * inv2zb;
                float z1e1o = z1 * e1o, z2e2o = z2 * e2o;
                pp = e1o * z2 - z2e2o + e2;
                pv = -e1o + e2o;
                vp = (z1e1o - z2e2o + e2) * z2;
                vv = -z1e1o + z2e2o;
            }
            else if (zeta < 1f - Eps)
            {
                float oz = omega * zeta;
                float alpha = omega * Mathf.Sqrt(1f - zeta * zeta);
                float e = Mathf.Exp(-oz * dt);
                float c = Mathf.Cos(alpha * dt), s = Mathf.Sin(alpha * dt);
                float invAlpha = 1f / alpha;
                float eSin = e * s, eCos = e * c;
                float eOzSinOverAlpha = e * oz * s * invAlpha;
                pp = eCos + eOzSinOverAlpha;
                pv = eSin * invAlpha;
                vp = -eSin * alpha - oz * eOzSinOverAlpha;
                vv = eCos - eOzSinOverAlpha;
            }
            else
            {
                float e = Mathf.Exp(-omega * dt);
                float te = dt * e;
                float tef = te * omega;
                pp = tef + e;
                pv = te;
                vp = -omega * tef;
                vv = -tef + e;
            }

            float d = x - target;
            nx = d * pp + v * pv + target;
            nv = d * vp + v * vv;
        }

        /// <summary>
        /// How far past its target an under-damped spring overshoots on its first swing, as a fraction
        /// of the step: exp(−ζπ / √(1−ζ²)). 0.55 → about 12%, 0.6 → about 9.5%, 1 → 0. This is what
        /// sizes the eye's plop below the slide height and the legs' throw past their pose.
        /// </summary>
        public static float OvershootFraction(float zeta)
        {
            if (zeta >= 1f) return 0f;
            if (zeta <= 0f) return 1f;
            return Mathf.Exp(-zeta * Mathf.PI / Mathf.Sqrt(1f - zeta * zeta));
        }
    }
}
