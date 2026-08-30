using UnityEngine;

namespace VibeGame1
{
    public enum GameState { Playing, Paused, LevelUp, Dead, Won }

    /// <summary>Owns the high level game state and cursor lock. Lives on the Managers prefab.</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        public PlayerStatsData statsData;
        public GameFeelSettings feel;

        public GameState State { get; private set; } = GameState.Playing;

        public static bool IsPlaying => I != null && I.State == GameState.Playing;

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
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
