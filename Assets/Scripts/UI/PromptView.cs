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
        float alpha;
        string standing = "";
        string flash = "";
        float flashUntil = -1f;

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

        /// <summary>The standing cue. An empty string clears it; the flash is left alone either way.</summary>
        public void Set(string s)
        {
            standing = s ?? "";
        }

        /// <summary>A momentary line over the standing cue. A non-positive duration clears the flash.</summary>
        public void Flash(string s, float seconds)
        {
            flash = s ?? "";
            flashUntil = flash.Length > 0 && seconds > 0f ? Time.unscaledTime + seconds : -1f;
        }

        void Update()
        {
            if (text == null) return;
            string shown = Current;
            // Assign only on a change: TMP rebuilds its mesh on every set, and this runs every frame.
            if (shown.Length > 0 && text.text != shown) text.text = shown;
            float target = shown.Length > 0 ? 1f : 0f;
            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime * 8f);
            float pulse = shown.Length > 0 ? 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 10f) : 1f;
            var c = text.color; c.a = alpha * pulse; text.color = c;
            text.transform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.unscaledTime * 10f)) * Mathf.Lerp(0.8f, 1f, alpha);
        }
    }
}
