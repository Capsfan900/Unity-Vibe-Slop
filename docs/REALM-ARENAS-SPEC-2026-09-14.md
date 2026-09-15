# Realm arenas: themes and environmental hazards (Fable plan, 2026-09-14)

The user asked: "Make the arenas themed for each boss and have environmental hazards." Plan only; the lead
(Opus 5) and Sonnet workers implement. Every number below is read from the shipped code/data, not from a
field initialiser. Nothing here cuts, reorders or softens the four gated duels or the Warden.

## 0. Facts the plan stands on

| fact | value | source |
|---|---|---|
| the cell | floor R 18 (area 1017.9 m²), shell 27, walls/ceiling 20; centres (700, 0, 0/100/200/300/400) | `LevelDefinitionAuthoring.cs:1597-1599` |
| realm-local points | entry (0,1.2,-11.7), enemy (0,0.1,3.6), partner (5,0.1,1.6), exit (0,1.5,-15.5), pickups (+/-6.3,1.2,-3), Warden pickup (-6.3,1.2,-5.4) | `:1603-1604`, `SetSolar :2014-2031` |
| theme keys (KEEP: `SolarTransitionTests` pins one cut per key) | T1 SolarCyan, T2 SolarGold, T3 SolarAzure, T4 SolarViolet, Warden SolarGhost | `:1657-1673` |
| builder | floor = M_Platform cylinder; 20 wall boxes + ceiling slab in `M_SolarRealm<Theme>`; one point light 2.6 at 0.75 x ceiling; NavMesh bakes RenderMeshes on layer 0 only, `SkyLayer` is excluded | `LevelDefinitionBuilder.cs:677-724, 195-199` |
| gating seams | `BossArenaTrigger.Cleared` (public), `GameEvents.RealmFightStarted(arena)` (mini realms, fired inside `SolarArenaPortal.Enter`), `GameEvents.BossStarted/BossDefeated` (Warden), `GameEvents.PlayerRespawned`; `SolarArenaPortal.IntroHoldSeconds` 1.6 | `BossArenaTrigger.cs:48`, `SolarArenaPortal.cs:26,146`, `GameEvents.cs:20-28` |
| damage seam | `PlayerCombat.ReceiveAttack(in AttackInfo)` DEREFERENCES `a.attacker` (`:99,122,157,187`) - a hazard must pass a live `EnemyController`. `attack` may be null (guarded at `:122`; unused on Hit) | `PlayerCombat.cs:84-207` |
| unblockable | `ParryMath.Evaluate(..., unblockable, ...)` never returns Perfect/Blocked for unblockable; Hit path: damage x stagger mult, posture x1.5, flask interrupt, 2 m grounded shove | `ParryController.cs:251`, `PlayerCombat.cs:112,186-202` |
| precedent | `CinderJudgeStorm` ticks by pure distance test on a fixed clock, no collider, through ReceiveAttack | `CinderJudgeStorm.cs:101-148` |
| phase 2 reads | `EnemyController.InPhase2` (public), `BossController.Phase` (public) | `EnemyController.cs:111`, `BossController.cs:13` |
| player | ground 11 m/s, jump 2.4 m, dash 22 m/s, gravity -30; balloon `launchSpeed` 14 (apex 3.27 m) | `FirstPersonMotor.cs:32-45`, `Balloon.cs:29` |
| bosses (spec S5) | Lancer hover 3 m, verdict band 4.5-14, charge band 4.4-6.5 lunge 3.32; Dancer bankRange 20, ideal wall 6-12; Judge ring 3.6; V18 lift 11 / throw 6 m out, clap 4.1; Warden slam 4.3 + 2.2 lunge, thrust 4.5 + 2.9 | `BOSS-SPATIAL-SPEC-2026-09-14.md` S2, S5 |
| bloom | 1.05 threshold; `M_AlertTell` 3.0 is THE tell; warm = combat information or fire; per-object light cap 4 | `ANIMATION-VFX.md` rules 9, 10, 12 |
| audio | no per-realm bed exists (`Resources/Audio/Music`: ambient.ogg, boss.ogg only). Usable one-shots: `Sfx.Tension`, `Detonate`, `Thunder`, `Drone`, `Tick` | `ProceduralSfx.cs:5-22` |
| Level Studio | `solarRealm` is ONE record (`LevelDraftStore.cs:622`), diffed by whole-object `JsonUtility.ToJson` (`LevelDraftDiff.cs:48`), exported by `JsonUtility.FromJson<SolarRealmDef>` (`LevelDefinitionExporter.cs:106`) - additive fields round-trip with NO Level Studio code | |

