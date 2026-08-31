using UnityEngine;

namespace VibeGame1
{
    /// <summary>The shape of a wand's discharge. Drives which enemies the riposte blast reaches.</summary>
    public enum WandKind
    {
        /// <summary>Single target, high damage, fast. The reliable default.</summary>
        Bolt,
        /// <summary>Full damage to the ripostee plus splash to everything inside blastRadius.</summary>
        Scatter,
        /// <summary>Arcs from the ripostee to up to chainTargets further enemies for reduced damage.</summary>
        Chain,
        /// <summary>Pierces along a line from the player through the ripostee, hitting everything on it.</summary>
        Lance,
    }

    /// <summary>
    /// A magic wand, fired as part of the riposte (the critical attack after a posture break).
    /// Bloodborne firearms are the reference: several of them, swapped as loadout rather than fired
    /// freely, each with a distinct cadence and blast shape.
    /// </summary>
    [CreateAssetMenu(menuName = "VibeGame1/Wand")]
    public class WandData : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Wand";
        [Tooltip("Short all-caps label for HUD use.")]
        public string shortLabel = "WAND";
        [TextArea] public string description;
        [ColorUsage(true, true)] public Color color = Color.white;

        [Header("Viewmodel")]
        public GameObject viewmodelPrefab;
        [Tooltip("Offhand size. 0.5 was a splinter at 95° FOV — each wand now sets its own in " +
                 "WandFactory so a long thin one and a short fat one both read.")]
        public float viewmodelScale = 0.7f;

        [Header("Blast")]
        public WandKind kind = WandKind.Bolt;
        [Tooltip("Added on top of the equipped weapon's executeDamage, on the riposte target only.")]
        public float damage = 200f;
        [Tooltip("Scatter/Chain: search radius. Lance: pierce LENGTH along the firing line. Bolt: unused.")]
        public float blastRadius = 0f;
        [Tooltip("Damage dealt to enemies other than the riposte target. Never counts as a deathblow.")]
        public float splashDamage = 0f;
        [Tooltip("Chain only: how many further enemies the arc jumps to.")]
        public int chainTargets = 3;

        [Header("Cadence — together these set the riposte's rhythm")]
        [Tooltip("Seconds spent raising and charging the wand before the discharge.")]
        public float windup = 0.22f;
        [Tooltip("Seconds spent settling after the discharge, before control returns.")]
        public float recover = 0.24f;
        [Tooltip("Seconds before this wand can be fired again. The deathblow itself is NEVER gated — " +
                 "a riposte taken while the wand is cooling falls back to the melee execute, so a boss " +
                 "deathblow window can never be locked out by a cooldown. Heavier wands wait longer.")]
        public float cooldown = 4f;

        [Header("Feel")]
        [Tooltip("Metres of shove applied to enemies caught in the blast.")]
        public float knockback = 3f;
        public float hitStop = 0.1f;
        public float shake = 0.3f;
    }
}
