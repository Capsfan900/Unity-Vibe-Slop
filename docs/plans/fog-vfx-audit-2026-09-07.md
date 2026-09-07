# Fog and VFX audit — 2026-09-07

Requested during the final-descent implementation. Read-only investigation; no graphics tuning changed.

**Fog and several VFX were built. The broader graphics-polish proposal was only partly implemented.**

- Fog: commit `8d852bc` implemented blue `#0E1C34`, linear 36–170 m. Live Unity MCP readback from
  `Assets/Scenes/Level_01.unity` this session confirmed `fog=True`, RGBA `(0.055,0.110,0.204,1)`, start 36,
  end 170. Historical comparison recorded only about 0.8% frame difference at the 91 m approach;
  the subtle result was intentional. See `ProjectSetup.cs` shared fog constants and `ANIMATION-VFX.md`.
- A real earlier application failure: commit `4fdf763` records a rebuild against an empty scene.
  Scene-local fog/ambient/sky changes consequently appeared ineffective. That session corrected the
  open scene and rebuilt Level_01.
- VFX are wired: four mist wisps on `pshooter_enemy01.prefab`, enemy `DeathMist` through
  `EnemyController`, `WeaponImpactFx` through `WeaponController`, and the perfect-parry shockwave
  through `ParryImpact`. Relevant commits: `25e5498`, `14b2bd8`, `79e5a1f`, `37fd8d9`.
- Structural smoothness, SMAA and PC SSAO in `graphics-polish-2026-09-06.md` remain proposed:
  shipped Stone/Ground/Platform smoothness is zero, Player camera uses FXAA, PC SSAO is inactive.
  Commit `3efd256` explicitly called the proposal “plan only”; later history implements fog/A5,
  not the complete proposal. No subsequent authorization for every proposed graphics change was found.

Documentation traps: `ProjectSetup`'s success log still prints the obsolete fog values even though its
assignments use the new constants. The graphics plan's warm-sandbox-ambient observation predates the
`bd262cb` fix; `SandboxBuilder` now reads the shared constants.

Further graphics work should be a separate reviewed change, measured from identical player-height
captures with real HUD/VFX and frame-time checks. The audit proves settings and wiring; it does not
establish that every effect reads well in motion.
