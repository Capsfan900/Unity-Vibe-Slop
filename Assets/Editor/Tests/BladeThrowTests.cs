using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.Tests
{
    public class BladeThrowTests
    {
        const string AssetPath = "Assets/Data/Items/BladeThrow.asset";

        [Test]
        public void PositionFollowsTheArc()
        {
            Vector3 p = BladeMath.Position(Vector3.zero, new Vector3(0f, 0f, 24f), 8f, 1f);
            Assert.AreEqual(24f, p.z, 1e-4f);
            Assert.AreEqual(-4f, p.y, 1e-4f);
        }

        [Test]
        public void RecallableInFlightAndWhileLodgedOnly()
        {
            Assert.IsTrue(BladeMath.Recallable(false, false, 0.01f, 1.1f, 0f, 3.5f), "any point in flight");
            Assert.IsTrue(BladeMath.Recallable(false, false, 1.09f, 1.1f, 0f, 3.5f));
            Assert.IsFalse(BladeMath.Recallable(false, false, 1.1f, 1.1f, 0f, 3.5f), "flight over");
            Assert.IsTrue(BladeMath.Recallable(false, true, 5f, 1.1f, 3.4f, 3.5f), "lodged window");
            Assert.IsFalse(BladeMath.Recallable(false, true, 5f, 1.1f, 3.5f, 3.5f));
            Assert.IsFalse(BladeMath.Recallable(true, false, 0.1f, 1.1f, 0f, 3.5f), "spent");
        }

        [Test]
        public void PullTargetNeverEndsInsideTheSurface()
        {
            Vector3 wall = new Vector3(0f, 5f, 10f);
            Vector3 onWall = BladeMath.PullTarget(wall, Vector3.back, true, 0.6f);
            Assert.Less(onWall.z, wall.z - 0.5f, "stands off a wall along its normal");
            Vector3 onFloor = BladeMath.PullTarget(wall, Vector3.up, true, 0.6f);
            Assert.GreaterOrEqual(onFloor.y, wall.y, "stands on a floor lodge");
            Vector3 inAir = BladeMath.PullTarget(wall, Vector3.zero, false, 0.6f);
            Assert.AreEqual(wall.y - 1f, inAir.y, 1e-4f, "in flight the chest meets the blade");
        }

        [Test]
        public void SpinStaysInRange()
        {
            float a = BladeMath.SpinAngle(10.3f, 1080f);
            Assert.GreaterOrEqual(a, 0f);
            Assert.Less(a, 360f);
        }

        [Test]
        public void ShippedBladeThrowStaysInsideTheHookEnvelope()
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetPath);
            Assert.IsNotNull(item, AssetPath + " missing - run VibeGame1/3. Create Data");
            Assert.AreEqual(ItemEffect.BladeThrow, item.effect);
            Assert.LessOrEqual(BladeMath.MaxReach(item.bladeSpeed, item.bladeFlightSeconds), 28f,
                "the throw must not out-reach the Hook's 28 m, which Level_01 is authored around");
            Assert.LessOrEqual(item.bladeRecallRange, 30f);
            Assert.AreEqual(0.35f, item.bladePullSeconds, 1e-4f);
            Assert.Greater(item.bladeLodgeSeconds, 0f);
        }
    }
}
