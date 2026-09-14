# Level map vocabulary

Use these names when discussing the campaign's 3D space. A prompt should identify the zone first, then the
exact object when known: **“In T4 — Warden Descent, retime `Spawn_T4_Surge_2`.”** The familiar aliases are
valid shorthand, but the canonical name and shipped ID remove ambiguity.

## Level 1 zones

| Canonical zone | Longitudinal bounds | Common aliases | Route identity |
|---|---:|---|---|
| **T0 — Opening Descent** | z -162.3 to 8 | first ramp, opening ramp, starting descent, reliquary landing | The long downhill projectile-parry tutorial, ending at the paired Heavy Sentries. |
| **T1 — Stone Causeway** | z 8 to 145.625 | first parkour section, causeway, Lancer section | Stepping stones and water shortcut into the Lancer split (the Seraph Lancer realm). |
| **T2 — Helix Tower** | z 145.625 to 267 | tower, wall-run tower, Judge section | The vertical wall-run/wall-jump circuit into the Judge split (the Cinder Judge realm). |
| **T3 — Balloon Aqueduct** | z 267 to 393.15 | balloon section, water span, Dancer section | Balloons, elevated water and the Dancer split (the Orbit Dancer realm). |
| **T4 — Warden Descent** | z 393.15 to 514.4 | last ramp, final ramp, Grappler section, grappler approach | The downhill Surge Turret sequence that flies you into the fourth mini-boss realm at its foot: the Grappler split. |
| **T5 — Warden Court** | z 514.5 to 600 | Warden approach, boss approach, Warden court, final deck | The return deck from the fourth realm, `Checkpoint_4`, and the Hollow Warden's sun: the Warden split. |

The split name is the boss-clear timing segment: **Lancer**, **Judge**, **Dancer**, **Grappler**, then
**Warden**. “Zone” describes a physical stretch of the main course; “split” describes the scored interval that
ends when that zone's boss dies. A zone carries exactly one split (the catalog and the Level Studio validator
both hold `zone.splitName == split.name` for the split's end spawner), which is why the fourth realm made T5 a
zone of its own rather than stretching T4. Solar Realms are the isolated boss arenas reached from their
matching zone; since 2026-09-13 every realm is the same cell — a 30 m floor under 24 m walls inside a 45 m
shell — and the five cells sit 100 m apart at x 700, z 0 / 100 / 200 / 300 / 400 in course order.

**Ids that keep an older zone prefix.** The Warden's records were minted in T4 (`T4.Arena.01`, `T4.Gate.01`,
`T4.BossPortal.01`, `T4.HollowWarden.01`, `T4.RunSplit.01`, `T4.Checkpoint.01`, `T4.Pickup.01`) and now sit in
T5. Ids are never renamed, so they keep the T4 prefix; `Spawn_Boss` and `Pickup_Boss_Hook` resolve to T5 by
`zoneIdOverride`, because the spawner's historical anchor (z 396) never moves.

