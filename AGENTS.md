# vibegame1 — agent instructions

First-person, **melee-only**, parry-focused speedrun platformer. Neon White level flow, Sekiro / Lies of P
deflect combat, dark-fantasy presentation. All code is namespace `VibeGame1`. Ship target: **Windows exe**.

**This file is the single, tool-neutral contract** (`CLAUDE.md` imports it; Codex, Cursor, Zed, Aider and Jules
read it directly). Edit rules HERE, never in a per-tool copy. It loads into every session, so it holds only
what every session needs; depth lives in `docs/`. Start a session at [docs/HANDOFF.md](docs/HANDOFF.md).

**Contracts load once, at session start.** When `AGENTS.md`, `CLAUDE.md`, a brief in `.claude/agents/` or a
skill changes, a running session is still carrying the old copy. Finish or park the task in flight, write
`docs/HANDOFF.md` (what is in flight, the next step), commit, then **clear context** (`/clear` or a new
session) and **resume from HANDOFF.md** so the current, slimmer contracts are what you work under. A spawned
subagent always reads the current brief; never resume an old agent across a contract change.

## Where to look

| Read this | When |
|---|---|
| [docs/HANDOFF.md](docs/HANDOFF.md) | **First, every session.** What is in flight, uncommitted, preserved user files, what to do next. |
| [docs/ENGINEERING-LOG.md](docs/ENGINEERING-LOG.md) | **Anything behaves strangely.** Past gotchas, root causes, invariants. |
| `docs/DATAFLOW.md` | Before changing any system: how it flows end to end. Update it in the same change. |
| [docs/TOOLING.md](docs/TOOLING.md) | Before building a tool — it probably exists. Menus, tests, harness, debug keys, subagent teams. |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Systems/combat changes: module map, event bus, singletons, feel contracts. |
| [docs/MOVEMENT-PRINCIPLES.md](docs/MOVEMENT-PRINCIPLES.md) | The motor, traversal pieces, levels, the movement HUD. |
| [docs/ANIMATION-VFX.md](docs/ANIMATION-VFX.md) | Any animation, viewmodel pose or effect; the bloom/light budget. |
| [docs/AUTHORING.md](docs/AUTHORING.md) · [docs/LEVEL-AUTHORING-TUTORIAL.md](docs/LEVEL-AUTHORING-TUTORIAL.md) | Adding a level, enemy, moveset or item (content is data). |
| [docs/LEVEL-VOCABULARY.md](docs/LEVEL-VOCABULARY.md) | Naming anything in a level: zone then stable `objectId` ("T4, `Spawn_T4_Surge_2`"). Never rename an authored ID. |
| [docs/PARRY-CHOREOGRAPHY.md](docs/PARRY-CHOREOGRAPHY.md) | Placing projectiles to a run's rhythm (`timing prime`, key `0`, generated parry modules). |
| [docs/LEVEL-EDITOR.md](docs/LEVEL-EDITOR.md) | The in-game F10 editor and the Level Studio draft bridge. |
| [docs/VERIFICATION-REPORT.md](docs/VERIFICATION-REPORT.md) | Current test counts, proven vs unproven, what needs a human playtest. |
| `docs/BACKLOG.md` · [docs/ASTRA-SYSTEMS-AUDIT-2026-09-07.md](docs/ASTRA-SYSTEMS-AUDIT-2026-09-07.md) | Requested-but-unbuilt work; open audit bugs. |
| [docs/DISTRIBUTION.md](docs/DISTRIBUTION.md) | Cutting a playtest build, GitHub Release, tracing a bug report to a build SHA. |
| [docs/HUMAN-DEVELOPMENT-GUIDE.md](docs/HUMAN-DEVELOPMENT-GUIDE.md) · [docs/CODE-TREE.md](docs/CODE-TREE.md) | The human's routing table; where a file lives. |
| [README.md](README.md) | Player controls, dev keys (private console passphrase), how to add content. |

## Environment

- **Unity 6000.5.10f1**, URP (materials must use `Universal Render Pipeline/*`; `Standard` renders magenta),
  **New Input System** only via `Assets/InputSystem_Actions.inputactions`.
- **C# 9** in project code (no file-scoped namespaces, `required`, list patterns). MCP `execute_code` compiles **C# 6**.
- `Library/` is gitignored. Commit before large refactors. WebGL/Pages are legacy — do not run them unless asked.

