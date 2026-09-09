using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1
{
    /// <summary>
    /// Procedural impact primitives: sparks, arcs, rings and glints.
    ///
    /// Art direction is Lies of P at double speed — cold, metallic, precise. Impact must read through
    /// SHAPE and DIRECTION, never through screen brightness. So everything here is thin, travels, and
    /// is gone fast; nothing expands as a blob and nothing lingers. Each element is drawn twice: a
    /// near-white hot core with a narrow tinted fringe behind it, which is what makes a bright line
    /// read as metal rather than as coloured mush.
    ///
    /// Entirely code driven — no prefabs, no particle assets, no material assets.
    /// All timing is unscaled so the hitstop that fires on the same frame as a parry does not freeze
    /// the feedback that explains the parry.
    ///
    /// PERFORMANCE: effects are POOLED, not created and destroyed. A single spark burst used to build
    /// ~25 GameObjects, 25 LineRenderers and 2 Materials and then destroy all of it a third of a second
    /// later; in a fast fight that was the dominant source of GC pressure (~25 gen0 collections/sec).
    /// A retired effect now deactivates and keeps its hierarchy AND its two materials for reuse, so the
    /// steady state allocates nothing. Per-frame colour is still written to the two owned materials
    /// rather than through per-renderer property blocks, because a spark burst has up to 24 renderers
    /// and two material writes beat twenty-four SetPropertyBlock calls.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlashFx : MonoBehaviour
    {
        enum Kind { Sparks, Arc, Ring, Flare, Beam }

        /// <summary>
        /// Hard ceiling on simultaneous effects. A fast fight can request a lot of these at once;
        /// dropping the overflow is far better than a hitch.
        /// </summary>
        const int MaxLive = 28;
        static int live;

        // Feel constants. Short lives are deliberate — the combat pace is the reason.
        const float SparkGravity = -16f;
        const float SparkDrag = 3.2f;
        const float SparkStreak = 0.035f;   // streak length per unit of speed
        const int ArcSegments = 16;
        const int RingSegments = 32;
        const int BeamSegments = 10;
        /// <summary>Fraction of a beam's life spent travelling. The rest is the hold-and-fade.</summary>
        const float BeamTravel = 0.30f;
        const float FlareWaist = 0.08f;
        const float FlareVerticalScale = 0.72f;

        static Shader unlitShader;
        static Transform camTransform;   // shared across all effects; Camera.main is a tagged lookup

        // ---- pooling ---------------------------------------------------------------------------
        // Retired effects live here with their hierarchy and materials intact. Static, so it survives
        // for the process; entries are null-checked on rent because a scene load destroys the objects
        // while leaving the references behind.
        static readonly Stack<SlashFx> pool = new Stack<SlashFx>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPoolState()
        {
            // Enter Play Mode Options may preserve managed statics while Unity destroys scene objects.
            // The stack already tolerates destroyed entries, but the separate live counter must reset too.
            pool.Clear();
            live = 0;
            camTransform = null;
        }

        struct Spark
        {
            public Vector3 pos;
            public Vector3 vel;
            public LineRenderer core;
            public LineRenderer fringe;
            public float life;
        }

        Kind kind;
        Color hue;
        float duration;
        float life;
        bool counted;   // this instance is currently included in `live`

        Material coreMat, fringeMat;
        readonly List<Spark> sparks = new List<Spark>();
        LineRenderer shapeCore, shapeFringe;

        // Every LineRenderer this instance has ever built, kept for reuse across pooled lives.
        readonly List<LineRenderer> ownedLines = new List<LineRenderer>();
        int linesInUse;

        // Flare geometry, cached: this used to allocate a Vector3[8] every frame, for every live flare
        // — and the wand charge fires flares on an accelerating cadence.
        readonly Vector3[] flarePoints = new Vector3[8];

        // Arc / Ring / Flare parameters
        Vector3 center, normal, startDir;
        float radius, degrees, size;

        // Beam parameters. The jitter offsets are baked once at build so the channel keeps its shape
        // while the head travels — a beam that re-jitters every frame reads as noise, not as a shot.
        Vector3 beamFrom, beamTo;
        readonly Vector3[] beamPoints = new Vector3[BeamSegments];
        readonly Vector3[] beamOffsets = new Vector3[BeamSegments];

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Short-lived streaks thrown from <paramref name="origin"/> along <paramref name="direction"/>.
        /// The workhorse: this is what a parry and a weapon hit actually read as.
        /// </summary>
        public static void Sparks(Vector3 origin, Vector3 direction, Color color, int count, float speed, float spread)
        {
            var fx = Spawn("Fx_Sparks", origin, color, 0.40f);
            if (fx == null) return;
            fx.kind = Kind.Sparks;
            fx.BuildSparks(origin, direction, Mathf.Clamp(count, 1, 24), speed, spread);
            fx.UpdateSparks(0f, 1f);
        }

        /// <summary>A thin crescent swipe. Appears instantly, fades with a slight outward push.</summary>
        public static void Arc(Vector3 center, Vector3 normal, Color color, float radius, float degrees, float seconds, Vector3 startDir = default)
        {
            var fx = Spawn("Fx_Arc", center, color, seconds);
            if (fx == null) return;
            fx.kind = Kind.Arc;
            fx.center = center;
            fx.normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            fx.startDir = startDir;
            fx.radius = Mathf.Max(0.05f, radius);
            fx.degrees = degrees;
            fx.BuildShape(ArcSegments, false, 0.05f, 0.012f);
            fx.UpdateArc(0f);
        }

        /// <summary>A flat expanding hoop. Grounded impacts and launches.</summary>
        public static void Ring(Vector3 center, Vector3 normal, Color color, float radius, float seconds)
        {
            var fx = Spawn("Fx_Ring", center, color, seconds);
            if (fx == null) return;
            fx.kind = Kind.Ring;
            fx.center = center;
            fx.normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            fx.radius = Mathf.Max(0.05f, radius);
            fx.BuildShape(RingSegments, true, 0.045f, 0.045f);
            fx.UpdateRing(0f);
        }

        /// <summary>
        /// A four-point star glint. Explicitly NOT a sphere — a cross of thin spikes reads as a
        /// highlight, an expanding ball reads as a cartoon explosion.
        /// </summary>
        public static void Flare(Vector3 pos, Color color, float size, float seconds)
        {
            var fx = Spawn("Fx_Flare", pos, color, seconds);
            if (fx == null) return;
            fx.kind = Kind.Flare;
            fx.center = pos;
            fx.size = Mathf.Max(0.02f, size);
            fx.BuildFlare();
            fx.UpdateFlare(0f);
        }

        /// <summary>
        /// A bolt that LEAVES <paramref name="from"/> and travels to <paramref name="to"/>.
        ///
        /// <para>This is the one primitive that answers "where did that come from": the riposte blast
        /// used to be drawn on the victim with nothing connecting it to the wand, so it read as an
        /// explosion with no source. A beam anchored at the wand tip is what makes the wand the
        /// author of the blast rather than a bystander.</para>
        ///
        /// <para>Both endpoints are sampled once, at the call. The wand is at full extension on the
        /// discharge frame, so a beam that chased a moving tip would only smear the withdraw.</para>
        /// </summary>
        public static void Beam(Vector3 from, Vector3 to, Color color, float width, float seconds)
        {
            var fx = Spawn("Fx_Beam", from, color, seconds);
            if (fx == null) return;
            fx.kind = Kind.Beam;
            fx.beamFrom = from;
            fx.beamTo = to;
            fx.BuildBeam(Mathf.Max(0.01f, width));
            fx.UpdateBeam(0f);
        }

        // ------------------------------------------------------------------ construction

        static SlashFx Spawn(string name, Vector3 pos, Color color, float seconds)
        {
            if (live >= MaxLive && !RecoverStaleLiveCount()) return null;

            SlashFx fx = null;
            while (pool.Count > 0 && fx == null)
            {
                fx = pool.Pop();          // may be a destroyed object from a previous scene
                if (fx == null) continue; // Unity's overloaded null: fall through and try the next
            }

            if (fx == null)
            {
                var go = new GameObject("Fx");
                fx = go.AddComponent<SlashFx>();
                fx.CreateMaterials();
            }

            // Callers supply interned names: renting must not allocate a concatenated string.
            fx.gameObject.name = name;
            fx.transform.position = pos;
            fx.gameObject.SetActive(true);

            fx.hue = color;
            fx.duration = Mathf.Max(0.02f, seconds);
            fx.life = 0f;
            fx.linesInUse = 0;
            fx.sparks.Clear();
            fx.shapeCore = null;
            fx.shapeFringe = null;
            fx.ApplyMaterialColours(color);

            live++;
            fx.counted = true;
            return fx;
        }

        /// <summary>
        /// The hot path trusts the O(1) counter. Only at the hard cap do we pay for a scene scan, because
        /// domain/scene reload combinations can destroy every pooled GameObject while preserving managed
        /// statics. A stale 28 must not suppress VFX forever; 28 genuinely active effects still shed load.
        /// </summary>
        static bool RecoverStaleLiveCount()
        {
            int actual = 0;
            var instances = Resources.FindObjectsOfTypeAll<SlashFx>();
            for (int i = 0; i < instances.Length; i++)
            {
                var fx = instances[i];
                if (fx != null && fx.counted && fx.gameObject.activeSelf) actual++;
            }
            live = actual;
            return live < MaxLive;
        }

        static Shader UnlitShader
        {
            get
            {
                if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
                return unlitShader;
            }
        }

        /// <summary>Built once per pooled instance and then reused for the life of the process.</summary>
        void CreateMaterials()
        {
            coreMat = Additive(Color.white);
            fringeMat = Additive(Color.white);
        }

        void ApplyMaterialColours(Color c)
        {
            // Core is pushed most of the way to white: a hot metal highlight keeps its hue only at the
            // edges. The fringe carries the item/wand identity.
            SetMatColour(coreMat, Color.Lerp(Normalise(c), Color.white, 0.78f), 1f);
            SetMatColour(fringeMat, Normalise(c), 0.7f);
        }

        static void SetMatColour(Material m, Color c, float alpha)
        {
            if (m == null) return;
            c.a = alpha;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        /// <summary>HDR colours arrive with components well above 1; flatten so alpha fading is meaningful.</summary>
        static Color Normalise(Color c)
        {
            float m = Mathf.Max(c.maxColorComponent, 0.0001f);
            return m > 1f ? new Color(c.r / m, c.g / m, c.b / m, 1f) : new Color(c.r, c.g, c.b, 1f);
        }

        /// <summary>
        /// Additive, depth-write-off material for procedural FX. Public because ItemVfx builds its own
        /// converging/drifting effects from the same visual language and should not fork this setup.
        /// The caller owns the returned material and must Destroy it.
        /// </summary>
        public static Material CreateAdditiveMaterial(Color c) => Additive(Normalise(c));

        /// <summary>Shared line-renderer setup so every procedural effect renders identically.</summary>
        public static LineRenderer CreateLine(Transform parent, string name, int points, float startWidth, float endWidth, bool loop, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            ConfigureLine(lr, points, startWidth, endWidth, loop, mat);
            return lr;
        }

        static void ConfigureLine(LineRenderer lr, int points, float startWidth, float endWidth, bool loop, Material mat)
        {
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.positionCount = points;
            lr.startWidth = startWidth;
            lr.endWidth = endWidth;
            lr.widthMultiplier = 1f;
            lr.numCapVertices = 1;
            lr.numCornerVertices = 1;
            lr.sharedMaterial = mat;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.lightProbeUsage = LightProbeUsage.Off;
            lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Flattens an HDR colour so alpha fading is meaningful. Public for ItemVfx.</summary>
        public static Color NormaliseColor(Color c) => Normalise(c);

        static Material Additive(Color c)
        {
            var m = new Material(UnlitShader) { name = "SlashFxRuntime" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // Transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);        // Additive
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        /// <summary>
        /// Rent a LineRenderer from this instance's own reserve, creating one only the first time a
        /// given slot is needed. Across a few effects the reserve reaches its high-water mark and the
        /// steady state stops allocating entirely.
        /// </summary>
        LineRenderer RentLine(string name, int points, float startWidth, float endWidth, bool loop, Material mat)
        {
            LineRenderer lr;
            if (linesInUse < ownedLines.Count)
            {
                lr = ownedLines[linesInUse];
                if (lr == null)   // destroyed out from under us; rebuild in place
                {
                    lr = CreateLine(transform, name, points, startWidth, endWidth, loop, mat);
                    ownedLines[linesInUse] = lr;
                }
                else
                {
                    lr.gameObject.SetActive(true);
                    ConfigureLine(lr, points, startWidth, endWidth, loop, mat);
                }
            }
            else
            {
                lr = CreateLine(transform, name, points, startWidth, endWidth, loop, mat);
                ownedLines.Add(lr);
            }
            linesInUse++;
            return lr;
        }

        void BuildSparks(Vector3 origin, Vector3 direction, int count, float speed, float spread)
        {
            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
            for (int i = 0; i < count; i++)
            {
                // Cone around the impact direction. Sparks that all travel identically read as a fan,
                // not as debris, so both the angle and the speed are jittered.
                Vector3 v = Quaternion.AngleAxis(Random.Range(-spread, spread), Random.onUnitSphere) * dir;
                float s = speed * Random.Range(0.55f, 1.35f);
                sparks.Add(new Spark
                {
                    pos = origin,
                    vel = v * s,
                    life = Random.Range(0.6f, 1f),
                    core = RentLine("SparkCore", 2, 0.030f, 0.004f, false, coreMat),
                    fringe = RentLine("SparkFringe", 2, 0.055f, 0.008f, false, fringeMat)
                });
            }
        }

        void BuildShape(int points, bool loop, float startWidth, float endWidth)
        {
            shapeFringe = RentLine("Fringe", points, startWidth * 2.1f, endWidth * 2.1f, loop, fringeMat);
            shapeCore = RentLine("Core", points, startWidth, endWidth, loop, coreMat);
        }

        void BuildFlare()
        {
            // A continuous line cannot represent separate strokes: the old crossed polyline drew
            // diagonal connectors through the flash. Trace one narrow four-point star silhouette.
            // Same two renderers and eight points; no crossing or doubled-back additive segments.
            shapeCore = RentLine("FlareCore", 8, 0.014f, 0.014f, true, coreMat);
            shapeFringe = RentLine("FlareFringe", 8, 0.028f, 0.028f, true, fringeMat);
        }

        void BuildBeam(float width)
        {
            Vector3 axis = beamTo - beamFrom;
            float len = axis.magnitude;
            Vector3 dir = len > 0.0001f ? axis / len : Vector3.forward;
            Vector3 right = Vector3.Cross(dir, Vector3.up);
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(dir, Vector3.right);
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right);

            // Jitter tapers to zero at both ends so the channel stays welded to the tip and the victim.
            float jitter = Mathf.Min(0.12f, len * 0.06f);
            for (int i = 0; i < BeamSegments; i++)
            {
                float t = i / (float)(BeamSegments - 1);
                float taper = Mathf.Sin(t * Mathf.PI);
                beamOffsets[i] = (right * Random.Range(-1f, 1f) + up * Random.Range(-1f, 1f)) * jitter * taper;
            }

            // Widest at the muzzle, tapering to the impact: the shot reads as being thrown forward.
            shapeFringe = RentLine("BeamFringe", BeamSegments, width * 2.4f, width * 1.1f, false, fringeMat);
            shapeCore = RentLine("BeamCore", BeamSegments, width, width * 0.35f, false, coreMat);
        }

        // ------------------------------------------------------------------ animation

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            life += dt;

            float k = Mathf.Clamp01(life / duration);
            // Sharp attack, quick tail: alpha falls off on a square so the last third is nearly gone.
            float alpha = 1f - k;
            alpha *= alpha;

            switch (kind)
            {
                case Kind.Sparks: UpdateSparks(dt, alpha); break;
                case Kind.Arc: UpdateArc(k); break;
                case Kind.Ring: UpdateRing(k); break;
                case Kind.Flare: UpdateFlare(k); break;
                case Kind.Beam: UpdateBeam(k); break;
            }

            SetAlpha(alpha);
            if (life >= duration) Retire();
        }

        void UpdateSparks(float dt, float alpha)
        {
            for (int i = 0; i < sparks.Count; i++)
            {
                var s = sparks[i];
                // Decelerate then fall: debris, not projectiles.
                s.vel += Vector3.up * (SparkGravity * dt);
                s.vel -= s.vel * Mathf.Min(1f, SparkDrag * dt);
                s.pos += s.vel * dt;

                Vector3 tail = s.pos - s.vel * SparkStreak;
                if (s.core != null) { s.core.SetPosition(0, s.pos); s.core.SetPosition(1, tail); }
                if (s.fringe != null) { s.fringe.SetPosition(0, s.pos); s.fringe.SetPosition(1, tail); }
                sparks[i] = s;
            }
        }

        void UpdateArc(float k)
        {
            // Eases outward slightly as it fades, so the swipe feels like it is still travelling.
            float r = radius * (1f + 0.18f * k);
            Vector3 a = startDir.sqrMagnitude > 0.0001f
                ? Vector3.ProjectOnPlane(startDir, normal).normalized
                : Perpendicular(normal);
            if (a.sqrMagnitude < 0.0001f) a = Perpendicular(normal);

            for (int i = 0; i < ArcSegments; i++)
            {
                float t = ArcSegments > 1 ? i / (float)(ArcSegments - 1) : 0f;
                float ang = Mathf.Lerp(-degrees * 0.5f, degrees * 0.5f, t);
                Vector3 p = center + Quaternion.AngleAxis(ang, normal) * a * r;
                if (shapeCore != null) shapeCore.SetPosition(i, p);
                if (shapeFringe != null) shapeFringe.SetPosition(i, p);
            }
        }

        void UpdateRing(float k)
        {
            // Punches out fast then coasts — ease-out, matching the lightning ring already in the game.
            float r = radius * (1f - (1f - k) * (1f - k));
            Vector3 a = Perpendicular(normal);
            Vector3 b = Vector3.Cross(normal, a);
            for (int i = 0; i < RingSegments; i++)
            {
                float ang = i / (float)RingSegments * Mathf.PI * 2f;
                Vector3 p = center + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * r;
                if (shapeCore != null) shapeCore.SetPosition(i, p);
                if (shapeFringe != null) shapeFringe.SetPosition(i, p);
            }
        }

        void UpdateFlare(float k)
        {
            // Contact is the peak, including the spawn frame. Growth delayed the visible payoff
            // by 30% of a short life; at low frame rates a needle's flash could miss its own peak.
            float s = size * (1f - Mathf.Clamp01(k));

            // Camera.main is a tagged lookup; cache it across all live effects rather than paying it
            // per flare per frame.
            if (camTransform == null)
            {
                var cam = Camera.main;
                if (cam != null) camTransform = cam.transform;
            }
            Vector3 right = camTransform != null ? camTransform.right : Vector3.right;
            Vector3 up = camTransform != null ? camTransform.up : Vector3.up;
            Vector3 r = right * s;
            Vector3 u = up * (s * FlareVerticalScale);
            flarePoints[0] = center + r;
            flarePoints[1] = center + (r + u) * FlareWaist;
            flarePoints[2] = center + u;
            flarePoints[3] = center + (-r + u) * FlareWaist;
            flarePoints[4] = center - r;
            flarePoints[5] = center - (r + u) * FlareWaist;
            flarePoints[6] = center - u;
            flarePoints[7] = center + (r - u) * FlareWaist;

            if (shapeCore != null) shapeCore.SetPositions(flarePoints);
            if (shapeFringe != null)
            {
                shapeFringe.SetPositions(flarePoints);
                shapeFringe.widthMultiplier = 1f + 0.4f * (1f - k);
            }
        }

        void UpdateBeam(float k)
        {
            // The head races out over the first BeamTravel of the life, then the whole channel holds
            // and fades. Every point behind the head sits on the line, so the beam is always anchored
            // at the muzzle — that anchor is the entire reason this primitive exists.
            float travel = Mathf.Clamp01(k / BeamTravel);
            travel = 1f - (1f - travel) * (1f - travel);   // ease out: leaves fast, arrives soft
            Vector3 head = Vector3.Lerp(beamFrom, beamTo, travel);

            for (int i = 0; i < BeamSegments; i++)
            {
                float t = i / (float)(BeamSegments - 1);
                beamPoints[i] = Vector3.Lerp(beamFrom, head, t) + beamOffsets[i] * travel;
            }
            if (shapeCore != null) shapeCore.SetPositions(beamPoints);
            if (shapeFringe != null) shapeFringe.SetPositions(beamPoints);
        }

        static Vector3 Perpendicular(Vector3 n)
        {
            Vector3 p = Vector3.Cross(n, Vector3.forward);
            if (p.sqrMagnitude < 0.001f) p = Vector3.Cross(n, Vector3.right);
            return p.normalized;
        }

        void SetAlpha(float a)
        {
            SetMatColour(coreMat, coreMat != null && coreMat.HasProperty("_BaseColor") ? coreMat.GetColor("_BaseColor") : Color.white, a);
            SetMatColour(fringeMat, fringeMat != null && fringeMat.HasProperty("_BaseColor") ? fringeMat.GetColor("_BaseColor") : Color.white, a * 0.7f);
        }

        /// <summary>
        /// Return to the pool instead of destroying. The hierarchy, the LineRenderers and both materials
        /// are kept — that is the whole point, and it is what takes the steady state to zero allocation.
        /// </summary>
        void Retire()
        {
            for (int i = 0; i < ownedLines.Count; i++)
                if (ownedLines[i] != null) ownedLines[i].gameObject.SetActive(false);

            sparks.Clear();
            shapeCore = null;
            shapeFringe = null;
            linesInUse = 0;

            if (counted) { live = Mathf.Max(0, live - 1); counted = false; }

            gameObject.SetActive(false);
            pool.Push(this);
        }

        void OnDestroy()
        {
            // Only a real teardown (scene unload / quit) reaches here; Retire() does not destroy.
            if (counted) { live = Mathf.Max(0, live - 1); counted = false; }
            if (coreMat != null) Destroy(coreMat);
            if (fringeMat != null) Destroy(fringeMat);
        }
    }
}
