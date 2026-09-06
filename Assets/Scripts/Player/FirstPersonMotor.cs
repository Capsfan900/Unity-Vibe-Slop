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

        [Header("Wall run - feel")]
        [Tooltip("Speed the wall accelerates you TOWARD while holding forward, m/s. Above a sprint on " +
                 "purpose (Titanfall: the wall is faster than the floor) - 1.25x groundSpeed. This is " +
                 "the payoff that makes a wall worth taking over the ground beside it.")]
        public float wallRunTopSpeed = 13.75f;
        [Tooltip("Seconds a run survives losing contact (a seam between two wall boxes, a slight bulge) " +
                 "before it ends. Without it a run glitched off at every joint in a greybox.")]
        public float wallRunLostGrace = 0.15f;
        [Tooltip("Seconds after a run ends on its own (expired, decayed, exhausted, lost) during which a " +
                 "jump press is still the RUN EXIT - thrown down the line - rather than nothing. The " +
                 "wall-run version of coyote time. Titanfall's players never trusted the wall until it " +
                 "rewarded an early or late press.")]
        public float wallRunExitGrace = 0.15f;

        [Header("Momentum")]
        [Tooltip("Horizontal speed in the air above which drag applies to the EXCESS. 1.6x groundSpeed. " +
                 "Below it the air keeps every m/s you bring; above it the surplus bleeds on " +
                 "exp(-airDrag t), so a dash or a slide-jump is a burst that settles, not a new cruise.")]
        public float airSoftCap = 17.6f;
        [Tooltip("Per-second decay of the excess over airSoftCap. 3 = the surplus halves every 0.23 s.")]
        public float airDrag = 3f;
        [Tooltip("Per-second decay of ground speed in excess of groundSpeed (dash landings, slide " +
                 "exits). Exponential, so it is the same at every framerate. 4 = halves every 0.17 s.")]
        public float groundOverspeedDecay = 4f;
        [Tooltip("Absolute ceiling on horizontal speed outside a dash. 2.5x groundSpeed. Nothing legal " +
                 "reaches it; it exists so nothing illegal can either.")]
        public float maxHorizontalSpeed = 27.5f;
        [Tooltip("A slide started within this many seconds of the previous slide ENDING is a chain. " +
                 "Chained slides earn less boost each (slideChainFalloff), which is what stops " +
                 "slide-jump-slide-jump being a free constant 16 m/s across the whole map.")]
        public float slideChainWindow = 1.2f;
        [Tooltip("Boost multiplier per consecutive chained slide: 5, 3, 1.8, 1.1 ... A short break on " +
                 "the ground resets it, so the tech still exists, it just costs rhythm instead of nothing.")]
        public float slideChainFalloff = 0.6f;

        [Header("Weight & air control")]
        [Tooltip("Gravity multiplier while FALLING (airborne, vel.y < 0, not on a wall). Rising is untouched so " +
                 "jumpHeight still means what it says; the way DOWN is heavier, which is where weight is read. " +
                 "1 = symmetric (the old feel: floaty). 1.5 = a held flat jump lands at ~14.7 m/s instead of 12.")]
        public float fallGravityMultiplier = 1.5f;
        [Tooltip("Per-second exponential decay of airborne speed in excess of groundSpeed. 0 = the air keeps " +
                 "every m/s you bring for as long as you fly (a cruise). 0.8 = a 17.6 m/s wall exit is 15.4 " +
                 "half a second later and 13.9 after a full second: a burst that settles, never a glide.")]
        public float airCarryDecay = 0.8f;
        [Tooltip("Degrees per second the airborne velocity TURNS toward the stick, speed preserved. The surf " +
                 "feel: you steer where you fly, you do not pump speed. Acts only while the stick is within " +
                 "90 deg of travel; braking (stick back) is AirAccelerate's job and stays a bleed.")]
        public float airSteerDegPerSec = 120f;
        [Tooltip("Landing speed (m/s, downward) below which a landing costs no horizontal speed. A held flat " +
                 "jump lands at ~14.7 with fallGravityMultiplier 1.5, so 16 leaves bunny-hopping free.")]
        public float landingSoftSpeed = 16f;
        [Tooltip("Landing speed at which the full landingSpeedLoss is taken. 26 = a 7.5 m drop.")]
        public float landingHardSpeed = 26f;
        [Tooltip("Fraction of horizontal speed lost on a landing at landingHardSpeed or above, scaling from 0 " +
                 "at landingSoftSpeed. Applied BEFORE this frame's slide press, so a landing-slide still " +
                 "recovers the run: you can shoot off a wall, you cannot land a 7 m drop at full tilt.")]
        public float landingSpeedLoss = 0.35f;
        [Tooltip("How far the controller is pressed DOWN per frame while grounded and not rising, as displacement " +
                 "only (the sweep stops at the floor). Must exceed the skin width: a -2 m/s pin moves 0.004 m at " +
                 "500 fps, inside the skin, so isGrounded flickered off and a slide died at coyote time on " +
                 "any fast machine. Zero disables (the old behaviour).")]
        public float groundSnapDistance = 0.12f;

        [Header("Balloon launch — the FLOAT")]
        [Tooltip("Seconds after a balloon launch during which gravity is scaled by launchGravityScale and " +
                 "air steer by launchSteerBoost: the pop hangs and can be aimed at the next orb. From play " +
                 "(2026-09-05): a 14 m/s punt was too fast to steer; this is the 'slower and controlled'.")]
        public float launchFloatSeconds = 0.45f;
        [Tooltip("Gravity multiplier inside the float window. 0.55 reads as a lift; 0 would read as a glitch.")]
        [Range(0.2f, 1f)] public float launchGravityScale = 0.55f;
        [Tooltip("Air steer rate multiplier inside the float window.")]
        public float launchSteerBoost = 1.6f;
        [Tooltip("Horizontal speed a pop trims the player to, m/s. A 22 m/s dash carried through an orb overshoots the next one by a storey; the re-armed dash is what closes the gap, so the carry only needs to be steerable.")]
        public float launchCarryCap = 9f;

        [Header("Traversal - water, balloons, grapple burst (2026-09-04 pivot)")]
        [Tooltip("Skating floor as a multiple of groundSpeed while on water. 1.35 x 11 = 14.85 m/s: faster " +
                 "than a sprint, under a dash, so water is the fastest FLOOR without out-running the air kit.")]
        public float waterSpeedScale = 1.35f;
        [Tooltip("m/s^2 the skated velocity turns and lifts at on water. A third of groundAccel: turning " +
                 "on water is a skate, not a snap. Also how fast a walker is lifted to the floor speed.")]
        public float waterAccel = 30f;
        [Tooltip("Seconds a water touch keeps counting after the trigger last refreshed it. Covers the gap " +
                 "between physics steps; short enough that stepping off is immediate.")]
        public float waterGrace = 0.15f;
        [Tooltip("Seconds after a grapple pull ARRIVES (and control is back) in which a dash press is a free " +
                 "BURST: no cooldown, no air charge, no stamina.")]
        public float pullBurstWindow = 0.30f;
        [Tooltip("Burst speed as a multiple of dashSpeed. 1.25 x 22 = 27.5, exactly maxHorizontalSpeed.")]
        public float pullBurstMultiplier = 1.25f;
        [Tooltip("Longest the burst will wait for control to return after an arrival (the deathblow " +
                 "cinematic holds CanMove false). Past this the window is forfeited rather than fired stale.")]
        public float pullBurstHold = 3f;

        [Header("Perfect timing — a move pressed on its moment gives stamina BACK")]
        // Three PERFECTs, each a short window around a physical moment the player can learn to feel
        // (PerfectMath explains the sizes: Sekiro's 0.20 s deflect and Celeste's 0.08 s coyote bracket
        // them). A miss is simply the ordinary move -- nothing taken, nothing said.
        [Tooltip("Seconds before the wall-run loan runs out in which a wall jump is PERFECT (the wall is " +
                 "about to give up). Also the seconds after a natural let-go in which the exit-grace jump " +
                 "is perfect. The exit grace itself is 0.15 s.")]
        public float perfectWallJumpWindow = 0.14f;
        [Tooltip("Stamina given back for a perfect wall jump. The run cost 12 to enter and 22/s to hold; " +
                 "20 is the entry plus a third of a second of it, so a perfect chain sustains itself.")]
        public float perfectWallJumpRefund = 20f;
        [Tooltip("A jump pressed sooner than this after a dash does NOT count: the dash has to be felt " +
                 "before the jump is thrown, or mashing both keys together would be the perfect.")]
        public float perfectDashJumpMinDelay = 0.04f;
        [Tooltip("Width of the perfect dash-jump window after the minimum delay. The dash lasts 0.16 s; " +
                 "0.04 + 0.12 reaches its end, so the perfect is 'jump out of the dash', not 'jump before it'.")]
        public float perfectDashJumpWindow = 0.12f;
        [Tooltip("Stamina given back for a perfect dash-jump: the dash's own cost (30), so a perfect chain " +
                 "of dash-jumps is free and an ordinary one costs what it always did.")]
        public float perfectDashJumpRefund = 30f;
        [Tooltip("First seconds of the 0.30 s grapple burst window in which the burst is PERFECT -- timed " +
                 "to the landing rather than fished for.")]
        public float perfectBurstWindow = 0.12f;
        [Tooltip("Stamina granted for a perfect burst. The burst is already free; this is the reward for " +
                 "landing the kill and leaving it on the beat.")]
        public float perfectBurstBonus = 30f;

        [Header("Forgiveness (MOVEMENT-PRINCIPLES rule 4: fudge toward intent)")]
        [Tooltip("A rising jump whose head clips the CORNER of a ledge is nudged sideways up to this many " +
                 "metres so the jump continues instead of stopping dead. Bounded: it honours intent, it never adds reach.")]
        public float cornerCorrectionMetres = 0.18f;
        [Tooltip("A falling player whose feet pass within this many metres UNDER a ledge top they are moving " +
                 "toward is lifted onto it. No velocity is added; never on a wall being fallen past, never while rising.")]
        public float ledgeCatchMetres = 0.22f;
        [Tooltip("How fast the near-miss lift moves the body, m/s. Bounded per frame so it is the same at any frame rate.")]
        public float ledgeCatchLiftSpeed = 6f;
        [Tooltip("Minimum horizontal speed for a near-miss catch: standing still and dropping past a ledge is a drop.")]
        public float ledgeCatchMinSpeed = 1.5f;

        /// <summary>Fraction of horizontal speed the last landing cost (0 = none). For FX and tests.</summary>
        public float LastLandingLoss { get; private set; }

        public bool CanMove = true;
        /// <summary>Temporary speed scalar (item effects). 1 = normal.</summary>
        public float SpeedMultiplier = 1f;

        public Vector3 Velocity => vel;
        public bool IsGrounded { get; private set; }
        public bool IsDashing => now < dashUntil;
        public bool IsSliding => sliding;
        /// <summary>Wall jumps spent since the last landing. Resets on ground contact.</summary>
        public int WallJumpsUsed => wallJumpsUsed;
        public bool IsWallRunning => wallRunning;
        /// <summary>Wall runs spent since the last landing. Resets on ground contact.</summary>
        public int WallRunsUsed => wallRunsUsed;
        /// <summary>The single air dash has been spent since the last landing.</summary>
        public bool AirDashUsed => airDashUsed;
        /// <summary>Consecutive chained slides so far (0 = the first). Drives the boost falloff.</summary>
        public int SlideChain => slideChain;
        /// <summary>Would a dash press fire THIS frame: gate, cooldown, air charge and stamina all
        /// together. The HUD pip reads this; it is the only honest answer to "can I dash".</summary>
        public bool CanDashNow => CanAct && now >= dashReadyAt && (IsGrounded || !airDashUsed)
                                  && (stamina == null || stamina.CanAfford(stamina.dashCost));
        /// <summary>Would a wall run be allowed to start if a wall were beside you now (budget, cooldown,
        /// stamina - not geometry).</summary>
        public bool CanWallRunNow => CanAct && wallRunsUsed < maxWallRuns && now >= wallRunReadyAt
                                     && (IsWallSurging || stamina == null || stamina.CanAfford(stamina.wallRunEntryCost));
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
        /// <summary>Standing in (or hopping just above) a <see cref="WaterVolume"/> this frame.</summary>
        public bool InWater => now < waterUntil;
        /// <summary>The water's conveyor velocity, or zero.</summary>
        public Vector3 WaterFlow => InWater && waterVolume != null ? waterVolume.Flow : Vector3.zero;
        /// <summary>The speed water skates you up to: groundSpeed x waterSpeedScale (x any item multiplier).</summary>
        public float WaterFloorSpeed => groundSpeed * SpeedMultiplier * waterSpeedScale;
        /// <summary>A dash press right now would be a free grapple-exit burst.</summary>
        public bool IsBurstOpen => TraversalMath.BurstOpen(now, pullBurstUntil);
        /// <summary>The dash in flight (or the last one) was a grapple-exit burst. PlayerFeedback reads it inside OnDashed.</summary>
        public bool LastDashWasBurst { get; private set; }
        /// <summary>Collider height the player stands at, sampled from the prefab at Awake.</summary>
        public float StandHeight => standHeight;

        public event Action OnLanded, OnJumped, OnDashed;
        /// <summary>Raised by <see cref="Launch(float)"/> - a balloon pop. PlayerFeedback listens.</summary>
        public event Action OnLaunched;
        /// <summary>Raised when a slide starts / ends, and when a wall jump fires. PlayerFeedback listens.</summary>
        public event Action OnSlideStarted, OnSlideEnded, OnWallJumped;
        /// <summary>A PERFECT fired: which move, and how much stamina actually came back (already clamped
        /// to the bar). PlayerFeedback and the HUD listen. Never raised for an ordinary move.</summary>
        public event Action<PerfectKind, float> OnPerfect;
        /// <summary>The last perfect's kind and motor-clock time. <c>None</c> / -99 until one fires.</summary>
        public PerfectKind LastPerfectKind { get; private set; }
        public float LastPerfectTime { get; private set; }
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
            jumpPressedAt = now;
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
            if (now < slideReadyAt) return false;
            if (!IsGrounded && now - lastGroundedTime > coyoteTime) return false;

            float sx = vel.x, sz = vel.z;
            float sp = Mathf.Sqrt(sx * sx + sz * sz);
            if (sp < slideMinEntrySpeed) return false;

            // THE SLIDE IS ON THE BUDGET (2026-09-06, the user). Last of the four gates on purpose:
            // TrySpend has a side effect, so it may only run once the slide is certain to start —
            // a refusal from a standstill, on cooldown, in the air or under the entry speed must
            // cost nothing. A refusal here names Slide, so the HUD flashes and says which move was
            // denied instead of the press reading as a dropped input.
            if (stamina != null && !stamina.TrySpend(stamina.slideCost, StaminaAction.Slide)) return false;

            // The boost DIMINISHES with the speed you already carry (full at a sprint, nothing at
            // slideMaxSpeed) and with every slide chained inside slideChainWindow. A slide entered at a
            // run still buys its full 5 m/s; a fourth back-to-back slide-hop buys ~1. That is the whole
            // momentum contract: speed is EARNED once, then preserved, never minted on every press.
            float run = groundSpeed * SpeedMultiplier;
            float fade = Mathf.Clamp01((slideMaxSpeed - sp) / Mathf.Max(0.01f, slideMaxSpeed - run));
            slideChain = now - lastSlideEndedAt <= slideChainWindow ? slideChain + 1 : 0;
            float boost = slideBoost * fade * Mathf.Pow(Mathf.Clamp01(slideChainFalloff), slideChain);
            float entry = Mathf.Min(Mathf.Max(sp, run) + boost, slideMaxSpeed);
            if (entry < sp) entry = sp;    // never slower for having pressed the button
            float k = entry / sp;
            vel.x = sx * k;
            vel.z = sz * k;

            sliding = true;
            slideEndsAt = now + slideMaxDuration;
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
            slideReadyAt = now + slideCooldown;
            lastSlideEndedAt = now;
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
                // PERFECT: left the wall on its last breath (the loan about to run out). Judged before
                // EndWallRun, which resets the run's clock.
                bool perfect = PerfectMath.WallJumpFromRunIsPerfect(wallRunElapsed, wallRunMaxDuration, perfectWallJumpWindow);
                vel = WallRunMath.Exit(vel, rn, rd, WallRunSettings, dashSpeed);
                lastWallNormal = rn;
                hasLastWall = true;
                jumpPressedAt = -99f;
                if (look != null) look.AddRollKick(Mathf.Sign(WallSide(rn)) * -wallRunExitRollKick, 0.28f);
                EndWallRun(WallRunEnd.Jumped);
                if (OnWallJumped != null) OnWallJumped();
                if (perfect) Perfect(PerfectKind.WallJump, perfectWallJumpRefund);
                return true;
            }

            if (IsGrounded || now - lastGroundedTime <= coyoteTime) return false;

            // EXIT GRACE. A run that just ended by itself (expired, decayed, exhausted, lost the face)
            // still answers a press for wallRunExitGrace seconds with the RUN EXIT, thrown down the
            // line. Without this, a jump pressed a few frames after the loan ran out was a whiff, and the
            // player learned to bail early instead of riding the wall - the opposite of the design.
            if (hasLastWall && now - wallRunLeftAt <= wallRunExitGrace)
            {
                Vector3 rn = wallRunLastNormal.sqrMagnitude > 0.5f ? wallRunLastNormal : lastWallNormal;
                Vector3 rd = wallRunLastDir;
                if (rd.sqrMagnitude > 0.5f)
                {
                    // PERFECT: the wall let go and the press came within the window of it. Reaching this
                    // branch at all means the run was ridden to its end rather than bailed early.
                    bool perfect = PerfectMath.GraceJumpIsPerfect(now - wallRunLeftAt, perfectWallJumpWindow);
                    vel = WallRunMath.Exit(vel, rn, rd, WallRunSettings, dashSpeed);
                    wallRunLeftAt = -99f;
                    jumpPressedAt = -99f;
                    if (look != null) look.AddRollKick(Mathf.Sign(WallSide(rn)) * -wallRunExitRollKick, 0.28f);
                    if (OnWallJumped != null) OnWallJumped();
                    if (perfect) Perfect(PerfectKind.WallJump, perfectWallJumpRefund);
                    return true;
                }
            }

            if (wallJumpsUsed >= maxWallJumps) return false;

            Vector3 n;
            if (!FindWall(out n)) return false;
            if (stamina != null && !stamina.TrySpend(stamina.wallJumpCost, StaminaAction.WallJump)) return false;

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
                pr.minEntrySpeed = IsWallSurging ? 0f : wallRunMinEntrySpeed;   // surge: any touch attaches
                pr.maxEntryFallSpeed = wallRunMaxEntryFallSpeed;
                pr.maxApproachCos = wallRunMaxApproachCos;
                pr.minLookAlongCos = wallRunMinLookAlongCos;
                pr.maxDuration = wallRunMaxDuration;
                pr.gravityStartScale = wallRunGravityStartScale;
                pr.gravityEndScale = wallRunGravityEndScale;
                pr.entryUpSpeed = wallRunEntryUpSpeed;
                pr.speedDecay = wallRunSpeedDecay;
                pr.minSustainSpeed = wallRunMinSustainSpeed;
                pr.accel = wallRunAccel * WallSurgeScale;
                pr.topSpeed = wallRunTopSpeed * SpeedMultiplier * WallSurgeScale;
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
            if (wallRunning) return false;
            if (!CanAct) return WallRunRefused(WallRunReject.CannotAct, false, Vector3.zero);
            // Airborne is the whole gate - no coyote exclusion. Running off a ledge along a wall and
            // attaching on the very next frame is the move; waiting 0.12 s for coyote to lapse made the
            // same approach work or not depending on where the ledge was, which read as "sometimes".
            if (IsGrounded) return WallRunRefused(WallRunReject.Grounded, false, Vector3.zero);
            // ...but "airborne" must mean it. A CharacterController drops isGrounded for a frame when the
            // capsule resizes for a slide or when a fast frame skims a seam (ENGINEERING-LOG), and on that
            // frame a corridor wall beside you would attach, stand you up, charge stamina and Land. So
            // inside coyote time you must be RISING (a jump) to count as airborne; past it, a fall counts.
            if (vel.y <= 0f && now - lastGroundedTime <= coyoteTime) return WallRunRefused(WallRunReject.Grounded, false, Vector3.zero);
            if (wallRunsUsed >= maxWallRuns) return WallRunRefused(WallRunReject.OverBudget, false, Vector3.zero);
            if (now < wallRunReadyAt) return WallRunRefused(WallRunReject.Cooldown, false, Vector3.zero);
            if (vel.y < -Mathf.Abs(wallRunMaxEntryFallSpeed)) return WallRunRefused(WallRunReject.FallingTooFast, false, Vector3.zero);
            if (!IsWallSurging && vel.x * vel.x + vel.z * vel.z < wallRunMinEntrySpeed * wallRunMinEntrySpeed) return WallRunRefused(WallRunReject.TooSlow, false, Vector3.zero);

            Vector3 n;
            if (!FindRunnableWall(out n)) return WallRunRefused(WallRunReject.NoWallInReach, false, Vector3.zero);


            Vector3 lookFlat = look != null
                ? new Vector3(look.AimForward.x, 0f, look.AimForward.z)
                : new Vector3(transform.forward.x, 0f, transform.forward.z);

            Vector3 runDir;
            WallRunReject why;
            if (!WallRunMath.CanEnter(vel, n, lookFlat, WallRunSettings, out runDir, out why)) return WallRunRefused(why, true, n);
            // Last, so a refusal means "you would have run, and could not afford it" - the HUD flashes
            // the bar and names the ability.
            // A slide that left the ground (still true through coyote) yields to the wall — the
            // slide-jump-into-wall-run is the chain the level spans are built on — unless a ceiling keeps
            // us crouched. Checked here, before the spend, so a refusal never costs stamina.
            if (sliding && CeilingBlocked()) return WallRunRefused(WallRunReject.Sliding, true, n);
            if (!IsWallSurging && stamina != null && !stamina.TrySpend(stamina.wallRunEntryCost, StaminaAction.WallRun))
            {
                // Throttle: this runs every airborne frame beside a runnable wall, and a refusal per frame
                // is a solid red bar. One named refusal, then the cooldown before the next.
                wallRunReadyAt = now + wallRunCooldown;
                return WallRunRefused(WallRunReject.NoStamina, true, n);
            }
            if (sliding) EndSlide(true);   // headroom was checked before the spend; force is safe here

            RecordWallRunDiag(WallRunReject.None, true, n);
            vel = WallRunMath.Enter(vel, runDir, WallRunSettings);
            wallLostTime = 0f;
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
        /// <summary>
        /// A move landed on its moment: give stamina back and say so. The refund is clamped by
        /// <see cref="PlayerStamina.Refund"/>, so the amount raised is what actually arrived (0 on a
        /// full bar -- the event still fires, because the perfect is the skill, not the payout).
        /// Never called for an ordinary move; a miss has no branch here at all.
        /// </summary>
        void Perfect(PerfectKind kind, float amount)
        {
            float got = stamina != null ? stamina.Refund(amount) : 0f;
            LastPerfectKind = kind;
            LastPerfectTime = now;
            if (OnPerfect != null) OnPerfect(kind, got);
        }

        public bool EndWallRun(WallRunEnd why)
        {
            if (!wallRunning) return false;
            wallRunning = false;
            wallRunEndReason = why;
            wallRunReadyAt = now + wallRunCooldown;
            wallLostTime = 0f;
            // Ended on its own: open the exit-grace window. A jump or a cancel closes it.
            bool natural = why == WallRunEnd.Expired || why == WallRunEnd.Decayed
                        || why == WallRunEnd.LostWall || why == WallRunEnd.Exhausted;
            wallRunLeftAt = natural ? now : -99f;
            wallRunLastDir = wallRunDir;
            wallRunLastNormal = wallRunNormal;
            if (look != null) look.SetRollBias(0f);
            if (OnWallRunEnded != null) OnWallRunEnded();
            return true;
        }

        /// <summary>Why the last run stopped. Diagnostic; the harness and tests read it.</summary>
        public WallRunEnd LastWallRunEnd => wallRunEndReason;

        // ---- WALL RUN DIAGNOSTIC ------------------------------------------------------------------
        // What the last TryWallRun decided and the numbers it decided on, so a play-tester can see WHY
        // "the wall run only works sometimes" (DebugKeys F9). A struct assigned in place - no strings,
        // no boxing - and the recording compiles away outside the editor / development builds, so the
        // release movement path pays a single `return false` per gate, exactly as before.

        /// <summary>One frame's wall-run entry verdict. Cosines are NaN where no wall was in reach to
        /// measure against; stamina is -1 where there is no <see cref="PlayerStamina"/>.</summary>
        public struct WallRunDiagnostic
        {
            public WallRunReject reason;   // None = a run started this frame
            public float time;             // motor-local `now` when recorded; compare with MotorTime for staleness
            public float flatSpeed;        // horizontal speed, m/s
            public float approachCos;      // |dot(travel dir, wall normal)|: 0 = along the face, 1 = straight into it
            public float lookCos;          // dot(flat look, run dir): 1 = looking down the run
            public bool wallFound;         // a runnable face was in reach (the scan ran and hit)
            public float stamina;          // PlayerStamina.Current at the time
        }

        WallRunDiagnostic wallRunDiag;

        /// <summary>The verdict of the most recent <see cref="TryWallRun"/>. Editor / development builds
        /// only; default (reason None, everything zero) in release.</summary>
        public WallRunDiagnostic WallRunDiag => wallRunDiag;

        /// <summary>Motor-local time, so a reader can tell how old <see cref="WallRunDiag"/> is.</summary>
        public float MotorTime => now;

        /// <summary>Record a refusal and return false, so each gate in <see cref="TryWallRun"/> stays a
        /// one-liner. Never changes the verdict.</summary>
        bool WallRunRefused(WallRunReject why, bool wallFound, Vector3 wallNormal)
        {
            RecordWallRunDiag(why, wallFound, wallNormal);
            return false;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        void RecordWallRunDiag(WallRunReject why, bool wallFound, Vector3 wallNormal)
        {
            wallRunDiag.reason = why;
            wallRunDiag.time = now;
            wallRunDiag.wallFound = wallFound;
            wallRunDiag.stamina = stamina != null ? stamina.Current : -1f;
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            float flatSpeed = flat.magnitude;
            wallRunDiag.flatSpeed = flatSpeed;
            wallRunDiag.approachCos = float.NaN;
            wallRunDiag.lookCos = float.NaN;
            if (!wallFound) return;

            // Same arithmetic as WallRunMath.CanEnter, so the numbers shown are the numbers judged.
            Vector3 nf = new Vector3(wallNormal.x, 0f, wallNormal.z);
            if (nf.sqrMagnitude < 1e-6f || flatSpeed < 1e-4f) return;
            nf.Normalize();
            wallRunDiag.approachCos = Mathf.Abs(Vector3.Dot(flat / flatSpeed, nf));

            Vector3 runDir;
            if (!WallRunMath.RunDirection(vel, nf, out runDir)) return;
            Vector3 lf = look != null
                ? new Vector3(look.AimForward.x, 0f, look.AimForward.z)
                : new Vector3(transform.forward.x, 0f, transform.forward.z);
            if (lf.sqrMagnitude < 1e-6f) return;
            lf.Normalize();
            wallRunDiag.lookCos = Vector3.Dot(lf, runDir);
        }

        /// <summary>
        /// Advance a run by up to <paramref name="dt"/> seconds and move the controller. Returns the time
        /// actually SPENT on the wall, which is less than <paramref name="dt"/> on the frame the duration
        /// cap is reached; <see cref="Update"/> spends the remainder in ordinary air, so the length of a
        /// run does not depend on where the frame boundaries happened to fall.
        /// </summary>
        float AdvanceWallRun(float dt, Vector3 wish)
        {
            Vector3 n = wallRunNormal;
            if (!ProbeWall(ref n))
            {
                // Contact lost. Ride it out for wallRunLostGrace on the last known face before giving
                // up: a seam between two boxes, a doorway lintel or a 5 cm bulge is not the end of the
                // wall, and ending the run there was most of what the player felt as glitching.
                wallLostTime += dt;
                if (wallLostTime > wallRunLostGrace) { EndWallRun(WallRunEnd.LostWall); return 0f; }
                n = wallRunNormal;
            }
            else wallLostTime = 0f;
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

            // Lean AWAY from the face (Titanfall's convention, and what the user's inner ear expects: your
            // body is being held up by the wall, so your head tilts off it). WallSide is +1 for a wall on
            // the right and PlayerLook's positive roll tilts the head LEFT, so the sign is +. It shipped
            // negative for a day - toward the wall - and read as reversed the first time it was played.
            if (look != null) look.SetRollBias(WallSide(n) * wallRunCameraRoll);

            if (cc.isGrounded) { IsGrounded = true; lastGroundedTime = now; EndWallRun(WallRunEnd.Landed); return used; }

            // The wall costs stamina by the second. Empty bar: the wall lets go. From a full bar a whole
            // run is always affordable (12 + 22 x 1.75 = 50.5 of 100), which is the assumption the
            // analyser's "full run" model rests on.
            if (!IsWallSurging && stamina != null && !stamina.Drain(stamina.wallRunDrainPerSecond, used))
            {
                EndWallRun(WallRunEnd.Exhausted);
                return used;
            }

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
        PlayerStamina stamina;
        Vector3 vel;
        // The motor's own clock. Every timer here (dash, slide, coyote, jump buffer, cooldowns) is
        // measured against THIS, never Time.time: Time.time is the world clock, which hitstop drives to
        // ~0.02x while the player keeps moving on PlayerDelta. On the world clock a dash landed with a
        // hit kept travelling for the whole freeze and every window grew by the hitstop length.
        float now;
        float lastGroundedTime = -99f, jumpPressedAt = -99f;
        float dashUntil, dashReadyAt;
        /// <summary>The dash in flight began on the ground (or inside coyote): a jump may be thrown out of it.</summary>
        bool dashFromGround;
        bool airDashUsed;
        Vector3 dashDir;
        /// <summary>Speed of the dash in flight: dashSpeed, or dashSpeed x pullBurstMultiplier for a burst.</summary>
        float dashSpeedNow;
        // Water: refreshed by WaterVolume.OnTriggerStay through TouchWater; expires on the motor clock.
        float waterUntil = -99f;
        float launchedAt = -99f;
        WaterVolume waterVolume;
        // Grapple burst: armed on arrival, opened when control is back, closed by the window or a fire.
        float pullBurstUntil = -99f;
        bool pullBurstPending;
        float pullBurstPendingUntil = -99f;
        // Perfect timing: when the dash in flight started and when the burst window opened, on the motor clock.
        float dashStartedAt = -99f;
        float pullBurstOpenedAt = -99f;
        float groundedPosTimer;

        bool sliding;
        float slideEndsAt, slideReadyAt;
        float lastSlideEndedAt = -99f;
        int slideChain;
        float standHeight = 1.8f;
        Vector3 standCenter;

        int wallJumpsUsed;
        Vector3 lastWallNormal;
        bool hasLastWall;

        bool wallRunning;
        float wallRunElapsed, wallRunReadyAt = -99f;
        float wallLostTime;                 // seconds the probe has failed for, inside wallRunLostGrace
        float wallRunLeftAt = -99f;         // motor clock when the last run ended on its own
        Vector3 wallRunLastDir;             // run direction at that moment, for the exit-grace jump
        Vector3 wallRunLastNormal;          // the LAST PROBED normal, not the entry one: curved faces
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
            stamina = GetComponent<PlayerStamina>();
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
            now += dt;

            // Grapple pull: the item owns velocity outright until it arrives. See BeginPull.
            if (pulling) { AdvancePull(dt); return; }

            // Posture broken: heavily slowed and unable to jump or dash, but NOT frozen — a full
            // lock-up over a pit would turn a stagger into a fall death.
            bool staggered = combat != null && combat.IsStaggered;
            bool canAct = CanMove && !staggered;

            // WATER. A stay-refreshed touch on the motor clock: true for waterGrace past the last
            // physics step that saw the volume, so a missed Exit (a teleport disables the controller)
            // cannot leave the player skating up the next staircase.
            bool inWater = now < waterUntil;
            if (!inWater) waterVolume = null;
            Vector3 flow = inWater && waterVolume != null ? waterVolume.Flow : Vector3.zero;

            // GRAPPLE BURST. Armed by EndPull(arrived); the window only OPENS once control is back,
            // because the deathblow cinematic holds CanMove false for its whole beat and a window that
            // ran out under a cutscene would be a burst nobody could ever press.
            if (pullBurstPending)
            {
                if (now > pullBurstPendingUntil) pullBurstPending = false;
                else if (CanMove) { pullBurstPending = false; pullBurstUntil = now + pullBurstWindow; pullBurstOpenedAt = now; }
            }
            bool burst = TraversalMath.BurstOpen(now, pullBurstUntil);

            Vector2 m = CanMove ? input.MoveAxis : Vector2.zero;
            Vector3 wish = transform.right * m.x + transform.forward * m.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            if (staggered) wish *= 0.4f;

            bool wasGrounded = IsGrounded;
            // An explicit take-off (Launch, the grapple's first frame) clears IsGrounded and sets vel.y
            // upward BEFORE the controller has moved, so cc.isGrounded is still last frame's answer.
            // Trusting it here ran one grounded frame of friction on a body that was already leaving:
            // 1.6 m/s gone at 140 fps, and Slide_EndsWhenAirborneButKeepsSpeed red on fast editor frames.
            IsGrounded = cc.isGrounded && !(!wasGrounded && vel.y > 0f);
            if (IsGrounded)
            {
                lastGroundedTime = now;
                airDashUsed = false;
                wallJumpsUsed = 0;
                wallRunsUsed = 0;
                hasLastWall = false;   // a landing forgives the wall you last used
                if (!wasGrounded)
                {
                    LastLandingSpeed = Mathf.Abs(vel.y);   // captured before vel.y is clamped to -2
                    // WEIGHT. A hard landing costs horizontal speed in proportion to how hard it was;
                    // a flat jump's landing costs nothing. Before TrySlide below, so the landing-slide
                    // stays the way to keep a run alive off a drop.
                    float keep = LandingSpeedFactor(LastLandingSpeed, landingSoftSpeed, landingHardSpeed, landingSpeedLoss);
                    LastLandingLoss = 1f - keep;
                    if (keep < 1f) { vel.x *= keep; vel.z *= keep; }
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
            // A broken posture drops you off the wall. Being staggered mid-run and carrying on would be
            // the one place in the game where losing a trade costs you nothing. Checked BEFORE the run
            // advances: a run consumes the whole frame and returns, so a check after it never fired.
            if (wallRunning && !canAct) EndWallRun(WallRunEnd.Cancelled);
            if (wallRunning)
            {
                bool buffered = now - jumpPressedAt <= jumpBuffer;
                if (canAct && buffered && TryWallJump())
                {
                    // Left the wall with the run's momentum. The rest of the frame is ordinary air, so
                    // fall through with dt intact.
                }
                else if (canAct && dashRequested && (burst || (now >= dashReadyAt && !airDashUsed
                         && (stamina == null || stamina.CanAfford(stamina.dashCost)))))
                {
                    // Same gate as the dash below (airborne, so the air dash must be unspent) — otherwise
                    // a spent dash press dropped you off the wall and no dash came.
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

            Vector3 hv = new Vector3(vel.x, 0f, vel.z);

            if (IsDashing)
            {
                hv = dashDir * dashSpeedNow;
                vel.y = 0f;
                // THE DASH-JUMP. Everything below this branch -- the ground and air logic, and with it
                // the jump block -- is skipped while a dash is in flight, so a jump pressed inside a
                // dash used to be examined only after it ended (and the 22 m/s sweep had lifted the
                // controller off the floor by then, so coyote had lapsed too): a dud, silently. A dash
                // that began on the ground is jump-eligible for its whole length; the jump ENDS the
                // dash so vel.y survives the next frame, and the dash's speed is already in hv, so it
                // becomes carried momentum that settles like every other burst. This is the move the
                // PERFECT window rewards (MOVEMENT-PRINCIPLES rule 4: the moment, not the frame).
                if (canAct && dashFromGround && now - jumpPressedAt <= jumpBuffer)
                {
                    bool perfect = !LastDashWasBurst
                        && PerfectMath.DashJumpIsPerfect(now - dashStartedAt, perfectDashJumpMinDelay, perfectDashJumpWindow);
                    EndSlide();
                    vel.y = Mathf.Sqrt(2f * -gravity * jumpHeight);
                    jumpPressedAt = -99f;
                    lastGroundedTime = -99f;
                    IsGrounded = false;
                    dashUntil = now;
                    if (OnJumped != null) OnJumped();
                    if (perfect) Perfect(PerfectKind.DashJump, perfectDashJumpRefund);
                }
            }
            else
            {
                // A slide keeps running on ground it has only just left. THIS IS LOAD-BEARING, not a
                // nicety: a CharacterController moving 0.8 m horizontally in one frame (16 m/s at 20 fps)
                // reports isGrounded FALSE on a flat floor, so a strictly-grounded slide bled no speed,
                // never reached its floor, and cancelled itself — 4.0 m at 500 fps and 1.8 m at 20 fps
                // from the same press. Framerate-dependent movement is unshippable in a speedrun game.
                bool slideOnGround = sliding && (IsGrounded || now - lastGroundedTime <= coyoteTime);

                if (slideOnGround && inWater)
                {
                    // A SLIDE ON WATER IS A SKATE: no friction, no decay end, no duration cap. It holds
                    // the water floor, turns at the skating rate and rides the conveyor, and it ends
                    // only when you leave the water (the ordinary rules resume with a full tail) or
                    // jump. The chain falloff is untouched - the boost you bought entering is spent
                    // the same way; the water just refuses to take it back.
                    Vector3 rel = TraversalMath.WaterStep(hv - flow, wish, WaterFloorSpeed, waterAccel, dt);
                    hv = TraversalMath.WaterVelocity(rel, flow);
                    slideEndsAt = now + slideMaxDuration;
                    if (IsGrounded && vel.y < 0f) vel.y = -2f;
                }
                else if (slideOnGround)
                {
                        // Steer slowly, bleed slowly. The slide is a committed line, and it is the
                        // ENTRY speed you are buying — jump-cancel it early and you keep all of it.
                        if (wish.sqrMagnitude > 0.001f)
                        {
                            float sp0 = hv.magnitude;
                            hv = Vector3.MoveTowards(hv, wish.normalized * sp0, slideSteerAccel * dt);
                        }
                        hv *= Mathf.Max(0f, 1f - slideFriction * dt);

                        bool spent = hv.magnitude <= slideEndSpeed || now >= slideEndsAt;
                        if (spent && !EndSlide())
                        {
                            // A ceiling is holding us down. Keep enough speed to crawl clear rather
                            // than stall crouched under it forever — see ENGINEERING-LOG.
                            Vector3 d = wish.sqrMagnitude > 0.001f ? wish.normalized
                                      : (hv.sqrMagnitude > 0.0001f ? hv.normalized
                                      : new Vector3(transform.forward.x, 0f, transform.forward.z).normalized);
                            hv = d * Mathf.Max(hv.magnitude, slideEndSpeed);
                            slideEndsAt = now + 0.2f;
                        }
                        // Only pinned to the floor when actually ON it — sliding off a ledge has to fall.
                        if (IsGrounded && vel.y < 0f) vel.y = -2f;
                }
                else if (IsGrounded && inWater)
                {
                    // SKATING. Velocity relative to the flow is held at or above the water floor with
                    // no friction and no overspeed decay, turned at waterAccel, and the conveyor is
                    // added back. Water never slows anyone: the only way off the floor speed is off
                    // the water. See TraversalMath.WaterStep.
                    Vector3 rel = TraversalMath.WaterStep(hv - flow, wish, WaterFloorSpeed, waterAccel, dt);
                    hv = TraversalMath.WaterVelocity(rel, flow);
                    if (vel.y < 0f) vel.y = -2f;
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
                        // Excess over a sprint decays EXPONENTIALLY toward the sprint: exp(-k dt) is the
                        // same answer at every framerate, and a dash landing settles into a run in ~0.5 s
                        // instead of holding 22 m/s for as long as you keep the stick forward.
                        hv = DecayExcess(hv, groundSpeed * SpeedMultiplier, groundOverspeedDecay, dt);
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
                    if (sliding && now - lastGroundedTime > coyoteTime) EndSlide();
                    // AIR CONTROL, two channels. STEER turns the velocity toward the stick at a fixed
                    // angular rate with the speed untouched (the surf feel: you decide where 17 m/s goes);
                    // ACCELERATE is Source's rule, adding along the stick only up to a run's worth, which
                    // is what a stick held BACK uses to brake. Steer first, so the brake acts on the
                    // direction you chose.
                    // THE FLOAT: for launchFloatSeconds after a balloon pop the steer turns faster, so
                    // the re-armed dash can be aimed at the next orb (the gravity half is below).
                    bool floating = now - launchedAt <= launchFloatSeconds;
                    hv = AirSteer(hv, wish, airSteerDegPerSec * (floating ? launchSteerBoost : 1f), dt);
                    hv = AirAccelerate(hv, wish, groundSpeed * SpeedMultiplier, airAccel, dt);
                    // WEIGHT. Speed above a run bleeds slowly while airborne (airCarryDecay): a wall exit
                    // or a slide-jump is a burst you spend, not a glide you keep. Then the SOFT CAP:
                    // the surplus over airSoftCap (a dash) bleeds fast on exp(-airDrag t). Together:
                    // momentum, never a cruise.
                    // Inside the water's boost zone (a hop along the surface) the carry is kept -
                    // "air resistance is very low" over water is the Neon White rule - but the soft cap
                    // still bleeds a dash, so a hop cannot launder one into a cruise.
                    if (!inWater) hv = DecayExcess(hv, groundSpeed * SpeedMultiplier, airCarryDecay, dt);
                    hv = DecayExcess(hv, airSoftCap, airDrag, dt);
                }

                // A balloon pop hangs: gravity is scaled inside the float window (see launchFloatSeconds).
                float gNow = now - launchedAt <= launchFloatSeconds ? gravity * launchGravityScale : gravity;
                vel.y += FallGravity(gNow, vel.y, IsGrounded, fallGravityMultiplier) * dt;
                if (!IsGrounded && vel.y > 0f && !input.JumpHeld) vel.y += gravity * jumpCutGravityMultiplier * dt;

                bool canJump = now - lastGroundedTime <= coyoteTime;
                bool buffered = now - jumpPressedAt <= jumpBuffer;
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
                        // (The PERFECT dash-jump is judged in the dash branch above: this block never
                        // runs while a dash is in flight.)
                    }
                    else
                    {
                        // Airborne with a pending press: the only remaining jump is off a wall. This is
                        // the ONLY frame the wall scan runs.
                        vel.x = hv.x; vel.z = hv.z;         // TryWallJump reads and writes vel
                        if (TryWallJump()) { hv.x = vel.x; hv.z = vel.z; }
                    }
                }

                // The burst short-circuits every gate INCLUDING the stamina spend: TrySpend has a side
                // effect, so it must stay last and must not run for a burst.
                if (canAct && dashRequested && (burst || (now >= dashReadyAt && (IsGrounded || !airDashUsed)
                    && (stamina == null || stamina.TrySpend(stamina.dashCost, StaminaAction.Dash)))))
                {
                    EndSlide();
                    dashDir = wish.sqrMagnitude > 0.01f ? wish.normalized : new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
                    dashUntil = now + dashDuration;
                    dashFromGround = IsGrounded || now - lastGroundedTime <= coyoteTime;
                    bool perfectBurst = false;
                    if (burst)
                    {
                        // The grapple exit burst: faster, free, and it leaves the ordinary dash exactly
                        // as it was - cooldown untouched, air charge untouched - so exploding out of a
                        // kill never costs the dash you had.
                        perfectBurst = PerfectMath.BurstIsPerfect(now - pullBurstOpenedAt, perfectBurstWindow);
                        pullBurstUntil = -99f;
                        dashSpeedNow = TraversalMath.BurstSpeed(dashSpeed, pullBurstMultiplier);
                        LastDashWasBurst = true;
                    }
                    else
                    {
                        dashReadyAt = now + dashCooldown;
                        if (!IsGrounded) airDashUsed = true;
                        dashSpeedNow = dashSpeed;
                        LastDashWasBurst = false;
                    }
                    dashStartedAt = now;
                    hv = dashDir * dashSpeedNow;
                    vel.y = 0f;
                    if (OnDashed != null) OnDashed();
                    if (perfectBurst) Perfect(PerfectKind.GrappleBurst, perfectBurstBonus);
                }
            }

            // One request lives exactly one movement step. Without this a dash pressed while already
            // dashing (the branch above is skipped) would fire again the instant the dash ended.
            dashRequested = false;

            // Absolute ceiling. Nothing above is designed; nothing above may survive a bug either.
            if (!IsDashing)
            {
                float s2 = hv.x * hv.x + hv.z * hv.z;
                if (s2 > maxHorizontalSpeed * maxHorizontalSpeed) hv *= maxHorizontalSpeed / Mathf.Sqrt(s2);
            }

            vel.x = hv.x;
            vel.z = hv.z;
            // GROUND SNAP, displacement only. While grounded and not rising the controller is pressed
            // down at least groundSnapDistance: the sweep stops at the floor, so on flat ground it
            // costs nothing, on a step down it follows, and at a ledge it is one frame of extra drop.
            // Without it the -2 m/s pin moved 0.004 m at 500 fps - inside the skin width - and
            // CharacterController.isGrounded flickered off, ending every slide at coyote time.
            Vector3 disp = vel * dt;
            if (IsGrounded && vel.y <= 0f && groundSnapDistance > 0f && disp.y > -groundSnapDistance)
                disp.y = -groundSnapDistance;
            // NEAR-MISS LANDING (forgiveness). Falling, not sliding or wall running, with the feet about
            // to pass just under a ledge top that lies ahead along the carried velocity: lift the body so
            // the next sweep lands on it. Bounded per frame, adds no velocity, ignores walls (a wall side
            // has no upward normal), and never fires on a standing drop.
            if (!IsGrounded && vel.y < 0f && !sliding && !wallRunning && !IsDashing && ledgeCatchMetres > 0f)
            {
                Vector3 hvNow = new Vector3(vel.x, 0f, vel.z);
                float hs = hvNow.magnitude;
                if (hs >= ledgeCatchMinSpeed)
                {
                    Vector3 ahead = transform.position + hvNow / hs * (cc.radius + 0.12f);
                    Vector3 from = ahead + Vector3.up * (ledgeCatchMetres + 0.05f);
                    if (Physics.Raycast(from, Vector3.down, out RaycastHit ledge, ledgeCatchMetres + 0.10f, worldMask, QueryTriggerInteraction.Ignore)
                        && ForgivenessMath.LedgeCatches(transform.position.y, ledge.point.y, ledgeCatchMetres, vel.y,
                                                        ledge.normal.y, 0.7f, hs, ledgeCatchMinSpeed))
                    {
                        float lift = ForgivenessMath.NudgeStep(ledge.point.y - transform.position.y + 0.02f, ledgeCatchLiftSpeed, dt);
                        if (lift > 0f) { disp.y = Mathf.Max(disp.y, 0f) + lift; }
                    }
                }
            }
            var flags = cc.Move(disp);
            if ((flags & CollisionFlags.Above) != 0 && vel.y > 0f)
            {
                // CORNER CORRECTION (forgiveness). The head met something while rising. If a capsule
                // shifted sideways by no more than cornerCorrectionMetres is clear, the player clipped a
                // ledge CORNER: nudge them across it and keep the jump. Otherwise it is a ceiling.
                float nudged = 0f;
                if (cornerCorrectionMetres > 0f)
                {
                    float r = cc.radius * 0.95f;
                    Vector3 p0 = transform.position + cc.center + Vector3.up * (cc.height * 0.5f - r) + Vector3.up * 0.06f;
                    Vector3 p1 = transform.position + cc.center - Vector3.up * (cc.height * 0.5f - r) + Vector3.up * 0.06f;
                    Vector3[] dirs = { transform.right, -transform.right, transform.forward, -transform.forward };
                    for (int i = 0; i < dirs.Length && nudged <= 0f; i++)
                    {
                        Vector3 off = dirs[i] * cornerCorrectionMetres;
                        if (!Physics.CheckCapsule(p0 + off, p1 + off, r, worldMask, QueryTriggerInteraction.Ignore))
                        {
                            cc.Move(off);
                            nudged = cornerCorrectionMetres;
                        }
                    }
                }
                if (!ForgivenessMath.CornerCorrects(nudged, cornerCorrectionMetres, vel.y)) vel.y = 0f;
            }
        }

        /// <summary>Gravity for this frame: the base value while grounded or rising, scaled by
        /// <paramref name="fallMultiplier"/> while airborne and descending. Pure.</summary>
        public static float FallGravity(float gravity, float vy, bool grounded, float fallMultiplier)
        {
            return (!grounded && vy < 0f) ? gravity * fallMultiplier : gravity;
        }

        /// <summary>Turn the horizontal velocity toward <paramref name="wish"/> at <paramref name="degPerSec"/>
        /// with its magnitude preserved. No-op when the stick is off, when the stick opposes travel
        /// (that is a brake, AirAccelerate's job) or when barely moving. Pure.</summary>
        public static Vector3 AirSteer(Vector3 hv, Vector3 wish, float degPerSec, float dt)
        {
            if (degPerSec <= 0f || wish.sqrMagnitude < 0.0001f) return hv;
            float sp = hv.magnitude;
            if (sp < 0.5f) return hv;
            Vector3 w = new Vector3(wish.x, 0f, wish.z);
            if (w.sqrMagnitude < 0.0001f) return hv;
            w.Normalize();
            if (Vector3.Dot(hv, w) < -1e-4f) return hv;   // a stick at 90 deg is a strafe, not a brake
            return Vector3.RotateTowards(hv, w * sp, degPerSec * Mathf.Deg2Rad * dt, 0f);
        }

        /// <summary>Fraction of horizontal speed KEPT on a landing at <paramref name="fallSpeed"/>: 1 at or
        /// below <paramref name="soft"/>, 1 - <paramref name="loss"/> at or above <paramref name="hard"/>,
        /// linear between. Pure.</summary>
        public static float LandingSpeedFactor(float fallSpeed, float soft, float hard, float loss)
        {
            if (hard <= soft || loss <= 0f) return 1f;
            float t = Mathf.Clamp01((fallSpeed - soft) / (hard - soft));
            return 1f - Mathf.Clamp01(loss) * t;
        }

        /// <summary>Speed above <paramref name="cap"/> decays on exp(-k dt) toward the cap; speed at or
        /// below it is untouched. Direction is never changed. Pure, framerate-independent.</summary>
        public static Vector3 DecayExcess(Vector3 hv, float cap, float k, float dt)
        {
            float sp = hv.magnitude;
            if (sp <= cap || sp < 1e-5f) return hv;
            float target = cap + (sp - cap) * Mathf.Exp(-k * dt);
            return hv * (target / sp);
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
            CancelPull();
            cc.enabled = false;
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            vel = Vector3.zero;
            dashUntil = 0f;
            dashRequested = false;
            airDashUsed = false;
            jumpPressedAt = -99f;       // a jump buffered before a warp must not fire at the spawn
            lastGroundedTime = -99f;    // no stale coyote window from the old location
            IsGrounded = false;
            wallJumpsUsed = 0;
            wallRunsUsed = 0;
            hasLastWall = false;
            slideChain = 0;
            lastSlideEndedAt = -99f;
            wallLostTime = 0f;
            wallRunLeftAt = -99f;
            waterUntil = -99f;
            launchedAt = -99f;
            waterVolume = null;
            pullBurstUntil = -99f;
            pullBurstPending = false;
            dashStartedAt = -99f;
            pullBurstOpenedAt = -99f;
            LastGroundedPosition = position;
            cc.enabled = true;

            // PlayerLook owns yaw and rewrites transform.rotation every frame, so setting the rotation
            // here alone is silently undone on the next Update. Push the yaw into it too, otherwise any
            // caller that teleports without also calling PlayerLook.SetYaw gets the old facing back.
            if (look == null) look = GetComponent<PlayerLook>();
            if (look != null) look.SetYaw(yaw);
        }

        public void AddImpulse(Vector3 impulse) { vel += impulse; }

        /// <summary>
        /// Launch straight up at a fixed speed - a balloon pop. The vertical component is REPLACED
        /// (<see cref="TraversalMath.Launch"/>: a capped jump, the same height every time), the
        /// horizontal is kept, any dash / slide / wall run ends, and the whole air kit is reset: the air
        /// dash, the wall-jump and wall-run budgets, and the wall you last used. A balloon is a fresh
        /// start in the air, which is what lets a level chain them. Raises <see cref="OnLaunched"/>.
        /// </summary>
        public void Launch(float upSpeed) { Launch(upSpeed, false); }

        /// <summary>
        /// <paramref name="trimCarry"/> is the balloon POP: the horizontal speed is trimmed to
        /// <see cref="launchCarryCap"/> so the next orb is aimable (pop -> aim -> dash). The plain
        /// overload keeps the carry untouched - it is the harness's and any future launcher's lift, and
        /// the slide-jump test relies on a launch never eating the speed a slide earned.
        /// </summary>
        public void Launch(float upSpeed, bool trimCarry)
        {
            dashUntil = 0f;
            airDashUsed = false;
            wallJumpsUsed = 0;
            wallRunsUsed = 0;
            hasLastWall = false;
            jumpPressedAt = -99f;
            EndSlide(true);
            EndWallRun(WallRunEnd.Cancelled);
            vel = TraversalMath.Launch(vel, upSpeed, trimCarry ? launchCarryCap : float.PositiveInfinity);
            IsGrounded = false;
            lastGroundedTime = -99f;
            launchedAt = now;
            if (OnLaunched != null) OnLaunched();
        }

        /// <summary>
        /// Re-arm the dash without firing one: cooldown cleared, air charge restored. What a balloon
        /// does to a player who DASHES through it - the dash in flight carries on untouched and the
        /// next press is free again, so a line of orbs can be dashed one to the next.
        /// </summary>
        public void RearmDash()
        {
            dashReadyAt = now;
            airDashUsed = false;
            // The dash in flight ENDS at the orb and its carry is trimmed to the same cap a pop uses:
            // measured 2026-09-05, a 22 m/s dash carried through orb 1 flew 16 m past orb 2. The chain
            // is pop → aim → dash; the re-armed dash is the reach, the carry only has to be steerable.
            if (IsDashing)
            {
                dashUntil = now;
                Vector3 h = new Vector3(vel.x, 0f, vel.z);
                float m = h.magnitude;
                if (m > launchCarryCap && m > 0.0001f) { h *= launchCarryCap / m; vel.x = h.x; vel.z = h.z; }
                launchedAt = now;   // the float window applies, so the aim has time
            }
        }

        /// <summary>
        /// A <see cref="WaterVolume"/> reports the player inside it. Refreshed every physics step it
        /// stays true; expires waterGrace later on the motor clock. The LAST volume to touch wins, which
        /// is fine - two overlapping sheets are an authoring error, not a case.
        /// </summary>
        public void TouchWater(WaterVolume volume)
        {
            waterVolume = volume;
            waterUntil = now + waterGrace;
        }

        // ---------------------------------------------------------------- items: pull + wall surge
        //
        // Self-contained: everything the two traversal items need from the motor lives between here
        // and the end of the class, plus one `if (pulling)` line at the top of Update and the three
        // IsWallSurging reads in WallRunSettings / TryWallRun / AdvanceWallRun.

        // ---- PULL (Grapple item) ----
        // The item owns velocity outright for the length of the pull: gravity, drag, input and the
        // slide/wall-run branches are all skipped. Driven as a POSITION curve (start → target with a
        // sine bulge) sampled on the motor clock, so the arc is the same at every framerate and
        // hitstop cannot stretch it (rule 1). Every write still goes through CharacterController.Move,
        // so a wall or a lintel in the way ends the pull where the body actually stopped.
        bool pulling;
        Vector3 pullStart, pullTarget;
        float pullStartedAt, pullSeconds, pullArcHeight;

        /// <summary>True while a <see cref="BeginPull"/> is in flight.</summary>
        public bool IsPulling => pulling;
        /// <summary>Where the current pull is heading. Zero when not pulling.</summary>
        public Vector3 PullTarget => pulling ? pullTarget : Vector3.zero;
        /// <summary>Fired once when a pull ends, for any reason. The argument is true on arrival, false
        /// when a collision or a cancel cut it short.</summary>
        public event Action<bool> OnPullEnded;
        /// <summary>Everything the player can stand on or push off - the mask the motor's own casts
        /// use. Items use it for line-of-sight so "can I hook that" and "can I run on that" agree.</summary>
        public int WorldMask => worldMask;

        /// <summary>
        /// Pull the body along a fast arc to <paramref name="target"/> (a TRANSFORM position - feet,
        /// on this prefab) over <paramref name="seconds"/>. Cancels a slide, a wall run and a dash.
        /// Ends on arrival, on a sideways/overhead collision, or via <see cref="CancelPull"/>; the
        /// body keeps the arc's final velocity so a hooked enemy on a ledge is reached with momentum
        /// still in hand.
        /// </summary>
        public void BeginPull(Vector3 target, float seconds)
        {
            EndSlide(true);
            EndWallRun(WallRunEnd.Cancelled);
            dashUntil = 0f;
            dashRequested = false;
            jumpPressedAt = -99f;
            pullStart = transform.position;
            pullTarget = target;
            pullStartedAt = now;
            pullSeconds = Mathf.Max(0.05f, seconds);
            // A low arc: a hair of rise for a short hop, a couple of metres for a full-range pull, so
            // the pull reads as a swing rather than a slide along a string.
            pullArcHeight = Mathf.Clamp((target - pullStart).magnitude * 0.08f, 0.35f, 2.2f);
            pulling = true;
            IsGrounded = false;
        }

        /// <summary>Stop an in-flight pull where it is. Velocity is left as the arc's last sample.</summary>
        public void CancelPull()
        {
            if (!pulling) return;
            EndPull(false);
        }

        void EndPull(bool arrived)
        {
            pulling = false;
            lastGroundedTime = -99f;    // no stale coyote from before the pull
            airDashUsed = false;        // the arc counts as a fresh start for the air kit
            if (arrived)
            {
                // The grapple EXIT BURST is armed here and opened in Update once control is back.
                pullBurstPending = true;
                pullBurstPendingUntil = now + pullBurstHold;
            }
            if (OnPullEnded != null) OnPullEnded(arrived);
        }

        /// <summary>One frame of the pull. Allocation-free; one <c>cc.Move</c>, no casts.</summary>
        void AdvancePull(float dt)
        {
            if (cc == null || !cc.enabled) { EndPull(false); return; }

            float k = Mathf.Clamp01((now - pullStartedAt) / pullSeconds);
            // Ease-out so the arrival is a settle, not a slam; the sine bulge is the arc itself.
            float e = 1f - (1f - k) * (1f - k);
            Vector3 want = Vector3.LerpUnclamped(pullStart, pullTarget, e);
            want.y += Mathf.Sin(e * Mathf.PI) * pullArcHeight;

            Vector3 disp = want - transform.position;
            vel = dt > 1e-5f ? disp / dt : Vector3.zero;

            var flags = cc.Move(disp);

            // A sideways or overhead hit is a wall in the way: stop here, keep what we had. Ground
            // contact (Below) is not a stop - the last part of the arc descends onto the target's floor.
            bool blocked = (flags & (CollisionFlags.Sides | CollisionFlags.Above)) != 0 && k > 0.08f;
            Vector3 left = pullTarget - transform.position;
            bool arrived = k >= 1f || left.sqrMagnitude < 0.04f;
            if (arrived || blocked)
            {
                // Settle: a modest carry-through along the arc's tangent, never the raw sample (which at
                // 20 fps is a single frame of 40 m/s). Clamped to a sprint so the landing is controllable.
                Vector3 hv = new Vector3(vel.x, 0f, vel.z);
                float sp = hv.magnitude;
                if (sp > groundSpeed) hv *= groundSpeed / sp;
                vel = new Vector3(hv.x, Mathf.Min(0f, vel.y), hv.z);
                IsGrounded = cc.isGrounded;
                if (IsGrounded) lastGroundedTime = now;
                EndPull(arrived && !blocked);
            }
        }

        // ---- WALL SURGE (WallSurge item) ----
        // Motor STATE, not a tuning mutation: the Inspector fields never move, WallRunSettings reads the
        // scale, and the two stamina calls in TryWallRun/AdvanceWallRun skip while it runs. On the motor
        // clock, so hitstop neither shortens nor stretches the window (rule 1).

        /// <summary>Multiplier on wallRunTopSpeed and wallRunAccel while surging.</summary>
        public const float WallSurgeSpeedScale = 1.5f;
        /// <summary>Motor-clock time the surge ends. -99 when none has ever run. Public so tests and
        /// the debug harness can end one early.</summary>
        public float wallSurgeUntil = -99f;
        public bool IsWallSurging => now < wallSurgeUntil;
        /// <summary>Seconds left on the surge; 0 when not surging.</summary>
        public float WallSurgeRemaining => Mathf.Max(0f, wallSurgeUntil - now);
        float WallSurgeScale => IsWallSurging ? WallSurgeSpeedScale : 1f;

        /// <summary>Start (or extend to at least) <paramref name="seconds"/> of surge from now.</summary>
        public void StartWallSurge(float seconds)
        {
            wallSurgeUntil = Mathf.Max(wallSurgeUntil, now + Mathf.Max(0f, seconds));
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
        NoTangent,        // degenerate - velocity is entirely into or out of the wall

        // The CHEAP gates in FirstPersonMotor.TryWallRun, checked before any physics query. CanEnter
        // never returns these; they exist so FirstPersonMotor.WallRunDiag can name every refusal.
        // Append only - the diagnostic readout indexes names by value.
        CannotAct,        // staggered / cannot move
        Grounded,         // a wall run starts in the air
        OverBudget,       // maxWallRuns spent since the last landing
        Cooldown,         // wallRunCooldown since the last run ended has not elapsed
        NoWallInReach,    // the scan found no runnable face
        Sliding,          // a slide could not end (ceiling) so cannot yield to the wall
        NoStamina         // every other gate passed; could not afford wallRunEntryCost
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
        Cancelled,    // dash, teleport, stagger
        Exhausted     // stamina hit zero on the wall
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

            // Speed is judged on the WHOLE horizontal velocity, not its tangential part: the approach
            // gate above has already said you are moving along the face, and Enter redirects all of it
            // down the run. Judging the projection made a 45-degree approach at a sprint "too slow",
            // which was most of "the wall run only works sometimes".
            if (flatSpeed < pr.minEntrySpeed) { why = WallRunReject.TooSlow; return false; }

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
            // ALL of the horizontal speed turns down the run (Titanfall: the wall catches and redirects
            // you; it does not bill you for the angle you arrived at). Capped at maxSpeed.
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            float along = flat.magnitude;
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
