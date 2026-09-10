using System.Text;
using TMPro;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Presentation-only world view of the existing local leaderboard. The generated Level 1 board owns
    /// the geometry and text; this component only binds that text to <see cref="Leaderboard"/>.
    /// </summary>
    public class WorldLeaderboardView : MonoBehaviour
    {
        [SerializeField] TMP_Text headingText;
        [SerializeField] TMP_Text rowsText;
        [SerializeField] TMP_Text footerText;
        [SerializeField] string levelDisplayName = "The Hollow Ascent";
        [SerializeField, Range(1, Leaderboard.DisplayCount)] int maxRows = Leaderboard.DisplayCount;
        [SerializeField] Vector2 boardSize = new Vector2(10f, 5f);
        [SerializeField] string backingMaterialKey = "Stone";
        [SerializeField] string glowMaterialKey = "NeonCyan";

        readonly StringBuilder buffer = new StringBuilder(768);
        Leaderboard subscribed;

        public int MaxRows { get { return maxRows; } }
        public Vector2 BoardSize { get { return boardSize; } }
        public string BackingMaterialKey { get { return backingMaterialKey; } }
        public string GlowMaterialKey { get { return glowMaterialKey; } }
        public TMP_Text HeadingText { get { return headingText; } }
        public TMP_Text RowsText { get { return rowsText; } }

        /// <summary>Called only by the regenerable level builder.</summary>
        public void Configure(string displayName, WorldLeaderboardDef definition,
                              TMP_Text heading, TMP_Text rows, TMP_Text footer)
        {
            levelDisplayName = string.IsNullOrWhiteSpace(displayName) ? "CURRENT LEVEL" : displayName;
            maxRows = definition != null
                ? Mathf.Clamp(definition.rowCount, 1, Leaderboard.DisplayCount)
                : Leaderboard.DisplayCount;
            boardSize = definition != null ? definition.size : new Vector2(10f, 5f);
            backingMaterialKey = definition != null ? definition.backingMaterialKey : "Stone";
            glowMaterialKey = definition != null ? definition.glowMaterialKey : "NeonCyan";
            headingText = heading;
            rowsText = rows;
            footerText = footer;
            RefreshBoard();
        }

        void OnEnable()
        {
            TrySubscribe();
            RefreshBoard();
        }

        void OnDisable()
        {
            if (subscribed != null) subscribed.Changed -= RefreshBoard;
            subscribed = null;
        }

        void Update()
        {
            // GhostRacing installs the model after scene load, so the board must tolerate either order.
            TrySubscribe();
        }

        void TrySubscribe()
        {
            var current = Leaderboard.I;
            if (current == subscribed) return;
            if (subscribed != null) subscribed.Changed -= RefreshBoard;
            subscribed = current;
            if (subscribed != null)
            {
                subscribed.Changed -= RefreshBoard;
                subscribed.Changed += RefreshBoard;
                RefreshBoard();
            }
        }

        public void RefreshBoard()
        {
            if (headingText != null)
                headingText.text = "LOCAL BEST RUNS\n<size=58%><color=#9BCBD2>" +
                                   levelDisplayName.ToUpperInvariant() + "</color></size>";
            if (footerText != null)
                footerText.text = "YOUR FASTEST FINISHES  •  SAVED ON THIS DEVICE";
            if (rowsText != null)
                rowsText.text = FormatRows(subscribed != null ? subscribed.Top : null, maxRows);
        }

        /// <summary>Pure table formatter kept public so the empty and capped states can be regression-tested.</summary>
        public static string FormatRows(RunEntry[] entries, int requestedRows)
        {
            if (entries == null || entries.Length == 0)
                return "<color=#77939A>NO RUNS YET\nFINISH THE LEVEL TO SET THE FIRST TIME</color>";

            int limit = Mathf.Clamp(requestedRows, 1, Leaderboard.DisplayCount);
            var result = new StringBuilder(640);
            int shown = 0;
            for (int i = 0; i < entries.Length && shown < limit; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;
                if (shown > 0) result.Append('\n');

                result.Append(shown == 0 ? "<color=#FFFFFF><b>" : "<color=#B9DDE2>");
                result.Append((shown + 1).ToString("00"));
                result.Append("     ").Append(SpeedrunTimer.Format(entry.TimeSeconds));
                result.Append("     ").Append(entry.deaths).Append(entry.deaths == 1 ? " DEATH" : " DEATHS");
                if (entry.verified) result.Append("     VERIFIED");
                result.Append(shown == 0 ? "</b></color>" : "</color>");
                shown++;
            }

            if (shown == 0)
                return "<color=#77939A>NO RUNS YET\nFINISH THE LEVEL TO SET THE FIRST TIME</color>";
            return result.ToString();
        }
    }
}
