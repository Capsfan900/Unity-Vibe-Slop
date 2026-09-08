# Handoff — crest atmosphere, sun opacity and turret reliability

## What happened

2026-09-07. The user asked for four things after the two-ramp / solar-realm work:

1. Fog "looks like a patch meant for that smaller section" from the new higher spawn.
2. The spheres should be **more opaque, not see-through** from outside — the only sphere change.
3. The first ramp a little longer and wider again.
4. The turret AI "works but is a bit jank" — the user confirmed **tracking is good**; the
   complaint is shot timing and projectiles missing.

Delivered as one `[Sol]` commit, with Astra consulting on engineering review.

- **Cloud sea** is now 900 x 1400 m at y -5 on a 90 x 144 grid (13,195 vertices), so its edges sit
  beyond the 300 m route camera from the raised crest. A cloud-only 80..280 m haze keeps bank detail
  past the unchanged 36..140 m route fog and settles into fog colour before the far clip.
- **Solar shells** use premultiplied blending with `_SurfaceOpacity` 0.92 on the exterior theme
  materials, so they attenuate the courts behind them instead of only adding glow. Zero surface
  opacity reproduces the previous additive look exactly and is kept for the corona and for the
  interior realm ceiling (which also keeps its serialized 0.20 emission override).
- **Opening ramp** is 120 m run / 30 m drop / 14 m wide at the same 1:4 grade, bottom still z -15.8;
  spawn `(0, 30.3, -139)`. The same five turrets spread to progress 22/47/71/96/113 m.
- **Turret reliability**: relative swept-sphere contact (both bodies' motion between frames, with
  teleport/hitstop history guards), a closing-rate ETA that feeds cue/weave/BoltRegistry, and an
  opening-only 1.25 s deadline for an unresolved launched shot. **Lead, homing, bolt speed and the
  tracking the user likes are unchanged.**

## Verified

- Quick EditMode **732/732**; full EditMode **870/870** (227.9 s,
  `TestResults/EditMode-20260907-212401.xml`); full FeatureTests **808/808**.
- Live opening probe at **60 fps and 30 fps**: five distinct parries on the slope, full **1.60x**,
  run-out reached. `TestResults/crest-polish/opening-60fps.txt`, `opening-30fps.txt`.
- Health Check **0 errors / 1764 existing warnings**. Both offline assemblies compile.
- Eight real renders in `RouteShots/crest-polish/` — crest, overview, exterior suns, interior.

A probe-side trap is recorded in ENGINEERING-LOG: the automatic parry forecast was landing exactly
on the 0.13 s perfect-window boundary, and one 978 ms editor stall invalidated a 30 fps trial.
Those were tester corrections; no combat window moved.

Restore tag `pre-crest-polish-2026-09-07` at `16443c0`. Reverting this one commit undoes the pass.

## Do first next session

1. **Only the user can judge feel.** Play the opening: does the five-shot ladder read fairly at the
   new length, and do the suns and cloud ocean look right from the crest?
2. Standalone/WebGL GPU cost of the 13k-vertex cloud sea and the four opaque shells is unmeasured.
3. In-flight UI work follows this commit — see the next handoff section once it lands.
