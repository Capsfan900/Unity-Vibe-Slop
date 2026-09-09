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
python .claude/skills/unity-editor/mcp_call.py run_tests '{"mode":"EditMode"}'            # returns a job_id
python .claude/skills/unity-editor/mcp_call.py get_test_job '{"job_id":"<id>","include_failed_tests":true}'
python .claude/skills/unity-editor/mcp_call.py --run-tests EditMode  # start + poll in ONE MCP session
python .claude/skills/unity-editor/mcp_call.py --exec "return 1 + 1;"
python .claude/skills/unity-editor/mcp_call.py --menu VibeGame1/Health Check
python .claude/skills/unity-editor/mcp_call.py --play  # --stop exits play mode
```

Use `--run-tests` from clients whose MCP helper creates one session per process. Test job handles are
session-scoped; starting in one process and polling from another can return `Invalid request parameters`
even while the editor is healthy. `--clear-tests` clears an orphan only after the editor has finished.

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

`run_tests` starts a job and returns immediately. Poll `get_test_job` in the same MCP session until `status`
is `succeeded` / `failed`; current counts live in `docs/VERIFICATION-REPORT.md`. The full suite takes a few
minutes in the editor. Use one
`for … sleep 10 … grep -q` loop in a single Bash call rather than chained sleeps. Do not enter play mode
while a job is running.

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
console for its success line. Current list: `VibeGame1/0. Rebuild Everything`, `1. Project Setup` … `9. Build
Main Menu`, `4a. Split Forge Animation Clips`, `4b. Build Mini-Bosses`, `8. Build Level From Definition`,
`Health Check`, `Run Feature Tests`, `Rebuild NavMesh`, `Level Arc Report`, `Span 1/2/3 Wall-Run Report`,
`Photograph Enemies / Weapons / The Sky / Wind-up Silhouettes / The New Routes / Span 1`, `Film the Whirl`,
`Audit Level Lights`, `Probe Forge Models`, `Export Current Level To Definition`. Anything without a menu
item: `execute_code` calling `VibeGame1.EditorTools.<Class>.<Method>()`.

Generators write real assets into the real project - nothing to copy back, no GUID divergence. **Exit play
mode first** (hard rule 8).

Captures in the editor render with the real pipeline. `Awake` still does not run in edit mode, so
`EnemyVisuals.Setup(data)` by hand still applies, and any capture should discard its first
`Camera.Render()` (see ENGINEERING-LOG, the orange Revenant).

---

## Reporting honestly

Say which half you did. "408/408 EditMode in the editor" and "698/0 FeatureTests in play mode" are proof
of arithmetic and state machines. Neither is proof that the game feels good; only the user playing is.
