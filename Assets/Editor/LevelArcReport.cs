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
    /// Flies <see cref="LevelArcAnalyzer"/> over every authored traversal in a
    /// <see cref="LevelDefinition"/> and prints what it finds.
    ///
    /// <para>Two entry points, deliberately. <see cref="Menu"/> is the editor button; <see cref="Run"/> is
    /// the same thing under <c>-batchmode -executeMethod VibeGame1.EditorTools.LevelArcReport.Run</c>, so
    /// the level's traversability can be checked on a copy of the project while the editor is busy. The
    /// numbers are the deliverable: gap, rise, how many of the sampled arcs arrive, and the clearance of
    /// the best one. "It probably still works" is not an answer this tool can give.</para>
    /// </summary>
    public static class LevelArcReport
    {
        public const string DefaultLevel = "Assets/Data/Levels/Level_01_Level.asset";

        /// <summary>Every hop the level is authored to contain, and the moveset it is meant to need.</summary>
        public struct Route
        {
            public string from, to, moveset;
            public Route(string moveset, string from, string to) { this.moveset = moveset; this.from = from; this.to = to; }
        }

        /// <summary>
        /// The baseline route, end to end. This list mirrors <c>FeatureTests > LevelStructure</c>: the two
        /// must agree, and a hop that exists in one and not the other is a bug in whichever was edited last.
        /// </summary>
        public static readonly Route[] BaseRoute =
        {
            new Route("Base", "Ground_Start", "T1_Stone_1"),
            new Route("Base", "T1_Stone_1", "T1_Stone_2"),
            new Route("Base", "T1_Stone_2", "T1_Stone_3"),
            new Route("Base", "T1_Stone_3", "T1_Stone_4"),
            new Route("Base", "T1_Stone_4", "T1_Causeway"),
            new Route("Base", "T1_Causeway", "T1_Stone_5"),
            new Route("Base", "T1_Stone_5", "T1_Arena"),
            new Route("Base", "T1_Arena", "T2_Entry"),
            new Route("Base", "T2_Entry", "T2_L1"),
            new Route("Base", "T2_L1", "T2_L2"),
            new Route("Base", "T2_L2", "T2_L3"),
            new Route("Base", "T2_L3", "T2_L4"),
            new Route("Base", "T2_L4", "T2_L5"),
            new Route("Base", "T2_L5", "T2_L6"),
            new Route("Base", "T2_L6", "T2_L7"),
            new Route("Base", "T2_L7", "T2_L8"),
            new Route("Base", "T2_L8", "T2_L9"),
            new Route("Base", "T2_L9", "T2_L10"),
            new Route("Base", "T2_L10", "T2_L11"),
            new Route("Base", "T2_L11", "T2_Bridge"),
            new Route("Base", "T3_Entry", "T3_Pillar_1"),
            new Route("Base", "T3_Pillar_1", "T3_Pillar_2"),
            new Route("Base", "T3_Pillar_2", "T3_Pillar_3"),
            new Route("Base", "T3_Pillar_3", "T3_Pillar_4"),
            new Route("Base", "T3_Pillar_4", "T3_Span"),
            new Route("Base", "T3_Span", "T3_Step_1"),
            new Route("Base", "T3_Step_1", "T3_Step_2"),
            new Route("Base", "T3_Step_2", "T3_Step_3"),
            new Route("Base", "T3_Step_3", "T3_Arena"),
        };

        /// <summary>The optional, tech-gated lines. Each must be reachable WITH the tech.</summary>
        public static readonly Route[] TechRoute =
        {
            new Route("SlideJump", "T1_Stone_1", "T1_Fast_1"),
            new Route("Base", "T1_Fast_1", "T1_Stone_4"),
        };

        [MenuItem("VibeGame1/Level Arc Report", priority = 300)]
        public static void Menu()
        {
            string report = Build(DefaultLevel);
            string path = Path.Combine(Directory.GetCurrentDirectory(), "LevelArcReport.txt");
            File.WriteAllText(path, report);
            Debug.Log(report + "\nWritten to " + path);
        }

        /// <summary>Headless entry point. Writes the report next to the project and logs it.</summary>
        public static void Run()
        {
            string report = Build(DefaultLevel);
            string path = Path.Combine(Directory.GetCurrentDirectory(), "LevelArcReport.txt");
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

            sb.AppendLine("LEVEL ARC REPORT — " + def.displayName + " (" + boxes.Count + " boxes)");
            sb.AppendLine();
            sb.Append(A.Describe(p));
            sb.AppendLine();

            var braking = new[] { A.AirControl.None, A.AirControl.Brake };

            sb.AppendLine("THE BASELINE ROUTE  (base moveset, stick released — no air control assumed)");
            int fails = 0;
            for (int i = 0; i < BaseRoute.Length; i++)
            {
                var r = BaseRoute[i];
                var v = A.AnalyzeHop(boxes, r.from, r.to, p, p.groundSpeed, floorY);
                if (!v.exists) { fails++; sb.AppendLine("  FAIL  " + v.Summary()); }
                else sb.AppendLine("        " + v.Summary());
            }
            sb.AppendLine();

            sb.AppendLine("THE TECH LINES");
            for (int i = 0; i < TechRoute.Length; i++)
            {
                var r = TechRoute[i];
                float speed = r.moveset == "SlideJump" ? p.SlideJumpSpeed : p.groundSpeed;
                var v = A.AnalyzeHop(boxes, r.from, r.to, p, speed, floorY, braking);
                if (!v.exists) fails++;
                sb.AppendLine("  " + (v.exists ? "      " : "FAIL  ") + "[" + r.moveset + "] " + v.Summary());
            }
            sb.AppendLine();

            sb.AppendLine("SLIDE GATES");
            sb.AppendLine("  " + A.CheckLintel(boxes, "T1_Fallen_Obelisk", "T1_Causeway", p).Summary("T1_Fallen_Obelisk", "T1_Causeway"));
            if (A.IndexOf(boxes, "T3_Fallen_Lintel") >= 0)
                sb.AppendLine("  " + A.CheckLintel(boxes, "T3_Fallen_Lintel", "T3_Span", p).Summary("T3_Fallen_Lintel", "T3_Span"));
            sb.AppendLine();

            sb.AppendLine("WALL-JUMP LINES");
            AppendChimney(sb, boxes, p, floorY, "T2_Tower", "T2_Buttress", "T2_L2", "T2_L8");
            sb.AppendLine();

            // ---- the parkour-first pieces (LevelDefinitionAuthoring / LevelTraversalAnalyzer) ----
            // The band is the SHOOTER'S data (EnemyData.projectileMinRange/MaxRange), never a literal: the report
            // said 6-30 while the test read 10-30 after the 2026-09-05 projectile retune, and a report that lies
            // is worse than none.
            var gruntData = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy01"));
            float bandLo = gruntData != null ? gruntData.projectileMinRange : 6f;
            float bandHi = gruntData != null ? gruntData.projectileMaxRange : 30f;
            // F6 (bolt-timing plan): the cone is the PLAYER'S shipped facing cone, never a literal, for
            // the same reason the band above is the shooter's own data -- a report that invents a
            // threshold lies the moment the data is retuned.
            var statsAsset = AssetDatabase.LoadAssetAtPath<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            float coneDeg = statsAsset != null ? statsAsset.facingConeDeg : 75f;

            // Deck -> (previous, next) on the baseline route, so a covered deck can be judged against the
            // direction the player is actually travelling when they stand on it.
            System.Func<string, string[]> routeOf = deck =>
            {
                string prev = null, next = null;
                for (int i = 0; i < BaseRoute.Length; i++)
                {
                    if (BaseRoute[i].to == deck) prev = BaseRoute[i].from;
                    if (BaseRoute[i].from == deck) next = BaseRoute[i].to;
                }
                return (prev == null && next == null) ? null : new[] { prev, next };
            };

            sb.AppendLine("SHOOTER PERCHES  (a bolt across the route inside the " + bandLo + "-" + bandHi + " m band, line clear)");
            sb.AppendLine("  FORCED LOOK-AWAY = the muzzle sits more than the parry's " + coneDeg.ToString("0") +
                          " deg facing cone off the route's direction of travel, so answering the bolt means");
            sb.AppendLine("  leaving the line. Not a failure on its own -- it is a PLACEMENT fault to fix by moving the perch.");
            int lookAway = 0;
            foreach (var pc in LevelDefinitionAuthoring.Perches)
            {
                var sv = LevelTraversalAnalyzer.AnalyzeShooterPlacement(boxes, pc.name, pc.spawn, pc.covers.Split(','),
                                                                        bandLo, bandHi, coneDeg, routeOf);
                if (sv.covered.Count == 0) fails++;
                if (sv.forcedLookAway != null) lookAway += sv.forcedLookAway.Count;
                sb.AppendLine("  " + (sv.covered.Count == 0 ? "FAIL  " : "      ") + sv.Summary());
            }
            sb.AppendLine("  " + (lookAway == 0
                ? "No forced look-away: every covered deck can be answered without leaving the route."
                : lookAway + " covered deck(s) force the player off the line to answer. Move those perches."));
            sb.AppendLine();
            sb.AppendLine("BALLOON ARC  (pop = " + p.launchCarryCap + " m/s carry + the orb's launch, float " + p.launchFloatSeconds + " s)");
            if (def.balloons != null && def.balloons.Length > 0)
            {
                var cv = LevelTraversalAnalyzer.AnalyzeChain(boxes, def.balloons, "T3_Entry", "T3_Span", p, floorY);
                if (!cv.complete) fails++;
                sb.AppendLine("  " + cv.Summary());
            }
            else sb.AppendLine("  (no balloons in the definition)");
            sb.AppendLine();
            sb.AppendLine("WATER LINES  (floor speed " + p.waterFloorSpeed.ToString("0.0") + " m/s)");
            if (def.waters != null)
                foreach (var w in def.waters)
                {
                    var wv = LevelTraversalAnalyzer.AnalyzeWater(boxes, w);
                    if (!wv.onADeck) fails++;
                    sb.AppendLine("  " + (wv.onADeck ? "      " : "FAIL  ") + wv.Summary());
                }
            sb.AppendLine();

            sb.AppendLine(fails == 0
                ? "VERDICT: every authored traversal has a clean arc."
                : "VERDICT: " + fails + " traversal(s) have NO clean arc.");
            return sb.ToString();
        }

        /// <summary>
        /// Measures a chimney and then actually climbs it: entering airborne off
        /// <paramref name="entryLedge"/> and searching push timings for one that tops out on
        /// <paramref name="exitLedge"/>.
        /// </summary>
        public static void AppendChimney(StringBuilder sb, IList<A.Box> boxes, A.MoveProfile p, float floorY,
                                         string tall, string shortFace, string entryLedge, string exitLedge)
        {
            int ifin = A.IndexOf(boxes, shortFace);
            if (ifin < 0) { sb.AppendLine("  " + shortFace + ": not present — The Ascent has no authored wall-jump line."); return; }

            var g = A.MeasureChimney(boxes, tall, shortFace, p);
            sb.AppendLine(string.Format("  {0} | {1}: width {2:0.00} m, {3:0.00} m of face overlap, " +
                                        "faces top out at {4:0.0} / {5:0.0}, open sky to {6}{7}",
                                        tall, shortFace, g.width, g.depth, g.shortTop, g.tallTop,
                                        g.clearSkyTo > 1e8f ? "the top" : g.clearSkyTo.ToString("0.0"),
                                        string.IsNullOrEmpty(g.ceiling) ? "" : " (" + g.ceiling + ")"));
            sb.AppendLine("    static geometry: " + (g.valid ? "VALID" : "INVALID — " + g.reason));

            int ie = A.IndexOf(boxes, entryLedge);
            if (ie < 0) { sb.AppendLine("    entry ledge " + entryLedge + " missing"); return; }
            var entry = boxes[ie];
            var fin = boxes[ifin];

            // Enter the way a player does: run off the ledge's edge toward the chimney and jump. Sample
            // the run-off point along the edge and the aim across the corridor.
            var best = new A.ClimbOutcome();
            best.landedOn = "(nothing)"; best.peakY = -999f;
            string bestEntry = "";
            float inset = p.SweptRadius + 0.05f;

            for (int zi = 0; zi < 7; zi++)
            {
                // The whole length of the ledge edge beside the fin. The MOUTH is at the fin's far
                // end — a fin flush with the ledge's face walls the slot off everywhere it stands, so
                // you enter past its end and drift in. Sampling only "inside the slot" would report a
                // climbable chimney as unenterable.
                float z = Mathf.Lerp(Mathf.Max(entry.min.z, fin.min.z - 2f) + inset,
                                     Mathf.Min(entry.max.z, fin.max.z + 2f) - inset, zi / 6f);
                for (int ai = 0; ai < 7; ai++)
                {
                    // Aim between straight into the corridor and 45 deg along it.
                    float ang = Mathf.Lerp(-20f, 30f, ai / 6f) * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(-Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 feet = new Vector3(entry.min.x + inset, entry.max.y, z);
                    if (!A.StandFree(feet, p, boxes, ie)) continue;
                    Vector3 vel = dir * p.groundSpeed + Vector3.up * p.JumpTakeoffSpeed;
                    var o = A.ClimbChimney(boxes, p, feet, vel, exitLedge, entryLedge, floorY);
                    if (o.success && (!best.success || o.pushes < best.pushes))
                    {
                        best = o;
                        bestEntry = string.Format("off {0} at z {1:0.0}, aimed {2:0} deg north of due west", entryLedge, z, Mathf.Lerp(-20f, 30f, ai / 6f));
                    }
                    else if (!best.success && o.peakY > best.peakY) { best = o; bestEntry = string.Format("off {0} at z {1:0.0}", entryLedge, z); }
                }
            }

            if (best.success)
                sb.AppendLine(string.Format("    CLIMB: {0} -> {1} in {2} wall jumps, peak y {3:0.00}, " +
                                            "min clearance {4:0.00} m.  Entry: {5}",
                                            entryLedge, best.landedOn, best.pushes, best.peakY,
                                            best.minClearance > 1e8f ? 99f : best.minClearance, bestEntry));
            else
                sb.AppendLine(string.Format("    CLIMB FAILS: best attempt reached y {0:0.00} and ended on {1}. {2}",
                                            best.peakY, best.landedOn, best.note));
        }
    }
}
