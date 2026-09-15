# Boss AI spec (Fable, 2026-09-14) + user decisions

Recovered from the 09-14 session transcript. Sections A1/A2/A4 committed in 493263a; A3 portal bug fixed in c269833.

## User decisions (2026-09-14)

- S1, S2: implement.
- S3: YES, T4 only: Judge no-contact stances stop blocking V18; T1 Lancer + Ninja stays sequential.
- S4: make BrawlerV18_Grab RED (unblockable), grab on Hit/Blocked.
- S5: Lancer 4 javelins; Dancer 3 whirl discs + 4 bounces; Judge shield bash into Heavy dive. NOT the V18 higher suplex.
- S6, S7: skipped (low).

## Fable report (verbatim)

# Fable review pass: boss AI / new boss designs — report + spec

Working tree: 15 tracked files modified, nothing committed, nothing staged. Both assemblies compile (`dotnet build` 0 errors). Quick EditMode suite green twice: 1317/1317 (`TestResults/EditMode-20260914-203953.xml`, run against the regenerated assets). No edits outside enemy AI / moveset / animation-wiring / their tests / DATAFLOW. Motor, TimeScaleController, InputReader, `PlayerCombat.ReceiveAttack` resolution, level geometry, HUD, `BossArenaTrigger`, `SolarArenaPortal`, pickups: untouched (I ran `git checkout` on three `Assets/Data/Items/*.asset` that `Create Data` had wiped — see C).

## A. What I finished implementing (the in-flight work)

### A1. Root cause of the live `[PuppetVisuals] 'ComboFinisher' had to be clamped` warning — FIXED (code)
- Not Lancer art. The forge ComboFinisher is the SAME motion on the Judge, the Dancer and the Lancer (`Assets/Enemies/*.clips.json`: 36 frames, `OnAttackHit` at 0.2 -> 1.5 s baked, 0.30 s of run-in). `PuppetVisuals.PlayAttackClip` computed `speed = 0.30 / secondsToImpact`; as the third hit of the string the wind-up is `0.65 + NextGap(0.22 x lerp(1,0.45,0.60)=0.147) + 0.06 = 0.857 s` -> x0.35, exactly the logged number. The Judge clamps even solo (0.30/0.76 = x0.39). The clamp started the clip at once at x0.4, so the contact frame landed 0.10-0.15 s BEFORE the blow on the 1.6x-posture finisher.
- Fix, in the one shared function every body routes through: `Assets/Scripts/Enemies/Core/PuppetVisuals.cs` — new pure `public static float ClipStartDelay(contact, secondsToImpact, minSpeed)`; `PlayAttackClip` now holds the current pose and starts the clip LATE at `minClipSpeed` so its contact lands on the impact (`pendingClip` / `pendingClipAt`, fired in `Update` before the impact hand-off; cleared in `PlayOneShot`, `PlayLoop`, `ReserveAnimatorUntil`, `ReserveAnimatorSoftly`, `ClearTelegraph`; shifted in `ReanchorAnimatorImpact`). The tell is untouched (base colour sink + cue are on the data clock). The warning now fires only for `speed > maxClipSpeed`. Class header + `docs/DATAFLOW.md` (PuppetVisuals map) updated.
- Test: `Assets/Editor/Tests/PuppetSpinTests.cs::AClipWithAShortRunInStartsLateAndStillLandsItsContactOnTheBlow` (the shipped 0.857 s case; delay + contact/floor == impact).

### A2. Reflected javelin flew UNDER the hovering Lancer — FIXED (code, 2 lines)
- `Assets/Scripts/Enemies/parkour_enemies/Projectile.cs:385,451`: the return flight aimed at `Chest(shooter.transform)` = root + 1.2 m, while `SeraphLancerVisuals` lifts the body 3 m on HoverRoot. The reflect still damaged (swept sphere on the root point) but visibly missed the body — the T1 lesson is "deflect it back INTO him". Now `shooter.BodyPoint(1.2f)` (existing public seam; `SeraphLancerVisuals`/`CinderJudgeVisuals` override it with `Lift`). Sentries: identical point. DATAFLOW reflect line updated.

