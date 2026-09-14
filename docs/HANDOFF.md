# Handoff — boss roster pass (2026-09-13)

## Current state

The approved plan `C:\Users\tyler\.claude\plans\misty-cuddling-bachman.md` is complete on `master`. Rollback for the
whole pass starts at tag `pre-boss-roster-2026-09-13` (after `ce4833f`, the user's WIP commit). Each stage is one
revertible commit:

- `c607d29` **Locomotion fix** (shared `PuppetVisuals`): the frozen run glide was a crossfade restarted every frame. Adds
  walk/run dead bands, stride-matched rate, soft holds and footfall dust.
- `bfab618` **Spell orbs**: per-spell `SpellOrbProfile` shapes and motion, `SpellOrbShell` URP shader, luminance
  normalisation, wheel feedback. *Not yet eyeballed in captures.*
- `3afe05f` Shoulder-charge push-off VFX.
- `71e8d2a` **Solar Realms**:
  - floor 30 / walls and ceiling 24, cells 100 m apart
  - new T4 Grappler realm (violet sun) before the Warden
  - slide-gate bars removed
- `6ed02f0` **Orbit Dancer**: forge-generated (`ai_skelly_tool/output/orbit_dancer_v1`); ricochet discs. Adds the
  `Projectile.OnWorldContact` / `Redirect` hook.
- `601d70e` **Core seams**:
  - `PlayerHitDeflection` / `IPlayerHitDeflector` + `PlayerCombat.ReceiveRecoil`
  - `FirstPersonMotor.BeginCarry` / `EndCarry` + `IsCarried`
- `2567130` **Seraph Lancer**: forge-generated (`seraph_lancer_v1`, fifth attempt); Sky Verdict hover javelins.
- `a9b4dee` **Cinder Judge Aegis shield**:
  - `cinder_judge_v2` forge retrofit; same rig, ShieldBash and ShieldRaise added
  - `CinderJudgeShield` deflects swings; Blade Throw or a Perfect bash shatters it
- `30519de` **V18 Skyfall Suplex**: `V18Grapple` grab → carry 11 m → throw → unblockable slam.
- `1e2b155` **Roster seated in Level_01** (`LevelDefinitionAuthoring.SeatBossRoster`):
  - T1 Seraph Lancer, T2 Cinder Judge, T3 Orbit Dancer, T4 V18 Grappler, then the Warden
  - splits Lancer / Judge / Dancer / Grappler / Warden
  - requiredRunSouls 3840

Generators run since the last code change: 4a, 3, 3b, 4, 4b, 4c, 7, 8a, 8, NavMesh (all through the open editor over
MCP).

## Verification — 2026-09-13 (run this session)

- Full EditMode: **1356/1356**. Play-mode FeatureTests on Level_01: **789 passed, 0 failed, 2 skipped**:
  - `Legendaries_LegacyBodies` now belongs to the Sandbox
  - `V18_SandboxSpawner` is Sandbox-only
- Level Arc Report: clean. Projectile Encounter Report: PASS. Health Check: 0 errors. Dashboard tests 5/5.
- Live Sandbox smokes (MCP):
  - locomotion loops while chasing
  - discs launch, bank and bounce
  - javelins rise, 3 throws, descend
  - shield deflects a real swing (+18 player posture) and a thrown blade shatters it
  - grab carries the player 10.6 m, throws 5.8 m and slams for 26
- **Unproven, human-only:**
  - feel and fairness of all four signatures
  - split pars 55/60/70/60/40
  - spell-orb readability
  - the grab's Perfect-avoid path in real play
  - fights inside the bigger realms

## Next action

1. The user plays Level_01 T1 → Warden and the Sandbox pads, then reports what feels bad. Retune from data
   (DataFactory blocks, MiniBossFactory component numbers), not code.
2. Screenshot or eyeball the spell orbs (item 1b was never captured).
3. Optional:
   - light-wing and disc SFX from the audio lane
   - a positional-audio overload for disc bounces
   - camera aim assist toward V18 during the carry (plan item, not built)

## Working rules learned this session

- **Inline first:** the user found the worker-agent lanes cost more tokens than they saved. Delegate only large,
  independent work, on cheap models.
- **Forge prompts:** use separated legs, fitted shorts or trousers, bare arms. "knight/lancer" concepts hide knees
  and fail the rig preflight. Launch builds with bash file redirection, not PowerShell `*>>`.
- **Clip travel:** trust the Unity-import clip travel over the Blender reading (ShoulderCharge 3.12 → 3.32).
- **Never `git stash` with the editor open:** it swaps files under Unity, and a regenerated `.pyc` blocks the pop.

## Preserved user-owned files

Do not stage, remove, overwrite, or relocate:

- `.claude/settings.local.json`
- `Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and `.meta`
- `Portraits/`
- all `RouteShots/` files and directories
- `output/` (untracked)
- `Tools/dashboard/__pycache__/build_dashboard.cpython-312.pyc` (build artefact; do not commit)
