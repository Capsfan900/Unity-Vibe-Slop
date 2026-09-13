# Handoff — config refresh and project read-in (2026-09-13)

## Current state

**2026-09-13 session (Claude Code, Opus 5 lead):** docs and config only. No runtime or editor code, data, generators
or scenes changed.

- A Fable 5.1 read-only review of the whole project and an audit of its agent config were run.
- `7884456` `[Astra] Refresh agent config for the Level Studio era; authorship by role`:
  - **AGENTS.md:** authorship is now by role. Any frontier model may lead as Astra and change core systems;
    non-lead tiers refine only. Every commit carries `[<role>]` plus a `Model:` trailer.
  - **AGENTS.md:** the pipeline table gains 3b/4c/8/8a/10, Level Studio and the Projectile Encounter Report.
  - **AGENTS.md:** stale test counts are removed, EditMode verification goes through `QuickTestRunner`, the new
    docs are routed, and the spellbook and parry-capture controls are listed.
  - **Briefs:** all 8 updated. `level-designer` now owns Level Studio use, zone vocabulary and parry
    choreography.
  - **Skills and TOOLING.md:** `unity-editor`, `session-handoff`, `dashboard` and the Astra adapter are updated,
    and the TOOLING.md roster lists the briefs by tier.
  - Rollback tag: `pre-config-refresh-2026-09-13`.
- `9815e3e` CLAUDE.md gains "How to read the project as it is now", which records the user's direction:
  - one level (Level_01) polished with Level Studio and parry capture;
  - the Windows exe is the only target and WebGL is legacy;
  - the four duels and the Warden are off-limits;
  - the user records parry runs;
  - work on `master` and ignore the stray worktrees.

**Carried from 2026-09-12:** the Level Studio / parry choreography / dashboard plan
(`docs/superpowers/plans/2026-09-12-level-studio-dashboard-implementation.md`) is fully integrated. Rollback for
that batch starts at `pre-level-studio-2026-09-12`.

## Verification — inherited from 2026-09-12 (not re-run 2026-09-13; nothing testable changed)

- Full EditMode: 1212/1212. Level_01 FeatureTests: 813 passed, 0 failed, 1 intentional skip.
- Health Check: 0 errors. Projectile Encounter Report: PASS.
- Runtime and editor builds clean.
- Unity MCP was not connected on 2026-09-13, so no editor work was done.
- Human-only and still open: Level Studio edit/undo/recovery/apply acceptance, recording runs on the four stages and
  judging module rhythm, clicking through the dashboard.

## Next action

1. **The user records runs and uses Level Studio on Level_01.** This is the acceptance still pending from 09-12;
   support it and fix what they report.
2. **Offer the retry-coherence fix.** The user has not yet approved it. It covers these open findings from
   `docs/ASTRA-SYSTEMS-AUDIT-2026-09-07.md`, which bite on a level that is replayed constantly:
   - A01: pause restart deals 99999 damage.
   - A02: stamina and motor cooldowns survive death.
   - A03: execute waits run through pause.

   Also still open: A18 (no controller menu focus), A22 (the radio ducks boss music to 0) and A13 (Level_01
   `levelId: samplescene`).
3. **Surge duration discrepancy.** `Assets/Data/PlayerStats.asset` ships `generalParrySurgeSeconds: 3.2`, while
   ARCHITECTURE/DATAFLOW say 2.0. Ask the user which feels right after a run, then make the docs match.

## Open questions for the user

1. **Hook rule** (`ParryController.cs:248-259`): Grapple, Rebound and Deflect Sigil turn a matching turret's bolt
   that lands within 0.13 s of E, during the pull, into a Perfect. Keep it as a designed second timing source (then
   document it in ARCHITECTURE), or require the parry input?
2. Is gamepad support required? Menus have no focus and prompts show keyboard glyphs.
3. Restart from checkpoint currently kills the player (souls lost, bloodstain). Intended?

## Preserved user-owned files

Do not stage, remove, overwrite, or relocate:

- `.claude/settings.local.json`
- `Assets/Materials/M_Water.mat`
- `Assets/Scenes/Level_01.unity`
- `Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and `.meta`
- `Portraits/`
- all `RouteShots/` files and directories
- `output/` (untracked, unexplained on 2026-09-13; leave it)
- `Tools/dashboard/__pycache__/build_dashboard.cpython-312.pyc` (build artefact, modified; do not commit)
