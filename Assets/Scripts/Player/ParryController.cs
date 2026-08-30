using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Tap parry. Press opens an Active window (perfect + late). After it closes there is a short recovery,
    /// shorter if the parry connected so combos can be chain-deflected.
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

        void Start()
        {
            d = GameManager.I != null ? GameManager.I.statsData : null;
        }

        void Update()
        {
            if (!GameManager.IsPlaying || d == null) return;

            if (Current == State.Active && Time.time >= stateEnd)
            {
                Current = State.Recovery;
                stateEnd = Time.time + (consumed ? d.parrySuccessRecovery : d.parryWhiffRecovery);
                if (viewmodel != null) viewmodel.EndParry();
            }
            else if (Current == State.Recovery && Time.time >= stateEnd)
            {
                Current = State.Idle;
            }

            if (InputReader.I.ParryPressed && CanParry()) StartParry();
        }

        bool CanParry()
        {
            if (Current != State.Idle) return false;
            if (combat != null && (combat.IsExecuting || combat.IsDrinking)) return false;
            return true;
        }

        public void StartParry()
        {
            if (weapons != null) weapons.CancelAttack();
            Current = State.Active;
            pressTime = Time.time;
            consumed = false;
            stateEnd = pressTime + PerfectWindow + LateWindow;
            if (viewmodel != null) viewmodel.PlayParry(PerfectWindow + LateWindow);
            AudioManager.Play(Sfx.Swing, 0.35f, 1.4f);
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

        public void Cancel()
        {
            Current = State.Idle;
            if (viewmodel != null) viewmodel.EndParry();
        }
    }
}
