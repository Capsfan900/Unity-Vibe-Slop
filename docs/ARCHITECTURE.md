# Architecture

How `vibegame1` is put together, and the invariants that keep it working.

Related: [TOOLING.md](TOOLING.md) · [ENGINEERING-LOG.md](ENGINEERING-LOG.md) · [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md) · [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md) · [multiplayer-system-design.md](multiplayer-system-design.md)

---

## The four rules

1. **`TimeScaleController` is the only thing that writes `Time.timeScale`.** Everything else *requests* a
   scale and gets a handle back.
2. **`InputReader` is the only script that touches the Input System.** Everything else reads its
   properties.
3. **All combat resolves through `PlayerCombat.ReceiveAttack`.** One function decides
   Perfect / Blocked / Hit and applies every consequence.
4. **Everything is regenerable.** Materials, data, prefabs, HUD and level are all built by editor code.
   Nothing in the scene is hand-authored, so the scene is disposable.

---

## Module map — `Assets/Scripts/`

| Folder | Files | Contents |
|---|---:|---|
| `Core/` | 9 | `GameManager` (state machine, cursor, `runInBackground`), `InputReader`, `TimeScaleController`, `GameEvents`, `Layers`, `ViewCamera`, `SettingsData` / `SettingsStore` (PlayerPrefs under `vg1.settings.*`) / `SettingsApplier` (the only thing that pushes settings outward; self-bootstrapped `DontDestroyOnLoad`) |
| `Combat/` | 6 | `Health`, `Posture`, `DamageInfo`, `ParryMath`, `PostureMath`, `EmissiveFlash` |
| `Player/` | 20 | `FirstPersonMotor` (+ `WallRunMath`, same file), `PlayerLook`, `LockOnController`, `LockOnMarker`, `OffhandViewmodel`, `PlayerCombat`, `ParryController`, `PlayerPosture`, `PlayerStats`, `PlayerResources`, **`PlayerStamina`**, `WeaponController`, `WeaponViewmodel`, `ViewmodelArm`, `WandController`, `ExecuteInteractor`, `FlaskAbility`, `UltimateAbility`, `PlayerItems`, `PlayerDeath` |
| `Enemies/Core/` · `Enemies/parkour_enemies/` · `Enemies/souls_enemies/` | 5 | **Two families since 2026-09-06** (`EnemyPaths`): `parkour_enemies` = the span sentries `Sentry_*` (`Projectile`, `ProjectileShooter`, `ProjectileMath`; never melee, mostly shoot); `souls_enemies` = the duels: Grunt, Heavy, the Warden (`BossController`) and every `Legendary_*`. Shared brain in `Core/`: `EnemyController` (FSM), `EnemyVisuals`, `EnemyPostureBar`, `EnemySpawner` |
| `Level/` | 6 | `LevelManager`, `Checkpoint`, `ItemPickup`, `BossArenaTrigger`, `KillZone`, `SpeedrunTimer` |
| `UI/` | 11 | `HUDController`, `BarView`, **`StaminaView`**, `BossBarView`, `ItemSlotView`, `ScreenFlash`, `PromptView`, `PauseMenu`, `WandSelectMenu`, `SettingsMenu` (one class serves both the title screen and the pause path), **`MainMenuController`** |
| `Feel/` | 22 | `CameraShake`, `CameraFX`, `PlayerFeedback`, `FlickerLight`, `LightningEffect`, `AudioManager`, `ProceduralSfx`, `ParryImpulse` / `ParryImpact`, `DashImpulse` / `DashFx`, `SlideImpulse` / `SlideFx` (the `*Impulse` is pure math, the `*Fx` / `*Impact` applies it), `SlashFx`, `WeaponTrail`, `WeaponEmber`, `PyreArc`, `EnergyGlow`, `ItemVfx`, `DeathMist`, `SkyFollower`, `Starfield` |
| `Progression/` | 4 | `SoulsWallet`, `Bloodstain`, `UpgradeMath`, `LevelUpMenu` |
| `Data/` | 9 | ScriptableObject definitions (see below) |
| `Debug/` | 5 | `DebugKeys`, `TestMenu`, `DebugHarness`, `FeatureTests`, `SandboxController` |

---

## Asset layout

| Path | Contents |
|---|---|
| `Assets/Scenes/` | `MainMenu.unity` (**build index 0** — the game boots here), `Level_01.unity` (the campaign level) and `Sandbox.unity` |
| `Assets/Settings/` | URP pipeline assets and renderers — `PC_RPAsset` / `PC_Renderer` and `Mobile_*` variants, plus the volume profiles |
| `Assets/Data/` | All ScriptableObject tuning (see below) |
| `Assets/Materials/` | Generated `M_*.mat`, all URP/Lit |
| `Assets/Prefabs/` | Generated prefabs — Player, Managers, HUD, enemies, boss, weapons, pickups |
| `Assets/Resources/Audio/` | `Sfx/<SfxName>/*` and `Music/{ambient,boss}.ogg` |
| `Assets/Scripts/` | Runtime code, namespace `VibeGame1` |
| `Assets/Editor/` | Generators, health check, test runner — namespace `VibeGame1.EditorTools` |

The PC tier uses a deferred renderer; the Mobile/WebGL tier is forward. Both are set to HDR colour
grading by `ProjectSetup`.

---

## Event bus

`GameEvents` is a static class of events. Gameplay raises, UI listens — the HUD has no direct references
into gameplay. `GameEvents.ClearAll()` exists for domain-reload safety.

`PlayerHealthChanged` · `PyreChanged` · `WandCooldownChanged` · `FlaskChanged` · `SoulsChanged` · `WeaponChanged` ·
`WandChanged` · `ParryResolved` · `PlayerDamaged` · `PlayerDied` · `PlayerRespawned` ·
`CheckpointReached` · `EnemyKilled` · `BossStarted` · `BossHealthChanged` · `BossPostureChanged` ·
`BossDefeated` · `PromptChanged` · `UltimateUsed` · `PlayerPostureChanged` · `PlayerPostureBroken` ·
`DeathblowReady` · `ItemsChanged` · `ItemPickedUp` · `ItemUsed` · `RiposteLanded`

`RiposteLanded` is raised **before** the killing damage, so listeners can still read the victim's
position and state. It is raised by whichever path actually lands the riposte: `WandController` at the
discharge, or `ExecuteInteractor` in the no-wand melee fallback — and the Grapple kill goes through
`ExecuteInteractor.ExecuteNow`, so it raises it the same way. The two are mutually exclusive; never
raise it twice for one riposte.

---

## Singletons

Twelve, all exposing a static `I`. Note for any future multiplayer work: roughly half are per-player
concepts and would need de-singletoning first — see the design doc.

| Singleton | Owns |
|---|---|
| `GameManager` | Game state (Playing/Paused/LevelUp/Dead/Won), cursor lock |
| `InputReader` | Every Input System action |
| `TimeScaleController` | `Time.timeScale`, hitstop, slow-mo, pause |
| `LevelManager` | Spawners, checkpoints, respawn, enemy reset, bloodstain |
| `SpeedrunTimer` | Run clock |
| `SoulsWallet` | Currency |
| `AudioManager` | SFX pool + crossfading music |
| `CameraShake`, `CameraFX` | Camera feel |
| `ScreenFlash` | Full-screen flashes |
| `SettingsApplier` | Pushes `SettingsStore.Current` onto `PlayerLook`, `CameraFX`, `QualitySettings`, the URP volume clone. `DontDestroyOnLoad`, bootstrapped by `RuntimeInitializeOnLoadMethod` — nothing to place |
| `SettingsMenu` | The settings panel, one instance per scene (title screen and pause path share the class) |

---

## Time scale

Requests carry an `affectsPlayer` flag; the **minimum** active request wins per channel.

| Effect | Scale | `affectsPlayer` | Result |
|---|---|---|---|
| Hitstop | 0.02 | `false` | World freezes, player keeps full momentum |
| Super-attack slow-mo | 0.25 | `false` | World crawls, player does not — the power fantasy |
| Pause / menus | 0 | `true` | Everything stops |

- **Player movement must read `TimeScaleController.PlayerDelta`, never `Time.deltaTime`.**
- UI and feedback use `Time.unscaledDeltaTime` so they survive hitstop and pause.
- Weapon swing animation deliberately uses **scaled** time, so hitstop freezes the swing.

---

## Combat flow

```
EnemyController.Update
  Windup  → EnemyVisuals.Telegraph  (dim charge, arm rears back)
  cue     → EnemyVisuals.CueFlash + Sfx.ParryCue      [cueLead = 0.28 s before impact]
  Strike  → DoImpact (distance + cone test)
              └→ PlayerCombat.ReceiveAttack(AttackInfo)
                   └→ ParryController.Resolve → ParryMath.Evaluate
                        Perfect → no damage, enemy posture, FULL Pyre, hitstop, flash
                        Blocked → timed press: 30% damage + 0.9× posture + 35% Pyre
                                   HELD GUARD:  0% damage + 1.5× posture + no Pyre
                        Hit     → full damage + player posture (1.6× if staggered)
```

Player→enemy hits are `Physics.OverlapSphere` in front of the camera (`WeaponController.DoHit`).
Enemy→player hits are a distance + cone test at the scheduled impact time — no hitbox colliders.

### Combat feel contracts

- **Parry windows.** Perfect `0.13` / late `0.12` / whiff recovery `0.5`. `cueLead` `0.28` is serialized
  on `EnemyController` and must stay ≈ reaction (0.20) + half the perfect window. No enemy attack windup
  below `0.45`.
- **Posture (Sekiro).** Both sides have it. A perfect parry costs the player **nothing**; a held guard
  costs `damage × 1.5` posture, a timed block `damage × 0.9`, a raw hit `damage × 0.5`, unblockables
  ×1.5 on top. Player break = 1.5 s stagger, 0.4× move speed, no jump/dash/attack/parry/flask/super, and
  1.6× damage taken. Enemy break opens the deathblow window.
- **The guard (hold RMB).** Defence used to be press-only, so between presses the player was simply
  naked. Holding the parry button now raises a **stance**: any blow that would have been a Hit resolves
  as a Blocked instead. Four rules make it an addition beneath the timing game rather than a replacement
  for it. (1) **The timing is evaluated first and the stance only upgrades a Hit** — a hold can never
  produce a Perfect, so the deflect is strictly better than the guard, always. (2) **It costs posture,
  not health**: chip 0, posture `× 1.5` (the biggest multiplier in the game), no Pyre at all — the guard
  is the floor under a fight, not an achievement, and turtling must not charge the super. (3) **Posture
  does not regenerate while it is up** (`guardPostureRegenMultiplier` 0), so turtling is a losing
  strategy with a visible clock on it: three guarded 20-damage hits break you. (4) **Unblockables and
  your own back are still uncovered**, and **your own swing drops your guard**. Shipped from
  `DataFactory` on `PlayerStatsData` / `GameFeelSettings` (rule 9); one binding, two reads
  (`InputReader.ParryPressed` for the window, `ParryHeld` for the stance).
- **A guarded hit is a THUD, not a beat.** It eats the damage, so the blow has to arrive as force or it
  reads as nothing happening: `guardHitStop` 0.05 (shorter than a deflect’s 0.09 — you did not earn
  this one), a `guardShove` of 1.2 m off the line, nine dull grey-steel sparks struck half-way between
  the blade tip and the incoming line, and `WeaponViewmodel.GuardImpact` kicking the stance back and
  recovering it. It deliberately borrows **none** of the deflect’s pale-steel screen flash or chromatic
  pulse: enemies no longer glow, so the deflect is the only bright event in a fight and a guard that
  looked like one would make the two outcomes indistinguishable.
- **Deathblow (Sekiro).** Breaking an enemy's posture makes the body **buckle** — it leans back, sags
  and throws its guard open, and it never moves toward the player — and raises a **mark on that enemy**,
  a small flat violet spot on its sternum. The deathblow then happens only when the player deliberately
  attacks that marked enemy. An attack press with nothing marked is an ordinary
  swing; against a marked enemy the same press is consumed by `ExecuteInteractor`, and `CommitCue`
  bursts violet sparks out of the victim's sternum, runs a beam from the weapon tip to it and plays the
  execute stinger *on the press frame* so the player can feel that the button did something different.
  The prompt names the input (`DEATHBLOW [ATTACK]`) rather than just the verb. Before this the press was
  already deliberate and nothing on screen said so, which is why it played as automatic.
- **The mark is a small flat glowing spot on the sternum.** One billboarded quad of `M_DeathblowMark`
  (violet, peak 2.60, blooms), rolled to a diamond, stepped onto the body surface toward the eye because
  a mark at the centre of mass renders inside the mesh. It began as a crossed diamond of three cubes
  over the head — an object in the world rather than a mark on a body, and at deathblow range a blob the
  camera runs into. Its size is **angular**, not fixed: the spot stands off the chest toward the viewer,
  so it is always nearer than the body and a fixed-size quad grows faster than the enemy does as the
  player closes. And it is **dropped on the press frame** by `BeginExecuted`, so it can never be between
  the wand tip and the body at contact — the commit shatter takes over that same point.
