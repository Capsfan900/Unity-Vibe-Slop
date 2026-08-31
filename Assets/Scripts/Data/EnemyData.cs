using UnityEngine;

namespace VibeGame1
{
    [CreateAssetMenu(menuName = "VibeGame1/Enemy")]
    public class EnemyData : ScriptableObject
    {
        public string displayName = "Grunt";

        [Header("Vitals")]
        public float maxHP = 60f;
        public float maxPosture = 60f;
        public float postureRegen = 10f;
        public float postureRegenDelay = 1.5f;
        public float staggerSeconds = 3f;

        [Header("Movement")]
        public float moveSpeed = 4.5f;
        public float turnSpeed = 360f;
        public float aggroRange = 14f;
        public float attackRange = 2.2f;
        public float attackCooldown = 0.4f;
        public float parryRecoilSeconds = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("Sekiro-style relentlessness. 0 = unchanged (passive, one combo then back off), " +
                 "1 = maximum pressure: recovery and cooldown are cut hard, the enemy steps toward you " +
                 "between combos, and it keeps swinging through your deflects instead of resetting.\n" +
                 "Readability is NOT affected: the parry cue still fires cueLead seconds before every impact.")]
        public float aggression = 0f;

        [Header("Weight — aggressive should read as deliberate, not twitchy")]
        [Tooltip("Turn rate multiplier while winding up. Well under 1 so a committed attack stays committed " +
                 "and circling the enemy is a real answer. This is the main thing separating pressure from unfairness.")]
        [Range(0.05f, 1f)] public float windupTurnMultiplier = 0.3f;
        [Tooltip("Top speed of the Recover-state step-in, as a fraction of moveSpeed. Kept well below chase " +
                 "speed so closing the gap reads as a stalk rather than a dart.")]
        [Range(0f, 1f)] public float stepSpeedMultiplier = 0.35f;
        [Tooltip("How fast the step-in accelerates and decelerates (m/s^2). Low values remove the snap.")]
        public float stepAcceleration = 7f;
        [Tooltip("Stop stepping once within this fraction of attackRange — kills the micro-adjust jitter.")]
        [Range(0.5f, 1.2f)] public float stepDeadzone = 0.95f;
        [Tooltip("Minimum pause after a combo ends, before anything else. Aggression may not compress this " +
                 "away: a combo has to read as a phrase with a breath after it, not an endless stream.")]
        public float comboBreathSeconds = 0.45f;
        [Tooltip("While waiting for an attack slot, hold at this multiple of preferredRange and circle.")]
        public float readyDistanceMultiplier = 1.7f;

        [Header("Spacing — you cannot read a wind-up you are standing inside")]
        [Tooltip("The distance this enemy WANTS to fight from, centre to centre. It approaches to here and " +
                 "HOLDS rather than closing all the way in. Must be >= attackRange, and larger for big " +
                 "enemies: a 2.2x-scale boss standing at 2m fills the screen and its telegraph is unreadable.\n" +
                 "Attacks are launched from here and close the remaining gap with their own lungeDistance.")]
        public float preferredRange = 3f;
        [Tooltip("How far beyond preferredRange the enemy will still commit to an attack, rather than " +
                 "shuffling to get perfectly placed first.")]
        public float commitTolerance = 0.6f;
        [Tooltip("Slack around preferredRange where the enemy stops correcting and just strafes. Without " +
                 "this it oscillates in and out forever.")]
        public float repositionDeadzone = 0.45f;
        [Tooltip("Backing-off speed as a fraction of moveSpeed. Retreating should read as deliberate " +
                 "disengagement, not a panicked scuttle, so keep it below the step-in speed.")]
        [Range(0f, 1f)] public float backStepSpeedMultiplier = 0.28f;
        [Tooltip("Sidestep speed as a fraction of moveSpeed, used while holding at preferredRange.")]
        [Range(0f, 1f)] public float strafeSpeedMultiplier = 0.3f;
        [Tooltip("The lunge stops closing once this near the player, so a committed attack travels into " +
                 "range without burrowing through them.")]
        public float lungeMinDistance = 1.2f;

        [Header("Reward")]
        public int soulValue = 40;

        [Header("Look")]
        public Color bodyColor = new Color(0.16f, 0.04f, 0.04f);
        [ColorUsage(true, true)] public Color emission = new Color(1f, 0.16f, 0.16f) * 1.8f;
        public float scale = 1f;

        [Header("Attacks")]
        [Tooltip("Optional shared repertoire. When assigned this is the AUTHORING source of truth: it adds " +
                 "per-combo weights and range gating that a plain array cannot express, and it can be " +
                 "reused across enemies.\n" +
                 "DataFactory mirrors it down into 'combos' so the runtime path is unchanged — see " +
                 "SelectCombo() for the range-aware selection that supersedes it once EnemyController " +
                 "calls through.")]
        public EnemyMoveset moveset;

        [Tooltip("The flat repertoire actually read by EnemyController today. Mirrored from 'moveset' when " +
                 "one is assigned; authored directly when it is not.")]
        public AttackCombo[] combos;

        /// <summary>
        /// The combos in effect: the moveset when one is assigned, otherwise the inline array. Use this
        /// rather than touching <c>combos</c> directly so an assigned moveset always wins.
        /// </summary>
        public AttackCombo[] ResolveCombos()
        {
            if (moveset != null)
            {
                var fromMoveset = moveset.ToComboArray();
                if (fromMoveset != null && fromMoveset.Length > 0) return fromMoveset;
            }
            return combos;
        }

        /// <summary>
        /// Range-aware, weighted combo selection. Falls back to a uniform pick over <c>combos</c> when no
        /// moveset is assigned, so behaviour is identical for enemies that have not been migrated.
        /// </summary>
        public AttackCombo SelectCombo(float distanceToTarget)
        {
            if (moveset != null)
            {
                var picked = moveset.Select(distanceToTarget);
                if (picked != null) return picked;
            }
            if (combos == null || combos.Length == 0) return null;
            return combos[Random.Range(0, combos.Length)];
        }
    }
}
