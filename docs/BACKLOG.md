# Backlog

Requested but not yet built, plus known gaps. When one is completed, move it into
`ENGINEERING-LOG.md` (if it involved a non-obvious problem) and update `DATAFLOW.md` + `ARCHITECTURE.md`.

Related: [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md) — what is proven and what is not.

---

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

`DebugHarness` and `FeatureTests` parry on a state transition — frame-perfect information no human has.
They prove the state machine, never that the game feels good or is fair.

---

## 2. ~~Enemy silhouettes are only half-fixed~~ — closed

`MaterialFactory.Configure` was forcing smoothness 0 **and** specular-highlights-off on every material,
so a backlit enemy had only a diffuse term and could not show curvature at any light level. Smoothness is
now per-`Spec` (default 0, neon shapes unchanged) and `M_Enemy` ships at 0.34. The torso now carries an
interior gradient instead of reading as a uniform cutout. Not emission — "enemies do not glow" holds.

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

- `Movement_LandsAndGrounds` — failed once on a first run after entering play mode, never since.
- `Flask_DrinkCoroutineAndInterruptOnHit` — observed `noHeal=False` once (the heal landing before the
  interrupt), green on every run since. Watch it; if it recurs, the fix is to sequence the interrupt
  against the heal frame rather than against realtime.

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
skipping L3–L7 and `Pickup_T2_Updraft`. Optional, out of reach of the base kit (9 m of rise), rejoins
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

## 6. Smaller open questions

- **Boss framing at deathblow range.** At the 3.5 m `range` a 2.2×-scale boss's torso is a wall across
  the middle of the shot. This is the `preferredRange`/`range` tension, not the stagger pose. Untouched
  deliberately — changing either affects every fight.
- ~~**The alert tell may want to be bigger, not brighter.**~~ **Closed: leave it alone, measured.**
  Rendered through the shipped volume profile at 1920×1080 / FOV 95 and diffed tell-on against tell-off,
  the 0.25 m cube already covers 47 × 60 px at the grunt's 3 m `preferredRange` — 5.6% of frame height,
  the same on-screen height as the deathblow mark. Bloom, not geometry, is most of its area. And growth is
  spent at the wrong end: the tell is clipped by the top of the frame at 1.5 m already, and 0.40 m reaches
  that edge sooner (gap at 2 m: 125 px → 83 px). Its top edge is also 0.025 m past the world posture bar
  at 2.6. Pinned in `Assets/Editor/Tests/AlertTellFramingTests.cs`. **Still open, and needs a human:**
  whether it *reads* in 0.45 s of peripheral vision, and whether angular sizing (it is 22 × 26 px at 6 m)
  is worth having.
- **`parTime = 240`** for the four-tile run is a guess.
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
