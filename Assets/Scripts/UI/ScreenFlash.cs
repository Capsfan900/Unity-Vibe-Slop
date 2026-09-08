using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// The one full-screen colour overlay. Three sources share it and the LARGEST alpha wins, so
    /// <see cref="Image.color"/> keeps exactly one writer — this Update. Two writers on one channel has
    /// cost this project a feature twice.
    ///
    /// <list type="bullet">
    /// <item><b>Flash</b> — the short decaying punctuation every combat event already uses.</item>
    /// <item><b>Curtain</b> — a cinematic CUT: opaque in the calling frame, held for a wall-clock beat
    /// AND a rendered-frame budget, then revealed. Used by the solar portals so the teleport frame can
    /// never be seen.</item>
    /// <item><b>Wash</b> — a continuous tint written every frame by whatever is near the camera. It is
    /// re-requested each frame and self-clears, so nothing can leave the screen stuck tinted.</item>
    /// </list>
    ///
    /// Everything runs on UNSCALED time: a cut must complete through hitstop, a pause and a death, and
    /// it must never touch <c>Time.timeScale</c> (hard rule 1 — <c>TimeScaleController</c> owns it).
    /// There is no handle to release and therefore no exit path that can leak one.
    /// </summary>
    public class ScreenFlash : MonoBehaviour
    {
        public static ScreenFlash I { get; private set; }
        public Image image;

        Color color = Color.white;
        float peak, duration, t = 999f;

        bool curtainLive, curtainRevealing;
        Color curtainHot = Color.white, curtainSettle = Color.white;
        float curtainHold, curtainReveal = 0.42f, curtainColourFraction = 0.4f;
        int curtainMinFrames = 1, curtainStartFrame;
        float curtainStart, curtainRevealStart;

        float washPending, washShown;
        Color washColor = Color.white;

        /// <summary>The alpha actually on screen this frame. For tests and the debug harness.</summary>
        public float Alpha { get { return image != null ? image.color.a : 0f; } }
        /// <summary>True while a cut is covering or revealing.</summary>
        public bool CurtainActive { get { return curtainLive; } }
        /// <summary>The distance-driven wash applied this frame, before the max with flash and curtain.</summary>
        public float WashAmount { get { return washShown; } }

        void Awake()
        {
            I = this;
            if (image == null) image = GetComponent<Image>();
            if (image != null) { image.raycastTarget = false; image.color = new Color(1, 1, 1, 0); }
        }

        void OnDestroy() { if (I == this) I = null; }

        public void Flash(Color c, float peakAlpha, float seconds)
        {
            // do not let a weaker flash cut a stronger one short
            if (t < duration && peak * (1f - t / duration) > peakAlpha) return;
            color = c; peak = peakAlpha; duration = Mathf.Max(0.01f, seconds); t = 0f;
        }

        /// <summary>
        /// Covers the screen NOW and holds it, then reveals. The cover is written to the Image inside
        /// this call rather than deferred to Update, because the caller teleports in the same call and
        /// Unity renders no frame in between — that ordering, not the hold length, is what guarantees the
        /// destination is never seen uncovered.
        /// </summary>
        public void Curtain(Color hot, Color settle, float holdSeconds, int minHoldFrames,
                            float revealSeconds, float colourSettleFraction)
        {
            curtainHot = hot;
            curtainSettle = settle;
            curtainHold = Mathf.Max(0f, holdSeconds);
            curtainMinFrames = Mathf.Max(1, minHoldFrames);
            curtainReveal = Mathf.Max(0.01f, revealSeconds);
            curtainColourFraction = Mathf.Clamp(colourSettleFraction, 0.05f, 1f);
            curtainStart = Time.unscaledTime;
            curtainStartFrame = Time.frameCount;
            curtainRevealing = false;
            curtainLive = true;
            if (image != null) image.color = new Color(hot.r, hot.g, hot.b, 1f);
        }

        /// <summary>Drops a cut on the spot. Used by portal/arena resets so a debug teleport cannot
        /// strand a cover that was armed for a crossing that is being undone.</summary>
        public void ClearCurtain() { curtainLive = false; curtainRevealing = false; }

        /// <summary>
        /// Requests the continuous tint for this frame; the strongest request wins and the value is
        /// consumed by Update. Callers re-request every frame, so walking away clears it by itself.
        /// </summary>
        public void RequestWash(Color c, float amount)
        {
            if (amount <= washPending) return;
            washPending = Mathf.Clamp01(amount);
            washColor = c;
        }

        /// <summary>
        /// True while the cover must stay fully opaque. Both conditions matter and neither implies the
        /// other: the clock keeps a cut readable on a fast machine, the frame count keeps it honest on a
        /// slow one.
        /// </summary>
        public static bool CurtainHolding(float elapsed, int framesSince, float holdSeconds, int minFrames)
        {
            return elapsed < holdSeconds || framesSince < minFrames;
        }

        /// <summary>Reveal curve. Ease-out: clears the frame fast, then leaves a thinning theme tint.</summary>
        public static float CurtainReveal(float revealElapsed, float revealSeconds)
        {
            if (revealSeconds <= 0f) return 0f;
            float k = 1f - Mathf.Clamp01(revealElapsed / revealSeconds);
            return k * k;
        }

        void Update()
        {
            if (image == null) return;
            if (t < duration) t += Time.unscaledDeltaTime;

            float flashA = 0f;
            if (t < duration)
            {
                float k = 1f - Mathf.Clamp01(t / duration);
                flashA = peak * k * k;
            }

            float curtainA = 0f;
            Color curtainC = curtainHot;
            if (curtainLive)
            {
                if (!curtainRevealing &&
                    !CurtainHolding(Time.unscaledTime - curtainStart, Time.frameCount - curtainStartFrame,
                                    curtainHold, curtainMinFrames))
                {
                    curtainRevealing = true;
                    curtainRevealStart = Time.unscaledTime;
                }

                if (!curtainRevealing) curtainA = 1f;
                else
                {
                    float e = Time.unscaledTime - curtainRevealStart;
                    curtainA = CurtainReveal(e, curtainReveal);
                    curtainC = Color.Lerp(curtainHot, curtainSettle,
                                          Mathf.Clamp01(e / (curtainReveal * curtainColourFraction)));
                    if (curtainA <= 0.0005f) { curtainA = 0f; curtainLive = false; }
                }
            }

            washShown = washPending;
            float washA = washPending;
            Color washC = washColor;
            washPending = 0f;

            float a = flashA;
            Color c = color;
            if (washA > a) { a = washA; c = washC; }
            if (curtainA > a) { a = curtainA; c = curtainC; }

            // A full-screen Image dirties the whole HUD canvas batch on every colour write, so write
            // only when something actually moved.
            Color current = image.color;
            if (a <= 0f)
            {
                if (current.a != 0f) image.color = new Color(c.r, c.g, c.b, 0f);
                return;
            }
            if (Mathf.Abs(current.a - a) < 0.001f && Mathf.Abs(current.r - c.r) < 0.004f &&
                Mathf.Abs(current.g - c.g) < 0.004f && Mathf.Abs(current.b - c.b) < 0.004f) return;
            image.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
        }
    }
}
