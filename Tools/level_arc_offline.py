"""Offline mirror of Assets/Editor/LevelArcAnalyzer.AnalyzeHop, the LevelTraversalAnalyzer
line-of-sight / water checks and the SolarArenaTests portal geometry, so a level change can be
measured without the Unity editor.

This is a VERIFICATION AID for the level designer, not a second source of truth: the editor's
LevelArcReport and the Level*Tests remain the authority. Every constant here is read off the
shipped Player.prefab, the shipped EnemyData/PlayerStats assets, the level asset and the authoring
source, never typed in twice.

Usage:  python Tools/level_arc_offline.py            # measures the SHIPPED asset as it is on disk
        python Tools/level_arc_offline.py --after     # applies the CURRENT authoring pass first (what 8a will write)
        python Tools/level_arc_offline.py --torches   # the torch light budget
        python Tools/level_arc_offline.py --sight     # HOW MANY MOVES AHEAD you can see, shipped vs authored

2026-09-13 REWRITE. The tool had drifted from the level: it still walked the pre-solar route
(T1_Stone_5, T1_Arena, T2_Entry, T2_Bridge, T3_Entry, T3_Step_3, T3_Arena), parsed a Ramps table
that had since become RampBetween(...) rows (so it saw 0 ramps), and applied the legacy Reshapes
table as its "after" pass - which the hybrid pass (OpenCourseDecks + HybridCourseStructures)
superseded. Now:
  * the BASELINE ROUTE and TECH LINES are parsed out of LevelArcReport.cs, the same lists the editor
    report and FeatureTests hold;
  * --after mirrors Apply: normalise the shipped boxes back to canonical section coordinates,
    reshape by OpenCourseDecks, remove-and-re-add HybridCourseStructures, drop RemovedSlideGates,
    re-add the Perches, write the SetPlatform/AddPlatform rows, then translate the sections back;
  * ramps, balloons, torches, arenas and spawns come from the asset (they are what is built);
  * the shooter band, the parry cone and the perch table are data, not literals;
  * two new sections - REALM LAYOUT and SOLAR APPROACHES - mirror SolarArenaTests: cell size and
    spacing, the far-plane rule, each sun's approach gap and its silhouette clearances.

RAMPS (2026-09-07). A ramp is the one piece in the level that is NOT an axis-aligned box, so it gets its
own oriented-box class here rather than being flattened to an AABB. Two departures from exactness, both
stated where they are made: the arc sweep treats the player capsule as axis-aligned IN RAMP-LOCAL SPACE
(an error of at most 1 - cos(15 deg) = 3.4% of the capsule's height on the steepest ramp authored), and an
arc that touches a ramp on the way down is counted as an ARRIVAL rather than a block, because a body that
lands on a ramp is on the route.
"""
import io
import math
import re
import sys
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSET = os.path.join(ROOT, "Assets/Data/Levels/Level_01_Level.asset")
PLAYER = os.path.join(ROOT, "Assets/Prefabs/Player.prefab")
AUTHORING = os.path.join(ROOT, "Assets/Editor/LevelDefinitionAuthoring.cs")
ARC_REPORT = os.path.join(ROOT, "Assets/Editor/LevelArcReport.cs")
SENTRY = os.path.join(ROOT, "Assets/Data/Enemies/parkour_enemies/pshooter_enemy01.asset")
STATS = os.path.join(ROOT, "Assets/Data/PlayerStats.asset")

# ----------------------------------------------------------------- the shipped move profile
def profile():
    t = open(PLAYER).read()
    def f(k):
        m = re.search(r"^  %s: ([-\d.eE]+)" % k, t, re.M)
        return float(m.group(1))
    p = dict(gravity=f("gravity"), jumpHeight=f("jumpHeight"), groundSpeed=f("groundSpeed"),
             airAccel=f("airAccel"), fallGravityMultiplier=f("fallGravityMultiplier"),
             airCarryDecay=f("airCarryDecay"), slideBoost=f("slideBoost"),
             slideMaxSpeed=f("slideMaxSpeed"), slideHeight=f("slideHeight"))
    m = re.search(r"^  jumpCutGravityMultiplier: ([-\d.]+)", t, re.M)
    p["jumpCut"] = float(m.group(1)) if m else 0.0
    # CharacterController on the same prefab
    p["radius"] = float(re.search(r"^  m_Radius: ([\d.]+)", t, re.M).group(1))
    p["height"] = float(re.search(r"^  m_Height: ([\d.]+)", t, re.M).group(1))
    p["skin"] = float(re.search(r"^  m_SkinWidth: ([\d.]+)", t, re.M).group(1))
    p["r"] = p["radius"] + p["skin"]
    p["takeoff"] = math.sqrt(2.0 * -p["gravity"] * p["jumpHeight"])
    p["slideJump"] = min(p["groundSpeed"] + p["slideBoost"], p["slideMaxSpeed"])
    return p

def _yaml_float(path, key, default):
    try:
        t = open(path).read()
    except IOError:
        return default
    m = re.search(r"^  %s: ([-\d.eE]+)" % key, t, re.M)
    return float(m.group(1)) if m else default

def shooter_band():
    """The shooter's own data, never a literal (LevelArcReport reads the same asset)."""
    return _yaml_float(SENTRY, "projectileMinRange", 6.0), _yaml_float(SENTRY, "projectileMaxRange", 30.0)

def facing_cone():
    return _yaml_float(STATS, "facingConeDeg", 75.0)

# ----------------------------------------------------------------- boxes
class Box(object):
    __slots__ = ("name", "mn", "mx")
    def __init__(self, name, c, s):
        self.name = name
        self.mn = (c[0] - s[0] / 2.0, c[1] - s[1] / 2.0, c[2] - s[2] / 2.0)
        self.mx = (c[0] + s[0] / 2.0, c[1] + s[1] / 2.0, c[2] + s[2] / 2.0)
    @property
    def top(self): return self.mx[1]
    @property
    def center(self): return tuple((self.mn[i] + self.mx[i]) / 2.0 for i in range(3))
    @property
    def size(self): return tuple(self.mx[i] - self.mn[i] for i in range(3))
    def shifted(self, dz):
        c = self.center
        return Box(self.name, (c[0], c[1], c[2] + dz), self.size)

V = r"\{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}"

def _section(t, name):
    """The YAML block of one top-level array of the level asset."""
    body = t.split("\n  %s:" % name, 1)
    if len(body) < 2: return ""
    body = body[1]
    m = re.search(r"\n  [A-Za-z]+:", body)
    return body[:m.start()] if m else body

def parse_boxes(t):
    body = t.split("platforms:")[1].split("\n  spawns:")[0]
    out = []
    for item in re.split(r"^  - ", body, flags=re.M)[1:]:
        name = re.search(r"^\s*name: (\S+)", item, re.M)
        center = re.search(r"^\s*center: %s" % V, item, re.M)
        size = re.search(r"^\s*size: %s" % V, item, re.M)
        if name and center and size:
            out.append(Box(name.group(1), tuple(float(x) for x in center.groups()), tuple(float(x) for x in size.groups())))
    return out

def load_boxes():
    return parse_boxes(open(ASSET).read())

def load_spawns():
    t = open(ASSET).read()
    body = _section(t, "spawns")
    out = {}
    for m in re.finditer(r"  - meta:[\s\S]*?name: (\S+)[\s\S]*?position: %s" % V, body):
        out[m.group(1)] = tuple(float(x) for x in m.groups()[1:4])
    return out

def load_balloons():
    t = open(ASSET).read()
    body = _section(t, "balloons")
    out = []
    for m in re.finditer(r"  - meta:[\s\S]*?name: (\S+)\s*\n\s+position: %s[\s\S]*?radius: ([\d.]+)" % V, body):
        out.append((m.group(1), tuple(float(x) for x in m.groups()[1:4]), float(m.group(5))))
    return out

