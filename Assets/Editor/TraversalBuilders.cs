using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Editor-time entry points for a balloon or a sheet of water. The construction itself lives in the
    /// runtime <see cref="LevelPieceFactory"/> (shared with the in-game <see cref="LevelEditor"/>);
    /// these wrap it with the editor context - PrefabUtility instantiation so a placed balloon keeps its
    /// prefab link, static flags for batching - so <see cref="LevelDefinitionBuilder"/> and
    /// <see cref="SandboxBuilder"/> build exactly what the runtime builds.
    /// </summary>
    public static class TraversalBuilders
    {
        public static GameObject BuildBalloon(GameObject prefab, string name, Vector3 position,
                                              float launchSpeed, float respawnSeconds, float radius, Transform parent)
        {
            if (prefab == null)
            {
                Debug.LogWarning("[TraversalBuilders] Balloon prefab missing; skipping " + name +
                                 " (run VibeGame1/4. Build Prefabs).");
                return null;
            }
            var def = new BalloonDef { name = name, position = position, launchSpeed = launchSpeed, respawnSeconds = respawnSeconds, radius = radius };
            return LevelPieceFactory.Balloon(def, parent, EditorContext.With(prefab, null), null, 0);
        }

        public static GameObject BuildWater(string name, Vector3 center, Vector3 size, Vector3 flowDirection,
                                            float flowSpeed, Material material, Transform parent)
        {
            var def = new WaterDef { name = name, center = center, size = size, flowDirection = flowDirection, flowSpeed = flowSpeed };
            return LevelPieceFactory.Water(def, parent, EditorContext.With(null, material), null, 0);
        }
    }

    /// <summary>
    /// The editor-time <see cref="LevelPieceContext"/>: assets through AssetDatabase, prefab instances
    /// through PrefabUtility (the link survives), static flags through GameObjectUtility. One place, so
    /// every editor-side builder resolves keys the same way the runtime resolves its serialised library.
    /// </summary>
    public static class EditorContext
    {
        const string MatDir = "Assets/Materials/";
        const string PrefabDir = "Assets/Prefabs/";
        const string ItemsDir = "Assets/Data/Items/";

        public static LevelPieceContext Default()
        {
            return new LevelPieceContext
            {
                materials = key => AssetDatabase.LoadAssetAtPath<Material>(MatDir + "M_" + key + ".mat"),
                prefabs = key => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + key + ".prefab"),
                items = key => AssetDatabase.LoadAssetAtPath<ItemData>(ItemsDir + key + ".asset"),
                instantiate = prefab => (GameObject)PrefabUtility.InstantiatePrefab(prefab, UnityEngine.SceneManagement.SceneManager.GetActiveScene()),
                markStatic = go => GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI),
            };
        }

        /// <summary>A default context with one prefab / material pinned (the sandbox passes what it already loaded).</summary>
        public static LevelPieceContext With(GameObject balloonPrefab, Material water)
        {
            var c = Default();
            if (balloonPrefab != null) { var inner = c.prefabs; c.prefabs = key => key == "Balloon" ? balloonPrefab : inner(key); }
            if (water != null) { var innerM = c.materials; c.materials = key => key == "Water" ? water : innerM(key); }
            return c;
        }
    }
}
