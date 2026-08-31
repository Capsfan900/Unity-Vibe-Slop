using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace VibeGame1
{
    /// <summary>
    /// Sandbox-scene-only conveniences. Lives on the "Sandbox" root of Assets/Scenes/Sandbox.unity.
    ///
    /// This deliberately does NOT duplicate <see cref="DebugKeys"/> (F5-F8 hotkeys) or
    /// <see cref="TestMenu"/> (F1 overlay: warp / give item / equip weapon / restore / kill nearby).
    /// What lives here is what only makes sense in a flat arena: spawning an enemy in front of you on
    /// demand, an inert practice dummy, waking the boss without an arena trigger, and the two
    /// "never run out" toggles.
    ///
    /// Every entry point is null-guarded and safe to call when nothing is set up.
    /// </summary>
    public class SandboxController : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Header("Spawning")]
        [Tooltip("Parallel to enemyPrefabs; used for labels and for the dummy's stat overrides.")]
        public EnemyData[] spawnableEnemies;
        [Tooltip("Enemy prefabs spawnable via SpawnEnemyInFront(index).")]
        public GameObject[] enemyPrefabs;
        [Tooltip("Metres in front of the player to drop a spawned enemy.")]
        public float spawnDistance = 8f;
        [Tooltip("Max metres the spawn point may be nudged to land on the NavMesh.")]
        public float navSampleRadius = 6f;

        [Header("Practice dummy")]
        [Tooltip("Index into enemyPrefabs used by SpawnDummy(). Normally the Grunt.")]
        public int dummyPrefabIndex;
        public float dummyHealth = 999999f;

        [Header("Cheats")]
        [Tooltip("A used item is immediately handed back.")]
        public bool infiniteItems;
        [Tooltip("The flask silently refills whenever it is not full.")]
        public bool infiniteFlask;

        readonly List<GameObject> spawned = new List<GameObject>();

        // ---- lifecycle ---------------------------------------------------------------------------

        void OnEnable() { GameEvents.ItemUsed += OnItemUsed; }
        void OnDisable() { GameEvents.ItemUsed -= OnItemUsed; }

        void Update()
        {
            if (!infiniteFlask) return;
            var res = OnPlayer<PlayerResources>();
            if (res != null && res.FlaskCharges < res.MaxFlask) res.RefillFlask();
        }

        void OnItemUsed(ItemData item)
        {
            if (!infiniteItems || item == null) return;
            var inventory = OnPlayer<PlayerItems>();
            if (inventory != null && !inventory.IsFull) inventory.TryPickup(item);
        }

        // ---- lookups -----------------------------------------------------------------------------

        static PlayerCombat Player() => FindAnyObjectByType<PlayerCombat>();

        static T OnPlayer<T>() where T : Component
        {
            var p = Player();
            return p != null ? p.GetComponent<T>() : null;
        }

        /// <summary>Flat forward from the camera when available, otherwise the body's facing.</summary>
        static Vector3 PlayerForward(PlayerCombat p)
        {
            var look = p != null ? p.GetComponent<PlayerLook>() : null;
            Vector3 f = look != null ? look.AimForward : (p != null ? p.transform.forward : Vector3.forward);
            f.y = 0f;
            return f.sqrMagnitude < 0.0001f ? Vector3.forward : f.normalized;
        }

        // ---- spawning ----------------------------------------------------------------------------

        /// <summary>Drop enemyPrefabs[index] onto the NavMesh in front of the player, facing them.</summary>
        [ContextMenu("Spawn Enemy 0 In Front")]
        public void SpawnEnemyInFront() => SpawnEnemyInFront(0);

        public GameObject SpawnEnemyInFront(int index)
        {
            if (enemyPrefabs == null || index < 0 || index >= enemyPrefabs.Length)
            {
                Debug.LogWarning($"[Sandbox] No enemy prefab at index {index}.");
                return null;
            }

            var prefab = enemyPrefabs[index];
            if (prefab == null)
            {
                Debug.LogWarning($"[Sandbox] Enemy prefab slot {index} is empty.");
                return null;
            }

            var player = Player();
            if (player == null) { Debug.LogWarning("[Sandbox] No player in the scene."); return null; }

            Vector3 forward = PlayerForward(player);
            Vector3 target = player.transform.position + forward * Mathf.Max(1f, spawnDistance);

            // Enemies drive a NavMeshAgent; spawning off-mesh leaves them frozen.
            if (NavMesh.SamplePosition(target, out var hit, Mathf.Max(1f, navSampleRadius), NavMesh.AllAreas))
                target = hit.position;
            else
                Debug.LogWarning("[Sandbox] No NavMesh near the spawn point; the enemy may not move.");

            Quaternion facing = Quaternion.LookRotation(-forward, Vector3.up);
            var go = Instantiate(prefab, target, facing);
            go.name = prefab.name + " (sandbox)";
            spawned.Add(go);
            return go;
        }

        /// <summary>
        /// An inert, effectively unkillable Grunt: aggro locked so it never swings, with a huge health
        /// pool. For practising swing timing, hit reactions and posture damage against a still target.
        /// </summary>
        [ContextMenu("Spawn Practice Dummy")]
        public void SpawnDummy()
        {
            var go = SpawnEnemyInFront(dummyPrefabIndex);
            if (go == null) return;
            go.name = "Practice Dummy (sandbox)";
            StartCoroutine(MakeDummyCo(go));
        }

        /// <summary>EnemyController.Init() runs in Start and would overwrite these, so wait a frame.</summary>
        IEnumerator MakeDummyCo(GameObject go)
        {
            yield return null;
            if (go == null) yield break;

            var enemy = go.GetComponent<EnemyController>();
            if (enemy != null) enemy.aggroLocked = true;

            var health = go.GetComponent<Health>();
            if (health != null)
            {
                health.SetMax(Mathf.Max(1f, dummyHealth), false);
                health.ResetFull();
            }
        }

        /// <summary>Wakes the boss without an arena trigger (the sandbox has none).</summary>
        [ContextMenu("Activate Boss")]
        public void ActivateBoss()
        {
            var boss = FindAnyObjectByType<BossController>();
            if (boss == null) { Debug.LogWarning("[Sandbox] No boss in the scene."); return; }
            boss.Activate();
        }

        // ---- clearing ----------------------------------------------------------------------------

        /// <summary>
        /// Instantly despawns every enemy, including spawner-owned ones. This is a despawn, not a
        /// kill: no death animation, no souls. TestMenu's "Kill Nearby" is the one that kills properly.
        /// </summary>
        [ContextMenu("Clear All Enemies")]
        public void ClearAllEnemies()
        {
            foreach (var spawner in FindObjectsByType<EnemySpawner>())
                if (spawner != null) spawner.Despawn();

            foreach (var enemy in FindObjectsByType<EnemyController>())
                if (enemy != null) Destroy(enemy.gameObject);

            spawned.Clear();
        }

        [ContextMenu("Toggle Infinite Flask")]
        public void ToggleInfiniteFlask()
        {
            infiniteFlask = !infiniteFlask;
            Debug.Log($"[Sandbox] Infinite flask {(infiniteFlask ? "ON" : "OFF")}");
        }

        [ContextMenu("Toggle Infinite Items")]
        public void ToggleInfiniteItems()
        {
            infiniteItems = !infiniteItems;
            Debug.Log($"[Sandbox] Infinite items {(infiniteItems ? "ON" : "OFF")}");
        }

        /// <summary>Enemies back to their pads, player restored and returned to spawn.</summary>
        [ContextMenu("Reset Sandbox")]
        public void ResetSandbox()
        {
            // Drop anything spawned by hand first, so ResetEnemies only restores the pads.
            foreach (var go in spawned) if (go != null) Destroy(go);
            spawned.Clear();

            if (LevelManager.I != null) LevelManager.I.ResetEnemies();

            // And put every pad back to SLEEP. ResetEnemies re-instantiates from the prefab, whose
            // aggroLocked is false, so without this a reset would leave the whole row awake and walking
            // at you — the exact state the wake switches exist to prevent.
            foreach (var sw in FindObjectsByType<SandboxEnemySwitch>(FindObjectsSortMode.None)) sw.Rearm();

            var player = Player();
            if (player != null)
            {
                if (player.Health != null) player.Health.ResetFull();

                var posture = player.GetComponent<PlayerPosture>();
                if (posture != null) posture.ResetFull();

                var res = player.GetComponent<PlayerResources>();
                if (res != null) { res.RefillFlask(); res.AddPyre(1000f); }

                var spawn = LevelManager.I != null ? LevelManager.I.startSpawn : null;
                var motor = player.GetComponent<FirstPersonMotor>();
                var look = player.GetComponent<PlayerLook>();
                if (spawn != null && motor != null)
                {
                    motor.Teleport(spawn.position, spawn.eulerAngles.y);
                    if (look != null) look.SetYaw(spawn.eulerAngles.y);
                }
            }

            Debug.Log("[Sandbox] Reset.");
        }
#endif
    }
}
