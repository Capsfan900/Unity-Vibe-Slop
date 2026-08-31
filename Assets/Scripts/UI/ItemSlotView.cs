using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// One slot in the bottom-centre item row. Slot 0 is the one that fires on [E].
    /// All animation is unscaled so the row still reacts while the world is frozen.
    /// </summary>
    public class ItemSlotView : MonoBehaviour
    {
        public Image background;
        public Image icon;
        public TMP_Text label;
        public TMP_Text keyHint;

        static readonly Color EmptyBg = new Color(1f, 1f, 1f, 0.05f);
        static readonly Color EmptyIcon = new Color(1f, 1f, 1f, 0.09f);

        ItemData item;
        bool isNext;
        Color tint = Color.white;
        float flash;
        float punch;

        public ItemData Item => item;

        /// <summary>HDR item colours blow out the UI, so scale them back into 0..1 for widgets.</summary>
        public static Color Normalize(Color c)
        {
            float m = c.maxColorComponent;
            if (m > 1f) c = new Color(c.r / m, c.g / m, c.b / m, 1f);
            c.a = 1f;
            return c;
        }

        void Awake() { Apply(); }

        public void Set(ItemData newItem, bool next)
        {
            item = newItem;
            isNext = next;
            tint = Normalize(item != null ? item.color : Color.white);

            if (label != null) label.text = item != null ? item.shortLabel : string.Empty;
            if (keyHint != null) keyHint.enabled = item != null && isNext;
            Apply();
        }

        /// <summary>White pop when an item lands in this slot.</summary>
        public void Flash() { flash = 1f; }

        /// <summary>Scale punch when an item is spent.</summary>
        public void Punch() { punch = 1f; }

        void Update()
        {
            if (flash <= 0f && punch <= 0f) return;
            float dt = Time.unscaledDeltaTime;
            flash = Mathf.MoveTowards(flash, 0f, dt * 3f);
            punch = Mathf.MoveTowards(punch, 0f, dt * 4f);
            Apply();
        }

        void Apply()
        {
            bool filled = item != null;

            if (background != null)
            {
                Color bg = filled
                    ? new Color(tint.r, tint.g, tint.b, isNext ? 0.34f : 0.18f)
                    : EmptyBg;
                background.color = Color.Lerp(bg, Color.white, flash * 0.8f);
            }

            if (icon != null)
            {
                Color ic = filled ? tint : EmptyIcon;
                if (filled && !isNext) ic.a = 0.7f;
                icon.color = Color.Lerp(ic, Color.white, flash * 0.9f);
            }

            if (label != null)
                label.color = Color.Lerp(filled ? Color.white : new Color(1f, 1f, 1f, 0.25f), Color.white, flash);

            transform.localScale = Vector3.one * (1f + 0.28f * punch + 0.08f * flash);
        }
    }
}
