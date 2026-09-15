using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Melee enemy state machine. Attacks are telegraphed and resolved by a distance/cone test at impact
    /// time through PlayerCombat.ReceiveAttack.
    ///
    /// <para><b>This brain is rig-agnostic.</b> It knows states, timings and distances — nothing else. It
    /// never references a NavMeshAgent, a Renderer or an Animator. Movement goes through
    /// <see cref="IEnemyLocomotion"/> and presentation through <see cref="IEnemyPresentation"/>, so an
    /// authored animated enemy is a new pair of implementations (Animator-driven presentation, root-motion
    /// locomotion) with zero changes here.</para>
    ///
    /// <para><b>Presentation timing is DATA-driven, never animation-driven.</b> Telegraph is handed the
    /// wind-up duration and must fit it. If a clip length were ever allowed to set the wind-up, the
    /// guarantee that the cue fires cueLead before every impact would break and attacks would stop being
    /// reliably parryable.</para>
    /// </summary>
    // NavMeshLocomotion is required so a rebuilt prefab gets a working body automatically without
    // PrefabFactory having to know about it. This attribute is prefab-authoring convenience only — the
    // field below is typed as IEnemyLocomotion, so the BRAIN stays rig-agnostic. A model-based enemy
    // drops this attribute and supplies its own IEnemyLocomotion component instead.
    [RequireComponent(typeof(Health), typeof(Posture), typeof(NavMeshLocomotion))]
    public class EnemyController : MonoBehaviour
    {
        public enum State { Idle, Chase, Windup, Strike, Recover, Staggered, Executed, Dead }

        public EnemyData data;
        [Tooltip("When true the enemy ignores the player until aggroLocked is cleared (boss arena).")]
        public bool aggroLocked;

        [Tooltip("Seconds before the hit lands that the 'parry now' cue fires.\n" +
                 "Sized as (reaction time ~0.20s) + (half the perfect window) so a player reacting to the cue " +
                 "lands mid-window. Too small and the parry becomes unreactable; too large and it becomes a rhythm game.")]
        [SerializeField] float cueLead = 0.28f;

        public State Current { get; private set; } = State.Idle;
        public Health Health { get; private set; }
        public Posture Posture { get; private set; }
        public EnemyAttackData CurrentAttack => attack;
        /// <summary>Seconds before impact the parry cue fires (serialized per instance, 0.28 everywhere).</summary>
        public float CueLead => cueLead;
        public bool IsStaggered => Current == State.Staggered;
        /// <summary>True once the body died to a deathblow / execute (set in Die, before the death events fire). A
        /// parkour enemy finished this way throws no flare (SentryBurst).</summary>
        public bool DiedExecuted { get; private set; }
        public bool IsAlive => Current != State.Dead;

        protected IEnemyLocomotion locomotion;
        protected IEnemyPresentation visuals;

        /// <summary>
        /// A world point on the VISIBLE body, <paramref name="localHeight"/> up it — what to aim a
        /// marker or a camera assist at. Falls back to this transform when there is no presentation,
        /// which is the case in a bare unit test.
        ///
        /// <para>Read-only and deliberately narrow: it exposes a POINT, not the presentation, so the
        /// <see cref="IEnemyPresentation"/> seam stays closed and nothing outside can start driving the
        /// body.</para>
        /// </summary>
        public Vector3 BodyPoint(float localHeight)
        {
            return visuals != null
                ? visuals.BodyPoint(localHeight)
                : transform.TransformPoint(new Vector3(0f, localHeight, 0f));
        }
        protected Transform player;
        protected PlayerCombat playerCombat;
        protected float windupMult = 1f;
        protected float speedMult = 1f;

        float stateEnd;
        float nextAttackTime;
        AttackCombo combo;
        int comboIndex;
        EnemyAttackData attack;
        float impactTime;
        bool struck;
        float cueTime;
        bool cued;

        /// <summary>Set when a deflect interrupts a combo the enemy should push through anyway.</summary>
        bool resumeComboAfterRecover;

        // ---- move history (per instance, never on the shared moveset asset) ----
        float[] moveLastUsedAt;
        /// <summary>Entry index of the last combo chosen from the moveset, or -1. Tests read it.</summary>
        public int LastMoveIndex { get; private set; } = -1;

        // ---- interrupts ----
        FlaskAbility playerFlask;
        bool playerWasDrinking;
        float nextFlaskPunishAt;
        /// <summary>Seconds between two flask punishes by the same enemy.</summary>
        public const float FlaskPunishCooldown = 4f;
        /// <summary>How many times this enemy has punished a flask. Tests and the harness.</summary>
        public int FlaskPunishes { get; private set; }
        /// <summary>Consecutive deflects inside the current exchange; tightens each following gap.</summary>
        int parryStreak;

        /// <summary>0 = stock passive behaviour, 1 = maximum pressure. Never affects telegraph readability.</summary>
        protected float Aggression => data != null
            ? Mathf.Clamp01(data.aggression + (phase2 ? data.phase2AggressionBonus : 0f)) : 0f;

        // ---- phase 2 ----
        bool phase2;
        /// <summary>True once health crossed <see cref="EnemyData.phase2Threshold"/>. Tests and the HUD read it.</summary>
        public bool InPhase2 => phase2;
        public const float Phase2RoarSeconds = 0.9f;

        /// <summary>Phase 2 is due exactly once, when the ratio first reaches the threshold. Pure.</summary>
        public static bool PhaseShiftDue(float healthRatio, float threshold, bool already)
        {
            return !already && threshold > 0f && healthRatio <= threshold && healthRatio > 0f;
        }

        EnemyMoveset ActiveMoveset => data == null ? null
            : phase2 && data.phase2Moveset != null ? data.phase2Moveset : data.moveset;

        // ---- punish edges beyond the flask (spec 4.5) ----
        WandController playerWand;
        FirstPersonMotor playerMotor;
        float lastWandCooldown;
        float airSpentSeconds;
        public const float AirPunishDelay = 0.25f;

        /// <summary>Damped speed of the Recover-state step-in, so closing the gap accelerates rather than snapping.</summary>
        float stepSpeed;
        /// <summary>Stable per-enemy circling direction, so waiting enemies don't all orbit the same way.</summary>
        int circleDir = 1;
        /// <summary>Projected impact time of the committed attack; float.MaxValue when not committed.</summary>
        float projectedImpact = float.MaxValue;

        // ---- committed lunge ---------------------------------------------------------------------
        // The enemy commits from preferredRange — far enough back that the wind-up is readable — and then
        // physically travels the attack's lungeDistance so the blow still lands. The travel starts at the
        // CUE and ends at impact, which is what makes the cue read as "it is coming at you NOW".
        // Direction is frozen to transform.forward at commit time, so strafing genuinely dodges it.
        float lungeSpeed;
        float lungeEndTime;
        Vector3 lungeDir;
        /// <summary>When this swing's travel starts: the cue for a short lunge, earlier for a long one.</summary>
        float lungeStartTime = float.MaxValue;
        bool lungeCommitted;

        /// <summary>The fastest a body may cover its lunge. A 4.7 m charge squeezed into the 0.28 s cue window
        /// ran at 16.8 m/s after half a second of running in place (Fable spatial spec R2).</summary>
        public const float MaxLungeSpeed = 9f;

        /// <summary>Seconds before impact the lunge's travel starts: never later than the cue, earlier when the
        /// distance would otherwise exceed <see cref="MaxLungeSpeed"/>. Every lunge up to 2.5 m stays cue-bound. Pure.</summary>
        public static float LungeWindow(float lungeDistance, float cueLeadSeconds)
        {
            return Mathf.Max(cueLeadSeconds, Mathf.Max(0f, lungeDistance) / MaxLungeSpeed);
        }

        // ---- deflect step-back (spec R4): the body pays for being parried, not just the mesh ------------
        public const float ParryRecoilMetres = 0.35f;
        float recoilStepSpeed, recoilStepEnd;

        // ---- global attack arbitration -----------------------------------------------------------
        // The player's parry facing cone is 75 degrees, so two enemies attacking from opposite sides are
        // not simultaneously parryable no matter how good the player is. Rather than widen the cone (which
        // would cheapen every deflect) we queue the attackers: one commits, the rest hold at a ready
        // distance and circle. Pressure stays high, but it stays ANSWERABLE.

        static readonly List<EnemyController> activeEnemies = new List<EnemyController>();
        public static IReadOnlyList<EnemyController> ActiveEnemies => activeEnemies;

        /// <summary>
        /// How many enemies may be mid-attack at once. 1 reads best; 2 is chaotic but survivable.
        /// Static for the hot path, but the SHIPPED value lives on <see cref="GameFeelSettings"/> and is
        /// seeded by <see cref="GameManager.Awake"/> — a bare static resets to this initialiser on every
        /// domain reload, so it is not a place tuning can survive. Change it in the asset, not here.
        /// </summary>
        public static int MaxSimultaneousAttackers = 1;

        /// <summary>Time this enemy's committed attack will land, or MaxValue when it has no hit pending.</summary>
        public float NextImpactTime
        {
            get
            {
                if (Current == State.Windup) return projectedImpact;
                if (Current == State.Strike && !struck) return impactTime;
                return float.MaxValue;
            }
        }

        /// <summary>Time this enemy's "parry now" cue fires, or MaxValue when it has already fired or is not attacking.</summary>
        public float NextCueTime =>
            !cued && (Current == State.Windup || Current == State.Strike) ? cueTime : float.MaxValue;

        /// <summary>True when the enemy is committed to a swing (used for arbitration and by the player's parry).</summary>
        public bool IsCommitted => Current == State.Windup || Current == State.Strike;

        static bool InThreatRange(EnemyController e, Vector3 playerPos)
        {
            if (e.data == null) return false;
            float reach = e.data.preferredRange + 3f;
            return (e.transform.position - playerPos).sqrMagnitude <= reach * reach;
        }

        /// <summary>Is any enemy actually about to hit the player? Drives the player's graduated whiff penalty.</summary>
        public static bool AnyAttackIncoming(Vector3 playerPos, float withinSeconds)
        {
            float limit = Time.time + withinSeconds;
            for (int i = 0; i < activeEnemies.Count; i++)
            {
                var e = activeEnemies[i];
                if (e == null || !e.IsAlive) continue;
                if (e.NextImpactTime > limit) continue;
                if (!InThreatRange(e, playerPos)) continue;
                return true;
            }
            // A bolt in flight is incoming too (bolt-timing plan 2026-09-06, F2). No range test: a bolt is
            // already aimed at the player, so its ARRIVAL TIME is the question, not how far its perch is.
            return BoltRegistry.AnyImpactBefore(limit);
        }

        /// <summary>Soonest upcoming parry cue, so the player's recovery can be clamped to end before it.</summary>
        public static float EarliestCueTime(Vector3 playerPos, float withinSeconds)
        {
            float limit = Time.time + withinSeconds;
            float best = float.MaxValue;
            for (int i = 0; i < activeEnemies.Count; i++)
            {
                var e = activeEnemies[i];
                if (e == null || !e.IsAlive) continue;
                float t = e.NextCueTime;
                if (t > limit || t >= best) continue;
                if (!InThreatRange(e, playerPos)) continue;
                best = t;
            }
            return Mathf.Min(best, BoltRegistry.EarliestCueTime(limit));   // ...and a bolt's cue counts (F2)
        }

        /// <summary>
        /// Self-healing arbitration: we count who is currently mid-swing rather than handing out tokens that
        /// have to be returned. An enemy that dies, staggers or is executed drops out of the count for free.
        /// </summary>
        bool MayCommitToAttack()
        {
            // A bolt, disc or javelin landing inside the earliest possible wind-up + parry window is an attack
            // in progress too: committing now would stack two answers on one beat.
            if (BoltRegistry.AnyImpactBefore(Time.time + ProjectileCommitHorizon)) return false;
            int committed = 0;
            for (int i = 0; i < activeEnemies.Count; i++)
            {
                var e = activeEnemies[i];
                if (e == null || e == this || !e.IsAlive) continue;
                if (e.IsCommitted && !HoldsFreeStance(e.data, e.attack)) committed++;
            }
            return committed < Mathf.Max(1, MaxSimultaneousAttackers);
        }

        /// <summary>Shortest wind-up floor (0.45) + impact slack (0.05) + parry window (0.4).</summary>
        public const float ProjectileCommitHorizon = 0.9f;

        /// <summary>A no-contact stance on an enemy authored to free its partner does not hold the attack slot.</summary>
        public static bool HoldsFreeStance(EnemyData d, EnemyAttackData atk)
        {
            return d != null && d.stanceFreesPartner && atk != null && atk.range <= 0f;
        }

        /// <summary>
        /// Chain straight into the next phrase instead of Recover -> Chase -> walk -> commit. Only off a
        /// short-recovery phrase (a signature's big recovery stays the punish), only with the player still in
        /// the commit band, and only as often as the enemy is aggressive. Every hit is still cued.
        /// </summary>
        public static bool ShouldChainPhrase(float aggression, float recovery, float dist, float band, float roll)
        {
            return recovery <= MaxChainRecovery && dist <= band && roll < aggression;
        }

        // ---- accuracy (Fable souls-AI accuracy spec 2026-09-14, A1-A6) ---------------------------------
        // The enemy may close, aim and snap only up to the cue; from the cue the swing is a frozen commit.
        // Walk = hit, sprint strafe = dodge, backpedal = eat the closer.

        /// <summary>Cap on the player's radial speed fed into the predicted-distance gate.</summary>
        public const float LeadCap = 6f;
        /// <summary>Seconds of retreat the selector looks ahead, so a backpedal is SEEN where the closers live.</summary>
        public const float SelectHorizon = 0.6f;
        /// <summary>Seconds a landable commit may be refused in band before the enemy throws anyway.</summary>
        public const float RefuseTimeout = 1.2f;
        /// <summary>Cap on the player's lateral speed the cue's aim leads.</summary>
        public const float LeadCapLateral = 3f;
        /// <summary>The largest yaw jump the commit snap at the cue may make.</summary>
        public const float CommitSnapDeg = 25f;

        /// <summary>Speed of the wind-up stalk-in. Pure.</summary>
        public static float StalkSpeed(float moveSpeed, float stepSpeedMultiplier) => moveSpeed * stepSpeedMultiplier;

        /// <summary>The stalk stops here, so the lunge still has room to arrive. Pure.</summary>
        public static float StalkTarget(float lungeMin, float range, float lunge) => Mathf.Max(lungeMin, range - lunge - 0.2f);

        /// <summary>Player speed away from the enemy (retreat &gt; 0), clamped. Pure.</summary>
        public static float RadialSpeed(Vector3 playerVelocity, Vector3 enemyToPlayer)
        {
            playerVelocity.y = 0f; enemyToPlayer.y = 0f;
            if (enemyToPlayer.sqrMagnitude < 0.0001f) return 0f;
            return Mathf.Clamp(Vector3.Dot(playerVelocity, enemyToPlayer.normalized), -LeadCap, LeadCap);
        }

        /// <summary>Where the player will stand, relative to the body, when the first hit lands. Pure.</summary>
        public static float PredictedImpactDistance(float dist, float radial, float seconds, float stalkSpeed,
                                                    float stalkSeconds, float lunge, float lungeMin)
        {
            return Mathf.Max(lungeMin, dist + radial * seconds - stalkSpeed * Mathf.Max(0f, stalkSeconds) - lunge);
        }

        /// <summary>A no-contact stance (range 0) always lands; a blow lands inside range + 0.5. Pure.</summary>
        public static bool CanLand(float predicted, float range) => range <= 0f || predicted <= range + 0.5f;

        /// <summary>The distance the moveset is read at: a retreat looks further away, an approach does not. Pure.</summary>
        public static float SelectDistance(float dist, float radial, float horizon) => dist + Mathf.Max(0f, radial) * horizon;

        /// <summary>Direction from <paramref name="pos"/> to the player led by a capped velocity. Pure, flat.</summary>
        public static Vector3 CommitAim(Vector3 playerPos, Vector3 playerVelocity, Vector3 pos, float leadCap, float seconds)
        {
            playerVelocity.y = 0f;
            Vector3 d = playerPos + Vector3.ClampMagnitude(playerVelocity, leadCap) * Mathf.Max(0f, seconds) - pos;
            d.y = 0f;
            return d;
        }

        /// <summary>Turn <paramref name="current"/> toward <paramref name="wanted"/> by at most maxDeg (flat, unit). Pure.</summary>
        public static Vector3 SnapYaw(Vector3 current, Vector3 wanted, float maxDeg)
        {
            current.y = 0f; wanted.y = 0f;
            if (wanted.sqrMagnitude < 0.0001f) return current.normalized;
            if (current.sqrMagnitude < 0.0001f) return wanted.normalized;
            return Vector3.RotateTowards(current.normalized, wanted.normalized, maxDeg * Mathf.Deg2Rad, 0f).normalized;
        }

        float refusedSince = -1f;

        Vector3 PlayerVelocity()
        {
            if (playerMotor == null && player != null) playerMotor = player.GetComponent<FirstPersonMotor>();
            return playerMotor != null ? playerMotor.Velocity : Vector3.zero;
        }

        /// <summary>Will this first hit, thrown now, arrive? The same law the stalk and lunge execute.</summary>
        bool FirstHitCanLand(EnemyAttackData atk, float dist, float radial)
        {
            if (atk == null || data == null) return true;
            float seconds = atk.windup * windupMult + atk.impactDelay;
            float stalkSeconds = seconds - LungeWindow(atk.lungeDistance, cueLead);
            float predicted = PredictedImpactDistance(dist, radial, seconds, StalkSpeed(data.moveSpeed * speedMult, data.stepSpeedMultiplier),
                                                      stalkSeconds, atk.lungeDistance, data.lungeMinDistance);
            return CanLand(predicted, atk.range);
        }

        /// <summary>Pick at the predicted distance and commit only what lands. Refused in band, the body presses in;
        /// after RefuseTimeout it throws anyway (honest pressure, not a loop). Returns true on a commit.</summary>
        bool TryCommit(float dist, Vector3 toP, float dt)
        {
            float radial = RadialSpeed(PlayerVelocity(), toP);
            lastPickIndex = -1;   // an override's pick (the Warden's patterns) has no cooldown to undo
            var c = ChooseCombo(SelectDistance(dist, radial, SelectHorizon));
            if (c == null || c.hits == null || c.hits.Length == 0) return false;
            bool timedOut = refusedSince >= 0f && Time.time - refusedSince >= RefuseTimeout;
            // ponytail: on timeout it throws whatever was rolled this frame, not the longest-reach entry.
            if (timedOut || FirstHitCanLand(c.hits[0], dist, radial))
            {
                refusedSince = -1f;
                BeginCombo(c);
                return true;
            }
            UndoLastPick();
            if (refusedSince < 0f) refusedSince = Time.time;
            if (locomotion != null && locomotion.IsReady && dist > data.lungeMinDistance && toP.sqrMagnitude > 0.0001f)
                locomotion.Nudge(toP.normalized * data.moveSpeed * speedMult * dt);
            return false;
        }

        int lastPickIndex = -1, lastPickPrevMove = -1;
        float lastPickPrevTime;

        /// <summary>A refused pick never happened: its cooldown clock and LastMoveIndex are restored.</summary>
        void UndoLastPick()
        {
            if (lastPickIndex < 0 || moveLastUsedAt == null || lastPickIndex >= moveLastUsedAt.Length) return;
            moveLastUsedAt[lastPickIndex] = lastPickPrevTime;
            LastMoveIndex = lastPickPrevMove;
            lastPickIndex = -1;
        }

        public const float MaxChainRecovery = 1f;
        /// <summary>Phrases chained back to back before a real breath is forced.</summary>
        public const int MaxChainedPhrases = 2;
        int chainedPhrases;

        protected virtual void Awake()
        {
            // Resolved as interfaces: whatever component supplies them is the body. Both may legitimately
            // be null (a headless test dummy) — every call below null-guards.
            locomotion = GetComponentInChildren<IEnemyLocomotion>();
            Health = GetComponent<Health>();
            Posture = GetComponent<Posture>();
            visuals = GetComponentInChildren<IEnemyPresentation>();
            if (locomotion == null)
                Debug.LogError($"[Enemy] {name} has no IEnemyLocomotion — it will never move. " +
                               "Add NavMeshLocomotion (or another implementation) to the prefab root.", this);
            gameObject.layer = Layers.Enemy;
            // Stable per-enemy orbit direction. Derived from the name hash rather than an entity id:
            // Unity 6 is deprecating int casts on EntityId, and this needs no engine identity anyway.
            circleDir = (name.GetHashCode() & 1) == 0 ? 1 : -1;
        }

        protected virtual void OnEnable()
        {
            if (!activeEnemies.Contains(this)) activeEnemies.Add(this);
        }

        protected virtual void OnDisable()
        {
            // Destroy() fires OnDisable, so the registry self-cleans on death with no explicit release.
            activeEnemies.Remove(this);
        }

        protected virtual void Start()
        {
            EnsurePlayerTarget();
            Init();
            Health.OnDied += HandleDeath;
            Health.OnDamaged += HandleDamaged;
            Posture.OnBroken += HandleBroken;
            Posture.OnStaggerEnded += HandleStaggerEnded;
            SetState(State.Idle);
        }

        protected virtual void Init()
        {
            if (data == null) { Debug.LogError($"[Enemy] {name} has no EnemyData", this); return; }
            Health.SetMax(data.maxHP, false);
            Health.ResetFull();
            Posture.Configure(data.maxPosture, data.postureRegen, data.postureRegenDelay, data.staggerSeconds,
                              data.usesPosture);
            Posture.RegenMultiplier = () => Mathf.Lerp(0.25f, 1f, Health.Ratio);
            // Stand off at preferredRange, NOT at a fraction of attackRange. The old value
            // (attackRange * 0.7) parked a 2.2x-scale boss ~2m away, where its wind-up filled the screen
            // and could not be read. Stopping short is what buys the player room to see the tell.
            float standoff = Mathf.Max(data.attackRange * 0.95f, data.preferredRange);
            if (locomotion != null)
                locomotion.Configure(data.moveSpeed * speedMult, data.turnSpeed, 40f,
                                     standoff, 0.45f * data.scale, 2f * data.scale);
            transform.localScale = Vector3.one * data.scale;
            if (visuals != null) visuals.Setup(data);
        }

        protected virtual void Update()
        {
            if (Current == State.Dead || Current == State.Executed || data == null) return;
            if (!EnsurePlayerTarget()) return;
            if (Time.timeScale <= 0f) return;

            if (visuals != null) visuals.SetPostureRatio(Posture.Ratio);

            Vector3 toP = player.position - transform.position; toP.y = 0f;
            float dist = toP.magnitude;
            float dt = Time.deltaTime;

            // INTERRUPT: the flask. A rising edge on the player's drink, while this enemy is between
            // phrases (Chase or Recover), is FromSoft's heal punish -- chance-based, per-enemy cooldown,
            // never for a sentry, never from inside a wind-up (one attack at a time, the tell stays true).
            bool drinkEdge = PlayerDrinkEdge();
            bool spellEdge = PlayerSpellEdge();
            bool airEdge = PlayerAirSpentEdge(dt);
            if (Current == State.Chase || Current == State.Recover)
            {
                if (drinkEdge) TryPunish(dist, data.flaskPunishChance);
                else if (spellEdge) TryPunish(dist, data.spellPunishChance);
                else if (airEdge) TryPunish(dist, data.airPunishChance);
            }

            switch (Current)
            {
                case State.Idle:
                    if (!aggroLocked && dist <= WakeRange && HasLineOfSight()) SetState(State.Chase);
                    break;

                case State.Chase:
                    // A SENTRY (EnemyData.rangedOnly, 2026-09-06) never closes and never commits. It holds
                    // its perch, tracks the player at full turn rate the whole time they are in range, and
                    // leaves the shooting to ProjectileShooter. Losing the line does not put it to sleep:
                    // a runner ducking behind a pillar is met by the next bolt the moment they clear it.
                    if (data.rangedOnly)
                    {
                        if (locomotion != null) locomotion.Stop();
                        FaceTarget(toP, dt);
                        break;
                    }
                    // Only close in for real if an attack slot is free. Otherwise hold at a ready
                    // distance and circle, so the player faces a queue of threats instead of a scrum.
                    bool mayCommit = MayCommitToAttack();
                    if (locomotion != null)
                    {
                        // Walk toward the player but STOP at preferredRange (the configured standoff).
                        // The enemy holds there instead of closing to contact — that gap is the room the
                        // player needs to see a wind-up start.
                        locomotion.MoveTo(mayCommit ? player.position : ReadyPosition(toP, dist));
                    }
                    // A1: always face something real. In range, the player; out on the path, the direction of
                    // travel (a body used to slide its path with whatever facing it last had, and the far-band
                    // 25 deg gate below failed on every approach).
                    {
                        Vector3 pathVel = locomotion != null ? locomotion.Velocity : Vector3.zero;
                        pathVel.y = 0f;
                        FaceTarget(dist <= data.preferredRange * 1.6f || pathVel.sqrMagnitude <= 0.04f ? toP : pathVel, dt);
                    }

                    // Hold the ring: once at preferred range, circle rather than jostling inward.
                    if (mayCommit && Mathf.Abs(dist - data.preferredRange) <= data.repositionDeadzone)
                        Circle(dt);

                    bool attempted = false;
                    if (mayCommit && dist <= data.preferredRange + data.commitTolerance
                        && Time.time >= nextAttackTime
                        && Vector3.Angle(transform.forward, toP) <= 50f)
                    {
                        attempted = true;
                        TryCommit(dist, toP, dt);
                    }
                    // FAR-BAND COMMIT (2026-09-04, from play: "he needs to use his charge when you get
                    // too far"). The gate above only ever attacks inside the commit band, so a moveset
                    // entry authored for 5-18 m -- a shoulder charge, a leaping slam -- could never fire:
                    // the enemy walked in to 3.6 m and threw a sweep. Now, outside the band, an enemy
                    // attacks IF AND ONLY IF the moveset has an entry whose range band contains this
                    // distance (EnemyMoveset.HasEligible; the selector's fallback-to-anything is not
                    // consulted, so nothing without a closer is thrown from range). The chosen combo's
                    // own lungeDistance does the closing, from the cue like every other lunge, and the
                    // facing gate is tighter than the near one because a lunge is aimed where the body
                    // faces at the cue and a 50 deg miss from 8 m is a body flying past the player.
                    else if (mayCommit && dist > data.preferredRange + data.commitTolerance
                             && dist <= data.aggroRange
                             && Time.time >= nextAttackTime
                             && data.moveset != null
                             && data.moveset.HasEligible(SelectDistance(dist, RadialSpeed(PlayerVelocity(), toP), SelectHorizon))
                             && Vector3.Angle(transform.forward, toP) <= 25f)
                    {
                        attempted = true;
                        TryCommit(dist, toP, dt);
                    }
                    if (!attempted) refusedSince = -1f;
                    break;

                case State.Windup:
                    // Heavily reduced turn rate: the swing is aimed where it was committed, so strafing
                    // around an enemy mid-wind-up genuinely works instead of being tracked perfectly.
                    // From the cue on, no turning at all: the commit snap in FireCue is the last aim.
                    if (!cued)
                    {
                        FaceTarget(toP, dt, data.windupTurnMultiplier);
                        // A2: stalk in until the cue, so a step back does not turn the swing into air.
                        if (attack != null && attack.range > 0f && !data.rangedOnly && locomotion != null && locomotion.IsReady
                            && dist > StalkTarget(data.lungeMinDistance, attack.range, attack.lungeDistance))
                            locomotion.Nudge(toP.normalized * StalkSpeed(data.moveSpeed * speedMult, data.stepSpeedMultiplier) * dt);
                    }
                    if (!cued && Time.time >= cueTime) FireCue();
                    if (!lungeCommitted && Time.time >= lungeStartTime) CommitLunge();
                    ApplyLunge(dist, dt);
                    if (Time.time >= stateEnd) BeginStrike();
                    break;

                case State.Strike:
                    // impactDelay can be longer than cueLead, in which case the cue lands after the wind-up ends
                    if (!cued && Time.time >= cueTime) FireCue();
                    if (!lungeCommitted && Time.time >= lungeStartTime) CommitLunge();
                    ApplyLunge(dist, dt);
                    if (!struck && Time.time >= impactTime) { struck = true; DoImpact(toP, dist); }
                    if (Time.time >= stateEnd) NextHitOrRecover();
                    break;

                case State.Recover:
                    // The breath between phrases. The enemy keeps you in view and returns to its fighting
                    // distance — stepping IN if you fled, backing OFF if it ended up on top of you, and
                    // circling once it is happy. Recovering by hugging the player is what made every
                    // wind-up unreadable.
                    FaceTarget(toP, dt, 0.6f);
                    if (RepositionsDuringRecover(data.rangedOnly)) Reposition(toP, dist, dt);
                    else if (locomotion != null) locomotion.Stop();
                    if (Time.time < recoilStepEnd && !data.rangedOnly && locomotion != null && locomotion.IsReady)
                        locomotion.Nudge(-transform.forward * recoilStepSpeed * dt);
                    if (Time.time >= stateEnd)
                    {
                        if (resumeComboAfterRecover && CanResumeCombo(dist)) ResumeCombo();
                        else
                        {
                            resumeComboAfterRecover = false;
                            // Respect aggroLocked on the way out of Recover. Being parried or damaged
                            // routes through here, so without this check any locked enemy — including
                            // the boss before its arena trigger fires — silently wakes up and attacks.
                            SetState(aggroLocked ? State.Idle : State.Chase);
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// A ranged-only enemy is a fixed route tool in every non-dead state. Letting the generic melee
        /// recovery step run after a reflected bolt walked sentries off their authored perches.
        /// </summary>
        public static bool RepositionsDuringRecover(bool rangedOnly)
        {
            return !rangedOnly;
        }

        /// <summary>
        /// Reacquires the player when scene order, respawn, or an in-play domain reload invalidates the
        /// non-serialized target references. Without this retry an otherwise healthy enemy can remain in
        /// Idle forever because Start is not guaranteed to run again after those transitions.
        /// </summary>
        bool EnsurePlayerTarget()
        {
            if (player != null && playerCombat != null && playerCombat.transform == player) return true;

            var pc = FindAnyObjectByType<PlayerCombat>();
            if (pc == null)
            {
                player = null;
                playerCombat = null;
                playerFlask = null;
                playerWasDrinking = false;
                return false;
            }

            bool changed = pc != playerCombat;
            player = pc.transform;
            playerCombat = pc;
            if (changed)
            {
                playerFlask = null;
                playerWasDrinking = false;
            }
            return true;
        }

        bool PlayerDrinkEdge()
        {
            if (playerFlask == null && player != null) playerFlask = player.GetComponent<FlaskAbility>();
            bool drinking = playerFlask != null && playerFlask.IsDrinking;
            bool edge = drinking && !playerWasDrinking;
            playerWasDrinking = drinking;
            return edge;
        }

        /// <summary>A spell cast is the wand cooldown jumping up this frame.</summary>
        bool PlayerSpellEdge()
        {
            if (playerWand == null && player != null) playerWand = player.GetComponent<WandController>();
            if (playerWand == null) return false;
            float cd = playerWand.CooldownRemaining;
            bool edge = cd > lastWandCooldown + 0.05f;
            lastWandCooldown = cd;
            return edge;
        }

        /// <summary>Fires once when the player has been airborne with the air dash spent for AirPunishDelay.</summary>
        bool PlayerAirSpentEdge(float dt)
        {
            if (playerMotor == null && player != null) playerMotor = player.GetComponent<FirstPersonMotor>();
            if (playerMotor == null) return false;
            if (playerMotor.IsGrounded || !playerMotor.AirDashUsed) { airSpentSeconds = 0f; return false; }
            float before = airSpentSeconds;
            airSpentSeconds += dt;
            return before < AirPunishDelay && airSpentSeconds >= AirPunishDelay;
        }

        void TryPunish(float dist, float chance)
        {
            if (data == null || chance <= 0f || data.rangedOnly || aggroLocked) return;
            if (Time.time < nextFlaskPunishAt || dist > data.aggroRange) return;
            if (!MayCommitToAttack()) return;
            bool inBand = dist <= data.preferredRange + data.commitTolerance * 2f
                       || (data.moveset != null && data.moveset.HasEligible(dist));
            if (!inBand) return;
            if (Random.value > chance) return;
            PunishFlaskNow(dist, true);
        }

        /// <summary>
        /// Abort the breath and attack NOW: the flask punish, forced. Public so the harness and
        /// FeatureTests can prove the path without rolling dice. Refused while dead, executed, staggered
        /// or already committed; otherwise the recovery is cut and a combo begins this frame, with the
        /// cue still cueLead before impact like every other attack.
        /// </summary>
        public bool PunishFlaskNow(float dist, bool requireLandable = false)
        {
            if (Current == State.Dead || Current == State.Executed || Current == State.Staggered || IsCommitted) return false;
            float radial = 0f;
            if (requireLandable && player != null)
            {
                Vector3 toP = player.position - transform.position; toP.y = 0f;
                radial = RadialSpeed(PlayerVelocity(), toP);
            }
            lastPickIndex = -1;
            var c = ChooseCombo(SelectDistance(dist, radial, SelectHorizon));
            if (c == null || c.hits == null || c.hits.Length == 0) return false;
            // The dice path uses the same arrival law as every commit; the harness path stays unconditional.
            if (requireLandable && !FirstHitCanLand(c.hits[0], dist, radial)) { UndoLastPick(); return false; }
            FlaskPunishes++;
            nextFlaskPunishAt = Time.time + FlaskPunishCooldown;
            AudioManager.Play(Sfx.Tick, 0.7f, 0.8f);
            BeginCombo(c);
            return true;
        }

        /// <summary>How far away the player wakes this enemy: melee aggro, or the bolt band for a shooter.</summary>
        float WakeRange => data.shootsProjectiles ? Mathf.Max(data.aggroRange, data.projectileMaxRange) : data.aggroRange;

        /// <summary>
        /// Any of three lines (head, chest, feet) clear counts as sight. One line to the chest missed a
        /// player whose chest was behind a rail while their head and legs were in plain view, which is
        /// exactly the shape a runner on a span presents to a perch above it.
        /// </summary>
        bool HasLineOfSight()
        {
            return HasLineOfSight(transform.position + Vector3.up * 1.2f, player.position);
        }

        public static bool HasLineOfSight(Vector3 eye, Vector3 playerFeet)
        {
            int mask = ~(Layers.EnemyMask | Layers.PlayerMask | (1 << Layers.Interactable));
            for (int i = 0; i < 3; i++)
            {
                Vector3 to = playerFeet + Vector3.up * (i == 0 ? 0.8f : i == 1 ? 1.5f : 0.2f);
                if (!Physics.Linecast(eye, to, mask, QueryTriggerInteraction.Ignore)) return true;
            }
            return false;
        }

        /// <summary>
        /// Turn toward the player, eased as it converges so the final degrees settle instead of snapping.
        /// A linear RotateTowards is what makes an aggressive enemy look like it is tracking on rails.
        /// </summary>
        void FaceTarget(Vector3 dir, float dt, float rateMultiplier = 1f)
        {
            if (dir.sqrMagnitude < 0.001f || locomotion == null) return;
            Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up);
            float angle = Quaternion.Angle(transform.rotation, target);
            // Full rate on a big correction, tapering to ~25% as it lines up. The BRAIN owns this curve;
            // locomotion just applies the capped rate, so a root-motion rig obeys the same feel rule.
            float ease = Mathf.Clamp01(0.25f + angle / 45f);
            locomotion.FaceTowards(dir, data.turnSpeed * rateMultiplier * ease);
        }

        /// <summary>
        /// Return to the fighting ring. Steps IN when the player has backed away, backs OFF when the enemy
        /// has ended up inside its own preferred range, and strafes when it is already happy.
        ///
        /// Backing off is the half that was missing: previously the only correction was forward, so every
        /// exchange collapsed to contact distance and the telegraphs became impossible to read.
        ///
        /// Damped on purpose — the old version applied full speed on the first frame and cut to zero the
        /// moment it was in range, which read as a twitch.
        /// </summary>
        void Reposition(Vector3 toP, float dist, float dt)
        {
            if (locomotion == null || !locomotion.IsReady) return;
            if (toP.sqrMagnitude < 0.0001f) return;

            float error = dist - data.preferredRange;

            if (Mathf.Abs(error) <= data.repositionDeadzone)
            {
                // Settled at range: hold the distance and drift around, so the pressure stays visible
                // without turning into a shove.
                stepSpeed = Mathf.MoveTowards(stepSpeed, 0f, data.stepAcceleration * dt);
                Circle(dt);
                return;
            }

            bool closing = error > 0f;
            float top = data.moveSpeed * speedMult
                      * (closing ? data.stepSpeedMultiplier * Mathf.Lerp(0.5f, 1f, Aggression)
                                 : data.backStepSpeedMultiplier);
            // Never overshoot the ring in a single frame.
            float capped = Mathf.Min(top, Mathf.Abs(error) / Mathf.Max(dt, 0.0001f));
            stepSpeed = Mathf.MoveTowards(stepSpeed, capped, data.stepAcceleration * dt);
            if (stepSpeed <= 0.01f) return;

            Vector3 dir = toP.normalized * (closing ? 1f : -1f);
            locomotion.Nudge(dir * stepSpeed * dt);
        }

        /// <summary>Sidestep around the player at the current distance. Keeps a held enemy alive-looking.</summary>
        void Circle(float dt)
        {
            if (locomotion == null || player == null) return;
            float speed = data.moveSpeed * speedMult * data.strafeSpeedMultiplier;
            locomotion.Strafe(player.position, circleDir, speed, dt);
        }

        /// <summary>
        /// The committed travel. Runs from the CUE to the impact, so the enemy commits from a distance the
        /// player can read and then closes — rather than standing in your face and swinging.
        ///
        /// Direction is frozen at commit time, so sidestepping a committed attack actually works. It stops
        /// short of the player so a lunge never burrows through them.
        /// </summary>
        void ApplyLunge(float dist, float dt)
        {
            if (lungeSpeed <= 0.001f || Time.time >= lungeEndTime) return;
            if (locomotion == null || !locomotion.IsReady) return;
            if (dist <= data.lungeMinDistance) return;
            locomotion.Nudge(lungeDir * lungeSpeed * dt);
        }

        /// <summary>
        /// Where to wait when another enemy holds the attack slot: out at ready distance, drifting around
        /// the player rather than crowding them. Keeps the pressure visible without stacking hitboxes.
        /// </summary>
        Vector3 ReadyPosition(Vector3 toP, float dist)
        {
            float ready = data.preferredRange * data.readyDistanceMultiplier;
            Vector3 away = dist > 0.01f ? -toP.normalized : transform.forward;
            Vector3 tangent = Vector3.Cross(Vector3.up, away) * circleDir;
            Vector3 offset = (away + tangent * 0.85f).normalized * ready;
            return player.position + offset;
        }

        bool CanResumeCombo(float dist)
        {
            return combo != null && combo.hits != null
                && comboIndex + 1 < combo.hits.Length
                && dist <= data.preferredRange + data.commitTolerance * 2f;
        }

        /// <summary>Continue an interrupted combo. The gap tightens per deflect — a real Sekiro exchange.</summary>
        void ResumeCombo()
        {
            resumeComboAfterRecover = false;
            comboIndex++;
            BeginWindup(combo.hits[comboIndex], NextGap(attack));
        }

        /// <summary>
        /// Gap before the next hit of a combo. Shrinks with aggression and with each deflect landed,
        /// but the wind-up itself is untouched, so the cue still fires cueLead before impact.
        /// </summary>
        float NextGap(EnemyAttackData from)
        {
            float baseGap = from != null ? from.comboGap : 0.2f;
            float gap = baseGap * Mathf.Lerp(1f, 0.45f, Aggression) - parryStreak * 0.03f;
            // Floored well above zero: hits inside a combo still need to read as separate beats, not as
            // one continuous blur. The cue keeps them parryable either way — this is purely legibility.
            return Mathf.Max(0.1f, gap);
        }

        /// <summary>
        /// Picks the next combo. Routed through EnemyData.SelectCombo so a moveset's authored WEIGHTS and
        /// range gating actually apply — a uniform pick made the signature bait as common as the filler,
        /// and let a long-reach opener fire from point blank.
        /// </summary>
        protected virtual AttackCombo ChooseCombo(float distanceToTarget)
        {
            if (data == null) return null;
            // History-aware when a moveset is authored: per-entry cooldowns keep a signature from coming
            // twice running (EnemyMoveset.SelectIndex). The clock is this instance's, never the asset's.
            var ms = ActiveMoveset;
            if (ms != null && ms.entries != null && ms.entries.Length > 0)
            {
                if (moveLastUsedAt == null || moveLastUsedAt.Length != ms.entries.Length)
                {
                    moveLastUsedAt = new float[ms.entries.Length];
                    for (int i = 0; i < moveLastUsedAt.Length; i++) moveLastUsedAt[i] = -1e9f;
                }
                float edgeRoom = player != null ? SolarArenaPortal.EdgeRoom(player.position) : float.MaxValue;
                int idx = ms.SelectIndex(distanceToTarget, moveLastUsedAt, Time.time, edgeRoom);
                if (idx >= 0)
                {
                    lastPickIndex = idx;
                    lastPickPrevTime = moveLastUsedAt[idx];
                    lastPickPrevMove = LastMoveIndex;
                    moveLastUsedAt[idx] = Time.time;
                    LastMoveIndex = idx;
                    return ms.entries[idx].combo;
                }
            }
            var picked = data.SelectCombo(distanceToTarget);
            if (picked != null) return picked;
            // No moveset authored: fall back to the flat list.
            if (data.combos == null || data.combos.Length == 0) return null;
            return data.combos[Random.Range(0, data.combos.Length)];
        }

        void BeginCombo(AttackCombo c)
        {
            combo = c;
            comboIndex = 0;
            parryStreak = 0;
            chainedPhrases = 0;
            resumeComboAfterRecover = false;
            BeginWindup(c.hits[0], 0f);
        }

        bool TryChainPhrase()
        {
            if (data == null || data.rangedOnly || aggroLocked || player == null || attack == null) return false;
            if (chainedPhrases >= MaxChainedPhrases) return false;
            Vector3 toP = player.position - transform.position; toP.y = 0f;
            float dist = toP.magnitude;
            if (Vector3.Angle(transform.forward, toP) > 50f) return false;
            if (!ShouldChainPhrase(Aggression, attack.recovery, dist, data.preferredRange + data.commitTolerance, Random.value))
                return false;
            if (!MayCommitToAttack()) return false;
            float radial = RadialSpeed(PlayerVelocity(), toP);
            lastPickIndex = -1;
            var c = ChooseCombo(SelectDistance(dist, radial, SelectHorizon));
            if (c == null || c.hits == null || c.hits.Length == 0) return false;
            if (!FirstHitCanLand(c.hits[0], dist, radial)) { UndoLastPick(); return false; }
            var from = attack;
            chainedPhrases++;
            combo = c;
            comboIndex = 0;
            parryStreak = 0;
            resumeComboAfterRecover = false;
            BeginWindup(c.hits[0], NextGap(from));
            return true;
        }

        void BeginWindup(EnemyAttackData atk, float gap)
        {
            attack = atk;
            SetState(State.Windup);
            float windup = atk.windup * windupMult + gap;
            stateEnd = Time.time + windup;

            // The hit lands at (wind-up end + impactDelay); the cue must precede it by cueLead.
            projectedImpact = stateEnd + atk.impactDelay;
            cueTime = projectedImpact - cueLead;
            cued = false;
            lungeCommitted = false;
            lungeStartTime = projectedImpact - LungeWindow(atk.lungeDistance, cueLead);

            if (visuals != null) visuals.Telegraph(atk, windup);
            // Tick is now only "an attack is starting" — the cue is what says "press now", so keep this quiet.
            AudioManager.Play(Sfx.Tick, atk.unblockable ? 0.6f : 0.4f, atk.unblockable ? 0.7f : 1f);

            // Wind-up shorter than the cue lead: there is no room to telegraph, so cue immediately.
            if (cueTime <= Time.time) FireCue();
            if (lungeStartTime <= Time.time) CommitLunge();
        }

        /// <summary>Beat 2 of the telegraph: the hard visual snap + audio ping that means "parry NOW".</summary>
        void FireCue()
        {
            cued = true;
            if (attack == null) return;

            // A6: the commit snap. At most CommitSnapDeg toward where the player will be at impact, then frozen.
            if (player != null && locomotion != null && attack.range > 0f)
            {
                Vector3 aim = SnapYaw(transform.forward, CommitAim(player.position, PlayerVelocity(), transform.position,
                                                                   LeadCapLateral, projectedImpact - Time.time), CommitSnapDeg);
                locomotion.FaceTowards(aim, CommitSnapDeg / Mathf.Max(Time.deltaTime, 0.0001f));
            }
            // Snap -> lunge -> flash, so a lane-shaped tell freezes on the committed direction.
            if (!lungeCommitted && Time.time >= lungeStartTime) CommitLunge();

            if (visuals != null) visuals.CueFlash(attack.unblockable);
            AudioManager.Play(Sfx.ParryCue, attack.unblockable ? 1f : 0.9f, attack.unblockable ? 0.75f : 1f, 0.02f);
        }

        /// <summary>
        /// Commit the travel. Aimed where the enemy faces at THIS instant and held there, so a player who
        /// sidesteps after it is genuinely missed. Sized to arrive exactly at impact. At the cue for a short
        /// lunge; up to LungeWindow before impact for a long charge, which is still a commit you answer by moving.
        /// </summary>
        void CommitLunge()
        {
            lungeCommitted = true;
            if (attack == null || attack.lungeDistance <= 0.01f) return;
            float travel = Mathf.Max(0.05f, projectedImpact - Time.time);
            lungeSpeed = attack.lungeDistance / travel;
            lungeEndTime = projectedImpact;
            // A6: aimed at the led player, never more than the snap off the body's facing.
            lungeDir = player != null
                ? SnapYaw(transform.forward, CommitAim(player.position, PlayerVelocity(), transform.position, LeadCapLateral, travel), CommitSnapDeg)
                : transform.forward;
            lungeDir.y = 0f;
            lungeDir = lungeDir.sqrMagnitude > 0.0001f ? lungeDir.normalized : transform.forward;
        }

        void BeginStrike()
        {
            SetState(State.Strike);
            struck = false;
            impactTime = Time.time + attack.impactDelay;
            projectedImpact = impactTime;
            stateEnd = impactTime + attack.strikeDuration;
            if (visuals != null) visuals.Strike(attack.lungeDistance, attack.strikeDuration + attack.impactDelay);
            AudioManager.Play(Sfx.Swing, 0.6f, 0.8f);
        }

        void DoImpact(Vector3 toP, float dist)
        {
            if (playerCombat == null) return;
            if (dist > attack.range + 0.5f) return;
            if (Vector3.Angle(transform.forward, toP) > attack.coneDeg * 0.5f) return;
            playerCombat.ReceiveAttack(new AttackInfo
            {
                attack = attack,
                attacker = this,
                damage = attack.damage,
                unblockable = attack.unblockable
            });
        }

        void NextHitOrRecover()
        {
            comboIndex++;
            if (combo != null && combo.hits != null && comboIndex < combo.hits.Length)
            {
                BeginWindup(combo.hits[comboIndex], NextGap(attack));
                return;
            }
            if (TryChainPhrase()) return;
            chainedPhrases = 0;
            combo = null;
            parryStreak = 0;
            if (visuals != null) visuals.ClearTelegraph();

            // Aggression compresses the dead time between combos without touching any wind-up — but it
            // may NOT compress it to nothing. A combo has to land as a phrase with an audible breath after
            // it; a continuous stream of swings is both exhausting and impossible to read as rhythm.
            float recover = Mathf.Max(attack.recovery * Mathf.Lerp(1f, 0.35f, Aggression), data.comboBreathSeconds);
            SetState(State.Recover, recover);
            nextAttackTime = stateEnd + data.attackCooldown * Mathf.Lerp(1f, 0.3f, Aggression);

            // Show the breath: the enemy visibly resettles, which is the player's window to act.
            if (visuals != null) visuals.Settle(recover);
        }

        protected void SetState(State s, float duration = 0f)
        {
            Current = s;
            if (duration > 0f) stateEnd = Time.time + duration;
            // Nothing is pending unless a wind-up/strike sets it again; keeps the arbitration and the
            // player's cue lookahead from seeing stale impact times.
            if (s != State.Windup && s != State.Strike) projectedImpact = float.MaxValue;
            // The step-in ramps from rest each time, so leaving Recover cannot carry momentum into a lunge.
            if (s != State.Recover) stepSpeed = 0f;
            // A lunge belongs to one committed swing; never let it bleed into the next state.
            if (s != State.Windup && s != State.Strike) { lungeSpeed = 0f; lungeEndTime = 0f; }
            if (locomotion != null && s != State.Chase) locomotion.Stop();
        }

        // ---- reactions -------------------------------------------------------------------

        /// <summary>Called by PlayerCombat on a perfect parry. Posture damage applied last so a break overrides the recoil state.</summary>
        public virtual void OnParried(float postureDamage)
        {
            if (Current == State.Dead || Current == State.Executed) return;

            // A deflect no longer automatically ends the exchange. An aggressive enemy recoils briefly
            // and then presses on with the rest of its combo, each follow-up arriving a little sooner.
            // The wind-ups are unchanged, so every one of those hits is still cued and parryable.
            bool pressOn = Aggression >= 0.5f
                        && combo != null && combo.hits != null
                        && comboIndex + 1 < combo.hits.Length;

            if (pressOn) parryStreak++;
            else { combo = null; parryStreak = 0; }
            resumeComboAfterRecover = pressOn;

            if (visuals != null) { visuals.ClearTelegraph(); visuals.Recoil(); }
            if (Current != State.Staggered)
            {
                float recoil = data.parryRecoilSeconds * Mathf.Lerp(1f, 0.55f, Aggression);
                SetState(State.Recover, recoil);
                recoilStepSpeed = ParryRecoilMetres / Mathf.Max(0.05f, recoil);
                recoilStepEnd = stateEnd;
                nextAttackTime = stateEnd + (pressOn ? 0f : data.attackCooldown * Mathf.Lerp(1f, 0.3f, Aggression));
            }
            if (postureDamage > 0f) Posture.Add(postureDamage);
        }

        public virtual void OnBlocked()
        {
            if (visuals != null) visuals.HitFlash();
        }

        protected virtual void HandleDamaged(DamageInfo d)
        {
            if (Current == State.Dead) return;
            if (data != null && Health != null && PhaseShiftDue(Health.Ratio, data.phase2Threshold, phase2)) EnterPhase2();
            if (visuals != null) visuals.HitFlash();
            if (Current == State.Idle && !aggroLocked) SetState(State.Chase);
        }

        /// <summary>
        /// Phase 2: a roar beat, then heavier signatures on shorter cooldowns and a step more aggression. A
        /// committed swing is never interrupted (its cue stays true); the beat only replaces a breath.
        /// </summary>
        void EnterPhase2()
        {
            phase2 = true;
            moveLastUsedAt = null;   // every signature is available the moment the phase turns
            if (visuals != null) visuals.Roar();
            if (CameraShake.I) CameraShake.I.Medium();
            if (Current == State.Chase || Current == State.Recover)
            {
                combo = null;
                parryStreak = 0;
                resumeComboAfterRecover = false;
                SetState(State.Recover, Phase2RoarSeconds);
                nextAttackTime = stateEnd;
            }
        }

        protected virtual void HandleBroken()
        {
            if (Current == State.Dead || Current == State.Executed) return;
            combo = null;
            parryStreak = 0;
            resumeComboAfterRecover = false;
            SetState(State.Staggered);
            if (visuals != null) { visuals.ClearTelegraph(); visuals.Slump(true); }
            AudioManager.Play(Sfx.Stagger);
            // P4: a sentry breaking is a sting on top of the stagger -- the one sound on a span that says
            // "look up, it is open", since the body itself is 25 m away and does not glow.
            if (data != null && data.rangedOnly) AudioManager.Play(Sfx.PostureBreak, 0.7f, 1.35f, 0.03f);
        }

        protected virtual void HandleStaggerEnded()
        {
            if (Current != State.Staggered) return;
            if (visuals != null) visuals.Slump(false);
            SetState(State.Recover, 0.4f);
            nextAttackTime = stateEnd;
        }

        protected virtual void HandleDeath()
        {
            Die();
        }

        protected void Die()
        {
            if (Current == State.Dead) return;
            // Captured before the state change: a body that dies mid-deathblow gets the grander burst.
            bool executed = Current == State.Executed;
            DiedExecuted = executed;
            SetState(State.Dead);
            combo = null;
            if (locomotion != null) locomotion.Disable();
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            if (SoulsWallet.I != null && data != null) SoulsWallet.I.Add(data.soulValue);
            GameEvents.RaiseEnemyKilled(this);
            if (visuals != null) visuals.Die();

            // Dark Souls homage: nothing leaves a corpse. Every enemy — grunt, heavy, boss segment,
            // deathblow or not — comes apart into rising mist. DeathMist is pooled, so this costs a
            // handful of writes into an already-built ParticleSystem and allocates nothing.
            float s = data != null ? Mathf.Max(0.25f, data.scale) : 1f;
            DeathMist.Burst(transform.position + Vector3.up * (0.95f * s),
                            s,
                            data != null ? data.emission : Color.white,
                            executed);
            StartCoroutine(DissolveCo(executed ? 0.55f : 0.45f));

            AudioManager.Play(Sfx.Souls, 0.8f);
            Destroy(gameObject, 1.5f);
        }

        /// <summary>
        /// The body loses its shape as the mist takes it: it sinks, spreads, and collapses.
        ///
        /// <para>Transform-only, and only on the ROOT. EnemyVisuals owns this enemy's materials and its
        /// own Die() presentation on the visual child; two writers on one channel is a bug this project
        /// has already paid for once, so the fade is left to it and the silhouette work happens here.</para>
        ///
        /// <para>Safe to run on a dying object: Update() early-returns on State.Dead, locomotion is
        /// disabled and the colliders are off by the time this starts, so nothing else is writing the
        /// transform. Unscaled, so a deathblow's hitstop does not stall the dissolve halfway.</para>
        /// </summary>
        IEnumerator DissolveCo(float seconds)
        {
            Vector3 p0 = transform.position;
            Vector3 s0 = transform.localScale;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / seconds);
                float e = k * k;   // hangs for a beat, then goes all at once

                transform.position = p0 + Vector3.down * (0.5f * e);
                // Non-uniform: it settles outward while it collapses, like ash losing its shape rather
                // than a model being scaled out.
                float spread = Mathf.Lerp(1f, 1.2f, k) * Mathf.Lerp(1f, 0.15f, e);
                transform.localScale = new Vector3(s0.x * spread, s0.y * Mathf.Lerp(1f, 0.08f, e), s0.z * spread);
                yield return null;
            }
            transform.localScale = s0 * 0.02f;
        }

        EnemyVisuals markVisuals;

        /// <summary>
        /// The single point every part of the deathblow beat aims at: the deathblow glyph on this
        /// enemy's sternum, pushed off the body surface toward <paramref name="eye"/> so it is never
        /// inside the mesh. The commit burst, the wand's blast and the marker itself all read this, so
        /// the shatter, the bolt and the explosion land on the same pixels.
        /// </summary>
        public Vector3 DeathblowPoint(Vector3 eye)
        {
            if (markVisuals == null) markVisuals = GetComponentInChildren<EnemyVisuals>(true);
            if (markVisuals != null) return markVisuals.DeathblowPoint(eye);
            float s = Mathf.Max(0.01f, transform.localScale.x);
            return transform.position + Vector3.up * (1.45f * s);
        }

        public void BeginExecuted(Transform executor)
        {
            if (Current == State.Dead) return;
            SetState(State.Executed);
            combo = null;
            // The window is spent the instant the blow is committed. A glyph still hanging over a body
            // that is already being killed invites a second press that can never land.
            if (visuals != null) visuals.SetDeathblowReady(false);
            Posture.HoldStagger(3f);
            Vector3 dir = executor.position - transform.position; dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        public void EndExecuted()
        {
            if (Current != State.Executed || Health.IsDead) return;
            Posture.EndStagger();
            if (visuals != null) visuals.Slump(false);
            SetState(State.Recover, 0.6f);
            nextAttackTime = stateEnd;
        }
    }
}
