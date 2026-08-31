using System;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Top-level convenience menu. The numbered steps under VibeGame1/ can each be run on their own;
    /// this runs the whole pipeline in the one order that actually works:
    ///
    ///   ProjectSetup -> Materials -> Data -> Prefabs -> HUD -> Level
    ///
    /// The order matters. Prefabs reference materials and ScriptableObjects, the HUD reads
    /// UpgradeTable.asset, and the level instantiates the Player/Managers/HUD prefabs. Running these
    /// out of order silently produces prefabs with null references.
    /// </summary>
    public static class VibeGameMenu
    {
        public const string TestScenePath = "Assets/Scenes/Level_01.unity";

        [MenuItem("VibeGame1/0. Rebuild Everything", priority = 0)]
        public static void RebuildEverything()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[VibeGame1] Rebuild Everything aborted: exit play mode first. " +
                                 "The level builder edits and saves the open scene, which Unity forbids during play.");
                return;
            }

            var steps = new (string label, Action run)[]
            {
                ("Project setup (layers, HDR grading, volume, lighting)", ProjectSetup.Run),
                ("Materials",                                             MaterialFactory.CreateAll),
                ("Data (ScriptableObjects)",                              DataFactory.CreateAll),
                // Must run BEFORE prefabs: PrefabFactory assigns the wand viewmodels back onto these
                // assets. Miss this step and every riposte silently falls back to the melee deathblow.
                ("Wands",                                                 WandFactory.CreateAll),
                ("Prefabs",                                               PrefabFactory.BuildAll),
                ("HUD",                                                   HudBuilder.Build),
                ("Level greybox + NavMesh",                               LevelGreyboxBuilder.Build),
            };

            var sw = Stopwatch.StartNew();
            int completed = 0;

            try
            {
                for (int i = 0; i < steps.Length; i++)
                {
                    var (label, run) = steps[i];
                    EditorUtility.DisplayProgressBar(
                        "VibeGame1 — Rebuild Everything",
                        $"({i + 1}/{steps.Length}) {label}",
                        (float)i / steps.Length);

                    run();
                    completed++;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VibeGame1] Rebuild FAILED on step {completed + 1}/{steps.Length} " +
                               $"({steps[Mathf.Min(completed, steps.Length - 1)].label}): {e.Message}\n{e}");
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                sw.Stop();
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[VibeGame1] Rebuild Everything complete — {completed}/{steps.Length} steps in {sw.ElapsedMilliseconds} ms.\n" +
                      "Next: VibeGame1/Health Check to validate wiring, then press Play.");
        }

        [MenuItem("VibeGame1/Open Test Level", priority = 20)]
        public static void OpenTestLevel()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[VibeGame1] Cannot open a scene during play mode.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) == null)
            {
                Debug.LogError($"[VibeGame1] Test scene not found at {TestScenePath}.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
        }
    }
}
