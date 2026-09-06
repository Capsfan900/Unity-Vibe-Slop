# Handoff — state of play

**Rewritten at the end of every session; describes a moment, not the project.** Read this first when picking
up where the last chat stopped, then [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md).

Last session: **2026-09-06, all day** (Fable 5.1 then Opus 5, four subagent teams).

## What happened

**2026-09-06 was a long session. Newest first.**

1. **The HUD pass is IN FLIGHT** — a `ui-designer` (Opus) agent was running when the session ended, on four
   things the user asked for: Best Runs moved under the radio in one right-hand column and shipping collapsed,
   the wand name + cooldown bar removed from the top-left pane, and the souls counter made to read as a
   counted resource. Its transcript is
   `C:\Temp\claude\C--Users-tyler-Main-Storage-vibegame1\5ae61e4e-bd67-47c5-923c-f1857df5cbad\tasks\a60f2079725e1526f.output`.
   **Check `git status` first**: if `Assets/Editor/HudBuilder.cs`, `Assets/Scripts/UI/*` or `Assets/Scripts/Ghost/GhostHud.cs`
   are dirty, the agent finished and its work is uncommitted — read it, run `5. Build HUD`, run both suites,
   then commit it as `[ui-designer] …` (one commit, the regression guard). If the tree is clean it never landed;
   re-delegate with the same brief.
2. **The radio** (the user's "2000s racing game" ask). `LevelRadio` on Managers + `RadioView` pane top-right.
   mp3 / ogg / wav go in `Assets/Resources/Audio/Radio/<SceneName>/` — `Level_01/`, `Sandbox/`, `Default/`.
   Keyed to the SCENE, not `levelId`: `LevelRegistry` is an editor asset under `Assets/Data` that a build never
   loads, which made the first version always resolve null (caught in play, not by a test). Keys `]` `[` and
   backslash. `Level_01/` holds two 8-second **placeholder tones** so the pane is visible out of the box —
   delete them when real music goes in.
3. **The slide costs stamina** (`slideCost` 12). It had sat in BACKLOG 0b unbuilt while the user asked for it
   repeatedly — the lesson is in the backlog note now.
4. **Two prompt channels.** `GameEvents.PromptFlash` is additive beside `PromptChanged`; a momentary PERFECT no
   longer erases a standing "GRAPPLE [DASH]" for good. Residual, recorded in DATAFLOW and BACKLOG: the standing
   slot still has several writers and no owner, so a writer clearing with `""` blanks another's live cue.
5. **Parkour enemies never wait to be finished.** A posture break detonates them (flare); a grapple-hook finish
   kills with NO flare (`EnemyController.DiedExecuted`). They die to their own reflected bolts, 2 or 3 deflects.
   Bolts are 40 / 36 m/s with in-flight homing so one can never sail past unparriable.
6. **`pshooter_enemy01` / `02`** — the parkour family renamed, and the whole enemy tree split into
   `parkour_enemies` / `souls_enemies` (`EnemyPaths` decides by name). The melee pill guys are souls enemies on
   the sandbox pads again.
7. **The sentry flare + flare grapple**, the soulslike combat pass for the duels (per-move cooldowns, the flask
   interrupt, the near-break beat, the Drillmaster showcase), the horizon silhouette under the eclipse, and
   turbulent death mist.
8. **`session-handoff` skill + a context watcher** (`.claude/skills/session-handoff/`). A PostToolUse hook reads
   the last turn's real token usage from the transcript and warns once each at 65 / 80 / 90 percent. This
   handoff was written because it fired.

## State of the tree

- **Committed and clean** as of `7c9fd88`, unless the in-flight HUD agent above has since written files.
- Revert points: `pre-team-passes-2026-09-06` (before any subagent work), `pre-combat-plan-2026-09-06` (before
  the combat plan). Every team pass is its own commit prefixed with the team name, so one regression reverts alone.
- Generators re-run since the last code change: `3`, `4`, `4b`, `5`, `7`, level-from-definition, Health Check.
  **The HUD agent's work will need `5. Build HUD` before its tests mean anything.**
- Four subagent teams live in `.claude/agents/`: `combat-designer` (Opus, plans only), `ui-designer` (Opus),
  `vfx-art-team` (Opus), `editor-controls` (Sonnet). Their mandate and the one-commit-per-pass rule are a
  CLAUDE.md hard rule and `docs/TOOLING.md`.

## Verification

| Suite | Result | When |
|---|---|---|
| EditMode, full | **604 / 604** | 2026-09-06, after the radio scene-key fix |
| Feature suite, play mode | **765 / 766** | 2026-09-06, on Level_01 |

The single failure is `Flask_DrinkCoroutineAndInterruptOnHit`, a timing flake: it passes twice in isolation and
its detail flips between runs. A run showing ~20 failures means the editor was throttled while unfocused, not a
regression — rerun it focused before believing it.

**Nothing from this session has been played by a human.** The flare at 30 m, the bolt homing and its bigger
core, the detonation, the radio pane, the sky silhouette, the slide's new cost and the souls economy behind it
are all reasoned and tested, not felt. The Sandbox is the place: the SENTRY pad on the second row for the
parkour loop (deflect twice, watch it burst, aim at the flare, DASH), the DRILLMASTER pad for the whole souls kit,
the Grunt and Heavy pads for melee.

## Do first next session

1. **Land or re-run the in-flight HUD pass** (item 1 above). It is the only thing that may be half-done.
2. **Play a span and the Drillmaster**, then retune from what it actually feels like. Everything else is guesswork
   until then, and there is a lot of untested feel stacked up.
3. **Answer the combat-designer's open questions** in `docs/plans/combat-plan-2026-09-06.md` and
   `docs/plans/soulslike-report-gap-analysis-2026-09-06.md` — the next combat work is blocked on taste calls
   only the user can make.

## Open questions for the user

1. Should reflected bolts ever kill a sentry outright, or only ever open it?
2. Should the flare grapple cost anything, or stay a free reward?
3. Is a rear watch region (an enemy punishing you for circling behind) fair in first person?
4. Should the standing prompt slot get an owner key, so one writer's clear cannot blank another's cue?

