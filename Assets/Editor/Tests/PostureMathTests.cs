using NUnit.Framework;
using VibeGame1;

namespace VibeGame1.Tests
{
    public class PostureMathTests
    {
        [Test]
        public void Apply_BelowMax_DoesNotBreak()
        {
            var (value, broke) = PostureMath.Apply(10f, 60f, 20f);
            Assert.AreEqual(30f, value, 0.0001f);
            Assert.IsFalse(broke);
        }

        [Test]
        public void Apply_ReachingMax_Breaks()
        {
            var (value, broke) = PostureMath.Apply(50f, 60f, 10f);
            Assert.AreEqual(60f, value, 0.0001f);
            Assert.IsTrue(broke);
        }

        [Test]
        public void Apply_Overshoot_ClampsToMaxAndBreaks()
        {
            var (value, broke) = PostureMath.Apply(55f, 60f, 100f);
            Assert.AreEqual(60f, value, 0.0001f);
            Assert.IsTrue(broke);
        }

        [Test]
        public void Apply_NegativeAmount_ClampsToZeroAndDoesNotBreak()
        {
            var (value, broke) = PostureMath.Apply(5f, 60f, -50f);
            Assert.AreEqual(0f, value, 0.0001f);
            Assert.IsFalse(broke);
        }

        [Test]
        public void Apply_ZeroAmountAtMax_DoesNotBreak()
        {
            var (value, broke) = PostureMath.Apply(60f, 60f, 0f);
            Assert.AreEqual(60f, value, 0.0001f);
            Assert.IsFalse(broke);
        }

        [Test]
        public void Regen_ReducesByRateTimesDt()
        {
            float v = PostureMath.Regen(30f, 10f, 0.5f);
            Assert.AreEqual(25f, v, 0.0001f);
        }

        [Test]
        public void Regen_NeverBelowZero()
        {
            float v = PostureMath.Regen(2f, 10f, 1f);
            Assert.AreEqual(0f, v, 0.0001f);
            Assert.GreaterOrEqual(PostureMath.Regen(0f, 100f, 10f), 0f);
        }
    }
}
