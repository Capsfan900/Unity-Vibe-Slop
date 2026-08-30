using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>Dark Souls style level-up screen. Opens with Tab, freezes time, spends souls on stats.</summary>
    public class LevelUpMenu : MonoBehaviour
    {
        [Serializable]
        public class Row
        {
            public StatType type;
            public TMP_Text label;
            public TMP_Text cost;
            public Button button;
        }

        public GameObject panel;
        public TMP_Text soulsText;
        public TMP_Text summaryText;
        public Button closeButton;
        public Row[] rows;
        public UpgradeTable table;

        PlayerStats stats;
        int timeHandle = -1;
        bool open;

        void Start()
        {
            stats = FindAnyObjectByType<PlayerStats>();
            if (panel != null) panel.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (rows != null)
                foreach (var r in rows)
                {
                    var row = r;
                    if (row.button != null) row.button.onClick.AddListener(() => Buy(row.type));
                }
        }

        void Update()
        {
            if (InputReader.I == null) return;
            if (InputReader.I.LevelUpPressed)
            {
                if (open) Close();
                else if (GameManager.IsPlaying) Open();
            }
            else if (open && InputReader.I.PausePressed) Close();
        }

        public void Open()
        {
            if (open || stats == null) return;
            open = true;
            GameManager.I.SetState(GameState.LevelUp);
            timeHandle = TimeScaleController.I.Request(0f);
            if (panel != null) panel.SetActive(true);
            Refresh();
            AudioManager.Play(Sfx.Click);
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            if (panel != null) panel.SetActive(false);
            if (timeHandle >= 0) { TimeScaleController.I.Release(timeHandle); timeHandle = -1; }
            GameManager.I.SetState(GameState.Playing);
            AudioManager.Play(Sfx.Click, 1f, 0.8f);
        }

        void Buy(StatType t)
        {
            if (stats == null || table == null) return;
            int level = stats.GetLevel(t);
            if (level >= table.maxLevel) return;
            int cost = table.Cost(level);
            if (SoulsWallet.I == null || !SoulsWallet.I.TrySpend(cost)) { AudioManager.Play(Sfx.Click, 0.5f, 0.5f); return; }
            stats.Increase(t);
            AudioManager.Play(Sfx.Souls, 0.8f, 1.4f);
            Refresh();
        }

        static string Describe(StatType t)
        {
            switch (t)
            {
                case StatType.Vitality: return "VITALITY   +10 max HP";
                case StatType.Strength: return "STRENGTH   hammer / sword damage";
                case StatType.Dexterity: return "DEXTERITY  dagger / sword damage";
                case StatType.Arcane: return "ARCANE     parry juice + ultimate";
                case StatType.Flask: return "FLASK      +heal / +charge";
            }
            return t.ToString();
        }

        void Refresh()
        {
            int souls = SoulsWallet.I != null ? SoulsWallet.I.Souls : 0;
            if (soulsText != null) soulsText.text = $"SOULS  {souls}";
            if (summaryText != null && stats != null)
                summaryText.text = $"LEVEL {stats.TotalLevel}    HP {stats.MaxHP:F0}    FLASK {stats.FlaskCharges} x {stats.FlaskHeal:F0}";
            if (rows == null || table == null || stats == null) return;
            foreach (var r in rows)
            {
                int level = stats.GetLevel(r.type);
                int cost = table.Cost(level);
                bool maxed = level >= table.maxLevel;
                if (r.label != null) r.label.text = $"{Describe(r.type)}    [{level}]";
                if (r.cost != null) r.cost.text = maxed ? "MAX" : cost.ToString();
                if (r.button != null) r.button.interactable = !maxed && souls >= cost;
            }
        }
    }
}
