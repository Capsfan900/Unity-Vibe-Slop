using NUnit.Framework;
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
        [Test]
        public void ZeroAtBothEndpoints()
        {
            Vector3 from = new Vector3(0.1f, -0.1f, 0.4f);
            Vector3 to = new Vector3(-0.2f, 0.15f, 0.5f);
            Assert.AreEqual(Vector3.zero, WeaponViewmodel.SwingArc(from, to, 0f));
            Assert.AreEqual(Vector3.zero, WeaponViewmodel.SwingArc(from, to, 1f));
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
