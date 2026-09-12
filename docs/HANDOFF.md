# Handoff — Windows friend build ready

## Current state — 2026-09-12

Work is unified on `master` and pushed through `302fc6e`. The retained Unity CLI pilot and all gameplay
work from that branch are present; MCP is the primary live-editor/build/test transport. The tracked tree is
clean. Preserve the user-owned untracked files listed below.

The Windows friend build is ready at
`Builds/vibegame1-windows-playtest-2026-09-12-friends.zip` (47,874,159 bytes, SHA256
`A242F599D450A54F2B179458D5643058766151A7358A83212B9E8EE09D1E2BDD`). Its source tag
`playtest-2026-09-12-friends` is pushed and resolves to `302fc6e`. The executable includes Main Menu,
Level 1 and Sandbox. Release diagnostics remain gated behind the in-game console and password.

The ZIP is prepared locally but is not a GitHub Release asset: GitHub CLI is not installed and no API token
is available in this environment. Upload that exact ZIP at the repository's Releases page using the already
pushed tag; do not rebuild it merely to obtain `build_inputs_dirty=no`, because the intentional local song
is the only dirty game input.

## Completion evidence

- **Song included:** Unity imported and packed
  `FineArt & jazza's dance party - Eyes Wide Shut.mp3` as 3.3 MB / 3.2% of packed assets. Its source SHA256
  is `0490CA1D7A06D76C59B5C17505A21008746DA5A05AB51AEBB7CDF53B0AC65576`. The file remains local and is
  named/hashed in `friend-build-content.txt` inside the ZIP.
- **Windows startup resolved:** `BuildRunner` and the serialized Windows PlayerSettings both pin D3D11.
  `build-info.txt` reports `graphics_apis=Direct3D11`. The exact packaged executable, launched with no
  renderer override, selected Direct3D 11.0 and remained alive for the full 15-second native smoke. The
  former D3D12 path failed at swapchain presentation with DXGI `0x887a0001`.
- **Tests:** full EditMode **963/963 passed**, zero failures/skips, 51.98 s. Runtime and editor offline
  builds both compile with zero errors; the editor assembly retains 18 existing warnings.
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

1. Create the GitHub Release for pushed tag `playtest-2026-09-12-friends` and upload the prepared ZIP.
2. Have a friend launch `vibegame1.exe`; if startup fails, collect the adjacent Player log and GPU/driver
   details. Do not add a D3D12 fallback until it is proven on the affected machine.
3. Human-play the V18 Sandbox pad and report tell/contact/weight issues before promoting it from test enemy.

## Preserved user-owned files

Do not stage, remove or overwrite `.claude/settings.local.json`, `Portraits/`, unrelated `RouteShots/`, or
`Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and its `.meta`.
