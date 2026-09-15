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
        [Tooltip("False for mechanical turret bodies: posture Add/Break calls become inert and generated " +
                 "prefabs omit posture/deathblow presentation. Health and melee damage remain normal.")]
        public bool usesPosture = true;
        [Tooltip("Explicit turret role used by Hook and prefab presentation. Never infer this from rangedOnly.")]
        public bool isTurret;

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

        [Header("Interrupts — the duel reads the player (2026-09-06)")]
        [Tooltip("Chance (0-1) that this enemy ABORTS its recovery or approach and attacks the moment the player " +
                 "starts drinking a flask inside aggro range (FromSoft's heal punish). 0 = never. Chance-based and " +
                 "on a 4 s per-enemy cooldown so the flask is a decision, not a trap. Ignored by sentries.")]
        [Range(0f, 1f)] public float flaskPunishChance = 0f;

        [Tooltip("While this enemy holds a NO-CONTACT stance (an attack with range 0, e.g. the Judge's shield " +
                 "raise) it does not take the duo attack slot, so its partner may attack. Off = strictly sequential.")]
        public bool stanceFreesPartner = false;

        [Header("Boss phase 2 (2026-09-14)")]
        [Tooltip("Health ratio at or under which phase 2 begins (once). 0 = no phase 2.")]
        [Range(0f, 1f)] public float phase2Threshold = 0f;
        [Tooltip("Moveset used from phase 2 on (signatures heavier and on shorter cooldowns). Null = keep the first.")]
        public EnemyMoveset phase2Moveset;
        [Tooltip("Added to aggression in phase 2 (clamped to 1). Wind-ups and the cue are untouched.")]
        [Range(0f, 0.5f)] public float phase2AggressionBonus = 0f;

        [Header("Punishing player states (the flask punish's siblings)")]
        [Tooltip("Chance to attack at once when the player casts a spell (E) inside aggro range. Shares the flask cooldown.")]
        [Range(0f, 1f)] public float spellPunishChance = 0f;
        [Tooltip("Chance to attack at once when the player is airborne with the air dash already spent. Shares the flask cooldown.")]
        [Range(0f, 1f)] public float airPunishChance = 0f;

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

        [Header("Projectile — parkour enemies (2026-09-05)")]
        [Tooltip("This enemy fires parriable bolts down a span while it is awake and not in a melee attack. " +
                 "A perfect deflect sends the bolt back (parriedProjectileDamage / Posture to the shooter) and " +
                 "buys the player parrySpeedGain along their look. The bolt resolves through " +
                 "PlayerCombat.ReceiveAttack like every attack.")]
        public bool shootsProjectiles;
        [Tooltip("A SENTRY (2026-09-06): this enemy never melees. It holds its perch, wakes when the player is inside " +
                 "projectileMaxRange with a line, turns to track them and shoots on the metronome. Nothing about " +
                 "the moveset is touched, so a test may still drive a combo on it directly.")]
        public bool rangedOnly;
        [Tooltip("The attack the bolt carries: its damage and parryPostureMultiplier. Written by DataFactory.")]
        public EnemyAttackData projectileAttack;
        [Tooltip("For a one-shot sentry, seconds between shots. For a burst sentry, the quiet cooldown " +
                 "begins after the phrase's final emission; follow-up cadence is projectileBurstInterval.")]
        public float projectileInterval = 1.6f;
        [Tooltip("Shots in one parry phrase. 1 preserves the ordinary sentry metronome; the Heavy Sentry " +
                 "ships 3. Runtime clamps to its fixed three-shot ownership buffer.")]
        [Min(1)] public int projectileBurstCount = 1;
        [Tooltip("Minimum predicted CONTACT spacing between shots in one phrase, not a blind launch delay. " +
                 "0.42 s clears the 0.28 s cue and 0.08 s perfect-parry recovery with 0.06 s slack.")]
        [Min(0.01f)] public float projectileBurstInterval = 0.42f;
        [Tooltip("THE ARM-UP (bolt-timing plan 2026-09-06, F1). Seconds this enemy must wait after it ACQUIRES " +
                 "the player -- the frame it comes into band with a clear line -- before its first bolt may " +
                 "leave. The metronome is held while the line is blocked, so without this a stale beat fires on " +
                 "the very frame you crest a ledge or land, and two perches covering one crest fire together. " +
                 "0.7 s is one cue lead plus a landing. Capped at one interval: an acquisition costs at most " +
                 "one bolt.")]
        public float projectileAcquireDelay = 0.7f;
        public float projectileSpeed = 32f;
        [Tooltip("Fires only inside this band: far enough that the flight is a readable tell, near enough to matter.")]
        public float projectileMinRange = 10f;
        public float projectileMaxRange = 30f;
        [Tooltip("Fraction of the player's velocity the shot leads by. 1 = aimed where a runner WILL be at impact " +
                 "(you meet the bolt on the run); 0 = aimed where they were (a runner outruns every bolt). " +
                 "Under 1 so a sidestep still steps out of the line.")]
        [Range(0f, 1f)] public float projectileLead = 0.8f;
        [Tooltip("Degrees per second the bolt may TURN in flight toward the player's chest (2026-09-06: 'sometimes it " +
                 "misses and you cannot parry'). 0 = a straight line. 180 makes a bolt that always arrives at a runner " +
                 "while still reading as a line; the cue and the speed are untouched.")]
        public float projectileHomingDegPerSec = 0f;
        [Tooltip("Uses an exact predicted-path obstruction check for a traversal-support sentry instead of the " +
                 "broad player-contact-radius sweep that brushes nearby parkour geometry. It never bypasses range, " +
                 "line of sight, solid walls, frontal arrival or cue safety. Keep false for Heavy Sentries and Surge Turrets.")]
        public bool projectileAllowTightRouteShots;
        [Tooltip("For a broad-clearance shooter standing on a perch, ignores only that collider's radius-only " +
                 "brush while the forecast is leaving it. The bolt centreline, adjacent blockers, and any later " +
                 "re-entry still reject the shot. Used by the Heavy Sentry; keep false for Surge Turrets.")]
        public bool projectileIgnoreDepartureSupport;
        public float parriedProjectileDamage = 30f;
        public float parriedProjectilePosture = 40f;
        [Tooltip("Metres per second added along the look on a perfect deflect of a bolt.")]
        public float parrySpeedGain = 6f;
        [Header("Projectile readability")]
        [Tooltip("Presentation-only multiplier for the bolt core. Logical hit radius and path are unchanged.")]
        [Min(0.5f)] public float projectileVisualScale = 1f;
        [Tooltip("Presentation-only multiplier for trail width.")]
        [Min(0.5f)] public float projectileTrailScale = 1f;
        [Tooltip("Presentation-only multiplier for the press-now cue flare.")]
        [Min(0.5f)] public float projectileCueScale = 1f;
        [Tooltip("Ordered Perfect contacts required from one burst to destroy this turret. Zero disables " +
                 "the contract. Any other incoming result invalidates that burst.")]
        [Min(0)] public int perfectBurstParriesToDestroy;

        [Header("Parry surge — pshooter_enemy03 (2026-09-06)")]
        [Tooltip("How much FirstPersonMotor.SpeedMultiplier one deflected bolt from this enemy is worth through " +
                 "its dedicated SurgeTurret payout. 0 means it uses no enemy-specific payout; ordinary Perfects " +
                 "still use PlayerStatsData's shared surge ladder. The HUD status strip shows live stacks.")]
        public float parrySurgeStep;
        [Tooltip("The ceiling: how many deflects may stack. 1 + step x maxStacks is the fastest the player " +
                 "can ever be made by this enemy.")]
        public int parrySurgeMaxStacks;
        [Tooltip("Seconds of NOT parrying before ONE stack falls off. Stacks are lost one at a time, never " +
                 "all at once, so a missed turret costs a step rather than the whole ramp.")]
        public float parrySurgeSeconds = 1.5f;

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
