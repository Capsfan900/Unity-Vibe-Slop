using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE EMBER REVENANT's four wind-up silhouettes, measured rather than asserted.
    ///
    /// <para><b>Read this before adding an assertion here.</b> Eleven wind-up poses once shipped green
    /// with confident comments and ten of them made a different shape on screen than the comment
    /// claimed — see <c>docs/ENGINEERING-LOG.md</c>, "A wind-up pose cannot be computed, only
    /// photographed". A test that asserted the authored <c>armWindup</c> Eulers would have passed on
    /// every single one of those. So nothing in this file compares a Euler. Every number below comes
    /// out of <see cref="PoseSilhouette"/>, which applies the pose to a real instance of the real
    /// prefab and RASTERISES its geometry through the player's real camera — 1.60 m eye, 95° FOV, at
    /// the Revenant's own <c>preferredRange</c> of 3.40 m, on the frozen cue peak.</para>
    ///
    /// <para><b>Why the whole body and not the blade.</b> <c>FeatureTests.MeasureWindup</c> measures the
    /// BLADE's angle, which is right for the primitives. An imported forge model has empty arm pivots
    /// and a 3 cm spark marker where the blade would be (<c>MiniBossFactory.BuildModelBody</c>), so on
    /// this body that metric measures a 3 cm cube. The Revenant's anticipation is carried entirely by
    /// <c>bodyOffset</c> / <c>bodyEuler</c> through <c>LungeRoot</c>, and the body outline is therefore
    /// the thing to measure. Without the authored poses these four attacks measure IoU <b>1.00</b>
    /// against each other: literally one shape, four times.</para>
    ///
    /// <para>Rule 9: everything is read from the shipped <c>.asset</c> on disk, never from a C# default.</para>
    /// </summary>
    public class PoseSilhouetteTests
    {
        const string Slash = "Revenant_Slash";
        const string Stab = "Revenant_Stab";
        const string Overhead = "Revenant_Overhead";
        const string Kick = "Revenant_Kick";
        static readonly string[] All = { Slash, Stab, Overhead, Kick };

        // Separation thresholds, per channel: how far apart two silhouettes must be on ONE channel
        // before they read as different shapes at three frames. Lengths in body-heights.
        const float AxisSep = 25f;    // degrees of body tilt
        const float CySep = 0.18f;    // vertical centroid
        const float TopSep = 0.18f;   // crown height
        const float CxSep = 0.18f;    // horizontal centroid
        const float WSep = 0.20f;     // silhouette width
        const float HSep = 0.18f;     // silhouette height
        const float AreaSep = 0.25f;  // covered area, as a fraction of the resting body's
        /// <summary>Above this, two poses share too much of the frame to be told apart at a glance.</summary>
        const float MaxPairIoU = 0.60f;

        static EnemyAttackData Atk(string n) =>
            AssetDatabase.LoadAssetAtPath<EnemyAttackData>("Assets/Data/Attacks/" + n + ".asset");
        static GameObject Prefab() =>
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Legendary_Revenant.prefab");
        static EnemyData Data() =>
            AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("Legendary_Revenant"));

        // ---------------------------------------------------------------- the measurement

        static Dictionary<string, PoseSilhouette.Shape> cache;

        /// <summary>
        /// Stage the real prefab once per pose and measure the mask. "REST" is measured alongside them
        /// and is what every body-height in this file is relative to.
        /// </summary>
        static Dictionary<string, PoseSilhouette.Shape> Shapes()
        {
            if (cache != null) return cache;
            var prefab = Prefab();
            var data = Data();
            Assert.IsNotNull(prefab, "Legendary_Revenant.prefab missing — run VibeGame1/4b. Build Mini-Bosses");
            Assert.IsNotNull(data, "Legendary_Revenant.asset missing — run VibeGame1/3. Create Data");

            float dist = Mathf.Max(1f, data.preferredRange);
            float chest = 1.11f * Mathf.Max(0.01f, data.scale);

            var names = new string[All.Length + 1];
            var masks = new bool[All.Length + 1][];
            names[0] = "REST";
            for (int i = 0; i <= All.Length; i++)
            {
                var atk = i == 0 ? null : Atk(All[i - 1]);
                if (i > 0)
                {
                    Assert.IsNotNull(atk, All[i - 1] + ".asset missing — run VibeGame1/3. Create Data");
                    names[i] = All[i - 1];
                }
                var inst = PoseSilhouette.Stage(prefab, data);
                try
                {
                    if (atk != null) PoseSilhouette.ApplyPeak(inst, atk);
                    int filled;
                    masks[i] = PoseSilhouette.Raster(inst, dist, chest, out filled);
                    Assert.Greater(filled, 0, names[i] + " rendered nothing — the body left the frame");
                }
                finally { Object.DestroyImmediate(inst); }
            }

            var shapes = PoseSilhouette.Normalise(names, masks);
            cache = new Dictionary<string, PoseSilhouette.Shape>();
            for (int i = 0; i < shapes.Length; i++) cache[names[i]] = shapes[i];
            return cache;
        }

        static PoseSilhouette.Shape S(string n) { return Shapes()[n]; }

        static string Describe(PoseSilhouette.Shape s)
        {
            return string.Format("{0}(w {1:0.00} h {2:0.00} dCx {3:0.00} dCy {4:0.00} dTop {5:0.00} " +
                                 "axis {6:0} area {7:0.00})",
                                 s.name, s.w, s.h, s.cx, s.cy, s.top, s.axis, s.area);
        }

        /// <summary>Which channel separates two measured silhouettes, and by how many thresholds.</summary>
        static float Separation(PoseSilhouette.Shape a, PoseSilhouette.Shape b, out string channel)
        {
            float best = Mathf.Abs(a.axis - b.axis) / AxisSep; channel = "body tilt";
            float v = Mathf.Abs(a.cy - b.cy) / CySep; if (v > best) { best = v; channel = "height of centre"; }
            v = Mathf.Abs(a.top - b.top) / TopSep; if (v > best) { best = v; channel = "crown height"; }
            v = Mathf.Abs(a.cx - b.cx) / CxSep; if (v > best) { best = v; channel = "side"; }
            v = Mathf.Abs(a.w - b.w) / WSep; if (v > best) { best = v; channel = "width"; }
            v = Mathf.Abs(a.h - b.h) / HSep; if (v > best) { best = v; channel = "height"; }
            v = Mathf.Abs(a.area - b.area) / AreaSep; if (v > best) { best = v; channel = "area"; }
            return best;
        }

        // ---------------------------------------------------------------- the tests

        [Test]
        public void EveryRevenantAttack_CarriesAnAuthoredPose()
        {
            foreach (var n in All)
            {
                var p = Atk(n).windupPose;
                Assert.IsNotNull(p, n + " has no WindupPose at all");
                Assert.IsTrue(p.authored,
                    n + " falls back to the cone-derived generic pose. On an imported body that pose is " +
                    "INVISIBLE — the arm pivots are empty and the body channel is identical for every " +
                    "attack, so all four wind-ups become the same picture.");
            }
        }

        /// <summary>
        /// The four silhouettes must not rhyme. Measured two ways, because either alone can be fooled:
        /// IoU catches "the same shape in the same place" and the channel separation catches "different
        /// enough numbers to matter" — a pose can score a low IoU by jittering and still read the same.
        /// </summary>
        [Test]
        public void TheFourWindups_AreFourDifferentShapesOnScreen()
        {
            for (int i = 0; i < All.Length; i++)
                for (int j = i + 1; j < All.Length; j++)
                {
                    var a = S(All[i]);
                    var b = S(All[j]);
                    float iou = PoseSilhouette.IoU(a.mask, b.mask);
                    string channel;
                    float sep = Separation(a, b, out channel);
                    Assert.LessOrEqual(iou, MaxPairIoU, string.Format(
                        "{0} and {1} cover the same {2:P0} of the frame. {3} vs {4}",
                        All[i], All[j], iou, Describe(a), Describe(b)));
                    Assert.GreaterOrEqual(sep, 1f, string.Format(
                        "{0} vs {1} are separated only {2:0.00}x on their best channel ({3}). {4} vs {5}",
                        All[i], All[j], sep, channel, Describe(a), Describe(b)));
                }
        }

        /// <summary>Each pose must also be visibly a POSE — a body that has not moved is not a wind-up.</summary>
        [Test]
        public void EveryWindup_DepartsVisiblyFromTheRestingBody()
        {
            var rest = S("REST");
            foreach (var n in All)
            {
                var s = S(n);
                float iou = PoseSilhouette.IoU(rest.mask, s.mask);
                Assert.LessOrEqual(iou, 0.75f, string.Format(
                    "{0} still covers {1:P0} of the resting silhouette — the anticipation barely moves " +
                    "the body. {2}", n, iou, Describe(s)));
            }
        }

        /// <summary>
        /// The four reads, one per attack, each asserted on the channel it is supposed to own. These are
        /// the shapes the comments in <c>DataFactory</c> claim; this is what stops those comments from
        /// drifting into fiction the way the previous eleven did.
        /// </summary>
        [Test]
        public void TheSlash_IsTheNarrowCoiledOne()
        {
            var s = S(Slash);
            var rest = S("REST");
            Assert.Less(s.w, rest.w - 0.20f, "the 95deg cut winds the body 38deg and should read " +
                        "NARROWER than the body stands. " + Describe(s) + " vs " + Describe(rest));
            Assert.Less(Mathf.Abs(s.axis), 15f, "and level, not tilted — the tilt belongs to the kick. " + Describe(s));
        }

        [Test]
        public void TheStab_GrowsWithoutRising()
        {
            var s = S(Stab);
            Assert.GreaterOrEqual(s.h, 1.10f, "the thrust drives at the camera, so it must be the TALLEST " +
                                  "silhouette. " + Describe(s));
            Assert.GreaterOrEqual(s.area, 1.00f, "and cover more of the frame than the body at rest. " + Describe(s));
            Assert.LessOrEqual(Mathf.Abs(s.top), 0.08f, "but its crown must NOT rise — growing while the head " +
                               "stays put is what separates 'coming at you' from the overhead's 'going up'. " + Describe(s));
        }

        [Test]
        public void TheOverhead_IsTheOnlyOneThatGoesUp()
        {
            var o = S(Overhead);
            Assert.GreaterOrEqual(o.cy, 0.12f, "the big punish must LIFT. " + Describe(o));
            Assert.GreaterOrEqual(o.top, 0.10f, "and its crown with it. " + Describe(o));
            foreach (var n in new[] { Slash, Stab, Kick })
                Assert.Less(S(n).cy, o.cy - 0.12f,
                    n + " rises as much as the overhead does; the lift is the overhead's whole read. " +
                    Describe(S(n)) + " vs " + Describe(o));

            // THE LESSON FROM Heavy_Overhead, in assert form: the attack with the most damage and the
            // longest wind-up may never draw the smallest mark on screen. Rearing back foreshortens a
            // body exactly as it foreshortens a blade, and that is how a 0.95 s heavy ends up looking
            // smaller than the 0.62 s cut it is supposed to tower over.
            Assert.Greater(o.area, S(Slash).area,
                "the 0.95 s heavy covers LESS of the frame than the 0.62 s slash. " +
                Describe(o) + " vs " + Describe(S(Slash)));
        }

        [Test]
        public void TheKick_IsTheOnlyTiltedBodyAndTheLowest()
        {
            var k = S(Kick);
            Assert.GreaterOrEqual(Mathf.Abs(k.axis), 20f,
                "the unblockable is the one pose that CANTS the body. " + Describe(k));
            Assert.LessOrEqual(k.cy, -0.12f, "and sinks. " + Describe(k));
            foreach (var n in new[] { Slash, Stab, Overhead })
                Assert.Less(Mathf.Abs(S(n).axis), Mathf.Abs(k.axis) - 15f,
                    n + " tilts nearly as far as the kick does; the cant is the unblockable's read and " +
                    "getting it wrong costs the player a hit they cannot parry. " +
                    Describe(S(n)) + " vs " + Describe(k));
        }

        /// <summary>
        /// A pose describes what the body LOOKS like for a span that was already tuned. It may not move
        /// the span. These are the shipped numbers from before any pose existed; if authoring a
        /// silhouette ever changes one of them, the parry window it was calibrated against has moved.
        /// </summary>
        [Test]
        public void AuthoringPoses_MovedNoTiming()
        {
            AssertTiming(Slash, 0.62f, 0.05f, 0.16f, 0.55f, 3.4f, 95f, 20f, 0.9f);
            AssertTiming(Stab, 0.55f, 0.04f, 0.12f, 0.50f, 3.9f, 40f, 24f, 1.5f);
            AssertTiming(Overhead, 0.95f, 0.07f, 0.22f, 1.40f, 3.6f, 70f, 44f, 1.3f);   // damage 38 -> 44, 2026-09-14 deadlier pass
            AssertTiming(Kick, 0.70f, 0.05f, 0.18f, 0.90f, 3.2f, 55f, 18f, 1.1f);
            Assert.IsTrue(Atk(Kick).unblockable, "the kick is still the designated anti-turtle");
        }

        static void AssertTiming(string n, float windup, float impact, float strike, float recovery,
                                 float range, float cone, float damage, float lunge)
        {
            var a = Atk(n);
            Assert.AreEqual(windup, a.windup, 0.0001f, n + ".windup moved");
            Assert.AreEqual(impact, a.impactDelay, 0.0001f, n + ".impactDelay moved");
            Assert.AreEqual(strike, a.strikeDuration, 0.0001f, n + ".strikeDuration moved");
            Assert.AreEqual(recovery, a.recovery, 0.0001f, n + ".recovery moved");
            Assert.AreEqual(range, a.range, 0.0001f, n + ".range moved");
            Assert.AreEqual(cone, a.coneDeg, 0.0001f, n + ".coneDeg moved");
            Assert.AreEqual(damage, a.damage, 0.0001f, n + ".damage moved");
            Assert.AreEqual(lunge, a.lungeDistance, 0.0001f, n + ".lungeDistance moved");
        }

        /// <summary>
        /// <see cref="PoseSilhouette.Resolve"/> duplicates the private cone-derived fallback inside
        /// <c>EnemyVisuals.PoseFor</c>, because a measuring tool in the editor assembly cannot see it.
        /// This is what notices if the runtime fallback ever changes underneath the copy — and it is
        /// also the check that the fallback still exists at all, since 25+ attack assets ride it.
        /// </summary>
        [Test]
        public void TheConeFallback_StillMatchesTheRuntimeVocabulary()
        {
            var wide = ScriptableObject.CreateInstance<EnemyAttackData>();
            var narrow = ScriptableObject.CreateInstance<EnemyAttackData>();
            var mid = ScriptableObject.CreateInstance<EnemyAttackData>();
            try
            {
                wide.coneDeg = 110f; narrow.coneDeg = 40f; mid.coneDeg = 70f;
                Assert.IsFalse(wide.windupPose.authored, "a fresh attack must start UNauthored");
                var w = PoseSilhouette.Resolve(wide);
                var n = PoseSilhouette.Resolve(narrow);
                var m = PoseSilhouette.Resolve(mid);
                Assert.IsFalse(w.authored);
                // Three different fallback poses, and the SAME body channel in all three — which is
                // precisely why an imported body cannot ride the fallback: the only channel it can see
                // is the one the fallback never varies.
                Assert.AreNotEqual(w.armWindup, n.armWindup, "wide and narrow cones share an arm pose");
                Assert.AreNotEqual(m.armWindup, n.armWindup, "mid and narrow cones share an arm pose");
                Assert.AreEqual(w.bodyOffset, n.bodyOffset);
                Assert.AreEqual(w.bodyEuler, m.bodyEuler);
            }
            finally
            {
                Object.DestroyImmediate(wide);
                Object.DestroyImmediate(narrow);
                Object.DestroyImmediate(mid);
            }
        }
    }
}
