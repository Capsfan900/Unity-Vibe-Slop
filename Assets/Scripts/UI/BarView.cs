using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// Bar with a trailing "ghost" and an optional pulse when full.
    ///
    /// IMPORTANT: the width is driven by the fill RectTransform's anchors, NOT by Image.fillAmount.
    /// Unity's Image ignores type=Filled/fillAmount entirely when the Image has no sprite (it falls
    /// back to a plain quad), which silently left every bar in this HUD rendering permanently full.
    /// Anchor-driving works with or without a sprite and is independent of the pivot.
    /// </summary>
    public class BarView : MonoBehaviour
    {
        public Image fill;
        public Image ghost;
        public bool pulseWhenFull;
        public Color fillColor = Color.white;

        [Tooltip("How fast the trailing ghost bar catches up, in ratio per second.")]
        public float ghostSpeed = 0.8f;

        float target = 1f;
        float ghostValue = 1f;
        float flashUntil = -1f;
        Color flashColor = Color.white;

        bool nearBreak;
        float nearBreakStrength;
        float nearBreakHz = 4.5f;

        public float Value => target;

        /// <summary>True while this bar is beating toward white for a near-break read (see EnemyPostureBar,
        /// NearBreakRatio 0.8 / NearBreakHz 4.5). Any posture-shaped bar should call this every time its
        /// ratio changes so player and boss posture read the same "one more deflect" signal the grunt
        /// world-space bar already gives.</summary>
        public bool NearBreak => nearBreak;

        void Awake()
        {
            if (fill != null) fill.color = fillColor;
            SetFillRatio(fill, target);
            SetFillRatio(ghost, ghostValue);
        }

        public void Set(float ratio)
        {
            target = Mathf.Clamp01(ratio);
            SetFillRatio(fill, target);
            if (target > ghostValue)
            {
                ghostValue = target;
                SetFillRatio(ghost, ghostValue);
            }
        }

        /// <summary>Change the base fill colour (e.g. posture intensifying toward red as it fills).</summary>
        public void SetColor(Color c)
        {
            fillColor = c;
            if (fill != null && !pulseWhenFull && Time.unscaledTime >= flashUntil) fill.color = c;
        }

        /// <summary>Briefly override the fill colour (posture break, big heal, etc). Unscaled time.</summary>
        public void Flash(Color c, float seconds)
        {
            flashColor = c;
            flashUntil = Time.unscaledTime + Mathf.Max(0.01f, seconds);
        }

        /// <summary>
        /// Drive the near-break beat for a posture-shaped bar. Call every time the ratio changes with
        /// ratio &gt;= threshold (see EnemyPostureBar.NearBreakRatio, 0.8) and not broken. strength scales
        /// with how close to break (0 at threshold, 1 at full) so the beat gets harder, not just binary.
        /// </summary>
        public void SetNearBreak(bool active, float strength = 1f, float hz = 4.5f)
        {
            nearBreak = active;
            nearBreakStrength = Mathf.Clamp01(strength);
            nearBreakHz = hz;
        }

        /// <summary>How hard a near-break beat should read at this ratio: 0 right at threshold, 1 at a full
        /// bar. Shared by every posture-shaped bar (player, boss) so "how close to break" always answers
        /// the same way EnemyPostureBar's world-space bar does.</summary>
        public static float NearBreakStrength(float ratio, float threshold)
            => ratio >= threshold ? Mathf.Clamp01((ratio - threshold) / (1f - threshold)) : 0f;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (ghost != null)
            {
                float nv = Mathf.MoveTowards(ghostValue, target, dt * ghostSpeed);
                if (!Mathf.Approximately(nv, ghostValue))
                {
                    ghostValue = nv;
                    SetFillRatio(ghost, ghostValue);
                }
            }

            if (fill == null) return;

            if (Time.unscaledTime < flashUntil)
            {
                fill.color = flashColor;
                return;
            }

            if (pulseWhenFull && target >= 0.999f)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
                fill.color = Color.Lerp(fillColor, Color.white, k * 0.7f);
                return;
            }

            if (nearBreak)
            {
                float beat = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * nearBreakHz * Mathf.PI * 2f);
                fill.color = Color.Lerp(fillColor, Color.white, 0.55f * nearBreakStrength * beat);
                return;
            }

            fill.color = fillColor;
        }

        /// <summary>
        /// Width is the anchor span. fillAmount is forced to 1 so that a Filled image with a sprite
        /// does not shrink a second time on top of the already-narrowed rect.
        /// </summary>
        static void SetFillRatio(Image img, float r)
        {
            if (img == null) return;
            r = Mathf.Clamp01(r);

            bool visible = r > 0.001f;
            if (img.enabled != visible) img.enabled = visible;
            if (!visible) return;

            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(r, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (img.type == Image.Type.Filled) img.fillAmount = 1f;
        }
    }
}
