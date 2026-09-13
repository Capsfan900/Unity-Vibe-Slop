# The in-game level editor

Build a level from inside the game, save it as data, play it at once, and hand it to the campaign
pipeline if it earns a place. Version 1 (2026-09-05): the pieces the campaign is made of, first-person
placement on a grid, save / load / play, a CUSTOM list in the main menu. Same data, same piece factory as
`8. Build Level From Definition`, so a level made here is regenerable data (hard rule 4) — never
hand-placed scene state.

**Finalized 2026-09-05 and shelved** as a test-and-tweak tool (levels are authored in the Unity editor,
[LEVEL-AUTHORING-TUTORIAL.md](LEVEL-AUTHORING-TUTORIAL.md)). The last pass fixed what the user felt:
a tap now builds where the preview was on the PRESS frame (not where the aim had drifted to by release),
a carried piece no longer catches its own aim ray, the aim's surface is debounced over three frames so
the preview stops hopping at slab edges, and the size is ONE number — a step aimed at a placed slab
steps from that slab's own size and the panel shows it. Added: Esc cancels a grab (the piece goes back),
Ctrl+Z / Ctrl+Y undo and redo (a 50-deep snapshot stack), Ctrl+D duplicates the aimed piece, the arrow
keys nudge the aimed or carried piece one grid cell (Ctrl: up / down), `P` / PLACE HERE builds the
selected piece under the camera, a numeric readout (size, grid, rotation, undo depth, the aimed piece
and its size), and a 7-cell grid decal drawn on whatever surface the preview sits on.

Related: [AUTHORING.md](AUTHORING.md) §1 (LevelDefinition) · [DATAFLOW.md](DATAFLOW.md) → *Level editor* ·
[BACKLOG.md](BACKLOG.md) §0 · [TOOLING.md](TOOLING.md)

For an unambiguous editing request, use: `<zone ID/name> / <canonical object ID or alias> / <requested change>`.

---

## Opening and closing

