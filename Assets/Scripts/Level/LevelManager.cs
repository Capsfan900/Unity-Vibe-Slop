using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Owns spawners, checkpoints and the respawn flow.</summary>
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager I { get; private set; }

        public Transform startSpawn;
        public GameObject bloodstainPrefab;

        public Checkpoint Current { get; private set; }
        public int DeathCount { get; private set; }

        readonly List<EnemySpawner> spawners = new List<EnemySpawner>();
        GameObject bloodstain;

        FirstPersonMotor motor;
        PlayerLook look;
        Health playerHealth;
        PlayerResources resources;

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
        }

        void Start()
        {
            spawners.AddRange(FindObjectsByType<EnemySpawner>());
            var pc = FindAnyObjectByType<PlayerCombat>();
            if (pc != null)
            {
                motor = pc.GetComponent<FirstPersonMotor>();
                look = pc.GetComponent<PlayerLook>();
                playerHealth = pc.GetComponent<Health>();
                resources = pc.GetComponent<PlayerResources>();
            }
            SpawnAll();
        }

        public void SpawnAll()
        {
            foreach (var s in spawners) if (s != null) s.Spawn();
        }

        public void ResetEnemies()
        {
            foreach (var s in spawners) if (s != null) s.Spawn();
            foreach (var t in FindObjectsByType<BossArenaTrigger>()) t.ResetArena();
        }

        public void SetCheckpoint(Checkpoint c)
        {
            if (c == null || Current == c) return;
            Current = c;
            c.Activate();
            if (playerHealth != null) playerHealth.ResetFull();
            if (resources != null) resources.RefillFlask();
            GameEvents.RaiseCheckpointReached(c);
            AudioManager.Play(Sfx.Checkpoint);
        }

        public void Respawn()
        {
            DeathCount++;
            ResetEnemies();
            Transform spawn = Current != null && Current.spawnPoint != null ? Current.spawnPoint : startSpawn;
            Vector3 pos = spawn != null ? spawn.position : Vector3.zero;
            float yaw = spawn != null ? spawn.eulerAngles.y : 0f;
            if (motor != null) motor.Teleport(pos, yaw);
            if (look != null) look.SetYaw(yaw);
            if (playerHealth != null) playerHealth.ResetFull();
            if (resources != null) resources.RefillFlask();
            GameManager.I.SetState(GameState.Playing);
            GameEvents.RaisePlayerRespawned();
        }

        public void SpawnBloodstain(Vector3 pos, int souls)
        {
            if (bloodstain != null) Destroy(bloodstain);
            if (bloodstainPrefab == null) return;
            bloodstain = Instantiate(bloodstainPrefab, pos, Quaternion.identity);
            var b = bloodstain.GetComponent<Bloodstain>();
            if (b != null) b.amount = souls;
        }

        /// <summary>Debug: teleport to a checkpoint by GameObject name (e.g. "Checkpoint_2").</summary>
        public void Warp(string checkpointName)
        {
            foreach (var c in FindObjectsByType<Checkpoint>())
            {
                if (c.name != checkpointName) continue;
                SetCheckpoint(c);
                if (motor != null) motor.Teleport(c.spawnPoint.position, c.spawnPoint.eulerAngles.y);
                if (look != null) look.SetYaw(c.spawnPoint.eulerAngles.y);
                return;
            }
            Debug.LogWarning($"[LevelManager] No checkpoint named {checkpointName}");
        }
    }
}
