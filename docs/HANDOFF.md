# Handoff — state of play

**Rewritten at the end of every session; describes a moment, not the project.** Read this first when picking
up where the last chat stopped, then [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md).

Last session: **2026-09-07** (Opus 5), third session that day. Six commits, tree clean at `6b2ec75`.

> ## ⚠ READ THIS BEFORE ANYTHING ELSE
>
> **Four subagent passes landed and NOT ONE has been run in the editor.** They compile (`dotnet build`,
> 0 errors, both assemblies) and that is all that is proven. **No generator has been re-run and neither
> suite has been run since `fad1e88`.** Three of the four passes need generators before they do anything
> at all, and one of them (`hammer.neon`) is a rule-9 data change that is inert until then.
>
> **First job next session, in this exact order:**
> 1. `VibeGame1.EditorTools.DataFactory.CreateAll()` **then** `PrefabFactory.BuildAll()` — generators 3 and
>    4 are ONE operation; 3 alone silently nulls every item's `viewmodelPrefab`.
> 2. `HudBuilder.Build()` (5) — the new ARM MOVEMENT row does not exist until this runs.
> 3. `SandboxBuilder.Build()` (7) — the sandbox ambient fix is inert until this runs.
> 4. `Health Check`, then the full EditMode suite, then the feature suite in play mode.
> 5. Three new test files have **no `.meta` yet** (`MovementPoseTests`, `WeaponSwingArcTests`,
>    `HitReadTests`) plus `MovementPose.cs` — Unity generates those on its first import. Let it import
>    before running anything.
>
> **Revert point: tag `pre-team-passes-2026-09-07b` at `fad1e88`.** Every pass is its own commit, so any
> one of them reverts alone.

## What happened

**Verification first, then a four-lane batch.**

**`aa72712` — the WebGL build exists**, the last unproven half of the pipeline. **30.8 MB, and that is
already gzipped** — Unity ships the `.unityweb` files pre-compressed (`1f8b` magic), so it is the wire size
a playtester waits for, against 95.6 MB for Windows. Built in **4:56**, 0 errors, 3 benign warnings (IL2CPP
splitting three large TextMeshPro methods). All four `Build/` assets plus `index.html` serve **HTTP 200 at
full length** from a plain static server, which also exercises the gzip decompression fallback. **Nobody has
opened it in a browser**, so the WASM has never been instantiated and the pointer-lock gate never clicked.
Also: `ProjectSettings.asset` goes dirty after a WebGL build by design (`BuildRunner` enforcing
`PROJECT:Playtest` / gzip / fallback), and Burst spills `Data/lib_burst_generated.*` into the project root —
now covered by `/[Dd]ata/` in `.gitignore`.

**`1edf04b` — the feature suite passes after the package removal.** `777 / 777, 0 failed, 0 skipped` in
60.7 s, with **both** guards asserted in the same call that started it (`warm=True timeScale=1`). Closes the
last inherited claim. The same commit narrows backlog 5c — see "Still open" below.

**`bd262cb` `[sandbox-docs-worker]` — the sandbox stops lying about how the game looks.** It carried the
pre-cold-pass warm set as hand-copied literals (key light `#C9663A`, ambient `#3E4A6B`/`#7A5540`/`#191424`),
so every silhouette and material judged in the workshop was judged under the wrong light. It now **reads
`ProjectSetup`'s constants directly**, so the drift is structurally impossible rather than merely corrected.
`ARCHITECTURE.md`'s palette section, which still described the blood-red palette, was rewritten against
`MaterialFactory.Table`; **the trim table was wholly stale** — `M_NeonPink` is tile 3 azure `#2F6BFF`, not
"boss court trim"; `M_NeonRed` is ghost green `#3FE07A`, not crimson. All four verified against
`MaterialFactory.cs:72-87` by the lead before committing.

