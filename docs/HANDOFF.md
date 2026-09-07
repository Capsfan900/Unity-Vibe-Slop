# Handoff — state of play

**Rewritten at the end of every session; describes a moment, not the project.** Read this first when picking
up where the last chat stopped, then [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md).

Last session: **2026-09-07** (Opus 5). Three commits, all landed, tree clean. **The weapon pass is now fully
verified — that was the previous session's outstanding job and it is done.**

## What happened

1. **`5ce8260` the weapon pass is verified.** EditMode **722/722** (222 s), `Health Check` **0 errors**
   (1748 warnings, all the known UGUI/TMP optional-null-ref noise), feature suite **777 / 777 / 0 skipped**.
   The red the weapon pass shipped on (721/722) was a bad assertion in the VFX lane's own test — it wanted
   every weapon's hue to normalise to exactly 1.0, but `SlashFx.Normalise` is a **ceiling**, so Rosethorn's
   `#5FD66A` correctly stays at 0.839. Nothing in the weapon pass regressed anything.

   **The find worth carrying forward is how the feature suite first FAILED.** A run came back **720 / 49**,
   with failures spanning Hitstop, TimeScale, Stamina, Flare, WallRun, Posture, combo, the wand pedestal,
   the trail and lock-on — a set that reads exactly like a broad regression. Every one of the 49 was a
   timing test reading a **stopped clock**: `Time.timeScale` was 0 for the whole run. The session had
   followed the documented domain-reload rule (enter play, exit, re-enter, check `GameManager.I != null`)
   and that check *passed*. It is necessary and **not sufficient** — the singleton proves the session is
   warm, not that the world is running. Asserting the clock in the same call that starts the suite turned
   the identical tree into 777/777. Written up in `ENGINEERING-LOG.md`, top entry.

   Consequently `CLAUDE.md`'s Verification section **no longer carries test counts at all**. The numbers it
   did carry (558/558, 747/747) had gone stale and were lying to every session that loaded them; the counts
   now live only in `VERIFICATION-REPORT.md`, which the same sentence already linked to.

