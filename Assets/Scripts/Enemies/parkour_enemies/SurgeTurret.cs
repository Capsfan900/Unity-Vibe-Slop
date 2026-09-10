using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// <c>pshooter_enemy03</c>'s brain. A small round turret that exists to be PARRIED, not fought: it
    /// shoots, it dies to a single touch, and every bolt you deflect makes you faster.
    ///
    /// <para><b>Why a subclass and not a component.</b> The only signal in the game that says "the player
    /// perfectly deflected something *I* threw" is <see cref="EnemyController.OnParried"/> -- PlayerCombat
    /// calls it on the attacker, and <see cref="Projectile"/> puts the shooter in that slot
    /// (<c>AttackInfo.attacker</c>). It is already <c>virtual</c>, and <see cref="BossController"/> is the
    /// precedent for extending the brain by subclassing it. Nothing in <c>Enemies/Core</c> changes;
    /// <c>GetComponent&lt;EnemyController&gt;()</c> finds this the way it finds the boss.</para>
    ///
    /// <para><b>The payout goes through the sanctioned hook.</b> A deflect calls
    /// <see cref="ParrySurge.Grant"/>, which drives <c>FirstPersonMotor.SpeedMultiplier</c> (the existing
    /// item-speed scalar the HUD status strip already displays). This class writes NO velocity and adds no
    /// motor entry point -- hard rule 10. The bolt's own <c>parrySpeedGain</c> impulse still happens inside
    /// <see cref="Projectile"/>, untouched: the deflect shoves you along your look AND the surge raises the
    /// speed you settle at. They are the punch and the sustain of the same beat.</para>
    ///
    /// <para><b>It is a target, not a duel.</b> Its posture is set out of reach in DataFactory and its HP is
    /// 1, so it never staggers, never offers a deathblow and never throws a flare -- it just dies. The
    /// reflected bolt that comes home is what usually kills it, so the parry is both the reward and the
    /// kill in one input.</para>
    /// </summary>
    public class SurgeTurret : EnemyController
    {
        FirstPersonMotor motor;

        /// <summary>Deflects this turret has paid out. Tests and the harness.</summary>
        public int SurgesGranted { get; private set; }
        /// <summary>The surge this turret last fed. Null until something is deflected.</summary>
        public ParrySurge LastSurge { get; private set; }

        public override void OnParried(float postureDamage)
        {
            base.OnParried(postureDamage);

            // postureDamage > 0 is the discriminator between a REAL deflect and the Ultimate's courtesy
            // call (UltimateAbility line 261 passes 0 to open every enemy in the blast). The ultimate is
            // not a parry and must not pay a surge, or the super would hand out the whole ladder for free.
            if (postureDamage <= 0f) return;
            if (data == null || data.parrySurgeStep <= 0f || data.parrySurgeMaxStacks <= 0) return;

            if (motor == null)
            {
                var combat = FindAnyObjectByType<PlayerCombat>();
                if (combat != null) motor = combat.GetComponent<FirstPersonMotor>();
            }
            if (motor == null) return;

            LastSurge = ParrySurge.Grant(motor, data.parrySurgeStep, data.parrySurgeMaxStacks, data.parrySurgeSeconds);
            if (LastSurge == null) return;
            SurgesGranted++;

            // The confirmation is AUDIO ONLY and it RISES with the stack, so the player hears the ladder
            // climbing without looking at the strip. No new HUD element: StatusStripView already prints
            // "SPEED SURGE xN" from the live ParrySurge, and the strip is where a speed state belongs.
            float pitch = Mathf.Lerp(1.15f, 1.6f, Mathf.InverseLerp(1, Mathf.Max(1, data.parrySurgeMaxStacks), LastSurge.Stacks));
            AudioManager.Play(Sfx.Tick, 0.7f, pitch, 0.02f);
        }
    }
}
