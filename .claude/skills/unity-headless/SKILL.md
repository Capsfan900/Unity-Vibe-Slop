---
name: unity-headless
description: Run this Unity project's tests, generators and camera captures WITHOUT the editor - in a disposable copy driven by -batchmode. Use when the editor is busy or in play mode, when several agents need Unity at once, or when the MCP bridge is unavailable. Also covers driving the live editor over the MCP HTTP bridge directly.
---

# Running Unity headless

Two ways to reach Unity without going through Claude Code's MCP tools. They do different jobs and the
difference matters.

| | Headless copy (`-batchmode`) | Live bridge (HTTP) |
|---|---|---|
| EditMode tests | yes | yes |
| Generators (`Create Data`, `Build Mini-Bosses`, …) | yes | yes |
| Camera captures | yes | yes |
| **Works while the user is in play mode** | **yes** | no |
| **Several agents at once** | **yes** | no — one editor |
| **Play mode** (`FeatureTests`, `DebugHarness`) | **no** | **yes** |
| Reads the user's real console / scene | no | yes |

**The user's rule (2026-09-03): do NOT run a headless copy while their editor is open on the project.**
Use the live bridge (section 4) — `run_tests` for EditMode, play mode for `FeatureTests`. The copy is for
when there is no editor at all.

**The copy proves arithmetic and assets. Only the bridge can run a frame of gameplay.** Do not describe
work verified solely in a copy as "verified" without saying which half you did.

---

## 1. The headless copy

### Set up (once per agent)

```powershell
$SRC = "C:\Users\tyler\Main Storage\vibegame1"
$DST = "C:\Temp\claude\<your-name>\proj"
robocopy "$SRC\Assets"          "$DST\Assets"          /E /MT:16 /NFL /NDL /NJH /NJS /NP
robocopy "$SRC\Packages"        "$DST\Packages"        /E /NFL /NDL /NJH /NJS /NP
robocopy "$SRC\ProjectSettings" "$DST\ProjectSettings" /E /NFL /NDL /NJH /NJS /NP
```

~19 MB. Unity builds its own `Library` on first launch — several minutes, once. `robocopy` exits with a
**non-zero code on success** (1 = files copied, 3 = copied + extra); do not treat that as failure.

### Re-sync before each verification

```powershell
robocopy "$SRC\Assets" "$DST\Assets" /E /MT:16 /NFL /NDL /NJH /NJS /NP /PURGE
```

### Run the EditMode suite

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

### Run any editor method

```powershell
Start-Process -FilePath $U -Wait -ArgumentList @(
  "-batchmode","-projectPath",$DST,
  "-executeMethod","VibeGame1.EditorTools.DataFactory.CreateAll",
  "-quit","-logFile","$DST\d.log")
```

`Unity.exe` **detaches** — without `-Wait` the call returns instantly and `$LASTEXITCODE` is empty. Always
`-Wait`.

---

## 2. The five traps, all of which have cost real time here

**1. `-executeMethod` silently does nothing if ANY script fails to compile.** No error is surfaced to the
caller. A whole regeneration was lost this way and only noticed because a value had not changed. **Always**
check the log before believing a no-op:

```powershell
Get-Content "$DST\d.log" | Select-String ": error CS" | Select-Object -First 5
```

In a shared tree this usually means another agent is mid-edit. Excluding their file from *your copy* is
legitimate; editing it in the real tree is not.

**2. `/PURGE` deletes anything that exists only in the copy.** Generated assets get wiped by the next sync,
and tests then fail claiming the enemy/level/wand does not exist — true of the copy, false of the work.
Order matters: **sync, THEN generate, THEN copy back.**

**3. Generated assets are committed artefacts — copy them back.** `Assets/Data/**`, `Assets/Prefabs/**`,
`Assets/Animation/**` and FBX `.meta` files are in git. Copy them back **with their `.meta`**, or GUIDs
diverge:

