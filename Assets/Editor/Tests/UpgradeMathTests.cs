using NUnit.Framework;
using VibeGame1;

namespace VibeGame1.Tests
{
    public class UpgradeMathTests
    {
        [Test] public void LevelZero_CostsBase() => Assert.AreEqual(100, UpgradeMath.Cost(0, 100, 1.22f));
        [Test] public void LevelOne_CostsBaseTimesGrowth() => Assert.AreEqual(122, UpgradeMath.Cost(1, 100, 1.22f));
        [Test] public void NegativeLevel_ClampsToBase() => Assert.AreEqual(100, UpgradeMath.Cost(-3, 100, 1.22f));

        [Test]
        public void Cost_IsMonotonicIncreasing()
        {
            int previous = UpgradeMath.Cost(0, 100, 1.22f);
            for (int level = 1; level <= 20; level++)
            {
                int cost = UpgradeMath.Cost(level, 100, 1.22f);
                Assert.Greater(cost, previous, $"Cost at level {level} should exceed level {level - 1}");
                previous = cost;
            }
        }
    }
}