## Hard rules

**Roles and commits**
- **Authorship is by role, not model brand.** Only the **lead** (the frontier model the user runs as lead) may
  change a core system — the motor, parry/combat resolution, `TimeScaleController`, `InputReader`, enemy
  brains — and names each changed core file in the commit body. User-reported bug fixes are in scope for anyone.
- **Workers refine, never invent.** Non-lead workers (subagents, specialist briefs) improve what exists; a new
  mechanic, input, resource or screen is proposed in the report and stops there. Workers verify offline; the
  lead owns the Unity editor, re-runs the named generators and both suites, and commits each pass as ONE commit.
  Tag before a batch (`pre-<theme>-<date>`) so a regression is one `git revert`.
- **Every commit names role AND model:** subject `[role]` (`[Astra]`, `[level-designer]`), body ends with a
  `Model:` trailer naming the model that actually ran, e.g. `Model: Opus 5 (Claude Code)`.
- **Fence files.** Two workers never edit the same file; every brief carries a do-not-touch list.

**Engine invariants**
1. **`TimeScaleController` is the only writer of `Time.timeScale`.** Player movement reads `PlayerDelta`, never
   `Time.deltaTime` — hitstop must never freeze the player.
2. **`InputReader` is the only script touching the Input System.**
3. **All combat resolves through `PlayerCombat.ReceiveAttack`.**
4. **Everything is regenerable.** Nothing in a scene is hand-authored; hand-placed extras go under a
   `Level_Manual` / `Sandbox_Manual` root, which builders never touch.
5. **Never use `Image.fillAmount`** (a null-sprite UGUI `Image` ignores it); `BarView` drives anchors.
6. **Never `IgnoreLayerCollision` a pair that needs triggers** — it suppresses `OnTriggerEnter` too.
7. **`Sfx` enum names are folder names** under `Resources/Audio/Sfx/`. Append only; never reorder or rename.
8. **Exit play mode before any generator — and never compile or refresh while the USER is in play mode.**
   Check `EditorApplication.isPlaying` first; if they are playing, ask before stopping it.
9. **A code default is not a shipped value.** Changing a field initialiser does nothing to an existing asset:
   write it in `DataFactory` / the factory, assert it in a test, and verify numbers in the `.asset`/`.prefab` YAML.
10. **A traversal piece never writes a velocity.** It calls the motor's entry points (`Launch`, `RearmDash`, `TouchWater`).
- **New content is data, not code** (see `docs/AUTHORING.md`). **Changing a system updates `docs/DATAFLOW.md`** in the same change.
- **Parry contract:** no wind-up under 0.45 s; the cue fires `cueLead` 0.28 s before every impact. Wind-ups only get longer.

## Rebuild pipeline — `VibeGame1/…`

`0. Rebuild Everything` runs 1–6 (with 3b) in the only working order. Call from MCP as
`VibeGame1.EditorTools.<Class>.<Method>()`. Save open scenes before each scene-changing step.

| Step | Function | Notes |
|---|---|---|
| 1 Project Setup | `ProjectSetup.Run()` | Layers (Player 6, Enemy 7, Interactable 8), physics, grading, fog |
| 2 Materials | `MaterialFactory.CreateAll()` | `Assets/Materials/M_*.mat` |
| 3 Data | `DataFactory.CreateAll()` | ScriptableObjects; **overwrites Inspector tuning**. Never run alone: follow with 3b and 4 |
| 3b Wands | `WandFactory.CreateAll()` | Must precede 4 (else ripostes fall back to the melee deathblow) |
| 4 Prefabs | `PrefabFactory.BuildAll()` | Player, managers, enemies, boss, weapons, pickups |
| 4a Clip split | `ForgeClipSplitter.SplitAll()` | After importing/re-exporting a forge FBX. Not in 0 |
| 4b Mini-bosses | `MiniBossFactory.CreateAll()` | The `Legendary_*` prefabs + animators. Not in 0 |
| 4c Spellbook | `SpellbookFactory.CreateAll()` | Not in 0 |
| 5 HUD | `HudBuilder.Build()` | `Assets/Prefabs/HUD.prefab` |
| 6 Level | `LevelGreyboxBuilder.Build()` | Level root, NavMesh bake |
| 7 Sandbox | `SandboxBuilder.Build()` | `Assets/Scenes/Sandbox.unity` |
| 8 / 8a | `LevelDefinitionBuilder.BuildSelected()` / `LevelDefinitionAuthoring.ReworkLevel01()` | Level scene from its `LevelDefinition`; 8a rewrites Level 1's data. Then `LevelGreyboxBuilder.RebuildNavMesh()`. Not in 0 |
| 9 Main Menu | `MainMenuBuilder.Build()` | Build index 0; rows from `LevelRegistry`. Not in 0 |
| 10 Recording stages | `ParryRecordingStageFactory.CreateAll()` | Not in 0 |

