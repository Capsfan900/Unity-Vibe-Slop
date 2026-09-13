"""Offline mirror of Assets/Editor/LevelArcAnalyzer.AnalyzeHop and the LevelTraversalAnalyzer
line-of-sight / water checks, so a level change can be measured without the Unity editor.

This is a VERIFICATION AID for the level designer, not a second source of truth: the editor's
LevelArcReport and the Level*Tests remain the authority. Every constant here is read off the
shipped Player.prefab and the level asset, never typed in twice.

Usage:  python Tools/level_arc_offline.py            # measures the shipped asset
        python Tools/level_arc_offline.py --after     # applies the openness reshapes first
        python Tools/level_arc_offline.py --torches   # the torch light budget, with and without the beacons
        python Tools/level_arc_offline.py --sight     # HOW MANY MOVES AHEAD you can see, shipped vs reshaped
        python Tools/level_arc_offline.py --noramps   # as above but with the authored RAMPS removed

RAMPS (2026-09-07). A ramp is the one piece in the level that is NOT an axis-aligned box, so it gets its
own oriented-box class here rather than being flattened to an AABB — an AABB of a yawed 6 m ramp is half
again as wide as the ramp and would report bolt lines and sightlines blocked that are not. Ramps are read
out of LevelDefinitionAuthoring.Ramps by the same "parse the source, never retype the numbers" rule as
the reshapes. Two departures from exactness, both stated where they are made: the arc sweep treats the
player capsule as axis-aligned IN RAMP-LOCAL SPACE (an error of at most 1 - cos(15 deg) = 3.4% of the
capsule's height on the steepest ramp authored), and an arc that touches a ramp on the way down is
counted as an ARRIVAL rather than a block, because a body that lands on a ramp is on the route.
"""
import io
import math
import re
import sys
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSET = os.path.join(ROOT, "Assets/Data/Levels/Level_01_Level.asset")
PLAYER = os.path.join(ROOT, "Assets/Prefabs/Player.prefab")

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

V = r"\{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}"

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
    body = t.split("\n  spawns:")[1].split("\n  pickups:")[0]
    out = {}
    for m in re.finditer(r"  - name: (\S+)[\s\S]*?position: %s" % V, body):
        out[m.group(1)] = tuple(float(x) for x in m.groups()[1:4])
    return out

def load_balloons():
    t = open(ASSET).read()
    if "\n  balloons:" not in t: return []
    body = t.split("\n  balloons:")[1].split("\n  waters:")[0]
    out = []
    for m in re.finditer(r"  - name: (\S+)\s*\n\s+position: %s[\s\S]*?radius: ([\d.]+)" % V, body):
        out.append((m.group(1), tuple(float(x) for x in m.groups()[1:4]), float(m.group(5))))
    return out

# ----------------------------------------------------------------- the openness pass, READ from the source
# These are NOT retyped here. The one time they were, a rail centre was transcribed 0.2 m off and the
# error was invisible to this tool because the tool agreed with the typo. The tables are parsed straight
# out of Assets/Editor/LevelDefinitionAuthoring.cs, so the thing measured is the thing that will be built.
AUTHORING = os.path.join(ROOT, "Assets/Editor/LevelDefinitionAuthoring.cs")
CSV3 = r"new Vector3\(([-\d.f]+), ([-\d.f]+), ([-\d.f]+)\)"

def _f(s): return float(s.rstrip("f"))

def _table(src, marker, pattern):
    body = src.split(marker)[1]
    body = body.split("};")[0]
    return re.findall(pattern, body)

def load_reshapes():
    src = io.open(AUTHORING, encoding="utf-8").read()
    rows = _table(src, "public static readonly Reshape[] Reshapes",
                  r'new Reshape\("(\w+)",\s*' + CSV3 + r',\s*' + CSV3)
    return [(r[0], tuple(_f(x) for x in r[1:4]), tuple(_f(x) for x in r[4:7])) for r in rows]

