# Handoff — duo realms (2026-09-14)

## Current state

Committed on `master`, one commit after tag `pre-duo-realms-2026-09-14`:

- **Realms trimmed** (the user: "a little too large"). Every Level_01 cell is now 25 m floor, 37.5 m shell and
  20 m walls/ceiling (was 30 / 45 / 24). V18's grab lifts 11 m, so it still fits.
- **Two mini-bosses per realm, fought at once** (the user, 2026-09-14). `LevelDefinitionAuthoring.RealmPartners`:
  - T1: Seraph Lancer + Thirteenth Shade (`Spawn_Legendary_T1_Duo`)
  - T2: Orbit Dancer + Argent Halberdier (`_T2_Duo`)
  - T3: Ember Revenant + Pale Marionette (`_T3_Duo`)
  - T4: V18 Grappler + Cinder Judge (`_T4_Duo`). The two hardest, placed before the Warden.
  - Bench: Iron Penitent, Ashen Chorister, Drillmaster, Flurry Brawler v15.
- **Mechanism:**
  - `SolarRealmDef.partnerSpawnerName/Position/Yaw` → builder `MoveIntoRealm` → `BossArenaTrigger.partnerSpawner`
    (clears only when both are dead).
  - `RunSplitDef.alsoRequiredSpawnerNames` → `LevelRunScorer` closes the split on the last of the pair to die.
  - Existing `EnemyController.MaxSimultaneousAttackers = 1` keeps one wind-up at a time.
- **Numbers:** requiredRunSouls 5640. Splits renamed Lancer / Dancer / Revenant / Grappler / Warden, with
  S pars 65 / 70 / 80 / 75 / 40 (placeholders).
- **Core-system note:** none of the listed core systems were changed (motor, combat resolution,
  TimeScaleController, InputReader, enemy brains). Touched: `BossArenaTrigger`, `LevelRunScorer`,
  `LevelDefinition`, the builder and authoring.

Generators run: 8a Rework, 8 Build From Definition (Level_01), Rebuild NavMesh.

## Verification (2026-09-14, this session)

- Full EditMode: **1357/1357** (new `DuoSplitClosesOnWhicheverPartnerDiesLastAndNeitherIsARegularKill`).
- Play-mode FeatureTests on Level_01 (`GameManager.I` set, timeScale 1): **797 passed, 0 failed, 2 skipped**.
  The gate loop now also kills the partner and checks `DuoSealedWhilePartnerAlive`.
- Not re-run: Level Arc Report, Projectile Encounter Report, Health Check. Realms only shrank and route
  geometry is untouched, but run them once.
- **Human-only:**
  - duo difficulty and fairness (both at full HP)
  - whether the 20 m ceiling crowds the Lancer's hover or the Judge's storm
  - the placeholder split pars

## Next action

1. **New request, not started. The user said: "the fights need to be cinematic like a souls game."** Scope it
   with them first. Likely pieces:
   - boss intro (name card, fog-gate style entry, camera hold)
   - boss HP bars for BOTH duo members
   - music stingers and phase-change music
   - a deathblow/kill cam and slow-mo on the final kill
   - an arena-clear "VICTORY ACHIEVED" banner

   Check what already exists first (`HUDController` boss bar, `SolarTransition`, `TimeScaleController` for slow-mo)
   before building. HUD lives in the ui-designer lane; music in audio-engineer.
2. The user plays the four duo realms and reports. Retune HP and damage from data, not code.
3. Leftovers from 09-13: spell-orb readability capture, disc/wing SFX, V18 carry aim assist.

## Working rules learned

- **Inline first:** worker lanes cost more tokens than they save. Delegate only large, independent work.
- **Offline `dotnet build` fails in this shell** (NuGet `path1` null). Compile through Unity instead: refresh, then
  poll `isCompiling` and a reflection probe for the new symbol.
- **Never `git stash` with the editor open.**
- **Ask before stopping play mode.** The user may be playing.

## Preserved user-owned files

Do not stage, remove, overwrite, or relocate:

- `.claude/settings.local.json`
- `Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and `.meta`
- `Portraits/`
- all `RouteShots/` files and directories
- `output/` (untracked)
- `Tools/dashboard/__pycache__/build_dashboard.cpython-312.pyc` (build artefact; do not commit)
