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
| [docs/MOVEMENT-PRINCIPLES.md](docs/MOVEMENT-PRINCIPLES.md) | **Touching the motor, a traversal piece, a level or the movement HUD.** What makes movement satisfying to pilot, mapped onto this project's systems. |
| [docs/ANIMATION-VFX.md](docs/ANIMATION-VFX.md) | **Touching an animation, a viewmodel pose or any effect.** Craft principles, an audit of what this project gets right, and the open gaps. |
| [docs/LEVEL-AUTHORING-TUTORIAL.md](docs/LEVEL-AUTHORING-TUTORIAL.md) | **Making a level.** Author it as a data asset in the Unity editor, prove it with the arc report and tests, use the in-game editor (F10) only to feel and tweak. |
| [docs/AUTHORING.md](docs/AUTHORING.md) | **Adding a level, enemy, moveset or item.** Content is data — ScriptableObjects plus a menu item, not new code. |
| [docs/SESSION-PROTOCOL.md](docs/SESSION-PROTOCOL.md) | Session start/end checklist and token discipline. |
| `/dashboard` | **Seeing everything at once.** Builds `Tools/dashboard/out/index.html`: every doc, the change log, test results, systems map, search. |
| [docs/LEVEL-EDITOR.md](docs/LEVEL-EDITOR.md) | **The in-game level editor** (F10): keys, files, PLAY, EXPORT, and how it shares the campaign's piece factory. |
| [docs/HANDOFF.md](docs/HANDOFF.md) | **Picking up where the last chat stopped.** Rewritten every session: what is in flight, uncommitted, and what to do first. |
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
- **Model boundary (the user's rule, 2026-09-03).** The game's systems were written by **Fable**. A session
  running as **Opus** ADDS features - new files, new components, new content - and does **not** rewrite,
  retune or refactor a system Fable wrote unless the user says so in that session. Bug fixes the user
  explicitly reports ("you reversed the lean") are fixes, not overrides, and are in scope. When a feature
  genuinely cannot be added without changing a Fable system, name the file and the line and ask first.
  The user's words: *"don't touch any core system written by Fable ... only let Opus override Fable-made
  game systems if I say."*
- **Regression guard for any work not done by Fable or Opus (the user's rule, 2026-09-06).** Sonnet
  workers, subagents and any other model REFINE systems Fable and Opus built; they never invent a mechanic,
  input, resource or screen (propose it in the report and stop). They verify offline only (`dotnet build`);
  the lead session owns the Unity editor. The lead commits each such pass as ONE commit prefixed with the
  worker's name after re-running the generators the report names and both suites, and tags the tree before a
  batch (`pre-<theme>-<date>`), so any regression is a single `git revert`. Details: `docs/TOOLING.md`
  "Subagent teams".
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
10. **A traversal piece never writes a velocity.** Balloons, water and any future launcher call the
   motor's entry points (`Launch`, `RearmDash`, `TouchWater`); `FirstPersonMotor` decides what the body does.

## Rebuild pipeline — `VibeGame1/…`

`0. Rebuild Everything` runs steps 1-6 in the only order that works. Prefer it.

| Step | Function | Produces |
|---|---|---|
| 1. Project Setup | `ProjectSetup.Run()` | Layers (Player 6, Enemy 7, Interactable 8), physics matrix, HDR grading, volume profile, fog/light, `runInBackground` |
| 2. Create Materials | `MaterialFactory.CreateAll()` | `Assets/Materials/M_*.mat` |
| 3. Create Data | `DataFactory.CreateAll()` | ScriptableObjects — **overwrites Inspector tuning** |
| 4. Build Prefabs | `PrefabFactory.BuildAll()` | Player (with `PlayerBody` legs), Managers, enemies, boss, weapons, pickups |
| 4a. Split Forge Animation Clips | `ForgeClipSplitter.SplitAll()` | Named `AnimationClip`s from each `Assets/Enemies/*.clips.json`. **Not in Rebuild Everything** — run after importing or re-exporting an animated forge FBX |
| 4b. Build Mini-Bosses | `MiniBossFactory.CreateAll()` | The six `Legendary_*` prefabs, plus the generated Animator controller and named-clip table for any animated one. **Not in Rebuild Everything** |
| 5. Build HUD | `HudBuilder.Build()` | `Assets/Prefabs/HUD.prefab` |
| 6. Build Level | `LevelGreyboxBuilder.Build()` | `Level` root, NavMesh bake, scene instances |
| 7. Build Sandbox | `SandboxBuilder.Build()` | `Assets/Scenes/Sandbox.unity` |
| 9. Build Main Menu | `MainMenuBuilder.Build()` | `Assets/Prefabs/MainMenu.prefab` + `Assets/Scenes/MainMenu.unity`, **build index 0**. Rows come from `LevelRegistry`. Not in Rebuild Everything. |

Also: `Health Check` (read-only validator — run after any rebuild), `Run Feature Tests`,
`Run Quick EditMode Tests` (the EditMode suite minus the slow `[Category("LevelLines")]` fixtures — 400 tests in ~6 s
against the full 538 in ~194 s) and `Run Full EditMode Tests`, `Open Test Level`, `Open Sandbox Scene`, `Rebuild NavMesh`.

Call from MCP as `VibeGame1.EditorTools.<Class>.<Method>()`.

## Verification

| Layer | How |
|---|---|
| Project state | `VibeGame1/Health Check` |
| Pure logic | MCP `run_tests`, `mode: EditMode` (`Assets/Editor/Tests/`) — works unfocused |
| Behaviour | Play mode, then `VibeGame1.EditorTools.FeatureTestRunner.Start()` and `.Poll()` |
| Whole fights | Play mode, then `VibeGame1.DebugHarness.Run("parry")` / `("boss")` / `("death")`, read `.Log` |

Current: EditMode **558/558** (full, 2026-09-05 10:5x; quick set 420/420), feature suite **747 / 747 / 0 skipped** (2026-09-05, fresh session on the reworked Level_01 with 32 m/s bolts) from a fresh play-mode session on Level_01 ([report](docs/VERIFICATION-REPORT.md)). The EditMode suite
grew from 32 in one session and covers shipped-asset arithmetic for the Marionette, the Revenant and the
wands, the lock-on control law, the parry impulse, the Pyre arc and level jump-arc clearance. **A session that has been recompiled under
is not a fresh one** — a domain reload wipes every static without re-running `Awake`, so `GameManager.I`
is null and the suite reports failures that are not real. Check `GameManager.I != null` first.

`DebugHarness` and `FeatureTests` parry on a state transition — frame-perfect information no human has.
They prove the state machine, **never** that the game feels good or is fair.

## Dev keys — editor / development builds only

`4` dev blade · `F1` test menu · `F5` warp to boss · `F6` full restore · `F7` +1000 souls ·
`F8` god mode · `F10` level editor · `E` use item

## MCP workflow

- **The Unity Editor must be open on this project** or no MCP tool works. If tools error, time out or
  return `no_unity_session`, check that before debugging anything else. If the editor IS open and still
  says `no_unity_session`, the bridge lost its startup handshake: save any script (a domain reload
  re-arms it via `McpReconnect`) or run **Tools → MCP Bootstrap → Reconnect Bridge**.
- Prefer MCP tools over blind file writes for scenes, prefabs and component wiring — hand-editing
  `.unity` / `.prefab` YAML or `.meta` GUIDs corrupts references.
- Scripts may be written as files, but expect a **domain reload**; the next call can time out while Unity
  recompiles. Re-issue rather than assuming failure.
- **Read the console after every script batch.** One compile error puts Unity in Safe Mode, which
  presents as "the project won't open, the scene is blank".
- `execute_code` compiles as **C# 6** — keep snippets plain.
