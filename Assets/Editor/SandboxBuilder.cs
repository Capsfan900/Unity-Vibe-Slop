using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// One-shot builder for the sandbox / dev scene (Assets/Scenes/Sandbox.unity): a flat walled arena
    /// for trying combat, weapons, items and enemies without running the campaign course.
    ///
    /// Re-running destroys and rebuilds the "Sandbox" root plus the Player / Managers / HUD instances.
    /// Hand-placed extras belong under a sibling "Sandbox_Manual" root, which is never touched — the
    /// same contract LevelGreyboxBuilder has with "Level_Manual".
    /// </summary>
    public static class SandboxBuilder
    {
        const string MatDir = "Assets/Materials/";
        const string PrefabDir = "Assets/Prefabs/";
        const string ItemsDir = "Assets/Data/Items";
        // Enemy data lives in EnemyPaths (parkour_enemies / souls_enemies) since the 2026-09-06 split.
        const string ScenePath = "Assets/Scenes/Sandbox.unity";
        const string ProfilePath = "Assets/Settings/SampleSceneProfile.asset";

        const string RootName = "Sandbox";
        const string ManualRootName = "Sandbox_Manual";
        /// <summary>The sandbox's own practical light rig. A separate root so ClearGeneratedRoots can
        /// drop it wholesale on a rebuild without going near the scene's directional light, which
        /// EnsureEnvironment resolves BY TYPE and would otherwise re-find as one of these.</summary>
        const string LightsRoot = "Sandbox_Lights";

        // Floor top sits at y = 0; everything is measured from there.
        const float FloorTop = 0f;
        public const float HalfExtent = 30f;   // 60 x 60 arena (public: MovementYardTests reads it)

        static Material mGround, mPlatform, mPink, mCyan, mYellow, mStone, mTorch, mEnemy, mBoss, mWater;
        static GameObject pBalloon;
        static Material mWepSword, mWepHammer, mWepDagger, mWepDev;
        static GameObject pPlayer, pManagers, pHud, pGrunt, pHeavy, pBoss, pItemPickup;
        static GameObject pLegNinja, pLegKnight, pLegSpellsword, pLegMarionette, pLegRevenant, pLegHalberdier, pLegDrillmaster;
        static GameObject pLegBrawler;
        static GameObject pSentryGrunt, pSentryHeavy, pSurgeTurret;

        static int boxCount, trimCount, spawnerCount, torchCount, pickupCount;

        [MenuItem("VibeGame1/7. Build Sandbox Scene")]
        public static void Build()
        {
            // PLAY MODE GUARD: this method destroys the Level root before rebuilding it.
            // In play mode the EditorSceneManager calls throw, leaving the scene wiped and
            // unsaveable - which is exactly how the level was lost once already.
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            {
                UnityEngine.Debug.LogError("["+nameof(SandboxBuilder)+"] Refusing to build during play mode. Exit play mode and try again.");
                return;
            }

            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SandboxBuilder] Cannot build while in play mode. Stop play mode and try again.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[SandboxBuilder] Cancelled (unsaved changes kept).");
                return;
            }

            boxCount = trimCount = spawnerCount = torchCount = pickupCount = 0;
            LoadAssets();

            Scene scene = OpenOrCreateSandboxScene();

            // Clear first: a previous build's torch point-lights would otherwise be candidates when
            // EnsureEnvironment goes looking for the scene's directional light.
            ClearGeneratedRoots();
            EnsureEnvironment();

            var sandbox = new GameObject(RootName);
            SetStatic(sandbox);
            Transform root = sandbox.transform;

            var startSpawn = BuildFloorAndWalls(root);
            BuildPlatformingCorner(root);
            BuildDashGap(root);
            BuildWallRunGauntlet(root);
            var yardSpawn = BuildMovementYard(root);
            BuildEnemyPads(root);
            BuildItemPedestals(root);
            // Wand altar beside the sandbox start, same contract as the level: trigger reaches the
            // spawn point so the loadout is picked before anything else happens.
            WandAltar("WandPedestal_Start", new Vector3(0f, FloorTop, 2.5f), 3f, root);
            BuildWeaponRack(root);
            BuildTorches(root);
            BuildKillZone(root);
            BuildSky(root);

            // ---- Player / Managers / HUD ------------------------------------------------------------
            var player = InstantiatePrefab(pPlayer, "Player", null);
            if (player != null)
            {
                player.transform.position = startSpawn.transform.position;
                player.transform.rotation = startSpawn.transform.rotation;
            }

            var managers = InstantiatePrefab(pManagers, "Managers", null);
            if (managers != null)
            {
                var lm = managers.GetComponentInChildren<LevelManager>(true);
                if (lm != null) lm.startSpawn = startSpawn.transform;
                else Debug.LogWarning("[SandboxBuilder] Managers prefab has no LevelManager; startSpawn not assigned.");
            }

            InstantiatePrefab(pHud, "HUD", null);

            BuildSandboxController(root, yardSpawn);

            // Save first so the scene has a path, then bake (NavMeshData is written beside the scene
            // asset and needs one), then save again to persist the reference.
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            var surface = sandbox.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.layerMask = 1 << 0; // Default only
            surface.BuildNavMesh();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            AddToBuildSettings();

            Debug.Log($"[SandboxBuilder] Built sandbox: {boxCount} boxes, {trimCount} trims, {spawnerCount} spawners, " +
                      $"{torchCount} torches, {pickupCount} pickups, navmesh built={surface.navMeshData != null}, saved '{ScenePath}'.");
        }

        [MenuItem("VibeGame1/Open Sandbox Scene")]
        public static void OpenSandbox()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Debug.LogWarning($"[SandboxBuilder] {ScenePath} does not exist yet. Run 'VibeGame1/7. Build Sandbox Scene' first.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // ---- scene bootstrap -----------------------------------------------------------------------

        static Scene OpenOrCreateSandboxScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path == ScenePath) return active;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            return scene;
        }

        /// <summary>
        /// Directional light, global volume, fog and ambient. Values mirror
        /// ProjectSetup.SetupSceneEnvironment so the sandbox reads like the campaign level; if that
        /// file's palette changes, update these too (or run VibeGame1/1. Project Setup with this scene open).
        /// </summary>
        static void EnsureEnvironment()
        {
            // Must match on type, not just "any Light" — torches are point lights and would be hijacked.
            Light light = null;
            foreach (var candidate in Object.FindObjectsByType<Light>())
            {
                if (candidate == null || candidate.type != LightType.Directional) continue;
                light = candidate;
                break;
            }
            if (light == null)
            {
                var go = new GameObject("Directional Light");
                light = go.AddComponent<Light>();
            }
            light.type = LightType.Directional;
            // Mirrors ProjectSetup: low and from +Z, so the sandbox is backlit by the same eclipse.
            light.transform.rotation = Quaternion.Euler(10f, 180f, 0f);
            // Was the pre-cold-pass warm ember (#C9663A). ProjectSetup's key light is
            // ProjectSetup.KeyLightColor, the pale cold sun #5A79AD — read directly so this can't drift
            // from the campaign's again.
            light.color = ProjectSetup.KeyLightColor;
            light.intensity = 1.05f;
            light.shadows = LightShadows.Soft;

            var volume = Object.FindAnyObjectByType<Volume>();
            if (volume == null)
            {
                var go = new GameObject("Global Volume");
                volume = go.AddComponent<Volume>();
            }
            volume.isGlobal = true;
            volume.priority = 0f;
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null) volume.sharedProfile = profile;
            else Debug.LogWarning($"[SandboxBuilder] Missing volume profile {ProfilePath}; post-processing will differ from the level.");

            // FOG IS NOT MIRRORED BY HAND ANY MORE. It used to be three literals here (#0C0912 violet,
            // 45, 240) beside three different literals in ProjectSetup, and the colours had already
            // drifted apart — the sandbox was still violet after the cold pass took the level blue.
            // The sandbox exists so a value can be judged in here and trusted out there, so it now reads
            // the campaign's constants directly and the drift is structurally impossible.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = ProjectSetup.FogColor;
            RenderSettings.fogStartDistance = ProjectSetup.FogStartDistance;
            RenderSettings.fogEndDistance = ProjectSetup.FogEndDistance;
            // Trilight, matching ProjectSetup: cool starlight above, cold eclipse light on vertical
            // faces, near-black bounce underneath. This is what lifts the scene - the sky mesh is unlit
            // and contributes no illumination by itself. The EQUATOR term is the one that matters:
            // Trilight lights by normal, so every wall, pillar and torso is lit by it alone.
            //
            // Was the pre-cold-pass WARM set (#3E4A6B sky / #7A5540 equator / #191424 ground, each
            // *1.35 by hand here). That drifted from the campaign the same way the old hand-copied fog
            // literals did — an enemy silhouette judged in here did not transfer to the level, which is
            // the sandbox's entire reason to exist. Read ProjectSetup's constants directly, same as
            // fog above, so it can't drift again; the *1.35 multiplier is already baked into them.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // ambientIntensity is a NO-OP in Trilight mode (Unity only applies it to Skybox ambient),
            // so the multipliers live in the colours - same as ProjectSetup.
            RenderSettings.ambientSkyColor = ProjectSetup.AmbientSky;
            RenderSettings.ambientEquatorColor = ProjectSetup.AmbientEquator;
            RenderSettings.ambientGroundColor = ProjectSetup.AmbientGround;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.skybox = null;

            BuildRoomLights();
        }

        /// <summary>
        /// Practical lights for the sandbox, and ONLY for the sandbox.
        ///
        /// <para>The campaign level is lit for atmosphere: one dying sun low behind the course, heavy
        /// fog, and a Trilight ambient whose equator term does most of the work. That is correct there —
        /// the darkness is the art direction, and the log has three separate passes' worth of scar
        /// tissue about not "fixing" it by pushing emissives.</para>
        ///
        /// <para>But the sandbox is a <b>workshop</b>. Its whole job is that you can SEE things: a
        /// wind-up silhouette, an arm pose, where a stagger pose actually puts the body, whether a
        /// marker is on the chest or inside it. At the campaign's light level an enemy on the south pads
        /// is a dark cutout against a dark floor, which is exactly the condition under which every
        /// readability bug in this project has hidden.</para>
        ///
        /// <para>So the room gets its own rig, and it is deliberately conventional: a soft cool key from
        /// high above the arena centre so bodies are separated from the floor, and a warm fill over the
        /// enemy pad row from the player's side so the surface a player actually looks at is lit rather
        /// than backlit. None of it touches the shared ambient, the volume profile, the bloom threshold
        /// or any material — those are the values the campaign's look is made of, and the sandbox
        /// borrowing them unchanged is what keeps it a useful place to judge the campaign.</para>
        ///
        /// <para>Range and intensity are modest on purpose: the point is to lift shapes off the floor,
        /// not to wash the room out. A blown-out sandbox lies in the other direction — a tell that reads
        /// fine under 12 units of fill can still be invisible in the level.</para>
        /// </summary>
        static void BuildRoomLights()
        {
            var old = FindRoot(LightsRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(LightsRoot);

            // Key: high, cool, wide. Separates every body from the floor it stands on.
            Lamp("Sandbox_Key", new Vector3(0f, 16f, -6f), new Color(0.72f, 0.78f, 0.95f), 260f, 46f, root.transform);

            // Fill over the pad row, from the PLAYER'S side (north of the pads) so the faces and torsos
            // the player is reading are lit rather than backlit by the eclipse. Three of them, because
            // the row is 40 m wide and one light at that range falls off into the corners.
            var warm = new Color(1f, 0.82f, 0.62f);
            Lamp("Sandbox_Fill_W", new Vector3(-14f, 7f, -11f), warm, 90f, 24f, root.transform);
            Lamp("Sandbox_Fill_C", new Vector3(4f, 7f, -11f), warm, 90f, 24f, root.transform);
            Lamp("Sandbox_Fill_E", new Vector3(22f, 7f, -11f), warm, 90f, 24f, root.transform);

            // A low, cool bounce at the player spawn so the viewmodel and the wand read on arrival —
            // the first thing anyone checks in here is what is in their hands.
            Lamp("Sandbox_Spawn", new Vector3(0f, 5f, 4f), new Color(0.66f, 0.72f, 0.88f), 40f, 16f, root.transform);

            // The movement yard gets the same cool key, twice, because it is 120 m long. Landings and
            // wall exits are judged by where the floor is, and a floor you cannot see is a yard you
            // cannot tune in. Torches along the kerbs give it warmth; these give it a ground.
            Lamp("Yard_Key_W", new Vector3(62f, 18f, 0f), new Color(0.72f, 0.78f, 0.95f), 260f, 52f, root.transform);
            Lamp("Yard_Key_E", new Vector3(122f, 18f, 0f), new Color(0.72f, 0.78f, 0.95f), 260f, 52f, root.transform);
        }

        /// <summary>One point light. Shadows off: six shadow-casting lights in one room is a frame-rate
        /// bill for a debug scene, and the key light already grounds everything.</summary>
        static void Lamp(string name, Vector3 pos, Color color, float intensity, float range, Transform parent)
        {
            var go = Empty(name, pos, Quaternion.identity, parent);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
        }

        /// <summary>
        /// Destroys every root this builder owns, plus "Level".
        ///
        /// <para>"Level" is LevelGreyboxBuilder's root, and it builds into whatever scene is ACTIVE.
        /// Run "6. Build Level" while Sandbox.unity happens to be open and the campaign course lands
        /// inside the sandbox and gets saved there - which has already happened once, leaving the
        /// arena buried under four tiles of course geometry and a second set of spawners. Clearing it
        /// here means one "7. Build Sandbox Scene" repairs the scene instead of needing hand surgery.
        /// Exact-name match only, so "Level_Manual" and "Sandbox_Manual" are still never touched.</para>
        /// </summary>
        static void ClearGeneratedRoots()
        {
            foreach (var name in new[] { RootName, LightsRoot, "Level", "Player", "Managers", "HUD", "Main Camera" })
            {
                GameObject existing;
                while ((existing = FindRoot(name)) != null) Object.DestroyImmediate(existing);
            }
        }

        static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == ScenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[SandboxBuilder] Added {ScenePath} to build settings (existing entries kept).");
        }

        // ---- sections ------------------------------------------------------------------------------

        /// <summary>
        /// Same deep-space sky as the campaign level, so lighting and materials read identically when
        /// they are tuned in here. See <see cref="Starfield"/> for why it is geometry and not a skybox.
        ///
        /// <para>The radius stays at 25 even though the movement yard now reaches x = 152. The sky is
        /// NOT a dome the geometry has to fit inside: it follows the camera (<see cref="SkyFollower"/>),
        /// draws in the Background queue and writes no depth, so a wall 100 m away still paints over it.
        /// What the radius must do is stay below <c>ProjectSetup.FogStartDistance</c> (36) — enlarging it
        /// to "enclose" the yard would fog the sky and change nothing else. Note the real clearance is
        /// tighter than 34 vs 25 suggests: the eclipse halo is a wide flat disc whose corners sit ~31.2 m
        /// out, so the mesh's true outer radius is 31, not 25.
        /// <c>SkyEclipseTests.TheSkyIsFogImmuneByGeometryNotByAssumption</c> measures it.</para>
        /// </summary>
        static void BuildSky(Transform root)
        {
            var skyGroup = new GameObject("Sky");
            skyGroup.transform.SetParent(root, false);
            // THE ECLIPSE, at the same 22 / 38 the shipped level carries in Level_01_Level.asset.sky.
            // Three copies of this pair exist (Starfield.DefaultEclipse*, SkyDef's initialisers, and the
            // level asset) and the sandbox is the fourth; keep them equal, or the place you go to look
            // at an enemy shows a different sky than the place you fight it.
            Starfield.Build(skyGroup.transform, starCount: 1200, radius: 25f, seed: 20260830,
                            includeEclipse: true, eclipseYawDeg: 0f, eclipsePitchDeg: 22f,
                            eclipseDiameterDeg: 38f);
        }

        static GameObject BuildFloorAndWalls(Transform root)
        {
            Trim(Box("Sandbox_Floor", new Vector3(0f, FloorTop - 0.5f, 0f), new Vector3(HalfExtent * 2f, 1f, HalfExtent * 2f), mPlatform, root), mPink);

            const float wallH = 3f;
            float wy = FloorTop + wallH * 0.5f;
            Box("Wall_N", new Vector3(0f, wy, HalfExtent), new Vector3(HalfExtent * 2f, wallH, 0.5f), mGround, root);
            Box("Wall_S", new Vector3(0f, wy, -HalfExtent), new Vector3(HalfExtent * 2f, wallH, 0.5f), mGround, root);
            Box("Wall_W", new Vector3(-HalfExtent, wy, 0f), new Vector3(0.5f, wallH, HalfExtent * 2f), mGround, root);

            // The east wall has a doorway cut in it: z -3..3, straight east (+X) of the spawn, leading
            // into the movement yard (BuildMovementYard). Two halves rather than one wall with a hole.
            float eastHalfLen = HalfExtent - YardDoorHalfWidth;               // 27 m each side
            float eastHalfZ = YardDoorHalfWidth + eastHalfLen * 0.5f;         // centred at z = +/-16.5
            Box("Wall_E_N", new Vector3(HalfExtent, wy, eastHalfZ), new Vector3(0.5f, wallH, eastHalfLen), mGround, root);
            Box("Wall_E_S", new Vector3(HalfExtent, wy, -eastHalfZ), new Vector3(0.5f, wallH, eastHalfLen), mGround, root);

            return Empty("StartSpawn", new Vector3(0f, FloorTop + 1.2f, 0f), Quaternion.identity, root);
        }

        /// <summary>
        /// North-east staircase for jump/dash tuning. Reachability rule (jump 2.4 m, run 11 m/s):
        /// rise &lt;= 1.5 m with gap &lt;= 4.5 m. Every step here is a 1.5 m rise over a ~1.7 m gap.
        /// </summary>
        static void BuildPlatformingCorner(Transform root)
        {
            Trim(Box("Jump_1", new Vector3(12f, 1.0f, 12f), new Vector3(4f, 1f, 4f), mPlatform, root), mCyan);  // top 1.5
            Trim(Box("Jump_2", new Vector3(16f, 2.5f, 16f), new Vector3(4f, 1f, 4f), mPlatform, root), mCyan);  // top 3.0
            Trim(Box("Jump_3", new Vector3(12f, 4.0f, 20f), new Vector3(4f, 1f, 4f), mPlatform, root), mCyan);  // top 4.5
            Trim(Box("Jump_4", new Vector3(17f, 5.5f, 23f), new Vector3(5f, 1f, 5f), mPlatform, root), mCyan);  // top 6.0
            Trim(Box("Jump_5", new Vector3(22f, 7.0f, 19f), new Vector3(4f, 1f, 4f), mPlatform, root), mCyan);  // top 7.5
        }

        /// <summary>Two level pads 7 m apart edge-to-edge: too far to jump, comfortable with a dash.</summary>
        static void BuildDashGap(Transform root)
        {
            Trim(Box("Dash_A", new Vector3(-14f, 1.0f, 14f), new Vector3(4f, 1f, 4f), mPlatform, root), mYellow);  // top 1.5
            Trim(Box("Dash_B", new Vector3(-14f, 1.0f, 25f), new Vector3(4f, 1f, 4f), mPlatform, root), mYellow);  // top 1.5
        }

        /// <summary>
        /// <b>The wall-run proving ground.</b> Four boxes in the empty north-central strip, laid out so
        /// each part of the mechanic can be exercised and JUDGED separately rather than all at once.
        ///
        /// <para>Deliberately clear of everything else: the enemy pads run along z = -18 (x -28 to 26),
        /// the jump staircase owns x 10-24, the dash pads own x -16 to -12, and the start spawn and wand
        /// altar own z 0-5.5. Everything here lives in x -7.6 to 6.6, z 5.5 to 29.7, which was empty.</para>
        ///
        /// <list type="number">
        ///   <item><b>WallRun_Face</b> - a 14.5 m x 8 m slab. Its EAST side (x 6.6, facing the jump
        ///   staircase) is the practice lane: sprint along the floor beside it, jump, and mount it from
        ///   a standing start with nothing at stake. Its WEST side is the real run.</item>
        ///   <item><b>The gap.</b> WallRun_Launch (top 1.5 m) to WallRun_Landing (top 4.0 m) is 13.5 m
        ///   of separation with a 2.5 m rise, and the landing pad is sheer on every side. A jump leaves
        ///   the ground at 12 m/s and hangs 0.8 s, so it covers under 9 m on the flat and less while
        ///   climbing: LevelArcAnalyzer.AnalyzeHop finds NO arc at groundSpeed and none at slide-jump
        ///   speed, which is the property that makes it a test rather than a decoration. The pad sits
        ///   DOWN THE LINE of the run - a metre past the far end of the face, its east edge 0.4 m off
        ///   the face plane - because the run exit throws you along the wall (+4 m/s tangent, 7 m/s
        ///   out), not off it: a pad offset even 1 m further west is only reachable by leaving the wall
        ///   inside 0.7 s, i.e. by a wall jump wearing a costume. Here the LONGEST arriving route runs
        ///   1.27 s / 13.8 m and leaves at 1.45 s, which is the whole mechanic.</item>
        ///   <item><b>WallRun_Corner</b> - a second, separate run wall 5.4 m west of the landing pad,
        ///   running north-south, its EAST side runnable heading north or south. Leaving the pad
        ///   westward you are pointed straight AT it, and the entry rules correctly refuse that (a face
        ///   you run into is a collision, not a run); a player who steers along it first mounts it.
        ///   That contrast is the thing to judge - whether the refusal reads as a rule or as the game
        ///   ignoring you.</item>
        /// </list>
        ///
        /// <para>Every number here was chosen against LevelArcAnalyzer on the shipped prefab, NOT by eye
        /// (MeasureWallRun: 1.6 s, 13.5 m released / 17.6 m held, +1.25 m then -2.24 m). The first draft
        /// put the landing 8 m west of the line with the corner across the exit, and AnalyzeWallRunGap
        /// proved it mounted the face from 124 entries and arrived from NONE - every exit line ran into
        /// the corner. Three sweeps later this is the analyser's answer (520 / 1800 clean routes);
        /// WallRunGauntletTests holds it. Nobody has run it.</para>
        /// </summary>
        static void BuildWallRunGauntlet(Transform root)
        {
            var group = new GameObject("WallRunGauntlet");
            group.transform.SetParent(root, false);
            SetStatic(group);
            Transform g = group.transform;

            // Take-off deck. Big enough to build up a full sprint along +z before leaving it.
            Trim(Box("WallRun_Launch", new Vector3(2f, 1.0f, 8f), new Vector3(5f, 1f, 5f), mPlatform, g), mCyan);

            // The run wall itself, z 10 to 24.5. 14.5 m long: a released-stick full run covers 14.4 m of
            // face (1.75 s at 0.35/s decay), so the wall ends at the moment the mechanic does and the pad
            // is what comes next.
            Trim(Box("WallRun_Face", new Vector3(6f, 4f, 17.25f), new Vector3(1.2f, 8f, 14.5f), mStone, g), mYellow);

            // Only reachable off the wall. Sheer on all four sides and 4 m up, so there is no walk-up.
            // z 24 to 29, x -1 to 5: a metre past the end of the face, 0.4 m west of its plane.
            Trim(Box("WallRun_Landing", new Vector3(2f, 3.5f, 26.5f), new Vector3(6f, 1f, 5f), mPlatform, g), mCyan);

            // The second run wall: x -7.6 to -6.4, z 18 to 29.7, east face runnable north or south.
            Trim(Box("WallRun_Corner", new Vector3(-7f, 4f, 23.85f), new Vector3(1.2f, 8f, 11.7f), mStone, g), mYellow);
        }

        // ---- the movement yard --------------------------------------------------------------------

        /// <summary>Half-width of the doorway cut in the arena's east wall (z -3..3).</summary>
        public const float YardDoorHalfWidth = 3f;
        /// <summary>Yard floor extent along X. The 2 m between the arena wall (x 30) and here is the doorway floor.</summary>
        public const float YardMinX = 32f;
        public const float YardMaxX = 152f;
        /// <summary>Yard floor half-extent along Z (same as the arena, so the north/south kerbs line up with its walls).</summary>
        public const float YardHalfZ = 30f;
        /// <summary>Where <see cref="SandboxController.WarpToMovementYard"/> puts you: just inside the doorway, facing +X.</summary>
        public static readonly Vector3 YardSpawnPos = new Vector3(36f, 1.2f, 0f);

        /// <summary>What a yard box is FOR. The builder uses it to pick geometry vs marker; the test uses it to pick which contract applies.</summary>
        /// <summary>Water and Balloon are the pivot's traversal pieces: not solids (the tests skip them
        /// in overlap checks), built by <see cref="TraversalBuilders"/>. For a Balloon the box is the
        /// orb's bounding cube (size = 2 x radius) and <c>center</c> its centre.</summary>
        public enum YardKind { Floor, Kerb, Wall, Pad, Step, Tier, Runway, Stripe, Water, Balloon }
        public enum YardMat { None, Ground, Platform, Stone, Pink, Cyan, Yellow }

        /// <summary>
        /// One yard box, as a literal. Pure data (no scene objects), so MovementYardTests can pin the
        /// yard's contract without opening the scene. <c>size</c> is the full world extent, like <see cref="Box"/>.
        /// </summary>
        public struct YardBox
        {
            public string name;
            public Vector3 center, size;
            public YardKind kind;
            public YardMat mat, trim;

            public YardBox(string name, Vector3 center, Vector3 size, YardKind kind, YardMat mat, YardMat trim = YardMat.None)
            {
                this.name = name; this.center = center; this.size = size;
                this.kind = kind; this.mat = mat; this.trim = trim;
            }

            public float MinX { get { return center.x - size.x * 0.5f; } }
            public float MaxX { get { return center.x + size.x * 0.5f; } }
            public float MinZ { get { return center.z - size.z * 0.5f; } }
            public float MaxZ { get { return center.z + size.z * 0.5f; } }
            public float Bottom { get { return center.y - size.y * 0.5f; } }
            public float Top { get { return center.y + size.y * 0.5f; } }
        }

        /// <summary>
        /// <b>The movement yard, as literals.</b> A 120 x 60 m annex east of the arena for tuning wall
        /// running, jumping, sliding, wall exits, landings and air control — room to actually run, which
        /// the 60 x 60 arena with a fight on every side of it does not have.
        ///
        /// <para>This list IS the yard. <see cref="BuildMovementYard"/> only turns it into boxes, and
        /// <c>MovementYardTests</c> asserts the contract against it (gap sizes, step rises, corridor
        /// width, the clear run-off), so a change here is checked before anyone stands on it.</para>
        ///
        /// <para>Layout, all tops relative to the floor at y = 0, X east, Z north:</para>
        /// <list type="bullet">
        ///   <item><b>Doorway</b> x 30..32, z -3..3 — a 2 m floor bridging the arena wall gap.</item>
        ///   <item><b>Yard_Floor</b> x 32..152, z -30..30, 1 m kerbs on the outer edges. A 1.75 m fill
        ///   either side of the doorway closes the strip between the arena wall and the yard floor.</item>
        ///   <item><b>Stripes</b> at every 10 m of x from 40 to 150 (yellow at 50 / 100 / 150) so a
        ///   distance can be read by eye. Markers only: no collider.</item>
        ///   <item><b>Runway</b> x 40..70, z -13..-3, top 4 m, 1.5 m steps at its west end and 30 m of
        ///   nothing east of it — sprint off a ledge, slide-jump off a ledge.</item>
        ///   <item><b>Long walls</b> two 40 x 8 x 1 m walls at z 18 and z 24.5, x 60..100, a 5.5 m
        ///   corridor between: long runs, exits into open floor, wall-to-wall chains.</item>
        ///   <item><b>Gap ladder</b> six 6 x 6 x 2 m pads along z -18 from x 40, gaps 4 / 6 / 8 / 10 /
        ///   12 m, with a 1 m step before the first so the climb starts as a hop.</item>
        ///   <item><b>Drop tower</b> x 126..134, tiers with tops at 3 / 6 / 9 / 12 m ascending north
        ///   from z -27 to z 5. Every tier is reached by a 1.5 m-rise, zero-gap chain of 2 x 2 steps
        ///   (the reachability rule: rise &lt;= 1.5 m with gap &lt;= 4.5 m), and every tier's east face
        ///   drops onto 18 m of flat floor.</item>
        /// </list>
        /// Everything stays clear of z -3..3 from x 32 to 126, so the doorway looks straight down 94 m of
        /// open floor.
        /// </summary>
        public static List<YardBox> YardLayout()
        {
            var L = new List<YardBox>();
            float yardW = YardMaxX - YardMinX;               // 120
            float yardCx = (YardMinX + YardMaxX) * 0.5f;     // 92
            float floorY = FloorTop - 0.5f;

            // ---- floor, doorway, kerbs ------------------------------------------------------------
            L.Add(new YardBox("Yard_Floor", new Vector3(yardCx, floorY, 0f), new Vector3(yardW, 1f, YardHalfZ * 2f),
                              YardKind.Floor, YardMat.Platform, YardMat.Pink));
            L.Add(new YardBox("Yard_Doorway", new Vector3(HalfExtent + 1f, floorY, 0f), new Vector3(YardMinX - HalfExtent, 1f, YardDoorHalfWidth * 2f),
                              YardKind.Floor, YardMat.Platform));

            const float kerbH = 1f, kerbT = 0.5f;
            float kerbY = FloorTop + kerbH * 0.5f;
            L.Add(new YardBox("Yard_Kerb_N", new Vector3(yardCx, kerbY, YardHalfZ), new Vector3(yardW, kerbH, kerbT), YardKind.Kerb, YardMat.Ground));
            L.Add(new YardBox("Yard_Kerb_S", new Vector3(yardCx, kerbY, -YardHalfZ), new Vector3(yardW, kerbH, kerbT), YardKind.Kerb, YardMat.Ground));
            // Butted against the N/S kerbs rather than through them (no overlapping solids).
            L.Add(new YardBox("Yard_Kerb_E", new Vector3(YardMaxX, kerbY, 0f), new Vector3(kerbT, kerbH, YardHalfZ * 2f - kerbT), YardKind.Kerb, YardMat.Ground));
            // The strip between the arena wall (x 30.25) and the yard floor (x 32) is filled solid so
            // nobody steps west off the yard into a 2 m slot. Leaves the doorway (z -3..3) open.
            float fillX0 = HalfExtent + 0.25f, fillW = YardMinX - fillX0, fillCx = fillX0 + fillW * 0.5f;
            float fillLen = YardHalfZ - YardDoorHalfWidth, fillCz = YardDoorHalfWidth + fillLen * 0.5f;
            L.Add(new YardBox("Yard_Kerb_W_N", new Vector3(fillCx, kerbY, fillCz), new Vector3(fillW, kerbH, fillLen), YardKind.Kerb, YardMat.Ground));
            L.Add(new YardBox("Yard_Kerb_W_S", new Vector3(fillCx, kerbY, -fillCz), new Vector3(fillW, kerbH, fillLen), YardKind.Kerb, YardMat.Ground));

            // ---- distance stripes -----------------------------------------------------------------
            const float stripeT = 0.15f;
            for (int x = 40; x <= 150; x += 10)
            {
                bool major = x % 50 == 0;
                L.Add(new YardBox("Yard_Stripe_" + x, new Vector3(x, FloorTop + stripeT * 0.5f, 0f), new Vector3(stripeT, stripeT, YardHalfZ * 2f),
                                  YardKind.Stripe, major ? YardMat.Yellow : YardMat.Cyan));
            }

            // ---- runway ---------------------------------------------------------------------------
            // z -13..-3: south of the doorway line so the straight run east stays open, and south of
            // the long walls so a run exit off Yard_LongWall's south face lands on flat floor, not on this.
            const float runwayCz = -8f;
            L.Add(new YardBox("Yard_Runway_Step1", new Vector3(34f, FloorTop + 0.75f, runwayCz), new Vector3(2f, 1.5f, 4f), YardKind.Step, YardMat.Stone));   // top 1.5
            L.Add(new YardBox("Yard_Runway_Step2", new Vector3(37.5f, FloorTop + 1.5f, runwayCz), new Vector3(2f, 3f, 4f), YardKind.Step, YardMat.Stone));   // top 3.0
            L.Add(new YardBox("Yard_Runway", new Vector3(55f, FloorTop + 2f, runwayCz), new Vector3(30f, 4f, 10f),
                              YardKind.Runway, YardMat.Platform, YardMat.Yellow));                                                                         // top 4.0, x 40..70

            // ---- long walls -----------------------------------------------------------------------
            L.Add(new YardBox("Yard_LongWall", new Vector3(80f, FloorTop + 4f, 18f), new Vector3(40f, 8f, 1f), YardKind.Wall, YardMat.Stone, YardMat.Yellow));
            L.Add(new YardBox("Yard_LongWall_B", new Vector3(80f, FloorTop + 4f, 24.5f), new Vector3(40f, 8f, 1f), YardKind.Wall, YardMat.Stone, YardMat.Yellow));

            // ---- gap ladder -----------------------------------------------------------------------
            const float ladderZ = -18f, padW = 6f, padH = 2f;
            L.Add(new YardBox("Yard_GapLadder_Step", new Vector3(38.5f, FloorTop + 0.5f, ladderZ), new Vector3(3f, 1f, padW), YardKind.Step, YardMat.Stone));
            float padX0 = 40f;
            int[] gaps = { 4, 6, 8, 10, 12 };
            for (int i = 0; i <= gaps.Length; i++)
            {
                L.Add(new YardBox("Yard_GapLadder_" + (i + 1), new Vector3(padX0 + padW * 0.5f, FloorTop + padH * 0.5f, ladderZ), new Vector3(padW, padH, padW),
                                  YardKind.Pad, YardMat.Platform, YardMat.Cyan));
                if (i < gaps.Length) padX0 += padW + gaps[i];
            }

            // ---- drop tower -----------------------------------------------------------------------
            // Tiers ascend NORTH along x 126..134; every east face (x 134) is a straight drop onto the
            // 18 m of floor before the east kerb. The step blocks sit on the west half of each tier,
            // against the next tier's south face, so the east half stays a clean run-off.
            const float towerX = 130f, tierW = 8f, stepW = 2f, stepX = 128f;
            float z0 = -27f;
            for (int i = 0; i < 4; i++)
            {
                float top = 3f * (i + 1);
                float zc = z0 + tierW * 0.5f + tierW * i;
                L.Add(new YardBox("Yard_Tower_" + (i + 1), new Vector3(towerX, FloorTop + top * 0.5f, zc), new Vector3(tierW, top, tierW),
                                  YardKind.Tier, YardMat.Platform, YardMat.Cyan));
                // The step that reaches THIS tier: 1.5 m below its top, touching its south face.
                float stepBase = top - 3f;                                       // the previous tier's top (or the floor)
                float stepZ = z0 + tierW * i - stepW * 0.5f;
                L.Add(new YardBox("Yard_Tower_Step" + (i + 1), new Vector3(stepX, FloorTop + stepBase + 0.75f, stepZ), new Vector3(stepW, 1.5f, stepW),
                                  YardKind.Step, YardMat.Stone));
            }

            // ---- water lane (2026-09-04 pivot) ----------------------------------------------------
            // A 60 x 6 m sheet along z 8..14 flowing EAST at 6 m/s: north of the doorway line (kept
            // open), south of the long walls at z 17.5. Run in from the west, feel the lift to the
            // 14.85 m/s skating floor, slide on it and never slow down, jump off it at the east end.
            L.Add(new YardBox("Yard_Water", new Vector3(70f, FloorTop + WaterThickness * 0.5f, 11f),
                              new Vector3(60f, WaterThickness, 6f), YardKind.Water, YardMat.None));

            // ---- balloon chain ----------------------------------------------------------------------
            // Five orbs you DASH between, not a column you ride up. From play (2026-09-05): the 3 m
            // column was "too small and not placed in a manner where I can dash to one another". Each
            // orb sits BalloonChainStep ahead and BalloonChainRise higher than the last, zig-zagging
            // BalloonChainSway in z so the float window's steer has something to do. The arithmetic
            // MovementYardTests pins: a pop at BalloonLaunch is a v^2/2g rise, the run's 11 m/s carries
            // through the hang, and the re-armed dash (3.5 m) closes what the arc misses.
            // Three orbs stacked 3 m apart over x 110, z -10: north of the gap ladder's sixth pad
            // (z -21..-15), south of the doorway line. A 14 m/s launch is a 3.27 m rise, so each orb is
            // just inside the reach of the launch below it - the chain is meant to be climbed one
            // touch at a time, and a dash through the bottom one re-arms the dash for the next.
            for (int i = 0; i < BalloonChainCount; i++)
            {
                var c = BalloonChainStart + new Vector3(BalloonChainStep * i, BalloonChainRise * i,
                                                       (i % 2 == 0 ? 0f : BalloonChainSway));
                L.Add(new YardBox("Yard_Balloon_" + (i + 1), c, Vector3.one * (BalloonRadius * 2f),
                                  YardKind.Balloon, YardMat.None));
            }

            return L;
        }

        /// <summary>The yard water sheet's thickness; the tests read it to hold the sheet to the floor.</summary>
        public const float WaterThickness = 0.04f;
        /// <summary>Yard water flow, m/s, east.</summary>
        public const float WaterFlowSpeed = 6f;
        /// <summary>The yard balloon chain: where it starts, the step ahead / rise / sway per orb, the count, the
        /// orb radius and the launch speed. All pinned by MovementYardTests.</summary>
        public static readonly Vector3 BalloonChainStart = new Vector3(104f, FloorTop + 3.0f, -10f);
        public const int BalloonChainCount = 5;
        // Re-flown 2026-09-05 with LevelTraversalAnalyzer (the arc the motor actually flies after a pop: 9 m/s
        // carry, 11 m/s launch, the 0.45 s float): about 5 m across and 2-3 m up per orb. The old 5.5 / 1.2
        // predates the carry cap and the float and asked for a flat hop the pop no longer makes.
        public const float BalloonChainStep = 5.0f, BalloonChainRise = 2.4f, BalloonChainSway = 1.5f;
        public const float BalloonRadius = 1.1f, BalloonLaunch = 11f;

        /// <summary>
        /// Builds <see cref="YardLayout"/> under a "MovementYard" group, plus its torches and the
        /// YardSpawn point, and returns that spawn for <see cref="BuildSandboxController"/>.
        /// </summary>
        static GameObject BuildMovementYard(Transform root)
        {
            var group = new GameObject("MovementYard");
            group.transform.SetParent(root, false);
            SetStatic(group);
            Transform g = group.transform;

            foreach (var b in YardLayout())
            {
                if (b.kind == YardKind.Stripe)
                {
                    Stripe(b.name, b.center, b.size, YardMaterial(b.mat), g);
                    continue;
                }
                if (b.kind == YardKind.Water)
                {
                    TraversalBuilders.BuildWater(b.name, b.center, b.size, Vector3.right, WaterFlowSpeed, mWater, g);
                    continue;
                }
                if (b.kind == YardKind.Balloon)
                {
                    TraversalBuilders.BuildBalloon(pBalloon, b.name, b.center, BalloonLaunch, 2.5f, b.size.x * 0.5f, g);
                    continue;
                }
                var box = Box(b.name, b.center, b.size, YardMaterial(b.mat), g);
                if (b.trim != YardMat.None) Trim(box, YardMaterial(b.trim));
            }

            // Torches along the kerbs, alternating sides, none closer than 20 m to another: the point
            // is not to light the yard (the key lamps in BuildRoomLights do that) but to give the
            // 120 m of floor a scale you can read in the fog.
            Torch("Yard_Torch_1", new Vector3(36f, FloorTop, 16f), g);
            Torch("Yard_Torch_2", new Vector3(60f, FloorTop, 28f), g);
            Torch("Yard_Torch_3", new Vector3(70f, FloorTop, -28f), g);
            Torch("Yard_Torch_4", new Vector3(90f, FloorTop, 28f), g);
            Torch("Yard_Torch_5", new Vector3(110f, FloorTop, -28f), g);
            Torch("Yard_Torch_6", new Vector3(130f, FloorTop, 28f), g);
            Torch("Yard_Torch_7", new Vector3(150f, FloorTop, -28f), g);
            Torch("Yard_Torch_8", new Vector3(150f, FloorTop, 10f), g);

            // Facing +X: down the yard.
            return Empty("YardSpawn", YardSpawnPos, Quaternion.Euler(0f, 90f, 0f), g);
        }

        static Material YardMaterial(YardMat m)
        {
            switch (m)
            {
                case YardMat.Ground: return mGround;
                case YardMat.Platform: return mPlatform;
                case YardMat.Stone: return mStone;
                case YardMat.Pink: return mPink;
                case YardMat.Cyan: return mCyan;
                case YardMat.Yellow: return mYellow;
                default: return null;
            }
        }

        /// <summary>A marker bar: a <see cref="Box"/> with its collider removed, same construction as a <see cref="Trim"/> bar.</summary>
        static GameObject Stripe(string name, Vector3 center, Vector3 size, Material m, Transform parent)
        {
            var go = Box(name, center, size, m, parent);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            boxCount--;
            trimCount++;
            return go;
        }

        /// <summary>
        /// Spawn pads along the south wall, with live spawners for Grunt / Heavy / Boss and the six
        /// legendary mini-bosses (three campaign duellists, three sandbox-only prototypes).
        ///
        /// <para>The row used to end at the Boss pad with two unused "spare" pads at x 18 / 24. Three
        /// legendaries need three slots, so the eastern half of the row is re-spaced rather than
        /// crammed: 4.5 m pads at x 16 / 21 / 26, which keeps 1.75 m clear of the boss pad and 1.5 m
        /// clear of the east wall at x = 30.</para>
        ///
        /// <para>Every pad sits at z = -18 and every occupant is 22 m or more from the player spawn at
        /// the origin - further than any of their aggro ranges (the legendaries top out at 18) - so the
        /// arena is quiet on load and you walk to the fight you want.</para>
        ///
        /// <para>Names mirror the prefab and EnemyData names exactly (Spawn_Legendary_Ninja ->
        /// Legendary_Ninja.prefab). The campaign level's Spawn_GruntA -> Spawn_T1_GruntA rename is the
        /// reason DebugHarness has a suffix fallback at all; don't introduce a second dialect here.</para>
        /// </summary>
        static void BuildEnemyPads(Transform root)
        {
            const float z = -18f;
            const float padY = FloorTop - 0.4f;   // 0.1 m proud of the floor: visible, trivially walkable
            const float spawnY = FloorTop + 0.3f;

            Box("Pad_Grunt", new Vector3(-14f, padY, z), new Vector3(4f, 1f, 4f), mEnemy, root);
            Box("Pad_Heavy", new Vector3(-5f, padY, z), new Vector3(5f, 1f, 5f), mEnemy, root);
            Box("Pad_Boss", new Vector3(8f, padY, z), new Vector3(8f, 1f, 8f), mBoss, root);

            // M_Boss for the legendaries, matching MiniBossFactory's body material: the pad reads
            // "this one is a duel" before you are close enough to see the silhouette.
            Box("Pad_Legendary_Ninja", new Vector3(16f, padY, z), new Vector3(4.5f, 1f, 4.5f), mBoss, root);
            Box("Pad_Legendary_Knight", new Vector3(21f, padY, z), new Vector3(4.5f, 1f, 4.5f), mBoss, root);
            Box("Pad_Legendary_Spellsword", new Vector3(26f, padY, z), new Vector3(4.5f, 1f, 4.5f), mBoss, root);

            // The Pale Marionette goes at the WEST end of the row, not on it. The eastern half is
            // already at its limit (the Spellsword pad's edge is 1.5 m off the x = 30 wall), and a
            // fourth 4.5 m pad squeezed in there would have the two duellists inside each other's
            // 18 m aggro. West of the Grunt pad there is 14 m of empty floor: x = -22 leaves 1.75 m
            // clear of the Grunt pad, 5.75 m clear of the west wall, and ~8 m clear of the item
            // pedestal column (which runs down x = -22 but stops around z = -10). It is 28.4 m from
            // the player spawn, comfortably outside its own 18 m aggro, so the arena stays quiet.
            Box("Pad_Legendary_Marionette", new Vector3(-22f, padY, z), new Vector3(4.5f, 1f, 4.5f), mBoss, root);

            // The Ember Revenant goes WEST of the Marionette, continuing the same reasoning: x = -28
            // leaves 1.5 m clear of the Marionette pad and 2 m clear of the west wall at x = -32. Its
            // aggro is 20 m, so it is placed 6 m from the Marionette on purpose -- both are woken by
            // their own switch, and two duellists that pull each other is a sandbox that cannot be used
            // to look at either of them.
            Box("Pad_Legendary_Revenant", new Vector3(-28f, padY, z), new Vector3(4.5f, 1f, 4.5f), mBoss, root);

            // The Argent Halberdier takes the one gap left in the row: between the Heavy pad (edge at
            // x -2.5) and the Warden's (edge at x 4). A 4.5 m pad at x 0.75 spans -1.5..3.0, one metre
            // clear of each neighbour. West of the Revenant there is no room (its pad is already 2 m
            // off the wall) and the eastern run is full. It is 18 m from the player spawn; like every
            // pad enemy it sleeps behind its wake switch, so the arena is still quiet on load.
            Box("Pad_Legendary_Halberdier", new Vector3(0.75f, padY, z), new Vector3(4.5f, 1f, 4.5f), mBoss, root);

            // Enemies sit south of the player, so they face +Z (identity), unlike the campaign level.
            var sGrunt = Spawner("Spawn_Grunt", new Vector3(-14f, spawnY, z), pGrunt, false, root);
            var sHeavy = Spawner("Spawn_Heavy", new Vector3(-5f, spawnY, z), pHeavy, false, root);
            var sBoss = Spawner("Spawn_Boss", new Vector3(8f, spawnY, z), pBoss, true, root);

            // isBoss stays false: the legendaries are plain EnemyControllers, not the Warden, so they
            // respawn and reset exactly like a grunt does and need no arena trigger to wake. The flag
            // is only read for the spawner gizmo colour and by tooling that wants "an ordinary enemy".
            var sNinja = Spawner("Spawn_Legendary_Ninja", new Vector3(16f, spawnY, z), pLegNinja, false, root);
            var sKnight = Spawner("Spawn_Legendary_Knight", new Vector3(21f, spawnY, z), pLegKnight, false, root);
            var sSpell = Spawner("Spawn_Legendary_Spellsword", new Vector3(26f, spawnY, z), pLegSpellsword, false, root);
            var sMarionette = Spawner("Spawn_Legendary_Marionette", new Vector3(-22f, spawnY, z), pLegMarionette, false, root);
            var sRevenant = Spawner("Spawn_Legendary_Revenant", new Vector3(-28f, spawnY, z), pLegRevenant, false, root);
            var sHalberdier = Spawner("Spawn_Legendary_Halberdier", new Vector3(0.75f, spawnY, z), pLegHalberdier, false, root);
            // THE DRILLMASTER (2026-09-06): the soulslike-combat showcase. The southern row is full, so it
            // takes a SECOND ROW at z -26 behind the Ninja pad (x 14: clear of the Warden's pad, x <= 12,
            // and inside the wall). Its switch sits between the rows on the player's side.
            Box("Pad_Legendary_Drillmaster", new Vector3(14f, padY, z - 8f), new Vector3(4.5f, 1f, 4.5f), mBoss, root);
            var sDrill = Spawner("Spawn_Legendary_Drillmaster", new Vector3(14f, spawnY, z - 8f), pLegDrillmaster, false, root);
            // THE FLURRY BRAWLER (2026-09-07): the roster's first flurry enemy, and the fourth forge
            // prototype. The southern row is full and its west end is at the wall (the Revenant's pad
            // at x -28 is already 2 m off it), so it takes the WEST END OF THE SECOND ROW: x -22,
            // z -26. That is 5.75 m clear of the sentry pad at x -14 and 5.75 m clear of the west
            // wall, it is 34 m from the player spawn -- more than twice its 16 m aggro, so the arena
            // is quiet on load -- and it is the only free spot where a 16 m aggro cannot reach another
            // duellist's pad once woken. Its switch sits between the rows, on the player's side.
            Box("Pad_Legendary_FlurryBrawler", new Vector3(-22f, padY, z - 8f), new Vector3(4.5f, 1f, 4.5f), mBoss, root);
            var sBrawler = Spawner("Spawn_Legendary_FlurryBrawler", new Vector3(-22f, spawnY, z - 8f), pLegBrawler, false, root);
            // parkour_enemies: one SENTRY on the second row behind the Grunt pad, so a bolt, a deflect boost
            // and the sentry dash can be studied without leaving the arena. The pill Grunt/Heavy pads in
            // the front row stay MELEE (the user's "little pill guys for combat").
            Box("Pad_pshooter_enemy01", new Vector3(-14f, padY, z - 8f), new Vector3(4f, 1f, 4f), mEnemy, root);
            var sSentry = Spawner("Spawn_pshooter_enemy01", new Vector3(-14f, spawnY, z - 8f), pSentryGrunt, false, root);
            // pshooter_enemy03: a ROW of three surge turrets on the same second row. See turretX below.
            var sTurrets = new GameObject[turretX.Length];
            for (int i = 0; i < turretX.Length; i++)
            {
                Box("Pad_pshooter_enemy03_" + (i + 1), new Vector3(turretX[i], padY, z - 8f), new Vector3(3f, 1f, 3f), mEnemy, root);
                sTurrets[i] = Spawner("Spawn_pshooter_enemy03_" + (i + 1), new Vector3(turretX[i], spawnY, z - 8f), pSurgeTurret, false, root);
            }

            // ---- one WAKE switch per pad, on the player's side of it ------------------------------
            // The sandbox is a workshop, not a fight. With default aggro, stepping off the spawn pad
            // starts three fights at once and nothing can be studied — you cannot read a wind-up, time
            // a parry or photograph a stagger with two other things swinging at your back. Every pad's
            // enemy now sleeps behind EnemyController.aggroLocked (the same flag the Warden uses) and
            // these are the triggers, one each. Placed 3.2 m north of the pad so the player meets the
            // switch before the enemy.
            const float switchZ = z + 3.2f;
            Switch("Wake_Grunt", new Vector3(-14f, FloorTop, switchZ), sGrunt, "GRUNT", root);
            Switch("Wake_Heavy", new Vector3(-5f, FloorTop, switchZ), sHeavy, "HEAVY", root);
            Switch("Wake_Boss", new Vector3(8f, FloorTop, switchZ - 1.6f), sBoss, "WARDEN", root);
            Switch("Wake_Legendary_Ninja", new Vector3(16f, FloorTop, switchZ), sNinja, "NIGHTJAR", root);
            Switch("Wake_Legendary_Knight", new Vector3(21f, FloorTop, switchZ), sKnight, "IRON PENITENT", root);
            Switch("Wake_Legendary_Spellsword", new Vector3(26f, FloorTop, switchZ), sSpell, "ASHEN CHORISTER", root);
            Switch("Wake_Legendary_Marionette", new Vector3(-22f, FloorTop, switchZ), sMarionette, "PALE MARIONETTE", root);
            Switch("Wake_Legendary_Revenant", new Vector3(-28f, FloorTop, switchZ), sRevenant, "EMBER REVENANT", root);
            Switch("Wake_Legendary_Halberdier", new Vector3(0.75f, FloorTop, switchZ), sHalberdier, "ARGENT HALBERDIER", root);
            Switch("Wake_Legendary_Drillmaster", new Vector3(14f, FloorTop, z - 8f + 3.2f), sDrill, "DRILLMASTER", root);
            Switch("Wake_Legendary_FlurryBrawler", new Vector3(-22f, FloorTop, z - 8f + 3.2f), sBrawler, "FLURRY BRAWLER", root);
            Switch("Wake_pshooter_enemy01", new Vector3(-14f, FloorTop, z - 8f + 3.2f), sSentry, "SENTRY", root);
            for (int i = 0; i < turretX.Length; i++)
                Switch("Wake_pshooter_enemy03_" + (i + 1), new Vector3(turretX[i], FloorTop, z - 8f + 3.2f),
                       sTurrets[i], "TURRET " + (i + 1), root);
        }

        /// <summary>
        /// THE SURGE TURRET ROW (2026-09-06). Three of them, not one, because ONE turret cannot show what
        /// this enemy is: the whole design is the LADDER a row pays out (x1.12, x1.24, x1.36 on the HUD
        /// status strip), and a single pad would only ever prove the first rung. In the campaign they live
        /// on a long descending ramp; the sandbox is flat, so three in a line at 5 m spacing is the nearest
        /// honest rehearsal — parry, run, parry, run, then stand still and watch the strip walk back down.
        ///
        /// <para>The second row (z -26) between the Sentry pad (x -14, edge -12) and the Warden's second-row
        /// neighbour the Drillmaster (x 14, edge 11.75). x -8, -3 and 2 with 3 m pads leaves 2.5 m clear of
        /// the Sentry and 8.25 m clear of the Drillmaster, and every one of them sleeps behind its own wake
        /// switch like every other pad, so the arena is still quiet on load.</para>
        /// </summary>
        static readonly float[] turretX = { -8f, -3f, 2f };

        /// <summary>
        /// A wake switch: stone post on Default (walkable, bakes) with a small emissive lamp on
        /// Interactable carrying the trigger, exactly like <see cref="WandAltar"/>'s split. The lamp is
        /// what the player aims at and it doubles as the pad's status light — lit means asleep.
        ///
        /// <para>Rule 9: every serialized value on <see cref="SandboxEnemySwitch"/> is written here.</para>
        /// </summary>
        static GameObject Switch(string name, Vector3 groundPos, GameObject spawner, string label, Transform parent)
        {
            Box(name + "_Post", groundPos + Vector3.up * 0.45f, new Vector3(0.28f, 0.9f, 0.28f), mStone, parent);

            var lamp = Box(name, groundPos + Vector3.up * 1.05f, new Vector3(0.34f, 0.34f, 0.34f), mCyan, parent, false);
            lamp.layer = Layers.Interactable;

            // The box collider that ships with the primitive stays as the visible lamp; the RANGE
            // trigger is a separate sphere on the same object, matching the altar. Walking past must
            // never wake anything — the trigger is a range check and the press is the verb.
            var solid = lamp.GetComponent<Collider>();
            if (solid != null) Object.DestroyImmediate(solid);
            var trigger = lamp.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 7f;      // in the lamp's own 0.34 scale: ~2.4 m of world reach

            var sw = lamp.AddComponent<SandboxEnemySwitch>();
            sw.spawner = spawner != null ? spawner.GetComponent<EnemySpawner>() : null;
            sw.lamp = lamp.GetComponent<Renderer>();
            sw.enemyName = label;
            sw.lookDot = 0.86f;
            return lamp;
        }

        /// <summary>One pedestal per ItemData in Assets/Data/Items, along the west side.</summary>
        static void BuildItemPedestals(Transform root)
        {
            var group = new GameObject("Pickups");
            group.transform.SetParent(root, false);

            if (pItemPickup == null)
            {
                Debug.LogWarning("[SandboxBuilder] ItemPickup prefab missing; no item pedestals placed.");
                return;
            }
            if (!AssetDatabase.IsValidFolder(ItemsDir))
            {
                Debug.LogWarning($"[SandboxBuilder] {ItemsDir} does not exist (run DataFactory first); no item pedestals placed.");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:ItemData", new[] { ItemsDir });
            if (guids.Length == 0)
            {
                Debug.LogWarning($"[SandboxBuilder] No ItemData assets in {ItemsDir}; no item pedestals placed.");
                return;
            }

            const float x = -22f;
            float startZ = -(guids.Length - 1) * 2.5f;   // 5 m apart, centred on z = 0

            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var data = AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (data == null) continue;

                float z = startZ + i * 5f;
                Box($"Pedestal_{data.name}", new Vector3(x, FloorTop + 0.5f, z), new Vector3(1.6f, 1f, 1.6f), mStone, group.transform);

                var go = InstantiatePrefab(pItemPickup, "Pickup_" + data.name, group.transform);
                if (go == null) continue;
                go.transform.position = new Vector3(x, FloorTop + 2.2f, z);   // pedestal top + 1.2, matching the level
                go.transform.rotation = Quaternion.identity;

                var pickup = go.GetComponent<ItemPickup>();
                if (pickup != null) pickup.item = data;
                else Debug.LogWarning($"[SandboxBuilder] Pickup_{data.name} has no ItemPickup component.");

                pickupCount++;
            }
        }

        /// <summary>
        /// Four marker pads along the east side, colour-keyed to each weapon's material. Purely visual —
        /// weapons are swapped with keys 1-4.
        /// </summary>
        static void BuildWeaponRack(Transform root)
        {
            var group = new GameObject("WeaponRack");
            group.transform.SetParent(root, false);

            var entries = new (string name, Material mat)[]
            {
                ("Sword", mWepSword),
                ("Hammer", mWepHammer),
                ("Dagger", mWepDagger),
                ("DevBlade", mWepDev),
            };

            const float x = 22f;
            for (int i = 0; i < entries.Length; i++)
            {
                float z = -9f + i * 6f;
                Box($"WeaponPad_{entries[i].name}", new Vector3(x, FloorTop + 0.5f, z), new Vector3(3f, 1f, 3f), mStone, group.transform);
                // Post carries the weapon's own material so the rack reads at a glance.
                Box($"WeaponPost_{entries[i].name}", new Vector3(x, FloorTop + 2.1f, z), new Vector3(0.35f, 2.2f, 0.35f), entries[i].mat, group.transform);
            }
        }

        static void BuildTorches(Transform root)
        {
            var group = new GameObject("Torches");
            group.transform.SetParent(root, false);
            Transform t = group.transform;

            const float c = 26f;
            Torch("Torch_SW", new Vector3(-c, FloorTop, -c), t);
            Torch("Torch_SE", new Vector3(c, FloorTop, -c), t);
            Torch("Torch_NW", new Vector3(-c, FloorTop, c), t);
            Torch("Torch_NE", new Vector3(c, FloorTop, c), t);
            Torch("Torch_N", new Vector3(0f, FloorTop, c), t);
            Torch("Torch_S", new Vector3(0f, FloorTop, -c), t);
            // Off the z = 0 line: that is now the walk from the spawn to the yard doorway, and a torch
            // post (no collider) standing in it reads as a mistake even though you pass through it.
            Torch("Torch_E", new Vector3(c, FloorTop, 6f), t);
            Torch("Torch_W", new Vector3(-c, FloorTop, 0f), t);
        }

        static void BuildKillZone(Transform root)
        {
            // Covers the arena AND the movement yard with margin: x -40..165, z -40..40.
            var kill = Empty("KillZone", new Vector3(62.5f, -25f, 0f), Quaternion.identity, root);
            var col = kill.AddComponent<BoxCollider>();
            col.size = new Vector3(205f, 2f, 80f);
            col.isTrigger = true;
            kill.AddComponent<KillZone>();
        }

        static void BuildSandboxController(Transform root, GameObject yardSpawn)
        {
            var go = new GameObject("SandboxController");
            go.transform.SetParent(root, false);
            var controller = go.AddComponent<SandboxController>();

            // WarpToMovementYard() teleports here: just inside the yard doorway, facing down the yard.
            controller.yardSpawn = yardSpawn != null ? yardSpawn.transform : null;

            // Rule 9: a field initialiser does nothing to a component already serialized in the scene, so
            // the shipped respawn values are written here. Practising a fight should not mean walking
            // back to a menu — pad enemies come back a few seconds after they die.
            controller.autoRespawnPadEnemies = true;
            controller.respawnDelay = 4f;

            // APPEND ONLY. The documented indices (0 Grunt, 1 Heavy, 2 Boss) are in README_Sandbox.md
            // and in muscle memory; renumbering silently changes what SpawnEnemyInFront(2) drops.
            controller.enemyPrefabs = new[] { pGrunt, pHeavy, pBoss, pLegNinja, pLegKnight, pLegSpellsword,
                                              pLegMarionette, pLegRevenant, pLegHalberdier, pLegDrillmaster,
                                              pSentryGrunt, pSentryHeavy, pSurgeTurret, pLegBrawler };
            controller.spawnableEnemies = new[]
            {
                LoadEnemyData("Grunt"),
                LoadEnemyData("Heavy"),
                LoadEnemyData("Boss"),
                LoadEnemyData("Legendary_Ninja"),
                LoadEnemyData("Legendary_Knight"),
                LoadEnemyData("Legendary_Spellsword"),
                LoadEnemyData("Legendary_Marionette"),
                LoadEnemyData("Legendary_Revenant"),
                LoadEnemyData("Legendary_Halberdier"),
                LoadEnemyData("Legendary_Drillmaster"),
                LoadEnemyData("pshooter_enemy01"),
                LoadEnemyData("pshooter_enemy02"),
                LoadEnemyData("pshooter_enemy03"),
                LoadEnemyData("Legendary_FlurryBrawler"),
            };
            controller.dummyPrefabIndex = 0;   // Grunt
        }

        static EnemyData LoadEnemyData(string name)
        {
            return AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(name));
        }

        // ---- helpers -------------------------------------------------------------------------------

        static void LoadAssets()
        {
            mGround = LoadMat("M_Ground");
            mPlatform = LoadMat("M_Platform");
            mPink = LoadMat("M_NeonPink");
            mCyan = LoadMat("M_NeonCyan");
            mYellow = LoadMat("M_NeonYellow");
            mStone = LoadMat("M_Stone");
            mTorch = LoadMat("M_Torch");
            mEnemy = LoadMat("M_Enemy");
            mBoss = LoadMat("M_Boss");
            mWater = LoadMat("M_Water");
            mWepSword = LoadMat("M_Weapon_Sword");
            mWepHammer = LoadMat("M_Weapon_Hammer");
            mWepDagger = LoadMat("M_Weapon_Dagger");
            mWepDev = LoadMat("M_Weapon_Dev");

            pPlayer = LoadPrefab("Player");
            pManagers = LoadPrefab("Managers");
            pHud = LoadPrefab("HUD");
            pGrunt = LoadPrefab("Enemy_Grunt");
            pHeavy = LoadPrefab("Enemy_Heavy");
            pBoss = LoadPrefab("Boss");
            pItemPickup = LoadPrefab("ItemPickup");

            // Legendary mini-bosses (Assets/Editor/MiniBossFactory.cs). Missing ones only warn, like
            // every other prefab here - the arena still builds, just without those pads populated.
            pLegNinja = LoadPrefab("Legendary_Ninja");
            pLegKnight = LoadPrefab("Legendary_Knight");
            pLegSpellsword = LoadPrefab("Legendary_Spellsword");
            pLegMarionette = LoadPrefab("Legendary_Marionette");
            pLegRevenant = LoadPrefab("Legendary_Revenant");
            pLegHalberdier = LoadPrefab("Legendary_Halberdier");
            pLegDrillmaster = LoadPrefab("Legendary_Drillmaster");
            pLegBrawler = LoadPrefab("Legendary_FlurryBrawler");
            pSentryGrunt = LoadPrefab("pshooter_enemy01");
            pSentryHeavy = LoadPrefab("pshooter_enemy02");
            pSurgeTurret = LoadPrefab("pshooter_enemy03");
            pBalloon = LoadPrefab("Balloon");
        }

        static Material LoadMat(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + name + ".mat");
            if (m == null) Debug.LogWarning($"[SandboxBuilder] Missing material {MatDir}{name}.mat (run the material setup first).");
            return m;
        }

        static GameObject LoadPrefab(string name)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + name + ".prefab");
            if (p == null) Debug.LogWarning($"[SandboxBuilder] Missing prefab {PrefabDir}{name}.prefab (run PrefabFactory/HudBuilder first).");
            return p;
        }

        static Color Hex(string hex, Color fallback) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;

        static GameObject FindRoot(string name)
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }

        static void SetStatic(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        static GameObject Box(string name, Vector3 center, Vector3 size, Material m, Transform parent, bool isStatic = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.layer = 0;
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            if (isStatic) SetStatic(go);
            boxCount++;
            return go;
        }

        /// <summary>Four thin neon bars along the top edges of a box-shaped platform.</summary>
        static void Trim(GameObject platform, Material m)
        {
            const float t = 0.15f;
            Vector3 c = platform.transform.position;
            Vector3 s = platform.transform.localScale;
            float top = c.y + s.y * 0.5f + t * 0.5f;
            float hx = s.x * 0.5f - t * 0.5f;
            float hz = s.z * 0.5f - t * 0.5f;
            Transform parent = platform.transform.parent;

            var bars = new (string suffix, Vector3 pos, Vector3 size)[]
            {
                ("_Trim_N", new Vector3(c.x, top, c.z + hz), new Vector3(s.x, t, t)),
                ("_Trim_S", new Vector3(c.x, top, c.z - hz), new Vector3(s.x, t, t)),
                ("_Trim_E", new Vector3(c.x + hx, top, c.z), new Vector3(t, t, s.z)),
                ("_Trim_W", new Vector3(c.x - hx, top, c.z), new Vector3(t, t, s.z)),
            };

            foreach (var b in bars)
            {
                // Built as a sibling first (world-space scale), then parented so the non-uniform parent
                // scale does not distort it.
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = platform.name + b.suffix;
                bar.transform.SetParent(parent, false);
                bar.transform.position = b.pos;
                bar.transform.localScale = b.size;
                bar.layer = 0;
                if (m != null) bar.GetComponent<Renderer>().sharedMaterial = m;
                Object.DestroyImmediate(bar.GetComponent<Collider>());
                GameObjectUtility.SetStaticEditorFlags(bar, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
                bar.transform.SetParent(platform.transform, true);
                trimCount++;
            }
        }

        /// <summary>Stone post + ember cube + flickering point light. basePos is the floor surface.</summary>
        static GameObject Torch(string name, Vector3 basePos, Transform parent)
        {
            var torch = new GameObject(name);
            torch.transform.SetParent(parent, false);
            torch.transform.position = basePos;

            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Post";
            Object.DestroyImmediate(post.GetComponent<Collider>());
            post.transform.SetParent(torch.transform, false);
            post.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            post.transform.localScale = new Vector3(0.25f, 1.6f, 0.25f);
            if (mStone != null) post.GetComponent<Renderer>().sharedMaterial = mStone;

            var ember = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ember.name = "Ember";
            Object.DestroyImmediate(ember.GetComponent<Collider>());
            ember.transform.SetParent(torch.transform, false);
            ember.transform.localPosition = new Vector3(0f, 1.75f, 0f);
            ember.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
            var emberRenderer = ember.GetComponent<Renderer>();
            emberRenderer.shadowCastingMode = ShadowCastingMode.Off;
            if (mTorch != null) emberRenderer.sharedMaterial = mTorch;

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(torch.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Hex("#FF8A2A", new Color(1f, 0.54f, 0.16f));
            light.range = 9f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            var flicker = lightGo.AddComponent<FlickerLight>();
            flicker.baseIntensity = 2.5f;
            flicker.amplitude = 0.9f;
            flicker.speed = 9f;

            torchCount++;
            return torch;
        }

        static GameObject Empty(string name, Vector3 pos, Quaternion rot, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = rot;
            return go;
        }

        /// <summary>
        /// The wand altar: stone plinth on Default (walkable geometry, bakes) plus a floating crystal on
        /// Interactable carrying the trigger. Mirrors LevelGreyboxBuilder.WandAltar so both scenes get an
        /// identical pedestal. <paramref name="groundPos"/> is the floor surface it stands on.
        /// </summary>
        static GameObject WandAltar(string name, Vector3 groundPos, float triggerRadius, Transform parent)
        {
            Box(name + "_Plinth", groundPos + Vector3.up * 0.4f, new Vector3(1.8f, 0.8f, 1.8f), mStone, parent);

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = groundPos;

            var sc = root.AddComponent<SphereCollider>();
            sc.center = new Vector3(0f, 1.2f, 0f);
            sc.radius = triggerRadius;
            sc.isTrigger = true;

            var pedestal = root.AddComponent<WandPedestal>();

            var visual = Empty("Visual", groundPos + Vector3.up * 1.5f, Quaternion.identity, root.transform);
            pedestal.visual = visual.transform;

            var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crystal.name = "Crystal";
            Object.DestroyImmediate(crystal.GetComponent<Collider>());
            crystal.transform.SetParent(visual.transform, false);
            crystal.transform.localScale = new Vector3(0.42f, 0.42f, 0.42f);
            crystal.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
            if (mCyan != null) crystal.GetComponent<Renderer>().sharedMaterial = mCyan;

            var lightGo = Empty("Glow", groundPos + Vector3.up * 1.6f, Quaternion.identity, root.transform);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = 2.2f;
            light.shadows = LightShadows.None;
            if (mCyan != null) light.color = mCyan.HasProperty("_BaseColor") ? mCyan.GetColor("_BaseColor") : Color.cyan;

            SetLayerRecursively(root, Layers.Interactable);
            return root;
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        static GameObject Spawner(string name, Vector3 pos, GameObject prefab, bool isBoss, Transform parent)
        {
            var go = Empty(name, pos, Quaternion.identity, parent);   // faces +Z, toward the arena centre
            var sp = go.AddComponent<EnemySpawner>();
            sp.prefab = prefab;
            sp.isBoss = isBoss;
            spawnerCount++;
            return go;
        }

        static GameObject InstantiatePrefab(GameObject prefab, string name, Transform parent)
        {
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }
    }
}
