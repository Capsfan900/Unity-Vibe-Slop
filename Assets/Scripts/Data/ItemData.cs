using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The two item kinds. Both are TRAVERSAL tools, Neon White style: the level is built around
    /// them, and each one is a way to move that the base kit does not have.
    /// </summary>
    public enum ItemEffect
    {
        /// <summary>Hook an enemy in view and get pulled to it; arrive with a deathblow. The
        /// Sekiro grapple-kill: a kill IS the move.</summary>
        Grapple,
        /// <summary>For a few seconds every wall run is free, faster, and attaches from any speed.</summary>
        WallSurge,
    }

    /// <summary>
    /// A Neon White style single-use pickup. Found in the level, carried in a small slot row,
    /// consumed on use, and restored when the level resets.
    /// </summary>
    [CreateAssetMenu(menuName = "VibeGame1/Item")]
    public class ItemData : ScriptableObject
    {
        public string displayName = "Item";
        [Tooltip("Short all-caps label for the HUD slot, e.g. HOOK.")]
        public string shortLabel = "ITEM";
        [TextArea] public string description;

        public ItemEffect effect = ItemEffect.Grapple;
        [ColorUsage(true, true)] public Color color = Color.white;

        [Header("Offhand viewmodel")]
        [Tooltip("Shown in the player's offhand when this item is the queued spell. Assigned by PrefabFactory.")]
        public GameObject viewmodelPrefab;
        public float viewmodelScale = 0.5f;

        [Header("Grapple")]
        [Tooltip("Metres. Furthest enemy the hook will take. Refused (item kept) beyond it.")]
        public float grappleRange = 28f;
        [Tooltip("Degrees off the crosshair an enemy may be and still be hooked. Narrow on purpose: the " +
                 "hook is aimed, and the lock-on target always wins when there is one.")]
        public float grappleConeDeg = 12f;
        [Tooltip("Seconds the pull takes to reach the stand-off point, whatever the distance.")]
        public float grappleSeconds = 0.35f;
        [Tooltip("A Legendary_* or boss that is NOT staggered is not killed on arrival — it takes this " +
                 "fraction of its max posture instead, and the player lands at stand-off.")]
        public float grappleBigPostureFraction = 0.35f;

        [Header("Wall surge")]
        [Tooltip("Seconds of free, faster, attach-from-anything wall running.")]
        public float surgeSeconds = 8f;
    }
}
