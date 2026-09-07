# Handoff — state of play

**Rewritten at the end of every session; describes a moment, not the project.** Read this first when picking
up where the last chat stopped, then [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md).

Last session: **2026-09-07** (Opus 5), fourth session that day. One commit, tree clean at `57626eb`.

> ## The verification debt is paid
>
> The previous session's headline was "four subagent passes landed and NOT ONE has been run in the editor."
> **That is now done.** Generators re-run in the right order, both suites green, the shipped values read
> back off the assets. No generator debt is outstanding and no suite is stale.
>
> **Nothing is blocking. Pick work from "Do first next session" or ask the user.**

## What happened

**One job: run the four-lane batch through the editor and prove it.** `57626eb` is the whole session.

**The generators, in the only order that works.** `DataFactory.CreateAll()` then `PrefabFactory.BuildAll()`
as one operation, then `HudBuilder.Build()`, then `SandboxBuilder.Build()`. What they actually shipped,
read back off the assets rather than assumed:

- `Hammer.asset` `neon` moved `0.878, 0.400, 0.102` → `0.659, 0.820, 0.180` (`#A8D12E`). This was rule 9
  in the flesh — the code change had been inert since the pass landed.
- `VM_Hammer.prefab`'s `EnergyGlow.tint` moved with it (`0.88,0.40,0.10` → `0.658,0.82,0.18`), which is the
  second hardcoded copy the previous lead moved by hand. The two are still only linked by a comment.
- `HUD.prefab` gained the **ARM MOVEMENT** row. `Sandbox.unity` picked up the ambient fix.
- Every item's `viewmodelPrefab` survived — 3 and 4 stayed one operation, so nothing was silently nulled.
- `Player.prefab` and `pshooter_enemy01.prefab` show large diffs that are **fileID reordering churn**, not
  behaviour. Expected from any `PrefabFactory` run.

**Two EditMode failures, both test-side, neither a fault in a system.** The full suite ran 745 (up from 722
— the batch added three test files) and failed exactly two, both in the batch's own new tests.
`WeaponSwingArcTests.ZeroAtBothEndpoints` asserted exact `Vector3` equality on a half-sine endpoint, and
`Mathf.Sin(Mathf.PI)` is `-8.7e-8`, not `0`; NUnit compares `Vector3` with `Equals` (exact), not the `==`
epsilon. `MovementPoseTests` expected falling and rising to carry opposite-signed tilts, but `MovementPose`
splits them across channels **on purpose** — a fall is a drop plus a pull-back (position only), an ascent is
a muzzle-up tilt (rotation only) — so `falling.euler.x` is `0` and that product could never be negative.
Both tests now assert what the files document; the second is renamed `Rising_TiltsUpAndFallingDoesNot`.

**A flake worth knowing about before it costs someone an hour.** The first full feature run reported
`Trail_ClosesAfterStrike` failing. It passes 3/3 in isolation and the re-run was 777/777. The test samples
on realtime (`WaitRealtime`) while `AttackCo` advances on `Time.deltaTime`, so a loaded or unfocused editor
lets the coroutine fall behind and the ribbon can still be open at the 1.00 s sample.

**Two editor traps hit this session, both new to the log.**

