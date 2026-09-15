# Souls AI accuracy spec (Fable, 2026-09-14)

Read-only pass over `EnemyController`, `NavMeshLocomotion`, `EnemyMoveset`, the four Visuals profiles and the
specials components, against the SHIPPED `.asset` YAML (never a C# initialiser). Trigger: the user, after playing:
*"smooth but not actually going towards and directly hitting the player ... actually throw and use magic / specials
... and these specials need a visual cue."* Parry contract untouched: no wind-up under 0.45 s, cue 0.28 s before
impact (`EnemyController.cs:38`). The target is **"a player who does not answer gets hit"** — never homing. A
full-speed strafe (11 m/s, `FirstPersonMotor.cs:32`) must still dodge every cone under 120 deg.

Parkour enemies (`rangedOnly` sentries) are out of scope. The Warden's moveset is not touched (`BossController`
overrides `ChooseCombo`; the user's rule). The four gated duels stay exactly where they are.

## 0. Audit tables (shipped numbers)

### 0.1 Spacing per body (`Assets/Data/Enemies/souls_enemies/*.asset`)

| body | moveSpeed | turnSpeed x windupTurnMult = wind-up peak deg/s | preferred + commitTolerance = **commit edge** | lungeMin | stepSpeedMult | aggression |
|---|---|---|---|---|---|---|
| Seraph Lancer | 4.8 | 320 x 0.30 = 96 | 2.6 + 0.3 = **2.9** | 0.9 | 0.55 | 0.70 |
| Orbit Dancer | 5.4 | 380 x 0.30 = 114 | 2.2 + 0.3 = **2.5** | 0.9 | 0.62 | 0.85 |
| Cinder Judge | 4.6 | 300 x 0.30 = 90 | 2.4 + 0.3 = **2.7** | 0.9 | 0.55 | 0.80 |
| V18 Grappler | 5.2 | 340 x 0.30 = 102 | 2.0 + 0.3 = **2.3** | 0.9 | 0.60 | 0.90 |
| Argent Halberdier | 5.8 | 320 x 0.28 = 90 | 3.2 + 0.4 = **3.6** | 1.2 | 0.55 | 0.90 |
| Ember Revenant | 3.4 | 220 x 0.35 = 77 | 3.4 + 0.7 = **4.1** | 1.6 | 0.35 | 0.55 |
| Pale Marionette | 5.0 | 300 x 0.28 = 84 | 3.7 + 0.7 = **4.4** | 2.8 | 0.30 | 0.62 |
| Legendary_Ninja | 6.4 | 430 x 0.36 = 155 | 3.2 + 0.7 = **3.9** | 1.05 | 0.50 | 1.00 |
| Grunt / Heavy | 5.6 / 3.9 | 128 / 57 | **3.6** / **4.3** | 1.1 / 1.3 | 0.38 / 0.32 | 0.90 / 0.75 |

The NavMesh stopping distance is `max(attackRange x 0.95, preferredRange)` = `preferredRange` on every body
(`EnemyController.cs:333-336`). The commit check runs every Chase frame at `dist <= edge` (`:399`), so an enemy
walking in **commits on the first frame it crosses the edge, never at preferredRange**. Every distance below
starts from the edge.

### 0.2 Reach arithmetic per attack (`Assets/Data/Attacks/*.asset`)

`allow = range + 0.5` (`DoImpact`, `:844`); `T = windup + impactDelay`; the lunge runs from `impact −
LungeWindow(lunge, 0.28)` and stops at `lungeMin` (`:665-671`, `:818-828`); no turning at all in Strike (`:437-444`).

| attack | T s | range -> allow | lunge | cone | from edge, standing: impact dist | margin |
|---|---|---|---|---|---|---|
| Lancer Jab2 / Swing / Finisher | 0.55 / 0.60 / 0.71 | 2.45->2.95 / 2.55->3.05 / 2.6->3.1 | 0.5 / 0.5 / 0 | 65 / 70 / 70 | 2.4 / 1.9 / 1.9 | 0.55 / 1.15 / 1.2 |
| Lancer Stab / Kick / Heavy | 0.65 / 0.65 / 1.13 | 2.9->3.4 / 2.45->2.95 / 2.7->3.2 | 0 / 0 / 0.75 | 35 / 55 / 80 | 2.9 / 2.9 / 2.15 | 0.5 / **0.05** / 1.05 |
| Dancer Jab2 / Stab / Heavy | 0.50 / 0.55 / 1.03 | 2.3->2.8 / 2.4->2.9 / 2.65->3.15 | 0.5 / 0.5 / 0.75 | 65 / 40 / 80 | 2.0 / 2.0 / 1.75 | 0.8 / 0.9 / 1.4 |
| Judge Jab2 / Kick / Heavy / Bash | 0.55 / 0.70 / 1.13 / 0.81 | 2.35->2.85 / 2.45->2.95 / 2.7->3.2 / 2.6->3.1 | 0.5 / 0.5 / 0.75 / 0 | 65 / 55 / 80 / 70 | 2.2 / 2.2 / 1.95 / 2.7 | 0.65 / 0.75 / 1.25 / 0.4 |
| V18 Jab2 / Overhead / Dash / Grab | 0.55 / 1.03 / 0.65 / 0.90 | 2.35->2.85 / 2.55->3.05 / 2.6->3.1 / 2.4->2.9 | 0.4 / 0.4 / 1.77 / 0 | 65 / 70 / 45 / 60 | 1.9 / 1.9 / 0.9 / 2.3 | 0.95 / 1.15 / 2.2 / 0.6 |
| Halberdier Sweep / Thrust / Slam / Charge | 0.65 / 0.54 / 1.01 / 0.86 | 2.9->3.4 / 2.7->3.2 / 2.8->3.3 / 3.0->3.5 | 1.0 / 1.3 / 1.1 / 4.7 | 120 / 36 / 60 / 40 | 2.6 / 2.3 / 2.5 / (5-8 m band) 1.2-3.3 | 0.8 / 0.9 / 0.8 / ok |
| Revenant Slash / Stab / Overhead | 0.67 / 0.59 / 1.02 | 3.4->3.9 / 3.9->4.4 / 3.6->4.1 | 0.9 / 1.5 / 1.3 | 95 / 40 / 70 | 3.2 / 2.6 / 2.8 | 0.7 / 1.8 / 1.3 |
| Marionette SpinUp / Overhead / Lash | 0.84 / 1.07 / 1.13 | 3.9->4.4 / 3.7->4.2 / 8->8.5 | 0.55 / 1.7 / 0 | 200 / 65 / 175 | 3.85 / 2.8 / 4.4 | 0.55 / 1.4 / 4.1 |
| Ninja Cut / Rush; Grunt Jab; Heavy Sweep | 0.49 / 0.53; 0.49; 0.60 | 3->3.5 / 3.4->3.9; 2.6->3.1; 3.1->3.6 | 0.9 / 2.0; 0.7; 0.8 | 75 / 60; 70; 110 | 3.0 / 1.9; 2.9; 3.5 | 0.5 / 2.0; **0.2**; **0.1** |

**Against a player standing still, every blow lands.** Reach is not the bug (the 09-14 spatial pass already put
0.4-0.5 m lunges on the fist bodies). The misses come from motion — theirs and the player's.

### 0.3 Specials: bands vs where the fight actually sits (`Assets/Data/Movesets/souls_enemies/*.asset`)

The fight sits at 2-3 m: Recover steps the body back to `preferredRange` (`Reposition`, `:621`), a deflect
recoils it 0.35 m (`:917`), and the player, in first person, drifts backward to see the whole body.

| boss | special | band | weight / cooldown | near-pool share (player <= edge) | fires in a 40 s fight (est.) |
|---|---|---|---|---|---|
| Lancer | SKY VERDICT | 4.5-14 | 2.6 / 8.5 | **0 %** (edge 2.9) | 0-1, only if you run > 4.5 m and he happens to face you |
| Lancer | SHOULDER CHARGE (red) | 4.4-6.5 | 2.2 / 6 | 0 % | ~0 on approach (see cause 1) |
| Dancer | ORBIT STORM | 4.5-12 | 2.6 / 4 | **0 %** (edge 2.5) | 0-1 |
| Dancer | SPIN THROW (whirl + discs) | 0-3.2 | 1.5 + 1.6 / 5, 7 | 32 % | 4-5 (fine) |
| Judge | STORM JUDGEMENT | 0-4.5 | 1.6 / 9 | 12 % | 3-4 (fine) |
| Judge | AEGIS -> bash -> Heavy | 0-3.6 | 1.8 / 7 | 14 % | 4 (fine) |
| V18 | SKYFALL SUPLEX (red) / CLAP / DASH (red) | 0-2.8 / 0-3.8 / 2.6-4.7 | 2 / 6, 1.5 / 5, 2.6 / 0 | 15 / 11 / far-branch only | 4 / 4 / 2-3 (fine) |
| Halberdier | LEAP / CHARGE x3 | 4.5-8 / 5-8, 5-18 | 1.8 / 3.5; 5, 4, 2 / 0 | 0 % (edge 3.6) | 1-2, only after you back off |
| Revenant | thrust from range | 3.5-6 | 1.5 / 0 | ~9 % at 3.5-4.1 | 2-3 |
| Marionette | STRING LASH (red) | 5-99 | 2.2 / 3 | 0 % (edge 4.4) | 1-2 |

Phase 2 doubles signature weights (`DataFactory.BossKit`) but cannot make a 4.5 m band contain a 2.6 m fight.

## 1. Why blows miss (ranked by how much of the user's complaint each explains)

**C1 — HIGH. Beyond `preferredRange x 1.6` the body never turns toward the player.** `Chase` calls `FaceTarget`
only when `dist <= preferredRange * 1.6f` (`EnemyController.cs:393`); `NavMeshLocomotion.Configure` sets
`agent.updateRotation = false` (`NavMeshLocomotion.cs:37`); the only other root-rotation writer is `BeginExecuted`
(`:1070`). So from aggro (18 m) down to 4.2 m (Lancer), 3.5 m (Dancer), 3.7 m (V18) the enemy **walks its path
with a stale facing** — the forge Run clip plays under a body sliding sideways or backwards. That is the literal
"not going towards the player". It also kills the far-band gate: the charge/verdict branch needs facing
`<= 25 deg` (`:420`), which is only true by accident outside the turn radius, so **the opening charge never fires
on approach** and the Lancer's rise almost never does.

**C2 — HIGH. Combos are thrown in place.** `NextHitOrRecover -> BeginWindup(next hit)` (`:855-861`) has no
distance check; only `CanResumeCombo` after a deflect has one (`:686`). `SetState(Windup)` stops locomotion
(`:891`) and nothing moves the body until the cue. A player who takes ONE step back after hit 1 watches hits 2
and 3 swing at air from a body that stands still — the "not going towards" of the close fight.

**C3 — HIGH. Any backward drift dodges everything from the edge.** With the body stopped for the whole wind-up and
a fixed-distance lunge, the impact distance is `edge + v_back x T − lunge`. Lancer Jab2 needs `v_back <= 1.0 m/s`
to land (2.9 + 0.55v − 0.5 <= 2.95); Heavy `<= 0.9`; Dancer Jab2 `<= 1.6`; V18 Jab2 `<= 1.7`; Halberdier Sweep
`<= 1.2`; Grunt Jab `<= 0.4`. There is no backpedal speed multiplier on the motor (11 m/s in every direction), so
the natural first-person "give myself room" drift of 1-2 m/s defeats every non-charge attack in the roster. And
because Chase speed (4.6-5.8) barely exceeds a 4 m/s walk, the enemy re-enters the band at the edge, commits,
whiffs, recovers, repeats: the loop the user is describing.

**C4 — MED. Facing freezes at the wind-up's end, not at the cue, and freezes wherever the eased tracker left it.**
Windup tracks at `turnSpeed x 0.3 x ease`, `ease = clamp01(0.25 + angle/45)` (`:600-609`); Strike does not turn.
A 4 m/s walker at 2.9 m sweeps 79 deg/s; the Lancer's tracker settles at the angle where `96 x ease = 79`, i.e.
**26 deg behind**, plus 4-6 deg of drift over `impactDelay`. Jab2's 65 deg cone (half 32.5) still catches him;
Stab 35, Kick 55, Judge/V18 at 85-100 deg/s (settle 31-33 deg) miss a *walking* player. Walking should be hit.

**C5 — MED. The lunge is aimed at `transform.forward` at the cue, not at the player** (`:825`). Combined with C4
the lunge carries the body 0.5-1.3 m along a line 26-33 deg off a walking player. The direction MUST still freeze
(a strafe must dodge) — it just needs to freeze on a better line.

**C6 — LOW.** Move bands wider than the reach (`Revenant slash 0-6` vs reach 3.9 + 0.9; `Marionette overhead
0-99`; `Ninja cut 0-99`; `Halberdier SHOULDER CHARGE 5-18`) let the far-branch and the flask punish throw a blow
that cannot arrive. Only matters once section 2 evaluates bands at a *predicted* distance.

Not a cause: `lungeMinDistance` (0.9-1.6 is under every allow), the 50 deg near gate (the tracker recovers 50 deg in
~0.5 s against a standing player), `MaxSimultaneousAttackers 1` (`GameFeel.asset:15`).

## 2. Accuracy fixes (brain, lead-only)

Design rule for all of them: **the enemy may close, aim and snap only up to the cue; from the cue the swing is a
frozen commit.** Walk = hit, run = dodge, backpedal = eat the closer.

### 2.1 Fixes

| # | fixes | change (exact) | formula |
|---|---|---|---|
| A1 | C1 | `EnemyController.cs:393`: drop the `1.6x` radius. Chase faces the player at full `turnSpeed` whenever it is inside `aggroRange`; while path-following outside 1.6x, face the **path direction** instead (`locomotion.Velocity` — add `Vector3 Velocity { get; }` to `IEnemyLocomotion`, `agent.velocity` / root delta). | `faceDir = dist <= pref*1.6 ? toP : (loco.Velocity.sqrMagnitude > 0.04 ? loco.Velocity : toP)` |
| A2 | C2, C3 | **Stalk-in during the wind-up.** In `case State.Windup`, before the cue only: `if (!cued && dist > StalkTarget(attack)) locomotion.Nudge(toP.normalized * StalkSpeed * dt)`. Pure helpers. Combo hits 2/3 get it for free (they re-enter Windup). | `StalkSpeed = data.moveSpeed * data.stepSpeedMultiplier` (Lancer 2.64, Dancer 3.35, Judge 2.53, V18 3.12, Halb 3.19, Rev 1.19, Mar 1.5, Ninja 3.2, Grunt 2.1). `StalkTarget = max(data.lungeMinDistance, atk.range − atk.lungeDistance − 0.2)` (Lancer Jab2 1.75, Heavy 1.75; Halb Sweep 1.7; Rev Overhead 2.1). Stalk seconds available = `T − LungeWindow(lunge)`: Jab2 0.27 s, Heavy 0.85 s, combo hit 2 ≈ 0.41 s. |
| A3 | C3 | **Predicted-distance commit gate.** In Chase (near branch, far branch, `TryChainPhrase`, `TryPunish`): after `ChooseCombo`, refuse the commit when the first hit cannot arrive; while refused and in band, **press** (`Nudge` toward the player at `moveSpeed`, overriding the stopping distance) — the agent otherwise parks at `preferredRange` and never gets closer. | `radial = clamp(dot(playerVel_xz, −toP.normalized), −LeadCap, LeadCap)` (retreat > 0), `LeadCap = 6`. `predicted = dist + radial*T − StalkSpeed*max(0, T − LungeWindow) − lunge`, floored at `lungeMin`. `CanLand(atk) = atk.range <= 0 \|\| predicted <= atk.range + 0.5`. |
| A4 | C3, specials | **Select at the predicted distance.** `ChooseCombo(selectDist)` with `selectDist = dist + radial * SelectHorizon`, `SelectHorizon = 0.6` (the median wind-up). A retreating player is *seen* at 4-6 m, where the closers and the magic live (Charge 4.4-6.5, Verdict, Storm, Leap, Lash, Rush, Revenant thrust). Standing/approaching player: `selectDist == dist`, behaviour unchanged. | Lancer vs 4 m/s backpedal from 2.9: `selectDist = 5.3` → Charge/Verdict eligible; Charge predicted `2.9 + 4x1.03 − 2.64x0.66 − 3.32 = 2.1 <= 3.5` ✓. |
| A5 | starvation | Edge-dancer timeout: if `CanLand` has refused for `RefuseTimeout = 1.2 s` continuously inside the band and no eligible entry lands, commit the eligible entry with the largest `range + lunge` anyway (a Grunt has no closer; a whiff after 1.2 s of pressing is honest pressure, not a loop). | `refusedSince` timestamp, reset on any commit or on leaving the band. |
| A6 | C4, C5 | **Commit snap + led aim at the cue, then freeze.** In `FireCue`, before `visuals.CueFlash`: `aim = player.pos + ClampMagnitude(playerVel_xz, LeadCapLateral) * (projectedImpact − now)`; `locomotion.FaceTowards(aim − pos, CommitSnapDeg / dt)` capped so the yaw jump is `<= CommitSnapDeg`; `CommitLunge` uses `lungeDir = (aim − pos).normalized`; Windup after the cue and Strike never turn (already true for Strike; add `if (!cued)` to the Windup `FaceTarget`). | `LeadCapLateral = 3 m/s`, `CommitSnapDeg = 25`. Lead angle at 2.9 m = `3x0.28/2.9 rad = 16.6 deg`. |
| A7 | C6 | Band hygiene, data-only (worker lane B): `maxRange <= range + 0.5 + lunge + StalkAllowance(1.0)` for every entry whose first hit has `range > 0`. | pinned by a new pure test over every shipped moveset. |

Order inside `FireCue` matters for the sigil (section 4): snap → `CommitLunge` (if due) → `CueFlash`, so the lane
freezes on the committed direction.

### 2.2 Hit / miss, current → proposed (edge commit; strafe lag = tracker settle + post-cue drift; "M x" = miss by x m or deg)

| boss / attack | stand | back 2 | back 4 | back 11 | strafe 4 | strafe 11 | **min lateral dodge** | **min retreat that avoids the melee** |
|---|---|---|---|---|---|---|---|---|
| Lancer Jab2 (65) | ✓ → ✓ | M 0.55 → ✓ (2.88) | M 1.65 → refused, **Charge lands** (2.1) or Verdict | M → refused, Verdict (javelins) | 30 deg ✓ (by 2) → 6 deg ✓ | M 100 deg → M 52 deg | 9.0 m/s | 2.4 m/s (then the red charge) |
| Lancer Heavy (80) | ✓ → ✓ | M 0.75 → ✓ (2.45) | M 3.5 → refused, Charge | M → Verdict | 32 ✓ → 3 ✓ | M → M 78 | 7.5 m/s | 2.7 m/s |
| Lancer Stab (35) | ✓ (0.5) → ✓ | M → ✓ | M → refused | M → Verdict | M 30 vs 17.5 → 6 ✓ | M → M | 6.5 m/s | 2.4 m/s |
| Dancer Jab2 (65) | ✓ → ✓ | M 0.2 → ✓ (2.26) | M 1.2 → refused, Charge / **Storm** (band 2.8+, sec. 3) | M → Storm | 30 ✓ → 7 ✓ | M → M 57 | 8.3 m/s | 2.7 m/s |
| Judge Jab2 (65) | ✓ → ✓ | M 0.45 → ✓ | M 1.55 → refused, Charge | M → Charge refused, press | **M 35 vs 32.5** → 12 ✓ | M → M 61 | 8.5 m/s | 2.6 m/s |
| Judge Storm (360, ring 3.6) | ✓ → ✓ | ✓ → ✓ | M (7.1 at impact) → refused unless predicted <= 3.6 | M → M | ✓ → ✓ | ✓ (ring) → ✓ | leave the ring | 1.6 m/s |
| V18 Jab2 (65) | ✓ → ✓ | M 0.25 → ✓ | M 1.25 → refused, **Dash lands** (1.98, red) | M → Charge / press | **M 37 vs 32.5** → 15 ✓ | M → M 77 | 8.0 m/s | 3.0 m/s |
| V18 Grab (60, red) | ✓ (0.6) → ✓ | M 1.2 → ✓ (stalk 0.62 s x 3.12 = 1.9) | M → refused, Dash | M → Charge | 28 ✓ → 5 ✓ | M → M | 7.0 m/s | 4.1 m/s |
| Halberdier Sweep (120) | ✓ → ✓ | M 0.5 → ✓ | M 1.8 → refused, **CHARGE lands** (1.26 → lungeMin 1.2, red) | M → Leap refused, press | 24 ✓ → 4 ✓ | 80 deg M → 42 deg ✓ **(geometry 3.39 vs 3.4: a hair)** | ~12.7 m/s (a 120 sweep is parried or out-ranged, not strafed) | 2.6 m/s |
| Revenant Slash (95) | ✓ → ✓ | M 0.7 → ✓ | M 2.0 → refused; **no closer reaches** (3.4 m/s body) → press, loses | M → loses | 25 ✓ → 4 ✓ | M → M | 9.5 m/s | 2.5 m/s (he cannot catch 4; the wall does) |
| Marionette SpinUp (200) | ✓ → ✓ | M 1.1 → ✓ (stalk 0.56 s x 1.5) | M → refused, **Lash lands** (7.6 <= 8.5) | M → Lash refused | ✓ → ✓ | ✓ (cone 200) → ✓ | out-range (4.4 m) | 2.2 m/s |
| Ninja Cut (75) | ✓ → ✓ | M 0.5 → ✓ | M 1.5 → refused, **Rush lands** (3.22) | M → refused, press (6.4 m/s catches 4, not 11) | 27 ✓ → 3 ✓ | M → M | 9.5 m/s | 2.6 m/s |
| Grunt Jab (70) | ✓ (0.2) → ✓ (0.65) | M 0.8 → ✓ | M 1.8 → refused, press (5.6 vs 4 closes 1.6 m/s, lands in ~0.8 s) | M → M | 26 ✓ → 4 ✓ | M → M | 9.0 m/s | 3.2 m/s (and he catches you) |

Reading the table: **standing and walking players get hit; a sprinting strafe still dodges every cone under 120;
a backpedal is answered by the red closer** (charge / dash / rush / lash) instead of a whiff. The Revenant is the
one body that cannot catch a 4 m/s retreat — by design (3.4 m/s); the 18 m realm and `wallBias` do that job.

## 3. Specials usage fixes

A4 alone turns every retreat into a Charge / Verdict / Storm / Leap / Lash / Rush pick. The remaining gap is the
player who **stays** at 2-3 m and trades: the Lancer and the Dancer never cast at that distance.

| boss | change | file / anchor | why it is honest |
|---|---|---|---|
| Lancer | SKY VERDICT `minRange 4.5 -> 2.5` | `DataFactory.cs:2589` | He rises 3 m: from 2.5 m the muzzle is 4.5 m up, pitch 61 deg, path 5.1 m; `ProjectileMath.LaunchSpeed` floors the flight at 0.40 s (12.8 m/s < 15) so the cue still has 0.12 s of slack; the parry facing test is horizontal (`PlayerCombat.cs:100`), so "look up" is the lesson, not a requirement. `hoverTrackDegPerSec 220` (`MiniBossFactory.cs:530`) holds a 4 m/s walker at 2.5 m (92 deg/s). |
| Dancer | ORBIT STORM `minRange 4.5 -> 2.8`, `wallBias 0.5` kept | `DataFactory.cs:2347` | Bank probes (`OrbitDancerDiscs.cs:304-330`) are muzzle-relative and find a wall at any range in an 18 m realm; the direct disc at 2.8 m flies 0.40 s (floor), cued. The DiscThrow clip's baked hop-back plays after the release. |
| Judge | none to bands; STORM `wallBias 2.0` kept; Aegis fine | — | Both already live in the near pool (0.3 / 0.5 fires per 10 s). |
| V18 | DASH `minRange 2.6 -> 2.3` (= his edge, so it is the retreat answer from frame 1) | `DataFactory.cs:1953` | Dash lands from 2.3 at 4 m/s retreat (table). |
| Halberdier | LEAP `4.5-8 -> 3.8-8`; SHOULDER CHARGE `5-18 -> 5-9` (spatial spec, still open) | `DataFactory.cs:1439-1440` | Leap from 3.8: 3.8 − 2.4 = 1.4 >= lungeMin 1.2, <= allow 3.3. |
| Revenant | thrust from range `3.5-6 -> 3.5-6.5`; slash / slash-slash / slash-into-thrust `maxRange 6, 6, 7 -> 5.5`; kick 5 -> 4.8 | `DataFactory.cs:~1215-1222` | A7 hygiene: slash reach 3.9 + 0.9 + 1.0. |
| Marionette | spins `maxRange 6 -> 5.9`; OVERHEAD / HELD `99 -> 6.9` | `DataFactory.cs:~1040-1046` | A7. Lash stays 5-99 (range 8, cone 175). |
| Ninja | cut strings `99 -> 5.4`; rush `3.4-99 -> 3.4-6.9` | `DataFactory.cs:~735-745` | A7. |
| Grunt / Heavy | strings `99 -> 4.8` / `5.4`; Heavy step-overhead `2.8-99 -> 2.8-5.9` | `DataFactory.cs:~210-230, 305-325` | A7. |

**Special pressure (optional, needs ONE new field — proposing and stopping, per the mandate).** If after A4 and the
band changes a playtest still shows a boss trading jabs for 15 s, add `EnemyData.signaturePressureSeconds` (0 =
off; bosses 9) and in `EnemyMoveset.WeightedPick` multiply the weight of every `cooldown > 0` entry by
`PressureWeight(sinceLastSignature, pressure) = 1 + 3 x clamp01((since − pressure) / pressure)` (pure). No new
input, resource or screen; it reshapes existing weights. Not in this pass unless the lead says so.

Backstep-then-cast is **not** proposed: `backStepSpeedMultiplier` 0.35 x 4.8 = 1.7 m/s would take 1.5 s to open
2.5 m, and the Lancer's rise / the Dancer's hop already ARE the room-making; lowering the bands is the smaller diff.

`Projectile Encounter Report`: **not required** by anything above (no `projectileSpeed`, interval or cue change;
`ParryModuleSolver` inputs untouched). Re-run only if the lead changes Lancer 15 / Dancer 16 m/s.

## 4. Special cue spec (presentation only; worker lane A)

What exists: the base darkening + red `alertMarker` for unblockables (`EnemyVisuals.cs:395-407`), the instant
`CueFlash` snap/flare/sparks (`:415-435`), the Judge's growing storm ring (`CinderJudgeStormFx`, "the ring IS the
tell"), the Dancer's hand crackle, the Lancer's rise + wings, the charges' one push-off ring at wind-up start
(`CinderJudgeVisuals.cs:212`, then 0.6 s of nothing while the body idles). What is missing is a **shared** tell
that says *what shape is coming and where*, from wind-up start, on every special.

**The Signature Sigil** — one new sealed class `EnemySigilFx` (modelled on `CinderJudgeStormFx`: LineRenderers on a
scene-level root, URP/Unlit additive, normalised under the 1.05 bloom threshold), driven from the **base**
`EnemyVisuals.Telegraph / CueFlash / Strike / ClearTelegraph / Recoil / Die` so every body (puppet and primitive)
speaks it. The shape is **derived from the attack data, no new field**:

| shape | when (pure `SigilShape.For(atk)`) | geometry | examples |
|---|---|---|---|
| RING | `coneDeg >= 300 && range > 0` | circle at `range + 0.5` on the floor under the enemy's feet | Judge Storm (already drawn by its profile: `DrawsOwnSigil` returns true, skip), Halberdier Spin 4.2 m, Dancer SpinThrow 3.1, Marionette spins 4.4, V18 Clap (cone 180 -> FAN, below) |
| FAN | `90 <= coneDeg < 300 && (windup >= 0.9 \|\| unblockable)` | arc of `coneDeg` at radius `range + 0.5`, apex at the feet, tracking facing until the cue | Marionette Lash (175 deg, 8.5 m), V18 Clap (180, 4.1), Knight Vent, Ninja Reap (140, red) |
| LANE | `coneDeg < 90 && lungeDistance >= 1.5` | floor strip from the feet, width 1.0 m, length `lunge + range + 0.5`, tracking facing until the cue, then **frozen** on the committed direction with a bright end-cap | every ShoulderCharge (Lancer/Dancer/Judge 6.5 m, V18 7.3, Halberdier 8.2), V18 Dash 4.9, Halberdier Leap 5.7, Revenant Stab 5.9, Drill Lunge |
| DISC (short red) | `unblockable && lungeDistance < 1.5 && coneDeg < 300` | small ring at `range + 0.5`, red | V18 Grab 2.9, every Kick |
| none | jabs, swings, stabs, finishers (`windup < 0.9`, blue, no lunge) | — | the base tell is enough; the sigil must stay rare to stay loud |

No-contact stances (`range <= 0`: Sky Verdict, DiscThrow, ShieldRaise) draw nothing on the floor; they get a **hand
charge** instead: `SlashFx.Flare` at the muzzle bone growing 0.08 → 0.30 over the wind-up in the boss accent (the
Dancer's crackle already does this; the Lancer's RightHand gets the same, gold; the javelins' own 0.28 s cue is the
parry signal and is untouched).

**Timing** (relative to the brain's clock; the standard cue at impact − 0.28 s is unchanged and still the only
"press now"):

| t | sigil |
|---|---|
| wind-up start (`Telegraph`) | appears at alpha 0.25, geometry grows from 30 % to 100 % over `min(0.5 x windup, 0.45 s)` (the storm ring's rule) |
| wind-up | LANE/FAN follow `transform.root.forward` (the brain's 0.3x tracker), RING/DISC follow the feet |
| cue (`CueFlash`) | alpha 0.25 → 0.6 in one frame (instant, no easing — contract rule 2), LANE end-cap flares (`SlashFx.Flare` 0.3 m, peak 1.3, under the alert tell 3.0 and the parry glow 3.2), geometry **frozen** (after the brain's commit snap, section 2.1 order) |
| `Strike` | fade over 0.10 s; RING/FAN with `strikeDuration > 0.5` hold until the strike ends |
| `ClearTelegraph` / `Recoil` / `Slump` / `Die` | gone at once |

**Colour**: hue = `SlashFx.NormaliseColor(EnemyData.emission)` (Lancer gold, Dancer teal, Judge ember, V18 bone,
Halberdier silver-blue, Revenant ember-red, Marionette pale violet) — no new colour field; **every unblockable sigil
is the alert red `(1.0, 0.2, 0.15)`** regardless of body, because red already means "move, do not parry".
Alpha caps: ring 0.6, fill none (lines only: ≤ 4 LineRenderers per body, one material per body, pooled on the
scene root like the storm). Bloom budget: the sigil never exceeds 1.0 except the one-frame end-cap flare at 1.3.

**Audio**: one sting at wind-up start for any attack that draws a sigil — reuse `Sfx.Tension` at 0.55 / pitch 0.8
(no enum change; a dedicated sound is the audio-engineer's call, `Sfx` is append-only).

## 5. Work split — disjoint file lists

### LEAD-ONLY lane (brain)
Files: `Assets/Scripts/Enemies/Core/EnemyController.cs`, `Assets/Scripts/Enemies/Core/IEnemyLocomotion.cs`,
`Assets/Scripts/Enemies/Core/NavMeshLocomotion.cs`, `Assets/Scripts/Enemies/Core/PuppetLocomotion.cs` (the
`Velocity` getter), `docs/DATAFLOW.md` (Enemy AI map: the Windup stalk, the predicted gate, the commit snap).
Items: A1-A6. Pure statics to add in `EnemyController` for the tests: `StalkSpeed(moveSpeed, stepMult)`,
`StalkTarget(lungeMin, range, lunge)`, `PredictedImpactDistance(dist, radial, T, stalkSpeed, stalkSeconds, lunge,
lungeMin)`, `CanLand(predicted, range)`, `SelectDistance(dist, radial, horizon)`, `CommitAim(playerPos, playerVel,
pos, leadCap, seconds)`, `SnapYaw(current, wanted, maxDeg)`.
Acceptance: `Assets/Editor/Tests/souls_enemies/SoulsCombatTests.cs` (lead-owned for this pass) gains
`AWalkingBackpedalIsRefusedAndTheCloserIsSelected` (Lancer numbers from 2.2), `AStandingPlayerIsAlwaysLandable`
(every shipped attack, edge commit, predicted <= allow), `ASprintStrafeStillExitsEveryConeUnder120`
(residual angle formula from 2.2 at 11 m/s), `TheCommitSnapNeverExceeds25Deg`. Quick EditMode green; FeatureTests
parry harness still parries every attack (it parries on the state transition, so the stalk cannot break it).
Generators: none (no data change in this lane).

### WORKER lane A (Sonnet) — "Signature Sigil" special cues
Files: **new** `Assets/Scripts/Enemies/Core/EnemySigilFx.cs`; `Assets/Scripts/Enemies/Core/EnemyVisuals.cs`
(hooks in Telegraph/CueFlash/Strike/ClearTelegraph/Recoil/Slump/Die + `protected virtual bool DrawsOwnSigil(atk)`);
`Assets/Scripts/Enemies/Core/CinderJudgeVisuals.cs` (override `DrawsOwnSigil` true for the storm — one method);
`Assets/Scripts/Enemies/Core/SeraphLancerVisuals.cs` (the RightHand charge flare during the verdict rise — inside
the existing `skyVerdictAttack` branch); **new** `Assets/Editor/Tests/EnemySigilTests.cs`; `docs/ANIMATION-VFX.md`
(one paragraph). Do NOT touch: `EnemyController.cs`, any locomotion class, `PuppetVisuals.cs`, `OrbitDancerVisuals.cs`,
`FlurryBrawlerV18Visuals.cs`, `DataFactory.cs`, `MiniBossFactory.cs`, any `*DataTests.cs`, `Projectile.cs`,
`CinderJudgeStormFx.cs`.
Acceptance: pure `EnemySigilTests`: `ShapeForEveryShippedAttackIsDerivedNotAuthored` (loads every
`Assets/Data/Attacks/*.asset`, asserts RING/FAN/LANE/DISC/none per the table, and that no blue jab draws one),
`TheLaneLengthIsLungePlusReachPlusSlack`, `AnUnblockableSigilIsAlwaysRed`, `NoSigilMaterialExceedsOne`. Quick
EditMode green; `dotnet build` both assemblies. No generator.

### WORKER lane B (Sonnet) — band hygiene + specials bands (data)
Files: `Assets/Editor/DataFactory.cs` (only the `Entry(...)` / `EntryCd(...)` range arguments named in section 3
and the Halberdier `5-18 -> 5-9`), the pins in `Assets/Editor/Tests/souls_enemies/{SeraphLancerDataTests,
OrbitDancerDataTests,FlurryBrawlerV18DataTests,HalberdierDataTests,RevenantDataTests,MarionetteDataTests}.cs`, and
**new** `Assets/Editor/Tests/souls_enemies/MovesetReachTests.cs`; `docs/DATAFLOW.md` lines 1069-1101 only if a
number quoted there changes (the Verdict / Storm band). Do NOT touch: `EnemyController.cs`, any Visuals class,
`MiniBossFactory.cs`, `SoulsCombatTests.cs`, `Boss_Moveset` / the Warden block, wind-ups, `impactDelay`, `cueLead`,
damage, `projectileSpeed`, any `Level*` file, the `Legendary_Ninja` / Grunt / Heavy *EnemyData* (bands only).
Acceptance: `MovesetReachTests.TheFirstHitOfEveryEntryCanReachItsOwnBandEdge` (every shipped moveset except
`Boss_Moveset`: `maxRange <= range + 0.5 + lunge + 1.0` when `range > 0`), `SkyVerdictAndOrbitStormAreEligibleAtTheFightingDistance`
(`HasEligible(preferredRange + commitTolerance)` includes them). Generators: `3. Create Data` then **3b and 4 in
order** (the 09-14 spec's warning: step 3 alone clears the three item viewmodels). Quick EditMode green.
`Projectile Encounter Report`: not required.

Merge order: lane B (data) → lead (brain) → lane A (presentation, which depends on the `FireCue` ordering in 2.1).
One commit per lane, `[combat-designer]`-prefixed for A/B, lead-prefixed for the brain; tag `pre-accuracy-2026-09-14`.

## 6. Risks

1. **Readability inside 2 m.** The stalk brings scale-1 bodies to ~1.75 m at the blow (Sekiro distance) but the
   1.15-scale Halberdier/Marionette to 1.7-2.1 m. If a wind-up stops reading, raise `StalkTarget`'s slack 0.2 → 0.5
   on the big bodies; never touch the wind-up. Playtest call.
2. **Duo arbitration.** The press (A3) moves a body that is *not* committed toward the player while its partner
   swings — legal (only Windup/Strike hold the slot) but two bodies can crowd. Keep `ReadyPosition` for the one
   without the slot (already the case: the press only runs when `mayCommit`).
3. **The predicted gate and the flask punish.** `TryPunish` must use the same gate or a punish can whiff exactly
   when it should land; A3 names it. `PunishFlaskNow` (the harness path) stays unconditional.
4. **A refused commit looks like hesitation** if the press is not visible. `PuppetLocomotion` already picks Walk/Run
   from real root speed, so the press reads as a walk-in; the sigil gives the special its own read.
5. **Snap + lead vs the 75 deg parry cone.** A 25 deg yaw jump at the cue is on the enemy, not the player; the
   player's facing test is unchanged. A walker who parries on the cue is at ≤ 12 deg residual — inside.
6. **Charges from 2.9 m** (A4 makes them the backpedal answer): the lunge window is 0.37-0.52 s at 9 m/s; predicted
   arrival clamps at `lungeMin` so a red charge never burrows. If it lands too often, the fair knob is the charge
   entries' weight, not the gate.
7. **Verdict at 2.5 m** puts javelins at 61 deg pitch; `Javelin` homing 40 deg/s over a 0.40 s flight buys 16 deg.
   If the tip visibly misses a walker, the fix is `projectileLead 0.7 → 0.8` on the Lancer — a solver-neutral field.
8. **Test count drift**: lane B rewrites six DataTests pins; lane A adds one fixture. Re-run Quick, then Full, before
   the commit that touches `DataFactory`.

Model: Fable 5.1 (Claude Code)
