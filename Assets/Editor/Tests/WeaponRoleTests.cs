using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE WEAPON ROSTER'S JOBS, asserted as a CROSSING rather than a ladder.
    ///
    /// <para><see cref="WeaponSilhouetteTests"/> already holds the set's SHAPE — swing time, contact
    /// frame, combo length, reach, on-screen silhouette. Every one of those climbs monotonically from
    /// needle to maul, and it has to. But a monotone ladder is not a roster: before 2026-09-06 the
    /// sword and the maul had effectively identical throughput (73.9 vs 72.2 health/s, 36.4 vs 39.5
    /// posture/s), so the maul was a slower sword with more reach, and the needle was LAST in both
    /// columns — an option nobody loses by deleting.</para>
    ///
    /// <para>This file asserts the fix: the two posture channels run OPPOSITE to health damage, so
    /// each of the three archetypes wins one column outright and loses another outright.
    /// <list type="bullet">
    /// <item>Rosethorn — the BREAKER. Worst killer, best hit-posture, widest parry window.</item>
    /// <item>Cerulean Edge — the INSTRUMENT. The middle of every column, and untouched by this pass
    /// because its <c>parryPostureDamage</c> 25 is the constant two boss fights are built on.</item>
    /// <item>Sunbreaker — the CRUSHER. Best killer, worst hit-posture; it breaks people through the
    /// DEFLECT (40, the crown) behind the narrowest parry window in the game.</item>
    /// </list></para>
    ///
    /// <para>Rule 9 throughout: every number is read off the shipped <c>.asset</c> through
    /// <see cref="AssetDatabase"/>, never off a C# field initialiser. If <c>DataFactory</c> has not
    /// been re-run, these fail — which is the point.</para>
    /// </summary>
    public class WeaponRoleTests
    {
        const string EnemyDir = "Assets/Data/Enemies";

        static WeaponData W(string n)
        {
            var w = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Data/Weapons/" + n + ".asset");
            Assert.IsNotNull(w, n + ".asset missing — run VibeGame1/3. Create Data");
            return w;
        }

        static EnemyData E(string path)
        {
            var e = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
            Assert.IsNotNull(e, path + " missing — run VibeGame1/3. Create Data");
            return e;
        }

        /// <summary>Health damage per second of an uninterrupted full combo: every multiplier applied,
        /// over comboLength swings of attackDuration each (WeaponController.SwingCo chains one swing
        /// per attackDuration).</summary>
        static float HealthPerSecond(WeaponData w)
        {
            float total = 0f;
            for (int i = 0; i < w.comboLength; i++) total += w.baseDamage * w.ComboMultiplier(i);
            return total / (w.comboLength * w.attackDuration);
        }

        /// <summary>Posture per second of the same combo. Posture is applied FLAT per hit —
        /// WeaponController.DoHit calls Posture.Add(w.postureDamage) with no combo multiplier — so this
        /// is deliberately not HealthPerSecond's shape.</summary>
        static float PosturePerSecond(WeaponData w)
        {
            return w.postureDamage / w.attackDuration;
        }

        // ------------------------------------------------------------------ the crossing

        [Test]
        public void TheCrossing_HealthAndHitPostureRunOppositeWays()
        {
            var d = W("Dagger");
            var s = W("Sword");
            var h = W("Hammer");

            // Health damage climbs with weight...
            Assert.Less(HealthPerSecond(d), HealthPerSecond(s),
                "the needle must be the worst killer (" + HealthPerSecond(d) + " vs " + HealthPerSecond(s) + ")");
            Assert.Less(HealthPerSecond(s), HealthPerSecond(h),
                "the maul must be the best killer (" + HealthPerSecond(s) + " vs " + HealthPerSecond(h) + ")");

            // ...and hit-posture falls with it. This inversion IS the roster.
            Assert.Greater(PosturePerSecond(d), PosturePerSecond(s),
                "the needle must be the best breaker (" + PosturePerSecond(d) + " vs " + PosturePerSecond(s) + ")");
            Assert.Greater(PosturePerSecond(s), PosturePerSecond(h),
                "the maul must be the worst breaker (" + PosturePerSecond(s) + " vs " + PosturePerSecond(h) + ")");

            // And the gaps are differences of KIND, not stat-sheet nuance. The old sword/maul pair sat
            // at 1.02x and 1.09x, which is exactly what "delete it and switch to the thing that already
            // did its job" looks like written as numbers.
            Assert.GreaterOrEqual(PosturePerSecond(d) / PosturePerSecond(h), 1.6f,
                "needle/maul posture ratio " + (PosturePerSecond(d) / PosturePerSecond(h)) +
                " — the breaker column has flattened");
            Assert.GreaterOrEqual(HealthPerSecond(h) / HealthPerSecond(d), 1.6f,
                "maul/needle damage ratio " + (HealthPerSecond(h) / HealthPerSecond(d)) +
                " — the killer column has flattened");
        }

        [Test]
        public void TwoWaysToBreakAnEnemy_AndTheyBelongToDifferentWeapons()
        {
            var d = W("Dagger");
            var s = W("Sword");
            var h = W("Hammer");

            // Breaking by HITTING is the needle's; breaking by DEFLECTING is the maul's. The sword is
            // the middle of both, which is why it can stay the calibration reference.
            Assert.Greater(PosturePerSecond(d), PosturePerSecond(h), "hit-posture crown must be the needle's");
            Assert.Greater(h.parryPostureDamage, d.parryPostureDamage, "deflect-posture crown must be the maul's");
            Assert.Greater(h.parryPostureDamage, s.parryPostureDamage, "the maul must out-deflect the sword");

            // ...and each crown is paid for on the other side of the same exchange: the maul's deflect
            // is behind the narrowest window in the game, the needle's is behind the widest, and the
            // widest window earns the least Pyre so that forgiveness is never simply free.
            Assert.Less(h.parryWindowMultiplier, 1f, "the maul's deflect posture must cost window width");
            Assert.Greater(d.parryWindowMultiplier, 1f, "the needle's forgiveness is what it is FOR");
            Assert.Less(d.pyreBonus, h.pyreBonus, "the widest window must be the worst Pyre earner, or it is free");
        }

        [Test]
        public void TheNeedleReachesTheDeathblowBeforeTheKill_AndTheMaulNeverDoes()
        {
            // The one behavioural claim of the breaker role, checked against the shipped Grunt (60/60):
            // swings-to-break must be FEWER than swings-to-kill with the dagger, and the reverse with
            // the maul. This is what makes the deathblow the needle's KILL rather than a bonus, and it
            // is why the needle is the roster's parry-first weapon in offence as well as defence.
            var grunt = E(EnemyDir + "/souls_enemies/Grunt.asset");

            AssertBreaksBeforeKilling(W("Dagger"), grunt, true);
            AssertBreaksBeforeKilling(W("Hammer"), grunt, false);
        }

        static void AssertBreaksBeforeKilling(WeaponData w, EnemyData e, bool breakFirst)
        {
            int toBreak = Mathf.CeilToInt(e.maxPosture / w.postureDamage);
            int toKill = 0;
            float hp = e.maxHP;
            while (hp > 0f && toKill < 64)
            {
                hp -= w.baseDamage * w.ComboMultiplier(toKill % w.comboLength);
                toKill++;
            }
            if (breakFirst)
                Assert.Less(toBreak, toKill, w.displayName + " must BREAK the " + e.name + " in " + toBreak +
                    " hits before it kills it in " + toKill + " — the deathblow is this weapon's kill");
            else
                Assert.Less(toKill, toBreak, w.displayName + " must KILL the " + e.name + " in " + toKill +
                    " hits before it breaks it in " + toBreak + " — the maul does not play the posture game with its swings");
        }

        [Test]
        public void ABreakersDeathblow_KillsTheToughestThingItCanBreak()
        {
            // executeDamage 250 sat UNDER the Iron Penitent's 260 HP and the Ashen Chorister's 300, so
            // the needle could break a duellist and then fail to cash it — the role's promise broken by
            // one number. Checked against the largest ordinary health bar that ships. Bosses are
            // excluded: BossController's deathIsStagger removes a SEGMENT per deathblow, so a boss bar
            // is never compared against one execute.
            float toughest = 0f;
            string worst = "";
            foreach (var guid in AssetDatabase.FindAssets("t:EnemyData", new[] { EnemyDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var e = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
                if (e == null || e.name.Contains("Boss")) continue;
                if (e.maxHP > toughest) { toughest = e.maxHP; worst = e.name; }
            }
            Assert.Greater(toughest, 0f, "no EnemyData found under " + EnemyDir);
            Assert.GreaterOrEqual(W("Dagger").executeDamage, toughest,
                "Rosethorn.executeDamage " + W("Dagger").executeDamage + " cannot kill " + worst + " at " +
                toughest + " HP — the breaker breaks things it then cannot cash");
        }

        // ------------------------------------------------------------------ weight, felt

        [Test]
        public void HitStopScalesWithWeight_ButNeverOutPunctuatesTheDeflect()
        {
            var d = W("Dagger");
            var s = W("Sword");
            var h = W("Hammer");

            Assert.Less(d.hitStopSeconds, s.hitStopSeconds, "the needle must freeze the world least");
            Assert.Less(s.hitStopSeconds, h.hitStopSeconds, "the maul must freeze the world most");
            Assert.GreaterOrEqual(h.hitStopSeconds / d.hitStopSeconds, 2.5f,
                "maul/needle hitstop ratio " + (h.hitStopSeconds / d.hitStopSeconds) +
                " — everything is landing at the same weight, which is THE most cited melee failure");

            // THE CEILING. The deflect is what this game is about and it must own the longest freeze in
            // the game; an ordinary maul swing at 0.11 s was longer than GameFeel.parryHitStop 0.09 and
            // silently made hitting things the biggest beat on screen.
            var feel = AssetDatabase.LoadAssetAtPath<GameFeelSettings>("Assets/Data/GameFeel.asset");
            Assert.IsNotNull(feel, "GameFeel.asset missing");
            foreach (var n in new[] { "Dagger", "Sword", "Hammer" })
                Assert.Less(W(n).hitStopSeconds, feel.parryHitStop,
                    n + " hitstop " + W(n).hitStopSeconds + " >= parryHitStop " + feel.parryHitStop +
                    " — a routine swing may never out-punctuate a deflect");
        }

        [Test]
        public void EveryWeaponsAnticipationIsARealFractionOfItsSwing()
        {
            // WeaponViewmodel.AttackCo spends hitDelay travelling to the windup pose and dur*0.2 on the
            // strike leg, so hitDelay/attackDuration IS the anticipation share. A weapon whose contact
            // arrives in the first tenth of its animation has no wind-up to read, and one that is
            // mostly wind-up has no follow-through left to sell the arrival.
            foreach (var n in new[] { "Dagger", "Sword", "Hammer", "DevBlade" })
            {
                var w = W(n);
                float share = w.hitDelay / w.attackDuration;
                Assert.Greater(share, 0.2f, n + " spends only " + share.ToString("P0") + " of its swing winding up");
                Assert.Less(share, 0.6f, n + " spends " + share.ToString("P0") + " winding up — no follow-through left");
            }

            // And the share climbs with weight: the maul is nearly half wind-up, the needle a fifth.
            Assert.Less(W("Dagger").hitDelay / W("Dagger").attackDuration,
                        W("Sword").hitDelay / W("Sword").attackDuration,
                        "the sword must telegraph more than the needle");
            Assert.Less(W("Sword").hitDelay / W("Sword").attackDuration,
                        W("Hammer").hitDelay / W("Hammer").attackDuration,
                        "the maul must telegraph more than the sword");
        }

        [Test]
        public void TheInstrumentIsUntouched_TheSwordIsTheCalibrationReference()
        {
            // This pass deliberately changed NOTHING on the sword. Its parryPostureDamage 25 is the
            // Marionette's six-deflect economy and the Iron Penitent's eight-beat economy, and its
            // 26 / 16 sits between the needle's and the maul's on both columns by construction. Pinned
            // here so a future role pass that "rebalances the middle" trips over the reason first.
            var s = W("Sword");
            Assert.AreEqual(26f, s.baseDamage, 1e-4f, "sword baseDamage moved — see MarionetteDataTests / FeatureTests Knight");
            Assert.AreEqual(16f, s.postureDamage, 1e-4f, "sword postureDamage moved");
            Assert.AreEqual(25f, s.parryPostureDamage, 1e-4f, "sword parryPostureDamage is load-bearing arithmetic");
            Assert.AreEqual(0.06f, s.hitStopSeconds, 1e-4f, "sword hitStopSeconds moved");
            Assert.AreEqual(1f, s.parryWindowMultiplier, 1e-4f, "the sword IS the 1.0 the other windows are multiples of");
        }
    }
}