def load_torches():
    t = open(ASSET).read()
    body = _section(t, "torches")
    out = []
    for m in re.finditer(r"  - meta:[\s\S]*?name: (\S+)\s*\n\s+basePosition: %s" % V, body):
        out.append((m.group(1), tuple(float(x) for x in m.groups()[1:4])))
    return out

def load_checkpoints():
    t = open(ASSET).read()
    body = _section(t, "checkpoints")
    out = {}
    for m in re.finditer(r"  - meta:[\s\S]*?name: (\S+)\s*\n\s+position: %s" % V, body):
        out[m.group(1)] = tuple(float(x) for x in m.groups()[1:4])
    return out

# ----------------------------------------------------------------- the authoring source, READ, never retyped
# These are NOT retyped here. The one time they were, a rail centre was transcribed 0.2 m off and the
# error was invisible to this tool because the tool agreed with the typo. The tables are parsed straight
# out of Assets/Editor/LevelDefinitionAuthoring.cs, so the thing measured is the thing that will be built.
CSV3 = r"new Vector3\(([-\d.f]+), ([-\d.f]+), ([-\d.f]+)\)"

def _f(s): return float(s.rstrip("f"))

def _src():
    return io.open(AUTHORING, encoding="utf-8").read()

def _table(src, marker, pattern):
    body = src.split(marker)[1]
    body = body.split("};")[0]
    return re.findall(pattern, body)

def _consts(src):
    """Every `const float X = 1f` / `const string X = "..."` in the authoring class."""
    out = {}
    for m in re.finditer(r"const float ([\w, =\d.f-]+);", src):
        for part in m.group(1).split(","):
            k, v = part.split("=")
            out[k.strip()] = _f(v.strip())
    for m in re.finditer(r'const string (\w+) = "([^"]+)";', src):
        out[m.group(1)] = m.group(2)
    return out

def _named(consts, token):
    """A literal "T4_Gate" or a const name like GrapplerGate, resolved."""
    return token.strip('"') if token.startswith('"') else consts[token]

def load_open_decks():
    rows = _table(_src(), "public static readonly Reshape[] OpenCourseDecks",
                  r'new Reshape\("(\w+)",\s*' + CSV3 + r',\s*' + CSV3)
    return [(r[0], tuple(_f(x) for x in r[1:4]), tuple(_f(x) for x in r[4:7])) for r in rows]

def load_hybrid():
    rows = _table(_src(), "public static readonly HybridStructure[] HybridCourseStructures",
                  r'new HybridStructure\("(\w+)",\s*' + CSV3 + r',\s*' + CSV3)
    return [(r[0], tuple(_f(x) for x in r[1:4]), tuple(_f(x) for x in r[4:7])) for r in rows]

def load_removed_gates():
    m = re.search(r"RemovedSlideGates = \{([^}]*)\}", _src())
    return re.findall(r'"(\w+)"', m.group(1)) if m else []

def load_perches():
    src = _src()
    rows = _table(src, "public static readonly Perch[] Perches",
                  r'new Perch\("(\w+)", "(\w+)",\s*' + CSV3 + r',\s*([-\d.f]+),\s*"([\w,]+)"\)')
    m = re.search(r"PerchSize = " + CSV3, src)
    size = tuple(_f(x) for x in m.groups())
    return [(r[0], r[1], tuple(_f(x) for x in r[2:5]), _f(r[5]), r[6].split(",")) for r in rows], size

def load_beacons():
    rows = _table(_src(), "public static readonly TorchDef[] OpenRouteBeacons",
                  r'name = "(\w+)", basePosition = ' + CSV3)
    return [(r[0], tuple(_f(x) for x in r[1:4])) for r in rows]

def load_waters():
    rows = _table(_src(), "public static readonly Water[] Waters",
                  r'new Water\("(\w+)",\s*' + CSV3 + r',\s*' + CSV3)
    return dict((r[0], (tuple(_f(x) for x in r[1:4]), tuple(_f(x) for x in r[4:7]))) for r in rows)

def load_translation():
    m = re.search(r"TranslateCourseSections\(def, ([-\d.f]+), ([-\d.f]+), ([-\d.f]+), false\)", _src())
    return _f(m.group(1)), _f(m.group(2)), _f(m.group(3))

def load_set_platforms(consts):
    """The SetPlatform / AddPlatform rows of ApplySolarSpacing, in CANONICAL coordinates."""
    out = []
    pat = (r'(?:SetPlatform|AddPlatform)\(platforms, (\w+|"\w+"), new Vector3\(([-\d.f]+), ([-\d.f]+), '
           r'([-\d.f]+)( \+ WardenShift)?\), ' + CSV3 + r'\)')
    for m in re.finditer(pat, _src()):
        z = _f(m.group(4)) + (consts["WardenShift"] if m.group(5) else 0.0)
        out.append((_named(consts, m.group(1)), (_f(m.group(2)), _f(m.group(3)), z),
                    tuple(_f(x) for x in m.groups()[5:8])))
    return out

def load_route():
    """LevelArcReport.BaseRoute / TechRoute, the lists the editor report and FeatureTests hold."""
    src = io.open(ARC_REPORT, encoding="utf-8").read()
    base = _table(src, "public static readonly Route[] BaseRoute", r'new Route\("(\w+)", "(\w+)", "(\w+)"\)')
    tech = _table(src, "public static readonly Route[] TechRoute", r'new Route\("(\w+)", "(\w+)", "(\w+)"\)')
    return [(b[1], b[2]) for b in base], [(t[1], t[2], t[0]) for t in tech]

# ----------------------------------------------------------------- the solar realms
# Mirror of SolarArenaTests: the exterior sun, the physical/visual radii, the cell, and the approach
# deck each gate is launched from (IsApproachPlatform). Shipped values come from the asset; --after
# values come from the SetSolar rows plus EnsureGrapplerArena / SetGateZ in the authoring source.
APPROACH_DECK = {"T1_Gate": "T1_Causeway", "T2_Gate": "T2_L11", "T3_Gate": "T3_Step_2",
                 "T4_Gate": "T4_Grappler_Approach", "Boss_Gate": "Boss_Approach"}
# SolarArenaTests.IsApproachPlatform / IsApproachTorch: the pieces allowed within 1.5 m of a sun's
# visible surface because they ARE its approach. Everything else keeps 12 m so nothing silhouettes
# inside the plasma.
APPROACH_PIECES = {
    "T1_Gate": ("T1_Causeway", "T1_Rail_L", "T1_Rail_R", "T1_Wall_Causeway", "T1_Wall_Landing", "T1_Perch_E",
                "Torch_T1_Causeway_N", "Torch_Beacon_T1_Causeway_2"),
    "T2_Gate": ("T2_L11", "Torch_T2_Top", "Torch_Beacon_T2_L11"),
    "T3_Gate": ("T3_Step_1", "T3_Step_2"),
    "T4_Gate": ("T4_Grappler_Approach",),
    "Boss_Gate": ("Boss_Approach",),
}
CARRY_GATES = ("T1_Gate", "T2_Gate", "T3_Gate")   # projectile-earned carry: 13.5-15.5 m
PLAYER_CAMERA_FAR_CLIP = 300.0

def torch_boxes(torches):
    """A torch as the test sees it: a 0.3 x 1.9 x 0.3 post standing on its base."""
    return [Box(name, (pos[0], pos[1] + 0.95, pos[2]), (0.3, 1.9, 0.3)) for name, pos in torches]

class Realm(object):
    def __init__(self, gate):
        self.gate = gate
        self.theme = ""
        self.sun = (0.0, 0.0, 0.0); self.radius = 0.0; self.visual = 0.0
        self.cell = (0.0, 0.0, 0.0); self.floor = 20.0; self.shell = 30.0; self.wall = 12.0; self.ceiling = 12.0
        self.entryZ = 0.0; self.exitZ = None; self.triggerZ = 0.0
        self.gateY = 18.0; self.exitY = 18.0; self.gateSize = (9.0, 4.0, 0.5)
        self.entry = None; self.enemy = None; self.exit = None; self.pickup = None; self.has_return = True
        self.retry = None; self.ret = None

