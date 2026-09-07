# Two downhill ramps, complete Level 1 route

The user clarified that the late descent is welcome but must not become the level's starting point.
Preserve that encounter, prepend a second ramp before the original Ground_Start, and spawn on the
new opening crest. Astra/high owns authoring and tests; the lead owns Unity generation and verification.

- Opening crest: 10 m wide, top y9, z -60 to -51.6; spawn `(0,9.3,-55)`, yaw 0.
- Opening descent: 36 m run, 9 m drop, 10 m wide, z -51.8 to -15.8.
- Flat run-out: top y0, z -16 to -7.8, overlapping Ground_Start's rear edge at z -8.
- Move the starting weapon pedestal to `(3,9,-55)`; retain Checkpoint_1 in the original starting area.
- Preserve the late 48 m / 12 m descent, three turrets, boss approach and existing campaign sections.
- Extend the kill plane behind the opening while retaining coverage of the complete original route.

Author through an idempotent LevelDefinitionAuthoring pass. Do not change movement, combat, enemy or
VFX systems. Verify generated data and scene, full EditMode geometry tests, Play-mode feature coverage,
real main-menu load, pre-checkpoint respawn and physical traversal into the old starting deck.
Restore tag: `pre-opening-ramp-2026-09-07`.
