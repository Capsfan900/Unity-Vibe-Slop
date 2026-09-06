# Animation and VFX — principles, audit, and what to fix

What the craft literature says, what this project actually does, and the gap between them. Written from a
code audit against published guidance, not from taste.

Related: [ARCHITECTURE.md](ARCHITECTURE.md) (feel contracts, art direction) ·
[ENGINEERING-LOG.md](ENGINEERING-LOG.md) (the traps already paid for) · [DATAFLOW.md](DATAFLOW.md)

---

## 1. The principles worth holding to

### An attack has three phases, and each one has a job

| Phase | Job | Guidance |
|---|---|---|
| **Anticipation** (wind-up) | The warning. Stores energy. | Length ≈ player reaction (~0.25 s) + trigger time + a difficulty buffer. Each attack's anticipation must be **drastically different** from the others, or the player cannot tell them apart. |
| **Attack** (strike) | The damage. | Short. **Constant speed, not wavy** — an unambiguous arc with a clear direction. Weapon trails exist to make that arc legible. |
| **Recovery** | The window of opportunity. | Its length *is* the counterattack opportunity. This is a balance dial, not an animation afterthought. |

Two corollaries this project already lives by, worth stating so nobody "fixes" them:

- **Player attacks have a fourth phase before anticipation: the trigger** — decision, press, input lag.
  Input lag should stay under 100 ms for combat to feel sharp.
- **Readability is testable cheaply.** If a pose is not clear at three frames, it will not be clear at
  thirty. Judge a wind-up by its silhouette, not its detail.

### Impact is a pose, a pause, and a sound — in that order

A strong contact pose, a well-timed effect, and a brief pause that lets the eye *catch* the frame.
Hit pause works by dropping the playback rate of attacker and victim, not the whole world: the sharper
implementations ramp the rate down, hold, and ramp back, and deliberately let **particles and camera
effects keep running at normal speed** so the moment reads as impact rather than as a stall.

**Holding the follow-through slightly longer than the surrounding frames is what gives a weapon weight.**
It is the cheapest weight cue there is.

### Stylized VFX is shape, timing, colour — in that order

Shape carries intent (pointed reads as harm, round as benign). Timing shapes anticipation, climax and
dissipation. Effects obey the same animation principles as characters: squash and stretch, arcs, slow in
and slow out, secondary action. Build effects from placeholder shapes tuned purely for rhythm first —
anticipation, impact, fade — and only then make them pretty.

---

## 2. What this project does today

Audited from the code, not assumed.

**Sound already, do not regress:**

- **Hitstop scopes correctly.** `TimeScaleController` requests carry `affectsPlayer`; hitstop and slow-mo
  pass `false`, and `FirstPersonMotor` reads `PlayerDelta`, so an impact never freezes the player's own
  movement. This is hard rule 1 and it matches the guidance exactly.
- **VFX keep running during hitstop.** `SlashFx` animates on `Time.unscaledDeltaTime`, so effects and
  camera continue at full speed while the world is at 0.02 — which is precisely what the hit-pause
  literature recommends and what stops a freeze reading as a stall.
- **The viewmodel swing deliberately does NOT.** `AttackCo` uses scaled `Time.deltaTime`, so the arm
  freezes mid-swing during hitstop. That is the impact device, and it is a documented feel contract.
- **Enemy wind-ups respect reaction time.** No enemy attack wind-up is below **0.45 s**, and the parry cue
  fires `cueLead ≈ 0.20 + perfectWindow/2` (~0.28 s) before impact — the same arithmetic the guidance
  gives, arrived at independently and logged.
- **Anticipation silhouettes are already being judged the right way.** The Pale Marionette's tempo-break
  wind-up was rejected and rebuilt because the body kept drifting underneath it, making a 1.0 s wind-up
  look like another 0.76 s pass. That is the "drastically different anticipation" rule enforced in
  practice.
- **The slide has a body.** `PlayerBody` throws the legs out in front of the lens on a 6 Hz under-damped
  spring (~0.10 s, 9% overshoot) and yaws them to the velocity; the eye arrives on a 4.5 Hz spring that plops
  0.065 m below the slide height; a held 6 mm lens rumble rides the speed quadratically. Nothing brightens.
  What it does NOT do yet: the arms still ignore the slide (BACKLOG §2b, slice 2).
