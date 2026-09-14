using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1
{
    /// <summary>
    /// The lightning tornado that stands on <see cref="CinderJudgeStorm"/>'s cylinder: a ground ring at
    /// exactly the tick radius and a few jagged helical strands rising from it and narrowing toward the
    /// floating body. Owned and driven by <see cref="CinderJudgeVisuals"/>; it schedules nothing and
    /// knows nothing about damage.
    ///
    /// <para><b>The ring IS the tell.</b> It is drawn dim and GROWING to the full radius during the
    /// wind-up, so the circle the player must leave is on the floor before the storm ignites, and it is
    /// drawn at the same radius the ticks test. A zone whose picture and whose arithmetic disagreed
    /// would be the worst kind of unfair.</para>
    ///
    /// <para>Code-driven, no assets, like <see cref="LightningEffect"/>: a handful of LineRenderers on a
    /// scene-level root (left behind by the body, not towed, the <see cref="EmberAura"/> argument),
    /// re-jittered on a crackle interval so the strands read as electricity rather than as ribbon.
    /// Animated on unscaled time so a hitstop does not freeze the crackle; its LIFE follows the enemy's
    /// scaled clock through the visuals that drive it. The hot core sits well under the parry glow (3.2)
    /// and the alert tell (3.0); the fringe is normalised under the bloom threshold.</para>
    /// </summary>
    public sealed class CinderJudgeStormFx
    {
        public const int RingSegments = 48;
        public const int StrandPoints = 20;
        /// <summary>Total degrees a strand twists from the floor to the body. ~2.5 turns.</summary>
        public const float StrandTwistDeg = 900f;
        /// <summary>Strand radius at the top, as a fraction of the ground radius: a funnel, not a tube.</summary>
        public const float FunnelTopFraction = 0.35f;

        readonly Transform root;
        readonly LineRenderer ring;
        readonly LineRenderer[] strands;
        readonly Vector3[][] jitter;
        readonly float[] strandPhase;
        readonly Vector3[] buffer;
        readonly Material ringMat, coreMat, fringeMat;
        readonly Color hue;
        readonly float crackleInterval;
        readonly float jitterAmount;
        readonly System.Random rng = new System.Random();
        float untilCrackle;
        bool ringOn, strandsOn;

        public CinderJudgeStormFx(string ownerName, Color stormHue, int strandCount, float coreIntensity,
                                  float crackleSeconds, float jitterMetres)
        {
            hue = SlashFx.NormaliseColor(stormHue);
            crackleInterval = Mathf.Max(0.01f, crackleSeconds);
            jitterAmount = Mathf.Max(0f, jitterMetres);

            root = new GameObject("CinderJudgeStorm (" + ownerName + ")").transform;

            // Three materials: the ring and the fringe strands are normalised (under the 1.05 bloom
            // threshold, like an enemy's trail); the core strands sit at coreIntensity so the funnel
            // blooms modestly against a dark floor without competing with a deflect.
            ringMat = Additive(hue);
            fringeMat = Additive(new Color(hue.r * 0.6f, hue.g * 0.6f, hue.b * 0.6f, 1f));
            coreMat = Additive(hue * Mathf.Max(1f, coreIntensity));

            ring = SlashFx.CreateLine(root, "Ring", RingSegments, 0.06f, 0.06f, true, ringMat);
            ring.alignment = LineAlignment.TransformZ;
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            int n = Mathf.Clamp(strandCount, 1, 8);
            strands = new LineRenderer[n];
            jitter = new Vector3[n][];
            strandPhase = new float[n];
            for (int i = 0; i < n; i++)
            {
                bool core = i < 2;
                strands[i] = SlashFx.CreateLine(root, "Strand" + i, StrandPoints,
                                                core ? 0.055f : 0.03f, core ? 0.02f : 0.01f, false,
                                                core ? coreMat : fringeMat);
                jitter[i] = new Vector3[StrandPoints];
                strandPhase[i] = 360f * i / n;
            }
            buffer = new Vector3[Mathf.Max(RingSegments, StrandPoints)];
            SetVisible(false, false);
        }

        public void SetVisible(bool ringVisible, bool strandsVisible)
        {
            ringOn = ringVisible;
            strandsOn = strandsVisible;
            if (ring != null) ring.enabled = ringVisible;
            for (int i = 0; i < strands.Length; i++)
                if (strands[i] != null) strands[i].enabled = strandsVisible;
        }

        /// <summary>
        /// Pose the effect for this frame. <paramref name="ringRadius"/> is the ring as drawn NOW (it
        /// grows to <paramref name="radius"/> over the wind-up); <paramref name="funnelTop"/> is the
        /// height the strands reach (the floating body's chest); <paramref name="yawDeg"/> turns the
        /// whole funnel with the body so the strands are seen to fly off it.
        /// </summary>
        public void Draw(Vector3 groundCenter, float radius, float ringRadius, float funnelTop, float yawDeg,
                         float ringAlpha, float strandAlpha, float unscaledDt)
        {
            if (root == null) return;

            untilCrackle -= unscaledDt;
            if (untilCrackle <= 0f)
            {
                untilCrackle = crackleInterval;
                for (int i = 0; i < jitter.Length; i++)
                    for (int k = 0; k < StrandPoints; k++)
                    {
                        float taper = Mathf.Sin(k / (float)(StrandPoints - 1) * Mathf.PI);
                        jitter[i][k] = new Vector3(Rand(), Rand() * 0.5f, Rand()) * (jitterAmount * taper);
                    }
            }

            if (ringOn && ring != null)
            {
                float r = Mathf.Max(0.05f, ringRadius);
                for (int i = 0; i < RingSegments; i++)
                {
                    float a = i / (float)RingSegments * Mathf.PI * 2f;
                    buffer[i] = groundCenter + new Vector3(Mathf.Cos(a) * r, 0.05f, Mathf.Sin(a) * r);
                }
                ring.SetPositions(buffer);
                Tint(ringMat, hue, Mathf.Clamp01(ringAlpha));
            }

            if (strandsOn)
            {
                float top = Mathf.Max(0.5f, funnelTop);
                for (int i = 0; i < strands.Length; i++)
                {
                    if (strands[i] == null) continue;
                    for (int k = 0; k < StrandPoints; k++)
                    {
                        float t = k / (float)(StrandPoints - 1);
                        float rr = Mathf.Lerp(radius, radius * FunnelTopFraction, t);
                        float ang = (strandPhase[i] + yawDeg + t * StrandTwistDeg) * Mathf.Deg2Rad;
                        buffer[k] = groundCenter
                                  + new Vector3(Mathf.Cos(ang) * rr, 0.1f + t * top, Mathf.Sin(ang) * rr)
                                  + jitter[i][k];
                    }
                    strands[i].SetPositions(buffer);
                }
                Tint(coreMat, hue * 1.4f, Mathf.Clamp01(strandAlpha));
                Tint(fringeMat, new Color(hue.r * 0.6f, hue.g * 0.6f, hue.b * 0.6f, 1f), Mathf.Clamp01(strandAlpha) * 0.8f);
            }
        }

        /// <summary>A random point on the ring's rim, at ground level: where an arc flies off to.</summary>
        public Vector3 RimPoint(Vector3 groundCenter, float radius)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            return groundCenter + new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius);
        }

        public void Dispose()
        {
            if (ringMat != null) Object.Destroy(ringMat);
            if (coreMat != null) Object.Destroy(coreMat);
            if (fringeMat != null) Object.Destroy(fringeMat);
            if (root != null) Object.Destroy(root.gameObject);
        }

        float Rand() => (float)rng.NextDouble() * 2f - 1f;

        static void Tint(Material m, Color c, float alpha)
        {
            if (m == null) return;
            var tinted = new Color(c.r, c.g, c.b, alpha);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tinted);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tinted);
        }

        /// <summary>
        /// The same URP/Unlit additive recipe <see cref="LightningEffect"/> and <see cref="SlashFx"/>
        /// use, taken un-normalised so the core strands may sit a little over 1.0.
        /// <see cref="SlashFx.CreateAdditiveMaterial"/> flattens its input, which is right for a spark
        /// and wrong for the one part of this effect that is allowed to bloom.
        /// </summary>
        static Material Additive(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader) { name = "CinderStormRuntime" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }
    }
}
