using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Which levels are unlocked and the best time on each. Persisted as JSON in
    /// <c>Application.persistentDataPath</c>.
    ///
    /// Everything is keyed by <c>LevelDefinition.levelId</c> — the same key the ghost-racing recordings and
    /// the leaderboard use, so a run, its ghost and its personal best all agree without a lookup table.
    /// Renaming a levelId orphans all three together, which is the honest failure mode.
    ///
    /// JSON rather than PlayerPrefs: PlayerPrefs on Windows is the registry, which is awkward to inspect,
    /// impossible to hand-edit for testing and does not travel with the project.
    /// </summary>
    public static class LevelProgress
    {
        const string FileName = "progress.json";

        [Serializable]
        class Entry
        {
            public string levelId;
            public bool unlocked;
            public bool completed;
            public float bestTime = -1f;     // seconds; -1 = never finished
            public int bestDeaths = -1;
            public string lastPlayedUtc;
        }

        [Serializable]
        class SaveFile
        {
            public int version = 1;
            public List<Entry> entries = new List<Entry>();
        }

        static SaveFile cache;

        static string Path_ => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        static SaveFile Data
        {
            get
            {
                if (cache != null) return cache;
                cache = Load();
                return cache;
            }
        }

        static SaveFile Load()
        {
            try
            {
                if (File.Exists(Path_))
                {
                    var json = File.ReadAllText(Path_);
                    var parsed = JsonUtility.FromJson<SaveFile>(json);
                    if (parsed != null && parsed.entries != null) return parsed;
                }
            }
            catch (Exception e)
            {
                // A corrupt save must never block play. Losing progress is bad; refusing to launch is worse.
                Debug.LogWarning("[LevelProgress] Could not read " + Path_ + " (" + e.Message + "). Starting fresh.");
            }
            return new SaveFile();
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(Path_, JsonUtility.ToJson(Data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LevelProgress] Could not write " + Path_ + ": " + e.Message);
            }
        }

        static Entry Get(string levelId, bool create)
        {
            if (string.IsNullOrEmpty(levelId)) return null;
            var list = Data.entries;
            for (int i = 0; i < list.Count; i++)
                if (list[i].levelId == levelId) return list[i];
            if (!create) return null;
            var e = new Entry { levelId = levelId };
            list.Add(e);
            return e;
        }

        // ---- queries ---------------------------------------------------------------------------

        public static bool IsUnlocked(string levelId, LevelRegistry registry)
        {
            var e = Get(levelId, false);
            if (e != null && e.unlocked) return true;

            // No saved state yet: the first N levels in campaign order are open by default.
            if (registry == null) return true;
            int idx = registry.IndexOf(levelId);
            return idx >= 0 && idx < Mathf.Max(1, registry.initiallyUnlocked);
        }

        public static bool IsCompleted(string levelId)
        {
            var e = Get(levelId, false);
            return e != null && e.completed;
        }

        /// <summary>Best time in seconds, or -1 when the level has never been finished.</summary>
        public static float BestTime(string levelId)
        {
            var e = Get(levelId, false);
            return e != null ? e.bestTime : -1f;
        }

        public static int BestDeaths(string levelId)
        {
            var e = Get(levelId, false);
            return e != null ? e.bestDeaths : -1;
        }

        // ---- mutations -------------------------------------------------------------------------

        public static void Unlock(string levelId)
        {
            var e = Get(levelId, true);
            if (e == null || e.unlocked) return;
            e.unlocked = true;
            Save();
        }

        /// <summary>
        /// Record a finished run. Returns true when this beat the stored best, which is the signal the
        /// ghost system uses to decide whether to overwrite the saved ghost.
        /// </summary>
        public static bool RecordCompletion(string levelId, float seconds, int deaths, LevelRegistry registry)
        {
            var e = Get(levelId, true);
            if (e == null) return false;

            e.completed = true;
            e.unlocked = true;
            e.lastPlayedUtc = DateTime.UtcNow.ToString("o");

            bool improved = e.bestTime < 0f || seconds < e.bestTime;
            if (improved)
            {
                e.bestTime = seconds;
                e.bestDeaths = deaths;
            }

            // Finishing a level opens the next one in campaign order.
            if (registry != null)
            {
                int idx = registry.IndexOf(levelId);
                var ordered = registry.Ordered();
                if (idx >= 0 && idx + 1 < ordered.Length)
                {
                    var next = Get(ordered[idx + 1].SafeLevelId, true);
                    if (next != null) next.unlocked = true;
                }
            }

            Save();
            return improved;
        }

        /// <summary>Wipes all progress. Exposed for the test menu and for a real "new game".</summary>
        public static void ResetAll()
        {
            cache = new SaveFile();
            Save();
        }

        public static string SaveFilePath => Path_;
    }
}
