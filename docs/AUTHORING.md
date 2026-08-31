# Authoring Guide

How to add content to vibegame1 **without writing code**. Everything here is a ScriptableObject
edited in the Inspector plus a menu item that rebuilds the scene from it.

If a step in this document requires opening a `.cs` file, that is a bug in the tooling — note it in
the "Still needs code" section at the bottom rather than working around it silently.

> All menu items live under the **VibeGame1** menu. The editor must not be in play mode: every
> generator refuses to run while playing, because the level was destroyed that way once already.

---

## 1. Levels

A level is one `LevelDefinition` asset: identity, platforms, spawns, pickups, checkpoints, torches,
the boss arena, the kill plane and the player start. `LevelDefinitionBuilder` turns it into a scene.

### 1a. Migration: DONE. The level is data now.

`Assets/Data/Levels/Level_01_Level.asset` **is the level**. `LevelGreyboxBuilder.Build()`
("6. Build Level", and step 6 of Rebuild Everything) now just forwards to
`LevelDefinitionBuilder.Build()` with that asset, so the two builders cannot disagree. The literal
coordinates survive as `LevelGreyboxBuilder.BuildHardcoded()` — the reference implementation and the
fallback when no definition exists. **Do not author new content there.**

To re-derive the asset after hand-editing the scene, run **VibeGame1 → Export Current Level To
Definition**, then rebuild and diff. The round trip is the proof the two representations agree.

### 1b. Add a new level

1. `Assets/Data/Levels/` → right-click → **Create → VibeGame1 → Level Definition**.
   Easier alternative: duplicate the exported `Level_01_Level` and edit from there — starting
   from a level that plays well beats starting from an empty asset.
2. Fill in identity:
   - `levelId` — **stable, lowercase, never renamed after a build ships.** Progress, personal bests
     and ghost recordings are all keyed by it; renaming orphans all three.
   - `displayName`, `sceneName`, `orderIndex` (campaign position), `parTime` (target seconds).
3. Author contents. Each array element is one object:
   - **platforms** — `center`, `size`, `materialKey`, and `trim` for the neon edge bars.
     Give each tile its own trim colour; it is the cheapest way to make a section read as a place.
   - **spawns** — `prefabKey` (`Enemy_Grunt`, `Enemy_Heavy`, `Legendary_*`, `Boss`), `yaw`, `isBoss`.
   - **pickups** — `itemKey` matching an item asset name in `Assets/Data/Items/`.
   - **checkpoints** — `name` matters: `LevelManager.Warp()` finds checkpoints **by name**, and F5
     warps to the LAST one (`Checkpoint_4`). One per tile entrance.
   - **pedestals** — wand altars. `groundPosition` is the floor it stands on; the plinth, crystal,
     glow and trigger are built from there. Omit these and the level has no way to pick a wand.
   - **arenas** — every gated fight, mini-boss and boss alike:
     - entry `gate*` positions: rest **sunk**, rise to seal the player in.
     - `clearSpawnerName` + `hasExitGate` + `exitGate*`: a mini-boss arena. The exit gate rests **up**
       and drops when that spawner's enemy dies. Leave `clearSpawnerName` empty for the boss arena —
       that one wakes the `BossController` and never reopens.
   - **sky** — the starfield dome and eclipse. Its parameters cannot be read back off the built mesh,
     so the exporter records only that a sky exists; edit the numbers on the asset.
   - **torches**, **killZone**, **playerStart** / **playerStartYaw**.

   Reachability contract (jump 2.4 m, run 11 m/s): rise ≤ 1.5 m with gap ≤ 4.5 m, or rise ≤ 1 m with
   gap ≤ 6 m. Measure gaps edge to edge, and remember a platform's walkable top is `center.y + size.y/2`.
4. Select the asset and run **VibeGame1 → 8. Build Level From Definition**.
5. Add it to the campaign: open `Assets/Data/LevelRegistry.asset` and place it in `levels`.
   Running **VibeGame1 → 3. Create Data** also adopts any unlisted definition automatically, but it
   appends — the running order is yours to set.