def load_beacons():
    src = io.open(AUTHORING, encoding="utf-8").read()
    rows = _table(src, "public static readonly TorchDef[] RouteBeacons",
                  r'name = "(\w+)", basePosition = ' + CSV3)
    return [(r[0], tuple(_f(x) for x in r[1:4])) for r in rows]

def load_waters():
    src = io.open(AUTHORING, encoding="utf-8").read()
    rows = _table(src, "public static readonly Water[] Waters",
                  r'new Water\("(\w+)",\s*' + CSV3 + r',\s*' + CSV3)
    return dict((r[0], (tuple(_f(x) for x in r[1:4]), tuple(_f(x) for x in r[4:7]))) for r in rows)

RAMP_ROW = (r'new Ramp\("(\w+)",\s*' + CSV3 +
            r',\s*([-\d.f]+),\s*([-\d.f]+),\s*([-\d.f]+),\s*([-\d.f]+)')

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
        self.name, self.base, self.w, self.run, self.rise, self.yaw, self.th =             name, base, width, run, rise, yaw, thickness
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
    """The authored ramp table, parsed out of LevelDefinitionAuthoring — never retyped here."""
    src = io.open(AUTHORING, encoding="utf-8").read()
    if "public static readonly Ramp[] Ramps" not in src: return []
    rows = _table(src, "public static readonly Ramp[] Ramps", RAMP_ROW)
    th = 0.5
    m = re.search(r"RampThickness = ([\d.]+)f", src)
    if m: th = float(m.group(1))
    return [RampBox(r[0], tuple(_f(x) for x in r[1:4]), _f(r[4]), _f(r[5]), _f(r[6]), _f(r[7]), th)
            for r in rows]

RESHAPES = load_reshapes()
WATER_AFTER = load_waters()
WATER_BEFORE = {"T1_Water_Fast": ((0.0, 0.52, 28.0), (2.2, 0.04, 5.6))}

def load_torches():
    """The level's own torches, straight out of the asset."""
    t = open(ASSET).read()
    body = t.split("\n  torches:")[1]
    for stop in ("\n  pickups:", "\n  balloons:", "\n  waters:", "\n  spawns:"):
        body = body.split(stop)[0]
    out = []
    for m in re.finditer(r"  - name: (\S+)\s*\n\s+basePosition: %s" % V, body):
        out.append((m.group(1), tuple(float(x) for x in m.groups()[1:4])))
    return out

def apply_reshapes(boxes):
    """Mirror of Apply's first block: look each box up BY NAME and write an absolute centre/size."""
    by = dict((b.name, i) for i, b in enumerate(boxes))
    for name, c, s in RESHAPES:
        if name in by:
            boxes[by[name]] = Box(name, c, s)
    return boxes

def apply_beacons(torches):
    """Mirror of Apply: drop every Torch_Beacon_ and re-add the authored list."""
    out = [t for t in torches if not t[0].startswith("Torch_Beacon_")]
    out.extend(load_beacons())
    return out

# ----------------------------------------------------------------- geometry
def seg_box_distance(x, z, y0, y1, b):
    dx = max(b.mn[0] - x, x - b.mx[0], 0.0)
    dz = max(b.mn[2] - z, z - b.mx[2], 0.0)
    dy = max(b.mn[1] - y1, y0 - b.mx[1], 0.0)
    return math.sqrt(dx * dx + dy * dy + dz * dz)

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

def gravity_for(p, vy, hold):
    g = p["gravity"]
    if vy > 0.0:
        if not hold: g += p["gravity"] * p["jumpCut"]
    elif vy < 0.0 and p["fallGravityMultiplier"] > 0.0:
        g = p["gravity"] * p["fallGravityMultiplier"]
    return g

def carry(p, v):
    k = p["airCarryDecay"]
    if k <= 0.0: return v
    sp = math.hypot(v[0], v[2])
    cap = p["groundSpeed"]
    if sp <= cap or sp < 1e-5: return v
    target = cap + (sp - cap) * math.exp(-k * DT)
    f = target / sp
    return (v[0] * f, v[1], v[2] * f)

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
                # cheap world-AABB reject before the local transform: the capsule cannot touch a slab it is
                # not even inside the bounding box of, and this is seconds instead of minutes.
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
    # arriving. Any other ramp nearby is just a slab, and blocks. Getting this wrong is not academic — the
    # first version counted arrival on ANY nearby ramp as success and reported the T1_Fast_1 slide-jump
    # GATE broken, because arcs off T1_Stone_1 were landing on the Stone_1 -> Stone_2 ramp.
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

