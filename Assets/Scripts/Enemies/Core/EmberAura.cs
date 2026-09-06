using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// An enemy that burns: a constant emission floor on the body, a stream of embers rising off it,
    /// and one weak point light. Bolt this onto any enemy and it becomes a creature of fire or energy
    /// without touching its brain, its timing or its silhouette language.
    ///
    /// <para><b>Why the glow is asked for and never written.</b> <see cref="EnemyVisuals.WriteBody"/>
    /// is the single writer of the body renderers' <c>_BaseColor</c> and <c>_EmissionColor</c>, so this
    /// calls <see cref="EnemyVisuals.SetAura"/> instead of setting a property block of its own. That is
    /// the same arrangement <see cref="WeaponEmber"/> has with <see cref="EnergyGlow"/> on the player's
    /// blade, and it exists because this project has twice lost a feature to two writers on one
    /// material channel. ONE WRITER PER CHANNEL.</para>
    ///
    /// <para><b>Why the fire does not out-shout the fight.</b> Emission on an enemy is spoken for: it
    /// means "you deflected". So the burning floor ships far below the parry spike, and it is passed
    /// through the same <c>chargeDark</c> the rest of the body uses, which makes a wind-up read as the
    /// fire being SUCKED IN and the strike as it flooding back. The aura therefore reinforces the
    /// telegraph instead of washing it out — the enemy going dark as it charges is the single most
    /// important tell it has.</para>
    ///
    /// <para><b>It burns hotter as it breaks.</b> Intensity rides <c>Posture.Ratio</c>, exactly like
    /// the eye does on every other enemy, so a player already fluent in "the light is climbing, the
    /// break is close" reads this body for free.</para>
    ///
    /// <para>Costs one additive material, a fixed pool of quads and (optionally) one point light. No
    /// per-frame allocation. Unscaled time throughout, so hitstop does not freeze the fire — a frozen
    /// flame reads as a dropped frame.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class EmberAura : MonoBehaviour
    {
        [Header("Colour")]
        [Tooltip("The fire's hue. Over the 1.05 bloom threshold so it blooms, but nowhere near the " +
                 "parry glow's 3.2 — a deflect must stay the brightest thing an enemy ever does.")]
        [ColorUsage(true, true)] public Color emberHot = new Color(1f, 0.45f, 0.12f, 1f) * 1.5f;

        [Header("Body glow")]
        [Tooltip("Emission floor at full health. Deliberately dim: this is a creature lit from inside, " +
                 "not a lamp. Raising it past ~0.6 starts eating the parry read.")]
        [Range(0f, 1f)] public float glowAtRest = 0.22f;
        [Tooltip("Emission floor as posture approaches a break. The body stokes up as it loses the " +
                 "exchange, which is the same language the eye speaks on every other enemy.")]
        [Range(0f, 1f)] public float glowAtBreak = 0.55f;
        [Tooltip("Breaths per second of the idle flicker. Slow and shallow — fire moves, it does not blink.")]
        public float pulseSpeed = 1.7f;
        [Range(0f, 0.5f)] public float pulseAmount = 0.14f;

        [Header("Embers")]
        [Range(0, 40)] public int emberCount = 18;
        [Tooltip("Embers spawned per second at rest. The pool is fixed; only the RATE and lifetime move.")]
        public float emberRate = 9f;
        public float emberSize = 0.045f;
        public float emberLife = 1.1f;
        [Tooltip("Metres per second the embers rise. Slower than sparks: these drift off a body, they " +
                 "are not thrown from an impact.")]
        public float emberRise = 0.85f;
        public float emberDrift = 0.35f;
        [Tooltip("Radius around the body that embers are shed from, and how far up it they start.")]
        public float emberRadius = 0.34f;
        public float emberFromHeight = 0.15f;
        public float emberToHeight = 1.55f;

        [Header("Light")]
        public bool lightEnabled = true;
        [Tooltip("Weak and short-range ON PURPOSE. The scene runs heavy bloom and a URP asset that " +
                 "allows few additional lights per object; this project has already shipped a riposte " +
                 "that rendered as a black screen. The fire must be legible without washing out the " +
                 "body it is burning on.")]
        public float lightIntensity = 1.35f;
        public float lightRange = 3.2f;
        public float lightHeight = 0.95f;

        EnemyVisuals visuals;
        EnemyController owner;
        Transform emberRoot;
        Transform[] embers;
        Vector3[] emberVel;
        float[] emberAge;
        Light auraLight;
        Material emberMat;      // owned here, destroyed with the component
        float spawnAccum;
        int nextEmber;
        float phase;

        /// <summary>The emission floor being rendered this frame. Read by tests and the capture harness.</summary>
        public float Glow { get; private set; }

        void Awake()
        {
            visuals = GetComponentInChildren<EnemyVisuals>(true);
            owner = GetComponentInParent<EnemyController>();
            // Per-instance so two burning enemies on screen never pulse in lockstep, which reads as
            // one animation playing twice rather than two things being alight.
            phase = Random.value * 10f;
            Build();
        }

        void OnDestroy()
        {
            if (emberMat != null) Destroy(emberMat);
            // The root is not a child of this enemy, so nothing else will collect it.
            if (emberRoot != null) Destroy(emberRoot.gameObject);
        }

        void Build()
        {
            // ADDITIVE, like every other spark in the project. A lit material with a dark base colour
            // is a HOLE in the frame at this size rather than a faint mote; additive can only ever add
            // light. Same call SlashFx and WeaponEmber use.
            emberMat = SlashFx.CreateAdditiveMaterial(emberHot);

            // A root at SCENE level, not under the enemy. Embers shed from a body should be left
            // behind as it moves, not towed along with it — parented under the enemy they slide
            // sideways during a lunge and read as decoration rather than as fire coming off it.
            // It is a real object rather than an unparented free-for-all so the pool is still owned,
            // still findable in the Hierarchy, and cleaned up in OnDestroy instead of leaking a
            // handful of quads into the scene every time an enemy dies.
            var rootGo = new GameObject("EmberAura (" + name + ")");
            emberRoot = rootGo.transform;

            if (emberCount > 0)
            {
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
                    r.sharedMaterial = emberMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    go.transform.SetParent(emberRoot, false);
                    go.transform.localScale = Vector3.one * emberSize;
                    go.SetActive(false);
                    embers[i] = go.transform;
                    emberAge[i] = -1f;
                }
            }

            if (!lightEnabled) return;
            var lightGo = new GameObject("EmberAuraLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, lightHeight, 0f);
            auraLight = lightGo.AddComponent<Light>();
            auraLight.type = LightType.Point;
            auraLight.color = emberHot;
            auraLight.intensity = lightIntensity;
            auraLight.range = lightRange;
            auraLight.shadows = LightShadows.None;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            // ---- how hot ----------------------------------------------------------------------
            // Posture.Ratio climbs toward a break, so the body stokes up as it loses the exchange.
            float heat = glowAtRest;
            if (owner != null && owner.Posture != null)
                heat = Mathf.Lerp(glowAtRest, glowAtBreak, Mathf.Clamp01(owner.Posture.Ratio));

            float breath = 1f + Mathf.Sin((Time.unscaledTime + phase) * pulseSpeed * Mathf.PI * 2f) * pulseAmount;
            Glow = heat * breath;

            // Asked for, never written. See the class doc.
            if (visuals != null) visuals.SetAura(emberHot, Glow);
            if (auraLight != null) auraLight.intensity = lightIntensity * breath * (heat / Mathf.Max(0.0001f, glowAtRest)) * 0.5f;

            // ---- embers -----------------------------------------------------------------------
            if (embers == null) return;

            spawnAccum += emberRate * (0.6f + heat) * dt;
            while (spawnAccum >= 1f)
            {
                spawnAccum -= 1f;
                Spawn();
            }

            for (int i = 0; i < embers.Length; i++)
            {
                if (emberAge[i] < 0f) continue;
                emberAge[i] += dt;
                float k = emberAge[i] / emberLife;
                if (k >= 1f)
                {
                    emberAge[i] = -1f;
                    embers[i].gameObject.SetActive(false);
                    continue;
                }
                embers[i].position += emberVel[i] * dt;
                // Embers accelerate upward as they cool and lighten, and shrink to nothing so they
                // wink out rather than vanishing mid-air.
                emberVel[i] += Vector3.up * (emberRise * 0.6f) * dt;
                float s = emberSize * (1f - k) * (1f - k);
                embers[i].localScale = new Vector3(s, s, s);
            }
        }

        void Spawn()
        {
            int i = nextEmber;
            nextEmber = (nextEmber + 1) % embers.Length;

            Vector2 disc = Random.insideUnitCircle * emberRadius;
            Vector3 at = transform.position
                       + new Vector3(disc.x, Random.Range(emberFromHeight, emberToHeight), disc.y);

            var t = embers[i];
            // Positioned in world space under the scene-level root, so the body moves out from under
            // its own fire instead of towing it. That is most of what makes it read as burning rather
            // than as a decorated mesh.
            t.position = at;
            t.localScale = Vector3.one * emberSize;
            t.gameObject.SetActive(true);

            emberVel[i] = new Vector3(Random.Range(-emberDrift, emberDrift),
                                      emberRise * Random.Range(0.7f, 1.3f),
                                      Random.Range(-emberDrift, emberDrift));
            emberAge[i] = 0f;
        }

        void OnDisable()
        {
            if (embers == null) return;
            for (int i = 0; i < embers.Length; i++)
            {
                if (embers[i] == null) continue;
                emberAge[i] = -1f;
                embers[i].gameObject.SetActive(false);
            }
            // Hand the body back: a disabled aura must not leave the enemy stuck alight.
            if (visuals != null) visuals.SetAura(Color.black, 0f);
        }
    }
}
