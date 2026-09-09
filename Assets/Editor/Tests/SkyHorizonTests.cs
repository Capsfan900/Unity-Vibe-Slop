using NUnit.Framework;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class SkyHorizonTests
    {
        [Test]
        public void SecondaryPlanetBodiesRingsAndHalosStayAboveTheHorizonBand()
        {
            // Check generated vertices, not just centre pitches: a large tilted ring or halo could
            // cross the horizon even when its body's authoring anchor looks safely overhead.
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color>();
            var triangles = new System.Collections.Generic.List<int>();
            var buildPlanets = typeof(Starfield).GetMethod("BuildPlanets",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(buildPlanets);
            buildPlanets.Invoke(null, new object[] { vertices, colors, triangles, 25f,
                Direction(0f, Starfield.DefaultEclipsePitchDeg) });
            Assert.IsNotEmpty(vertices);
            for (int i = 0; i < vertices.Count; i++)
            {
                float elevation = Mathf.Asin(vertices[i].normalized.y) * Mathf.Rad2Deg;
                Assert.Greater(elevation, 30f,
                    "secondary planet geometry reached the horizon/fighting band at vertex " + i);
            }
        }

        [Test]
        public void LowerAtmosphereClosesEveryHorizonDirectionWithoutAnotherDrawCall()
        {
            Color originalFog = RenderSettings.fogColor;
            var parent = new GameObject("~SkyHorizonTest");
            Mesh mesh = null;
            Material[] materials = null;
            try
            {
                RenderSettings.fogColor = ProjectSetup.FogColor;
                var sky = Starfield.Build(parent.transform);
                mesh = sky.GetComponent<MeshFilter>().sharedMesh;
                materials = sky.GetComponent<MeshRenderer>().sharedMaterials;
                Assert.AreEqual(2, mesh.subMeshCount);
                Assert.AreEqual(2, materials.Length, "the atmosphere shares the existing LDR sky draw");

                Color expected = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? ProjectSetup.FogColor.linear : ProjectSetup.FogColor;
                var vertices = mesh.vertices;
                var colors = mesh.colors;
                var indices = mesh.GetTriangles(0);
                float[] lowerPitches = { -80f, -25f, -10f, -8f };
                // Sample the actual mesh in every direction, including between longitude vertices.
                // Both a bright star and a dark nebula behind fully hazed sea must vanish to the same
                // colour; an alpha-only or RGB-only fade cannot satisfy both backgrounds.
                for (int yaw = 0; yaw < 360; yaw += 5)
                {
                    foreach (float pitch in lowerPitches)
                    {
                        Color atmosphere;
                        Assert.IsTrue(SampleAtmosphere(vertices, colors, indices, Direction(yaw, pitch), out atmosphere),
                            "missing horizon coverage at yaw " + yaw + ", pitch " + pitch);
                        Assert.That(atmosphere.a, Is.EqualTo(1f).Within(0.0001f));
                        Assert.That(atmosphere.r, Is.EqualTo(expected.r).Within(0.0001f));
                        Assert.That(atmosphere.g, Is.EqualTo(expected.g).Within(0.0001f));
                        Assert.That(atmosphere.b, Is.EqualTo(expected.b).Within(0.0001f));
                    }
                    Color middle;
                    Assert.IsTrue(SampleAtmosphere(vertices, colors, indices, Direction(yaw, 0f), out middle));
                    Assert.That(middle.a, Is.InRange(0.75f, 0.85f), "dense eye-level air obscures the distant skyline");
                    Color above;
                    Assert.IsTrue(SampleAtmosphere(vertices, colors, indices, Direction(yaw, 8f), out above));
                    Assert.That(above.a, Is.InRange(0.25f, 0.45f), "the upper sky must emerge through a broad grade");
                    Assert.IsFalse(SampleAtmosphere(vertices, colors, indices, Direction(yaw, 19f), out above),
                        "the atmosphere must clear high sky and secondary planets");
                }
                // The last 64 LDR triangles are the opaque eclipse disc. The atmospheric layer now
                // draws before it, so broadening the upper grade never veils the focal black limb.
                for (int i = indices.Length - 64 * 3; i < indices.Length; i++)
                {
                    Assert.AreEqual(1f, colors[indices[i]].a);
                    Assert.Greater(Mathf.Abs(vertices[indices[i]].magnitude - 25f * 0.94f), 0.001f);
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
                if (mesh != null) Object.DestroyImmediate(mesh);
                if (materials != null)
                    foreach (var material in materials) Object.DestroyImmediate(material);
                RenderSettings.fogColor = originalFog;
            }
        }

        [Test]
        public void AtmosphereGradeIsContinuousAtTheWrapAndBothLatitudeJoins()
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color>();
            var triangles = new System.Collections.Generic.List<int>();
            var build = typeof(Starfield).GetMethod("BuildHorizonAtmosphere",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(build);
            build.Invoke(null, new object[] { vertices, colors, triangles, 25f });
            var v = vertices.ToArray();
            var c = colors.ToArray();
            var t = triangles.ToArray();
            Color fog = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? RenderSettings.fogColor.linear : RenderSettings.fogColor;
            foreach (float yaw in new[] { 0f, 4.5f, 90f, 179f, 270f, 359.99f })
            {
                float previous = 1f;
                for (int step = 0; step <= 260; step++)
                {
                    float pitch = -7.5f + step * 0.1f;
                    Color sample;
                    bool hit = SampleAtmosphere(v, c, t, Direction(yaw, pitch), out sample);
                    if (!hit) sample = new Color(fog.r, fog.g, fog.b, 0f);
                    Assert.LessOrEqual(sample.a, previous + 0.001f, "grade must fade monotonically");
                    Assert.LessOrEqual(previous - sample.a, 0.007f,
                        "a latitude join must not create a narrow horizontal stripe");
                    Assert.That(sample.r, Is.EqualTo(fog.r).Within(0.0001f));
                    Assert.That(sample.g, Is.EqualTo(fog.g).Within(0.0001f));
                    Assert.That(sample.b, Is.EqualTo(fog.b).Within(0.0001f));
                    previous = sample.a;
                }
            }
        }

        [TestCase(-5f)]
        [TestCase(-3f)]
        [TestCase(0f)]
        public void PartialAtmosphereOverMatchingFogDoesNotDarkenTheSeam(float pitch)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Sky compositing requires a graphics device.");
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color>();
            var triangles = new System.Collections.Generic.List<int>();
            Color oldFog = RenderSettings.fogColor;
            try
            {
                RenderSettings.fogColor = ProjectSetup.FogColor;
                typeof(Starfield).GetMethod("BuildHorizonAtmosphere",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    .Invoke(null, new object[] { vertices, colors, triangles, 25f });
            }
            finally { RenderSettings.fogColor = oldFog; }
            Color sample;
            Assert.IsTrue(SampleAtmosphere(vertices.ToArray(), colors.ToArray(), triangles.ToArray(),
                Direction(4.5f, pitch), out sample));
            Color fog = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? ProjectSetup.FogColor.linear : ProjectSetup.FogColor;
            Color pixel = RenderSpriteProbe(sample, fog);
            Assert.That(pixel.r, Is.EqualTo(fog.r).Within(0.0005f));
            Assert.That(pixel.g, Is.EqualTo(fog.g).Within(0.0005f));
            Assert.That(pixel.b, Is.EqualTo(fog.b).Within(0.0005f),
                "the real sprite shader must premultiply only once; double alpha makes a dark seam");
        }

        static Color RenderSpriteProbe(Color color, Color background)
        {
            var material = new Material(Shader.Find("Sprites/Default"));
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
            mesh.colors = new[] { color, color, color, color };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            var target = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            var readback = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
            var oldTarget = RenderTexture.active;
            bool oldSrgb = GL.sRGBWrite;
            try
            {
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                material.SetVector("_Color", Vector4.one);
                material.SetVector("_RendererColor", Vector4.one);
                material.SetVector("_Flip", Vector4.one);
                RenderTexture.active = target;
                GL.sRGBWrite = false;
                GL.Clear(false, true, background);
                GL.PushMatrix();
                try
                {
                    GL.LoadOrtho();
                    Assert.IsTrue(material.SetPass(0));
                    Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                }
                finally { GL.PopMatrix(); }
                readback.ReadPixels(new Rect(0, 0, 8, 8), 0, 0, false);
                readback.Apply(false);
                return readback.GetPixel(4, 4);
            }
            finally
            {
                GL.sRGBWrite = oldSrgb;
                RenderTexture.active = oldTarget;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
            }
        }

        static Vector3 Direction(float yaw, float pitch)
        {
            return Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
        }

        static bool SampleAtmosphere(Vector3[] vertices, Color[] colors, int[] indices,
                                     Vector3 ray, out Color color)
        {
            // The atmosphere is the layer at 94% of the 25 m sky radius. The eclipse disc draws after it.
            const float atmosphereRadius = 25f * 0.94f;
            for (int i = indices.Length - 3; i >= 0; i -= 3)
            {
                int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                var a = vertices[ia];
                var b = vertices[ib];
                var c = vertices[ic];
                if (Mathf.Abs(a.magnitude - atmosphereRadius) > 0.001f ||
                    Mathf.Abs(b.magnitude - atmosphereRadius) > 0.001f ||
                    Mathf.Abs(c.magnitude - atmosphereRadius) > 0.001f) continue;
                var ab = b - a;
                var ac = c - a;
                var p = Vector3.Cross(ray, ac);
                float determinant = Vector3.Dot(ab, p);
                if (Mathf.Abs(determinant) < 0.00001f) continue;
                float u = Vector3.Dot(-a, p) / determinant;
                var q = Vector3.Cross(-a, ab);
                float v = Vector3.Dot(ray, q) / determinant;
                float distance = Vector3.Dot(ac, q) / determinant;
                if (u < -0.00001f || v < -0.00001f || u + v > 1.00001f || distance <= 0f) continue;
                color = colors[ia] * (1f - u - v) + colors[ib] * u + colors[ic] * v;
                return true;
            }
            color = Color.clear;
            return false;
        }
    }
}
