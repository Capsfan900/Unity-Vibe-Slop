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

---

## Combat: outgoing attack

```
InputReader.AttackPressed → WeaponController.Update() → TryAttack()   ← public entry point
    staggered/drinking/parrying? → refuse
    staggered enemy in range?    → ExecuteInteractor.TryExecute() instead   (see Riposte)
    else StartSwing() → coroutine:
        wait hitDelay → Physics.OverlapSphere(cam + fwd*hitOffset, hitRadius, EnemyMask)
            → Health.TakeDamage(baseDamage × comboMult × PlayerStats.DamageMultiplier)
            → Posture.Add(weapon.postureDamage)
            → hit spark, hitstop, shake
        combo window → next swing
```

**Invariants**
- Player→enemy hits are instantaneous sphere queries (no projectiles, no hitbox colliders). This shape is
  deliberately server-authority-friendly.
- Damage scales through `PlayerStats.DamageMultiplier(weapon)`, never hard-coded.

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
  **0.62 m**, Sunbreaker **0.72 m** — a 2.3× spread where the dagger pass had 1.2×. Length is free because
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
               plus `subdivisions` interpolated points toward it, pushed newest-first
    fading   → widthMultiplier *= fade, material alpha = fade², and the tail RETRACTS
               (count → ceil(peak * fade)) so the arc closes instead of hanging there
    LineRenderer (useWorldSpace = false) under the CAMERA, additive URP/Unlit from
    SlashFx.CreateAdditiveMaterial
```

**Shipped values** — written explicitly by `PrefabFactory` on `ViewmodelRoot` (rule 9): 12 points,
3 subdivisions per frame, head width 0.030 m, width curve `1 → 0.5 @0.3 → 0.18 @0.65 → 0.03`,
fade 0.11 s, peak channel **1.15**.

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

## Riposte and wands

```
enemy posture breaks → EnemyVisuals.Slump(true) → the body BUCKLES back and down (never forward)
                     → SetDeathblowReady(true) → the deathblow SPOT goes up on that enemy's sternum
                       (this is the Sekiro mark: on the body, not on the HUD, raised by the BREAK and
                       not by where the player is looking). It is also the point the whole beat is
                       aimed at, and its lifetime is exactly the window.

enemy staggered + in cone → ExecuteInteractor.Update() → Target / HasMarkedTarget
                            ⇢ DeathblowReady(bool) → HUD banner (boss only)
                            ⇢ PromptChanged("DEATHBLOW  [ATTACK]")        ← names the INPUT
    wand cooling? ⇢ PromptChanged("DEATHBLOW  [ATTACK]  WAND 3.2s")  ← says so, never fails silently

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
    wand = WandController.WandReady ? Current : null       ← a cooling wand DEGRADES to melee
    player invulnerable, CanMove = false, enemy.BeginExecuted()
    melee commit (wandCommit 0.18s)
    StepInCo: CharacterController.Move toward the victim, stopping at stabStandoff (2.2m x enemy scale).
              It only ever CLOSES the gap, and the press had to be inside range (3.5 m) — so on a
              2.2x boss the real riposte distance is the RANGE, not the standoff, and that is the
              tighter framing the stagger pose has to survive.
    → WandController.FireRiposte(target, weapon.executeDamage)
          readyAt = unscaledTime + wand.cooldown   ⇢ WandCooldownChanged → HUD WandCooldownBar
          OffhandViewmodel.PlayThrust(windup, hold, recover)   COCK → STAB → HOLD → WITHDRAW
              tip light ramps tipLightIdle → tipLightCharged over the cock; glints on an
              accelerating cadence at OffhandViewmodel.TipWorldPosition
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
- Wand `windup`/`recover` set the riposte's cadence; that is what makes wands feel different.
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
- **Every element of the blast is anchored to `OffhandViewmodel.TipWorldPosition`.** Anything drawn only on
  the victim reads as an explosion with no author — that was the whole of the "you can't see the wand" bug.
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
- **The wand carries its own light.** The world is near-black and the wand shaft is a dark material, so
  without `OffhandViewmodel`'s tip light the prop silhouettes into the background and only the emissive
  tip survives. The light also lights the victim, so the charge doubles as the read on who you are killing.
