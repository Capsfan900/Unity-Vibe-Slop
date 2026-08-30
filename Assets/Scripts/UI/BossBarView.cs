using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    public class BossBarView : MonoBehaviour
    {
        public GameObject root;
        public TMP_Text nameText;
        public BarView health;
        public BarView posture;
        public Image[] pips;

        void OnEnable()
        {
            GameEvents.BossStarted += OnStarted;
            GameEvents.BossHealthChanged += OnHealth;
            GameEvents.BossPostureChanged += OnPosture;
            GameEvents.BossDefeated += OnDefeated;
            GameEvents.PlayerRespawned += Hide;
        }

        void OnDisable()
        {
            GameEvents.BossStarted -= OnStarted;
            GameEvents.BossHealthChanged -= OnHealth;
            GameEvents.BossPostureChanged -= OnPosture;
            GameEvents.BossDefeated -= OnDefeated;
            GameEvents.PlayerRespawned -= Hide;
        }

        void Start() { Hide(); }

        void OnStarted(BossController b)
        {
            if (root != null) root.SetActive(true);
            if (nameText != null && b != null && b.data != null) nameText.text = b.data.displayName;
        }

        void OnHealth(float c, float m, int segments)
        {
            if (health != null) health.Set(m > 0 ? c / m : 0f);
            if (pips != null)
                for (int i = 0; i < pips.Length; i++)
                    if (pips[i] != null) pips[i].enabled = i < segments;
        }

        void OnPosture(float c, float m)
        {
            if (posture != null) posture.Set(m > 0 ? c / m : 0f);
        }

        void OnDefeated() { Invoke(nameof(Hide), 1.5f); }

        void Hide() { if (root != null) root.SetActive(false); }
    }
}
