using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Where leaderboard rows and ghost blobs live.
    ///
    /// Everything is callback-shaped even though the local implementation answers immediately. That is the
    /// whole point: a remote backend is a UnityWebRequest away, and swapping it in must not change a single
    /// call site. The design doc's §6 backend is exactly these three operations —
    /// <c>POST /runs</c>, <c>GET /leaderboard</c>, <c>GET /runs/{id}/ghost</c>.
    ///
    /// A remote implementation additionally needs, per §7:
    ///   - Auth: an OIDC access token on every request; never roll your own. Bind the token to the account.
    ///   - Validation: the server decides the time, never the client. <see cref="RunEntry.verified"/> is
    ///     server-set — a locally recorded run is ALWAYS unverified, and this class must never set it true.
    ///   - Anti-cheat: submissions carry the input stream too (§7.5) so the server can re-simulate the top
    ///     N; plausibility checks (time floor, speed vs FirstPersonMotor limits, checkpoint order) run on
    ///     every submission.
    ///   - Transport: TLS 1.3 only, a hard payload cap (~2 MB) on ghost uploads, per-account rate limits.
    /// The client is never trusted. Nothing in this file should ever be read back as authoritative.
    /// </summary>
    public interface ILeaderboardBackend
    {
        string Name { get; }
        void Submit(GhostRecording recording, Action<RunEntry> onDone);
        void FetchTop(string level, int count, Action<RunEntry[]> onDone);
        void FetchGhost(string runId, Action<GhostRecording> onDone);
        void Clear(string level);
    }

    /// <summary>Disk-backed implementation. Synchronous underneath, async-shaped on the surface.</summary>
    public class LocalLeaderboardBackend : ILeaderboardBackend
    {
        public string Name => "Local";

        public void Submit(GhostRecording recording, Action<RunEntry> onDone)
        {
            var entry = RunStore.SaveRun(recording);
            if (onDone != null) onDone(entry);
        }

        public void FetchTop(string level, int count, Action<RunEntry[]> onDone)
        {
            var idx = RunStore.LoadIndex(level);
            int n = Mathf.Min(count, idx.entries.Length);
            var top = new RunEntry[n];
            Array.Copy(idx.entries, top, n);
            if (onDone != null) onDone(top);
        }

        public void FetchGhost(string runId, Action<GhostRecording> onDone)
        {
            if (onDone != null) onDone(RunStore.LoadGhost(runId));
        }

        public void Clear(string level) => RunStore.ClearLevel(level);
    }

    /// <summary>
    /// The leaderboard model. Owns the backend and caches the current level's rows.
    /// Rendering lives in <see cref="GhostHud"/> and <see cref="WorldLeaderboardView"/>;
    /// this class holds no UI.
    /// </summary>
    public class Leaderboard : MonoBehaviour
    {
        public const int DisplayCount = 8;

        public static Leaderboard I { get; private set; }

        public ILeaderboardBackend Backend { get; private set; } = new LocalLeaderboardBackend();
        public RunEntry[] Top { get; private set; } = new RunEntry[0];
        public RunEntry PersonalBest => Top.Length > 0 ? Top[0] : null;

        public event Action Changed;

        string level;

        void Awake()
        {
            if (I != null && I != this) { Destroy(this); return; }
            I = this;
            level = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        void OnDestroy() { if (I == this) I = null; }

        void Start() => Refresh();

        /// <summary>Swap in a different backend (a remote one, later). Refreshes immediately.</summary>
        public void SetBackend(ILeaderboardBackend backend)
        {
            if (backend == null) return;
            Backend = backend;
            Refresh();
        }

        public void Refresh()
        {
            Backend.FetchTop(level, DisplayCount, rows =>
            {
                Top = rows ?? new RunEntry[0];
                if (Changed != null) Changed();
            });
        }

        /// <summary>Submit a finished run. Returns the row via callback, or null if it did not place.</summary>
        public void Submit(GhostRecording recording, Action<RunEntry> onDone)
        {
            if (recording == null) { if (onDone != null) onDone(null); return; }
            Backend.Submit(recording, entry =>
            {
                Refresh();
                if (onDone != null) onDone(entry);
            });
        }

        public void LoadPersonalBestGhost(Action<GhostRecording> onDone)
        {
            var pb = PersonalBest;
            if (pb == null) { if (onDone != null) onDone(null); return; }
            Backend.FetchGhost(pb.id, onDone);
        }

        [ContextMenu("Ghost/Clear This Level's Leaderboard")]
        public void ClearLevel()
        {
            Backend.Clear(level);
            Refresh();
            Debug.Log("[Leaderboard] Cleared " + level);
        }

        [ContextMenu("Ghost/Log Leaderboard")]
        public void LogLeaderboard()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Leaderboard] " + level + " via " + Backend.Name);
            if (Top.Length == 0) sb.AppendLine("  (no runs yet)");
            for (int i = 0; i < Top.Length; i++)
            {
                var e = Top[i];
                sb.AppendLine($"  {i + 1}. {SpeedrunTimer.Format(e.TimeSeconds)}  deaths={e.deaths}  {e.dateIso}{(e.verified ? "  [verified]" : "")}");
            }
            sb.AppendLine(RunStore.DescribeStorage(level));
            Debug.Log(sb.ToString());
        }
    }
}
