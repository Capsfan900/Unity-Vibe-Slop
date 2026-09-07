# Handoff — five opening parries and solar boss realms

## What happened

2026-09-07: Sol 5.6 implemented the requested opening turret sequence and four solar boss realms,
with Astra reviewing the design and engineering. The opening row is LEFT, RIGHT, LEFT, then two
above on one stepped cyan floating platform. The overhead pair sits at (-2.4,8.6,15) and (3.2,8.6,15).
Two live runs earned all five distinct grants and the existing 1.60x speed multiplier before the
first jump. The encounter-local coordinator uses existing projectiles and combat resolution.

Four rotating, glowing spheres remain at the original court anchors. Crossing one transports the
player into a stationary enclosed realm matching its cyan/gold/azure/green theme. The three existing
legendary keepers remain mini-bosses; defeating them unlocks a return to the onward route. The existing
three-phase final boss retains run completion. F5 and the debug boss harness resolve the new entrance.
Realm cells are at x700, beyond the route camera; historical authored spawn anchors survive export.

Both ramps, the corrected opening spawn, full original route, downhill fix, rolling cloud sea and
projectile weave remain. Earlier commits: opening ramp 7a68d19, cloud sea 0db52f0, weave a927bd0.
Spark was requested but is unavailable in this session; Sol performed edits with Astra consulting.

## State of the tree

Current pass is verified and saved as a single [Sol] commit. Restore tag
pre-solar-realms-2026-09-07 points to 0db52f0; reverting this pass restores the prior two-ramp/cloud level.
The user's untracked .claude/settings.local.json is excluded.

Generators run: CreateSolarArenaMaterials, ReworkLevel01 (idempotence verified), and
BuildCanonicalHeadless including NavMesh and saved Level_01. No prefab rebuild is needed: only the
five named scene instances opt into the coordinator. Solar ceiling opacity is serialized on its
visual component and reapplied on enable; floors use flat MeshColliders and ceiling motion is Y-only.

## Verification

- Full EditMode: 861/861 passed, no failures/skips, 225.4 seconds.
  TestResults/EditMode-20260907-193216.xml.
- Full FeatureTests: 804/804 passed, no failures/skips, 67.0 seconds, fresh unpaused Play session.
  TestResults/solar-realms/features-final.txt. Final quick EditMode after harness fixes: 723/723 passed,
  TestResults/EditMode-20260907-195202.xml. The full 861-test run covered the final gameplay/geometry;
  subsequent changes only isolated staged tests from real turret targets and fixed test placement.
- Opening live probe passed twice: five shots, five distinct parries, peak multiplier 1.60.
  Final contacts: 1.070, 1.687, 2.387, 3.070, 3.654 seconds after slide launch.
  Probe drives forward intent after the slope and uses forecast parries, so this proves integration,
  not human timing/fairness. TestResults/solar-realms/opening-final.txt.
- All three mini portals: physical entry, locked exit, complete NavMesh path to keeper, defeat,
  unlocked exit and physical return onto the correct grounded deck passed at final realm positions.
- Final sphere: physical entry activated the existing boss; three executes consumed three segments
  and stopped the speedrun timer. Standalone reset rescue, death checkpoint and re-entry passed.
- A 123-soul pickup inside a realm survived enemy reset and was recovered on re-entry. The ordinary
  death trial reclaimed its pickup immediately while the dying player still overlapped it; the
  unchanged Bloodstain trigger accepts that overlap. Do not claim persistent death-drop delay fixed.
- Health Check: 0 errors, 1764 existing warnings. Both offline assemblies compile with zero errors.
  Unity is stopped on the saved Level_01 scene. Test frame settings and incidental settings
  reserialization are restored; no generator or verification step remains due.
- Generated sun/realm renders reviewed; coloured patterns replace the initial white washout.
  RouteShots/solar-realms contains the final scene captures. Shader support and opacity persistence
  were read back in Unity. Human artistic acceptance and standalone/WebGL GPU performance are unproven.

## Do first next session

1. Read the final verification report and git status; preserve the user's local settings file.
2. For human playtesting, load Level 1 from the menu: start above the opening descent, parry the
   left/right/left/overhead pair, then cross each sun and defeat its keeper to continue.
3. If the user dislikes this pass, revert its single [Sol] commit; the restore tag identifies its parent.

## Open questions for the user

None needed to complete the implementation. Human feel and appearance feedback can guide refinement.
