# Handoff — Level Studio, parry choreography, and dashboard

## Current state

The approved implementation in `docs/superpowers/plans/2026-09-12-level-studio-dashboard-implementation.md`
is integrated on `master`. Do not restart its design or replay completed increments. Rollback begins at tag
`pre-level-studio-2026-09-12` (`894da95`).

Implemented:

- Protected campaign working copies, atomic save/autosave and recovery generations.
- Searchable zone/route/type hierarchy with stable selection and source-state reporting.
- Inert reusable scene preview; native transform and zone handles; numeric/multi-edit; copy, paste, duplicate,
  delete, focus, hide and isolate; Undo/Redo and autosave integration.
- Structured validation and deterministic diff; guarded transactional apply with rollback and an editor-only
  F10 draft-ID bridge.
- Developer-gated `timing prime` plus keyboard `0` capture, automatic atomic JSON export, movement/zone/split/
  object/projectile events, and four regenerable recording-stage assets.
- Deterministic best-fit parry module generation using shipped enemy/projectile data, with unsatisfied beats
  reported and draft-only application.
- Categorized dashboard navigation, Archive, search, documentation audit, derived Level Map, capture-folder
  action, human development guide, parry workflow guide, and repaired offline level-arc parser.

## Verification — 2026-09-12

- Full EditMode: **1212/1212 passed**, 0 failed/skipped, 81.1 s.
- Focused Level Studio/parry suites: **140/140 passed**.
- Fresh Level_01 FeatureTests: **813 passed, 0 failed, 1 intentional Sandbox-only skip**, 61.2 s;
  `GameManager.I != null`, `Time.timeScale == 1` before start.
- Health Check: **0 errors**; 3334 existing broad serialized-null warnings.
- Projectile Encounter Report: **PASS** at 11/17.6/27.5 m/s for every campaign shooter.
- Runtime build: 0 warnings/errors. Editor build: 0 errors, 18 known warnings.
- Offline arc/dashboard tests: **5/5 passed**; dashboard regenerated successfully.

## Next action

Implementation is complete. The remaining acceptance is human-only: use Level Studio visibly for edit/Undo/
recovery and blocked/successful apply, record runs on all four surfaces and judge generated-module rhythm, then
click through the dashboard. Do not publish a friend build unless requested after that acceptance.

## Preserved user-owned files

Do not stage, remove, overwrite, or relocate:

- `.claude/settings.local.json`
- `Assets/Materials/M_Water.mat`
- `Assets/Scenes/Level_01.unity`
- `Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and `.meta`
- `Portraits/`
- all `RouteShots/` files and directories
