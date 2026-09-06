# Gap analysis — external soulslike enemy-AI report vs. this game (2026-09-06)

Source: `docs/research/soulslike-enemy-ai-report.md`. Plan only; nothing here is implemented.
Companion: `docs/plans/combat-plan-2026-09-06.md` (P1/P2/P4 are landing now — P2's `ParryMath.SourceDirection`
already exists at `Assets/Scripts/Combat/ParryMath.cs:14`). This page deliberately does not restate them.

**Headline: the report describes an architecture this game already has.** The stack-FSM, the weighted
distance-banded selector, the attack tokens, the posture economy with HP-tied regen, the graduated anti-mash
parry, the phase-swapped moveset — all shipped. The genuinely new material is a short list: per-move
cooldown/history, interrupts, the Lies-of-P staggerable window, and AI LOD.

## 1. Idea-by-idea

| Report idea | Status | Reasoning |
|---|---|---|
| Stack/hierarchical FSM "Goal" brain | **Have** (flat FSM) `Enemies/EnemyController.cs:29` | `Idle/Chase/Windup/Strike/Recover/Staggered/Executed/Dead` + combo resume (`:341-360`). A *stack* buys nothing at this enemy count. |
| Weighted attack selection | **Have** `Data/EnemyMoveset.cs:53` | Two-pass weighted roll, no allocation. |
| Distance-banded move pools | **Have** `Data/EnemyMoveset.cs:127`, far-band commit `EnemyController.cs:~312` | `minRange/maxRange` per entry; `HasEligible` gates the far commit. |
| **Per-move cooldown / last-used history** | **Missing** | `MovesetEntry` has weight + band, no `cooldown`/`lastUsed`. A signature combo can repeat back-to-back. |
| Combo chains as authored sequences | **Have** `Data/EnemyAttackData.cs:81` (`AttackCombo.hits`) | Phrases are authored, with a `comboBreathSeconds` floor (`EnemyData.cs:45`). |
| RNG-gated optional combo extension | **Partial** | `CanResumeCombo` continues on condition, not on a weighted roll for an extra hit. |
| **Interrupts (heal / item / behind-boss watch region)** | **Missing** | `Health.OnDamaged` (`EnemyController.cs:228`) is the only reactive hook. `FlaskAbility.IsDrinking` is public and unread by any enemy. |
| Animation-event-driven hitboxes | **Not a fit** | Combat is timing-data-driven (`EnemyAttackData.windup/impactDelay`) and resolves through `PlayerCombat.ReceiveAttack` (rule 3). Clips are *stretched onto* authored impact times (`EnemyAttackData.cs:27-33`) — the inverse, on purpose, and correct for a parry game where the window must be a number. |
| Frame data startup/active/recovery on the SO | **Have** `Data/EnemyAttackData.cs:11-16` | In seconds, not frames — better for a variable-framerate parry. |
| Posture as second health bar | **Have** `Combat/Posture.cs` | Break → stagger → deathblow. |
| **HP-tied posture regen (Sekiro)** | **Have** `EnemyController.cs:240` | `RegenMultiplier = Lerp(0.25f, 1f, Health.Ratio)`. Already shipped; the report confirms it. |
| Posture "close to breaking" flash | **Partial** | `EnemyPostureBar` + `visuals.SetPostureRatio` show the ratio; no distinct near-break state. |
| **Lies-of-P staggerable window (full ≠ broken)** | **Missing / by design** | Here posture-full *is* the break (`Posture.cs:48`). Adding a "you must land X on the white bar" gate is a real design fork. |
| Guard Regain / rally on block | **Not a fit as-is** | Held guard already costs **0** chip (`DataFactory.cs:1481`), so there is nothing to win back. See §3. |
| Perfect Guard ~155 ms | **Have, more generous** `DataFactory.cs:1447` | 0.13 s perfect + 0.12 s late, degrading to a held block — exactly the report's recommended fall-through. |
| **Anti-mash lockout** | **Have, better** `ParryController.cs:201-210` | Three-tier graduated recovery (success 0.08 / mistime / whiff 0.5), clamped so it never outlasts the next cue (`:222`). |
| Delayed/variable windups to punish rhythm | **Partial → worth doing** | Windups vary 0.45–0.9 s across assets, but the cue always fires 0.28 s before impact, so rhythm is *learnable per attack*, never baited. |
| Unblockable / red Fury attacks | **Have** `EnemyAttackData.cs:24`, `ParryMath.cs:56` | Pink alert tell; guard cannot eat it. |
| Group attack tokens | **Have** `EnemyController.cs:119,191` | `MaxSimultaneousAttackers`; non-committed enemies hold `ReadyPosition` and circle. |
| Ranged kiting archetype | **Adapted** `EnemyData.cs:76-90` | Sentries hold a perch instead of kiting — correct for parkour-first. |
| Multi-phase boss, moveset swap at thresholds | **Have** `Enemies/BossController.cs:55,67,91` | |
| Aggression scaling at HP thresholds | **Partial** `EnemyData.cs:30` | Aggression is an authored constant, not raised at an HP threshold. |
| **Time-slicing / AI LOD** | **Missing** | Every `EnemyController.Update` and `ProjectileShooter.Update` runs every frame; `ActiveEnemies` (`:110`) is the registry that makes slicing trivial. |
| Hyper armor on enemy startup | **Missing** | Player is melee-only and light; a trade-through enemy would mostly remove parry answers. Low value. |
| Stamina gating the player | **Not a fit** | Speedrun frame; Pyre is the resource. |
| Animancer / NodeCanvas / Behavior Designer / A* / Final IK | **Reject** — see §3 | |
| Root motion for attacks | **Not a fit** | `lungeDistance` + `ApplyLunge` is deterministic and testable; root motion would desync from the authored impact time. |
| NavMesh throttling, no `OnTriggerStay`, pooling | **Partial** | Bolts are `CreatePrimitive`d per shot (`ProjectileShooter.cs:~73`) — a pooling target if a span ever fires hundreds. |
| Lock-on / third-person camera advice | **Reject** | First-person; lock-on exists for wands only. |

