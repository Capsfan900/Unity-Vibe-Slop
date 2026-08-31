# vibegame1

First-person, **melee-only**, parry-focused speedrun platformer. Neon White level flow, Sekiro / Lies of P
deflect combat, dark-fantasy presentation. All code is namespace `VibeGame1`.

**This file is an index, deliberately small — it is loaded into every session.** Depth lives in `docs/`;
read only the one you need. See [docs/SESSION-PROTOCOL.md](docs/SESSION-PROTOCOL.md).

## Where to look

| Read this | When |
|---|---|
| [docs/ENGINEERING-LOG.md](docs/ENGINEERING-LOG.md) | **Anything behaves strangely.** Every past gotcha, its root cause and the invariant. Check here first. |
| `docs/DATAFLOW.md` | How each system actually flows, end to end. Read before changing any system. |
| `docs/BACKLOG.md` | Requested but not yet built, with the intended shape. |
| [docs/TOOLING.md](docs/TOOLING.md) | Before building any tool — it probably exists. Menus, tests, harness, debug keys, sandbox, configs. |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Changing systems or combat. Module map, event bus, singletons, feel contracts, art direction, audio. |
| [docs/AUTHORING.md](docs/AUTHORING.md) | **Adding a level, enemy, moveset or item.** Content is data — ScriptableObjects plus a menu item, not new code. |
| [docs/SESSION-PROTOCOL.md](docs/SESSION-PROTOCOL.md) | Session start/end checklist and token discipline. |
| [docs/VERIFICATION-REPORT.md](docs/VERIFICATION-REPORT.md) | What is proven vs unproven, current test results, and what still needs a human playtest. |
| [docs/multiplayer-system-design.md](docs/multiplayer-system-design.md) | Networking or backend work. Design only, not implemented. |
| [README.md](README.md) | Human-facing overview: controls, how to add content. |
| [CREDITS.md](CREDITS.md) | CC0 audio sources and licences. |

## Environment

- **Unity 6000.5.10f1** (Unity 6.5) — `C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe`
- **C# 9.** No `ref`-to-`in`, file-scoped namespaces, `required` members or list patterns.
- **URP.** Materials must use `Universal Render Pipeline/*` shaders — `Standard` renders **magenta**.
- **New Input System** only, via `Assets/InputSystem_Actions.inputactions`. No legacy `Input.GetAxis`.
- Build modules: **Windows Standalone** and **WebGL** only.
- `Library/` is gitignored — never commit it. Commit before large refactors.

## Hard rules
- **Adding or changing a system means updating its map in `docs/DATAFLOW.md` in the same change.** A map that lies is worse than no map.
- **New content is authored as data, not code.** New levels, enemies and movesets are assets built by a menu item — see `docs/AUTHORING.md` before writing another hardcoded builder.

1. **`TimeScaleController` is the only writer of `Time.timeScale`.** Player movement reads
   `TimeScaleController.PlayerDelta`, **never** `Time.deltaTime` — hitstop must never freeze the player.
2. **`InputReader` is the only script touching the Input System.**
3. **All combat resolves through `PlayerCombat.ReceiveAttack`.**
4. **Everything is regenerable.** Nothing in the scene is hand-authored — a wiped scene is a rebuild, not
   data loss. Hand-placed extras go under a `Level_Manual` / `Sandbox_Manual` root, which builders never touch.
5. **Never use `Image.fillAmount`** — a null-sprite UGUI `Image` silently ignores it. `BarView` drives
   RectTransform anchors.
6. **Never `IgnoreLayerCollision` a pair that needs triggers** — it suppresses `OnTriggerEnter` too.
7. **`Sfx` enum names are folder names** under `Resources/Audio/Sfx/`. Append only; never reorder or rename.
8. **Exit play mode before running any editor generator.**
9. **A code default is not a shipped value.** Changing a field initialiser does nothing to a
   ScriptableObject that already exists — rewrite it in `DataFactory` and assert it in `FeatureTests`.

## Rebuild pipeline — `VibeGame1/…`

`0. Rebuild Everything` runs steps 1-6 in the only order that works. Prefer it.

| Step | Function | Produces |
|---|---|---|
| 1. Project Setup | `ProjectSetup.Run()` | Layers (Player 6, Enemy 7, Interactable 8), physics matrix, HDR grading, volume profile, fog/light, `runInBackground` |
| 2. Create Materials | `MaterialFactory.CreateAll()` | `Assets/Materials/M_*.mat` |
| 3. Create Data | `DataFactory.CreateAll()` | ScriptableObjects — **overwrites Inspector tuning** |
| 4. Build Prefabs | `PrefabFactory.BuildAll()` | Player, Managers, enemies, boss, weapons, pickups |
| 5. Build HUD | `HudBuilder.Build()` | `Assets/Prefabs/HUD.prefab` |
| 6. Build Level | `LevelGreyboxBuilder.Build()` | `Level` root, NavMesh bake, scene instances |
| 7. Build Sandbox | `SandboxBuilder.Build()` | `Assets/Scenes/Sandbox.unity` |
| 9. Build Main Menu | `MainMenuBuilder.Build()` | `Assets/Prefabs/MainMenu.prefab` + `Assets/Scenes/MainMenu.unity`, **build index 0**. Rows come from `LevelRegistry`. Not in Rebuild Everything. |

Also: `Health Check` (read-only validator — run after any rebuild), `Run Feature Tests`,
`Open Test Level`, `Open Sandbox Scene`, `Rebuild NavMesh`.

Call from MCP as `VibeGame1.EditorTools.<Class>.<Method>()`.

## Verification

| Layer | How |
|---|---|
| Project state | `VibeGame1/Health Check` |
| Pure logic | MCP `run_tests`, `mode: EditMode` (`Assets/Editor/Tests/`) — works unfocused |
| Behaviour | Play mode, then `VibeGame1.EditorTools.FeatureTestRunner.Start()` and `.Poll()` |
| Whole fights | Play mode, then `VibeGame1.DebugHarness.Run("parry")` / `("boss")` / `("death")`, read `.Log` |

Current: EditMode **20/20**, feature suite **518 passed / 1 failed / 2 skipped**, from a fresh
play-mode session ([report](docs/VERIFICATION-REPORT.md)). **A session that has been recompiled under
is not a fresh one** — a domain reload wipes every static without re-running `Awake`, so `GameManager.I`
is null and the suite reports failures that are not real. Check `GameManager.I != null` first.

`DebugHarness` and `FeatureTests` parry on a state transition — frame-perfect information no human has.
They prove the state machine, **never** that the game feels good or is fair.

## Dev keys — editor / development builds only

`4` dev blade · `F1` test menu · `F5` warp to boss · `F6` full restore · `F7` +1000 souls ·
`F8` god mode · `E` use item

## MCP workflow

- **The Unity Editor must be open on this project** or no MCP tool works. If tools error, time out or
  return `no_unity_session`, check that before debugging anything else.
- Prefer MCP tools over blind file writes for scenes, prefabs and component wiring — hand-editing
  `.unity` / `.prefab` YAML or `.meta` GUIDs corrupts references.
- Scripts may be written as files, but expect a **domain reload**; the next call can time out while Unity
  recompiles. Re-issue rather than assuming failure.
- **Read the console after every script batch.** One compile error puts Unity in Safe Mode, which
  presents as "the project won't open, the scene is blank".
- `execute_code` compiles as **C# 6** — keep snippets plain.
