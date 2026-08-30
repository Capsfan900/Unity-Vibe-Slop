using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VibeGame1
{
    /// <summary>FOV kicks and post-processing pulses (chromatic aberration, vignette). Unscaled time.</summary>
    public class CameraFX : MonoBehaviour
    {
        public static CameraFX I { get; private set; }

        public Camera cam;
        public Volume volume;
        public float baseFov = 95f;

        ChromaticAberration chroma;
        Vignette vignette;

        float fovKick, fovKickVel;
        float chromaAmount, chromaDecay;
        float vigAmount, vigDecay;
        float baseVignette = 0.32f;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            if (cam == null) cam = GetComponentInChildren<Camera>();
            if (volume == null) volume = FindAnyObjectByType<Volume>();
            if (cam != null) baseFov = cam.fieldOfView;
            if (volume != null && volume.profile != null)
            {
                volume.profile.TryGet(out chroma);
                if (volume.profile.TryGet(out vignette)) baseVignette = vignette.intensity.value;
            }
        }

        public void FovKick(float delta) { fovKick = delta; }

        public void ChromaticPulse(float intensity, float seconds)
        {
            chromaAmount = Mathf.Max(chromaAmount, intensity);
            chromaDecay = intensity / Mathf.Max(0.01f, seconds);
        }

        public void VignettePulse(float intensity, float seconds)
        {
            vigAmount = Mathf.Max(vigAmount, intensity);
            vigDecay = intensity / Mathf.Max(0.01f, seconds);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            fovKick = Mathf.SmoothDamp(fovKick, 0f, ref fovKickVel, 0.18f, Mathf.Infinity, dt);
            if (cam != null) cam.fieldOfView = baseFov + fovKick;

            if (chroma != null)
            {
                chromaAmount = Mathf.Max(0f, chromaAmount - chromaDecay * dt);
                chroma.intensity.value = chromaAmount;
            }
            if (vignette != null)
            {
                vigAmount = Mathf.Max(0f, vigAmount - vigDecay * dt);
                vignette.intensity.value = baseVignette + vigAmount;
            }
        }
    }
}
