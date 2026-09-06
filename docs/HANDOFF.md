# Handoff — state of play

**Rewritten at the end of every session; describes a moment, not the project.** Read this first when picking
up where the last chat stopped, then [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md).

Last session: **2026-09-04, late evening** (Fable 5.1, four parallel forks).

## What happened

**Latest (2026-09-06 midday, Fable 5.1):** the span shooters are SENTRIES (`EnemyData.rangedOnly`: never melee,
hold the perch, wake at 32 m with a three-line sight check, fire on a fixed metronome with 80% velocity lead and
a launch that slows inside 11.5 m so the near edge is 3 m). New SENTRY DASH: DASH at a staggered sentry pulls you
to it and executes, with a new `Sfx.Teleport`. HUD: BEST RUNS pane sized to the board, the level-editor pane's
root (not its glass) is what F10 toggles — the empty black pane is gone. Generators 3, 4, 5 re-run; EditMode
561/561 (full), feature 747/747. **Not played by a human yet** — the rhythm, lead and dash feel need the user.
Still nothing committed.

**Latest (2026-09-05 late morning):** projectiles retuned (32/28 m/s, 10–30 m band, glow exception, 9 m/s parry gain), three perches re-aimed for the new band, the arc report now reads the band from data; quick suite 420/420, full 558/558 (before the last two perch edits, which only changed covers lists), feature 747/747. In-game level editor finalized and SHELVED; levels are authored per LEVEL-AUTHORING-TUTORIAL.md. Still nothing committed.


1. **THE ARGENT HALBERDIER** (`Legendary_Halberdier`, from `ai_skelly_tool/output/a_towering_swift`) is on
   its sandbox pad at x 0.75, wake switch "ARGENT HALBERDIER", SpawnEnemyInFront index 8. First forge body
   with GENERATED per-character attack clips (nine, six travelling) and an albedo texture. New machinery:
   `EnemyAttackData.clip`, the named-clip table on `PuppetVisuals`, `TravelRoot` + `CompensateTravel`
   (root motion is NOT extracted on a Generic rig — see ENGINEERING-LOG), `ModelSpec.zShift/albedo`,
   `ForgeClipSplitter` reads the manifest's `root` block and the tool's skin contract.
2. **The player has legs** (`PlayerBody`, under the root) that swing forward into view on a slide, plus a
   heavier slide: eye plop on a spring, a held lens rumble, kick pitch 1.8°, a plant on stand-up.
3. **The pivot's first three pieces** — balloons, water, the grapple exit burst — are built as data + code
   and placed in the sandbox movement yard (`Yard_Water` x 40..100, `Yard_Balloon_1..3` at x 110). Levels
   are NOT reworked yet and the in-game editor is NOT started. See BACKLOG §0 for the intended shape.
4. `McpReconnect.cs` re-arms the MCP bridge on every domain reload (the editor lost the startup race).
5. `Tools/measure_forge_fbx.py` measures a forge FBX in Blender when the editor is unavailable.

## State of the tree

- **Nothing from this session is committed.** ~200 changed/new files including regenerated
  `Assets/Data/**`, `Assets/Prefabs/**`, `Assets/Animation/**`, `Assets/Scenes/Sandbox.unity`, the
  ArgentHalberdier FBX/meta and new materials. Commit before any large refactor (CLAUDE.md).
- Every generator has been run in the live editor after the last code change: `2`, `3`, `4`, `4a`, `4b`,
  `7`, Health Check (no errors; the 1321 warnings are the pre-existing HUD `SettingsMenu.slider` nulls).
- EditMode: **552 / 552, 0 skipped** (2026-09-05 09:40, full run, 213 s). The three perch pins closed by MOVING perches: T1_Perch_E to (11, 3.5, 63) covering the wall-run landing + Stone_5 (a line to the causeway centre crosses T1_Wall_Causeway), T2 spawns swapped so GruntB stands inside the west wall at (-7.5, 4, 110.5) between the landing and the mount, both T3 perches beside T3_Wall_Span at (-7.5, 24, 217.5) and (11, 31, 227.5). No span assertion touched.
- Feature suite: **747 / 747, 0 skipped** (2026-09-05 09:45, fresh session on the reworked Level_01). The forgiveness rig now starts 0.2 m before the edge; its miss case is proven as "never on the ledge top" — the capsule pushed into the ledge's side hovers at y -58.1 instead of sliding to the floor, worth a look by the forgiveness owner.
- Level editor (finalized 2026-09-05, shelved as a tweak tool): press-frame placement, self-ray-free grab, surface debounce, one size number + readout, Esc cancel, Ctrl+Z/Y, Ctrl+D, arrow nudge, P / PLACE HERE, grid decal; EditMode 17 + feature 10 green; five new actions in the input asset.
- Level editor: the flashing map (editor ground coplanar with every floor) and the dead piece options (a text list, not buttons) are fixed and proven; the yard balloon chain is retuned to the measured pop arc (5.0 m across, 2.4 m up).
- Also this session, after the first handoff: the Halberdier's cuts remapped onto the authored strike clips with ranges at the blade and a far-band commit in `EnemyController` (the charge now actually fires from 5 m+); the glass HUD, fluid bars and Pyre fire built and seen once; the perfect-timing refunds built; balloons/water/burst built into the yard.

## Do first next session

**The in-game level editor v1 is BUILT (2026-09-05).** `docs/LEVEL-EDITOR.md` is its page; the generators
(5, 9, 7, 8 on Level_01) have been re-run through the shared `LevelPieceFactory` and Level_01 rebuilt identically
(125 root children, 441 renderers, 122 colliders, same bounds; `Torches` / `Pickups` groups restored). Test and
play results: see VERIFICATION-REPORT.

1. Open the editor on the project; if `mcpforunity://instances` says 0, save any script or run
   **Tools → MCP Bootstrap → Reconnect Bridge**.
2. **Fix the perfect dash-jump** (3 red feature checks): measure `IsGrounded` through a ground dash with the harness's own setup (`CanAct` must be true), then most likely let a ground-started dash count as grounded for a jump for its 0.16 s. Then re-run `FeatureTests` (expect 734 / 0).
3. **Fix the top-centre overlap**: the developer help text draws over the clock pill (HudBuilder).
4. **Play it.** Nobody has: the halberdier's 4.7 m charge lunge (~14 m/s over the cue window) may need
   retuning; the legs, the eye plop and the 6 mm rumble are feel calls; the 14.85 m/s water floor, the
   3.27 m balloon launch and the 27.5 m/s grapple burst are all numbers from arithmetic.
5. Then the rest of the pivot: rework `Level_01_Level.asset` (remove or repurpose the filler spawns, lay in
   balloons and water, teach `LevelArcAnalyzer` the balloon launch) — with the level editor now available
   for laying pieces out by hand and exporting them.

## Open questions for the user

- Editor v1 scope: place / move / delete / save / play the existing pieces — or more?
- Are the `Level_01` filler Grunt/Heavy spawns removed outright, or kept as traversal tools?
