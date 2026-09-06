using System.Text;
using TMPro;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The top-left status strip: one line per HELD item (FIFO order, the front one marked as the one
    /// [E] fires) and one line per ACTIVE EFFECT with a countdown — "WALL SURGE  6.4s", "GOD MODE",
    /// "SPEED x1.5". Nothing shows while the player carries nothing and no effect runs.
    ///
    /// <para>Built by <c>HudBuilder</c>, referenced from <see cref="HUDController.statusStrip"/>.
    /// The item list arrives on <c>GameEvents.ItemsChanged</c>; the effects are read per frame from the
    /// player (the <see cref="StaminaView"/> idiom — only the motor knows how long a surge has left,
    /// and the countdown has to tick). One multi-line TMP label rather than a pool of row objects:
    /// the text is rebuilt only when what it would SHOW changes (tenths of a second, not the raw
    /// float), the same gate the speedrun timer uses, so an idle strip costs nothing per frame.</para>
    ///
    /// <para>Unscaled: the rows must still update while the world is frozen. Plain ASCII: the default
    /// SDF atlas has no dingbats, so the current-item marker is "&gt;", not a triangle.</para>
    /// </summary>
    public class StatusStripView : MonoBehaviour
    {
        public TMP_Text text;

        /// <summary>Ember gold — the shipped HUD's "ready / cost" colour, used for timed effects.</summary>
        static readonly Color EffectColor = new Color(0.878f, 0.627f, 0.188f);   // #E0A030
        /// <summary>Blood — god mode is a cheat and reads as one.</summary>
        static readonly Color GodColor = new Color(1f, 0.227f, 0.102f);          // #FF3A1A

        readonly StringBuilder sb = new StringBuilder(160);
        ItemData[] held = System.Array.Empty<ItemData>();
        FirstPersonMotor motor;
        Health health;
        string shown = "";
        int rows;

        // What the label last showed, quantised the way it is printed.
        int shownSurgeTenths = -1;
        bool shownGod;
        int shownSpeedPct = 100;
        bool itemsDirty = true;

        /// <summary>Number of lines currently on screen. 0 means the strip is blank.</summary>
        public int RowCount => rows;
        public bool IsEmpty => rows == 0;
        /// <summary>The rich-text string on the label. Tests read it; nothing else should.</summary>
        public string Text => shown;

        void OnEnable()
        {
            GameEvents.ItemsChanged += OnItems;
            itemsDirty = true;
        }

        void OnDisable()
        {
            GameEvents.ItemsChanged -= OnItems;
        }

        void Start()
        {
            if (text != null) text.text = "";
            Rebuild();
        }

        void OnItems(ItemData[] items)
        {
            held = items ?? System.Array.Empty<ItemData>();
            itemsDirty = true;
        }

        void Update()
        {
            if (motor == null)
            {
                motor = FindAnyObjectByType<FirstPersonMotor>();
                if (motor != null) health = motor.GetComponent<Health>();
            }

            int surgeTenths = motor != null && motor.IsWallSurging ? Mathf.CeilToInt(motor.WallSurgeRemaining * 10f) : -1;
            bool god = health != null && health.Invulnerable;
            int speedPct = motor != null ? Mathf.RoundToInt(motor.SpeedMultiplier * 100f) : 100;

            if (!itemsDirty && surgeTenths == shownSurgeTenths && god == shownGod && speedPct == shownSpeedPct) return;
            shownSurgeTenths = surgeTenths;
            shownGod = god;
            shownSpeedPct = speedPct;
            itemsDirty = false;
            Rebuild();
        }

        void Rebuild()
        {
            sb.Length = 0;
            rows = 0;

            for (int i = 0; i < held.Length; i++)
            {
                var item = held[i];
                if (item == null) continue;
                string hex = ColorUtility.ToHtmlStringRGB(ItemSlotView.Normalize(item.color));
                string name = string.IsNullOrEmpty(item.displayName) ? item.shortLabel : item.displayName;
                if (rows > 0) sb.Append('\n');
                // The front item is the one [E] fires; the rest queue behind it, dimmed.
                if (i == 0) sb.Append("<color=#").Append(hex).Append(">> ").Append(name.ToUpperInvariant()).Append("</color>");
                else sb.Append("<alpha=#8C><color=#").Append(hex).Append(">  ").Append(name.ToUpperInvariant()).Append("</color><alpha=#FF>");
                rows++;
            }

            if (shownSurgeTenths >= 0)
            {
                if (rows > 0) sb.Append('\n');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(EffectColor)).Append(">WALL SURGE  ")
                  .Append(shownSurgeTenths / 10).Append('.').Append(shownSurgeTenths % 10).Append("s</color>");
                rows++;
            }

            if (shownSpeedPct != 100)
            {
                if (rows > 0) sb.Append('\n');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(EffectColor)).Append(">SPEED x")
                  .Append((shownSpeedPct / 100f).ToString("0.##")).Append("</color>");
                rows++;
            }

            if (shownGod)
            {
                if (rows > 0) sb.Append('\n');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(GodColor)).Append(">GOD MODE</color>");
                rows++;
            }

            shown = sb.ToString();
            if (text != null) text.text = shown;
        }
    }
}
