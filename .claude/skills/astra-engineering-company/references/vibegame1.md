# vibegame1 adapter

This file specializes the agent-agnostic company protocol for `vibegame1`. `AGENTS.md` remains authoritative; if it changes, follow it and update this adapter rather than preserving a conflicting rule here.

## Authorship and decision boundaries

- The game is first-person, melee-only, parry-focused, built with Unity 6000.5.10f1, URP, the new Input System, and C# 9 under namespace `VibeGame1`.
- Astra is a role name, not a model — it names whichever frontier model the user is running as lead for the session (originally Fable for the core systems, then Astra for the 09-08..09-12 revamp; could be another model next). Every commit records both role and actual model: subject prefix `[<role>]`, body trailer `Model: <actual model> (<harness>)`, e.g. `Model: Fable 5.1 (Claude Code)`. Astra-as-role may coordinate and review, but model rank never overrides `AGENTS.md` or the user's scope.
- Only the lead role changes a lead-owned core system (the motor, parry/combat resolution, time scale, input, enemy brain). Work by Senior/Engineer/Utility workers is refinement only: it must not invent a mechanic, input, resource, or screen. Propose such a change to the user and stop that lane.
- A lead-owned core system may be changed by a non-lead worker only when the user explicitly authorizes it in the current session, except for an explicitly reported bug fix. If an additive feature cannot avoid touching one, cite the file and line and ask first. Any commit that does touch a core system names the file in its body.
- New levels, enemies, movesets, and items are authored as data through their existing factories and authoring pipeline.

The lead must inspect authorship and current diffs before assigning an editing lane. A cheaper model is never used to evade a model or role boundary.

## Route specialists by ownership

Read the matching brief in `.claude/agents/` before assigning or performing specialist work:

| Lane | Brief | Default shape |
|---|---|---|
| Level geometry and pacing, Level Studio use, zone/object vocabulary, parry-choreography authoring | `level-designer.md` | authors level data (including through Level Studio's zone/route/type UI and the timing-capture-driven parry module generator); proposes cross-system hooks. Level Studio's own source code (`Assets/Editor/LevelStudio/`) stays lead-owned. |
| New approved enemy | `enemy-designer.md` | additive data, prefab pipeline, tests |
| Combat critique or design | `combat-designer.md` | plan-only unless implementation is explicit |
| Effects and materials | `vfx-art-team.md` | visuals only |
| Sound and mix | `audio-engineer.md` | audio only |
| HUD and menus | `ui-designer.md` | UI only |
| F10 controls | `editor-controls.md` | editor controls only |
| Missing-request audit | `unbuilt-asks.md` | read-only evidence report |

The brief's ownership boundary is part of the worker packet. Do not split a lane across agents when they would edit or depend on the same files.

## Documentation routing

Read only what the task needs:

- Strange behavior: `docs/ENGINEERING-LOG.md` first.
- Any system: `docs/DATAFLOW.md`; also update its map in the same change.
- Combat/system architecture: `docs/ARCHITECTURE.md`.
- Movement, traversal, levels, or movement HUD: `docs/MOVEMENT-PRINCIPLES.md`.
- Animation, viewmodel pose, or effects: `docs/ANIMATION-VFX.md`.
- Level creation: `docs/LEVEL-AUTHORING-TUTORIAL.md` and `docs/AUTHORING.md`.
- Level zone/object vocabulary: `docs/LEVEL-VOCABULARY.md`.
- Parry choreography (timing capture, deterministic module generation): `docs/PARRY-CHOREOGRAPHY.md`.
- Ghost racing: `docs/GHOST-RACING.md`.
- Dashboard / human-facing project status: `docs/HUMAN-DEVELOPMENT-GUIDE.md`.
- Tools and agent teams: `docs/TOOLING.md`.

Wide searches belong to a read-only utility worker that returns citations and conclusions. The lead reads the relevant source before making a consequential decision.

## Editing and verification ownership

- Only the lead controls the user's open Unity editor. Workers verify offline with `dotnet build Assembly-CSharp.csproj` and `dotnet build Assembly-CSharp-Editor.csproj` as appropriate.
- Run useful offline checks as lanes finish. Workers report required generator steps; after integration, the lead batches editor-dependent generators, health checks, suites, harnesses, and visual inspection into one editor pass.
- Exit play mode before generators. Confirm `GameManager.I != null` and `Time.timeScale == 1` before trusting play-mode results.
- Verify shipped values in `.asset` or `.prefab` YAML after their generator runs; a C# default is not a shipped value.
- Tests and harnesses prove logic/state, not feel or fairness. Reserve those claims for a human playtest.
- Workers never commit. The lead owns tags and one revertible commit per worker/team pass, using the `[<role>]` prefix and `Model:` trailer required by `AGENTS.md`, after the named generators and suites have been rerun.

## Escalation

A worker returns early with evidence when it encounters:

- an overlap with another lane's files;
- a required change to a lead-owned core system without current-session authorization;
- a new mechanic, input, resource, or screen;
- a need to operate the Unity editor;
- a failed offline build caused outside its owned files;
- ambiguous user intent that changes shipped behavior.

Its report names the blocker, affected file and line, smallest viable decision, and any safe work already completed. The lead decides whether to re-scope, promote, integrate, or ask the user.
