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

## 5b. The Ascent has no wall-jump route, and should

Wall jumping shipped with one route use (the recovery pylons on The Long Span) and one incidental one
(`T2_Tower` is a wall like any other, so a missed ledge on the spiral can be saved off it). **The Ascent —
a 20 m vertical tower, the obvious home for the mechanic — has no authored line.**

What was tried and cut: two facing slabs on `T2_Entry` forming a 2.6 m chimney that climbed ~8 m and dropped
you on `T2_L6`, skipping five ledges. It worked mechanically. It was cut because a screenshot from the
player's own arrival angle showed it as **one undifferentiated black slab a metre from the face** — the slot
is invisible from the only direction anyone approaches it, and moving it far enough west to be readable moved
it out of the sightline entirely. A shortcut nobody can see is not a shortcut.

The design that should be tried next, and why it was not: a single **buttress fin on `T2_L2`**, at
`center (5.6, 10.2, 123), size (0.6, 7.4, 2.6)`, forming a **2.8 m chimney with the tower's east face**
(x = 2.5). You enter it airborne off L2's west edge, climb five pushes to ~y 17, and top out on **`T2_L8`**
(top 15.5) directly above — skipping L3-L7. It is architecturally honest (a buttress springing from a ledge
against the tower it circles), it is a vertical fin rather than a wall across the path so it cannot fill the
frame, and you look through the slot from the approach rather than at a face.

**It was not shipped because it risks obstructing the existing `T2_L2` → `T2_L3` hop**, which leaves L2 over
its north-west corner and passes straight through where the fin would stand. `CheckHop` measures box-to-box
gaps and would *not* catch an obstruction in the middle of the arc, so this needs a real play test — jump
L2 → L3 with the fin in place — before it can go in. Do that first, and shorten or shift the fin north/south
until the hop is clean.

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
