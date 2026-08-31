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
| [Posture and deathblow](#posture-and-deathblow) | mapped | `Posture` / `PlayerPosture` |
| [Riposte and wands](#riposte-and-wands) | mapped | `ExecuteInteractor` |
| [Viewmodel arms](#viewmodel-arms) | mapped | `WeaponViewmodel` / `ViewmodelArm` |
| [Pyre and the super attack](#pyre-and-the-super-attack) | mapped | `PlayerResources` / `UltimateAbility` |
| [Items](#items) | mapped | `ItemPickup` |
| [Enemy AI](#enemy-ai) | mapped | `EnemyController.Update` |
| [Boss](#boss) | mapped | `BossController` |
| [Progression](#progression) | mapped | `SoulsWallet` |
| [Level flow](#level-flow) | mapped | `LevelManager` |
| [HUD](#hud) | mapped | `GameEvents` ⇢ `HUDController` |
| [Audio](#audio) | mapped | `AudioManager` |
| [Wand pedestal](#wand-pedestal) | mapped | `WandPedestal` |

---

## Input

```
Unity Input System (project-wide InputSystem_Actions asset)
  → InputReader  (THE ONLY consumer of the Input System)
      exposes bools/axes: MoveAxis, LookDelta, JumpPressed, DashPressed,
      AttackPressed, ParryPressed, HealPressed, UltimatePressed,
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

---

## Movement

```
InputReader → FirstPersonMotor.Update()
    JumpPressed → TryJump()   DashPressed → TryDash()   ← public entry points; both re-check CanAct
    dt = TimeScaleController.PlayerDelta      ← NOT Time.deltaTime
    staggered? (PlayerCombat.IsStaggered) → wish *= 0.4, no jump/dash
    ground/air acceleration, coyote time, jump buffer, variable jump, dash
  → CharacterController.Move()
  ⇢ OnJumped / OnLanded / OnDashed
      → PlayerFeedback  → AudioManager (footstep/jump/land/dash)
                        → CameraFX (FOV kick), CameraShake, camera dip on the pivot
```

**Invariants**
- Player movement reads `TimeScaleController.PlayerDelta`, never `Time.deltaTime`. Hitstop and slow-mo must not
  brake the player.
- `Teleport(position, yaw)` must push yaw into `PlayerLook`, which owns rotation and rewrites it every frame.
- `PlayerLook` owns yaw/pitch; the motor owns position. Never both.

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
            → ParryController.Resolve() → ParryMath.Evaluate(elapsedSincePress, perfect, late, facing, unblockable)
                 Perfect → no damage, enemy posture, FULL Pyre gain, hitstop, flash   ⇢ ParryResolved
                 Blocked → damage × 0.3, PLAYER posture × 0.9, Pyre × pyreBlockFraction (0.35)
                 Hit     → full damage, PLAYER posture × 0.5
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
- Both successful outcomes stoke the **Pyre** meter, never a hit. See
  [Pyre and the super attack](#pyre-and-the-super-attack).

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

## Posture and deathblow

Two independent implementations with the same idea — do not merge them, they have different rules.

```
ENEMY   Posture           built by: player parries (big), player hits (small), Stormcall/Ultimate (huge)
        regen after delay, scaled by health fraction (hurt enemies recover posture slower)
        full → Break() → State.Staggered → deathblow window
        ⇢ (boss only) BossPostureChanged → HUD

PLAYER  PlayerPosture     built by: BLOCKING (damage × 0.9), taking a hit (damage × 0.5), unblockable ×1.5
        perfect parry costs NOTHING  ← the whole point
        regen after delay, faster at high health
        full → Break() → 1.5s stagger: 0.4× move speed, no jump/dash/attack/parry/flask/ultimate,
                          and 1.6× damage taken
        ⇢ PlayerPostureChanged / PlayerPostureBroken → HUD
```

**Invariants**
- Perfect parry is the only sustainable answer: blocking is a resource, not a free option.
- `Posture.Add` is a no-op while broken, so a stagger cannot be extended by piling on hits.

---

## Riposte and wands

```
enemy staggered + in cone → ExecuteInteractor.Update() ⇢ DeathblowReady(bool) → HUD banner
    wand cooling? ⇢ PromptChanged("EXECUTE   WAND 3.2s")   ← says so, never fails silently
InputReader.AttackPressed → TryExecute() → ExecuteCo:
    wand = WandController.WandReady ? Current : null       ← a cooling wand DEGRADES to melee
    player invulnerable, CanMove = false, enemy.BeginExecuted()
    melee commit (wandCommit 0.18s)
    StepInCo: CharacterController.Move toward the victim, stopping at stabStandoff (2.2m x enemy scale)
    → WandController.FireRiposte(target, weapon.executeDamage)
          readyAt = unscaledTime + wand.cooldown   ⇢ WandCooldownChanged → HUD WandCooldownBar
          OffhandViewmodel.PlayThrust(windup, hold, recover)   COCK → STAB → HOLD → WITHDRAW
              tip light ramps tipLightIdle → tipLightCharged over the cock; glints on an
              accelerating cadence at OffhandViewmodel.TipWorldPosition
          ⇢ RiposteLanded(target)        ← raised BEFORE damage, so armed Stormcall can read the victim
          THE BLAST IS DRAWN FROM THE TIP, NOT ON THE VICTIM:
              OffhandViewmodel.MuzzleFlash()             tip light blows out, lighting the victim
              SlashFx.Flare(tip)                         muzzle glint at the wand
              SlashFx.Beam(tip → contact)                the bolt leaving the wand
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
- `RiposteLanded` fires before the damage — the armed Stormcall depends on reading the victim while alive.
- Wand `windup`/`recover` set the riposte's cadence; that is what makes wands feel different.
- Falls back to the original melee deathblow when no wand is equipped **or while the wand is cooling**.
- **The cooldown gates the BLAST, never the deathblow.** Emberlance 3.5 s, Stormneedle 5.5 s,
  Gravecall 7 s, Voidspine 9 s — every one of them longer than the boss's 5 s deathblow window, so a
  strict gate would have made the boss unkillable through no fault of the player. It starts at the
  discharge, not at the end of the recover, so a slow wand's own cadence is inside its own wait.
- The cooldown is on the CONTROLLER, not per wand asset: cycling with `R` mid-fight does not refresh it.
- **A cooling wand must say so.** `ExecuteInteractor` appends the remaining seconds to the `EXECUTE`
  prompt. A riposte that quietly comes out as melee reads as a broken wand.
- **Every element of the blast is anchored to `OffhandViewmodel.TipWorldPosition`.** Anything drawn only on
  the victim reads as an explosion with no author — that was the whole of the "you can't see the wand" bug.
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
        SuperKind picks ONLY the VFX: Cleave arc / Flurry fanned beams / Quake ring / Nova double ring
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

OffhandController owns the use input (NOT PlayerItems — two readers would double-spend)
  R cycles the offhand: wand → each carried item → wand
  E uses it → PlayerItems.Use(item) → ⇢ ItemUsed
    Updraft        → FirstPersonMotor.Launch()
    SoulLantern    → health/posture/flask restored
    PhantomStep    → temporary invulnerability + SpeedMultiplier

GameEvents.PlayerRespawned → every ItemPickup re-enables; PlayerItems clears
```

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
- Stormcall arms rather than fires: the payoff is earned through a deflect-to-deathblow exchange.

---

## Enemy AI

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

**Spacing model** — `preferredRange` is the distance an enemy WANTS to fight from. It approaches, holds,
commits from there, and the lunge closes the gap during the strike. That is what makes a wind-up readable:
you can see the whole enemy when it commits.

| | preferred |
|---|---|
| Grunt | 3.0 |
| Heavy | 3.6 |
| Legendary_Ninja (The Thirteenth Shade) | 3.2 |
| Legendary_Knight (The Iron Penitent) | 4.0 |
| Legendary_Spellsword (The Ashen Chorister) | 4.3 |
| Boss (The Hollow Warden) | 4.6 |

The three `Legendary_*` mini-bosses are ordinary `EnemyController`s built by
**VibeGame1 → 4b. Build Mini-Bosses** (`Editor/MiniBossFactory.cs`), a sibling of step 4 rather than
part of it. They are deliberately NOT `BossController`s: that class raises `BossDefeated`, which stops
the speedrun timer and clears the level. See ARCHITECTURE.md → *Legendary mini-bosses*.

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

## Boss

```
BossArenaTrigger (player enters) → BossController.Activate() ⇢ BossStarted → HUD bar + boss music
  Health.deathIsStagger = true  → HP 0 does NOT kill
      → ⇢ OnZeroHealth → Posture.Break() + HoldStagger(5s)   = deathblow window
      → riposte (isExecute) → HandleDeath(): SegmentsLeft--, heal, next phase, roar
      → miss the window → HP restored to 12%, fight continues
  3 segments consumed → ⇢ BossDefeated → timer stops, LEVEL CLEAR
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
  Checkpoint_2
Tile 2  THE ASCENT               vertical - 11 ledges spiralling a 20 m tower
  (yellow)                       arena top y 20   Legendary_Knight
  Checkpoint_3
Tile 3  THE LONG SPAN            high and exposed - pillar hops, then a 22 m span with a Heavy on it
  (red)                          arena top y 28   Legendary_Spellsword
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
   PyreChanged        → PyreBar (bottom-left) + "<SUPER NAME> READY [Q]" banner at full
   WandCooldownChanged→ WandCooldownBar (top-left, under the wand name)
   item slots → ItemSlotView          deathblow banner, toasts, popups → TMP
```

**Invariants**
- Gameplay never references the HUD. One-way: gameplay raises, HUD listens.
- `BarView` drives the fill RectTransform's **anchors**, not `Image.fillAmount` — a UGUI `Image` with a null
  sprite silently ignores `fillAmount` and renders permanently full. That bug made the boss look invulnerable.
- All HUD animation uses unscaled time.

---

## Wand pedestal

The pre-run loadout choice. The altar stands on the level's spawn point, so the first thing a run meets is
"which wand?" — a commitment made before the clock matters, not a key cycled mid-fight. It is **deliberate**:
aim at the altar and press **F**. Proximity alone never opens it.

```
LevelGreyboxBuilder / SandboxBuilder  (build time)
   plinth box (layer Default, walkable, bakes into the NavMesh)
   + trigger root (layer Interactable, SphereCollider r=3 centred 1.2 m up) → WandPedestal
     placed so the trigger already covers StartSpawn: the offer is there from the first frame

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
