using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Rotates only a solar arena's visible plasma layers. Collision, triggers and NavMesh geometry stay
    /// still, so the effect cannot move a player or an enemy. Counter-rotation keeps motion readable on
    /// a sphere whose silhouette otherwise never changes.
    ///
    /// <para>On an EXTERIOR portal sun it also drives the crossing: the shell parts before the camera can
    /// reach it, and the sun's light closes over the eye as the player descends toward the transport.
    /// <see cref="SolarArenaPortal"/> switches that on at runtime by writing
    /// <see cref="crossingTriggerRadius"/>; the realm-ceiling instance leaves it at zero and behaves
    /// exactly as it always has.</para>
    /// </summary>
    public class SolarArenaVisual : MonoBehaviour
    {
        public Transform plasma;
        public Transform corona;
        public Vector3 plasmaDegreesPerSecond = new Vector3(2f, 7f, 1f);
        public Vector3 coronaDegreesPerSecond = new Vector3(-1f, -4f, 3f);
        [Tooltip("Optional persistent renderer override. Negative keeps the material's authored opacity.")]
        public float plasmaOpacityOverride = -1f;
        [Tooltip("Optional surface-occlusion override. Zero preserves an additive realm ceiling.")]
        public float plasmaSurfaceOpacityOverride = -1f;

        [Header("Portal crossing — written at runtime by SolarArenaPortal")]
        [Tooltip("Gameplay trigger radius in metres. Zero leaves the dissolve and the wash off entirely, " +
                 "which is what every non-portal instance (the realm ceiling) wants.")]
        public float crossingTriggerRadius;
        [Tooltip("Shipped SolarRealmDef.themeMaterialKey, so the wash wears this sun's own colour.")]
        public string crossingThemeKey = "SolarCyan";
        [Tooltip("False once the arena is cleared. The shell still dissolves — a pop is a pop either way " +
                 "— but the wash stops, because a sun that can no longer take you must not claim it can.")]
        public bool crossingArmed;

        static readonly int FadeId = Shader.PropertyToID("_Fade");

        MaterialPropertyBlock block;
        Renderer plasmaRenderer, coronaRenderer;
        float plasmaFade = 1f, coronaFade = 1f;
        Camera eye;

        void OnEnable()
        {
            plasmaRenderer = plasma != null ? plasma.GetComponent<Renderer>() : null;
            coronaRenderer = corona != null ? corona.GetComponent<Renderer>() : null;
            plasmaFade = coronaFade = -1f;      // force one write so a reloaded scene starts consistent
            ApplyMaterialOverrides();
        }

        /// <summary>Applies serialized per-instance material values without instancing the shared asset.</summary>
        public void ApplyMaterialOverrides()
        {
            if (plasma == null || (plasmaOpacityOverride < 0f && plasmaSurfaceOpacityOverride < 0f)) return;
            var renderer = plasma.GetComponent<Renderer>();
            if (renderer == null) return;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            if (plasmaOpacityOverride >= 0f)
                properties.SetFloat("_Alpha", Mathf.Clamp01(plasmaOpacityOverride));
            if (plasmaSurfaceOpacityOverride >= 0f)
                properties.SetFloat("_SurfaceOpacity", Mathf.Clamp01(plasmaSurfaceOpacityOverride));
            renderer.SetPropertyBlock(properties);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (plasma != null) plasma.Rotate(plasmaDegreesPerSecond * dt, Space.Self);
            if (corona != null) corona.Rotate(coronaDegreesPerSecond * dt, Space.Self);
            UpdateCrossing();
        }

        /// <summary>
        /// The drawn radius, taken from the shell's own world scale rather than a second serialized copy
        /// of it — one number, so a resized sun can never disagree with its own dissolve.
        /// </summary>
        public float VisualRadius { get { return Radius(plasma); } }

        static float Radius(Transform shell)
        {
            return shell != null ? Mathf.Abs(shell.lossyScale.x) * 0.5f : 0f;
        }

        void UpdateCrossing()
        {
            if (crossingTriggerRadius <= 0f || plasma == null) return;
            float visualRadius = VisualRadius;
            if (visualRadius <= crossingTriggerRadius) return;

            Camera cam = ActiveCamera();
            if (cam == null) return;
            float distance = Vector3.Distance(cam.transform.position, plasma.position);

            // Each shell parts against ITS OWN radius. The corona is 1.06x the body, so sharing the
            // body's number would leave it drawn for the ~0.7 m after the camera has already crossed it.
            SetFade(plasmaRenderer, ref plasmaFade, SolarTransition.ShellFade(distance, visualRadius));
            SetFade(coronaRenderer, ref coronaFade, SolarTransition.ShellFade(distance, Radius(corona)));

            if (!crossingArmed || ScreenFlash.I == null) return;
            float wash = SolarTransition.Wash(distance, visualRadius, crossingTriggerRadius);
            if (wash > 0f) ScreenFlash.I.RequestWash(SolarTransition.WashTint(crossingThemeKey, wash), wash);
        }

        void SetFade(Renderer target, ref float current, float value)
        {
            if (target == null) return;
            // Ordinary approach frames sit at a flat 1 and cost nothing; only the ~6 m membrane band
            // writes a block. The endpoints are forced through so a fade can never stall just shy of 0.
            bool endpoint = (value <= 0f && current > 0f) || (value >= 1f && current < 1f);
            if (!endpoint && Mathf.Abs(current - value) < 0.004f) return;
            current = value;
            if (block == null) block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetFloat(FadeId, value);
            target.SetPropertyBlock(block);
        }

        Camera ActiveCamera()
        {
            if (eye != null && eye.isActiveAndEnabled) return eye;
            eye = CameraFX.I != null ? CameraFX.I.cam : null;
            if (eye == null) eye = Camera.main;
            return eye;
        }
    }
}
