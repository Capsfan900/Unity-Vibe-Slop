using NUnit.Framework;
using UnityEngine;
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
    
        // ---- P2 (combat plan 2026-09-06): facing is judged against the SOURCE of the attack ----

        [Test]
        public void ASwingComesFromTheAttacker_ExactlyAsBefore()
        {
            var src = ParryMath.SourceDirection(Vector3.zero, new Vector3(3f, 2f, 4f), new Vector3(0f, 0f, 0f));
            Assert.AreEqual(new Vector3(3f, 0f, 4f), src, "zero incoming = the old flat bearing to the attacker");
        }

        [Test]
        public void ABoltComesFromAgainstItsTravel()
        {
            // Bolt flying +x (the perch is behind the player, to the LEFT of the route); it arrives from -x.
            var src = ParryMath.SourceDirection(new Vector3(1f, -0.3f, 0f), new Vector3(-50f, 0f, -50f), Vector3.zero);
            Assert.Less(src.x, -0.9f); Assert.AreEqual(0f, src.y, 1e-4f);
        }

        [Test]
        public void ARunnerPastThePerchCanStillDeflectTheBoltTheySee()
        {
            // Player faces +z, running away from a perch that sits behind-left at (-20, 0, -10). The
            // bearing test would veto (angle ~ 153 deg > 75). The bolt has been LED and is crossing in
            // from the front-left, travelling (+0.6, 0, -0.8): its source is front-left, inside the cone.
            Vector3 fwd = Vector3.forward;
            Vector3 bearing = ParryMath.SourceDirection(Vector3.zero, new Vector3(-20f, 0f, -10f), Vector3.zero);
            Assert.IsFalse(ParryMath.IsFacing(fwd, bearing, 75f), "the old rule vetoes a perch you have passed");
            Vector3 bolt = ParryMath.SourceDirection(new Vector3(0.6f, 0f, -0.8f), new Vector3(-20f, 0f, -10f), Vector3.zero);
            Assert.IsTrue(ParryMath.IsFacing(fwd, bolt, 75f), "but the bolt itself is in front of you");
        }

        [Test]
        public void ABoltGenuinelyFromBehindIsStillNotParriable()
        {
            Vector3 bolt = ParryMath.SourceDirection(new Vector3(0f, 0f, 1f), Vector3.zero, Vector3.zero);   // travelling +z, from -z
            Assert.IsFalse(ParryMath.IsFacing(Vector3.forward, bolt, 75f));
            Assert.IsTrue(ParryMath.IsFacing(Vector3.forward, Vector3.zero, 75f), "on top of you counts as facing, never a NaN veto");
        }
}
}