- **Two spots on one torso.** The lock-on dot lives at the centre of mass (1.05) and the deathblow mark
  at the sternum (1.45), and they are separated on five axes at once: height, size (~5% of the frame
  against ~1%, both angular so neither grows into the other), hue (violet against pale bone-grey),
  brightness (2.60, blooms hard, against 0.81, deliberately under the 1.05 threshold) and motion (rolls
  and breathes against dead still). `FeatureTests > Deathblow` fails if the hues or the heights converge.
- **The riposte is a shot, and the victim has to stay in it.** The stagger pose is signed entirely away
  from the camera, the wand's full extension finishes near screen centre so the tip visually lands on
  the chest, the blast blooms from the chest **surface** (a point at the centre of mass renders inside
  the mesh), and the death animation falls **backwards** — a corpse pitching forward at `stabStandoff`
  drops a whole body through the lens on the frame the player is watching the blast.
- **Boss deathblow.** HP 0 does **not** kill — it breaks posture and opens a 5 s window; only
  `isExecute` damage removes a segment. Missing the window restores 12% HP. The `DeathblowReady` HUD
  banner exists because players could not tell this was happening.
- **Aggression.** Non-boss enemies are relentless by design: `EnemyData.aggression` (Grunt 0.9,
  Heavy 0.75, Boss 0) scales down recovery, cooldown, parry recoil and combo gaps, and enemies step
  toward the player during Recover. A perfect parry mid-combo makes the *next* hit come faster (a real
  Sekiro exchange), floored so it never becomes unreactable.
- **Pyre.** Every *successful* parry stokes the meter — a perfect deflect at full rate
  (`basePyrePerPerfect` 25 + Arcane + `weapon.pyreBonus`), a block at `pyreBlockFraction` 0.35 of that.
  A raw hit gives nothing. It **does not decay**: fights here are separated by long parkour stretches
  and a draining bar would tax the traversal. One sink (the super, spent in full), one reset (death).
  Roughly four clean deflects to full.
- **The weapon is the meter.** `Feel/WeaponEmber.cs` sets the blade on fire in proportion to Pyre, from
  a single ember at a sliver of charge to a blaze at full. This is the player's *primary* read on their
  own charge; the HUD bar only confirms it. Intensities are deliberately conservative — see the art
  direction note below.
- **The super leaves the weapon.** Every element of a super's VFX is anchored to
  `WeaponViewmodel.TipWorldPosition` — the main-hand twin of the wand's `OffhandViewmodel.TipWorldPosition`
  — so the blast is visibly authored by the blade, head or point that swung. The quake traces the hammer
  head down to the floor and radiates from *that* contact point. An effect centred on the player reads as
  something happening to them, not something they did.
- **Super attack (`Q`, full Pyre).** One per weapon, authored entirely as `WeaponData.super*` fields.
  Sword *Emberfall Arc* (one 170° sweep), dagger *Thornstorm* (nine stabs in a 70° cone, posture-heavy),
  hammer *Bronzefall* (0.52 s wind-up into a 360° quake, 130 posture, 6 m knockback), dev blade
  *Oathbreaker* (instant 12 m nova). `SuperKind` chooses only how the blow is drawn; the geometry is
  the numbers. `UltimateAbility` is the driver — repointed, not replaced, so `Q` and the prefab wiring
  survive. Rule 1 holds: `affectsPlayer:false` slow-mo, realtime waits.
- **Wand cooldown.** The riposte blast is a resource: Emberlance 3.5 s, Stormneedle 5.5 s, Gravecall 7 s,
  Voidspine 9 s. It gates the **blast**, never the deathblow — a cooling wand degrades to the melee
  execute and the prompt says so, because every cooldown is longer than the boss's 5 s deathblow window.
- **Lock-on (Souls, in first person).** `MMB` acquires the enemy nearest the crosshair, raises a small
  pale dot on its chest, and then **softly keeps it framed while the mouse is still**. There is no camera
  to orbit here and the mouse is the parry hand, so the contract is one line: *the mouse is always
  authoritative and the assist only spends frames the player is not using.* Nothing scales or filters the
  look delta — `PlayerLook.Update` applies it untouched and `LockOnController` adds a correction
  afterwards in `LateUpdate`, multiplied by a gate that reaches zero as soon as the mouse moves. The
  correction is proportional (`4 deg/s` per degree of error, capped at `90 deg/s`, dead inside `2.2 deg`)
  so it eases rather than snaps, and it runs on `PlayerDelta` so hitstop cannot lurch it. Mousing more
  than `62 deg` off the target **drops** the lock rather than dragging you back. One key does all three
  verbs, chosen by where you aim: release if you are still looking at what you hold, switch if you are
  not. It also drops on death, out of range (`32 m`) and 0.7 s of unbroken occlusion.
- **Movement feedback** lives in `Feel/PlayerFeedback.cs` — footsteps, jump/land/dash/slide/wall-jump audio,
  landing dip scaled to fall speed, and the eye drop during a slide. It subscribes to `FirstPersonMotor`'s
  `OnJumped` / `OnLanded` / `OnDashed` / `OnSlideStarted` / `OnWallJumped`. The landing dip and the slide
  crouch are **separate** offsets on the same pivot: the dip is a spring back to zero, the crouch is held for
  as long as the slide lasts, so the two never fight.
- **A wall run sheds grit and sparks from the feet** (2026-09-03 evening). `WallRunFx` keeps a 40-mote additive
  pool in a scene-level root and throws it back down the run from the foot contact on the face, at a rate that
  falls with speed and with the age of the loan (`WallRunImpulse.GritRate`, pinned in `WallRunImpulseTests`),
  so a run about to let go visibly thins out; each foot-tick adds three discrete sparks. Dust, not fire:
  peak channel 0.55, zero bloom, unscaled time, no per-frame allocation.
- **Slide (`Left Ctrl`).** A momentum move, not a crouch: refused below `5 m/s`, so you cannot slide out of a
  standstill. Entry is `max(current, run) + 5` capped at `22`, i.e. **16 m/s out of an 11 m/s run**. It bleeds
  at `2/s` to a floor of `8 m/s` — **4.0 m in 0.35 s**, measured, and identical from 20 fps to 400 fps. The
  capsule drops `1.8 → 1.0 m`, so a slide passes under geometry a standing player cannot. Average speed over
  the slide (11.5 m/s) is barely above a run: **the slide is not the reward, the jump out of it is.**
- **Downhill slide exception (2026-09-07).** On actual contact with a walkable downhill slope, the motor
  sustains the slide and applies slope acceleration without dry friction. Acceleration caps at the
  greater of the existing slide ceiling and carried speed; it does not manufacture extra overspeed.
  A displacement-only ground snap follows the contacted plane. Flat ground, uphill travel, water and
  airborne travel keep their existing rules, and jumping still cancels immediately.
- **Slide-jump is the tech.** Jumping cancels a slide without touching horizontal speed, so a slide-jump
  leaves the ground at **15.8 m/s against a run's 11**, and clears **12.6 m against 8.8 m** with the jump key
  held. Skill expression is entirely in *when* you cancel: press early and you keep 16 m/s, ride the slide to
  its floor and you leave at 8. There is no way to get that speed without deciding to.
- **Wall jump (`Space`, airborne, near a wall).** No binding of its own — it resolves inside the motor after
  the coyote jump has had its chance, so it can never steal an ordinary jump. Detection is an **8-direction
  spherecast fan** around the direction you are asking for, not a single ray: a wall jump taken while looking
  anywhere but straight at the wall is the normal case in first person, and a ray turns that into a silent
  miss. It sets `vel.y = 11` (**a 2.02 m rise, held; 0.92 m if you release**), adds `12 m/s` along the wall
  normal, and **keeps the component running along the wall** — dropping only the part going into it, which is
  what makes a chimney read as flow rather than a reset.
- **The same wall twice is refused** (normals within ~32°), so one face is not a free ladder; two facing walls
  alternate normals and chain. `maxWallJumps` (5) bounds a chimney to about **8-10 m of climb per airtime**,
  which is one tower section, not an elevator. Landing forgives the wall.
- **Wall run (no binding).** Entered by *arriving*: airborne (no coyote wait — running off a ledge along a
  wall attaches on the next frame), off a 0.20 s cooldown, under `maxWallRuns` 6 (stamina is the real
  bound), able to pay 12 stamina, and then the gates in `WallRunMath.CanEnter` — **total** horizontal speed
  `wallRunMinEntrySpeed` 6 (a jog qualifies; a shuffle never does), falling slower than
  `wallRunMaxEntryFallSpeed` 9 (a run extends a line, it does not undo a plummet), travel within
  `wallRunMaxApproachCos` 0.80 (**53°**) of the wall plane — a sprint-jump taken at 45° toward a wall is
  how a first-person player actually arrives at one — and not looking *backwards* (`wallRunMinLookAlongCos`
  −0.05). A slide that left the ground yields to the wall. **Entry redirects all of your horizontal speed
  down the run** (Titanfall: the wall catches you, it does not bill the angle) and floors `vel.y` at
  `wallRunEntryUpSpeed` 3 (the catch, ~1.25 m of borrowed height). Gravity ramps `0.10× → 0.60×` on t² over
  `wallRunMaxDuration` 1.75 s, so the end of the loan is legible while you are still on the wall. Speed
  along the face bleeds at `wallRunSpeedDecay` 0.35/s (stick released) or is topped up at `wallRunAccel`
  14 m/s² toward **`wallRunTopSpeed` 13.75 — 1.25× a sprint: the wall is faster than the floor, by exactly
  that much** (held forward); under `wallRunMinSustainSpeed` 4 the wall drops you — a sprint entry rides
  the full clock, a 6 m/s entry bleeds out at 1.16 s; an empty stamina bar drops you too (`Exhausted`,
  22/s on the wall). Losing contact at a seam is ridden out for `wallRunLostGrace` 0.15 s before the run
  ends. `wallRunStickSpeed` 2.5 presses you into the face on displacement only. `Space` while running is
  the exit, `WallRunMath.Exit`: `vel.y = 10`, +7 along the normal, **+4 along the run**
  (`wallRunExitTangentBoost`) — a wall jump throws you *off* the wall, a run exit throws you *down the
  line*, clamped to `dashSpeed` — and a press within `wallRunExitGrace` 0.15 s of a run ending on its own
  is *still that exit*, never a whiff (the wall-run coyote time; Titanfall's players never trusted the wall
  until an early or late press paid). `PlayerLook` sums two roll channels about the camera's own forward: a
  held `rollBias` of `wallRunCameraRoll` 13° **away from the wall** (Titanfall's convention — it shipped
  toward the wall for a day and was played as "reversed"; `WallRunLive_LeansAwayFromTheWall` pins the sign)
  and a transient `rollKick` of 7° back toward the wall on exit, so the release snaps through level; the
  aim vector never moves. Same-wall refusal and `lastWallNormal` are shared with the wall jump. Full
  map: DATAFLOW "Movement — wall run".
- **Momentum is a soft cap, never a ceiling.** `FirstPersonMotor.DecayExcess` bleeds only the speed *above*
  a cap on `exp(-k dt)`: in the air the excess over `airSoftCap` 17.6 (1.6× run) at `airDrag` 3/s, so a 22
  m/s dash is within 1 m/s of the cap half a second later — you kept 17.6, you did not keep 22 forever; on
  the ground the excess over a sprint at `groundOverspeedDecay` 4/s. `maxHorizontalSpeed` 27.5 is the hard
  clamp nothing legal reaches. The slide boost (+5) **fades with the speed you already carry** (full at a
  sprint, zero at `slideMaxSpeed`) and with every slide chained inside `slideChainWindow` 1.2 s
  (`slideChainFalloff` 0.6: 5, 3, 1.8, 1.1 …). Speed is earned once and preserved; it is never minted on
  every press, which is what stopped "dash and move endlessly at a constant speed".
- **Weight: you fall harder than you rise, you spend what you carry, and a hard landing costs.** (2026-09-03
  evening, after play: "the movement needs more weight, I can still just shoot off a wall or ledge".) Gravity is
  `-30` rising and `fallGravityMultiplier` 1.5× (`-45`) while airborne and descending, so `jumpHeight` still means
  2.4 m but the way down is heavy — a held flat jump lands at ~14.7 m/s instead of 12. In the air the excess over
  a sprint bleeds at `airCarryDecay` 0.8/s *before* the soft cap: a 17.6 m/s wall exit is 15.4 half a second later
  and 13.9 after one, a burst you spend rather than a glide you keep. And a landing at or above `landingSoftSpeed`
  16 costs horizontal speed, linearly to `landingSpeedLoss` 35% at `landingHardSpeed` 26 (a 7.5 m drop); it is
  applied before that frame's slide press, so the landing-slide is still how you keep a run alive off a drop.
  All three are pure functions (`FallGravity`, `DecayExcess`, `LandingSpeedFactor`), pinned in `AirFeelTests`,
  and `LevelArcAnalyzer` reads the first two off the prefab so every route is re-judged under them.
