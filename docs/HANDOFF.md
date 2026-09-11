# Handoff — restored vertical course, Insight flare routes and timing capture

## Current state — 2026-09-10

The interrupted Astra pass is complete and verified. No agent work remains in flight.

The previous openness pass explicitly deleted almost every named T1–T3 rail, wall, tower, lintel, recovery
post and connector ramp. That was the root cause of the level feeling flat. The same wide decks remain, but
the generator now rebuilds those authored structures idempotently: T1 has its rails/obelisks/lintel and
wall-run landing; T2 has its tower, buttress and east/west wall lines; T3 has its lintel, recovery posts,
walls, landing and four-balloon vertical chain. Turret perches were moved only where a restored structure
would obstruct the actual running line; every claimed shot corridor now passes the arc report.

The blue sentries remain mechanically unchanged. Their flare-assisted branches are now three authored
`InsightRouteDef` entries, one per parkour section, using explicit source spawners and entry/rejoin anchors.
The builder produces a large cyan hand silhouette with an `INSIGHT` label. Every marker is on the Sky layer,
has no collider/trigger and cannot affect traversal or NavMesh. These are optional faster/harder skill lines,
while the standard route still expects projectile parries to preserve speed.

Player-controlled projectile timing is no longer a black box. In editor/development builds, the backquote
console supports `timing start`, `timing stop`, `timing status`, `timing export` and `timing discard`.
`PlayerTimingCapture` keeps a bounded 30 Hz trace in memory, records exact parry presses and projectile
emission/cue/contact/result events, and includes player position/velocity/speed/slide state plus the firing
enemy data and spawner when available. It never drives input, combat or enemy decisions. Export is explicit
and local under `Application.persistentDataPath/timing-captures/`.

## Verification

- Runtime build: zero warnings/errors; editor build: zero errors and 18 known warnings from the existing
  descent probe and Forge DTO fields.
- Health Check: PASS with 1942 existing broad serialized-null/audio fallback warnings.
- Projectile Encounter Report: PASS at 11 / 17.6 / 27.5 m/s.
- Level Arc Report: PASS.
- Full EditMode: **943/943 passed**, zero failures/skips, 44.49 s.
- Fresh unpaused FeatureTests: **806/806 passed**, zero failures/skips, 58.4 s.
- Live timing-recorder smoke test: valid JSON, 229 samples over 8.77 s, zero dropped samples/events.
  Projectile events remain deliberately unproven by that stationary spawn test.

Rollback tag: `pre-restored-vertical-route-2026-09-10`.

## Human playtest next

1. Run every restored T1–T3 wall-run, wall-jump and vertical branch at speed; confirm the wider spaces still
   feel open and none of the restored silhouettes recreates the old cramped line.
2. Check that each cyan hand reads as a Neon White-style secret/Insight invitation and that its blue-sentry
   flare line is actually faster and harder than the standard route.
3. For remaining turret feel, capture three ordinary and three fast attempts: open the console, run
   `timing start`, close it and play; afterward run `timing stop` then `timing export`. Give the printed JSON
   path to the next session so shot gates can be tuned against player motion instead of automation.
4. Judge projectile visibility and parry rhythm at human timing. Do not retune the final-ramp turrets the
   user already approved unless a capture demonstrates a regression.

## Preserved user-owned files

The music replacement under `Assets/Resources/Audio/Radio/`, `.claude/settings.local.json`, `Portraits/`
and unrelated `RouteShots/` were not staged or modified by this pass.
