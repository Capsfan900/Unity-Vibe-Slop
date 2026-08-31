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
            EnsureAlwaysIncludedShader("Universal Render Pipeline/Unlit");
            // Starfield builds its material at runtime via Shader.Find, so nothing references this
            // shader from an asset and it would otherwise be stripped from player builds.
            EnsureAlwaysIncludedShader("Sprites/Default");
            // Keep play mode ticking when the Editor window loses focus (needed for scripted
            // play-mode verification, and stops the game freezing when you alt-tab).
            PlayerSettings.runInBackground = true;
            // The project shipped with the URP template's identity, which is what the Editor
            // title bar reads from — it showed "com.unity.template.urp-blank". Set here rather
            // than by hand so a fresh clone gets the right name from step 1 of the pipeline.
            PlayerSettings.productName = "vibegame1";
            PlayerSettings.companyName = "vibegame1";

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
            // Sky geometry lives off layer 0 for one reason: NavMeshSurface bakes from RenderMeshes with
            // layerMask 1<<0, and the sky is a 25-unit sphere parented under the Level root. On layer 0 it
            // would be bake input. Nothing raycasts or overlaps against this layer.
            changed |= SetLayer(layers, Starfield.SkyLayer, Starfield.SkyLayerName);

            if (changed) tagManager.ApplyModifiedProperties();
            log.Add($"Layers: {Layers.Player}=Player, {Layers.Enemy}=Enemy, {Layers.Interactable}=Interactable, {Starfield.SkyLayer}={Starfield.SkyLayerName} ({(changed ? "updated" : "already set")})");
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

            // Interactable MUST stay enabled against Player. IgnoreLayerCollision suppresses trigger
            // callbacks as well as physical contacts, so ignoring it silently kills OnTriggerEnter for
            // item pickups, checkpoints and bloodstains. Every Interactable collider is isTrigger, so
            // leaving the pair enabled costs nothing and blocks no movement.
            Physics.IgnoreLayerCollision(Layers.Interactable, Layers.Player, false);
            Physics.IgnoreLayerCollision(Layers.Interactable, Layers.Enemy, true);
            Physics.IgnoreLayerCollision(Layers.Interactable, defaultLayer, true);

            log.Add("Physics matrix: Player/Enemy collide, Enemy/Enemy collide, Interactable triggers vs Player (ignores Enemy/Default)");
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
            // Raised threshold + halved intensity: at 1.0/0.9 every ash trim and enemy bled light and
            // the frame was fatiguing. Now only torches, the ember accent and the parry glow bloom.
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.60f);
            bloom.scatter.Override(0.62f);
            bloom.highQualityFiltering.Override(false);

            // Tonemapping
            var tonemap = GetOrAdd<Tonemapping>(profile);
            tonemap.active = true;
            tonemap.mode.Override(TonemappingMode.ACES);

            // Vignette — heavy, closes in the frame.
            var vignette = GetOrAdd<Vignette>(profile);
            vignette.active = true;
            vignette.intensity.Override(0.34f);
            vignette.smoothness.Override(0.5f);

            // Chromatic aberration (driven at runtime by CameraFX)
            var chroma = GetOrAdd<ChromaticAberration>(profile);
            chroma.active = true;
            chroma.intensity.Override(0f);

            // Color adjustments — crushed, desaturated, slightly under-exposed.
            var color = GetOrAdd<ColorAdjustments>(profile);
            color.active = true;
            color.contrast.Override(20f);
            color.saturation.Override(-14f);
            color.postExposure.Override(0.15f);

            // Film grain
            var grain = GetOrAdd<FilmGrain>(profile);
            grain.active = true;
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.35f);
            grain.response.Override(0.8f);

            // White balance — cold moonlight tint.
            var wb = GetOrAdd<WhiteBalance>(profile);
            wb.active = true;
            // Eclipse light is warm, not moonlit. Positive temperature pushes the whole frame amber-red.
            wb.temperature.Override(14f);
            wb.tint.Override(6f);

            foreach (var component in profile.components)
                if (component != null) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);

            log.Add("Volume profile: Bloom 1.05/0.60/0.62, ACES, Vignette 0.34/0.5, ChromaticAberration 0, ColorAdjustments c20/s-14/e+0.15, FilmGrain 0.35, WhiteBalance +14/+6");
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

            // A deep-space void lit by one dying sun low behind the arena, under the Starfield sky.
            //
            // FOG. Colour moved off near-black (#0A0506) to a dark blue-violet that agrees with the sky
            // dome, so distance now reads as haze rather than as an absence of geometry. The range opened
            // from 32-130 to 45-240 because fog no longer has to be the backdrop - the star field is. At
            // 130 the far half of the course simply vanished, which is most of why the level read as
            // "too dark". Combat distances (3-8m) are still completely unfogged.
            var voidColor = Hex("#0C0912");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = voidColor;
            // Must stay comfortably ABOVE the Starfield radius (25) or the sky itself starts fogging.
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 240f;

            // AMBIENT. This is what actually lights the level - the sky mesh is unlit geometry and
            // contributes no illumination on its own, so the backdrop only "lights the level" if the
            // ambient agrees with it. Trilight is three-way by surface normal and costs nothing:
            //   sky      cool starlight from above - lifts platform TOPS, the surfaces you land on
            //   equator  warm ember from the eclipse - lifts VERTICAL faces, which is most of the level
            //   ground   near-black bounce, so undersides stay heavy and shapes keep their weight
            // Replaces a flat #2E1F18 that lit every face identically and read as flat grey mush.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#2B3654");
            RenderSettings.ambientEquatorColor = Hex("#4E3325");
            RenderSettings.ambientGroundColor = Hex("#0E0B12");
            RenderSettings.ambientIntensity = 1f;
            // No skybox on purpose: the gameplay camera is built with SolidColor clear flags, so a skybox
            // would never be drawn. Starfield is geometry precisely because of that.
            RenderSettings.skybox = null;

            bool lightFound = false;
            foreach (var light in Object.FindObjectsByType<Light>())
            {
                if (light.type != LightType.Directional) continue;
                // Low and from the north (+Z), so the course is backlit by the eclipse the player is
                // walking toward and every platform edge gets a rim. y=180 puts the light's origin behind
                // the boss arena; x=10 keeps it just above the horizon.
                light.transform.rotation = Quaternion.Euler(10f, 180f, 0f);
                light.intensity = 0.85f;
                light.color = Hex("#C9663A");                 // dying ember sun, not moonlight
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
            log.Add($"Scene '{scene.name}': fog 45-240 #0C0912, Trilight ambient sky#2B3654/eq#4E3325/gnd#0E0B12, no skybox (Starfield is geometry), low ember sun {(lightFound ? "configured" : "NOT found")}, main camera background {(cameraFound ? "set" : "skipped (none in scene)")}");
        }

        // ---------------------------------------------------------------- utils

        static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        /// <summary>
        /// Keep a shader in player builds even when no material asset references it. LightningEffect
        /// builds its material at runtime via Shader.Find, which would otherwise be stripped.
        /// </summary>
        static void EnsureAlwaysIncludedShader(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogWarning($"[ProjectSetup] Shader not found: {shaderName}"); return; }

            var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var arr = gs.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;

            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            gs.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

    }
}
