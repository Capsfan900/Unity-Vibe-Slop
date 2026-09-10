using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A bolt an enemy fires down a parkour span. It is an ATTACK: when it reaches the player it goes
    /// through <see cref="PlayerCombat.ReceiveAttack"/> like every other hit (hard rule 3), so the parry
    /// window, the block, the chip and the posture maths are the ones the player already knows.
    ///
    /// <para><b>What a perfect deflect does.</b> The bolt turns around and flies back at the shooter;
    /// arriving, it damages the shooter's health and posture. And the player GAINS SPEED along their look
    /// (<see cref="ProjectileMath.SpeedGain"/> → <c>FirstPersonMotor.AddImpulse</c>): a deflect on a
    /// span is a boost toward wherever you are aiming, which is what makes these enemies a route rather
    /// than a fight (BACKLOG §0, MOVEMENT-PRINCIPLES rules 5 and 6). A block or a hit resolves as it
    /// always does and the bolt is spent.</para>
    ///
    /// <para><b>The tell.</b> The flight is the wind-up: the bolt is visible for its whole travel, and the
    /// parry cue (<c>Sfx.ParryCue</c> plus a flare on the bolt) fires when 0.28 s of flight remain -- the
    /// same lead every enemy attack gives. Scaled time throughout: hitstop freezes it with the world.</para>
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public const float CueLead = 0.28f;
        public const float DefaultHitRadius = 1.0f;
        public const float DefaultMaxLife = 6f;
        public const float RuntimeForecastHorizon = 2f;

        /// <summary>
        /// THE ONE GLOW IN TRAVERSAL. Every effect in this project ships under the 1.05 bloom threshold
        /// because light on an enemy means "you deflected" (ANIMATION-VFX section 4). The bolt is the
        /// exception, on purpose and by data: it is not the enemy, it is the ATTACK'S TELL, and a tell you
        /// have to answer at 32 m/s while running has to be the brightest thing on the span. The SHOOTER
        /// itself still never glows until it is deflected. Peak channel 1.6: over the cap, under the
        /// ~1.25x ACES ceiling times the additive stack, so it blooms without whiting out.
        /// </summary>
        public static readonly Color HotCore = new Color(1.6f, 0.95f, 0.38f, 1f);
        public const float HotCorePeak = 1.6f;
        /// <summary>At the cue the core goes white-hot: the "press now" pop, the same beat a body's cue flash is.</summary>
        public static readonly Color CueCore = new Color(1.6f, 1.45f, 1.2f, 1f);
        /// <summary>Core diameter, metres. 0.28 read as a dot at 15 m; 0.55 reads as a ball in flight.</summary>
        public const float CoreSize = 0.55f;   // 2026-09-06: up from 0.36 -- "easier to see / larger" (lead's number, do not move)
        /// <summary>Seconds of flight the trail covers behind the core (at 36-40 m/s that is 5-6 m of streak).
        /// 2026-09-06 VFX pass: up from 0.12 -- a longer streak is what turns a fast ball into a readable LINE
        /// you can judge the path of, not just a dot you notice.</summary>
        public const float TrailSeconds = 0.16f;
        /// <summary>Trail head width as a fraction of the core: up from 0.55 so the streak has body, not just length.</summary>
        public const float TrailWidthScale = 0.75f;
        /// <summary>Recorded samples in the streak. A two-point tangent cannot show the core's curved history.</summary>
        public const int TrailPoints = 7;
        /// <summary>Largest visual displacement from the logical flight path. Below the 1 m hit radius, and
        /// close to the 0.275 m core radius, so the curve reads without advertising a false collision line.</summary>
        public const float WeaveAmplitude = 0.34f;
        public const float WeaveFadeInSeconds = 0.09f;
        public const float WeaveFadeOutSeconds = 0.12f;
        /// <summary>How much bigger the core gets at the cue -- the "press now" pop. Up from 1.9: a bigger cue
        /// flare is the one honest way to make the tell louder without touching when it fires.</summary>
        public const float CueFlareScale = 2.3f;

        [Tooltip("How close to the player's chest the bolt has to get to resolve as a hit, metres.")]
        public float hitRadius = DefaultHitRadius;   // 2026-09-06: up from 0.7 -- a bolt that grazes still resolves, so it can still be parried
        [Tooltip("Speed multiplier once deflected back at the shooter.")]
        public float reflectSpeedScale = 1.4f;
        [Tooltip("Seconds a bolt may live, either way, before it is cleaned up.")]
        public float maxLife = DefaultMaxLife;
        [Tooltip("Lens punch on a deflect that bought speed, degrees.")]
        public float deflectFovKick = 4f;

        EnemyController shooter;
        EnemyData data;
        Transform playerT;
        PlayerCombat combat;
        FirstPersonMotor motor;
        PlayerLook look;

        Vector3 dir;
        int boltId;      // BoltRegistry key, taken at Fire (F2)
        float speed;
        float age;
        bool cued, reflected, spent;
        Vector3 previousTargetChest;
        bool hasTargetHistory;
        float cueAt = -1f;
        float arrivedAt = -1f;
        Vector3 baseScale;
        float visualPhase;

        /// <summary>True only while this bolt can still resolve against the player.</summary>
        public bool IsIncoming { get { return !reflected && !spent; } }
        /// <summary>True while a perfect-parried bolt is returning to its shooter.</summary>
        public bool IsReflected { get { return reflected && !spent; } }
        /// <summary>True once this bolt has finished resolving.</summary>
        public bool IsSpent { get { return spent; } }
        /// <summary>Scaled world time when the incoming parry cue first fired, or -1 before it fires.</summary>
        public float CueAt { get { return cueAt; } }
        /// <summary>Scaled world time when the incoming bolt first reached the player, or -1 before arrival.</summary>
        public float ArrivedAt { get { return arrivedAt; } }
        /// <summary>
        /// Latest allocation-free flight forecast in scaled world time. MaxValue means the current path
        /// has no bounded contact; burst owners use this without inspecting the player's parry state.
        /// </summary>
        public float PredictedContactAt { get; private set; }

        /// <summary>Set once by the shooter. Direction begins toward the led target and the logical root then
        /// turns only through the existing capped homing. The visible child may weave before the cue.</summary>
        public void Fire(EnemyController from, EnemyData d, Vector3 direction, float speedMetresPerSecond,
                         PlayerCombat target)
        {
            Fire(from, d, direction, speedMetresPerSecond, target, float.PositiveInfinity);
        }

        public void Fire(EnemyController from, EnemyData d, Vector3 direction, float speedMetresPerSecond,
                         PlayerCombat target, float initialContactSeconds)
        {
            boltId = BoltRegistry.NextId();
            shooter = from;
            data = d;
            combat = target;
            playerT = target != null ? target.transform : null;
            motor = target != null ? target.GetComponent<FirstPersonMotor>() : null;
            look = target != null ? target.GetComponent<PlayerLook>() : null;
            dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            speed = speedMetresPerSecond;
            age = 0f;
            cued = false;
            reflected = false;
            spent = false;
            cueAt = -1f;
            arrivedAt = -1f;
            PredictedContactAt = float.IsPositiveInfinity(initialContactSeconds)
                ? float.MaxValue
                : Time.time + Mathf.Max(0f, initialContactSeconds);
            ResetTargetHistory(playerT);
            BuildTrail();
            visualPhase = ProjectileVisualMath.Phase(boltId);
        }

        LineRenderer trail;
        Renderer core;
        Transform visual;
        Vector3[] trailHistory;
        float trailSampleTimer;
        Vector3 previousTrailHead;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// A short additive streak recorded from the visible core's actual positions. Multiple points let
        /// it retain the pre-cue weave as a curve; a two-point velocity tangent can only draw a straight line.
        /// Scaled time like the bolt itself, with one fixed buffer allocated when the shot is born.
        /// </summary>
        void BuildTrail()
        {
            // ProjectileShooter authors an explicit Core child. A legacy/direct caller may still put its
            // Renderer on this root; that remains a supported straight-flight fallback and must never be offset.
            visual = transform.Find("Core");
            core = visual != null ? visual.GetComponent<Renderer>() : GetComponent<Renderer>();
            if (core == null) return;
            if (visual == null) visual = core.transform;
            baseScale = visual.localScale;
            trail = SlashFx.CreateLine(visual, "Trail", TrailPoints, CoreSize * TrailWidthScale, 0.03f, false, core.sharedMaterial);
            trail.useWorldSpace = true;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trailHistory = new Vector3[TrailPoints];
            ResetTrail();
        }

        void UpdateTrail(float dt)
        {
            if (trail == null || trailHistory == null) return;
            Vector3 head = visual != null ? visual.position : transform.position;
            float step = TrailSeconds / Mathf.Max(1, TrailPoints - 1);
            ProjectileVisualMath.RecordTrail(previousTrailHead, head, dt, step, ref trailSampleTimer, trailHistory);
            previousTrailHead = head;
            for (int i = 0; i < TrailPoints; i++) trail.SetPosition(i, trailHistory[i]);
        }

        void ResetTrail()
        {
            if (trail == null || trailHistory == null) return;
            trailSampleTimer = 0f;
            Vector3 head = visual != null ? visual.position : transform.position;
            previousTrailHead = head;
            for (int i = 0; i < TrailPoints; i++)
            {
                trailHistory[i] = head;
                trail.SetPosition(i, head);
            }
        }

        void SetCoreColor(Color c)
        {
            if (core == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            core.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            core.SetPropertyBlock(mpb);
        }

        static Vector3 Chest(Transform t) { return t.position + Vector3.up * 1.2f; }

        void ResetTargetHistory(Transform target)
        {
            hasTargetHistory = target != null;
            if (hasTargetHistory) previousTargetChest = Chest(target);
        }

        void RefreshTargetHistory()
        {
            ResetTargetHistory(reflected ? (shooter != null ? shooter.transform : null) : playerT);
        }

        void Update()
        {
            if (spent) return;
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                // The player still moves during hitstop. Refreshing here prevents that legitimate movement
                // from becoming one giant relative sweep when the world clock resumes.
                RefreshTargetHistory();
                return;
            }
            age += dt;
            if (age > maxLife) { Spend(); return; }

            Vector3 previousBolt = transform.position;

            if (!reflected && playerT != null && data != null && data.projectileHomingDegPerSec > 0f)
            {
                // Steering and the launch forecast use the same moving-target intercept. Chasing the
                // current chest bends a correct lead behind a fast crossing runner and moves the parry
                // beat outside the authored route window.
                Vector3 expectedVelocity = motor != null ? motor.Velocity : Vector3.zero;
                dir = ProjectileFlightMath.HomingDirection(dir, transform.position, Chest(playerT),
                    expectedVelocity, speed, data.projectileHomingDegPerSec, dt);
            }
            transform.position += dir * speed * dt;
            Vector3 currentBolt = transform.position;

            if (!reflected)
            {
                if (playerT == null || combat == null) { Spend(); return; }
                Vector3 target = Chest(playerT);
                Vector3 expectedTargetVelocity = motor != null ? motor.Velocity : Vector3.zero;
                Vector3 targetStart = hasTargetHistory
                    ? ProjectileMath.ContinuousTargetStart(previousTargetChest, target, expectedTargetVelocity,
                                                           TimeScaleController.PlayerDelta)
                    : target;
                // The motor and launch/steering forecasts all speak in player-clock metres per second.
                // Using scaled world dt here made the same player appear ~50x faster during hitstop,
                // which could turn a valid contact/cue into infinity for a few frames.
                Vector3 targetVelocity = ProjectileMath.ForecastTargetVelocity(
                    expectedTargetVelocity, motor != null, targetStart, target,
                    TimeScaleController.PlayerDelta);
                previousTargetChest = target;
                hasTargetHistory = true;
                float hitFraction;
                bool sweptHit = ProjectileMath.SweptSphereFirstHit(previousBolt, currentBolt, targetStart,
                                                                   target, hitRadius, out hitFraction);
                float remaining;
                if (sweptHit) remaining = 0f;
                else
                {
                    float homing = data != null ? data.projectileHomingDegPerSec : 0f;
                    bool forecast = ProjectileFlightMath.TryForecastContact(currentBolt, dir, speed,
                        target, targetVelocity, homing, hitRadius,
                        Mathf.Min(RuntimeForecastHorizon, Mathf.Max(0f, maxLife - age)), out remaining);
                    if (!forecast) remaining = float.PositiveInfinity;
                }
                PredictedContactAt = float.IsPositiveInfinity(remaining)
                    ? float.MaxValue
                    : Time.time + Mathf.Max(0f, remaining);
                if (ProjectileVisualMath.CanOffset(transform, visual))
                {
                    Vector3 offset = ProjectileVisualMath.WeaveOffset(dir, age, remaining, CueLead, visualPhase,
                                                                      WeaveAmplitude, WeaveFadeInSeconds,
                                                                      WeaveFadeOutSeconds, !cued);
                    visual.localPosition = transform.InverseTransformVector(offset);
                }
                if (ProjectileMath.CueDue(remaining, CueLead, cued))
                {
                    cued = true;
                    cueAt = Time.time;
                    AudioManager.Play(Sfx.ParryCue, 0.8f, 1.15f, 0.02f);
                    if (visual != null)
                    {
                        if (ProjectileVisualMath.CanOffset(transform, visual)) visual.localPosition = Vector3.zero;
                        visual.localScale = baseScale * CueFlareScale; // the flare: "press now", the same beat as a body's cue
                    }
                    SetCoreColor(CueCore);                      // ...and the core goes white-hot for the same reason
                }
                UpdateTrail(dt);
                // F2 (bolt-timing plan): this bolt is an INCOMING ATTACK for the player's fairness
                // machinery -- a missed parry costs the mistime, not the whiff, and recovery is clamped to
                // end before the next cue. Cleared the instant it is spent or reflected.
                BoltRegistry.Report(boltId,
                                    cued || float.IsPositiveInfinity(remaining)
                                        ? float.MaxValue
                                        : Time.time + Mathf.Max(0f, remaining - CueLead),
                                    float.IsPositiveInfinity(remaining)
                                        ? float.MaxValue
                                        : Time.time + Mathf.Max(0f, remaining));
                if (sweptHit)
                {
                    transform.position = Vector3.Lerp(previousBolt, currentBolt, hitFraction);
                    Arrive();
                }
                return;
            }

            // Flying back. Arriving at the shooter's chest is the payoff.
            if (ProjectileVisualMath.CanOffset(transform, visual)) visual.localPosition = Vector3.zero;
            UpdateTrail(dt);
            if (shooter == null || !shooter.IsAlive) { Spend(); return; }
            Vector3 shooterChest = Chest(shooter.transform);
            Vector3 shooterStart = hasTargetHistory
                ? ProjectileMath.ContinuousTargetStart(previousTargetChest, shooterChest, Vector3.zero, dt)
                : shooterChest;
            previousTargetChest = shooterChest;
            hasTargetHistory = true;
            float returnHitFraction;
            if (ProjectileMath.SweptSphereFirstHit(previousBolt, currentBolt, shooterStart, shooterChest,
                                                   hitRadius + 0.3f, out returnHitFraction))
            {
                transform.position = Vector3.Lerp(previousBolt, currentBolt, returnHitFraction);
                var info = new DamageInfo
                {
                    damage = data != null ? data.parriedProjectileDamage : 20f,
                    postureDamage = data != null ? data.parriedProjectilePosture : 20f,
                    point = transform.position,
                    direction = dir,
                    source = playerT != null ? playerT.gameObject : gameObject
                };
                if (shooter.Health != null) shooter.Health.TakeDamage(info);
                if (shooter.Posture != null) shooter.Posture.Add(info.postureDamage);
                SlashFx.Sparks(transform.position, -dir + Vector3.up * 0.3f, new Color(1f, 0.72f, 0.35f, 1f), 8, 7f, 40f);
                AudioManager.Play(Sfx.Hit, 0.9f, 1.1f, 0.05f);
                Spend();
            }
        }

        void Arrive()
        {
            if (arrivedAt < 0f) arrivedAt = Time.time;
            var info = new AttackInfo
            {
                attack = data != null ? data.projectileAttack : null,
                attacker = shooter,
                damage = data != null && data.projectileAttack != null ? data.projectileAttack.damage : 10f,
                unblockable = false,
                incomingDirection = dir     // P2: the parry is judged against the BOLT, not the perch's bearing
            };
            var result = shooter != null ? combat.ReceiveAttack(info) : ParryResult.None;

            if (result == ParryResult.Perfect && shooter != null && shooter.IsAlive)
            {
                // Deflected: back it goes, and the deflect buys speed toward the look.
                reflected = true;
                BoltRegistry.Clear(boltId);   // flying the other way: no longer incoming
                PredictedContactAt = float.MaxValue;
                dir = ProjectileMath.ReflectDirection(transform.position, Chest(shooter.transform), -dir);
                speed *= reflectSpeedScale;
                ResetTargetHistory(shooter.transform);
                if (visual != null)
                {
                    if (ProjectileVisualMath.CanOffset(transform, visual)) visual.localPosition = Vector3.zero;
                    visual.localScale = baseScale;
                }
                ResetTrail();                 // no incoming curve dragged behind the straight reflected payoff
                SetCoreColor(HotCore);
                if (motor != null)
                {
                    Vector3 aim = look != null ? look.AimForward : playerT.forward;
                    motor.AddImpulse(ProjectileMath.SpeedGain(aim, data != null ? data.parrySpeedGain : 6f));
                }
                if (CameraFX.I != null) CameraFX.I.FovKick(deflectFovKick);
                return;
            }
            Spend();
        }

        void Spend()
        {
            if (spent) return;
            spent = true;
            PredictedContactAt = float.MaxValue;
            BoltRegistry.Clear(boltId);
            Destroy(gameObject);
        }

        void OnDestroy() { BoltRegistry.Clear(boltId); }
    }
}
