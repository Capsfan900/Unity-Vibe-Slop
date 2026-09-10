using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Parriable projectiles on the parkour spans (2026-09-05): the maths in <see cref="ProjectileMath"/>,
    /// and the shipped data and prefabs (rule 9). Asset checks Ignore until `3. Create Data` /
    /// `4. Build Prefabs` have run; the maths runs anywhere.
    /// </summary>
    public class ProjectileTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void TheCueFiresOnceAtTheLead()
        {
            Assert.IsFalse(ProjectileMath.CueDue(1.0f, 0.28f, false), "a second out is not the cue");
            Assert.IsTrue(ProjectileMath.CueDue(0.28f, 0.28f, false), "exactly at the lead");
            Assert.IsTrue(ProjectileMath.CueDue(0.10f, 0.28f, false));
            Assert.IsFalse(ProjectileMath.CueDue(0.10f, 0.28f, true), "never twice");
        }

        [Test]
        public void FlightTimeIsDistanceOverSpeed_AndTheBandIsInclusive()
        {
            Assert.AreEqual(1.0f, ProjectileMath.TimeToImpact(32f, 32f), Eps);
            Assert.Greater(ProjectileMath.TimeToImpact(10f, 32f), 0.28f, "the nearest shot still leaves room for the cue");
            Assert.Less(ProjectileMath.TimeToImpact(15f, 32f), 0.5f, "a mid-band shot arrives inside half a second: a tell you answer at a run, not one you stop for");
            Assert.IsTrue(ProjectileMath.InBand(10f, 10f, 30f)); Assert.IsTrue(ProjectileMath.InBand(30f, 10f, 30f));
            Assert.IsFalse(ProjectileMath.InBand(9.9f, 10f, 30f)); Assert.IsFalse(ProjectileMath.InBand(30.1f, 10f, 30f));
        }

        [Test]
        public void ContactForecastUsesRelativeClosingSpeed_AndSeparatingShotsAreNotImminent()
        {
            float closing = ProjectileMath.RelativeTimeToContact(Vector3.zero, Vector3.forward * 20f,
                Vector3.forward * 36f, Vector3.back * 27.5f, 1f);
            Assert.AreEqual(19f / 63.5f, closing, Eps,
                "the cue must account for the player running into the bolt");

            float separating = ProjectileMath.RelativeTimeToContact(Vector3.zero, Vector3.forward * 20f,
                Vector3.forward * 36f, Vector3.forward * 40f, 1f);
            Assert.IsTrue(float.IsPositiveInfinity(separating),
                "a target opening the gap has no honest imminent-contact forecast");
        }

        [Test]
        public void FlightPlanMeasuresTheRealLaunchRootAndSphereContact_NotMuzzleToChest()
        {
            Vector3 muzzle = Vector3.zero;
            Vector3 chest = Vector3.forward * 15f;
            ProjectileFlightPlan plan = ProjectileFlightMath.Plan(muzzle, chest, Vector3.zero,
                40f, 1f, 180f, ProjectileShooter.SpawnForwardOffset,
                Projectile.DefaultHitRadius, Projectile.CueLead + ProjectileShooter.CueMargin,
                Projectile.DefaultMaxLife);

            Assert.AreEqual(ProjectileFlightReadiness.Ready, plan.readiness);
            Assert.AreEqual(ProjectileShooter.SpawnForwardOffset,
                Vector3.Distance(muzzle, plan.launchPosition), 1e-3f,
                "the forecast begins where FireAt creates the logical root, not back at the muzzle");
            Assert.That(plan.contactSeconds,
                Is.InRange(Projectile.CueLead + ProjectileShooter.CueMargin - 1e-3f,
                           Projectile.CueLead + ProjectileShooter.CueMargin + 0.01f),
                "15 m / 40 m/s looked safe only while the old estimate ignored 0.6 m of spawn offset and the 1 m hit radius");
            Assert.Less(plan.speed, 40f, "the planner should select the fastest contact-safe launch, not violate the cue budget");
        }

        [Test]
        public void ShippedMotionCasesStayContactSafe_AndAnUnsafeNearClosingShotIsRefused()
        {
            float minimum = Projectile.CueLead + ProjectileShooter.CueMargin;
            foreach (var targetVelocity in new[]
            {
                Vector3.zero,
                Vector3.back * 27.5f,
                Vector3.right * 27.5f
            })
            {
                float distance = targetVelocity.z < 0f ? 32f : 15f;
                ProjectileFlightPlan plan = ProjectileFlightMath.Plan(Vector3.zero,
                    Vector3.forward * distance, targetVelocity, 40f, 1f, 180f,
                    ProjectileShooter.SpawnForwardOffset, Projectile.DefaultHitRadius,
                    minimum, Projectile.DefaultMaxLife);
                Assert.AreEqual(ProjectileFlightReadiness.Ready, plan.readiness,
                    "stationary, maximum-speed closing and crossing runners all need an honest launch plan");
                Assert.GreaterOrEqual(plan.contactSeconds + 1e-3f, minimum);
                Assert.Less(plan.contactSeconds, Projectile.DefaultMaxLife);
            }

            ProjectileFlightPlan unsafeNear = ProjectileFlightMath.Plan(Vector3.zero,
                Vector3.forward * 2.5f, Vector3.back * 27.5f, 36f, 1f, 240f,
                ProjectileShooter.SpawnForwardOffset, Projectile.DefaultHitRadius,
                minimum, Projectile.DefaultMaxLife);
            Assert.AreEqual(ProjectileFlightReadiness.UnsafeFlight, unsafeNear.readiness,
                "if the runner reaches the hit sphere before the cue budget even at minimum bolt speed, refuse the shot");

            ProjectileFlightPlan noContact = ProjectileFlightMath.Plan(Vector3.zero,
                Vector3.forward * 15f, Vector3.forward * 50f, 40f, 1f, 0f,
                ProjectileShooter.SpawnForwardOffset, Projectile.DefaultHitRadius,
                minimum, Projectile.DefaultMaxLife);
            Assert.AreEqual(ProjectileFlightReadiness.NoContact, noContact.readiness,
                "a straight bolt slower than a fleeing target has no honest bounded arrival to cue");
        }

        [Test]
        public void FlightPlanInterceptsAClosingSpeedrunLineInsideTheEncounter()
        {
            Vector3 muzzle = new Vector3(-11.5f, 5.4f, 53f);
            Vector3 chest = new Vector3(-0.67f, 2.78f, 40.44f);
            Vector3 velocity = new Vector3(-1.03f, 0.52f, 27.48f);
            ProjectileFlightPlan plan = ProjectileFlightMath.Plan(muzzle, chest, velocity,
                40f, 1f, 180f, ProjectileShooter.SpawnForwardOffset,
                Projectile.DefaultHitRadius, Projectile.CueLead + ProjectileShooter.CueMargin,
                Projectile.DefaultMaxLife);

            Assert.AreEqual(ProjectileFlightReadiness.Ready, plan.readiness);
            Assert.GreaterOrEqual(plan.contactSeconds,
                Projectile.CueLead + ProjectileShooter.CueMargin - 1e-3f);
            Assert.Less(plan.contactSeconds, 0.8f,
                "an oblique closing runner should be intercepted locally, not chased beyond the route");
        }

        [Test]
        public void ForecastAndRuntimeShareOneCappedHomingStep()
        {
            Vector3 current = Vector3.forward;
            Vector3 next = ProjectileFlightMath.HomingDirection(current, Vector3.zero,
                Vector3.right * 10f, 180f, 0.1f);
            Assert.That(Vector3.Angle(current, next), Is.EqualTo(18f).Within(0.01f));
            Assert.AreEqual(1f, next.magnitude, Eps);
            Assert.AreEqual(current, ProjectileFlightMath.HomingDirection(current, Vector3.zero,
                Vector3.right, 0f, 0.1f), "zero-homing data preserves a straight bolt");
        }

        [Test]
        public void BurstCadenceIsAContactFloorAndLateContactsNeverCatchUp()
        {
            float second = ProjectileFlightMath.NextContactTime(100.44f, 0.42f);
            Assert.AreEqual(100.86f, second, Eps);
            Assert.IsFalse(ProjectileFlightMath.ContactSlotOpen(100.84f, second),
                "a blindly timed launch whose contact catches the first phrase beat must defer");
            Assert.IsTrue(ProjectileFlightMath.ContactSlotOpen(second, second));
            Assert.AreEqual(101.52f, ProjectileFlightMath.NextContactTime(101.10f, 0.42f), Eps,
                "a late actual forecast starts the next spacing from itself, never from stale phrase debt");
        }

        [Test]
        public void ASpacingTieRetriesAtTheFirstSafeContact_InsteadOfStarvingEveryBeat()
        {
            const float now = 100f;
            const float predicted = 100.40f;
            const float occupiedUntil = 100.82f;
            float retry = ProjectileFlightMath.RetryTimeForContactSlot(now, predicted, occupiedUntil);
            float unchangedFlight = predicted - now;

            Assert.That(retry, Is.EqualTo(100.428333f).Within(1e-4f));
            Assert.IsTrue(ProjectileFlightMath.ContactSlotOpen(retry + unchangedFlight, occupiedUntil),
                "the second sentry must own the first safe contact instead of losing the same 1.6 s tie forever");
            Assert.Less(retry - now, 1.6f,
                "a spacing collision is a short hand-off between traversal tools, not a skipped full beat");
        }

        [Test]
        public void TightRouteOcclusionKeepsTheBlueSentryArmed_ButConservativeShootersReacquire()
        {
            // A rail briefly hiding a blue traversal sentry must not charge it another 0.7 s arm-up
            // when the runner clears the rail. Heavy and Surge are deliberately conservative: their
            // acquisition state resets, so a newly restored line still gets its honest breath.
            Assert.IsTrue(ProjectileShooter.PreservesAcquisitionOnOcclusion(true),
                "ordinary blue sentries are authored inside tight parkour and retain their acquired beat");
            Assert.IsFalse(ProjectileShooter.PreservesAcquisitionOnOcclusion(false),
                "the Heavy keeps its conservative clearance/acquisition contract");

            var turret = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy03"));
            if (turret == null) Assert.Ignore("run 3. Create Data");
            Assert.IsFalse(ProjectileShooter.PreservesAcquisitionOnOcclusion(turret.projectileAllowTightRouteShots),
                "the already-tuned Surge Turret must not inherit the blue sentry's occlusion exception");
        }

        [Test]
        public void TightRouteTransientRejectionRetriesImmediately_ConservativeShootersHoldTheirBeat()
        {
            const float previousBeat = 100f;
            const float now = 100.25f;
            const float ordinaryInterval = 1.6f;

            float ordinaryRetry = ProjectileShooter.RejectionRetryTime(true, previousBeat, now, ordinaryInterval);
            Assert.That(ordinaryRetry, Is.EqualTo(now + ProjectileShooter.TightRouteRetrySeconds).Within(Eps),
                "a transient clear-path rejection on the blue traversal tool gets its authored short retry, never a full silent beat");
            Assert.Less(ordinaryRetry - now, ordinaryInterval);

            float heavyRetry = ProjectileShooter.RejectionRetryTime(false, previousBeat, now, ordinaryInterval);
            Assert.AreEqual(ProjectileMath.NextBeat(previousBeat, now, ordinaryInterval), heavyRetry, Eps,
                "the Heavy retains the old conservative metronome after a rejected shot");

            const float turretInterval = 1.1f;
            float turretRetry = ProjectileShooter.RejectionRetryTime(false, previousBeat, now, turretInterval);
            Assert.AreEqual(ProjectileMath.NextBeat(previousBeat, now, turretInterval), turretRetry, Eps,
                "the tuned ramp turret remains on its full beat after a rejection");
        }

        [Test]
        public void RuntimeForecastUsesMotorVelocityAndThePlayerClockDuringWorldHitstop()
        {
            // A player remains mobile through world hitstop. Dividing their observed displacement by the
            // scaled world delta would turn a 27.5 m/s runner into a false 1375 m/s target at 0.02x.
            Vector3 motorVelocity = new Vector3(4f, 0f, 27.5f);
            const float playerDelta = 1f / 60f;
            Vector3 previousChest = new Vector3(10f, 2f, 40f);
            Vector3 currentChest = previousChest + motorVelocity * playerDelta;
            float scaledWorldDelta = playerDelta * 0.02f;
            Vector3 badScaledObservation = (currentChest - previousChest) / scaledWorldDelta;

            Vector3 fromMotor = ProjectileMath.ForecastTargetVelocity(motorVelocity, true,
                previousChest, currentChest, playerDelta);
            Assert.That(Vector3.Distance(motorVelocity, fromMotor), Is.LessThan(Eps),
                "the motor's player-clock velocity is the authoritative forecast input during hitstop");
            Assert.Greater(badScaledObservation.magnitude, fromMotor.magnitude * 20f,
                "this fixture would catch a regression back to observed displacement / scaled Time.deltaTime");

            Vector3 fallback = ProjectileMath.ForecastTargetVelocity(Vector3.zero, false,
                previousChest, currentChest, playerDelta);
            Assert.That(Vector3.Distance(motorVelocity, fallback), Is.LessThan(Eps),
                "without a motor, infer from PlayerDelta, never the slowed world clock");
        }

        [Test]
        public void BlueSentryFacingUsesLookDirection_NotTheBackpedalVelocityProxy()
        {
            // The player is moving away from the perch but looking back at it: that is a deliberate,
            // parryable backpedal and must fire. Turning the LOOK away is the distinct unsafe case.
            Vector3 muzzle = Vector3.zero;
            Vector3 chest = Vector3.forward * 20f;
            Vector3 fleeingVelocity = Vector3.forward * 12f;

            Assert.IsTrue(ProjectileMath.ArrivesInsideFacing(muzzle, chest, fleeingVelocity, 40f,
                Vector3.back, 75f),
                "looking back toward the blue sentry keeps a fleeing run answerable");
            Assert.IsFalse(ProjectileMath.ArrivesInsideFacing(muzzle, chest, fleeingVelocity, 40f,
                Vector3.forward, 75f),
                "the same movement while looking away remains a bolt at an unanswerable back");
        }

        [Test]
        public void GodModeStillAllowsPerfectParries_ButIgnoresDamageOutcomes()
        {
            Assert.IsTrue(PlayerCombat.CanResolveWhileInvulnerable(false),
                "F8 invulnerability must keep the real blue-bolt parry path live");
            Assert.IsFalse(PlayerCombat.CanResolveWhileInvulnerable(true),
                "execution invulnerability must keep its presentation sealed from a carried parry window");
            Assert.IsFalse(PlayerCombat.ShouldIgnoreWhileInvulnerable(ParryResult.Perfect),
                "F8 must keep the real blue-bolt Perfect/reflection path live");
            Assert.IsTrue(PlayerCombat.ShouldIgnoreWhileInvulnerable(ParryResult.Hit));
            Assert.IsTrue(PlayerCombat.ShouldIgnoreWhileInvulnerable(ParryResult.Blocked));
            Assert.IsTrue(PlayerCombat.ShouldIgnoreWhileInvulnerable(ParryResult.None));
        }

        [Test]
        public void RelativeSweepFindsTheSameEarliestContactAt20_60_240FpsAndAHitch()
        {
            float expected = 9f / 63.5f;
            foreach (float dt in new[] { 1f / 20f, 1f / 60f, 1f / 240f, 0.1f })
            {
                Vector3 bolt = Vector3.zero;
                Vector3 target = Vector3.forward * 10f;
                float elapsed = 0f;
                bool hit = false;
                for (int frame = 0; frame < 2000 && elapsed <= 1f; frame++)
                {
                    Vector3 nextBolt = bolt + Vector3.forward * 36f * dt;
                    Vector3 nextTarget = target + Vector3.back * 27.5f * dt;
                    float fraction;
                    if (ProjectileMath.SweptSphereFirstHit(bolt, nextBolt, target, nextTarget, 1f, out fraction))
                    {
                        Assert.AreEqual(expected, elapsed + fraction * dt, Eps,
                            "contact time drifted at dt=" + dt);
                        hit = true;
                        break;
                    }
                    bolt = nextBolt;
                    target = nextTarget;
                    elapsed += dt;
                }
                Assert.IsTrue(hit, "the moving target was tunneled through at dt=" + dt);
            }
        }

        [Test]
        public void RelativeSweepHitsACrossedTargetButRejectsANearMiss()
        {
            float fraction;
            Assert.IsTrue(ProjectileMath.SweptSphereFirstHit(Vector3.zero, Vector3.right * 4f,
                Vector3.right * 2f, Vector3.right * 2f, 0.5f, out fraction));
            Assert.AreEqual(0.375f, fraction, Eps, "the earliest sphere entry is the contact point");
            Assert.IsFalse(ProjectileMath.SweptSphereFirstHit(Vector3.zero, Vector3.right * 4f,
                new Vector3(2f, 1f, 0f), new Vector3(2f, 1f, 0f), 0.5f, out fraction),
                "passing outside the radius is still a miss");
        }

        [Test]
        public void TeleportIsReanchoredInsteadOfBecomingACollisionPath()
        {
            Vector3 previousTarget = Vector3.zero;
            Vector3 teleportedTarget = Vector3.right * 100f;
            Vector3 start = ProjectileMath.ContinuousTargetStart(previousTarget, teleportedTarget,
                Vector3.zero, 1f / 60f);
            Assert.AreEqual(teleportedTarget, start, "a teleport starts a new target history");

            float fraction;
            Assert.IsFalse(ProjectileMath.SweptSphereFirstHit(Vector3.right * 50f, Vector3.right * 50f,
                start, teleportedTarget, 1f, out fraction),
                "the teleport path itself may not strike a stationary bolt");

            Vector3 ordinaryStep = Vector3.right * (27.5f / 60f);
            Assert.AreEqual(previousTarget, ProjectileMath.ContinuousTargetStart(previousTarget, ordinaryStep,
                Vector3.right * 27.5f, 1f / 60f), "ordinary fast movement remains part of the relative sweep");
            Assert.AreEqual(ordinaryStep, ProjectileMath.ContinuousTargetStart(previousTarget, ordinaryStep,
                Vector3.right * 27.5f, 0f), "a stopped world clock refreshes history instead of accumulating motion");
        }

        [Test]
        public void TheSpeedGainFollowsTheLook_FlatAndNeverNaN()
        {
            var g = ProjectileMath.SpeedGain(new Vector3(0f, 0.7f, 0.7f), 9f);
            Assert.AreEqual(9f, g.magnitude, Eps, "the gain is the full amount even when looking up");
            Assert.AreEqual(0f, g.y, Eps, "flattened: a deflect never launches you");
            Assert.AreEqual(Vector3.zero, ProjectileMath.SpeedGain(Vector3.up, 6f), "straight up gives nothing, not NaN");
            Assert.AreEqual(Vector3.zero, ProjectileMath.SpeedGain(Vector3.forward, 0f));
        }

        [Test]
        public void ReflectionPointsAtTheShooter()
        {
            var d = ProjectileMath.ReflectDirection(new Vector3(0f, 1f, 10f), new Vector3(0f, 1.2f, 0f), Vector3.back);
            Assert.Less(d.z, -0.99f);
            Assert.AreEqual(Vector3.back, ProjectileMath.ReflectDirection(Vector3.one, Vector3.one, Vector3.back), "degenerate falls back");
        }

        [Test]
        public void TheParkourEnemiesShipShooting()
        {
            var grunt = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy01"));
            var heavy = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy02"));
            if (grunt == null || heavy == null) Assert.Ignore("run 3. Create Data");
            string yaml = System.IO.File.ReadAllText(EnemyPaths.Data("pshooter_enemy01"));
            if (!yaml.Contains("shootsProjectiles:")) Assert.Ignore("3. Create Data has not been re-run since the projectile fields were added");
            foreach (var e in new[] { grunt, heavy })
            {
                Assert.IsTrue(e.shootsProjectiles, e.name + " does not shoot; the spans need it as a route");
                Assert.IsNotNull(e.projectileAttack, e.name + " has no bolt attack");
                Assert.Greater(e.projectileAttack.damage, 0f);
                Assert.IsTrue(e.rangedOnly, e.name + " is a sentry: it never melees on a span (2026-09-06)");
                // 2026-09-07 (bolt-timing plan, F5): the near edge moved 3 -> 6 m so a close bolt shows
                // ~0.44 s of flight rather than 0.36. Still well inside a Heavy's 3.6 m reach x 2, so the
                // original promise -- a sentry keeps shooting as you close, it never goes quiet -- stands.
                Assert.LessOrEqual(e.projectileMinRange, 7f, e.name + ": a sentry keeps shooting all the way in");
                Assert.GreaterOrEqual(e.projectileMaxRange, 30f, e.name + ": it wakes at the far end of the span");
                Assert.GreaterOrEqual(e.projectileLead, 0.6f, e.name + ": a runner must MEET the bolt, not outrun it");
                Assert.Greater(e.projectileHomingDegPerSec, 0f, e.name + ": a bolt that can miss is a parry you were never offered (2026-09-06)");
                Assert.GreaterOrEqual(e.projectileSpeed, 36f, e.name + ": the user asked for faster bolts");
                float launch = ProjectileMath.LaunchSpeed(e.projectileMinRange, e.projectileSpeed, Projectile.CueLead, ProjectileShooter.CueMargin);
                Assert.Greater(ProjectileMath.TimeToImpact(e.projectileMinRange, launch), Projectile.CueLead,
                    e.name + ": the nearest bolt would arrive before its cue could fire");
                Assert.Greater(e.parriedProjectileDamage, 0f); Assert.Greater(e.parrySpeedGain, 0f);
                Assert.LessOrEqual(e.projectileMaxRange, e.aggroRange + 20f);
                // The 2026-09-05 retune from play: a bolt is answered at a run, never waited for.
                Assert.GreaterOrEqual(e.projectileSpeed, 28f, e.name + ": " + e.projectileSpeed + " m/s is a bolt you stop and wait for");
                Assert.Less(ProjectileMath.TimeToImpact(15f, e.projectileSpeed), 0.55f, e.name + ": a mid-band shot must arrive inside ~half a second");
                Assert.LessOrEqual(e.projectileInterval, 2.5f, e.name + ": a runner must meet a bolt on the way through, not after stopping");
                Assert.GreaterOrEqual(e.parrySpeedGain, 8f, e.name + ": the deflect has to read as a boost on the run, close to a dash's scale");
            }
            var boss = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("Boss"));
            if (boss != null) Assert.IsFalse(boss.shootsProjectiles, "the Warden is a duel, not a span");
        }

        [Test]
        public void ANearBandShotStillCuesAfterItFires_NeverBefore()
        {
            // At the band's near edge the cue must be DUE only after the bolt exists: remaining flight at
            // fire time is above the lead, so CueDue is false on the first frame and true once the bolt
            // has closed to 0.28 s out. If minRange ever drops under speed x lead, the cue would be owed
            // before the tell exists and the parry contract breaks.
            foreach (var pair in new[] { new Vector2(10f, 32f), new Vector2(10f, 28f) })
            {
                float atFire = ProjectileMath.TimeToImpact(pair.x, pair.y);
                Assert.IsFalse(ProjectileMath.CueDue(atFire, Projectile.CueLead, false),
                    pair.y + " m/s from " + pair.x + " m: the cue is owed at fire time (" + atFire.ToString("F2") + " s of flight)");
                Assert.IsTrue(ProjectileMath.CueDue(Projectile.CueLead, Projectile.CueLead, false));
            }
        }

        [Test]
        public void ReflectedBoltsKillAParkourEnemy_InThreeOrFewer()
        {
            // 2026-09-06 (user): parkour enemies are finished by PARRYING their bolts, never by a grapple. The
            // flare on death is optional traversal. So the reflects to kill must be few enough to be a rhythm.
            foreach (var path in new[] { EnemyPaths.Data("pshooter_enemy01"), EnemyPaths.Data("pshooter_enemy02") })
            {
                var e = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
                if (e == null) Assert.Ignore("run 3. Create Data");
                if (!e.rangedOnly) continue;
                int reflectsToKill = Mathf.CeilToInt(e.maxHP / Mathf.Max(1f, e.parriedProjectileDamage));
                Assert.LessOrEqual(reflectsToKill, 3, e.name + ": " + reflectsToKill + " reflects to kill is a chore, not a rhythm");
                Assert.GreaterOrEqual(reflectsToKill, 2, e.name + ": one reflect killing it makes the bolt a free kill, not a duel");
            }
        }

        [Test]
        public void TheLaunchSlowsInsideTheCueDistance_AndNeverBeyondIt()
        {
            // 32 m/s x 0.36 s = 11.52 m. Beyond that the data speed; inside it the flight is pinned at
            // cue lead + margin, so a 3 m shot still cues 0.28 s out and flies 0.36 s in total.
            Assert.AreEqual(32f, ProjectileMath.LaunchSpeed(20f, 32f, 0.28f, 0.08f), Eps);
            float near = ProjectileMath.LaunchSpeed(3f, 32f, 0.28f, 0.08f);
            Assert.AreEqual(0.36f, ProjectileMath.TimeToImpact(3f, near), 1e-3f);
            Assert.IsFalse(ProjectileMath.CueDue(ProjectileMath.TimeToImpact(3f, near), 0.28f, false), "the cue is not owed at fire time");
            Assert.Greater(ProjectileMath.LaunchSpeed(0f, 32f, 0.28f, 0.08f), 0f, "degenerate distance is not a zero speed");
        }

        [Test]
        public void TheShotLeadsARunner_FlatAndPartial()
        {
            Vector3 muzzle = new Vector3(0f, 1.3f, 0f), chest = new Vector3(0f, 1.2f, 16f);
            Vector3 still = ProjectileMath.LeadTarget(muzzle, chest, Vector3.zero, 32f, 0.8f);
            Assert.AreEqual(0f, Vector3.Distance(still, chest), Eps, "a standing player is aimed at directly");
            Vector3 run = ProjectileMath.LeadTarget(muzzle, chest, new Vector3(10f, 5f, 0f), 32f, 0.8f);
            Assert.AreEqual(chest.y, run.y, Eps, "vertical velocity is never led: the jump arc is the player's");
            Assert.Greater(run.x, 3.5f, "at 16 m and 32 m/s the flight is ~0.5 s; 80% of 10 m/s over that is ~4 m of lead");
            Assert.Less(run.x, 5f, "and the lead is partial, so a sidestep still leaves the line");
            Vector3 full = ProjectileMath.LeadTarget(muzzle, chest, new Vector3(10f, 0f, 0f), 32f, 0f);
            Assert.AreEqual(0f, Vector3.Distance(full, chest), Eps, "lead 0 aims where they were");
        }

        [Test]
        public void TheBeatIsHeldNotReset_AndNeverRepaidAsABurst()
        {
            Assert.AreEqual(11.6f, ProjectileMath.NextBeat(10f, 10.3f, 1.6f), Eps, "a shot 0.3 s late stays on the 1.6 s grid");
            Assert.AreEqual(21.6f, ProjectileMath.NextBeat(10f, 20f, 1.6f), Eps, "a long silence re-anchors to now rather than firing a burst");
            Assert.AreEqual(11.6f, ProjectileMath.NextBeat(10f, 10f, 1.6f), Eps);
        }

        [Test]
        public void TheBoltIsTheOneGlowInTraversal()
        {
            // The project's rule is a 1.05 bloom cap on every effect. The bolt is the documented exception
            // (Projectile.HotCore): it is the attack's TELL, not the enemy. Pin both halves -- the bolt is
            // over the cap, and SlashFx's own normalisation still keeps everything else under it.
            float peak = Mathf.Max(Projectile.HotCore.r, Mathf.Max(Projectile.HotCore.g, Projectile.HotCore.b));
            Assert.AreEqual(Projectile.HotCorePeak, peak, Eps);
            Assert.Greater(peak, 1.05f, "the bolt must bloom: it is a tell answered at 32 m/s while running");
            Assert.LessOrEqual(peak, 2.0f, "over the ACES ceiling it whites out and stops reading as a ball");
            float cuePeak = Mathf.Max(Projectile.CueCore.r, Mathf.Max(Projectile.CueCore.g, Projectile.CueCore.b));
            Assert.AreEqual(peak, cuePeak, Eps, "the cue pop changes hue toward white, not brightness -- the flare is the size step");
            var normalised = SlashFx.NormaliseColor(Projectile.HotCore);
            Assert.LessOrEqual(Mathf.Max(normalised.r, Mathf.Max(normalised.g, normalised.b)), 1.0f + Eps,
                "SlashFx must still normalise: the exception is written over the material by the shooter, never by widening the cap");
            Assert.Greater(Projectile.CoreSize, 0.3f, "a 0.28 m core read as a dot at 15 m");
        }

        [Test]
        public void TheBoltsTrailAndCueAreEasierToRead()
        {
            // 2026-09-06 VFX pass, from the user: "the projectile [needs to] be a little easier to see /
            // larger". The lead already moved CoreSize/speed/homing; this pass owns the trail and the cue pop.
            Assert.Greater(Projectile.TrailSeconds, 0.12f, "up from the 0.12 s it shipped at -- a longer streak reads as a LINE, not a dot with a smear");
            Assert.Greater(Projectile.TrailWidthScale, 0.55f, "up from the 0.55 the trail shipped at -- the streak needs body as well as length");
            Assert.Greater(Projectile.CueFlareScale, 1.9f, "up from the 1.9x the cue pop shipped at -- a louder 'press now' without moving when it fires");
        }

        [Test]
        public void TheHeavySentryShipsAThreeParryPhrase_WithNoPrematurePostureBreak()
        {
            var basic = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy01"));
            var heavy = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy02"));
            var turret = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy03"));
            if (basic == null || heavy == null || turret == null) Assert.Ignore("run 3. Create Data");
            string yaml = System.IO.File.ReadAllText(EnemyPaths.Data("pshooter_enemy02"));
            if (!yaml.Contains("projectileBurstCount"))
                Assert.Ignore("run 3. Create Data after the projectile-phrase fields were added");

            Assert.AreEqual(1, basic.projectileBurstCount, "the ordinary ghost remains one read per beat");
            Assert.AreEqual(1, turret.projectileBurstCount, "the authored opening row remains five one-shot members");
            Assert.AreEqual(3, heavy.projectileBurstCount, "the Heavy Sentry's identity is three rapid parries");
            Assert.IsTrue(basic.projectileAllowTightRouteShots,
                "the blue ghost is a frequent traversal tool, so nearby parkour may not silence a clear shot");
            Assert.IsFalse(heavy.projectileAllowTightRouteShots,
                "the Heavy keeps the conservative broad flight-clearance contract");
            Assert.IsFalse(turret.projectileAllowTightRouteShots,
                "the tuned ramp Surge Turret must not inherit the blue ghost's exception");
            Assert.IsFalse(basic.projectileIgnoreDepartureSupport);
            Assert.IsTrue(heavy.projectileIgnoreDepartureSupport,
                "the Heavy keeps its broad sweep but may leave the exact perch beneath its muzzle");
            Assert.IsFalse(turret.projectileIgnoreDepartureSupport,
                "the tuned ramp Surge Turret must not inherit the Heavy's departure-only policy");
            Assert.AreEqual(0.42f, heavy.projectileBurstInterval, Eps,
                "0.28 cue + 0.08 perfect recovery + 0.06 honest slack");
            Assert.AreEqual(2.4f, heavy.projectileInterval, Eps,
                "the old interval is now the quiet cooldown after the third emission");
            Assert.AreEqual(330f, heavy.maxPosture, Eps,
                "330 is the round ceiling above all three DevBlade parries plus two completed returns");
            Assert.LessOrEqual(heavy.projectileBurstCount, ProjectileShooter.MaxBurstShots,
                "the runtime owns a fixed allocation-free phrase buffer");

            var dev = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Data/Weapons/DevBlade.asset");
            if (dev == null) Assert.Ignore("run 3. Create Data");
            float immediateParry = dev.parryPostureDamage * heavy.projectileAttack.parryPostureMultiplier;
            float afterTwoReturns = 2f * (immediateParry + heavy.parriedProjectilePosture);
            float beforeThirdReturn = 3f * immediateParry + 2f * heavy.parriedProjectilePosture;
            Assert.Less(afterTwoReturns, heavy.maxPosture, "two complete answers cannot end a three-read phrase");
            Assert.Less(beforeThirdReturn, heavy.maxPosture,
                "the third cue must remain a projectile parry, not be replaced by a deathblow prompt");
            Assert.GreaterOrEqual(heavy.parriedProjectileDamage * 3f, heavy.maxHP,
                "the third reflected return still finishes the enemy");
        }

        [Test]
        public void TightRouteClearanceKeepsTheOrdinaryGhostActive_WithoutChangingTheHeavy()
        {
            var basic = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy01"));
            var heavy = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy02"));
            if (basic == null || heavy == null) Assert.Ignore("run 3. Create Data");
            string yaml = System.IO.File.ReadAllText(EnemyPaths.Data("pshooter_enemy01"));
            if (!yaml.Contains("projectileAllowTightRouteShots"))
                Assert.Ignore("run 3. Create Data after the tight-route firing policy was added");

            GameObject enemy = null;
            GameObject player = null;
            GameObject blocker = null;
            ProjectileShooter shooter = null;
            try
            {
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.name = "TightRouteClearanceBrush";
                blocker.transform.position = new Vector3(500.85f, 101.25f, 507.5f);
                blocker.transform.localScale = new Vector3(0.2f, 0.2f, 1f);

                BuildRuntimeShooter(basic, out enemy, out player, out shooter);
                Physics.SyncTransforms();
                Assert.IsTrue(EnemyController.HasLineOfSight(enemy.transform.position + Vector3.up * 1.3f,
                                                             player.transform.position),
                    "the blocker is beside the centreline: ordinary LOS remains honestly clear");
                shooter.SetSequenceControlled(true);
                bool enteredBand;
                bool ready;
                Assert.IsNotNull(shooter.TryFireSequenceShot(true, out enteredBand, out ready));
                Assert.IsTrue(enteredBand);
                Assert.IsTrue(ready, "nearby parkour must not silence the ordinary traversal ghost");
                Assert.AreEqual(ProjectileShotReadiness.Ready, shooter.LastReadiness);

                DestroyOwnedBolts(shooter);

                // Put a thin solid brush directly across the forecast chest path. One of the broader
                // head/feet visibility rays remains clear, so this specifically proves the relaxed ghost
                // still audits its actual bolt path and cannot shoot through a wall.
                blocker.transform.position = new Vector3(500f, 101.25f, 507.5f);
                blocker.transform.localScale = new Vector3(4f, 0.18f, 0.2f);
                Physics.SyncTransforms();
                Assert.IsTrue(EnemyController.HasLineOfSight(enemy.transform.position + Vector3.up * 1.3f,
                                                             player.transform.position),
                    "a head or feet sightline stays visible so the forecast-path guard owns this rejection");
                Assert.IsNull(shooter.TryFireSequenceShot(true, out enteredBand, out ready));
                Assert.IsTrue(enteredBand);
                Assert.IsFalse(ready);
                Assert.AreEqual(ProjectileShotReadiness.BlockedFlight, shooter.LastReadiness,
                    "tight-route mode may clear a nearby rail, never a solid brush across the bolt path");

                Object.DestroyImmediate(enemy);
                Object.DestroyImmediate(player);
                enemy = null;
                player = null;
                shooter = null;

                // Restore the beside-path rail brush: Heavy should retain the old broad 1 m clearance
                // and reject this conservative case, while the ordinary ghost above accepts it.
                blocker.transform.position = new Vector3(500.85f, 101.25f, 507.5f);
                blocker.transform.localScale = new Vector3(0.2f, 0.2f, 1f);
                BuildRuntimeShooter(heavy, out enemy, out player, out shooter);
                Physics.SyncTransforms();
                shooter.SetSequenceControlled(true);
                Assert.IsNull(shooter.TryFireSequenceShot(true, out enteredBand, out ready));
                Assert.IsTrue(enteredBand);
                Assert.IsFalse(ready);
                Assert.AreEqual(ProjectileShotReadiness.BlockedFlight, shooter.LastReadiness,
                    "the Heavy's broad flight-clearance behavior is unchanged");
            }
            finally
            {
                DestroyOwnedBolts(shooter);
                if (enemy != null) Object.DestroyImmediate(enemy);
                if (player != null) Object.DestroyImmediate(player);
                if (blocker != null) Object.DestroyImmediate(blocker);
            }
        }

        [Test]
        public void HeavyCanLeaveItsOwnSupport_ButThatSupportAndSiblingGeometryStillBlockTheBoltLine()
        {
            var heavy = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy02"));
            if (heavy == null) Assert.Ignore("run 3. Create Data");
            GameObject enemy = null;
            GameObject player = null;
            GameObject support = null;
            GameObject sibling = null;
            ProjectileShooter shooter = null;
            try
            {
                BuildRuntimeShooter(heavy, out enemy, out player, out shooter);
                // Match the shipped T3 geometry: the target is below the high east perch, so the 1 m
                // broad sweep brushes the top while the actual centreline clears it.
                player.transform.position = enemy.transform.position + Vector3.forward * 15f + Vector3.down * 6f;
                support = GameObject.CreatePrimitive(PrimitiveType.Cube);
                support.name = "HeavyDepartureSupport";
                support.transform.position = enemy.transform.position + Vector3.down * 0.55f;
                support.transform.localScale = new Vector3(3f, 1f, 3f);
                Physics.SyncTransforms();

                shooter.SetSequenceControlled(true);
                bool enteredBand;
                bool ready;
                Assert.IsNotNull(shooter.TryFireSequenceShot(true, out enteredBand, out ready),
                    "a radius-only brush against the exact support beneath the Heavy must not silence its phrase");
                Assert.IsTrue(ready);
                Assert.AreEqual(ProjectileShotReadiness.Ready, shooter.LastReadiness);
                DestroyOwnedBolts(shooter);
                shooter.CancelSequencePhrase(ProjectilePhraseCancellation.SequenceReset, true);

                // A second collider in the same departure segment is never covered by the support exemption.
                sibling = GameObject.CreatePrimitive(PrimitiveType.Cube);
                sibling.name = "HeavySupportSiblingBlocker";
                sibling.transform.position = enemy.transform.position + new Vector3(0.85f, 0.5f, 0.9f);
                sibling.transform.localScale = new Vector3(0.2f, 0.2f, 0.4f);
                Physics.SyncTransforms();
                Assert.IsNull(shooter.TryFireSequenceShot(true, out enteredBand, out ready));
                Assert.AreEqual(ProjectileShotReadiness.BlockedFlight, shooter.LastReadiness,
                    "an adjacent collider may not hide behind the ignored support hit");

                Object.DestroyImmediate(sibling);
                sibling = null;
                // Extending that SAME support under the forecast makes the centreline enter it after
                // departure. The exemption is radius-only, never permission to shoot through the perch.
                support.transform.localScale = new Vector3(3f, 1f, 8f);
                Physics.SyncTransforms();
                Assert.IsFalse(InvokeFlightPathClear(enemy, player, heavy, support.GetComponent<Collider>()),
                    "the departure exception itself must reject the selected support on the bolt centreline");
                Assert.IsNull(shooter.TryFireSequenceShot(true, out enteredBand, out ready));
                Assert.That(shooter.LastReadiness,
                    Is.EqualTo(ProjectileShotReadiness.BlockedFlight)
                        .Or.EqualTo(ProjectileShotReadiness.NoLineOfSight),
                    "the exact support still blocks, regardless of which conservative gate sees it first");

                // An overhanging/tall support can contain the muzzle at launch. Casts that begin inside a
                // collider are not a dependable obstruction signal, so the departure exception must fail
                // closed before forecasting instead of granting permission to shoot outward through it.
                support.transform.position = enemy.transform.position + Vector3.up * 0.8f;
                support.transform.localScale = new Vector3(3f, 4f, 3f);
                Physics.SyncTransforms();
                Assert.IsFalse(InvokeFlightPathClear(enemy, player, heavy, support.GetComponent<Collider>()),
                    "the low-level forecast must fail closed when its ignored support encloses launch");
                Assert.IsNull(shooter.TryFireSequenceShot(true, out enteredBand, out ready));
                Assert.That(shooter.LastReadiness,
                    Is.EqualTo(ProjectileShotReadiness.BlockedFlight)
                        .Or.EqualTo(ProjectileShotReadiness.NoLineOfSight),
                    "a selected support enclosing the launch centre is solid geometry, never a radius brush");
            }
            finally
            {
                DestroyOwnedBolts(shooter);
                if (enemy != null) Object.DestroyImmediate(enemy);
                if (player != null) Object.DestroyImmediate(player);
                if (support != null) Object.DestroyImmediate(support);
                if (sibling != null) Object.DestroyImmediate(sibling);
            }
        }

        [Test]
        public void HeavySentryRuntimeOwnsExactlyThreeEmissions_ThenTheFullQuietCooldown()
        {
            var heavy = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy02"));
            if (heavy == null) Assert.Ignore("run 3. Create Data");
            if (!System.IO.File.ReadAllText(EnemyPaths.Data("pshooter_enemy02")).Contains("projectileBurstCount"))
                Assert.Ignore("run 3. Create Data after the projectile-phrase fields were added");

            GameObject enemy = null;
            GameObject player = null;
            ProjectileShooter shooter = null;
            try
            {
                BuildRuntimeShooter(heavy, out enemy, out player, out shooter);
                shooter.SetSequenceControlled(true);

                bool enteredBand;
                bool ready;
                Projectile first = shooter.TryFireSequenceShot(true, out enteredBand, out ready);
                Assert.IsTrue(enteredBand);
                Assert.IsTrue(ready);
                Assert.IsNotNull(first);
                Assert.AreEqual(1, shooter.Fired);
                Assert.IsTrue(shooter.PhraseActive);

                // Resolve each preceding incoming obligation like a clean deflect, then open the next
                // deterministic contact slot. UpdatePhrase remains the production emission path.
                ResolveOwnedIncomingAndOpenSlot(shooter);
                InvokeShooter(shooter, "UpdatePhrase");
                Assert.AreEqual(2, shooter.Fired);
                Assert.IsTrue(shooter.PhraseActive);

                ResolveOwnedIncomingAndOpenSlot(shooter);
                InvokeShooter(shooter, "UpdatePhrase");
                Assert.AreEqual(3, shooter.Fired);
                Assert.IsFalse(shooter.PhraseActive);
                Assert.IsTrue(shooter.PhraseEmissionsComplete);
                Assert.AreEqual(ProjectilePhraseCancellation.None, shooter.LastPhraseCancellation);

                float nextFire = (float)ShooterField("nextFireAt").GetValue(shooter);
                Assert.That(nextFire - Time.time, Is.EqualTo(heavy.projectileInterval).Within(0.05f),
                    "the 2.4 s quiet starts after the third emission, not at phrase start");
                shooter.SetSequenceControlled(false);
                InvokeShooter(shooter, "Update");
                Assert.AreEqual(3, shooter.Fired, "there is no fourth shot before the post-phrase cooldown");
            }
            finally
            {
                DestroyOwnedBolts(shooter);
                if (enemy != null) Object.DestroyImmediate(enemy);
                if (player != null) Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void CancelledHeavyPhraseReportsWhy_AndNeverRepaysMissingShotsAsACatchupBurst()
        {
            var heavy = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy02"));
            if (heavy == null) Assert.Ignore("run 3. Create Data");
            if (!System.IO.File.ReadAllText(EnemyPaths.Data("pshooter_enemy02")).Contains("projectileBurstCount"))
                Assert.Ignore("run 3. Create Data after the projectile-phrase fields were added");

            GameObject enemy = null;
            GameObject player = null;
            ProjectileShooter shooter = null;
            try
            {
                BuildRuntimeShooter(heavy, out enemy, out player, out shooter);
                shooter.SetSequenceControlled(true);
                bool enteredBand;
                bool ready;
                Assert.IsNotNull(shooter.TryFireSequenceShot(true, out enteredBand, out ready));

                // Leave the authored firing band before shot two. Force only the scheduling window; the
                // production validation must still see the real out-of-band target and cancel the phrase.
                player.transform.position = enemy.transform.position + Vector3.forward * 80f;
                ShooterField("nextPhraseContactAt").SetValue(shooter, Time.time);
                ShooterField("followupDeadlineAt").SetValue(shooter, Time.time + 1f);
                InvokeShooter(shooter, "UpdatePhrase");

                Assert.AreEqual(1, shooter.Fired);
                Assert.IsFalse(shooter.PhraseActive);
                Assert.IsTrue(shooter.PhraseEmissionsComplete);
                Assert.AreEqual(ProjectilePhraseCancellation.OutOfBand, shooter.LastPhraseCancellation);
                Assert.AreEqual(1, shooter.PhraseCancellationCount);

                player.transform.position = enemy.transform.position + Vector3.forward * 15f;
                shooter.SetSequenceControlled(false);
                InvokeShooter(shooter, "Update");
                Assert.AreEqual(1, shooter.Fired,
                    "re-entering the route may start a later phrase after cooldown, never repay two missed emissions now");
            }
            finally
            {
                DestroyOwnedBolts(shooter);
                if (enemy != null) Object.DestroyImmediate(enemy);
                if (player != null) Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void TheIncomingWeaveIsBoundedDeterministicAndSettlesBeforeTheCue()
        {
            float phase = ProjectileVisualMath.Phase(17);
            Assert.AreEqual(phase, ProjectileVisualMath.Phase(17), Eps, "the same shot is reproducible");
            Assert.AreNotEqual(phase, ProjectileVisualMath.Phase(18), "successive bolts should not trace the same curve");

            Vector3 atMuzzle = ProjectileVisualMath.WeaveOffset(Vector3.forward, 0f, 0.8f,
                Projectile.CueLead, phase, Projectile.WeaveAmplitude, Projectile.WeaveFadeInSeconds,
                Projectile.WeaveFadeOutSeconds, true);
            Assert.AreEqual(Vector3.zero, atMuzzle, "the visual must not pop sideways on its first frame");

            for (int i = 0; i <= 100; i++)
            {
                float age = i * 0.01f;
                Vector3 offset = ProjectileVisualMath.WeaveOffset(new Vector3(0.2f, -0.1f, 1f), age, 0.8f,
                    Projectile.CueLead, phase, Projectile.WeaveAmplitude, Projectile.WeaveFadeInSeconds,
                    Projectile.WeaveFadeOutSeconds, true);
                Assert.LessOrEqual(offset.magnitude, Projectile.WeaveAmplitude + Eps, "visual curve exceeded its hard displacement cap");
            }

            Assert.AreEqual(Vector3.zero, ProjectileVisualMath.WeaveOffset(Vector3.forward, 0.4f, Projectile.CueLead,
                Projectile.CueLead, phase, Projectile.WeaveAmplitude, Projectile.WeaveFadeInSeconds,
                Projectile.WeaveFadeOutSeconds, true), "the visual must reach the logical line at cue onset");
            Assert.AreEqual(Vector3.zero, ProjectileVisualMath.WeaveOffset(Vector3.forward, 0.5f, 0.1f,
                Projectile.CueLead, phase, Projectile.WeaveAmplitude, Projectile.WeaveFadeInSeconds,
                Projectile.WeaveFadeOutSeconds, true), "the entire final cue window must stay honest");
            Assert.AreEqual(Vector3.zero, ProjectileVisualMath.WeaveOffset(Vector3.forward, 0.2f, 1f,
                Projectile.CueLead, phase, Projectile.WeaveAmplitude, Projectile.WeaveFadeInSeconds,
                Projectile.WeaveFadeOutSeconds, false), "a reflected bolt must immediately fly straight");
        }

        [Test]
        public void OnlyAChildVisualMayLeaveTheLogicalRoot()
        {
            var root = new GameObject("logical bolt");
            var child = new GameObject("Core");
            var unrelated = new GameObject("unrelated renderer");
            try
            {
                child.transform.SetParent(root.transform, false);
                Assert.IsFalse(ProjectileVisualMath.CanOffset(root.transform, root.transform),
                    "a direct/legacy renderer on the root must stay on the collision path");
                Assert.IsTrue(ProjectileVisualMath.CanOffset(root.transform, child.transform));
                Assert.IsFalse(ProjectileVisualMath.CanOffset(root.transform, null));
                Assert.IsFalse(ProjectileVisualMath.CanOffset(root.transform, unrelated.transform),
                    "an unrelated transform is not the bolt's presentation child");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(unrelated);
            }
        }

        [Test]
        public void LowFrameRateTrailSamplesEachCrossingInsteadOfRepeatingTheHead()
        {
            var history = new Vector3[4];
            float timer = 0f;
            ProjectileVisualMath.RecordTrail(Vector3.zero, new Vector3(5f, 0f, 0f), 0.05f, 0.02f,
                                             ref timer, history);

            Assert.LessOrEqual(Vector3.Distance(new Vector3(5f, 0f, 0f), history[0]), Eps,
                "slot zero is the live rendered head");
            Assert.LessOrEqual(Vector3.Distance(new Vector3(4f, 0f, 0f), history[1]), Eps,
                "second crossing in the 20 fps frame");
            Assert.LessOrEqual(Vector3.Distance(new Vector3(2f, 0f, 0f), history[2]), Eps,
                "first crossing in the 20 fps frame");
            Assert.AreNotEqual(history[1], history[2], "two crossed intervals may not collapse into one repeated point");
            Assert.AreEqual(0.01f, timer, Eps);
        }

        [Test]
        public void EveryEnemyPrefabCarriesTheShooter()
        {
            foreach (var n in new[] { "pshooter_enemy01", "pshooter_enemy02" })
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + n + ".prefab");
                if (p == null) Assert.Ignore("run 4. Build Prefabs");
                if (p.GetComponent<ProjectileShooter>() == null)
                    Assert.Ignore(n + " was built before ProjectileShooter existed; run 4. Build Prefabs");
                Assert.IsNotNull(p.GetComponent<EnemyController>());
            }
        }

        static void BuildRuntimeShooter(EnemyData data, out GameObject enemy, out GameObject player,
                                        out ProjectileShooter shooter)
        {
            enemy = new GameObject("HeavyPhraseEnemy");
            enemy.transform.position = new Vector3(500f, 100f, 500f);
            enemy.layer = Layers.Enemy;
            var controller = enemy.AddComponent<EnemyController>();
            controller.data = data;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(EnemyController).GetField("<Current>k__BackingField", flags)
                .SetValue(controller, EnemyController.State.Chase);
            shooter = enemy.AddComponent<ProjectileShooter>();
            // EditMode AddComponent does not guarantee MonoBehaviour.Awake. Production always has this
            // lifecycle edge; the isolated fixture must establish the same controller reference.
            InvokeShooter(shooter, "Awake");

            player = new GameObject("HeavyPhrasePlayer");
            player.transform.position = enemy.transform.position + Vector3.forward * 15f;
            player.transform.forward = Vector3.back; // the ordinary blue sentry's source is behind this fixture player
            player.layer = Layers.Player;
            player.AddComponent<Health>();
            var playerCombat = player.AddComponent<PlayerCombat>();
            // The shipped Level_01 scene also contains a PlayerCombat. Pin this isolated runtime fixture
            // to its own target instead of relying on FindAnyObjectByType's undefined scene ordering.
            typeof(ProjectileShooter).GetField("combat", flags).SetValue(shooter, playerCombat);
            typeof(ProjectileShooter).GetField("motor", flags).SetValue(shooter, null);
            Physics.SyncTransforms();
        }

        static System.Reflection.FieldInfo ShooterField(string name)
        {
            return typeof(ProjectileShooter).GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        }

        static void InvokeShooter(ProjectileShooter shooter, string method)
        {
            typeof(ProjectileShooter).GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(shooter, null);
        }

        static void ResolveOwnedIncomingAndOpenSlot(ProjectileShooter shooter)
        {
            var bolts = (Projectile[])ShooterField("phraseBolts").GetValue(shooter);
            var reflected = typeof(Projectile).GetField("reflected",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            for (int i = 0; i < shooter.PhraseShotsEmitted; i++)
                if (bolts[i] != null) reflected.SetValue(bolts[i], true);
            ShooterField("nextPhraseContactAt").SetValue(shooter, Time.time);
            ShooterField("followupDeadlineAt").SetValue(shooter, Time.time + 1f);
        }

        static void DestroyOwnedBolts(ProjectileShooter shooter)
        {
            if (shooter == null) return;
            var field = ShooterField("phraseBolts");
            var bolts = field != null ? field.GetValue(shooter) as Projectile[] : null;
            if (bolts == null) return;
            for (int i = 0; i < bolts.Length; i++)
                if (bolts[i] != null) Object.DestroyImmediate(bolts[i].gameObject);
        }

        static bool InvokeFlightPathClear(GameObject enemy, GameObject player, EnemyData data,
                                          Collider departureSupport)
        {
            Vector3 muzzle = enemy.transform.position + Vector3.up * 1.3f;
            Vector3 chest = player.transform.position + Vector3.up * 1.2f;
            ProjectileFlightPlan plan = ProjectileFlightMath.Plan(muzzle, chest, Vector3.zero,
                data.projectileSpeed, data.projectileLead, data.projectileHomingDegPerSec,
                ProjectileShooter.SpawnForwardOffset, Projectile.DefaultHitRadius,
                Projectile.CueLead + ProjectileShooter.CueMargin, Projectile.DefaultMaxLife);
            var method = typeof(ProjectileShooter).GetMethod("FlightPathClear",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            return (bool)method.Invoke(null, new object[] {
                plan, chest, Vector3.zero, data.projectileHomingDegPerSec,
                Projectile.DefaultHitRadius, departureSupport
            });
        }
    }
}
