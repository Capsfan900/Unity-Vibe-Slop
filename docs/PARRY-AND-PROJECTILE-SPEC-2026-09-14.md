# Parry impact and projectile readability — spec (2026-09-14)

Planner: Fable. Implementers: the lead (Opus 5) for LEAD-ONLY items, Sonnet workers for WORKER lanes.
Plan only; nothing here has been changed. Every number in section 0 was read from the shipped
`.asset` / `.prefab` YAML or a named constant, never a C# field initialiser (hard rule 9).

The user's two asks: (1) "parrying needs the real impact feel of Sekiro / Lies of P"; (2) "make the
enemies' magic projectiles more realistic and voluminous so they are easier to see and parry."

**Fixed contracts, restated so nobody moves them:** wind-ups ≥ 0.45 s, `cueLead` 0.28 s, perfect
window 0.13 / late 0.12 (`Assets/Data/PlayerStats.asset:17-18`), `TimeScaleController` the only
`Time.timeScale` writer, every hitstop `affectsPlayer:false` (rule 1), all combat through
`PlayerCombat.ReceiveAttack` (rule 3), `hitRadius` 1.0 and every projectile speed / interval / cue lead
UNCHANGED (so `ParryModuleSolver` modules and `VibeGame1/Projectile Encounter Report` do not need a
re-run), bloom threshold 1.05, ACES ceiling 2.0, exactly two bloom exceptions (bolt core 1.6, flare core).

---

## 0. Current-state audit

### 0.1 The clock (what hitstop does to the next cue)

| Clock | Source | Behaviour under hitstop |
|---|---|---|
| World (`Time.timeScale`) | `TimeScaleController.Apply` `Assets/Scripts/Core/TimeScaleController.cs:116-131`, min of all requests | 0.02 during the freeze |
| Player motor | `PlayerDelta` `:32-39` | 1.0 — never frozen |
| Enemy brain | `Time.time` / `Time.deltaTime` `Assets/Scripts/Enemies/Core/EnemyController.cs:351,431-434` | frozen with the world |
| Bolt flight and cue | scaled `dt` `Assets/Scripts/Enemies/parkour_enemies/Projectile.cs:349-372` | frozen with the world |
| Parry window (`pressTime`, `elapsed`) | `Time.time` `Assets/Scripts/Player/ParryController.cs:184,250` | frozen with the world |

Perfect staircase today (`GameFeel.asset:16,19,81,82` → `ParryImpulse.WorldScaleAt`
`Assets/Scripts/Feel/ParryImpulse.cs:119-128`): 0.02 for 0.090 s, 0.45 for 0.0385 s, 0.725 for 0.0315 s,
then 1.0. Real time 0.160 s; world time elapsed inside it 0.0018 + 0.0173 + 0.0228 = **0.042 s**; world
time lost **0.118 s** (`LostWorldSeconds`; pinned < 0.13 by
`ParryImpactTests.WholeHitStopCostsLessWorldTimeThanOnePerfectWindow`).

**Why the next cue is safe.** Cue, impact and the window are all judged on the same world clock, so the
freeze stretches them uniformly in real time and never compresses the 0.28 s lead. The earliest a second
parryable impact can follow a deflect is `comboGap` 0.12 + `windup` 0.45 = 0.57 world-s, so its cue is
≥ 0.29 world-s away; a Heavy burst's next contact is +0.40 world-s (cue at +0.12). Both are far beyond
the 0.042 world-s that elapse during the whole staircase, so **no cue can fire inside a hitstop** and the
window a player presses into during the release is longer in real time, never shorter. The only cost
of a longer freeze is fight-wide real-time delay, which is why the spec below does **not** lengthen the
freeze: weight comes from force channels, not more frozen frames (Sekiro's own deflect stop is ~4–5
frames ≈ 0.07–0.08 s; ours is already 0.09).

### 0.2 What each outcome does today

