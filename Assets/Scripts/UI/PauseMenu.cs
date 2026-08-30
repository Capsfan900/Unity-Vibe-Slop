using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    public class PauseMenu : MonoBehaviour
    {
        public GameObject panel;
        public Button resumeButton;
        public Button restartButton;
        public Button quitButton;

        int handle = -1;
        bool open;

        void Start()
        {
            if (panel != null) panel.SetActive(false);
            if (resumeButton != null) resumeButton.onClick.AddListener(Close);
            if (restartButton != null) restartButton.onClick.AddListener(RestartFromCheckpoint);
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
