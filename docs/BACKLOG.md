# Historical backlog

This file preserves prior requests and known gaps as historical evidence. Its contents are not current
authorization; the supersession notice below controls what may be resumed.

> **Superseded 2026-09-08.** The user explicitly said to disregard the rest of this backlog. The sections
> below remain as historical context and evidence, not as authorized work. The explicitly authorized
> 2026-09-08/09 pass is complete in source and generated assets: release-build F10 gating through an
> in-game console command, native-resolution/settings repair, toggleable first-person arm movement, Wall
> Surge retuning, larger post-first-miniboss sections and isolated solar realms, sphere/platform/weapon
> VFX, atmosphere/lighting, solar-entry audio, the agent-agnostic dashboard, and reusable route-authored
> projectile encounters including the Heavy Sentry's three-shot parry phrase. Human feel review remains;
> do not resume any other historical row without a new user request.

Related: [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md) — what is proven and what is not.

---

## 0. The pivot (2026-09-04): parkour first, enemies as tools — ALL SIX BUILT, none played

The user's words: *"instead having small enemies in the between levels parkour just focus on making the
parkour better and the movement work better with the levels like neon white and use the enemy placement and
other creative things to get around certain parts of the map."* Six pieces, in the order they were built (each later one needed the earlier ones to exist as data). Every one
of them is now BUILT — the markers below were added 2026-09-06 after a session read the unmarked rows as
unbuilt work and told the user water had never been started. **The open item is not code, it is a human on the
spans.** The intended shape each was built to:

| Piece | Intended shape |
|---|---|
| **Balloons** — **BUILT 2026-09-04** (`Balloon.cs`, `BalloonDef`, `motor.Launch` / `RearmDash`; three orbs in Level_01, unplayed) | The user's words: *"a floating orb that gives you a boost and allow you to dash through it."* Neon White: bounce up off a buoyant balloon demon, or discard-dash INTO one and bounce off it; balloon pops are "capped" jumps. Here: a `BalloonDef` on `LevelDefinition` + a `Balloon` prefab from `PrefabFactory` — a floating orb on `Interactable` that (a) BOUNCES the player straight up with an authored launch speed when touched from any direction or hit by melee, and (b) if the player is dashing when they reach it, lets the dash carry THROUGH it and re-arms the dash on the far side (the orb is the dash's refuel). Pops on use, respawns after a delay. Launch goes through a motor entry point (`Launch(Vector3 velocity)` on the motor clock, rule 1), never a velocity write from the orb. Placed by `LevelDefinitionBuilder`; the analyser learns it as a reach primitive. |
| **Grapple exit burst** — **BUILT 2026-09-04** (`pullBurstPending` armed by `OnPullEnded`, consumed by `TryDash`; `perfectBurstWindow` 0.12 s pays `perfectBurstBonus` 30, unplayed) | Keep the grapple finish (`PlayerItems` → `BeginPull` → deathblow). On arrival, a window (~0.25 s) in which a dash press "explodes out" along the move axis at dash speed without spending the dash's stamina or cooldown — one flag on the motor consumed by `TryDash`, raised by `OnPullEnded(arrived)`. Feel: the dash package plus a bigger FOV punch. |
| **Parriable projectiles** — **BUILT + RE-AUTHORED 2026-09-09, automated verification complete; human feel review remains** | Every shipped shooter belongs to exactly one data-authored route-audit group with a bounded engagement window. `ProjectileFlightMath` plans swept-sphere contact with the same moving-target homing law used at runtime. Only the explicitly progress-gated T0 opening uses a volley coordinator; ordinary T1-T4 sentries remain autonomous and repeat their normal range, LOS, facing, blocker and cue-safety loop instead of waiting for invisible route corridors. The Heavy Sentry is a finished stone reliquary silhouette and fires three contacts 0.42 s apart, then rests 2.4 s; three returns kill it and five strongest-blade parries break its 330 posture. `Projectile Encounter Report` proves the authored route geometry at 11 / 17.6 / 27.5 m/s. |
| **Water** — **BUILT 2026-09-04/05, matches this spec in full, unplayed** (`WaterDef` + `WaterVolume` trigger with `boostHeight` 0.35; motor `waterSpeedScale` 1.35 / `waterAccel` 30 / `waterGrace`, derived `InWater` / `WaterFlow` / `WaterFloorSpeed` so no tuning field moves; the slide on water is a skate with no friction, no decay end and no duration cap; `WaterFx` spray + hiss off `PlayerFeedback`; in Level_01 and the sandbox yard; `PivotMovementTests` / `LevelTraversalTests` / `MovementYardTests`) | The user's words: *"flowing water on the ground where you can just speed boost and slide around like skating."* Neon White: walking on water is the fastest movement — no ground friction, a boost zone that extends a little above the surface, jumping up steep water slopes beats running. Here: a `WaterDef` (a flat box volume with a flow direction) built as a trigger + a visible surface; while the player is inside (or within ~0.3 m above), the motor uses a water ground mode: ground friction off, a speed floor/boost along the flow, slide-like steering (skating), the slide never ends on water, and the camera/feel layer gets a spray + a low hiss. The boost is a `WaterSettings` derived while `IsInWater`, like `WallRunSettings` under the surge — the motor's tuning fields never move. |
| **Level rework** | **BUILT + VERIFIED 2026-09-09; human route-feel review remains.** The post-first-miniboss T2/T3 spaces were rebuilt locally, not merely translated: a ~28 m T2 helix with wider terraces, a 9.6 × 26 m T3 span and broad pillars, a true 13.5 m first wall-run gap, a four-balloon arc, and isolated solar spheres with 6 m visual membrane bands. Final section offsets are T2 +38 m, T3 +70 m and T4/boss +96 m; all route content, gates, pickups and kill bounds move with their section. The three level-span suites, arc/clearance tests and projectile report pin the shipped data. |
| **In-game level editor** | **BUILT 2026-09-05 (v1)** — `docs/LEVEL-EDITOR.md`. F10 / F1 row / main-menu CUSTOM list; first-person placement of platforms, wall faces, balloons, water, spawns, pickups, checkpoints, torches and the player start on a grid; grab, rotate, resize, delete; SAVE/LOAD as `LevelDocument` JSON under `persistentDataPath/levels/`; PLAY rebuilds with a runtime NavMesh; EXPORT ASSET writes a real `LevelDefinition`. One piece factory shared with `8. Build Level From Definition`. Left out of v1: terrain, lighting, undo, multi-select, arena gates/pedestals, a scrolling custom list. |

