# Combat plan — 2026-09-06

Scope: how to make this game's combat better, before any change is made. Plan only; nothing here has
been implemented. Genre lane: **animation-driven action** verbs inside a **first-person speedrun**
frame — enemies are a route, not filler (BACKLOG.md §0).

## Diagnosis — the loop as it stands today

Two loops, not one. **The duel** (Warden, six legendaries): the enemy winds up ≥ 0.45 s, a cue fires
`cueLead` 0.28 s before impact (`docs/ARCHITECTURE.md:131`), the player answers inside a 0.13 s perfect
/ 0.12 s late window (`Assets/Editor/DataFactory.cs:1443`) with a held guard underneath at ×1.5 posture
and 0 chip (`DataFactory.cs:1478`); a perfect deflect costs nothing, pays 25 Pyre and enemy posture, and
a broken enemy takes a deathblow. **The span** (new, unplayed): Grunt and Heavy are now sentries —
`rangedOnly`, never melee, wake at 32 m on a three-line sight check, fire on a fixed metronome (Grunt
1.6 s @ 32 m/s, Heavy 2.4 s @ 28 m/s, `DataFactory.cs:259-299`) leading 80 % of the player's velocity,
with the launch slowed inside 11.5 m so every flight is ≥ cue lead + 0.08 s
(`ProjectileShooter.cs:63`). A perfect deflect reflects the bolt (30 dmg / 40 posture Grunt, 40 / 50
Heavy) and pays the player **9 m/s along their look** (`Projectile.cs:191`) — the boost is the point.
Stagger the sentry and `SentryDash` (range 30 m, cone 18°, pull 0.35 s, `SentryDash.cs:22-27`) turns the
dash press into a blink-and-execute.

## What works — protect these

- **One resolution path.** Every incoming attack, bolt included, resolves through
  `PlayerCombat.ReceiveAttack` (rule 3). The ladder deflect > guard > hit is monotone and cannot be
  gamed; the held guard grants no Pyre, so turtling cannot charge the super (`PlayerCombat.cs`, Blocked).
- **One cue, one meaning.** 0.28 s before impact, always — melee wind-up or bolt (`Projectile.cs:137`).
  A sentry never fires inside a melee wind-up, so a tell is never two things.
- **The deflect is the only bright event.** Enemies do not glow; the bolt core at 1.6 is the single
  documented bloom exception (`docs/DATAFLOW.md:997` invariants). Rare discipline — do not spend it.
- **The boost is what fuses the two genres.** Deflect → speed along the aim is the one mechanic that
  makes an enemy a *piece of level* (MOVEMENT-PRINCIPLES 5 & 6). Everything below serves it.

## Problems, ranked

1. **The Grunt can never be sentry-dashed. (high confidence — arithmetic, not taste.)**
   Grunt `maxHP` 60 (`DataFactory.cs:195`); a reflected bolt does 30 damage *and* 40 posture
   (`Projectile.cs:155`, `DataFactory.cs:262`). Two reflects = 60 damage = dead on the same frame the
   posture (80 ≥ 60) would have broken it, and `Health.TakeDamage` runs before `Posture.Add`. The corpse
   never enters stagger, so `SentryDash.FindTarget` (`SentryDash.cs:84`, requires `IsAlive &&
   IsStaggered`) can never see it. **The brand-new verb is unreachable on the enemy the player meets
   first.** Heavy survives by luck: 3 × 40 = 120 < 130 HP while 3 × 50 = 150 ≥ 110 posture. Hurts
   everyone.
2. **A moving runner cannot legally deflect a bolt from a perch they have passed. (high.)**
   `ParryMath.Evaluate` returns `Hit` unless `facing` (`ParryMath.cs:31`), and `facing` is measured to
   the **shooter's** flat position within ±75° (`PlayerCombat.cs`; `facingConeDeg` 75,
   `DataFactory.cs:1456`) — not to the bolt. A sentry that leads 80 % keeps shooting as you run past and
   away; from there the bolt is uncatchable by design and the only answer is a held guard nobody holds
   while sprinting. Hurts the speedrunner most, and is invisible: it reads as "the parry didn't work".
3. **Deflect-for-speed and deflect-for-legality point in opposite directions. (high.)**
   The boost is applied along `look.AimForward`; legality is judged against the shooter. A perch only
   "boosts" if it sits roughly *ahead* along the route. Nothing in the data or the arc report enforces
   that — perches were placed for sight lines (HANDOFF: three perches re-aimed), not for boost vectors.
   Half the deflects will be a chore and half a launch, with no way to tell which before pressing.
4. **The metronome is per-enemy, not per-span. (medium.)** Two sentries covering one span each run their
   own clock from their own spawn (`ProjectileShooter.cs:38`). Beats drift into arbitrary phase, so the
   rhythm a runner is meant to learn differs every run and two cues can land inside one parry recovery.
   The "fixed metronome" argument only pays out if a span has *one* beat.
5. **Stagger is a state with no read at distance. (medium.)** A staggered sentry buckles and stops
   firing; `SentryDash` raises a text prompt inside an 18° cone. At 25 m, against a greybox that
   deliberately does not glow, "this one is open" is carried by a pose and a line of HUD text. The
   skill's rule — every gameplay state must be communicated — is not met for the newest state.
6. **The span has one verb and one answer. (medium.)** Deflect, deflect, dash, kill. There is never a
   reason not to deflect, no cost to a miss beyond 12 damage, and no second read. The duel has four
   (deflect / guard / leave the cone / punish the opening). A span will be satisfying once and mechanical
   by the fifth run.