| Do | How |
|---|---|
| Open | In any build: press **`**, enter the private developer passphrase, then use **F10** or **F1 → LEVEL EDITOR [F10]** |
| Close (back to ordinary play, where you were) | **F10** again, or **EXIT** on the panel |
| Play the level you are editing | **PLAY** on the panel: rebuilds it fresh, bakes a NavMesh, drops you at the player start, spawns the enemies |
| Return from playing to editing | **F10** (the prompt says so) or **EDIT** |

While editing the game state is `Editing`: gameplay scripts idle, the player is a fly camera on the same
look, and the HUD's glass panel on the left lists the pieces.

## Choosing what to place

The panel's top block is a 3 × 3 grid of piece buttons — PLATFORM, WALLFACE, BALLOON, WATER, SPAWN, PICKUP,
CHECKPOINT, TORCH, PLAYERSTART. Click one (the cursor must be free: **Tab**) and it lights; the line under the
grid names the selection with its size or variant. **Shift+wheel** / `[` `]` step through the same list and
**Ctrl+wheel** / `V` change the variant (which enemy, which item). All three routes write the same selection,
so what the button says is what the next click builds. Until 2026-09-05 this block was plain text, which is
why clicking it did nothing.

## Flying and aiming

The editor opens with the **cursor free** and the camera parked above the ground looking at the level's
start, so the panel works on the first click. **Tab** locks the cursor for look-fly and frees it again;
with it free the keys still fly at half speed, so the panel never strands you. WASD moves, **Space** up,
**Left Ctrl** down, **Left Shift** fast — the fly accelerates and coasts on a 0.12 s ease and Shift blends
in rather than switching (MOVEMENT-PRINCIPLES rule 1), the same at any frame rate (rule 8).

The aim is the crosshair while the cursor is locked and the **pointer** while it is free. It lands on
level geometry or on the editor's **ground**: a 200 m dark plane with a cyan grid every 4 m that exists
only while editing (the scene itself is hidden), so there is always something to build on. The cyan
preview slides to its cell on a 0.06 s ease and holds its cell with a quarter-cell of hysteresis, so it
never flickers on a boundary: a **1 m grid** for platforms, wall faces and water, **0.5 m** for
everything else; hold **Left Alt** for free placement. The piece under the aim is tinted toward the
accent: that is what a delete or a grab will take.

## Pieces and keys

| Key | Action |
|---|---|
| `[` / `]` | previous / next piece kind: Platform, Wall face, Balloon, Water, Enemy spawn, Pickup, Checkpoint, Torch, Player start |
| `V` | the variant: which enemy a spawn drops (Grunt, Heavy, the Legendaries…), which item a pickup holds |
| `=` / `−` | size ladder 2 / 4 / 6 / 8 m, then +2 m a step (aimed at a placed platform or water sheet, resizes it) |
| `T` | rotate 90° (a box swaps its X and Z extents; water turns its flow); **Shift+T** rotates the other way — a 270° turn is one press either direction, not three |
| `I` / middle mouse | **pick** (eyedropper): aim at a piece and the selection becomes it — kind, variant (which enemy, which item) and size, so the next click builds another one just like it |
| Left mouse | tap: place where the preview was when you pressed (on a piece too, to build on it). **Hold on a placed piece**: grab it and carry it with the aim; release to drop on the grid |
| Right mouse / `X` / `Delete` | delete the aimed piece. Right mouse is unconventional for a delete (every other editor uses RMB for look/orbit); it stays for now, Ctrl+Z covers a slip |
| Mouse wheel | size ladder 2 / 4 / 6 / 8 m then +2 m, works over the panel too (a placed platform or sheet under the aim steps from ITS size); **Shift + wheel** cycles the piece kind; **Ctrl + wheel** the variant |
| hold `G` | alias for the grab (the old way) |
| `Esc` / `Backspace` | cancel: a carried piece goes back where it was; a pending press is dropped. The pause menu does not open on that press |
| `Ctrl+Z` / `Ctrl+Y`, UNDO | undo / redo, 50 edits deep (place, delete, resize, move, nudge, duplicate). NEW / LOAD clear the history |
| `Ctrl+D` | duplicate the aimed piece one grid cell further along your look |
| Arrow keys | nudge the aimed or carried piece one grid cell along the world axis nearest your look; **Ctrl + up / down** moves it up / down. The arrows do not fly while editing |
| `P`, PLACE HERE | build the selected piece on the surface straight below the camera (2 m under it if there is none) |

Water flows the way you were facing when you placed it. A balloon launches straight up (11 m/s as the
HUD ships it) and re-arms a dash that passes through it; chains 3 m apart read as arcs (MOVEMENT-PRINCIPLES
rule 7). A player start is one per level; placing another moves it.

## Files: save, load, new, export

- **SAVE** writes the level as JSON to `<persistentDataPath>/levels/<name>.json`
  (Windows: `%USERPROFILE%\AppData\LocalLow\vibegame1\vibegame1\levels\`). The name comes from the panel's
  text field (Tab to type); unsafe characters are replaced.
- **‹ ›** step through the saved files, **LOAD** rebuilds the chosen one in place.
- **NEW** starts a fresh 16 m slab with a player start.
- **EXPORT ASSET** (Unity editor only) writes a real `LevelDefinition` to
  `Assets/Data/Levels/Custom/<name>_Level.asset` with `sceneName = Custom_<name>` and `orderIndex 100`.
  To bake it into a campaign scene: create a scene of that name, open it, select the asset, run
  `VibeGame1/8. Build Level From Definition`; add it to `LevelRegistry` and rebuild the main menu (`9`) for
  a level-select row.
- When F10 is attached to a protected Level Studio draft, **SAVE** autosaves that draft and PLAY → EDIT
  autosaves any changes. NEW, LOAD, legacy file selection and EXPORT are disabled so no second JSON lineage is
  created. Stopping play mode flushes the same draft once more only when it is dirty.

The JSON is `LevelDocument`. Its editable lists cover platforms, spawns, pickups, checkpoints, torches,
balloons, water sheets and player start; a complete embedded definition snapshot preserves every unedited campaign
field and stable ID during a Level Studio round trip. Loading an older file with a missing list is safe.

## The main menu

**LEVEL SELECT** lists every saved JSON under a **CUSTOM** divider below the sandbox row. Clicking one
loads the sandbox scene with the level queued (`LevelEditor.PendingLoadPath`); the editor builds it and
puts you straight into PLAY. Long lists run off the bottom of the panel in v1.

In the Unity editor, unlocking developer access also reveals **LEVEL EDITOR** when Level Studio has a resumable
active draft. It queues that draft ID, loads its target scene, and enters PLAY through the editor-only bridge.

## How it is built

- `Assets/Scripts/Level/LevelPieceFactory.cs` is the ONE piece factory. `8. Build Level From Definition`
  and the runtime editor both call `BuildDocument`; the editor-time context resolves materials and prefabs
  through the AssetDatabase, the runtime context through the library `HudBuilder` serialises onto the
  `LevelEditor` component (8 materials, 11 prefabs, the items, 8 spawn keys). Torches and pickups sit under
  `Torches` / `Pickups` groups exactly as the campaign builder always made them, because
  `LevelDefinitionExporter` reads those groups by name.
- Every built object carries a `LevelPiece` tag (kind + index into the document); the crosshair resolves
  what it is aiming at through that tag, never by name.
- The editor lives on the HUD prefab (`HudBuilder.BuildLevelEditor`), so it exists in every gameplay
  scene without a prefab of its own; every number on it is written there (rule 9).
- Input goes through `InputReader` only (rule 2): fourteen optional actions on the Player map
  (`LevelEditor`, `EditorPlace` … `EditorDown`) in `Assets/InputSystem_Actions.inputactions`.
- **F10 is gated in every build (2026-09-11).** A trusted tester must open the command console with
  Backquote and enter the private developer passphrase; the grant lasts for that process only and resets
  on the next run. The plain phrase is not stored, but this remains a client-side deterrent rather than
  authentication. The gate lives at `InputReader.LevelEditorPressed`, the one place the F10 action exists.
  **This does not disable the editor.** A custom level still loads and plays in every build: the main
  menu's CUSTOM rows set `PendingLoadPath`, and `LoadPendingAndPlay` calls `Enter()`/`Play()` directly.
  `TestMenu`'s LEVEL EDITOR row is behind the same symbols and EXPORT behind `#if UNITY_EDITOR`.