Design guidance for all of it: [MOVEMENT-PRINCIPLES.md](MOVEMENT-PRINCIPLES.md) (added 2026-09-04) — in particular corner correction and near-miss forgiveness before any gap gets tighter, and shapes (arcs, lines, curves) as the unit of level composition.

Historical questions at the time were how deep the editor should go and whether Level_01 filler spawns
should be removed or kept as tools. Both are superseded context, not current work.

---

## 0b. Queued after the level rework (user, 2026-09-05)

- ~~**The slide costs stamina.**~~ **BUILT 2026-09-06** (the user asked again; it had sat here unbuilt).
  `PlayerStamina.slideCost` 12 — the wall-run/wall-jump price, with the dash still the expensive burst at 30 —
  spent in `FirstPersonMotor.TrySlide` after every refusal gate so a refused slide is free; `StaminaAction.Slide`
  names the refusal; written by `PrefabFactory.BuildPlayer`, pinned by `StaminaTunablesTests`. The landing-slide
  and the chain falloff are untouched.
- **An owner key on the standing prompt slot. BUILT 2026-09-06** (the user asked for it by name). Two steps:
  flashes ("PERFECT", "NO TARGET") moved to their own `GameEvents.PromptFlash` channel so they hand the
  standing cue back; then every standing write got an owner — `RaisePromptChanged(PromptOwner.Grapple, s)` —
  and a clear now lands only when the clearer still holds the line, or nobody does. `PromptOwner` lists the
  seven writers; `PromptView.AcceptsStandingWrite` is the pure rule; `PromptOwnerTests` and the feature
  suite's `Prompt_*` pin it. See DATAFLOW "THE PROMPT LINE".

- **Re-tune the perfect-timing stamina regain** ("more reasonable to do but takes skill"): play-test the
  three windows (wall jump 0.14 s at the let-go, dash-jump 0.04–0.16 s, burst 0.12 s) and their refunds
  (20 / 30 / +30) against a hand on the stick; widen or move the *anchor* before widening the window
  (MOVEMENT-PRINCIPLES rule 4); consider a partial refund band around the perfect so a near miss pays
  something; keep a miss free of penalty. Both changes are motor/stamina data + tests, then one editor pass.

## 0c. Level editor QOL pass — finalized 2026-09-05, shelved

Finalized 2026-09-05 in one bounded pass (see `docs/LEVEL-EDITOR.md`, "Finalized and shelved"): placement at
the press-frame preview, the carried piece skips its own aim ray, a three-frame surface debounce, one size
number stepping from the aimed piece with a numeric readout, Esc cancel, Ctrl+Z / Ctrl+Y (50 deep), Ctrl+D,
arrow nudge, `P` / PLACE HERE, and a grid decal on any aimed surface. Left out: the preview is invisible
when it sits inside the slab it would be built on, the decal is flat on slopes, no redo button, a Water /
wall-face nudge uses the platform anchor rule. Then shelved by the user: *"it seems like a waste to keep spending so many tokens on this when the
Unity editor can do it."* The in-game editor stays at v1 as a test-and-tweak tool; levels are authored in the
Unity editor per [LEVEL-AUTHORING-TUTORIAL.md](LEVEL-AUTHORING-TUTORIAL.md). The notes below stay as the
list for whoever picks it up later.