1. **`SandboxBuilder.Build()` opens a modal dialog and it deadlocks the MCP bridge.** `SandboxBuilder.cs:67`
   calls `EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()`, and the generators before it leave
   `Level_01` dirty, so a "Scene(s) Have Been Modified" window goes up. **Every MCP call then returns
   `Unity session not ready … ping not answered`** — which looks exactly like a lost bridge and is not one.
   Unity's own CPU sits at ~0 while it waits. **Diagnose it by enumerating Unity's top-level windows**
   (`EnumWindows` filtered to the editor's PID) rather than by retrying the bridge. It was resolved by
   clicking **Save**. **Save the scene before running generator 7 and it never happens.**
2. **`WeaponShots.Shoot()` leaves the editor in an empty `Untitled` scene**, which presents to the user as
   "the editor is showing an untitled view" and "no cameras rendering". Nothing is lost — nothing in this
   project is hand-authored (hard rule 4) — but **reopen `Level_01` after any photograph tool** rather than
   leaving the user staring at an empty scene.

## State of the tree

- **Committed and clean at `57626eb`.** One commit this session.
- **Generators re-run since the last code change: ALL OF THEM** (3, 4, 5, 7). This is the first handoff in
  three that can say so.
- **Tags**: `pre-team-passes-2026-09-07b` at `fad1e88` is still the revert point for the four passes, each
  of which is still its own commit and still reverts alone. Also `pre-distribution-2026-09-07`.
- Expect `ProjectSettings.asset` and `Assets/Settings/*` to go dirty after any build — URP shader-prefilter
  churn, not a change anyone made. `git restore` is safe there (`git checkout --` is blocked by the
  permission classifier here).
- **The Span 4 worktree is still in place**, unchanged across six handoffs:
  `.claude/worktrees/agent-ac943965c5ea99158`, holding `d3b6245`, which exists on no other branch.

## Verification

Numbers, dates and who ran them. Full detail in [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md).

| Suite | Result | When |
|---|---|---|
| `FeatureTests`, play mode | **777 / 777, 0 failed** | 2026-09-07, **run this session**, `warm=True ts=1` asserted in the starting call |
| EditMode, quick | **607 / 607** (7.9 s) | 2026-09-07, **run this session**, after the two test fixes |
| EditMode, full | 745 tests, 743 / 2 failed | 2026-09-07, **run this session**, before those fixes; both failures fixed in `57626eb` |
| `Health Check` | 0 errors, 1756 known warnings | 2026-09-07, **run this session** (1748 before; +8 for the new HUD row) |
| Windows build boots | PASS | 2026-09-07 earlier — inherited |
| WebGL build | BUILT, 30.8 MB gzipped, all assets serve HTTP 200 | 2026-09-07 earlier — inherited |
| WebGL build *runs* | **NEVER OPENED IN A BROWSER** | — |

**What is still unproven, stated plainly.** Everything above proves arithmetic and state machines. The
batch was mostly **feel** work and no suite can judge it:

- **The swing arc** (`WeaponViewmodel.SwingArc`, the bow off the chord) has never been watched by a human.
- **Arm movement** (`MovementPose`) likewise. It has a live toggle for exactly this reason —
  **F1 → PLAYER → ARM MOVEMENT** — so it can be A/B'd against its absence in one session.
- **The Sunbreaker's new colour was photographed and it is a problem.** `#A8D12E` clears the enemy bolt's
  hue by 47° as the rule requires, but under the real pipeline it reads **olive-lime**, which is not a
  colour a weapon called the Sunbreaker should be. See the open question below — this one has a
  photograph behind it, not a hunch.

## Do first next session

1. **Settle the Sunbreaker's colour** (open question 1). It is the only thing this session found and did
   not fix, and it is a one-line change in `DataFactory` plus the paired constant at
   `PrefabFactory.cs:266` — but which colour is the user's call, not a session's.
2. **Look at a swing and at the arm movement.** Play `Level_01`, swing, jump, wall-run, slide, dash; toggle
   ARM MOVEMENT off and on from F1. Five minutes, and it is the entire remaining verification debt of the
   four-lane batch. Needs a human.
3. **Open the WebGL build in a browser** — `cd Builds/WebGL && python -m http.server 8123 --bind 127.0.0.1`,
   then `http://127.0.0.1:8123` (`file://` will not work). Closes the last unknown in the distribution
   pipeline. Needs a human.

## Open questions for the user

1. **The Sunbreaker's hue — new, and the sharpest of these.** `hammer.neon` is now `#A8D12E` (hue ~75°),
   chosen to clear the enemy bolt (~23°) by the ≥45° rule the wand set holds. Photographed with the real
   pipeline this session: it reads olive-lime. **The awkward part is that there is no way out on the warm
   side** — the bolt lives there, so any gold or amber that reads "sunbreaker" is inside 45° of the one
   thing that must read fastest in a parry game. The three ways out: **keep the green** and let the name be
   the odd one; **go brass-warm and separate by value/saturation instead of hue** (dropping the ≥45° rule
   for this one weapon, deliberately, with the reason written down); or **rename the weapon** to match the
   colour it now is. Fable data — needs the user's word either way.
2. **The Resolution row** — `SettingsMenu.cs:439-442` destroys the `0/0` native sentinel; cycling the row
   once writes a concrete pair forever. User's instruction: *"detect native and use that, nothing else."*
   **Delete the row** (recommended) or **add a "Native" entry at index 0**? Fable system, needs their word.
3. **F10 in a shipped build** — `LevelEditor.cs` gates only its EXPORT button, so a playtester can open the
   fly-cam editor. One `#if UNITY_EDITOR || DEVELOPMENT_BUILD` fixes it. **Blocks publishing.**
4. **Per-weapon camera kick** — `WeaponController` calls `CameraShake.I.Small()` for every weapon, so a maul
   hit shakes as hard as a needle flick. Named by the combat lane as the largest remaining weight gap.
   Fable retune, parked awaiting a yes.
5. **Surge decay** — `pshooter_enemy03.parrySurgeSeconds` 2.0 s, tuned against the *old* spiral spacing;
   `BACKLOG.md` has the intended shape (1.2 s, or decay by distance travelled). Fable retune, parked.
6. **The Span 4 worktree** — cherry-pick, redo on master, or drop?
7. **GitHub Pages one-time clicks** (only the user can): Settings → Pages → Deploy from a branch →
   `gh-pages` → `/ (root)`.

## Still open, not started

**Backlog 5c, the Legendary Ninja riposte framing.** Narrowed two sessions ago and untouched since. All
seven `Legendary_*` prefabs were probed at rest at the standoff the test uses: nothing is inside the camera,
nothing is near the 0.5 m floor, and the Ninja is the second-roomiest of the seven. The stagger pose is
innocent too (`StaggerEuler` `-13°`, `StaggerSag` `-0.14` z lean the body **back** from the lens). So the
failure is **live-only**; leading candidate is that `DeathblowFraming` picks its stand point from the
dummy's position and only *then* waits 0.35 s to settle. The Ninja is also the only one of the seven whose
nearest renderer is a primitive `Body` rather than a forge `EnemyMesh`.

**The repro recipe, worked out and still not run:** `Level_01` has **zero** enemies in play mode since the
parkour pivot, so this cannot be reproduced there. The **sandbox** already has `Spawn_Legendary_Ninja` and a
`Wake_Legendary_Ninja` switch — open `Sandbox.unity`, play, wake it, break its posture, and read the
`Deathblow_StaggerPoseClearsNearPlane_*` failure message; it names the offending renderer.

Smaller and still open, all inherited: `EnemyController`'s now-redundant `visuals.HitFlash()` call (harmless,
resolved via Max/Min, but extending `IEnemyPresentation` with a fraction parameter is the cleaner fix);
`PlayerFeedback.cs` was never given the `MovementPose` hook that BACKLOG 2b literally specifies (the worker
used each viewmodel's own `LateUpdate` instead, deliberately, to stay in its lane); and `HudBuilder`'s colour
constants were never audited against the cold palette.

## In flight

Nothing running. No subagents were used this session.

**Rebuild the task list from `BACKLOG.md`** — task lists are session-local and do not survive a `/clear`.

---

## Restore phrase

**Paste this into a fresh session verbatim. It is the whole boot sequence.**

    Read docs/HANDOFF.md, then CLAUDE.md. This is vibegame1: melee-only first-person parry
    speedrun platformer, namespace VibeGame1, Unity 6000.5.10f1, C# 9, URP.

    State: clean at 57626eb, and for the first time in three sessions there is NO
    verification debt - every generator has been re-run against the current code and both
    suites are green (feature 777/777 with warm=True ts=1 asserted in the starting call,
    EditMode quick 607/607, Health Check 0 errors / 1756 known warnings). Nothing is
    blocking.

    What is unproven is FEEL, and no suite can judge it: the swing arc, the arm movement
    (toggle it live at F1 -> PLAYER -> ARM MOVEMENT), and the Sunbreaker's new colour,
    which was photographed and IS a problem - #A8D12E clears the enemy bolt by 47 degrees
    as the rule demands but reads olive-lime, and there is no warm hue that clears the
    bolt at all. That decision is question 1 in the handoff and it is the user's.

    Six rules that override instinct:
    - Confirm the Unity editor is open (PowerShell: Get-Process Unity). Everything goes
      through the user's open editor - there is no headless copy. Drive it with
      .claude/skills/unity-editor/mcp_call.py, run from the project root.
    - A MODAL DIALOG IN UNITY DEADLOCKS THE MCP BRIDGE and presents as "Unity session not
      ready ... ping not answered", which looks exactly like a lost bridge. Unity's CPU
      sits at ~0 while it waits. Diagnose by enumerating Unity's top-level windows
      (EnumWindows filtered to its PID), not by retrying. SandboxBuilder.Build() causes
      one every time if a scene is dirty - save the scene first.
    - execute_menu_item over MCP returns success:true and does nothing. Use execute_code
      and read back the asset every step is supposed to produce.
    - Generators 3 and 4 are ONE operation. Running 3 alone silently nulls every item's
      viewmodelPrefab. Check git status over the whole tree, not the file you edited.
    - Rule 9: a code default is not a shipped value. Changing a field initialiser does
      nothing to a ScriptableObject that already exists. Write it in DataFactory, re-run,
      read it back.
    - A feature-suite result is only evidence if Time.timeScale == 1 when it started.
      GameManager.I != null proves the session is warm, NOT that the world is running - a
      paused run reports ~49 plausible failures across unrelated systems.

    Level_01 has ZERO enemies in play mode since the parkour pivot. Any enemy-facing test
    needs a deliberate spawn in the SANDBOX, which has Spawn_Legendary_Ninja and a
    Wake_Legendary_Ninja switch. This is why backlog 5c is still open.

    Never smoke-test a build with -screen-width/-screen-height flags: Unity PERSISTS them
    to HKCU:\Software\vibegame1\vibegame1, so they poison every later flagless launch.

    Model boundary: an Opus session ADDS features and does not rewrite or retune a system
    Fable wrote unless the user says so in that session. Subagents REFINE only - they never
    invent a mechanic, input, resource or screen; they propose it and stop. The lead owns
    the editor, commits one commit per worker pass, and tags before a batch.

    Seven decisions are parked on the user: the Sunbreaker's hue (new, has a photograph
    behind it), the settings Resolution row, F10 in a shipped build (blocks publishing),
    per-weapon camera kick, the surge decay, the unmerged Span 4 worktree, and the GitHub
    Pages clicks only they can make.

    Then ask me what to work on.
