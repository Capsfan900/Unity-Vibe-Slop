using UnityEngine;

namespace VibeGame1
{
    public enum ParryResult { None, Perfect, Blocked, Hit }

    public struct DamageInfo
    {
        public float damage;
        public float postureDamage;
        public Vector3 point;
        public Vector3 direction;
        public GameObject source;
        public bool isExecute;
    }

    public struct AttackInfo
    {
        public EnemyAttackData attack;
        public EnemyController attacker;
        public float damage;
        public bool unblockable;
    }
}
