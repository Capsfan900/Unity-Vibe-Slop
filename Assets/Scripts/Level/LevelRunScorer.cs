using System;
using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Pure rules for data-authored split grading and run-completion requirements.</summary>
    public static class RunScoreMath
    {
        public static SplitGrade Grade(RunSplitDef split, float seconds)
        {
            if (split == null) return SplitGrade.D;
            if (seconds <= split.sSeconds) return SplitGrade.S;
            if (seconds <= split.aSeconds) return SplitGrade.A;
            if (seconds <= split.bSeconds) return SplitGrade.B;
            if (seconds <= split.cSeconds) return SplitGrade.C;
            return SplitGrade.D;
        }

        public static int Bonus(RunGradeBonusDef bonuses, SplitGrade grade)
        {
            if (bonuses == null) return 0;
            switch (grade)
            {
                case SplitGrade.S: return bonuses.sSouls;
                case SplitGrade.A: return bonuses.aSouls;
                case SplitGrade.B: return bonuses.bSouls;
                case SplitGrade.C: return bonuses.cSouls;
                default: return bonuses.dSouls;
            }
        }

        /// <summary>The soul requirement is inclusive; a zero requirement preserves legacy completion.</summary>
        public static bool MeetsSoulQuota(int earnedSouls, int requiredSouls)
        {
            return earnedSouls >= Mathf.Max(0, requiredSouls);
        }

        /// <summary>The exact gate for writing LevelProgress after a boss defeat.</summary>
        public static bool IsCompletionSuccessful(int earnedSouls, int requiredSouls, int regularKills,
                                                  int requiredRegularKills, int completedSplits, int splitCount)
        {
            return MeetsSoulQuota(earnedSouls, requiredSouls)
                   && regularKills >= Mathf.Max(0, requiredRegularKills)
                   && completedSplits >= Mathf.Max(0, splitCount);
        }

        /// <summary>Checks a definition's authored run contract before it is built into a level scene.</summary>
        public static bool TryValidateDefinition(LevelDefinition definition, out string error)
        {
            error = null;
            if (definition == null) { error = "definition is null"; return false; }
            if (definition.requiredRunSouls < 0 || definition.requiredRegularKills < 0)
            {
                error = "run soul and regular-kill requirements must be nonnegative";
                return false;
            }

            var spawnerNames = new HashSet<string>();
            if (definition.spawns != null)
            {
                for (int i = 0; i < definition.spawns.Length; i++)
                {
                    SpawnDef spawn = definition.spawns[i];
                    if (spawn == null) continue;
                    if (string.IsNullOrEmpty(spawn.name)) { error = "a spawn has no name"; return false; }
                    if (!spawnerNames.Add(spawn.name)) { error = "duplicate spawner name '" + spawn.name + "'"; return false; }
                }
            }

            // Assets authored before split scoring have no serialized bonus object. Treat that absence as
            // the all-zero table so an otherwise empty legacy definition still builds and completes.
            RunGradeBonusDef bonuses = definition.gradeBonuses ?? new RunGradeBonusDef();
            if (bonuses.dSouls < 0 || bonuses.cSouls < bonuses.dSouls ||
                bonuses.bSouls < bonuses.cSouls || bonuses.aSouls < bonuses.bSouls || bonuses.sSouls < bonuses.aSouls)
            {
                error = "grade bonuses must be nonnegative and monotonic D/C/B/A/S";
                return false;
            }

            var endpoints = new HashSet<string>();
            int splitCount = definition.runSplits != null ? definition.runSplits.Length : 0;
            for (int i = 0; i < splitCount; i++)
            {
                RunSplitDef split = definition.runSplits[i];
                if (split == null) { error = "run split " + i + " is null"; return false; }
                if (string.IsNullOrEmpty(split.endSpawnerName) || !spawnerNames.Contains(split.endSpawnerName))
                {
                    error = "run split '" + split.name + "' has an unknown endpoint";
                    return false;
                }
                if (!endpoints.Add(split.endSpawnerName))
                {
                    error = "duplicate split endpoint '" + split.endSpawnerName + "'";
                    return false;
                }
                if (!FiniteAscending(split.sSeconds, split.aSeconds, split.bSeconds, split.cSeconds))
                {
                    error = "run split '" + split.name + "' thresholds must be finite, nonnegative, and S/A/B/C ascending";
                    return false;
                }
            }
            int regularSpawnerCount = spawnerNames.Count - endpoints.Count;
            if (regularSpawnerCount < definition.requiredRegularKills)
            {
                error = "regular-kill requirement exceeds the number of distinct non-split spawners";
                return false;
            }
            return true;
        }

        static bool FiniteAscending(float s, float a, float b, float c)
        {
            return FiniteNonNegative(s) && FiniteNonNegative(a) && FiniteNonNegative(b) && FiniteNonNegative(c)
                   && s <= a && a <= b && b <= c;
        }

        static bool FiniteNonNegative(float value)
        {
            return value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>The immutable record emitted when one ordered split closes.</summary>
    [Serializable]
    public struct RunSplitResult
    {
        public int splitIndex;
        public string name;
        public string endSpawnerName;
        public SplitGrade grade;
        public float seconds;
        public int soulBonus;
    }

    /// <summary>The immutable result emitted once when the boss ends a run.</summary>
    [Serializable]
    public struct LevelRunResult
    {
        public bool completed;
        public bool quotaMet;
        public bool regularKillsMet;
        public bool splitsCompleted;
        public int earnedSouls;
        public int requiredSouls;
        public int regularKills;
        public int requiredRegularKills;
        public int completedSplits;
        public int splitCount;
        public float elapsedSeconds;
        public bool hasFinalSplit;
        public RunSplitResult finalSplit;
    }

    /// <summary>
    /// Observes enemy deaths to keep a run-local, non-farmable score. EnemyController remains the sole
    /// owner of base soul awards; this component only mirrors those base values for quota accounting and
    /// awards the additional, data-authored split bonus.
    /// </summary>
    public class LevelRunScorer : MonoBehaviour
    {
        public static LevelRunScorer I { get; private set; }

        [Header("Data")]
        public LevelDefinition definition;
        public LevelRegistry registry;

        [SerializeField] SpeedrunTimer timer;

        readonly HashSet<string> creditedSpawnerNames = new HashSet<string>();
        bool timerBound;
        bool running;
        bool evaluated;
        float previousSplitElapsed;

        public int EarnedSouls { get; private set; }
        public int RequiredSouls { get { return definition != null ? Mathf.Max(0, definition.requiredRunSouls) : 0; } }
        public int RegularKills { get; private set; }
        public int RequiredRegularKills { get { return definition != null ? Mathf.Max(0, definition.requiredRegularKills) : 0; } }
        public int CurrentSplitIndex { get; private set; }
        /// <summary>True only after a successful boss-end evaluation has recorded progression.</summary>
        public bool Completed { get; private set; }
        /// <summary>Only the run-earned soul quota; regular-kill and split gates remain separately visible.</summary>
        public bool QuotaMet { get { return RunScoreMath.MeetsSoulQuota(EarnedSouls, RequiredSouls); } }
        public RunSplitResult? LastSplit { get; private set; }
        /// <summary>Frozen boss-end adjudication, available after <see cref="GameEvents.LevelRunEvaluated"/> fires.</summary>
        public LevelRunResult? LastResult { get; private set; }
        public bool Evaluated { get { return evaluated; } }

        int SplitCount { get { return definition != null && definition.runSplits != null ? definition.runSplits.Length : 0; } }

        void Awake()
        {
            I = this;
        }

        void OnEnable()
        {
            GameEvents.EnemyKilled += OnEnemyKilled;
            GameEvents.BossDefeated += OnBossDefeated;
            BindTimer();
        }

        void Start()
        {
            // Managers and the level root have no script-execution-order contract. A Start retry binds
            // safely when this component enabled before the timer's Awake.
            BindTimer();
        }

        void OnDisable()
        {
            GameEvents.EnemyKilled -= OnEnemyKilled;
            GameEvents.BossDefeated -= OnBossDefeated;
            if (timerBound && timer != null) timer.RunStarted -= BeginRun;
            timerBound = false;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }

        void BindTimer()
        {
            if (timerBound) return;
            if (timer == null) timer = SpeedrunTimer.I;
            if (timer == null) return;
            timer.RunStarted += BeginRun;
            timerBound = true;
        }

        /// <summary>Resets all run-local credit when SpeedrunTimer starts a new run.</summary>
        public void BeginRun()
        {
            creditedSpawnerNames.Clear();
            EarnedSouls = 0;
            RegularKills = 0;
            CurrentSplitIndex = 0;
            Completed = false;
            evaluated = false;
            LastSplit = null;
            LastResult = null;
            previousSplitElapsed = 0f;
            running = true;
            GameEvents.RaiseRunScoreChanged(this);
        }

        void OnEnemyKilled(EnemyController enemy)
        {
            if (!running || enemy == null) return;
            EnemySpawner spawner = ResolveSpawner(enemy);
            if (spawner == null) return;
            if (!IsAuthoredSpawner(spawner.name)) return;
            float elapsed = timer != null ? timer.Elapsed : 0f;
            int souls = enemy.data != null ? enemy.data.soulValue : 0;
            TryCreditSpawnerForRun(spawner.name, souls, elapsed);
        }

        EnemySpawner ResolveSpawner(EnemyController enemy)
        {
            EnemySpawner[] spawners = FindObjectsByType<EnemySpawner>();
            for (int i = 0; i < spawners.Length; i++)
            {
                EnemySpawner spawner = spawners[i];
                if (spawner != null && spawner.Instance == enemy.gameObject) return spawner;
            }
            return null;
        }

        bool IsAuthoredSpawner(string spawnerName)
        {
            if (definition == null || definition.spawns == null) return false;
            for (int i = 0; i < definition.spawns.Length; i++)
            {
                SpawnDef spawn = definition.spawns[i];
                if (spawn != null && spawn.name == spawnerName) return true;
            }
            return false;
        }

        /// <summary>
        /// Credits an already-resolved spawner once. Public for deterministic runtime tests; normal play
        /// reaches it only through <see cref="GameEvents.EnemyKilled"/> and <see cref="ResolveSpawner"/>.
        /// </summary>
        public bool TryCreditSpawnerForRun(string spawnerName, int soulValue, float elapsedSeconds)
        {
            if (!running || string.IsNullOrEmpty(spawnerName) || !creditedSpawnerNames.Add(spawnerName)) return false;

            EarnedSouls += soulValue;
            if (!IsSplitSpawner(spawnerName)) RegularKills++;

            RunSplitDef split = CurrentSplit();
            if (split != null && split.endSpawnerName == spawnerName)
            {
                float segmentSeconds = Mathf.Max(0f, elapsedSeconds - previousSplitElapsed);
                SplitGrade grade = RunScoreMath.Grade(split, segmentSeconds);
                int bonus = RunScoreMath.Bonus(definition != null ? definition.gradeBonuses : null, grade);
                EarnedSouls += bonus;
                if (bonus != 0 && SoulsWallet.I != null) SoulsWallet.I.Add(bonus);

                LastSplit = new RunSplitResult
                {
                    splitIndex = CurrentSplitIndex,
                    name = split.name,
                    endSpawnerName = split.endSpawnerName,
                    grade = grade,
                    seconds = segmentSeconds,
                    soulBonus = bonus,
                };
                CurrentSplitIndex++;
                previousSplitElapsed = elapsedSeconds;
                GameEvents.RaiseSplitGraded(LastSplit.Value);
            }

            GameEvents.RaiseRunScoreChanged(this);
            return true;
        }

        bool IsSplitSpawner(string spawnerName)
        {
            if (definition == null || definition.runSplits == null) return false;
            for (int i = 0; i < definition.runSplits.Length; i++)
            {
                RunSplitDef split = definition.runSplits[i];
                if (split != null && split.endSpawnerName == spawnerName) return true;
            }
            return false;
        }

        RunSplitDef CurrentSplit()
        {
            if (definition == null || definition.runSplits == null || CurrentSplitIndex >= definition.runSplits.Length) return null;
            return definition.runSplits[CurrentSplitIndex];
        }

        void OnBossDefeated()
        {
            if (evaluated) return;
            // The score event must contain the timer's final time even though this scorer was attached
            // before the Managers prefab and therefore subscribed to BossDefeated first.
            if (timer != null) timer.FinishRun();
            evaluated = true;
            running = false;

            bool splitComplete = CurrentSplitIndex >= SplitCount;
            bool regularKillsMet = RegularKills >= RequiredRegularKills;
            bool quotaMet = QuotaMet;
            Completed = RunScoreMath.IsCompletionSuccessful(EarnedSouls, RequiredSouls, RegularKills,
                                                              RequiredRegularKills, CurrentSplitIndex, SplitCount);
            float elapsed = timer != null ? timer.Elapsed : 0f;
            if (Completed && definition != null)
            {
                int deaths = LevelManager.I != null ? LevelManager.I.DeathCount : 0;
                LevelProgress.RecordCompletion(definition.SafeLevelId, elapsed, deaths, registry);
            }

            LastResult = new LevelRunResult
            {
                completed = Completed,
                quotaMet = quotaMet,
                regularKillsMet = regularKillsMet,
                splitsCompleted = splitComplete,
                earnedSouls = EarnedSouls,
                requiredSouls = RequiredSouls,
                regularKills = RegularKills,
                requiredRegularKills = RequiredRegularKills,
                completedSplits = CurrentSplitIndex,
                splitCount = SplitCount,
                elapsedSeconds = elapsed,
                hasFinalSplit = LastSplit.HasValue,
                finalSplit = LastSplit.HasValue ? LastSplit.Value : default(RunSplitResult),
            };
            GameEvents.RaiseLevelRunEvaluated(LastResult.Value);
        }
    }
}
