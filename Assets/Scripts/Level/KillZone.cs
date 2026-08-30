using UnityEngine;

namespace VibeGame1
{
    [RequireComponent(typeof(Collider))]
    public class KillZone : MonoBehaviour
    {
        void Awake() { GetComponent<Collider>().isTrigger = true; }

        void OnTriggerEnter(Collider other)
        {
            var pc = other.GetComponentInParent<PlayerCombat>();
            if (pc != null)
            {
                pc.Health.Invulnerable = false;
                pc.Health.TakeDamage(new DamageInfo { damage = 99999f, source = gameObject });
                return;
            }
            var e = other.GetComponentInParent<EnemyController>();
            if (e != null && e.IsAlive)
                e.Health.TakeDamage(new DamageInfo { damage = 99999f, isExecute = true, source = gameObject });
        }
    }
}
