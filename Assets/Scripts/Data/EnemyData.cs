using UnityEngine;

namespace VibeGame1
{
    [CreateAssetMenu(menuName = "VibeGame1/Enemy")]
    public class EnemyData : ScriptableObject
    {
        public string displayName = "Grunt";

        [Header("Vitals")]
        public float maxHP = 60f;
        public float maxPosture = 60f;
        public float postureRegen = 10f;
        public float postureRegenDelay = 1.5f;
        public float staggerSeconds = 3f;

        [Header("Movement")]
        public float moveSpeed = 4.5f;
        public float turnSpeed = 360f;
        public float aggroRange = 14f;
        public float attackRange = 2.2f;
        public float attackCooldown = 0.4f;
        public float parryRecoilSeconds = 0.5f;

        [Header("Reward")]
        public int soulValue = 40;

        [Header("Look")]
        public Color bodyColor = new Color(0.16f, 0.04f, 0.04f);
        [ColorUsage(true, true)] public Color emission = new Color(1f, 0.16f, 0.16f) * 1.8f;
        public float scale = 1f;

        [Header("Attacks")]
        public AttackCombo[] combos;
    }
}
