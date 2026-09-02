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
        const string EnemiesDir = "Assets/Data/Enemies";
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
        const float HalfExtent = 30f;   // 60 x 60 arena

        static Material mGround, mPlatform, mPink, mCyan, mYellow, mStone, mTorch, mEnemy, mBoss;
        static Material mWepSword, mWepHammer, mWepDagger, mWepDev;
        static GameObject pPlayer, pManagers, pHud, pGrunt, pHeavy, pBoss, pItemPickup;
        static GameObject pLegNinja, pLegKnight, pLegSpellsword, pLegMarionette, pLegRevenant;

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

            BuildSandboxController(root);

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
            light.color = Hex("#C9663A", new Color(0.788f, 0.4f, 0.227f));
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

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex("#0C0912", new Color(0.047f, 0.035f, 0.07f));
            // Start must stay clear of the Starfield radius (25) or the sky begins to fog.
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 240f;
            // Trilight, matching ProjectSetup: cool starlight above, warm eclipse ember on vertical
            // faces, near-black bounce underneath. This is what lifts the scene - the sky mesh is unlit
            // and contributes no illumination by itself. The EQUATOR term is the one that matters:
            // Trilight lights by normal, so every wall, pillar and torso is lit by it alone.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // ambientIntensity is a NO-OP in Trilight mode (Unity only applies it to Skybox ambient),
            // so the multipliers live in the colours - same as ProjectSetup.
            RenderSettings.ambientSkyColor = Hex("#3E4A6B", new Color(0.243f, 0.290f, 0.420f)) * 1.35f;
            RenderSettings.ambientEquatorColor = Hex("#7A5540", new Color(0.478f, 0.333f, 0.251f)) * 1.35f;
            RenderSettings.ambientGroundColor = Hex("#191424", new Color(0.098f, 0.078f, 0.141f)) * 1.35f;
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
            Box("Wall_E", new Vector3(HalfExtent, wy, 0f), new Vector3(0.5f, wallH, HalfExtent * 2f), mGround, root);
            Box("Wall_W", new Vector3(-HalfExtent, wy, 0f), new Vector3(0.5f, wallH, HalfExtent * 2f), mGround, root);

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
        ///   <item><b>WallRun_Face</b> - a 13 m x 8 m slab. Its EAST side (x 6.6, facing the jump
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

            // The run wall itself, z 10 to 23. 13 m long: a released-stick full run covers 13.5 m of
            // face, so the wall ends at the moment the mechanic does and the pad is what comes next.
            Trim(Box("WallRun_Face", new Vector3(6f, 4f, 16.5f), new Vector3(1.2f, 8f, 13f), mStone, g), mYellow);

            // Only reachable off the wall. Sheer on all four sides and 4 m up, so there is no walk-up.
            // z 24 to 29, x -1 to 5: a metre past the end of the face, 0.4 m west of its plane.
            Trim(Box("WallRun_Landing", new Vector3(2f, 3.5f, 26.5f), new Vector3(6f, 1f, 5f), mPlatform, g), mCyan);

            // The second run wall: x -7.6 to -6.4, z 18 to 29.7, east face runnable north or south.
            Trim(Box("WallRun_Corner", new Vector3(-7f, 4f, 23.85f), new Vector3(1.2f, 8f, 11.7f), mStone, g), mYellow);
        }

        /// <summary>
        /// Spawn pads along the south wall, with live spawners for Grunt / Heavy / Boss and the three
        /// legendary mini-bosses.
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
        }

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
            Torch("Torch_E", new Vector3(c, FloorTop, 0f), t);
            Torch("Torch_W", new Vector3(-c, FloorTop, 0f), t);
        }

        static void BuildKillZone(Transform root)
        {
            var kill = Empty("KillZone", new Vector3(0f, -25f, 0f), Quaternion.identity, root);
            var col = kill.AddComponent<BoxCollider>();
            col.size = new Vector3(140f, 2f, 140f);
            col.isTrigger = true;
            kill.AddComponent<KillZone>();
        }

        static void BuildSandboxController(Transform root)
        {
            var go = new GameObject("SandboxController");
            go.transform.SetParent(root, false);
            var controller = go.AddComponent<SandboxController>();

            // Rule 9: a field initialiser does nothing to a component already serialized in the scene, so
            // the shipped respawn values are written here. Practising a fight should not mean walking
            // back to a menu — pad enemies come back a few seconds after they die.
            controller.autoRespawnPadEnemies = true;
            controller.respawnDelay = 4f;

            // APPEND ONLY. The documented indices (0 Grunt, 1 Heavy, 2 Boss) are in README_Sandbox.md
            // and in muscle memory; renumbering silently changes what SpawnEnemyInFront(2) drops.
            controller.enemyPrefabs = new[] { pGrunt, pHeavy, pBoss, pLegNinja, pLegKnight, pLegSpellsword,
                                              pLegMarionette, pLegRevenant };
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
            };
            controller.dummyPrefabIndex = 0;   // Grunt
        }

        static EnemyData LoadEnemyData(string name)
        {
            if (!AssetDatabase.IsValidFolder(EnemiesDir)) return null;
            return AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemiesDir}/{name}.asset");
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