The user's words after playing v1 with the new controls: *"it works for the most part but placing of the
items is really janky and resizing them, the size buttons still half work or get bugged out, and overall
QOL of that tool needs improvement."* Shape: a dedicated pass with a hand-on-mouse loop — reproduce each
of: placement jank (does the preview lag the aim, does a click land where the preview was, does snap
fight the grab), the size buttons (half-working / stuck state — likely the same second-copy-of-state bug
family as the piece-kind buttons), and a QOL list (undo, duplicate, nudge with arrow keys, a visible
grid on any surface, numeric size readout, a "place at player" button, escape to cancel a grab). Prove
each with a scripted session AND a screenshot, then a human pass.

## 1. Nobody has played this

**This is the largest open risk in the project, and no amount of further automation closes it.**

The suite stands at 522 assertions, all green with zero skips. Every one of them proves a state machine. **None proves the game is
playable.** Specifically unproven:

- **The jumps.** Every gap in the four-tile level satisfies the documented reachability contract
  (rise ≤1.5 m / gap ≤4.5 m, or rise ≤1 m / gap ≤6 m) *on paper*. Nobody has jumped one.
- **The three legendary fights.** The Iron Penitent's spin economy is proven in numbers — 8 clean
  deflects break his 260 posture, the stagger is 5.0 s against a 2.2 s longest recovery — but nobody has
  fought him. Whether the cadence is rideable or a blender is a feel question.
- **Whether a dropped gate leaves a gap a player fits through.**
- **The lock-on assist.** Built so any mouse motion zeroes the assist that frame. Whether that reads as
  help or as a fight has never been tested by a human hand.
- **The deathblow mark**, the riposte's cinematic beat, and whether the Pyre fire reads as escalating
  charge *in motion* rather than in stills.
- **The main menu.** Its 36 assertions pass, but no human has clicked Play.
- **The 2026-09-03 movement retune.** Wall-run entry gates, the wall's top speed, the momentum soft caps,
  the diminishing slide boost and the stamina budget + HUD were all built from a research brief and
  arithmetic in one pass, straight after the user reported the old tuning felt bad. **Played once that
  evening: "wall running and movement feels better"** — but "needs more weight, I can still just shoot off
  a wall or ledge", "more control in the air like CS:GO surfing", "you need to be able to slide-jump".
- **The 2026-09-03 evening weight pass**, unplayed: `fallGravityMultiplier` 1.5, `airCarryDecay` 0.8/s,
  `AirSteer` 120°/s, the landing tax (16 → 26 m/s, up to 35%), and the ground snap that fixed the
  slide-jump. Plus the **sandbox movement yard** (east of the arena through the doorway, or F1 → MOVEMENT
  YARD), the **top-left status strip** (items and effects), the **wand pedestal hidden unless F1 →
  WAND PEDESTAL: ON**, and **wall-run particles** (grit off the feet at 44/s falling with speed and age,
  three sparks per foot-tick; `WallRunLive_*` in the suite proves they fire, only eyes prove they read). First things to feel: does a wall exit arc and land rather than sail; can you carve
  a 17 m/s exit onto a landing with the stick; does the slide-jump fire every time; and are the Ascent's
  hops (T2, 4–10 of 25 launch points under the heavier fall, the same as at 1.35×) stingy now — if so the
  fix is the ledges, not the gravity.

`DebugHarness` and `FeatureTests` parry on a state transition — frame-perfect information no human has.
They prove the state machine, never that the game feels good or is fair.

---

## 2. ~~Enemy silhouettes are only half-fixed~~ — closed

`MaterialFactory.Configure` was forcing smoothness 0 **and** specular-highlights-off on every material,
so a backlit enemy had only a diffuse term and could not show curvature at any light level. Smoothness is
now per-`Spec` (default 0, neon shapes unchanged) and `M_Enemy` ships at 0.34. The torso now carries an
interior gradient instead of reading as a uniform cutout. Not emission — "enemies do not glow" holds.

---

## 2b. The player model - asked for, scoped, NOT built (open decision)

Asked for on 2026-09-03: *"make the hands and feet look better and the player model, since now with the
movement the player is gonna see it."* Surveyed and scoped, then stopped unbuilt when the user set the
**model-boundary rule** (see `AGENTS.md` hard rules: an Opus session adds features and does not rewrite a
system Fable wrote without being told to). The three slices, and which side of that line each sits on:

| Slice | Rule | What it is |
|---|---|---|
| **Body, legs, feet** | **BUILT 2026-09-04** | `PlayerBody.cs` under the root: hips / chest / two 3-segment legs of primitives, a distance-driven gait in step with the hand bob, an air tuck, a slide pose thrown out on a spring with the leg root yawed to the velocity, boots rolled into the face on a wall run, and a landing knee dip off `LastLandingSpeed`. Casts shadows (the player finally has one). One wiring line in `PrefabFactory.BuildPlayer`. Still open: a proper "air trail" and any silhouette better than boxes. |
| **Arms react to movement** | adds a channel to 3 Fable files | Nothing in `Assets/Scripts/Feel/` references either viewmodel: wall run, slide, dash and air steering drive the **camera only**, and the walk bob runs only when `IsGrounded && speed > 0.5`, so the hands are rigid through every airborne move. A pure `MovementPose.cs` plus an additive offset summed onto `Model` after the pose and sway in both viewmodels, driven from `PlayerFeedback.Update`. |
| **Rebuild the hands** | **an override** | Each hand is 8 flat cubes (palm, four finger slabs, thumb, cuff, band) with no joints; each arm is 2 stretched cubes. Jointed fingers, a wrapping thumb, knuckles, a bracer and an elbow cop would rewrite `PrefabFactory.BuildHand` / `BuildArm` and extend `ViewmodelArm`'s solver - squarely a Fable system. |

Capture tooling for before/after already exists: `VibeGame1/Photograph Weapons` renders through the player
prefab's own camera and can pose the arms by name (`RebuildAndShoot()` regenerates first).

---

## 3. Known drift and fragility

- ~~`LevelGreyboxBuilder` writes stale spawner names.~~ **Closed as documentation, not a rename.** Its
  `GruntA/B/C/D` names are internally consistent for the pre-rework course it builds, and renaming a
  deprecated path is churn. `BuildHardcoded` now logs a warning that it is the legacy course with the old
  naming dialect and that `6. Build Level` restores the shipped level.
- ~~`DebugHarness` cannot reach the sandbox.~~ **Closed** — `FindSpawned` now falls back to matching on
  enemy KIND after the exact and suffix passes, so a scene-specific naming dialect degrades to finding
  the right kind of enemy rather than to a silent no-op.
- ~~Three marker classes duplicate the same billboard code.~~ **Closed, but not as proposed.** On
  inspection the three do NOT share facing maths — the posture bar yaws only and stays upright, the
  deathblow mark billboards then rolls about the view axis, and the lock-on dot billboards on all three
  axes. Those are three different reads, and a base class with a mode enum would have hidden that. Only
  the genuinely duplicated part — the `Camera.main` cache and its null/destroyed guard — was extracted,
  into `ViewCamera`.
- ~~`WeaponController.Awake` caches the viewmodel with an active-only `GetComponentInChildren`.~~
  **Closed** — now passes `includeInactive: true`.
- ~~`MaxSimultaneousAttackers` is a static with no Inspector exposure.~~ **Closed** — the shipped value
  lives on `GameFeelSettings` (written by `DataFactory`, rule 9) and is seeded into the static by
  `GameManager.Awake`. It stays a static for the enemy hot path, but tuning now survives a reload and
  is inspectable.
- ~~`WandFactory` runs as step `3b` inside `0. Rebuild Everything` but has no test covering it.~~
  **Closed.** `Assets/Editor/Tests/WandDataTests.cs` — 18 EditMode assertions against the shipped
  `.asset` files (rule 9), covering the fields another system dereferences (a null `viewmodelPrefab`
  empties the offhand; a missing asset is a null loadout slot that silently degrades every riposte to the
  bare melee execute), the arithmetic (the `Clamp(recover*0.7, 0.16, 0.28)` stab hold spans
  0.160–0.280 s across the set; `cooldown > windup + recover` for all four), and every silent zero that
  disables a blast shape without an error — `splashDamage = 0` makes `Splash` a no-op while the full VFX
  still draws, and Lance's `Max(2f, blastRadius)` floor would ship a 2 m pierce from a 0. Plus the trades
  the comments claim: windup order and cooldown order agree, no wand dominates another on all six axes,
  and hue separation is ≥ 45° (measured minimum 58°) so you can tell which wand you hold.

---

## 4. Test-suite flakiness

One intermittent remains, a staging race rather than a game bug:

- `Trail_ClosesAfterStrike` (2026-09-07) — failed once in a full run, passes 3/3 in isolation and
  777/777 on the re-run. The test samples on **realtime** (`WaitRealtime`) while `AttackCo` advances on
  `Time.deltaTime`, so a loaded or unfocused editor lets the coroutine fall behind and the ribbon is still
  open at the 1.00 s sample. The fix, if it recurs, is to sample against the coroutine's own progress
  rather than against a wall clock — not to widen the window, which would stop the test proving the ribbon
  closes at all.

