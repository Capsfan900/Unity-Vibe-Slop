using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    public static class ParryRecordingStageFactory
    {
        const string Folder = "Assets/Data/Levels/Recording";

        [MenuItem("VibeGame1/10. Build Parry Recording Stages")]
        public static void Menu()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[ParryRecordingStages] Exit play mode first."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[ParryRecordingStages] Cancelled (unsaved changes kept).");
                return;
            }
            CreateAll();
            BuildScenes(false);   // the prompt above already let the user save or discard
        }

        /// <summary>Writes the four LevelDefinition assets. Idempotent; touches no scene.</summary>
        public static void CreateAll()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[ParryRecordingStages] Exit play mode first."); return; }
            Directory.CreateDirectory(Folder);
            Save("Recording_Flat", new[] { Platform("Flat", Vector3.zero, new Vector3(12f, 1f, 100f)) }, null, null);
            Save("Recording_Downhill", null, new[] { Ramp("Downhill", new Vector3(0f, 8f, -45f), 90f, -8f) }, null);
            Save("Recording_Ice", new[] { Platform("IceDeck", Vector3.zero, new Vector3(12f, 1f, 100f)) }, null,
                new[] { Water("IceLine", new Vector3(0f, .52f, 0f), new Vector3(10f, .04f, 96f)) });
            Save("Recording_Mixed", new[] {
                Platform("MixedStart", new Vector3(0f, 0f, -35f), new Vector3(12f, 1f, 30f)),
                Platform("MixedEnd", new Vector3(0f, -4f, 35f), new Vector3(12f, 1f, 30f)) },
                new[] { Ramp("MixedRamp", new Vector3(0f, .5f, -20f), 40f, -4f) },
                new[] { Water("MixedWater", new Vector3(0f, -3.48f, 35f), new Vector3(10f, .04f, 26f)) });
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ParryRecordingStages] Built 4 recording stages.");
        }

        /// <summary>
        /// Creates (or rebuilds) one playable scene per stage through the normal
        /// <see cref="LevelDefinitionBuilder"/>, then reopens the scene that was open before. Never prompts:
        /// it saves open scenes first, because a save dialog deadlocks the MCP bridge. The scenes are
        /// launched in the editor by path from the main menu's developer rows, so they are deliberately NOT
        /// added to the build scene list (BuildRunner would strip them from the exe anyway).
        /// <paramref name="saveOpenScenes"/> is for callers with no dialog (MCP); the menu prompts instead.
        /// </summary>
        public static void BuildScenes(bool saveOpenScenes)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ParryRecordingStages] Exit play mode first."); return; }
            if (saveOpenScenes) EditorSceneManager.SaveOpenScenes();
            string previous = EditorSceneManager.GetActiveScene().path;

            int built = 0;
            foreach (var stage in ParryRecordingStages.All)
            {
                string assetPath = Folder + "/" + stage.key + ".asset";
                if (File.Exists(stage.ScenePath))
                    EditorSceneManager.OpenScene(stage.ScenePath, OpenSceneMode.Single);
                else
                {
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    EditorSceneManager.SaveScene(scene, stage.ScenePath);
                }

                // Load AFTER the scene swap: OpenScene unloads a definition held only by a local.
                var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
                if (def == null) { Debug.LogError("[ParryRecordingStages] Missing " + assetPath + "; run CreateAll first."); continue; }
                if (EditorSceneManager.GetActiveScene().name != def.sceneName)
                {
                    Debug.LogError("[ParryRecordingStages] " + stage.key + " targets '" + def.sceneName + "', not " + stage.ScenePath + ".");
                    continue;
                }
                LevelDefinitionBuilder.Build(def);
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                built++;
            }

            if (!string.IsNullOrEmpty(previous) && File.Exists(previous))
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            Debug.Log("[ParryRecordingStages] Built " + built + " recording scenes. Launch them from the main menu's developer rows.");
        }

        static void Save(string key, PlatformDef[] platforms, RampDef[] ramps, WaterDef[] waters)
        {
            ParryRecordingStages.Stage stage;
            if (!ParryRecordingStages.TryGet(key, out stage)) { Debug.LogError("[ParryRecordingStages] Unknown stage " + key); return; }
            string path = Folder + "/" + key + ".asset";
            var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
            if (level == null) { level = ScriptableObject.CreateInstance<LevelDefinition>(); AssetDatabase.CreateAsset(level, path); }
            level.levelId = key.ToLowerInvariant(); level.displayName = stage.displayName; level.sceneName = stage.sceneName;
            level.playerStart = new Vector3(0f, 1.5f, -46f); level.playerStartYaw = 0f;
            if (key == "Recording_Downhill" && ramps != null && ramps.Length == 1)
            {
                var ramp = ramps[0];
                const float entryDistance = 1f;
                level.playerStart = ramp.basePosition + ramp.Heading * entryDistance
                    + Vector3.up * (ramp.rise * entryDistance / ramp.run + 1.2f);
            }
            level.zones = new[] { new ZoneDef { zoneId = "T0", canonicalName = stage.displayName, splitName = "Recording", center = Vector3.zero, size = new Vector3(40f, 30f, 120f) } };
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