def load_realms_asset():
    t = open(ASSET).read()
    body = _section(t, "arenas")
    out = []
    for item in re.split(r"^  - meta:", body, flags=re.M)[1:]:
        def vec(key, block=item):
            m = re.search(r"^\s*%s: %s" % (key, V), block, re.M)
            return tuple(float(x) for x in m.groups()) if m else None
        def num(key, default, block=item):
            m = re.search(r"^\s*%s: ([-\d.eE]+)" % key, block, re.M)
            return float(m.group(1)) if m else default
        r = Realm(re.search(r"^\s*gateName: (\S+)", item, re.M).group(1))
        r.gateSize = vec("gateSize") or r.gateSize
        gc = vec("gateClosedPosition"); r.entryZ, r.gateY = gc[2], gc[1]
        r.triggerZ = vec("triggerPosition")[2]
        if int(num("hasExitGate", 0)):
            ec = vec("exitGateClosedPosition"); r.exitZ, r.exitY = ec[2], ec[1]
        solar = item.split("solarRealm:")[1]
        r.theme = re.search(r"themeMaterialKey: (\S+)", solar).group(1)
        r.sun = vec("exteriorCenter", solar); r.radius = num("exteriorRadius", 12.0, solar)
        r.visual = num("visualRadius", 0.0, solar) or r.radius
        r.cell = vec("realmCenter", solar); r.floor = num("realmFloorRadius", 20.0, solar)
        r.shell = num("realmShellRadius", 30.0, solar)
        r.wall = num("realmWallHeight", 12.0, solar); r.ceiling = num("realmCeilingHeight", 12.0, solar)
        r.entry = vec("playerEntryPosition", solar); r.enemy = vec("enemySpawnPosition", solar)
        r.pickup = vec("arenaPickupPosition", solar); r.exit = vec("realmExitPosition", solar)
        r.has_return = bool(int(num("hasReturn", 1, solar)))
        r.retry = vec("retryPosition", solar); r.ret = vec("returnPosition", solar)
        out.append(r)
    return out

def load_realms_authoring():
    src = _src(); consts = _consts(src)
    shift = consts.get("WardenShift", 0.0)
    shipped = dict((r.gate, r) for r in load_realms_asset())
    pat = (r'SetSolar\(def, (\w+|"\w+"), "(\w+)", new Vector3\(([-\d.f]+), ([-\d.f]+), ([-\d.f]+)( \+ WardenShift)?\), '
           r'([-\d.f]+), ([-\d.f]+),\s*new Vector3\(([-\d.f]+), ([-\d.f]+), ([-\d.f]+) \* RealmSpacing\), (\w+|"\w+"),'
           r'\s*new Vector3\(([-\d.f]+), ([-\d.f]+), ([-\d.f]+)( \+ WardenShift)?\), '
           r'(?:Vector3\.zero|new Vector3\(([-\d.f]+), ([-\d.f]+), ([-\d.f]+)\)), (true|false)\)')
    out = []
    for m in re.finditer(pat, src):
        gate = _named(consts, m.group(1))
        r = Realm(gate)
        r.theme = m.group(2)
        r.sun = (_f(m.group(3)), _f(m.group(4)), _f(m.group(5)) + (shift if m.group(6) else 0.0))
        r.radius, r.visual = _f(m.group(7)), _f(m.group(8))
        r.cell = (_f(m.group(9)), _f(m.group(10)), _f(m.group(11)) * consts["RealmSpacing"])
        r.floor, r.shell = consts["RealmFloorRadius"], consts["RealmShellRadius"]
        r.wall, r.ceiling = consts["RealmWallHeight"], consts["RealmCeilingHeight"]
        r.retry = (_f(m.group(13)), _f(m.group(14)), _f(m.group(15)) + (shift if m.group(16) else 0.0))
        r.ret = None if m.group(17) is None else (_f(m.group(17)), _f(m.group(18)), _f(m.group(19)))
        r.has_return = m.group(20) == "true"
        c = r.cell
        r.entry = (c[0], c[1] + 1.2, c[2] + consts["RealmEntryZ"])
        r.enemy = (c[0], c[1] + 0.1, c[2] + consts["RealmEnemyZ"])
        r.exit = (c[0], c[1] + 1.5, c[2] + consts["RealmExitZ"])
        r.pickup = (c[0] + consts["RealmPickupX"], c[1] + 1.2, c[2] - 3.0)
        # Gate rows: the historical gates through SetGateZ, the grappler's through EnsureGrapplerArena.
        g = re.search(r'arena.gateName == "%s"\) SetGateZ\(arena, ([-\d.f]+)( \+ WardenShift)?, ([-\d.f]+), ([-\d.f]+)( \+ WardenShift)?\)' % gate, src)
        if g:
            r.entryZ = _f(g.group(1)) + (shift if g.group(2) else 0.0)
            r.exitZ = _f(g.group(3)) if _f(g.group(3)) > 0 else None
            r.triggerZ = _f(g.group(4)) + (shift if g.group(5) else 0.0)
            if gate in shipped:
                r.gateY, r.exitY, r.gateSize = shipped[gate].gateY, shipped[gate].exitY, shipped[gate].gateSize
        else:
            ensure = src.split("static void EnsureGrapplerArena")[1].split("var spawns")[0]
            gc = re.search(r"arena\.gateClosedPosition = " + CSV3, ensure).groups()
            ec = re.search(r"arena\.exitGateClosedPosition = " + CSV3, ensure).groups()
            tp = re.search(r"arena\.triggerPosition = " + CSV3, ensure).groups()
            r.entryZ, r.gateY = _f(gc[2]), _f(gc[1])
            r.exitZ, r.exitY = _f(ec[2]), _f(ec[1])
            r.triggerZ = _f(tp[2])
        out.append(r)
    return out

# ----------------------------------------------------------------- ramps, from the asset
def _mat(yaw, angle):
    """Unity's Quaternion.Euler(-angle, yaw, 0) as a 3x3, i.e. Ry(yaw) * Rx(-angle)."""
    cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    ca, sa = math.cos(math.radians(-angle)), math.sin(math.radians(-angle))
    Ry = ((cy, 0.0, sy), (0.0, 1.0, 0.0), (-sy, 0.0, cy))
    Rx = ((1.0, 0.0, 0.0), (0.0, ca, -sa), (0.0, sa, ca))
    return tuple(tuple(sum(Ry[i][k] * Rx[k][j] for k in range(3)) for j in range(3)) for i in range(3))

def _mul(M, v):  return tuple(sum(M[i][j] * v[j] for j in range(3)) for i in range(3))
def _mulT(M, v): return tuple(sum(M[j][i] * v[j] for j in range(3)) for i in range(3))

