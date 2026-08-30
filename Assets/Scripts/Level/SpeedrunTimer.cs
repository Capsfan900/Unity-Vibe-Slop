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

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void OnEnable() { GameEvents.BossDefeated += Stop; }
        void OnDisable() { GameEvents.BossDefeated -= Stop; }

        void Update()
        {
            if (Finished) return;
            var state = GameManager.I != null ? GameManager.I.State : GameState.Playing;
            if (!Running)
            {
                if (state == GameState.Playing && InputReader.I != null && InputReader.I.AnyMovementInput) Running = true;
                return;
            }
            if (state == GameState.Playing || state == GameState.Dead) Elapsed += Time.unscaledDeltaTime;
        }

        void Stop() { Running = false; Finished = true; }

        public static string Format(float seconds)
        {
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return $"{m:00}:{s:00.00}";
        }
    }
}