**The fourth realm's objects (T4).** Gate `T4_Gate` (trigger `T4_ArenaTrigger`, exit `T4_Gate_Exit`), portal
sun at `(0, 22.3, 479.7)`, spawner `Spawn_Legendary_V18Grappler`, pickup `Pickup_T4_Grappler`, run-out deck
`T4_Grappler_Approach`, checkpoint `Checkpoint_T4_Grappler`. The numbered `Checkpoint_1..4` names stay
load-bearing (F5 warps to `Checkpoint_4`, still the Warden's approach), so the new checkpoint is named for its
place, not renumbered.

## Enemy vocabulary

| Canonical enemy | Shipped key | Common aliases | Role |
|---|---|---|---|
| **Sentry** | `pshooter_enemy01` | blue squid, blue ghost, flare shooter | Floating projectile enemy; its successful defeat creates the optional faster/harder Challenge Route. |
| **Heavy Sentry** | `pshooter_enemy02` | heavy turret, reliquary turret, three-shot heavy | Stationary dark reliquary that fires a rapid three-shot phrase. The T0 pair are `Spawn_T0_Reliquary_1` and `_2`. |
| **Surge Turret** | `pshooter_enemy03` | ramp turret, rapid turret | Small fixed projectile turret used on the long downhill routes. T0 and T4 suffixes identify shot order. |

| **The Grappler** | `Legendary_FlurryBrawlerV18` on `Spawn_Legendary_V18Grappler` | T4 mini-boss, fourth realm, V18 | Skyfall Suplex: grabs, flies up, throws. |
| **The Seraph Lancer** | `Legendary_SeraphLancer` on `Spawn_Legendary_Ninja` | T1 mini-boss | Sky Verdict: rises on light-wings, three parryable javelins. |
| **The Cinder Judge** | `Legendary_CinderJudge` on `Spawn_Legendary_Knight` | T2 mini-boss | Storm Judgement lightning tornado + Aegis of Judgement magic shield. |
| **The Orbit Dancer** | `Legendary_OrbitDancer` on `Spawn_Legendary_Spellsword` | T3 mini-boss | Orbit Storm: ricochet discs. |

Boss roster seated 2026-09-13 (`LevelDefinitionAuthoring.SeatBossRoster`): the spawner NAMES are historical
(Ninja/Knight/Spellsword) and stay the stable contract for splits, gates and clear logic; only the prefab keys
changed, and the three spawns received fresh family object IDs. The Thirteenth Shade, Iron Penitent and Ashen
Chorister remain as Sandbox fixtures.

The parkour projectile lineage above is separate from the Souls melee lineage (`Legendary_*`, `Boss`).
Never use “blue squid” to mean a Heavy Sentry or Surge Turret.

## Object words

- **platform**: a named solid in `platforms[]`; use its authored ID such as `T2_L8`.
- **ramp**: a sloped traversal solid in `ramps[]`; “first ramp” means `T0_Ramp_Descent`, while “last ramp”
  means `T4_Ramp_Descent`.
- **parry route**: an ordered `projectileSequences[]` firing schedule. This is not an alternate path.
- **Challenge Route**: an optional faster/harder flare shortcut defined in `challengeRoutes[]`. Its
  invisible anchor exists only to preserve authoring and timing data.
- **arena gate / Solar Realm**: the main-course gate and isolated boss arena belonging to a split. Five of
  them: `T1_Gate`, `T2_Gate`, `T3_Gate`, `T4_Gate`, `Boss_Gate`.
- **checkpoint**: the respawn anchor at the entrance of the next traversal zone (`Checkpoint_1..4`), plus
  `Checkpoint_T4_Grappler` on the descent's run-out before the fourth realm.
- **slide gate / lintel**: RETIRED 2026-09-13. `T1_Fallen_Obelisk` (`T1.Platform.10`) and `T3_Fallen_Lintel`
  (`T3.Platform.08`) were the bars across the causeway and the span; `LevelDefinitionAuthoring.RemovedSlideGates`
  removes them on every `8a` and nothing re-adds them. Do not use "slide gate" for anything in Level 1 now.

## Dashboard data

The development dashboard derives bounds, splits, placed IDs, types and data keys from the shipped
`LevelDefinition` asset. This small JSON block contains aliases only; it is not a second placed-object inventory.

<!-- dashboard-level-vocabulary -->
```json
{
  "levels": [
    {
      "file": "Assets/Data/Levels/Level_01_Level.asset",
      "zones": [
        {"id": "T0", "aliases": ["first ramp", "opening ramp", "starting descent", "reliquary landing"]},
        {"id": "T1", "aliases": ["first parkour section", "causeway", "Lancer section"]},
        {"id": "T2", "aliases": ["tower", "wall-run tower", "Judge section"]},
        {"id": "T3", "aliases": ["balloon section", "water span", "Dancer section"]},
        {"id": "T4", "aliases": ["last ramp", "final ramp", "Grappler section", "grappler approach"]},
        {"id": "T5", "aliases": ["Warden approach", "boss approach", "Warden court", "final deck"]}
      ]
    }
  ],
  "enemyTerms": {
    "pshooter_enemy01": {"canonical": "Sentry", "aliases": ["blue squid", "blue ghost", "flare shooter"]},
    "pshooter_enemy02": {"canonical": "Heavy Sentry", "aliases": ["heavy turret", "reliquary turret", "three-shot heavy"]},
    "pshooter_enemy03": {"canonical": "Surge Turret", "aliases": ["ramp turret", "rapid turret"]},
    "Legendary_Ninja": {"canonical": "The Thirteenth Shade", "aliases": ["Ninja"]},
    "Legendary_SeraphLancer": {"canonical": "The Seraph Lancer", "aliases": ["Lancer", "sky knight", "T1 sub-boss"]},
    "Legendary_Knight": {"canonical": "The Iron Penitent", "aliases": ["Knight"]},
    "Legendary_CinderJudge": {"canonical": "The Cinder Judge", "aliases": ["Judge", "T2 sub-boss"]},
    "Legendary_Spellsword": {"canonical": "The Ashen Chorister", "aliases": ["Spellsword"]},
    "Legendary_OrbitDancer": {"canonical": "The Orbit Dancer", "aliases": ["Dancer", "disc duelist", "T3 sub-boss"]},
    "Legendary_FlurryBrawlerV18": {"canonical": "The Grappler", "aliases": ["Grappler", "T4 mini-boss", "fourth realm", "V18"]},
    "Boss": {"canonical": "The Hollow Warden", "aliases": ["Warden", "main boss"]}
  }
}
```
