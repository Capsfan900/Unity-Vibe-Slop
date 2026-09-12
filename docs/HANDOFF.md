# Handoff — Unity CLI pilot and V18 Souls-enemy prototype

## Current state — 2026-09-11

Work is on `unity-cli-pilot`; `master` remains the pre-CLI baseline at `17f81f0`, protected by
`pre-unity-cli-pilot-2026-09-11`. The working branch still needs its final commit and push.

The V18 Flurry Brawler is implemented as a completely separate test Souls-enemy lineage. It uses ordinary
melee `EnemyController` and `PlayerCombat.ReceiveAttack`, with its own data, nine attacks, moveset, prefab,
Generic FBX clip library, Animator controller and `FlurryBrawlerV18Visuals` presentation layer. It has no
parkour projectile, surge or boss components and does not replace V15. Its independent Sandbox pad is at
`(112, FloorTop, 20)`.

The earlier in-flight release pass is also present: centralized process-local `DeveloperAccess`, locked
release diagnostics, transactional build/publish scripts, the persistent-wand toothpick repair, regenerated
HUD/Main Menu and the official Unity CLI/Pipeline pilot. Preserve the user-owned files listed below.

## Verification completed

- Full EditMode: **963/963 passed**, zero failures/skips, 49.34 s.
- Fresh Level_01 FeatureTests: **813 passed, 0 failed, 1 intentional V18/Sandbox skip**, 60.4 s.
- Focused Sandbox `V18Smoke`: **21/21 passed**, zero failures/skips, 9.6 s.
- V18 smoke proves committed Clap survives ordinary damage, blocked/perfect Clap each resolves and grounds
  once with one ring, Shoulder shows Run→contact with one 4.12 m lunge, and Combo2 has one contact plus tail.
- Runtime/editor offline builds: zero errors; editor retains 18 existing warnings.
- Health Check: no error section; 1942 existing broad serialized-null/audio warnings.

Human review remains required for the animation feel: Dash-as-rising-clap readability, shoulder/body contact,
Clap weight, and fair manual parry tells.

## Unity licensing and tool result

The failed disposable probe is not caused by too many projects or a missing AnimationModule. Raw
`Unity.exe -batchmode -createProject` bypassed Hub bootstrap/authentication, lost the Licensing Client pipe,
registered zero built-in packages, and only then failed to resolve `Animator`. Hub-authenticated live-editor
execution works.

Unity CLI `1.0.0-beta.8` with `com.unity.pipeline 0.7.0-exp.1` helped with short state/preflight/eval calls and
successfully drove several generators. It is not yet a replacement for MCP: Pipeline can disappear around a
domain reload, and a long generator can exceed the package's five-second main-thread response window even
while Unity finishes writing assets. Use CLI for short live-editor queries; use MCP and asset readback for
long generators/tests. Never run `unity test/build/run` against this active working copy.

Unity's `com.unity.ai.assistant` package also waits roughly 30 seconds for its Account API on Play Mode domain
reload, temporarily logging zero matching entitlements before licensing resolves successfully. Confirm
`Time.frameCount` advances, `GameManager.I != null` and `Time.timeScale == 1`; the editor-state phase can remain
stale during that delay.

## Exact resume order

1. Review/stage only intended project changes. Exclude `.claude/settings.local.json`, `Portraits/`, unrelated
   `RouteShots/`, and the unlicensed replacement Radio track.
2. Create/push the rollback tag for this worker batch, commit the branch as one revertible worker-prefixed
   integration, and push `unity-cli-pilot` with upstream.
3. For a public WebGL playtest, temporarily move the unlicensed replacement song and `.meta` outside
   `Assets/`, build from the exact clean commit, verify `build-info.txt` says `build_inputs_dirty=no`, publish
   `gh-pages`, then restore the local files.
4. Human-play the V18 Sandbox pad and report feel/tell issues before promoting it from a test enemy.

## Preserved user-owned files

Do not stage, remove or overwrite `.claude/settings.local.json`, `Portraits/`, unrelated `RouteShots/`, or
`Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and its `.meta`.