| Layer | Perfect | Blocked (timed press) | Blocked (held guard) | Hit | Player posture break | Enemy posture break |
|---|---|---|---|---|---|---|
| Hitstop | 0.09 @0.02 + release 0.07/0.45 (`PlayerCombat.cs:127`, `ParryImpact.cs:104-114`) | **none** (`PlayerCombat.cs:144-183`) | 0.05 @0.02 (`:247`) | none | none | none (`EnemyController.cs:957-969`) |
| Camera | `Small()` Perlin 0.06 m/0.12 s/25 Hz + Kick pitch −1.6°, yaw ±1.1°, roll ±1.3°, offset 0.035 m, 0.16 s, attack 18 % (`GameFeel.asset:75-79`, `ParryImpulse.cs:81-94`) | `Medium()` 0.14 m/0.20 s | `Medium()` + shove 1.2 m | `Big()` 0.30 m/0.35 s, vignette 0.38/0.45 s, shove 2 m | `Big()`, vignette 0.45/0.6 s (`PlayerPosture.cs:84-86`) | none |
| FOV | −2.2° punch-in, SmoothDamp 0.18 s (`GameFeel.asset:80`, `CameraFX.cs:78`). **Bolts: overwritten the same frame by +4 (`Projectile.cs:442`, `CameraFX.FovKick` assigns, `:40`) — the punch-in never happens on a span** | none | none | none | none | none |
| Viewmodel | `DeflectImpact` 30/20/100 ms, offset (0.055, −0.065, −0.015) m, (9, 0, −7)° (`WeaponViewmodel.cs:558-569`) | **none** | `GuardImpact` 45/130 ms, (0.07, −0.09, −0.02) m, (14, 0, −10)° (`:657-659`) | none | none | — |
| Sparks / shapes | 12 pale-steel @9 m/s, 26° + crescent 0.85 m/110°/0.16 s + teal hoop r 0.62/0.18 s at 1.05 m (54 % of frame) (`PlayerCombat.cs:137-139`, `ParryImpulse.cs:189-199`) | 5 grey @5 m/s (`:179`) | 9 steel @6.5 m/s on the blade (`:259`) | 4 dark red @3.5 m/s | none | slump tilt 0.2 s + sternum mark (`EnemyVisuals.cs:513-524`) |
| Screen flash | white α 0.077 / 0.09 s (`:140`), chroma 0.35 / 0.12 s | hurt α 0.072 / 0.12 s | none | hurt α 0.18 / 0.22 s | **hurt α 0.55 / 0.45 s** (`PlayerPosture.cs:85`) — breaks ANIMATION-VFX rule 3 | none |
| Audio | `Parry` 1.0 @1.0 ±0.08 + `Parry` 0.42 @2.15 + `Block` 0.55 @0.55 (`:141`, `ParryImpulse.cs:287-294`). Folder has 10 real clips `parry_00..09.ogg`; procedural fallback = 480 Hz partials, 20 ms noise, 65 Hz thump, 0.4 s (`ProceduralSfx.cs:176-190`) | `Block` 1.0 @1.0 | `Block` 1.0 @0.85 | `Hurt` | `PostureBreak` (5 clips) | `Stagger`; `PostureBreak` 0.7 @1.35 **only for `rangedOnly`** (`:968`) |
| Enemy side | `EnemyVisuals.Recoil`: body glow 3.2 (the one licensed bloom), flare 0.5 m/0.16 s + 10 sparks at the weapon, visual lunge −0.18 m/0.25 s (`EnemyVisuals.cs:459-477`); brain steps back 0.35 m over `parryRecoilSeconds × lerp(1, 0.55, aggression)` (`EnemyController.cs:161,897-922`; Grunt 0.42 s → 0.21 s); `PuppetVisuals` z −0.18 m, pitch −8°, τ 0.1 s (`PuppetVisuals.cs:834-841`); posture `+parryPostureDamage × 1.2` | `HitFlash` | `HitFlash` | — | — | bar flashes white (`EnemyPostureBar.cs:78`) |
| Rumble | none anywhere (no `SetMotorSpeeds` in the project) | | | | | |
| Streak | HUD text `PERFECT xN` only (`HUDController.cs:414`); nothing in the world escalates | | | | | |

**Diagnosis.** The Perfect package is *legible* but *light*: the camera kick is 1.6° / 3.5 cm (under two
frames of travel), the blade kick is 6 cm, the freeze is fine, and on the span (where most parries
happen) the FOV punch-in is silently lost to the bolt's +4 punch-out. Nothing escalates across a string.
A **timed block has no hitstop and no weapon response at all**, so it reads as "nothing" or as a hit.
An **enemy posture break — the deathblow moment — is nearly silent** for melee enemies (no hitstop, no
camera, no world shape, `PostureBreak` only on turrets). The player's own break buys its weight with a
0.55 screen flash, the one thing rule 3 forbids.

### 0.3 Projectiles today (shipped values)

Angular size: θ ≈ 57.3·D/d degrees; at 1080p and the shipped 95° vertical FOV (`CameraFX.baseFov`),
focal length f = 540 / tan 47.5° = 495 px, so px ≈ 495·D/d. Cue distance = speed × 0.28 s (pure flight;
a runner closing at 11 m/s pushes the blue cue out to ≈ 14 m).

| Projectile | Speed / range | Visible size (m) | Cue flare | Peak | Trail | Halo |
|---|---|---|---|---|---|---|
| Blue sentry bolt `pshooter_enemy01` (`Assets/Data/Enemies/parkour_enemies/pshooter_enemy01.asset:56-58,66-68`) | 40 m/s, 6–32 m | sphere 0.55 × 1.10 = **0.605** (`Projectile.CoreSize`, `ProjectileShooter.cs:738`) | ×2.3 × 1.10 = ×2.53 → 1.53 m, step at cue (`Projectile.cs:357-360`) | `HotCore` (1.6, 0.95, 0.38) → `CueCore` (1.6, 1.45, 1.2) | 7 pts, 0.16 s (6.4 m), 0.45 → 0.03 m linear (`Projectile.cs:42-50,205`) | none |
| Heavy sentry `pshooter_enemy02` (`:56-58,66-68`) | 36 m/s, 6–48 m, bursts of 3 | 0.66 | ×2.645 → 1.75 m | same | 0.50 m head | none |
| Surge turret `pshooter_enemy03` (`:56-58,66-68`) | 36 m/s, 2.5–36 m | 0.74 | ×2.875 → 2.13 m | same | 0.54 m head | none |
| Orbit Dancer disc (`Legendary_OrbitDancer.prefab:1630-1647`, `OrbitDancerDiscs.cs:345-359`) | 16 m/s, 4.5–12 m, ≤ 4 live | cylinder Ø 0.60, **0.012 m thick, axis world-up — edge-on at eye height** (blade child 0.031 m) | ×2.3 × 0.7 = ×1.61 → 0.97 m | rim (0.36, 1.25, 1.15) | 0.55·0.75·0.6 = 0.25 m head, 0.16 s | none |
| Seraph javelin (`Legendary_SeraphLancer.prefab:1750-1763`, `SeraphLancerJavelins.cs:265-285`) | 15 m/s, 4.5–14 m, ≤ 5 live | tip sphere 0.30; shaft 1.6 × **0.05 m**, point-first (foreshortened to the tip) | ×1.61 → 0.48 m tip | tip (1.45, 1.05, 0.40) | 0.21 m head, 0.16 s | none |