- **The health and stamina bars are liquid.** `FluidBarView` + `VibeGame1/UI/FluidBar`: a two-harmonic
  surface that flows along the bar, a meniscus at the surface and the leading edge, and a slosh driven by the
  player's own acceleration — tilt capped at ~2.7 px, a dash ripples the wave, a landing dips the level —
  all closed-form springs on unscaled time. Brightening is toward white and clamped at 1.0: the HUD never
  blooms.
- **The parry charge burns.** The PYRE meter is a procedural fire in the shape of a loading bar
  (`FireBarView` + `VibeGame1/UI/FireBar`): heat rises with the charge, a gain kicks it, a full meter holds
  a roaring band that pulses and never blooms. No texture, no particles, one quad, unscaled time.

---

## 3. The gaps, in priority order

### 3.1 There is no weapon trail — the swing arc is invisible ✅ CLOSED

**Finding.** `SlashFx` has `Arc`, `Ring`, `Beam`, `Flare`, `Sparks` — all `LineRenderer`-based, all spawned
at *impact points*. Nothing is attached to the moving blade. There is **no `TrailRenderer` anywhere in the
project**.

**Why it matters.** The trail is the standard device for making the dangerous arc legible, and it matters
more here than usual for two compounding reasons: the weapons were just reshaped into a **short dagger
family**, so there is less blade to see, and URP has **no motion blur** in this project, so a fast swing
has nothing to smear. A 0.22 s dagger arc at 60 fps is a dozen frames of a small object moving fast.

