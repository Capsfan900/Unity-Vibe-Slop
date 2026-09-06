using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>Hard rule 9, for the wall run: a code default is not a shipped value.</b>
    ///
    /// <para>Three places carry the wall-run tuning: the field initialisers in
    /// <see cref="FirstPersonMotor"/> (decorative — they touch nothing that already exists), the writes in
    /// <c>PrefabFactory.BuildAll</c> (authoritative — they are what a rebuild ships), and the literal list
    /// in <c>WallRunMechanicTests.Shipped()</c> (what every mechanic test actually proves things about).
    /// This file pins the shipped <c>Player.prefab</c> to that same list, so if any of the three drifts,
    /// something fails loudly instead of the tests quietly verifying numbers nobody is playing.</para>
    ///
    /// <para>It also holds the DECAY WINDOW: the retune 0.20 -> 0.35 happened because the shipped decay
    /// sat below <c>ln(minEntry/minSustain)/maxDuration</c>, which made <c>wallRunMinSustainSpeed</c>
    /// unreachable and every run end on the clock regardless of entry speed. The window assertions make
    /// that class of dead-parameter retune impossible to ship silently again.</para>
    /// </summary>
    public class WallRunTunablesTests
    {
        const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        static FirstPersonMotor Motor()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(go, PlayerPrefabPath + " is missing — run VibeGame1/4. Build Prefabs.");
            var m = go.GetComponentInChildren<FirstPersonMotor>(true);
            Assert.IsNotNull(m, "Player.prefab has no FirstPersonMotor.");
            return m;
        }

        [Test]
        public void ThePrefabCarriesTheShippedWallRunTuning()
        {
            var m = Motor();
            // The same list WallRunMechanicTests.Shipped() writes as literals. One assert per field so a
            // drift names the field that moved rather than "some number changed".
            Assert.AreEqual(6f, m.wallRunMinEntrySpeed, 1e-4f, "wallRunMinEntrySpeed");
            Assert.AreEqual(9f, m.wallRunMaxEntryFallSpeed, 1e-4f, "wallRunMaxEntryFallSpeed");
            Assert.AreEqual(0.80f, m.wallRunMaxApproachCos, 1e-4f, "wallRunMaxApproachCos");
            Assert.AreEqual(-0.05f, m.wallRunMinLookAlongCos, 1e-4f, "wallRunMinLookAlongCos");
            Assert.AreEqual(1.75f, m.wallRunMaxDuration, 1e-4f, "wallRunMaxDuration");
            Assert.AreEqual(0.10f, m.wallRunGravityStartScale, 1e-4f, "wallRunGravityStartScale");
            Assert.AreEqual(0.60f, m.wallRunGravityEndScale, 1e-4f, "wallRunGravityEndScale");
            Assert.AreEqual(3f, m.wallRunEntryUpSpeed, 1e-4f, "wallRunEntryUpSpeed");
            Assert.AreEqual(0.35f, m.wallRunSpeedDecay, 1e-4f,
                "wallRunSpeedDecay — 0.20 shipped once and made the sustain floor unreachable; " +
                "if this is a deliberate retune, prove the window test below still passes");
            Assert.AreEqual(4f, m.wallRunMinSustainSpeed, 1e-4f, "wallRunMinSustainSpeed");
            Assert.AreEqual(14f, m.wallRunAccel, 1e-4f, "wallRunAccel");
            Assert.AreEqual(13.75f, m.wallRunTopSpeed, 1e-4f, "wallRunTopSpeed (1.25x groundSpeed: the wall is faster than the floor)");
            Assert.AreEqual(0.15f, m.wallRunLostGrace, 1e-4f, "wallRunLostGrace");
            Assert.AreEqual(0.15f, m.wallRunExitGrace, 1e-4f, "wallRunExitGrace");
            Assert.AreEqual(17.6f, m.airSoftCap, 1e-4f, "airSoftCap (1.6x groundSpeed)");
            Assert.AreEqual(3f, m.airDrag, 1e-4f, "airDrag");
            Assert.AreEqual(4f, m.groundOverspeedDecay, 1e-4f, "groundOverspeedDecay");
            Assert.AreEqual(27.5f, m.maxHorizontalSpeed, 1e-4f, "maxHorizontalSpeed (2.5x groundSpeed)");
            Assert.AreEqual(1.2f, m.slideChainWindow, 1e-4f, "slideChainWindow");
            Assert.AreEqual(0.6f, m.slideChainFalloff, 1e-4f, "slideChainFalloff");
            Assert.AreEqual(2.5f, m.wallRunStickSpeed, 1e-4f, "wallRunStickSpeed");
            Assert.AreEqual(10f, m.wallRunExitUpSpeed, 1e-4f, "wallRunExitUpSpeed");
            Assert.AreEqual(7f, m.wallRunExitPushSpeed, 1e-4f, "wallRunExitPushSpeed");
            Assert.AreEqual(4f, m.wallRunExitTangentBoost, 1e-4f, "wallRunExitTangentBoost");
            Assert.AreEqual(6, m.maxWallRuns, "maxWallRuns (stamina is the real bound now)");
            Assert.AreEqual(0.20f, m.wallRunCooldown, 1e-4f, "wallRunCooldown");
            Assert.AreEqual(13f, m.wallRunCameraRoll, 1e-4f, "wallRunCameraRoll");
            Assert.AreEqual(7f, m.wallRunExitRollKick, 1e-4f, "wallRunExitRollKick");
        }

        [Test]
        public void BothEndConditionsAreReachable_TheDecayWindow()
        {
            // Exponential decay: speed after the full clock is v0 * exp(-k * T). For BOTH endings to be
            // live paths, a minimum-speed entry must cross minSustainSpeed before T (k above the lower
            // bound) and a sprint entry must NOT (k below the upper bound). Shipped: 0.21 < 0.35 < 0.49.
            var m = Motor();
            var pr = m.WallRunSettings;

            float lower = Mathf.Log(pr.minEntrySpeed / pr.minSustainSpeed) / pr.maxDuration;
            float upper = Mathf.Log(pr.topSpeed / pr.minSustainSpeed) / pr.maxDuration;

            Assert.Greater(pr.speedDecay, lower,
                "a minimum-speed entry rides the full clock: wallRunMinSustainSpeed is unreachable and " +
                "'a slow entry gets a short run' is gone. This is the exact 0.20 bug, re-shipped.");
            Assert.Less(pr.speedDecay, upper,
                "a full sprint entry bleeds out before the timer: wallRunMaxDuration is unreachable and " +
                "the loan never runs its length.");

            // And the entry gate itself must sit above the floor, or a run could be born already dead.
            Assert.Greater(pr.minEntrySpeed, pr.minSustainSpeed,
                "minEntrySpeed <= minSustainSpeed: a legal entry ends on frame one");
        }

        [Test]
        public void ASlowEntryLosesRealTimeAndASprintDoesNot()
        {
            // The window test above proves reachability; this one proves the FEEDBACK is big enough to
            // read. A scrape-in entry must lose at least a quarter of the clock, or the distinction the
            // parameter buys is imperceptible and might as well not exist.
            var m = Motor();
            var pr = m.WallRunSettings;

            float slowRunEnds = Mathf.Log(pr.minEntrySpeed / pr.minSustainSpeed) / pr.speedDecay;
            Assert.Less(slowRunEnds, 0.75f * pr.maxDuration,
                "a minimum entry keeps " + (slowRunEnds / pr.maxDuration * 100f).ToString("F0") +
                "% of the clock — too close to a full run to read as a shorter one");
            Assert.Greater(slowRunEnds, 0.4f * pr.maxDuration,
                "a minimum entry gets under 40% of the clock — a legal entry that instantly dumps you " +
                "reads as a broken wall, not a rule");
        }
    }
}
