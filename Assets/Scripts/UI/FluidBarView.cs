using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// The MATHS of a sloshing bar, with no Unity objects in it — the same arrangement <see cref="SlideImpulse"/>
    /// has with <see cref="SlideFx"/>. <see cref="FluidBarView"/> is the thin layer that hands these numbers to
    /// the shader.
    ///
    /// <para><b>The whole brief is "not too much, just enough".</b> A liquid in a tube tilts against the
    /// acceleration of the tube and rings back. So the surface tilt is a damped spring whose TARGET is the
    /// current lateral acceleration (scaled and hard-clamped), which gives a small lean under sustained
    /// acceleration and a ring-down when it stops — and the clamp is applied to the target AND the state, so a
    /// dash landing cannot throw the surface out of the bar.</para>
    /// </summary>
    public static class FluidSlosh
    {
        /// <summary>Surface tilt the fluid is pulled toward for a lateral acceleration, height-fractions per
        /// bar width. Sign: accelerate RIGHT, the liquid piles up on the LEFT (surface higher on the left =
        /// negative tilt in the shader's x - 0.5 convention).</summary>
        public static float TiltTarget(float lateralAccel, float gain, float maxTilt)
        {
            if (maxTilt < 0f) maxTilt = 0f;
            return Mathf.Clamp(-lateralAccel * gain, -maxTilt, maxTilt);
        }

        /// <summary>One frame of the tilt spring. Closed form, so 20 fps and 240 fps ring identically.</summary>
        public static void Step(ref float tilt, ref float tiltVel, float target, float hz, float damping,
                                float maxTilt, float dt)
        {
            float nx, nv;
            SlideImpulse.Spring(tilt, tiltVel, target, 2f * Mathf.PI * Mathf.Max(0.05f, hz), damping, dt,
                                out nx, out nv);
            tilt = Mathf.Clamp(nx, -maxTilt, maxTilt);
            tiltVel = nv;
        }

        /// <summary>How far the fluid LEVEL dips on a landing, as a fraction of the bar height: the liquid
        /// compresses under the impact and springs back. Zero under the same 8% threshold the camera dip uses.</summary>
        public static float LandingDip(float landingSpeed, float fullSpeed, float maxDip)
        {
            float k = Mathf.Clamp01(landingSpeed / Mathf.Max(0.01f, fullSpeed));
            return k < 0.08f ? 0f : maxDip * k;
        }
    }

    /// <summary>
    /// Turns a <see cref="BarView"/>'s fill into a fluid: the surface, meniscus, wave, flow and glow live in
    /// <c>VibeGame1/UI/FluidBar</c>; this component drives that shader and makes the liquid JOSTLE with the
    /// player.
    ///
    /// <para><b>The BarView keeps the bar.</b> The fill's EXTENT is still the RectTransform anchors BarView
    /// writes (the project rule: never <c>Image.fillAmount</c>), its colour is still <c>Image.color</c> (so the
    /// existing flash and pulse behaviour reads through unchanged), and nothing else in the HUD knows this
    /// component exists. The shader only decides which pixels INSIDE that rect are liquid and how they look.</para>
    ///
    /// <para><b>The jostle.</b> The player's acceleration, read off the motor's velocity delta and projected
    /// onto the camera's right axis, drives a damped spring that tilts the surface (<see cref="FluidSlosh"/>);
    /// forward acceleration kicks the wave phase, so a dash sends a ripple along the bar; a landing dips the
    /// level and it springs back. All three are hard-clamped and settle in about 0.4 s. Both bars read the same
    /// motor, so they slosh as one liquid.</para>
    ///
    /// <para>Unscaled time throughout: this is UI, and a liquid frozen by hitstop reads as a dropped frame.
    /// One material clone per Image, set once, written with <c>Material.SetFloat</c> — never
    /// <c>sharedMaterial</c>, which would tie every bar to one set of numbers. No per-frame allocation.</para>
    /// </summary>
    [RequireComponent(typeof(BarView))]
    [DisallowMultipleComponent]
    public class FluidBarView : MonoBehaviour
    {
        [Header("Shader")]
        public Shader shader;
        [Tooltip("Bar width over bar height. Converts the leading-edge meniscus into height units so it is the " +
                 "same thickness on screen as the surface meniscus. Written by HudExtensions from the rect.")]
        public float aspect = 23.3f;

        [Header("Fluid look")]
        [Range(0f, 1f)] public float level = 0.86f;
        [Range(0f, 0.3f)] public float wave = 0.045f;
        [Range(0.25f, 6f)] public float waveLength = 1.6f;
        [Range(-3f, 3f)] public float flow = 0.32f;
        [Tooltip("Meniscus brightness. The shader brightens toward white and never past 1.0 in any channel, " +
                 "so this cannot bloom whatever the value; kept modest so the bar stays a bar, not a light.")]
        [Range(0f, 1f)] public float glow = 0.55f;
        [Range(0f, 1f)] public float deep = 0.35f;
        [Range(0.01f, 0.6f)] public float edge = 0.16f;
        [Tooltip("The ghost (trailing) image gets the same liquid at this alpha, with no glow and no wave, so " +
                 "it reads as the pale line the liquid drained from rather than a white slab above the surface.")]
        [Range(0f, 1f)] public float ghostAlpha = 0.35f;

        [Header("Jostle — not too much, just enough")]
        [Tooltip("Surface tilt per m/s^2 of lateral acceleration, in height-fractions per bar width.")]
        public float tiltGain = 0.012f;
        [Tooltip("Hard cap on the tilt. 0.15 on an 18 px bar is ~2.7 px at either end.")]
        public float maxTilt = 0.15f;
        [Tooltip("Ring-down of the tilt spring. 2.5 Hz / 0.35 settles inside ~0.4 s with two visible swings.")]
        public float tiltHz = 2.5f;
        [Range(0.05f, 1f)] public float tiltDamping = 0.35f;
        [Tooltip("Wave phase kicked per m/s^2 of forward acceleration: a dash sends a ripple down the bar.")]
        public float phaseGain = 0.05f;
        public float maxPhaseRate = 6f;
        [Tooltip("Level dip on a landing at landingFullSpeed, fraction of the bar height, springing back.")]
        public float landingDip = 0.10f;
        public float landingFullSpeed = 22f;
        public float dipHz = 3.5f;
        [Range(0.05f, 1f)] public float dipDamping = 0.5f;
        [Tooltip("Acceleration above this is a teleport or a respawn, not movement, and is ignored.")]
        public float maxAccel = 120f;

        [Header("Pulse")]
        [Tooltip("Meniscus pulse on a DECREASE of the bar (damage, a spend), decaying over pulseSeconds.")]
        [Range(0f, 1f)] public float pulseAmount = 0.6f;
        public float pulseSeconds = 0.35f;

        static readonly int FillId = Shader.PropertyToID("_Fill");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");
        static readonly int LevelId = Shader.PropertyToID("_Level");
        static readonly int WaveId = Shader.PropertyToID("_Wave");
        static readonly int WaveLengthId = Shader.PropertyToID("_WaveLength");
        static readonly int FlowId = Shader.PropertyToID("_Flow");
        static readonly int SloshId = Shader.PropertyToID("_Slosh");
        static readonly int SloshPhaseId = Shader.PropertyToID("_SloshPhase");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int PulseId = Shader.PropertyToID("_Pulse");
        static readonly int DeepId = Shader.PropertyToID("_Deep");
        static readonly int EdgeId = Shader.PropertyToID("_Edge");

        BarView bar;
        Material fillMat, ghostMat;
        FirstPersonMotor motor;
        Transform cam;
        float nextMotorSearch;
        Vector3 lastVel;
        bool haveLastVel;
        float tilt, tiltVel;
        float phase, phaseRate;
        float dip, dipVel;
        float pulse;
        float lastValue = -1f;

        /// <summary>The surface tilt being rendered this frame. For tests and the harness.</summary>
        public float Tilt { get { return tilt; } }
        /// <summary>The level dip being rendered this frame. For tests and the harness.</summary>
        public float Dip { get { return dip; } }

        void Awake()
        {
            bar = GetComponent<BarView>();
            if (shader == null)
            {
                Debug.LogError("[FluidBarView] " + name + " has no shader. Run VibeGame1/5. Build HUD — " +
                               "HudExtensions.ApplyFluidBars assigns VibeGame1/UI/FluidBar.", this);
                enabled = false;
                return;
            }
            fillMat = Attach(bar.fill, 1f);
            ghostMat = Attach(bar.ghost, ghostAlpha);
            if (ghostMat != null)
            {
                // The ghost is the pale line the liquid drained from: same level, flat surface, no glow.
                ghostMat.SetFloat(WaveId, 0f);
                ghostMat.SetFloat(GlowId, 0f);
                ghostMat.SetFloat(DeepId, 0f);
            }
            WriteStatic();
        }

        Material Attach(Image img, float alpha)
        {
            if (img == null) return null;
            var m = new Material(shader) { name = shader.name + " (" + img.name + ")" };
            // A null-sprite Simple image: uv 0..1 across the rect the anchors carve out, which is the
            // contract the shader's bar-space maths relies on (see the shader header).
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.material = m;
            if (alpha < 1f)
            {
                var c = img.color; c.a *= alpha; img.color = c;
            }
            return m;
        }

        void OnDestroy()
        {
            if (fillMat != null) Destroy(fillMat);
            if (ghostMat != null) Destroy(ghostMat);
            Unhook();
        }

        void OnDisable() { Unhook(); }

        void Unhook()
        {
            if (motor != null) motor.OnLanded -= OnLanded;
            motor = null;
            haveLastVel = false;
        }

        void OnLanded()
        {
            if (motor == null) return;
            dip += FluidSlosh.LandingDip(motor.LastLandingSpeed, landingFullSpeed, landingDip);
        }

        void WriteStatic()
        {
            Write(fillMat, AspectId, aspect); Write(ghostMat, AspectId, aspect);
            Write(fillMat, WaveId, wave);
            Write(fillMat, WaveLengthId, waveLength); Write(ghostMat, WaveLengthId, waveLength);
            Write(fillMat, FlowId, flow); Write(ghostMat, FlowId, flow);
            Write(fillMat, GlowId, glow);
            Write(fillMat, DeepId, deep);
            Write(fillMat, EdgeId, edge); Write(ghostMat, EdgeId, edge);
        }

        static void Write(Material m, int id, float v) { if (m != null) m.SetFloat(id, v); }

        void Update()
        {
            if (fillMat == null || bar == null) return;
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            float now = Time.unscaledTime;

            // ---- the player, found lazily: the HUD is its own prefab and the player can respawn.
            if (motor == null && now >= nextMotorSearch)
            {
                nextMotorSearch = now + 0.5f;
                motor = FindAnyObjectByType<FirstPersonMotor>();
                if (motor != null)
                {
                    motor.OnLanded += OnLanded;
                    var c = Camera.main;
                    cam = c != null ? c.transform : motor.transform;
                    haveLastVel = false;
                }
            }

            // ---- acceleration → tilt target (lateral) and phase kick (forward)
            float tiltTarget = 0f;
            if (motor != null && GameManager.IsPlaying)
            {
                Vector3 v = motor.Velocity;
                if (haveLastVel)
                {
                    Vector3 a = (v - lastVel) / dt;
                    if (a.magnitude <= maxAccel)
                    {
                        Transform basis = cam != null ? cam : motor.transform;
                        float lateral = Vector3.Dot(a, basis.right);
                        Vector3 fwd = basis.forward; fwd.y = 0f;
                        float forward = fwd.sqrMagnitude > 1e-4f ? Vector3.Dot(a, fwd.normalized) : 0f;
                        tiltTarget = FluidSlosh.TiltTarget(lateral, tiltGain, maxTilt);
                        phaseRate = Mathf.Clamp(phaseRate + forward * phaseGain, -maxPhaseRate, maxPhaseRate);
                    }
                }
                lastVel = v;
                haveLastVel = true;
            }

            FluidSlosh.Step(ref tilt, ref tiltVel, tiltTarget, tiltHz, tiltDamping, maxTilt, dt);
            phase += phaseRate * dt;
            phaseRate = Mathf.Lerp(phaseRate, 0f, 1f - Mathf.Exp(-4f * dt));

            // ---- the landing dip springs back to zero; clamped so a hard landing never empties the bar
            float nd, ndv;
            SlideImpulse.Spring(dip, dipVel, 0f, 2f * Mathf.PI * dipHz, dipDamping, dt, out nd, out ndv);
            dip = Mathf.Clamp(nd, -landingDip, landingDip * 1.5f);
            dipVel = ndv;
            if (Mathf.Abs(dip) < 0.0005f && Mathf.Abs(dipVel) < 0.001f) { dip = 0f; dipVel = 0f; }

            // ---- a decrease pulses the meniscus (damage on health, a spend on stamina)
            float value = bar.Value;
            if (lastValue >= 0f && value < lastValue - 1e-4f) pulse = pulseAmount;
            lastValue = value;
            pulse = Mathf.MoveTowards(pulse, 0f, dt * pulseAmount / Mathf.Max(0.01f, pulseSeconds));

            // ---- hand it to the shader
            float lvl = Mathf.Clamp(level - dip, 0.05f, 1f);
            fillMat.SetFloat(FillId, value);
            fillMat.SetFloat(LevelId, lvl);
            fillMat.SetFloat(SloshId, tilt);
            fillMat.SetFloat(SloshPhaseId, phase);
            fillMat.SetFloat(PulseId, pulse);
            if (ghostMat != null)
            {
                // The ghost's own extent is BarView's business; it only needs the same liquid geometry.
                ghostMat.SetFloat(FillId, GhostFill());
                ghostMat.SetFloat(LevelId, lvl);
                ghostMat.SetFloat(SloshId, tilt);
                ghostMat.SetFloat(SloshPhaseId, phase);
            }
        }

        /// <summary>The ghost image's current extent, read back off the anchors BarView wrote.</summary>
        float GhostFill()
        {
            if (bar.ghost == null) return 0f;
            return Mathf.Clamp01(bar.ghost.rectTransform.anchorMax.x);
        }
    }
}