class RampBox(object):
    """Mirror of RampDef: base is the centre of the LOW edge on the WALKABLE face, the angle is
    DERIVED from rise over run, and the slab hangs `thickness` DOWN from that face."""
    __slots__ = ("name", "base", "w", "run", "rise", "yaw", "th", "angle", "slope",
                 "R", "center", "half", "topPos", "mn", "mx")
    def __init__(self, name, base, width, run, rise, yaw, thickness=0.5):
        self.name, self.base, self.w, self.run, self.rise, self.yaw, self.th = \
            name, base, width, run, rise, yaw, thickness
        self.angle = math.degrees(math.atan2(rise, max(1e-4, run)))
        self.slope = math.hypot(run, rise)
        self.R = _mat(yaw, self.angle)
        f, u = _mul(self.R, (0.0, 0.0, 1.0)), _mul(self.R, (0.0, 1.0, 0.0))
        self.center = tuple(base[i] + f[i] * self.slope / 2.0 - u[i] * thickness / 2.0 for i in range(3))
        self.half = (width / 2.0, thickness / 2.0, self.slope / 2.0)
        h = (math.sin(math.radians(yaw)) * run, rise, math.cos(math.radians(yaw)) * run)
        self.topPos = tuple(base[i] + h[i] for i in range(3))
        cs = []
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    v = _mul(self.R, (sx * self.half[0], sy * self.half[1], sz * self.half[2]))
                    cs.append(tuple(self.center[i] + v[i] for i in range(3)))
        self.mn = tuple(min(c[i] for c in cs) for i in range(3))
        self.mx = tuple(max(c[i] for c in cs) for i in range(3))
    def local(self, p):
        return _mulT(self.R, (p[0] - self.center[0], p[1] - self.center[1], p[2] - self.center[2]))
    def seg_hits(self, a, b):
        """Exact slab test in ramp-local space."""
        la, lb = self.local(a), self.local(b)
        t0, t1 = 0.0, 1.0
        for ax in range(3):
            o, dd, lo, hi = la[ax], lb[ax] - la[ax], -self.half[ax], self.half[ax]
            if abs(dd) < 1e-9:
                if o < lo or o > hi: return False
                continue
            ta, tb = (lo - o) / dd, (hi - o) / dd
            if ta > tb: ta, tb = tb, ta
            t0, t1 = max(t0, ta), min(t1, tb)
            if t0 > t1: return False
        return True
    def capsule_distance(self, feet, p):
        """Distance from the player capsule to the slab, MEASURED IN RAMP-LOCAL SPACE with the capsule
        treated as axis-aligned there. Exact for a yaw-only ramp, and off by at most 1 - cos(angle) of
        the capsule height on a pitched one."""
        l = self.local(feet)
        y0, y1 = l[1] + p["r"], l[1] + p["height"] - p["r"]
        dx = max(-self.half[0] - l[0], l[0] - self.half[0], 0.0)
        dz = max(-self.half[2] - l[2], l[2] - self.half[2], 0.0)
        dy = max(-self.half[1] - y1, y0 - self.half[1], 0.0)
        return math.sqrt(dx * dx + dy * dy + dz * dz) - p["r"]
    def overlaps(self, b, pad=0.0):
        """Does the slab intersect an axis-aligned Box? AABB reject, then a lattice of interior points."""
        if not all(self.mn[i] < b.mx[i] + pad and self.mx[i] > b.mn[i] - pad for i in range(3)):
            return False
        for i in range(22):
            for j in range(6):
                for k in range(4):
                    v = _mul(self.R, (-self.half[0] + 2 * self.half[0] * k / 3.0,
                                      -self.half[1] + 2 * self.half[1] * j / 5.0,
                                      -self.half[2] + 2 * self.half[2] * i / 21.0))
                    q = (self.center[0] + v[0], self.center[1] + v[1], self.center[2] + v[2])
                    if all(b.mn[m] - pad <= q[m] <= b.mx[m] + pad for m in range(3)): return True
        return False

def load_ramps():
    """Every shipped ramp, out of the asset - the built geometry, whichever pass authored it."""
    t = open(ASSET).read()
    body = _section(t, "ramps")
    out = []
    pat = (r"  - meta:[\s\S]*?name: (\S+)\s*\n\s+basePosition: %s\s*\n\s+width: ([-\d.]+)\s*\n\s+run: ([-\d.]+)"
           r"\s*\n\s+rise: ([-\d.]+)\s*\n\s+thickness: ([-\d.]+)\s*\n\s+yaw: ([-\d.]+)") % V
    for m in re.finditer(pat, body):
        out.append(RampBox(m.group(1), tuple(float(x) for x in m.groups()[1:4]), float(m.group(5)),
                           float(m.group(6)), float(m.group(7)), float(m.group(9)), float(m.group(8))))
    return out

# ----------------------------------------------------------------- the authoring pass, mirrored
def section_of(name):
    """Which rigid section a box belongs to under TranslateCourseSections, or None."""
    if name.startswith("T2_"): return 2
    if name.startswith("T3_"): return 3
    if name.startswith("T4_") or name.startswith("Boss_"): return 4
    return None

def apply_authoring(boxes):
    """Mirror of Apply's geometry: normalise the shipped boxes to canonical section coordinates,
    reshape by OpenCourseDecks, remove-and-re-add HybridCourseStructures, drop RemovedSlideGates,
    re-add the perches, write the SetPlatform/AddPlatform rows, then translate the sections back
    by the deltas ApplySolarRealms passes to TranslateCourseSections."""
    consts = _consts(_src())
    t2, t3, t4 = load_translation()
    decks = dict((n, (c, s)) for n, c, s in load_open_decks())
    rows = dict((n, (c, s)) for n, c, s in load_set_platforms(consts))
    by = dict((b.name, b) for b in boxes)
    # NormalizeSolarCourseSpacing derives the shift each section currently carries from one anchor.
    def offset(name, canonical_z):
        return by[name].center[2] - canonical_z if name in by else 0.0
    shift = {2: offset("T2_L1", decks["T2_L1"][0][2]), 3: offset("T3_Pillar_1", decks["T3_Pillar_1"][0][2]),
             4: offset("T4_Entry", rows["T4_Entry"][0][2])}
    out = []
    for b in boxes:
        s = section_of(b.name)
        out.append(b.shifted(-shift[s]) if s else b)
    idx = dict((b.name, i) for i, b in enumerate(out))
    for n, (c, s) in decks.items():
        if n in idx: out[idx[n]] = Box(n, c, s)
    hybrid = load_hybrid()
    names = set(n for n, _, _ in hybrid) | set(load_removed_gates()) | set(rows.keys())
    perches, psize = load_perches()
    names |= set(n for n, _, _, _, _ in perches)
    out = [b for b in out if b.name not in names]
    out.extend(Box(n, c, s) for n, c, s in hybrid)
    out.extend(Box(n, c, psize) for n, _, c, _, _ in perches)
    out.extend(Box(n, c, s) for n, (c, s) in rows.items())
    delta = {2: t2, 3: t3, 4: t4}
    final = []
    for b in out:
        s = section_of(b.name)
        final.append(b.shifted(delta[s]) if s else b)
    return final

def apply_beacons(torches):
    """Mirror of Apply: drop every Torch_Beacon_ and re-add the authored list (canonical coordinates)."""
    t2, t3, t4 = load_translation()
    out = [t for t in torches if not t[0].startswith("Torch_Beacon_")]
    for name, pos in load_beacons():
        s = 2 if "_T2_" in name else 3 if "_T3_" in name else 4 if ("_T4_" in name or "_Boss_" in name) else None
        dz = {2: t2, 3: t3, 4: t4}.get(s, 0.0)
        out.append((name, (pos[0], pos[1], pos[2] + dz)))
    return out

# ----------------------------------------------------------------- geometry
def seg_box_distance(x, z, y0, y1, b):
    dx = max(b.mn[0] - x, x - b.mx[0], 0.0)
    dz = max(b.mn[2] - z, z - b.mx[2], 0.0)
    dy = max(b.mn[1] - y1, y0 - b.mx[1], 0.0)
    return math.sqrt(dx * dx + dy * dy + dz * dz)

def point_box_distance(pt, b):
    return seg_box_distance(pt[0], pt[2], pt[1], pt[1], b)

def clearance(feet, p, b):
    return seg_box_distance(feet[0], feet[2], feet[1] + p["r"], feet[1] + p["height"] - p["r"], b) - p["r"]

def stand_free(feet, p, boxes, ignore):
    for i, b in enumerate(boxes):
        if i == ignore: continue
        if clearance(feet, p, b) < 0.0: return False
    return True

def gap_and_rise(a, b):
    dx = max(a.mn[0] - b.mx[0], b.mn[0] - a.mx[0], 0.0)
    dz = max(a.mn[2] - b.mx[2], b.mn[2] - a.mx[2], 0.0)
    return math.sqrt(dx * dx + dz * dz), b.mx[1] - a.mx[1]

DT = 0.002

