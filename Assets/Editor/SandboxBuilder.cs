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

        // Floor top sits at y = 0; everything is measured from there.
        const float FloorTop = 0f;
        const float HalfExtent = 30f;   // 60 x 60 arena

        static Material mGround, mPlatform, mPink, mCyan, mYellow, mStone, mTorch, mEnemy, mBoss;
        static Material mWepSword, mWepHammer, mWepDagger, mWepDev;
        static GameObject pPlayer, pManagers, pHud, pGrunt, pHeavy, pBoss, pItemPickup;

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
            light.intensity = 0.85f;
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
            // and contributes no illumination by itself.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#2B3654", new Color(0.169f, 0.212f, 0.329f));
            RenderSettings.ambientEquatorColor = Hex("#4E3325", new Color(0.306f, 0.2f, 0.145f));
            RenderSettings.ambientGroundColor = Hex("#0E0B12", new Color(0.055f, 0.043f, 0.071f));
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.skybox = null;
        }

        static void ClearGeneratedRoots()
        {
            foreach (var name in new[] { RootName, "Player", "Managers", "HUD", "Main Camera" })
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
            Starfield.Build(skyGroup.transform, starCount: 1200, radius: 25f, seed: 20260830,
                            includeEclipse: true, eclipseYawDeg: 0f, eclipsePitchDeg: 13f,
                            eclipseDiameterDeg: 19f);
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

        /// <summary>Spawn pads along the south wall, with live spawners for Grunt / Heavy / Boss.</summary>
        static void BuildEnemyPads(Transform root)
        {
            const float z = -18f;
            const float padY = FloorTop - 0.4f;   // 0.1 m proud of the floor: visible, trivially walkable

            Box("Pad_Grunt", new Vector3(-14f, padY, z), new Vector3(4f, 1f, 4f), mEnemy, root);
            Box("Pad_Heavy", new Vector3(-5f, padY, z), new Vector3(5f, 1f, 5f), mEnemy, root);
            Box("Pad_Boss", new Vector3(8f, padY, z), new Vector3(8f, 1f, 8f), mBoss, root);
            Box("Pad_Spare_1", new Vector3(18f, padY, z), new Vector3(4f, 1f, 4f), mGround, root);
            Box("Pad_Spare_2", new Vector3(24f, padY, z), new Vector3(4f, 1f, 4f), mGround, root);

            // Enemies sit south of the player, so they face +Z (identity), unlike the campaign level.
            Spawner("Spawn_Grunt", new Vector3(-14f, FloorTop + 0.3f, z), pGrunt, false, root);
            Spawner("Spawn_Heavy", new Vector3(-5f, FloorTop + 0.3f, z), pHeavy, false, root);
            Spawner("Spawn_Boss", new Vector3(8f, FloorTop + 0.3f, z), pBoss, true, root);
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

            controller.enemyPrefabs = new[] { pGrunt, pHeavy, pBoss };
            controller.spawnableEnemies = new[]
            {
                LoadEnemyData("Grunt"),
                LoadEnemyData("Heavy"),
                LoadEnemyData("Boss"),
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