### Material and prefab keys

Materials are referenced by **string key**, not by object reference: key `Platform` resolves to
`Assets/Materials/M_Platform.mat`. This is deliberate — `MaterialFactory` *recreates* its materials,
which would break hard references stored in a saved definition. Keys survive that, keep the asset
small, and make a definition readable in a git diff. A missing key logs a warning and builds with the
default material rather than failing the whole level.

Prefab keys resolve against `Assets/Prefabs/`.

---

## 2. Enemies

An enemy is one `EnemyData` asset (`Assets/Data/Enemies/`). Vitals, movement, spacing, aggression,
look and its attack repertoire.

To add one:

1. **Create → VibeGame1 → Enemy**, or duplicate `Grunt.asset`.
2. Set `preferredRange` **at or above** `attackRange`, and larger for a big enemy — a 2.2×-scale boss
   standing at 2 m fills the screen and its wind-up becomes unreadable.
3. Assign a `moveset` (below).
4. Reference it from an `EnemySpawner` prefab, then from a level's `spawns` via `prefabKey`.

> **Remember hard rule 9: a code default is not a shipped value.** Changing a field initialiser in
> `EnemyData.cs` does nothing to an asset that already exists on disk. To change a shipped value,
> either edit the asset in the Inspector or rewrite it in `DataFactory` and re-run
> **VibeGame1 → 3. Create Data**.

---


### Shipped enemies

All of these are written by `DataFactory` and will be **overwritten** by **3. Create Data** — hard rule 9.

| `EnemyData` | Prefab | Moveset | Controller | Role |
|---|---|---|---|---|
| `Grunt` | `Enemy_Grunt` | `Grunt_Moveset` | `EnemyController` | Filler; teaches the basic deflect |
| `Heavy` | `Enemy_Heavy` | `Heavy_Moveset` | `EnemyController` | Slow filler; tempo change |
| `Legendary_Ninja` — *The Thirteenth Shade* | `Legendary_Ninja` | `Legendary_Ninja_Moveset` | `EnemyController` | Mini-boss; sustained cadence + an unblockable sweep |
| `Legendary_Knight` — *The Iron Penitent* | `Legendary_Knight` | `Legendary_Knight_Moveset` | `EnemyController` | Mini-boss; patience, huge commitment both ways |
| `Legendary_Spellsword` — *The Ashen Chorister* | `Legendary_Spellsword` | `Legendary_Spellsword_Moveset` | `EnemyController` | Mini-boss; feint/transition + ranged opener + grab |
| `Boss` — *The Hollow Warden* | `Boss` | `Boss_Moveset` + phases | `BossController` | The duel; segments and level clear |

The three mini-bosses are prefabs built by **VibeGame1 → 4b. Build Mini-Bosses**
(`Editor/MiniBossFactory.cs`), *not* by `4. Build Prefabs` — so re-tuning them never rebuilds the player
rig. They use `EnemyController` on purpose: `BossController` raises `BossDefeated`, which stops the
speedrun timer and clears the level. See ARCHITECTURE.md → *Legendary mini-bosses*.

---

## 3. Movesets

An `EnemyMoveset` (`Assets/Data/Movesets/`) is a list of combos, each with:

- **weight** — relative likelihood. `1` is ordinary; `0.3` keeps a signature bait rare enough to stay
  surprising; `3` makes a filler the default rhythm.
- **minRange / maxRange** — the distance band in which the combo may be chosen. Gate a lunging opener
  to the far band so it is never picked point-blank, where an advancing jab looks like the enemy is
  walking through you.
- **label** — free text, for you. Write the tempo down: `"jab-jab-HEAVY (the signature bait)"` is the
  difference between a readable asset and four identical rows.

Design rules that keep combos fair while fast:

- Every hit fires its parry cue `cueLead` (0.28 s) before impact, whichever combo is selected. Speed
  comes from tempo *changes*, never from shortening the tell.
