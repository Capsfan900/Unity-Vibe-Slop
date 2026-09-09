using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Procedural Berserk-Eclipse sky: a midnight-blue gradient dome, dark clotted nebulae, a cold star
    /// field, a handful of distant ringed planets and the Eclipse itself — an enormous black solar disc
    /// ringed by burning light — all baked into ONE mesh. Two submeshes / two materials: everything LDR shares one, and the white-hot corona
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
    /// index buffer is written back-to-front: dome, nebulae, stars, planets, halo and corona falloff,
    /// the black disc, and finally — in submesh 1, drawn after all of submesh 0 — the white-hot rim
    /// licking the disc's edge. That is what puts stars *behind* the disc and the burning rim *over*
    /// its limb, and what lets each planet's ring pass behind its own body and then in front of it
    /// without any depth buffer at all (see <see cref="BuildPlanets"/>).
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
        /// bloom threshold. Vertex colours clamp at 1.0, so submesh 0 (the whole cold field, planets and
        /// rings included) physically cannot bloom or reach the ~1.25 ACES desaturation knee; presence
        /// there is bought with area and contrast, never intensity.</summary>
        public const float CoronaHdrBoost = 1.35f;

        /// <summary>Dome tessellation. The dome is the FIRST thing written into the sky mesh, so its
        /// (latitude + 1) * (longitude + 1) = <see cref="DomeVertexCount"/> vertices are the mesh's first
        /// vertices -- SkyEclipseTests reads these rather than re-deriving the layout.</summary>
        public const int DomeLongitude = 40;
        public const int DomeLatitude = 20;
        public const int DomeVertexCount = (DomeLatitude + 1) * (DomeLongitude + 1);

        // ---- distant planets (2026-09-06 cold pass) ------------------------------------------------

        /// <summary>How many planets hang in the dome. Four: "a bunch... not too many". Five started to
        /// read as a diagram of a solar system rather than a sky; three left the half of the dome away
        /// from the eclipse empty.</summary>
        public const int PlanetCount = 4;

        /// <summary>Hard ceiling on any planet or ring vertex's peak channel. They are SCENERY, never
        /// tells: at 0.55 a planet sits 2.5x under the corona rim's <see cref="CoronaHdrBoost"/> 1.35 and
        /// ~2.9x under the enemy bolt, so nothing in the sky can ever compete with an attack tell. They
        /// live in submesh 0, whose material tint is exactly 1.0, so this is belt AND braces: the ceiling
        /// is the intent, the LDR submesh is the guarantee.</summary>
        public const float PlanetPeakCeiling = 0.55f;

        /// <summary>
        /// A distant planet. Placed by yaw/pitch rather than a position so the composition is stated in
        /// the terms the frame is read in. Every one sits at pitch >= 40 (high in the dome, clear of the
        /// eye-level band where bolts and enemy silhouettes are read) and >= 50 degrees of arc from the
        /// eclipse centre, which is just outside the eclipse halo's 44-degree radius — so a planet never
        /// sits behind a bolt the player is trying to read, and never crowds the level's focal image.
        /// </summary>
        public struct PlanetDef
        {
            public float yawDeg;
            public float pitchDeg;
            /// <summary>Angular diameter. The eclipse is 38; the biggest planet here is a quarter of it.</summary>
            public float diameterDeg;
            /// <summary>The limb facing the eclipse. Peak channel must stay under <see cref="PlanetPeakCeiling"/>.</summary>
            public Color lit;
            /// <summary>The far limb. Near-black, so the body reads as a SPHERE and not a coin.</summary>
            public Color shadow;
            /// <summary>Inner / outer ring radius as multiples of the body radius. 0 = no rings.</summary>
            public float ringInner, ringOuter;
            /// <summary>Ellipse flattening: 1 = face-on circle, 0 = edge-on line. Saturn reads at ~0.3.</summary>
            public float ringFlatten;
            public Color ringColor;
            public float ringAlpha;
        }

        /// <summary>Shipped planets (rule 9: these ARE the values; SkyEclipseTests asserts them).</summary>
        public static PlanetDef[] Planets => new[]
        {
            // The cobalt giant: the composition's counterweight, opposite the eclipse and high.
            new PlanetDef { yawDeg = 118f, pitchDeg = 47f, diameterDeg = 9.5f,
                            lit = Hex("#4A6288"), shadow = Hex("#0A0F1C"),
                            ringInner = 1.34f, ringOuter = 2.05f, ringFlatten = 0.26f,
                            ringColor = Hex("#5A6E8A"), ringAlpha = 0.50f },
            // A pale bare moon, almost overhead. No rings: two ringed planets is a motif, four is wallpaper.
            new PlanetDef { yawDeg = 208f, pitchDeg = 62f, diameterDeg = 5.0f,
                            lit = Hex("#6F7B88"), shadow = Hex("#0A0E14"),
                            ringInner = 0f, ringOuter = 0f, ringFlatten = 0f,
                            ringColor = Color.black, ringAlpha = 0f },
            // The ice-ringed one, tilted more face-on so its rings are unmistakably rings.
            new PlanetDef { yawDeg = 296f, pitchDeg = 43f, diameterDeg = 7.0f,
                            lit = Hex("#3D7A8A"), shadow = Hex("#08151C"),
                            ringInner = 1.28f, ringOuter = 1.92f, ringFlatten = 0.36f,
                            ringColor = Hex("#4E7E8B"), ringAlpha = 0.45f },   // 0x8B = 0.545: authored UNDER PlanetPeakCeiling, not merely clamped to it
            // Small and far, on the eclipse's side of the sky but 52 degrees off it and 58 degrees up.
            new PlanetDef { yawDeg = 52f, pitchDeg = 58f, diameterDeg = 3.6f,
                            lit = Hex("#3E4C78"), shadow = Hex("#06080F"),
                            ringInner = 0f, ringOuter = 0f, ringFlatten = 0f,
                            ringColor = Color.black, ringAlpha = 0f },
        };

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

            // --- 3b. Planets -----------------------------------------------------------------------
            // Scenery, not a tell. Written after the stars (so a planet occludes the field behind it) and
            // BEFORE the eclipse (so the corona always burns over everything, including a planet).
            BuildPlanets(verts, colors, tris, radius, eclipseDir);

            // --- 4. Eclipse ------------------------------------------------------------------------
            // Last in the index buffer: the corona burns over the stars and the disc eats its centre.
            // The white-hot rim goes into ITS OWN triangle list — submesh 1, the HDR material.
            var trisHot = new List<int>(1024);
            if (includeEclipse)
                BuildEclipse(verts, colors, tris, trisHot, radius, eclipseDir, eclipseDiameterDeg);
            else
                BuildHorizonAtmosphere(verts, colors, tris, radius);

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

        static void BuildHorizonAtmosphere(List<Vector3> vertices, List<Color> colors,
                                          List<int> triangles, float radius)
        {
            const int longitude = DomeLongitude;
            // Fine latitude rings keep the smooth angular grade from becoming two straight bands.
            // -7 is above the sea's clipped edge from the 36 m crest. The upper grade reaches +18,
            // so the sky emerges gradually rather than switching on in a stripe below the eclipse.
            // The disc and hot rim draw AFTER this layer; high secondary planets stay above it.
            var pitches = new List<float> { -90f, -30f, -10f };
            for (int pitch = -7; pitch <= 18; pitch++) pitches.Add(pitch);
            // Sprites/Default consumes mesh vertex colours directly, whereas unity_FogColor is in
            // the active rendering colour space. Match it rather than baking a second blue palette.
            Color fog = RenderingColor(RenderSettings.fogColor);
            int first = vertices.Count;
            for (int ring = 0; ring < pitches.Count; ring++)
            {
                for (int lon = 0; lon <= longitude; lon++)
                {
                    vertices.Add(Direction(lon * 360f / longitude, pitches[ring]) * (radius * 0.94f));
                    // SpriteFrag premultiplies RGB itself. Premultiplying the vertices as well
                    // darkens a fog-on-fog blend by alpha twice, leaving a charcoal seam.
                    Color color = fog;
                    color.a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-7f, 18f, pitches[ring]));
                    colors.Add(color);
                }
            }
            int stride = longitude + 1;
            for (int ring = 0; ring < pitches.Count - 1; ring++)
            {
                for (int lon = 0; lon < longitude; lon++)
                {
                    int a = first + ring * stride + lon;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
        }

        static Color RenderingColor(Color color)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        }

        /// <summary>Unit direction from a yaw (0 = +Z) and a pitch above the horizon.</summary>
        static Vector3 Direction(float yawDeg, float pitchDeg)
        {
            float yaw = yawDeg * Mathf.Deg2Rad;
            float pitch = pitchDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(pitch);
            return new Vector3(Mathf.Sin(yaw) * c, Mathf.Sin(pitch), Mathf.Cos(yaw) * c).normalized;
        }

        /// <summary>
        /// An indexed sphere, vertex-coloured by elevation. Blue-black at the zenith — the dark itself
        /// is blue now — a midnight band at the horizon, and a cold bias toward the eclipse so the sky
        /// agrees with the key light instead of fighting it. The horizon band is deliberately the
        /// brightest part of the field (~0.25 peak, well under 1.0): it is the backdrop every
        /// combat-range enemy silhouette is read against, and it carries the same linear luminance the
        /// blood band did, so no silhouette got harder to read when the hue moved.
        /// </summary>
        static void BuildDome(List<Vector3> v, List<Color> c, List<int> t, float radius, Vector3 eclipseDir)
        {
            const int longitude = DomeLongitude;
            const int latitude = DomeLatitude;

            // COLD PASS (2026-09-06). Every one of these is the blue at the EXACT Rec.709 linear
            // luminance of the blood it replaces (.0032 / .0170 / .0011 / .0616), so the horizon band is
            // still the same weight of backdrop that every combat-range enemy silhouette is read against.
            // Hue moved; the light level did not.
            Color zenith = Hex("#060A17");    // blue-black overhead: dark, but the dark is blue
            Color horizon = Hex("#13233F");   // midnight band at eye level — the silhouette backdrop
            Color nadir = Hex("#020409");     // below the floor; almost never seen
            Color glow = Hex("#1E4488");      // cold bias added around the eclipse

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

                    // Cold bias toward the eclipse. Wider than the old pow-6 glow: the whole quadrant
                    // of sky around the dead sun is alight, which is what makes the frame read as the
                    // Eclipse rather than a blue night with a spot in it.
                    float toward = Mathf.Max(0f, Vector3.Dot(dir, eclipseDir));
                    col += glow * Mathf.Pow(toward, 4f);

                    // Mesh COLOR has no automatic sRGB decode. Match the fog and lit world instead
                    // of treating the authored midnight palette as linear emission (a bright blue wall).
                    c.Add(PM(RenderingColor(col), 1f));
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
        /// A haze-obscured ring of ruin spires sitting just below the horizon, all the way around.
        /// Drawn nearer than the dome (radius 0.975 against the dome's 1.0) so it silhouettes correctly,
        /// and biased low so it never competes with the eclipse or a mid-air read.
        /// </summary>
        static void BuildHorizonSilhouette(List<Vector3> v, List<Color> c, List<int> t, float radius, System.Random rng)
        {
            const int n = SilhouetteSegments;
            float r = radius * 0.975f;
            Color body = Hex("#020409");     // matches the dome's nadir: no new hue introduced
            Color rimCatch = Hex("#1C365D"); // a thin cold catch-light along the topmost edge only

            int baseIdx = v.Count;
            const float basePitch = -3f;     // a touch below the horizon: grounded, not floating
            var heights = new float[n];
            for (int i = 0; i < n; i++) heights[i] = Lerp(rng, SilhouetteMinDeg, SilhouetteMaxDeg);

            for (int i = 0; i <= n; i++)
            {
                int wi = i % n;
                float yaw = i / (float)n * 360f;
                v.Add(Direction(yaw, basePitch) * r);
                c.Add(PM(body, 0.25f));
                v.Add(Direction(yaw, basePitch + heights[wi]) * r);
                c.Add(PM(Color.Lerp(body, rimCatch, 0.6f), 0.12f));
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
            // Deliberately dim: churning clots of deeper and colder blue, so the field feels like a
            // slowly boiling sky rather than a flat gradient. Each is the luminance-matched cold twin of
            // the blood clot it replaces. Nothing here approaches the corona.
            Color[] palette =
            {
                Hex("#142346"),   // cold clot
                Hex("#0B1531"),   // near-black indigo
                Hex("#113057"),   // steel dust
                Hex("#18193B"),   // bruised violet-blue
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
            // Cold pass: the field is cold again, but it is not a clean night sky — the eclipse is
            // still dying overhead. 70% cold bone, 20% a deeper blue, and a 10% pale-gold minority kept
            // deliberately so the field has temperature variation and does not read as one flat tint.
            // Those gold stars are sub-pixel motes capped at 0.55 brightness: far too small and too dim
            // to be confused with anything warm that matters.
            Color coldWhite = Hex("#DCE6FF");  // cold bone, 70% of the field
            Color blue = Hex("#8FB0EC");       // deeper blue minority
            Color amber = Hex("#FFE2B0");      // a few dying gold points

            // A tilted great circle: a third of the stars cluster near it, giving a galactic band.
            Vector3 bandNormal = new Vector3(0.42f, 0.84f, -0.34f).normalized;
            int bandStars = starCount / 3;

            // Small on purpose: against a mid-tone ground an unlit quad reads as a pale square,
            // not a point, so size does the work brightness used to.
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
                // Capped at 0.55: the stars recede under the cold field — the Eclipse owns this sky.
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
        /// The Eclipse, back to front: a vast cold halo, a brighter mid-glow, the saturated corona
        /// falloff, the black disc — and finally, into the HOT triangle list (submesh 1, HDR material),
        /// the thin white-hot rim drawn OVER the disc's limb. Presence is bought with AREA (the disc is
        /// ~38 degrees across, the halo more than twice that) and CONTRAST (a near-black disc against a
        /// rim at full brightness), never with field intensity: everything coloured here stays LDR and
        /// only the near-white rim crosses the bloom threshold.
        ///
        /// <para><b>The corona went COLD with the world, and that was the load-bearing decision of the
        /// cold pass.</b> A warm corona would have left a huge, static, warm bloom sitting in the sky —
        /// and the amber enemy bolt (peak 1.6) has to fly across that sky and stay the most legible warm
        /// thing in the frame. Warm now means exactly two things in this game: an attack tell, or fire
        /// (a torch, a checkpoint). The backdrop is not allowed to speak either language.</para>
        /// </summary>
        static void BuildEclipse(List<Vector3> v, List<Color> c, List<int> t, List<int> tHot,
                                 float radius, Vector3 dir, float diameterDeg)
        {
            Basis(dir, out Vector3 right, out Vector3 up);
            Vector3 center = dir * radius * 0.96f;

            float discR = radius * Mathf.Tan(diameterDeg * 0.5f * Mathf.Deg2Rad);

            // Vast outer halo: the sky around the dead sun is alight, ~2.3x the disc.
            AddSoftDisc(v, c, t, center + dir * (radius * 0.004f), right * discR * 2.3f, up * discR * 2.3f,
                        RenderingColor(Hex("#17408F")), 0.42f, 32);

            // Mid glow, brighter and tighter.
            AddSoftDisc(v, c, t, center + dir * (radius * 0.008f), right * discR * 1.55f, up * discR * 1.55f,
                        RenderingColor(Hex("#2163B5")), 0.32f, 32);

            // Corona falloff: saturated cold light fading outward. LDR — never blooms.
            AddRing(v, c, t, center + dir * (radius * 0.012f), right, up,
                    discR * 1.05f, discR * 1.42f, Hex("#3F92D1"), 0.9f, 64);

            // Haze grades the background and diffuse corona over a broad angular range. Keep the
            // focal disc and thin burning limb in front, so the wider grade cannot wash them out.
            BuildHorizonAtmosphere(v, c, t, radius);

            // The disc: a dead sun. Not float-zero black — #03050A keeps ~2/255 under it, so an
            // 8-10/255 enemy body overlapping the disc is dim-on-dark rather than a hole in the world.
            AddDisc(v, c, t, center + dir * (radius * 0.016f), right * discR, up * discR, Hex("#03050A"), 64);

            // The burning rim: white-hot at the limb, gone within ~8% of the radius. It starts just
            // INSIDE the disc edge and is drawn after it, so the fire licks over the black limb.
            // Cold bone-white, at the same linear luminance the warm #FFD9A8 carried, so the one sky
            // element allowed to bloom is exactly as bright as it always was — just no longer amber.
            AddRing(v, c, tHot, center + dir * (radius * 0.020f), right, up,
                    discR * 0.995f, discR * 1.075f, Hex("#C9E2FB"), 1f, 96);
        }

        /// <summary>Fan segments around a planet body. 28 is smooth at ~10 degrees of arc and costs 29 verts.</summary>
        const int PlanetBodySegments = 28;
        /// <summary>Segments per ring HALF. Two halves, two bands, so a ringed planet costs ~200 verts.</summary>
        const int PlanetRingSegments = 24;
        /// <summary>Soft outer glow, as a multiple of the body radius. A gradient, never an emissive.</summary>
        const float PlanetHaloScale = 2.1f;
        /// <summary>Alpha at the centre of that glow. Deliberately faint: this is atmosphere, not bloom.</summary>
        const float PlanetHaloAlpha = 0.13f;

        /// <summary>
        /// A handful of distant ringed planets, baked into the same mesh and the same LDR material as
        /// everything else — no extra draw call, no per-planet renderer, nothing for WebGL to choke on.
        ///
        /// <para><b>They glow without being bright.</b> "Glow" here is a soft alpha gradient around the
        /// limb plus a lit-to-shadow terminator across the body, not an emissive: every vertex is capped
        /// at <see cref="PlanetPeakCeiling"/> and lives in submesh 0, whose tint is exactly 1.0, so a
        /// planet physically cannot cross the 1.05 bloom threshold. Nothing in the sky may compete with
        /// an attack tell.</para>
        ///
        /// <para><b>The rings sort themselves, with no depth buffer.</b> The sky writes no depth, so
        /// overlap is resolved purely by INDEX ORDER. Each planet is therefore emitted in five passes in
        /// this exact sequence: halo, then the FAR half of each ring band, then the body, then the NEAR
        /// half of each band. The far half is the part of the ellipse above the centre line, which is
        /// what passes behind the planet from a viewer at the dome's centre; drawing it before the body
        /// lets the opaque body paint over it, and drawing the near half after lets it cross the lit
        /// limb. That is the whole Saturn read, bought with ordering rather than sorting — and it is
        /// stable, because index order is deterministic where alpha sorting is not.</para>
        /// </summary>
        static void BuildPlanets(List<Vector3> v, List<Color> c, List<int> t, float radius, Vector3 eclipseDir)
        {
            foreach (var def in Planets)
            {
                Vector3 dir = Direction(def.yawDeg, def.pitchDeg);
                Basis(dir, out Vector3 right, out Vector3 up);
                Vector3 center = dir * radius * 0.988f;
                float r = radius * Mathf.Tan(def.diameterDeg * 0.5f * Mathf.Deg2Rad);

                // Which way the eclipse lies, projected into the planet's own billboard plane. The
                // terminator then agrees with the only light source in the sky instead of being arbitrary.
                Vector2 lightDir = new Vector2(Vector3.Dot(eclipseDir, right), Vector3.Dot(eclipseDir, up));
                if (lightDir.sqrMagnitude < 1e-6f) lightDir = Vector2.right;
                lightDir.Normalize();

                Color lit = Cap(def.lit);
                Color shadow = Cap(def.shadow);
                Color ring = Cap(def.ringColor);
                bool hasRings = def.ringOuter > def.ringInner && def.ringAlpha > 0f;

                // 1. Halo — a soft gradient, brightest at the limb colour and gone by 2.1x the radius.
                AddSoftDisc(v, c, t, center - dir * (radius * 0.002f),
                            right * r * PlanetHaloScale, up * r * PlanetHaloScale,
                            Color.Lerp(shadow, lit, 0.75f), PlanetHaloAlpha, 20);

                // 2. The far half of each ring band, BEFORE the body, so the body occludes it.
                if (hasRings)
                {
                    AddRingHalf(v, c, t, center, right, up, r * def.ringInner, r * def.ringOuter * 0.86f,
                                def.ringFlatten, ring, def.ringAlpha, true);
                    AddRingHalf(v, c, t, center, right, up, r * def.ringOuter * 0.93f, r * def.ringOuter,
                                def.ringFlatten, ring, def.ringAlpha * 0.6f, true);
                }

                // 3. The body: an opaque fan, lit limb to shadow limb across the terminator.
                AddPlanetBody(v, c, t, center + dir * (radius * 0.001f), right * r, up * r, lit, shadow, lightDir);

                // 4. The near half of each band, AFTER the body, so it crosses the lit face.
                if (hasRings)
                {
                    AddRingHalf(v, c, t, center + dir * (radius * 0.002f), right, up,
                                r * def.ringInner, r * def.ringOuter * 0.86f,
                                def.ringFlatten, ring, def.ringAlpha, false);
                    AddRingHalf(v, c, t, center + dir * (radius * 0.002f), right, up,
                                r * def.ringOuter * 0.93f, r * def.ringOuter,
                                def.ringFlatten, ring, def.ringAlpha * 0.6f, false);
                }
            }
        }

        /// <summary>Clamps every channel to <see cref="PlanetPeakCeiling"/>. Scenery cannot outshine a tell.</summary>
        static Color Cap(Color c)
        {
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (peak <= PlanetPeakCeiling) return c;
            float k = PlanetPeakCeiling / peak;
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }

        /// <summary>
        /// An opaque billboard fan shaded from <paramref name="lit"/> on the limb facing
        /// <paramref name="lightDir2D"/> to <paramref name="shadow"/> on the far limb, with the centre
        /// held at the midpoint. Two vertices' worth of gradient is all it takes to stop a flat circle
        /// reading as a coin: the terminator is the silhouette cue that says "sphere".
        /// </summary>
        static void AddPlanetBody(List<Vector3> v, List<Color> c, List<int> t, Vector3 center,
                                  Vector3 right, Vector3 up, Color lit, Color shadow, Vector2 lightDir2D)
        {
            int centre = v.Count;
            v.Add(center);
            c.Add(PM(Color.Lerp(shadow, lit, 0.45f), 1f));

            for (int s = 0; s < PlanetBodySegments; s++)
            {
                float a = s / (float)PlanetBodySegments * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                v.Add(center + right * ca + up * sa);
                // Half-Lambert: a hard terminator on a body this small reads as a bitten crescent.
                float k = (ca * lightDir2D.x + sa * lightDir2D.y) * 0.5f + 0.5f;
                c.Add(PM(Color.Lerp(shadow, lit, k * k), 1f));
            }
            for (int s = 0; s < PlanetBodySegments; s++)
            {
                int q = centre + 1 + s;
                int w = centre + 1 + (s + 1) % PlanetBodySegments;
                t.Add(centre); t.Add(q); t.Add(w);
            }
        }

        /// <summary>
        /// Half of a flattened annulus in the billboard plane — the Saturn ring. <paramref name="far"/>
        /// selects the half above the centre line, which is the half that passes BEHIND the planet from
        /// the dome's centre. Alpha fades to zero at both radial edges so the band has no hard rim, and
        /// the ends of the half are left square: they meet the other half exactly on the centre line,
        /// where the body's limb is, so the seam is never visible.
        /// </summary>
        static void AddRingHalf(List<Vector3> v, List<Color> c, List<int> t, Vector3 center,
                                Vector3 right, Vector3 up, float innerRadius, float outerRadius,
                                float flatten, Color col, float alpha, bool far)
        {
            int start = v.Count;
            float a0 = far ? 0f : Mathf.PI;
            float mid = (innerRadius + outerRadius) * 0.5f;
            for (int s = 0; s <= PlanetRingSegments; s++)
            {
                float a = a0 + s / (float)PlanetRingSegments * Mathf.PI;
                Vector3 unit = right * Mathf.Cos(a) + up * (Mathf.Sin(a) * flatten);
                v.Add(center + unit * innerRadius);
                c.Add(PM(col, 0f));
                v.Add(center + unit * mid);
                c.Add(PM(col, alpha));
                v.Add(center + unit * outerRadius);
                c.Add(PM(col, 0f));
            }
            for (int s = 0; s < PlanetRingSegments; s++)
            {
                int a = start + s * 3;
                int b = a + 3;
                // inner -> mid strip
                t.Add(a); t.Add(a + 1); t.Add(b);
                t.Add(b); t.Add(a + 1); t.Add(b + 1);
                // mid -> outer strip
                t.Add(a + 1); t.Add(a + 2); t.Add(b + 1);
                t.Add(b + 1); t.Add(a + 2); t.Add(b + 2);
            }
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
