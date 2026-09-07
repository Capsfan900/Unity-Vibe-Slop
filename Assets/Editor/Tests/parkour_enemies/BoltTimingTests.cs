using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The bolt-timing pass (plan <c>docs/plans/bolt-timing-2026-09-06.md</c>), from the lead's play:
    /// *"there are certain portions where the projectile just comes in at a bad time ... the placement and
    /// timing of the shots needs to work with the game as well so the player can actually make the parrys
    /// while moving fast."* Arrival TIMING, not difficulty. Four laws, all pure:
    ///
    /// <list type="bullet">
    /// <item>F1 <see cref="ProjectileMath.AcquireBeat"/> — a sentry takes a breath when it acquires you.</item>
    /// <item>F2 <see cref="BoltRegistry"/> — a bolt in flight is an incoming attack.</item>
    /// <item>F3 <see cref="ProjectileMath.ArrivesInFront"/> — no bolt at a fleeing back.</item>
    /// <item>F4 <see cref="ProjectileMath.FirstBeat"/> — one beat per span, not one per spawn frame.</item>
    /// <item>F5 — the shipped near-flight numbers (rule 9: asserted on the ASSET, not the initialiser).</item>
    /// </list>
    /// </summary>
    public class BoltTimingTests
    {
        const float Eps = 1e-4f;

        // ---- F1: the arm-up ------------------------------------------------------------------------

        [Test]
        public void AcquiringTakesABreath_AndAStaleBeatNeverFiresOnTheFrameItSeesYou()
        {
            // The bug: the beat is HELD while the line is blocked, so after seconds out of sight it is in
            // the PAST and the shot leaves on the first frame line of sight is established.
            Assert.AreEqual(100.7f, ProjectileMath.AcquireBeat(90f, 100f, 1.6f, 0.7f), Eps,
                "a beat owed from ten seconds ago must be pushed a full arm-up out, not fired at once");
            Assert.AreEqual(100.7f, ProjectileMath.AcquireBeat(100f, 100f, 1.6f, 0.7f), Eps,
                "a beat due exactly now still owes the breath");
        }

        [Test]
        public void AShooterAlreadyInBandKeepsItsBeatExactly()
        {
            // The arm-up only ever moves a beat FORWARD, so a sentry you are already running past is not
            // slowed down, and re-acquiring can never grant a free early shot.
            Assert.AreEqual(101.4f, ProjectileMath.AcquireBeat(101.4f, 100f, 1.6f, 0.7f), Eps,
                "a beat already later than the arm-up stands untouched");
            Assert.AreEqual(100.71f, ProjectileMath.AcquireBeat(100.71f, 100f, 1.6f, 0.7f), Eps,
                "...even by a hundredth");
            Assert.AreEqual(100f, ProjectileMath.AcquireBeat(90f, 100f, 1.6f, 0f), Eps,
                "a zero delay is the old behaviour exactly");
        }

        [Test]
        public void TheArmUpNeverCostsMoreThanOneBolt()
        {
            // Capped at one interval: a mis-set delay can silence a perch for a breath, never for a span.
            Assert.AreEqual(101.1f, ProjectileMath.AcquireBeat(0f, 100f, 1.1f, 5f), Eps,
                "the delay is clamped to one interval");
            Assert.AreEqual(105f, ProjectileMath.AcquireBeat(0f, 100f, 0f, 5f), Eps,
                "a degenerate interval does not clamp the delay to zero");
        }

        [Test]
        public void TheArmUpIsShipped_OnEveryEnemyThatShoots()
        {
            // Rule 9: the field's initialiser proves nothing. These are the values in the ASSET.
            foreach (var n in new[] { "pshooter_enemy01", "pshooter_enemy02", "pshooter_enemy03" })
            {
                var e = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(n));
                if (e == null) Assert.Ignore("run 3. Create Data");
                string yaml = System.IO.File.ReadAllText(EnemyPaths.Data(n));
                if (!yaml.Contains("projectileAcquireDelay"))
                    Assert.Ignore("3. Create Data has not been re-run since the arm-up field was added");
                Assert.GreaterOrEqual(e.projectileAcquireDelay, Projectile.CueLead,
                    n + ": an arm-up shorter than the cue lead is not a breath, it is the old bug");
                Assert.LessOrEqual(e.projectileAcquireDelay, e.projectileInterval,
                    n + ": an arm-up longer than the beat would cost more than one bolt");
            }
        }

        // ---- F2: a bolt in flight is an incoming attack --------------------------------------------

        [Test]
        public void ABoltInFlightIsTheEarliestCue_AndASpentOneIsNot()
        {
            BoltRegistry.Reset();
            Assert.AreEqual(float.MaxValue, BoltRegistry.EarliestCueTime(200f), "an empty span has no cue");
            Assert.IsFalse(BoltRegistry.AnyImpactBefore(200f));

            // A bolt 0.4 s out at t=100: it cues 0.12 s from now (0.4 - the 0.28 lead) and lands at 100.4.
            BoltRegistry.Report(1, 100.12f, 100.4f);
            BoltRegistry.Report(2, 101.30f, 101.58f);
            Assert.AreEqual(2, BoltRegistry.Count);
            Assert.AreEqual(100.12f, BoltRegistry.EarliestCueTime(100.6f), Eps,
                "the nearer bolt is the cue the player's recovery must end before");
            Assert.IsTrue(BoltRegistry.AnyImpactBefore(100.6f),
                "a missed bolt parry is a MISTIME (0.2 s), not the whiff tax (0.5 s)");
            Assert.IsFalse(BoltRegistry.AnyImpactBefore(100.3f), "...but only inside the lookahead");
            Assert.AreEqual(float.MaxValue, BoltRegistry.EarliestCueTime(100.05f),
                "a cue beyond the lookahead is not reported");

            BoltRegistry.Clear(1);
            Assert.AreEqual(101.30f, BoltRegistry.EarliestCueTime(200f), Eps,
                "a spent or reflected bolt is not incoming");
            BoltRegistry.Clear(2);
            Assert.AreEqual(0, BoltRegistry.Count);
            Assert.AreEqual(float.MaxValue, BoltRegistry.EarliestCueTime(200f));
            Assert.IsFalse(BoltRegistry.AnyImpactBefore(200f));
        }

        [Test]
        public void ABoltThatHasAlreadyCuedStillLands_ButOffersNoSecondCue()
        {
            BoltRegistry.Reset();
            // Past its cue: Projectile reports MaxValue for the cue and keeps the impact, exactly as
            // EnemyController.NextCueTime does for a melee attack that has already cued.
            BoltRegistry.Report(7, float.MaxValue, 100.15f);
            Assert.AreEqual(float.MaxValue, BoltRegistry.EarliestCueTime(200f), "no cue is owed twice");
            Assert.IsTrue(BoltRegistry.AnyImpactBefore(100.6f), "the hit is still coming");
            BoltRegistry.Reset();
        }

        [Test]
        public void ReportUpdatesABoltInPlace_RatherThanStackingForecasts()
        {
            BoltRegistry.Reset();
            BoltRegistry.Report(3, 100.4f, 100.68f);
            BoltRegistry.Report(3, 100.2f, 100.48f);
            Assert.AreEqual(1, BoltRegistry.Count, "one bolt reporting every frame is one entry");
            Assert.AreEqual(100.2f, BoltRegistry.EarliestCueTime(200f), Eps);
            BoltRegistry.Clear(99);   // clearing an unknown id is harmless
            Assert.AreEqual(1, BoltRegistry.Count);
            BoltRegistry.Reset();
        }

        // ---- F3: no bolt at a fleeing back ---------------------------------------------------------

        [Test]
        public void NoBoltIsLaunchedAtAFleeingBack()
        {
            Vector3 muzzle = new Vector3(0f, 1.3f, 0f), chest = new Vector3(0f, 1.2f, 15f);
            Assert.IsFalse(ProjectileMath.ArrivesInFront(muzzle, chest, new Vector3(0f, 0f, 11f), 40f, 75f),
                "running straight away: the bolt arrives from behind and ParryMath.Evaluate returns Hit before timing");
            Assert.IsFalse(ProjectileMath.ArrivesInFront(muzzle, chest, new Vector3(11f, 0f, 11f), 40f, 75f),
                "running away at 45 degrees is still a back");
        }

        [Test]
        public void ARunnerCrossingOrClosingIsStillShotAt()
        {
            Vector3 muzzle = new Vector3(0f, 1.3f, 0f), chest = new Vector3(0f, 1.2f, 15f);
            Assert.IsTrue(ProjectileMath.ArrivesInFront(muzzle, chest, new Vector3(0f, 0f, -11f), 40f, 75f),
                "running at the perch");
            Assert.IsTrue(ProjectileMath.ArrivesInFront(muzzle, chest, new Vector3(11f, 0f, 0f), 40f, 75f),
                "crossing the arc is the span working as intended, not a fleeing back");
            Assert.IsTrue(ProjectileMath.ArrivesInFront(muzzle, chest, new Vector3(11f, 0f, 1f), 40f, 75f),
                "a nearly perpendicular run is crossing, not fleeing");
            Assert.IsTrue(ProjectileMath.ArrivesInFront(muzzle, chest, Vector3.zero, 40f, 75f),
                "a standing player's velocity is no proxy for their look, so the shot is never refused");
            Assert.IsTrue(ProjectileMath.ArrivesInFront(muzzle, chest, new Vector3(0f, 0f, 0.5f), 40f, 75f),
                "walking pace is not fleeing");
            Assert.IsTrue(ProjectileMath.ArrivesInFront(muzzle, muzzle, new Vector3(0f, 0f, 11f), 40f, 75f),
                "degenerate geometry fires rather than throwing");
            Assert.IsTrue(ProjectileMath.ArrivesInFront(muzzle, chest, new Vector3(0f, 22f, 0f), 40f, 75f),
                "vertical speed is not a heading: a jump is not a flight");
        }

        [Test]
        public void TheGateAgreesWithTheParryItProtects()
        {
            // The whole point: what F3 refuses is exactly what ParryMath would have scored a Hit on facing
            // alone. Judged as PlayerCombat does -- SourceDirection off the bolt's travel, IsFacing against
            // the flat heading -- so the two rules can never drift apart.
            Vector3 muzzle = new Vector3(0f, 1.3f, 0f), chest = new Vector3(0f, 1.2f, 15f);
            Vector3 fleeing = new Vector3(0f, 0f, 11f);
            Vector3 travel = (chest + fleeing * 0.5f) - muzzle;
            bool facing = ParryMath.IsFacing(fleeing, ParryMath.SourceDirection(travel, muzzle, chest), 75f);
            Assert.IsFalse(facing, "the parry would have been vetoed on facing");
            Assert.IsFalse(ProjectileMath.ArrivesInFront(muzzle, chest, fleeing, 40f, 75f),
                "...so the shot is never launched");
        }

        // ---- F4: one beat per span -----------------------------------------------------------------

        [Test]
        public void TwoSentriesOnOneSpanNeverFireTogether()
        {
            // Anchored to a shared epoch, alternate sentries sit a HALF interval apart -- two perches
            // covering one crest are a tempo, not two clocks arguing. 0.8 s at the Grunt's 1.6 s beat is
            // wider than the whiff recovery (0.5 s), so the second cue is always answerable.
            const float epoch = 100f, now = 100f, interval = 1.6f, minDelay = 1f;
            float a = ProjectileMath.FirstBeat(epoch, now, interval, 0f, minDelay);
            float b = ProjectileMath.FirstBeat(epoch, now, interval, interval * 0.5f, minDelay);
            Assert.AreEqual(101.6f, a, Eps, "the first beat clears the spawn delay and stays on the grid");
            Assert.AreEqual(102.4f, b, Eps,
                "the offset sentry sits a half beat off the same grid (100.8 is inside the spawn delay, so it takes the next slot)");
            float gap = Mathf.Abs(a - b);
            var stats = AssetDatabase.LoadAssetAtPath<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            float whiff = stats != null ? stats.parryWhiffRecovery : 0.5f;
            Assert.GreaterOrEqual(gap, whiff,
                "two bolts closer together than the whiff recovery is the unanswerable case");
            Assert.GreaterOrEqual(a, now + minDelay, "never on the frame of a spawn");
            Assert.GreaterOrEqual(b - interval, now, "the offset beat is not pulled into the past either");
        }

        [Test]
        public void TheFirstBeatWalksTheGridForwardWhateverTheEpochIs()
        {
            Assert.AreEqual(112f, ProjectileMath.FirstBeat(100f, 110f, 2f, 0f, 1f), Eps,
                "an old epoch is advanced by whole intervals, never fired at once");
            Assert.AreEqual(105f, ProjectileMath.FirstBeat(105f, 100f, 2f, 0f, 1f), Eps,
                "a beat already ahead of the delay stands");
            Assert.AreEqual(101f, ProjectileMath.FirstBeat(0f, 100f, 0f, 0f, 1f), Eps,
                "a degenerate interval falls back to the spawn delay");
        }

        // ---- F5: a longer minimum flight up close --------------------------------------------------

        [Test]
        public void ANearBoltShowsMoreFlight_AndTheCueLeadIsStillFlat()
        {
            Assert.AreEqual(0.16f, ProjectileShooter.CueMargin, Eps,
                "F5: up from 0.08, so the nearest bolt flies 0.44 s instead of 0.36");
            Assert.AreEqual(0.28f, Projectile.CueLead, Eps,
                "the cue lead is a contract shared with every melee attack -- the plan REJECTS scaling it with speed");
            float near = ProjectileMath.LaunchSpeed(6f, 40f, Projectile.CueLead, ProjectileShooter.CueMargin);
            Assert.AreEqual(0.44f, ProjectileMath.TimeToImpact(6f, near), 1e-3f,
                "a bolt from the new near edge flies the lead plus the margin");
            Assert.IsFalse(ProjectileMath.CueDue(ProjectileMath.TimeToImpact(6f, near), Projectile.CueLead, false),
                "the cue is still never owed before the bolt exists");
        }

        [Test]
        public void TheSentriesShipTheLongerNearFlight()
        {
            foreach (var n in new[] { "pshooter_enemy01", "pshooter_enemy02" })
            {
                var e = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(n));
                if (e == null) Assert.Ignore("run 3. Create Data");
                Assert.GreaterOrEqual(e.projectileMinRange, 6f,
                    n + ": F5 moved the near edge 3 -> 6 m so a close bolt is not a flinch");
                float launch = ProjectileMath.LaunchSpeed(e.projectileMinRange, e.projectileSpeed,
                                                          Projectile.CueLead, ProjectileShooter.CueMargin);
                float flight = ProjectileMath.TimeToImpact(e.projectileMinRange, launch);
                Assert.GreaterOrEqual(flight, Projectile.CueLead + ProjectileShooter.CueMargin - 1e-3f,
                    n + ": the nearest bolt must still show the lead plus the margin");
                Assert.Less(e.projectileMinRange, 3.6f * 2f,
                    n + ": a near edge past twice a Heavy's reach would make a perch toothless up close");
            }
        }
    }
}