- `Movement_LandsAndGrounds` — failed once on a first run after entering play mode, never since.
- `Flask_DrinkCoroutineAndInterruptOnHit` — observed `noHeal=False` once (the heal landing before the
  interrupt), green on every run since. Watch it; if it recurs, the fix is to sequence the interrupt
  against the heal frame rather than against realtime.

Also 2026-09-03 evening, each red exactly once across four full runs and green when its section runs
alone: `Trail_LiveDuringStrike`, `Deathblow_StaggerPoseStaysFramed_BossScale` (both in the run where the
editor gained focus and frame time went 4 → 17 ms), `Items_GrappleBigTakesPosture` (`actual=0`; 21/21 in
isolation). The pattern is the same as below: what the previous test left behind. Fixed the same evening:
`WallJump_ChainsBetweenFacingWalls` (chimney now starts from a full stamina bar) and `Stamina_RefillsToFull`
(the test teleports home after its three dashes before timing the regen).

Fixed 2026-09-03: `Level_CheckpointHeals` / `_RefillsFlask` picked `FindObjectsByType<Checkpoint>()[0]`,
which is sometimes the checkpoint already current, and `SetCheckpoint` is a no-op on that one by
design; the test now picks a non-current checkpoint. `Slide_EndsWhenAirborneButKeepsSpeed` was a real
motor wrinkle (one grounded friction frame after `Launch`), fixed in the motor — see ENGINEERING-LOG.
`Items_PhysicsPickup` (`held 0 -> 0`) and `Stamina_RegenWaitsItsDelay` each went red once in the
retune's first runs and green since; both are on watch.

Fixed already and worth copying as the pattern: `Items_PhysicsPickup` and `Progression_BloodstainRecovery`
both shoved the player blindly forward into the wand pedestal plinth. Both now sweep eight compass
directions for a clear path **and** ground before walking, and use 14 m/s rather than 6 (ground friction
is 14/s, so a 6 m/s impulse travels ~0.4 m).

---

## 5. ~~Two remaining input-gated skips~~ — closed

`TryInteract()` on `WandPedestal` and `TryCycle()` on `WandController` are now public entry points that
`Update` calls with the polled input (rule 2 intact: `InputReader` still does the reading). The suite
runs with **zero skips** for the first time — `WandPedestal_FOpensMenu` exercises the real gating and
`WandPedestal_InteractRefusedWhenLookingAway` proves it refuses when it should.

---

## 5b. ~~The Ascent has no wall-jump route~~ — built, unplayed

`T2_Buttress` — `center (4.6, 10.2, 122.5)`, `size (0.8, 7.4, 3.0)` — hangs on `T2_L2`'s west FACE and
forms a **1.70 m chimney** with the tower's east face, 2.50 m of face overlap, open to the sky. You leave
L2's west edge around z 125, climb **five pushes to y 15.85** and top out on **`T2_L8`** (top 15.5),
skipping L3–L7 and `Pickup_T2_Surge`. Optional, out of reach of the base kit (9 m of rise), rejoins
34 m short of the T2 arena trigger, and a miss is a death — which is the right price.

**The blocker is gone rather than dodged.** `LevelArcAnalyzer` flies the real ballistic arc and reports
that the fin costs `T2_L2 → T2_L3` **two of twenty-five sampled take-off points and no clearance at all**
(best line unchanged at 2.02 m); the points it costs are on L2's south-west quarter, and the natural line
over the north-west corner is untouched. `LevelArcClearanceTests.Buttress_DoesNotObstructTheL2ToL3Hop`
keeps it that way, and all 29 baseline hops are clean. The fin sits entirely off the deck, so the run-up
is intact.

Two things the previous attempt got wrong and this one addresses: it is a **vertical fin seen edge-on**
from the approach rather than a wall across the frame, and `Torch_T2_Buttress` at `(6, 6.5, 122.5)` lights
it warm against the tower's unlit east face. The shot from `T2_Entry` shows **sky through the slot**,
which is precisely the frame the cut version failed.

**Still open, and honestly.** No human has climbed it. The analyser models far less control than a player
has — fixed push timings, one brake, no continuous steering — so it can say the route exists and cannot
say how forgiving it is; in its own sampling only 3 of 36 entry positions succeed, which understates a
real player but is not nothing. And standing ON L2 the fin is a large unlit black mass filling the left
third of the frame; the torch rescues it from below, but that face wants a second look.

Shipped alongside: **`T3_Fallen_Lintel`** at `center (0, 26.15, 222)`, `size (5, 0.8, 1.2)` — a fifth
obelisk fallen across The Long Span between the first standing pair. 1.25 m of clearance against a 0.90 m
slide capsule and a 1.80 m stand, top 2.05 m above the deck, so it costs **time, never access** — the same
contract `T1_Fallen_Obelisk` already keeps. Also unplayed.

**Deliberately NOT added:** T2 climbs 1.5 m per ledge so no slide-jump shortcut is legal there (the
envelope caps rise at 1 m), and T3's pillars are 2.5 m squares that cannot host a slide entry. Scattering
more tech would have been slop.

