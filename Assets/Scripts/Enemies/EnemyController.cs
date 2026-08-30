using UnityEngine;
using UnityEngine.AI;

namespace VibeGame1
{
    /// <summary>
    /// Melee enemy state machine. Attacks are telegraphed (emissive ramp + tick) and resolved by a
    /// distance/cone test at impact time through PlayerCombat.ReceiveAttack.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(Posture), typeof(NavMeshAgent))]
    public class EnemyController : MonoBehaviour
    {
        public enum State { Idle, Chase, Windup, Strike, Recover, Staggered, Executed, Dead }

        public EnemyData data;
        [Tooltip("When true the enemy ignores the player until aggroLocked is cleared (boss arena).")]
        public bool aggroLocked;

        public State Current { get; private set; } = State.Idle;
        public Health Health { get; private set; }
        public Posture Posture { get; private set; }
        public EnemyAttackData CurrentAttack => attack;
        public bool IsStaggered => Current == State.Staggered;
        public bool IsAlive => Current != State.Dead;

        protected NavMeshAgent agent;
        protected EnemyVisuals visuals;
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

        protected virtual void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            Health = GetComponent<Health>();
            Posture = GetComponent<Posture>();
            visuals = GetComponentInChildren<EnemyVisuals>();
            gameObject.layer = Layers.Enemy;
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
            agent.speed = data.moveSpeed * speedMult;
            agent.angularSpeed = data.turnSpeed;
            agent.acceleration = 40f;
            agent.stoppingDistance = Mathf.Max(0.5f, data.attackRange * 0.7f);
            agent.radius = 0.45f * data.scale;
            agent.height = 2f * data.scale;
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
                    if (agent.enabled && agent.isOnNavMesh)
                    {
                        agent.isStopped = false;
                        agent.SetDestination(player.position);
                    }
                    if (dist <= data.attackRange * 1.2f) FaceTarget(toP, dt);
                    if (dist <= data.attackRange && Time.time >= nextAttackTime && Vector3.Angle(transform.forward, toP) <= 50f)
                    {
                        var c = ChooseCombo();
                        if (c != null && c.hits != null && c.hits.Length > 0) BeginCombo(c);
                    }
                    break;

                case State.Windup:
                    FaceTarget(toP, dt);
                    if (Time.time >= stateEnd) BeginStrike();
                    break;

                case State.Strike:
                    if (!struck && Time.time >= impactTime) { struck = true; DoImpact(toP, dist); }
                    if (Time.time >= stateEnd) NextHitOrRecover();
                    break;

                case State.Recover:
                    if (Time.time >= stateEnd) SetState(State.Chase);
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

        void FaceTarget(Vector3 dir, float dt)
        {
            if (dir.sqrMagnitude < 0.001f) return;
            Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, data.turnSpeed * dt);
        }

        protected virtual AttackCombo ChooseCombo()
        {
            if (data.combos == null || data.combos.Length == 0) return null;
            return data.combos[Random.Range(0, data.combos.Length)];
        }

        void BeginCombo(AttackCombo c)
        {
            combo = c;
            comboIndex = 0;
            BeginWindup(c.hits[0], 0f);
        }

        void BeginWindup(EnemyAttackData atk, float gap)
        {
            attack = atk;
            SetState(State.Windup);
            float windup = atk.windup * windupMult + gap;
            stateEnd = Time.time + windup;
            if (visuals != null) visuals.Telegraph(windup, atk.unblockable);
            AudioManager.Play(Sfx.Tick, atk.unblockable ? 1f : 0.7f, atk.unblockable ? 0.7f : 1f);
        }

        void BeginStrike()
        {
            SetState(State.Strike);
            struck = false;
            impactTime = Time.time + attack.impactDelay;
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
                BeginWindup(combo.hits[comboIndex], attack.comboGap);
                return;
            }
            combo = null;
            if (visuals != null) visuals.ClearTelegraph();
            SetState(State.Recover, attack.recovery);
            nextAttackTime = stateEnd + data.attackCooldown;
        }

        protected void SetState(State s, float duration = 0f)
        {
            Current = s;
            if (duration > 0f) stateEnd = Time.time + duration;
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = s != State.Chase;
        }

        // ---- reactions -------------------------------------------------------------------

        /// <summary>Called by PlayerCombat on a perfect parry. Posture damage applied last so a break overrides the recoil state.</summary>
        public virtual void OnParried(float postureDamage)
        {
            if (Current == State.Dead || Current == State.Executed) return;
            combo = null;
            if (visuals != null) { visuals.ClearTelegraph(); visuals.Recoil(); }
            if (Current != State.Staggered)
            {
                SetState(State.Recover, data.parryRecoilSeconds);
                nextAttackTime = stateEnd + data.attackCooldown;
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
            SetState(State.Dead);
            combo = null;
            if (agent != null && agent.enabled) agent.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            if (SoulsWallet.I != null && data != null) SoulsWallet.I.Add(data.soulValue);
            GameEvents.RaiseEnemyKilled(this);
            if (visuals != null) visuals.Die();
            AudioManager.Play(Sfx.Souls, 0.8f);
            Destroy(gameObject, 1.5f);
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
