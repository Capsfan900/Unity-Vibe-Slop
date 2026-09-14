using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class LevelRunScoringTests
    {
        static RunSplitDef Split(string name, float s)
        {
            return new RunSplitDef { name = name, endSpawnerName = name, sSeconds = s, aSeconds = s + 10f,
                                     bSeconds = s + 20f, cSeconds = s + 30f };
        }

        [Test]
        public void GradesUseInclusiveAscendingThresholds()
        {
            var split = Split("A", 10f);
            Assert.AreEqual(SplitGrade.S, RunScoreMath.Grade(split, 10f));
            Assert.AreEqual(SplitGrade.A, RunScoreMath.Grade(split, 20f));
            Assert.AreEqual(SplitGrade.B, RunScoreMath.Grade(split, 30f));
            Assert.AreEqual(SplitGrade.C, RunScoreMath.Grade(split, 40f));
            Assert.AreEqual(SplitGrade.D, RunScoreMath.Grade(split, 40.001f));
        }

        [Test]
        public void BonusTableMapsEveryGradeAndCanRemainMonotonic()
        {
            var bonuses = new RunGradeBonusDef { dSouls = 0, cSouls = 25, bSouls = 50, aSouls = 75, sSouls = 100 };
            Assert.AreEqual(0, RunScoreMath.Bonus(bonuses, SplitGrade.D));
            Assert.AreEqual(25, RunScoreMath.Bonus(bonuses, SplitGrade.C));
            Assert.AreEqual(50, RunScoreMath.Bonus(bonuses, SplitGrade.B));
            Assert.AreEqual(75, RunScoreMath.Bonus(bonuses, SplitGrade.A));
            Assert.AreEqual(100, RunScoreMath.Bonus(bonuses, SplitGrade.S));
            Assert.GreaterOrEqual(bonuses.cSouls, bonuses.dSouls);
            Assert.GreaterOrEqual(bonuses.bSouls, bonuses.cSouls);
            Assert.GreaterOrEqual(bonuses.aSouls, bonuses.bSouls);
            Assert.GreaterOrEqual(bonuses.sSouls, bonuses.aSouls);
        }

        [Test]
        public void SoulQuotaIsSeparateFromTheRegularKillAndSplitCompletionGates()
        {
            Assert.IsFalse(RunScoreMath.MeetsSoulQuota(99, 100));
            Assert.IsTrue(RunScoreMath.MeetsSoulQuota(100, 100));
            Assert.IsTrue(RunScoreMath.MeetsSoulQuota(0, 0), "empty definitions retain legacy completion");
            Assert.IsFalse(RunScoreMath.IsCompletionSuccessful(100, 100, 3, 4, 4, 4),
                "progress cannot be recorded without the distinct regular-kill floor");
            Assert.IsFalse(RunScoreMath.IsCompletionSuccessful(100, 100, 4, 4, 3, 4),
                "progress cannot be recorded before every ordered endpoint closes");
            Assert.IsTrue(RunScoreMath.IsCompletionSuccessful(100, 100, 4, 4, 4, 4));
            Assert.IsTrue(RunScoreMath.IsCompletionSuccessful(3560, 3560, 4, 4, 4, 4),
                "the shipped minimum route clears with four D/zero-bonus splits");
            Assert.IsFalse(RunScoreMath.IsCompletionSuccessful(3800, 3560, 3, 4, 4, 4),
                "fast grades and extra souls may never replace the four-distinct-regular floor");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BossEvaluationIsIdenticalInEitherSubscriberOrder_AndIncludesFinalSplitBonus(bool scorerFirst)
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.requiredRunSouls = 1600;
            def.requiredRegularKills = 1; // keeps this isolated test from writing real LevelProgress
            def.gradeBonuses = new RunGradeBonusDef { sSouls = 100 };
            def.spawns = new[] { new SpawnDef { name = "Spawn_Boss" } };
            def.runSplits = new[] { new RunSplitDef { name = "Warden", endSpawnerName = "Spawn_Boss",
                sSeconds = 10f, aSeconds = 20f, bSeconds = 30f, cSeconds = 40f } };
            GameObject timerHost = null;
            GameObject scorerHost = null;
            object previousTimer = StaticSingleton(typeof(SpeedrunTimer));
            object previousScorer = StaticSingleton(typeof(LevelRunScorer));
            object previousWallet = StaticSingleton(typeof(SoulsWallet));
            object previousBossDefeated = StaticEvent("BossDefeated");
            object previousRunEvaluated = StaticEvent("LevelRunEvaluated");
            object previousRunScoreChanged = StaticEvent("RunScoreChanged");
            object previousSplitGraded = StaticEvent("SplitGraded");
            int evaluatedEvents = 0;
            System.Action<LevelRunResult> observed = _ => evaluatedEvents++;
            try
            {
                SpeedrunTimer timer;
                LevelRunScorer scorer;
                SetStaticEvent("BossDefeated", null);
                SetStaticEvent("LevelRunEvaluated", null);
                SetStaticEvent("RunScoreChanged", null);
                SetStaticEvent("SplitGraded", null);
                GameEvents.LevelRunEvaluated += observed;
                SetStaticSingleton(typeof(SpeedrunTimer), null);
                SetStaticSingleton(typeof(LevelRunScorer), null);
                SetStaticSingleton(typeof(SoulsWallet), null);
                if (scorerFirst)
                {
                    scorerHost = new GameObject("Scorer first");
                    scorer = scorerHost.AddComponent<LevelRunScorer>();
                    Invoke(scorer, "OnDisable"); // remove any lifecycle Unity happened to run in EditMode
                    scorer.definition = def;
                    timerHost = new GameObject("Timer second");
                    timer = timerHost.AddComponent<SpeedrunTimer>();
                    Invoke(timer, "OnDisable");
                    SetStaticSingleton(typeof(SpeedrunTimer), null);
                    SetStaticSingleton(typeof(LevelRunScorer), null);
                    Invoke(scorer, "Awake");
                    Invoke(scorer, "OnEnable");
                    Invoke(timer, "Awake");
                    Invoke(timer, "OnEnable");
                    Invoke(scorer, "BindTimer");
                }
                else
                {
                    timerHost = new GameObject("Timer first");
                    timer = timerHost.AddComponent<SpeedrunTimer>();
                    Invoke(timer, "OnDisable");
                    scorerHost = new GameObject("Scorer second");
                    scorer = scorerHost.AddComponent<LevelRunScorer>();
                    Invoke(scorer, "OnDisable");
                    scorer.definition = def;
                    SetStaticSingleton(typeof(SpeedrunTimer), null);
                    SetStaticSingleton(typeof(LevelRunScorer), null);
                    Invoke(timer, "Awake");
                    Invoke(timer, "OnEnable");
                    Invoke(scorer, "Awake");
                    Invoke(scorer, "OnEnable");
                }

                SetInstanceAutoProperty(timer, "Elapsed", 12.5f);
                scorer.BeginRun();
                Assert.IsTrue(scorer.TryCreditSpawnerForRun("Spawn_Boss", 1500, 8f),
                    "EnemyKilled credits the final boss before BossDefeated evaluates the run");
                GameEvents.RaiseBossDefeated();

                Assert.IsTrue(timer.Finished);
                Assert.IsTrue(scorer.LastResult.HasValue);
                var result = scorer.LastResult.Value;
                Assert.AreEqual(1600, result.earnedSouls, "1500 base + 100 S split bonus must be frozen into the result");
                Assert.IsTrue(result.quotaMet);
                Assert.IsTrue(result.splitsCompleted);
                Assert.IsTrue(result.hasFinalSplit);
                Assert.AreEqual(SplitGrade.S, result.finalSplit.grade);
                Assert.AreEqual(100, result.finalSplit.soulBonus);
                Assert.AreEqual(12.5f, result.elapsedSeconds, 1e-4f, "adjudication freezes the timer's final value");
                Assert.IsFalse(result.completed, "the deliberately missing regular floor prevents persistence");
                Assert.AreEqual(1, evaluatedEvents);

                GameEvents.RaiseBossDefeated();
                Assert.AreEqual(1, evaluatedEvents, "boss/result events are one-shot");
                Assert.IsFalse(scorer.TryCreditSpawnerForRun("late", 999, 9f), "late kills cannot mutate a frozen run");
                Assert.AreEqual(1600, scorer.LastResult.Value.earnedSouls);
            }
            finally
            {
                GameEvents.LevelRunEvaluated -= observed;
                if (scorerHost != null) Invoke(scorerHost.GetComponent<LevelRunScorer>(), "OnDisable");
                if (timerHost != null) Invoke(timerHost.GetComponent<SpeedrunTimer>(), "OnDisable");
                if (scorerHost != null) Object.DestroyImmediate(scorerHost);
                if (timerHost != null) Object.DestroyImmediate(timerHost);
                SetStaticSingleton(typeof(SpeedrunTimer), previousTimer);
                SetStaticSingleton(typeof(LevelRunScorer), previousScorer);
                SetStaticSingleton(typeof(SoulsWallet), previousWallet);
                SetStaticEvent("BossDefeated", previousBossDefeated);
                SetStaticEvent("LevelRunEvaluated", previousRunEvaluated);
                SetStaticEvent("RunScoreChanged", previousRunScoreChanged);
                SetStaticEvent("SplitGraded", previousSplitGraded);
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void ScoredGhostWaitsForEvaluationAndDiscardsFailure_WhileLegacyStillFinishes()
        {
            GameObject scorerHost = null;
            GameObject recorderHost = null;
            object previousScorer = StaticSingleton(typeof(LevelRunScorer));
            try
            {
                scorerHost = new GameObject("Scored level marker");
                var scorer = scorerHost.AddComponent<LevelRunScorer>();
                SetStaticSingleton(typeof(LevelRunScorer), scorer);
                recorderHost = new GameObject("Scored recorder");
                var recorder = recorderHost.AddComponent<RunRecorder>();
                Invoke(recorder, "OnRunStarted");
                Invoke(recorder, "OnLegacyRunFinished");
                Assert.IsTrue(recorder.IsRecording,
                    "RunFinished/BossDefeated must wait while a scored level is still adjudicating");

                Invoke(recorder, "OnRunEvaluated", new LevelRunResult { completed = false });
                Assert.IsFalse(recorder.IsRecording);
                Assert.AreEqual(0, recorder.SampleCount, "a failed scored run is discarded, never submitted as a ghost");

                Object.DestroyImmediate(recorderHost);
                recorderHost = null;
                Object.DestroyImmediate(scorerHost);
                scorerHost = null;
                SetStaticSingleton(typeof(LevelRunScorer), null);

                recorderHost = new GameObject("Legacy recorder");
                recorder = recorderHost.AddComponent<RunRecorder>();
                Invoke(recorder, "OnRunStarted");
                Invoke(recorder, "OnLegacyRunFinished");
                Assert.IsFalse(recorder.IsRecording, "an old level without a scorer preserves legacy completion");
            }
            finally
            {
                if (recorderHost != null) Object.DestroyImmediate(recorderHost);
                if (scorerHost != null) Object.DestroyImmediate(scorerHost);
                SetStaticSingleton(typeof(LevelRunScorer), previousScorer);
            }
        }

        [Test]
        public void ScorerCreditsEachSpawnerOnceClosesOnlyTheCurrentSplitAndAddsBonus()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.requiredRunSouls = 310;
            def.requiredRegularKills = 1;
            def.gradeBonuses = new RunGradeBonusDef { sSouls = 100 };
            def.runSplits = new[] { Split("A", 10f), Split("B", 10f) };
            GameObject host = null;
            LevelRunScorer scorer = null;
            object previousScorer = StaticSingleton(typeof(LevelRunScorer));
            object previousWallet = StaticSingleton(typeof(SoulsWallet));
            object previousRunScoreChanged = StaticEvent("RunScoreChanged");
            object previousSplitGraded = StaticEvent("SplitGraded");

            try
            {
                SetStaticSingleton(typeof(LevelRunScorer), null);
                SetStaticSingleton(typeof(SoulsWallet), null);
                SetStaticEvent("RunScoreChanged", null);
                SetStaticEvent("SplitGraded", null);
                host = new GameObject("Run scoring test");
                scorer = host.AddComponent<LevelRunScorer>();
                Invoke(scorer, "OnDisable");
                scorer.definition = def;
                SetStaticSingleton(typeof(LevelRunScorer), scorer);
                scorer.BeginRun();
                Assert.IsTrue(scorer.TryCreditSpawnerForRun("regular", 10, 2f));
                Assert.IsFalse(scorer.TryCreditSpawnerForRun("regular", 10, 3f), "a respawn cannot farm run score");
                Assert.IsTrue(scorer.TryCreditSpawnerForRun("B", 50, 4f), "out-of-order split still has base soul credit");
                Assert.AreEqual(0, scorer.CurrentSplitIndex, "only the current split may close");
                Assert.IsTrue(scorer.TryCreditSpawnerForRun("A", 50, 10f));
                Assert.AreEqual(1, scorer.CurrentSplitIndex);
                Assert.AreEqual(SplitGrade.S, scorer.LastSplit.Value.grade);
                Assert.IsFalse(scorer.TryCreditSpawnerForRun("B", 50, 20f), "the already killed later split cannot close retroactively");
                Assert.AreEqual(210, scorer.EarnedSouls, "10 regular + 50 B + 50 A + 100 A bonus");
                Assert.AreEqual(1, scorer.RegularKills);
                Assert.IsFalse(scorer.QuotaMet, "the second ordered split was not closed");
            }
            finally
            {
                if (scorer != null) Invoke(scorer, "OnDisable");
                if (host != null) Object.DestroyImmediate(host);
                SetStaticSingleton(typeof(LevelRunScorer), previousScorer);
                SetStaticSingleton(typeof(SoulsWallet), previousWallet);
                SetStaticEvent("RunScoreChanged", previousRunScoreChanged);
                SetStaticEvent("SplitGraded", previousSplitGraded);
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void AuthoringWritesTheLevel01RunContract()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            Assert.IsNotNull(shipped);
            var def = Object.Instantiate(shipped);
            try
            {
                LevelDefinitionAuthoring.Apply(def);
                Assert.AreEqual(4040, def.requiredRunSouls,
                    "four sub-bosses (400 + 600 + 900 + 480) + Warden 1500 + four 40-soul regulars define the Level_01 baseline");
                Assert.AreEqual(4, def.requiredRegularKills);
                CollectionAssert.AreEqual(new[] { "Spawn_Legendary_Ninja", "Spawn_Legendary_Knight",
                    "Spawn_Legendary_Spellsword", "Spawn_Legendary_V18Grappler", "Spawn_Boss" },
                    System.Array.ConvertAll(def.runSplits, s => s.endSpawnerName));
                CollectionAssert.AreEqual(new[] { 55f, 60f, 70f, 60f, 40f }, System.Array.ConvertAll(def.runSplits, s => s.sSeconds));
                Assert.AreEqual(100, def.gradeBonuses.sSouls);
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void ValidationRequiresUniqueExistingEndpointsOrderedFiniteThresholdsAndMonotonicBonuses()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.spawns = new[] { new SpawnDef { name = "A" }, new SpawnDef { name = "B" } };
            def.runSplits = new[] { Split("first", 10f), Split("second", 10f) };
            def.runSplits[0].endSpawnerName = "A";
            def.runSplits[1].endSpawnerName = "B";
            def.gradeBonuses = new RunGradeBonusDef { dSouls = 0, cSouls = 25, bSouls = 50, aSouls = 75, sSouls = 100 };
            try
            {
                string error;
                Assert.IsTrue(RunScoreMath.TryValidateDefinition(def, out error), error);
                def.runSplits[1].endSpawnerName = "A";
                Assert.IsFalse(RunScoreMath.TryValidateDefinition(def, out error));
                def.runSplits[1].endSpawnerName = "B";
                def.runSplits[0].aSeconds = float.PositiveInfinity;
                Assert.IsFalse(RunScoreMath.TryValidateDefinition(def, out error));
                def.runSplits[0].aSeconds = 20f;
                def.runSplits[0].bSeconds = 19f;
                Assert.IsFalse(RunScoreMath.TryValidateDefinition(def, out error));
                def.runSplits[0].bSeconds = 30f;
                def.gradeBonuses.bSouls = 24;
                Assert.IsFalse(RunScoreMath.TryValidateDefinition(def, out error));
                def.gradeBonuses.bSouls = 50;
                def.spawns = new[] { new SpawnDef { name = "A" }, new SpawnDef { name = "A" } };
                Assert.IsFalse(RunScoreMath.TryValidateDefinition(def, out error));
                def.spawns = new[] { new SpawnDef { name = "A" }, new SpawnDef { name = "B" } };
                def.requiredRegularKills = 1;
                Assert.IsFalse(RunScoreMath.TryValidateDefinition(def, out error),
                    "both authored spawners are split endpoints, so there is no regular kill to satisfy the floor");
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void ShippedAssetCarriesTheRunContractAfterAuthoring()
        {
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            Assert.AreEqual(4040, def.requiredRunSouls);
            Assert.AreEqual(4, def.requiredRegularKills);
            Assert.AreEqual(5, def.runSplits.Length);
            CollectionAssert.AreEqual(new[] { "Ninja", "Knight", "Spellsword", "Grappler", "Warden" },
                System.Array.ConvertAll(def.runSplits, s => s.name));
            CollectionAssert.AreEqual(new[] { "Spawn_Legendary_Ninja", "Spawn_Legendary_Knight",
                "Spawn_Legendary_Spellsword", "Spawn_Legendary_V18Grappler", "Spawn_Boss" },
                System.Array.ConvertAll(def.runSplits, s => s.endSpawnerName));
            CollectionAssert.AreEqual(new[] { 55f, 60f, 70f, 60f, 40f },
                System.Array.ConvertAll(def.runSplits, s => s.sSeconds));
            CollectionAssert.AreEqual(new[] { 0, 25, 50, 75, 100 }, new[] {
                def.gradeBonuses.dSouls, def.gradeBonuses.cSouls, def.gradeBonuses.bSouls,
                def.gradeBonuses.aSouls, def.gradeBonuses.sSouls });
        }

        [Test]
        public void ShippedLevelSceneCarriesTheScorerAndItsDataReferences()
        {
            const string path = "Assets/Scenes/Level_01.unity";
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                LevelRunScorer scorer = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    scorer = root.GetComponentInChildren<LevelRunScorer>(true);
                    if (scorer != null) break;
                }
                Assert.IsNotNull(scorer, "rebuild Level_01 after adding LevelRunScorer");
                Assert.IsNotNull(scorer.definition);
                Assert.AreEqual(4040, scorer.definition.requiredRunSouls);   // +480: the T4 grappler realm (2026-09-13)
                Assert.IsNotNull(scorer.registry);
            }
            finally
            {
                if (openedHere && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void BuilderSourceWiresTheScorerToDefinitionAndRegistry()
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "Editor", "LevelDefinitionBuilder.cs"));
            StringAssert.Contains("level.AddComponent<LevelRunScorer>()", source);
            StringAssert.Contains("runScorer.definition = def", source);
            StringAssert.Contains("runScorer.registry = AssetDatabase.LoadAssetAtPath<LevelRegistry>(RegistryPath)", source);
        }

        static void Invoke(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, method + " integration hook was renamed");
            m.Invoke(target, args != null && args.Length > 0 ? args : null);
        }

        static object StaticSingleton(System.Type type)
        {
            var f = type.GetField("<I>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(f, type.Name + ".I must remain an auto-property for this isolated fixture");
            return f.GetValue(null);
        }

        static void SetStaticSingleton(System.Type type, object value)
        {
            var f = type.GetField("<I>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(f, type.Name + ".I must remain an auto-property for this isolated fixture");
            f.SetValue(null, value);
        }

        static object StaticEvent(string eventName)
        {
            var f = typeof(GameEvents).GetField(eventName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "GameEvents." + eventName + " backing field was renamed");
            return f.GetValue(null);
        }

        static void SetStaticEvent(string eventName, object value)
        {
            var f = typeof(GameEvents).GetField(eventName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "GameEvents." + eventName + " backing field was renamed");
            f.SetValue(null, value);
        }

        static void SetInstanceAutoProperty(object target, string property, object value)
        {
            var f = target.GetType().GetField("<" + property + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, target.GetType().Name + "." + property + " must remain an auto-property");
            f.SetValue(target, value);
        }
    }
}