- **Air control is a steer, then a bleed.** (Same session: "more control in the air, like CS:GO surfing".)
  `AirSteer` turns the airborne velocity toward the stick at `airSteerDegPerSec` 120 with the speed untouched —
  you decide *where* 17 m/s goes, you cannot pump it — and only while the stick is within 90° of travel. Then
  Source's `AirAccelerate` adds along the stick only up to a run, which is what a stick held back brakes with.
  Steer, then accelerate, then the two decays. The analyser's three control modes (hold / brake / none) are
  unchanged by the steer because a stick along or against travel has nothing to turn toward.
- **The controller is pressed into the floor by distance, not by speed.** The final `cc.Move` displacement is at
  least `groundSnapDistance` 0.12 m down while grounded and not rising. The old `-2 m/s` pin moved 0.004 m per
  frame at 500 fps — inside the 0.05 skin — and `CharacterController.isGrounded` flickered off, which is why the
  suite's slide measured `0/80 frames grounded` and 1.66 m, and why a slide-jump pressed more than 0.12 s into a
  slide was refused on a fast machine: the coyote window had expired on a player standing on the floor. The sweep
  stops at the floor, so on flat ground the snap costs nothing; on a step down it follows; at a ledge it is one
  frame of extra drop.
- **Stamina is the budget, and it is on screen.** `PlayerStamina` (100): dash 30, wall-run entry 12 then
  22/s, wall jump 12; regen 45/s grounded and 18/s airborne after a 0.45 s delay. Three dashes from full;
  one comes back in about a second on the ground; a full bar always covers a full wall run (50.5), which
  `LevelArcAnalyzer` assumes. `StaminaView` draws the bar with a tick at every dash between health and
  posture, three pips (**DASH / AIR / WALL**) that light only when the ability is available *this frame*
  (stamina and cooldown and the air charge), fades to 45% when full and idle, and on a refusal flashes red
  and names the ability — never a silent no. `F8` god mode makes it infinite.
- **The motor keeps its own clock.** Every timer — dash, slide, coyote, jump buffer, wall-run cooldown — is
  measured against a motor-local `now` advanced by `TimeScaleController.PlayerDelta`, never `Time.time`.
  `Time.time` is the world clock and hitstop drives it to ~0.02×; on it a dash that landed a hit kept
  travelling for the whole freeze and every window grew by the hitstop length. Hitstop now neither freezes
  nor extends a dash, a slide or a coyote window.
- **The movement path allocates nothing and is measured, not asserted.** `FindWall` is 0 bytes over 20 000
  calls at 2.36 µs, and runs at most once per jump press. The wall run adds two spherecasts on eligible
  airborne frames and one per frame while running. See DATAFLOW's Movement invariants.

**Perfect timing (2026-09-04).** Three moves have a PERFECT: a wall jump on the wall's last breath (the loan's
final 0.14 s, or the exit-grace jump within 0.14 s of a natural let-go), a jump thrown 0.04–0.16 s out of a
dash, and the grapple burst pressed in the first 0.12 s of its window. A perfect gives stamina back — the
dash's own 30, 20 for the wall, +30 for the burst — clamped to the bar; a miss is simply the ordinary move.
The windows sit in the learnable band (Celeste's 5-frame coyote below, Sekiro's ~12-frame deflect above) and
are judged on the motor clock, so they are the same width of time at any frame rate. `PerfectMath` is pure;
`FirstPersonMotor.OnPerfect` and `PlayerStamina.Refunded` are the seams the feel layer and the HUD hang off.

### Enemy aggression

`EnemyData.aggression` (0–1) is read through `EnemyController.Aggression`. **Grunt 0.9, Heavy 0.75,
Boss 0** — the boss is deliberately excluded and keeps its authored phase pacing; its pressure comes from
phases, not this multiplier.

Aggression compresses *dead time only*, never wind-ups:

| Scaled by aggression | Effect at 1.0 |
|---|---|
| `attack.recovery` | ×0.35 |
| `data.attackCooldown` | ×0.30 |
| `data.parryRecoilSeconds` | ×0.55 |
| Combo gap | ×0.45, minus `0.04 s` per consecutive parry |

Enemies also **step toward the player during `Recover`** (`StepToward`, via `agent.Move` so they stay on
the NavMesh), so backing off never buys a free reset. And a deflect no longer ends the exchange: at
`aggression ≥ 0.5` with hits remaining, the enemy recoils briefly then **resumes its combo**, each
follow-up arriving sooner — a real Sekiro exchange, floored so it never becomes unreactable.

> Because every wind-up stays ≥ `0.45 s` and the cue always fires `cueLead` before impact, raising
> aggression raises **pressure**, never unreadability. That separation is the whole safety property —
> preserve it.

**Gating.** Every path out of `Recover` re-checks `aggroLocked`. See
[ENGINEERING-LOG.md](ENGINEERING-LOG.md) — omitting that let a parried boss wake before its arena trigger.

### Legendary mini-bosses

Three named duellists gate the road to the Hollow Warden. Built by **VibeGame1 → 4b. Build Mini-Bosses**
(`Editor/MiniBossFactory.cs`) from data written in `DataFactory`; prefab keys `Legendary_Ninja`,
`Legendary_Knight`, `Legendary_Spellsword`.

They are plain **`EnemyController`s, not `BossController`s** — deliberately. `BossController` owns
deathblow segments, `Health.deathIsStagger`, the HUD boss bar and `RaiseBossDefeated`, and that last one
stops the speedrun timer and clears the level. A mini-boss wired as a boss would end the run three times
before the Warden. They read as legendary through their data — scale, palette, souls, moveset length —
and through a distinct silhouette. Feedback is the world-space `EnemyPostureBar`, as on Grunt and Heavy.

**Two of the three now carry an imported body** rather than the primitive rig. `Legendary_Spellsword`
uses `Assets/Enemies/AshenChorister.fbx` (a hooded scythe wraith with a tentacle skirt) and
`Legendary_Knight` uses `Assets/Enemies/IronPenitent.fbx` (a furnace-bellied iron figure); the Ninja
still uses the primitives, which remain the reference implementation. Both are ~12 000 triangles,
`Universal Render Pipeline/Lit` on the shared `M_Boss`, and both went in through the seam described in
*Swapping enemy models* below without one line of gameplay change: `EnemyVisuals` is still the plain
primitive component, its bindings simply point at the imported mesh instead. Two consequences worth
knowing:

- **`EnemyVisuals.eye` is where the model's signature light lives.** It is the one sanctioned always-on
  enemy emissive and it is already driven by `Posture.Ratio`, so the Chorister's eye slot and the
  Penitent's furnace grate are the *same* channel every other enemy uses — smouldering at rest, flaring
  ember-orange as the posture bar fills. Nothing new glows; the Penitent's grate going white-hot at the
  break is simply that ramp reaching the top, and it is the most legible "kill me now" tell in the game.
- **`armPivot` / `weaponPivot` are empty transforms, not the model's bones.** The forge auto-rig is
  placed by proportion and does not follow the art, so the wind-up reads through `LungeRoot`'s whole-body
  lean and the base-colour sink-and-snap rather than through a swinging limb. See ENGINEERING-LOG.

Each one exists to teach one parry skill, and the next assumes you have it:

| | Name | Shape | Teaches |
|---|---|---|---|
| `Legendary_Ninja` | **THE THIRTEENTH SHADE** | 3–5 hit strings of 0.45 s cuts, aggression `1.0`, recoveries at `0.22`, `preferredRange 3.2` | **Ride the cadence.** Sustained deflect rhythm — and `Ninja_Reap`, an unblockable sweep inside that rhythm, teaches that holding the cadence is not the same as holding parry. |
| `Legendary_Knight` | **THE IRON PENITENT** | the spinning furnace: a 1.15 s spool-up into 3–4 beats of `Knight_SpinHit` on a steady ~0.94 s interval, then `Knight_SpinOut` and its 2.2 s recovery. aggression `0.55`, `comboBreathSeconds 0.18`, `1.6×` scale at `preferredRange 4.2` | **Hold a cadence under pressure.** Every beat is parryable and the interval never changes, so the fight is one sustained rhythm rather than a series of separate reads — and the payoff is deliberately outsized: each deflected beat is `parryPostureMultiplier 1.3` (32.5 posture with the sword), so **8 clean deflects break his 260 bar**, and the break is a **5.0 s** stagger straight into a deathblow that kills him outright. Blocking is worth *zero* enemy posture, so it must be real deflects. `Knight_Overhead` survives as a rare 1.0 s tempo break, and `Knight_Vent` — a wide unblockable out to 8 m, gated to the far band — exists so that backing off and waiting the spin out is never the optimal line. |
| `Legendary_Spellsword` | **THE ASHEN CHORISTER** | hybrid; `Spellsword_Emberfall` opens at range, `Spellsword_Feint` fakes the end of a phrase, `Spellsword_Grasp` punishes greed | **Do not trust the phrase.** Champion-Gundyr shaped: the feint's short `recovery` plus a long `comboGap` makes the breath after the heavy a lie, and stepping in to punish it is what `Grasp` is for. The hardest of the three. |

The Shade is fast through **combo density and short recoveries only**. No wind-up in the set is below
`0.45 s` — the contract above is not negotiable for a mini-boss, and a faster tell would need its cue to
fire before the wind-up began.

### The Ember Revenant — the burning prototype

`Legendary_Revenant`. **A prototype and a sandbox exhibit, not a campaign enemy**, exactly like the
Marionette: it has a pad at x − 28, a wake switch, and it is in no `LevelDefinition` and no
`LevelRegistry`. The body is the first export from **`ai_skelly_tool`** — a tall, lanky blade-bearer —
and it went through the existing forge pipeline with no bespoke code at all: FBX + `clips.json` →
`4a. Split Forge Animation Clips` → a `ModelSpec` → `4b. Build Mini-Bosses`.

It exists to answer two questions:

**1. Can an enemy be lit from inside without wrecking the readability language?**
Yes, but only by asking rather than writing. Emission on an enemy is *already spoken for* —
`EnemyVisuals` reserves it so that light means "you deflected", never "an attack is happening". So
**`EmberAura`** never touches a property block; it calls `EnemyVisuals.SetAura`, and the existing single
writer folds a dim constant floor (0.22) in under the parry spike (3.2). Crucially the floor runs through
the same `chargeDark` as everything else, so **a body on fire still visibly inhales on a wind-up** — the
fire is drawn in as it charges and floods back on the strike. The aura reinforces the telegraph rather
than washing it out. Intensity rides `Posture.Ratio`, so it stokes hotter as the break approaches, which
is the language the eye already speaks on every other enemy. On top of that: embers shed from the body
into a scene-level root (so it moves out from under its own fire rather than towing it) and one weak
point light. See ENGINEERING-LOG.md.

**2. Does a second animated forge model drop in without bespoke code?**
It does — but only because two measurement tools now exist. This rig's **skeleton spans y 0.96 to 1.20
while its mesh reaches 1.96**: the top 0.76 m is shoulder spikes and hood with no bones in it. Every pivot
inferred from the bounding box would have floated in mid-air. `VibeGame1/Probe Forge Models` reads the
bones; `VibeGame1/Photograph Enemies` checks the result on screen.

**The fight is a READ, not a cadence — which is the whole point of having both prototypes.** Where the
Marionette is a metronome you hold at the parry contract's floor, this is slow, enormous and committed,
with real openings. It is the gentler of the two on purpose, and a test enforces that every one of its
wind-ups is slower than a Marionette spin pass.

| | `windup` | `impactDelay` | `strike` | `recovery` | cone | dmg | parry × |
|---|---|---|---|---|---|---|---|
| `Revenant_Slash` | 0.62 | 0.05 | 0.16 | 0.55 | 95° | 20 | 1.3 |
| `Revenant_Stab` | 0.55 | 0.04 | 0.12 | 0.50 | **40°** | 24 | 1.45 |
| `Revenant_Overhead` | 0.95 | 0.07 | 0.22 | **1.40** | 70° | 38 | 1.8 |
| `Revenant_Kick` (unblockable) | 0.70 | 0.05 | 0.18 | 0.90 | 55° | 18 | — |

`Revenant_Overhead`'s 1.40 s recovery is **the biggest punish window in the game** — this is where a new
player learns that a whiffed heavy is free damage. `Revenant_Stab` is chosen against what the ART
actually does rather than its name: sampling hand separation across every clip, `AttackStab` is the only
one that CLOSES (0.81 → 0.19 → 0.24 m), so it is the thrust, and its 40° cone matches. `Revenant_Kick`
is the anti-turtle: the one unblockable, so simply holding guard is never a complete answer.

