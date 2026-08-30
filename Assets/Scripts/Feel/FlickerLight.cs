using UnityEngine;

namespace VibeGame1
{
    /// <summary>Torch flicker: Perlin-noise intensity wobble plus a tiny position jitter. Unscaled time so it keeps burning during hitstop/pause.</summary>
    [RequireComponent(typeof(Light))]
    public class FlickerLight : MonoBehaviour
    {
        public float baseIntensity = 2.5f;
        public float amplitude = 0.9f;
        public float speed = 9f;
        public float positionJitter = 0.04f;

        Light lightSource;
        Vector3 basePos;
        float seed;

        void Awake()
        {
            lightSource = GetComponent<Light>();
            basePos = transform.localPosition;
            Vector3 p = transform.position;
            seed = (p.x * 12.9898f + p.y * 78.233f + p.z * 37.719f) % 1000f;
        }

        void Update()
        {
            float t = Time.unscaledTime * speed + seed;
            float n = Mathf.PerlinNoise(t, seed * 0.37f) * 2f - 1f;          // -1..1 slow wobble
            float spark = Mathf.PerlinNoise(t * 3.1f, seed * 0.11f) - 0.5f; // faster crackle
            lightSource.intensity = Mathf.Max(0f, baseIntensity + (n * 0.7f + spark * 0.6f) * amplitude);

            float jx = (Mathf.PerlinNoise(t * 0.8f, seed + 1f) - 0.5f) * 2f;
            float jy = (Mathf.PerlinNoise(t * 0.8f, seed + 2f) - 0.5f) * 2f;
            float jz = (Mathf.PerlinNoise(t * 0.8f, seed + 3f) - 0.5f) * 2f;
            transform.localPosition = basePos + new Vector3(jx, jy, jz) * positionJitter;
        }
    }
}
