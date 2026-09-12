# Level Studio, shared vocabulary, and human development dashboard

Date: 2026-09-12  
Status: user-approved design

## Purpose

Turn the existing F10 level tweak mode into one half of a dependable hybrid authoring workflow. A Unity
Editor **Level Studio** window owns serious editing; F10 remains the fast player-eye adjustment and playtest
surface. Both edit one working copy of the existing `LevelDefinition` data and use the existing
`LevelPieceFactory` build path.

The same change establishes canonical spatial vocabulary, replaces Insight branding with Challenge Routes,
turns the timing capture into a parry-choreography authoring tool, and reorganizes the generated development
dashboard into a practical manual for humans.

Standalone distribution builds are explicitly out of scope. These tools are for development inside the Unity
Editor. Existing release-build developer gates remain intact.

## Product principles

1. A campaign original is never the live editing document.
2. Every meaningful level object is data, selectable, named, searchable, and owned by one zone.
3. The serious editor and player-eye editor share one document rather than exporting between competing
   formats.
4. A human can discover how to change, regenerate, validate, debug, and undo a system from the dashboard.
5. Validation describes the actual shipped data. Code defaults and unbuilt scene state are not evidence.
6. Generated parry choreography is deterministic, inspectable, manually adjustable, and never applied directly
   to the campaign.

## 1. Hybrid authoring architecture

### Level Studio

Add an editor-only Level Studio window as the primary authoring surface. It loads registered
`LevelDefinition` assets, creates protected working copies, previews them with the existing piece factory,
and uses Unity Scene view handles and Undo for manipulation.

The window layout is:

- **Browser and hierarchy, left:** Campaign Originals, Working Copies, Custom Levels, and Recovery; beneath
  the open level, a searchable tree grouped by zone, route, and object type.
- **Scene view, centre:** native selection and transform handles plus editor overlays for zones, names, route
  lines, projectile coverage, entry/rejoin anchors, and validation markers.
- **Inspector, right:** common identity/transform/zone fields followed by type-specific data fields.
- **Validation and history, bottom:** collapsible errors, warnings, source revision, autosaves, and the current
  before/after change summary.

Level Studio supports native move/rotate/scale handles, local/world axes, numeric values, configurable grid
and angle snapping, multi-select and box-select, duplicate, copy/paste, delete, undo/redo, focus selection,
hide/isolate, filters, and visibility toggles. Destructive actions participate in Unity Undo and campaign
application has an additional disk backup.

### Shared working document

Campaign assets remain the canonical `LevelDefinition` format. Opening one creates or resumes an editor-only
working copy with:

- a stable draft ID and display name;
- source asset GUID, source level ID, source commit/fingerprint, and creation time;
- last manual save and autosave timestamps;
- the full editable level document;
- validation summary and change count.

Draft and recovery files live in a dedicated project-local `LevelDrafts/` area excluded from player builds and
source control by default. Autosave uses a bounded rotation per draft, writes atomically through a temporary
file, and never overwrites the last manual save. A damaged newest autosave falls back to the prior valid copy.

F10 attaches to this same working document. **Edit** rebuilds the preview from it; **Play** rebuilds gameplay
from it; returning to Level Studio refreshes the same draft. The current `RuntimeLevelDocument` becomes an
adapter/serialization view of the shared data rather than an independent source of truth.

### Entry and scope

The Unity menu exposes **VibeGame1 / Level Studio**. During editor play mode, `editor unlock dung` reveals
the Level Editor entry on the game menu for the process and F10 continues to toggle edit/play. No new
standalone-build workflow is required.

Every placed level entity is editable: platforms, ramps, walls, water, balloons, traversal pieces, enemy
spawns, enemy data references, perch/aim settings, projectile sequences, Challenge Routes, checkpoints,
player start, kill volumes, arenas, gates, boss portals, pickups, spell altars, torches, lights, leaderboard,
and safe material/visual presets. Raw shader internals and unrelated project systems do not appear in the
level inspector.

## 2. Protected load, save, and apply flow

The level browser exposes:

- **Campaign Originals:** protected registered assets; the action is Create Working Copy.
- **Working Copies:** resumable drafts with source, age, validation status, autosave time, and changed count.
- **Custom Levels:** levels created from scratch through the same schema.
- **Recovery:** autosaves grouped beneath their draft, newest valid save first.

Search accepts level name, zone/split, canonical object ID, friendly name, object type, shipped data key, or
documented alias. Double-click loads. Resume Last Draft is available at the top.

**Apply to Campaign** follows one transaction:

1. Compare the source fingerprint to the draft's base fingerprint; a changed source blocks blind apply.
2. Run schema, reference, spatial, traversal, encounter, and campaign-flow validation.
3. Show additions, removals, transform changes, tuning changes, and zone changes grouped by zone/object.
4. Require explicit confirmation for warnings; errors cannot be overridden.
5. Write a timestamped rollback snapshot of the canonical asset.
6. Update the canonical `LevelDefinition`, rebuild through the existing builder, and run the level reports and
   targeted tests.
