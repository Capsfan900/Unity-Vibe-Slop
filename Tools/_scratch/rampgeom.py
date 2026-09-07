import math, os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "Tools"))
import level_arc_offline as L

def mat(yaw, a):
    cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    ca, sa = math.cos(math.radians(-a)), math.sin(math.radians(-a))
    Ry = [[cy,0,sy],[0,1,0],[-sy,0,cy]]
    Rx = [[1,0,0],[0,ca,-sa],[0,sa,ca]]
    return [[sum(Ry[i][k]*Rx[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
def mul(M,v):  return tuple(sum(M[i][j]*v[j] for j in range(3)) for i in range(3))
def mulT(M,v): return tuple(sum(M[j][i]*v[j] for j in range(3)) for i in range(3))

class Ramp(object):
    def __init__(self, name, base, width, run, rise, yaw, th=0.5):
        self.name, self.base, self.w, self.run, self.rise, self.yaw, self.th = name, base, width, run, rise, yaw, th
        self.angle = math.degrees(math.atan2(rise, run))
        self.slope = math.hypot(run, rise)
        self.R = mat(yaw, self.angle)
        f, u = mul(self.R,(0,0,1)), mul(self.R,(0,1,0))
        self.center = tuple(base[i] + f[i]*self.slope/2.0 - u[i]*th/2.0 for i in range(3))
        self.half = (width/2.0, th/2.0, self.slope/2.0)
        h = (math.sin(math.radians(yaw))*run, rise, math.cos(math.radians(yaw))*run)
        self.top = tuple(base[i]+h[i] for i in range(3))
        cs = []
        for sx in (-1,1):
            for sy in (-1,1):
                for sz in (-1,1):
                    v = mul(self.R,(sx*self.half[0], sy*self.half[1], sz*self.half[2]))
                    cs.append(tuple(self.center[i]+v[i] for i in range(3)))
        self.mn = tuple(min(c[i] for c in cs) for i in range(3))
        self.mx = tuple(max(c[i] for c in cs) for i in range(3))
    def local(self, p): return mulT(self.R, tuple(p[i]-self.center[i] for i in range(3)))
    def seg_hits(self, a, b):
        la, lb = self.local(a), self.local(b)
        d = tuple(lb[i]-la[i] for i in range(3))
        t0, t1 = 0.0, 1.0
        for ax in range(3):
            o, dd, lo, hi = la[ax], d[ax], -self.half[ax], self.half[ax]
            if abs(dd) < 1e-9:
                if o < lo or o > hi: return False
                continue
            ta, tb = (lo-o)/dd, (hi-o)/dd
            if ta > tb: ta, tb = tb, ta
            t0, t1 = max(t0, ta), min(t1, tb)
            if t0 > t1: return False
        return True
    def point_depth(self, p):
        """Signed penetration of a point into the slab: >0 inside."""
        l = self.local(p)
        return min(self.half[i]-abs(l[i]) for i in range(3))
    def aabb_overlaps(self, b, pad=0.0):
        return all(self.mn[i] < b.mx[i]+pad and self.mx[i] > b.mn[i]-pad for i in range(3))
    def obb_overlaps(self, b, pad=0.0):
        """SAT-lite: sample the slab's own volume against an AABB, plus AABB-vs-AABB. Exact enough
        for a 0.5 m slab: 21 x 5 x 3 lattice of interior points."""
        if not self.aabb_overlaps(b, pad): return False
        for i in range(22):
            for j in range(6):
                for k in range(4):
                    lx = -self.half[0] + 2*self.half[0]*k/3.0
                    ly = -self.half[1] + 2*self.half[1]*j/5.0
                    lz = -self.half[2] + 2*self.half[2]*i/21.0
                    w = mul(self.R,(lx,ly,lz))
                    p = tuple(self.center[m]+w[m] for m in range(3))
                    if all(b.mn[m]-pad <= p[m] <= b.mx[m]+pad for m in range(3)): return True
        return False
