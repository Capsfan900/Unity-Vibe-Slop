using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>Filled-image bar with a trailing "ghost" and an optional pulse when full.</summary>
    public class BarView : MonoBehaviour
    {
        public Image fill;
        public Image ghost;
        public bool pulseWhenFull;
        public Color fillColor = Color.white;

        float target = 1f;
        float ghostValue = 1f;

        void Awake()
        {
            if (fill != null) fill.color = fillColor;
        }

        public void Set(float ratio)
        {
            target = Mathf.Clamp01(ratio);
            if (fill != null) fill.fillAmount = target;
            if (target > ghostValue) ghostValue = target;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (ghost != null)
            {
                ghostValue = Mathf.MoveTowards(ghostValue, target, dt * 0.8f);
                ghost.fillAmount = ghostValue;
            }
            if (fill != null && pulseWhenFull)
            {
                if (target >= 0.999f)
                {
                    float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
                    fill.color = Color.Lerp(fillColor, Color.white, k * 0.7f);
                }
                else fill.color = fillColor;
            }
        }
    }
}