def sweep_arc(feet, v, hold, control, p, bounds, ignore, clear_ignore, max_t, floor_y, ramps=(), blockers=()):
    """Faithful to LevelArcAnalyzer.SweepArc; `bounds` is a list of (minx,miny,minz,maxx,maxy,maxz).

    The only departure is a cheap early-out: since d = sqrt(dx^2+dy^2+dz^2) >= max(dx,dy,dz), a box
    with any separation component greater than the swept radius cannot be touching, so it is skipped
    before the square root. That changes speed, never the answer."""
    r, h = p["r"], p["height"]
    grav, jumpcut, fallmul = p["gravity"], p["jumpCut"], p["fallGravityMultiplier"]
    gs, aa, k = p["groundSpeed"], p["airAccel"], p["airCarryDecay"]
    decay = math.exp(-k * DT) if k > 0 else 1.0
    wx = wz = 0.0
    if control:
        m = math.hypot(v[0], v[2])
        if m > 0.01:
            s = -1.0 if control == "brake" else 1.0
            wx, wz = v[0] / m * s, v[2] / m * s
    vx, vy, vz = v
    fx, fy, fz = feet
    t = 0.0
    min_clear = 1e9
    n = len(bounds)
    while t < max_t:
        if vy > 0.0:
            g = grav if hold else grav + grav * jumpcut
        elif vy < 0.0 and fallmul > 0.0:
            g = grav * fallmul
        else:
            g = grav
        vy += g * DT
        if wx or wz:
            add = gs - (vx * wx + vz * wz)
            if add > 0.0:
                a = aa * DT
                if a > add: a = add
                vx += wx * a; vz += wz * a
        if k > 0.0:
            sp = math.hypot(vx, vz)
            if sp > gs and sp > 1e-5:
                f = (gs + (sp - gs) * decay) / sp
                vx *= f; vz *= f
        prev_y = fy
        fx += vx * DT; fy += vy * DT; fz += vz * DT
        t += DT
        y0 = fy + r; y1 = fy + h - r
        # A ramp is route geometry, not an obstacle: touching one on the way down is an ARRIVAL (you are
        # standing on the slope and you run up it), touching one on the way up is a block like any box.
        for pass_i in (0, 1):
            for rm in (ramps if pass_i == 0 else blockers):
                if fx < rm.mn[0] - r or fx > rm.mx[0] + r: continue
                if fz < rm.mn[2] - r or fz > rm.mx[2] + r: continue
                if fy + h < rm.mn[1] - r or fy > rm.mx[1] + r: continue
                d = rm.capsule_distance((fx, fy, fz), p)
                if d < 0.0:
                    if pass_i == 0 and vy <= 0.0: return ("ramp", -1, min_clear)
                    return ("block", -1, min_clear)
                if d < min_clear: min_clear = d
        for i in range(n):
            if i == ignore: continue
            b = bounds[i]
            dx = b[0] - fx
            if dx < 0.0:
                dx = fx - b[3]
                if dx < 0.0: dx = 0.0
            if dx > r and i == clear_ignore: continue
            dz = b[2] - fz
            if dz < 0.0:
                dz = fz - b[5]
                if dz < 0.0: dz = 0.0
            dy = b[1] - y1
            if dy < 0.0:
                dy = y0 - b[4]
                if dy < 0.0: dy = 0.0
            if dx > r or dy > r or dz > r:
                if i != clear_ignore and dx < 3.0 and dy < 3.0 and dz < 3.0:
                    d = math.sqrt(dx * dx + dy * dy + dz * dz) - r
                    if d < min_clear: min_clear = d
                continue
            d = math.sqrt(dx * dx + dy * dy + dz * dz) - r
            if d < 0.0:
                if vy <= 0.0 and prev_y >= b[4] - 0.03:
                    return ("land", i, min_clear)
                if i != clear_ignore and d < min_clear: min_clear = d
                return ("block", i, min_clear)
            if i != clear_ignore and d < min_clear: min_clear = d
        if fy < floor_y: break
    return ("none", -1, min_clear)


def launch_band(a, b, inset, n):
    Depth, Spread = 6.0, 3.0
    x0, x1 = a.mn[0] + inset, a.mx[0] - inset
    z0, z1 = a.mn[2] + inset, a.mx[2] - inset
    if b.mn[0] >= a.mx[0]: x0 = max(x0, a.mx[0] - Depth)
    elif b.mx[0] <= a.mn[0]: x1 = min(x1, a.mn[0] + Depth)
    else:
        x0 = max(x0, b.mn[0] - Spread); x1 = min(x1, b.mx[0] + Spread)
    if b.mn[2] >= a.mx[2]: z0 = max(z0, a.mx[2] - Depth)
    elif b.mx[2] <= a.mn[2]: z1 = min(z1, a.mn[2] + Depth)
    else:
        z0 = max(z0, b.mn[2] - Spread); z1 = min(z1, b.mx[2] + Spread)
    if x1 < x0: x0 = x1 = min(max(b.center[0], a.mn[0] + inset), a.mx[0] - inset)
    if z1 < z0: z0 = z1 = min(max(b.center[2], a.mn[2] + inset), a.mx[2] - inset)
    pts = []
    for i in range(n):
        fx = 0.5 if n == 1 else i / float(n - 1)
        for j in range(n):
            fz = 0.5 if n == 1 else j / float(n - 1)
            pts.append((x0 + (x1 - x0) * fx, a.mx[1], z0 + (z1 - z0) * fz))
    return pts

def grid_on_top(b, inset, n):
    x0, x1 = b.mn[0] + inset, b.mx[0] - inset
    z0, z1 = b.mn[2] + inset, b.mx[2] - inset
    if x1 < x0: x0 = x1 = b.center[0]
    if z1 < z0: z0 = z1 = b.center[2]
    pts = []
    for i in range(n):
        fx = 0.5 if n == 1 else i / float(n - 1)
        for j in range(n):
            fz = 0.5 if n == 1 else j / float(n - 1)
            pts.append((x0 + (x1 - x0) * fx, b.mx[1], z0 + (z1 - z0) * fz))
    return pts

def near(boxes, lo, hi):
    return [b for b in boxes
            if b.mn[0] <= hi[0] and b.mx[0] >= lo[0] and b.mn[1] <= hi[1] and b.mx[1] >= lo[1]
            and b.mn[2] <= hi[2] and b.mx[2] >= lo[2]]

def index_of(boxes, name):
    for i, b in enumerate(boxes):
        if b.name == name: return i
    return -1

