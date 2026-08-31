using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Local persistence for runs. Two kinds of file, split the same way the backend would be:
    ///
    ///   runs_&lt;level&gt;.json    the leaderboard index — metadata rows only, small, read constantly
    ///   ghost_&lt;id&gt;.json      one recording blob per run — large, read only when a ghost is loaded
    ///
    /// Nothing here throws. A missing file is a first run; a corrupt file is treated as missing and moved
    /// aside rather than deleted, so a bad write can still be inspected.
    /// </summary>
    public static class RunStore
    {
        /// <summary>How many leaderboard rows to keep per level. Blobs beyond this are deleted.</summary>
        public const int MaxEntries = 10;

        static string Root => Application.persistentDataPath;
        static string IndexPath(string level) => Path.Combine(Root, "runs_" + Sanitize(level) + ".json");
        static string GhostPath(string id) => Path.Combine(Root, "ghost_" + Sanitize(id) + ".json");

        static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
            return new string(chars);
        }

        // ---- index ---------------------------------------------------------------------------------

        public static RunIndex LoadIndex(string level)
        {
            var idx = ReadJson<RunIndex>(IndexPath(level));
            if (idx == null) idx = new RunIndex();
            if (idx.entries == null) idx.entries = new RunEntry[0];
            idx.level = level;
            SortByTime(idx.entries);
            return idx;
        }

        public static RunEntry LoadPersonalBest(string level)
        {
            var idx = LoadIndex(level);
            return idx.entries.Length > 0 ? idx.entries[0] : null;
        }

        static void SortByTime(RunEntry[] entries)
        {
            if (entries == null) return;
            Array.Sort(entries, (a, b) =>
            {
                if (a == null) return 1;
                if (b == null) return -1;
                return a.timeMs.CompareTo(b.timeMs);
            });
        }

        // ---- write ---------------------------------------------------------------------------------

        /// <summary>
        /// Persist a finished run. Returns the leaderboard row, or null if the recording was unusable.
        /// The blob is only written if the run makes the top <see cref="MaxEntries"/> — there is no point
        /// storing the samples of a run nobody will ever race.
        /// </summary>
        public static RunEntry SaveRun(GhostRecording rec)
        {
            if (rec == null || !rec.IsValid) return null;

            if (string.IsNullOrEmpty(rec.id))
                rec.id = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + UnityEngine.Random.Range(1000, 9999);

            var entry = new RunEntry
            {
                id = rec.id,
                level = rec.level,
                timeMs = rec.timeMs,
                deaths = rec.deaths,
                dateIso = DateTime.UtcNow.ToString("o"),
                verified = false,
            };

            var idx = LoadIndex(rec.level);
            var list = new List<RunEntry>(idx.entries);
            list.Add(entry);
            list.Sort((a, b) => a.timeMs.CompareTo(b.timeMs));

            // Trim, deleting the blobs of anything that fell off the board.
            while (list.Count > MaxEntries)
            {
                var dropped = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                if (dropped != null) DeleteGhost(dropped.id);
            }

            bool madeTheBoard = list.Contains(entry);
            if (madeTheBoard) WriteJson(GhostPath(rec.id), rec);

            idx.entries = list.ToArray();
            WriteJson(IndexPath(rec.level), idx);

            return madeTheBoard ? entry : null;
        }

        // ---- blobs ---------------------------------------------------------------------------------

        public static GhostRecording LoadGhost(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var rec = ReadJson<GhostRecording>(GhostPath(id));
            return rec != null && rec.IsValid ? rec : null;
        }

        public static GhostRecording LoadPersonalBestGhost(string level)
        {
            var pb = LoadPersonalBest(level);
            return pb != null ? LoadGhost(pb.id) : null;
        }

        public static void DeleteGhost(string id)
        {
            try { if (File.Exists(GhostPath(id))) File.Delete(GhostPath(id)); }
            catch (Exception e) { Debug.LogWarning("[RunStore] Could not delete ghost " + id + ": " + e.Message); }
        }

        /// <summary>Wipe everything for a level. Exposed for the test menu / a fresh start.</summary>
        public static void ClearLevel(string level)
        {
            var idx = LoadIndex(level);
            foreach (var e in idx.entries) if (e != null) DeleteGhost(e.id);
            try { if (File.Exists(IndexPath(level))) File.Delete(IndexPath(level)); }
            catch (Exception e) { Debug.LogWarning("[RunStore] Could not delete index: " + e.Message); }
        }

        public static string DescribeStorage(string level)
        {
            long indexBytes = SafeSize(IndexPath(level));
            long blobBytes = 0;
            var idx = LoadIndex(level);
            foreach (var e in idx.entries) if (e != null) blobBytes += SafeSize(GhostPath(e.id));
            return $"{Root}\n  index {indexBytes / 1024f:F1} KB, {idx.entries.Length} entries, blobs {blobBytes / 1024f:F1} KB";
        }

        static long SafeSize(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; } catch { return 0; }
        }

        // ---- json ----------------------------------------------------------------------------------

        static T ReadJson<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                // Move it aside rather than deleting — a corrupt file is evidence.
                Debug.LogWarning("[RunStore] Unreadable, quarantining " + Path.GetFileName(path) + ": " + e.Message);
                try { if (File.Exists(path)) File.Move(path, path + ".corrupt"); } catch { }
                return null;
            }
        }

        static void WriteJson(string path, object value)
        {
            try
            {
                // Write to a temp file and swap, so a crash mid-write cannot corrupt the existing file.
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(value));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RunStore] Write failed for " + Path.GetFileName(path) + ": " + e.Message);
            }
        }
    }
}
