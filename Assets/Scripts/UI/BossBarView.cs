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
            // The flag can still be latched from the last fight (or from the moment before this boss died);
            // a re-shown bar must not open mid-beat.
            if (posture != null) posture.SetNearBreak(false);
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
            if (posture == null) return;
            float r = m > 0 ? c / m : 0f;
            posture.Set(r);
            // Give the boss posture bar the same near-break read the grunt world-space bar has
            // (EnemyPostureBar: NearBreakRatio 0.8, NearBreakHz 4.5) — the boss fight is the moment this
            // signal matters most, and it was previously the one posture bar in the game without it.
            posture.SetNearBreak(r >= EnemyPostureBar.NearBreakRatio, BarView.NearBreakStrength(r, EnemyPostureBar.NearBreakRatio), EnemyPostureBar.NearBreakHz);
        }

        // Broken/defeated shows the break read, not the near-break beat (mirrors EnemyPostureBar's !broken gate).
        void OnDefeated() { if (posture != null) posture.SetNearBreak(false); Invoke(nameof(Hide), 1.5f); }

        void Hide()
        {
            if (posture != null) posture.SetNearBreak(false);
            if (root != null) root.SetActive(false);
        }
    }
}
