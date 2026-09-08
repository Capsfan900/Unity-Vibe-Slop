# Crest atmosphere and turret reliability — 2026-09-07

User-approved scope: cloud ocean must read broadly from the higher spawn; exterior suns should be
more opaque; opening ramp slightly longer/wider; shot timing and missed contacts need refinement.
The user explicitly likes projectile tracking. Preserve tracking, homing, projectile speed, lead,
parry windows and all core movement tuning.

Implementation lanes:
- Lead: fixed cloud sea 900x1400 m, 90x144 grid, y -5; cloud-only haze 80..280 m preserves bank detail beyond
  the unchanged 36..140 m route haze. Exterior shell opacity .92 using premultiplied blending; zero
  surface opacity reproduces existing additive ceiling/corona. Persist ceiling override on component.
- Sol projectile worker: swept relative contact, teleport-safe target history, relative closing-time
  cue/registry estimate. Tracking remains unchanged. Tests cover frame rates, hitches and real misses.
- Sol level/sequence worker: 120x14 m opening, same 1:4 slope and bottom -15.8; turrets/gates spread
  proportionally. Opening-only 1.25 s missed-bolt deadline, safe owned-bolt cleanup/reset/re-enable.
- Astra: independent engineering review. Existing LOS helper already checks three body heights;
  no speculative LOS changes are included.

Verification: regenerate materials+canonical data/scene; read serialized values; inspect actual crest
and exterior/realm renders; real five-parry slope probe with cue/contact intervals; quick tests first,
full level suite after final geometry, fresh full feature suite, health and offline builds. Preserve
user local settings and save a single revertible commit after verification.

Restore tag: pre-crest-polish-2026-09-07 at 16443c0. Unity was confirmed stopped before source edits.
