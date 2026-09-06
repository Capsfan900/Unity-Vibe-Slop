using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1
{
    /// <summary>
    /// The death dissolve. Every enemy comes apart into rising mist when it dies — a Dark Souls homage:
    /// bodies do not ragdoll or lie around cluttering the arena, they lose their shape and drift away.
    /// A deathblow gets the same effect, bigger.
    ///
    /// <para><b>Why a ParticleSystem.</b> The rest of this project's FX are LineRenderers because they
    /// draw thin travelling shapes. Mist is the opposite — many soft overlapping billboards — and that is
    /// exactly what a ParticleSystem is for: the whole burst is ONE draw call and the simulation runs on
    /// a worker thread. Doing this with N transforms would cost N transform writes per frame on the main
    /// thread for no visual gain.</para>
    ///
    /// <para><b>Performance.</b> A fixed pool of systems, built once in code, sharing ONE material and ONE
    /// generated texture. Nothing is Instantiate'd or Destroy'ed during combat, so a fight full of deaths
    /// allocates nothing — the same discipline <see cref="SlashFx"/> already established after pooling
    /// took this project from ~25 gen0 collections/sec.</para>
    ///
    /// <para><b>Unscaled time.</b> A deathblow fires hitstop on the same frame the enemy dies. The mist is
    /// the feedback that explains the kill, so it must not freeze along with the world.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class DeathMist : MonoBehaviour
    {
        /// <summary>
        /// Deaths are discrete events, never per-frame, so a handful of systems is plenty. When every
        /// system is busy the least-loaded one absorbs the extra burst rather than a new one being
        /// created — <see cref="MaxParticles"/> then caps the total on screen.
        /// </summary>
        const int PoolSize = 5;
        const int MaxParticles = 96;
        const int TextureSize = 32;

        /// <summary>
        /// 2026-09-06 VFX pass ("physics simulations and effects"): the mist previously rose as a rigid
        /// column, decelerated only by the velocity clamp below. Real smoke buoyancy is turbulent, not
        /// laminar. A small, damped noise field breaks that up without changing how fast or how far the
        /// burst reads -- it is the same silhouette, just alive rather than solid.
        /// </summary>
        public const float MistNoiseStrength = 0.28f;
        public const float MistNoiseFrequency = 0.35f;
        public const float MistNoiseScrollSpeed = 0.15f;

        static readonly List<DeathMist> pool = new List<DeathMist>(PoolSize);
        static int roundRobin;

        // Shared by every system in the pool, so all five can batch together.
        static Material sharedMat;
        static Texture2D sharedTex;
        static int liveInstances;

        ParticleSystem ps;

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Burst mist at <paramref name="position"/>. <paramref name="grand"/> is the deathblow version:
        /// more particles, larger, thrown harder. Safe to call with no pool built yet.
        /// </summary>
        public static void Burst(Vector3 position, float scale, Color tint, bool grand)
        {
            var fx = Rent();
            if (fx == null || fx.ps == null) return;

            float s = Mathf.Clamp(scale, 0.25f, 4f);
            fx.transform.position = position;

            // Written straight into the main module (Unity's module structs write through to the system),
            // so one pooled system serves a Grunt and the Boss without any per-death allocation.
            var main = fx.ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(Tint(tint));
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f * s, (grand ? 0.34f : 0.24f) * s);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f * s, (grand ? 3.4f : 2.3f) * s);

            var shape = fx.ps.shape;
            shape.radius = 0.26f * s;

            // Emit() needs the system to be simulating; it stops itself once its duration elapses.
            if (!fx.ps.isPlaying) fx.ps.Play();

            int count = Mathf.RoundToInt((grand ? 38f : 24f) * Mathf.Clamp(s, 0.6f, 2f));
            fx.ps.Emit(count);
        }

        // ------------------------------------------------------------------ pooling

        static DeathMist Rent()
        {
            EnsurePool();

            // Prefer a system with nothing alive so two deaths never visibly share one emitter. Falling
            // back to the least-loaded (then round-robin) means a burst is always served, never dropped.
            DeathMist fallback = null;
            int fewest = int.MaxValue;
            for (int i = 0; i < pool.Count; i++)
            {
                var m = pool[i];
                if (m == null || m.ps == null) continue;
                int c = m.ps.particleCount;
                if (c == 0) return m;
                if (c < fewest) { fewest = c; fallback = m; }
            }
            if (fallback != null) return fallback;

            if (pool.Count == 0) return null;
            roundRobin = (roundRobin + 1) % pool.Count;
            return pool[roundRobin];
        }

        /// <summary>
        /// Builds the pool on first use and repairs it afterwards. Entries are null-checked because a
        /// scene load destroys the GameObjects while leaving these static references behind — the same
        /// trap SlashFx guards against on rent.
        /// </summary>
        static void EnsurePool()
        {
            for (int i = pool.Count - 1; i >= 0; i--)
                if (pool[i] == null) pool.RemoveAt(i);

            while (pool.Count < PoolSize)
            {
                var go = new GameObject("~DeathMist");
                var fx = go.AddComponent<DeathMist>();
                fx.Configure();
                pool.Add(fx);
            }
        }

        // ------------------------------------------------------------------ construction

        void Configure()
        {
            liveInstances++;

            ps = gameObject.AddComponent<ParticleSystem>();   // also brings in the renderer

            // AddComponent on a live GameObject runs Awake immediately, and a fresh ParticleSystem has
            // playOnAwake=true — so it is already simulating before we configure it. Unity refuses to
            // accept main.duration on a playing system (warning, value silently dropped). Stop it first.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // mist stays put, not glued to the emitter
            main.useUnscaledTime = true;                                  // hitstop must not freeze the kill feedback
            main.maxParticles = MaxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.22f;                                // NEGATIVE: the mist rises
            main.stopAction = ParticleSystemStopAction.None;

            // Burst-only: every particle comes from an explicit Emit() call, never a rate.
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            shape.radiusThickness = 1f;

            // Punches outward then settles into a drift, so the burst reads as the body coming apart
            // rather than as a uniform puff.
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.14f;
            limit.limit = new ParticleSystem.MinMaxCurve(0.7f);

            // Damped turbulence so the rise reads as smoke curling, not a solid column being pushed up at
            // a fixed rate. Low quality (1D) is plenty at this particle count and this size on screen, and
            // "damping" on means the field itself settles rather than churning forever.
            var noise = ps.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = new ParticleSystem.MinMaxCurve(MistNoiseStrength);
            noise.frequency = MistNoiseFrequency;
            noise.scrollSpeed = MistNoiseScrollSpeed;
            noise.damping = true;

            // Fade in fast, out slow. Allocating a Gradient/AnimationCurve here is fine — this runs once
            // per pooled system for the life of the process, never per burst.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

            var psr = GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.sharedMaterial = SharedMaterial();
            psr.shadowCastingMode = ShadowCastingMode.Off;
            psr.receiveShadows = false;
            psr.lightProbeUsage = LightProbeUsage.Off;
            psr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            psr.sortingFudge = -2f;
        }

        /// <summary>
        /// One additive material for the whole pool, so all five systems batch into a single draw.
        ///
        /// <para><b>This deliberately does not use <see cref="SlashFx.CreateAdditiveMaterial"/>.</b> That
        /// builds on <c>Universal Render Pipeline/Unlit</c>, which does not read vertex colour — and
        /// per-particle colour is exactly how a ParticleSystem delivers <c>startColor</c>. Every burst
        /// would have rendered white regardless of the enemy's tint. The PARTICLES variant of the URP
        /// unlit shader is the one that multiplies vertex colour through, so the tint survives. The blend
        /// setup below is otherwise the same additive recipe SlashFx uses.</para>
        /// </summary>
        /// <summary>
        /// The soft additive particle material, shared with <see cref="PyreMist"/> so the two mist
        /// systems batch together and only one 32x32 falloff texture exists in the process.
        /// Callers MUST bracket their use with <see cref="RetainShared"/> / <see cref="ReleaseShared"/>
        /// — the material is destroyed with the last holder, and a renderer left pointing at a
        /// destroyed material draws nothing at all (not magenta, which is why it is easy to miss).
        /// </summary>
        internal static Material MistMaterial() { return SharedMaterial(); }

        /// <summary>Claim a share of the mist material/texture. Balanced by <see cref="ReleaseShared"/>.</summary>
        internal static void RetainShared() { liveInstances++; }

        /// <summary>Test-only: first pooled system's noise module, so the buoyancy wiring can be pinned
        /// without touching play mode. Builds the pool if it does not exist yet.</summary>
        public static ParticleSystem.NoiseModule PeekNoiseModuleForTests()
        {
            EnsurePool();
            return pool[0].ps.noise;
        }

        /// <summary>Give it back. The last release frees the material and the generated texture.</summary>
        internal static void ReleaseShared()
        {
            liveInstances = Mathf.Max(0, liveInstances - 1);
            if (liveInstances > 0) return;
            if (sharedMat != null) { Destroy(sharedMat); sharedMat = null; }
            if (sharedTex != null) { Destroy(sharedTex); sharedTex = null; }
        }

        static Material SharedMaterial()
        {
            if (sharedMat != null) return sharedMat;

            // Explicit checks rather than ??: Unity's overloaded equality makes null-coalescing on
            // UnityEngine.Object unreliable, and a null shader here would render the mist bright magenta.
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");

            sharedMat = new Material(sh)
            {
                name = "DeathMistRuntime",
                hideFlags = HideFlags.HideAndDontSave,
            };

            if (sharedMat.HasProperty("_BaseColor")) sharedMat.SetColor("_BaseColor", Color.white);
            if (sharedMat.HasProperty("_Color")) sharedMat.SetColor("_Color", Color.white);
            if (sharedMat.HasProperty("_ColorMode")) sharedMat.SetFloat("_ColorMode", 0f);   // Multiply by vertex colour
            if (sharedMat.HasProperty("_Surface")) sharedMat.SetFloat("_Surface", 1f);       // Transparent
            if (sharedMat.HasProperty("_Blend")) sharedMat.SetFloat("_Blend", 2f);           // Additive
            if (sharedMat.HasProperty("_SrcBlend")) sharedMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (sharedMat.HasProperty("_DstBlend")) sharedMat.SetFloat("_DstBlend", (float)BlendMode.One);
            if (sharedMat.HasProperty("_ZWrite")) sharedMat.SetFloat("_ZWrite", 0f);
            if (sharedMat.HasProperty("_Cull")) sharedMat.SetFloat("_Cull", (float)CullMode.Off);
            sharedMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            sharedMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            sharedMat.renderQueue = (int)RenderQueue.Transparent;

            var tex = MistTexture();
            if (sharedMat.HasProperty("_BaseMap")) sharedMat.SetTexture("_BaseMap", tex);
            if (sharedMat.HasProperty("_MainTex")) sharedMat.SetTexture("_MainTex", tex);
            return sharedMat;
        }

        /// <summary>
        /// A 32x32 radial falloff, generated in code — 4 KB, no texture asset. Untextured additive quads
        /// render as hard squares, which reads as debris rather than mist; the falloff is what makes the
        /// particles soft-edged.
        /// </summary>
        static Texture2D MistTexture()
        {
            if (sharedTex != null) return sharedTex;

            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = "DeathMistTex",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var px = new Color32[TextureSize * TextureSize];
            float c = (TextureSize - 1) * 0.5f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x - c) / c;
                    float dy = (y - c) / c;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a *= a;   // squared: tight core, wispy edge
                    px[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);   // no mips; drop the CPU copy

            sharedTex = tex;
            return tex;
        }

        /// <summary>
        /// Enemy emission colours are HDR and can sit well above 1. Flattened and dimmed slightly so the
        /// mist reads as smoke lit from within rather than as a flare — the project is deliberately dark.
        /// </summary>
        static Color Tint(Color c)
        {
            Color n = SlashFx.NormaliseColor(c);
            return new Color(n.r * 0.85f, n.g * 0.85f, n.b * 0.85f, 1f);
        }

        void OnDestroy()
        {
            // Only a real teardown reaches here — pooled systems are never destroyed during play. The
            // shared material and texture outlive individual systems, so they go with the last one —
            // and "the last one" now counts PyreMist's pool too, via the same counter.
            ReleaseShared();
        }
    }
}
