using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// One-shot greybox builder for the test level. Re-running destroys and rebuilds the "Level" root
    /// (plus Player / Managers / HUD instances). Hand-placed extras belong under a sibling "Level_Manual"
    /// root, which is never touched.
    /// </summary>
    public static class LevelGreyboxBuilder
    {
        const string MatDir = "Assets/Materials/";
        const string PrefabDir = "Assets/Prefabs/";

        const string ItemsDir = "Assets/Data/Items/";

        // mPink is the EMBER accent, mCyan the BONE trim. The old gold (M_NeonYellow) is gone from
        // the level: three hues was the rainbow. The material asset still exists for other builders.
        static Material mGround, mPlatform, mPink, mCyan, mGate, mStone, mTorch;
        static int torchCount;
        static GameObject pPlayer, pManagers, pHud, pGrunt, pHeavy, pBoss, pCheckpoint, pItemPickup;

        static int boxCount, trimCount, spawnerCount, pickupCount;

        /// <summary>
        /// The canonical level, as data. While this asset exists it is the SOURCE OF TRUTH and the menu
        /// item below merely forwards to it; the literal coordinates in BuildHardcoded() are kept only as
        /// the reference implementation and the fallback for a project with no definitions yet.
        /// </summary>
        public const string CanonicalDefinitionPath = "Assets/Data/Levels/Level_01_Level.asset";

        /// <summary>
        /// Builds the canonical level. Delegates to <see cref="LevelDefinitionBuilder"/> whenever the
        /// definition asset exists, so "6. Build Level" and "8. Build Level From Definition" can never
        /// produce two different levels. Two builders that can disagree is how a level rework gets
        /// silently reverted by the next Rebuild Everything.
        /// </summary>
        [MenuItem("VibeGame1/6. Build Level")]
        public static void Build()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            {
                UnityEngine.Debug.LogError("["+nameof(LevelGreyboxBuilder)+"] Refusing to build during play mode. Exit play mode and try again.");
                return;
            }

            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(CanonicalDefinitionPath);
            if (def != null)
            {
                LevelDefinitionBuilder.Build(def);
                return;
            }

            Debug.LogWarning($"[LevelGreyboxBuilder] No level definition at {CanonicalDefinitionPath}; " +
                             "building the hard-coded greybox instead. Run 'VibeGame1/Export Current Level " +
                             "To Definition' afterwards to make the level data again.");
            BuildHardcoded();
        }

        /// <summary>
        /// The original hand-coded greybox: ~200 lines of literal coordinates. Superseded by the level
        /// definition above and kept as the reference implementation - the thing the data format has to
        /// be able to express. Do not author new content here.
        /// </summary>
        [MenuItem("VibeGame1/Legacy: Build Hard-Coded Greybox", priority = 400)]
        public static void BuildHardcoded()
        {
            // This is the PRE-REWORK course, kept only so the original layout is recoverable. It is NOT
            // the shipped level: `6. Build Level` forwards to LevelDefinitionBuilder and the definition
            // asset is the source of truth. It also speaks a different SPAWNER NAMING DIALECT
            // (Spawn_GruntA/B/C/D, Spawn_Heavy) than the four-tile level (Spawn_T1_GruntA, ...), which is
            // exactly the mismatch that made DebugHarness report success while finding nothing. Anything
            // that looks a spawner up by name must tolerate both; see DebugHarness.FindSpawned.
            Debug.LogWarning("[" + nameof(LevelGreyboxBuilder) + "] Building the LEGACY hard-coded course. "
                + "This replaces the four-tile level in the open scene and uses the old spawner names. "
                + "Run 'VibeGame1/6. Build Level' to restore the shipped level from its definition.");

            // PLAY MODE GUARD: this method destroys the Level root before rebuilding it.
            // In play mode the EditorSceneManager calls throw, leaving the scene wiped and
            // unsaveable - which is exactly how the level was lost once already.
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            {
                UnityEngine.Debug.LogError("["+nameof(LevelGreyboxBuilder)+"] Refusing to build during play mode. Exit play mode and try again.");
                return;
            }

            boxCount = trimCount = spawnerCount = torchCount = pickupCount = 0;
            LoadAssets();

            // ---- clear previous build (never touch Level_Manual) ----------------------------------
            foreach (var name in new[] { "Level", "Player", "Managers", "HUD", "Main Camera" })
            {
                GameObject existing;
                while ((existing = FindRoot(name)) != null) Object.DestroyImmediate(existing);
            }

            var level = new GameObject("Level");
            SetStatic(level);
            Transform root = level.transform;

            // ---- A. Start ---------------------------------------------------------------------------
            Trim(Box("Ground_Start", new Vector3(0, -0.5f, 0), new Vector3(14, 1, 14), mPlatform, root), mPink);
            var startSpawn = Empty("StartSpawn", new Vector3(0, 1.2f, -4), Quaternion.identity, root);

            // Wand altar, two metres ahead of the start and with a trigger wide enough to already cover
            // the spawn point: the loadout is chosen before the run, not cycled during it.
            WandAltar("WandPedestal_Start", new Vector3(0f, 0f, -2f), 3f, root);

            // ---- B. Jump gaps -----------------------------------------------------------------------
            Trim(Box("Plat_B1", new Vector3(0, -0.5f, 14), new Vector3(4, 1, 4), mPlatform, root), mCyan);
            Trim(Box("Plat_B2", new Vector3(4, 0.5f, 22), new Vector3(4, 1, 4), mPlatform, root), mCyan);
            Trim(Box("Plat_B3", new Vector3(-3, 1.5f, 30), new Vector3(4, 1, 4), mPlatform, root), mCyan);
            Trim(Box("Plat_B4", new Vector3(0, 2.5f, 38), new Vector3(5, 1, 5), mPlatform, root), mCyan);

            // ---- C. Walkway -------------------------------------------------------------------------
            Box("Walk_C", new Vector3(0, 3, 54), new Vector3(6, 1, 26), mPlatform, root);
            Box("Rail_C_L", new Vector3(-3.1f, 4, 54), new Vector3(0.2f, 1.2f, 26), mPink, root);
            Box("Rail_C_R", new Vector3(3.1f, 4, 54), new Vector3(0.2f, 1.2f, 26), mPink, root);
            Box("Pillar_C1", new Vector3(2, 5, 56), new Vector3(1, 4, 1), mStone, root);
            Box("Pillar_C2", new Vector3(-2, 5, 62), new Vector3(1, 4, 1), mStone, root);
            Spawner("Spawn_GruntA", new Vector3(0, 3.6f, 50), pGrunt, false, root);
            Spawner("Spawn_GruntB", new Vector3(1.5f, 3.6f, 60), pGrunt, false, root);

            // ---- D. Pillar hop ----------------------------------------------------------------------
            // Reachability rule (jump 2.4 m, run 11 m/s): rise <= 1.5 m with gap <= 4.5 m; rise <= 1 m with gap <= 6 m.
            // Pillars are 10 m tall columns whose TOP is at center.y + 5.
            Trim(Box("Pil_D1", new Vector3(0, 0, 71.5f), new Vector3(2.5f, 10, 2.5f), mPlatform, root), mCyan);      // top 5.0  (walkway top 3.5, gap 3.25)
            Trim(Box("Pil_D2", new Vector3(3, 1.5f, 76.5f), new Vector3(2.5f, 10, 2.5f), mPlatform, root), mCyan);   // top 6.5  (gap 2.5)
            Trim(Box("Pil_D3", new Vector3(-1, 3, 81.5f), new Vector3(2.5f, 10, 2.5f), mPlatform, root), mCyan);     // top 8.0  (gap 2.9)
            Trim(Box("Plat_CP1", new Vector3(0, 8.5f, 92), new Vector3(10, 1, 10), mPlatform, root), mPink);           // top 9.0  (gap 4.25)
            Checkpoint("Checkpoint_1", new Vector3(0, 9, 92), root);

            // ---- E. Climb (spiral of ledges, each +1.5 m, gaps 1-3 m) ------------------------------
            Trim(Box("Led_E1", new Vector3(6, 10, 100), new Vector3(4, 1, 4), mPlatform, root), mCyan);       // top 10.5
            Trim(Box("Led_E2", new Vector3(0, 11.5f, 106), new Vector3(4, 1, 4), mPlatform, root), mCyan);    // top 12.0
            Trim(Box("Led_E3", new Vector3(-6, 13, 110), new Vector3(4, 1, 4), mPlatform, root), mCyan);      // top 13.5
            Trim(Box("Led_E4", new Vector3(0, 14.5f, 115), new Vector3(4, 1, 4), mPlatform, root), mCyan);    // top 15.0
            Trim(Box("Led_E5", new Vector3(5, 16, 119), new Vector3(4, 1, 4), mPlatform, root), mCyan);       // top 16.5
            Trim(Box("Tower_E", new Vector3(0, 10, 111), new Vector3(3, 14, 3), mGround, root), mCyan);     // visual anchor, top 17
            Trim(Box("Landing_E", new Vector3(0, 17.5f, 128), new Vector3(16, 1, 12), mPlatform, root), mPink); // top 18, z 122..134
            Box("Rail_E_L", new Vector3(-8.1f, 18.6f, 128), new Vector3(0.2f, 1.2f, 12), mPink, root);
            Box("Rail_E_R", new Vector3(8.1f, 18.6f, 128), new Vector3(0.2f, 1.2f, 12), mPink, root);
            Spawner("Spawn_Heavy", new Vector3(0, 18.1f, 128), pHeavy, false, root);
            Spawner("Spawn_GruntC", new Vector3(-5, 18.1f, 125), pGrunt, false, root);
            Spawner("Spawn_GruntD", new Vector3(5, 18.1f, 131), pGrunt, false, root);

            // ---- F. Bridge + checkpoint 2 -----------------------------------------------------------
            Box("Bridge_F", new Vector3(0, 17.5f, 138), new Vector3(3, 1, 8), mPlatform, root);
            Trim(Box("Plat_CP2", new Vector3(0, 17.5f, 146), new Vector3(8, 1, 8), mPlatform, root), mPink);
            Checkpoint("Checkpoint_2", new Vector3(0, 18, 146), root);

            // ---- G. Arena ---------------------------------------------------------------------------
            Trim(Box("Arena", new Vector3(0, 17.5f, 170), new Vector3(34, 1, 34), mPlatform, root), mPink);
            Box("Wall_N", new Vector3(0, 20, 187), new Vector3(34, 4, 0.5f), mGround, root);
            Box("Wall_S_L", new Vector3(-10, 20, 153), new Vector3(14, 4, 0.5f), mGround, root);
            Box("Wall_S_R", new Vector3(10, 20, 153), new Vector3(14, 4, 0.5f), mGround, root);
            Box("Wall_E", new Vector3(17, 20, 170), new Vector3(0.5f, 4, 34), mGround, root);
            Box("Wall_W", new Vector3(-17, 20, 170), new Vector3(0.5f, 4, 34), mGround, root);
            Box("Pillar_G_SW", new Vector3(-14, 21, 158), new Vector3(1.5f, 8, 1.5f), mStone, root);
            Box("Pillar_G_SE", new Vector3(14, 21, 158), new Vector3(1.5f, 8, 1.5f), mStone, root);
            Box("Pillar_G_NW", new Vector3(-14, 21, 182), new Vector3(1.5f, 8, 1.5f), mStone, root);
            Box("Pillar_G_NE", new Vector3(14, 21, 182), new Vector3(1.5f, 8, 1.5f), mStone, root);

            var gateOpen = new Vector3(0, 15.5f, 153);
            var gateClosed = new Vector3(0, 20, 153);
            var gate = Box("Gate", gateOpen, new Vector3(6, 4, 0.5f), mGate, root, isStatic: false);

            var trigger = Empty("ArenaTrigger", new Vector3(0, 20, 157), Quaternion.identity, root);
            var triggerCol = trigger.AddComponent<BoxCollider>();
            triggerCol.size = new Vector3(6, 4, 2);
            triggerCol.isTrigger = true;
            var arena = trigger.AddComponent<BossArenaTrigger>();
            arena.gate = gate.transform;
            arena.gateOpenPosition = gateOpen;
            arena.gateClosedPosition = gateClosed;

            Spawner("Spawn_Boss", new Vector3(0, 18.1f, 176), pBoss, true, root);

            // ---- Sky --------------------------------------------------------------------------------
            // A deep-space backdrop that also LIGHTS the level: the gradient dome and star field give the
            // course somewhere to be, and the Trilight ambient set in ProjectSetup (cool from above, ember
            // from the eclipse's side) is what actually raises the light level. One mesh, one draw call.
            //
            // The eclipse now lives INSIDE this sky rather than as world quads at z=268. Two reasons:
            //   1. FOG. At 268 units the old quads sat far past fogEndDistance, so linear fog blended them
            //      100% to fogColor - the "focal image" was a flat fog-coloured slab, and worse, it would
            //      have blanked out a 320-unit-wide rectangle of the new star field behind it.
            //   2. PARALLAX. A celestial body should hold the same angular size wherever you stand. The
            //      old quads grew as you ran toward them, which read as a wall, not a sun.
            // It is still framed dead ahead down +Z and directly behind the boss, and is still drawn as
            // corona-then-black-disc so only a burning rim shows.
            var skyGroup = new GameObject("Sky");
            skyGroup.transform.SetParent(root, false);
            // Layer SkyLayer keeps this out of the NavMesh bake below, which collects RenderMeshes on
            // layer 0 from the Level root's children - a 25-unit sphere would otherwise be bake input.
            // The legacy greybox takes Starfield's shipped eclipse geometry (pitch/diameter defaults,
            // asserted by SkyEclipseTests). The SHIPPED level goes through LevelDefinitionBuilder,
            // where the same numbers are authored on LevelDefinition.sky - keep the two in agreement.
            Starfield.Build(skyGroup.transform, starCount: 1200, radius: 25f, seed: 20260830);

            // ---- Torches (dark fantasy light sources; basePos = platform top surface) ---------------
            var torches = new GameObject("Torches");
            torches.transform.SetParent(root, false);
            Transform torchRoot = torches.transform;
            // Start pad corners
            Torch("Torch_Start_SW", new Vector3(-6, 0, -6), torchRoot);
            Torch("Torch_Start_SE", new Vector3(6, 0, -6), torchRoot);
            Torch("Torch_Start_NW", new Vector3(-6, 0, 6), torchRoot);
            Torch("Torch_Start_NE", new Vector3(6, 0, 6), torchRoot);
            // Walkway ends
            Torch("Torch_Walk_S_L", new Vector3(-2.5f, 3.5f, 42), torchRoot);
            Torch("Torch_Walk_S_R", new Vector3(2.5f, 3.5f, 42), torchRoot);
            Torch("Torch_Walk_N_L", new Vector3(-2.5f, 3.5f, 66), torchRoot);
            Torch("Torch_Walk_N_R", new Vector3(2.5f, 3.5f, 66), torchRoot);
            // Checkpoint 1 (top y = 9)
            Torch("Torch_CP1_L", new Vector3(-3, 9, 92), torchRoot);
            Torch("Torch_CP1_R", new Vector3(3, 9, 92), torchRoot);
            // Landing (top y = 18)
            Torch("Torch_Land_SW", new Vector3(-7, 18, 123), torchRoot);
            Torch("Torch_Land_SE", new Vector3(7, 18, 123), torchRoot);
            Torch("Torch_Land_NW", new Vector3(-7, 18, 133), torchRoot);
            Torch("Torch_Land_NE", new Vector3(7, 18, 133), torchRoot);
            // Checkpoint 2 (top y = 18)
            Torch("Torch_CP2_L", new Vector3(-3, 18, 146), torchRoot);
            Torch("Torch_CP2_R", new Vector3(3, 18, 146), torchRoot);
            // Arena: beside the corner pillars, mid walls, and flanking the entrance
            Torch("Torch_Arena_SW", new Vector3(-12.5f, 18, 158), torchRoot);
            Torch("Torch_Arena_SE", new Vector3(12.5f, 18, 158), torchRoot);
            Torch("Torch_Arena_NW", new Vector3(-12.5f, 18, 182), torchRoot);
            Torch("Torch_Arena_NE", new Vector3(12.5f, 18, 182), torchRoot);
            Torch("Torch_Arena_W", new Vector3(-15.5f, 18, 170), torchRoot);
            Torch("Torch_Arena_E", new Vector3(15.5f, 18, 170), torchRoot);
            Torch("Torch_Arena_N", new Vector3(0, 18, 185), torchRoot);
            Torch("Torch_Arena_Gate_L", new Vector3(-8, 18, 154), torchRoot);
            Torch("Torch_Arena_Gate_R", new Vector3(8, 18, 154), torchRoot);

            // ---- Pickups (single-use items; y = platform top + 1.2) ---------------------------------
            var pickups = new GameObject("Pickups");
            pickups.transform.SetParent(root, false);
            Transform pickupRoot = pickups.transform;
            // Start platform (top 0.0), a few metres in front of StartSpawn at (0, 1.2, -4).
            // Deliberately the very first thing you can touch, so the headline item is testable
            // immediately instead of only appearing deep in the course.
            ItemAt("Pickup_Hook_Spawn", new Vector3(2f, 1.2f, 0f), "Grapple", pickupRoot);
            ItemAt("Pickup_Surge_Spawn", new Vector3(-2f, 1.2f, 0f), "WallSurge", pickupRoot);
            // Walkway far end (top 3.5) — pays for the pillar hop that follows.
            ItemAt("Pickup_Hook_1", new Vector3(-2f, 4.7f, 64f), "Grapple", pickupRoot);
            // Checkpoint 1 platform (top 9.0). Moved off (3,*,92) which is inside Torch_CP1_R,
            // and kept away from the checkpoint's respawn point at (0, 9.2, 90).
            ItemAt("Pickup_Surge_1", new Vector3(-3.5f, 10.2f, 95f), "WallSurge", pickupRoot);
            // Landing (top 18.0) — the headline item, right before the Heavy + 2 Grunts.
            // Nudged off (-6,*,124) which sat 1.4 m from Torch_Land_SW.
            ItemAt("Pickup_Hook_2", new Vector3(-4f, 19.2f, 126f), "Grapple", pickupRoot);
            // Checkpoint 2 platform (top 18.0). Moved off (-2.5,*,146) which overlapped Torch_CP2_L,
            // and clear of the checkpoint trigger box (z 144.5..147.5).
            ItemAt("Pickup_Surge_2", new Vector3(0f, 19.2f, 148.5f), "WallSurge", pickupRoot);
            // Boss arena near the entrance (top 18.0) — lets the player open with a stagger.
            ItemAt("Pickup_Hook_3", new Vector3(-7f, 19.2f, 160f), "Grapple", pickupRoot);

            // ---- Global -----------------------------------------------------------------------------
            var kill = Empty("KillZone", new Vector3(0, -25, 95), Quaternion.identity, root);
            var killCol = kill.AddComponent<BoxCollider>();
            killCol.size = new Vector3(140, 2, 260);
            killCol.isTrigger = true;
            kill.AddComponent<KillZone>();

            // ---- NavMesh ----------------------------------------------------------------------------
            var surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.layerMask = 1 << 0; // Default only
            surface.BuildNavMesh();

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
                else Debug.LogWarning("[LevelGreyboxBuilder] Managers prefab has no LevelManager; startSpawn not assigned.");
            }

            InstantiatePrefab(pHud, "HUD", null);

            // ---- Save -------------------------------------------------------------------------------
            var scene = level.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[LevelGreyboxBuilder] Built level: {boxCount} boxes, {trimCount} trims, {spawnerCount} spawners, {torchCount} torches, " +
                      $"{pickupCount} pickups, navmesh built={surface.navMeshData != null}, scene saved '{scene.path}'.");
        }

        [MenuItem("VibeGame1/Rebuild NavMesh")]
        public static void RebuildNavMesh()
        {
            var level = FindRoot("Level");
            if (level == null) { Debug.LogWarning("[LevelGreyboxBuilder] No 'Level' root in the open scene."); return; }
            var surface = level.GetComponent<NavMeshSurface>();
            if (surface == null) surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.layerMask = 1 << 0;
            surface.BuildNavMesh();
            EditorSceneManager.MarkSceneDirty(level.scene);
            Debug.Log("[LevelGreyboxBuilder] NavMesh rebuilt.");
        }

        // ---- helpers ------------------------------------------------------------------------------

        static void LoadAssets()
        {
            mGround = LoadMat("M_Ground");
            mPlatform = LoadMat("M_Platform");
            mPink = LoadMat("M_NeonPink");
            mCyan = LoadMat("M_NeonCyan");
            mGate = LoadMat("M_Gate");
            mStone = LoadMat("M_Stone");
            mTorch = LoadMat("M_Torch");

            pPlayer = LoadPrefab("Player");
            pManagers = LoadPrefab("Managers");
            pHud = LoadPrefab("HUD");
            // parkour_enemies (2026-09-06 split): everything the campaign level places to cover a span is
            // a SENTRY. The melee Enemy_Grunt / Enemy_Heavy live on the sandbox pads.
            pGrunt = LoadPrefab("Sentry_Grunt");
            pHeavy = LoadPrefab("Sentry_Heavy");
            pBoss = LoadPrefab("Boss");
            pCheckpoint = LoadPrefab("Checkpoint");
            pItemPickup = LoadPrefab("ItemPickup");
        }

        static Material LoadMat(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + name + ".mat");
            if (m == null) Debug.LogWarning($"[LevelGreyboxBuilder] Missing material {MatDir}{name}.mat (run the material setup first).");
            return m;
        }

        static GameObject LoadPrefab(string name)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + name + ".prefab");
            if (p == null) Debug.LogWarning($"[LevelGreyboxBuilder] Missing prefab {PrefabDir}{name}.prefab (run PrefabFactory/HudBuilder first).");
            return p;
        }

        static GameObject FindRoot(string name)
        {
            var scene = SceneManager_GetActive();
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }

        static UnityEngine.SceneManagement.Scene SceneManager_GetActive()
        {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene();
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

        /// <summary>
        /// Stone post + ember cube + flickering point light. basePos is the platform's top surface.
        /// Not static (the light moves) and the cubes carry no colliders, so the NavMesh ignores them.
        /// </summary>
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
            emberRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (mTorch != null) emberRenderer.sharedMaterial = mTorch;

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(torch.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = ColorUtility.TryParseHtmlString("#FF8A2A", out var c) ? c : new Color(1f, 0.54f, 0.16f);
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
                // Built as a sibling first (world-space scale), then parented so the non-uniform parent scale
                // does not distort it.
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = platform.name + b.suffix;
                bar.transform.SetParent(parent, false);
                bar.transform.position = b.pos;
                bar.transform.localScale = b.size;
                bar.layer = 0;
                if (m != null) bar.GetComponent<Renderer>().sharedMaterial = m;
                // Trims are decoration: no collider, no navmesh contribution.
                Object.DestroyImmediate(bar.GetComponent<Collider>());
                GameObjectUtility.SetStaticEditorFlags(bar, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
                bar.transform.SetParent(platform.transform, true);
                trimCount++;
            }
        }

        /// <summary>
        /// The wand altar: a stone plinth on Default (so it still bakes as level geometry) plus a
        /// floating crystal on Interactable carrying the trigger. <paramref name="groundPos"/> is the
        /// floor surface it stands on; <paramref name="triggerRadius"/> is how far out the menu opens,
        /// deliberately wide enough to reach the player start so the choice happens before the run.
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

            // Interactable keeps the trigger out of the NavMesh bake (Default layer only), exactly as
            // ItemPickup does. The plinth above stays on Default because it IS walkable geometry.
            SetLayerRecursively(root, Layers.Interactable);
            return root;
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        static GameObject Empty(string name, Vector3 pos, Quaternion rot, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = rot;
            return go;
        }

        static GameObject Spawner(string name, Vector3 pos, GameObject prefab, bool isBoss, Transform parent)
        {
            var go = Empty(name, pos, Quaternion.Euler(0, 180, 0), parent); // faces -Z toward the incoming player
            var sp = go.AddComponent<EnemySpawner>();
            sp.prefab = prefab;
            sp.isBoss = isBoss;
            spawnerCount++;
            return go;
        }

        static GameObject Checkpoint(string name, Vector3 pos, Transform parent)
        {
            var go = InstantiatePrefab(pCheckpoint, name, parent);
            if (go == null) return null;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            return go;
        }

        /// <summary>Places an ItemPickup instance carrying the named ItemData from Assets/Data/Items.</summary>
        static GameObject ItemAt(string name, Vector3 pos, string itemAssetName, Transform parent)
        {
            if (pItemPickup == null)
            {
                Debug.LogWarning($"[LevelGreyboxBuilder] ItemPickup prefab missing; skipping {name}.");
                return null;
            }
            var data = AssetDatabase.LoadAssetAtPath<ItemData>(ItemsDir + itemAssetName + ".asset");
            if (data == null)
            {
                Debug.LogWarning($"[LevelGreyboxBuilder] Missing item data {ItemsDir}{itemAssetName}.asset (run DataFactory first); skipping {name}.");
                return null;
            }

            var go = InstantiatePrefab(pItemPickup, name, parent);
            if (go == null) return null;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;

            var pickup = go.GetComponent<ItemPickup>();
            if (pickup != null) pickup.item = data;
            else Debug.LogWarning($"[LevelGreyboxBuilder] {name} has no ItemPickup component.");

            pickupCount++;
            return go;
        }

        static GameObject InstantiatePrefab(GameObject prefab, string name, Transform parent)
        {
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager_GetActive());
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }
    }
}
