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
            // Particles/Unlit is resolved by Shader.Find at RUNTIME (DeathMist, PyreMist) and is
            // referenced by no asset, so a player build strips it. The failure mode is not magenta --
            // a ParticleSystemRenderer whose material has a null shader draws NOTHING, silently. The
            // death dissolve and the Pyre mist would simply be absent from a shipped build while
            // looking perfect in the editor. Found by inspection, never by a test: nothing in this
            // project builds a player.
            EnsureAlwaysIncludedShader("Universal Render Pipeline/Particles/Unlit");
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

            // Vignette — heavy, closes in the frame. 0.34 -> 0.27: in FIRST PERSON the platform you are
            // about to land on is at the BOTTOM EDGE of the frame, which is exactly where a vignette
            // crushes hardest. Still clearly a vignette, no longer a footing tax.
            var vignette = GetOrAdd<Vignette>(profile);
            vignette.active = true;
            vignette.intensity.Override(0.27f);
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
            // 0.35 -> 0.26. Grain is signal-destroying at the bottom of the range: a wall sitting at
            // 25/255 and grain swinging +-9 is a wall made of noise. Enough grain left to keep the film
            // texture, not enough to swallow the tonal range the ambient lift just bought.
            grain.intensity.Override(0.26f);
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

            log.Add("Volume profile: Bloom 1.05/0.60/0.62, ACES, Vignette 0.27/0.5, ChromaticAberration 0, ColorAdjustments c20/s-14/e+0.15, FilmGrain 0.26, WhiteBalance +14/+6");
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

        // ---- shipped environment palette (rule 9: asserted by SkyEclipseTests) --------------------
        // The Eclipse pass: every environment colour moved from blue-violet to blood red at MATCHED
        // luminance - hue changed, light level untouched - so enemy readability (the equator's 0.15
        // linear-luminance floor in FeatureTests) is preserved by construction, not by luck.

        /// <summary>Fog and camera clear. Was #0C0912 blue-violet; distance now reads as red haze.</summary>
        public static readonly Color VoidColor = Hex("#1A0708");
        /// <summary>Trilight sky term - platform TOPS. Was #3E4A6B x1.35 (lin lum .130); now .135.</summary>
        public static readonly Color AmbientSky = Hex("#6B4045") * 1.35f;
        /// <summary>Trilight equator - EVERY vertical face and every backlit enemy torso. Was #7A5540
        /// x1.35 (lin lum .209); now .204, still comfortably over FeatureTests' 0.15 floor.</summary>
        public static readonly Color AmbientEquator = Hex("#82503A") * 1.35f;
        /// <summary>Trilight ground bounce. Undersides stay heavy so shapes keep weight.</summary>
        public static readonly Color AmbientGround = Hex("#1F1010") * 1.35f;
        /// <summary>The dying sun behind the arena. Was #C9663A; nudged toward blood.</summary>
        public static readonly Color KeyLightColor = Hex("#C9542E");

        static void SetupSceneEnvironment(List<string> log)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                log.Add("No loaded scene: fog/ambient/light skipped.");
                return;
            }

            // The Eclipse: a world drowned in red under a dead sun, low and enormous behind the arena.
            //
            // FOG. Dark blood-red (was #0C0912 blue-violet) so distance reads as red haze and agrees
            // with the Starfield dome's blood horizon. The 45-240 range is unchanged - fog is not the
            // backdrop, the sky is. Combat distances (3-8m) are still completely unfogged.
            var voidColor = VoidColor;
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
            //
            // THE EQUATOR TERM IS THE WHOLE LEVEL. Trilight lights by NORMAL, so a first-person
            // platformer - walls, pillars, platform risers, enemy torsos, every surface you actually
            // aim at - is lit almost entirely by the equator colour. The first pass had equator
            // #4E3325 at intensity 1 (~0.040 linear luminance); multiplied by a 1% structural albedo
            // that is ~0.0005 linear, i.e. 6/255 after grading - indistinguishable from black once
            // film grain lands on it. Three independent passes each concluded "the frame is black
            // outside the trims" and each blamed the tonemapper; the surfaces were dark going IN.
            // Equator is now ~3.8x brighter and the ground bounce ~3x, which puts an unlit vertical
            // stone face around 25/255 - a readable dark grey that still sits ~100x under the
            // emissive trims, so the eclipse and the neon stay the brightest things in the frame.
            // TRAP: RenderSettings.ambientIntensity IS IGNORED IN TRILIGHT (and Flat) MODE. It only
            // scales Skybox ambient. Setting it to 1.35 here rendered pixel-for-pixel identically to
            // 1.0 - measured, not assumed. The multiplier therefore has to live in the COLOURS, which
            // are HDR: Color * f is the only knob that actually does anything in this mode.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Was cool starlight #3E4A6B x1.35 - under a blood sky, platform tops now catch a dusty
            // rose-red at the same linear luminance (.130 -> .135), so footing legibility is unmoved.
            RenderSettings.ambientSkyColor = AmbientSky;
            // x1.35. The equator carries every vertical face AND every enemy: an enemy walking toward
            // the eclipse is BACKLIT, so the side facing the player receives no key light at all and
            // this term is the only thing rendering it. Tuned by measurement, not by eye: at x2.4 the
            // ground read 53/255 against 23/255 before the pass - a lit room, not a dark one. x1.35
            // lands the ground at ~36/255, a ~1.6x lift that makes structure legible without
            // flattening it, and every emissive is untouched (the gate measured 154.1 -> 155.2 and the
            // alert tell 204 -> 211 across the whole pass).
            // Hue nudged red (#7A5540 -> #82503A) at matched luminance (.209 -> .204): every enemy
            // body renders at the same 8-10/255 it was tuned to, just under a redder cast.
            RenderSettings.ambientEquatorColor = AmbientEquator;
            // Was #191424 violet; now dark maroon at the same near-black weight.
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.ambientIntensity = 1f;   // no-op in Trilight; kept explicit, see above
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
                // 0.85 -> 1.05. The key is the only directional in the scene (URP gives exactly one
                // main light with shadows, and a second directional would eat an additional-light slot
                // on every object near a torch), so it has to carry all of the directional modelling.
                // Raised with the ambient rather than instead of it: ambient alone flattens, because
                // Trilight gives every vertical face the same value regardless of which way it faces.
                light.intensity = 1.05f;
                light.color = KeyLightColor;                  // dying blood-ember sun, not moonlight
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
            log.Add($"Scene '{scene.name}': fog 45-240 #1A0708 (blood), Trilight ambient sky#6B4045x1.35/eq#82503Ax1.35/gnd#1F1010x1.35 (ambientIntensity is a no-op in Trilight), no skybox (Starfield is geometry), low blood-ember sun 1.05 #C9542E {(lightFound ? "configured" : "NOT found")}, main camera background {(cameraFound ? "set" : "skipped (none in scene)")}");
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
