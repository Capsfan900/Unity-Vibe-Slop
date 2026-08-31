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

## 2. Enemy silhouettes are only half-fixed

The ambient/albedo pass lifted the backlit grunt from a measured 0.0 to 8.8 against a 36.3 floor — a
readable silhouette, but with essentially no interior modelling. The course is deliberately backlit, so
the side of an enemy facing the player receives **zero** key light and ambient is all it gets.

The honest remaining fix is a **rim/fresnel term** on `M_Enemy`, which needs a per-spec smoothness field
in `MaterialFactory.Configure`. It is explicitly **not** emission: "enemies do not glow" is a documented
feel contract — light means you deflected.

---

## 3. Known drift and fragility

- **`LevelGreyboxBuilder` still writes stale spawner names** (`Spawn_GruntA`) that no longer match the
  built level (`Spawn_T1_GruntA`). The two builders are out of sync.
- ~~`DebugHarness` cannot reach the sandbox.~~ **Closed** — `FindSpawned` now falls back to matching on
  enemy KIND after the exact and suffix passes, so a scene-specific naming dialect degrades to finding
  the right kind of enemy rather than to a silent no-op.
- **Three marker classes duplicate the same billboard code.** `EnemyPostureBar`, `DeathblowMarker` and
  `LockOnMarker` each reimplement "unscaled-time presentation animation on a world-space primitive". A
  small `WorldMarker` base with `Billboard(yawOnly)` would fold all three.
- ~~`WeaponController.Awake` caches the viewmodel with an active-only `GetComponentInChildren`.~~
  **Closed** — now passes `includeInactive: true`.
- **`MaxSimultaneousAttackers`** is a static with no Inspector exposure; resets to 1 on domain reload.
- **`WandFactory`** runs as step `3b` inside `0. Rebuild Everything` but has no test covering it.

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

## 6. Smaller open questions

- **Boss framing at deathblow range.** At the 3.5 m `range` a 2.2×-scale boss's torso is a wall across
  the middle of the shot. This is the `preferredRange`/`range` tension, not the stagger pose. Untouched
  deliberately — changing either affects every fight.
- **The alert tell may want to be bigger, not brighter.** Past peak 3.0 you only buy white; the next
  lever is the 0.25 m cube's size.
- **`parTime = 240`** for the four-tile run is a guess.
- **42 point lights** in the level, up from 25. No performance measurement has been taken.
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
