using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// <b>Span 1 — the wall-run lines between the start pad and the Ninja arena, flown.</b>
    ///
    /// <para>The companion to <see cref="LevelArcReport"/> for the parkour added to the opening span.
    /// <c>LevelArcReport</c> proves the BASELINE route hop by hop; this proves the two walls on the right
    /// of it do what they claim — that a player who mounts them RUNS them (author against <c>longest</c>,
    /// never <c>best</c>: a landing reached only by hopping off at 0.2 s is a wall jump in a costume),
    /// that the landing cannot be reached without the wall (or the tech buys nothing), and that the landing
    /// rejoins the course with a plain hop (or it is a second gate).</para>
    ///
    /// <para>Headless: <c>-executeMethod VibeGame1.EditorTools.LevelSpan1Report.Run</c>. Writes
    /// <c>LevelSpan1Report.txt</c> beside the project. <c>LevelSpan1Tests</c> asserts the same table.</para>
    /// </summary>
    public static class LevelSpan1Report
    {
        /// <summary>One authored wall run: leave <c>from</c>, mount <c>wall</c>, arrive on <c>to</c>.</summary>
        public struct WallRunLine
        {
            public string from, wall, to;
            /// <summary>The base hop the landing continues with, so the line is never a second gate.</summary>
            public string rejoin;
            public string purpose;
            /// <summary>Where on <c>from</c> the player actually takes off (x/z, world). Zero = let the
            /// analyser pick the band facing the target — wrong for a wall beside a long deck, where that
            /// band is at the far end and the only way to mount the wall from it is backwards.</summary>
            public Vector3 launchLo, launchHi;
            /// <summary>False for a secondary mount of a wall already proven elsewhere: reported, not
            /// held to the LONG-run bar, because a mid-wall mount is short by construction.</summary>
            public bool mustBeLong;

            public WallRunLine(string from, string wall, string to, string rejoin, string purpose)
            {
                this.from = from; this.wall = wall; this.to = to; this.rejoin = rejoin; this.purpose = purpose;
                launchLo = launchHi = Vector3.zero; mustBeLong = true;
            }
            public WallRunLine From(float x0, float z0, float x1, float z1)
            { var c = this; c.launchLo = new Vector3(x0, 0f, z0); c.launchHi = new Vector3(x1, 0f, z1); return c; }
            public WallRunLine Secondary() { var c = this; c.mustBeLong = false; return c; }
        }

        /// <summary>
        /// The span's wall-run lines. Both walls stand on the RIGHT of the course, both run north, both
        /// are optional: a faster line past a slower base route that stays exactly as it was.
        /// </summary>
        public static readonly WallRunLine[] WallRunLines =
        {
            new WallRunLine("Ground_Start", "T1_Wall_Start", "T1_Stone_4", "T1_Causeway",
                "THE OPENING LINE. 20 m of wall on the right of the stepping stones, visible from spawn; " +
                "mount it off the start pad and skip all four stones"),
            new WallRunLine("T1_Stone_4", "T1_Wall_Causeway", "T1_Wall_Landing", "T1_Stone_5",
                "THE CAUSEWAY WALL. 16 m beside the causeway: past both grunts and the fallen obelisk " +
                "without touching the deck, landing on the ledge at its end"),
            new WallRunLine("T1_Causeway", "T1_Wall_Causeway", "T1_Wall_Landing", "T1_Stone_5",
                "the causeway wall mounted late, over the east rail, once the deck gets crowded")
                .From(-1.5f, 43f, 1.5f, 50f).Secondary(),
        };

        /// <summary>The wall lengths the lines are sized to. A sprint run covers 13.5–17.6 m of wall.</summary>
        public const float MinWallLength = 14.5f;   // a released 11 m/s entry covers 14.4 m at 1.75 s

        [MenuItem("VibeGame1/Span 1 Wall-Run Report", priority = 302)]
        public static void Menu() { Run(); }

        public static void Run()
        {
            string report = Build(LevelArcReport.DefaultLevel);
            string path = Path.Combine(Directory.GetCurrentDirectory(), "LevelSpan1Report.txt");
            File.WriteAllText(path, report);
            Debug.Log("\n" + report);
            Console.WriteLine(report);
        }

        /// <summary>A scraping entry: the slowest ladder whose top rung can still clear the entry floor.</summary>
        public static float ScrapingEntrySpeed(A.MoveProfile p) { return p.WallRun.minEntrySpeed + 1f; }

        static readonly A.AirControl[] WithBraking = { A.AirControl.None, A.AirControl.Brake };

        public static string Build(string levelPath)
        {
            var sb = new StringBuilder();
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(levelPath);
            if (def == null) return "FAIL: " + levelPath + " not found.";
            A.MoveProfile p; string err;
            if (!A.TryLoadProfile(out p, out err)) return "FAIL: " + err;

            var boxes = A.BoxesFrom(def);
            float floorY = def.killZone.center.y;

            sb.AppendLine("SPAN 1 WALL-RUN REPORT — " + def.displayName);
            var env = A.MeasureWallRun(p, p.groundSpeed, true);
            var scrape = A.MeasureWallRun(p, p.WallRun.minEntrySpeed, false);
            sb.AppendLine("  sprint entry:  " + env.Summary());
            sb.AppendLine("  minimum entry: " + scrape.Summary());
            sb.AppendLine();

            sb.AppendLine("THE WALLS");
            foreach (var name in new[] { "T1_Wall_Start", "T1_Wall_Causeway" })
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
                if (!AppendWallRunLine(sb, boxes, p, floorY, WallRunLines[i])) fails++;
                sb.AppendLine();
            }

            sb.AppendLine(fails == 0 ? "VERDICT: every span-1 line has a clean, long route from a sprint entry."
                                     : "VERDICT: " + fails + " line(s) FAIL.");
            return sb.ToString();
        }

        /// <summary>
        /// One line, three ways in, plus the two facts that make it a LINE rather than a decoration: the
        /// landing is out of reach without the wall, and the landing rejoins the course. Returns false when
        /// a line that must be long has no clean, long route from a sprint entry.
        /// </summary>
        public static bool AppendWallRunLine(StringBuilder sb, IList<A.Box> boxes, A.MoveProfile p, float floorY, WallRunLine line)
        {
            sb.AppendLine("  " + line.from + " -> " + line.wall + " -> " + line.to + ":  " + line.purpose);
            var sprint = A.AnalyzeWallRunGap(boxes, line.from, line.wall, line.to, p, p.groundSpeed, floorY, line.launchLo, line.launchHi);
            var slide = A.AnalyzeWallRunGap(boxes, line.from, line.wall, line.to, p, p.SlideJumpSpeed, floorY, line.launchLo, line.launchHi);
            var scrape = A.AnalyzeWallRunGap(boxes, line.from, line.wall, line.to, p, ScrapingEntrySpeed(p), floorY, line.launchLo, line.launchHi);
            sb.AppendLine("    sprint    " + sprint.Summary());
            sb.AppendLine("    slide-j   " + slide.Summary());
            sb.AppendLine("    scraping  " + scrape.Summary());
            if (sprint.exists)
                sb.AppendLine(string.Format("    LONGEST arriving run (sprint): {0:0.00} s / {1:0.0} m on the wall, leave at {2}, " +
                                            "entered at {3:0.0} m/s along the face at z {4:0.0}, lands at ({5:0.0}, {6:0.0}, {7:0.0})",
                                            sprint.longest.runDuration, sprint.longest.runDistance,
                                            sprint.longestLeaveAt >= 1e8f ? "the end (rides it out)" : sprint.longestLeaveAt.ToString("0.00") + " s",
                                            new Vector2(sprint.longest.entryVel.x, sprint.longest.entryVel.z).magnitude,
                                            sprint.longest.entryFeet.z,
                                            sprint.longest.landingFeet.x, sprint.longest.landingFeet.y, sprint.longest.landingFeet.z));

            var noWallBase = A.AnalyzeHop(boxes, line.from, line.to, p, p.groundSpeed, floorY, WithBraking);
            var noWallSlide = A.AnalyzeHop(boxes, line.from, line.to, p, p.SlideJumpSpeed, floorY, WithBraking);
            sb.AppendLine("    without the wall: base " + (noWallBase.exists ? "REACHES IT (not gated)" : "cannot") +
                          ", slide-jump " + (noWallSlide.exists ? "REACHES IT (not gated)" : "cannot") +
                          "  (gap " + noWallBase.gap.ToString("0.0") + " m)");
            var rejoin = A.AnalyzeHop(boxes, line.to, line.rejoin, p, p.groundSpeed, floorY);
            sb.AppendLine("    rejoin " + line.to + " -> " + line.rejoin + ": " + (rejoin.exists ? "ok" : "FAIL") +
                          " (" + rejoin.cleanLaunchPoints + "/" + rejoin.launchPoints + " take-off points)");

            bool ok = sprint.exists && sprint.cleanRoutes >= 3 && (!line.mustBeLong || sprint.longest.runDuration >= 1.0f);
            sb.AppendLine("    " + (ok ? (line.mustBeLong ? "OK" : "OK (secondary mount; short by construction)")
                                       : "FAIL — no clean" + (line.mustBeLong ? ", LONG" : "") + " route from a sprint entry"));
            return ok;
        }
    }
}