**`2c7ee51` `[viewmodel-worker]` — the swing arcs and the hands react to movement.** The strike leg used
`Pose.Lerp`, cutting a straight **chord** — the blade slid rather than swept. `SwingArc` now bows the
position off that chord (the same half-sine as the existing `GuardArc`, scaled to 30% of the chord's *own*
length so each weapon bows in proportion to its authored reach) with a 1.35× rotation lead so the tip
arrives before the wrist. **Every timing untouched.** And new pure `MovementPose.cs` (no MonoBehaviour, no
Update, no singleton) summed additively onto the model, hard-clamped to 0.14 m / 20°, so the hands stop
being rigid through every airborne move.

**Lead fix inside that pass, worth knowing.** The worker smoothed on raw `Time.unscaledDeltaTime`, arguing
it matched sway. It does not — this file runs **two clocks on purpose**: sway is unscaled because it is
driven by the *mouse*, while the bob runs on `TimeScaleController.PlayerDelta` under an explicit "RULE 1:
driven by the player's own movement" comment. Movement pose belongs on the bob's clock. Two real
consequences of the original: `PlayerDelta` clamps to 0.05 s so a frame hitch cannot snap the offset to
target, and `PlayerScale` goes to 0 on a pause while `unscaledDeltaTime` does not — the hands would have
drifted behind the pause menu. Both call sites moved.

**`6885469` — ARM MOVEMENT is toggleable from the F1 menu.** The user asked for this the moment the channel
landed: *"it may not work with the game."* `MovementPose.Enabled` is checked inside `Compute`, so there is
one switch and the viewmodels' own smoothing carries the hands back rather than dropping them. Wired
exactly like WAND PEDESTAL. **F1 → PLAYER → ARM MOVEMENT.** (Note: this project's `Pose` is its own struct
in `WeaponData.cs` with no `identity` member.)

**`6b2ec75` `[hit-read-worker]` — a heavy hit reads as heavy, and the Sunbreaker drops the bolt's colour.**
`HitFlash` was a fixed tint pop, so an 88-damage finisher and a 9-damage jab looked identical. It now scales
with damage as a **fraction of the victim's max HP** (floor 0.02, ceiling 0.30; boost 0.70→1.00; duration
0.13→0.50 s), and at the floor it is exactly the old constant so a chip still pops. And `hammer.neon` moved
from `#E0661A` (hue ~23°, **~5° from the enemy bolt**) to `#A8D12E` (~75°, **47° clear**), applying the same
≥45° rule the wand set already holds — in a parry game the player's own weapon must not compete with the one
thing that has to read fastest.

**Second lead fix.** The worker flagged that its own fix was incomplete in a file it did not own, and it was
right: `PrefabFactory.cs:266` held a **second, hardcoded copy** of the old amber driving the viewmodel glow,
because `Energise` does not read `WeaponData.neon`. Left alone, the data would say one colour and the
viewmodel would glow another. Moved to match, with a comment saying the two must change together.

## State of the tree

- **Committed and clean at `6b2ec75`.** Six commits this session.
- **Tags**: `pre-team-passes-2026-09-07b` at `fad1e88` (immediately before the batch — this is the revert
  point), plus `pre-buildpipeline-2026-09-07` at `23b53a3`.
- **Generators re-run since the last code change: NONE.** This is the single most important line in this
  file. See the box at the top.
- Expect `ProjectSettings.asset` and `Assets/Settings/*` to go dirty after any build — URP shader-prefilter
  churn, not a change anyone made. `git restore` is safe there (`git checkout --` is blocked by the
  permission classifier here).
- **The Span 4 worktree is still in place**, unchanged across five handoffs:
  `.claude/worktrees/agent-ac943965c5ea99158`, holding `d3b6245`, which exists on no other branch.

## Verification

| Suite | Result | When |
|---|---|---|
| `FeatureTests`, play mode | **777 / 777, 0 failed** (60.7 s) | 2026-09-07, run this session — **but BEFORE the four passes** |
| EditMode, full | 722 / 722 | 2026-09-07 earlier — **before the four passes** |
| `Health Check` | 0 errors, 1748 known warnings | 2026-09-07 earlier — **before the four passes** |
| Windows build boots | PASS | 2026-09-07 earlier — inherited |
| WebGL build | **BUILT**, 30.8 MB gzipped, 0 errors, all assets serve HTTP 200 | 2026-09-07, run this session |
| WebGL build *runs* | **NEVER OPENED IN A BROWSER** | — |
| **The four subagent passes** | **`dotnet build` 0 errors, BOTH assemblies. NOTHING ELSE.** | 2026-09-07 |

