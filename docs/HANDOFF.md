# Handoff — open parry route, Heavy/Hook/items and final-ramp reliability

## Current state — 2026-09-10

The interrupted Astra pass is complete and verified. No agent work remains in flight.

Level 1 is opened across the post-opening spaces so movement and projectile parries have room. T1–T3 retain
autonomous blue sentries; the blue type was not retuned in this final repair. T0 remains an authored runtime
sequence: five Surge Turrets once, then the two Heavy Reliquaries alternate repeatedly. T4 is now an authored
three-member runtime sequence because its high-speed single-shot rhythm needs deterministic ordering.

The Heavy Reliquary globally fires a rapid three-contact phrase at 0.40 s contact cadence, then rests 0.90 s
from the final incoming answer. All three ordered projectiles from the same phrase must be Perfect to destroy
it; any block, hit, expiry or cancellation invalidates that phrase. Its reflected bolts do zero damage on the
first two answers, it has no posture/deathblow contract, and normal melee health damage still kills it.

The final-ramp failure had two real timing causes. `ProjectileVolleySequence.Advance` transferred one
shooter's personal refire cooldown to the next member, delaying later shots until they were behind the player.
Now different members observe only the sequence's 0.11 s recovery gap, while each shooter enforces its own
rest when selected again. T4 also uses an explicit zero first-member arm-up because its visible approach
already teaches the row. Its three Surge perches sit at ramp progress 24/36/48 m and fire at gates 0/14/28 m.
Projectile facing now uses the terminal incoming direction from the same accepted 120 Hz flight forecast.

Hook is retained and reworked: E hooks a turret, and only the matching real incoming projectile colliding
during the tight authored Hook timing can become a Perfect. That destroys the matching turret and primes one
airborne bonus dash-jump. Wrong, reflected, late or unrelated projectiles cannot satisfy it. Non-turret Hook
movement remains available. Wall Surge is retired without reusing its serialized enum value; Rebound and
Deflect Sigil are shipped data items. Perfect dash-jump and wall-exit timing now have actionable HUD cues.

The top-left status strip includes toggleable effects, carried/armed item state and the run soul contract.
Level 1 ships D–S split bonuses and requires the three sub-bosses, main boss and four regular-enemy soul
values. Generated data, prefabs, HUD, main menu, level scene and NavMesh are current.

## Verification

- Offline editor build: zero errors; 18 known warnings from probe API deprecations and Forge DTO fields.
- Health Check: PASS.
- Projectile Encounter Report: PASS at 11 / 17.6 / 27.5 m/s.
- Level Arc Report: PASS.
- Full EditMode: **935/935 passed**, zero failures/skips, 45.8 s.
- Final targeted level-export persistence guard: **1/1 passed**.
- Fresh unpaused FeatureTests: **801/801 passed**, zero failures/skips, 62.7 s.
- T4 live real-motor probe: 3/3 shots, 3/3 Perfects/Surge grants, no cancellations, run-out reached.
- T0 live real-motor probe: five Surge Perfects plus both Heavy 3/3 phrases, 11/11 total, 1.60x ladder.

Rollback tag: `pre-open-parry-route-2026-09-10`.

## Human playtest next

1. Play the final ramp normally and judge the readability/cadence of its three large Surge projectiles.
2. Judge the two opening Heavy 0.40 s triple-parry phrases with human timing and confirm a failed answer
   clearly resets the three-Perfect requirement.
3. Test Hook by pressing E on a turret and meeting its actual bolt; confirm the successful bonus airborne
   dash-jump is understandable, while an early/late Hook still performs movement without the reward.
4. Compare Rebound and Deflect Sigil, Perfect Jump cues, the opened post-opening route, status strip and
   D–S split/soul economy in a complete run.

## Preserved user-owned files

The music replacement under `Assets/Resources/Audio/Radio/`, `.claude/settings.local.json`, `Portraits/`
and unrelated `RouteShots/` were not staged or modified by this pass.
