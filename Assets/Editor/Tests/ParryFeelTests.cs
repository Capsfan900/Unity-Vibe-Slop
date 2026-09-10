using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Pure contracts for the physical weapon confirmation on a successful deflect. These assertions do
    /// not replace a playtest; they prevent the one authored beat from silently becoming a guard thud,
    /// a teleport to idle, or a timing change to the parry system.
    /// </summary>
    public class ParryFeelTests
    {
        [Test]
        public void DeflectKickUsesTheAuthoredPushHoldAndRecoveryBudget()
        {
            Assert.AreEqual(0.03f, WeaponViewmodel.DeflectKickPush, 1e-5f);
            Assert.AreEqual(0.02f, WeaponViewmodel.DeflectKickHold, 1e-5f);
            Assert.AreEqual(0.10f, WeaponViewmodel.DeflectKickRecover, 1e-5f);
            Assert.AreEqual(0.15f, WeaponViewmodel.DeflectKickTotal, 1e-5f);
            Assert.Less(WeaponViewmodel.DeflectKickTotal, 0.25f,
                "the recoil must be over before the normal parry flick/stance beat would become a second window");
        }

        [Test]
        public void DeflectKickBeginsAtTheLiveTapOrHoldPoseAndReturnsThere()
        {
            Pose tap = new Pose(new Vector3(0.12f, -0.18f, 0.52f), new Vector3(-11f, 24f, 8f));
            Pose hold = new Pose(new Vector3(0.34f, -0.10f, 0.62f), new Vector3(-34f, 17f, 56f));

            Pose idle = new Pose(new Vector3(0.28f, -0.22f, 0.62f), new Vector3(-8f, 6f, 0f));
            Pose guard = new Pose(new Vector3(0.38f, -0.06f, 0.56f), new Vector3(-35f, 18f, 58f));
            AssertPose(tap, WeaponViewmodel.DeflectKickPose(tap, idle, 0f), "tap starts from its actual live pose");
            AssertPose(hold, WeaponViewmodel.DeflectKickPose(hold, guard, 0f), "hold starts from its actual live pose");
            AssertPose(idle, WeaponViewmodel.DeflectKickPose(tap, idle, WeaponViewmodel.DeflectKickTotal),
                "tap completes its recovery at the requested idle pose");
            AssertPose(guard, WeaponViewmodel.DeflectKickPose(hold, guard, WeaponViewmodel.DeflectKickTotal),
                "hold completes its recovery at the requested guard pose");
        }

        [Test]
        public void DeflectKickHoldsADownAndOutContactPose()
        {
            Pose live = new Pose(new Vector3(0.18f, -0.15f, 0.55f), new Vector3(-16f, 20f, 24f));
            Pose impact = WeaponViewmodel.DeflectKickPose(live, live, WeaponViewmodel.DeflectKickPush + 0.01f);
            Pose expected = new Pose(live.pos + WeaponViewmodel.DeflectKickOffset.pos,
                                     live.euler + WeaponViewmodel.DeflectKickOffset.euler);
            AssertPose(expected, impact, "the 20 ms contact hold must preserve the authored impact pose");
            Assert.Greater(impact.pos.x, live.pos.x, "outward means farther into the lower-right weapon lane");
            Assert.Less(impact.pos.y, live.pos.y, "impact drops the weapon rather than raising it into the enemy");
            Assert.Less(impact.pos.z, live.pos.z, "impact stays shallow so it cannot magnify toward the lens");
        }

        static void AssertPose(Pose expected, Pose actual, string message)
        {
            Assert.AreEqual(expected.pos.x, actual.pos.x, 1e-4f, message + " (x)");
            Assert.AreEqual(expected.pos.y, actual.pos.y, 1e-4f, message + " (y)");
            Assert.AreEqual(expected.pos.z, actual.pos.z, 1e-4f, message + " (z)");
            Assert.Less(Quaternion.Angle(Quaternion.Euler(expected.euler), Quaternion.Euler(actual.euler)), 0.01f,
                message + " (rotation)");
        }
    }
}
