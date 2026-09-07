# Astra systems audit — vibegame1

Date: 2026-09-07. Requested by the owner: a real once-over of all systems by Astra, no changes, in Markdown for reference with Claude.

## Scope, authorship and evidence

Three `gpt-6-astra` reviewers inspected combat/progression, movement/levels, and UI/audio/presentation. The combat reviewer also reviewed ghosts, persistence and supporting infrastructure. The lead consolidated their findings and cross-checked source, serialized assets and existing verification artifacts.

This is a broad source-and-data audit, not a line-by-line certification of every file. No code, assets, scenes, settings or existing documents were changed for this report. No editor interaction, generators, tests, builds, package changes or commits were performed. The only deliverable written is this document.

The repository changed externally during the review: `d96b008` → `aaa6daa` → `b8d453c`. The final observed HEAD was `b8d453c`; the preexisting `.claude/settings.local.json` remained untracked. In particular, the level was being revised by another session. Recheck cited locations and live content before implementing anything. Line numbers are reference points, not immutable identifiers.

**Evidence labels:** **Confirmed** means the inspected code/data establishes the mechanism; it does not mean a fresh in-game reproduction occurred. **Conditional** means the mechanism requires a stated configuration, timing or content arrangement. **Design** means a recommendation or question, not a demonstrated defect. **Unverified** identifies missing runtime evidence.

**Priorities:** P1 = address before a dependable external playtest of the affected feature; P2 = meaningful reliability or consistency issue; P3 = lower-impact polish/platform mismatch. The numbered engineering register contains **22 findings: 9 P1, 12 P2 and 1 P3**. No P0 catastrophe was established. These are development priorities, not numerical fun or quality scores. Related clock/reset findings share causes and should not be treated as 22 independent implementation tasks.

## Development verdict

The project has substantial, purposeful foundations: a capable movement controller, centralized combat resolution, enemy presentation separated from AI, data-driven content, shared authoring/runtime geometry, useful mathematical tests, and a complete local ghost feature. The main risk is integration. Systems that work individually disagree about pause, death, active-level identity, action ownership and menu focus.

Those disagreements directly affect the promise of a parry-focused speedrun platformer. A retry can retain the previous attempt's movement debt, a paused offensive action can continue against frozen enemies, a ghost can lose clock alignment, and a completed run can fail to reach the campaign progress system. Fixing these boundaries is more valuable than adding another mechanic or performing a broad aesthetic retune.

The parkour-first direction is supported by the movement toolkit and sentry placement. The unresolved design question is whether the repeated gated duels reinforce that direction or dominate it. The authored 225-second par is not evidence of a good pacing balance. A recorded complete run and unfamiliar-player observation are still needed.

## Coverage map

