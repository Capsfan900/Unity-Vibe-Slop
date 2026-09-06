using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Procedural Berserk-Eclipse sky: a blood-red gradient dome, dark clotted nebulae, a dying star
    /// field and the Eclipse itself — an enormous black solar disc ringed by burning light — all baked
    /// into ONE mesh. Two submeshes / two materials: everything LDR shares one, and the white-hot corona
    /// rim alone sits on an HDR-tinted second (see <see cref="CoronaHdrBoost"/>), so exactly one sky
    /// element can cross the bloom threshold. Two draw calls for the entire sky.
    ///
    /// Why geometry and not a skybox: the gameplay camera is built by PrefabFactory with
    /// <see cref="CameraClearFlags.SolidColor"/>, so <c>RenderSettings.skybox</c> would never be drawn.
    /// Geometry renders regardless of clear flags.
    ///
    /// Three properties make this behave like a skybox even though it is a mesh:
    ///   1. <b>Render queue 1000 (Background)</b> — it draws before all opaque geometry.
    ///   2. <b>ZWrite off</b> (inherent to the sprite shader) — it writes no depth, so every platform,
    ///      wall and enemy paints over it. It can never occlude gameplay, and a wall correctly hides
    ///      the eclipse behind it.
    ///   3. <b>It follows the camera</b> (see <see cref="SkyFollower"/>) at a small radius, so it never
    ///      parallaxes as the player runs the 200-unit course, and it is always inside the camera's
    ///      300-unit far plane.
    ///
    /// The small radius is also what makes it immune to fog: at radius 25 with fog starting at 45 the
    /// fog factor is zero, so the sky stays crisp no matter what the shader does with fog. That
    /// deliberately does not depend on any per-material fog trick, which is unreliable in URP because
    /// fog keywords are global.
    ///
    /// Draw order inside the mesh is index order (nothing depth-sorts with ZWrite off), so the
    /// index buffer is written back-to-front: dome, nebulae, stars, halo and corona falloff, the black
    /// disc, and finally — in submesh 1, drawn after all of submesh 0 — the white-hot rim licking the
    /// disc's edge. That is what puts stars *behind* the disc and the burning rim *over* its limb.
    ///
    /// The mesh is on its own layer so the NavMeshSurface (which bakes from RenderMeshes on layer 0)
    /// cannot collect a 25-unit sphere as walkable geometry.
    /// </summary>
    public static class Starfield
    {
        /// <summary>Layer for sky geometry. Excluded from the NavMesh bake, queried by nothing.</summary>
        public const int SkyLayer = 9;
        public const string SkyLayerName = "Sky";

        // ---- shipped eclipse geometry (rule 9: these ARE the shipped values; SkyEclipseTests asserts them) ----

        /// <summary>Angular diameter of the eclipse. Berserk scale: at 38 degrees it fills over half the
        /// vertical frame at FOV 70 — it must dominate the sky, not decorate it.</summary>
        public const float DefaultEclipseDiameterDeg = 38f;

        /// <summary>Elevation of the disc centre. Bottom limb = pitch - diameter/2 = 3 degrees above the
        /// horizon: oppressively low, but the black centre stays mostly off the eye-level band that
        /// combat-range enemy heads are read against, and the corona glow floods the horizon behind them.</summary>
        public const float DefaultEclipsePitchDeg = 22f;

        /// <summary>HDR multiplier on the corona-rim material — the ONLY sky element allowed over the 1.05
        /// bloom threshold. Vertex colours clamp at 1.0, so submesh 0 (the whole red field) physically
        /// cannot bloom or reach the ~1.25 ACES desaturation knee; presence there is bought with area
        /// and contrast, never intensity.</summary>
        public const float CoronaHdrBoost = 1.35f;

        /// <summary>Dome tessellation. The dome is the FIRST thing written into the sky mesh, so its
        /// (latitude + 1) * (longitude + 1) = <see cref="DomeVertexCount"/> vertices are the mesh's first
        /// vertices -- SkyEclipseTests reads these rather than re-deriving the layout.</summary>
        public const int DomeLongitude = 40;
        public const int DomeLatitude = 20;
        public const int DomeVertexCount = (DomeLatitude + 1) * (DomeLongitude + 1);

        /// <summary>
        /// Vertex colours and no fog. URP/Unlit ignores COLOR and applies fog, so it cannot be used here.
        /// </summary>
        const string PreferredShader = "Sprites/Default";
        const string FallbackShader = "Universal Render Pipeline/Unlit";

        /// <summary>Background queue: drawn before opaque geometry, so everything paints over it.</summary>
        const int BackgroundQueue = 1000;

        // Premultiplied-alpha blending (Blend One OneMinusSrcAlpha) means a colour must be scaled by its
        // own alpha, or a soft edge reads as a bright halo instead of fading out.
        static Color PM(Color c, float a)
        {
            return new Color(c.r * a, c.g * a, c.b * a, a);
        }

        /// <summary>
        /// Builds the sky under <paramref name="parent"/> and returns its root.
        /// </summary>
        /// <param name="starCount">Stars in the field. ~1200 is one draw call and costs nothing.</param>
        /// <param name="radius">
        /// Sky radius. Keep it well BELOW <c>RenderSettings.fogStartDistance</c> — that is what makes the
        /// sky immune to fog without relying on shader keywords.
        /// </param>
        /// <param name="seed">Fixed so a rebuild produces the same sky.</param>
        /// <param name="includeEclipse">The Berserk eclipse, drawn last so it silhouettes the stars.</param>
        /// <param name="eclipseYawDeg">0 = +Z, the direction the course runs and the boss arena sits.</param>
        /// <param name="eclipsePitchDeg">Elevation above the horizon. Low, matching the ember key light.</param>
        /// <param name="eclipseDiameterDeg">Angular diameter. Enormous: it IS the level's focal image.</param>
        public static GameObject Build(
            Transform parent,
            int starCount = 1200,
            float radius = 25f,
            int seed = 20260830,
            bool includeEclipse = true,
            float eclipseYawDeg = 0f,
            float eclipsePitchDeg = DefaultEclipsePitchDeg,
            float eclipseDiameterDeg = DefaultEclipseDiameterDeg)
        {
            var root = new GameObject("Starfield");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.layer = SkyLayer;

            var verts = new List<Vector3>(8192);
            var colors = new List<Color>(8192);
            var tris = new List<int>(16384);
            var rng = new System.Random(seed);

            Vector3 eclipseDir = Direction(eclipseYawDeg, eclipsePitchDeg);

            // --- 1. Gradient dome ------------------------------------------------------------------
            // The base sky. Written first so everything else paints over it.
            BuildDome(verts, colors, tris, radius, eclipseDir);

            // --- 1b. Horizon silhouette --------------------------------------------------------------
            // 2026-09-06 VFX pass: "the sky and surrounds must look polished and like a complete game."
            // Elden Ring's Erdtree and Dark Souls 3's distant Irithyll skyline both put something with a
            // SHAPE between the player and the sky, so the horizon reads as a place seen from within,
            // not a hard edge where geometry stops. Pure near-black, no new hue and no light budget: it
            // silhouettes against the dome's horizon band the same way a body silhouettes against it.
            BuildHorizonSilhouette(verts, colors, tris, radius, rng);

            // --- 2. Nebulae ------------------------------------------------------------------------
            // Large, very dim soft discs. They do most of the work of making the sky feel like a place
            // rather than a black void with dots in it.
            BuildNebulae(verts, colors, tris, radius, rng);

            // --- 3. Stars --------------------------------------------------------------------------
            // A third of them are pulled toward a tilted great circle so the field has a visible
            // galactic band instead of reading as uniform noise.
            BuildStars(verts, colors, tris, radius, starCount, rng);

            // --- 4. Eclipse ------------------------------------------------------------------------
            // Last in the index buffer: the corona burns over the stars and the disc eats its centre.
            // The white-hot rim goes into ITS OWN triangle list — submesh 1, the HDR material.
            var trisHot = new List<int>(1024);
            if (includeEclipse)
                BuildEclipse(verts, colors, tris, trisHot, radius, eclipseDir, eclipseDiameterDeg);

            var mesh = new Mesh { name = "SkyMesh" };
            // 5-6k verts fits UInt16 comfortably; being explicit documents the budget.
            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.subMeshCount = trisHot.Count > 0 ? 2 : 1;
            mesh.SetTriangles(tris, 0, false);   // false: do NOT recalculate bounds yet
            if (trisHot.Count > 0) mesh.SetTriangles(trisHot, 1, false);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = root.AddComponent<MeshRenderer>();
            // Submesh 1 (the corona rim alone) gets its own material with an HDR tint: the only sky
            // element that may cross the bloom threshold. Everything else shares the LDR material.
            if (trisHot.Count > 0)
                renderer.sharedMaterials = new[] { CreateSkyMaterial(1f), CreateSkyMaterial(CoronaHdrBoost) };
            else
                renderer.sharedMaterial = CreateSkyMaterial(1f);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;

            root.AddComponent<SkyFollower>();

            return root;
        }

        static Material CreateSkyMaterial(float hdrBoost)
        {
            var shader = Shader.Find(PreferredShader);
            if (shader == null)
            {
                shader = Shader.Find(FallbackShader);
                Debug.LogWarning($"[Starfield] '{PreferredShader}' not found; falling back to '{FallbackShader}'. " +
                                 "That shader ignores vertex colours, so the sky will render as a flat tint.");
            }

            bool hot = hdrBoost > 1f;
            var mat = new Material(shader) { name = hot ? "M_SkyCorona (runtime)" : "M_Sky (runtime)" };
            // Background queue is the whole trick: drawn before opaque geometry, writing no depth.
            // The corona sits one step later so its ordering after the disc never depends on submesh
            // iteration order.
            mat.renderQueue = hot ? BackgroundQueue + 1 : BackgroundQueue;
            // The tint multiplies the (clamped, LDR) vertex colours. Above 1 here is what lets the rim
            // — and only the rim — exceed 1.0 and therefore bloom.
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(hdrBoost, hdrBoost, hdrBoost, 1f));
            return mat;
        }

        // ---- sky elements --------------------------------------------------------------------------

        /// <summary>Unit direction from a yaw (0 = +Z) and a pitch above the horizon.</summary>
        static Vector3 Direction(float yawDeg, float pitchDeg)
        {
            float yaw = yawDeg * Mathf.Deg2Rad;
            float pitch = pitchDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(pitch);
            return new Vector3(Mathf.Sin(yaw) * c, Mathf.Sin(pitch), Mathf.Cos(yaw) * c).normalized;
        }

        /// <summary>
        /// An indexed sphere, vertex-coloured by elevation. Black-red at the zenith — the dark itself
        /// is red now, not violet — a blood band at the horizon, and a burning bias toward the eclipse
        /// so the sky agrees with the key light instead of fighting it. The horizon band is deliberately
        /// the brightest part of the field (~0.29 peak, well under 1.0): it is the backdrop every
        /// combat-range enemy silhouette is read against, and a near-black enemy on a mid-red ground
        /// reads far better than the old dark-on-dark violet.
        /// </summary>
        static void BuildDome(List<Vector3> v, List<Color> c, List<int> t, float radius, Vector3 eclipseDir)
        {
            const int longitude = DomeLongitude;
            const int latitude = DomeLatitude;

            Color zenith = Hex("#1A0407");    // black-red overhead: dark, but the dark is red
            Color horizon = Hex("#4A0A0D");   // blood band at eye level — the silhouette backdrop
            Color nadir = Hex("#0A0203");     // below the floor; almost never seen
            Color ember = Hex("#8A1A08");     // burning bias added around the eclipse

            int baseIndex = v.Count;

            for (int lat = 0; lat <= latitude; lat++)
            {
                // 0 at the south pole, 1 at the north pole.
                float lt = lat / (float)latitude;
                float phi = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, lt);
                float y = Mathf.Sin(phi);
                float ringR = Mathf.Cos(phi);

                for (int lon = 0; lon <= longitude; lon++)
                {
                    float ln = lon / (float)longitude;
                    float theta = ln * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Sin(theta) * ringR, y, Mathf.Cos(theta) * ringR);
                    v.Add(dir * radius);

                    // Vertical gradient.
                    Color col;
                    if (y >= 0f) col = Color.Lerp(horizon, zenith, Mathf.Pow(y, 0.75f));
                    else col = Color.Lerp(horizon, nadir, Mathf.Pow(-y, 0.6f));

                    // Burning bias toward the eclipse. Wider than the old pow-6 glow: the whole
                    // quadrant of sky around the dead sun is on fire, which is what makes the frame
                    // read as the Eclipse rather than a red night with a spot in it.
                    float toward = Mathf.Max(0f, Vector3.Dot(dir, eclipseDir));
                    col += ember * Mathf.Pow(toward, 4f);

                    c.Add(PM(col, 1f));
                }
            }

            int stride = longitude + 1;
            for (int lat = 0; lat < latitude; lat++)
            {
                for (int lon = 0; lon < longitude; lon++)
                {
                    int a = baseIndex + lat * stride + lon;
                    int b = a + 1;
                    int d = a + stride;
                    int e = d + 1;
                    // Winding is irrelevant: the sprite shader is Cull Off, so the dome is visible from
                    // inside without flipping triangles.
                    t.Add(a); t.Add(d); t.Add(b);
                    t.Add(b); t.Add(d); t.Add(e);
                }
            }
        }

        /// <summary>Angular height range of a single spire, degrees. Jagged and irregular -- ruins, not a wall.</summary>
        const float SilhouetteMinDeg = 1.2f;
        const float SilhouetteMaxDeg = 7.5f;
        /// <summary>Segments around the full horizon. Cheap: two extra triangles per segment, one draw call.</summary>
        const int SilhouetteSegments = 64;

        /// <summary>
        /// A jagged ring of near-black ruin spires sitting just below the horizon, all the way around.
        /// Drawn nearer than the dome (radius 0.975 against the dome's 1.0) so it silhouettes correctly,
        /// and biased low so it never competes with the eclipse or a mid-air read.
        /// </summary>
        static void BuildHorizonSilhouette(List<Vector3> v, List<Color> c, List<int> t, float radius, System.Random rng)
        {
            const int n = SilhouetteSegments;
            float r = radius * 0.975f;
            Color body = Hex("#0A0203");     // matches the dome's nadir: no new hue introduced
            Color rimHot = Hex("#6A1608");   // a thin ember catch-light along the topmost edge only

            int baseIdx = v.Count;
            const float basePitch = -3f;     // a touch below the horizon: grounded, not floating
            var heights = new float[n];
            for (int i = 0; i < n; i++) heights[i] = Lerp(rng, SilhouetteMinDeg, SilhouetteMaxDeg);

            for (int i = 0; i <= n; i++)
            {
                int wi = i % n;
                float yaw = i / (float)n * 360f;
                v.Add(Direction(yaw, basePitch) * r);
                c.Add(PM(body, 1f));
                v.Add(Direction(yaw, basePitch + heights[wi]) * r);
                c.Add(PM(Color.Lerp(body, rimHot, 0.6f), 1f));
            }
            for (int i = 0; i < n; i++)
            {
                int b0 = baseIdx + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                t.Add(b0); t.Add(t0); t.Add(b1);
                t.Add(b1); t.Add(t0); t.Add(t1);
            }
        }

        static void BuildNebulae(List<Vector3> v, List<Color> c, List<int> t, float radius, System.Random rng)
        {
            // Deliberately dim: churning clots of darker and warmer red, so the field feels like a
            // slowly boiling sky rather than a flat gradient. Nothing here approaches the corona.
            Color[] palette =
            {
                Hex("#4A0E10"),   // clotted blood
                Hex("#320609"),   // dried blood, near-black
                Hex("#5A1A08"),   // ember rust
                Hex("#3A0B14"),   // bruised crimson
            };

            const int count = 11;
            for (int i = 0; i < count; i++)
            {
                // Biased above the horizon: nebulae below it are hidden by the level anyway.
                Vector3 dir = RandomDirection(rng, -0.15f);
                float sizeDeg = Lerp(rng, 14f, 30f);
                float r = radius * Mathf.Tan(sizeDeg * 0.5f * Mathf.Deg2Rad);
                Color col = palette[rng.Next(palette.Length)];
                float alpha = Lerp(rng, 0.10f, 0.22f);

                Basis(dir, out Vector3 right, out Vector3 up);
                AddSoftDisc(v, c, t, dir * radius * 0.985f, right * r, up * r, col, alpha, 14);
            }
        }

        static void BuildStars(List<Vector3> v, List<Color> c, List<int> t, float radius, int starCount, System.Random rng)
        {
            // The Eclipse sky is not a starry night: the field survives, but dying — pale embers,
            // not cold points. Temperature variation keeps it from reading as noise.
            Color coldWhite = Hex("#FFC9A8");  // pale ember (name kept: still 70% of the field)
            Color blue = Hex("#D6684A");       // dim blood-ember minority
            Color amber = Hex("#FFCE96");

            // A tilted great circle: a third of the stars cluster near it, giving a galactic band.
            Vector3 bandNormal = new Vector3(0.42f, 0.84f, -0.34f).normalized;
            int bandStars = starCount / 3;

            // Smaller than the old cold field: against a mid-tone red ground an unlit quad reads as
            // a pale square, not a point, so size does the work brightness used to.
            float sizeMin = radius * 0.0014f;
            float sizeMax = radius * 0.0044f;

            for (int i = 0; i < starCount; i++)
            {
                Vector3 dir;
                if (i < bandStars)
                {
                    // Pick a direction, then flatten it toward the band plane.
                    Vector3 raw = RandomDirection(rng, -1f);
                    float along = Vector3.Dot(raw, bandNormal);
                    raw -= bandNormal * along * Lerp(rng, 0.80f, 0.99f);
                    dir = raw.sqrMagnitude > 1e-5f ? raw.normalized : Vector3.up;
                }
                else dir = RandomDirection(rng, -1f);

                float roll = (float)rng.NextDouble();
                Color tint = roll < 0.70f ? coldWhite : (roll < 0.90f ? blue : amber);

                // Most stars are faint; a few are bright. Squaring the roll keeps the field from
                // looking like uniform confetti.
                float brightRoll = (float)rng.NextDouble();
                // Capped at 0.55: the stars recede under the red field — the Eclipse owns this sky.
                float brightness = Mathf.Lerp(0.12f, 0.55f, brightRoll * brightRoll);
                float size = Mathf.Lerp(sizeMin, sizeMax, brightRoll * brightRoll);
                if (brightRoll > 0.985f) size *= 1.9f;   // a handful of standouts

                Basis(dir, out Vector3 right, out Vector3 up);
                // Quads face the sphere centre, which is where the camera sits, so they are camera-facing
                // without any per-frame billboarding work.
                AddQuad(v, c, t, dir * radius * 0.99f, right * size, up * size, PM(tint * brightness, 1f));
            }
        }

        /// <summary>
        /// The Eclipse, back to front: a vast blood halo, a hotter mid-glow, the saturated corona
        /// falloff, the black disc — and finally, into the HOT triangle list (submesh 1, HDR material),
        /// the thin white-hot rim drawn OVER the disc's limb. Presence is bought with AREA (the disc is
        /// ~38 degrees across, the halo more than twice that) and CONTRAST (a near-black disc against a
        /// rim at full brightness), never with field intensity: ACES desaturates a saturated red toward
        /// orange above ~1.25, so everything red here stays LDR and only the near-white rim crosses the
        /// bloom threshold — and pushing a near-white toward white is exactly what burning should do.
        /// </summary>
        static void BuildEclipse(List<Vector3> v, List<Color> c, List<int> t, List<int> tHot,
                                 float radius, Vector3 dir, float diameterDeg)
        {
            Basis(dir, out Vector3 right, out Vector3 up);
            Vector3 center = dir * radius * 0.96f;

            float discR = radius * Mathf.Tan(diameterDeg * 0.5f * Mathf.Deg2Rad);

            // Vast outer halo: the sky around the dead sun is on fire, ~2.3x the disc.
            AddSoftDisc(v, c, t, center + dir * (radius * 0.004f), right * discR * 2.3f, up * discR * 2.3f,
                        Hex("#8A1206"), 0.42f, 32);

            // Mid glow, hotter and tighter.
            AddSoftDisc(v, c, t, center + dir * (radius * 0.008f), right * discR * 1.55f, up * discR * 1.55f,
                        Hex("#BE2A0C"), 0.32f, 32);

            // Corona falloff: saturated fire fading outward. LDR — stays red, never blooms.
            AddRing(v, c, t, center + dir * (radius * 0.012f), right, up,
                    discR * 1.05f, discR * 1.42f, Hex("#FF4A10"), 0.9f, 64);

            // The disc: a dead sun. Not float-zero black — #0D0304 keeps ~2/255 under it, so an
            // 8-10/255 enemy body overlapping the disc is dim-on-dark rather than a hole in the world.
            AddDisc(v, c, t, center + dir * (radius * 0.016f), right * discR, up * discR, Hex("#0D0304"), 64);

            // The burning rim: white-hot at the limb, gone within ~8% of the radius. It starts just
            // INSIDE the disc edge and is drawn after it, so the fire licks over the black limb.
            AddRing(v, c, tHot, center + dir * (radius * 0.020f), right, up,
                    discR * 0.995f, discR * 1.075f, Hex("#FFD9A8"), 1f, 96);
        }

        // ---- mesh primitives -----------------------------------------------------------------------

        static void AddQuad(List<Vector3> v, List<Color> c, List<int> t, Vector3 center, Vector3 right, Vector3 up, Color col)
        {
            int i = v.Count;
            v.Add(center - right - up);
            v.Add(center + right - up);
            v.Add(center + right + up);
            v.Add(center - right + up);
            for (int k = 0; k < 4; k++) c.Add(col);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>Triangle fan, opaque at the centre and fading to nothing at the rim.</summary>
        static void AddSoftDisc(List<Vector3> v, List<Color> c, List<int> t, Vector3 center, Vector3 right, Vector3 up, Color col, float alpha, int segments)
        {
            int centre = v.Count;
            v.Add(center);
            c.Add(PM(col, alpha));

            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                v.Add(center + right * Mathf.Cos(a) + up * Mathf.Sin(a));
                c.Add(PM(col, 0f));
            }
            for (int s = 0; s < segments; s++)
            {
                int p = centre + 1 + s;
                int q = centre + 1 + (s + 1) % segments;
                t.Add(centre); t.Add(p); t.Add(q);
            }
        }

        /// <summary>Flat fan at a single colour and full opacity.</summary>
        static void AddDisc(List<Vector3> v, List<Color> c, List<int> t, Vector3 center, Vector3 right, Vector3 up, Color col, int segments)
        {
            int centre = v.Count;
            Color pm = PM(col, 1f);
            v.Add(center);
            c.Add(pm);

            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                v.Add(center + right * Mathf.Cos(a) + up * Mathf.Sin(a));
                c.Add(pm);
            }
            for (int s = 0; s < segments; s++)
            {
                int p = centre + 1 + s;
                int q = centre + 1 + (s + 1) % segments;
                t.Add(centre); t.Add(p); t.Add(q);
            }
        }

        /// <summary>Annulus, bright at the inner edge and transparent at the outer.</summary>
        static void AddRing(List<Vector3> v, List<Color> c, List<int> t, Vector3 center, Vector3 right, Vector3 up,
                            float innerRadius, float outerRadius, Color col, float innerAlpha, int segments)
        {
            int start = v.Count;
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                Vector3 dir = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                v.Add(center + dir * innerRadius);
                c.Add(PM(col, innerAlpha));
                v.Add(center + dir * outerRadius);
                c.Add(PM(col, 0f));
            }
            for (int s = 0; s < segments; s++)
            {
                int i0 = start + s * 2;
                int o0 = i0 + 1;
                int i1 = start + (s + 1) % segments * 2;
                int o1 = i1 + 1;
                t.Add(i0); t.Add(o0); t.Add(i1);
                t.Add(i1); t.Add(o0); t.Add(o1);
            }
        }

        // ---- utils ---------------------------------------------------------------------------------

        /// <summary>Uniform direction on the sphere, optionally clamped to keep it above a given height.</summary>
        static Vector3 RandomDirection(System.Random rng, float minY)
        {
            float y = Mathf.Lerp(Mathf.Max(-1f, minY), 1f, (float)rng.NextDouble());
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float theta = (float)rng.NextDouble() * Mathf.PI * 2f;
            return new Vector3(Mathf.Sin(theta) * r, y, Mathf.Cos(theta) * r);
        }

        static void Basis(Vector3 dir, out Vector3 right, out Vector3 up)
        {
            Vector3 reference = Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right;
            right = Vector3.Normalize(Vector3.Cross(reference, dir));
            up = Vector3.Cross(dir, right);
        }

        static float Lerp(System.Random rng, float a, float b)
        {
            return Mathf.Lerp(a, b, (float)rng.NextDouble());
        }

        static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }
    }
}
