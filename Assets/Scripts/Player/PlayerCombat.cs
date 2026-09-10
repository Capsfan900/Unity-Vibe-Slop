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
        PlayerPosture posture;

        public Health Health => health;
        public PlayerPosture Posture => posture;
        public bool IsExecuting => exec != null && exec.IsExecuting;
        public bool IsDrinking => flask != null && flask.IsDrinking;
        public bool IsAttacking => weapons != null && weapons.IsAttacking;

        /// <summary>Posture broken: the player cannot parry or attack and takes amplified damage.</summary>
        public bool IsStaggered => posture != null && posture.IsBroken;

        public bool IsBusy => IsExecuting || IsDrinking || IsAttacking || IsStaggered;

        public int PerfectParries { get; private set; }

        /// <summary>
        /// The deflect colour. Matches EnemyVisuals.ParryGlow so the spark on your weapon and the flash on
        /// the enemy are visibly the same event rather than two unrelated effects.
        /// </summary>
        static readonly Color DeflectSteel = new Color(0.78f, 0.88f, 1f);

        /// <summary>
        /// The guard colour. Deliberately duller and warmer than <see cref="DeflectSteel"/>: a guarded
        /// hit is steel scraping steel, and it must never be mistaken on screen for a deflect.
        /// </summary>
        static readonly Color GuardSteel = new Color(0.62f, 0.63f, 0.60f);

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
            posture = GetComponent<PlayerPosture>();
            gameObject.layer = Layers.Player;

            // The fire on the weapon is the player's read on their own Pyre. It attaches here rather
            // than in PrefabFactory so it exists on every player that has ever been built — the shipped
            // prefab, the sandbox, and the ones tests spin up — with no rebuild required.
            // DisallowMultipleComponent makes this idempotent.
            if (GetComponent<WeaponEmber>() == null) gameObject.AddComponent<WeaponEmber>();
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

            // Facing is judged against where the attack COMES FROM: the attacker for a swing, against
            // the travel for a bolt (AttackInfo.incomingDirection; combat plan 2026-09-06, P2). Melee
            // passes a zero direction and resolves exactly as before.
            Vector3 to = ParryMath.SourceDirection(a.incomingDirection, a.attacker.transform.position, transform.position);
            bool facing = ParryMath.IsFacing(transform.forward, to, d.facingConeDeg);

            var result = parry.Resolve(a, facing);
            var w = weapons.Current;

            // Captured before this hit resolves: a hit that BREAKS posture is not itself amplified,
            // but everything that lands while you are already broken is.
            float dmgMult = IsStaggered ? d.staggeredDamageMultiplier : 1f;
            float postureMult = a.unblockable ? 1.5f : 1f;

            switch (result)
            {
                case ParryResult.Perfect:
                    PerfectParries++;
                    // Close the window now rather than letting it idle out — you are actionable again in
                    // parrySuccessRecovery instead of coasting on a spent window. This is what lets
                    // back-to-back deflects in a combo feel continuous instead of laggy.
                    parry.NotifyDeflected();
                    a.attacker.OnParried(w.parryPostureDamage * (a.attack != null ? a.attack.parryPostureMultiplier : 1f));
                    GrantGeneralParrySurge(a.attacker, d);
                    // Pyre: a perfect deflect stokes the fire at full rate. See the Blocked case for
                    // the deliberate asymmetry.
                    resources.AddPyre(stats.PyrePerPerfect + w.pyreBonus);
                    TimeScaleController.I.HitStop(feel.parryHitStop, feel.hitStopScale);
                    if (CameraShake.I) CameraShake.I.Small();
                    if (CameraFX.I) CameraFX.I.ChromaticPulse(feel.parryChromatic, feel.parryChromaticTime);
                    // The deflect reads through SHAPE: sparks thrown back along the incoming line plus a
                    // tight crescent across the block. The screen flash is cut to a fraction of what it
                    // was — at full alpha it was washing out the sparks that explain the parry.
                    // Pulled most of the way to pale steel rather than using the raw weapon hue. Enemies
                    // no longer glow at all, so a deflect is the only bright event in a fight and it must
                    // look like the SAME event every time — four differently-coloured parries read as four
                    // different mechanics. The weapon still tints it, just faintly.
                    Color deflect = Color.Lerp(w != null ? w.neon : feel.parryFlash, DeflectSteel, 0.7f);
                    SparkAt(a, deflect, 12, 9f, 26f);
                    DeflectArc(a, deflect, 0.85f);
                    if (ScreenFlash.I) ScreenFlash.I.Flash(feel.parryFlash, feel.parryFlashAlpha * 0.22f, 0.09f);
                    AudioManager.Play(Sfx.Parry, 1f, 1f, 0.08f);
                    break;

                case ParryResult.Blocked:
                {
                    // Two roads reach Blocked and they cost different things.
                    //   TIMED BLOCK (a press that landed in the late window): 30% damage, 0.9x posture,
                    //     and a fraction of the Pyre — you timed SOMETHING, just not well enough.
                    //   HELD GUARD  (the Sekiro stance, no press in window): NO damage at all and the
                    //     largest posture bill in the game, and no Pyre whatsoever. The guard is the
                    //     floor under a fight, not an achievement.
                    // The ladder stays monotone from the player's side: deflect (nothing, + Pyre,
                    // + enemy posture) > guard (posture only) > hit (health AND posture).
                    bool guarded = parry != null && parry.LastResolveWasGuard;
                    float chip = a.damage * (guarded ? d.guardChipDamageMultiplier : d.blockDamageMultiplier) * dmgMult;
                    if (chip > 0f)
                        health.TakeDamage(new DamageInfo { damage = chip, source = a.attacker.gameObject });
                    AddPosture(a.damage * (guarded ? d.guardPostureMultiplier : d.blockPostureMultiplier) * postureMult);
                    a.attacker.OnBlocked();
                    if (!guarded)
                    {
                        // A timed block IS a successful parry, so it stokes the Pyre — but at a fraction
                        // of a perfect deflect. You still took damage and posture for it; the fire
                        // should reward the timing, not the survival. A HELD guard timed nothing, so it
                        // earns nothing: this is what stops turtling from charging the super.
                        resources.AddPyre((stats.PyrePerPerfect + w.pyreBonus) * d.pyreBlockFraction);
                    }
                    if (CameraShake.I) CameraShake.I.Medium();
                    if (guarded)
                    {
                        // The guard eats the damage, so the impact has to arrive as FORCE or it reads as
                        // nothing happening: a hitstop beat, a spark where steel met steel on the raised
                        // blade, a shove off the line, and the stance itself kicked back and recovering.
                        GuardImpact(a, feel, to);
                    }
                    else
                    {
                        // Scraped, not deflected: fewer sparks, slower, desaturated toward steel.
                        SparkAt(a, Color.Lerp(feel.hurtFlash, Color.gray, 0.6f), 5, 5f, 34f);
                        if (ScreenFlash.I) ScreenFlash.I.Flash(feel.hurtFlash, feel.hurtFlashAlpha * 0.18f, 0.12f);
                        AudioManager.Play(Sfx.Block);
                    }
                    break;
                }

                case ParryResult.Hit:
                    health.TakeDamage(new DamageInfo { damage = a.damage * dmgMult, source = a.attacker.gameObject });
                    AddPosture(a.damage * d.hitPostureMultiplier * postureMult);
                    if (flask != null) flask.Interrupt();
                    if (CameraShake.I) CameraShake.I.Big();
                    if (CameraFX.I) CameraFX.I.VignettePulse(0.38f, 0.45f);
                    // Taking a hit should feel heavy and WRONG, not bright. The vignette and shake carry
                    // it; a dark smear across the contact replaces the old full-screen red.
                    SparkAt(a, new Color(0.55f, 0.10f, 0.12f), 4, 3.5f, 45f);
                    if (ScreenFlash.I) ScreenFlash.I.Flash(feel.hurtFlash, feel.hurtFlashAlpha * 0.45f, 0.22f);
                    AudioManager.Play(Sfx.Hurt);
                    GameEvents.RaisePlayerDamaged(a.damage);
                    // Small grounded-only shove. No upward component and nothing while airborne:
                    // launching the player on a 6 m walkway over a pit turned every hit into a fall.
                    if (motor != null && motor.IsGrounded && to.sqrMagnitude > 0.01f)
                        motor.AddImpulse(-to.normalized * 2f);
                    break;
            }

            GameEvents.RaiseParryResolved(result);
            return result;
        }

        /// <summary>
        /// A Perfect is a speed-stack confirmation everywhere, except for a Surge Turret: its overridden
        /// <see cref="SurgeTurret.OnParried"/> already pays the opening ramp's dedicated 1.4 s ladder.
        /// Keeping the exclusion beside combat resolution prevents the turret from double-granting while
        /// ordinary blue sentries, Heavy Sentries, melee enemies, and bosses share the authored player data.
        /// </summary>
        public static bool ShouldGrantGeneralParrySurge(EnemyController attacker)
        {
            return attacker != null && !(attacker is SurgeTurret);
        }

        void GrantGeneralParrySurge(EnemyController attacker, PlayerStatsData d)
        {
            if (!ShouldGrantGeneralParrySurge(attacker) || d == null || motor == null) return;
            ParrySurge.Grant(motor, d.generalParrySurgeStep, d.generalParrySurgeMaxStacks,
                             d.generalParrySurgeSeconds);
        }


        /// <summary>
        /// Feedback for a blow absorbed by the HELD guard. Deliberately percussive rather than bright:
        /// the deflect owns the pale-steel flash and the chromatic pulse, and a guard that borrowed them
        /// would make the two outcomes look like the same event.
        /// </summary>
        void GuardImpact(in AttackInfo a, GameFeelSettings feel, Vector3 to)
        {
            TimeScaleController.I.HitStop(feel.guardHitStop, feel.hitStopScale);

            // The spark belongs ON THE BLADE, not floating in front of the chest — the guard is the
            // weapon doing the work, and a spark anywhere else reads as an unattributed effect.
            // Half-way from the blade tip out to the incoming line: steel on steel.
            Vector3 p = ContactPoint(a);
            var vm = GetComponentInChildren<WeaponViewmodel>(true);
            if (vm != null) p = Vector3.Lerp(vm.TipWorldPosition, p, 0.55f);
            Vector3 dir = a.attacker != null
                ? (a.attacker.transform.position - transform.position).normalized
                : transform.forward;
            dir = (dir + Vector3.up * 0.35f).normalized;
            SlashFx.Sparks(p, dir, GuardSteel, 9, 6.5f, 30f);
            if (vm != null) vm.GuardImpact();

            // Grounded-only, no upward component — same rule as a clean hit: launching the player off a
            // 6 m walkway over a pit turns every blow into a fall.
            if (motor != null && motor.IsGrounded && to.sqrMagnitude > 0.01f)
                motor.AddImpulse(-to.normalized * feel.guardShove);

            AudioManager.Play(Sfx.Block, 1f, 0.85f, 0.06f);
        }

        /// <summary>
        /// Contact point for an incoming attack: roughly chest height, out along the line to the
        /// attacker. Sparks fly back the way the blow came from, which is what makes the deflect
        /// legible without a screen tint.
        /// </summary>
        Vector3 ContactPoint(in AttackInfo a)
        {
            Vector3 origin = transform.position + Vector3.up * 1.25f;
            if (a.attacker == null) return origin + transform.forward * 0.9f;
            if (a.incomingDirection.sqrMagnitude > 1e-6f)
            {
                Vector3 source = ParryMath.SourceDirection(a.incomingDirection, a.attacker.transform.position,
                                                            transform.position);
                return origin + (source.sqrMagnitude > 1e-6f ? source.normalized : transform.forward) * 0.9f;
            }
            Vector3 to = a.attacker.transform.position + Vector3.up * 1.1f - origin;
            return origin + Vector3.ClampMagnitude(to, 1.0f);
        }

        void SparkAt(in AttackInfo a, Color color, int count, float speed, float spread)
        {
            Vector3 p = ContactPoint(a);
            // Back along the incoming line, biased slightly up so debris arcs into view.
            Vector3 attackerPosition = a.attacker != null ? a.attacker.transform.position : transform.position;
            Vector3 dir = ParryMath.SourceDirection(a.incomingDirection, attackerPosition, transform.position);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
            dir = (dir + Vector3.up * 0.45f).normalized;
            SlashFx.Sparks(p, dir, color, count, speed, spread);
        }

        void DeflectArc(in AttackInfo a, Color color, float radius)
        {
            Vector3 p = ContactPoint(a);
            Vector3 attackerPosition = a.attacker != null ? a.attacker.transform.position : transform.position;
            Vector3 toAttacker = ParryMath.SourceDirection(a.incomingDirection, attackerPosition, transform.position);
            toAttacker = toAttacker.sqrMagnitude > 1e-6f ? toAttacker.normalized : transform.forward;
            // Swept across the line of the blow: the arc plane faces the attacker.
            SlashFx.Arc(p, toAttacker, color, radius, 110f, 0.16f, Vector3.up);
        }

        void AddPosture(float amount)
        {
            if (posture != null) posture.Add(amount);
        }
    }
}
