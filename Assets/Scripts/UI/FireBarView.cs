using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// The PYRE meter as a fire that burns in the shape of a loading bar.
    ///
    /// <para><b>Sibling of <see cref="BarView"/>, not a replacement.</b> The bar's EXTENT is still the
    /// BarView's anchor-driven fill (rule 5: never <c>Image.fillAmount</c>, and the ghost/flash/colour
    /// language every other bar speaks is untouched). This component adds one full-width
    /// <see cref="flames"/> image above it whose material is <c>VibeGame1/UI/FireBar</c>, and every frame
    /// hands that material the meter (<c>_Fill</c>), a heat that grows with the charge, a kick on a gain,
    /// the full flag and UNSCALED time. The fire is drawn entirely in the shader from procedural noise:
    /// no texture, no particles, one quad.</para>
    ///
    /// <para><b>Per-instance material, never <c>sharedMaterial</c>.</b> The prefab references the shared
    /// <c>M_PyreFire</c> asset; Awake clones it so the per-frame writes cannot dirty the asset or leak
    /// between instances, and OnDestroy disposes the clone.</para>
    ///
    /// <para><b>Unscaled time throughout.</b> A fire that froze in hitstop or under the pause menu would
    /// read as the HUD breaking. <c>_T</c> is handed in rather than read from the shader's <c>_Time</c>,
    /// which is scaled.</para>
    ///
    /// <para><b>Never brighter than 1.0.</b> The shader clamps every channel; <see cref="FireBarMath"/>
    /// keeps heat and kick inside 0..1. Light on screen means "you deflected", and a HUD element must
    /// never out-shout a <c>CueFlash</c>.</para>
    /// </summary>
    [RequireComponent(typeof(BarView))]
    [DisallowMultipleComponent]
    public class FireBarView : MonoBehaviour
    {
        [Tooltip("The full-width image the fire is drawn on: the bar's rect plus the flame headroom " +
                 "above it. Its material must use VibeGame1/UI/FireBar.")]
        public Image flames;

        [Header("Heat — grows with the charge")]
        [Tooltip("heat = fill ^ curve. Below 1 the fire is already lively at a quarter charge; the top " +
                 "of the meter is reserved for the roaring band the _Full flag adds.")]
        public float heatCurve = 0.8f;
        [Tooltip("Kick added to the heat when the meter GAINS (a parry stoked it), 0..1.")]
        [Range(0f, 1f)] public float kickOnGain = 1f;
        [Tooltip("Per-second exponential decay of the kick. 4 = gone in about half a second.")]
        public float kickDecay = 4f;
        [Tooltip("A gain smaller than this fraction of the bar is not a kick (float noise, a trickle).")]
        public float gainThreshold = 0.005f;

        [Header("Full — the ready state pulses, it does not bloom")]
        public float fullPulseHz = 1.4f;
        [Range(0f, 0.5f)] public float fullPulseAmount = 0.15f;

        static readonly int FillId = Shader.PropertyToID("_Fill");
        static readonly int HeatId = Shader.PropertyToID("_Heat");
        static readonly int KickId = Shader.PropertyToID("_Kick");
        static readonly int FullId = Shader.PropertyToID("_Full");
        static readonly int TId = Shader.PropertyToID("_T");

        BarView bar;
        Material mat;
        float lastValue = -1f;
        float kick;

        /// <summary>The heat handed to the shader this frame, 0..1. For tests and the harness.</summary>
        public float Heat { get; private set; }
        /// <summary>The kick handed to the shader this frame, 0..1.</summary>
        public float Kick { get { return kick; } }

        void Awake()
        {
            bar = GetComponent<BarView>();
            if (flames != null && flames.material != null)
            {
                mat = new Material(flames.material) { name = flames.material.name + " (instance)" };
                flames.material = mat;
            }
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }

        void LateUpdate()
        {
            if (mat == null || flames == null) return;
            float dt = Time.unscaledDeltaTime;

            float value = bar != null ? bar.Value : 0f;
            if (lastValue >= 0f && value > lastValue + gainThreshold) kick = Mathf.Max(kick, kickOnGain);
            lastValue = value;
            kick = FireBarMath.DecayKick(kick, kickDecay, dt);

            bool full = value >= 0.999f;
            Heat = FireBarMath.Heat(value, heatCurve, full ? FireBarMath.FullPulse(Time.unscaledTime, fullPulseHz, fullPulseAmount) : 0f);

            bool visible = value > 0.001f;
            if (flames.enabled != visible) flames.enabled = visible;
            if (!visible) return;

            mat.SetFloat(FillId, value);
            mat.SetFloat(HeatId, Heat);
            mat.SetFloat(KickId, kick);
            mat.SetFloat(FullId, full ? 1f : 0f);
            mat.SetFloat(TId, Time.unscaledTime);
        }
    }

    /// <summary>
    /// The arithmetic of the Pyre fire, with no Unity objects in it — the same arrangement
    /// <c>SlideImpulse</c> and <c>ParryImpulse</c> use, so <c>FireBarTests</c> can drive it.
    /// </summary>
    public static class FireBarMath
    {
        /// <summary>Heat 0..1 from the meter: fill^curve, plus an optional bounded pulse, clamped.</summary>
        public static float Heat(float fill, float curve, float pulse)
        {
            float f = Mathf.Clamp01(fill);
            float c = Mathf.Max(0.05f, curve);
            return Mathf.Clamp01(Mathf.Pow(f, c) + Mathf.Max(0f, pulse));
        }

        /// <summary>Exponential decay of the gain kick, snapped to exactly zero once negligible.</summary>
        public static float DecayKick(float kick, float ratePerSecond, float dt)
        {
            if (kick <= 0f || dt <= 0f) return Mathf.Max(0f, kick);
            float k = kick * Mathf.Exp(-Mathf.Max(0f, ratePerSecond) * dt);
            return k < 0.001f ? 0f : k;
        }

        /// <summary>The ready-state breath, 0..amount. A pulse, never a bloom.</summary>
        public static float FullPulse(float unscaledTime, float hz, float amount)
        {
            return Mathf.Clamp(amount, 0f, 0.5f) * (0.5f + 0.5f * Mathf.Sin(unscaledTime * hz * 2f * Mathf.PI));
        }

        /// <summary>
        /// The shader's colour ramp, reproduced on the CPU so a test can sweep it: ember → flame → core,
        /// with every channel clamped to 1.0 — under the 1.05 bloom threshold.
        /// </summary>
        public static Color Ramp(float temperature, Color ember, Color flame, Color core)
        {
            float t = Mathf.Clamp01(temperature);
            Color c = Color.Lerp(ember, flame, Mathf.Clamp01(t * 1.5f));
            c = Color.Lerp(c, core, Mathf.Clamp01((t - 0.62f) * 2.6f));
            return new Color(Mathf.Min(c.r, 1f), Mathf.Min(c.g, 1f), Mathf.Min(c.b, 1f), 1f);
        }

        /// <summary>
        /// Fraction of the flame headroom a tongue may reach at a given heat and noise sample: the
        /// shader's <c>reach</c>. Bounded to 1 (the rect) by construction, so the fire can never leave
        /// the loading bar's silhouette.
        /// </summary>
        public static float Reach(float heat, float noise, float noise2, bool full)
        {
            float h = Mathf.Clamp01(heat);
            float n = Mathf.Clamp01(noise), n2 = Mathf.Clamp01(noise2);
            float reach = Mathf.Clamp01((n - 0.32f) * 1.9f) * Mathf.Lerp(0.18f, 1f, h) * (0.75f + 0.5f * n2);
            if (full) reach = Mathf.Max(reach, 0.55f + 0.35f * n);
            return Mathf.Clamp01(reach);
        }
    }
}
