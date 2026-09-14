using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.Tests
{
    public class PuppetLocomotionTests
    {
        [Test]
        public void RunHasADeadBandSoAStepInSpeedDoesNotFlicker()
        {
            // V18's recovery step-in sat on the old single 2.6 threshold and flipped Walk/Run every frame.
            Assert.AreEqual(PuppetLocomotion.Walk, PuppetLocomotion.Choose(2.6f, PuppetLocomotion.Walk));
            Assert.AreEqual(PuppetLocomotion.Run, PuppetLocomotion.Choose(2.6f, PuppetLocomotion.Run));
            Assert.AreEqual(PuppetLocomotion.Run, PuppetLocomotion.Choose(3.0f, PuppetLocomotion.Walk));
            Assert.AreEqual(PuppetLocomotion.Walk, PuppetLocomotion.Choose(2.2f, PuppetLocomotion.Run));
            Assert.AreEqual(PuppetLocomotion.Idle, PuppetLocomotion.Choose(0.1f, PuppetLocomotion.Run));
            Assert.AreEqual(PuppetLocomotion.Idle, PuppetLocomotion.Choose(0.4f, PuppetLocomotion.Idle));
            Assert.AreEqual(PuppetLocomotion.Walk, PuppetLocomotion.Choose(0.3f, PuppetLocomotion.Walk));
        }

        [Test]
        public void RateMatchesStrideWithinClamp()
        {
            Assert.AreEqual(1f, PuppetLocomotion.Rate(4f, 0f), 1e-4f, "unmeasured stride keeps the authored rate");
            Assert.AreEqual(1.25f, PuppetLocomotion.Rate(4.5f, 3.6f), 1e-3f);
            Assert.AreEqual(PuppetLocomotion.MaxRate, PuppetLocomotion.Rate(20f, 2f), 1e-4f);
            Assert.AreEqual(PuppetLocomotion.MinRate, PuppetLocomotion.Rate(0.1f, 3f), 1e-4f);
        }

        [Test]
        public void FootfallsAtZeroAndHalf()
        {
            Assert.IsTrue(PuppetLocomotion.CrossedFootfall(0.45f, 0.55f));
            Assert.IsTrue(PuppetLocomotion.CrossedFootfall(0.95f, 1.05f));
            Assert.IsFalse(PuppetLocomotion.CrossedFootfall(0.1f, 0.3f));
            Assert.IsTrue(PuppetLocomotion.CrossedFootfall(0.8f, 0.1f), "a restart counts");
        }

        [TestCase("Legendary_FlurryBrawlerV18")]
        [TestCase("Legendary_CinderJudge")]
        public void ShippedForgeBodiesHaveMeasuredStridesAndDust(string prefab)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + prefab + ".prefab");
            Assert.IsNotNull(go, prefab);
            var pv = go.GetComponentInChildren<PuppetVisuals>(true);
            Assert.IsNotNull(pv, prefab + " PuppetVisuals");
            Assert.Greater(pv.walkStrideSpeed, 0.5f, "walk stride measured on the imported clip");
            Assert.Greater(pv.runStrideSpeed, pv.walkStrideSpeed, "run stride covers more ground than walk");
            Assert.Greater(pv.footstepDust.a, 0f, "footfall dust enabled");
        }
    }
}
