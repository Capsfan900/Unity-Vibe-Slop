# Fog and incoming projectile follow-up

User-authorized scope, 2026-09-07: visible drifting mist around the route AND stronger distant haze;
more curved/erratic-looking enemy shots. The violet sentry reward flare is explicitly unchanged.
Lead reviewed the design; Sol implements bounded lanes. Attempts to create an Astra/high design agent
were rejected by the thread limit, so this is not attributed to an Astra review.

## Fog lane

Existing blue linear fog is enabled, 36–170 m. A same-frame 170/140 comparison from the new starting
area changed mean RGB by only 0.002% of channel range: distance fog alone will not deliver the user's
request for visible route atmosphere. Keep the existing hue/start and shorten the distant ramp to
140 m, preserving the sky geometry and near landing/combat visibility constraints. Add soft drifting
world-space mist through the player prefab's presentation components, sharing DeathMist's generated
texture with balanced ownership and an ambient-only material clone for camera fading. No lights,
collisions, shadows, gameplay writes or new input.

Budget: one particle renderer, maximum 48 particles, scaled time, bounded cold additive contribution,
near-camera fade, immediately populated at spawn, slow drift and world-space persistence when looking
around. Review the actual downhill route: a camera-relative slab that merely follows the view is not
acceptable, nor is mist above the whole descent with no visible route atmosphere.

## Incoming shot lane

History shows the violet reward flare remains ballistic and the amber shots retain capped homing.
No committed erratic/wobble version was found. A two-point tangent streak hides much of the homing curve.
This pass improves visual flight; it does not claim to restore an identified historical algorithm.

Preserve the logical projectile root, homing rate, speed, hit radius, forecast/arrival calculation,
damage, parry cue and deflect impulse. A separate visible core receives deterministic scaled-time weave,
maximum 0.34 m. Offset must reach zero by cue onset and remain zero after cue latching, even if distance
later increases. Reflection is straight immediately. Legacy root-renderer callers must never have their
logical transform displaced by visual code. Record a fixed-budget curved trail from actual visible head
history; interpolate sample crossings at low frame rates and clear the incoming trail on reflection.

## Lead verification

- Read generated prefab/scene values; preserve reproducibility through existing factories.
- Compile in Unity and read the console after the batch.
- Run quick EditMode and fresh play-mode feature coverage; report actual failures without attributing
  changing timing/target failures to a cause that has not been established.
- Capture matched camera views with mist/haze off and on, and inspect near footing and enemy cues.
- Check actual particle counts/materials, one renderer, no shadow/collision/light modules, no per-frame
  allocations from projectile trail buffers; record frame-time observations without calling editor
  samples a player-build benchmark.
- Exercise real incoming shots, cue convergence, reflection and the descent's three surge grants.
- Commit the fog and projectile lanes separately. Restore tag before both: `pre-fog-flare-2026-09-07`.

Starting-state caveat: the spawn correction passed 693 quick EditMode checks and 70 live LevelStructure
checks plus a real main-menu load. Its full feature run was 770/777; trail, grapple and flask failures
remain undiagnosed. The earlier full 777/777 result predates that spawn correction.
