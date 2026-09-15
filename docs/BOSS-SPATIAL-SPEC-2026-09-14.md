# Boss spatial / animation spec (Fable review, 2026-09-14)

Read-only pass over the seven realm bosses on the `PuppetVisuals` path. Every number below is read from
the shipped `.asset` / `.prefab` YAML and `Assets/Enemies/*.clips.json`, never from a C# initialiser.
The lead (Opus 5) implements; nothing here was edited. `Legendary_Ninja` is NOT on this path (primitive
`EnemyVisuals`, `MiniBossFactory.cs:1166`); it appears only where a shared base-class defect reaches it.

## 0. The arithmetic every finding uses

| symbol | value | source |
|---|---|---|
| cueLead | 0.28 s | `EnemyController.cs:38` |
| travel window | cue -> impact = 0.28 s, so `lungeSpeed = lungeDistance / 0.28` | `EnemyController.cs:731-739` |
| lunge stop | `dist <= lungeMinDistance` | `EnemyController.cs:593` |
| combo gap | `comboGap x lerp(1, 0.45, aggression)`, floor 0.1 | `EnemyController.cs:632-635` |
| secondsToImpact | `windup + gap + impactDelay` | `EnemyController.cs:707-711`, `PuppetVisuals.cs:269` |
| clip contact | `namedClipLengths[i] x namedClipHits[i]` (prefab tables) | `PuppetVisuals.cs:482` |
| clip rate | `contact / secondsToImpact`, floor 0.4 (late start), cap 3.5 | prefabs `minClipSpeed/maxClipSpeed`; `PuppetVisuals.cs:546-594` |
| visual lunge | `visuals.Strike(lungeDistance, strike+impactDelay)` -> `LungeCo` moves **LungeRoot** to `+forward x dist` over 40 % of that window, back over 60 % | `EnemyController.cs:752`, `EnemyVisuals.cs:438-446, 708-735` |
| player | groundSpeed 11 m/s (`FirstPersonMotor.cs:32`), perfect window 0.13 s (`PlayerStats.asset:17`) |
| realm | floor R 25, shell 37.5, wall/ceiling 20 m, enemy at z +5, entry z -16.25, partner x 7 | `LevelDefinitionAuthoring.cs:1595-1602` |

Strafe geometry: at 11 m/s on a 2.6 m ring the player sweeps 242 deg/s; wind-up tracking is
`turnSpeed x windupTurnMultiplier x ease` = 96 deg/s peak on the Lancer (`EnemyController.cs:524-533`).
Every cone (35-120 deg) is therefore honest to a fault: any full-speed strafe leaves it, and a walking
player (4 m/s, 88 deg/s) still out-turns the wind-up. No cone needs widening.

## 1. Roster-wide defects (fix once, in the shared function)

