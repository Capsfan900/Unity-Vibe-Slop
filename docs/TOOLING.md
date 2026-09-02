# Custom tooling catalogue

Everything purpose-built for this project. **Check here before building a tool — it probably exists.**

Related: [ARCHITECTURE.md](ARCHITECTURE.md) · [ENGINEERING-LOG.md](ENGINEERING-LOG.md) · [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md) · [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md)

---

## 1. Editor menu — `VibeGame1/…`

All content in this project is **generated from code**. Nothing in the scene is hand-authored, so a
wiped scene is a rebuild, not a data-loss event.

| Menu item | Source | What it does |
|---|---|---|
| `0. Rebuild Everything` | `Editor/VibeGameMenu.cs` | Runs steps 1→6 (including `3b. Create Wands`) in the only order that works, behind a progress bar. Refuses in play mode. **Prefer this over calling steps by hand.** |
| `1. Project Setup` | `Editor/ProjectSetup.cs` | Layers (Player=6, Enemy=7, Interactable=8), physics matrix, HDR colour grading, volume profile, fog/lighting, Always Included Shaders, `runInBackground`. |
| `2. Create Materials` | `Editor/MaterialFactory.cs` | `Assets/Materials/M_*.mat`, all URP/Lit with `_EMISSION` enabled. |
| `3. Create Data` | `Editor/DataFactory.cs` | All ScriptableObjects. ⚠️ **Overwrites Inspector tuning** — see [ENGINEERING-LOG](ENGINEERING-LOG.md). |
| `3b. Create Wands` | `Editor/WandFactory.cs` | The four riposte wands in `Assets/Data/Wands/`. **Must run before `4. Build Prefabs`**, which assigns the viewmodels back onto these assets. Included in `0. Rebuild Everything` between Data and Prefabs. Skip it and every riposte silently falls back to the melee deathblow. |
| `4. Build Prefabs` | `Editor/PrefabFactory.cs` | Player, Managers, enemies, boss, weapon and wand viewmodels, checkpoint, bloodstain, item pickup. |
| `4a. Split Forge Animation Clips` | `Editor/ForgeClipSplitter.cs` | For every `Assets/Enemies/*.clips.json`, writes `ModelImporter.clipAnimations` from the manifest and forces the rig to `Generic`. An animated forge FBX imports as ONE take (`Take 001`) until this runs. **Not** in `0. Rebuild Everything`: run it after copying in or re-exporting an animated model, then `4b`. |
| `4b. Build Mini-Bosses` | `Editor/MiniBossFactory.cs` | The four `Legendary_*` prefabs. A sibling of step 4, not part of it, so re-tuning a duellist never rebuilds the player rig. For an animated model it also calls `PuppetAnimatorFactory` to generate `Assets/Animation/<Prefab>_Animator.controller`. |
| `5. Build HUD` | `Editor/HudBuilder.cs` | `Assets/Prefabs/HUD.prefab` — bars, item slots, menus, test menu, EventSystem (`InputSystemUIInputModule`). |
| `6. Build Level` | `Editor/LevelGreyboxBuilder.cs` | Rebuilds the `Level` root in the open scene, bakes NavMesh, places Player/Managers/HUD. Never touches a `Level_Manual` sibling root. |
| `7. Build Sandbox Scene` | `Editor/SandboxBuilder.cs` | Builds `Assets/Scenes/Sandbox.unity`. Preserves a `Sandbox_Manual` root. See [README_Sandbox](../Assets/Scenes/README_Sandbox.md). |
| `9. Build Main Menu` | `Editor/MainMenuBuilder.cs` | `Assets/Prefabs/MainMenu.prefab` + `Assets/Scenes/MainMenu.unity`, and puts that scene at **build index 0**. One level-select row per `LevelRegistry` entry plus a `SANDBOX` row. Preserves a `MainMenu_Manual` root. NOT part of `0. Rebuild Everything` — re-run it after adding a level to the registry. |
| `Health Check` | `Editor/ProjectHealthCheck.cs` | Read-only validator. |
| `Run Feature Tests` | `Editor/FeatureTestRunner.cs` | Starts the play-mode suite (must already be in play mode). |
| `Open Test Level` / `Open Sandbox Scene` / `Open Main Menu Scene` | `VibeGameMenu` / `SandboxBuilder` / `MainMenuBuilder` | Scene shortcuts, prompt to save first. |
| `Rebuild NavMesh` | `LevelGreyboxBuilder` | Re-bakes without a full level rebuild. |

