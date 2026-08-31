using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Tap parry. Press opens an Active window (perfect + late). After it closes there is a recovery whose
    /// length depends on WHY the press failed, so the system punishes mashing without punishing a genuine
    /// mistimed deflect during a fast exchange.
    ///
    /// Three responsiveness rules, in order of how much they matter:
    ///   1. Input is buffered. A press is never silently dropped just because the state machine was busy.
    ///   2. The whiff penalty is graduated. Pressing at nothing is mashing; pressing at an incoming attack
    ///      and missing the window is a mistake, and costs far less.
    ///   3. Recovery can never outlast the next cue. Whatever the penalty, you are always able to act again
    ///      before the next "parry now" signal fires.
    /// </summary>
    public class ParryController : MonoBehaviour
    {
        public enum State { Idle, Active, Recovery }
        public State Current { get; private set; } = State.Idle;
        public bool IsActive => Current == State.Active;
        public bool IsBusy => Current != State.Idle;

        float pressTime = -99f;
        float stateEnd;
        bool consumed;

        /// <summary>True when the press that opened the current window had an attack genuinely inbound.</summary>
        bool pressWasContested;

        /// <summary>Most recent parry press, whether or not the state machine could act on it yet.</summary>
        float bufferedPressTime = -99f;

        PlayerStatsData d;
        WeaponController weapons;
        WeaponViewmodel viewmodel;
        PlayerCombat combat;

        public float PerfectWindow => d.parryPerfectWindow * (weapons != null && weapons.Current != null ? weapons.Current.parryWindowMultiplier : 1f);
        public float LateWindow => d.parryLateWindow;

        void Awake()
        {
            weapons = GetComponent<WeaponController>();
            combat = GetComponent<PlayerCombat>();
            viewmodel = GetComponentInChildren<WeaponViewmodel>();
        }

        void OnEnable() { GameEvents.PlayerPostureBroken += Cancel; }
        void OnDisable() { GameEvents.PlayerPostureBroken -= Cancel; }

        void Start()
        {
            d = GameManager.I != null ? GameManager.I.statsData : null;
        }

        void Update()
        {
            if (!GameManager.IsPlaying || d == null) return;

            // Record intent FIRST. Reading the press before the state machine advances means a press made
            // during Recovery survives to be spent the instant Recovery ends, and a press made while Idle
            // is still consumed on this same frame — buffering costs zero latency.
            if (InputReader.I.ParryPressed) bufferedPressTime = Time.time;

            if (Current == State.Active && Time.time >= stateEnd) EnterRecovery();
            else if (Current == State.Recovery && Time.time >= stateEnd) Current = State.Idle;

            if (Current == State.Idle && HasBufferedPress() && CanParry())
            {
                bufferedPressTime = -99f;
                StartParry();
            }
        }

        bool HasBufferedPress() => Time.time - bufferedPressTime <= d.parryInputBuffer;

        bool CanParry()
        {
            if (Current != State.Idle) return false;
            if (combat != null && combat.IsStaggered) return false;
            if (combat != null && (combat.IsExecuting || combat.IsDrinking)) return false;
            return true;
        }

        public void StartParry()
        {
            if (weapons != null) weapons.CancelAttack();
            Current = State.Active;
            pressTime = Time.time;
            consumed = false;

            // Captured at PRESS time, not at window close: by the time the window shuts the attack may
            // already have landed. What matters is whether the player was reacting to something real.
            pressWasContested = EnemyController.AnyAttackIncoming(transform.position, d.parryIncomingLookahead);

            stateEnd = pressTime + PerfectWindow + LateWindow;
            if (viewmodel != null) viewmodel.PlayParry(PerfectWindow + LateWindow);
            AudioManager.Play(Sfx.Swing, 0.35f, 1.4f);
        }

        void EnterRecovery()
        {
            float recovery;
            if (consumed) recovery = d.parrySuccessRecovery;               // deflected: back on your feet at once
            else if (pressWasContested) recovery = d.parryMistimeRecovery;  // tried and missed: a real attempt
            else recovery = d.parryWhiffRecovery;                           // swung at nothing: this is the mash tax

            Current = State.Recovery;
            stateEnd = Time.time + recovery;
            ClampRecoveryToNextCue();

            if (viewmodel != null) viewmodel.EndParry();
        }

        /// <summary>
        /// Hard guarantee: recovery must never still be running when the next "parry now" cue fires,
        /// or the player watches a signal they are structurally unable to answer. Naturally a no-op when
        /// nothing is incoming, which is exactly when the full mash penalty should stand.
        /// </summary>
        void ClampRecoveryToNextCue()
        {
            float nextCue = EnemyController.EarliestCueTime(transform.position, d.parryIncomingLookahead + 1f);
            if (nextCue >= float.MaxValue) return;

            float mustEndBy = nextCue - d.parryCueSafetyMargin;
            float floor = Time.time + d.parryMinRecovery;
            stateEnd = Mathf.Min(stateEnd, Mathf.Max(floor, mustEndBy));
        }

        public ParryResult Resolve(in AttackInfo a, bool facing)
        {
            if (Current != State.Active) return ParryResult.Hit;
            float elapsed = Time.time - pressTime;
            var r = ParryMath.Evaluate(elapsed, PerfectWindow, LateWindow, facing, a.unblockable);
            if (r != ParryResult.Hit) consumed = true;
#if UNITY_EDITOR
            Debug.Log($"[Parry] {r} elapsed={elapsed * 1000f:F0}ms perfect={PerfectWindow * 1000f:F0}ms facing={facing} unblockable={a.unblockable}");
#endif
            return r;
        }

        /// <summary>
        /// Called by PlayerCombat the moment a deflect lands. Closes the window immediately instead of
        /// letting it idle out: in a flurry you are ready again in parrySuccessRecovery (~0.08s) rather
        /// than coasting on a stale window for up to another 0.25s. This is what makes back-to-back
        /// deflects feel continuous.
        /// </summary>
        public void NotifyDeflected()
        {
            if (Current != State.Active) return;
            consumed = true;
            EnterRecovery();
        }

        public void Cancel()
        {
            Current = State.Idle;
            bufferedPressTime = -99f;
            if (viewmodel != null) viewmodel.EndParry();
        }
    }
}
