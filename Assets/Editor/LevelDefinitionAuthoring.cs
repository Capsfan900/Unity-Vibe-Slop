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
    ///
    /// <para><b>The openness pass (2026-09-06).</b> A second, additive pass on the same asset, for the
    /// complaint that the level read as cramped and flat. It reshapes named boxes in place — it creates
    /// and destroys nothing — to give the run air: every rail drops below a sliding eyeline, the T1
    /// causeway widens away from its wall-run corridor, the one water slide line grows to a size worth a
    /// committed entry, and the spiral's eleven identical 4 m squares grow until every gap in the climb
    /// is inside the reach contract instead of at its edge. A rhythm of route beacons puts a light on the
    /// deck you are leaving and the one you are arriving on, because the torch budget had all gone to the
    /// arenas and the run was dark. See <see cref="Reshapes"/> for the numbers and the reason each one is
    /// safe.</para>
    /// </summary>
    public static class LevelDefinitionAuthoring
    {
        public const string Level01 = "Assets/Data/Levels/Level_01_Level.asset";

        // ================================================================ the openness pass (2026-09-06)
        //
        // WHY. Level_01 read as cramped and flat: the run was a chain of 4 m squares between 1.2 m rails,
        // and the rails were the worst of it. A SLIDING player's eye sits 0.8 m above the deck
        // (slideHeight 0.9 on the shipped Player.prefab), so a 1.2 m rail put the whole world above the
        // eyeline: the causeway, the bridge and the boss approach — the level's three longest straights,
        // and the three places you most want to be sliding — were slid BLIND, in a trench. Everything in
        // this table is either "you can see over it now" or "the deck is wide enough to steer on".
        //
        // HOW IT IS SAFE. Every entry is an ABSOLUTE center/size, so the pass is idempotent, and every
        // entry moves geometry in the direction that can only ADD room:
        //   * rails get SHORTER, never taller — an arc that was clean stays clean;
        //   * T1_Causeway and T1_Fast_1 widen WESTWARD / EASTWARD AWAY from their wall-run corridor, so
        //     the face the wall run is flown against and its 1.0 m standoff are bit-identical;
        //   * the spiral pads grow, which SHRINKS every gap between them (see the table).
        // The x-coordinates that LevelSpan*Tests pin are held exactly: T2_Buttress.max.x == T2_L2.min.x
        // (5.0, Buttress_DoesNotStandOnTheL2Deck), every ledge beside T2_Wall_East stays inside
        // x < 10.5 - 2r = 9.7 and every ledge beside T2_Wall_West outside x > -10.6 + 2r = -9.8
        // (TheLedgesBesideTheWallStayClearOfTheRunLine), T1_Causeway.max.x stays 1.5 against
        // T1_Wall_Causeway.min.x 3.4, and T1_Wall_Landing.min.x 2.6 still clears T1_Rail_R.max.x 1.7.
        public struct Reshape
        {
            public string name; public Vector3 center, size; public string why;
            public Reshape(string n, Vector3 c, Vector3 s, string w) { name = n; center = c; size = s; why = w; }
        }

        public static readonly Reshape[] Reshapes =
        {
            // ---- the rails: 1.2 m -> 0.65 m. Bottom stays flush with its deck top; only the height moves.
            // A rail's INNER face is flush with the deck edge and its body hangs OUTSIDE it — that is the
            // shipped pattern (T1_Causeway.max.x 1.5, T1_Rail_R spans 1.5..1.7) and it is not cosmetic: a
            // rail standing on the deck eats a capsule radius of take-off room either side of its 0.2 m.
            // T1_Rail_L follows the widened causeway to its new west edge -3.5, so it spans -3.7..-3.5.
            new Reshape("T1_Rail_L", new Vector3(-3.6f, 2.325f, 54f), new Vector3(0.2f, 0.65f, 22f),
                        "the causeway's west rail follows the widened deck out to its new edge, and drops below a sliding eyeline"),
            new Reshape("T1_Rail_R", new Vector3(1.6f, 2.325f, 54f), new Vector3(0.2f, 0.65f, 22f),
                        "the east rail drops so the causeway is slid with the east perch and its bolt in view"),
            new Reshape("T2_Bridge_Rail_L", new Vector3(-2.1f, 20.325f, 150f), new Vector3(0.2f, 0.65f, 10f),
                        "the bridge out of the spiral is 20 m up: the drop should be visible from it, not hidden by it"),
            new Reshape("T2_Bridge_Rail_R", new Vector3(2.1f, 20.325f, 150f), new Vector3(0.2f, 0.65f, 10f), "as above"),
            new Reshape("Boss_Approach_Rail_L", new Vector3(-3.1f, 28.325f, 291f), new Vector3(0.2f, 0.65f, 16f),
                        "16 m of approach to the boss gate, currently slid blind"),
            new Reshape("Boss_Approach_Rail_R", new Vector3(3.1f, 28.325f, 291f), new Vector3(0.2f, 0.65f, 16f), "as above"),

            // ---- T1: the causeway stops being a tightrope.
            // 3 m wide over 22 m was the level's defining pinch and it was not a chosen one. It widens to
            // 5 m WESTWARD ONLY (max.x stays 1.5): the wall-run corridor on the east — T1_Wall_Causeway's
            // west face at x 3.4, run line x 2.95, standoff 1.0 m — does not move by a millimetre.
            new Reshape("T1_Causeway", new Vector3(-1f, 1.5f, 54f), new Vector3(5f, 1f, 22f),
                        "22 m of 3 m-wide deck is a corridor, not a causeway; 5 m is enough to steer a slide and to choose a side"),
            // The slide gate has to keep spanning the deck it gates (CheckLintel.spansTheDeck: the lintel's
            // x range must contain the deck's), so it grows with it: 5 m -> 7 m, recentred on the new deck.
            // Heights are untouched, so clearance stays 1.30 m — a slide fits, standing does not, and the
            // 1.60 m top is still jumpable. It costs time, never access.
            new Reshape("T1_Fallen_Obelisk", new Vector3(-1f, 3.7f, 63f), new Vector3(7f, 0.8f, 1.2f),
                        "the slide gate follows the deck out so it still spans it"),
            // MEASURED, and the reason this entry exists at all: with the causeway recentred on x -1, the
            // bolt line from T1_Perch_W's muzzle (-7.5, 5.5, 44) to the causeway's chest point (-1, 3.2, 54)
            // enters the old obelisk's slab (x -5.1..-3.9) at z 49.5, y 4.2 and is BLOCKED. Moved to
            // x -6.4 the line is clear, and the post becomes the marker for the causeway's new west edge.
            new Reshape("T1_Obelisk_W", new Vector3(-6.4f, 3.5f, 50f), new Vector3(1.2f, 7f, 1.2f),
                        "it stood exactly in the west perch's bolt line onto the widened causeway"),
            // The one dedicated slide line in the level was 2.5 x 6 m. A slide costs stamina now, so it has
            // to pay: 4 x 7 m, widened EAST (min.x stays -1.25, so the 0.25 m seam with T1_Stone_3 does not
            // close) and lengthened NORTH. The south edge does NOT move: the 8.5 m entry gap from
            // T1_Stone_1 is what gates this line to a slide-jump, and a base jump clears ~7.3 m.
            new Reshape("T1_Fast_1", new Vector3(0.75f, 0f, 28.5f), new Vector3(4f, 1f, 7f),
                        "6 m of water for an 8.5 m committed entry was a bad trade; 7 m of it, 4 m wide, is a line"),

            // ---- T2: the spiral's eleven identical 4 m squares.
            // Every pad grows to ~4.8 x 5. The point is not the area, it is what it does to the GAPS,
            // which were sitting at or past the reach contract's 4.5 m ceiling for a 1.5 m rise:
            //     Entry->L1 5.1 -> 4.5   L1->L2 4.0 -> 3.0   L2->L3 4.2 -> 3.2   L3->L4 4.2 -> 2.9
            //     L4->L5   4.0 -> 3.0    L5->L6 3.1 -> 2.1   L6->L7 3.1 -> 2.1   L7->L8 4.0 -> 3.0
            //     L8->L9   4.2 -> 3.2    L9->L10 1.4 -> 0.7
            // A climb of eleven contract-edge leaps onto 4 m squares becomes a climb you can carry speed
            // through. L2 and L8 widen EAST ONLY because x 5.0 is pinned: T2_Buttress's east face is the
            // L2 deck's west edge and the buttress chimney is measured from it.
            //
            // A SHORTER GAP IS NOT AUTOMATICALLY A BETTER HOP, and this table is where that was learned:
            // growing a take-off deck moves the sampled take-off points with it, and if the extra room is
            // on the WRONG side of an obstacle the hop loses launch points while the gap number improves.
            // Every entry below is measured hop by hop against the shipped asset (Tools/level_arc_offline.py),
            // not argued from the gap alone.
            new Reshape("T2_L1", new Vector3(7f, 4.5f, 116f), new Vector3(4.8f, 1f, 5f), "the spiral's first pad; 1.0 m of standoff left to T2_Wall_East's run line"),
            new Reshape("T2_L2", new Vector3(7.25f, 6f, 124f), new Vector3(4.5f, 1f, 5f), "east only: min.x 5.0 is the buttress's face"),
            new Reshape("T2_L3", new Vector3(0f, 7.5f, 131f), new Vector3(5f, 1f, 5f), "the north turn of the spiral"),
            new Reshape("T2_L4", new Vector3(-7f, 9f, 124f), new Vector3(4.8f, 1f, 5f), "the west wall's mount ledge; 1.1 m of standoff left"),
            new Reshape("T2_L5", new Vector3(-7f, 10.5f, 116f), new Vector3(4.8f, 1f, 5f), "clear of T2_Perch_W in plan (z 109-112)"),
            new Reshape("T2_L6", new Vector3(0f, 12f, 111f), new Vector3(5f, 1f, 5f), "the south turn, over the west perch"),
            new Reshape("T2_L7", new Vector3(7f, 13.5f, 116f), new Vector3(4.8f, 1f, 5f), "the second lap"),
            new Reshape("T2_L8", new Vector3(7.25f, 15f, 124f), new Vector3(4.5f, 1f, 5f), "east only, and the buttress chimney's exit ledge — a bigger target for the climb"),
            // L9 is the north turn of the SECOND lap and stands directly over L3, the north turn of the
            // first. It was the one pad in the spiral left at 4 x 4 while the deck it answers grew to
            // 5 x 5, and the cost was measured: L8 -> L9 is the spiral's hardest hop (it must clear the
            // tower's north-east corner) and growing L8 alone made it WORSE — 10 clean take-off points
            // out of 25 in the shipped asset, 8 with L8 grown, 13 with L9 matched to L3. Matching it also
            // makes the two laps read as the same turn seen twice, which is what a spiral is for.
            new Reshape("T2_L9", new Vector3(0f, 16.5f, 131f), new Vector3(5f, 1f, 5f),
                        "the second lap's north turn, matched to T2_L3 directly below it"),
        };

        /// <summary>
        /// Route decks whose neon edge was off. A ledge with no trim is a ledge you read late, and
        /// <c>T2_Bridge</c> was the only deck on the whole critical path without one — an unlit 10 m
        /// plank 20 m up. Trim is decoration with no collider and no NavMesh, so this is free.
        /// </summary>
        public static readonly string[] TrimOn = { "T2_Bridge" };

        /// <summary>
        /// Trim keys corrected with the trim. <c>T2_Bridge</c> carried <c>NeonPink</c> in a gold span —
        /// turning its trim on unchanged would have lit an azure edge on a T2 tile and broken the span
        /// language (T1 cyan, T2 gold, T3 red, wall-run pieces cyan everywhere). The bridge RAILS stay
        /// unlit on the vfx team's veto: at 0.65 m their top edge sits at a sliding player's eye height,
        /// and four emissive bars across the middle of the frame is exactly where the alert tell is read.
        /// </summary>
        public static readonly string[,] TrimKey = { { "T2_Bridge", "NeonYellow" } };

        /// <summary>
        /// ROUTE BEACONS. The torch budget was spent on the four arenas (4-6 each) and the run got almost
        /// nothing: three torches for the whole 21 m spiral, four for 66 m of T3, two for the 22 m
        /// causeway. A torch has no collider (<c>LevelPieceFactory.Torch</c>) and its ember is the only
        /// object in the palette licensed to bloom, so it is the only navigational mark that survives
        /// distance — trim is the near instrument ("here is the edge"), a torch is the far one ("that is
        /// where next"). One per deck at its LEADING OUTER corner lights the deck you are leaving and the
        /// one you are arriving on without ever standing where you land.
        ///
        /// <para><b>The placement law, from the vfx team's audit, and the reason this list is shorter
        /// than the decks it serves.</b> <c>AdditionalLightsPerObjectLimit</c> is 4 on both RP assets, so
        /// the 5th light reaching a surface is dropped after being paid for — and WHICH one is dropped
        /// changes as you move, which reads as a torch popping on and off as you run past. So: no torch
        /// may have more than three other torch lights within its 9 m range, which on a route means
        /// beacons no closer than 6.5 m. That is why there is no beacon on T2_L1 or T2_L2 (the spiral
        /// folds back over itself and <c>Torch_T2_Buttress</c> already stands 1.8 m from L2), and why the
        /// three T3 beacons are 8 m apart rather than the 5 m the pillars are. Measured, not guessed:
        /// <c>Tools/level_arc_offline.py --torches</c> prints the worst cluster, and
        /// <c>TorchDensityTests</c> fails the build if this list ever drifts past 4.</para>
        /// </summary>
        public static readonly TorchDef[] RouteBeacons =
        {
            new TorchDef { name = "Torch_Beacon_T1_Causeway_1", basePosition = new Vector3(-3.2f, 2f, 51f) },
            new TorchDef { name = "Torch_Beacon_T1_Causeway_2", basePosition = new Vector3(-3.2f, 2f, 57f) },
            // The spiral, second half of each lap: L1 and L2 are already read from the entry pair and the
            // buttress torch, and beaconing them tips the L5/L6/L7 stack over the limit.
            new TorchDef { name = "Torch_Beacon_T2_L3", basePosition = new Vector3(-2.2f, 8f, 132.8f) },
            new TorchDef { name = "Torch_Beacon_T2_L4", basePosition = new Vector3(-8.8f, 9.5f, 122.2f) },
            new TorchDef { name = "Torch_Beacon_T2_L5", basePosition = new Vector3(-8.8f, 11f, 114.2f) },
            new TorchDef { name = "Torch_Beacon_T2_L7", basePosition = new Vector3(8.8f, 14f, 117.6f) },
            new TorchDef { name = "Torch_Beacon_T2_L8", basePosition = new Vector3(8.9f, 15.5f, 125.6f) },
            new TorchDef { name = "Torch_Beacon_T2_L9", basePosition = new Vector3(-1.7f, 17f, 132.5f) },
            new TorchDef { name = "Torch_Beacon_T2_L10", basePosition = new Vector3(6.7f, 18.5f, 137.5f) },
            // T3: on the pillars, at the OUTER corner of the top face — a 2.5 m square you land on at
            // speed gets nothing in the middle of it — every 8 m rather than every pillar.
            new TorchDef { name = "Torch_Beacon_T3_Pillar_1", basePosition = new Vector3(-1f, 21.5f, 197.2f) },
            new TorchDef { name = "Torch_Beacon_T3_Pillar_3", basePosition = new Vector3(-4f, 23.5f, 206.9f) },
            new TorchDef { name = "Torch_Beacon_T3_Pillar_4", basePosition = new Vector3(-1.2f, 24.5f, 214f) },
        };

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
            // its end leaves at the full carry. A LINE the tech route rides. Grown with the deck under it
            // (see Reshapes: T1_Fast_1 is now 4 x 7 centred on x 0.75, z 28.5) with 0.3 m of dry stone
            // left at every edge, so the sheet still reads as lying ON the deck rather than as the deck.
            new Water("T1_Water_Fast", new Vector3(0.75f, 0.52f, 28.5f), new Vector3(3.4f, 0.04f, 6.4f), Vector3.forward, 6f),
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
            // The openness pass runs FIRST and edits boxes it does not own the existence of, only their
            // shape: it looks each one up BY NAME and writes an absolute centre and size, so a second run
            // writes the same numbers and nothing is ever created or destroyed here.
            int reshaped = 0;
            if (def.platforms != null)
                foreach (var r in Reshapes)
                    foreach (var pf in def.platforms)
                        if (pf != null && pf.name == r.name) { pf.center = r.center; pf.size = r.size; reshaped++; }
            if (def.platforms != null)
            {
                foreach (var name in TrimOn)
                    foreach (var pf in def.platforms)
                        if (pf != null && pf.name == name) pf.trim = true;
                for (int i = 0; i < TrimKey.GetLength(0); i++)
                    foreach (var pf in def.platforms)
                        if (pf != null && pf.name == TrimKey[i, 0]) pf.trimMaterialKey = TrimKey[i, 1];
            }

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

            // Route beacons, the same way: remove ours by prefix, re-add. The level's hand-placed torches
            // (Torch_T1_*, Torch_Boss_* and the rest) are never touched — this pass owns "Torch_Beacon_".
            var torches = new List<TorchDef>(def.torches ?? new TorchDef[0]);
            torches.RemoveAll(t => t != null && t.name != null && t.name.StartsWith("Torch_Beacon_"));
            foreach (var b in RouteBeacons)
                torches.Add(new TorchDef { name = b.name, basePosition = b.basePosition });
            def.torches = torches.ToArray();

            return string.Format("Level_01 reworked: {0} boxes reshaped for openness, {1} perches, {2} spawns moved onto them, " +
                                 "{3} balloons (T3 arc), {4} water sheets, {5} route beacons; {6} platforms and {7} torches total.",
                                 reshaped, Perches.Length, moved, balloons.Count, waters.Count, RouteBeacons.Length,
                                 def.platforms.Length, def.torches.Length);
        }
    }
}