Driving from MCP — same functions, no UI:

```csharp
VibeGame1.EditorTools.ProjectSetup.Run();
VibeGame1.EditorTools.MaterialFactory.CreateAll();
VibeGame1.EditorTools.DataFactory.CreateAll();
VibeGame1.EditorTools.PrefabFactory.BuildAll();
VibeGame1.EditorTools.HudBuilder.Build();
VibeGame1.EditorTools.LevelGreyboxBuilder.Build();   // NOT in play mode
VibeGame1.EditorTools.MainMenuBuilder.Build();       // NOT in play mode
```

### Health Check
Catches the failure classes that have actually bitten this project: non-URP (magenta) materials, prefabs
with missing scripts or null references, a Player prefab missing a component, wrong layer names, LDR
colour grading, the scene missing from build settings, and empty `Resources/Audio/Sfx/<Name>` folders.
Reports `ERRORS` vs `WARNINGS` and a `PASS`/`FAIL` line. Resolves player components and the `Sfx` enum
**by reflection**, so it still runs while gameplay scripts are mid-edit.

**Run it after any rebuild, before assuming something is broken in code.**

---

## 2. Automated feature tests — `FeatureTests`

`Assets/Scripts/Debug/FeatureTests.cs` + `Assets/Editor/FeatureTestRunner.cs`. Sections cover movement,
hitstop scoping, parry maths, live parry outcomes, player posture, enemy posture and execute, weapons,
wand pedestal, viewmodel arms, tell readability, deathblow, lock-on, items, flask, Pyre + super,
progression, level flow, **level structure**, **the arena gate loop**, boss, HUD bars, audio, **the main menu**.

**Last run: 522 passed · 0 failed · 0 skipped — `SUITE PASS` (~29 s), on `Assets/Scenes/Level_01.unity`
from a fresh play-mode session.** It has found five real shipping bugs; full write-up in
[VERIFICATION-REPORT.md](VERIFICATION-REPORT.md).

⚠️ **Run it from a FRESH play-mode session.** `LevelFlow` asserts on a running speedrun timer, and a
suite that has already defeated the boss has stopped it. Filters work: `Start("Gate")` runs the arena
gate loop alone in ~9 s.

Play-mode coroutines, not NUnit — see [ENGINEERING-LOG](ENGINEERING-LOG.md) for why. Every test is
timeout-guarded, wrapped in try/catch so one failure cannot take down the suite, and restores player
state between tests. All waits use **unscaled** time because hitstop is itself under test.

**Driving it over MCP** (enter play mode first, and let a few frames elapse):

```csharp
// start
VibeGame1.EditorTools.FeatureTestRunner.Start();          // or .Start("Boss") to filter
// poll — appends the full report once finished
VibeGame1.EditorTools.FeatureTestRunner.Poll();
VibeGame1.EditorTools.FeatureTestRunner.IsDone();
VibeGame1.EditorTools.FeatureTestRunner.FullReport();
```

`Poll()` returns `running=… done=… passed=… failed=… skipped=… current='…'`; the report ends with
`SUITE PASS` or `SUITE FAIL`. Every assertion logs actual vs expected, so failures are diagnosable from
the report text without re-running.

**Known permanent skips (7).** Coyote time, jump buffer, dash / air-dash, weapon combo advance, the flask
drink coroutine, the super attack, and timer-start are only reachable through `InputReader`
inside `Update()`. Covering them needs small public entry points (`TryJump()`, `TryDash()`,
`TryDrink()`, `TrySuper()`) — a deliberate refactor, not a test-side fix.

**Two assertions are regression guards**, and should not be "simplified" away: HUD bars assert on
`fill.rectTransform.anchorMax.x` (never `fillAmount`), and pickup/bloodstain tests walk into the trigger
with real physics and assert `!Physics.GetIgnoreLayerCollision(Interactable, Player)`. Both encode bugs
that already shipped once.