7. Mark the draft applied only after all required verification succeeds. On a write/build failure, restore the
   snapshot and report the failed stage without discarding the draft.

Blocking errors include duplicate or malformed IDs, no/multiple zone ownership, broken data references,
missing player start or required checkpoints, invalid enemy data, unreachable required route, impossible
arena/gate flow, and a failed canonical rebuild. Warnings include near-boundary ownership, sparse lighting,
unusual spacing, a Challenge Route without a supporting enemy, and other legal but suspicious arrangements.

## 3. Zones and common vocabulary

One editor-only-visible **Zone Volume** represents both a physical zone and its scored split. Its metadata is:

- stable zone ID, for example `T2`;
- canonical zone name, for example `Helix Tower`;
- scored split name, for example `Knight`;
- aliases, description, order, bounds, and display colour.

The volume is selectable and resizable. Object membership is derived from the object's authoring anchor inside
the volume. A manual override exists for deliberate edge cases, is visually marked, and is included in the
change summary. An object in zero or multiple primary volumes is an error. Optional nested route/encounter
overlays may overlap and do not alter primary zone ownership.

New object IDs are generated deterministically within their zone and type, then remain stable when moved:

- `T2.Platform.L8`
- `T2.Sentry.02`
- `T4.SurgeTurret.03`
- `T3.ChallengeRoute.01`

Friendly names remain editable. Search and documentation resolve aliases such as “blue squid,” “last ramp,”
and “heavy turret” to canonical types and placed IDs. Renaming a friendly label never breaks references;
changing a canonical ID is an explicit refactor that updates references through one command.

`LEVEL-VOCABULARY.md` describes conventions, but the inventory shown by Level Studio and the dashboard is
derived from the same shipped level metadata. It is not maintained as a second hand-written object list.

## 4. Challenge Route migration

Keep the existing optional faster/harder flare-sentry route gameplay. Remove the **Insight** brand and every
hand-shaped world marker.

Migrate `InsightRouteDef`, its serialized field, authoring IDs, marker/export vocabulary, tests, dashboard, and
current documentation to `ChallengeRoute`. Preserve serialized data with Unity migration attributes or a
deterministic migration step so no route anchors disappear. The rebuilt campaign creates no hand geometry.
Historical reports remain unchanged inside Archive and are clearly labelled historical.

## 5. Parry Choreography Recorder and deterministic module generation

### Intent

The recorder is primarily an encounter-authoring instrument. A designer performs the desired traversal and
parry rhythm; the system captures the path and beat intent, then arranges/tunes existing projectile enemies so
their projectiles meet that run at the recorded beats.

### Base recording stages

Provide a small regenerable recording level with named stage presets:

- Flat Walkway
- Downhill Ramp
- Ice/Water Slideway
- Mixed Traversal

Each stage has known geometry, zone metadata, a player start, a clean sightline envelope, and no authored
enemy rhythm. The presets are data-built and tested like other project content.

### Controls and capture

`timing prime` arms the system; `timing start` remains a compatibility alias for priming. Once armed, `0`
toggles capture. Starting clears the pending take and shows **PARRY RECORDING**. Stopping always writes one
JSON file atomically into the system-owned `timing-captures` folder and reports the exact path. There is no
unsaved stopped state. `timing status` and `timing discard` remain diagnostic controls; manual export is not a
second format or required normal step.

The JSON records:

- stage/level, zone/split, timestamps, sample rate, and recorder version;
- player position, look direction, velocity, speed, grounded/slide/wall-run/dash/pull state, and contacted
  surface type;
- every parry press as a desired contact beat, including position, direction, motion state, and surrounding
  cadence;
- existing projectile emission/cue/arrival/outcome when present;
- canonical shooter/spawner/projectile/route IDs when the run occurs in an authored level.

### Solver

One capture produces one deterministic best-fit module. The solver:

1. Cleans duplicate/accidental taps using explicit minimum-spacing rules while retaining the original take.
2. Divides the path into candidate firing windows using speed, surface, line of sight, and zone bounds.
3. Chooses only existing parkour-projectile enemy types and legal data-driven timing controls.
4. Solves enemy perch/position, facing, acquisition, shot phrase, and projectile flight so predicted contacts
   match the recorded beat positions/times without obstructing the intended view or route.
5. Preserves cue-safety, min/max range, blocker, route-clearance, and rapid-phrase constraints.
6. Emits a working-copy module containing geometry references, enemy spawns, projectile sequence data, and
   entry/rejoin anchors.

The result opens in Level Studio and reports per-beat time error, spatial error, visibility, enemy choice, and
overall confidence. An unsatisfied beat is never silently omitted. The designer can move/tune objects, rerun
the solver for selected beats, playtest through F10, and validate before placing the module into a campaign
zone.

