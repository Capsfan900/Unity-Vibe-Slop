using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The movement budget, as SHIPPED on Player.prefab (rule 9) and as ARITHMETIC: what a full bar buys,
    /// what it costs to get it back, and the one assumption the level analyser rests on — that a full
    /// bar always covers a full wall run. Plus the pure momentum law the motor uses for every soft cap.
    /// </summary>
    public class StaminaTunablesTests
    {
        const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        static GameObject Player()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(go, PlayerPrefabPath + " is missing — run VibeGame1/4. Build Prefabs.");
            return go;
        }

        [Test]
        public void ThePrefabCarriesTheShippedStaminaTuning()
        {
            var s = Player().GetComponentInChildren<PlayerStamina>(true);
            Assert.IsNotNull(s, "Player.prefab has no PlayerStamina — PrefabFactory must add it (rebuild prefabs).");
            Assert.AreEqual(100f, s.max, 1e-4f, "max");
            Assert.AreEqual(45f, s.regenPerSecondGrounded, 1e-4f, "regenPerSecondGrounded");
            Assert.AreEqual(18f, s.regenPerSecondAirborne, 1e-4f, "regenPerSecondAirborne");
            Assert.AreEqual(0.45f, s.regenDelay, 1e-4f, "regenDelay");
            Assert.AreEqual(30f, s.dashCost, 1e-4f, "dashCost");
            Assert.AreEqual(12f, s.wallRunEntryCost, 1e-4f, "wallRunEntryCost");
            Assert.AreEqual(22f, s.wallRunDrainPerSecond, 1e-4f, "wallRunDrainPerSecond");
            Assert.AreEqual(12f, s.wallJumpCost, 1e-4f, "wallJumpCost");
            Assert.IsFalse(s.Infinite, "Infinite shipped ON — the budget is decorative");
        }

        [Test]
        public void AFullBarBuysExactlyThreeDashes()
        {
            var s = Player().GetComponentInChildren<PlayerStamina>(true);
            int dashes = Mathf.FloorToInt(s.max / s.dashCost + 1e-4f);
            Assert.AreEqual(3, dashes, "a full bar should be three dashes: fewer reads as stingy, four is the old endless chain");
            // And the bar TICKS the HUD draws at every dashCost are honest: 3 ticks inside 100.
            Assert.Less(s.dashCost * 3f, s.max + 1e-3f);
            Assert.Greater(s.dashCost * 4f, s.max);
        }

        [Test]
        public void AFullBarAlwaysCoversAFullWallRun()
        {
            // LevelArcAnalyzer models every run at its full duration. If a full bar could not pay for
            // one, every wall line in the level would be authored against a run the player cannot take.
            var go = Player();
            var s = go.GetComponentInChildren<PlayerStamina>(true);
            var m = go.GetComponentInChildren<FirstPersonMotor>(true);
            float fullRun = s.wallRunEntryCost + s.wallRunDrainPerSecond * m.wallRunMaxDuration;
            Assert.Less(fullRun, s.max,
                "a full run costs " + fullRun.ToString("F1") + " of " + s.max + " — the wall drops you before the clock does");
            // But not so cheap that the budget never bites: two full runs and a dash must NOT fit.
            Assert.Greater(fullRun * 2f + s.dashCost, s.max,
                "two full runs and a dash fit in one bar; the budget is not bounding anything");
        }

        [Test]
        public void TheGroundRefillsFastAndTheAirDoesNot()
        {
            var s = Player().GetComponentInChildren<PlayerStamina>(true);
            float groundRefill = s.regenDelay + s.max / s.regenPerSecondGrounded;
            float airRefill = s.regenDelay + s.max / s.regenPerSecondAirborne;
            Assert.Less(groundRefill, 3f, "an empty bar takes " + groundRefill.ToString("F1") + " s to refill on the ground — too long for a speedrun game");
            Assert.Greater(airRefill, 2f * groundRefill, "the air refills nearly as fast as the ground; there is no reason to land");
            // One dash back on the ground within a second: the rhythm the level is authored to.
            Assert.Less(s.regenDelay + s.dashCost / s.regenPerSecondGrounded, 1.2f);
        }

        // ------------------------------------------------------------------ momentum law

        [Test]
        public void DecayExcess_LeavesSpeedUnderTheCapAlone()
        {
            Vector3 v = new Vector3(3f, 0f, 4f);   // 5 m/s
            Vector3 r = FirstPersonMotor.DecayExcess(v, 17.6f, 3f, 0.016f);
            Assert.AreEqual(v, r);
        }

        [Test]
        public void DecayExcess_IsTheSameAtEveryFramerate()
        {
            // The whole point of exp(-k dt): 1 s of drag is 1 s of drag whether it is 20 frames or 500.
            Vector3 v0 = new Vector3(0f, 0f, 24f);
            Vector3 a = v0, b = v0, c = v0;
            for (int i = 0; i < 20; i++) a = FirstPersonMotor.DecayExcess(a, 17.6f, 3f, 1f / 20f);
            for (int i = 0; i < 60; i++) b = FirstPersonMotor.DecayExcess(b, 17.6f, 3f, 1f / 60f);
            for (int i = 0; i < 500; i++) c = FirstPersonMotor.DecayExcess(c, 17.6f, 3f, 1f / 500f);
            float want = 17.6f + (24f - 17.6f) * Mathf.Exp(-3f);
            Assert.AreEqual(want, a.z, 1e-3f, "20 fps");
            Assert.AreEqual(want, b.z, 1e-3f, "60 fps");
            Assert.AreEqual(want, c.z, 1e-3f, "500 fps");
        }

        [Test]
        public void DecayExcess_NeverChangesDirectionAndNeverCrossesTheCap()
        {
            Vector3 v = new Vector3(20f, 0f, 10f);
            Vector3 r = v;
            for (int i = 0; i < 600; i++) r = FirstPersonMotor.DecayExcess(r, 17.6f, 3f, 1f / 60f);
            Assert.AreEqual(0f, Vector3.Angle(v, r), 1e-3f, "drag turned the velocity");
            Assert.GreaterOrEqual(r.magnitude, 17.6f - 1e-3f, "drag pulled speed UNDER the soft cap");
            Assert.Less(r.magnitude, 17.7f, "after 10 s the surplus should be gone");
        }

        [Test]
        public void ADashInTheAirIsABurstNotACruise()
        {
            // 22 m/s dash exit, soft cap 17.6, drag 3: the surplus halves every 0.23 s, so within half a
            // second you are within 1 m/s of the cap. That is "momentum" (you kept 17.6) without "fly off
            // the map" (you did not keep 22 forever).
            var m = Player().GetComponentInChildren<FirstPersonMotor>(true);
            Vector3 v = new Vector3(0f, 0f, m.dashSpeed);
            float t = 0f;
            while (t < 0.5f) { v = FirstPersonMotor.DecayExcess(v, m.airSoftCap, m.airDrag, 1f / 60f); t += 1f / 60f; }
            Assert.Less(v.z, m.airSoftCap + 1f, "half a second after a dash you are still cruising at " + v.z.ToString("F1"));
            Assert.Greater(v.z, m.groundSpeed, "the air bled a dash below a sprint; momentum is gone");
            Assert.Less(m.airSoftCap, m.dashSpeed, "the soft cap is above the dash; drag never engages");
            Assert.Greater(m.maxHorizontalSpeed, m.dashSpeed, "the hard cap is under the dash; a dash would clip itself");
        }

        [Test]
        public void ChainedSlidesEarnLessEachTime()
        {
            var m = Player().GetComponentInChildren<FirstPersonMotor>(true);
            float first = m.slideBoost;
            float fourth = m.slideBoost * Mathf.Pow(m.slideChainFalloff, 3);
            Assert.Less(fourth, first * 0.3f, "a fourth chained slide still earns " + fourth.ToString("F1") + " m/s; the chain is free");
            Assert.Greater(fourth, 0.5f, "a fourth chained slide earns nothing; the tech is dead rather than taxed");
            Assert.Greater(m.slideChainWindow, m.slideCooldown, "the chain window is shorter than the cooldown; no slide can ever chain");
        }
    }
}
