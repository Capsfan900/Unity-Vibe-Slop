using TMPro;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// THE RADIO PANE (top-right), in the HUD's glass language: station name, a track title that tickers
    /// when it is too long for the glass, "TRACK 2/7", a thin progress line and the key hint — a 2000s
    /// racing-game stereo, read at a glance and never in the way.
    ///
    /// <para><b>It reads <see cref="LevelRadio"/>, it never drives it.</b> The backbone owns the audio,
    /// the playlist and the keys ( <c>]</c> next, <c>[</c> previous, <c>\</c> toggle ); this is a readout.
    /// When a level ships no mp3s <see cref="LevelRadio.HasPlaylist"/> is false and the whole pane ROOT is
    /// hidden — a radio with nothing to play is not a dim pane, it is no pane (the same rule BEST RUNS
    /// follows).</para>
    ///
    /// <para><b>Allocation-free per frame.</b> The three strings are rebuilt only when
    /// <see cref="LevelRadio.OnTrackChanged"/> fires or the cached value actually differs; Update() only
    /// moves a RectTransform and calls <see cref="BarView.Set"/> (anchors, never
    /// <c>Image.fillAmount</c> — hard rule 5). Everything runs on unscaled time so the radio keeps
    /// ticking through hitstop and the pause menu, exactly like the audio does.</para>
    ///
    /// <para>Layout numbers are authored in <c>HudBuilder</c> (hard rule 9); the fields here are wiring
    /// plus the two motion constants the builder also writes.</para>
    /// </summary>
    public class RadioView : MonoBehaviour
    {
        [Tooltip("The pane ROOT (the group, not the glass): hidden whenever the level has no playlist.")]
        public GameObject paneRoot;
        public TMP_Text stationText;
        public TMP_Text titleText;
        public TMP_Text counterText;
        public TMP_Text keyHintText;
        public BarView progressBar;
        [Tooltip("The masked viewport the title tickers inside; its width is the ticker's window.")]
        public RectTransform titleViewport;

        [Header("Ticker (authored by HudBuilder)")]
        [Tooltip("Ticker scroll speed in canvas units per second.")]
        public float tickerSpeed = 34f;
        [Tooltip("Seconds the ticker rests at each end before travelling back.")]
        public float tickerPause = 1.6f;

        [Header("Track-change flourish")]
        [Tooltip("Seconds of the slide-in + flash when the track changes.")]
        public float changeSeconds = 0.45f;
        [Tooltip("Canvas units the title slides in from on a track change.")]
        public float changeSlide = 26f;

        static readonly Color Ember = new Color(0.851f, 0.537f, 0.102f);   // #D9891A, the HUD's ember gold

        string cachedTitle = "";
        string cachedStation = "";
        int cachedIndex = -2, cachedCount = -1;
        float tickerTime;
        float changeAt = -99f;
        bool subscribed;
        Color titleBase = Color.white;

        void Awake()
        {
            if (titleText != null) titleBase = titleText.color;
        }

        void OnEnable()
        {
            if (LevelRadio.I != null) LevelRadio.I.OnTrackChanged += OnTrackChanged;
            Refresh(true);
        }

        void OnDisable()
        {
            if (LevelRadio.I != null) LevelRadio.I.OnTrackChanged -= OnTrackChanged;
            subscribed = false;
        }

        void OnTrackChanged()
        {
            changeAt = Time.unscaledTime;
            tickerTime = 0f;
            Refresh(false);
        }

        void Update()
        {
            var radio = LevelRadio.I;
            // A late-spawning Managers prefab: subscribe as soon as the singleton exists.
            if (radio != null && !subscribed) { radio.OnTrackChanged -= OnTrackChanged; radio.OnTrackChanged += OnTrackChanged; subscribed = true; }

            bool show = radio != null && radio.HasPlaylist;
            if (paneRoot != null && paneRoot.activeSelf != show) paneRoot.SetActive(show);
            if (!show || radio == null) return;

            Refresh(false);

            if (progressBar != null) progressBar.Set(radio.IsOn ? radio.Progress : 0f);

            // The ticker and the track-change flourish share one x on the title, so a change that lands
            // mid-scroll reads as one motion rather than two fighting for the same transform.
            if (titleText != null && titleViewport != null)
            {
                tickerTime += Time.unscaledDeltaTime;
                float viewW = titleViewport.rect.width;
                float textW = titleText.preferredWidth;
                float x = TickerOffset(textW, viewW, tickerTime, tickerSpeed, tickerPause);

                float k = changeSeconds > 0f ? (Time.unscaledTime - changeAt) / changeSeconds : 1f;
                if (k < 1f)
                {
                    float e = 1f - (1f - Mathf.Clamp01(k)) * (1f - Mathf.Clamp01(k));   // ease-out
                    x += changeSlide * (1f - e);
                    // A flash of ember on the way in: the same accent the pane's edge light warms to.
                    titleText.color = Color.Lerp(Ember, titleBase, e);
                }
                else if (titleText.color != titleBase) titleText.color = titleBase;

                var rt = titleText.rectTransform;
                var p = rt.anchoredPosition;
                if (!Mathf.Approximately(p.x, x)) rt.anchoredPosition = new Vector2(x, p.y);
            }
        }

        /// <summary>Rebuild the cached strings, but only the ones that actually changed.</summary>
        void Refresh(bool force)
        {
            var radio = LevelRadio.I;
            if (radio == null) return;

            string station = StationLine(radio.StationName);
            if (force || station != cachedStation)
            {
                cachedStation = station;
                if (stationText != null) stationText.text = station;
            }

            string title = radio.IsOn ? radio.TrackTitle : "PAUSED";
            if (force || title != cachedTitle)
            {
                cachedTitle = title;
                if (titleText != null) titleText.text = title;
            }

            int i = radio.TrackIndex, n = radio.TrackCount;
            if (force || i != cachedIndex || n != cachedCount)
            {
                cachedIndex = i; cachedCount = n;
                if (counterText != null) counterText.text = CounterLine(i, n);
            }
        }

        // ---- pure helpers, unit tested in RadioViewTests -------------------------------------------

        /// <summary>"THE HOLLOW ASCENT FM" — the level's display name as a station, upper case.</summary>
        public static string StationLine(string levelDisplayName)
        {
            if (string.IsNullOrWhiteSpace(levelDisplayName)) return "RADIO FM";
            return levelDisplayName.Trim().ToUpperInvariant() + " FM";
        }

        /// <summary>"TRACK 2/7", one-based; empty for an empty playlist.</summary>
        public static string CounterLine(int index, int count)
        {
            if (count <= 0 || index < 0) return "";
            return "TRACK " + (index + 1) + "/" + count;
        }

        /// <summary>
        /// The title's x inside its viewport at time <paramref name="t"/>: 0 while it fits, otherwise a
        /// ping-pong that rests <paramref name="pause"/> seconds at each end and travels at
        /// <paramref name="speed"/> units per second. Pure, so the motion is a test and not a squint.
        /// </summary>
        public static float TickerOffset(float textWidth, float viewWidth, float t, float speed, float pause)
        {
            float overflow = textWidth - viewWidth;
            if (overflow <= 0f || speed <= 0f) return 0f;
            if (pause < 0f) pause = 0f;
            float travel = overflow / speed;
            float cycle = (pause + travel) * 2f;
            if (cycle <= 0f) return 0f;
            float u = t % cycle;
            if (u < 0f) u += cycle;
            if (u < pause) return 0f;
            u -= pause;
            if (u < travel) return -speed * u;
            u -= travel;
            if (u < pause) return -overflow;
            u -= pause;
            return -(overflow - speed * u);
        }
    }
}
