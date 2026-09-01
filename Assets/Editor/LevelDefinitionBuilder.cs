using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Builds a scene from a <see cref="LevelDefinition"/> asset.
    ///
    /// This is the same MECHANISM as <c>LevelGreyboxBuilder</c> — identical output shape (a "Level" root,
    /// the same static flags, the same NavMesh bake, the same Player/Managers/HUD instances, the same
    /// untouched "Level_Manual" sibling) — but driven entirely by data. The hand-coded builder is left
    /// exactly as it is: it remains the reference implementation and the fallback.
    ///
    /// Migration path: <c>VibeGame1/Export Current Level To Definition</c> turns the existing level into an
    /// asset, then this rebuilds it. Round-tripping the two is the proof they agree.
    /// </summary>
    public static class LevelDefinitionBuilder
    {
        const string MatDir = "Assets/Materials/";
        const string PrefabDir = "Assets/Prefabs/";
        const string ItemsDir = "Assets/Data/Items/";

        static Material mStone, mTorch, mCyan;
        static GameObject pPlayer, pManagers, pHud, pCheckpoint, pItemPickup;
        static int boxCount, trimCount, spawnerCount, torchCount, pickupCount;
        static readonly System.Collections.Generic.Dictionary<string, EnemySpawner> builtSpawners =
            new System.Collections.Generic.Dictionary<string, EnemySpawner>();

        [MenuItem("VibeGame1/8. Build Level From Definition")]
        public static void BuildSelected()
        {
            var def = Selection.activeObject as LevelDefinition;
            if (def == null)
            {
                EditorUtility.DisplayDialog(
                    "No level selected",
                    "Select a LevelDefinition asset in the Project window, then run this again.\n\n" +
                    "No definitions yet? Run 'VibeGame1/Export Current Level To Definition' first — it " +
                    "turns the level in the open scene into one.",
                    "OK");
                return;
            }
            Build(def);
        }

        /// <summary>
        /// Open a definition's own target scene, rebuild it and SAVE — the one entry point that works
        /// under <c>-batchmode -executeMethod</c>.
        ///
        /// <para>Batch mode starts with an empty scene, so `6. Build Level` refuses there: the
        /// active-scene guard below (correctly) will not build the campaign level into whatever happens
        /// to be open. That guard is worth keeping — it exists because the level was once built into the
        /// sandbox — so the headless path opens the right scene itself rather than weakening it.</para>
        ///
        /// <para><b>Load the definition AFTER opening the scene.</b> `OpenScene` in Single mode unloads
        /// unused assets, and a `LevelDefinition` held only by a local counts as unused: the reference
        /// survives as Unity's fake-null, this method refuses with "Null LevelDefinition" in a log nobody
        /// reads, and a capture taken afterwards shows the OLD scene looking entirely plausible. That
        /// exact sequence cost a level-geometry session its first round of screenshots.</para>
        /// </summary>
        /// <param name="definitionPath">Defaults to the canonical `Level_01_Level.asset`.</param>
        public static void BuildHeadless(string definitionPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[LevelDefinitionBuilder] Refusing to build during play mode.");
                return;
            }

            var probe = AssetDatabase.LoadAssetAtPath<LevelDefinition>(definitionPath);
            if (probe == null)
            {
                Debug.LogError("[LevelDefinitionBuilder] No LevelDefinition at " + definitionPath);
                return;
            }
            string scenePath = "Assets/Scenes/" + probe.sceneName + ".unity";

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

            // Re-load AFTER the scene swap — see the remark above. This is not defensive noise.
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(definitionPath);
            if (def == null)
            {
                Debug.LogError("[LevelDefinitionBuilder] The definition went null across the scene load. " +
                               "Nothing was built.");
                return;
            }

            Build(def);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[LevelDefinitionBuilder] Headless build complete and SAVED: " + scenePath);
        }

        /// <summary>
        /// Zero-argument wrapper for the canonical level, because <c>-executeMethod</c> refuses anything
        /// else: "Only methods with 0 arguments are supported", and an OPTIONAL parameter still counts as
        /// an argument. It fails at the very end of a multi-minute batch launch, after the whole import
        /// and compile, which is an expensive way to learn it.
        /// </summary>
        [MenuItem("VibeGame1/8b. Build Level From Definition (headless)")]
        public static void BuildCanonicalHeadless()
        {
            BuildHeadless("Assets/Data/Levels/Level_01_Level.asset");
        }

        public static void Build(LevelDefinition def)
        {
            // PLAY MODE GUARD: this destroys the Level root before rebuilding it. In play mode the
            // EditorSceneManager calls throw, leaving the scene wiped and unsaveable — which is exactly
            // how the level was lost once already. See ENGINEERING-LOG.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[" + nameof(LevelDefinitionBuilder) + "] Refusing to build during play mode. Exit play mode and try again.");
                return;
            }
            if (def == null) { Debug.LogError("[LevelDefinitionBuilder] Null LevelDefinition."); return; }

            // ACTIVE SCENE GUARD: this builds into whatever scene is currently OPEN, not into
            // def.sceneName, and it ends in SaveOpenScenes(). Run it with Sandbox.unity open and the
            // campaign course is built into the sandbox and saved over it - which happened once, with
            // no error, while two agents shared the editor. Refuse rather than corrupt the wrong scene.
            var active = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(def.sceneName) && active.name != def.sceneName)
            {
                Debug.LogError("[" + nameof(LevelDefinitionBuilder) + "] Refusing to build '" + def.name
                    + "' into the open scene '" + active.name + "' - it targets '" + def.sceneName
                    + "'. Open that scene first (VibeGame1/Open Test Level).");
                return;
            }

            boxCount = trimCount = spawnerCount = torchCount = pickupCount = 0;
            builtSpawners.Clear();
            LoadShared();

            // ---- clear previous build (never touch Level_Manual) ----------------------------------
            foreach (var n in new[] { "Level", "Player", "Managers", "HUD", "Main Camera" })
            {
                GameObject existing;
                while ((existing = FindRoot(n)) != null) Object.DestroyImmediate(existing);
            }

            // ---- clear RUNTIME DEBRIS -----------------------------------------------------------------
            // Anything spawned during play (enemies from spawners, Fx_*, ~DeathMist, the ghost-racing
            // installer, the feature-test host) becomes a scene ROOT, and a builder run ends in
            // SaveOpenScenes() - so one play session followed by one rebuild bakes that debris into the
            // level file permanently. Hard rule 4 says everything not hand-placed is regenerable and
            // hand-placed extras live under Level_Manual, which gives us an exact allow-list.
            int debris = 0;
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go == null) continue;
                if (KeepAsRoot.Contains(go.name) || go.name.EndsWith("_Manual")) continue;
                // DontSave objects never reach the scene file, and one of them is the live feature-test
                // host. Leave them alone.
                if ((go.hideFlags & HideFlags.DontSave) != 0) continue;
                Debug.Log($"[LevelDefinitionBuilder] Removing runtime debris root '{go.name}' " +
                          "(spawned in play mode and saved into the scene).");
                Object.DestroyImmediate(go);
                debris++;
            }

            var level = new GameObject("Level");
            SetStatic(level);
            Transform root = level.transform;

            // ---- geometry ---------------------------------------------------------------------------
            if (def.platforms != null)
            {
                foreach (var p in def.platforms)
                {
                    if (p == null) continue;
                    var box = Box(p.name, p.center, p.size, LoadMat(p.materialKey), root, p.isStatic);
                    if (p.trim) Trim(box, LoadMat(p.trimMaterialKey));
                }
            }

            // ---- player start -----------------------------------------------------------------------
            var startSpawn = Empty("StartSpawn", def.playerStart, Quaternion.Euler(0f, def.playerStartYaw, 0f), root);

            // ---- spawners ---------------------------------------------------------------------------
            if (def.spawns != null)
            {
                foreach (var s in def.spawns)
                {
                    if (s == null) continue;
                    var prefab = LoadPrefab(s.prefabKey);
                    if (prefab == null) continue;   // LoadPrefab already warned
                    var go = Empty(s.name, s.position, Quaternion.Euler(0f, s.yaw, 0f), root);
                    var sp = go.AddComponent<EnemySpawner>();
                    sp.prefab = prefab;
                    sp.isBoss = s.isBoss;
                    builtSpawners[s.name] = sp;
                    spawnerCount++;
                }
            }

            // ---- checkpoints ------------------------------------------------------------------------
            if (def.checkpoints != null)
            {
                foreach (var c in def.checkpoints)
                {
                    if (c == null) continue;
                    var go = InstantiatePrefab(pCheckpoint, c.name, root);
                    if (go == null) continue;
                    go.transform.position = c.position;
                    go.transform.rotation = Quaternion.identity;

                    // The prefab carries its own spawnPoint child; honour the authored offset.
                    var cp = go.GetComponent<Checkpoint>();
                    if (cp != null && cp.spawnPoint != null)
                        cp.spawnPoint.position = c.position + c.spawnOffset;
                }
            }

            // ---- torches ----------------------------------------------------------------------------
            if (def.torches != null && def.torches.Length > 0)
            {
                var torches = new GameObject("Torches");
                torches.transform.SetParent(root, false);
                foreach (var t in def.torches)
                {
                    if (t == null) continue;
                    Torch(t.name, t.basePosition, torches.transform);
                }
            }

            // ---- wand altars --------------------------------------------------------------------------
            // Must survive the round trip: a level built without its pedestal has no way to pick a wand,
            // and the omission is invisible until you play it.
            if (def.pedestals != null)
            {
                foreach (var pd in def.pedestals)
                {
                    if (pd == null) continue;
                    WandAltar(pd.name, pd.groundPosition, pd.triggerRadius, root);
                }
            }

            // ---- pickups ----------------------------------------------------------------------------
            if (def.pickups != null && def.pickups.Length > 0)
            {
                var pickups = new GameObject("Pickups");
                pickups.transform.SetParent(root, false);
                foreach (var p in def.pickups)
                {
                    if (p == null) continue;
                    ItemAt(p.name, p.position, p.itemKey, pickups.transform);
                }
            }

            // ---- arenas (mini-boss gates and the boss gate are the same mechanism) --------------------
            if (def.arenas != null)
            {
                foreach (var a in def.arenas)
                {
                    if (a == null || !a.enabled) continue;
                    var gate = Box(a.gateName, a.gateOpenPosition, a.gateSize, LoadMat(a.gateMaterialKey), root, isStatic: false);

                    var trigger = Empty(a.triggerName, a.triggerPosition, Quaternion.identity, root);
                    var col = trigger.AddComponent<BoxCollider>();
                    col.size = a.triggerSize;
                    col.isTrigger = true;
                    var bat = trigger.AddComponent<BossArenaTrigger>();
                    bat.gate = gate.transform;
                    bat.gateOpenPosition = a.gateOpenPosition;
                    bat.gateClosedPosition = a.gateClosedPosition;

                    if (a.hasExitGate)
                    {
                        // Rest position is CLOSED: the exit is sealed until the legendary falls.
                        var exit = Box(a.exitGateName, a.exitGateClosedPosition, a.exitGateSize,
                                       LoadMat(a.exitGateMaterialKey), root, isStatic: false);
                        bat.exitGate = exit.transform;
                        bat.exitGateClosedPosition = a.exitGateClosedPosition;
                        bat.exitGateOpenPosition = a.exitGateOpenPosition;
                    }

                    if (!string.IsNullOrEmpty(a.clearSpawnerName))
                    {
                        EnemySpawner sp;
                        if (builtSpawners.TryGetValue(a.clearSpawnerName, out sp)) bat.clearSpawner = sp;
                        else Debug.LogWarning("[LevelDefinitionBuilder] Arena '" + a.triggerName + "' names clearSpawner '" +
                                              a.clearSpawnerName + "', which is not a spawn in this definition (a missing " +
                                              "mini-boss prefab also drops its spawner). Its exit gate will never open.");
                    }
                }
            }

            // ---- sky --------------------------------------------------------------------------------
            // On the Sky layer, so the NavMesh bake below (RenderMeshes, layer 0 only) ignores it.
            if (def.sky != null && def.sky.enabled)
            {
                var skyGroup = new GameObject("Sky");
                skyGroup.transform.SetParent(root, false);
                Starfield.Build(skyGroup.transform, def.sky.starCount, def.sky.radius, def.sky.seed,
                                def.sky.includeEclipse, def.sky.eclipseYawDeg, def.sky.eclipsePitchDeg,
                                def.sky.eclipseDiameterDeg);
            }

            // ---- kill zone --------------------------------------------------------------------------
            if (def.killZone != null)
            {
                var kill = Empty(def.killZone.name, def.killZone.center, Quaternion.identity, root);
                var kc = kill.AddComponent<BoxCollider>();
                kc.size = def.killZone.size;
                kc.isTrigger = true;
                kill.AddComponent<KillZone>();
            }

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
                else Debug.LogWarning("[LevelDefinitionBuilder] Managers prefab has no LevelManager; startSpawn not assigned.");
            }

            InstantiatePrefab(pHud, "HUD", null);

            // ---- save -------------------------------------------------------------------------------
            var scene = level.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[LevelDefinitionBuilder] Built '{def.SafeLevelId}' ({def.displayName}): {boxCount} boxes, " +
                      $"{trimCount} trims, {spawnerCount} spawners, {torchCount} torches, {pickupCount} pickups, " +
                      $"navmesh built={surface.navMeshData != null}, {debris} debris roots removed, " +
                      $"scene saved '{scene.path}'.");
        }

        // ---- helpers ------------------------------------------------------------------------------
        // Mirrors LevelGreyboxBuilder's helpers deliberately: same geometry, same static flags, same
        // collider/trim rules. Divergence here would mean the two builders silently produce different
        // levels from the same intent.

        static void LoadShared()
        {
            mStone = LoadMat("Stone");
            mTorch = LoadMat("Torch");
            mCyan = LoadMat("NeonCyan");
            pPlayer = LoadPrefab("Player");
            pManagers = LoadPrefab("Managers");
            pHud = LoadPrefab("HUD");
            pCheckpoint = LoadPrefab("Checkpoint");
            pItemPickup = LoadPrefab("ItemPickup");
        }

        /// <summary>'Platform' -> Assets/Materials/M_Platform.mat. Keys omit the M_ prefix.</summary>
        static Material LoadMat(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "M_" + key + ".mat");
            if (m == null) Debug.LogWarning($"[LevelDefinitionBuilder] Missing material {MatDir}M_{key}.mat (run '2. Create Materials' first).");
            return m;
        }

        static GameObject LoadPrefab(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + key + ".prefab");
            if (p == null) Debug.LogWarning($"[LevelDefinitionBuilder] Missing prefab {PrefabDir}{key}.prefab (run '4. Build Prefabs' / '5. Build HUD' first).");
            return p;
        }

        /// <summary>
        /// The only GameObjects allowed to sit at the root of a built level scene. Everything else at the
        /// root is either this builder's output (destroyed and rebuilt above) or play-mode debris.
        /// A `*_Manual` root is the sanctioned place for hand-placed extras and is never touched.
        /// </summary>
        static readonly System.Collections.Generic.HashSet<string> KeepAsRoot =
            new System.Collections.Generic.HashSet<string>
            {
                "Directional Light", "Global Volume", "Level", "Player", "Managers", "HUD", "Main Camera",
            };

        static GameObject FindRoot(string name)
        {
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
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
                // Built as a sibling first (world-space scale), then parented, so the non-uniform parent
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

        static GameObject Empty(string name, Vector3 pos, Quaternion rot, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = rot;
            return go;
        }

        /// <summary>Stone post + ember cube + flickering point light. basePos is the platform's top surface.</summary>
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

        static GameObject ItemAt(string name, Vector3 pos, string itemKey, Transform parent)
        {
            if (pItemPickup == null)
            {
                Debug.LogWarning($"[LevelDefinitionBuilder] ItemPickup prefab missing; skipping {name}.");
                return null;
            }
            var data = AssetDatabase.LoadAssetAtPath<ItemData>(ItemsDir + itemKey + ".asset");
            if (data == null)
            {
                Debug.LogWarning($"[LevelDefinitionBuilder] Missing item data {ItemsDir}{itemKey}.asset (run '3. Create Data' first); skipping {name}.");
                return null;
            }

            var go = InstantiatePrefab(pItemPickup, name, parent);
            if (go == null) return null;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;

            var pickup = go.GetComponent<ItemPickup>();
            if (pickup != null) pickup.item = data;
            else Debug.LogWarning($"[LevelDefinitionBuilder] {name} has no ItemPickup component.");

            pickupCount++;
            return go;
        }

        /// <summary>
        /// Stone plinth on Default (walkable, bakes into the NavMesh) plus a floating crystal on
        /// Interactable carrying the trigger. Mirrors LevelGreyboxBuilder.WandAltar exactly - divergence
        /// here means the two builders produce different altars from the same intent.
        /// </summary>
        static GameObject WandAltar(string name, Vector3 groundPos, float triggerRadius, Transform parent)
        {
            Box(name + "_Plinth", groundPos + Vector3.up * 0.4f, new Vector3(1.8f, 0.8f, 1.8f), mStone, parent);

            var altar = new GameObject(name);
            altar.transform.SetParent(parent, false);
            altar.transform.position = groundPos;

            var sc = altar.AddComponent<SphereCollider>();
            sc.center = new Vector3(0f, 1.2f, 0f);
            sc.radius = triggerRadius;
            sc.isTrigger = true;

            var pedestal = altar.AddComponent<WandPedestal>();

            var visual = Empty("Visual", groundPos + Vector3.up * 1.5f, Quaternion.identity, altar.transform);
            pedestal.visual = visual.transform;

            var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crystal.name = "Crystal";
            Object.DestroyImmediate(crystal.GetComponent<Collider>());
            crystal.transform.SetParent(visual.transform, false);
            crystal.transform.localScale = new Vector3(0.42f, 0.42f, 0.42f);
            crystal.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
            if (mCyan != null) crystal.GetComponent<Renderer>().sharedMaterial = mCyan;

            var lightGo = Empty("Glow", groundPos + Vector3.up * 1.6f, Quaternion.identity, altar.transform);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = 2.2f;
            light.shadows = LightShadows.None;
            if (mCyan != null) light.color = mCyan.HasProperty("_BaseColor") ? mCyan.GetColor("_BaseColor") : Color.cyan;

            SetLayerRecursively(altar, Layers.Interactable);
            return altar;
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        static GameObject InstantiatePrefab(GameObject prefab, string name, Transform parent)
        {
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }
    }
}