## 6. Human development dashboard

Reorganize the generated dashboard navigation into collapsed task categories:

1. **Start Here** — current status, handoff, controls, and “where do I change this?”
2. **Level Building** — Level Studio, working copies, vocabulary, Level Map, movement/reach constraints, and
   apply/recovery.
3. **Combat & Enemies** — enemy families, data assets, movesets, projectile/parry timing, and safe tuning.
4. **Systems** — movement, spellbook/items, UI, audio, VFX, scoring, saves, and system dependencies.
5. **Testing & Debugging** — health check, test suites, common failures, capture inspection, and Parry
   Choreography Recorder/module generation.
6. **Tools & Distribution** — generators, builds, releases, traceability, and rollback.
7. **Archive** — historical handoffs, dated verification, superseded plans, and engineering history; collapsed
   by default but searchable.

Current pages use a practical template: purpose, user workflow, data/source locations, safe edit points,
regeneration, verification, debugging symptoms, and rollback. The dashboard distinguishes current truth from
history and does not present an old test count or handoff as current status.

Add an audit that reports broken local links, uncategorized documents, obsolete current terminology,
duplicate/conflicting current instructions, missing source/data references, and current feature claims without
matching code or shipped assets. Historical text is exempt from terminology rewrites but visibly archived.

The Level Map reads zone metadata and shipped level objects to render each zone's bounds, split, aliases,
platforms, enemies, routes, checkpoints, and object IDs. Enemy aliases resolve to their canonical family and
placed instances.

The recorder guide contains the exact prime/`0` workflow, base stage selection, JSON path/schema, module
generation, confidence/error interpretation, manual correction, and validation steps. The local served
dashboard offers copy-command buttons and an allowlisted **Open Timing Captures Folder** action bound only to
`127.0.0.1`; the static-file version shows a copyable absolute path instead.

## 7. Error handling and compatibility

- Draft parsing is versioned. Unsupported newer versions are read-only and never rewritten.
- Missing referenced assets appear as blocking errors with the owner object ID and expected key.
- Scene preview/build exceptions keep the draft and show the failed object/stage.
- Apply conflicts never auto-merge transforms or lists; the user creates a fresh copy or deliberately rebases.
- Existing custom JSON loads through a migration adapter and is saved in the new version only after validation.
- Existing F10 bindings remain available. Runtime editor controls continue to flow only through `InputReader`.
- Level Studio uses Unity Editor events/handles, not the runtime Input System.
- Existing generated campaign roots remain regenerable; editor preview objects are never mistaken for manual
  scene content.

## 8. Verification and acceptance

Automated verification must cover:

- campaign originals remain byte-identical through load/edit/cancel;
- working-copy autosave, recovery fallback, source-conflict detection, and apply rollback;
- round-trip fidelity for every `LevelDefinition` object array;
- selection, multi-edit, transform, duplicate/delete, undo/redo, stable IDs, and search aliases;
- zone containment, boundary, override, overlap, orphan, and ID uniqueness rules;
- Challenge Route data migration and complete absence of generated hand markers;
- recorder prime/`0` state transitions, one JSON format, atomic auto-export, surface/movement/beat fields, and
  bounded capture buffers;
- deterministic solver output from fixed captures, per-beat reporting, LOS/range/cue/clearance constraints,
  and no silent dropped beats;
- dashboard category assignment, collapsed Archive, vocabulary rendering, link audit, terminology audit, and
  safe local folder action;
- offline runtime/editor compilation, full EditMode tests, fresh-session FeatureTests, Health Check, Level Arc
  Report, and Projectile Encounter Report.

Human acceptance requires:

1. Open Level Studio and create a working copy of Level 1 without consulting source code.
2. Jump to a named zone, locate an enemy by alias, move it and a platform, undo/redo, then play the draft.
3. Return through F10, confirm the same changes, and recover an autosave.
4. Review an apply diff, observe an intentional validation block, fix it, and apply/rebuild successfully.
5. Record desired rhythms on flat, ramp, ice/water, and mixed stages; generate the same module twice and obtain
   the same result; play and manually refine it.
6. Use the dashboard alone to find the relevant data, change path, regeneration command, tests, capture file,
   and rollback procedure.

## 9. Delivery boundaries

This pass improves the existing Fable/Opus systems under the user's explicit authorization. It reuses their
data and factories rather than replacing combat, movement, projectile resolution, or the campaign builder.
Changes to a mapped system update `docs/DATAFLOW.md` in the same commit. New recording stages and editor data
are authored as data and generated through editor factories. The work is divided into rollback-safe commits
for the shared document/vocabulary, Level Studio UI, recorder/solver, Challenge Route migration, dashboard
reorganization, and final integration/verification.