**Since then: wall-run walls on spans 1–3.** `T1_Wall_Start` / `T1_Wall_Causeway`, `T2_Wall_East` /
`T2_Wall_West`, `T3_Wall_Pillars` / `T3_Wall_Span` (with their landings) are authored in
`Level_01_Level.asset` and flown by `LevelSpan1-3Report`; `LevelSpan1Tests` / `LevelSpan2Tests` /
`LevelSpan3Tests` assert that a sprint entry runs each wall and arrives, that the landing needs the wall
and rejoins the course (the spiral, on T2), and that the walls stand off every deck and ledge. Same status as the buttress: proven by
arithmetic, unplayed by anyone.

---

## 5c. The Legendary Ninja fails the riposte frame

Found 2026-09-03 when the feature suite's dummy turned out to be whichever spawner came first. With
`Legendary_Ninja` as the subject, `Deathblow_StaggerPoseClearsNearPlane` and `_StaysFramed` fail at both
scales (`nearest=0.00 m, cameraInsideBody=True` at the 3.5 m stand-off) and its mark height (1.28) sits
only 0.23 m from the lock dot (1.05) against the 0.3 m floor. The suite now measures a grunt on purpose, so
this is **open, not hidden**: run `DeathblowFraming` against each `Legendary_*` prefab (its failure message
now names the offending renderer) and either move the stagger pose, the stand-off, or the mark height per
body in `MiniBossFactory`.

**Measured 2026-09-07, and it rules out the two obvious suspects.** All seven `Legendary_*` prefabs were
probed at rest, in the editor with no play mode, with the eye at 1.6 m and the standoff the test actually
uses (`Min(stabStandoff * scale, range)` = 2.20 m at x1, 3.50 m at x2.2). **Nothing is inside the camera and
nothing is even close to the 0.5 m framing floor:**

| Body | nearest at x1.0 | nearest at x2.2 | nearest renderer |
|---|---|---|---|
| Halberdier | 1.13 m | **1.15 m** (tightest of the set) | `EnemyMesh` |
| Revenant | 1.36 | 1.64 | `EnemyMesh` |
| Spellsword | 1.55 | 2.06 | `EnemyMesh` |
| Knight / Drillmaster | 1.58 | 2.15 | `EnemyMesh` |
| **Ninja** | **1.86** | **2.75** | **`Body`** |
| Marionette | 1.98 | 3.01 | `EnemyMesh` |

So the reported `nearest=0.00 m, cameraInsideBody=True` is **not resting geometry, and the Ninja is the
second-roomiest body of the seven**. The stagger pose is not the cause either: `EnemyVisuals.StaggerEuler`
is `-13 deg` and `StaggerSag` is `-0.14` in z — it leans the body BACK, away from the lens, which is the fix
that already landed. Whatever fails is therefore **live-only**. The leading candidate is that
`DeathblowFraming` computes its stand point from `dummy.transform.position` and only then waits 0.35 s
realtime for the pose to settle, so a body still closing distance during that wait ends up nearer than the
standoff it was measured for. Note also that the Ninja is the only one of the seven whose nearest renderer
is a primitive `Body` rather than a forge `EnemyMesh`, so it is built down a different path.

**Next step is a live repro, and it needs a deliberate spawn:** `Level_01` now has **zero** enemies in play
mode (the parkour pivot removed the filler spawns), so the failure cannot be reproduced by entering play
mode on the shipped level. Spawn a `Legendary_Ninja` in the sandbox, break its posture, and read the
`Deathblow_StaggerPoseClearsNearPlane_*` failure message — it names the offending renderer.

**Correction to the line above:** `markHeight = 1.28` is the **Drillmaster's** spec, not the Ninja's, and
only five of the seven bodies carry an explicit `markHeight` in `MiniBossFactory` (the rest fall back to the
`1.45` default at line 180). Confirm which body owns which value before moving any of them.

## 6. Smaller open questions

- **A rear watch region on the Legendaries — PARKED by the user, 2026-09-06:** *"save that for later, the
  rear region."* Item F of `docs/plans/soulslike-report-gap-analysis-2026-09-06.md`: an enemy that punishes
  you for circling behind it. The doubt that put it here is first person — you strafe around a Legendary's
  back and it whips out a rear attack whose wind-up was behind your camera, so the tell can only ever be a
  sound. Shapes if it comes back: cut it; keep it with a distinct directional audio wind-up; or duels only,
  where you are already reading one enemy closely. **Do not build it, cost it or re-ask** — it waits for the
  user to raise it. Nothing else in the duel work (A per-move cooldowns, B AI LOD, C the delayed attack) is
  blocked on it.
