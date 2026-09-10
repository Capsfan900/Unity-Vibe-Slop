# Handoff — projectile AI, run scoring, status stacks and parry feel

## What happened

2026-09-09 continuation. Astra led the architecture and final safety review while cheaper agents audited
offline compilation, UI hierarchy, documentation and requirement coverage. The ordinary blue
`pshooter_enemy01` traversal sentries are autonomous and fire repeatedly through tight parkour whenever
their exact predicted bolt line is clear. Brief rail/ledge occlusion no longer charges their 0.7 s acquire
delay again, transient rejected plans retry after 0.08 s, and backpedalling uses the player's actual look
direction rather than movement as a facing proxy. Every emission still passes range, LOS, frontal-arrival,
solid-obstruction, contact and cue-safety gates.
The T3 Heavy retains its conservative 1 m forecast but may leave only the exact support collider detected
beneath it; solid lines, buried muzzles, siblings and later obstructions still fail closed. Surge/ramp
turrets retain their already-correct tuning and do not inherit any of these tight-route exceptions.

Runtime steering, contact prediction and cue timing now share `ProjectileMath.ForecastTargetVelocity`:
motor velocity is authoritative and the fallback uses `TimeScaleController.PlayerDelta`, so world hitstop
cannot manufacture a huge target velocity and suppress the warning. F8 god mode now remains damage immunity
without muting a valid Perfect parry, reflection or speed reward; deathblow execution invulnerability still
rejects attacks before parry resolution.

The top-left HUD now presents the run requirement as two levels: primary `RUN earned/required`, then quieter
FOES/SPLITS context. Its 184 px panel is sized for the maximum eight shipped rows and still supports optional live
status-effect rows. Four restrained marks around the crosshair reinforce only an already-cued projectile
inside the existing 0.28 s action window; they do not aim, select or reveal attacks early. A
successful non-turret Perfect grants a +0.12 speed stack, capped at five, with one-stack 2.0 s decay; the
F1 developer toggle hides only effects. Level_01 now has authored D–S split bonuses and a 3560-soul
baseline equal to all three sub-bosses, the Warden and four 40-soul regulars, with all encounter gates
required. Parry presentation uses the same source direction as combat, the rendered camera eye, a sharper
viewmodel kick, and a shorter 0.35/0.12 s chromatic contact.

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

- Baseline rollback tag for this focused continuation: `pre-projectile-ui-pass-2026-09-09`.
- Recovery note: the original session completed implementation and verification but stopped before its
  documented commit was created. The resumed Astra integration audited and recovered that intact worktree.
- All required material/data/prefab/HUD/main-menu/level generators were run in the open Unity editor. The
  canonical `Level_01` scene and shipped data/prefab values are current. Two consecutive level reworks
  produced SHA-256 `7EE7FBB72B497D551673AC1C42E86C2DB90C31DE3142080FD19A9DCF925D4B89`.
- The prior recovery is commit `7be071a` (`[Astra] Restore projectile AI, run scoring and parry feedback`).
  This focused continuation is one revertible Astra integration containing code, generated HUD/scene,
  tests and system maps together.
  The user's local `.claude/settings.local.json`, `Portraits/`, and `RouteShots/` capture
  output remain intentionally uncommitted.
- No subagent work remains in flight.

## Verification

- Full EditMode: **1017/1017 passed**, zero failures/skips, **176.9 s**, on the final code and asset tree.
  Focused affected surface: **85/85**; the final seven new regression tests passed **7/7**.
- Full FeatureTests: **804/804 passed**, zero failures/skips, **58.7 s**, from a fresh unpaused `Level_01`
  session after checking `GameManager.I != null` and `Time.timeScale == 1`.
- Live Level_01 probes: all five shipped ordinary placements emitted while the player moved through T1,
  both T2 layers and T3; the lower T2 also fired during the look-back/backpedal case. A real blue bolt
  Perfect reflected and paid one 1.12x stack; the T3 Heavy emitted exactly three from the route and a
  temporary solid blocker rejected its next launch. Ramp turrets were not retuned.
- Whole-fight `DebugHarness` runs passed for `parry`, `boss` and `death`: Spellsword/Knight completed with
  8/8 perfect deflects and executions, all boss phases completed, and respawn rebuilt the enemy instance.
- Health Check reports no error section / **1932 warnings**. Offline runtime and editor assemblies compile with zero
  errors. The editor assembly retains 18 known
  warnings: eight obsolete `FindObjectsByType` calls in `LevelDescentProbe` and ten JSON-populated Forge DTO
  fields. These are unrelated to this pass.
- Heavy portraits and updated T2/T3 player-eye route captures were reviewed. They prove composition, not
  feel. T2 intentionally remains vertically layered despite its wider terraces.

Automated checks prove geometry legality, data ownership, projectile contact arithmetic, generated values,
state machines and effect budgets. They cannot prove player feel or visual comfort in motion.

## Do first next session

1. Human-play the tight T1–T3 parkour at normal and surge speed. Confirm the frequent blue shots produce
   useful movement choices rather than noise, and judge the Heavy's 0.42-second three-parry rhythm while moving.
2. Complete a scored run and judge the two-level top-left status/run display, subtle crosshair projectile
   bracket, D–S split payouts and 3560-soul gate.
3. Judge the tighter parry recoil/chromatic contact and projectile cue/audio with real human timing.

## Open questions for the user

Only human feel/appearance acceptance remains. No implementation or architecture decision is blocked.
