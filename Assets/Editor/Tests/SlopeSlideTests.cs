using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>The hill, before anyone slides down one.</b>
    ///
    /// <para>Until 2026-09-07 nothing in this game was sloped: every <c>LevelPieceKind</c> was an
    /// axis-aligned box, and <c>FirstPersonMotor.slideMaxDuration</c>'s tooltip guarded against "a
    /// downhill slide" running forever — a guard on a hill that could not be built. The user asked for
    /// ramps, so <see cref="TraversalMath.SlopeAccel"/> is the law that makes one worth sliding down and
    /// the other worth avoiding.</para>
    ///
    /// <para><b>The safety argument, and the reason this fixture leads with it.</b> The slope term is
    /// added to a SHIPPED motor that a whole level was tuned against. The only thing that makes that
    /// safe is that it is <i>exactly</i> zero on flat ground: every span authored before ramps existed
    /// is an axis-aligned box, so the normal is <c>Vector3.up</c>, the projection is the zero vector,
    /// and the slide behaves bit-for-bit as it did. <see cref="FlatGroundIsBitForBitUnchanged"/> is the
    /// assertion that protects every movement number tuned before this change.</para>
    ///
    /// <para>Pure maths, no scene, no <c>CharacterController</c> — the same pattern
    /// <c>WallRunMath</c> and <c>PerfectMath</c> follow, so the law can be argued about without a
    /// play-mode run.</para>
    /// </summary>
    public class SlopeSlideTests
    {
        const float Scale = 0.85f;   // FirstPersonMotor.slideSlopeAccel's shipped default

        static Vector3 NormalFromPitch(float degrees, Vector3 downhillXZ)
        {
            // A plane descending toward `downhillXZ`. Its surface is y = -z*tan(theta) along that axis,
            // so the normal is (0, 1, tan(theta)) there — it leans TOWARD the downhill side, not away
            // from it. Getting this backwards is what made the first run of this fixture assert a
            // downhill pull against an uphill plane; SlopeAccel itself was right all along.
            Vector3 d = downhillXZ.normalized;
            float r = Mathf.Deg2Rad * degrees;
            return (Vector3.up * Mathf.Cos(r) + d * Mathf.Sin(r)).normalized;
        }

        // ------------------------------------------------------------------ the safety property

        [Test]
        public void FlatGroundIsBitForBitUnchanged()
        {
            // THE assertion. Every level in the game today is flat boxes; if this ever returns anything
            // but the exact zero vector, a slope term has started leaking into geometry nobody
            // re-authored and every slide number tuned before 2026-09-07 is silently wrong.
            Assert.AreEqual(Vector3.zero, TraversalMath.SlopeAccel(Vector3.up, Scale),
                "flat ground must contribute EXACTLY zero, not merely something small");

            // Un-normalised and slightly-off normals are still flat enough to matter: a
            // CharacterController does not hand back a perfect (0,1,0) every frame.
            Assert.AreEqual(Vector3.zero, TraversalMath.SlopeAccel(Vector3.up * 3.7f, Scale));
            Assert.Less(TraversalMath.SlopeAccel(new Vector3(0f, 1f, 0.0005f), Scale).magnitude, 0.05f,
                "a hair off vertical is a flat floor, not a hill");
        }

        [Test]
        public void ScaleZeroDisablesSlopesEntirely()
        {
            // The escape hatch: if ramps ever feel wrong in play, one shipped value returns the motor to
            // its pre-ramp behaviour without reverting code.
            Vector3 n = NormalFromPitch(30f, Vector3.forward);
            Assert.AreEqual(Vector3.zero, TraversalMath.SlopeAccel(n, 0f));
            Assert.AreEqual(Vector3.zero, TraversalMath.SlopeAccel(n, -1f), "a negative scale is off, never reversed");
        }

        // ------------------------------------------------------------------ direction

        [Test]
        public void ItPullsDOWNHILL_AndOnlyHorizontally()
        {
            foreach (var downhill in new[] { Vector3.forward, Vector3.back, Vector3.right, new Vector3(1f, 0f, 1f).normalized })
            {
                Vector3 a = TraversalMath.SlopeAccel(NormalFromPitch(25f, downhill), Scale);

                Assert.Greater(Vector3.Dot(a.normalized, downhill.normalized), 0.999f,
                    "the pull must point straight down the fall line, not across it");
                Assert.AreEqual(0f, a.y, 1e-5f,
                    "the slide's speed is horizontal; the motor owns vertical separately (vel.y is " +
                    "pinned while grounded), so a y component here would fight it");
            }
        }

        [Test]
        public void UphillIsTheSameLaw_NotASpecialCase()
        {
            // A player sliding INTO a rise has velocity opposing the same vector, so the slide bleeds and
            // dies early on a climb with no sign test anywhere. This is the property that makes the
            // motor change one line instead of a branch.
            Vector3 n = NormalFromPitch(25f, Vector3.forward);
            Vector3 a = TraversalMath.SlopeAccel(n, Scale);

            Vector3 goingDownhill = Vector3.forward * 14f;
            Vector3 goingUphill = Vector3.back * 14f;

            Assert.Greater(Vector3.Dot(a, goingDownhill), 0f, "downhill must gain");
            Assert.Less(Vector3.Dot(a, goingUphill), 0f, "uphill must bleed, through the very same term");
        }

        // ------------------------------------------------------------------ magnitude

        [Test]
        public void SteeperPullsHarder_UpToTheWalkableLimit()
        {
            float last = -1f;
            // 45 deg is Unity's CharacterController.slopeLimit default: past it the player cannot walk
            // up at all and the ramp becomes a wall on the return trip. A level's ramps should live
            // under it, so that is the band this asserts monotonic.
            foreach (var deg in new[] { 5f, 10f, 20f, 30f, 40f, 45f })
            {
                float m = TraversalMath.SlopeAccel(NormalFromPitch(deg, Vector3.forward), Scale).magnitude;
                Assert.Greater(m, last, "a steeper hill must pull harder than a shallower one (" + deg + " deg)");
                last = m;
            }
        }

        [Test]
        public void TheMagnitudeIsGravityAlongTheSlope()
        {
            // g * sin(theta) * cos(theta) * scale -- the HORIZONTAL part of the along-slope pull. Stated
            // as arithmetic so the number can be argued about rather than felt for.
            float g = Mathf.Abs(Physics.gravity.y);
            foreach (var deg in new[] { 10f, 20f, 30f, 40f })
            {
                float r = Mathf.Deg2Rad * deg;
                float expect = g * Mathf.Sin(r) * Mathf.Cos(r) * Scale;
                float actual = TraversalMath.SlopeAccel(NormalFromPitch(deg, Vector3.forward), Scale).magnitude;
                Assert.AreEqual(expect, actual, 1e-3f, deg + " deg");
            }
        }

        [Test]
        public void AGentleRampPaysRealSpeedWithoutBreakingTheSlide()
        {
            // A 15 deg ramp, the sort a level designer would put under a slide line. Over one shipped
            // slideMaxDuration (0.9 s) the hill must add enough to be worth aiming at, and not so much
            // that a slide leaves on a speed nothing else in the game can produce.
            Vector3 a = TraversalMath.SlopeAccel(NormalFromPitch(15f, Vector3.forward), Scale);
            float gained = a.magnitude * 0.9f;

            Assert.Greater(gained, 1.5f,
                "under ~1.5 m/s over a whole slide a ramp is not worth steering onto");
            Assert.Less(gained, 8f,
                "a single 0.9 s interval must not hand out more than the dash does; sustained downhill " +
                "speed is separately bounded by slideMaxSpeed");
        }

        // ------------------------------------------------------------------ robustness

        [Test]
        public void ADegenerateOrWallNormalIsIgnored_NeverASpike()
        {
            // A CharacterController can report an odd contact for a frame in a corner. A movement law
            // that spiked on one bad sample would launch the player, so these return zero rather than
            // NaN, infinity or a sideways shove.
            Assert.AreEqual(Vector3.zero, TraversalMath.SlopeAccel(Vector3.zero, Scale), "degenerate normal");
            Assert.AreEqual(Vector3.zero, TraversalMath.SlopeAccel(Vector3.down, Scale), "an overhang is not ground");
            Assert.AreEqual(Vector3.zero, TraversalMath.SlopeAccel(Vector3.forward, Scale), "a vertical wall is not ground");

            Vector3 steep = NormalFromPitch(80f, Vector3.forward);
            Vector3 v = TraversalMath.SlopeAccel(steep, Scale);
            Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.z), "never NaN");
            Assert.IsFalse(float.IsInfinity(v.magnitude), "never infinite");
        }

        [Test]
        public void SlopeDegrees_AgreesWithTheNormalsItIsGiven()
        {
            Assert.AreEqual(0f, TraversalMath.SlopeDegrees(Vector3.up), 1e-3f);
            Assert.AreEqual(0f, TraversalMath.SlopeDegrees(Vector3.zero), 1e-3f, "degenerate reads as flat, never as a cliff");
            foreach (var deg in new[] { 5f, 15f, 30f, 45f })
                Assert.AreEqual(deg, TraversalMath.SlopeDegrees(NormalFromPitch(deg, Vector3.right)), 1e-2f);
        }

        // ------------------------------------------------------------------ rule 9

        [Test]
        public void TheSlopeScaleIsSHIPPED_NotJustAFieldInitialiser()
        {
            // Rule 9: a code default is not a shipped value. slideSlopeAccel is new, so Player.prefab's
            // YAML has no key for it until PrefabFactory writes one -- and a missing key silently
            // deserialises to the initialiser, which LOOKS right and is not proof of anything. This
            // reads the prefab, so it fails until generator 4 has run.
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (player == null) Assert.Ignore("Player.prefab not built yet - run VibeGame1/4. Build Prefabs.");
            var motor = player.GetComponent<FirstPersonMotor>();
            Assert.IsNotNull(motor);

            string yaml = System.IO.File.ReadAllText("Assets/Prefabs/Player.prefab");
            if (!yaml.Contains("slideSlopeAccel:"))
                Assert.Ignore("Player.prefab predates the ramp pass - run VibeGame1/4. Build Prefabs " +
                              "(a missing YAML key takes the field initialiser, which is not proof).");

            Assert.AreEqual(Scale, motor.slideSlopeAccel, 1e-4f,
                "the constant this fixture reasons with is not what the game ships");
            Assert.GreaterOrEqual(motor.slideSlopeAccel, 0f, "negative would reverse hills, not disable them");
            Assert.LessOrEqual(motor.slideSlopeAccel, 1f,
                "above 1 a slide gains more than gravity itself gives, which no ramp can justify");
        }

        [Test]
        public void TheLawIsFrameRateIndependent()
        {
            // Integrating the same hill in 1, 6 and 60 steps must land within a hair. Movement that
            // depends on frame rate is unshippable in a speedrun game -- the motor already learned this
            // the expensive way with the slide's own ground check.
            Vector3 a = TraversalMath.SlopeAccel(NormalFromPitch(20f, Vector3.forward), Scale);
            float total = 0.6f;
            foreach (var steps in new[] { 1, 6, 60, 600 })
            {
                float dt = total / steps;
                Vector3 v = Vector3.zero;
                for (int i = 0; i < steps; i++) v += a * dt;
                Assert.AreEqual((a * total).magnitude, v.magnitude, 1e-3f, steps + " steps");
            }
        }
    }

    /// <summary>The sustained descent under shipped tuning; the scene probe owns actual controller contact.</summary>
    public class DownhillSlideTests
    {
        FirstPersonMotor motor;
        float slopeLimit;
        readonly Vector3 normal = new Vector3(0f, 1f, 0.25f).normalized;

        [OneTimeSetUp] public void LoadShippedMotor()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            Assert.IsNotNull(prefab);
            motor = prefab.GetComponent<FirstPersonMotor>();
            slopeLimit = prefab.GetComponent<CharacterController>().slopeLimit;
            Assert.IsNotNull(motor);
        }

        bool Sustains(Vector3 v, Vector3 n, bool grounded = true)
        {
            return TraversalMath.SustainsDownhillSlide(v, n, motor.slideSlopeAccel, slopeLimit, grounded);
        }

        Vector3 Step(Vector3 v, Vector3 n, float dt, bool downhill)
        {
            return TraversalMath.DrySlideStep(v, n, motor.slideSlopeAccel, motor.slideFriction, motor.slideMaxSpeed, dt, downhill);
        }

        [TestCase(20)] [TestCase(60)] [TestCase(240)]
        public void FlatUphillAndAirborneRetainExactLegacyArithmetic(int fps)
        {
            foreach (var n in new[] { Vector3.up, normal })
                foreach (bool grounded in new[] { false, true })
                {
                    Vector3 velocity = Vector3.back * 16f;
                    for (int frame = 0; frame < fps; frame++)
                    {
                        bool downhill = Sustains(velocity, n, grounded);
                        Assert.IsFalse(downhill);
                        // Use the same multiplication as the legacy motor, including float rounding.
                        Vector3 exact = velocity + TraversalMath.SlopeAccel(n, motor.slideSlopeAccel) * (1f / fps);
                        exact *= Mathf.Max(0f, 1f - motor.slideFriction * (1f / fps));
                        velocity = Step(velocity, n, 1f / fps, downhill);
                        Assert.AreEqual(exact, velocity);
                    }
                }
        }

        [Test] public void SustainNeedsActualDownhillContactAndEnabledSlopeTuning()
        {
            Assert.IsTrue(Sustains(Vector3.forward * 16f, normal));
            Assert.IsFalse(Sustains(Vector3.forward * 16f, normal, false), "cached air/coyote normal");
            Assert.IsFalse(Sustains(Vector3.right * 16f, normal), "traversing across the slope");
            Assert.IsFalse(Sustains(Vector3.back * 16f, normal), "uphill");
            Assert.IsFalse(Sustains(Vector3.zero, normal), "stationary");
            foreach (var n in new[] { Vector3.up, Vector3.zero, Vector3.down, Vector3.forward, new Vector3(0f, 1f, 2f).normalized })
                Assert.IsFalse(Sustains(Vector3.forward * 16f, n), "nonwalkable or flat " + n);
            Assert.IsFalse(TraversalMath.SustainsDownhillSlide(Vector3.forward * 16f, normal, 0f, slopeLimit, true));
        }

        [TestCase(20)] [TestCase(60)] [TestCase(240)]
        public void OneSlideCrosses48MetresThenEndsOnTheRunOut(int fps)
        {
            float dt = 1f / fps, now = 0f, distance = 0f, endsAt = motor.slideMaxDuration;
            Vector3 velocity = Vector3.forward * 16f;
            while (distance < 48f && now < 5f)
            {
                now += dt;
                bool downhill = Sustains(velocity, normal);
                Assert.IsTrue(downhill);
                velocity = Step(velocity, normal, dt, downhill);
                endsAt = now + motor.slideMaxDuration;
                Assert.Greater(velocity.magnitude, motor.slideEndSpeed);
                Assert.LessOrEqual(velocity.magnitude, motor.slideMaxSpeed + 0.001f);
                distance += velocity.z * dt;
            }
            Assert.GreaterOrEqual(distance, 48f);
            Assert.Greater(now, motor.slideMaxDuration, "the route needs more than the old duration");
            float exitTime = now, runOut = 0f;
            do
            {
                now += dt;
                Assert.IsFalse(Sustains(velocity, Vector3.up));
                velocity = Step(velocity, Vector3.up, dt, false);
                runOut += velocity.z * dt;
            } while (velocity.magnitude > motor.slideEndSpeed && now < endsAt);
            Assert.LessOrEqual(now - exitTime, motor.slideMaxDuration + dt);
            Assert.Less(runOut, 12f, "original friction should end the slide within the 24.4 m run-out");
        }

        [Test] public void AddedSpeedIsCappedButExternalCarryIsPreserved()
        {
            Assert.That(Step(Vector3.forward * (motor.slideMaxSpeed - 0.1f), normal, 1f, true).magnitude,
                Is.EqualTo(motor.slideMaxSpeed).Within(0.0001f));
            float carry = motor.slideMaxSpeed + 3f;
            Assert.That(Step(Vector3.forward * carry, normal, 1f, true).magnitude, Is.EqualTo(carry).Within(0.0001f));
        }

        [Test] public void DownhillSpeedAgreesAt20_60_240Fps()
        {
            foreach (int fps in new[] { 20, 60, 240 })
            {
                Vector3 velocity = Vector3.forward * 16f;
                for (int i = 0; i < fps * 2; i++) velocity = Step(velocity, normal, 1f / fps, true);
                float expected = 16f + TraversalMath.SlopeAccel(normal, motor.slideSlopeAccel).z * 2f;
                Assert.That(velocity.z, Is.EqualTo(expected).Within(0.002f), fps + " fps");
            }
        }

        [TestCase(20)] [TestCase(60)] [TestCase(240)]
        public void DownhillSnapFollowsThePlaneIncludingControllerSkin(int fps)
        {
            Vector3 displacement = new Vector3(0f, -motor.groundSnapDistance, motor.slideMaxSpeed / fps);
            float planeY = -displacement.z * 0.25f;
            float snapY = TraversalMath.DownhillSnapY(displacement, normal, motor.groundSnapDistance);
            Assert.LessOrEqual(snapY, planeY - motor.groundSnapDistance + 0.0001f);
            Assert.That(TraversalMath.DownhillSnapY(displacement, Vector3.up, motor.groundSnapDistance), Is.EqualTo(displacement.y));
            Assert.That(TraversalMath.DownhillSnapY(displacement, normal, 0f), Is.EqualTo(displacement.y));
            Assert.That(TraversalMath.DownhillSnapY(displacement, Vector3.forward, motor.groundSnapDistance), Is.EqualTo(displacement.y));
        }
    }
}
