# vibegame1

A first-person, **melee-only**, parry-focused speedrun platformer prototype.

Neon White's level flow and run timer, Sekiro / Lies of P deflect combat (posture, staggers, deathblows),
a dark-fantasy greybox look. One test level: a parkour course gated by enemies, two checkpoints, and a
three-segment boss.

Status: **prototype** — mechanics and feel are the deliverable, not content.

---

## Requirements

| | |
|---|---|
| Unity | **6000.5.10f1** (Unity 6.5) — exact version, the project is pinned |
| Render pipeline | URP (`com.unity.render-pipelines.universal` 17.5) |
| Input | Input System package (project-wide actions asset). No legacy `Input.GetAxis`. |
| Build targets | Windows Standalone, WebGL |

## Running it

1. Open the project in Unity 6000.5.10f1.
2. `VibeGame1 > Open Main Menu Scene` (or open `Assets/Scenes/MainMenu.unity` — it is build index 0, so a
   built game starts here too).
3. Press **Play**. The cursor is unlocked in the menu: **PLAY** starts the first unlocked level,
   **LEVEL SELECT** lists every level in `Assets/Data/LevelRegistry.asset` with its par and your personal
   best, plus a `SANDBOX` practice arena.
4. Entering a level locks the cursor automatically. `Esc` pauses; **MAIN MENU** on the pause screen comes
   back here.

To skip the menu and work on a level directly, `VibeGame1 > Open Test Level`
(`Assets/Scenes/Level_01.unity`) and press Play; click once in the Game view to lock the cursor.

If anything looks broken (magenta materials, frozen HUD bars, enemies standing still), run
`VibeGame1 > Health Check` first — it names the exact problem.

## Controls

| Action | Keyboard / Mouse | Gamepad |
|---|---|---|
| Move | `WASD` | Left stick |
| Look | Mouse | Right stick |
| Jump | `Space` | A / Cross |
| Dash | `Left Shift` | B / Circle |
| **Slide** (needs speed; jump out of it to keep it) | `Left Ctrl` | Left trigger |
| **Wall jump** (airborne, near a wall) | `Space` again | A / Cross again |
| **Wall run** (no binding — arrive fast along a wall, looking along it) | — | — |
| **Settings** (sensitivity, FOV, graphics) | Title screen `SETTINGS`, or `Esc` → `SETTINGS` in a level | Same |
| Attack | `LMB` | RB |
| **Parry** (tap) | `RMB` | LB |
| **Guard** (hold) | `RMB` **held** | LB held |
| **Lock on / switch / release** | `MMB` (middle mouse) | Right stick click |
| Deathblow | `LMB` **aimed at an enemy carrying the violet marker** | RB |
| Heal (flask) | `F` | D-pad Up |
| **Choose wand** (aim at the altar at the start of a level) | `F` | D-pad Down |
| **Super attack** (needs a full PYRE bar; unique to each weapon) | `Q` | Y / Triangle |
| **Use item** | `E` | Right trigger |
| Swap weapon | `1` `2` `3` / scroll | D-pad Left / Right |
| Level up | `Tab` | Select |
| Pause | `Esc` | Start |

`F` is shared: while a wand altar's `[F]  CHOOSE WAND` prompt is showing it opens the wand menu, otherwise it
drinks a flask. `R` still cycles wands as a debug convenience.

**Movement tech.** A slide needs speed to start and adds 5 m/s on top of it — 16 m/s out of a run — then
bleeds back to 8 over about 4 m, and drops you low enough to pass under things you cannot walk under. The
point is not the slide: it is **jumping out of one**, which leaves the ground at 15.8 m/s and clears a 12.6 m
gap instead of 8.8 m. Cancel early and you keep all of it. A **wall jump** is just `Space` again while
airborne near a wall — it throws you off the wall and 2 m up, keeps whatever speed you had running *along*
the wall, and refuses the same face twice in a row, so two facing walls climb and one does not.

A **wall run** has no key. Arrive at a wall airborne, carrying at least 7 m/s *along* it (a sprint does,
a shuffle does not), moving with the face rather than into it, and looking roughly down it — and you run
it: a small upward catch, then gravity comes back in over about 1.6 s so you can feel the loan being called
in. Hold forward to keep speed on a long wall; let go and it bleeds, and under 5 m/s the wall drops you.
`Space` off the wall throws you *down the line* you were running — up 10, out 7, and +4 along the wall —
which is the payoff. The camera leans 13° into the wall while you are on it. The same face cannot be
re-run straight away, and three runs per airtime is the budget.

