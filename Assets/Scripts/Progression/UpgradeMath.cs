using UnityEngine;

namespace VibeGame1
{
    public enum StatType { Vitality, Strength, Dexterity, Arcane, Flask }

    public static class UpgradeMath
    {
        public static int Cost(int currentLevel, int baseCost, float growth)
        {
            return Mathf.RoundToInt(baseCost * Mathf.Pow(growth, Mathf.Max(0, currentLevel)));
        }
    }
}
