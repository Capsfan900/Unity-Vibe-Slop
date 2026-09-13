using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using TMPro;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace VibeGame1.EditorTools
{
    public sealed class LevelBuildResult { public bool success; public string message, scenePath; }

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
        const string RegistryPath = "Assets/Data/LevelRegistry.asset";

        static GameObject pPlayer, pManagers, pHud;

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
            string runScoreError;
            if (!RunScoreMath.TryValidateDefinition(def, out runScoreError))
            {
                Debug.LogError("[LevelDefinitionBuilder] Invalid run scoring on '" + def.SafeLevelId + "': " + runScoreError);
                return;
            }

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
            var runScorer = level.AddComponent<LevelRunScorer>();
            runScorer.definition = def;
            runScorer.registry = AssetDatabase.LoadAssetAtPath<LevelRegistry>(RegistryPath);

            var content = BuildWorldContent(def, new LevelWorldContentContext
            {
                root = root,
                pieces = EditorContext.Default(),
                builtSpawners = new System.Collections.Generic.Dictionary<string, EnemySpawner>(),
                pedestalStone = LoadMat("Stone"),
                pedestalCyan = LoadMat("NeonCyan"),
                cloudSea = LoadMat("CloudSea"),
                safety = LevelWorldContentSafety.CampaignRuntime,
                buildCloudSea = true
            });
            var counts = content.counts;
            var startSpawn = content.startSpawn;

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

            Debug.Log($"[LevelDefinitionBuilder] Built '{def.SafeLevelId}' ({def.displayName}): {counts}, " +
                      $"navmesh built={surface.navMeshData != null}, {debris} debris roots removed, " +
                      $"scene saved '{scene.path}'.");
        }

        /// <summary>
        /// Builds the definition's world content below <paramref name="context"/>'s explicit root.
        /// This deliberately has no scene lifecycle policy: it never opens, saves or clears scenes,
        /// bakes NavMesh, creates Player/Managers/HUD, or adds a LevelRunScorer. Those campaign-only
        /// responsibilities remain in <see cref="Build"/>.  A caller also chooses whether live runtime
        /// coordinators/triggers are installed, so editor preview can render the same authored content
        /// without becoming a second gameplay world.
        /// </summary>
        public static LevelWorldContentResult BuildWorldContent(LevelDefinition def, LevelWorldContentContext context)
        {
            if (def == null) throw new System.ArgumentNullException(nameof(def));
            if (context == null) throw new System.ArgumentNullException(nameof(context));
            if (context.root == null) throw new System.ArgumentException("World content needs an explicit root.", nameof(context));
            if (context.pieces == null) throw new System.ArgumentException("World content needs an explicit piece context.", nameof(context));
            if (context.builtSpawners == null)
                context.builtSpawners = new System.Collections.Generic.Dictionary<string, EnemySpawner>();

            context.builtSpawners.Clear();
            Transform root = context.root;
            var counts = LevelPieceFactory.BuildDocument(LevelDocument.FromDefinition(def), root, context.pieces);
            var startSpawnT = root.Find("StartSpawn");
            var startSpawn = startSpawnT != null ? startSpawnT.gameObject
                           : LevelPieceFactory.PlayerStart(def.playerStart, def.playerStartYaw, root);
            foreach (var sp in root.GetComponentsInChildren<EnemySpawner>(true)) context.builtSpawners[sp.name] = sp;

            if (context.BuildsRuntimeBehaviours && def.projectileSequences != null)
            {
                foreach (var sequence in def.projectileSequences)
                {
                    if (sequence == null || sequence.spawnerNames == null || sequence.spawnerNames.Length == 0) continue;
                    if (!sequence.CoordinatesRuntime) continue;
                    var ordered = new System.Collections.Generic.List<EnemySpawner>();
                    bool complete = true;
                    foreach (string spawnerName in sequence.spawnerNames)
                    {
                        EnemySpawner member;
                        if (context.builtSpawners.TryGetValue(spawnerName, out member)) ordered.Add(member);
                        else
                        {
                            complete = false;
                            Debug.LogWarning("[LevelDefinitionBuilder] Projectile sequence '" + sequence.name +
                                             "' names missing spawner '" + spawnerName + "'.");
                        }
                    }
                    if (!complete) continue;
                    var host = LevelPieceFactory.Empty(sequence.name, Vector3.zero, Quaternion.identity, root);
                    host.AddComponent<ProjectileVolleySequence>().Configure(
                        ordered.ToArray(), sequence.recoveryGap, sequence.readinessTimeout,
                        sequence.shotResolutionTimeout,
                        sequence.progressOrigin, sequence.progressDirection, sequence.memberProgressGates,
                        null, sequence.repeatFromIndex, sequence.firstMemberAcquireDelay);
                }
            }

            if (def.pedestals != null)
            {
                foreach (var pd in def.pedestals)
                {
                    if (pd == null) continue;
                    WandAltar(pd.name, pd.groundPosition, pd.triggerRadius, root, context.pieces,
                              context.pedestalStone, context.pedestalCyan);
                }
            }

            if (def.arenas != null)
            {
                foreach (var a in def.arenas)
                {
                    if (a == null || !a.enabled) continue;
                    var gate = LevelPieceFactory.Box(a.gateName, a.gateOpenPosition, a.gateSize,
                                                     context.pieces.Material(a.gateMaterialKey), root, context.pieces,
                                                     false, counts);
                    var trigger = LevelPieceFactory.Empty(a.triggerName, a.triggerPosition, Quaternion.identity, root);
                    var col = trigger.AddComponent<BoxCollider>();
                    col.size = a.triggerSize;
                    col.isTrigger = true;
                    BossArenaTrigger fight = null;
                    if (context.BuildsRuntimeBehaviours)
                    {
                        fight = trigger.AddComponent<BossArenaTrigger>();
                        fight.gate = gate.transform;
                        fight.gateOpenPosition = a.gateOpenPosition;
                        fight.gateClosedPosition = a.gateClosedPosition;
                    }

                    if (a.hasExitGate)
                    {
                        var exit = LevelPieceFactory.Box(a.exitGateName, a.exitGateClosedPosition, a.exitGateSize,
                                                         context.pieces.Material(a.exitGateMaterialKey), root,
                                                         context.pieces, false, counts);
                        if (fight != null)
                        {
                            fight.exitGate = exit.transform;
                            fight.exitGateClosedPosition = a.exitGateClosedPosition;
                            fight.exitGateOpenPosition = a.exitGateOpenPosition;
                        }
                    }

                    if (fight != null && !string.IsNullOrEmpty(a.clearSpawnerName))
                    {
                        EnemySpawner sp;
                        if (context.builtSpawners.TryGetValue(a.clearSpawnerName, out sp)) fight.clearSpawner = sp;
                        else Debug.LogWarning("[LevelDefinitionBuilder] Arena '" + a.triggerName + "' names clearSpawner '" +
                                              a.clearSpawnerName + "', which is not a spawn in this definition (a missing " +
                                              "mini-boss prefab also drops its spawner). Its exit gate will never open.");
                    }

                    if (a.solarRealm != null && a.solarRealm.enabled)
                        BuildSolarRealm(a, fight, root, context.pieces, context.builtSpawners,
                                        context.BuildsRuntimeBehaviours);
                }
            }

            if (def.sky != null && def.sky.enabled)
            {
                var skyGroup = new GameObject("Sky");
                skyGroup.transform.SetParent(root, false);
                Starfield.Build(skyGroup.transform, def.sky.starCount, def.sky.radius, def.sky.seed,
                                def.sky.includeEclipse, def.sky.eclipseYawDeg, def.sky.eclipsePitchDeg,
                                def.sky.eclipseDiameterDeg);
            }

            if (context.buildCloudSea) CloudSea.BuildCampaign(root, context.cloudSea);
            BuildWorldLeaderboard(def, root, context.pieces);
            BuildChallengeRoutes(def, root);

            if (def.killZone != null)
            {
                var kill = LevelPieceFactory.Empty(def.killZone.name, def.killZone.center, Quaternion.identity, root);
                var kc = kill.AddComponent<BoxCollider>();
                kc.size = def.killZone.size;
                kc.isTrigger = true;
                if (context.BuildsRuntimeBehaviours) kill.AddComponent<KillZone>();
            }

            if (context.safety == LevelWorldContentSafety.PreviewSafe)
                NeutralizePreviewRuntime(root);

            return new LevelWorldContentResult
            {
                root = root,
                startSpawn = startSpawn,
                counts = counts,
                builtSpawners = context.builtSpawners
            };
        }

        public static LevelBuildResult TryBuild(LevelDefinition def)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return new LevelBuildResult { message = "Build refused during play mode." };
            if (def == null) return new LevelBuildResult { message = "Level definition is missing." };
            string scoringError;
            if (!RunScoreMath.TryValidateDefinition(def, out scoringError)) return new LevelBuildResult { message = scoringError };
            var active = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(def.sceneName) && active.name != def.sceneName) return new LevelBuildResult { message = "The active scene does not match the level definition." };
            try { Build(def); return new LevelBuildResult { success = true, scenePath = active.path, message = "Level built." }; }
            catch (System.Exception e) { return new LevelBuildResult { message = e.Message, scenePath = active.path }; }
        }

        /// <summary>
        /// Leaves a preview's data-bearing scene artifacts inspectable while preventing the temporary
        /// root from becoming a gameplay participant if it survives into play mode. This is intentionally
        /// post-construction: it applies equally to direct factory pieces, prefab-backed pieces and the
        /// builder-owned altar/arena/realm presentation without forking their visual construction path.
        /// </summary>
        static void NeutralizePreviewRuntime(Transform root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                // CloudSea owns a generated presentation mesh and releases it from OnDisable. It has no
                // collider, input, combat, player or event behaviour, so keeping this one presentation
                // component alive preserves the authored cloud visual without reactivating gameplay.
                if (!(behaviour is CloudSea)) behaviour.enabled = false;
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

        // ---- helpers ------------------------------------------------------------------------------
        // Mirrors LevelGreyboxBuilder's helpers deliberately: same geometry, same static flags, same
        // collider/trim rules. Divergence here would mean the two builders silently produce different
        // levels from the same intent.

        static void LoadShared()
        {
            pPlayer = LoadPrefab("Player");
            pManagers = LoadPrefab("Managers");
            pHud = LoadPrefab("HUD");
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

        // Box / Trim / Empty / Torch / ItemAt moved to LevelPieceFactory (runtime), shared with LevelEditor.

        /// <summary>
        /// Stone plinth on Default (walkable, bakes into the NavMesh) plus a floating crystal on
        /// Interactable carrying the trigger. Mirrors LevelGreyboxBuilder.WandAltar exactly - divergence
        /// here means the two builders produce different altars from the same intent.
        /// </summary>
        static GameObject WandAltar(string name, Vector3 groundPos, float triggerRadius, Transform parent,
                                    LevelPieceContext ctx, Material stone, Material cyan)
        {
            LevelPieceFactory.Box(name + "_Plinth", groundPos + Vector3.up * 0.4f, new Vector3(1.8f, 0.8f, 1.8f), stone, parent, ctx, true, null);

            var altar = new GameObject(name);
            altar.transform.SetParent(parent, false);
            altar.transform.position = groundPos;

            var sc = altar.AddComponent<SphereCollider>();
            sc.center = new Vector3(0f, 1.2f, 0f);
            sc.radius = triggerRadius;
            sc.isTrigger = true;

            var pedestal = altar.AddComponent<WandPedestal>();

            var visual = LevelPieceFactory.Empty("Visual", groundPos + Vector3.up * 1.5f, Quaternion.identity, altar.transform);
            pedestal.visual = visual.transform;

            var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crystal.name = "Crystal";
            Object.DestroyImmediate(crystal.GetComponent<Collider>());
            crystal.transform.SetParent(visual.transform, false);
            crystal.transform.localScale = new Vector3(0.42f, 0.42f, 0.42f);
            crystal.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
            if (cyan != null) crystal.GetComponent<Renderer>().sharedMaterial = cyan;

            var lightGo = LevelPieceFactory.Empty("Glow", groundPos + Vector3.up * 1.6f, Quaternion.identity, altar.transform);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = 2.2f;
            light.shadows = LightShadows.None;
            if (cyan != null) light.color = cyan.HasProperty("_BaseColor") ? cyan.GetColor("_BaseColor") : Color.cyan;

            SetLayerRecursively(altar, Layers.Interactable);
            return altar;
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        static void BuildWorldLeaderboard(LevelDefinition level, Transform parent, LevelPieceContext ctx)
        {
            var data = level.worldLeaderboard;
            if (data == null || !data.enabled) return;

            float width = Mathf.Max(4f, data.size.x);
            float height = Mathf.Max(2.5f, data.size.y);
            var root = LevelPieceFactory.Empty(
                string.IsNullOrWhiteSpace(data.name) ? "WorldLeaderboard" : data.name,
                data.position, Quaternion.Euler(0f, data.yaw, 0f), parent);
            var view = root.AddComponent<WorldLeaderboardView>();

            Material backing = ctx.Material(data.backingMaterialKey);
            Material glow = ctx.Material(data.glowMaterialKey);
            LeaderboardVisualBox("Backing", new Vector3(0f, 0f, 0.12f),
                new Vector3(width, height, 0.24f), backing, root.transform);

            const float rail = 0.13f;
            float faceZ = -0.18f;
            LeaderboardVisualBox("Glow_Top", new Vector3(0f, height * 0.5f, faceZ),
                new Vector3(width + rail, rail, rail), glow, root.transform);
            LeaderboardVisualBox("Glow_Bottom", new Vector3(0f, -height * 0.5f, faceZ),
                new Vector3(width + rail, rail, rail), glow, root.transform);
            LeaderboardVisualBox("Glow_Left", new Vector3(-width * 0.5f, 0f, faceZ),
                new Vector3(rail, height, rail), glow, root.transform);
            LeaderboardVisualBox("Glow_Right", new Vector3(width * 0.5f, 0f, faceZ),
                new Vector3(rail, height, rail), glow, root.transform);
            LeaderboardVisualBox("Glow_Divider", new Vector3(0f, height * 0.19f, faceZ),
                new Vector3(width * 0.90f, rail * 0.55f, rail * 0.55f), glow, root.transform);

            var canvasGo = new GameObject("WorldCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 2;
            var canvasRect = canvasGo.GetComponent<RectTransform>();
            float metresPerUnit = width / 1000f;
            canvasRect.sizeDelta = new Vector2(1000f, height / metresPerUnit);
            canvasRect.localScale = Vector3.one * metresPerUnit;
            canvasRect.localPosition = new Vector3(0f, 0f, -0.25f);

            var heading = LeaderboardText(canvasRect, "Heading", 64f, FontStyles.Bold,
                TextAlignmentOptions.Center, new Vector2(0.06f, 0.72f), new Vector2(0.94f, 0.96f),
                new Color(0.86f, 1f, 1f, 1f));
            var rows = LeaderboardText(canvasRect, "Rows", 26f, FontStyles.Normal,
                TextAlignmentOptions.TopLeft, new Vector2(0.10f, 0.14f), new Vector2(0.90f, 0.68f),
                new Color(0.73f, 0.87f, 0.89f, 1f));
            rows.lineSpacing = 4f;
            var footer = LeaderboardText(canvasRect, "Footer", 25f, FontStyles.Normal,
                TextAlignmentOptions.Center, new Vector2(0.08f, 0.025f), new Vector2(0.92f, 0.12f),
                new Color(0.48f, 0.68f, 0.71f, 1f));

            view.Configure(level.displayName, data, heading, rows, footer);
            SetLayerRecursively(root, Starfield.SkyLayer);
        }

        static void BuildChallengeRoutes(LevelDefinition level, Transform parent)
        {
            if (level.challengeRoutes == null) return;
            foreach (var data in level.challengeRoutes)
            {
                if (data == null) continue;

                var root = new GameObject(ChallengeRouteMarker.NameFor(data.routeId));
                root.transform.SetParent(parent, false);
                root.AddComponent<ChallengeRouteMarker>().Configure(data);
            }
        }

        static GameObject LeaderboardVisualBox(string name, Vector3 localPosition, Vector3 localScale,
                                               Material material, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        static TMP_Text LeaderboardText(RectTransform parent, string name, float size, FontStyles style,
                                        TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax,
                                        Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        static void BuildSolarRealm(ArenaDef arena, BossArenaTrigger fight, Transform levelRoot, LevelPieceContext ctx,
                                    System.Collections.Generic.IDictionary<string, EnemySpawner> spawnerLookup,
                                    bool buildRuntimeBehaviours)
        {
            var def = arena.solarRealm;
            EnemySpawner spawner;
            if (!spawnerLookup.TryGetValue(def.enemySpawnerName, out spawner))
            {
                Debug.LogWarning("[LevelDefinitionBuilder] Solar arena '" + arena.gateName +
                                 "' names missing spawner '" + def.enemySpawnerName + "'.");
            }
            else
            {
                // SpawnDef coordinates remain the historical exterior anchors. Moving only the built
                // marker keeps ApplyDescent and repeated authoring migrations independent of realm space.
                var placement = spawner.gameObject.AddComponent<SolarRealmPlacement>();
                placement.authoredPosition = spawner.transform.position;
                placement.authoredYaw = spawner.transform.eulerAngles.y;
                spawner.transform.position = def.enemySpawnPosition;
                spawner.transform.rotation = Quaternion.Euler(0f, def.enemySpawnYaw, 0f);
            }

            if (!string.IsNullOrEmpty(def.arenaPickupName))
            {
                var pickup = levelRoot.Find("Pickups/" + def.arenaPickupName);
                if (pickup != null)
                {
                    var placement = pickup.gameObject.AddComponent<SolarRealmPlacement>();
                    placement.authoredPosition = pickup.position;
                    placement.authoredYaw = pickup.eulerAngles.y;
                    pickup.position = def.arenaPickupPosition;
                }
                else Debug.LogWarning("[LevelDefinitionBuilder] Solar arena '" + arena.gateName +
                                      "' names missing pickup '" + def.arenaPickupName + "'.");
            }

            var exterior = LevelPieceFactory.Empty(arena.triggerName + "_SolarPortal", def.exteriorCenter,
                                                     Quaternion.identity, levelRoot);
            var trigger = exterior.AddComponent<SphereCollider>();
            trigger.radius = def.exteriorRadius;
            trigger.isTrigger = true;
            SolarArenaPortal portal = null;
            if (buildRuntimeBehaviours)
            {
                portal = exterior.AddComponent<SolarArenaPortal>();
                portal.arena = fight;
                portal.definition = CloneSolarRealm(def);
                if (fight != null) fight.solarPortal = portal;
            }

            float visualRadius = def.visualRadius > 0f ? def.visualRadius : def.exteriorRadius;
            var plasma = VisualSphere("Plasma", def.exteriorCenter, visualRadius * 2f,
                                      ctx.Material(def.themeMaterialKey), exterior.transform);
            var corona = VisualSphere("Corona", def.exteriorCenter, visualRadius * 2.12f,
                                      ctx.Material("SolarCorona"), exterior.transform);
            var spin = exterior.AddComponent<SolarArenaVisual>();
            spin.plasma = plasma.transform;
            spin.corona = corona.transform;

            var realm = LevelPieceFactory.Empty(arena.triggerName + "_Realm", def.realmCenter,
                                                 Quaternion.identity, levelRoot);
            if (portal != null)
            {
                portal.realmBoundsCenter = realm.transform;
                portal.realmContainmentRadius = def.realmShellRadius;
            }

            // A round, disconnected NavMesh island. Collision is stationary; only the shell visuals spin.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            // Unity's primitive cylinder uses a CapsuleCollider, which bulges under this wide, shallow
            // scale. Match the stationary walkable disc exactly so physics agrees with its NavMesh.
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.AddComponent<MeshCollider>().sharedMesh = floor.GetComponent<MeshFilter>().sharedMesh;
            floor.transform.SetParent(realm.transform, false);
            floor.transform.position = def.realmCenter + Vector3.down * 0.5f;
            floor.transform.localScale = new Vector3(def.realmFloorRadius * 2f, 0.5f, def.realmFloorRadius * 2f);
            floor.layer = 0;
            var floorMat = ctx.Material("Platform");
            if (floorMat != null) floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            ctx.MarkStatic(floor);

            var realmMaterial = ctx.Material(RealmMaterialKey(def.themeMaterialKey));
            BuildRealmBoundary(realm.transform, def.realmCenter, def.realmFloorRadius, realmMaterial, ctx);

            // The opaque two-sided shell closes the room against the campaign sky. Its emission is
            // deliberately dim; a separate procedural disc above the arena carries the solar motion.
            VisualSphere("OpaqueRealmShell", def.realmCenter + Vector3.up * 6f,
                         def.realmShellRadius * 2f, realmMaterial, realm.transform);
            var innerShell = VisualDisc("SolarCeiling", def.realmCenter + Vector3.up * 11.9f,
                                        def.realmFloorRadius * 1.15f, ctx.Material(def.themeMaterialKey), realm.transform);
            var innerSpin = realm.AddComponent<SolarArenaVisual>();
            innerSpin.plasma = innerShell.transform;
            innerSpin.plasmaDegreesPerSecond = new Vector3(0f, 3.5f, 0f);
            // The ceiling fills much more of the frame than an exterior sun. Serialize the override on
            // its visual owner so reopening the scene restores the renderer property block at runtime.
            innerSpin.plasmaOpacityOverride = 0.20f;
            innerSpin.plasmaSurfaceOpacityOverride = 0f;
            innerSpin.ApplyMaterialOverrides();

            var lightGo = LevelPieceFactory.Empty("SolarLight", def.realmCenter + Vector3.up * 9f,
                                                  Quaternion.identity, realm.transform);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = ThemeColor(def.themeMaterialKey);
            light.range = def.realmFloorRadius * 2.2f;
            light.intensity = 2.6f;
            light.shadows = LightShadows.None;

            Transform realmEntry = Marker("RealmEntry", def.playerEntryPosition, def.playerEntryYaw, realm.transform);
            Transform worldRetry = Marker("WorldRetry", def.retryPosition, def.retryYaw, exterior.transform);
            Transform worldReturn = Marker("WorldReturn", def.returnPosition, def.returnYaw, exterior.transform);
            if (portal != null)
            {
                portal.realmEntry = realmEntry;
                portal.worldRetry = worldRetry;
                portal.worldReturn = worldReturn;
                portal.hasReturn = def.hasReturn;
            }

            if (def.hasReturn)
            {
                var exit = LevelPieceFactory.Empty("RealmExit", def.realmExitPosition, Quaternion.identity, realm.transform);
                var exitCol = exit.AddComponent<SphereCollider>();
                exitCol.radius = 1.5f;
                exitCol.isTrigger = true;
                if (portal != null)
                {
                    var exitTrigger = exit.AddComponent<SolarRealmExitTrigger>();
                    exitTrigger.portal = portal;
                }
                VisualSphere("ExitGlow", def.realmExitPosition, 2.5f, ctx.Material("SolarCorona"), exit.transform);
                if (portal != null) portal.realmExitRoot = exit;
            }
        }

        static Transform Marker(string name, Vector3 position, float yaw, Transform parent)
        {
            return LevelPieceFactory.Empty(name, position, Quaternion.Euler(0f, yaw, 0f), parent).transform;
        }

        static GameObject VisualSphere(string name, Vector3 position, float diameter, Material material, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * diameter;
            go.layer = Starfield.SkyLayer;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        static GameObject VisualDisc(string name, Vector3 position, float diameter, Material material, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.localScale = new Vector3(diameter, 0.08f, diameter);
            go.layer = Starfield.SkyLayer;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        static void BuildRealmBoundary(Transform parent, Vector3 center, float floorRadius, Material material,
                                       LevelPieceContext ctx)
        {
            const int Segments = 20;
            float wallRadius = floorRadius + 0.35f;
            float wallLength = 2f * wallRadius * Mathf.Tan(Mathf.PI / Segments) + 0.5f;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * 360f / Segments;
                float rad = angle * Mathf.Deg2Rad;
                LevelPieceFactory.Box("Boundary_" + i,
                    center + new Vector3(Mathf.Sin(rad) * wallRadius, 6f, Mathf.Cos(rad) * wallRadius),
                    new Vector3(wallLength, 12f, 1f), material, parent, ctx, true, null)
                    .transform.rotation = Quaternion.Euler(0f, angle, 0f);
            }

            LevelPieceFactory.Box("Boundary_Ceiling", center + Vector3.up * 12.5f,
                                  new Vector3(floorRadius * 2f, 1f, floorRadius * 2f),
                                  material, parent, ctx, true, null);
        }

        static string RealmMaterialKey(string solarKey)
        {
            if (solarKey == "SolarGold") return "SolarRealmGold";
            if (solarKey == "SolarAzure") return "SolarRealmAzure";
            if (solarKey == "SolarGhost") return "SolarRealmGhost";
            return "SolarRealmCyan";
        }

        static SolarRealmDef CloneSolarRealm(SolarRealmDef source)
        {
            return source == null ? null : JsonUtility.FromJson<SolarRealmDef>(JsonUtility.ToJson(source));
        }

        static Color ThemeColor(string key)
        {
            if (key == "SolarGold") return new Color(0.85f, 0.68f, 0.16f);
            if (key == "SolarAzure") return new Color(0.20f, 0.42f, 1f);
            if (key == "SolarGhost") return new Color(0.25f, 0.88f, 0.48f);
            return new Color(0.21f, 0.86f, 0.93f);
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
