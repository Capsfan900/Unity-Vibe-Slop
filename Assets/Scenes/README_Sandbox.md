# Sandbox scene

`Assets/Scenes/Sandbox.unity` — a flat, walled 60 × 60 arena for trying combat, weapons, items and
enemies without running the campaign course in `Level_01.unity`, plus a **120 × 60 movement yard**
through a doorway in its east wall for tuning wall running, jumping, sliding and landings with room to run.

## Rebuilding

Menu: **`VibeGame1/7. Build Sandbox Scene`**
Open without rebuilding: **`VibeGame1/Open Sandbox Scene`**

It is also reachable **in game**: the main menu's `LEVEL SELECT` panel carries a `SANDBOX` row, below a
divider and marked `DEV`, so it reads as a practice space rather than a campaign level. It is not in
`LevelRegistry` and never will be — `MainMenuController.sandboxSceneName` names the scene directly.

The builder is one-shot and reproducible, exactly like `VibeGame1/6. Build Level`:

- It prompts to save unsaved changes, then opens (or creates) `Sandbox.unity`.
- It destroys and regenerates the `Sandbox`, `Player`, `Managers` and `HUD` roots.
- It also destroys a stray **`Level`** root. `6. Build Level` builds into whatever scene is *active*,
  so running it with this scene open drops the campaign course into the arena and saves it there.
  One rebuild undoes that. (`Level_Manual` is matched by exact name only and is never touched.)
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
y = −25 as a backstop (x −40…165, z ±40 — it covers the yard too). The east wall has a 6 m doorway
at z −3…3 (`Wall_E_N` / `Wall_E_S`) leading into the yard.

Every spawner is live on load, but nothing charges you at spawn: the nearest pad occupant is ~22 m from
the origin and the widest aggro range in the arena is 18 m, so you walk to the fight you pick.

| Area | Where | What it is |
|---|---|---|
| **Player spawn** | centre `(0, 1.2, 0)`, facing +Z | `StartSpawn`, also wired to `LevelManager.startSpawn` |
| **Platforming staircase** | north-east, `Jump_1`…`Jump_5` | Five steps, each a **1.5 m rise over a ~1.7 m gap** — the project reachability limit is rise ≤ 1.5 m with gap ≤ 4.5 m. Tops at 1.5 / 3.0 / 4.5 / 6.0 / 7.5 |
| **Dash gap** | north-west, `Dash_A` → `Dash_B` | Two level pads **7 m apart edge-to-edge**: too far to jump, comfortable with a dash. Yellow trim |
| **Enemy pads** | south wall, z = −18 (+ a second row at z = −26) | Ten pads, each with a live `EnemySpawner`: `Pad_Legendary_Revenant` (x −28), `Pad_Legendary_Marionette` (x −22), `Pad_Grunt` (x −14), `Pad_Heavy` (x −5), `Pad_Legendary_Halberdier` (x 0.75), `Pad_Boss` (x 8), then `Pad_Legendary_Ninja` (x 16), `Pad_Legendary_Knight` (x 21), `Pad_Legendary_Spellsword` (x 26). The first two prototypes are at the **west** end rather than on the eastern run: the Spellsword pad's edge is already 1.5 m off the x = 30 wall, and duellists squeezed together there would sit inside each other's aggro. The Revenant sits 6 m west of the Marionette for the same reason — its own aggro is 20 m, and two duellists that pull each other make a sandbox you cannot use to look at either of them. The Halberdier takes the one gap left in the row, between the Heavy pad and the Warden's, one metre clear of each; every pad enemy sleeps behind its wake switch, so the arena is still quiet on load |
| **Wand altar** | `(0, 0, 2.5)`, 2.5 m in front of the spawn | `WandPedestal_Start` — stone plinth, cyan crystal, 3 m trigger that reaches the spawn point. Look at it and press **F** to open the wand-select menu. Same contract as the campaign level: the riposte loadout is a pre-run commitment |
| **Item pedestals** | west side, x = −22 | One stone pedestal per `ItemData` in `Assets/Data/Items`, 5 m apart, pickup floating 1.2 m above the pedestal top |
| **Weapon rack** | east side, x = 22 | Four marker pads with posts colour-keyed to Sword / Hammer / Dagger / DevBlade. Purely visual — swap weapons with keys **1–4** |
| **Torches** | 8 around the perimeter | Same `FlickerLight` setup as the campaign level |

### Movement yard (`MovementYard` group, east of the arena)

Walk straight east (+X) from the spawn through the doorway, or `SandboxController.WarpToMovementYard()`. The F1 test menu has the same warp as **MOVEMENT YARD**.
Every box here is a literal in `SandboxBuilder.YardLayout()`, and `MovementYardTests` pins the numbers
below without opening the scene. X runs east, Z north, all tops relative to the floor at y = 0.