def analyze_hop(boxes, a_name, b_name, p, max_speed, floor_y, controls=("none",), ramps=()):
    ia0, ib0 = index_of(boxes, a_name), index_of(boxes, b_name)
    if ia0 < 0 or ib0 < 0: return None
    a, b = boxes[ia0], boxes[ib0]
    gap, rise = gap_and_rise(a, b)
    lo = tuple(min(a.mn[i], b.mn[i]) - (12, 8, 12)[i] for i in range(3))
    hi = tuple(max(a.mx[i], b.mx[i]) + (12, 22, 12)[i] for i in range(3))
    nb = near(boxes, lo, hi)
    ia, ib = index_of(nb, a_name), index_of(nb, b_name)
    bounds = [(x.mn[0], x.mn[1], x.mn[2], x.mx[0], x.mx[1], x.mx[2]) for x in nb]
    inset = p["r"] + 0.05
    launch = launch_band(a, b, inset, 5)
    aim = grid_on_top(b, inset, 3)
    speeds = [max_speed * f for f in (0.45, 0.65, 0.85, 1.0)]
    # Only the ramps in this hop's airspace matter, and only the one that SERVES the hop counts as an
    # arrival. A ramp whose top lands on B is route geometry for this hop: touching it on the way down is
    # arriving. Any other ramp nearby is just a slab, and blocks.
    def _on(b, pt): return (b.mn[0] - 0.01 <= pt[0] <= b.mx[0] + 0.01 and
                            b.mn[2] - 0.01 <= pt[2] <= b.mx[2] + 0.01 and abs(b.mx[1] - pt[1]) < 0.02)
    rms = [r for r in ramps
           if r.mn[0] <= hi[0] and r.mx[0] >= lo[0] and r.mn[1] <= hi[1] and r.mx[1] >= lo[1]
           and r.mn[2] <= hi[2] and r.mx[2] >= lo[2]]
    serving = [r for r in rms if _on(b, r.topPos)]
    blocking = [r for r in rms if r not in serving]
    clean_lp = lp = clean_arcs = ramp_arcs = 0
    best_clear = -1e9
    blockers = {}
    for lf in launch:
        if not stand_free(lf, p, nb, ia): continue
        lp += 1
        any_here = False
        for am in aim:
            dx, dz = am[0] - lf[0], am[2] - lf[2]
            if dx * dx + dz * dz < 0.25: continue
            m = math.hypot(dx, dz); dx, dz = dx / m, dz / m
            for sp in speeds:
                for hold in (True, False):
                    for c in controls:
                        vel = (dx * sp, p["takeoff"], dz * sp)
                        kind, hit, mc = sweep_arc(lf, vel, hold, None if c == "none" else c,
                                                  p, bounds, ia, ib, 4.0, floor_y, serving, blocking)
                        if kind == "ramp":
                            ramp_arcs += 1
                            clean_arcs += 1
                            any_here = True
                            continue
                        if kind == "block" and hit != ib:
                            blockers[nb[hit].name] = blockers.get(nb[hit].name, 0) + 1
                            continue
                        if kind != "land" or hit != ib: continue
                        clean_arcs += 1
                        any_here = True
                        best_clear = max(best_clear, 99.0 if mc >= 1e8 else mc)
        if any_here: clean_lp += 1
    chief = max(blockers.items(), key=lambda kv: kv[1])[0] if blockers else "-"
    return dict(gap=gap, rise=rise, exists=clean_arcs > 0, clean=clean_lp, points=lp,
                arcs=clean_arcs, rampArcs=ramp_arcs, ramps=len(rms),
                best=0.0 if best_clear == -1e9 else best_clear, chief=chief)

# ----------------------------------------------------------------- shooter lines
def line_clear(a, b, boxes, skip, ramps=()):
    for r in ramps:
        if r.seg_hits(a, b): return False, r.name
    d = tuple(b[i] - a[i] for i in range(3))
    for i, bx in enumerate(boxes):
        if i in skip: continue
        t0, t1, hit = 0.0, 1.0, True
        for ax in range(3):
            o, dd, lo, hi = a[ax], d[ax], bx.mn[ax], bx.mx[ax]
            if abs(dd) < 1e-6:
                if o < lo or o > hi: hit = False; break
                continue
            ta, tb = (lo - o) / dd, (hi - o) / dd
            if ta > tb: ta, tb = tb, ta
            t0, t1 = max(t0, ta), min(t1, tb)
            if t0 > t1: hit = False; break
        if hit: return False, bx.name
    return True, None

def shooters(boxes, lo, hi, ramps=()):
    """Every authored perch against the decks its `covers` field claims, in band with a clear line."""
    rows = []
    perches, _ = load_perches()
    for perch, spawn, _, _, decks in perches:
        ip = index_of(boxes, perch)
        if ip < 0: rows.append((perch, "MISSING", [], [], [])); continue
        pb = boxes[ip]
        muzzle = (pb.center[0], pb.mx[1] + 1.5, pb.center[2])
        cov, blk, oob = [], [], []
        for d in decks:
            idd = index_of(boxes, d)
            if idd < 0: blk.append(d + "?"); continue
            db = boxes[idd]
            chest = (db.center[0], db.mx[1] + 1.2, db.center[2])
            dist = math.sqrt(sum((chest[i] - muzzle[i]) ** 2 for i in range(3)))
            if dist < lo or dist > hi: oob.append("%s(%.1f)" % (d, dist)); continue
            ok, by = line_clear(muzzle, chest, boxes, {ip, idd}, ramps)
            (cov if ok else blk).append("%s(%.1f%s)" % (d, dist, "" if ok else " <-" + by))
        rows.append((perch, "", cov, blk, oob))
    return rows

# ----------------------------------------------------------------- the torch light budget
# Offline mirror of TorchDensityTests.NoTorchClusterExceedsThePerObjectLightLimit. URP's
# AdditionalLightsPerObjectLimit is 4 on both shipped RP assets; the 5th light reaching a surface is
# dropped AFTER being paid for, and which one is dropped changes as the camera moves, which reads as a
# torch popping. Probe at every torch, because that is where the player runs.
TORCH_RANGE = 9.0
TORCH_LIGHT_UP = 1.9
TORCH_EYE = 1.6

def torch_density(torches):
    rows = []
    for name, pos in torches:
        eye = (pos[0], pos[1] + TORCH_EYE, pos[2])
        n = 0
        for _, q in torches:
            lp = (q[0], q[1] + TORCH_LIGHT_UP, q[2])
            if math.sqrt(sum((lp[i] - eye[i]) ** 2 for i in range(3))) <= TORCH_RANGE: n += 1
        rows.append((n, name))
    rows.sort(reverse=True)
    return rows

def report_torches(after):
    torches = load_torches()
    if after: torches = apply_beacons(torches)
    rows = torch_density(torches)
    print("=== TORCH DENSITY, %s ===" % ("AFTER the authoring pass" if after else "shipped asset"))
    print("  %d torches; limit is 4 lights reaching any one probe" % len(torches))
    over = [r for r in rows if r[0] > 4]
    for n, name in rows[:8]:
        print("  %-32s %d light(s) reach it%s" % (name, n, "   OVER BUDGET" if n > 4 else ""))
    print("  worst cluster %d  ->  %s" % (rows[0][0], "OVER BUDGET" if over else "inside the 4-light limit"))

# ----------------------------------------------------------------- the sightline probe
# "Open" is a measurement, not a mood: standing on a deck, HOW MANY of the decks you are about to run
# can you actually see? A level whose answer is 1 is a corridor however wide its decks are. The probe
# stands the eye at the deck's centre 1.7 m up and looks at each following route deck's centre 0.5 m up,
# counting how many it can reach in a row before something opaque gets in the way; it stops at 6.
SIGHT_AHEAD = 6

def route_order(base_route):
    out, seen = [], set()
    for a, b in base_route:
        for n in (a, b):
            if n not in seen: seen.add(n); out.append(n)
    return out

def sightlines(boxes, base_route, ramps=()):
    order = route_order(base_route)
    idx = dict((b.name, i) for i, b in enumerate(boxes))
    rows, total = [], 0
    for i, n in enumerate(order):
        if n not in idx: continue
        a = boxes[idx[n]]
        eye = (a.center[0], a.top + 1.7, a.center[2])
        ahead, blocker, far = 0, None, 0.0
        for j in range(i + 1, min(i + 1 + SIGHT_AHEAD, len(order))):
            m = order[j]
            if m not in idx: break
            b = boxes[idx[m]]
            tgt = (b.center[0], b.top + 0.5, b.center[2])
            ok, who = line_clear(eye, tgt, boxes, set([idx[n], idx[m]]), ramps)
            if not ok: blocker = who; break
            ahead += 1
            far = max(far, math.sqrt(sum((tgt[k] - eye[k]) ** 2 for k in range(3))))
        total += ahead
        rows.append((n, ahead, far, blocker))
    return total / float(max(1, len(rows))), rows

def report_sight():
    base, _ = load_route()
    ramps = load_ramps()
    before = load_boxes()
    after = apply_authoring(load_boxes())
    mb, rb = sightlines(before, base, ramps)
    ma, ra = sightlines(after, base, ramps)
    print("MOVES VISIBLE AHEAD (of the next %d route decks)" % SIGHT_AHEAD)
    print("  shipped asset %.2f  ->  after the authoring pass %.2f" % (mb, ma))
    print()
    for (n, a0, d0, b0), (_, a1, d1, b1) in zip(rb, ra):
        mark = "  " if a0 == a1 else ("^ " if a1 > a0 else "v ")
        print("  %s%-20s %d -> %d  (%5.1f -> %5.1f m)  blocker %s -> %s"
              % (mark, n, a0, a1, d0, d1, b0 or "-", b1 or "-"))