### A3. Lancer death / T1 portal — traced, NOT the AI
- `Health.OnDied -> EnemyController.Die`: `SetState(Dead)`, colliders off, `RaiseEnemyKilled`, `SeraphLancerVisuals.Die -> CancelSpecialPresentation` (Falling, lift -> 0), `SeraphLancerJavelins.Update` recalls incoming javelins on `!IsAlive`, `Destroy(gameObject, 1.5f)`. `Health.IsDead` is set before `OnDied` (`Health.cs:44/58`). No legendary prefab uses `BossController` (only `Boss.prefab`); nothing sets `Invulnerable` outside debug/FeatureTests; `SolarArenaPortal.cs:191` releases both intro `aggroLocked`s. So `BossArenaTrigger.IsSpawnDead` sees `Health.IsDead` or null. Point the portal investigation at the `sawPartnerAlive` latch (`BossArenaTrigger.cs:107-118`): it only latches once `partnerSpawner.Instance != null` is observed AFTER `triggered`; if the T1 duo spawner (`Spawn_Legendary_T1_Duo`, prefab `Legendary_Ninja`) has no instance at that moment it never clears.

### A4. Tempo pass (data-only; started before the process change, finished to a clean point)
Wind-ups untouched everywhere (that is the parry contract). `Assets/Editor/DataFactory.cs`:
| body | aggression | comboBreath | attackCooldown | other |
|---|---|---|---|---|
| Seraph Lancer | 0.60 -> 0.70 | 0.50 -> 0.40 | 0.35 -> 0.25 | verdict real punish 1.46 -> 1.31 s |
| Orbit Dancer | 0.78 -> 0.85 | 0.32 -> 0.28 | 0.22 -> 0.18 | |
| Cinder Judge | 0.70 -> 0.80 | 0.45 -> 0.35 | 0.35 -> 0.25 | Storm recovery 3.00 -> 3.20 so the landing stays >= 1.5 s (1.54) |
| V18 Grappler | 0.80 -> 0.90 | 0.35 -> 0.28 | 0.25 -> 0.18 | v15 Brawler untouched |
| Argent Halberdier | 0.85 -> 0.90 | 0.22 | 0.30 -> 0.20 | |
| Ember Revenant | 0.38 -> 0.55 | 0.35 | 0.75 -> 0.50 | crosses the 0.5 press-on line (`OnParried`): presses through a deflect |
| Pale Marionette | unchanged | | | its spin beat/recoil are DERIVED from 0.62 (`parryRecoilSeconds 0.1387`) — see spec S6 |
What aggression does (`EnemyController.cs`): recovery x(1-0.65a) [735], cooldown x(1-0.7a) [737], combo gap x(1-0.55a) [605], parry recoil x(1-0.45a) [778], press-on after a deflect at >= 0.5 [767]. Tests re-pinned: `CinderJudgeDataTests.cs:152,184`, `OrbitDancerDataTests.cs:166`, `SeraphLancerDataTests.cs:183`. DATAFLOW lines 1399/1465/1488 updated. Assets regenerated with `DataFactory.CreateAll()` (verified in YAML).

## B. Spec for everything else (ranked; a sonnet-tier worker implements)