**Settings** live in one menu reached two ways: `SETTINGS` on the title screen, or `Esc` then `SETTINGS`
from the pause menu in a level. Mouse and stick sensitivity, FOV, resolution, display mode, vsync, frame
cap, quality, bloom and film grain; saved between sessions and applied in every scene.

Level 01 uses them in three places, plus the wall-run lines below. On **The Shattered Causeway** a fallen standing stone lies across the
walkway near its north end — slide under it to keep your speed, or jump over it and lose it — and one broken
slab out in the middle of the stepping stones is reachable only with a slide-jump, which skips two hops. On
**The Long Span** the broken balustrade posts either side of the walkway are close enough to kick off, so a
missed step is recoverable instead of fatal. **None of them is the only way through**: the original route
still works with nothing but jump and dash, and the fallen stone can simply be jumped.

The wall-run lines are all optional too and all faster than the route they stand beside: two trimmed
walls on the right of **The Shattered Causeway** (the first skips the four stepping stones), two on the
outside of **The Ascent**'s spiral (each skips two ledges of a leg — the run buys distance, the jump off it
buys the height back), and two on the right of **The Long Span**, which chain. None has been played by a
human yet; they are proven by the analyser only.

**Lock-on is one key doing three things**, resolved by where you are aiming when you press it: nothing
locked, it locks the enemy nearest the crosshair; already locked and still looking at it, it releases;
already locked but looking somewhere else, it switches to whatever is nearest the crosshair now. A small
pale dot appears on the locked enemy's chest. While it is held the camera softly keeps the target framed
when your mouse is still, so you can strafe around it — **the mouse always wins**: move it and the assist
stands down instantly, and swing far enough away (about 60 degrees) and the lock drops. It also drops on
its own when the target dies, leaves range or stays behind cover.

### Dev keys — editor and development builds only

Implemented in `Assets/Scripts/Debug/DebugKeys.cs`; compiled out of release builds.

| Key | Effect |
|---|---|
| `F1` | Toggle the test menu |
| `4` | Equip "Oathbreaker (TEST)" — 60 dmg, very forgiving parry window, 1000 execute damage |
| `F5` | Warp to the boss arena entrance with the test blade equipped |
| `F6` | Full heal + refill flasks + fill PYRE + clear the wand cooldown |
| `F7` | +1000 souls |
| `F8` | Toggle god mode |

## Core mechanics

- **Parry.** Tap parry. The enemy telegraph runs *dim charge → hard cue flash + audio ping → impact*.
  React to the **cue**, not the wind-up. Perfect deflect = no damage, builds the enemy's posture and
  your PYRE. Late = block: reduced damage, it costs **your** posture, and it stokes PYRE at only 35%.
- **Guard.** *Hold* the same button and the blade comes up across your body and stays there. Anything
  that would have hit you is blocked instead: **no damage at all** — but it costs **posture**, more than
  anything else in the game, and your posture **stops regenerating** while the blade is up. So the guard
  is somewhere to stand, not somewhere to live: turtle and you will be guard-broken and staggered in
  about three blows. A well-timed press while guarding is still a perfect deflect — the deflect is
  always the better answer. Two things go straight through a guard: an **unblockable** (the hot pink
  tell — move) and anything that hits you from behind. Swinging drops your own guard.
- **Posture.** Both sides have it. Fill an enemy's to open a **deathblow**. Let yours fill and you are
  staggered for 1.5 s at 0.4× speed, unable to act, taking 1.6× damage.
- **Deathblow.** When you break an enemy's posture a spinning **violet glyph** appears over its head —
  that one is ready to be killed. Attack *that* enemy and the swing becomes a deathblow: the glyph
  shatters, the blow commits, and it is unmistakably not a normal swing. An attack press with no marked
  enemy in front of you is always just a swing. The glyph vanishes the moment the enemy recovers, so the
  window is exactly as long as it looks. (Not the same as the **hot pink** cube, which means an
  unblockable attack is coming — that one you get away from.)
- **Enemies press you.** Grunts and Heavies are relentless by design — short recovery, they close distance
  while recovering, and a deflect does **not** end their combo. Wind-ups never drop below 0.45 s and the
  cue always fires the same lead before impact, so the pressure rises without becoming unreactable.
- **Boss.** Three segments. HP reaching 0 does **not** kill — it opens a 5 s deathblow window. Miss it
  and the boss recovers 12% HP.