| System | Assessment | Findings / next evidence |
|---|---|---|
| Game state, event ownership, time scaling | Central owners exist; consumers do not share one pause contract | A03, A08, A10; modal transition checks |
| Ground/air movement, dash, slide | Rich control and forgiveness; explicit momentum limits | A02, A10; uninterrupted normal-resource runs |
| Wall-run/jump, pull/burst, traversal buffs | Clear motor entry points; lifecycle reset incomplete | A02; buffer/cooldown tests across death |
| Stamina | Availability feedback is tied to actual mechanics | A02; fresh-attempt resource consistency |
| Water, balloons, ramps | Shared piece authoring and motor-owned movement | A08; actual slope/water/balloon chaining remains unplayed here |
| Camera/look/lock-on | Mouse agency, hysteresis, occlusion grace and tracking are deliberate | D05; controller feel and comfort unverified |
| Health, posture, parry/guard | Coherent incoming-resolution path and distinct defensive outcomes | A04; cue readability and real attack geometry |
| Ordinary weapons, combos | Buffering/cancellation and weapon-specific data exist | A04, A06, D03 |
| Supers/Pyre | Distinct supers and readable resource concept | A03, A06; pause/action overlap |
| Wands/deathblows | Shared execute and cooldown fallback are useful | A03, A07; transaction consistency |
| Items, grapple, flask | FIFO inventory and spend-on-success semantics are sensible | A06; action overlap and death reset |
| Enemy AI/movesets | Shared controller with rig/locomotion interfaces and attack arbitration | A04, A05; individual encounters not fight-tested |
| Bosses/arena progression | Explicit deathblow segments and exit conditions | A09; pre-entry kills and phase pacing |
| Souls/stats/upgrades | Runtime scaling works as a coherent local system | D01, D02; persistence intent needs a clear contract |
| Death/checkpoints/retry | Short respawn delay; reset ownership incomplete | A01, A02, A11 |
| Campaign completion/unlocks | Recording method exists but is disconnected | A12 |
| Run timer, PBs, ghosts | Real feature with recording, playback, board and result feedback | A13–A17; identity, time and storage boundaries |
| Main menu, pause, modal navigation | Generated UI exists; focus/transition holes | A18, A19, A20 |
| HUD/status/prompts | Refusal feedback and prompt ownership are strong | D05, D06; real gameplay/aspect-ratio review |
| Settings/input schemes | Store/applier separation and clamping are sound | A21, D05, D06 |
| Audio/radio/music | Fallback synthesis and volume trims exist | A22, D07 |
| Animation/viewmodels/VFX | Contact fitting and explicit effect ownership are good foundations | A06, D03, D05; filmed contact and masking checks |
| Materials/lighting/rendering | Deliberate palette and generators; performance not measured here | D04; current-camera, GPU and WebGL checks |
| Level data/builders/analyzers | Shared data and geometry reduce drift | A11; reachability is not whole-route validation |
| In-game editor/custom levels | Useful bounded authoring tool; shipped play needs lifecycle repair | A11, A16; partial campaign round-trip is intentional |
| Saves/local persistence | Settings and run stores exist; completion and durability gaps | A12–A17 |
| Builds/distribution | Scene resolution, compile guards and build stamps exist | V02; target execution and reproducibility checks |
| Tests/debug tools/docs | Substantial historical automated evidence | V01; integration gaps and stale summaries |
| Multiplayer/backend | Local asynchronous ghost racing implemented; remote backend is a design/interface | No network service or authoritative competition verified |

## Findings requiring engineering attention

### A01 — P2 — Checkpoint restart is implemented as death

**Confirmed.** `Assets/Scripts/UI/PauseMenu.cs:85` deals 99999 damage. `Assets/Scripts/Player/PlayerDeath.cs` then removes carried souls, creates a bloodstain, plays death feedback and respawns. `Assets/Scripts/Combat/Health.cs:36` rejects that damage when invulnerable, so the command can close the menu without restarting.

**Impact:** A practice command inherits death penalties and does not work consistently under the existing invulnerability toggle.

**Recommended change:** Define an explicit checkpoint-retry operation with a documented wallet, timer and world-reset policy. Do not merely force execute damage through invulnerability.

**Acceptance:** Restart with and without invulnerability; verify position, wallet, enemies, pickups and timer. Separately verify that actual death retains its intended consequences.

### A02 — P1 — Death does not reset the complete movement state

**Confirmed.** `Assets/Scripts/Level/LevelManager.cs:67` restores health/flask and emits respawn. `Assets/Scripts/Player/PlayerStamina.cs:143` has `ResetFull`, but no respawn subscription. `Assets/Scripts/Player/FirstPersonMotor.cs:1586` does not clear `dashReadyAt`, `slideReadyAt`, `wallRunReadyAt` or `wallSurgeUntil`. Ending slide/wall-run during teleport can introduce cooldowns (`:484`, `:713`), and the motor clock stops outside Playing (`:950`).

**Impact:** A retry can begin with depleted stamina, a move unavailable because of the previous attempt, or a surviving Wall Surge. A short respawn delay does not make the attempt state consistent.

**Recommended change:** Establish one death-reset contract for resources, cooldowns and temporary buffs. Keep ordinary teleport semantics separate if needed.

**Acceptance:** Die during dash, slide, wall-run, depleted stamina and active Surge; compare the next attempt with a fresh start.

### A03 — P1 — Offensive actions and wand readiness advance during pause

