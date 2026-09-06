using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// The parkour-first rework of <c>Level_01_Level.asset</c>, written as CODE that rewrites the asset
    /// deterministically (hard rule 9: a code default is not a shipped value; hard rule 4: everything is
    /// regenerable) rather than as hand edits to YAML. Re-runnable: every piece it owns is named, removed
    /// and re-added, so running it twice yields the same asset.
    ///
    /// <para><b>What it changes, and why (docs/BACKLOG.md §0, docs/MOVEMENT-PRINCIPLES.md).</b> The six
    /// Grunt/Heavy spawns were melee filler standing ON the route. They are now SHOOTERS on perches beside
    /// it: each stands where its bolt crosses the line the player is running (inside the shooter's 6–30 m
    /// band, with a clear line), so a perfect parry of the bolt is a speed boost on that very line — the
    /// enemy is a route piece, not a wall. A balloon ARC bypasses the T3 pillar hops for a player who can
    /// chain pops (rule 7: shapes), and two water LINES ride the fast slide deck in T1 and the T3 span,
    /// the second one turning the run toward the first step. The three legendaries and the boss are
    /// untouched: those are the fights.</para>
    /// </summary>
    public static class LevelDefinitionAuthoring
    {
        public const string Level01 = "Assets/Data/Levels/Level_01_Level.asset";

        // ---------------------------------------------------------------- the perches
        // A perch is a 3 x 1 x 3 stone shelf hung BESIDE the course, outside every wall-run wall in plan
        // (LevelSpan*Tests forbid a T*_ box overlapping a wall in plan) and outside every hop's corridor.
        // Numbers are Unity metres; the shooter stands at top + 0.1 like every other spawn.
        public struct Perch
        {
            public string name, spawn;
            public Vector3 center;      // size is 3 x 1 x 3 for all of them
            public float yaw;           // facing the route it covers
            public string covers;       // the route decks its bolts cross — for the report and the tests
            public Perch(string n, string s, Vector3 c, float y, string cov) { name = n; spawn = s; center = c; yaw = y; covers = cov; }
        }

        public static readonly Vector3 PerchSize = new Vector3(3f, 1f, 3f);

        public static readonly Perch[] Perches =
        {
            // T1: the causeway (z 43-65, top 2.0). West perch at z 44 looks down the whole causeway; the
            // east one sits NORTH of T1_Wall_Causeway's end (z 58) and covers the wall-run landing and the
            // hop onto Stone_5 - a line from the east to the causeway's centre crosses the wall (x 3.4-4.4,
            // top 6) and is blocked, measured by the arc report 2026-09-05.
            new Perch("T1_Perch_W", "Spawn_T1_GruntA", new Vector3(-7.5f, 3.5f, 44f), 90f, "T1_Causeway,T1_Stone_4"),
            // (16, 60): the projectile band's near edge moved 6 -> 10 m on 2026-09-05 (a bolt must be cued 0.28 s out
            // at 32 m/s), and the old (11, 63) perch was 6.8 m from T1_Wall_Landing. From here the landing is
            // 12.4 m and Stone_5 19 m, both lines clear of the east obelisk (z 66.5) and the causeway wall (z <= 58).
            new Perch("T1_Perch_E", "Spawn_T1_GruntB", new Vector3(16f, 3.5f, 60f), 270f, "T1_Wall_Landing,T1_Stone_5"),
            // T2: the spiral. LevelSpan2Tests pins Spawn_T2_GruntB BESIDE the west wall, inside it (x > the
            // wall's east face -10.6), between the landing pad (z 106.5) and the mount L4 (z 122), so a run
            // along the wall passes it: a low perch at z 109-112, under the L6/L5 hops (10-12 m up). GruntA
            // takes the east side, outside and above T2_Wall_East (top 10) so its bolt clears the wall.
            // Covers L1 (15.5 m) and L2 (19.8 m): after the band's near edge moved to 10 m (2026-09-05), Entry at
            // 9.3 m and L5 at 5.5 m are inside the muzzle's dead zone, so they are no longer claimed. The
            // perch itself stays where LevelSpan2Tests pins the grunt.
            new Perch("T2_Perch_W", "Spawn_T2_GruntB", new Vector3(-7.5f, 4f, 110.5f), 90f, "T2_L1,T2_L6,T2_L2"),   // L6 (10.4 m, straight up the spiral) is the second clear line; L2 is blocked from here
            new Perch("T2_Perch_E", "Spawn_T2_GruntA", new Vector3(13.8f, 10.5f, 129f), 250f, "T2_L3,T2_L9"),   // L2 sits inside the 10 m near edge of the band since 2026-09-05; a claimed deck must be IN band
            // T3: LevelSpan3Tests pins BOTH spawns beside T3_Wall_Span (z 214.5-234.5). West perch beside the
            // span's start (z 216-219, x -9..-6: clear of T3_Obelisk_W1 at x -5.6 / z 221.4 in plan), off the
            // arc's landing (x -1.3) and the pillar hops, covering the last three pillars
            // from behind; east perch OUTSIDE and ABOVE the span wall (top 30), covering the three steps.
            new Perch("T3_Perch_W", "Spawn_T3_Grunt", new Vector3(-7.5f, 24f, 217.5f), 60f, "T3_Pillar_2,T3_Pillar_3"),   // Pillar_4 is inside the 10 m near edge; a claimed deck must be in band
            new Perch("T3_Perch_E", "Spawn_T3_Heavy", new Vector3(11f, 31f, 227.5f), 240f, "T3_Step_1,T3_Step_2,T3_Step_3"),
        };

        // ---------------------------------------------------------------- the balloon arc
        // T3: an ARC of three orbs west of the pillar hops, from T3_Entry's edge to a fall onto T3_Span.
        // MEASURED, not copied from the yard: a pop is 11 m/s up with the carry trimmed to 9 m/s AND a
        // 0.45 s float at 0.55 gravity, so it apexes ~3.5 m above the orb about 5 m out. The next orb
        // therefore sits ~5 m across and ~3 m UP (the yard's 1.2 m rise was laid before the float and a
        // pop sails 2 m over it). Flown in LevelTraversalTests: entry-jump into orb 1 (4 m out, 3 m up),
        // two pops, and the fall from orb 3 lands on the span at z ~216, short of the fallen lintel.
        public const float BalloonLaunch = 11f, BalloonRadius = 1.1f, BalloonRespawn = 2.5f;
        public static readonly Vector3[] T3Arc =
        {
            new Vector3(-4f, 23.0f, 197f),
            new Vector3(-6.5f, 26.0f, 201.3f),
            new Vector3(-6f, 29.0f, 206.2f),
        };

        // ---------------------------------------------------------------- the water lines
        public struct Water
        {
            public string name; public Vector3 center, size, flow; public float speed;
            public Water(string n, Vector3 c, Vector3 s, Vector3 f, float sp) { name = n; center = c; size = s; flow = f.normalized; speed = sp; }
        }
        public static readonly Water[] Waters =
        {
            // The fast slide deck in T1 (top 0.5): a slide on water never decays, so the slide-jump off
            // its end leaves at the full carry. A LINE the tech route rides.
            new Water("T1_Water_Fast", new Vector3(0f, 0.52f, 28f), new Vector3(2.2f, 0.04f, 5.6f), Vector3.forward, 6f),
            // The T3 span after the fallen lintel (top 24.5): skate the run, then the last sheet turns the
            // flow toward T3_Step_1 at (3, 25.5, 241) — a line that TURNS the run (rule 7).
            new Water("T3_Water_Span", new Vector3(0f, 24.52f, 229.5f), new Vector3(3.6f, 0.04f, 13f), Vector3.forward, 6f),
            new Water("T3_Water_Turn", new Vector3(0.8f, 24.52f, 236.2f), new Vector3(2.4f, 0.04f, 1.6f), new Vector3(0.6f, 0f, 0.8f), 6f),
        };

        [MenuItem("VibeGame1/8a. Rework Level_01 (parkour first)")]
        public static void ReworkLevel01()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[LevelAuthoring] Refusing to run in play mode.");
                return;
            }
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level01);
            if (def == null) { Debug.LogError("[LevelAuthoring] " + Level01 + " not found."); return; }
            string summary = Apply(def);
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            Debug.Log("[LevelAuthoring] " + summary + " Now run VibeGame1/8. Build Level From Definition with the asset selected.");
        }

        /// <summary>
        /// Applies the rework to <paramref name="def"/> in memory. Idempotent. Returns a one-line summary
        /// of the counts. Public so a test can run it on a copy and prove both the effect and the
        /// idempotence without touching the shipped asset.
        /// </summary>
        public static string Apply(LevelDefinition def)
        {
            // Perches: remove ours, re-add. Named with "_Perch_" so nothing else can collide.
            var platforms = new List<PlatformDef>(def.platforms ?? new PlatformDef[0]);
            platforms.RemoveAll(p => p != null && p.name != null && p.name.Contains("_Perch_"));
            foreach (var pc in Perches)
            {
                platforms.Add(new PlatformDef
                {
                    name = pc.name, center = pc.center, size = PerchSize, materialKey = "Stone",
                    trim = true, trimMaterialKey = "NeonPink", isStatic = true,
                });
            }
            def.platforms = platforms.ToArray();

            // Spawns: moved by NAME. Names are load-bearing (DebugHarness and FeatureTests find spawners by
            // name), so a spawn is never renamed or removed, only re-placed.
            int moved = 0;
            if (def.spawns != null)
                foreach (var pc in Perches)
                    foreach (var s in def.spawns)
                        if (s != null && s.name == pc.spawn)
                        {
                            s.position = pc.center + new Vector3(0f, PerchSize.y * 0.5f + 0.1f, 0f);
                            s.yaw = pc.yaw;
                            moved++;
                        }

            // The arc and the lines are replaced wholesale: they are this rework's, nothing else authors them.
            var balloons = new List<BalloonDef>();
            for (int i = 0; i < T3Arc.Length; i++)
                balloons.Add(new BalloonDef { name = "T3_Arc_" + (i + 1), position = T3Arc[i],
                                              launchSpeed = BalloonLaunch, radius = BalloonRadius, respawnSeconds = BalloonRespawn });
            def.balloons = balloons.ToArray();

            var waters = new List<WaterDef>();
            foreach (var w in Waters)
                waters.Add(new WaterDef { name = w.name, center = w.center, size = w.size, flowDirection = w.flow, flowSpeed = w.speed });
            def.waters = waters.ToArray();

            return string.Format("Level_01 reworked: {0} perches, {1} spawns moved onto them, {2} balloons (T3 arc), {3} water sheets; {4} platforms total.",
                                 Perches.Length, moved, balloons.Count, waters.Count, def.platforms.Length);
        }
    }
}
