using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Neon White flavoured first person motor: snappy ground acceleration, momentum preserving air
    /// control, coyote time, jump buffering, variable jump height, a dash with one air charge, a
    /// momentum SLIDE and a WALL JUMP.
    ///
    /// PERFORMANCE CONTRACT. This runs every frame for the whole session, so it allocates nothing.
    /// No LINQ, no closures, no per-frame arrays, no strings. The only physics queries beyond
    /// CharacterController.Move are:
    ///   * the wall scan, which runs ONLY on the frame a buffered jump is resolved while airborne
    ///     (<see cref="FindWall"/>, at most 8 non-allocating spherecasts on that one frame), and
    ///   * the ceiling probe, which runs ONLY on the frame a slide tries to end
    ///     (<see cref="CeilingBlocked"/>, one non-allocating spherecast).
    /// Both use *NonAlloc with a cached buffer and an explicit layer mask, never ~0.
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

        [Header("Slide")]
        [Tooltip("Speed ADDED on entry, on top of whatever you were already carrying. A slide is a " +
                 "reward for entering it fast, not a way to get fast from a standstill.")]
        public float slideBoost = 5f;
        [Tooltip("Hard ceiling on entry speed, so a dash landing cannot be laundered into a rocket.")]
        public float slideMaxSpeed = 22f;
        [Tooltip("Below this you cannot start a slide — it is a momentum move, not a crouch.")]
        public float slideMinEntrySpeed = 5f;
        [Tooltip("Speed lost per second as a fraction, the same shape as groundFriction. Low: the " +
                 "whole point is that the slide CARRIES.")]
        public float slideFriction = 2f;
        [Tooltip("The slide ends when it decays to this. Still above a walk, so you exit with something.")]
        public float slideEndSpeed = 8f;
        [Tooltip("Hard cap on one slide, so a downhill slide can never run forever.")]
        public float slideMaxDuration = 0.9f;
        public float slideCooldown = 0.3f;
        [Tooltip("CharacterController height while sliding. The stand height is read off the collider " +
                 "at Awake, so this is the only number that decides what you fit under.")]
        public float slideHeight = 0.9f;
        [Tooltip("How fast a slide can be steered, in m/s of direction change. Deliberately far below " +
                 "groundAccel: committing to a line is the cost of the speed.")]
        public float slideSteerAccel = 8f;

        [Header("Wall jump")]
        [Tooltip("How far from the capsule surface a wall counts. A spherecast, not a ray — this game " +
                 "rewards flow and a ray punishes a 5 cm miss.")]
        public float wallCheckDistance = 0.55f;
        [Tooltip("Vertical speed set by a wall jump. 11 against gravity -30 is a 2.0 m rise.")]
        public float wallJumpUpSpeed = 11f;
        [Tooltip("Speed added along the wall normal. Momentum ALONG the wall is preserved; only the " +
                 "component going INTO the wall is dropped.")]
        public float wallJumpPushSpeed = 12f;
        [Tooltip("Wall jumps allowed before touching the ground again. Bounds a chimney climb; without " +
                 "it a pair of facing walls is an infinite elevator.")]
        public int maxWallJumps = 5;
        [Tooltip("Two wall normals closer together than this cosine count as THE SAME WALL and the " +
                 "second is refused. 0.85 ~ 32 deg. This is what stops free vertical climbing.")]
        public float sameWallCosineLimit = 0.85f;

        public bool CanMove = true;
        /// <summary>Temporary speed scalar (item effects). 1 = normal.</summary>
        public float SpeedMultiplier = 1f;

        public Vector3 Velocity => vel;
        public bool IsGrounded { get; private set; }
        public bool IsDashing => Time.time < dashUntil;
        public bool IsSliding => sliding;
        /// <summary>Wall jumps spent since the last landing. Resets on ground contact.</summary>
        public int WallJumpsUsed => wallJumpsUsed;
        public Vector3 LastGroundedPosition { get; private set; }
        /// <summary>Downward speed at the moment of the last landing (for camera dip / land sfx).</summary>
        public float LastLandingSpeed { get; private set; }
        public float HorizontalSpeed => new Vector2(vel.x, vel.z).magnitude;
        /// <summary>Collider height the player stands at, sampled from the prefab at Awake.</summary>
        public float StandHeight => standHeight;

        public event Action OnLanded, OnJumped, OnDashed;
        /// <summary>Raised when a slide starts / ends, and when a wall jump fires. PlayerFeedback listens.</summary>
        public event Action OnSlideStarted, OnSlideEnded, OnWallJumped;

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

        /// <summary>
        /// Start a slide. Same contract as <see cref="TryJump"/> / <see cref="TryDash"/>: Update calls it
        /// when <see cref="InputReader"/> reports the press, tests call it directly.
        ///
        /// Refused unless you are on the ground (or inside coyote time) and already moving at
        /// <see cref="slideMinEntrySpeed"/>. Returns true only if a slide actually began.
        /// </summary>
        public bool TrySlide()
        {
            if (!CanAct || sliding) return false;
            if (Time.time < slideReadyAt) return false;
            if (!IsGrounded && Time.time - lastGroundedTime > coyoteTime) return false;

            float sx = vel.x, sz = vel.z;
            float sp = Mathf.Sqrt(sx * sx + sz * sz);
            if (sp < slideMinEntrySpeed) return false;

            float entry = Mathf.Min(Mathf.Max(sp, groundSpeed * SpeedMultiplier) + slideBoost, slideMaxSpeed);
            float k = entry / sp;
            vel.x = sx * k;
            vel.z = sz * k;

            sliding = true;
            slideEndsAt = Time.time + slideMaxDuration;
            SetHeight(slideHeight);

            // RESEAT THE CONTROLLER. Resizing a CharacterController drops its ground contact until the
            // next Move, so the frame after a slide started reported IsGrounded == false and the airborne
            // branch cancelled the slide on the spot. It survived at 500 fps, where contact came back
            // inside coyote time, and died at 20 fps, where it did not — a slide that works only on a
            // fast machine is worse than one that never works. One tiny downward Move re-establishes
            // contact immediately; the coyote tolerance below is the backstop, not the fix.
            cc.Move(Vector3.down * Mathf.Max(0.02f, cc.skinWidth * 2f));

            if (OnSlideStarted != null) OnSlideStarted();
            return true;
        }

        /// <summary>
        /// End a slide and stand back up. Returns false — and stays sliding — when there is not enough
        /// headroom to stand, which is what lets a slide pass UNDER geometry without the player popping
        /// into it. Pass force:true only where standing is guaranteed safe.
        /// </summary>
        public bool EndSlide(bool force = false)
        {
            if (!sliding) return false;
            if (!force && CeilingBlocked()) return false;
            sliding = false;
            SetHeight(standHeight);
            slideReadyAt = Time.time + slideCooldown;
            if (OnSlideEnded != null) OnSlideEnded();
            return true;
        }

        /// <summary>
        /// Push off a wall. Airborne only — an ordinary jump owns the ground and the coyote window, so
        /// this can never steal one. Refused with no wall in range, once
        /// <see cref="maxWallJumps"/> are spent, and on the SAME wall twice in a row (see
        /// <see cref="sameWallCosineLimit"/>), which is what stops a single face being a free ladder.
        /// A facing pair of walls alternates normals, so a chimney climbs.
        /// </summary>
        public bool TryWallJump()
        {
            if (!CanAct) return false;
            if (IsGrounded || Time.time - lastGroundedTime <= coyoteTime) return false;
            if (wallJumpsUsed >= maxWallJumps) return false;

            Vector3 n;
            if (!FindWall(out n)) return false;

            // Keep what runs ALONG the wall, drop only what runs into it, then push off. Preserving the
            // tangential component is what makes a chimney feel like flow instead of a reset.
            float hx = vel.x, hz = vel.z;
            float into = hx * n.x + hz * n.z;
            if (into < 0f) { hx -= n.x * into; hz -= n.z * into; }
            hx += n.x * wallJumpPushSpeed;
            hz += n.z * wallJumpPushSpeed;

            float sp = Mathf.Sqrt(hx * hx + hz * hz);
            if (sp > dashSpeed) { float k = dashSpeed / sp; hx *= k; hz *= k; }

            vel.x = hx;
            vel.z = hz;
            vel.y = wallJumpUpSpeed;

            lastWallNormal = n;
            hasLastWall = true;
            wallJumpsUsed++;
            jumpPressedAt = -99f;
            EndSlide();   // not forced: under a low ceiling we stay slid and stand once clear
            if (OnWallJumped != null) OnWallJumped();
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

        bool sliding;
        float slideEndsAt, slideReadyAt;
        float standHeight = 1.8f;
        Vector3 standCenter;

        int wallJumpsUsed;
        Vector3 lastWallNormal;
        bool hasLastWall;

        /// <summary>Reused by every physics query here. Sized once; never grows, never allocates.</summary>
        readonly RaycastHit[] castHits = new RaycastHit[4];
        /// <summary>Everything the player can stand on or push off. Not ~0: Player and Enemy are excluded
        /// so you cannot wall jump off a grunt, and triggers are ignored at the call site so the
        /// Interactable layer's altars and checkpoints are invisible to it.</summary>
        int worldMask;

        PlayerCombat combat;
        PlayerLook look;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            combat = GetComponent<PlayerCombat>();
            look = GetComponent<PlayerLook>();
            LastGroundedPosition = transform.position;
            standHeight = cc.height;
            standCenter = cc.center;
            worldMask = ~((1 << Layers.Player) | (1 << Layers.Enemy) | (1 << Layers.Interactable));
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
                wallJumpsUsed = 0;
                hasLastWall = false;   // a landing forgives the wall you last used
                if (!wasGrounded)
                {
                    LastLandingSpeed = Mathf.Abs(vel.y);   // captured before vel.y is clamped to -2
                    if (OnLanded != null) OnLanded();
                }
                groundedPosTimer += dt;
                if (groundedPosTimer > 0.3f) { groundedPosTimer = 0f; LastGroundedPosition = transform.position; }
            }

            if (input.JumpPressed) TryJump();   // TryJump/TryDash/TrySlide re-apply the canAct gate themselves
            if (input.DashPressed) TryDash();
            if (input.SlidePressed) TrySlide();
            // NO release-cancel, deliberately. Ending the slide when the key comes up made the move
            // frame-rate dependent — one long frame is enough for a tap to be a whole slide — and it
            // made the slide untestable, because a scripted TrySlide holds no key. A slide is a
            // COMMITTED 0.35 s of speed; you cancel it by jumping or dashing out, which is the tech.

            Vector3 hv = new Vector3(vel.x, 0f, vel.z);

            if (IsDashing)
            {
                hv = dashDir * dashSpeed;
                vel.y = 0f;
            }
            else
            {
                // A slide keeps running on ground it has only just left. THIS IS LOAD-BEARING, not a
                // nicety: a CharacterController moving 0.8 m horizontally in one frame (16 m/s at 20 fps)
                // reports isGrounded FALSE on a flat floor, so a strictly-grounded slide bled no speed,
                // never reached its floor, and cancelled itself — 4.0 m at 500 fps and 1.8 m at 20 fps
                // from the same press. Framerate-dependent movement is unshippable in a speedrun game.
                bool slideOnGround = sliding && (IsGrounded || Time.time - lastGroundedTime <= coyoteTime);

                if (slideOnGround)
                {
                        // Steer slowly, bleed slowly. The slide is a committed line, and it is the
                        // ENTRY speed you are buying — jump-cancel it early and you keep all of it.
                        if (wish.sqrMagnitude > 0.001f)
                        {
                            float sp0 = hv.magnitude;
                            hv = Vector3.MoveTowards(hv, wish.normalized * sp0, slideSteerAccel * dt);
                        }
                        hv *= Mathf.Max(0f, 1f - slideFriction * dt);

                        bool spent = hv.magnitude <= slideEndSpeed || Time.time >= slideEndsAt;
                        if (spent && !EndSlide())
                        {
                            // A ceiling is holding us down. Keep enough speed to crawl clear rather
                            // than stall crouched under it forever — see ENGINEERING-LOG.
                            Vector3 d = wish.sqrMagnitude > 0.001f ? wish.normalized
                                      : (hv.sqrMagnitude > 0.0001f ? hv.normalized
                                      : new Vector3(transform.forward.x, 0f, transform.forward.z).normalized);
                            hv = d * Mathf.Max(hv.magnitude, slideEndSpeed);
                            slideEndsAt = Time.time + 0.2f;
                        }
                        // Only pinned to the floor when actually ON it — sliding off a ledge has to fall.
                        if (IsGrounded && vel.y < 0f) vel.y = -2f;
                }
                else if (IsGrounded)
                {
                    if (wish.sqrMagnitude < 0.001f)
                    {
                        float sp = hv.magnitude;
                        if (sp > 0f) hv *= Mathf.Max(0f, sp - sp * groundFriction * dt) / sp;
                        if (vel.y < 0f) vel.y = -2f;
                    }
                    else
                    {
                        // keep excess speed (dash landings) but steer toward wish direction
                        float target = Mathf.Max(groundSpeed * SpeedMultiplier, Mathf.Min(hv.magnitude, dashSpeed));
                        hv = Vector3.MoveTowards(hv, wish * target, groundAccel * dt);
                        if (hv.magnitude > groundSpeed * SpeedMultiplier) hv = Vector3.MoveTowards(hv, hv.normalized * groundSpeed * SpeedMultiplier, groundFriction * 2f * dt);
                        if (vel.y < 0f) vel.y = -2f;
                    }
                }
                else
                {
                    // Leaving the ground ends the slide but NEVER the momentum it bought: sliding off a
                    // ledge, or jump-cancelling one, is the whole point of the move. If there is no
                    // headroom to stand yet, EndSlide refuses and we retry next frame.
                    //
                    // COYOTE-TOLERANT, and it has to be: SHRINKING THE CHARACTERCONTROLLER BREAKS ITS
                    // GROUND CONTACT for a frame or two, so `if (sliding) EndSlide()` here cancelled
                    // every slide on the frame after it started — the slide worked and lasted 0.00 s.
                    // Reusing coyoteTime also means a lip, a seam between two platforms or a kerb no
                    // longer eats a slide, and sliding off a ledge still leaves the whole coyote window
                    // to jump-cancel with the speed intact.
                    if (sliding && Time.time - lastGroundedTime > coyoteTime) EndSlide();
                    hv = AirAccelerate(hv, wish, groundSpeed * SpeedMultiplier, airAccel, dt);
                }

                vel.y += gravity * dt;
                if (!IsGrounded && vel.y > 0f && !input.JumpHeld) vel.y += gravity * jumpCutGravityMultiplier * dt;

                bool canJump = Time.time - lastGroundedTime <= coyoteTime;
                bool buffered = Time.time - jumpPressedAt <= jumpBuffer;
                if (canAct && buffered)
                {
                    if (canJump)
                    {
                        // A slide-jump keeps hv untouched — that is the speedrun tech, and it is why
                        // EndSlide runs here rather than the jump resetting speed to a run.
                        EndSlide();
                        vel.y = Mathf.Sqrt(2f * -gravity * jumpHeight);
                        jumpPressedAt = -99f;
                        lastGroundedTime = -99f;
                        IsGrounded = false;
                        if (OnJumped != null) OnJumped();
                    }
                    else
                    {
                        // Airborne with a pending press: the only remaining jump is off a wall. This is
                        // the ONLY frame the wall scan runs.
                        vel.x = hv.x; vel.z = hv.z;         // TryWallJump reads and writes vel
                        if (TryWallJump()) { hv.x = vel.x; hv.z = vel.z; }
                    }
                }

                if (canAct && dashRequested && Time.time >= dashReadyAt && (IsGrounded || !airDashUsed))
                {
                    EndSlide();
                    dashDir = wish.sqrMagnitude > 0.01f ? wish.normalized : new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
                    dashUntil = Time.time + dashDuration;
                    dashReadyAt = Time.time + dashCooldown;
                    if (!IsGrounded) airDashUsed = true;
                    hv = dashDir * dashSpeed;
                    vel.y = 0f;
                    if (OnDashed != null) OnDashed();
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

        // ---------------------------------------------------------------- collider height

        /// <summary>Resize the capsule about the FEET, so lowering it never lifts or drops the player.</summary>
        void SetHeight(float h)
        {
            if (cc == null) return;
            cc.height = h;
            cc.center = new Vector3(standCenter.x, standCenter.y - (standHeight - h) * 0.5f, standCenter.z);
        }

        /// <summary>
        /// Is there room to stand back up? One non-allocating spherecast straight up through the space
        /// the standing capsule would occupy. Runs only on the frame a slide tries to end.
        /// </summary>
        public bool CeilingBlocked()
        {
            if (cc == null) return false;
            float r = Mathf.Max(0.05f, cc.radius * 0.95f);
            float gap = standHeight - cc.height;
            if (gap <= 0.001f) return false;
            Vector3 origin = transform.position + Vector3.up * (cc.height - r);
            return Physics.SphereCastNonAlloc(origin, r, Vector3.up, castHits, gap + 0.05f,
                                              worldMask, QueryTriggerInteraction.Ignore) > 0;
        }

        // ---------------------------------------------------------------- wall detection

        /// <summary>
        /// Eight spherecasts fanned around the direction the player is asking for, nearest-aligned wall
        /// wins. A fan rather than one cast forward because a wall jump taken while looking anywhere but
        /// straight at the wall is the normal case in a first person game, and a single ray turns that
        /// into a silent miss.
        ///
        /// PUBLIC so tests can assert "no wall here" without jumping. Costs 8 spherecasts, and is called
        /// on at most one frame per jump press.
        /// </summary>
        public bool FindWall(out Vector3 normal)
        {
            normal = Vector3.zero;
            if (cc == null) return false;

            Vector3 pref = new Vector3(vel.x, 0f, vel.z);
            if (pref.sqrMagnitude < 0.01f) pref = new Vector3(transform.forward.x, 0f, transform.forward.z);
            if (pref.sqrMagnitude < 0.0001f) return false;
            pref.Normalize();

            Vector3 origin = transform.position + cc.center;
            float r = Mathf.Max(0.05f, cc.radius * 0.9f);
            float best = -2f;
            bool found = false;

            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                float s = Mathf.Sin(a), c = Mathf.Cos(a);
                Vector3 dir = new Vector3(pref.x * c + pref.z * s, 0f, -pref.x * s + pref.z * c);

                int hits = Physics.SphereCastNonAlloc(origin, r, dir, castHits, wallCheckDistance,
                                                      worldMask, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < hits; h++)
                {
                    Vector3 raw = castHits[h].normal;
                    if (Mathf.Abs(raw.y) > 0.4f) continue;              // a floor or a ceiling, not a wall
                    Vector3 n = new Vector3(raw.x, 0f, raw.z);
                    if (n.sqrMagnitude < 0.0001f) continue;
                    n.Normalize();
                    // Same wall as the last push-off: refused, or one face is a free ladder.
                    if (hasLastWall && Vector3.Dot(n, lastWallNormal) > sameWallCosineLimit) continue;
                    float score = -(n.x * pref.x + n.z * pref.z);       // most directly faced wins
                    if (score > best) { best = score; normal = n; found = true; }
                }
            }
            return found;
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
            EndSlide(true);
            cc.enabled = false;
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            vel = Vector3.zero;
            dashUntil = 0f;
            wallJumpsUsed = 0;
            hasLastWall = false;
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
            EndSlide(true);
            vel.y = upSpeed;
            IsGrounded = false;
            lastGroundedTime = -99f;
        }
    }
}