---

## 3. EditMode tests

`Assets/Editor/Tests/` — `ParryMathTests`, `PostureMathTests`, `UpgradeMathTests`. Pure functions only.
**Last run: 20 / 20 pass.** Run via MCP `run_tests` with `mode: EditMode`, or the Test Runner window.
These work while the editor is unfocused, unlike play mode.

---

## 4. Scripted fight scenarios — `DebugHarness`

`Assets/Scripts/Debug/DebugHarness.cs`. Watch a *whole fight* play out rather than assert on units —
complementary to `FeatureTests`, not replaced by it.

```csharp
VibeGame1.DebugHarness.Run("parry");   // grunt → parry → stagger → execute, then a heavy
VibeGame1.DebugHarness.Run("boss");    // arena, all deathblow segments
VibeGame1.DebugHarness.Run("death");   // death → bloodstain → respawn → enemy reset → recovery
// then read:
VibeGame1.DebugHarness.Done;  VibeGame1.DebugHarness.Log;
```

⚠️ The harness parries on an enemy **state transition**, so it has frame-perfect information no human
has. It proves the state machine, **not** that the game is readable or fair. Never report harness
success as "feel verified".

⚠️ Don't click the Game view while a scenario runs — real mouse input feeds the player.

### Capturing what a scenario looks like — `ViewmodelCapture`

`Assets/Editor/ViewmodelCapture.cs`. Renders `Camera.main` to a RenderTexture and writes PNGs. It is
driven from `EditorApplication.update`, **not** from a coroutine, so play-mode timing is untouched and a
burst catches the frames a player would actually see — including mid-riposte, where a stall would move
the very thing being inspected.

```csharp
VibeGame1.EditorTools.ViewmodelCapture.Shoot(@"C:\tmp\one.png");       // single frame, now
VibeGame1.EditorTools.ViewmodelCapture.Burst(@"C:\tmp", "swing", 12);  // one PNG per editor tick
VibeGame1.EditorTools.ViewmodelCapture.Status;
```

⚠️ Verify `EditorApplication.isPlaying` **in the same call** that captures. A script edit recompiles and
drops play mode, and an edit-mode Player has no weapon instance and a hand collapsed at the camera
origin — the frame looks like the viewmodel vanished. See the engineering log.

⚠️ And verify `GameManager.I != null` too. A recompile *during* play mode reloads the domain, which wipes
every static — singletons, `GameEvents` subscriptions — without re-running `Awake`. Play mode looks fine
and nothing works. **After any script edit, exit and re-enter play mode.**

`LoadoutTour` walks **all four weapons** in one editor-driven sequence — equip, let a tick pass, shoot
the idle, play the swing and burst across it — which is the only reliable way to compare the weapons,
because every frame then shares a camera, a light and a scene state.

```csharp
VibeGame1.EditorTools.ViewmodelCapture.LoadoutTour(@"C:/tmp/weapons", 3f);  // 3f = swing playback stretch
VibeGame1.EditorTools.ViewmodelCapture.TourStatus;
```

⚠️ **Never equip and shoot in the same frame.** `SetWeapon` frees the old model with `Destroy`, which
Unity defers to end of frame, so the shot contains two weapons stacked on each other. `LoadoutTour`
waits; a hand-rolled loop will not.

⚠️ The `slowFactor` stretches only the swing. The pose path is a normalised lerp, so a 3× swing walks
exactly the same poses — it just gives the ~8 Hz editor tick enough samples to see a 0.22 s dagger arc.

### Filming a whole beat, frame by frame — `FrameFilm`

`Assets/Scripts/Debug/FrameFilm.cs` (runtime, not editor). Captures in `LateUpdate`, so it gets **every
rendered frame**, and buffers the textures in memory, writing the PNGs only once the run is over.

Use it for anything shorter than about two seconds. `ViewmodelCapture` ticks at roughly 8 Hz in the
background; a 0.6-1.0 s riposte gets about six samples from it and the stab and the blast fall in the
gaps. The buffering matters as much as the rate: every beat in the riposte is on **realtime** waits
(rule 1), so a slower frame rate does not slow the riposte down with it — a per-frame `EncodeToPNG`
costs enough to halve the sample count it is meant to raise.

