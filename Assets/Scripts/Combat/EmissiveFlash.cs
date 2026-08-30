using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Drives _EmissionColor on a set of renderers through a MaterialPropertyBlock.
    /// Materials must have the _EMISSION keyword enabled (done in DataFactory/materials).
    /// All timing is unscaled so hitstop does not freeze feedback.
    /// </summary>
    public class EmissiveFlash : MonoBehaviour
    {
        public Renderer[] renderers;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        Color baseColor = Color.black;
        MaterialPropertyBlock mpb;
        Coroutine routine;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0 && renderers[0] != null && renderers[0].sharedMaterial != null &&
                renderers[0].sharedMaterial.HasProperty(EmissionId))
                baseColor = renderers[0].sharedMaterial.GetColor(EmissionId);
            Apply(baseColor);
        }

        public void SetRenderers(params Renderer[] r) { renderers = r; }

        public void SetBase(Color c)
        {
            baseColor = c;
            if (routine == null) Apply(c);
        }

        public void Flash(Color c, float seconds)
        {
            Stop();
            routine = StartCoroutine(FlashCo(c, seconds));
        }

        /// <summary>Lerp from base to target over seconds and hold there until Clear().</summary>
        public void Ramp(Color to, float seconds)
        {
            Stop();
            routine = StartCoroutine(RampCo(to, seconds));
        }

        public void Pulse(Color a, Color b, float period)
        {
            Stop();
            routine = StartCoroutine(PulseCo(a, b, period));
        }

        public void Clear()
        {
            Stop();
            Apply(baseColor);
        }

        void Stop()
        {
            if (routine != null) { StopCoroutine(routine); routine = null; }
        }

        IEnumerator FlashCo(Color c, float s)
        {
            float t = 0f;
            while (t < s)
            {
                Apply(Color.Lerp(c, baseColor, t / s));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Apply(baseColor);
            routine = null;
        }

        IEnumerator RampCo(Color to, float s)
        {
            float t = 0f;
            while (t < s)
            {
                float k = t / s;
                Apply(Color.Lerp(baseColor, to, k * k));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Apply(to);
            // hold
            while (true) yield return null;
        }

        IEnumerator PulseCo(Color a, Color b, float period)
        {
            float t = 0f;
            while (true)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(t / period * Mathf.PI * 2f);
                Apply(Color.Lerp(a, b, k));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        public void Apply(Color c)
        {
            if (renderers == null) return;
            mpb.SetColor(EmissionId, c);
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(mpb);
        }
    }
}
