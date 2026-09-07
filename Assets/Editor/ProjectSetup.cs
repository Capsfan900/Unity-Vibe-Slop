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

            // White balance — removes the leftover warm cast now that the palette is cold.
            var wb = GetOrAdd<WhiteBalance>(profile);
            wb.active = true;
            // COLD PASS: was +14/+6, which pushed the whole frame amber-red. The palette itself now
            // carries the blue (ambient, fog, materials, sky), so white balance only needs to remove
            // the leftover warm cast - NOT to add more blue on top. -6 is deliberately small: a heavy
            // negative temperature would also desaturate the amber bolt and the pink-red alert tell,
            // and those two warm tells being saturated against a cold world is the entire readability
            // argument for the pass. Tint 0: no magenta or green push, so bone stays bone.
            wb.temperature.Override(-6f);
            wb.tint.Override(0f);

            foreach (var component in profile.components)
                if (component != null) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);

            log.Add("Volume profile: Bloom 1.05/0.60/0.62, ACES, Vignette 0.27/0.5, ChromaticAberration 0, ColorAdjustments c20/s-14/e+0.15, FilmGrain 0.26, WhiteBalance -6/0");
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
        // THE COLD PASS (2026-09-06, user-directed). Every environment colour moved from blood red to
        // deep cold blue at MATCHED Rec.709 LINEAR LUMINANCE - hue changed, light level untouched -
        // exactly the way the Eclipse pass moved them from blue-violet to red. That is not a stylistic
        // nicety: FeatureTests.Lighting_EquatorLitsVerticals enforces a 0.15 linear-luminance floor on
        // the only term that lights a backlit enemy torso, and every structural albedo was tuned
        // against these three numbers. Matching luminance means enemy readability survives the hue
        // change by CONSTRUCTION, not by luck.
        //
        // WHAT DID NOT GO BLUE, AND WHY. The attack TELLS stay warm: the bolt core (1.6 amber), the
        // alert tell (M_AlertTell, pink-red at 3.0), the enemy cue spark (bone) and the deflect sparks.
        // Warm on cold is the highest-contrast pair in the wheel, so a cold world makes every one of
        // them MORE legible, not less - it is free readability. Fire also keeps its meaning: torches
        // and the checkpoint stay ember, the Dark Souls bonfire read - warm means safety in a cold world.
        // Turning the tells blue with the world would have destroyed the game's readability to satisfy
        // a palette request.

        /// <summary>Camera clear. Was #1A0708 blood; distance now reads as cold blue haze.
        /// Linear luminance 0.0039 - identical to the red it replaces. The sky dome covers the whole
        /// sphere, so this is almost never actually seen; it exists so a missing sky fails to black-blue
        /// rather than to Unity's default cornflower.</summary>
        public static readonly Color VoidColor = Hex("#060D18");

        // ---- FOG: the depth ramp (2026-09-06, graphics-polish item A5) ------------------------------
        //
        // Fog was 45 -> 240 in the SAME near-black as the camera clear, and it did nothing. Level_01's
        // longest sightline is about 90 m (the spawn pad up the causeway to the T1 arena); at 45/240
        // that is 21% fogged, and the 20-60 m band that every traversal read happens in was under 8%.
        // There was no aerial perspective anywhere the player actually looks.
        //
        // THE COLOUR IS THE FIX, NOT THE RANGE. #060D18 is linear luminance .0039 - which is the dome's
        // ZENITH value (#060A17, .0032), not the horizon. The dome's horizon band is #13233F at .0170,
        // 4.3x brighter, and that band is what geometry is read against in a first-person platformer
        // where you look forward and slightly down. So the old fog converged distant geometry to a value
        // DARKER than the sky behind it: a hole punched in the backdrop, extinction rather than haze.
        // FogColor is now the horizon band's own hue at ~0.7 of its value (lin lum .0117), which lands
        // between the zenith and the horizon - the two elevations geometry is actually silhouetted
        // against. This inverts the risk the plan flagged: a distant SHADOWED face (~.009 linear) now
        // gets LIGHTER as it recedes and a distant lit deck top gets slightly darker, which compresses
        // far contrast toward a mid value. That is what aerial perspective is. Nothing goes to black.
        //
        // WHY THE LANDING TARGET IS SAFE. Every jump in Level_01 lands within 12 m - the longest is
        // T3_Entry -> T3_Pillar_1 at 9 m, and the T3 pillar hops are 5-6 m. At a 36 m start the surface
        // you are about to stand on is at fog factor EXACTLY ZERO, always. What the ramp touches is the
        // route AHEAD (the pillar line at 24 m, the span's far end at 48 m, the next arena at 64-90 m),
        // which is preview, not foot placement.
        //
        // THE START FLOOR IS 31 m, NOT 25. The Starfield dome radius is 25, but the eclipse HALO is a
        // flat soft disc of lateral radius discR*2.3 = 19.8 m sitting 24.1 m down the eclipse axis, so
        // its outermost verts are sqrt(24.1^2 + 19.8^2) = 31.2 m from the camera - the widest thing in
        // the sky mesh, and 6 m past the radius everyone quotes. 34 clears the whole mesh with margin,
        // and SkyEclipseTests.TheSkyIsFogImmuneByGeometryNotByAssumption measures the BUILT mesh rather
        // than trusting this comment. Going under ~32 would fog a wedge across the eclipse halo, which
        // is a different and much larger job (the dome would have to be sized to the fog). 36 rather
        // than a bare 32 buys ~4.8 m of headroom, which costs the ramp under 1.5% and means a future
        // widening of the halo trips the test instead of shipping a wedge.
        //
        // THE END IS 170, DOWN FROM 240. Nothing in this level is read past ~90 m - the tiles are walled
        // arenas and the sightlines are bounded. An end of 240 spent only the first 37% of the ramp on
        // the whole level; 170 spends 48%. Resulting factors: 25 m -> 0%, 50 m -> 12%, 64 m -> 22%,
        // 87 m -> 38%, 100 m -> 48%. A distant torch ember (FlickerLight culls the LIGHT at 42 m and
        // leaves the mesh) keeps 62% of its punch at 87 m, so the level's beacons still carry.

        /// <summary>The colour distant geometry converges to: the dome's horizon band #13233F at ~0.7 of
        /// its value. Linear luminance 0.0117 - 3.0x the old fog, 0.69x the horizon band it sits in front
        /// of, and above a shadowed stone face (~0.009), so distance LIGHTENS the dark instead of eating
        /// it. Not the same constant as <see cref="VoidColor"/> on purpose: the clear is the void, the fog
        /// is the sky.</summary>
        public static readonly Color FogColor = Hex("#0E1C34");

        /// <summary>First metre of fog. Must stay clear of the SKY MESH's true outer radius (~31.2 m at
        /// the eclipse halo's corners), not the quoted dome radius of 25. Also far past every landing
        /// target in Level_01 (longest hop 9 m), so fog can never touch the surface you are about to
        /// stand on.</summary>
        public const float FogStartDistance = 36f;

        /// <summary>Full fog. Sized to the level's real depth (~90 m of usable sightline), not to the
        /// far clip - a 240 m end put the entire course inside the ramp's first third.</summary>
        public const float FogEndDistance = 170f;
        /// <summary>Trilight sky term - platform TOPS. Was #6B4045 x1.35 (lin lum .1348); now .1348
        /// exactly, as moonlight instead of dusty rose. Footing legibility is unmoved.</summary>
        public static readonly Color AmbientSky = Hex("#344C78") * 1.35f;
        /// <summary>Trilight equator - EVERY vertical face and every backlit enemy torso. Was #82503A
        /// x1.35 (lin lum .2045); now .2045 exactly, cold slate. Still 1.36x over FeatureTests'
        /// 0.15 floor, which is the number that decides whether an enemy is a readable shape.</summary>
        public static readonly Color AmbientEquator = Hex("#3F5E88") * 1.35f;
        /// <summary>Trilight ground bounce, deep indigo. Was #1F1010 x1.35 (lin lum .0110); now .0110.
        /// Undersides stay heavy so shapes keep weight.</summary>
        public static readonly Color AmbientGround = Hex("#0E1326") * 1.35f;
        /// <summary>The one directional. Was the dying blood sun #C9542E (lin lum .1896); now a pale
        /// cold sun at .1896. Same modelling, same rim on every platform edge, opposite temperature -
        /// and it is now the COMPLEMENT of every warm tell it backlights.</summary>
        public static readonly Color KeyLightColor = Hex("#5A79AD");

        static void SetupSceneEnvironment(List<string> log)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                log.Add("No loaded scene: fog/ambient/light skipped.");
                return;
            }

            // The Eclipse: a world drowned in COLD light under a dead sun, low and enormous behind
            // the arena.
            //
            // FOG. Deep blue haze in the dome's own horizon hue, ramping 36 -> 170 m so the traversal
            // band (20-60 m) and the next-arena read (64-90 m) finally have aerial perspective, while
            // combat (3-8 m) and every landing target (<= 12 m) stay at fog factor zero. The full
            // argument, including why the start floor is 31 m and not the quoted dome radius of 25,
            // is on FogColor / FogStartDistance above. SandboxBuilder reads these same constants.
            var voidColor = VoidColor;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = FogStartDistance;
            RenderSettings.fogEndDistance = FogEndDistance;

            // AMBIENT. This is what actually lights the level - the sky mesh is unlit geometry and
            // contributes no illumination on its own, so the backdrop only "lights the level" if the
            // ambient agrees with it. Trilight is three-way by surface normal and costs nothing:
            //   sky      moonlight from above - lifts platform TOPS, the surfaces you land on
            //   equator  cold eclipse light - lifts VERTICAL faces, which is most of the level
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
            // Was blood #6B4045 x1.35 - under a cold sky, platform tops now catch moonlight at the
            // EXACT same linear luminance (.1348 -> .1348), so footing legibility is unmoved.
            RenderSettings.ambientSkyColor = AmbientSky;
            // x1.35. The equator carries every vertical face AND every enemy: an enemy walking toward
            // the eclipse is BACKLIT, so the side facing the player receives no key light at all and
            // this term is the only thing rendering it. Tuned by measurement, not by eye: at x2.4 the
            // ground read 53/255 against 23/255 before the pass - a lit room, not a dark one. x1.35
            // lands the ground at ~36/255, a ~1.6x lift that makes structure legible without
            // flattening it, and every emissive is untouched (the gate measured 154.1 -> 155.2 and the
            // alert tell 204 -> 211 across the whole pass).
            // Hue moved cold (#82503A -> #3F5E88) at EXACTLY matched luminance (.2045 -> .2045):
            // every enemy body renders at the same 8-10/255 it was tuned to, under a colder cast. A
            // backlit torso is the single hardest read in the game and this term is all it gets.
            RenderSettings.ambientEquatorColor = AmbientEquator;
            // Was #1F1010 maroon; now deep indigo at the same near-black weight (.0110).
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
                light.color = KeyLightColor;                  // pale cold sun: the COMPLEMENT of every warm tell
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
            log.Add($"Scene '{scene.name}': fog 45-240 #060D18 (cold), Trilight ambient sky#344C78x1.35/eq#3F5E88x1.35/gnd#0E1326x1.35 (ambientIntensity is a no-op in Trilight), no skybox (Starfield is geometry), low pale-cold sun 1.05 #5A79AD {(lightFound ? "configured" : "NOT found")}, main camera background {(cameraFound ? "set" : "skipped (none in scene)")}");
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