```csharp
VibeGame1.FrameFilm.Run(@"C:	mp
iposte", "Enemy_Grunt");   // whole parry-to-riposte, every frame
VibeGame1.FrameFilm.Status;                                   // done / frames / log
```

`RunSwings(dir)` films **one real swing per weapon at the shipped attack speed**, flushing per weapon
(four weapons of 1024×576 frames at once is a quarter of a gigabyte). Use it for anything about the
swing itself — the arc, the trail, the follow-through hold, the wind-up easing. `LoadoutTour` cannot
answer those: the swing trail is open for the strike leg only, which is 0.044–0.14 s, and an ~8 Hz
editor tick samples that window less than once and reports "there is no trail".

`RunWindups(dir, prefabName, distance)` films **every wind-up pose in one enemy's moveset**, from the
player's eye, at a chosen fighting distance, in the real lighting. It stages a fresh enemy, switches off
its brain, its agent and its **colliders** (a 2.2x boss otherwise shoves the camera off the mark), parks
the player square in front and keeps re-parking between poses, then writes two frames per attack: `_mid`
at ~55% of the wind-up and `_peak` immediately after `CueFlash`, where the arm hitches past full
extension and freezes. **The `_peak` frame is the one the parry decision is made on.** It enumerates
`BossData.phases[].patterns` as well as `combos`, because a boss does not attack out of `combos`.

```csharp
VibeGame1.FrameFilm.RunWindups(@"C:/tmp/windups", "Enemy_Grunt", 3.8f);   // Heavy 4.2, Boss 8.0
```

Suggested distances: Grunt **3.8 m**, Heavy **4.2 m**, Boss **8 m** — the boss fights from 4.6 m, but at
2.2x scale it fills the frame there, so film it further out to judge the silhouette and remember the
real read is closer. **A wind-up pose is judged from these photographs, never from the authored Euler**
— see ENGINEERING-LOG.md.

`RunGuardEntry(dir)` films the three entry paths into the held guard.

### Settings menu

Reached from the title screen (SETTINGS) and from the pause menu (ESC → SETTINGS). Ten rows: mouse /
gamepad sensitivity, FOV, resolution, display mode, vsync, frame cap, quality, bloom, film grain. Persists
to PlayerPrefs under `vg1.settings.*`, applied on load in every scene by `SettingsApplier` (no component
to place — it bootstraps itself). Resolution / display mode take effect in builds only. Both prefabs are
emitted by `SettingsPanelKit` (in `HudBuilder.cs`); rebuild with `5. Build HUD` and `9. Build Main Menu`.
Tests: `Assets/Editor/Tests/SettingsDataTests.cs` (logic, 24) and `SettingsPrefabTests.cs` (every binding
on both prefabs, 6). Adding a setting: a `SettingsData` field + key, a `SettingsMenu.RowKind` + entry in
`AllKinds` + cases in `ValueLabel`/`Step`/`LabelFor`, an applier branch, and the kit emits the row
automatically.

### Measuring an imported forge model — `ForgeModelProbe`

`Assets/Editor/ForgeModelProbe.cs`, menu **VibeGame1 → Probe Forge Models**, and headless via
`-executeMethod VibeGame1.EditorTools.ForgeModelProbe.Batch`. For every FBX in `Assets/Enemies/` it
reports mesh bounds and facing, the full bone list, the bind-pose position of every landmark bone, a
**suggested `ModelSpec` derived from the bones**, and per-clip hand separation sampled at 25/50/75%.

Run it before writing a `ModelSpec`, and trust it over the bounding box. On the Ember Revenant the two
disagree by 0.76 m — see ENGINEERING-LOG.md. The per-clip arm span is how the Marionette's spin clip was
chosen: `AttackSwing` tucks the arms to 0.76 m, `Roar` holds 2.0 m throughout.

### Photographing an enemy — `EnemyPortrait`

