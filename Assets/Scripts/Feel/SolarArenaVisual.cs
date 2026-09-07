using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Rotates only a solar arena's visible plasma layers. Collision, triggers and NavMesh geometry stay
    /// still, so the effect cannot move a player or an enemy. Counter-rotation keeps motion readable on
    /// a sphere whose silhouette otherwise never changes.
    /// </summary>
    public class SolarArenaVisual : MonoBehaviour
    {
        public Transform plasma;
        public Transform corona;
        public Vector3 plasmaDegreesPerSecond = new Vector3(2f, 7f, 1f);
        public Vector3 coronaDegreesPerSecond = new Vector3(-1f, -4f, 3f);
        [Tooltip("Optional persistent renderer override. Negative keeps the material's authored opacity.")]
        public float plasmaOpacityOverride = -1f;

        void OnEnable()
        {
            ApplyMaterialOverrides();
        }

        /// <summary>Applies serialized per-instance material values without instancing the shared asset.</summary>
        public void ApplyMaterialOverrides()
        {
            if (plasma == null || plasmaOpacityOverride < 0f) return;
            var renderer = plasma.GetComponent<Renderer>();
            if (renderer == null) return;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetFloat("_Alpha", Mathf.Clamp01(plasmaOpacityOverride));
            renderer.SetPropertyBlock(properties);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (plasma != null) plasma.Rotate(plasmaDegreesPerSecond * dt, Space.Self);
            if (corona != null) corona.Rotate(coronaDegreesPerSecond * dt, Space.Self);
        }
    }
}
