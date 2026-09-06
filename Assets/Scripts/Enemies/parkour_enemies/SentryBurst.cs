using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A parkour enemy DETONATES (2026-09-06): on a posture break OR on death by its own reflected bolts it
    /// explodes and throws a <see cref="SentryFlare"/> up over the route it was covering. It never waits to be
    /// finished. The one exception is a finish by the grapple hook (pull, break, execute in one frame): that
    /// kill is the reward itself and throws no flare.
    ///
    /// <para>Lives on the Sentry_* prefabs only (PrefabFactory adds it beside ProjectileShooter when the data
    /// shoots) and touches no brain code: it listens to Posture.OnBroken like the controller does. Every
    /// number is written by PrefabFactory (rule 9).</para>
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class SentryBurst : MonoBehaviour
    {
        /// <summary>
        /// The burst's colour family: the same violet-white as the flare it throws (SentryFlare.Core),
        /// never the bolt's amber -- this is a REWARD lighting up, not a THREAT (2026-09-06 VFX pass).
        /// </summary>
        public static readonly Color BurstHue = new Color(0.85f, 0.65f, 1f, 1f);
        /// <summary>
        /// Star-flare size at the chest, metres. 1.6 (a `Flare`'s "highlight" shape, same footprint as
        /// the bolt core) read as a glint, not an event, at 25 m -- a body just died and a hook appeared,
        /// which is rarer and bigger than a routine parry spark. Sized against the sentry's own ~1.8 m
        /// height so it reads as "this body" exploding, not a mote near it.
        /// </summary>
        public const float BurstFlareSize = 2.4f;
        public const float BurstFlareSeconds = 0.3f;
        /// <summary>
        /// The shockwave a `Flare` alone cannot sell: an expanding hoop is legible as "an event happened
        /// here" from far outside the sparks' own travel, which is the read a distant span needs.
        /// </summary>
        public const float BurstRingRadius = 2.6f;
        public const float BurstRingSeconds = 0.4f;
        public const int BurstSparkCount = 22;
        public const float BurstSparkSpeed = 11f;
        public const float BurstSparkSpread = 170f;

        [Tooltip("Metres per second straight up at launch. 9 with gravity 4 apexes ~10 m above the perch after 2.25 s.")]
        public float flareUpSpeed = 9f;
        [Tooltip("Metres per second along the sentry's flat facing (over the route it covered).")]
        public float flareOutSpeed = 3f;
        [Tooltip("Downward acceleration on the flare, m/s^2. Low: it floats, it does not fall.")]
        public float flareGravity = 4f;
        [Tooltip("Seconds the flare lives; it fades linearly to nothing over this.")]
        public float flareLife = 4.5f;

        EnemyController ctrl;
        bool fired;

        /// <summary>The flare this sentry threw, or null. Tests and the harness.</summary>
        public SentryFlare LastFlare { get; private set; }

        void Awake()
        {
            ctrl = GetComponent<EnemyController>();
        }

        Health health;
        Posture posture;
        bool pendingBurst;

        void OnEnable()
        {
            if (health == null) health = GetComponent<Health>();
            if (posture == null) posture = GetComponent<Posture>();
            if (health != null) health.OnDied += HandleDied;
            if (posture != null) posture.OnBroken += HandleBroken;
        }
        void OnDisable()
        {
            if (health != null) health.OnDied -= HandleDied;
            if (posture != null) posture.OnBroken -= HandleBroken;
        }

        bool IsParkour => ctrl != null && ctrl.data != null && ctrl.data.rangedOnly;

        /// <summary>
        /// 2026-09-06 (user): a parkour enemy NEVER waits to be finished. A posture break is a detonation --
        /// deferred one frame so the grapple hook, which breaks and executes in the same frame, can claim
        /// the body first (that finish throws no flare).
        /// </summary>
        void HandleBroken()
        {
            if (fired || !IsParkour || ctrl == null || !ctrl.IsAlive) return;
            pendingBurst = true;
        }

        void Update()
        {
            if (!pendingBurst) return;
            pendingBurst = false;
            if (fired || ctrl == null) return;
            if (ctrl.Current == EnemyController.State.Executed || ctrl.DiedExecuted) return;   // the hook got it: no flare
            if (!ctrl.IsAlive) return;                                                          // died meanwhile: HandleDied decided
            fired = true;
            Detonate();
        }

        /// <summary>
        /// Death by its own reflected bolt (or anything that is not an execute) throws the flare. A death
        /// by deathblow / grapple-finish throws nothing: the finish is the reward, not a lift.
        /// </summary>
        void HandleDied()
        {
            if (fired || !IsParkour || ctrl == null) return;
            if (ctrl.Current == EnemyController.State.Executed || ctrl.DiedExecuted) return;
            fired = true;
            Detonate();
        }

        /// <summary>The burst, forced. Public so FeatureTests can prove it without a posture break.</summary>
        public SentryFlare Detonate()
        {
            Vector3 chest = transform.position + Vector3.up * 1.3f * Mathf.Max(0.3f, transform.localScale.y);
            LastFlare = SentryFlare.Spawn(chest, transform.forward, flareUpSpeed, flareOutSpeed, flareGravity, flareLife);

            SlashFx.Flare(chest, BurstHue, BurstFlareSize, BurstFlareSeconds);
            SlashFx.Ring(chest, Vector3.up, BurstHue, BurstRingRadius, BurstRingSeconds);
            SlashFx.Sparks(chest, Vector3.up, BurstHue, BurstSparkCount, BurstSparkSpeed, BurstSparkSpread);
            AudioManager.Play(Sfx.Thunder, 0.6f, 1.5f, 0.05f);
            if (CameraShake.I != null) CameraShake.I.Small();

            // Forced on a living body (tests, the harness): through the ordinary death path so souls, the kill
            // event and the mist happen as for any kill. On a real death this is already true.
            if (ctrl.Health != null && !ctrl.Health.IsDead)
                ctrl.Health.TakeDamage(new DamageInfo { damage = 999999f, point = chest, direction = Vector3.up, source = gameObject });
            return LastFlare;
        }
    }
}
