using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    public class ScreenFlash : MonoBehaviour
    {
        public static ScreenFlash I { get; private set; }
        public Image image;

        Color color = Color.white;
        float peak, duration, t = 999f;

        void Awake()
        {
            I = this;
            if (image == null) image = GetComponent<Image>();
            if (image != null) { image.raycastTarget = false; image.color = new Color(1, 1, 1, 0); }
        }

        void OnDestroy() { if (I == this) I = null; }

        public void Flash(Color c, float peakAlpha, float seconds)
        {
            // do not let a weaker flash cut a stronger one short
            if (t < duration && peak * (1f - t / duration) > peakAlpha) return;
            color = c; peak = peakAlpha; duration = Mathf.Max(0.01f, seconds); t = 0f;
        }

        void Update()
        {
            if (image == null) return;
            if (t >= duration) { if (image.color.a != 0f) image.color = new Color(color.r, color.g, color.b, 0f); return; }
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t / duration);
            image.color = new Color(color.r, color.g, color.b, peak * k * k);
        }
    }
}