- PLAY bakes a runtime `NavMeshSurface`; if the bake fails, spawned enemies stand still rather than throw.

## What v1 leaves out

Terrain, lighting, multi-select, arena gates and pedestals (campaign-only pieces), a level's par time
and registry entry (edit the exported asset), and any cloud or sharing of files. A box rotates only by
swapping its X and Z extents. The custom list in the main menu does not scroll. Left out of the final
pass (shelved): the preview is not visible when it sits inside the slab it would be built on (only the
grid decal marks the spot), the grid decal is a flat square even on a sloped face, redo has no button,
and a nudge on a Water sheet or a wall face uses the same anchor rule as a platform.

## Tests

`LevelEditorTests` (EditMode, 17): JSON round trip incl. balloons and water, null-list safety, the
definition mirror, grid snap, snap hysteresis, the frame-rate-independent ease, the HUD's control numbers,
the size ladder, safe file names, runtime-vs-editor factory parity on a small document, the HUD carrying
the editor, the main menu's template row, one kind shared by panel and wheel, the size stepping from the
piece, the bounded undo stack, the aim-surface debounce, the arrow nudge mapping. `FeatureTests →
LevelEditor` (10): on the HUD, enter, place, tag, trim, save, new, load, JSON on disk, exit.
Scripted probes in play (2026-09-05, Sandbox): a 0.4 s press on the ground slab then a 25° turn carried
it to exactly the snapped release aim (5, 0, 2), no drift onto itself; two `+` steps on a 6 m slab with 8 m
pending gave 8 then 10 m and the readout showed 10 m; three undos took it 10 → 8 → 6 m and put the slab
back at the origin.