**Confirmed clock mismatch; live consequences need reproduction.** Pause sets state and time scale to zero (`Assets/Scripts/UI/PauseMenu.cs:46`). Execute/wand steps use realtime waits (`Assets/Scripts/Player/ExecuteInteractor.cs:187`, `:200`, `:218`; `WandController.cs:206`, `:329`), execute movement uses unscaled delta (`ExecuteInteractor.cs:259`), and super waits/flight use realtime/unscaled time (`UltimateAbility.cs:135`, `:232`, damage at `:248`). Wand readiness uses unscaled time (`WandController.cs:33`, `:151`). Ongoing actions lack a common pause gate.

**Impact:** A started action can progress or deal damage against paused opponents, and a paused player can recover wand readiness without run time passing.

**Recommended change:** Use an action clock that ignores hitstop while stopping during gameplay pause; apply the same policy to coroutines and cooldowns.

**Acceptance:** Super → pause before impact; execute → pause; fire wand → pause for its cooldown. Resume without extra action progress or cooldown benefit unless expressly intended.

### A04 — P1 — Melee validity ignores important world geometry

**Confirmed.** Incoming melee flattens target height (`Assets/Scripts/Enemies/Core/EnemyController.cs:276`), passes that horizontal distance onward (`:360`), and resolves by radius/cone (`:663`). Initial wake LOS (`:289`) is not impact occlusion. Player weapon overlap (`Assets/Scripts/Player/WeaponController.cs:130`) and execute selection (`ExecuteInteractor.cs:70`) also lack world-occlusion checks.

**Impact:** A player directly above a melee attacker can remain inside its horizontal hit area. Thin solid cover can separate attacker and victim while melee or execute still succeeds. Exact campaign exposure depends on geometry.

**Recommended change:** Give attacks meaningful vertical reach and an appropriate impact-occlusion rule. Explicitly distinguish any magical attack intentionally allowed through cover.

**Acceptance:** Repeat an incoming swing with the player on a higher ledge and behind a wall; repeat outgoing melee/execute across the same wall. Confirm valid close-range attacks still connect.

### A05 — P1 — Bolts move without swept collision or world blocking

**Confirmed mechanism; tunneling frequency unmeasured.** `Assets/Scripts/Enemies/parkour_enemies/Projectile.cs:145` moves the transform directly and checks endpoint distance to player (`:160`) or reflected target (`:167`). It has no segment sweep/world collision. Shooter LOS is checked at launch (`ProjectileShooter.cs:59`), after which the bolt homes.

**Impact:** Cover entered after launch does not stop the bolt. A fast bolt and moving player can pass between sampled endpoints, losing a damage/parry interaction; reflected payoff can also miss.

**Recommended change:** Sweep from previous to new position, resolve the nearest valid collision, and consider player relative motion.

**Acceptance:** Incoming and reflected bolts at several frame rates, head-on movement, lateral dodging, and cover entered after launch. Preserve the intended cue lead and deflect behavior.

### A06 — P2 — Supers do not participate in shared action ownership

**Confirmed missing exclusion; desired combinations are a design decision.** `Assets/Scripts/Player/PlayerCombat.cs:21` omits super state from busy status. `UltimateAbility.cs:73` excludes execute/stagger but can independently start viewmodel attack (`:111`). Ordinary weapon input (`WeaponController.cs:86`) does not gate on the super.

**Impact:** Swing and super can both schedule damage; flask/guard/super can overlap and compete for hand presentation. Damage may not match the visible action.

**Recommended change:** Write the allowed action matrix first, then enforce commitment/cancellation consistently. Do not assume every overlap should be forbidden.

**Acceptance:** Swing → super, super → swing, guard → super and flask → super. Inspect damage events and the visible hand action together.

### A07 — P2 — A riposte can change wand after committing its timing

**Confirmed transaction race.** `Assets/Scripts/Player/ExecuteInteractor.cs:173` captures the ready wand and uses its timing (`:183`), waits (`:187`), then `WandController.FireRiposte` reads `Current` again (`WandController.cs:144`). Cycling (`:105`) only checks Playing.

**Impact:** Switching during commitment can change discharge, cooldown and model after the step-in was chosen for another wand.

**Recommended change:** Carry the selected wand through the committed action, or prevent selection changes during commitment.

**Acceptance:** Cycle inside the execute commit and compare selected weapon, timing, discharge, cooldown and model.