## 1. One hazard language, five accents

Every hazard speaks the enemy tell's grammar so nothing new has to be learned:

- **warn** >= 1.2 s: the marker mesh swaps to `M_RealmWarn` (new, `#FF5A18 x 0.95`, peak 0.95 < 1.05: warm because it IS combat information, rule 10) and `Sfx.Tension` plays once.
- **cue** exactly `Projectile.CueLead` 0.28 s: marker swaps to `M_AlertTell` (3.0, the existing tell) - the same red flash a red enemy attack gives.
- **active**: marker stays `M_AlertTell`; damage lands. All hazards are **unblockable** (red = move). A parryable hazard would route `attacker.OnParried` into the boss's brain (posture, recoil) - a lead decision, not taken here.
- **rest**: marker back to its realm accent material.
- **No colliders, no physics.** Like `CinderJudgeStorm`, every hazard is a distance/angle test on the player's root against its own clock. No trigger layers, no CharacterController push, no hidden velocity (rule 10 holds by construction).
- **Damage** `player.ReceiveAttack(new AttackInfo { attack = null, attacker = owner, damage = def.damage, unblockable = true })` where `owner` = `arena.clearSpawner.Instance`'s `EnemyController`; if it is null (dead/destroyed) the tick is skipped. Per-player re-hit cooldown 1.0 s per hazard.
- **Gate.** Armed only after `RealmFightStarted(myArena)` + `ArmDelay` = `IntroHoldSeconds` 1.6 + 0.4 = **2.0 s** (first warn ends >= 3.2 s after arrival); the Warden realm arms on `BossStarted` + 2.0 s. Disarmed (and markers reset) on `arena.Cleared` (polled), `BossDefeated`, `PlayerRespawned`. Disarmed = no damage, no clock.
- **Phase 2**: `owner.InPhase2 || partner.InPhase2` (Warden: `BossController.Phase >= 1`) scales the period by `phase2PeriodScale` and applies the per-realm escalation below.
- **Lights:** none added. One `SolarLight` per realm stays the only light (per-object cap 4 untouched; `TorchDensityTests` unaffected).
- **Non-solid props go on `Starfield.SkyLayer`** (like `VisualSphere`) or the RenderMeshes bake turns a 6 m-high ring into a floating NavMesh floor. Solid props stay layer 0, static, and carve the bake.

Safe-area rule: `safe = (floor - hazard active area - boss area attack - solid prop footprints) / floor`, computed at the worst simultaneous overlap. `EnemyController.MaxSimultaneousAttackers = 1` so only ONE boss area attack is live at a time. Floors: T1-T3 >= 0.80, T4 >= 0.60, Warden >= 0.33 (justified in S2.5).

## 2. The five realms

Realm-local coordinates (x, z) on the floor; y is height above the floor. Azimuth a: (R sin a, R cos a).

