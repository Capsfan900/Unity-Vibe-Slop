using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>Every authored traversal in Level_01, flown.</b>
    ///
    /// <para><c>FeatureTests > LevelStructure</c> checks that the GAP between two platforms is inside the
    /// reachability envelope. It cannot see an obstruction in the middle of the arc, and that blind spot is
    /// exactly why the wall-jump buttress designed for The Ascent sat unbuilt in BACKLOG §5b for want of a
    /// play test. These tests close it: they simulate the player's real ballistic arc, swept as the real
    /// capsule, against every box in the level definition.</para>
    ///
    /// <para>They are EDIT-mode and pure — no scene, no play mode — so they run headless while the editor
    /// is busy, which is the only reason they get run at all. The movement constants come off the shipped
    /// <c>Player.prefab</c>, so retuning the motor retunes these tests rather than invalidating them.</para>
    ///
    /// <para><b>What passing means.</b> That a clean line exists and the geometry does not lie. It does not
    /// mean the jump feels good, is readable, or is fair; no test can say that, and no human has yet played
    /// the routes added here.</para>
    /// </summary>
    public class LevelArcClearanceTests
    {
        static LevelDefinition def;
        static List<A.Box> boxes;
        static A.MoveProfile profile;
        static float floorY;

        [OneTimeSetUp]
        public void Load()
        {
            def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelArcReport.DefaultLevel);
            Assert.IsNotNull(def, "Level_01_Level.asset is missing");
            string err;
            Assert.IsTrue(A.TryLoadProfile(out profile, out err), err);
            boxes = A.BoxesFrom(def);
            floorY = def.killZone.center.y;
        }

        // ------------------------------------------------------------------ the constants are real

        [Test]
        public void MoveProfile_ComesFromThePlayerPrefabAndIsSane()
        {
            Assert.Less(profile.gravity, 0f, "gravity must pull down");
            Assert.Greater(profile.jumpHeight, 0f);
            Assert.Greater(profile.groundSpeed, 0f);
            Assert.Greater(profile.capsuleRadius, 0f);
            Assert.Greater(profile.standHeight, profile.slideHeight,
                "a slide that is not shorter than standing cannot pass under anything");
            Assert.AreEqual(Mathf.Sqrt(2f * -profile.gravity * profile.jumpHeight), profile.JumpTakeoffSpeed, 0.001f);
        }

        // ------------------------------------------------------------------ the baseline route

        static IEnumerable<TestCaseData> BaseRouteCases()
        {
            foreach (var r in LevelArcReport.BaseRoute)
                yield return new TestCaseData(r.from, r.to).SetName("Base_" + r.from + "_to_" + r.to);
        }

        /// <summary>
        /// The whole course, hop by hop, with the stick RELEASED — no air control assumed, which is the
        /// conservative reading. Any new geometry that stands in one of these arcs fails here instead of
        /// in someone's run.
        /// </summary>
        [Test, TestCaseSource(nameof(BaseRouteCases))]
        public void BaselineHopHasACleanArc(string from, string to)
        {
            var v = A.AnalyzeHop(boxes, from, to, profile, profile.groundSpeed, floorY);
            Assert.IsTrue(v.exists, v.Summary());
            Assert.GreaterOrEqual(v.cleanLaunchPoints, 3,
                "a hop that works from fewer than three of the sampled take-off spots is a pixel-perfect " +
                "jump, not a route: " + v.Summary());
        }

        // ------------------------------------------------------------------ the tech lines

        static readonly A.AirControl[] WithBraking = { A.AirControl.None, A.AirControl.Brake };

        [Test]
        public void SlideJump_Stone1ToFast1_IsReachableWithTheTech()
        {
            var v = A.AnalyzeHop(boxes, "T1_Stone_1", "T1_Fast_1", profile, profile.SlideJumpSpeed, floorY, WithBraking);
            Assert.IsTrue(v.exists, v.Summary());
        }

        [Test]
        public void SlideJump_Stone1ToFast1_IsOutOfReachOfTheBaseKit()
        {
            var v = A.AnalyzeHop(boxes, "T1_Stone_1", "T1_Fast_1", profile, profile.groundSpeed, floorY);
            Assert.IsFalse(v.exists,
                "the fast line must EXCEED the base envelope or the tech buys nothing: " + v.Summary());
        }

        [Test]
        public void SlideJump_Fast1RejoinsTheCourse()
        {
            var v = A.AnalyzeHop(boxes, "T1_Fast_1", "T1_Stone_4", profile, profile.groundSpeed, floorY);
            Assert.IsTrue(v.exists, "the exit from a fast line is NOT a second gate: " + v.Summary());
        }

        // ------------------------------------------------------------------ slide gates

        [Test]
        public void T1FallenObelisk_IsASlideGateAndNotAWall()
        {
            var v = A.CheckLintel(boxes, "T1_Fallen_Obelisk", "T1_Causeway", profile);
            Assert.IsTrue(v.exists, "T1_Fallen_Obelisk missing");
            Assert.IsTrue(v.slideFits, v.Summary("T1_Fallen_Obelisk", "T1_Causeway"));
            Assert.IsTrue(v.standingBlocked, v.Summary("T1_Fallen_Obelisk", "T1_Causeway"));
            Assert.IsTrue(v.jumpable, "it must stay jumpable, or it costs ACCESS rather than time: " +
                                      v.Summary("T1_Fallen_Obelisk", "T1_Causeway"));
            Assert.IsTrue(v.spansTheDeck, v.Summary("T1_Fallen_Obelisk", "T1_Causeway"));
        }

        [Test]
        public void T3FallenLintel_IsASlideGateAndNotAWall()
        {
            var v = A.CheckLintel(boxes, "T3_Fallen_Lintel", "T3_Span", profile);
            Assert.IsTrue(v.exists, "T3_Fallen_Lintel missing");
            Assert.IsTrue(v.slideFits, v.Summary("T3_Fallen_Lintel", "T3_Span"));
            Assert.IsTrue(v.standingBlocked, v.Summary("T3_Fallen_Lintel", "T3_Span"));
            Assert.IsTrue(v.jumpable, v.Summary("T3_Fallen_Lintel", "T3_Span"));
            Assert.IsTrue(v.spansTheDeck, v.Summary("T3_Fallen_Lintel", "T3_Span"));
        }

        [Test]
        public void T3FallenLintel_DoesNotSitOnThePickupOrObstructTheSpanRun()
        {
            var v = A.AnalyzeHop(boxes, "T3_Pillar_4", "T3_Span", profile, profile.groundSpeed, floorY);
            Assert.IsTrue(v.exists, "the lintel must not block the arrival on the span: " + v.Summary());

            int il = A.IndexOf(boxes, "T3_Fallen_Lintel");
            Assert.GreaterOrEqual(il, 0);
            var l = boxes[il];
            foreach (var pk in def.pickups)
            {
                bool inside = pk.position.x > l.min.x - 0.4f && pk.position.x < l.max.x + 0.4f &&
                              pk.position.y > l.min.y - 0.4f && pk.position.y < l.max.y + 0.4f &&
                              pk.position.z > l.min.z - 0.4f && pk.position.z < l.max.z + 0.4f;
                Assert.IsFalse(inside, pk.name + " is inside T3_Fallen_Lintel");
            }
        }

        // ------------------------------------------------------------------ the buttress on The Ascent

        [Test]
        public void Buttress_Exists()
        {
            Assert.GreaterOrEqual(A.IndexOf(boxes, "T2_Buttress"), 0,
                "The Ascent is a 20 m tower and the obvious home for wall jumping; T2_Buttress is its line.");
        }

        [Test]
        public void Buttress_FormsAClimbableChimneyWithTheTower()
        {
            var g = A.MeasureChimney(boxes, "T2_Tower", "T2_Buttress", profile);
            Assert.IsTrue(g.valid, "chimney geometry: " + g.reason);
            Assert.GreaterOrEqual(g.width, 1.5f);
            Assert.LessOrEqual(g.width, 3f);
            Assert.Less(g.shortTop, g.tallTop,
                "the two faces must be DIFFERENT heights so the climb has an exit — you leave over the shorter one");
        }

        /// <summary>
        /// THE TEST THAT UNBLOCKED §5b. The fin stands beside the take-off for <c>T2_L2 → T2_L3</c>, and
        /// the reason it was never built is that nothing could say whether it obstructed that arc. It can
        /// now, and this is the assertion that keeps it true.
        /// </summary>
        [Test]
        public void Buttress_DoesNotObstructTheL2ToL3Hop()
        {
            var v = A.AnalyzeHop(boxes, "T2_L2", "T2_L3", profile, profile.groundSpeed, floorY);
            Assert.IsTrue(v.exists, v.Summary());
            Assert.GreaterOrEqual(v.cleanLaunchPoints, 8,
                "the buttress may narrow the take-off but it must not squeeze it to a sliver: " + v.Summary());
        }

        [Test]
        public void Buttress_ClimbsToTheExitLedge()
        {
            var o = BestButtressClimb();
            Assert.IsTrue(o.success,
                "no sequence of wall pushes tops out on T2_L8: best attempt reached y " +
                o.peakY.ToString("0.00") + " and ended on " + o.landedOn);
            Assert.LessOrEqual(o.pushes, profile.maxWallJumps,
                "the climb must fit inside one airtime's worth of wall jumps");
        }

        /// <summary>The shortcut has to be a shortcut: the base kit must not be able to make the same jump.</summary>
        [Test]
        public void Buttress_ShortcutIsOutOfReachOfTheBaseKit()
        {
            var v = A.AnalyzeHop(boxes, "T2_L2", "T2_L8", profile, profile.groundSpeed, floorY, WithBraking);
            Assert.IsFalse(v.exists, "T2_L2 -> T2_L8 must need the wall jump: " + v.Summary());
        }

        /// <summary>
        /// And it has to rejoin the course BEFORE the arena, or the gate is bypassed and the run soft-locks.
        /// T2_L8 is mid-spiral; the remaining ledges and the bridge still lead into the trigger.
        /// </summary>
        [Test]
        public void Buttress_RejoinsTheCourseAheadOfTheArena()
        {
            var onward = A.AnalyzeHop(boxes, "T2_L8", "T2_L9", profile, profile.groundSpeed, floorY);
            Assert.IsTrue(onward.exists, "the exit ledge must continue the course: " + onward.Summary());

            int i8 = A.IndexOf(boxes, "T2_L8"), ib = A.IndexOf(boxes, "T2_Bridge");
            Assert.GreaterOrEqual(i8, 0);
            Assert.GreaterOrEqual(ib, 0);
            Assert.Less(boxes[i8].max.z + 0.01f, boxes[ib].max.z,
                "T2_L8 must still be short of the bridge that leads into the arena trigger");
        }

        [Test]
        public void Buttress_DoesNotStandOnTheL2Deck()
        {
            int ifin = A.IndexOf(boxes, "T2_Buttress"), i2 = A.IndexOf(boxes, "T2_L2");
            Assert.GreaterOrEqual(ifin, 0);
            var f = boxes[ifin];
            var l2 = boxes[i2];
            Assert.LessOrEqual(f.max.x, l2.min.x + 0.001f,
                "the buttress hangs on L2's west FACE; standing it on the deck eats the run-up");
        }

        static A.ClimbOutcome BestButtressClimb()
        {
            int ie = A.IndexOf(boxes, "T2_L2"), ifin = A.IndexOf(boxes, "T2_Buttress");
            var entry = boxes[ie];
            var fin = boxes[ifin];
            float inset = profile.SweptRadius + 0.05f;

            var best = new A.ClimbOutcome();
            best.peakY = -999f; best.landedOn = "(fell)";

            for (int zi = 0; zi < 7; zi++)
            {
                // The whole ledge edge beside the fin, not just the part inside the slot: the fin is
                // flush with the ledge's face, so the slot's MOUTH is past the fin's end.
                float z = Mathf.Lerp(Mathf.Max(entry.min.z, fin.min.z - 2f) + inset,
                                     Mathf.Min(entry.max.z, fin.max.z + 2f) - inset, zi / 6f);
                if (z < entry.min.z + inset || z > entry.max.z - inset) continue;
                for (int ai = 0; ai < 7; ai++)
                {
                    float ang = Mathf.Lerp(-20f, 30f, ai / 6f) * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(-Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 feet = new Vector3(entry.min.x + inset, entry.max.y, z);
                    if (!A.StandFree(feet, profile, boxes, ie)) continue;
                    var o = A.ClimbChimney(boxes, profile, feet,
                                           dir * profile.groundSpeed + Vector3.up * profile.JumpTakeoffSpeed,
                                           "T2_L8", "T2_L2", floorY);
                    if (o.success && (!best.success || o.pushes < best.pushes)) best = o;
                    else if (!best.success && o.peakY > best.peakY) best = o;
                }
            }
            return best;
        }
    }
}
