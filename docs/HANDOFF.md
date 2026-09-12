# Handoff — spellbook, Pyre and traversal-readability pass

## Current state — 2026-09-12

`master` contains the implementation and generated assets for the persistent open spellbook, item casting,
Pyre fire slash, slide-only ice/water behavior, and five blue-Sentry route placements. The source tree is
verified and committed as `[Astra] Add spellbook Pyre slash and refine traversal routes`; resolve the exact
SHA with `git rev-parse --short HEAD`. It still needs the push and replacement Windows friend build/release
attachment tagged `playtest-2026-09-12-spellbook`.

The rollback point before this batch is tag `pre-flare-ice-refine-2026-09-12` at `250dc2e`. Preserve the
user-owned song and local art/capture folders listed below.

## What changed

- Visible offhand wand models are replaced by one generated `VM_Spellbook` kept open in the left hand.
  `WandData` / `WandController` remain internal compatibility names for the four riposte inscriptions.
- Ten book pages flutter, three loose pages flow around it, and the selected inscription or current FIFO item
  appears as a spell-tinted core/eight-rune halo above the book. Every accepted item casts through the book.
- Pyre uses `FireSlashFx`, a large weapon-originated fire sheet; damage follows the visible contact beat.
  The prior electrical library (`PyreArc`, `LightningEffect`, dormant `BoltCo`) is preserved for a future enemy.
- Water/ice applies its speed floor, conveyor and no-decay behavior only during an active grounded slide.
  Ordinary running, jumping and airborne carry use the normal motor laws.
- All five blue-Sentry perches have strict early visibility tests. The final T2 upper placement is world
  `(21,20,185)`, yaw `230°`, covering L9 with clear LOS, portal clearance and two high-speed contact samples.

## Verification

- Full EditMode: **991/991 passed**, 0 failed/skipped, 49.4 s,
  `TestResults/EditMode-20260912-095827.xml`. The two preceding full runs also passed 991/991.
- Fresh Level_01 FeatureTests: **813 passed, 0 failed, 1 intentional Sandbox-only skip**, 58.8 s, after
  `GameManager.I != null` and `Time.timeScale == 1` were both proven.
- Projectile Encounter Report: **PASS** for every campaign shooter at 11, 17.6 and 27.5 m/s.
- Level Arc Report: **PASS**, including every route hop, wall line, slide gate, balloon, water sheet and perch;
  no forced look-away.
- Offline compile: runtime 0 warnings/errors; editor 0 errors plus 18 known warnings.
- Health Check: no error section; 3318 existing broad serialized-null/audio warnings.
- Live player-eye screenshot inspected after the spellbook material/rune refinement. Human play still owns
  route cadence and fire-slash feel.

## Exact resume order

1. Push `master` plus the rollback tag; do not stage the preserved local files.
2. Rebuild the D3D11 Windows friend package with the local Level 01 song included, smoke-launch the exact EXE,
   update `friend-build-content.txt`/hashes, then attach the ZIP to the GitHub Release.
3. Human-play all five blue-Sentry beats and Pyre against live enemies; use the timing recorder if a bolt still
   feels late or inconsistent before changing global projectile timing.

## Preserved user-owned files

Do not stage, remove or overwrite `.claude/settings.local.json`, `Portraits/`, unrelated `RouteShots/`, or
`Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and its `.meta`.
