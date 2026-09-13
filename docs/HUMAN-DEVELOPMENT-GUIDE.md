# Human Development Guide

## Purpose

This is the human entry point for changing **the entire game**: levels, movement, combat, enemies, items,
spellbook presentation, UI, VFX, audio, progression, saves, tools, tests, and friend builds. The dashboard keeps
the complete library available under collapsed categories; this page tells you where to begin without requiring
you to understand the whole codebase first.

The project has three kinds of truth:

1. **Content data** says what ships: levels, enemies, attacks, movesets, items, and feel values.
2. **Factories** regenerate prefabs, materials, scenes, and HUD assets from that data.
3. **Runtime systems** interpret the data. Their ownership is mapped in [Dataflow](DATAFLOW.md).

When unsure, change data before code and run the narrowest generator that owns the result.

## Common Tasks

| I want to… | Start here | Change | Regenerate / verify |
|---|---|---|---|
| See what is unfinished now | [Handoff](HANDOFF.md), then [Backlog](BACKLOG.md) | The smallest owned file | Focused test plus the verification named in the handoff |
| Move, add, or remove level objects | [Level Authoring Tutorial](LEVEL-AUTHORING-TUTORIAL.md) | `Assets/Data/Levels/` through Level Studio or level authoring | Build Level From Definition, Level Arc Report, playtest |
| Understand a place or enemy name | [Level Vocabulary](LEVEL-VOCABULARY.md) and the dashboard Level Map | Zone/split metadata and stable object IDs | Level validation and map rebuild |
| Record and reproduce a parry rhythm | [Parry Choreography](PARRY-CHOREOGRAPHY.md) | A timing capture, then its best-fit module | Inspect beat errors, open in Level Studio, playtest |
| Tune movement | [Movement Principles](MOVEMENT-PRINCIPLES.md) | Movement data and existing motor entry points | Movement tests, arc report, human feel pass |
| Tune parry, damage, posture, or hit feel | [Architecture](ARCHITECTURE.md), Combat section; [Dataflow](DATAFLOW.md) | Attack/feel data through the shared combat path | Combat tests, fresh FeatureTests, human timing pass |
| Tune an existing enemy | [Authoring](AUTHORING.md), enemy and moveset sections | `Assets/Data/Enemies/`, `Attacks/`, and `Movesets/` | Create Data, Build Prefabs, enemy tests, encounter playtest |
| Add a new enemy, item, moveset, or level | [Authoring](AUTHORING.md) | ScriptableObject data plus an existing factory seam | Narrow generator, shipped-asset assertion, relevant tests |
| Change spells or held items | [Authoring](AUTHORING.md) and [Dataflow](DATAFLOW.md) | Item/spell data first; presentation under `Assets/Scripts/Player/` and `Feel/` | Data/prefab generator, item tests, cast playtest |
| Change HUD, menus, prompts, or keybind labels | [Architecture](ARCHITECTURE.md), UI section; [Tooling](TOOLING.md) | UI scripts, input actions, and HUD builder as appropriate | Build HUD or Main Menu, UI tests, resolution check |
| Change animation, VFX, materials, or readability | [Animation & VFX](ANIMATION-VFX.md) | Existing effect/material owner; generated values live in factories | Narrow generator, capture, readability review |
| Change scoring, souls, splits, saves, or ghosts | [Dataflow](DATAFLOW.md) and [Ghost Racing](GHOST-RACING.md) | The owning data/system only | Focused tests plus a complete run/save/reload pass |
| Diagnose something strange | [Engineering Log](ENGINEERING-LOG.md), then [Dataflow](DATAFLOW.md) | Root cause at the shared owner | Reproduce, add one regression check, verify |
| Find an existing tool or generator | [Tooling](TOOLING.md) | Usually nothing—reuse the existing command | Read back what the tool wrote |
| Make a Windows friend build | [Distribution](DISTRIBUTION.md) | Build configuration after tests are green | Windows build smoke test and release traceability |

## Dashboard Categories

- **Start Here** — this guide, current handoff, project contract, and player-facing overview.
- **Level Building** — Level Studio, campaign working copies, vocabulary, Level Map, traversal constraints, and
  applying validated changes.
- **Combat & Enemies** — enemy families, attack data, movesets, projectile/parry choreography, and safe tuning.
- **Systems** — architecture, dataflow, movement, items/spellbook, UI, audio, VFX, scoring, saves, and ghosts.
- **Testing & Debugging** — current proof, backlog, specialist audits, health checks, and failure diagnosis.
- **Tools & Distribution** — generators, shared workflows, build/release instructions, credits, and rollback.
- **Archive** — dated plans, old handoffs, research, and engineering history. It remains searchable but is not
  presented as current truth.

Only **Start Here** opens automatically. Expanding another category reveals its complete library; collapsing it
hides navigation clutter but never removes or discards documentation.

## Where Data Lives

