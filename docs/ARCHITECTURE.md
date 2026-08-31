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
| `Core/` | 5 | `GameManager` (state machine, cursor, `runInBackground`), `InputReader`, `TimeScaleController`, `GameEvents`, `Layers` |
| `Combat/` | 6 | `Health`, `Posture`, `DamageInfo`, `ParryMath`, `PostureMath`, `EmissiveFlash` |
| `Player/` | 16 | `FirstPersonMotor`, `PlayerLook`, `PlayerCombat`, `ParryController`, `PlayerPosture`, `PlayerStats`, `PlayerResources`, `WeaponController`, `WeaponViewmodel`, `ViewmodelArm`, `WandController`, `ExecuteInteractor`, `FlaskAbility`, `UltimateAbility`, `PlayerItems`, `PlayerDeath` |
| `Enemies/` | 5 | `EnemyController` (FSM), `BossController`, `EnemyVisuals`, `EnemyPostureBar`, `EnemySpawner` |
| `Level/` | 6 | `LevelManager`, `Checkpoint`, `ItemPickup`, `BossArenaTrigger`, `KillZone`, `SpeedrunTimer` |
| `UI/` | 10 | `HUDController`, `BarView`, `BossBarView`, `ItemSlotView`, `ScreenFlash`, `PromptView`, `PauseMenu`, `WandSelectMenu`, **`MainMenuController`** |
| `Feel/` | 7 | `CameraShake`, `CameraFX`, `PlayerFeedback`, `FlickerLight`, `LightningEffect`, `AudioManager`, `ProceduralSfx` |
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
`DeathblowReady` · `ItemsChanged` · `ItemPickedUp` · `ItemUsed` · `ItemArmed` · `RiposteLanded`

`ItemArmed` carries the armed item, or `null` to clear the HUD tell. `RiposteLanded` is raised **before**
the killing damage, so listeners can still read the victim's position and state — that ordering is what
makes the Stormcall discharge work. It is raised by whichever path actually lands the riposte:
`WandController` at the discharge, or `ExecuteInteractor` in the no-wand melee fallback. The two are
mutually exclusive; never raise it twice for one riposte.

---

## Singletons

Ten, all exposing a static `I`. Note for any future multiplayer work: roughly half are per-player
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
  hammer *Sunbreak* (0.52 s wind-up into a 360° quake, 130 posture, 6 m knockback), dev blade
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
- **Slide (`Left Ctrl`).** A momentum move, not a crouch: refused below `5 m/s`, so you cannot slide out of a
  standstill. Entry is `max(current, run) + 5` capped at `22`, i.e. **16 m/s out of an 11 m/s run**. It bleeds
  at `2/s` to a floor of `8 m/s` — **4.0 m in 0.35 s**, measured, and identical from 20 fps to 400 fps. The
  capsule drops `1.8 → 1.0 m`, so a slide passes under geometry a standing player cannot. Average speed over
  the slide (11.5 m/s) is barely above a run: **the slide is not the reward, the jump out of it is.**
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
- **The movement path allocates nothing and is measured, not asserted.** `FindWall` is 0 bytes over 20 000
  calls at 2.36 µs, and runs at most once per jump press. See DATAFLOW's Movement invariants.

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

### The Pale Marionette — the animated prototype

`Legendary_Marionette`. **A prototype and a sandbox exhibit, not a campaign enemy.** It has a pad, a
wake switch and `SandboxController.SpawnEnemyInFront(6)`; it is in no `LevelDefinition` and no
`LevelRegistry`. The four-tile course's three gate keepers are designed and tested and a fourth was not
what was asked for.

It exists to answer two questions the project had not answered:

**1. Can an enemy spin genuinely fast without breaking the 0.45 s wind-up floor?**
Yes, because *visual spin rate and hit cadence are different quantities*. The body's peak angular speed
is about **1500 °/s — roughly 4 revolutions a second** — and the damaging passes arrive every
**0.76 s** from a **0.50 s** wind-up. One revolution is still exactly one pass, so "parry it each time
it comes around" holds literally; the speed comes from the revolution being **non-uniform**. Each pass
travels a whole turn on an ease-out curve that starts at 2.5× the average rate and decays to about
0.25×, so the puppet blurs through the back of the turn and **decelerates into you**. The deceleration
IS the wind-up: at the cue, 0.28 s out, it is still ~85° off and visibly slowing, and the cue flash
lands as it comes round the corner. Nothing about that touches timing — `PuppetVisuals` writes one
local yaw on a dedicated `SpinRoot` transform and nothing else.

