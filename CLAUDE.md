# vibegame1

Unity game project. Claude drives the Unity Editor through MCP.

## Environment

- **Unity 6000.5.10f1** (Unity 6.5) — `C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe`
- **Render pipeline: URP** (`com.unity.render-pipelines.universal`). Materials must use URP shaders
  (`Universal Render Pipeline/Lit`, not `Standard`) or they render magenta.
- **Input: the new Input System** (`com.unity.inputsystem`). Actions live in
  `Assets/InputSystem_Actions.inputactions`. Do not use legacy `Input.GetAxis`.
- Build modules installed: **Windows Standalone** and **WebGL** only. No Android/iOS unless added in Hub.

## MCP workflow

Tools come from `com.coplaydev.unity-mcp` (MCP for Unity), a UPM package that runs a bridge inside the
Editor.

- **The Unity Editor must be open on this project for any MCP tool to work.** If tools start erroring or
  timing out, check that first before debugging anything else.
- Prefer MCP tools over blind file writes for scenes, prefabs, and component wiring — they go through
  Unity's serialization. Hand-editing `.unity` / `.prefab` YAML or `.meta` GUIDs corrupts references.
- C# scripts may be written as files, but expect a **domain reload** afterward; the next tool call can
  time out while Unity recompiles. Re-issue it rather than assuming failure.
- After writing scripts, read the Unity console to confirm a clean compile before continuing.

## Layout

- `Assets/Scenes/` — scenes (`SampleScene.unity` is the template default)
- `Assets/Settings/` — URP pipeline assets and renderers (PC + Mobile variants)
- `Assets/Scripts/` — game code (create as needed)
- `Assets/TutorialInfo/` — template readme boilerplate, safe to delete

## Conventions

- Commit before large refactors. `Library/` is gitignored — never commit it.

## Game: vibegame1 (first-person parry-parkour prototype)

Neon White level flow + Sekiro/Lies of P parry combat, first-person, **melee only**. All code is in namespace
`VibeGame1`. Tuning lives in ScriptableObjects under `Assets/Data/` (weapons, enemies, attacks, player stats,
upgrade table, game feel) — tweak in the Inspector; re-running `VibeGame1/3. Create Data` resets weapon/enemy/attack
assets to the values coded in `Assets/Editor/DataFactory.cs`.

### Rebuild pipeline (Unity menu `VibeGame1/…`, or `execute_code` calling `VibeGame1.EditorTools.X.Y()`)
1. `ProjectSetup.Run()` — layers (Player=6, Enemy=7, Interactable=8), HDR grading, volume profile, fog/light.
2. `MaterialFactory.CreateAll()` — `Assets/Materials/M_*.mat` (URP/Lit, `_EMISSION` on).
3. `DataFactory.CreateAll()` — ScriptableObjects.
4. `PrefabFactory.BuildAll()` — Player, Managers, enemies, boss, weapon viewmodels, checkpoint, bloodstain.
5. `HudBuilder.Build()` — `Assets/Prefabs/HUD.prefab` (UGUI + TMP; EventSystem uses InputSystemUIInputModule).
6. `LevelGreyboxBuilder.Build()` — rebuilds the `Level` root in the open scene, bakes NavMesh, places Player/Managers/HUD.
   Never deletes a sibling root named `Level_Manual` — put hand-placed extras there.

### Key runtime scripts
- `Assets/Scripts/Player/PlayerCombat.cs` — every enemy hit resolves here (Perfect / Blocked / Hit) via `ParryController` + `ParryMath`.
- `Assets/Scripts/Enemies/EnemyController.cs` — FSM Idle/Chase/Windup/Strike/Recover/Staggered/Executed/Dead; `BossController` adds segments + phases.
- `Assets/Scripts/Core/TimeScaleController.cs` — the only thing that touches `Time.timeScale` (hitstop, ultimate, pause).
- `Assets/Scripts/Core/GameEvents.cs` — static event bus; HUD subscribes here.
- `Assets/Scripts/Core/InputReader.cs` — only script that touches the Input System (`InputSystem.actions`).

### Verification
- EditMode tests: `Assets/Editor/Tests/` (`run_tests` mode EditMode) — parry window edges, posture, upgrade cost.
- Scripted play-mode scenarios (no input needed): in play mode run
  `VibeGame1.DebugHarness.Run("parry" | "boss" | "death")` via `execute_code`, then read `VibeGame1.DebugHarness.Log`.
  Don't click the Game view while a scenario runs — mouse input feeds the real player.
- Editor console: `[Parry]` logs print elapsed ms vs window for every parry resolution (editor only).

### Art direction: dark fantasy
Void-black violet background + fog, cold moonlight, blood/ember/ghost-teal accents, flickering torches (`FlickerLight`),
film grain + heavy vignette. Palette lives in `Assets/Editor/MaterialFactory.cs` (material names are kept stable —
`M_NeonPink` = blood, `M_NeonCyan` = ghost teal, `M_NeonYellow` = ember) and `HudBuilder.cs` color constants.
Audio: real **CC0** clips live in `Assets/Resources/Audio/Sfx/<SfxName>/*` (random variant per play) and
`Assets/Resources/Audio/Music/{ambient,boss}.ogg` (crossfades on BossStarted / BossDefeated / respawn). Sources and
licenses are listed in `CREDITS.md`. To swap a sound, drop files into the matching folder — no code changes.
`ProceduralSfx.cs` is only a fallback for empty folders (the user found it harsh — prefer sourced clips).
Gotcha: an `AudioSource` added and `Play()`ed inside the same scene-load `Awake` never starts — start it in `Start()`.

### Dev / test keys (editor + development builds only, `Assets/Scripts/Debug/DebugKeys.cs`)
- **4** — equip "Oathbreaker (TEST)" dev blade (60 dmg, huge parry window, 1000 execute).
- **F5** — warp to the boss arena entrance with the dev blade equipped. **F6** — full heal + flasks + juice.
- **F7** — +1000 souls. **F8** — toggle god mode.
