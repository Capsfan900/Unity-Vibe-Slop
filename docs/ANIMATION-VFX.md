# Animation and VFX — principles, audit, and what to fix

What the craft literature says, what this project actually does, and the gap between them. Written from a
code audit against published guidance, not from taste.

Related: [ARCHITECTURE.md](ARCHITECTURE.md) (feel contracts, art direction) ·
[ENGINEERING-LOG.md](ENGINEERING-LOG.md) (the traps already paid for) · [DATAFLOW.md](DATAFLOW.md)

---

## 1. The principles worth holding to

### Solar arena presentation (2026-09-07)

The four campaign boss courts have rotating portal suns with cyan, gold, azure and green themes.
`SolarArena.shader` supplies moving plasma currents; `SolarArenaVisual` counter-rotates the exterior
layers. The matching enclosed realms keep their floor and walls stationary. Only the ceiling's
visual disc rotates, around its vertical axis, so it cannot tilt into the fight.

These requested suns are a deliberate scenery-glow exception. Preserve coloured bands and dark
eye-level backgrounds for enemy tells; judge the generated result through the actual player camera
with post-processing enabled. Additive front/back layers can wash a coloured material into white.
Regenerate through `MaterialFactory.CreateSolarArenaMaterials()` and the level builder after tuning.
The ceiling's 0.20 opacity override lives on `SolarArenaVisual` and reapplies on enable. A property
block written only by the editor generator is transient and disappears when the scene reloads.
The cloud sea remains below the route and uses its separate material and animation.

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
| Verdigris | 0.70 / 0.30 | 0.140 | 0.070 | 0.190 (0.260) |
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

### 3.7 The sentry detonation and flare grapple ✅ first pass, 2026-09-06

**Finding, this audit.** Two brand-new effects, untested by a human: the sentry detonation
(`SentryBurst.Detonate`) and the flare-grapple toss (`FlareGrapple.HandlePullEnded`). Both are the
payoff for two clean deflects and need to read at range and sell a mechanic, not just decorate one.

- **The detonation under-read at distance.** It fired a `SlashFx.Flare` sized 1.6 m (the same "highlight,
  not an explosion" primitive and a similar size to a routine parry spark) plus 18 sparks — nothing with
  a footprint bigger than the sparks' own travel, so a body dying 20+ m down a span had no cue that
  scaled with the distance. **Fixed**: added a `SlashFx.Ring` shockwave (`BurstRingRadius` 2.6 m,
  `BurstRingSeconds` 0.4 s, longer than the flash) and grew the flare to 2.4 m / 0.3 s. Colour
  (`SentryBurst.BurstHue`) stays violet-leaning — the same family as `SentryFlare.Core` — never the
  bolt's amber, so the burst never lies about which kind of event it is (reward vs threat). All three
  calls still go through `SlashFx`'s pooled, 1.0-normalised materials — this is size and shape doing the
  work of "legible at range," not a bloom exception; the bolt and the standing flare glow keep that
  privilege, the momentary flash does not need it.
- **The toss had no visual anchored to the player.** `Consume()` puts sparks at the flare's own (already
  spent) position; the only thing that happened to the player's view was the ordinary `FovKick` — the
  same channel every other kick uses, so a toss read identically to a normal dash punch. **Fixed**: an
  expanding `SlashFx.Ring` at the player's feet at the instant of launch ("thrown FROM here," mirroring
  the pull line's "pulled TO there"), and a `CameraFX.ChromaticPulse` (`tossChroma` 0.16, 0.25 s) as a
  separate g-force channel from the FovKick — the one moment the motor hands the player vertical speed
  they did not ask for with WASD deserves its own cue, not a copy of an existing one.
- **The flare-vs-bolt hue split is hue-only, and the halo is budgeted into it.** `SentryFlare.Core`
  (1.18, 0.88, 1.6 — violet-white) against `Projectile.HotCore` (1.6, 0.95, 0.38 — amber): both peak at
  the same **1.6**, so brightness never carries the distinction, only hue does — bone/ember for "answer
  this," violet for "use this." The flare's constant `sin(t·23)` flicker against the bolt's flat
  glow-until-cue is the same split read as motion: alive and waiting (flare) vs steady until it demands
  you act (bolt). The halo is an **additive sphere drawn over the core**, so the two peaks *sum in the
  overlap*: `SentryFlare.Halo` is a named constant at peak **0.4**, and 1.6 + 0.4 lands exactly on the
  2.0 ACES ceiling (`FlareTests.TheCoreAndHaloTogetherStayUnderTheAcesCeiling`). The core was dropped
  from 1.9 to 1.6 rather than the halo dropped to 0.1: the halo is what makes the flare findable at
  30 m, and a 0.1 aura is not visible at all — the core loses nothing at 1.6 because it is already the
  brightest violet on the span.
