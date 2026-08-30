using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Perlin camera shake on a dedicated ShakeRoot transform. Unscaled time, stacking.</summary>
    public class CameraShake : MonoBehaviour
    {
        public static CameraShake I { get; private set; }

        class Shake { public float amp, duration, start, freq, seed; }
        readonly List<Shake> shakes = new List<Shake>();

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public void Add(float amplitude, float seconds, float freq = 25f)
        {
            shakes.Add(new Shake { amp = amplitude, duration = seconds, start = Time.unscaledTime, freq = freq, seed = Random.value * 100f });
        }

        public void Small() { var f = GameManager.I ? GameManager.I.feel : null; Add(f ? f.shakeSmallAmp : 0.06f, f ? f.shakeSmallTime : 0.12f); }
        public void Medium() { var f = GameManager.I ? GameManager.I.feel : null; Add(f ? f.shakeMedAmp : 0.14f, f ? f.shakeMedTime : 0.2f); }
        public void Big() { var f = GameManager.I ? GameManager.I.feel : null; Add(f ? f.shakeBigAmp : 0.3f, f ? f.shakeBigTime : 0.35f); }

        void LateUpdate()
        {
            Vector3 offset = Vector3.zero;
            float rot = 0f;
            float now = Time.unscaledTime;
            for (int i = shakes.Count - 1; i >= 0; i--)
            {
                var s = shakes[i];
                float t = (now - s.start) / s.duration;
                if (t >= 1f) { shakes.RemoveAt(i); continue; }
                float falloff = 1f - t;
                falloff *= falloff;
                float k = now * s.freq;
                offset.x += (Mathf.PerlinNoise(s.seed, k) - 0.5f) * 2f * s.amp * falloff;
                offset.y += (Mathf.PerlinNoise(s.seed + 10f, k) - 0.5f) * 2f * s.amp * falloff;
                rot += (Mathf.PerlinNoise(s.seed + 20f, k) - 0.5f) * 2f * s.amp * 8f * falloff;
            }
            transform.localPosition = offset;
            transform.localRotation = Quaternion.Euler(0f, 0f, rot);
        }
    }
}