`Assets/Editor/EnemyPortrait.cs`, menu **VibeGame1 → Photograph Enemies**, headless via
`-executeMethod VibeGame1.EditorTools.EnemyPortrait.Batch`. Renders a prefab from the player's eye height
at that enemy's own `preferredRange`, from four angles, and prints where the eye and deathblow glyph
actually ended up. It calls `EnemyVisuals.Setup` and `SetAura` by hand because **`Awake` never runs in
edit mode**, so without them the body renders in the wrong colour and unlit.

⚠ **It discards a warm-up render, and so should anything else that captures headlessly.** Unity's first
`Camera.Render()` in `-batchmode` returns before the pipeline has set itself up and produces a flat,
wrongly-lit frame — the first Revenant portrait came out solid orange and read as a broken material.

⚠ **Runtime-only effects do not appear.** `EmberAura`'s embers are spawned by its update loop, and
nothing updates in edit mode. The body glow in these frames is real; the particles are absent. Assert
those with a test instead — `RevenantDataTests.ThePrefabActuallyCarriesTheAura` resolves the component
TYPE, which is the only thing that catches a **missing script** (a prefab whose YAML looks perfect but
whose `GetComponent` returns null at runtime, silently).

### Filming the Marionette's whirl headless — `SpinFilm`

`Assets/Editor/SpinFilm.cs`. **The one capture tool that needs neither play mode nor a working MCP
bridge**, which is why it exists separately from `FrameFilm`: it drives `SpinRoot` directly at the frame
times a player at a given frame rate would see, renders the real prefab through a camera at
`preferredRange`, and writes the frames plus the number that actually matters — **degrees of body yaw
per rendered frame**.

```csharp
VibeGame1.EditorTools.SpinFilm.Capture(@"C:/tmp/spin", 60f);   // menu: VibeGame1/Film the Whirl
```

Or fully headless, on a *copy* of the project so it never touches a running editor:

```
Unity.exe -batchmode -projectPath <copy> -executeMethod VibeGame1.EditorTools.SpinFilm.Batch -logFile <log>
```

`Batch()` films at **60 and 30 fps** — 30 being where `PuppetVisuals.ResolvePeak`'s alias guard is
supposed to start earning its keep, so the report shows the guard engaging rather than asserting that it
would. Past roughly **90°/frame** a 2-fold-symmetric silhouette (a humanoid with its arms out) aliases
into apparent random orientation; the report states the fastest step and whether it clears that.

**Copying the project to run Unity headless is a general escape hatch worth remembering.** `Assets` +
`Packages` + `ProjectSettings` is ~19 MB; Unity rebuilds its own `Library` in the copy. That gives you
the EditMode suite (`-runTests -testPlatform EditMode -testResults <xml>`) and any `-executeMethod`
while the real editor stays untouched — including while someone is mid-playtest in it.

