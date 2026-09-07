using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The arithmetic of the three traversal pieces the 2026-09-04 pivot added — balloons, water and
    /// the grapple exit burst — as PURE STATIC FUNCTIONS with no Unity objects in them, following the
    /// <see cref="WallRunMath"/> / <see cref="SlideImpulse"/> pattern: <see cref="FirstPersonMotor"/>
    /// calls these a frame at a time, and <c>PivotMovementTests</c> calls them with no scene.
    ///
    /// <para><b>Balloon.</b> A launch REPLACES the vertical component and keeps the horizontal one —
    /// Neon White's "capped jump": you always leave a balloon at exactly its launch speed, however hard
    /// you were falling or rising into it, so the height it buys is the same every time and a level can
    /// be authored against it. A player who is DASHING through it keeps the dash instead (the orb is a
    /// gate you punch through, not a trampoline you bounce on) and gets the dash re-armed.</para>
    ///
    /// <para><b>Water.</b> Flowing ground you skate on. The motor keeps a velocity RELATIVE to the flow;
    /// water holds that at or above a floor (<c>groundSpeed × waterSpeedScale</c>) with no friction and no
    /// overspeed decay, turns it at a skating rate rather than the ground's snap, and adds the flow back
    /// as a conveyor. Nothing here ever slows the player: water is the fastest surface, as in Neon White,
    /// and the only way off the floor speed is to leave the water or jump.</para>
    ///
    /// <para><b>Burst.</b> For a short window after a grapple pull ARRIVES, a dash press fires
    /// regardless of cooldown, air charge or stamina, at <c>dashSpeed × pullBurstMultiplier</c>. The
    /// window is measured on the motor clock and only opens once control is back (the deathblow
    /// cinematic holds <c>CanMove</c> false), so the burst is always something you can actually press.</para>
    /// </summary>
    public static class TraversalMath
    {
        /// <summary>A balloon launch: vertical REPLACED by <paramref name="upSpeed"/>, horizontal kept.</summary>
        /// <summary>
        /// Gravity's component ALONG a ground plane, scaled — the horizontal acceleration a slide feels
        /// on a hill. Downhill on the XZ plane; magnitude <c>g·sin(theta)·cos(theta)·scale</c>, which is
        /// the horizontal part of the along-slope pull.
        ///
        /// <para><b>Exactly zero on flat ground</b>, and that is the whole safety argument for adding
        /// this to a shipped motor: every span authored before ramps existed is an axis-aligned box, so
        /// <paramref name="groundNormal"/> is <c>Vector3.up</c>, the projection is the zero vector, and
        /// the slide behaves bit-for-bit as it did. Nothing that was tuned on the flat can drift.</para>
        ///
        /// <para>Uphill is not a special case: a player sliding INTO a rise has velocity opposing this
        /// vector, so the same term bleeds them and the slide dies early on a climb. One law, both
        /// directions — no sign tests, nothing to get backwards.</para>
        ///
        /// <para>A degenerate or upward-of-vertical normal returns zero rather than throwing, because a
        /// CharacterController can report an odd contact for a frame in a corner and a movement law must
        /// never spike on one bad sample.</para>
        /// </summary>
        /// <param name="groundNormal">Unit normal of the surface; <c>Vector3.up</c> is flat.</param>
        /// <param name="scale">Fraction of gravity's pull the slide feels. 0 disables slopes entirely.</param>
        public static Vector3 SlopeAccel(Vector3 groundNormal, float scale)
        {
            if (scale <= 0f) return Vector3.zero;
            if (groundNormal.sqrMagnitude < 1e-6f) return Vector3.zero;
            Vector3 n = groundNormal.normalized;
            if (n.y >= 0.9999f) return Vector3.zero;                    // flat: nothing to gain
            if (n.y <= 0f) return Vector3.zero;                         // a wall or an overhang is not ground

            // Gravity projected onto the plane, then flattened to XZ: the slide's speed is horizontal,
            // and the motor owns vertical separately (vel.y is pinned while grounded).
            Vector3 g = Physics.gravity;
            Vector3 alongSlope = g - n * Vector3.Dot(g, n);
            alongSlope.y = 0f;
            return alongSlope * scale;
        }

        /// <summary>Slope of a ground normal in degrees; 0 is flat. Exposed so tests and the HUD can
        /// talk about a hill in the same units a level designer authors one in.</summary>
        public static float SlopeDegrees(Vector3 groundNormal)
        {
            if (groundNormal.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.Angle(groundNormal.normalized, Vector3.up);
        }

        /// <summary>Only contact with a walkable slope, moving down its fall line, sustains a dry slide.
        /// Actual grounded state is required: coyote time must not turn a departed ledge into a hill.</summary>
        public static bool SustainsDownhillSlide(Vector3 velocity, Vector3 groundNormal, float scale,
                                                float slopeLimit, bool grounded)
        {
            if (!grounded || groundNormal.sqrMagnitude < 1e-6f) return false;
            if (SlopeDegrees(groundNormal) > slopeLimit) return false;
            velocity.y = 0f;
            return Vector3.Dot(velocity, SlopeAccel(groundNormal, scale)) > 0.001f;
        }

        /// <summary>The original dry-slide arithmetic unless the body is descending a walkable slope.
        /// Downhill adds gravity without friction, capped at the existing slide ceiling; carried speed
        /// earned elsewhere is preserved, but the hill cannot increase it beyond that ceiling.</summary>
        public static Vector3 DrySlideStep(Vector3 velocity, Vector3 groundNormal, float scale, float friction,
                                          float speedCap, float dt, bool downhill)
        {
            if (downhill)
            {
                float ceiling = Mathf.Max(speedCap, velocity.magnitude);
                return Vector3.ClampMagnitude(velocity + SlopeAccel(groundNormal, scale) * dt, ceiling);
            }
            velocity += SlopeAccel(groundNormal, scale) * dt;
            velocity *= Mathf.Max(0f, 1f - friction * dt);
            return velocity;
        }

        /// <summary>Follow the contacted downhill plane as displacement, without writing vertical
        /// velocity. The motor calls this only for a grounded, non-rising downhill slide.</summary>
        public static float DownhillSnapY(Vector3 displacement, Vector3 groundNormal, float contactMargin)
        {
            if (groundNormal.y <= 0.0001f || contactMargin <= 0f) return displacement.y;
            float planeY = -(displacement.x * groundNormal.x + displacement.z * groundNormal.z) / groundNormal.y;
            return planeY < 0f ? Mathf.Min(displacement.y, planeY - contactMargin) : displacement.y;
        }

        public static Vector3 Launch(Vector3 vel, float upSpeed)
        {
            return Launch(vel, upSpeed, float.PositiveInfinity);
        }

        /// <summary>
        /// A pop that also CAPS the carried horizontal speed. From play (2026-09-05): a dash carried
        /// through an orb at 22 m/s flew 16 m past the next one. The chain is meant to be pop → aim →
        /// dash, so the carry is trimmed to a controllable speed and the re-armed dash supplies the reach.
        /// </summary>
        public static Vector3 Launch(Vector3 vel, float upSpeed, float carryCap)
        {
            Vector3 h = new Vector3(vel.x, 0f, vel.z);
            float m = h.magnitude;
            if (carryCap >= 0f && m > carryCap && m > 0.0001f) h *= carryCap / m;
            return new Vector3(h.x, Mathf.Max(0f, upSpeed), h.z);
        }

        /// <summary>
        /// What touching a balloon does: a dashing player keeps the dash and is re-armed (true), anyone
        /// else is launched (false). One place, so the motor and the balloon cannot disagree.
        /// </summary>
        public static bool DashesThrough(bool isDashing)
        {
            return isDashing;
        }

        /// <summary>
        /// One frame of skating. <paramref name="rel"/> is the horizontal velocity RELATIVE to the water's
        /// flow. The heading is the stick when held, else the current heading; the target speed along it
        /// is the larger of <paramref name="floorSpeed"/> and the speed already carried, so water never
        /// slows anyone and always lifts a walker to the floor. Approached at <paramref name="accel"/>
        /// m/s^2 — below the ground's 90 on purpose: turning on water is a skate, not a snap.
        /// A stationary body with no stick stays put: water carries you, it does not pull you in.
        /// </summary>
        public static Vector3 WaterStep(Vector3 rel, Vector3 wish, float floorSpeed, float accel, float dt)
        {
            rel.y = 0f;
            Vector3 w = new Vector3(wish.x, 0f, wish.z);
            float sp = rel.magnitude;
            Vector3 dir;
            if (w.sqrMagnitude > 0.0001f) dir = w.normalized;
            else if (sp > 0.05f) dir = rel / sp;
            else return rel;
            float target = Mathf.Max(floorSpeed, sp);
            return Vector3.MoveTowards(rel, dir * target, Mathf.Max(0f, accel) * dt);
        }

        /// <summary>World horizontal velocity on water: the skated velocity plus the flow conveyor.</summary>
        public static Vector3 WaterVelocity(Vector3 rel, Vector3 flow)
        {
            return new Vector3(rel.x + flow.x, 0f, rel.z + flow.z);
        }

        /// <summary>The conveyor: a normalised direction times a speed; zero direction = still water.</summary>
        public static Vector3 Flow(Vector3 direction, float speed)
        {
            Vector3 d = new Vector3(direction.x, 0f, direction.z);
            if (d.sqrMagnitude < 0.0001f || speed <= 0f) return Vector3.zero;
            return d.normalized * speed;
        }

        /// <summary>The speed a grapple exit burst fires at.</summary>
        public static float BurstSpeed(float dashSpeed, float multiplier)
        {
            return dashSpeed * Mathf.Max(0f, multiplier);
        }

        /// <summary>Is the burst window open at <paramref name="now"/>? Closed once <paramref name="until"/> passes.</summary>
        public static bool BurstOpen(float now, float until)
        {
            return now < until;
        }
    }
}
