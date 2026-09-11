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
        /// <summary>The real incoming bolt, when this attack arrived by projectile contact.</summary>
        public Projectile projectile;
        public float damage;
        public bool unblockable;
        /// <summary>
        /// World direction the attack is TRAVELLING when it reaches the player, or zero when the attack has
        /// no direction of its own (a swing: the facing test then uses the attacker's position). A bolt fills
        /// this so a runner who has passed the perch can still deflect the bolt they can see coming (combat
        /// plan 2026-09-06, P2). Facing is judged against the SOURCE of the attack, so the source direction
        /// is the negative of this.
        /// </summary>
        public Vector3 incomingDirection;
    }
}