**The cadence cannot drift**, which is the other half of making a rhythm learnable. The whirl's phase
is not integrated forward between passes; it is re-derived every beat from where the body actually is
and the data's own time-to-impact, so a dropped frame, a hitstop or a deflect cannot accumulate. And
the beat itself is equal whether you deflect or not: an unparried pass is
`windup 0.50 + gap 0.10 + impactDelay 0.04 + strike 0.12 = 0.76 s`, and a parried one is
`recoil + 0.50 + 0.10 + 0.04`, so `parryRecoilSeconds` is set to **0.167** — which times aggression
0.62's 0.719 multiplier gives a 0.120 s recoil and a 0.760 s parried beat. That number is *derived*,
not felt; changing `aggression` means re-deriving it. (Residual: a perfect parry may land up to half
the perfect window early, so the next beat can be pulled in by ≤ 0.065 s. Bounded and player-caused.)

**The economy: six clean deflects break it, and the spin breaks EARLY.** With the sword a deflected
pass is `parryPostureDamage 25 × parryPostureMultiplier 1.4 = 35`, so 6 × 35 = its whole 210 bar. The
signature phrase is *eight* passes long, so a clean player breaks it two passes before it would have
ended on its own and a sloppy one has to survive the whole thing — **the player's rhythm decides how
long the spin lasts, not a script.** That is the choice over "run N revolutions then self-recover",
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
| `Marionette_SpinUp` | 0.95 | 0.05 | 0.14 | 0.20 | 200° | 18 | 1.4 |
| `Marionette_SpinPass` | **0.50** | 0.04 | 0.12 | 0.20 | 200° | 17 | 1.4 |
| `Marionette_SpinOut` | 0.60 | 0.05 | 0.18 | **2.00** | 200° | 26 | 1.6 |
| `Marionette_Overhead` | 1.00 | 0.07 | 0.24 | 1.00 | 65° | 40 | 1.9 |
| `Marionette_Lash` (unblockable) | 1.05 | 0.08 | 0.30 | 1.50 | 175° | 30 | — |

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
  specifies — 1.36× for a spin pass. If a clip is the wrong length, the clip loses.

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

| Item | Effect | Behaviour |
|---|---|---|
| **Stormcall** | `LightningStrike` | **Arms** on use; discharges on the next riposte (see below) |
| **Updraft** | `Updraft` | `FirstPersonMotor.Launch()` — a vertical platforming shortcut |
| **Soul Lantern** | `SoulLantern` | Full heal, clears posture, refills the flask |
| **Phantom Step** | `PhantomStep` | Temporary invulnerability + `SpeedMultiplier` boost |

### Stormcall: arm, then riposte

Stormcall is **not** a panic button. Using it stores the charge and raises `ItemArmed`; it only
discharges when `RiposteLanded` fires — i.e. once you have earned a deflect-to-deathblow exchange.

```
PlayerItems.UseCurrent  → Apply → ArmLightning        [ArmedStorm set, ItemArmed raised, HUD tell]
        …player fights…
ExecuteInteractor       → GameEvents.RiposteLanded(victim)   [raised BEFORE the killing damage]
        └→ PlayerItems.OnRiposteLanded → Detonate(item, victim.position)
             normal enemies → killed outright (isExecute)
             boss          → heavy damage + posture shattered → deathblow window
```

The blast is centred on the **riposte victim**, not the player, so it is an AoE reward for closing the
exchange. `ArmedStorm` is cleared on respawn along with the inventory.

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
- **`RiposteLanded` is raised before the damage**, so the armed Stormcall can still read the victim.
  `WandController` raises it at the discharge; `ExecuteInteractor` raises it only in the no-wand melee
  fallback. Exactly one of the two fires per riposte.

Chosen at the **wand pedestal** at the level's spawn point: aim at the altar and press `F`
(`WandPedestal` → `WandSelectMenu` → `WandController.Equip`) — see the Wand pedestal map in `DATAFLOW.md`.
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
| `ItemData` | Stormcall, Updraft, SoulLantern, PhantomStep |
| `PlayerStatsData` | `PlayerStats.asset` — parry windows, posture, flask, Pyre, super slow-mo |
| `UpgradeTable` | Souls costs |
| `GameFeelSettings` | Hitstop, shake, flash, FOV kick |

⚠️ **`VibeGame1/3. Create Data` overwrites these.** Values you want to keep must go back into
`Assets/Editor/DataFactory.cs`.

---

## Presentation

### Art direction — dark fantasy
Void-black violet background and fog, cold moonlight, blood / ember / ghost-teal accents, flickering
torches (`FlickerLight`), film grain and heavy vignette. Four separable trim accents: ghost teal, brass
gold, crimson, ember orange - one per level tile.

Palette lives in `Editor/MaterialFactory.cs` and the colour constants in `Editor/HudBuilder.cs`.
**Material names are historical and no longer describe their colour** — they are kept stable because the
builders reference them by name:

**Lighting and structural albedo — shipped values.** Ambient and the key light live in
`Editor/ProjectSetup.SetupSceneEnvironment` (mirrored in `Editor/SandboxBuilder.EnsureEnvironment`), the
structural albedos in `Editor/MaterialFactory.Table`, and the enemy body albedos in
`Editor/DataFactory` — never in the Inspector or on a `.mat` (rules 4 and 9).

