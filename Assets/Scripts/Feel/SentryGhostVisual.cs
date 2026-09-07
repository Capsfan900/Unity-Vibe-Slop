using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The Sentry's body language: a floating cartoon ghost (2026-09-06, from the user — "make it like a
    /// blush ghost that looks like a cartoon ghost and is floating and glowing with wispy mist around it").
    ///
    /// <para><b>It owns three things and nothing else: the FLOAT, the HEM and the MIST.</b> The shell, the
    /// face and the hem geometry are built by <c>PrefabFactory.BuildEnemy</c>; the arm rig, the telegraph,
    /// the cue and every timing still belong to <see cref="EnemyVisuals"/>. Nothing here touches the root,
    /// the collider, the <c>NavMeshAgent</c>, the deathblow height or the posture bar — the float happens
    /// entirely inside a child of <c>LungeRoot</c>, so the capsule a bolt-parry and a deathblow key off is
    /// exactly where it always was.</para>
    ///
    /// <para><b>Luminous, never blooming.</b> "An enemy body never glows until it is deflected" existed so
    /// that light on a body means "you deflected". A ghost that is not lit is not a ghost, so the rule is
    /// now stated as a BUDGET rather than a ban: the shell's constant emission floor plus every mist wisp
    /// that could possibly overlap it sums to <see cref="MaxStackedPeak"/> = 1.02, under the 1.05 bloom
    /// threshold. The ghost reads as softly lit in a dark world; it physically cannot bloom, and the
    /// deflect spike (<c>EnemyVisuals.ParryGlow</c>, warm, 3.2) is still 7.6x brighter and the opposite
    /// temperature. The floor goes through <see cref="EnemyVisuals.SetAura"/> — the one sanctioned door
    /// into that channel — so it is modulated by <c>chargeDark</c> like an ember aura and the ghost visibly
    /// INHALES its own light on a wind-up. That is a free, truthful extra tell.</para>
    ///
    /// <para><b>Additive mist cannot hide the bolt.</b> The wisps are additive, so they can only ever ADD
    /// light: an amber bolt at 1.6 seen through a 0.15 cold haze is still an amber bolt. An alpha-blended
    /// mist could have dimmed the one thing the player must parry; this one cannot.</para>
    ///
    /// <para><b>Cost, exactly.</b> No particle system, no <c>TrailRenderer</c>, no per-frame allocation and
    /// no <c>MaterialPropertyBlock</c> for the mist at all (it breathes in SIZE, not in brightness — on a
    /// dim additive blob a size change is visible and a brightness change is not). Per ghost per frame:
    /// one float write, <c>hem.Length</c> rotations, <see cref="mistCount"/> transform writes, one property
    /// block copy for the second eye and one <c>SetAura</c>. Every wisp shares ONE static additive
    /// material made by <see cref="SlashFx.CreateAdditiveMaterial"/>, so the SRP batcher folds them.</para>
    ///
    /// <para><b>Scaled vs unscaled is a decision here, not a default.</b> The float and the hem are BODY
    /// motion and run on scaled time, so hitstop freezes the ghost with the world exactly like its arm.
    /// The mist is world FX and runs unscaled, like everything in <see cref="SlashFx"/>, so a hit-pause
    /// reads as impact rather than as a stall.</para>
    /// </summary>
    public class SentryGhostVisual : MonoBehaviour
    {
        // ---------------------------------------------------------------- the budget
        /// <summary>The shipped bloom threshold. Nothing on this body may reach it.</summary>
        public const float BloomThreshold = 1.05f;
        /// <summary>What the LIT term can contribute on the ghost's brightest channel, and the reason the
        /// other two numbers are as low as they are. The shell's albedo peaks at 0.855 (M_SentryGhost) and the
        /// most incident light anywhere in this level is the Trilight equator (Rec.709 luminance 0.2045)
        /// plus the key (0.1896) — call it 0.39 — so a fully lit facet renders about 0.33. Rounded up.
        /// A budget that ignores the albedo term is not a budget; the first draft of this pass did, and
        /// it would have shipped a ghost at 1.37.</summary>
        public const float LitShellCeiling = 0.35f;
        /// <summary>Peak channel of the shell's constant emission floor.</summary>
        public const float ShellEmissionPeak = 0.28f;
        /// <summary>Peak channel of ONE mist wisp. Deliberately quiet: it is the LAST term in the budget,
        /// so the only way to make the mist louder is to make the body dimmer.</summary>
        public const float MistPeak = 0.10f;
        /// <summary>The most wisps this thing is ever allowed to build. The budget below is computed
        /// against this number, not against whatever a prefab happens to ask for.</summary>
        public const int MistMaxCount = 4;
        /// <summary>Absolute worst case: a fully lit facet of the shell, its emission floor, and ALL FOUR
        /// wisps stacked on that same pixel. 0.35 + 0.28 + 4 x 0.10 = 1.03, under the 1.05 threshold.
        /// The ghost cannot bloom by construction, not by tuning.</summary>
        public static float MaxStackedPeak { get { return LitShellCeiling + ShellEmissionPeak + MistPeak * MistMaxCount; } }

        /// <summary>The ghost's light: pale COLD blue-white. Deliberately not warm — warm in this game means
        /// a combat tell or fire (ANIMATION-VFX section 4 rule 10) and a warm body would put an enemy in the
        /// tells' colour family. Deliberately not violet either: violet is the flare, i.e. "use this", and
        /// the Sentry used to squat on that hue. Cold says nothing, which is what a body should say; the
        /// ghost separates from the near-black world by VALUE (its albedo is ~30x a wall's), not by hue.</summary>
        public static readonly Color AuraTint = new Color(0.62f, 0.80f, 1.00f, 1f);
        /// <summary>The mist's tint, already scaled to <see cref="MistPeak"/>. The same cold family as the
        /// shell, a shade bluer so the haze sits behind the body's value instead of flattening into it.</summary>
        public static readonly Color MistTint = new Color(0.42f, 0.64f, 1.00f, 1f) * MistPeak;

        // ---------------------------------------------------------------- wiring (PrefabFactory writes all of it)
        [Header("Bound by PrefabFactory.BuildEnemy — rule 9: every number is written there")]
        public Transform floatRoot;
        public Transform[] hem;
        /// <summary>The second eye. <see cref="EnemyVisuals"/> holds ONE eye renderer and drives the posture
        /// tell into it; this mirrors that property block so the ghost never blinks out of one eye.</summary>
        public Renderer mirrorEye;

        [Header("Float")]
        public float bobAmplitude = 0.07f;
        public float bobHz = 0.42f;
        public float secondaryAmplitude = 0.018f;
        public float secondaryHz = 0.93f;
        public float swayAmplitude = 0.03f;
        public float rollDegrees = 2.5f;

        [Header("Hem")]
        public float hemWaveDegrees = 9f;
        public float hemWaveHz = 0.62f;

        [Header("Mist")]
        public int mistCount = 4;
        public float mistRadius = 0.62f;
        public float mistRadiusSpread = 0.22f;
        public float mistLowHeight = 0.55f;
        public float mistHighHeight = 1.55f;
        public float mistSize = 0.34f;
        public float mistFlatten = 0.62f;
        public float mistOrbitSeconds = 7.5f;
        public float mistBobAmplitude = 0.14f;
        public float mistBreathHz = 0.31f;

        static Material mistMat;

        EnemyVisuals visuals;
        Transform mistRoot;
        Transform[] wisps;
        float[] wispPhase, wispRadius, wispHeight, wispSize, wispDir;
        Vector3 floatBase;
        Quaternion[] hemBase;
        float bodyTime;
        MaterialPropertyBlock eyeMpb;

        void Awake()
        {
            visuals = GetComponent<EnemyVisuals>();
            if (floatRoot != null) floatBase = floatRoot.localPosition;
            if (hem != null)
            {
                hemBase = new Quaternion[hem.Length];
                for (int i = 0; i < hem.Length; i++)
                    hemBase[i] = hem[i] != null ? hem[i].localRotation : Quaternion.identity;
            }
            eyeMpb = new MaterialPropertyBlock();
            BuildMist();
        }

        /// <summary>
        /// Four soft additive blobs on lazy counter-rotating orbits. Built here rather than in the prefab
        /// for the same reason <see cref="SentryFlare"/> builds its halo in code: the material has to come
        /// from <see cref="SlashFx.CreateAdditiveMaterial"/>, which normalises to 1.0, so the mist can never
        /// be quietly re-tinted brighter by an Inspector edit on a prefab that already exists.
        /// </summary>
        void BuildMist()
        {
            int n = Mathf.Clamp(mistCount, 0, MistMaxCount);
            if (n == 0 || floatRoot == null) return;

            if (mistMat == null)
            {
                mistMat = SlashFx.CreateAdditiveMaterial(MistTint);
                if (mistMat.HasProperty("_BaseColor")) mistMat.SetColor("_BaseColor", MistTint);
                if (mistMat.HasProperty("_Color")) mistMat.SetColor("_Color", MistTint);
            }

            var rootGo = new GameObject("Mist");
            rootGo.layer = gameObject.layer;
            mistRoot = rootGo.transform;
            mistRoot.SetParent(floatRoot, false);

            wisps = new Transform[n];
            wispPhase = new float[n]; wispRadius = new float[n];
            wispHeight = new float[n]; wispSize = new float[n]; wispDir = new float[n];

            for (int i = 0; i < n; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Wisp" + i;
                go.layer = gameObject.layer;
                // A stray collider on the Enemy layer would be a phantom hitbox floating a metre off the
                // body. It goes before anything else touches this object.
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.transform.SetParent(mistRoot, false);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = mistMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                wisps[i] = go.transform;
                // Irrational-ish spacing so four wisps never line up into a rigid carousel.
                wispPhase[i] = i * 1.87f;
                wispRadius[i] = mistRadius + mistRadiusSpread * ((i % 2 == 0) ? 0.5f : -0.5f);
                wispHeight[i] = Mathf.Lerp(mistLowHeight, mistHighHeight, n > 1 ? (i / (float)(n - 1)) : 0.5f);
                wispSize[i] = mistSize * (0.78f + 0.16f * (i % 3));
                wispDir[i] = (i % 2 == 0) ? 1f : -0.72f;   // counter-rotating, and never the same speed
            }
        }

        void Update()
        {
            // BODY: scaled. Hitstop freezes the ghost with the world, exactly like its arm.
            bodyTime += Time.deltaTime;
            float w = bodyTime * Mathf.PI * 2f;
            if (floatRoot != null)
            {
                float y = bobAmplitude * Mathf.Sin(w * bobHz) + secondaryAmplitude * Mathf.Sin(w * secondaryHz);
                float x = swayAmplitude * Mathf.Sin(w * bobHz * 0.5f + 1.1f);
                floatRoot.localPosition = floatBase + new Vector3(x, y, 0f);
                floatRoot.localRotation = Quaternion.Euler(0f, 0f, rollDegrees * Mathf.Sin(w * bobHz * 0.73f));
            }

            if (hem != null && hemBase != null)
            {
                for (int i = 0; i < hem.Length && i < hemBase.Length; i++)
                {
                    if (hem[i] == null) continue;
                    float p = i * 1.24f;
                    float s = Mathf.Sin(w * hemWaveHz + p);
                    float c = Mathf.Cos(w * hemWaveHz * 0.81f + p);
                    hem[i].localRotation = hemBase[i] * Quaternion.Euler(hemWaveDegrees * s, 0f, hemWaveDegrees * 0.6f * c);
                }
            }

            // MIST: unscaled, like every other effect in the game, so a hit-pause reads as impact.
            if (wisps != null)
            {
                float mt = Time.unscaledTime;
                float orbit = Mathf.Max(0.5f, mistOrbitSeconds);
                for (int i = 0; i < wisps.Length; i++)
                {
                    if (wisps[i] == null) continue;
                    float a = mt * (Mathf.PI * 2f / orbit) * wispDir[i] + wispPhase[i];
                    float h = wispHeight[i] + mistBobAmplitude * Mathf.Sin(mt * 0.83f + wispPhase[i]);
                    wisps[i].localPosition = new Vector3(Mathf.Cos(a) * wispRadius[i], h, Mathf.Sin(a) * wispRadius[i]);
                    float breath = 0.78f + 0.22f * Mathf.Sin(mt * Mathf.PI * 2f * mistBreathHz + wispPhase[i]);
                    float s = wispSize[i] * breath;
                    wisps[i].localScale = new Vector3(s, s * mistFlatten, s);
                }
            }

            if (visuals != null)
            {
                // The one sanctioned door into enemy emission. Breathes 0.88..1.00 of the floor, so the peak
                // is ShellEmissionPeak and never a photon more.
                float breath = 0.94f + 0.06f * Mathf.Sin(Time.unscaledTime * 1.7f);
                visuals.SetAura(AuraTint, ShellEmissionPeak * breath);

                if (mirrorEye != null && visuals.eye != null)
                {
                    visuals.eye.GetPropertyBlock(eyeMpb);
                    mirrorEye.SetPropertyBlock(eyeMpb);
                }
            }
        }
    }
}
