# Handoff — two ramps and the complete Level 1 route

2026-09-07. Astra/high designed and implemented this correction; lead Codex generated and verified it.

## What happened

The previous start relocation skipped most of Level 1. The user clarified that the late ramp should
stay, with a second ramp added BEFORE the original start. This pass implements that layout:

- New opening: 10 m wide crest at y9, a 36 m downhill run dropping 9 m, then a flat run-out joining
  Ground_Start. Player starts at `(0,9.3,-55)`, facing downhill; weapon pedestal is beside it.
- The original starting area, checkpoints and complete route remain in place.
- The existing 48 m / 12 m descent and three Surge Turrets remain before the boss. Serialized late
  platforms, ramp, turrets and arenas matched the captured pre-change baseline exactly.
- Kill-plane bounds extend behind the new opening, from z-80 to450.

This is level authoring only. Movement, combat, enemies, fog and projectile presentation are unchanged.
The earlier drifting mist and projectile work remains in `c396802` and `a927bd0`.

## State of the tree and rollback

This correction lands as `[Astra] Add an opening descent before the complete Level 1 route`.
Revert that commit to undo only this pass. Restore tag: `pre-opening-ramp-2026-09-07` at `6964424`.
The user's untracked `.claude/settings.local.json` is excluded.

Completed generators: `LevelDefinitionAuthoring.ReworkLevel01()` twice (identical serialized data),
then `LevelDefinitionBuilder.BuildCanonicalHeadless()` (scene saved and NavMesh rebuilt). Saved
StartSpawn, Player and LevelManager.startSpawn all read `(0,9.3,-55)`. No other generator is required.
The authored shape and verification intent are in `docs/plans/opening-ramp-2026-09-07.md`.

## Verification

See [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md) for final results and limitations. Both offline
assemblies compile; the editor has 16 existing warnings. This geometry change requires the full
EditMode suite, including level-line checks. Automation does not prove human feel or fairness.

Final full EditMode: **843/843 passed**. Full FeatureTests: **776/777**, one pickup-reset assertion
failed. Main-menu load, pre-checkpoint respawn, continuous opening slide and movement onto the original
starting deck passed live. Arc report is clean. The full feature result is not reported as green.
The isolated Items group subsequently passed **43/43**. Final Health Check: **0 errors, 1764 warnings**.
Unity is stopped with Level_01 open; temporary frame-rate settings and observers were restored/removed.

## Do first next session

1. Play Level 1 from the main menu: new opening ramp, full original course, late turret descent, boss.
2. Read the verification report before changing gameplay to address a context-dependent harness result.
3. Preserve the two-ramp ordering; never restore the late descent as Level 1's starting spawn.

## Open questions

None blocking the requested layout. No standalone build was cut during this pass.