- **Boss framing at deathblow range.** A 2.2x-scale boss's torso is a wall across the middle of the shot.
  This is the `preferredRange`/`range` tension, not the stagger pose. Untouched deliberately — changing
  either affects every fight. **Numbers as of 2026-09-06:** `boss.preferredRange` is **4.6** (this line
  used to say 3.5), `attackRange` 3.0, and the deathblow stand-off is `stabStandoff` 2.2 x the boss's 2.2
  scale. The gap moved; whether it FRAMES has still never been measured or seen.
- ~~**The alert tell may want to be bigger, not brighter.**~~ **Closed: leave it alone, measured.**
  Rendered through the shipped volume profile at 1920×1080 / FOV 95 and diffed tell-on against tell-off,
  the 0.25 m cube already covers 47 × 60 px at the grunt's 3 m `preferredRange` — 5.6% of frame height,
  the same on-screen height as the deathblow mark. Bloom, not geometry, is most of its area. And growth is
  spent at the wrong end: the tell is clipped by the top of the frame at 1.5 m already, and 0.40 m reaches
  that edge sooner (gap at 2 m: 125 px → 83 px). Its top edge is also 0.025 m past the world posture bar
  at 2.6. Pinned in `Assets/Editor/Tests/AlertTellFramingTests.cs`. **Still open, and needs a human:**
  whether it *reads* in 0.45 s of peripheral vision, and whether angular sizing (it is 22 × 26 px at 6 m)
  is worth having.
- **`parTime` for the four-tile run is a guess.** It is **225** now, not the 240 this line used to say (verified
  2026-09-06 against `Level_01_Level.asset`); still nobody has run the level to see whether it is a fair par.
- ~~**42 point lights** in the level, up from 25. No performance measurement has been taken.~~
  **Measured, and it is 55, not 42.** `VibeGame1/Audit Level Lights` over 68 camera-height probes: 55
  additional point lights (43 with `FlickerLight`, ranges 7–9 m), **zero of them casting shadows** — the
  only shadow caster is the directional key light. The 42 m cull leaves 21.1 enabled on average (worst
  24); only **1.6 reach a given probe** (worst 6) against a per-object limit of 4, and only **1 probe of
  68** exceeds it. The count buys gather-and-sort plus 43 strided `Update()`s, not overdraw. The number to
  watch when adding torches is reaching-lights-per-point, not the total. **Not closed:** no frame cost has
  been measured — `PerfProbe` needs play mode.
- Two deliberate `SampleScene` leftovers: `Assets/Settings/SampleSceneProfile.asset` (a volume profile,
  not the scene) and `templateDefaultScene` in `ProjectSettings.asset` (inert URP-template residue).

---

## Closed

- ~~Wand selection pedestal at level start.~~ Shipped: look at it, press `F`.
- ~~Wands and magic must READ in the first-person view.~~ The cause was `stabStandoff = 1.25` parking the
  camera 0.8 m from the victim's surface; the blast now anchors to the wand tip.
- ~~Per-system dataflow maps.~~ Every system in `DATAFLOW.md` is `mapped`, and the standing rule is in
  `SESSION-PROTOCOL.md` under *While working*.
- ~~7 feature-test skips from input-gated behaviour.~~ Closed by the `Try*()` entry points.
- ~~The landing fight needs a human playtest.~~ Superseded by item 1 — the whole game needs one.

## Settle the surge decay against the reworked spiral (opened 2026-09-06)

`pshooter_enemy03.parrySurgeSeconds` is 2.0 s per stack, tuned blind against the OLD Level_01 spiral
spacing. The openness pass (`5885b9a`) dropped the spiral hop-to-hop travel to ~0.35-0.5 s, so a player
who parries one bolt at the bottom of The Ascent still holds stacks four hops later and the x1.60 ceiling
becomes trivially maintainable rather than something to fight for. Intended shape: either **1.2 s** per
stack (a stack costs about three hops of the reworked spacing), or decay tied to distance travelled rather
than time, which survives any future respacing. Change it in `DataFactory`, re-run generator `3`, and
assert the shipped value in `SurgeTurretTests` — a code default is not a shipped value (AGENTS.md rule 9).

## F10 opens the level editor in a shipped playtest build (opened 2026-09-07)

`Assets/Scripts/Debug/DebugKeys.cs` gates F5/F6/F7/F8/F9 behind
`#if UNITY_EDITOR || DEVELOPMENT_BUILD`, so they compile out of a playtest build correctly.
**`Assets/Scripts/Level/LevelEditor.cs` does not.** Only its EXPORT button is hidden outside the editor
(`#if !UNITY_EDITOR`, lines 200-202) — a playtester who presses F10 gets the fly-cam level editor over
their run. Found by the build-distribution worker while wiring `BuildRunner`; not changed, because gating
a gameplay screen is outside a build lane and the file is a system this session did not own.

