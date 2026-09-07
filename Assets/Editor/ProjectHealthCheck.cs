using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Validates that the generated project state is actually wired up. Everything here has been a
    /// real bug at some point: magenta Standard-shader materials, prefabs with null references after
    /// running the builders out of order, missing layers, LDR grading killing the bloom.
    ///
    /// Run after VibeGame1/0. Rebuild Everything. Read-only — it never mutates assets.
    /// </summary>
    public static class ProjectHealthCheck
    {
        const string MaterialDir = "Assets/Materials";
        const string PrefabDir = "Assets/Prefabs";
        const string DataDir = "Assets/Data";
        const string SfxDir = "Assets/Resources/Audio/Sfx";
        const string UrpAssetPath = "Assets/Settings/PC_RPAsset.asset";
        const string ScenePath = "Assets/Scenes/Level_01.unity";

        static readonly string[] ExpectedMaterials =
        {
            "M_Ground", "M_Platform", "M_NeonPink", "M_NeonCyan", "M_NeonYellow", "M_NeonRed",
            "M_Enemy", "M_EnemyEye", "M_Boss", "M_Weapon_Sword", "M_Weapon_Hammer", "M_Weapon_Dagger",
            "M_Weapon_Dev", "M_Checkpoint", "M_Bloodstain", "M_Gate", "M_Torch", "M_Stone", "M_AlertTell",
        };

        static readonly string[] ExpectedSingletonData = { "PlayerStats", "UpgradeTable", "GameFeel" };

        /// <summary>Data subfolders that must contain at least one asset. Items is newer — warn only.</summary>
        static readonly (string folder, bool required)[] ExpectedDataFolders =
        {
            ("Weapons", true), ("Enemies", true), ("Attacks", true), ("Items", false),
        };

        static readonly string[] ExpectedPlayerComponents =
        {
            "CharacterController", "FirstPersonMotor", "PlayerLook", "Health", "PlayerStats",
            "PlayerResources", "PlayerPosture", "ParryController", "PlayerCombat", "WeaponController",
            "PlayerItems", "ExecuteInteractor", "FlaskAbility", "UltimateAbility", "PlayerDeath",
            "PlayerFeedback",
        };

        static readonly (int index, string name)[] ExpectedLayers =
        {
            (6, "Player"), (7, "Enemy"), (8, "Interactable"),
        };

        /// <summary>Object-reference fields that are legitimately null in a prefab (wired at runtime, or optional).</summary>
        static readonly string[] OptionalRefNameFragments =
        {
            "m_script", "clip", "audio", "icon", "sprite", "material", "profile", "cursor",
            "optional", "target", "instance", "bloodstain", "gate", "spawn", "volume", "cam", "camera",
        };

        static List<string> errors;
        static List<string> warnings;

        [MenuItem("VibeGame1/Health Check", priority = 21)]
        public static void Run()
        {
            errors = new List<string>();
            warnings = new List<string>();

            CheckMaterials();
            CheckData();
            CheckPrefabs();
            CheckPlayerPrefab();
            CheckLayers();
            CheckRenderPipeline();
            CheckBuildSettings();
            CheckAudioFolders();

            Report();
        }

        static void Err(string m) => errors.Add(m);
        static void Warn(string m) => warnings.Add(m);

        // ---------------------------------------------------------------------------------------

        static void CheckMaterials()
        {
            foreach (var name in ExpectedMaterials)
            {
                string path = $"{MaterialDir}/{name}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
                    Err($"Missing material {path} — run VibeGame1/2. Create Materials.");
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                if (mat.shader == null)
                {
                    Err($"{path} has no shader.");
                    continue;
                }
                // UGUI shaders (the fluid bars, the Pyre fire) draw on a Screen Space Overlay canvas,
                // outside the render pipeline: URP has no say in them and they cannot render magenta
                // for the pipeline's reason. They live under VibeGame1/UI/.
                if (mat.shader.name.StartsWith("VibeGame1/UI/", StringComparison.Ordinal)) continue;
                // Custom URP shaders declare their pipeline in the SubShader tag; their display name
                // need not use Unity's built-in prefix (for example VibeGame1/Cloud Sea).
                if (!mat.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)
                    && mat.GetTag("RenderPipeline", false, "") != "UniversalPipeline")
                    Err($"{path} uses '{mat.shader.name}' — non-URP shaders render magenta. Use Universal Render Pipeline/Lit.");
            }
        }

        static void CheckData()
        {
            foreach (var name in ExpectedSingletonData)
            {
                string path = $"{DataDir}/{name}.asset";
                if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) == null)
                    Err($"Missing data asset {path} — run VibeGame1/3. Create Data.");
            }

            foreach (var (folder, required) in ExpectedDataFolders)
            {
                string dir = $"{DataDir}/{folder}";
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    string msg = $"Data folder {dir} does not exist.";
                    if (required) Err(msg + " Run VibeGame1/3. Create Data.");
                    else Warn(msg + " (Expected once that feature is built.)");
                    continue;
                }

                int count = AssetDatabase.FindAssets("t:ScriptableObject", new[] { dir }).Length;
                if (count != 0) continue;

                string empty = $"Data folder {dir} is empty.";
                if (required) Err(empty + " Run VibeGame1/3. Create Data.");
                else Warn(empty);
            }
        }

        static void CheckPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir))
            {
                Err($"{PrefabDir} does not exist — run VibeGame1/4. Build Prefabs.");
                return;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                int missing = CountMissingScripts(go);
                if (missing > 0)
                    Err($"{path} has {missing} missing script(s) — a component's class was renamed or deleted.");

                ScanNullReferences(go, path);
            }
        }

        static int CountMissingScripts(GameObject root)
        {
            int total = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                total += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            return total;
        }

        static void ScanNullReferences(GameObject root, string path)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;   // missing script, already reported

                var so = new SerializedObject(behaviour);
                var prop = so.GetIterator();
                int guard = 0;

                while (prop.NextVisible(true) && guard++ < 512)
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (prop.objectReferenceValue != null) continue;
                    if (IsOptionalRef(prop.name)) continue;

                    Warn($"{path} :: {behaviour.GetType().Name}.{prop.name} is null " +
                         "(may be wired at runtime — verify if this field is required).");
                }
            }
        }

        static bool IsOptionalRef(string fieldName)
        {
            string lower = fieldName.ToLowerInvariant();
            return OptionalRefNameFragments.Any(f => lower.Contains(f));
        }

        static void CheckPlayerPrefab()
        {
            string path = $"{PrefabDir}/Player.prefab";
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (player == null)
            {
                Err($"Missing {path} — run VibeGame1/4. Build Prefabs.");
                return;
            }

            // Compare by type NAME so this file never fails to compile when a gameplay script is mid-edit.
            var present = new HashSet<string>(
                player.GetComponents<Component>()
                      .Where(c => c != null)
                      .Select(c => c.GetType().Name));

            foreach (var required in ExpectedPlayerComponents)
                if (!present.Contains(required))
                    Err($"Player.prefab is missing component '{required}' — check PrefabFactory.BuildAll().");
        }

        static void CheckLayers()
        {
            foreach (var (index, expected) in ExpectedLayers)
            {
                string actual = LayerMask.LayerToName(index);
                if (actual != expected)
                    Err($"Layer {index} is '{actual}' but should be '{expected}' — run VibeGame1/1. Project Setup.");
            }
        }

        static void CheckRenderPipeline()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null)
            {
                Err($"Missing URP asset at {UrpAssetPath}.");
                return;
            }

            if (urp.colorGradingMode != ColorGradingMode.HighDynamicRange)
                Err($"{UrpAssetPath} uses LDR color grading — HDR emissive bloom will be crushed. " +
                    "Run VibeGame1/1. Project Setup.");

            if (!urp.supportsHDR)
                Warn($"{UrpAssetPath} has HDR disabled.");
        }

        static void CheckBuildSettings()
        {
            bool present = EditorBuildSettings.scenes.Any(
                s => string.Equals(s.path, ScenePath, StringComparison.OrdinalIgnoreCase));

            if (!present) Err($"{ScenePath} is not in the build settings (File > Build Profiles).");
            else if (!EditorBuildSettings.scenes.First(
                         s => string.Equals(s.path, ScenePath, StringComparison.OrdinalIgnoreCase)).enabled)
                Warn($"{ScenePath} is in the build settings but disabled.");
        }

        static void CheckAudioFolders()
        {
            var sfxType = AppDomain.CurrentDomain.GetAssemblies()
                                   .Select(a => a.GetType("VibeGame1.Sfx", false))
                                   .FirstOrDefault(t => t != null && t.IsEnum);

            if (sfxType == null)
            {
                Warn("Could not resolve the VibeGame1.Sfx enum by reflection — skipped the audio folder check.");
                return;
            }

            foreach (var name in Enum.GetNames(sfxType))
            {
                // Drone is the ambient loop; it lives under Audio/Music, not Audio/Sfx.
                if (name == "Drone") continue;

                string dir = $"{SfxDir}/{name}";
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    Warn($"No clip folder {dir} — Sfx.{name} falls back to the synthesized clip.");
                    continue;
                }

                if (AssetDatabase.FindAssets("t:AudioClip", new[] { dir }).Length == 0)
                    Warn($"{dir} contains no AudioClips — Sfx.{name} falls back to the synthesized clip.");
            }

            foreach (var track in new[] { "ambient", "boss" })
                if (Resources.Load<AudioClip>($"Audio/Music/{track}") == null)
                    Warn($"Missing music track Resources/Audio/Music/{track} — the ambient drone fallback will be used.");
        }

        // ---------------------------------------------------------------------------------------

        static void Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== VibeGame1 Health Check ===");

            if (errors.Count > 0)
            {
                sb.AppendLine($"\nERRORS ({errors.Count}):");
                foreach (var e in errors) sb.AppendLine("  [x] " + e);
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine($"\nWARNINGS ({warnings.Count}):");
                foreach (var w in warnings) sb.AppendLine("  [!] " + w);
            }

            sb.AppendLine();
            sb.AppendLine(errors.Count == 0
                ? $"RESULT: PASS ({warnings.Count} warning(s))"
                : $"RESULT: FAIL ({errors.Count} error(s), {warnings.Count} warning(s))");

            if (errors.Count > 0) Debug.LogError(sb.ToString());
            else if (warnings.Count > 0) Debug.LogWarning(sb.ToString());
            else Debug.Log(sb.ToString());
        }
    }
}
