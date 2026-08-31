using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Procedural deep-space sky: gradient dome, nebulae, a galactic band, a star field and the eclipse,
    /// all baked into ONE mesh with ONE material — one draw call for the entire sky.
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
    /// Draw order inside the single mesh is index order (nothing depth-sorts with ZWrite off), so the
    /// index buffer is written back-to-front: dome, nebulae, stars, corona, then the black eclipse disc
    /// last. That is what puts stars *behind* the disc rather than in front of it.
    ///
    /// The mesh is on its own layer so the NavMeshSurface (which bakes from RenderMeshes on layer 0)
    /// cannot collect a 25-unit sphere as walkable geometry.
    /// </summary>
    public static class Starfield
    {
        /// <summary>Layer for sky geometry. Excluded from the NavMesh bake, queried by nothing.</summary>
        public const int SkyLayer = 9;
        public const string SkyLayerName = "Sky";

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
        /// <param name="includeEclipse">The Berserk-style eclipse, drawn last so it silhouettes the stars.</param>
        /// <param name="eclipseYawDeg">0 = +Z, the direction the course runs and the boss arena sits.</param>
        /// <param name="eclipsePitchDeg">Elevation above the horizon. Low, matching the ember key light.</param>
        /// <param name="eclipseDiameterDeg">Angular diameter. Large: it is the level's focal image.</param>
        public static GameObject Build(
            Transform parent,
            int starCount = 1200,
            float radius = 25f,
            int seed = 20260830,
            bool includeEclipse = true,
            float eclipseYawDeg = 0f,
            float eclipsePitchDeg = 13f,
            float eclipseDiameterDeg = 19f)
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
            if (includeEclipse)
                BuildEclipse(verts, colors, tris, radius, eclipseDir, eclipseDiameterDeg);

            var mesh = new Mesh { name = "SkyMesh" };
            // 5-6k verts fits UInt16 comfortably; being explicit documents the budget.
            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0, false);   // false: do NOT recalculate bounds yet
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateSkyMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;

            root.AddComponent<SkyFollower>();

            return root;
        }

        static Material CreateSkyMaterial()
        {
            var shader = Shader.Find(PreferredShader);
            if (shader == null)
            {
                shader = Shader.Find(FallbackShader);
                Debug.LogWarning($"[Starfield] '{PreferredShader}' not found; falling back to '{FallbackShader}'. " +
                                 "That shader ignores vertex colours, so the sky will render as a flat tint.");
            }

            var mat = new Material(shader) { name = "M_Sky (runtime)" };
            // Background queue is the whole trick: drawn before opaque geometry, writing no depth.
            mat.renderQueue = BackgroundQueue;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
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
        /// An indexed sphere, vertex-coloured by elevation. Deep blue-black at the zenith, a violet
        /// haze at the horizon, and an ember bias toward the eclipse so the sky agrees with the key
        /// light instead of fighting it.
        /// </summary>
        static void BuildDome(List<Vector3> v, List<Color> c, List<int> t, float radius, Vector3 eclipseDir)
        {
            const int longitude = 40;
            const int latitude = 20;

            Color zenith = Hex("#050610");    // deep space overhead
            Color horizon = Hex("#151030");   // violet haze where the sky meets the level
            Color nadir = Hex("#03030A");     // below the floor; almost never seen
            Color ember = Hex("#4E2110");     // warm bias added around the eclipse only

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

                    // Warm bias toward the eclipse, tight enough that it reads as a glow around it and
                    // not as a second light source.
                    float toward = Mathf.Max(0f, Vector3.Dot(dir, eclipseDir));
                    col += ember * Mathf.Pow(toward, 6f);

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

        static void BuildNebulae(List<Vector3> v, List<Color> c, List<int> t, float radius, System.Random rng)
        {
            // Deliberately desaturated and dim. A bright nebula would compete with the eclipse and with
            // the ember accent that marks ledges.
            Color[] palette =
            {
                Hex("#2A1840"),   // deep violet
                Hex("#12283A"),   // cold slate blue
                Hex("#3A1A18"),   // dull ember
                Hex("#16303A"),   // faint teal
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
            // Cold whites dominate, with a minority of blues and a few ambers so the field has
            // temperature variation rather than looking like grey noise.
            Color coldWhite = Hex("#DDE6FF");
            Color blue = Hex("#9FC0FF");
            Color amber = Hex("#FFCE96");

            // A tilted great circle: a third of the stars cluster near it, giving a galactic band.
            Vector3 bandNormal = new Vector3(0.42f, 0.84f, -0.34f).normalized;
            int bandStars = starCount / 3;

            float sizeMin = radius * 0.0022f;
            float sizeMax = radius * 0.0072f;

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
                float brightness = Mathf.Lerp(0.30f, 1f, brightRoll * brightRoll);
                float size = Mathf.Lerp(sizeMin, sizeMax, brightRoll * brightRoll);
                if (brightRoll > 0.985f) size *= 1.9f;   // a handful of standouts

                Basis(dir, out Vector3 right, out Vector3 up);
                // Quads face the sphere centre, which is where the camera sits, so they are camera-facing
                // without any per-frame billboarding work.
                AddQuad(v, c, t, dir * radius * 0.99f, right * size, up * size, PM(tint * brightness, 1f));
            }
        }

        /// <summary>
        /// Corona then disc. The corona is a ring that is brightest at its inner edge and fades outward;
        /// the disc is pure black and drawn last, so only a burning rim survives.
        /// </summary>
        static void BuildEclipse(List<Vector3> v, List<Color> c, List<int> t, float radius, Vector3 dir, float diameterDeg)
        {
            Basis(dir, out Vector3 right, out Vector3 up);
            Vector3 center = dir * radius * 0.96f;

            float discR = radius * Mathf.Tan(diameterDeg * 0.5f * Mathf.Deg2Rad);

            // Outer bloom: a wide, dim halo so the eclipse sits in a glow rather than being pasted on.
            AddSoftDisc(v, c, t, center + dir * (radius * 0.005f), right * discR * 2.6f, up * discR * 2.6f,
                        Hex("#7A2408"), 0.26f, 28);

            // The burning rim itself.
            AddRing(v, c, t, center + dir * (radius * 0.01f), right, up,
                    discR * 1.005f, discR * 1.34f, Hex("#FF6A18"), 1f, 64);

            // The disc: pure black, drawn last, eating the centre of the corona.
            AddDisc(v, c, t, center + dir * (radius * 0.015f), right * discR, up * discR, Color.black, 48);
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
