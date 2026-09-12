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
    ///
    /// <para><b>The ramp pass (2026-09-07).</b> A third, additive pass: the level's first sloped geometry,
    /// five <see cref="RampDef"/>s that turn four hops into runs. See <see cref="Ramps"/> for each one's
    /// numbers, its derived angle and its job. These five connectors climb from y 0 toward y 28;
    /// their job is continuity. <see cref="ApplyDescent"/> adds the final 48 m descent after T3.
    /// <b>A ramp is not a kicker</b>: a
    /// <c>CharacterController</c> leaving a ramp's top gets no vertical impulse, so a ramp that stops short
    /// of the next deck is only a jump with a shorter run-up. Every ramp here meets its deck at both ends.</para>
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
            new Reshape("T1_Rail_L", new Vector3(-4.6f, 2.325f, 54f), new Vector3(0.2f, 0.65f, 22f),
                        "the causeway's west rail follows the widened deck out to its new edge, and sits below a sliding eyeline"),
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
            new Reshape("T1_Causeway", new Vector3(-1.5f, 1.5f, 54f), new Vector3(6f, 1f, 22f),
                        "3 m -> 5 m (pass 1) -> 6 m: 22 m of deck at 4.4:1 still read as a trench; at 3.7:1 with a 0.65 m rail it reads as a bridge"),
            // The slide gate has to keep spanning the deck it gates (CheckLintel.spansTheDeck: the lintel's
            // x range must contain the deck's), so it grows with it: 5 m -> 7 m, recentred on the new deck.
            // Heights are untouched, so clearance stays 1.30 m — a slide fits, standing does not, and the
            // 1.60 m top is still jumpable. It costs time, never access.
            new Reshape("T1_Fallen_Obelisk", new Vector3(-1.5f, 3.7f, 49f), new Vector3(7f, 0.8f, 1.2f),
                        "MEASURED: at z 63 the gate stood 1.4 m behind the causeway's take-off edge and was the level's " +
                        "single worst piece of geometry - 5 clean launch points out of 20 onto T1_Stone_5, and a causeway " +
                        "you could see NOTHING from (0 route decks visible ahead, blocked by this slab from six vantage " +
                        "points running back to the spawn). At z 49 it is the first thing on the deck instead of the last: " +
                        "the exit hop goes to 20/25, the causeway sees 3 decks ahead, and the gate is read from T1_Stone_3 " +
                        "onward instead of arriving in your face. z 52 measures identically and was rejected - it blocks " +
                        "T1_Perch_W's bolt line onto the causeway (the bolt passes z 49 at y 4.35, 0.25 m over this slab's top)."),
            // MEASURED, and the reason this entry exists at all: with the causeway recentred on x -1, the
            // bolt line from T1_Perch_W's muzzle (-7.5, 5.5, 44) to the causeway's chest point (-1, 3.2, 54)
            // enters the old obelisk's slab (x -5.1..-3.9) at z 49.5, y 4.2 and is BLOCKED. Moved to
            // x -6.4 the line is clear, and the post becomes the marker for the causeway's new west edge.
            new Reshape("T1_Obelisk_W", new Vector3(-6.4f, 3.5f, 50f), new Vector3(1.2f, 7f, 1.2f),
                        "it stood exactly in the west perch's bolt line onto the widened causeway. Re-measured " +
                        "2026-09-07 against the perch's new home at (-11.5, 3.5, 53): both bolt lines pass x -8.8 " +
                        "or further west at this slab's z, so x -6.4 stays clear and the post keeps marking the " +
                        "causeway's west edge"),
            // The one dedicated slide line in the level was 2.5 x 6 m. A slide costs stamina now, so it has
            // to pay: 4 x 7 m, widened EAST (min.x stays -1.25, so the 0.25 m seam with T1_Stone_3 does not
            // close) and lengthened NORTH. The south edge does NOT move: the 8.5 m entry gap from
            // T1_Stone_1 is what gates this line to a slide-jump, and a base jump clears ~7.3 m.
            new Reshape("T1_Fast_1", new Vector3(0.75f, 0f, 28.5f), new Vector3(4f, 1f, 7f),
                        "6 m of water for an 8.5 m committed entry was a bad trade; 7 m of it, 4 m wide, is a line"),

            // ================================================================================
            // ---- T1: THE OPENING BREATHES (2026-09-07, the user's ask: "more runway before the
            // first enemy so you can have more time to parry, and space that ramp and all the
            // objects a tad bit more").
            //
            // WHAT WAS ACTUALLY WRONG, measured rather than felt. The parry WINDOW is not a level
            // number: Projectile.CueLead is a flat 0.28 s everywhere and ProjectileShooter.CueMargin
            // holds a near bolt's flight at 0.44 s, so no amount of geometry buys a longer cue. What
            // geometry owns is WHERE the cue lands and how long the sentry has been in view first.
            // T1_Perch_W at (-7.5, 3.5, 44) has a muzzle at y 5.4, and EnemyController.WakeRange for a
            // shooter is max(aggroRange, projectileMaxRange) = 32 m. Solving 32 m from that muzzle down
            // the route centreline puts the wake at z 13.1 - on T1_Stone_1, BEFORE the level's first
            // ramp. Add the shipped 0.7 s arm-up (projectileAcquireDelay) at ~11 m/s and the first bolt
            // launches near z 21, flies 25 m in 0.63 s, and arrives at z ~27.5: the gap between
            // T1_Stone_2 and T1_Stone_3 / T1_Fast_1. The level's first parry cue was landing on the
            // player MID-AIR, over the one hop in T1 that is a real choice. The budget was never short;
            // it was already spent.
            //
            // THE FIX IS TWO SHAPES, not a timing change (nothing in EnemyData, Projectile or
            // PlayerStats is touched by this pass):
            //   1. the perch moves out and up-route to (-11.5, 3.5, 53) - see Perches below. The wake
            //      solves to z ~23.3, so the whole first ramp is run before the sentry is even awake
            //      (+10.2 m of runway), and the first bolt now arrives around z 38, mid-deck on
            //      T1_Stone_4, with both feet down and the causeway dead ahead.
            //   2. the four stones and the first ramp get their "tad" of space here. Every entry is
            //      ABSOLUTE and grows the deck ALONG THE AXIS THE GAP IS NOT MEASURED ON, so not one
            //      hop distance in the chain changes and every hop gains launch points:
            //          Stone_1  5 x 5   -> 7 x 6.4, grown SOUTH and sideways. Its north edge stays at
            //                   z 16.5 exactly, so the ramp overlap (0.2 m) and the 8.5 m slide-jump
            //                   gate onto T1_Fast_1 are bit-identical; the 3.5 m step off Ground_Start
            //                   becomes 2.1 m and the opening reads as a plaza instead of two islands.
            //          Stone_2  4 x 4   -> 5.5 x 4, grown WEST only. max.x stays 5.5, so it still
            //                   clears T1_Wall_Start's run line (TheOpeningLineSkipsTheFourStones).
            //          Stone_3  4 x 4   -> 5 x 4, grown WEST only. min.x -6.5; max.x stays -1.5 so the
            //                   0.25 m seam with T1_Fast_1 does not close, and T1_Ramp_Stone34's
            //                   yawed base (x -4.4..-1.6) stops hanging over the west lip.
            //          Stone_4  5 x 6   -> 6 x 6, grown WEST only. max.x 2.5 still stands off
            //                   T1_Wall_Causeway's run line (x 3.0) by 0.5 m.
            //      Widening Stone_1 to 7 and Stone_2 west to x 0 is also what lets T1_Ramp_Stone12 go
            //      3.0 -> 3.5 m wide and become FULLY SUPPORTED at both ends - the second ramp in the
            //      level that is, and the first thing the player runs up.
            new Reshape("T1_Stone_1", new Vector3(0f, -0.5f, 13.3f), new Vector3(7f, 1f, 6.4f),
                        "the first deck grows SOUTH (north edge pinned at z 16.5): 3.9 m more run-up into the level's " +
                        "first ramp, a 2.1 m step off Ground_Start instead of 3.5, and the 8.5 m Fast_1 gate untouched"),
            new Reshape("T1_Stone_2", new Vector3(2.75f, 0f, 22f), new Vector3(5.5f, 1f, 4f),
                        "grown WEST to x 0 so the widened first ramp lands fully on it; max.x 5.5 is held for the wall-run line"),
            new Reshape("T1_Stone_3", new Vector3(-4f, 0.5f, 30f), new Vector3(5f, 1f, 4f),
                        "grown WEST to x -6.5; the 0.25 m seam with T1_Fast_1 at x -1.25 is held, and T1_Ramp_Stone34 stops overhanging"),
            new Reshape("T1_Stone_4", new Vector3(-0.5f, 1f, 38f), new Vector3(6f, 1f, 6f),
                        "grown WEST with the causeway it feeds; this is where the first bolt now arrives, so it is the " +
                        "one deck in the chain that has to be stood on rather than crossed"),

            // ---- T2: a full-scale spiral rather than eleven cramped stepping stones.
            // These are canonical, pre-translation coordinates. ApplySolarRealms moves the complete span
            // +38 m in Z after every dependent object has been placed. The broad turn balconies preserve
            // the alternating climb, while the L2/L3 and L8/L9 grades are derived from their supported
            // endpoints below. The tower/buttress pair moves +3 m in X as one assembly: the chimney stays
            // 2.2 m wide and the buttress east face remains flush with L2.min.x at x = 8.
            new Reshape("T2_L1", new Vector3(10f, 4.5f, 113.375f), new Vector3(8f, 1f, 10f),
                        "the first broad landing after the cyan realm return; it carries the hook pickup and checkpoint on authored ground"),
            new Reshape("T2_L2", new Vector3(11f, 6f, 124f), new Vector3(6f, 1f, 6.25f), "the widened chimney mouth; min.x 8.0 stays flush with the buttress face"),
            new Reshape("T2_L3", new Vector3(0.5f, 7.5f, 132f), new Vector3(13f, 1f, 7f),
                        "the thirteen-metre north turn of the first lap, with room to redirect after the derived grade"),
            new Reshape("T2_L4", new Vector3(-10f, 9f, 124f), new Vector3(8f, 1f, 7.5f), "the widened west wall mount, kept off the run line"),
            new Reshape("T2_L5", new Vector3(-10f, 10.5f, 114f), new Vector3(8f, 1f, 7.5f), "a full outer balcony around the west perch"),
            new Reshape("T2_L6", new Vector3(0f, 12f, 111.5f), new Vector3(7.8f, 1f, 8.75f),
                        "the isolated south turn of the spiral and the authored ground for Pickup_T2_Surge"),
            new Reshape("T2_L7", new Vector3(10f, 13.5f, 114f), new Vector3(8f, 1f, 7.5f), "the widened start of the second lap"),
            new Reshape("T2_L8", new Vector3(11f, 15f, 124f), new Vector3(6f, 1f, 7.5f), "the widened buttress chimney exit; min.x remains 8.0"),
            // L9 is the north turn of the SECOND lap and stands directly over L3, the north turn of the
            // first. It was the one pad in the spiral left at 4 x 4 while the deck it answers grew to
            // 5 x 5, and the cost was measured: L8 -> L9 is the spiral's hardest hop (it must clear the
            // tower's north-east corner) and growing L8 alone made it WORSE — 10 clean take-off points
            // out of 25 in the shipped asset, 8 with L8 grown, 13 with L9 matched to L3. Matching it also
            // makes the two laps read as the same turn seen twice, which is what a spiral is for.
            new Reshape("T2_L9", new Vector3(0.5f, 16.5f, 132f), new Vector3(13f, 1f, 7f),
                        "the second north turn, footprint-matched to T2_L3 below it"),

            // ---- T2: the spire. THE MEASURED SIGHTLINE FIX FOR THE SPIRAL.
            // A "moves visible ahead" probe (eye at deck centre + 1.7, target at the next decks' centre + 0.5, over
            // Tools/level_arc_offline.py's own line_clear) named T2_Tower as the FIRST BLOCKER from eight of the
            // spiral's eleven vantage points: L1, L2, L3 and L7 could each see exactly ONE deck ahead. A 5 x 5 core
            // in a helix 19 m across is a wall you circle. At 4 x 4 the probe gives L7 four decks ahead instead of
            // one, L5 four instead of three and L3 two instead of one. 3 x 3 was measured too: it buys nothing more
            // and it opens the buttress chimney to 2.7 m, past the 2.6 m a wall push crosses in 0.22 s.
            // It narrows in X ONLY, 5 -> 4, and that is deliberate: X is where the whole sightline gain lives
            // (measured, 4x5 and 4x4 both give 3.00 mean moves-ahead, 5x4 only 2.84), while Z is the axis the
            // buttress chimney's DEPTH is measured on. So the chimney keeps its 2.5 m depth bit-for-bit and
            // only its width moves, 1.7 -> 2.2 m: still inside the 1.5-3 m contract, still under the 2.6 m a
            // push crosses. The tower carries no deck, so nothing stands on what it loses.
            //
            // THE ONE THING IN THIS PASS THAT COULD NOT BE PROVEN OFFLINE, and the fallback if it breaks.
            // LevelArcClearanceTests.Buttress_ClimbsToTheExitLedge simulates the real wall-push arc through
            // this chimney and there is no offline mirror of ClimbChimney. If it fails, change the 4f below
            // to 4.5f: that puts the chimney at 1.95 m, a quarter of a metre from the shipped 1.70 m, and
            // the sightline still measures 2.84 against the shipped 2.45. Nothing else in this table depends
            // on the tower.
            new Reshape("T2_L10", new Vector3(8f, 18f, 140f), new Vector3(6f, 1f, 6f), "a broad penultimate landing with a clean read on the launch terrace"),
            new Reshape("T2_L11", new Vector3(0f, 19.5f, 150f), new Vector3(10f, 1f, 8f), "the final launch terrace into the gold sun"),
            new Reshape("T2_Tower", new Vector3(3f, 12f, 124f), new Vector3(4f, 20f, 5f),
                        "the spiral's core was the spiral's blindfold; a 5 m wall in a 19 m helix -> 4 m"),
            new Reshape("T2_Buttress", new Vector3(7.6f, 10.2f, 122.5f), new Vector3(0.8f, 7.4f, 3f),
                        "moves with the tower while preserving a 2.2 m chimney and a face flush with T2_L2"),
            new Reshape("T2_Wall_East", new Vector3(15.7f, 5.5f, 123.5f), new Vector3(1.2f, 9f, 21f), "the east alternate line follows the expanded spiral"),
            new Reshape("T2_Wall_Landing_East", new Vector3(11.4f, 7.5f, 137.5f), new Vector3(5.6f, 1f, 8f), "the east line lands level with the widened north balcony"),
            new Reshape("T2_Wall_West", new Vector3(-15.8f, 10.5f, 115f), new Vector3(1.2f, 9f, 22f), "the west alternate line follows the expanded spiral"),
            new Reshape("T2_Wall_Landing_West", new Vector3(-10.8f, 12f, 101.75f), new Vector3(6.8f, 1f, 9.5f), "the west line retains an isolated landing"),

            // ---- T3: four elevated terraces open into a wide water span.
            // These are canonical, pre-translation coordinates; the complete section moves +70 m in Z.
            // The first terrace receives the gold-realm return and the next two make the cross-course
            // precision beat readable at the larger scale without changing the movement thresholds.
            new Reshape("T3_Pillar_1", new Vector3(2.75f, 15.5f, 198f), new Vector3(9f, 12f, 5f),
                        "the entry terrace of the pillar run and authored ground for the surge pickup and checkpoint"),
            new Reshape("T3_Pillar_2", new Vector3(5f, 16.5f, 204.5f), new Vector3(10.5f, 12f, 4f), "the exposed hop line grows into a readable elevated terrace"),
            new Reshape("T3_Pillar_3", new Vector3(-5f, 17.5f, 211f), new Vector3(10.5f, 12f, 4f), "a wide cross-course landing keeps the long-span scale"),
            new Reshape("T3_Pillar_4", new Vector3(0f, 18.5f, 217.5f), new Vector3(6f, 12f, 5f), "the last pillar remains a distinct launch into the span"),

            // The long span is now a 9.6 x 26 m run with its posts, lintel, water and alternate wall line
            // moved as one assembly. The lintel remains 0.1 m wider than the deck on each side.
            new Reshape("T3_Span", new Vector3(0f, 24f, 234f), new Vector3(9.6f, 1f, 26f),
                        "a wide, long water line with room to steer and a supported route pickup"),
            new Reshape("T3_Fallen_Lintel", new Vector3(0f, 26.15f, 226f), new Vector3(9.8f, 0.8f, 1.2f), "the slide gate widens with the full span"),
            new Reshape("T3_Obelisk_W1", new Vector3(-8f, 22f, 226f), new Vector3(1.2f, 10f, 1.2f), "outer rhythm post on the expanded span"),
            new Reshape("T3_Obelisk_E1", new Vector3(8f, 22f, 226f), new Vector3(1.2f, 10f, 1.2f), "outer rhythm post on the expanded span"),
            new Reshape("T3_Obelisk_W2", new Vector3(-8f, 22f, 237.5f), new Vector3(1.2f, 10f, 1.2f), "outer rhythm post on the expanded span"),
            new Reshape("T3_Obelisk_E2", new Vector3(8f, 22f, 237.5f), new Vector3(1.2f, 10f, 1.2f), "outer rhythm post on the expanded span"),
            new Reshape("T3_Recovery_W1", new Vector3(-5.8f, 20f, 225f), new Vector3(1.2f, 8f, 1.2f), "recovery post stays between the deck and outer obelisk"),
            new Reshape("T3_Recovery_E1", new Vector3(5.8f, 20f, 225f), new Vector3(1.2f, 8f, 1.2f), "recovery post stays between the deck and outer obelisk"),
            new Reshape("T3_Recovery_W2", new Vector3(-5.8f, 20f, 236.5f), new Vector3(1.2f, 8f, 1.2f), "recovery post stays between the deck and outer obelisk"),
            new Reshape("T3_Recovery_E2", new Vector3(5.8f, 20f, 236.5f), new Vector3(1.2f, 8f, 1.2f), "recovery post stays between the deck and outer obelisk"),
            new Reshape("T3_Step_1", new Vector3(3f, 25f, 251f), new Vector3(12f, 1f, 6f), "the first exit terrace is large enough to steer after the wall line"),
            new Reshape("T3_Step_2", new Vector3(-3f, 26.5f, 259f), new Vector3(12f, 1f, 8f), "the final launch terrace broadens before the azure sun"),
            new Reshape("T3_Wall_Pillars", new Vector3(12.6f, 22.5f, 202f), new Vector3(1.2f, 7f, 23f), "the long pillar shortcut gates the direct jump and earns a full wall run"),
            new Reshape("T3_Wall_Landing_S", new Vector3(10f, 22.5f, 217.25f), new Vector3(8f, 1f, 6.5f), "the isolated landing catches the full west exit throw and chains into the span wall"),
            new Reshape("T3_Wall_Span", new Vector3(13.1f, 26f, 232.5f), new Vector3(1.2f, 8f, 23f), "the long wall-run line moves outside the widened span"),

            // ---- THE ARENA DOORWAYS: 6 m -> 9 m.
            // Every arena is entered and left through a 6 m slot in a 4 m wall, and the probe named
            // Wall_T1_N_R as the first blocker from BOTH T1_Stone_5 and T1_Arena: standing in a 26 m room you
            // could see one deck ahead, because the next span was behind a letterbox. The arena floors are the
            // only wide space in the level and the doors were throwing that space away. Measured at 9 m:
            // T1_Arena -> T2_Entry goes 13 -> 21 clean launch points, T1_Arena sees three decks ahead instead
            // of one, and T2_L10 sees four instead of two (it is looking through the T2 arena's south door at
            // the spiral's own exit). Each wall keeps its outer end and its length changes; the arena's
            // footprint does not move. The GATES and the arena TRIGGERS widen with them - see Gates - or a
            // player entering at x 4 walks past the trigger and the fight never starts.
            new Reshape("Wall_T1_S_L", new Vector3(-8.75f, 6f, 73.75f), new Vector3(8.5f, 4f, 0.5f), "T1 arena south door 6 -> 9 m"),
            new Reshape("Wall_T1_S_R", new Vector3(8.75f, 6f, 73.75f), new Vector3(8.5f, 4f, 0.5f), "as above"),
            new Reshape("Wall_T1_N_L", new Vector3(-8.75f, 6f, 100.25f), new Vector3(8.5f, 4f, 0.5f), "T1 arena north door: the one the probe caught"),
            new Reshape("Wall_T1_N_R", new Vector3(8.75f, 6f, 100.25f), new Vector3(8.5f, 4f, 0.5f), "as above"),
            new Reshape("Wall_T2_S_L", new Vector3(-9.25f, 22f, 155.75f), new Vector3(9.5f, 4f, 0.5f), "T2 arena is 28 m wide, so its walls are 9.5 m"),
            new Reshape("Wall_T2_S_R", new Vector3(9.25f, 22f, 155.75f), new Vector3(9.5f, 4f, 0.5f), "as above"),
            new Reshape("Wall_T2_N_L", new Vector3(-9.25f, 22f, 184.25f), new Vector3(9.5f, 4f, 0.5f), "as above"),
            new Reshape("Wall_T2_N_R", new Vector3(9.25f, 22f, 184.25f), new Vector3(9.5f, 4f, 0.5f), "as above"),
            new Reshape("Wall_T3_S_L", new Vector3(-8.75f, 30f, 256.75f), new Vector3(8.5f, 4f, 0.5f), "T3 arena south door"),
            new Reshape("Wall_T3_S_R", new Vector3(8.75f, 30f, 256.75f), new Vector3(8.5f, 4f, 0.5f), "as above"),
            new Reshape("Wall_T3_N_L", new Vector3(-8.75f, 30f, 283.25f), new Vector3(8.5f, 4f, 0.5f), "as above"),
            new Reshape("Wall_T3_N_R", new Vector3(8.75f, 30f, 283.25f), new Vector3(8.5f, 4f, 0.5f), "as above"),
            // The boss arena's SOUTH door only. Its north wall's slot opens onto nothing and widening it would
            // just be a bigger hole in the back of the last room.
            new Reshape("Wall_Boss_S_L", new Vector3(-11.75f, 30f, 300.75f), new Vector3(14.5f, 4f, 0.5f), "the boss door, seen from 16 m of approach"),
            new Reshape("Wall_Boss_S_R", new Vector3(11.75f, 30f, 300.75f), new Vector3(14.5f, 4f, 0.5f), "as above"),
        };

        /// <summary>
        /// The arena gates and triggers, widened with the doorways above. A gate is matched BY
        /// <c>gateName</c> and only its X is written, so this is absolute and idempotent like everything else
        /// here, and the sink/rise positions and the material are untouched. The TRIGGER matters as much as the
        /// gate: it is a 6 m box in the mouth of the arena, and a 9 m door with a 6 m trigger is a door you can
        /// walk through at x 4 without the fight ever starting. Boss_Gate is in the list; its arena has no exit
        /// gate, and the north doorways of the boss room are deliberately left at 6 m.
        /// </summary>
        public struct GateWidth
        {
            public string gateName; public float width; public bool exitToo;
            public GateWidth(string g, float w, bool e) { gateName = g; width = w; exitToo = e; }
        }

        public static readonly GateWidth[] Gates =
        {
            new GateWidth("T1_Gate", 9f, true),
            new GateWidth("T2_Gate", 9f, true),
            new GateWidth("T3_Gate", 9f, true),
            new GateWidth("Boss_Gate", 9f, false),
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
        public static readonly string[,] TrimKey =
        {
            { "T2_Bridge", "NeonYellow" },
            // The vfx team's call on "the spiral is eleven identical gold squares". Three deck SHAPES with
            // three jobs is the fix; three HUES is not, because colour in this level is the SPAN word (which
            // tile am I in, read at 20-40 m) and role is a silhouette word (read at 2-8 m, where trim lives).
            // Give role a hue and the player holds two mappings for one channel. But T2_L2 and T2_L8 are not
            // a third role - they are the MOUTH and the EXIT of the buttress chimney, the spiral's wall-jump
            // shortcut, and this level already says "an alternate line off the main span is cyan" on every
            // wall-run piece. So they wear the word they already belong to. No new material.
            { "T2_L2", "NeonCyan" },
            { "T2_L8", "NeonCyan" },
        };

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
            // The causeway's west edge moved -3.5 -> -4.5 with the deck, so these follow it: a beacon 1.3 m
            // INSIDE a 6 m deck is a post in the middle of the road, and the whole point of the pair is that
            // they mark the edge you are steering along.
            new TorchDef { name = "Torch_Beacon_T1_Causeway_1", basePosition = new Vector3(-4.2f, 2f, 51f) },
            new TorchDef { name = "Torch_Beacon_T1_Causeway_2", basePosition = new Vector3(-4.2f, 2f, 57f) },
            // The spiral, second half of each lap: L1 and L2 are already read from the entry pair and the
            // buttress torch, and beaconing them tips the L5/L6/L7 stack over the limit.
            // L3 and L9 grew from 5 m to 7 m wide, so their beacons follow the new west edge (-3.5).
            new TorchDef { name = "Torch_Beacon_T2_L3", basePosition = new Vector3(-5.7f, 8f, 135.2f) },
            new TorchDef { name = "Torch_Beacon_T2_L4", basePosition = new Vector3(-13.7f, 9.5f, 120.55f) },
            new TorchDef { name = "Torch_Beacon_T2_L5", basePosition = new Vector3(-13.7f, 11f, 110.55f) },
            new TorchDef { name = "Torch_Beacon_T2_L7", basePosition = new Vector3(13.7f, 14f, 110.55f) },
            new TorchDef { name = "Torch_Beacon_T2_L8", basePosition = new Vector3(13.7f, 15.5f, 127.45f) },
            new TorchDef { name = "Torch_Beacon_T2_L9", basePosition = new Vector3(-5.7f, 17f, 135.2f) },
            new TorchDef { name = "Torch_Beacon_T2_L10", basePosition = new Vector3(10.7f, 18.5f, 142.7f) },
            // T3: on the pillars, at the OUTER corner of the top face — a 2.5 m square you land on at
            // speed gets nothing in the middle of it — every 8 m rather than every pillar.
            new TorchDef { name = "Torch_Beacon_T3_Pillar_1", basePosition = new Vector3(-1.45f, 21.5f, 195.8f) },
            new TorchDef { name = "Torch_Beacon_T3_Pillar_3", basePosition = new Vector3(-9.95f, 23.5f, 209.3f) },
            new TorchDef { name = "Torch_Beacon_T3_Pillar_4", basePosition = new Vector3(2.7f, 24.5f, 219.7f) },

            // ---- THE ALTERNATE LINES, which were invisible.
            // Every span in this level already HAS a second way through - the wall runs, proven by
            // LevelSpan1/2/3Tests and by the buttress chimney tests - and not one of their four landing decks
            // carried a single torch. A route the player cannot see is not a route, and "there is only one
            // line" was never true here; it was only ever unlit. Trim is the near instrument and these decks
            // are already trimmed cyan; the far instrument is the ember, the only object licensed to bloom.
            //
            // The vfx team's two corrections, taken: (1) the mark goes at the corner nearest where the
            // DECISION is made, not the leading corner - a mark at the far end is behind you by the time it
            // could act; (2) it is NOT a different colour. A cold ember over a warm point light breaks the
            // warm-means-fire rule and an emissive over 1.05 is an unlicensed bloom exception. It differs
            // from a route beacon by GRAMMAR: route beacons come in a rhythm of pairs on the line you are
            // already on, and these stand ALONE, off the line, at a different height.
            new TorchDef { name = "Torch_Beacon_Alt_T1_Landing", basePosition = new Vector3(6.1f, 2.5f, 60f) },
            new TorchDef { name = "Torch_Beacon_Alt_T2_East", basePosition = new Vector3(13.9f, 8f, 133.8f) },
            new TorchDef { name = "Torch_Beacon_Alt_T2_West", basePosition = new Vector3(-13.9f, 12.5f, 104f) },
            new TorchDef { name = "Torch_Beacon_Alt_T3_S", basePosition = new Vector3(13.7f, 23f, 207.3f) },
        };

        // Sparse edge markers for the open course. These replace the dense historical spiral rhythm and
        // deliberately leave the centre fourteen metres of every landing free of posts.
        public static readonly TorchDef[] OpenRouteBeacons =
        {
            new TorchDef { name = "Torch_Beacon_T1_Causeway_1", basePosition = new Vector3(-6.5f, 2f, 47f) },
            new TorchDef { name = "Torch_Beacon_T1_Causeway_2", basePosition = new Vector3(6.5f, 2f, 56f) },
            new TorchDef { name = "Torch_Beacon_T2_L1", basePosition = new Vector3(19.5f, 5f, 110f) },
            new TorchDef { name = "Torch_Beacon_T2_L3", basePosition = new Vector3(-6.5f, 8f, 135f) },
            new TorchDef { name = "Torch_Beacon_T2_L6", basePosition = new Vector3(-8.5f, 12.5f, 104f) },
            new TorchDef { name = "Torch_Beacon_T2_L9", basePosition = new Vector3(11.5f, 17f, 135f) },
            new TorchDef { name = "Torch_Beacon_T2_L11", basePosition = new Vector3(-8.5f, 20f, 146f) },
            new TorchDef { name = "Torch_Beacon_T3_Pillar_1", basePosition = new Vector3(-6.5f, 21.5f, 193f) },
            new TorchDef { name = "Torch_Beacon_T3_Pillar_3", basePosition = new Vector3(-11.5f, 23.5f, 216f) },
            new TorchDef { name = "Torch_Beacon_T3_Span", basePosition = new Vector3(7.5f, 24.5f, 235f) },
            new TorchDef { name = "Torch_Beacon_T3_Step_2", basePosition = new Vector3(-10.5f, 27f, 253f) },
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
            // MOVED 2026-09-07 from (-7.5, 3.5, 44), which was 1 m north of T1_Ramp_Causeway's top edge:
            // you crested the ramp and the sentry was beside you. Its 32 m wake radius reached back to
            // z 13.1, so it armed before the level's FIRST ramp and its opening bolt landed in the air
            // over the Stone_2 -> Stone_3 choice (the arithmetic is under the T1 opening block in
            // Reshapes). From (-11.5, 3.5, 53) the same radius solves to z ~23.3: the first ramp is run
            // unopposed, the arm-up burns on the approach, and the first bolt arrives around z 38 - mid
            // deck on the widened T1_Stone_4, feet down, running straight at the causeway, so the parry
            // boost throws the player up the line they are already on (perch rule 4).
            // x -11.5 rather than -8: at z 53 the perch is broadside to the causeway, and the nearest
            // causeway chest point must stay OUTSIDE projectileMinRange 6 m or the sentry goes quiet
            // exactly where it is meant to be firing. From here it is 7.3 m - in band the whole way.
            // Both claimed decks stay in band with a clear line: T1_Stone_4 at 25.0 m and T1_Causeway at
            // 10.7 m, and both lines pass west of T1_Obelisk_W (x -7.0..-5.8) and of T1_Fallen_Obelisk.
            // Wide forward flanks: the first shelf rises above the west causeway shoulder, so Stone 4 sees
            // the squid and its incoming bolt before the slide gate. The second is high enough that its line
            // clears that gate, but still sits inside the course's east shoulder rather than the wall-run.
            // Both shots remain in the forward parry cone and their flare branch rejoins before the arena.
            new Perch("T1_Perch_W", "Spawn_T1_GruntA", new Vector3(-24f, 4.5f, 60f), 90f, "T1_Stone_4,T1_Causeway"),
            new Perch("T1_Perch_E", "Spawn_T1_GruntB", new Vector3(6.8f, 5f, 62f), 180f, "T1_Causeway"),

            // T2: announce each half of the helix from the OUTER forward shoulder, not from behind its
            // central tower. The lower blue squid is visible from L1 and L2 before the first switchback;
            // the upper one is visible from L8 before committing to L9. It sits on the inside-forward
            // shoulder of L9: clear of L10's landing corridor and the gold portal sun, but still close
            // enough for multiple valid high-speed predicted contacts. Their bolts cross those same terrace turns,
            // and both flares feed the existing shortcut/rejoin interval.
            new Perch("T2_Perch_W", "Spawn_T2_GruntB", new Vector3(20f, 8f, 136f), 205f, "T2_L1,T2_L2"),
            new Perch("T2_Perch_E", "Spawn_T2_GruntA", new Vector3(21f, 20f, 147f), 230f, "T2_L9"),
            // T3: LevelSpan3Tests pins BOTH spawns beside T3_Wall_Span (z 214.5-234.5). West perch beside the
            // span's start (z 216-219, x -9..-6: clear of T3_Obelisk_W1 at x -5.6 / z 221.4 in plan), off the
            // arc's landing (x -1.3) and the pillar hops, covering the last three pillars
            // from behind; east perch OUTSIDE and ABOVE the span wall (top 30), covering the three steps.
            // Above the west shoulder after pillar three, raised half a metre so the first pillar sees the
            // enemy and its bolt over the next terrace instead of through it. This is the red Insight
            // carrier: its flare feeds the balloon branch.
            new Perch("T3_Perch_W", "Spawn_T3_Grunt", new Vector3(-18f, 23.5f, 220f), 140f, "T3_Pillar_1,T3_Pillar_2"),
            // Half a metre farther west preserves all three Heavy shot lines while keeping its support
            // outside the red portal sun's full unrelated-silhouette clearance budget.
            new Perch("T3_Perch_E", "Spawn_T3_Heavy", new Vector3(-15.5f, 27f, 254f), 60f, "T3_Span,T3_Step_1,T3_Step_2"),
        };

        // ---------------------------------------------------------------- the balloon arc
        // T3: an ARC of four orbs west of the pillar hops, from P1's edge to a fall onto T3_Span.
        // MEASURED, not copied from the yard: a pop is 11 m/s up with the carry trimmed to 9 m/s AND a
        // 0.45 s float at 0.55 gravity, so it apexes ~3.5 m above the orb about 5 m out. The next orb
        // therefore sits ~5 m across and ~3 m UP (the yard's 1.2 m rise was laid before the float and a
        // pop sails 2 m over it). Flown in LevelTraversalTests: entry-jump into orb 1 (4 m out, 3 m up),
        // three pops, and the fall from orb 4 lands on the span, short of the fallen lintel.
        public const float BalloonLaunch = 11f, BalloonRadius = 1.1f, BalloonRespawn = 2.5f;
        // Optional west-side aerial line. The broad pillar landings remain the readable normal route;
        // this arc restores the old vertical skill line at the larger scale and rejoins on T3_Span.
        public static readonly Vector3[] T3Arc =
        {
            new Vector3(-8f, 24f, 199f),
            new Vector3(-12f, 27f, 205f),
            new Vector3(-14f, 30f, 212f),
            new Vector3(-11f, 33f, 220f),
        };

        // ---------------------------------------------------------------- the ramps (2026-09-07)
        //
        // THE LEVEL'S FIRST SLOPED GEOMETRY. `LevelPieceKind.Ramp` landed with a motor that reads the real
        // ground normal, so a slide now gains speed downhill and BLEEDS it uphill through the same term
        // (TraversalMath.SlopeAccel, magnitude g*sin*cos*0.85 = 25.5*sin(t)*cos(t) m/s^2 on the shipped
        // slideSlopeAccel). Two consequences decided every number below:
        //
        //   1. LEVEL_01 HAS NO DESCENT. It climbs monotonically from y 0 at the start pad to y 28 in the
        //      boss room; measured deck by deck, not one route hop in the level goes down. So every ramp
        //      here is a CLIMB, and not one of them can pay a downhill slide. A ramp's job in this level
        //      is therefore CONTINUITY - the run never leaves the ground - and never speed gained.
        //   2. A RAMP IS NOT A KICKER. A CharacterController leaving a ramp's top edge keeps its
        //      horizontal velocity and gets no vertical impulse, so a ramp that stops short of the next
        //      deck is just a jump with a shorter run-up. Every ramp here MEETS the deck it serves, base
        //      and top both overlapping by 0.2 m or more, so the two surfaces are flush and seamless.
        //
        // ANGLE IS DERIVED, NEVER AUTHORED (docs/AUTHORING.md 1c): 7.3 / 7.5 / 11.8 / 14.4 / 15.4 deg.
        // The ceiling is ~35 (a CharacterController's 45 deg slopeLimit turns anything steeper into a
        // wall on the way up); nothing here is close, because nothing here had the height to spend.
        //
        // MATERIAL: "Stone", not "Platform", on the vfx team's call. A ramp cannot wear trim - the trim
        // builder places world-axis bars and a ramp has no world-axis edges - so it is the only route
        // piece in the level with no neon edge, and in M_Platform (#475262) at 7 deg against a deck of
        // the identical hue and value it reads as NOTHING until you are on it. M_Stone (#2A3443) is 2.5x
        // darker, non-emissive, in the same cold family: it separates on VALUE, which costs no light, no
        // bloom budget and no place in the trim-hue grammar (T1 cyan / T2 gold / T3 red is the SPAN word
        // and a ramp must not speak it). The perches are Stone too, but they wear pink trim and hang
        // beside the course; a trimless dark slab lying between two lit decks cannot be read as a perch.
        public struct Ramp
        {
            public string name; public Vector3 basePosition;
            public float width, run, rise, yaw; public string why;
            public Ramp(string n, Vector3 b, float w, float ru, float ri, float y, string wy)
            { name = n; basePosition = b; width = w; run = ru; rise = ri; yaw = y; why = wy; }
        }

        public const float RampThickness = 0.5f;

        static Ramp RampBetween(string name, Vector3 bottom, Vector3 top, float width, string why)
        {
            Vector3 horizontal = top - bottom;
            horizontal.y = 0f;
            return new Ramp(name, bottom, width, horizontal.magnitude, top.y - bottom.y,
                            Mathf.Atan2(horizontal.x, horizontal.z) * Mathf.Rad2Deg, why);
        }

        // Five short connectors restore grounded flow between selected landings without narrowing the
        // expanded decks. Each endpoint sits on a walkable top; the derived angles remain below 35 degrees.
        public static readonly Ramp[] Ramps =
        {
            RampBetween("T1_Ramp_Stone12", new Vector3(3f, 0f, 16.5f), new Vector3(3f, 0.5f, 18f), 4f,
                        "a full-width first grade preserves momentum into the east landing"),
            RampBetween("T1_Ramp_Stone34", new Vector3(-2f, 1f, 34.8f), new Vector3(-2f, 1.5f, 36.2f), 4f,
                        "a compact grade keeps the west-to-centre switch grounded"),
            RampBetween("T1_Ramp_Causeway", new Vector3(-5f, 1.5f, 43.5f), new Vector3(-5f, 2f, 46f), 3f,
                        "a diagonal shoulder grade feeds the widened causeway while leaving its middle open"),
            RampBetween("T2_Ramp_L2_L3", new Vector3(14f, 6.5f, 127f), new Vector3(10f, 8f, 130f), 3f,
                        "the lower spiral grade turns the east balcony into the broad north traverse"),
            RampBetween("T2_Ramp_L8_L9", new Vector3(14f, 15.5f, 127f), new Vector3(11f, 17f, 130f), 3f,
                        "the upper spiral repeats the readable diagonal at greater height"),
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
            // its end leaves at the full carry. A LINE the tech route rides. T1_Fast_1 is now 8 x 12 at
            // (10, 0, 25); the sheet stays inset from every shoulder edge. Its centre is 0.1 m east of
            // T1_Stone_2's edge so the two coplanar, overlapping decks have one unambiguous water owner.
            new Water("T1_Water_Fast", new Vector3(10.1f, 0.52f, 25f), new Vector3(7.4f, 0.04f, 11.4f), Vector3.forward, 6f),
            // The T3 span after the fallen lintel (top 24.5): skate the run, then the last sheet turns the
            // flow toward T3_Step_1 at (3, 25.5, 241) — a line that TURNS the run (rule 7).
            new Water("T3_Water_Span", new Vector3(0f, 24.52f, 235f), new Vector3(15.4f, 0.04f, 13.4f), Vector3.forward, 6f),
            new Water("T3_Water_Turn", new Vector3(1f, 24.52f, 240.8f), new Vector3(8f, 0.04f, 1.6f), new Vector3(0.25f, 0f, 0.97f), 6f),
        };

        // The connective course is authored as broad landing-to-landing motion, in canonical coordinates
        // before solar spacing translates T2 +38 m and T3 +70 m. These generous decks are the normal line;
        // restored walls, vertical props and the hand-marked flare line sit beside or above them as faster,
        // harder skill-expression routes rather than gates on basic completion.
        public static readonly Reshape[] OpenCourseDecks =
        {
            new Reshape("T1_Stone_1", new Vector3(0f, -0.5f, 13f), new Vector3(12f, 1f, 8f), "open launch plaza"),
            new Reshape("T1_Stone_2", new Vector3(4f, 0f, 22f), new Vector3(12f, 1f, 8f), "broad east landing"),
            new Reshape("T1_Stone_3", new Vector3(-4f, 0.5f, 31f), new Vector3(12f, 1f, 8f), "broad west landing"),
            new Reshape("T1_Stone_4", new Vector3(0f, 1f, 40.5f), new Vector3(14f, 1f, 9f), "first projectile interception deck"),
            new Reshape("T1_Causeway", new Vector3(0f, 1.5f, 51.6f), new Vector3(14f, 1f, 12.8f), "open parry runway before the cyan portal"),
            new Reshape("T1_Fast_1", new Vector3(10f, 0f, 25f), new Vector3(8f, 1f, 12f), "optional water shoulder, outside the main line"),

            new Reshape("T2_L1", new Vector3(14f, 4.5f, 113.375f), new Vector3(12f, 1f, 10f), "lower east terrace"),
            new Reshape("T2_L2", new Vector3(18f, 6f, 124f), new Vector3(10f, 1f, 8f), "lower east turn"),
            new Reshape("T2_L3", new Vector3(2f, 7.5f, 132.5f), new Vector3(18f, 1f, 8f), "lower north traverse"),
            new Reshape("T2_L4", new Vector3(-13f, 9f, 124f), new Vector3(12f, 1f, 8f), "lower west turn"),
            new Reshape("T2_L5", new Vector3(-16f, 10.5f, 113f), new Vector3(10f, 1f, 9f), "lower west terrace"),
            new Reshape("T2_L6", new Vector3(0f, 12f, 107f), new Vector3(18f, 1f, 10f), "wide south crossover"),
            new Reshape("T2_L7", new Vector3(16f, 13.5f, 113f), new Vector3(12f, 1f, 9f), "upper east terrace"),
            new Reshape("T2_L8", new Vector3(18f, 15f, 124f), new Vector3(10f, 1f, 8f), "upper east turn"),
            new Reshape("T2_L9", new Vector3(3f, 16.5f, 132.5f), new Vector3(18f, 1f, 8f), "upper north traverse"),
            new Reshape("T2_L10", new Vector3(16f, 18f, 141f), new Vector3(12f, 1f, 8f), "upper projectile approach"),
            new Reshape("T2_L11", new Vector3(0f, 19.5f, 145.5f), new Vector3(18f, 1f, 5f), "gold portal launch deck"),

            new Reshape("T3_Pillar_1", new Vector3(0f, 15.5f, 196f), new Vector3(14f, 12f, 8f), "broad first red landing"),
            new Reshape("T3_Pillar_2", new Vector3(4f, 16.5f, 205f), new Vector3(16f, 12f, 8f), "broad second red landing"),
            new Reshape("T3_Pillar_3", new Vector3(-4f, 17.5f, 214f), new Vector3(16f, 12f, 8f), "broad third red landing"),
            new Reshape("T3_Pillar_4", new Vector3(0f, 18.5f, 223f), new Vector3(14f, 12f, 8f), "broad fourth red landing"),
            new Reshape("T3_Span", new Vector3(0f, 24f, 235f), new Vector3(16f, 1f, 14f), "wide water runway"),
            new Reshape("T3_Step_1", new Vector3(3f, 25f, 246f), new Vector3(16f, 1f, 6f), "first exit terrace"),
            new Reshape("T3_Step_2", new Vector3(-3f, 26.5f, 253f), new Vector3(16f, 1f, 6f), "azure portal launch deck"),
        };

        public struct HybridStructure
        {
            public string name; public Vector3 center, size; public string materialKey, trimMaterialKey, why;
            public bool trim;
            public HybridStructure(string n, Vector3 c, Vector3 s, string material, bool hasTrim,
                                   string trimMaterial, string reason)
            {
                name = n; center = c; size = s; materialKey = material; trim = hasTrim;
                trimMaterialKey = trimMaterial; why = reason;
            }
        }

        // The 2026-09-10 hybrid pass restores the course's silhouettes and secondary movement lines at
        // the scale of OpenCourseDecks. Main landings stay broad; walls and posts live on their shoulders.
        public static readonly HybridStructure[] HybridCourseStructures =
        {
            new HybridStructure("T1_Rail_L", new Vector3(-7.1f, 2.325f, 51.6f), new Vector3(.2f, .65f, 12.8f), "Stone", false, "", "low outer rail keeps the wider bridge silhouette without hiding a sliding view"),
            new HybridStructure("T1_Rail_R", new Vector3(7.1f, 2.325f, 51.6f), new Vector3(.2f, .65f, 12.8f), "Stone", false, "", "matching low outer rail"),
            new HybridStructure("T1_Obelisk_W", new Vector3(-9.5f, 4.5f, 50f), new Vector3(1.4f, 9f, 1.4f), "Stone", true, "NeonCyan", "a tall waypoint outside the fourteen-metre runway"),
            new HybridStructure("T1_Fallen_Obelisk", new Vector3(0f, 3.7f, 48f), new Vector3(15f, .8f, 1.2f), "Stone", true, "NeonCyan", "a readable slide-under gate across the expanded causeway"),
            new HybridStructure("T1_Wall_Start", new Vector3(15.5f, 4f, 25f), new Vector3(1.2f, 10f, 24f), "Stone", true, "NeonCyan", "an optional wall line beside the water shoulder"),
            new HybridStructure("T1_Wall_Causeway", new Vector3(10f, 4f, 51.5f), new Vector3(1.2f, 10f, 20f), "Stone", true, "NeonCyan", "a second wall line outside the open causeway"),
            new HybridStructure("T1_Wall_Landing", new Vector3(10f, 2.5f, 60.5f), new Vector3(8f, 1f, 8f), "Platform", true, "NeonCyan", "a generous wall-run rejoin shelf outside the cyan sun"),

            new HybridStructure("T2_Tower", new Vector3(8f, 12f, 124f), new Vector3(4f, 20f, 5f), "Stone", true, "NeonYellow", "the signature core stays vertical but no longer hides the helix entry or the lower sentry"),
            new HybridStructure("T2_Buttress", new Vector3(12.2f, 10.5f, 124f), new Vector3(.8f, 9f, 4f), "Stone", true, "NeonYellow", "a 1.8 m chimney remains beside, rather than inside, the lower east terrace"),
            new HybridStructure("T2_Wall_East", new Vector3(24.5f, 10f, 126f), new Vector3(1.2f, 14f, 30f), "Stone", true, "NeonYellow", "an outer east wall-run line leaves the broad balcony untouched"),
            new HybridStructure("T2_Wall_Landing_East", new Vector3(18f, 18f, 142.5f), new Vector3(10f, 1f, 8f), "Platform", true, "NeonYellow", "east expert line rejoins on the upper terrace with clear solar silhouette margin"),
            new HybridStructure("T2_Wall_West", new Vector3(-22.5f, 13f, 116f), new Vector3(1.2f, 14f, 30f), "Stone", true, "NeonYellow", "an outer west wall-run line frames the second lap"),
            new HybridStructure("T2_Wall_Landing_West", new Vector3(-14.5f, 12f, 99f), new Vector3(13f, 1f, 8f), "Platform", true, "NeonYellow", "west wall-run rejoin overlaps the wide south crossover"),

            new HybridStructure("T3_Fallen_Lintel", new Vector3(0f, 26.15f, 231f), new Vector3(17f, .8f, 1.2f), "Stone", true, "NeonRed", "a full-span slide gate restores the red runway's vertical rhythm"),
            new HybridStructure("T3_Obelisk_W1", new Vector3(-11f, 23f, 231f), new Vector3(1.2f, 13f, 1.2f), "Stone", true, "NeonRed", "outer posts make the span read at speed"),
            new HybridStructure("T3_Obelisk_W2", new Vector3(-11f, 23f, 239f), new Vector3(1.2f, 13f, 1.2f), "Stone", true, "NeonRed", "outer posts make the span read at speed"),
            new HybridStructure("T3_Obelisk_E1", new Vector3(11f, 23f, 231f), new Vector3(1.2f, 13f, 1.2f), "Stone", true, "NeonRed", "outer posts make the span read at speed"),
            new HybridStructure("T3_Obelisk_E2", new Vector3(11f, 23f, 239f), new Vector3(1.2f, 13f, 1.2f), "Stone", true, "NeonRed", "outer posts make the span read at speed"),
            new HybridStructure("T3_Recovery_W1", new Vector3(-9f, 21f, 230f), new Vector3(1.2f, 9f, 1.2f), "Stone", false, "", "low recovery post outside the eight-metre deck edge"),
            new HybridStructure("T3_Recovery_W2", new Vector3(-9f, 21f, 238f), new Vector3(1.2f, 9f, 1.2f), "Stone", false, "", "low recovery post outside the eight-metre deck edge"),
            new HybridStructure("T3_Recovery_E1", new Vector3(9f, 21f, 230f), new Vector3(1.2f, 9f, 1.2f), "Stone", false, "", "low recovery post outside the eight-metre deck edge"),
            new HybridStructure("T3_Recovery_E2", new Vector3(9f, 21f, 238f), new Vector3(1.2f, 9f, 1.2f), "Stone", false, "", "low recovery post outside the eight-metre deck edge"),
            new HybridStructure("T3_Wall_Pillars", new Vector3(13.5f, 24f, 205f), new Vector3(1.2f, 10f, 30f), "Stone", true, "NeonRed", "an east wall-run bypass beside the four broad pillars"),
            new HybridStructure("T3_Wall_Landing_S", new Vector3(10.5f, 24.5f, 222f), new Vector3(9f, 1f, 8f), "Platform", true, "NeonRed", "wall line rejoins at the red span approach"),
            new HybridStructure("T3_Wall_Span", new Vector3(11f, 29f, 235f), new Vector3(1.2f, 10f, 26f), "Stone", true, "NeonRed", "a high line runs beside the water span and its lintel"),
        };

        static int ApplyOpenProjectileCourse(LevelDefinition def)
        {
            var platforms = new List<PlatformDef>(def.platforms ?? new PlatformDef[0]);
            int written = 0;
            foreach (var shape in OpenCourseDecks)
            {
                var platform = platforms.Find(p => p != null && p.name == shape.name);
                if (platform == null)
                    throw new System.InvalidOperationException("Open projectile course requires " + shape.name + ".");
                platform.center = shape.center;
                platform.size = shape.size;
                written++;
            }
            foreach (var structure in HybridCourseStructures)
            {
                platforms.RemoveAll(p => p != null && p.name == structure.name);
                platforms.Add(new PlatformDef
                {
                    name = structure.name, center = structure.center, size = structure.size,
                    materialKey = structure.materialKey, trim = structure.trim,
                    trimMaterialKey = structure.trimMaterialKey, isStatic = true,
                });
                written++;
            }
            def.platforms = platforms.ToArray();
            return written;
        }

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
            var studioMetadata = CaptureStudioMetadata(def);
            // Solar spacing translates whole course sections. Put an already-authored asset back on the
            // canonical pre-spacing coordinates before the absolute rework tables below run, otherwise a
            // second Apply would mix freshly reset pieces with pieces that still carry the section shift.
            NormalizeSolarCourseSpacing(def);

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

            // This final shape layer supersedes the historical cramped spiral, rails, walls and obelisks.
            // It runs after the earlier compatibility reshapes so those values cannot leak into output.
            reshaped += ApplyOpenProjectileCourse(def);

            // The arena gates and triggers follow the widened doorways. Matched by gateName, X only, absolute.
            int gated = 0;
            if (def.arenas != null)
                foreach (var g in Gates)
                    foreach (var a in def.arenas)
                        if (a != null && a.gateName == g.gateName)
                        {
                            a.gateSize = new Vector3(g.width, a.gateSize.y, a.gateSize.z);
                            a.triggerSize = new Vector3(g.width, a.triggerSize.y, a.triggerSize.z);
                            if (g.exitToo) a.exitGateSize = new Vector3(g.width, a.exitGateSize.y, a.exitGateSize.z);
                            gated++;
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

            // Ramps: remove ours by name, re-add. The level ships with none, and only this pass authors
            // them, but the removal is by the "_Ramp_" infix rather than wholesale so a ramp dropped in by
            // the F10 editor and exported back survives a re-run of 8a — the same rule the perches follow.
            var ramps = new List<RampDef>(def.ramps ?? new RampDef[0]);
            ramps.RemoveAll(r => r != null && r.name != null && r.name.Contains("_Ramp_"));
            foreach (var rp in Ramps)
            {
                ramps.Add(new RampDef
                {
                    name = rp.name, basePosition = rp.basePosition, width = rp.width, run = rp.run,
                    rise = rp.rise, thickness = RampThickness, yaw = rp.yaw,
                    materialKey = "Stone", isStatic = true,
                });
            }
            def.ramps = ramps.ToArray();

            var waters = new List<WaterDef>();
            foreach (var w in Waters)
                waters.Add(new WaterDef { name = w.name, center = w.center, size = w.size, flowDirection = w.flow, flowSpeed = w.speed });
            def.waters = waters.ToArray();

            // Route beacons, the same way: remove ours by prefix, re-add. The level's hand-placed torches
            // (Torch_T1_*, Torch_Boss_* and the rest) are never touched — this pass owns "Torch_Beacon_".
            var torches = new List<TorchDef>(def.torches ?? new TorchDef[0]);
            torches.RemoveAll(t => t != null && t.name != null && t.name.StartsWith("Torch_Beacon_"));
            foreach (var b in OpenRouteBeacons)
                torches.Add(new TorchDef { name = b.name, basePosition = b.basePosition });
            def.torches = torches.ToArray();

            ApplyDescent(def);
            ApplyOpeningDescent(def);
            ApplySpawnLeaderboard(def);
            ApplySolarRealms(def);
            ApplyProjectileEncounterSequences(def);
            ApplyRunScoring(def);
            ApplyInsightRoutes(def);
            RestoreStudioMetadata(def, studioMetadata);
            var studioReport = ApplyLevelStudioMetadata(def);
            if (studioReport.errors.Count > 0)
                throw new System.InvalidOperationException("Level Studio metadata: " + string.Join("; ", studioReport.errors));

            return string.Format("Level_01 reworked: {0} boxes reshaped for openness, {1} perches, {2} spawns moved onto them, " +
                                 "{3} balloons (T3 arc), {4} water sheets, {5} ramps, {6} route beacons, {7} arena doors widened; " +
                                 "{8} platforms and {9} torches total.",
                                 reshaped, Perches.Length, moved, balloons.Count, waters.Count, def.ramps.Length,
                                 OpenRouteBeacons.Length, gated, def.platforms.Length, def.torches.Length);
        }

        /// <summary>
        /// Writes only Level Studio metadata. Safe to call on the current shipped layout without
        /// rerunning historical geometry reworks. Names/aliases follow LEVEL-VOCABULARY.md; physical
        /// bounds follow the measured post-spacing anchor audit and leave 0.1 m gaps between zones.
        /// </summary>
        public static ZoneAssignmentReport ApplyLevelStudioMetadata(LevelDefinition def)
        {
            if (def == null) return LevelObjectCatalog.AssignZones(null);
            def.zones = new[]
            {
                StudioZone("T0", "Opening Descent", "Opening", 0, -166f, 7.8f, Color.cyan,
                    "first ramp", "opening ramp", "starting descent", "reliquary landing"),
                StudioZone("T1", "Stone Causeway", "Ninja", 1, 7.9f, 136.7f, new Color(0.3f, 0.8f, 1f),
                    "first parkour section", "causeway", "Ninja section"),
                StudioZone("T2", "Helix Tower", "Knight", 2, 136.8f, 260.7f, new Color(1f, 0.8f, 0.2f),
                    "tower", "wall-run tower", "Knight section"),
                StudioZone("T3", "Balloon Aqueduct", "Spellsword", 3, 260.8f, 392.9f, new Color(0.3f, 0.5f, 1f),
                    "balloon section", "water span", "Spellsword section"),
                StudioZone("T4", "Warden Descent", "Warden", 4, 393f, 520f, new Color(0.8f, 0.6f, 1f),
                    "last ramp", "final ramp", "Warden approach", "boss approach")
            };
            // These audit-only sequences have no progress gates and retain their historical origin zero.
            // Their explicit metadata ownership must not move any gameplay/forecast coordinate.
            foreach (var sequence in def.projectileSequences ?? new ProjectileSequenceDef[0])
            {
                if (sequence == null) continue;
                if (sequence.meta == null) sequence.meta = new LevelObjectMeta();
                if (!string.IsNullOrEmpty(sequence.meta.zoneIdOverride)) continue;
                if (sequence.name == "T1_ParryRoute") sequence.meta.zoneIdOverride = "T1";
                else if (sequence.name == "T2_ParryRoute") sequence.meta.zoneIdOverride = "T2";
                else if (sequence.name == "T3_ParryRoute") sequence.meta.zoneIdOverride = "T3";
            }
            if (def.worldLeaderboard != null && def.worldLeaderboard.enabled)
            {
                if (def.worldLeaderboard.meta == null) def.worldLeaderboard.meta = new LevelObjectMeta();
                if (string.IsNullOrEmpty(def.worldLeaderboard.meta.zoneIdOverride)) def.worldLeaderboard.meta.zoneIdOverride = "T0";
            }
            // This historical pickup anchor is moved into the Boss Solar Realm by the builder.
            // It belongs to Warden/T4 even though its stored z=376 sits before the T4 entry.
            foreach (var pickup in def.pickups ?? new PickupDef[0])
            {
                if (pickup == null || pickup.name != "Pickup_Boss_Hook") continue;
                if (pickup.meta == null) pickup.meta = new LevelObjectMeta();
                if (string.IsNullOrEmpty(pickup.meta.zoneIdOverride)) pickup.meta.zoneIdOverride = "T4";
            }
            return LevelObjectCatalog.AssignZones(def);
        }

        static ZoneDef StudioZone(string id, string canonical, string split, int order,
            float zMin, float zMax, Color color, params string[] aliases)
        {
            // All main-course anchors fit this finite envelope. Remote realm points inherit arena
            // ownership rather than expanding the primary course volumes out to x=700.
            return new ZoneDef { zoneId = id, canonicalName = canonical, splitName = split, order = order,
                center = new Vector3(0f, 32f, (zMin + zMax) * 0.5f), size = new Vector3(256f, 192f, zMax - zMin),
                displayColor = color, aliases = aliases };
        }

        static Dictionary<string, LevelObjectMeta> CaptureStudioMetadata(LevelDefinition def)
        {
            var result = new Dictionary<string, LevelObjectMeta>(System.StringComparer.Ordinal);
            foreach (var record in LevelObjectCatalog.Enumerate(def))
            {
                string key = StudioMetadataKey(record);
                if (result.ContainsKey(key))
                    throw new System.InvalidOperationException("Ambiguous authoring metadata owner: " + key);
                result.Add(key, new LevelObjectMeta { objectId = record.meta.objectId,
                    friendlyName = record.meta.friendlyName, zoneIdOverride = record.meta.zoneIdOverride });
            }
            return result;
        }

        static void RestoreStudioMetadata(LevelDefinition def, Dictionary<string, LevelObjectMeta> previous)
        {
            foreach (var record in LevelObjectCatalog.Enumerate(def))
            {
                LevelObjectMeta saved;
                if (!previous.TryGetValue(StudioMetadataKey(record), out saved)) continue;
                record.meta.objectId = saved.objectId;
                record.meta.friendlyName = saved.friendlyName;
                record.meta.zoneIdOverride = saved.zoneIdOverride;
            }
        }

        static string StudioMetadataKey(LevelObjectRecord record)
        {
            string key = record.kind + "/";
            if (record.owner != null)
            {
                key += StudioMetadataKey(record.owner);
                if (record.kind == LevelObjectKind.ProjectileEngagementWindow)
                {
                    var window = (ProjectileEngagementWindowDef)record.data;
                    var siblings = ((ProjectileSequenceDef)record.owner.data).engagementWindows;
                    int occurrence = 0;
                    for (int i = 0; i < record.index; i++)
                        if (siblings[i] != null && siblings[i].spawnerName == window.spawnerName) occurrence++;
                    key += "/" + window.spawnerName + "/" + occurrence;
                }
                return key;
            }
            if (record.index < 0) return key;
            if (record.kind == LevelObjectKind.Arena) return key + ((ArenaDef)record.data).gateName;
            if (record.kind == LevelObjectKind.InsightRoute) return key + ((InsightRouteDef)record.data).routeId;
            if (record.kind == LevelObjectKind.RunSplit) return key + ((RunSplitDef)record.data).endSpawnerName;
            // Every other top-level repeatable definition uses an existing, load-bearing name field.
            return key + (string)record.data.GetType().GetField("name").GetValue(record.data);
        }

        /// <summary>Writes the campaign run contract as level data, never as a scorer-side level special case.</summary>
        static void ApplyRunScoring(LevelDefinition def)
        {
            // User contract: every scored run answers all three sub-bosses, the Warden, and any four
            // authored regular enemies. 400 + 600 + 900 + 1500 + (4 * 40) = 3560 baseline souls;
            // split bonuses are additional rewards and never substitute for the boss/regular gates.
            def.requiredRunSouls = 3560;
            def.requiredRegularKills = 4;
            def.gradeBonuses = new RunGradeBonusDef { dSouls = 0, cSouls = 25, bSouls = 50, aSouls = 75, sSouls = 100 };
            def.runSplits = new[]
            {
                Split("Ninja", "Spawn_Legendary_Ninja", 55f),
                Split("Knight", "Spawn_Legendary_Knight", 60f),
                Split("Spellsword", "Spawn_Legendary_Spellsword", 70f),
                Split("Warden", "Spawn_Boss", 40f),
            };
        }

        /// <summary>
        /// Signposts the optional flare shortcuts with a world-space hand marker and gives developer
        /// timing capture one shared entry/rejoin interval per section. Crossing these boxes changes no
        /// gameplay; taking an attributed flare is what selects the expert branch inside the interval.
        /// Positions are final world coordinates because this runs after <see cref="ApplySolarRealms"/>.
        /// </summary>
        static void ApplyInsightRoutes(LevelDefinition def)
        {
            def.insightRoutes = new[]
            {
                new InsightRouteDef
                {
                    routeId = "T1_Insight_Flare",
                    sourceSpawnerNames = new[] { "Spawn_T1_GruntA", "Spawn_T1_GruntB" },
                    markerPosition = new Vector3(9f, 4.5f, 38f),
                    markerEulerAngles = new Vector3(0f, 180f, 0f),
                    markerScale = Vector3.one * 1.5f,
                    markerMaterialKey = "NeonCyan",
                    entryCenter = new Vector3(0f, 5f, 37f),
                    entrySize = new Vector3(42f, 16f, 10f),
                    rejoinCenter = new Vector3(0f, 7f, 70f),
                    rejoinSize = new Vector3(30f, 20f, 10f),
                },
                new InsightRouteDef
                {
                    routeId = "T2_Insight_Flare",
                    sourceSpawnerNames = new[] { "Spawn_T2_GruntB", "Spawn_T2_GruntA" },
                    markerPosition = new Vector3(-18f, 13f, 150f),
                    markerEulerAngles = new Vector3(0f, 180f, 0f),
                    markerScale = Vector3.one * 1.5f,
                    markerMaterialKey = "NeonYellow",
                    entryCenter = new Vector3(0f, 13f, 149f),
                    entrySize = new Vector3(52f, 32f, 12f),
                    rejoinCenter = new Vector3(0f, 22f, 198f),
                    rejoinSize = new Vector3(32f, 24f, 12f),
                },
                new InsightRouteDef
                {
                    routeId = "T3_Insight_Flare",
                    sourceSpawnerNames = new[] { "Spawn_T3_Grunt" },
                    markerPosition = new Vector3(-9f, 25f, 274f),
                    markerEulerAngles = new Vector3(0f, 180f, 0f),
                    markerScale = Vector3.one * 1.5f,
                    markerMaterialKey = "NeonRed",
                    entryCenter = new Vector3(0f, 25f, 276f),
                    entrySize = new Vector3(42f, 28f, 12f),
                    rejoinCenter = new Vector3(0f, 30f, 339f),
                    rejoinSize = new Vector3(32f, 24f, 12f),
                },
            };
        }

        static RunSplitDef Split(string name, string endSpawnerName, float sSeconds)
        {
            return new RunSplitDef
            {
                name = name,
                endSpawnerName = endSpawnerName,
                sSeconds = sSeconds,
                aSeconds = sSeconds * 1.15f,
                bSeconds = sSeconds * 1.30f,
                cSeconds = sSeconds * 1.50f,
            };
        }

        /// <summary>
        /// Gives every campaign projectile enemy one data-authored route-audit window. Geometry proves where
        /// a shot is useful; it does not become an invisible runtime trigger. The explicitly
        /// progress-gated opening and final-ramp rows are sequence-controlled; ordinary route sentries retain
        /// autonomous range/LOS/facing fire. Another level can author the same evidence without level-specific AI code.
        /// </summary>
        public static void ApplyProjectileEncounterSequences(LevelDefinition def)
        {
            var sequences = new List<ProjectileSequenceDef>(def.projectileSequences ?? new ProjectileSequenceDef[0]);
            string[] owned = { "T1_ParryRoute", "T2_ParryRoute", "T3_ParryRoute", "T4_SurgeRoute" };
            sequences.RemoveAll(s => s != null && System.Array.IndexOf(owned, s.name) >= 0);

            var opening = sequences.Find(s => s != null && s.name == "T0_SurgeVolley");
            var openingRamp = FindRamp(def, "T0_Ramp_Descent");
            if (opening != null && openingRamp != null)
            {
                // The five Surge beats teach the hill once. The two Heavy sources are the sustained
                // closing rhythm and alternate forever after the first complete pass.
                opening.repeatFromIndex = 5;
                Vector3 start = openingRamp.basePosition + Vector3.up * 1.2f;
                Vector3 end = openingRamp.TopPosition + Vector3.up * 1.2f;
                opening.engagementWindows = new[]
                {
                    Window("Spawn_T0_Surge_1", start, end, 7f, 3f, 3f, 24f),
                    Window("Spawn_T0_Surge_2", start, end, 7f, 3f, 20f, 46f),
                    Window("Spawn_T0_Surge_3", start, end, 7f, 3f, 42f, 70f),
                    Window("Spawn_T0_Surge_4", start, end, 7f, 9f, 66f, 94f),
                    Window("Spawn_T0_Surge_5", start, end, 7f, 9f, 90f, 130f),
                    Window("Spawn_T0_Reliquary_1", start, end, 7f, 3f, 90f, 130f),
                    Window("Spawn_T0_Reliquary_2", start, end, 7f, 3f, 112f, 143f),
                };
            }

            // T1 owns one main-line beat and one optional east-line beat. Broad corridors accommodate
            // the authored zigzag while their finite end prevents either sentry from shooting into T2.
            Vector3 t1Start = DeckChest(def, "T1_Stone_4", 0f, -3f);
            sequences.Add(Sequence("T1_ParryRoute",
                new[] { "Spawn_T1_GruntA", "Spawn_T1_GruntB" }, 1.75f,
                new[]
                {
                    WindowToEnd("Spawn_T1_GruntA", t1Start, DeckChest(def, "T1_Causeway", 0f, 4f), 12f, 3f, 2f, 1f),
                    WindowToEnd("Spawn_T1_GruntB", t1Start, DeckChest(def, "T1_Causeway", 0f, 5f), 12f, 3f, 6f, 1f),
                }));

            // The two stacked T2 laps deliberately use different Y ranges. The west sentry owns the
            // lower turn; the east sentry cannot arm until the upper lap, even though XZ overlaps it.
            sequences.Add(Sequence("T2_ParryRoute",
                new[] { "Spawn_T2_GruntB", "Spawn_T2_GruntA" }, 1.75f,
                new[]
                {
                    WindowToEnd("Spawn_T2_GruntB", DeckChest(def, "T2_L2", 0f, -3f),
                                DeckChest(def, "T2_L4", 0f, 2f), 16f, 3.2f, 2f, 1f),
                    WindowToEnd("Spawn_T2_GruntA", DeckChest(def, "T2_L9", 0f, -2f),
                                DeckChest(def, "T2_L11", 0f, 1f), 16f, 3.2f, 2f, 1f),
                }));

            // The ordinary sentry reads across the pillar approach. The Heavy's three-contact phrase gets
            // the entire widened water span and both exit terraces: over three seconds at base run speed.
            sequences.Add(Sequence("T3_ParryRoute",
                new[] { "Spawn_T3_Grunt", "Spawn_T3_Heavy" }, 2.2f,
                new[]
                {
                    WindowToEnd("Spawn_T3_Grunt", DeckChest(def, "T3_Pillar_1", 0f, -3f),
                                DeckChest(def, "T3_Pillar_3", 0f, 3f), 18f, 8f, 2f, 1f),
                    WindowToEnd("Spawn_T3_Heavy", DeckChest(def, "T3_Span", 0f, -10f),
                                DeckChest(def, "T3_Step_2", 0f, 3f), 12f, 8f, 4f, 2f),
                }));

            var t4Ramp = FindRamp(def, "T4_Ramp_Descent");
            Vector3 t4Start = t4Ramp.basePosition + Vector3.up * 1.2f;
            Vector3 t4End = DeckChest(def, "Boss_Approach", 0f, 5f);
            var finalRamp = Sequence("T4_SurgeRoute",
                new[] { "Spawn_T4_Surge_1", "Spawn_T4_Surge_2", "Spawn_T4_Surge_3" }, 1.35f,
                new[]
                {
                    Window("Spawn_T4_Surge_1", t4Start, t4End, 10f, 4f, 8f, 30f),
                    Window("Spawn_T4_Surge_2", t4Start, t4End, 10f, 4f, 20f, 42f),
                    Window("Spawn_T4_Surge_3", t4Start, t4End, 10f, 4f, 32f, 48f),
                });
            // The final ramp is too fast for three independent acquire clocks: a legal first shot could
            // still resolve late enough that the third turret saw the player from behind and rejected
            // FacingAway. One data-owned coordinator serializes the three real single-shot turrets while
            // each keeps its global shooter rules. Perches stay ahead of their intended contact so every
            // answer remains inside the unchanged 75-degree forward parry cone while looking down-route.
            finalRamp.progressOrigin = t4Start;
            finalRamp.progressDirection = t4Ramp.Heading;
            finalRamp.memberProgressGates = new[] { 0f, 14f, 28f };
            // The boss approach already presents the whole row before the lip. Starting immediately at
            // gate zero puts all three minimum-cue contacts on the ramp instead of spending 0.7 s twice.
            finalRamp.firstMemberAcquireDelay = 0f;
            sequences.Add(finalRamp);

            def.projectileSequences = sequences.ToArray();
        }

        static ProjectileSequenceDef Sequence(string name, string[] members, float timeout,
                                               ProjectileEngagementWindowDef[] windows)
        {
            return new ProjectileSequenceDef
            {
                name = name,
                spawnerNames = members,
                recoveryGap = 0.11f,
                readinessTimeout = timeout,
                shotResolutionTimeout = 1.75f,
                engagementWindows = windows,
            };
        }

        static ProjectileEngagementWindowDef WindowToEnd(string spawner, Vector3 start, Vector3 end,
                                                          float width, float height, float arrivalStart,
                                                          float endMargin)
        {
            float length = Vector2.Distance(new Vector2(start.x, start.z), new Vector2(end.x, end.z));
            return Window(spawner, start, end, width, height, arrivalStart,
                          Mathf.Max(arrivalStart + 0.5f, length - endMargin));
        }

        static ProjectileEngagementWindowDef Window(string spawner, Vector3 start, Vector3 end,
                                                     float width, float height, float arrivalStart,
                                                     float arrivalEnd)
        {
            return new ProjectileEngagementWindowDef
            {
                spawnerName = spawner,
                routeStart = start,
                routeEnd = end,
                halfWidth = width,
                heightTolerance = height,
                arrivalStart = arrivalStart,
                arrivalEnd = arrivalEnd,
            };
        }

        static Vector3 DeckChest(LevelDefinition def, string name, float xOffset, float zOffset)
        {
            var deck = System.Array.Find(def.platforms, p => p != null && p.name == name);
            if (deck == null) throw new System.InvalidOperationException("Projectile route requires " + name + ".");
            return new Vector3(deck.center.x + xOffset, deck.center.y + deck.size.y * 0.5f + 1.2f,
                               deck.center.z + zOffset);
        }

        static RampDef FindRamp(LevelDefinition def, string name)
        {
            return def.ramps != null ? System.Array.Find(def.ramps, r => r != null && r.name == name) : null;
        }

        /// <summary>The final descent and its existing surge-turret encounter. Absolute, re-runnable data.</summary>
        public static void ApplyDescent(LevelDefinition def)
        {
            var platforms = new List<PlatformDef>(def.platforms);
            var boss = platforms.Find(p => p.name == "Boss_Arena");
            // ApplySolarSpacing removes the obsolete exterior court after this pass has used its old
            // anchor once. On the second application the already-authored T4 descent is the proof that
            // a missing Boss_Arena means "solar migration complete", not corrupt source data.
            if (boss == null && !platforms.Exists(p => p != null && p.name == "T4_Entry"))
                throw new System.InvalidOperationException("Descent requires Boss_Arena on an unmigrated level.");
            // Derive the translation from the current anchor: a second application moves nothing.
            Vector3 shift = boss != null ? new Vector3(0f, 15.5f, 390f) - boss.center : Vector3.zero;
            foreach (var p in platforms)
                if (p.name.Contains("Boss")) p.center += shift;
            // Apply's earlier Reshapes restores these two walls to the original arena coordinates.
            // Give them their final absolute positions even when the arena anchor has already moved.
            foreach (var p in platforms)
            {
                if (p.name == "Wall_Boss_S_L") p.center = new Vector3(-11.75f, 18f, 370.75f);
                if (p.name == "Wall_Boss_S_R") p.center = new Vector3(11.75f, 18f, 370.75f);
            }
            foreach (var s in def.spawns)
                if (s.name == "Spawn_Boss") s.position += shift;
            foreach (var p in def.pickups)
                if (p.name.Contains("Boss")) p.position += shift;
            foreach (var t in def.torches)
                if (t.name.StartsWith("Torch_Boss_")) t.basePosition += shift;
            foreach (var a in def.arenas)
                if (a.gateName == "Boss_Gate")
                {
                    a.gateOpenPosition += shift; a.gateClosedPosition += shift;
                    a.triggerPosition += shift;
                    if (a.hasExitGate) { a.exitGateOpenPosition += shift; a.exitGateClosedPosition += shift; }
                }
            foreach (var c in def.checkpoints)
                if (c.name == "Checkpoint_4") c.position = new Vector3(0f, 16f, 361f);

            platforms.RemoveAll(p => p.name.StartsWith("T4_") || p.name.StartsWith("Boss_Approach_Rail_"));
            var approach = platforms.Find(p => p.name == "Boss_Approach");
            approach.center = new Vector3(0f, 15.5f, 358.8f);
            approach.size = new Vector3(10f, 1f, 24.4f);
            platforms.Add(new PlatformDef { name = "T4_Entry", center = new Vector3(0f, 27.5f, 291f),
                size = new Vector3(10f, 1f, 16f), materialKey = "Platform", trim = true });

            var ramps = new List<RampDef>(def.ramps);
            ramps.RemoveAll(r => r.name == "T4_Ramp_Descent");
            var descent = new RampDef { name = "T4_Ramp_Descent", basePosition = new Vector3(0f, 28f, 298.8f),
                width = 10f, run = 48f, rise = -12f, thickness = 0.5f, materialKey = "Stone" };
            ramps.Add(descent);
            def.ramps = ramps.ToArray();

            var spawns = new List<SpawnDef>(def.spawns);
            spawns.RemoveAll(s => s.name.StartsWith("Spawn_T4_Surge_"));
            float[] progress = { 24f, 36f, 48f };
            for (int i = 0; i < progress.Length; i++)
            {
                // Alternate sides and keep all three muzzles beside the actual sloped surface. The old
                // same-side row ended at z 354, eight metres beyond the ramp, so the last sentry shot a
                // fleeing player from the run-out. These progress values are measured from the ramp lip;
                // each perch remains ahead of its contact rather than producing a mechanically invalid
                // behind-the-shoulder arrival while the player correctly looks down the route.
                float z = descent.basePosition.z + progress[i];
                float y = descent.basePosition.y + descent.rise * Mathf.Clamp01((z - descent.basePosition.z) / descent.run);
                float x = i == 1 ? 7f : -7f;
                platforms.Add(new PlatformDef { name = "T4_TurretPad_" + (i + 1),
                    center = new Vector3(x, y - 0.5f, z), size = new Vector3(3f, 1f, 3f),
                    materialKey = "Stone", trim = true, trimMaterialKey = "NeonPink" });
                spawns.Add(new SpawnDef { name = "Spawn_T4_Surge_" + (i + 1), prefabKey = "pshooter_enemy03",
                    position = new Vector3(x, y + 0.1f, z), yaw = x > 0f ? 210f : 150f });
            }
            def.spawns = spawns.ToArray();
            def.platforms = platforms.ToArray();
            def.killZone.center = new Vector3(0f, -30f, 200f);
            def.killZone.size = new Vector3(200f, 2f, 500f);
        }

        /// <summary>
        /// A separate downhill opening before Ground_Start; the complete original route follows it.
        ///
        /// EVERYTHING HERE IS DERIVED FROM FOUR NUMBERS (OpeningRun / OpeningGrade / OpeningBottomZ /
        /// OpeningSeam) and a progress coordinate measured DOWN THE SLOPE FROM ITS TOP, because that is
        /// the coordinate the encounter is actually authored in: the volley's progressOrigin sits on the
        /// ramp's top edge, so a gate value and a perch value are the same kind of number and the whole
        /// ladder moves as one when the hill's length changes.
        ///
        /// 2026-09-07 rework, from play. Two reports: "the ramp needs to be longer at the top", and
        /// "the turret is not aggroing soon enough (the first one on the left)".
        ///  * The hill is 120 m -> 144 m of run at the SAME 1:4 grade. The bottom is pinned at
        ///    z -15.8 / y 0 (T0_RunOut and then Ground_Start follow it), so the extra 24 m appears at the
        ///    top: the crest rises y 30 -> y 36 and moves z -139.8 -> -165.6.
        ///  * progressOrigin moves WITH the ramp's top edge. This is the load-bearing part. If the origin
        ///    had stayed at z -135.8 the new 24 m would be pure run-up, the player would cross gate 0 at
        ///    ~21 m/s instead of ~13, and the first bolt's flight would fall from 0.31 s to 0.33 s while
        ///    arriving at 22 m/s - the OPPOSITE of what was asked for. With the origin on the lip, gate 0
        ///    is crossed at the slowest moment the player will ever have on this hill, exactly as before.
        ///  * The first perch moves from progress 22 to progress 30. That is the whole "aggro" fix, and it
        ///    is placement, not tuning. Every other beat in the ladder opens 26-32 m before its perch;
        ///    beat 1 alone opened at 22, so its bolt flew 0.31 s against 0.45-0.65 s for the rest. At 30 it
        ///    flies ~0.46 s and still contacts at ~15 m/s. In WORLD space the perch also climbs 17 m up the
        ///    hill (z -113.8 -> -129.8): it is now the first landmark on the slope, lit and tracking from
        ///    the crest, instead of a shape 25 m ahead and nearly beside the lane.
        ///  * memberProgressGates[0] IS 0 AND STAYS 0. It was raised to 14 earlier the same day to "buy
        ///    runway"; the user reported the sliding parry rhythm broke. A gate is a FLOOR on where a beat
        ///    may open, not a schedule - raising it deletes the first beat's approach and pushes the whole
        ///    sequential ladder later into the slide. Runway is bought by moving GEOMETRY, which is what
        ///    this pass does. Gate 1 moves 16 -> 18 only to follow beat 1's contact point 2 m down the
        ///    hill, so the pause between contact 1 and launch 2 stays at the shipped ~0.14 s.
        ///  * Perches 2-5 keep their progress (47 / 71 / 96 / 113) and therefore their arrival speeds and
        ///    contact spacing exactly; they simply sit 24 m further up the hill in world space. The 47 m
        ///    of empty slope below the last contact is the pass's other deliberate shape: the exhale, where
        ///    a five-stack 1.60x surge is finally FELT before the run-out hands the player to Ground_Start.
        /// </summary>
        public static void ApplyOpeningDescent(LevelDefinition def)
        {
            // The four numbers. The grade is 1:4 and the bottom is pinned; changing OpeningRun alone
            // lengthens the hill upward and carries the crest, the start, the ladder and the kill bounds.
            const float OpeningRun = 144f;
            const float OpeningGrade = 0.25f;
            const float OpeningBottomZ = -15.8f;
            const float OpeningSeam = 0.2f;      // deck/ramp overlap at both ends of the slope
            const float LaneWidth = 14f;

            float topZ = OpeningBottomZ - OpeningRun;      // -159.8
            float topY = OpeningRun * OpeningGrade;        // 36
            // A point at <paramref name="progress"/> metres down the slope, offset sideways and upward
            // from the walking surface. Every piece below is placed with this, so the hill's length is
            // the only thing that has to change to move all of it.
            Vector3 slope(float progress, float x, float above)
            {
                return new Vector3(x, topY - OpeningGrade * progress + above, topZ + progress);
            }

            // The crest is a threshold, not a room: 12 m deep so the drop is a decision rather than a
            // stumble, 14 m wide to match the lane exactly (a deck wider than its ramp is a lip over
            // nothing at the seam). From here the eye runs 148 m to the run-out.
            var platforms = new List<PlatformDef>(def.platforms);
            platforms.RemoveAll(p => p.name == "T0_Entry" || p.name == "T0_RunOut");
            const float EntryDepth = 12f;
            platforms.Add(new PlatformDef { name = "T0_Entry",
                center = new Vector3(0f, topY - 0.5f, topZ + OpeningSeam - EntryDepth * 0.5f),
                size = new Vector3(LaneWidth, 1f, EntryDepth), materialKey = "Platform", trim = true,
                trimMaterialKey = "NeonCyan" });
            platforms.Add(new PlatformDef { name = "T0_RunOut",
                center = new Vector3(0f, -0.5f, OpeningBottomZ - OpeningSeam + 8.2f * 0.5f),
                size = new Vector3(LaneWidth, 1f, 8.2f), materialKey = "Platform", trim = true,
                trimMaterialKey = "NeonCyan" });
            def.platforms = platforms.ToArray();

            var ramps = new List<RampDef>(def.ramps);
            ramps.RemoveAll(r => r.name == "T0_Ramp_Descent");
            ramps.Add(new RampDef { name = "T0_Ramp_Descent", basePosition = new Vector3(0f, topY, topZ),
                width = LaneWidth, run = OpeningRun, rise = -OpeningRun * OpeningGrade,
                thickness = RampThickness, materialKey = "Stone" });
            def.ramps = ramps.ToArray();

            // Five existing surge turrets punctuate the whole hill: LEFT, RIGHT, LEFT, then two overhead.
            // The lower pads alternate 0.2 m beyond the 14 m slide lane, tops flush with the slope so each
            // reads as a widening of the lane rather than an object in it. The last pair share one
            // connected open Z-shaped dais whose offset decks leave both downward shot lines clear.
            var spawns = new List<SpawnDef>(def.spawns);
            spawns.RemoveAll(s => s.name.StartsWith("Spawn_T0_Surge_") ||
                                  s.name.StartsWith("Spawn_T0_Reliquary_"));
            platforms.RemoveAll(p => p.name.StartsWith("T0_TurretPad_") ||
                                     p.name.StartsWith("T0_OverheadDais_") ||
                                     p.name.StartsWith("T0_ReliquaryPad_"));
            // Progress down the slope, in metres from the crest lip. 30 is the fix: it gives beat 1 the
            // 26-32 m of announcement every other beat already had.
            var lowerPerches = new[]
            {
                slope( 30f, -8.7f, 0.1f),
                slope( 47f,  8.7f, 0.1f),
                slope( 71f, -8.7f, 0.1f),
            };
            for (int i = 0; i < lowerPerches.Length; i++)
            {
                var shot = lowerPerches[i];
                string suffix = (i + 1).ToString();
                platforms.Add(new PlatformDef
                {
                    name = "T0_TurretPad_" + suffix,
                    center = new Vector3(shot.x, shot.y - 0.6f, shot.z),
                    size = new Vector3(3f, 1f, 3f), materialKey = "Stone", trim = true,
                    trimMaterialKey = "NeonCyan"
                });
                spawns.Add(new SpawnDef
                {
                    name = "Spawn_T0_Surge_" + suffix, prefabKey = "pshooter_enemy03",
                    position = shot, yaw = shot.x > 0f ? 210f : 150f
                });
            }

            // Two offset crowns joined around the east edge read as one floating platform without putting
            // a solid ceiling through the rear sentry's shot. Cyan marks the two occupied terraces; the
            // narrow stone spine, return and tapered undersides keep the silhouette light above the slope.
            // The group is RIGID: it is placed from one anchor on the front terrace and every other piece
            // is a local offset from it, so the tuned 6.75 m route clearance and the rear muzzle height
            // (too high a muzzle makes capped homing overfly a falling runner) survive any change to the
            // hill's length untouched.
            Vector3 dais = slope(97f, 0f, 7.25f);
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_Front", center = dais + new Vector3(3.2f, 0f, 0f),
                size = new Vector3(5f, 1f, 5f), materialKey = "Stone", trim = true,
                trimMaterialKey = "NeonCyan"
            });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_Rear", center = dais + new Vector3(-3.2f, -4.5f, 17f),
                size = new Vector3(5f, 1f, 4f), materialKey = "Stone", trim = true,
                trimMaterialKey = "NeonCyan"
            });
            for (int step = 0; step < 3; step++)
                platforms.Add(new PlatformDef
                {
                    name = "T0_OverheadDais_Spine_" + step,
                    center = dais + new Vector3(4.8f, -0.5f - step * 1.35f, 4.35f + step * 5.2f),
                    size = new Vector3(1.8f, 1.7f, 4.9f), materialKey = "Stone"
                });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_Return", center = dais + new Vector3(1.6f, -4.5f, 18f),
                size = new Vector3(7.8f, 0.8f, 1.8f), materialKey = "Stone"
            });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_FrontUnder", center = dais + new Vector3(3.2f, -0.15f, 0f),
                size = new Vector3(3.8f, 0.3f, 3.8f), materialKey = "Stone"
            });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_FrontKeel", center = dais + new Vector3(3.2f, -0.6f, 0f),
                size = new Vector3(2.2f, 0.6f, 2.2f), materialKey = "Stone"
            });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_RearUnder", center = dais + new Vector3(-3.2f, -4.45f, 17f),
                size = new Vector3(3.8f, 0.3f, 3.8f), materialKey = "Stone"
            });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_RearKeel", center = dais + new Vector3(-3.2f, -4.7f, 17f),
                size = new Vector3(2.2f, 0.2f, 2.2f), materialKey = "Stone"
            });

            var overheadShots = new[]
            {
                dais + new Vector3( 3.2f,  0.6f, -1f),   // progress 96, 7.6 m above the slope
                dais + new Vector3(-3.2f, -3.9f, 16f),   // progress 113, 7.35 m above the slope
            };
            for (int i = 0; i < overheadShots.Length; i++)
            {
                var shot = overheadShots[i];
                spawns.Add(new SpawnDef
                {
                    name = "Spawn_T0_Surge_" + (i + 4), prefabKey = "pshooter_enemy03",
                    position = shot, yaw = shot.x > 0f ? 195f : 165f
                });
            }

            // Two Heavy Reliquaries close the hill side-by-side on the bottom run-out flanks. A seam
            // placement made contact three pass behind a 27.5 m/s runner; z=6 keeps both sources ahead
            // through all six contacts while still reading as the base of the ramp. Their pads remain
            // outside Ground_Start's +/-8 m deck edge and never narrow the route.
            var reliquaryShots = new[]
            {
                new Vector3(-10f, 0.1f, 6f),
                new Vector3( 10f, 0.1f, 6f),
            };
            for (int i = 0; i < reliquaryShots.Length; i++)
            {
                var shot = reliquaryShots[i];
                string suffix = (i + 1).ToString();
                platforms.Add(new PlatformDef
                {
                    name = "T0_ReliquaryPad_" + suffix,
                    center = new Vector3(shot.x, shot.y - 0.6f, shot.z),
                    size = new Vector3(3f, 1f, 3f), materialKey = "Stone", trim = true,
                    trimMaterialKey = "NeonCyan"
                });
                spawns.Add(new SpawnDef
                {
                    name = "Spawn_T0_Reliquary_" + suffix, prefabKey = "pshooter_enemy02",
                    position = shot, yaw = shot.x > 0f ? 210f : 150f
                });
            }
            def.platforms = platforms.ToArray();
            def.spawns = spawns.ToArray();

            var sequences = new List<ProjectileSequenceDef>(def.projectileSequences ?? new ProjectileSequenceDef[0]);
            sequences.RemoveAll(s => s != null && s.name == "T0_SurgeVolley");
            sequences.Add(new ProjectileSequenceDef
            {
                name = "T0_SurgeVolley",
                spawnerNames = new[]
                {
                    "Spawn_T0_Surge_1", "Spawn_T0_Surge_2", "Spawn_T0_Surge_3",
                    "Spawn_T0_Surge_4", "Spawn_T0_Surge_5",
                    "Spawn_T0_Reliquary_1", "Spawn_T0_Reliquary_2"
                },
                // 0.08 s is the shipped successful-parry recovery; another 0.03 s gives input slack before
                // the next launch. A closing runner can contact sooner than the nominal 0.44 s flight, so
                // the live opening probe owns the actual contact-spacing proof.
                recoveryGap = 0.11f,
                readinessTimeout = 1.1f,
                // A Heavy phrase spans 0.84 s after its first predicted contact; do not retire a clean
                // third answer on the one-shot Surge timeout.
                shotResolutionTimeout = 1.75f,
                // The lip of the hill. Gate values are metres down the slope from here, which is also how
                // the perches above are placed, so a gate and its perch are directly comparable.
                progressOrigin = new Vector3(0f, 0f, topZ),
                progressDirection = Vector3.forward,
                // Announcement per beat (perch progress minus gate): 30, 29, 31, 32, 26. Before this pass
                // beat 1 alone opened at 22 and its bolt flew half as long as every other one.
                // DO NOT RAISE THE FIRST GATE. See the summary above - it was tried on 2026-09-07 and the
                // sliding parry rhythm broke. Move geometry instead.
                memberProgressGates = new[] { 0f, 18f, 40f, 64f, 87f, 90f, 112f }
            });
            def.projectileSequences = sequences.ToArray();

            // 2.5 m of crest ahead of the player: enough to read the hill and start the run, close enough
            // that perch 1 is inside its 36 m horizontal wake radius (33.6 m from here) at spawn, so it is
            // awake and tracking before the player has moved.
            def.playerStart = new Vector3(0f, topY + 0.3f, topZ - 2.5f);
            def.playerStartYaw = 0f;
            foreach (var pedestal in def.pedestals)
                if (pedestal.name == "WandPedestal_Start")
                    pedestal.groundPosition = new Vector3(3f, topY, topZ - 2.5f);

            // Catch falls behind the new crest while retaining the final arena's z 450 boundary.
            def.killZone.center = new Vector3(0f, -30f, 130f);
            def.killZone.size = new Vector3(200f, 2f, 640f);
        }

        /// <summary>
        /// Places the local-records board on the entry deck behind the final authored spawn. Keeping this
        /// derived from playerStart makes repeated authoring deterministic when the opening descent moves.
        /// </summary>
        public static void ApplySpawnLeaderboard(LevelDefinition def)
        {
            if (def == null) return;
            Vector3 behind = Quaternion.Euler(0f, def.playerStartYaw, 0f) * Vector3.back;
            def.worldLeaderboard = new WorldLeaderboardDef
            {
                enabled = true,
                name = "WorldLeaderboard",
                position = def.playerStart + behind * 8.3f + Vector3.up * 2.7f,
                yaw = Mathf.Repeat(def.playerStartYaw + 180f, 360f),
                size = new Vector2(10f, 5f),
                rowCount = Leaderboard.DisplayCount,
                backingMaterialKey = "Stone",
                glowMaterialKey = "NeonCyan",
            };
        }

        /// <summary>
        /// Turns the four existing gated courts into portal suns, spreads later course sections as rigid
        /// groups, and preserves the historical legendary/boss SpawnDefs. The builder moves each live
        /// arena spawner into its disconnected realm.
        /// </summary>
        public static void ApplySolarRealms(LevelDefinition def)
        {
            NormalizeSolarCourseSpacing(def);

            SetSolar(def, "T1_Gate", "SolarCyan", new Vector3(0f, 8.2f, 87.3f), 16f, 22f,
                new Vector3(700f, 0f, 0f), "Spawn_Legendary_Ninja",
                new Vector3(0f, 2.2f, 62f), new Vector3(10f, 5.2f, 151.375f), true);
            SetSolar(def, "T2_Gate", "SolarGold", new Vector3(0f, 24.55f, 216.8f), 17f, 23f,
                new Vector3(700f, 0f, 80f), "Spawn_Legendary_Knight",
                new Vector3(0f, 20.2f, 188f), new Vector3(2.75f, 21.7f, 268f), true);
            SetSolar(def, "T3_Gate", "SolarAzure", new Vector3(0f, 32.2f, 356.3f), 16f, 22f,
                new Vector3(700f, 0f, 160f), "Spawn_Legendary_Spellsword",
                new Vector3(-3f, 27.2f, 329f), new Vector3(0f, 28.2f, 393.15f), true);
            SetSolar(def, "Boss_Gate", "SolarGhost", new Vector3(0f, 22.3f, 486.3f), 25f, 31f,
                new Vector3(700f, 0f, 240f), "Spawn_Boss",
                new Vector3(0f, 16.2f, 450f), Vector3.zero, false);

            ApplySolarSpacing(def);
            TranslateCourseSections(def, 38f, 70f, 96f, false);
        }

        /// <summary>
        /// Replaces the four obsolete exterior courts with open transition gaps. The fights already stand
        /// on the generated twenty-metre realm floors; leaving the old rectangular floors and walls under
        /// the portal suns only made the previous level visible through their plasma. Route decks now stop
        /// outside each visible shell, and the next section begins beyond it after the realm return.
        /// </summary>
        static void ApplySolarSpacing(LevelDefinition def)
        {
            var platforms = new List<PlatformDef>(def.platforms ?? new PlatformDef[0]);
            platforms.RemoveAll(p => p != null && IsLegacySolarGeometry(p.name));

            SetPlatform(platforms, "T1_Causeway", new Vector3(0f, 1.5f, 51.6f), new Vector3(14f, 1f, 12.8f));
            SetPlatform(platforms, "T4_Entry", new Vector3(0f, 27.5f, 297.15f), new Vector3(10f, 1f, 3.7f));
            SetPlatform(platforms, "Boss_Approach", new Vector3(0f, 15.5f, 352.3f), new Vector3(10f, 1f, 11.4f));
            def.platforms = platforms.ToArray();

            var torches = new List<TorchDef>(def.torches ?? new TorchDef[0]);
            torches.RemoveAll(t => t != null && t.name != null &&
                (t.name.StartsWith("Torch_T1_Arena_") || t.name.StartsWith("Torch_T2_Arena_") ||
                 t.name.StartsWith("Torch_T3_Arena_") || t.name.StartsWith("Torch_Boss_") ||
                 t.name.StartsWith("Torch_T2_Entry_") || t.name.StartsWith("Torch_T3_Entry_")));
            foreach (var torch in torches)
            {
                if (torch == null) continue;
                if (torch.name == "Torch_T2_Mid") torch.basePosition = new Vector3(-8.5f, 12.5f, 104f);
                else if (torch.name == "Torch_T2_Buttress") torch.basePosition = new Vector3(22.5f, 6.5f, 124f);
                else if (torch.name == "Torch_T2_Top") torch.basePosition = new Vector3(8.5f, 20f, 146f);
                else if (torch.name == "Torch_T3_Span_S") torch.basePosition = new Vector3(-7.5f, 24.5f, 231f);
                else if (torch.name == "Torch_T3_Span_N") torch.basePosition = new Vector3(7.5f, 24.5f, 240f);
            }
            def.torches = torches.ToArray();

            // Route pickups remain authored at their historical height, but their XZ now sits on a real
            // walkable top. Realm pickups are deliberately untouched: the builder migrates those by name.
            foreach (var pickup in def.pickups ?? new PickupDef[0])
            {
                if (pickup == null) continue;
                // Wall Surge is retired. Preserve the existing pickup cadence while distributing its
                // replacements by role: Rebound on movement entries, Deflect Sigil before projectile spans.
                if (pickup.name == "Pickup_Surge_Spawn") pickup.itemKey = "Rebound";
                else if (pickup.name == "Pickup_T1_Surge") pickup.itemKey = "DeflectSigil";
                else if (pickup.name == "Pickup_T2_Surge") pickup.itemKey = "Rebound";
                else if (pickup.name == "Pickup_T3_Surge") pickup.itemKey = "DeflectSigil";
                else if (pickup.name == "Pickup_T3_Surge_2") pickup.itemKey = "Rebound";
                if (pickup.name == "Pickup_T2_Hook") pickup.position = new Vector3(14f, 6.2f, 113.375f);
                else if (pickup.name == "Pickup_T2_Surge") pickup.position = new Vector3(0f, 13.7f, 107f);
                else if (pickup.name == "Pickup_T3_Surge") pickup.position = new Vector3(1f, 22.7f, 198f);
                else if (pickup.name == "Pickup_T3_Hook") pickup.position = new Vector3(0f, 25.7f, 234f);
                else if (pickup.name == "Pickup_T1_Surge") pickup.position = new Vector3(-8f, 5.2f, 80f);
                else if (pickup.name == "Pickup_T2_Hook_2") pickup.position = new Vector3(-9f, 21.2f, 162f);
                else if (pickup.name == "Pickup_T3_Surge_2") pickup.position = new Vector3(8f, 29.2f, 261f);
                else if (pickup.name == "Pickup_Boss_Hook") pickup.position = new Vector3(-8f, 17.2f, 376f);
            }

            foreach (var arena in def.arenas ?? new ArenaDef[0])
            {
                if (arena == null) continue;
                if (arena.gateName == "T1_Gate") SetGateZ(arena, 63.25f, 145.625f, 78.3f);
                else if (arena.gateName == "T2_Gate") SetGateZ(arena, 191.75f, 264.75f, 206.8f);
                else if (arena.gateName == "T3_Gate") SetGateZ(arena, 332.25f, 390.75f, 345.3f);
                else if (arena.gateName == "Boss_Gate") SetGateZ(arena, 453.3f, 0f, 471.3f);
            }

            foreach (var checkpoint in def.checkpoints ?? new CheckpointDef[0])
            {
                if (checkpoint == null) continue;
                if (checkpoint.name == "Checkpoint_2")
                {
                    checkpoint.position = new Vector3(14f, 5f, 113.375f);
                    checkpoint.spawnOffset = new Vector3(0f, 0.2f, 0f);
                }
                else if (checkpoint.name == "Checkpoint_3")
                {
                    checkpoint.position = new Vector3(2.75f, 21.5f, 198f);
                    checkpoint.spawnOffset = new Vector3(0f, 0.2f, 0f);
                }
                else if (checkpoint.name == "Checkpoint_4")
                {
                    checkpoint.position = new Vector3(0f, 16f, 354f);
                    checkpoint.spawnOffset = new Vector3(0f, 0.2f, -3f);
                }
            }

            def.killZone.center = new Vector3(0f, -30f, 180f);
            def.killZone.size = new Vector3(240f, 2f, 760f);
        }

        static bool IsLegacySolarGeometry(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name == "T1_Arena" || name == "T1_Stone_5" || name == "T1_Obelisk_E" || name == "T2_Arena" ||
                   name == "T2_Bridge" || name == "T2_Bridge_Rail_L" || name == "T2_Bridge_Rail_R" ||
                   name == "T2_Entry" || name == "T3_Arena" || name == "T3_Entry" ||
                   name == "T3_Step_3" || name == "Boss_Arena" ||
                   name.StartsWith("Wall_T1_") || name.StartsWith("Wall_T2_") ||
                   name.StartsWith("Wall_T3_") || name.StartsWith("Wall_Boss_") ||
                   name.StartsWith("Pillar_Boss_");
        }

        static void SetPlatform(List<PlatformDef> platforms, string name, Vector3 center, Vector3 size)
        {
            var platform = platforms.Find(p => p != null && p.name == name);
            if (platform == null) throw new System.InvalidOperationException("Solar spacing requires " + name + ".");
            platform.center = center;
            platform.size = size;
        }

        static void SetGateZ(ArenaDef arena, float entryZ, float exitZ, float triggerZ)
        {
            arena.gateOpenPosition = new Vector3(arena.gateOpenPosition.x, arena.gateOpenPosition.y, entryZ);
            arena.gateClosedPosition = new Vector3(arena.gateClosedPosition.x, arena.gateClosedPosition.y, entryZ);
            arena.triggerPosition = new Vector3(arena.triggerPosition.x, arena.triggerPosition.y, triggerZ);
            if (!arena.hasExitGate) return;
            arena.exitGateOpenPosition = new Vector3(arena.exitGateOpenPosition.x, arena.exitGateOpenPosition.y, exitZ);
            arena.exitGateClosedPosition = new Vector3(arena.exitGateClosedPosition.x, arena.exitGateClosedPosition.y, exitZ);
        }

        /// <summary>
        /// Moves complete route sections rather than nudging whichever mesh happens to intersect a sun.
        /// T2, T3 and the T4/boss run use independent translations, preserving every within-section jump,
        /// ramp, wall-run, bolt line and pickup while opening real transition distance between spans.
        /// Historical legendary/boss SpawnDefs stay fixed because the builder
        /// relocates those live enemies into their remote realms and tests deliberately preserve the
        /// authored migration anchors.
        /// </summary>
        static void TranslateCourseSections(LevelDefinition def, float t2Delta, float t3Delta,
                                            float t4Delta, bool includeRealmMigrationAnchors)
        {
            Vector3 t2 = Vector3.forward * t2Delta;
            Vector3 t3 = Vector3.forward * t3Delta;
            Vector3 t4 = Vector3.forward * t4Delta;

            foreach (var p in def.platforms ?? new PlatformDef[0])
            {
                if (p == null || string.IsNullOrEmpty(p.name)) continue;
                if (p.name.StartsWith("T2_")) p.center += t2;
                else if (p.name.StartsWith("T3_")) p.center += t3;
                else if (p.name.StartsWith("T4_") || p.name.StartsWith("Boss_")) p.center += t4;
            }
            foreach (var r in def.ramps ?? new RampDef[0])
            {
                if (r == null || string.IsNullOrEmpty(r.name)) continue;
                if (r.name.StartsWith("T2_")) r.basePosition += t2;
                else if (r.name.StartsWith("T3_")) r.basePosition += t3;
                else if (r.name.StartsWith("T4_") || r.name.StartsWith("Boss_")) r.basePosition += t4;
            }
            foreach (var s in def.spawns ?? new SpawnDef[0])
            {
                if (s == null || string.IsNullOrEmpty(s.name)) continue;
                if (s.name.StartsWith("Spawn_T2_")) s.position += t2;
                else if (s.name.StartsWith("Spawn_T3_")) s.position += t3;
                else if (s.name.StartsWith("Spawn_T4_")) s.position += t4;
            }
            foreach (var p in def.pickups ?? new PickupDef[0])
            {
                if (p == null || string.IsNullOrEmpty(p.name)) continue;
                bool realmAnchor = p.name == "Pickup_T2_Hook_2" || p.name == "Pickup_T3_Surge_2" || p.name == "Pickup_Boss_Hook";
                if (realmAnchor && !includeRealmMigrationAnchors) continue;
                if (p.name.StartsWith("Pickup_T2_")) p.position += t2;
                else if (p.name.StartsWith("Pickup_T3_")) p.position += t3;
                else if (p.name.StartsWith("Pickup_Boss_")) p.position += t4;
            }
            foreach (var c in def.checkpoints ?? new CheckpointDef[0])
            {
                if (c == null) continue;
                if (c.name == "Checkpoint_2") c.position += t2;
                else if (c.name == "Checkpoint_3") c.position += t3;
                else if (c.name == "Checkpoint_4") c.position += t4;
            }
            foreach (var torch in def.torches ?? new TorchDef[0])
            {
                if (torch == null || string.IsNullOrEmpty(torch.name)) continue;
                if (torch.name.Contains("_T2_")) torch.basePosition += t2;
                else if (torch.name.Contains("_T3_")) torch.basePosition += t3;
                else if (torch.name.Contains("_T4_") || torch.name.Contains("_Boss_")) torch.basePosition += t4;
            }
            foreach (var balloon in def.balloons ?? new BalloonDef[0])
                if (balloon != null && balloon.name != null && balloon.name.StartsWith("T3_")) balloon.position += t3;
            foreach (var water in def.waters ?? new WaterDef[0])
                if (water != null && water.name != null && water.name.StartsWith("T3_")) water.center += t3;
        }

        static void NormalizeSolarCourseSpacing(LevelDefinition def)
        {
            float t2 = PlatformOffset(def, "T2_L1", 113.375f);
            float t3 = PlatformOffset(def, "T3_Pillar_1", 196f);
            float t4 = RampOffset(def, "T4_Ramp_Descent", 298.8f);
            if (Mathf.Abs(t2) > 0.001f || Mathf.Abs(t3) > 0.001f || Mathf.Abs(t4) > 0.001f)
                TranslateCourseSections(def, -t2, -t3, -t4, true);
        }

        static float PlatformOffset(LevelDefinition def, string name, float canonicalZ)
        {
            var platform = def.platforms != null ? System.Array.Find(def.platforms, p => p != null && p.name == name) : null;
            return platform != null ? platform.center.z - canonicalZ : 0f;
        }

        static float RampOffset(LevelDefinition def, string name, float canonicalZ)
        {
            var ramp = def.ramps != null ? System.Array.Find(def.ramps, r => r != null && r.name == name) : null;
            return ramp != null ? ramp.basePosition.z - canonicalZ : 0f;
        }

        static void SetSolar(LevelDefinition def, string gateName, string theme, Vector3 exterior, float radius,
                             float visualRadius,
                             Vector3 realmCenter, string enemySpawner, Vector3 retry, Vector3 worldReturn,
                             bool hasReturn)
        {
            var arena = def.arenas != null ? System.Array.Find(def.arenas, a => a != null && a.gateName == gateName) : null;
            if (arena == null) throw new System.InvalidOperationException("Solar realm requires arena " + gateName + ".");
            var r = arena.solarRealm ?? new SolarRealmDef();
            r.enabled = true;
            r.themeMaterialKey = theme;
            r.exteriorCenter = exterior;
            r.exteriorRadius = radius;
            r.visualRadius = visualRadius;
            r.realmCenter = realmCenter;
            r.realmFloorRadius = 20f;
            r.realmShellRadius = 30f;
            r.playerEntryPosition = realmCenter + new Vector3(0f, 1.2f, -13f);
            r.playerEntryYaw = 0f;
            r.retryPosition = retry;
            r.retryYaw = 0f;
            r.enemySpawnerName = enemySpawner;
            r.enemySpawnPosition = realmCenter + new Vector3(0f, 0.1f, 4f);
            r.enemySpawnYaw = 180f;
            if (gateName == "T1_Gate") { r.arenaPickupName = "Pickup_T1_Surge"; r.arenaPickupPosition = realmCenter + new Vector3(-7f, 1.2f, -2f); }
            else if (gateName == "T2_Gate") { r.arenaPickupName = "Pickup_T2_Hook_2"; r.arenaPickupPosition = realmCenter + new Vector3(-7f, 1.2f, -2f); }
            else if (gateName == "T3_Gate") { r.arenaPickupName = "Pickup_T3_Surge_2"; r.arenaPickupPosition = realmCenter + new Vector3(7f, 1.2f, -2f); }
            else { r.arenaPickupName = "Pickup_Boss_Hook"; r.arenaPickupPosition = realmCenter + new Vector3(-7f, 1.2f, -5f); }
            r.hasReturn = hasReturn;
            r.realmExitPosition = realmCenter + new Vector3(0f, 1.5f, -17.5f);
            r.returnPosition = worldReturn;
            r.returnYaw = 0f;
            arena.solarRealm = r;
        }
    }
}