2. **`97f2540` build-distribution-worker — the game becomes something you can hand to a playtester.**
   Two channels, the standard pair for Unity: **WebGL → GitHub Pages** (a tester clicks a link, no install)
   and a **zipped Windows build → GitHub Release**. New: `Assets/Editor/BuildRunner.cs`
   (`Windows()` / `WebGL()` / `All()` as static methods returning a summary string — the menu items are thin
   wrappers, because `execute_menu_item` over MCP returns success and does nothing), the
   `Assets/WebGLTemplates/Playtest` template (click-to-play gate to satisfy pointer lock's user-gesture
   requirement; gzip + decompression fallback because Pages cannot set Brotli's Content-Encoding headers),
   `Tools/publish/Publish-WebGL.ps1`, `Tools/publish/Publish-WindowsRelease.ps1`, and `docs/DISTRIBUTION.md`.
   Each build writes a `build-info.txt` with the git short SHA, branch, UTC timestamp and Unity version so a
   playtest report traces to an exact build. No CI build — Unity-in-Actions needs a licence secret that does
   not exist here, so builds are cut locally and only the output is published.

   **Verified only as far as it can be without building:** it compiles clean in the live editor (0 console
   errors) and the type loads with the exact API surface. **No build has ever been cut through it.**

Also this session, outside the game: a `/doctor` pass removed four leftover agent worktrees (~156 MB) and
their branches, deleted a duplicate lowercase-drive project entry in `~/.claude.json` that would have
started a session with **no UnityMCP configured**, and removed an orphaned `SessionStart` hook. Backups sit
beside both files as `*.doctor-backup`.

## State of the tree

- **Committed and clean.** Nothing uncommitted, nothing stashed.
- **Tag `pre-distribution-2026-09-07`** sits immediately before the distribution commit;
  `pre-weapons-2026-09-06` still sits before the weapon batch.
- **Generators re-run since the last code change:** none were needed — no `DataFactory` or prefab change
  landed this session. Steps 2, 3, 4 were last run in the previous session and read back off disk.
- **One worktree deliberately left in place:** `.claude/worktrees/agent-ac943965c5ea99158` holds commit
  `d3b6245 "Span 4: the Warden causeway — 18 m becomes 104 m with three wall-run legs"`, which exists on
  **no other branch**. It never landed on master. See "Open questions".

## Verification

| Suite | Result | When |
|---|---|---|
| EditMode, full | **722 / 722, 0 failed** (222 s) | 2026-09-07, run this session |
| `Health Check` | **PASS, 0 errors**, 1748 known warnings | 2026-09-07, run this session |
| Feature suite, play mode | **777 / 777 / 0 skipped** | 2026-09-07, run this session |
| `BuildRunner` output | **NEVER BUILT** | — |

All three suites were run by this session, not inherited. **Nothing in the weapon pass has been played by a
human**, and that is unchanged: whether Rosethorn at 9 base damage reads as a breaker or just as weak,
whether the Sunbreaker's mechanical creak reads as tension or as input latency, and whether the new pooled
`WeaponImpactFx` hit confirm reads at all now that it is no longer the brightest thing on screen — all
still unproven by anything but arithmetic.

## Do first next session

1. **Cut the first playtest build and prove `BuildRunner` works.** Exit play mode, then
   `VibeGame1.EditorTools.BuildRunner.Windows()` via `execute_code` (the faster of the two), read the
   returned summary, and confirm `Builds/Windows/build-info.txt` carries the right SHA. Then `WebGL()`.
   Until a build exists, `97f2540` is unproven code.
2. **Settle the F10 question before anything ships** — `docs/BACKLOG.md`, "F10 opens the level editor in a
   shipped playtest build". A playtester can currently open the fly-cam editor over their run.
3. **Ask the user for a human playtest of the weapon pass.** Three weapons changed roles and every number
   is reasoned, not felt. This is the only thing that can settle it.

## Open questions for the user

1. **The unmerged Span 4 worktree.** `d3b6245 "Span 4: the Warden causeway — 18 m becomes 104 m with three
   wall-run legs"` is real level work on a branch that was never merged, in a worktree far behind master
   (it deletes 126k lines relative to master, so it is an old base — a merge would need care, not a
   fast-forward). Cherry-pick it, redo it on master, or drop it?
2. **GitHub Pages needs one-time clicks only the user can make**: Settings → Pages → Deploy from a branch →
   `gh-pages` → `/ (root)`. The branch does not need to exist first. Result:
   `https://capsfan900.github.io/Unity-Vibe-Slop/`.
3. **Per-weapon camera kick.** `WeaponController` calls `CameraShake.I.Small()` for every weapon, so a maul
   hit shakes exactly as hard as a needle flick. Named by the combat lane as the largest remaining weight
   gap. One line.
4. **The Sunbreaker is amber, and amber is the bolt.** `Hammer.neon` sits ~5° of hue from
   `Projectile.HotCore` — the colour the player is trained to read as "deflect this at 32 m/s" — and the
   trail paints it across the frame on every strike. Either a red-ember `(0.90, 0.30, 0.16)` or an explicit
   written acceptance of the collision.
5. **The swing is a straight chord, not an arc.** `WeaponViewmodel.cs:268` lerps position linearly; only the
   rotation slerps, so the hand travels straight. The fix pattern already exists in this project
   (`GuardArc`, line 412, bows the grip on a half-sine). Costs no timing and no data.

Smaller, still open, all pre-existing: per-weapon hit reaction (`EnemyVisuals.HitFlash` is a fixed tint pop,
so an 88-damage finisher and a 9-damage jab read identically); whether the reworked spiral is now too easy
(every gap 2.1–3.2 m against a 4.5 m reach ceiling); `SandboxBuilder.cs:222` still carries the warm
pre-cold-pass ambient, so the workshop lies about how enemies read in the campaign; and
`ARCHITECTURE.md:833` is broadly stale (still the blood-red palette). `docs/BACKLOG.md` also still holds
the surge-decay retune against the reworked spiral.

## In flight

Nothing. The build-distribution worker completed and its work is committed.

---

## Restore phrase

**Paste this into a fresh session verbatim. It is the whole boot sequence.**

    Read docs/HANDOFF.md, then CLAUDE.md. This is vibegame1: melee-only first-person parry
    speedrun platformer, namespace VibeGame1, Unity 6000.5.10f1, C# 9, URP.

    State: clean. The weapon pass is fully verified (EditMode 722/722, feature suite 777/777,
    Health Check 0 errors, all measured 2026-09-07). Tag pre-distribution-2026-09-07 sits
    before the playtest-build commit.

    Do this first, in order:
    1. Confirm the Unity editor is open on this project (PowerShell: Get-Process Unity).
       Everything goes through the user's open editor - there is no headless copy. Drive it
       with .claude/skills/unity-editor/mcp_call.py, run from the project root.
    2. Cut the first playtest build: exit play mode, then run
       VibeGame1.EditorTools.BuildRunner.Windows() via execute_code and read the returned
       summary. BuildRunner compiles and loads but has NEVER produced a build.
    3. Read docs/BACKLOG.md's F10 item before anything ships to a playtester.

    Four rules that override instinct:
    - execute_menu_item over MCP returns success:true and does nothing. Use execute_code and
      read back the asset every step is supposed to produce.
    - Generators 3 and 4 are ONE operation. Running 3 alone silently nulls every item's
      viewmodelPrefab. Check git status over the whole tree, not the file you edited.
    - Rule 9: a code default is not a shipped value. Changing a field initialiser does nothing
      to a ScriptableObject that already exists. Write it in DataFactory, re-run, read it back.
    - A feature-suite result is only evidence if Time.timeScale == 1 when it started.
      GameManager.I != null proves the session is warm, NOT that the world is running - a
      paused run reports ~49 plausible failures across unrelated systems.

    Model boundary: an Opus session ADDS features and does not rewrite or retune a system
    Fable wrote unless the user says so in that session. Subagents REFINE only - they never
    invent a mechanic, input, resource or screen; they propose it and stop. The lead owns the
    editor, commits one commit per worker pass, and tags before a batch.

    Then ask me what to work on.
