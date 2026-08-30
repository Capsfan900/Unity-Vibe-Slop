using UnityEngine;

namespace VibeGame1
{
    [CreateAssetMenu(menuName = "VibeGame1/Upgrade Table")]
    public class UpgradeTable : ScriptableObject
    {
        public int baseCost = 100;
        public float growth = 1.22f;
        public int maxLevel = 20;

        public int Cost(int currentLevel) => UpgradeMath.Cost(currentLevel, baseCost, growth);
    }
}
