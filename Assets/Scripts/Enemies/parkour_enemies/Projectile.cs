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
        /// <summary>How much bigger the core gets at the cue -- the "press now" pop. Up from 1.9: a bigger cue
        /// flare is the one honest way to make the tell louder without touching when it fires.</summary>
        public const float CueFlareScale = 2.3f;

        [Tooltip("How close to the player's chest the bolt has to get to resolve as a hit, metres.")]
        public float hitRadius = 1.0f;   // 2026-09-06: up from 0.7 -- a bolt that grazes still resolves, so it can still be parried
        [Tooltip("Speed multiplier once deflected back at the shooter.")]
        public float reflectSpeedScale = 1.4f;
        [Tooltip("Seconds a bolt may live, either way, before it is cleaned up.")]
        public float maxLife = 6f;
        [Tooltip("Lens punch on a deflect that bought speed, degrees.")]
        public float deflectFovKick = 4f;

        EnemyController shooter;
        EnemyData data;
        Transform playerT;
        PlayerCombat combat;
        FirstPersonMotor motor;
        PlayerLook look;

        Vector3 dir;
        float speed;
        float age;
        bool cued, reflected, spent;
        Vector3 baseScale;

        /// <summary>Set once by the shooter. Direction is toward the player's chest at fire time and never
        /// changes: a bolt is a straight line you can step out of.</summary>
        public void Fire(EnemyController from, EnemyData d, Vector3 direction, float speedMetresPerSecond,
                         PlayerCombat target)
        {
            shooter = from;
            data = d;
            combat = target;
            playerT = target != null ? target.transform : null;
            motor = target != null ? target.GetComponent<FirstPersonMotor>() : null;
            look = target != null ? target.GetComponent<PlayerLook>() : null;
            dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            speed = speedMetresPerSecond;
            baseScale = transform.localScale;
            BuildTrail();
        }

        LineRenderer trail;
        Renderer core;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// A short additive streak behind the core: the cheapest possible motion read (two points, one
        /// shared material), and it is what makes a 0.36 m ball at 32 m/s read as a SHOT rather than a
        /// spark. Scaled time like the bolt itself.
        /// </summary>
        void BuildTrail()
        {
            core = GetComponent<Renderer>();
            if (core == null) return;
            trail = SlashFx.CreateLine(transform, "Trail", 2, CoreSize * TrailWidthScale, 0.03f, false, core.sharedMaterial);
            trail.useWorldSpace = true;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            UpdateTrail();
        }

        void UpdateTrail()
        {
            if (trail == null) return;
            Vector3 head = transform.position;
            trail.SetPosition(0, head);
            trail.SetPosition(1, head - dir * speed * TrailSeconds);
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

        void Update()
        {
            if (spent) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            if (age > maxLife) { Spend(); return; }

            if (!reflected && playerT != null && data != null && data.projectileHomingDegPerSec > 0f)
            {
                // HOMING (2026-09-06): the bolt turns toward the chest at a capped rate, so a runner is met
                // and the parry is always on offer. It is still a line you can read; it is no longer one
                // that sails past because the lead guessed wrong.
                Vector3 want = (Chest(playerT) - transform.position);
                if (want.sqrMagnitude > 1e-4f)
                    dir = Vector3.RotateTowards(dir, want.normalized, data.projectileHomingDegPerSec * Mathf.Deg2Rad * dt, 0f).normalized;
            }
            transform.position += dir * speed * dt;
            UpdateTrail();

            if (!reflected)
            {
                if (playerT == null || combat == null) { Spend(); return; }
                Vector3 target = Chest(playerT);
                float remaining = ProjectileMath.TimeToImpact(Vector3.Distance(transform.position, target), speed);
                if (ProjectileMath.CueDue(remaining, CueLead, cued))
                {
                    cued = true;
                    AudioManager.Play(Sfx.ParryCue, 0.8f, 1.15f, 0.02f);
                    transform.localScale = baseScale * CueFlareScale;   // the flare: "press now", the same beat as a body's cue
                    SetCoreColor(CueCore);                      // ...and the core goes white-hot for the same reason
                }
                if (Vector3.Distance(transform.position, target) <= hitRadius)
                    Arrive();
                return;
            }

            // Flying back. Arriving at the shooter's chest is the payoff.
            if (shooter == null || !shooter.IsAlive) { Spend(); return; }
            if (Vector3.Distance(transform.position, Chest(shooter.transform)) <= hitRadius + 0.3f)
            {
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
                dir = ProjectileMath.ReflectDirection(transform.position, Chest(shooter.transform), -dir);
                speed *= reflectSpeedScale;
                transform.localScale = baseScale;
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
            Destroy(gameObject);
        }
    }
}
