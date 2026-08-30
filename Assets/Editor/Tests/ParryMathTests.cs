using NUnit.Framework;
using VibeGame1;

namespace VibeGame1.Tests
{
    public class ParryMathTests
    {
        const float Perfect = 0.15f;
        const float Late = 0.20f;

        static ParryResult Eval(float elapsed, bool facing = true, bool unblockable = false)
            => ParryMath.Evaluate(elapsed, Perfect, Late, facing, unblockable);

        [Test] public void JustInsidePerfectWindow_IsPerfect() => Assert.AreEqual(ParryResult.Perfect, Eval(0.149f));
        [Test] public void AtPerfectEdge_IsPerfect() => Assert.AreEqual(ParryResult.Perfect, Eval(0.15f));
        [Test] public void JustPastPerfect_IsBlocked() => Assert.AreEqual(ParryResult.Blocked, Eval(0.151f));
        [Test] public void AtLateEdge_IsBlocked() => Assert.AreEqual(ParryResult.Blocked, Eval(0.35f));
        [Test] public void JustPastLate_IsHit() => Assert.AreEqual(ParryResult.Hit, Eval(0.351f));
        [Test] public void NegativeElapsed_IsHit() => Assert.AreEqual(ParryResult.Hit, Eval(-0.01f));
        [Test] public void NotFacing_IsHit() => Assert.AreEqual(ParryResult.Hit, Eval(0.05f, facing: false));
        [Test] public void Unblockable_IsHit() => Assert.AreEqual(ParryResult.Hit, Eval(0.05f, unblockable: true));
        [Test] public void ZeroElapsed_IsPerfect() => Assert.AreEqual(ParryResult.Perfect, Eval(0f));
    }
}
