# Rolling clouds beneath the level

The user's clarified target is an ocean of rolling, wispy clouds below the map structures, visible
across the lower view and tying the whole level together. The existing player-emitted route mist
does not satisfy that shape. Replace that shipped treatment with a broad world-space cloud sea;
retain distant atmospheric haze and the existing combat effects.

5.6 Sol at extra-high effort owns the presentation implementation. The lead owns editor generation,
visual review and verification. Restore tag: `pre-cloud-ocean-2026-09-07`.

Acceptance:

- A continuous cloud bed beneath the full route, including the new opening and the boss end.
- Broad rolling billows with finer drifting wisps, visible motion and soft variation rather than a
  featureless flat sheet or isolated puffs following the camera.
- Cloud tops below the lowest platforms; no colliders, gameplay water, damage, movement or cue changes.
- Clear platform silhouettes and landing edges; the sea reads as atmosphere, not a walkable surface.
- Regenerable scene/material wiring, bounded rendering cost and no dependence on camera rotation.
- Inspect matched real renders at the opening, original start, an elevated span and the late descent;
  inspect temporal change from a fixed camera. Check pause behavior and generated material references.

The implementation uses the project's installed URP APIs. Reference:
[Unity's custom URP shader structure](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/writing-shaders-urp-basic-unlit-structure.html).
Editor verification does not establish standalone/WebGL performance or final artistic approval.

Implemented and verified 2026-09-07. The first bright contour treatment was refined into softer
cloud banks; shipped material uses billow/wisp scales 0.03/0.11 and wave height 1.5 m. Camera renders
from all four planned route heights, temporal motion and pause behavior were checked. Quick EditMode
709/709 and full FeatureTests 777/777 passed. See VERIFICATION-REPORT.md for evidence and limits.
