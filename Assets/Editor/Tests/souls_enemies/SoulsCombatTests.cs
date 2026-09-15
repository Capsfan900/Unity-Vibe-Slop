using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The soulslike combat pass (2026-09-06, from docs/plans/soulslike-report-gap-analysis-2026-09-06.md):
    /// per-move cooldowns in the selector, the flask interrupt as data, the tempo-break attack every duel
    /// carries, and the Drillmaster showcase. Maths runs anywhere; asset checks Ignore until 3 / 4b ran.
    /// </summary>
    public class SoulsCombatTests
    {
        static EnemyAttackData Atk(string n, float windup)
        {
            var a = ScriptableObject.CreateInstance<EnemyAttackData>();
            a.attackName = n; a.windup = windup;
            return a;
        }

        static EnemyMoveset Set(params MovesetEntry[] entries)
        {
            var m = ScriptableObject.CreateInstance<EnemyMoveset>();
            m.entries = entries;
            return m;
        }

        static MovesetEntry E(string label, float weight, float min, float max, float cooldown, params EnemyAttackData[] hits)
        {
            return new MovesetEntry { label = label, weight = weight, minRange = min, maxRange = max, cooldown = cooldown, combo = new AttackCombo(hits) };
        }

        static float[] Fresh(int n) { var h = new float[n]; for (int i = 0; i < n; i++) h[i] = -1e9f; return h; }

        [Test]
        public void ASignatureOnCooldownIsNeverChosen_TheFillerIs()
        {
            var filler = Atk("jab", 0.5f); var sig = Atk("overhead", 1.0f);
            var ms = Set(E("jab", 1f, 0f, 99f, 0f, filler), E("OVERHEAD", 100f, 0f, 99f, 7f, sig));
            var hist = Fresh(2);
            var randomState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(0x51A7);
                bool chosenBefore = false;
                for (int i = 0; i < 64; i++) chosenBefore |= ms.SelectIndex(3f, hist, 10f) == 1;
                Assert.IsTrue(chosenBefore,
                    "the weighted signature must be eligible before its cooldown starts");

                hist[1] = 10f;   // thrown at t=10
                for (int i = 0; i < 50; i++)
                    Assert.AreEqual(0, ms.SelectIndex(3f, hist, 12f),
                        "inside the 7 s cooldown only the filler is eligible");

                bool chosenAfter = false;
                for (int i = 0; i < 64; i++) chosenAfter |= ms.SelectIndex(3f, hist, 17.5f) == 1;
                Assert.IsTrue(chosenAfter, "the signature must be eligible again after cooldown");
            }
            finally { UnityEngine.Random.state = randomState; }
        }

        [Test]
        public void WhenEverythingInBandIsCooling_TheEnemyStillAttacks()
        {
            var ms = Set(E("A", 1f, 0f, 99f, 5f, Atk("a", 0.5f)), E("B", 1f, 0f, 99f, 5f, Atk("b", 0.5f)));
            var hist = new[] { 10f, 10f };
            int idx = ms.SelectIndex(3f, hist, 11f);
            Assert.IsTrue(idx == 0 || idx == 1, "cooldowns relax rather than starve the enemy");
        }

        [Test]
        public void OutOfBandFallsBackToSomething_AndAnEmptySetIsMinusOne()
        {
            var ms = Set(E("near", 1f, 0f, 3f, 0f, Atk("a", 0.5f)));
            Assert.AreEqual(0, ms.SelectIndex(20f, Fresh(1), 0f));
            Assert.AreEqual(-1, Set().SelectIndex(3f, Fresh(0), 0f));
        }

        [Test]
        public void FreshHistoryIsOffCooldown()
        {
            var ms = Set(E("SIG", 1f, 0f, 99f, 30f, Atk("s", 1f)));
            Assert.AreEqual(0, ms.SelectIndex(3f, Fresh(1), 0f), "a -1e9 sentinel is 'never thrown', not 'thrown at the dawn of time'");
        }

        static readonly string[] Duels =
        {
            "Legendary_Ninja", "Legendary_Knight", "Legendary_Spellsword", "Legendary_Marionette",
            "Legendary_Revenant", "Legendary_Halberdier", "Legendary_Drillmaster",
        };

        [Test]
        public void EveryDuelCarriesATempoBreak_AndACooledSignature()
        {
            foreach (var n in Duels)
            {
                var d = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(n));
                if (d == null) Assert.Ignore("run 3. Create Data");
                if (d.moveset == null) Assert.Ignore(n + " has no moveset yet");
                float minW = float.MaxValue, maxW = 0f; bool anyCooldown = false;
                foreach (var e in d.moveset.entries)
                {
                    if (e == null || e.combo == null || e.combo.hits == null) continue;
                    if (e.cooldown > 0f) anyCooldown = true;
                    foreach (var h in e.combo.hits)
                    {
                        if (h == null) continue;
                        Assert.GreaterOrEqual(h.windup, 0.45f, n + "/" + h.name + " wind-up under the floor");
                        minW = Mathf.Min(minW, h.windup); maxW = Mathf.Max(maxW, h.windup);
                    }
                }
                Assert.GreaterOrEqual(maxW - minW, 0.25f, n + ": every wind-up is within 0.25 s of every other -- there is no tempo break, so parrying on the metronome is free");
                Assert.IsTrue(anyCooldown, n + ": no entry has a cooldown, so its signature can come twice running");
                Assert.Greater(d.flaskPunishChance, 0f, n + " never punishes the flask");
                Assert.Less(d.flaskPunishChance, 1.0001f);
            }
        }

        [Test]
        public void SentriesNeverPunishTheFlask_TheWardenDoes()
        {
            foreach (var n in new[] { "pshooter_enemy01", "pshooter_enemy02" })
            {
                var d = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(n));
                if (d == null) Assert.Ignore("run 3. Create Data");
                Assert.AreEqual(0f, d.flaskPunishChance, n + " is a route, not a duel: it has no interrupt");
                Assert.IsTrue(d.rangedOnly && d.shootsProjectiles, n + " is a parkour_enemy: it shoots and never melees");
            }
            // The pill guys are souls_enemies again (2026-09-06 split): melee, with the interrupt.
            foreach (var n in new[] { "Grunt", "Heavy" })
            {
                var d = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(n));
                if (d == null) Assert.Ignore("run 3. Create Data");
                Assert.IsFalse(d.rangedOnly || d.shootsProjectiles, n + " is the melee trainer, not a sentry");
                Assert.Greater(d.flaskPunishChance, 0f, n + " is a duel: it reads the flask");
            }
            var boss = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("Boss"));
            if (boss != null) Assert.Greater(boss.flaskPunishChance, 0f);
        }

        [Test]
        public void TheDrillmasterShipsAsTheShowcase()
        {
            var d = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("Legendary_Drillmaster"));
            if (d == null) Assert.Ignore("run 3. Create Data");
            Assert.AreEqual(1f, d.flaskPunishChance, 1e-4f, "the showcase punishes EVERY flask so the feature is seen");
            Assert.IsFalse(d.rangedOnly); Assert.IsFalse(d.shootsProjectiles);
            int cooled = 0; bool delayed = false, unblockable = false, farBand = false;
            foreach (var e in d.moveset.entries)
            {
                if (e.cooldown > 0f) cooled++;
                if (e.minRange >= 4f) farBand = true;
                foreach (var h in e.combo.hits) { if (h.windup >= 1.2f) delayed = true; if (h.unblockable) unblockable = true; }
            }
            Assert.GreaterOrEqual(cooled, 4, "most of its phrases are signatures on cooldown");
            Assert.IsTrue(delayed, "it carries the delayed overhead"); Assert.IsTrue(unblockable, "and the anti-turtle kick");
            Assert.IsTrue(farBand, "and a far-band closer");
            var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Legendary_Drillmaster.prefab");
            if (p == null) Assert.Ignore("run 4b. Build Mini-Bosses");
            Assert.IsNotNull(p.GetComponent<EnemyController>());
            Assert.AreSame(d, p.GetComponent<EnemyController>().data);
        }

        [Test]
        public void NearBreakStartsAtEightyPercent()
        {
            Assert.AreEqual(0.8f, EnemyPostureBar.NearBreakRatio, 1e-4f, "Sekiro's orange flash: the bar beats before it breaks, not at it");
            Assert.Greater(EnemyPostureBar.NearBreakHz, 2f); Assert.Less(EnemyPostureBar.NearBreakHz, 8f);
        }

        [Test]
        public void EnemyReacquiresPlayerWhenItsRuntimeTargetReferencesAreLost()
        {
            var enemyObject = new GameObject("Enemy_Target_Reacquire_Test");
            var playerObject = new GameObject("Player_Target_Reacquire_Test");
            try
            {
                playerObject.AddComponent<Health>();
                playerObject.AddComponent<PlayerCombat>();

                var enemy = enemyObject.AddComponent<EnemyController>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(EnemyController).GetField("player", flags).SetValue(enemy, null);
                typeof(EnemyController).GetField("playerCombat", flags).SetValue(enemy, null);

                bool acquired = (bool)typeof(EnemyController)
                    .GetMethod("EnsurePlayerTarget", flags)
                    .Invoke(enemy, null);

                Assert.IsTrue(acquired);
                Assert.IsNotNull(typeof(EnemyController).GetField("player", flags).GetValue(enemy),
                    "an enemy whose non-serialized target was cleared must not stay inert forever");
                Assert.IsNotNull(typeof(EnemyController).GetField("playerCombat", flags).GetValue(enemy),
                    "reacquisition must restore the combat receiver as well as the transform");
            }
            finally
            {
                Object.DestroyImmediate(enemyObject);
                Object.DestroyImmediate(playerObject);
            }
        }
        // ---- 2026-09-14 boss AI pass: phrase chaining, stance slot, projectile arbitration ----

        [Test]
        public void APhraseChainsOnlyOffAShortRecovery_InsideTheBand_AtTheAggressionRate()
        {
            Assert.IsTrue(EnemyController.ShouldChainPhrase(0.8f, 0.9f, 2.5f, 2.7f, 0.5f));
            Assert.IsFalse(EnemyController.ShouldChainPhrase(0.8f, 2.4f, 2.5f, 2.7f, 0.1f), "a signature's big recovery stays the punish");
            Assert.IsFalse(EnemyController.ShouldChainPhrase(0.8f, 0.9f, 4.0f, 2.7f, 0.1f), "a player who backed out of the band gets the breath");
            Assert.IsFalse(EnemyController.ShouldChainPhrase(0.8f, 0.9f, 2.5f, 2.7f, 0.85f), "rolled above aggression");
            Assert.IsFalse(EnemyController.ShouldChainPhrase(0f, 0.1f, 1f, 2.7f, 0f), "a passive enemy never chains");
        }

        [Test]
        public void OnlyANoContactStanceOnAFlaggedEnemyFreesTheAttackSlot()
        {
            var d = ScriptableObject.CreateInstance<EnemyData>();
            var stance = Atk("stance", 0.55f); stance.range = 0f;
            var blow = Atk("blow", 0.55f); blow.range = 2.6f;
            try
            {
                Assert.IsFalse(EnemyController.HoldsFreeStance(d, stance), "unflagged (T1 Lancer): strictly sequential");
                d.stanceFreesPartner = true;
                Assert.IsTrue(EnemyController.HoldsFreeStance(d, stance));
                Assert.IsFalse(EnemyController.HoldsFreeStance(d, blow), "a blow always holds the slot");
            }
            finally { Object.DestroyImmediate(d); Object.DestroyImmediate(stance); Object.DestroyImmediate(blow); }
        }

        [Test]
        public void ABoltLandingInsideTheCommitHorizonBlocksACommit()
        {
            BoltRegistry.Reset();
            try
            {
                Assert.IsFalse(BoltRegistry.AnyImpactBefore(10f + EnemyController.ProjectileCommitHorizon));
                BoltRegistry.Report(BoltRegistry.NextId(), float.MaxValue, 10.8f);
                Assert.IsTrue(BoltRegistry.AnyImpactBefore(10f + EnemyController.ProjectileCommitHorizon));
                Assert.IsFalse(BoltRegistry.AnyImpactBefore(9.8f + EnemyController.ProjectileCommitHorizon), "a bolt past the horizon does not");
            }
            finally { BoltRegistry.Reset(); }
        }
        // ---- 2026-09-14 spatial spec: lunge window, clip entry blend, follow-through ----

        [Test]
        public void ALongChargeStartsItsTravelEarlyEnoughToStayUnderNineMetresASecond()
        {
            Assert.AreEqual(0.28f, EnemyController.LungeWindow(0.5f, 0.28f), 1e-4f, "a short step stays cue-bound");
            Assert.AreEqual(0.28f, EnemyController.LungeWindow(2.5f, 0.28f), 1e-4f);
            Assert.AreEqual(3.32f / 9f, EnemyController.LungeWindow(3.32f, 0.28f), 1e-4f, "the forge charge");
            Assert.AreEqual(4.7f / 9f, EnemyController.LungeWindow(4.7f, 0.28f), 1e-4f, "the Halberdier charge");
            Assert.LessOrEqual(4.7f / EnemyController.LungeWindow(4.7f, 0.28f), EnemyController.MaxLungeSpeed + 1e-3f);
            Assert.AreEqual(0.28f, EnemyController.LungeWindow(-1f, 0.28f), 1e-4f);
        }

        [Test]
        public void AttackClipsBlendByTheirWindupAndKeepTheirOwnTail()
        {
            Assert.AreEqual(0.07f, PuppetVisuals.AttackEntryBlend(0.1f, 0.07f), 1e-4f, "never under the jar blend");
            Assert.AreEqual(0.14f, PuppetVisuals.AttackEntryBlend(0.56f, 0.07f), 1e-4f);
            Assert.AreEqual(0.16f, PuppetVisuals.AttackEntryBlend(2f, 0.07f), 1e-4f, "never over 0.16 s");
            Assert.AreEqual(0.6f, PuppetVisuals.FollowThrough(1.5f, 0.3f, 0.45f), 1e-4f, "a long flourish is capped");
            Assert.AreEqual(0.45f, PuppetVisuals.FollowThrough(0.6f, 0.4f, 0.45f), 1e-4f, "a short tail keeps the default");
            Assert.AreEqual(0.52f, PuppetVisuals.FollowThrough(1.0f, 0.48f, 0.45f), 1e-4f);
        }
    }
}
