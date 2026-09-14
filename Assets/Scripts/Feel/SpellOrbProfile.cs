using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The signature silhouette a spell wears in the persistent spellbook. Identity is carried by SHAPE
    /// and MOTION first and hue second: two spells may share a colour family (Rebound and Gravecall are
    /// both green; the Sigil and Voidspine are both violet) and still read at a glance because a spinning
    /// fan ring is not a sinking wisp and a pulsing gem is not a rim of spines falling inward.
    ///
    /// <para>Explicit numeric values: these are serialized on <see cref="ItemData"/> and
    /// <see cref="WandData"/> assets, so a reorder would silently reshape every orb.</para>
    /// </summary>
    public enum SpellOrbShape
    {
        /// <summary>No signature rig. The orb is a bare core and shell (the empty-book read).</summary>
        None = 0,
        /// <summary>Hook: a barbed crescent orbiting the core.</summary>
        Crescent = 1,
        /// <summary>Rebound: two counter-spinning fan rings.</summary>
        FanRings = 2,
        /// <summary>Deflect Sigil: a faceted gem over a seal plate, beating on a slow pulse.</summary>
        DiamondSeal = 3,
        /// <summary>Blade Throw: a small sword tumbling end over end.</summary>
        SwordGlyph = 4,
        /// <summary>Emberlance: a heat-wobbled shell with embers rising through it.</summary>
        Molten = 5,
        /// <summary>Gravecall: two dark sockets on the core and wisps sinking out of it.</summary>
        SkullMist = 6,
        /// <summary>Stormneedle: needles that re-strike at a crackle cadence.</summary>
        NeedleArcs = 7,
        /// <summary>Voidspine: a darkened centre with spines falling inward.</summary>
        VoidRim = 8,
    }

    /// <summary>
    /// Per-spell presentation profile for the book's orb. Presentation only: nothing here is read by
    /// combat, the motor or the items. Every shipped number is written by <c>DataFactory</c> (items) or
    /// <c>WandFactory</c> (inscriptions) and asserted by <c>SpellbookVisualTests</c> — a field initialiser
    /// here is a code default, never a shipped value (AGENTS.md rule 9).
    /// </summary>
    [System.Serializable]
    public class SpellOrbProfile
    {
        /// <summary>Idle ceiling for any signature spin. Above this the orb competes with a wind-up tell
        /// in the parry quadrant; charge and the cast pose may exceed it briefly, idle may not.</summary>
        public const float MaxIdleSpinDegreesPerSecond = 300f;
        /// <summary>Idle ceiling for the signature motion rate (Hz). The needle crackle is the fastest.</summary>
        public const float MaxIdleMotionRate = 8f;

        [Tooltip("Which generated rig the book shows for this spell. Shape is the primary identity read.")]
        public SpellOrbShape shape = SpellOrbShape.None;

        [Tooltip("Secondary hue for the rig parts and the shell's inner swirl. The core and rim use the spell's own colour.")]
        [ColorUsage(true, true)] public Color detail = Color.white;

        [Header("Motion signature")]
        [Tooltip("Degrees per second of the rig's signature rotation (orbit, ring spin, tumble). 0 for rigs that do not spin.")]
        public float spinDegreesPerSecond = 0f;
        [Tooltip("Cycles per second of the rig's signature motion: heartbeat, ember rise, wisp sink, re-strike, infall.")]
        public float motionRate = 1f;
        [Tooltip("0..1 scale on the signature motion's travel. Idle motion stays well under tell amplitude.")]
        [Range(0f, 1f)] public float motionAmplitude = 0.5f;

        [Header("Glass shell")]
        [Tooltip("Fresnel power of the shell's rim. Higher is a thinner, sharper glass edge.")]
        public float shellRim = 2.2f;
        [Tooltip("0..1 strength of the inner swirling field seen through the glass.")]
        [Range(0f, 1f)] public float shellSwirl = 0.5f;
        [Tooltip("Signed vertical drift of the inner field: positive rises (heat), negative sinks (rot), 0 is lateral.")]
        public float shellFlow = 0f;
        [Tooltip("0..1 heat wobble of the shell surface. Fire only.")]
        [Range(0f, 1f)] public float shellWobble = 0f;
        [Tooltip("0..1 darkening of the shell's centre so the core reads as being pulled inward. Void only.")]
        [Range(0f, 1f)] public float shellDark = 0f;

        /// <summary>Rec.709 relative luminance of the stored channels.</summary>
        public static float Luminance(Color c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        /// <summary>
        /// A colour whose peak channel is exactly <paramref name="peak"/>, hue preserved. Used for every
        /// rig part and the shell: decoration around the core may be luminous but never blooms.
        /// </summary>
        public static Color PeakNormalised(Color c, float peak)
        {
            float max = Mathf.Max(0.0001f, c.maxColorComponent);
            float k = peak / max;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }
    }
}
