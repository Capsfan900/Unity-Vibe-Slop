using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>Data and presentation contracts for optional Insight-route hand markers.</summary>
    public class InsightRouteMarkerTests
    {
        static InsightRouteDef Sample()
        {
            return new InsightRouteDef
            {
                routeId = "T2_InsightHand",
                sourceSpawnerNames = new[] { "Spawn_T2_GruntA", "Spawn_T2_GruntB" },
                markerPosition = new Vector3(4f, 8f, 12f),
                markerEulerAngles = new Vector3(0f, 135f, 0f),
                markerScale = new Vector3(1.2f, 0.8f, 1.4f),
                markerMaterialKey = "NeonCyan",
                entryCenter = new Vector3(2f, 4f, 8f),
                entrySize = new Vector3(5f, 3f, 6f),
                rejoinCenter = new Vector3(7f, 10f, 19f),
                rejoinSize = new Vector3(4f, 2f, 5f),
            };
        }

        [Test]
        public void DefinitionDefaultsToAnEmptyInsightRouteArray()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                Assert.IsNotNull(def.insightRoutes);
                Assert.AreEqual(0, def.insightRoutes.Length);
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void MarkerCopiesAuthoredDataAndReadsBackItsWorldPose()
        {
            var go = new GameObject("Insight marker test");
            try
            {
                var marker = go.AddComponent<InsightRouteMarker>();
                var source = Sample();
                marker.Configure(source);
                source.sourceSpawnerNames[0] = "mutated";

                Assert.AreEqual(InsightRouteMarker.NameFor("T2_InsightHand"), go.name);
                Assert.Less((go.transform.position - source.markerPosition).magnitude, 1e-4f);
                Assert.Less(Quaternion.Angle(go.transform.rotation, Quaternion.Euler(source.markerEulerAngles)), 0.01f);
                Assert.Less((go.transform.localScale - source.markerScale).magnitude, 1e-4f);

                var back = marker.ToDefinition();
                Assert.AreEqual("T2_InsightHand", back.routeId);
                Assert.AreEqual("Spawn_T2_GruntA", back.sourceSpawnerNames[0]);
                Assert.AreEqual(source.entryCenter, back.entryCenter);
                Assert.AreEqual(source.entrySize, back.entrySize);
                Assert.AreEqual(source.rejoinCenter, back.rejoinCenter);
                Assert.AreEqual(source.rejoinSize, back.rejoinSize);
                Assert.AreEqual(source.markerMaterialKey, back.markerMaterialKey);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void BuilderCreatesOnlySkyLayerColliderlessHandAndLabel()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            var parent = new GameObject("Insight builder test");
            try
            {
                def.insightRoutes = new[] { Sample() };
                var build = typeof(LevelDefinitionBuilder).GetMethod("BuildInsightRoutes",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(build, "the regenerable level builder must own Insight marker geometry");
                build.Invoke(null, new object[] { def, parent.transform, new LevelPieceContext() });

                var marker = parent.GetComponentInChildren<InsightRouteMarker>(true);
                Assert.IsNotNull(marker);
                Assert.AreEqual("T2_InsightHand", marker.RouteId);
                Assert.IsNotNull(marker.transform.Find("Palm"));
                var label = marker.GetComponentInChildren<TextMeshPro>(true);
                Assert.IsNotNull(label);
                Assert.AreEqual("INSIGHT", label.text);
                foreach (var t in marker.GetComponentsInChildren<Transform>(true))
                {
                    Assert.AreEqual(Starfield.SkyLayer, t.gameObject.layer,
                        t.name + " must stay outside the Default-only NavMesh bake");
                    Assert.IsEmpty(t.GetComponents<Collider>(),
                        t.name + " is presentation-only and must not block the route");
                }
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ExporterRoundTripsInsightRouteWithoutExportingHandGeometryAsPlatforms()
        {
            var source = ScriptableObject.CreateInstance<LevelDefinition>();
            var exported = ScriptableObject.CreateInstance<LevelDefinition>();
            var root = new GameObject("Level");
            try
            {
                source.insightRoutes = new[] { Sample() };
                var build = typeof(LevelDefinitionBuilder).GetMethod("BuildInsightRoutes",
                    BindingFlags.Static | BindingFlags.NonPublic);
                build.Invoke(null, new object[] { source, root.transform, new LevelPieceContext() });

                LevelDefinitionExporter.ExportInto(root, exported);
                Assert.AreEqual(1, exported.insightRoutes.Length);
                var back = exported.insightRoutes[0];
                Assert.AreEqual("T2_InsightHand", back.routeId);
                CollectionAssert.AreEqual(source.insightRoutes[0].sourceSpawnerNames, back.sourceSpawnerNames);
                Assert.AreEqual(source.insightRoutes[0].entryCenter, back.entryCenter);
                Assert.AreEqual(source.insightRoutes[0].entrySize, back.entrySize);
                Assert.AreEqual(source.insightRoutes[0].rejoinCenter, back.rejoinCenter);
                Assert.AreEqual(source.insightRoutes[0].rejoinSize, back.rejoinSize);
                Assert.AreEqual(0, exported.platforms.Length,
                    "the generated hand is a marker, never a sequence of walkable platform exports");
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