| # | rank | defect | evidence | fix (exact) | test |
|---|---|---|---|---|---|
| R1 | HIGH | **The lunge is applied twice.** The brain moves the root `lungeDistance` from cue to impact AND `LungeCo` pops LungeRoot another `lungeDistance` in `0.4 x (strike+impactDelay)` s, then slides it back. Halberdier_Charge: root 4.7 m at 16.8 m/s, mesh a further 4.7 m in 0.104 s (45 m/s), back over 0.156 s. Thrust 1.3, Leap 2.4, Revenant Stab 1.5, Marionette Overhead 1.7 (x1.15 scale), every Heavy 0.75. Only the four profiles zero it, and only for Shoulder/Dash (`FlurryBrawlerV18Visuals.cs:178-179`). | `EnemyController.cs:113-119` says the root lunge REPLACED the child-transform lunge; the child lunge was never removed. | `PuppetVisuals.Strike` (`PuppetVisuals.cs:281`): `base.Strike(0f, seconds)` for every rigged body (the clip carries the swing; the brain carries the travel). The 18 deg pitch lean it also drops is re-added by P3 below. Primitives (Ninja, Grunt) keep theirs, but cap it: `EnemyVisuals.LungeCo` -> `Mathf.Min(dist, 0.35f)`. | `PuppetSpinTests`: new `RiggedStrikeNeverMovesLungeRoot` (instantiate `Legendary_Halberdier`, call `Strike(4.7f, 0.26f)`, assert LungeRoot.localPosition.z == lungeBase.z after 0.1 s). |
| R2 | HIGH | **Shoulder charges run on a treadmill.** All five chargers stage `Run` at Telegraph, but `SetState(Windup)` stops locomotion (`EnemyController.cs:806`) and the root does not move until the cue. Lancer/Dancer/Judge: 0.61-0.66 s of Run in place, then the root covers 3.32 m in 0.28 s (11.9 m/s). V18: 0.87 s in place, 4.12 m at 14.7 m/s. Halberdier (plain PuppetVisuals) plays the whole ShoulderCharge clip from t=0 with its 3.55 m of Hips cancelled by TravelRoot: 0.58 s running in place, then 4.7 m in the clip's airborne frames. | `CinderJudgeVisuals.cs:196-205`, `SeraphLancerVisuals.cs:280-294`, `OrbitDancerVisuals.cs:144-157`, `FlurryBrawlerV18Visuals.cs:119-134`, clips.json ShoulderCharge root fwd 2.67/3.45/3.55. | Brain (lead-owned): widen the travel window for long lunges. `EnemyController.BeginWindup`: `lungeStart = projectedImpact - Mathf.Max(cueLead, atk.lungeDistance / MaxLungeSpeed)` with `public const float MaxLungeSpeed = 9f`; `ApplyLunge` runs from `lungeStart`, direction frozen at `lungeStart` (still a commit; a red charge is answered by moving). Windows: 3.32 m -> 0.37 s, 4.12 -> 0.46, 4.7 -> 0.52; every lunge <= 2.5 m is unchanged (cue-bound). Presentation: hold `clipIdle` (Judge: `Roar`, V18: `Block`) until `lungeStart`, `Run` from `lungeStart`, charge clip at `impact - contact` as now. Hoist that staging out of the four profiles into `PuppetVisuals` keyed on `EnemyAttackData.lungeDistance >= 2.5f && clip named` (deletes four identical copies) so the Halberdier gets it with zero new class. | `SoulsCombatTests`: pure `LungeWindow(lunge, cueLead, maxSpeed)`; `HalberdierBehaviourTests.TheChargeCanLandFromItsWholeBand` unchanged (distance arithmetic identical). |
| R3 | HIGH | **Short-run-in clips play in slow motion.** ComboFinisher (Lancer/Dancer/Judge) has 0.30 s of run-in; as hit 3 it wants x0.34-0.38 and now starts late then plays 0.75 s at x0.4. SpinThrow (0.142 s run-in) plays at x0.4 for 0.355 s; ShieldBash (0.119 s) 0.30 s at x0.4; V18 Dash (0.275 s, no baked hit event) x0.42 for 0.65 s. A 3-7 frame anticipation stretched 2.5x reads as slow-mo, and the freeze that precedes it (previous clip clamped on its last frame) reads as a hitch. | `PuppetVisuals.cs:546-551`; contacts from prefab tables. | (a) New prefab field `lateStartSpeed = 0.7f` (written by `MiniBossFactory` beside `minClipSpeed`, `MiniBossFactory.cs:1280`): `ClipStartDelay(contact, toImpact, lateStartSpeed)`; the pending clip plays at `lateStartSpeed`. Delays become: Finisher 0.45 s, SpinThrow 0.45, Bash 0.70, Jab2 0.11, Stab 0.16, Heavy 0.33-0.53 (a heavy holding its guard 0.3 s is right). (b) Fill the delay: in `Update`, while `pendingClipAt < MaxValue` and the current state is non-looping with `normalizedTime >= 1`, `CrossFadeInFixedTime(clipIdle, clipBlend * 2.5f)` once. (c) Attack-entry blend `Mathf.Clamp(0.25f * secondsToImpact, clipBlend, 0.16f)` in `PlayAttackClip` (the Recoil jar keeps 0.07). | `PuppetSpinTests.AClipWithAShortRunInStartsLateAndStillLandsItsContactOnTheBlow`: floor 0.4 -> 0.7, delay 0.857-0.30/0.7 = 0.428; add `AnIdleFillNeverOutlivesThePendingClip`. |
| R4 | MED | **Deflect recoil is a mesh pop.** `Recoil` runs `LungeCo(-0.5, 0.25)`: the mesh jumps 0.5 m behind its collider in 0.1 s and slides forward again; the root never moves. | `EnemyVisuals.cs:470-475` | Visual: `-0.5f` -> `-0.18f`. Root (lead, brain): in `OnParried`, nudge the body back `ParryRecoilMetres = 0.35f` spread over `parryRecoilSeconds` (`locomotion.Nudge(-facing * 0.35 / recoil * dt)` while `Current == Recover && Time.time < recoilEnd`). The step-back is what makes a deflect look like it cost him something. | `SoulsCombatTests`: `RecoilStepPerFrame(metres, seconds, dt)` pure; `FeatureTests` parry harness asserts root moved 0.25-0.45 m after a Perfect. |
| R5 | MED | **Three older bodies have no stride reference.** `walkStrideSpeed/runStrideSpeed = 0` on Halberdier, Revenant, Marionette (in-place forge Walk/Run, no `root` block in their clips.json), so `PuppetLocomotion.Rate` returns 1x and the Halberdier's feet slide at 5.8 m/s under a ~3.4 m/s run cycle (1.7x slide). | prefabs `walkStrideSpeed: 0`; `MiniBossFactory.cs:1300-1301`, `StrideSpeed` returns 0 when Hips do not travel. | `MiniBossFactory.cs:1300`: `pv.walkStrideSpeed = s > 0 ? s : 1.9f; runStrideSpeed = s > 0 ? s : 3.4f` (the forge's measured range is 1.8-2.7 walk, 3.1-4.4 run). Rate clamps to 1.6x, cutting the slide to ~1.06x. | `PuppetLocomotionTests`: `AnInPlaceClipGetsTheForgeNominalStride`. |
| R6 | LOW | Follow-through ownership is 0.45 s on every clip; the Finisher's 1.2 s flourish and the Kick's leg-out tail are cut mid-motion when locomotion reclaims. | `PuppetVisuals.cs:151, 593` | `followThroughSeconds` 0.45 -> `Mathf.Min(0.6f, tail)` where `tail = length - contact` per clip, computed in `PlayAttackClip`. Recovery (0.55-1.3 s raw, x0.42-0.65) still covers it. | none (presentation). |

None of R1-R6 touches a wind-up, `impactDelay`, `cueLead`, a shooter `projectileSpeed`/interval, or
`ParryModuleSolver` inputs. **`Projectile Encounter Report` is not required by anything in this file**;
it becomes required only if the lead changes `projectileSpeed` (Lancer 15, Dancer 16), which I do not propose.

