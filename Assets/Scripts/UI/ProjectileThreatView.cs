using UnityEngine;

namespace VibeGame1
{
    /// <summary>Quiet crosshair bracket shown only while an incoming bolt is in its existing parry cue window.</summary>
    public class ProjectileThreatView : MonoBehaviour
    {
        public CanvasGroup group;
        public RectTransform bracket;
        public float pulseHz = 7f;
        public float maxAlpha = 0.32f;

        const float Epsilon = 0.001f;

        /// <summary>Pure read of the existing bolt forecasts; this does not create or alter a threat.</summary>
        public static bool IsActionable(float now)
        {
            return BoltRegistry.AnyCuedImpactBefore(now + Projectile.CueLead + Epsilon);
        }

        void Update()
        {
            float now = Time.time;
            bool active = IsActionable(now);
            if (!active)
            {
                if (group != null) group.alpha = 0f;
                if (bracket != null) bracket.localScale = Vector3.one;
                return;
            }

            // Unscaled animation keeps the cue visible through hitstop, while the active test above
            // remains on the same scaled clock as the projectile forecast.
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseHz * Mathf.PI * 2f);
            if (group != null) group.alpha = maxAlpha * (0.72f + 0.28f * wave);
            if (bracket != null)
                bracket.localScale = Vector3.one * (1f + 0.035f * wave);
        }
    }
}
