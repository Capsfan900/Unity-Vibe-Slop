---
name: unity-editor
description: Run this Unity project's tests, generators, menu items and camera captures from Claude Code through the user's OPEN editor over the MCP HTTP bridge. If no editor is running, stop and ask the user to open it - there is no headless fallback any more.
---

# Driving Unity from a session

**The user's rule (2026-09-03): everything goes through their open editor.** The old `-batchmode` copy of
the project was retired the same day: a second import fought their session for CPU and disk, and the
copy's diverging `.meta` GUIDs were a standing trap. If the editor is not running, say so and ask the
user to open it. Do not launch Unity yourself.

Check first (PowerShell, not Git Bash - `tasklist` filters miss the editor from Bash):

```powershell
Get-Process Unity -ErrorAction SilentlyContinue | Select-Object Id, MainWindowTitle
```

A window titled `vibegame1 - … - Unity 6.5` = go. Nothing = ask the user to open the project.

---

## The live editor over HTTP

The MCP server is **HTTP transport on `127.0.0.1`**, spawned by the editor as it loads. Claude Code's
own `mcp__UnityMCP__*` tools reach it when they connected at startup; when they did not (startup race -
the client gives up in ~7 s, the editor spawns the server minutes later), the server is still fine.
`/mcp` -> UnityMCP -> Reconnect fixes the client. Or skip the client entirely:

**`.claude/skills/unity-editor/mcp_call.py`** - plain JSON-RPC, no dependencies, one call per process.
It initialises a session, pins the real project's editor instance (selection is session-scoped, and a stray
second instance makes the server refuse every call until one is chosen), then runs the tool. Run it from
the project root - do not `cd` into the skill folder, a shell parked there locks the directory on Windows.

```
python .claude/skills/unity-editor/mcp_call.py --list
python .claude/skills/unity-editor/mcp_call.py --schema run_tests
python .claude/skills/unity-editor/mcp_call.py --resource mcpforunity://editor/state
python .claude/skills/unity-editor/mcp_call.py read_console '{"action":"get","types":["error"],"count":20,"format":"plain"}'
python .claude/skills/unity-editor/mcp_call.py execute_menu_item '{"menu_path":"VibeGame1/Health Check"}'
python .claude/skills/unity-editor/mcp_call.py refresh_unity '{}'
python .claude/skills/unity-editor/mcp_call.py manage_editor '{"action":"play"}'          # or "stop"
python .claude/skills/unity-editor/mcp_call.py execute_code '{"action":"execute","code":"return VibeGame1.EditorTools.FeatureTestRunner.Poll();"}'
python .claude/skills/unity-editor/mcp_call.py execute_code '{"action":"execute","code":"return VibeGame1.EditorTools.QuickTestRunner.RunQuick();"}'
python .claude/skills/unity-editor/mcp_call.py execute_code '{"action":"execute","code":"return VibeGame1.EditorTools.QuickTestRunner.RunFull();"}'
python .claude/skills/unity-editor/mcp_call.py --exec "return 1 + 1;"
python .claude/skills/unity-editor/mcp_call.py --menu VibeGame1/Health Check
python .claude/skills/unity-editor/mcp_call.py --play  # --stop exits play mode
```

`mcp_call.py` still exposes `--run-tests` / `--clear-tests` (they wrap MCP `run_tests` / `get_test_job`), but
do not use them on this project's EditMode suite — see "EditMode tests" below for why they hang.

**`instance_count: 0` from `mcpforunity://instances` (or `no_unity_session` from every call) with the editor
open** means the editor's bridge lost its startup handshake with the server — it happened on 2026-09-04 when
the transport gave up 40 s before the uvx server came up. `Assets/Editor/McpReconnect.cs` retries on every
domain reload, so saving any script re-arms it; so does **Tools → MCP Bootstrap → Reconnect Bridge** in the
editor. While it is down: measure a forge model with `Tools/measure_forge_fbx.py` (Blender, the forge tool's
own venv, Unity axes) and catch compile errors with `dotnet build Assembly-CSharp-Editor.csproj` at the repo
root — the csproj files are Unity-generated and reference the real engine DLLs.

**Read `editor/state` before doing anything.** If `is_playing` is true the user is at the controls - read-only
calls only. Entering play mode, running generators (hard rule 8) or changing scenes takes over their
session; the user has said live edits are fine, but do not enter play mode while they are.

### Compile

Script edits on disk -> `refresh_unity` -> poll `editor/state` until `is_compiling` is false -> `read_console`
for `error CS`. One compile error is Safe Mode. `execute_code` compiles as **C# 6** (no local functions,
no `$"{x:F0}"` in lambdas; every snippet needs `"action":"execute"` and a `return`). Ambiguous `Object`
in FeatureTests: write `UnityEngine.Object`.

### EditMode tests

**SAVE THE SCENE FIRST** - `manage_scene '{"action":"save"}'`. The runner opens a new untitled scene, and if
the open one is dirty (any prefab rebuild or generator dirties it) Unity raises a modal save dialog that
blocks its main thread: the job reports `failed to initialize`, every later bridge call times out, and the
window title reads `Untitled`. Only a human click clears it. See ENGINEERING-LOG, "The EditMode runner hangs
Unity behind a save dialog".

