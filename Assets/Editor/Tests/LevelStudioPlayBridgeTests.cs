using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    public class LevelStudioPlayBridgeTests
    {
        [Test]
        public void DefinitionDocumentRoundTripPreservesFullSchemaWithoutAliasing()
        {
            var source = ScriptableObject.CreateInstance<LevelDefinition>();
            var target = ScriptableObject.CreateInstance<LevelDefinition>();
            source.levelId = "sentinel"; source.sceneName = "Level_01"; source.requiredRunSouls = 77;
            source.playerStartMeta.objectId = "T0.PlayerStart";
            source.platforms = new[] { new PlatformDef { name = "Deck", meta = new LevelObjectMeta { objectId = "T0.Platform.01" }, center = Vector3.one } };
            source.pedestals = new[] { new PedestalDef { name = "Pedestal", meta = new LevelObjectMeta { objectId = "T0.Pedestal.01" }, triggerRadius = 9f } };
            source.arenas = new[] { new ArenaDef { meta = new LevelObjectMeta { objectId = "T0.Arena.01" }, clearSpawnerName = "Boss", solarRealm = new SolarRealmDef { enabled = true, realmCenter = Vector3.forward * 5f } } };
            source.runSplits = new[] { new RunSplitDef { meta = new LevelObjectMeta { objectId = "T0.RunSplit.01" }, name = "Split", aSeconds = 12f } };
            string expected = JsonUtility.ToJson(source);

            var document = LevelDocument.FromDefinition(source);
            document.platforms[0].center = Vector3.up;
            Assert.AreEqual(Vector3.one, source.platforms[0].center, "document must not alias campaign data");
            document.platforms[0].center = Vector3.one;
            document.CopyTo(target);

            Assert.AreEqual(expected, JsonUtility.ToJson(target));
            Object.DestroyImmediate(source); Object.DestroyImmediate(target);
        }
    }
}