### A08 — P2 — Pause advances balloons and gates while the timer stops

**Confirmed; route benefit conditional.** `Assets/Scripts/Level/Balloon.cs:77` and `:114` use unscaled availability time; `BossArenaTrigger.cs:109` moves gates with unscaled delta. `SpeedrunTimer.cs:40` excludes paused/menu time.

**Impact:** Waiting in pause can restore a balloon or finish opening collision geometry for free on the run clock. The reviewed campaign does not establish a profitable repeated-balloon route.

**Recommended change / acceptance:** Apply the same explicit pause-versus-hitstop clock policy as A03. Pause immediately after a balloon pop or gate transition and inspect state on resume.

### A09 — P2 — Killing an arena keeper before entry can strand its exit

**Conditional state-machine defect.** `Assets/Scripts/Level/BossArenaTrigger.cs:71` begins evaluating keeper death only after entry. At `:90`, absence is recognized only after `sawAlive` was set from a present instance.

**Impact:** If an early kill has already despawned before entry, the trigger never observes the keeper alive and may never open the exit. Normal campaign targeting opportunities were not demonstrated.

**Recommended change / acceptance:** Track spawned/killed state independently of entry. Kill the keeper before entering, then enter both before and after corpse despawn.

### A10 — P2 — Low frame rates change movement relative to run time

**Confirmed arithmetic.** `Assets/Scripts/Core/TimeScaleController.cs:34` caps player delta at 0.05 seconds; the motor consumes it (`FirstPersonMotor.cs:955`) while the timer adds full unscaled delta (`SpeedrunTimer.cs:40`).

**Impact:** At sustained 10 FPS, movement advances about half a simulated second per real second while the run clock charges a full second. Hitches also alter the player/world timing relationship.

**Recommended change:** Consume accumulated time with bounded integration steps, or define and verify a supported minimum frame rate. Do not simply remove a physics-stability clamp without evaluation.

**Acceptance:** Sustained 15/20/30 FPS and isolated long frames; compare distance, cooldowns, stamina and enemy timing against elapsed time.

### A11 — P1 — Custom-level death still uses the host scene lifecycle

**Confirmed wiring defect.** `Assets/Scripts/Level/LevelManager.cs:33` captures host spawners once. `LevelEditor.cs:311` rebuilds/plays a custom document and maintains separate spawners (`:479`), but does not bind its start/registry into LevelManager. Host roots are hidden (`:497`), while ordinary death selects the manager's old checkpoint/start (`LevelManager.cs:71`). Custom levels are reachable through `MainMenuController.cs:311` in a shipped build.

**Impact:** A death before a custom checkpoint returns to the host start, potentially outside the course. Custom enemies miss ordinary respawn reset. A custom checkpoint only fixes part of that lifecycle.

**Recommended change:** Bind/unbind the active document's start, checkpoints and spawners through one level lifecycle.

**Acceptance:** Custom play → death before checkpoint → death after checkpoint → rebuild → play again. Verify position and all enemies, not just a successful initial spawn.

### A12 — P1 — Campaign completion persistence has no caller

**Confirmed repository search.** `Assets/Scripts/Level/LevelProgress.cs:144` implements `RecordCompletion`, including unlock-next behavior, but the search across `Assets/**/*.cs` finds no call. The inspected win flow and ghost submission do not bridge into it.

**Impact:** Finishing can display a result and save a ghost without recording campaign completion or unlocking the next registered level. With one campaign level, the unlock failure remains latent until content expands.

**Recommended change / acceptance:** Have a single authoritative completion path record campaign progress exactly once. Complete a two-entry registry's first level, return to menu, restart the application and confirm completion/unlock/PB agree.

### A13 — P1 — Menu and run storage disagree about level identity

**Confirmed.** `Assets/Scripts/Ghost/RunRecorder.cs:160` and `Leaderboard.cs:81` use the scene name. `MainMenuController.cs:162` queries `SafeLevelId`. The current definition uses `levelId: samplescene` and `sceneName: Level_01` (`Assets/Data/Levels/Level_01_Level.asset:15`). Custom documents share the Sandbox host scene.