| Kind | Canonical location | Important owner |
|---|---|---|
| Campaign levels and zones | `Assets/Data/Levels/` | Level definitions and Level Studio working copies |
| Enemies | `Assets/Data/Enemies/` | `EnemyData` |
| Attacks and parry timing | `Assets/Data/Attacks/` | `AttackData` and the shared combat path |
| Movesets | `Assets/Data/Movesets/` | Moveset data referenced by enemies |
| Items and spells | `Assets/Data/Items/` and generated prefabs | Item/spell data and item controllers |
| Player and runtime systems | `Assets/Scripts/Player/`, `Core/`, `Combat/`, `Level/` | Owners shown in [Architecture](ARCHITECTURE.md) |
| UI and feel | `Assets/Scripts/UI/`, `Feel/` | HUD/menu builders and presentation components |
| Generated prefabs/materials/scenes | `Assets/Prefabs/`, `Assets/Materials/`, `Assets/Scenes/` | Editor factories—do not hand-edit their YAML |
| Parry recordings | the runtime `timing-captures` folder | Prime/`0` workflow in [Parry Choreography](PARRY-CHOREOGRAPHY.md) |
| Proof and known gaps | [Verification Report](VERIFICATION-REPORT.md), [Backlog](BACKLOG.md) | Current proof, not remembered test counts |

Use the dashboard **Code Graph**, **Event Bus**, and **Dataflow Maps** when the owning file is unclear.

## Safe Editing

- Commit or tag before a broad batch, and preserve unrelated dirty files.
- Campaign levels open as protected working copies. Apply to Campaign is deliberate and validation-gated.
- Content belongs in data assets and existing factories, not one-off scene objects or hardcoded builders.
- Preserve `Level_Manual` and `Sandbox_Manual`; generators deliberately do not own them.
- Never hand-edit `.unity`, `.prefab`, `.asset`, or `.meta` YAML to wire references.
- A C# default is not a shipped value. Write it through the owning factory and assert the generated asset.
- Keep the invariants in [Architecture](ARCHITECTURE.md): one input reader, one time-scale writer, and all incoming
  combat through `PlayerCombat.ReceiveAttack`.
- For a bug, reproduce and trace every caller before editing. Fix the shared owner once.

## Regeneration

Exit play mode before any generator. Prefer the narrow menu item listed in [Tooling](TOOLING.md):

- data change → **Create Data**;
- prefab/viewmodel change → **Build Prefabs** or its specific factory;
- HUD change → **Build HUD**;
- campaign geometry change → **Build Level From Definition**;
- sandbox change → **Build Sandbox**;
- menu/registry change → **Build Main Menu**.

Use **Rebuild Everything** only when the full dependency-ordered rebuild is intended. After every generator, read
the console and inspect the produced asset; a success message without a changed shipped value is not proof.

## Verification

Use the smallest proof that can fail for the change, then widen according to risk:

1. Compile runtime and editor assemblies.
2. Run the focused EditMode test that covers the changed rule.
3. Run Quick or Full EditMode tests for shared-system changes.
4. Run Health Check and any relevant level arc, encounter, light, or asset report.
5. Start a **fresh, unpaused** play session for FeatureTests.
6. Play the affected route or fight. Automated parries prove state transitions, not fairness or feel.
7. Smoke-test the exact Windows executable that will be shared.

Record new facts in [Verification Report](VERIFICATION-REPORT.md). Test counts live there rather than in this guide.

## Debugging Symptoms

Read [Engineering Log](ENGINEERING-LOG.md) before guessing; it records prior root causes and invariants.

| Symptom | First checks |
|---|---|
| Blank scene, missing objects, or Safe Mode | Unity Console for the first compile error; confirm the expected scene is open |
| Unity bridge does not respond | Editor open, no modal dialog, bridge connected; save a script or reconnect from MCP Bootstrap |
| Many unrelated timing tests fail | `GameManager.I` exists, `Time.timeScale == 1`, and play began after the last compile |
| Inspector value changed but game did not | Read the shipped asset/prefab; its generator may still own and overwrite it |
| Generated level lost a hand-placed object | It was outside a protected manual root or authored outside the level definition |
| Projectile works in one placement only | Check shared enemy data/controller first, then the zone sightline and range |
| UI clips or scales badly | Test supported aspect ratios and inspect anchors instead of tuning one resolution |
| Friend build differs from the editor | Verify build revision/configuration, graphics API, included scenes, and generated assets |

The dashboard's **Documentation Health** page reports broken links, stale terminology, missing referenced paths,
and uncategorized documents. Fix current documentation; preserve historical records under Archive.

## Rollback

Keep each coherent change in one small commit. If it regresses the game, revert that commit or restore the named
pre-batch tag—never reset over unrelated user work. Generated assets and their factory/data source belong in the
same commit so regeneration cannot silently reintroduce a bug. Build rollback is in
[Distribution](DISTRIBUTION.md); session continuity is in [Session Protocol](SESSION-PROTOCOL.md).
