using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VibeGame1.EditorTools;
using YardBox = VibeGame1.EditorTools.SandboxBuilder.YardBox;
using YardKind = VibeGame1.EditorTools.SandboxBuilder.YardKind;
using YardMat = VibeGame1.EditorTools.SandboxBuilder.YardMat;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>The sandbox movement yard, measured before anyone stands on it.</b>
    ///
    /// <para><see cref="SandboxBuilder.YardLayout"/> is the yard as literals; the scene is only ever a
    /// rendering of it. These tests hold the numbers the yard exists to provide — gap sizes you can name,
    /// a tower every tier of which is reachable under the project's rise / gap rule, a wall long enough
    /// for a full run, a ledge with nothing after it — so a nudge to one box in the builder is caught here
    /// as a broken contract rather than discovered as a jump that no longer lands.</para>
    ///
    /// <para>Unlike <c>WallRunGauntletTests</c> nothing is restated: the test reads the same list the
    /// builder consumes. What passing means: the geometry is what the doc says it is. Nobody has run it.</para>
    /// </summary>
    public class MovementYardTests
    {
        const float Eps = 0.001f;
        const float MaxRise = 1.5f;   // the reachability rule: rise <= 1.5 m with gap <= 4.5 m
        const float MaxGap = 4.5f;

        static List<YardBox> yard;

        [OneTimeSetUp]
        public void Load()
        {
            yard = SandboxBuilder.YardLayout();
            Assert.IsNotEmpty(yard);
        }

        static YardBox Find(string name)
        {
            foreach (var b in yard) if (b.name == name) return b;
            Assert.Fail("no yard box named " + name);
            return default(YardBox);
        }

        static IEnumerable<YardBox> Solids()
        {
            return yard.Where(b => b.kind != YardKind.Stripe && b.kind != YardKind.Water && b.kind != YardKind.Balloon);
        }

        // ---- the pivot's traversal pieces (2026-09-04) ---------------------------------------------

        [Test]
        public void TheWaterLaneLiesOnTheFloorInsideTheYard_OffTheDoorwayLine()
        {
            var water = Find("Yard_Water");
            Assert.AreEqual(YardKind.Water, water.kind);
            Assert.AreEqual(0f, water.Bottom, Eps, "the sheet must sit ON the floor, not in it");
            Assert.AreEqual(SandboxBuilder.WaterThickness, water.size.y, Eps);
            Assert.GreaterOrEqual(water.MinX, SandboxBuilder.YardMinX - Eps);
            Assert.LessOrEqual(water.MaxX, SandboxBuilder.YardMaxX + Eps);
            Assert.GreaterOrEqual(water.MinZ, SandboxBuilder.YardDoorHalfWidth - Eps, "the lane crosses the doorway line");
            // Nothing solid stands on the lane: it is a clean 60 m skate.
            foreach (var s in Solids())
                if (s.kind != YardKind.Floor)
                    Assert.IsFalse(s.MinX < water.MaxX && water.MinX < s.MaxX && s.MinZ < water.MaxZ && water.MinZ < s.MaxZ,
                                   s.name + " stands on the water lane");
        }

        [Test]
        public void EveryBalloonIsWithinAPopAndADashOfThePrevious()
        {
            // The chain is DASHED, not ridden: 11 m/s against -30 is a 2.0 m rise (no float counted --
            // the float only makes it easier), the run's 11 m/s carries through a 2v/g = 0.73 s hang
            // (8 m), and the re-armed dash adds 3.5 m. Each orb must sit inside that reach of the one
            // before it, off the doorway line, and the first inside a standing jump (2.4 m) plus the radius.
            var orbs = yard.Where(b => b.kind == YardKind.Balloon).OrderBy(b => b.center.x).ToList();
            // 2026-09-05: the step is 5.0 m across and 2.4 m up -- the arc the analyser measures after a pop
            // (9 m/s carry, 11 m/s launch, the float), not the flatter hop the chain was first laid for.
            Assert.AreEqual(5.0f, SandboxBuilder.BalloonChainStep, 0.01f, "the chain step is the measured pop arc");
            Assert.AreEqual(2.4f, SandboxBuilder.BalloonChainRise, 0.01f, "the rise stays inside the pop's 2 m + the orb");
            Assert.AreEqual(SandboxBuilder.BalloonChainCount, orbs.Count, "balloon count");
            const float g = 30f, run = 11f, dash = 22f * 0.16f;
            float v = SandboxBuilder.BalloonLaunch;
            float rise = v * v / (2f * g);
            float hang = 2f * v / g;
            Assert.LessOrEqual(orbs[0].center.y - SandboxBuilder.BalloonRadius, 2.4f + 0.05f, "the first orb is out of a jump's reach");
            for (int i = 1; i < orbs.Count; i++)
            {
                float up = orbs[i].center.y - orbs[i - 1].center.y;
                Vector3 d = orbs[i].center - orbs[i - 1].center; d.y = 0f;
                Assert.LessOrEqual(up, rise + SandboxBuilder.BalloonRadius, orbs[i].name + " is above the pop's reach");
                Assert.LessOrEqual(d.magnitude, run * hang + dash + SandboxBuilder.BalloonRadius, orbs[i].name + " is beyond a hang plus a dash");
                Assert.Greater(d.magnitude, dash, orbs[i].name + " is so close a dash alone lands it -- no aiming needed");
            }
            foreach (var o in orbs)
                Assert.IsTrue(o.MaxZ < -SandboxBuilder.YardDoorHalfWidth || o.MinZ > SandboxBuilder.YardDoorHalfWidth,
                              o.name + " hangs over the doorway line");
        }

        /// <summary>Edge-to-edge horizontal separation between two boxes (0 when they touch or overlap in plan).</summary>
        static float PlanGap(YardBox a, YardBox b)
        {
            float dx = Mathf.Max(Mathf.Max(a.MinX - b.MaxX, b.MinX - a.MaxX), 0f);
            float dz = Mathf.Max(Mathf.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static bool Overlaps(YardBox a, YardBox b)
        {
            return a.MinX < b.MaxX - Eps && b.MinX < a.MaxX - Eps
                && a.MinZ < b.MaxZ - Eps && b.MinZ < a.MaxZ - Eps
                && a.Bottom < b.Top - Eps && b.Bottom < a.Top - Eps;
        }

        // ---- the annex as a whole -------------------------------------------------------------------

        [Test]
        public void TheYardStaysOutOfTheArena()
        {
            // Everything east of the arena wall at x = 30. WallRunGauntletTests and the enemy pads own the
            // arena; the yard may touch its east wall and nothing else.
            foreach (var b in yard)
                Assert.GreaterOrEqual(b.MinX, SandboxBuilder.HalfExtent - Eps, b.name + " reaches into the arena");
        }

        [Test]
        public void TheDoorwayFloorBridgesTheWallGapAtFloorLevel()
        {
            var door = Find("Yard_Doorway");
            Assert.AreEqual(30f, door.MinX, Eps);
            Assert.AreEqual(SandboxBuilder.YardMinX, door.MaxX, Eps);
            Assert.AreEqual(0f, door.Top, Eps, "the doorway floor is not flush with the arena floor");
            Assert.AreEqual(-SandboxBuilder.YardDoorHalfWidth, door.MinZ, Eps);
            Assert.AreEqual(SandboxBuilder.YardDoorHalfWidth, door.MaxZ, Eps);

            var floor = Find("Yard_Floor");
            Assert.AreEqual(0f, floor.Top, Eps);
            Assert.AreEqual(SandboxBuilder.YardMinX, floor.MinX, Eps);
            Assert.AreEqual(SandboxBuilder.YardMaxX, floor.MaxX, Eps);
        }

        [Test]
        public void TheDoorwayLineIsOpenFloorAsFarAsTheTower()
        {
            // From the doorway you look straight east down z -3..3. Nothing solid may stand in that lane
            // before the drop tower, so a straight sprint has 94 m to build in.
            var tower = Find("Yard_Tower_4");
            foreach (var b in Solids())
            {
                if (b.kind == YardKind.Floor || b.name.StartsWith("Yard_Tower")) continue;
                bool inLane = b.MinZ < SandboxBuilder.YardDoorHalfWidth - Eps && b.MaxZ > -SandboxBuilder.YardDoorHalfWidth + Eps;
                bool beforeTower = b.MinX < tower.MinX - Eps;
                Assert.IsFalse(inLane && beforeTower && b.MaxX > SandboxBuilder.YardMinX + Eps,
                    b.name + " stands in the doorway lane (z -3..3) before the tower");
            }
            Assert.GreaterOrEqual(tower.MinX - SandboxBuilder.YardMinX, 90f, "the straight run from the doorway has shrunk");
        }

        [Test]
        public void NoTwoSolidsOverlap()
        {
            var solids = Solids().ToList();
            for (int i = 0; i < solids.Count; i++)
                for (int j = i + 1; j < solids.Count; j++)
                    Assert.IsFalse(Overlaps(solids[i], solids[j]), solids[i].name + " overlaps " + solids[j].name);
        }

        [Test]
        public void TheYardIsFencedOnItsOuterEdges()
        {
            foreach (var name in new[] { "Yard_Kerb_N", "Yard_Kerb_S", "Yard_Kerb_E", "Yard_Kerb_W_N", "Yard_Kerb_W_S" })
            {
                var k = Find(name);
                Assert.AreEqual(YardKind.Kerb, k.kind);
                Assert.GreaterOrEqual(k.Top, 1f - Eps, name + " is lower than 1 m");
            }
            Assert.AreEqual(SandboxBuilder.YardMaxX, Find("Yard_Kerb_E").center.x, Eps, "the far kerb has left the east edge");
        }

        // ---- distance stripes -------------------------------------------------------------------------

        [Test]
        public void StripesEveryTenMetres_BrighterEveryFifty()
        {
            var stripes = yard.Where(b => b.kind == YardKind.Stripe).OrderBy(b => b.center.x).ToList();
            var expected = new List<float>();
            for (int x = 40; x <= 150; x += 10) expected.Add(x);
            Assert.AreEqual(expected.Count, stripes.Count, "stripe count");
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i], stripes[i].center.x, Eps, stripes[i].name);
                Assert.AreEqual(expected[i] % 50 == 0 ? YardMat.Yellow : YardMat.Cyan, stripes[i].mat, stripes[i].name + " colour");
                Assert.AreEqual(SandboxBuilder.YardHalfZ * 2f, stripes[i].size.z, Eps, stripes[i].name + " does not span the yard");
                Assert.LessOrEqual(stripes[i].size.y, 0.2f, stripes[i].name + " is a step, not a marker");
            }
        }

        // ---- gap ladder -------------------------------------------------------------------------------

        [Test]
        public void TheGapLadderGapsAre_4_6_8_10_12()
        {
            var pads = yard.Where(b => b.kind == YardKind.Pad).OrderBy(b => b.MinX).ToList();
            Assert.AreEqual(6, pads.Count, "pad count");

            var gaps = new List<float>();
            for (int i = 1; i < pads.Count; i++)
            {
                Assert.AreEqual(pads[i - 1].Top, pads[i].Top, Eps, "the ladder is level");
                Assert.AreEqual(pads[i - 1].center.z, pads[i].center.z, Eps, "the ladder is a straight row");
                gaps.Add(pads[i].MinX - pads[i - 1].MaxX);
            }
            CollectionAssert.AreEqual(new[] { 4f, 6f, 8f, 10f, 12f }, gaps.Select(g => Mathf.Round(g * 1000f) / 1000f).ToArray());

            Assert.AreEqual(40f, pads[0].MinX, Eps, "the first pad's edge sits on the 40 m stripe");
            Assert.AreEqual(2f, pads[0].Top, Eps, "pad height");
        }

        [Test]
        public void TheFirstPadIsClimbedInHops_NotOneJump()
        {
            var step = Find("Yard_GapLadder_Step");
            var first = yard.Where(b => b.kind == YardKind.Pad).OrderBy(b => b.MinX).First();
            Assert.LessOrEqual(step.Top, MaxRise + Eps, "floor to step");
            Assert.LessOrEqual(first.Top - step.Top, MaxRise + Eps, "step to first pad");
            Assert.LessOrEqual(PlanGap(step, first), MaxGap + Eps);
            Assert.Less(step.MaxX, first.MinX + Eps, "the step is not on the approach side of the pad");
        }

        // ---- long walls -------------------------------------------------------------------------------

        [Test]
        public void TheLongWallsAreLongAndTallAndMakeACorridor()
        {
            var a = Find("Yard_LongWall");
            var b = Find("Yard_LongWall_B");
            foreach (var w in new[] { a, b })
            {
                Assert.AreEqual(YardKind.Wall, w.kind);
                Assert.GreaterOrEqual(w.size.y, 8f - Eps, w.name + " is shorter than 8 m");
                Assert.GreaterOrEqual(w.size.x, 40f - Eps, w.name + " is shorter than 40 m");
                Assert.AreEqual(0f, w.Bottom, Eps, w.name + " floats");
            }
            Assert.AreEqual(a.MinX, b.MinX, Eps, "the walls are not parallel-aligned");
            Assert.AreEqual(a.MaxX, b.MaxX, Eps, "the walls are not parallel-aligned");
            Assert.AreEqual(5.5f, b.MinZ - a.MaxZ, Eps, "corridor width");
        }

        [Test]
        public void TheLongWallExitsIntoOpenFloor()
        {
            // A run along the SOUTH face of Yard_LongWall (z 17.5) leaves you heading east or south into
            // nothing: no solid within 15 m south of the wall across its whole length, and none within
            // 15 m past its east end.
            var wall = Find("Yard_LongWall");
            foreach (var b in Solids())
            {
                if (b.name == wall.name || b.kind == YardKind.Floor) continue;
                bool southBand = b.MaxZ > wall.MinZ - 15f && b.MinZ < wall.MinZ && b.MaxX > wall.MinX && b.MinX < wall.MaxX;
                Assert.IsFalse(southBand, b.name + " sits in the long wall's exit zone");
                bool pastEnd = b.MinX < wall.MaxX + 15f && b.MaxX > wall.MaxX && b.MaxZ > wall.MinZ - 2f && b.MinZ < Find("Yard_LongWall_B").MaxZ + 2f;
                Assert.IsFalse(pastEnd, b.name + " sits past the long walls' east end");
            }
        }

        // ---- drop tower -------------------------------------------------------------------------------

        [Test]
        public void EveryTowerTierIsReachableUnderTheRiseGapRule()
        {
            // The climb chain is every tower piece ordered by its top. Consecutive pieces must obey the
            // project rule (rise <= 1.5 m, gap <= 4.5 m), starting from the floor.
            var chain = yard.Where(b => b.name.StartsWith("Yard_Tower")).OrderBy(b => b.Top).ToList();
            Assert.AreEqual(8, chain.Count, "four tiers and four steps");

            float prevTop = 0f;
            YardBox? prev = null;
            foreach (var piece in chain)
            {
                Assert.LessOrEqual(piece.Top - prevTop, MaxRise + Eps, piece.name + " rises more than 1.5 m over the piece before it");
                if (prev.HasValue)
                    Assert.LessOrEqual(PlanGap(prev.Value, piece), MaxGap + Eps, piece.name + " is more than 4.5 m from the piece before it");
                prevTop = piece.Top;
                prev = piece;
            }

            var tiers = chain.Where(b => b.kind == YardKind.Tier).OrderBy(b => b.Top).ToList();
            CollectionAssert.AreEqual(new[] { 3f, 6f, 9f, 12f }, tiers.Select(t => t.Top).ToArray());
            foreach (var t in tiers) Assert.AreEqual(8f, t.size.x, Eps, t.name + " is not 8 m across");
        }

        [Test]
        public void EveryTowerTierDropsOntoFlatFloor()
        {
            // The east face of every tier is a straight drop: nothing solid between it and the east kerb
            // across the tier's own z band, and at least 15 m of floor to land on.
            var kerb = Find("Yard_Kerb_E");
            foreach (var tier in yard.Where(b => b.kind == YardKind.Tier))
            {
                Assert.GreaterOrEqual(kerb.MinX - tier.MaxX, 15f, tier.name + " has too little floor east of it");
                foreach (var b in Solids())
                {
                    if (b.name == tier.name || b.kind == YardKind.Floor || b.kind == YardKind.Kerb) continue;
                    bool east = b.MinX >= tier.MaxX - Eps && b.MaxZ > tier.MinZ + Eps && b.MinZ < tier.MaxZ - Eps;
                    Assert.IsFalse(east, b.name + " stands in " + tier.name + "'s drop zone");
                }
            }
        }

        // ---- runway -----------------------------------------------------------------------------------

        [Test]
        public void TheRunwayIsClimbedFromTheWestUnderTheRiseGapRule()
        {
            var runway = Find("Yard_Runway");
            Assert.AreEqual(4f, runway.Top, Eps, "runway height");
            Assert.AreEqual(30f, runway.size.x, Eps, "runway length");

            var chain = new List<YardBox> { Find("Yard_Runway_Step1"), Find("Yard_Runway_Step2"), runway };
            float prevTop = 0f;
            for (int i = 0; i < chain.Count; i++)
            {
                Assert.LessOrEqual(chain[i].Top - prevTop, MaxRise + Eps, chain[i].name + " rise");
                if (i > 0)
                {
                    Assert.LessOrEqual(PlanGap(chain[i - 1], chain[i]), MaxGap + Eps, chain[i].name + " gap");
                    Assert.Less(chain[i - 1].MaxX, chain[i].MinX + Eps, "the steps are not on the west end");
                }
                prevTop = chain[i].Top;
            }
        }

        [Test]
        public void TheRunwayHasThirtyMetresOfNothingEastOfIt()
        {
            var runway = Find("Yard_Runway");
            float clearUntil = runway.MaxX + 30f;
            foreach (var b in Solids())
            {
                if (b.name == runway.name || b.kind == YardKind.Floor) continue;
                bool inBand = b.MaxZ > runway.MinZ + Eps && b.MinZ < runway.MaxZ - Eps;
                bool inRun = b.MaxX > runway.MaxX + Eps && b.MinX < clearUntil - Eps;
                Assert.IsFalse(inBand && inRun, b.name + " is inside the 30 m past the runway's edge");
            }
        }
    }
}
