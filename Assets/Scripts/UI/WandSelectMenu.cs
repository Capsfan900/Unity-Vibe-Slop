using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// The wand loadout screen opened by a <see cref="WandPedestal"/>. Freezes time and picks one wand.
    ///
    /// Open/close is EXACTLY the <see cref="LevelUpMenu"/> / <see cref="PauseMenu"/> pattern: set the
    /// GameState, take one TimeScaleController handle at scale 0, and always release that handle on close.
    /// </summary>
    public class WandSelectMenu : MonoBehaviour
    {
        [Serializable]
        public class Row
        {
            public GameObject root;
            public TMP_Text title;
            public TMP_Text stats;
            public TMP_Text description;
            public Button button;
        }

        public static WandSelectMenu I { get; private set; }

        public GameObject panel;
        public TMP_Text currentText;
        public Button closeButton;
        public Row[] rows;

        WandController wands;
        int timeHandle = -1;
        bool open;

        public bool IsOpen => open;

        /// <summary>Test/debug read-only view of the live time handle. -1 means "nothing held".</summary>
        public int TimeHandle => timeHandle;

        void Awake()
        {
            if (I != null && I != this) { /* keep the first; HUD is a singleton prefab */ }
            I = this;
        }

        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            if (panel != null) panel.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (rows != null)
                for (int i = 0; i < rows.Length; i++)
                {
                    int index = i;   // captured per row, not per loop
                    if (rows[i] != null && rows[i].button != null)
                        rows[i].button.onClick.AddListener(() => Select(index));
                }
        }

        void Update()
        {
            if (!open || InputReader.I == null) return;
            if (InputReader.I.PausePressed) Close();
        }

        public void Open(WandController controller)
        {
            if (open || controller == null || GameManager.I == null || TimeScaleController.I == null) return;
            wands = controller;
            open = true;
            GameManager.I.SetState(GameState.Paused);
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
            // ALWAYS released, on every exit path — a leaked 0-scale handle freezes the game forever.
            if (timeHandle >= 0) { TimeScaleController.I.Release(timeHandle); timeHandle = -1; }
            GameManager.I.SetState(GameState.Playing);
            AudioManager.Play(Sfx.Click, 1f, 0.8f);
        }

        /// <summary>
        /// Shut the menu from anywhere. The pedestal sits ON the spawn point, so a scripted run
        /// (feature suite, DebugHarness) starts with the menu open and time held at zero — every
        /// automated entry point calls this first so it is never fighting a 0-scale handle.
        /// </summary>
        public static void ForceClose()
        {
            if (I != null && I.open) I.Close();
        }

        /// <summary>Equip wand <paramref name="index"/> from the player's loadout and close.</summary>
        public void Select(int index)
        {
            if (wands != null) wands.Equip(index);
            Close();
        }

        void Refresh()
        {
            var loadout = wands != null ? wands.loadout : null;
            if (currentText != null)
            {
                var cur = wands != null ? wands.Current : null;
                currentText.text = cur != null
                    ? $"EQUIPPED   {cur.displayName.ToUpperInvariant()}"
                    : "EQUIPPED   NONE";
            }

            if (rows == null) return;
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                WandData w = loadout != null && i < loadout.Length ? loadout[i] : null;

                if (row.root != null) row.root.SetActive(w != null);
                if (w == null) continue;

                bool equipped = wands != null && wands.Index == i;
                if (row.title != null)
                    row.title.text = (equipped ? "> " : "") + w.displayName.ToUpperInvariant() + "   " + w.kind.ToString().ToUpperInvariant();
                if (row.stats != null)
                    row.stats.text = $"DMG {w.damage:0}    WINDUP {w.windup:0.00}s    RECOVER {w.recover:0.00}s";
                if (row.description != null)
                    row.description.text = w.description;
                // Every row stays clickable, including the equipped one — picking what you already
                // hold is a legitimate way to confirm the loadout and leave.
                if (row.button != null) row.button.interactable = true;
            }
        }
    }
}
