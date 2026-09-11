using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;
using T = VibeGame1.EditorTools.LevelTraversalAnalyzer;

namespace VibeGame1.Tests
{
    [Category("LevelLines")]
    public class LevelSpan2Tests
    {
        LevelDefinition def;
        A.MoveProfile profile;
        static readonly Vector3[] Centers =
        {
            new Vector3(14f,4.5f,151.375f), new Vector3(18f,6f,162f), new Vector3(2f,7.5f,170.5f),
            new Vector3(-13f,9f,162f), new Vector3(-16f,10.5f,151f), new Vector3(0f,12f,145f),
            new Vector3(16f,13.5f,151f), new Vector3(18f,15f,162f), new Vector3(3f,16.5f,170.5f),
            new Vector3(16f,18f,179f), new Vector3(0f,19.5f,183.5f),
        };
        static readonly Vector3[] Sizes =
        {
            new Vector3(12f,1f,10f), new Vector3(10f,1f,8f), new Vector3(18f,1f,8f),
            new Vector3(12f,1f,8f), new Vector3(10f,1f,9f), new Vector3(18f,1f,10f),
            new Vector3(12f,1f,9f), new Vector3(10f,1f,8f), new Vector3(18f,1f,8f),
            new Vector3(12f,1f,8f), new Vector3(18f,1f,5f),
        };

        [SetUp]
        public void Load()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            def = Object.Instantiate(shipped);
            LevelDefinitionAuthoring.Apply(def);
            string error;
            Assert.IsTrue(A.TryLoadProfile(out profile, out error), error);
        }
        [TearDown] public void Clean() { Object.DestroyImmediate(def); }

        [Test]
        public void DoubleSwitchbackShipsAsElevenFullTerraces()
        {
            for (int i = 0; i < 11; i++)
            {
                var p = def.platforms.Single(x => x.name == "T2_L" + (i + 1));
                Assert.That(Vector3.Distance(p.center, Centers[i]), Is.LessThan(0.001f), p.name + " center");
                Assert.That(Vector3.Distance(p.size, Sizes[i]), Is.LessThan(0.001f), p.name + " size");
                Assert.GreaterOrEqual(p.size.x, 10f, p.name + " is too narrow to redirect at speed");
            }
        }

        [Test]
        public void TowerChimneyAndWallClutterAreGone()
        {
            string[] removed = { "T2_Tower", "T2_Buttress", "T2_Wall_East", "T2_Wall_Landing_East",
                                 "T2_Wall_West", "T2_Wall_Landing_West" };
            foreach (string name in removed) Assert.IsFalse(def.platforms.Any(p => p.name == name), name);
            Assert.IsFalse(def.ramps.Any(r => r.name.StartsWith("T2_")));
        }

        [Test]
        public void TerraceChainIsReachableAtBaseRunSpeed()
        {
            var boxes = A.BoxesFrom(def);
            for (int i = 1; i < 11; i++)
            {
                var hop = A.AnalyzeHop(boxes, "T2_L" + i, "T2_L" + (i + 1), profile,
                                       profile.groundSpeed, def.killZone.center.y);
                Assert.IsTrue(hop.exists, hop.Summary());
                Assert.GreaterOrEqual(hop.cleanLaunchPoints, 3, hop.Summary());
            }
        }

        [Test]
        public void LowerAndUpperSentriesEachCrossTheirAssignedTerraces()
        {
            var boxes = A.BoxesFrom(def);
            foreach (var perch in LevelDefinitionAuthoring.Perches.Where(p => p.name.StartsWith("T2_")))
            {
                var verdict = T.AnalyzeShooter(boxes, perch.name, perch.spawn, perch.covers.Split(','), 10f, 32f);
                Assert.AreEqual(perch.covers.Split(',').Length, verdict.covered.Count, verdict.Summary());
            }
        }
    }
}
