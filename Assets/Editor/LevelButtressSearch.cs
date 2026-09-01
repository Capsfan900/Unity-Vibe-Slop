using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Picks the numbers for the buttress fin on The Ascent, instead of guessing them.
    ///
    /// <para>BACKLOG §5b proposed one fin and could not ship it, because the fin stands where the
    /// <c>T2_L2 → T2_L3</c> arc passes and nothing measured that. This sweeps a family of candidate fins
    /// through <see cref="LevelArcAnalyzer"/> and scores each one on the two things that matter: how much
    /// of the existing hop survives, and whether the chimney it forms can actually be climbed to the exit
    /// ledge. It is a search, run once, whose ANSWER is what goes in the level asset — the level stays
    /// data, and this stays the working that produced it.</para>
    /// </summary>
    public static class LevelButtressSearch
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelArcReport.DefaultLevel);
            A.MoveProfile p; string err;
            if (def == null || !A.TryLoadProfile(out p, out err)) { Debug.Log("FAIL"); return; }

            var baseBoxes = A.BoxesFrom(def);
            float floorY = def.killZone.center.y;

            // Baseline, so every candidate is scored against what the hop is worth today.
            var before = A.AnalyzeHop(baseBoxes, "T2_L2", "T2_L3", p, p.groundSpeed, floorY);
            sb.AppendLine("BASELINE  T2_L2 -> T2_L3: " + before.Summary());
            sb.AppendLine();

            float[] finEastX = { 5.0f };                // the fin's outer face; L2's west face is x = 5.0
            float[] thickness = { 0.8f };
            float[] zCenter = { 121.8f, 122.2f, 122.6f };
            float[] zDepth = { 2.4f, 2.8f, 3.2f };
            float[] topY = { 13.9f };
            float[] l8Width = { 4f };

            sb.AppendLine("finX(min..max)   z(min..max)     top    L8w | L2->L3 clean pts | chimney | climb");
            for (int a1 = 0; a1 < finEastX.Length; a1++)
            for (int t1 = 0; t1 < thickness.Length; t1++)
            for (int z1 = 0; z1 < zCenter.Length; z1++)
            for (int d1 = 0; d1 < zDepth.Length; d1++)
            for (int y1 = 0; y1 < topY.Length; y1++)
            for (int w1 = 0; w1 < l8Width.Length; w1++)
            {
                float ex = finEastX[a1], th = thickness[t1];
                float cz = zCenter[z1], dz = zDepth[d1], ty = topY[y1], lw = l8Width[w1];

                var boxes = new List<A.Box>(baseBoxes);
                int i8 = A.IndexOf(boxes, "T2_L8");
                boxes[i8] = new A.Box("T2_L8", new Vector3(5f + lw * 0.5f, 15f, 124f), new Vector3(lw, 1f, 4f));
                boxes.Add(new A.Box("T2_Buttress",
                    new Vector3(ex - th * 0.5f, (6.5f + ty) * 0.5f, cz),
                    new Vector3(th, ty - 6.5f, dz)));

                var hop = A.AnalyzeHop(boxes, "T2_L2", "T2_L3", p, p.groundSpeed, floorY);
                var g = A.MeasureChimney(boxes, "T2_Tower", "T2_Buttress", p);

                var climb = BestClimb(boxes, p, floorY, "T2_L2", "T2_L8");

                sb.AppendLine(string.Format(
                    "{0,4:0.0}..{1,4:0.0}  {2,6:0.0}..{3,6:0.0}  {4,5:0.0}  {5,3:0.0} | {6,2}/{7,2} pts, {8,3} arcs | " +
                    "w {9:0.00} d {10:0.00} {11} | {12}",
                    ex - th, ex, cz - dz * 0.5f, cz + dz * 0.5f, ty, lw,
                    hop.cleanLaunchPoints, hop.launchPoints, hop.cleanArcs,
                    g.width, g.depth, g.valid ? "ok " : "BAD",
                    climb));
            }

            string path = Path.Combine(Directory.GetCurrentDirectory(), "ButtressSearch.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("\n" + sb);
        }

        static string BestClimb(IList<A.Box> boxes, A.MoveProfile p, float floorY, string entryLedge, string exitLedge)
        {
            int ie = A.IndexOf(boxes, entryLedge);
            int ifin = A.IndexOf(boxes, "T2_Buttress");
            if (ie < 0 || ifin < 0) return "no entry";
            var entry = boxes[ie];
            var fin = boxes[ifin];
            float inset = p.SweptRadius + 0.05f;

            A.ClimbOutcome best = new A.ClimbOutcome();
            best.peakY = -999f; best.landedOn = "(fell)";
            int successes = 0;
            var where = new Dictionary<string, int>();

            for (int zi = 0; zi < 6; zi++)
            {
                float z = Mathf.Lerp(Mathf.Max(entry.min.z, fin.min.z - 2f) + inset,
                                     Mathf.Min(entry.max.z, fin.max.z + 2f) - inset, zi / 5f);
                if (z < entry.min.z + inset || z > entry.max.z - inset) continue;
                for (int ai = 0; ai < 6; ai++)
                {
                    float deg = Mathf.Lerp(-20f, 30f, ai / 5f);
                    float ang = deg * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(-Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 feet = new Vector3(entry.min.x + inset, entry.max.y, z);
                    if (!A.StandFree(feet, p, boxes, ie)) continue;
                    var o = A.ClimbChimney(boxes, p, feet, dir * p.groundSpeed + Vector3.up * p.JumpTakeoffSpeed,
                                           exitLedge, entryLedge, floorY);
                    if (o.success) successes++;
                    int n; where.TryGetValue(o.landedOn, out n); where[o.landedOn] = n + 1;
                    if (o.success && (!best.success || o.pushes < best.pushes)) best = o;
                    else if (!best.success && o.peakY > best.peakY) best = o;
                }
            }

            var hist = new StringBuilder();
            foreach (var kv in where) hist.Append(kv.Key).Append(" x").Append(kv.Value).Append("  ");
            return best.success
                ? string.Format("CLIMBS in {0} pushes, peak {1:0.0} ({2} entries work) [{3}]", best.pushes, best.peakY, successes, hist)
                : string.Format("no climb (best peak {0:0.0}) [{1}]", best.peakY, hist);
        }
    }
}