⚠️ **Restart play mode before judging a VFX frame.** A domain reload (any script edit anywhere,
including another agent's) wipes non-serialized fields but leaves pooled `GameObject`s in the scene, so
things like the Pyre embers freeze mid-flight wearing whatever they last had and are photographed as
artefacts that no longer exist in the code.

It borrows a standing spot from an enemy the level already placed (a blind NavMesh spawn on a platformer
course lands on whatever ledge is nearest), switches that enemy off and stages a **fresh** one of the
requested prefab there — an enemy the level has been fighting cannot be reliably staggered on demand,
because one caught inside a committed combo returns to `Windup` on the next frame.

---

## 4b. Performance measurement — `PerfProbe`

`Assets/Editor/PerfProbe.cs`. Driven from outside play mode like `FeatureTestRunner`:

```csharp
VibeGame1.EditorTools.PerfProbe.Start("running", 300);   // label, frames
VibeGame1.EditorTools.PerfProbe.Poll();                  // progress, then the report
```

Reports **mean / p50 / p95 / p99 / worst** frame time, the **count of frames over 16.7 ms**, and mono
heap delta in KB and KB/frame.

Two things it does deliberately, because they change how you read it:

- **Percentiles and the worst frame, not just a mean.** This is a speedrun game — it is ruined by the
  worst frame, not the average. A change that is fine on average but adds a p99 spike has failed.
- **Samples `Time.unscaledDeltaTime`.** Hitstop and the super's slow-mo drive `Time.timeScale` to 0.02;
  scaled delta would report those frames as impossibly fast.

**Compare runs, never read absolutes.** Editor play mode carries editor overhead and is not a build. The
useful measurement is the same scenario in the same scene, before versus after a change. The number that
matters most for movement code is **KB/frame while moving, which should be ~0** — steady per-frame
garbage is what eventually produces the collection hitch that loses a run.

---

## 5. In-game test menu — **F1**

`Assets/Scripts/Debug/TestMenu.cs`, built by `HudBuilder.BuildTestMenu`. Editor and development builds
only. Pauses via `TimeScaleController` and unlocks the cursor.

| Column | Buttons |
|---|---|
| **WARP** | Start · Checkpoint 1 · Checkpoint 2 · Boss Arena |
| **GIVE ITEM** | Stormcall · Updraft · SoulLantern · PhantomStep |
| **WEAPON** | Slot 1–4 |
| **PLAYER** | Full restore · Toggle god mode · +1000 souls · Break my posture |
| **ENEMIES** | Kill nearby · Stagger nearby · Reset enemies |

Plus a live readout: FPS, HP, posture, pyre, flask, weapon, held items, position, souls, deaths, timer,
boss segment/phase/HP/posture/state, and `Time.timeScale` vs `WorldScale`/`PlayerScale`.

---

## 6. Debug hotkeys

`Assets/Scripts/Debug/DebugKeys.cs`, editor and development builds only.

| Key | Action |
|---|---|
| `4` | Equip **Oathbreaker (TEST)** — 60 dmg, very wide parry window, 1000 execute damage |
| `F1` | Test menu |
| `F5` | Warp to boss arena entrance with the dev blade equipped |
| `F6` | Full heal + flasks + Pyre + wand cooldown |
| `F7` | +1000 souls |
| `F8` | Toggle god mode |
| `E` | Use current item |
| `R` | Cycle riposte wand (Emberlance → Gravecall → Stormneedle → Voidspine) |
| `F` | At a wand pedestal (aim at it, prompt showing): open the wand selection menu. Otherwise drinks a flask. |

---

## 7. Sandbox scene

`Assets/Scenes/Sandbox.unity`, built by `VibeGame1/7. Build Sandbox Scene`. A flat 60×60 arena for
trying combat without running the course: six enemy spawn pads along the south wall
(Grunt / Heavy / Boss / Legendary_Ninja / Legendary_Knight / Legendary_Spellsword), a `WandPedestal`
altar at the spawn point, item pedestals enumerated from `Assets/Data/Items`, a jump/dash platforming
corner, and a weapon rack.

`SandboxController` (`Assets/Scripts/Debug/SandboxController.cs`) adds, all `[ContextMenu]`-exposed:
`SpawnEnemyInFront(int)` (0 Grunt, 1 Heavy, 2 Boss, 3-5 the legendaries — append-only), `SpawnDummy()` (aggro-locked, ~1M HP practice target), `ActivateBoss()` (the
boss spawns inert — there is no arena trigger), `ClearAllEnemies()`, `ToggleInfiniteFlask()`,
`ToggleInfiniteItems()`, `ResetSandbox()`.

Full layout: [README_Sandbox.md](../Assets/Scenes/README_Sandbox.md).

---

## 8. Console diagnostics

`[Parry]` logs print elapsed ms vs the window for every parry resolution (editor only) — the fastest way
to tell "the window is wrong" from "the player is early".

---

## 9. Config files

| File | Purpose |
|---|---|
| `.editorconfig` | C# style matched to existing code (Allman, 4-space, CRLF). Unity noise diagnostics IDE0044/0051/0052 disabled — `[SerializeField]` fields look unused to the analyzer. |
| `.gitattributes` | Unity YAML gets `merge=unityyamlmerge eol=lf`; binaries marked `binary`. **SmartMerge needs a one-time `git config` per machine — command is in the file header.** Git LFS deliberately not configured; the header explains when to switch. |
| `.claude/settings.json` | Permission allowlist for routine Unity MCP + read-only Bash. `execute_code` and `manage_asset` are deliberately **excluded** — both can destroy work. |
| `CREDITS.md` | CC0 audio sources and licences. |