- Keep wind-ups at or above 0.45 s. Below that the combo stops being a parry problem and becomes a
  reaction-time problem.
- Vary tempo *inside* the phrase. `fast, fast, SLOW` is the whole game: two hits train a rhythm and
  the third breaks it.
- `comboBreathSeconds` is a floor aggression may not compress away. A combo has to read as a phrase
  with a breath after it, not an endless stream.

Existing movesets (`Grunt_Moveset`, `Heavy_Moveset`, `Boss_Moveset`) are generated by `DataFactory`
and will be **overwritten** on the next **3. Create Data**. Edit them there, or duplicate to a new
asset and point the enemy at the copy.

---

## 4. Weapons, wands and items

Generated by `DataFactory` into `Assets/Data/Weapons`, `Assets/Data/Wands` and `Assets/Data/Items`.
Add one by copying an existing block in `Assets/Editor/DataFactory.cs` and re-running
**3. Create Data**; view models come from `PrefabFactory`. This is the one content area that is still
code-shaped — see below.

**Every weapon carries a super attack.** It is the payoff for a full Pyre bar (`Q`) and it is pure
data — `UltimateAbility` reads these fields and nothing else, so a new weapon needs no new code:

| Field | Meaning |
|---|---|
| `superName` | HUD name. Shown on the "… READY [Q]" banner and as the centre banner when it fires. |
| `superKind` | `Cleave` / `Flurry` / `Quake` / `Nova`. **Presentation only** — picks how the blow is drawn. |
| `superDamage`, `superPostureDamage` | Per hit. Damage runs through `PlayerStats.DamageMultiplier`. |
| `superRadius`, `superArcDeg` | The geometry. 360° hits everything within the radius. |
| `superHits` | Applications, spread evenly over `superActive`. >1 is what makes a flurry a flurry. |
| `superWindup` / `superActive` / `superRecover` | The cadence. This is where a weapon's weight lives. |
| `superHitStop`, `superShake`, `superKnockback` | Impact. |

Give each one an identity that matches the weapon: the hammer's is slow, 360° and posture-heavy; the
dagger's is nine small stabs in a narrow cone. A super that could belong to any weapon is a bug.

**Every wand carries a `cooldown`.** It gates the riposte *blast* only — while it runs the deathblow
still lands as the melee execute — so a heavy wand can safely wait 9 s. Written in `WandFactory`.

---

## 5. Progress and the campaign

- `Assets/Data/LevelRegistry.asset` is the ordered campaign plus `initiallyUnlocked`.
- `LevelProgress` (static) persists unlocks, best times and best death counts as JSON in
  `Application.persistentDataPath/progress.json`, keyed by `levelId`.
- `LevelProgress.RecordCompletion(...)` returns `true` when the run beat the stored best — that is
  the signal for overwriting a saved ghost. It also unlocks the next level in campaign order.
- `LevelProgress.ResetAll()` wipes it, for the test menu and for a real "new game".
- JSON rather than PlayerPrefs so the file can be inspected, hand-edited for testing, and deleted.

---

## Still needs code

Honest list of what this pass did **not** make data-driven:

1. **Moveset weights and range gating are authored but not yet live.** `EnemyController.ChooseCombo()`
   still reads `EnemyData.combos` and picks uniformly. `DataFactory` mirrors each moveset down into
   `combos`, so today's behaviour is unchanged and correct — but weights and ranges do nothing until
   `ChooseCombo` calls `data.SelectCombo(distanceToTarget)` instead. That is a one-line change in a
   file this pass was not permitted to touch.
2. **Weapons, wands and items** are still authored in `DataFactory` rather than as standalone assets.
3. **Level geometry is boxes.** `PlatformDef` describes an axis-aligned box; anything else needs a
   prefab spawned through a spawn entry.
4. **Tiles are a naming convention, not a hierarchy.** `PlatformDef` has no tile field, so sections are
   distinguished only by a `T1_` / `T2_` / `T3_` / `Boss_` name prefix. Grouping them under scene
   sub-roots would break the exporter, which walks the direct children of `Level`.