- Screen flash and chromatic aberration are capped low on purpose. Both were previously loud enough
  (0.55 alpha, 1.0 chroma) to destroy the wand they were meant to punctuate.
- Poses, scales and the standoff all live on the Player prefab or on `WandData`, so **`PrefabFactory` and
  `WandFactory` write them explicitly** — see CLAUDE.md rule 9. `FeatureTests > WandReadability` asserts it.

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
        SuperKind picks ONLY the VFX, and EVERY element of it is anchored to
            WeaponViewmodel.TipWorldPosition — the sword's point, the hammer's head, the dagger's tip:
              all kinds, first beat: Beam(grip → tip) + Flare(tip)      the blow starts in your hand
              Cleave  Fan(tip, 170 deg) + Arc ahead of the tip + Sparks off the edge
              Flurry  Beam(tip → fanned target) once per hit, + a muzzle glint at the point
              Quake   raycast down from the head → impact; Beam(head → impact), Flare(impact),
                      360 deg spoke fan FROM THE IMPACT POINT, debris up
              Nova    360 deg spoke fans from the tip, one lifted out of plane
    hitStop(superHitStop), shake(superShake), Release()
```

| Weapon | Super | Kind | Dmg × hits | Posture | Reach | Arc | Wind-up |
|---|---|---|---:|---:|---:|---:|---:|
| **Cerulean Edge** (sword) | Emberfall Arc | Cleave | 150 × 1 | 70 | 5.5 m | 170° | 0.30 |
| **Rosethorn** (dagger) | Thornstorm | Flurry | 34 × 9 | 26 ea | 4 m | 70° | 0.14 |
| **Sunbreaker** (hammer) | Sunbreak | Quake | 200 × 1 | 130 | 7.5 m | 360° | 0.52 |
| **Oathbreaker** (dev) | Oathbreaker | Nova | 600 × 1 | 400 | 12 m | 360° | 0.06 |

**Invariants**
- **The Pyre bar does not decay.** Fights in this game are separated by long stretches of parkour; a
  bar that bleeds out between arenas would tax the traversal the game is built around. It has exactly
  one sink (the super, which spends it in full) and one reset (`PlayerRespawned`).
- **A perfect deflect must always be worth strictly more than a block.** `pyreBlockFraction < 1` is
  what keeps the tighter window worth chasing. `FeatureTests > Parry_BlockStokesPyreLessThanPerfect`.
- **The blast leaves the WEAPON.** Every element is anchored to `WeaponViewmodel.TipWorldPosition`, the
  main-hand twin of `OffhandViewmodel.TipWorldPosition` that the wand riposte already uses. Drawn from
  `transform.position` — which is what it did — a super reads as something happening *to* the player
  rather than something they authored: the hand swings and an unrelated effect goes off around the navel.
  The quake in particular traces the hammer head down to the floor and radiates from *that* contact point.
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
           → FirstPersonMotor.BeginPull(StandoffPoint(e), 0.35 s)
                StandoffPoint = enemy feet + (player side, flat) × ExecuteInteractor.stabStandoff × data.scale
                motor.Update: `if (pulling) { AdvancePull(dt); return; }` — the item OWNS velocity:
                  position curve start→target, sine arc (0.35..2.2 m), ease-out, on the motor clock;
                  one cc.Move per frame; ends on arrival, on a Sides/Above collision, or CancelPull
                  (Teleport calls it). ⇢ OnPullEnded(arrived). Slide/wall run/dash cancelled on entry.
           → on arrival (e alive):
                PlayerItems.IsBig(e)  = BossController, or name / data.name starts "Legendary"
                big && !IsStaggered   → e.Posture.Add(0.35 × Posture.Max); flare + sparks; DONE
                otherwise             → if (!IsStaggered) e.Posture.Break()   → HandleBroken → State.Staggered (sync)
                                        → ExecuteInteractor.ExecuteNow(e)     → the SAME ExecuteCo as a pressed
                                          deathblow: wand riposte (WandController raises RiposteLanded) or
                                          melee execute (interactor raises it), isExecute damage, hitstop
  WallSurge → FirstPersonMotor.StartWallSurge(8 s)   wallSurgeUntil on the motor clock `now`
              IsWallSurging → WallRunSettings: topSpeed ×1.5, accel ×1.5, minEntrySpeed 0
                            → TryWallRun: TooSlow gate and stamina.TrySpend skipped
                            → AdvanceWallRun: stamina.Drain skipped
                            → CanWallRunNow ignores stamina
            → ItemVfx.Surge (feet ring + SurgeTrail: arcs off the wall while surging AND running)
            → prompt "SURGE Ns" once a second (SurgePromptCo), "" on expiry
            → StatusStripView reads IsWallSurging / WallSurgeRemaining per frame → "WALL SURGE  N.Ns" row

THE PROMPT LINE — two channels (2026-09-06)
  GameEvents.PromptChanged (string)        → PromptView.standing : a cue true while a condition holds
        "DEATHBLOW  [ATTACK]" (ExecuteInteractor), "GRAPPLE  [DASH]" (FlareGrapple), "SURGE N.Ns" (PlayerItems),
        the level editor's PLAYING line. Every writer is EDGE-TRIGGERED (raises only when its own string changes).
  GameEvents.PromptFlash (string, seconds) → PromptView.flash : momentary, drawn OVER the standing cue and then
        gone — "PERFECT" (PlayerFeedback, 0.9 s), "NO TARGET" (PlayerItems, 0.6 s).
  PromptView.Current = flash while unexpired, else standing. Unscaled time. Before the split there was one
  channel, so a PERFECT erased a live GRAPPLE cue permanently (nothing re-raised it). FeatureTests: Prompt_*.
  RESIDUAL, known: the STANDING slot still has several writers and no owner, so one writer's clear ("" at the
  end of a SURGE) blanks another's live cue until that writer's own string changes. Next smallest step is an
  owner key on the standing slot, so a clear only lands if the clearer is the one being shown. BACKLOG 0b.

GameEvents.PlayerRespawned → every ItemPickup re-enables; PlayerItems clears

THE SENTRY FLARE (2026-09-06; parkour_enemies)
  Posture.OnBroken on a pshooter_* → SentryBurst.HandleBroken → pendingBurst, resolved NEXT frame in Update:
      still alive and not Executed → Detonate() (kills through Health.TakeDamage: souls, EnemyKilled, mist) + the flare.
      A parkour enemy NEVER waits to be finished (user, 2026-09-06).
  Health.OnDied on a pshooter_* → SentryBurst.HandleDied: not Executed / DiedExecuted → Detonate() (the flare).
      The reflected bolts kill it (parriedProjectileDamage 30 on 60 HP = 2 deflects; 45 on 130 HP = 3).
  THE EXCEPTION: the grapple hook (PlayerItems.PullCo: Break + ExecuteNow in one frame, State.Executed, then the
      killing blow) -- EnemyController.DiedExecuted -- throws NO flare. The finish is the reward, not a lift.
  Detonate(): SentryFlare.Spawn(chest, forward, up 9, out 3, gravity 4, life 4.5) + the burst (2.4 m flash,
      2.6 m ring shockwave, sparks, Thunder, shake). The flare is optional traversal.
  SentryFlare.Update (scaled time): position = FlareMath.Position(origin, v, g, t) -- floats up ~10 m, hangs, sinks;
      glow = 1 - t/life drives size and colour; Grappleable while glow > MinGrappleGlow 0.08; gone at life.
      SentryFlare.Live is the registry.
FLARE GRAPPLE (FlareGrapple on the Player prefab, DefaultExecutionOrder -50, BEFORE the motor)
  every frame while playing, not pulling, not executing:
    Target = nearest-to-crosshair Grappleable flare within range 30 m, coneDeg 20, world ray clear
    prompt "GRAPPLE  [DASH]" when Target != null and ExecuteInteractor has no target
  DASH pressed with a Target → GrappleNow(f): line + FovKick + Sfx.Dash, motor.BeginPull(flare - 0.6 m, 0.35 s)
    motor.OnPullEnded (arrived OR cut) → motor.Launch(tossUpSpeed 14) [rule 10 entry point], FovKick 8,
      ChromaticPulse(tossChroma 0.16, 0.25s) [g-force read, a separate channel from FovKick -- VFX pass
      2026-09-06, the toss used to have no visual anchored to the player at all], shake, Sfx.Teleport,
      SlashFx.Ring(player position, hue, radius 1.4, 0.25s) [sells "thrown FROM here"],
      f.Consume() (sparks; the flare is spent by the use). Tosses++.
    the motor's own Update returns early while IsPulling, so the press never doubles as an ordinary dash
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

### Offhand slot

```
OffhandController  = the player's left hand, ALWAYS visible (OffhandViewmodel)
   Kind = Wand (default) | Item
   Wand in hand  → a riposte blasts (WandController.FireRiposte), charge builds at the tip
   Item in hand  → the wand is STOWED; the riposte falls back to a melee deathblow
   ⇢ OffhandChanged(kind, item, wand) → HUD readout