PERCHES = [
    ("T1_Perch_W", ["T1_Causeway", "T1_Stone_4"]),
    ("T1_Perch_E", ["T1_Wall_Landing", "T1_Stone_5"]),
    ("T2_Perch_W", ["T2_L1", "T2_L6", "T2_L2"]),
    ("T2_Perch_E", ["T2_L3", "T2_L9"]),
    ("T3_Perch_W", ["T3_Pillar_2", "T3_Pillar_3"]),
    ("T3_Perch_E", ["T3_Step_1", "T3_Step_2", "T3_Step_3"]),
]

def shooters(boxes, lo, hi, ramps=()):
    rows = []
    for perch, decks in PERCHES:
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

# ----------------------------------------------------------------- main
BASE_ROUTE = [
    ("Ground_Start", "T1_Stone_1"), ("T1_Stone_1", "T1_Stone_2"), ("T1_Stone_2", "T1_Stone_3"),
    ("T1_Stone_3", "T1_Stone_4"), ("T1_Stone_4", "T1_Causeway"), ("T1_Causeway", "T1_Stone_5"),
    ("T1_Stone_5", "T1_Arena"), ("T1_Arena", "T2_Entry"), ("T2_Entry", "T2_L1"), ("T2_L1", "T2_L2"),
    ("T2_L2", "T2_L3"), ("T2_L3", "T2_L4"), ("T2_L4", "T2_L5"), ("T2_L5", "T2_L6"), ("T2_L6", "T2_L7"),
    ("T2_L7", "T2_L8"), ("T2_L8", "T2_L9"), ("T2_L9", "T2_L10"), ("T2_L10", "T2_L11"),
    ("T2_L11", "T2_Bridge"), ("T3_Entry", "T3_Pillar_1"), ("T3_Pillar_1", "T3_Pillar_2"),
    ("T3_Pillar_2", "T3_Pillar_3"), ("T3_Pillar_3", "T3_Pillar_4"), ("T3_Pillar_4", "T3_Span"),
    ("T3_Span", "T3_Step_1"), ("T3_Step_1", "T3_Step_2"), ("T3_Step_2", "T3_Step_3"),
    ("T3_Step_3", "T3_Arena"),
]

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
    print("=== TORCH DENSITY, %s ===" % ("AFTER the beacon pass" if after else "shipped asset"))
    print("  %d torches; limit is 4 lights reaching any one probe" % len(torches))
    over = [r for r in rows if r[0] > 4]
    for n, name in rows[:8]:
        print("  %-32s %d light(s) reach it%s" % (name, n, "   OVER BUDGET" if n > 4 else ""))
    print("  worst cluster %d  ->  %s" % (rows[0][0], "OVER BUDGET" if over else "inside the 4-light limit"))

# ----------------------------------------------------------------- the sightline probe
# "Open" is a measurement, not a mood, and this is the one that matters most for a speedrun level:
# standing on a deck, HOW MANY of the decks you are about to run can you actually see? A level whose
# answer is 1 is a corridor however wide its decks are, because the player is being told the route one
# box at a time. The probe stands the eye at the deck's centre 1.7 m up (standing height off the
# shipped CharacterController) and looks at each following route deck's centre 0.5 m up - a foot-level
# target, because what you need to see is the SURFACE, not the airspace over it - and counts how many
# it can reach in a row before something opaque gets in the way. It stops at 6 because past that the
# answer is "the rest of the level" and stops being informative.
#
# It found three things the arc report could not: T1_Fallen_Obelisk, a slide gate parked on the
# causeway's exit edge, was the first blocker from SIX consecutive vantage points and left the causeway
# itself seeing nothing at all; T2_Tower, a 5 m core in a 19 m helix, was the first blocker from eight
# of the spiral's eleven; and the arenas' 6 m doorways were throwing away the only wide rooms in the
# level. All three are fixed in the second openness pass.

