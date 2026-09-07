using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE FLARE TOSS (2026-09-06, the user: "the pop up from the flare needs to be a bit higher and the
    /// player should have like 2 seconds of extra hangtime only").
    ///
    /// <para>Two things are pinned here. The HEIGHT: 18 m/s against gravity -30 is a 5.4 m rise, and
    /// because a toss now suppresses the jump cut for the length of its window, that number is what the
    /// player gets every time rather than 1.9 m when they were not holding jump. The WINDOW: it is
    /// bounded — the fall is slowed for 2 s and then it is an ordinary fall again — which is the whole
    /// difference between hangtime and a hover.</para>
    ///
    /// <para>The arithmetic is integrated here with the motor's own pure statics
    /// (<see cref="FirstPersonMotor.HangGravityScale"/>, <see cref="FirstPersonMotor.FallGravity"/>,
    /// <see cref="FirstPersonMotor.LaunchApex"/>) at a fixed step, no scene. Behaviour on a real body is
    /// the Flare_* group in FeatureTests.</para>
    /// </summary>
    public class FlareTossTests
    {
        const string PlayerPrefab = "Assets/Prefabs/Player.prefab";
        const float Eps = 1e-3f;

        static GameObject Player()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(go, PlayerPrefab + " missing — run VibeGame1/4. Build Prefabs");
            return go;
        }

        static FirstPersonMotor Motor()
        {
            var m = Player().GetComponentInChildren<FirstPersonMotor>(true);
            Assert.IsNotNull(m, "Player.prefab has no FirstPersonMotor");
            return m;
        }

        static FlareGrapple Grapple()
        {
            var g = Player().GetComponentInChildren<FlareGrapple>(true);
            Assert.IsNotNull(g, "Player.prefab has no FlareGrapple");
            return g;
        }

        // ------------------------------------------------------------------ shipped values (rule 9)

        [Test]
        public void ThePrefabCarriesTheRaisedTossAndItsHangWindow()
        {
            var g = Grapple();
            Assert.AreEqual(18f, g.tossUpSpeed, Eps, "tossUpSpeed — the raised toss");
            Assert.AreEqual(2f, g.tossHangSeconds, Eps, "tossHangSeconds — 'like 2 seconds', the user");
            var m = Motor();
            Assert.AreEqual(0.22f, m.hangGravityScale, Eps, "hangGravityScale");
            Assert.AreEqual(2.5f, m.hangSecondsCap, Eps, "hangSecondsCap");
            Assert.LessOrEqual(g.tossHangSeconds, m.hangSecondsCap,
                "the toss must fit inside the motor's cap or it would be silently trimmed");
        }

        [Test]
        public void TheBalloonFloatIsUntouchedByTheTossWork()
        {
            var m = Motor();
            Assert.AreEqual(0.45f, m.launchFloatSeconds, Eps, "launchFloatSeconds — balloons");
            Assert.AreEqual(0.55f, m.launchGravityScale, Eps, "launchGravityScale — balloons");
            Assert.AreEqual(1.6f, m.launchSteerBoost, Eps, "launchSteerBoost — balloons");
            Assert.AreEqual(9f, m.launchCarryCap, Eps, "launchCarryCap — balloons");
        }

        // ------------------------------------------------------------------ the height

        [Test]
        public void TheTossClearsTwoJumpsAndBeatsTheOldOne()
        {
            var m = Motor();
            float g = Mathf.Abs(m.gravity);
            float now = FirstPersonMotor.LaunchApex(Grapple().tossUpSpeed, g);
            float before = FirstPersonMotor.LaunchApex(14f, g);      // the shipped toss before this pass
            Assert.AreEqual(5.4f, now, 0.01f, "18 m/s against -30 is a 5.4 m rise");
            Assert.AreEqual(3.267f, before, 0.01f, "14 m/s was a nominal 3.27 m");
            Assert.Greater(now - before, 2f, "'a bit higher' is worth about a jump's height, not a metre");
            Assert.Greater(now, 2f * m.jumpHeight, "a toss must beat two jumps or the flare is not a lift");
            Assert.Less(now, 8f, "and it must not be a storey-and-a-half free: the flare is a route, not a rocket");
        }

        [Test]
        public void TheRiseIsNeverScaledSoTheApexIsExactlyWhatTheSpeedBuys()
        {
            var m = Motor();
            Assert.AreEqual(1f, FirstPersonMotor.HangGravityScale(12f, m.hangGravityScale), Eps, "rising: untouched");
            Assert.AreEqual(1f, FirstPersonMotor.HangGravityScale(0.01f, m.hangGravityScale), Eps, "still rising");
            Assert.AreEqual(m.hangGravityScale, FirstPersonMotor.HangGravityScale(-0.01f, m.hangGravityScale), Eps,
                "the moment the body falls, the hang applies");
            Assert.AreEqual(0.1f, FirstPersonMotor.HangGravityScale(-5f, 0f), Eps,
                "a zero scale is clamped: zero gravity would be a hover, which is the thing being avoided");
            Assert.AreEqual(1f, FirstPersonMotor.HangGravityScale(-5f, 4f), Eps, "and it never makes the fall heavier");

            // Integrated: the apex of a hung toss equals the apex of an unhung one.
            float apexHung = Simulate(18f, m, 2f, 0.6f).peak;
            float apexPlain = Simulate(18f, m, 0f, 0.6f).peak;
            Assert.AreEqual(apexPlain, apexHung, 0.02f, "the window slows the FALL only");
            Assert.AreEqual(FirstPersonMotor.LaunchApex(18f, Mathf.Abs(m.gravity)), apexHung, 0.06f,
                "and the closed form is the same number the integrator gets");
        }

        // ------------------------------------------------------------------ the window

        [Test]
        public void TheHangIsBoundedAndThenTheFallIsOrdinaryAgain()
        {
            var m = Motor();
            float hang = Grapple().tossHangSeconds;
            var inside = Simulate(18f, m, hang, hang);              // sampled at the closing frame
            var after = Simulate(18f, m, hang, hang + 1f);          // one second past it

            Assert.Greater(inside.y, -8f, "2 s after an 18 m/s toss the hung body is still within a storey of the toss height");
            float plainAtSameTime = Simulate(18f, m, 0f, hang).y;
            Assert.Less(plainAtSameTime, -30f, "without the hang the same 2 s is a very long way down");
            Assert.Greater(inside.y - plainAtSameTime, 25f, "the window is worth a real amount of air");

            // BOUNDED: the second past the window is fallen at the ordinary rate, not the hung one.
            float droppedAfter = inside.y - after.y;
            float vAtClose = Mathf.Abs(inside.vy);
            float ordinary = vAtClose * 1f + 0.5f * Mathf.Abs(m.gravity) * m.fallGravityMultiplier;
            Assert.AreEqual(ordinary, droppedAfter, 1.5f,
                "past the window the body falls at gravity x fallGravityMultiplier — no float survives it");
            Assert.Greater(Mathf.Abs(after.vy) - vAtClose, 30f, "and it is accelerating like a normal fall again");
        }

        [Test]
        public void TheHangIsAFloatAndNeverAGlide()
        {
            var m = Motor();
            var s = Simulate(18f, m, Grapple().tossHangSeconds, Grapple().tossHangSeconds);
            Assert.Less(s.y, s.peak, "the body is falling by the time the window closes, not sitting at apex");
            Assert.Less(s.vy, -8f, "and it is carrying real downward speed out of the window");
            Assert.Greater(s.peak - s.y, 4f, "it has visibly descended: a float, not a hover");
        }

        // ------------------------------------------------------------------ the integrator

        struct Sample { public float y, vy, peak; }

        /// <summary>
        /// The motor's vertical step, exactly as <c>FirstPersonMotor.Update</c> runs it for a tossed body:
        /// gravity scaled by the hang while falling inside the window, the fall multiplier on top, and NO
        /// jump cut (a toss is not a jump). Height is relative to the toss point.
        /// </summary>
        static Sample Simulate(float upSpeed, FirstPersonMotor m, float hangSeconds, float seconds)
        {
            const float dt = 1f / 240f;
            float y = 0f, vy = upSpeed, peak = 0f, t = 0f;
            while (t < seconds)
            {
                bool hanging = t < hangSeconds;
                float g = m.gravity * (hanging ? FirstPersonMotor.HangGravityScale(vy, m.hangGravityScale) : 1f);
                vy += FirstPersonMotor.FallGravity(g, vy, false, m.fallGravityMultiplier) * dt;
                y += vy * dt;
                if (y > peak) peak = y;
                t += dt;
            }
            var s = new Sample();
            s.y = y; s.vy = vy; s.peak = peak;
            return s;
        }
    }
}