- **Pyre, and setting your weapon on fire.** Every successful parry stokes the **PYRE** bar (bottom
  left, under your health). As it fills your weapon visibly catches — an ember or two at a quarter
  charge, a blaze at full — so you can read your own charge without ever looking at the HUD. It does
  not drain: the only thing that spends it is the super, and the only thing that resets it is dying.
- **Super attack (`Q`).** At full Pyre, the equipped weapon unleashes its own super, and each of the
  four is different: the sword's **Emberfall Arc** is one huge 170° sweep, the dagger's **Thornstorm**
  is nine fast stabs into a narrow cone that shreds posture, the hammer's **Sunbreak** is a slow
  overhead into the ground that quakes 360° and throws everything 6 m back, and the test blade's
  **Oathbreaker** is an instant 12 m nova. The banner tells you which one `Q` is holding.
- **The wand has a cooldown.** The riposte fires your equipped wand, and after a discharge that wand
  needs 3.5 s (Emberlance) to 9 s (Voidspine) before it can fire again — the bar under the wand name
  tracks it. A deathblow taken while the wand is cooling still lands, as the plain melee execute, and
  the prompt tells you how long is left. You are never locked out of a kill.
- **Speedrun timer** runs on unscaled time; hitstop and slow-mo do not cheat it.

## Items

Single-use pickups found in the level, Neon White style. You carry **three**, they fire in pickup order
(the leftmost HUD slot is next), and `E` uses one. Dying restores every pickup in the world, so a run
always starts from the same state.

| Item | Effect |
|---|---|
| **Stormcall** | **Arms** — your next successful **riposte** discharges a lightning storm centred on the victim, killing nearby lesser enemies outright and shattering a boss's posture |
| **Updraft** | Launches you straight up — a vertical shortcut |
| **Soul Lantern** | Full heal, clears your posture, refills the flask |
| **Phantom Step** | Brief invulnerability and a speed boost |

Stormcall is deliberately not a panic button: using it stores the charge, and you still have to earn the
deflect-to-deathblow exchange that releases it. A Stormcall and an Updraft sit at the level spawn point
so both are testable immediately.

## Rebuilding generated content

Almost everything — materials, ScriptableObjects, prefabs, the HUD, the level greybox and its NavMesh —
is generated by editor scripts so the whole game can be reproduced from source.

```
VibeGame1 > 0. Rebuild Everything      ← runs all six steps in the correct order
```

Individual steps (order matters; later steps consume earlier output):

| Step | Menu item | Produces |
|---|---|---|
| 1 | `1. Project Setup` | Layers 6/7/8, HDR color grading, volume profile, fog and lighting |
| 2 | `2. Create Materials` | `Assets/Materials/M_*.mat` (URP/Lit, emission enabled) |
| 3 | `3. Create Data` | ScriptableObjects in `Assets/Data` |
| 4 | `4. Build Prefabs` | Player, Managers, enemies, boss, weapon viewmodels, pickups |
| 5 | `5. Build HUD` | `Assets/Prefabs/HUD.prefab` |
| 6 | `6. Build Level` | The `Level` root in the open scene + NavMesh bake |

Utilities: `VibeGame1 > Health Check`, `Rebuild NavMesh`, `Open Test Level`.

> **Re-running step 3 resets weapon/enemy/attack assets** to the values hard-coded in
> `Assets/Editor/DataFactory.cs`. Inspector tweaks you want to keep must be copied back into that file.

> Hand-placed scene objects belong under a root named **`Level_Manual`** — the level builder deletes and
> rebuilds `Level` but never touches `Level_Manual`.

## Where things live

```
Assets/
  Scripts/
    Core/         GameManager, InputReader, TimeScaleController, GameEvents, Layers
    Player/       FirstPersonMotor, PlayerLook, PlayerCombat, ParryController, PlayerPosture,
                  WeaponController, WeaponViewmodel, ExecuteInteractor, FlaskAbility,
                  UltimateAbility, PlayerItems, PlayerStats, PlayerResources, PlayerDeath
    Combat/       Health, Posture, ParryMath, PostureMath, DamageInfo, EmissiveFlash
    Enemies/      EnemyController (FSM), BossController, EnemyVisuals, EnemyPostureBar, EnemySpawner
    Level/        LevelManager, Checkpoint, KillZone, BossArenaTrigger, SpeedrunTimer, ItemPickup
    UI/           HUDController, BarView, BossBarView, ItemSlotView, ScreenFlash, PromptView, PauseMenu
    Feel/         CameraShake, CameraFX, PlayerFeedback, LightningEffect, AudioManager,
                  ProceduralSfx, FlickerLight
    Progression/  SoulsWallet, Bloodstain, LevelUpMenu, UpgradeMath
    Data/         ScriptableObject definitions (WeaponData, EnemyData, ItemData, ...)
    Debug/        DebugKeys, TestMenu, DebugHarness, FeatureTests, SandboxController
  Editor/         The generators above + Tests/
  Data/           Tuning assets (edit these, not code)
  Materials/  Prefabs/  Resources/Audio/  Scenes/  Settings/
```