**Unproven and worth stating plainly:** nothing in this session's batch has been seen, run or measured in
the editor. `#A8D12E` in particular is a gold-brass that reads slightly green for a weapon called the
*Sunbreaker* — it satisfies the hue rule, but it wants human eyes before it stands. The swing arc and the
arm movement are both pure feel and can only be judged by looking at them; the arm movement now has a
toggle precisely so it can be compared against its absence.

## Do first next session

1. **Run the generator and suite sequence in the box at the top of this file**, then look at a swing and at
   the Sunbreaker. That is the whole verification debt of this session.
2. **Open the WebGL build in a browser** — `cd Builds/WebGL && python -m http.server 8123 --bind 127.0.0.1`,
   then `http://127.0.0.1:8123` (`file://` will not work). Five minutes, closes the last unknown in the
   distribution pipeline, needs a human.
3. **Get the parked decisions out of the user** — five below, two carried across three handoffs now.

## Open questions for the user

1. **The Resolution row** — `SettingsMenu.cs:439-442` destroys the `0/0` native sentinel; cycling the row
   once writes a concrete pair forever. User's instruction: *"detect native and use that, nothing else."*
   **Delete the row** (recommended) or **add a "Native" entry at index 0**? Fable system, needs their word.
2. **F10 in a shipped build** — `LevelEditor.cs` gates only its EXPORT button, so a playtester can open the
   fly-cam editor. One `#if UNITY_EDITOR || DEVELOPMENT_BUILD` fixes it. **Blocks publishing.**
3. **Per-weapon camera kick** — `WeaponController` calls `CameraShake.I.Small()` for every weapon, so a maul
   hit shakes as hard as a needle flick. Named by the combat lane as the largest remaining weight gap.
   Fable retune, parked awaiting a yes.
4. **Surge decay** — `pshooter_enemy03.parrySurgeSeconds` 2.0 s, tuned against the *old* spiral spacing;
   `BACKLOG.md` has the intended shape (1.2 s, or decay by distance travelled). Fable retune, parked.
5. **The Span 4 worktree** — cherry-pick, redo on master, or drop?
6. **GitHub Pages one-time clicks** (only the user can): Settings → Pages → Deploy from a branch →
   `gh-pages` → `/ (root)`.

## Still open, not started

