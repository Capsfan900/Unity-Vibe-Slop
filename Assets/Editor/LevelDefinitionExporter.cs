using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Reads the "Level" root of the open scene and writes a <see cref="LevelDefinition"/> asset from it.
    ///
    /// This is the migration path. The existing level is ~200 lines of literal coordinates inside
    /// <c>LevelGreyboxBuilder</c>; nobody should retype those into an Inspector by hand. Export once, and
    /// the hand-coded level becomes editable data.
    ///
    /// It is also the correctness check: export the built level, rebuild it with
    /// <c>LevelDefinitionBuilder</c>, and the two representations should produce the same scene. If they
    /// diverge, one of the two builders has drifted.
    /// </summary>
    public static class LevelDefinitionExporter
    {
        const string LevelsDir = "Assets/Data/Levels";

        [MenuItem("VibeGame1/Export Current Level To Definition")]
        public static void Export()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[LevelDefinitionExporter] Exit play mode first — scene contents differ at runtime " +
                               "(enemies are spawned, pickups may be collected).");
                return;
            }

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            GameObject levelRoot = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == "Level") { levelRoot = go; break; }

            if (levelRoot == null)
            {
                Debug.LogError("[LevelDefinitionExporter] No 'Level' root in the open scene. Build a level first.");
                return;
            }

            DataFactory.EnsureFolder(LevelsDir);
            string sceneName = string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
            string path = LevelsDir + "/" + sceneName + "_Level.asset";
            var def = DataFactory.GetOrCreate<LevelDefinition>(path);

            def.sceneName = sceneName;
            if (string.IsNullOrWhiteSpace(def.levelId) || def.levelId == "level_01")
                def.levelId = sceneName.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(def.displayName)) def.displayName = sceneName;

            ExportInto(levelRoot, def);

            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = def;
            EditorGUIUtility.PingObject(def);

            Debug.Log($"[LevelDefinitionExporter] Exported '{def.SafeLevelId}' to {path}: " +
                      $"{def.platforms.Length} platforms, {def.spawns.Length} spawns, {def.pickups.Length} pickups, " +
                      $"{def.checkpoints.Length} checkpoints, {def.torches.Length} torches, {def.pedestals.Length} pedestals, " +
                      $"{def.balloons.Length} balloons, {def.waters.Length} waters, " +
                      $"{def.arenas.Length} arenas, sky={def.sky.enabled}.");
        }

        /// <summary>
        /// Read <paramref name="levelRoot"/> into <paramref name="def"/>, touching no assets. Split out of
        /// <see cref="Export"/> so a test can round-trip a temporary root through the exporter and the
        /// builder's helpers without writing a file. Sky parameters are left as authored (see below).
        /// </summary>
        public static void ExportInto(GameObject levelRoot, LevelDefinition def)
        {
            var platforms = new List<PlatformDef>();
            var spawns = new List<SpawnDef>();
            var balloons = new List<BalloonDef>();
            var waters = new List<WaterDef>();
            var pickups = new List<PickupDef>();
            var checkpoints = new List<CheckpointDef>();
            var torches = new List<TorchDef>();
            var pedestals = new List<PedestalDef>();
            var arenas = new List<ArenaDef>();

            // Pass 1: find the arenas first, so their gates are not also exported as plain platforms, and
            // so the plinth of a wand altar is not exported as a platform the builder would then double.
            var gateTransforms = new HashSet<Transform>();
            var skipNames = new HashSet<string>();
            foreach (var trig in levelRoot.GetComponentsInChildren<BossArenaTrigger>(true))
            {
                var col = trig.GetComponent<BoxCollider>();
                var a = new ArenaDef
                {
                    enabled = true,
                    triggerName = trig.name,
                    triggerPosition = trig.transform.position,
                    triggerSize = col != null ? col.size : new Vector3(6f, 4f, 2f),
                    gateOpenPosition = trig.gateOpenPosition,
                    gateClosedPosition = trig.gateClosedPosition,
                    clearSpawnerName = trig.clearSpawner != null ? trig.clearSpawner.name : "",
                };
                if (trig.gate != null)
                {
                    gateTransforms.Add(trig.gate);
                    a.gateName = trig.gate.name;
                    a.gateSize = trig.gate.localScale;
                    a.gateMaterialKey = MaterialKeyOf(trig.gate.gameObject);
                }
                if (trig.exitGate != null)
                {
                    gateTransforms.Add(trig.exitGate);
                    a.hasExitGate = true;
                    a.exitGateName = trig.exitGate.name;
                    a.exitGateSize = trig.exitGate.localScale;
                    a.exitGateMaterialKey = MaterialKeyOf(trig.exitGate.gameObject);
                    a.exitGateClosedPosition = trig.exitGateClosedPosition;
                    a.exitGateOpenPosition = trig.exitGateOpenPosition;
                }
                arenas.Add(a);
            }

            // Wand altars. The plinth is a plain box the altar builder makes for itself, so exporting it
            // as a platform too would stack two plinths on the next build.
            foreach (var ped in levelRoot.GetComponentsInChildren<WandPedestal>(true))
            {
                var sc = ped.GetComponent<SphereCollider>();
                pedestals.Add(new PedestalDef
                {
                    name = ped.name,
                    groundPosition = ped.transform.position,
                    triggerRadius = sc != null ? sc.radius : 3f,
                });
                skipNames.Add(ped.name + "_Plinth");
            }

            // The sky's build parameters (star count, seed, eclipse angles) cannot be recovered from the
            // mesh it produced. Record only that a sky exists; the numbers stay as authored on the asset.
            def.sky.enabled = false;
            foreach (Transform c in levelRoot.transform)
                if (c.name == "Sky") { def.sky.enabled = true; break; }

            // Pass 2: walk the direct children. Trim bars are parented UNDER their platform, so they never
            // surface here — that is what keeps them from being exported as platforms in their own right.
            foreach (Transform child in levelRoot.transform)
            {
                var go = child.gameObject;

                if (go.name == "StartSpawn")
                {
                    def.playerStart = child.position;
                    def.playerStartYaw = child.eulerAngles.y;
                    continue;
                }

                var killZone = go.GetComponent<KillZone>();
                if (killZone != null)
                {
                    var col = go.GetComponent<BoxCollider>();
                    def.killZone.name = go.name;
                    def.killZone.center = child.position;
                    if (col != null) def.killZone.size = col.size;
                    continue;
                }

                if (go.GetComponent<BossArenaTrigger>() != null) continue;   // captured in pass 1
                if (gateTransforms.Contains(child)) continue;
                if (go.GetComponent<WandPedestal>() != null) continue;       // captured in pass 1
                if (skipNames.Contains(go.name)) continue;                   // altar plinth, rebuilt by the altar
                if (go.name == "Sky") continue;

                var spawner = go.GetComponent<EnemySpawner>();
                if (spawner != null)
                {
                    spawns.Add(new SpawnDef
                    {
                        name = go.name,
                        prefabKey = spawner.prefab != null ? spawner.prefab.name : "",
                        position = child.position,
                        yaw = child.eulerAngles.y,
                        isBoss = spawner.isBoss,
                    });
                    continue;
                }

                var balloon = go.GetComponent<Balloon>();
                if (balloon != null)
                {
                    balloons.Add(new BalloonDef
                    {
                        name = go.name,
                        position = child.position,
                        launchSpeed = balloon.launchSpeed,
                        respawnSeconds = balloon.respawnSeconds,
                        radius = balloon.radius,
                    });
                    continue;
                }

                var water = go.GetComponent<WaterVolume>();
                if (water != null)
                {
                    waters.Add(new WaterDef
                    {
                        name = go.name,
                        center = child.position,
                        size = water.size,
                        flowDirection = water.flowDirection,
                        flowSpeed = water.flowSpeed,
                    });
                    continue;
                }

                var checkpoint = go.GetComponent<Checkpoint>();
                if (checkpoint != null)
                {
                    var offset = checkpoint.spawnPoint != null
                        ? checkpoint.spawnPoint.position - child.position
                        : new Vector3(0f, 0.2f, -2f);
                    checkpoints.Add(new CheckpointDef
                    {
                        name = go.name,
                        position = child.position,
                        spawnOffset = offset,
                    });
                    continue;
                }

                if (go.name == "Torches")
                {
                    foreach (Transform t in child)
                        torches.Add(new TorchDef { name = t.name, basePosition = t.position });
                    continue;
                }

                if (go.name == "Pickups")
                {
                    foreach (Transform p in child)
                    {
                        var pickup = p.GetComponent<ItemPickup>();
                        pickups.Add(new PickupDef
                        {
                            name = p.name,
                            itemKey = pickup != null && pickup.item != null ? pickup.item.name : "",
                            position = p.position,
                        });
                    }
                    continue;
                }

                // Anything left that renders a mesh is geometry.
                if (go.GetComponent<MeshRenderer>() == null) continue;

                bool hasTrim = false;
                string trimKey = "NeonPink";
                foreach (Transform sub in child)
                {
                    if (!sub.name.EndsWith("_Trim_N")) continue;
                    hasTrim = true;
                    trimKey = MaterialKeyOf(sub.gameObject);
                    break;
                }

                platforms.Add(new PlatformDef
                {
                    name = go.name,
                    center = child.position,
                    size = child.localScale,
                    materialKey = MaterialKeyOf(go),
                    trim = hasTrim,
                    trimMaterialKey = trimKey,
                    isStatic = GameObjectUtility.AreStaticEditorFlagsSet(go, StaticEditorFlags.BatchingStatic),
                });
            }

            def.platforms = platforms.ToArray();
            def.spawns = spawns.ToArray();
            def.pickups = pickups.ToArray();
            def.checkpoints = checkpoints.ToArray();
            def.torches = torches.ToArray();
            def.pedestals = pedestals.ToArray();
            def.balloons = balloons.ToArray();
            def.waters = waters.ToArray();
            def.arenas = arenas.ToArray();
        }

        /// <summary>'M_Platform' -> 'Platform'. Keys omit the prefix so definitions read cleanly.</summary>
        static string MaterialKeyOf(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null || r.sharedMaterial == null) return "";
            string n = r.sharedMaterial.name;
            return n.StartsWith("M_") ? n.Substring(2) : n;
        }
    }
}
