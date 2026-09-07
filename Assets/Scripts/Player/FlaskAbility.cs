using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Estus-style heal. Charge is spent on start; taking a hit interrupts the drink (charge lost).</summary>
    public class FlaskAbility : MonoBehaviour
    {
        public bool IsDrinking { get; private set; }

        Health health;
        PlayerStats stats;
        PlayerResources resources;
        PlayerCombat combat;
        WeaponController weapons;
        WeaponViewmodel viewmodel;
        bool interrupted;

        void Awake()
        {
            health = GetComponent<Health>();
            stats = GetComponent<PlayerStats>();
            resources = GetComponent<PlayerResources>();
            combat = GetComponent<PlayerCombat>();
            weapons = GetComponent<WeaponController>();
            viewmodel = GetComponentInChildren<WeaponViewmodel>();
        }

        void Update()
        {
            if (!GameManager.IsPlaying || IsDrinking) return;
            // F is bound to BOTH Heal and Interact. When a wand pedestal is offering its prompt the
            // press belongs to the altar; deciding it here rather than by script execution order is
            // what stops one press from silently drinking a flask as the menu opens.
            if (WandPedestal.PromptActive) return;
            if (!InputReader.I.HealPressed) return;
            TryDrink();
        }

        /// <summary>
        /// One heal press: spend a charge and start the drink, or refuse (already drinking, busy, at full
        /// health, no charges). <see cref="Update"/> calls this when <see cref="InputReader"/> reports the
        /// press — input is still read only there. Returns whether a drink started.
        /// </summary>
        public bool TryDrink()
        {
            if (IsDrinking) return false;
            if (combat.IsExecuting || combat.IsAttacking || combat.IsStaggered) return false;
            if (health.Current >= health.Max - 0.01f) { AudioManager.Play(Sfx.Click, 0.4f, 0.6f); return false; }
            if (!resources.UseFlask()) { AudioManager.Play(Sfx.Click, 0.4f, 0.6f); return false; }
            StartCoroutine(DrinkCo());
            return true;
        }

        IEnumerator DrinkCo()
        {
            IsDrinking = true;
            interrupted = false;
            float dur = GameManager.I.statsData.flaskDrinkSeconds;
            if (weapons != null) weapons.CancelAttack();
            if (viewmodel != null) viewmodel.PlayDrink(dur);
            AudioManager.Play(Sfx.Heal);
            float t = 0f;
            while (t < dur && !interrupted) { t += Time.deltaTime; yield return null; }
            if (!interrupted)
            {
                health.Heal(stats.FlaskHeal);
                if (ScreenFlash.I) ScreenFlash.I.Flash(new Color(0.4f, 1f, 0.6f), 0.25f, 0.3f);
                AudioManager.Play(Sfx.Checkpoint, 0.6f, 1.3f);
            }
            IsDrinking = false;
        }

        public void Interrupt()
        {
            if (!IsDrinking) return;
            interrupted = true;
            if (viewmodel != null) viewmodel.Interrupt();
            // 2026-09-06 audio pass: PlayerCombat plays Sfx.Hurt for the hit that caused this regardless of
            // whether a drink was in progress, so a punished heal sounded exactly like an ordinary hit --
            // the charge you just lost had no sound of its own. Layers on top of, never instead of, Hurt.
            AudioManager.Play(Sfx.Spill, 0.7f, 1.1f, 0.04f);
        }
    }
}
