using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1
{
    /// <summary>
    /// The visible, drifting half of the world's fog. Unity's linear scene fog supplies distant aerial
    /// perspective; this one small world-space particle field gives the route a moving layer the player
    /// can actually see. It follows the player root only as an emitter: particles stay in world space,
    /// so turning the camera never drags a sheet of mist across the frame.
    ///
    /// <para>The soft procedural texture is shared with <see cref="DeathMist"/>. Ambient mist is
    /// deliberately additive and cold. It cannot darken a landing, while camera fading, count, alpha
    /// and maximum screen size bound how much overlapping sheets can wash out contrast.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class AmbientMist : MonoBehaviour
    {
        [Header("Population")]
        [Min(1)] public int maxParticles = 48;
        [Min(0f)] public float emissionRate = 4f;
        [Min(0.1f)] public float lifetimeMin = 8f;
        [Min(0.1f)] public float lifetimeMax = 12f;

        [Header("Route volume (player-root local space)")]
        public Vector3 volumeCenter = new Vector3(0f, -1f, 28f);
        public Vector3 volumeSize = new Vector3(26f, 4f, 28f);
        [Min(0.1f)] public float sizeMin = 8f;
        [Min(0.1f)] public float sizeMax = 14f;

        [Header("Ground alignment")]
        [Min(0f)] public float groundProbeForward = 28f;
        [Min(0.01f)] public float groundProbeInterval = 0.25f;
        [Min(0f)] public float groundProbeStartHeight = 12f;
        [Min(0.1f)] public float groundProbeDistance = 60f;
        [Min(0f)] public float groundClearance = 0.8f;

        [Header("Look")]
        public Color tint = new Color(0.28f, 0.40f, 0.58f, 1f);
        [Range(0f, 1f)] public float alphaMin = 0.18f;
        [Range(0f, 1f)] public float alphaMax = 0.26f;
        [Range(0.01f, 0.30f)] public float maxScreenSize = 0.22f;
        [Min(0f)] public float cameraFadeNear = 3f;
        [Min(0.01f)] public float cameraFadeFar = 9f;
        [Min(0f)] public float noiseStrength = 0.35f;
        [Min(0.001f)] public float noiseFrequency = 0.08f;
        [Min(0f)] public float noiseScrollSpeed = 0.04f;
        public uint randomSeed = 0xA11CEu;

        ParticleSystem mist;
        Material ambientMaterial;
        bool holdsSharedMaterial;
        float groundProbeClock;

        const int DefaultLayerMask = 1 << 0;

        /// <summary>Configured system, exposed so EditMode tests can verify the renderer and simulation.</summary>
        public ParticleSystem System { get { return mist; } }

        void Awake()
        {
            EnsureConfigured();
        }

        void OnEnable()
        {
            if (mist != null && !mist.isPlaying) mist.Play(true);
        }

        void Update()
        {
            groundProbeClock += Time.deltaTime;
            if (groundProbeClock < Mathf.Max(0.01f, groundProbeInterval)) return;
            groundProbeClock = 0f;
            AlignEmitterToGround();
        }

        /// <summary>
        /// Builds the one runtime particle system. Idempotent so tests can configure a component created
        /// outside play mode and so a disabled/re-enabled player never acquires a second renderer.
        /// </summary>
        public void EnsureConfigured()
        {
            if (mist != null) return;

            DeathMist.RetainShared();
            holdsSharedMaterial = true;

            mist = gameObject.AddComponent<ParticleSystem>();
            // A newly-added ParticleSystem starts immediately with template defaults. Stop it before
            // writing duration/prewarm; Unity otherwise warns and silently keeps the old duration.
            mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            mist.useAutoRandomSeed = false;
            mist.randomSeed = randomSeed == 0u ? 1u : randomSeed;

            float lifeLo = Mathf.Max(0.1f, Mathf.Min(lifetimeMin, lifetimeMax));
            float lifeHi = Mathf.Max(lifeLo, Mathf.Max(lifetimeMin, lifetimeMax));
            float sizeLo = Mathf.Max(0.1f, Mathf.Min(sizeMin, sizeMax));
            float sizeHi = Mathf.Max(sizeLo, Mathf.Max(sizeMin, sizeMax));

            var main = mist.main;
            main.loop = true;
            main.prewarm = true;                    // a fresh load begins with atmosphere, not an empty field
            main.playOnAwake = false;
            main.duration = lifeHi;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = false;           // atmosphere freezes with the world and with pause
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = Mathf.Max(1, maxParticles);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeLo, lifeHi);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.18f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeLo, sizeHi);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(tint.r, tint.g, tint.b, Mathf.Min(alphaMin, alphaMax)),
                new Color(tint.r, tint.g, tint.b, Mathf.Max(alphaMin, alphaMax)));
            main.gravityModifier = 0f;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = mist.emission;
            emission.enabled = true;
            emission.rateOverTime = Mathf.Max(0f, emissionRate);

            // 14-42 m forward with shipped values. AlignEmitterToGround moves this box onto the actual
            // route slope; the authored centre remains the safe fallback when there is no ground ahead.
            var shape = mist.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = volumeCenter;
            shape.scale = new Vector3(
                Mathf.Max(0.1f, volumeSize.x),
                Mathf.Max(0.1f, volumeSize.y),
                Mathf.Max(0.1f, volumeSize.z));
            AlignEmitterToGround();                 // place the prewarmed population on the route immediately

            var noise = mist.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = Mathf.Max(0f, noiseStrength);
            noise.frequency = Mathf.Max(0.001f, noiseFrequency);
            noise.scrollSpeed = Mathf.Max(0f, noiseScrollSpeed);
            noise.damping = true;

            var color = mist.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(1f, 0.72f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var size = mist.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.72f),
                new Keyframe(0.18f, 1f),
                new Keyframe(0.72f, 1.08f),
                new Keyframe(1f, 0.82f)));

            var rotation = mist.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.10f, 0.10f);

            // Keep every expensive/interactive particle module off. This is one bounded visual layer.
            var collision = mist.collision;
            collision.enabled = false;
            var lights = mist.lights;
            lights.enabled = false;
            var trails = mist.trails;
            trails.enabled = false;
            var trigger = mist.trigger;
            trigger.enabled = false;

            var renderer = mist.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // Clone the shared mist material so camera fading belongs to ambient haze only. Death and
            // Pyre mist are short authored payoffs and must remain fully visible beside the camera.
            ambientMaterial = new Material(DeathMist.MistMaterial())
            {
                name = "AmbientMistRuntime",
                hideFlags = HideFlags.HideAndDontSave,
            };
            float fadeNear = Mathf.Max(0f, cameraFadeNear);
            float fadeFar = Mathf.Max(fadeNear + 0.01f, cameraFadeFar);
            if (ambientMaterial.HasProperty("_CameraFadingEnabled"))
                ambientMaterial.SetFloat("_CameraFadingEnabled", 1f);
            if (ambientMaterial.HasProperty("_CameraNearFadeDistance"))
                ambientMaterial.SetFloat("_CameraNearFadeDistance", fadeNear);
            if (ambientMaterial.HasProperty("_CameraFarFadeDistance"))
                ambientMaterial.SetFloat("_CameraFarFadeDistance", fadeFar);
            if (ambientMaterial.HasProperty("_CameraFadeParams"))
                ambientMaterial.SetVector("_CameraFadeParams", new Vector4(fadeNear, 1f / (fadeFar - fadeNear), 0f, 0f));
            ambientMaterial.EnableKeyword("_FADING_ON");
            renderer.sharedMaterial = ambientMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = Mathf.Clamp(maxScreenSize, 0.01f, 0.25f);
            renderer.sortingFudge = -4f;

            mist.Play(true);
        }

        /// <summary>
        /// One bounded ground query at the route-preview anchor. Default-layer geometry is the only
        /// valid surface; enemies, triggers and interactables cannot pull the atmosphere around.
        /// </summary>
        public bool AlignEmitterToGround()
        {
            if (mist == null) return false;

            Vector3 origin = transform.TransformPoint(new Vector3(0f, groundProbeStartHeight, groundProbeForward));
            RaycastHit hit;
            bool found = Physics.Raycast(origin, Vector3.down, out hit, Mathf.Max(0.1f, groundProbeDistance),
                DefaultLayerMask, QueryTriggerInteraction.Ignore);

            var shape = mist.shape;
            if (!found)
            {
                shape.position = volumeCenter;
                shape.rotation = Vector3.zero;
                return false;
            }

            Vector3 localCenter;
            Quaternion localRotation;
            ResolveGroundPose(transform.position, transform.rotation, hit.point, hit.normal,
                groundClearance, out localCenter, out localRotation);
            shape.position = localCenter;
            shape.rotation = localRotation.eulerAngles;
            return true;
        }

        /// <summary>Pure pose conversion used by the ground probe and EditMode tests.</summary>
        public static void ResolveGroundPose(Vector3 rootPosition, Quaternion rootRotation,
            Vector3 groundPoint, Vector3 groundNormal, float clearance,
            out Vector3 localCenter, out Quaternion localRotation)
        {
            Vector3 normal = groundNormal.sqrMagnitude > 1e-6f ? groundNormal.normalized : Vector3.up;
            Quaternion inverseRoot = Quaternion.Inverse(rootRotation);
            localCenter = inverseRoot * (groundPoint + normal * Mathf.Max(0f, clearance) - rootPosition);
            localRotation = Quaternion.FromToRotation(Vector3.up, inverseRoot * normal);
        }

        void OnDisable()
        {
            if (mist != null)
                mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnDestroy()
        {
            ReleaseResources();
        }

        /// <summary>Idempotent teardown. Runtime calls this from OnDestroy; EditMode tests that invoke
        /// <see cref="EnsureConfigured"/> manually must call it because Unity does not send OnDestroy to
        /// a non-ExecuteAlways component outside play mode.</summary>
        public void ReleaseResources()
        {
            if (ambientMaterial != null)
            {
                if (Application.isPlaying) Destroy(ambientMaterial);
                else DestroyImmediate(ambientMaterial);
                ambientMaterial = null;
            }
            if (!holdsSharedMaterial) return;
            holdsSharedMaterial = false;
            DeathMist.ReleaseShared();
        }
    }
}
