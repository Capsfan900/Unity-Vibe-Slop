# Developer consultation — 2026-09-07

Scope: continuation of the design audit, grounded in the current working tree and the September 6 route capture. Recommendations only; no gameplay or tuning changes. Existing uncommitted level work was preserved.

## Direction

The strongest design proposition is turning a defensive success into forward movement. Judge the next pass by whether players can see a route, parry while following it, and intentionally spend the resulting speed. More weapons and traversal options will not establish that by themselves. Concentrate the next playable slice on one short, repeatable section using the existing mechanics.

## Ranked findings

### 1. Retry currently inherits death penalties — confirmed in code

`Assets/Scripts/UI/PauseMenu.cs:85` implements RESTART FROM CHECKPOINT by dealing 99999 damage. `Assets/Scripts/Player/PlayerDeath.cs` then takes the wallet, creates a bloodstain, plays death feedback and waits before respawning. `Assets/Scripts/Combat/Health.cs:36` rejects that damage while invulnerable, so restart can close the menu without restarting.

Player consequence: a practice command can cost progression and behaves differently under a debug flag. This muddies the distinction between choosing another attempt and failing one.

Recommendation: give the existing checkpoint restart an explicit reset path and define its wallet, pickup, enemy and timer behavior. Preserve actual death behavior. Do not simply bypass invulnerability with execute damage: that retains the semantic problem.

Acceptance: activate the same menu command with and without invulnerability; both return to the checkpoint. Confirm the intended wallet policy, restored world state and timer policy. A real death must still produce its intended penalties.

### 2. The route image has competing focal points — visual judgment

Evidence: `RouteShots/fog/2_spawn_to_T1_arena_91m_AFTER.png`, dated September 6; this is a historical capture, not a fresh view of the modified level.

The eclipse gives the place a recognizable landmark and the cyan edges make isolated ledges visible. However, the eclipse occupies a large, high-contrast part of the frame; foreground cyan solids, wall trim and landing borders compete for attention. The orange central object also overlaps the route from this viewpoint. Farther geometry becomes a stack of dark silhouettes.

Recommendation: retain the eclipse, reduce its competition with immediate footing, and reserve the strongest local contrast for the next landing and actionable threat. Tune framing and value before adding detail. Inspect this in motion with the real HUD and effects before changing materials globally.

Acceptance: at spawn, before the first jump and after landing, a new player can indicate the next safe destination without explanation. Compare identical cameras before/after, then verify a normal-speed run; a still image cannot establish movement readability.

### 3. Prove the parry-to-route connection in one section — design hypothesis

The combat plan identifies deflect-driven movement as the connection between dueling and speedrunning. The current level definition contains sentry entries alongside legendary and boss entries (`Assets/Data/Levels/Level_01_Level.asset:717`). Authoring entries alone do not prove which enemies currently spawn or how a live route plays.

Recommendation: use one existing sentry and one clearly visible landing to teach the relationship. The first success should carry the player somewhere useful and understandable. The second encounter should let them deliberately repeat it; optional lines can then reward timing. Avoid requiring several unfamiliar movement techniques in the first demonstration.

Acceptance: observe an unfamiliar player. Record whether they discover the parry, recognize its movement reward, and deliberately reuse it. Record why each failure happened: missed cue, unclear destination, overshoot, or input misunderstanding. Reachability tests remain necessary but cannot answer these questions.

### 4. Weapon weight has an undifferentiated camera response — confirmed call site, unverified feel

`Assets/Scripts/Player/WeaponController.cs:152` calls `CameraShake.I.Small()` for the shared hit response. That channel does not distinguish a heavy weapon from a light one. This does not establish that all their other feedback is identical.

Recommendation: first compare current weapons against the same target. If heavy hits lack distinction, tune the existing response by weapon with restraint. Judge sound, animation and camera response together; simply increasing shake can obstruct the next cue.

Acceptance: heavy and light impacts are distinguishable while the player can still read the next incoming attack. Keep reduced-motion settings effective.

## Corrections to the inherited audit

- The handoff's F10 release-build blocker is fixed at the input layer: `InputReader.LevelEditorPressed` returns false outside editor/development builds (`Assets/Scripts/Core/InputReader.cs:176`). Do not reopen this as an outstanding code task.
- The handoff cites a 2.0-second surge for `pshooter_enemy03`; its current asset contains 1.5 seconds. Evaluate the shipped value before suggesting another retune.
- The handoff's zero-enemy statement is not sufficient evidence about the current level. The current data contains ten spawn entries; runtime population still needs inspection.
- The resolution selector still assigns concrete dimensions (`SettingsMenu.cs:436–442`). Keep the native-resolution behavior on the correctness list, separate from the core-loop design pass.

## Proposed next pass

