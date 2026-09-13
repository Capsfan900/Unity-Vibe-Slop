# Dataflow maps

One map per game system: **what triggers it, what data moves, who owns each step, and where it ends.**
The purpose is long-horizon planning — you should be able to answer "if I change X, what breaks?" from this
file without reading the code.

> **Standing rule — this file is not optional.**
> Adding or materially changing a system means updating its map here in the same change. A map that lies is
> worse than no map. If you add a system, add its section; if you delete one, delete its section.

**Conventions**
- `A → B` = A calls or sends data to B.
- `⇢` = a `GameEvents` broadcast (fire-and-forget; the sender does not know the receivers).
- **bold** = the single owner of that step.
- Every map ends with an **Invariants** block — the things that must stay true.

---

## System index

| System | Status | Entry point |
|---|---|---|
| [Input](#input) | mapped | `InputReader` |
| [Movement](#movement) | mapped | `FirstPersonMotor` |
| [Time scale](#time-scale) | mapped | `TimeScaleController` |
| [Combat: incoming attack](#combat-incoming-attack) | mapped | `EnemyController.DoImpact` |
| [Combat: outgoing attack](#combat-outgoing-attack) | mapped | `WeaponController` |
| [Lock-on](#lock-on) | mapped | `LockOnController` |
| [Posture and deathblow](#posture-and-deathblow) | mapped | `Posture` / `PlayerPosture` |
| [Riposte and wands](#riposte-and-wands) | mapped | `ExecuteInteractor` |
| [Viewmodel arms](#viewmodel-arms) | mapped | `WeaponViewmodel` / `ViewmodelArm` |
| [Swing trail](#swing-trail) | mapped | `WeaponTrail` |
| [Pyre and the super attack](#pyre-and-the-super-attack) | mapped | `PlayerResources` / `UltimateAbility` |
| [Items](#items) | mapped | `ItemPickup` |
| [Enemy AI](#enemy-ai) | mapped | `EnemyController.Update` |
| [Boss](#boss) | mapped | `BossController` |
| [Progression](#progression) | mapped | `SoulsWallet` |
| [Level flow](#level-flow) | mapped | `LevelManager` |
| [HUD](#hud) | mapped | `GameEvents` ⇢ `HUDController` |
| [Audio](#audio) | mapped | `AudioManager` |
| [Wand pedestal](#wand-pedestal) | mapped | `WandPedestal` |
| [Main menu](#main-menu) | mapped | `MainMenuController` |

---

## Input

```
Unity Input System (project-wide InputSystem_Actions asset)
  → InputReader  (THE ONLY consumer of the Input System)
      exposes bools/axes: MoveAxis, LookDelta, JumpPressed, DashPressed, SlidePressed/SlideHeld,
      AttackPressed, ParryPressed, HealPressed, UltimatePressed, LockOnPressed,
      UseItemPressed, WandCyclePressed, WeaponSlotPressed, TestMenuPressed, PausePressed…
  → every consumer polls InputReader.I in its own Update()
```

**Invariants**
- No other class may reference `UnityEngine.InputSystem`. One consumer means rebinding is a one-file change.
- Actions are looked up with `throwIfNotFound: false`, so a missing binding degrades to "never pressed"
  rather than throwing at startup.
- Input is still polled inside `Update()`, but every input-gated behaviour now has a **public entry point**
  that `Update` calls with the polled value: `FirstPersonMotor.TryJump/TryDash`, `WeaponController.TryAttack`,
  `FlaskAbility.TryDrink`, `UltimateAbility.TryUltimate`, `SpeedrunTimer.TryStartRun`. Input reading stays in
  `Update`; the Try* methods take no input and re-apply their own gates, so tests (and, later, a network
  command stream) drive the real behaviour without a synthesised device. This closed all 7 feature-test skips.
  `LockOnController.TryLockOn` is one of these.
- **Check the whole keyboard before binding a key.** `F` was given to the wand altar while `Heal` already
  held it and both fired on one press. `LockOn` is `<Mouse>/middleButton` because middle mouse is the Souls
  convention *and* was the only free pointer button — the scroll wheel, the other convention, is already
  `Previous`/`Next` weapon cycling, so lock-on switching had to be folded into the lock key instead.
  `Slide` is `<Keyboard>/leftCtrl` + `<Gamepad>/leftTrigger`, both of which were bound to nothing at all.
  **`C` was NOT used even though it plays as free**: the Unity template's unread `Crouch` action still holds
  it, and putting a second action on an occupied key is exactly the `F` bug. Left trigger was chosen over
  `buttonEast` for the same reason — `buttonEast` already carries `Dash` *and* `Crouch`.
- **The wall jump got no binding at all.** It is `Space` while airborne near a wall, resolved inside
  `FirstPersonMotor` after the coyote jump has had its chance. A separate key for "jump, but on a wall" is a
  key the player has to think about in mid-air.

---

## Lock-on

```
InputReader.LockOnPressed → LockOnController.Update() → TryLockOn()   ← public entry point
    not locked            → FindBest()  → Acquire(e)
    locked, aim < 14 deg off target     → Release()
    locked, aim >= 14 deg off target    → FindBest(); switch, or Release if it is the same enemy

  FindBest() scores EnemyController.ActiveEnemies:
      alive, dist <= acquireRange (26), angle off crosshair <= 55 deg, line of sight clear
      score = angle_deg + dist_m * 0.35        ← lowest wins; the thing you LOOK at beats the thing that is near

LockOnController.LateUpdate()          ← after PlayerLook.Update has applied the player's own mouse
  Validate()   target dead / destroyed / dist > dropRange (32) / aim > 62 deg off / occluded > 0.7 s
               → Release()
  Assist(point)
      err  = angle(cam.forward, target)                       ; skipped inside a 2.2 deg deadzone
      gate = 1 while the mouse is still, 0 once it is moving  ← THE MOUSE IS AUTHORITATIVE
      rate = min(4 * err, 90 deg/s) * gate * fade
      → PlayerLook.NudgeAim(dYaw, dPitch)  with dt = TimeScaleController.PlayerDelta
  marker.Track(chestPoint, notOccluded)
      → LockOnMarker: pulls the dot 0.75 m off the chest toward the eye, billboards it,
        scales it to a constant ANGULAR size, eases the acquire pop. Unscaled time.
```

**The marker is ONE object owned by the player**, built into the Player prefab by
`PrefabFactory.BuildLockOn`, not one per enemy like the alert cube and the deathblow glyph. Lock-on is a
fact about the *player* and at most one thing is ever the target, so a single mote travels — which also
means every lockable body gets it for free, including the `Legendary_*` mini-bosses that a different
factory builds.

**Invariants**
- **The mouse is never scaled, filtered or overridden.** `PlayerLook.Update` applies the player's look
  delta exactly as it always did; the assist runs afterwards in `LateUpdate` and only adds. Its gate
  falls to zero the moment the mouse moves, so an assist frame and a player frame never overlap.
- `PlayerLook` still owns yaw and pitch. Nothing rotates the camera transform directly —
  `NudgeAim(dYaw, dPitch)` is the only sanctioned door, and it re-applies through `Apply()`. A direct
  transform write is silently undone on the next frame.
- The assist integrates `TimeScaleController.PlayerDelta`, never `Time.deltaTime` (rule 1). On scaled
  time it would stall during the hitstop of the very parry it is helping you land.
- **Looking away breaks the lock; it never pulls you back.** Past `breakAngleDeg` (62) the lock drops.
  Fighting the player for the camera is the failure mode of every bad aim assist.
- The dot marks the target's **chest**, and is pulled 0.75 m toward the eye — the chest point is *inside*
  a 0.45 m capsule, so a dot left on it renders inside the enemy and is never seen.
- The dot is the one combat marker held **under** the 1.05 bloom threshold. Never raise it: the alert
  tell and the deathblow glyph are 2.5–3x over it, and "does not bloom" is what separates information
  from an alarm at a glance.

---

## Movement

```
InputReader → FirstPersonMotor.Update()
    JumpPressed  → TryJump()    DashPressed → TryDash()    ← public entry points; all re-check CanAct
    SlidePressed → TrySlide()   (Jump while airborne near a wall → TryWallJump())
    dt = TimeScaleController.PlayerDelta      ← NOT Time.deltaTime
    staggered? (PlayerCombat.IsStaggered) → wish *= 0.4, no jump/dash/slide/wall jump
    ground/air acceleration, coyote time, jump buffer, variable jump, dash
    STAMINA    dash / wall run / wall jump ask PlayerStamina.TrySpend first — see "Stamina" below
    MOMENTUM   DecayExcess(hv, cap, k, dt): speed over a cap bleeds on exp(-k dt), never a flat ceiling
                 air:    excess over groundSpeed 11 at airCarryDecay 0.8/s (the carry is spent, not kept),
                         THEN excess over airSoftCap 17.6 (1.6x run) at airDrag 3/s (a dash settles in ~0.5 s)
                 ground: excess over groundSpeed 11 at groundOverspeedDecay 4/s
                 hard clamp maxHorizontalSpeed 27.5 outside a dash
    WEIGHT     gravity -30 rising, x fallGravityMultiplier 1.5 while airborne and descending (FallGravity)
                 landing: LandingSpeedFactor(fallSpeed, soft 16, hard 26, loss 0.35) scales hv on the landing
                 frame, BEFORE this frame's TrySlide — a flat jump (lands ~14.7) is free, a 7.5 m drop costs 35%
    AIR STEER  AirSteer(hv, wish, 120 deg/s, dt): velocity TURNS toward the stick, speed preserved, only while
                 the stick is within 90 deg of travel; then AirAccelerate (Source: add along wish up to a run)
                 which is what a stick held back brakes with
    GROUND SNAP final cc.Move displacement.y = min(vel.y dt, -groundSnapDistance 0.12) while grounded and not
                 rising — displacement only, the sweep stops at the floor. Without it the -2 pin moved 0.004 m
                 at 500 fps, inside skinWidth 0.05, isGrounded flickered off and every slide died at coyote time
    SLIDE      costs stamina.slideCost 12 (spent in TrySlide AFTER the standstill / cooldown / airborne / min-speed
               gates, so a refused slide is free; a refusal raises StaminaAction.Slide and flashes like the dash's)
               capsule 1.8 → 0.9 m, boost +5 x fade x 0.6^chain (cap 22), bleeds at 2/s to a floor of 8
                 fade = (22 - speed)/(22 - 11): full at a sprint, nothing at the cap
                 chain = slides started within 1.2 s of the last one ENDING: 5, 3, 1.8, 1.1 ...
               SLOPE (2026-09-07, ramps): TraversalMath.DrySlideStep applies the original
                 hv += SlopeAccel(GroundNormal, slideSlopeAccel 0.85) dt, then friction, on flat/uphill/air contact.
                 Only an actually grounded slide moving downhill on a slope within cc.slopeLimit sustains:
                 no dry friction, gravity adds speed up to existing slideMaxSpeed 22, duration renewed to
                 now + slideMaxDuration 0.9. Carry already above 22 is preserved but gains no further speed.
                 Flat/uphill exit immediately resumes ordinary friction and expiry with that remaining tail.
                 Water, steering, slide-jump and dash cancellation keep their existing paths.
                 GroundNormal is written in OnControllerColliderHit during
                 cc.Move -- the most UPWARD contact of the frame with normal.y > 0.2, so a wall brushed while
                 sliding down a ramp is not read as ground. The term is gravity projected onto the ground
                 plane, flattened to XZ, and is EXACTLY zero on a flat floor: every span authored before ramps
                 existed is an axis-aligned box, so nothing tuned on the flat can drift. Uphill opposes the
                 same gravity vector and keeps the original friction, so it bleeds and dies early on a climb.
                 DownhillSnapY extends only the grounded downhill slide's displacement to the contacted
                 plane at this frame's horizontal destination, plus groundSnapDistance. This keeps the
                 1:4 slope grounded when 20 fps travel exceeds the old fixed snap. It never writes vel.y;
                 rising, jump/dash cancellation, water, air and flat/uphill movement retain the original snap.
                 slideSlopeAccel = 0 restores the pre-ramp motor without a code change.
                 SlopeSlideTests + DownhillSlideTests cover shipped tuning and 20/60/240 fps arithmetic;
                 LevelDescentProbe checks actual controller contact and the uninterrupted descent in play.
    WALL JUMP  8 x SphereCastNonAlloc fan → vel.y = 11, +12 m/s along the wall normal; costs 12 stamina
    WALL RUN   no binding; entered by arriving — see "Movement — wall run" below
    now += dt                                 ← the motor's OWN clock; every timer reads it
    PULL       `if (pulling) { AdvancePull(dt); return; }` right after the clock: the Grapple item owns
                 the whole frame (position curve + one cc.Move). See "Items".
    WALL SURGE IsWallSurging (now < wallSurgeUntil) scales WallRunSettings and skips both stamina calls
  → CharacterController.Move()
  ⇢ OnJumped / OnLanded / OnDashed / OnSlideStarted / OnSlideEnded / OnWallJumped / OnWallRunStarted / OnWallRunEnded
      → PlayerFeedback  → AudioManager (footstep/jump/land/dash/slide/wall jump)
                        → CameraFX (FOV kick), CameraShake, camera dip AND slide crouch on the pivot
                        → DashFx / SlideFx — see "Movement — slide and dash feel" below
```

**Invariants**
- Player movement reads `TimeScaleController.PlayerDelta`, never `Time.deltaTime`. Hitstop and slow-mo must not
  brake the player. This holds for the slide's decay and the wall jump too.
- **Every motor timer is measured against the motor-local `now`, never `Time.time`.** `now` advances by
  `PlayerDelta`; `Time.time` is the world clock, which hitstop drives to ~0.02×. On the world clock a dash
  that landed a hit kept travelling for the whole freeze and every window (dash, slide, coyote, jump
  buffer, cooldowns) grew by the hitstop length. Hitstop now neither freezes nor extends any of them.
- `Teleport(position, yaw)` must push yaw into `PlayerLook`, which owns rotation and rewrites it every frame.
- `PlayerLook` owns yaw/pitch; the motor owns position. Never both.
- **The movement path allocates nothing.** No LINQ, no closures, no per-frame arrays, no strings. The only
  physics queries beyond `CharacterController.Move` are `FindWall` (8 `SphereCastNonAlloc`, ONLY on the frame a
  buffered jump is resolved while airborne) and `CeilingBlocked` (one, ONLY on the frame a slide tries to end),
  both through one cached `RaycastHit[4]` and an explicit mask that excludes Player, Enemy and Interactable.
  Measured: `FindWall` **0 bytes over 20 000 calls, 2.36 µs each**; `CeilingBlocked` 0 bytes.
- **A slide keeps running on ground it has only just left** (`slideOnGround` = sliding && within `coyoteTime`).
  Load-bearing, not a nicety — see ENGINEERING-LOG, "a CharacterController moving 0.8 m in one frame is not
  grounded".
- **A slide never restores standing height under a ceiling.** `EndSlide` returns false and stays slid; the
  slide branch then holds speed at `slideEndSpeed` so the player always crawls clear rather than stalling.
- **A wall jump can never be an ordinary jump.** Refused on the ground and inside `coyoteTime`, so the ground
  jump always wins the press; refused on the same wall twice running (`sameWallCosineLimit`) so one face is not
  a free ladder; `maxWallJumps` bounds a chimney to one airtime's worth of climb.
- **Jump-cancelling a slide keeps the slide's speed.** `EndSlide` runs before the jump and never touches `hv`.
  That is the whole speedrun tech, and every optional fast line in `Level_01` is authored to it.
- **A burst is a decay, not a cruise.** Nothing sets a speed the air then keeps forever: a dash, a slide-jump
  or a run exit lands you above `airSoftCap` and the surplus bleeds on `exp(-airDrag t)`. You keep 17.6 of a
  22 m/s dash; you do not keep 22. `DecayExcess` is pure and framerate-independent (`StaminaTunablesTests`).
- **Speed is earned once, then preserved.** The slide boost fades with the speed you already carry and with
  every chained slide, so slide-jump-slide-jump is a rhythm that costs, not a free constant 16 m/s.

---

## Time scale

```
callers → TimeScaleController.Request(scale, duration, affectsPlayer)
                              .HitStop(seconds)        → affectsPlayer: false
   WorldScale  = min(all requests)          → Time.timeScale   (enemies, physics, VFX)
   PlayerScale = min(requests where affectsPlayer)   → PlayerDelta (movement)
```

| Effect | World | Player |
|---|---|---|
| Hitstop | 0.02 | 1 |
| Ultimate slow-mo | 0.25 | 1 |
| Pause / menus | 0 | 0 |

**Invariants**
- `TimeScaleController` is the ONLY writer of `Time.timeScale`. Never set it directly.
- All UI and feedback run on unscaled time so menus and hitstop never freeze the HUD.
- This system is **fundamentally incompatible with multiplayer** as written — a global time scale would freeze
  every player. See `multiplayer-system-design.md`.

---

## Combat: incoming attack

The core loop of the game. Every hit the player takes goes through exactly one path.

```
EnemyController.Update()
  Windup  → EnemyVisuals.Telegraph(attack, duration)      dim charge
  cue     → EnemyVisuals.CueFlash() + Sfx.ParryCue        ← THE parry signal, cueLead (0.28s) before impact
  Strike  → DoImpact(): distance + cone test
      → PlayerCombat.ReceiveAttack(AttackInfo)
            facing = angle(player.forward, toAttacker) <= facingConeDeg (75°)
            → ParryController.Resolve() → ParryMath.Evaluate(elapsed, perfect, late, facing, unblockable, GUARDING)
                 guarding = InputReader.ParryHeld (RMB DOWN) && not staggered/executing/drinking/ATTACKING
                 timing first, stance second — the guard only ever upgrades a would-be Hit:
                 Perfect → no damage, enemy posture, FULL Pyre gain, hitstop, flash   ⇢ ParryResolved
                           → `ParryController.NotifyDeflected()` closes into success recovery, then
                             `ParryImpact` resolves the rendered camera eye, applies the source-direction
                             kick / FOV / stepped release / layered audio, and `WeaponViewmodel.DeflectImpact()`
                             recoils from its live pose for both a tap and a held deflect
                 Blocked (timed, late press) → damage × 0.3, PLAYER posture × 0.9, Pyre × 0.35
                 Blocked (HELD GUARD)        → damage × 0.0, PLAYER posture × 1.5, NO Pyre,
                                               guardHitStop 0.05, guardShove 1.2 m, spark at the blade,
                                               WeaponViewmodel.GuardImpact() kicks the stance
                 Hit     → full damage, PLAYER posture × 0.5
            unblockable / facing away → Hit, whether or not the guard is up
      ⇢ ParryResolved → HUDController popup
      ⇢ PlayerHealthChanged → HUD health bar
```

Timing budget for one attack:

```
   windup start                cue                     impact
        |------------------------|----------------------->|
                                 |<---- cueLead 0.28s ---->|
        player reacts R after cue; elapsed at impact = 0.28 − R
        R in [0.15, 0.28] → Perfect      (perfect window 0.13)
        R in [0.03, 0.15) → Blocked      (late window 0.12)
        R > 0.28 or < 0.03 → Hit
```

**Invariants**
- Every enemy wind-up is ≥ 0.45s and the cue always fires `cueLead` before impact. Aggression may compress
  *dead time* but never the wind-up — otherwise attacks become unreactable.
- `cueLead ≈ human reaction (~0.20s) + half the perfect window`. Change one, re-derive the other.
- `PlayerCombat.ReceiveAttack` is the single funnel. No other code may damage the player from an enemy attack.
- Both **timed** outcomes stoke the **Pyre** meter; a held guard and a raw hit stoke nothing. See
  [Pyre and the super attack](#pyre-and-the-super-attack). Turtling must not charge the super.
- **The guard is resolved strictly after the timing and can only upgrade a Hit.** `ParryMath.Evaluate`
  runs the windows first; only a result that came out `Hit` is turned into `Blocked` by the stance. A
  hold can therefore never manufacture a Perfect, which is the single line that keeps the deflect
  strictly better than the guard and keeps the whole design pointing at the 0.13 s window.
- **`unblockable` goes straight through the guard**, exactly as it goes through a press. The pink
  `M_AlertTell` marker means "this one cannot be answered with steel — move"; a guard that ate it would
  make the loudest signal in the game a lie. Same veto for `facing`: a guard does not protect your back.
- **Your own swing drops your guard.** `ParryController.CanGuard` is false while `PlayerCombat.IsAttacking`,
  so "hold RMB, mash LMB" is not a free win. The viewmodel puts the stance back up when the swing ends.
- **One button, two reads.** `InputReader.ParryPressed` (`WasPressedThisFrame`) opens the window;
  `InputReader.ParryHeld` (`IsPressed`) holds the stance. No new binding — in Sekiro the deflect is not a
  different input from the guard, it is the guard pressed at the right moment.
- `ParryController.GuardHeld` is settable (`ReleaseGuardOverride()` hands control back to the button),
  following the `TryJump` / `TryDash` idiom, so `FeatureTests > Guard` can hold a stance with no device.
- **Deflect feedback uses the combat source rule.** Melee uses the attacker; a projectile uses the direction
  opposite its travel, so the kick, sparks and arc still agree with the actual hit after the runner has
  passed the sentry. `ParryImpact` resolves its eye once from `CameraFX` (then `Camera.main`, then player
  fallback): the hoop and directional kick must use the rendered eye, never the root at the player's feet.
- **A perfect has its own recoil, not the guard thud.** `WeaponViewmodel.DeflectImpact()` starts at the live
  pose and settles to guard or idle on `TimeScaleController.PlayerDelta`; it is valid for tap and hold.
  `GuardImpact()` remains the heavier blocked-guard-only response. The chromatic accent is 0.35 for 0.12 s:
  contact texture, not a veil over the next cue.

---

## Combat: outgoing attack

```
InputReader.AttackPressed → WeaponController.Update() → TryAttack()   ← public entry point
    staggered/drinking/parrying? → refuse
    staggered enemy in range?    → ExecuteInteractor.TryExecute() instead   (see Riposte)
    else StartSwing() → coroutine:
        AudioManager.Play(WeaponAudio.SwingSfx(w), ...)   ← weight-picked (2026-09-06 weapon-audio pass)
        wait hitDelay → Physics.OverlapSphere(cam + fwd*hitOffset, hitRadius, EnemyMask)
            → Health.TakeDamage(baseDamage × comboMult × PlayerStats.DamageMultiplier)
            → Posture.Add(weapon.postureDamage)
            → WeaponImpactFx.Hit(point, cam.forward, w, isFinisher)   ← presentation only
            → hitstop, shake, AudioManager.Play(WeaponAudio.HitSfx(w), ...)
        combo window → next swing
```

**Invariants**
- Player→enemy hits are instantaneous sphere queries (no projectiles, no hitbox colliders). This shape is
  deliberately server-authority-friendly.
- Damage scales through `PlayerStats.DamageMultiplier(weapon)`, never hard-coded.
- `WeaponAudio.SwingSfx` / `.HitSfx` pick `Sfx.Swing`/`Sfx.Hit` (sword, mid-weight, real CC0 clips) vs the
  synthesis-only `Sfx.SwingLight`/`Sfx.HitLight` (dagger) or `Sfx.SwingHeavy`/`Sfx.HitHeavy` (hammer) purely
  from `WeaponData.hitStopSeconds` — no per-weapon branch, no new data field.
- **The hit confirm is `WeaponImpactFx`, and it is drawn ONLY through `SlashFx`** (2026-09-06 weapon-look
  pass). Flare + a spark fan thrown back out of the wound + a shockwave ring for the maul alone; every
  size, count, speed and spread is interpolated from `WeaponImpactFx.Mass(w)`, which is
  `InverseLerp(0.22, 0.86, attackDuration)` — derived from the shipped ladder, never a new data field.
  It replaced an inline unpooled lit sphere at `neon * 4` (peak **3.51** on the hammer, louder than the
  alert tell). Peak is now exactly **1.0**: `SlashFx` normalises, so a hit you LANDED cannot bloom.
  Pinned by `Assets/Editor/Tests/WeaponImpactVfxTests.cs`.

---

## Viewmodel arms

The player has visible gauntleted arms. There is **no Animator and no rig import** — the arm is derived
every frame from the pose the viewmodel was already driving, so no authored pose or tuned timing changed
when the arms were added.

```
Camera
 ├ OffhandRoot        ← OffhandViewmodel poses THIS transform (rest / raised / cocked / thrust)
 │   └ OffhandModel
 │       └ HandL      ← gauntlet boxes; rigid child of the posed node
 │           ├ Grip   ← the wand / item instance parents HERE
 │           └ Wrist  ← marker the arm solver reaches for
 ├ OffhandArmRig      ← ViewmodelArm (left). Under the CAMERA, not under OffhandRoot: the offhand
 │   ├ UpperArm         root is itself swung across the screen, so a shoulder parented to it
 │   └ Forearm          would travel with the hand and the elbow would never bend.
 └ ViewmodelRoot      ← sway + bob (WeaponViewmodel.LateUpdate)
     ├ Model          ← attack / parry / drink / execute / wand poses
     │   └ HandR
     │       ├ Grip   ← the weapon (or the riposte override model) parents HERE
     │       └ Wrist
     └ ArmRig         ← ViewmodelArm (right), under ViewmodelRoot so sway/bob move arm + weapon as one
         ├ UpperArm
         └ Forearm
```

```
WeaponViewmodel.SetWeapon / ShowOverride
    Instantiate(prefab, grip)                    weapon is a RIGID CHILD of the hand
    CloseHandOn(instance):
        p = model.InverseTransformPoint(prefab's Grip* part)
        hand.localPosition = p ;  grip.localPosition = -p
        → the hand slides onto the hilt, the weapon does not move a pixel

every pose coroutine → ApplyPose(model)          unchanged; now carries the hand and weapon

ViewmodelArm.LateUpdate  [DefaultExecutionOrder 200 — AFTER both viewmodels]
    shoulder = rigRoot.TransformPoint(shoulderLocal)
    two-bone IK shoulder → wrist.position, elbow broken along poleLocal
    out of reach → straighten and STRETCH both bones (up to maxStretch) to still meet the hand
    PlaceBone: world position/rotation + localScale (thickness, thickness, length)
```

**Invariants**
- **The weapon is parented to the hand, never posed beside it.** Nothing in the animation path can
  separate the two — the arm only ever draws two bones to wherever the hand already is. This is why the
  arms cost zero retuning.
- **Hand position comes from the weapon prefab's `Grip*` part, at runtime.** Every weapon hangs its hilt
  at a different height; a fixed hand position grips one of them and empty air for the rest. A prefab
  with no `Grip*` part (item shards) is simply held in the middle of the palm.
- **Swapping a weapon does not free the old one until the frame ends.** `SetWeapon` releases the previous
  model with `Destroy(instance)`, which Unity defers. Equipping and rendering in the *same* frame draws
  **both** weapons stacked on each other — this is a screenshot/tooling trap, not a prefab bug. Anything
  that equips and then captures must let a frame pass (`ViewmodelCapture.LoadoutTour` does).
- **Four weapon lengths, one framing rule.** Extent above the fist (prefab height above `Grip*` ×
  `viewmodelScale`, as `DataFactory` ships it): Rosethorn **0.32 m**, Oathbreaker **0.50 m**, Cerulean Edge
  **0.62 m**, Verdigris **0.72 m** — a 2.3× spread where the dagger pass had 1.2×. Length is free because
  it is no longer what keeps the frame readable: `WeaponSilhouette` rasterises the real prefab from the
  player's eye and `WeaponSilhouetteTests` holds every shipped weapon in every held pose to (1) never
  crossing the crosshair disc, (2) never covering more than a small fraction of the frame, (3) tip inside
  the frame. Long blades are held further out and canted across the lower-right corner, not vertical.
  Silhouettes and per-weapon `viewmodelScale`:
  [`ARCHITECTURE.md > The blade family`](ARCHITECTURE.md#the-blade-family--weapon-viewmodels).
- **The arm solver runs at execution order 200.** At the default order it can solve before the pose is
  written and the arm trails the hand by a frame, which is precisely what makes a viewmodel look detached.
- **Bones stretch rather than clamp.** The authored swing poses reach ~0.96m from the shoulder while the
  rest pose is ~0.74m; a hard IK clamp would leave the wrist short of the hand and visibly detach the arm.
- **The shoulder sits well off the camera axis** (x ±0.22, y −0.40, z −0.10). The arm must cross the 0.03
  near plane on its way back to the body; out there the cut happens outside the frustum instead of as a
  hole in the middle of the frame.
- Sway/bob use `TimeScaleController.PlayerDelta` (rule 1) — they are driven by the player's own movement
  and now carry the whole arm. The **swing** clock stays scaled on purpose: hitstop freezing the arm
  mid-swing is the impact device.
- Rule 9: hand geometry, grip/wrist markers, bone lengths, thicknesses, shoulder anchor and pole all live
  serialized on `Player.prefab`, so `PrefabFactory` writes every one of them explicitly.
  `FeatureTests > ViewmodelArms` asserts the shipped rig.

---

## Swing trail

The arc of the strike, drawn behind the blade tip. It exists because the weapons are a short dagger
family and this project has **no motion blur**: a 0.22 s arc is a dozen frames of a small object moving
fast and nothing smears. It is also a **mechanical readout** — it is open for the strike leg and nothing
else, so the ribbon is the player's read on when the weapon is dangerous.

```
WeaponViewmodel.AttackCo
    ... anticipation leg (EaseIn) ...
    trail.BeginStrike()                     ribbon cleared, recording opens
    ... strike leg (EaseOut, fast) ...
    current = end ; ApplyPose(current)      land exactly on swingEnd FIRST
    trail.EndStrike()                       takes ONE FINAL SAMPLE, then closes
    ... follow-through hold, then recovery ...

WeaponTrail.LateUpdate      (same GameObject as WeaponViewmodel — the pose is written in Update,
                             so LateUpdate always reads the pose that was actually rendered)
    emitting → Sample(): space.InverseTransformPoint(viewmodel.TipWorldPosition)
               `Tip*` renderer hierarchy resolved once per held model; live bounds read each sample
               movement below 2.5 mm accumulates without consuming the finite point budget
               plus `subdivisions` interpolated points toward a real sample, pushed newest-first
    fading   → widthMultiplier *= fade, material alpha = fade², and the tail RETRACTS
               (count → ceil(peak * fade)) so the arc closes instead of hanging there
    LineRenderer (useWorldSpace = false) under the CAMERA, additive URP/Unlit from
    SlashFx.CreateAdditiveMaterial
```

**Shipped values** — written explicitly by `PrefabFactory` on `ViewmodelRoot` (rule 9): 12 points,
3 subdivisions per frame, head width 0.030 m, width curve `1 → 0.5 @0.3 → 0.18 @0.65 → 0.03`,
fade 0.11 s, peak channel **1.15**.

**Those two are the SWORD's values, and the mass curve bends them around it** (2026-09-06 weapon-look
pass). `WeaponTrail.ApplyMass` multiplies the authored width and fade by `WidthScale`/`FadeScale` of
`WeaponImpactFx.Mass(w)`, so the ribbon carries the weapon's weight in the one channel guaranteed to be
on screen at contact: dagger x0.60 / x0.80 (0.018 m, 0.088 s — a whip), sword x1.00 (0.030 m, 0.110 s —
unchanged, it is the reference), hammer x1.76 / x1.38 (0.053 m, 0.152 s — a slab that hangs a beat).
The authored fields are never written to, so `FeatureTests > Trail_WidthCapped / _FadeIsShort` still read
the shipped numbers; the products are pinned by `WeaponImpactVfxTests`.

**Invariants**
- **Camera space, not world space — this is why there is no `TrailRenderer`.** A `TrailRenderer` emits in
  world space, which is right for a sword in the world and wrong for a viewmodel: turning the mouse
  mid-swing would leave the ribbon hanging in the world and drag it across the frame. Points are recorded
  in the **camera's** local space (not `ViewmodelRoot`'s, which carries sway and bob and would make the
  ribbon swim while you run).
- **The ribbon spans the strike leg exactly.** Never the wind-up (nothing is dangerous yet), never the
  recovery (nothing is any more). `FeatureTests > Trail_SilentDuringWindup / _LiveDuringStrike /
  _ClosesAfterStrike` assert all three.
- **`EndStrike` samples once more before it closes**, and the caller must apply `swingEnd` first.
  Sampling happens in LateUpdate and the swing loop ends in Update, so without that final sample the
  newest point is a frame behind the arc and the ribbon renders visibly **detached** from the blade.
- **Width is the only head-to-tail channel.** URP/Unlit ignores vertex colour, so there is no per-vertex
  alpha; the taper curve has to carry "this end is older" on its own.
- **1.15 peak, and it does not move.** Over the 1.05 bloom threshold so it glows, under the ~1.25 where
  ACES desaturates a saturated hue toward orange, and far under the alert tell (3.00) and the deathblow
  mark (2.60). Those are alarms; this is flourish.
- The hue is the equipped weapon's `WeaponData.neon`, normalised — heard on `GameEvents.WeaponChanged`,
  with a fallback read off `WeaponController.Current` for the equip that happened before the subscribe.

---

## Posture and deathblow

Two independent implementations with the same idea — do not merge them, they have different rules.

```
ENEMY   Posture           built by: player parries (big), player hits (small), Ultimate (huge),
                                    Grapple onto an unstaggered Legendary_* / boss (35% of max)
        regen after delay, scaled by health fraction (hurt enemies recover posture slower)
        full → Break() → State.Staggered → deathblow window
                          EnemyVisuals.Slump(true)
                              THE BODY BUCKLES: leans BACK -13 deg, rolls 9, sags 0.20 down and
                              0.14 away, arms flung back and OUT. Nothing travels toward the player.
                          → SetDeathblowReady(true) → the deathblow SPOT goes up ON THE STERNUM: one
                              small flat billboarded quad (M_DeathblowMark, violet, blooms), held to a
                              CONSTANT ANGULAR size and stepped onto the body surface toward the eye.
                              It stays until the window closes for any reason:
                              HandleStaggerEnded / BeginExecuted / Die all drop it — so it is already
                              gone before the wand thrust and can never foul the stab.
                              EnemyController.DeathblowPoint(eye) is that same point, and it is where
                              the commit shatter and the wand blast are drawn
        ⇢ (boss only) BossPostureChanged → HUD

PLAYER  PlayerPosture     built by: HELD GUARD (damage × 1.5), timed BLOCK (× 0.9), a raw hit (× 0.5),
                          unblockable ×1.5 on top
        perfect parry costs NOTHING  ← the whole point
        regen after delay, faster at high health,
        and SUSPENDED ENTIRELY while ParryController.IsGuarding (guardPostureRegenMultiplier 0)
        full → Break() → 1.5s stagger: 0.4× move speed, no jump/dash/attack/parry/flask/ultimate,
                          and 1.6× damage taken
        ⇢ PlayerPostureChanged / PlayerPostureBroken → HUD
```

**Invariants**
- Perfect parry is the only sustainable answer: blocking is a resource, not a free option.
- **The guard charges posture, not health, and that is the reason this bar exists.** Chip damage is 0
  (the Sekiro contract for a normal attack) and the posture bill is the largest multiplier in the game
  — 1.5, against 0.9 for a timed block and 0.5 for a raw hit. The ladder stays monotone from the
  player’s side: **deflect** (nothing, +Pyre, +enemy posture) > **guard** (posture only) > **hit**
  (health *and* posture). Three guarded 20-damage hits fill a 100-posture bar.
- **Posture does not regenerate behind a raised blade.** `PlayerPosture.Update` returns early while
  `ParryController.IsGuarding`. Without this a player holds RMB forever: guarded hits cost only posture,
  and posture that regenerates through the guard makes the fight an unloseable, unwinnable stalemate.
  Guard-break is therefore reachable by turtling alone, and it is the punish that ends the strategy —
  1.5 s of 0.4× speed, no parry and 1.6× damage taken, which a grunt converts into ~64 of your 100 HP.
- **The guard cannot be re-raised through its own break.** `CanGuard` is false while
  `PlayerCombat.IsStaggered`, so the stagger cannot be turtled out of.
- `Posture.Add` is a no-op while broken, so a stagger cannot be extended by piling on hits.
- **A posture break must show ON THE ENEMY, not only on the HUD.** During an exchange the player's eyes
  are on the enemy's cue flash at the centre of the screen; a prompt at the edge of the frame is a
  confirmation, never the signal. The signal is now the **pose** — the body buckles, sags and opens —
  plus the stagger-tint breath and the world posture bar, not a drawn marker.
- **The break must never move geometry toward the player.** `Slump` used to pitch the body *forward*
  28 degrees, straight at the eye, because the enemy is facing the player and `BeginExecuted` turns it
  to face them again. Over a 2 m body that walks the chest almost a metre closer, and the deathblow's
  own step-in then closes to `stabStandoff`. Every component of the stagger pose is signed away from
  the camera, and `FeatureTests > Deathblow_StaggerPoseClearsNearPlane/StaysFramed` measure the nearest
  enemy renderer to the eye at both 1x and 2.2x scale.
- **The mark is a flat spot ON the body, and its size is ANGULAR.** One billboarded quad, no volume,
  nothing for the camera or the wand to run into. It started as a crossed diamond of three cubes over
  the head; that was a blob. The size is the subtler half: the spot stands `surfaceOffset` metres *off*
  the chest toward the viewer — it has to, or it renders inside the mesh — so it is always nearer than
  the body it marks and a **fixed**-size quad grows faster than the enemy does as the player closes. At
  0.22 m fixed it filled a quarter of the frame at stabbing range while the grunt filled a fifth. Held
  to a constant angular size (~5% of the frame) it reads the same at three metres and at one, exactly as
  the lock-on dot already does.
- **`DeathblowMarker` is also the anchor.** The same sternum point is what `ExecuteInteractor.CommitCue`
  shatters at and `WandController.FireRiposte` blooms the blast from, so the mark, the shatter and the
  explosion land on the same pixels. `drawMark` exists so it can be turned off without losing that.
- **It is dropped on the press frame.** `BeginExecuted` calls `SetDeathblowReady(false)` before the melee
  commit, so the spot is gone long before the wand reaches the body — it says "press now", and once the
  press has happened the commit shatter takes over that exact point.
- **Anything drawn at a body's centre of mass is inside the mesh.** The anchor is offset
  `surfaceOffset` metres toward the eye — 0.80 for a capsule, 0.90 for the Iron Penitent, 1.00 for the
  Ashen Chorister's robe, each measured from the model's own depth at build time.

---

## Riposte inscriptions and the spellbook

```
enemy posture breaks → EnemyVisuals.Slump(true) → the body BUCKLES back and down (never forward)
                     → SetDeathblowReady(true) → the deathblow SPOT goes up on that enemy's sternum
                       (this is the Sekiro mark: on the body, not on the HUD, raised by the BREAK and
                       not by where the player is looking). It is also the point the whole beat is
                       aimed at, and its lifetime is exactly the window.

enemy staggered + in cone → ExecuteInteractor.Update() → Target / HasMarkedTarget
                            ⇢ DeathblowReady(bool) → HUD banner (boss only)
                            ⇢ PromptChanged("DEATHBLOW  [ATTACK]")        ← names the INPUT
    inscription cooling? ⇢ PromptChanged("DEATHBLOW  [ATTACK]  WAND 3.2s")  ← legacy label; says so

InputReader.AttackPressed → WeaponController.TryAttack()
    ExecuteInteractor.TryExecute()  →  false when HasMarkedTarget is false  →  ORDINARY SWING
                                    →  true  only against a MARKED enemy   →  deathblow
    TryExecute() → ExecuteCo:
    CommitCue(e)  ← the frame of the press, before any of the deathblow's own timing:
        the SPOT SHATTERS — violet flare + sparks burst at e.DeathblowPoint(eye), the
        sternum on the SURFACE facing the player, i.e. exactly where the mark was standing
        the frame before — a beam runs WeaponViewmodel.TipWorldPosition → that point,
        ChromaticPulse(0.3),
        and the audio is Sfx.Execute at 0.62 pitch, NOT Sfx.Swing
      ← this is what makes a deliberate press feel deliberate; without it the first
        0.18 s of a deathblow was pixel- and audio-identical to the swing the player
        thought they had thrown
    inscription = WandController.WandReady ? Current : null ← compatibility type remains WandData
    player invulnerable, CanMove = false, enemy.BeginExecuted()
    melee commit (wandCommit 0.18s)
    StepInCo: CharacterController.Move toward the victim, stopping at stabStandoff (2.2m x enemy scale).
              It only ever CLOSES the gap, and the press had to be inside range (3.5 m) — so on a
              2.2x boss the real riposte distance is the RANGE, not the standoff, and that is the
              tighter framing the stagger pose has to survive.
    → WandController.FireRiposte(target, weapon.executeDamage)
          readyAt = unscaledTime + wand.cooldown   ⇢ WandCooldownChanged → HUD WandCooldownBar
          OffhandViewmodel.PlayThrust(windup, hold, recover)
              persistent SpellbookVisual raises and casts; selected inscription colours the runic orb
              above the open pages; the book instance is never replaced or rescaled
          ⇢ RiposteLanded(target)        ← raised BEFORE damage, so listeners can read the victim
          THE BLAST IS DRAWN FROM THE TIP, NOT ON THE VICTIM — and CONTACT is the surface of
          the chest, e.DeathblowPoint(eye), not its centre. `origin + up*0.95` was the middle of
          the body: every flare drawn there rendered INSIDE the enemy and was never seen.
              OffhandViewmodel.MuzzleFlash()             tip light blows out, lighting the victim
              SlashFx.Flare(tip)                         muzzle glint at the wand
              SlashFx.Beam(tip → contact)                the bolt leaving the wand
              SlashFx.Ring(contact, facing the eye)      the blast blooming out of the wound
              Lance also: SlashFx.Beam(contact → contact + aim * blastRadius)
              SlashFx.Sparks(contact, back along the beam) + Flare(contact)
          damage by WandKind:
              Bolt    single target
              Scatter target + splash in blastRadius
              Chain   target + arcs to 3 (LightningEffect.Strike)
              Lance   pierces along a ray
          only the RIPOSTEE gets isExecute = true
          hitstop, shake, FovKick(-13) → +6, ChromaticPulse(0.4), ScreenFlash(0.20 / 0.16s)
    → WeaponViewmodel.ClearOverride(), enemy.EndExecuted(), control restored
```

**Invariants**
- Only the ripostee takes `isExecute` damage. Splash/chain/pierce is ordinary damage, so an AoE can never
  deathblow a bystander boss.
- `RiposteLanded` fires before the damage — listeners depend on reading the victim while alive.
- `WandData.windup/recover` still set the riposte cadence; the name is serialized compatibility, while
  the player-facing presentation is a selected spellbook inscription.
- Falls back to the original melee deathblow when no wand is equipped **or while the wand is cooling**.
- **The deathblow is a PRESS, and the press must announce itself.** `TryExecute` returns false whenever
  there is no marked target, so an attack press away from a broken enemy is always just a swing. Against
  a marked one it consumes the press — and `CommitCue` exists so the player can tell those two apart on
  the frame they happen. `FeatureTests > Deathblow` asserts both directions.
- **The marker is raised by the posture break, not by the interactor.** The interactor decides whether
  *this press* would land; the glyph says *this enemy is killable*. Gating the glyph on the aim cone
  would hide the state that the player needs in order to decide to turn and look.
- **The cooldown gates the BLAST, never the deathblow.** Emberlance 3.5 s, Stormneedle 5.5 s,
  Gravecall 7 s, Voidspine 9 s — every one of them longer than the boss's 5 s deathblow window, so a
  strict gate would have made the boss unkillable through no fault of the player. It starts at the
  discharge, not at the end of the recover, so a slow wand's own cadence is inside its own wait.
- The cooldown is on the CONTROLLER, not per wand asset: cycling with `R` mid-fight does not refresh it.
- **A cooling wand must say so.** `ExecuteInteractor` appends the remaining seconds to the `EXECUTE`
  prompt. A riposte that quietly comes out as melee reads as a broken wand.
- **Every element of the blast is anchored to the book's `CastOrigin`.** `TipWorldPosition` remains the
  compatibility API and resolves to `SpellbookVisual.CastOriginWorldPosition` while the book is present.
- **The thrust pose finishes near SCREEN CENTRE**, `thrustPosition (-0.07, -0.09, 0.80)` /
  `thrustEuler (66, -6, 4)`, written in `PrefabFactory` (rule 9). A viewmodel wand can never physically
  reach 2.2 m, so the stab is sold by the tip visually *landing on* the victim's chest — which only
  happens if it travels inward, not if it finishes off to the left where the wand rests.
- **The contact point is on the body SURFACE, never its centre.** One source of truth,
  `EnemyController.DeathblowPoint(eye)`, shared by the glyph anchor, the commit shatter and the blast,
  so the three land on the same pixels instead of near each other.
- **The camera must be able to FRAME the victim.** `ExecuteInteractor.stabStandoff` is a presentation
  value as much as a gameplay one: under ~2m the camera ends up inside a 0.45m-radius capsule at 95° FOV
  and the entire riposte plays behind a wall of black.
- **The book remains open and readable in motion.** Ten fixed leaves flutter on `PlayerDelta`, three loose
  pages orbit without physics, and a core plus eight separated runes show the current inscription/item.
  Parchment self-light stays below bloom; only the spell orb owns the hot channel.
- Screen flash and chromatic aberration are capped low on purpose. Both were previously loud enough
  (0.55 alpha, 1.0 chroma) to destroy the wand they were meant to punctuate.
- Poses, scales and the standoff all live on the Player prefab or on `WandData`, so **`PrefabFactory` and
  `WandFactory` write them explicitly** — see AGENTS.md rule 9. `FeatureTests > WandReadability` asserts it.

---

## Pyre and the super attack

The parry charge meter, and the payoff it buys. Replaced "parry juice" and the weapon-agnostic
Overdrive burst: same slot on the HUD, same `Q` binding, completely different economy.

```
PlayerCombat.ReceiveAttack
    Perfect → PlayerResources.AddPyre(PlayerStats.PyrePerPerfect + weapon.pyreBonus)
    Blocked → PlayerResources.AddPyre(same × PlayerStatsData.pyreBlockFraction 0.35)
    Hit     → nothing
  ⇢ PyreChanged(current, max) → HUD PyreBar + the "<SUPER> READY [Q]" banner at full

WeaponEmber.LateUpdate  (attached at runtime by PlayerCombat.Awake, DisallowMultipleComponent)
    charge = MoveTowards(charge, PlayerResources.PyreRatio, chargeLerpSpeed)   ← the fire LAGS the bar
    heat   = InverseLerp(deadzone 0.02, 1, charge)
    EnergyGlow.SetTint(Lerp(weapon.neon, emberHot × (1 + heat×1.6), heat × 0.8))   ← ASKED, not written
    EnergyGlow.SetCharge(heat × 0.55)                                              ← capped well below 1
    embers: pooled cubes shed along the blade, spawn rate ∝ heat², drifting up in viewmodel space
    light:  point light at 0.6 × blade length, intensity ∝ heat², range 0.5 → 2.4

InputReader.UltimatePressed → UltimateAbility.Update → TrySuper()
    refuse unless PyreFull, a weapon is equipped, and not executing/staggered
    ConsumePyre() (the bar's ONLY sink)  ⇢ UltimateUsed → HUD banner names the super
    TimeScaleController.Request(ultSlowScale 0.25, total, affectsPlayer:FALSE)  ← world crawls, you do not
    WeaponViewmodel.PlayAttack(0, superWindup + superActive, superWindup)
    wait superWindup → superHits × ApplyHit (evenly spread over superActive) → wait superRecover
        ApplyHit: OverlapSphere(superRadius) ∩ cone(superArcDeg) around PlayerLook.AimForward
                  Health.TakeDamage(superDamage × PlayerStats.DamageMultiplier(weapon))
                  Posture.Add(boss ? Posture.Max × ultBossPostureFraction / hits : superPostureDamage)
                  OnParried(0), NavMeshAgent shove by superKnockback
        VFX is anchored to WeaponViewmodel.TipWorldPosition:
              FireSlashFx.Play(tip, aim, ember hue, superRadius, superArcDeg, hit progress, lifetime)
              one broad hot edge + torn fringe + embers per authored hit; multi-hit weapons walk the
              edge across the same authored arc
        FireSlashContactCo waits for the visible edge beat, then applies the unchanged damage/posture/shove
    hitStop(superHitStop), shake(superShake), Release()
```

| Weapon | Super | Kind | Dmg × hits | Posture | Reach | Arc | Wind-up |
|---|---|---|---:|---:|---:|---:|---:|
| **Cerulean Edge** (sword) | Emberfall Arc | Cleave | 150 × 1 | 70 | 5.5 m | 170° | 0.30 |
| **Rosethorn** (dagger) | Thornstorm | Flurry | 34 × 9 | 26 ea | 4 m | 70° | 0.14 |
| **Verdigris** (hammer) | Bronzefall | Quake | 200 × 1 | 130 | 7.5 m | 360° | 0.52 |
| **Oathbreaker** (dev) | Oathbreaker | Nova | 600 × 1 | 400 | 12 m | 360° | 0.06 |

**Invariants**
- **The Pyre bar does not decay.** Fights in this game are separated by long stretches of parkour; a
  bar that bleeds out between arenas would tax the traversal the game is built around. It has exactly
  one sink (the super, which spends it in full) and one reset (`PlayerRespawned`).
- **A perfect deflect must always be worth strictly more than a block.** `pyreBlockFraction < 1` is
  what keeps the tighter window worth chasing. `FeatureTests > Parry_BlockStokesPyreLessThanPerfect`.
- **The fire slash leaves the WEAPON.** Every sheet begins at `WeaponViewmodel.TipWorldPosition`; damage
  follows its contact beat instead of preceding the visible effect. `PyreArc`, `LightningEffect` and the
  dormant `BoltCo` remain intact as the preserved electrical-flight library for a future enemy.
- **Every super is data.** `UltimateAbility` reads `WeaponData` and nothing else; a new weapon gets a
  super by authoring `super*` fields in `DataFactory`. `SuperKind` chooses presentation, never geometry.
- **The fire is the primary read on charge.** The HUD bar is the confirmation, not the signal — the
  weapon must be legibly hotter at 60% than at 30% with the HUD covered.
- **`WeaponEmber` never writes `_EmissionColor` on the blade.** `EnergyGlow` owns that channel and
  rewrites it every `LateUpdate`; the ember asks through `SetTint`/`SetCharge`. Its own pooled embers
  are separate renderers, and they borrow the blade's material because a stock primitive material has
  `_EMISSION` off and would swallow the write.
- Rule 1 holds: the time request is `affectsPlayer:false` and every wait is realtime, so a super's
  hitstop cannot freeze the swing that caused it.

---

## Items

```
ItemPickup (trigger, layer Interactable)
  OnTriggerEnter ← player CharacterController
  → PlayerItems.TryPickup(ItemData)     capacity 3, FIFO
  ⇢ ItemPickedUp → HUD toast     ⇢ ItemsChanged → HUD slot row

  ⇢ ItemsChanged → StatusStripView (top-left strip: held list, front item marked)

E → PlayerItems.UseCurrent (InputReader.UseItemPressed; FIFO, no swap step)
  → Apply(item) FIRST — returns false to REFUSE, and a refused item is kept and nothing is announced
  → held.Remove → ⇢ ItemsChanged, ⇢ ItemUsed, Sfx.ItemUse

  Grapple  → FindGrappleTarget: LockOnController.Target if in range + LOS,
                                else OverlapSphereNonAlloc(EnemyMask, 28 m) → nearest to the crosshair
                                inside 12° with Physics.Raycast(motor.WorldMask) clear
             none → prompt "NO TARGET" (0.6 s), Sfx.Click, return false (NOT consumed)
           → ItemVfx.GrappleLine(offhand tip → e.DeathblowPoint), OffhandViewmodel.PlayUse, FOV kick
           → FirstPersonMotor.BeginPull(StandoffPoint(e), 0.35 s, rewardArrival = !e.data.isTurret)
                StandoffPoint = enemy feet + (player side, flat) × ExecuteInteractor.stabStandoff × data.scale
                motor.Update: `if (pulling) { AdvancePull(dt); return; }` — the item OWNS velocity:
                  position curve start→target, sine arc (0.35..2.2 m), ease-out, on the motor clock;
                  one cc.Move per frame; ends on arrival, on a Sides/Above collision, or CancelPull
                  (Teleport calls it). ⇢ OnPullEnded(arrived). Slide/wall run/dash cancelled on entry.
           → turret path, DURING the pull:
                a real matching Projectile reaches PlayerCombat.ReceiveAttack
                → ParryController may upgrade Hit → Perfect only when E-to-contact ≤ grapplePerfectWindow 0.13
                  and projectile.shooter == GrappleTarget; no global RMB window is opened
                → Projectile pays its normal deflect speed first
                → PlayerItems.CompleteHookPerfect kills through Health and PrimeHookDashJump()
                → the next legal airborne dash may be jumped out of once; that jump gets capped horizontal
                  carry + 1.12× rise. Pull arrival, early/late E, or another shooter's bolt pays nothing.
           → non-turret path, only after a successful arrival (e alive):
                PlayerItems.IsBig(e)  = BossController, or name / data.name starts "Legendary"
                big && !IsStaggered   → e.Posture.Add(0.35 × Posture.Max); flare + sparks; DONE
                otherwise             → if (!IsStaggered) e.Posture.Break()   → HandleBroken → State.Staggered (sync)
                                        → ExecuteInteractor.ExecuteNow(e)     → the SAME ExecuteCo as a pressed
                                          deathblow: wand riposte (WandController raises RiposteLanded) or
                                          melee execute (interactor raises it), isExecute damage, hitstop
  Rebound → ArmRebound(1.18×, +3 m/s); duplicate activation refuses and keeps the pickup
          → next successful AIRBORNE dash or wall jump consumes it
          → horizontal exit is strengthened under maxHorizontalSpeed, air dash is refreshed
          → failed input and ground dash retain it; death / respawn / teleport clears it
  DeflectSigil → armed until the next ParryResolved(Perfect); duplicate activation refuses and keeps it
               → Block / Hit retain it
               → Perfect consumes it, adds two EXTRA general ParrySurge stacks and +5 m/s look impulse
               → death / respawn / disable clears it

  `ItemEffect.WallSurge = 1` remains a retired serialization tombstone. The factory deletes its asset and
  level authoring migrates every old pickup key to Rebound or DeflectSigil; value 1 is never reinterpreted.

THE PROMPT LINE — two channels (2026-09-06)
  GameEvents.PromptChanged (owner, string) → PromptView.standing : a cue true while a condition holds
        "DEATHBLOW  [ATTACK]" (ExecuteInteractor), "GRAPPLE  [DASH]" (FlareGrapple),
        the level editor's PLAYING line. Every writer is EDGE-TRIGGERED (raises only when its own string changes).
  GameEvents.PromptFlash (string, seconds) → PromptView.flash : momentary, drawn OVER the standing cue and then
        gone — "PERFECT", "WALL EXIT [SPACE]", "DASH JUMP [SPACE]" (PlayerFeedback),
        "REBOUND ARMED" / "SIGIL AWAITS A PERFECT" (PlayerItems), "NO TARGET" (PlayerItems).
  PromptView.Current = flash while unexpired, else standing. Unscaled time. Before the split there was one
  channel, so a PERFECT erased a live GRAPPLE cue permanently (nothing re-raised it). FeatureTests: Prompt_*.
  THE OWNER KEY (2026-09-06, the user's call, closing the residual the split left): every standing write
  carries a PromptOwner key — execute / grapple / surge / pedestal / sandbox / leveleditor / debug, "" being
  unowned. PromptView.AcceptsStandingWrite(currentOwner, writer, text) is the whole rule and is PURE:
        non-empty text  → always accepted (last speaker wins, exactly as before)
        empty text      → accepted only if the writer still HOLDS the line, or nobody does
  so an old timed owner expiring can no longer blank a live "GRAPPLE  [DASH]" that will never re-raise itself. Clearing
  releases the line back to unowned. PromptView.StandingOwner exposes the holder. Pinned by PromptOwnerTests
  (the pure rule, EditMode) and FeatureTests Prompt_OwnerTakesTheLine / _AnotherOwnersClearIsIgnored /
  _AnonymousClearIsIgnoredWhileOwned / _TheHolderMayClear (the view obeying it, play mode).

GameEvents.PlayerRespawned → every ItemPickup re-enables; PlayerItems clears

THE SENTRY FLARE (2026-09-06; parkour_enemies)
  Posture.OnBroken on a pshooter_* → SentryBurst.HandleBroken → pendingBurst, resolved NEXT frame in Update:
      still alive and not Executed → Detonate() (kills through Health.TakeDamage: souls, EnemyKilled, mist) + the flare.
      A parkour enemy NEVER waits to be finished (user, 2026-09-06).
  Health.OnDied on the blue pshooter_enemy01 → SentryBurst.HandleDied: not Executed / DiedExecuted
      → Detonate() (the flare). Mechanical Heavy/Surge turrets carry neither SentryBurst nor posture UI.
  THE EXCEPTION: the grapple hook (PlayerItems.PullCo: Break + ExecuteNow in one frame, State.Executed, then the
      killing blow) -- EnemyController.DiedExecuted -- throws NO flare. The finish is the reward, not a lift.
  Detonate(): SentryFlare.Spawn(chest, forward, up 9, out 3, gravity 4, life 4.5) + the burst (2.4 m flash,
      2.6 m ring shockwave, sparks, Sfx.Detonate, shake). The flare is optional traversal.
      Sfx.Detonate NOT Sfx.Thunder (2026-09-06 audio pass): Thunder is the Stormbreak ultimate and must
      stay the loudest thing in the game; a sentry can detonate many times a level.
  SentryFlare.Update (scaled time): position = FlareMath.Position(origin, v, g, t) -- floats up ~10 m, hangs, sinks;
      glow = 1 - t/life drives size and colour; Grappleable while glow > MinGrappleGlow 0.08; gone at life.
      SentryFlare.Live is the registry.
      THREE concentric additive shells (2026-09-06 polish pass), all driven in WORLD space with the parent
      scale divided out: Core 1.40 m @ peak 1.45, Halo 2.30 m @ 0.24, Outer 3.60 m @ 0.12 -- summed 1.81,
      under the 2.0 ACES ceiling. The peak came DOWN (was 1.6) to pay for the size, so the flare is now the
      BIGGEST thing on a span and the bolt (1.6) is still the BRIGHTEST. Core flickers at 3.7 Hz, Outer
      breathes at ~0.5 Hz -- two rates, one object.
      SHAPE = SpawnPop(age) x DeathContract(age, life), multiplying core + both shells together: a 0.18 s
      arrival from 0.18x with a ~6% overshoot, and a 0.35 s collapse that begins only AFTER the flare has
      stopped being grappleable. A natural expiry is silent; only Consume() bangs (ring + flash + sparks).
      Trail: a 6-point recorded history (ring buffer, no allocation) -- it used to be a 2-point straight
      tangent, which pointed along the current velocity on a parabolic path.
THE SURGE TURRET -- pshooter_enemy03 (2026-09-06; parkour_enemies)
  "a small little circle shaped turret that just shoots the player and dies in one hit but boosts the
   player's speed when they parry" (the user). It lives in a ROW on a long descending ramp: a target, not
   a duel. Same bolt, same 0.28 s cue lead, same ProjectileShooter as every other span enemy.

  THE BRAIN IS A SUBCLASS.  SurgeTurret : EnemyController  (the BossController precedent; nothing in
    Enemies/Core changes, and GetComponent<EnemyController>() finds it exactly as it finds the boss).
    OnParried is the ONLY signal that says "the player perfectly deflected a bolt *I* fired" -- PlayerCombat
    calls it on AttackInfo.attacker, and Projectile puts the shooter there.

  PlayerCombat.ReceiveAttack → ParryResult.Perfect → attacker.OnParried(postureDamage)
    → for every NON-SurgeTurret attacker, PlayerCombat grants the shared player-authored ladder:
       ParrySurge.Grant(motor, stats.generalParrySurgeStep, generalParrySurgeMaxStacks,
         generalParrySurgeSeconds). Shipped: +0.12, cap 5, one-stack decay every 2.0 s. A new
         qualifying Perfect refreshes the decay deadline; misses, blocks and hits do not reset it.
    → SurgeTurret.OnParried owns the opening ramp's existing dedicated ladder instead, so it is
       excluded from the shared grant and never double-pays: base first (recoil, posture), then
       postureDamage <= 0 → RETURN.  That is UltimateAbility line 261's courtesy call, not a parry; the
         super must not hand out the whole ladder for free.
       → ParrySurge.Grant(motor, data.parrySurgeStep, parrySurgeMaxStacks, parrySurgeSeconds)
         (shipped ramp timing remains +0.12, cap 5, 1.4 s one-stack decay)
       → Sfx.Tick at a pitch that RISES with the stack (audio only: no new HUD element)
    ...and independently, inside Projectile as for any sentry, the deflected bolt turns around, shoves the
    player along their look (parrySpeedGain 9) and comes home to kill the 1 HP body. The parry IS the kill.

  ParrySurge (on the PLAYER, added at runtime by the first eligible Perfect -- the Player prefab is untouched)
    the ONLY driver of FirstPersonMotor.SpeedMultiplier (FirstPersonMotor.cs:301), the existing item-speed
    hook nothing shipped had used. No motor entry point added, no velocity written (hard rule 10), no new
    resource. StatusStripView renders its actual stack count as "SPEED SURGE xN".
    stacks = SurgeMath.Grant(stacks, max)          one per deflect, capped
    SpeedMultiplier = SurgeMath.Multiplier(stacks, step) = 1 + stacks x step
    Update (TimeScaleController.PlayerDelta, hard rule 1 -- hitstop never freezes the surge or its timer):
      SurgeMath.DropDue → ONE stack falls every parrySurgeSeconds of not parrying, never all at once.
    RESTORING 1 -- four covered paths, because a stuck multiplier is the worst bug this feature could have:
      the ladder walks down to zero on its own; GameEvents.PlayerDied and PlayerRespawned → Clear();
      OnDisable (a level reload, a teardown, the component going away) → Clear(), unconditionally.
      It never writes the multiplier at all while it holds no stacks, so it cannot fight a future item.

  DATA-AUTHORED PROJECTILE ENCOUNTERS (optional and reusable; all campaign shooters are audited by this)
    LevelDefinition.projectileSequences[] names existing EnemySpawner objects in route order
      -> engagementWindows[] binds that spawner to a world-space route line, horizontal/vertical corridor,
         and an allowed predicted-contact interval for offline/editor auditing; duplicate names support
         alternate paths. These windows never become invisible runtime firing triggers.
      -> a record with memberProgressGates creates one ProjectileVolleySequence with those ordered spawners
      -> each bound ProjectileShooter.SetSequenceControlled(true) gives that progress-gated row ownership
      -> a record without progress gates remains audit-only; ordinary sentries stay autonomous and repeat
         their normal range/LOS/facing beat instead of firing once inside a narrow corridor
      -> the first member keeps its existing projectileAcquireDelay after band, LOS and frontal readiness
         unless level data explicitly sets firstMemberAcquireDelay (T4 uses 0 because its visible approach
         already supplies the read and the first contact must remain on the ramp)
      -> ProjectileVolleySequence asks exactly one member to TryFireSequencePhrase
          -> ProjectileShooter validates life, band, LOS, facing, swept blocker path and cue-safe flight
             before EVERY emission
         -> one-shot data emits one bolt; Heavy data owns a three-shot phrase whose CONTACTS, not launch
            frames, are reserved 0.40 s apart. A transient LOS/facing/flight rejection re-plans inside the
            finite follow-up deadline; death, missing target or leaving range cancels immediately, and an
            expired phrase never creates catch-up debt
      -> a real SurgeTurret grant advances immediately; block/hit/expiry advances when that phrase is done
      -> recoveryGap 0.11 s follows resolution, just beyond shipped parrySuccessRecovery 0.08 s
      -> a member's projectileInterval rest belongs only to that same shooter; moving A -> B never transfers
         A's refire clock, while looping back to A still waits on A.IsPhraseResting
      -> optional progressOrigin / progressDirection / memberProgressGates hold each member until the
         runner crosses its authored route distance; the opening gates are 0/18/40/64/87/90/112 m and
         the final-ramp gates are 0/14/28 m
      -> after that gate and the recovery gap, a 1.1 s readiness deadline skips unavailable members;
         ungated sequences retain their previous deadline behavior, and resets clear the gate latch
      -> PlayerRespawned or wholesale EnemySpawner instance replacement restarts and rebinds the row
      -> optional shotResolutionTimeout (opening: 1.75 s, legacy default: 0) bounds an unresolved launched
         shot; retire only that sequence's incoming bolt, then resume normal recovery and order
      -> reset/death/rebind or timeout clears owned incoming shots; reflected return shots keep their payoff

    SHIPPED RUNTIME SEQUENCES:
      - T0_SurgeVolley: five one-shot Surge Turrets followed by two repeating, alternating three-contact
        Heavy Reliquaries at the opening run-out.
      - T4_SurgeRoute: three one-shot Surge Turrets, ordered by ramp progress so a fast descent cannot make
        three independent acquire clocks skip a later beat.
      T1_ParryRoute, T2_ParryRoute and T3_ParryRoute remain audit-only route groups whose enemies fire
      autonomously. `Projectile Encounter Report` accepts any LevelDefinition and proves ownership, valid
      windows and at least one safe contact per window at 11 / 17.6 / 27.5 m/s.

  PROJECTILE CONTACT AND CUE RELIABILITY
    ProjectileFlightMath.Plan starts at the REAL projectile root, solves a constant-velocity intercept,
      and integrates the same capped moving-target homing law used by Projectile.Update at 120 Hz
      -> the accepted sweep carries its terminal contactDirection into facing validation; no second
         muzzle-distance lead estimate may reinterpret a cue-slowed shot as arriving from behind
      -> sweep relative bolt/target motion for the first hit-radius contact, incoming and reflected
      -> if full speed would contact before CueLead 0.28 + CueMargin 0.16, bounded search finds the fastest
         safe slower flight; a flight with no safe contact is refused rather than launched
      -> discontinuous target jumps reset history; dt=0 refreshes history while the player can still move
      -> measured target motion / world delta gives closing speed; (distance - hit radius) / closing speed
         feeds the cue, visible weave fade and BoltRegistry consistently; separating flight has no imminent ETA
      -> actual contact still resolves through PlayerCombat.ReceiveAttack using the tracked incoming direction

  SHIPPED NUMBERS (DataFactory, rule 9; pinned by SurgeTurretTests)
    maxHP 1          one hit from anything kills it. maxPosture 200 -- out of reach on purpose, so it never
                     staggers and never raises a DEATHBLOW glyph on a body that is already dead.
    NO SentryBurst   PrefabFactory skips it for this body: a target throws no flare. Five floating grapple
                     flares down one ramp is clutter, not traversal.
    bolt             interval 1.1 s (> 2 x cue lead, so two cues never overlap), speed 36 m/s, homing
                     240 deg/s (it must turn DOWN as well as across at a player descending past it),
                     band 2.5 .. 36 m, lead 1.0.
    surge            step 0.12 (+1.32 m/s a parry on groundSpeed 11), maxStacks 5 → ceiling x1.60,
                     parrySurgeSeconds 1.4 → 0.3 s slack over the 1.1 s firing beat and a 7.0 s
                     full-ladder run-out. Still a simple timer; no distance rule or second mechanic.
    body             scale 0.55, #6F7C8A over #A8C4DC x0.9 (under the 1.05 bloom cap). Separates from the
                     two sentries on shape (a sphere in a hoop -- the only round silhouette), size (half)
                     and value (mid steel between the ghost's near-white and the Heavy Sentry's near-black).

FLARE GRAPPLE (FlareGrapple on the Player prefab, DefaultExecutionOrder -50, BEFORE the motor)
  every frame while playing, not pulling, not executing:
    Target = nearest-to-crosshair Grappleable flare within range 30 m, coneDeg 20, world ray clear
    prompt "GRAPPLE  [DASH]" when Target != null and ExecuteInteractor has no target
  DASH pressed with a Target → GrappleNow(f): line + FovKick + Sfx.Dash, motor.BeginPull(flare - 0.6 m, 0.35 s)
    motor.OnPullEnded (arrived OR cut) → motor.Launch(tossUpSpeed 18, tossHangSeconds 2) [rule 10 entry
      point; the overload opens the motor's HANG WINDOW -- see below], FovKick 8,
      ChromaticPulse(tossChroma 0.16, 0.25s) [g-force read, a separate channel from FovKick -- VFX pass
      2026-09-06, the toss used to have no visual anchored to the player at all], shake, Sfx.Teleport,
      SlashFx.Ring(player position, hue, radius 1.4, 0.25s) [sells "thrown FROM here"],
      f.Consume() (sparks; the flare is spent by the use). Tosses++.
    the motor's own Update returns early while IsPulling, so the press never doubles as an ordinary dash

  THE TOSS AND ITS HANGTIME (2026-09-06, the user: "a bit higher, and like 2 seconds of extra hangtime only")
    Launch(up, hang) = Launch(up) [capped-jump rise, air kit reset, balloons unchanged] + BeginHangTime(hang)
    FirstPersonMotor.hangUntil = now + min(hang, hangSecondsCap 2.5)   [motor clock, never Time.time]
    while hangUntil is open, inside the motor's air branch:
      gravity × HangGravityScale(vel.y, hangGravityScale 0.22) -- 1 while RISING (the apex is exactly
        18²/(2×30) = 5.4 m, authorable), 0.22 while FALLING (×fallGravityMultiplier = -9.9 m/s²)
      the JUMP CUT is suspended: a toss is not a jump, and the cut was eating up to two thirds of it
      air steer takes launchSteerBoost, the same knob the balloon float uses -- the window is for aiming
    it ENDS: on the clock (2 s), on landing, on a dash, on a wall jump, on a wall run entry, on another
      BeginPull, on a plain Launch (a balloon), and on WarpTo. IsHanging / HangRemaining expose it.
    a balloon calls Launch(upSpeed) and gets the 0.45 s / 0.55 float exactly as before -- the two windows
      are separate fields and the hang path is only ever opened by the flare toss
```

**Invariants**
- **`Apply` runs before the item is removed, and `false` means kept.** A Grapple with nothing to hook
  costs nothing. Both `Use` and `UseCurrent` honour this; there is no third spend path.
- **The grapple kill is the deathblow.** `ExecuteInteractor.ExecuteNow` is the pressed deathblow's
  coroutine with the look-cone gate skipped — it still refuses a dead or unstaggered enemy, so
  `PlayerItems` opens the victim with `Posture.Break()` first. Player-side damage is untouched
  (rule 3); enemy death is the existing `isExecute` path.
- **The surge mutates no tuning field.** `wallRunTopSpeed`, `wallRunAccel`, `wallRunMinEntrySpeed`
  never move; `WallRunSettings` derives the scaled values while `IsWallSurging`. `FeatureTests`
  `Items_SurgeLeavesInspectorFields` holds this.
- **The pull is motor state on the motor clock.** Hitstop can neither freeze nor stretch it (rule 1),
  it allocates nothing, and every write is a `cc.Move`, so walls still stop it.

### Persistent spellbook, inscriptions and FIFO items

```
WandController = serialized compatibility owner of the selected riposte inscription
   equip/start → OffhandViewmodel.ShowWand(Current) → create one SpellbookVisual if absent
   selection   → SpellbookVisual.SetSelectedSpell(Current); never swap the book instance
   riposte     → book raises/casts; blast originates at SpellbookVisual.CastOrigin

PlayerItems = FIFO inventory/effect state
   pickup/use/respawn → ItemsChanged → HUD slots + SpellbookVisual.SetFrontItem(Current)
   accepted use       → CaptureAcceptedCast(item) BEFORE removal, then PlayUse(item)
   NEVER ShowItem/ShowWand: an item changes the orb, not the book model or its scale
```

**Invariants**
- Inscriptions and items are independent data channels sharing one presentation. Neither can replace, destroy
  or rescale the persistent book; death/respawn clears item state without rebuilding it.
- Legacy wand viewmodel assets remain serialized for compatibility/future content, but the player no longer
  displays them. `ShowWand` is a compatibility method whose shipped path updates the book inscription.
- `Physics.IgnoreLayerCollision(Interactable, Player)` must stay **false** — it suppresses trigger callbacks,
  not just contacts, and silently kills every pickup, checkpoint and bloodstain.

---

## The radio (2026-09-06)

```
Managers prefab → LevelRadio (one 2D AudioSource, volume = AudioManager.musicVolume * masterVolume, faded on unscaled time)
  Start / SceneManager.activeSceneChanged → LoadForActiveLevel()
     folder = the ACTIVE SCENE NAME ("Level_01") → RadioMath.ResourcesFolder → "Audio/Radio/Level_01"
     StationName = RadioMath.StationFor(scene) → "LEVEL 01"     [the scene is the only level identity a BUILD has:
     LevelRegistry is an editor asset under Assets/Data and nothing loads it at runtime -- a levelId lookup came
     back null in play, 2026-09-06]
     Resources.LoadAll<AudioClip>  (mp3 / ogg / wav dropped in Assets/Resources/Audio/Radio/<Scene>/, name order;
                                    empty → "Audio/Radio/Default"; still empty → radio OFF, HasPlaylist false,
                                    RadioView hides its pane ROOT)
     autoPlay → TurnOn(): AudioManager.MusicDuck = 0 (the ambient bed ducks; boss music still swaps as before)
  InputReader.RadioNextPressed (]) → Next()        RadioMath.Next wraps
  InputReader.RadioPreviousPressed ([) → Previous() RadioMath.PreviousRestartsCurrent(elapsed, 3 s): restart, else back
  InputReader.RadioTogglePressed (\) → Toggle()     TurnOff → MusicDuck = 1
  track ends (source stopped, not AudioListener.pause) → Next()
  OnTrackChanged → RadioView (HUD root, RadioPane in the top-right CORNER; the BEST RUNS pane that used to sit
                   under it was REMOVED 2026-09-07 — HudBuilder.BestRuns* survive only as the column anchor
                   (BestRunsBottom -272) that HintText / LevelEditorPanel hang off): rebuilds the
                   station ("<displayName> FM") / title (ticker in a RectMask2D, unscaled) / "TRACK i/n" strings and
                   fires a 0.45 s slide + ember→bone flash; Progress drives a BarView by anchors; polls HasPlaylist
                   each frame and toggles the pane ROOT (a level with no mp3s shows nothing; OFF shows PAUSED).
                   Read-only: the keys are InputReader → LevelRadio, never the HUD.
```

## Enemy AI

### souls_enemies — the duel reads the player (2026-09-06, from docs/plans/soulslike-report-gap-analysis-2026-09-06.md)

```
ChooseCombo(dist)   (EnemyController; the boss overrides for its phase patterns)
   moveset authored → EnemyMoveset.SelectIndex(dist, moveLastUsedAt[], Time.time)
        rule 1: in band, weight > 0, off cooldown (MovesetEntry.cooldown; signatures carry 5-8 s, filler 0)
        rule 2: in band ignoring cooldowns (a fight where everything cools still attacks)
        rule 3: first non-empty entry
        → moveLastUsedAt[idx] = now on THIS instance (never on the shared asset), LastMoveIndex = idx
   BossController.ChooseCombo: the phase's patterns[], never the same index twice running when it has > 1
INTERRUPT: the flask   (EnemyController.Update, before the state switch; never a sentry)
   PlayerDrinkEdge(): FlaskAbility.IsDrinking rising edge on the player
   in Chase or Recover → TryPunishFlask(dist): data.flaskPunishChance > 0, not aggroLocked, ≥ FlaskPunishCooldown 4 s
        since the last one, dist ≤ aggroRange, an attack slot free (MayCommitToAttack), in the commit band or
        moveset.HasEligible(dist), Random.value ≤ chance → PunishFlaskNow(dist): FlaskPunishes++, BeginCombo(ChooseCombo)
        -- the recovery is CUT and the wind-up starts this frame; the cue is still cueLead before impact.
        Refused inside Windup/Strike/Staggered (one attack at a time). Legendaries 0.5-0.75, Warden 0.7, Drillmaster 1.0.
NEAR-BREAK read   (EnemyPostureBar.LateUpdate + EnemyVisuals.SetPostureRatio)
   posture ratio ≥ NearBreakRatio 0.8 and not broken → the fill and the eye beat toward white at NearBreakHz 4.5,
   amplitude rising to the break. Hue, not brightness: the eye's peak stays 1.4 (bloom budget untouched).
   Rising edge only (was-not-near-break → is) → AudioManager.Play(Sfx.Tension) once (2026-09-06 audio pass:
   this read was pure visual before — a player not looking at THIS enemy's bar got no warning at all. Not a
   repeating beat: several near-break enemies ticking every ~0.22 s would spam the shared one-shot pool).
THE SURGE TURRET  (pshooter_enemy03; sandbox pads x -8 / -3 / 2, z -26, a ROW of three) -- SurgeTurret,
   an EnemyController subclass. rangedOnly, so Chase holds the perch and tracks; 1 HP, unreachable posture,
   no flare. Its OnParried pays the ramp's dedicated 1.4 s speed-surge timing through ParrySurge and is
   excluded from PlayerCombat's general 2.0 s Perfect payout to prevent a double stack. Full flow above.
THE DRILLMASTER   (Legendary_Drillmaster; sandbox pad x 14, z -26, SpawnEnemyInFront 9) -- the showcase body:
   every signature on cooldown, a 1.25 s DELAYED overhead in a 0.5 s fight, a feint, an unblockable kick,
   a far-band lunge, flaskPunishChance 1.0, posture 150. Knight silhouette in slate and cold blue.
```

```
EnemyController  = the BRAIN ONLY. Rig-agnostic: it knows states, timings and distances.
   ├─ IEnemyLocomotion    (NavMeshLocomotion today; root-motion rig later)
   └─ IEnemyPresentation  (EnemyVisuals today; Animator-driven rig later)

  Start / Update → EnsurePlayerTarget(): retain the live PlayerCombat target; reacquire when scene order,
                   respawn, or an in-play domain reload clears the non-serialized references
  Idle    → aggro range + line of sight (and NOT aggroLocked) → Chase
  Chase   → locomotion.MoveTo(player); approach to preferredRange and HOLD
            in range + off cooldown + MayCommitToAttack() → Windup
  Windup  → FaceTowards at a CAPPED turn rate (circling works, commits stay committed)
            telegraph; cue fires cueLead (0.28s) before impact
  Strike  → the attack's lungeDistance translates the BODY along the facing frozen at cue time
            (previously the lunge only moved a child transform, so attacks only landed in your face)
  Recover → Reposition: step in if you fled, back off if inside its own range, strafe when settled
            then resume combo OR (aggroLocked ? Idle : Chase)
  Staggered → deathblow available     Executed / Dead

  arbitration: static activeEnemies; only MaxSimultaneousAttackers (1) may be mid-swing.
```

### Wind-up silhouettes — one pose per attack

The wind-up's job is to say **which** attack is coming, not just that one is. Timing alone cannot do
that: a 0.45 s jab and a 0.72 s heavy look identical for their first 0.45 s. So the SHAPE carries it,
and the shape is **data on the attack**, not code.

```
EnemyAttackData.windupPose : WindupPose
   authored     off → the cone-derived fallback below. Most of the 25+ attack assets are content
                that never needed its own silhouette, and they stay that way.
   armWindup    shoulder Euler at the peak.  THE pose.
   armStrike    shoulder Euler the swing carries through to — it must RESOLVE the wind-up
   bodyOffset   whole-body offset, metres, enemy-local (+Z toward the player)
   bodyEuler    whole-body Euler. Yaw is the cheapest big silhouette change there is.
   weaponLag    0-1, how much the hand trails the shoulder so the blade whips

EnemyController.BeginWindup(atk, gap)
   → IEnemyPresentation.Telegraph(atk, seconds)          seconds is AUTHORITATIVE (contract rule 1)
        → EnemyVisuals.PoseFor(atk)
             authored ? the WindupPose : cone-derived fallback
                          coneDeg >= 90 → PoseSweep      (a horizontal swing)
                          coneDeg <= 45 → PoseThrust     (a straight-back cock)
                          otherwise     → PoseOverhead   (arm rears high)
        → WindupCo(seconds)
             a short anticipation dip (down + forward), then ease-out to the peak:
               LungeRoot.localPosition = lungeBase + bodyOffset * k
               LungeRoot.localRotation = lungeBaseRot * Euler(bodyEuler * k)
               armPivot                = armBase   * Euler(armWindup, eased)
               weaponPivot             = weaponBase* Euler(sameEuler * weaponLag)
   → CueFlash(unblockable)  cueLead (0.28 s) before impact
        SetArm(armWindup * 1.12) and FREEZE (cuePeak). ← the frame the parry decision is made on
   → Strike(lunge, seconds)
        LungeCo lerps the arm from wherever it is to armStrike with lag 0.55,
        while LungeRoot returns to base + forward * lungeDistance.
```

**Nothing here touches a duration.** `windup`, `impactDelay`, `strikeDuration`, `recovery`, `comboGap`
and the 0.45 s wind-up floor are calibrated against `parryPerfectWindow` and `cueLead`; a pose only
changes what the body looks like for a span already decided.

**THE BLADE'S ON-SCREEN ANGLE IS NOT THE ANGLE YOU AUTHOR.** `armWindup` drives the SHOULDER; the hand
trails it by `weaponLag` and the whole body is rotated and offset underneath both, so what the player
sees is the product of three rotations plus perspective. Poses must be **measured**, never derived —
see ENGINEERING-LOG.md → *A wind-up pose cannot be computed, only photographed*.

The shipped silhouettes, measured from the player's eye at fighting distance (Grunt 3.8 m,
Heavy 4.5 m, Boss 5.7 m). `tilt` is the blade's on-screen angle (0 = level bar, ±90 = upright mast),
`len` its length in body-heights after foreshortening, `tip` where its high end sits relative to the
body's centre, in body-heights:

| Attack | wind-up | tilt | len | tip (x, y) | reads as |
|---|---|---|---|---|---|
| `Grunt_Jab` | 0.45 s | 10 | 0.89 | (0.92, 0.20) | level bar, chest height, right |
| `Grunt_Slash` | 0.50 s | −45 | 0.88 | (−0.40, 0.61) | 45° diagonal climbing left |
| `Grunt_Heavy` | 0.72 s | 88 | 0.87 | (0.13, 1.16) | **vertical mast** above the head, body sunk |
| `Heavy_Step` | 0.50 s | −55 | 0.74 | (−0.12, 0.02) | low steep bar; the body advances |
| `Heavy_Overhead` | 0.75 s | 88 | 0.89 | (−0.04, 1.20) | **vertical mast**, dead centre |
| `Heavy_Sweep` | 0.55 s | 0 | 0.90 | (0.91, 0.10) | **level bar**, widest shape it makes |
| `Boss_Slash` | 0.50 s | −60 | 0.80 | (−0.39, 0.87) | steep diagonal, left, high |
| `Boss_DoubleSlash_A` | 0.45 s | 30 | 0.86 | (0.73, 0.36) | shallow diagonal, right, mid |
| `Boss_DoubleSlash_B` | 0.45 s | −30 | 0.74 | (−0.39, 0.36) | hit A mirrored |
| `Boss_Slam` | 0.90 s | 88 | 0.90 | (0.12, 1.17) | **vertical mast** at 2.2× scale |
| `Boss_Thrust` | 0.85 s | −7 | **0.40** | (−0.10, **−0.17**) | **unblockable**: the only pose below the centre line, the only flat one, the only short one |

`Boss_Thrust` is the one the player must never misread — the answer is to MOVE, not parry, and a
mistake costs 55. Its pose is a **second, independent** read that ADDS to the pink `M_AlertTell` marker
(peak 3.0) and the red cue tint; it does not replace them.

Regression cover: `FeatureTests → WindupPoses` instantiates the real prefabs, drives the real rig
through the real pose maths and measures the blade, then asserts the pairs whose confusion costs the
player are separated on at least one channel (angle, foreshortening, side, height). It also drives an
**unauthored** attack through `Telegraph` and asserts the cone-derived fallback still rears the arm.

Filming them: `VibeGame1.FrameFilm.RunWindups(dir, "Enemy_Grunt", 3.8f)` — see TOOLING.md.

**Spacing model** — `preferredRange` is the distance an enemy WANTS to fight from. It approaches, holds,
commits from there, and the lunge closes the gap during the strike. That is what makes a wind-up readable:
you can see the whole enemy when it commits.

| | preferred |
|---|---|
| Grunt | 3.0 |
| Heavy | 3.6 |
| Legendary_Ninja (The Thirteenth Shade) | 3.2 |
| Legendary_Knight (The Iron Penitent) | 4.2 |
| Legendary_Spellsword (The Ashen Chorister) | 4.3 |
| Legendary_Marionette (The Pale Marionette) — *prototype, sandbox only* | 3.7 |
| Legendary_FlurryBrawlerV18 — *TEST, sandbox only* | 2.0 |
| Boss (The Hollow Warden) | 4.6 |

The three campaign `Legendary_*` mini-bosses and the separate sandbox prototypes are ordinary
`EnemyController`s built by
**VibeGame1 → 4b. Build Mini-Bosses** (`Editor/MiniBossFactory.cs`), a sibling of step 4 rather than
part of it. They are deliberately NOT `BossController`s: that class raises `BossDefeated`, which stops
the speedrun timer and clears the level. See ARCHITECTURE.md → *Legendary mini-bosses*.

Several (`Legendary_Spellsword`, `Legendary_Knight`, `Legendary_Marionette`, and later sandbox
prototypes including `Legendary_FlurryBrawlerV18`) carry an **imported mesh** from
`Assets/Enemies/*.fbx` in place of the primitive body. Nothing in this map changes: the
brain is untouched and the bindings simply resolve to the imported `SkinnedMeshRenderer` and to empty
pivots parented under it rather than to primitives. Physics stays on the prefab root — a legless model
hovers by lifting the mesh inside `Visual`, never via `NavMeshAgent.baseOffset`. See AUTHORING.md →
*Importing a forge model*.

### Animated presentation — the second `IEnemyPresentation`

`Legendary_Marionette` is the first enemy driven by real animation clips. It substitutes
**`PuppetVisuals`**, an `EnemyVisuals` subclass, at the same seam — `GetComponentInChildren<
IEnemyPresentation>()` finds it and the brain is unchanged.

```
Assets/Enemies/PaleMarionette.fbx  +  PaleMarionette.clips.json   (committed source art)
        │
   4a. Split Forge Animation Clips  (Editor/ForgeClipSplitter.cs)
        │   writes ModelImporter.clipAnimations from the manifest; forces Generic rig;
        │   writes NO AnimationEvents (an event driving gameplay = a second timing authority)
        ▼
   15 named AnimationClips as FBX sub-assets
        │
   4b. Build Mini-Bosses → PuppetAnimatorFactory.Build()
        │   Assets/Animation/Legendary_Marionette_Animator.controller
        │   one state per clip, NO transitions — it is a clip library, not a brain
        ▼
   Legendary_Marionette.prefab
        Visual (PuppetVisuals) ─ LungeRoot ─ SpinRoot ─ Model (Animator)
             base lean/lunge      the whirl    the clips
```

Per-attack flow, on top of the ordinary Windup/Strike map above:

```
EnemyController.BeginWindup(atk, gap)
   → PuppetVisuals.Telegraph(atk, seconds)
        → base.Telegraph(...)                  the SHARED colour sink + alert marker
        → PlayAttackClip(atk, seconds + impactDelay)
             Animator.speed = clipContactTime / secondsToImpact      ← clip bends to data
             ClipFor(atk) picks the clip AND its own contact anchor:
                 atk.clip set       → namedClips[i]   EXPLICIT DATA FIRST. A generated,
                                                      per-character clip (HalberdSweep,
                                                      ShoulderCharge) has no canonical name
                                                      the mapping below could know; the
                                                      attack names it, and 4b bakes every
                                                      manifest clip with OnAttackHit onto
                                                      the prefab with its own length + anchor
                                                      (namedClipLengths / namedClipHits).
                                                      Unknown name → one warning, fall through.
                 spin prefix        → clipSpin
                 name ends "_Stab"  → clipStab      NAME BEFORE HEURISTIC. A kick is
                 name ends "_Kick"  → clipKick      the unblockable, so an unblockable
                 unblockable / >=0.9→ clipHeavy     test placed first swallows it --
                 else               → clipAttack    which is what used to happen.
             Each clip carries its OWN baked length + anchor, or it would be stretched
             onto a different clip's contact frame and land its blow at the wrong moment.
        → name starts with spinAttackPrefix ? BeginPass(...) : UnwindToSquare()
             BeginPass  re-anchors WITHOUT changing speed. The rate is constant; the ARC is
                        what gets chosen -- the whole number of revolutions whose implied
                        speed is closest to spinDegPerSec. Re-derived every beat from the
                        CURRENT yaw, so phase can never accumulate error.
                        └ ResolveRate(spinDegPerSec, smoothedDt)   alias guard: caps the
                           constant rate so the body steps <= 75 deg per rendered frame.
                           Slack at 60 fps (34.8 deg), engages below ~28 fps.
                        └ if no whole-revolution arc fits inside maxRateCorrection (12%),
                           the RATE WINS and the body lands off-square. A visible speed
                           change is worse than a few tens of degrees of misalignment.
             UnwindToSquare  stops the whirl and squares the body up — the tempo-break
                        overhead and the far-band lash read as a break BECAUSE the body stops
   → (each frame)  spinPhase = passArc * (1 - k)        LINEAR. No curve, anywhere.
                        Reaches 0 exactly at the impact, which is what puts the body
                        square-on to the player at the blow.
   → (between passes)  spinPhase -= ResolveRate() * dt    the SAME constant rate, not a
                        follow-through decaying to an idle drift -- that sag was half of
                        the pulse this design exists to remove.
   → FireCue()   → base.CueFlash()   the ONLY tell. A constant spin has no positional
                        wind-up by design, so the parry rides on the flash and its audio.
   → BeginStrike → Strike(): the whirl carries THROUGH, it does not stop on the blow
   → OnParried   → Recoil(): the "Hit" clip as a jar. The spin survives a deflect; only the
                   posture bar records it.
   → HandleBroken→ Slump(true): the whirl stops DEAD and the glyph comes up. That frame is
                   the punish read, and it works because nothing else is moving.
```

#### Flurry Brawler V18 — staged animation, one combat clock

`Legendary_FlurryBrawlerV18` is an additive **sandbox-only TEST enemy**, not a replacement or retune of
`Legendary_FlurryBrawler` v15. It remains in the `souls_enemies` lineage: an ordinary melee
`EnemyController` with `shootsProjectiles = false` and `rangedOnly = false`; its prefab has no
`BossController`, `ProjectileShooter`, `SentryBurst`, `ProjectileVolleySequence`, or `ParrySurge`.

```
DataFactory.CreateAll
   → Legendary_FlurryBrawlerV18 EnemyData
   → Legendary_FlurryBrawlerV18_Moveset + BrawlerV18_* attacks
4a. ForgeClipSplitter
   → FlurryBrawlerV18.fbx: exactly 18 Generic, nonempty, eventless clip sub-assets
4b. MiniBossFactory
   → generated 18-state Legendary_FlurryBrawlerV18_Animator.controller
   → Legendary_FlurryBrawlerV18 prefab (EnemyController + FlurryBrawlerV18Visuals)
7. SandboxBuilder
   → separate pad/spawner/wake switch at (112, 20); v15 pad remains
```

The allowlist is exactly `Idle`, `Walk`, `Run`, `Jump`, `AttackSwing`, `AttackOverhead`, `AttackStab`,
`AttackKick`, `Hit`, `Stagger`, `Roar`, `Block`, `Death`, `Jab2`, `Dash`, `Clap`, `ShoulderCharge`, and
`Combo2`. The controller is still a clip library with no authored transitions and the splitter writes
zero `AnimationEvent`s; every hit remains the single schedule owned by `EnemyController` and resolves
only through `PlayerCombat.ReceiveAttack`.

`FlurryBrawlerV18Visuals` is the V18-only presentation profile. Ordinary attacks use
`PuppetVisuals`; Shoulder holds `Run` then stages `ShoulderCharge` at contact, Clap holds `Idle` during
the authored 1.6 m lift then stages `Clap`, and Combo2 declares exactly one contact and preserves its
authored tail. `Strike` re-anchors those presentation deadlines to `EnemyController.NextImpactTime`, so
a frame-late Windup→Strike transition cannot move the visible contact away from the real impact. The
Clap profile owns only `LungeRoot.y` through descent and clamps it to the captured ground height before
emitting the existing ring/sparks once, even on a miss. Dash and Shoulder travel remain
`EnemyAttackData.lungeDistance`; `TravelRoot` cancels Hips XZ and the Animator never applies root motion.
Jump on wake and Block on ordinary recovery are presentation only. Damage during a committed attack
keeps the staged sequence visible; recoil, stagger, death, and non-committed hits may interrupt it.

#### The blade trail — `EnemyWeaponTrail`

```
EnemyWeaponTrail.LateUpdate()          (MiniBossFactory.WireBladeTrail, ModelSpec.bladeTrail)
   → animator.GetCurrentAnimatorStateInfo(0).shortNameHash ∈ attackClips (= pv.namedClips)
   → live = WindowContains(normalizedTime, namedClipHits[i], leadIn 0.22, tail 0.12)
        live: Sample() base = RightHand.TransformPoint(bladeBaseLocal), tip = ...(bladeTipLocal)
              first frame past the contact: SlashFx.Sparks(tip, tip velocity, hue, 4)
        else: the strip collapses from its oldest edge over fadeSeconds (0.12)
   → mesh.vertices rewritten in place (28 verts); renderer disabled when nothing is live
```

**Invariants**
- **The trail reads the clip, never the brain.** Its window is a fraction of the clip around the baked
  contact frame, so it draws the cut at whatever speed the clip is playing and never the wind-up.
- **A swing may not glow.** The strip's material peaks at 1.0, under the 1.05 bloom cap; `CueFlash` and
  the parry keep the light budget. The contact sparks (4) stay under the parry's ten.
- **Scaled time.** The strip freezes in hitstop with the puppet.
- **Aggression is asserted as EFFECTIVE values.** A recovery that looks like an opening in DataFactory is
  played at ×0.45 at aggression 0.85; `HalberdierBehaviourTests.TheHeavyIsStillAPunishAfterTheAggressionScaling`
  holds the number the player actually gets.

### parkour_enemies — the sentries shoot, and a deflect is a boost

Since the 2026-09-06 split the span shooters are their own assets, `pshooter_enemy01` / `pshooter_enemy02`
(`Assets/Data/Enemies/parkour_enemies`, copied from the melee Grunt / Heavy by `DataFactory` then flipped to
`rangedOnly` + `shootsProjectiles`).

**`pshooter_enemy01` is a floating cartoon GHOST** (2026-09-06, user-directed). `PrefabFactory.BuildEnemy`
takes a `ghost` flag and calls `BuildGhostBody` instead of `BuildPillBody` for that one prefab: a rounded
1.06 x 1.24 m shell at y 1.30, five waving hem tatters down to y 0.24, two ember eye sockets and a dark
mouth, two nub arms on the SAME pivots as the pill (so no authored wind-up pose moved) and no blade
(`EnemyVisuals.weapon` is null; `WeaponPoint()` falls back to `weaponPivot`). `SentryGhostVisual`
(`Assets/Scripts/Feel`) owns the float, the hem wave and four additive mist wisps it builds at `Awake`.

- **Nothing about the float is physical.** It writes `FloatRoot`, a child of `LungeRoot`. The root, the
  capsule (h 2, r 0.45, centre 1), the `NavMeshAgent`, the deathblow height (1.45) and the world posture
  bar are byte-for-byte what they were.
- **Luminous, never blooming.** The emission floor goes through `EnemyVisuals.SetAura` (the only sanctioned
  door), so it is modulated by `chargeDark` and the ghost inhales its light on a wind-up. Budget: lit 0.35
  + floor 0.28 + 4 x 0.10 mist = **1.03**, under the 1.05 bloom threshold. Pinned by `GhostTests`.
- **Colour.** `M_SentryGhost` albedo `#A9C2DA` (also `EnemyData.bodyColor`, so shell and hem match), cold
  blue-white. Both sentries lost their violet: violet is `SentryFlare`, i.e. "use this", and no body may
  wear it.
- **`pshooter_enemy02` is the HEAVY RELIQUARY, not a pill.** Two split stone feet, a pinched metal waist,
  a broad faceted upper mass, crown/cheeks and three recessed ember apertures make it read as a fixed
  three-barrel perch weapon rather than a humanoid duellist. It carries no arms or blade; empty standard
  pivots preserve the controller contract. Twelve renderers share three generated materials, with no
  lights, particles or material instances. `HeavySentryVisualTests` pins that budget and silhouette.
- **Time split.** Float and hem run on SCALED time (body motion, freezes in hitstop); the mist runs
  UNSCALED like every other effect. `Level_01_Level.asset` places only these; the melee
`Enemy_Grunt` / `Enemy_Heavy` are `souls_enemies` on the sandbox pads. `ProjectileShooter` is added by
`PrefabFactory` only to a body whose data shoots.

```
EnemyController.Update()  Idle → Chase when dist ≤ WakeRange (= max(aggroRange, projectileMaxRange) for a shooter: 32 m)
                          AND HasLineOfSight (any of three lines clear: head 1.5, chest 0.8, feet 0.2 -- a runner
                          behind a rail is still seen). A SENTRY (EnemyData.rangedOnly: Grunt, Heavy) in Chase
                          only locomotion.Stop() + FaceTarget: it holds its perch, never commits a combo, and
                          never goes back to sleep. A test may still call BeginCombo on it directly.
ProjectileShooter.Awake()  autonomous sentries retain the shared span epoch; only a progress-gated runtime
                          volley (the shipped T0 opening) waits for its sequence owner.
ProjectileShooter.Update()   (on every Enemy_* prefab; fires only when EnemyData.shootsProjectiles)
   gate: awake (Current != Idle), alive, not staggered, not committed (no bolt during a melee wind-up),
         not aggroLocked, player inside [projectileMinRange 6 (turret 2.5), projectileMaxRange
         32 ordinary / 48 Heavy / 36 Surge], HasLineOfSight
         (same three lines; only cast once the band test passes)
   → F1, THE ARM-UP: on the out-of-band/blocked → in-band TRANSITION,
     nextFireAt = ProjectileMath.AcquireBeat(nextFireAt, now, interval, projectileAcquireDelay 0.7) -- the beat is
     HELD, so a stale one used to fire on the FIRST FRAME the line cleared: the frame you crest a ledge or land.
     Only ever moves a beat forward, and never by more than one interval. The ordinary blue tight-route
     sentry and the Heavy's multi-contact planner preserve completed acquisition across a brief LOS loss;
     every restored line still revalidates band, LOS, facing, obstruction and flight. Surge reacquires normally.
   → at a launch request (autonomous beat or ProjectileVolleySequence phrase):
     F3, NO BOLT AT A FLEEING BACK: ordinary blue and the three-contact Heavy use
             ProjectileMath.ArrivesInsideFacing with the player's actual flat AimForward; Surge retains
             ArrivesInFront(muzzle, chest, motor.Velocity, speed, statsData.facingConeDeg 75). Both predict
             the arrival point and take the bearing it comes FROM there (ParryMath.SourceDirection, the rule
             the parry itself judges). A Heavy phrase remains answerable while a deflect changes velocity;
             the one-shot Surge keeps its movement-facing ramp contract.
     ProjectileFlightMath.Plan(real root, chest, motor.Velocity, desired speed, lead, homing, hit radius,
             CueLead + CueMargin) solves the exact constant-velocity intercept, then sweeps forecast projectile
             and player spheres with the same moving-intercept capped-homing step the runtime uses.
     → world-obstruction forecast: the ordinary blue `pshooter_enemy01` traversal sentry linecasts the
       exact predicted path through tight route geometry; Heavy Sentry and Surge Turret retain the 1 m
       broad clearance sweep. All three still require band, LOS, frontal arrival, forecast contact and cue safety.
       The Heavy's generated `projectileIgnoreDepartureSupport` may ignore only the collider detected
       directly beneath it while the 1 m forecast leaves that support. The actual centreline, a buried
       muzzle, any sibling collider, a full NonAlloc hit buffer, and the same support after departure all
       fail closed. Surge Turrets never inherit this policy.
      → no contact / unsafe cue / blocked sweep:
             refuse this emission; ordinary blue and Heavy planning retry after 0.08 s. During an active
             Heavy phrase, transient failures retry only until its bounded follow-up deadline; a persistent
             failure cancels without catch-up. The one-shot Surge retains its full-beat behavior.
     → READY: fire at the plan's launchPosition, direction and fastest cue-safe speed
     → autonomous arrival reservation occupied: ordinary blue sentry retries at the first safe predicted
       contact slot; it does not advance one full beat and preserve a same-phase tie forever. Heavy uses the
       same responsive hand-off; Surge retains its existing beat behavior.
     → burstCount 3: reserve the next predicted CONTACT at +0.40 s, re-plan and revalidate before each follow-up
     → every bolt owns immutable (phraseId, ordinal) identity and reports its incoming result exactly once
     → ordered Perfect 1/2 retain the phrase; any Block/Hit/expiry/cancel invalidates it; Perfect 3 kills
       through ordinary Health. Reflected Heavy bolts do zero health/posture damage, so stale returns cannot
       accidentally finish the contract. The next phrase rests 0.90 s from the FINAL incoming resolution.
     a 0.55 m Bolt core at the chest, multiplied only for presentation by EnemyData projectileVisualScale --
     SlashFx additive ember with Projectile.HotCore (peak 1.6) written OVER the normalised colour: THE ONE
     GLOW IN TRAVERSAL, because the bolt is the tell -- plus a 7-point additive trail 0.16 s long,
     Projectile.Fire(shooter, data, dir/speed from the shared flight plan)
Projectile.Update()  (scaled time: hitstop freezes it)
   LOGICAL root follows ProjectileFlightMath.HomingDirection toward the moving intercept; the cue ETA is
   measured from relative swept motion and reconciled with the launch plan. Both use
   ProjectileMath.ForecastTargetVelocity: motor velocity first, or transform displacement divided by
   TimeScaleController.PlayerDelta -- never slowed Time.deltaTime -- so hitstop cannot fabricate speed.
   Every motor read then passes through ProjectileMath.GroundAwareTargetVelocity. On flat grounded support
   only, the motor's intentional negative-Y ground-stick residue becomes zero before planning, steering and
   cue/contact prediction. Airborne falls, upward launches and grounded slope velocity remain unchanged.
   → the amber Core CHILD alone weaves up to 0.34 m on a deterministic per-shot phase before the cue;
     it eases in over 0.09 s, fades back over 0.12 s, and is exactly on the logical line for the whole
     remaining ≤ 0.28 s cue window. A 7-point fixed buffer records the visible head, so the trail curves too.
     Reflection clears that history and flies visually straight. None of this enters arrival, registry or damage.
    → BoltRegistry.Report(id, cue = now + remaining - 0.28 (MaxValue once cued), impact = now + remaining) every frame,
     Clear() on reflect / spend / destroy -- F2: EnemyController.AnyAttackIncoming and .EarliestCueTime consult the
     registry as well as the melee list, so a missed bolt parry costs parryMistimeRecovery 0.2 s and not the
     parryWhiffRecovery 0.5 s mash tax, and ParryController.ClampRecoveryToNextCue works on a span
     ProjectileThreatView reads only AnyCuedImpactBefore(now + 0.28): four quiet brackets reinforce an
     already-fired cue at the crosshair, never reveal an uncued shot or become a second targeting system
   → remaining ≤ 0.28 s once → Sfx.ParryCue + the bolt flares ×2.3 × projectileCueScale and its core goes white-hot
                                                                        (the same lead every attack gives)
    → each live homing segment Linecasts against the motor's world mask; solid route geometry retires
      the bolt when its contact is before or tied with the swept player contact, so a curved shot cannot
      pass through an intervening ramp obstacle and damage the player out of sight
    → within hitRadius of the chest before any world contact → PlayerCombat.ReceiveAttack(AttackInfo{projectileAttack, shooter})   (rule 3)
        Perfect → report the phrase outcome exactly once, then the bolt REFLECTS at ×1.4 toward a living shooter,
                  motor.AddImpulse(ProjectileMath.SpeedGain(look.AimForward, parrySpeedGain 9))   (rule 10: motor entry point;
                  a run at 11 becomes 20 and bleeds toward the 17.6 air soft cap -- a boost, not a new cruise)
                  CameraFX.FovKick(4); ReceiveAttack already did the deflect sparks / hitstop / OnParried
        Blocked / Hit / None → spent (the chip / damage / posture landed in ReceiveAttack as usual)
   reflected ordinary bolt reaches the shooter → Health.TakeDamage(parriedProjectileDamage) + Posture.Add(parriedProjectilePosture),
                                        sparks, Sfx.Hit, spent
```

**Invariants**
- **A bolt is an attack** and resolves only through `PlayerCombat.ReceiveAttack` (rule 3). Nothing here writes health or posture on the player.
- **A planned clear flight is not permanent permission through walls.** Homing reacts to live movement,
  so every runtime segment rechecks solid world geometry and resolves the earliest contact deterministically.
- **The flight is the tell, and it is cued at 0.28 s like every attack.** A launch must forecast first
  contact at or beyond 0.44 s; the planner slows only as much as required, and refuses impossible shots.
- **A bolt in flight is an INCOMING ATTACK** (`BoltRegistry`, F2). The two cue helpers on `EnemyController` read it with NO range test — a bolt is already aimed at you, so its arrival time is the question, not its perch's distance. Melee's 6 m `InThreatRange` is untouched.
- **A sentry that has just acquired you takes a breath** (`AcquireBeat`, F1) and **never shoots a back it has already passed**. Ordinary blue and Heavy judge the player's real look, allowing deliberate look-back answers; Surge retains movement-facing. Route windows describe geometry, not parry state; every follow-up still earns a legal shot.
- **Brief traversal occlusion does not repay the acquire delay.** Ordinary blue and the Heavy's multi-contact planner remain armed and retry a rejected legal plan in 0.08 s, but every emission still passes the whole launch contract. A Heavy that has begun a phrase retries transient failures only until its finite deadline. The one-shot Surge keeps its old reacquire/full-beat behavior.
- **Forecast clocks cannot disagree.** A player moving through world hitstop still has player-clock/motor velocity; never divide that displacement by slowed world delta for a projectile cue.
- **Ground contact is not a fall.** On flat support, the motor's small negative-Y stick velocity must not
  forecast the player through the deck and reject a legal Heavy burst. Apply the shared ground-aware helper
  in launch planning and both runtime projectile forecasts. Do not flatten a ramp or an airborne fall.
- **A ranged-only recovery holds its perch.** `EnemyController.Recover` stops locomotion for `rangedOnly`
  enemies; only melee enemies run `Reposition`. A deflected sentry must resume shooting from the same pad.
- **A mechanical turret has no posture fiction.** `EnemyData.usesPosture=false` makes `Posture.Add`, `Break`
  and `HoldStagger` inert; generated Heavy/Surge prefabs omit posture bars and deathblow marks. Health and
  ordinary melee damage stay live.
- **Tight-route permission narrows only the clearance shape.** `EnemyData.projectileAllowTightRouteShots`
  makes the ordinary blue traversal sentry use a thin exact path, not a world-collision exemption; solid
  walls, LOS, arrival contact, frontal readability and cue safety remain gates. Heavy Sentries and already-
  tuned Surge Turrets must stay on their conservative 1 m clearance sweep.
- **Arrival spacing is an arrival contract, not Update order.** When equal-phase ordinary sentries contest a
  contact, retry from the first open contact slot. Do not make both wait a whole interval, which recreates
  their tie and starves the later updater.
- **The cue lead is FLAT at 0.28 s and stays flat.** F5 lengthens the near FLIGHT (`CueMargin` 0.16, near edge 6 m) instead of scaling the lead with speed: the lead is a contract shared with every melee attack, and an elastic one would give the loudest signal in the game a variable meaning.
- **The bolt is the one glow in traversal.** Every other effect stays under the 1.05 bloom cap; the bolt's core ships at 1.6 (`Projectile.HotCore`, pinned by `TheBoltIsTheOneGlowInTraversal`) because it is the ATTACK'S tell, and the shooter itself still never glows until it is deflected.
- **A deflect buys speed through the motor** (`AddImpulse`), flattened along the LOOK — aim at the next ledge and deflect (rule 10; MOVEMENT-PRINCIPLES 5 and 6).
- **One attack at a time**: the shooter never fires inside a melee wind-up or strike, so a tell is never two things.
- **The data decides** (rule 9): projectile stats and burst cadence are on `EnemyData`; ownership and route
  windows are on `LevelDefinition`. The component carries no encounter-specific coordinates. The Warden
  and the legendaries do not shoot.

### Burning enemies — `EmberAura`

```
EmberAura.Update()                       (Legendary_Revenant; bolt onto any enemy)
   → heat = lerp(glowAtRest, glowAtBreak, Posture.Ratio) x breath
   → EnemyVisuals.SetAura(emberHot, heat)      ASKS. Never writes a property block.
        └ WriteBody() folds it in as an emission FLOOR under the parry spike, and
          multiplies it by chargeDark like everything else -- so a burning body
          still INHALES on a wind-up and floods back on the strike.
   → embers: a fixed pool of additive quads, spawned around the body and parented to
        a SCENE-LEVEL root, so the enemy moves out from under its own fire instead of
        towing it. Rate scales with heat; the pool never grows.
   → one weak point light at chest height.
```

**Invariants**
- **It ASKS for the glow.** `WriteBody` is the single writer of the body's `_BaseColor` /
  `_EmissionColor`; a second writer here would be overwritten on the next frame and would silently
  break the parry read. Same arrangement `WeaponEmber` has with `EnergyGlow`.
- **The floor stays far under the parry spike** (0.22 vs 3.2). A deflect must remain the brightest thing
  an enemy ever does, or "light means you deflected" stops being true for burning enemies.
- Unscaled time throughout, so hitstop does not freeze the fire. A frozen flame reads as a dropped frame.

### Generated clips and root motion — `Legendary_Halberdier`

The Argent Halberdier's attacks are nine clips generated for this body alone (`forge.py --motion`), six
of which TRAVEL: the tool bakes the pelvis path onto the Hips bone and marks the clip `root.motion` in
the manifest. Nothing here lets a clip move the enemy; the travel becomes data.

```
ArgentHalberdier.clips.json      "root": {"motion": true, "forward_m": 0.898, ...}   (SOURCE travel)
        │
   4a. Split Forge Animation Clips
        │   NO root node, on purpose (motionNodeName and the avatar's root bone both empty): on a
        │   Generic rig a root node moves the Hips' WHOLE transform — XZ, the leap's lift, the sweep's
        │   yaw — onto the model root, and a mis-spelled path imports zero clips. The Hips travel stays
        │   in the clip like every other bone. Skin: Custom weights, 8 bones per vertex (the tool's contract).
        ▼
   4b. Build Mini-Bosses (MiniBossFactory.WireAnimatedBody)   manifest has a root.motion clip ⇒
        │   LungeRoot ─ SpinRoot ─ TravelRoot ─ Model      one more transform, one more single writer
        │   pv.travelRoot / pv.hipsBone / pv.hipsRestLocal (the Hips' bind position in model space)
        ▼
   PuppetVisuals.CompensateTravel()  (LateUpdate, after the Animator poses the rig)
        │   TravelRoot.localPosition = −(Hips.position − rest).xz     lift and yaw are left alone
        │   → the mesh stays over its collider while a thrust walks the pelvis 1.2 m or a charge 4.7 m
        ▼
   EnemyAttackData.lungeDistance  (DataFactory, rule 9)  → the SAME distance, run cue→impact by
        │                                                  EnemyController.ApplyLunge like every lunge
        ▼
   HalberdierDataTests.EveryLungeIsTheClipsOwnTravel     samples the clip on the FBX: forward Hips
   HalberdierDataTests.TheTravelRoot_KeepsTheMeshOverItsCollider   travel == lungeDistance (±0.15 m),
                                                         a non-travelling clip ⇒ lunge 0; drift < 0.05 m
```

**Invariants**
- **A clip never moves an enemy. The art's travel is data.** The NavMeshAgent owns the transform and the
  parry contract's reach is `range + lungeDistance`; a clip that displaced the body would make the reach
  a function of playback speed. The tool's `EnemyForgeRootMotion` is deliberately not in the project.
- **An attack that plays a generated clip names it** (`EnemyAttackData.clip`). The pipeline mapping
  knows only the four canonical clips; a generated one is otherwise unreachable, silently.
- **Measure the travel on the imported clip, never on the sidecar.** `forward_m` is source motion,
  scaled ~×1.3 onto this rig at export. The test reads the clip, so the data cannot drift from the art.

**Invariants specific to this path**
- **On an IMPORTED body the wind-up silhouette is carried entirely by `bodyOffset` / `bodyEuler`.**
  `MiniBossFactory` gives forge models empty arm pivots and a 3 cm cube for `EnemyVisuals.weapon`, so the
  `armWindup` half of a `WindupPose` drives nothing the player can see. Unauthored, every attack on such a
  body renders the SAME silhouette — measured at IoU 1.00 across the Revenant's four. See
  ENGINEERING-LOG.md.
- The whirl writes `SpinRoot.localRotation` and NOTHING else. It never touches a collider, a range, a
  cone or a time — the impact test is exactly the one every other enemy uses.
- **The spin speed NEVER changes.** Not between passes, not into an impact, not during a strike. Any
  easing at all is a speed change, and a speed change every 0.69 s is a pulse rather than a spin. This
  is the property the whole presentation rests on; `SpinFilm` reports per-frame yaw step, which is flat
  when it holds and visibly ramped when it does not.
- **`spinDegPerSec` × the beat must be a whole number of revolutions.** 2087 × 0.69 = 1440 = 4 turns.
  Otherwise every pass needs its arc corrected to land square-on, a corrected arc is a changed speed,
  and the pulse returns quietly through arithmetic. Retuning the beat means retuning the rate.
- **The frame-rate guard clamps the RATE, never the phase.** `ResolveRate` may slow the spin on a
  machine that cannot render it; it may never make the body arrive late. General form: a performance
  guard may degrade how something *looks*, never *when it happens*.
- `SpinRoot` is its own transform, not the model root. The generic clips keep their root curves, so the
  Animator writes the model's local rotation every frame; the whirl on the same transform would be two
  writers on one channel and would stutter or vanish with nothing in the console.
- Playback runs on SCALED time (`Animator.updateMode = Normal`), so hitstop freezes the puppet with
  everything else. `PlayerDelta` is for player-driven motion only (rule 1).
- A clip name the component asks for and the FBX does not have fails **silently** at runtime —
  `CrossFade` to a missing state is a no-op. `MiniBossFactory` validates the nine slot names AND every
  `EnemyAttackData.clip` in the enemy's moveset at build time, against the imported clips and the
  baked table both.

**Invariants**
- `EnemyController` never names `NavMeshAgent` or `EnemyVisuals`. All movement goes through
  `IEnemyLocomotion`, all presentation through `IEnemyPresentation`. That is the seam that lets a bought or
  authored animated rig drop in without touching the brain.
- **Presentation timing is DATA-driven, never animation-driven.** `Telegraph` receives the duration and must
  fit it. If a clip length ever dictates the wind-up, the `cueLead` guarantee breaks and attacks stop being
  reliably parryable.
- The brain owns facing: `NavMeshAgent.updateRotation` is false, so nothing else rotates the transform.
- Leaving `Recover` honours `aggroLocked`, or a parried boss wakes before its arena trigger.
- Every attack's `range` must cover `preferredRange + commitTolerance`, or committed attacks whiff.
- **The far-band commit (2026-09-04).** Outside the commit band the enemy attacks IF AND ONLY IF
  `EnemyMoveset.HasEligible(dist)` — an entry whose range band contains the distance (the charge, the
  leap); the selector's fallback-to-anything is not consulted there, so nothing without a closer is thrown
  from range, and the facing gate is 25° rather than 50° because a lunge is aimed where the body faces
  at the cue. The chosen combo's own `lungeDistance` does the closing.

## Settings

```
SettingsMenu (UI, both prefabs)      edits SettingsStore.Current, Save() on every notch
   → SettingsStore                   PlayerPrefs under vg1.settings.*; raises Changed
   → SettingsApplier                 DontDestroyOnLoad singleton, self-bootstrapped via
        │                             RuntimeInitializeOnLoadMethod -- nothing to place
        ├→ InputReader.ApplyBindingOverrides                 (24 player-facing keyboard/mouse binds;
        │                                                     interactive rebind excludes reserved
        │                                                     developer, console and pause controls;
        │                                                     loaded JSON is filtered back to those exact
        │                                                     bindings and cannot override reserved actions)
        ├→ PlayerLook.mouseSensitivity / stickSensitivity   (public fields; PlayerLook is
        │                                                     never edited by settings code)
        ├→ CameraFX.baseFov + cam.fieldOfView               (CameraFX rewrites FOV every frame,
        │                                                     so both, and once more a frame after
        │                                                     sceneLoaded because CameraFX.Start
        │                                                     captures baseFov)
        ├→ QualitySettings.SetQualityLevel, THEN vSyncCount, THEN targetFrameRate
        ├→ Screen.SetResolution                             (builds only; no-op in the editor)
        │    saved 0/0 = NATIVE, resolved from Display.main.systemWidth/systemHeight rather than
        │    the already-resized game window; the menu keeps NATIVE as a real index-zero choice
        ├→ Volume.profile (runtime CLONE, never sharedProfile): Bloom.intensity =
        │                                                     authored x scale; FilmGrain on/off
        ├→ MovementPose.Enabled                              persisted ARM MOVEMENT toggle; visual only
        └→ AudioManager.masterVolume / .musicVolume         (2026-09-06) = AUTHORED x scale, the same
                                                              shape bloom uses. The authored pair is
                                                              measured ONCE per AudioManager instance
                                                              (Managers.prefab ships 0.7 / 0.45) and is
                                                              deliberately NOT re-measured on a scene
                                                              load: re-reading the same instance would
                                                              read back our own output and compound it.
   applied on Awake, on every sceneLoaded (+1 frame), and on every Changed.
```

**Audio rows (2026-09-06).** The panel carries MASTER VOLUME and MUSIC VOLUME, in the AUDIO section,
last. Both are `SettingsData` floats persisted at `vg1.settings.volMaster` / `volMusic` with every other
setting, both are percent sliders in 5% notches, and both are heard the frame they move: `Commit()` →
`SettingsStore.Save` → `Changed` → `SettingsApplier.ApplyAudio`, and `AudioManager` reads both fields
live (music every `Update`, SFX on every one-shot). `SettingsMenu.Audition` also ticks one `Sfx.Click`
per 0.09 s while a volume row is moving, so master is audible on a silent screen.

> **There is no SFX row, and there must not be one until there is a bus.** `AudioManager.PlayInternal`
> multiplies a one-shot by `masterVolume × trim` — there is no third gain to point a slider at.
> `SettingsAudioTests.ThereIsNoSfxRow_BecauseThereIsNoSfxBus` fails if one appears.

**Flow meter (top-right, under the radio) (2026-09-07).** `HudBuilder` emits the `FlowMeter` group
into the band the removed BEST RUNS pane left (`BestRunsTop` -164 to `BestRunsBottom` -272, width 300).
`FlowMeterView` reads, per frame: `FirstPersonMotor.SpeedMultiplier` -> the `x1.42` value (the aggregate
every speed source writes, which is what "from any source" means), `FirstPersonMotor.HorizontalSpeed`
-> the `27 M/S` line, `ParrySurge.Stacks` -> the five pips, and `ParrySurge.SecondsToNextDrop` -> the
ember decay hairline (a `BarView` - anchors, never `Image.fillAmount`). The PARRY CHAIN is counted
**in the view** from `GameEvents.ParryResolved`: `Perfect` increments, `Blocked`/`Hit` reset,
`PlayerDied`/`PlayerRespawned` clear. It is display-only by construction - nothing in the game may read
it back. The pane's `CanvasGroup` sits at alpha 0 whenever there is no boost and no chain, lingering
1.5 s so a LOSS is watchable, and the root is in `HUDController.editorHiddenRoots` (F10 takes run
readouts off screen). A boost from a non-surge source shows its multiplier with the pips dark, which is
truthful rather than a lie about stacks.

**Rebindable keys (one, so far) (2026-09-07).** `SettingsMenu` row `WeaponTwirlKey` ("FLOURISH KEY",
CONTROL section) -> `InputReader.BeginWeaponTwirlRebind(onComplete, onCancel)` -> Input System
`PerformInteractiveRebinding` (pointer deltas and sticks excluded, ESC cancels) -> a control-path
**string** back to `SettingsMenu` -> `SettingsData.weaponTwirlBinding` -> `SettingsStore.Save`
(PlayerPrefs `vg1.settings.bindTwirl`) -> `SettingsStore.Changed` -> `SettingsApplier.ApplyBindings`
-> `InputReader.ApplyWeaponTwirlOverride`. `InputReader.Awake` applies the same stored value before
the map is enabled, so a level that loads without the applier's deferred pass still starts on the
player's key. `""` means "no override": `SettingsData.SanitizeBindingPath` empties anything malformed,
over-long or bound to escape, and an empty value calls `RemoveBindingOverride(0)` - the action can
never end up bound to nothing. Consumer: `WeaponTwirl.Update` polls `InputReader.WeaponTwirlPressed`
behind `GameManager.IsPlaying`. Hard rule 2 is intact - `SettingsMenu` contains no
`UnityEngine.InputSystem` reference.

> **The flourish is cosmetic and must stay that way.** `WeaponTwirl` spins `WeaponViewmodel.grip`'s
> local rotation, which the viewmodel never writes (it only sets that transform's local POSITION, when
> aligning a newly equipped model) and which nothing downstream reads. It cannot reach a swing, a parry
> window or a hitbox. Delete the component and the viewmodel behaves exactly as it did.

**Arm movement (2026-09-08).** The CONTROL section's `ARM MOVEMENT` row persists
`SettingsData.armMovement` under `vg1.settings.armMovement`, default ON. `SettingsApplier` pushes it to
`MovementPose.Enabled`; OFF returns a zero additive pose so both viewmodels ease back to their authored
rest/combat animation instead of freezing mid-offset. It never changes a hitbox, attack timing or motor state.

**Invariants**
- Sensitivity is applied OUTWARD onto `PlayerLook`'s public fields. Settings code never edits
  `PlayerLook.cs`; that is the architecture, not a workaround for file ownership.
- Opening from the pause menu takes its own `TimeScaleController.Request(0f)` handle (rule 1) and
  re-enables `PauseMenu` one frame late, because `PausePressed` is true for the whole frame.
- Bloom writes the runtime clone. Writing `sharedProfile` from play mode dirties the asset on disk.
- Sliders are stock UGUI `Slider`s driven by anchors — never `Image.fillAmount` (rule 5).
- **A volume is a SCALE on the shipped mix, not an absolute gain.** 100% is the mix the audio pass
  authored, so retuning `Managers.prefab` moves every player's 100% with it, and `MasterGain` /
  `MusicGain` clamp to 0..1 because `AudioSource.volume` above 1 clips rather than amplifying.
- **The panel's layout is arithmetic, not eyeballing.** `SettingsPanelKit.LastRowY` is the same pure
  function the builder walks; `SettingsAudioTests` runs it and proves the last row clears BACK / RESET,
  that both clear the glass card, and that BACK stays on screen at **21:9** — where the canvas scaler
  leaves only 935 logical units of height and the buttons used to sit at y −484, off the bottom.

### Stamina

```
FirstPersonMotor (dash, wall run entry, wall jump)
  → PlayerStamina.TrySpend(cost, StaminaAction)   whole or nothing; never partial
       dash 30 | wall-run entry 12 | wall jump 12          (a run exit / exit-grace jump is FREE)
  → PlayerStamina.Drain(22/s, used)  every wall-run step → false at 0 → EndWallRun(Exhausted)
  (`IsWallSurging` remains only as dormant legacy motor compatibility; no shipped item activates it)
  PlayerStamina.Update   dt = PlayerDelta (rule 1); regenDelay 0.45 s after any spend, then
                         +45/s grounded, +18/s airborne, to max 100. Infinite (F8 god mode) never spends.
  ⇢ GameEvents.StaminaChanged(cur, max)  → StaminaView.bar (BarView, 3 ticks at 30/60/90)
  ⇢ GameEvents.StaminaRefused(action)    → StaminaView: bar.Flash(red), label names the ability 0.8 s
                                          → PlayerFeedback.OnStaminaRefused: AudioManager.Play(Sfx.Refuse,
                                            pitch varies by action) (2026-09-06 audio pass — a refusal was
                                            previously silent and read as a dropped input, worst on the slide)
  StaminaView.Update  pips DASH / AIR / WALL ← FirstPersonMotor.CanDashNow / !AirDashUsed / CanWallRunNow
                      (stamina AND cooldown AND the air charge: "can I, this frame")
                      CanvasGroup alpha 0.45 when full and idle, 1.0 within 1.2 s of a spend or refusal
  Tuning: PrefabFactory.BuildPlayer → Player.prefab → PlayerStamina; asserted by StaminaTunablesTests.
  HUD:    HudBuilder.StaminaBlock → HUD.prefab (y 104, between health and posture).
```

**Invariants**
- **A full bar always affords a full wall run** (12 + 22 x 1.75 = 50.5 < 100). `LevelArcAnalyzer` models
  every run at full duration; if this ever fails, every wall line is authored against a run nobody can take.
- **Every refusal is named on screen.** `TrySpend` raises before returning false. A silent refusal reads as
  a dropped input, which is the exact complaint this system exists to answer.
- Regen and delay run on `PlayerDelta`, unscaled UI on `Time.unscaledTime`.

### Forgiveness — corner correction and the near-miss landing (MOVEMENT-PRINCIPLES rule 4)

```
FirstPersonMotor.Update, the final cc.Move                       (laws: ForgivenessMath, pure)
  before the sweep, airborne && falling && !sliding && !wallRunning && !IsDashing && hs ≥ ledgeCatchMinSpeed
     → raycast DOWN from (feet + velocity dir × (radius + 0.12), + ledgeCatchMetres + 0.05)
     → ForgivenessMath.LedgeCatches(feetY, hit.y, ledgeCatchMetres 0.22, vel.y, hit.normal.y ≥ 0.7, hs)
        → disp.y += NudgeStep(top − feet + 0.02, ledgeCatchLiftSpeed 6, dt)   bounded lift, no velocity added
  after the sweep, CollisionFlags.Above && vel.y > 0
     → Physics.CheckCapsule at ±right / ±forward × cornerCorrectionMetres (0.18)
        clear ⇒ cc.Move(offset), vel.y KEPT (a corner)        blocked everywhere ⇒ vel.y = 0 (a ceiling)
  Tuning: PrefabFactory.BuildPlayer (four fields, rule 9). Tests: ForgivenessTests (laws, 20/60/240 fps),
          FeatureTests Movement: Forgive_NearMissLandsOnTheLedge / AHalfMetreMissIsAMiss / AWallSideNeverCatches.
```

**Invariants**
- **Forgiveness never adds reach.** The corner nudge is capped at its margin and only fires when a capsule
  inside that margin is clear; the catch only lifts from within `ledgeCatchMetres` and adds no velocity.
  The level's reach contract (`LevelArcAnalyzer`) is unchanged by either.
- **A wall side never catches.** The probe's hit must be a floor (normal.y ≥ 0.7); falling past a face is a fall.
- **Only the honest cases.** Never while rising, sliding, wall running or dashing; never from a standing drop.

### Perfect timing — stamina back for a move landed on its moment

```
FirstPersonMotor                                      (laws: PerfectMath, pure; windows on the motor clock)
  UpdateWallRunPerfectWindow predicts the earliest clock / speed-decay / stamina release
     → PerfectMath.WallJumpHybridIsPerfect(secondsToRelease, secondsSinceRelease, 0.20)
       ≤ 0.20 s BEFORE a predictable release → PlayerFeedback "WALL EXIT [SPACE]"
       ≤ 0.20 s AFTER any natural release     → the same forgiveness half
     → TryWallJump inside either half → capped +2 m/s tangent and Perfect(WallJump, 20)
  ground jump fires while IsDashing && !LastDashWasBurst   (a grounded dash is jump-eligible for its whole
     length -- dashFromGround -- and the jump ENDS the dash so vel.y survives; see ENGINEERING-LOG)
     → PerfectMath.DashJumpIsPerfect(now − dashStartedAt, minDelay 0.06, window 0.10)
       [0.06, 0.16] s after the dash fired — never the same frame
       → PlayerFeedback "DASH JUMP [SPACE]" while open → Perfect(DashJump, 30 = the dash)
  the grapple burst fires
     → PerfectMath.BurstIsPerfect(now − pullBurstOpenedAt, 0.12)  first 0.12 s of the 0.30 s window
                                                                            → Perfect(GrappleBurst, +30)
  Perfect(kind, amount)
     → PlayerStamina.Refund(amount)   PerfectMath.Refund: clamped to max, never a debit, regen delay untouched
          ⇢ PlayerStamina.Refunded(got), GameEvents.StaminaChanged        (the bar visibly refills — rule 6)
     → LastPerfectKind / LastPerfectTime
     ⇢ OnPerfect(kind, got)  → PlayerFeedback.OnPerfect: Sfx.ParryCue ×1.5 + Sfx.Swing ×1.9 (rule 7: no new
                                Sfx), FovKick(feel.perfectFovKick 3), prompt "PERFECT" for feel.perfectPromptSeconds 0.6
  A MISS reaches none of this: the ordinary move, at its ordinary cost, with nothing said.
  Tuning: PrefabFactory.BuildPlayer → Player.prefab (seven motor fields); DataFactory → GameFeel.asset (two).
  Tests:  PerfectTimingTests (laws, edges, the 20/60/240 fps width check, shipped values);
          FeatureTests "PerfectTiming" (a same-frame dash+jump is ordinary and costs; a jump 0.10 s into
          the dash is perfect and refunds the dash).
```

**Invariants**
- **A perfect is anchored to a physical moment, never a frame** (MOVEMENT-PRINCIPLES rule 4): the wall
  letting go, the dash's launch, the pull landing. Windows are seconds on the motor clock: 0.20 s per
  wall-release side, 0.10 s for dash-jump, 0.12 s for burst. Wall and dash announce the exact [SPACE]
  action, and
  `PerfectTimingTests.EveryWindowIsTheSameWidthAt20_60_240Fps` holds their width to one frame at each rate (rule 8).
- **A miss is the ordinary move.** No penalty, no message, no branch. The dash-jump's minimum delay is what
  keeps a mashed dash+jump from being the perfect.
- **The refund never overfills and never debits**, and it does not reset the regen delay: a refund is not a spend.
- **No new button** (rule 5). Every perfect is an expression of the controls the player already has.
- **The refund is felt on the body AND the bar** (rule 6): `OnPerfect` and `PlayerStamina.Refunded` both fire;
  the HUD is expected to subscribe to one of them.

### Movement — wall run

```
InputReader (jump/dash only; wall running has NO binding -- entry is by arriving correctly)
  → FirstPersonMotor.Update      dt = TimeScaleController.PlayerDelta   (rule 1)
     → TryWallRun()               cheap gates: airborne (NO coyote wait), budget <= maxWallRuns 6,
                                   off a 0.20 s cooldown, fall speed < 9, horizontal speed >= 6
        → FindRunnableWall         2 spherecasts; refuses lastWallNormal via sameWallCosineLimit
        → a slide yields           sliding through coyote → EndSlide (refused only under a ceiling)
        → WallRunMath.CanEnter     approach cos <= 0.80 (53 deg off the face), TOTAL flat speed >= 6,
                                   look-along cos >= -0.05 (only looking backwards refuses)
        → PlayerStamina.TrySpend(12, WallRun)   last, so a refusal means "you would have run";
                                   SKIPPED while IsWallSurging (so is the TooSlow gate: minEntry 0)
        → Enter                    ALL horizontal speed redirected down the run (cap 22), floor vy at +3
                                   → OnWallRunStarted; PlayerLook.SetRollBias(+/-13 deg)
     → AdvanceWallRun(dt)         ProbeWall (lost? ride wallRunLostGrace 0.15 s on the last normal)
                                   → WallRunMath.Advance: 2 ms substeps, gravity x0.10 → x0.60 on t^2,
                                   tangential x exp(-0.35 h), top-up 14 m/s^2 toward wallRunTopSpeed
                                   13.75 (1.25x a sprint: the wall is FASTER than the floor) while
                                   holding forward
                                   → cc.Move(disp - n * 2.5 * used)    (pressed to the face)
                                   → PlayerStamina.Drain(22/s)  → Exhausted at 0; SKIPPED while surging
                                   (surge: topSpeed and accel ×1.5, from WallRunSettings, never the fields)
        → ShouldEnd                Expired (1.75 s) | Decayed (< 4 m/s) | LostWall | Landed |
                                   Exhausted | Cancelled (stagger, dash). Unspent dt → the air branch.
                                   A natural end opens wallRunExitGrace 0.15 s: a jump inside it is
                                   still the run exit below, not a whiff.
     → jump while running         WallRunMath.Exit: +4 along, +7 out, vy 10, clamped to dashSpeed
                                   → EndWallRun(Jumped) → PlayerLook.AddRollKick(7 deg, 0.28 s)
  Camera: PlayerLook sums rollBias + rollKick as pivot local Z. Unscaled time. Aim-invariant.
  Tuning: PrefabFactory → Player.prefab → FirstPersonMotor.WallRunSettings, and the SAME prefab
          feeds LevelArcAnalyzer.MoveProfile.WallRun -- one source for the motor and the analyser.
```

**Invariants**
- Every timer and integration on `PlayerDelta`. Hitstop can neither freeze nor extend a run.
- Velocity is never derived from `CharacterController` state, so the resized-controller trap that
  produced the framerate-dependent slide is structurally absent; `TheRunIsIdenticalAtEveryFramerate`
  integrates at 500/144/90/60/30/12 fps and holds duration to ±5 ms, distance to ±1 cm.
- `wallRunSpeedDecay` must sit inside `(ln(minEntry/minSustain), ln(top/minSustain)) / maxDuration`
  or one end condition is unreachable. Asserted. Shipped: (0.23, 0.71), decay 0.35; a 6 m/s entry bleeds
  out at 1.16 s, a sprint rides the 1.75 s clock.
- **Entry judges intent, never geometry-luck.** Total speed (not the tangential projection), 53° of
  approach, any look that is not backwards, no coyote wait, a slide yields. The 2026-09-03 log entry
  lists what each of the old gates was silently refusing.

### Movement — slide and dash feel

```
FirstPersonMotor ⇢ OnDashed
  → PlayerFeedback.OnDashed      Sfx.Dash + Sfx.Swing pitched 1.55 (two voices, rule 7: no new entry)
     dirLocal = InputReader.MoveAxis on the SAME frame (camera yaw == body yaw)
     → CameraFX.FovKick(feel.dashFovKick 8°), ChromaticPulse(0.35, 0.18 s)
     → DashImpulse.FromDash(dirLocal, pitch 0.9, roll 1.4, offset 0.06)   pure math, unit tested
        → CameraShake.Kick(euler, offset, 0.14 s, KickAttackFraction)     no yaw, ever
     → DashFx.Fire(dirLocal, 0.22 s, alpha 0.85)   speed lines, CAMERA space, fixed LineRenderers,
                                                    peak channel 0.90 < 1.05 bloom threshold, unscaled time
FirstPersonMotor ⇢ OnSlideStarted / OnSlideEnded
  → PlayerFeedback                one-shot entry punch (FovKick, CameraShake pitch kick) / end punch (-2.5°)
     → SlideFx.Begin() / End()    End() releases the FOV hold (CameraFX.FovHold(0))
PlayerFeedback.Update (every frame, unscaled)
  → SlideFx.Tick(slideFovHold 8, slideRollDegrees 3.5, sparkRate 5, dustRate 34, scrapeVolume 0.22)
     reads motor.IsSliding + HorizontalSpeed → SlideImpulse.FovForSpeed / Normalised speed band
        → CameraFX.FovHold(...)   HELD, proportional to speed still carried; exactly 0 at slideEndSpeed
        → scrape gain/pitch, grit rate ride the same fraction; grit shed into a SCENE root (a trail)
FirstPersonMotor ⇢ OnWallRunStarted / OnWallRunEnded          (PlayerLook's 13° lean AWAY from the face + 7° exit roll kick back toward it are separate)
  → PlayerFeedback.OnWallRunStarted   Sfx.Footstep 0.50 @1.12 + Sfx.Land 0.28 @1.15 (the catch; rule 7: no new entry)
     nLocal = InverseTransformDirection(motor.WallRunNormal) INSIDE the event — the motor zeroes it before the end event
     → WallRunImpulse.AttachKick(nLocal, wallRunAttachOffset 0.03)   translation TOWARD the wall, no rotation
        → CameraShake.Kick(…, wallRunAttachTime 0.12, KickAttackFraction)
     → WallRunFx.Begin(nLocal)         caches the normal for the end handler
  → PlayerFeedback.OnWallRunEnded     reads motor.LastWallRunEnd (written before the raise)
     → WallRunImpulse.EndKick(why, nLocal, dropPitch 1.4, dropOffset 0.03, lostDrift 0.02)   pure, per-ending:
          Jumped / Landed / Cancelled → false (exit roll kick, OnLanded, or the dash own it)
          Expired / Decayed / Exhausted → DOWNWARD sag (pitch +1.4°, head −0.03 m), DropAttackFraction 0.30 (a give, not a hit)
          LostWall → drift 0.02 m AWAY along the normal
        → CameraShake.Kick(…, wallRunDropTime 0.16)
     Exhausted only → Sfx.Land 0.30 @0.70 (the quieter, lower thud)
     → WallRunFx.End()                 arms a one-frame FovHold(0) release
PlayerFeedback.Update (every frame, unscaled) — AFTER SlideFx.Tick, deliberately
  → WallRunFx.Tick(wallRunFovHold 3.5, wallRunStepDistance 1.6, wallRunGritRate 44, wallRunStepSparks 3)
     live = running && motor.IsWallRunning && IsPlaying (ends from live state if the event was swallowed)
        → CameraFX.FovHold(3.5) while live, FovHold(0) once on release; otherwise leaves the channel to SlideFx
        → distance along the wall (3D path) ≥ 1.6 m → returns true
        → GRIT off the FOOT CONTACT (feet + 0.28 up − normal × capsule radius): pooled additive cubes (40, 0.05 m,
          0.38 s, peak channel 0.55 = no bloom) in a scene-level WallRunGrit root, thrown back down the run and a
          little off the face, at WallRunImpulse.GritRate(speed/topSpeed, elapsed/maxDuration, 44/s) — linear in
          speed, falling to 40% by the end of the loan, the same shape as StepPitch
        → on each foot-tick: SlashFx.Sparks(contact, 3) — the grit is the contact, the sparks are the step
           → PlayerFeedback: Sfx.Footstep wallRunStepVolume 0.40 @ WallRunImpulse.StepPitch(elapsed/maxDuration: 1.30→1.12), dip += 0.008
Tuning: GameFeelSettings.dash* / slide* / wallRun* — written by DataFactory (rule 9), defaults in PlayerFeedback are fallbacks only.
```

**Invariants**
- `ParryImpact.Shockwave` → `SlashFx.Ring` (teal `#A8E6DA`, 0.34 m, 0.18 s, normal = level direction
  to the attacker, origin = `PlayerCombat.ContactPoint` + 0.12 m). The world's confirmation that a
  Perfect resolved, so the deflect reads without reading the `PERFECT` word; numbers in `ParryImpulse`,
  peak 0.98, no bloom exception. Reachable only from `ParryController.NotifyDeflected`.
- `*Impulse` is the math, `*Fx` is the component — the `ParryImpulse` / `ParryImpact` split, so every curve is
  an EditMode test, not prose.
- **Force, not light.** Nothing in a slide or dash crosses the 1.05 bloom threshold; emission on an enemy means
  "you deflected" and traversal never speaks that language.
- The slide's sustained layers are driven from the motor's **live state** every frame, not from the events
  alone: a respawn or a disabled player can swallow `OnSlideEnded`, and a held FOV that never released would be
  wrong for the rest of the run. `WallRunFx` follows the same rule.
- **`CameraFX.FovHold` has one writer at a time, kept so by ORDER.** `SlideFx.Tick` writes it every frame
  (0 when not sliding); `WallRunFx.Tick` runs after it in `PlayerFeedback.Update` and writes only while a run is
  live plus one release frame. A slide is grounded and a wall run is airborne, so the motor never has both; do
  not reorder the two ticks, and do not add a third holder without a real arbitration.
- **A wall run's rotation belongs to `PlayerLook`** (the 13° lean, the 7° exit roll kick). The feel package adds
  translation, a small FOV hold, feet and the let-go — never a second roll or any yaw.
- **The wall-run end is told apart by `WallRunEnd`, and `Jumped` gets nothing extra.** The exit roll kick is the
  loudest cue a run can produce; stacking a sag on it would blur the one ending that is the player's own doing.
  The sag is reserved for the wall letting go, because that is the ending the exit-grace jump has to be learned
  against.

### The player body and the slide's weight — `PlayerBody` + the held rumble + the eye spring

```
Player.prefab (PrefabFactory.BuildPlayerBody — one call in BuildPlayer)
  Body (PlayerBody)                         under the ROOT: yaws with the look, level under a pitched lens
    Torso ─ Hips, Chest                     pivot AT the hip joint (0.92); chest tops out at 1.34 (< 1.35 lens rule)
    Leg_L ─ Thigh ─ Knee ─ Shin, Boot       hip 0.92, thigh 0.46, shin 0.44: sole on y 0.00
    Leg_R ─ ...                              layer Player, no colliders, casts shadows, receives none

PlayerBody.Update()  (reads FirstPersonMotor's public state only; subscribes OnLanded / OnSlideEnded)
   → blend  = SlideImpulse.Spring(…, target = IsSliding ? 1 : 0, 6 Hz, ζ 0.6)   the THROW, closed form
   → phase += TimeScaleController.PlayerDelta * HorizontalSpeed * 1.3           gait, rule 1, in step with the hand bob
   → base pose: ground/wall = gait swing ±30° / knee 42° on the forward swing; air = 14° / 40° tuck
   → wall run: legs roll 14° INTO the face (WallRunNormal), torso 6° off it
   → kneeDip += 34° × LastLandingSpeed/22 on OnLanded, +12° on OnSlideEnded; springs back at 9/s
   → slide pose (lerped by blend, may overshoot to 1.15): hips −0.52 / +0.25 z, torso −22°,
     lead leg 70°/12°, trail 60°/26°; leg root yaws to the VELOCITY, not the look
   → writes: Body.localPosition/rotation, Torso, Leg_L/R, Knee_L/R localRotation   (its own transforms only)

PlayerFeedback.Update()
   → crouch = SlideImpulse.Spring(…, target = IsSliding ? 0.55 : 0, feel.slideCrouchHz 4.5, feel.slideCrouchDamping 0.55)
        the eye PLOPS ~0.065 m below the slide height and settles up; rises past neutral on stand-up
   → SlideFx.Tick(…, feel.slideRumble)
        → CameraShake.SetRumble(SlideImpulse.RumbleAmplitude(SpeedFraction, 0.006))   FOURTH held channel
PlayerFeedback.OnSlideEnded()  → + Sfx.Land at 0.30 / 1.05: weight arriving on the feet (rule 7: no new Sfx)
```

**Invariants**
- **`PlayerBody` owns only its own transforms** (Body, Torso, the leg and knee pivots). It never writes the camera pivot, ShakeRoot, the collider or the motor; a second writer on any of those would fight the existing owners.
- **The body never rises above y 1.35** in any pose the rest-pose factory builds; `SlideFeelTests` measures the prefab's renderer bounds. The slide pose only ever LOWERS it.
- **The slide throw and the eye plop are closed-form springs** (`SlideImpulse.Spring`), never explicit integration: the same shape at 20 and 240 fps. `SlideFeelTests.TheSpringIsFrameRateIndependent` holds it.
- **`CameraShake.SetRumble` has one writer** (`SlideFx.Tick`, every frame, 0 when not sliding), exactly like `SetRoll` and `CameraFX.FovHold`. Zero at `slideEndSpeed` by construction (quadratic in speed fraction).
- **The gait phase advances on `PlayerDelta`** (rule 1) at the same 1.3 rad/m as the viewmodel bob, so the hands and feet stride together.
- **The motor's slide numbers are untouched** (boost, friction, duration, end speed, steer). Everything in this pass is presentation.


### Traversal pieces — balloons, water, the grapple burst (2026-09-04 pivot)

Three additions to the movement kit. None of them writes a velocity: each one calls a motor entry point
(`Launch`, `RearmDash`, `TouchWater`) or arms a window the motor itself opens, and the arithmetic lives in
`TraversalMath` as pure functions (no scene) so `PivotMovementTests` can drive it and the motor cannot drift
from the tests.

```
BALLOON  (Assets/Prefabs/Balloon.prefab, PrefabFactory.BuildBalloon; placed by LevelDefinitionBuilder from
          LevelDefinition.balloons[] or by SandboxBuilder's yard column, both through TraversalBuilders.BuildBalloon)
  player's CharacterController enters the SphereCollider trigger (Interactable layer)
    → Balloon.Pop(motor)
        motor.IsDashing ?  motor.RearmDash()          dashReadyAt = now, airDashUsed = false; the dash in flight ENDS
                                                       and its carry is trimmed to launchCarryCap (pop → aim → dash;
                                                       carries on untouched (TraversalMath.DashesThrough)
                        :  motor.Launch(launchSpeed)  vel = TraversalMath.Launch(vel, up, launchCarryCap 9): vertical REPLACED (a
                                                       capped jump), horizontal kept; air dash, wall-jump and
                                                       wall-run budgets reset; slide / wall run / dash end;
                                                       then the FLOAT: for launchFloatSeconds (0.45) gravity
                                                       x launchGravityScale (0.55), air steer x launchSteerBoost
                                                       (1.6) -- the pop hangs and is aimed at the next orb
                                                       ⇢ OnLaunched → PlayerFeedback: FovKick(feel.balloonFovKick),
                                                         nose-up pitch kick, small hop dip
        orb hidden, collider off; SlashFx.Sparks + Ring (peak 1.0, no bloom); Sfx.Jump ×1.3 + Sfx.Land soft
        respawn after respawnSeconds with a scale-in (unscaled time); GameEvents.PlayerRespawned restores it

WATER    (no prefab: TraversalBuilders.BuildWater from LevelDefinition.waters[] / the yard lane)
  WaterVolume (BoxCollider trigger = sheet + boostHeight 0.35 above it, Interactable layer; the sheet mesh
  has NO collider — the floor under it is what you stand on)
    OnTriggerEnter / OnTriggerStay → motor.TouchWater(volume)   waterUntil = now + waterGrace (0.15) — a
                                                                 stay-refresh on the motor clock, never an Exit
  FirstPersonMotor.Update
    inWater = now < waterUntil;  flow = volume.Flow (TraversalMath.Flow: normalised × flowSpeed)
    slide branch, grounded + in water:
                               rel = TraversalMath.WaterStep(hv − flow, wish, WaterFloorSpeed, waterAccel, dt)
                               hv  = rel + flow
                               WaterFloorSpeed = groundSpeed × waterSpeedScale (1.35 → 14.85 m/s)
                               no groundFriction, no groundOverspeedDecay, turned at 30 m/s² not 90;
                               slideEndsAt is pushed every frame (no decay end, no cap):
                               a slide on water ends only off the water or on a jump
    grounded, not sliding:     ordinary ground acceleration, friction and overspeed decay; water contact alone
                               never creates a skate or adds the flow conveyor
    airborne:                  ordinary air carry decay and air soft cap, even while inside the shallow trigger;
                               a slide-jump carries its earned speed but cannot turn water contact into a cruise
    Teleport clears it.  PlayerFeedback reads motor.InWater as an EDGE: FovKick(waterEnterFovKick) + soft Land
    on entry; WaterFx.Tick every frame: spray (SlashFx.Sparks, cold blue) + a synthesised hiss loop on its own
    AudioSource, both riding speed / WaterFloorSpeed

GRAPPLE BURST
  FirstPersonMotor.EndPull(arrived = true)  →  pullBurstPending, pullBurstPendingUntil = now + pullBurstHold (3 s)
  Update: pending && CanMove → pullBurstUntil = now + pullBurstWindow (0.30 s)      opens only once control is
                                                                                    back: ExecuteInteractor holds
                                                                                    CanMove false for the deathblow
  dash gate: canAct && dashRequested && (burst || the ordinary gate)   burst short-circuits cooldown, air charge
                                                                       AND the stamina spend
  burst: dashSpeedNow = TraversalMath.BurstSpeed(dashSpeed, pullBurstMultiplier) = 27.5 (= maxHorizontalSpeed)
         dashReadyAt / airDashUsed UNTOUCHED (the dash you had is still yours); LastDashWasBurst = true
         ⇢ OnDashed → the dash package + FovKick(+feel.burstFovKick) and a longer chromatic pulse
```

**Invariants**
- **A traversal piece never writes a velocity.** Balloon and WaterVolume call `Launch` / `RearmDash` / `TouchWater`;
  the motor decides. A second writer on `vel` would be the slide-vs-agent fight in a new costume.
- **A launch REPLACES the vertical speed** (`TraversalMath.Launch`). The height a balloon buys is the same every
  time, which is what lets a level be authored against it (3.27 m at 14 m/s / −30).
- **Water rewards an active grounded slide; it is not an ice-physics mode.** Only that slide calls
  `WaterStep`, which targets `max(floor, carried)` and never slows the committed slide. Running, standing and
  jumping use the ordinary ground/air laws, so merely touching the sheet never throws or carries the player.
- **Water is a stay-refreshed touch, never an Enter/Exit pair.** A CharacterController disabled for a teleport
  sends no Exit; a grace on the motor clock cannot be left on.
- **The burst window opens when control returns, not when the pull lands**, and it is forfeited after
  `pullBurstHold` rather than fired stale.
- **No held lens channel for water.** `FovHold`, `SetRoll`, `SetRumble` each keep one writer (SlideFx); water
  gets a one-shot kick on entry and textures (spray, hiss) only — a slide crossing water must not have two
  writers on the lens.
- **The motor's existing numbers are untouched.** Six new fields, all written by `PrefabFactory.BuildPlayer`
  and asserted by `PivotMovementTests`.

### Level Studio vocabulary and zone metadata (foundation)

`LevelDefinition.zones[]` owns physical zone bounds, stable zone ID, canonical/split names, aliases,
order and display colour. Existing gameplay arrays retain their serialized names and values; this
foundation adds `LevelObjectMeta` (stable object ID, editable friendly name, explicit zone override)
to their records. `LevelDefinitionAuthoring.Apply` snapshots metadata by named authoring owner before
regeneration, restores IDs/labels/overrides onto recreated records, then writes zones and assigns IDs
after every object array and split endpoint exists. `ApplyLevelStudioMetadata` is also a metadata-only
entry point for preserving the current shipped layout; the lead must invoke/save it through Unity.
`LevelObjectCatalog.SpawnTypeKey` is the shared read-only source for enemy-family spawn IDs.

`LevelObjectCatalog.Enumerate(definition)` exposes the original array index/data reference, metadata,
authoring anchor and type key through `LevelObjectRecord`. Null arrays/entries are skipped without
renumbering; missing legacy metadata is hydrated in place. IDs are assigned only by `AssignZones`.
The runtime editor's existing serialized `LevelPieceKind` is independent and unchanged. Spawn type keys
come from an explicit prefab-key family map (`pshooter_enemy01/02/03` -> `Sentry/HeavySentry/SurgeTurret`,
the three campaign legendaries -> `ThirteenthShade/IronPenitent/AshenChorister`, `Boss` -> `HollowWarden`);
unknown keys block ID assignment. Other types use `LevelObjectKind`. Existing IDs must match their
record's type segment; the zone prefix can differ from current ownership after a move.

Nested records expose an `owner` record, full `path`, and `LevelObjectAnchor[]` with field paths and
position snapshots for SceneTool handles. Entry/exit gates use the arena data with separate metadata;
portals and engagement windows retain their own data/metadata references. Child ownership inherits
the arena/sequence zone, so remote room coordinates never claim another primary zone. Matching child
overrides warn; conflicting ones error. Optional disabled children are omitted and acquire no IDs.

| Catalog object | Authoring anchor / ownership |
|---|---|
| Platform, Water | `center` |
| Ramp, Torch | `basePosition` |
| Spawn, Pickup, Checkpoint, Balloon | `position` |
| Pedestal | `groundPosition` |
| Arena | `triggerPosition` |
| ProjectileSequence | `progressOrigin` |
| ChallengeRoute | `entryCenter` |
| RunSplit | Named `endSpawnerName` must resolve uniquely; anchor and zone follow that spawn, including its override |
| Gate, ExitGate | Enabled arena's entry gate / optional exit gate; open and closed positions are two handles on one stable child |
| BossPortal | Enabled SolarRealm; exterior/room centers, player entry/retry, enemy/pickup, exit/return positions are eight handles on one child |
| ProjectileEngagementWindow | Each sequence window; `routeStart` and `routeEnd` handles, original window index and owner retained |
| PlayerStart | `playerStart`; metadata on `playerStartMeta`; zone-owned singleton ID such as `T0.PlayerStart` |
| WorldLeaderboard | `position`; zone-owned while enabled, singleton ID such as `T0.WorldLeaderboard`; disabled data remains in catalog |
| Sky, KillZone | Global IDs `Level.Sky` / `Level.KillZone`; anchors zero / kill-volume center; no primary zone requirement |

`AssignZones(definition)` validates finite positive zone bounds and unique well-formed zone IDs, sorts
zones by order then ordinal ID, and classifies each authoring anchor against inclusive bounds. Zero
or multiple matches are errors, including shared boundaries. A unique valid explicit override resolves
spatial ambiguity and emits a warning; an invalid override never falls back to containment. An anchor
within 0.1 m of a containing zone face warns. Split overrides cannot contradict the end spawner's zone;
a matching override warns, and the zone's `splitName` must equal the scored split's `name`.

All existing object IDs are reserved before blank IDs receive the first unused per-zone/type suffix
(`T0.Platform.01`, `.02`, ...). Existing IDs survive moves, reorderings and friendly-name edits; their
zone prefix records original identity, not current containment. Duplicate or malformed IDs are errors
and are never silently rewritten. Spatial singleton IDs omit the numeric suffix; disabled leaderboard
data waits until enabled for its first assignment. `Level` is reserved for global IDs. Validation returns
`ZoneAssignmentReport.errors/warnings`; it can assign other valid blank records while reporting errors,
so callers must validate a working copy before applying. No runtime combat or movement consumes this
metadata, and no scene/prefab generation changes are included in this foundation.

Level 1 names/aliases follow `LEVEL-VOCABULARY.md`, with split names Opening/Ninja/Knight/Spellsword/Warden.
Physical assignment volumes follow the measured post-spacing anchors: T0 z[-166,7.8], T1 [7.9,136.7],
T2 [136.8,260.7], T3 [260.8,392.9], T4 [393,520]. The deliberate 0.1 m gaps avoid inclusive face overlap;
future objects inside a gap correctly fail validation. These physical bounds differ from the document's
human longitudinal ranges, which remain semantic until the dashboard derives its map from the asset.
Primary X[-128,128], Y[-64,128] contain main-course anchors. Explicit exceptions preserve layout: the
leaderboard belongs to T0; zero-origin T1/T2/T3 route-only sequences override to their actual section;
`Pickup_Boss_Hook` at its historical migration anchor belongs to T4. Existing authored overrides survive.
Regeneration matches root records by load-bearing names (split endpoint/route ID
where applicable), children by owner, and same-spawner engagement windows by occurrence within that
sequence; changing duplicate-window ordering requires an explicit metadata migration.

`ChallengeRoute` remains ordinal 11. Its stable IDs use `*.ChallengeRoute.*`; route source spawners and
entry/rejoin anchors remain authored data through regeneration and export.

### Level Studio protected draft storage — `LevelDraftStore`

```
Campaign LevelDefinition asset (read-only while authoring)
  -> LevelDraftStore.CreateFromCampaign()
  -> versioned envelope (draft ID, schema, monotonic revision, SHA-256 payload hash, base snapshot)
  -> <project>/LevelDrafts/<draftId>/draft.json       (atomic manual replacement)
  -> autosave.candidate.<unique>.json -> autosave.revision.<revision>.json
  -> immutable publication; newest three valid snapshots retained (legacy .0/.1/.2 remain readable)

LevelDraftStore.List()
  -> validates schema/version/ID/hash before materializing any LevelDefinition
  -> source GUID + canonical SHA-256 -> Unchanged / Changed / Missing / Unknown apply state
  -> invalid/read-only/recovery-only diagnostic rows remain visible
  -> non-overlapping root/scoring, zone, singleton and object projection -> changedObjectCount
     (gate/exit/portal/window fields have their own owners even while disabled;
      duplicate IDs use occurrence keys; relative order of surviving objects counts once per collection)
LevelDraftStore.Recover()
  -> RecoverResult / RecoveryHistory expose every candidate with revision, validity and diagnostic
  -> repairs interrupted publication/trim before selecting a recovery; never promotes it over the manual save
  -> valid first, embedded revision descending, parsed UTC descending, stable path ascending
  -> newest three current valid files stay active; corrupt files move intact into quarantine/ with diagnostics
```

The campaign asset is never a draft save target. `LevelDrafts/` is outside `Assets/`, ignored by git and
excluded from player builds; the store accepts an explicit root so EditMode tests use a disposable directory.
Writes validate the mutable manifest and both complete finite payloads before persistence, then flush a unique
same-directory temporary file and atomically publish it. Live manifest revisions/times advance only after the
whole operation succeeds. Per-draft process locks coordinate store instances and reject reentrant writes.
Unsupported active manual/recovery formats or schemas freeze every write and automatic repair for that draft;
only their headers reach summaries. A corrupt manual is retained and cannot be implicitly overwritten.
Failure injection covers before/after directory creation, enumeration/read, temp creation/write/flush,
move/replace publication, trim/delete, quarantine and cleanup. Reads repair interrupted current-format work
without a later save. Returned `LevelDraft` objects own their cloned `LevelDefinition` and implement `IDisposable`.

### Level Studio validation and diff

`LevelStudioValidator.Validate` reads an in-memory `LevelDefinition` without hydrating IDs or changing assets. It
returns immutable, deterministically sorted errors and warnings for schema, run scoring, stable IDs, zones,
owner/split agreement, named references, campaign flow, projectile windows, resources and the exact Level 1
traversal adapter. `LevelDraftDiff.Compare` stops on any validation error, then inventories root, zone, singleton,
authored and disabled nested records by canonical stable ID. It emits immutable, deterministic Add/Remove,
Transform, Tuning and Zone changes; array order is one collection-level change rather than index-based object churn.

`LevelApplyTransaction` owns apply ordering while `ILevelApplyEnvironment` owns Unity and filesystem effects. It
rejects stale/missing sources and dirty/wrong scenes before writes; warning confirmation is bound to the exact draft
fingerprint. The Unity environment snapshots the canonical asset and scene bytes plus a journal under the draft's
backup directory, persists into the existing asset GUID, runs the structured canonical build and verifier, then
atomically rebases the draft. Any failure after backup restores and verifies both byte snapshots; the journal is
removed only after the rebase succeeds.
Its preflight traversal adapter runs `LevelArcReport.Build` directly against the in-memory draft, so broken Level 1
routes are rejected before backup or source writes rather than validating the unchanged on-disk asset.

### The in-game level editor — `LevelEditor` + the one piece factory

```
LevelDefinition asset  ──LevelDocument.FromDefinition──►  LevelDocument (JSON mirror: platforms, ramps, spawns,
        ▲                                                   pickups, checkpoints, torches, balloons, waters,
        │ doc.CopyTo(def)  (EXPORT ASSET, editor only)      playerStart)  ◄──ToJson / FromJson──►  <persistentDataPath>/levels/<name>.json
        │
   8. Build Level From Definition (Editor/LevelDefinitionBuilder)      LevelEditor (on the HUD prefab; HudBuilder.BuildLevelEditor)
        │  EditorContext: AssetDatabase / PrefabUtility / static flags   RuntimeContext: the serialised library (materials, prefabs, items)
        └───────────────► LevelPieceFactory.BuildDocument(doc, root, ctx) ◄───────────────┘
                              Platform (+Trim) · Ramp · PlayerStart "StartSpawn" · Spawner · Checkpoint
                              Torch (under "Torches") · Pickup (under "Pickups") · Balloon · Water
                              every object gets a LevelPiece tag (kind, index)
                              Ground/Stone/Platform share Architectural Stone: metre-scale joints/grain/
                              edge wear in shading only; geometry, colliders and renderer counts unchanged
   reusable seam also builds pedestals / arenas / sky / routes / kill-volume shape; NavMesh / Player /
   Managers / HUD / scene lifecycle remain campaign-wrapper only

DeveloperAccess (one process-local capability; plain passphrase is never stored)
   Backquote console input → normalize → SHA-256 → constant digest comparison
   wrong/empty → no state change; accepted → IsUnlocked=true until the process restarts
   ├─ InputReader permits F1, F5-F10 and weapon slot 4
   ├─ DeveloperConsole permits timing commands
   ├─ TestMenu.Open and its public toggles permit mutation
   ├─ MainMenuController exposes Sandbox/custom rows; SandboxController otherwise disables itself
   └─ LevelEditor entry, Sandbox controller/wake-switch mutations, wand pedestal, DebugHarness and
      FrameFilm re-check at entry

LevelEditor.Update  (GameState.Editing; InputReader is the only input reader — optional Editor* actions)
   F10 is rejected in EVERY build until DeveloperAccess.IsUnlocked. `LevelEditor.Enter/Toggle` also
   enforce the capability, so a direct component/UnityEvent call cannot bypass the input gate. Sandbox
   mutations, the dev wand pedestal and scripted diagnostics enforce it at their own entry points too.
   The grant resets on subsystem registration and is not saved. A trusted tester can unlock, return to
   the menu and reach Sandbox or
   custom-level tools; an ordinary player sees only campaign rows.
   F10 ─► Enter(): returnPosition, fly camera on PlayerLook, cursor locked, panel shown
   Aim(): ray from the lens → grid snap (1 m platforms/water/walls/ramps, 0.5 m else; Alt = free) → preview cube
   [ ] kind · V variant · = − size ladder · T rotate (axis swap / flow turn), Shift+T the reverse turn
   LMB Place(point, normal) ─► doc.<list>.Add(def) → LevelPieceFactory.<Piece>(def, customRoot, ctx)
   X / Delete   DeletePiece(LevelPiece under the crosshair) ─► doc list remove + rebuild indices
   G   grab: the tagged piece follows the aim, dropped on the grid on release
   I / middle mouse   PickPiece(LevelPiece under the crosshair): kind/variant/size become the pending selection
   Tab cursor free ↔ locked (panel: name field, NEW, SAVE, ‹ ›, LOAD, PLAY, EXPORT ASSET, EXIT)
   SAVE ─► doc.ToJson() → levels/<safe name>.json     LOAD ─► FromJson → Rebuild()
   PLAY ─► Rebuild() → NavMeshSurface.BuildNavMesh() (try/catch: enemies stand still on failure)
           → Teleport to playerStart → spawners spawn → GameState.Playing; F10 / EDIT → BackToEditing()
   EXIT ─► Exit(): destroy customRoot, restore returnPosition, GameState.Playing
MainMenuController.RefreshCustomRows ─► one CUSTOM row per levels/*.json → LevelEditor.PendingLoadPath → load Sandbox → Play

DeveloperConsole (HUD overlay; Backquote/Enter actions live in InputReader)
   LOCKED: `help` | `clear` | bare secret passphrase (only its digest ships)
   UNLOCKED: `help` | `clear` | `timing start/stop/status/export/discard` plus F1/F5-F10/4;
   no generic reflection/cheat execution
   Open → TimeScaleController.Request(0), GameState.Paused, free cursor, focus TMP_InputField;
          locked input is password-masked and every rejected/accepted credential echo is redacted
   Close → release its own time handle and restore prior state/cursor
```

### Reusable world-content construction seam

```
LevelDefinition + LevelWorldContentContext(explicit root, piece resolver, materials, per-build spawner map,
                                           CampaignRuntime / PreviewSafe policy and cloud flag)
  -> LevelDefinitionBuilder.BuildWorldContent(...)
       -> LevelPieceFactory.BuildDocument plus player start, projectile sequence hosts, pedestals, arenas,
          solar-realm visuals, sky, cloud sea, world leaderboard, Challenge Route anchors and kill-volume shape
       -> LevelWorldContentResult(root, startSpawn, counts, builtSpawners)

Campaign Build() owns scene-root cleanup, LevelRunScorer, NavMesh bake, Player/Managers/HUD bootstrap and save;
it supplies the canonical editor context with runtime behaviours enabled and consumes the seam result.
Level Studio preview supplies PreviewSafe: after shared construction it disables every generated gameplay
MonoBehaviour and Collider, retaining authored renderer/mesh/marker/collider data while preventing gameplay
participation. The presentation-only `CloudSea` stays enabled because disabling it releases its generated mesh. The
seam never opens, saves or clears scenes, bakes NavMesh, creates campaign roots or writes global gameplay state.

`LevelStudioWindow` edits only the draft clone through `LevelStudioSceneTool`. Native SceneView selection and W/E/R
handles, the serialized numeric inspector, multi-edit, duplicate/copy/paste/delete and zone bounds all register Undo
on that clone; changes autosave through `LevelDraftStore`. `LevelStudioPreview` owns one hidden, unsaved PreviewSafe
root, updates transform-only edits in place, and rebuilds it after structural or arbitrary serialized edits. Route and
projectile lines are presentation-only handles and never add scene objects or gameplay state.
```

**Invariants**

- A content build may only construct below its supplied root; it never discovers or mutates a scene root.
- The spawner lookup is per build, never a static dictionary shared by campaign and preview.
- PreviewSafe preserves authored visual/geometry construction and collider data, but disables generated gameplay
  MonoBehaviours and every Collider (including spawners, pickups, checkpoints, water, balloons and altars).
- The campaign wrapper is the sole owner of destructive lifecycle work and uses this same seam, so it cannot drift
  from preview construction through a copied factory path.

`PlayerTimingCapture` is an explicitly unlocked, opt-in, local-only diagnostic in every build. `timing prime`
(`timing start` remains an alias) creates the recorder in `Primed`; only then does the developer-gated
`InputReader.TimingCaptureTogglePressed` (`0`) start a fresh take. The next `0` stops and atomically renames one
version-2 JSON file under `Application.persistentDataPath/timing-captures/`, then returns to `Primed`. Each human
parry press records a desired beat with movement, look, surface, zone and split context even when no projectile
exists; observed projectile emission/cue/arrival/result events remain diagnostic evidence. The recorder reads
`InputReader`, `FirstPersonMotor`, `LevelPiece`, `LevelRunScorer` and `Projectile` state and never drives input,
combat, velocity or time scale.

`Level Studio → Select Capture → Generate Module` parses that same version-2 file, previews its path/beats,
and sends every desired beat through `ParryModuleSolver`. The editor-only solver reads the shipped Sentry/Heavy
`EnemyData` and the shared `ProjectileFlightMath`, tests fixed left/right perch candidates inside the selected
zone, groups only cadence-compatible triples into a Heavy phrase, and emits a stable placement plus one
`ParryBeatFit` per requested beat. Unsatisfied beats remain explicit report failures. `ApplyToDraft` appends the
successful generated spawns to the working copy under one Undo step; it never writes the campaign asset.

**Invariants**
- **A ramp is authored as a RISE over a RUN, never as an angle** (`RampDef`, 2026-09-07 — the game's only
  non-axis-aligned geometry). `basePosition` is the centre of the LOW edge of the walkable face; `run` is
  the HORIZONTAL distance covered and `rise` the height gained, so the high edge is exactly
  `basePosition + Heading * run + up * rise` (`TopPosition`) and an arc report can use both without
  trigonometry. `AngleDegrees = atan2(rise, run)` is DERIVED and nothing stores it. The defaults, 2 m over
  6 m, are **18.4°**; keep authored ramps under ~35°, because a CharacterController's 45° `slopeLimit`
  turns anything steeper into a wall on the way up.
- **`LevelPieceFactory.Ramp` builds ONE rotated cube and nothing else** — collider, renderer, `LevelPiece`
  tag, no behaviour (hard rule 10). The slab is `Quaternion.Euler(-AngleDegrees, yaw, 0)` at `BoxCenter`
  with scale `(width, thickness, SlopeLength)`, on layer 0 so the NavMesh bake walks it, and no trim bars
  (the trim builder places world-axis bars, which a rotated slab has none of). Whether a slide accelerates
  down it is the motor's business, read off the real ground normal — the ramp never tells it anything.
  `LevelPieceFactory.RampFrom` is the exact inverse, and is how `Export Current Level To Definition`
  round-trips a ramp instead of flattening it into a `PlatformDef`.
- **`LevelPieceKind` is APPEND-ONLY, exactly like the `Sfx` enum.** Level JSON stores the kind as a NUMBER;
  `Ramp` is 9 and every earlier value keeps its index, which is why a level saved before ramps existed
  still loads (`RampPieceTests`). A missing `ramps` list comes back null from `JsonUtility` and
  `LevelDocument.FromJson` fills it, the same rule every other list follows.
- **Current hybrid projectile course (2026-09-10; supersedes the removal-only openness pass).**
  `ApplyOpenProjectileCourse` writes five T1 landings at 12–14 m wide, eleven T2 terraces at 10–18 m,
  and T3's four 14–16 m landings plus 16 m-wide water/exit decks. `HybridCourseStructures` then regenerates
  the low rails, tower/chimney, lintels, obelisks, recovery posts, wall-run faces and rejoin landings on the
  expanded shoulders. Five connector ramps and the four-balloon T3 arc are restored as secondary movement
  lines; sparse edge beacons leave the broad centre readable. T1/T2/T3 stop 14.55/14.42/15.17 m before their portal
  triggers: ordinary slide-jump carry fails, while two projectile-deflect impulses succeed, so ignoring
  parries loses. The six existing autonomous blue/Heavy spawners are re-perched across early/late decks
  and may fire repeatedly through their normal range/LOS/facing loop; the engagement windows audit those
  useful crossings but never gate them at runtime. The final 48 m ramp's three single-shot Surge spawns are
  at progress 16/32/44 m on alternating x = -7/+7/-7 pads; the old third spawn was eight metres beyond the
  slope. `LevelFinalRampTurretPlacementTests` pins pad clearance, alternation and on-ramp progress.
- **Challenge Routes are level data, not a hidden enemy special case.**
  `LevelDefinition.challengeRoutes[]` stores a stable route id, the existing blue-sentry spawner names that
  can offer the flare, and shared entry/rejoin boxes. The builder creates only an invisible, colliderless
  anchor; the boxes are telemetry anchors and have no trigger or gameplay effect. A normal run still answers
  the projectile line for speed. Destroying a blue sentry and taking its flare selects the faster, harder
  optional line, which rejoins before its solar arena. The exporter round-trips the anchor component and
  never exports it as platform geometry.
- **Level_01's parkour-first layout is CODE that writes the asset** (`LevelDefinitionAuthoring.Apply`, menu
  `8a`): perches + spawn moves + the balloon arc + the water lines, idempotent. `LevelTraversalAnalyzer`
  flies the arc (pop = carry trimmed to `launchCarryCap`, `launchFloatSeconds` at `launchGravityScale`, the
  re-armed dash allowed only on the final fall) and casts the shooters' bolt lines (muzzle → deck chest,
  inside the shooter's band, crossing no box); `Level Arc Report` prints all three; `LevelTraversalTests`
  holds them off a copy of the asset. A pop's spacing is DERIVED from the flown pop, never copied from
  another level (ENGINEERING-LOG, "A balloon chain laid to the yard's spacing").
- **`Apply`'s first block RESHAPES by name and owns nothing's existence** (openness pass, 2026-09-06).
  `LevelDefinitionAuthoring.Reshapes` is a table of `name -> absolute centre/size`; `Apply` looks each one
  up in `def.platforms` and overwrites those two fields, creating and destroying nothing, so a second run
  writes the same numbers. `TrimOn` / `TrimKey` and the `Torch_Beacon_`-prefixed `RouteBeacons` follow the
  same remove-ours-then-re-add discipline as the perches. **A shorter gap is not automatically a better
  hop:** growing a take-off deck moves the sampled take-off points with it, and extra room on the wrong
  side of an obstacle costs clean launch points while the gap number improves (measured on `T2_L8 → T2_L9`:
  gap 4.24 → 3.91 but 10 → 8 clean points; fixed by growing the LANDING deck `T2_L9` instead, 3.20 / 13).
  `Tools/level_arc_offline.py` parses those tables straight out of the C# and measures every hop, the
  torch density and the pinned x-coordinates without the editor; `--after`, `--torches`, `--sight`.
- **`Apply`'s third pass places the RAMPS** (2026-09-07). `LevelDefinitionAuthoring.Ramps` is a table of
  `name → base / width / run / rise / yaw`; `Apply` drops every `RampDef` whose name contains `_Ramp_` and
  re-adds the table, the same remove-ours-then-re-add discipline as the perches, so a ramp dropped in by the
  F10 editor and exported back survives a re-run of `8a`. Five ramps, 7.3–15.4°, all `materialKey "Stone"`.
  Three design facts that are not obvious from `RampDef` and cost measurement to learn:
  **(1) These five connectors climb** — the route before the final descent rises from y 0 → 28 — so
  their `slideSlopeAccel` term bleeds a climbing slide and their job is continuity;
  **(2) a ramp is not a kicker**, because a `CharacterController` leaving the top edge gains no vertical, so
  a ramp that stops short of its deck is just a jump with a shorter run-up; **(3) a ramp occludes exactly
  like a slab** — the 4 m `T2_L8 → T2_L9` ramp stood inside `T2_Perch_E`'s bolt line onto `T2_L9`, and the
  west-swung alternative cut `T2_Tower` and cost three sightlines, so the shipped one is 3 m wide and lands
  at z 129.3, under the bolt.
- **`LevelArcAnalyzer.BoxesFrom` (line 59) reads `def.platforms` only.** Ballistic hop analysis remains
  blind to ramps; `LevelRampPlacementTests` and `LevelDescentReport` check their geometry separately.
  `Tools/level_arc_offline.py` grew
  an oriented-box `RampBox` (exact slab test for bolt lines and sightlines; the arc sweep treats the capsule
  as axis-aligned in ramp-local space, an error ≤ 1 − cos 15.4° of capsule height, and counts an arc that
  touches a ramp on the way down as an ARRIVAL because a body that lands on a slope is on the route). Run it
  with `--noramps` for the before. Making `BoxesFrom` ramp-aware is the additive hook and is a lead call.
- **The T1 opening's runway is a PERCH placement, not a timing number** (2026-09-07). A parry cue is a
  flat 0.28 s everywhere (`Projectile.CueLead`) and `ProjectileShooter.CueMargin` floors a near bolt's
  flight at 0.44 s, so **no level geometry can lengthen the window**. What the level owns is two things:
  how far the player runs before a sentry is awake, and whether their feet are down when the cue lands.
  A shooter's wake radius is `EnemyController.WakeRange = max(aggroRange, projectileMaxRange)` — 32 m for
  `pshooter_enemy01` — so solving that radius against the route centreline gives the exact z at which an
  encounter begins. `T1_Perch_W` used to stand at `(-7.5, 3.5, 44)`, one metre past `T1_Ramp_Causeway`'s
  top edge: it solved to a wake at **z 13.1**, *before* the level's first ramp, and its opening bolt
  (0.7 s `projectileAcquireDelay`, then a 25 m / 0.62 s flight) arrived at **z ~27.5** — the gap between
  `T1_Stone_2` and `T1_Stone_3`, i.e. in mid-air over the only hop in T1 that is a choice. Moved to
  `(-11.5, 3.5, 53)` the same arithmetic gives a wake at **z 23.3** (the whole first ramp is run
  unopposed, 35.6 m from the muzzle at the ramp's top edge against a 32 m wake) and a first impact at
  **z 37.9**, mid-deck on `T1_Stone_4`, feet down, causeway ahead — so the parry boost throws the player
  along the line they are already on. `x -11.5` is set by the OTHER band edge: broadside to the causeway,
  the nearest chest point must stay outside `projectileMinRange` 6 m or the sentry goes silent exactly
  where it is meant to fire; from here it is 7.3 m. `LevelT1OpeningTests` pins all four numbers off the
  shipped asset and the shipped `EnemyData`.
- **The stones around that ramp grew along the axis their gaps are NOT measured on** (same pass). Four
  `Reshapes` entries: `T1_Stone_1` 5 x 5 → **7 x 6.4** grown SOUTH (north edge pinned at z 16.5, so the
  ramp's 0.2 m seam and the 8.5 m committed gate onto `T1_Fast_1` are bit-identical, while the step off
  `Ground_Start` goes 3.5 m → 2.1 m), `T1_Stone_2` 4 x 4 → **5.5 x 4** west (max.x held at 5.5 for
  `T1_Wall_Start`'s run line), `T1_Stone_3` 4 x 4 → **5 x 4** west (the 0.25 m seam with `T1_Fast_1`
  held), `T1_Stone_4` 5 x 6 → **6 x 6** west. Not one hop distance in the chain got longer and two got
  shorter: `Ground_Start → T1_Stone_1` 3.50 / 18 clean points → **2.10 / 23**, `T1_Stone_2 → T1_Stone_3`
  5.00 / 11 (best clearance 1.63) → **4.27 / 14 (2.12)**, `T1_Stone_3 → T1_Stone_4` clearance 3.59 → 99
  (nothing in the arc at all). `T1_Ramp_Stone12` widened 3.0 → **3.5 m** and recentred x 2.0 → 1.75,
  which puts it inside both decks' x span at both ends — the second **fully supported** ramp in the level
  and the first one the player runs up.
- **`Apply` also writes the arena gates** (second openness pass, 2026-09-07). `LevelDefinitionAuthoring.Gates`
  matches a `LevelDefinition.ArenaDef` by `gateName` and writes only the X of `gateSize`, `triggerSize` and
  (where the arena has one) `exitGateSize` — absolute and idempotent like the rest. It exists because the
  arena doorways widened 6 m → 9 m and **a doorway, its gate and its trigger are one measurement**: a 9 m
  door with a 6 m trigger is a door the player walks through at x 4 while the fight never starts.
- **Opening descent (2026-09-07, lengthened the same day):** `LevelDefinitionAuthoring.Apply` runs
  `ApplyDescent`, then `ApplyOpeningDescent`. The second pass is authored from **four numbers**
  (`OpeningRun` 144, `OpeningGrade` 0.25, `OpeningBottomZ` -15.8, `OpeningSeam` 0.2) and a **progress
  coordinate measured down the slope from its lip**; every deck, perch, gate and bound below is derived
  from them by a local `slope(progress, x, above)` helper, so changing the hill's length moves the whole
  opening as one piece. It adds a 14 m wide, 12 m deep crest at **y 36 / z -171.6..-159.6**,
  `T0_Ramp_Descent` (**144 m run, 36 m drop**, z -159.8..-15.8, still exactly 1:4), and a level run-out at
  y 0 / z -16..-7.8. The bottom of the hill is **pinned** because the run-out and then the unchanged
  `Ground_Start` follow it, so the 2026-09-07 lengthening (120 -> 144 m of run, from "the ramp needs to be
  longer at the top") appears entirely **above** the lip. The run-out overlaps the rear of `Ground_Start`
  by 0.2 m; the entire original course follows. `playerStart` is **(0,36.3,-162.3)**, 2.5 m back from the
  lip facing downhill (yaw 0), and `WandPedestal_Start` is beside it at (3,36,-162.3).
  `playerStart/playerStartYaw` flow through `LevelPieceFactory.PlayerStart` into `StartSpawn`;
  `LevelDefinitionBuilder` places the saved player there and wires the level manager for pre-checkpoint
  respawns. Existing checkpoints remain in place. Kill bounds are (0,-30,130) x (200,2,640), covering the
  raised crest from z -190 through the arena's z 450. The pass replaces only its own named pieces and is
  idempotent. `LevelDescentTests` checks the shipped start, full-width level joins, two large descents,
  migration from the erroneous late spawn, the unopposed payoff straight, and preservation of the existing
  route and encounter. `LevelRampPlacementTests` checks every shipped slope for deck contact and obstruction.
  Five surge turrets sit at slope progress **30/47/71/96/113 m**: left, right, left, then right/left
  overhead. The overhead pair is placed as a **rigid group** off one anchor at slope progress 97, so the
  tuned 6.0-6.6 m route clearance and the rear muzzle height (too high a muzzle makes capped homing overfly
  a falling runner) survive any change to the hill's length. All five contacts belong on the slope, around
  progress 16/28/51/76/97; the 47 m of empty slope below the last contact is the deliberate exhale, where
  the five-stack 1.60x surge is felt before the run-out hands the player to `Ground_Start`.
  Two `pshooter_enemy02` Heavy Reliquaries then sit on 3 m cyan-trimmed pads at **(-10,0.1,6)** and
  **(10,0.1,6)**, outside `Ground_Start`'s +/-8 m edge. They are members 6/7 of the same coordinator and
  each resolves its full three-contact phrase before the next member advances. After the five Surge beats
  run once, `repeatFromIndex = 5` loops the two surviving Heavy sources A→B→A. A dead member is skipped;
  if both are gone the coordinator is dormant until respawn.
- **The T0 volley's gate coordinate, and why `memberProgressGates[0]` is 0 (2026-09-07).**
  `T0_SurgeVolley.progressOrigin` sits **on the ramp's top edge** (0,0,-159.8) with
  `progressDirection = forward`, so a gate value and a perch's slope progress are the same kind of number.
  Gates are `{ 0, 18, 40, 64, 87, 90, 112 }`; the last two belong to the bottom Heavy pair. Two rules came out of play that day:
  **(a) a gate is a FLOOR on where a beat may open, not a schedule.** `memberProgressGates[0]` was raised
  0 -> 14 to "buy runway before the first enemy"; the user reported the sliding parry rhythm broke ("the
  logic for those was somewhat working ... but now its off"). Raising it did not add approach, it deleted
  the first beat's approach and pushed the whole sequential ladder later into the slide. It is back at 0
  and `OpeningTurretTests` pins it there. Runway is bought by moving **geometry**.
  **(b) the origin must ride the lip.** When the hill was lengthened, `progressOrigin` moved up with it. Had
  it stayed at z -135.8 the new 24 m would have been pure run-up: the player would cross gate 0 at ~21 m/s
  instead of ~13, and the first bolt's flight would have *shortened*. On the lip, gate 0 is still crossed at
  the slowest moment on the hill.
  **The perch fix.** "The turret is not aggroing soon enough (the first one on the left)" was an
  *announcement* problem, not a wake-range one: every other beat opens 26-32 m above its perch, but beat 1
  opened at 22, so its bolt flew ~0.31 s against 0.45-0.65 s for the rest. Perch 1 moved to slope progress
  30 (world z -113.8 -> **-129.8**, 17 m further up the hill). Announcements are now **30/29/31/32/26 m**,
  its bolt flies ~0.46 s, and it is 33.6 m horizontally from `playerStart` -- inside the sentry's 36 m wake
  radius (`EnemyController` measures wake with y flattened), so it is awake and tracking before the player
  has moved. Contact speed is essentially unchanged (~15 m/s). `ProjectileVolleySequence` only grants the
  0.7 s `projectileAcquireDelay` arm-up to **member 0**; members 1-4 fire the first frame past their gate,
  in band, with a line and a frontal arrival, which is why gate 1 moved 16 -> 18 (following beat 1's contact
  2 m down the hill) to keep the shipped ~0.14 s pause between contact 1 and launch 2.
- **Final descent (2026-09-07):** `ApplyDescent` retains a 10 m wide
  entry at y 28 feeding `T4_Ramp_Descent` (48 m run, 12 m drop), then a 24.4 m run-out at y 16.
  Three `Spawn_T4_Surge_*` entries use the existing `pshooter_enemy03` prefab on side pads at
  z 324/342/360, 18 m apart. Live interception moved the row 8 m past its first draft: the original
  opening bolt could chase behind the player and never arrive. The last beat now rides the run-out.
  `Boss_Arena` is the translation anchor: its geometry, spawn, pickup, torches and gate/trigger move as
  one unit to z 390 / deck y 16. `Checkpoint_4` remains on the final boss approach; kill bounds extend
  to z 450. The two south walls receive final absolute positions because the earlier `Reshapes` pass
  resets them to their original coordinates. Applying twice does not accumulate translation or duplicate content.
  `LevelDescentReport` supplements the platform-only ballistic analyser with exact oriented ramp/box
  segment tests for sliding/standing bolt lines and crest-to-run-out preview, sampling the shipped ramp's
  base, run, rise and heading. Existing ramp placement
  tests now inspect every shipped ramp. This is geometry evidence, not homing/timing or feel evidence.
- **`--sight` is the openness measurement, and it sees what `AnalyzeHop` structurally cannot.** It stands
  the eye at each route deck's centre + 1.7 m, looks at the next six decks' surfaces (+ 0.5 m), and counts
  how many are visible in a row before an opaque box intervenes. Level_01 measured **2.45 mean moves
  visible ahead** after the first openness pass and **3.00** after the second. The three things it named
  that no hop analysis could: a slide gate parked on its deck's exit edge (`T1_Fallen_Obelisk` at z 63 —
  first blocker from six consecutive decks, and the causeway itself saw *nothing*; it was also standing
  1.4 m behind that deck's take-off edge, worth 5 clean launch points of 20); a 5 m tower core inside a
  19 m helix (first blocker from eight of the spiral's eleven decks); and 6 m doorways in the 26–28 m
  arena walls. A named blocker is not automatically a bug — two slide gates and four pillars are supposed
  to be in the way.
- **The runtime editor and `8. Build Level From Definition` share ONE piece factory.** A piece that renders
  differently in the two is a bug in the factory, not in either caller. `LevelEditorTests.RuntimeAndEditorFactoriesAgree`
  builds a small document both ways and compares names and positions.
- **Custom levels are data, never scene state.** The editor edits a `LevelDocument`; the scene objects are
  a rebuildable view of it. Nothing is read back off the scene except the `LevelPiece` tag.
- **The campaign hierarchy is unchanged**: platforms, spawners and checkpoints as direct children of `Level`,
  torches under `Torches`, pickups under `Pickups`, the start as `StartSpawn`. `LevelDefinitionExporter`
  reads those names; flattening them exports torches as platforms.
- **Every editor number is written by `HudBuilder`** (rule 9): grid sizes, the size ladder, fly speeds,
  the library.
- **Input through `InputReader` only** (rule 2); every editor action is optional, so a map without them
  still runs.

## Boss

```
exterior SolarArenaPortal (player crosses the arena sun)
  → BossArenaTrigger.BeginFight(player) → gate seals
  → FirstPersonMotor.Teleport(realmEntry, yaw) → enclosed same-scene boss realm
  → final arena: BossController.Activate() ⇢ BossStarted → HUD bar + boss music
  Health.deathIsStagger = true  → HP 0 does NOT kill
      → ⇢ OnZeroHealth → Posture.Break() + HoldStagger(5s)   = deathblow window
      → riposte (isExecute) → HandleDeath(): SegmentsLeft--, heal, next phase, roar
      → miss the window → HP restored to 12%, fight continues
  3 segments consumed → ⇢ BossDefeated

     BossDefeated
        → scored level: `LevelRunScorer` stops `SpeedrunTimer`, freezes one `LevelRunResult`, and ⇢ LevelRunEvaluated
           → completed: `LevelProgress.RecordCompletion`, `RunRecorder` saves ghost, HUD "LEVEL CLEAR"
           → incomplete: no progress write, `RunRecorder.Discard`, HUD "RUN INCOMPLETE" with unmet gates
        → unscored legacy level: SpeedrunTimer / RunRecorder / HUD retain the direct BossDefeated completion path
        → AudioManager fades the boss track
        → WinCo (all REALTIME waits -- Won may stop the clock):
             +2.0 s  GameManager.SetState(Won)
             +4.5 s  cursor released, TimeScaleController.ResetScale(),
                     SceneManager.LoadScene("MainMenu")
     THE LOOP CLOSES HERE. It previously did not: Won was set, nothing in the project
     listened for it, and the cursor was re-LOCKED -- a cleared level left the player in a
     finished world with no way out. ResetScale exists for this transition: the deathblow's
     hitstop is still live when the load starts, and a scene load destroys the controller
     before the request expires, so without it the menu comes up running at 0.02x.
```

**Invariants**
- Only `isExecute` damage removes a segment. Ordinary damage can never kill the boss.
- The boss is excluded from the aggression system (`aggression = 0`) — its pressure comes from phases.

---

## Progression

```
enemy dies → SoulsWallet.Add(soulValue) ⇢ SoulsChanged → HUD
player dies → PlayerDeath: SoulsWallet.TakeAll() → LevelManager.SpawnBloodstain(lastGroundedPos, souls)
Bloodstain OnTriggerEnter → SoulsWallet.Add(amount), destroy
    (the drop is synchronous inside Health.OnDied; die on your own checkpoint and you respawn
     on the stain and recover it immediately — intended, and it is why a test must sample the
     wallet at the moment of death, not after the respawn)
LevelUpMenu (Tab) → UpgradeMath.Cost(level, base, growth) → TrySpend → PlayerStats.Increase(stat)
                  → PlayerStats.Apply() → Health.SetMax / PlayerPosture max / flask charges

LevelDefinition run contract (optional; zero/empty preserves legacy completion)
  → LevelRunScorer begins with SpeedrunTimer.RunStarted
  → EnemyKilled → resolve its `EnemySpawner.Instance` → authored spawner name credited ONCE for this run
       → count that enemy's existing base `EnemyData.soulValue` toward run-local earned souls
       → non-split spawner → distinct regular-kill count
       → only the CURRENT ordered `RunSplitDef.endSpawnerName` closes a split:
            elapsed since prior split → D/C/B/A/S threshold → data-authored bonus → SoulsWallet.Add(bonus)
            ⇢ SplitGraded + RunScoreChanged
  → BossDefeated → `SpeedrunTimer.FinishRun()` → freeze `LevelRunResult`
       → earned souls >= requiredRunSouls AND regular kills >= requiredRegularKills AND every split closed
       → success only: LevelProgress.RecordCompletion + ghost save; otherwise discard ghost
       ⇢ LevelRunEvaluated (the sole scored-level completion authority for HUD/progress/ghost)
```

`Level_01` writes a 3560-soul and four-distinct-regular-spawner gate: 3400 from Ninja, Knight,
Spellsword and Warden plus four 40-soul regulars. Its ordered endpoints are Ninja,
Knight, Spellsword and Warden; split bonuses are D/C/B/A/S = 0/25/50/75/100. Its S segments are
55/60/70/40 seconds, with A/B/C at 1.15/1.30/1.50× those times. The four boss rewards plus four regular
enemies form the base requirement; bonuses are real earned souls and may help meet the inclusive soul gate.

**Invariants**
- **Run credit identifies authored spawners, not enemy instances.** Respawns and repeat deaths cannot farm a
  quota or a split; test-spawned and un-authored enemies do not count.
- **Splits are strictly ordered.** Killing a later endpoint early grants its base souls (once) but cannot
  close or award that split; only the current endpoint advances the clock segment.
- **`LevelRunEvaluated` is frozen once and is the scored-level terminal truth.** Consumers must not infer a
  clear from `BossDefeated` when a scorer exists. Failed gates show the result then return to the menu, but
  do not write `LevelProgress` or retain a ghost.

---

## Level flow

`Level_01` is **one continuous run** of four connected sections: three main tiles, each ending in a
gated legendary mini-boss, then the boss tile. One timer, no scene loads.

```
Sanctum (top y 0, pink)          wand altar - the loadout is chosen before the clock matters
  Checkpoint_1
Tile 1  THE SHATTERED CAUSEWAY   low, fast, horizontal - stepping stones + a railed dash run
  (cyan)                         arena top y 4    Legendary_Ninja
                                 opening spacing (2026-09-07): Stone_1 7 x 6.4 @ (0,-0.5,13.3) | Stone_2 5.5 x 4 @ (2.75,0,22)
                                   Stone_3 5 x 4 @ (-4,0.5,30) | Stone_4 6 x 6 @ (-0.5,1,38)
                                   T1_Ramp_Stone12 base (1.75,0,16.3) 3.5 wide, 3.90 run, +0.5 - fully supported both ends
                                   T1_Perch_W (-11.5, 3.5, 53): wakes at z 23.3, first bolt lands z 37.9 on Stone_4
                                 wall-run walls (right, both run north, both optional):
                                   T1_Wall_Start    (7.1, 2.5, 22.5)  1.2 x 7 x 20   skips the four stones
                                   T1_Wall_Causeway (3.9, 2.5, 50)    1.0 x 7 x 16 → T1_Wall_Landing (4.6, 2, 64.5) 4 x 1 x 10
  Checkpoint_2
Tile 2  THE ASCENT               vertical - 11 ledges spiralling a 20 m tower
  (yellow)                       arena top y 20   Legendary_Knight
                                 wall-run walls (outside of the spiral; a run DESCENDS, the exit jump buys the height back):
                                   T2_Wall_East (15.7, 5.5, 161.5) 1.2 x 9 x 21 → T2_Wall_Landing_East (11.4, 7.5, 175.5) 5.6 x 1 x 8
                                   T2_Wall_West (-15.8, 10.5, 153) 1.2 x 9 x 22 → T2_Wall_Landing_West (-10.8, 12, 139.75) 6.8 x 1 x 9.5
  Checkpoint_3
Tile 3  THE LONG SPAN            high and exposed - broad pillar terraces, then a 26 m span with a Heavy on it
  (red)                          arena top y 28   Legendary_Spellsword
                                 wall-run walls (right, both run north, both optional; they chain):
                                   T3_Wall_Pillars (12.6, 22.5, 272) 1.2 x 7 x 23 → T3_Wall_Landing_S (10, 22.5, 287.25) 8 x 1 x 6.5
                                   T3_Wall_Span    (13.1, 26, 302.5) 1.2 x 8 x 23
                                 All authored in Level_01_Level.asset (Stone, NeonCyan trim), NOT in LevelGreyboxBuilder;
                                 proven by LevelSpan1-3Report / LevelSpan1-3Tests against `longest`, never `best`.
  Checkpoint_4
Boss    THE ECLIPSE COURT        38 m walled court facing the eclipse down +Z
  (pink)                         arena top y 28   Boss
```

```
LevelDefinition (Assets/Data/Levels/Level_01_Level.asset)   <- THE SOURCE OF TRUTH
  -> LevelDefinitionBuilder.Build()     scene: geometry, spawners, checkpoints, torches, pickups,
                                        wand altars, arenas, sky, kill plane, NavMesh bake
  LevelGreyboxBuilder.Build() ("6. Build Level") FORWARDS to the above whenever that asset exists.
  Its literal coordinates survive only as BuildHardcoded(), the reference implementation.

BossArenaTrigger  = ONE mechanism for every gated fight
  entry gate rests SUNK, rises to seal you in on OnTriggerEnter / BeginFight
  boss arena      (clearSpawner null)  -> BossController.Activate(); nothing ever reopens
  mini-boss arena (clearSpawner set)   -> exit gate rests UP; Update() watches that spawner and
                                          drops BOTH gates once its enemy is dead
                                          (latched on "seen alive" so it cannot open at level start)

SolarArenaPortal = OPTIONAL same-scene transport layered over BossArenaTrigger
  SolarRealmDef.visualRadius sizes the exterior plasma/corona independently of the portal collider;
    zero falls back to exteriorRadius for older definitions. Shipped visual radii remain 22/23/22/31 m;
    physical radii are 16/17/16/25 m, retaining a 6 m membrane band.
  SolarArenaVisual rotates exterior plasma/corona; realm ceiling rotates around Y only
    -> SolarArena shader uses premultiplied blending: _SurfaceOpacity 0.92 on exterior theme materials
       attenuates the background; zero on corona and the serialized ceiling override preserves additive glow
    -> serialized plasmaOpacityOverride reapplies the ceiling renderer property block on enable
    -> SolarArena.shader moves seamless domain-warped currents with distance-filtered filaments;
       stationary realm floor/walls own collision and `_Fade` remains the final crossing multiplier
    -> the shared final premultiplied fade stays unchanged through 65% fog transmittance, then releases
       solar colour and surface occlusion together to zero at full fog; no distant black sun discs
  ApplySolarSpacing removes the obsolete exterior rectangular courts/walls/torches and stops each route
    deck 1.5–2.4 m before its visible shell; the four open trigger gaps are 7.6–8.4 m.
  post-first-miniboss scale is LOCAL first, then translated as one authored section: T2 is a ~28 m helix
    with larger terraces and moves +38 m in Z; T3 has broad 9–12 m pillar terraces, a 9.6 × 26 m span,
    a 13.5 m first wall-run gap and moves +70 m; T4/boss moves +96 m. Every gate, trigger, checkpoint,
    pickup, water line, balloon, perch and engagement window follows its section. The exterior sun centres
    are Z 87.3 / 216.8 / 356.3 / 486.3, with enough empty approach/departure air that route geometry does
    not overlap the membrane.
  crossing the isolated exterior sphere rejects cleared arenas and debounce/missing-motor failures;
    success calls BeginFight, then Teleport, then same-frame SolarTransition.Cut + Sfx.SolarWarp exactly once
  builder moves the LIVE named spawner and court pickup into a disconnected enclosed realm with its own
    generated 20 m collision/NavMesh floor, boundary, ceiling and light
    → SolarRealmPlacement preserves their authored exterior coordinates for scene export round trips
  mini-boss clear → inner return portal appears → player chooses when to return beyond the exit gate
  final boss has no return portal → existing BossDefeated / LEVEL CLEAR flow remains the only exit
  ResetArena → ResetPortal; death still respawns at the exterior checkpoint, while the bloodstain
                remains inside the active realm and can be recovered by entering the sphere again

LevelManager  owns spawners, checkpoints, respawn
  Checkpoint trigger → SetCheckpoint(): heal, refill flask ⇢ CheckpointReached
  death / void-fall (9m below last grounded AND no solid route surface within the downward support probe)
      / KillZone → PlayerDeath → Respawn():
      ResetEnemies() (re-instantiate every spawner, ResetArena() on every trigger) → Teleport
      ⇢ PlayerRespawned → items restored, posture cleared, boss bar hidden, ambient music
SpeedrunTimer: starts on first movement input, stops on BossDefeated, unscaled, excludes menus
```

### Sky and palette

```
VibeGame1/1. Project Setup -> ProjectSetup.SetupSceneEnvironment()
    mainCam.backgroundColor        = VoidColor        #060D18   lin lum .0039  (never seen: the dome covers it)
    RenderSettings.fogColor        = FogColor         #20344D   lin lum .0330  = composited lower atmosphere
    RenderSettings.fogStart / End  = 36 / 140  -> 25 m 0% · 50 m 13% · 64 m 27% · 87 m 49% · 100 m 62%
    RenderSettings.ambientSkyColor = AmbientSky       #344C78 x1.35   .1348  platform TOPS
    RenderSettings.ambientEquator  = AmbientEquator   #3F5E88 x1.35   .2045  every wall + every BACKLIT enemy
    RenderSettings.ambientGround   = AmbientGround    #0E1326 x1.35   .0110
    the one directional            = KeyLightColor    #5A79AD  @1.05, Euler (10,180,0)
  + VolumeProfile: Bloom 1.05/0.60, ACES, Vignette 0.27, WhiteBalance -6 / 0

VibeGame1/2. Create Materials -> MaterialFactory.Table (Assets/Materials/M_*.mat)
    structural  M_Ground #36404F · M_Stone #424D5F · M_Platform #586579 · M_Enemy #1A1E29
      -> the first three use VibeGame1/Architectural Stone: URP lighting/shadows/fog plus procedural
         metre-scale masonry and normal-only relief; no extra geometry or decorative emission;
         M_Platform keeps its prior #475262 x0.10 emission exactly
    trims       T1 ice cyan #35DCEC · T2 brass #D8C22A x0.75 · T3 azure #2F6BFF · T4 ghost green #3FE07A
    WARM ON PURPOSE  M_Torch, M_Checkpoint (fire = safety), M_EnemyEye, M_AlertTell (the tell, 3.00)

VibeGame1/2. Create Materials -> MaterialFactory.CreateCloudSea() -> Assets/Materials/M_CloudSea.mat
    shader VibeGame1/Cloud Sea is a serialized asset reference (cannot be stripped as Shader.Find-only)
    deep/body/crest all peak below 1.0 -> no scenery bloom; transparent queue 2990, ZWrite Off, depth-tested
    ProjectHealthCheck accepts the UniversalPipeline SubShader tag for custom URP shader names

VibeGame1/4. Build Prefabs -> PrefabFactory.BuildPlayer()
    no AmbientMist on Player or camera; atmosphere cannot move with the route or view

6. Build Level -> LevelDefinitionBuilder -> Starfield.Build(def.sky.*)   ONE mesh, TWO materials
    index order (no depth is written, so index order IS draw order):
      dome -> subdued horizon silhouette -> nebulae -> stars -> PLANETS -> eclipse halo/mid/falloff
      -> lower atmosphere: 1,189 verts / 6,720 indices, fog-colour opaque below -7 degrees and a smooth
         fade to zero by +18 degrees -> opaque eclipse disc -> HDR rim; no extra renderer or material
      -> Sprites/Default performs the one premultiply; vertex RGB stays straight to avoid a dark seam
      -> [submesh 1, HDR tint CoronaHdrBoost 1.35] the white-hot rim
    per planet, five passes in order: halo -> ring FAR half -> body -> ring NEAR half
      (that ordering is the whole Saturn read; there is no alpha sort to rely on)
  + CloudSea.BuildCampaign(Level, M_CloudSea) on Sky layer
      fixed at (0,-5,150), spans 900x1400 m around route bounds x -19.5..19.5 / z -144..409.5
      90x144 grid = 13,195 verts / 77,760 indices / one renderer; shader owns every moving pixel
      vertex: three crossing swells + irregular bank lift, max crest y -3.35 below lowest underside y -1
      fragment: nested domain-warped billow bodies + stretched counter-flow erosion + broad edge feather
      scaled shader time; cloud-only 80..280 m haze converges RGB AND opacity into the same fog colour;
      the lower-sky atmosphere supplies continuous coverage beyond the clipped grid; no collider,
      particles, lights, shadows, probes or C# Update

7. Build Sandbox -> CloudSea.BuildSandbox(Sandbox, same M_CloudSea)
    fixed at (65,-5,0), spans 320x240 m around the room and movement yard through x=152
```

**Palette invariants**
- **Fog is the COMPOSITED lower atmosphere bleeding in, never a hole punched in it.** The bare dome
  horizon is not the final background after the atmosphere layer. `FogColor #20344D` (.0330 linear)
  lies above shadowed structural radiance and below representative nearby cloud luminance (.1053), so
  distance closes the former black valley instead of multiplying it. `SkyEclipseTests` and
  `ArchitecturalFinishTests` pin the shipped target and the measured ambient-by-albedo floor.
- **`fogStartDistance`'s floor is ~31 m, not the dome radius of 25.** The eclipse **halo** is a flat soft
  disc of lateral radius `discR * 2.3` = 19.8 m parked 24.1 m down the eclipse axis, so its corners are
  `sqrt(24.1² + 19.8²)` = **31.2 m** out — the widest thing in the sky mesh. A start between 25 and 31
  fogs a *wedge* across the halo. `SkyEclipseTests.TheSkyIsFogImmuneByGeometryNotByAssumption` measures
  the built mesh rather than trusting the number.
- **Fog can never touch a landing target.** Every ordinary jump in Level_01 lands within 12 m (the T3
  pillar hops are 5-6 m; solar approaches transition instead of exposing a distant landing target),
  and combat resolves at 3-8 m, so both sit at fog factor exactly zero. The ramp is for the route *ahead* — pillar line 24 m, span far end
  48 m, next arena 64-90 m. `SkyEclipseTests.FogNeverTouchesCombatOrALandingTarget`.
- **The visible lower atmosphere belongs to the world, not the player.** `CloudSea` is one scene-owned
  grid far below landing and combat surfaces. It never follows the player, raycasts, collides or writes
  gameplay state; the shader moves a common wave field under the whole route. Solid geometry wins the
  depth test, every colour stays below bloom, and broad off-route margins plus edge feathering hide the
  finite mesh in linear fog. It uses scaled shader time because atmosphere freezes with the world;
  death and Pyre mist remain unscaled authored payoffs. `CloudSeaTests` pins coverage, crest clearance,
  mesh/draw cost and material hierarchy. `AmbientMist` remains dormant as a reversible legacy helper.
- **A larger cloud rectangle cannot close the geometric horizon.** From the 36 m opening crest, the
  300 m camera plane clips the finite sea below eye level. Fully hazed cloud pixels therefore become
  opaque while the camera-centred sky mesh contributes a matching lower atmosphere at every yaw.
  Its grade runs -7..+18 degrees and is inserted before the opaque eclipse disc/HDR rim, while high
  secondary planets begin above 30 degrees. The old 64-spire silhouette is retained only as a quiet
  alpha 0.25/0.12 ruin layer instead of a row of black teeth. `SkyHorizonTests` pins the full ring,
  straight-alpha rendering-space fog colour, ordering and eclipse clearance.
- **`SandboxBuilder.EnsureEnvironment` no longer mirrors fog by hand** — it reads `ProjectSetup.FogColor
  / FogStartDistance / FogEndDistance` directly, because the hand-mirrored copies had already drifted
  (the sandbox was still violet `#0C0912` after the cold pass took the level blue). Its **ambient is
  still drifted and warm** (`#7A5540` equator vs ProjectSetup's `#3F5E88`) — known, deliberately not
  fixed in A5.
- **The cold pass changed HUE, never LIGHT LEVEL.** Every environment colour was fitted to the Rec.709
  linear luminance of the blood-red value it replaced. `SkyEclipseTests.TheColdPassChangedHueAndNotLightLevel`
  pins the four numbers; `FeatureTests.Lighting_EquatorLitsVerticals` (0.15 floor) is the play-mode half.
- **Warm means exactly two things: a combat tell, or fire.** Cold is the world. That is why the corona rim
  went cold with the sky (a warm bloom that size would be the backdrop the amber bolt has to fly across)
  and why the deflect glow, `EnemyVisuals.ParryGlow`, went the OTHER way — bone-white at the same 3.2 peak.
- **The sky can only bloom through one material.** Submesh 0's tint is exactly 1.0 and vertex colours clamp
  at 1.0, so planets, rings, stars and the whole field physically cannot cross the 1.05 threshold.
  `Starfield.PlanetPeakCeiling` 0.55 is the intent on top of that guarantee.
- **The torch light budget is LOCAL OVERLAP, not the total.** A torch is a point light, range **9**,
  intensity 2.5, no shadows, light at deck +1.9 (`LevelPieceFactory.Torch`), plus `FlickerLight`
  (cullDistance 42, hysteresis 4, frameStride 2) which disables the LIGHT past 42 m and leaves the ember
  mesh — so a distant torch costs one distance check and still reads as a beacon. Both shipped RP assets
  cap `AdditionalLightsPerObjectLimit` at **4**; the 5th light reaching a surface is dropped after being
  paid for, and which one is dropped changes with the camera (visible popping). Level_01's 44 torches peak
  at **3** reaching lights, and the 12 route beacons added by the openness pass (56 total) still peak at
  **3** — which is why there is no beacon on `T2_L1`, `T2_L2` or `T2_L6`. `Assets/Editor/Tests/TorchDensityTests.cs` pins the
  rule and the instrument; `VibeGame1/Audit Level Lights` (`LightAudit`) measures the built scene.

**Invariants**
- `LevelManager.Warp()` finds checkpoints **by name**. The checkpoints are `Checkpoint_1`..`Checkpoint_4`,
  one per tile entrance, and `Checkpoint_4` is the boss tile: `DebugKeys` F5, `TestMenu` and
  `DebugHarness("boss")` all warp there. Renaming or reordering them breaks all three.
- Tiles are a **naming convention** (`T1_`, `T2_`, `T3_`, `Boss_`), not scene sub-roots. The exporter walks
  the direct children of `Level`, so an intermediate grouping node would silently swallow everything
  under it on the next export.
- A mini-boss arena's `clearSpawnerName` must match a `SpawnDef.name` in the same definition. A missing
  mini-boss **prefab** drops that spawner, and its exit gate then never opens - the builder warns.

---

## HUD

```
gameplay ⇢ GameEvents  →  HUDController → widgets
   health / pyre / posture / boss health / boss posture → BarView
   PlayerPostureChanged / BossPostureChanged → BarView.SetNearBreak(ratio ≥ EnemyPostureBar.NearBreakRatio
        0.8, strength, NearBreakHz 4.5) (2026-09-06) — the player and boss posture bars now beat toward
        white the same way the grunt world-space bar (EnemyPostureBar) already did; previously the boss
        bar had no near-break read at all and the player bar used an undocumented 0.7 threshold for its
        colour lerp alone (no beat). BarView.NearBreakStrength is the one formula all three bars share.
        The beat is CLEARED (SetNearBreak(false)) on respawn and HUD Start, on PlayerPostureBroken, and in
        BossBarView.OnStarted/OnDefeated/Hide — a broken or re-shown bar shows the break read, never a beat
        latched from the last fight (mirrors EnemyPostureBar's `!broken` gate).
   PyreChanged        → PyreBar (bottom-left) + "<SUPER NAME> READY [Q]" banner at full
   WandCooldownChanged→ nothing on the HUD (2026-09-06, the user's ask). The wand name and its cooldown
                        hairline are gone from the loadout pane: the ONE moment a wand cooldown decides
                        anything is the moment the execute prompt is up, and ExecuteInteractor.cs:95
                        already prints "DEATHBLOW [ATTACK] WAND 2.0s" at the crosshair right then.
                        The event still fires; HUDController no longer subscribes.
   SoulsChanged       → SoulsText (top-left, "SOULS" label + the digits in mint, 26 pt).
                        HUDController.UpdateSouls ROLLS the shown number toward the wallet on the
                        unscaled clock (max(soulsRollPerSecond 24, gap × 4)/s) and flashes it toward
                        ember for soulsFlashSeconds 0.45 with a 1.14 punch about its LEFT pivot.
                        A gain rolls, a SPEND snaps — the label never shows souls the wallet does not
                        hold. Formats only when the displayed integer changes (the run timer's rule).
   item slots → ItemSlotView          deathblow banner, toasts, popups → TMP
   PromptChanged (standing) / PromptFlash (momentary) → PromptView, ONE line at (0, −132).
                        The throb now SETTLES: PromptView.PulseAmount(age, settleSeconds 0.6) scales the
                        wobble to zero 0.6 s after the shown string changes, and a settled line at rest
                        writes neither colour nor scale. A cue that stands for eight seconds (SURGE, the
                        level editor's PLAYING banner) used to throb for all eight.
   Centre-screen events speak ONE palette (HUDController statics, all under the 1.05 cap):
                        ghost teal #A8E6DA = a deflect (PERFECT) · ember #E0A030 = a reward (BLOCK, the
                        super, LEVEL CLEAR) · mint #A9D8A0 = banked (CHECKPOINT) · blood #FF3A1A = danger
                        (YOU DIED, POSTURE BROKEN) · bone #E8E2D6 = a name (the boss). PERFECT and
                        CHECKPOINT shipped the same raw cyan until 2026-09-06.
   PlayerDied / PlayerRespawned → HUDController.ClearMomentary(): the parry popup and the item toast are
                        CANCELLED, not left to fade over the death card.
   GameManager.State == Editing (F10) → HUDController hides editorHiddenRoots by ROOT and restores them
                        on the way out: Vitals, Loadout, Clock, ItemSlots, StatusStrip (+ the PYRE READY
                        banner, re-derived from the last PyreChanged since it has no pane to hang off).
                        The editor parks the CharacterController, so every one of those was a frozen
                        readout claiming to be live. NOT in the list, on purpose — ONE OWNER PER PANE:
                        RadioPane (RadioView), the crosshair (the editor aims with it) and PromptText
                        (the editor writes PLAYING to it). HudStateTests pins it.
   GhostHud (its own runtime canvas) → GhostDelta ONLY on screen: ±s under the run timer, green ahead /
                                       red behind, fading out whenever the ghost has no delta. Its
                                       leaderboard table still builds from Leaderboard.Changed but
                                       BoardVisible ships FALSE (2026-09-07, the user's ask: "remove the
                                       best runs tab from the in game UI"), so RefreshBoard writes an
                                       empty string and nothing draws. GhostRacing's context menu
                                       "Ghost/Toggle Leaderboard Panel" turns it on for debugging. There
                                       is no HUD pane path any more — HUDController has no BEST RUNS
                                       fields, and HudBuilder emits no BestRunsPane
   LevelDefinition.worldLeaderboard is authored behind Level_01's StartSpawn
      -> LevelDefinitionBuilder.BuildWorldLeaderboard: one generated root, stone backing, cyan emissive rails,
         world-space Canvas; every descendant is on Starfield.SkyLayer and has no collider
      -> WorldLeaderboardView.TrySubscribe -> Leaderboard.I.Top / Leaderboard.Changed
         "LOCAL BEST RUNS" plus rank/time/deaths, up to the authored eight rows; truthful empty state and
         "SAVED ON THIS DEVICE" footer. It is a physical local-record display, not the removed screen HUD.
      -> LevelDefinitionExporter captures the marker/config and skips the root from generic platform export.
   HintText (top-right, one line)     contextual hints ONLY — the static bind list is gone from play:
                                       ControlsInfo.Text → the settings INFO card (SettingsPanelKit, one
                                       emitter for the pause path AND the title path) and F1 → INFO
   ItemsChanged → StatusStripView (top-left, one gap under the loadout pane at y −144)
                                                            one line per HELD item, FIFO,
                                                            "> GRAPPLE" front / dimmed queue
                  + RunScoreChanged / per-frame scorer read → persistent two-level run contract:
                                                               "RUN souls/required" in mint, then quieter
                                                               "FOES n/required   SPLITS n/total"
                  + per-frame read of the player (the StaminaView idiom, not an event):
                    PlayerItems.ReboundArmed      → "REBOUND ARMED"
                    PlayerItems.DeflectSigilArmed → "SIGIL ARMED"
                    ParrySurge.Stacks     → "SPEED SURGE xN"    when N > 0
                    motor.SpeedMultiplier → "SPEED x1.5"        only for a non-ParrySurge speed source
                    Health.Invulnerable   → "GOD MODE"
                  F1 developer menu → StatusStripView.StatusEffectsVisible flips active-effect rows only;
                                      held-item and persistent run rows stay visible
                  blank when idle; the label is rewritten only when a shown value changes
   BoltRegistry.AnyCuedImpactBefore(now + Projectile.CueLead) → ProjectileThreatView at the crosshair:
                  four 30 px bracket marks, unscaled restrained pulse, alpha ≤ 0.32; hidden until the
                  projectile's existing one-shot cue has fired and impact is within 0.28 s
```

#### The Pyre fire — `FireBarView` + `VibeGame1/UI/FireBar`

```
   PyreBar (BarView)  + FireBarView (sibling; HudExtensions.ApplyPyreFire adds it at 5. Build HUD)
        BarView.Set(ratio)          → the EXTENT: anchor-driven fill (rule 5), dark ember, no white pulse
        FireBarView.LateUpdate()    → Flames image (full width, bar height + 0.9× headroom above)
              value > last + 0.005  → kick = 1        a parry stoked it; decays at 4/s (FireBarMath.DecayKick)
              heat = fill^0.8 (+ a 0.15 breath at full)                          (FireBarMath.Heat)
              mat.SetFloat(_Fill, _Heat, _Kick, _Full, _T = unscaled time)   per-instance clone, never sharedMaterial
        VibeGame1/UI/FireBar.shader  → 3-octave value noise scrolling up; fire only where x < _Fill (+ a licking front),
                                       tongues reach into the headroom by heat × noise (FireBarMath.Reach, ≤ the rect),
                                       ember → flame → core ramp, min(col, 1.0)
```

**Invariants**
- **The fire is presentation on top of `BarView`, never a second extent.** The meter's width is still the
  anchor-driven fill; the shader only reads `_Fill` to know where to stop. Rule 5 stands.
- **The HUD never blooms.** The three ramp colours ship ≤ 1.0 per channel and the shader clamps the result;
  `FireBarTests.TheRamp_NeverCrossesTheBloomCap` sweeps it. Light means "you deflected".
- **Flames stay inside the loading bar's silhouette.** The overshoot is the Flames quad's own headroom
  (0.9 × bar height) and `reach ≤ 1` of it; the fire cannot be drawn outside that rect.
- **Unscaled time.** `_T` is handed in by the component, not read from the shader's scaled `_Time`, so
  hitstop and the pause menu do not freeze the fire.
- **One per-instance material, disposed in OnDestroy.** The prefab references `Assets/Materials/UI/M_PyreFire.mat`.

### The fluid bars — `FluidBarView` + `VibeGame1/UI/FluidBar`

```
5. Build HUD → HudExtensions.ApplyAll(root) → ApplyFluidBars(root)     (Editor/HudExtensions.FluidBars.cs)
     finds BarView "HealthBar" / "StaminaBar" BY NAME, adds FluidBarView, writes every number (rule 9),
     sets fill/ghost to null-sprite Simple images (the shader's UV contract), aspect = rect width / height

BarView.Set(ratio)           unchanged: the fill's EXTENT is still the RectTransform anchors (never fillAmount)
BarView.Flash / SetColor     unchanged: Image.color is the shader's tint, so the refusal flash reads through

FluidBarView.Awake           one Material clone per Image (fill, ghost), never sharedMaterial
FluidBarView.Update (unscaled)
   → motor found lazily (FindAnyObjectByType, retried every 0.5 s; the player respawns) — OnLanded subscribed
   → a = Δvelocity / dt, ignored above maxAccel (120: a teleport, not movement)
        lateral (camera right)  → FluidSlosh.TiltTarget(a, 0.012, cap 0.15) → FluidSlosh.Step: spring 2.5 Hz / ζ 0.35
        forward                 → wave phase rate kick (0.05 per m/s², cap 6, decays at 4/s): a dash ripples the bar
        OnLanded                → level dip FluidSlosh.LandingDip(speed, 22, 0.10), springs back 3.5 Hz / ζ 0.5
   → bar.Value fell            → meniscus pulse 0.6, decaying over 0.35 s
   → shader: _Fill = bar.Value, _Level = level − dip, _Slosh = tilt, _SloshPhase, _Pulse
             (ghost: same liquid, its own _Fill read back off its anchors, no wave, no glow)
FluidBar.shader (fragment)   bar-space x = uv.x × _Fill; surface = level + wave(2 harmonics, flows) + tilt × (x − 0.5)
                             inside = below the surface; meniscus at the surface and at the leading edge
                             (edge distance × _Aspect, so both are the same thickness on screen);
                             body darkens toward the bottom; brightening is toward white, min(col, 1)
```

**Invariants**
- **BarView still owns the bar.** Extent through the anchors, colour through `Image.color`; `FluidBarView`
  only decides which pixels INSIDE the fill are liquid. Nothing that reads `BarView.Value` changed.
- **The liquid never blooms.** The shader brightens toward white and clamps at 1.0 per channel; the canvas is
  Screen Space Overlay, outside post-processing anyway. Light on screen still means "you deflected".
- **"Not too much, just enough" is a clamp, not a taste.** Tilt is capped at 0.15 of the bar height across
  the width (~2.7 px on the 18 px health bar) on the target AND the state; the dip at 0.10; the phase rate at 6.
  `FluidBarTests` holds the cap and the ring-down (< 10% of the cap 0.5 s after release, identical at 20/60/240 fps).
- **Closed-form springs** (`SlideImpulse.Spring`), so the picture does not depend on the frame rate.
- **Unscaled time.** Hitstop and pause must not freeze a liquid; a frozen wave reads as a dropped frame.
- **One material clone per Image, set once.** Never `sharedMaterial`; never a per-frame allocation.


**Invariants**
- Gameplay never references the HUD. One-way: gameplay raises, HUD listens.
- `BarView` drives the fill RectTransform's **anchors**, not `Image.fillAmount` — a UGUI `Image` with a null
  sprite silently ignores `fillAmount` and renders permanently full. That bug made the boss look invulnerable.
- All HUD animation uses unscaled time.
- **The playing HUD carries no bind dump.** `ControlsInfo` is the one source of the key reference; it is
  shown on the settings INFO card (both prefabs, one emitter) and reached from F1. `HudGlassTests.
  ThePlayingHudCarriesNoBindDump` and `SettingsPrefabTests.BothPrefabs_CarryTheSameInfoTab` hold it.
- **BEST RUNS is GONE from play (2026-09-07).** Removing it meant closing TWO draw paths: `HudBuilder`'s
  glass pane AND `GhostHud`'s fallback text block, which would otherwise have drawn the old loose
  `"<b>BEST RUNS</b>" + table` in the pane's place and looked worse than before. `HudColumnTests`
  asserts the prefab carries no `BestRuns*` object and no label reading BEST RUNS, and that
  `GhostHud.BoardVisible` ships false.
- **The top-right is ONE column: radio, hint, level-editor panel.** Every y below the radio still hangs
  off `HudBuilder.BestRunsBottom` (-272) — the constants outlived the pane they were named for and are
  now nothing but that anchor. Do not re-tune them; the hint (-12) and the 700-tall editor panel (-48)
  are positioned against them and `HudColumnTests` pins the numbers.
- **The status strip is one multi-line TMP label, rebuilt on change.** `StatusStripView.RowCount` /
  `IsEmpty` / `Text` are the test surface (`FeatureTests > HUD_StatusStrip*`); rows are rich-text
  and the generated strip is 184 px, sized for the maximum eight shipped rows. Run progress spends two rows so
  its required soul target stays primary while encounter/split context remains available at a glance. Rows
  are lines, not child objects, so there is nothing to pool and nothing serialized beyond the label.
- **The projectile bracket reinforces action, not surveillance.** It reads the existing bolt registry only
  after the world-space/audio cue has fired. It cannot aim, select, reveal an uncued attack or influence combat.
- **The developer effect toggle is effect-only and session-only.** It controls armed item / SPEED SURGE /
  generic speed / GOD MODE rows, never held inventory or the level's persistent run contract. Live views
  rebuild immediately so the F1 menu cannot leave stale text on screen.

---

## Main menu

The game boots into `Assets/Scenes/MainMenu.unity` — **build index 0**. `Level_01` and `Sandbox` follow it.
There is no Player, GameManager, TimeScaleController or HUD in that scene: nothing there is gameplay.

```
MainMenuBuilder ("9. Build Main Menu", edit mode only)
   -> Assets/Prefabs/MainMenu.prefab   canvas + TitlePanel + LevelPanel + EventSystem
   -> Assets/Scenes/MainMenu.unity     MenuCamera (solid Dark + AudioListener) + MenuAudio (AudioManager)
                                       + one MainMenu prefab instance
   -> EditorBuildSettings              MainMenu inserted at index 0; every other entry kept, reindexed

MainMenuController  (on the prefab; the whole front end)
   Awake / Start   Cursor.lockState = None, visible = true          <- BOTH: see the invariants
   Start           ShowTitle() -> Refresh()

   Refresh()   THE LEVEL LIST IS DATA
       registry.Ordered()                      <- Assets/Data/LevelRegistry.asset
       EnsureRowCapacity(n)                    clones a row if the registry grew since the last build
       per row:  displayName + orderIndex
                 "PAR mm:ss.ss"                LevelDefinition.parTime
                 "BEST mm:ss.ss"               RunStore.LoadPersonalBest(levelId).TimeSeconds,
                                               falling back to LevelProgress.BestTime(levelId)
                 LevelProgress.IsUnlocked(id, registry)  -> button.interactable, "LOCKED"
                 LevelProgress.IsCompleted(id)           -> "CLEARED"
       PlayTargetSceneName = first unlocked level's sceneName

   PLAY          -> LoadScene(PlayTargetSceneName)
   LEVEL SELECT  -> OpenLevelSelect()  (Refresh, swap panels)
   a level row   -> LoadScene(LevelDefinition.sceneName)      NOT levelId, NOT the display name
   SANDBOX row   -> LoadScene("Sandbox")                      marked DEV; never in the registry
   QUIT          -> Application.Quit(); in the editor a log + leave play mode

SceneManager.LoadScene(sceneName)
   the level scene brings its own Managers prefab:
       GameManager.Start -> SetState(Playing) -> Cursor.lockState = Locked      <- the symmetric relock
       TimeScaleController fresh, no handles held
       SpeedrunTimer      Running = false; starts only on the first movement input
       GhostRacing        created by its sceneLoaded hook (see invariants)
       WandPedestal       needs look-at + F; it never opens itself

PauseMenu.mainMenuButton -> ReturnToMainMenu()
   PrepareForSceneChange()   Close()  -> releases the 0-scale TimeScaleController handle
                             Cursor unlocked
   SceneManager.LoadScene("MainMenu")
```

**Invariants**
- **The level list is `LevelRegistry`, never a literal.** The builder emits one row per registry entry and
  `MainMenuController.Refresh()` re-reads the registry every time the panel opens; `EnsureRowCapacity`
  clones a row when the registry has more levels than the prefab was built with. Adding a level to
  `Assets/Data/LevelRegistry.asset` puts it in the menu with **no code change and no rebuild**.
- **Load by `LevelDefinition.sceneName`.** `levelId` is a save key (`samplescene`, deliberately, even though
  the scene is `Level_01.unity`) and is never a file name.
- **`MainMenu` must stay at build index 0** or the game boots into a level. `MainMenuBuilder` re-inserts it
  on every run; `FeatureTests > MainMenu` asserts it.
- **The pause menu releases its time handle BEFORE the load.** A leaked 0-scale handle across a scene load
  is how the next scene starts frozen. `PrepareForSceneChange()` exists so the suite can assert this
  without leaving the level scene.
- **Cursor is symmetric**: the menu unlocks in `Awake` *and* `Start` (the outgoing level's teardown can run
  after our `Awake`), and `GameManager.SetState(Playing)` re-locks it on entering a level. Nothing else
  touches `Cursor.lockState` on this path.
- **The menu scene has no `SpeedrunTimer`** — that is what tells `GhostRacing` "this is not a level".
  Do not put the Managers prefab in it.
- The menu palette is HudBuilder's, duplicated as constants rather than shared, so a HUD tweak cannot
  silently move the menu. **UI alphas are composited in LINEAR space**: 0.045 of an ember over near-black
  comes back out as a flat brown stripe. A "whisper" is ~0.005, not ~0.05.

---

## Wand pedestal

The pre-run loadout choice. The altar stands on the level's spawn point, so the first thing a run meets is
"which wand?" — a commitment made before the clock matters, not a key cycled mid-fight. It is **deliberate**:
aim at the altar and press **F**. Proximity alone never opens it.

**It is a dev fixture unless switched on.** `WandPedestal.DevMenuEnabled` (static, default `false`) gates
every pedestal: off, the altar does not draw, cannot register range, never prompts, and `TryInteract()` /
`Open()` refuse. The F1 test menu's **WAND PEDESTAL: OFF/ON** button is the only switch. Without it the
player simply keeps the Player prefab's loadout — all four wands, Emberlance equipped, `R` cycles.

```
LevelGreyboxBuilder / SandboxBuilder  (build time)
   plinth box (layer Default, walkable, bakes into the NavMesh)
   + trigger root (layer Interactable, SphereCollider r=3 centred 1.2 m up) → WandPedestal
     placed so the trigger already covers StartSpawn: the offer is there from the first frame

WandPedestal.DevMenuEnabled  (static bool, default false)
   TestMenu.ToggleWandPedestal()  ← "WAND PEDESTAL: OFF/ON" button (HudBuilder.BuildTestMenu)
   WandPedestal.Awake + Update    → ApplyEnabled(flag) whenever it differs from what was applied:
        renderers + lights enabled = flag, trigger collider enabled = flag,
        sibling "<name>_Plinth" SetActive(flag)  (found by the builders' naming convention),
        off → Clear() (a disabled collider fires no OnTriggerExit)
   off → Update returns before the spin/prompt/interact block; TryInteract() and Open() return false
   IsHidden  = the applied state, for tests

WandPedestal.OnTriggerEnter / OnTriggerExit ← player CharacterController   (the ItemPickup idiom)
   sets/clears inRange ONLY — the trigger is a range check, never an opener
   GameEvents.PlayerRespawned → clear

WandPedestal.Update()
   ready = !menu.IsOpen && GameManager.IsPlaying && inRange && IsLookedAt()
       IsLookedAt = dot(Camera.main.forward, dir to the floating crystal) >= lookDot (0.8, ~37° cone)
       NOT a raycast: the camera stands inside the 3 m trigger sphere and a ray from inside reports no hit
   ready changed ⇢ PromptChanged("[F]  CHOOSE WAND" | "") → PromptView     (edge-triggered)
   ready && InputReader.I.InteractPressed  →  Open(WandController)

InputReader  ← the ONLY Input System consumer
   Interact  (Player map, Button, <Keyboard>/f + <Gamepad>/buttonNorth)  →  InteractPressed

WandSelectMenu.Open(controller)        ← the LevelUpMenu / PauseMenu pattern, unchanged
   GameManager.SetState(Paused)
   timeHandle = TimeScaleController.I.Request(0f)             ONE handle, held while open
   panel.SetActive(true) → Refresh()
       rows ← WandController.loadout   displayName · kind · damage · windup/recover · description
       rows beyond the live loadout are hidden (HudBuilder builds one row per wand asset on disk)

row button → WandSelectMenu.Select(index) → WandController.Equip(index) → Close()
close button / Esc (InputReader.PausePressed) → Close()
WandSelectMenu.Close()
   TimeScaleController.I.Release(timeHandle); timeHandle = -1
   GameManager.SetState(Playing)

WandSelectMenu.ForceClose()  ← FeatureTests.RunSuite / ResetPlayerState, DebugHarness.Run
```

R still cycles wands through `WandController.Next()` — kept deliberately as a debug convenience. The
pedestal is the intended, deliberate choice; R is the fast one for testing.

**Invariants**
- The panel is procedural, built by `HudBuilder.BuildWandMenu`. Nothing about it is hand-authored, and it
  uses no `Image.fillAmount`.
- **Exactly one time handle, released on every exit path.** Select, close button, Esc and `ForceClose` all
  route through `Close()`, which is idempotent — a leaked 0-scale request freezes the game permanently.
- The pedestal never equips anything itself. It only opens the menu; `WandController.Equip` stays the single
  writer of the equipped index.
- **Range is not consent.** `OnTriggerEnter` only records that the player is nearby; the menu opens on
  `InteractPressed` while looking at the altar, and nothing else. Walking past must never pause the game.
- The prompt is raised only on change, because `PromptChanged` is a shared bus (`ExecuteInteractor` uses it
  too) and a per-frame re-raise would fight it.
- `F` is bound to **both** `Interact` and `Heal`. `WandPedestal.PromptActive` gives the altar priority and
  `FlaskAbility.Update` stands down on it — the tie is broken explicitly, never by script execution order.
- **Off means invisible AND inert, from `Awake`.** The flag is checked before anything else in `Update`,
  `TryInteract` and `Open`, so a hidden altar can never pause the game; the collider is disabled so it
  cannot even record range. `FeatureTests > WandPedestal_*DevMenuOff` hold this, and the suite enables
  the flag only for the duration of `TestWandPedestal`, restoring whatever it found.
- `WandSelectMenu.ForceClose()` stays the belt-and-braces guard for scripted runs (`FeatureTests`,
  `DebugHarness`): nothing opens the menu without input now, but a driver must never inherit one.
- The plinth stays on layer Default (it is walkable geometry); only the trigger and its visuals are on
  Interactable, keeping them out of the NavMesh bake — the same split `ItemPickup` uses.

---

## Audio

```
AudioManager.Play(Sfx, volume, pitch, jitter)
  → random variant from Resources/Audio/Sfx/<EnumName>/   (real CC0 clips)
  → falls back to ProceduralSfx.Build(sfx) when the folder is empty
Music: Resources/Audio/Music/{ambient,boss}, crossfaded on BossStarted / BossDefeated / PlayerRespawned
```

**Invariants**
- `Sfx` enum member names ARE the Resources folder names. Renaming a member silently breaks clip loading.
- An `AudioSource` added and `Play()`ed in the same scene-load `Awake` never starts — start it in `Start()`.

# Playtest build and publication

```text
MCP against the already-open, Hub-authenticated editor
  → BuildRunner.Windows / WebGL
     → derive MainMenu + LevelRegistry scenes + Sandbox
     → refuse play mode / compilation failure / missing module
     → enforce High stripping
     → Windows: Direct3D11 only
     → WebGL: Playtest template + gzip + decompression fallback
     → BuildPipeline writes Builds/<target>.staging
     → strip *_DoNotShip diagnostics
     → build-info.txt (commit, dirtiness, target settings, graphics API, scenes)
     → successful staging directory atomically replaces Builds/<target>
  → publisher rejects stale SHA or dirty game inputs
     → Windows zip / GitHub Release
     → WebGL gh-pages publisher retained but retired; branch deleted after failed browser playtest
```

The CLI/Pipeline pilot is retained, but it is not the build authority: its five-second main-thread window
can report HTTP 400 while Unity continues working. MCP owns the operation and asset/output readback. A
Windows friend build is D3D11 because automatic D3D12 selection failed at swapchain presentation on the
test machine before the menu; the same executable stayed alive under D3D11. Local-only content included in
a private friend package must be named and hashed in that package rather than mistaken for a clean,
reproducible public build.
