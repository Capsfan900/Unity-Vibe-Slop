# Longer opening and enclosing suns — 2026-09-07

User-approved refinement, implemented by Sol 5.6 with Astra engineering review.

- Extend only the opening descent from 36 m to 108 m and width 10 m to 12 m, retaining its 1:4 slope and existing bottom join. Move the start/pedestal to the new crest. Preserve the complete original route and later ramp.
- Space the five existing surge turrets down the hill, left/right/left then right/left above a connected offset floating dais. Gate the ordered sequence on route progress and retain existing projectile, parry, movement and surge values. Start readiness deadlines after reaching each gate.
- Grow exterior sun meshes to radii 22/23/22/31 m around fixed centers. Keep teleport radii separate so retries and returns retain their tested behavior. Check old floor, wall, pillar, gate and torch bounds fit inside.
- Extend the cloud bed and kill-zone coverage behind the new crest.
- Regenerate canonical data and scene; prove idempotence, actual five on-slope contacts/full 1.60 boost, full EditMode and FeatureTests, and review rendered captures.

Restore point: pre-long-opening-2026-09-07 (1275950). Deliver as one revertible commit.

Astra corrected an early speed estimate during review: the motor's final horizontal clamp is 27.5 m/s. Local downhill impulse preservation does not bypass that clamp. No motor tuning is changed.