1. Correct checkpoint-restart semantics and verify the affected reset behavior.
2. Capture the current opening route with HUD, then make a focused readability pass if the historical issue remains.
3. Observe several unfamiliar players repeat that section; use their failures to choose the next change.
4. Return to weapon presentation after route comprehension and intentional parry movement are demonstrated.

No new mechanic is required for this pass.

## Evidence limits

## Expanded product assessment

The first pass was too narrow: correctness issues are only part of a consulting audit. The larger concern is whether the implemented systems reinforce the user's recorded parkour-first direction (`docs/BACKLOG.md`, section 0).

### The run needs a clear contract

`SpeedrunTimer.Update` counts Playing and Dead time, but excludes LevelUp. `LevelUpMenu.Open` is available while playing and pauses the world. `PlayerStats` changes damage, health, flask capacity and Pyre reward. These are confirmed behaviors, not automatically defects: purchasing upgrades can be an intentional speedrun strategy. However, the design must say whether a personal best represents movement mastery, build optimization, or both. The legacy `LevelProgress` entry stores level ID, time and deaths; this review has not established all metadata or validity rules in the separate ghost storage system.

Recommendation: define the starting state and legal in-run actions for a comparable attempt before balancing par times. For an initial movement evaluation, hold loadout and starting stats constant experimentally. This is a test condition, not a proposal to delete progression or add a new mode.

### Pacing must survive the duel transitions

The serialized level has six sentries, three legendary entries, one boss and arena gates, with parTime 225 seconds. This is a substantial authored mix for the first registered level. It risks teaching players to accelerate between prolonged stops. Runtime wiring and encounter duration remain unmeasured; do not mistake authored entries for a played pacing chart.

Recommendation: record a complete run and separate travel, active combat, waiting for an attack opportunity, recovery and menus. Duels can provide contrast, but their duration and resolution should reward mastery. If better execution barely reduces encounter time because the player is waiting for mandatory cycles, that encounter works against the speedrun promise. Fix the offending encounter before adding more roster content.

### Teach decisions before combinations

The controls and system inventory include slide, dash, wall-run, wall-jump, water, balloons, grapple, surge, parry, guard, deathblow, wands, Pyre and upgrades. This is an inventory, not proof that the opening teaches them all simultaneously. Nevertheless, the learning order deserves explicit authorship.

Proposed opening lesson: establish a safe ordinary jump; show a faster optional slide line; introduce one readable projectile on forgiving footing; then reuse that projectile-to-movement lesson over a gap with a recovery opportunity. Give the player a chance to predict the outcome before combining multiple unfamiliar verbs. Existing advanced options can remain available without being necessary for the first success.

Acceptance: watch the first ten minutes without coaching. Log where the player stops, what they think each object does, and whether their second success is deliberate. Do not infer comprehension from completing a jump once.

### The finish should sell the next attempt

`HUDController.OnBossDefeated` displays LEVEL CLEAR and elapsed time, then `WinCo` waits and returns to the main menu. The project already has personal-best and ghost systems; claiming replay support is missing would be wrong. The narrower issue is the transition: the inspected win flow prioritizes exiting the level over immediately choosing another attempt.

Recommendation: review the existing result flow around three decisions: understand the result, retry, or leave. Show the improvement relative to the previous best where available and allow the player to advance promptly. Any new screen or input remains a proposal; none was implemented. Measure actual boss-defeat-to-next-controllable-attempt latency before changing it.

### Visual identity needs functional hierarchy

The eclipse is a recognizable signature worth retaining. The inspected historical capture reads as a graphic obstacle course with dark-fantasy atmosphere; it does not yet sell convincing architectural material or scale. That is acceptable for a prototype. Priority is consistent visual meaning: safe footing, runnable walls, interactable objects and hostile threats should be distinguishable by shape and value as well as color. Additional bloom and texture detail will not establish those distinctions.

Use identical player-height captures at decision points, including the actual HUD. Check the next landing during an attack effect, not only in an empty scene. No current HUD-overload finding is established by the HUD-free image inspected here.

### Production focus

There is substantial infrastructure: generated content, test harnesses, a level editor, ghosts, progression and a growing enemy roster. The next deliverable should be evidence that their combination creates a repeatable, understandable run. Additional infrastructure has a lower priority unless it directly removes an observed playtest obstacle.

Suggested order: establish run/reset rules; fix checkpoint retry; record the current opening and complete run; adjust one section's teaching and sightlines; observe unfamiliar players; then revise the pacing and finish flow using those observations. Defer new weapons, enemies and broad cosmetic passes until this loop is demonstrated.

A successful milestone is a player who can explain a failed attempt, name a specific improvement, and voluntarily try it again. This is a proposed qualitative acceptance criterion, not a measured result.

## Verification limits

No fresh playtest, frame-time measurement, generator run or test-suite run was performed. The local Python command used by the Unity bridge could not start, so no live editor state was obtained. This audit uses source, serialized data, existing plans and an inspected historical image. Findings explicitly distinguish confirmed code behavior from visual judgment and hypotheses.
