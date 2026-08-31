using UnityEngine;

namespace VibeGame1
{
    /// <summary>Runtime Souls-style stat sheet. Derived values feed Health, PlayerResources and weapon damage.</summary>
    public class PlayerStats : MonoBehaviour
    {
        public PlayerStatsData data;
        public int Vitality, Strength, Dexterity, Arcane, FlaskLevel;

        Health health;
        PlayerResources resources;

        public float MaxHP => data.baseHP + Vitality * data.hpPerVitality;
        /// <summary>Pyre gained by a perfect parry, before the weapon's own pyreBonus.</summary>
        public float PyrePerPerfect => data.basePyrePerPerfect + Arcane * data.pyrePerArcane;
        public int FlaskCharges => data.flaskBaseCharges + FlaskLevel / 2;
        public float FlaskHeal => data.flaskBaseHeal + ((FlaskLevel + 1) / 2) * data.flaskHealPerLevel;
        public int TotalLevel => Vitality + Strength + Dexterity + Arcane + FlaskLevel;

        void Awake()
        {
            health = GetComponent<Health>();
            resources = GetComponent<PlayerResources>();
            if (data == null && GameManager.I != null) data = GameManager.I.statsData;
        }

        void Start()
        {
            if (data == null && GameManager.I != null) data = GameManager.I.statsData;
            Apply(true);
        }

        public float DamageMultiplier(WeaponData w)
        {
            if (w == null) return 1f;
            return 1f + data.damagePerStatPoint * (Strength * w.strScale + Dexterity * w.dexScale + Arcane * w.arcScale);
        }

        public int GetLevel(StatType t)
        {
            switch (t)
            {
                case StatType.Vitality: return Vitality;
                case StatType.Strength: return Strength;
                case StatType.Dexterity: return Dexterity;
                case StatType.Arcane: return Arcane;
                case StatType.Flask: return FlaskLevel;
            }
            return 0;
        }

        public void Increase(StatType t)
        {
            switch (t)
            {
                case StatType.Vitality: Vitality++; break;
                case StatType.Strength: Strength++; break;
                case StatType.Dexterity: Dexterity++; break;
                case StatType.Arcane: Arcane++; break;
                case StatType.Flask: FlaskLevel++; break;
            }
            Apply(false);
        }

        public void Apply(bool fullHeal)
        {
            if (health != null)
            {
                health.SetMax(MaxHP, false);
                if (fullHeal) health.ResetFull();
            }
            if (resources != null) resources.SetMaxFlask(FlaskCharges, fullHeal);
        }
    }
}
