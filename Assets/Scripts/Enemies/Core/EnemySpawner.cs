using UnityEngine;

namespace VibeGame1
{
    /// <summary>Scene marker that instantiates an enemy prefab. LevelManager respawns through these on player death.</summary>
    public class EnemySpawner : MonoBehaviour
    {
        public GameObject prefab;
        public bool isBoss;
        public GameObject Instance { get; private set; }

        public void Spawn()
        {
            Despawn();
            if (prefab == null) return;
            Instance = Instantiate(prefab, transform.position, transform.rotation);
            Instance.name = prefab.name;
        }

        public void Despawn()
        {
            if (Instance != null) Destroy(Instance);
            Instance = null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = isBoss ? Color.magenta : Color.red;
            Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.5f);
            Gizmos.DrawLine(transform.position + Vector3.up, transform.position + Vector3.up + transform.forward);
        }
    }
}
