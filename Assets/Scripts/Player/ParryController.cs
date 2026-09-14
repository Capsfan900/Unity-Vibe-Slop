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

        /// <summary>null = read the button; set = forced (the feature suite drives the stance).</summary>
        bool? guardForced;
        bool guardPoseUp;

        /// <summary>
        /// Is the parry button DOWN this frame? Settable, following the project's TryJump/TryDash idiom
        /// so the feature suite can hold a stance without an input device; call
        /// <see cref="ReleaseGuardOverride"/> to hand control back to the button.
        /// </summary>
        public bool GuardHeld
        {
            get
            {
                if (guardForced.HasValue) return guardForced.Value;
                return InputReader.I != null && InputReader.I.ParryHeld;
            }
            set { guardForced = value; }
        }

        /// <summary>Stop forcing <see cref="GuardHeld"/>; read the button again.</summary>
        public void ReleaseGuardOverride() { guardForced = null; }

        /// <summary>
        /// The Sekiro stance is actually up: the button is down AND the player is in a state that can
        /// hold steel. Swinging drops your own guard on purpose — otherwise "hold RMB, mash LMB" is a
        /// free win and the timing game never has to be played.
        /// </summary>
        public bool IsGuarding => GuardHeld && CanGuard();

        /// <summary>True when the last <see cref="Resolve"/> produced a Blocked that came from the HELD
        /// guard rather than from a late press. PlayerCombat reads it to pick the cost.</summary>
        public bool LastResolveWasGuard { get; private set; }

        /// <summary>True when the press that opened the current window had an attack genuinely inbound.</summary>
        bool pressWasContested;

        /// <summary>Most recent parry press, whether or not the state machine could act on it yet.</summary>
        float bufferedPressTime = -99f;

        /// <summary>
        /// Where the last blow this controller judged came from. Captured in <see cref="Resolve"/>
        /// because that is the only place the AttackInfo is in scope, and spent one call later in
        /// <see cref="NotifyDeflected"/> — PlayerCombat calls the two back to back on the same frame,
        /// so this is never stale by more than that.
        /// It exists so the deflect's camera kick can point somewhere. A kick with no direction is a
        /// shake, and this game already has one of those.
        /// </summary>
        Vector3 lastFeedbackSource;
        bool haveFeedbackSource;
        bool hookPerfectFeedback;

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

            SyncGuardPose();
        }

        bool CanGuard()
        {
            if (ThrownBlade.IsAway) return false;          // BladeThrow: no steel in hand, no guard
            if (combat == null) return true;
            if (combat.IsCarried) return false;            // held in a grab
            if (combat.IsStaggered) return false;          // a broken guard is the punish; it must stay broken
            if (combat.IsExecuting || combat.IsDrinking) return false;
            if (combat.IsAttacking) return false;          // your own swing opens you up
            return true;
        }

        /// <summary>
        /// One writer for the stance pose, and it tracks the BUTTON rather than <see cref="IsGuarding"/>.
        /// The two differ in exactly one place and it matters: swinging drops your guard *mechanically*,
        /// but the swing must still END IN the stance rather than in idle, so while an attack is running
        /// this leaves the viewmodel alone and <c>AttackCo</c> retargets its own recovery leg at the
        /// guard pose. Anything else produces the two-stage motion this whole path exists to avoid.
        /// </summary>
        void SyncGuardPose()
        {
            if (viewmodel == null) return;
            viewmodel.GuardWanted = GuardHeld && CanShowGuard();

            // The attack coroutine owns the model while it runs and lands in the stance by itself.
            if (combat != null && combat.IsAttacking) return;

            bool up = viewmodel.GuardWanted;
            if (up == guardPoseUp) return;
            guardPoseUp = up;
            if (up) viewmodel.PlayGuard();
            else viewmodel.EndGuard();
        }

        /// <summary>
        /// <see cref="CanGuard"/> without the attacking clause: what the VIEWMODEL should show. A swing
        /// suspends the guard's protection but not the blade's journey back to the stance.
        /// </summary>
        bool CanShowGuard()
        {
            if (combat == null) return true;
            if (combat.IsStaggered) return false;
            if (combat.IsExecuting || combat.IsDrinking) return false;
            return true;
        }

        bool HasBufferedPress() => Time.time - bufferedPressTime <= d.parryInputBuffer;

        bool CanParry()
        {
            if (Current != State.Idle) return false;
            if (ThrownBlade.IsAway) return false;          // BladeThrow: unarmed until the sword returns
            if (combat != null && combat.IsCarried) return false;   // held in a grab
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
            // ONE MOTION INTO GUARD. The press and the stance are the same button, so a press made with
            // the button DOWN goes straight to the stance. It used to play the parry FLICK first and
            // settle into the stance afterwards, and because that flick lives at x 0.05 - inward and
            // forward, almost on the crosshair - raising the guard read as "present the weapon, THEN
            // guard": the blade shot forward and came back. There is no waypoint on the path now.
            // The flick survives only for a press with the button already released (gamepad tap, tests).
            if (viewmodel != null)
            {
                if (IsGuarding) viewmodel.PlayGuard();
                else viewmodel.PlayParry(PerfectWindow + LateWindow);
            }
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

            // EndParry falls back into the stance when the button is still down (WeaponViewmodel keeps
            // its own `guarding` flag), so the window closing is invisible to a player who is holding.
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
            // The timing is evaluated FIRST and the stance only ever upgrades what would have been a
            // Hit. A hold can never manufacture a Perfect, so the deflect stays strictly better than
            // the guard and the whole design keeps pointing at the window.
            // The feedback has to use the SAME source rule as the facing check. A melee blow comes
            // from its attacker, while a bolt comes from against its travel so running past a perch
            // does not make the kick point toward the perch behind the player.
            haveFeedbackSource = a.attacker != null || a.incomingDirection.sqrMagnitude > 1e-6f;
            Vector3 attackerPosition = a.attacker != null ? a.attacker.transform.position : transform.position;
            lastFeedbackSource = FeedbackSourceDirection(a.incomingDirection, attackerPosition, transform.position);

            bool guarding = IsGuarding;
            float elapsed = Current == State.Active ? Time.time - pressTime : float.MaxValue;
            var r = ParryMath.Evaluate(elapsed, PerfectWindow, LateWindow, facing, a.unblockable, guarding);
            // HOOK is a narrowly scoped second timing source: it may upgrade only the matching turret's
            // real projectile contact, during the pull, inside the item's authored E-to-contact window.
            // It never opens a general parry window and therefore cannot deflect any other attack.
            if (r == ParryResult.Hit && facing && !a.unblockable && a.projectile != null)
            {
                var items = GetComponent<PlayerItems>();
                if (items != null && items.TryResolveHookParry(a.projectile, a.attacker))
                {
                    r = ParryResult.Perfect;
                    hookPerfectFeedback = true;
                }
            }
            LastResolveWasGuard = r == ParryResult.Blocked &&
                                  (Current != State.Active || elapsed > PerfectWindow + LateWindow + 1e-4f);
            if (r != ParryResult.Hit && Current == State.Active && !LastResolveWasGuard) consumed = true;
#if UNITY_EDITOR
            Debug.Log($"[Parry] {r} elapsed={(Current == State.Active ? elapsed * 1000f : -1f):F0}ms perfect={PerfectWindow * 1000f:F0}ms facing={facing} unblockable={a.unblockable} guard={guarding}");
#endif
            return r;
        }

        /// <summary>
        /// The feedback counterpart to combat-facing. Kept as a named seam because projectile travel
        /// and attacker position intentionally disagree after the runner has passed a sentry.
        /// </summary>
        public static Vector3 FeedbackSourceDirection(Vector3 incomingDirection, Vector3 attackerPosition, Vector3 playerPosition)
        {
            return ParryMath.SourceDirection(incomingDirection, attackerPosition, playerPosition);
        }

        /// <summary>
        /// Called by PlayerCombat the moment a deflect lands. Closes the window immediately instead of
        /// letting it idle out: in a flurry you are ready again in parrySuccessRecovery (~0.08s) rather
        /// than coasting on a stale window for up to another 0.25s. This is what makes back-to-back
        /// deflects feel continuous.
        ///
        /// <para>It is also the deflect's one guaranteed same-frame hook, so the impact package hangs
        /// off it: <see cref="ParryImpact"/> adds the directional camera kick, the FOV punch, the
        /// stepped hitstop release and the extra audio voices that PlayerCombat's own feedback does not
        /// cover. All of it is force rather than light, on purpose — see ParryImpact.</para>
        /// </summary>
        public void NotifyDeflected()
        {
            bool normalWindow = Current == State.Active;
            if (!normalWindow && !hookPerfectFeedback) return;
            if (normalWindow)
            {
                consumed = true;
                EnterRecovery();
            }
            hookPerfectFeedback = false;

            // AFTER EnterRecovery, never before. EnterRecovery calls EndParry, which re-asserts the
            // stance when the button is still down; kicking the blade first and then re-raising the
            // guard on top of it would eat the kickback entirely.
            ParryImpact.Deflect(transform, lastFeedbackSource, haveFeedbackSource);

            // The blade's own recoil. DeflectImpact runs on PlayerDelta (rule 1), so while the world is
            // frozen at 0.02 the weapon is the ONE thing still moving — the strongest weight cue in the
            // package and it costs nothing. It starts from the live pose, so tap and hold deflects both
            // get the same physical confirmation while the blocked-guard thud stays separate.
            if (viewmodel != null) viewmodel.DeflectImpact();
        }

        public void Cancel()
        {
            Current = State.Idle;
            bufferedPressTime = -99f;
            guardPoseUp = false;
            if (viewmodel != null) { viewmodel.GuardWanted = false; viewmodel.EndGuard(); viewmodel.EndParry(); }
        }
    }
}
