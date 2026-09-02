using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;
using WallRunLine = VibeGame1.EditorTools.LevelSpan1Report.WallRunLine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// <b>Span 3 — the wall-run lines of The Long Span, from the Knight's arena to the Spellsword's, flown.</b>
    ///
    /// <para>The Span 1 pattern (<see cref="LevelSpan1Report"/>) applied to the third span: two trimmed
    /// faces on the RIGHT, both running north, both optional, both faster than the base route they stand
    /// beside — which is left exactly as it was. <c>LevelArcReport</c> proves that base route hop by hop;
    /// this proves the walls do what they claim: a player who mounts them RUNS them (author against
    /// <c>longest</c>, never <c>best</c>), the landing cannot be reached without the wall, and the landing
    /// rejoins the course with a plain hop.</para>
    ///
    /// <para>Headless: <c>-executeMethod VibeGame1.EditorTools.LevelSpan3Report.Run</c>. Writes
    /// <c>LevelSpan3Report.txt</c> beside the project. <c>LevelSpan3Tests</c> asserts the same table.</para>
    /// </summary>
    public static class LevelSpan3Report
    {
        /// <summary>The two walls, south to north.</summary>
        public static readonly string[] Walls = { "T3_Wall_Pillars", "T3_Wall_Span" };

        /// <summary>
        /// The span's wall-run lines. The pillar wall skips the whole four-pillar climb; the span wall skips
        /// the grunt, the heavy and the fallen lintel without touching the deck.
        /// </summary>
        public static readonly WallRunLine[] WallRunLines =
        {
            new WallRunLine("T3_Entry", "T3_Wall_Pillars", "T3_Wall_Landing_S", "T3_Span",
                "THE PILLAR LINE. 16 m of wall on the right of the four pillars; mount it off the entry pad, " +
                "skip every pillar and land beside the south end of the span"),
            // Mounted from the PILLAR LINE'S LANDING, not from the deck: the obelisks put the face 5.5 m
            // out from a deck 4 m wide, and no jump off it reaches the wall at a legal approach angle
            // (every arc from the fourth pillar or the deck's south end lands on the deck first). The pad
            // sits right under the face, so the two lines chain: one right-hand route the whole span long.
            // The landing is the FIRST STEP itself: the exit throws 7 m/s west, and a 1.2 s run's exit arc
            // lands 4.5 m out at z 241 — which is T3_Step_1. A pad beside the step was flown and removed:
            // every arc overflew it. The second east obelisk is the leave window's far edge — leave at
            // 1.2 s from a mount past z 219 and you clear it; leave from an early mount and you ride into it.
            new WallRunLine("T3_Wall_Landing_S", "T3_Wall_Span", "T3_Step_1", "T3_Step_2",
                "THE SPAN LINE. 20 m of wall outside the east obelisks, mounted off the pillar line's landing: " +
                "past the grunt, the heavy and the lintel without touching the deck, exiting onto the first step"),
        };

        public const float MinWallLength = LevelSpan1Report.MinWallLength;

        [MenuItem("VibeGame1/Span 3 Wall-Run Report", priority = 304)]
        public static void Menu() { Run(); }

        public static void Run()
        {
            string report = Build(LevelArcReport.DefaultLevel);
            string path = Path.Combine(Directory.GetCurrentDirectory(), "LevelSpan3Report.txt");
            File.WriteAllText(path, report);
            Debug.Log("\n" + report);
            Console.WriteLine(report);
        }

        /// <summary>The base hops the walls stand beside. Their clean take-off counts are printed so the
        /// floors in <c>LevelSpan3Tests</c> are set from measurement, not hope.</summary>
        public static readonly string[][] NeighbouringHops =
        {
            new[] { "T3_Entry", "T3_Pillar_1" }, new[] { "T3_Pillar_1", "T3_Pillar_2" },
            new[] { "T3_Pillar_2", "T3_Pillar_3" }, new[] { "T3_Pillar_3", "T3_Pillar_4" },
            new[] { "T3_Pillar_4", "T3_Span" }, new[] { "T3_Span", "T3_Step_1" },
            new[] { "T3_Step_1", "T3_Step_2" }, new[] { "T3_Step_2", "T3_Step_3" },
        };

        public static string Build(string levelPath)
        {
            var sb = new StringBuilder();
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(levelPath);
            if (def == null) return "FAIL: " + levelPath + " not found.";
            A.MoveProfile p; string err;
            if (!A.TryLoadProfile(out p, out err)) return "FAIL: " + err;

            var boxes = A.BoxesFrom(def);
            float floorY = def.killZone.center.y;

            sb.AppendLine("SPAN 3 WALL-RUN REPORT — " + def.displayName);
            var env = A.MeasureWallRun(p, p.groundSpeed, true);
            var scrape = A.MeasureWallRun(p, p.WallRun.minEntrySpeed, false);
            sb.AppendLine("  sprint entry:  " + env.Summary());
            sb.AppendLine("  minimum entry: " + scrape.Summary());
            sb.AppendLine();

            sb.AppendLine("THE WALLS");
            foreach (var name in Walls)
            {
                int i = A.IndexOf(boxes, name);
                if (i < 0) { sb.AppendLine("  " + name + ": MISSING"); continue; }
                var w = boxes[i];
                float len = w.max.z - w.min.z;
                sb.AppendLine(string.Format("  {0}: face x {1:0.0}, z {2:0.0} -> {3:0.0} ({4:0.0} m of wall), top y {5:0.0}   {6}",
                    name, w.min.x, w.min.z, w.max.z, len, w.max.y, len >= MinWallLength ? "long enough for a full run" : "TOO SHORT for a full run"));
            }
            sb.AppendLine();

            int fails = 0;
            sb.AppendLine("THE LINES  (author against `longest`, never `best`)");
            for (int i = 0; i < WallRunLines.Length; i++)
            {
                if (!LevelSpan1Report.AppendWallRunLine(sb, boxes, p, floorY, WallRunLines[i])) fails++;
                sb.AppendLine();
            }

            sb.AppendLine("THE BASE HOPS BESIDE THEM  (clean take-off points; the floors in LevelSpan3Tests come from here)");
            foreach (var hop in NeighbouringHops)
            {
                var v = A.AnalyzeHop(boxes, hop[0], hop[1], p, p.groundSpeed, floorY);
                sb.AppendLine("  " + v.Summary());
            }
            if (A.IndexOf(boxes, "T3_Fallen_Lintel") >= 0)
                sb.AppendLine("  " + A.CheckLintel(boxes, "T3_Fallen_Lintel", "T3_Span", p).Summary("T3_Fallen_Lintel", "T3_Span"));
            sb.AppendLine();

            sb.AppendLine(fails == 0 ? "VERDICT: every span-3 line has a clean, long route from a sprint entry."
                                     : "VERDICT: " + fails + " line(s) FAIL.");
            return sb.ToString();
        }
    }
}
