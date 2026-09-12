using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>Data and anchor contracts for optional faster/harder flare routes.</summary>
    public class ChallengeRouteMarkerTests
    {
        static ChallengeRouteDef Sample()
        {
            return new ChallengeRouteDef
            {
                meta = new LevelObjectMeta { objectId = "T2.ChallengeRoute.01", friendlyName = "Tower flare", zoneIdOverride = "T2" },
                routeId = "T2_Challenge_Flare",
                sourceSpawnerNames = new[] { "Spawn_T2_GruntA", "Spawn_T2_GruntB" },
                entryCenter = new Vector3(2f, 4f, 8f),
                entrySize = new Vector3(5f, 3f, 6f),
                rejoinCenter = new Vector3(7f, 10f, 19f),
                rejoinSize = new Vector3(4f, 2f, 5f),
            };
        }

        [Test]
        public void DefinitionDefaultsToAnEmptyChallengeRouteArray()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                Assert.IsNotNull(def.challengeRoutes);
                Assert.AreEqual(0, def.challengeRoutes.Length);
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void MigrationMetadataKeepsLegacySerializedRouteDataReadable()
        {
            var field = typeof(LevelDefinition).GetField("challengeRoutes");
            Assert.IsNotNull(field);
            var fieldMigration = (FormerlySerializedAsAttribute)System.Array.Find(
                field.GetCustomAttributes(typeof(FormerlySerializedAsAttribute), false),
                attribute => ((FormerlySerializedAsAttribute)attribute).oldName == "insightRoutes");
            Assert.IsNotNull(fieldMigration);
            Assert.IsNotNull(typeof(ChallengeRouteDef).GetCustomAttribute<MovedFromAttribute>());
        }

        [Test]
        public void MarkerCopiesAuthoredDataAndUsesItsEntryAnchor()
        {
            var go = new GameObject("Challenge marker test");
            try
            {
                var marker = go.AddComponent<ChallengeRouteMarker>();
                var source = Sample();
                marker.Configure(source);
                source.sourceSpawnerNames[0] = "mutated";
                source.meta.objectId = "mutated";

                Assert.AreEqual(ChallengeRouteMarker.NameFor("T2_Challenge_Flare"), go.name);
                Assert.Less((go.transform.position - source.entryCenter).magnitude, 1e-4f);

                var back = marker.ToDefinition();
                Assert.AreEqual("T2_Challenge_Flare", back.routeId);
                Assert.AreEqual("Spawn_T2_GruntA", back.sourceSpawnerNames[0]);
                Assert.AreEqual("T2.ChallengeRoute.01", back.meta.objectId);
                Assert.AreEqual("Tower flare", back.meta.friendlyName);
                Assert.AreEqual("T2", back.meta.zoneIdOverride);
                Assert.AreNotSame(source.meta, back.meta);
                Assert.AreEqual(source.entryCenter, back.entryCenter);
                Assert.AreEqual(source.entrySize, back.entrySize);
                Assert.AreEqual(source.rejoinCenter, back.rejoinCenter);
                Assert.AreEqual(source.rejoinSize, back.rejoinSize);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void BuilderCreatesChallengeAnchorsWithoutHandGeometry()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            var parent = new GameObject("Challenge builder test");
            try
            {
                def.challengeRoutes = new[] { Sample() };
                var build = typeof(LevelDefinitionBuilder).GetMethod("BuildChallengeRoutes",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(build, "the regenerable level builder must own challenge-route anchors");
                build.Invoke(null, new object[] { def, parent.transform });

                var marker = parent.GetComponentInChildren<ChallengeRouteMarker>(true);
                Assert.IsNotNull(marker);
                Assert.AreEqual("T2_Challenge_Flare", marker.RouteId);
                Assert.AreEqual(1, parent.GetComponentsInChildren<ChallengeRouteMarker>(true).Length);
                Assert.AreEqual(0, parent.GetComponentsInChildren<Renderer>(true).Length);
                Assert.AreEqual(0, parent.GetComponentsInChildren<Collider>(true).Length);
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ExporterRoundTripsChallengeRouteWithoutExportingMarkerAsPlatforms()
        {
            var source = ScriptableObject.CreateInstance<LevelDefinition>();
            var exported = ScriptableObject.CreateInstance<LevelDefinition>();
            var root = new GameObject("Level");
            try
            {
                source.challengeRoutes = new[] { Sample() };
                var build = typeof(LevelDefinitionBuilder).GetMethod("BuildChallengeRoutes",
                    BindingFlags.Static | BindingFlags.NonPublic);
                build.Invoke(null, new object[] { source, root.transform });
                var marker = root.GetComponentInChildren<ChallengeRouteMarker>(true);
                Assert.IsNotNull(marker);
                var movedEntry = new Vector3(21f, 34f, 55f);
                marker.transform.position = movedEntry;

                LevelDefinitionExporter.ExportInto(root, exported);
                Assert.AreEqual(1, exported.challengeRoutes.Length);
                var back = exported.challengeRoutes[0];
                Assert.AreEqual("T2_Challenge_Flare", back.routeId);
                CollectionAssert.AreEqual(source.challengeRoutes[0].sourceSpawnerNames, back.sourceSpawnerNames);
                Assert.AreEqual(movedEntry, back.entryCenter);
                Assert.AreEqual(source.challengeRoutes[0].entrySize, back.entrySize);
                Assert.AreEqual(source.challengeRoutes[0].rejoinCenter, back.rejoinCenter);
                Assert.AreEqual(source.challengeRoutes[0].rejoinSize, back.rejoinSize);
                Assert.AreEqual(source.challengeRoutes[0].meta.objectId, back.meta.objectId);
                Assert.AreEqual(source.challengeRoutes[0].meta.friendlyName, back.meta.friendlyName);
                Assert.AreEqual(source.challengeRoutes[0].meta.zoneIdOverride, back.meta.zoneIdOverride);
                Assert.AreEqual(0, exported.platforms.Length,
                    "the generated anchor is never a sequence of walkable platform exports");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(exported);
                Object.DestroyImmediate(root);
            }
        }
    }
}