```

**Invariants**
- The offhand is a SHARED slot. Equipping an item genuinely costs you the wand riposte — that trade is the
  decision the slot exists to make.
- The wand is on screen full time. It previously existed only for the length of a riposte, which is why the
  blast read as an explosion with no visible source.
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
  OnTrackChanged → RadioView (HUD root, RadioPane in the top-right CORNER; BEST RUNS moved one column left to
                   HudBuilder.BestRunsX -348, BestRunsBottom / HintText / LevelEditorPanel unchanged): rebuilds the
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
THE DRILLMASTER   (Legendary_Drillmaster; sandbox pad x 14, z -26, SpawnEnemyInFront 9) -- the showcase body:
   every signature on cooldown, a 1.25 s DELAYED overhead in a 0.5 s fight, a feint, an unblockable kick,
   a far-band lunge, flaskPunishChance 1.0, posture 150. Knight silhouette in slate and cold blue.
```

```
EnemyController  = the BRAIN ONLY. Rig-agnostic: it knows states, timings and distances.
   ├─ IEnemyLocomotion    (NavMeshLocomotion today; root-motion rig later)
   └─ IEnemyPresentation  (EnemyVisuals today; Animator-driven rig later)

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
| Boss (The Hollow Warden) | 4.6 |

The three `Legendary_*` mini-bosses are ordinary `EnemyController`s built by
**VibeGame1 → 4b. Build Mini-Bosses** (`Editor/MiniBossFactory.cs`), a sibling of step 4 rather than
part of it. They are deliberately NOT `BossController`s: that class raises `BossDefeated`, which stops
the speedrun timer and clears the level. See ARCHITECTURE.md → *Legendary mini-bosses*.

Three of them (`Legendary_Spellsword`, `Legendary_Knight`, `Legendary_Marionette`) carry an **imported
mesh** from `Assets/Enemies/*.fbx` in place of the primitive body. Nothing in this map changes: the
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
`rangedOnly` + `shootsProjectiles`, violet body). `Level_01_Level.asset` places only these; the melee
`Enemy_Grunt` / `Enemy_Heavy` are `souls_enemies` on the sandbox pads. `ProjectileShooter` is added by
`PrefabFactory` only to a body whose data shoots.

```
EnemyController.Update()  Idle → Chase when dist ≤ WakeRange (= max(aggroRange, projectileMaxRange) for a shooter: 32 m)
                          AND HasLineOfSight (any of three lines clear: head 1.5, chest 0.8, feet 0.2 -- a runner
                          behind a rail is still seen). A SENTRY (EnemyData.rangedOnly: Grunt, Heavy) in Chase
                          only locomotion.Stop() + FaceTarget: it holds its perch, never commits a combo, and
                          never goes back to sleep. A test may still call BeginCombo on it directly.
ProjectileShooter.Update()   (on every Enemy_* prefab; fires only when EnemyData.shootsProjectiles)
   gate: awake (Current != Idle), alive, not staggered, not committed (no bolt during a melee wind-up),
         not aggroLocked, player inside [projectileMinRange 3, projectileMaxRange 32], HasLineOfSight (same three lines)
   → on the METRONOME (ProjectileMath.NextBeat: Grunt 1.6 s, Heavy 2.4 s, no jitter; a held beat stays on the
     grid, a silence longer than one beat re-anchors instead of bursting):
     speed = ProjectileMath.LaunchSpeed(dist, projectileSpeed 40 / 36, CueLead 0.28, CueMargin 0.08) -- inside 14.4 m the
             launch slows so every flight is ≥ 0.36 s and the cue is never owed before the bolt exists
     target = ProjectileMath.LeadTarget(muzzle, chest, motor.Velocity (flat), speed, projectileLead 1.0), and in flight
              the bolt HOMES toward the chest at projectileHomingDegPerSec (180 / 150) -- 2026-09-06: a bolt never
              sails past unparriable; core 0.55 m, hitRadius 1.0
     a 0.36 m Bolt sphere at the chest --
     SlashFx additive ember with Projectile.HotCore (peak 1.6) written OVER the normalised colour: THE ONE
     GLOW IN TRAVERSAL, because the bolt is the tell -- plus a 2-point additive trail 0.12 s long,
     Projectile.Fire(shooter, data, dir = toward the LED target AT FIRE TIME, the launch speed above (Grunt 32, Heavy 28 m/s
     beyond 11.5 m; a mid-band shot flies 0.47 s -- answered at a run, never waited for)
Projectile.Update()  (scaled time: hitstop freezes it)
   straight line; remaining = ProjectileMath.TimeToImpact(dist, speed)
   → remaining ≤ 0.28 s once → Sfx.ParryCue + the bolt flares ×2.3 (CueFlareScale) and its core goes white-hot (CueCore)
                                                                        (the same lead every attack gives)
   → within hitRadius of the chest → PlayerCombat.ReceiveAttack(AttackInfo{projectileAttack, shooter})   (rule 3)
        Perfect → the bolt REFLECTS at ×1.4 toward the shooter's chest,
                  motor.AddImpulse(ProjectileMath.SpeedGain(look.AimForward, parrySpeedGain 9))   (rule 10: motor entry point;
                  a run at 11 becomes 20 and bleeds toward the 17.6 air soft cap -- a boost, not a new cruise)
                  CameraFX.FovKick(4); ReceiveAttack already did the deflect sparks / hitstop / OnParried
        Blocked / Hit / None → spent (the chip / damage / posture landed in ReceiveAttack as usual)
   reflected bolt reaches the shooter → Health.TakeDamage(parriedProjectileDamage) + Posture.Add(parriedProjectilePosture),
                                        sparks, Sfx.Hit, spent
```

**Invariants**
- **A bolt is an attack** and resolves only through `PlayerCombat.ReceiveAttack` (rule 3). Nothing here writes health or posture on the player.
- **The flight is the tell, and it is cued at 0.28 s like every attack.** `projectileMinRange / projectileSpeed` must exceed the lead (`ProjectileTests`); at 32 m/s the cue is 9 m out, which is why the band starts at 10 m.
- **The bolt is the one glow in traversal.** Every other effect stays under the 1.05 bloom cap; the bolt's core ships at 1.6 (`Projectile.HotCore`, pinned by `TheBoltIsTheOneGlowInTraversal`) because it is the ATTACK'S tell, and the shooter itself still never glows until it is deflected.
- **A deflect buys speed through the motor** (`AddImpulse`), flattened along the LOOK — aim at the next ledge and deflect (rule 10; MOVEMENT-PRINCIPLES 5 and 6).
- **One attack at a time**: the shooter never fires inside a melee wind-up or strike, so a tell is never two things.
- **The data decides** (rule 9): every number is on `EnemyData`; the component carries none. The Warden and the legendaries do not shoot.

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
        ├→ PlayerLook.mouseSensitivity / stickSensitivity   (public fields; PlayerLook is
        │                                                     never edited by settings code)
        ├→ CameraFX.baseFov + cam.fieldOfView               (CameraFX rewrites FOV every frame,
        │                                                     so both, and once more a frame after
        │                                                     sceneLoaded because CameraFX.Start
        │                                                     captures baseFov)
        ├→ QualitySettings.SetQualityLevel, THEN vSyncCount, THEN targetFrameRate
        ├→ Screen.SetResolution                             (builds only; no-op in the editor)
        └→ Volume.profile (runtime CLONE, never sharedProfile): Bloom.intensity =
                                                              authored x scale; FilmGrain on/off
   applied on Awake, on every sceneLoaded (+1 frame), and on every Changed.
```

**Invariants**
- Sensitivity is applied OUTWARD onto `PlayerLook`'s public fields. Settings code never edits
  `PlayerLook.cs`; that is the architecture, not a workaround for file ownership.
- Opening from the pause menu takes its own `TimeScaleController.Request(0f)` handle (rule 1) and
  re-enables `PauseMenu` one frame late, because `PausePressed` is true for the whole frame.
- Bloom writes the runtime clone. Writing `sharedProfile` from play mode dirties the asset on disk.
- Sliders are stock UGUI `Slider`s driven by anchors — never `Image.fillAmount` (rule 5).

### Stamina

```
FirstPersonMotor (dash, wall run entry, wall jump)
  → PlayerStamina.TrySpend(cost, StaminaAction)   whole or nothing; never partial
       dash 30 | wall-run entry 12 | wall jump 12          (a run exit / exit-grace jump is FREE)
  → PlayerStamina.Drain(22/s, used)  every wall-run step → false at 0 → EndWallRun(Exhausted)
  (both wall-run calls are skipped while FirstPersonMotor.IsWallSurging — the Wall Surge item)
  PlayerStamina.Update   dt = PlayerDelta (rule 1); regenDelay 0.45 s after any spend, then
                         +45/s grounded, +18/s airborne, to max 100. Infinite (F8 god mode) never spends.
  ⇢ GameEvents.StaminaChanged(cur, max)  → StaminaView.bar (BarView, 3 ticks at 30/60/90)
  ⇢ GameEvents.StaminaRefused(action)    → StaminaView: bar.Flash(red), label names the ability 0.8 s
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
  TryWallJump, run in progress
     → PerfectMath.WallJumpFromRunIsPerfect(wallRunElapsed, wallRunMaxDuration 1.75, perfectWallJumpWindow 0.14)
       the loan has ≤ 0.14 s left: the wall is about to give up            → Perfect(WallJump, 20)
  TryWallJump, exit grace (the run ended on its own: Expired / Decayed / LostWall / Exhausted)
     → PerfectMath.GraceJumpIsPerfect(now − wallRunLeftAt, 0.14)           → Perfect(WallJump, 20)
  ground jump fires while IsDashing && !LastDashWasBurst   (a grounded dash is jump-eligible for its whole
     length -- dashFromGround -- and the jump ENDS the dash so vel.y survives; see ENGINEERING-LOG)
     → PerfectMath.DashJumpIsPerfect(now − dashStartedAt, minDelay 0.04, window 0.12)
       [0.04, 0.16] s after the dash fired — never the same frame           → Perfect(DashJump, 30 = the dash)
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
  letting go, the dash's launch, the pull landing. Windows are seconds on the motor clock, 0.12–0.14 s —
  inside the learnable band between Celeste's 0.08 s coyote and Sekiro's 0.20 s deflect — and
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
    ground branch, in water:   rel = TraversalMath.WaterStep(hv − flow, wish, WaterFloorSpeed, waterAccel, dt)
                               hv  = rel + flow
                               WaterFloorSpeed = groundSpeed × waterSpeedScale (1.35 → 14.85 m/s)
                               no groundFriction, no groundOverspeedDecay, turned at 30 m/s² not 90
    slide branch, in water:    the SAME step; slideEndsAt is pushed every frame (no decay end, no cap):
                               a slide on water ends only off the water or on a jump
    air branch, in water:      the boost zone: airCarryDecay is skipped (the carry is kept); airSoftCap still bleeds
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
- **Water never slows anyone** (`WaterStep` targets `max(floor, carried)`), and **still water never starts a
  stationary body moving**. The only way off the water floor is off the water or a jump.
- **Water is a stay-refreshed touch, never an Enter/Exit pair.** A CharacterController disabled for a teleport
  sends no Exit; a grace on the motor clock cannot be left on.
- **The burst window opens when control returns, not when the pull lands**, and it is forfeited after
  `pullBurstHold` rather than fired stale.
- **No held lens channel for water.** `FovHold`, `SetRoll`, `SetRumble` each keep one writer (SlideFx); water
  gets a one-shot kick on entry and textures (spray, hiss) only — a slide crossing water must not have two
  writers on the lens.
- **The motor's existing numbers are untouched.** Six new fields, all written by `PrefabFactory.BuildPlayer`
  and asserted by `PivotMovementTests`.

### The in-game level editor — `LevelEditor` + the one piece factory

```
LevelDefinition asset  ──LevelDocument.FromDefinition──►  LevelDocument (JSON mirror: platforms, spawns,
        ▲                                                   pickups, checkpoints, torches, balloons, waters,
        │ doc.CopyTo(def)  (EXPORT ASSET, editor only)      playerStart)  ◄──ToJson / FromJson──►  <persistentDataPath>/levels/<name>.json
        │
   8. Build Level From Definition (Editor/LevelDefinitionBuilder)      LevelEditor (on the HUD prefab; HudBuilder.BuildLevelEditor)
        │  EditorContext: AssetDatabase / PrefabUtility / static flags   RuntimeContext: the serialised library (materials, prefabs, items)
        └───────────────► LevelPieceFactory.BuildDocument(doc, root, ctx) ◄───────────────┘
                              Platform (+Trim) · PlayerStart "StartSpawn" · Spawner · Checkpoint
                              Torch (under "Torches") · Pickup (under "Pickups") · Balloon · Water
                              every object gets a LevelPiece tag (kind, index)
   arenas / pedestals / sky / kill zone / NavMesh / Player / Managers / HUD   stay in the builder (campaign only)

LevelEditor.Update  (GameState.Editing; InputReader is the only input reader — 15 optional Editor* actions)
   F10 ─► Enter(): returnPosition, fly camera on PlayerLook, cursor locked, panel shown
   Aim(): ray from the lens → grid snap (1 m platforms/water, 0.5 m else; Alt = free) → preview cube
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
```

**Invariants**
- **Level_01's parkour-first layout is CODE that writes the asset** (`LevelDefinitionAuthoring.Apply`, menu
  `8a`): perches + spawn moves + the balloon arc + the water lines, idempotent. `LevelTraversalAnalyzer`
  flies the arc (pop = carry trimmed to `launchCarryCap`, `launchFloatSeconds` at `launchGravityScale`, the
  re-armed dash allowed only on the final fall) and casts the shooters' bolt lines (muzzle → deck chest,
  inside the shooter's band, crossing no box); `Level Arc Report` prints all three; `LevelTraversalTests`
  holds them off a copy of the asset. A pop's spacing is DERIVED from the flown pop, never copied from
  another level (ENGINEERING-LOG, "A balloon chain laid to the yard's spacing").
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
BossArenaTrigger (player enters) → BossController.Activate() ⇢ BossStarted → HUD bar + boss music
  Health.deathIsStagger = true  → HP 0 does NOT kill
      → ⇢ OnZeroHealth → Posture.Break() + HoldStagger(5s)   = deathblow window
      → riposte (isExecute) → HandleDeath(): SegmentsLeft--, heal, next phase, roar
      → miss the window → HP restored to 12%, fight continues
  3 segments consumed → ⇢ BossDefeated → timer stops, LEVEL CLEAR → back to the MENU

     BossDefeated
        → SpeedrunTimer.Stop, RunRecorder saves the ghost, AudioManager fades the boss track
        → HUDController.OnBossDefeated: "LEVEL CLEAR" + the run time, 8 s
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
```

---

## Level flow

`Level_01` is **one continuous run** of four connected sections: three main tiles, each ending in a
gated legendary mini-boss, then the boss tile. One timer, no scene loads.

```
Sanctum (top y 0, pink)          wand altar - the loadout is chosen before the clock matters
  Checkpoint_1
Tile 1  THE SHATTERED CAUSEWAY   low, fast, horizontal - stepping stones + a railed dash run
  (cyan)                         arena top y 4    Legendary_Ninja
                                 wall-run walls (right, both run north, both optional):
                                   T1_Wall_Start    (7.1, 2.5, 22.5)  1.2 x 7 x 20   skips the four stones
                                   T1_Wall_Causeway (3.9, 2.5, 50)    1.0 x 7 x 16 → T1_Wall_Landing (4.6, 2, 64.5) 4 x 1 x 10
  Checkpoint_2
Tile 2  THE ASCENT               vertical - 11 ledges spiralling a 20 m tower
  (yellow)                       arena top y 20   Legendary_Knight
                                 wall-run walls (outside of the spiral; a run DESCENDS, the exit jump buys the height back):
                                   T2_Wall_East (11.1, 5.5, 123.5)  1.2 x 9 x 21 → T2_Wall_Landing_East (6.8, 7.5, 137.5) 5.6 x 1 x 8
                                   T2_Wall_West (-11.2, 10.5, 115)  1.2 x 9 x 22 → T2_Wall_Landing_West (-7.05, 12, 101.75) 5.1 x 1 x 9.5
  Checkpoint_3
Tile 3  THE LONG SPAN            high and exposed - pillar hops, then a 22 m span with a Heavy on it
  (red)                          arena top y 28   Legendary_Spellsword
                                 wall-run walls (right, both run north, both optional; they chain):
                                   T3_Wall_Pillars (7.1, 22.5, 198.5) 1.2 x 7 x 16 → T3_Wall_Landing_S (5.5, 22.5, 210.5) 6 x 1 x 7
                                   T3_Wall_Span    (7.6, 26, 224.5)   1.2 x 8 x 20
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
  entry gate rests SUNK, rises to seal you in on OnTriggerEnter
  boss arena      (clearSpawner null)  -> BossController.Activate(); nothing ever reopens
  mini-boss arena (clearSpawner set)   -> exit gate rests UP; Update() watches that spawner and
                                          drops BOTH gates once its enemy is dead
                                          (latched on "seen alive" so it cannot open at level start)

LevelManager  owns spawners, checkpoints, respawn
  Checkpoint trigger → SetCheckpoint(): heal, refill flask ⇢ CheckpointReached
  death / void-fall (9m below last grounded) / KillZone → PlayerDeath → Respawn():
      ResetEnemies() (re-instantiate every spawner, ResetArena() on every trigger) → Teleport
      ⇢ PlayerRespawned → items restored, posture cleared, boss bar hidden, ambient music
SpeedrunTimer: starts on first movement input, stops on BossDefeated, unscaled, excludes menus
```

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
gameplay ⇢ GameEvents (24 events)  →  HUDController → widgets
   health / pyre / posture / wand cooldown / boss health / boss posture → BarView
   PlayerPostureChanged / BossPostureChanged → BarView.SetNearBreak(ratio ≥ EnemyPostureBar.NearBreakRatio
        0.8, strength, NearBreakHz 4.5) (2026-09-06) — the player and boss posture bars now beat toward
        white the same way the grunt world-space bar (EnemyPostureBar) already did; previously the boss
        bar had no near-break read at all and the player bar used an undocumented 0.7 threshold for its
        colour lerp alone (no beat). BarView.NearBreakStrength is the one formula all three bars share.
        The beat is CLEARED (SetNearBreak(false)) on respawn and HUD Start, on PlayerPostureBroken, and in
        BossBarView.OnStarted/OnDefeated/Hide — a broken or re-shown bar shows the break read, never a beat
        latched from the last fight (mirrors EnemyPostureBar's `!broken` gate).
   PyreChanged        → PyreBar (bottom-left) + "<SUPER NAME> READY [Q]" banner at full
   WandCooldownChanged→ WandCooldownBar (top-left, under the wand name)
   item slots → ItemSlotView          deathblow banner, toasts, popups → TMP
   GhostHud (its own runtime canvas) → writes the PB table INTO HUD.BestRunsPane (glass, top-right,
                                       300×196 at (−32,−32); shown only once there is a board) — falls
                                       back to its own text when the pane is absent
   HintText (top-right, one line)     contextual hints ONLY — the static bind list is gone from play:
                                       ControlsInfo.Text → the settings INFO card (SettingsPanelKit, one
                                       emitter for the pause path AND the title path) and F1 → INFO
   ItemsChanged → StatusStripView (top-left, under SOULS)   one line per HELD item, FIFO,
                                                            "> GRAPPLE" front / dimmed queue
                  + per-frame read of the player (the StaminaView idiom, not an event):
                    motor.IsWallSurging   → "WALL SURGE  6.4s"  (WallSurgeRemaining, tenths)
                    motor.SpeedMultiplier → "SPEED x1.5"        when ≠ 1
                    Health.Invulnerable   → "GOD MODE"
                  blank when idle; the label is rewritten only when a shown value changes
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
- **BEST RUNS is a glass pane, and it clears the clock and the level-editor panel** by rect; the pane
  ships hidden and `GhostHud` owns showing it.
- **The status strip is one multi-line TMP label, rebuilt on change.** `StatusStripView.RowCount` /
  `IsEmpty` / `Text` are the test surface (`FeatureTests > HUD_StatusStrip*`); rows are rich-text
  lines, not child objects, so there is nothing to pool and nothing serialized beyond the label.

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
