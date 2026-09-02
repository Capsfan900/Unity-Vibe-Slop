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
        float fovHoldTarget, fovHold, fovHoldVel;
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

        /// <summary>
        /// A SUSTAINED FOV offset, held until it is set again. Summed with the decaying
        /// <see cref="FovKick"/> rather than replacing it, so a move can have both a punch on entry and
        /// a hold for its duration — which is exactly the shape a slide needs and the reason this
        /// exists. Call with 0 to release it.
        ///
        /// <para>Written every frame by whatever owns the hold. There is deliberately no stacking and
        /// no priority: <c>cam.fieldOfView</c> has ONE writer (this Update) and the hold has one writer
        /// at a time (<see cref="SlideFx"/>). Two systems holding the FOV at once would fight, and this
        /// project has lost a feature to two writers on one channel twice.</para>
        ///
        /// <para>Eased rather than applied raw. The slide's hold falls to exactly zero as the slide
        /// decays to its end speed, so releasing it is normally continuous anyway — but a slide
        /// jump-cancelled at full speed drops the target several degrees in one frame, and the ease is
        /// what stops that being a visible pop.</para>
        /// </summary>
        public void FovHold(float degrees) { fovHoldTarget = degrees; }

        /// <summary>The FOV offset actually being rendered this frame, kick plus hold. For tests.</summary>
        public float FovOffset { get { return fovKick + fovHold; } }

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
            // 0.09 s: fast enough that the hold is up while the entry kick is still peaking, slow
            // enough that a jump-cancel out of a fast slide is a settle rather than a step.
            fovHold = Mathf.SmoothDamp(fovHold, fovHoldTarget, ref fovHoldVel, 0.09f, Mathf.Infinity, dt);
            if (fovHoldTarget == 0f && Mathf.Abs(fovHold) < 0.01f) { fovHold = 0f; fovHoldVel = 0f; }
            if (cam != null) cam.fieldOfView = baseFov + fovKick + fovHold;

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