Also: `Level Studio` window, `Projectile Encounter Report` (re-run after moving/retuning any shooter),
`Level Arc Report`, `Health Check` (read-only), `Build/Windows` (`BuildRunner.Preflight()` then `.Windows()`).

**Generator churn:** 3→4b re-serialise the Legendary animator controllers, `Player.prefab`, `VM_Spellbook.prefab`
and `pshooter_enemy01.prefab` in a new order with identical content. A balanced `git diff --numstat` on those is
churn — `git checkout` it, do not commit it.

## Verification

| Layer | How |
|---|---|
| Project state | `VibeGame1/Health Check` |
| Pure logic | `QuickTestRunner.RunQuick()` / `.RunFull()`, then poll for a new `TestResults/*.xml` (MCP `run_tests` cannot start this suite) |
| Behaviour | Play mode, `FeatureTestRunner.Start()` (optionally `Start("filter")`), poll `.Poll()` until `done=True` |
| Whole fights | Play mode, `DebugHarness.Run("parry" / "boss" / "death")`, read `.Log` |

- **Iterate on the scoped tests; run both full suites once before each commit.** Counts live in
  `docs/VERIFICATION-REPORT.md`, never here.
- **Three ways a play-mode run lies.** (a) A domain reload during play (a recompile, or the MCP bridge
  reconnecting) wipes statics without `Awake`: read `FeatureTestRunner.Start()`'s RETURN value — it refuses with
  an ERROR when `GameManager.I` is null; stop and re-enter play mode. (b) A paused world (`Time.timeScale == 0`)
  reports dozens of plausible failures across unrelated systems. (c) `LockOn_AssistRecentresTarget` is a known
  real-time flake — rerun before believing it.
- `DebugHarness` / `FeatureTests` parry on a state transition, frame-perfect: they prove the state machine,
  **never** that the game feels good or is fair. Feel is the user's call.

## Driving the Unity editor

The **editor must be open** (there is no headless copy). Use MCP `mcp__UnityMCP__*`, or from any shell
`python .claude/skills/unity-editor/mcp_call.py <tool> '<json>'` (bare tool names, e.g. `execute_code`). Details
and traps (modal dialogs deadlock the bridge; a generator runs the OLD assembly until the new symbol resolves by
reflection; read the console after every script batch — one compile error means Safe Mode) are in
`.claude/skills/unity-editor/SKILL.md`. Prefer tools over hand-editing `.unity`/`.prefab` YAML or `.meta` GUIDs.

**Offline:** `dotnet build Assembly-CSharp.csproj` / `Assembly-CSharp-Editor.csproj` catch compile errors without
Unity; `Tools/level_arc_offline.py` proves level geometry; `Tools/measure_forge_fbx.py` measures a forge FBX.

## Specialist briefs and skills (plain Markdown — any model can follow them)

Briefs in `.claude/agents/*.md`, each with its ownership boundary and do-not-touch list: `level-designer` (level
shape, Level Studio use), `enemy-designer` (a new enemy as data + prefab), `combat-designer` (combat review and
plans; plans only by default), `vfx-art-team`, `audio-engineer`, `ui-designer`, `editor-controls` (F10 editor
controls only), `unbuilt-asks` (read-only audit of asked-but-unbuilt work). Skills in `.claude/skills/*/SKILL.md` (read the file directly if a harness has one disabled):
`unity-editor`, `session-handoff`, `dashboard`, `astra-engineering-company` (cost-aware lead/worker orchestration).

**Without a Claude Code harness:** read the brief or skill and follow it yourself, including its do-not-touch list.
