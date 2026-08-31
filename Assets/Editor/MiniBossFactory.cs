using VibeGame1;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Builds the three legendary mini-boss prefabs: Legendary_Ninja, Legendary_Knight,
    /// Legendary_Spellsword.
    ///
    /// Split out of <see cref="PrefabFactory"/> deliberately, following the <see cref="WandFactory"/>
    /// precedent: the mini-bosses are content, they are re-tuned far more often than the player rig,
    /// and a separate menu item means iterating on them never rebuilds the whole prefab set.
    ///
    /// <para><b>These are plain <see cref="EnemyController"/>s, not <see cref="BossController"/>s.</b>
    /// BossController owns deathblow segments, <c>Health.deathIsStagger</c>, the HUD boss bar and
    /// <c>RaiseBossDefeated</c> — and that last one stops the speedrun timer and clears the level. A
    /// mini-boss wired as a boss would end the run three times before the Warden. They read as
    /// "legendary" through their <c>EnemyData</c> (size, palette, souls, moveset) plus a distinct
    /// silhouette, not through a different controller.</para>
    ///
    /// <para>The rig mirrors <c>PrefabFactory.BuildEnemy</c> exactly — same child names, same
    /// <see cref="EnemyVisuals"/> bindings, same layer, same posture bar — because
    /// <c>EnemyVisuals</c> and <c>EnemyController</c> resolve those by reference, and the collider /
    /// NavMeshAgent footprint is re-sized at runtime from <c>EnemyData.scale</c>.</para>
    /// </summary>
    public static class MiniBossFactory
    {
        const string PrefabDir = "Assets/Prefabs";
        const string MaterialDir = "Assets/Materials";
        const string EnemyDataDir = "Assets/Data/Enemies";

        [MenuItem("VibeGame1/4b. Build Mini-Bosses")]
        public static void CreateAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[MiniBossFactory] Refusing to run in play mode. Exit play mode first.");
                return;
            }

            DataFactory.EnsureFolder(PrefabDir);

            BuildMiniBoss("Legendary_Ninja", EnemyDataDir + "/Legendary_Ninja.asset", Silhouette.Ninja);
            BuildMiniBoss("Legendary_Knight", EnemyDataDir + "/Legendary_Knight.asset", Silhouette.Knight);
            BuildMiniBoss("Legendary_Spellsword", EnemyDataDir + "/Legendary_Spellsword.asset", Silhouette.Spellsword);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MiniBossFactory] Built 3 legendary mini-boss prefabs under " + PrefabDir);
        }

        enum Silhouette { Ninja, Knight, Spellsword }

        // ------------------------------------------------------------------ the rig

        static void BuildMiniBoss(string name, string dataPath, Silhouette shape)
        {
            var data = Load<EnemyData>(dataPath);
            if (data == null)
            {
                Debug.LogError("[MiniBossFactory] Missing EnemyData " + dataPath +
                               " — run VibeGame1/3. Create Data first. Skipping " + name + ".");
                return;
            }

            var root = new GameObject(name);
            root.layer = Layers.Enemy;

            // Baseline footprint only: EnemyController.Init re-sizes agent and collider by data.scale.
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.45f;
            agent.height = 2f;
            agent.speed = 4f;
            agent.angularSpeed = 360f;
            agent.acceleration = 40f;
            agent.stoppingDistance = 1.5f;

            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.radius = 0.45f;
            col.height = 2f;
            col.isTrigger = false;

            root.AddComponent<Health>();
            root.AddComponent<Posture>();

            var ctrl = root.AddComponent<EnemyController>();
            ctrl.data = data;

            // M_Boss for all three: they are duels, and the boss body material is the visual language
            // the player already reads as "this one is a fight, not a filler".
            Material bodyMat = Mat("M_Boss");

            var visual = Empty("Visual", root.transform, Vector3.zero);
            var visuals = visual.AddComponent<EnemyVisuals>();
            var flash = visual.AddComponent<EmissiveFlash>();

            var lungeRoot = Empty("LungeRoot", visual.transform, Vector3.zero);

            // Body proportions carry most of the read at a glance: thin/fast, huge/slow, tall/ornate.
            Vector3 bodyScale;
            switch (shape)
            {
                case Silhouette.Ninja: bodyScale = new Vector3(0.68f, 0.98f, 0.68f); break;
                case Silhouette.Knight: bodyScale = new Vector3(1.25f, 1.02f, 1.25f); break;
                default: bodyScale = new Vector3(0.85f, 1.08f, 0.85f); break;
            }
            var body = Prim(PrimitiveType.Capsule, "Body", lungeRoot.transform, new Vector3(0f, 1f, 0f), bodyScale, bodyMat);
            var eye = Prim(PrimitiveType.Cube, "Eye", lungeRoot.transform, new Vector3(0f, 1.55f, 0.4f), new Vector3(0.3f, 0.12f, 0.15f), Mat("M_EnemyEye"));

            // ---- arm rig: shoulder -> upper arm -> forearm/hand -> weapon --------------------------
            // Same names and hierarchy as PrefabFactory.BuildEnemy: EnemyVisuals rotates the shoulder to
            // sell the wind-up, and the hand trails it. Do not rename these.
            var shoulder = Empty("ArmPivot", lungeRoot.transform, new Vector3(0.5f, 1.45f, 0f));
            Prim(PrimitiveType.Cube, "UpperArm", shoulder.transform, new Vector3(0f, -0.28f, 0f), new Vector3(0.17f, 0.56f, 0.17f), bodyMat);

            var hand = Empty("WeaponPivot", shoulder.transform, new Vector3(0f, -0.56f, 0f));
            Prim(PrimitiveType.Cube, "ForeArm", hand.transform, new Vector3(0f, -0.22f, 0f), new Vector3(0.14f, 0.46f, 0.14f), bodyMat);

            GameObject weapon;
            switch (shape)
            {
                case Silhouette.Ninja:
                    // short, straight, held low — a blade you can throw five of in the time of one cleave
                    weapon = Prim(PrimitiveType.Cube, "Weapon", hand.transform, new Vector3(0f, -0.4f, 0.34f), new Vector3(0.08f, 0.1f, 1.05f), bodyMat);
                    weapon.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
                    // off-hand tanto: the second blade is the whole point of the archetype
                    var offhand = Empty("OffhandPivot", lungeRoot.transform, new Vector3(-0.42f, 1.1f, 0.12f));
                    var tanto = Prim(PrimitiveType.Cube, "OffhandBlade", offhand.transform, new Vector3(0f, 0f, 0.3f), new Vector3(0.07f, 0.09f, 0.7f), bodyMat);
                    tanto.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
                    break;

                case Silhouette.Knight:
                    // a slab. Long enough that the overhead's arc is visible from 4 m.
                    weapon = Prim(PrimitiveType.Cube, "Weapon", hand.transform, new Vector3(0f, -0.62f, 0.55f), new Vector3(0.26f, 0.2f, 1.95f), bodyMat);
                    weapon.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
                    Prim(PrimitiveType.Cube, "PauldronR", lungeRoot.transform, new Vector3(0.62f, 1.62f, 0f), new Vector3(0.45f, 0.28f, 0.5f), bodyMat);
                    Prim(PrimitiveType.Cube, "PauldronL", lungeRoot.transform, new Vector3(-0.62f, 1.62f, 0f), new Vector3(0.45f, 0.28f, 0.5f), bodyMat);
                    Prim(PrimitiveType.Cube, "Shield", lungeRoot.transform, new Vector3(-0.62f, 1.08f, 0.28f), new Vector3(0.16f, 0.95f, 0.7f), bodyMat);
                    break;

                default:
                    // long ceremonial blade plus a floating focus: the hybrid read, melee AND caster
                    weapon = Prim(PrimitiveType.Cube, "Weapon", hand.transform, new Vector3(0f, -0.52f, 0.5f), new Vector3(0.13f, 0.15f, 1.7f), bodyMat);
                    weapon.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
                    var focus = Empty("FocusPivot", lungeRoot.transform, new Vector3(-0.55f, 1.5f, 0.25f));
                    Prim(PrimitiveType.Sphere, "Focus", focus.transform, Vector3.zero, new Vector3(0.26f, 0.26f, 0.26f), Mat("M_NeonPink"));
                    Prim(PrimitiveType.Cube, "Mantle", lungeRoot.transform, new Vector3(0f, 1.72f, -0.1f), new Vector3(0.95f, 0.22f, 0.6f), bodyMat);
                    break;
            }

            var alert = Prim(PrimitiveType.Cube, "Alert", visual.transform, new Vector3(0f, 2.5f, 0f), new Vector3(0.25f, 0.25f, 0.25f), Mat("M_AlertTell"));
            alert.SetActive(false);

            visuals.body = body.GetComponent<Renderer>();
            visuals.eye = eye.GetComponent<Renderer>();
            visuals.weapon = weapon.GetComponent<Renderer>();
            visuals.lungeRoot = lungeRoot.transform;
            visuals.armPivot = shoulder.transform;
            visuals.weaponPivot = hand.transform;
            visuals.alertMarker = alert;
            flash.renderers = new[] { visuals.body, visuals.weapon };

            // Posture bar, as on Grunt/Heavy. The HUD boss bar belongs to the Warden alone — these three
            // are read from the world-space bar, which is also the "execute me now" pulse on stagger.
            BuildPostureBar(visual.transform);

            Save(root, PrefabDir + "/" + name + ".prefab");
        }

        /// <summary>Two quads above the head: dark backing plus a left-anchored fill pivot scaled 0..1.
        /// Mirrors <c>PrefabFactory.BuildPostureBar</c>; <see cref="EnemyPostureBar"/> drives the pivot's
        /// X scale, never <c>Image.fillAmount</c>.</summary>
        static void BuildPostureBar(Transform parent)
        {
            const float width = 1.1f;
            const float height = 0.13f;

            var bar = Empty("PostureBar", parent, new Vector3(0f, 2.6f, 0f));
            var comp = bar.AddComponent<EnemyPostureBar>();

            var bg = Prim(PrimitiveType.Quad, "BarBG", bar.transform, Vector3.zero, new Vector3(width, height, 1f), Mat("M_Ground"));

            var fillPivot = Empty("FillPivot", bar.transform, new Vector3(-width * 0.5f, 0f, 0f));
            var fill = Prim(PrimitiveType.Quad, "BarFill", fillPivot.transform,
                new Vector3(width * 0.5f, 0f, -0.01f), new Vector3(width, height * 0.62f, 1f), Mat("M_NeonYellow"));

            comp.billboard = bar.transform;
            comp.fillPivot = fillPivot.transform;
            comp.fillRenderer = fill.GetComponent<Renderer>();
            comp.backgroundRenderer = bg.GetComponent<Renderer>();
            comp.headHeight = 2f;
            comp.heightAboveHead = 0.6f;
        }

        // ------------------------------------------------------------------ helpers
        // Copies of the PrefabFactory privates. Duplicated rather than made public on purpose: this file
        // must stay buildable while PrefabFactory is being edited independently.

        static Material Mat(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/" + name + ".mat");
            if (m == null) Debug.LogWarning("[MiniBossFactory] Material not found: " + MaterialDir + "/" + name + ".mat (run VibeGame1/2. Create Materials first)");
            return m;
        }

        static T Load<T>(string path) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static GameObject Empty(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go;
        }

        static void Save(GameObject temp, string path)
        {
            bool ok;
            PrefabUtility.SaveAsPrefabAsset(temp, path, out ok);
            if (!ok) Debug.LogError("[MiniBossFactory] Failed to save prefab " + path);
            else Debug.Log("[MiniBossFactory] Saved " + path);
            Object.DestroyImmediate(temp);
        }
    }
}
