using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Ghost racing orchestrator: recorder + ghost + leaderboard + HUD, wired together.
    ///
    /// It bootstraps itself with <c>[RuntimeInitializeOnLoadMethod]</c> rather than living in a prefab.
    /// That is deliberate — the whole feature stays additive, so a `4. Build Prefabs` or `6. Build Level`
    /// rebuild can never drop it, and removing ghost racing means deleting one folder.
    ///
    /// Phase 1 of docs/multiplayer-system-design.md. There is no networking here and none is needed: this
    /// is the asynchronous product the design doc recommends shipping first.
    /// </summary>
    public class GhostRacing : MonoBehaviour
    {
        public static GhostRacing I { get; private set; }

        [Tooltip("Race against your personal best automatically when the level starts.")]
        public bool autoLoadPersonalBest = true;

        public RunRecorder Recorder { get; private set; }
        public GhostPlayer Ghost { get; private set; }
        public Leaderboard Board { get; private set; }
        public GhostHud Hud { get; private set; }

        static readonly Color Gold = new Color(1f, 0.84f, 0.35f);
        static readonly Color Plain = new Color(0.85f, 0.87f, 0.92f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // Only in a playable scene. SpeedrunTimer lives on the Managers prefab, so its presence is a
            // reliable "this is a level, not a menu" test.
            if (Object.FindAnyObjectByType<SpeedrunTimer>() == null) return;
            if (Object.FindAnyObjectByType<GhostRacing>() != null) return;

            var go = new GameObject("GhostRacing");
            go.AddComponent<GhostRacing>();
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;

            Recorder = gameObject.AddComponent<RunRecorder>();
            Board = gameObject.AddComponent<Leaderboard>();
            Hud = gameObject.AddComponent<GhostHud>();

            var ghostGo = new GameObject("Ghost");
            ghostGo.transform.SetParent(transform, false);
            Ghost = ghostGo.AddComponent<GhostPlayer>();
        }

        void OnDestroy() { if (I == this) I = null; }

        void OnEnable() { GameEvents.PlayerRespawned += OnRespawned; }
        void OnDisable() { GameEvents.PlayerRespawned -= OnRespawned; }

        void Start()
        {
            if (autoLoadPersonalBest) LoadPersonalBest();
        }

        /// <summary>
        /// Deaths do NOT end a speedrun — the timer keeps counting and the death is recorded as part of the
        /// run. Only the ghost's playback cursor is reset, because the player has jumped back to a
        /// checkpoint and the delta would otherwise be measured against a point they already passed.
        /// </summary>
        void OnRespawned()
        {
            if (Ghost != null) Ghost.ResetPlayback();
        }

        public void LoadPersonalBest()
        {
            if (Board == null || Ghost == null) return;
            Board.LoadPersonalBestGhost(rec =>
            {
                Ghost.Load(rec);
                if (rec != null)
                    Debug.Log($"[GhostRacing] Racing PB {SpeedrunTimer.Format(rec.timeMs / 1000f)} ({rec.samples.Length} samples).");
                else
                    Debug.Log("[GhostRacing] No personal best yet — finish the level to record one.");
            });
        }

        /// <summary>Called by <see cref="RunRecorder"/> when a run ends.</summary>
        public static void NotifyRunRecorded(GhostRecording rec)
        {
            if (I != null) I.SubmitRun(rec);
        }

        void SubmitRun(GhostRecording rec)
        {
            if (rec == null || Board == null) return;

            var previousBest = Board.PersonalBest;
            Board.Submit(rec, entry =>
            {
                if (entry == null)
                {
                    Debug.Log("[GhostRacing] Run finished but did not make the board.");
                    if (Hud != null) Hud.ShowResult(SpeedrunTimer.Format(rec.timeMs / 1000f), Plain);
                    return;
                }

                bool isNewBest = previousBest == null || entry.timeMs < previousBest.timeMs;
                string time = SpeedrunTimer.Format(entry.TimeSeconds);

                if (isNewBest)
                {
                    if (Hud != null) Hud.ShowResult("NEW BEST  " + time, Gold, 5f);
                    Debug.Log("[GhostRacing] New personal best: " + time);
                    // Race the new PB from the next attempt.
                    Board.LoadPersonalBestGhost(g => { if (Ghost != null) Ghost.Load(g); });
                }
                else
                {
                    float delta = (entry.timeMs - previousBest.timeMs) / 1000f;
                    if (Hud != null) Hud.ShowResult(time + "   (+" + delta.ToString("0.00") + "s)", Plain, 5f);
                    Debug.Log($"[GhostRacing] Finished {time}, +{delta:0.00}s off PB.");
                }
            });
        }

        // ---- manual controls (no TestMenu edit — use the component context menu or call from execute_code)

        [ContextMenu("Ghost/Load Personal Best")]
        public void CtxLoadPersonalBest() => LoadPersonalBest();

        [ContextMenu("Ghost/Hide Ghost")]
        public void CtxHideGhost() { if (Ghost != null) Ghost.Clear(); }

        [ContextMenu("Ghost/Save Current Run Now")]
        public void CtxForceSave() { if (Recorder != null) Recorder.ForceSave(); }

        [ContextMenu("Ghost/Log Leaderboard")]
        public void CtxLogBoard() { if (Board != null) Board.LogLeaderboard(); }

        [ContextMenu("Ghost/Clear Leaderboard")]
        public void CtxClear() { if (Board != null) Board.ClearLevel(); if (Ghost != null) Ghost.Clear(); }

        [ContextMenu("Ghost/Toggle Leaderboard Panel")]
        public void CtxToggleBoard() { if (Hud != null) Hud.SetBoardVisible(!Hud.BoardVisible); }

        /// <summary>Status line for the console / test menu.</summary>
        public string Describe()
        {
            var rec = Recorder;
            var g = Ghost;
            return "GhostRacing"
                 + " recording=" + (rec != null && rec.IsRecording)
                 + " samples=" + (rec != null ? rec.SampleCount : 0)
                 + " ghostLoaded=" + (g != null && g.HasRun)
                 + " ghostVisible=" + (g != null && g.Visible)
                 + " entries=" + (Board != null ? Board.Top.Length : 0)
                 + " backend=" + (Board != null ? Board.Backend.Name : "none");
        }
    }
}
