using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <c>pshooter_enemy03</c>, the SURGE TURRET (2026-09-06, the user's ask): a small round turret that
    /// shoots, dies in one hit, and makes the player faster for every bolt they deflect.
    ///
    /// <para>Three things are pinned here and each one is a rule someone could break by accident later:
    /// the surge always walks back to exactly 1 (a stuck <c>SpeedMultiplier</c> is the worst bug this
    /// feature can have), it dies to ONE touch from anything and never opens a duel, and its bolt is the
    /// same bolt with the same cue lead as every other bolt on a span.</para>
    ///
    /// <para>Asset checks Ignore until <c>3. Create Data</c> / <c>4. Build Prefabs</c> have run; the maths
    /// runs anywhere. Rule 9: every data assertion reads the SHIPPED ASSET, never a field initialiser.</para>
    /// </summary>
    public class SurgeTurretTests
    {
        const float Eps = 1e-4f;
        const string Path = "Assets/Prefabs/pshooter_enemy03.prefab";

        static EnemyData Data()
        {
            var d = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy03"));
            if (d == null) Assert.Ignore("run 3. Create Data");
            if (d.parrySurgeMaxStacks <= 0 && d.parrySurgeStep <= 0f)
                Assert.Ignore("3. Create Data has not been re-run since the parry-surge fields were added");
            return d;
        }

        // ---------------------------------------------------------------- the maths

        [Test]
        public void AParryAddsOneStack_AndTheCeilingHolds()
        {
            Assert.AreEqual(1, SurgeMath.Grant(0, 5));
            Assert.AreEqual(5, SurgeMath.Grant(4, 5));
            Assert.AreEqual(5, SurgeMath.Grant(5, 5), "the ceiling is a ceiling: parrying a sixth pays nothing more");
            Assert.AreEqual(0, SurgeMath.Grant(3, 0), "an enemy that pays no surge can never grant one");
        }

        [Test]
        public void TheMultiplierIsExactlyOneWithNoStacks()
        {
            // The motor's shipped speeds must be bit-for-bit what an unsurged player gets, or every
            // movement number in the project quietly changes the moment this feature exists.
            Assert.AreEqual(1f, SurgeMath.Multiplier(0, 0.12f), 0f);
            Assert.AreEqual(1f, SurgeMath.Multiplier(3, 0f), 0f, "a zero step is inert, not a NaN");
            Assert.AreEqual(1f, SurgeMath.Multiplier(-2, 0.12f), 0f);
            Assert.AreEqual(1.12f, SurgeMath.Multiplier(1, 0.12f), Eps);
            Assert.AreEqual(1.60f, SurgeMath.Multiplier(5, 0.12f), Eps);
        }

        [Test]
        public void StacksFallOneAtATime_AndAlwaysReachZero()
        {
            // The decay is the safety story: from any state, with no further parries, the ladder walks
            // DOWN to zero on its own. A cliff (all stacks at once) would be both unreadable on the HUD
            // and unforgiving; a decay that could stall would leave the player permanently fast.
            int stacks = 5;
            float now = 0f, nextDrop = SurgeMath.NextDropTime(now, 2f);
            int drops = 0;
            for (int frame = 0; frame < 2000 && stacks > 0; frame++)
            {
                now += 1f / 60f;
                if (!SurgeMath.DropDue(stacks, now, nextDrop)) continue;
                stacks = SurgeMath.Drop(stacks);
                nextDrop = SurgeMath.NextDropTime(now, 2f);
                drops++;
            }
            Assert.AreEqual(0, stacks, "the surge must reach exactly zero on its own");
            Assert.AreEqual(5, drops, "one stack at a time, five times -- never a cliff");
            Assert.Less(now, SurgeMath.FullDecaySeconds(5, 2f) + 0.2f, "and it takes about maxStacks x stackSeconds");
            Assert.IsFalse(SurgeMath.DropDue(0, 9999f, 0f), "an idle surge does nothing forever");
            Assert.AreEqual(0, SurgeMath.Drop(0), "never negative");
        }

        // ---------------------------------------------------------------- the shipped data

        [Test]
        public void TheTurretPaysASurge_AndTheCeilingIsSomethingAHumanCanStillAim()
        {
            var d = Data();
            Assert.Greater(d.parrySurgeStep, 0f, "the whole enemy is the surge; without it this is a worse sentry");
            Assert.GreaterOrEqual(d.parrySurgeMaxStacks, 3, "a row has to read as a LADDER, not as a switch that flips on the second one");
            float ceiling = SurgeMath.MaxMultiplier(d.parrySurgeMaxStacks, d.parrySurgeStep);
            Assert.Greater(ceiling, 1.3f, "under ~x1.3 a whole row of parries is not visibly worth doing");
            Assert.LessOrEqual(ceiling, 1.7f,
                "past ~x1.7 the level's jump arcs and the motor's air control stop being something a human can aim");
            // One parry must be FELT and must not be a jolt: on a groundSpeed of 11 this is +1.3 m/s.
            Assert.GreaterOrEqual(d.parrySurgeStep * 11f, 0.9f, "a single deflect has to register");
            Assert.LessOrEqual(d.parrySurgeStep * 11f, 2.5f, "one lucky parry must not be worth more than the four that follow it");
        }

        [Test]
        public void TheSurgeFallsOffIfYouStopParrying_ButNotBetweenTwoHonestOnes()
        {
            var d = Data();
            Assert.That(d.parrySurgeSeconds, Is.EqualTo(1.4f).Within(Eps),
                "the current wider route needs a 7.0 s full-ladder carry, not an unbounded or distance-based rule");
            Assert.Greater(d.parrySurgeSeconds, d.projectileInterval,
                "a stack must not bleed away inside one bolt metronome: that would punish a clean parry");
            // Grant resets the drop timer, so this number never governs a player who is still inside a
            // turret's metronome -- it governs the RUN-OUT after the last one. 1.4 s keeps 0.3 s of slack
            // over the 1.1 s beat, while five stacks clear in 7.0 s instead of leaking farther down-route.
            Assert.Greater(d.parrySurgeSeconds - d.projectileInterval, 0.25f,
                "under ~0.25 s of slack over the bolt metronome, a parry that lands a fraction late bleeds a "
                + "stack and the ladder reads as random rather than as 'keep parrying'");
            float full = SurgeMath.FullDecaySeconds(d.parrySurgeMaxStacks, d.parrySurgeSeconds);
            Assert.That(full, Is.EqualTo(7f).Within(Eps), "five 1.4 s stacks must clear predictably");
            Assert.LessOrEqual(full, 9f,
                "a reward that outlives its span is just a buff -- the whole ladder must be gone within a span's length");
            Assert.GreaterOrEqual(full, 5f, "and it must survive the gap between two turrets on a ramp");
        }

        [Test]
        public void OneHitKillsIt_AndItNeverOpensADuel()
        {
            var d = Data();
            Assert.LessOrEqual(d.maxHP, 1f, "the user's word was 'dies in one hit'; 1 HP is the only value every damage source clears");
            Assert.Greater(d.maxHP, 0f, "a zero-HP body is a divide Health has never been asked to do");
            Assert.GreaterOrEqual(d.parriedProjectileDamage, d.maxHP,
                "its own reflected bolt must finish it: the parry IS the kill");
            // Posture out of reach. The biggest single parry in the game is the dev blade's 60 x a 1.5
            // parryPostureMultiplier = 90. If posture could break, the turret would raise a DEATHBLOW glyph
            // for the ~0.3 s its own bolt is flying home -- a duel prompt on a body that is already dead.
            Assert.Greater(d.maxPosture, 90f * 1.5f,
                "posture must stay unreachable: this is a target, not a duel");
            Assert.IsTrue(d.rangedOnly, "it never melees");
            Assert.AreEqual(0f, d.flaskPunishChance, Eps, "a route, not a duel: it does not read the flask");
        }

        [Test]
        public void ItsBoltIsTheSameBolt_ParriableOnTheSameRead()
        {
            var d = Data();
            Assert.IsTrue(d.shootsProjectiles);
            Assert.IsNotNull(d.projectileAttack, "it reuses the shared bolt attack, it does not invent one");
            Assert.Greater(d.projectileHomingDegPerSec, 0f, "a bolt that can miss is a parry you were never offered");
            // The parry contract: a bolt must never arrive before its own cue, even from the band's near edge.
            float launch = ProjectileMath.LaunchSpeed(d.projectileMinRange, d.projectileSpeed, Projectile.CueLead, ProjectileShooter.CueMargin);
            Assert.Greater(ProjectileMath.TimeToImpact(d.projectileMinRange, launch), Projectile.CueLead,
                "the nearest bolt would arrive before its cue could fire");
            // ...and two bolts from the SAME turret must never be in the air with overlapping cues, or one
            // parry input has to answer two tells.
            Assert.Greater(d.projectileInterval, Projectile.CueLead * 2f,
                "a faster beat than two cue leads puts two 'press now' flashes on top of each other");
            Assert.LessOrEqual(d.projectileInterval, 1.6f,
                "it has about a second in your arc as you slide past: a slower beat means no bolt at all");
            Assert.Less(ProjectileMath.TimeToImpact(15f, d.projectileSpeed), 0.55f,
                "a mid-band shot must arrive inside ~half a second: answered at a run, never waited for");
        }

        [Test]
        public void ItReadsAsSomethingElseEntirelyFromTheOtherTwo()
        {
            var d = Data();
            var ghost = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy01"));
            var heavy = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy02"));
            if (ghost == null || heavy == null) Assert.Ignore("run 3. Create Data");

            // SIZE: half a sentry. Silhouette and value are the other two axes; this is the one a number
            // can hold on to, and it is the one that makes a row read as a row.
            Assert.LessOrEqual(d.scale, 0.7f, "'a small little turret' -- it must be visibly smaller than either sentry");
            Assert.Less(d.scale, ghost.scale * 0.8f);
            Assert.Less(d.scale, heavy.scale * 0.8f);

            // VALUE: mid steel, between the ghost's near-white and the Heavy Sentry's near-black, so all
            // three separate by brightness alone at a distance where hue is gone.
            float V(Color c) { return Mathf.Max(c.r, Mathf.Max(c.g, c.b)); }
            Assert.Less(V(d.bodyColor), V(ghost.bodyColor) - 0.15f, "it must not read as the pale ghost");
            Assert.Greater(V(d.bodyColor), V(heavy.bodyColor) + 0.15f, "nor as the dark Heavy Sentry");

            // COLD, like every other body. Warm means a combat tell or it means fire (ANIMATION-VFX
            // section 4), and this thing is read while an amber bolt crosses it.
            Assert.Greater(d.bodyColor.b, d.bodyColor.r, "a warm body would sit in the tells' colour family");
            Assert.Greater(d.emission.b, d.emission.r);
            // ...and it must not bloom: light on an enemy means "you deflected it".
            Assert.LessOrEqual(V(d.emission), 1.05f, "an enemy body that blooms steals the meaning of a deflect");
        }

        // ---------------------------------------------------------------- the shipped prefab

        [Test]
        public void ThePrefabCarriesTheTurretBrainAndTheShooter_AndNoFlare()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (p == null) Assert.Ignore("run 4. Build Prefabs");
            if (p.GetComponent<SurgeTurret>() == null) Assert.Ignore("built before SurgeTurret existed; run 4. Build Prefabs");
            Assert.IsNotNull(p.GetComponent<EnemyController>(), "the subclass must still answer as an EnemyController");
            Assert.IsNotNull(p.GetComponent<ProjectileShooter>(), "it shoots: that is half its sentence");
            Assert.IsNull(p.GetComponent<SentryBurst>(),
                "a target throws no flare: a row of five floating grapple flares down one ramp is clutter, not traversal");
        }

        [Test]
        public void TheBodyIsACircle_AndItIsNotFloating()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (p == null) Assert.Ignore("run 4. Build Prefabs");
            var lunge = p.transform.Find("Visual/LungeRoot");
            if (lunge == null) Assert.Ignore("run 4. Build Prefabs");
            Assert.IsNotNull(lunge.Find("Ring"), "the hoop is the read: without it this is a ball, not a turret");
            Assert.GreaterOrEqual(lunge.Find("Ring").childCount, 12,
                "under twelve segments the hoop is visibly a polygon at 10 m, and the whole point is that it is a CIRCLE");
            Assert.IsNotNull(lunge.Find("Stalk"), "a floating orb is the ghost's language; a turret is bolted down");
            Assert.IsNotNull(lunge.Find("Eye"), "the muzzle is the facing read");
            // The rig has to stay the standard one so nothing that poses an enemy special-cases this body.
            Assert.IsNotNull(lunge.Find("ArmPivot"));
            Assert.IsNotNull(lunge.Find("ArmPivot/WeaponPivot"));

            // The collider is untouched by the body: the visible turret sits INSIDE its own hitbox.
            var col = p.GetComponent<CapsuleCollider>();
            Assert.IsNotNull(col);
            var core = lunge.Find("Body");
            Assert.IsNotNull(core);
            Assert.Less(core.localPosition.y + 0.5f, col.center.y + col.height * 0.5f + Eps,
                "the core must not poke out of the top of the capsule that is actually shot at");
            Assert.Greater(core.localPosition.y - 0.5f, col.center.y - col.height * 0.5f - Eps);
        }
    }
}