# ----------------------------------------------------------------- the solar realms
def report_realms(realms, boxes):
    """Mirror of SolarArenaTests: one cell size, 100 m apart, shells apart, beyond the far plane."""
    fails = 0
    print("REALM LAYOUT (cells at x 700; SolarArenaTests holds every line)")
    route_half_width = max(abs(b.center[0]) + b.size[0] / 2.0 for b in boxes)
    for i, r in enumerate(realms):
        corner = math.hypot(r.floor + 0.35 + 0.5, r.wall - r.ceiling * 0.5)
        problems = []
        if corner >= r.shell: problems.append("wall corner outside shell")
        empty_x = abs(r.cell[0]) - r.shell - route_half_width
        if empty_x <= PLAYER_CAMERA_FAR_CLIP: problems.append("inside the %.0f m far plane" % PLAYER_CAMERA_FAR_CLIP)
        for pt, label in ((r.entry, "entry"), (r.enemy, "enemy"), (r.pickup, "pickup"), (r.exit if r.has_return else None, "exit")):
            if pt is None: continue
            radial = math.hypot(pt[0] - r.cell[0], pt[2] - r.cell[2])
            if radial > r.floor - 1.0: problems.append("%s lacks a metre of floor" % label)
            if not (0.0 <= pt[1] - r.cell[1] <= 1.5): problems.append("%s is not on the floor" % label)
        for other in realms[i + 1:]:
            d = math.sqrt(sum((r.cell[k] - other.cell[k]) ** 2 for k in range(3)))
            if d <= r.shell + other.shell: problems.append("shell overlaps " + other.gate)
        if problems: fails += 1
        print("  %s%-9s %-10s sun (%5.1f, %5.2f, %6.1f) r %4.1f / vis %4.1f   cell z %5.1f  floor %4.1f shell %4.1f  wall %4.1f ceiling %4.1f  %s"
              % ("FAIL " if problems else "     ", r.gate, r.theme, r.sun[0], r.sun[1], r.sun[2], r.radius, r.visual,
                 r.cell[2], r.floor, r.shell, r.wall, r.ceiling, "; ".join(problems) or "ok"))
    if len(realms) > 1:
        gaps = [realms[i + 1].cell[2] - realms[i].cell[2] for i in range(len(realms) - 1)]
        print("  cell spacing along z: %s" % ", ".join("%.0f" % g for g in gaps))
    return fails

def report_solar_approaches(realms, boxes, torches, p):
    """Mirror of SolarArenaTests.EveryExteriorApproachReachesThePortalAcrossAnOpenGap and
    ExteriorSunsHaveBreathingRoom: the take-off gap and the silhouette clearances around each sun."""
    fails = 0
    print("SOLAR APPROACHES (first three: 13.5-15.5 m projectile-earned carry; T4 and Boss: an open jump 1-8.5 m)")
    print("  approach pieces may come within 1.5 m of the visible sun; everything else stays 12 m clear")
    silhouettes = list(boxes) + torch_boxes(torches)
    for r in realms:
        deck_name = APPROACH_DECK.get(r.gate)
        approach = set(APPROACH_PIECES.get(r.gate, ()))
        ideck = index_of(boxes, deck_name) if deck_name else -1
        problems = []
        gap = float("nan"); vis_clear = float("nan")
        if ideck < 0:
            problems.append("approach deck %s missing" % deck_name)
        else:
            deck = boxes[ideck]
            player_y = deck.top + 0.9
            dy = abs(player_y - r.sun[1])
            if dy >= r.radius: problems.append("portal misses the take-off height")
            else:
                near_z = r.sun[2] - math.sqrt(r.radius ** 2 - dy ** 2)
                gap = near_z - deck.mx[2]
                if r.gate in CARRY_GATES:
                    if not (13.5 <= gap <= 15.5): problems.append("carry gap %.2f outside 13.5-15.5" % gap)
                elif not (1.0 < gap <= 8.5): problems.append("open-jump gap %.2f outside 1-8.5" % gap)
            vis_clear = point_box_distance(r.sun, deck) - r.visual
            if vis_clear < 1.45: problems.append("approach deck only %.2f m from the visible sun" % vis_clear)
        # Approach pieces within 1.5 m, every other box, torch and gate slab 12 m clear of the visible sun.
        worst, worst_name = 1e9, "-"
        for b in silhouettes:
            d = point_box_distance(r.sun, b) - r.visual
            if b.name in approach:
                if d < 1.45: problems.append("approach piece %s only %.2f m from the visible sun" % (b.name, d))
                continue
            if d < worst: worst, worst_name = d, b.name
        for other in realms:
            slabs = [("%s entry gate" % other.gate, other.entryZ, other.gateY, other.gate == r.gate)]
            if other.exitZ is not None: slabs.append(("%s exit gate" % other.gate, other.exitZ, other.exitY, False))
            for label, z, y, approach in slabs:
                slab = Box(label, (0.0, y, z), (other.gateSize[0], other.gateSize[1], other.gateSize[2]))
                d = point_box_distance(r.sun, slab) - r.visual
                if approach:
                    if d < 1.45: problems.append("own gate only %.2f m from the visible sun" % d)
                elif d < worst: worst, worst_name = d, label
        if worst < 11.95: problems.append("%s silhouettes %.2f m from the visible sun" % (worst_name, worst))
        if problems: fails += 1
        print("  %s%-9s off %-20s gap %5.2f  deck clearance %5.2f  nearest other %-22s %6.2f  %s"
              % ("FAIL " if problems else "     ", r.gate, deck_name or "-", gap, vis_clear, worst_name, worst,
                 "; ".join(problems) or "ok"))
    return fails

