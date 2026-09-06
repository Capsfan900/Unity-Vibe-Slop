# Handoff: make the in-game level editor's controls intuitive

Written 2026-09-06 for a Sonnet 5 agent. Scope: **the in-game level editor's control scheme only**
(`Assets/Scripts/Level/LevelEditor.cs`, `Assets/Scripts/Core/InputReader.cs`,
`Assets/InputSystem_Actions.inputactions`, `docs/LEVEL-EDITOR.md`). Do not touch the motor, combat,
HUD glass, or any level content. The editor is shelved as a tweak tool (memory: level-editor-shelved),
so keep this to bindings and small interaction fixes, not a redesign.

## Read first
- `docs/LEVEL-EDITOR.md` (the current key table, "Pieces and keys")
- `Assets/Scripts/Level/LevelEditor.cs` lines ~500-630 (the per-frame input handling in `Update`)
- `Assets/Scripts/Core/InputReader.cs` lines 18-90 and 150-180 (how editor actions are found and exposed;
  every action is optional via `map.FindAction(name, false)`)
- CLAUDE.md hard rule 2: only `InputReader` touches the Input System. Rule 9: no code default is a shipped value.

## Research: what makes 3D editor controls intuitive (Blender, Unity, Unreal, Hammer, Halo Forge,
## Fortnite Creative, Minecraft)
1. **Reversible operations are symmetric.** Rotate has a back-step (Blender R then negative, Forge bumpers,
   Minecraft/Fortnite Shift+rotate). Ours: `T` rotates one way only. A 270 turn is three presses.
2. **"Pick" / eyedropper.** Middle-click (Minecraft pick-block), Alt+click (Unreal, Photoshop), Fortnite
   copy: aim at a thing and your selection becomes that thing (kind, variant, size). We have nothing.
3. **Delete lives on Delete.** Every editor: `Delete` / `X`. Ours: `X` and **right mouse**. Right mouse in
   every 3D tool is look/orbit or context menu; a delete on RMB is the classic accidental-destruction bind.
   Undo exists (Ctrl+Z), which softens it, but a Delete-key alias costs nothing.
4. **Modifiers are consistent.** Shift = fast / alternate direction, Ctrl = command chord, Alt = temporary
   override (our Alt = free placement is the Unity/Blender convention: hold to suspend snapping). Keep.
5. **The wheel does the thing you are looking at.** Ours: wheel = size, Shift+wheel = kind, Ctrl+wheel =
   variant. That is coherent; do not change it. (Unity/Unreal use RMB+wheel for fly speed; we have no free
   modifier for that and the fly already has Shift fast, so skip.)
6. **Feedback closes the loop.** Every action should change the readout or preview the same frame. Ours
   mostly does (readout, tint, grid decal).

## Do, in order
1. **Shift+T rotates the other way.** In `LevelEditor.Update`: when `input.EditorRotatePressed`, pass
   `input.EditorFastHeld ? -1 : 1` to `LevelEditorMath.RotateStep`; `axisSwap` toggles either way.
2. **Pick (eyedropper).** New action `EditorPick` bound to `<Keyboard>/i` (middle mouse is `LockOn`, and
   `q` is `Ultimate`; both are Player-map gameplay actions that idle while editing, so **middle mouse may
   be shared** — add `<Mouse>/middleButton` as a second binding if you confirm nothing in the editor reads
   LockOn). `InputReader.EditorPickPressed`. On press with a piece under the aim and no grab: set `kind` to
   `under.kind`; for Spawn set `spawnIndex` from `doc.spawns[under.index].prefabKey` via `spawnKeys`; for
   Pickup set `itemIndex` from `doc.pickups[under.index].itemKey` via `itemKeys`; for Platform / Water set
   `size` from the def's `size.x` (and `axisSwap` when `size.z != size.x`); then `hasSnapped = false` and
   `RefreshPanel(under)`. Add a PICK button on the panel only if trivial; the key is enough.
3. **Delete key alias.** Add `<Keyboard>/delete` to `EditorDelete` in the `.inputactions` JSON (copy an
   existing binding object, new GUID, same `groups`). Keep RMB for now; note in the doc that it is
   unconventional and that Ctrl+Z covers it.
4. **Doc.** Update the key table in `docs/LEVEL-EDITOR.md` and the `Level editor` map in `docs/DATAFLOW.md`
   (rule: a changed system updates its map in the same change).
5. **Tests.** `Assets/Editor/Tests/LevelEditorTests.cs` (or the nearest fixture) has pure-math tests for
   `LevelEditorMath.RotateStep`; add one for the negative step. Add an EditMode test that parses the
   `.inputactions` JSON and asserts `EditorPick` and the `delete` binding exist.

## How to verify
- Offline compile: `dotnet build Assembly-CSharp.csproj` then `Assembly-CSharp-Editor.csproj` from the repo
  root (Unity regenerates the csproj on refresh, so a brand-new file may show as missing until then).
- In the user's OPEN editor (never headless; `.claude/skills/unity-editor`): `refresh_unity`, read the
  console for `error CS`, then `manage_scene save` and `run_tests {"mode":"EditMode"}`. Last known:
  561/561. The feature suite has 10 level-editor checks; run it in play mode if you changed anything
  beyond bindings.
- The `.inputactions` file is JSON; editing it by hand is fine, but keep GUIDs unique.

## Out of scope (seen, not done)
- Moving delete off RMB entirely.
- Fly-speed on the wheel.
- Any panel redesign.
