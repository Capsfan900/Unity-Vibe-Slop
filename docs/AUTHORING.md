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

   **Reachability contract.** Measure gaps edge to edge, and remember a platform's walkable top is
   `center.y + size.y/2`. Every envelope below is well inside the measured capability — the margin is
   deliberate, because a player is not a test harness.

   | Moveset | Envelope | Measured capability it is drawn from |
   |---|---|---|
   | **Base** (jump 2.4 m, run 11 m/s) | rise ≤ 1.5 m with gap ≤ **4.5 m**, or rise ≤ 1 m with gap ≤ **6 m** | 8.8 m flat, jump held |
   | **Slide-jump** (Left Ctrl, then Space) | rise ≤ 1 m with gap ≤ **8.5 m** | 12.6 m flat at a 15.8 m/s takeoff |
   | **Wall jump** (Space, airborne, near a wall) | **≤ 1.6 m of climb per push**, ≤ 5 pushes per airtime, so ≤ **8 m** of chimney between two facing faces | 2.02 m rise per push, held |

   Wall-jump geometry: two faces **1.5-3 m apart** (the scan reaches ~0.5 m past the capsule, and the push
   crosses 2.6 m in 0.22 s), and make the two faces **different heights** so the climb has an exit — you leave
   over the shorter one. A slide gate is a lintel with **1.05-1.7 m** of clearance over the deck (the slide
   capsule is 1.0 m, standing is 1.8 m); if you want it to gate rather than merely slow, put its top more than
   2.4 m above the deck so it cannot be jumped onto.

   **Tech-gated routes must be optional.** A player who has not learned the tech has to be able to finish the
   level, so a fast line is a *shortcut past* the slow route, never a replacement for it — and it must rejoin
   the main path **before** the next arena trigger, or it bypasses the gate and soft-locks the run. Both rules
   are asserted per hop in `FeatureTests > LevelStructure` (`CheckHop` / `CheckHopIsGated`), measured off the
   built geometry rather than off the numbers in the definition.
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
| `Legendary_Knight` — *The Iron Penitent* | `Legendary_Knight` | `Legendary_Knight_Moveset` | `EnemyController` | Mini-boss; the spinning furnace — a sustained parry cadence with an outsized payoff. **Imported body** (`Assets/Enemies/IronPenitent.fbx`) |
| `Legendary_Spellsword` — *The Ashen Chorister* | `Legendary_Spellsword` | `Legendary_Spellsword_Moveset` | `EnemyController` | Mini-boss; feint/transition + ranged opener + grab. **Imported body** (`Assets/Enemies/AshenChorister.fbx`) |
| `Legendary_Revenant` — *The Ember Revenant* | `Legendary_Revenant` | `Legendary_Revenant_Moveset` | `EnemyController` | **PROTOTYPE, sandbox pad only (x − 28).** The first BURNING enemy — `EmberAura` gives it a constant emission floor, rising embers and a weak light, all riding `Posture.Ratio`. A READ rather than a cadence: slow committed swings, a 40° thrust, an unblockable kick, and a 1.4 s overhead recovery that is the biggest punish window in the game. **Imported ANIMATED body** (`Assets/Enemies/EmberRevenant.fbx`, from `ai_skelly_tool`). See §2b. |
| `Legendary_Marionette` — *The Pale Marionette* | `Legendary_Marionette` | `Legendary_Marionette_Moveset` | `EnemyController` | **PROTOTYPE, sandbox pad only — deliberately not in `Level_01`.** The whirl: deflect every pass on a 0.69 s beat (the parry contract's floor), nine passes to a phrase, six clean deflects break it. **Imported ANIMATED body** (`Assets/Enemies/PaleMarionette.fbx`), driven by `PuppetVisuals` + an `Animator`. See §2b. |
| `Boss` — *The Hollow Warden* | `Boss` | `Boss_Moveset` + phases | `BossController` | The duel; segments and level clear |

### 2a. Importing a forge model — and the one source-art exception

**Hard rule 4 says everything in the scene is regenerable. `Assets/Enemies/` is the documented
exception.** An FBX exported from `enemy-forge` is *authored art*, like the CC0 audio under
`Resources/Audio` — no menu item can rebuild it. So it is a **committed asset**, and
`MiniBossFactory` references it **by path and fails loudly** if it is missing: a clear `Debug.LogError`
naming the file and how to restore it, and the prefab is abandoned. There is deliberately **no silent
fallback to primitives**, because a boxy stand-in in a shipped build reads as a bug rather than as a
missing file. That folder holds `AshenChorister.fbx` and `IronPenitent.fbx` plus the `*_source.png`
drawing each was generated from, kept alongside for provenance.

To bring in a new one:

1. **Copy the FBX into a path containing `/Enemies/`**, with a sane name — not the generator's
   timestamp. `Assets/Editor/EnemyForgeImporter.cs` (the tool's own `AssetPostprocessor`, vendored
   as-is under its `EnemyForge.Editor` namespace) applies metre scale and sane mesh defaults to
   anything matching that path. Copy the source drawing in beside it.
2. **Check the rig type is Generic.** The postprocessor asks for Humanoid, which is wrong here and
   often does not run at all — see ENGINEERING-LOG. Nothing in this project is animated by an
   `Animator`; `EnemyVisuals` drives plain transforms, so a Humanoid avatar buys nothing and a legless
   silhouette cannot produce a valid one.
3. **Verify the facing by rendering it**, from ±X and ±Z, and looking. Forge output is "Unity axes",
   which fixes the scale and the ground plane but says nothing about which way the figure looks. Both
   shipped models face **+Z**; do not assume the next one does.
4. **Add a `ModelSpec` to `MiniBossFactory.ModelFor`**: the file name, the hover lift, a yaw correction
   if needed, and the local positions of the glowing slot, the shoulder, the hand and the weapon-FX
   marker. Everything else — collider, agent, `EnemyVisuals`, posture bar, alert cube — is shared with
   the primitive path and needs no per-model work.
5. **If the FBX ships a `*.clips.json` beside it, it is ANIMATED** — do §2b instead of stopping here.
6. Run **VibeGame1 → 4b. Build Mini-Bosses** and look at it at `preferredRange` in the real lighting.

Three things that will look broken if you skip them:

- **Physics stays on the prefab root.** The collider, the `NavMeshAgent` and `EnemyData.scale` belong to
  the root; the art is parented under `Visual/LungeRoot`. A legless model **hovers by lifting the mesh**
  (`ModelSpec.yLift`, 0.10 m on the Chorister), never by touching `agent.baseOffset` — that would move
  the agent, the capsule and the distance/cone impact test with it.
- **The material must be `Universal Render Pipeline/*`** or it renders magenta. Forge FBXs carry vertex
  colours and no texture, and `EnemyVisuals` overwrites `_BaseColor` from `EnemyData` every frame
  anyway, so the shared `M_Boss` is the right answer and no new material is needed.
- **Every `EnemyVisuals` binding must be non-null.** They are all null-guarded at runtime, so a missed
  one is silent — the enemy just quietly stops telegraphing. `MiniBossFactory` logs an error if any of
  the seven is null, and `FeatureTests` → `Legendaries` asserts all of them plus the URP material.

### 2b. Importing an ANIMATED forge model — splitting `clips.json`

`Assets/Enemies/PaleMarionette.fbx` is the first rigged, clip-carrying forge export the project has
taken. The two older models (`AshenChorister`, `IronPenitent`) are static meshes; this one has a
24-bone skeleton and fifteen animations.

**The FBX imports as ONE take.** enemy-forge concatenates every animation into a single 517-frame
timeline, which Unity imports as one clip named `Take 001`. The frame ranges that carve it back up
ship alongside the FBX as `<model>.clips.json` — a list of `{name, start, end, loop, events}`.

Typing those ranges into the Rig/Animation inspector by hand is exactly the hand-authored state hard
rule 4 forbids: re-import the FBX and they are gone, silently, and the enemy stands in its bind pose.
So the manifest is the source of truth and one menu item applies it:

**VibeGame1 → 4a. Split Forge Animation Clips** (`Editor/ForgeClipSplitter.cs`). It walks every
`*.clips.json` under `Assets/Enemies/`, and for each one writes `ModelImporter.clipAnimations` from the
manifest and reimports. It is idempotent, and it is included in neither `0. Rebuild Everything` nor
`4b`, so **run it once after copying an animated FBX in, and again after re-exporting one**.

To add another animated model:

1. Copy the FBX **and its `.clips.json`** into `Assets/Enemies/`, named to match
   (`Foo.fbx` + `Foo.clips.json`). Copy the source drawing in as `Foo_source.png` for provenance.
1b. Run **VibeGame1 → Probe Forge Models** (`Editor/ForgeModelProbe.cs`) and write the `ModelSpec` from
   what it reports. **Do not infer pivots from the bounding box.** The Ember Revenant's mesh reaches
   y 1.96 while its head bone sits at 1.20 — the top 0.76 m is spikes and hood with no bones in it — so a
   bounds-derived eye or deathblow glyph floats in mid-air, silently. The probe also prints per-clip hand
   separation, which is how the spin/pose clip gets picked by measurement instead of by name.
2. Run **4a. Split Forge Animation Clips**. Confirm in the console that it reports the clip count you
   expect, then check the FBX's sub-assets: fifteen named clips, none of them `empty`.
3. Add a `ModelSpec` to `MiniBossFactory.ModelFor` with `animated = true` plus `idleClip`,
   `attackClip`, `heavyClip` and (if it whirls) `spinClip` and `spinPrefix`.
4. Run **4b. Build Mini-Bosses**. It calls `PuppetAnimatorFactory.Build`, which generates
   `Assets/Animation/<Prefab>_Animator.controller` from the clips in the FBX, and it **errors on any
   clip name the spec asks for that the model does not have** — a missing clip is otherwise completely
   silent at runtime.

Four things that are settled and should not be re-litigated per model:

- **The rig is `Generic`, not `Humanoid`.** `EnemyForgeImporter` asks for Humanoid on first import and
  it is wrong for every forge silhouette. `ForgeClipSplitter` forces Generic, so a stray reimport
  cannot put it back and leave every clip retargeted to mush. The clips were authored on this exact
  skeleton; retargeting buys nothing.
- **No `AnimationEvent`s are written**, even though the manifest carries them. An event with no
  receiver warns on every play, and an event that drove gameplay would make the fight's timing hostage
  to a clip length. The manifest's `OnAttackHit` / `OnRoar` times ARE used — at *build* time, baked
  onto `PuppetVisuals` so playback speed can be scaled (below).
- **Timing stays data-driven.** `PuppetVisuals` scales `Animator.speed` so the clip's own contact frame
  lands on the impact `EnemyAttackData` specifies. If a clip is the wrong length the CLIP is stretched,
  never the attack. A scale outside `minClipSpeed`..`maxClipSpeed` logs a warning naming the clip.
- **Pick the clip by measuring, not by name.** For the Marionette the obvious choice for a spin pass
  was `AttackSwing`; sampling the `LeftHand`/`RightHand` separation through every clip showed it tucks
  the arms to a 0.76 m span, while `Roar` holds 2.0 m for its whole length. `Roar` is the spin clip.
  See ENGINEERING-LOG.

---

The four mini-bosses are prefabs built by **VibeGame1 → 4b. Build Mini-Bosses**
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

### Authoring an attack means authoring its WIND-UP POSE

An attack is not finished when its timings are set. Timing alone cannot say *which* attack is coming — a
0.45 s jab and a 0.72 s heavy look identical for their first 0.45 s — so the **silhouette** carries it,
and the silhouette is data: `EnemyAttackData.windupPose`, written in `DataFactory` via the `Pose(...)`
helper (rule 9), read by `EnemyVisuals.PoseFor`.

`authored` off is a legitimate answer. Most of the 25+ attack assets are content that never needed its
own shape, and they fall back to a cone-derived pose (wide cone → sweep, narrow → thrust, otherwise
overhead). **Author a pose when confusing this attack with a neighbour would cost the player something**
— a different dodge direction, a wasted parry, an unblockable taken to the face.

**Do not author the numbers. Author the shape, then solve for the numbers.** `armWindup` drives the
SHOULDER; the hand trails it by `weaponLag` and the body is rotated underneath both, so the blade lands
tens of degrees from wherever the arithmetic says. Every pose in the game was authored this way once and
ten of the eleven made a shape other than the one their comment claimed — see
[ENGINEERING-LOG.md → *A wind-up pose cannot be computed, only photographed*](ENGINEERING-LOG.md).

The loop that works:

1. Decide the shape in words, against its neighbours: *a level bar at chest height on the right*, *a
   vertical mast a body-height above the head*, *a short stub aimed at the camera and below the centre
   line*. Three shapes per moveset is plenty; six that all read as "an attack is coming" is worse than
   three that read.
2. Film what you have: `VibeGame1.FrameFilm.RunWindups(dir, "Enemy_Grunt", 3.8f)` in play mode. It stages
   a clean enemy, parks the player square in front at that distance and writes a `_mid` and a `_peak`
   frame per attack. **Look at every frame.**
3. Tune against the measurement, not the Euler. The signature that matters is the blade's on-screen
   **angle** (0 = level, ±90 = upright), its **length** after foreshortening, and where its **tip** sits
   in body-heights from the body's centre.
4. Make `armStrike` *resolve* `armWindup` — the swing has to visibly travel through. `FeatureTests`
   requires at least 40° between them.
5. Add the pair to `FeatureTests → WindupPoses` if confusing it costs the player.

Two failure modes that are invisible in the Inspector and obvious in a photograph:

- **A blade cocked back over the head foreshortens to a stub.** A first-person player only ever sees the
  enemy from the front. Keep the blade in the view plane.
- **Rotating a blade rotates it out of the light.** The same blade can go from bright tan to near-black
  on a near-black enemy. The dark one is the one nobody parries.

Existing movesets (`Grunt_Moveset`, `Heavy_Moveset`, `Boss_Moveset`) are generated by `DataFactory`
and will be **overwritten** on the next **3. Create Data**. Edit them there, or duplicate to a new
asset and point the enemy at the copy.

---

## 4. Weapons, wands and items

Generated by `DataFactory` into `Assets/Data/Weapons`, `Assets/Data/Wands` and `Assets/Data/Items`.
Add one by copying an existing block in `Assets/Editor/DataFactory.cs` and re-running
**3. Create Data**; view models come from `PrefabFactory`. This is the one content area that is still
code-shaped — see below.

**Every weapon is a short blade, and the shape is the spec sheet.** The whole set is dagger-scale
(0.27-0.32 m of model above the fist), so a new weapon must NOT be made to feel heavy by being long.
Two things are load-bearing when you add one:

| Requirement | Why |
|---|---|
| The prefab must have a part named `Grip*` | `WeaponViewmodel.CloseHandOn` slides the HAND onto it. No `Grip*` and the fist closes on empty air — `VM_Hammer` shipped like that once. |
| `viewmodelScale` must be written in `DataFactory` (rule 9) | It defaults to 0.5 on the class and a code default never reaches an asset that already exists. Pick it so the model lands in the 0.27-0.32 m band. |

Give it a silhouette no existing weapon owns — the four in use are a wide **cross**, a **top-heavy**
mass head, a bare **needle** and a **wavy serrated** blade — and a hue no existing weapon owns. Two
weapons the player has to squint at is the same bug as two supers that could belong to anyone. The
shipped set and the reasoning: [`ARCHITECTURE.md > The blade family`](ARCHITECTURE.md#the-blade-family--weapon-viewmodels).

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
