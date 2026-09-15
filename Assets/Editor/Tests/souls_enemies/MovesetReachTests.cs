using NUnit.Framework;
using UnityEditor;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Reach hygiene (souls-AI accuracy spec 2026-09-14, A7): an entry with a real first hit never selects
    /// beyond <c>range + 0.5 + lunge + 1.0</c>. The seven realm bodies only: Grunt, Heavy and Legendary_Ninja
    /// still ship 99 m strings (spec section 3 names their fix; not done yet).
    /// </summary>
    public class MovesetReachTests
    {
        static readonly string[] EnemyNames =
        {
            SeraphLancerAuthoring.EnemyName,
            OrbitDancerAuthoring.EnemyName,
            CinderJudgeAuthoring.EnemyName,
            FlurryBrawlerV18Authoring.EnemyName,
            "Legendary_Halberdier",
            "Legendary_Revenant",
            "Legendary_Marionette",
        };

        static EnemyData Data(string enemyName) =>
            AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(enemyName));

        [Test]
        public void TheFirstHitOfEveryEntryCanReachItsOwnBandEdge()
        {
            int checkedEntries = 0;
            foreach (var enemyName in EnemyNames)
            {
                var d = Data(enemyName);
                Assert.IsNotNull(d, "run VibeGame1/3. Create Data (" + enemyName + ")");
                var ms = d.moveset;
                Assert.IsNotNull(ms, enemyName + " has no moveset assigned.");
                Assert.IsNotNull(ms.entries, enemyName + "'s moveset has no entries.");

                foreach (var e in ms.entries)
                {
                    if (e == null || e.combo == null || e.combo.hits == null || e.combo.hits.Length == 0) continue;
                    var first = e.combo.hits[0];
                    // A no-contact stance (range 0) carries no reach to bound.
                    if (first == null || first.range <= 0f) continue;

                    float allow = first.range + 0.5f + first.lungeDistance + 1.0f;
                    Assert.LessOrEqual(e.maxRange, allow + 0.001f,
                        enemyName + " '" + e.label + "': maxRange " + e.maxRange + " exceeds " + first.name +
                        "'s reach " + allow + " (range " + first.range + " + 0.5 allow + lunge " +
                        first.lungeDistance + " + 1.0 stalk allowance) -- the far branch could commit a blow " +
                        "that can never arrive.");
                    checkedEntries++;
                }
            }
            Assert.Greater(checkedEntries, 0,
                "no entries were checked -- run VibeGame1/3. Create Data, then 3b and 4 in order.");
        }

        [Test]
        public void SkyVerdictAndOrbitStormAreEligibleAtTheFightingDistance()
        {
            // A player who stays and trades at the commit edge must be able to draw the signature.
            AssertSpecialEligibleAtCommitEdge(
                SeraphLancerAuthoring.EnemyName, SeraphLancerAuthoring.SkyVerdictAttackName, "SKY VERDICT");
            AssertSpecialEligibleAtCommitEdge(
                OrbitDancerAuthoring.EnemyName, OrbitDancerAuthoring.DiscThrowAttackName, "ORBIT STORM");
        }

        static void AssertSpecialEligibleAtCommitEdge(string enemyName, string attackName, string label)
        {
            var d = Data(enemyName);
            Assert.IsNotNull(d, "run VibeGame1/3. Create Data (" + enemyName + ")");
            Assert.IsNotNull(d.moveset, enemyName + " has no moveset assigned.");

            float commitEdge = d.preferredRange + d.commitTolerance;
            Assert.IsTrue(d.moveset.HasEligible(commitEdge),
                enemyName + " has nothing eligible at its own commit edge " + commitEdge + " m.");

            MovesetEntry entry = null;
            foreach (var e in d.moveset.entries)
                if (e != null && e.combo != null && e.combo.hits != null && e.combo.hits.Length == 1 &&
                    e.combo.hits[0] != null && e.combo.hits[0].name == attackName)
                {
                    entry = e;
                    break;
                }
            Assert.IsNotNull(entry, enemyName + " has no standalone moveset entry for " + attackName + ".");
            Assert.IsTrue(entry.IsEligible(commitEdge),
                label + " (" + entry.minRange + "-" + entry.maxRange + " m) is not eligible at " + enemyName +
                "'s commit edge " + commitEdge + " m (preferredRange " + d.preferredRange + " + commitTolerance " +
                d.commitTolerance + ") -- a player who stays and trades never draws it.");
        }
    }
}
