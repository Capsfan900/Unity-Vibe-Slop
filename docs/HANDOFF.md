# Handoff — state of play

**Rewritten at the end of every session; describes a moment, not the project.** Read this first when picking
up where the last chat stopped, then [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md).

Last session: **2026-09-06, all day** (Fable 5.1 then Opus 5, five subagent teams).

## What happened

**2026-09-06 was a long session. Newest first.**

0. **AFTER this handoff was first written, five more passes landed and were regenerated.** Each is one
   commit, so each reverts alone: `d954c61` + `8397d86` audio-engineer (the silent systems got sounds, a
   licence gap closed, the radio streams songs and skipping works, the placeholder tones are gone),
   `c6428ee` ui-designer (an audio settings panel, a QA sweep, one palette at the centre of the screen),
   `25e5498` vfx-art-team (the world goes cold: blue palette, ringed planets, a ghost sentry, a bigger
   flare), `bb72aee` combat-designer (the flare toss goes higher and buys two seconds of float), then
   `4fdf763` re-ran every generator — materials, data, prefabs, HUD, menu, level and sandbox.
0b. **The standing prompt got an OWNER KEY** (`c0ed46d`) — the residual item 4 below records, closed on the
   user's word. Every standing write carries a `PromptOwner` key; a non-empty cue still takes the line
   (last speaker wins), but a clear only lands when the clearer still holds it, or nobody does. So a SURGE
   expiring can no longer blank a live "GRAPPLE  [DASH]" that will never re-raise itself.
   `PromptView.AcceptsStandingWrite` is pure and pinned by `PromptOwnerTests`; four `Prompt_Owner*` checks
   in the feature suite prove the view obeys it. DATAFLOW "THE PROMPT LINE" is updated.
1. **The HUD pass LANDED** (`0d1c73c`). The top-right is one column now: radio on top, Best Runs beneath it and
   shipping COLLAPSED (best time + "+N MORE"), auto-expanding for 4 s when the leaderboard changes — no new input.
   The wand name and cooldown bar are gone from the loadout pane (only HudBuilder and HUDController read them;
   `ExecuteInteractor` still prints the wand cooldown at the crosshair when it matters), and the souls counter is
   bone label over rolling mint digits that flash ember on a gain and snap on a spend. `HudColumnTests` (11) pin it.
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

- **Committed and clean** as of `c0ed46d`.
- Revert points: `pre-team-passes-2026-09-06` (before any subagent work), `pre-combat-plan-2026-09-06` (before
  the combat plan). Every team pass is its own commit prefixed with the team name, so one regression reverts alone.
- Generators re-run since the last code change: `3`, `4`, `4b`, `5`, `7`, level-from-definition, Health Check.
  `5. Build HUD` has been re-run since the HUD column pass, so its prefab pins assert rather than skip.
- Four subagent teams live in `.claude/agents/`: `combat-designer` (Opus, plans only), `ui-designer` (Opus),
  `vfx-art-team` (Opus), `editor-controls` (Sonnet). Their mandate and the one-commit-per-pass rule are a
  CLAUDE.md hard rule and `docs/TOOLING.md`.

## Verification

| Suite | Result | When |
|---|---|---|
| EditMode, full | **677 / 677**, 0 skipped, 233.9 s | 2026-09-06, after the five passes AND the owner key (`c0ed46d`) |
| Feature suite, play mode | **765 / 766** — STALE | 2026-09-06, but BEFORE the five passes and the owner key. **Re-run it.** |

The single failure is `Flask_DrinkCoroutineAndInterruptOnHit`, a timing flake: it passes twice in isolation and
its detail flips between runs. A run showing ~20 failures means the editor was throttled while unfocused, not a
regression — rerun it focused before believing it.

**Nothing from this session has been played by a human.** The flare at 30 m, the bolt homing and its bigger
core, the detonation, the radio pane, the sky silhouette, the slide's new cost and the souls economy behind it
are all reasoned and tested, not felt. The Sandbox is the place: the SENTRY pad on the second row for the
parkour loop (deflect twice, watch it burst, aim at the flare, DASH), the DRILLMASTER pad for the whole souls kit,
the Grunt and Heavy pads for melee.

## Do first next session

0. **Re-run the feature suite in play mode.** EditMode is green at 677/677, but the play-mode figure predates
   the audio, UI, VFX and combat commits and the prompt owner key — the editor was in play mode (the user was
   testing) when the owner key landed, so it was never run. Level_01, fresh session, `GameManager.I != null`
   first.
1. **Play a span and the Drillmaster**, then retune from what it actually feels like. Everything else is guesswork
   until then, and there is a lot of untested feel stacked up.
2. **Answer the combat-designer's open questions** in `docs/plans/combat-plan-2026-09-06.md` and
   `docs/plans/soulslike-report-gap-analysis-2026-09-06.md` — the next combat work is blocked on taste calls
   only the user can make.

## Open questions for the user

**All four are settled (2026-09-06) — three answered, one deferred. Do not re-ask them.**

1. ~~Should reflected bolts ever kill a sentry outright, or only ever open it?~~ **They kill, and already do.**
   The user: *"this system is already in place it kills the enemy and they explode with the flare."* A span
   IS clearable at range; the dash line is the faster, showier one, not the only one.
2. ~~Should the flare grapple cost anything?~~ **No — free.** The user: *"does nothing its free."* The two
   deflects that earned it are the whole price. Do not spend stamina or the dash cooldown on it.
3. ~~Is a rear watch region fair in first person?~~ **DEFERRED, not refused.** The user, after hearing what
   it meant: *"save that for later, the rear region."* Item F of the soulslike gap analysis sits in BACKLOG
   until they raise it themselves. Nothing in the duels is blocked on it — A, B and C are the next combat
   work and none of them touch it.
4. ~~An owner key on the standing prompt slot?~~ **Yes, and it is BUILT** (`c0ed46d`, item 0b above).

