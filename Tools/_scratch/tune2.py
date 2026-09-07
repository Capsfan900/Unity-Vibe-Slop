import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tune import test, Ramp
CANDS = [
  (Ramp("L8L9_short_narrow", (6.5,15.5,125.6), 3.0, 5.45, 1.5, 313), ("T2_L8","T2_L9")),
  (Ramp("L8L9_short_n2",     (7.0,15.5,125.2), 3.0, 5.60, 1.5, 318), ("T2_L8","T2_L9")),
  (Ramp("T2_Ramp_L7_L8",     (7.0,14.0,117.0), 4.0, 5.00, 1.5,   0), ("T2_L7","T2_L8")),
  (Ramp("T2_Ramp_L1_L2",     (7.2,5.0,117.5),  3.6, 4.50, 1.5,   0), ("T2_L1","T2_L2")),
  (Ramp("T2_Ramp_L9_L10",    (3.0,17.0,132.0), 3.5, 4.47, 1.5,  27), ("T2_L9","T2_L10")),
  (Ramp("T2_Ramp_L4_L5",     (-7.0,9.5,122.0), 4.0, 5.00, 1.5, 180), ("T2_L4","T2_L5")),
]
for r, ig in CANDS: test(r, ig)
