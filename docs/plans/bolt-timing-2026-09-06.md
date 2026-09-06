# Bolt arrival timing — 2026-09-06

Plan only. Nothing here is implemented. Source: the lead's playtest — *"there are certain portions
where the projectile just comes in at a bad time … it needs to be hard but the placement and timing
of the shots needs to work with the game as well so the player can actually make the parrys while
moving fast."* Read literally: **arrival timing and perch placement**, not difficulty.

## Diagnosis — when a bolt is unfair rather than hard

A sentry fires whenever it is awake, in band (3–32 m, `Assets/Editor/DataFactory.cs:267`), has a clear
line, and its metronome is due (`Assets/Scripts/Enemies/parkour_enemies/ProjectileShooter.cs:41-69`).
Speed 40 / 36 m/s, interval 1.6 / 2.4 s (`DataFactory.cs:266,309`), full velocity lead, homing 180 / 150
deg/s (`Projectile.cs:136`). The cue is a flat 0.28 s before impact (`Projectile.cs:23`) and the launch
slows inside ~14.4 m so a bolt can never beat its own cue (`ProjectileMath.cs:53`). The player answers
with a 0.13 s perfect + 0.12 s late window (`DataFactory.cs:1560-1561`) inside a 75° facing cone
(`DataFactory.cs:1573`), judged against the bolt's travel (`Projectile.cs:193`). Run 11 m/s, slide 15.9,
air cap 17.6, hard cap 27.5 (`FirstPersonMotor.cs:179`): 0.28 s of cue is 3–4.5 m of ground.

**Three of the "bad times" are structural bugs, not difficulty.**

1. **The first bolt of an encounter has no anticipation at all.** `nextFireAt` is set once at spawn
   (`ProjectileShooter.cs:38`) and the beat is *held*, not reset, while out of band or blocked
   (`ProjectileMath.cs:81`). After the player has been out of line for seconds, `nextFireAt` is already
   in the past, so the shot fires on the **first frame** line of sight is established — which on a
   parkour route is the frame you crest a ledge, land, or swing round a corner. At 12 m that is a cue
   0.03 s after launch and impact 0.31 s later, while you are still resolving a landing. Two stale
   perches covering one crest fire on the *same frame*. This is almost certainly the exact complaint.
   **High confidence.**
2. **Bolts are invisible to the player's fairness machinery.** `EnemyController.EarliestCueTime` and
   `AnyAttackIncoming` only look at melee wind-ups, and only inside `preferredRange + 3` m
   (`Assets/Scripts/Enemies/Core/EnemyController.cs:156-192`). So on a span: a missed parry is scored as
   a *whiff* and costs `parryWhiffRecovery` 0.5 s instead of the mistime's 0.2 s
   (`DataFactory.cs:1562,1567`), and `ClampRecoveryToNextCue` (`ParryController.cs:218-230`) is a no-op —
   which breaks the controller's own stated invariant, *"recovery can never outlast the next cue"*
   (`ParryController.cs:11-13`). Miss one bolt on a 1.6 s beat and the next one can cue **and land** while
   you are structurally unable to press. **High confidence — the literal unanswerable case.**
3. **A sentry keeps shooting your back.** Nothing gates firing on bearing. Run away at 11 m/s from 15 m
   and a homing 40 m/s bolt closes in ~0.52 s, arriving from outside the 75° cone: `ParryMath.Evaluate`
   returns `Hit` before timing is even considered (`Assets/Scripts/Combat/ParryMath.cs:31`). Not hard —
   impossible, unless you spin 180° and lose the route. **High confidence.**

**Player states, honestly sorted.** Parry is legal in almost everything: `CanParry` vetoes only
staggered / executing / drinking (`ParryController.cs:170-176`), so slide, air-dash (0.16 s,
`FirstPersonMotor.cs:46`), mid-jump, mid-balloon and landing are **hard, not unfair** — the camera is free
and 0.28 s is enough. Genuinely incompatible:

- **Wall-run entry.** `wallRunMinLookAlongCos` 0.30 (`FirstPersonMotor.cs:101`, checked at `:1875`)
  requires the look within ~72.5° of the run line to attach. Turning to a bolt in the ~0.3 s before a wall
  makes the wall reject you (`WallRunReject.LookingAway`). Sustain does *not* check look, so a bolt
  **during** a run is fair; a bolt during the **approach** is a forced loss either way.
- **Forced look-away geometry.** A perch covering a stretch where the route turns away: cone and route
  point in opposite directions, and no input resolves it.
- **A correct deflect that costs the run.** `AddImpulse` is unclamped (`FirstPersonMotor.cs:1526`) and a
  deflect adds 9 m/s along the look (`DataFactory.cs:275`). Mid-jump over a gap, answering *correctly* can
  overshoot the landing. The answer is perch placement, not a smaller boost.

## Proposals, ranked by value / cost

