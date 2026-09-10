using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Pins the player-authored speed ladder that every Perfect parry earns outside the tuned opening
    /// Surge Turret route. Asset assertions read shipped data, never field initialisers.
    /// </summary>
    public class ParrySurgeGeneralTests
    {
        const float Eps = 0.0001f;

        static PlayerStatsData Stats()
        {
            var stats = AssetDatabase.LoadAssetAtPath<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            if (stats == null) Assert.Ignore("run VibeGame1/3. Create Data");
            return stats;
        }

        [Test]
        public void GeneralPerfectLadderShipsAtOnePointTwelveFiveTimesWithTwoSecondDecay()
        {
            var stats = Stats();
            Assert.That(stats.generalParrySurgeStep, Is.EqualTo(0.12f).Within(Eps));
            Assert.AreEqual(5, stats.generalParrySurgeMaxStacks);
            Assert.That(stats.generalParrySurgeSeconds, Is.EqualTo(2f).Within(Eps));
            Assert.That(SurgeMath.MaxMultiplier(stats.generalParrySurgeMaxStacks, stats.generalParrySurgeStep),
                        Is.EqualTo(1.6f).Within(Eps));
            Assert.That(SurgeMath.FullDecaySeconds(stats.generalParrySurgeMaxStacks, stats.generalParrySurgeSeconds),
                        Is.EqualTo(10f).Within(Eps));
        }

        [Test]
        public void GeneralLadderCanBuildAcrossTheOrdinaryBlueSentryBeat()
        {
            var stats = Stats();
            var blue = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy01"));
            if (blue == null) Assert.Ignore("run VibeGame1/3. Create Data");
            Assert.Greater(stats.generalParrySurgeSeconds, blue.projectileInterval,
                "the 1.6 second ordinary blue-sentry beat must refresh, not consume, a clean stack");
            Assert.That(stats.generalParrySurgeSeconds - blue.projectileInterval, Is.EqualTo(0.4f).Within(Eps));
        }

        [Test]
        public void OrdinaryAttackersUseTheGeneralRouteButSurgeTurretsAreExcluded()
        {
            var ordinaryObject = new GameObject("ordinary-parry-source");
            var turretObject = new GameObject("surge-turret-parry-source");
            try
            {
                var ordinary = ordinaryObject.AddComponent<EnemyController>();
                var turret = turretObject.AddComponent<SurgeTurret>();
                Assert.IsTrue(PlayerCombat.ShouldGrantGeneralParrySurge(ordinary),
                    "a normal melee, blue Sentry, Heavy Sentry, or boss pays the shared ladder after OnParried");
                Assert.IsFalse(PlayerCombat.ShouldGrantGeneralParrySurge(turret),
                    "SurgeTurret.OnParried already grants its dedicated ladder; the shared route must not double-pay");
                Assert.IsFalse(PlayerCombat.ShouldGrantGeneralParrySurge(null));
            }
            finally
            {
                Object.DestroyImmediate(ordinaryObject);
                Object.DestroyImmediate(turretObject);
            }
        }

        [Test]
        public void OpeningTurretKeepsItsDedicatedTuning()
        {
            var turret = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy03"));
            if (turret == null) Assert.Ignore("run VibeGame1/3. Create Data");
            Assert.That(turret.parrySurgeStep, Is.EqualTo(0.12f).Within(Eps));
            Assert.AreEqual(5, turret.parrySurgeMaxStacks);
            Assert.That(turret.parrySurgeSeconds, Is.EqualTo(1.4f).Within(Eps),
                "the tuned ramp turret run-out must not inherit the general two-second decay");
        }
    }
}