SIGHT_AHEAD = 6

def route_order():
    out, seen = [], set()
    for a, b in BASE_ROUTE:
        for n in (a, b):
            if n not in seen: seen.add(n); out.append(n)
    return out

def sightlines(boxes, ramps=()):
    order = route_order()
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
    return total / float(len(rows)), rows

def report_sight():
    before = load_boxes(); apply_reshapes(before)
    after = load_boxes(); apply_reshapes(after)
    mb, rb = sightlines(before)
    ma, ra = sightlines(after, load_ramps())
    print("MOVES VISIBLE AHEAD (of the next %d route decks)" % SIGHT_AHEAD)
    print("  without the ramps %.2f  ->  with the authored ramps %.2f" % (mb, ma))
    print()
    for (n, a0, d0, b0), (_, a1, d1, b1) in zip(rb, ra):
        mark = "  " if a0 == a1 else ("^ " if a1 > a0 else "v ")
        print("  %s%-16s %d -> %d  (%5.1f -> %5.1f m)  blocker %s -> %s"
              % (mark, n, a0, a1, d0, d1, b0 or "-", b1 or "-"))


def main():
    after = "--after" in sys.argv
    ramps = [] if "--noramps" in sys.argv else load_ramps()
    if "--sight" in sys.argv:
        report_sight()
        return
    if "--torches" in sys.argv:
        report_torches(True)
        print()
        report_torches(False)
        return
    p = profile()
    boxes = load_boxes()
    if after: apply_reshapes(boxes)
    floor_y = -25.0
    print("=== %s | %d ramp(s) ===" % ("AFTER the openness pass" if after else "shipped asset", len(ramps)))
    print("profile: gravity %.0f jump %.1f (takeoff %.2f) ground %.0f slide-jump %.0f r %.2f h %.2f"
          % (p["gravity"], p["jumpHeight"], p["takeoff"], p["groundSpeed"], p["slideJump"], p["r"], p["height"]))
    print()
    print("BASELINE ROUTE")
    fails = 0
    for a, b in BASE_ROUTE:
        v = analyze_hop(boxes, a, b, p, p["groundSpeed"], floor_y, ("none",), ramps)
        if v is None: print("  MISSING %s -> %s" % (a, b)); fails += 1; continue
        if not v["exists"]: fails += 1
        print("  %-6s %-14s -> %-16s gap %5.2f rise %+5.2f  clean %2d/%2d pts  arcs %3d  best clr %5.2f  chief %-18s %s"
              % ("FAIL" if not v["exists"] else "", a, b, v["gap"], v["rise"], v["clean"], v["points"],
                 v["arcs"], v["best"], v["chief"],
                 ("RAMP: %d of %d arcs land on the slope" % (v["rampArcs"], v["arcs"])) if v.get("rampArcs") else ""))
    print()
    print("TECH LINES")
    for a, b, sp, name in (("T1_Stone_1", "T1_Fast_1", p["slideJump"], "SlideJump"),
                           ("T1_Fast_1", "T1_Stone_4", p["groundSpeed"], "Base")):
        v = analyze_hop(boxes, a, b, p, sp, floor_y, ("none", "brake"), ramps)
        print("  [%-9s] %-13s -> %-13s gap %5.2f rise %+5.2f clean %2d/%2d  %s"
              % (name, a, b, v["gap"], v["rise"], v["clean"], v["points"], "ok" if v["exists"] else "FAIL"))
    g = analyze_hop(boxes, "T1_Stone_1", "T1_Fast_1", p, p["groundSpeed"], floor_y, ("none", "brake"), ramps)
    print("  GATE: base kit onto T1_Fast_1 -> %s (must be UNREACHABLE)"
          % ("REACHABLE - GATE BROKEN" if g["exists"] else "cannot"))
    print()
    print("SLIDE GATES")
    for lint, deck in (("T1_Fallen_Obelisk", "T1_Causeway"), ("T3_Fallen_Lintel", "T3_Span")):
        l, d = boxes[index_of(boxes, lint)], boxes[index_of(boxes, deck)]
        clr = l.mn[1] - d.mx[1]
        print("  %-18s over %-12s clearance %.2f (slide %s, standing %s) top %.2f above (%s) spans deck %s"
              % (lint, deck, clr, "fits" if clr > p["slideHeight"] + 0.05 else "DOES NOT FIT",
                 "blocked" if clr < p["height"] else "NOT BLOCKED",
                 l.mx[1] - d.mx[1], "jumpable" if l.mx[1] - d.mx[1] < p["jumpHeight"] - 0.2 else "NOT JUMPABLE",
                 l.mn[0] <= d.mn[0] and l.mx[0] >= d.mx[0]))
    print()
    print("SHOOTER PERCHES (band 3-32 m off pshooter_enemy01)")
    for perch, err, cov, blk, oob in shooters(boxes, 3.0, 32.0, ramps):
        flag = "FAIL " if (err or len(cov) < 2 or oob) else "     "
        print("  %s%-12s covers %-46s blocked %-34s out of band %s"
              % (flag, perch, ",".join(cov) or "-", ",".join(blk) or "-", ",".join(oob) or "-"))
    print()
    print("WATER SHEETS")
    sheets = WATER_AFTER if after else WATER_BEFORE
    for name, (c, s) in sheets.items():
        bottom = c[1] - s[1] / 2.0
        deck = None
        for b in boxes:
            if abs(bottom - b.mx[1]) < 0.06 and b.mn[0] <= c[0] <= b.mx[0] and b.mn[2] <= c[2] <= b.mx[2]:
                deck = b; break
        inside = deck is not None and (c[0] - s[0] / 2 >= deck.mn[0] - 0.01 and c[0] + s[0] / 2 <= deck.mx[0] + 0.01
                                       and c[2] - s[2] / 2 >= deck.mn[2] - 0.01 and c[2] + s[2] / 2 <= deck.mx[2] + 0.01)
        print("  %-16s on %-12s inside deck %s" % (name, deck.name if deck else "NOTHING", inside))
    print()
    print("BALLOON ORB CLEARANCE (orb must clear every box by radius + 0.40)")
    for name, pos, rad in load_balloons():
        worst, wb = 1e9, "-"
        for b in boxes:
            d = seg_box_distance(pos[0], pos[2], pos[1], pos[1], b)
            if d < worst: worst, wb = d, b.name
        print("  %-12s nearest %-16s %.2f m  %s" % (name, wb, worst, "ok" if worst > rad + 0.4 else "FAIL"))
    print()
    print("PINNED CONSTRAINTS (the x-coordinates Level*Tests hold)")
    def bx(n): return boxes[index_of(boxes, n)]
    checks = [
        ("T2_Buttress.max.x == T2_L2.min.x", abs(bx("T2_Buttress").mx[0] - bx("T2_L2").mn[0]) < 0.002),
        ("T2_L1.max.x < T2_Wall_East.min.x - 2r", bx("T2_L1").mx[0] < bx("T2_Wall_East").mn[0] - 2 * p["radius"]),
        ("T2_L2.max.x < T2_Wall_East.min.x - 2r", bx("T2_L2").mx[0] < bx("T2_Wall_East").mn[0] - 2 * p["radius"]),
        ("T2_L4.min.x > T2_Wall_West.max.x + 2r", bx("T2_L4").mn[0] > bx("T2_Wall_West").mx[0] + 2 * p["radius"]),
        ("T2_L5.min.x > T2_Wall_West.max.x + 2r", bx("T2_L5").mn[0] > bx("T2_Wall_West").mx[0] + 2 * p["radius"]),
        ("T2_L1 hugs east wall (0.8 < d <= 2)", 2 * p["radius"] < bx("T2_Wall_East").mn[0] - bx("T2_L1").mx[0] <= 2.0),
        ("T2_L4 hugs west wall (0.8 < d <= 2)", 2 * p["radius"] < bx("T2_L4").mn[0] - bx("T2_Wall_West").mx[0] <= 2.0),
        ("T1_Wall_Landing.min.x >= T1_Rail_R.max.x", bx("T1_Wall_Landing").mn[0] >= bx("T1_Rail_R").mx[0]),
        ("T1_Causeway.max.x < T1_Wall_Causeway.min.x", bx("T1_Causeway").mx[0] < bx("T1_Wall_Causeway").mn[0]),
        ("T1_Fast_1.max.x < T1_Wall_Start.min.x - 2r", bx("T1_Fast_1").mx[0] < bx("T1_Wall_Start").mn[0] - 2 * p["radius"]),
        # A rail's INNER face is flush with the deck edge and its body hangs OUTSIDE. A rail standing ON
        # the deck steals a capsule radius of take-off room either side of its 0.2 m, which is exactly how
        # the causeway -> T1_Stone_5 hop lost a launch column the first time this pass was written.
        ("T1_Rail_L hangs outside the causeway edge",
         abs(bx("T1_Rail_L").mx[0] - bx("T1_Causeway").mn[0]) < 0.002),
        ("T1_Rail_R hangs outside the causeway edge",
         abs(bx("T1_Rail_R").mn[0] - bx("T1_Causeway").mx[0]) < 0.002),
        ("T2_L9 clears T2_L10 in plan", bx("T2_L9").mx[2] <= bx("T2_L10").mn[2] + 0.001),
        ("T2_L9 matches T2_L3's footprint",
         abs((bx("T2_L9").mx[0] - bx("T2_L9").mn[0]) - (bx("T2_L3").mx[0] - bx("T2_L3").mn[0])) < 0.002 and
         abs((bx("T2_L9").mx[2] - bx("T2_L9").mn[2]) - (bx("T2_L3").mx[2] - bx("T2_L3").mn[2])) < 0.002),
    ]
    for label, ok in checks:
        print("  %-42s %s" % (label, "ok" if ok else "FAIL"))
    print()
    print("PERCH PLAN OVERLAP (a perch may overlap nothing in plan)")
    bad = 0
    for perch, _ in PERCHES:
        pb = bx(perch)
        for b in boxes:
            if b.name == perch: continue
            if pb.mn[0] < b.mx[0] and pb.mx[0] > b.mn[0] and pb.mn[2] < b.mx[2] and pb.mx[2] > b.mn[2]:
                print("  FAIL %s overlaps %s" % (perch, b.name)); bad += 1
    if not bad: print("  all six clear")
    print()
    print("RAMPS (angle ceiling ~35 deg; base and top must each overlap their deck)")
    for r in ramps:
        solid = [b.name for b in boxes if r.overlaps(b)]
        base_on = [b.name for b in boxes if b.mn[0] - 0.01 <= r.base[0] <= b.mx[0] + 0.01
                   and b.mn[2] - 0.01 <= r.base[2] <= b.mx[2] + 0.01 and abs(b.mx[1] - r.base[1]) < 0.02]
        top_on = [b.name for b in boxes if b.mn[0] - 0.01 <= r.topPos[0] <= b.mx[0] + 0.01
                  and b.mn[2] - 0.01 <= r.topPos[2] <= b.mx[2] + 0.01 and abs(b.mx[1] - r.topPos[1]) < 0.02]
        print("  %-18s %4.1f deg  %.1f w x %.2f run x %+.2f rise  yaw %5.1f   base on %-14s top on %-14s %s"
              % (r.name, r.angle, r.w, r.run, r.rise, r.yaw,
                 ",".join(base_on) or "NOTHING", ",".join(top_on) or "NOTHING",
                 "ok" if (base_on and top_on and abs(r.angle) <= 35.0) else "FAIL"))
        stray = [n for n in solid if n not in base_on and n not in top_on]
        if stray: print("      *** cuts through %s" % ", ".join(stray))
    print()
    print("VERDICT: %s" % ("clean" if fails == 0 else "%d baseline hop(s) with no clean arc" % fails))

if __name__ == "__main__":
    main()
