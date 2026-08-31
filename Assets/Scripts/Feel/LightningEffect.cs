using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1
{
    /// <summary>
    /// One-shot lightning storm for the Stormbreak item: a jagged bolt onto every victim, an
    /// expanding ground ring and a flash of point lights.
    ///
    /// Entirely code driven — no prefabs, no particle assets, no material assets — and self
    /// destructing. Everything runs on unscaled time so the hitstop that fires alongside the strike
    /// does not freeze the effect mid-crackle.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightningEffect : MonoBehaviour
    {
        const float Duration = 0.5f;
        const float CrackleInterval = 0.04f;
        const int CrackleCount = 4;
        const float RingGrowSeconds = 0.25f;

        const int BoltPoints = 14;
        const float BoltHeight = 18f;
        const float BoltJitter = 0.5f;
        const int RingSegments = 48;
        const int MaxLights = 8;

        /// <summary>
        /// Fire a strike. Safe to call with an empty or null <paramref name="targets"/> list — the
        /// ring and a few decorative bolts still play, so the item never feels like it fizzled.
        /// </summary>
        public static void Strike(Vector3 origin, List<Vector3> targets, Color color, float radius)
        {
            var go = new GameObject("LightningStrike");
            go.transform.position = origin;
            go.AddComponent<LightningEffect>().Init(origin, targets, color, radius);
        }

        class Bolt
        {
            public LineRenderer lr;
            public Vector3 from, to;
            public float jitter;
            public Vector3[] points;
        }

        static Shader unlitShader;

        readonly List<Bolt> bolts = new List<Bolt>();
        readonly List<Light> lights = new List<Light>();

        LineRenderer ring;
        Material mat;
        Color color;
        Vector3 origin;
        float radius;
        float life;
        float nextCrackle;
        int crackled;
        System.Random rng;

        void Init(Vector3 strikeOrigin, List<Vector3> targets, Color c, float r)
        {
            origin = strikeOrigin;
            color = c;
            radius = Mathf.Max(0.5f, r);
            rng = new System.Random(Random.Range(int.MinValue, int.MaxValue));
            mat = CreateAdditiveMaterial(c);

            var strikePoints = new List<Vector3>();
            if (targets != null && targets.Count > 0)
            {
                strikePoints.AddRange(targets);
            }
            else
            {
                // Nothing was in range. Still put bolts down around the ring so the use reads as a
                // storm rather than a dud.
                for (int i = 0; i < 3; i++)
                {
                    float ang = i / 3f * Mathf.PI * 2f + (float)rng.NextDouble();
                    strikePoints.Add(origin + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius * 0.6f);
                }
            }

            foreach (var p in strikePoints) BuildBolt(p);
            BuildRing();
            BuildLights(strikePoints);
            Randomise();
        }

        // ------------------------------------------------------------------ construction

        static Shader UnlitShader
        {
            get
            {
                if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
                return unlitShader;
            }
        }

        static Material CreateAdditiveMaterial(Color c)
        {
            var m = new Material(UnlitShader) { name = "LightningRuntime" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);

            // Additive, no depth write: bolts should glow over the scene and blow out the bloom.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // 1 = Transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);       // 2 = Additive
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        LineRenderer MakeLine(string name, int pointCount, float startWidth, float endWidth, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.positionCount = pointCount;
            lr.startWidth = startWidth;
            lr.endWidth = endWidth;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.sharedMaterial = mat;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.lightProbeUsage = LightProbeUsage.Off;
            lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return lr;
        }

        void BuildBolt(Vector3 target)
        {
            Vector3 top = target + Vector3.up * BoltHeight;
            AddBolt(top, target, BoltPoints, 0.18f, 0.05f, BoltJitter);

            // One or two short forks peeling off the main channel.
            int forks = 1 + rng.Next(2);
            for (int i = 0; i < forks; i++)
            {
                float k = Mathf.Lerp(0.3f, 0.75f, (float)rng.NextDouble());
                Vector3 from = Vector3.Lerp(top, target, k);
                var dir = new Vector3(
                    (float)rng.NextDouble() * 2f - 1f,
                    -0.4f - (float)rng.NextDouble() * 0.6f,
                    (float)rng.NextDouble() * 2f - 1f).normalized;
                float len = Mathf.Lerp(2f, 4.5f, (float)rng.NextDouble());
                AddBolt(from, from + dir * len, 6, 0.08f, 0.02f, BoltJitter * 0.6f);
            }
        }

        void AddBolt(Vector3 from, Vector3 to, int points, float startWidth, float endWidth, float jitter)
        {
            var bolt = new Bolt
            {
                from = from,
                to = to,
                jitter = jitter,
                points = new Vector3[points],
                lr = MakeLine("Bolt", points, startWidth, endWidth, false)
            };
            bolts.Add(bolt);
        }

        void BuildRing()
        {
            var go = new GameObject("Ring");
            go.transform.SetParent(transform, false);
            // Lay the ribbon flat on the ground instead of billboarding it at the camera.
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.positionCount = RingSegments;
            ring.startWidth = 0.25f;
            ring.endWidth = 0.25f;
            ring.numCapVertices = 2;
            ring.alignment = LineAlignment.TransformZ;
            ring.sharedMaterial = mat;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.lightProbeUsage = LightProbeUsage.Off;
            ring.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        void BuildLights(List<Vector3> strikePoints)
        {
            AddLight(origin + Vector3.up * 1.5f, 16f, 12f);
            int count = Mathf.Min(strikePoints.Count, MaxLights - 1);
            for (int i = 0; i < count; i++) AddLight(strikePoints[i] + Vector3.up * 1.2f, 10f, 8f);
        }

        void AddLight(Vector3 position, float range, float intensity)
        {
            var go = new GameObject("Flash");
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            float m = Mathf.Max(color.maxColorComponent, 0.0001f);
            l.color = color / m;
            lights.Add(l);
        }

        // ------------------------------------------------------------------ animation

        /// <summary>Re-jitter every bolt so the channels visibly crackle for the first few frames.</summary>
        void Randomise()
        {
            foreach (var b in bolts)
            {
                FillBolt(b);
                b.lr.SetPositions(b.points);
            }
        }

        void FillBolt(Bolt b)
        {
            int n = b.points.Length;
            Vector3 axis = b.to - b.from;
            float len = axis.magnitude;
            Vector3 dir = len > 0.0001f ? axis / len : Vector3.down;

            Vector3 right = Vector3.Cross(dir, Vector3.forward);
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(dir, Vector3.right);
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right);

            for (int i = 0; i < n; i++)
            {
                float k = n > 1 ? i / (float)(n - 1) : 0f;
                // taper to zero at both ends so the bolt stays anchored to sky and target
                float taper = Mathf.Sin(k * Mathf.PI);
                float jx = ((float)rng.NextDouble() * 2f - 1f) * b.jitter * taper;
                float jy = ((float)rng.NextDouble() * 2f - 1f) * b.jitter * taper;
                b.points[i] = Vector3.Lerp(b.from, b.to, k) + right * jx + up * jy;
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            life += dt;

            if (crackled < CrackleCount && life >= nextCrackle)
            {
                crackled++;
                nextCrackle = life + CrackleInterval;
                Randomise();
            }

            // ground ring punches outward fast then holds
            if (ring != null)
            {
                float grow = Mathf.Clamp01(life / RingGrowSeconds);
                float r = radius * (1f - (1f - grow) * (1f - grow));   // ease out
                for (int i = 0; i < RingSegments; i++)
                {
                    float a = i / (float)RingSegments * Mathf.PI * 2f;
                    ring.SetPosition(i, origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r + Vector3.up * 0.05f);
                }
            }

            // hold at full brightness through the crackle, then fade out
            float holdUntil = CrackleCount * CrackleInterval;
            float alpha = life <= holdUntil ? 1f : 1f - Mathf.Clamp01((life - holdUntil) / Mathf.Max(0.01f, Duration - holdUntil));
            alpha *= alpha;

            if (mat != null)
            {
                var c = new Color(color.r, color.g, color.b, alpha);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            }

            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] == null) continue;
                float baseIntensity = i == 0 ? 12f : 8f;
                lights[i].intensity = baseIntensity * alpha;
            }

            if (life >= Duration) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }
}
