using System;
using UnityEngine;

namespace VibeGame1
{
    [CreateAssetMenu(menuName = "VibeGame1/Enemy Attack")]
    public class EnemyAttackData : ScriptableObject
    {
        public string attackName = "Slash";
        [Tooltip("Telegraph length in seconds. The player parries at the END of the windup.")]
        public float windup = 0.55f;
        [Tooltip("Seconds after the windup ends before the hit is applied.")]
        public float impactDelay = 0.05f;
        public float strikeDuration = 0.2f;
        public float recovery = 0.7f;
        public float range = 2.4f;
        public float coneDeg = 70f;
        public float damage = 20f;
        public float lungeDistance = 0.5f;
        [Tooltip("Gap before the next hit when this attack is part of a combo.")]
        public float comboGap = 0.2f;
        [Tooltip("Multiplier on the posture damage the enemy takes when this attack is perfectly parried.")]
        public float parryPostureMultiplier = 1f;
        public bool unblockable;
    }

    [Serializable]
    public class AttackCombo
    {
        public EnemyAttackData[] hits;
        public AttackCombo() { }
        public AttackCombo(params EnemyAttackData[] h) { hits = h; }
    }
}
