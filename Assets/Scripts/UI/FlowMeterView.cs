using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// THE FLOW METER (2026-09-07, the user's ask): the top-right band under the radio, carrying the two
    /// numbers a runner wants at the crosshair and nothing else — the SPEED BOOST the player is holding
    /// right now (from ANY source) with its live speed and its stack pips, and the PARRY CHAIN, the count
    /// of consecutive perfect deflects.
    ///
    /// <para><b>It is a readout and only a readout.</b> The chain is counted HERE, in the UI, on purpose:
    /// nothing in the game may read it back, so putting the counter anywhere a system could reach it would
    /// be an invitation to make it mean something. It grants nothing and costs nothing (the user:
    /// "just a metric now").</para>
    ///
    /// <para><b>Where each number comes from.</b> The boost is <see cref="FirstPersonMotor.SpeedMultiplier"/>
    /// — the single aggregate every speed source in the game writes, which is what "from any source" means.
    /// The live speed is <see cref="FirstPersonMotor.HorizontalSpeed"/>. The pips are
    /// <see cref="ParrySurge.Stacks"/> when a surge exists on the player; a boost from something that is
    /// not the surge shows its multiplier with the pips dark, which is the truth. The decay bar is
    /// <see cref="ParrySurge.SecondsToNextDrop"/> over the window this view calibrates from the surge
    /// itself (the seconds-per-stack is not exposed, and the reading right after a grant IS that window),
    /// so a stack about to expire is visible before it goes.</para>
    ///
    /// <para><b>At rest it is not there.</b> No boost and no chain means alpha 0 — no "x1.00" and no "0"
    /// shouted at the player for a whole run. It lingers <see cref="idleLingerSeconds"/> after both fall
    /// back to rest so that LOSING the surge or breaking the chain is something you see happen, then fades.
    /// Unscaled/CanvasGroup only: this view never writes a colour on a bar (BarView owns those) and never
    /// touches gameplay.</para>
    /// </summary>
    public class FlowMeterView : MonoBehaviour
    {
        public CanvasGroup group;
        public TMP_Text speedValue;      // "x1.42" — authoritative
        public TMP_Text speedMs;         // "27 M/S" — the live speed
        public TMP_Text streakValue;     // "x7"
        public TMP_Text streakLabel;
        public Image[] stackPips;
        public BarView decayBar;

        [Header("Shipped by HudBuilder (hard rule 9)")]
        [Tooltip("Seconds the meter stays up after the boost and the chain are both back at rest, so the " +
                 "loss reads instead of vanishing on the same frame it happens.")]
        public float idleLingerSeconds = 1.5f;
        [Tooltip("CanvasGroup alpha per second on the way in. Fast: a gain is news.")]
        public float fadeInSpeed = 10f;
        [Tooltip("CanvasGroup alpha per second on the way out. Slow: a loss should be watched, not blinked.")]
        public float fadeOutSpeed = 2.2f;
        [Tooltip("Multiplier above which the boost counts as live. 1.005 ignores float dust on 1.")]
        public float boostEpsilon = 1.005f;
        [Tooltip("Ember gold: the boost, its pips and its decay line.")]
        public Color emberColor = new Color(0.878f, 0.627f, 0.188f);
        [Tooltip("Ghost teal: the deflect colour. The chain is a parry readout, so it wears the parry colour.")]
        public Color tealColor = new Color(0.498f, 0.749f, 0.710f);
        [Tooltip("An unearned pip: bone, nearly out.")]
        public Color pipOffColor = new Color(0.910f, 0.886f, 0.839f, 0.18f);

        FirstPersonMotor motor;
        ParrySurge surge;
        int streak;
        float idleSince = -99f;
        float decayWindow;
        int lastStacks = -1;

        // Last values actually written, quantised the way they are printed: an idle meter must not
        // rewrite its labels every frame (the speedrun timer's gate).
        int shownBoostPct = -1;
        int shownMs = -1;
        int shownStreak = -1;

        /// <summary>Consecutive perfect deflects. Display only — nothing in the game may read this.</summary>
        public int Streak { get { return streak; } }
        /// <summary>Alpha the meter is currently asking for. Tests read it.</summary>
        public float TargetAlpha { get; private set; }

        void OnEnable()
        {
            GameEvents.ParryResolved += OnParry;
            GameEvents.PlayerDied += Break;
            GameEvents.PlayerRespawned += Break;
            if (group != null) group.alpha = 0f;
            idleSince = -99f;
        }

        void OnDisable()
        {
            GameEvents.ParryResolved -= OnParry;
            GameEvents.PlayerDied -= Break;
            GameEvents.PlayerRespawned -= Break;
        }

        /// <summary>
        /// A PERFECT deflect extends the chain. Anything else that resolved against the player ends it:
        /// a Blocked hit is timing that failed (it still costs health and posture) and a Hit is the chain
        /// plainly broken. ParryResult.None is not a resolution against the player and is ignored.
        /// Death and respawn clear it too, so a fresh attempt never inherits a stale number.
        /// </summary>
        /// <summary>
        /// What one resolution does to the chain, as a PURE function of the current count.
        ///
        /// <para>Pure on purpose, the way <see cref="MovementPose"/> and <c>ProjectileMath</c> are: a
        /// MonoBehaviour's <c>OnEnable</c> never runs in an EditMode test, so a test that raises
        /// <c>GameEvents.ParryResolved</c> at a component built with <c>new GameObject(...)</c> is
        /// asserting against a handler that was never subscribed - it reads 0 forever and looks like a
        /// counting bug. The rule lives here where it can be tested without a running scene.</para>
        ///
        /// <para>A <c>Blocked</c> hit is timing that failed and still costs health and posture, so it
        /// ends the chain; <c>None</c> is not a resolution against the player and leaves it alone.</para>
        /// </summary>
        public static int NextStreak(int streak, ParryResult r)
        {
            if (r == ParryResult.Perfect) return streak + 1;
            if (r == ParryResult.Blocked || r == ParryResult.Hit) return 0;
            return streak;
        }

        void OnParry(ParryResult r)
        {
            streak = NextStreak(streak, r);
        }

        void Break() { streak = 0; }

        void Update()
        {
            if (motor == null) motor = FindAnyObjectByType<FirstPersonMotor>();
            if (motor != null && surge == null) surge = motor.GetComponent<ParrySurge>();

            float mult = motor != null ? motor.SpeedMultiplier : 1f;
            bool boosted = mult >= boostEpsilon;
            bool live = boosted || streak > 0;

            float now = Time.unscaledTime;
            if (live) idleSince = -99f;
            else if (idleSince < -1f) idleSince = now;

            TargetAlpha = live || now - idleSince < idleLingerSeconds ? 1f : 0f;
            if (group != null)
            {
                float rate = TargetAlpha > group.alpha ? fadeInSpeed : fadeOutSpeed;
                group.alpha = Mathf.MoveTowards(group.alpha, TargetAlpha, Time.unscaledDeltaTime * rate);
            }

            // Nothing on screen: do not spend a frame formatting text nobody can see.
            if (group != null && group.alpha <= 0.001f) return;

            int boostPct = Mathf.RoundToInt(mult * 100f);
            if (boostPct != shownBoostPct)
            {
                shownBoostPct = boostPct;
                if (speedValue != null)
                {
                    speedValue.text = "x" + (boostPct / 100f).ToString("0.00");
                    speedValue.color = boosted ? emberColor : new Color(0.910f, 0.886f, 0.839f, 0.45f);
                }
            }

            int ms = motor != null ? Mathf.RoundToInt(motor.HorizontalSpeed) : 0;
            if (ms != shownMs)
            {
                shownMs = ms;
                if (speedMs != null) speedMs.text = ms + " M/S";
            }

            int stacks = surge != null ? surge.Stacks : 0;
            if (stacks != lastStacks)
            {
                lastStacks = stacks;
                if (stackPips != null)
                    for (int i = 0; i < stackPips.Length; i++)
                        if (stackPips[i] != null) stackPips[i].color = i < stacks ? emberColor : pipOffColor;
            }

            // The decay window calibrates itself: the seconds-to-next-drop is at its largest on the frame
            // after a grant, and that value IS the surge's per-stack window. Reading it beats guessing,
            // and ParrySurge exposes no seconds-per-stack getter.
            if (surge != null)
            {
                float left = surge.SecondsToNextDrop;
                if (left > decayWindow) decayWindow = left;
                if (decayBar != null) decayBar.Set(decayWindow > 0.001f ? left / decayWindow : 0f);
            }
            else if (decayBar != null) decayBar.Set(0f);

            if (streak != shownStreak)
            {
                shownStreak = streak;
                if (streakValue != null)
                {
                    streakValue.text = streak > 0 ? "x" + streak : "-";
                    streakValue.color = streak > 0 ? tealColor : new Color(0.910f, 0.886f, 0.839f, 0.45f);
                }
                if (streakLabel != null)
                    streakLabel.color = new Color(0.910f, 0.886f, 0.839f, streak > 0 ? 0.55f : 0.35f);
            }
        }
    }
}
