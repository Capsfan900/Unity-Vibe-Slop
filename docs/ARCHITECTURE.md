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
| `UI/` | 7 | `HUDController`, `BarView`, `BossBarView`, `ItemSlotView`, `ScreenFlash`, `PromptView`, `PauseMenu` |
| `Feel/` | 7 | `CameraShake`, `CameraFX`, `PlayerFeedback`, `FlickerLight`, `LightningEffect`, `AudioManager`, `ProceduralSfx` |
| `Progression/` | 4 | `SoulsWallet`, `Bloodstain`, `UpgradeMath`, `LevelUpMenu` |
| `Data/` | 9 | ScriptableObject definitions (see below) |
| `Debug/` | 5 | `DebugKeys`, `TestMenu`, `DebugHarness`, `FeatureTests`, `SandboxController` |

---

## Asset layout

| Path | Contents |
|---|---|
| `Assets/Scenes/` | `Level_01.unity` (the campaign test level) and `Sandbox.unity` |
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
                        Blocked → 30% damage + player posture
                        Hit     → full damage + player posture (1.6× if staggered)
```

Player→enemy hits are `Physics.OverlapSphere` in front of the camera (`WeaponController.DoHit`).
Enemy→player hits are a distance + cone test at the scheduled impact time — no hitbox colliders.

### Combat feel contracts

- **Parry windows.** Perfect `0.13` / late `0.12` / whiff recovery `0.5`. `cueLead` `0.28` is serialized
  on `EnemyController` and must stay ≈ reaction (0.20) + half the perfect window. No enemy attack windup
  below `0.45`.
- **Posture (Sekiro).** Both sides have it. A perfect parry costs the player **nothing**; blocking costs
  `damage × 0.9` posture, a raw hit `damage × 0.5`, unblockables ×1.5. Player break = 1.5 s stagger,
  0.4× move speed, no jump/dash/attack/parry/flask/super, and 1.6× damage taken. Enemy break opens the
  deathblow window.
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
- **Super attack (`Q`, full Pyre).** One per weapon, authored entirely as `WeaponData.super*` fields.
  Sword *Emberfall Arc* (one 170° sweep), dagger *Thornstorm* (nine stabs in a 70° cone, posture-heavy),
  hammer *Sunbreak* (0.52 s wind-up into a 360° quake, 130 posture, 6 m knockback), dev blade
  *Oathbreaker* (instant 12 m nova). `SuperKind` chooses only how the blow is drawn; the geometry is
  the numbers. `UltimateAbility` is the driver — repointed, not replaced, so `Q` and the prefab wiring
  survive. Rule 1 holds: `affectsPlayer:false` slow-mo, realtime waits.
- **Wand cooldown.** The riposte blast is a resource: Emberlance 3.5 s, Stormneedle 5.5 s, Gravecall 7 s,
  Voidspine 9 s. It gates the **blast**, never the deathblow — a cooling wand degrades to the melee
  execute and the prompt says so, because every cooldown is longer than the boss's 5 s deathblow window.
- **Movement feedback** lives in `Feel/PlayerFeedback.cs` — footsteps, jump/land/dash audio, landing dip
  scaled to fall speed. It subscribes to `FirstPersonMotor`'s `OnJumped` / `OnLanded` / `OnDashed`.

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
and through a distinct silhouette on the shared enemy rig. Feedback is the world-space
`EnemyPostureBar`, as on Grunt and Heavy.

Each one exists to teach one parry skill, and the next assumes you have it:

| | Name | Shape | Teaches |
|---|---|---|---|
| `Legendary_Ninja` | **THE THIRTEENTH SHADE** | 3–5 hit strings of 0.45 s cuts, aggression `1.0`, recoveries at `0.22`, `preferredRange 3.2` | **Ride the cadence.** Sustained deflect rhythm — and `Ninja_Reap`, an unblockable sweep inside that rhythm, teaches that holding the cadence is not the same as holding parry. |
| `Legendary_Knight` | **THE IRON PENITENT** | 0.8–1.1 s wind-ups, aggression `0.35`, recoveries up to `1.4`, `1.6×` scale at `preferredRange 4.0` | **Wait, then commit.** Huge posture payoff (`parryPostureMultiplier 1.9` on the overhead) against huge punishment for a panicked early parry, and `Knight_Quake` is an unblockable you walk out of and then punish. |
| `Legendary_Spellsword` | **THE ASHEN CHORISTER** | hybrid; `Spellsword_Emberfall` opens at range, `Spellsword_Feint` fakes the end of a phrase, `Spellsword_Grasp` punishes greed | **Do not trust the phrase.** Champion-Gundyr shaped: the feint's short `recovery` plus a long `comboGap` makes the breath after the heavy a lie, and stepping in to punish it is what `Grasp` is for. The hardest of the three. |

The Shade is fast through **combo density and short recoveries only**. No wind-up in the set is below
`0.45 s` — the contract above is not negotiable for a mini-boss, and a faster tell would need its cue to
fire before the wind-up began.

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

Two traps are baked into those numbers:

- **Grey is not a colour.** Cyan and yellow were once near-neutral bone tints (`(0.30, 0.28, 0.25)` and
  `(0.35, 0.31, 0.26)`) and tiles 1 and 2 rendered identically. A trim that carries identity must carry
  hue, not just brightness.
- **Do not push a hue past the bloom threshold to make it louder.** `M_NeonRed` was `#FF2A10 × 2.2`; that
  far above the shipped 1.05 bloom threshold the tonemapper desaturates it and it renders *orange*,
  indistinguishable from the ember boss court. All four navigational trims are held at or under 1.25 (peak
  channel 0.72–1.10) so they stay saturated and do not smear. Loudness comes from **hue separation**, not
  intensity.
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
with heat *squared* so it stays out of the way until the fire genuinely rages, and the embers are 14
pooled 12 mm cubes rather than a particle system. Escalation is carried by *rate and motion* — more
embers, faster flow band, faster motes — not by raw brightness, because brightness is the one axis
bloom will take away from you.

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

Player map: Move, Look, Attack, Parry, Jump, Dash, Heal, Ultimate, Previous, Next, WeaponSlot1-4,
UseItem, WandCycle, Interact, LevelUpMenu, Pause, TestMenu, DebugWarpBoss, DebugRestore, DebugSouls,
DebugGodMode.

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
