using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The weapon catches fire as the Pyre meter fills. This is the player's PRIMARY read on their own
    /// charge — you should be able to play with the HUD off and still know how close the super is — so
    /// it escalates continuously from a first ember at a sliver of charge to a blaze at full.
    ///
    /// <para><b>Three layers, all driven from one 0..1 value:</b></para>
    /// <list type="bullet">
    /// <item><b>Heat.</b> The blade's own <see cref="EnergyGlow"/> is pushed toward ember orange and its
    /// charge is raised, which brightens the breath, speeds the flow band and quickens the motes.
    /// The glow is asked rather than written directly: it owns <c>_EmissionColor</c> on those renderers
    /// and would overwrite a property block set here on the very next frame. ONE WRITER PER CHANNEL.</item>
    /// <item><b>Embers.</b> A small fixed pool of tiny quads shed from along the blade and drifting up
    /// in viewmodel space. Count is fixed; the SPAWN RATE and lifetime scale with charge, so low charge
    /// is a lone flickering mote and full charge is a continuous stream.</item>
    /// <item><b>Light.</b> One short-range point light at the blade. Deliberately weak and short: this
    /// project has already shipped a riposte that rendered as a black screen and a 0.55-alpha
    /// ScreenFlash that shredded the frame, and the scene runs heavy bloom. The fire must be legible
    /// WITHOUT washing out the thing it is burning on. See docs/ENGINEERING-LOG.md.</item>
    /// </list>
    ///
    /// <para>Attaches itself at runtime from <see cref="PlayerCombat"/> so no prefab rebuild is needed;
    /// <c>DisallowMultipleComponent</c> makes that idempotent. Unscaled time throughout, so hitstop
    /// never freezes the fire in the player's hands.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponEmber : MonoBehaviour
    {
        [Header("Heat")]
        [Tooltip("Ember hue the blade is driven toward at full charge. Blended FROM the weapon's own " +
                 "neon so each weapon still reads as itself while it heats.")]
        [ColorUsage(true, true)] public Color emberHot = new Color(1f, 0.42f, 0.10f, 1f);
        [Tooltip("How far toward emberHot the blade tint travels at full charge. Never 1: a weapon " +
                 "that loses its own colour entirely stops being identifiable.")]
        [Range(0f, 1f)] public float tintBlendAtFull = 0.8f;
        [Tooltip("EnergyGlow.SetCharge at full Pyre. The glow's own charge ramp adds ~0.85 emission and " +
                 "a 1.2 tip boost at 1.0, which blows out under bloom — this caps it well below that.")]
        [Range(0f, 1f)] public float glowChargeAtFull = 0.55f;

        [Header("Embers")]
        [Range(0, 24)] public int emberCount = 14;
        public float emberSize = 0.012f;
        [Tooltip("Embers per second at full charge. Scales with charge squared, so low charge is a " +
                 "flicker rather than a thin constant drizzle.")]
        public float emberRateAtFull = 26f;
        public float emberLife = 0.55f;
        public float emberRise = 0.55f;
        public float emberDrift = 0.16f;

        [Header("Light")]
        public bool lightEnabled = true;
        [Tooltip("Intensity at full charge. Kept low on purpose — the blade's own emission does most " +
                 "of the work and a bright point light at arm's length white-outs the frame.")]
        public float lightIntensityAtFull = 1.5f;
        public float lightRangeAtFull = 2.4f;

        [Header("Response")]
        [Tooltip("How fast the fire chases the meter. Slow enough that a parry reads as the fire " +
                 "CATCHING rather than as the bar snapping.")]
        public float chargeLerpSpeed = 3.5f;
        [Tooltip("Charge below which nothing burns at all. An unlit weapon must read as unlit.")]
        [Range(0f, 0.2f)] public float deadzone = 0.02f;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        PlayerResources resources;
        WeaponViewmodel viewmodel;
        WeaponData weapon;

        EnergyGlow glow;          // on the equipped weapon model, under viewmodel.grip
        Transform bladeRoot;      // the model instance the embers are shed from
        float bladeLength = 0.4f;

        Transform emberRoot;      // viewmodel-space parent, so embers trail the hand rather than the world
        Transform[] embers;
        Vector3[] emberVel;
        float[] emberAge;
        Light emberLight;
        MaterialPropertyBlock mpb;

        float charge;             // smoothed 0..1
        float spawnAccum;
        int nextEmber;
        int rescanCountdown;

        /// <summary>0..1 heat actually being rendered. Read by tests and by the screenshot harness.</summary>
        public float Charge { get { return charge; } }
        /// <summary>True once the weapon is visibly alight.</summary>
        public bool IsBurning { get { return charge > deadzone; } }

        void Awake()
        {
            resources = GetComponent<PlayerResources>();
            viewmodel = GetComponentInChildren<WeaponViewmodel>(true);
            mpb = new MaterialPropertyBlock();
            BuildEmbers();
        }

        void OnEnable() { GameEvents.WeaponChanged += OnWeaponChanged; }
        void OnDisable() { GameEvents.WeaponChanged -= OnWeaponChanged; }

        void OnWeaponChanged(WeaponData w)
        {
            weapon = w;
            glow = null;
            bladeRoot = null;
            rescanCountdown = 2;   // the model is instantiated in the same frame; bind on the next one
        }

        void BuildEmbers()
        {
            if (emberCount <= 0) return;

            Transform parent = viewmodel != null ? viewmodel.transform : transform;
            var rootGo = new GameObject("EmberRoot");
            rootGo.transform.SetParent(parent, false);
            emberRoot = rootGo.transform;

            embers = new Transform[emberCount];
            emberVel = new Vector3[emberCount];
            emberAge = new float[emberCount];
            for (int i = 0; i < emberCount; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Ember" + i;
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.transform.SetParent(emberRoot, false);
                go.transform.localScale = Vector3.one * emberSize;
                go.SetActive(false);
                embers[i] = go.transform;
                emberAge[i] = -1f;
            }

            if (!lightEnabled) return;
            var lightGo = new GameObject("EmberLight");
            lightGo.transform.SetParent(parent, false);
            emberLight = lightGo.AddComponent<Light>();
            emberLight.type = LightType.Point;
            emberLight.color = emberHot;
            emberLight.intensity = 0f;
            emberLight.range = 0.5f;
            emberLight.shadows = LightShadows.None;
            emberLight.enabled = false;
        }

        /// <summary>
        /// Find the EnergyGlow on whatever model the viewmodel is currently holding. Done by search
        /// under the public <c>grip</c> rather than by reaching into WeaponViewmodel's private model
        /// instance, so nothing here depends on that file's internals.
        /// </summary>
        void Rebind()
        {
            if (viewmodel == null)
            {
                viewmodel = GetComponentInChildren<WeaponViewmodel>(true);
                if (viewmodel == null) return;
            }

            Transform grip = viewmodel.grip != null ? viewmodel.grip : viewmodel.model;
            if (grip == null) return;

            var found = grip.GetComponentInChildren<EnergyGlow>(true);
            // The wand swapped in for a riposte also parents under the grip and carries its own glow;
            // it is transient and must never be set alight.
            var over = viewmodel.OverrideInstance;
            if (found != null && over != null && found.transform.IsChildOf(over.transform)) found = null;

            glow = found;
            bladeRoot = found != null ? found.transform : null;

            // Embers borrow the blade's own material. A stock primitive material has _EMISSION off,
            // so a property-block emission write on it is silently ignored — the exact class of
            // no-op this project has been bitten by before.
            if (bladeRoot != null && embers != null)
            {
                var src = bladeRoot.GetComponentInChildren<Renderer>(true);
                if (src != null && src.sharedMaterial != null)
                    for (int i = 0; i < embers.Length; i++)
                    {
                        var er = embers[i] != null ? embers[i].GetComponent<Renderer>() : null;
                        if (er != null) er.sharedMaterial = src.sharedMaterial;
                    }
            }

            bladeLength = 0.4f;
            if (bladeRoot == null) return;
            var rends = bladeRoot.GetComponentsInChildren<Renderer>(true);
            float top = 0f;
            for (int i = 0; i < rends.Length; i++)
            {
                float y = bladeRoot.InverseTransformPoint(rends[i].bounds.center).y;
                if (y > top) top = y;
            }
            if (top > 0.05f) bladeLength = top;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            float target = resources != null ? resources.PyreRatio : 0f;
            charge = Mathf.MoveTowards(charge, target, dt * chargeLerpSpeed);
            float heat = charge <= deadzone ? 0f : Mathf.InverseLerp(deadzone, 1f, charge);

            if (rescanCountdown > 0 && --rescanCountdown == 0) Rebind();
            if (glow == null && bladeRoot == null && rescanCountdown == 0) Rebind();

            ApplyHeat(heat);
            ApplyLight(heat);
            AnimateEmbers(dt, heat);
        }

        void ApplyHeat(float heat)
        {
            if (glow == null) return;

            Color baseHue = weapon != null ? weapon.neon : Color.white;
            // Blend in HDR: the ember colour is authored bright, so a hot blade genuinely outshines a
            // cold one rather than only changing hue.
            Color hot = Color.Lerp(baseHue, emberHot * (1f + heat * 1.6f), heat * tintBlendAtFull);
            glow.SetTint(hot);
            glow.SetCharge(heat * glowChargeAtFull);
        }

        void ApplyLight(float heat)
        {
            if (emberLight == null) return;
            bool on = heat > 0.001f;
            if (emberLight.enabled != on) emberLight.enabled = on;
            if (!on) return;

            if (bladeRoot != null)
                emberLight.transform.position = bladeRoot.TransformPoint(Vector3.up * bladeLength * 0.6f);

            // Squared, so the light stays out of the way until the fire is genuinely raging. A flicker
            // on top keeps it reading as fire rather than as a lamp bolted to the sword.
            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.unscaledTime * 7f, 0.37f);
            emberLight.intensity = lightIntensityAtFull * heat * heat * flicker;
            emberLight.range = Mathf.Lerp(0.5f, lightRangeAtFull, heat);
            emberLight.color = Color.Lerp(emberHot, Color.white, heat * 0.25f);
        }

        void AnimateEmbers(float dt, float heat)
        {
            if (embers == null) return;

            // Spawn. Squared rate: one lonely ember at a quarter charge, a stream at full.
            if (heat > 0.001f && bladeRoot != null && emberRoot != null)
            {
                spawnAccum += dt * emberRateAtFull * heat * heat;
                int budget = 3;
                while (spawnAccum >= 1f && budget-- > 0)
                {
                    spawnAccum -= 1f;
                    Spawn(heat);
                }
                if (spawnAccum > 3f) spawnAccum = 3f;
            }

            float life = emberLife * Mathf.Lerp(0.6f, 1f, heat);
            for (int i = 0; i < embers.Length; i++)
            {
                if (emberAge[i] < 0f) continue;
                emberAge[i] += dt;
                float k = emberAge[i] / life;
                if (k >= 1f)
                {
                    emberAge[i] = -1f;
                    embers[i].gameObject.SetActive(false);
                    continue;
                }

                embers[i].localPosition += emberVel[i] * dt;
                emberVel[i] += Vector3.up * emberRise * dt;
                // Shrink and fade together: an ember that only fades reads as a dying LED.
                float s = emberSize * (1f - k) * Mathf.Lerp(0.7f, 1.5f, heat);
                embers[i].localScale = Vector3.one * s;

                var r = embers[i].GetComponent<Renderer>();
                if (r == null) continue;
                float fade = (1f - k) * (1f - k);
                r.GetPropertyBlock(mpb);
                mpb.SetColor(EmissionId, emberHot * (2.2f * fade));
                mpb.SetColor(BaseColorId, Color.black);
                r.SetPropertyBlock(mpb);
            }
        }

        void Spawn(float heat)
        {
            int i = nextEmber;
            nextEmber = (nextEmber + 1) % embers.Length;
            var tr = embers[i];
            if (tr == null) return;

            // Shed from a random point along the blade, biased toward the tip — that is where a blade
            // actually burns hottest, and it keeps the trail out of the fist.
            float t = Mathf.Lerp(0.25f, 1f, Mathf.Sqrt(Random.value));
            Vector3 world = bladeRoot.TransformPoint(Vector3.up * bladeLength * t);
            tr.position = world;
            tr.localScale = Vector3.one * emberSize;
            tr.gameObject.SetActive(true);

            Vector3 dir = new Vector3(Random.Range(-1f, 1f), Random.Range(0.4f, 1f), Random.Range(-1f, 1f)).normalized;
            emberVel[i] = dir * emberDrift * Mathf.Lerp(0.7f, 1.4f, heat);
            emberAge[i] = 0f;
        }
    }
}
