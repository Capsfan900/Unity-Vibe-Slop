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
            new Reshape("T2_L1", new Vector3(7f, 4.5f, 115.5f), new Vector3(4.8f, 1f, 8f),
                        "the spiral's first pad, and the landing for the level's WORST hop: T2_Entry -> T2_L1 was a 4.54 m " +
                        "diagonal off an 8 m deck with 6 clean launch points out of 25. Grown 5 -> 8 m deep, in BOTH " +
                        "directions on purpose - south (z 113.5 -> 111.5) shortens the entry gap to 2.57 m and takes the hop " +
                        "to 13/25; north (118.5 -> 119.5) pays back the take-off room that growing south costs the NEXT hop, " +
                        "so T2_L1 -> T2_L2 holds at 20/25 instead of falling to 15. x is untouched: max.x 9.4 keeps the " +
                        "1.1 m standoff LevelSpan2Tests pins against T2_Wall_East's run line."),
            new Reshape("T2_L2", new Vector3(7.25f, 6f, 124f), new Vector3(4.5f, 1f, 5f), "east only: min.x 5.0 is the buttress's face"),
            new Reshape("T2_L3", new Vector3(0f, 7.5f, 131f), new Vector3(7f, 1f, 5f),
                        "TURN BALCONY: 7 x 5, the north turn of the first lap. T2_L2 -> T2_L3 11 -> 16 clean points, " +
                        "T2_L3 -> T2_L4 13 -> 14."),
            new Reshape("T2_L4", new Vector3(-7f, 9f, 124f), new Vector3(4.8f, 1f, 5f), "the west wall's mount ledge; 1.1 m of standoff left"),
            new Reshape("T2_L5", new Vector3(-7f, 10.5f, 116f), new Vector3(4.8f, 1f, 5f), "clear of T2_Perch_W in plan (z 109-112)"),
            new Reshape("T2_L6", new Vector3(0f, 12f, 111f), new Vector3(5f, 1f, 7f),
                        "TURN BALCONY, the south turn, over the west perch - grown in Z ONLY. Measured: at 7 m WIDE it " +
                        "collapses both its gaps to 1.10 m, which is a step and not a hop; at 7 m DEEP it holds both gaps " +
                        "at 2.10 m and still takes T2_L5 -> T2_L6 21 -> 23 and T2_L6 -> T2_L7 21 -> 23. A deck grows toward " +
                        "the arc that LANDS on it, not toward the one that leaves it."),
            new Reshape("T2_L7", new Vector3(7f, 13.5f, 116f), new Vector3(4.8f, 1f, 5f), "the second lap"),
            new Reshape("T2_L8", new Vector3(7.25f, 15f, 124f), new Vector3(4.5f, 1f, 5f), "east only, and the buttress chimney's exit ledge — a bigger target for the climb"),
            // L9 is the north turn of the SECOND lap and stands directly over L3, the north turn of the
            // first. It was the one pad in the spiral left at 4 x 4 while the deck it answers grew to
            // 5 x 5, and the cost was measured: L8 -> L9 is the spiral's hardest hop (it must clear the
            // tower's north-east corner) and growing L8 alone made it WORSE — 10 clean take-off points
            // out of 25 in the shipped asset, 8 with L8 grown, 13 with L9 matched to L3. Matching it also
            // makes the two laps read as the same turn seen twice, which is what a spiral is for.
            new Reshape("T2_L9", new Vector3(0f, 16.5f, 131f), new Vector3(7f, 1f, 5f),
                        "TURN BALCONY, matched to T2_L3 directly below it - the two laps must read as the same turn seen " +
                        "twice, which is what a spiral is for. T2_L8 -> T2_L9 (the spiral's hardest hop) 13 -> 17 clean " +
                        "points, T2_L9 -> T2_L10 24 -> 25."),

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
            new Reshape("T2_Tower", new Vector3(0f, 12f, 124f), new Vector3(4f, 20f, 5f),
                        "the spiral's core was the spiral's blindfold; a 5 m wall in a 19 m helix -> 4 m"),

            // ---- T3: the first pillar is the landing, not the test.
            // T3_Entry -> T3_Pillar_1 was 10 clean launch points out of 25 onto a 2.5 m square 20 m up. Pillars 2
            // and 3 stay at 2.5 m ON PURPOSE - the pillar run is the level's precision beat and the only place it
            // asks for a placed foot - but a beat has to be ENTERED, and entering it should not be the hardest jump
            // in it. 3.5 m takes the entry to 15/25 and leaves P2 -> P3, the 4.72 m signature leap, untouched.
            new Reshape("T3_Pillar_1", new Vector3(0f, 15.5f, 198f), new Vector3(3.5f, 12f, 3.5f),
                        "the entry landing of the pillar run; 2 and 3 keep their 2.5 m because that is their job"),

            // ---- T3: the span was the causeway's problem, unfixed, one span later.
            // 4 m wide over 22 m (5.5:1) carrying two water sheets you SKATE, a slide gate and the turn onto the
            // steps. Widened to 4.8 m, which is every millimetre available: T3_Wall_Landing_S.min.x is 2.5 and
            // LevelSpan3Tests forbids the two touching, and T3_Fallen_Lintel spans x +/-2.5 and must CONTAIN the
            // deck, so +/-2.4 leaves 0.1 m of margin at both pins. Neutral on every hop and every sightline: this
            // one is bought purely in room to steer.
            new Reshape("T3_Span", new Vector3(0f, 24f, 226f), new Vector3(4.8f, 1f, 22f),
                        "the last long deck still at 4 m; +20% room to skate, out of the only 0.8 m going spare"),

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
            new TorchDef { name = "Torch_Beacon_T2_L3", basePosition = new Vector3(-3.2f, 8f, 132.8f) },
            new TorchDef { name = "Torch_Beacon_T2_L4", basePosition = new Vector3(-8.8f, 9.5f, 122.2f) },
            new TorchDef { name = "Torch_Beacon_T2_L5", basePosition = new Vector3(-8.8f, 11f, 114.2f) },
            new TorchDef { name = "Torch_Beacon_T2_L7", basePosition = new Vector3(8.8f, 14f, 117.6f) },
            new TorchDef { name = "Torch_Beacon_T2_L8", basePosition = new Vector3(8.9f, 15.5f, 125.6f) },
            new TorchDef { name = "Torch_Beacon_T2_L9", basePosition = new Vector3(-3.2f, 17f, 132.5f) },
            new TorchDef { name = "Torch_Beacon_T2_L10", basePosition = new Vector3(6.7f, 18.5f, 137.5f) },
            // T3: on the pillars, at the OUTER corner of the top face — a 2.5 m square you land on at
            // speed gets nothing in the middle of it — every 8 m rather than every pillar.
            new TorchDef { name = "Torch_Beacon_T3_Pillar_1", basePosition = new Vector3(-1f, 21.5f, 197.2f) },
            new TorchDef { name = "Torch_Beacon_T3_Pillar_3", basePosition = new Vector3(-4f, 23.5f, 206.9f) },
            new TorchDef { name = "Torch_Beacon_T3_Pillar_4", basePosition = new Vector3(-1.2f, 24.5f, 214f) },

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
            new TorchDef { name = "Torch_Beacon_Alt_T2_East", basePosition = new Vector3(8.6f, 8f, 134f) },
            new TorchDef { name = "Torch_Beacon_Alt_T2_West", basePosition = new Vector3(-9.1f, 12.5f, 104f) },
            new TorchDef { name = "Torch_Beacon_Alt_T3_S", basePosition = new Vector3(8f, 23f, 207.5f) },
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

        public static readonly Ramp[] Ramps =
        {
            // ---- T1: the stepping stones stop being five identical +0.5 m squares.
            // Ground_Start -> Stone_1 -> Stone_2 -> Stone_3 -> Stone_4 -> Causeway was the flattest,
            // most mechanical passage in the level: four hops of exactly +0.5 m, none of them a decision.
            // Three of the four become grades; the FOURTH is deliberately left as a jump (see the
            // rejection note under Stone_2 -> Stone_3 below), because it is the only one with a choice
            // underneath it.
            //
            // Stone_1 (top 0.0, z 11.5-16.5) -> Stone_2 (top 0.5, z 20-24) overlap in x 1.5-2.5, so the
            // ramp runs straight up that band: base 0.2 m inside Stone_1's lip, top 0.2 m onto Stone_2.
            // 0.5 m over 3.5 m of gap is 7.3 deg and there is no way to make it steeper - a ramp that
            // stopped short would be a jump with a shorter run-up, not a launch (see above).
            new Ramp("T1_Ramp_Stone12", new Vector3(2.0f, 0.0f, 16.3f), 3.0f, 3.90f, 0.5f, 0f,
                     "the opening's second hop becomes a run: you leave the start pad on a jump (the verb the " +
                     "level teaches first) and then do not touch the air again until the one hop that matters"),
            // Stone_3 (top 1.0) -> Stone_4 (top 1.5). Yawed 18 deg so the ramp leans back east with the
            // zigzag and lands on Stone_4's west half, setting up the causeway ramp at x -0.5. The yaw is
            // also what keeps it off T1_Fast_1 (x -1.25..2.75, z 25-32, top 0.5): at the base end the ramp
            // sits at x -4.4..-1.6, clear by 0.35 m, and it only crosses x -1.25 north of z 32 where the
            // water deck has ended. Measured: no solid overlap with any box in the level.
            new Ramp("T1_Ramp_Stone34", new Vector3(-3.0f, 1.0f, 31.9f), 3.0f, 3.80f, 0.5f, 18f,
                     "the first half of a two-stage climb into the causeway, and the shape that carries the " +
                     "zigzag's last turn on the ground instead of in the air"),
            // Stone_4 -> the causeway, and the best of the three. The two decks share x -2.5..1.5 exactly,
            // so a 3.8 m ramp centred on x -0.5 is FULLY SUPPORTED at both ends with no overhang anywhere -
            // the only ramp in the level that is - and it clears T1_Rail_R (min.x 1.5) by 0.1 m. 2.4 m of
            // run for 0.5 m makes it the steepest of the T1 set at 11.8 deg.
            // ITS JOB, and the reason it is worth more than the two above: T1_Fallen_Obelisk is a slide
            // gate 5.8 m past the causeway's south edge (1.30 m of clearance - a slide fits, standing does
            // not). Arriving by hop you land, stand, and then have to buy a slide inside 5.8 m. Arriving by
            // ramp you are already grounded and already moving, so the gate is entered out of a run.
            new Ramp("T1_Ramp_Causeway", new Vector3(-0.5f, 1.5f, 40.8f), 3.8f, 2.40f, 0.5f, 0f,
                     "the causeway's slide gate stops being something you land in front of and becomes " +
                     "something you run into"),

            // ---- T2: one grade per lap of the spiral, both at the north turn.
            // Eleven pads, every rise exactly +1.5 m, was the other half of "eleven identical squares" -
            // the first half (the pads' footprints) was fixed by the openness pass. These two are the
            // spiral's two WEAKEST hops, 16/25 and 17/25 clean launch points, and they are the same corner
            // of the helix one lap apart: L3 and L9 are the two 7 m turn balconies, L9 directly over L3.
            // A turn is where a runner loses the most speed anyway; doing it on a grade with both feet on
            // the ground beats doing it as a diagonal hop onto a pad.
            //
            // L2 (top 6.5, x 5-9.5) -> L3 (top 8.0, x -3.5..3.5). Yaw 329 leans north-west with the turn.
            // Base at z 125.6 rather than further south because T2_Buttress (x 4.2-5.0, z 121-124, rising
            // from y 6.5) is the chimney's face and the ramp's south-west corner would clip it at z < 124.6.
            // Lands at x 3.0 - the EAST end of the balcony - deliberately: the exit is L3 -> L4 to the
            // south-west, and landing east leaves the whole 7 m of balcony to arc through.
            new Ramp("T2_Ramp_L2_L3", new Vector3(6.0f, 6.5f, 125.6f), 4.0f, 5.83f, 1.5f, 329f,
                     "lap one's north turn is run, not jumped; 14.4 deg, and the only thing in the spiral " +
                     "that is not a square"),
            // L8 -> L9, the same turn one lap up, and the hardest piece of geometry in this pass.
            // MEASURED REJECTIONS, both of them: the same 4 m / 5.83 m / yaw 329 shape as the ramp above,
            // translated +9 m in y, BLOCKS T2_Perch_E's bolt onto T2_L9 - the muzzle at (13.8, 12.5, 129)
            // and L9's chest at (0, 18.2, 131) put the bolt inside the slab, and a perch that loses half
            // its coverage is a perch that is no longer a route piece (rule: a ramp is solid geometry and
            // occludes exactly like a slab). Swinging the ramp WEST to duck under the bolt instead drives
            // it into T2_Tower (x -2..2, 20 m tall) and costs three sightlines.
            // The gap the bolt leaves is a NARROW ramp that lands EARLY: the bolt is still out at x > 10
            // for z < 129.9, so a 3 m wide ramp landing on L9 at z 129.3 passes under it entirely. Yaw 313
            // and run 5.45 fit that window; the price is 1 m of width and a slightly steeper 15.4 deg,
            // which is why the two laps' grades are near-twins rather than twins.
            new Ramp("T2_Ramp_L8_L9", new Vector3(6.5f, 15.5f, 125.6f), 3.0f, 5.45f, 1.5f, 313f,
                     "lap two's north turn, threaded between the tower and the east perch's bolt line: " +
                     "3 m wide and landing early is the only shape that clears both"),
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
            foreach (var b in RouteBeacons)
                torches.Add(new TorchDef { name = b.name, basePosition = b.basePosition });
            def.torches = torches.ToArray();

            ApplyDescent(def);
            ApplyOpeningDescent(def);
            ApplySolarRealms(def);

            return string.Format("Level_01 reworked: {0} boxes reshaped for openness, {1} perches, {2} spawns moved onto them, " +
                                 "{3} balloons (T3 arc), {4} water sheets, {5} ramps, {6} route beacons, {7} arena doors widened; " +
                                 "{8} platforms and {9} torches total.",
                                 reshaped, Perches.Length, moved, balloons.Count, waters.Count, def.ramps.Length,
                                 RouteBeacons.Length, gated, def.platforms.Length, def.torches.Length);
        }

        /// <summary>The final descent and its existing surge-turret encounter. Absolute, re-runnable data.</summary>
        public static void ApplyDescent(LevelDefinition def)
        {
            var platforms = new List<PlatformDef>(def.platforms);
            var boss = platforms.Find(p => p.name == "Boss_Arena");
            if (boss == null) throw new System.InvalidOperationException("Descent requires Boss_Arena.");
            // Derive the translation from the current anchor: a second application moves nothing.
            Vector3 shift = new Vector3(0f, 15.5f, 390f) - boss.center;
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
            for (int i = 0; i < 3; i++)
            {
                // Live interception needed another 8 m of lead for the opening shot. Keep the row's
                // 18 m rhythm, with its last beat on the run-out rather than beside a fleeing player.
                float z = 324f + 18f * i;
                float y = descent.basePosition.y + descent.rise * Mathf.Clamp01((z - descent.basePosition.z) / descent.run);
                platforms.Add(new PlatformDef { name = "T4_TurretPad_" + (i + 1),
                    center = new Vector3(6.7f, y - 0.5f, z), size = new Vector3(3f, 1f, 3f),
                    materialKey = "Stone", trim = true, trimMaterialKey = "NeonPink" });
                spawns.Add(new SpawnDef { name = "Spawn_T4_Surge_" + (i + 1), prefabKey = "pshooter_enemy03",
                    position = new Vector3(6.7f, y + 0.1f, z), yaw = 210f });
            }
            def.spawns = spawns.ToArray();
            def.platforms = platforms.ToArray();
            def.killZone.center = new Vector3(0f, -30f, 200f);
            def.killZone.size = new Vector3(200f, 2f, 500f);
        }

        /// <summary>A separate downhill opening before Ground_Start; the complete original route follows it.</summary>
        public static void ApplyOpeningDescent(LevelDefinition def)
        {
            // A broad crest gives room to orient before committing. The 1:4 grade now runs for 120 m,
            // long enough for all five parry beats to live on the descent instead of crowding Ground_Start.
            // Both slope ends overlap their decks by 0.2 m; the run-out still meets Ground_Start at y 0.
            var platforms = new List<PlatformDef>(def.platforms);
            platforms.RemoveAll(p => p.name == "T0_Entry" || p.name == "T0_RunOut");
            platforms.Add(new PlatformDef { name = "T0_Entry", center = new Vector3(0f, 29.5f, -139.8f),
                size = new Vector3(14f, 1f, 8.4f), materialKey = "Platform", trim = true,
                trimMaterialKey = "NeonCyan" });
            platforms.Add(new PlatformDef { name = "T0_RunOut", center = new Vector3(0f, -0.5f, -11.9f),
                size = new Vector3(14f, 1f, 8.2f), materialKey = "Platform", trim = true,
                trimMaterialKey = "NeonCyan" });
            def.platforms = platforms.ToArray();

            var ramps = new List<RampDef>(def.ramps);
            ramps.RemoveAll(r => r.name == "T0_Ramp_Descent");
            ramps.Add(new RampDef { name = "T0_Ramp_Descent", basePosition = new Vector3(0f, 30f, -135.8f),
                width = 14f, run = 120f, rise = -30f, thickness = RampThickness, materialKey = "Stone" });
            def.ramps = ramps.ToArray();

            // Five existing surge turrets punctuate the whole hill: LEFT, RIGHT, LEFT, then two overhead.
            // The lower pads alternate beyond the 14 m slide lane. The last pair share one connected open
            // Z-shaped dais whose offset decks leave both downward shot lines clear.
            var spawns = new List<SpawnDef>(def.spawns);
            spawns.RemoveAll(s => s.name.StartsWith("Spawn_T0_Surge_"));
            platforms.RemoveAll(p => p.name.StartsWith("T0_TurretPad_") ||
                                     p.name.StartsWith("T0_OverheadDais_"));
            var lowerShots = new[]
            {
                new Vector3(-8.7f, 24.6f, -113.8f),
                new Vector3( 8.7f, 18.35f, -88.8f),
                new Vector3(-8.7f, 12.35f, -64.8f),
            };
            for (int i = 0; i < lowerShots.Length; i++)
            {
                var shot = lowerShots[i];
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
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_Front", center = new Vector3(3.2f, 13f, -38.8f),
                size = new Vector3(5f, 1f, 5f), materialKey = "Stone", trim = true,
                trimMaterialKey = "NeonCyan"
            });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_Rear", center = new Vector3(-3.2f, 8.5f, -21.8f),
                size = new Vector3(5f, 1f, 4f), materialKey = "Stone", trim = true,
                trimMaterialKey = "NeonCyan"
            });
            // The rear terrace follows the falling player: too high a muzzle makes capped homing
            // overfly the runner. Three overlapping descending beams connect both floating crowns.
            for (int step = 0; step < 3; step++)
                platforms.Add(new PlatformDef
                {
                    name = "T0_OverheadDais_Spine_" + step,
                    center = new Vector3(4.8f, 12.5f - step * 1.35f, -34.45f + step * 5.2f),
                    size = new Vector3(1.8f, 1.7f, 4.9f), materialKey = "Stone"
                });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_Return", center = new Vector3(1.6f, 8.5f, -20.8f),
                size = new Vector3(7.8f, 0.8f, 1.8f), materialKey = "Stone"
            });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_FrontUnder", center = new Vector3(3.2f, 12.85f, -38.8f),
                size = new Vector3(3.8f, 0.3f, 3.8f), materialKey = "Stone"
            });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_FrontKeel", center = new Vector3(3.2f, 12.4f, -38.8f),
                size = new Vector3(2.2f, 0.6f, 2.2f), materialKey = "Stone"
            });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_RearUnder", center = new Vector3(-3.2f, 8.55f, -21.8f),
                size = new Vector3(3.8f, 0.3f, 3.8f), materialKey = "Stone"
            });
            platforms.Add(new PlatformDef
            {
                name = "T0_OverheadDais_RearKeel", center = new Vector3(-3.2f, 8.3f, -21.8f),
                size = new Vector3(2.2f, 0.2f, 2.2f), materialKey = "Stone"
            });

            var overheadShots = new[]
            {
                new Vector3( 3.2f, 13.6f, -39.8f),
                new Vector3(-3.2f, 9.1f, -22.8f),
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
                    "Spawn_T0_Surge_4", "Spawn_T0_Surge_5"
                },
                // 0.08 s is the shipped successful-parry recovery; another 0.03 s gives input slack before
                // the next launch. A closing runner can contact sooner than the nominal 0.44 s flight, so
                // the live opening probe owns the actual contact-spacing proof.
                recoveryGap = 0.11f,
                readinessTimeout = 1.1f,
                shotResolutionTimeout = 1.25f,
                progressOrigin = new Vector3(0f, 0f, -135.8f),
                progressDirection = Vector3.forward,
                memberProgressGates = new[] { 0f, 16f, 40f, 64f, 87f }
            });
            def.projectileSequences = sequences.ToArray();

            def.playerStart = new Vector3(0f, 30.3f, -139f);
            def.playerStartYaw = 0f;
            foreach (var pedestal in def.pedestals)
                if (pedestal.name == "WandPedestal_Start")
                    pedestal.groundPosition = new Vector3(3f, 30f, -139f);

            // Catch falls behind the new crest while retaining the final arena's z 450 boundary.
            def.killZone.center = new Vector3(0f, -30f, 145f);
            def.killZone.size = new Vector3(200f, 2f, 610f);
        }

        /// <summary>
        /// Turns the four existing gated courts into portal suns without moving their route anchors or
        /// their authored SpawnDefs. The builder moves each live spawner into its disconnected realm.
        /// </summary>
        public static void ApplySolarRealms(LevelDefinition def)
        {
            SetSolar(def, "T1_Gate", "SolarCyan", new Vector3(0f, 8.2f, 87f), 12f, 22f,
                new Vector3(700f, 0f, 0f), "Spawn_Legendary_Ninja",
                new Vector3(0f, 3.2f, 70f), new Vector3(0f, 4.2f, 102f), true);
            SetSolar(def, "T2_Gate", "SolarGold", new Vector3(0f, 24.55f, 170f), 13f, 23f,
                new Vector3(700f, 0f, 80f), "Spawn_Legendary_Knight",
                new Vector3(0f, 20.2f, 150f), new Vector3(0f, 20.2f, 186f), true);
            SetSolar(def, "T3_Gate", "SolarAzure", new Vector3(0f, 32.2f, 270f), 12f, 22f,
                new Vector3(700f, 0f, 160f), "Spawn_Legendary_Spellsword",
                new Vector3(0f, 28.2f, 253f), new Vector3(0f, 28.2f, 286f), true);
            SetSolar(def, "Boss_Gate", "SolarGhost", new Vector3(0f, 22.3f, 390f), 18f, 31f,
                new Vector3(700f, 0f, 240f), "Spawn_Boss",
                new Vector3(0f, 16.2f, 361f), Vector3.zero, false);
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
