using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class LevelT1OpeningTests
    {
        LevelDefinition def;
        [SetUp]
        public void Load()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            def = Object.Instantiate(shipped);
            LevelDefinitionAuthoring.Apply(def);
        }
        [TearDown] public void Clean() { Object.DestroyImmediate(def); }

        [Test]
        public void OpenCoursePassPreservesTheCompleteOpeningHill()
        {
            var ramp = def.ramps.Single(r => r.name == "T0_Ramp_Descent");
            Assert.That(ramp.basePosition, Is.EqualTo(new Vector3(0f, 36f, -159.8f)));
            Assert.AreEqual(14f, ramp.width, 0.001f);
            Assert.AreEqual(144f, ramp.run, 0.001f);
            Assert.AreEqual(-36f, ramp.rise, 0.001f);
            Assert.That(def.playerStart, Is.EqualTo(new Vector3(0f, 36.3f, -162.3f)));
            Assert.AreEqual(5, def.spawns.Count(s => s.name.StartsWith("Spawn_T0_Surge_")));
            Assert.AreEqual(2, def.spawns.Count(s => s.name.StartsWith("Spawn_T0_Reliquary_")));
        }

        [Test]
        public void FirstPostOpeningDeckRemainsJoinedToGroundStart()
        {
            var ground = def.platforms.Single(p => p.name == "Ground_Start");
            var first = def.platforms.Single(p => p.name == "T1_Stone_1");
            float gap = Mathf.Max(first.center.z - first.size.z * 0.5f -
                                  (ground.center.z + ground.size.z * 0.5f), 0f);
            Assert.LessOrEqual(gap, 3f, "the preserved opening must hand off cleanly into the open course");
            Assert.GreaterOrEqual(first.size.x, 12f, "the first post-opening landing must not become a pinch");
        }
    }
}