**S1 (high) — Duo arbitration ignores projectiles in flight.** `EnemyController.MayCommitToAttack` (l.201) counts only `IsCommitted` (Windup/Strike). The Dancer's discs live 4 s / 3 bounces after her Strike ends (~0.2 s post-release); the Lancer's javelin 3 lands up to 0.35 s after his Strike ends. So the partner (Halberdier / Ninja) can cue while a bolt is incoming from another side — the exact "not simultaneously parryable" case the arbitration exists for. Change: in `MayCommitToAttack`, also return false when `BoltRegistry.AnyImpactBefore(Time.time + horizon)` where `horizon` = the enemy's earliest possible cue + parry window ≈ `0.45 + 0.05 + 0.4`; add a one-line note in `docs/DATAFLOW.md` (enemy arbitration). Keep `TryPunishFlask` on the same gate (it calls `MayCommitToAttack`). Test: extend `Assets/Editor/Tests/parkour_enemies/BoltTimingTests.cs` style with a pure check if you factor the predicate as `static bool ProjectileBlocksCommit(float now, float horizon)`. Taste note for the lead: the Dancer would also hold her own melee while her discs bounce; that is intended (one thing to answer at a time).

**S2 (high, spectacle+fluidity) — Chain phrases instead of Recover -> Chase -> walk -> commit.** The visible "hard stop" between strings is: `NextHitOrRecover` -> Recover (≥ `comboBreathSeconds`) -> `SetState(Chase)` -> re-enter the band + 50° facing + `nextAttackTime`. Change in `EnemyController.NextHitOrRecover`: when the player is still inside `preferredRange + commitTolerance` and `Random.value < Aggression`, pick the next combo now and `BeginWindup(next.hits[0], NextGap(attack))` (skip Recover/cooldown); otherwise the existing breath. Every hit stays cued (`BeginWindup` is unchanged). Cap chained phrases at 2 per `parryStreak == 0` exchange so a signature's recovery (verdict/storm/Aegis) is still the punish: only chain when `attack.recovery <= 1.0f`. Data: none. Test: a pure `static bool ShouldChainPhrase(aggression, recovery, dist, band, roll)` in `SoulsCombatTests`.

**S3 (medium) — The Judge's stances lock the duo.** Aegis raise is 2.4 s of Strike with no contact + 0.75 s bash wind-up; Storm 1.6+0.45+3.2 s. Both count as committed, so V18 circles for 3.3-5.3 s per signature. Options for the lead (taste): (a) leave (one thing at a time), or (b) exempt no-contact stances from arbitration: in `MayCommitToAttack` treat `e.IsCommitted && e.CurrentAttack.range <= 0f` as NOT committed (Aegis raise, Sky Verdict) — then V18 attacks while the Judge shields / the Lancer hovers, which is the "absurd but fair" duo (two reads, both cued). I recommend (b) for T4 only; T1 Lancer+Ninja stays sequential. If (b): `EnemyController.cs:201-211`, plus S1's projectile guard still applies.

**S4 (medium) — V18 Skyfall Suplex: a blue that punishes a Block.** `V18Grapple.OnParryResolved` grabs on `Hit` OR `Blocked` (`V18Grapple.cs:79`); `BrawlerV18_Grab` is `unblockable=false` (DataFactory 1895). The roster's rule (Lancer block, DataFactory 2364) is "a guard is never punished for being a guard" on blues. Decide: keep as the T4 lesson ("Perfect or nothing") and make it READ as such — set `a.unblockable = true` on `BrawlerV18_Grab` so the cue is RED (Sekiro's perilous grab: dodge it), OR keep blue and grab only on `Hit`. My recommendation: red + grab on Hit/Blocked (ParryMath forces Hit on red anyway), `V18GrappleTests` updated (`unblockable` assertion). Also: `slamTimeout 3 s` + `WatchSlam` fires on `motor.IsGrounded` — fine.

**S5 (medium, spectacle) — "Absurd" candidates, all data-first:** (i) Lancer: 4 javelins (`SeraphLancerJavelins.javelinsPerVerdict` 3->4, `liveCap` 4->5) with strike 2.75 -> 3.85 (3 x 1.10) — keeps the 1.10 cadence; (ii) Judge: Aegis bash as a 2-hit (bash, then Heavy) so the shield phrase ends in the dive; moveset entry `cjShieldRaise, cjShieldBash, cjHeavy` in DataFactory 2124; (iii) Dancer: whirl -> 3 banked discs (`whirlCount` 2->3) and `maxBounces` 3->4; (iv) V18: suplex `liftHeight` 11 -> 14 with `liftSeconds` 0.9 -> 1.1 (strike 2.0 still covers 1.1+0.35+throw). Each needs its DataTests pin updated; (i) and (iii) need `Projectile Encounter Report` re-run (live caps only; speeds/intervals unchanged so `ParryModuleSolver` output is unaffected).

