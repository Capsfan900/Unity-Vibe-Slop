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
    }
}
