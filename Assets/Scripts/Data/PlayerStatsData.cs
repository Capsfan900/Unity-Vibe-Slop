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
        public float parryPerfectWindow = 0.13f;
        public float parryLateWindow = 0.12f;
        [Tooltip("Recovery after a parry pressed at NOTHING. Long on purpose: this is the mash tax.")]
        public float parryWhiffRecovery = 0.5f;
        public float parrySuccessRecovery = 0.08f;
        public float blockDamageMultiplier = 0.3f;
        public float facingConeDeg = 75f;

        [Header("Parry responsiveness")]
        [Tooltip("A parry press is remembered for this long and spent the instant the player can act again, " +
                 "so input is never silently dropped mid-flurry. Costs no latency when already idle.")]
        public float parryInputBuffer = 0.2f;
        [Tooltip("Recovery after a parry that MISSED its window while an attack was genuinely incoming. " +
                 "Far shorter than the mash tax: a mistimed deflect is a mistake, not spam, and eating the " +
                 "full penalty would lock the player out of the next hit of a combo.")]
        public float parryMistimeRecovery = 0.2f;
        [Tooltip("How far ahead to look for a scheduled impact when deciding whether a press was contested.")]
        public float parryIncomingLookahead = 0.6f;
        [Tooltip("Recovery is clamped so it always ends this long BEFORE the next parry cue fires.")]
        public float parryCueSafetyMargin = 0.04f;
        [Tooltip("Floor for a clamped recovery, so a deflect still reads as a distinct beat.")]
        public float parryMinRecovery = 0.05f;

        [Header("Parry Speed Surge")]
        [Tooltip("Speed multiplier added for each perfect parry outside the opening Surge Turret row.")]
        public float generalParrySurgeStep = 0.12f;
        [Tooltip("Maximum speed-surge stacks earned from ordinary enemies, elites, and bosses.")]
        public int generalParrySurgeMaxStacks = 5;
        [Tooltip("Seconds between one-stack decay steps after the latest ordinary, elite, or boss perfect parry.")]
        public float generalParrySurgeSeconds = 2f;

        [Header("Posture (Sekiro)")]
        public float basePosture = 100f;
        public float posturePerVitality = 4f;
        public float postureRegenPerSecond = 22f;
        public float postureRegenDelay = 1.2f;
        public float postureStaggerSeconds = 1.5f;
        [Tooltip("Posture gained per point of incoming damage when the hit is BLOCKED (late parry).")]
        public float blockPostureMultiplier = 0.9f;
        [Tooltip("Posture gained per point of incoming damage when the hit lands clean.")]
        public float hitPostureMultiplier = 0.5f;
        [Tooltip("Damage amplification while the player's posture is broken.")]
        public float staggeredDamageMultiplier = 1.6f;

        [Header("Guard (HOLD the parry button — the Sekiro stance)")]
        [Tooltip("Chip damage per point of incoming damage taken through a HELD guard. Shipped at 0: " +
                 "the Sekiro contract is that a guard costs POSTURE, not health, and that is the entire " +
                 "reason posture exists as a second bar. Guarding is still strictly worse than " +
                 "deflecting (it costs posture and stokes no Pyre) and strictly better than eating the " +
                 "hit (no health at all), so the ladder Perfect > Guard > Hit holds without chip.")]
        public float guardChipDamageMultiplier = 0f;
        [Tooltip("Posture gained per point of incoming damage taken through a HELD guard. The largest " +
                 "multiplier in the game — larger than a timed block (0.9) and three times a raw hit " +
                 "(0.5) — because posture is the ONLY price the guard charges. Three guarded hits from " +
                 "a 20-damage attack fill the bar and break you.")]
        public float guardPostureMultiplier = 1.5f;
        [Tooltip("Multiplier on posture regeneration WHILE the guard is held. Shipped at 0: turtling " +
                 "must not be free, or the fight becomes a stalemate you can win by standing still.")]
        public float guardPostureRegenMultiplier = 0f;

        [Header("Pyre (the parry charge meter) / Super attack")]
        [Tooltip("Full bar. At full the equipped weapon's super attack unlocks on Q.")]
        public float maxPyre = 100f;
        [Tooltip("Pyre gained by a PERFECT parry, before the weapon's pyreBonus and Arcane.")]
        public float basePyrePerPerfect = 25f;
        [Tooltip("Fraction of the perfect gain awarded for a BLOCK (late parry). A block survives a " +
                 "hit; it is not mastery, so it stokes the fire far more slowly.")]
        public float pyreBlockFraction = 0.35f;
        [Tooltip("Extra Pyre per perfect parry per point of Arcane.")]
        public float pyrePerArcane = 1f;
        [Tooltip("The Pyre bar does NOT decay. It is spent, in full, by the super attack.")]
        public float ultSlowScale = 0.25f;
        public float ultSlowSeconds = 1.6f;
        [Tooltip("Fraction of a BOSS's posture bar a super fills. Ordinary enemies take the weapon's " +
                 "full superPostureDamage instead.")]
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
