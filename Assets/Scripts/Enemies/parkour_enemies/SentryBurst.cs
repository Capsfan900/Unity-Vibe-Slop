using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A sentry DETONATES when its posture breaks (2026-09-06): the body dies on the spot (souls, mist,
    /// the EnemyKilled event, all through the ordinary death path via Health.TakeDamage) and throws a
    /// <see cref="SentryFlare"/> up over the route it was covering. This replaces the sentry dash: the
    /// reward for two clean deflects is a floating grapple point, not a body to blink to.
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
        Posture posture;
        bool fired;

        /// <summary>The flare this sentry threw, or null. Tests and the harness.</summary>
        public SentryFlare LastFlare { get; private set; }

        void Awake()
        {
            ctrl = GetComponent<EnemyController>();
            posture = GetComponent<Posture>();
        }

        void OnEnable() { if (posture != null) posture.OnBroken += HandleBroken; }
        void OnDisable() { if (posture != null) posture.OnBroken -= HandleBroken; }

        void HandleBroken()
        {
            if (fired || ctrl == null || !ctrl.IsAlive) return;
            var data = ctrl.data;
            if (data == null || !data.rangedOnly) return;   // a melee body that shares the prefab code never bursts
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

            // Through the ordinary death path so souls, the kill event and the mist all happen as for any kill.
            if (ctrl.Health != null && !ctrl.Health.IsDead)
                ctrl.Health.TakeDamage(new DamageInfo { damage = 999999f, point = chest, direction = Vector3.up, source = gameObject });
            return LastFlare;
        }
    }
}
