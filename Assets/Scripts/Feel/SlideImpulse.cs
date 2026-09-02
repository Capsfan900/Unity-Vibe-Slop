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
    }
}
