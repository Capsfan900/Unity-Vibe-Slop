# vibegame1 — agent instructions

First-person, **melee-only**, parry-focused speedrun platformer. Neon White level flow, Sekiro / Lies of P
deflect combat, dark-fantasy presentation. All code is namespace `VibeGame1`.

> **THIS FILE IS THE SINGLE SOURCE OF TRUTH, and it is tool-neutral.**
> `CLAUDE.md` imports it verbatim, so Claude Code, Codex, Cursor, Zed, Aider, Jules and anything else that
> reads `AGENTS.md` all get the same contract. **Edit this file, never a per-tool copy** — a project that
> says two different things to two different models will drift, and the drift is silent.
>
> Tool-specific extras (which CLI drives the Unity editor, what a "skill" or "subagent" means in your
> harness) live at the bottom under *Working in a tool other than Claude Code* and in `CLAUDE.md`.

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
| `/dashboard` | **Seeing everything at once.** Builds `Tools/dashboard/out/index.html`: every doc, the change log, test results, systems map, search. **Currently disabled** in `.claude/settings.local.json` — drop its `skillOverrides` entry to use it. |
| [docs/LEVEL-EDITOR.md](docs/LEVEL-EDITOR.md) | **The in-game level editor** (F10): keys, files, PLAY, EXPORT, and how it shares the campaign's piece factory. |
| [docs/HANDOFF.md](docs/HANDOFF.md) | **Picking up where the last chat stopped.** Rewritten every session: what is in flight, uncommitted, and what to do first. |
| [docs/DISTRIBUTION.md](docs/DISTRIBUTION.md) | **Cutting a playtest build or sharing a link.** `BuildRunner`, GitHub Pages (WebGL) and Releases (Windows), one-time GitHub settings, tracing a bug report to a build SHA. |
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

**Current counts live in [docs/VERIFICATION-REPORT.md](docs/VERIFICATION-REPORT.md), never here** — a number
copied into this always-loaded file goes stale silently and then lies to every session. The EditMode suite
covers shipped-asset arithmetic for the Marionette, the Revenant and the wands, the lock-on control law, the
parry impulse, the Pyre arc and level jump-arc clearance.

**Two ways a play-mode run lies, both of which have cost a session real time.** A session that has been
recompiled under is not a fresh one — a domain reload wipes every static without re-running `Awake`, so
`GameManager.I` is null and the suite reports failures that are not real. And a run taken against a
**paused world** reports ~49 plausible failures across unrelated systems, because every timing test is
reading a stopped clock. Check **both** `GameManager.I != null` **and** `Time.timeScale == 1` before you
believe any result.

`DebugHarness` and `FeatureTests` parry on a state transition — frame-perfect information no human has.
They prove the state machine, **never** that the game feels good or is fair.

## Dev keys — editor / development builds only

`4` dev blade · `F1` test menu · `F5` warp to boss · `F6` full restore · `F7` +1000 souls ·
`F8` god mode · `F10` level editor · `E` use item

## Driving the Unity editor

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

---

## The specialist briefs — `.claude/agents/*.md`

Eight standing briefs. **They are plain Markdown and they are useful to ANY model**, not just to a harness
that can spawn subagents. Each one carries the lane's ownership boundary, the craft rules it must hold and
the files it must not touch — that knowledge is the point, the delegation is just the delivery mechanism.

| Brief | Owns |
|---|---|
| `level-designer` | The SHAPE of a level: space, sightlines, pacing, the arc of a span, where a route opens and pinches, ramps and slide lines, where each enemy perch sits so its bolt crosses the line the player is running. Authors levels as DATA through `LevelDefinitionAuthoring`. |
| `enemy-designer` | A NEW enemy the project's own way: an `EnemyData` asset written by `DataFactory`, a prefab built by `PrefabFactory`, a moveset, and the smallest additive component if it does something no existing enemy does. |
| `combat-designer` | Review, critique and plans for combat, enemies, parry/deflect, hit response, difficulty and game feel. **Plans only by default** — it does not edit unless told to. |
| `vfx-art-team` | Effects, materials, shaders, colour, the light budget, and the readability of every tell. Never gameplay timing, combat resolution or movement. |
| `audio-engineer` | Every sound the game makes, the mix and the audio budget. Never gameplay timing or visuals. |
| `ui-designer` | The HUD, main menu, pause/settings, prompts, bars and every on-screen readout. Never gameplay code. |
| `editor-controls` | The in-game F10 level editor's CONTROLS only. |
| `unbuilt-asks` | Read-only auditor. Finds things the user asked for that were never built, by cross-checking CLAIMS (docs, remits, tooltips, commit bodies) against EXISTENCE in the code and the **shipped data**. |

**If your tool cannot spawn subagents, read the relevant brief and follow it yourself.** That is the whole
value; `unbuilt-asks.md` in particular is a procedure, not a personality.

## The skills — `.claude/skills/*/SKILL.md`

Also plain Markdown, also readable by any tool.

| Skill | What it encodes |
|---|---|
| `unity-editor` | How to drive the user's OPEN Unity editor over the MCP HTTP bridge, and the traps: the modal dialog that deadlocks the bridge, `execute_menu_item` silently doing nothing, reading back what a generator claims to have written. |
| `session-handoff` | How to end a session so the next one does not lose an hour: write `docs/HANDOFF.md`, commit, verify, and state exactly how to resume. |
| `dashboard` | Builds `Tools/dashboard/out/index.html` — every doc, the change log, test results, systems map, search. |

## Working in a tool other than Claude Code

Everything above applies unchanged. These are the substitutions:

| Claude Code | Anywhere else |
|---|---|
| `CLAUDE.md` auto-loads | Read `AGENTS.md` — Codex, Cursor, Zed, Aider and Jules load it automatically |
| The `Skill` tool | Read `.claude/skills/<name>/SKILL.md` and follow it |
| The `Agent` tool | Read `.claude/agents/<name>.md` and adopt that brief yourself, including its do-not-touch list |
| MCP `mcp__UnityMCP__*` tools | `python .claude/skills/unity-editor/mcp_call.py <tool> '<json>'` from the project root — plain JSON-RPC, no dependencies, works from any shell |

**Verifying without the editor.** `dotnet build Assembly-CSharp.csproj` and
`dotnet build Assembly-CSharp-Editor.csproj` from the project root compile against the real engine DLLs and
catch every syntax and type error — the `.csproj` files are Unity-generated. `Tools/level_arc_offline.py`
proves level geometry by parsing `LevelDefinitionAuthoring.cs` directly, and `Tools/measure_forge_fbx.py`
measures a forge FBX in Blender. None of the three needs Unity running.

**The rule that survives every tool change:** *a code default is not a shipped value.* Whatever model you
are, verify a number by reading the `.asset` / `.prefab` YAML, never the C# field initialiser. Checking the
C# instead has produced false "already done" conclusions in this project more than once.

