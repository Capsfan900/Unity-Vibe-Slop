using System.IO;
using VibeGame1;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Builds every gameplay prefab from primitives + components so nothing has to be wired by hand.
    /// Idempotent: re-running overwrites the prefab assets in place.
    /// </summary>
    public static class PrefabFactory
    {
        const string PrefabDir = "Assets/Prefabs";
        const string WeaponDir = "Assets/Prefabs/Weapons";
        const string MaterialDir = "Assets/Materials";

        [MenuItem("VibeGame1/4. Build Prefabs")]
        public static void BuildAll()
        {
            EnsureFolder("Assets", "Prefabs");
            EnsureFolder(PrefabDir, "Weapons");

            // A. weapons first so WeaponData.viewmodelPrefab can be assigned
            BuildWeaponViewmodels();

            // E. bloodstain before Managers so LevelManager can reference it
            GameObject bloodstain = BuildBloodstain();
            BuildCheckpoint();
            BuildPlayer();
            BuildManagers(bloodstain);
            BuildEnemy("Enemy_Grunt", "Assets/Data/Enemies/Grunt.asset", false);
            BuildEnemy("Enemy_Heavy", "Assets/Data/Enemies/Heavy.asset", false);
            BuildEnemy("Boss", "Assets/Data/Enemies/Boss.asset", true);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PrefabFactory] All prefabs built.");
        }

        // ------------------------------------------------------------------ helpers

        static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }

        static Material Mat(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{name}.mat");
            if (m == null) Debug.LogWarning($"[PrefabFactory] Material not found: {MaterialDir}/{name}.mat (run DataFactory / material setup first)");
            return m;
        }

        static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) Debug.LogWarning($"[PrefabFactory] Asset not found: {path}");
            return a;
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material m, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            if (!keepCollider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
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

        static GameObject Save(GameObject temp, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path, out bool ok);
            if (!ok) Debug.LogError($"[PrefabFactory] Failed to save prefab {path}");
            else Debug.Log($"[PrefabFactory] Saved {path}");
            Object.DestroyImmediate(temp);
            return prefab;
        }

        // ------------------------------------------------------------------ A. weapons

        static void BuildWeaponViewmodels()
        {
            Material ground = Mat("M_Ground");

            // Sword
            {
                var root = new GameObject("VM_Sword");
                Prim(PrimitiveType.Cube, "Blade", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.06f, 0.9f, 0.12f), Mat("M_Weapon_Sword"));
                Prim(PrimitiveType.Cube, "Guard", root.transform, Vector3.zero, new Vector3(0.3f, 0.05f, 0.08f), ground);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.15f, 0f), new Vector3(0.05f, 0.25f, 0.05f), ground);
                var prefab = Save(root, $"{WeaponDir}/VM_Sword.prefab");
                AssignViewmodel("Assets/Data/Weapons/Sword.asset", prefab);
            }
            // Hammer
            {
                var root = new GameObject("VM_Hammer");
                Prim(PrimitiveType.Cube, "Head", root.transform, new Vector3(0f, 0.7f, 0f), new Vector3(0.35f, 0.25f, 0.25f), Mat("M_Weapon_Hammer"));
                Prim(PrimitiveType.Cube, "Handle", root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.06f, 0.8f, 0.06f), ground);
                var prefab = Save(root, $"{WeaponDir}/VM_Hammer.prefab");
                AssignViewmodel("Assets/Data/Weapons/Hammer.asset", prefab);
            }
            // Dagger
            {
                var root = new GameObject("VM_Dagger");
                Prim(PrimitiveType.Cube, "Blade", root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.05f, 0.5f, 0.08f), Mat("M_Weapon_Dagger"));
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.1f, 0f), new Vector3(0.04f, 0.18f, 0.04f), ground);
                var prefab = Save(root, $"{WeaponDir}/VM_Dagger.prefab");
                AssignViewmodel("Assets/Data/Weapons/Dagger.asset", prefab);
            }
            // Dev / test blade (slot 4)
            {
                Material blade = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/M_Weapon_Dev.mat");
                if (blade == null)
                {
                    Debug.LogWarning("[PrefabFactory] M_Weapon_Dev.mat not found, falling back to M_Weapon_Dagger for VM_DevBlade.");
                    blade = Mat("M_Weapon_Dagger");
                }
                var root = new GameObject("VM_DevBlade");
                Prim(PrimitiveType.Cube, "Blade", root.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.07f, 1.1f, 0.14f), blade);
                Prim(PrimitiveType.Cube, "Guard", root.transform, Vector3.zero, new Vector3(0.34f, 0.05f, 0.08f), ground);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.17f, 0f), new Vector3(0.05f, 0.28f, 0.05f), ground);
                var prefab = Save(root, $"{WeaponDir}/VM_DevBlade.prefab");
                AssignViewmodel("Assets/Data/Weapons/DevBlade.asset", prefab);
            }
        }

        static void AssignViewmodel(string weaponAssetPath, GameObject prefab)
        {
            var w = Load<WeaponData>(weaponAssetPath);
            if (w == null || prefab == null) return;
            w.viewmodelPrefab = prefab;
            EditorUtility.SetDirty(w);
        }

        // ------------------------------------------------------------------ B. player

        static void BuildPlayer()
        {
            var root = new GameObject("Player");
            root.layer = Layers.Player;

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 45f;
            cc.skinWidth = 0.05f;

            root.AddComponent<FirstPersonMotor>();
            var look = root.AddComponent<PlayerLook>();
            root.AddComponent<Health>();
            var stats = root.AddComponent<PlayerStats>();
            stats.data = Load<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            root.AddComponent<PlayerResources>();
            root.AddComponent<ParryController>();
            root.AddComponent<PlayerCombat>();
            var weapons = root.AddComponent<WeaponController>();
            weapons.loadout = new[]
            {
                Load<WeaponData>("Assets/Data/Weapons/Sword.asset"),
                Load<WeaponData>("Assets/Data/Weapons/Hammer.asset"),
                Load<WeaponData>("Assets/Data/Weapons/Dagger.asset"),
                Load<WeaponData>("Assets/Data/Weapons/DevBlade.asset"),
            };
            weapons.hitSparkMaterial = Mat("M_NeonYellow");
            root.AddComponent<ExecuteInteractor>();
            root.AddComponent<FlaskAbility>();
            var ult = root.AddComponent<UltimateAbility>();
            ult.ringMaterial = Mat("M_NeonCyan");
            root.AddComponent<PlayerDeath>();

            // Camera rig
            var pivot = Empty("CameraPivot", root.transform, new Vector3(0f, 1.6f, 0f));
            var shakeRoot = Empty("ShakeRoot", pivot.transform, Vector3.zero);
            shakeRoot.AddComponent<CameraShake>();

            var camGo = Empty("Camera", shakeRoot.transform, Vector3.zero);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 95f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("#06040A");
            camGo.AddComponent<AudioListener>();
            var urpCam = cam.GetUniversalAdditionalCameraData();
            urpCam.renderPostProcessing = true;
            urpCam.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            var fx = camGo.AddComponent<CameraFX>();
            fx.cam = cam;
            fx.baseFov = 95f;

            var vmRoot = Empty("ViewmodelRoot", camGo.transform, Vector3.zero);
            var vm = vmRoot.AddComponent<WeaponViewmodel>();
            var model = Empty("Model", vmRoot.transform, Vector3.zero);
            vm.model = model.transform;

            look.pivot = pivot.transform;
            look.cam = camGo.transform;

            Save(root, $"{PrefabDir}/Player.prefab");
        }

        // ------------------------------------------------------------------ C. managers

        static void BuildManagers(GameObject bloodstainPrefab)
        {
            var root = new GameObject("Managers");
            var gm = root.AddComponent<GameManager>();
            gm.statsData = Load<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            gm.feel = Load<GameFeelSettings>("Assets/Data/GameFeel.asset");
            root.AddComponent<InputReader>();
            root.AddComponent<TimeScaleController>();
            root.AddComponent<AudioManager>();
            root.AddComponent<SoulsWallet>();
            root.AddComponent<SpeedrunTimer>();
            var lm = root.AddComponent<LevelManager>();
            lm.bloodstainPrefab = bloodstainPrefab;
            lm.startSpawn = null; // level builder assigns
            root.AddComponent<DebugKeys>();

            Save(root, $"{PrefabDir}/Managers.prefab");
        }

        // ------------------------------------------------------------------ D. enemies

        static void BuildEnemy(string name, string dataPath, bool isBoss)
        {
            var root = new GameObject(name);
            root.layer = Layers.Enemy;

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

            EnemyController ctrl;
            if (isBoss)
            {
                var boss = root.AddComponent<BossController>();
                boss.data = Load<BossData>(dataPath);
                ctrl = boss;
            }
            else
            {
                ctrl = root.AddComponent<EnemyController>();
                ctrl.data = Load<EnemyData>(dataPath);
            }

            Material bodyMat = Mat(isBoss ? "M_Boss" : "M_Enemy");

            var visual = Empty("Visual", root.transform, Vector3.zero);
            var visuals = visual.AddComponent<EnemyVisuals>();
            var flash = visual.AddComponent<EmissiveFlash>();

            var lungeRoot = Empty("LungeRoot", visual.transform, Vector3.zero);
            var body = Prim(PrimitiveType.Capsule, "Body", lungeRoot.transform, new Vector3(0f, 1f, 0f), new Vector3(0.9f, 1f, 0.9f), bodyMat);
            var eye = Prim(PrimitiveType.Cube, "Eye", lungeRoot.transform, new Vector3(0f, 1.55f, 0.4f), new Vector3(0.3f, 0.12f, 0.15f), Mat("M_EnemyEye"));
            var weapon = Prim(PrimitiveType.Cube, "Weapon", lungeRoot.transform, new Vector3(0.55f, 1.0f, 0.35f), new Vector3(0.12f, 1.3f, 0.12f), bodyMat);
            weapon.transform.localRotation = Quaternion.Euler(20f, 0f, -15f);

            var alert = Prim(PrimitiveType.Cube, "Alert", visual.transform, new Vector3(0f, 2.5f, 0f), new Vector3(0.25f, 0.25f, 0.25f), Mat("M_NeonRed"));
            alert.SetActive(false);

            visuals.body = body.GetComponent<Renderer>();
            visuals.eye = eye.GetComponent<Renderer>();
            visuals.weapon = weapon.GetComponent<Renderer>();
            visuals.lungeRoot = lungeRoot.transform;
            visuals.alertMarker = alert;
            flash.renderers = new[] { visuals.body, visuals.weapon };

            Save(root, $"{PrefabDir}/{name}.prefab");
        }

        // ------------------------------------------------------------------ E. bloodstain

        static GameObject BuildBloodstain()
        {
            var root = new GameObject("Bloodstain");
            root.layer = Layers.Interactable;
            var sc = root.AddComponent<SphereCollider>();
            sc.radius = 1f;
            sc.isTrigger = true;
            var bs = root.AddComponent<Bloodstain>();

            var visual = Prim(PrimitiveType.Cube, "Visual", root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(0.35f, 0.35f, 0.35f), Mat("M_Bloodstain"));
            visual.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
            bs.visual = visual.transform;

            return Save(root, $"{PrefabDir}/Bloodstain.prefab");
        }

        // ------------------------------------------------------------------ F. checkpoint

        static void BuildCheckpoint()
        {
            var root = new GameObject("Checkpoint");
            root.layer = Layers.Interactable;
            var bc = root.AddComponent<BoxCollider>();
            bc.size = new Vector3(3f, 3f, 3f);
            bc.center = new Vector3(0f, 1.5f, 0f);
            bc.isTrigger = true;
            var cp = root.AddComponent<Checkpoint>();

            var spawn = Empty("Spawn", root.transform, new Vector3(0f, 0.2f, -2f));
            cp.spawnPoint = spawn.transform;

            var pillar = Prim(PrimitiveType.Cube, "Pillar", root.transform, new Vector3(0f, 1.5f, 0f), new Vector3(0.4f, 3f, 0.4f), Mat("M_Checkpoint"));
            var flash = pillar.AddComponent<EmissiveFlash>();
            flash.renderers = new[] { pillar.GetComponent<Renderer>() };

            // decorative ring of 4 thin cubes around the base
            Material ring = Mat("M_Checkpoint");
            float r = 1.2f;
            Prim(PrimitiveType.Cube, "Ring_N", root.transform, new Vector3(0f, 0.08f, r), new Vector3(2.4f, 0.12f, 0.12f), ring);
            Prim(PrimitiveType.Cube, "Ring_S", root.transform, new Vector3(0f, 0.08f, -r), new Vector3(2.4f, 0.12f, 0.12f), ring);
            Prim(PrimitiveType.Cube, "Ring_E", root.transform, new Vector3(r, 0.08f, 0f), new Vector3(0.12f, 0.12f, 2.4f), ring);
            Prim(PrimitiveType.Cube, "Ring_W", root.transform, new Vector3(-r, 0.08f, 0f), new Vector3(0.12f, 0.12f, 2.4f), ring);

            Save(root, $"{PrefabDir}/Checkpoint.prefab");
        }

        // ------------------------------------------------------------------ misc

        static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.black;
        }
    }
}
