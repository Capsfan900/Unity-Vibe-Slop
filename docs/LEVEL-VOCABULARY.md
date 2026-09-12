# Level map vocabulary

Use these names when discussing the campaign's 3D space. A prompt should identify the zone first, then the
exact object when known: **“In T4 — Warden Descent, retime `Spawn_T4_Surge_2`.”** The familiar aliases are
valid shorthand, but the canonical name and shipped ID remove ambiguity.

## Level 1 zones

| Canonical zone | Longitudinal bounds | Common aliases | Route identity |
|---|---:|---|---|
| **T0 — Opening Descent** | z -162.3 to 8 | first ramp, opening ramp, starting descent, reliquary landing | The long downhill projectile-parry tutorial, ending at the paired Heavy Sentries. |
| **T1 — Stone Causeway** | z 8 to 145.625 | first parkour section, causeway, Ninja section | Stepping stones and water shortcut into the Thirteenth Shade split. |
| **T2 — Helix Tower** | z 145.625 to 267 | tower, wall-run tower, Knight section | The vertical wall-run/wall-jump circuit into the Iron Penitent split. |
| **T3 — Balloon Aqueduct** | z 267 to 393.15 | balloon section, water span, Spellsword section | Balloons, elevated water and the Ashen Chorister split. |
| **T4 — Warden Descent** | z 393.15 to 520 | last ramp, final ramp, Warden approach, boss approach | The final downhill Surge Turret sequence into the Hollow Warden split. |

The split name is the boss-clear timing segment: **Ninja**, **Knight**, **Spellsword**, then **Warden**.
“Zone” describes a physical stretch of the main course; “split” describes the scored interval that ends when
that zone's boss dies. Solar Realms are the isolated boss arenas reached from their matching zone.

## Enemy vocabulary

| Canonical enemy | Shipped key | Common aliases | Role |
|---|---|---|---|
| **Sentry** | `pshooter_enemy01` | blue squid, blue ghost, flare shooter | Floating projectile enemy; its successful defeat creates the optional faster/harder Challenge Route. |
| **Heavy Sentry** | `pshooter_enemy02` | heavy turret, reliquary turret, three-shot heavy | Stationary dark reliquary that fires a rapid three-shot phrase. The T0 pair are `Spawn_T0_Reliquary_1` and `_2`. |
| **Surge Turret** | `pshooter_enemy03` | ramp turret, rapid turret | Small fixed projectile turret used on the long downhill routes. T0 and T4 suffixes identify shot order. |

The parkour projectile lineage above is separate from the Souls melee lineage (`Legendary_*`, `Boss`, and
the Sandbox-only V18 test boss). Never use “blue squid” to mean a Heavy Sentry or Surge Turret.

## Object words

- **platform**: a named solid in `platforms[]`; use its authored ID such as `T2_L8`.
- **ramp**: a sloped traversal solid in `ramps[]`; “first ramp” means `T0_Ramp_Descent`, while “last ramp”
  means `T4_Ramp_Descent`.
- **parry route**: an ordered `projectileSequences[]` firing schedule. This is not an alternate path.
- **Challenge Route**: an optional faster/harder flare shortcut defined in `challengeRoutes[]`. Its
  invisible anchor exists only to preserve authoring and timing data.
- **arena gate / Solar Realm**: the main-course gate and isolated boss arena belonging to a split.
- **checkpoint**: the respawn anchor at the entrance of the next traversal zone.

## Dashboard data

The development dashboard parses this block and overlays it on the shipped level geometry. Keep it valid
JSON and update it whenever a zone boundary or canonical term changes.

<!-- dashboard-level-vocabulary -->
```json
{
  "levels": [
    {
      "file": "Assets/Data/Levels/Level_01_Level.asset",
      "zones": [
        {"id": "T0", "canonical": "Opening Descent", "zMin": -162.3, "zMax": 8, "aliases": ["first ramp", "opening ramp", "starting descent", "reliquary landing"], "prompt": "Opening downhill parry tutorial and paired Heavy Sentries."},
        {"id": "T1", "canonical": "Stone Causeway", "zMin": 8, "zMax": 145.625, "aliases": ["first parkour section", "causeway", "Ninja section"], "prompt": "Stepping stones, water shortcut and Thirteenth Shade split."},
        {"id": "T2", "canonical": "Helix Tower", "zMin": 145.625, "zMax": 267, "aliases": ["tower", "wall-run tower", "Knight section"], "prompt": "Vertical wall-run circuit and Iron Penitent split."},
        {"id": "T3", "canonical": "Balloon Aqueduct", "zMin": 267, "zMax": 393.15, "aliases": ["balloon section", "water span", "Spellsword section"], "prompt": "Balloon and elevated-water traversal into the Ashen Chorister split."},
        {"id": "T4", "canonical": "Warden Descent", "zMin": 393.15, "zMax": 520, "aliases": ["last ramp", "final ramp", "Warden approach", "boss approach"], "prompt": "Final Surge Turret descent into the Hollow Warden split."}
      ]
    }
  ],
  "enemyTerms": {
    "pshooter_enemy01": {"canonical": "Sentry", "aliases": ["blue squid", "blue ghost", "flare shooter"]},
    "pshooter_enemy02": {"canonical": "Heavy Sentry", "aliases": ["heavy turret", "reliquary turret", "three-shot heavy"]},
    "pshooter_enemy03": {"canonical": "Surge Turret", "aliases": ["ramp turret", "rapid turret"]},
    "Legendary_Ninja": {"canonical": "The Thirteenth Shade", "aliases": ["Ninja", "T1 sub-boss"]},
    "Legendary_Knight": {"canonical": "The Iron Penitent", "aliases": ["Knight", "T2 sub-boss"]},
    "Legendary_Spellsword": {"canonical": "The Ashen Chorister", "aliases": ["Spellsword", "T3 sub-boss"]},
    "Boss": {"canonical": "The Hollow Warden", "aliases": ["Warden", "main boss"]}
  }
}
```
