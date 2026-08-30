using System;
using UnityEngine;

namespace VibeGame1
{
    [Serializable]
    public class BossPhase
    {
        public AttackCombo[] patterns;
        public float windupMultiplier = 1f;
        public float speedMultiplier = 1f;
        [ColorUsage(true, true)] public Color accent = Color.red;
    }

    [CreateAssetMenu(menuName = "VibeGame1/Boss")]
    public class BossData : EnemyData
    {
        [Header("Boss")]
        public int segments = 3;
        public BossPhase[] phases;
    }
}