| Distance | Blue bolt (uncued / cued) | Heavy | Surge | Disc (face / edge) | Javelin tip / shaft |
|---|---|---|---|---|---|
| max range | 32 m: 1.1° / 9 px | 48 m: 0.8° / 7 px | 36 m: 1.2° / 10 px | 12 m: 2.9° / 25 px face, **0.15° / 1 px edge** | 14 m: 1.2° / 11 px, shaft 0.2° / 2 px |
| 15 m | 2.3° / 20 px | 2.5° / 22 px | 2.8° / 24 px | — | — |
| **cue moment** | 11.2 m: **7.8° / 68 px** | 10.1 m: 9.9° / 86 px | 10.1 m: 12.1° / 105 px | 4.5 m: 12.3° face / **0.6° edge** | 4.2 m: 6.6° / 57 px |
| 8 m (cued) | 11° / 95 px | 12.5° | 15° | 6.9° face / 0.35° edge | 3.4° (uncued 2.1°) |
| 3 m | 29° | 33° | 40° | 18° / 1° | 9° |

**Diagnosis.** The bolt *at the cue* already reads (7.8–12°); what does not read is the **approach**: a
9–24 px amber dot with no aura against `FogColor` #20344D and the cold `AmbientEquator` walls, so the
player sees the shot only when it pops — one beat of information instead of a flight to track. The
**disc is a sub-pixel sliver** for most of its flight (horizontal spin plane at chest height, rule 4). The
**javelin's shaft is 2 px** at range and the whole spear foreshortens to a 0.3 m dot because it flies
point-first. No projectile has any body/volume between the hard core and the thin line.

---

## 1. Parry impact — target spec

Reference reasoning. **Sekiro:** a deflect is a very short hard stop, a bright metallic "kin", a spark
burst at the blade contact, a small camera kick, and an enemy posture bar that visibly *jumps*; strings
of deflects escalate spark size and sit on a rising rhythm. **Lies of P:** a perfect guard has a slightly
longer stop with a brief slow-down after, a heavy blade-on-blade ring with body, and a strong enemy
jolt. Our translation: keep the 0.09 freeze (already Sekiro-scale), keep the stepped release (already
the Lies of P ease), and buy the missing weight in **force** — kick, head sink, FOV, blade, enemy jolt,
spectral audio width — plus a **streak ladder** and a **block that is visibly the lesser event** (a block
drives the view DOWN, a deflect drives it UP; the sign is the distinction).

Streak index `i` = consecutive Perfects within 2.0 s of the last, capped at 4, reset by any Blocked /
Hit (`ParryImpact` keeps it; presentation-only, no new resource; mirrors `HUDController.perfectStreak`).

### 1.1 Perfect (deflect)

