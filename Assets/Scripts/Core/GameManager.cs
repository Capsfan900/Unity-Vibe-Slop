using UnityEngine;

namespace VibeGame1
{
    public enum GameState { Playing, Paused, LevelUp, Dead, Won, Editing }

    /// <summary>Owns the high level game state and cursor lock. Lives on the Managers prefab.</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        public PlayerStatsData statsData;
        public GameFeelSettings feel;

        public GameState State { get; private set; } = GameState.Playing;

        public static bool IsPlaying => I != null && I.State == GameState.Playing;
        /// <summary>The in-game level editor is open (LevelEditor). Gameplay idles like any non-Playing state; the
        /// look keeps working and the editor owns the cursor.</summary>
        public static bool IsEditing => I != null && I.State == GameState.Editing;

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            // Keep the game loop ticking when the window loses focus. Without this the editor stalls
            // play mode the moment you alt-tab, which also blocks all automated play-mode testing.
            Application.runInBackground = true;
            // Seed the attack-arbitration static from shipped data. It stays a static because it is read
            // in the enemy hot path, but a static ALONE silently resets to its field initialiser on every
            // domain reload, so the tuned value never survived a recompile and could not be inspected.
            if (feel != null) EnemyController.MaxSimultaneousAttackers = feel.maxSimultaneousAttackers;
        }

        void Start()
        {
            SetState(GameState.Playing);
        }

        public void SetState(GameState s)
        {
            State = s;
            bool lockCursor = s == GameState.Playing || s == GameState.Dead;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
        }
    }
}
