using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <see cref="MovementPose"/> is a pure function (no MonoBehaviour, no Update, no singleton), so it
    /// is pinned directly rather than through a scene: build a <see cref="MovementPose.State"/> by hand
    /// and read the <see cref="Pose"/> it returns.
    ///
    /// <para>Two things every assertion below is defending, per ANIMATION-VFX section 4: the offset
    /// must be SECONDARY motion (never as loud as a swing arc or the guard stance), and it must be
    /// ADDITIVE-safe — zero state in, zero offset out, so a caller that never calls this behaves exactly
    /// as it did before the file existed.</para>
    /// </summary>
    public class MovementPoseTests
    {
        static MovementPose.State Grounded() => new MovementPose.State { grounded = true };

        [Test]
        public void GroundedIdle_IsZero()
        {
            Pose p = MovementPose.Compute(Grounded());
            Assert.AreEqual(Vector3.zero, p.pos);
            Assert.AreEqual(Vector3.zero, p.euler);
        }

        [Test]
        public void Falling_DropsAndPullsBackButNeverCompetesWithAPose()
        {
            var s = new MovementPose.State { grounded = false, verticalVelocity = -20f }; // well past the reference fall speed
            Pose p = MovementPose.Compute(s);
            Assert.Less(p.pos.y, 0f, "falling should sink the hands, not lift them");
            Assert.Less(p.pos.z, 0f, "falling should pull the hands back toward the chest");
            // Secondary motion: nowhere near the guard blend (0.07 m) or a swing (tens of centimetres).
            Assert.Less(p.pos.magnitude, 0.08f);
        }

        [Test]
        public void Rising_TiltsTheOppositeWayFromFalling()
        {
            var falling = MovementPose.Compute(new MovementPose.State { grounded = false, verticalVelocity = -20f });
            var rising = MovementPose.Compute(new MovementPose.State { grounded = false, verticalVelocity = 20f });
            Assert.AreEqual(0f, rising.pos.y, 0.0001f, "rising should not sink the hands");
            Assert.AreNotEqual(0f, rising.euler.x);
            // Opposite sign from the falling case's tilt — one direction lifts, the other does not.
            Assert.Less(falling.euler.x * rising.euler.x, 0f);
        }

        [Test]
        public void GroundedNeverGetsTheAirborneTerm()
        {
            // Falling fast but the caller says grounded (e.g. a hard landing frame) — the term must not
            // fire, or a landing dip would double up with PlayerFeedback's own landing dip.
            var s = new MovementPose.State { grounded = true, verticalVelocity = -25f };
            Pose p = MovementPose.Compute(s);
            Assert.AreEqual(Vector3.zero, p.pos);
        }

        [Test]
        public void WallRun_LeansTowardTheWallAndRollsWithIt()
        {
            // Wall's normal points toward camera-LEFT (-x): the player is running along a wall on their
            // right, and should brace/lean toward it, i.e. camera-right (+x).
            var s = new MovementPose.State { grounded = false, wallRunning = true, wallNormalLocal = new Vector3(-1f, 0f, 0f) };
            Pose p = MovementPose.Compute(s);
            Assert.Greater(p.pos.x, 0f);
            Assert.Greater(p.euler.z, 0f);

            // Mirrored wall: everything flips sign.
            var mirrored = MovementPose.Compute(new MovementPose.State { grounded = false, wallRunning = true, wallNormalLocal = new Vector3(1f, 0f, 0f) });
            Assert.Less(mirrored.pos.x, 0f);
            Assert.Less(mirrored.euler.z, 0f);
        }

        [Test]
        public void WallRun_SuppressesTheAirborneTerm()
        {
            // A wall run always starts in the air (falling into the wall), but the wall-run term should
            // be the only one that fires — otherwise the hands would sink AND brace at once, muddying
            // which state the player is actually reading.
            var s = new MovementPose.State
            {
                grounded = false,
                wallRunning = true,
                wallNormalLocal = new Vector3(-1f, 0f, 0f),
                verticalVelocity = -12f,
            };
            Pose p = MovementPose.Compute(s);
            Assert.AreEqual(0f, p.pos.y, 0.0001f, "the airborne sink must not stack under a wall run");
        }

        [Test]
        public void Sliding_DropsLeansForwardAndPitchesDown()
        {
            var p = MovementPose.Compute(new MovementPose.State { grounded = false, sliding = true });
            Assert.Less(p.pos.y, 0f);
            Assert.Greater(p.pos.z, 0f);
            Assert.Greater(p.euler.x, 0f);
        }

        [Test]
        public void Dashing_PullsHandsBack()
        {
            var p = MovementPose.Compute(new MovementPose.State { grounded = true, dashing = true });
            Assert.Less(p.pos.z, 0f);
            Assert.Greater(p.euler.x, 0f);
        }

        [Test]
        public void EverythingStacked_StillStaysUnderTheHardCeiling()
        {
            // Not a state the motor can actually produce simultaneously (sliding + wall-running +
            // dashing are mutually exclusive on the motor), but the function must still be safe if a
            // caller ever manages to combine them — this is the ceiling that protects that case.
            var s = new MovementPose.State
            {
                grounded = false,
                sliding = true,
                wallRunning = true,
                dashing = true,
                wallNormalLocal = new Vector3(-1f, 0f, 0f),
                verticalVelocity = -30f,
            };
            Pose p = MovementPose.Compute(s);
            Assert.LessOrEqual(p.pos.magnitude, 0.141f, "hard ceiling from MovementPose.MaxPosMagnitude");
            Assert.LessOrEqual(Mathf.Abs(p.euler.x), 20.01f);
            Assert.LessOrEqual(Mathf.Abs(p.euler.y), 20.01f);
            Assert.LessOrEqual(Mathf.Abs(p.euler.z), 20.01f);
        }
    }
}