| Channel | Today | Target | Where |
|---|---|---|---|
| Freeze | 0.09 @0.02 | **unchanged** | — |
| Release | 0.07, first step 0.45, second 0.725 | **unchanged** | — |
| Camera kick | pitch −1.6°, yaw ±1.1°, roll ±1.3°, offset 0.035 m, 0.16 s, attack 18 % | pitch **−2.4°**, yaw ±1.1°, roll ±1.6°, offset **0.05 + 0.005·i** m (≤ 0.07), **0.14 s**, attack **12 %** (17 ms: one frame out, the hit) | `DataFactory.cs:3053-3057` → `GameFeel.asset:75-79`; `ParryImpulse.KickAttackFraction` |
| Perlin texture | `Small()` 0.06 m / 0.12 s | keep | — |
| FOV | −2.2° | **−3.5 − 0.4·i°** (≤ −5.1, still under the dash's 8); on a bolt the +4 punch-out is **delayed to freeze end** (0.09 s realtime) so in-then-out reads as impact-then-boost | `DataFactory.cs:3058`; `Projectile.cs:442` (worker B, see §3) |
| Blade | (0.055, −0.065, −0.015) m, (9, 0, −7)°, 30/20/100 ms | **(0.07, −0.09, −0.02) m, (14, 0, −10)°**, 30/20/**110** ms, then a **ring tremor**: 2 cycles at 18 Hz, ±0.6° roll, decaying over 0.12 s (the steel rings) | `WeaponViewmodel.DeflectKickOffset`, `DeflectImpactCo` |
| Contact | 12 sparks @9 m/s 26° + crescent + hoop r 0.62 | **18 + 3·i sparks** (≤ 30) @ **12 + i m/s**, 32°; add `SlashFx.Flare(contact, DeflectSteel, 0.55 m, 0.10 s)` (normalised, peak 1.0, no bloom); hoop r **0.62 + 0.045·i** (≤ 0.80, still the smallest wave); crescent unchanged | `PlayerCombat.cs:138` count/speed **LEAD**, flare **LEAD** (one line); hoop `ParryImpulse.ShockRadius` → `ShockRadiusAt(i)` |
| Screen flash | white α 0.077 / 0.09 s | **remove** (rule 3; the flare replaces it). Chroma 0.35 / 0.12 s unchanged | `PlayerCombat.cs:140` **LEAD** |
| Audio | `Parry` 1.0@1.0 + `Parry` 0.42@2.15 + `Block` 0.55@0.55 | `Parry` **1.0 @ 1.0 + 0.05·i** (≤ 1.2; keeps the 480 Hz clang under `ParryCue`'s 1600 Hz band) · bright `Parry` **0.42 @ 2.15 + 0.12·i** · body `Block` **0.55 + 0.08·i @ 0.55 − 0.03·i** (heavier each hit). Jitter unchanged | `ParryImpulse.DeflectLayers` → `DeflectLayersAt(i)` |
| Enemy jolt | visual lunge −0.18 m / 0.25 s; puppet z −0.18, pitch −8° | lunge **−0.30 m / 0.22 s**; puppet **z −0.26 m, pitch −12°**, τ 0.10 s. Brain step-back 0.35 m **unchanged** (it is timing) | `EnemyVisuals.cs:477`, `PuppetVisuals.cs:839-840` |
| Enemy posture bar | fill moves, no flash | **jump**: white flash 0.6 decaying at 4/s on every `Posture.Add` ≥ 5, plus a 0.10 s overshoot of the fill by +6 % (Sekiro's bar kick) | `EnemyPostureBar.cs` (new `OnPostureAdded` subscriber) |
| Rumble | none | low 0.55 / high 0.9 for 0.08 s realtime | **needs a new seam in `InputReader` (only Input System toucher) — propose and stop**, §3 |

### 1.2 Blocked — timed press (late window)

| Channel | Today | Target |
|---|---|---|
| Hitstop | none | **0.05 @0.02**, no release step (`guardHitStop`; it is the same "you did not earn this" beat as the guard) |
| Camera | `Medium()` | `Small()` + Kick pitch **+1.2° (DOWN)**, offset 0.03 m, 0.12 s |
| FOV | none | none (the deflect owns the punch) |
| Blade | none | new `WeaponViewmodel.BlockImpact()`: the guard thud's pose path from the *live* pose, 45/130 ms, (0.07, −0.09, −0.02) m, (14, 0, −10)° — same shape as `GuardImpact` but valid when not guarding |
| Sparks | 5 grey @5 m/s | **8 grey @6 m/s**, 34° |
| Audio | `Block` 1.0@1.0 | `Block` 1.0 @ **0.95** + `Block` **0.40 @ 0.50** (dull body under the scrape); streak resets |
| Flash | hurt α 0.072 | keep |

### 1.3 Blocked — held guard

Keep everything shipped (0.05 stop, 9 steel sparks on the blade, `GuardImpact`, 1.2 m shove, `Block`
@0.85). Add the **downward** kick (pitch +1.2°, 0.12 s) in place of `Medium()` so the guard and the
timed block share one "absorbed" grammar and the deflect alone lifts the view.

### 1.4 Hit — unchanged except the shove rule already shipped. Streak resets.

### 1.5 Posture break beats

**Enemy break (the deathblow opens).** Target: the loudest *non-bloom* beat in a fight.

| Channel | Target | Where |
|---|---|---|
| Hitstop | **0.06 @0.02** requested from the brain's break handler (min-wins with any parry/swing stop already live, so it never adds real time to a Perfect that breaks) | `EnemyController.HandleBroken` **LEAD** |
| Audio | `Stagger` + `PostureBreak` **1.0 @1.0 for every enemy** (drop the `rangedOnly` gate; turrets keep 0.7 @1.35) | `EnemyController.cs:968` **LEAD** |
| World shape | `SlashFx.Ring(chest 1.2 m, up, accent, 1.2 m, 0.25 s)` + `Flare(chest, accent, 0.6 m, 0.14 s)` — the guard shattering, normalised (no bloom); slump tilt 0.2 s unchanged | `EnemyVisuals.Slump(true)` |
| Camera | `Medium()` + FOV −1.5° | `EnemyVisuals.Slump(true)` (presentation hook already called by the brain) |
| Bar | existing white flash, then **hide the fill** for the stagger (the bar is empty; the mark is the prompt) | `EnemyPostureBar.OnBroken` |

**Player break.** Screen flash **0.55 → 0.25 α / 0.30 s** (rule 3), keep `Big()` + vignette, add Kick pitch
**+3° (down)**, offset 0.06 m, 0.25 s — "your guard gave way". `PlayerPosture.Break` **LEAD** (3 lines).

**Deathblow landing** (`ExecuteInteractor.cs:210-216`: 0.14 stop, `Big()`, chroma 1.0, FOV −12, flash
0.5): flash **0.5 → 0.25** (rule 3); otherwise already the biggest beat. **LEAD**, one number.

### 1.6 Tests that pin §1 (`Assets/Editor/Tests/ParryImpactTests.cs`)

Update `ShippedAssetCarriesTheShippedValuesNotTheCodeDefaults` (new kick/FOV numbers),
`KickIsBoundedFarBelowTheAimBudget` (2.4 + 1.1 = 3.5 < 4 ✓, offset ≤ 0.07 < 0.08 ✓),
`FovPunchIsInwardAndSmallerThanADash` (5.1 < 8 ✓), `KickIsOverBeforeTheNextParryCue` (0.14 < 0.28 ✓).
Add: `StreakLadderNeverExceedsTheAimOrHeadBudgetAtCap`, `StreakPitchStaysUnderTheCueBand`
(1.2 × 480 Hz < 1600 Hz), `BlockKickIsDownAndDeflectKickIsUp`, `HoopAtMaxStreakIsStillTheSmallestWave`
(0.80 < 0.85), `NoOutcomeFlashesAboveRuleThree` (every `ScreenFlash` α ≤ 0.25),
`WholeHitStopCostsLessWorldTimeThanOnePerfectWindow` (unchanged, must still pass).

---

## 2. Projectiles — target spec ("volumetric magic")

**Readability target: ≥ 8° of arc at the cue moment, ≥ 2.5° (≈ 22 px) at the shooter's max range.**
Why 8°: the player's eyes are on the route line, not the bolt, so the cue must be a *peripheral* read;
a stimulus of ~6° is detected reliably out to ~20° eccentricity, 8° adds margin, and it is exactly what
the shipped blue bolt already achieves (7.8°) — the one shooter the user has not complained about. Why
2.5° at range: that is where the shot becomes trackable from launch instead of appearing at the cue.

**What stays fixed:** `hitRadius` 1.0, every speed / interval / homing / lead / `cueLead`, `CoreSize`
0.55, `HotCore` 1.6, `CueCore`, the cue step ×2.3, the pre-cue 0.34 m weave, `projectileVisualScale` /
`TrailScale` / `CueScale` data. No `ParryModuleSolver` or Encounter Report re-run is needed.

### 2.1 The shared look (every `Projectile`, built where the "Core" is built)

| Layer | Spec | Budget |
|---|---|---|
| Core | unchanged sphere, `HotCore` 1.6 (the bolt is the one glow in traversal) | exception 1 |
| **Halo** shell | additive sphere child of the visual, **world Ø = 2.2 × core Ø** (blue 1.33 m), URP/Unlit additive via `SlashFx.CreateAdditiveMaterial`, colour = per-enemy accent at **peak 0.24**; counter-scaled at the cue so its *world* size does not inherit the ×2.3 flare (same `HaloWorldDiameter` idiom as `SentryFlare.cs:130-132`) | overlap sum 1.6 + 0.24 + 0.10 = **1.94 < 2.0** |
| **Outer** shell | additive sphere, **3.4 × core Ø** (blue 2.06 m), same hue at **0.10** | in the 1.94 |
| Trail | `LineRenderer.widthCurve` **(0, 0.9) (0.25, 1.0) (0.7, 0.45) (1, 0)** × head width; head width **1.0 × core** (was 0.75); life **0.22 s** (was 0.16; 8.8 m at 40 m/s); **9 points** (was 7). Colour unchanged (core material) | additive over fog only |
| **Wake** (phase 2, only if the trail still reads thin in play) | second line under the trail: 0.30 s, 1.6 × width, halo hue at 0.12 | one extra renderer per bolt |
| Heat distortion / smoke | **not** — needs URP Opaque Texture + a refraction shader (a full-res colour copy per frame) for a 0.6 m object seen at 11 m. The two soft shells are the "volume" | — |
| **Cue swell** | at cue: the existing ×2.3 step + `CueCore` (the punctuation stays binary, same argument as the hitstop onset), halo **0.24 → 0.30**, then the core grows **linearly ×2.3 → ×2.8 over the last 0.28 s** so the parry moment is a visible *swelling*, not a static pop | 1.6 + 0.30 + 0.10 = **2.0** exactly |
| Deflect burst | on Perfect at the bolt: `SlashFx.Flare(bolt, halo hue, 0.7 m, 0.10 s)` + the 18-spark / hoop package from §1 | normalised |
| **Reflected ("yours now")** | core → **`ReflectedCore` (0.66, 0.90, 0.86) at peak 1.0** (the PERFECT teal `#A8E6DA`; **cannot bloom** — it is no longer a tell and must not compete with the next amber cue), halo → teal 0.20, trail teal, trail life 0.28 s, core scale ×1.15 | ≤ 1.05 |
| Bounce / re-cue (disc) | `Redirect` re-arms the swell; the halo returns to 0.24 | — |

### 2.2 Per projectile

| Projectile | Accent (halo / outer) | Extra | Angular size after |
|---|---|---|---|
| Blue sentry bolt | pale amber (1.0, 0.80, 0.50) | — | 32 m: outer 2.06 m → **3.7° / 32 px**; cue 11.2 m: core 1.53 m + halo → **7.8° core, 13° aura** |
| Heavy sentry | ember (1.0, 0.50, 0.25) | bursts: the 3 bolts share one hue | 48 m: 2.6° / 23 px; cue: 9.9° core |
| Surge turret | white-amber (1.0, 0.90, 0.70) | — | 36 m: 3.9°; cue 12.1° |
| Orbit Dancer disc | cyan rim (0.36, 1.25, 1.15) → halo (0.36, 1.0, 0.92) at 0.20 (rim 1.25 + 0.20 = 1.45 < 2.0) | **bank the disc 35° about its flight direction** (`core.rotation = Heading(dir) * Euler(0,0,35)` in `LateUpdate`, spin stays about local Y) so the face shows as an ellipse, minor axis 0.6·sin 35° = **0.34 m**; halo Ø **1.6 × 0.6 = 0.96 m** | 12 m: halo 4.6° / 40 px, face 2.9 × 1.6°; cue 4.5 m: **19.8° aura, 12.3 × 7° face** |
| Seraph javelin | gold (1.45, 1.05, 0.40) → halo (1.0, 0.75, 0.35) at 0.22 (1.45 + 0.22 = 1.67) | shaft Ø **0.05 → 0.09 m** (rule 4: 0.05 is sub-pixel at 14 m); halo on the **tip** only, Ø **3 × 0.30 = 0.90 m**; relic inherits the new diameter automatically (`Javelin.OnWorldContact` measures the shaft) | 14 m: halo 3.7° / 32 px, shaft 0.37° / 3 px; cue 4.2 m: tip 6.6°, **aura 12.3°** |

### 2.3 Performance caps (unchanged, stated)

Sentry bolts: no global cap; ≤ `MaxBurstShots` 3 per Heavy phrase, ordinary sentries one at a time,
Level_01 worst case ≈ 8 live. Discs `liveCap` 4, javelins `liveCap` 5 (prefab), `BouncingDisc.LiveCount`
/ `Javelin.LiveCount`. Per projectile after this spec: 1 core + 2 shells + 1 line (+1 wake) = 4–5
renderers, **no lights, no particles, no material instances beyond the two shared shell materials per
shooter type**, no allocation per frame (shells are built once with the bolt). `SlashFx.MaxLive` 28 is
untouched; the cue swell is a scale write, not an effect.

### 2.4 Tests that pin §2

`ProjectileTests.TheBoltIsTheOneGlowInTraversal` (unchanged, 1.6). New `ProjectileVisualTests.cs`:
`CorePlusShellsStayUnderTheAcesCeilingBeforeAndAtTheCue` (1.94 / 2.0), `CueSwellIsMonotoneAndEndsAtArrival`,
`ReflectedCoreCannotBloom` (peak ≤ 1.05), `TrailWidthCurvePeaksBehindTheHeadAndEndsAtZero`,
`AngularSizeAtTheCueIsAtLeastEightDegreesForEveryShippedShooter` (reads the five `EnemyData` assets:
speed × 0.28 vs core × cue scale + halo), `AngularSizeAtMaxRangeIsAtLeastTwoAndAHalfDegrees`,
`ShellWorldDiameterDoesNotInheritTheCueFlare`, `LogicalHitRadiusAndSpeedsAreUntouched` (asserts the
five shipped speeds / `hitRadius` 1.0 / `CueLead` 0.28 byte-for-byte — the guard that says "no solver
re-run"). `OrbitDancerDataTests`: `DiscBankShowsAtLeastAThirdOfItsFace` (sin 35° ≥ 0.33).
`SeraphLancerDataTests`: `JavelinShaftIsNotSubPixelAtMaxRange` (0.09 m at 14 m ≥ 0.35°).

---

## 3. Work split — disjoint file lists

The lead is concurrently in `LevelDefinitionAuthoring.cs`, the `Level_01` asset/scene and
`SolarArenaTests`; nobody below touches those. Another Fable is planning arena theming / hazards; nothing
below touches arena or hazard files. **`DataFactory.cs` belongs to lane A only** (the projectile lane
changes no data). One commit per lane, `[combat-designer]`-prefixed, `Model:` trailer.

### Lane L — LEAD-ONLY (core)

Files: `Assets/Scripts/Player/PlayerCombat.cs`, `Assets/Scripts/Player/PlayerPosture.cs`,
`Assets/Scripts/Enemies/Core/EnemyController.cs`, `Assets/Scripts/Player/ExecuteInteractor.cs`,
`Assets/Scripts/Core/InputReader.cs` (proposal only).

1. `PlayerCombat.ReceiveAttack` Perfect: sparks 12/9 → `ParryImpulse.SparkCountAt(i)` / `SparkSpeedAt(i)`;
   add the contact `SlashFx.Flare`; **delete** the `ScreenFlash` line (`:140`); call
   `ParryImpact.NoteResult(result)` after the switch (streak feed). Blocked (timed): add
   `TimeScaleController.I.HitStop(feel.guardHitStop, feel.hitStopScale)`, replace `Medium()` with
   `ParryImpact.Block(...)` (lane A's new static), sparks 5/5 → 8/6, `vm.BlockImpact()`, add the body
   layer. Held guard: `Medium()` → `ParryImpact.Block(...)`.
2. `PlayerPosture.Break`: flash 0.55/0.45 → 0.25/0.30; add the +3° down kick.
3. `EnemyController.HandleBroken`: `PostureBreak` for every enemy; request the 0.06 break stop through
   `TimeScaleController.I.HitStop`. `OnParried` **unchanged**.
4. `ExecuteInteractor.cs:215`: flash 0.5 → 0.25.
5. **Proposal, do not build without the user:** `InputReader.Rumble(float low, float high, float
   seconds)` — the one seam (rule 2) that would let `ParryImpact` ask for controller rumble; Perfect
   0.55/0.9/0.08 s, block 0.35/0.2/0.06 s, hit 0.8/0.4/0.18 s, realtime timer, cleared on pause.

Do not touch: anything in lanes A/B/C, `TimeScaleController.cs` (no change is needed — every request
already composes by min), the motor.

Acceptance: `QuickTestRunner.RunQuick()` green; play mode `DebugHarness.Run("parry")` and
`FeatureTests > Guard / Deathblow` green with `GameManager.I != null && Time.timeScale == 1`.

### Lane A — WORKER: parry presentation

Files: `Assets/Scripts/Feel/ParryImpact.cs`, `Assets/Scripts/Feel/ParryImpulse.cs`,
`Assets/Scripts/Player/WeaponViewmodel.cs`, `Assets/Scripts/Enemies/Core/EnemyVisuals.cs`,
`Assets/Scripts/Enemies/Core/PuppetVisuals.cs`, `Assets/Scripts/Enemies/Core/EnemyPostureBar.cs`,
`Assets/Editor/DataFactory.cs` (**lines 3040-3064 only**, the `GameFeel` block),
`Assets/Editor/Tests/ParryImpactTests.cs`.

- `ParryImpulse`: `StreakWindowSeconds` 2.0, `StreakCap` 4, `KickAttackFraction` 0.12,
  `ShockRadiusAt(i)`, `SparkCountAt(i)`, `SparkSpeedAt(i)`, `FovPunchAt(i, base)`, `HeadSinkAt(i, base)`,
  `DeflectLayersAt(i)` (the §1.1 audio ladder), `BlockKick` (+1.2°, 0.03 m, 0.12 s) — all pure, all tested.
- `ParryImpact`: streak state + `NoteResult`; `Deflect` uses the ladder; new `Block(eye, source, feel)`
  (Small + down kick + body layer). Camera stays on unscaled time; every stop stays `affectsPlayer:false`.
- `WeaponViewmodel`: `DeflectKickOffset` numbers, `DeflectKickRecover` 0.11, the 18 Hz ring tremor,
  new `BlockImpact()` (guard-thud path from the live pose, valid unguarded). All on `PlayerDelta`.
- `EnemyVisuals.Recoil` lunge −0.30/0.22; `Slump(true)` ring + flare + `Medium()` + FOV −1.5.
- `PuppetVisuals` recoil 0.26 m / 12°.
- `EnemyPostureBar`: the jump flash on `Posture.Add`, hide fill while broken.
- `DataFactory` feel block: kick pitch 2.4, roll 1.6, offset 0.05, time 0.14, FOV −3.5; the lead re-runs
  `3. Create Data` before committing.

Do not touch: `PlayerCombat.cs`, `ParryController.cs`, `CameraFX.cs`, `CameraShake.cs` (no change
needed: `Kick` already takes the attack fraction), `Projectile*.cs`, `ProceduralSfx.cs`, `HUDController.cs`.

Acceptance: §1.6 tests; in play, three Perfects in a row on a Grunt must sound and kick progressively
harder, a timed block must visibly push the view *down* with a blade thud, and the crosshair hoop must
still be the smallest wave.

### Lane B — WORKER: projectile visuals

Files: `Assets/Scripts/Enemies/parkour_enemies/Projectile.cs` (**presentation members only**:
constants `:37-58`, `BuildTrail/UpdateTrail/ResetTrail`, `SetCoreColor`, the cue block `:349-361`, the
reflect visual lines `:456-462`, `Redirect` visual lines `:507-513`, and `:442` FOV delay — **never**
`Fire`, the flight/forecast/registry code, `Arrive` resolution, `ResolveIncoming`, `Spend`),
`Assets/Scripts/Enemies/parkour_enemies/ProjectileShooter.cs` (`:722-758` bolt build only),
`Assets/Scripts/Enemies/Core/OrbitDancerDiscs.cs` (`:338-372` build), `Assets/Scripts/Enemies/Core/BouncingDisc.cs`
(`LateUpdate` bank), `Assets/Scripts/Enemies/Core/SeraphLancerJavelins.cs` (`:262-300` build; `shaftDiameter`
default 0.09 **and** the shipped `Legendary_SeraphLancer.prefab:1753` via `MiniBossFactory` — the lead
re-runs `4b. Build Mini-Bosses`), `Assets/Scripts/Enemies/Core/Javelin.cs` (halo counter-scale),
`Assets/Editor/Tests/parkour_enemies/ProjectileVisualTests.cs` (new),
`Assets/Editor/Tests/souls_enemies/OrbitDancerDataTests.cs`, `SeraphLancerDataTests.cs`.

- A small shared helper *inside `Projectile`* (`BuildShell(parent, scale, colour)`,
  `ShellWorldDiameter`, `CueSwellScale(t)`) so the three builders call one thing; no new file.
- `ProjectileShooter.BuildBolt`: two shells with the per-enemy accent (§2.2), keyed off the shipped
  `EnemyData` (a `bodyColor`-derived hue is *not* acceptable — the accents are listed; three named
  constants).
- `Projectile.cs:442`: `StartCoroutine` with `WaitForSecondsRealtime(feel.parryHitStop)` before
  `FovKick(deflectFovKick)`.
- Materials: `SlashFx.CreateAdditiveMaterial` (URP/Unlit, additive) only. **No new shader, no
  `ParticleSystem`, no `TrailRenderer`, no light.**

Do not touch: `ProjectileMath.cs`, `ProjectileFlightMath.cs`, `BoltRegistry.cs`, `ProjectileVolleySequence.cs`,
`SentryFlare.cs` (the flare's own budget is untouched), `SurgeTurret.cs`, `DataFactory.cs`,
`Assets/Editor/LevelStudio/*`, any `EnemyData` asset.

Acceptance: §2.4 tests green; `LogicalHitRadiusAndSpeedsAreUntouched` is the proof that the Encounter
Report and the solver need no re-run. In play: from `Spawn_T0` look at the first blue sentry at 30 m —
the shot must be trackable from the muzzle; the disc must show a face at chest height; a reflected bolt
must be teal and not bloom.

### Lane C — WORKER: audio

Files: `Assets/Scripts/Feel/ProceduralSfx.cs` (`Parry`, `Block`, `PostureBreak` fallbacks only),
`Assets/Resources/Audio/Sfx/Parry/`, `/Block/`, `/PostureBreak/` (clip curation), `Assets/Editor/Tests/WeaponAudioTests.cs`.

- Audit the 10 `parry_*.ogg`: keep only clips with a ≤ 20 ms broadband transient **and** a metallic ring
  ≥ 0.25 s (Sekiro's "kin" is transient + inharmonic bell partials ≈ 1.4 / 2.1 / 2.9 / 3.8 kHz); cull the
  rest (a variant pool that contains a dull clip makes one deflect in five feel like a block).
- Procedural `Parry` fallback: add the inharmonic partials above (decay 0.30 s) and a 45 Hz sub 60 ms
  under the 65 Hz thump; keep the 480 Hz clang so `ParryCue` (1600 Hz) stays clear.
- `Block`: lengthen to 0.28 s with a 110 Hz body; it is the "lesser, solid" sound.
- `PostureBreak`: it now plays for every enemy — verify the 5 clips sit under `Execute`'s level and
  above `Stagger`'s; if not, trim gain in the files, not in code.
- No new `Sfx` entries (rule 7 allows appending, but nothing here needs one). The per-streak pitch /
  volume table is **lane A's** (`ParryImpulse.DeflectLayersAt`); lane C reports any change it wants to
  that table rather than editing it.

Do not touch: `AudioManager.cs`, `ParryImpulse.cs`, `WeaponAudio.cs`, anything in lanes A/B/L.

Acceptance: `WeaponAudioTests` green; in play a Perfect / Block / enemy break are three distinguishable
sounds with eyes closed.

### Order

L and A are independent at the file level but *semantically* paired (L calls A's `ParryImpact.Block`
and `NoteResult`); the lead lands A first, then L. B and C run in parallel with both. After all four:
`3. Create Data`, `4b. Build Mini-Bosses`, `Health Check`, `RunQuick`, play-mode `FeatureTests`.

---

## 4. Risks

1. **Streak state and domain reload.** `ParryImpact` is static; its `GameEvents.ParryResolved` hook must
   be re-armed lazily on first `Deflect` and cleared on `PlayerRespawned`, or a reload without
   `Awake` leaves a stale streak. Pin with a test on the pure ladder and a reset assertion.
2. **Aim budget at streak cap.** Rotation does not escalate (pitch + yaw = 3.5° < 4 at every `i`);
   only head sink and FOV do. If the lead wants rotation to climb, the `KickIsBoundedFarBelowTheAimBudget`
   ceiling has to be argued up first, not slipped past.
3. **Bolt FOV in-then-out.** Delaying the +4 by 0.09 s realtime on a bolt Perfect is a new *shape*; if it
   reads as a wobble, fall back to summing (−3.5 + 4 = +0.5, i.e. effectively no punch) rather than
   restoring the overwrite.
4. **ACES ceiling at the cue.** Core 1.6 + halo 0.30 + outer 0.10 = 2.0 exactly; any later "make the halo
   brighter" breaks the ceiling. The test in §2.4 must sum the three constants, not assert them singly.
5. **Teal on the disc.** The Dancer's cyan rim (0.36, 1.25, 1.15) is ~25° of hue from the reflected /
   PERFECT teal. Incoming = cyan + bloom, reflected = mint + no bloom is the separation; if it still
   confuses in play, shift the reflected core toward white (0.85, 0.95, 0.92), not the rim.
6. **Renderer count.** Five renderers per bolt × ~8 live is fine; the optional wake line is gated on a
   play check, not shipped by default.
7. **`shaftDiameter` is prefab data.** Changing the C# default does nothing to
   `Legendary_SeraphLancer.prefab` (rule 9); lane B must change the value `MiniBossFactory` writes and the
   lead must rebuild the prefab, and `SeraphLancerDataTests` must read the prefab.
8. **Hitstop unchanged is a choice, not an oversight.** If a playtest still says "no weight" after the
   force channels land, the next lever is the *release* (0.07 → 0.10, first step 0.45 → 0.35), which
   costs 0.014 world-s and stays under the 0.13 test — never the freeze.

Model: Fable 5.1 (Claude Code)
