# Handoff — ramp start, route mist and incoming-shot visuals

2026-09-07. Lead Codex; Sol implemented the fog, projectile presentation and final harness fixes.
An attempted Astra/high follow-up launch hit the thread limit; do not attribute the visual pass to Astra.

## What happened

Level 1 loads at the downhill crest `(0,28.3,297.5)` with its starting wand pedestal beside it.
The real Main Menu load path was verified. The earlier route remains authored in the level.
The 48 m downhill ramp, three Surge Turrets and downhill slide refinement were implemented in
`691fc89`; the corrected start/pedestal is `a12fbec`.

Both requested atmosphere layers are implemented: visible drifting route mist plus stronger cold
linear distance fog (36–140 m). Mist is world-space, prewarmed, capped at 48 particles, aligned to
Default-layer ground every 0.25 seconds, and faded near the camera. The amber enemy shots now have
bounded visual weave and a curved history trail; they converge to the logical trajectory before the
parry cue and remain centered after reflection. Violet reward flares are unchanged. No historical
wobble implementation was found; this is new visual motion over the existing homing behavior.

## State of the tree and rollback

- `a927bd0` — `[Sol] Add bounded pre-cue motion to enemy bolts`.
- `c396802` — `[Sol] Add drifting route mist and stronger distant haze`.
- The final harness-only follow-up is the commit titled `[Sol] Correct physical pickup and flask test staging`.
- Undo either visual pass independently with `git revert <commit>`; undo both using
  `git revert c396802 a927bd0`. Restore tags: `pre-fog-flare-2026-09-07`,
  `pre-descent-start-2026-09-07`, and `pre-ramp-descent-2026-09-07`.
- The user's untracked `.claude/settings.local.json` is excluded.

Generators completed: the narrow `PrefabFactory.BuildPlayer` generator rebuilt Player; authoritative
ProjectSetup fog values were applied through the editor and saved in Level_01 and Sandbox. The full
ProjectSetup/BuildAll pipeline was not rerun, to avoid unrelated generated changes. The factories
contain every shipped value. Level data/scene/NavMesh generation was completed for the earlier ramp
and start correction. No generator is needed for runtime-created projectiles or the harness fix.

## Verification

See [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md) for final suite results and historical failures.
Final quick EditMode: **701/701**. Final full FeatureTests: **776/777**, with the uninterrupted-heal
subcase failing; a fresh isolated Flask/FlaskPunish run then passed **11/11**. Its remaining
full-suite context dependence is not fully diagnosed. Do not claim a green full suite.
Both offline assemblies compile (16 existing editor warnings, no errors). The original ramp pass ran
831 full EditMode tests successfully; the visual pass uses the 701-test quick suite because geometry
and motor code are unchanged. Health Check reports 0 errors and 1764 warnings.

The final saved encounter passed all three surge grants at capped 60 fps with mist and weave active.
Live visual observations confirmed a 0.34 m maximum weave, zero cue/reflection offset, 36–38 mist
particles, correct slope alignment and balanced material leases. Before/after captures are in
`RouteShots/fog-flares/`. Temporary editor observers are removed and frame-rate settings restored.

Repeated full-suite failures were traced to test staging: 14 m/s impulses stopped short of pickup
triggers, and the flask test inherited a falling player whose respawn looked like healing. The
harness-only follow-up increases its two entry impulses to 20 m/s and grounds the flask test at the
current checkpoint. Assertions and gameplay systems are unchanged. Detailed ignored evidence lives
in `TestResults/descent/`; the neutral-input run and diagnostic failures remain recorded honestly.

## Do first next session

1. Human-playtest Level 1 from the main menu for ramp, cue and atmosphere feel.
2. Consult ENGINEERING-LOG before chasing feature-suite staging or editor polling failures.
   If improving the suite, diagnose the remaining context-dependent uninterrupted-flask failure.
3. Check standalone/WebGL shader variants and performance before claiming build-level validation.

## Open questions

None blocking the requested implementation. No new player build was cut; editor automation does not
prove human fairness, full-course aesthetics or standalone performance.