## 2. Per boss

Columns: attack | problem | measured | proposed change | test pin. Ranks are how visibly wrong it looks.
Contact seconds are the prefab table values; rates are for the attack's shipped combo position.

### 2.1 Seraph Lancer (`Legendary_SeraphLancer`, aggression 0.70, pref 2.6 + 0.3, lungeMin 0.9)

| attack | problem | measured | proposed change | test pin |
|---|---|---|---|---|
| Jab2 (hit 1) | HIGH reach: fist clip with lunge 0 lands at 2.6-2.9 m; a forge humanoid's fist reaches ~1.3 m from the root, so the blow visibly stops ~1.3 m short. MED band gap: reach 2.35+0.5 = 2.85 < band 2.9. | contact 0.305 / 0.55 = x0.55 ok | `DataFactory.cs:2384` range 2.35 -> 2.45, lungeDistance 0 -> 0.5 (a 1.8 m/s step from the cue; lungeMin 0.9 keeps it off the capsule) | `SeraphLancerDataTests` reach pin; `CinderJudgeDataTests.cs:217-221`-style "no travel => lunge 0" rule must become `<= 0.6f` for canonical (non-generated) clips |
| Swing (hit 2) | same reach; rate fine | 0.435 / 0.723 = x0.60 | `:2395` lungeDistance 0 -> 0.5 | as above |
| ComboFinisher (hit 3) | HIGH slow-mo (R3) | 0.30 / 0.882 = x0.34 -> late 0.13 s then x0.4 | R3 | R3 |
| Stab | ok: a lance thrust at 2.9 m is honest; x0.53 | 0.344 / 0.65 | none | |
| Kick | ok | 0.413 / 0.65 = x0.64 | none | |
| Heavy (dive) | MED: Jump segment x0.47 then hop 0.8 m; lunge 0.75 doubled (R1) | 0.27 / 0.57 | R1; `diveHopHeight` 0.8 -> 1.1 (`MiniBossFactory.cs:~524`) so the dive reads as a dive from 2.6 m | `SeraphLancerDataTests` dive pin |
| ShoulderCharge | HIGH treadmill (R2): 0.61 s in place, then 3.32 m at 11.9 m/s | 1.03 - 0.417 | R2 | R2 |
| Sky Verdict | MED aim/facing: HoverRoot tracks at 120 deg/s; a player at 4.5 m and 11 m/s needs 140 deg/s, so the javelin leaves a hand pointing up to 40 deg off the line. Geometry otherwise sound: muzzle at 1.5 + 3.0 m, pitch 36 deg down at 4.5 m, 13 deg at 14 m; `LaunchSpeed` floor (0.40 s flight) only binds under 6 m of path; homing 40 deg/s buys 16-36 deg over a 0.4-0.9 s flight. 4th throw at impact+3.30, descent at +3.40: tight but sequential. | `MiniBossFactory.cs:530` | `hoverTrackDegPerSec` 120 -> 220 | `SeraphLancerDataTests` tracking pin |

Phrase chaining (S2): Finisher (1.2 s tail) -> Jab2 is the snap: cut 0.2 s into the flourish by a 0.07 s
blend. R3(c) fixes it (0.14 s blend) and R3(a) lets the tail run 0.45 s more. Kick -> Stab is fine at 0.14.

### 2.2 Orbit Dancer (`Legendary_OrbitDancer`, aggression 0.85, pref 2.2 + 0.3, lungeMin 0.9)

| attack | problem | measured | proposed change | test pin |
|---|---|---|---|---|
| Jab2 / Swing / Stab / Kick | HIGH reach (fists at 2.2-2.5 m, lunge 0) | x0.61 / x0.61 / x0.54 / x0.61 | `DataFactory.cs:2161/2172/2190/2197` lungeDistance 0 -> 0.5 | `OrbitDancerDataTests` |
| ComboFinisher | HIGH slow-mo (R3) | 0.30 / 0.788 = x0.38 | R3 | R3 |
| Heavy | x0.54 ok; R1 doubling | 0.557 / 1.03 | R1 | |
| ShoulderCharge | HIGH treadmill (R2) | 1.03 - 0.417 = 0.61 s in place | R2 | R2 |
| DiscThrow | ok: x0.50, the baked hop (0.696-0.913) plays after the release at 1x | 0.378 / 0.76 | none | |
| SpinThrow | HIGH: 3-frame whirl at x0.19-0.22 -> x0.4 late-started; the whirl is the one move that should blur | 0.142 / 0.65 (0.735 as hit 2) | R3 plus data-only: `MiniBossFactory` Dancer block set `pv.spinAttackPrefix = "OrbitDancer_SpinThrow"` so `BeginPass` drives SpinRoot: 0.65 s x 2087 deg/s = 3.77 rev -> 4 whole revs at 2215 deg/s (+6 %, inside the 12 % band), square-on at the release; 0.735 s -> 4 revs at 1959 (-6 %). The clip still plays; the body blurs under it. | `PuppetSpinTests` BeginPass arithmetic already covers it; add `OrbitDancerDataTests.TheWhirlIsAWholeNumberOfRevolutions` |
| bank shots | MED arena: probes 40/65/90 deg reach 20 m; at R 25 the wall is 20-30 m from her, so most banks fall back to the fan and fly off. The shrink HELPS: at R <= 15 every probe finds a wall. Ideal wall distance 6-12 m (path 12-24 m, 0.9-1.7 s). | `OrbitDancerDiscs.cs:304-330`, prefab `bankRange 20` | keep 20 if R <= 15; else `bankRange = R + 5` | `OrbitDancerDataTests` bank pin |

