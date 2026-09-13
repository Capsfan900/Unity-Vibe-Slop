using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.Tests
{
    public class ParryRecordingStageTests
    {
        [TestCase("Recording_Flat", "Platform")]
        [TestCase("Recording_Downhill", "Ramp")]
        [TestCase("Recording_Ice", "Water")]
        [TestCase("Recording_Mixed", "Mixed")]
        public void RecordingStageHasOneClearZoneAndNoShooters(string key, string surface)
        {
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Data/Levels/Recording/" + key + ".asset");
            Assert.IsNotNull(def, key);
            Assert.AreEqual(1, def.zones.Length);
            Assert.IsFalse(def.spawns.Any(s => s.prefabKey.StartsWith("pshooter_")));
            Assert.AreEqual("T0", def.zones[0].zoneId);
            if (surface == "Platform") Assert.IsNotEmpty(def.platforms);
            if (surface == "Ramp") Assert.IsNotEmpty(def.ramps);
            if (surface == "Water") Assert.IsNotEmpty(def.waters);
            if (surface == "Mixed") Assert.IsTrue(def.platforms.Length > 0 && def.ramps.Length > 0 && def.waters.Length > 0);
        }

        [Test]
        public void EveryRecordingStageHasItsOwnPlayableScene()
        {
            foreach (var stage in ParryRecordingStages.All)
            {
                var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Data/Levels/Recording/" + stage.key + ".asset");
                Assert.IsNotNull(def, stage.key);
                Assert.AreEqual(stage.sceneName, def.sceneName, stage.key + " must target its own scene");
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(stage.ScenePath),
                    stage.ScenePath + " is missing - run VibeGame1/10. Build Parry Recording Stages");
            }
        }

        [Test]
        public void DownhillPlayerStartsOnTheRampSurface()
        {
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Data/Levels/Recording/Recording_Downhill.asset");
            var ramp = def.ramps.Single();
            float along = Vector3.Dot(def.playerStart - ramp.basePosition, ramp.Heading);
            float surfaceY = ramp.basePosition.y + ramp.rise * (along / ramp.run);
            Vector3 lateral = def.playerStart - ramp.basePosition - ramp.Heading * along;
            lateral.y = 0f;

            Assert.That(along, Is.InRange(0f, ramp.run), "spawn must be over the ramp, not behind its edge");
            Assert.That(lateral.magnitude, Is.LessThan(ramp.width * .5f), "spawn must be inside the ramp width");
            Assert.That(def.playerStart.y - surfaceY, Is.InRange(1f, 2f), "spawn must stand just above the ramp surface");
        }
    }
}
