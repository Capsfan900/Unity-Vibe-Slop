using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1.Tests
{
    /// <summary>Pins the cloud ocean's world coverage, geometry cost and presentation-only contract.</summary>
    public class CloudSeaTests
    {
        const string MaterialPath = "Assets/Materials/M_CloudSea.mat";
        const string LevelPath = "Assets/Data/Levels/Level_01_Level.asset";

        [Test]
        public void CampaignSeaIsOneFixedWorldSurfaceBelowTheWholeRoute()
        {
            var parent = new GameObject("~CloudSeaTest");
            try
            {
                var sea = CloudSea.BuildCampaign(parent.transform, null);
                Assert.AreEqual(parent.transform, sea.transform.parent);
                Assert.AreEqual(new Vector3(0f, CloudSea.CampaignBaseY, CloudSea.CampaignCenterZ),
                    sea.transform.localPosition);
                Assert.AreEqual(Starfield.SkyLayer, sea.gameObject.layer,
                    "cloud scenery must stay out of the Default-only NavMesh bake");

                var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelPath);
                Assert.IsNotNull(level);
                Assert.IsNotEmpty(level.platforms);
                float routeMinX = float.MaxValue, routeMaxX = float.MinValue;
                float routeMinZ = float.MaxValue, routeMaxZ = float.MinValue;
                foreach (var platform in level.platforms)
                {
                    routeMinX = Mathf.Min(routeMinX, platform.center.x - platform.size.x * 0.5f);
                    routeMaxX = Mathf.Max(routeMaxX, platform.center.x + platform.size.x * 0.5f);
                    routeMinZ = Mathf.Min(routeMinZ, platform.center.z - platform.size.z * 0.5f);
                    routeMaxZ = Mathf.Max(routeMaxZ, platform.center.z + platform.size.z * 0.5f);
                }
                float minX = sea.transform.localPosition.x - sea.surfaceSize.x * 0.5f;
                float maxX = sea.transform.localPosition.x + sea.surfaceSize.x * 0.5f;
                float minZ = sea.transform.localPosition.z - sea.surfaceSize.y * 0.5f;
                float maxZ = sea.transform.localPosition.z + sea.surfaceSize.y * 0.5f;
                Assert.LessOrEqual(minX, routeMinX - 80f);
                Assert.GreaterOrEqual(maxX, routeMaxX + 80f);
                Assert.LessOrEqual(minZ, routeMinZ - 80f);
                Assert.GreaterOrEqual(maxZ, routeMaxZ + 80f);

                Assert.IsNull(sea.GetComponent<ParticleSystem>(), "the shared ocean is not a field of local puffs");
                Assert.IsNull(sea.GetComponent<Collider>(), "lower atmosphere must never become traversal geometry");
                Assert.IsNull(sea.GetComponent<Light>(), "scenery does not spend the local light budget");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void GeneratedGridIsOneWebGlBoundedRenderer()
        {
            var parent = new GameObject("~CloudSeaGridTest");
            try
            {
                var sea = CloudSea.BuildCampaign(parent.transform, null);
                var mesh = sea.GeneratedMesh;
                var renderer = sea.Renderer;
                Assert.IsNotNull(mesh);
                Assert.AreEqual((CloudSea.CampaignXSegments + 1) * (CloudSea.CampaignZSegments + 1), mesh.vertexCount);
                Assert.AreEqual(CloudSea.CampaignXSegments * CloudSea.CampaignZSegments * 6,
                    (int)mesh.GetIndexCount(0));
                Assert.AreEqual(1, mesh.subMeshCount);
                Assert.AreEqual(1, sea.GetComponentsInChildren<MeshRenderer>(true).Length);
                Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode);
                Assert.IsFalse(renderer.receiveShadows);
                Assert.AreEqual(LightProbeUsage.Off, renderer.lightProbeUsage);
                Assert.AreEqual(ReflectionProbeUsage.Off, renderer.reflectionProbeUsage);
                Assert.AreEqual(MotionVectorGenerationMode.ForceNoMotion, renderer.motionVectorGenerationMode);
                Assert.GreaterOrEqual(mesh.bounds.extents.y, sea.verticalBounds,
                    "shader displacement must stay inside renderer bounds at wave peaks");

                sea.enabled = false;
                Assert.IsNull(sea.GeneratedMesh, "disabling the ExecuteAlways preview must release its transient mesh");
                sea.enabled = true;
                Assert.IsNotNull(sea.GeneratedMesh, "re-enabling must regenerate the one surface");
                Assert.AreEqual(1, sea.GetComponentsInChildren<MeshRenderer>(true).Length,
                    "preview lifecycle must never accumulate renderers");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ShippedMaterialMakesBillowsAndWispsWithoutBlooming()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.IsNotNull(material, "run VibeGame1/2. Create Materials before testing the cloud sea");
            Assert.IsNotNull(material.shader);
            Assert.AreEqual("VibeGame1/Cloud Sea", material.shader.name);
            Assert.AreEqual((int)RenderQueue.Transparent - 10, material.renderQueue);

            Color deep = material.GetColor("_DeepColor");
            Color body = material.GetColor("_CloudColor");
            Color crest = material.GetColor("_CrestColor");
            Assert.LessOrEqual(Mathf.Max(deep.maxColorComponent,
                Mathf.Max(body.maxColorComponent, crest.maxColorComponent)), 1f,
                "ambient cloud scenery must stay under the 1.05 attack-tell bloom threshold");
            Assert.Greater(crest.grayscale, body.grayscale,
                "wave crests need a value change so rolling billows read from the route above");
            Assert.Greater(body.grayscale, deep.grayscale);

            float largeScale = material.GetFloat("_LargeScale");
            float detailScale = material.GetFloat("_DetailScale");
            Assert.Greater(detailScale, largeScale * 3f,
                "wisps need a separate smaller scale instead of one stretched noise field");
            Assert.Greater(material.GetFloat("_WarpStrength"), 10f,
                "large billows need visible domain curl rather than straight scrolling bands");
            Assert.That(material.GetFloat("_FlowSpeed"), Is.InRange(0.02f, 0.08f),
                "the ocean should roll past slowly enough to feel massive");
            Assert.That(material.GetFloat("_DetailStrength"), Is.InRange(0.18f, 0.35f),
                "fine flow should erode soft edges; a stronger narrow field turns into bright contour rings");
        }

        [Test]
        public void HighestPossibleSwellStaysBelowEveryStructure()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.IsNotNull(material, "run VibeGame1/2. Create Materials before testing the cloud sea");
            var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelPath);
            Assert.IsNotNull(level);
            float lowestUnderside = float.MaxValue;
            foreach (var platform in level.platforms)
                lowestUnderside = Mathf.Min(lowestUnderside, platform.center.y - platform.size.y * 0.5f);
            float highest = CloudSea.CampaignBaseY +
                            material.GetFloat("_WaveHeight") * CloudSea.MaximumWaveCoefficient;
            Assert.LessOrEqual(highest, lowestUnderside - 1f,
                "cloud crests need at least one metre of visible air below the lowest shipped structure");
        }

        [Test]
        public void SandboxProfileCoversTheMovementYardWithoutChangingTheCampaignMeshBudget()
        {
            float minX = CloudSea.SandboxCenterX - CloudSea.SandboxWidth * 0.5f;
            float maxX = CloudSea.SandboxCenterX + CloudSea.SandboxWidth * 0.5f;
            Assert.LessOrEqual(minX, -30f - 50f);
            Assert.GreaterOrEqual(maxX, 152f + 50f);
            Assert.GreaterOrEqual(CloudSea.SandboxLength * 0.5f, 30f + 50f);
            Assert.Less((CloudSea.SandboxXSegments + 1) * (CloudSea.SandboxZSegments + 1), 4000);
        }
    }
}
