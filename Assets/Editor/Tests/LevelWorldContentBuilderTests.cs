using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The reusable world-content seam is deliberately narrower than the destructive campaign build.
    /// These tests protect the boundary Level Studio preview will use: content is constructed below the
    /// supplied root, while scene cleanup, saves, NavMesh and gameplay bootstrap stay with Build().
    /// </summary>
    public class LevelWorldContentBuilderTests
    {
        GameObject root;
        LevelDefinition definition;
        GameObject enemyPrefab;
        GameObject checkpointPrefab;
        GameObject pickupPrefab;
        GameObject balloonPrefab;
        ItemData testItem;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("ExplicitPreviewRoot");
            definition = ScriptableObject.CreateInstance<LevelDefinition>();
            definition.platforms = new[]
            {
                new PlatformDef { name = "PreviewPlatform", center = new Vector3(2f, 1f, 3f), size = new Vector3(6f, 1f, 8f) }
            };
            definition.pedestals = new[]
            {
                new PedestalDef { name = "PreviewAltar", groundPosition = new Vector3(2f, 2f, 3f), triggerRadius = 2f }
            };
            enemyPrefab = new GameObject("PreviewEnemyPrefab");
            checkpointPrefab = new GameObject("PreviewCheckpointPrefab");
            checkpointPrefab.AddComponent<BoxCollider>();
            var checkpoint = checkpointPrefab.AddComponent<Checkpoint>();
            checkpoint.spawnPoint = new GameObject("SpawnPoint").transform;
            checkpoint.spawnPoint.SetParent(checkpointPrefab.transform, false);
            pickupPrefab = new GameObject("PreviewPickupPrefab");
            pickupPrefab.AddComponent<BoxCollider>();
            pickupPrefab.AddComponent<ItemPickup>();
            balloonPrefab = new GameObject("PreviewBalloonPrefab");
            balloonPrefab.AddComponent<SphereCollider>();
            balloonPrefab.AddComponent<Balloon>();
            testItem = ScriptableObject.CreateInstance<ItemData>();
            definition.spawns = new[]
            {
                new SpawnDef { name = "PreviewSpawner", prefabKey = "PreviewEnemy" }
            };
            definition.checkpoints = new[]
            {
                new CheckpointDef { name = "PreviewCheckpoint", position = new Vector3(1f, 1f, 1f) }
            };
            definition.pickups = new[]
            {
                new PickupDef { name = "PreviewPickup", itemKey = "PreviewItem", position = new Vector3(2f, 2f, 2f) }
            };
            definition.balloons = new[]
            {
                new BalloonDef { name = "PreviewBalloon", position = new Vector3(3f, 3f, 3f) }
            };
            definition.waters = new[]
            {
                new WaterDef { name = "PreviewWater", center = new Vector3(4f, 0f, 4f), size = new Vector3(4f, 0.1f, 4f) }
            };
            definition.torches = new[]
            {
                new TorchDef { name = "PreviewTorch", basePosition = new Vector3(5f, 0f, 5f) }
            };
            definition.arenas = new[]
            {
                new ArenaDef
                {
                    enabled = true,
                    gateName = "PreviewGate",
                    gateOpenPosition = new Vector3(8f, -2f, 3f),
                    gateClosedPosition = new Vector3(8f, 2f, 3f),
                    triggerName = "PreviewArena",
                    triggerPosition = new Vector3(8f, 1f, 3f),
                    triggerSize = new Vector3(5f, 3f, 4f)
                }
            };
            definition.challengeRoutes = new[]
            {
                new ChallengeRouteDef { routeId = "PreviewRoute", entryCenter = new Vector3(3f, 2f, 5f) }
            };
            definition.killZone = new KillZoneDef { name = "PreviewKill", center = new Vector3(0f, -10f, 0f), size = new Vector3(30f, 2f, 30f) };
            definition.sky = new SkyDef { enabled = false };
            definition.worldLeaderboard = new WorldLeaderboardDef { enabled = false };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(definition);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(enemyPrefab);
            Object.DestroyImmediate(checkpointPrefab);
            Object.DestroyImmediate(pickupPrefab);
            Object.DestroyImmediate(balloonPrefab);
            Object.DestroyImmediate(testItem);
        }

        [Test]
        public void BuildWorldContent_UsesOnlyTheExplicitRoot_WhenRuntimeBehaviourIsDisabled()
        {
            var context = new LevelWorldContentContext
            {
                root = root.transform,
                pieces = TestPieces(),
                builtSpawners = new Dictionary<string, EnemySpawner>(),
                safety = LevelWorldContentSafety.PreviewSafe,
                buildCloudSea = false
            };

            var unrelated = new GameObject("UnrelatedSceneSibling");
            Vector3 unrelatedPosition = unrelated.transform.position;
            var rootsBefore = RootSet();

            LevelWorldContentResult result;
            try { result = LevelDefinitionBuilder.BuildWorldContent(definition, context); }
            finally
            {
                Assert.AreEqual(unrelatedPosition, unrelated.transform.position);
                Assert.IsNull(unrelated.transform.parent);
                CollectionAssert.AreEquivalent(rootsBefore, RootSet(),
                    "preview construction may add only descendants to its explicit root, never scene siblings");
                Object.DestroyImmediate(unrelated);
            }

            Assert.AreSame(root.transform, result.root);
            Assert.IsNotNull(root.transform.Find("PreviewPlatform"));
            Assert.IsNotNull(root.transform.Find("PreviewAltar"));
            Assert.IsNotNull(root.transform.Find("PreviewGate"));
            Assert.IsNotNull(root.transform.Find("PreviewArena"));
            Assert.IsNotNull(root.GetComponentInChildren<ChallengeRouteMarker>(true));
            Assert.IsNotNull(root.transform.Find("PreviewKill"));
            Assert.IsNull(root.GetComponentInChildren<LevelRunScorer>(true));
            Assert.IsNull(root.GetComponentInChildren<ProjectileVolleySequence>(true));
            Assert.IsNull(root.GetComponentInChildren<BossArenaTrigger>(true));
            Assert.IsNull(root.GetComponentInChildren<KillZone>(true));
            Assert.IsNull(FindComponentNamed("NavMeshSurface"));
            Assert.IsNull(root.transform.Find("Player"));
            Assert.IsNull(root.transform.Find("Managers"));
            Assert.IsNull(root.transform.Find("HUD"));
            Assert.IsFalse(root.GetComponentInChildren<EnemySpawner>(true).enabled);
            Assert.IsFalse(root.GetComponentInChildren<Checkpoint>(true).enabled);
            Assert.IsFalse(root.GetComponentInChildren<ItemPickup>(true).enabled);
            Assert.IsFalse(root.GetComponentInChildren<Balloon>(true).enabled);
            Assert.IsFalse(root.GetComponentInChildren<WaterVolume>(true).enabled);
            Assert.IsFalse(root.GetComponentInChildren<WandPedestal>(true).enabled);
            Assert.IsFalse(root.GetComponentInChildren<FlickerLight>(true).enabled);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(behaviour is CloudSea))
                    Assert.IsFalse(behaviour.enabled, behaviour.GetType().Name + " must be inert in PreviewSafe mode");
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                Assert.IsFalse(collider.enabled, collider.name + " must remain authored but inactive in PreviewSafe mode");
            Assert.IsTrue(root.GetComponentInChildren<Renderer>(true).enabled,
                "preview safety must not turn off the visual artifacts it exists to inspect");
        }

        [Test]
        public void BuildWorldContent_CampaignRuntimeKeepsTheSharedFactoryArtifactsLive()
        {
            var context = new LevelWorldContentContext
            {
                root = root.transform,
                pieces = TestPieces(),
                builtSpawners = new Dictionary<string, EnemySpawner>(),
                safety = LevelWorldContentSafety.CampaignRuntime,
                buildCloudSea = false
            };

            LevelWorldContentResult result = LevelDefinitionBuilder.BuildWorldContent(definition, context);

            Assert.IsNotNull(result.startSpawn);
            Assert.AreSame(context.builtSpawners, result.builtSpawners);
            Assert.IsTrue(result.builtSpawners.ContainsKey("PreviewSpawner"),
                "the campaign wrapper receives the per-build map used for sequence and arena wiring");
            Assert.IsNotNull(root.GetComponentInChildren<BossArenaTrigger>(true));
            Assert.IsNotNull(root.GetComponentInChildren<KillZone>(true));
            Assert.IsTrue(root.GetComponentInChildren<EnemySpawner>(true).enabled);
            Assert.IsTrue(root.GetComponentInChildren<Checkpoint>(true).enabled);
            Assert.IsTrue(root.GetComponentInChildren<ItemPickup>(true).enabled);
            Assert.IsTrue(root.GetComponentInChildren<Balloon>(true).enabled);
            Assert.IsTrue(root.GetComponentInChildren<WaterVolume>(true).enabled);
            Assert.IsTrue(root.GetComponentInChildren<WandPedestal>(true).enabled);
            Assert.IsTrue(root.GetComponentInChildren<Collider>(true).enabled);
        }

        [Test]
        public void BuildWorldContent_KeepsRepresentativeStaticArtifactsIdenticalAcrossRuntimeModes()
        {
            var runtimeRoot = new GameObject("RuntimeContentRoot");
            try
            {
                var preview = LevelDefinitionBuilder.BuildWorldContent(definition, new LevelWorldContentContext
                {
                    root = root.transform,
                    pieces = TestPieces(),
                    builtSpawners = new Dictionary<string, EnemySpawner>(),
                    safety = LevelWorldContentSafety.PreviewSafe,
                    buildCloudSea = false
                });
                var runtime = LevelDefinitionBuilder.BuildWorldContent(definition, new LevelWorldContentContext
                {
                    root = runtimeRoot.transform,
                    pieces = TestPieces(),
                    builtSpawners = new Dictionary<string, EnemySpawner>(),
                    safety = LevelWorldContentSafety.CampaignRuntime,
                    buildCloudSea = false
                });

                Assert.AreEqual(preview.counts.boxes, runtime.counts.boxes);
                Assert.AreEqual(root.transform.Find("PreviewPlatform").position,
                                runtimeRoot.transform.Find("PreviewPlatform").position);
                Assert.AreEqual(root.transform.Find("PreviewAltar").position,
                                runtimeRoot.transform.Find("PreviewAltar").position);
                Assert.AreEqual(root.transform.Find("PreviewGate").localScale,
                                runtimeRoot.transform.Find("PreviewGate").localScale);
                Assert.AreEqual(root.GetComponentInChildren<ChallengeRouteMarker>(true).RouteId,
                                runtimeRoot.GetComponentInChildren<ChallengeRouteMarker>(true).RouteId);
            }
            finally { Object.DestroyImmediate(runtimeRoot); }
        }

        Component FindComponentNamed(string typeName)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
                if (component != null && component.GetType().Name == typeName) return component;
            return null;
        }

        LevelPieceContext TestPieces()
        {
            return new LevelPieceContext
            {
                prefabs = key => key == "PreviewEnemy" ? enemyPrefab :
                                  key == "Checkpoint" ? checkpointPrefab :
                                  key == "ItemPickup" ? pickupPrefab :
                                  key == "Balloon" ? balloonPrefab : null,
                items = key => key == "PreviewItem" ? testItem : null,
                instantiate = prefab => Object.Instantiate(prefab)
            };
        }

        static GameObject[] RootSet()
        {
            return SceneManager.GetActiveScene().GetRootGameObjects();
        }
    }
}
