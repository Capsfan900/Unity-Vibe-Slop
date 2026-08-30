using UnityEngine;

namespace VibeGame1
{
    /// <summary>Hub that every incoming enemy attack funnels through. Resolves parry/block/hit and applies feedback.</summary>
    public class PlayerCombat : MonoBehaviour
    {
        Health health;
        ParryController parry;
        WeaponController weapons;
        PlayerResources resources;
        PlayerStats stats;
        PlayerLook look;
        FlaskAbility flask;
        ExecuteInteractor exec;
        FirstPersonMotor motor;

        public Health Health => health;
        public bool IsExecuting => exec != null && exec.IsExecuting;
        public bool IsDrinking => flask != null && flask.IsDrinking;
        public bool IsAttacking => weapons != null && weapons.IsAttacking;
        public bool IsBusy => IsExecuting || IsDrinking || IsAttacking;

        public int PerfectParries { get; private set; }

        void Awake()
        {
            health = GetComponent<Health>();
            parry = GetComponent<ParryController>();
            weapons = GetComponent<WeaponController>();
            resources = GetComponent<PlayerResources>();
            stats = GetComponent<PlayerStats>();
            look = GetComponent<PlayerLook>();
            flask = GetComponent<FlaskAbility>();
            exec = GetComponent<ExecuteInteractor>();
            motor = GetComponent<FirstPersonMotor>();
            gameObject.layer = Layers.Player;
        }

        void OnEnable()
        {
            health.OnChanged += OnHealthChanged;
        }

        void OnDisable()
        {
            health.OnChanged -= OnHealthChanged;
        }

        void Start()
        {
            GameEvents.RaisePlayerHealthChanged(health.Current, health.Max);
        }

        void OnHealthChanged(float c, float m) => GameEvents.RaisePlayerHealthChanged(c, m);

        public ParryResult ReceiveAttack(in AttackInfo a)
        {
            if (health.IsDead) return ParryResult.None;
            if (health.Invulnerable) return ParryResult.None;

            var d = GameManager.I.statsData;
            var feel = GameManager.I.feel;

            Vector3 to = a.attacker.transform.position - transform.position; to.y = 0f;
            Vector3 f = transform.forward; f.y = 0f;
            bool facing = to.sqrMagnitude < 0.01f || Vector3.Angle(f, to) <= d.facingConeDeg;

            var result = parry.Resolve(a, facing);
            var w = weapons.Current;

            switch (result)
            {
                case ParryResult.Perfect:
                    PerfectParries++;
                    a.attacker.OnParried(w.parryPostureDamage * (a.attack != null ? a.attack.parryPostureMultiplier : 1f));
                    resources.AddJuice(stats.JuicePerPerfect + w.juiceBonus);
                    TimeScaleController.I.HitStop(feel.parryHitStop, feel.hitStopScale);
                    if (CameraShake.I) CameraShake.I.Small();
                    if (CameraFX.I) CameraFX.I.ChromaticPulse(feel.parryChromatic, feel.parryChromaticTime);
                    if (ScreenFlash.I) ScreenFlash.I.Flash(feel.parryFlash, feel.parryFlashAlpha, 0.12f);
                    AudioManager.Play(Sfx.Parry, 1f, 1f, 0.08f);
                    break;

                case ParryResult.Blocked:
                    health.TakeDamage(new DamageInfo { damage = a.damage * d.blockDamageMultiplier, source = a.attacker.gameObject });
                    a.attacker.OnBlocked();
                    if (CameraShake.I) CameraShake.I.Medium();
                    if (ScreenFlash.I) ScreenFlash.I.Flash(feel.hurtFlash, feel.hurtFlashAlpha * 0.5f, 0.15f);
                    AudioManager.Play(Sfx.Block);
                    break;

                case ParryResult.Hit:
                    health.TakeDamage(new DamageInfo { damage = a.damage, source = a.attacker.gameObject });
                    if (flask != null) flask.Interrupt();
                    if (CameraShake.I) CameraShake.I.Big();
                    if (CameraFX.I) CameraFX.I.VignettePulse(0.25f, 0.4f);
                    if (ScreenFlash.I) ScreenFlash.I.Flash(feel.hurtFlash, feel.hurtFlashAlpha, 0.25f);
                    AudioManager.Play(Sfx.Hurt);
                    GameEvents.RaisePlayerDamaged(a.damage);
                    // small knockback away from attacker
                    if (motor != null && to.sqrMagnitude > 0.01f) motor.AddImpulse(-to.normalized * 4f + Vector3.up * 1.5f);
                    break;
            }

            GameEvents.RaiseParryResolved(result);
            return result;
        }
    }
}