Chaining: Jab2 -> SpinThrow is a combo already; after a throw `MayCommitToAttack` refuses for 0.9 s
(discs in flight), so no snap. Kick (leg out) -> Jab2: R3(c).

### 2.3 Cinder Judge (`Legendary_CinderJudge`, aggression 0.80, pref 2.4 + 0.3, lungeMin 0.9)

| attack | problem | measured | proposed change | test pin |
|---|---|---|---|---|
| Jab2 / Swing / Stab / Kick | HIGH reach (lunge 0 at 2.4-2.7 m) | x0.55 / x0.57 / x0.57 / x0.56 | `DataFactory.cs:1982/1992/2010/2017` lungeDistance 0 -> 0.5 | `CinderJudgeDataTests.cs:213-221` relax |
| ComboFinisher | HIGH slow-mo (R3) | 0.30 / 0.894 = x0.34 | R3 | R3 |
| Heavy | MED: x0.49 (x0.43 as Aegis hit 3); the smash's take-off and contact are the same frame, so slow is tolerable; R1 doubling | 0.557 / 1.13 (1.30) | R1; leave the rate | |
| ShoulderCharge | HIGH treadmill (R2): 0.66 s in place | 1.08 - 0.417 | R2 | R2 |
| ShieldRaise | ok: clip stretched x1.32 then clamped and held 2.4 s | 0.79 / 0.60 | none | |
| ShieldBash | MED: 3-frame bash wants x0.14 -> 0.57 s hold then 0.30 s at x0.4 | 0.119 / 0.866 | R3 (0.70 s hold, 0.17 s bash at x0.7) | R3 |
| Storm Judgement | ok: Roar x0.755 over the charge; Jump x0.6 to 2.4 m over 0.45 s; 2.85 s hover at 540 deg/s = 4.3 revs; landing punish 1.54 s. Ring 3.6 = range 3.1 + 0.5 (pinned). Arena: the ring must have an exit: R >= 8 (ring 3.6 + fighting distance 2.4 + 1.5 m of running room). | `CinderJudgeDataTests.cs:242-248` | none to the storm; see arena table | |

Chaining: Aegis ends in the Heavy (recovery 1.4 > 1.0) so it never chains; Stab -> Jab2 at 0.14 s blend is fine.

### 2.4 V18 Grappler (`Legendary_FlurryBrawlerV18`, aggression 0.90, pref 2.0 + 0.3, lungeMin 0.9)

| attack | problem | measured | proposed change | test pin |
|---|---|---|---|---|
| Jab2 / Swing / Stab / Overhead / Kick | HIGH reach at 2.0-2.3 m with fists, lunge 0 (the closest fighter, so the shortfall is smallest: ~0.7 m) | x0.55 / x0.59 / x0.54 / x0.53 / x0.56 | `DataFactory.cs:1860/1832/1846/1839/1853` lungeDistance 0 -> 0.4 | `FlurryBrawlerV18DataTests.cs:322` |
| Dash | MED: 12-frame hop at x0.42 for 0.65 s, no baked hit event (0.6 default) | 0.275 / 0.65 | stage like the charge (R2 hoist, threshold `lungeDistance >= 1.5f` for Dash): Idle until impact-0.275, Dash at 1x; the root's 1.77 m at 6.3 m/s from the cue is fine | R2 |
| ShoulderCharge | HIGH treadmill (R2): 0.87 s in place, 4.12 m at 14.7 m/s | 1.08 - 0.215 | R2 (window 0.46 s at 9 m/s) | R2 |
| LevitateClap | LOW: ring 3.8 vs reach 3.6 + 0.5 = 4.1 | prefab `clapRingRadius 3.8` | `MiniBossFactory.cs:238` 3.8 -> 4.1 | `FlurryBrawlerV18DataTests` clap pin |
| Grab (red) | ok: Clap clip x0.47; lift 11 m smoothstep 0.9 s, hold 0.35, throw: from 10.9 m at 14 m/s down under g 30 -> t 0.505 s, horizontal 11.9 m/s, lands 6 m out as V18 lands (descend 0.5 s). Arena: throw target ignores walls; ceiling must clear 11 + 1.8 m eye = 13 m. | `V18Grapple.cs:60-70`, prefab 2051-2059 | none; see arena table | `V18GrappleTests` |
| GrabSlam | ok (range 0, fired by `WatchSlam` on ground contact) | | none | |
| Combo2 | MED: 4.36 s of run-in over 1.55 s = x2.81 fast-forward, then a 2.14 s tail held while the brain is already in Chase (glide) | 6.5 x 0.671 | taste: windup 1.5 -> 2.6 (x1.68; wind-ups may lengthen) at `DataFactory.cs:1916`, recovery 4.6 -> 3.5; or keep as the "absurd" one | `FlurryBrawlerV18DataTests` combo pin |

### 2.5 Argent Halberdier (`Legendary_Halberdier`, aggression 0.90, pref 3.2 + 0.4, lungeMin 1.2, scale 1.15, plain PuppetVisuals)

