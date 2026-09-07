# Opening turrets and solar boss realms

User request: add the missing turret encounter to the opening ramp, then replace the four boss courts'
presentation with large rotating, glowing sun-like spheres at their existing positions. Crossing a sphere
enters an enclosed realm matching that sun and containing the existing Souls-style opponent.

Sol 5.6 implements; Astra/high consults on design and engineering. The lead owns Unity generation,
visual review and verification. Restore tag: `pre-solar-realms-2026-09-07` at `0db52f0`.

## Design

| Court | Floor-top anchor | Sphere radius | Existing opponent |
|---|---|---|---|
| T1 | (0,4,87) | 12 m | Legendary_Ninja |
| T2 | (0,20,170) | 13 m | Legendary_Knight |
| T3 | (0,28,270) | 12 m | Legendary_Spellsword |
| Final | (0,16,390) | 18 m | Boss |

Each sun retains the court's horizontal anchor. Raise its visual center above the floor so its lower
surface intersects the approach. Rotate asymmetric plasma patterns and coronas; keep the physical
realm floor and enclosure stationary. Use matching colors inside, with darker backgrounds around
the opponent so attack cues stay readable.

Realm cells are disconnected parts of the same generated scene. Keep spawners active for the existing
LevelManager discovery/reset flow, and preserve authored exterior anchors for repeated level generation.
Optional realm data supplies the built spawner's new position. Four existing BossArenaTrigger objects
continue owning encounter completion and gates; portal entry uses FirstPersonMotor.Teleport.

Mini-boss victory unlocks an intentional return portal onto the onward route. The final boss retains
the existing level-clear behavior. Death retries from the normal exterior checkpoint, with bloodstains
recoverable by re-entering. ResetEnemies must reset both encounter gates and portal state.

Opening encounter update: five existing pshooter_enemy03 turrets in LEFT, RIGHT, LEFT order, followed
by two above on an attractive floating platform. The upper pair fires sequentially with enough time
for distinct parries. The intended run deflects ALL FIVE and earns the full existing speed boost.
Dedicated Sol agent `five_turret_sol` owns this sequence. Preserve the full 10 m center lane, opening
spawn and late-ramp encounter. Choose placement and any encounter-local scheduling from actual attack
lead and downhill interception, not only static range; avoid global shooter or movement retuning.

The final stepped U-shaped dais carries the upper pair side by side at `(-2.4,8.6,15)` and
`(3.2,8.6,15)`. Its front terrace is centred at `(0.75,8,16)` and its underside clears the route by
at least 6 m. The original rear stagger made the fifth contact cross the first jump and lose a stack;
two live runs of this closer arrangement earn all five grants and reach 1.60x before that jump.
The encounter uses an optional data-authored coordinator: one incoming shot at a time, advance after
a real deflect or the incoming bolt resolves, then a short launch gap. A finite readiness timeout
prevents a missed shot from silencing the remaining row. Existing autonomous shooters retain their
default behavior. The current turret asset already supplies five stacks at 0.12 each, capped at 1.60x;
each grant refreshes the existing 1.5 s stack timer. Actual contact spacing must be proven live.

Realm cells sit at x 700, outside the player camera's 300 m far plane, so their enclosures cannot
appear as extra planets above the campaign. Solar bodies stay saturated; moving bands carry the
glow. The smaller ceiling disc persists its reduced opacity through a serialized visual setting.

## Verification

- Regenerate twice and compare authored data; verify exact exterior anchors and both ramp encounters.
- Check real scene references, enclosure colliders, disconnected NavMesh islands and boss discovery.
- Cross each sphere into its correct realm. Return stays locked while the keeper is alive; winning
  opens it and crossing it returns beyond the sphere without immediately entering again.
- Check death/retry, recoverable bloodstain, reset/re-entry and final boss completion.
- Update stale F5/debug boss entry coordinates to resolve the authored final entrance.
- Run full EditMode including level-line checks, fresh FeatureTests, Health Check and visual captures.

Implementation and verification are complete: full EditMode 861/861, final quick 723/723, full
FeatureTests 804/804, both live five-parry runs and portal lifecycle checks passed. See
VERIFICATION-REPORT.md for evidence and limits. Passing these checks does not establish human combat
feel or standalone rendering performance.
