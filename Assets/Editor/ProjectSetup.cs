using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// One-shot, idempotent project configuration: physics layers, HDR color grading,
    /// neon post-processing profile, renderer features and scene lighting/fog.
    /// </summary>
    public static class ProjectSetup
    {
        const string PcRpAssetPath = "Assets/Settings/PC_RPAsset.asset";
        const string MobileRpAssetPath = "Assets/Settings/Mobile_RPAsset.asset";
        const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";
        const string ProfilePath = "Assets/Settings/SampleSceneProfile.asset";

        [MenuItem("VibeGame1/1. Project Setup")]
        public static void Run()
        {
            var log = new List<string>();

            SetupLayers(log);
            SetupPhysicsMatrix(log);
            SetupRenderPipelineAssets(log);
            SetupVolumeProfile(log);
            SetupRendererFeatures(log);
            SetupSceneEnvironment(log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[ProjectSetup] Done:\n - " + string.Join("\n - ", log));
        }

        // ------------------------------------------------------------------ layers

        static void SetupLayers(List<string> log)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                log.Add("TagManager.asset not found; layers NOT set.");
                return;
            }

            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");
            if (layers == null || !layers.isArray)
            {
                log.Add("TagManager 'layers' property not found; layers NOT set.");
                return;
            }

            bool changed = false;
            changed |= SetLayer(layers, Layers.Player, "Player");
            changed |= SetLayer(layers, Layers.Enemy, "Enemy");
            changed |= SetLayer(layers, Layers.Interactable, "Interactable");

            if (changed) tagManager.ApplyModifiedProperties();
            log.Add($"Layers: {Layers.Player}=Player, {Layers.Enemy}=Enemy, {Layers.Interactable}=Interactable ({(changed ? "updated" : "already set")})");
        }

        static bool SetLayer(SerializedProperty layers, int index, string name)
        {
            if (index < 0 || index >= layers.arraySize) return false;
            var element = layers.GetArrayElementAtIndex(index);
            if (element.stringValue == name) return false;
            if (!string.IsNullOrEmpty(element.stringValue))
                Debug.LogWarning($"[ProjectSetup] Layer {index} was '{element.stringValue}', overwriting with '{name}'.");
            element.stringValue = name;
            return true;
        }

        // ---------------------------------------------------------- physics matrix

        static void SetupPhysicsMatrix(List<string> log)
        {
            const int defaultLayer = 0;

            // Player <-> Enemy collide (CharacterController is blocked by enemy bodies).
            Physics.IgnoreLayerCollision(Layers.Player, Layers.Enemy, false);
            // Enemies collide with each other (NavMeshAgents + capsules).
            Physics.IgnoreLayerCollision(Layers.Enemy, Layers.Enemy, false);

            // Interactable is trigger-only: never physically collides with anything.
            Physics.IgnoreLayerCollision(Layers.Interactable, Layers.Player, true);
            Physics.IgnoreLayerCollision(Layers.Interactable, Layers.Enemy, true);
            Physics.IgnoreLayerCollision(Layers.Interactable, defaultLayer, true);

            log.Add("Physics matrix: Player/Enemy collide, Enemy/Enemy collide, Interactable ignores Player/Enemy/Default");
        }

        // -------------------------------------------------------- render pipeline

        static void SetupRenderPipelineAssets(List<string> log)
        {
            foreach (var path in new[] { PcRpAssetPath, MobileRpAssetPath })
            {
                var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (rp == null)
                {
                    log.Add($"RP asset missing: {path}");
                    continue;
                }

                bool changed = false;
                if (rp.colorGradingMode != ColorGradingMode.HighDynamicRange)
                {
                    rp.colorGradingMode = ColorGradingMode.HighDynamicRange;
                    changed = true;
                }
                if (!rp.supportsHDR)
                {
                    rp.supportsHDR = true;
                    changed = true;
                }
                if (changed) EditorUtility.SetDirty(rp);
                log.Add($"{System.IO.Path.GetFileNameWithoutExtension(path)}: HDR grading + HDR ({(changed ? "updated" : "already set")})");
            }
        }

        // ---------------------------------------------------------- volume profile

        static void SetupVolumeProfile(List<string> log)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                log.Add($"Volume profile missing: {ProfilePath}");
                return;
            }

            // Bloom — restrained: only true emissives (torches, telegraphs, weapons) glow.
            var bloom = GetOrAdd<Bloom>(profile);
            bloom.active = true;
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.7f);
            bloom.highQualityFiltering.Override(false);

            // Tonemapping
            var tonemap = GetOrAdd<Tonemapping>(profile);
            tonemap.active = true;
            tonemap.mode.Override(TonemappingMode.ACES);

            // Vignette — heavy, closes in the frame.
            var vignette = GetOrAdd<Vignette>(profile);
            vignette.active = true;
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.5f);

            // Chromatic aberration (driven at runtime by CameraFX)
            var chroma = GetOrAdd<ChromaticAberration>(profile);
            chroma.active = true;
            chroma.intensity.Override(0f);

            // Color adjustments — crushed, desaturated, slightly under-exposed.
            var color = GetOrAdd<ColorAdjustments>(profile);
            color.active = true;
            color.contrast.Override(25f);
            color.saturation.Override(-20f);
            color.postExposure.Override(-0.2f);

            // Film grain
            var grain = GetOrAdd<FilmGrain>(profile);
            grain.active = true;
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.35f);
            grain.response.Override(0.8f);

            // White balance — cold moonlight tint.
            var wb = GetOrAdd<WhiteBalance>(profile);
            wb.active = true;
            wb.temperature.Override(-12f);
            wb.tint.Override(4f);

            foreach (var component in profile.components)
                if (component != null) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);

            log.Add("Volume profile: Bloom 1.0/0.9/0.7, ACES, Vignette 0.45/0.5, ChromaticAberration 0, ColorAdjustments c25/s-20/e-0.2, FilmGrain 0.35, WhiteBalance -12/+4");
        }

        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T existing)) return existing;
            var added = profile.Add<T>(true);
            // Sub-assets must be persisted alongside the profile asset.
            if (AssetDatabase.Contains(profile) && !AssetDatabase.Contains(added))
                AssetDatabase.AddObjectToAsset(added, profile);
            return added;
        }

        // ------------------------------------------------------- renderer features

        static void SetupRendererFeatures(List<string> log)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(PcRendererPath);
            if (renderer == null)
            {
                log.Add($"Renderer data missing: {PcRendererPath}");
                return;
            }

            int disabled = 0;
            foreach (var feature in renderer.rendererFeatures)
            {
                if (feature == null) continue;
                if (feature.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                if (feature.isActive)
                {
                    feature.SetActive(false);
                    EditorUtility.SetDirty(feature);
                    disabled++;
                }
            }
            if (disabled > 0)
            {
                EditorUtility.SetDirty(renderer);
                renderer.SetDirty();
            }
            log.Add($"PC_Renderer: SSAO {(disabled > 0 ? "disabled" : "already off / not present")}");
        }

        // -------------------------------------------------------- scene settings

        static void SetupSceneEnvironment(List<string> log)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                log.Add("No loaded scene: fog/ambient/light skipped.");
                return;
            }

            // Dark fantasy: near-black violet void, dense fog, cold dim moonlight.
            var voidColor = Hex("#06040A");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = voidColor;
            RenderSettings.fogStartDistance = 18f;
            RenderSettings.fogEndDistance = 95f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Hex("#14101E");
            RenderSettings.skybox = null;

            bool lightFound = false;
            foreach (var light in Object.FindObjectsByType<Light>())
            {
                if (light.type != LightType.Directional) continue;
                light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
                light.intensity = 0.55f;
                light.color = Hex("#7F8FB8");
                light.shadows = LightShadows.Soft;
                EditorUtility.SetDirty(light);
                EditorUtility.SetDirty(light.transform);
                lightFound = true;
                break;
            }

            // The gameplay camera lives on the Player prefab; if a main camera is in the scene, match its clear color to the fog.
            bool cameraFound = false;
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.clearFlags = CameraClearFlags.SolidColor;
                mainCam.backgroundColor = voidColor;
                EditorUtility.SetDirty(mainCam);
                cameraFound = true;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            log.Add($"Scene '{scene.name}': linear fog 18-95 #06040A, flat ambient #14101E, no skybox, moonlight directional {(lightFound ? "configured" : "NOT found")}, main camera background {(cameraFound ? "set" : "skipped (none in scene)")}");
        }

        // ---------------------------------------------------------------- utils

        static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }
    }
}
