using NUnit.Framework;
using UnityEngine;

namespace VibeGame1.Tests
{
    /// <summary>Pure geometry plus bounded-runtime checks for the fire slash's presentation contract.</summary>
    public class FireSlashFxTests
    {
        static readonly Vector3 Origin = new Vector3(3f, 1.4f, -2f);

        [Test]
        public void ArcEndpoints_AreTheSuppliedRadiusAndSymmetricAroundForward()
        {
            var g = FireSlashFx.CreateGeometry(Origin, Vector3.forward, 6f, 100f);
            Vector3 left = g.Point(0f) - Origin;
            Vector3 right = g.Point(1f) - Origin;
            Assert.AreEqual(6f, left.magnitude, 1e-4f);
            Assert.AreEqual(6f, right.magnitude, 1e-4f);
            Assert.AreEqual(-50f, Vector3.SignedAngle(Vector3.forward, left, Vector3.up), 1e-4f);
            Assert.AreEqual(50f, Vector3.SignedAngle(Vector3.forward, right, Vector3.up), 1e-4f);
            Assert.AreEqual(Origin.y, g.Point(0f).y, 1e-4f, "ends stay on the actual attack plane");
            Assert.AreEqual(Origin.y, g.Point(1f).y, 1e-4f, "ends stay on the actual attack plane");
        }

        [Test]
        public void Progress_TravelsMonotonicallyAcrossTheProvidedArc()
        {
            var g = FireSlashFx.CreateGeometry(Origin, Vector3.forward, 4f, 120f);
            Vector3 quarter = g.Point(0.25f) - Origin;
            Vector3 middle = g.Point(0.5f) - Origin;
            Vector3 threeQuarter = g.Point(0.75f) - Origin;
            quarter.y = middle.y = threeQuarter.y = 0f;
            Assert.Less(Vector3.SignedAngle(Vector3.forward, quarter, Vector3.up), 0f);
            Assert.AreEqual(0f, Vector3.SignedAngle(Vector3.forward, middle, Vector3.up), 1e-4f);
            Assert.Greater(Vector3.SignedAngle(Vector3.forward, threeQuarter, Vector3.up), 0f);
            Assert.Greater(g.Point(0.5f).y, Origin.y, "the mid-arc has the small first-person readability lift");
        }

        [Test]
        public void Lifetime_IsBoundedForVisualReadabilityAndLoad()
        {
            Assert.AreEqual(FireSlashFx.MinSeconds, FireSlashFx.Lifetime(-4f), 1e-5f);
            Assert.AreEqual(FireSlashFx.MaxSeconds, FireSlashFx.Lifetime(99f), 1e-5f);
            Assert.AreEqual(FireSlashFx.DefaultSeconds, FireSlashFx.Lifetime(FireSlashFx.DefaultSeconds), 1e-5f);
        }

        [Test]
        public void FireSlash_StaysInsideTheBloomBudget()
        {
            Assert.LessOrEqual(FireSlashFx.PeakChannel, 1.05f);
            Assert.AreEqual(1.05f, FireSlashFx.PeakChannel, 1e-5f,
                "this is the deliberately minimal hot-edge exception, never an unbounded HDR wash");

            FireSlashFx fx = FireSlashFx.Play(Origin, Vector3.forward, Color.magenta * 20f, 5f, 120f);
            try
            {
                foreach (var line in fx.GetComponentsInChildren<LineRenderer>(true))
                {
                    Color color = line.sharedMaterial.GetColor("_BaseColor");
                    float peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
                    Assert.LessOrEqual(peak, FireSlashFx.PeakChannel + 1e-4f,
                        "a hostile HDR input must not turn a Pyre payoff into a screen wash");
                }
            }
            finally { if (fx != null) fx.ReleaseForTests(); }
        }

        [Test]
        public void DegenerateInputs_ProduceFiniteVisibleGeometry()
        {
            var g = FireSlashFx.CreateGeometry(new Vector3(float.NaN, 0f, 0f), Vector3.zero, -2f, -90f);
            // The important contract is that a corrupt input is downgraded to a harmless, finite visual rather than
            // writing NaN values into a LineRenderer.
            var sane = FireSlashFx.CreateGeometry(Origin, Vector3.zero, -2f, -90f);
            Assert.AreEqual(Vector3.forward, sane.forward);
            Assert.AreEqual(2f, sane.radius, 1e-5f);
            Assert.AreEqual(90f, sane.arcDegrees, 1e-5f);
            Vector3 point = sane.Point(0.5f);
            Assert.IsFalse(float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z));
            Assert.IsFalse(float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z));
            Vector3 repaired = g.Point(0.5f);
            Assert.IsFalse(float.IsNaN(repaired.x) || float.IsNaN(repaired.y) || float.IsNaN(repaired.z));
        }

        [Test]
        public void PooledSlash_ReleasesItsBudgetOnExplicitCleanup()
        {
            int before = FireSlashFx.ActiveCount;
            FireSlashFx fx = FireSlashFx.Play(Origin, Vector3.forward, Color.cyan, 5f, 120f, 0.5f, 0.2f);
            Assert.IsNotNull(fx);
            try
            {
                Assert.AreEqual(before + 1, FireSlashFx.ActiveCount);
                bool hasCachedFringe = false;
                foreach (var line in fx.GetComponentsInChildren<LineRenderer>(true))
                    hasCachedFringe |= line.positionCount == FireSlashFx.ArcPoints;
                Assert.IsTrue(hasCachedFringe,
                    "constructed geometry is cached in renderer buffers, never allocated every frame");
            }
            finally
            {
                if (fx != null) fx.ReleaseForTests();
            }
            Assert.AreEqual(before, FireSlashFx.ActiveCount);
        }
    }
}
