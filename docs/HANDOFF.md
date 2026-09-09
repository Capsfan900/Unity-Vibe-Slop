# Handoff — expanded realms, projectile recovery and enemy-AI audit

## What happened

2026-09-09. The Astra-led level, projectile, presentation and release-readiness pass is implemented and
fully integrated. The post-first-miniboss route is no longer a rigidly translated cramped block: T2 is a
broad local helix, T3 is a wider true wall-run span with a four-balloon alternate arc, and the later realm
and boss move farther down-route. All four solar arenas have larger visual shells and measured empty space
around their physical membranes, with obsolete interior court geometry removed.

Projectile enemies now share one reusable contact-planning contract. `ProjectileFlightMath` provides
allocation-free moving-target interception, capped homing and swept contact prediction. Authored
`ProjectileEngagementWindowDef` corridors audit whether each encounter has legal route contacts; they are
not runtime trigger volumes. Only the explicitly progress-gated T0 opening builds a volley coordinator.
Ordinary T1-T4 sentries are autonomous again and repeat their normal range/LOS/facing/cue-safe firing loop.
`VibeGame1/Projectile Encounter Report` audits any `LevelDefinition` at base, surge and maximum designed
speed. The former pill-shaped Heavy Sentry is a broad three-aperture stone reliquary and fires a rapid
three-shot, 0.42-second parry phrase followed by 2.4 seconds of quiet.

The requested enemy-AI follow-up found one lifecycle hole: `EnemyController` acquired the player only once
in `Start`, so a missing/replaced player or in-play domain reload could strand a valid enemy in Idle. It now
reacquires only while its target references are absent or inconsistent. No combat timing, moveset, state,
movement or perception mechanic was retuned.

The same integrated tree also contains the completed release console/F10 gate, native resolution and arm
settings, viewmodel and weapon/impact VFX pass, solar crossing time-warp sound, fog/lighting/horizon pass,
finished architectural materials, and agent-agnostic project dashboard updates requested in this workstream.

## State of the tree

- Baseline rollback tag: `pre-console-space-arms-2026-09-08` at `e2c2d43`.
- Recovery note: the original session completed implementation and verification but stopped before its
  documented commit was created. The resumed Astra integration audited and recovered that intact worktree.
- All required material/data/prefab/HUD/main-menu/level generators were run in the open Unity editor. The
  canonical `Level_01` scene and shipped data/prefab values are current. Two consecutive level reworks
  produced SHA-256 `7EE7FBB72B497D551673AC1C42E86C2DB90C31DE3142080FD19A9DCF925D4B89`.
- This pass is landed as one revertible commit titled `[Astra] Recover level, projectile and enemy AI pass`.
  The user's local `.claude/settings.local.json`, `Portraits/`, and `RouteShots/` capture
  output remain intentionally uncommitted.
- No subagent work remains in flight.

## Verification

- Full EditMode: **981/981 passed**, zero failures/skips, **158.0 s**, run 2026-09-09 on the final code and
  asset tree. Focused combined AI/projectile slice: **63/63**; enemy-family surface: **182/182**.
- Full FeatureTests: **802/802 passed**, zero failures/skips, **58.8 s**, from a fresh unpaused `Level_01`
  session after checking `GameManager.I != null` and `Time.timeScale == 1`.
- Affected EditMode surface: **178/178**; focused geometry/projectile surface: **72/72**; VFX pool:
  **15/15**. Projectile Encounter Report and Level Arc Report both pass.
- Whole-fight `DebugHarness` runs passed for `parry`, `boss` and `death`: the live ninja/knight completed
  attacks and executions, all three boss phases completed, and respawn rebuilt the enemy instance.
- Health Check reports **0 errors / 1924 warnings**. Offline runtime and editor assemblies compile with zero
  errors. The editor assembly retains 18 known
  warnings: eight obsolete `FindObjectsByType` calls in `LevelDescentProbe` and ten JSON-populated Forge DTO
  fields. These are unrelated to this pass.
- Heavy portraits and updated T2/T3 player-eye route captures were reviewed. They prove composition, not
  feel. T2 intentionally remains vertically layered despite its wider terraces.

Automated checks prove geometry legality, data ownership, projectile contact arithmetic, generated values,
state machines and effect budgets. They cannot prove player feel or visual comfort in motion.

## Do first next session

1. Human-play T2/T3 at normal and surge speed, including the first T3 wall run and alternate balloon arc;
   judge perceived solar breathing room, not only measured clearance.
2. Run past ordinary blue sentries and confirm they fire naturally throughout their readable range rather
   than only in narrow patches. Fight the Heavy Sentry and judge its 0.42-second three-parry rhythm,
   projectile cue/audio readability and 2.4-second recovery while moving.
3. Check fog/lighting, sphere-entry audio, weapon VFX and the first-person arm toggle at normal frame rate.

## Open questions for the user

Only human feel/appearance acceptance remains. No implementation or architecture decision is blocked.
