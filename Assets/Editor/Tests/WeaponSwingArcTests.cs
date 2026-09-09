using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <see cref="WeaponViewmodel.SwingArc"/> is the fix for "the swing is a straight chord, not an
    /// arc" (ANIMATION-VFX-adjacent finding, 2026-09-07 viewmodel pass): <c>AttackCo</c>'s strike leg
    /// used to <c>Pose.Lerp</c> straight from <c>wind</c> to <c>end</c>, which draws a straight line
    /// through the strike no matter how far apart the two poses are. This pins the arc's own maths —
    /// pure and static, so no scene or coroutine is needed to check it.
    /// </summary>
    public class WeaponSwingArcTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void StationaryTrailSamples_PreserveTheArc_AndFinalContactIsExact()
        {
            var go = new GameObject("TrailSamplingTest");
            var trail = go.AddComponent<WeaponTrail>();
            if (typeof(WeaponTrail).GetField("points", Hidden).GetValue(trail) == null)
                typeof(WeaponTrail).GetMethod("Awake", Hidden).Invoke(trail, null);
            try
            {
                var record = typeof(WeaponTrail).GetMethod("RecordTip", Hidden);
                Vector3 start = new Vector3(0f, 0f, 0.5f);
                Vector3 end = new Vector3(0.2f, 0.1f, 0.5f);
                record.Invoke(trail, new object[] { start, false });
                record.Invoke(trail, new object[] { end, false });
                int count = trail.PointCount;
                var points = (Vector3[])typeof(WeaponTrail).GetField("points", Hidden).GetValue(trail);
                Vector3 oldest = points[count - 1];
                for (int i = 0; i < 20; i++)
                    Assert.IsFalse((bool)record.Invoke(trail, new object[] { end, false }));
                Assert.AreEqual(count, trail.PointCount, "a held hand must not manufacture new samples");
                Assert.AreEqual(oldest, points[count - 1], "hitstop must not replace the arc with identical head points");

                Vector3 exactContact = end + Vector3.right * (WeaponTrail.MinimumSampleDistance * 0.5f);
                Assert.IsFalse((bool)record.Invoke(trail, new object[] { exactContact, false }),
                    "sub-pixel movement accumulates instead of consuming the point budget");
                Assert.IsTrue((bool)record.Invoke(trail, new object[] { exactContact, true }));
                Assert.AreEqual(exactContact, points[0], "EndStrike must still land exactly on the blade tip");
                Assert.LessOrEqual(trail.PointCount, trail.maxPoints);
            }
            finally { DisposeTrail(trail); }
        }

        [Test]
        public void HeldTrail_DissolvesOnTheUnscaledFadeBudget_AndDisableClearsItsSibling()
        {
            var go = new GameObject("TrailLifecycleTest");
            var trail = go.AddComponent<WeaponTrail>();
            if (typeof(WeaponTrail).GetField("points", Hidden).GetValue(trail) == null)
                typeof(WeaponTrail).GetMethod("Awake", Hidden).Invoke(trail, null);
            try
            {
                trail.BeginStrike();
                var record = typeof(WeaponTrail).GetMethod("RecordTip", Hidden);
                record.Invoke(trail, new object[] { Vector3.forward, false });
                record.Invoke(trail, new object[] { Vector3.forward + Vector3.right, false });
                typeof(WeaponTrail).GetField("peak", Hidden).SetValue(trail, trail.PointCount);
                typeof(WeaponTrail).GetMethod("Draw", Hidden).Invoke(trail, null);
                var line = (LineRenderer)typeof(WeaponTrail).GetField("line", Hidden).GetValue(trail);
                Assert.IsTrue(line.enabled);
                typeof(WeaponTrail).GetMethod("FadeTail", Hidden).Invoke(trail,
                    new object[] { trail.CurrentFadeSeconds * 0.5f });
                Assert.Greater(trail.PointCount, 0);
                typeof(WeaponTrail).GetMethod("FadeTail", Hidden).Invoke(trail,
                    new object[] { trail.CurrentFadeSeconds * 0.5f });
                Assert.AreEqual(0, trail.PointCount, "a frozen swing may hold its head, but its old ribbon still expires");
                typeof(WeaponTrail).GetMethod("OnDisable", Hidden).Invoke(trail, null);
                Assert.IsFalse(line.enabled, "the camera-space sibling must not survive a disabled viewmodel");
                Assert.AreEqual(0, line.positionCount);
                Assert.IsFalse(trail.IsEmitting);
            }
            finally { DisposeTrail(trail); }
        }

        [Test]
        public void TipSampling_CachesTheHierarchyButKeepsRendererBoundsLive()
        {
            var root = new GameObject("TipSamplingCacheTest");
            var viewmodel = root.AddComponent<WeaponViewmodel>();
            var held = GameObject.CreatePrimitive(PrimitiveType.Cube);
            held.name = "HeldWeapon";
            held.transform.SetParent(root.transform, false);
            var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tip.name = "TipBlade";
            tip.transform.SetParent(held.transform, false);
            tip.transform.localPosition = new Vector3(0f, 2f, 0f);
            typeof(WeaponViewmodel).GetField("instance", Hidden).SetValue(viewmodel, held);

            try
            {
                Vector3 first = viewmodel.TipWorldPosition;
                var cached = (Renderer)typeof(WeaponViewmodel)
                    .GetField("cachedTipRenderer", Hidden).GetValue(viewmodel);
                Assert.AreSame(tip.GetComponent<Renderer>(), cached,
                    "the named business end should be resolved once for this held model");

                // Warm Unity's renderer-bounds bridge before checking the managed hot path.
                for (int i = 0; i < 8; i++) _ = viewmodel.TipWorldPosition;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) _ = viewmodel.TipWorldPosition;
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.LessOrEqual(allocated, 64L,
                    "active weapon trails must not allocate a renderer array or rescan the hierarchy per sample");

                tip.transform.localPosition += Vector3.right;
                Vector3 moved = viewmodel.TipWorldPosition;
                Assert.AreNotEqual(first, moved,
                    "the renderer is cached, but its current world-space bounds remain authoritative");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void DisposeTrail(WeaponTrail trail)
        {
            var lineInfo = typeof(WeaponTrail).GetField("line", Hidden);
            var line = (LineRenderer)lineInfo.GetValue(trail);
            var matInfo = typeof(WeaponTrail).GetField("mat", Hidden);
            var mat = (Material)matInfo.GetValue(trail);
            lineInfo.SetValue(trail, null);
            matInfo.SetValue(trail, null);
            if (line != null) Object.DestroyImmediate(line.gameObject);
            if (mat != null) Object.DestroyImmediate(mat);
            Object.DestroyImmediate(trail.gameObject);
        }

        [Test]
        public void ZeroAtBothEndpoints()
        {
            Vector3 from = new Vector3(0.1f, -0.1f, 0.4f);
            Vector3 to = new Vector3(-0.2f, 0.15f, 0.5f);
            // A tolerance, not exact equality: the bow is a half-sine, and Mathf.Sin(Mathf.PI) is
            // -8.7e-8 rather than 0 in single precision, so the k=1 endpoint lands a few nanometres
            // off the chord. NUnit's Vector3 comparison is exact (Equals, not the == epsilon), so an
            // exact assert here fails on a value that is zero for every purpose this arc has.
            Assert.AreEqual(0f, WeaponViewmodel.SwingArc(from, to, 0f).magnitude, 1e-5f);
            Assert.AreEqual(0f, WeaponViewmodel.SwingArc(from, to, 1f).magnitude, 1e-5f);
        }

        [Test]
        public void PeaksAtTheMidpointOfTheSwing()
        {
            Vector3 from = new Vector3(0.1f, -0.1f, 0.4f);
            Vector3 to = new Vector3(-0.2f, 0.15f, 0.5f);
            float mid = WeaponViewmodel.SwingArc(from, to, 0.5f).magnitude;
            float quarter = WeaponViewmodel.SwingArc(from, to, 0.25f).magnitude;
            float threeQuarter = WeaponViewmodel.SwingArc(from, to, 0.75f).magnitude;
            Assert.Greater(mid, quarter);
            Assert.Greater(mid, threeQuarter);
        }

        [Test]
        public void BowIsPerpendicularToTheChord()
        {
            Vector3 from = Vector3.zero;
            Vector3 to = new Vector3(0.3f, 0f, 0f); // a pure horizontal swing (chord along camera-right)
            Vector3 bow = WeaponViewmodel.SwingArc(from, to, 0.5f);
            Vector3 chord = (to - from).normalized;
            // Perpendicular within floating-point tolerance: the dot product is ~0.
            Assert.Less(Mathf.Abs(Vector3.Dot(bow.normalized, chord)), 0.001f);
        }

        [Test]
        public void ScalesWithTheChordLength_SoEveryWeaponBowsByTheSameProportion()
        {
            Vector3 from = Vector3.zero;
            float shortMag = WeaponViewmodel.SwingArc(from, new Vector3(0.1f, 0f, 0f), 0.5f).magnitude;
            float longMag = WeaponViewmodel.SwingArc(from, new Vector3(0.4f, 0f, 0f), 0.5f).magnitude;
            // 4x the chord length should bow ~4x as far in absolute terms, i.e. the SAME fraction.
            Assert.AreEqual(shortMag * 4f, longMag, 0.0001f);
            // And that fraction is the authored constant.
            Assert.AreEqual(0.1f * WeaponViewmodel.SwingArcOut, shortMag, 0.0001f);
        }

        [Test]
        public void DegenerateChord_ReturnsZeroRatherThanNaN()
        {
            Vector3 p = new Vector3(0.2f, 0.1f, 0.3f);
            Vector3 result = WeaponViewmodel.SwingArc(p, p, 0.5f);
            Assert.AreEqual(Vector3.zero, result);
        }

        [Test]
        public void PureThrust_StillBowsRatherThanCollapsing()
        {
            // A chord that runs straight down the view axis (a thrust) has no perpendicular in the
            // chord x forward plane — the fallback (world-up) must still produce a nonzero bow at the
            // midpoint, or a straight-forward attack would silently lose its arc.
            Vector3 from = Vector3.zero;
            Vector3 to = new Vector3(0f, 0f, 0.5f);
            Vector3 bow = WeaponViewmodel.SwingArc(from, to, 0.5f);
            Assert.Greater(bow.magnitude, 0f);
        }

        [Test]
        public void RotationLeadReachesFullBeforePositionDoes()
        {
            // The lead constant is > 1, so k * lead clamped to 1 hits 1.0 strictly before k itself does.
            float kAtRotationDone = 1f / WeaponViewmodel.SwingRotationLead;
            Assert.Less(kAtRotationDone, 1f);
            Assert.Greater(WeaponViewmodel.SwingRotationLead, 1f, "a lead of 1.0 or less would not visibly separate rotation from position");
        }
    }
}
