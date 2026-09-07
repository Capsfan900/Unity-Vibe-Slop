# Distribution

How to cut a playtest build and hand a playtester a link, using the standard two channels for a Unity
playtest: **WebGL on GitHub Pages** (click a link, zero install) and **Windows as a zipped GitHub Release**
(download and run).

**No GameCI / Unity-in-Actions.** Building Unity headlessly in GitHub Actions needs a Unity licence secret
the user has not provided. Builds happen locally, in the editor the lead session already has open. The
`.github/` directory intentionally does not exist for this project — GitHub Pages here is served straight
from a branch ("Deploy from a branch"), which needs no workflow file at all.

## One-time GitHub settings (the user does this, once)

1. Repo → **Settings → Pages → Build and deployment → Source**: `Deploy from a branch`.
2. **Branch**: `gh-pages`, folder `/ (root)`. (The branch does not need to exist yet — the first
   `Publish-WebGL.ps1` run below creates it.)
3. Save. After the first publish, the game is at `https://capsfan900.github.io/Unity-Vibe-Slop/`
   (owner `Capsfan900`, repo `Unity-Vibe-Slop` → project Pages URL is `https://<owner>.github.io/<repo>/`).

Nothing else to click. Releases need no settings — anyone with the repo URL can see them.

## Cutting a build (the lead runs this in the open editor)

From `execute_code` (or the `VibeGame1/Build/…` menu, which does the same thing but only logs — don't rely
on `execute_menu_item` over MCP, it can report success without building):

```csharp
VibeGame1.EditorTools.BuildRunner.Preflight() // -> checks everything, builds nothing. Run this first.
VibeGame1.EditorTools.BuildRunner.Windows()   // -> Builds/Windows/vibegame1.exe + build-info.txt
VibeGame1.EditorTools.BuildRunner.WebGL()     // -> Builds/WebGL/index.html + build-info.txt
VibeGame1.EditorTools.BuildRunner.All()       // both, in order
```

**Read `Builds/last-build.txt`, not the return value of the call.** Every run writes its summary there and
logs it to the console. A build blocks Unity's main thread for minutes, which drops the MCP websocket, so
the call that started the build routinely returns `success:false` with a null message *for a build that
succeeded*. The file is the reliable channel. (Once the Library cache is warm an incremental build takes
about five seconds and the call does return normally — the drop only bites on a cold build.)

`Builds/` lives at the repo root, outside `Assets/`, and is gitignored (`/[Bb]uilds/`) — a build must never
enter repo history.

### It is designed to work whatever state the project is in

You should be able to add a level, add an enemy, and rebuild without remembering anything. `BuildRunner`
either fixes the problem itself or refuses with one sentence naming the fix:

| State | What happens |
|---|---|
| Editor is in play mode | Exits play mode, refuses this run, tells you to re-run. Exiting costs a frame and a domain reload, so it cannot happen inside the same call. |
| Scripts still compiling | Refuses. Wait for the spinner. |
| Project has compile errors | **Refuses.** A build here silently ships the last good assemblies — a build that lies about what is in it. |
| Build target module not installed | Refuses, naming the Unity Hub path to add it. |
| Scene list is stale / a new level was added | **Repairs it**, and says what it changed. See below. |
| A required scene file is missing | Refuses, naming the `LevelDefinition` and the path it expected. |
| An open scene has unsaved edits | Builds anyway, and **warns** that it used the version on disk. It will not silently bake a half-finished edit. |

**The scene list is derived from data on every build**, never trusted from `EditorBuildSettings`:
`MainMenu.unity` at index 0, then the scene named by every campaign level in `LevelRegistry` (in
`orderIndex` order), then `Sandbox.unity`. Add a `LevelDefinition` to the registry and its scene is in the
next build with nothing else to remember. The sandbox ships deliberately — `MainMenuController.LoadSandbox`
and every custom-level row load it by name, so a build without it has a main menu with dead buttons.

### Build configuration is enforced at build time

Set in `BuildRunner`, not in the Inspector, so a build never depends on what someone last clicked:

- **`BuildOptions.None`** — a playtest build, not a development build. The `F5`–`F9` dev keys are gated
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD` in `Assets/Scripts/Debug/DebugKeys.cs`, so they compile out
  entirely. **The `F10` in-game level editor is not gated that way** — only its EXPORT button is
  (`#if !UNITY_EDITOR` in `Assets/Scripts/Level/LevelEditor.cs`), so a playtester can still open F10 and
  roam in fly-cam. A gameplay decision this script does not make — see `docs/BACKLOG.md`.
- **Managed stripping: `High`** on both Standalone and WebGL. This is worth ~26 MB (see below) and is the
  one setting here that can break the game at runtime rather than at build time, because the linker cannot
  see reflection. **Any change to it must be re-proved by launching the build**, not by a test suite —
  nothing in either suite runs the player.
- `*_BurstDebugInformation_DoNotShip` folders are deleted from the output after every build. Obey the name.

