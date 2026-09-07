# Handoff — rolling cloud sea and the complete two-ramp Level 1

## What happened

2026-09-07: the user's fog target is now a continuous rolling cloud bed BELOW the map. Sol at
extra-high effort implemented CloudSea and its URP shader; lead reviewed/refined the appearance,
regenerated assets and verified the result. The old player-relative AmbientMist component is removed
from the shipped Player prefab. Distant haze remains at 36–140 m. No movement/combat changes this pass.

The earlier Astra/high layout correction is committed as `7a68d19`: a new 36 m / 9 m opening descent
before the original start, spawn `(0,9.3,-55)`, and the existing 48 m / 12 m late descent before the boss.
The complete original route remains between them. Projectile weave remains in `a927bd0`.

## State of the tree

Cloud work is one `[Sol] Add rolling cloud sea beneath the full level` commit. Revert that commit to
undo only this presentation pass; restore tag `pre-cloud-ocean-2026-09-07` points to `7a68d19`.
The user's untracked `.claude/settings.local.json` is intentionally excluded.

Generated: `MaterialFactory.CreateCloudSea()`, narrow `PrefabFactory.BuildPlayer()`, canonical
`LevelDefinitionBuilder.BuildCanonicalHeadless()` including NavMesh; Sandbox cloud wiring was applied
through `CloudSea.BuildSandbox()` and saved. Both saved scenes reopened with one sea and no AmbientMist.
Everything is regenerable through the normal material/prefab/scene builders. No generator remains due.
CloudSea's transient mesh releases on disable and regenerates on enable/reload. One renderer, 3977
vertices in campaign / 3185 in Sandbox; serialized material prevents a Shader.Find-only dependency.
Health Check now recognizes custom shaders by their URP pipeline tag as well as the built-in prefix.

## Verification

- Quick EditMode **709/709 passed**, including five new cloud tests; slow unchanged level lines omitted.
- Full FeatureTests **777/777 passed**, fresh unpaused session, neutral input, cap 60 fps restored afterward.
- Health Check **0 errors, 1764 existing warnings**; shader supported with zero compiler messages.
- Offline assemblies compile; editor has 16 existing warnings. Final editor-only validator fix compiled
  in Unity and its check was rerun after the runtime suites.
- Real render review: opening, original start, elevated span, late ramp and detail. Fixed-view cloud
  motion confirmed; pause captures pixel-identical with post-processing grain temporarily disabled.
- Prior geometry pass full EditMode **843/843 passed**; actual menu load, spawn/respawn and opening
  traversal verified. See VERIFICATION-REPORT.md for earlier feature-suite flakiness and exact artifacts.

Unity is stopped with Level_01 open. Screenshots: `RouteShots/cloud-ocean/`. Current feature report:
`TestResults/cloud-ocean/features-final.txt`; quick XML `TestResults/EditMode-20260907-175659.xml`.
No standalone/WebGL performance benchmark or human artistic approval is claimed. The sea is a
shader-animated surface, not volumetric fluid simulation.

## Do first next session

1. Let the user inspect the cloud ocean from the opening and elevated route. Tune its material through
   MaterialFactory if requested; preserve landing-edge readability and clearance beneath platforms.
2. Keep both ramps and the full original route. Loading Level 1 must start at the opening crest.
3. For camera captures, move SkyFollower with the camera before rendering and restore both afterward;
   moving only the camera in one execute_code call produces a falsely black sky before LateUpdate.
