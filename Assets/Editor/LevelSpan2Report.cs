using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;
using S1 = VibeGame1.EditorTools.LevelSpan1Report;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// <b>Span 2 — the wall-run lines up The Ascent, between the Ninja arena and the Knight, flown.</b>
    ///
    /// <para>The Ascent is a spiral of eleven 4 m ledges around a 5 m tower, 1.5 m of rise per ledge. A
    /// wall run DESCENDS over its length (a full sprint run nets −2.24 m), so the only honest shape here
    /// is a line that skips the two ledges of one leg by running the outside of the spiral and landing on
    /// a pad level with the leg's top — the run buys distance, the exit jump buys the height back
    /// (+3 m from a late leave, measured). Two walls, one per flank of the first loop, each read exactly
    /// as the teaching span taught: a trimmed face on your right is a run line. <c>T2_Wall_East</c> is
    /// run north off T2_L1 past T2_L2; <c>T2_Wall_West</c> is run south off T2_L4 past T2_L5 and the
    /// grunt on it.</para>
    ///
    /// <para>Same bar as <see cref="LevelSpan1Report"/>: author against <c>longest</c>, never <c>best</c>;
    /// the landing must be out of reach without the wall, and must rejoin the spiral with a plain hop.
    /// Headless: <c>-executeMethod VibeGame1.EditorTools.LevelSpan2Report.Run</c>. Writes
    /// <c>LevelSpan2Report.txt</c> beside the project. <c>LevelSpan2Tests</c> asserts the same table.</para>
    /// </summary>
    public static class LevelSpan2Report
    {
        /// <summary>The span's walls, one per flank of the first loop. Each is on the RIGHT of the player
        /// running it: east running north, west running south.</summary>
        public static readonly string[] Walls = { "T2_Wall_East", "T2_Wall_West" };

        /// <summary>
        /// The span's wall-run lines. Each mounts its wall from the ledge that hugs it (1.1–1.6 m off the
        /// run line — a deck any further off cannot reach the face before the arc falls below it, which
        /// is why there is no line off <c>T2_Entry</c>), runs the length of a leg and lands +3 m up on a pad
        /// level with the ledge two rungs on, which it then hops onto.
        /// </summary>
        public static readonly S1.WallRunLine[] WallRunLines =
        {
            new S1.WallRunLine("T2_L1", "T2_Wall_East", "T2_Wall_Landing_East", "T2_L3",
                "THE EAST LINE. Off T2_L1 onto the curtain on your right, run north past T2_L2 and land " +
                "level with T2_L3: the first leg of the spiral in one run")
                .From(5f, 114f, 9f, 118f),
            new S1.WallRunLine("T2_L4", "T2_Wall_West", "T2_Wall_Landing_West", "T2_L6",
                "THE WEST LINE. Off T2_L4 onto the wall on your right, run south past T2_L5 (and the grunt " +
                "standing on it) and land level with T2_L6: the second leg, and the fight, skipped")
                .From(-9f, 122f, -5f, 126f),
        };

        [MenuItem("VibeGame1/Span 2 Wall-Run Report", priority = 303)]
        public static void Menu() { Run(); }

        public static void Run()
        {
            string report = Build(LevelArcReport.DefaultLevel);
            string path = Path.Combine(Directory.GetCurrentDirectory(), "LevelSpan2Report.txt");
            File.WriteAllText(path, report);
            Debug.Log("\n" + report);
            Console.WriteLine(report);
        }

        public static string Build(string levelPath)
        {
            var sb = new StringBuilder();
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(levelPath);
            if (def == null) return "FAIL: " + levelPath + " not found.";
            A.MoveProfile p; string err;
            if (!A.TryLoadProfile(out p, out err)) return "FAIL: " + err;

            var boxes = A.BoxesFrom(def);
            float floorY = def.killZone.center.y;

            sb.AppendLine("SPAN 2 WALL-RUN REPORT — " + def.displayName);
            var env = A.MeasureWallRun(p, p.groundSpeed, true);
            var scrape = A.MeasureWallRun(p, p.WallRun.minEntrySpeed, false);
            sb.AppendLine("  sprint entry:  " + env.Summary());
            sb.AppendLine("  minimum entry: " + scrape.Summary());
            sb.AppendLine();

            sb.AppendLine("THE WALL");
            foreach (var name in Walls)
            {
                int i = A.IndexOf(boxes, name);
                if (i < 0) { sb.AppendLine("  " + name + ": MISSING"); continue; }
                var w = boxes[i];
                float len = w.max.z - w.min.z;
                sb.AppendLine(string.Format("  {0}: face x {1:0.0}, z {2:0.0} -> {3:0.0} ({4:0.0} m of wall), y {5:0.0} -> {6:0.0}   {7}",
                    name, w.min.x, w.min.z, w.max.z, len, w.min.y, w.max.y,
                    len >= S1.MinWallLength ? "long enough for a full run" : "TOO SHORT for a full run"));
            }
            sb.AppendLine();

            int fails = 0;
            sb.AppendLine("THE LINES  (author against `longest`, never `best`)");
            for (int i = 0; i < WallRunLines.Length; i++)
            {
                if (!S1.AppendWallRunLine(sb, boxes, p, floorY, WallRunLines[i])) fails++;
                sb.AppendLine();
            }

            sb.AppendLine("THE SPIRAL BESIDE THE WALL  (base hops, unchanged)");
            string[][] hops =
            {
                new[] { "T2_Entry", "T2_L1" }, new[] { "T2_L1", "T2_L2" }, new[] { "T2_L2", "T2_L3" },
                new[] { "T2_L3", "T2_L4" }, new[] { "T2_L4", "T2_L5" }, new[] { "T2_L5", "T2_L6" },
                new[] { "T2_L6", "T2_L7" }, new[] { "T2_L7", "T2_L8" }, new[] { "T2_L8", "T2_L9" },
                new[] { "T2_L9", "T2_L10" },
            };
            foreach (var h in hops)
            {
                var v = A.AnalyzeHop(boxes, h[0], h[1], p, p.groundSpeed, floorY);
                sb.AppendLine("  " + v.Summary());
            }
            sb.AppendLine();

            sb.AppendLine(fails == 0 ? "VERDICT: every span-2 line has a clean, long route from a sprint entry."
                                     : "VERDICT: " + fails + " line(s) FAIL.");
            return sb.ToString();
        }
    }
}
