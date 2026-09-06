using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// PERFECT timing — a wall jump on the wall's last breath, a jump thrown out of a dash, a grapple
    /// burst on the landing — gives stamina back. The laws live in <see cref="PerfectMath"/> as pure
    /// functions so they can be driven here without a scene, and the shipped numbers live on
    /// <c>Player.prefab</c> / <c>GameFeel.asset</c> (hard rule 9), read back below.
    ///
    /// <para>docs/MOVEMENT-PRINCIPLES.md rule 8 (consistency) is the one with teeth here:
    /// <see cref="EveryWindowIsTheSameWidthAt20_60_240Fps"/> steps each law on three frame clocks and
    /// holds the accepted duration to the authored window within one frame, so a perfect is the same
    /// width of TIME on every machine and never a frame count.</para>
    /// </summary>
    public class PerfectTimingTests
    {
        static GameObject Player() => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        static FirstPersonMotor Motor() => Player() != null ? Player().GetComponent<FirstPersonMotor>() : null;

        // ---------------------------------------------------------------- wall jump

        [Test]
        public void WallJumpFromRun_IsPerfectOnlyInTheLastBreathOfTheLoan()
        {
            const float loan = 1.75f, window = 0.14f;
            Assert.IsFalse(PerfectMath.WallJumpFromRunIsPerfect(0.05f, loan, window), "the instant you attach is ordinary");
            Assert.IsFalse(PerfectMath.WallJumpFromRunIsPerfect(1.0f, loan, window), "the middle of the run is ordinary");
            Assert.IsFalse(PerfectMath.WallJumpFromRunIsPerfect(loan - window - 0.001f, loan, window), "just before the window");
            Assert.IsTrue(PerfectMath.WallJumpFromRunIsPerfect(loan - window, loan, window), "the window's opening edge");
            Assert.IsTrue(PerfectMath.WallJumpFromRunIsPerfect(loan - 0.01f, loan, window), "the last frame of the run");
            Assert.IsTrue(PerfectMath.WallJumpFromRunIsPerfect(loan, loan, window), "the loan's own end");
            Assert.IsFalse(PerfectMath.WallJumpFromRunIsPerfect(1.0f, 0f, window), "a zero loan can never be perfect");
            Assert.IsFalse(PerfectMath.WallJumpFromRunIsPerfect(1.7f, loan, 0f), "a zero window can never be perfect");
        }

        [Test]
        public void GraceJump_IsPerfectInsideTheWindowAfterTheLetGo()
        {
            const float window = 0.14f;
            Assert.IsTrue(PerfectMath.GraceJumpIsPerfect(0f, window), "the very frame the wall let go");
            Assert.IsTrue(PerfectMath.GraceJumpIsPerfect(window, window), "the closing edge");
            Assert.IsFalse(PerfectMath.GraceJumpIsPerfect(window + 0.001f, window), "past the window");
            Assert.IsFalse(PerfectMath.GraceJumpIsPerfect(-0.01f, window), "a negative time is a stale clock, never a perfect");
        }

        // ---------------------------------------------------------------- dash-jump

        [Test]
        public void DashJump_NeverCountsOnTheSameFrameAsTheDash()
        {
            const float min = 0.04f, window = 0.12f;
            Assert.IsFalse(PerfectMath.DashJumpIsPerfect(0f, min, window), "mashing dash+jump together is not a timing");
            Assert.IsFalse(PerfectMath.DashJumpIsPerfect(min - 0.001f, min, window), "just under the minimum delay");
            Assert.IsTrue(PerfectMath.DashJumpIsPerfect(min, min, window), "the minimum delay itself");
            Assert.IsTrue(PerfectMath.DashJumpIsPerfect(0.10f, min, window), "mid-window");
            Assert.IsTrue(PerfectMath.DashJumpIsPerfect(min + window, min, window), "the closing edge");
            Assert.IsFalse(PerfectMath.DashJumpIsPerfect(min + window + 0.001f, min, window), "past the dash");
            Assert.IsFalse(PerfectMath.DashJumpIsPerfect(-1f, min, window), "no dash in flight (stale clock)");
        }

        [Test]
        public void TheDashJumpWindowLiesInsideTheDashItself()
        {
            // The perfect is 'jump OUT of the dash'. If the window outran the dash, a jump after the dash
            // had already ended would count, which is a different (and easier) move.
            var m = Motor();
            if (m == null) Assert.Ignore("Player.prefab not built yet — run VibeGame1/4. Build Prefabs.");
            Assert.LessOrEqual(m.perfectDashJumpMinDelay + m.perfectDashJumpWindow, m.dashDuration + 1e-4f,
                "window ends " + (m.perfectDashJumpMinDelay + m.perfectDashJumpWindow) + "s after the dash fired, " +
                "but the dash lasts " + m.dashDuration + "s.");
            Assert.Greater(m.perfectDashJumpMinDelay, 0.02f,
                "a minimum delay under two frames at 60 fps lets a mashed dash+jump count.");
        }

        // ---------------------------------------------------------------- burst

        [Test]
        public void Burst_IsPerfectOnlyInTheFirstPartOfItsWindow()
        {
            const float window = 0.12f;
            Assert.IsTrue(PerfectMath.BurstIsPerfect(0f, window));
            Assert.IsTrue(PerfectMath.BurstIsPerfect(window, window));
            Assert.IsFalse(PerfectMath.BurstIsPerfect(0.2f, window), "the back of the 0.30 s burst window is ordinary");
            Assert.IsFalse(PerfectMath.BurstIsPerfect(-0.01f, window));
        }

        [Test]
        public void ThePerfectBurstWindowIsShorterThanTheBurstWindow()
        {
            var m = Motor();
            if (m == null) Assert.Ignore("Player.prefab not built yet — run VibeGame1/4. Build Prefabs.");
            Assert.Less(m.perfectBurstWindow, m.pullBurstWindow,
                "a perfect window as wide as the burst window makes every burst perfect.");
        }

        // ---------------------------------------------------------------- refund

        [Test]
        public void Refund_NeverOverfillsAndNeverDebits()
        {
            Assert.AreEqual(100f, PerfectMath.Refund(90f, 100f, 30f), 1e-4f, "clamped to the bar");
            Assert.AreEqual(60f, PerfectMath.Refund(30f, 100f, 30f), 1e-4f);
            Assert.AreEqual(30f, PerfectMath.Refund(30f, 100f, 0f), 1e-4f, "zero adds nothing");
            Assert.AreEqual(30f, PerfectMath.Refund(30f, 100f, -20f), 1e-4f, "a negative refund is never a debit");
            Assert.AreEqual(100f, PerfectMath.Refund(100f, 100f, 30f), 1e-4f, "a full bar stays full");
        }

        // ---------------------------------------------------------------- consistency (rule 8)

        [Test]
        public void EveryWindowIsTheSameWidthAt20_60_240Fps()
        {
            // Step each law on three frame clocks and measure how much TIME accepts a press. The
            // accepted span must equal the authored window to within one frame of that clock — a
            // window that grew or shrank with the frame rate would be a different mechanic on a
            // different machine (docs/MOVEMENT-PRINCIPLES.md rule 8).
            const float loan = 1.75f, wallWindow = 0.14f, minDelay = 0.04f, dashWindow = 0.12f, burstWindow = 0.12f;
            foreach (float fps in new[] { 20f, 60f, 240f })
            {
                float dt = 1f / fps;
                float wall = 0f, grace = 0f, dash = 0f, burst = 0f;
                for (float t = 0f; t <= 2.5f; t += dt)
                {
                    if (PerfectMath.WallJumpFromRunIsPerfect(t, loan, wallWindow)) wall += dt;
                    if (PerfectMath.GraceJumpIsPerfect(t, wallWindow)) grace += dt;
                    if (PerfectMath.DashJumpIsPerfect(t, minDelay, dashWindow)) dash += dt;
                    if (PerfectMath.BurstIsPerfect(t, burstWindow)) burst += dt;
                }
                Assert.AreEqual(wallWindow, wall, dt + 1e-3f, "wall-jump window at " + fps + " fps");
                Assert.AreEqual(wallWindow, grace, dt + 1e-3f, "grace window at " + fps + " fps");
                Assert.AreEqual(dashWindow, dash, dt + 1e-3f, "dash-jump window at " + fps + " fps");
                Assert.AreEqual(burstWindow, burst, dt + 1e-3f, "burst window at " + fps + " fps");
            }
        }

        // ---------------------------------------------------------------- shipped values (rule 9)

        [Test]
        public void ThePrefabCarriesTheShippedPerfectTuning()
        {
            var m = Motor();
            if (m == null) Assert.Ignore("Player.prefab not built yet — run VibeGame1/4. Build Prefabs.");
            string yaml = System.IO.File.ReadAllText("Assets/Prefabs/Player.prefab");
            if (!yaml.Contains("perfectWallJumpWindow:"))
                Assert.Ignore("Player.prefab predates the perfect-timing fields — run VibeGame1/4. Build Prefabs " +
                              "(a missing YAML key deserialises to the field initialiser, which is not proof).");
            Assert.AreEqual(0.14f, m.perfectWallJumpWindow, 1e-4f, "perfectWallJumpWindow");
            Assert.AreEqual(20f, m.perfectWallJumpRefund, 1e-4f, "perfectWallJumpRefund");
            Assert.AreEqual(0.04f, m.perfectDashJumpMinDelay, 1e-4f, "perfectDashJumpMinDelay");
            Assert.AreEqual(0.12f, m.perfectDashJumpWindow, 1e-4f, "perfectDashJumpWindow");
            Assert.AreEqual(30f, m.perfectDashJumpRefund, 1e-4f, "perfectDashJumpRefund");
            Assert.AreEqual(0.12f, m.perfectBurstWindow, 1e-4f, "perfectBurstWindow");
            Assert.AreEqual(30f, m.perfectBurstBonus, 1e-4f, "perfectBurstBonus");

            // The learnable band: under ~0.08 s (Celeste's coyote) is a reflex nobody can practise; over
            // ~0.20 s (Sekiro's un-spammed deflect) is free. Every window sits between.
            foreach (var w in new[] { m.perfectWallJumpWindow, m.perfectDashJumpWindow, m.perfectBurstWindow })
                Assert.That(w, Is.InRange(0.08f, 0.20f), "window " + w + "s is outside the learnable band");
            Assert.LessOrEqual(m.perfectWallJumpWindow, m.wallRunExitGrace,
                "the perfect grace window is wider than the exit grace itself, so part of it can never fire.");

            var st = Player().GetComponent<PlayerStamina>();
            Assert.IsNotNull(st);
            Assert.AreEqual(st.dashCost, m.perfectDashJumpRefund, 1e-4f,
                "a perfect dash-jump refunds exactly the dash: a chain of perfects is free, no more.");
            Assert.LessOrEqual(m.perfectWallJumpRefund, st.wallRunEntryCost + st.wallRunDrainPerSecond * 0.5f,
                "the wall-jump refund pays back more than a half-second run cost — a perfect chain would GAIN stamina.");
        }

        [Test]
        public void TheFeelAssetCarriesThePerfectValues()
        {
            var feel = AssetDatabase.LoadAssetAtPath<GameFeelSettings>("Assets/Data/GameFeel.asset");
            if (feel == null) Assert.Ignore("GameFeel.asset missing — run VibeGame1/3. Create Data.");
            string yaml = System.IO.File.ReadAllText("Assets/Data/GameFeel.asset");
            if (!yaml.Contains("perfectFovKick:"))
                Assert.Ignore("GameFeel.asset predates the perfect fields — run VibeGame1/3. Create Data.");
            Assert.AreEqual(3f, feel.perfectFovKick, 1e-4f, "perfectFovKick");
            Assert.AreEqual(0.6f, feel.perfectPromptSeconds, 1e-4f, "perfectPromptSeconds");
        }
    }
}
