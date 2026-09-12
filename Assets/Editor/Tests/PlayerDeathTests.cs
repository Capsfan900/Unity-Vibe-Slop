using NUnit.Framework;

namespace VibeGame1.Tests
{
    public class PlayerDeathTests
    {
        [Test]
        public void DownhillJumpAboveRouteSurfaceIsNotAVoidDeath()
        {
            Assert.IsFalse(PlayerDeath.ShouldTriggerVoidFall(20f, 35f, 9f, false, true),
                "descending farther than the threshold is legal while a ramp remains underneath");
        }

        [Test]
        public void UnsupportedFallPastThresholdStillDies()
        {
            Assert.IsTrue(PlayerDeath.ShouldTriggerVoidFall(20f, 35f, 9f, false, false));
            Assert.IsFalse(PlayerDeath.ShouldTriggerVoidFall(27f, 35f, 9f, false, false));
            Assert.IsFalse(PlayerDeath.ShouldTriggerVoidFall(20f, 35f, 9f, true, false));
        }
    }
}
