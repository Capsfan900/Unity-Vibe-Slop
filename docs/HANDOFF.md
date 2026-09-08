# Handoff — longer opening and enclosing suns

2026-09-07: Sol 5.6 refined the opening with Astra design/engineering consultation. Spark was requested
but unavailable. The lead regenerated, inspected and verified the canonical Unity scene.

## Implemented

- Opening ramp: 108 m run, 27 m drop, 12 m width. Spawn (0,27.3,-127), pedestal beside it.
  Bottom remains (0,0,-15.8), joining the complete unchanged original route. Later T4 ramp remains.
- Five existing surge turrets at slope progress 20/42/64/86/102 m: left/right/left, then right/left
  overhead. A stepped open Z-shaped floating dais connects the last two; rear turret y9.1,
  front y13.6. The lower rear height prevents its unchanged homing bolt from overflying the runner.
- Optional progress gates 0/14/36/58/78 m hold members until their route beat. Finite readiness
  windows start after the gate and recovery gap. Existing ungated sequences retain their behavior.
- Four fixed-center sun shells enlarged to visual radii 22/23/22/31 m. Old court geometry fits with
  >=1.5 m margin. Physical portal radii remain 12/13/12/18, preserving retries, exits and realm fights.
- Cloud sea extends to 300x740 m centered (0,-5,150); kill zone covers z -160..450.
- Grapple test driver now aims vertically at downhill dummies instead of using melee yaw-only facing.
  Core movement, grapple, projectile flight, parry rules and boost tuning remain unchanged.

## Verification

- Saved-scene opening probe PASS: five shots, five distinct parries, all contacts ON the slope,
  full 1.60 multiplier and run-out reached. Contacts 1.082/1.716/2.418/3.184/3.851 s at
  progress 14.95/30.50/49.81/70.88/89.22 m. TestResults/long-opening/opening-final.txt.
- Full FeatureTests 804/804, zero failures/skips, 66.5 s. TestResults/long-opening/features-final.txt.
- Full EditMode 864 ran: 863 passed, one beam-clearance failure; all 138 slow reachability tests passed.
  Raising that decorative beam 0.3 m resolved it. Quick EditMode 726/726 then passed. Final rerun: TestResults/EditMode-20260907-204123.xml.
  See VERIFICATION-REPORT for exact final rerun paths and the unrelated weighted-choice test flake.
- Health 0 errors / 1764 existing warnings; both offline assemblies compile with 0 errors.
- Seven saved renders inspected: RouteShots/long-opening. Physical/visual sphere sizes read back from
  generated objects; tests measure floor/wall/pillar/gate/torch containment.
- ReworkLevel01 idempotence verified; BuildCanonicalHeadless rebuilt NavMesh and saved Level_01.

Automatic forecast parries prove integration, not human fairness. Human appearance/feel acceptance
and standalone/WebGL GPU cost remain unverified. No required generator remains.

## Rollback and tree

This refinement is one [Sol] commit. Restore tag pre-long-opening-2026-09-07 points to parent 1275950.
Revert this refinement commit to recover the accepted previous two-ramp/sun-realm version.
The user's untracked .claude/settings.local.json is excluded. Test frame settings restored;
Unity is left stopped on Level_01. Earlier accepted opening/cloud/weave/realm work remains intact.

Next: load Level 1 from the menu and feel the longer descent, alternating parries and enlarged suns.
No user decision is needed to complete this pass.