| attack | problem | measured | proposed change | test pin |
|---|---|---|---|---|
| Sweep / Thrust / Slam | HIGH R1: mesh pops 1.0 / 1.3 / 1.1 m (x1.15) beyond the root's own step, then moonwalks back. Rates x0.63 / x0.59 / x0.54 (x0.55 / 0.50 / 0.50 as later hits). Reach honest (halberd). | `DataFactory.cs:1297-1328` | R1 | R1 |
| Charge | HIGH R1+R2: root 4.7 m at 16.8 m/s in the cue window, mesh +4.7 m in 0.104 s, 0.58 s of running in place first. MED band: the 5-18 m "to the aggro edge" entry whiffs past 8.2 m by design (a closer), but a red charge thrown from 15 m that stops 6.8 m short reads as a bug. | `:1329-1348`; moveset entry "SHOULDER CHARGE (unblockable, to the aggro edge)" | R1 + R2 (window 0.52 s at 9 m/s). Entry maxRange 18 -> 9 (Chase closes the rest); `HalberdierBehaviourTests.cs:184-199` still passes (`HasEligible(7)` true, `HasEligible(19)` false) | `HalberdierBehaviourTests.TheChargeCanLandFromItsWholeBand` |
| Leap | ok timing: x1.17, airborne from 0.87 s to the hit, lunge 2.4 over 0.28 s matches; R1 doubling (+2.76 m pop). No landing squash. | `:1359-1368` | R1; P6 squash at the clip's JumpLand (0.771 x 1.79 = 1.38 s clip time) | |
| Spin | ok: x1.15, cone 300, lunge 0 | | none | |
| Kick | ok x0.97 | | none | |
| locomotion | MED R5 (stride 0 at 5.8 m/s) | | R5 | R5 |
| unused art | LOW: HalberdSweep, HalberdBackswing, OverheadSlam, Thrust, HeavyWindup are baked into the table and referenced by nothing (`DataFactory.cs:1299-1324` chose the canonical clips on purpose). | | leave; note for the enemy-designer | |

### 2.6 Ember Revenant (`Legendary_Revenant`, aggression 0.55, pref 3.4 + 0.7, lungeMin 2.2, moveSpeed 3.4)

| attack | problem | measured | proposed change | test pin |
|---|---|---|---|---|
| Slash / Stab / Overhead / Kick | HIGH R1 (0.9 / 1.5 / 1.3 / 1.1 m pops on the slowest body in the roster). Rates are the roster's best: x0.96 / 0.85 / 0.83 / 0.79 (x0.78 / 0.68 / - / 0.66 after a slash). | `DataFactory.cs:1091-1160` | R1 | `RevenantDataTests` |
| reach | MED: ranges 3.2-3.9 with the lunge stopping at 2.2 m; a greatsword reaches ~1.8 m, so a blow that lands at 3.4-4.1 m (band) has the tip ~1 m short unless the step carries. | | `lungeMinDistance` 2.2 -> 1.6 (`DataFactory` Revenant EnemyData block, ~`:1175`), Slash lunge 0.9 -> 1.2 | `RevenantDataTests` reach pin |
| thrust from range (3.5-8 m) | LOW: reach 3.9 + 0.5 + 1.5 = 5.9; from 6-8 m it is a closer that whiffs | | entry maxRange 8 -> 6 | |
| locomotion | LOW R5 | | R5 | |

Chaining: Slash (0.52 s tail) -> Slash cut at 0.16 s = a stutter; R3(c) blend 0.17 s fixes it.

### 2.7 Pale Marionette (`Legendary_Marionette`, aggression 0.62, pref 3.7 + 0.7, lungeMin 2.8, scale 1.15)

