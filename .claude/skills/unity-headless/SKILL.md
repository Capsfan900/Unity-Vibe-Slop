---
name: unity-headless
description: Run this Unity project's tests, generators, menu items and camera captures from Claude Code - through the OPEN editor over the MCP HTTP bridge (the default, and the only path when the user's editor is up), or, only when no editor is running, in a disposable -batchmode copy.
---

# Driving Unity from a session

**The user's rule (2026-09-03): when their editor is open on the project, everything goes through that
editor.** No `-batchmode` copy alongside it — a second import fights their session for CPU and disk.
The headless copy in section 3 exists only for a machine with no editor running.

Check first:

```powershell
Get-Process Unity -ErrorAction SilentlyContinue | Select-Object Id, MainWindowTitle
Get-NetTCPConnection -State Listen -LocalPort 8090 -ErrorAction SilentlyContinue
```

A window titled `vibegame1 - … - Unity 6.5` plus port 8090 listening = use section 1. Nothing = section 3.

---

## 1. The live editor over HTTP

The MCP server is **HTTP transport on `127.0.0.1:8090`**, spawned by the editor as it loads. Claude Code's
own `mcp__UnityMCP__*` tools reach it when they connected at startup; when they did not (startup race —
the client gives up in ~7 s, the editor spawns the server minutes later), the server is still fine.
`/mcp` → UnityMCP → Reconnect fixes the client. Or skip the client entirely:

**`.claude/skills/unity-headless/mcp_call.py`** — plain JSON-RPC, no dependencies, one call per process.
It initialises a session, pins the real project's editor instance (selection is session-scoped, and a stray
second instance makes the server refuse every call until one is chosen), then runs the tool.

```
python mcp_call.py --list
python mcp_call.py --resource mcpforunity://editor/state
python mcp_call.py read_console '{"action":"get","types":["error"],"count":20,"format":"plain"}'
python mcp_call.py execute_menu_item '{"menu_path":"VibeGame1/Health Check"}'
python mcp_call.py refresh_unity '{}'
python mcp_call.py manage_editor '{"action":"play"}'          # or "stop"
python mcp_call.py execute_code '{"action":"execute","code":"return VibeGame1.EditorTools.FeatureTestRunner.Poll();"}'
python mcp_call.py run_tests '{"mode":"EditMode"}'            # returns a job_id
python mcp_call.py get_test_job '{"job_id":"<id>","include_failed_tests":true}'
```

**Read `editor/state` before doing anything.** If `is_playing` is true the user is at the controls — read-only
calls only. Entering play mode, running generators (hard rule 8) or changing scenes takes over their
session; the user has said live edits are fine, but do not enter play mode while they are.

### Compile

Script edits on disk → `refresh_unity` → wait ~45 s → `read_console` for `error CS`. One compile error is
Safe Mode. `execute_code` compiles as **C# 6** (no local functions, no `$"{x:F0}"` in lambdas; every
snippet needs `"action":"execute"` and a `return`). Ambiguous `Object` in FeatureTests: write
`UnityEngine.Object`.

### EditMode tests

`run_tests` starts a job and returns immediately. Poll `get_test_job` until `status` is `succeeded` /
`failed`; the full 382-test suite takes ~3 min in the editor. Use one `until … grep -q` loop in a single
Bash call rather than chained sleeps.

### Behaviour tests (play mode)

```
manage_editor play  →  wait ~20 s  →  execute_code "VibeGame1.EditorTools.FeatureTestRunner.Start(); return \"ok\";"
→  wait ~80 s  →  execute_code "return VibeGame1.EditorTools.FeatureTestRunner.FullReport();"  →  manage_editor stop
```

The report comes back as a JSON string; parse it (`json.loads(...)["data"]["result"]`) rather than
grepping the escaped text. A session recompiled *under* play mode is not fresh (`GameManager.I` null);
stop, refresh, play again.

### Generators, reports and captures