**Shipped.** `Assets/Scripts/Feel/WeaponTrail.cs`, on `ViewmodelRoot`, wired with every value written
explicitly in `PrefabFactory` (rule 9). Full map: [`DATAFLOW.md > Swing trail`](DATAFLOW.md#swing-trail).

| | Value | Why |
|---|---|---|
| Points / subdivisions | 12 / 3 per frame | the strike leg is 0.044–0.14 s; without interpolated points the "arc" is a two-segment elbow |
| Head width | 0.030 m | ~5% of screen height at the ~0.5 m the viewmodel sits from the lens |
| Width curve | `1 → 0.5 @0.3 → 0.18 @0.65 → 0.03` | URP/Unlit ignores vertex colour, so width is the **only** head-to-tail channel |
| Fade | 0.11 s, tail retracting | outlives a 3-frame arc, dies before the recovery does |
| Peak channel | **1.15** | over the 1.05 bloom threshold, under the ~1.25 ACES ceiling, far under the tell (3.00) and the mark (2.60) |
| Hue | `WeaponData.neon`, normalised | steel-blue / ember / green / pale violet — the trail carries weapon identity |

It is open for the **strike leg only** — never the wind-up, never the recovery — so the ribbon doubles as
the player's read on when the weapon is dangerous.

**Two corrections to this audit, both from running it rather than reading it.**

- **The fix is not a `TrailRenderer`, and the absence of one was not the bug.** A `TrailRenderer` emits
  its points in **world space**. That is right for a sword in the world and wrong for a viewmodel:
  turning the mouse mid-swing would leave the ribbon hanging in the world and drag it across the frame.
  This is a `LineRenderer` with `useWorldSpace = false`, recorded in the **camera's** local space (not
  `ViewmodelRoot`'s, which carries sway and bob and would make the ribbon swim while you run). There is
  still no `TrailRenderer` in this project and there should not be one.
- **A trail that only dims is worse than no trail.** The first pass tapered linearly over 18 points and
  dimmed in place: the ribbon left the screen edge at full width, and then hung in the air detached from
  the blade for five frames. Both are fixed, both are invariants now, and both are in the engineering log.

### 3.2 The anticipation eases the wrong way ✅ CLOSED

**Finding.** `AttackCo` lerps into the wind-up pose with `EaseOut` — fast at first, settling slowly into
the wind-up. Anticipation should do the opposite: **ease in**, starting slow and accelerating into the
strike, because that is what reads as storing energy.

**Shipped.** `WeaponViewmodel.EaseIn(t) = t²` on the anticipation leg only. The strike leg keeps
`EaseOut` — fast off the mark, which is what the guidance asks for — and the recovery keeps `EaseInOut`.

### 3.3 There is no follow-through hold ✅ CLOSED

**Finding.** `AttackCo` goes from `swingEnd` straight into the recovery lerp. There is no pause at the end
of the arc.

**Shipped.** `WeaponViewmodel.FollowThroughHold = 0.07 s` (~4 frames at 60 Hz), capped at **45% of the
recovery leg** and taken **out of** it, never added to the attack. Total attack duration is unchanged, so
`attackDuration`, `hitDelay`, `comboWindow` and the parry window calibrated against them are untouched:

| Weapon | `dur` / `hitDelay` | strike | hold | recovery (was) |
|---|---|---|---|---|
| Cerulean Edge | 0.38 / 0.12 | 0.076 | 0.070 | 0.114 (0.184) |
| Sunbreaker | 0.70 / 0.30 | 0.140 | 0.070 | 0.190 (0.260) |
| Rosethorn | 0.22 / 0.06 | 0.044 | 0.052 | 0.064 (0.116) |
| Oathbreaker | 0.30 / 0.08 | 0.060 | 0.070 | 0.090 (0.160) |

The dagger's hold is short because the 45% cap bites on a 0.116 s recovery. That is deliberate: a full
0.07 s hold there would leave 0.046 s of recovery and the settle would read as a snap.

**One correction to this audit.** "It costs nothing but a `WaitForSeconds`" is wrong twice over. The hold
runs on the same **scaled** `Time.deltaTime` as the rest of the swing — hitstop must still freeze the arm
mid-follow-through, that is the impact device — and the guard target has to be read **after** the hold,
not before it, or a guard pressed during the follow-through is silently dropped.

### 3.4 Hitstop is binary, not curved — OPEN, deliberately

**Finding.** Hitstop snaps the world to `hitStopScale` 0.02 and snaps back.

**Fix (candidate, not obviously correct).** Ramping the rate down, holding, and ramping back is smoother
than a binary freeze. **But** the binary version is currently doing a job — a hard freeze is a stronger
punctuation than a ramp, and this game's whole feel is built on a crisp deflect. Prototype it, compare,
and be prepared to keep what exists. Do not change this because a document said so.

### 3.5 Enemy anticipation shares one pose vocabulary ✅ CLOSED

**Finding.** Procedural enemies animate through `EnemyVisuals`, which played a generic wind-up. Distinct
attacks (`Grunt_Jab` vs `Grunt_Heavy`) were distinguished mostly by *timing* and the alert tell, not by
silhouette. The guidance is explicit that each anticipation must be drastically different.

**Shipped.** `EnemyAttackData.windupPose` (a `WindupPose`: `armWindup` / `armStrike`, `bodyOffset` /
`bodyEuler`, `weaponLag`), read by `EnemyVisuals.PoseFor`, with every value written in `DataFactory`
(rule 9). Eleven attacks across the Grunt, the Heavy and the Boss are authored; everything else keeps
the cone-derived fallback. Full map: [`DATAFLOW.md > Wind-up silhouettes`](DATAFLOW.md#enemy-ai).

**No timing moved.** Every `windup`, `impactDelay`, `strikeDuration`, `recovery` and `comboGap` is the
same number it was. A pose only changes what the body looks like for a span already calibrated against
`parryPerfectWindow` (0.13) and `cueLead` (~0.28 s).

**The correction this gap needed, and it is the whole story.** The schema and the mechanism were built
first, and eleven poses were authored against them with confident comments — "pure vertical", "pure
horizontal", "rears past vertical", "the exact mirror of hit A". **Photographed from the player's eye,
ten of the eleven made a different shape than the comment claimed**, and four of the boss's five were
the same shape as each other. The cause is in section 4 rule 8 below. What the audit above got wrong is
implied by that: this gap was never "add per-attack poses", it was "**measure** per-attack poses". The
mechanism was the easy half.

The shipped silhouettes, measured on screen (`tilt` 0 = level bar, ±90 = upright mast; `tip` in
body-heights from the body's centre):

| | before (measured) | after (measured) |
|---|---|---|
| `Grunt_Jab` (0.45 s opener) | level bar, chest, right — *kept* | tilt 10, tip (0.92, 0.20) |
| `Grunt_Slash` | tilt 87 — a **vertical**, i.e. the heavy's shape | tilt −45, tip (−0.40, 0.61) — 45° diagonal, left |
| `Grunt_Heavy` (0.72 s rhythm-breaker) | tilt −3, len **0.38** — a short horizontal stub at head height, the *smallest* shape in the moveset | tilt 88, tip (0.13, **1.16**) — a mast a body-height above the head |
| `Heavy_Step` | tilt −22, shallow | tilt −55, tip (−0.12, 0.02) — low bar, body advances |
| `Heavy_Overhead` ("vertical") | tilt **74**, len **0.51** — a steep diagonal, half a blade, turned away from the key light | tilt 88, tip (−0.04, 1.20) |
| `Heavy_Sweep` ("horizontal") | tilt **63** — the *same* steep diagonal, in the same place | tilt 0, tip (0.91, 0.10) |
| `Boss_Slash` | tilt 76 | tilt −60, tip (−0.39, 0.87) |
| `Boss_DoubleSlash_A` | tilt 72 | tilt 30, tip (0.73, 0.36) |
| `Boss_DoubleSlash_B` ("exact mirror of A") | tilt **71** — the same lean as A, not the mirror | tilt −30, tip (−0.39, 0.36) |
| `Boss_Slam` ("tallest in the game") | tilt 63, len **0.54** | tilt 88, tip (0.12, 1.17) |
| `Boss_Thrust` (**unblockable**) | tilt −7, len 0.40, tip (−0.10, −0.17) | **unchanged** |

**Verdict, honestly, pair by pair — the side-by-side test.**

- **`Heavy_Overhead` vs `Heavy_Sweep` — fixed, and it was the worst case in the game.** The two attacks
  with *opposite correct answers* (vertical vs horizontal) shipped as the same steep diagonal 11° apart
  in the same place. They are now 88° and 0°: a mast and a level bar. Told apart at three frames.
- **`Grunt_Jab` vs `Grunt_Heavy` — fixed.** A small bar at chest height against a mast a body-height
  over the head. The heavy is now the biggest shape in the moveset, which is what a 0.72 s
  rhythm-breaker with the best deflect value should be; it used to be the smallest.
- **`Boss_Thrust` vs everything — holds, and it is the one pose that needed no work.** It is the only
  boss wind-up below the body's centre line, the only flat one, and the only short one (0.40 body-heights
  against 0.80–0.90) because the blade is aimed down the camera. Weakest channel is against
  `Boss_Slash`, where the separation is **height** (0.56 body-heights) rather than angle (29°) — thin,
  and reported as such by the test. It survives because the pose is the *second* read: the red cue tint
  and the pink `M_AlertTell` marker (peak 3.0) are the first, and the pose adds to them.
- **`Boss_DoubleSlash_A` vs `B` — a deliberate mirror, not a distinction.** They always play in that
  order, so confusing them costs nothing.
- **`Boss_Slash` vs `Boss_DoubleSlash_B` — the weakest surviving pair.** Both lean left; they are
  separated by height and steepness, not by direction. Kept because B never opens a combo (A does), so
  the player has already been told which phrase they are in.
- **`Heavy_Step` is the quietest pose in the set and is meant to be.** Its read is the body closing, not
  the arm. It is the one I would look at first if a playtest says the Heavy is hard to read.

**Nothing here proves the game is fair.** These are photographs and measurements of static peak frames,
plus a regression test that re-measures them. Whether 0.72 s is enough to *act* on a mast is a human
playtest question, and it is still open.

### 3.6 The embers barely register ✅ CLOSED, as far as the caps allow

**Finding, already reported by the agent that built it.** The Pyre fire's individual embers are ~12 mm and
read as texture on the blade, not as sparks — the colour ramp and the light do ~90% of the work.

**Shipped.** Shape over quantity, in `WeaponEmber`. The caps that keep the fire out of the frame did
**not** move: `glowChargeAtFull` 0.55 and the light at 1.5 @ 2.4 m are unchanged.

| | Was | Now |
|---|---|---|
| Count | 14 | **10** |
| Size | 0.012 m cube | **0.026 m cross-section, stretched 2.6× along its own velocity** → a ~12 × 68 mm streak |
| Rate at full | 26 /s | **12 /s** |
| Life | 0.55 s | **0.34 s** |
| Rise / drift | 0.55 / 0.16 | **1.1 / 0.6**, with a ×0.6–1.4 per-spark speed spread |
| Material | the blade's URP/Lit, base black + emission | **its own additive URP/Unlit**, peak channel 1.45 |
| Brightness ramp | `(1−k)²` | `(1−k)^0.45` — hot for almost all of its life, then gone |

**Verdict, honestly.** The sparks are now *discrete*: a handful of orange streaks leaving the blade, which
is what "spark" means and what the 12 mm motes never were. They are still a modest effect, and that is
where the caps leave it — the only knobs that would make the fire louder are the glow charge and the
point light, and both exist to stop it eating the frame. Closed as far as **shape** can take it.

**A correction to this audit, and a trap the fix created.** Making the ember bigger exposed what the
12 mm version had been hiding: it was a **lit black box**, invisible at 12 mm and a **dark sliver** at
68 mm. Borrowing the blade's material was the wrong call for a spark; it now owns an additive one, and
additive can only ever *add* light, so a dim spark is faint rather than a hole in the frame.

---

## 4. Standing rules for this project's effects

1. **Effects run on unscaled time.** They must keep moving while hitstop holds the world, or the pause
   reads as a stall. `SlashFx` already does this — match it.
2. **The viewmodel swing runs on scaled time.** Freezing the arm mid-swing *is* the impact.
3. **Never buy impact with `ScreenFlash`.** 0.55 alpha plus chromatic aberration shredded the frame once
   and washed out the very thing it was meant to highlight. Flash is 0.20 and chroma 0.4 for that reason.
   Get impact from the beam, the flare, the sparks and the light they cast.
4. **A line under ~0.05 m is sub-pixel at combat distance.** `SlashFx.Ring` draws a 0.045 m hoop, which
   was invisible at 7.5 m and had to be rebuilt as a radial spoke fan. Check the size at the distance it
   will actually be seen.
5. **A trail is a mechanical readout, not decoration.** The swing ribbon is open for the strike leg and
   nothing else, so it tells the player when the weapon is dangerous. Widening that window to make the
   effect prettier makes the game lie.
6. **An authored angle is not an on-screen angle.** A wind-up pose authors a SHOULDER rotation; the
   hand trails it by `weaponLag` and the body is rotated underneath both, so the blade lands tens of
   degrees from where the arithmetic says. Photograph the pose from the player's eye at fighting
   distance, or you are tuning a number nobody sees.
7. **An effect that is bigger is not the same effect scaled.** A shape too small to see is also too small
   to be visibly *wrong*; growing it exposes whatever its material was doing all along. The embers turned
   out to be lit black boxes.
8. **Timings are data.** Anything that changes how long an attack takes belongs in `AttackData` /
   `WeaponData`, never in an animation curve. The parry window is tuned against those numbers.
9. **One exception to the bloom cap, and it is the tell, not the enemy.** The enemy bolt
   (`Projectile.HotCore`, peak 1.6) is the only traversal effect over the 1.05 threshold: a shot you have
   to deflect at 32 m/s while running must be the brightest thing on the span. The rule survives because
   the SHOOTER still never glows until deflected, `SlashFx` still normalises everything it makes, and the
   exception is a named constant with a test (`ProjectileTests.TheBoltIsTheOneGlowInTraversal`), not a
   widened cap. Anything else that wants to glow argues against this rule, not around it.

---

## Sources

- [Keys to Combat Design: Anatomy of an Attack — GDKeys](https://gdkeys.com/keys-to-combat-design-1-anatomy-of-an-attack/)
- [A More Realistic HitStop — Ahmad Mohammadnejad](https://www.ahmadmohammadnejad.com/sandbox/a-more-realistic-hitstop)
- [Pixelblog 9: Melee Attacks — SLYNYRD](https://www.slynyrd.com/blog/2018/9/8/pixelblog-9-melee-attacks)
- [Juicing up your game attacks — GDQuest](https://www.gdquest.com/library/juicy_attack/)
- [How do 3rd person melee combat games communicate GAME and HIT feel? — Jason de Heras](https://www.jasondeheras.com/gamedesign/2021/4/23/how-do-3rd-person-melee-combat-games-communicate-game-and-hit-feel)
- [Creating Stylized VFX in Unity — 80.lv](https://80.lv/articles/creating-stylized-vfx-in-unity)
- [From Realism to Stylization: Game VFX Production — 80.lv](https://80.lv/articles/from-realism-to-stylization-game-vfx-production)
- [Sword & Melee Weapon Animation guide — Mocap Online](https://mocaponline.com/blogs/mocap-news/sword-melee-animation-guide)
