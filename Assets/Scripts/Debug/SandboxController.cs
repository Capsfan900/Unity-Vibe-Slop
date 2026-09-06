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

        [Header("Respawn")]
        [Tooltip("Pad enemies come back a few seconds after they die, so practising a fight does not " +
                 "mean walking back to a menu. Campaign respawn is unaffected: there it is tied to the " +
                 "player dying, which is the whole point of a checkpoint.")]
        public bool autoRespawnPadEnemies = true;
        [Tooltip("Seconds between an enemy dying and its pad producing a fresh one. Long enough to watch " +
                 "the death and collect souls, short enough that you are not waiting to try again.")]
        [Range(0.5f, 15f)] public float respawnDelay = 4f;

        [Header("Movement yard")]
        [Tooltip("Where WarpToMovementYard() puts you: just inside the yard doorway, facing down the yard. " +
                 "Written by SandboxBuilder (the YardSpawn empty under MovementYard).")]
        public Transform yardSpawn;

        readonly List<GameObject> spawned = new List<GameObject>();

        // Pad respawn bookkeeping. Keyed by spawner so a pad only ever has one pending respawn, and so
        // clearing or resetting the sandbox can cancel them all without hunting coroutines.
        readonly Dictionary<EnemySpawner, float> respawnAt = new Dictionary<EnemySpawner, float>();

        // CACHED, because the respawn tick runs every frame and FindObjectsByType ALLOCATES A NEW ARRAY
        // on every call. Scanning for pads and switches each frame was ~2 array allocations per frame
        // for the whole session — steady garbage in the scene where movement is practised, which is
        // precisely what eventually produces a collection hitch. Pads are scene fixtures built by
        // SandboxBuilder and do not come and go, so a periodic refresh is enough to survive anything
        // spawned or destroyed at runtime.
        EnemySpawner[] padCache;
        SandboxEnemySwitch[] switchCache;
        float padCacheRefreshAt;
        const float PadCacheRefreshSeconds = 2f;

        // ---- lifecycle ---------------------------------------------------------------------------

        void OnEnable() { GameEvents.ItemUsed += OnItemUsed; }
        void OnDisable() { GameEvents.ItemUsed -= OnItemUsed; }

        void Update()
        {
            TickPadRespawns();

            if (!infiniteFlask) return;
            var res = OnPlayer<PlayerResources>();
            if (res != null && res.FlaskCharges < res.MaxFlask) res.RefillFlask();
        }

        /// <summary>
        /// Watches every pad spawner and rebuilds its occupant a few seconds after it dies.
        ///
        /// Deliberately polled rather than event-driven: an enemy can leave play as a corpse that is
        /// still present (State.Dead), as a destroyed object, or by being cleared out from under us by
        /// ClearAllEnemies, and a death event only covers the first. Asking "is this pad empty or is its
        /// occupant dead?" each frame covers all three and cannot leak a subscription.
        ///
        /// Uses unscaled time: the sandbox is where hitstop and the super's slow-mo get exercised, and a
        /// respawn clock that stretches under them would feel broken for no reason.
        /// </summary>
        void TickPadRespawns()
        {
            if (!autoRespawnPadEnemies) { respawnAt.Clear(); return; }

            RefreshPadCache(false);
            var pads = padCache;
            for (int i = 0; i < pads.Length; i++)
            {
                var pad = pads[i];
                if (pad == null || pad.prefab == null) continue;

                bool empty = pad.Instance == null;
                if (!empty)
                {
                    var ec = pad.Instance.GetComponent<EnemyController>();
                    // A boss "dies" into a stagger and is only finished by a deathblow, so IsAlive is the
                    // wrong question for it — Health.IsDead is the one that means gone for good.
                    var hp = pad.Instance.GetComponent<Health>();
                    bool dead = (ec != null && !ec.IsAlive) || (hp != null && hp.IsDead);
                    if (!dead) { respawnAt.Remove(pad); continue; }
                }

                float due;
                if (!respawnAt.TryGetValue(pad, out due))
                {
                    respawnAt[pad] = Time.unscaledTime + respawnDelay;
                    continue;
                }
                if (Time.unscaledTime < due) continue;

                respawnAt.Remove(pad);
                pad.Spawn();               // Spawn() despawns the corpse first, so this cannot stack.

                // Put the replacement back to SLEEP. The prefab ships aggroLocked = false, and the pad's
                // switch tracks woken enemies by INSTANCE, so a respawn is a stranger to it — without
                // this the new enemy walks off its pad at you the moment it appears, which is exactly
                // the state the wake switches exist to prevent. Same reasoning as ResetSandbox.
                var sw = SwitchFor(pad);
                if (sw != null) sw.Rearm();
            }
        }

        /// <summary>
        /// Re-scan the scene for pads and wake switches, at most every <see cref="PadCacheRefreshSeconds"/>
        /// unless <paramref name="force"/>. Unscaled time so a paused or slow-mo'd sandbox still refreshes.
        /// </summary>
        void RefreshPadCache(bool force)
        {
            if (!force && padCache != null && Time.unscaledTime < padCacheRefreshAt) return;
            padCache = FindObjectsByType<EnemySpawner>();
            switchCache = FindObjectsByType<SandboxEnemySwitch>();
            padCacheRefreshAt = Time.unscaledTime + PadCacheRefreshSeconds;
        }

        /// <summary>The wake switch belonging to a pad, or null for pads that have none.</summary>
        SandboxEnemySwitch SwitchFor(EnemySpawner pad)
        {
            if (switchCache == null) RefreshPadCache(true);
            for (int i = 0; i < switchCache.Length; i++)
                if (switchCache[i] != null && switchCache[i].spawner == pad) return switchCache[i];
            return null;
        }

        /// <summary>Cancel every pending pad respawn — used when the sandbox is cleared or reset.</summary>
        /// <summary>
        /// Cancel every pending pad respawn, and force the next tick to re-scan. Clearing or resetting
        /// the sandbox destroys and re-creates enemies, so the cached arrays hold dead references at
        /// exactly that moment — the one case the periodic refresh is too slow for.
        /// </summary>
        void CancelPadRespawns() { respawnAt.Clear(); padCacheRefreshAt = 0f; }

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

            // "Clear" has to MEAN clear. With pad respawn armed, emptying the pads is the exact trigger
            // that refills them, so the arena would repopulate a few seconds later and the command would
            // look broken. Turn respawn off and say so; ResetSandbox turns it back on.
            CancelPadRespawns();
            if (autoRespawnPadEnemies)
            {
                autoRespawnPadEnemies = false;
                Debug.Log("[SandboxController] Cleared all enemies and disabled pad auto-respawn "
                        + "(otherwise the pads refill in " + respawnDelay + "s). ResetSandbox() re-enables it.");
            }
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

            // A reset is "put the sandbox back how it started", which includes pad respawn being armed —
            // ClearAllEnemies turns it off, and leaving it off would make reset a one-way door.
            CancelPadRespawns();
            autoRespawnPadEnemies = true;

            if (LevelManager.I != null) LevelManager.I.ResetEnemies();

            // And put every pad back to SLEEP. ResetEnemies re-instantiates from the prefab, whose
            // aggroLocked is false, so without this a reset would leave the whole row awake and walking
            // at you — the exact state the wake switches exist to prevent.
            foreach (var sw in FindObjectsByType<SandboxEnemySwitch>()) sw.Rearm();

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

        /// <summary>
        /// Teleport to the movement yard (the 120 x 60 m annex east of the arena) without walking the
        /// 36 m from spawn. Same Teleport + SetYaw pattern as <see cref="ResetSandbox"/>; nothing else is
        /// touched — health, enemies and the respawn clock are exactly as you left them.
        /// </summary>
        [ContextMenu("Warp To Movement Yard")]
        public void WarpToMovementYard()
        {
            if (yardSpawn == null)
            {
                Debug.LogWarning("[Sandbox] No yardSpawn assigned — rebuild the sandbox (VibeGame1/7. Build Sandbox Scene).");
                return;
            }

            var motor = OnPlayer<FirstPersonMotor>();
            if (motor == null) { Debug.LogWarning("[Sandbox] No player in the scene."); return; }

            float yaw = yardSpawn.eulerAngles.y;
            motor.Teleport(yardSpawn.position, yaw);
            var look = OnPlayer<PlayerLook>();
            if (look != null) look.SetYaw(yaw);
        }
#endif
    }
}
