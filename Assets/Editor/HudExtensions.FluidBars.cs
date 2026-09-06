using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// The FLUID BARS pass: health and stamina become liquid in a tube (<see cref="FluidBarView"/> +
    /// <c>VibeGame1/UI/FluidBar</c>). Finds each bar by NAME on the finished HUD, attaches the component and
    /// writes every number (hard rule 9: a field initialiser is not a shipped value).
    ///
    /// <para>The BarView underneath is untouched: it still drives the fill's extent through the anchors and
    /// its colour through <c>Image.color</c>, so the refusal flash, the damage read and every test that
    /// reads <c>BarView.Value</c> work exactly as before. Only the pixels inside the fill change.</para>
    /// </summary>
    public static partial class HudExtensions
    {
        const string FluidShaderName = "VibeGame1/UI/FluidBar";

        static void ApplyFluidBars(GameObject hudRoot)
        {
            var shader = Shader.Find(FluidShaderName);
            if (shader == null)
            {
                Debug.LogError("[HudExtensions] Shader '" + FluidShaderName + "' not found. Is " +
                               "Assets/Shaders/UI/FluidBar.shader present and compiling? The health and " +
                               "stamina bars are left as flat fills.");
                return;
            }

            // Health: 420 x 18, blood red, with a ghost. The deeper, slower liquid of the two.
            Fluid(hudRoot, "HealthBar", shader, level: 0.86f, wave: 0.045f, waveLength: 1.6f, flow: 0.32f,
                  glow: 0.55f, deep: 0.35f, edge: 0.16f);
            // Stamina: 420 x 10, cold cyan, no ghost. Thinner bar, so a slightly higher level and a
            // finer, quicker ripple; it is spent and refilled constantly and should look alive.
            Fluid(hudRoot, "StaminaBar", shader, level: 0.88f, wave: 0.06f, waveLength: 2.2f, flow: 0.45f,
                  glow: 0.6f, deep: 0.25f, edge: 0.22f);
        }

        static void Fluid(GameObject hudRoot, string barName, Shader shader, float level, float wave,
                          float waveLength, float flow, float glow, float deep, float edge)
        {
            BarView bar = null;
            foreach (var b in hudRoot.GetComponentsInChildren<BarView>(true))
                if (b.name == barName) { bar = b; break; }
            if (bar == null)
            {
                Debug.LogError("[HudExtensions] No BarView named '" + barName + "' on the HUD; the fluid pass " +
                               "skipped it. HudBuilder must keep that name.");
                return;
            }

            var fluid = bar.GetComponent<FluidBarView>();
            if (fluid == null) fluid = bar.gameObject.AddComponent<FluidBarView>();
            fluid.shader = shader;

            var rt = bar.GetComponent<RectTransform>();
            Vector2 size = rt != null ? rt.sizeDelta : new Vector2(420f, 18f);
            fluid.aspect = size.y > 0.01f ? size.x / size.y : 23.3f;

            fluid.level = level;
            fluid.wave = wave;
            fluid.waveLength = waveLength;
            fluid.flow = flow;
            fluid.glow = glow;
            fluid.deep = deep;
            fluid.edge = edge;
            fluid.ghostAlpha = 0.35f;

            // The jostle. "Not too much, just enough": 0.012 tilt per m/s^2 means a hard 20 m/s^2 strafe
            // start leans the surface 0.24 of the cap and a dash landing hits the 0.15 cap — ~2.7 px on
            // the 18 px health bar — then rings down at 2.5 Hz / 0.35 inside ~0.4 s.
            fluid.tiltGain = 0.012f;
            fluid.maxTilt = 0.15f;
            fluid.tiltHz = 2.5f;
            fluid.tiltDamping = 0.35f;
            fluid.phaseGain = 0.05f;
            fluid.maxPhaseRate = 6f;
            fluid.landingDip = 0.10f;
            fluid.landingFullSpeed = 22f;
            fluid.dipHz = 3.5f;
            fluid.dipDamping = 0.5f;
            fluid.maxAccel = 120f;
            fluid.pulseAmount = 0.6f;
            fluid.pulseSeconds = 0.35f;

            // The fill and ghost images become null-sprite Simple images at build time as well as at
            // Awake, so the prefab's serialised state already matches the shader's UV contract.
            Simple(bar.fill);
            Simple(bar.ghost);

            Debug.Log("[HudExtensions] " + barName + " → fluid (aspect " + fluid.aspect.ToString("F1") +
                      ", level " + level + ", wave " + wave + ").");
        }

        static void Simple(UnityEngine.UI.Image img)
        {
            if (img == null) return;
            img.sprite = null;
            img.type = UnityEngine.UI.Image.Type.Simple;
        }
    }
}
