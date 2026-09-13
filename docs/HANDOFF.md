# Handoff — Level Studio, parry choreography, and dashboard

## Resume instruction

Read `AGENTS.md` and this file, then resume in the existing feature worktree:

`C:/Users/tyler/Main Storage/vibegame1/.worktrees/level-studio`

The approved master plan is `docs/superpowers/plans/2026-09-12-level-studio-dashboard-implementation.md`.
The approved design is `docs/superpowers/specs/2026-09-12-level-studio-dashboard-design.md`.
Do not restart the design discussion. Continue the reviewed implementation sequence below.

## Safe checkpoint — 2026-09-12

The user deliberately ended the long session after the active reviews. `master` is clean except for the
preserved user-owned files listed below. Accepted increments are committed on both the isolated feature branch
and `master`; the only uncommitted feature-worktree files are the validator/diff lane, which is not accepted yet.

Rollback tag: `pre-level-studio-2026-09-12` at `894da95`.

### Accepted and committed

`master` now ends at:

- `60d4ae2 [Astra] Save Unity metadata and rebuilt Level 1`
- `9bde0ab [Astra] Extract safe level world construction`
- `46cda7c [Astra] Fix Level Studio hierarchy assertion`
- `dc7b6ed [Astra] Add Level Studio browser and hierarchy`
- `244f162 [Astra] Save Challenge Route migration`
- `939d232 [Astra] Add protected level drafts and recovery`
- `9f2f878 [Astra] Rename challenge routes and remove hand markers`
- `25b08b8 [Astra] Save Level 1 zone vocabulary`
- `1c7dbd1 [Astra] Add level zones and stable object vocabulary`

Feature branch `level-studio` contains the corresponding source commits through `1a0df5b`; main additionally
owns Unity-generated `.meta` files and the regenerated Level 1 scene.

Implemented behavior:

- Stable object IDs, five Zone Volumes, common level/enemy vocabulary, and Challenge Route naming without
  Insight branding or hand symbols.
- Protected campaign working copies with atomic save/autosave, recovery generations, future-schema read-only
  handling, fingerprints, source-state reporting, and exact change accounting.
- A searchable Level Studio browser with protected campaign copies, custom levels, deterministic Resume Last
  Draft, recovery nesting, zone/route/type hierarchy, alias search, stable selection, and durable scene-editor
  consumer fields.
- A reviewed reusable world-content construction seam. Campaign building retains scene cleanup, NavMesh,
  Player/Managers/HUD, runtime behavior, and saving. PreviewSafe construction uses an explicit root and makes
  generated gameplay components/colliders inert while preserving authored visuals and marker data.
- The shipped Level 1 asset and scene now use `challengeRoutes`; the old marker presentation fields are gone.

Review evidence:

- Browser review: PASS in `.superpowers/sdd/2026-09-12-level-studio-dashboard-implementation/level-studio-task-2-review.md`.
- World seam review: PASS in `.superpowers/sdd/2026-09-12-level-studio-dashboard-implementation/world-content-seam-review.md`.
- Draft-store final review: PASS in `.superpowers/sdd/2026-09-12-level-studio-dashboard-implementation/level-studio-task-1-final-review.md`.

## Verification at this checkpoint

The Unity bridge was reconnected and the open editor was stopped on `Assets/Scenes/Level_01.unity`.

- Level vocabulary + Challenge Route + traversal focused suites: **50/50 passed**.
- `LevelDraftStoreTests`: **99/99 passed**.
- `LevelStudioBrowserTests`: **19/19 passed** after correcting one test that compared IDs to expected keys.
- `LevelWorldContentBuilderTests`: **3/3 passed**.
- `LevelDefinitionBuilder.BuildHeadless(Level_01)` succeeded after the seam extraction and saved the scene.
- Unity console contained no compile errors. Health Check completed with the existing broad serialized-null
  warnings; no new error section was observed.
- The ignored offline aggregate harness currently builds with **0 warnings, 0 errors**.

Known verification-tool defect: `py -3 Tools/level_arc_offline.py --after --sight` currently crashes because
its parser still looks for the obsolete `Reshapes`/`Ramps` table names and reads zero rows after the authoring
tables became `OpenCourseDecks`/the current ramp source. Fix this during the dashboard/docs tooling audit; do
not treat its present crash as a level-geometry failure.

## Exact next action — validator/diff review loop

The feature worktree has exactly these uncommitted implementation files:

- `Assets/Editor/LevelStudio/LevelStudioValidator.cs`
- `Assets/Editor/LevelStudio/LevelDraftDiff.cs`
- `Assets/Editor/Tests/LevelStudioValidationDiffTests.cs`

They compile in the offline aggregate harness, but are **not accepted or committed**. The first two reviews
failed. The latest correction pass was interrupted only to create this safe session boundary, so begin by
reviewing the current bytes against both:

- `.superpowers/sdd/2026-09-12-level-studio-dashboard-implementation/validator-diff-review.md`
- `.superpowers/sdd/2026-09-12-level-studio-dashboard-implementation/apply-transaction-audit.md`

Do not commit unless the review passes. Load-bearing requirements include: every validation error blocks diff
without throwing; immutable structured reports; kind-aware stable IDs; null/duplicate handling; run scoring,
resource/reference, traversal/encounter, warning, child-owner and split-agreement gates; complete `$level` and
`$zone/<id>` plus disabled nested inventory; deterministic grouped classifications; and real multi-element
reorder coverage. After review passes, commit the three files plus the validator/diff DATAFLOW paragraph, cherry-pick
to `master`, refresh Unity, read console errors, and run `VibeGame1.Tests.LevelStudioValidationDiffTests`.

## Remaining approved implementation order

1. Finish Level Studio scene editing: preview host, full adapter matrix, native SceneView handles, numeric inspector,
   multi-edit, duplicate/copy/paste/delete, Undo/Redo, focus, hide/isolate, zone overlays, and lifecycle/isolation tests.
2. Finish safe validation/diff/apply/rollback plus the editor-only F10 draft-ID bridge. Use the complete apply audit;
   it requires full-schema `LevelDocument`, structured builder results, source+scene rollback, atomic draft rebase,
   and an editor-side bridge because runtime code cannot reference `Assets/Editor`.
3. Implement parry choreography: `timing prime`; developer-gated `0` start/stop; automatic single JSON export under
   `timing-captures`; raw desired-parry beats plus movement/zone/split/object/projectile lifecycle data; Flat,
   Downhill, Water Slideway, and Mixed regenerable recording stages; deterministic one-best-fit draft module.
4. Complete dashboard/docs cleanup: categorized navigation, Archive, search, docs-health checks, derived Level Map,
   common vocabulary, human editing/debug guides, parry-recording workflow, Open Capture Folder, and the stale
   `level_arc_offline.py` parser fix.
5. Run focused suites after every increment, then full runtime/editor builds, full EditMode, fresh Level 1
   FeatureTests with `GameManager.I != null` and `Time.timeScale == 1`, reports, and honest human-only feel checks.
6. Rewrite `docs/VERIFICATION-REPORT.md` and this handoff. Do not publish a new friend build unless the user asks
   after human acceptance.

## Preserved user-owned files

Do not stage, remove, overwrite, or relocate:

- `.claude/settings.local.json`
- `Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and its `.meta`
- `Portraits/`
- all `RouteShots/` files and directories
