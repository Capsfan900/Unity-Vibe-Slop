using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;
using T = VibeGame1.EditorTools.LevelTraversalAnalyzer;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The parkour-first rework of Level_01 (docs/BACKLOG.md §0), proven off the definition with the
    /// shipped movement numbers, the way the span tests prove the wall-run lines:
    /// the six filler spawns stand on perches BESIDE the route with a clear bolt line across it, the T3
    /// balloon arc chains at a pop's MEASURED spacing (~5 m across, ~3 m up) and lands on the span, and the water
    /// sheets lie on decks. <c>LevelDefinitionAuthoring.Apply</c> is run on a COPY, so the shipped asset
    /// is read but never written here; a second test proves the same method is idempotent.
    /// </summary>
    public class LevelTraversalTests
    {
        LevelDefinition def;
        List<A.Box> boxes;
        A.MoveProfile p;

        [SetUp]
        public void Load()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            Assert.IsNotNull(shipped, "Level_01_Level.asset is missing");
            // A copy with the rework applied: the tests hold whether or not 8a has been run on disk yet.
            def = Object.Instantiate(shipped);
            LevelDefinitionAuthoring.Apply(def);
            boxes = A.BoxesFrom(def);
            string err;
            Assert.IsTrue(A.TryLoadProfile(out p, out err), err);
        }

        [TearDown]
        public void Unload() { if (def != null) Object.DestroyImmediate(def); }

        A.Box Box(string name)
        {
            int i = A.IndexOf(boxes, name);
            Assert.GreaterOrEqual(i, 0, name + " is not in the level");
            return boxes[i];
        }

        [Test]
        public void TheReworkIsIdempotent()
        {
            string once = LevelDefinitionAuthoring.Apply(def);
            int platforms = def.platforms.Length, balloons = def.balloons.Length, waters = def.waters.Length;
            string twice = LevelDefinitionAuthoring.Apply(def);
            Assert.AreEqual(once, twice);
            Assert.AreEqual(platforms, def.platforms.Length, "a second run added perches");
            Assert.AreEqual(balloons, def.balloons.Length);
            Assert.AreEqual(waters, def.waters.Length);
        }

        [Test]
        public void EveryFillerSpawnStandsOnItsPerch_NotOnTheRoute()
        {
            foreach (var pc in LevelDefinitionAuthoring.Perches)
            {
                var perch = Box(pc.name);
                SpawnDef s = null;
                foreach (var sd in def.spawns) if (sd.name == pc.spawn) s = sd;
                Assert.IsNotNull(s, pc.spawn + " is gone; spawner names are load-bearing (DebugHarness finds them by name)");
                Assert.That(s.position.x, Is.InRange(perch.min.x, perch.max.x), pc.spawn + " is off its perch in x");
                Assert.That(s.position.z, Is.InRange(perch.min.z, perch.max.z), pc.spawn + " is off its perch in z");
                Assert.AreEqual(perch.max.y + 0.1f, s.position.y, 0.01f, pc.spawn + " does not stand on the perch top");
                // Beside the route, never on it: the perch overlaps no other box in plan.
                foreach (var b in boxes)
                {
                    if (b.name == pc.name) continue;
                    bool overlap = perch.min.x < b.max.x && perch.max.x > b.min.x && perch.min.z < b.max.z && perch.max.z > b.min.z;
                    Assert.IsFalse(overlap, pc.name + " overlaps " + b.name + " in plan; a perch hangs beside the course");
                }
            }
        }

        [Test]
        public void EveryShooterHasAClearBoltLineAcrossItsRoute()
        {
            // The band is the shooter's (EnemyData.projectileMinRange/MaxRange, 6-30 m on Grunt and Heavy):
            // read off the data so a retune re-judges every perch.
            var grunt = AssetDatabase.LoadAssetAtPath<EnemyData>("Assets/Data/Enemies/Grunt.asset");
            float lo = grunt != null ? grunt.projectileMinRange : 6f, hi = grunt != null ? grunt.projectileMaxRange : 30f;
            foreach (var pc in LevelDefinitionAuthoring.Perches)
            {
                var v = T.AnalyzeShooter(boxes, pc.name, pc.spawn, pc.covers.Split(','), lo, hi);
                Assert.GreaterOrEqual(v.covered.Count, 2, v.Summary() + " — a shooter that cannot put a bolt across at least two decks of its route is filler again");
                Assert.AreEqual(0, v.outOfBand.Count, v.Summary() + " — a covered deck lies outside the shooter's band");
            }
        }

        [Test]
        public void TheArcChainsAtAPopsMeasuredSpacing()
        {
            // A pop (11 m/s up, 9 m/s carry, 0.45 s float at 0.55 gravity) apexes ~3.5 m above the orb
            // about 5 m out, so the next orb sits <= 5.7 m across and 2.4-3.3 m UP; lower and the player
            // sails over it (the yard's 1.2 m rise predates the float). Every orb clear of every box.
            Assert.GreaterOrEqual(def.balloons.Length, 3, "an arc is at least three orbs");
            for (int i = 0; i < def.balloons.Length; i++)
            {
                var o = def.balloons[i];
                foreach (var b in boxes)
                {
                    float d = A.SegmentBoxDistance(o.position.x, o.position.z, o.position.y, o.position.y, b);
                    Assert.Greater(d, o.radius + 0.4f, o.name + " sits inside or against " + b.name);
                }
                if (i == 0) continue;
                var prev = def.balloons[i - 1];
                Vector3 flat = o.position - prev.position; flat.y = 0f;
                Assert.LessOrEqual(flat.magnitude, 5.7f, prev.name + " -> " + o.name + " is " + flat.magnitude.ToString("0.0") + " m across, past the yard's 5.5 m step");
                Assert.That(o.position.y - prev.position.y, Is.InRange(2.4f, 3.3f), prev.name + " -> " + o.name + " rises " + (o.position.y - prev.position.y).ToString("0.0") + " m; a pop apexes ~3.5 m up, so 2.4-3.3 m is the band a chain hits without the dash");
            }
        }

        [Test]
        public void TheArcIsFlownFromTheEntryOntoTheSpan()
        {
            var v = T.AnalyzeChain(boxes, def.balloons, "T3_Entry", "T3_Span", p, def.killZone.center.y);
            Assert.IsTrue(v.complete, v.Summary());
            // The links between orbs must not need the dash — that is the rhythm; only the final fall
            // onto the span may spend it (docs/MOVEMENT-PRINCIPLES.md rule 5: the dash is the skill).
            for (int i = 1; i < v.links.Count - 1; i++)
                Assert.IsFalse(v.links[i].neededDash, v.links[i].Summary() + " — an orb-to-orb link should be a pop alone");
        }

        [Test]
        public void TheArcDoesNotObstructThePillarHops()
        {
            // The orbs live west of the pillars; a hop's launch band is on the pillar tops. Every orb is
            // more than a body away from every pillar in plan.
            foreach (var o in def.balloons)
                foreach (var name in new[] { "T3_Pillar_1", "T3_Pillar_2", "T3_Pillar_3", "T3_Pillar_4", "T3_Entry", "T3_Span" })
                {
                    var b = Box(name);
                    float dx = Mathf.Max(b.min.x - o.position.x, o.position.x - b.max.x, 0f);
                    float dz = Mathf.Max(b.min.z - o.position.z, o.position.z - b.max.z, 0f);
                    Assert.Greater(Mathf.Sqrt(dx * dx + dz * dz), o.radius + p.SweptRadius, o.name + " hangs over " + name + "'s top: a runner on the deck would pop it");
                }
        }

        [Test]
        public void EveryWaterSheetLiesOnADeck()
        {
            Assert.GreaterOrEqual(def.waters.Length, 2, "the rework lays at least two water lines");
            foreach (var w in def.waters)
            {
                var v = T.AnalyzeWater(boxes, w);
                Assert.IsTrue(v.onADeck, v.Summary());
                Assert.IsTrue(v.insideDeck, v.Summary() + " — the sheet hangs past its deck's edge");
                Assert.IsTrue(v.flowIsUnit, v.Summary());
            }
        }

        [Test]
        public void TheSpanWaterTurnsTheRunTowardTheFirstStep()
        {
            WaterDef turn = null;
            foreach (var w in def.waters) if (w.name == "T3_Water_Turn") turn = w;
            Assert.IsNotNull(turn, "no T3_Water_Turn sheet");
            var step = Box("T3_Step_1");
            Vector3 toStep = new Vector3((step.min.x + step.max.x) * 0.5f, 0f, (step.min.z + step.max.z) * 0.5f) - new Vector3(turn.center.x, 0f, turn.center.z);
            float angle = Vector3.Angle(toStep, turn.flowDirection);
            Assert.Less(angle, 25f, "the turn sheet flows " + angle.ToString("0") + " deg off the line to T3_Step_1; it should carry the run into the step");
        }

        [Test]
        public void TheFastDeckWaterIsTheSlideLine()
        {
            WaterDef fast = null;
            foreach (var w in def.waters) if (w.name == "T1_Water_Fast") fast = w;
            Assert.IsNotNull(fast);
            Assert.AreEqual("T1_Fast_1", T.AnalyzeWater(boxes, fast).deck, "the T1 water rides the slide-jump deck, nothing else");
            Assert.Greater(p.waterFloorSpeed, p.groundSpeed, "water must be faster than the run or the line is decoration");
        }
    }
}