**Tuning lives in `Assets/Data/`**, not in code. Weapon damage, enemy HP/posture, attack windups, parry
windows, upgrade costs and game-feel constants are all ScriptableObjects you can edit in the Inspector
while in play mode.

### Architectural rules worth knowing

- `TimeScaleController` is the **only** thing that writes `Time.timeScale`. Player movement reads
  `TimeScaleController.PlayerDelta`, never `Time.deltaTime`, so hitstop never brakes your momentum.
- `GameEvents` is a static event bus. Gameplay raises, UI listens — no UI references in gameplay code.
- `InputReader` is the only script that touches the Input System.
- `BarView` sizes bars via RectTransform anchors, **not** `Image.fillAmount` (a UGUI `Image` with a null
  sprite silently ignores `fillAmount` and renders full — this froze every bar in the game once).

## Tests

Current status: **EditMode 20/20 pass · feature suite 192 passed, 0 failed, 7 skipped (`SUITE PASS`)**.
Full write-up, including the bugs the suite caught and what still needs a human:
[`docs/VERIFICATION-REPORT.md`](docs/VERIFICATION-REPORT.md).

**EditMode** tests in `Assets/Editor/Tests/` cover pure logic — parry window boundaries, posture math,
upgrade costs. Run with `Window > General > Test Runner > EditMode > Run All`. These work even when the
editor is unfocused.

**The feature suite** (`Assets/Scripts/Debug/FeatureTests.cs`) covers behaviour across 15 sections —
movement, hitstop scoping, parry outcomes, both posture systems, boss deathblows, weapons, items, flask,
ultimate, progression, level flow, HUD bars and audio. Enter play mode, then `VibeGame1 > Run Feature
Tests`, or over MCP:

```csharp
VibeGame1.EditorTools.FeatureTestRunner.Start();
VibeGame1.EditorTools.FeatureTestRunner.Poll();   // appends the full report when done
```

**Whole fights** — `Assets/Scripts/Debug/DebugHarness.cs` plays scripted scenarios with no input:

```csharp
VibeGame1.DebugHarness.Run("parry");   // or "boss" / "death"
// then read VibeGame1.DebugHarness.Log
```

> Both harnesses parry on an enemy state transition, so they have frame-perfect information no human has.
> They prove the **systems** work. They cannot tell you whether anything is readable, fair or fun — only
> playing can. A green suite is never "feel verified".

## Adding content

**A weapon** — add a `WeaponData` block in `DataFactory.CreateAll()` (damage, combo, hitbox,
`parryWindowMultiplier`), build its viewmodel in `PrefabFactory`, then add it to the Player's
`WeaponController.loadout`. Run steps 3 and 4.

**An enemy** — add an `EnemyData` (+ `EnemyAttackData` per attack, windup ≥ 0.45 s) in `DataFactory`,
add a prefab case in `PrefabFactory`, then place an `EnemySpawner` in `LevelGreyboxBuilder`. Run 3, 4, 6.

**An item** — add an `ItemEffect` enum value and handle it in `PlayerItems.Apply()`, create the
`ItemData` asset in `DataFactory`, and place an `ItemPickup` in `LevelGreyboxBuilder`. Run 3, 4, 6.
For an item that triggers on a later event rather than immediately, follow the Stormcall pattern: store
it in `Apply()`, raise `GameEvents.ItemArmed`, and act on the event that should release it.

**A sound** — drop `.ogg`/`.wav` files into `Assets/Resources/Audio/Sfx/<SfxName>/`. A random variant
plays each time. No code change. Empty folders fall back to `ProceduralSfx` synthesis.

## Credits

All audio is **CC0**. Sources are listed in [`CREDITS.md`](CREDITS.md).

`CLAUDE.md` holds the working notes and design contracts for AI-assisted development on this project.