# ----------------------------------------------------------------- main
def main():
    after = "--after" in sys.argv
    if "--sight" in sys.argv:
        report_sight()
        return
    if "--torches" in sys.argv:
        report_torches(True)
        print()
        report_torches(False)
        return
    p = profile()
    base_route, tech_route = load_route()
    ramps = load_ramps()
    boxes = apply_authoring(load_boxes()) if after else load_boxes()
    realms = load_realms_authoring() if after else load_realms_asset()
    floor_y = -25.0
    print("=== %s | %d boxes | %d ramp(s) | %d realm(s) ===" %
          ("AFTER the authoring pass (what 8a writes)" if after else "shipped asset", len(boxes), len(ramps), len(realms)))
    print("profile: gravity %.0f jump %.1f (takeoff %.2f) ground %.0f slide-jump %.0f r %.2f h %.2f"
          % (p["gravity"], p["jumpHeight"], p["takeoff"], p["groundSpeed"], p["slideJump"], p["r"], p["height"]))
    print()
    print("BASELINE ROUTE (LevelArcReport.BaseRoute; every hop needs a clean arc and >= 3 clean launch points)")
    fails = 0
    for a, b in base_route:
        v = analyze_hop(boxes, a, b, p, p["groundSpeed"], floor_y, ("none",), ramps)
        if v is None: print("  MISSING %s -> %s" % (a, b)); fails += 1; continue
        bad = (not v["exists"]) or v["clean"] < 3
        if bad: fails += 1
        print("  %-6s %-14s -> %-20s gap %5.2f rise %+5.2f  clean %2d/%2d pts  arcs %3d  best clr %5.2f  chief %-22s %s"
              % ("FAIL" if bad else "", a, b, v["gap"], v["rise"], v["clean"], v["points"],
                 v["arcs"], v["best"], v["chief"],
                 ("RAMP: %d of %d arcs land on the slope" % (v["rampArcs"], v["arcs"])) if v.get("rampArcs") else ""))
    print()
    print("TECH LINES (LevelArcReport.TechRoute; each must be reachable WITH its moveset)")
    for a, b, moveset in tech_route:
        sp = p["slideJump"] if moveset == "SlideJump" else p["groundSpeed"]
        v = analyze_hop(boxes, a, b, p, sp, floor_y, ("none", "brake"), ramps)
        if v is None: print("  MISSING %s -> %s" % (a, b)); fails += 1; continue
        if not v["exists"]: fails += 1
        print("  [%-9s] %-13s -> %-13s gap %5.2f rise %+5.2f clean %2d/%2d  %s"
              % (moveset, a, b, v["gap"], v["rise"], v["clean"], v["points"], "ok" if v["exists"] else "FAIL"))
    print()
    print("SLIDE GATES (retired 2026-09-13: LevelDefinitionAuthoring.RemovedSlideGates; a present one is a stale asset)")
    present = [g for g in load_removed_gates() if index_of(boxes, g) >= 0]
    for g in present:
        b = boxes[index_of(boxes, g)]
        print("  FAIL %-18s still present at (%.1f, %.2f, %.1f) - run 8a" % (g, b.center[0], b.center[1], b.center[2]))
    fails += len(present)
    if not present: print("  none present - ok")
    print()
    lo, hi = shooter_band()
    print("SHOOTER PERCHES (band %.0f-%.0f m off pshooter_enemy01; every claimed deck in band with a clear line)" % (lo, hi))
    for perch, err, cov, blk, oob in shooters(boxes, lo, hi, ramps):
        flag = "FAIL " if (err or blk or oob) else "     "
        if err or blk or oob: fails += 1
        print("  %s%-12s covers %-46s blocked %-34s out of band %s"
              % (flag, perch, ",".join(cov) or "-", ",".join(blk) or "-", ",".join(oob) or "-"))
    print()
    print("WATER SHEETS (the authored Waters table, section-translated; a sheet lies inside one deck)")
    t2, t3, t4 = load_translation()
    for name, (c, s) in load_waters().items():
        sec = section_of(name)
        c = (c[0], c[1], c[2] + {2: t2, 3: t3, 4: t4}.get(sec, 0.0))
        bottom = c[1] - s[1] / 2.0
        deck = None
        for b in boxes:
            if abs(bottom - b.mx[1]) < 0.06 and b.mn[0] <= c[0] <= b.mx[0] and b.mn[2] <= c[2] <= b.mx[2]:
                deck = b; break
        inside = deck is not None and (c[0] - s[0] / 2 >= deck.mn[0] - 0.01 and c[0] + s[0] / 2 <= deck.mx[0] + 0.01
                                       and c[2] - s[2] / 2 >= deck.mn[2] - 0.01 and c[2] + s[2] / 2 <= deck.mx[2] + 0.01)
        if not inside: fails += 1
        print("  %-16s on %-12s inside deck %s" % (name, deck.name if deck else "NOTHING", "ok" if inside else "FAIL"))
    print()
    print("BALLOON ORB CLEARANCE (orb must clear every box by radius + 0.40)")
    for name, pos, rad in load_balloons():
        worst, wb = 1e9, "-"
        for b in boxes:
            d = seg_box_distance(pos[0], pos[2], pos[1], pos[1], b)
            if d < worst: worst, wb = d, b.name
        if worst <= rad + 0.4: fails += 1
        print("  %-12s nearest %-16s %.2f m  %s" % (name, wb, worst, "ok" if worst > rad + 0.4 else "FAIL"))
    print()
    fails += report_realms(realms, boxes)
    print()
    fails += report_solar_approaches(realms, boxes, apply_beacons(load_torches()) if after else load_torches(), p)
    print()
    print("PINNED CONSTRAINTS (what LevelSpan1-3Tests hold on the hybrid course)")
    def bx(n): return boxes[index_of(boxes, n)]
    def shoulder(n): b = bx(n); return abs(b.center[0]) - b.size[0] / 2.0
    checks = []
    for n in ("T1_Rail_L", "T1_Rail_R", "T1_Obelisk_W", "T1_Wall_Start", "T1_Wall_Causeway", "T1_Wall_Landing"):
        checks.append(("%s stays off the T1 centre strip (|x| >= 3)" % n, shoulder(n) >= 3.0))
    for n in ("T2_Wall_East", "T2_Wall_Landing_East", "T2_Wall_West", "T2_Wall_Landing_West"):
        checks.append(("%s stays outside the T2 crossover (|x| >= 7)" % n, shoulder(n) >= 7.0))
    checks.append(("T2_Buttress.max.x <= T2_L2.min.x", bx("T2_Buttress").mx[0] <= bx("T2_L2").mn[0] + 1e-3))
    checks.append(("T2_Tower is 4 wide x 20 tall", abs(bx("T2_Tower").size[0] - 4.0) < 1e-3 and abs(bx("T2_Tower").size[1] - 20.0) < 1e-3))
    for n in ("T3_Obelisk_W1", "T3_Obelisk_W2", "T3_Obelisk_E1", "T3_Obelisk_E2", "T3_Recovery_W1", "T3_Recovery_W2",
              "T3_Recovery_E1", "T3_Recovery_E2", "T3_Wall_Pillars", "T3_Wall_Landing_S", "T3_Wall_Span"):
        checks.append(("%s stays off the T3 lane (|x| >= 4)" % n, shoulder(n) >= 4.0))
    for i in range(1, 12):
        checks.append(("T2_L%d is >= 10 m wide" % i, bx("T2_L%d" % i).size[0] >= 10.0))
    for label, ok in checks:
        if not ok: fails += 1
        print("  %-52s %s" % (label, "ok" if ok else "FAIL"))
    print()
    print("PERCH VOLUME OVERLAP (a perch shelf may intersect no other box in 3D)")
    bad = 0
    perches, _ = load_perches()
    for perch, _, _, _, _ in perches:
        ip = index_of(boxes, perch)
        if ip < 0: print("  FAIL %s missing" % perch); bad += 1; continue
        pb = boxes[ip]
        for b in boxes:
            if b.name == perch: continue
            if all(pb.mn[k] < b.mx[k] and pb.mx[k] > b.mn[k] for k in range(3)):
                print("  FAIL %s intersects %s" % (perch, b.name)); bad += 1
    if not bad: print("  all %d clear" % len(perches))
    fails += bad
    print()
    print("RAMPS (angle ceiling ~35 deg; base and top must each overlap their deck)")
    for r in ramps:
        solid = [b.name for b in boxes if r.overlaps(b)]
        base_on = [b.name for b in boxes if b.mn[0] - 0.01 <= r.base[0] <= b.mx[0] + 0.01
                   and b.mn[2] - 0.01 <= r.base[2] <= b.mx[2] + 0.01 and abs(b.mx[1] - r.base[1]) < 0.02]
        top_on = [b.name for b in boxes if b.mn[0] - 0.01 <= r.topPos[0] <= b.mx[0] + 0.01
                  and b.mn[2] - 0.01 <= r.topPos[2] <= b.mx[2] + 0.01 and abs(b.mx[1] - r.topPos[1]) < 0.02]
        ok = bool(base_on and top_on and abs(r.angle) <= 35.0)
        if not ok: fails += 1
        print("  %-18s %5.1f deg  %.1f w x %6.2f run x %+6.2f rise  yaw %5.1f   base on %-22s top on %-22s %s"
              % (r.name, r.angle, r.w, r.run, r.rise, r.yaw,
                 ",".join(base_on) or "NOTHING", ",".join(top_on) or "NOTHING", "ok" if ok else "FAIL"))
        stray = [n for n in solid if n not in base_on and n not in top_on]
        if stray: print("      *** cuts through %s" % ", ".join(stray))
    print()
    print("VERDICT: %s" % ("clean" if fails == 0 else "%d check(s) failed" % fails))
    return fails

if __name__ == "__main__":
    sys.exit(1 if main() else 0)