**Do not use MCP `run_tests` / `get_test_job` for this project.** `Unity.PerformanceTesting`'s prebuild setup
blocks on the Account API for 30 s while the bridge's job manager gives up at 15 s, so the job never starts.
Use `execute_code` to call `VibeGame1.EditorTools.QuickTestRunner.RunQuick()` (everything except the slow
`[Category("LevelLines")]` fixtures) or `.RunFull()` (the whole suite, current counts
in `docs/VERIFICATION-REPORT.md`). Both return immediately and run asynchronously; the completion signal is
the console line `[QuickTests] ... DONE run=N passed=N failed=N skipped=N ... xml=TestResults/EditMode-*.xml`.
Poll `read_console` for that line, or watch for a new file under `TestResults/` on disk, in one
`for … sleep 10 … grep -q` loop rather than chained sleeps. Do not enter play mode while a run is in progress.

### Behaviour tests (play mode)

```
manage_editor play  ->  wait ~20 s  ->  execute_code "return VibeGame1.EditorTools.FeatureTestRunner.Start();"
->  poll execute_code "return VibeGame1.EditorTools.FeatureTestRunner.IsDone() ? \"DONE\" : \"…\";" every 10 s
->  execute_code "return VibeGame1.EditorTools.FeatureTestRunner.FullReport();"  ->  manage_editor stop
```

The report comes back as a JSON string; parse it (`json.loads(...)["data"]["result"]`) rather than
grepping the escaped text. A session recompiled *under* play mode is not fresh (`GameManager.I` null);
stop, refresh, play again. The suite runs on whatever scene is open - check `active_scene` is
`Level_01.unity` first (a generator that opened the sandbox leaves it open).

### Generators, reports and captures

Every generator and capture is a menu item - `execute_menu_item` with the exact path, then read the
console for its success line, since a menu item can report success and run nothing. Current list:
`VibeGame1/0. Rebuild Everything`, `1. Project Setup` … `9. Build Main Menu`, `3b. Create Wands`,
`4a. Split Forge Animation Clips`, `4b. Build Mini-Bosses`, `4c. Build Spellbook Visual`,
`7. Build Sandbox Scene`, `8. Build Level From Definition`, `8a. Rework Level_01 (parkour first)`,
`8b. Build Level From Definition (headless)`, `10. Build Parry Recording Stages`, `Level Studio`,
`Health Check`, `Run Feature Tests`, `Run Quick EditMode Tests`, `Run Full EditMode Tests`, `Rebuild NavMesh`,
`Level Arc Report`, `Span 1/2/3 Wall-Run Report`, `Projectile Encounter Report`,
`Photograph Enemies / Weapons / The Sky / Wind-up Silhouettes / The New Routes / Span 1`, `Film the Whirl`,
`Audit Level Lights`, `Probe Forge Models`, `Export Current Level To Definition`, `Build/Windows`,
`Build/WebGL`, `Build/All`, `Build/Preflight (no build)`. Anything without a menu item: `execute_code`
calling `VibeGame1.EditorTools.<Class>.<Method>()`.

Generators write real assets into the real project - nothing to copy back, no GUID divergence. **Exit play
mode first** (hard rule 8). Before any generator that changes scenes, save the open scenes first
(`manage_scene '{"action":"save"}'` or `SaveOpenScenes()` from `execute_code`) — a modal save-or-discard
dialog deadlocks the bridge's main thread and presents as `Unity session not ready … ping not answered`,
which looks exactly like a lost bridge but is not one; Unity's CPU sits near zero while it waits. Diagnose a
suspected deadlock by enumerating Unity's top-level windows filtered to its PID, not by retrying.

A generator can run against the assembly Unity has already compiled, not the file you just wrote —
`AssetDatabase.Refresh()` (or `refresh_unity`) returns before compilation starts, so an `isCompiling == false`
check taken right after it can pass while the OLD code is still loaded. Probe for the new symbol by
reflection and loop until it resolves, rather than trusting the refresh call's return.

A long build (`Build/Windows`, `Build/WebGL`, `Build/All`) can outlast the websocket connection and drop the
call silently. Read `Builds/last-build.txt` for the real result, not the MCP call's return value — see
`docs/DISTRIBUTION.md`, which also documents `Tools/unity-cli/Invoke-VibeGame.ps1` as the preferred non-MCP
path for a build (`preflight`, `build-webgl`, `build-windows`).

Captures in the editor render with the real pipeline. `Awake` still does not run in edit mode, so
`EnemyVisuals.Setup(data)` by hand still applies, and any capture should discard its first
`Camera.Render()` (see ENGINEERING-LOG, the orange Revenant).

---

## Reporting honestly

Say which half you did. "N/N EditMode in the editor" and "N/N FeatureTests in play mode" (current counts in
`docs/VERIFICATION-REPORT.md`, never copied into this file) are proof of arithmetic and state machines.
Neither is proof that the game feels good; only the user playing is.