| Knob | Shipped | Reasoning |
|---|---|---|
| ambient mode | `Trilight` | three-way by surface normal, costs nothing |
| ambient sky | `#3E4A6B × 1.35` | cool starlight, lifts platform **tops** — the surfaces you land on |
| ambient equator | `#7A5540 × 1.35` | warm eclipse ember — lifts every **vertical** face and every enemy |
| ambient ground | `#191424 × 1.35` | dim violet bounce; undersides stay heavy so shapes keep weight |
| `ambientIntensity` | **1.0 — it is a no-op here** | Unity applies it to *Skybox* ambient only; in Trilight the multiplier must live in the colours |
| key directional | `#C9663A`, **1.05**, Euler `(10, 180, 0)` | one dying sun low behind the arena, backlighting the course |
| `M_Ground` | `#262023` | ~0.020 linear — most of the structural surface area |
| `M_Stone` | `#3A3134` | ~0.033 linear — walls, pillars, obelisks; above ground so a wall separates from the floor |
| `M_Platform` | `#56504A` | ~0.082 linear — ash top, footing legibility, never trim |
| `M_Enemy` | `#1F1D24` | material default only — **the body albedo comes from `EnemyData.bodyColor`**; smoothness **0.34** (see below) |

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
| `M_NeonCyan` | tile 1 trim, ultimate ring, pickup shell | `#1FB9D6 × 1.00` → `(0.12, 0.73, 0.84)` | cold ghost teal |
| `M_NeonYellow` | tile 2 trim, hit sparks, posture bar | `#D8C22A × 0.85` → `(0.72, 0.65, 0.14)` | brass gold |
| `M_NeonRed` | tile 3 trim (navigation only) | `#FF1010 × 1.10` → `(1.10, 0.07, 0.07)` | saturated crimson |
| `M_NeonPink` | boss court trim, checkpoints, gates | `#C4400F × 1.15` → `(0.88, 0.29, 0.07)` | ember orange |
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
  survives peripheral vision, and it cost nothing: at 0.78 against a `#1F1D24` enemy it is still ~60x the
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

Every weapon is a **short blade**, built from primitives by `PrefabFactory.BuildWeaponViewmodels`. The
player's read, once visible arms landed, was that a short blade is the only thing that shows the swing
*and* the hand at 95° FOV — a long blade is a pole across the frame and its arc leaves the screen. So
length stopped being the differentiator and **mass and edge** took over: the on-screen extent above the
fist sits in a deliberately tight 0.27–0.32 m band, and each weapon still has to answer *what does this
do* in one glance.

| Weapon | Silhouette | The one-glance tell | `viewmodelScale` | Extent above the fist |
|---|---|---|---|---|
| **Cerulean Edge** (sword) | Short cruciform dirk | The **cross** — the widest guard in the set, ending in knobbed quillons, over a parallel-sided blade and a disc pommel. Symmetric and featureless on purpose: the generalist looks like the default sword. Steel-blue `#8FB5D9`. | `0.52` | 0.321 m |
| **Sunbreaker** (hammer) | Weighted war-dirk | **Top-heavy.** A blocky mass head ~4× the width of any blade, cheeks either side, an ember band across it and a stubby spike over the top, on the shortest and thickest haft. All the volume is above the hand. Ember `#E0661A`. | `0.50` | 0.298 m |
| **Rosethorn** (dagger) | Needle stiletto | **Thinnest section in the set**, hard taper to a point, a guard barely wider than the blade. This is the reference silhouette — the one that reads best — so it is the one changed least. Green `#5FD66A`. | `0.50` | 0.271 m |
| **Oathbreaker (TEST)** (dev) | Serrated arcane kris | The only **non-straight** blade (slices alternate side to side), the only **barbed** edge, twin rings at two radii, and pale violet `#C6A6FF` rather than the set's greens so it can never be read as Rosethorn. The cheat weapon should look ceremonial and wrong. | `0.53` | 0.318 m |

**Weight class is told by mass, not by reach.** A hammer that is dagger-length can no longer say "slow
and heavy" by being long, so it says it by putting every cubic centimetre of its volume above the fist
and by having the only grip the hand visibly has to open wider for. This is a real, acknowledged loss of
information: reach is *not* encoded in the viewmodel at all and never was — `hitOffset` / `hitRadius` are
camera-space and a 0.3 m viewmodel never reached 1.6 m — so the weapon's true range is still learned only
from the cadence (`attackDuration` 0.22 s to 0.70 s), never from the model.

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
/ respawn. Sources and licences: [`CREDITS.md`](../CREDITS.md).

**To swap a sound, drop files into the matching folder — no code change.** `ProceduralSfx.cs` synthesizes
a fallback only when a folder is empty; sourced clips are strongly preferred (the synthesized set was
rejected as harsh).

⚠️ `Sfx` enum names *are* the folder names — append only, never reorder or rename.

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