## 2. The ranked shortlist (value / cost)

**A. Per-move cooldown + last-used history.** *(code, small; `EnemyMoveset`)*
Feel: a legendary never throws its signature combo twice in a row — the repertoire reads as a repertoire.
Touches: `Assets/Scripts/Data/EnemyMoveset.cs` — add `cooldown` to `MovesetEntry`; move selection to an
instance-side runtime wrapper holding `lastUsedTime[]` (the report's own warning: never mutate the shared SO),
owned by `EnemyController`, passed into `Select`. `EnemyData.SelectCombo` keeps its signature with an optional
history argument so untouched callers behave identically.
Test: EditMode — a two-entry moveset with a cooldown on the heavy never returns the heavy twice inside the
cooldown, and never returns null when the heavy is the only eligible entry (cooldown must degrade to weight 0,
not to "no attack"). Risk: low; the fallback path must not starve. *This is the single highest value/cost item.*

**B. AI LOD / time-slicing for sentries.** *(code, small)*
Feel: none — that is the point; a span can hold twelve perches without a frame cost.
Touches: `EnemyController.Update` early-out and `ProjectileShooter.Update`, driven off the existing
`activeEnemies` registry (`:110`) — round-robin the *decision* work by index while leaving anything that
carries a cue (Windup/Strike, and every live `Projectile`) at full rate.
Test: EditMode on a pure `AiBudget.ShouldTick(index, frame, bucketCount)` helper; FeatureTests asserts a
sliced sentry still fires on its metronome within one interval. Risk: **must never slice a cued state** — a
late cue is a broken parry contract. Confine slicing to `Idle`/`Chase`.

**C. Delayed-timing attacks in a legendary moveset.** *(data-only)*
Feel: one attack in the phrase hangs a beat longer, and parrying on rhythm eats it.
Touches: `DataFactory.cs` only — a new `EnemyAttackData` per legendary with a long `windup` and a longer
`impactDelay`. **The cue still fires 0.28 s before impact**, so this punishes rote rhythm, never reaction.
Test: EditMode shipped-asset assert — every attack still satisfies `windup >= 0.45` and the cue lands
`cueLead` before impact, plus a new assert that each legendary moveset contains at least one entry whose
windup differs from its siblings by ≥ 0.2 s. Risk: authoring only; it cannot break readability by construction.

**D. Interrupt: punish the flask.** *(code, small)*
Feel: drinking mid-duel is a decision, not a free top-up.
Touches: a small `EnemyInterrupts` component reading `FlaskAbility.IsDrinking`; on a rising edge, if the enemy
is in `Recover` and inside `aggroRange`, roll a per-`EnemyData` `flaskPunishChance` and end the recover early
into a chosen combo. **Additive only** — it schedules an attack through the existing `BeginCombo` path and
touches no Fable timing.
Test: FeatureTests — begin a drink in front of a Warden with the chance forced to 1 and assert the boss leaves
`Recover` within a frame and its next impact is still cued 0.28 s ahead. Risk: medium-feel; a boss that
*always* punishes makes the flask dead. Chance must be authored, and it must be gated to `Recover` so it can
never cancel a wind-up the player is already answering.

**E. Near-break posture read at distance.** *(presentation; extends P4)*
Feel: you can see a sentry is one deflect from open, from across the gap.
Touches: `EnemyVisuals.SetPostureRatio` / `EnemyPostureBar` — above ~0.8 ratio, pulse the existing posture
colour rather than adding a new bright element (bloom budget, `DATAFLOW.md:997`).
Test: FeatureTests — at ratio ≥ threshold the near-break flag is set and clears on `EndStagger`. Risk: low,
but it must not become a second bright event competing with the deflect.

**F. Interrupt: the watch region behind a legendary.** *(code, small)*
Feel: circling behind a duelist to farm free hits is answered, once, with a turn-and-sweep.
Touches: the same `EnemyInterrupts` component — a rear cone + radius on `EnemyData`, firing a designated
"vent" combo (the Knight already has `Knight_Vent`) on a cooldown.
Test: EditMode on the pure cone test; FeatureTests places the player behind and asserts one vent, then a
cooldown. Risk: it can feel unfair in first person where the enemy is off-screen — needs the audio sting
before the cue, and a generous cooldown.

**G. Aggression raised at an HP threshold.** *(data + tiny code)*
Feel: a legendary's last third is a different fight without a new moveset.
Touches: `EnemyData` gains `aggressionAtLowHP` + `lowHPThreshold`; `EnemyController.Aggression` (`:86`) lerps
on `Health.Ratio`. That property is Fable's — **ask before editing**, or read the threshold in `BossController`
only. Test: EditMode on the pure aggression curve. Risk: touches a Fable system; boss-only variant avoids it.

**H. Bolt pooling.** *(code, small; defer)*
Only worth doing once a span demonstrably fires enough bolts to matter. Listed so it is not forgotten.

## 3. Explicitly reject

- **Animancer, NodeCanvas, Behavior Designer, A* Pathfinding, Final IK.** No non-programmer designers; content
  is already data (`docs/AUTHORING.md`); enemies are few and bespoke — the report's own "handful of bespoke
  bosses → stay hand-rolled" threshold. Each is a paid dependency and a WebGL build risk.
- **Animation-event-driven hitboxes.** Would move the impact frame into the clip. In a parry game the impact
  time must be an authored number a test can assert; the pipeline correctly stretches clips *onto* it.
- **Guard Regain / rally.** The held guard costs zero chip here, so there is no blocked damage to win back.
  Adding chip to create rally would make the block strictly worse to sell a mechanic that pushes toward
  post-block aggression — the opposite of a speedrun's "keep moving". Reject unless the chip=0 rule changes.
- **Lies-of-P staggerable window (full bar ≠ broken).** It inserts an extra required input between posture-full
  and the deathblow. In the duel that is a possible depth add; **on a span it would kill the sentry dash**, the
  newest verb, which keys off `IsStaggered`. Not worth forking the posture contract. Flagged as a question.
- **Player stamina, hyper armor, i-frame rolls, third-person camera and lock-on advice, GOAP/utility AI.**
  Wrong genre lane or already answered.

## 4. Open questions

1. Do you want the Lies-of-P "posture full, now land a specific action" gate in the **duels only**, or is
   posture-full-breaks-immediately the final answer for this game?
2. Should the flask interrupt (D) exist at all, or is drinking meant to be safe in a speedrun where the real
   cost is already the time it takes?
3. Is a rear watch-region (F) fair in first person, where the punishing enemy is behind the camera?
4. May a session raise `EnemyController.Aggression` (a Fable property, `EnemyController.cs:86`) for item G, or
   should HP-scaled aggression live in `BossController` only?

## 5. Do first

1. **A — per-move cooldown/history.** Small, data-authorable afterwards, and it is the report's own "single most
   important trick". It makes every existing moveset better with no new content.
2. **C — one delayed attack per legendary.** Data-only, provable, and it is the cheapest depth the duels can get.
3. **B — AI LOD**, confined to `Idle`/`Chase`. Pure win, and it removes the ceiling on how many perches a span
   may hold before the level designer has to think about frame cost.
