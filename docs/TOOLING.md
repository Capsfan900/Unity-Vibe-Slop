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
| `2. Create Materials` | `Editor/MaterialFactory.cs` | `Assets/Materials/M_*.mat`, all URP/Lit with `_EMISSION` enabled — plus `M_Balloon` (gold, under the bloom cap) and `M_Water`, the one transparent material (alpha blend set up in `Configure`, never by hand). |
| `3. Create Data` | `Editor/DataFactory.cs` | All ScriptableObjects. ⚠️ **Overwrites Inspector tuning** — see [ENGINEERING-LOG](ENGINEERING-LOG.md). |
| `3b. Create Wands` | `Editor/WandFactory.cs` | The four riposte wands in `Assets/Data/Wands/`. **Must run before `4. Build Prefabs`**, which assigns the viewmodels back onto these assets. Included in `0. Rebuild Everything` between Data and Prefabs. Skip it and every riposte silently falls back to the melee deathblow. |
| `4. Build Prefabs` | `Editor/PrefabFactory.cs` | Player (with `PlayerBody` legs and the motor's six traversal numbers: water floor / accel / grace, burst window / multiplier / hold), Managers, enemies, boss, weapon and wand viewmodels, checkpoint, bloodstain, item pickup, and `Balloon.prefab` (`BuildBalloon`). The Heavy Sentry is generated as a blade-free 12-renderer stone reliquary, not the legacy pill. |
| `4a. Split Forge Animation Clips` | `Editor/ForgeClipSplitter.cs` | For every `Assets/Enemies/*.clips.json`, writes `ModelImporter.clipAnimations` from the manifest and forces the rig to `Generic`. An animated forge FBX imports as ONE take (`Take 001`) until this runs. Also applies the forge's skin contract (custom weights, 8 bones per vertex) and reads the manifest's `root.motion` flags — deliberately setting NO root node (a Generic rig's root node moves the whole Hips transform, lift and yaw included); a travelling clip keeps its Hips travel in the pose and `4b` gives the model a `TravelRoot` that `PuppetVisuals` cancels it on, with the distance shipped as `lungeDistance` (AUTHORING.md §2b). **Not** in `0. Rebuild Everything`: run it after copying in or re-exporting an animated model, then `4b`. |
| `4b. Build Mini-Bosses` | `Editor/MiniBossFactory.cs` | The six `Legendary_*` prefabs. A sibling of step 4, not part of it, so re-tuning a duellist never rebuilds the player rig. For an animated model it also calls `PuppetAnimatorFactory` to generate `Assets/Animation/<Prefab>_Animator.controller`, bakes every attack clip's length and contact frame onto `PuppetVisuals` (the named-clip table `EnemyAttackData.clip` resolves against), validates every attack's clip against the FBX, and for a `ModelSpec` with an `albedo` writes the per-model body material (`Assets/Materials/M_<model>.mat`). A `ModelSpec` with `bladeTrail` also gets an `EnemyWeaponTrail` (hand → weaponFxPos, in the hand bone's space, from the bind pose). |
| `5. Build HUD` | `Editor/HudBuilder.cs` | `Assets/Prefabs/HUD.prefab` — bars, item slots, menus, test menu, EventSystem (`InputSystemUIInputModule`). Regenerates the glass sprites under `Assets/UI/Generated/` (`Editor/UiSprites.cs`: pane, tile, pill, track, shadow, edge light, sheen — 9-sliced PNGs, uncompressed, committed), then runs the styling passes in `Editor/HudExtensions.cs`: `ApplyFluidBars` (health + stamina → `FluidBarView` with `VibeGame1/UI/FluidBar`) and `ApplyPyreFire` (`PyreBar` → `FireBarView`, creating `Assets/Materials/UI/M_PyreFire.mat` from `Assets/Shaders/UI/FireBar.shader` on first run). Every number written in the passes. `HudGlassTests`, `FluidBarTests` and `FireBarTests` pin the shipped values. Also the BEST RUNS glass pane (top-right, hidden until `GhostHud` has a board), the settings INFO card built by `SettingsPanelKit` from `ControlsInfo.Text` (the key reference, off the playing HUD), and the F1 menu's INFO button. |
| `6. Build Level` | `Editor/LevelGreyboxBuilder.cs` | Rebuilds the `Level` root in the open scene, bakes NavMesh, places Player/Managers/HUD. Never touches a `Level_Manual` sibling root. |
| `7. Build Sandbox Scene` | `Editor/SandboxBuilder.cs` | Builds `Assets/Scenes/Sandbox.unity`. Preserves a `Sandbox_Manual` root. See [README_Sandbox](../Assets/Scenes/README_Sandbox.md). |
| `9. Build Main Menu` | `Editor/MainMenuBuilder.cs` | `Assets/Prefabs/MainMenu.prefab` + `Assets/Scenes/MainMenu.unity`, and puts that scene at **build index 0**. One level-select row per `LevelRegistry` entry plus a `SANDBOX` row. Preserves a `MainMenu_Manual` root. NOT part of `0. Rebuild Everything` — re-run it after adding a level to the registry. |
| `8a. Rework Level_01 (parkour first)` | `Editor/LevelDefinitionAuthoring.cs` | Rewrites `Level_01_Level.asset` deterministically: broad T1–T3 decks plus restored shoulder walls/tower/lintels/obstacles, five connector ramps, the four-balloon T3 arc, water, Insight hand/flare-route data, isolated solar approaches, all shooter perches, five bounded projectile route-audit groups, and the explicitly progress-gated T0 opening volley. Idempotent; run it, then `8`, `Level Arc Report`, `Projectile Encounter Report` and both suites. `LevelTraversalTests` proves the same on a copy. |
| `8. Build Level From Definition` + the in-game editor | `Editor/LevelDefinitionBuilder.cs`, `Scripts/Level/LevelPieceFactory.cs`, `Scripts/Level/LevelEditor.cs` | Menu 8 builds every piece through `LevelPieceFactory.BuildDocument`, the SAME factory the runtime editor (F10) uses; the editor saves `LevelDocument` JSON under `persistentDataPath/levels/`, PLAY rebuilds + bakes a runtime NavMesh, EXPORT ASSET writes `Assets/Data/Levels/Custom/<name>_Level.asset`. See [LEVEL-EDITOR.md](LEVEL-EDITOR.md). |
| `Health Check` | `Editor/ProjectHealthCheck.cs` | Read-only validator. |
| `Run Feature Tests` | `Editor/FeatureTestRunner.cs` | Starts the play-mode suite (must already be in play mode). |
| `Run Quick EditMode Tests` / `Run Full EditMode Tests` | `Editor/QuickTestRunner.cs` | Runs the EditMode NUnit suite in the editor via `TestRunnerApi`. **Quick** skips the four level-line fixtures (`LevelSpan1/2/3Tests`, `LevelArcClearanceTests`), which carry `[Category("LevelLines")]` and simulate the motor along every wall-run line. Current counts and timings live only in `VERIFICATION-REPORT.md`; do not copy them here. The Test Framework `Filter` can only INCLUDE categories, so the quick run retrieves the EditMode test tree, drops the leaves whose `Categories` contain `LevelLines`, and runs the rest by `testNames`. Both log one completion line and write NUnit XML to `TestResults/EditMode-<stamp>.xml`. Callable as `VibeGame1.EditorTools.QuickTestRunner.RunQuick()` / `.RunFull()`. Cannot run in play mode. |
| `Open Test Level` / `Open Sandbox Scene` / `Open Main Menu Scene` | `VibeGameMenu` / `SandboxBuilder` / `MainMenuBuilder` | Scene shortcuts, prompt to save first. |
| `Rebuild NavMesh` | `LevelGreyboxBuilder` | Re-bakes without a full level rebuild. |
| `Level Arc Report` | `Editor/LevelArcReport.cs` | Flies `LevelArcAnalyzer` over every authored traversal in the `LevelDefinition` and prints what it finds; writes `LevelArcReport.txt` beside the project. Headless: `-executeMethod VibeGame1.EditorTools.LevelArcReport.Run`. |
| `Projectile Encounter Report` | `Editor/ProjectileEncounterReport.cs` | Audits any `LevelDefinition`: every projectile spawn has exactly one route-audit owner, every member has a valid bounded route window, and the runtime's own `ProjectileFlightMath` can create a cue-safe in-window contact at 11, 17.6 and 27.5 m/s. Windows prove authored geometry; only records with member progress gates create runtime volley coordinators. Writes `ProjectileEncounterReport.txt`; any failure is a level-authoring failure, not a warning to ignore. |
| *(no menu)* `LevelArcAnalyzer` | `Editor/LevelArcAnalyzer.cs` | The model under every level report and `LevelArcClearanceTests`: is that jump actually possible, and does anything stand in the middle of the arc? Flies the real ballistic arc and the real `WallRunMath` (it calls the motor's functions, never reimplements them) against the definition's boxes. |
| `Span 1 / 2 / 3 Wall-Run Report` | `Editor/LevelSpan1Report.cs`, `LevelSpan2Report.cs`, `LevelSpan3Report.cs` | Fly the wall-run lines of one span (Causeway, Ascent, Long Span) and print mount, run and landing per line, authored against `longest`, never `best`. Each writes `LevelSpanNReport.txt` beside the project — transient output, gitignored (`/*Report.txt`). `LevelSpan1-3Tests` assert the same tables. |
| *(no menu)* `LevelWallRunProbe` | `Editor/LevelWallRunProbe.cs` | The verbose companion to the arc report's wall-run section: for ONE line, every mount found and where every leave time puts the player. `AnalyzeWallRunGap` answers "does a route exist"; this answers "why not". Headless only: `-executeMethod VibeGame1.EditorTools.LevelWallRunProbe.Run`. |
| `Photograph The Sky` | `Editor/SkyShots.cs` | Photographs the sky and — the shot that decides anything — an enemy silhouetted against it at combat range, including standing on the eclipse's black disc. Headless: `SkyShots.Batch`. |
| `Photograph Weapons` | `Editor/WeaponShots.cs` | Every shipped weapon in every held pose FROM THE PLAYER'S EYE through the player prefab's own camera, crosshair and framing disc drawn into the frame, `WeaponSilhouette` numbers beside each. `RebuildAndShoot()` regenerates first. Headless: `WeaponShots.Batch`. |
| *(no menu)* `WeaponSilhouette` | `Editor/WeaponSilhouette.cs` | Rasterises the real viewmodel prefab at the real `viewmodelScale` under a real `Pose`, by hand (no GPU, no play mode): coverage, crosshair-disc coverage, tip-in-frame, extent above the fist. The main-hand twin of `PoseSilhouette`; `WeaponSilhouetteTests` asserts it on every shipped weapon. |

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

Reached from the title screen (SETTINGS) and from the pause menu (ESC → SETTINGS). Thirteen rows: mouse /
gamepad sensitivity, FOV, flourish key, resolution, display mode, vsync, frame cap, quality, bloom, film
grain, master volume and music volume. Persists
to PlayerPrefs under `vg1.settings.*`, applied on load in every scene by `SettingsApplier` (no component
to place — it bootstraps itself). Resolution / display mode take effect in builds only. Both prefabs are
emitted by `SettingsPanelKit` (in `HudBuilder.cs`); rebuild with `5. Build HUD` and `9. Build Main Menu`.
Tests: `Assets/Editor/Tests/SettingsDataTests.cs` (logic, 31) and `SettingsPrefabTests.cs` (every binding
on both prefabs, 11). Adding a setting: a `SettingsData` field + key, a `SettingsMenu.RowKind` + entry in
`AllKinds` + cases in `ValueLabel`/`Step`/`LabelFor`, an applier branch, and the kit emits the row
automatically.

### The development dashboard — `/dashboard`

`Tools/dashboard/build_dashboard.py` (Python 3, no dependencies; the `/dashboard` skill runs it with
`--open`) writes `Tools/dashboard/out/index.html`: status tiles (EditMode from the runner's
`TestResults.xml`, the feature suite from VERIFICATION-REPORT, uncommitted files, last commit), the handoff,
the change log (commits + every engineering-log entry), a Tests view (per suite, failures, skips, slowest), a
Systems view (DATAFLOW's maps, backlog sections), every doc rendered, and a search across all of them.
`--serve` keeps it on `http://127.0.0.1:8765/`. Read-only and editor-free; re-run to refresh. The output
folder is gitignored.

### Measuring an imported forge model — `ForgeModelProbe`

`Assets/Editor/ForgeModelProbe.cs`, menu **VibeGame1 → Probe Forge Models**, and headless via
`-executeMethod VibeGame1.EditorTools.ForgeModelProbe.Batch`. For every FBX in `Assets/Enemies/` it
reports mesh bounds and facing, the full bone list, the bind-pose position of every landmark bone, a
**suggested `ModelSpec` derived from the bones**, and per-clip hand separation sampled at 25/50/75%.

Run it before writing a `ModelSpec`, and trust it over the bounding box. On the Ember Revenant the two
disagree by 0.76 m — see ENGINEERING-LOG.md. The per-clip arm span is how the Marionette's spin clip was
chosen: `AttackSwing` tucks the arms to 0.76 m, `Roar` holds 2.0 m throughout.

**Without the editor: `Tools/measure_forge_fbx.py`.** Run with the forge tool's own Blender-carrying
venv (`C:\Users\tyler\Main Storage\ai_skelly_tool\.venv\Scripts\python.exe Tools/measure_forge_fbx.py --
<fbx> <clips.json>`). Same numbers in Unity axes — bounds, every bone head, facing slices of the mesh,
per-clip arm span, and per-clip Hips travel and lift — with no bridge and no play mode. It is what
measured the Argent Halberdier when the bridge was down, and the Hips-travel column is the one
`lungeDistance` has to match (the sidecar's `forward_m` is source motion, ~×1.3 smaller).

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

**The headless copy of the project is retired (2026-09-03).** It once gave the EditMode suite and any
`-executeMethod` on a `-batchmode` copy while the real editor stayed untouched, but a second import
competed with the user's session and the copy's `.meta` GUIDs diverged from the real ones. Everything
now goes through the open editor over the MCP bridge — see `.claude/skills/unity-editor/SKILL.md`. An
editor that is open but answers `no_unity_session` (the instances resource reports zero) lost the bridge
handshake at startup; `Assets/Editor/McpReconnect.cs` re-arms it on every domain reload, so saving any
script — or **Tools → MCP Bootstrap → Reconnect Bridge** — brings it back without a restart. If
no editor is running, ask the user to open it.

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

`Assets/Scripts/Debug/TestMenu.cs`, built by `HudBuilder.BuildTestMenu`. Present for trusted diagnosis in
every build, but `DeveloperAccess` keeps it inert until the private Backquote-console passphrase is accepted.
Pauses via `TimeScaleController` and unlocks the cursor.

| Column | Buttons |
|---|---|
| **WARP** | Start · Checkpoint 1 · Checkpoint 2 · Boss Arena |
| **GIVE ITEM** | Grapple · Rebound · Deflect Sigil |
| **WEAPON** | Slot 1–4 |
| **PLAYER** | Full restore · Toggle god mode · +1000 souls · Break my posture · **Wand pedestal: OFF/ON** (shows / hides the spawn altar — `WandPedestal.DevMenuEnabled`, off by default; without it the player keeps the prefab loadout, Emberlance equipped) |
| **ENEMIES** | Kill nearby · Stagger nearby · Reset enemies |

Plus a live readout: FPS, HP, posture, pyre, flask, weapon, held items, position, souls, wand altar on/off, deaths, timer,
boss segment/phase/HP/posture/state, and `Time.timeScale` vs `WorldScale`/`PlayerScale`.

---

## 6. Debug hotkeys

`Assets/Scripts/Debug/DebugKeys.cs`, gated in every build by the same process-local developer capability.

| Key | Action |
|---|---|
| `4` | Equip **Oathbreaker (TEST)** — 60 dmg, very wide parry window, 1000 execute damage |
| `F1` | Test menu |
| `F5` | Warp to boss arena entrance with the dev blade equipped |
| `F6` | Full heal + flasks + Pyre + wand cooldown |
| `F7` | +1000 souls |
| `F8` | Toggle god mode |
| `F9` | Toggle the **wall-run diagnostic**: a live one-line prompt readout of why the last wall run did or did not start (reason, flat speed, approach cos, look cos, wall found, stamina), plus a console line each time the reason changes |
| `E` | Use current item |
| `R` | Cycle riposte wand (Emberlance → Gravecall → Stormneedle → Voidspine) |
| `F` | At a wand pedestal (aim at it, prompt showing): open the wand selection menu. Otherwise drinks a flask. The pedestal only exists once **WAND PEDESTAL: ON** is set in the F1 test menu. |

### Player timing capture

After granting developer access in any build, open the Backquote console and use `timing start`, play the
ramp/encounter normally, then use `timing stop` and `timing export`. Export prints the absolute path to a
local JSON file under `Application.persistentDataPath/timing-captures/`. The trace contains player position,
velocity, grounded/slide/wall-run/dash/pull state, raw parry presses, and projectile emission/cue/arrival/
resolution timestamps with bolt, phrase and enemy-data identity. `timing status` reports the bounded buffer;
`timing discard` erases it. No file is created until export and nothing uploads.

---

## 7. Sandbox scene

`Assets/Scenes/Sandbox.unity`, built by `VibeGame1/7. Build Sandbox Scene`. Two rooms:

- **The arena** — a flat 60×60 walled room for trying combat without running the course: eight enemy
  spawn pads along the south wall (Grunt / Heavy / Boss / the five legendaries), each behind a wake
  switch so nothing charges you on load; a `WandPedestal` altar at the spawn point; item pedestals
  enumerated from `Assets/Data/Items`; a jump/dash platforming corner; the wall-run gauntlet
  (`WallRunGauntletTests` flies it); and a weapon rack.
- **The movement yard** — a 120×60 annex through a 6 m doorway in the arena's east wall, for tuning wall
  running, jumping, sliding, wall exits, landings and air control with room to run: distance stripes
  every 10 m, a 30 m raised runway with 30 m of nothing after it, two 40×8 m parallel wall-run walls
  with a 5.5 m corridor, a gap ladder (4/6/8/10/12 m), and a drop tower with tops at 3/6/9/12 m whose
  east faces all drop onto flat floor. The yard is a literal list — `SandboxBuilder.YardLayout()` —
  that the builder renders and `MovementYardTests` (EditMode) checks without the scene. Change the
  list, not the scene.

`SandboxController` (`Assets/Scripts/Debug/SandboxController.cs`) adds, all `[ContextMenu]`-exposed:
`SpawnEnemyInFront(int)` (0 Grunt, 1 Heavy, 2 Boss, 3-7 the legendaries — append-only), `SpawnDummy()`
(aggro-locked, ~1M HP practice target), `ActivateBoss()` (the boss spawns inert — there is no arena
trigger), `ClearAllEnemies()`, `ToggleInfiniteFlask()`, `ToggleInfiniteItems()`, `ResetSandbox()`, and
`WarpToMovementYard()` (teleport to the yard doorway, facing down the yard). The same warp is on the F1 test menu as **MOVEMENT YARD** (Warp column; a no-op outside the sandbox).

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

## 10. Unity CLI / Pipeline pilot

The Unity Hub-installed CLI is `1.0.0-beta.8`; this project pins experimental
`com.unity.pipeline` `0.7.0-exp.1`. Roll back both project-side changes with tag
`pre-unity-cli-pilot-2026-09-11`. The CLI talks to the already-open editor—it must never be confused with
`unity test`, `unity build` or `unity run`, which may launch a second/batch Editor against this working copy.

`Tools/unity-cli/Invoke-VibeGame.ps1` finds `unity` on PATH or Unity Hub's bundled executable and keeps the
project path explicit. Common calls:

```powershell
./Tools/unity-cli/Invoke-VibeGame.ps1 status
./Tools/unity-cli/Invoke-VibeGame.ps1 commands
./Tools/unity-cli/Invoke-VibeGame.ps1 state
./Tools/unity-cli/Invoke-VibeGame.ps1 preflight
./Tools/unity-cli/Invoke-VibeGame.ps1 health
./Tools/unity-cli/Invoke-VibeGame.ps1 build-webgl
./Tools/unity-cli/Invoke-VibeGame.ps1 build-windows
```

The MCP bridge is the primary terminal path for editor state, generators, tests and builds. It has the
project's established session pinning, state readback and long-operation workflow. Keep the CLI/Pipeline
pilot installed so none of the work developed on `unity-cli-pilot` is rolled back, but treat
`unity command ... eval` as an optional probe for short, read-only calls only. If Pipeline is absent,
unreachable, times out or changes behavior, do not retry it or start another Editor—continue through MCP
against the already-open editor.

Treat Pipeline reachability as ephemeral across domain reloads. In the 2026-09-11 pilot, `state`, preflight,
clip splitting, data generation, mini-boss generation and Sandbox generation all succeeded through the CLI,
but a later long mini-boss generation exceeded Pipeline's five-second main-thread response window. Unity
continued and wrote the prefab/controller after the client had returned HTTP 400. A subsequent `state` call
found port 7801, while the immediately following `preflight` temporarily reported no Pipeline instance.
On 2026-09-11 a later CLI preflight also exceeded that same five-second window and returned HTTP 400 while
the editor remained healthy. Therefore MCP owns normal work. Use CLI only when a short diagnostic benefits
from comparing transports; after any CLI call, read the editor or generated asset back through MCP. Neither
a CLI timeout nor a transient missing descriptor proves that Unity stopped the requested work.

The CLI's Hub account database and `Library/Pipeline/.unity-pipeline-port` descriptor are user-scoped. A
restricted automation sandbox can therefore return `LOCAL_STORE_UNWRITABLE`, fail to read the descriptor, or
claim the server is unreachable even while the editor and Pipeline are healthy. Confirm `status` from the
normal signed-in user context before treating that as a project failure. A healthy result names this exact
project, the pinned package version, and `pipelineServer.isReachable: true`. Raw direct
`Unity.exe -batchmode ... -createProject` probes are not a fallback: without Hub launch/bootstrap context they
can lose the Licensing Client pipe, register zero built-in packages and fail on engine modules that are in
fact installed.

## 11. Playtest builds

`Assets/Editor/BuildRunner.cs` cuts a non-dev playtest build (`BuildOptions.None`, development build OFF)
to `Builds/Windows/` and `Builds/WebGL/` at the repo root (gitignored). Call the static methods directly
from `execute_code` — `VibeGame1.EditorTools.BuildRunner.Windows()` / `.WebGL()` / `.All()` — never rely on
the `VibeGame1/Build/…` menu items over MCP, they only log. Publish with `Tools/publish/Publish-WebGL.ps1`
(GitHub Pages via a `gh-pages` worktree) and `Tools/publish/Publish-WindowsRelease.ps1` (zipped GitHub
Release). Full runbook, one-time GitHub settings and how to trace a bug report to a build SHA:
[docs/DISTRIBUTION.md](DISTRIBUTION.md).
Both publishers reject stale SHA metadata and dirty/unknown `Assets`, `Packages` or `ProjectSettings`
inputs. Full-repository dirtiness is recorded separately, so preserved local screenshots and personal
tool settings do not falsely make an otherwise reproducible player build unpublishable.

## Final-descent verification (2026-09-07)

`LevelDescentReport.Build(def)` supplements the platform-only ballistic arc report with exact
oriented ramp/box sightline checks. It reads the shipped ramp geometry. The eight
`LevelDescentTests` and expanded `LevelRampPlacementTests` cover migration, repeat generation,
doorway alignment, ramp endpoints and geometry obstruction.

In a fresh, unpaused Level_01 Play session, call `VibeGame1.EditorTools.LevelDescentProbe.Start(false)`
then `.Poll()`: one entry impulse and one `TrySlide`, followed by the real motor without injected
movement. It records speed, grounding and where the slide ends. `Start(true)` additionally allows
the three real turrets to fire and uses forecast-driven automatic parries; success requires all three
to grant surge. This proves integration, never human fairness. The probe temporarily protects health,
restores the prior invulnerability flag, returns the player, clears its projectiles and respawns the three
turrets. Health protection is OFF for the parry variant (invulnerability bypasses attack resolution).
`Start(false, true)` checks a mid-ramp jump cancellation. Live movement/jump input aborts the probe.
`StartOpening(false)` runs the same motor check on the new opening ramp; `StartOpening(true)` enables
its seven-member encounter and requires real surge grants from all five Surge Turrets plus exactly three
emissions and resolutions from each bottom Heavy Reliquary. Individual shot/grant counts remain
in the report: firing without a deflect does not prove that the bolt offered a frontal parry.
The opening variant requires all five Surge contacts on the 144 m slope, both three-contact Heavy phrases,
the full 1.60x multiplier, and arrival at the run-out. It reports each deflect time, position, slope progress,
Heavy readiness/cancellation and actual cue-to-contact interval. After the single
entry impulse, movement comes from the motor and real parry rewards. Automatic parries prove
integration, not a human input recording or a guarantee of encounter fairness.
The automatic-parry driver uses `EditorApplication.update`, which can poll too slowly when unfocused
to observe its 120 ms trigger. The final saved encounter passed at capped 60 fps, while an uncapped
run missed the first parry and cascaded into knockback. Treat an uncapped failure as inconclusive;
see VERIFICATION-REPORT for both runs. Restore any temporary frame-rate/VSync settings after testing.

## Subagent teams (2026-09-06)

For substantial multi-part work, `.claude/skills/astra-engineering-company/SKILL.md` provides the shared,
tool-neutral orchestration protocol. It treats model names as capability tiers, requires one owner per file,
keeps the Unity editor with the lead, and supplies a compact context-packet contract for workers. Its
`references/vibegame1.md` adapter applies the authorship and regression rules below; `AGENTS.md` remains the
authority if the adapter ever disagrees.

Five project subagents live in `.claude/agents/`. They exist to REFINE systems Fable and Opus built, never to
add mechanics; each is scoped to its own files and verifies offline (`dotnet build`) — the lead session owns the
Unity editor and runs the generators and suites after a pass.

| Agent | Model | Owns | Mode |
|---|---|---|---|
| `combat-designer` | Opus | combat, enemies, parry, difficulty | plan only unless told to implement |
| `editor-controls` | Sonnet | the F10 level editor's controls | edits |
| `ui-designer` | Opus | HUD, menus, bars, prompts, readouts | edits; **leads and delegates** its own lanes on a multi-part pass (`lead-and-delegate`) |
| `vfx-art-team` | Opus | effects, materials, shaders, colour, light budget, tells | edits |
| `audio-engineer` | Sonnet | every sound: cues, hits, movement, the radio, music beds, the mix; CC0 sourcing | edits |

**This applies to ANY work not done by Fable or Opus** — a one-off Sonnet worker, a fork on another model, a
remote job — not only the four named teams (CLAUDE.md, "Regression guard").

**Version control.** A team never commits. The lead commits each pass as ONE commit prefixed with the team's name
(`[ui-designer] …`, `[vfx-art-team] …`) after re-running the generators the report names and both suites, so a
regression is one `git revert <sha>`. Before a batch of team passes the lead tags the tree
(`git tag pre-<theme>-<date>`) as the coarse revert point.

