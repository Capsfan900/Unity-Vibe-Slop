using System.IO;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    public static class ParryRecordingStageFactory
    {
        const string Folder = "Assets/Data/Levels/Recording";

        [MenuItem("VibeGame1/10. Build Parry Recording Stages")]
        public static void CreateAll()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[ParryRecordingStages] Exit play mode first."); return; }
            Directory.CreateDirectory(Folder);
            Save("Recording_Flat", "Flat Walkway", new[] { Platform("Flat", Vector3.zero, new Vector3(12f, 1f, 100f)) }, null, null);
            Save("Recording_Downhill", "Downhill Ramp", null, new[] { Ramp("Downhill", new Vector3(0f, 8f, -45f), 90f, -8f) }, null);
            Save("Recording_Ice", "Ice / Water Slideway", new[] { Platform("IceDeck", Vector3.zero, new Vector3(12f, 1f, 100f)) }, null,
                new[] { Water("IceLine", new Vector3(0f, .52f, 0f), new Vector3(10f, .04f, 96f)) });
            Save("Recording_Mixed", "Mixed Traversal", new[] {
                Platform("MixedStart", new Vector3(0f, 0f, -35f), new Vector3(12f, 1f, 30f)),
                Platform("MixedEnd", new Vector3(0f, -4f, 35f), new Vector3(12f, 1f, 30f)) },
                new[] { Ramp("MixedRamp", new Vector3(0f, .5f, -20f), 40f, -4f) },
                new[] { Water("MixedWater", new Vector3(0f, -3.48f, 35f), new Vector3(10f, .04f, 26f)) });
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ParryRecordingStages] Built 4 recording stages.");
        }

        static void Save(string key, string displayName, PlatformDef[] platforms, RampDef[] ramps, WaterDef[] waters)
        {
            string path = Folder + "/" + key + ".asset";
            var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
            if (level == null) { level = ScriptableObject.CreateInstance<LevelDefinition>(); AssetDatabase.CreateAsset(level, path); }
            level.levelId = key.ToLowerInvariant(); level.displayName = displayName; level.sceneName = "ParryRecording";
            level.playerStart = new Vector3(0f, 1.5f, -46f); level.playerStartYaw = 0f;
            level.zones = new[] { new ZoneDef { zoneId = "T0", canonicalName = displayName, splitName = "Recording", center = Vector3.zero, size = new Vector3(40f, 30f, 120f) } };
            level.platforms = platforms ?? new PlatformDef[0]; level.ramps = ramps ?? new RampDef[0]; level.waters = waters ?? new WaterDef[0];
            level.spawns = new SpawnDef[0]; level.sky.enabled = false;
            level.killZone = new KillZoneDef { name = "KillZone", center = new Vector3(0f, -15f, 0f), size = new Vector3(50f, 2f, 130f) };
            EditorUtility.SetDirty(level);
        }

        static PlatformDef Platform(string name, Vector3 center, Vector3 size) { return new PlatformDef { meta = Meta(name), name = name, center = center, size = size, trim = true }; }
        static RampDef Ramp(string name, Vector3 start, float run, float rise) { return new RampDef { meta = Meta(name), name = name, basePosition = start, width = 12f, run = run, rise = rise }; }
        static WaterDef Water(string name, Vector3 center, Vector3 size) { return new WaterDef { meta = Meta(name), name = name, center = center, size = size, flowDirection = Vector3.forward, flowSpeed = 6f }; }
        static LevelObjectMeta Meta(string name) { return new LevelObjectMeta { objectId = "T0." + name + ".01", zoneIdOverride = "T0" }; }
    }
}
