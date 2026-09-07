using TMPro;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The one prompt line under the crosshair, on TWO channels (2026-09-06).
    ///
    /// <para><b>Standing</b> (<see cref="GameEvents.PromptChanged"/>): a cue that is true while a condition
    /// holds — "DEATHBLOW  [ATTACK]", "GRAPPLE  [DASH]", "SURGE 3.2s". Its writers are EDGE-TRIGGERED: they
    /// raise only when their own string changes, so whatever is standing here must survive anything drawn
    /// over it. <b>Flash</b> (<see cref="GameEvents.PromptFlash"/>): a momentary line with its own lifetime —
    /// "PERFECT", "NO TARGET" — which is drawn over the standing cue and then gives it back.</para>
    ///
    /// <para>Before the split there was one channel and a flash overwrote a live cue permanently: the player
    /// deflected a bolt, saw PERFECT, and the "GRAPPLE [DASH]" that was up for the flare it just made never
    /// came back until they looked away and returned. Unscaled time throughout — a prompt must not stretch
    /// in hitstop.</para>
    /// </summary>
    public class PromptView : MonoBehaviour
    {
        public TMP_Text text;

        [Tooltip("Unscaled seconds the line throbs after the string it shows CHANGES, before settling to " +
                 "a steady read. Authored by HudBuilder (hard rule 9).")]
        public float settleSeconds = 0.6f;

        float alpha;
        string standing = "";
        string standingOwner = PromptOwner.Anonymous;
        string flash = "";
        float flashUntil = -1f;
        string lastShown = "";
        float shownAt = -99f;

        /// <summary>What the line is drawing right now. Tests read it.</summary>
        public string Current => Time.unscaledTime < flashUntil && flash.Length > 0 ? flash : standing;

        void OnEnable()
        {
            GameEvents.PromptChanged += Set;
            GameEvents.PromptFlash += Flash;
        }

        void OnDisable()
        {
            GameEvents.PromptChanged -= Set;
            GameEvents.PromptFlash -= Flash;
        }

        /// <summary>Who currently holds the standing line. Tests read it.</summary>
        public string StandingOwner { get { return standingOwner; } }

        /// <summary>
        /// May <paramref name="writer"/> write <paramref name="text"/> over a line currently held by
        /// <paramref name="currentOwner"/>? A non-empty cue always takes the line — last speaker wins, as it
        /// always has. An empty one is a CLEAR, and a clear is only yours to give: it lands when you still
        /// hold the line, or when nobody does. Pure, so the rule is a test rather than a playtest.
        /// </summary>
        public static bool AcceptsStandingWrite(string currentOwner, string writer, string text)
        {
            if (!string.IsNullOrEmpty(text)) return true;
            if (string.IsNullOrEmpty(currentOwner)) return true;
            return currentOwner == (writer ?? PromptOwner.Anonymous);
        }

        /// <summary>The standing cue, unowned. Anyone may clear what this writes.</summary>
        public void Set(string s)
        {
            Set(PromptOwner.Anonymous, s);
        }

        /// <summary>
        /// The standing cue from a named owner. An empty string clears it only if this owner still holds the
        /// line; the flash channel is left alone either way.
        /// </summary>
        public void Set(string owner, string s)
        {
            string text = s ?? "";
            if (!AcceptsStandingWrite(standingOwner, owner, text)) return;
            standing = text;
            standingOwner = text.Length > 0 ? (owner ?? PromptOwner.Anonymous) : PromptOwner.Anonymous;
        }

        /// <summary>A momentary line over the standing cue. A non-positive duration clears the flash.</summary>
        public void Flash(string s, float seconds)
        {
            flash = s ?? "";
            flashUntil = flash.Length > 0 && seconds > 0f ? Time.unscaledTime + seconds : -1f;
        }

        /// <summary>
        /// How hard the line should throb, <paramref name="age"/> seconds after the string it shows last
        /// changed. 1 on arrival, 0 once it has settled.
        ///
        /// <para>It used to be a flat 1 forever, which is the wrong signal twice over. Motion means "read
        /// me NOW"; a line that never stops moving stops meaning anything, and this channel carries cues
        /// that stand for a long time — a wall-surge countdown, the level editor's PLAYING banner — so a
        /// permanent throb under the crosshair was competing with the fight for the same attention it was
        /// asking for. Pure, so the curve is a test rather than a squint.</para>
        /// </summary>
        public static float PulseAmount(float age, float settle)
        {
            if (settle <= 0f) return 0f;
            return Mathf.Clamp01(1f - age / settle);
        }

        void Update()
        {
            if (text == null) return;
            string shown = Current;
            // Assign only on a change: TMP rebuilds its mesh on every set, and this runs every frame.
            if (shown.Length > 0 && text.text != shown) text.text = shown;
            if (shown != lastShown) { lastShown = shown; shownAt = Time.unscaledTime; }

            float target = shown.Length > 0 ? 1f : 0f;
            float k = PulseAmount(Time.unscaledTime - shownAt, settleSeconds);
            // Settled AND at rest: write nothing. A resting prompt used to dirty the canvas every frame
            // with a colour and a scale that were no longer changing.
            if (k <= 0f && Mathf.Approximately(alpha, target)) return;

            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime * 8f);
            float wave = Mathf.Sin(Time.unscaledTime * 10f);
            float pulse = shown.Length > 0 ? Mathf.Lerp(1f, 0.85f + 0.15f * wave, k) : 1f;
            var c = text.color; c.a = alpha * pulse; text.color = c;
            text.transform.localScale = Vector3.one * (1f + 0.08f * k * wave) * Mathf.Lerp(0.8f, 1f, alpha);
        }
    }
}
