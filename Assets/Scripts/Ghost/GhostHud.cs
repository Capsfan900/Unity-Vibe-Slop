using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// Ghost racing HUD: the live delta and the leaderboard table.
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

        public bool BoardVisible { get; private set; } = true;

        void Awake()
        {
            Build();
        }

        void OnEnable()
        {
            if (Leaderboard.I != null) Leaderboard.I.Changed += RefreshBoard;
        }

        void OnDisable()
        {
            if (Leaderboard.I != null) Leaderboard.I.Changed -= RefreshBoard;
            if (hud != null && hud.bestRunsPane != null) hud.bestRunsPane.SetActive(false);
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
            // The HUD usually spawns after this canvas: while the board is still on the fallback text and
            // the HUD's pane turns up, move the table into the pane (HudPane re-tries once a second).
            if (BoardVisible && boardText != null && boardText.text.Length > 0 && HudPane() != null) RefreshBoard();
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

        HUDController hud;
        float hudLookupAt = -1f;

        /// <summary>The HUD's glass BEST RUNS pane, if the HUD prefab carries one. Looked up lazily and
        /// re-tried every second: the HUD may spawn after this canvas, and a scene change replaces it.</summary>
        HUDController HudPane()
        {
            if (hud == null && Time.unscaledTime - hudLookupAt > 1f)
            {
                hudLookupAt = Time.unscaledTime;
                hud = FindAnyObjectByType<HUDController>();
            }
            return hud != null && hud.bestRunsText != null ? hud : null;
        }

        public void RefreshBoard()
        {
            if (boardText == null) return;
            var pane = HudPane();
            if (!BoardVisible)
            {
                boardText.text = "";
                if (pane != null && pane.bestRunsPane != null) pane.bestRunsPane.SetActive(false);
                return;
            }

            var board = Leaderboard.I;
            sb.Length = 0;
            sb.AppendLine("<b>BEST RUNS</b>");

            if (board == null || board.Top.Length == 0)
            {
                sb.AppendLine("<alpha=#77>no runs yet - finish the level");
            }
            else
            {
                for (int i = 0; i < board.Top.Length; i++)
                {
                    var e = board.Top[i];
                    if (e == null) continue;
                    sb.Append(i == 0 ? "<b>" : "<alpha=#AA>");
                    sb.Append(i + 1).Append(". ").Append(SpeedrunTimer.Format(e.TimeSeconds));
                    if (e.deaths > 0) sb.Append("  <alpha=#77>x").Append(e.deaths);
                    if (e.verified) sb.Append(" <alpha=#99>[v]");
                    if (i == 0) sb.Append("</b>");
                    sb.AppendLine();
                }
            }
            if (pane != null)
            {
                // Into the HUD's glass pane: it carries its own BEST RUNS title, so the heading line goes.
                string s = sb.ToString();
                int nl = s.IndexOf('\n');
                pane.bestRunsText.text = nl >= 0 ? s.Substring(nl + 1) : s;
                if (pane.bestRunsPane != null) pane.bestRunsPane.SetActive(true);
                boardText.text = "";
            }
            else boardText.text = sb.ToString();
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