| Area | Where | What it is |
|---|---|---|
| **Doorway** | `Yard_Doorway` x 30…32, z −3…3 | 2 m floor bridging the gap in the arena's east wall. Solid 1 m fills (`Yard_Kerb_W_N/S`) either side of it close the strip between the wall and the yard floor |
| **Yard floor** | `Yard_Floor` x 32…152, z −30…30 | 120 × 60 m, `M_Platform` with pink trim, 1 m `M_Ground` kerbs on the north, south and east edges so nobody walks off the world |
| **Yard spawn** | `YardSpawn` `(36, 1.2, 0)`, facing +X | Target of `WarpToMovementYard()`; looks straight down 94 m of open floor (nothing solid stands in z −3…3 before the tower at x 126) |
| **Distance stripes** | `Yard_Stripe_40` … `Yard_Stripe_150`, every 10 m of x | Full-width 0.15 m bars, no collider. Cyan, with **yellow at x 50 / 100 / 150**. Read a jump or exit distance off them by eye |
| **Runway** | `Yard_Runway` x 40…70, z −13…−3, top **4 m** | 30 × 10 m plateau, yellow trim. Climbed at its west end by `Yard_Runway_Step1` (x 33…35, top 1.5) and `Step2` (x 36.5…38.5, top 3). **30 m of nothing east of its edge** — sprint off a ledge, slide-jump off a ledge, land on flat floor |
| **Long walls** | `Yard_LongWall` z 17.5…18.5, `Yard_LongWall_B` z 24…25, both x 60…100 | Two **40 × 8 × 1 m** stone walls, yellow trim, a **5.5 m corridor** between them. The south face of `Yard_LongWall` exits into open floor (nothing within 15 m south or past its east end); the corridor is for wall-to-wall chains |
| **Gap ladder** | `Yard_GapLadder_1…6` along z −18 (z −21…−15), from x 40 | Six **6 × 6 × 2 m** pads, cyan trim, with gaps of **4 / 6 / 8 / 10 / 12 m**: pads at x 40…46, 50…56, 62…68, 76…82, 92…98, 110…116. `Yard_GapLadder_Step` (x 37…40, top 1 m) before the first so the climb starts as a hop |
| **Drop tower** | `Yard_Tower_1…4` x 126…134, tiers ascending north from z −27 to z 5 | Four **8 × 8 m** tiers with tops at **3 / 6 / 9 / 12 m** (z −27…−19, −19…−11, −11…−3, −3…5). Each is reached by a 2 × 2 step (`Yard_Tower_Step1…4`, x 127…129) 1.5 m below its top against its south face — every hop in the chain is a 1.5 m rise with zero gap. Every tier's **east face (x 134) drops onto 18 m of flat floor** to test landings from each height |
| **Yard torches** | `Yard_Torch_1…8` along the kerbs | Eight `FlickerLight` torches, ≥ 20 m apart. Two cool key lamps (`Yard_Key_W` / `Yard_Key_E` under `Sandbox_Lights`) ground the floor; the fog (45 m start) still swallows the far end |
| **Water lane** | `Yard_Water`, x 40..100, z 8..14 | A 60 × 6 m sheet flowing EAST at 6 m/s. Run in from the west and feel the lift to the 14.85 m/s skating floor; slide on it and it never decays; jump off the east end with the speed. `MovementYardTests.TheWaterLaneLiesOnTheFloorInsideTheYard_OffTheDoorwayLine` holds it to the floor and off the doorway line |
| **Balloon chain** | `Yard_Balloon_1..5`, from (104, 3.0, −10), each 5.5 m further, 1.2 m higher, zig-zagging 1.5 m in z | Five 1.1 m orbs you DASH between (2026-09-05: the old 3 m column was too small and only rode you upward). A pop is 11 m/s — a 2.0 m rise — followed by a 0.45 s FLOAT (gravity ×0.55, steer ×1.6) so you can aim the re-armed dash at the next orb. `MovementYardTests.EveryBalloonIsWithinAPopAndADashOfThePrevious` |

Lighting, fog and the post-processing volume mirror `ProjectSetup.SetupSceneEnvironment`, so the
sandbox reads like the real game. If that palette changes, either update `SandboxBuilder.EnsureEnvironment`
or run `VibeGame1/1. Project Setup` with this scene open.

## The legendary mini-bosses

`Legendary_Ninja`, `Legendary_Knight`, `Legendary_Spellsword` and `Legendary_Marionette` (built by
`Assets/Editor/MiniBossFactory.cs`) each get a pad, a live spawner and a `SpawnEnemyInFront` index.

**`Legendary_Revenant` — THE EMBER REVENANT — is a prototype and lives here only**, alongside the
Marionette. It is the test body for the `ai_skelly_tool` pipeline and the project's first BURNING
enemy: `EmberAura` gives it a constant emission floor, embers rising off the body and one weak light,
all riding `Posture.Ratio` so it stokes up as its posture breaks. Where the Marionette is a cadence you
HOLD, the Revenant is a READ — slow, committed swings with real openings, and the longest punish window
in the game on its overhead. Wake it with `Wake_Legendary_Revenant` at x − 28.
See `docs/ARCHITECTURE.md` → *The Ember Revenant*.

