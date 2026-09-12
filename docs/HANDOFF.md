# Handoff — MCP-primary integration and V18 Souls-enemy prototype

## Current state — 2026-09-11

Work is on `unity-cli-pilot`; `master` remains the pre-CLI baseline at `17f81f0`, protected by
`pre-unity-cli-pilot-2026-09-11`. The implementation is pushed in `4ad0151`; deterministic WebGL platform
settings are pushed in `79dc3f7`. Rollback tag `pre-v18-souls-prototype-2026-09-11` is also on the remote.

The V18 Flurry Brawler is implemented as a completely separate test Souls-enemy lineage. It uses ordinary
melee `EnemyController` and `PlayerCombat.ReceiveAttack`, with its own data, nine attacks, moveset, prefab,
Generic FBX clip library, Animator controller and `FlurryBrawlerV18Visuals` presentation layer. It has no
parkour projectile, surge or boss components and does not replace V15. Its independent Sandbox pad is at
`(112, FloorTop, 20)`.

The earlier in-flight release pass is also present: centralized process-local `DeveloperAccess`, locked
release diagnostics, transactional build/publish scripts, the persistent-wand toothpick repair, regenerated
HUD/Main Menu and the official Unity CLI/Pipeline pilot. None of those game changes are being rolled back.
MCP is again the primary editor/build/test transport; the retained CLI is only an optional short diagnostic.
Preserve the user-owned files listed below.

## Verification completed

- Full EditMode: **963/963 passed**, zero failures/skips, 49.34 s.
- Fresh Level_01 FeatureTests: **813 passed, 0 failed, 1 intentional V18/Sandbox skip**, 60.4 s.
- Focused Sandbox `V18Smoke`: **21/21 passed**, zero failures/skips, 9.6 s.
- V18 smoke proves committed Clap survives ordinary damage, blocked/perfect Clap each resolves and grounds
  once with one ring, Shoulder shows Run→contact with one 4.12 m lunge, and Combo2 has one contact plus tail.
- Runtime/editor offline builds: zero errors; editor retains 18 existing warnings.
- Health Check: no error section; 1942 existing broad serialized-null/audio warnings.
- WebGL build artifact: **built and published**, 30.6 MB, three scenes, High stripping. The live
  `build-info.txt` identifies `79dc3f7`, `unity-cli-pilot` and `build_inputs_dirty=no`.
- GitHub Pages: published and returning HTTP 200 at
  `https://capsfan900.github.io/Unity-Vibe-Slop/`.
- Browser acceptance: **failed by human playtest** on 2026-09-11; the user reported unplayable frame pacing,
  degraded fidelity and broken-feeling behavior versus local testing. WebGL is experimental, and Windows is
  the primary friend-playtest target.

Human review remains required for the animation feel: Dash-as-rising-clap readability, shoulder/body contact,
Clap weight, and fair manual parry tells.

## Unity licensing and tool result

The failed disposable probe is not caused by too many projects or a missing AnimationModule. Raw
`Unity.exe -batchmode -createProject` bypassed Hub bootstrap/authentication, lost the Licensing Client pipe,
registered zero built-in packages, and only then failed to resolve `Animator`. Hub-authenticated live-editor
execution works.

Unity CLI `1.0.0-beta.8` with `com.unity.pipeline 0.7.0-exp.1` helped with some short state/eval calls and
successfully drove several generators, so the pilot and all game work created on its branch remain. It is
not the base workflow: Pipeline can disappear around a domain reload, and both a long generator and a later
preflight exceeded the package's five-second main-thread response window while Unity remained healthy.
Use MCP for normal live-editor queries, generators, tests and builds. Use CLI only as an optional short
diagnostic comparison, with MCP/asset readback afterward. Never run `unity test/build/run` against this
active working copy.

Unity's `com.unity.ai.assistant` package also waits roughly 30 seconds for its Account API on Play Mode domain
reload, temporarily logging zero matching entitlements before licensing resolves successfully. Confirm
`Time.frameCount` advances, `GameManager.I != null` and `Time.timeScale == 1`; the editor-state phase can remain
stale during that delay.

## Exact resume order

1. Finish a clean native Windows build: keep the unlicensed replacement song and `.meta` outside `Assets/`
   until the asynchronous build has fully finished, require `build_inputs_dirty=no`, then restore both local
   files and publish the zip as a GitHub Release.
2. Human-play the V18 Sandbox pad and report feel/tell issues before promoting it from a test enemy.
3. Continue ordinary work through MCP. Preserve every game change from `unity-cli-pilot`; do not reset to the
   old `master` tip or remove the Pipeline package merely to change transport priority.

## Preserved user-owned files

Do not stage, remove or overwrite `.claude/settings.local.json`, `Portraits/`, unrelated `RouteShots/`, or
`Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and its `.meta`.