240 HP against 160 posture, `aggression 0.38`: tankier and far less pushy than the Marionette's 170/210
at 0.62. It does **not** hold the Marionette's beat identity, and that is deliberate — a visible stumble
after a deflect is the reward here, not a metronome that must not drift.

### The Flurry Brawler — the volume prototype (2026-09-07)

`Legendary_FlurryBrawler`. **A prototype and a sandbox exhibit**, like the other three: a pad at
x −22 / z −26 (the west end of the second row), a wake switch (`FLURRY BRAWLER`), and in no
`LevelDefinition` and no `LevelRegistry`. The fourth forge body (`Assets/Enemies/FlurryBrawler.fbx`,
24 clips), and the first UNARMED one.

**Its job in one sentence:** an unarmed brawler that fights in VOLUME — four-beat strings thrown at a
0.73 s beat, whose only openings are the breath between phrases and the end of the flurry.

**It does not duplicate a roster job.** The Marionette is a metronome you HOLD, the Halberdier answers
DISTANCE, the Revenant is a body you READ; this one teaches *stay on the beat through a string, and do
not swing inside it*. Its beat is deliberately **above** the Marionette's 0.69 s floor: 0.732 s cold and
0.72 s once a deflect streak has pushed the combo gap onto its 0.10 s floor, so the fastest thing this
enemy can ever throw still clears the parry contract, and the fastest cadence in the game stays the
Marionette's.

**The economy.** With the sword (`parryPostureDamage 25`) a deflected jab or cross is 28.75 and a
deflected FLURRY is 50, so the signature string `jab, cross, jab, FLURRY` is 136.25 of a **160** posture
bar: hold a whole phrase and one more clean beat breaks it, and nothing less does. 190 HP is low for a
duellist on purpose — a player who cannot hold the rhythm can block (worth zero posture, so it never
breaks it) and cash the flurry's 0.86 s effective recovery for damage instead. The unblockable **kick**
is the price of turtling; the unblockable **shoulder charge** (3.90 m of the clip's own measured travel,
gated to ≥ 3.6 m) is the price of backing off.

**Eight attacks, eight clips, every one named on the attack** (`EnemyAttackData.clip`) — the Halberdier's
rule, because a generated clip is unreachable through the pipeline's canonical mapping. Two of those
clips carry more punches than blows (`Burst2`, `Burst4`): the extra punches are anticipation and
follow-through, and the parry rides the cue exactly as it does on the Marionette's whirl.

**Its travel is measured, never read off the sidecar.** `FlurryBrawler.clips.json` records SOURCE
motion, and on this body the export factor is not the Halberdier's uniform ~1.3× — the attack and step
clips measure 1.18–1.24× OVER the sidecar while Walk and Run measure 0.90× UNDER it. Every
`lungeDistance` is the Hips travel measured on the imported clip (`Tools/measure_forge_fbx.py`) and
`FlurryBrawlerDataTests.EveryLungeIsTheClipsOwnTravel` holds the two together. `Burst4`'s 0.21 m of
travel is LATERAL and ships as a lunge of **0**: `PuppetVisuals.CompensateTravel` cancels the pose's XZ
so the mesh never leaves the capsule that gets hit.

**No blade trail.** `EnemyWeaponTrail` sweeps a strip from the `RightHand` bone to `weaponFxPos`, which
is right for an axe head that really sits there and wrong twice over on a brawler: the marker is a fist,
and `Jab2`, `Uppercut` and `Burst4` are all anchored on the LEFT wrist. A two-handed trail would be a
change to `EnemyWeaponTrail` and is a lead call, not something an enemy smuggles in.

### The Argent Halberdier — the reach prototype

`Legendary_Halberdier`. **A prototype and a sandbox exhibit**, like the other two: a pad at x 0.75
(between the Heavy pad and the Warden's), a wake switch, and in no `LevelDefinition` and no
`LevelRegistry`. The body is the third `ai_skelly_tool` export — a towering armoured halberdier with a
tail, the first forge model to ship a painted albedo — and the first whose attacks are **generated,
per-character clips** (`forge.py --motion`) — nine generated, of which only four turned out to be
attacks (below).

It exists to answer one question, which turned into two:

**1. Does a forge model whose animation is its OWN drop in without the pipeline learning its name?**
Not as it stood. `PuppetVisuals.ClipFor` mapped attacks onto the forge's four canonical clips (swing,
heavy, `_Stab`, `_Kick`) and the Marionette-era reasoning — "the forge exports the same four clips for
every model, so the mapping is a property of the pipeline" — was true of authored clips and false of
generated ones. A `HalberdSweep` has no canonical name to map to. So the name moved onto the content:
`EnemyAttackData.clip`, resolved before every heuristic, against a table `4b` bakes onto the prefab from
every manifest clip carrying `OnAttackHit`, each with its own length and contact frame. The clip still
bends to the attack's clock; only the choice of clip is data now.

**2. What happens to root motion?**
The tool bakes a thrust's or a charge's pelvis path onto the Hips bone and, in its own Unity contract,
applies it through a component that moves the agent. This project does not let a clip own a transform:
the NavMeshAgent moves the enemy and an attack's reach is `range + lungeDistance`, run from the cue to
the impact. Unity's own root-node extraction turned out to be the wrong tool on a Generic rig (it moves
the Hips' whole transform — the leap's lift and the body turn included — onto the model root, and a bad
node path imports zero clips), so the travel stays IN the clip and `PuppetVisuals.CompensateTravel`
cancels its XZ on a dedicated `TravelRoot` every frame — the mesh stays over its collider instead of
running a metre ahead and snapping back — and the distance the art travelled ships as `lungeDistance`.
A test samples the clip's Hips travel and holds the data to it, and another holds the drift under 5 cm.
One trap on the way: the sidecar's `forward_m` is the *source* motion, scaled about ×1.3 onto this rig
at export; the clip is the truth, the sidecar is not. See ENGINEERING-LOG.md.