**Backlog 5c, the Legendary Ninja riposte framing — narrowed this session, and the two obvious suspects are
ruled out.** All seven `Legendary_*` prefabs were probed at rest at the standoff the test uses: nothing is
inside the camera, nothing is near the 0.5 m floor, and **the Ninja is the second-roomiest of the seven**
(1.86 / 2.75 m, against the Halberdier's 1.13 / 1.15 m). The stagger pose is innocent too — `StaggerEuler`
`-13°` and `StaggerSag` `-0.14` z lean the body **back** from the lens. So the failure is **live-only**;
leading candidate is that `DeathblowFraming` picks its stand point from the dummy's position and only *then*
waits 0.35 s to settle. The Ninja is also the only one of the seven whose nearest renderer is a primitive
`Body` rather than a forge `EnemyMesh`.

**The repro recipe, worked out and not yet run:** `Level_01` has **zero** enemies in play mode since the
parkour pivot, so this cannot be reproduced there. The **sandbox** already has `Spawn_Legendary_Ninja` and a
`Wake_Legendary_Ninja` switch — open `Sandbox.unity`, play, wake it, break its posture, and read the
`Deathblow_StaggerPoseClearsNearPlane_*` failure message; it names the offending renderer.

Also corrected in `BACKLOG.md`: `markHeight = 1.28` is the **Drillmaster's**, not the Ninja's, and only five
of seven bodies set it (the rest default to `1.45`).

Smaller and still open: the swing/hit work above did not touch `EnemyController`'s now-redundant
`visuals.HitFlash()` call (harmless, resolved via Max/Min, but extending `IEnemyPresentation` with a
fraction parameter is the cleaner fix); `PlayerFeedback.cs` was never given the `MovementPose` hook that
BACKLOG 2b literally specifies (the worker used each viewmodel's own `LateUpdate` instead, deliberately, to
stay in its lane); and `HudBuilder`'s colour constants were never audited against the cold palette.

## In flight

Nothing running. Three subagents were used this session and all three have reported and been committed.

**Rebuild the task list from `BACKLOG.md`** — task lists are session-local and do not survive a `/clear`.

---

## Restore phrase

**Paste this into a fresh session verbatim. It is the whole boot sequence.**

    Read docs/HANDOFF.md, then CLAUDE.md. This is vibegame1: melee-only first-person parry
    speedrun platformer, namespace VibeGame1, Unity 6000.5.10f1, C# 9, URP.

    State: clean at 6b2ec75. FOUR subagent passes landed and NOT ONE has been run in the
    editor - they compile (dotnet build, 0 errors, both assemblies) and that is all that is
    proven. No generator has been re-run and neither suite has run since fad1e88. Revert
    tag pre-team-passes-2026-09-07b sits at fad1e88; every pass is its own commit.

    Do this first, in order:
    1. Confirm the Unity editor is open on this project (PowerShell: Get-Process Unity).
       Everything goes through the user's open editor - there is no headless copy. Drive it
       with .claude/skills/unity-editor/mcp_call.py, run from the project root. Let Unity
       import first: four new files have no .meta yet.
    2. Run the generators the batch needs, in this order: DataFactory.CreateAll() THEN
       PrefabFactory.BuildAll() (3 and 4 are ONE operation), then HudBuilder.Build() (5,
       for the new ARM MOVEMENT row), then SandboxBuilder.Build() (7, for the ambient fix).
    3. Health Check, then the full EditMode suite, then the feature suite in play mode.
       Then LOOK at a swing and at the Sunbreaker - the batch is mostly feel work that no
       suite can judge.

    Five rules that override instinct:
    - A cold build drops the MCP websocket, so execute_code returns success:false with a null
      message for a build that SUCCEEDED. Read Builds/last-build.txt. Run
      BuildRunner.Preflight() first when anything looks wrong; it checks everything and
      builds nothing.
    - execute_menu_item over MCP returns success:true and does nothing. Use execute_code and
      read back the asset every step is supposed to produce.
    - Generators 3 and 4 are ONE operation. Running 3 alone silently nulls every item's
      viewmodelPrefab. Check git status over the whole tree, not the file you edited.
    - Rule 9: a code default is not a shipped value. Changing a field initialiser does nothing
      to a ScriptableObject that already exists. Write it in DataFactory, re-run, read it back.
    - A feature-suite result is only evidence if Time.timeScale == 1 when it started.
      GameManager.I != null proves the session is warm, NOT that the world is running - a
      paused run reports ~49 plausible failures across unrelated systems.

    Level_01 has ZERO enemies in play mode since the parkour pivot. Any enemy-facing test
    needs a deliberate spawn in the SANDBOX, which has Spawn_Legendary_Ninja and a
    Wake_Legendary_Ninja switch. This is why backlog 5c is still open.

    Never smoke-test a build with -screen-width/-screen-height flags: Unity PERSISTS them to
    HKCU:\Software\vibegame1\vibegame1, so they poison every later flagless launch.

    Model boundary: an Opus session ADDS features and does not rewrite or retune a system
    Fable wrote unless the user says so in that session. Subagents REFINE only - they never
    invent a mechanic, input, resource or screen; they propose it and stop. The lead owns the
    editor, commits one commit per worker pass, and tags before a batch.

    Five decisions are parked on the user, two carried across three handoffs: the settings
    Resolution row, F10 in a shipped build (blocks publishing), per-weapon camera kick, the
    surge decay, and the unmerged Span 4 worktree.

    Then ask me what to work on.
