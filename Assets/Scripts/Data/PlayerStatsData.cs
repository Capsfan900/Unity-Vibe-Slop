using UnityEngine;

namespace VibeGame1
{
    [CreateAssetMenu(menuName = "VibeGame1/Player Stats")]
    public class PlayerStatsData : ScriptableObject
    {
        [Header("Health")]
        public float baseHP = 100f;
        public float hpPerVitality = 10f;

        [Header("Parry")]
        public float parryPerfectWindow = 0.15f;
        public float parryLateWindow = 0.20f;
        public float parryWhiffRecovery = 0.25f;
        public float parrySuccessRecovery = 0.08f;
        public float blockDamageMultiplier = 0.3f;
        public float facingConeDeg = 75f;

        [Header("Juice / Ultimate")]
        public float maxJuice = 100f;
        public float baseJuicePerPerfect = 25f;
        public float juicePerArcane = 1f;
        public float ultSlowScale = 0.25f;
        public float ultSlowSeconds = 1.6f;
        public float ultRadius = 10f;
        public float ultBaseDamage = 80f;
        public float ultDamagePerArcane = 4f;
        public float ultBossPostureFraction = 0.5f;

        [Header("Flask")]
        public int flaskBaseCharges = 3;
        public float flaskBaseHeal = 45f;
        public float flaskHealPerLevel = 10f;
        public float flaskDrinkSeconds = 0.8f;

        [Header("Scaling")]
        public float damagePerStatPoint = 0.03f;
    }
}