- **The halo is driven in WORLD space.** It is a child of the sphere `Update` rescales, so a local scale
  would compound the core's — squaring both the glow curve and the `sin(t·23)` flicker, and collapsing
  the "never a pinprick" aura to a third of its intended size at low glow. `HaloWorldDiameter(glow,
  flicker)` is the single source of truth and the parent's scale is divided out each frame
  (`FlareTests.TheHaloIsDrivenInWorldSpaceAndNeverCollapses`).
- **The idle pulse is budgeted against the shared `SlashFx` pool.** A flare re-fires a soft
  `SlashFx.Flare` every `PulseInterval` 0.55 s for its whole life; `SlashFx` pools 28 live effects and
  `Spawn` returns null at the cap, so a span full of flares could push a *combat tell* out of the pool.
  `SentryFlare.PulseAllowed(liveCount)` gates it at `PulseLiveBudget` 2 — past that, the standing
  1.15 m core inside its 2.6 m halo is the read (`FlareTests.TheIdlePulseRespectsTheSharedSlashFxBudget`).

**Still open, needs a human playtest, not more code.** Whether 2.4 m / 2.6 m actually reads at 25 m on a
real monitor is a claim this pass could only reason about, not measure — there is no in-editor way to
screenshot the span from spawn distance without driving the editor, which is out of this agent's scope.
Tests below pin the *numbers*, not the *look*.

### 3.8 The world went COLD, and the tells did not ✅ 2026-09-06

**The ask, verbatim.** *"make the vfx team change the main color scheme to blue. and a bunch of planets in
the background that glow and have rings like saturn. not too many just enough to spice up the scene."*
An art-direction change the user authorised explicitly, so it is not an invention — but the readability
contract underneath it is not negotiable and the pass is built around protecting it.

