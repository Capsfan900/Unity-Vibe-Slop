using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Neon White style run timer. Starts on first movement input, stops when the boss dies. Realtime, excludes menus.</summary>
    public class SpeedrunTimer : MonoBehaviour
    {
        public static SpeedrunTimer I { get; private set; }

        public float Elapsed { get; private set; }
        public bool Running { get; private set; }
        public bool Finished { get; private set; }

        /// <summary>
        /// Run lifecycle, for anything that must span exactly one run — currently the ghost recorder.
        /// These are instance events rather than <see cref="GameEvents"/> entries because a run belongs to
        /// a timer, and the design doc's §9 Phase 0 item 5 calls for de-singletoning per-player state:
        /// when there are two players there are two timers, and a static event could not tell them apart.
        /// </summary>
        public event Action RunStarted;
        public event Action RunFinished;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void OnEnable() { GameEvents.BossDefeated += FinishRun; }
        void OnDisable() { GameEvents.BossDefeated -= FinishRun; }

        void Update()
        {
            if (Finished) return;
            var state = GameManager.I != null ? GameManager.I.State : GameState.Playing;
            if (!Running)
            {
                if (state == GameState.Playing && InputReader.I != null && InputReader.I.AnyMovementInput)
                    TryStartRun();
                return;
            }
            if (state == GameState.Playing || state == GameState.Dead) Elapsed += Time.unscaledDeltaTime;
        }

        /// <summary>
        /// Starts the run and raises <see cref="RunStarted"/>. <see cref="Update"/> calls this on the
        /// first movement input — input is still read only there. Idempotent: a run that is already
        /// running, or already finished, is left alone. Returns whether this call started the run.
        /// </summary>
        public bool TryStartRun()
        {
            if (Running || Finished) return false;
            Running = true;
            if (RunStarted != null) RunStarted();
            return true;
        }

        /// <summary>
        /// Freezes the clock and raises <see cref="RunFinished"/> once. Public so the level scorer can
        /// establish the single boss-end ordering: stop time first, snapshot the result second, then
        /// publish that evaluated result to progression, HUD and ghost recording.
        /// </summary>
        public void FinishRun()
        {
            if (Finished) return;
            Running = false;
            Finished = true;
            if (RunFinished != null) RunFinished();
        }

        public static string Format(float seconds)
        {
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return $"{m:00}:{s:00.00}";
        }
    }
}
