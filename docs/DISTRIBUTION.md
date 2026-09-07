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

Exit play mode first. Then, from `execute_code` (or the `VibeGame1/Build/…` menu, which does the same
thing but only logs — don't rely on `execute_menu_item` over MCP, it can report success without building):

```csharp
VibeGame1.EditorTools.BuildRunner.Windows()   // -> Builds/Windows/vibegame1.exe + build-info.txt
VibeGame1.EditorTools.BuildRunner.WebGL()     // -> Builds/WebGL/index.html + build-info.txt
VibeGame1.EditorTools.BuildRunner.All()       // both, in order
```

Each call returns a one-line summary (target, result, output path, size, duration, error/warning counts) —
read it, don't assume success. `Builds/` lives at the repo root, outside `Assets/`, and is already
gitignored (`/[Bb]uilds/` in `.gitignore`) — a build must never enter repo history.

The build fails loudly (returns an error string, does not throw) if `EditorBuildSettings` scene index 0
is not `Assets/Scenes/MainMenu.unity` — fix the scene list before building.

This is a **playtest build**, not a dev build: `BuildOptions.None`, development build OFF. The project's
`F5`/`F6`/`F7`/`F8`/`F9` dev keys are gated `#if UNITY_EDITOR || DEVELOPMENT_BUILD` in
`Assets/Scripts/Debug/DebugKeys.cs`, so they are compiled out of this build entirely — nothing to strip.
**The `F10` in-game level editor is not gated the same way** — only its EXPORT button is
(`#if !UNITY_EDITOR` in `Assets/Scripts/Level/LevelEditor.cs`); a playtester can still open F10 and roam
in fly-cam. That's a gameplay decision this build script does not make — flag it if it should be
dev-gated too.

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
