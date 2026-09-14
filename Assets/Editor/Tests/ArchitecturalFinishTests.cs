using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.Tests
{
    /// <summary>Visual finish must survive regeneration without buying detail with gameplay geometry.</summary>
    public class ArchitecturalFinishTests
    {
        static Material Shipped(string key)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_" + key + ".mat");
            Assert.IsNotNull(material, key + ": run Create Materials before this suite");
            return material;
        }

        [TestCase("VibeGame1/Architectural Stone")]
        [TestCase("VibeGame1/Solar Arena")]
        [TestCase("VibeGame1/Cloud Sea")]
        public void FinishShadersCompileWithoutErrors(string name)
        {
            var shader = Shader.Find(name);
            Assert.IsNotNull(shader, name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), name + ": inspect shader compiler messages");
        }

        [TestCase("Ground")]
        [TestCase("Stone")]
        [TestCase("Platform")]
        public void StructuralFinishShipsMetreScaledReliefAndAllDepthPasses(string key)
        {
            var material = Shipped(key);
            Assert.AreEqual("VibeGame1/Architectural Stone", material.shader.name);
            Assert.AreEqual("UniversalPipeline", material.GetTag("RenderPipeline", false));
            Assert.AreEqual(2000, material.renderQueue);
            foreach (string property in new[] { "_BaseColor", "_EmissionColor", "_Smoothness", "_Metallic" })
                Assert.IsTrue(material.HasProperty(property), key + " property-block compatibility: " + property);
            foreach (string pass in new[] { "ArchitecturalForward", "ShadowCaster", "DepthOnly", "DepthNormals" })
                Assert.GreaterOrEqual(material.FindPass(pass), 0, key + " missing " + pass);
            Assert.AreEqual(new Vector4(2.8f, 1.4f, 0.7f, 0f), material.GetVector("_BlockSize"));
            Assert.AreEqual(0.018f, material.GetFloat("_JointWidth"), 0.0001f);
            Assert.That(material.GetFloat("_ReliefDepth"), Is.GreaterThan(0f).And.LessThanOrEqualTo(0.01f));
            Assert.That(material.GetFloat("_Smoothness"), Is.InRange(0.15f, 0.3f));
            Assert.AreEqual(0f, material.GetFloat("_Metallic"));
            var emission = material.GetColor("_EmissionColor");
            Assert.LessOrEqual(Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b)), 0.04f,
                "stone detail must not introduce decorative glow");
        }

        [Test]
        public void StructuralPaletteKeepsShadowedMasonryAboveTheMeasuredReadabilityFloor()
        {
            AssertColor(Parse("#36404F"), Shipped("Ground").GetColor("_BaseColor"));
            AssertColor(Parse("#424D5F"), Shipped("Stone").GetColor("_BaseColor"));
            AssertColor(Parse("#586579"), Shipped("Platform").GetColor("_BaseColor"));
            AssertColor(Parse("#475262") * 0.10f, Shipped("Platform").GetColor("_EmissionColor"));

            // Evaluated from the shipped Trilight environment in Level_01. The old M_Ground produced
            // only .0029 before mortar/occlusion; this channel-wise product must leave real shadow detail.
            Color probe = new Color(0.078f, 0.180f, 0.413f, 1f);
            Color albedo = Shipped("Ground").GetColor("_BaseColor").linear;
            Color diffuse = new Color(albedo.r * probe.r, albedo.g * probe.g, albedo.b * probe.b, 1f);
            float luminance = diffuse.r * 0.2126f + diffuse.g * 0.7152f + diffuse.b * 0.0722f;
            Assert.Greater(luminance, 0.008f,
                "ambient x ground albedo is still a multiplicative near-zero before grading");
        }

        static Color Parse(string html)
        {
            Color color;
            Assert.IsTrue(ColorUtility.TryParseHtmlString(html, out color), html);
            return color;
        }

        static void AssertColor(Color expected, Color actual)
        {
            const float serializedTolerance = 0.00001f;
            Assert.AreEqual(expected.r, actual.r, serializedTolerance, "red");
            Assert.AreEqual(expected.g, actual.g, serializedTolerance, "green");
            Assert.AreEqual(expected.b, actual.b, serializedTolerance, "blue");
            Assert.AreEqual(expected.a, actual.a, serializedTolerance, "alpha");
        }

        [TestCase("SolarCyan")]
        [TestCase("SolarGold")]
        [TestCase("SolarAzure")]
        [TestCase("SolarGhost")]
        [TestCase("SolarViolet")]
        [TestCase("SolarCorona")]
        public void PlasmaDetailPreservesCrossingAndOpacityContracts(string key)
        {
            var material = Shipped(key);
            Assert.AreEqual("VibeGame1/Solar Arena", material.shader.name);
            Assert.AreEqual(1f, material.GetFloat("_Fade"), 0.0001f);
            bool corona = key == "SolarCorona";
            Assert.AreEqual(corona ? 0f : 0.92f, material.GetFloat("_SurfaceOpacity"), 0.0001f,
                "corona stays additive and the exterior membrane occludes the route");
            Assert.AreEqual(corona ? 0.25f : 0.65f, material.GetFloat("_DetailStrength"), 0.0001f);
            Assert.AreEqual(corona ? 0.08f : 0.28f, material.GetFloat("_FilamentStrength"), 0.0001f);
            Assert.AreEqual(corona ? 0.70f : 0.35f, material.GetFloat("_RimStrength"), 0.0001f);
        }

        [TestCase(1f, 1f, 0.92f)]
        [TestCase(0.35f, 1f, 0.92f)]
        [TestCase(0.175f, 1f, 0.46f)]
        [TestCase(0f, 1f, 0f)]
        [TestCase(1f, 0f, 0f)]
        public void SolarFogScalesRenderedOcclusionAndCrossingTogether(float transmission, float crossing, float expectedAlpha)
        {
            Color pixel = RenderSolarFogProbe(0.92f, transmission, crossing, true);
            Assert.AreEqual(expectedAlpha, pixel.a, 0.015f,
                "opaque exterior must release its background smoothly as fog becomes complete");
            if (expectedAlpha == 0f) AssertProbeBackground(pixel);
        }

        [Test]
        public void SolarCoronaLeavesNoAdditiveGhostAtFullFog()
        {
            AssertProbeBackground(RenderSolarFogProbe(0f, 0f, 1f, true));
            var clear = RenderSolarFogProbe(0f, 1f, 1f, true);
            Assert.Greater(clear.r, 0.42f, "the probe must actually draw the near corona");
            Assert.AreEqual(0f, clear.a, 0.01f, "corona retains additive alpha");
        }

        [Test]
        public void DisablingFogDoesNotHideTheSolarSurface()
        {
            var pixel = RenderSolarFogProbe(0.92f, 0f, 1f, false);
            Assert.AreEqual(0.92f, pixel.a, 0.015f,
                "URP ComputeFogIntensity returns zero without fog; that must not hide the sphere");
        }

        static void AssertProbeBackground(Color pixel)
        {
            Assert.AreEqual(0.4f, pixel.r, 0.015f);
            Assert.AreEqual(0.5f, pixel.g, 0.015f);
            Assert.AreEqual(0.6f, pixel.b, 0.015f);
            Assert.AreEqual(0f, pixel.a, 0.015f);
        }

        [TestCase(0f)]
        [TestCase(1f)]
        public void FullCloudHazeMatchesSkyFogOverDarkAndBrightScenery(float backgroundValue)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Cloud compositing requires a graphics device.");
            var material = new Material(Shipped("CloudSea"));
            var target = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            var readback = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true);
            var oldTarget = RenderTexture.active;
            var oldCamera = Shader.GetGlobalVector("_WorldSpaceCameraPos");
            var oldFog = Shader.GetGlobalColor("unity_FogColor");
            bool oldSrgb = GL.sRGBWrite;
            Color fog = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? EditorTools.ProjectSetup.FogColor.linear : EditorTools.ProjectSetup.FogColor;
            try
            {
                // Blit geometry is near the origin; put the eye beyond the SHIPPED haze end.
                Shader.SetGlobalVector("_WorldSpaceCameraPos",
                    new Vector4(0f, 0f, -material.GetFloat("_HazeEnd") - 20f, 1f));
                Shader.SetGlobalVector("unity_FogColor", (Vector4)fog);
                material.SetFloat("_WaveHeight", 0f);
                RenderTexture.active = target;
                GL.sRGBWrite = false;
                GL.Clear(false, true, new Color(backgroundValue, backgroundValue, backgroundValue, 1f));
                Graphics.Blit(Texture2D.whiteTexture, target, material, 0);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 16, 16), 0, 0, false);
                readback.Apply(false);
                Color pixel = readback.GetPixel(8, 8);
                Assert.That(pixel.r, Is.EqualTo(fog.r).Within(0.0005f));
                Assert.That(pixel.g, Is.EqualTo(fog.g).Within(0.0005f));
                Assert.That(pixel.b, Is.EqualTo(fog.b).Within(0.0005f));
                Assert.That(pixel.a, Is.EqualTo(1f).Within(0.001f),
                    "the clipped sea must converge to opaque matching fog, independent of stars behind it");
            }
            finally
            {
                Shader.SetGlobalVector("_WorldSpaceCameraPos", oldCamera);
                Shader.SetGlobalColor("unity_FogColor", oldFog);
                GL.sRGBWrite = oldSrgb;
                RenderTexture.active = oldTarget;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(material);
            }
        }

        // Render the actual shader into a tiny linear target. A zero depth coefficient makes the
        // linear fog transmittance uniform, so this tests compositing without a scene or camera.
        // All global fog state is restored; this fixture is run by the lead in EditMode.
        static Color RenderSolarFogProbe(float surface, float transmission, float crossing, bool fogEnabled)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Solar compositing requires a graphics device.");
            var material = new Material(Shader.Find("VibeGame1/Solar Arena"));
            var target = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            var readback = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true);
            var oldTarget = RenderTexture.active;
            var oldFog = Shader.GetGlobalVector("unity_FogParams");
            var oldFogColor = Shader.GetGlobalColor("unity_FogColor");
            bool oldSrgbWrite = GL.sRGBWrite;
            string[] keywords = { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" };
            var oldKeywords = new bool[keywords.Length];
            for (int i = 0; i < keywords.Length; i++)
                oldKeywords[i] = Shader.IsKeywordEnabled(keywords[i]);
            try
            {
                foreach (string keyword in keywords) Shader.DisableKeyword(keyword);
                if (fogEnabled)
                {
                    Shader.EnableKeyword("FOG_LINEAR");
                    material.EnableKeyword("FOG_LINEAR");
                }
                Shader.SetGlobalVector("unity_FogParams", new Vector4(0f, 0f, 0f, transmission));
                Shader.SetGlobalColor("unity_FogColor", new Color(0.02f, 0.03f, 0.04f, 1f));
                material.SetFloat("_SurfaceOpacity", surface);
                material.SetFloat("_Fade", crossing);
                material.SetFloat("_Alpha", 0.5f);
                material.SetFloat("_Pulse", 0f);
                material.SetFloat("_DetailStrength", 0f);
                material.SetFloat("_FilamentStrength", 0f);
                material.SetFloat("_RimStrength", 0f);
                material.SetColor("_CoreColor", Color.white * 0.4f);
                material.SetColor("_BandColor", Color.white * 0.4f);
                RenderTexture.active = target;
                GL.sRGBWrite = false;
                GL.Clear(false, true, new Color(0.4f, 0.5f, 0.6f, 0f));
                Graphics.Blit(Texture2D.blackTexture, target, material, 0);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 16, 16), 0, 0, false);
                readback.Apply(false);
                return readback.GetPixel(8, 8);
            }
            finally
            {
                Shader.SetGlobalVector("unity_FogParams", oldFog);
                Shader.SetGlobalColor("unity_FogColor", oldFogColor);
                for (int i = 0; i < keywords.Length; i++)
                {
                    if (oldKeywords[i]) Shader.EnableKeyword(keywords[i]);
                    else Shader.DisableKeyword(keywords[i]);
                }
                GL.sRGBWrite = oldSrgbWrite;
                RenderTexture.active = oldTarget;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void FinishedPlatformKeepsItsExactCollisionBoundsAndRendererBudget()
        {
            var root = new GameObject("~ArchitecturalFinishTest");
            try
            {
                var definition = new PlatformDef
                {
                    name = "FinishedSlab", center = new Vector3(3f, 6f, 9f),
                    size = new Vector3(12f, 2f, 8f), materialKey = "Platform",
                    trim = true, trimMaterialKey = "NeonCyan", isStatic = false
                };
                var context = new LevelPieceContext { materials = Shipped };
                var piece = LevelPieceFactory.Platform(definition, root.transform, context, null, 0);
                Physics.SyncTransforms();
                var collider = piece.GetComponent<BoxCollider>();
                Assert.IsNotNull(collider);
                Assert.AreEqual(definition.center, collider.bounds.center);
                Assert.AreEqual(definition.size, collider.bounds.size);
                Assert.AreEqual(1, piece.GetComponentsInChildren<Collider>(true).Length,
                    "decorative details must not obstruct movement or the NavMesh");
                Assert.AreEqual(5, piece.GetComponentsInChildren<Renderer>(true).Length,
                    "one slab plus its original four navigation trims, no decorative draw calls");
                Assert.AreEqual(24, piece.GetComponent<MeshFilter>().sharedMesh.vertexCount,
                    "the finish must retain Unity's original cube mesh");
                Assert.AreEqual(new Bounds(Vector3.zero, Vector3.one),
                    piece.GetComponent<MeshFilter>().sharedMesh.bounds);
                Assert.IsEmpty(piece.GetComponentsInChildren<Light>(true));
                foreach (Transform child in piece.transform)
                {
                    Assert.IsNull(child.GetComponent<Collider>());
                    Assert.AreSame(Shipped("NeonCyan"), child.GetComponent<Renderer>().sharedMaterial,
                        "finish must not recolour the navigation edge");
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void FinishedRampRetainsOneUndisplacedRotatedCube()
        {
            var root = new GameObject("~ArchitecturalRampTest");
            try
            {
                var definition = new RampDef
                {
                    name = "FinishedRamp", basePosition = new Vector3(1f, 2f, 3f),
                    width = 5f, run = 8f, rise = 2f, thickness = 0.7f, yaw = 35f,
                    materialKey = "Stone", isStatic = false
                };
                var piece = LevelPieceFactory.Ramp(definition, root.transform,
                    new LevelPieceContext { materials = Shipped }, null, 0);
                Assert.AreEqual(definition.BoxCenter, piece.transform.position);
                Assert.AreEqual(definition.BoxScale, piece.transform.localScale);
                Assert.Less(Quaternion.Angle(definition.Rotation, piece.transform.rotation), 0.001f);
                Assert.AreEqual(1, piece.GetComponentsInChildren<Renderer>(true).Length);
                Assert.AreEqual(1, piece.GetComponentsInChildren<Collider>(true).Length);
                Assert.AreEqual(Vector3.one, piece.GetComponent<BoxCollider>().size);
                Assert.AreEqual(24, piece.GetComponent<MeshFilter>().sharedMesh.vertexCount);
                Assert.AreEqual(0, piece.transform.childCount);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
