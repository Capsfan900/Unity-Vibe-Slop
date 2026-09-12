# Handoff — keybind friend-build update

## Current state — 2026-09-12

The new `master` pass adds the complete KEYBINDS page and the opening-ramp random-death/projectile hotfix.
Both generated UI prefabs and `MainMenu.unity` are current. MCP remains the primary live-editor/build/test
transport; the Unity CLI pilot remains available for short state/preflight calls.

The intended friend package is `Builds/vibegame1-windows-playtest-2026-09-12-keybinds.zip`. Trust it only
when `Builds/Windows/build-info.txt` names the current `master` HEAD and D3D11. The local Level 01 song is
intentionally untracked but included in the friend build; therefore `build_inputs_dirty=YES` is expected and
the strict public publisher cannot be used. Preserve the song and record its SHA256 in
`friend-build-content.txt` inside the ZIP.

## Completion evidence

- **Keybind verification:** quick EditMode **935/935 passed**, zero failures/skips, 7.6 s
  (`TestResults/EditMode-20260912-005833.xml`). Runtime/editor offline builds have zero errors. Health Check
  has zero errors. Both HUD and Main Menu generators were rerun.
- **Reserved controls:** persisted override JSON is filtered to the 24 exposed bindings; Escape, Backquote,
  Enter, gamepad Start, weapon slot 4 and F1/F5-F10 cannot be captured by an ordinary action.
- **Opening ramp:** void death requires both the old vertical threshold and no route surface beneath the
  player. Projectiles now resolve live solid-route contact before equal/later player contact.
- **Song source:** Unity imports
  `FineArt & jazza's dance party - Eyes Wide Shut.mp3` as 3.3 MB / 3.2% of packed assets. Its source SHA256
  is `0490CA1D7A06D76C59B5C17505A21008746DA5A05AB51AEBB7CDF53B0AC65576`. The file remains local.
- **Windows startup resolved:** `BuildRunner` and the serialized Windows PlayerSettings both pin D3D11.
  `build-info.txt` reports `graphics_apis=Direct3D11`. The exact packaged executable, launched with no
  renderer override, selected Direct3D 11.0 and remained alive for the full 15-second native smoke. The
  former D3D12 path failed at swapchain presentation with DXGI `0x887a0001`.
- **Canonical vocabulary:** `docs/LEVEL-VOCABULARY.md` defines T0 Opening Descent, T1 Stone Causeway, T2
  Helix Tower, T3 Balloon Aqueduct and T4 Warden Descent; enemy aliases map to shipped prefab/spawner IDs.
  `Tools/dashboard/out/index.html` overlays those zones and inventories on the parsed LevelDefinition map.
- **Warden causeway review:** commit `d3b6245` was not merged. It modifies an old generated
  `Level_01_Level.asset` without changing current authoring and would overwrite later course/projectile
  work. It is safely pushed as archival tag `preserved-warden-causeway-d3b6245` for a future authored port.
- **GitHub Pages removed:** remote and local `gh-pages` were deleted; a remote branch query returned no
  result. `Publish-WebGL.ps1` is retained only as rollback evidence and must not be run without a new user
  decision, because it would recreate the branch.

The V18 Flurry Brawler remains a separate Sandbox-only Souls-enemy lineage. It does not share the parkour
projectile enemy structure. Earlier focused Sandbox `V18Smoke` was 21/21; its animation feel still needs a
human playtest.

## Exact resume order

1. If the keybind ZIP is not yet attached, create/update its GitHub Release and upload the prepared ZIP.
2. Have a friend launch `vibegame1.exe`, rebind one movement and one combat input, restart, verify persistence,
   then run the opening descent. If startup fails, collect the adjacent Player log and GPU/driver
   details. Do not add a D3D12 fallback until it is proven on the affected machine.
3. Human-play the V18 Sandbox pad and report tell/contact/weight issues before promoting it from test enemy.

## Preserved user-owned files

Do not stage, remove or overwrite `.claude/settings.local.json`, `Portraits/`, unrelated `RouteShots/`, or
`Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and its `.meta`.