Intended shape: wrap the F10 key read in `LevelEditor` in the same
`#if UNITY_EDITOR || DEVELOPMENT_BUILD` the dev keys use, so the editor is unreachable in a release build
while staying available in every development build. Decide first whether playtesters SHOULD have it — an
in-game editor is a fine feedback tool if the build is going to trusted testers.

## Publish the first playtest build (opened 2026-09-07)

**The build half is done.** `BuildRunner` has now cut real Windows builds (2026-09-07): output verified,
`build-info.txt` correct, the player launches and reaches the menu with a clean `Player.log`. `BuildRunner`
was also hardened so a build works whatever state the project is in — see `docs/DISTRIBUTION.md`,
"It is designed to work whatever state the project is in".

**The WebGL half is now done too (2026-09-07).** First WebGL build cut: **30.8 MB gzipped** (the wire size —
the `.unityweb` files ship pre-compressed), 4:56, 3 benign IL2CPP warnings, 0 errors. Every asset serves
HTTP 200 at full length from a plain static server, which also exercises the gzip decompression fallback.
Numbers in `docs/VERIFICATION-REPORT.md`, "Builds — 2026-09-07".

**Still unpublished, and two things still stand in the way.** Nothing is on GitHub. Blocked on one-time
settings only the user can click (Pages → Deploy from a branch → `gh-pages` → `/ (root)`), and on the F10
decision above. Also **still unproven: that the WebGL build RUNS** — serving proves the bytes are reachable
and correctly laid out, but only a browser proves the WASM instantiates and the pointer-lock gate works.

## The settings menu can overwrite "native resolution" with a fixed one (opened 2026-09-07)

`SettingsData.screenWidth/screenHeight` use `0` as the sentinel for *"whatever the display is already at"*,
and `SettingsApplier.ApplyDisplay` correctly falls back to `Screen.width/height` when it sees it. That
design is right and nothing about it needs changing.

**But `SettingsMenu.cs:439-442` destroys the sentinel.** Cycling the Resolution row calls
`NearestResolutionIndex(0, 0, …)` and writes back a concrete `resolutionOptions[i]` — so touching that row
once converts "native" into a hard number that persists forever in `vg1.settings.screenW/H`. On this
machine it had been left at **1366×768 on a 2560×1440 display**, which is what "the resolution is wrong"
in the built exe actually was. Clearing the two keys back to `0` restored native immediately.

The user's instruction (2026-09-07): *"it just needs to detect native and use that, nothing else, don't
overcomplicate those"*. Intended shape: the Resolution row should either be removed entirely (native
always, display-mode row kept), or gain an explicit "Native" entry at index 0 that writes `0/0` back, so
the sentinel is reachable once it has been left. **Not changed** — `SettingsMenu` and `SettingsData` are
Fable systems and this is a UI/behaviour change, not a build fix.

Worth knowing regardless: launching a Unity player with `-screen-width/-screen-height/-screen-fullscreen`
**persists those values** to `HKCU:\Software\vibegame1\vibegame1`, so they affect every later launch with no
flags. Do not smoke-test a build with forced resolution flags.

---

## ~~The maul reads olive, not gold~~ — RESOLVED 2026-09-07 by renaming it *Verdigris*

**The bind.** `hammer.neon` shipped as `#A8D12E` in `57626eb` (hue ~75°), moved there from `#E0661A`
(hue ~23°) because the old amber sat **~5° from the enemy bolt**, and in a parry game the player's own
weapon must not compete with the one thing that has to read fastest. `#A8D12E` clears the bolt by 47° and
satisfies the ≥45° rule the wand set already holds — but photographed under the real pipeline it read
olive-lime, and the weapon was called the *Sunbreaker*.

**Why there was no easy answer.** The bolt (~28°) and the unblockable cue's red family (~357°) between
them forbid roughly **312°–73°** of the wheel, which is every hue that reads as gold, amber or brass. Hue
separation and "warm" were mutually exclusive for this weapon. **This constraint has not gone away** — any
future pass that wants a gold maul has to move `Projectile.HotCore` first.

**What was decided (the user's call, 2026-09-07):** the colour stays and **the name moved**. The maul is
now **Verdigris**, its super **Bronzefall** (was *Sunbreak*). `#A8D12E` is close to the green that grows on
corroded bronze and the maul's head is a blocky brass mass, so as *Verdigris* the hue reads as age on metal
— intentional — rather than as an amber that missed. The rationale is written into `DataFactory` beside
the value so a later pass does not "fix" it back.

**Still true for anyone touching this:** the colour lives in **TWO** places — `DataFactory`'s `hammer.neon`
**and** the hardcoded copy at `PrefabFactory.cs:266` driving the viewmodel `EnergyGlow.tint`, because
`Energise` does not read `WeaponData.neon`. They are linked by a comment only. Change both, then re-run
generators 3 **and** 4 as one operation (rule 9).
