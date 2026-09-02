using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>Span 4 of Level_01 — the approach to the Hollow Warden — flown before anyone stands on it.</b>
    ///
    /// <para>The span runs from the Spellsword arena's exit gate (z 283) to the boss arena's mouth. It was
    /// 16 m of railed walkway; it is now an 80 m causeway that breaks into three alternating wall-run legs
    /// of escalating length (14, 15, 16 m of face), each with a stepping stone on the open side as the
    /// base-kit line, the last leg landing on a rail-free <c>T4_Threshold</c> that abuts
    /// <c>Boss_Approach</c>. (Landing on the approach itself was tried: its rails cut the arriving
    /// routes from 227 to 26.) The whole boss unit (<c>Boss_*</c>, <c>Wall_Boss_*</c>,
    /// <c>Pillar_Boss_*</c>, spawn, pickup, torches, checkpoint, gate and trigger) moved +86 m in z,
    /// together.</para>
    ///
    /// <para>Three claims, each settled by <see cref="A"/> against the shipped <c>Player.prefab</c>:
    /// the fast line exists as a RUN (authored against <c>longest</c>, never <c>best</c>), it exists from a
    /// minimum-speed take-off too, and the slow line exists for a player who never touches a wall.
    /// A fourth guards the merge: the boss unit is the same unit it was, just further away.</para>
    ///
    /// <para>What passing means: a clean line exists in the arithmetic. Nobody has run this causeway.</para>
    /// </summary>
    public class LevelSpan4Tests
    {
        static LevelDefinition def;
        static List<A.Box> boxes;
        static A.MoveProfile profile;
        static float floorY;

        /// <summary>The slowest take-off the analyser's speed ladder will still mount a wall from: the
        /// ladder tops out at this and the wall wants 7 m/s ALONG the face.</summary>
        const float MinSpeedTakeoff = 8f;

        struct Leg
        {
            public string from, wall, to;
            public float minLongest;
            public Leg(string f, string w, string t, float m) { from = f; wall = w; to = t; minLongest = m; }
        }

        static readonly Leg[] Legs =
        {
            new Leg("T4_Causeway", "T4_Face_E1", "T4_Pier_1",     1.3f),
            new Leg("T4_Pier_1",   "T4_Face_W2", "T4_Pier_2",     1.4f),
            new Leg("T4_Pier_2",   "T4_Face_E3", "T4_Threshold",  1.5f),
        };

        /// <summary>The base-kit line: every hop a player who never wall-runs has to make.</summary>
        static readonly string[] SlowLine =
        {
            "T4_Causeway", "T4_Stone_1", "T4_Pier_1", "T4_Stone_2", "T4_Pier_2", "T4_Stone_3", "T4_Threshold",
        };

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

        static A.Box Find(string name)
        {
            int i = A.IndexOf(boxes, name);
            Assert.GreaterOrEqual(i, 0, name + " is not in the level");
            return boxes[i];
        }

        // ------------------------------------------------------------------ the fast line

        [Test]
        public void EveryLeg_IsRunnableAtASprint_AndIsARunNotAHop()
        {
            foreach (var leg in Legs)
            {
                var v = A.AnalyzeWallRunGap(boxes, leg.from, leg.wall, leg.to, profile, profile.groundSpeed, floorY);
                TestContext.Out.WriteLine(v.Summary());
                Assert.IsTrue(v.exists, v.Summary());
                Assert.GreaterOrEqual(v.cleanRoutes, 3,
                    "the crossing exists only as a pixel-perfect line, which is a trap, not a route: " + v.Summary());
                Assert.GreaterOrEqual(v.longest.runDuration, leg.minLongest,
                    "every arriving route leaves the wall early — a wall JUMP in a costume, not a run: " + v.Summary());
            }
        }

        [Test]
        public void EveryLeg_IsRunnableFromAMinimumSpeedTakeoff()
        {
            // Wall running is a base ability, so the fast line may be leaned on by a player who is not
            // carrying a sprint. A take-off at 8 m/s (7 m/s along the face after the aim spreads it) has
            // to mount, run and arrive too.
            foreach (var leg in Legs)
            {
                var v = A.AnalyzeWallRunGap(boxes, leg.from, leg.wall, leg.to, profile, MinSpeedTakeoff, floorY);
                TestContext.Out.WriteLine("min-speed: " + v.Summary());
                Assert.IsTrue(v.exists, v.Summary());
                Assert.GreaterOrEqual(v.cleanRoutes, 3, v.Summary());
                Assert.GreaterOrEqual(v.longest.runDuration, 1.0f, v.Summary());
            }
        }

        [Test]
        public void TheThreshold_AbutsTheBossApproach_NoHopNeeded()
        {
            // The slow line ends on the threshold and WALKS onto the approach: same deck height, no gap.
            // A gap here would put a jump between the player and the boss gate that no test flies.
            var thr = Find("T4_Threshold");
            var app = Find("Boss_Approach");
            Assert.AreEqual(thr.max.z, app.min.z, 0.01f, "the threshold and the approach do not meet");
            Assert.AreEqual(thr.Top, app.Top, 0.01f, "the threshold and the approach are at different heights");
            Assert.LessOrEqual(app.min.x, thr.min.x + 0.01f, "the approach is narrower than the threshold on the west");
            Assert.GreaterOrEqual(app.max.x, thr.max.x - 0.01f, "the approach is narrower than the threshold on the east");
        }

        [Test]
        public void TheLastLeg_RidesTheFullClockIntoTheArenaMouth()
        {
            // The final approach is the level's longest run. A 16 m face is sized so a sprint entry
            // spends the whole 1.6 s loan on it before the exit throws you onto the threshold.
            var leg = Legs[Legs.Length - 1];
            var v = A.AnalyzeWallRunGap(boxes, leg.from, leg.wall, leg.to, profile, profile.groundSpeed, floorY);
            Assert.IsTrue(v.exists, v.Summary());
            Assert.GreaterOrEqual(v.longest.runDuration, profile.WallRun.maxDuration - 0.1f,
                "the last face no longer hosts a full-duration run: " + v.Summary());
        }

        [Test]
        public void EveryFace_IsLongEnoughToHostAFullRun()
        {
            // A wall under ~14 m cannot host a full run (ENGINEERING-LOG, "author against longest").
            foreach (var leg in Legs)
            {
                var w = Find(leg.wall);
                Assert.GreaterOrEqual(w.max.z - w.min.z, 14f, leg.wall + " is too short to be run, only jumped off");
            }
        }

        [Test]
        public void EveryLeg_NeedsTheWall_ASlideJumpDoesNotCrossWithoutIt()
        {
            // If the pads were reachable by a slide-jump alone, the faces would be decoration and the
            // "fast line" would be a jump. They are not.
            foreach (var leg in Legs)
            {
                var v = A.AnalyzeHop(boxes, leg.from, leg.to, profile, profile.SlideJumpSpeed, floorY);
                Assert.IsFalse(v.exists, "the wall is not load-bearing; the gap is a slide-jump: " + v.Summary());
            }
        }

        // ------------------------------------------------------------------ the slow line

        [Test]
        public void TheSlowLine_IsCleanForTheBaseKit_FromAtLeastThreeTakeoffPoints()
        {
            for (int i = 0; i + 1 < SlowLine.Length; i++)
            {
                var v = A.AnalyzeHop(boxes, SlowLine[i], SlowLine[i + 1], profile, profile.groundSpeed, floorY);
                TestContext.Out.WriteLine(v.Summary());
                Assert.IsTrue(v.exists, v.Summary());
                Assert.GreaterOrEqual(v.cleanLaunchPoints, 3,
                    "the baseline hop exists from too few places to be a route: " + v.Summary());
                Assert.LessOrEqual(v.gap, 6f, "outside the documented base-kit envelope: " + v.Summary());
                Assert.LessOrEqual(v.rise, 1f, "outside the documented base-kit envelope: " + v.Summary());
            }
        }

        // ------------------------------------------------------------------ the boss unit moved as one

        [Test]
        public void TheBossUnit_KeptItsShippedRelativeLayout()
        {
            // Everything is measured from Boss_Arena's centre, which is where the unit was defined from.
            // These are the pre-move offsets restated; if any one of them drifts, half the unit moved.
            Vector3 c = Find("Boss_Arena").Center;
            AssertOffset("Boss_Approach",        c, new Vector3(0f, 0f, -29f));
            AssertOffset("Boss_Approach_Rail_L", c, new Vector3(-3.1f, 1.1f, -29f));
            AssertOffset("Boss_Approach_Rail_R", c, new Vector3(3.1f, 1.1f, -29f));
            AssertOffset("Wall_Boss_W",          c, new Vector3(-19.25f, 2.5f, 0f));
            AssertOffset("Wall_Boss_E",          c, new Vector3(19.25f, 2.5f, 0f));
            AssertOffset("Wall_Boss_S_L",        c, new Vector3(-11f, 2.5f, -19.25f));
            AssertOffset("Wall_Boss_S_R",        c, new Vector3(11f, 2.5f, -19.25f));
            AssertOffset("Wall_Boss_N_L",        c, new Vector3(-11f, 2.5f, 19.25f));
            AssertOffset("Wall_Boss_N_R",        c, new Vector3(11f, 2.5f, 19.25f));
            AssertOffset("Pillar_Boss_307_-16",  c, new Vector3(-16f, 3.5f, -13f));
            AssertOffset("Pillar_Boss_307_16",   c, new Vector3(16f, 3.5f, -13f));
            AssertOffset("Pillar_Boss_333_-16",  c, new Vector3(-16f, 3.5f, 13f));
            AssertOffset("Pillar_Boss_333_16",   c, new Vector3(16f, 3.5f, 13f));

            var arena = FindArena("Boss_Gate");
            AssertVec("Boss_Gate open",   arena.gateOpenPosition - c,   new Vector3(0f, -4f, -18.75f));
            AssertVec("Boss_Gate closed", arena.gateClosedPosition - c, new Vector3(0f, 2.5f, -18.75f));
            AssertVec("Boss_ArenaTrigger", arena.triggerPosition - c,   new Vector3(0f, 2.5f, -15f));

            AssertVec("Spawn_Boss", FindSpawn("Spawn_Boss").position - c, new Vector3(0f, 0.6f, 6f));
            AssertVec("Pickup_Boss_Lantern", FindPickup("Pickup_Boss_Lantern").position - c, new Vector3(-8f, 1.7f, -14f));
            AssertVec("Checkpoint_4", FindCheckpoint("Checkpoint_4").position - c, new Vector3(0f, 0.5f, -29f));
        }

        [Test]
        public void Checkpoint4_StillSatisfiesTheF5WarpContract()
        {
            // FeatureTests.Structure_WarpCheckpoint4LandsAtBossApproach: within 30 m of the trigger, on
            // the near side of it, at the boss tile's height. Restated here so it fails in EditMode
            // rather than in the next play-mode session.
            var cp = FindCheckpoint("Checkpoint_4");
            var arena = FindArena("Boss_Gate");
            Vector3 pp = cp.position + cp.spawnOffset;
            Vector3 ap = arena.triggerPosition;
            float d = Vector3.Distance(pp, ap);
            Assert.Less(d, 30f, "F5 lands too far from the boss trigger: " + d);
            Assert.Less(pp.z, ap.z, "F5 lands past the trigger");
            Assert.Less(Mathf.Abs(pp.y - ap.y), 8f, "F5 lands at the wrong height");

            // ...and on solid ground: the approach deck, not the void beside it.
            var deck = Find("Boss_Approach");
            Assert.IsTrue(pp.x > deck.min.x && pp.x < deck.max.x && pp.z > deck.min.z && pp.z < deck.max.z,
                "Checkpoint_4 spawn " + pp + " is not over Boss_Approach");
        }

        [Test]
        public void TheKillZone_StillCoversEveryPlatform()
        {
            var kz = def.killZone;
            float zMin = kz.center.z - kz.size.z * 0.5f, zMax = kz.center.z + kz.size.z * 0.5f;
            float xMin = kz.center.x - kz.size.x * 0.5f, xMax = kz.center.x + kz.size.x * 0.5f;
            foreach (var b in boxes)
            {
                Assert.IsTrue(b.min.z >= zMin && b.max.z <= zMax && b.min.x >= xMin && b.max.x <= xMax,
                    b.name + " hangs outside the kill zone's footprint — a fall there never ends");
                Assert.Greater(b.min.y, kz.center.y + kz.size.y * 0.5f, b.name + " is below the kill plane");
            }
        }

        [Test]
        public void TheSpan_IsActuallyLong()
        {
            // The ask was "stretched out". From the Spellsword gate to the boss gate, in metres.
            float from = Find("Wall_T3_N_L").max.z;
            float to = FindArena("Boss_Gate").gateClosedPosition.z;
            TestContext.Out.WriteLine("span 4: " + (to - from).ToString("0.0") + " m from the T3 exit gate to the boss gate");
            Assert.GreaterOrEqual(to - from, 90f, "span 4 is " + (to - from) + " m; it was 18 m and that was the problem");
        }

        // ------------------------------------------------------------------ helpers

        static void AssertOffset(string name, Vector3 origin, Vector3 expected)
        {
            AssertVec(name, Find(name).Center - origin, expected);
        }

        static void AssertVec(string what, Vector3 actual, Vector3 expected)
        {
            Assert.AreEqual(expected.x, actual.x, 0.01f, what + " x");
            Assert.AreEqual(expected.y, actual.y, 0.01f, what + " y");
            Assert.AreEqual(expected.z, actual.z, 0.01f, what + " z");
        }

        static ArenaDef FindArena(string gateName)
        {
            foreach (var a in def.arenas) if (a.gateName == gateName) return a;
            Assert.Fail("no arena with gate " + gateName);
            return null;
        }

        static SpawnDef FindSpawn(string name)
        {
            foreach (var s in def.spawns) if (s.name == name) return s;
            Assert.Fail("no spawn " + name);
            return null;
        }

        static PickupDef FindPickup(string name)
        {
            foreach (var p in def.pickups) if (p.name == name) return p;
            Assert.Fail("no pickup " + name);
            return null;
        }

        static CheckpointDef FindCheckpoint(string name)
        {
            foreach (var c in def.checkpoints) if (c.name == name) return c;
            Assert.Fail("no checkpoint " + name);
            return null;
        }
    }
}