**F1 — Arm-up on acquisition.** *Code, `ProjectileShooter.cs` + one `EnemyData` field.*
Feel: a sentry that has just seen you takes a breath before its first shot; the beat starts after you are
on the ground and looking. On the transition from out-of-band/blocked to in-band,
`nextFireAt = Mathf.Max(nextFireAt, Time.time + data.projectileAcquireDelay)` (~0.7 s: one cue lead plus a
landing). Touches `ProjectileShooter.cs:41-69`, `EnemyData.cs`, `DataFactory.cs:266,309`. Test: EditMode on
a pure `ProjectileMath.AcquireBeat(previousBeat, now, interval, delay)` — no shot within `delay` of
acquisition, beat unchanged for a shooter already in band. Breaks: nothing; slightly fewer bolts per span.

**F2 — Bolts register as incoming.** *Code, small, touches shared cue helpers.*
Feel: the recovery rules the melee game already promises apply on a span too. A bolt reports its cue /
impact time to the static lists `EarliestCueTime` and `AnyAttackIncoming` read
(`EnemyController.cs:163-192`) — cleanest as a small bolt registry in `parkour_enemies` that the two
helpers also consult, leaving melee's 6 m `InThreatRange` untouched. Effect: a missed bolt parry costs
0.2 s not 0.5 s, and recovery is clamped to end before the next bolt's cue. Test: EditMode on the registry
(a bolt 0.4 s out is the earliest cue; a spent or reflected bolt is not). Breaks: `EnemyController.cs` is a
Fable core file — **name the boundary and ask before editing it**.

**F3 — A perch covers a stretch, not a fleeing back.** *Code, small, data-tunable.*
Feel: you run *through* a sentry's arc; once past, it is behind you, not shooting you. Refuse to *launch*
when the predicted arrival bearing falls outside `facingConeDeg` and the player's flat velocity is
receding — a gate before `FireAt` (`ProjectileShooter.cs:68`), not a held shot. Test: EditMode on a pure
`ProjectileMath.ArrivesInFront(muzzle, chest, playerVel, speed, coneDeg)`. Breaks: fewer bolts on long
straights; a badly placed perch will now read as toothless — which F6 measures.

**F4 — One beat per span.** *Small code + authored offset.* Feel: a tempo, not two clocks arguing. Anchor
`nextFireAt` to a shared level epoch plus a per-sentry half-beat offset instead of spawn time
(`ProjectileShooter.cs:38`). Much less urgent once F1 lands — acquisition was the real collider. Test:
EditMode — two sentries at one interval never produce cues closer together than `parryWhiffRecovery`.

**F5 — Longer minimum flight up close, *not* a speed-scaled cue.** *One const + one data field.* Raise
`ProjectileShooter.CueMargin` 0.08 → 0.16 (`ProjectileShooter.cs:30`) and/or `projectileMinRange` 3 → 6 m
(`DataFactory.cs:267`), so a near bolt shows ~0.44 s of flight. **Reject a speed-scaled cue lead:** 0.28 s
is a contract shared with every melee attack (`docs/ARCHITECTURE.md` feel contracts); making it elastic
gives the loudest signal in the game a variable meaning, and the same relief is available by lengthening
the *flight*. Test: shipped-value assert beside the existing bolt arithmetic.

**F6 — Make placement a measured constraint.** *Editor tooling, no gameplay change.* Extend the arc
report's shooter section (`Assets/Editor/LevelTraversalAnalyzer.cs:68-89`, `LevelArcReport.cs:95`) to flag
a claimed deck where the route tangent is more than `facingConeDeg` off the muzzle bearing (forced
look-away) or the covered stretch is an airborne gap or a wall-run approach. Then move the offending
perches in `Assets/Editor/LevelDefinitionAuthoring.cs:43-66`. Test: `LevelSpan*Tests` assert no perch
claims a flagged deck. Slow but permanent — and for the wall-run-entry case, **moving the perch is the
right fix, not a system change**.

**Rejected — the comfort blanket.** Holding fire while the player is airborne / mid-wall-run / mid-dash and
releasing on the way out. It makes the enemy read the player's state machine, teaches nothing, turns a
metronome into a slot machine, and would make spans mushy exactly where the lead said they must stay hard.
F1 buys the same relief honestly, because the unfair case was *acquisition*, not *airtime*.

## Questions for the lead

1. Should a sentry ever shoot a player who has run past it, or is a perch a stretch you pass through (F3)?
2. Which spot bit you — T1's wall-run landing (`T1_Perch_E`, 12.4 m), T2's spiral, or T3's steps? F6 needs a target.
3. Fewer-but-better acceptable? F1 + F3 cut bolts per span roughly a quarter; or shorten intervals to compensate.
4. `EnemyController.cs` is Fable's. Do I have your say-so for F2's cue registration there, or must it stay entirely in `parkour_enemies`?

## Do first

1. **F1 — arm-up on acquisition.** Cheapest, and most likely *the* bug.
2. **F2 — bolts register as incoming.** Restores a promise the code already documents.
3. **F3 — no bolts at a fleeing back.** Removes the only literally unanswerable arrival.