7. **The punishment model does not fit a speedrun. (low-medium.)** A bolt does 12 damage
   (`DataFactory.cs:249`). Missing every bolt on a span costs almost nothing, and nothing converts a
   missed deflect into the currency a runner actually cares about: time.

## Proposals, ranked by value / cost

**P1 — Let the Grunt live long enough to be executed.** *(data-only, `DataFactory`)*
Feel: two clean deflects and it is open, not dead.
`DataFactory.cs:262` — `parriedProjectileDamage` 30 → 20 (two reflects = 40 of 60 HP; posture 80 ≥ 60
breaks first). Test: an EditMode arithmetic assert beside the existing shipped-asset tests —
`ceil(maxPosture / parriedProjectilePosture) * parriedProjectileDamage < maxHP` for **every**
`rangedOnly` EnemyData, so the invariant cannot rot when the Heavy or a future sentry is retuned.
Breaks: nothing — reflect damage is used nowhere else.

**P2 — Judge a bolt parry against the BOLT, not the bearing.** *(code, small)*
Feel: if you can see it coming and you swing at it, it deflects.
`AttackInfo` gains an optional incoming direction; `Projectile.Arrive` (`Projectile.cs:167`) fills it
with `-dir`; `ReceiveAttack` uses it for the facing test when present and falls back to the attacker
position otherwise — melee behaviour is bit-identical, so no Fable system changes. Test: EditMode on the
facing helper (bolt outside the shooter's bearing but inside the crosshair cone = parriable; bolt
genuinely from behind the player = not). Breaks: it touches the rule-3 hub, so keep it strictly additive.

**P3 — One beat per span.** *(data + small code)*
Feel: a span has a tempo you learn, not two clocks arguing.
Anchor `nextFireAt` to a shared span phase (a per-level epoch plus a per-sentry offset authored on the
spawn) instead of `Time.time + 1f` at spawn (`ProjectileShooter.cs:38`). Two sentries on one span get
half-beat offsets and never coincident cues. Test: EditMode on `ProjectileMath.NextBeat` proving two
sentries at the same interval never produce cues closer together than `parryWhiffRecovery` (0.5 s).

**P4 — Make "open" legible at 25 m.** *(code, presentation only)*
Feel: you see the kill from across the gap.
Give a staggered sentry the deathblow mark scaled for distance — the existing violet sternum quad, a slow
pulse, and a one-shot sting on the break. Adds to the bloom budget only on a broken body, where it is the
only such body on screen. Test: `FeatureTests` — on stagger the mark is active and `SentryDash.Target` is
non-null inside the cone.

**P5 — Give the span a second read: the overcharged bolt.** *(data + code, medium)*
Feel: one bolt in the phrase is different, and it is the one that pays.
Every Nth beat (authored on `EnemyData`) the sentry fires a slower, larger bolt whose deflect pays double
`parrySpeedGain` and full sentry posture, and which genuinely hurts if it lands. That gives the span a
phrase instead of a pulse and answers "why would I not deflect every bolt?" — because the big one is the
launch and your window must be free for it. Test: EditMode on the beat selector plus a shipped-value
assert. Breaks: it is a new tell; it must reuse the same 0.28 s cue and differ only in size and colour,
never in timing.

**P6 — Convert a missed deflect into lost time, not lost health.** *(data-only first)*
Feel: the punishment fits a speedrun.
Cheapest version: lean on the bolt's `parryPostureMultiplier` (`DataFactory.cs:249`, currently 1.2) so a
sloppy span leaves you staggered — which *is* lost time. A later version scrubs carried speed through an
existing motor entry point (never a velocity write, rule 10). Defer until a human has run a span.

**P7 — Perch placement becomes a constraint with a test.** *(tooling)*
Extend the arc report's SHOOTER PERCHES section to score each perch by the angle between the deflect
boost vector (the player's look at the deflect point ≈ the route tangent) and the route forward, and flag
a perch whose boost points off-route. Cheap, and it catches problem 3 at authoring time instead of in
playtest.

## Open questions for the user

**Answered by the user, 2026-09-06 — 1 and 2 are settled; do not re-ask.**

1. ~~Should a sentry be **killable at all** by reflected bolts?~~ **ANSWERED: yes, and it already works that
   way.** The user: *"this system is already in place it kills the enemy and they explode with the flare."*
   Reflected bolts kill outright (2–3 deflects) and the death detonates into the flare that the grapple
   then takes — a span IS clearable at range, and the dash line is the faster, showier one rather than the
   only one. P1's numbers must be written against a killable sentry.
2. ~~Should the sentry dash **cost** anything?~~ **ANSWERED: no.** The user: *"does nothing its free."*
   The two deflects that earned it are the whole price. Do not spend stamina or the dash cooldown on it.
3. On a span, do you want to be able to **run straight past** a sentry as a legitimate line, or should an
   unanswered sentry make the span materially worse?
4. Is P5 (a phrase, not a pulse) the direction you want, or should the span beat stay dead simple with
   all the depth in the geometry?

## Do first

1. **P1** — the Grunt arithmetic. Data-only, one line, plus the invariant test. The new verb does not
   exist until this lands.
2. **P2** — parry the bolt, not the bearing. Small, additive, and it removes the most likely "this feels
   broken" report from the first playtest.
3. **P4** — a visible "open" state. Presentation only, and it is what turns P1 from a mechanic into a
   moment.

Then play a span before anything in P5–P7 is built. Nothing on this page has been felt by a human.
