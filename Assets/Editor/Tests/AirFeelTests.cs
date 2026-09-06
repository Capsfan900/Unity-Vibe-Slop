using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LevelArcAnalyzer = VibeGame1.EditorTools.LevelArcAnalyzer;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The 2026-09-03 evening "weight" retune, pinned. Three pure laws on the motor (fall gravity,
    /// air steer, landing tax), the ground-snap contract, and the shipped values on the prefab
    /// (rule 9). Plus the two things the retune must NOT have broken: a flat run-jump still clears
    /// the level's 6 m reach contract, and the slide-jump still out-jumps it by a margin worth
    /// learning.
    /// </summary>
    public class AirFeelTests
    {
        const string PlayerPrefab = "Assets/Prefabs/Player.prefab";

        static FirstPersonMotor Motor()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(go, PlayerPrefab + " missing — run VibeGame1/4. Build Prefabs");
            var m = go.GetComponentInChildren<FirstPersonMotor>(true);
            Assert.IsNotNull(m, "Player.prefab has no FirstPersonMotor");
            return m;
        }

        // ------------------------------------------------------------------ shipped values (rule 9)

        [Test]
        public void ThePrefabCarriesTheShippedWeightTuning()
        {
            var m = Motor();
            Assert.AreEqual(1.5f, m.fallGravityMultiplier, 1e-4f, "fallGravityMultiplier");
            Assert.AreEqual(0.8f, m.airCarryDecay, 1e-4f, "airCarryDecay");
            Assert.AreEqual(120f, m.airSteerDegPerSec, 1e-4f, "airSteerDegPerSec");
            Assert.AreEqual(16f, m.landingSoftSpeed, 1e-4f, "landingSoftSpeed");
            Assert.AreEqual(26f, m.landingHardSpeed, 1e-4f, "landingHardSpeed");
            Assert.AreEqual(0.35f, m.landingSpeedLoss, 1e-4f, "landingSpeedLoss");
            Assert.AreEqual(0.12f, m.groundSnapDistance, 1e-4f, "groundSnapDistance");
        }

        [Test]
        public void TheGroundSnapClearsTheSkinWidth()
        {
            // The whole point: a per-frame press that is inside the skin never registers a contact.
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            var cc = go.GetComponentInChildren<CharacterController>(true);
            var m = Motor();
            Assert.Greater(m.groundSnapDistance, cc.skinWidth * 2f,
                "groundSnapDistance must clear twice the skin width or isGrounded still flickers at high fps");
            Assert.Less(m.groundSnapDistance, cc.stepOffset,
                "a snap deeper than the step offset would drag the player down steps it should walk off");
        }

        [Test]
        public void ALandingFromAFlatJumpIsFree()
        {
            // A held flat jump lands at sqrt(2 g' h) with the heavier fall. That must be under the
            // soft threshold, or every bunny hop pays the tax and the tax reads as friction, not weight.
            var m = Motor();
            float landing = Mathf.Sqrt(2f * -m.gravity * m.fallGravityMultiplier * m.jumpHeight);
            Assert.Less(landing, m.landingSoftSpeed,
                "flat-jump landing " + landing.ToString("0.0") + " m/s is above landingSoftSpeed");
        }

        // ------------------------------------------------------------------ the laws

        [Test]
        public void FallGravity_OnlyWhileAirborneAndDescending()
        {
            Assert.AreEqual(-30f, FirstPersonMotor.FallGravity(-30f, 5f, false, 1.5f), 1e-5f, "rising");
            Assert.AreEqual(-45f, FirstPersonMotor.FallGravity(-30f, -5f, false, 1.5f), 1e-5f, "falling");
            Assert.AreEqual(-30f, FirstPersonMotor.FallGravity(-30f, -2f, true, 1.5f), 1e-5f, "grounded pin");
            Assert.AreEqual(-30f, FirstPersonMotor.FallGravity(-30f, 0f, false, 1.5f), 1e-5f, "apex");
        }

        [Test]
        public void AirSteer_TurnsWithoutChangingSpeed()
        {
            Vector3 hv = new Vector3(0f, 0f, 17f);
            Vector3 wish = new Vector3(1f, 0f, 0f);
            Vector3 after = FirstPersonMotor.AirSteer(hv, wish, 120f, 0.25f);
            Assert.AreEqual(17f, after.magnitude, 1e-3f, "speed is preserved");
            Assert.AreEqual(30f, Vector3.Angle(hv, after), 0.05f, "120 deg/s for 0.25 s is 30 deg");
            Assert.Greater(after.x, 0f, "turned toward the stick");
        }

        [Test]
        public void AirSteer_NeverOvershootsTheStick()
        {
            Vector3 hv = new Vector3(0f, 0f, 12f);
            Vector3 wish = new Vector3(1f, 0f, 1f).normalized;   // 45 deg off
            Vector3 after = FirstPersonMotor.AirSteer(hv, wish, 120f, 1f);   // could turn 120
            Assert.AreEqual(0f, Vector3.Angle(after, wish), 0.05f, "stops on the stick direction");
            Assert.AreEqual(12f, after.magnitude, 1e-3f);
        }

        [Test]
        public void AirSteer_LeavesBrakingToAirAccelerate()
        {
            // Stick against travel: not a steer. Turning 17 m/s around to face backwards would be a
            // free reversal; the brake is a bleed, and that is AirAccelerate's job.
            Vector3 hv = new Vector3(0f, 0f, 17f);
            Vector3 back = new Vector3(0.2f, 0f, -1f).normalized;
            Assert.AreEqual(hv, FirstPersonMotor.AirSteer(hv, back, 120f, 0.5f), "opposing stick");
            Assert.AreEqual(hv, FirstPersonMotor.AirSteer(hv, Vector3.zero, 120f, 0.5f), "no stick");
            Assert.AreEqual(hv, FirstPersonMotor.AirSteer(hv, Vector3.right, 0f, 0.5f), "steer disabled");
        }

        [Test]
        public void AirSteer_IsFramerateIndependent()
        {
            Vector3 hv = new Vector3(0f, 0f, 15f);
            Vector3 wish = Vector3.right;
            Vector3 one = FirstPersonMotor.AirSteer(hv, wish, 120f, 0.2f);
            Vector3 many = hv;
            for (int i = 0; i < 100; i++) many = FirstPersonMotor.AirSteer(many, wish, 120f, 0.002f);
            Assert.AreEqual(Vector3.Angle(hv, one), Vector3.Angle(hv, many), 0.05f, "same turn from 5 fps and 500 fps");
        }

        [Test]
        public void LandingTax_IsZeroBelowSoftAndFullAtHard()
        {
            Assert.AreEqual(1f, FirstPersonMotor.LandingSpeedFactor(10f, 16f, 26f, 0.35f), 1e-5f, "below soft");
            Assert.AreEqual(1f, FirstPersonMotor.LandingSpeedFactor(16f, 16f, 26f, 0.35f), 1e-5f, "at soft");
            Assert.AreEqual(0.825f, FirstPersonMotor.LandingSpeedFactor(21f, 16f, 26f, 0.35f), 1e-5f, "halfway");
            Assert.AreEqual(0.65f, FirstPersonMotor.LandingSpeedFactor(26f, 16f, 26f, 0.35f), 1e-5f, "at hard");
            Assert.AreEqual(0.65f, FirstPersonMotor.LandingSpeedFactor(60f, 16f, 26f, 0.35f), 1e-5f, "clamped above hard");
            Assert.AreEqual(1f, FirstPersonMotor.LandingSpeedFactor(60f, 26f, 16f, 0.35f), 1e-5f, "degenerate window disables");
            Assert.AreEqual(1f, FirstPersonMotor.LandingSpeedFactor(60f, 16f, 26f, 0f), 1e-5f, "zero loss disables");
        }

        [Test]
        public void CarryDecay_SpendsAWallExitButNotARun()
        {
            var m = Motor();
            Vector3 run = new Vector3(0f, 0f, m.groundSpeed);
            Assert.AreEqual(run, FirstPersonMotor.DecayExcess(run, m.groundSpeed, m.airCarryDecay, 1f),
                "a run in the air keeps every m/s: the decay is on the EXCESS over a run");

            Vector3 exit = new Vector3(0f, 0f, m.airSoftCap);   // 17.6, the most the air lets you keep
            float after1s = FirstPersonMotor.DecayExcess(exit, m.groundSpeed, m.airCarryDecay, 1f).magnitude;
            Assert.Less(after1s, m.airSoftCap - 3f, "a second of air has cost at least 3 m/s of a 17.6 exit: " + after1s.ToString("0.0"));
            Assert.Greater(after1s, m.groundSpeed + 1.5f, "but not all of it; the exit is still faster than a run at 1 s: " + after1s.ToString("0.0"));
        }

        // ------------------------------------------------------------------ what the retune must not break

        [Test]
        public void AFlatRunJumpStillClearsTheReachContract()
        {
            // The level authors gaps to "rise <= 1 m / gap <= 6 m". Fly the shipped profile over flat
            // ground with the analyser's own integrator and demand a margin over 6 m.
            LevelArcAnalyzer.MoveProfile p; string err;
            Assert.IsTrue(LevelArcAnalyzer.TryLoadProfile(out p, out err), err);
            float runJump = FlatJumpDistance(p, p.groundSpeed, true);
            Assert.Greater(runJump, 6.5f, "held run-jump over flat ground: " + runJump.ToString("0.00") + " m");
        }

        [Test]
        public void TheSlideJumpStillOutjumpsARunJumpByAQuarter()
        {
            LevelArcAnalyzer.MoveProfile p; string err;
            Assert.IsTrue(LevelArcAnalyzer.TryLoadProfile(out p, out err), err);
            float runJump = FlatJumpDistance(p, p.groundSpeed, true);
            float slideJump = FlatJumpDistance(p, p.SlideJumpSpeed, true);
            Assert.Greater(slideJump, runJump * 1.25f,
                "slide-jump " + slideJump.ToString("0.00") + " m vs run-jump " + runJump.ToString("0.00") + " m");
        }

        /// <summary>Horizontal distance a jump from flat ground covers back to the same height, integrated
        /// with the profile's own gravity and carry laws at 2 ms, stick held forward.</summary>
        static float FlatJumpDistance(LevelArcAnalyzer.MoveProfile p, float takeoffSpeed, bool holdJump)
        {
            const float dt = 0.002f;
            Vector3 v = new Vector3(0f, p.JumpTakeoffSpeed, takeoffSpeed);
            Vector3 pos = Vector3.zero;
            for (int i = 0; i < 5000; i++)
            {
                v.y += p.GravityFor(v.y, holdJump) * dt;
                v = p.Carry(v, dt);
                pos += v * dt;
                if (pos.y < 0f && v.y < 0f) return pos.z;
            }
            return pos.z;
        }
    }
}