**`Legendary_Halberdier` — THE ARGENT HALBERDIER — is a prototype and lives here only**, at x 0.75
between the Heavy pad and the Warden's. The third `ai_skelly_tool` body and the first whose attacks are
GENERATED, per-character clips: nine attacks, nine animations, each attack naming its own clip, and the
travelling ones (thrust, slam, charge, kick, leap) lunging exactly as far as their clip's root motion.
Where the Marionette is a cadence and the Revenant a read, this is REACH — it holds at 4 m, the furthest
of the roster, with an unblockable shoulder charge for a player who backs out of that band and an
unblockable kick for one who turtles inside it. Wake it with `Wake_Legendary_Halberdier`.
See `docs/ARCHITECTURE.md` → *The Argent Halberdier*.

**`Legendary_Marionette` — THE PALE MARIONETTE — is a prototype and lives here only.** It is in no
`LevelDefinition` and no `LevelRegistry`, and this pad plus index **6** is the only way to meet it. It
is also the project's first ANIMATED enemy: a rigged forge model driven by an `Animator` through
`PuppetVisuals`, whose whirl peaks around 4 revolutions a second while its damaging passes arrive on a
fixed 0.76 s beat. Six clean deflects break it; the break is a 4.0 s stagger straight into a deathblow.
See `docs/ARCHITECTURE.md` → *The Pale Marionette*.
Unlike the Warden they are plain `EnemyController`s, not `BossController`s, so:

- their spawners have `isBoss = false` and they respawn/reset exactly like a grunt,
- they need no `ActivateBoss()` — they wake on their own aggro range (18 m or less),
- they carry a world-space posture bar, not the HUD boss bar, which belongs to the Warden alone.

## The boss

`Spawn_Boss` spawns the boss on load, but `BossController` starts **aggro-locked** and the sandbox has
no `BossArenaTrigger`, so it stands inert until you wake it. Use `SandboxController.ActivateBoss()`
(component context menu) to start the fight.

## Pad enemies respawn

A pad enemy comes back **4 seconds after it dies**, asleep on its pad with its wake switch re-armed —
practising a fight should not mean walking back to a menu between attempts. Controlled by
`autoRespawnPadEnemies` and `respawnDelay` on `SandboxController` (both written by `SandboxBuilder`, so
change them there to make a change stick).

Two details worth knowing:

- **The replacement is asleep.** A freshly spawned prefab ships `aggroLocked = false`, and the pad's wake
  switch tracks woken enemies by *instance*, so a respawn is a stranger to it. Without an explicit
  re-arm the new enemy would walk off its pad at you the moment it appeared — the exact state the wake
  switches exist to prevent.
- **This is sandbox-only.** Campaign respawn is still tied to the *player* dying, which is what a
  checkpoint means. Nothing about `LevelManager.ResetEnemies` changed.

---

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
| `SpawnEnemyInFront(int index)` | Drops `enemyPrefabs[index]` on the NavMesh in front of you, facing you. **0** = Grunt · **1** = Heavy · **2** = Boss · **3** = Legendary_Ninja (THE THIRTEENTH SHADE) · **4** = Legendary_Knight (THE IRON PENITENT) · **5** = Legendary_Spellsword (THE ASHEN CHORISTER) · **6** = Legendary_Marionette (THE PALE MARIONETTE, prototype) · **7** = Legendary_Revenant (THE EMBER REVENANT, prototype) · **8** = Legendary_Halberdier (THE ARGENT HALBERDIER, prototype) · **9** = Legendary_Drillmaster (THE DRILLMASTER, the soulslike-combat showcase: cooled signatures, a delayed overhead, a feint, an unblockable kick, punishes every flask) · **10** = Sentry_Grunt · **11** = Sentry_Heavy (parkour_enemies: the span shooters; `Pad_Sentry_Grunt` sits on the second row behind the Grunt pad, wake switch SENTRY). The list is **append-only** — 0–2 are documented everywhere and must never be renumbered |
| `SpawnDummy()` | An **inert practice dummy**: a Grunt with `aggroLocked = true` and ~1M HP, for drilling swing timing, hit reactions and posture damage against a target that never fights back |
| `ActivateBoss()` | Wakes the boss (no arena trigger in this scene) |
| `ClearAllEnemies()` | Instant **despawn** of everything, spawner-owned included — no death animation, no souls. `TestMenu`'s "Kill Nearby" is the one that kills properly. **Also switches pad auto-respawn off**, or the pads would simply refill a few seconds later and "clear" would look broken |
| `ToggleInfiniteFlask()` | Flask silently refills whenever it is not full |
| `ToggleInfiniteItems()` | A used item is handed straight back |
| `ResetSandbox()` | Removes hand-spawned enemies, restores the pads, full-heals and returns you to spawn. Re-enables pad auto-respawn, so `ClearAllEnemies()` is not a one-way door |
| `WarpToMovementYard()` | Teleports you to `YardSpawn` (`(36, 1.2, 0)`, facing +X), just inside the yard doorway. Nothing else is touched — same `Teleport` + `SetYaw` pattern as the reset. `yardSpawn` is written by the builder |

Inspector fields worth knowing: `spawnDistance` (how far in front enemies appear),
`navSampleRadius` (how far the spawn point may be nudged to find NavMesh), `dummyPrefabIndex`,
`dummyHealth` and `yardSpawn`.