**3. Do generated strike clips strike? (2026-09-04, from play: "the animations don't line up with the
attack hitboxes.")** No. Sampling the halberd's tip through every clip: the four authored strikes whip
it at 36–86 m/s with the blow exactly on the manifest's contact frame; the generated "strikes"
(HalberdSweep, HalberdBackswing, Thrust, OverheadSlam, HeavyWindup) move it at 1–8 m/s, two of them
ending with the blade behind the body. Only Kick, LeapSlam, SpinSweep and ShoulderCharge have real
action. So the cuts now play the AUTHORED AttackSwing / AttackStab / AttackOverhead, the backswing and
the heavy are gone rather than doubled onto a shared clip (seven attacks, seven animations), and every
range came down to the blade: 2.7–2.9 m for the cuts plus a 1.0–1.3 m step from the cue, the commit band
at 3.2 + 0.4 m. `MiniBossFactory` now measures every generated clip's tip at `4b` and says "NO STRIKE"
for one that should not be an attack.

**The fight is REACH THROUGH THE CHARGE — the third lesson, after the Marionette's cadence and the
Revenant's read.** He fights at the blade (3.2 m) and is the fastest chaser in the roster, and what he
does with distance is the lesson: from 5 m to the aggro edge the shoulder charge is near-certain — the
longest lunge in the game, 4.7 m of the clip's own travel — and it opens straight into the string
(sweep, thrust, SLAM is the signature: wide, narrow, then the punish). Two unblockables, one for each way
of refusing the fight — the kick for a player who gets inside the halberd and holds guard, the charge for
one who backs out — a leap slam that visibly leaves the ground, an all-round spin for a player circling
behind, and the slam's recovery as the punish window. Firing an attack from OUTSIDE the commit band is
new in `EnemyController` (the far-band commit: only when the moveset has an entry whose band contains
the distance). No wind-up pose is authored, on purpose: each attack is a different animation, so the clip
is the silhouette, and a pose gets written only if a photograph shows two of them aliasing.

The pivots and the lunges were all measured — in Blender, because the editor's bridge was down that
day (`Tools/measure_forge_fbx.py`). The forge places every bone on the drawing's z = 0 plane while this
body's mass sits a quarter-metre ahead of it, hence `ModelSpec.zShift`; the tail is the only thing
behind that plane, which is how the facing was settled.

**Played once (2026-09-04) and retuned the same evening.** The first cut held at 4 m and threw the
charge from 5.5–9.5 m at the same weight as a sweep; in play he stood off, and a player who backed
away got a leap or nothing. Now the charge is *the* answer to distance — three entries from 5 m to the
aggro edge weighing 11 against the leap's 1.2, two of them chaining straight into the sweep pair or the
thrust — and inside 6 m the default pick is a three-hit string (sweep, backswing, thrust is the
signature). `aggression` 0.85: `EnemyController` plays recoveries at ×0.45, the cooldown at ×0.4 and the
gaps at ×0.53, and keeps swinging through a deflect. **No tell got shorter**: every wind-up is still ≥
0.45 s and the cue still leads by 0.28 s; the speed is density. Because the same scaling shrinks every
opening, the raw recoveries were rewritten for the aggressive enemy — the heavy at 2.4 s raw is a 1.08 s
opening in play, and `HalberdierBehaviourTests` holds the *effective* numbers, not the raw ones.

**The blade trail.** `EnemyWeaponTrail` (any `ModelSpec` with `bladeTrail`): a 14-sample strip of quads
between the weapon hand and the axe head, drawn only while the attack clip is inside its contact window
(`hit − 0.22 … hit + 0.12` of the clip, keyed to the same contact frame the blow is timed to), four
sparks on the contact frame, one owned additive material normalised to a peak channel of 1.0 — under
the 1.05 bloom threshold, so a swing has motion and shape but never the light a deflect owns. No particle
system, no per-frame allocation, disabled entirely between swings.

### The Pale Marionette — the animated prototype

`Legendary_Marionette`. **A prototype and a sandbox exhibit, not a campaign enemy.** It has a pad, a
wake switch and `SandboxController.SpawnEnemyInFront(6)`; it is in no `LevelDefinition` and no
`LevelRegistry`. The four-tile course's three gate keepers are designed and tested and a fourth was not
what was asked for.

It exists to answer two questions the project had not answered:

**1. Can an enemy spin genuinely fast without breaking the 0.45 s wind-up floor?**
Yes, because *visual spin rate and hit cadence are different quantities*. The body turns at a
**CONSTANT 2087 °/s — 5.8 revolutions a second** — and never changes speed: not into an impact, not
during the strike, not in the gap between passes. The damaging passes arrive every **0.69 s** from a
**0.45 s** wind-up. Nothing about the spin touches timing — `PuppetVisuals` writes one local yaw on a
dedicated `SpinRoot` transform and nothing else.

**It used to ease, and that was wrong.** The first version travelled each revolution on a curve starting
at 4.5× the average rate and decaying to 0.25×, so that decelerating into the player could serve as the
wind-up tell. Every derivation about that curve was correct and it passed a suite of tests measuring its
endpoints, monotonicity and exactness. Played, it read as a **pulse** — blur, slow, blur, slow, once a
beat — which looks like a stuttering animation, not a spinning body. *No test caught it, because every
test asked whether the curve was the curve it was meant to be and none asked whether there should be a
curve at all.* There were two pulse sources, and the second was worse: between passes the body fell to a
55 °/s "idle drift", so it visibly sagged and re-spooled in every one of the 0.20 s gaps.

**With a constant rate there is no positional tell left, and that is the point.** The parry rides
entirely on the cue flash and its audio at `cueLead`, so the fight demands precise timing against a
signal rather than pattern-matching against a slowdown.

**2087 is derived, not chosen by feel.** 2087 × 0.69 s = 1440° = exactly **four revolutions per beat**,
so an impact leaves the body square-on, the gap turns it by a whole-number remainder, and the next pass
covers a whole number of revolutions from there — all at one unchanging speed, forever, with a
correction of zero. That self-consistency is what
`MarionetteDataTests.TheSpinIsCONSTANT_AWholeNumberOfRevolutionsPerBeat` and
`PuppetSpinTests.ThePhaseIsSelfConsistent_SoNoPassAfterTheFirstNeedsCorrecting` protect: retune the
beat without retuning the rate and every pass silently starts needing a speed correction, which is the
pulse returning by arithmetic instead of by a curve.

**The wind-up is exactly ON the floor, and that is the ceiling.** 0.69 s is the fastest parry cadence
this game can legally ask for, which is arithmetic rather than taste: the cue fires `cueLead` (0.28 s)
before impact and impact is `windup + impactDelay`, so a wind-up under ~0.24 s would need its cue to
fire before the wind-up began and the pass would stop being parryable at all. The remaining lever is
`parryPerfectWindow` in `PlayerStatsData`, which is **global to every enemy in the game**. If a future
session wants the passes closer together, that is the conversation to have — not this number.
`MarionetteDataTests.TheBeatSitsExactlyOnTheParryContractsFloor` is deliberately brittle about it.

**The alias guard.** `PuppetVisuals.ResolveRate` clamps the constant rate against the *measured* frame
time so the body never steps more than 75° per rendered frame: past roughly 90° a frame a
2-fold-symmetric silhouette stops reading as rotation and becomes apparent random orientation. At 60 fps
the step is **34.8°** and the guard is slack; it only engages below ~28 fps, where it slows the spin
rather than letting it strobe. It never clamps the *phase* — that would let the body arrive late and
break the one invariant the whirl has.

**Alignment is bought with the ARC, never with the rate.** When an impact's interval is not a whole
number of revolutions — the spin-out, which arrives 0.21 s late by design — `BeginPass` nudges the arc
within `maxRateCorrection` (12%). If no whole-revolution arc fits inside that band, **the constant rate
wins and the body simply arrives at a different yaw**: a visible speed change is a worse defect than a
body that is a few tens of degrees off at the blow, because the spin is the whole read.

Measured from the rendered frames (`SpinFilm`, 31 frames at 60 fps): **34.8° every single frame, speed
variation 0.0**, 4.000 revolutions per beat.

**The cadence cannot drift**, which is the other half of making a rhythm learnable. The whirl's phase
is not integrated forward between passes; it is re-derived every beat from where the body actually is
and the data's own time-to-impact, so a dropped frame, a hitstop or a deflect cannot accumulate. And
the beat itself is equal whether you deflect or not: an unparried pass is
`windup 0.45 + gap 0.10 + impactDelay 0.04 + strike 0.10 = 0.69 s`, and a parried one is
`recoil + 0.45 + 0.10 + 0.04`. Which reduces to one identity every future retune has to preserve:

> `parryRecoilSeconds  ==  strikeDuration / lerp(1, 0.55, aggression)`

At aggression 0.62 the multiplier is 0.721, so `parryRecoilSeconds` is **0.1387** and the recoil is
0.1000 s — the strike's duration exactly, and a 0.690 s parried beat. That number is *derived*, not
felt; changing `strikeDuration` **or** `aggression` means re-running the division. (Residual: a perfect
parry may land up to half the perfect window early, so the next beat can be pulled in by ≤ 0.065 s.
Bounded and player-caused.)

**The gap is pinned, so the beat cannot shorten as you get better.** `NextGap` is
`max(0.10, comboGap × lerp(1, 0.45, aggression) − parryStreak × 0.03)`, and `0.12 × 0.721 = 0.087` is
already under the floor — so the floor is what binds, and neither aggression nor a growing deflect
streak can compress it. Pass 9 of a nine-pass phrase arrives on exactly the interval pass 1 did.

**The economy: six clean deflects break it, and the spin breaks EARLY.** With the sword a deflected
pass is `parryPostureDamage 25 × parryPostureMultiplier 1.4 = 35`, so 6 × 35 = its whole 210 bar. The
signature phrase is *nine* passes long, so a clean player breaks it three passes before it would have
ended on its own and a sloppy one has to survive the whole thing — **the player's rhythm decides how
long the spin lasts, not a script.** (Nine, up from eight, because the faster beat would otherwise have
made the phrase *shorter*: 9 × 0.69 s gives a 7.95 s phrase against the old 7.94 s, so the change reads
as a denser rhythm rather than a shorter fight. The brief was more parries, not the same fight over
quicker. The short spin went 4 → 5 passes, which keeps it just under the six needed to break — so the
short spin is the one that can never be broken through.) That is the choice over "run N revolutions then self-recover",
which would make skill irrelevant to the outcome; in the reference fight deflecting *is* the offence.
It uses the ordinary posture system, with no special case anywhere.

**Backing off is not the answer, and neither is parrying forever.** Three lines out:
`Marionette_Lash` is a wide unblockable to 8 m gated to the **far band only**, so retreating past its
reach is exactly what selects it (the same job `Knight_Vent` does); `Marionette_SpinOut` ends every
phrase with **2.0 s** of recovery, so a player who cannot hold the rhythm can block the passes — block
is worth *zero* enemy posture, so it does not progress the break — and cash the exit for damage
instead; and its **170 HP**, low for a duellist, makes that second route real. `Marionette_Overhead`
is a 1.0 s tempo break, and it is the one attack that is **not** whirled: the body stops and squares up
under it, so the break from the rhythm is visible in the silhouette and not only in the clip.

| | `windup` | `impactDelay` | `strike` | `recovery` | cone | dmg | parry × |
|---|---|---|---|---|---|---|---|
| `Marionette_SpinUp` | 0.80 | 0.04 | 0.10 | 0.20 | 200° | 18 | 1.4 |
| `Marionette_SpinPass` | **0.45** | 0.04 | 0.10 | 0.20 | 200° | 17 | 1.4 |
| `Marionette_SpinOut` | 0.65 | 0.05 | 0.18 | **2.00** | 200° | 26 | 1.6 |
| `Marionette_Overhead` | 1.00 | 0.07 | 0.24 | 1.00 | 65° | 40 | 1.9 |
| `Marionette_Lash` (unblockable) | 1.05 | 0.08 | 0.30 | 1.50 | 175° | 30 | — |

Two intervals in that table are deliberately *not* the cadence. `Marionette_SpinUp` carries a longer
wind-up (it is the warning) but its `impactDelay` and `strikeDuration` match the pass's exactly, so the
spool-up hands off after 0.69 s and **the player is on the metronome from beat one** rather than having
to find it on beat two. `Marionette_SpinOut` does the opposite on purpose: it arrives **0.21 s late**,
outside `parryPerfectWindow` (0.13) but comfortably inside the block window (0.25). So a player parrying
the *count* rather than the *body* blocks the exit instead of deflecting it — it costs them the posture
and the punish window, and it costs them no health. That is the fight's thesis charged at exactly the
right price, and both halves are pinned by
`MarionetteDataTests.TheExitBreaksTheMetronome_ButOnlyIntoABlock`.

**2. How should a rigged, clip-carrying model be animated?**
With an `Animator`, through **`PuppetVisuals : EnemyVisuals`**. Three decisions, in order of how much
they matter:

- **A subclass, not a replacement.** `EnemyVisuals` documents itself as the thing to subclass when real
  models arrive and `EnemyController` resolves `IEnemyPresentation`, so the brain never learns about
  any of this. More importantly it keeps the SHARED readability language: the base's base-colour
  sink-and-snap, the cue flash, the alert marker, the posture-driven eye and the deathblow glyph are
  the vocabulary every other enemy speaks, and a player must not have to learn a second one for this
  body. Every override calls `base` first and adds the clip and the whirl on top.
- **The controller is generated, and it is a clip LIBRARY with no transitions.**
  `Editor/PuppetAnimatorFactory.cs` builds `Assets/Animation/Legendary_Marionette_Animator.controller`
  from whatever clips the FBX actually contains — hard rule 4, and it also means the controller cannot
  end up pointing at stale clip sub-assets after a re-split. One state per clip, no authored
  transitions, driven by `Animator.CrossFadeInFixedTime`. A state machine with exit-time conditions
  would put a *second timing authority* in the project, and the first time a transition disagreed with
  a wind-up the attack would stop being parryable.
- **The clip bends to the data.** `Animator.speed` is scaled so the clip's own contact frame (the
  manifest's `OnAttackHit`, baked onto the prefab at build time) lands on the impact `EnemyAttackData`
  specifies — 1.50× for a spin pass. If a clip is the wrong length, the clip loses.

Hitstop: the Animator runs in `Normal` update mode and the whirl runs on `Time.time` / `Time.deltaTime`,
so the puppet **freezes with the world** on impact. Rule 1 reserves `PlayerDelta` for what the *player*
drives; an enemy that kept dancing through a freeze frame would kill the impact read.

Three transforms, three owners, deliberately never two writers on one channel:
`LungeRoot` (the base class's lean and lunge) → `SpinRoot` (the whirl) → `Model` (the Animator).

### Enemy posture bars

`Enemies/EnemyPostureBar.cs`, on **Grunt and Heavy only** — the boss uses the HUD bar. Two quads driven
by `MaterialPropertyBlock`, deliberately **not** a world-space Canvas: cheaper (no extra batch per enemy)
and it avoids the whole null-sprite `Image` class of bug.

Yaw-only billboard so bars stay upright, hidden below `hideBelowRatio` (0.02) so idle enemies are not
cluttered, colour ramps `#C9A227` → `#FF3A1A` as posture fills, flashes white on break and pulses while
staggered — that pulse is the "execute me now" tell. Reads `Posture.Ratio` in `LateUpdate` rather than
trusting event ordering against `Posture.Configure()`.

### Swapping enemy models

Yes, the primitives can be replaced with real models — the seam already exists. **`EnemyVisuals` is the
only class permitted to know about meshes, renderers or animation.** `EnemyController` contains zero
`Renderer` / `Material` / `Animator` references; it only calls presentation methods. Every one of those
methods is `virtual`, and the controller resolves its reference with
`GetComponentInChildren<EnemyVisuals>()` — so **a subclass is picked up automatically and no gameplay
code changes.**

The path is: subclass `EnemyVisuals`, override the presentation methods (`Setup`, `SetAccent`,
`SetPostureRatio`, `Telegraph`, `CueFlash`, `Strike`, `ClearTelegraph`, `Recoil`, `HitFlash`, `Slump`,
`Settle`, `Roar`, `Die`), and put the component on the enemy prefab in place of the primitive one. Keep
the primitive implementation as the fallback and as the reference for what the timing should look like.

Three rules must survive the swap. Breaking any of them breaks the parry, which is the whole game:

1. **Timing is data-driven, never animation-driven.** `Telegraph(atk, seconds)` is handed its duration and
   the visual must fit it. The cue is scheduled `cueLead` (0.28 s) before impact from the attack *data*.
   The moment a clip's length dictates the wind-up, the guarantee that every attack is reactable is gone.
2. **The cue is the loudest event.** `CueFlash` must stay instant, high-contrast and unmistakable, landing
   in a single frame. It is the "press parry now" signal — never eased, blended or out-shouted.
3. **Physics comes from the prefab root, never the model.** Colliders, `NavMeshAgent` radius/height and
   `EnemyData.scale` belong to the root. Parent imported art under the existing `Visual` child so the
   root's footprint is unchanged, or navigation and the distance/cone impact test shift with the art.

`Awake` is `protected virtual` and `StartMotion` is `protected` specifically to make subclassing safe — a
private Unity message would be *hidden* by a subclass's own `Awake`, silently skipping base
initialisation, and two coroutines writing the same transform is exactly the bug that made the cue hitch
invisible (see [ENGINEERING-LOG](ENGINEERING-LOG.md)).

---

## Items

Neon White style: found in the level, carried in a small slot row, consumed on use, restored on respawn.
`Player/PlayerItems.cs` holds them; `Level/ItemPickup.cs` is the world object; `UI/ItemSlotView.cs` is the
HUD slot.

- **Capacity 3, FIFO** — the leftmost HUD slot is the one that fires. Key `E` (`UseItem`).
- Pickup is a real `OnTriggerEnter` on layer `Interactable`. Collection disables the collider and
  renderers; `GameEvents.PlayerRespawned` restores them, so a run always starts from the same state.
- Colour comes from `ItemData.color` (HDR) via `MaterialPropertyBlock`, so one prefab serves every item.

There are exactly **two** items and both are MOVES — the level is built around them, Neon White
style (kill to move, wall to move). Nothing in the slot heals or protects.

| Item | Effect | Behaviour |
|---|---|---|
| **Grapple** (HOOK, cyan) | `Grapple` | Hooks the lock-on target, else the enemy nearest the crosshair (28 m, 12°, world line of sight). `FirstPersonMotor.BeginPull` flies the player to `ExecuteInteractor.stabStandoff × scale` in 0.35 s; on arrival a normal enemy is posture-broken and deathblown through `ExecuteInteractor.ExecuteNow` (the ONE execute path, so `RiposteLanded` fires). A `Legendary_*` / boss that is staggered dies the same way; one that is not takes 35% of its max posture and you land at stand-off. No target → "NO TARGET", **not consumed**. |
| **Wall Surge** (SURGE, yellow) | `WallSurge` | `FirstPersonMotor.StartWallSurge(8)`: motor STATE, not a tuning write. While `IsWallSurging`, `WallRunSettings` scales top speed and accel ×1.5 and zeroes `minEntrySpeed` (any airborne touch attaches), and both stamina calls in `TryWallRun` / `AdvanceWallRun` are skipped. HUD prompt counts down "SURGE 8s". |

### Refusal keeps the item

`PlayerItems.Apply` runs **before** the item leaves the slot and returns `false` to refuse. A Grapple
with nothing in range costs nothing and says "NO TARGET"; there is no second spend path.

---

## Wands (riposte weapons)

Bloodborne firearms are the reference. The riposte — the critical attack after a posture break — is no
longer a melee stab: the player commits, **draws a magic wand and blasts**. Wands are a swapped loadout,
not a freely-fired weapon. `Data/WandData.cs` is the asset, `Player/WandController.cs` the behaviour.

```
ExecuteInteractor.ExecuteCo
  melee commit (wandCommit 0.18s)          ← the lunge that sells the hit
  └→ WandController.FireRiposte(target, weapon.executeDamage)
       draw + charge   (wand.windup)
       discharge       → RiposteLanded raised, THEN damage applied
       settle          (wand.recover)
```

`WandKind` decides who the blast reaches:

| Kind | Reach |
|---|---|
| `Bolt` | Single target |
| `Scatter` | Ripostee + splash to everything inside `blastRadius` |
| `Chain` | Arcs from the ripostee to `chainTargets` further enemies |
| `Lance` | Pierces a line from the player through the ripostee; `blastRadius` is the **length** |

| Wand | Kind | Damage | Splash | Reach | Windup | Recover |
|---|---|---:|---:|---:|---:|---:|
| **Emberlance** | Bolt | 220 | – | – | 0.20 | 0.22 |
| **Gravecall** | Scatter | 120 | 90 | r 7 | 0.38 | 0.34 |
| **Stormneedle** | Chain ×3 | 150 | 70 | r 9 | 0.28 | 0.26 |
| **Voidspine** | Lance | 260 | 110 | len 14 | 0.46 | 0.40 |

Cadence is the real differentiator — a full riposte runs ~0.60 s (Emberlance) to ~1.04 s (Voidspine).
`damage` is added on top of the equipped weapon's `executeDamage`.

Two rules:

- **Only the ripostee takes `isExecute` damage.** Splash, chain and lance damage on every other enemy is
  ordinary damage. This is what stops an AoE deathblowing a bystander boss — `Health.deathIsStagger`
  means a boss segment may only be removed by an explicit riposte.
- **`RiposteLanded` is raised before the damage**, so listeners can still read the victim.
  `WandController` raises it at the discharge; `ExecuteInteractor` raises it only in the no-wand melee
  fallback (which is also the Grapple's path). Exactly one of the two fires per riposte.

Chosen at the **wand pedestal** at the level's spawn point: aim at the altar and press `F`
(`WandPedestal` → `WandSelectMenu` → `WandController.Equip`) — see the Wand pedestal map in `DATAFLOW.md`.
The altar is a **dev fixture**, hidden and inert until `WandPedestal.DevMenuEnabled` is switched on from the
F1 test menu; the default loadout (all four wands, Emberlance equipped) is what the player runs with.
Also cycled with **`R`** (`WandCycle`) as a debug convenience; both raise `GameEvents.WandChanged` for the HUD
label. With no wand equipped the original melee deathblow runs unchanged, so the system degrades safely.

Assets live in `Assets/Data/Wands/`, built by `VibeGame1/3b. Create Wands` — which **must run before
`4. Build Prefabs`**, because the prefab step assigns the viewmodels back onto the wand assets.

---

## Data-driven tuning

All balance lives in ScriptableObjects under `Assets/Data/`. Edit in the Inspector to iterate.

| Type | Assets |
|---|---|
| `WeaponData` | Sword, Hammer, Dagger, DevBlade |
| `WandData` | Emberlance, Gravecall, Stormneedle, Voidspine (`Assets/Data/Wands/`) |
| `EnemyData` / `BossData` | Grunt, Heavy, Boss |
| `EnemyAttackData` | 11 attacks (Grunt_Jab/Slash/Heavy, Heavy_Step/Sweep/Overhead, Boss_×5) |
| `ItemData` | Grapple, WallSurge |
| `PlayerStatsData` | `PlayerStats.asset` — parry windows, posture, flask, Pyre, super slow-mo |
| `UpgradeTable` | Souls costs |
| `GameFeelSettings` | Hitstop, shake, flash, FOV kick |

⚠️ **`VibeGame1/3. Create Data` overwrites these.** Values you want to keep must go back into
`Assets/Editor/DataFactory.cs`.

---

## Presentation

### Art direction — dark fantasy

Cold sky under **the Eclipse**: an enormous dead sun (38° across, 22° up, over the boss arena) — near-black disc, white-hot rim, the sky around it on fire — over a world drowned in blue (the 2026-09-06 cold-palette pass, user-directed: "change the main colour scheme to blue"). Deep-blue fog, a cold Trilight ambient at the *same luminance* as the warm set it replaced, a pale cold key light, and four separable trim hues — ice cyan / brass gold / azure / ghost green — one per level tile. Warm is reserved for exactly two things now: a combat tell (`M_AlertTell`, the bolt, the cue spark) and fire/safety (`M_Torch`, `M_Checkpoint` — the Dark Souls bonfire read); everything else in the world is cold, so any warm pixel reads as "pay attention" by construction. The sky is `Starfield` geometry, never a skybox — **two submeshes**: an LDR field that can never bloom, and the corona rim alone on an HDR tint. Its shipped geometry is `LevelDefinition.sky` for the shipped level and `Starfield.DefaultEclipse*` for the legacy greybox — keep them equal. Ambient table: sky `#344C78 × 1.35`, equator `#3F5E88 × 1.35`, ground `#0E1326 × 1.35`, key `#5A79AD` @ 1.05 (`Editor/ProjectSetup.AmbientSky` / `.AmbientEquator` / `.AmbientGround` / `.KeyLightColor`, applied in `ProjectSetup.SetupSceneEnvironment` and mirrored — read from the same constants, not hand-copied — in `SandboxBuilder.EnsureEnvironment`). Fog (rebuilt by A5, 2026-09-06) is `#0E1C34` (`ProjectSetup.FogColor`) — the dome's own horizon band `#13233F` at 0.7 value, lin lum .0117 — ramping **36 → 140 m** (`ProjectSetup.FogStartDistance` / `.FogEndDistance`), not the void colour `#060D18` over 45 → 240 that it replaced. Fog is the sky bleeding in front of distance, so it must sit between the dome's zenith and its horizon and above a shadowed stone face; below that it is extinction and it eats the deck you are about to land on. Start distance has a hard floor of ~31 m (the eclipse halo's corners), not the quoted dome radius of 25. Pinned by `SkyEclipseTests`. Visible route atmosphere comes from one bounded `AmbientMist` ParticleSystem on the player root: world-space cold additive sheets with camera fade, no gameplay or collision, built into the Player prefab and pinned by `AmbientMistTests`.

Structural albedos, trim hues and combat-tell colours all live in `Editor/MaterialFactory.Table`; ambient and the key light live in `Editor/ProjectSetup.SetupSceneEnvironment`; enemy body albedos live in `Editor/DataFactory`; the HUD's own colour constants live in `Editor/HudBuilder.cs`. None of these are on a `.mat` or in the Inspector (rules 4 and 9) — change the constant, then re-run the generator that consumes it (`2. Create Materials`, `1. Project Setup` / `7. Build Sandbox`, or `3. Create Data` respectively).

**The HUD is panes of smoked glass, edge-lit by the eclipse (2026-09-04).** UGUI has no blur, so glass
is built from four cheap layers — a dark rounded 9-slice at a linear-space alpha of 0.58, a soft shadow
under it so the pane sits *off* the frame, a faint sheen across its upper third, and a one-pixel light
along the top edge that warms from ember at the left to bone — all generated as tiny PNGs by
`Editor/UiSprites.cs` on every `5. Build HUD`. Three panes, three shapes: the vitals (a wide low pane,
bottom-left, one left edge and one bar width for health / stamina / posture / Pyre, numerals and pips in a
column to the right), the loadout (a narrow pane, top-left), and the clock (a pill). Item slots are glass
tiles; every menu is a scrim with a glass card on it; every button is a glass pill. Type is one family on
one scale, values in bone (`#E8E2D6`), labels quieter than values, offsets on an 8 px rhythm. **The UI
never blooms**: no graphic on the canvas exceeds 1.0 in any channel (`HudGlassTests`), because light on
screen means "you deflected". BEST RUNS is a fourth pane, top-right and hidden until a board exists; the
key-bind reference is not on the playing HUD at all — `ControlsInfo` feeds the settings INFO card and the F1
menu. Weapon name in bone; teal is left to the wand alone so the accent means one
thing. The fluid bars and the Pyre fire are styling passes applied afterwards through
`Editor/HudExtensions.cs`, which find `HealthBar` / `StaminaBar` / `PyreBar` by name.
**Material names are historical and no longer describe their colour** — they are kept stable because the
builders reference them by name:

**Lighting and structural albedo — shipped values.** Ambient and the key light live in
`Editor/ProjectSetup.SetupSceneEnvironment` (mirrored in `Editor/SandboxBuilder.EnsureEnvironment`), the
structural albedos in `Editor/MaterialFactory.Table`, and the enemy body albedos in
`Editor/DataFactory` — never in the Inspector or on a `.mat` (rules 4 and 9).

| Knob | Shipped | Reasoning |
|---|---|---|
| ambient mode | `Trilight` | three-way by surface normal, costs nothing |
| ambient sky | `#344C78 × 1.35` | cold starlight, lifts platform **tops** — the surfaces you land on |
| ambient equator | `#3F5E88 × 1.35` | cold eclipse light — lifts every **vertical** face and every enemy |
| ambient ground | `#0E1326 × 1.35` | near-black indigo bounce; undersides stay heavy so shapes keep weight |
| `ambientIntensity` | **1.0 — it is a no-op here** | Unity applies it to *Skybox* ambient only; in Trilight the multiplier must live in the colours |
| key directional | `#5A79AD`, **1.05**, Euler `(10, 180, 0)` | one dying, pale-cold sun low behind the arena, backlighting the course |
| `M_Ground` | `#1B222E` | ~0.0157 linear — most of the structural surface area |
| `M_Stone` | `#2A3443` | ~0.0334 linear — walls, pillars, obelisks; above ground so a wall separates from the floor |
| `M_Platform` | `#475262` | ~0.0821 linear — ash top, footing legibility, never trim |
| `M_Enemy` | `#1A1E29` | material default only — **the body albedo comes from `EnemyData.bodyColor`**; smoothness **0.34** (see below) |

**Smoothness is per-material, and it is the only thing that gives an enemy shape.** Everything else in this palette is matte by design, and `MaterialFactory.Configure` used to force smoothness 0 plus `_SPECULARHIGHLIGHTS_OFF` on *every* material. The course is backlit, so an enemy facing the player gets no key light — with only a diffuse term its whole torso renders as one flat value and no amount of extra ambient can carve it. Specs now carry their own `smoothness` (default 0, so the neon shapes are untouched) and `M_Enemy` ships at 0.34: enough for the ambient sky term to skim a shoulder, not enough to look wet. Deliberately **not** emission — *enemies do not glow*, because light on an enemy means you deflected.

| `EnemyData.bodyColor` | grunt `#3A3340` · heavy `#423630` · boss `#40304C` · ninja `#2E363C` · knight `#443A34` · spellsword `#3A3050` | ~8–10/255 on screen against a ~36/255 floor |
| vignette / film grain | 0.27 / 0.26 | still a vignette, no longer a footing tax |

**The equator term is the ambient budget.** Trilight lights by *normal*: the sky colour only reaches
up-facing faces and the ground colour only down-facing ones, so in a first-person platformer — walls,
pillars, risers, and every enemy torso — **almost everything the player aims at is lit by the equator
alone**. And an enemy walking toward the eclipse is *backlit*, so the side facing the player gets no key
light at all: the equator is the only thing rendering it. Three independent passes each reported "the
frame is black outside the trims" and each blamed the tonemapper. **The surfaces were dark going in.**
Full write-up, including the two traps that made the first attempt at this fix a no-op:
[`ENGINEERING-LOG.md`](ENGINEERING-LOG.md).

Four things that pass came with:

- **A dark world is not a black world.** Structural albedo under ~0.02 linear (~`#1A1A1A`) is darker than
  any real material and gives light nothing to land on. `M_Ground` was `#151011` — about **0.008 linear**,
  darker than coal — over most of the level's surface area.
- **Judge an albedo by what it renders as, never by the hex.** `EnemyData.bodyColor` reads like a mid
  grey now and renders at 8–10/255, four times darker than the ground the enemy stands on. Measured on a
  grunt at 4.5 m: `#1E1A20` → 1.6/255 (still a cutout), `#2E2836` → 4.2, `#3C3446` → 8.5, `#4A4256` → 14.3.
- **The world came up and no emissive moved.** Bloom threshold (1.05), bloom intensity (0.60), ACES and
  every trim value are exactly as they were. Across the whole pass the gate emissive measured 154.1 →
  155.2 and the alert tell 204 → 211 — i.e. unchanged — while the ground went 23 → 36 and a backlit grunt
  went 0 → ~9. Compensating for a brighter world by pushing emissives up walks straight back into the ACES
  desaturation trap below.
- **Enemies still do not glow.** The lift is *albedo*, and each enemy's is hue-separated — cool slate,
  warm iron, violet — so it reads against the warm ember-lit floor by colour as well as by value.
  Emission is still `black` at rest: light on an enemy means you deflected.

**Trim colour is the level's per-tile identity.** The course is four tiles and the only thing that tells
you which one you are in — from across the map, in the dark — is the colour of the platform trim. The
four accents are therefore chosen to be separable by *hue*, at similar luminance, and are the shipped
values in `MaterialFactory.Table`:

| Material (key) | Used for | Shipped emission | Reads as |
|---|---|---|---|
| `M_NeonCyan` | tile 1 trim, ultimate ring, pickup shell | `#35DCEC × 1.00` → `(0.04, 0.72, 0.84)` | ice cyan |
| `M_NeonYellow` | tile 2 trim, hit sparks, posture bar | `#D8C22A × 0.75` → `(0.52, 0.40, 0.02)` | brass gold — the one warm navigational hue kept (see below) |
| `M_NeonPink` | tile 3 trim (navigation only) | `#2F6BFF × 0.95` → `(0.03, 0.14, 0.95)` | deep azure |
| `M_NeonRed` | tile 4 trim (navigation only) | `#3FE07A × 0.95` → `(0.05, 0.71, 0.18)` | ghost green |
| `M_AlertTell` | **unblockable / alert cube** — combat only, never level trim | `#FF0A28 × 3.00` → `(3.00, 0.12, 0.47)` | hot pink-white, blooms hard |
| `M_DeathblowMark` | **deathblow spot** on a posture-broken enemy's sternum, and the commit shatter thrown at that same point — combat only | `#2A0BFF × 2.60` → `(0.43, 0.11, 2.60)` | arc violet-blue, blooms hard |
| `M_LockOnDot` | **lock-on dot** on the chest of the locked target — combat only | `#CBD2D8 × 0.95` → `(0.76, 0.78, 0.81)` | pale bone-grey, **never blooms** |

Two traps are baked into those numbers:

- **Grey is not a colour.** Cyan and yellow were once near-neutral bone tints (`(0.30, 0.28, 0.25)` and
  `(0.35, 0.31, 0.26)`) and tiles 1 and 2 rendered identically. A trim that carries identity must carry
  hue, not just brightness.
- **Do not push a hue past the bloom threshold to make it louder.** `M_NeonRed` was `#FF2A10 × 2.2`; that
  far above the shipped 1.05 bloom threshold the tonemapper desaturates it and it renders *orange*,
  indistinguishable from the ember boss court. All four navigational trims are held at or under 1.25 (peak
  channel 0.72–1.10) so they stay saturated and do not smear. Loudness comes from **hue separation**, not
  intensity.
- **Loud is not a hue: a channel over 1.0 clips.** `M_DeathblowMark` first shipped as `#7A2BFF × 2.60`
  = `(1.24, 0.44, 2.60)`. Both red *and* blue were over 1.0, both pinned at full, and the "violet" glyph
  rendered **magenta** — near-identical to the alert tell it exists to be distinguished from. Dropping red
  to 0.59 got it to orchid, still close enough to pink to hesitate over; 0.43 is where it finally reads as
  blue-violet. When a marker must be both loud and a specific hue, only the dominant channel may exceed
  1.0, and the rest have to come *down* — you cannot get a hue back by turning it up.
- **The quiet marker is quiet on purpose.** `M_LockOnDot` is the only combat marker held *under* the
  1.05 bloom threshold, and that is the whole design. The tell means danger and the glyph means
  opportunity, so both are 2.5–3x over it and both are meant to grab you; lock-on means neither — it is
  up for the entire fight and has to stay ignorable. "Does not bloom" is an axis of separation that
  survives peripheral vision, and it cost nothing: at 0.78 against a `#1A1E29` enemy it is still ~60x the
  albedo it sits on. It is also desaturated because every saturated slot is already spoken for (four
  trims, the tell, the glyph, the Pyre fire), which is exactly why the Dark Souls reticle is a plain pale
  dot. Raising it to "make it clearer" merges it with the two markers it exists to differ from.
- **Three markers, three places.** The alert cube and the deathblow glyph hang above the head; the lock
  dot sits on the **chest**, at ~9 cm against their 25 cm and 70 cm, and holds a constant angular size so
  it cannot grow into them up close. Nothing else in the game marks centre of mass.
- **A marker on the centre of mass is inside the body.** The first pass put the dot exactly on the chest
  point and it was invisible at every distance — the enemy capsule is 0.45 m in radius and the dot was
  rendering inside it. It is now pulled 0.75 m toward the eye. That is cheaper and far less fragile than
  a depth-test-off overlay material, and it keeps the dot honestly occluded by real cover.
- **Two markers over the same head must differ on more than colour.** The alert tell and the deathblow
  glyph hang in the same place and mean opposite things, so they are separated three ways at once: hue
  (pink-white vs violet-blue), silhouette (upright cube vs crossed diamond) and motion (still vs spinning
  and breathing). Motion is the axis that survives bloom, peripheral vision and colour blindness.
  `FeatureTests > Deathblow` asserts the hue separation and that the tell stays the louder of the two.
- **Never share a material between a navigational trim and a combat tell.** They want opposite
  intensities — the trim under the desaturation ceiling, the tell far over the bloom threshold — so one
  number cannot serve both and tuning either silently detunes the other. `M_NeonRed` used to be both the
  tile-3 trim *and* the unblockable marker; dropping it for the trim killed the tell. The tell now lives
  in its own key, `M_AlertTell`, and `FeatureTests.TestTellReadability` fails loudly if they are merged
  again. Full write-up: [`ENGINEERING-LOG.md`](ENGINEERING-LOG.md).

All materials are URP/Lit with `_EMISSION` enabled. A `Standard` shader renders **magenta** under URP —
Health Check flags this.

**Weapon fire (Pyre).** Ember orange `#FF6B1A`, blended *from* each weapon's own neon so a hot Cerulean
Edge is still recognisably the sword. The project's established failure mode is the viewmodel eating
the frame (a riposte that rendered as a black screen; `ScreenFlash` at 0.55 shredding a bloom-heavy
image), so every knob here is capped: the blade tint blends at most 80% toward ember, `EnergyGlow`
charge is driven to **0.55, never 1.0**, the point light is **1.5 intensity at 2.4 m range** and ramps
with heat *squared* so it stays out of the way until the fire genuinely rages, and the embers are **10
pooled streaks** rather than a particle system. Escalation is carried by *rate and motion* — more
embers, faster flow band, faster motes — not by raw brightness, because brightness is the one axis
bloom will take away from you.

An ember is a **12 × 68 mm streak** (0.026 m cross-section stretched 2.6× along its own velocity),
12 per second at full charge, 0.34 s of life, launched at 0.6 m/s with a ×0.6–1.4 spread and rising.
The first pass was 14 twelve-millimetre cubes drifting slowly, and it read as **texture on the blade**:
the colour ramp and the light did ~90% of the work. Fewer, larger, faster, shorter-lived is the whole
change — shape over quantity — and not one of the caps above moved. The spark carries its own
**additive** URP/Unlit material at peak channel 1.45, not the blade's lit material: a lit black box is
invisible at 12 mm and a dark sliver at 68 mm.

**The swing trail.** A ribbon behind the blade tip, open for the **strike leg and nothing else**, so it
also tells the player when the weapon is dangerous. Peak channel **1.15** in the weapon's own
`WeaponData.neon` — over the 1.05 bloom threshold so it glows, under the ~1.25 ACES ceiling so it keeps
its hue, and far under the alert tell (3.00) and the deathblow mark (2.60): those are alarms, this is
flourish, and the brightness bands are how the three stay told apart. 0.030 m at the head, tapering on a
curve to a hairline, gone 0.11 s after the strike closes. Recorded in **camera space**, which is why it is
a `LineRenderer` and not a `TrailRenderer` — a world-space trail would hang in the world and smear across
the frame the moment you turned mid-swing. Values and invariants:
[`DATAFLOW.md > Swing trail`](DATAFLOW.md#swing-trail).


### The main menu

`Assets/Scenes/MainMenu.unity` is **build index 0**, so a built game starts at a title screen rather than
inside a level. It is generated, like everything else: **VibeGame1 > 9. Build Main Menu**
(`Editor/MainMenuBuilder.cs`) writes `Assets/Prefabs/MainMenu.prefab`, rebuilds the scene around one
instance of it, and re-inserts the scene at build index 0 — keeping every other build-settings entry.
A hand-placed `MainMenu_Manual` root is never touched.

The menu owns no gameplay singletons. It carries a camera (solid dark + AudioListener), a standalone
`AudioManager`, and the canvas. Deliberately **no Managers prefab**: that would bring a `SpeedrunTimer`,
which is exactly the flag `GhostRacing` uses to decide "this is a level, not a menu".

**The level list is `Assets/Data/LevelRegistry.asset`, not a literal.** One row per entry, each showing
`displayName`, `parTime` and the personal best from `RunStore` (the same store the ghost races against),
plus `LOCKED` / `CLEARED` from `LevelProgress`. A separate `SANDBOX` row, marked `DEV`, sits below a
divider — a practice space, not a campaign level. Full map:
[`DATAFLOW.md > Main menu`](DATAFLOW.md#main-menu).

The palette is `HudBuilder`'s, copied as constants rather than shared: the menu must read as the same game
as the HUD, but a HUD layout tweak must not silently move the menu.

### The blade family — weapon viewmodels

Four lengths, built from primitives by `PrefabFactory.BuildWeaponViewmodels`. The dagger pass had
shrunk every weapon into a 0.27–0.32 m band because a long blade at 95° FOV became a pole across the
frame — but **length was never what broke the frame; pose was.** A long weapon held vertically 0.6 m from
the lens fills the screen; the same weapon held further out and *canted* lies diagonally across the
lower-right corner and covers less than the old sword did. So the three properties the dagger set
actually earned are now enforced by measurement — `WeaponSilhouette` rasterises the real prefab at the
real `viewmodelScale` from the player's own eye, and `WeaponSilhouetteTests` holds every shipped weapon
in every held pose to them: (1) nothing crosses the crosshair disc (≤ 1.5 % of it) in idle or guard,
(2) nothing covers more than 5 % of the frame, (3) the tip stays inside the frame. Length is free again,
and the extent above the fist now spans **0.32–0.72 m, a 2.3× spread** where the dagger pass had 1.2×.

| Weapon | Silhouette | The one-glance tell | `viewmodelScale` | Extent above the fist | Swing |
|---|---|---|---|---|---|
| **Rosethorn** (dagger) | Needle stiletto | **Unchanged to the millimetre.** Thinnest section in the set, hard taper, barely a guard. The reference the player already likes — the one weapon the length pass does not touch. Green `#5FD66A`. | `0.50` | 0.32 m | 0.22 s, 4-hit |
| **Oathbreaker (TEST)** (dev) | Serrated arcane kris | The only **non-straight** blade, barbed down one edge, twin rings at two radii, pale violet `#C6A6FF` so it can never be read as Rosethorn. Deliberately *between* dagger and sword. | `0.46` | 0.50 m | 0.32 s |
| **Cerulean Edge** (sword) | Cruciform arming sword | A real sword at last: 8-slice tapered blade, wide knobbed quillons, hand-and-a-half grip, disc pommel. Held across the lower-right corner so the whole length reads. Steel-blue `#8FB5D9`. | `0.46` | 0.62 m | 0.44 s, 3-hit |
| **Verdigris** (hammer) | Maul | A long haft the fist grips **low**, carrying a blocky mass head, cheeks and a spike three quarters of a metre above the hand — the only weapon whose mass is at the *far* end, where the commitment can be seen. Patina `#A8D12E` — see `DataFactory`, the hue is forced by the bolt. | `0.50` | 0.72 m | 0.86 s, 2-hit |

**Reach is still not encoded in the viewmodel** — `hitOffset` / `hitRadius` are camera-space and always
were (a 0.6 m model does not reach 2.1 m). The geometry *sells* the reach; the data *is* the reach
(Rosethorn 1.3 + 0.9, Cerulean Edge 2.1 + 1.15, Verdigris 2.5 + 1.7), and the two are tuned to agree in
direction, never in metres. Every step up the set is roughly 2× in swing time and +0.4–0.8 m of reach, so
a swap is noticed inside one swing without reading a stat.

**`Sword.parryPostureDamage = 25` is load-bearing, not a tuning knob.** 25 × 1.4 (the Marionette's
`SpinPass` parry multiplier) × 6 = 210, exactly the Pale Marionette's posture bar;
`MarionetteDataTests.SixCleanDeflects_BreakIt` asserts the six-deflect break against `Sword.asset` and
`WeaponSilhouetteTests.SixDeflectEconomy_TheSwordStaysAt25` guards it from inside weapon-land. Change it
and either keep the arithmetic landing on 6 or move `maxPosture` in the same edit.

**`viewmodelScale` is shipped from `DataFactory`, per weapon** (rule 9). A geometry change in
`PrefabFactory` without the matching scale is a weapon that quietly resizes on screen.

### The guard stance — a pose you live in

`WeaponData.parry` is a momentary flick and always was: it sits at x `0.05`, essentially on the
crosshair, and gets away with it because it is on screen for 0.25 s. A **held** stance has a different
job. The player lives in it for seconds at a time, during which the enemy they are guarding against is
in the centre of the frame, so `WeaponData.guard` is a separate authored pose held **well right of
centre** (x `0.29`–`0.38`, asserted `>= 0.20` by `FeatureTests > Guard`) and **raised above idle**,
blade angled up and inward across the lower-right quadrant. Nothing of it crosses screen centre. This
project has already paid for the other choice once — a riposte that rendered as a black screen.

- **Per weapon, because mass is the differentiator.** The hammer guards with its *head*, carried lowest
  and furthest out (`0.38, -0.22`) and rolled least, because it is the one weapon whose volume would
  occlude the enemy at blade height. The dagger guards *tight* — pulled in and steepest
  (`0.29, -0.12`, roll 64°) — which is what a stiletto with no guard to hide behind actually does.
  Sword and dev blade take the class default. Rule 9: all of it written in `DataFactory`.
- **The stance outlives every momentary pose.** `WeaponViewmodel.guarding` survives an attack, a parry
  flick and a weapon swap; each of them re-enters `PlayGuard()` on the way out. Dropping to idle when
  the parry window closed made a held guard visibly flinch back to the hip every quarter second.
- **ONE MOTION IN, ONE MOTION OUT.** Rise **0.08 s**, release **0.16 s**. The rise is short because the
  perfect window is 130 ms and a stance that arrives later than that lags the button in the only
  exchange that matters; the release is slower and eased in *and* out so putting the blade down reads as
  a deliberate beat rather than a snap. Three rules make the motion continuous, and all three were wrong
  on the first pass - the entry visibly pointed the blade forward and came back:
  **(1) no waypoint** - a press with the button down calls `PlayGuard` directly, never `PlayParry`
  first, because the flick lives at x `0.05`, almost on the crosshair, and having it on the path *was*
  the forward excursion; **(2) slerp, do not lerp eulers** - the blend takes
  `Quaternion.Slerp(model.localRotation, guard)` so it travels the shortest arc and cannot pass through
  an orientation nobody authored; **(3) start from the LIVE transform**, never from the authored idle,
  or interrupting a swing teleports the blade home before it begins travelling. `AttackCo` also
  retargets its **recovery leg** at the stance whenever `WeaponViewmodel.GuardWanted` is set, so a swing
  that ends with the button down settles *into* the guard instead of into idle and raising afterwards.
- **`GuardWanted` (the button) is deliberately not `IsGuarding` (the mechanical stance).** A swing
  suppresses the guard's *protection* but must not interrupt the blade's *journey* back to the stance.
- **The entry is verified geometrically, not by eye alone.** `FeatureTests > GuardEntry_*` samples the
  blade tip's angle off the camera axis every frame of the real blend and fails if any frame is nearer
  the crosshair than **both** endpoints. Shipped measurement: idle `32.9 deg` to guard `9.2 deg`,
  worst frame `9.2 deg` - monotone, no excursion - and a swing settles to the same `9.2 deg`.
- **RULE 1: the stance runs on `TimeScaleController.PlayerDelta`.** It is driven by the player’s own
  button, and a guard that freezes halfway up during the hitstop of the blow it is absorbing is exactly
  the stutter rule 1 exists to prevent. (The *swing* clock stays scaled on purpose — that is the impact
  device.)

### First-person arms
The player has **visible gauntleted arms**, built from primitives by `PrefabFactory.BuildHand` /
`BuildArm` like everything else — no imported rig, no Animator, nothing hand-placed (rule 4). Full
hierarchy and solve order: [`DATAFLOW.md > Viewmodel arms`](DATAFLOW.md#viewmodel-arms).

- **A fist of ~10 boxes per hand**: a palm plate behind the hilt, four banded fingers *in front of* it, a
  thumb, a cuff and a cuff band. The Z ordering is the whole read — with the fingers behind the weapon the
  fist is one featureless slab with a blade sticking out of it.
- **Two bones per arm**, stretched between a fixed shoulder anchor and the hand by `ViewmodelArm`, so a
  swing visibly starts at the shoulder rather than at the wrist.
- **Gauntleted, not bare skin.** Shipped values, `MaterialFactory.Table`:
  `M_Gauntlet` base `#1B1719`, emission `#1B1719 × 0.12` (dark cold leather);
  `M_GauntletTrim` base `#14161A`, emission `#6E7B88 × 0.15` (cool steel banding).
  **The hands sit at the bottom of the visual hierarchy** — below the weapon, below the enemy, below the
  trim — so they are deliberately the darkest lit surface on screen. The first pass used a warm mid-brown
  (`#2C2522` leather, `#8A5A34 × 0.40` trim) and, lit by the arena, read as pale **wood**: the knuckles
  were among the brightest objects in the frame. Two rules came out of that: the leather stays only just
  above `M_Ground` (`#151011`) so the fist keeps a silhouette without lighting up, and the banding is
  **cool** grey-steel, never warm tan — warm banding reads as wood and also fights the ember world for
  attention. Everything here is far under the 1.25 bloom threshold.
- **Readability first.** The hands sit low and wide, off the crosshair and off the enemy. The failure mode
  of this viewmodel has always been eating the frame — arms are extra pixels on screen, so they earn their
  place only by staying at the edges.

### Audio
Real **CC0** clips live in `Assets/Resources/Audio/Sfx/<SfxName>/*` (a random variant is chosen per play)
and `Assets/Resources/Audio/Music/{ambient,boss}.ogg`, which crossfade on `BossStarted` / `BossDefeated`
/ respawn. Sources and licences: [`CREDITS.md`](../CREDITS.md). The radio (`LevelRadio.cs`) is a separate
2D `AudioSource` playing per-scene playlists from `Resources/Audio/Radio/<SceneName>/`; see the file header
for the fallback-to-`Default` rule. `AudioManager.MusicDuck` ducks the ambient/boss bed to zero while it plays.

**To swap a sound, drop files into the matching folder — no code change.** `ProceduralSfx.cs` synthesizes
a fallback only when a folder is empty; sourced clips are strongly preferred (the synthesized set was
rejected as harsh). **2026-09-06 audit finding:** four shipped folders (`Footstep`, `Land`, `ParryCue`,
`PostureBreak`) turned out to be real Kenney (`Impact Sounds`, CC0) clips with no `CREDITS.md` row at
all — identified from their Vorbis comment block (`ARTIST=KenneyG`, GameSynth/Tsugi), which matches the
already-credited `Block` clips exactly. Backfilled; see `CREDITS.md`.

⚠️ `Sfx` enum names *are* the folder names — append only, never reorder or rename.

**The mix table (`AudioManager.trim`, static) is exhaustive over every non-`Drone` `Sfx` value** — a
missing row silently took `PlayInternal`'s 0.7 default, which is how `Sfx.Teleport` shipped un-mixed
until the 2026-09-06 audio pass. `AudioManager.Trim(Sfx)` / `.HasExplicitTrim(Sfx)` are public so
`Assets/Editor/Tests/AudioTests.cs` can pin the table's shape (exhaustive, in-range, headroom around
`Sfx.ParryCue`) without a scene. `ParryCue` fires 0.28 s before every impact and is the one sound
nothing else may mask or match in level.

**2026-09-06 audio pass — four systems that shipped silent.** All four were built the same week and had
no sound at all: a refused stamina action (dash/wall-run/wall-jump/**slide**) now plays `Sfx.Refuse`
(`PlayerFeedback.OnStaminaRefused`, subscribed to `GameEvents.StaminaRefused`); a parkour sentry's
detonation now plays a dedicated `Sfx.Detonate` instead of borrowing the Stormbreak ultimate's
`Sfx.Thunder` (`SentryBurst.Detonate`); an enemy crossing into the near-break posture threshold plays a
one-shot `Sfx.Tension` (`EnemyPostureBar.LateUpdate`, edge-triggered — not a repeating heartbeat, to avoid
spamming the one-shot pool with several near-break enemies at once); and a flask drink punished mid-heal
now layers `Sfx.Spill` on top of the ordinary `Sfx.Hurt` (`FlaskAbility.Interrupt`) so losing the charge
reads as more than an ordinary hit. All four are synthesized only (`ProceduralSfx.cs`) — no new clip
files — and mixed under `Sfx.ParryCue`'s trim.

**2026-09-06 weapon-audio pass — the roster shared one swing and one hit.** Every weapon's `WeaponController`
call played the exact same `Sfx.Swing` / `Sfx.Hit` regardless of which was equipped, so a dagger and a
hammer connected with an identical sound — "if a weapon sounds weak, do not just turn it up" applied in
reverse: nothing was missing from any one sound, the roster was missing three separate voices. `WeaponAudio`
(`Feel/WeaponAudio.cs`) is a pure function of `WeaponData.hitStopSeconds` — the field that already encodes
how much force a weapon commits (Dagger 0.03, Sword 0.06, Hammer 0.11) — into a light / mid / heavy band,
so any future weapon gets weight-appropriate audio from its own data with no new per-weapon code. The sword
keeps the original `Sfx.Swing` / `Sfx.Hit` (real CC0 clips, see `CREDITS.md`) as the mid-weight default; the
dagger gets `Sfx.SwingLight` / `Sfx.HitLight` (thin, dry, no sub-bass — a puncture, not a crunch) and the
hammer gets `Sfx.SwingHeavy` / `Sfx.HitHeavy` (a strained mechanical creak fires *ahead of* the wind, before
the swing even lands, plus real sub-bass and a rumbling tail). All four are synthesis-only — no new clip
files, mixed alongside their `Swing`/`Hit` siblings rather than under `ParryCue`'s stricter line, matching
the precedent `Sfx.Hit` (0.8) and `Sfx.Stagger` (0.8) already set. `WeaponController.SwingCo` / `.DoHit` now
call `WeaponAudio.SwingSfx(w)` / `.HitSfx(w)` in place of the hardcoded enum values — the only two lines
touched outside `Feel/`. See `Assets/Editor/Tests/WeaponAudioTests.cs`.

---

## Input

`Assets/InputSystem_Actions.inputactions`, read via `InputSystem.actions`. No `PlayerInput` component and
no generated C# class. Optional actions are looked up with `throwIfNotFound: false` so a missing binding
cannot crash startup. Legacy `Input.GetAxis` is forbidden.

Player map: Move, Look, Attack, Parry, Jump, Dash, Slide, Heal, Ultimate, LockOn, Previous, Next,
WeaponSlot1-4, UseItem, WandCycle, Interact, LevelUpMenu, Pause, TestMenu, DebugWarpBoss, DebugRestore,
DebugSouls, DebugGodMode.

`Slide` is `<Keyboard>/leftCtrl` + `<Gamepad>/leftTrigger` — the only two inputs in the map bound to nothing
at all. **`C` looks free and is not:** the Unity template's `Crouch` action still holds it (and
`<Gamepad>/buttonEast`, which `Dash` also holds). Nothing reads `Crouch`, but binding a second action onto an
occupied key is precisely how `F` came to fire the flask and the wand altar together. The **wall jump has no
binding**: it is `Space` while airborne near a wall.

`LockOn` is `<Mouse>/middleButton` + `<Gamepad>/rightStickPress`. Middle mouse is the Souls convention and
was the only free pointer button; the scroll wheel — the other convention, and the obvious home for target
CYCLING — is already `Previous`/`Next` weapon swapping, which is why switching targets is folded into the
lock key (aim off the held target, press again) rather than given a binding of its own.

`Interact` (`<Keyboard>/f` + `<Gamepad>/dpad/down`) is the deliberate world-use verb — today only the wand
pedestal. It **shares `F` with `Heal`**: `WandPedestal.PromptActive` gives the altar priority and
`FlaskAbility` stands down while the prompt is up, so the tie is resolved explicitly rather than by script
execution order.

---

## Physics layers

| # | Layer | Rules |
|---|---|---|
| 6 | Player | Collides with Enemy |
| 7 | Enemy | Collides with Player and Enemy |
| 8 | Interactable | Trigger-only. **Must stay enabled against Player** — see [ENGINEERING-LOG](ENGINEERING-LOG.md) |
