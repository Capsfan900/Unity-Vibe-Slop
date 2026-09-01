using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1
{
    /// <summary>
    /// Jagged multi-point bolts, in two shapes.
    ///
    /// <para><see cref="Strike"/> is a STORM: bolts fall out of the sky onto a list of ground
    /// positions, with an expanding ground ring and a flash of point lights. That is the Stormbreak
    /// item — weather, arriving from above, with no author on screen.</para>
    ///
    /// <para><see cref="Bundle"/> is a DISCHARGE: five channels braided into a rope that leaves a
    /// weapon tip and terminates inside a victim. Same jitter, opposite reading — this one has an
    /// author, a direction and a destination, which is what the Pyre needed and the storm could never
    /// give it. See <see cref="PyreArc"/>, which pairs it with the mist that rides the same channel.</para>
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

        // ---- bundle mode ---------------------------------------------------------------------
        // A DIRECTED bundle: several jagged channels braided around one spine, anchored exactly at a
        // weapon tip and exactly at a victim's contact point. This is what the Pyre discharge is —
        // "a bundle of lightning bolts coming out of the weapon and into the enemy" — as opposed to
        // Strike above, which is a storm falling out of the sky onto a list of ground positions.
        //
        // Five channels is the number that reads as a rope rather than as either one line (a laser)
        // or a mess. Strand 0 is dead centre so the bundle always has a bright straight core; the
        // other four spiral around it, which is what makes the shape read as depth in first person.

        /// <summary>Channels in one bundle: one straight spine plus four braided around it.</summary>
        public const int BundleStrands = 5;
        /// <summary>Points per channel. Below ~10 a jagged line reads as a polygon, not as lightning.</summary>
        public const int BundlePoints = 14;
        /// <summary>Total radians the braid twists over the whole span. ~1.6 turns.</summary>
        public const float BundleTwist = 10f;
        /// <summary>Seconds a bundle lives. Short: this punctuates an impact, it does not linger.</summary>
        public const float BundleSeconds = 0.34f;
        /// <summary>How many times a bundle re-jitters. The strobe is the whole read of "electric".</summary>
        public const int BundleCrackles = 8;
        public const float BundleCrackleInterval = 0.03f;
        /// <summary>
        /// How much of the hue the braid strands keep. A wand colour is HDR at ~2.6x; at full intensity
        /// every additive channel clips to white and the bundle loses its colour entirely.
        /// </summary>
        public const float BundleFringe = 0.34f;

        /// <summary>Braid radius for a span of <paramref name="distance"/> metres.</summary>
        public static float BundleSpread(float distance)
        {
            return Mathf.Clamp(distance * 0.075f, 0.06f, 0.55f);
        }

        /// <summary>Per-point random deviation for a span of <paramref name="distance"/> metres.</summary>
        public static float BundleJitter(float distance)
        {
            return Mathf.Clamp(distance * 0.045f, 0.035f, 0.30f);
        }

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

        /// <summary>
        /// A braided bundle of bolts from <paramref name="from"/> (a weapon tip) to
        /// <paramref name="to"/> (a victim's contact point). No ground ring, no sky bolts — this is a
        /// directed discharge, and every channel terminates EXACTLY on the target so the effect reads
        /// as arriving in the enemy rather than as passing near it.
        /// </summary>
        public static void Bundle(Vector3 from, Vector3 to, Color color, float scale)
        {
            if ((to - from).sqrMagnitude < 0.0004f) return;   // 2 cm: nothing to draw
            var go = new GameObject("LightningBundle");
            go.transform.position = from;
            go.AddComponent<LightningEffect>().InitBundle(from, to, color, Mathf.Clamp(scale, 0.25f, 4f));
        }

        class Bolt
        {
            public LineRenderer lr;
            public Vector3 from, to;
            public float jitter;
            public Vector3[] points;

            // Bundle channels only. A braided strand is regenerated from the pure geometry below
            // instead of the free-form jitter FillBolt applies to a storm bolt.
            public bool braid;
            public int strand;
            public float spread;
            public float phase;
        }

        static Shader unlitShader;

        readonly List<Bolt> bolts = new List<Bolt>();
        readonly List<Light> lights = new List<Light>();
        readonly List<float> lightBase = new List<float>();

        LineRenderer ring;
        Material mat;
        /// <summary>
        /// The FRINGE material, dimmer than <see cref="mat"/>. Only bundle mode uses it, and only for
        /// the braid strands and the forks — the spine keeps the hot one.
        ///
        /// <para>Wand and weapon colours are HDR at 2.4–2.6x. Drawn additively at that intensity every
        /// channel clips to pure white, so a five-strand bundle rendered as five identical white wires
        /// and the colour of the wand was nowhere on screen. This is the same core-plus-fringe trick
        /// SlashFx uses on every primitive: a near-white hot core with a narrow TINTED fringe behind it
        /// is what makes a bright line read as a charged object rather than as a white scribble.</para>
        /// </summary>
        Material braidMat;
        Color color;
        Vector3 origin;
        float radius;
        float life;
        float nextCrackle;
        int crackled;
        System.Random rng;

        // Per-instance cadence so a bundle can be shorter and strobe faster than a storm without
        // either of them having to reach for the other's constants.
        float duration = Duration;
        float crackleInterval = CrackleInterval;
        int crackleCount = CrackleCount;
        float twistPhase;

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

        void InitBundle(Vector3 from, Vector3 to, Color c, float scale)
        {
            origin = from;
            color = c;
            radius = 0f;
            duration = BundleSeconds;
            crackleInterval = BundleCrackleInterval;
            crackleCount = BundleCrackles;
            rng = new System.Random(Random.Range(int.MinValue, int.MaxValue));
            mat = CreateAdditiveMaterial(c);
            braidMat = CreateAdditiveMaterial(FringeOf(c));

            float dist = Vector3.Distance(from, to);
            float spread = BundleSpread(dist) * scale;
            float jitter = BundleJitter(dist) * scale;

            for (int s = 0; s < BundleStrands; s++)
            {
                // The spine is the thickest and cleanest; the braid strands are thinner wires around
                // it. Tapering end width to a fraction of start width points the whole bundle: it is
                // fattest where it leaves the weapon and needle-sharp where it enters the enemy.
                bool spine = s == 0;
                var bolt = new Bolt
                {
                    from = from,
                    to = to,
                    jitter = spine ? jitter * 0.5f : jitter,
                    spread = spine ? 0f : spread,
                    strand = s,
                    braid = true,
                    phase = (float)rng.NextDouble() * Mathf.PI * 2f,
                    points = new Vector3[BundlePoints],
                };
                bolt.lr = MakeLine("Strand", BundlePoints,
                    (spine ? 0.045f : 0.028f) * scale,
                    (spine ? 0.015f : 0.009f) * scale, false,
                    spine ? mat : braidMat);
                bolts.Add(bolt);
            }

            // Forks peeling off the bundle mid-flight. Free-form (not braided) so they read as the
            // charge spilling out of the channel rather than as more of the rope.
            Vector3 axis = (to - from).normalized;
            for (int i = 0; i < 3; i++)
            {
                float k = Mathf.Lerp(0.25f, 0.8f, (float)rng.NextDouble());
                Vector3 root = Vector3.Lerp(from, to, k);
                var dir = new Vector3(
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble() * 2f - 1f);
                dir = Vector3.ProjectOnPlane(dir, axis);
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;
                dir.Normalize();
                float len = Mathf.Lerp(0.25f, 0.75f, (float)rng.NextDouble()) * Mathf.Max(0.6f, dist * 0.22f);
                AddBolt(root, root + dir * len, 6, 0.022f * scale, 0.006f * scale, jitter * 0.6f, braidMat);
            }

            // Two lights, one at each end: the weapon visibly casts the flash, and the victim is lit
            // by what just arrived in it. The rest of the scene stays dark, which is the point.
            AddLight(from, 6f * scale, 5f * scale);
            AddLight(to, 8f * scale, 7f * scale);
            Randomise();
        }

        // ------------------------------------------------------------------ bundle geometry

        /// <summary>
        /// Fill <paramref name="into"/> with one braided channel from <paramref name="from"/> to
        /// <paramref name="to"/>. PURE and side-effect free so the shape can be asserted in an
        /// EditMode test without a scene: pass <paramref name="rng"/> as null for the deterministic
        /// braid with no random jitter.
        ///
        /// <para>Both endpoints are written EXACTLY. That is the invariant the whole effect rests on:
        /// a channel that merely passes near the wand tip and near the chest reads as an unrelated
        /// spark, and the taper below (sin over the span) is what guarantees the deviation goes to
        /// zero at both ends instead of being clipped there.</para>
        /// </summary>
        public static void FillStrand(Vector3 from, Vector3 to, int strand, int strandCount,
                                      float spread, float jitter, float phase,
                                      System.Random rng, Vector3[] into)
        {
            if (into == null || into.Length < 2) return;
            int n = into.Length;

            Vector3 axis = to - from;
            float len = axis.magnitude;
            Vector3 dir = len > 0.0001f ? axis / len : Vector3.forward;

            // A stable perpendicular basis. Vector3.up first so a horizontal bundle (the common case)
            // braids around a predictable frame instead of flipping when the aim crosses an axis.
            Vector3 right = Vector3.Cross(dir, Vector3.up);
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(dir, Vector3.right);
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right).normalized;

            // Strand 0 is the spine (spread forced to zero by the caller); 1..n-1 sit evenly around
            // the circle so the rope has no gap in it.
            float around = strand <= 0 || strandCount < 2
                ? 0f
                : (strand - 1) / (float)(strandCount - 1) * Mathf.PI * 2f;

            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1);
                float taper = Mathf.Sin(k * Mathf.PI);
                float tw = around + phase + k * BundleTwist;
                Vector3 lateral = (right * Mathf.Cos(tw) + up * Mathf.Sin(tw)) * spread * taper;

                if (rng != null)
                {
                    float jx = ((float)rng.NextDouble() * 2f - 1f) * jitter * taper;
                    float jy = ((float)rng.NextDouble() * 2f - 1f) * jitter * taper;
                    lateral += right * jx + up * jy;
                }
                into[i] = Vector3.Lerp(from, to, k) + lateral;
            }

            // Anchored exactly, not approximately. Floating point in the lerp above is enough to
            // leave a visible millimetre gap at a wand tip that the camera is 40 cm from.
            into[0] = from;
            into[n - 1] = to;
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

        LineRenderer MakeLine(string name, int pointCount, float startWidth, float endWidth, bool loop, Material material)
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
            lr.sharedMaterial = material != null ? material : mat;
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

        void AddBolt(Vector3 from, Vector3 to, int points, float startWidth, float endWidth, float jitter, Material material = null)
        {
            var bolt = new Bolt
            {
                from = from,
                to = to,
                jitter = jitter,
                points = new Vector3[points],
                lr = MakeLine("Bolt", points, startWidth, endWidth, false, material)
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
            // (Strike only — a bundle has no ground ring.)
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
            lightBase.Add(intensity);
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
            if (b.braid)
            {
                FillStrand(b.from, b.to, b.strand, BundleStrands, b.spread, b.jitter,
                           b.phase + twistPhase, rng, b.points);
                return;
            }

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

            // The braid keeps turning between crackles, so the bundle writhes along its own axis
            // instead of only strobing in place. ~0.4 of a turn over a bundle's life.
            twistPhase += dt * 7f;

            if (crackled < crackleCount && life >= nextCrackle)
            {
                crackled++;
                nextCrackle = life + crackleInterval;
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
            float holdUntil = crackleCount * crackleInterval;
            float alpha = life <= holdUntil ? 1f : 1f - Mathf.Clamp01((life - holdUntil) / Mathf.Max(0.01f, duration - holdUntil));
            alpha *= alpha;

            Fade(mat, color, alpha);
            Fade(braidMat, FringeOf(color), alpha);

            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] == null) continue;
                // Read from what the light was actually built with. Hardcoding 12/8 here silently
                // overrode the bundle's much dimmer end-lights and blew the frame out.
                float baseIntensity = i < lightBase.Count ? lightBase[i] : 8f;
                lights[i].intensity = baseIntensity * alpha;
            }

            if (life >= duration) Destroy(gameObject);
        }

        /// <summary>The braid's colour: the hue kept, the HDR intensity spent, so it does not clip.</summary>
        public static Color FringeOf(Color c)
        {
            return new Color(c.r * BundleFringe, c.g * BundleFringe, c.b * BundleFringe, c.a);
        }

        static void Fade(Material m, Color c, float alpha)
        {
            if (m == null) return;
            var tinted = new Color(c.r, c.g, c.b, alpha);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tinted);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tinted);
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (braidMat != null) Destroy(braidMat);
        }
    }
}
