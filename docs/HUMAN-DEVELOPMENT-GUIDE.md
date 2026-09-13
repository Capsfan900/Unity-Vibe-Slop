# Human Development Guide

## Purpose

Use this page to choose the smallest safe workflow for changing vibegame1. Start with [Level Studio](LEVEL-EDITOR.md)
for course edits, [Authoring](AUTHORING.md) for content data, and [Architecture](ARCHITECTURE.md) before system work.

## Common Tasks

- Build or adjust a level: follow [Level Authoring Tutorial](LEVEL-AUTHORING-TUTORIAL.md) and use the shared
  [Level Vocabulary](LEVEL-VOCABULARY.md).
- Tune combat or enemies: edit the ScriptableObjects described by [Authoring](AUTHORING.md), then regenerate.
- Design projectile rhythm: follow [Parry Choreography](PARRY-CHOREOGRAPHY.md).
- Find or run a tool: use the [Tooling catalogue](TOOLING.md) or generated dashboard.

## Where Data Lives

Levels live under `Assets/Data/Levels/`; enemies, attacks and movesets live under `Assets/Data/Enemies/`,
`Assets/Data/Attacks/`, and `Assets/Data/Movesets/`. Runtime flow is mapped in [Dataflow](DATAFLOW.md).

## Safe Editing

Create a Level Studio working copy; shipped originals are protected. Content belongs in data assets, not new
hardcoded builders. Preserve `Level_Manual` and `Sandbox_Manual` roots and never hand-edit scene or prefab YAML.

## Regeneration

Exit play mode, run the narrow generator listed in [Tooling](TOOLING.md), and read the generated asset back.
Use `VibeGame1/0. Rebuild Everything` only when the complete dependency-ordered rebuild is intended.

## Verification

Run focused EditMode tests first, then the full suite, Health Check, level arc and projectile encounter reports.
Use a fresh play session for FeatureTests; the [Verification Report](VERIFICATION-REPORT.md) records proven facts.

## Debugging Symptoms

Check [Engineering Log](ENGINEERING-LOG.md) before guessing. A blank scene often means a compile error; a dead MCP
call may be a modal dialog or lost bridge; many plausible timing failures usually mean a paused or stale play session.

## Rollback

Keep changes in small commits. Revert the offending commit or restore the pre-batch tag; do not reset over unrelated
user work. Distribution and build rollback details live in [Distribution](DISTRIBUTION.md).