**Impact:** The menu looks up a different PB key from the one used to save the run. Different custom levels share a host-scene storage namespace if they produce recordings.

**Recommended change:** Resolve one active logical level identity for timer, recording, board, progress and menu. Preserve existing data with a deliberate migration policy.

**Acceptance:** Complete and reload the campaign, then two distinct custom documents; each must show only its own records under the same identity everywhere.

### A14 — P1 — Ghost catch-up skips timestamps but playback has no timestamps

**Confirmed algorithmic inconsistency.** `Assets/Scripts/Ghost/RunRecorder.cs:108` emits at most a capped number of samples, then advances `nextSampleTime` directly to elapsed time after a sufficiently large hitch (`:116`). `GhostData.cs:87` and playback infer time only from sample index divided by tick rate.

**Impact:** Skipped tick slots compress the recording's timeline. Later ghost positions play too early and the ghost can finish before its stored finish time. The comment claiming clock alignment is not upheld by the representation.

**Recommended change:** Preserve skipped time explicitly, or preserve fixed sample slots with bounded reconstruction. Keep both data size and frame cost bounded.

**Acceptance:** Record a known path with a one-second hitch. Compare positions at fixed elapsed times and recording duration against the unhitching baseline.

### A15 — P2 — PB auto-load depends on component Start order

**Conditional initialization hazard.** `Assets/Scripts/Ghost/GhostRacing.cs:80` loads the PB in Start; `Leaderboard.cs:88` fills its initially empty board in its own Start. The inspected code has no explicit dependency ordering or retry for that initial load.

**Impact:** The leaderboard can appear after refresh while the automatic ghost load has already concluded that no PB exists.

**Recommended change / acceptance:** Load after the board's first successful refresh or perform explicit initialization. Cold-load a level with an existing PB under both initialization orders.

### A16 — P2 — Custom-file load failure has no clean recovery

**Confirmed paths; malformed-file case conditional.** `Assets/Scripts/Level/LevelEditor.cs:212` enters editing before loading a pending document. A missing file returns early after entry, while `LoadFile` (`:373`) reads/parses JSON without an exception boundary. `LevelDocument.cs:83` normalizes missing lists but does not catch parsing errors. F10 entry/exit input is disabled in release builds.

**Impact:** A stale or corrupt saved level can leave the player in editing state rather than return cleanly to the menu. This is not an unavoidable softlock: the editor Exit button is wired (`LevelEditor.cs:195`). The failure still exposes unintended editor/default content and requires manual recovery.

**Recommended change / acceptance:** Validate/load before replacing active play state, handle I/O/parse failures, and restore a known screen. Try a missing file, malformed JSON and unreadable file from the custom-level menu.

### A17 — P2 — Run storage does not fully uphold its durability/validation contract

**Confirmed conditional failure paths.** `Assets/Scripts/Ghost/RunStore.cs:174` writes a temporary file, deletes the prior destination, then moves the replacement. This leaves a failure window rather than an atomic replacement. Errors are logged and swallowed; `SaveRun` can still return a leaderboard entry (`:102`) after a failed blob/index write. Loaded indices retain null entries, but save sorting dereferences every row (`:91`). `GhostRecording.IsValid` (`GhostData.cs:89`) only checks sample count and tick rate.

**Impact:** Interrupted/failed writes can lose the old destination or report a saved result whose files are incomplete. Syntactically valid damaged index data can break later submissions. This is local reliability, not a claim of authoritative online anti-cheat.

**Recommended change:** Validate deserialized entries, propagate persistence success, and use a recoverable replacement/backup scheme supported by each target. Address index/blob consistency together. The current save also prunes old ghost blobs before successfully publishing the new index; publish the replacement successfully before pruning retained data.

**Acceptance:** Null index rows, failed blob write, failed index replacement, stale temporary file and restart recovery. Do not test destructively against a player's real saves.

### A18 — P1 — Controller-only menus lack initial focus

**Confirmed configuration gap.** `Assets/Prefabs/MainMenu.prefab:4150` and `Assets/Prefabs/HUD.prefab:23391` have null first selection. Builders (`MainMenuBuilder.cs:166`, `HudBuilder.cs:531`) do not assign it; UI code contains no selection-establishing `SetSelectedGameObject`/`.Select()` calls. Panel methods only activate content.

