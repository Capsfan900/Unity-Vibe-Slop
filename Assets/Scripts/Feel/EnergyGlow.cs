using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Animates a procedurally-built viewmodel so it reads as an object charged with energy rather
    /// than a static primitive. Three layers, all driven from one hue:
    ///
    ///   Pulse — emission breathes on a sine, phase-offset per instance so two weapons on screen
    ///           never throb in lockstep.
    ///   Flow  — a bright band travels along local +Y. Each "Seg*" renderer samples a moving gaussian,
    ///           which is why blades are built as stacked segments instead of one long cube.
    ///   Float — "Float*" children orbit, bob and counter-rotate on their own clocks.
    ///
    /// ONE WRITER PER MATERIAL CHANNEL. While this component exists it owns `_EmissionColor` on every
    /// renderer it collected, and it rewrites them every frame. Anything else that pokes emission on
    /// the same renderers (WandController.Tint, OffhandViewmodel.Tint) is silently overwritten on the
    /// next frame — those call <see cref="SetTint"/> instead. The project already lost a feature to
    /// exactly this shape of bug: EnemyVisuals.CueFlash was overwritten by a still-running WindupCo,
    /// so the parry cue's silhouette never rendered. See docs/ENGINEERING-LOG.md.
    ///
    /// Cost: one cached MaterialPropertyBlock, no per-frame allocation, no lights. Runs on unscaled
    /// time so hitstop does not freeze the weapon in the player's hands.
    /// </summary>
    [DisallowMultipleComponent]
    public class EnergyGlow : MonoBehaviour
    {
        [Header("Tint")]
        [ColorUsage(true, true)] public Color tint = Color.white;

        [Tooltip("Master dimmer over every layer below. The wand should read as a WISP: a faint core you " +
                 "could hold in the dark, not a light source. Raise only if the scene gets brighter.")]
        [Range(0f, 2f)] public float brightness = 1f;

        [Tooltip("Emission multiplier at the dimmest point of the breath.")]
        public float baseIntensity = 0.34f;
        [Tooltip("How much the breath adds on top of baseIntensity.")]
        public float pulseAmplitude = 0.18f;
        [Tooltip("Breaths per second. Slow: a wisp drifts, it does not blink.")]
        public float pulseSpeed = 0.75f;

        [Header("Flow band (travels along local +Y)")]
        public bool flowEnabled = true;
        [Tooltip("Band travel speed, in normalised blade lengths per second.")]
        public float flowSpeed = 0.55f;
        [Tooltip("Extra emission at the centre of the band.")]
        public float flowStrength = 0.34f;
        [Tooltip("Band width as a fraction of the blade. Smaller = tighter, more electric.")]
        [Range(0.03f, 0.6f)] public float flowWidth = 0.10f;

        [Header("Tip")]
        [Tooltip("Tips read as the hot core, so they sit brighter than the segments.")]
        public float tipBoost = 2.4f;

        [Header("Wisp motes")]
        [Tooltip("Small drifting points around the tip. This is what makes the wand read as magical " +
                 "rather than as a glowing stick. 0 disables them.")]
        [Range(0, 6)] public int moteCount = 3;
        public float moteRadius = 0.055f;
        public float moteSize = 0.014f;
        public float moteDriftSpeed = 0.9f;

        [Header("Floating parts")]
        public float orbitSpeed = 55f;
        public float bobAmount = 0.012f;
        public float bobSpeed = 1.9f;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        MaterialPropertyBlock mpb;

        Renderer[] segments;      // "Seg*" — ordered low to high along local +Y
        float[] segmentHeights;   // normalised 0..1 position of each segment along the blade
        Renderer[] tips;          // "Tip*"
        Transform[] floats;       // "Float*"
        Transform[] motes;        // generated wisp motes near the tip
        float[] moteState;        // yaw phase, bob phase, speed per mote (packed x3)
        FloatState[] floatStates;

        float phase;              // per-instance so weapons do not sync
        float flowPos;
        float charge;             // 0..1, raised during a riposte wind-up

        struct FloatState
        {
            public Vector3 localOrigin;
            public float radius;
            public float angle;
            public float dir;      // +1 / -1 so rings counter-rotate
            public float speedMul;
            public float bobPhase;
        }

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            phase = Random.value * Mathf.PI * 2f;
            Collect();
            BuildMotes();
        }

        /// <summary>
        /// Re-scan children. Call after instantiating or swapping the model under this root.
        /// Safe when a category is absent — a wand with no Seg* parts simply does not flow.
        /// </summary>
        public void Collect()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);

            int segCount = 0, tipCount = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                string n = renderers[i].gameObject.name;
                if (n.StartsWith("Seg")) segCount++;
                else if (n.StartsWith("Tip")) tipCount++;
            }

            segments = new Renderer[segCount];
            tips = new Renderer[tipCount];
            int s = 0, t = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                string n = renderers[i].gameObject.name;
                if (n.StartsWith("Seg")) segments[s++] = renderers[i];
                else if (n.StartsWith("Tip")) tips[t++] = renderers[i];
            }

            // Normalise each segment's height along the blade so the flow band is independent of how
            // many segments a given weapon happens to use.
            segmentHeights = new float[segments.Length];
            if (segments.Length > 0)
            {
                float min = float.MaxValue, max = float.MinValue;
                for (int i = 0; i < segments.Length; i++)
                {
                    float y = segments[i].transform.localPosition.y;
                    if (y < min) min = y;
                    if (y > max) max = y;
                }
                float span = Mathf.Max(0.0001f, max - min);
                for (int i = 0; i < segments.Length; i++)
                    segmentHeights[i] = (segments[i].transform.localPosition.y - min) / span;
            }

            var trs = GetComponentsInChildren<Transform>(true);
            int fCount = 0;
            for (int i = 0; i < trs.Length; i++)
                if (trs[i].gameObject.name.StartsWith("Float")) fCount++;

            floats = new Transform[fCount];
            floatStates = new FloatState[fCount];
            int f = 0;
            for (int i = 0; i < trs.Length; i++)
            {
                if (!trs[i].gameObject.name.StartsWith("Float")) continue;
                var tr = trs[i];
                var lp = tr.localPosition;
                floats[f] = tr;
                floatStates[f] = new FloatState
                {
                    localOrigin = lp,
                    // Orbit radius comes from however far the part was authored from the axis, so the
                    // designed silhouette is preserved rather than overridden.
                    radius = new Vector2(lp.x, lp.z).magnitude,
                    angle = Mathf.Atan2(lp.z, lp.x),
                    dir = (f % 2 == 0) ? 1f : -1f,
                    speedMul = 1f + f * 0.35f,
                    bobPhase = f * 1.7f,
                };
                f++;
            }
        }

        /// <summary>Set the hue everything derives from. The single entry point for colouring a viewmodel.</summary>
        public void SetTint(Color c)
        {
            tint = c;
            ApplyEmission();   // immediate, so a swap is not dark for one frame
        }

        /// <summary>
        /// 0 = idle, 1 = fully charged. Raises pulse rate, flow speed and brightness so a riposte
        /// wind-up is visible in the hand rather than only at the target.
        /// </summary>
        public void SetCharge(float value) => charge = Mathf.Clamp01(value);

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            // Charge accelerates the whole animation, not just its brightness — cadence is what sells
            // "about to release".
            float chargeRate = 1f + charge * 2.5f;
            phase += dt * pulseSpeed * chargeRate * Mathf.PI * 2f;
            if (flowEnabled) flowPos = Mathf.Repeat(flowPos + dt * flowSpeed * chargeRate, 1f);

            ApplyEmission();
            AnimateFloats(dt, chargeRate);
            AnimateMotes(dt, chargeRate);
        }

        void ApplyEmission()
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();

            float breath = baseIntensity + pulseAmplitude * (0.5f + 0.5f * Mathf.Sin(phase));
            breath += charge * 0.85f;

            if (segments != null)
            {
                for (int i = 0; i < segments.Length; i++)
                {
                    var r = segments[i];
                    if (r == null) continue;

                    float intensity = breath;
                    if (flowEnabled && segmentHeights != null && i < segmentHeights.Length)
                    {
                        // Wrapped distance to the band centre, so the band re-enters at the base
                        // instead of popping when it runs off the tip.
                        float d = Mathf.Abs(segmentHeights[i] - flowPos);
                        d = Mathf.Min(d, 1f - d);
                        float g = Mathf.Exp(-(d * d) / (2f * flowWidth * flowWidth));
                        intensity += flowStrength * g * (1f + charge * 0.75f);
                    }
                    Write(r, tint * intensity);
                }
            }

            if (tips != null)
            {
                float tipIntensity = breath * tipBoost + charge * 1.2f;
                for (int i = 0; i < tips.Length; i++)
                    if (tips[i] != null) Write(tips[i], tint * tipIntensity);
            }
        }

        void Write(Renderer r, Color emission)
        {
            emission *= brightness;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionId, emission);
            mpb.SetColor(BaseColorId, Color.black);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// A few tiny points orbiting the tip on lazy, offset paths. Built from primitives sharing the
        /// tip's material so no new asset is needed, and animated in the same LateUpdate pass as
        /// everything else. Cost is moteCount transforms + moteCount property-block writes.
        /// </summary>
        void BuildMotes()
        {
            if (moteCount <= 0) return;

            Transform anchor = (tips != null && tips.Length > 0) ? tips[0].transform : transform;
            Material shared = (tips != null && tips.Length > 0) ? tips[0].sharedMaterial : null;

            motes = new Transform[moteCount];
            moteState = new float[moteCount * 3];
            for (int i = 0; i < moteCount; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Mote" + i;
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.transform.SetParent(anchor, false);
                go.transform.localScale = Vector3.one * moteSize;

                var r = go.GetComponent<Renderer>();
                if (shared != null) r.sharedMaterial = shared;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                motes[i] = go.transform;
                moteState[i * 3 + 0] = Random.value * Mathf.PI * 2f;          // yaw phase
                moteState[i * 3 + 1] = Random.value * Mathf.PI * 2f;          // bob phase
                moteState[i * 3 + 2] = 0.7f + Random.value * 0.6f;            // speed variation
            }
        }

        void AnimateMotes(float dt, float chargeRate)
        {
            if (motes == null) return;
            for (int i = 0; i < motes.Length; i++)
            {
                var tr = motes[i];
                if (tr == null) continue;
                float spd = moteState[i * 3 + 2];
                moteState[i * 3 + 0] += dt * moteDriftSpeed * spd * chargeRate;
                float a = moteState[i * 3 + 0];
                float bob = Mathf.Sin(Time.unscaledTime * 1.6f * spd + moteState[i * 3 + 1]);

                // Wander on a slightly irregular path so they drift rather than orbit mechanically.
                float rad = moteRadius * (0.75f + 0.35f * Mathf.Sin(a * 1.7f));
                tr.localPosition = new Vector3(Mathf.Cos(a) * rad, bob * moteRadius * 0.8f, Mathf.Sin(a) * rad);
            }
        }

        void AnimateFloats(float dt, float chargeRate)
        {
            if (floats == null) return;
            // floats is a Transform[] and survives a domain reload; floatStates does not, so it comes
            // back null with floats still populated. Rebuild rather than skip, or the parts freeze.
            // Same failure shape as the MaterialPropertyBlock null in ItemGlint - see ENGINEERING-LOG.
            if (floatStates == null || floatStates.Length != floats.Length) { Collect(); if (floatStates == null) return; }
            for (int i = 0; i < floats.Length; i++)
            {
                var tr = floats[i];
                if (tr == null) continue;
                var st = floatStates[i];

                st.angle += dt * orbitSpeed * Mathf.Deg2Rad * st.dir * st.speedMul * chargeRate;
                floatStates[i] = st;

                float bob = Mathf.Sin(Time.unscaledTime * bobSpeed + st.bobPhase) * bobAmount;
                tr.localPosition = new Vector3(
                    Mathf.Cos(st.angle) * st.radius,
                    st.localOrigin.y + bob,
                    Mathf.Sin(st.angle) * st.radius);

                // Counter-rotate the part itself so rings visibly spin rather than just translating.
                tr.localRotation = Quaternion.Euler(0f, -st.angle * Mathf.Rad2Deg * 0.6f, 0f);
            }
        }
    }
}