`build-info.txt` records the SHA, branch, **whether the tree was dirty**, UTC time, build duration, Unity
version, scripting backend, stripping level and the exact scene list.

### Size, and where it goes

The Windows build is **~96 MB**, of which roughly 10 MB is this game (assets ~9 MB, `Assembly-CSharp.dll`
~0.6 MB) and the rest is Unity. Two changes on 2026-09-07 took it down from ~148 MB:

- **Six unused packages removed** from `Packages/manifest.json`: `com.unity.ai.inference` (which alone
  shipped a 14 MB `DirectML.dll` into a melee platformer), `visualscripting`, `purchasing`, `analytics`,
  `timeline`, `xr.legacyinputhelpers`. None had a reverse dependency or a single reference in `Assets/`.
- **Managed stripping was `Disabled` on Standalone** — not merely low, off. Setting it to `High` took
  `vibegame1_Data/Managed` from 37 MB to 11 MB (`System.Xml`, `System.Data`, `System.Drawing` were all
  shipping into a game that parses no XML and opens no database).

**WebGL is the number that matters for a link.** The first WebGL build was cut 2026-09-07: **30.8 MB, and
that is already gzipped** — Unity ships the `.unityweb` files pre-compressed (they carry the `1f8b` gzip
magic), so it is the wire size a playtester actually waits for, not a pre-compression figure. Uncompressed
the payload is 58.7 MB: `WebGL.wasm` 31.0 → 8.7 MB, `WebGL.data` 27.4 → 22.0 MB, `WebGL.framework.js`
0.3 → 0.1 MB, plus a 48 KB uncompressed loader. It builds in **4:56** — well under the 10-20 min an
IL2CPP-to-WASM build is usually braced for. WebGL is always IL2CPP, so the Standalone Mono note below does
not apply to it.

Two side effects of a WebGL build, both harmless and both now handled. `ProjectSettings.asset` goes dirty
because `BuildRunner` enforces `webGLTemplate: PROJECT:Playtest`, `webGLCompressionFormat: 1` (gzip) and
`webGLDecompressionFallback: 1` — that fallback is what lets the gzipped build load from a server that
sends no `Content-Encoding` header, including GitHub Pages and a bare `python -m http.server`. And Burst
spills `Data/lib_burst_generated.{cpp,wasm}` into the **project root**, written relative to the working
directory; it is referenced nowhere in `Assets/` and is covered by `/[Dd]ata/` in `.gitignore`.

To check a WebGL build locally before publishing, serve it and open it — opening `index.html` from `file://`
will not work:

```
cd Builds/WebGL && python -m http.server 8123 --bind 127.0.0.1
```

Unity's floor for a stripped URP game is ~50–60 MB of engine you cannot get back. Content is what scales
from here. **IL2CPP is not installed** for Windows Standalone (only the Mono variations are present in the
Hub install), so the Standalone backend is `Mono2x`; installing *Windows Build Support (IL2CPP)* in Unity
Hub would cut the managed side further at the cost of much slower builds.

## Publishing

### WebGL → GitHub Pages (the important one — send a link)

```powershell
./Tools/publish/Publish-WebGL.ps1
```

Mirrors `Builds/WebGL` into a throwaway git worktree at `.worktrees/gh-pages` (gitignored, never touches
`master`), checked out to the `gh-pages` branch (created as an orphan on first run), commits and pushes.
Pages republishes automatically within a minute or two of the push, per the one-time setting above.

### Windows → GitHub Release (zip, download-and-run)

```powershell
./Tools/publish/Publish-WindowsRelease.ps1 -Tag playtest-2026-09-07
```

Zips `Builds/Windows` to `Builds/vibegame1-windows-<tag>.zip`. If the `gh` CLI is on PATH it tags, pushes
the tag and creates the release with the zip attached. **`gh` was confirmed absent from both Git Bash and
PowerShell on this machine** — the script detects that and instead prints the exact manual steps: tag,
push the tag, then open `https://github.com/Capsfan900/Unity-Vibe-Slop/releases/new?tag=<tag>` and drag
the zip in by hand.

## Tracing a playtest bug report

Every build carries `build-info.txt` next to the executable (Windows) or at the site root (WebGL):
`git_sha`, `git_branch`, `built_utc`, `unity_version`, `product_version`. Ask a playtester reporting a bug
for that SHA (Windows: the file next to the .exe; WebGL: view-source on the Pages URL won't show it since
it's not linked from `index.html` — tell playtesters to check the browser's Network tab for
`build-info.txt`, or just tag each Pages publish with the commit message the publish script already writes,
`git log gh-pages` on the maintainer's machine). `git show <sha>:CLAUDE.md` (or any file) reproduces the
exact tree the report was against.

## What this deliberately does not do

- No Unity build inside GitHub Actions (licence secret not provided — see above).
- No change to the F10 dev-key gating question — reported, not decided here (see "Cutting a build").
- No auto-versioning/auto-tagging beyond what the two publish scripts do when the lead runs them by hand.