**Impact:** Gameplay supports controllers, but a controller-only player can start at a menu with no navigable selected button. Automatic navigation links do not establish a first target.

**Recommended change / acceptance:** Assign focus on each panel entry and restore it on close. Cold boot → play → pause → settings → back → resume without touching a mouse.

### A19 — P2 — One Escape can close a modal and open pause

**Conditional execution-order hazard.** `LevelUpMenu.cs:52`/`:72` and `WandSelectMenu.cs:66`/`:88` consume PausePressed by returning to Playing. `PauseMenu.cs:37` sees the same frame's press and can open. `SettingsMenu.cs:139` already guards this class of problem, but the other modals do not.

**Impact:** Closing a modal unexpectedly replaces it with pause instead of resuming.

**Recommended change / acceptance:** Use consistent modal ownership/input consumption. Exercise Escape and gamepad Start with closing menu updates before and after PauseMenu.

### A20 — P2 — Runtime level-list growth can overlap fixed sections

**Conditional layout defect.** `MainMenuController.cs:243` grows campaign rows downward, while sandbox/custom rows retain builder positions (`MainMenuBuilder.cs:269`, `:287`). Custom rows are unbounded (`MainMenuController.cs:287`) inside a fixed area without a ScrollRect (`MainMenuBuilder.cs:242`).

**Impact:** More registry entries than the generated prefab expects overlap later sections; enough custom levels overflow the screen. The current one-entry menu does not itself demonstrate this.

**Recommended change / acceptance:** Reflow all sections using live counts and provide a bounded scrolling viewport. Test an expanded registry and ten custom saves at several aspect ratios.

### A21 — P3 — WebGL exposes display controls its apply path skips

**Confirmed platform mismatch.** `SettingsApplier.cs:197` intentionally skips resolution/display mode in WebGL. `SettingsMenu.cs:301` still permits DisplayMode, with no browser-specific explanation.

**Impact:** A saved setting can appear to work but have no effect. Resolution exposure also depends on browser enumeration.

**Recommended change / acceptance:** Disable/explain browser-owned options or connect an appropriate browser gesture path. Verify in an actual browser build. Separately, the native-resolution selector still writes concrete dimensions (`SettingsMenu.cs:436`); decide the desired native-reset behavior before changing it.

### A22 — P2 — Default radio ducking suppresses boss music

**Confirmed source/data conflict.** `Assets/Scripts/Audio/LevelRadio.cs:151` sets `AudioManager.MusicDuck = 0`. Boss start selects boss music (`AudioManager.cs:138`), but the fade still multiplies that duck (`:163`, `:167`). Radio does not subscribe to boss events. Autoplay defaults true and a Level_01 radio track is present, while its description promises boss takeover.

**Impact:** With radio active, the boss transition can select an inaudible score while the radio continues.

**Recommended change / acceptance:** Give radio/boss music explicit ownership and restore it on the appropriate death/defeat transitions. Test boss entry, death, re-entry, victory and manual radio-off.

## Design assessment — proposals, not bug claims

### D01 — State what a competitive run measures

`PlayerStats` changes damage, health, flask capacity and Pyre gain; level-up can pause while the run timer excludes that state. This can be intentional route/build strategy. It is not automatically cheating. Define the initial loadout/stat state, legal upgrade opportunities and valid finish conditions before balancing par times. For the next evaluation, hold starting conditions constant so improvement can be attributed to execution. This does not require deleting progression or adding another mode.

Ghost records carry scene/level, time, deaths and samples but no reviewed content revision or build/loadout identity. A route/motor revision can make an old PB incomparable. Establish invalidation/version policy before presenting times as comparable across updates; remote verification is not implemented.

### D02 — Clarify whether upgrades are run-local or persistent

`Assets/Scripts/Player/PlayerStats.cs:9` and `Progression/SoulsWallet.cs:8` keep runtime values on scene objects. The reviewed progression classes have no save/load path for those stats. This is acceptable for a run-based economy, but misleading if the player expects permanent RPG growth. Document and communicate the intended lifetime; do not implement permanent progression merely because a menu uses familiar stat names.