**S6 (low) — Marionette tempo.** Untouched because `parryRecoilSeconds 0.1387 x lerp(1,0.55,0.62) = 0.100 = strikeDuration` is a derived identity and the 0.69 s beat sets the 2087°/s spin (4 rev/beat). To raise her aggression to 0.70: recoil must become `0.100 / lerp(1,0.55,0.70) = 0.1460`, and `MarionetteDataTests` recomputes the beat from `d.aggression` (gap = max(0.1, 0.x x lerp(1,0.45,a))) — verify the beat stays 0.69 (the gap floor 0.1 may already bind; if so nothing else moves). DataFactory 988.

**S7 (low) — Lancer verdict's no-op impact feeds the player's whiff/mistime scoring.** `NextImpactTime` reports the verdict's range-0 contact, so `AnyAttackIncoming` (l.164) calls it "incoming" within 5.6 m. Harmless (javelins register via `BoltRegistry`); ignore unless a whiff-scoring bug is reported.

Reviewed and clean: Cinder Judge shield (`PlayerHitDeflection` bypassed for executes/ripostes; shatter routes through `OnParried`), Storm ticks (fixed cadence off the brain's impact; unblockable is honest since the answer is LEAVE), Dancer bank shots (mirror + confirm ray, fan fallback flies off), V18 grab abort paths (death/stagger/leaving Strike drop the player; `PlayerRespawned` aborts), execute window (`PlayerCombat.cs:91` refuses hits while executing, so a partner cannot hit during a deathblow).

## C. Generators / tests to re-run before commit
- `VibeGame1/3. Create Data` — ALREADY run by me, but re-run in the standard order **3 -> 3b -> 4** (or `0. Rebuild Everything`): step 3 alone cleared `viewmodelPrefab` on `Assets/Data/Items/{DeflectSigil,Grapple,Rebound}.asset`; I restored those three from HEAD (`git checkout`), so the tree is correct now, but any future `Create Data` must be followed by 3b/4.
- `4b. Build Mini-Bosses` not needed (no clip tables changed). `Projectile Encounter Report` not needed for A/A4 (no shooter speed/interval/cue change); required for S5 (i)/(iii).
- Quick EditMode: green (1317/1317). Run Full EditMode + Level_01 FeatureTests (play mode) before commit; FeatureTests has no pin on the six changed aggressions (only `Chorister_Aggression`).
- Feel is unplayed: the tempo pass and the late-start finisher need the user's eyes on the Lancer string (Jab2, Swing, Finisher) in the Sandbox.

## Files touched (all mine unless noted)
`Assets/Scripts/Enemies/Core/PuppetVisuals.cs`, `Assets/Scripts/Enemies/parkour_enemies/Projectile.cs`, `Assets/Editor/DataFactory.cs`, `Assets/Editor/Tests/PuppetSpinTests.cs`, `Assets/Editor/Tests/souls_enemies/{CinderJudgeDataTests,OrbitDancerDataTests,SeraphLancerDataTests}.cs`, `docs/DATAFLOW.md`; regenerated by Create Data: `Assets/Data/Attacks/CinderJudge_StormJudgement.asset`, `Assets/Data/Enemies/souls_enemies/Legendary_{CinderJudge,FlurryBrawlerV18,Halberdier,OrbitDancer,Revenant,SeraphLancer}.asset`. `Assets/Scenes/Level_01.unity` was saved via the bridge (required before the EditMode runner; content unchanged by me). `docs/HANDOFF.md` shows modified with an empty diff (line endings) — not mine.

Model: Fable 5.1 (Claude Code), lead for this pass.
