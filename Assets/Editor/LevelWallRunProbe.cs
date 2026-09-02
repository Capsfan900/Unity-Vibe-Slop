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
    /// The verbose companion to <see cref="LevelArcReport"/>'s wall-run section: for ONE authored line it
    /// prints every mount the search found and, for each, where every leave time puts the player — exit
    /// point, what was landed on or hit, and where. <c>AnalyzeWallRunGap</c> answers "does a route exist";
    /// this answers "why not", which is the question you actually have when it says no.
    ///
    /// <para>Headless: <c>-executeMethod VibeGame1.EditorTools.LevelWallRunProbe.Run</c>, with the line's
    /// index in the span's <c>WallRunLines</c> in the environment variable <c>VIBE_WALLRUN_LINE</c>
    /// (the span itself in <c>VIBE_WALLRUN_SPAN</c>, default 1)
    /// (default 0) and the entry speed in <c>VIBE_WALLRUN_SPEED</c> (default: sprint). Writes
    /// <c>LevelWallRunProbe.txt</c> beside the project.</para>
    /// </summary>
    public static class LevelWallRunProbe
    {
        /// <summary>Which span's line table <c>VIBE_WALLRUN_LINE</c> indexes: 1 (default), 2 or 3, from the
        /// environment variable <c>VIBE_WALLRUN_SPAN</c>.</summary>
        public static int Span
        {
            get
            {
                int s = 1;
                string e = Environment.GetEnvironmentVariable("VIBE_WALLRUN_SPAN");
                if (!string.IsNullOrEmpty(e)) int.TryParse(e, out s);
                return s;
            }
        }

        public static void Run()
        {
            int idx = 0; float speed = -1f;
            string e = Environment.GetEnvironmentVariable("VIBE_WALLRUN_LINE");
            if (!string.IsNullOrEmpty(e)) int.TryParse(e, out idx);
            e = Environment.GetEnvironmentVariable("VIBE_WALLRUN_SPEED");
            if (!string.IsNullOrEmpty(e)) float.TryParse(e, System.Globalization.NumberStyles.Float,
                                                          System.Globalization.CultureInfo.InvariantCulture, out speed);

            string report = Build(LevelArcReport.DefaultLevel, idx, speed);
            string path = Path.Combine(Directory.GetCurrentDirectory(), "LevelWallRunProbe.txt");
            File.WriteAllText(path, report);
            Debug.Log("\n" + report);
        }

        /// <summary>Which span's lines <c>VIBE_WALLRUN_LINE</c> indexes: <see cref="Span"/>, default 1.</summary>
        static LevelSpan1Report.WallRunLine[] LinesForSpan()
        {
            switch (Span)
            {
                case 2: return LevelSpan2Report.WallRunLines;
                case 3: return LevelSpan3Report.WallRunLines;
                default: return LevelSpan1Report.WallRunLines;
            }
        }

        public static string Build(string levelPath, int lineIndex, float maxSpeed)
        {
            var sb = new StringBuilder();
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(levelPath);
            if (def == null) return "FAIL: " + levelPath + " not found.";
            A.MoveProfile p; string err;
            if (!A.TryLoadProfile(out p, out err)) return "FAIL: " + err;
            if (maxSpeed <= 0f) maxSpeed = p.groundSpeed;

            var all = A.BoxesFrom(def);
            float floorY = def.killZone.center.y;
            var lines = LinesForSpan();
            var line = lines[Mathf.Clamp(lineIndex, 0, lines.Length - 1)];
            sb.AppendLine("PROBE " + line.from + " -> " + line.wall + " -> " + line.to + " at max entry speed " + maxSpeed.ToString("0.0"));

            int ia0 = A.IndexOf(all, line.from), iw0 = A.IndexOf(all, line.wall), ib0 = A.IndexOf(all, line.to);
            if (ia0 < 0 || iw0 < 0 || ib0 < 0) return sb.AppendLine("missing box").ToString();
            A.Box a = all[ia0], w = all[iw0], b = all[ib0];
            Vector3 lo = Vector3.Min(Vector3.Min(a.min, b.min), w.min) - new Vector3(14f, 10f, 14f);
            Vector3 hi = Vector3.Max(Vector3.Max(a.max, b.max), w.max) + new Vector3(14f, 24f, 14f);
            var boxes = A.Near(all, lo, hi);
            int ia = A.IndexOf(boxes, line.from), iw = A.IndexOf(boxes, line.wall), ib = A.IndexOf(boxes, line.to);

            // The same sampling AnalyzeWallRunGap uses, restated so this prints what it saw.
            float inset = p.SweptRadius + 0.05f;
            var launch = new List<Vector3>();
            bool region = line.launchHi.x > line.launchLo.x && line.launchHi.z > line.launchLo.z;
            float x0 = a.min.x + inset, x1 = a.max.x - inset, z0 = a.min.z + inset, z1 = a.max.z - inset;
            if (region)
            {
                x0 = Mathf.Max(x0, line.launchLo.x); x1 = Mathf.Min(x1, line.launchHi.x);
                z0 = Mathf.Max(z0, line.launchLo.z); z1 = Mathf.Min(z1, line.launchHi.z);
            }
            else
            {
                if (b.min.z >= a.max.z) z0 = Mathf.Max(z0, a.max.z - 6f);
                else if (b.max.z <= a.min.z) z1 = Mathf.Min(z1, a.min.z + 6f);
                if (b.min.x >= a.max.x) x0 = Mathf.Max(x0, a.max.x - 6f);
                else if (b.max.x <= a.min.x) x1 = Mathf.Min(x1, a.min.x + 6f);
            }
            for (int i = 0; i < 5; i++) for (int j = 0; j < 5; j++)
                launch.Add(new Vector3(Mathf.Lerp(x0, x1, i / 4f), a.max.y, Mathf.Lerp(z0, z1, j / 4f)));

            var aims = new List<Vector3>();
            foreach (var bx in new[] { b, w })
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
                    aims.Add(new Vector3(Mathf.Lerp(bx.min.x + inset, bx.max.x - inset, i / 2f), bx.max.y,
                                         Mathf.Lerp(bx.min.z + inset, bx.max.z - inset, j / 2f)));
            float[] speeds = { maxSpeed * 0.45f, maxSpeed * 0.65f, maxSpeed * 0.85f, maxSpeed };
            float[] leaves = { 0.20f, 0.45f, 0.70f, 0.95f, 1.20f, 1.45f, float.MaxValue };

            var seen = new HashSet<string>();
            int mounts = 0;
            foreach (var lf in launch)
            {
                if (!A.StandFree(lf, p, boxes, ia)) continue;
                foreach (var am in aims)
                {
                    Vector3 dir = am - lf; dir.y = 0f;
                    if (dir.sqrMagnitude < 0.25f) continue;
                    dir.Normalize();
                    foreach (var sp in speeds)
                    {
                        Vector3 vel = dir * sp + Vector3.up * p.JumpTakeoffSpeed;
                        var probe = A.FlyWallRun(boxes, p, lf, vel, iw, ia, float.MaxValue, floorY);
                        if (!probe.entered) continue;
                        string key = probe.entryFeet.x.ToString("0.0") + "/" + probe.entryFeet.z.ToString("0.0") + "/" +
                                     probe.entryFeet.y.ToString("0.0") + "/" + probe.entryVel.magnitude.ToString("0");
                        if (!seen.Add(key)) continue;
                        mounts++;
                        sb.AppendLine(string.Format("MOUNT from ({0:0.0},{1:0.0}) aim ({2:0.0},{3:0.0}) at {4:0.0} m/s: enters at ({5:0.0},{6:0.0},{7:0.0}) after {8:0.00} s, along {9:0.0} m/s",
                            lf.x, lf.z, am.x, am.z, sp, probe.entryFeet.x, probe.entryFeet.y, probe.entryFeet.z, probe.timeToEntry,
                            new Vector2(probe.entryVel.x, probe.entryVel.z).magnitude));
                        foreach (var lv in leaves)
                        {
                            var r = A.FlyWallRun(boxes, p, lf, vel, iw, ia, lv, floorY);
                            string what = r.landed ? "LANDS on " + boxes[r.landedOn].name
                                        : r.blocked ? "HITS " + boxes[r.blockedBy].name : "falls out";
                            Vector3 end = r.landed ? r.landingFeet : r.exitFeet;
                            sb.AppendLine(string.Format("    leave {0}: ran {1:0.00} s / {2:0.0} m ({3}), exit at ({4:0.0},{5:0.0},{6:0.0}) -> {7} at ({8:0.0},{9:0.0},{10:0.0})",
                                lv >= 1e8f ? "end " : lv.ToString("0.00"), r.runDuration, r.runDistance, r.ended,
                                r.exitFeet.x, r.exitFeet.y, r.exitFeet.z, what, end.x, end.y, end.z));
                        }
                    }
                }
            }
            sb.AppendLine(mounts + " distinct mounts");
            return sb.ToString();
        }
    }
}