### D03 — Verify weapon roles with effective encounter time

The shipped dagger has 9 damage/13 posture over a nominal 0.22 seconds and a 1.35 perfect-window multiplier; sword 26/16 over 0.44 with 1.0; hammer 52/26 over 0.86 with 0.75. Raw posture-rate arithmetic favors the dagger, which also has a more forgiving perfect window. That is a reason to test roles, not proof of imbalance: reach, recovery, combo multipliers, parry posture, scaling and super duration matter.

Measure effective time-to-posture-break and safe damage opportunities against the same opponent. Confirm a valuable hammer route/encounter role before retuning it. The ordinary hit camera response uses the same `CameraShake.Small()` call (`WeaponController.cs:152`), but other sound/animation differences mean weight cannot be judged from that call alone.

### D04 — Preserve the landmark; establish a route-reading hierarchy

The lead inspected the historical `RouteShots/fog/2_spawn_to_T1_arena_91m_AFTER.png` from September 6. Its eclipse is recognizable, while cyan foreground forms and trims compete with landing edges. This is an observation about that image, not the externally revised current level. The capture has no gameplay HUD, so it cannot establish HUD overload.

Use fresh player-height views at actual decisions, with HUD and combat effects active. Prioritize immediate safe footing and threats by shape/value as well as hue. Additional bloom, textures or decoration should follow that test. Architecture/material richness can remain prototype quality while the route becomes clear.

### D05 — Add a deliberate comfort/accessibility plan

Settings expose sensitivity, FOV, quality, bloom, grain and volume, but not a general motion/flash scale, invert look, remapping or HUD/text scale (`SettingsData.cs:67`, `SettingsMenu.cs:32`). Camera feedback layers FOV kick, noise, directional kick, roll and wall lean; full-screen flashes are also used.

The absence of controls is confirmed; actual discomfort is not. Proposed priority: reduced camera motion/flash, inversion and control rebinding. Preserve gameplay timing and movement behavior when presentation is reduced. Existing development toggles are not equivalent to persistent player settings.

### D06 — Teach controller controls and let results support replay

`ControlsInfo.cs:23` hardcodes PC references and `HUDController.cs:378` uses `[Q]`. Scheme-aware prompts would make the existing controller bindings discoverable. The finish flow (`HUDController.cs:610`) automatically returns to menu after about 6.5 seconds at code defaults and runs on realtime even over a newly opened pause/settings panel.

Ghost code already displays NEW BEST or a result delta; do not report PB feedback as missing. The narrower recommendation is to let the player read the result and choose another attempt promptly, without an unwanted timed scene change. Any new UI is a proposal only.

### D07 — Listen to simultaneous threats before changing the mix

Pooled one-shots are 2D (`AudioManager.cs:99`), so they do not communicate threat direction/distance spatially. That may be intentional for a timing cue. Listen to overlapping enemies with radio on/off and judge cue attribution, masking and priority. More trim-table assertions cannot establish what the player hears under pressure.

### D08 — Measure how the level's duels affect its parkour-first promise

The authored route has three traversal spans, three gated legendary fights and a final boss. Checkpoints at approximately z=6, 105, 189 and 291 place recovery after completed sections. This is sensible structure. It could still become acceleration followed by long mandatory waits if fights do not shorten meaningfully with mastery.

Record first-time and practiced runs; separate movement, fighting, waiting for attack opportunities, recovery and menus. Teach ordinary movement before requiring compound advanced techniques. A first sentry interaction should demonstrate a useful movement reward that the player can predict and deliberately repeat. Keep advanced options available without requiring all of them for the first success.

## Verification and production assessment

### V01 — Existing tests are substantial but the summaries are stale

The inspected `TestResults/EditMode-20260907-132036.xml` reports **792 total, 792 passed, 0 failed, 0 skipped**, ending **2026-09-07 17:20:36 UTC**. This is a historical artifact; this audit did not run it and does not establish which later changes it covers.

`docs/VERIFICATION-REPORT.md` and `docs/HANDOFF.md` still emphasize older 607/745 counts and prior commits. Historical feature results report 777/777, but no new feature suite was run here. Do not combine dates/counts into a claim that the current HEAD is fully verified.

