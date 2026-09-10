using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// Ghost racing HUD: the live delta and a debug-only screen leaderboard table.
    ///
    /// This builds its own Canvas at runtime rather than being authored into HUD.prefab. That keeps ghost
    /// racing entirely self-contained — it can be added or removed without touching HudBuilder or
    /// HUDController, and it cannot be broken by a HUD rebuild. Its sorting order sits below menus so a
    /// pause or level-up panel still covers it.
    /// </summary>
    public class GhostHud : MonoBehaviour
    {
        [Header("Placement")]
        public Vector2 deltaAnchoredPos = new Vector2(0f, -96f);   // under the run timer
        public Vector2 boardAnchoredPos = new Vector2(-40f, -140f); // top-right, under the hint text

        static readonly Color Ahead = new Color(0.55f, 0.95f, 0.60f);   // faster than PB
        static readonly Color Behind = new Color(0.96f, 0.45f, 0.38f);  // slower
        static readonly Color Muted = new Color(1f, 1f, 1f, 0.55f);

        TMP_Text deltaText;
        TMP_Text boardText;
        GhostPlayer ghost;
        readonly StringBuilder sb = new StringBuilder(512);

        /// <summary>
        /// The leaderboard table. Ships OFF (2026-09-07, the user's ask): BEST RUNS is a menu readout,
        /// not something a player reads at the crosshair mid-run, and the HUD's glass pane for it was
        /// removed in the same pass. The data and the table are still here - GhostRacing's
        /// "Ghost/Toggle Leaderboard Panel" context menu turns it on for debugging. The live ghost
        /// DELTA under the timer is separate and always on.
        /// </summary>
        public bool BoardVisible { get; private set; }

        void Awake()
        {
            Build();
        }

        /// <summary>The Leaderboard we are actually subscribed to, so the subscription is symmetric even
        /// when the singleton is replaced (a scene change) or arrives late (see <see cref="Update"/>).</summary>
        Leaderboard subscribed;

        void OnEnable()
        {
            TrySubscribe();
        }

        void OnDisable()
        {
            if (subscribed != null) { subscribed.Changed -= RefreshBoard; subscribed = null; }
        }

        /// <summary>
        /// Subscribe to whatever Leaderboard exists NOW, once. OnEnable alone was not enough: this canvas
        /// is built in Awake and the Leaderboard is a singleton on another object, so on any load order
        /// where it comes up second the subscription was silently skipped and the board never
        /// updated for the rest of the session. Update() re-tries, the same lazy-subscribe idiom
        /// RadioView uses for LevelRadio.
        /// </summary>
        void TrySubscribe()
        {
            var board = Leaderboard.I;
            if (board == subscribed) return;
            if (subscribed != null) subscribed.Changed -= RefreshBoard;
            subscribed = board;
            if (subscribed != null) { subscribed.Changed -= RefreshBoard; subscribed.Changed += RefreshBoard; RefreshBoard(); }
        }

        void Start()
        {
            ghost = FindAnyObjectByType<GhostPlayer>();
            RefreshBoard();
        }

        // ---- construction ---------------------------------------------------------------------------

        void Build()
        {
            var canvasGo = new GameObject("GhostHudCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Below the main HUD's menus so pause/level-up still cover this.
            canvas.sortingOrder = 5;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            deltaText = MakeText(canvasGo.transform, "GhostDelta", 30f,
                new Vector2(0.5f, 1f), new Vector2(320f, 40f), deltaAnchoredPos, TextAlignmentOptions.Center);
            deltaText.fontStyle = FontStyles.Bold;
            deltaText.alpha = 0f;

            boardText = MakeText(canvasGo.transform, "GhostBoard", 17f,
                new Vector2(1f, 1f), new Vector2(420f, 260f), boardAnchoredPos, TextAlignmentOptions.TopRight);
            boardText.color = Muted;
        }

        static TMP_Text MakeText(Transform parent, string name, float size, Vector2 anchor, Vector2 sizeDelta,
                                 Vector2 anchoredPos, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.richText = true;
            tmp.text = "";

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;
            return tmp;
        }

        // ---- live delta -----------------------------------------------------------------------------

        void Update()
        {
            TrySubscribe();
            if (deltaText == null) return;
            var timer = SpeedrunTimer.I;

            if (ghost == null || !ghost.HasRun || timer == null || !timer.Running || !ghost.HasDelta)
            {
                if (deltaText.alpha > 0f)
                    deltaText.alpha = Mathf.MoveTowards(deltaText.alpha, 0f, Time.unscaledDeltaTime * 4f);
                return;
            }

            // Positive means the player took longer to reach this point than the ghost did.
            float delta = timer.Elapsed - ghost.GhostTimeAtPlayer;
            deltaText.color = delta <= 0f ? Ahead : Behind;
            deltaText.text = (delta <= 0f ? "-" : "+") + Mathf.Abs(delta).ToString("0.00") + "s";
            deltaText.alpha = Mathf.MoveTowards(deltaText.alpha, 1f, Time.unscaledDeltaTime * 6f);
        }

        // ---- leaderboard ----------------------------------------------------------------------------

        /// <summary>One leaderboard row, in the HUD's language: rank, time, deaths, verified mark.</summary>
        static void AppendRow(StringBuilder b, int index, RunEntry e)
        {
            b.Append(index == 0 ? "<b>" : "<alpha=#AA>");
            b.Append(index + 1).Append(". ").Append(SpeedrunTimer.Format(e.TimeSeconds));
            if (e.deaths > 0) b.Append("  <alpha=#77>x").Append(e.deaths);
            if (e.verified) b.Append(" <alpha=#99>[v]");
            if (index == 0) b.Append("</b>");
        }

        /// <summary>
        /// Rebuilds the table on this canvas' own text. This is now the ONLY draw path: the HUD carries
        /// no BEST RUNS pane to hand a table to (removed 2026-09-07), so with <see cref="BoardVisible"/>
        /// false - the shipped state - nothing about the leaderboard reaches the screen.
        /// </summary>
        public void RefreshBoard()
        {
            if (boardText == null) return;
            if (!BoardVisible) { boardText.text = ""; return; }

            var board = Leaderboard.I;
            sb.Length = 0;
            int rows = 0;

            if (board == null || board.Top.Length == 0)
            {
                sb.Append("<alpha=#77>no runs yet - finish the level");
            }
            else
            {
                for (int i = 0; i < board.Top.Length; i++)
                {
                    var e = board.Top[i];
                    if (e == null) continue;
                    if (rows > 0) sb.Append('\n');
                    AppendRow(sb, rows, e);
                    rows++;
                }
                if (rows == 0) sb.Append("<alpha=#77>no runs yet - finish the level");
            }

            boardText.text = "<b>BEST RUNS</b>\n" + sb.ToString();
        }


        public void SetBoardVisible(bool value)
        {
            BoardVisible = value;
            RefreshBoard();
        }

        /// <summary>Big centre message when a run finishes. Kept here so it cannot collide with HUDController.</summary>
        public void ShowResult(string message, Color colour, float seconds = 4f)
        {
            StopAllCoroutines();
            StartCoroutine(ResultCo(message, colour, seconds));
        }

        System.Collections.IEnumerator ResultCo(string message, Color colour, float seconds)
        {
            if (deltaText == null) yield break;
            deltaText.color = colour;
            deltaText.text = message;
            deltaText.alpha = 1f;
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
            deltaText.alpha = 0f;
        }
    }
}
