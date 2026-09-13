using System.Linq;
using NUnit.Framework;
using UnityEditor;

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
    }
}
