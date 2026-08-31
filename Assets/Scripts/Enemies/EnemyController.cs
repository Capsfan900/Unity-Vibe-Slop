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
        public bool IsStaggered => Current == State.Staggered;
        public bool IsAlive => Current != State.Dead;

        protected IEnemyLocomotion locomotion;
        protected IEnemyPresentation visuals;
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
        /// <summary>Consecutive deflects inside the current exchange; tightens each following gap.</summary>
        int parryStreak;

        /// <summary>0 = stock passive behaviour, 1 = maximum pressure. Never affects telegraph readability.</summary>
        protected float Aggression => data != null ? Mathf.Clamp01(data.aggression) : 0f;

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

        // ---- global attack arbitration -----------------------------------------------------------
        // The player's parry facing cone is 75 degrees, so two enemies attacking from opposite sides are
        // not simultaneously parryable no matter how good the player is. Rather than widen the cone (which
        // would cheapen every deflect) we queue the attackers: one commits, the rest hold at a ready
        // distance and circle. Pressure stays high, but it stays ANSWERABLE.

        static readonly List<EnemyController> activeEnemies = new List<EnemyController>();
        public static IReadOnlyList<EnemyController> ActiveEnemies => activeEnemies;

        /// <summary>How many enemies may be mid-attack at once. 1 reads best; 2 is chaotic but survivable.</summary>
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
            return false;
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
            return best;
        }

        /// <summary>
        /// Self-healing arbitration: we count who is currently mid-swing rather than handing out tokens that
        /// have to be returned. An enemy that dies, staggers or is executed drops out of the count for free.
        /// </summary>
        bool MayCommitToAttack()
        {
            int committed = 0;
            for (int i = 0; i < activeEnemies.Count; i++)
            {
                var e = activeEnemies[i];
                if (e == null || e == this || !e.IsAlive) continue;
                if (e.IsCommitted) committed++;
            }
            return committed < Mathf.Max(1, MaxSimultaneousAttackers);
        }

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
            var pc = FindAnyObjectByType<PlayerCombat>();
            if (pc != null) { player = pc.transform; playerCombat = pc; }
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
            Posture.Configure(data.maxPosture, data.postureRegen, data.postureRegenDelay, data.staggerSeconds);
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
            if (Current == State.Dead || Current == State.Executed || player == null || data == null) return;
            if (Time.timeScale <= 0f) return;

            if (visuals != null) visuals.SetPostureRatio(Posture.Ratio);

            Vector3 toP = player.position - transform.position; toP.y = 0f;
            float dist = toP.magnitude;
            float dt = Time.deltaTime;

            switch (Current)
            {
                case State.Idle:
                    if (!aggroLocked && dist <= data.aggroRange && HasLineOfSight()) SetState(State.Chase);
                    break;

                case State.Chase:
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
                    if (dist <= data.preferredRange * 1.6f) FaceTarget(toP, dt);

                    // Hold the ring: once at preferred range, circle rather than jostling inward.
                    if (mayCommit && Mathf.Abs(dist - data.preferredRange) <= data.repositionDeadzone)
                        Circle(dt);

                    if (mayCommit && dist <= data.preferredRange + data.commitTolerance
                        && Time.time >= nextAttackTime
                        && Vector3.Angle(transform.forward, toP) <= 50f)
                    {
                        var c = ChooseCombo(dist);
                        if (c != null && c.hits != null && c.hits.Length > 0) BeginCombo(c);
                    }
                    break;

                case State.Windup:
                    // Heavily reduced turn rate: the swing is aimed where it was committed, so strafing
                    // around an enemy mid-wind-up genuinely works instead of being tracked perfectly.
                    FaceTarget(toP, dt, data.windupTurnMultiplier);
                    if (!cued && Time.time >= cueTime) FireCue();
                    ApplyLunge(dist, dt);
                    if (Time.time >= stateEnd) BeginStrike();
                    break;

                case State.Strike:
                    // impactDelay can be longer than cueLead, in which case the cue lands after the wind-up ends
                    if (!cued && Time.time >= cueTime) FireCue();
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
                    Reposition(toP, dist, dt);
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

        bool HasLineOfSight()
        {
            Vector3 from = transform.position + Vector3.up * 1.2f;
            Vector3 to = player.position + Vector3.up * 0.8f;
            int mask = ~(Layers.EnemyMask | Layers.PlayerMask | (1 << Layers.Interactable));
            return !Physics.Linecast(from, to, mask, QueryTriggerInteraction.Ignore);
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
            resumeComboAfterRecover = false;
            BeginWindup(c.hits[0], 0f);
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

            if (visuals != null) visuals.Telegraph(atk, windup);
            // Tick is now only "an attack is starting" — the cue is what says "press now", so keep this quiet.
            AudioManager.Play(Sfx.Tick, atk.unblockable ? 0.6f : 0.4f, atk.unblockable ? 0.7f : 1f);

            // Wind-up shorter than the cue lead: there is no room to telegraph, so cue immediately.
            if (cueTime <= Time.time) FireCue();
        }

        /// <summary>Beat 2 of the telegraph: the hard visual snap + audio ping that means "parry NOW".</summary>
        void FireCue()
        {
            cued = true;
            if (attack == null) return;

            // Commit the travel now. Aimed where the enemy is facing at THIS instant and held there, so a
            // player who sidesteps after the cue is genuinely missed. Sized to arrive exactly at impact.
            if (attack.lungeDistance > 0.01f)
            {
                float travel = Mathf.Max(0.05f, projectedImpact - Time.time);
                lungeSpeed = attack.lungeDistance / travel;
                lungeEndTime = projectedImpact;
                lungeDir = transform.forward;
                lungeDir.y = 0f;
                lungeDir = lungeDir.sqrMagnitude > 0.0001f ? lungeDir.normalized : transform.forward;
            }

            if (visuals != null) visuals.CueFlash(attack.unblockable);
            AudioManager.Play(Sfx.ParryCue, attack.unblockable ? 1f : 0.9f, attack.unblockable ? 0.75f : 1f, 0.02f);
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
            if (visuals != null) visuals.HitFlash();
            if (Current == State.Idle && !aggroLocked) SetState(State.Chase);
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

        public void BeginExecuted(Transform executor)
        {
            if (Current == State.Dead) return;
            SetState(State.Executed);
            combo = null;
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