| attack | problem | measured | proposed change | test pin |
|---|---|---|---|---|
| SpinPass | HIGH R1: a 0.6 m (0.69 scaled) LungeRoot pop in 0.056 s and back in 0.084 s on EVERY 0.69 s beat: exactly the positional pulse `PuppetVisuals.cs:161-175` was built to remove. Rate x1.24 fine; beat = 0.45 + 0.1 + 0.04 + 0.1 = 0.69 = 4 revs (pinned). | `DataFactory.cs:878` | R1 (the brain's 0.6 m step stays: 2.1 m/s, cue-bound) | `MarionetteDataTests` |
| SpinPass reach | MED: the whirl hits at 3.9 + 0.5 m with arms spanning 2.0 m (Roar) -> a 1.15 m radius blur landing 2.7 m short. S6 said leave her tempo; this is reach, not tempo. | `Marionette` EnemyData `:967` | preferredRange 3.7 -> 2.8, lungeMinDistance 2.8 -> 1.8, SpinPass/SpinUp range 3.9 -> 3.1, SpinOut 4.1 -> 3.3, Overhead 3.7 -> 3.2. Beat untouched. | `MarionetteDataTests` reach/band pins (`preferredRange >= attackRange` 3.2 needs attackRange 3.2 -> 2.8 too) |
| Overhead | HIGH R1 (1.96 m scaled pop) | 0.85 / 1.07 = x0.79 | R1 | |
| Lash | LOW readability: an 8 m unblockable "string lash" plays the AttackOverhead clip (unblockable -> clipHeavy). | `PuppetVisuals.cs:510-514` | `Marionette_Lash.clip = "AttackStab"` (a reach-out) at `DataFactory.cs:955` | `MarionetteDataTests` clip pin |

### 2.8 Ninja (shared base only)
`Legendary_Ninja` is primitive `EnemyVisuals`; R1's cap (0.35 m) reaches it: Rush 2.0 m currently pops the
capsule mesh 1.9 m ahead of its collider. Chaining: aggression 1.0 and recoveries 0.22-0.6 s mean it chains
every phrase (cap 2), up to 13 cuts back to back at T1; arbitration with the Lancer keeps it sequential.

## 3. Procedural layer (Megabonk-simple clips, Unity sells the weight)

One writer per transform. Insert **`PoseRoot`** between `LungeRoot` and `HoverRoot/StormRoot/GrabRoot/SpinRoot`
in `MiniBossFactory` (the HoverRoot/StormRoot precedent) and write it ONLY from a new `PuppetVisuals`
block below; `LungeRoot` stays the base class's (dip + settle, and R1 zeroes its lunge). Every amplitude is
scaled time, derived from the `seconds` the brain hands `Telegraph`/`Strike`, capped so nothing outlives
its window; the cue never moves. Curves: ease-out `1-(1-k)^2`, ease-in `k^2`, damped return
`A e^(-t/tau) (1 + t/tau)`.

| # | beat | hook | motion on PoseRoot (default body) | timing |
|---|---|---|---|---|
| P1 | anticipation lean-back | `Telegraph` | z -0.06 m, y -0.04 m, pitch -5 deg (chest up), replaces the base dip's forward component for rigged bodies | 0 -> `min(0.18, 0.30 x windup)` ease-out, HELD to the cue |
| P2 | cue hitch | `CueFlash` | pitch +3 deg, z +0.03 m, instant (contract: no easing), held to the strike | one frame |
| P3 | lunge lean | `Strike` + `Update` while the brain's lunge is live | pitch = `clamp(lungeSpeed x 2.2 deg per m/s, 0, 22)`: 0.5 m -> 4 deg, 0.75 -> 6, 1.3 -> 10, 3.32 -> 22 (cap), 4.7 -> 22 | in over 0.08 s from `lungeStart`, out over 0.12 s after impact |
| P4 | overshoot + settle | `Strike` at impact | z +0.08 m, pitch +4 deg at impact+0.06, damped return tau 0.08 s | done by impact+0.30 |
| P5 | torso yaw lead | `Telegraph` -> `Strike`, written into `spinPhase` only on bodies with `spinAttackPrefix == ""` (one writer on SpinRoot) | wind-up counter-yaw: sweeps (cone >= 90) -14 deg, thrusts (cone <= 45) -6 deg, else -10; swings through to +10 / +4 / +7 at impact | ease-out over the wind-up, ease-in over cue -> impact, settle to 0 over 0.25 s |
| P6 | landing squash | `Land()` in Judge/Lancer; `V18Grapple` descent end (`SetLift(0)`, `V18Grapple.cs:138`, via `visuals.Settle`-style call); Halberdier Leap/Spin at the clip's JumpLand time | y -0.12 m, pitch +6 deg in 0.06 s, damped return tau 0.10 | done by +0.30 |
| P7 | deflect recoil | `Recoil` | z -0.18 m, pitch -8 deg in 0.05 s, return over `parryRecoilSeconds`; root step-back per R4 | |
| P8 | idle / strafe weight shift | `Update` when `locoState != Run` | roll `clamp(lateralRootSpeed x 2.5 deg per m/s, +-5)` toward the strafe direction, y bob 0.02 m at 1.2 Hz while circling | continuous |

Per-body multipliers on the table above (one `proceduralScale` Vector3-ish tuple written by `MiniBossFactory`;
if the lead prefers zero new fields, bake them as constants keyed on the profile class):

| boss | P1 | P3 cap | P4 | P5 | notes |
|---|---|---|---|---|---|
| Seraph Lancer | x0.8 | 18 deg | x0.8 | x1.0 | light; wings flick 0.10 spread on the cue (`WingSpread` already exists) |
| Orbit Dancer | x0.8 | 18 | x1.0 | x1.6 | she pirouettes: P5 on jabs +-16 deg; SpinThrow uses the real whirl (2.2) |
| Cinder Judge | x1.2 | 22 | x1.2 | x0.8 | ShieldRaise: z -0.08 m plant over the raise; storm landing squash 0.16 m |
| V18 Grappler | x1.0 | 22 | x1.5 (z 0.12) | alternating +-10 deg per jab | grab lift-off: y overshoot +0.15 m at lift start |
| Argent Halberdier | x1.0 | 22 | x1.2 | Sweep/Spin +-18 deg | Charge: P3 at cap from `lungeStart`; Leap squash P6 |
| Ember Revenant | x1.6 (0.25 s, z -0.10) | 12 | x1.2, tau 0.12 | x0.8 | slow and heavy; Overhead settle 0.4 s |
| Pale Marionette | passes: NONE (no pulse, by design); Overhead/Lash: x1.0 | 8 | passes: none | n/a (whirl owns SpinRoot) | only the tempo breaks get the layer |

Test: `PuppetSpinTests.ProceduralLayerNeverWritesLungeRootOrSpinRootOnAWhirlBody`; a pure
`DampedReturn(A, tau, t)` and `LeanForLungeSpeed(speed, cap)` in the same file.

## 4. Choreography: what the brain is missing (mapped onto EnemyController + EnemyMoveset)

### 4.1 HP phases (data: 3 fields; brain: ~8 lines)
- `EnemyData`: `float phase2Threshold = 0f` (0 = none), `EnemyMoveset phase2Moveset`, `float phase2AggressionBonus = 0.1f`.
- `EnemyController`: `bool phase2`; in `HandleDamaged` (`:842`) `if (!phase2 && data.phase2Threshold > 0 && Health.Ratio <= data.phase2Threshold) EnterPhase2()`. `EnterPhase2`: `phase2 = true; moveLastUsedAt = null` (re-sized on the next `ChooseCombo`), `combo = null`, `SetState(Recover, 0.9f)`, `visuals.Roar()` (exists on every profile; `PuppetVisuals.cs:337`), `AudioManager.Play(Sfx.Thunder)`, `CameraShake.I.Small()`. `ChooseCombo` reads `phase2 && data.phase2Moveset != null ? data.phase2Moveset : data.moveset`; `Aggression` adds the bonus (clamped). Arbitration, flask punish, chaining untouched.
- `DataFactory`: each boss gets `Legendary_X_Moveset_P2` = the same entries with signature weight x2 and cooldown x0.5; threshold 0.5 for all seven (0.6 on the Revenant, the slowest).
- Test: `SoulsCombatTests.PhaseShiftFiresOnceAtTheThreshold` (pure `static bool PhaseShiftDue(ratio, threshold, already)`); `FeatureTests`: drive `Health` to 49 %, assert `LastMoveIndex` came from P2 and one `Roar` was staged.

### 4.2 Held wind-ups (roll-catch), data-only plus one presentation rule
- Per boss ONE held variant of the tempo-break heavy, a second attack asset with `windup + 0.40 s`, same clip, damage +10 %, its own moveset entry weight 0.6 / cooldown 6 (never in phase 1 below 30 % HP? no: always available; phase 2 doubles its weight). Wind-ups only get longer; the cue stays 0.28 s before impact.

| boss | asset (new) | windup old -> new | damage | DataFactory anchor |
|---|---|---|---|---|
| Lancer | `SeraphLancer_HeavyHeld` | 1.05 -> 1.45 | 32 -> 35 | `:2435` |
| Dancer | `OrbitDancer_HeavyHeld` | 0.95 -> 1.35 | 30 -> 33 | `:2206` |
| Judge | `CinderJudge_HeavyHeld` | 1.05 -> 1.45 | 36 -> 40 | `:2024` |
| V18 | `BrawlerV18_OverheadHeld` | 0.95 -> 1.35 | 34 -> 37 | `:1839` |
| Halberdier | `Halberdier_SlamHeld` | 0.95 -> 1.35 | 38 -> 42 | `:1318` |
| Revenant | `Revenant_OverheadHeld` | 0.95 -> 1.40 | 38 -> 42 | `:1133` |
| Marionette | `Marionette_OverheadHeld` | 1.00 -> 1.40 | 40 -> 44 | `:941` |

- Presentation (the FromSoft hold, `PuppetVisuals.PlayAttackClip`): when `secondsToImpact > contact / lateStartSpeed + 0.25`, play the clip at `lateStartSpeed` to `contact - 0.25 s` of clip time, freeze (`animator.speed = 0`) with a 1.5 Hz 0.02 m tremble on PoseRoot, resume at 1x at `impact - 0.25`. Pure `HoldWindow(contact, toImpact, rate, resumeLead) -> (freezeAt, resumeAt)`; test in `PuppetSpinTests`.

### 4.3 Arena awareness (one static, one field, three lines)
- `SolarArenaPortal` sets `ArenaBounds.Current = (realmBoundsCenter.position, realmContainmentRadius)` on entry (`SolarArenaPortal.cs:102-114`) and clears it on exit; a static struct, nothing else.
- `MovesetEntry.wallBias = 1f` (new float). `EnemyMoveset.WeightedPick` takes `float edgeRoom` (metres from the player to the wall, `radius - |player - centre|`, `MaxValue` outside a realm) and multiplies each weight by `Mathf.Lerp(e.wallBias, 1f, Mathf.Clamp01(edgeRoom / 4f))`.
- Data: charges (all five) `wallBias 2.5`; Storm 2.0; Suplex 2.0; Halberdier Leap 2.0; Marionette Lash 2.0 (drag them back); Dancer volley 0.5 (banks want room), SpinThrow 1.5. Everything else 1.
- Test: `SoulsCombatTests.WallBiasOnlyBindsInsideFourMetresOfTheWall` (pure).

### 4.4 Deadlier, more signatures (data-only; the lead is editing these lines this pass, so merge, do not overwrite)
Rule kept: every blow cued; blues never punish a guard (no `unblockable` flips beyond the shipped Grab).

| boss | signature | cooldown old -> new | weight old -> new | damage old -> new |
|---|---|---|---|---|
| Lancer | Sky Verdict / Heavy dive / Charge | 10 -> 7 / 7 -> 5 / 8 -> 6 | 2 -> 2.6 / 1.2 -> 1.5 / 2.2 | javelin 14 -> 16, Heavy 32 -> 36 |
| Dancer | Orbit Storm / SpinThrow / Heavy | 6 -> 4 / 8 -> 5 / 8 -> 6 | 2 -> 2.6 / 1 -> 1.5 / 1 -> 1.3 | disc 12 -> 14, Heavy 30 -> 34 |
| Judge | Storm / Aegis / Charge | 14 -> 9 / 11 -> 7 / 7 -> 5 | 1 -> 1.6 / 1.3 -> 1.8 / 2.4 | tick 6 -> 7 (11 ticks = 77), Heavy 36 -> 40, Bash 22 -> 26 |
| V18 | Suplex / Clap / Charge | 10 -> 6 / 8 -> 5 / 7 -> 5 | 1.2 -> 2.0 / 1.1 -> 1.5 / 2.4 | GrabSlam 26 -> 32, Clap 28 -> 32, Overhead 34 -> 38 |
| Halberdier | Spin / Leap / Slam | 7 -> 4 / 6 -> 3.5 / 6 -> 4 | 1 -> 1.6 / 1.2 -> 1.8 / 0.8 -> 1.2 | Slam 38 -> 42, Leap 34 -> 38 |
| Revenant | Overhead / Kick | 6 -> 3.5 / 5 -> 3 | 1.5 -> 2.2 / 1.2 -> 1.6 | Overhead 38 -> 44, Stab 24 -> 27 |
| Marionette | Lash / Overhead | 5 -> 3 / 7 -> 4 | 1.6 -> 2.2 / 1.2 -> 1.8 | Lash 30 -> 34, Overhead 40 -> 44 |

Pins: `*DataTests` cooldown/weight/damage asserts for each line. `Projectile Encounter Report` not
required (no speed/interval change; javelin/disc damage is not a solver input).

### 4.5 Punishing player states (only the existing public API)
Readable today: `PlayerCombat.IsDrinking / IsAttacking / IsExecuting / IsStaggered / IsCarried / IsBusy`
(`PlayerCombat.cs:21-30`), `FirstPersonMotor.IsGrounded / AirDashUsed / IsDashing / IsSliding / IsWallRunning /
IsHanging / IsPulling / HorizontalSpeed` (`FirstPersonMotor.cs:309-378, 1756, 1815`), `WandController.WandReady /
CooldownRemaining / CooldownTotal` (`WandController.cs:33-37`; the E cast starts the cooldown, so
`CooldownRemaining > CooldownTotal - 0.1f` is "cast within the last 0.1 s").
- Generalise `TryPunishFlask` (`EnemyController.cs:466`) into `TryPunish(dist, chance)` with three edges, all
  on the existing `FlaskPunishCooldown` 4 s and `PunishFlaskNow` seam: (1) flask (as now), (2) spell cast
  edge -> `EnemyData.spellPunishChance` (0.5 on all seven), (3) spent air dash: `!IsGrounded && AirDashUsed`
  held for >= 0.25 s -> `EnemyData.airPunishChance` (0.6; the pick is biased to far-band entries because a
  player who cannot dash is where a charge lands). `IsPulling` is not punished: the hook is the T1 lesson.
- Test: `SoulsCombatTests.PunishEdgesShareOneCooldown`; `FeatureTests` drives `WandController.TryCycle` + cast.

## 5. Arena minimums (the lead is shrinking `RealmFloorRadius` 25 / shell 37.5)

| ability | assumes | minimum floor radius | notes |
|---|---|---|---|
| Lancer Sky Verdict band 4.5-14, hover 3 m | separation >= 4.5 | 8 | ceiling >= 6 m |
| Lancer/Dancer/Judge Charge far band 4.4-6.5, lunge 3.32 | a 6.5 m line | 8 | |
| Judge Storm ring 3.6 (+2.4 pref +1.5 exit room) | an exit from the ring anywhere | 8 | at R 8 the ring can still fit with the Judge <= 4.4 m off-centre |
| V18 Charge 4.4-7.1, lunge 4.12; Suplex throw 6 m out, lift 11 m | room to land 6 m out | 9 | **ceiling >= 14 m** (11 m lift + 1.8 m eye + margin) |
| Halberdier Charge 5-8 (18), Leap 4.5-8, lunge 4.7 | an 8 m line | 9 | `agent.Move` clamps to the NavMesh at the wall |
| Dancer banks (probe 20 m, ideal wall 6-12 m), volley 4.5-12 | a wall inside 20 m from anywhere | 8 ... 15 (or `bankRange = R + 5` above 15) | shrinking helps her |
| Revenant thrust 3.5-8 | | 6 | |
| Marionette Lash 5+, whirl at 3.9 (3.1 proposed) | | 6 | |
| Ninja Rush 3.4+ | | 5 | |
| **duo realms** (two bodies + player; T4 = Judge ring + V18 throw) | | **12** | recommended floor for all four duo realms: 12-15 m; 12 keeps the T4 pair from stacking the storm on the throw landing |

## 6. Do-first (cheapest, most visible)
1. R1 (one line in `PuppetVisuals.Strike`, one clamp in `EnemyVisuals.LungeCo`): removes the worst thing in the roster (Halberdier charge 45 m/s mesh pop, Marionette pulse, Revenant moonwalk).
2. R3 (`lateStartSpeed 0.7` + idle fill + blend by window): fixes the slow-mo finisher/whirl/bash and the chaining snaps in one function.
3. R2 (lunge window at 9 m/s + hoisted charge staging): ends the treadmill on five bodies and gives the Halberdier the staging the others already have.
Then the reach steps (0.4-0.5 m lunges on the fist bodies, Marionette ring 3.7 -> 2.8), then Section 4 in the order written.

## 7. Conflicts with the lead's concurrent edit
`Assets/Editor/DataFactory.cs` (Sections 2, 4.2, 4.4 name lines the lead is editing this pass: damage, weights, cooldowns),
`Assets/Editor/MiniBossFactory.cs` (R2/R3/R5, 2.2 `spinAttackPrefix`, 2.4 clap ring), the realm constants in
`LevelDefinitionAuthoring.cs:1595` and `SolarArenaPortal.cs` (Section 4.3 and 5), and their tests. All numbers
here are targets to merge after the lead's pass, not a competing edit. `EnemyController.cs` changes (R2, R4,
4.1, 4.3, 4.5) are core-brain edits and belong to the lead by rule.

Model: Fable 5.1 (Claude Code)
