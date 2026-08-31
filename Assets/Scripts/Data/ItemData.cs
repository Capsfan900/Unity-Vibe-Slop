using UnityEngine;

namespace VibeGame1
{
    public enum ItemEffect
    {
        /// <summary>Launches the player straight up — a platforming shortcut.</summary>
        Updraft,
        /// <summary>Full heal, clears posture, refills the flask.</summary>
        SoulLantern,
        /// <summary>Brief invulnerability plus a speed boost.</summary>
        PhantomStep,
    }

    /// <summary>
    /// A Neon White style single-use pickup. Found in the level, carried in a small slot row,
    /// consumed on use, and restored when the level resets.
    /// </summary>
    [CreateAssetMenu(menuName = "VibeGame1/Item")]
    public class ItemData : ScriptableObject
    {
        public string displayName = "Item";
        [Tooltip("Short all-caps label for the HUD slot, e.g. STORM.")]
        public string shortLabel = "ITEM";
        [TextArea] public string description;

        public ItemEffect effect = ItemEffect.SoulLantern;
        [ColorUsage(true, true)] public Color color = Color.white;

        [Header("Offhand viewmodel")]
        [Tooltip("Shown in the player's offhand when this item is the queued spell. Assigned by PrefabFactory.")]
        public GameObject viewmodelPrefab;
        public float viewmodelScale = 0.5f;

        [Header("Tuning (meaning depends on effect)")]
        [Tooltip("Effect radius where applicable.")]
        public float radius = 12f;
        [Tooltip("Damage where applicable.")]
        public float damage = 250f;
        [Tooltip("Updraft: launch speed. PhantomStep: speed multiplier.")]
        public float power = 18f;
        [Tooltip("PhantomStep: seconds of invulnerability.")]
        public float duration = 2.5f;
    }
}
