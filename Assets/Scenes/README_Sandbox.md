# Sandbox scene

`Assets/Scenes/Sandbox.unity` — a flat, walled 60 × 60 arena for trying combat, weapons, items and
enemies without running the campaign course in `Level_01.unity`.

## Rebuilding

Menu: **`VibeGame1/7. Build Sandbox Scene`**
Open without rebuilding: **`VibeGame1/Open Sandbox Scene`**

The builder is one-shot and reproducible, exactly like `VibeGame1/6. Build Level`:

- It prompts to save unsaved changes, then opens (or creates) `Sandbox.unity`.
- It destroys and regenerates the `Sandbox`, `Player`, `Managers` and `HUD` roots.
- A sibling root named **`Sandbox_Manual`** is **never touched** — put hand-placed experiments there
  so a rebuild does not wipe them. (Same contract `Level_Manual` has in the campaign scene.)
- It refuses to run in play mode.
- The scene is added to Build Settings without disturbing the existing `Level_01` entry.

Run it **after** the asset generators, since it consumes their output:
`1. Project Setup` → `2. Create Materials` → `3. Create Data` → `4. Build Prefabs` → `5. Build HUD` → `7. Build Sandbox Scene`.
Missing materials, prefabs or item assets only produce warnings — the scene still builds, just
without those pieces.

## Layout

Floor top sits at **y = 0**. Walls are 3 m high, so you cannot fall out; a `KillZone` sits at
y = −25 as a backstop.

| Area | Where | What it is |
|---|---|---|
| **Player spawn** | centre `(0, 1.2, 0)`, facing +Z | `StartSpawn`, also wired to `LevelManager.startSpawn` |
| **Platforming staircase** | north-east, `Jump_1`…`Jump_5` | Five steps, each a **1.5 m rise over a ~1.7 m gap** — the project reachability limit is rise ≤ 1.5 m with gap ≤ 4.5 m. Tops at 1.5 / 3.0 / 4.5 / 6.0 / 7.5 |
| **Dash gap** | north-west, `Dash_A` → `Dash_B` | Two level pads **7 m apart edge-to-edge**: too far to jump, comfortable with a dash. Yellow trim |
| **Enemy pads** | south wall, z = −18 | `Pad_Grunt` (x −14), `Pad_Heavy` (x −5), `Pad_Boss` (x 8), plus two spare pads (x 18, 24) for future enemies. Live `EnemySpawner`s on the first three |
| **Item pedestals** | west side, x = −22 | One stone pedestal per `ItemData` in `Assets/Data/Items`, 5 m apart, pickup floating 1.2 m above the pedestal top |
| **Weapon rack** | east side, x = 22 | Four marker pads with posts colour-keyed to Sword / Hammer / Dagger / DevBlade. Purely visual — swap weapons with keys **1–4** |
| **Torches** | 8 around the perimeter | Same `FlickerLight` setup as the campaign level |

Lighting, fog and the post-processing volume mirror `ProjectSetup.SetupSceneEnvironment`, so the
sandbox reads like the real game. If that palette changes, either update `SandboxBuilder.EnsureEnvironment`
or run `VibeGame1/1. Project Setup` with this scene open.

## The boss

`Spawn_Boss` spawns the boss on load, but `BossController` starts **aggro-locked** and the sandbox has
no `BossArenaTrigger`, so it stands inert until you wake it. Use `SandboxController.ActivateBoss()`
(component context menu) to start the fight.

## SandboxController

On the `Sandbox` root. Editor / development builds only. It deliberately does **not** duplicate the
existing debug tools — use those first:

- **F1** — `TestMenu` overlay: warp, give item, equip weapon, full restore, god mode, +souls,
  break posture, kill/stagger/reset enemies, plus a live state readout.
- **F5–F8** — `DebugKeys`: warp to boss, restore, +1000 souls, toggle god mode.

`SandboxController` adds what only makes sense in a flat arena. Every entry point is also a component
context-menu item (right-click the component header in the Inspector):

| Method | What it does |
|---|---|
| `SpawnEnemyInFront(int index)` | Drops `enemyPrefabs[index]` on the NavMesh in front of you, facing you. Index 0 = Grunt, 1 = Heavy, 2 = Boss |
| `SpawnDummy()` | An **inert practice dummy**: a Grunt with `aggroLocked = true` and ~1M HP, for drilling swing timing, hit reactions and posture damage against a target that never fights back |
| `ActivateBoss()` | Wakes the boss (no arena trigger in this scene) |
| `ClearAllEnemies()` | Instant **despawn** of everything, spawner-owned included — no death animation, no souls. `TestMenu`'s "Kill Nearby" is the one that kills properly |
| `ToggleInfiniteFlask()` | Flask silently refills whenever it is not full |
| `ToggleInfiniteItems()` | A used item is handed straight back |
| `ResetSandbox()` | Removes hand-spawned enemies, restores the pads, full-heals and returns you to spawn |

Inspector fields worth knowing: `spawnDistance` (how far in front enemies appear),
`navSampleRadius` (how far the spawn point may be nudged to find NavMesh), `dummyPrefabIndex`
and `dummyHealth`.
