using UnityEngine;

namespace VibeGame1
{
    /// <summary>Which move was timed perfectly. <see cref="None"/> is the resting value.</summary>
    public enum PerfectKind { None = 0, WallJump = 1, DashJump = 2, GrappleBurst = 3 }

    /// <summary>
    /// The arithmetic of a PERFECT — a move pressed inside a short window around a physical moment the
    /// player can learn to feel — as pure functions, so <c>PerfectTimingTests</c> can drive every edge
    /// without a scene and the motor cannot drift from the tests. Same arrangement as
    /// <see cref="WallRunMath"/>, <see cref="TraversalMath"/> and <see cref="SlideImpulse"/>.
    ///
    /// <para><b>Why windows, and why these sizes.</b> A perfect that is frame-perfect (ULTRAKILL's dash
    /// jump: one "dashing" frame at the end of a slide) is a speedrunner's trick, not a mechanic; the
    /// guides say outright that the slide-jump is preferable because the timing is too tight. A window
    /// that is the whole move (any wall jump, any dash-then-jump) is free and teaches nothing. The
    /// learnable band sits where Sekiro's deflect and Celeste's forgiveness do: Sekiro's default deflect
    /// window is ~12 frames (0.20 s) and shrinks when spammed; Celeste's coyote time is 5 frames
    /// (0.08 s) and nobody notices it exists, only that the controls feel fair. So every window here is
    /// 0.12–0.14 s, around a MOMENT — the wall giving up, the dash's launch, the grapple's landing — and
    /// a miss is simply the ordinary move: no penalty, no message, nothing taken.</para>
    ///
    /// <para><b>The reward never punishes.</b> A perfect gives stamina BACK (the move's own cost, or a
    /// bonus for a move that was already free) and is clamped to the bar; a miss changes nothing.
    /// Pressing early or late costs exactly what the move always cost.</para>
    ///
    /// <para><b>docs/MOVEMENT-PRINCIPLES.md, the rules this obeys.</b> Rule 4 (fudge toward intent):
    /// every window is anchored to a PHYSICAL moment -- the wall letting go, the dash's launch, the
    /// pull landing -- never to a frame, and a miss is the ordinary move with no penalty and no message.
    /// Rule 5 (mastery visible, no shortcuts): no new button; the perfect is an expression of the
    /// controls the player already has, and a chain of them is visible skill. Rule 6 (physical, not
    /// magical): the refund is FELT -- <c>FirstPersonMotor.OnPerfect</c> and <c>PlayerStamina.Refunded</c>
    /// fire so the body (sound layer, a small widen, the PERFECT stamp) and the bar (a visible refill)
    /// both answer it. Rule 8 (consistency): every window here is judged on the motor's own clock and
    /// <c>PerfectTimingTests</c> runs each law at 20, 60 and 240 fps, so a perfect is the same width of
    /// time on every machine.</para>
    /// </summary>
    public static class PerfectMath
    {
        /// <summary>
        /// The hybrid wall-jump perfect starts in the final window before a predictable release;
        /// it is not judged against an unseen fixed duration and remains open for the same brief
        /// forgiveness after that release.
        /// </summary>
        /// <remarks>
        /// The motor predicts clock, decay and stamina releases from the live run and opens the cue when
        /// this law opens. A lost face has no honest lead, so it uses only the post-release half.
        /// </remarks>
        public static bool WallJumpHybridIsPerfect(float secondsToRelease, float secondsSinceRelease, float window)
        {
            if (window <= 0f) return false;
            bool before = secondsToRelease >= 0f && secondsToRelease <= window;
            bool after = secondsSinceRelease >= 0f && secondsSinceRelease <= window;
            return before || after;
        }

        /// <summary>
        /// A jump pressed <paramref name="sinceDash"/> seconds after a dash fired is perfect inside
        /// [<paramref name="minDelay"/>, <paramref name="minDelay"/> + <paramref name="window"/>]. The
        /// minimum delay is what stops a mashed dash+jump on the same frame (or a buffered jump that
        /// happened to be pending) from counting: the dash has to be FELT before the jump is thrown.
        /// </summary>
        public static bool DashJumpIsPerfect(float sinceDash, float minDelay, float window)
        {
            if (window <= 0f || sinceDash < 0f) return false;
            return sinceDash >= minDelay && sinceDash <= minDelay + window;
        }

        /// <summary>
        /// The grapple-exit burst is perfect when pressed inside the first <paramref name="window"/>
        /// seconds of its 0.30 s opening — timed to the landing, not fished for.
        /// </summary>
        public static bool BurstIsPerfect(float sinceOpened, float window)
        {
            return window > 0f && sinceOpened >= 0f && sinceOpened <= window;
        }

        /// <summary>The bar after a refund: never above <paramref name="max"/>, never a debit.</summary>
        public static float Refund(float current, float max, float amount)
        {
            if (amount <= 0f) return current;
            return Mathf.Min(max, current + amount);
        }
    }
}
