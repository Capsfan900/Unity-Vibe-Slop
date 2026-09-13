using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Single-use traversal/combat-tech tools. Explicit numeric values protect old serialized
    /// WallSurge assets from silently becoming a different item while the factory migrates them out.
    /// </summary>
    public enum ItemEffect
    {
        /// <summary>Hook an enemy in view and get pulled to it; arrive with a deathblow. The
        /// Sekiro grapple-kill: a kill IS the move.</summary>
        Grapple = 0,
        /// <summary>Retired. Kept only as a serialization tombstone; factories no longer ship it.</summary>
        WallSurge = 1,
        /// <summary>Arms the next legal airborne dash or wall jump for a stronger, refreshed exit.</summary>
        Rebound = 2,
        /// <summary>Waits for the next Perfect, then adds speed stacks and a forward impulse.</summary>
        DeflectSigil = 3,
        /// <summary>Throw the equipped sword; DASH at it (in flight or lodged) to be pulled to it.
        /// Unarmed until it returns.</summary>
        BladeThrow = 4,
    }

    /// <summary>
    /// A Neon White style single-use spell. Found in the level, selected in the persistent book,
    /// consumed on use, and restored when the level resets.
    /// </summary>
    [CreateAssetMenu(menuName = "VibeGame1/Item")]
    public class ItemData : ScriptableObject
    {
        public string displayName = "Item";
        [Tooltip("Short all-caps label for compact spell readouts, e.g. HOOK.")]
        public string shortLabel = "ITEM";
        [TextArea] public string description;

        public ItemEffect effect = ItemEffect.Grapple;
        [ColorUsage(true, true)] public Color color = Color.white;

        [Header("Legacy item model")]
        [Tooltip("Dormant compatibility data for older builds that swapped items into the offhand. The persistent offhand now always displays the equipped wand.")]
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

        [Tooltip("Seconds before real contact that using HOOK may count as the projectile's Perfect timing. " +
                 "The pull itself never manufactures a hit or a deflect.")]
        public float grapplePerfectWindow = 0.13f;

        [Header("Rebound")]
        [Tooltip("Horizontal exit-strength multiplier on the next legal airborne dash or wall jump.")]
        public float reboundExitMultiplier = 1.18f;
        [Tooltip("Extra forward metres/second composed into that successful exit, under the motor's caps.")]
        public float reboundBonusSpeed = 3f;

        [Header("Deflect sigil")]
        [Tooltip("Additional speed-surge stacks paid by the next Perfect after the sigil is armed.")]
        [Min(1)] public int deflectSigilBonusStacks = 2;
        [Tooltip("Extra forward metres/second paid with the qualifying Perfect.")]
        public float deflectSigilImpulse = 5f;

        [Header("Blade throw")]
        [Tooltip("Metres per second the sword leaves the hand at, along the aim.")]
        public float bladeSpeed = 24f;
        [Tooltip("Metres per second squared of drop on the throw's arc. Low: a tomahawk, not a lob.")]
        public float bladeGravity = 8f;
        [Tooltip("Seconds the sword flies before it returns on its own. Speed x this is the throw's reach cap.")]
        public float bladeFlightSeconds = 1.1f;
        [Tooltip("Seconds a lodged sword stays recallable before it returns on its own.")]
        public float bladeLodgeSeconds = 3.5f;
        [Tooltip("Degrees per second of end-over-end tumble in flight.")]
        public float bladeSpinDegreesPerSecond = 1080f;
        [Tooltip("Metres. Furthest the sword can be and still be recalled to.")]
        public float bladeRecallRange = 30f;
        [Tooltip("Degrees off the aim inside which the sword is a recall target. Generous: it is moving.")]
        public float bladeRecallConeDeg = 25f;
        [Tooltip("Seconds of pull to the sword. The hook's 0.35.")]
        public float bladePullSeconds = 0.35f;
        [Tooltip("An enemy the sword lodges in takes the equipped weapon's posture damage times this. No health damage.")]
        public float bladePostureMultiplier = 1.5f;
        [Tooltip("World size of the thrown model relative to the weapon's viewmodel scale.")]
        public float bladeModelScale = 1.6f;

        [Header("Retired serialized data")]
        [Tooltip("Legacy Wall Surge duration. Kept only so old serialized assets remain readable; no shipped item uses it.")]
        public float surgeSeconds = 8f;
    }
}
