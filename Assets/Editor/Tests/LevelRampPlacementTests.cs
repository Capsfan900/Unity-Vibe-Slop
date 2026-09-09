using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>All shipped ramps in Level_01, checked against four things a ramp can silently get wrong.</b>
    ///
    /// <para>These exist because <c>LevelArcAnalyzer.BoxesFrom</c> reads <c>def.platforms</c> ONLY (line 59),
    /// so a ramp is invisible to the arc report and to every <c>Level*Tests</c> fixture: nothing in the
    /// shipped tooling can fail because of one, which also means nothing proves one. The four failures
    /// below are all invisible in the Inspector and all fatal in play:</para>
    ///
    /// <list type="number">
    /// <item><b>Too steep.</b> A <c>CharacterController</c> will not walk up anything past its 45°
    /// <c>slopeLimit</c>, so a steeper ramp is a WALL on the way up — a one-way door with no sign on it.
    /// The authoring ceiling is 35°, which leaves margin for the controller's own contact jitter.</item>
    /// <item><b>A base or a top that meets nothing.</b> A ramp is not a kicker: leaving the top edge gives
    /// no vertical impulse, so a ramp that stops short of its deck is only a jump with a shorter run-up,
    /// and a base hanging in the void is a step you cannot get onto. Both ends must land flush on a
    /// platform's walkable top.</item>
    /// <item><b>Cutting through something.</b> A ramp is solid geometry and can bury itself in a tower, a
    /// buttress or a water deck; the only boxes it may intersect are the two decks it joins.</item>
    /// <item><b>Standing in a wall-run corridor.</b> The span tests forbid a <c>T*_</c> BOX overlapping a
    /// wall-run wall in plan, and they cannot see a ramp — so the same rule is asserted here by hand.</item>
    /// </list>
    ///
    /// <para>What passing does NOT mean: nothing here says a ramp is fun, or that a slide up one feels
    /// right. The slope term bleeds a climbing slide (<c>TraversalMath.SlopeAccel</c>) and only a human
    /// can say whether the bleed on a 15° ramp reads as weight or as a wall.</para>
    /// </summary>
    public class LevelRampPlacementTests
    {
        const float AngleCeiling = 35f;
        static LevelDefinition def;

        [OneTimeSetUp]
        public void Load()
        {
            def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            Assert.IsNotNull(def, "Level_01_Level.asset is missing");
        }

        /// <summary>The shipped slopes, including the descent outside the small-connector table.</summary>
        static RampDef[] Authored()
        {
            // Check SHIPPED slopes, including the descent; the small-connector table is not the level.
            return def.ramps;
        }

        /// <summary>The asset holds what the table authors — proof the pass reached the disk (rule 9).</summary>
        [Test]
        public void TheAssetCarriesEveryAuthoredRamp()
        {
            Assert.IsNotNull(def.ramps, "Level_01 has no ramps array; run VibeGame1/8a");
            foreach (var want in LevelDefinitionAuthoring.Ramps)
            {
                RampDef got = null;
                foreach (var r in def.ramps) if (r != null && r.name == want.name) { got = r; break; }
                Assert.IsNotNull(got, want.name + " is authored but not in the asset; re-run VibeGame1/8a");
                Assert.AreEqual(want.run, got.run, 0.001f, want.name + " run");
                Assert.AreEqual(want.rise, got.rise, 0.001f, want.name + " rise");
                Assert.AreEqual("Stone", got.materialKey,
                    want.name + " must be M_Stone: a ramp cannot wear trim, so its MATERIAL is the only " +
                    "thing separating it from the deck it leaves, and M_Platform is that deck's own");
            }
        }

        /// <summary>Under the slope limit, with margin. A one-way door is the worst bug a ramp can be.</summary>
        [Test]
        public void NoRampIsSteeperThanABodyCanWalk()
        {
            foreach (var r in Authored())
                Assert.LessOrEqual(Mathf.Abs(r.AngleDegrees), AngleCeiling,
                    r.name + " is " + r.AngleDegrees.ToString("0.0") + "°; past ~35° a CharacterController " +
                    "stops walking up it and the ramp becomes a wall in one direction");
        }

        /// <summary>Both ends flush on a deck: no lip to jump, no void to step into.</summary>
        [Test]
        public void EveryRampMeetsADeckAtBothEnds()
        {
            var boxes = A.BoxesFrom(def);
            foreach (var r in Authored())
            {
                Assert.IsTrue(SitsOnADeck(boxes, r.basePosition),
                    r.name + "'s base at " + r.basePosition + " is not flush on any platform top; a ramp " +
                    "you cannot step onto is decoration");
                Assert.IsTrue(SitsOnADeck(boxes, r.TopPosition),
                    r.name + "'s top at " + r.TopPosition + " lands on nothing. A ramp is not a kicker — " +
                    "leaving the top gives no vertical — so a ramp that stops short of its deck is only a " +
                    "jump with a shorter run-up");
            }
        }

        static bool SitsOnADeck(System.Collections.Generic.IList<A.Box> boxes, Vector3 p)
        {
            foreach (var b in boxes)
                if (p.x >= b.min.x - 0.02f && p.x <= b.max.x + 0.02f &&
                    p.z >= b.min.z - 0.02f && p.z <= b.max.z + 0.02f &&
                    Mathf.Abs(b.max.y - p.y) < 0.02f) return true;
            return false;
        }

        /// <summary>
        /// A ramp may bury itself in the two decks it joins and in nothing else. Sampled over the slab's
        /// own volume rather than its bounding box: the AABB of a yawed 6 m ramp is half again as wide as
        /// the ramp and reports collisions that are not there.
        /// </summary>
        [Test]
        public void NoRampCutsThroughAnythingButItsOwnTwoDecks()
        {
            var boxes = A.BoxesFrom(def);
            foreach (var r in Authored())
            {
                Quaternion rot = r.Rotation;
                Vector3 h = r.BoxScale * 0.5f, c = r.BoxCenter;
                foreach (var b in boxes)
                {
                    if (Joins(b, r.basePosition) || Joins(b, r.TopPosition)) continue;
                    for (int i = 0; i <= 21; i++)
                        for (int j = 0; j <= 3; j++)
                            for (int k = 0; k <= 3; k++)
                            {
                                Vector3 p = c + rot * new Vector3(
                                    Mathf.Lerp(-h.x, h.x, k / 3f),
                                    Mathf.Lerp(-h.y, h.y, j / 3f),
                                    Mathf.Lerp(-h.z, h.z, i / 21f));
                                bool inside = p.x > b.min.x && p.x < b.max.x && p.y > b.min.y &&
                                              p.y < b.max.y && p.z > b.min.z && p.z < b.max.z;
                                Assert.IsFalse(inside, r.name + " cuts through " + b.name + " at " + p);
                            }
                }
            }
        }

        static bool Joins(A.Box b, Vector3 p)
        {
            return p.x >= b.min.x - 0.02f && p.x <= b.max.x + 0.02f &&
                   p.z >= b.min.z - 0.02f && p.z <= b.max.z + 0.02f && Mathf.Abs(b.max.y - p.y) < 0.05f;
        }

        /// <summary>
        /// The span tests forbid a <c>T*_</c> box overlapping a wall-run wall in plan, because a wall
        /// standing on the course eats its run-up. They read platforms only, so the ramps are checked here.
        /// </summary>
        [Test]
        public void NoRampStandsInAWallRunCorridor()
        {
            var boxes = A.BoxesFrom(def);
            foreach (var r in Authored())
            {
                foreach (var b in boxes)
                {
                    if (b.name == null) continue;
                    if (b.name.IndexOf("_Wall", System.StringComparison.Ordinal) < 0 &&
                        !b.name.StartsWith("Wall_")) continue;
                    bool overlap = FootprintsOverlap(r, b);
                    Assert.IsFalse(overlap, r.name + " overlaps " + b.name + " in plan; a ramp beside a " +
                        "wall-run wall steals the standoff the run needs");
                }
            }
        }

        static bool FootprintsOverlap(RampDef ramp, A.Box box)
        {
            Quaternion rotation = ramp.Rotation;
            Vector3 half = ramp.BoxScale * 0.5f;
            Vector3[] basis3 = { rotation * Vector3.right, rotation * Vector3.up, rotation * Vector3.forward };
            Vector2[] basis = {
                new Vector2(basis3[0].x, basis3[0].z),
                new Vector2(basis3[1].x, basis3[1].z),
                new Vector2(basis3[2].x, basis3[2].z)
            };
            Vector2 boxCenter = new Vector2((box.min.x + box.max.x) * 0.5f,
                                            (box.min.z + box.max.z) * 0.5f);
            Vector2 boxHalf = new Vector2((box.max.x - box.min.x) * 0.5f,
                                          (box.max.z - box.min.z) * 0.5f);
            Vector2 delta = new Vector2(ramp.BoxCenter.x, ramp.BoxCenter.z) - boxCenter;
            Vector2[] axes = {
                Vector2.right, Vector2.up,
                new Vector2(-basis[0].y, basis[0].x),
                new Vector2(-basis[1].y, basis[1].x),
                new Vector2(-basis[2].y, basis[2].x)
            };
            for (int i = 0; i < axes.Length; i++)
            {
                if (axes[i].sqrMagnitude < 1e-8f) continue;
                Vector2 axis = axes[i].normalized;
                float rampRadius = Mathf.Abs(Vector2.Dot(axis, basis[0])) * half.x +
                                   Mathf.Abs(Vector2.Dot(axis, basis[1])) * half.y +
                                   Mathf.Abs(Vector2.Dot(axis, basis[2])) * half.z;
                float boxRadius = Mathf.Abs(axis.x) * boxHalf.x + Mathf.Abs(axis.y) * boxHalf.y;
                if (Mathf.Abs(Vector2.Dot(delta, axis)) >= rampRadius + boxRadius) return false;
            }
            return true;
        }
    }
}
