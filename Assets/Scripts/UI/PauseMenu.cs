using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VibeGame1
{
    public class PauseMenu : MonoBehaviour
    {
        public GameObject panel;
        public Button resumeButton;
        public Button restartButton;
        public Button mainMenuButton;
        public Button quitButton;

        /// <summary>Build index 0 — the front end. See MainMenuBuilder.</summary>
        public const string MainMenuScene = "MainMenu";

        int handle = -1;
        bool open;

        /// <summary>Test/debug read-only view of the live time handle. -1 means "nothing held".</summary>
        public int TimeHandle => handle;

        public bool IsOpen => open;

        void Start()
        {
            if (panel != null) panel.SetActive(false);
            if (resumeButton != null) resumeButton.onClick.AddListener(Close);
            if (restartButton != null) restartButton.onClick.AddListener(RestartFromCheckpoint);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ReturnToMainMenu);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
        }

        void Update()
        {
            if (InputReader.I == null || !InputReader.I.PausePressed) return;
            if (open) Close();
            else if (GameManager.IsPlaying) Open();
        }

        public void Open()
        {
            if (open) return;
            open = true;
            GameManager.I.SetState(GameState.Paused);
            handle = TimeScaleController.I.Request(0f);
            if (panel != null) panel.SetActive(true);
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            if (panel != null) panel.SetActive(false);
            if (handle >= 0) { TimeScaleController.I.Release(handle); handle = -1; }
            GameManager.I.SetState(GameState.Playing);
        }

        /// <summary>
        /// Back to the front end. Close() FIRST and unconditionally: it releases the 0-scale time
        /// handle. Loading a scene while that handle is still held leaves the next scene frozen —
        /// TimeScaleController's own OnDestroy is the only other thing that would restore it, and
        /// relying on destruction order for something the player can feel is not a plan.
        /// </summary>
        public void ReturnToMainMenu()
        {
            PrepareForSceneChange();
            SceneManager.LoadScene(MainMenuScene);
        }

        /// <summary>
        /// Everything <see cref="ReturnToMainMenu"/> must do BEFORE the load, split out so the feature
        /// suite can assert the handle is released without actually leaving the level scene.
        /// </summary>
        public void PrepareForSceneChange()
        {
            Close();
            // The menu scene has no GameManager to unlock the cursor, and MainMenuController's Awake
            // can run before this scene finishes tearing down, so release it here too.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void RestartFromCheckpoint()
        {
            Close();
            var pc = FindAnyObjectByType<PlayerCombat>();
            if (pc != null) pc.Health.TakeDamage(new DamageInfo { damage = 99999f });
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
