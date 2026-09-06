using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>What a stamina spend was for. Carried on the refusal event so the HUD can say WHICH
    /// ability you could not afford, not just "no".</summary>
    /// <summary>APPEND ONLY: the values are raised on GameEvents.StaminaRefused and read by the HUD.</summary>
    public enum StaminaAction { Dash = 0, WallRun = 1, WallJump = 2, Slide = 3 }

    /// <summary>
    /// The movement budget. Dashes, wall runs and wall jumps spend it; standing on the ground earns it
    /// back fast, the air earns it back slowly. It exists for exactly one reason: without it every
    /// movement ability was free and could be chained forever at a constant speed, and "when can I dash
    /// again" was a cooldown the player could not see. With it, the HUD bar IS the answer.
    ///
    /// <para><b>Rules.</b> Regen runs on <see cref="TimeScaleController.PlayerDelta"/> (hard rule 1), so
    /// hitstop neither freezes nor extends it. Every value here is written by <c>PrefabFactory</c> and
    /// asserted by <c>StaminaTunablesTests</c> (rule 9) — the field initialisers are documentation.
    /// A spend that cannot be afforded raises <see cref="Refused"/> and <c>GameEvents.StaminaRefused</c>
    /// and changes nothing; it is never partially taken, so a bar that reads "30" always buys one dash.</para>
    ///
    /// <para><b>Sizing.</b> 100 max, dash 30: three dashes from full, and the fourth needs 0.7 s on the
    /// ground (45/s) or 1.7 s in the air (18/s). A full 1.6 s wall run costs 12 + 22 × 1.6 = 47, so one
    /// full bar is two consecutive runs and a jump, and a run entered on a near-empty bar drops you
    /// early — the bar is the wall's "stamina" the user asked for, and it always covers a full run from
    /// full, which is what keeps <c>LevelArcAnalyzer</c>'s "full run from full stamina" model honest.</para>
    /// </summary>
    public class PlayerStamina : MonoBehaviour
    {
        [Header("Pool")]
        public float max = 100f;
        [Tooltip("Regen per second while grounded. Fast: standing still for a second and a half is a full bar.")]
        public float regenPerSecondGrounded = 45f;
        [Tooltip("Regen per second while airborne. Slow: the air is where the budget is spent, not earned.")]
        public float regenPerSecondAirborne = 18f;
        [Tooltip("Seconds after any spend before regen resumes. Short, so a bar never feels stuck.")]
        public float regenDelay = 0.45f;

        [Header("Costs")]
        public float dashCost = 30f;
        [Tooltip("Paid once on entering a wall run, on top of the per-second drain.")]
        public float wallRunEntryCost = 12f;
        [Tooltip("Drained every second on the wall. The run ends (Exhausted) when the bar hits zero.")]
        public float wallRunDrainPerSecond = 22f;
        public float wallJumpCost = 12f;
        [Tooltip("Stamina a slide costs at entry (2026-09-06, the user, repeatedly: the slide was the one movement " +
                 "tech still free). 12 like the wall run and the wall jump: the dash is the expensive burst at 30, " +
                 "everything else is a 12, so a full bar is three dashes or eight slides. Spent in " +
                 "FirstPersonMotor.TrySlide AFTER every refusal gate, so a slide that does not start is never paid " +
                 "for; a refusal names Slide and flashes like the dash's.")]
        public float slideCost = 12f;

        /// <summary>Debug: never spends, never refuses. God mode (F8) sets this.</summary>
        public bool Infinite;

        public float Current { get; private set; }
        public float Ratio => max > 0f ? Mathf.Clamp01(Current / max) : 0f;
        public bool IsFull => Current >= max - 0.01f;
        /// <summary>Seconds of regen delay left. 0 when regenerating (or full). The HUD reads this to
        /// show the bar "held" after a spend.</summary>
        public float DelayRemaining => Mathf.Max(0f, delayLeft);

        public event Action<float, float> Changed;
        public event Action<StaminaAction> Refused;
        /// <summary>A PERFECT gave stamina back: the amount that actually arrived (clamped). The HUD listens.</summary>
        public event Action<float> Refunded;

        FirstPersonMotor motor;
        float delayLeft;

        void Awake()
        {
            motor = GetComponent<FirstPersonMotor>();
            Current = max;
        }

        void Start() { Raise(); }

        void Update()
        {
            float dt = TimeScaleController.PlayerDelta;
            if (dt <= 0f) return;
            if (Infinite && Current < max) { Current = max; Raise(); }
            if (delayLeft > 0f) { delayLeft -= dt; return; }
            if (Current >= max) return;

            bool grounded = motor == null || motor.IsGrounded;
            float rate = grounded ? regenPerSecondGrounded : regenPerSecondAirborne;
            Current = Mathf.Min(max, Current + rate * dt);
            Raise();
        }

        public bool CanAfford(float cost) => Infinite || Current >= cost - 1e-3f;

        /// <summary>Take <paramref name="cost"/> or refuse it whole. Never partial.</summary>
        public bool TrySpend(float cost, StaminaAction what)
        {
            if (Infinite) return true;
            if (Current < cost - 1e-3f)
            {
                if (Refused != null) Refused(what);
                GameEvents.RaiseStaminaRefused(what);
                return false;
            }
            Current = Mathf.Max(0f, Current - cost);
            delayLeft = regenDelay;
            Raise();
            return true;
        }

        /// <summary>Continuous drain (wall run). Returns true while there is still stamina left after the
        /// drain, false the moment it hits zero.</summary>
        public bool Drain(float perSecond, float dt)
        {
            if (Infinite) return true;
            if (dt <= 0f || perSecond <= 0f) return Current > 0f;
            Current = Mathf.Max(0f, Current - perSecond * dt);
            delayLeft = regenDelay;
            Raise();
            return Current > 0f;
        }

        /// <summary>
        /// Stamina given BACK by a perfectly timed move (see <c>PerfectMath</c>). Clamped to the bar;
        /// never a debit; and it does not touch the regen delay -- a refund is not a spend, and a perfect
        /// chain must not keep the bar from regenerating on top of it. Returns what actually arrived.
        /// </summary>
        public float Refund(float amount)
        {
            if (Infinite || amount <= 0f) return 0f;
            float before = Current;
            Current = PerfectMath.Refund(Current, max, amount);
            float got = Current - before;
            if (got > 0f)
            {
                Raise();
                if (Refunded != null) Refunded(got);
            }
            return got;
        }

        public void ResetFull()
        {
            Current = max;
            delayLeft = 0f;
            Raise();
        }

        void Raise()
        {
            if (Changed != null) Changed(Current, max);
            GameEvents.RaiseStaminaChanged(Current, max);
        }
    }
}
