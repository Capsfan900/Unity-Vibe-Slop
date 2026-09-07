using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A pure function from movement state to an ADDITIVE viewmodel offset. No <c>MonoBehaviour</c>, no
    /// singleton, no <c>Update</c> of its own — <see cref="Compute"/> takes a <see cref="State"/> and
    /// returns a <see cref="VibeGame1.Pose"/>, so the whole thing is unit-testable without a scene.
    ///
    /// <para><b>BACKLOG 2b, "Arms react to movement."</b> Since the parkour pivot the player is airborne,
    /// wall-running, sliding and dashing almost constantly, and nothing in <c>Assets/Scripts/Feel/</c>
    /// ever reached either viewmodel — wall run, slide, dash and air steering drove the CAMERA only
    /// (<see cref="PlayerLook"/>, <see cref="CameraShake"/>), and the walk bob in
    /// <see cref="WeaponViewmodel"/> / <see cref="OffhandViewmodel"/> only runs while
    /// <c>IsGrounded &amp;&amp; speed &gt; 0.5</c>. So the hands sat rigid through the one thing the
    /// player now does most of the time. This closes that gap.</para>
    ///
    /// <para><b>Additive, not a replacement.</b> Both viewmodels sum this on top of whatever pose they
    /// already wrote to <c>Model</c> that frame (the idle blend, an attack, the guard stance) and on top
    /// of their existing sway/bob. Delete the caller and either viewmodel behaves exactly as it did
    /// before this file existed — nothing here overwrites the existing pose maths.</para>
    ///
    /// <para><b>Secondary motion, on purpose.</b> Every term below is a few centimetres or a few
    /// degrees — an order of magnitude under a swing arc or the guard stance — because this must never
    /// compete with a channel that carries combat information (ANIMATION-VFX section 4). It is a
    /// reaction to the player's own movement, the same job sway and bob already do.</para>
    /// </summary>
    public static class MovementPose
    {
        /// <summary>
        /// Everything this needs to know about the current frame, read by the caller off
        /// <c>FirstPersonMotor</c>. A struct rather than a long parameter list so a future caller cannot
        /// silently transpose two floats.
        /// </summary>
        public struct State
        {
            public bool grounded;
            public bool sliding;
            public bool wallRunning;
            public bool dashing;

            /// <summary>
            /// The wall's normal, converted into the PLAYER BODY's local space (i.e.
            /// <c>motor.transform.InverseTransformDirection(motor.WallRunNormal)</c>) — the same frame
            /// <see cref="PlayerFeedback"/>'s wall-run camera kicks already use, because
            /// <see cref="PlayerLook"/> yaws the body and only pitches the head, so the body's inverse
            /// IS the camera's yaw frame. X is camera-right: its sign alone says which side the wall is
            /// on. Ignored unless <see cref="wallRunning"/> is true.
            /// </summary>
            public Vector3 wallNormalLocal;

            /// <summary>World-space vertical velocity, metres/second. Positive = rising.</summary>
            public float verticalVelocity;
        }

        // ---- tuning ------------------------------------------------------------------------------
        // Plain constants, not a ScriptableObject field (rule 9 is fine with that here — nothing below
        // is content or a balance dial, it is a presentation offset with no gameplay consequence).
        // Every one of them is a SECONDARY-motion number: single-digit centimetres, single-digit
        // degrees. Compare WeaponViewmodel.SwingArcOut (0.30 of a whole swing's chord) or GuardArcOut
        // (0.07 m) to see how much smaller these are meant to read.

        /// <summary>Fall speed, m/s, that reaches the full airborne drop/pull-back.</summary>
        const float AirborneFallReference = 14f;
        /// <summary>Metres the hands sink as the player falls.</summary>
        const float AirborneDropAmount = 0.045f;
        /// <summary>Degrees the hands lift (muzzle up) on ascent — a jump or a launch.</summary>
        const float AirborneRiseTilt = 6f;

        /// <summary>Metres the hands press toward the wall while wall-running.</summary>
        const float WallRunLeanAmount = 0.03f;
        /// <summary>Degrees of roll while wall-running, in the SAME direction PlayerLook's own camera
        /// lean already goes — reinforcing that lean rather than fighting it with an independent one.</summary>
        const float WallRunRollAmount = 4f;

        /// <summary>Metres the hands drop with the slide crouch.</summary>
        const float SlideDropAmount = 0.05f;
        /// <summary>Metres the hands lean forward, into the slide.</summary>
        const float SlideForwardAmount = 0.03f;
        /// <summary>Degrees the hands pitch down while sliding.</summary>
        const float SlideTiltAmount = 5f;

        /// <summary>Metres the hands trail behind on a dash — the g-force pulling the weapon back.</summary>
        const float DashPullBack = 0.05f;
        /// <summary>Degrees the hands tip back on a dash.</summary>
        const float DashTiltBack = 7f;

        /// <summary>
        /// Hard ceiling on the summed position offset, metres. Generous headroom over any single term
        /// above (worst case ~0.13 m if several stacked) while still guaranteeing a future term cannot
        /// quietly grow this into something that competes with a pose.
        /// </summary>
        const float MaxPosMagnitude = 0.14f;
        /// <summary>Hard ceiling per rotation axis, degrees.</summary>
        const float MaxEulerAxis = 20f;

        /// <summary>
        /// The whole function. Deterministic, no side effects, safe to call every frame or from a test
        /// with a hand-built <see cref="State"/>.
        /// </summary>
        public static Pose Compute(State s)
        {
            Vector3 pos = Vector3.zero;
            Vector3 euler = Vector3.zero;

            // AIRBORNE. Only when neither the ground nor a wall is carrying the player — sliding and
            // wall running have their own, stronger reads below, and stacking this on top of either
            // would double-count "not on solid ground" with a second, contradictory term.
            if (!s.grounded && !s.sliding && !s.wallRunning)
            {
                float k = Mathf.Clamp(-s.verticalVelocity / AirborneFallReference, -1f, 1f);
                float falling = Mathf.Clamp01(k);
                float rising = Mathf.Clamp01(-k);
                pos.y -= AirborneDropAmount * falling;
                pos.z -= AirborneDropAmount * 0.4f * falling;
                euler.x -= AirborneRiseTilt * rising;
            }

            // WALL RUN. Brace toward the wall; roll WITH the body's own lean rather than adding a second,
            // independent one that would fight it.
            if (s.wallRunning)
            {
                float side = Mathf.Clamp(-s.wallNormalLocal.x, -1f, 1f);
                pos.x += side * WallRunLeanAmount;
                euler.z += side * WallRunRollAmount;
            }

            // SLIDE. Hands drop and lean forward with the crouch PlayerBody already throws the legs into.
            if (s.sliding)
            {
                pos.y -= SlideDropAmount;
                pos.z += SlideForwardAmount;
                euler.x += SlideTiltAmount;
            }

            // DASH. IsDashing is already a short (~0.16 s) window on the motor, so this needs no timer
            // of its own — the caller's own smoothing (a lerp toward this target, the same trick sway
            // already uses) is what turns the in/out into a kick rather than a snap.
            if (s.dashing)
            {
                pos.z -= DashPullBack;
                euler.x += DashTiltBack;
            }

            pos = Vector3.ClampMagnitude(pos, MaxPosMagnitude);
            euler.x = Mathf.Clamp(euler.x, -MaxEulerAxis, MaxEulerAxis);
            euler.y = Mathf.Clamp(euler.y, -MaxEulerAxis, MaxEulerAxis);
            euler.z = Mathf.Clamp(euler.z, -MaxEulerAxis, MaxEulerAxis);
            return new Pose(pos, euler);
        }
    }
}
