using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// An enemy's attack repertoire as a shareable asset.
    ///
    /// Movesets used to be inline <c>new AttackCombo(...)</c> arrays inside <c>DataFactory</c>, which means
    /// a new enemy archetype is a code change and two enemies can never share a repertoire. As an asset a
    /// moveset can be authored in the Inspector, reused across enemies, and swapped per boss phase.
    ///
    /// Beyond plain reuse this adds two things the inline arrays could not express:
    /// <list type="bullet">
    /// <item><b>Weight</b> — how often a combo is chosen, so a signature bait can be rare and a filler common.</item>
    /// <item><b>Range gating</b> — a combo is only eligible within a distance band, so a lunging opener is
    /// picked at reach and a fast flurry up close. Choosing a combo the enemy cannot reach is what makes
    /// an aggressive enemy look like it is flailing.</item>
    /// </list>
    ///
    /// Selection never affects readability: every wind-up is still ≥ 0.45 s and the parry cue still fires
    /// <c>cueLead</c> before each impact, whichever combo is chosen.
    /// </summary>
    [CreateAssetMenu(menuName = "VibeGame1/Enemy Moveset")]
    public class EnemyMoveset : ScriptableObject
    {
        [Tooltip("Shown in logs and the Inspector; not used at runtime.")]
        public string displayName = "Moveset";

        [Tooltip("Every combo this enemy can throw. Order does not matter — selection is weighted.")]
        public MovesetEntry[] entries = new MovesetEntry[0];

        /// <summary>
        /// Weighted, range-filtered pick. Returns null when nothing is eligible, which callers should
        /// treat as "hold position this beat" rather than as an error.
        /// </summary>
        /// <summary>
        /// Is there an entry whose band contains this distance? The far-band commit in
        /// <c>EnemyController</c> asks this before attacking from OUTSIDE the commit band, so an enemy
        /// with a charge authored for 5-18 m throws it from there, while one with nothing authored for
        /// range keeps walking in. <see cref="Select"/>'s fallback-to-anything is deliberately not
        /// consulted for that decision: a sweep thrown from 7 m is the whiff this exists to prevent.
        /// </summary>
        public bool HasEligible(float distanceToTarget)
        {
            if (entries == null) return false;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i] != null && entries[i].IsEligible(distanceToTarget) && entries[i].weight > 0f)
                    return true;
            return false;
        }

        public AttackCombo Select(float distanceToTarget)
        {
            if (entries == null || entries.Length == 0) return null;

            // Two passes rather than building a list: this runs inside enemy AI and allocating a
            // temporary list per attack decision is exactly the churn the perf pass is removing.
            float total = 0f;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].IsEligible(distanceToTarget)) total += Mathf.Max(0.0001f, entries[i].weight);

            if (total <= 0f) return FallbackAny();

            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < entries.Length; i++)
            {
                if (!entries[i].IsEligible(distanceToTarget)) continue;
                roll -= Mathf.Max(0.0001f, entries[i].weight);
                if (roll <= 0f) return entries[i].combo;
            }
            return FallbackAny();
        }

        /// <summary>
        /// Nothing was in range. Returning *something* beats returning nothing: an enemy that refuses to
        /// attack because the player is standing half a metre too far away reads as broken, and the
        /// spacing logic will close the gap during the lunge anyway.
        /// </summary>
        AttackCombo FallbackAny()
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].combo != null && entries[i].combo.hits != null && entries[i].combo.hits.Length > 0)
                    return entries[i].combo;
            return null;
        }

        /// <summary>Flattens to the plain array shape <c>EnemyData.combos</c> expects.</summary>
        public AttackCombo[] ToComboArray()
        {
            if (entries == null) return new AttackCombo[0];
            int n = 0;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].combo != null && entries[i].combo.hits != null && entries[i].combo.hits.Length > 0) n++;

            var result = new AttackCombo[n];
            int w = 0;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].combo != null && entries[i].combo.hits != null && entries[i].combo.hits.Length > 0)
                    result[w++] = entries[i].combo;
            return result;
        }
    }

    /// <summary>One combo plus the rules for when it may be chosen.</summary>
    [Serializable]
    public class MovesetEntry
    {
        [Tooltip("Label for the Inspector, e.g. 'jab-jab-HEAVY (the bait)'. Not used at runtime.")]
        public string label = "";

        [Tooltip("The hits, in order. One entry is a single attack; three is a three-hit phrase.")]
        public AttackCombo combo = new AttackCombo();

        [Tooltip("Relative likelihood among all eligible combos. 1 is ordinary; 0.3 makes a signature " +
                 "combo rare enough to stay surprising; 3 makes a filler the default rhythm.")]
        [Min(0f)] public float weight = 1f;

        [Tooltip("Closest distance at which this combo may be chosen. Raise it for a lunging opener that " +
                 "looks silly point-blank.")]
        public float minRange = 0f;

        [Tooltip("Furthest distance at which this combo may be chosen. Set generously — the enemy commits " +
                 "from preferredRange and the attack's own lungeDistance closes the rest.")]
        public float maxRange = 99f;

        public bool IsEligible(float distance)
        {
            if (combo == null || combo.hits == null || combo.hits.Length == 0) return false;
            return distance >= minRange && distance <= maxRange;
        }
    }
}