```powershell
Copy-Item "$DST\Assets\Data\Enemies\Foo.asset"      "$SRC\Assets\Data\Enemies\" -Force
Copy-Item "$DST\Assets\Data\Enemies\Foo.asset.meta" "$SRC\Assets\Data\Enemies\" -Force
```

**4. A NEW script gets a different `.meta` GUID in each project.** A prefab built in the copy references
the copy's GUID. Copied back, that is a **missing script**: YAML perfect, values intact, `GetComponent`
returns null at runtime, nothing in the console. Before building prefabs that reference a new script, copy
the real project's `.cs.meta` into the copy. And assert it with a test that resolves the TYPE —
`RevenantDataTests.ThePrefabActuallyCarriesTheAura` is the pattern; grepping the YAML passes on exactly
the broken case.

**5. Scene builders refuse to run with no scene open**, by design (a guard against building the campaign
level into the sandbox). `-batchmode` starts with an empty scene, so a headless scene build needs an entry
point that opens the target scene itself.

---

## 3. Camera captures

Worked examples: `Assets/Editor/EnemyPortrait.cs`, `SpinFilm.cs`, `LevelRouteShots.cs`.

- **Discard the first `Camera.Render()`.** In `-batchmode` it returns before the render pipeline has set
  itself up and produces a flat, wrongly-lit frame. One Revenant portrait came back solid orange and read
  as a broken material. It was the camera.
- **`Awake` never runs in edit mode.** `EnemyVisuals` applies its body colour there, so call `Setup(data)`
  (and `SetAura`) by hand or you photograph an unlit, wrongly-coloured body.
- **Runtime-spawned effects do not appear at all** — particles driven by an `Update` loop are simply
  absent. Assert those with a test; do not report "no fire" from a still frame.
- **`OpenScene` in Single mode unloads unused assets.** A `LevelDefinition` held only by a local becomes
  fake-null, the builder refuses in the log, and the frames come back showing the OLD scene looking
  perfectly plausible. **Load assets AFTER opening the scene, and read the builder's success line.**

---

## 4. Driving the LIVE editor over HTTP

The MCP server is configured as **HTTP transport on `127.0.0.1:8090`** and is spawned by the editor as it
loads. If Claude Code's own MCP tools are unavailable this session, the server itself is very likely fine
— check before assuming otherwise:

```powershell
Get-NetTCPConnection -State Listen -LocalPort 8090
```

**Port 6400 is a red herring.** It belongs to the *stdio* transport, where the server dials into a
Unity-side listener. On this HTTP install it is correctly never bound.

`scratchpad/mcp_call.py` (44 lines, plain JSON-RPC) reaches the same server and the same tools:

```
python mcp_call.py --list
python mcp_call.py --resource mcpforunity://editor/state
python mcp_call.py read_console '{"action":"get","types":["error"],"count":20,"format":"plain"}'
python mcp_call.py execute_menu_item '{"menu_path":"VibeGame1/Health Check"}'
python mcp_call.py execute_code '{"code":"VibeGame1.EditorTools.FeatureTestRunner.Start();"}'
```

**Check `editor/state` before doing anything.** If `is_playing` is true the user is at the controls —
read-only calls only. Entering play mode, running generators (hard rule 8) or changing scenes takes over
their session; ask first.

**The proper fix** when Claude Code's MCP tools are dead but the server is alive: `/mcp` → the server →
**Reconnect**, or relaunch with `claude --continue`. The usual cause is a startup race — the client gives
up in its first ~7 seconds, the editor spawns the server minutes later, and nothing retries.

---

## 5. Reporting honestly

State which half you did. "111/111 EditMode in the real Unity runner" and "no play-mode verification;
nobody has played it" are both true at once and the second one matters. A headless copy can prove that a
number is right, an asset is bound and a route exists geometrically. It cannot tell you whether any of it
feels good.
