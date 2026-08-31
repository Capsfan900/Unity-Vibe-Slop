using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Torch flicker: Perlin-noise intensity wobble plus a tiny position jitter. Unscaled time so it
    /// keeps burning during hitstop/pause.
    ///
    /// PERFORMANCE: the level carries ~25 of these. Two things made that cost more than it looked:
    /// five PerlinNoise samples plus a transform write per torch per frame (a transform write dirties
    /// the hierarchy and the light's culling data), and 25 realtime lights against a URP asset that
    /// only allows 4 additional lights per object — most were being culled every frame having
    /// contributed nothing. So a distant torch now disables its Light entirely and stops animating,
    /// and the survivors are staggered across frames. The emissive torch geometry is untouched, which
    /// is where the look actually comes from (that plus bloom); only the dynamic light is dropped.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class FlickerLight : MonoBehaviour
    {
        public float baseIntensity = 2.5f;
        public float amplitude = 0.9f;
        public float speed = 9f;
        public float positionJitter = 0.04f;

        [Header("Culling")]
        [Tooltip("Beyond this distance from the camera the Light is switched off and the flicker stops. " +
                 "The glowing torch mesh stays visible, so the level still reads as lit.")]
        public float cullDistance = 42f;
        [Tooltip("Hysteresis band so a torch sitting exactly on the boundary cannot strobe on and off.")]
        public float cullHysteresis = 4f;
        [Tooltip("Only every Nth frame is animated, offset per torch. 1 = every frame. " +
                 "Flicker is noise, so a slightly coarser step is invisible.")]
        public int frameStride = 2;

        Light lightSource;
        Transform camTransform;
        Vector3 basePos;
        float seed;
        int phase;          // per-instance frame offset, so the strided updates do not all land together
        bool lit = true;

        void Awake()
        {
            lightSource = GetComponent<Light>();
            basePos = transform.localPosition;
            Vector3 p = transform.position;
            seed = (p.x * 12.9898f + p.y * 78.233f + p.z * 37.719f) % 1000f;
            phase = Mathf.Abs(name.GetHashCode());
        }

        void Update()
        {
            // Camera.main does a tagged search; resolve once and re-resolve only if it goes away.
            if (camTransform == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                camTransform = cam.transform;
            }

            // Distance check is cheap and runs every frame so the on/off transition is never late.
            float sqr = (transform.position - camTransform.position).sqrMagnitude;
            float on = cullDistance;
            float off = cullDistance + Mathf.Max(0f, cullHysteresis);
            bool wantLit = lit ? sqr <= off * off : sqr <= on * on;

            if (wantLit != lit)
            {
                lit = wantLit;
                if (lightSource != null) lightSource.enabled = lit;
                if (!lit) transform.localPosition = basePos;   // leave it parked, not mid-jitter
            }
            if (!lit) return;

            int stride = Mathf.Max(1, frameStride);
            if (stride > 1 && ((Time.frameCount + phase) % stride) != 0) return;

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
