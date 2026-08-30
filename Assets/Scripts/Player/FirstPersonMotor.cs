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

        public Vector3 Velocity => vel;
        public bool IsGrounded { get; private set; }
        public bool IsDashing => Time.time < dashUntil;
        public Vector3 LastGroundedPosition { get; private set; }
        public float HorizontalSpeed => new Vector2(vel.x, vel.z).magnitude;

        public event Action OnLanded, OnJumped, OnDashed;

        CharacterController cc;
        Vector3 vel;
        float lastGroundedTime = -99f, jumpPressedAt = -99f;
        float dashUntil, dashReadyAt;
        bool airDashUsed;
        Vector3 dashDir;
        float groundedPosTimer;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            LastGroundedPosition = transform.position;
        }

        void Update()
        {
            if (!GameManager.IsPlaying || InputReader.I == null) return;
            var input = InputReader.I;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 m = CanMove ? input.MoveAxis : Vector2.zero;
            Vector3 wish = transform.right * m.x + transform.forward * m.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            bool wasGrounded = IsGrounded;
            IsGrounded = cc.isGrounded;
            if (IsGrounded)
            {
                lastGroundedTime = Time.time;
                airDashUsed = false;
                if (!wasGrounded) OnLanded?.Invoke();
                groundedPosTimer += dt;
                if (groundedPosTimer > 0.3f) { groundedPosTimer = 0f; LastGroundedPosition = transform.position; }
            }

            if (input.JumpPressed && CanMove) jumpPressedAt = Time.time;

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
                        float target = Mathf.Max(groundSpeed, Mathf.Min(hv.magnitude, dashSpeed));
                        hv = Vector3.MoveTowards(hv, wish * target, groundAccel * dt);
                        if (hv.magnitude > groundSpeed) hv = Vector3.MoveTowards(hv, hv.normalized * groundSpeed, groundFriction * 2f * dt);
                    }
                    if (vel.y < 0f) vel.y = -2f;
                }
                else
                {
                    hv = AirAccelerate(hv, wish, groundSpeed, airAccel, dt);
                }

                vel.y += gravity * dt;
                if (!IsGrounded && vel.y > 0f && !input.JumpHeld) vel.y += gravity * jumpCutGravityMultiplier * dt;

                bool canJump = Time.time - lastGroundedTime <= coyoteTime;
                if (CanMove && canJump && Time.time - jumpPressedAt <= jumpBuffer)
                {
                    vel.y = Mathf.Sqrt(2f * -gravity * jumpHeight);
                    jumpPressedAt = -99f;
                    lastGroundedTime = -99f;
                    IsGrounded = false;
                    OnJumped?.Invoke();
                }

                if (CanMove && input.DashPressed && Time.time >= dashReadyAt && (IsGrounded || !airDashUsed))
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
        }

        public void AddImpulse(Vector3 impulse) { vel += impulse; }
    }
}
