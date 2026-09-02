using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Neon White flavoured first person motor: snappy ground acceleration, momentum preserving air
    /// control, coyote time, jump buffering, variable jump height, a dash with one air charge, a
    /// momentum SLIDE, a WALL JUMP and an Apex-style WALL RUN.
    ///
    /// PERFORMANCE CONTRACT. This runs every frame for the whole session, so it allocates nothing.
    /// No LINQ, no closures, no per-frame arrays, no strings. The only physics queries beyond
    /// CharacterController.Move are:
    ///   * the wall scan, which runs ONLY on the frame a buffered jump is resolved while airborne
    ///     (<see cref="FindWall"/>, at most 8 non-allocating spherecasts on that one frame),
    ///   * the ceiling probe, which runs ONLY on the frame a slide tries to end
    ///     (<see cref="CeilingBlocked"/>, one non-allocating spherecast), and
    ///   * the wall-run pair: <see cref="FindRunnableWall"/>, TWO spherecasts, only on frames where the
    ///     player is airborne, above wallRunMinEntrySpeed, off cooldown and still has run budget; and
    ///     ProbeWall, ONE spherecast per frame while a run is actually in progress.
    /// All use *NonAlloc with a cached buffer and an explicit layer mask, never ~0.
    ///
    /// The decision logic and every integral of the wall run live in <see cref="WallRunMath"/> at the
    /// bottom of this file, as pure static functions on plain structs, so LevelArcAnalyzer models the
    /// mechanic by CALLING IT rather than by reimplementing it, and EditMode tests can drive it with no
    /// scene at all.
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

        [Header("Wall run")]
        [Tooltip("Speed ALONG the face required to start a run. 7 against a groundSpeed of 11 means a " +
                 "sprint entry qualifies and a shuffle never does — a wall run is a reward for arriving " +
                 "fast, exactly like the slide.")]
        public float wallRunMinEntrySpeed = 7f;
        [Tooltip("Falling faster than this and the wall will not catch you. Bounds how much a run can " +
                 "rescue: it extends a line, it does not undo a plummet.")]
        public float wallRunMaxEntryFallSpeed = 9f;
        [Tooltip("Ceiling on |dot(direction of travel, wall normal)|. 0.55 ~ 33 deg off the wall plane. " +
                 "Above it you are running INTO the face, which is a collision, not a run.")]
        public float wallRunMaxApproachCos = 0.55f;
        [Tooltip("Floor on dot(flattened look, run direction). 0.30 ~ 72 deg. The INTENT term: you may " +
                 "glance around mid-run, but a run you never looked at never starts.")]
        public float wallRunMinLookAlongCos = 0.30f;
        [Tooltip("Hard cap on one run. A wall run that never ends is a floor; 1.6 s at an 11 m/s entry is " +
                 "13.5 m of wall with the stick released and 17.6 m held forward, the lengths a " +
                 "corridor should be authored to.")]
        public float wallRunMaxDuration = 1.6f;
        [Tooltip("Gravity multiplier at the START of a run. 0.10 of -30 is -3 m/s^2: nearly free.")]
        public float wallRunGravityStartScale = 0.10f;
        [Tooltip("Gravity multiplier at the END, reached on t^2. 0.60 of -30 is -18 m/s^2 — you can feel " +
                 "the loan being called in while you are still on the wall.")]
        public float wallRunGravityEndScale = 0.60f;
        [Tooltip("FLOOR on vertical speed at the moment of entry — the catch. 3 m/s under the run's own " +
                 "gravity is about 1.25 m of rise over the first 0.75 s, then it is paid back.")]
        public float wallRunEntryUpSpeed = 3f;
        [Tooltip("Exponential bleed of speed along the wall, per second, stick released. 0.35 is chosen " +
                 "so BOTH endings are real: a sprint entry (11 m/s) keeps 57% and rides the full 1.6 s " +
                 "clock out at 6.3 m/s, while a minimum entry (7 m/s) bleeds to the 5 m/s floor at " +
                 "~0.96 s - arrive fast and the wall carries you, scrape in and it drops you early. " +
                 "Live only inside ln(minEntry/minSustain)/maxDuration < decay < " +
                 "ln(groundSpeed/minSustain)/maxDuration (0.21..0.49 as shipped); outside it one of the " +
                 "two end conditions is dead code. WallRunTunablesTests holds that window.")]
        public float wallRunSpeedDecay = 0.35f;
        [Tooltip("Below this the run drops you. Above a walk, so you always leave a wall with something.")]
        public float wallRunMinSustainSpeed = 5f;
        [Tooltip("Top-up along the run while holding forward, capped at groundSpeed. Lets a committed " +
                 "player hold a long wall, never exceed a sprint on it.")]
        public float wallRunAccel = 14f;
        [Tooltip("Speed pressed INTO the wall each frame. Applied to displacement only and never " +
                 "integrated into velocity, so it holds contact around a slight curve and can never " +
                 "accumulate into a shove.")]
        public float wallRunStickSpeed = 2.5f;
        [Tooltip("Vertical speed leaving a run. Slightly under wallJumpUpSpeed — the run already bought " +
                 "you height, so the exit buys distance instead.")]
        public float wallRunExitUpSpeed = 10f;
        [Tooltip("Speed added along the wall normal on exit. Under wallJumpPushSpeed on purpose: a " +
                 "wall jump throws you OFF the wall, a run exit throws you DOWN THE LINE.")]
        public float wallRunExitPushSpeed = 7f;
        [Tooltip("Speed added along the run direction on exit. This is the payoff for having run.")]
        public float wallRunExitTangentBoost = 4f;
        [Tooltip("Runs allowed before touching the ground again. sameWallCosineLimit already forbids " +
                 "re-running the face you just left, so this bounds a zig-zag between two faces the way " +
                 "maxWallJumps bounds a chimney.")]
        public int maxWallRuns = 3;
        [Tooltip("Seconds before another run may start. Stops an inside corner from re-latching on the " +
                 "same frame you left it.")]
        public float wallRunCooldown = 0.25f;
        [Tooltip("Camera roll toward the wall, degrees. Most of what sells a wall run is this. Applied " +
                 "by PlayerLook about the camera's own forward axis, so the aim vector never moves.")]
        public float wallRunCameraRoll = 13f;
        [Tooltip("Roll impulse on the exit jump, degrees, AWAY from the wall. A second, transient " +
                 "channel in PlayerLook, summed with the sustained lean.")]
        public float wallRunExitRollKick = 7f;

        public bool CanMove = true;
        /// <summary>Temporary speed scalar (item effects). 1 = normal.</summary>
        public float SpeedMultiplier = 1f;

        public Vector3 Velocity => vel;
        public bool IsGrounded { get; private set; }
        public bool IsDashing => Time.time < dashUntil;
        public bool IsSliding => sliding;
        /// <summary>Wall jumps spent since the last landing. Resets on ground contact.</summary>
        public int WallJumpsUsed => wallJumpsUsed;
        public bool IsWallRunning => wallRunning;
        /// <summary>Wall runs spent since the last landing. Resets on ground contact.</summary>
        public int WallRunsUsed => wallRunsUsed;
        /// <summary>Seconds into the current run. 0 when not running.</summary>
        public float WallRunElapsed => wallRunning ? wallRunElapsed : 0f;
        /// <summary>Outward normal of the face currently being run. Zero when not running.</summary>
        public Vector3 WallRunNormal => wallRunning ? wallRunNormal : Vector3.zero;
        /// <summary>Unit direction the current run travels along the face. Zero when not running.</summary>
        public Vector3 WallRunDirection => wallRunning ? wallRunDir : Vector3.zero;
        public Vector3 LastGroundedPosition { get; private set; }
        /// <summary>Downward speed at the moment of the last landing (for camera dip / land sfx).</summary>
        public float LastLandingSpeed { get; private set; }
        public float HorizontalSpeed => new Vector2(vel.x, vel.z).magnitude;
        /// <summary>Collider height the player stands at, sampled from the prefab at Awake.</summary>
        public float StandHeight => standHeight;

        public event Action OnLanded, OnJumped, OnDashed;
        /// <summary>Raised when a slide starts / ends, and when a wall jump fires. PlayerFeedback listens.</summary>
        public event Action OnSlideStarted, OnSlideEnded, OnWallJumped;
        /// <summary>Raised when a wall run starts / ends. Nothing subscribes yet; the events exist so the
        /// feel layer can be wired without reopening the motor.</summary>
        public event Action OnWallRunStarted, OnWallRunEnded;

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

            // A run in progress OWNS the press. It already knows which face it is on, so it needs no
            // scan; and it must not go through FindWall, which would refuse that face outright (the run
            // set it as lastWallNormal on entry, and sameWallCosineLimit is doing its job). This is the
            // composition the design asks for: one public "jump off a wall" entry point, two impulses.
            if (wallRunning)
            {
                Vector3 rn = wallRunNormal, rd = wallRunDir;
                vel = WallRunMath.Exit(vel, rn, rd, WallRunSettings, dashSpeed);
                lastWallNormal = rn;
                hasLastWall = true;
                jumpPressedAt = -99f;
                if (look != null) look.AddRollKick(Mathf.Sign(WallSide(rn)) * -wallRunExitRollKick, 0.28f);
                EndWallRun(WallRunEnd.Jumped);
                if (OnWallJumped != null) OnWallJumped();
                return true;
            }

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

        // ---------------------------------------------------------------- wall run

        /// <summary>The motor's wall-run fields, packed for <see cref="WallRunMath"/>. Built fresh each
        /// use: it is 17 floats on the stack, and the alternative is a cache that silently stops
        /// reflecting an Inspector tweak.</summary>
        public WallRunMath.Params WallRunSettings
        {
            get
            {
                var pr = new WallRunMath.Params();
                pr.gravity = gravity;
                pr.minEntrySpeed = wallRunMinEntrySpeed;
                pr.maxEntryFallSpeed = wallRunMaxEntryFallSpeed;
                pr.maxApproachCos = wallRunMaxApproachCos;
                pr.minLookAlongCos = wallRunMinLookAlongCos;
                pr.maxDuration = wallRunMaxDuration;
                pr.gravityStartScale = wallRunGravityStartScale;
                pr.gravityEndScale = wallRunGravityEndScale;
                pr.entryUpSpeed = wallRunEntryUpSpeed;
                pr.speedDecay = wallRunSpeedDecay;
                pr.minSustainSpeed = wallRunMinSustainSpeed;
                pr.accel = wallRunAccel;
                pr.topSpeed = groundSpeed * SpeedMultiplier;
                pr.maxSpeed = dashSpeed;
                pr.exitUpSpeed = wallRunExitUpSpeed;
                pr.exitPushSpeed = wallRunExitPushSpeed;
                pr.exitTangentBoost = wallRunExitTangentBoost;
                return pr;
            }
        }

        /// <summary>
        /// Start a wall run. Same contract as <see cref="TryJump"/> / <see cref="TryWallJump"/>: Update
        /// calls it, tests call it directly, and it re-applies every gate itself. There is NO BINDING for
        /// it — Apex-style wall running is entered by arriving correctly, not by pressing a key, and
        /// adding a button would put a second author on the Input System besides
        /// <see cref="InputReader"/>.
        ///
        /// <para>The cheap gates (airborne, budget, cooldown, fall speed, raw speed) are checked BEFORE
        /// any physics query, so the scan runs only on frames where a run could actually begin. See the
        /// performance contract at the top of this file.</para>
        /// </summary>
        public bool TryWallRun()
        {
            if (!CanAct || wallRunning || sliding) return false;
            if (IsGrounded || Time.time - lastGroundedTime <= coyoteTime) return false;
            if (wallRunsUsed >= maxWallRuns) return false;
            if (Time.time < wallRunReadyAt) return false;
            if (vel.y < -Mathf.Abs(wallRunMaxEntryFallSpeed)) return false;
            if (vel.x * vel.x + vel.z * vel.z < wallRunMinEntrySpeed * wallRunMinEntrySpeed) return false;

            Vector3 n;
            if (!FindRunnableWall(out n)) return false;

            Vector3 lookFlat = look != null
                ? new Vector3(look.AimForward.x, 0f, look.AimForward.z)
                : new Vector3(transform.forward.x, 0f, transform.forward.z);

            Vector3 runDir;
            WallRunReject why;
            if (!WallRunMath.CanEnter(vel, n, lookFlat, WallRunSettings, out runDir, out why)) return false;

            vel = WallRunMath.Enter(vel, runDir, WallRunSettings);
            wallRunning = true;
            wallRunElapsed = 0f;
            wallRunNormal = n;
            wallRunDir = runDir;
            wallRunsUsed++;
            // The face you are running is the face you may not re-enter, by exactly the rule that stops a
            // single wall being a free ladder. TryWallJump's run branch reads this back on the way out.
            lastWallNormal = n;
            hasLastWall = true;
            if (OnWallRunStarted != null) OnWallRunStarted();
            return true;
        }

        /// <summary>Stop the current run. Never touches horizontal momentum — leaving a wall keeps what
        /// the wall gave you, which is the same promise the slide makes.</summary>
        public bool EndWallRun(WallRunEnd why)
        {
            if (!wallRunning) return false;
            wallRunning = false;
            wallRunEndReason = why;
            wallRunReadyAt = Time.time + wallRunCooldown;
            if (look != null) look.SetRollBias(0f);
            if (OnWallRunEnded != null) OnWallRunEnded();
            return true;
        }

        /// <summary>Why the last run stopped. Diagnostic; the harness and tests read it.</summary>
        public WallRunEnd LastWallRunEnd => wallRunEndReason;

        /// <summary>
        /// Advance a run by up to <paramref name="dt"/> seconds and move the controller. Returns the time
        /// actually SPENT on the wall, which is less than <paramref name="dt"/> on the frame the duration
        /// cap is reached; <see cref="Update"/> spends the remainder in ordinary air, so the length of a
        /// run does not depend on where the frame boundaries happened to fall.
        /// </summary>
        float AdvanceWallRun(float dt, Vector3 wish)
        {
            Vector3 n = wallRunNormal;
            if (!ProbeWall(ref n)) { EndWallRun(WallRunEnd.LostWall); return 0f; }
            wallRunNormal = n;

            Vector3 runDir;
            if (!WallRunMath.RunDirection(vel, n, out runDir))
            {
                // Velocity went entirely normal to the face — a head-on collision, not a run.
                if (!WallRunMath.RunDirection(wallRunDir, n, out runDir)) { EndWallRun(WallRunEnd.LostWall); return 0f; }
            }
            wallRunDir = runDir;

            bool holdingForward = Vector3.Dot(wish, runDir) > 0.1f;

            Vector3 disp; float used;
            vel = WallRunMath.Advance(vel, runDir, wallRunElapsed, holdingForward, WallRunSettings,
                                      dt, out disp, out used);
            wallRunElapsed += used;

            // Hold contact: pressed into the face on the DISPLACEMENT only. Adding it to vel would let it
            // accumulate into a shove the moment the wall ended.
            disp += -n * (wallRunStickSpeed * used);

            var flags = cc.Move(disp);
            if ((flags & CollisionFlags.Above) != 0 && vel.y > 0f) vel.y = 0f;

            if (look != null) look.SetRollBias(-WallSide(n) * wallRunCameraRoll);

            if (cc.isGrounded) { IsGrounded = true; lastGroundedTime = Time.time; EndWallRun(WallRunEnd.Landed); return used; }

            float along = Mathf.Abs(vel.x * runDir.x + vel.z * runDir.z);
            WallRunEnd why;
            if (WallRunMath.ShouldEnd(wallRunElapsed, along, WallRunSettings, out why)) EndWallRun(why);
            return used;
        }

        /// <summary>+1 when the wall is on the player's RIGHT, -1 when it is on the left. The normal
        /// points AWAY from the face, so a wall on the right has a normal pointing left.</summary>
        float WallSide(Vector3 wallNormal)
        {
            float d = Vector3.Dot(wallNormal, transform.right);
            return d > 0f ? -1f : 1f;
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

        bool wallRunning;
        float wallRunElapsed, wallRunReadyAt = -99f;
        int wallRunsUsed;
        Vector3 wallRunNormal, wallRunDir;
        WallRunEnd wallRunEndReason;

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
                wallRunsUsed = 0;
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

            // ---- WALL RUN ---------------------------------------------------------------------------
            // Entered by ARRIVING correctly, never by a key: there is no wall-run binding, which is also
            // why this needs nothing from InputReader beyond the jump and dash it already reads.
            if (!wallRunning) TryWallRun();
            if (wallRunning)
            {
                bool buffered = Time.time - jumpPressedAt <= jumpBuffer;
                if (canAct && buffered && TryWallJump())
                {
                    // Left the wall with the run's momentum. The rest of the frame is ordinary air, so
                    // fall through with dt intact.
                }
                else if (canAct && dashRequested && Time.time >= dashReadyAt)
                {
                    EndWallRun(WallRunEnd.Cancelled);   // the dash below owns the frame
                }
                else
                {
                    float used = AdvanceWallRun(dt, wish);
                    dt -= used;
                    // A run that ended part-way through this frame gives the remainder back to the air
                    // branch, so the arc does not depend on where frame boundaries landed.
                    if (dt <= 1e-5f) { dashRequested = false; return; }
                }
            }
            // A broken posture drops you off the wall. Being staggered mid-run and carrying on would be
            // the one place in the game where losing a trade costs you nothing.
            if (wallRunning && !canAct) EndWallRun(WallRunEnd.Cancelled);

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
                    if (hasLastWall && WallRunMath.IsSameWall(n, lastWallNormal, sameWallCosineLimit)) continue;
                    float score = -(n.x * pref.x + n.z * pref.z);       // most directly faced wins
                    if (score > best) { best = score; normal = n; found = true; }
                }
            }
            return found;
        }

        /// <summary>
        /// Is there a runnable face beside the player? TWO spherecasts — one to each side of travel —
        /// not the eight-way fan <see cref="FindWall"/> uses, because "is there a wall beside me" is a
        /// different question from "is there any wall I could push off". Cheap on purpose: this is the
        /// only query on the wall-run path that can run on consecutive frames, and it runs only after
        /// <see cref="TryWallRun"/>'s cheap gates have already passed.
        ///
        /// <para>Refuses the wall last pushed off or run (<see cref="sameWallCosineLimit"/>), which is
        /// what stops one face being re-runnable indefinitely.</para>
        /// </summary>
        public bool FindRunnableWall(out Vector3 normal)
        {
            normal = Vector3.zero;
            if (cc == null) return false;

            Vector3 fwd = new Vector3(vel.x, 0f, vel.z);
            if (fwd.sqrMagnitude < 0.01f) fwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
            if (fwd.sqrMagnitude < 0.0001f) return false;
            fwd.Normalize();
            Vector3 side = new Vector3(fwd.z, 0f, -fwd.x);   // right of travel

            Vector3 origin = transform.position + cc.center;
            float r = Mathf.Max(0.05f, cc.radius * 0.9f);
            float best = float.MaxValue;
            bool found = false;

            for (int s = 0; s < 2; s++)
            {
                Vector3 dir = s == 0 ? side : -side;
                int hits = Physics.SphereCastNonAlloc(origin, r, dir, castHits, wallCheckDistance,
                                                      worldMask, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < hits; h++)
                {
                    Vector3 raw = castHits[h].normal;
                    if (Mathf.Abs(raw.y) > 0.4f) continue;              // a floor or a ceiling, not a wall
                    Vector3 n = new Vector3(raw.x, 0f, raw.z);
                    if (n.sqrMagnitude < 0.0001f) continue;
                    n.Normalize();
                    if (hasLastWall && WallRunMath.IsSameWall(n, lastWallNormal, sameWallCosineLimit)) continue;
                    float d = castHits[h].distance;
                    if (d < best) { best = d; normal = n; found = true; }
                }
            }
            return found;
        }

        /// <summary>
        /// Is the face we are running still there, and still the same face? ONE spherecast straight into
        /// it. Updates <paramref name="normal"/> so a gently angled or curving wall steers the run.
        /// A turn sharper than 60 deg is a different wall and ends the run — the new face is then a fresh
        /// entry with its own speed and intent test, which is what makes an outside corner read as two
        /// runs rather than one impossible one.
        /// </summary>
        bool ProbeWall(ref Vector3 normal)
        {
            if (cc == null) return false;
            Vector3 into = -normal;
            Vector3 origin = transform.position + cc.center;
            float r = Mathf.Max(0.05f, cc.radius * 0.9f);

            int hits = Physics.SphereCastNonAlloc(origin, r, into, castHits, wallCheckDistance,
                                                  worldMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 found = Vector3.zero;
            bool ok = false;
            for (int h = 0; h < hits; h++)
            {
                Vector3 raw = castHits[h].normal;
                if (Mathf.Abs(raw.y) > 0.4f) continue;
                Vector3 n = new Vector3(raw.x, 0f, raw.z);
                if (n.sqrMagnitude < 0.0001f) continue;
                n.Normalize();
                if (Vector3.Dot(n, normal) < 0.5f) continue;           // a different face; end the run
                float d = castHits[h].distance;
                if (d < best) { best = d; found = n; ok = true; }
            }
            if (ok) normal = found;
            return ok;
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
            EndWallRun(WallRunEnd.Cancelled);
            cc.enabled = false;
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            vel = Vector3.zero;
            dashUntil = 0f;
            wallJumpsUsed = 0;
            wallRunsUsed = 0;
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
            EndWallRun(WallRunEnd.Cancelled);
            vel.y = upSpeed;
            IsGrounded = false;
            lastGroundedTime = -99f;
        }
    }

    /// <summary>Why <see cref="WallRunMath.CanEnter"/> said no. An enum rather than a string because
    /// this is evaluated on the movement path and the movement path allocates nothing.</summary>
    public enum WallRunReject
    {
        None = 0,
        TooSlow,          // not carrying enough speed ALONG the wall
        FallingTooFast,   // a plummet does not catch a wall
        WrongApproach,    // running INTO the face rather than along it
        LookingAway,      // no intent: the player is not looking down the run
        NoTangent         // degenerate - velocity is entirely into or out of the wall
    }

    /// <summary>Why a wall run stopped.</summary>
    public enum WallRunEnd
    {
        None = 0,
        Expired,      // the loan ran out (wallRunMaxDuration)
        Decayed,      // bled below wallRunMinSustainSpeed
        LostWall,     // the face went away, or turned a corner too sharply
        Landed,
        Jumped,
        Cancelled     // dash, teleport, stagger
    }

    /// <summary>
    /// Every decision and every integral a wall run makes, as PURE STATIC FUNCTIONS on plain structs.
    ///
    /// <para><b>Why this is not just methods on the motor.</b> Two callers need identical answers.
    /// <see cref="FirstPersonMotor"/> runs it a frame at a time against a <c>CharacterController</c>, and
    /// <c>LevelArcAnalyzer</c> runs it thousands of times a second against a list of boxes so a level
    /// designer can ask "is that run possible?" without a playtest. A second implementation of the same
    /// arithmetic is a second implementation that will drift, and a level built against a drifted model
    /// is a level that is wrong in the only place it matters. So both call THIS.</para>
    ///
    /// <para><b>Frame-rate independence is built in, not tested in afterwards.</b>
    /// <see cref="Advance"/> sub-steps at <see cref="SubStep"/> (2 ms) internally and returns the exact
    /// displacement it integrated, and speed decay is <c>exp(-k dt)</c> rather than <c>(1 - k dt)</c>,
    /// which is the same number at every timestep instead of merely close at small ones. It also refuses
    /// to consume more time than the run has left, so a 100 ms frame cannot overshoot the duration cap.
    /// The slide shipped once at 4.0 m on a fast machine and 1.8 m on a slow one; that is not
    /// reproducible here by construction.</para>
    /// </summary>
    public static class WallRunMath
    {
        /// <summary>Integration sub-step. Matches LevelArcAnalyzer's, deliberately.</summary>
        public const float SubStep = 0.002f;

        /// <summary>
        /// The tuning of a wall run, detached from the MonoBehaviour so the analyser and the tests can
        /// build one without a scene. <see cref="FirstPersonMotor.WallRunSettings"/> fills it from the
        /// shipped prefab's fields; nothing here carries a default, on purpose - a default in two places
        /// is a lie waiting to happen.
        /// </summary>
        public struct Params
        {
            public float gravity;              // negative, the motor's own
            public float minEntrySpeed;        // m/s ALONG the wall
            public float maxEntryFallSpeed;    // m/s, positive magnitude
            public float maxApproachCos;       // |dot(velDirFlat, wallNormal)| ceiling
            public float minLookAlongCos;      // dot(lookFlat, runDir) floor
            public float maxDuration;          // s
            public float gravityStartScale;    // x gravity at t=0
            public float gravityEndScale;      // x gravity at t=maxDuration
            public float entryUpSpeed;         // vy floor applied once, on entry
            public float speedDecay;           // 1/s, exponential
            public float minSustainSpeed;      // m/s, below which the run drops you
            public float accel;                // m/s^2 top-up while holding forward
            public float topSpeed;             // ceiling the top-up accelerates toward (groundSpeed)
            public float maxSpeed;             // hard ceiling on tangential speed (dashSpeed)
            public float exitUpSpeed;
            public float exitPushSpeed;        // along the wall normal
            public float exitTangentBoost;     // along the run direction
        }

        /// <summary>
        /// The direction the run goes: the horizontal velocity with the into-the-wall component removed.
        /// Recomputed every frame so a gently curving or angled face steers the run instead of ending it.
        /// Returns false when velocity is entirely normal to the face, which is a collision, not a run.
        /// </summary>
        public static bool RunDirection(Vector3 vel, Vector3 wallNormal, out Vector3 runDir)
        {
            Vector3 n = new Vector3(wallNormal.x, 0f, wallNormal.z);
            float nl = n.magnitude;
            if (nl < 1e-4f) { runDir = Vector3.zero; return false; }
            n /= nl;

            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            Vector3 t = flat - n * Vector3.Dot(flat, n);
            if (t.sqrMagnitude < 1e-6f) { runDir = Vector3.zero; return false; }
            runDir = t.normalized;
            return true;
        }

        /// <summary>
        /// <b>Entry: speed and intent, never contact.</b> A wall run you fall into by accident is noise,
        /// so all four of these must hold and each rejects a specific kind of accident:
        /// <list type="number">
        ///   <item>you are carrying <c>minEntrySpeed</c> ALONG the face - not total speed, tangential
        ///   speed, so slamming into a wall at 20 m/s head-on is still a wall, not a run;</item>
        ///   <item>you are not falling faster than <c>maxEntryFallSpeed</c> - a plummet does not catch;</item>
        ///   <item>your travel is within <c>maxApproachCos</c> of the wall plane, i.e. you are moving
        ///   along it rather than at it;</item>
        ///   <item>you are LOOKING roughly down the run. This is the intent term, and it is what makes
        ///   the mechanic feel chosen rather than sprung on you.</item>
        /// </list>
        /// Pure: no motor state, no scene. The caller has already decided a runnable face is in reach.
        /// </summary>
        public static bool CanEnter(Vector3 vel, Vector3 wallNormal, Vector3 lookFlat, Params pr,
                                    out Vector3 runDir, out WallRunReject why)
        {
            runDir = Vector3.zero;
            why = WallRunReject.None;

            if (vel.y < -Mathf.Abs(pr.maxEntryFallSpeed)) { why = WallRunReject.FallingTooFast; return false; }

            Vector3 n = new Vector3(wallNormal.x, 0f, wallNormal.z);
            if (n.sqrMagnitude < 1e-6f) { why = WallRunReject.NoTangent; return false; }
            n.Normalize();

            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            float flatSpeed = flat.magnitude;
            if (flatSpeed < 1e-4f) { why = WallRunReject.TooSlow; return false; }

            // Approach angle, measured on the DIRECTION of travel rather than on the raw velocity, so a
            // fast player and a slow one are held to the same geometry.
            float approach = Mathf.Abs(Vector3.Dot(flat / flatSpeed, n));
            if (approach > pr.maxApproachCos) { why = WallRunReject.WrongApproach; return false; }

            if (!RunDirection(vel, n, out runDir)) { why = WallRunReject.NoTangent; return false; }

            float along = Vector3.Dot(flat, runDir);
            if (along < pr.minEntrySpeed) { why = WallRunReject.TooSlow; return false; }

            Vector3 lf = new Vector3(lookFlat.x, 0f, lookFlat.z);
            if (lf.sqrMagnitude > 1e-6f)
            {
                lf.Normalize();
                if (Vector3.Dot(lf, runDir) < pr.minLookAlongCos) { why = WallRunReject.LookingAway; return false; }
            }
            return true;
        }

        /// <summary>
        /// The velocity a run starts at. The into-the-wall component is dropped (the collider is about to
        /// cancel it anyway, and keeping it would make the first frame's decay lie), and vertical speed is
        /// floored at <c>entryUpSpeed</c> - the CATCH. That small upward pop is the whole "you are
        /// borrowing height" read: you gain about a metre and then start paying it back. It is a FLOOR,
        /// never an add, so arriving fast and already rising is not rewarded twice.
        /// </summary>
        public static Vector3 Enter(Vector3 vel, Vector3 runDir, Params pr)
        {
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            float along = Vector3.Dot(flat, runDir);
            if (along > pr.maxSpeed) along = pr.maxSpeed;
            return new Vector3(runDir.x * along, Mathf.Max(vel.y, pr.entryUpSpeed), runDir.z * along);
        }

        /// <summary>
        /// Gravity multiplier at <paramref name="elapsed"/> seconds into a run. Ramps from
        /// <c>gravityStartScale</c> to <c>gravityEndScale</c> on t squared, so the first half of a run is
        /// nearly free and the last quarter visibly drops you. A linear ramp read as one long mushy sink;
        /// the square is what makes the end of the loan legible WHILE YOU ARE STILL ON THE WALL, which is
        /// the difference between "it ended" and "it is taking my height back".
        /// </summary>
        public static float GravityScale(float elapsed, Params pr)
        {
            if (pr.maxDuration <= 0f) return pr.gravityEndScale;
            float t = Mathf.Clamp01(elapsed / pr.maxDuration);
            return Mathf.Lerp(pr.gravityStartScale, pr.gravityEndScale, t * t);
        }

        /// <summary>
        /// Integrate a wall run over <paramref name="dt"/> seconds and report both the end velocity and
        /// the exact displacement covered.
        ///
        /// <para>Sub-stepped at <see cref="SubStep"/>, so a 5 ms frame and a 50 ms frame produce the same
        /// arc rather than merely a similar one, and <paramref name="consumed"/> is clamped to the time
        /// the run has left so a long frame cannot run past the duration cap. The caller spends any
        /// remainder in ordinary air.</para>
        /// </summary>
        public static Vector3 Advance(Vector3 vel, Vector3 runDir, float elapsed, bool holdingForward,
                                      Params pr, float dt, out Vector3 displacement, out float consumed)
        {
            displacement = Vector3.zero;
            consumed = 0f;
            if (dt <= 0f) return vel;

            float left = Mathf.Max(0f, pr.maxDuration - elapsed);
            float budget = Mathf.Min(dt, left);
            if (budget <= 0f) return vel;

            int steps = Mathf.Max(1, Mathf.CeilToInt(budget / SubStep));
            float h = budget / steps;

            for (int i = 0; i < steps; i++)
            {
                float t = elapsed + i * h;

                vel.y += pr.gravity * GravityScale(t, pr) * h;

                // Tangential speed: exponential bleed, then an optional top-up along the run. Exp rather
                // than (1 - k h) so the answer does not depend on how the frame was sliced.
                float along = vel.x * runDir.x + vel.z * runDir.z;
                along *= Mathf.Exp(-pr.speedDecay * h);
                if (holdingForward && along < pr.topSpeed)
                    along = Mathf.Min(pr.topSpeed, along + pr.accel * h);
                if (along > pr.maxSpeed) along = pr.maxSpeed;

                vel.x = runDir.x * along;
                vel.z = runDir.z * along;

                displacement += vel * h;
            }

            consumed = budget;
            return vel;
        }

        /// <summary>
        /// Are these two wall normals THE SAME WALL for re-entry purposes? The rule
        /// <c>sameWallCosineLimit</c> encodes, lifted out of the two scans so it can be unit-tested and
        /// so a wall run and a wall jump can never disagree about what "the same face" means. Without it
        /// one face is a free ladder; with it a facing pair alternates and a corner chains.
        /// </summary>
        public static bool IsSameWall(Vector3 a, Vector3 b, float cosLimit)
        {
            return Vector3.Dot(a, b) > cosLimit;
        }

        /// <summary>Has the loan run out? Speed and time only - losing the wall is the caller's business.</summary>
        public static bool ShouldEnd(float elapsed, float tangentialSpeed, Params pr, out WallRunEnd why)
        {
            if (elapsed >= pr.maxDuration) { why = WallRunEnd.Expired; return true; }
            if (tangentialSpeed < pr.minSustainSpeed) { why = WallRunEnd.Decayed; return true; }
            why = WallRunEnd.None;
            return false;
        }

        /// <summary>
        /// The exit impulse. COMPOSED with the ordinary wall jump rather than replacing it: same shape -
        /// keep what runs ALONG the face, add out along the normal, set a fixed rise - but a run has
        /// EARNED something, so it also gets <c>exitTangentBoost</c> forward and trades some of the
        /// outward shove for it. A wall jump throws you off the wall; a wall-run exit throws you DOWN THE
        /// LINE you were already committed to, which is the whole reason to have run it.
        /// <paramref name="speedClamp"/> is the motor's <c>dashSpeed</c>, the same ceiling
        /// <c>TryWallJump</c> applies, so no route can launder a run into unbounded speed.
        /// </summary>
        public static Vector3 Exit(Vector3 vel, Vector3 wallNormal, Vector3 runDir, Params pr, float speedClamp)
        {
            Vector3 n = new Vector3(wallNormal.x, 0f, wallNormal.z);
            if (n.sqrMagnitude > 1e-6f) n.Normalize(); else n = Vector3.zero;

            float along = vel.x * runDir.x + vel.z * runDir.z;
            if (along < 0f) along = 0f;
            along += pr.exitTangentBoost;

            float hx = runDir.x * along + n.x * pr.exitPushSpeed;
            float hz = runDir.z * along + n.z * pr.exitPushSpeed;

            float sp = Mathf.Sqrt(hx * hx + hz * hz);
            if (speedClamp > 0f && sp > speedClamp) { float k = speedClamp / sp; hx *= k; hz *= k; }

            return new Vector3(hx, pr.exitUpSpeed, hz);
        }
    }
}
