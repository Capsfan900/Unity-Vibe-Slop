import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rampgeom import Ramp
import level_arc_offline as L

boxes = L.load_boxes(); D = {b.name: b for b in boxes}

def perch_line(perch, deck):
    pb, db = D[perch], D[deck]
    return (pb.center[0], pb.mx[1]+1.5, pb.center[2]), (db.center[0], db.mx[1]+1.2, db.center[2])

def test(r, ignore=()):
    print("%-22s run %.2f rise %+.2f yaw %5.1f  ANGLE %5.2f deg  top (%6.2f,%6.2f,%7.2f)"
          % (r.name, r.run, r.rise, r.yaw, r.angle, r.top[0], r.top[1], r.top[2]))
    print("   AABB  x %6.2f..%6.2f  y %6.2f..%6.2f  z %7.2f..%7.2f" % (r.mn[0],r.mx[0],r.mn[1],r.mx[1],r.mn[2],r.mx[2]))
    hits = [b.name for b in boxes if b.name not in ignore and r.obb_overlaps(b)]
    print("   solid overlap:", ", ".join(hits) or "-")
    bad = 0
    for perch, decks in L.PERCHES:
        for d in decks:
            if d not in D: continue
            muz, chest = perch_line(perch, d)
            if r.seg_hits(muz, chest):
                print("   *** BLOCKS BOLT %s -> %s" % (perch, d)); bad += 1
    # sightlines: every route-deck pair whose line passes it
    order = L.route_order()
    for i, n in enumerate(order):
        if n not in D: continue
        a = D[n]; eye = (a.center[0], a.top+1.7, a.center[2])
        for j in range(i+1, min(i+7, len(order))):
            m = order[j]
            if m not in D: continue
            b = D[m]; tgt = (b.center[0], b.top+0.5, b.center[2])
            if r.seg_hits(eye, tgt):
                print("   ~~~ blocks sightline %s -> %s" % (n, m)); bad += 1
    if not bad: print("   no bolt line, no sightline blocked")
    print()

CANDS = [
  (Ramp("T1_Ramp_Stone12",  ( 2.0, 0.0,  16.3), 3.0, 3.90, 0.5,   0), ("T1_Stone_1","T1_Stone_2")),
  (Ramp("T1_Ramp_Stone34",  (-3.0, 1.0,  31.9), 3.0, 3.80, 0.5,  18), ("T1_Stone_3","T1_Stone_4")),
  (Ramp("T1_Ramp_Causeway", (-0.5, 1.5,  40.8), 3.8, 2.40, 0.5,   0), ("T1_Stone_4","T1_Causeway")),
  (Ramp("T2_Ramp_L2_L3",    ( 6.0, 6.5, 125.6), 4.0, 5.83, 1.5, 329), ("T2_L2","T2_L3")),
  (Ramp("L8L9_east",        ( 6.0,15.5, 125.6), 4.0, 5.83, 1.5, 329), ("T2_L8","T2_L9")),
  (Ramp("L8L9_west",        ( 5.6,15.5, 126.2), 4.0, 7.29, 1.5, 297), ("T2_L8","T2_L9")),
  (Ramp("L8L9_west2",       ( 6.0,15.5, 126.0), 4.0, 6.60, 1.5, 305), ("T2_L8","T2_L9")),
]
for r, ig in CANDS: test(r, ig)