**The one decision that decides whether a palette flip succeeds.** Complementary contrast — warm on cool —
is the highest-contrast pair on the wheel and the standard device for making a gameplay object read
against a world ([Pixune](https://pixune.com/blog/color-theory-in-game-art-basics-and-complementary/),
[nastyrodent](https://nastyrodent.com/color-theory-for-game-art/)). Turning the tells blue along with the
world would have thrown that away to satisfy a colour request. So the rule this pass establishes is:

> **In this game, WARM means exactly two things and nothing else: a combat tell, or fire. Cold is the world.**

| Went cold | Stayed warm, deliberately |
|---|---|
| fog / ambient sky / equator / ground / the key light | the enemy bolt (`Projectile.HotCore`, amber 1.6) |
| every structural albedo and three of four trims | the alert tell (`M_AlertTell`, pink-red 3.00) |
| the whole sky: dome, nebulae, stars, silhouette | the parry cue spark (`EnemyVisuals.CueSpark`, bone) |
| **the eclipse corona rim** (`#FFD9A8` → `#C9E2FB`) | the enemy eye (`M_EnemyEye`, under the bloom cap) |
| the slide's grit dust (`SlideFx.gritHue`) | **fire = safety**: `M_Torch`, `M_Checkpoint` — the Dark Souls / Elden Ring bonfire read |

Two effects moved *against* the world rather than with it, and both are the point of the pass:

- **`EnemyVisuals.ParryGlow` went WARM**, `(0.78, 0.88, 1.0)` → `(1.0, 0.92, 0.80)`, peak unmoved at 3.2.
  A blue-white flash on a blue world reads as "the frame got brighter for a frame"; a bone-white one with
  a warm edge reads as **steel struck steel**, which is what a deflect is. Same peak, same duration,
  opposite temperature. This is the single loudest moment in the game and it now owns a colour the world
  cannot imitate.
- **`SentryFlare.Core` went more MAGENTA**, `(1.18, 0.88, 1.6)` → `(1.30, 0.78, 1.6)`, peak unmoved at 1.6
  and the halo's 0.4 untouched, so the 2.0 ACES ceiling is exactly where it was. It stays violet rather
  than going cold, because its blue channel is now the *world's* dominant channel — the red lift is what
  restores the gap. The three-way language survives: **amber = answer this, violet = use this, cold = the
  world, which says nothing.**

**Hue changed; light level did not.** Every environment colour was fitted to the Rec.709 **linear
luminance** of the blood-red value it replaced (equator .2045, sky .1348, ground .0110, key .1896;
structural albedos .0157 / .0334 / .0821 / .0130). That is how a palette flip avoids silently re-tuning
enemy readability: the equator term is the only light on a backlit torso, and it is exactly as bright as
it was. Pinned by `SkyEclipseTests.TheColdPassChangedHueAndNotLightLevel`.

**The trim set had to move, and the reason is a lie the old palette was telling.** Tile 3 shipped as
EMBER `#C4400F` — the enemy bolt's own hue. A static level trim sharing a hue family with the one thing
you must deflect at 32 m/s is a readability tax paid on every span. Tile 4 was crimson, which against a
BLUE world becomes the loudest thing on a wall and reads as the alert tell at a glance. Both are gone:
gold ~52° / ghost green ~142° / ice cyan ~186° / azure ~222°. Weakest pair is cyan vs azure at ~36°,
separated by luminance and saturation, and reported as such rather than hidden.

**The planets are scenery and are constructed so they can never be anything else.** Four of them, two
ringed, baked into the *same mesh and the same two materials* as the rest of the sky — no extra draw call,
which matters because WebGL is a shipped target. "Glow" is a soft alpha gradient plus a lit-to-shadow
terminator across the body, never an emissive: every vertex is capped at `Starfield.PlanetPeakCeiling`
0.55 and lives in submesh 0, whose material tint is exactly 1.0, so a planet **physically cannot** reach
the 1.05 bloom threshold, let alone the corona's 1.35 or the bolt's 1.6. All four sit at pitch ≥ 40° and
> 44° of arc from the eclipse centre (outside its halo), so none can ever sit behind a bolt the player is
trying to read.

**The rings sort themselves with no depth buffer.** The sky writes no depth, so overlap is resolved purely
by index order — which is deterministic where alpha sorting is not. Each planet is emitted in five passes:
halo → the ring's FAR half → the body → the ring's NEAR half. The far half is the arc above the centre
line, which is what passes behind the planet from a viewer at the dome's centre; the opaque body paints
over it, and the near half then crosses the lit limb. That is the entire Saturn read, bought with ordering.

**What this pass could not check, stated plainly.** Nobody looked at the screen. Every colour here is
arithmetic — luminance parity, channel ratios, angular separations — and the claims that a cold slate wall
still reads as a wall, that the ice-cyan and azure trims separate at 30 m, and that four planets is
"enough to spice up the scene" rather than too many are **reasoned, not seen**. They need one human
playtest.

### 3.9 The Sentry is a cartoon ghost, and the flare is bigger by giving up brightness ✅ 2026-09-06

**The ask, verbatim.** *"redesign the looks of the parkour enemy1 make it like a blush ghost that looks
like a cartoon ghost and is floating and glowing with wispy mist around it. Also take a pass on the flare
and make it bigger and look more polished."*

**Blush or bluish — the call, and why.** Read as **bluish**. A pink body would have put an *enemy* in the
tells' colour family on the one enemy read at 25 m down a span while an amber bolt crosses it, which
section 4 rule 10 forbids for exactly this reason. The ghost is a pale **cold blue-white**
(`M_SentryGhost` albedo `#A9C2DA`, 0.855 peak) and it separates from the world by **VALUE, not hue**:
every structural albedo in the game sits at 0.013–0.082, so the ghost is ~50x brighter than the wall
behind it. Value survives distance, fog and peripheral vision in a way a hue shift does not. It is also
deliberately **not violet** — violet means "use this" and belongs to the flare; the Sentry used to wear
`#5A2BD0` for no reason, and that has been retired on both sentries.

**The construction.** `PrefabFactory.BuildGhostBody` (pshooter_enemy01 only; `BuildPillBody` is unchanged
and still builds every other enemy):

| Part | What | Why |
|---|---|---|
| Shell | capsule, 1.06 x 1.24 m, centred 1.30 | a rounded dome, legible as a three-frame shape at 25 m |
| Hem | **five** capsule tatters, lengths 0.42–0.56, on a 0.33 ring, each on its own pivot | unequal lengths read as torn cloth; equal ones read as a skirt. Waves ±9° at 0.62 Hz |
| Face | two oval sockets half-sunk at the surface radius, `M_EnemyEye` (ember, under the cap), plus a dark mouth | the sockets are the FACING read at range; the mouth is close-range detail that goes sub-pixel past ~10 m and carries no information |
| Arms | two rounded nubs on the **identical pivots** (0.5, 1.45, 0), **no blade** | every authored wind-up pose lands exactly where it did. A ghost holding a 1.35 m cube sword was what made the old body read as a placeholder |
| Float | `FloatRoot` under `LungeRoot`: ±0.07 m at 0.42 Hz, a 0.018 m second harmonic, ±0.03 sway, ±2.5° roll | **the root, the capsule, the agent and the deathblow height (1.45) never move.** The hem hangs to y=0.24, so the silhouette still covers the collider — it does not float above its own hitbox |
| Mist | **four** additive spheres, one shared material, counter-rotating 7.5 s orbits, breathing in SIZE | see below |

**"Glowing" versus "an enemy never glows" — the reconciliation.** The ghost is **luminous without
blooming**. See section 4 rule 9, amended above: 0.35 lit + 0.28 floor + 4 x 0.10 mist = **1.03** against
the 1.05 threshold, so it *cannot* bloom by construction. The floor goes through `SetAura`, which means it
is modulated by `chargeDark` — the ghost dims as it winds up and returns as it strikes, a truthful extra
tell that came free with using the sanctioned channel.

**What the mist costs, exactly.** No particle system, no `TrailRenderer`, no per-frame allocation, and no
property block at all: it breathes in **size**, because on a dim additive blob a size change is visible
and a brightness change is not. Four transform writes per ghost per frame plus one shared additive
material (SRP-batched). It reaches 0.85 m from the centre line, so at 3 m it is an atmosphere rather than
a screen wipe. **And it structurally cannot hide the bolt**: additive light only ever ADDS, so an amber
bolt seen through the haze is still an amber bolt. An alpha mist could have dimmed the thing the player
must parry.

**The flare, bigger by 22% and 38% — and dimmer.** Core 1.15 → **1.40 m**, aura 2.6 → **3.6 m**, peak
1.6 → **1.45**. Four things make it read as finished rather than merely larger, and one candidate was
rejected as a lie:

- **A two-step falloff.** One additive sphere has a hard, obviously spherical edge. Two concentric shells
  (`Halo` 2.30 m @ 0.24, `Outer` 3.60 m @ 0.12) give the aura a step, which is what reads as a soft bloom
  and as a rim against the sky.
- **Two rates on one object.** The outer shell breathes at ~0.5 Hz against the core's 3.7 Hz flicker: a
  hot centre that trembles inside an aura that swells. **A rotation was rejected** — an untextured
  additive sphere spinning looks exactly like an untextured additive sphere not spinning.
- **An arrival and a death.** `SpawnPop` snaps from 0.18x through a ~6% overshoot to 1 over 0.18 s
  (anticipation, snap, settle); `DeathContract` collapses the last 0.35 s, which is **after** the flare
  stops being grappleable, so the shape never eats the window. A natural expiry stays **silent** —
  nothing happened, so nothing announces itself. `Consume()` — a real event — now throws a ring as well
  as sparks, the same three-part shape `SentryBurst` uses.
- **The trail stopped lying.** It was two points: head, and head minus the *current velocity*, i.e. a
  straight tangent drawn along a parabolic path, pointing at a place the flare had never been. It is now
  a six-point recorded history in a ring buffer (allocation-free) that curves with the arc. Pinned by
  `FlareTests.TheTrailIsARecordedArcNotAStraightTangent`, which measures the 0.20 m the tangent missed by.

**What this pass could not check, stated plainly.** Nobody looked at the screen. That a pale ghost reads
as a ghost rather than as a white pill, that four wisps at 0.10 are visible at all rather than invisible,
that the hem waves rather than wobbles, and that a 3.6 m aura at 1.45 still reads at 30 m — all
**reasoned from arithmetic, not seen**. The budget is the honest part; the look needs one human playtest.

### 3.10 The melee hit confirm was the loudest thing in the game, and the weapon had no body ✅ 2026-09-06

**Finding, this audit (the weapon-look lane of the weapon rework).** Three faults, all of them in the one
effect the player sees more than any other.

- **The hit confirm skipped `SlashFx` entirely and out-shouted every alarm.** `WeaponController.SpawnSpark`
  built a fresh `GameObject.CreatePrimitive(Sphere)` per hit — unpooled, a collider created and destroyed,
  a new `MaterialPropertyBlock` and a new component every time — and drew it at `WeaponData.neon * 4`.
  The hammer's hue peaks at 0.878, so **a routine chip hit rendered at 3.51**: above the unblockable alert
  tell (3.00), above the deathblow mark (2.60), and above `EnemyVisuals.ParryGlow` (3.20), the deflect body
  flash that is supposed to be the loudest moment in the game. Every other impact in the project goes
  through `SlashFx`, which normalises to exactly 1.0 — including the deflect's *own* flare and sparks — so
  the single effect that bypassed it was also the brightest, and the loudness hierarchy carried no
  information. It broke all three of `SlashFx`'s stated rules in one object: it read through BRIGHTNESS,
  it EXPANDED as a blob (0.35 → 0.85 m round sphere), and it carried no DIRECTION.
  **Shipped:** `Assets/Scripts/Feel/WeaponImpactFx.cs`. Flare + a spark fan thrown back out of the wound
  (the same rule `PlayerCombat.SparkAt` uses for a blow landing on you) + a shockwave ring **for the maul
  alone** — Sekiro's own split is the precedent, sparks for a block, sparks *and* a shockwave for the big
  one. Peak **1.0**, by construction rather than by constant. A hit you must REACT to blooms; a hit you
  LANDED does not.
- **Nothing about the impact knew which weapon threw it.** One 0.35 m sphere for a 0.22 s needle and a
  0.86 s maul. Every size, count, speed and spread now comes off `WeaponImpactFx.Mass(w)` =
  `InverseLerp(0.22, 0.86, attackDuration)` — derived from the shipped ladder, **not** a new data field,
  so a retuned weapon retunes its impact and the two can never disagree. The debris deliberately gets
  SLOWER as the weapon gets heavier (9 → 6 m/s): `SlashFx` pulls sparks at −16 m/s², so slow debris falls
  in a visibly heavier arc. Do not "fix" that inversion.
- **The swing ribbon was the same ribbon for every weapon.** `headWidth` 0.030 m and `fadeSeconds` 0.11 are
  serialised once by `PrefabFactory` and `WeaponTrail` never knew what was swinging — so the one channel
  guaranteed to be on screen at the moment of contact said nothing about the weapon in the hand. The mass
  curve now multiplies the authored values (rule 9: the numbers stay in the factory) and is fitted so the
  **sword lands within 0.5% of what shipped**: dagger 0.018 m / 0.088 s, sword 0.030 / 0.110, hammer
  0.053 / 0.152. The dagger's fade is the truthfulness fix in that row — its whole post-strike leg is
  0.116 s, so a flat 0.11 s ribbon was still on screen as the next flick began.
- **The weapon's MASS was darker than the glove holding it.** `M_WeaponCore` — guard, quillons, grip,
  pommel, the maul's head, cheeks and spike, i.e. exactly the parts that make an archetype read in
  silhouette — shipped at `#08070C`, **0.0025 linear luminance**. That is the last survivor of the mistake
  this document already records twice (`M_Ground` "darker than any real material", `M_Enemy` "a flat black
  cutout"), and it had two consequences: the mass parts were darker than the 0.013–0.082 world they are
  silhouetted against, so the weapon read as its emissive segments floating with no object holding them;
  and it sat **3.6x below `M_Gauntlet`** (0.0091), inverting the hierarchy `MaterialFactory` states in its
  own comments ("the hands sit at the bottom … below the weapon"). Now a cold gunmetal `#2B313C` at
  0.0313 — 3.4x the glove, just under `M_Stone` — with smoothness 0.45, because on a backlit course a
  specular highlight is the only channel a non-emissive surface has to say "metal, and curved". Nothing
  emits: the hot channel on a weapon belongs to the blade and the Pyre embers.

**What this pass could not check, stated plainly.** Nobody looked at the screen. That 0.42 m of flare
reads as a maul hit at 2.5 m, that an 18 mm dagger ribbon is still visible, and that a 0.031-albedo hilt
separates from a 0.016-albedo wall are **reasoned from arithmetic, not seen**. Pinned by
`Assets/Editor/Tests/WeaponImpactVfxTests.cs` (11 assertions across peak, ladder, shockwave gating,
ribbon weight and the albedo hierarchy).

**RESOLVED 2026-09-07 — the maul no longer wears the bolt's colour.** This lane's first open item was
that `Hammer.neon` (then `#E0661A`) sat ~5° of hue from `Projectile.HotCore`, so a maul swing painted the
screen the colour the player is trained to read as "deflect this". It is now `#A8D12E` (hue ~75°, 47°
clear), in `DataFactory` **and** in the paired `Energise` tint at `PrefabFactory.cs:266`, and
`HitReadTests` holds the floor. The first attempt kept the name and the colour read olive; the weapon was
renamed **Verdigris** instead, which is what makes a patina green on a brass maul head look deliberate.
There is no warm hue that clears the bolt — do not move this back without moving the bolt first.

**Still open in this lane, and it needs another owner's file:**

1. **Every non-blade part of every weapon shares ONE material.** Head, guard, grip and pommel are all
   `M_WeaponCore`, so there is no value break between the steel and the leather — the head reads as an
   extension of the haft. `MaterialFactory` can add the key; only `PrefabFactory` can assign it.

---

### 3.11 A perfect parry announced itself in TEXT ✅ 2026-09-07

**The ask, verbatim.** *"the perfect parry needs to have a minimal shockwave visual to know it was
performed, over the words."*

**Finding.** The world's vocabulary for a Perfect (sparks + a 0.85 m crescent + a 0.22x flash) was the
same vocabulary a scraped block used, only more of it. The thing that actually said PERFECT was the teal
word in `HUDController.OnParry` — and reading a word costs a beat the player does not have at 32 m/s.

**Shipped.** One `SlashFx.Ring` fired from `ParryImpact.Shockwave`, all numbers in `ParryImpulse`:
0.34 m radius (**30% of screen height** at 1.05 m and the shipped 95° vFOV), 0.18 s, `#A8E6DA` — the
same teal as the word it replaces. Normal is the level direction to the attacker, so it is a wave-front
seen face-on; origin is the contact point the sparks and the crescent already use, pushed 0.12 m down
the line.

- **A closed hoop is the message.** Blocked, Guard and Hit are all spark fans; only a Perfect throws a
  ring, so presence alone distinguishes it.
- **It is the SMALLEST wave in the game**, deliberately: under the maul's 1.10 m landed-hit shockwave
  and inside the 0.85 m crescent it completes. It is a confirmation, not a payoff.
- **It does not bloom.** Peak 0.98 through `SlashFx`'s 1.0 normalisation. The licensed bright moment of
  a deflect is `EnemyVisuals.ParryGlow` at 3.2 on the ENEMY, and it only means "you deflected" because
  nothing player-side competes with it.
- **It cannot lie.** `ParryImpact.Deflect` is called only from `ParryController.NotifyDeflected`, only
  from the `ParryResult.Perfect` branch.

**Not seen, only reasoned.** Nobody looked at the screen. Pinned by 5 assertions in `ParryImpactTests`.
**One thing to look for in the playtest:** teal at 168° sits ~20° from two shipped level trims (ghost
green ~142°, ice cyan ~186°). They are static 0.013–0.082 albedo surfaces and this is a moving
additive hoop for 0.18 s, so confusion is not expected — but that is reasoned, not seen.

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
9. **Exactly two exceptions to the bloom cap, both tells, neither an enemy.** Two traversal effects sit
   over the 1.05 threshold, and both peak at **1.6**:
   - the enemy bolt, `Projectile.HotCore` (1.6, 0.95, 0.38 — amber) — a shot you must deflect at 32 m/s
     while running has to be the brightest threat on the span. Pinned by
     `ProjectileTests.TheBoltIsTheOneGlowInTraversal`.
   - the sentry flare, `SentryFlare.Core` — **now 1.45, deliberately BELOW the bolt** (2026-09-06 polish
     pass) — plus its two additive shells, `Halo` (0.24) and `Outer` (0.12). All three overlap, so all
     three are budgeted together: 1.45 + 0.24 + 0.12 = **1.81**, under the 2.0 ACES ceiling.
     Pinned by `FlareTests.TheFlareIsATellSoItMayBloom` and
     `FlareTests.TheCoreAndHaloTogetherStayUnderTheAcesCeiling`.

   **The hierarchy between those two changed, and it is now one-directional: the BOLT is the brightest
   thing on a span (1.6) and the FLARE is the biggest (3.6 m of aura).** They used to sit at the same
   1.6 on the argument that hue alone should carry flare-vs-bolt. Growing the flare while holding that
   peak would have let a grapple point out-read the one object that can kill the player, so the peak came
   down to pay for the size. **Brightness says "answer this now"; area says "come and find this."**

   Equal peaks are the point: brightness says "this matters," **hue** says which — amber for "answer
   this," violet for "use this," and since the 2026-09-06 cold pass **cold blue says nothing at all,
   because cold blue is the world**.

   **Amended 2026-09-06 (the ghost pass).** The old wording was "an enemy body never glows until it is
   deflected." That ban is now a **budget**, because the user asked for a glowing ghost and the art call
   wins: an enemy body may be **LUMINOUS but never BLOOM**. `SentryGhostVisual` states the whole thing as
   arithmetic — lit albedo 0.35 + emission floor 0.28 + all four mist wisps 4 x 0.10 = **1.03**, under the
   1.05 threshold — and it goes through `EnemyVisuals.SetAura`, the one sanctioned door into that channel,
   so it is still modulated by `chargeDark` and the ghost visibly *inhales* its own light on a wind-up.
   What the old rule was protecting is untouched: the deflect spike is 3.2, eleven times the floor and the
   opposite temperature, so light that BLOOMS on a body still means "you deflected". Pinned by
   `GhostTests.TheGhostIsLuminousAndCannotBloom`.

   The rule survives because nothing on an enemy may cross 1.05,
   `SlashFx` still normalises everything it makes to 1.0, and each exception is a named constant with a
   test rather than a widened cap. Anything else that wants to glow argues against this rule, not around
   it — and anything additive stacked on an exception must be budgeted into the same 2.0 ceiling.

10. **Warm is reserved. The world is cold.** (2026-09-06.) Warm light in this game means a combat tell or
    it means fire, which means safety — and nothing else. The backdrop is not allowed to speak either
    language, which is why the eclipse corona went cold with the sky rather than staying a huge static
    warm bloom that the amber bolt has to fly across. A new effect that wants to be warm is claiming to
    be combat information; if it is not, it is cold.

11. **Nothing in the sky may compete with a tell, and the guarantee should be structural, not a number.**
    The sky's entire LDR field sits on a material tinted exactly 1.0 and vertex colours clamp at 1.0, so
    the planets, the rings, the stars and the whole dome *cannot* cross the bloom threshold no matter how
    they are tuned. `Starfield.PlanetPeakCeiling` (0.55) states the intent on top of that; the submesh is
    what enforces it. Prefer a construction that makes a mistake impossible over a constant that a later
    pass can quietly raise.

12. **A torch is the FAR instrument; trim is the NEAR one. Never swap their jobs.** (2026-09-06, the route
    beacon ruling.) At 20-40 m a route marker is only readable because it *blooms* — a sub-degree emitter
    under the 1.05 threshold shrinks to a dim pixel and vanishes. The torch ember (`M_Torch`, 1.15) is a
    licensed bloom exception (fire = safety), so it is the only navigational thing that survives distance;
    `FlickerLight` culls the LIGHT at 42 m but never the ember mesh, so a distant beacon torch costs one
    distance check. Trim is the opposite job: it is read at 2-8 m, it says "this is the edge you are about
    to leave", and all four keys sit under 1.05 by rule 9, so a trim can never do the distant job by being
    raised — raising it just makes a static edge speak the tell language. Spend torches for "where next",
    trim for "where the edge is", and if a beacon under-reads at distance spend SIZE or COUNT, never
    intensity: the bolt (`Projectile.HotCorePeak` 1.6) must stay at least 1.35x any torch, because both are
    warm and only one of them can kill you. **The budget that binds is local overlap, not the total**: URP's
    per-object limit is 4 on both shipped RP assets, and the 5th light reaching a surface is dropped by the
    renderer after being paid for in culling and in the flicker Update — and *which* one is dropped changes
    as the camera moves, so an over-budget cluster reads as a torch popping on and off as you run past.
    Pinned by `Assets/Editor/Tests/TorchDensityTests.cs` (Level_01 peaks at 3 of 4).

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
- [Understanding Color Theory in Game Art — Pixune](https://pixune.com/blog/color-theory-in-game-art-basics-and-complementary/)
- [Color Theory for Game Art: The Production Application Guide — Nasty Rodent](https://nastyrodent.com/color-theory-for-game-art/)
- [Designing for Difficulty: Readability in ARPGs — Game Developer](https://www.gamedeveloper.com/game-platforms/designing-for-difficulty-readability-in-arpgs)

## Fog is the sky bleeding in, never a hole punched in it (A5, 2026-09-06)

`RenderSettings.fogColor` must sit between the dome's zenith (`#060A17`, lin lum .0032) and its horizon
band (`#13233F`, .0170), and **above a shadowed stone face** (~.009 linear), so distance *lightens* dark
surfaces instead of eating them. Shipped: `#0E1C34`, 36 -> 140 m. Two traps. `fogStartDistance`'s floor is
the **eclipse halo's corners at 31.2 m**, not the quoted 25 m dome radius — the halo is a flat disc of
lateral radius 19.8 m parked 24.1 m down the eclipse axis. And fog must never reach a landing target:
every hop in Level_01 lands inside 12 m, so 36 keeps foot placement at fog factor zero. Pinned by four
tests in `SkyEclipseTests`.

**Measured, so expectations stay honest:** the first 36 -> 170 pass moved 0.8% of the historical 91 m
frame, and an identical-camera 170 -> 140 check at the new ramp crest moved mean RGB by only 0.002% of
channel range. Linear fog remains a route-depth instrument; it cannot supply visible moving atmosphere.

`CloudSea` supplies the moving layer the level actually calls for: one fixed world-space ocean below every
structure, spanning x -450..450 and z -550..850 around the shipped route (-19.5..19.5, -132..409.5). Its
surface sits at y -5; three crossing swells plus the irregular cloud-bank lift total at most 1.10 x the
1.50 m wave height, so the highest possible crest is y -3.35, still 2.35 m below the lowest platform
underside at y -1. The course therefore
reads as one set of ruins suspended above a common rolling cloud bed, rather than a local puff following
the player.

The effect is one 13,195-vertex / 77,760-index grid and one transparent URP draw. Long vertex swells make
the horizon physically rise and fall. In the fragment pass, a slow low-frequency field bends a three-octave
billow flow while stretched smaller noise erodes their boundaries in the other direction; the two spatial
scales are about 3.7x apart, so this reads as nested soft bodies with torn wispy edges instead of stretched flat
noise or bright contour rings. The broad edge
feather stays beyond the nearby route view and settles into cloud-specific haze before the rectangle
can show. It has no particles, collisions, lights, shadows, probes, per-frame C# work or gameplay state.
All colour channels remain below 1.0, and ordinary alpha blending plus depth testing keeps it behind solid
course geometry without spending the attack-tell bloom budget. Shader `_Time.y` is scaled in play, so pause
and hitstop freeze this world atmosphere; death/Pyre mist remain unscaled because they explain an event.

## Enclosing arena suns — 2026-09-07 refinement

The four exterior plasma bodies have visual radii 22/23/22/31 m at their existing centers. Each encloses
its historical floor, walls, pillars, gates and torches with at least 1.5 m margin. The corona scales
with the body. The saturated rotating patterns, realm ceiling and themes retain their accepted design.
The separate physical portal radii remain 12/13/12/18 m, preserving retry and onward-return positions.
The higher crest uses a 900x1400 m cloud bed and a separate 80..280 m cloud-haze range. Gameplay
fog remains 36..140 m. Exterior suns use 0.92 surface opacity to obscure the old courts, while the
corona and realm ceilings retain their additive appearance via zero surface-opacity overrides.