The route analyzer explicitly excludes enemies, gates and player aim (`Assets/Editor/LevelArcAnalyzer.cs:30`). Its shared mathematical model is valuable, but individual reachable arcs do not prove an understandable full route with realistic stamina and incoming attacks. Searches found presentation tests mentioning ghosts, but little coverage for the actual run recorder/store/completion integration described above.

The next meaningful tests are boundary scenarios: pause mid-action, death with active movement state, custom death, finish-to-menu persistence, ghost hitch alignment and controller-only modal navigation. Retain existing math tests; do not inflate test count with assertions that merely restate implementation constants.

### V02 — Build success and serving files do not establish target playability

`Builds/last-build.txt` records a WebGL build at 2026-09-07 05:16:21 UTC, 30.8 MB, three scenes, High stripping and three warnings. It predates the current review. Prior documents record a Windows menu boot and WebGL HTTP serving; they explicitly leave browser gameplay unproven.

`Assets/Editor/BuildRunner.cs` has useful compile/target guards, registry-driven scenes, output summaries and build metadata. It intentionally builds the on-disk scene while warning about unsaved editor scenes. Do not infer that a current dirty editor scene is what a build contains. A further **P2 build-pipeline finding**, outside the 22 gameplay/product entries: the code deletes the prior output directory before building (`:162`, build at `:188`), so a failed build removes the previous successful deliverable. Build into staging and promote on success; until then, retain a known-good distributable outside that working output.

A later runtime verification pass should cover Windows and WebGL startup, pointer lock and Escape, focus loss, audio start, settings, save/reload persistence, a full level finish and sustained performance. No frame-time, GPU-budget, memory-growth or long-session stability claims are established by this audit. Package installation and network backend behavior were not exercised.

### V03 — Keep generators and documentation aligned with the actual task

Generated data remains a strong reproducibility strategy. A source initializer is not a shipped ScriptableObject value, and generator 3 can clear references restored by generator 4. Future fixes must identify their required generator chain and read back changed assets. None was run here.

Custom documents deliberately omit campaign arenas, pedestals, sky and kill-zone replacement (`LevelDocument.cs:70`); that is an explicit partial round-trip, not proof that export loses supported data unexpectedly. Avoid expanding the shelved editor unless needed to fix shipped custom play.

Legacy claims to retire when documentation is next updated: F10 release input is already gated (`InputReader.cs:176`); the inspected turret asset uses 1.5-second surge rather than the handoff's 2.0; the level definition contains enemy spawns rather than supporting a blanket zero-enemy claim; PB/result feedback and ghost systems already exist.

## Suggested order for a later implementation pass

1. **Make attempts coherent:** A01–A03, A08 and A11. Define pause/reset ownership before applying isolated fixes.
2. **Make hits trustworthy:** A04–A07. Preserve readable cues and intentional cancellation while repairing geometry and ownership.
3. **Make completed runs persist and compare correctly:** A12–A17. Use one level identity and one completion transaction; validate clock alignment and recovery.
4. **Make the product operable:** A18, A19 and A22, followed by list/platform cases A20–A21.
5. **Close conditional route traps and performance behavior:** A09–A10, then actual route, audio, animation, comfort and target playtests.
6. **Use observed player behavior to choose content work:** D01–D08. Defer new mechanics, roster growth and broad cosmetic passes until a player can understand failure, identify an improvement and voluntarily repeat a run.

This ordering is a recommendation, not authorization to implement it. Several findings share a root cause; fix the owner and its consumers together rather than making a large collection of unrelated patches.

## Handoff prompt for Claude

> Read `docs/ASTRA-SYSTEMS-AUDIT-2026-09-07.md` and the current repository instructions. This is an Astra source/data audit, not a runtime certification. It proposes work but authorizes no code, asset, scene, settings or generator changes. Recheck the chosen finding against the current HEAD, distinguish confirmed mechanisms from conditional reproduction and design choices, and follow the owner's next instruction. Preserve unrelated work. Do not revive the stale F10 blocker, claim PB feedback is absent, or treat historical test counts as proof of the current tree. When implementation is explicitly requested, use the finding IDs, define expected behavior, verify the affected interaction, and update the required dataflow/generator evidence.