Every generator and capture is a menu item — `execute_menu_item` with the exact path, then read the
console for its success line. Current list: `VibeGame1/0. Rebuild Everything`, `1. Project Setup` … `9. Build
Main Menu`, `4a. Split Forge Animation Clips`, `4b. Build Mini-Bosses`, `8. Build Level From Definition`,
`Health Check`, `Run Feature Tests`, `Rebuild NavMesh`, `Level Arc Report`, `Span 1/2/3 Wall-Run Report`,
`Photograph Enemies / Weapons / The Sky / Wind-up Silhouettes / The New Routes / Span 1`, `Film the Whirl`,
`Audit Level Lights`, `Probe Forge Models`, `Export Current Level To Definition`. Anything without a menu
item: `execute_code` calling `VibeGame1.EditorTools.<Class>.<Method>()`.

Generators write real assets into the real project — nothing to copy back, no GUID divergence, which is
the other reason the editor path beats the copy. **Exit play mode first** (hard rule 8).

Captures in the editor render with the real pipeline, so the "discard the first `Camera.Render()`" trap
below mostly does not bite; `Awake` still does not run in edit mode, so `EnemyVisuals.Setup(data)` by hand
still applies.

---

## 2. Reporting honestly

Say which half you did. "382/382 EditMode in the editor" and "667/0 FeatureTests in play mode" are proof
of arithmetic and state machines. Neither is proof that the game feels good; only the user playing is.

---

## 3. The headless copy — ONLY when no editor is running

### Set up (once)

```powershell
$SRC = "C:\Users\tyler\Main Storage\vibegame1"
$DST = "C:\Temp\claude\<your-name>\proj"
robocopy "$SRC\Assets"          "$DST\Assets"          /E /MT:16 /NFL /NDL /NJH /NJS /NP
robocopy "$SRC\Packages"        "$DST\Packages"        /E /NFL /NDL /NJH /NJS /NP
robocopy "$SRC\ProjectSettings" "$DST\ProjectSettings" /E /NFL /NDL /NJH /NJS /NP
```

~19 MB; Unity builds its own `Library` on first launch (several minutes, once). `robocopy` exits non-zero
on success (1 = copied, 3 = copied + extra). Re-sync before each run with `/PURGE` on `Assets`.

### EditMode suite

```powershell
$U = "C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe"
Start-Process -FilePath $U -Wait -ArgumentList @(
  "-batchmode","-projectPath",$DST,
  "-runTests","-testPlatform","EditMode",
  "-testResults","$DST\r.xml","-logFile","$DST\b.log")
[xml]$x = Get-Content "$DST\r.xml"; $r = $x.'test-run'
"total=$($r.total) passed=$($r.passed) FAILED=$($r.failed)"
$x.SelectNodes("//test-case[@result='Failed']") | % { $_.name; $_.failure.message.'#cdata-section' }
```

Any editor method: `-executeMethod VibeGame1.EditorTools.DataFactory.CreateAll -quit`. `Unity.exe`
detaches — always `-Wait`. The copy cannot enter play mode: no `FeatureTests`, no `DebugHarness`.

### The five traps

1. **`-executeMethod` silently does nothing if ANY script fails to compile.** Check the log:
   `Get-Content "$DST\d.log" | Select-String ": error CS"`.
2. **`/PURGE` deletes anything that exists only in the copy.** Sync, THEN generate, THEN copy back.
3. **Generated assets are committed artefacts — copy them back with their `.meta`**, or GUIDs diverge.
4. **A NEW script gets a different `.meta` GUID in each project.** A prefab built in the copy against it
   is a missing script back home. Copy the real `.cs.meta` into the copy first; assert with a test that
   resolves the TYPE (`RevenantDataTests.ThePrefabActuallyCarriesTheAura`).
5. **Scene builders refuse to run with no scene open.** `-batchmode` starts empty; a headless scene build
   needs an entry point that opens the scene (`8b. Build Level From Definition (headless)`).

### Captures in the copy

Discard the first `Camera.Render()` (pipeline not set up; one Revenant came back solid orange). `Awake`
never runs in edit mode. Runtime-spawned particles are absent. `OpenScene` Single mode unloads unused
assets — load assets AFTER opening the scene and read the builder's success line.

**Port 6400 is a red herring** — it is the stdio transport's listener and is correctly never bound on
this HTTP install.
