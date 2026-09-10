using System.Text;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The top-left status strip: one line per HELD item (FIFO order, the front one marked as the one
    /// [E] fires) and one line per ACTIVE EFFECT with a countdown — "WALL SURGE  6.4s", "GOD MODE",
    /// "SPEED SURGE x3", plus the level's persistent run-soul / encounter requirement when configured.
    ///
    /// <para>Built by <c>HudBuilder</c>, referenced from <see cref="HUDController.statusStrip"/>.
    /// The item list arrives on <c>GameEvents.ItemsChanged</c>; the effects are read per frame from the
    /// player (the <see cref="StaminaView"/> idiom — only the motor knows how long a surge has left,
    /// and the countdown has to tick). One multi-line TMP label rather than a pool of row objects:
    /// the text is rebuilt only when what it would SHOW changes (tenths of a second, not the raw
    /// float), the same gate the speedrun timer uses, so an idle strip costs nothing per frame.</para>
    ///
    /// <para>Unscaled: the rows must still update while the world is frozen. Plain ASCII: the default
    /// SDF atlas has no dingbats, so the current-item marker is "&gt;", not a triangle.</para>
    /// </summary>
    public class StatusStripView : MonoBehaviour
    {
        public TMP_Text text;

        static readonly HashSet<StatusStripView> LiveViews = new HashSet<StatusStripView>();
        static bool statusEffectsVisible = true;

        /// <summary>
        /// Session-only developer preference for active-effect rows. Held-item rows deliberately ignore it:
        /// they are inventory state, not a temporary status effect.
        /// </summary>
        public static bool StatusEffectsVisible
        {
            get { return statusEffectsVisible; }
            set
            {
                if (statusEffectsVisible == value) return;
                statusEffectsVisible = value;
                foreach (var view in LiveViews) if (view != null) view.Rebuild();
            }
        }

        /// <summary>Ember gold — the shipped HUD's "ready / cost" colour, used for timed effects.</summary>
        static readonly Color EffectColor = new Color(0.878f, 0.627f, 0.188f);   // #E0A030
        /// <summary>Mint — banked run progress, matching the HUD's persistent progress language.</summary>
        static readonly Color RunColor = new Color(0.663f, 0.847f, 0.627f);      // #A9D8A0
        static readonly Color RunSecondaryColor = new Color(0.663f, 0.847f, 0.627f, 0.62f);
        /// <summary>Blood — god mode is a cheat and reads as one.</summary>
        static readonly Color GodColor = new Color(1f, 0.227f, 0.102f);          // #FF3A1A

        readonly StringBuilder sb = new StringBuilder(160);
        ItemData[] held = System.Array.Empty<ItemData>();
        FirstPersonMotor motor;
        ParrySurge parrySurge;
        LevelRunScorer runScorer;
        Health health;
        string shown = "";
        int rows;

        // What the label last showed, quantised the way it is printed.
        int shownSurgeTenths = -1;
        int shownParrySurgeStacks;
        bool shownGod;
        int shownSpeedPct = 100;
        int shownRunSouls = -1;
        int shownRequiredRunSouls;
        int shownRegularKills;
        int shownRequiredRegularKills;
        int shownCompletedSplits;
        int shownSplitCount;
        bool itemsDirty = true;

        /// <summary>Number of lines currently on screen. 0 means the strip is blank.</summary>
        public int RowCount => rows;
        public bool IsEmpty => rows == 0;
        /// <summary>The rich-text string on the label. Tests read it; nothing else should.</summary>
        public string Text => shown;

        void OnEnable()
        {
            LiveViews.Add(this);
            GameEvents.ItemsChanged += OnItems;
            GameEvents.RunScoreChanged += OnRunScoreChanged;
            itemsDirty = true;
        }

        void OnDisable()
        {
            LiveViews.Remove(this);
            GameEvents.ItemsChanged -= OnItems;
            GameEvents.RunScoreChanged -= OnRunScoreChanged;
        }

        void Start()
        {
            if (text != null) text.text = "";
            Rebuild();
        }

        void OnItems(ItemData[] items)
        {
            held = items ?? System.Array.Empty<ItemData>();
            itemsDirty = true;
        }

        void OnRunScoreChanged(LevelRunScorer scorer)
        {
            runScorer = scorer;
            itemsDirty = true;
            ObserveRunScore();
            Rebuild();
        }

        void ObserveRunScore()
        {
            if (runScorer == null) runScorer = LevelRunScorer.I;
            shownRunSouls = runScorer != null ? runScorer.EarnedSouls : -1;
            shownRequiredRunSouls = runScorer != null ? runScorer.RequiredSouls : 0;
            shownRegularKills = runScorer != null ? runScorer.RegularKills : 0;
            shownRequiredRegularKills = runScorer != null ? runScorer.RequiredRegularKills : 0;
            shownCompletedSplits = runScorer != null ? runScorer.CurrentSplitIndex : 0;
            shownSplitCount = runScorer != null && runScorer.definition != null && runScorer.definition.runSplits != null
                ? runScorer.definition.runSplits.Length : 0;
        }

        void Update()
        {
            if (motor == null)
            {
                motor = FindAnyObjectByType<FirstPersonMotor>();
                if (motor != null)
                {
                    health = motor.GetComponent<Health>();
                    parrySurge = motor.GetComponent<ParrySurge>();
                }
            }

            // ParrySurge is attached only after the first eligible Perfect, so retry while it
            // is absent. The strip observes its stack count; it never writes speed or surge state.
            if (motor != null && parrySurge == null) parrySurge = motor.GetComponent<ParrySurge>();

            int surgeTenths = motor != null && motor.IsWallSurging ? Mathf.CeilToInt(motor.WallSurgeRemaining * 10f) : -1;
            int parrySurgeStacks = parrySurge != null ? parrySurge.Stacks : 0;
            bool god = health != null && health.Invulnerable;
            int speedPct = motor != null ? Mathf.RoundToInt(motor.SpeedMultiplier * 100f) : 100;
            int oldRunSouls = shownRunSouls;
            int oldRegularKills = shownRegularKills;
            int oldCompletedSplits = shownCompletedSplits;
            ObserveRunScore();

            if (!itemsDirty && surgeTenths == shownSurgeTenths && parrySurgeStacks == shownParrySurgeStacks
                && god == shownGod && speedPct == shownSpeedPct && oldRunSouls == shownRunSouls
                && oldRegularKills == shownRegularKills && oldCompletedSplits == shownCompletedSplits) return;
            shownSurgeTenths = surgeTenths;
            shownParrySurgeStacks = parrySurgeStacks;
            shownGod = god;
            shownSpeedPct = speedPct;
            itemsDirty = false;
            Rebuild();
        }

        void Rebuild()
        {
            sb.Length = 0;
            rows = 0;

            for (int i = 0; i < held.Length; i++)
            {
                var item = held[i];
                if (item == null) continue;
                string hex = ColorUtility.ToHtmlStringRGB(ItemSlotView.Normalize(item.color));
                string name = string.IsNullOrEmpty(item.displayName) ? item.shortLabel : item.displayName;
                if (rows > 0) sb.Append('\n');
                // The front item is the one [E] fires; the rest queue behind it, dimmed.
                if (i == 0) sb.Append("<color=#").Append(hex).Append(">> ").Append(name.ToUpperInvariant()).Append("</color>");
                else sb.Append("<alpha=#8C><color=#").Append(hex).Append(">  ").Append(name.ToUpperInvariant()).Append("</color><alpha=#FF>");
                rows++;
            }

            if (shownRunSouls >= 0 && (shownRequiredRunSouls > 0 || shownRequiredRegularKills > 0 || shownSplitCount > 0))
            {
                if (rows > 0) sb.Append('\n');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(RunColor)).Append(">RUN ")
                  .Append(shownRunSouls).Append('/').Append(shownRequiredRunSouls).Append("</color>");
                rows++;
                sb.Append('\n');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(RunSecondaryColor)).Append(">FOES ")
                  .Append(shownRegularKills).Append('/').Append(shownRequiredRegularKills)
                  .Append("   SPLITS ").Append(shownCompletedSplits).Append('/').Append(shownSplitCount)
                  .Append("</color>");
                rows++;
            }

            if (!StatusEffectsVisible)
            {
                shown = sb.ToString();
                if (text != null) text.text = shown;
                return;
            }

            if (shownSurgeTenths >= 0)
            {
                if (rows > 0) sb.Append('\n');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(EffectColor)).Append(">WALL SURGE  ")
                  .Append(shownSurgeTenths / 10).Append('.').Append(shownSurgeTenths % 10).Append("s</color>");
                rows++;
            }

            if (shownParrySurgeStacks > 0)
            {
                if (rows > 0) sb.Append('\n');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(EffectColor)).Append(">SPEED SURGE x")
                  .Append(shownParrySurgeStacks).Append("</color>");
                rows++;
            }

            // Keep a truthful readout for a future non-surge speed source, but do not duplicate the
            // named stack row while ParrySurge owns the multiplier.
            if (shownSpeedPct != 100 && shownParrySurgeStacks <= 0)
            {
                if (rows > 0) sb.Append('\n');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(EffectColor)).Append(">SPEED x")
                  .Append((shownSpeedPct / 100f).ToString("0.##")).Append("</color>");
                rows++;
            }

            if (shownGod)
            {
                if (rows > 0) sb.Append('\n');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(GodColor)).Append(">GOD MODE</color>");
                rows++;
            }

            shown = sb.ToString();
            if (text != null) text.text = shown;
        }
    }
}
