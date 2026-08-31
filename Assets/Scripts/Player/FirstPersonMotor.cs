using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Neon White flavoured first person motor: snappy ground acceleration, momentum preserving air
    /// control, coyote time, jump buffering, variable jump height and a dash with one air charge.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonMotor : MonoBehaviour
    {
        [Header("Ground")]
        public float groundSpeed = 11f;
        public float groundAccel = 90f;
        public float groundFriction = 14f;

        [Header("Air")]
        public float airAccel = 35f;
        public float gravity = -30f;
        public float jumpHeight = 2.4f;
        public float jumpCutGravityMultiplier = 1.2f;
        public float coyoteTime = 0.12f;
        public float jumpBuffer = 0.12f;

        [Header("Dash")]
        public float dashSpeed = 22f;
        public float dashDuration = 0.16f;
        public float dashCooldown = 0.6f;

        public bool CanMove = true;
        /// <summary>Temporary speed scalar (item effects). 1 = normal.</summary>
        public float SpeedMultiplier = 1f;

        public Vector3 Velocity => vel;
        public bool IsGrounded { get; private set; }
        public bool IsDashing => Time.time < dashUntil;
        public Vector3 LastGroundedPosition { get; private set; }
        /// <summary>Downward speed at the moment of the last landing (for camera dip / land sfx).</summary>
        public float LastLandingSpeed { get; private set; }
        public float HorizontalSpeed => new Vector2(vel.x, vel.z).magnitude;

        public event Action OnLanded, OnJumped, OnDashed;

        /// <summary>
        /// Set by input, or by <see cref="RequestDash"/>. Consumed at the end of every movement step so a
        /// request never survives into a later frame.
        /// </summary>
        bool dashRequested;

        /// <summary>True while the motor will accept a jump or dash. False when movement is disabled or
        /// posture is broken — the same gate <see cref="Update"/> applies to input.</summary>
        public bool CanAct => CanMove && !(combat != null && combat.IsStaggered);

        /// <summary>
        /// Buffers a jump exactly as pressing the key does — coyote time, jump buffering and the canAct
        /// gate all still apply, so this is a real jump and not a teleport. <see cref="Update"/> calls
        /// this when <see cref="InputReader"/> reports the press; input is still read only there, so
        /// tests and scripted sequences have an entry point that needs no synthesised input device.
        /// Returns whether the request was accepted (not whether a jump ultimately fired).
        /// </summary>
        public bool TryJump()
        {
            if (!CanAct) return false;
            jumpPressedAt = Time.time;
            return true;
        }

        /// <summary>Queues a dash for the next movement step. Cooldown and air-dash rules still apply.
        /// Same contract as <see cref="TryJump"/>: Update calls it on input, tests call it directly.</summary>
        public bool TryDash()
        {
            if (!CanAct) return false;
            dashRequested = true;
            return true;
        }

        public void RequestJump() { TryJump(); }
        public void RequestDash() { TryDash(); }

        CharacterController cc;
        Vector3 vel;
        float lastGroundedTime = -99f, jumpPressedAt = -99f;
        float dashUntil, dashReadyAt;
        bool airDashUsed;
        Vector3 dashDir;
        float groundedPosTimer;

        PlayerCombat combat;
        PlayerLook look;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            combat = GetComponent<PlayerCombat>();
            look = GetComponent<PlayerLook>();
            LastGroundedPosition = transform.position;
        }

        void Update()
        {
            if (!GameManager.IsPlaying || InputReader.I == null) return;
            var input = InputReader.I;
            // Player movement runs on a clock that ignores hitstop and slow-mo, so landing a hit
            // never brakes your momentum. See TimeScaleController.PlayerDelta.
            float dt = TimeScaleController.PlayerDelta;
            if (dt <= 0f) return;

            // Posture broken: heavily slowed and unable to jump or dash, but NOT frozen — a full
            // lock-up over a pit would turn a stagger into a fall death.
            bool staggered = combat != null && combat.IsStaggered;
            bool canAct = CanMove && !staggered;

            Vector2 m = CanMove ? input.MoveAxis : Vector2.zero;
            Vector3 wish = transform.right * m.x + transform.forward * m.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            if (staggered) wish *= 0.4f;

            bool wasGrounded = IsGrounded;
            IsGrounded = cc.isGrounded;
            if (IsGrounded)
            {
                lastGroundedTime = Time.time;
                airDashUsed = false;
                if (!wasGrounded)
                {
                    LastLandingSpeed = Mathf.Abs(vel.y);   // captured before vel.y is clamped to -2
                    OnLanded?.Invoke();
                }
                groundedPosTimer += dt;
                if (groundedPosTimer > 0.3f) { groundedPosTimer = 0f; LastGroundedPosition = transform.position; }
            }

            if (input.JumpPressed) TryJump();   // TryJump/TryDash re-apply the canAct gate themselves
            if (input.DashPressed) TryDash();

            Vector3 hv = new Vector3(vel.x, 0f, vel.z);

            if (IsDashing)
            {
                hv = dashDir * dashSpeed;
                vel.y = 0f;
            }
            else
            {
                if (IsGrounded)
                {
                    if (wish.sqrMagnitude < 0.001f)
                    {
                        float sp = hv.magnitude;
                        if (sp > 0f) hv *= Mathf.Max(0f, sp - sp * groundFriction * dt) / sp;
                    }
                    else
                    {
                        // keep excess speed (dash landings) but steer toward wish direction
                        float target = Mathf.Max(groundSpeed * SpeedMultiplier, Mathf.Min(hv.magnitude, dashSpeed));
                        hv = Vector3.MoveTowards(hv, wish * target, groundAccel * dt);
                        if (hv.magnitude > groundSpeed * SpeedMultiplier) hv = Vector3.MoveTowards(hv, hv.normalized * groundSpeed * SpeedMultiplier, groundFriction * 2f * dt);
                    }
                    if (vel.y < 0f) vel.y = -2f;
                }
                else
                {
                    hv = AirAccelerate(hv, wish, groundSpeed * SpeedMultiplier, airAccel, dt);
                }

                vel.y += gravity * dt;
                if (!IsGrounded && vel.y > 0f && !input.JumpHeld) vel.y += gravity * jumpCutGravityMultiplier * dt;

                bool canJump = Time.time - lastGroundedTime <= coyoteTime;
                if (canAct && canJump && Time.time - jumpPressedAt <= jumpBuffer)
                {
                    vel.y = Mathf.Sqrt(2f * -gravity * jumpHeight);
                    jumpPressedAt = -99f;
                    lastGroundedTime = -99f;
                    IsGrounded = false;
                    OnJumped?.Invoke();
                }

                if (canAct && dashRequested && Time.time >= dashReadyAt && (IsGrounded || !airDashUsed))
                {
                    dashDir = wish.sqrMagnitude > 0.01f ? wish.normalized : new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
                    dashUntil = Time.time + dashDuration;
                    dashReadyAt = Time.time + dashCooldown;
                    if (!IsGrounded) airDashUsed = true;
                    hv = dashDir * dashSpeed;
                    vel.y = 0f;
                    OnDashed?.Invoke();
                }
            }

            // One request lives exactly one movement step. Without this a dash pressed while already
            // dashing (the branch above is skipped) would fire again the instant the dash ended.
            dashRequested = false;

            vel.x = hv.x;
            vel.z = hv.z;
            var flags = cc.Move(vel * dt);
            if ((flags & CollisionFlags.Above) != 0 && vel.y > 0f) vel.y = 0f;
        }

        static Vector3 AirAccelerate(Vector3 v, Vector3 wish, float maxSpeed, float accel, float dt)
        {
            if (wish.sqrMagnitude < 0.0001f) return v;
            Vector3 dir = wish.normalized;
            float current = Vector3.Dot(v, dir);
            float add = maxSpeed - current;
            if (add <= 0f) return v;
            float a = Mathf.Min(accel * dt, add);
            return v + dir * a;
        }

        public void Teleport(Vector3 position, float yaw)
        {
            cc.enabled = false;
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            vel = Vector3.zero;
            dashUntil = 0f;
            LastGroundedPosition = position;
            cc.enabled = true;

            // PlayerLook owns yaw and rewrites transform.rotation every frame, so setting the rotation
            // here alone is silently undone on the next Update. Push the yaw into it too, otherwise any
            // caller that teleports without also calling PlayerLook.SetYaw gets the old facing back.
            if (look == null) look = GetComponent<PlayerLook>();
            if (look != null) look.SetYaw(yaw);
        }

        public void AddImpulse(Vector3 impulse) { vel += impulse; }

        /// <summary>Launch straight up at a fixed speed (Updraft item). Cancels any dash.</summary>
        public void Launch(float upSpeed)
        {
            dashUntil = 0f;
            airDashUsed = false;
            vel.y = upSpeed;
            IsGrounded = false;
            lastGroundedTime = -99f;
        }
    }
}