### 2.1 T1 - Seraph Lancer alone: THE AERIE (SolarCyan)
**Identity:** a shattered cathedral vault open to a white sky; the roost of a knight who fights from the air.
**Palette:** walls/ceiling `M_SolarRealmCyan` (as built), floor `M_Platform`, accent `M_NeonCyan` (#35DCEC x1.0), motes tint #35DCEC.
**Dressing (props):** 8 *vault ribs* - solid boxes 0.6 x 14 x 0.6 at r 17.2, azimuths 0/45/.../315, tilted 12 deg inward (top leans to r ~14.3 at 14 m; footprint stays at the rim so no lane inside r 16 is touched); 6 *reliquary lanterns* - non-solid 0.4 cubes `M_NeonCyan` at y 8, r 12, azimuths 30/90/.../330 (sightline markers under the hover, above every eye line).
**Hazard 1 - Verdict Beams** (kind Strike, from the ceiling disc): 3 circles radius 2.2 at (0, +9), (-7.8, -4.5), (+7.8, -4.5) (a triangle of radius 9, inside the 4.5-14 javelin band). Period 6.0 s, offsets 0 / 2.0 / 4.0 s so at most ONE beam is active: warn 1.2 (floor ring `M_RealmWarn` + a 20 m, 30 %-alpha column), cue 0.28, active 0.35 (column `M_AlertTell`, one hit 14 unblockable on anyone inside), rest. Phase 2: period 4.5, radius 2.6. Marker: a flat cylinder 0.05 thick on the floor + a column (both non-solid, SkyLayer).
**Hazard 2 - Updraft Roost** (OPTIONAL, default `enabled = false`, LEAD decision): 4 vents radius 1.5 at (+/-8.8, +/-8.8) (r 12.4). A grounded player inside calls `motor.Launch(14f)` (the balloon's shipped number: apex 3.27 m = the Lancer's 3 m hover) with a 1.5 s per-vent cooldown; no damage. It changes the fight (a hovering Lancer becomes reachable) - that is why it ships off.
**Fit:** no solid inside r 16 -> the 6.5 m charge line and the 14 m verdict line are clear everywhere; ceiling 20 >= 6. NavMesh: ribs are 8 posts at r 17.2 with 13 m gaps -> one island.
**Safe area (worst, phase 2):** beam 21.2 + Lancer Heavy reach (2.9 + 0.5)^2 pi = 36.3 + charge line 6.5 x 1.5 = 9.8 -> 67.3 m² -> **93 % safe**.

### 2.2 T2 - Orbit Dancer alone: THE ORRERY (SolarGold)
**Identity:** a brass astronomer's orrery; she dances among the planets and her discs bank off the gnomons.
**Palette:** `M_SolarRealmGold`; accent `M_NeonYellow` (#D8C22A x0.75); motes #D8C22A.
**Dressing:** 4 *gnomon pylons* - SOLID boxes 1.4 x 7 x 1.4 (`M_Stone`, `M_NeonYellow` trim bars at the top) at r 12, azimuths 45/135/225/315 = (+/-8.49, +/-8.49). 12 *orbit ring segments* - non-solid 0.3 x 0.3 x 4.7 boxes `M_NeonYellow` forming a ring r 9 at y 6. 1 *sun orb* - non-solid 1.2 m sphere `M_SolarGold` at (0, 1.6, 0).
**Why the pylons:** her bank probes (40/65/90 deg, 20 m, `OrbitDancerDiscs.cs:304-333`) hit layer-0 verticals; the wall is 16-18 m from a fight near the centre (path 32-36 m, > the 12-24 m ideal). Pylon faces at r 11-13 are 6-12 m from any fight inside r 6 - exactly her ideal wall. Pylons are layer 0 like the walls, so the mask already includes them. If `LastBanked` stays low in the harness, widen to 2.0 m (lead call).
**Hazard 1 - The Gnomon Arm** (kind Sweep): a bar from r 3.0 to r 10.5 (length 7.5, width 0.5, height 1.0, `M_Stone` body, leading face `M_AlertTell` permanently - a standing red = "move"), rotating at **24 deg/s** (15 s/rev). Tip speed 10.5 x 0.419 = 4.4 m/s < 11 m/s: outrunnable everywhere; jump apex 2.4 m clears the 1.0 m top; the bar crosses a point in 0.11 s at the tip. Damage 16 unblockable, re-hit 1.0 s. Test: player inside r 3-10.5, |angle - armAngle| < atan(0.25 / r) + 0.4 deg, root y < floor + 1.0. Phase 2: 36 deg/s (10 s/rev, tip 6.6 m/s) and a second arm at +180 deg (5 s between passes). Clearance: arm tip 10.75 (with half-width) vs pylon inner face 12 - 0.99 = 11.01 -> 0.26 m; ring at y 6 is above it.
**Fit:** hub r < 3 (28 m²) and annulus r > 10.75 (654 m²) are never swept -> the arm cannot trap. Charge band 4.4-6.5 crosses the arm freely (agents ignore it). Sightline: a 1.4 m pylon at 6 m subtends 13 deg; a disc's last 0.28 s at 16 m/s is 4.5 m, so a bank returning from a pylon face >= 4.5 m away shows its cue in the open. Playtest item.
**Safe area (worst, phase 2):** 2 arms 7.5 + 1.5 m lead band ahead of each 22.5 = 30 + Heavy reach (2.5 + 0.5)^2 pi = 28.3 + pylons 7.8 -> 66 m² -> **93 % safe**.

### 2.3 T3 - Ember Revenant + Pale Marionette: THE ASHEN STAGE (SolarAzure)
**Identity:** a burned puppet theatre - a cold blue stage, her strings hanging from the flies, his embers underfoot.
**Palette:** `M_SolarRealmAzure`; accent `M_NeonPink` (the azure trim, #2F6BFF x0.95); motes ash grey #8A8F99 (never warm: warm is the hazard).
**Dressing:** 8 *strings* - non-solid 0.12 x 20 x 0.12 columns `M_SentryGhost` at r 15, azimuths 0/45/.../315. 2 *proscenium arches* - SOLID posts 0.6 x 9 x 0.6 at (+/-14, +/-3) with non-solid lintels 0.6 x 0.6 x 6.6 at y 9.3 (`M_Stone`). Posts sit at r 14.3: 7.3 m from the exit, 9.2 m from the pickup, 11.7 m from entry.
**Hazard 1 - Cinder Grates** (kind Strike): 6 circles radius 1.8 on a ring r 8, azimuths 0/60/.../300: (0,8) (6.93,4) (6.93,-4) (0,-8) (-6.93,-4) (-6.93,4). Period 5.0 s; even grates offset 0, odd grates offset 2.5 -> at most 3 active. Warn 1.2 (grate `M_RealmWarn`, ash motes rise), cue 0.28, active 0.5 (a 4 m ember column `M_AlertTell`; one hit 16 unblockable), rest 3.0. Phase 2 (either occupant): period 3.6, active 0.7. The grate at (0,-8) is 3.7 m from the entry and cannot fire before t = 3.2 s.
**Fit:** hub r < 6.2 (121 m²) and annulus r > 9.8 (716 m²) are never grated; a Revenant thrust (3.5-8 m) and a Marionette lash (8 m) cannot cover both. No solid inside r 14 -> every band in S5 is clear. NavMesh: two posts, 6 m apart -> one island.
**Safe area (worst, phase 2):** 3 grates 30.5 + Marionette whirl (3.1 + 1.15)^2 pi = 56.7 + posts 0.7 -> 88 m² -> **91 % safe** (Revenant slash 61 m² instead of the whirl: 92 %).

### 2.4 T4 - V18 Grappler + Cinder Judge: THE BRAND COURT (SolarViolet)
**Identity:** an execution ground: six iron brands set in the floor that the Judge's verdict ignites in turn, ringed by gibbets.
**Palette:** `M_SolarRealmViolet`; accent `M_RealmBrandViolet` (new, `#A45CFF x 0.9`, peak 0.9); motes #A45CFF.
**Dressing:** 4 *gibbets* - SOLID 1.0 x 8 x 1.0 `M_Boss` posts at r 14, azimuths 30/150/210/330 = (+/-7, +/-12.1), each with a non-solid 0.3 x 0.3 x 2 arm at y 7.8 pointing inward. 6 *brand seams* - non-solid 0.15 x 0.03 x 12 strips on the floor along the wedge edges (azimuths 0/60/.../300, r 4 -> 16), `M_RealmBrandViolet` at rest.
**Hazard 1 - The Brands** (kind Strike, sector variant `sectorDegrees = 60`): six 60 deg wedges of the annulus r 4-16 (125.7 m² each); the hub r < 4 and the rim r > 16 are never branded (the rim is where wallBias charges exit; the hub is the duel's centre). Wedge k ignites at t = k x T, T = 3.0 s: warn [kT - 1.48, kT - 0.28] (both seams + wedge floor decal `M_RealmWarn`), cue [kT - 0.28, kT], active [kT, kT + 1.5] ticking every 0.5 s (8 per tick, unblockable; 3 ticks max = 24). Clockwise; one lap 18 s. Phase 2 (either occupant): T 2.2 and the opposite wedge (k + 3) burns with k.
**Throw interaction:** grab -> lift 0.9 + hold 0.35 + flight 0.505 = 1.76 s > warn + cue 1.48, so the wedge the player will land in is already lit or resting when the grab resolves; landing in an active wedge costs at most 2 ticks (16) on top of GrabSlam. Never lethal alone. A carried player (`motor.IsCarried`) is never ticked.
**Fit:** no solid inside r 13.5; the Judge's 3.6 ring can at worst touch one gibbet face (1.0 m of a 22.6 m circumference - an exit always exists); V18's 6 m throw from anywhere r <= 8 lands at r <= 14 (a post is a solid the motor collides with; no clip). Ceiling 20 >= 14. NavMesh: gibbets 12 m apart -> one island.
**Safe area:** P1: wedge 125.7 + storm ring 40.7 + charge line 7.1 x 1.5 = 10.7 -> 177 m² -> **82.6 % safe**. P2: 2 wedges 251.4 + clap ring 4.1^2 pi = 52.8 + 10.7 -> 315 m² -> **69 % safe**. Floor 60 % holds; the storm and a brand can never leave zero floor because the hub r < 4 (50 m²) is outside every brand and the storm is centred on the Judge, who is not standing in the hub AND in the wedge at once (ring 3.6 < hub-to-wedge distance 4).

### 2.5 Warden: THE HOLLOW COURT (SolarGhost)
**Identity:** a dead sun's throne floor; the hollow tide breathes in from the rim and the court's sentinels watch it come.
**Palette:** `M_SolarRealmGhost`; accent `M_NeonRed` (the tile-4 green, #3FE07A x0.95); motes #3FE07A.
**Dressing:** 12 *sentinels* - SOLID 0.8 x 3.2 x 0.8 `M_Boss` bodies with a non-solid 0.5 head cube at r 16.5, every 30 deg (the entry at (0,-11.7) is 4.8 m from the nearest). Heads are the tide's marker meshes.
**Hazard 1 - The Hollow Tide** (kind Tide): the annulus r in [rTide, 18]. Warn 1.5 (heads `M_RealmWarn`, `Sfx.Tension`, ground motes thicken at the rim), cue 0.28 (heads `M_AlertTell`, `Sfx.Thunder`), active 3.0 (tick 0.5 s, 10 unblockable), rest 8.0 -> period 12.78 s, duty 23 %. `BossController.Phase` 0: rTide 14; Phase >= 1: rTide 12.5. From the rim to r 12.5 is 5.5 m = 0.5 s at 11 m/s, against a 1.78 s warning.
**Safe area:** P0: safe disc r 14 = 616 m² minus slam reach (4.3 + 2.2)^2 pi = 133 -> 483 m² -> **47 %**. P1+: disc r 12.5 = 491 - 133 -> 358 m² -> **35 %** (>= floor 33 %: a 25 m-wide disc is 2.3 s of running, the Warden's thrust line is 7.4 m long inside it, and the tide is off 77 % of the time).

## 3. Data shape (additive, defaults build nothing)

`Assets/Scripts/Data/LevelDefinition.cs`, beside `SolarRealmDef` (never rename an existing field):

```csharp
[Serializable] public class RealmPropDef      // one type covers ribs, pylons, strings, arches, gibbets, sentinels, rings
{
    public string name = "Prop";              // stable; the builder names the GameObject with it
    public Vector3 localCenter;               // realm-local, y = centre height above the floor
    public Vector3 size = Vector3.one;
    public Vector3 euler;                     // the T1 rib tilt
    public bool solid = true;                 // solid: layer 0, static, carves the NavMesh; else SkyLayer, no collider
    public string materialKey = "Stone";
}
public enum RealmHazardKind { Strike, Sweep, Tide }
[Serializable] public class RealmHazardDef
{
    public string name = "Hazard"; public RealmHazardKind kind; public bool enabled = true;
    public Vector3[] localCenters = new Vector3[0];   // Strike circles (ignored when sectorDegrees > 0)
    public float radius = 2f, innerRadius = 0f, outerRadius = 0f, sectorDegrees = 0f;  // Strike r / Sweep span / Tide inner; wedge width
    public int count = 1;                     // wedges (Strike sector) or arms (Sweep)
    public float periodSeconds = 6f, warnSeconds = 1.2f, activeSeconds = 0.35f, tickSeconds = 0f, phaseOffsetSeconds = 0f;
    public float[] phaseOffsets = new float[0];        // per-centre offsets (Strike)
    public float degreesPerSecond = 24f;      // Sweep
    public float damage = 14f;                // 0 = no damage (the optional T1 vents use launchUpSpeed instead)
    public float launchUpSpeed = 0f;          // > 0: motor.Launch(launchUpSpeed) on a grounded player inside; no damage
    public float phase2PeriodScale = 0.75f, phase2Radius = 0f, phase2InnerRadius = 0f, phase2DegreesPerSecond = 0f;
    public int phase2ExtraCount = 0;          // second arm / opposite wedge
}
// on SolarRealmDef:
public string accentMaterialKey = "";     // "" = none
public string moteTintHex = "";           // "" = no realm motes
public RealmPropDef[] props = new RealmPropDef[0];
public RealmHazardDef[] hazards = new RealmHazardDef[0];
```

- **Authoring:** new `Assets/Editor/RealmThemeAuthoring.cs` with `public static void Apply(SolarRealmDef r, string gateName)` that REWRITES `accentMaterialKey`, `moteTintHex`, `props[]`, `hazards[]` absolutely for the five gates (arrays regenerated, never appended, so `SolarArenaTests.SolarMigrationIsIdempotent` keeps holding). One line at the end of `LevelDefinitionAuthoring.SetSolar` (`:2034`): `RealmThemeAuthoring.Apply(r, gateName);` - the LEAD adds that line, since the lead is editing that file now.
- **Builder:** in `LevelDefinitionBuilder.BuildSolarArena` after `BuildRealmBoundary` (`:698`): `RealmDressingBuilder.Build(realm.transform, def, ctx, fight, buildRuntimeBehaviours)` (new file `Assets/Editor/RealmDressingBuilder.cs`): props via `LevelPieceFactory.Box` (solid) or a collider-stripped SkyLayer box (non-solid); a `RealmMotes` child = `AmbientMist` with `volumeCenter (0,2,0)`, `volumeSize (36,6,36)`, tint from `moteTintHex`, `alphaMax 0.14`, `sizeMin/Max 0.4/1.0`, `emissionRate 3` (budget: player mist 0.26 + motes 0.14 + wall emission ~0.3 = 0.70 < 1.05); one `RealmHazard` component per enabled hazard (runtime-behaviours builds only), with `arena = fight`, `floorCenter = def.realmCenter`, `floorRadius`, its marker renderers, and a copy of the def.
- **Materials:** two new `Spec` rows in `MaterialFactory.Table`: `M_RealmWarn` (black, `#FF5A18 x 0.95`) and `M_RealmBrandViolet` (black, `#A45CFF x 0.90`).
- **Level Studio:** round-trips with no code (S0, last row). Optional validator rule (LEAD, `LevelStudioValidator.cs`): every hazard centre and prop footprint inside `realmFloorRadius`.
- **Docs:** `docs/DATAFLOW.md` "THE CELL" block gains three lines (props, motes, hazards + gating) in the same change.

## 4. Runtime component (one class, three kinds)

`Assets/Scripts/Level/RealmHazard.cs` (MonoBehaviour) + `Assets/Scripts/Level/RealmHazardMath.cs` (pure statics):

- `RealmHazardMath.PhaseAt(t, period, warn, cue, active, offset) -> Idle|Warn|Cue|Active` with `cue == Projectile.CueLead` asserted by test; `WedgeIndex(angleDeg, count)`, `InsideSector(p, c, rIn, rOut, wedge, count)`, `InsideSweep(p, c, rIn, rOut, armAngleDeg, halfWidthMetres, maxHeight)`, `SweepTipSpeed(rOut, degPerSec)`, `SafeFraction(floorR, hazardArea, bossArea, propArea)`, `WedgeArea(rIn, rOut, deg)`.
- Component: subscribes `GameEvents.RealmFightStarted` (arms when `a == arena`), `BossStarted` (arms when `arena.clearSpawner == null`), `BossDefeated`, `PlayerRespawned`; `Update` early-outs unless armed and `!arena.Cleared` and `Time.timeScale > 0`; clock is `Time.time` (scaled, like the storm: hitstop freezes hazards too). Owner = `arena.clearSpawner.Instance?.GetComponent<EnemyController>()`; partner likewise; `InPhase2 = owner/partner.InPhase2 || (boss != null && boss.Phase >= 1)`.
- Presentation is only `sharedMaterial` swaps between the three shared assets (accent / `M_RealmWarn` / `M_AlertTell`) on the marker renderers - no instances, no lights. Sounds: warn `Sfx.Tension` (0.6), cue `Sfx.Tick` (1.0), active `Sfx.Detonate` for Strike, `Sfx.Thunder` for Tide onset; the Sweep arm plays nothing (constant red is its sound: audio lane may add a `Drone` loop later).
- Public read-outs for tests/harness: `Armed`, `Phase`, `TicksFired`, `TicksLanded`, `LastSafeFraction`.

## 5. Geometry vs boss abilities - exclusion zones (all realm-local)

| realm | no SOLID inside | why | checked by |
|---|---|---|---|
| T1 | r < 16 | 6.5 m charge line + 14 m verdict line from anywhere | `RealmThemeTests` |
| T2 | r < 11 (pylons at 12, faces 11.0-13.0); arm tip <= 10.75 | banks want faces 6-12 m out; arm never meets a pylon | same + `SweepClearsProps` |
| T3 | r < 14 (posts at 14.3) | thrust 3.5-8, lash 8, whirl 4.3 | same |
| T4 | r < 13.5 (gibbets at 14) | storm ring 3.6 needs an exit; throw lands <= 6 m out; ceiling 20 >= 14 | same |
| Warden | r < 16 (sentinels at 16.5) | slam 6.5, thrust 7.4 | same |
| all | 1.5 m of every authored point (entry, enemy, partner, exit, pickups) and solids >= 2.0 m apart edge to edge (agent diameter 1 m) | NavMesh stays one island; nothing spawns inside a prop | same |

Sightlines: the only solids taller than 1 m inside r 13 anywhere are the T2 pylons (S2.2, playtest item). Nothing non-solid sits between y 0.5 and y 5 inside r 14 except hazard markers (0.05 thick floor decals; columns only while a beam/grate is warned or active, i.e. already the thing you must see).

## 6. Lanes (disjoint files)

**Step 0 - LEAD lands the data shape first** (15 lines, `LevelDefinition.cs` S3) and commits, so every worker compiles against it. Then three lanes in parallel.

| lane | files (exclusive) | do not touch | acceptance |
|---|---|---|---|
| **LEAD** | `LevelDefinitionAuthoring.cs` (the one `SetSolar` call line), `SolarArenaTests.cs` (already open), `LevelStudioValidator.cs` (optional rule), decisions: T1 vents on/off, parryable hazards (no), pylon width | everything below | runs generators S7; both suites; one commit per worker pass |
| **Worker A (Sonnet) - hazard runtime** | NEW `Assets/Scripts/Level/RealmHazard.cs`, NEW `Assets/Scripts/Level/RealmHazardMath.cs`, NEW `Assets/Editor/Tests/RealmHazardMathTests.cs`, NEW `Assets/Editor/Tests/RealmHazardGatingTests.cs`; add the four files to the two csproj (throwaway) | `PlayerCombat`, `EnemyController`, `BossArenaTrigger`, `SolarArenaPortal`, motor, any Editor builder | `dotnet build` both; tests: cue == 0.28, warn >= 1.2, tip speed < 11, no damage before arm delay / after Cleared / with null owner / while `IsCarried`; sector and sweep membership at the edges; `SafeFraction` for the five worst cases in S2 within 0.5 % |
| **Worker B (Sonnet) - authoring + builder + materials** | NEW `Assets/Editor/RealmThemeAuthoring.cs`, NEW `Assets/Editor/RealmDressingBuilder.cs`, `Assets/Editor/LevelDefinitionBuilder.cs` (the one call at `:698`), `Assets/Editor/MaterialFactory.cs` (two Spec rows), NEW `Assets/Editor/Tests/RealmThemeTests.cs`, `docs/DATAFLOW.md` (THE CELL block) | `LevelDefinitionAuthoring.cs`, `SolarArenaTests.cs`, anything under `Assets/Scripts/` | `dotnet build`; `RealmThemeTests`: every S5 row, every hazard footprint inside the floor, `Apply` twice == same JSON, non-solid props carry no collider and sit on SkyLayer, solid props layer 0 + static, no `Light` under any `*_Realm` root except `SolarLight` |
| **Worker C (after A+B) - surface** | brief `vfx-art-team` (marker/column readability against each wall colour, mote alpha) and `audio-engineer` (Tension/Tick/Detonate/Thunder trims for hazards, optional Drone loop on the arm); edits limited to the presentation constants inside `RealmHazard.cs` and `RealmThemeAuthoring.cs` once A and B are committed | everything else | `dotnet build`; no bloom change: `M_RealmWarn` 0.95 and `M_RealmBrandViolet` 0.90 under 1.05, `M_AlertTell` untouched |

## 7. Generators, in order (LEAD, editor open, play mode off)

1. `2. Create Materials` (the two new specs) -> 2. `8a. Rework Level_01` -> 3. `8. Build Level From Definition` -> 4. `Rebuild NavMesh` -> 5. `Health Check` -> 6. `Level Arc Report` (realm sections unchanged; no perch moved) -> 7. `Run Quick EditMode Tests`, then Full.
`Projectile Encounter Report`: **not required** (no sentry moves, no projectile speed changes).
What the report must NOT show: any realm object outside its floor, more than one Light per realm.

## 8. Risks

1. **`ReceiveAttack` null attacker** - it dereferences `a.attacker`; the hazard skips ticks with no live owner. Lead may harden `PlayerCombat.cs:99/122/157/187` later (core file, lead only).
2. **Non-solid props on layer 0 bake into the NavMesh** (RenderMeshes collection). SkyLayer for every non-solid; pinned by `RealmThemeTests`.
3. **T4 throw into a brand** - bounded at 2 ticks (16) and never while carried; if it feels cheap, the fix is `T` 3.0 -> 3.5 (data), not code.
4. **T2 pylon occlusion of a disc cue** - playtest item; fallback is pylons 1.4 -> 1.0 wide at r 13 (banks still 7-13 m).
5. **Dancer bank rate** may drop if probes miss the 1.4 m faces (confirm-raycast falls back to the fan); measure `OrbitDancerDiscs.LastBanked` in the harness before widening.
6. **Idempotency** - arrays must be rewritten in `Apply`, never appended; `SolarArenaTests.SolarMigrationIsIdempotent` and `LevelTraversalTests` catch it.
7. **Intro hold** - the 2.0 s arm delay is `IntroHoldSeconds + 0.4`; if the lead lengthens the hold, `RealmHazard.ArmDelay` must read the constant, not copy it.
8. **Scaled clock** - hazards freeze under hitstop like the storm; a hazard that "should" keep ticking during hitstop is a design change, not planned.
9. **Draw calls** - ~90 static boxes across five realms, all batched; 5 extra `AmbientMist` systems at 48 particles each; no new lights.

Model: Fable 5.1 (Claude Code)
