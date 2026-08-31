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
| `5. Build HUD` | `Editor/HudBuilder.cs` | `Assets/Prefabs/HUD.prefab` — bars, item slots, menus, test menu, EventSystem (`InputSystemUIInputModule`). |
| `6. Build Level` | `Editor/LevelGreyboxBuilder.cs` | Rebuilds the `Level` root in the open scene, bakes NavMesh, places Player/Managers/HUD. Never touches a `Level_Manual` sibling root. |
| `7. Build Sandbox Scene` | `Editor/SandboxBuilder.cs` | Builds `Assets/Scenes/Sandbox.unity`. Preserves a `Sandbox_Manual` root. See [README_Sandbox](../Assets/Scenes/README_Sandbox.md). |
| `Health Check` | `Editor/ProjectHealthCheck.cs` | Read-only validator. |
| `Run Feature Tests` | `Editor/FeatureTestRunner.cs` | Starts the play-mode suite (must already be in play mode). |
| `Open Test Level` / `Open Sandbox Scene` | `VibeGameMenu` / `SandboxBuilder` | Scene shortcuts, prompt to save first. |
| `Rebuild NavMesh` | `LevelGreyboxBuilder` | Re-bakes without a full level rebuild. |

Driving from MCP — same functions, no UI:

```csharp
VibeGame1.EditorTools.ProjectSetup.Run();
VibeGame1.EditorTools.MaterialFactory.CreateAll();
VibeGame1.EditorTools.DataFactory.CreateAll();
VibeGame1.EditorTools.PrefabFactory.BuildAll();
VibeGame1.EditorTools.HudBuilder.Build();
VibeGame1.EditorTools.LevelGreyboxBuilder.Build();   // NOT in play mode
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

`Assets/Scripts/Debug/FeatureTests.cs` + `Assets/Editor/FeatureTestRunner.cs`. 15 sections: movement,
hitstop scoping, parry maths, live parry outcomes, player posture, enemy posture and execute, weapons,
items, flask, Pyre + super, progression, level flow, boss, HUD bars, audio.

**Last run: 192 passed · 0 failed · 7 skipped — `SUITE PASS` (~11.7 s).** It found four real shipping
bugs on its first outings; full write-up in [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md).

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
trying combat without running the course: enemy spawn pads (Grunt/Heavy/Boss), item pedestals enumerated
from `Assets/Data/Items`, a jump/dash platforming corner, and a weapon rack.

`SandboxController` (`Assets/Scripts/Debug/SandboxController.cs`) adds, all `[ContextMenu]`-exposed:
`SpawnEnemyInFront(int)`, `SpawnDummy()` (aggro-locked, ~1M HP practice target), `ActivateBoss()` (the
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
