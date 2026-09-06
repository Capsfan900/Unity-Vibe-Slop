using System.Collections;
using TMPro;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Subscribes to GameEvents and pushes values into the HUD widgets built by Editor/HudBuilder.</summary>
    public class HUDController : MonoBehaviour
    {
        public BarView healthBar;
        public BarView pyreBar;
        public BarView postureBar;
        /// <summary>Movement budget block (bar, ticks, ability pips). Self-driving; see StaminaView.</summary>
        public StaminaView staminaView;
        public TMP_Text deathblowText;
        public TMP_Text healthText;
        public TMP_Text flaskText;
        public TMP_Text soulsText;
        public TMP_Text timerText;
        public TMP_Text weaponText;
        public TMP_Text parryPopup;
        public TMP_Text centerText;

        [Header("Level clear")]
        [Tooltip("Scene loaded after a level is cleared. Build index 0 — see MainMenuBuilder.")]
        public string menuSceneName = "MainMenu";
        [Tooltip("Realtime seconds the LEVEL CLEAR screen stays up before returning to the menu, " +
                 "measured from when the Won state is set (2 s after the killing blow). Must stay " +
                 "under the clear message's own 8 s lifetime or the screen blanks before the load.")]
        public float returnToMenuSeconds = 4.5f;
        public TMP_Text hintText;
        [Tooltip("The glass BEST RUNS pane (top-right, under the radio). GhostHud writes its table into " +
                 "bestRunsText and shows the pane only once there is a board; it ships hidden AND collapsed.")]
        public GameObject bestRunsPane;
        public TMP_Text bestRunsText;
        [Tooltip("Everything in the BEST RUNS glass that is not a row: insets, the title band, the tail. " +
                 "Shipped by HudBuilder (hard rule 9) - the runtime cannot read an editor const.")]
        public float bestRunsChrome = 64f;
        [Tooltip("Height of one BEST RUNS row: 17 pt plus 4 pt line spacing.")]
        public float bestRunsRowHeight = 22f;
        [Tooltip("Most rows the board will ever send (Leaderboard.DisplayCount).")]
        public int bestRunsMaxRows = 8;
        [Tooltip("Unscaled seconds the board stays EXPANDED after it changes, then settles back to the " +
                 "personal best and a '+N MORE' line. There is no key for this on purpose: a bind nobody " +
                 "is told about is worse than a collapsed pane.")]
        public float bestRunsExpandSeconds = 4f;

        [Header("Souls")]
        [Tooltip("Floor on how fast the counter rolls toward the wallet, in souls per second; a big gain " +
                 "rolls proportionally faster so the number never crawls. Shipped by HudBuilder.")]
        public float soulsRollPerSecond = 24f;
        [Tooltip("Unscaled seconds the souls number stays punched and ember-warm after a gain.")]
        public float soulsFlashSeconds = 0.45f;
        [Tooltip("The glass RADIO pane (the top-right corner). RadioView shows it only while the level has " +
                 "a playlist; it ships hidden, and a level with no mp3s never sees a dead pane.")]
        public GameObject radioPane;
        public TMPro.TMP_Text pyreReadyLabel;
        public ItemSlotView[] itemSlots;
        public TMP_Text itemToastText;
        /// <summary>Top-left held-items / active-effects strip. Self-driving; see StatusStripView.</summary>
        public StatusStripView statusStrip;

        static readonly Color PostureBase = new Color(0.788f, 0.635f, 0.153f);   // #C9A227 bone/amber
        static readonly Color PostureDanger = new Color(1f, 0.227f, 0.102f);     // #FF3A1A
        // The souls pair, the same two hexes HudBuilder paints the pane with: mint at rest, ember for
        // the instant a gain lands. Both well under the 1.05 bloom cap - the UI never blooms.
        static readonly Color SoulsRest = new Color(0.663f, 0.847f, 0.627f);     // #A9D8A0 mint
        static readonly Color SoulsGain = new Color(0.851f, 0.537f, 0.102f);     // #D9891A ember

        int lastTimerCentis = -1;   // last value actually pushed to timerText; -1 forces the first write

        // ---- souls: the wallet, the number on screen, and the roll between them ----
        int soulsTarget;            // what the wallet actually holds
        int soulsShown;             // what the label is counting through
        int soulsWritten = -1;      // last value FORMATTED; -1 forces the first write
        float soulsCarry;           // sub-soul remainder of the roll
        float soulsFlash;           // seconds of gain flourish left
        RectTransform soulsRect;

        // ---- best runs: collapsed by default, expanded for a beat when the board changes ----
        string bestRunsBrief = "";  // the personal best plus "+N MORE"
        string bestRunsFull = "";   // every row the board sent
        float bestRunsExpandUntil = -1f;
        bool bestRunsExpanded;
        bool bestRunsDirty = true;
        int bestRunsRows = 1;       // rows in the text currently on screen; counted on a CHANGE, not per frame
        RectTransform bestRunsRect;
        Coroutine popup;
        Coroutine center;
        Coroutine toast;
        int perfectStreak;
        bool deathblowReady;
        ItemData pendingPickupFlash;
        string superName = "";

        void OnEnable()
        {
            GameEvents.PlayerPostureChanged += OnPlayerPosture;
            GameEvents.PlayerPostureBroken += OnPlayerPostureBroken;
            GameEvents.DeathblowReady += OnDeathblowReady;
            GameEvents.PlayerHealthChanged += OnHealth;
            GameEvents.PyreChanged += OnPyre;
            GameEvents.FlaskChanged += OnFlask;
            GameEvents.SoulsChanged += OnSouls;
            GameEvents.WeaponChanged += OnWeapon;
            GameEvents.ParryResolved += OnParry;
            GameEvents.PlayerDied += OnDied;
            GameEvents.PlayerRespawned += OnRespawned;
            GameEvents.CheckpointReached += OnCheckpoint;
            GameEvents.BossDefeated += OnBossDefeated;
            GameEvents.UltimateUsed += OnUltimate;
            GameEvents.BossStarted += OnBossStarted;
            GameEvents.ItemsChanged += OnItemsChanged;
            GameEvents.ItemPickedUp += OnItemPickedUp;
            GameEvents.ItemUsed += OnItemUsed;
        }

        void OnDisable()
        {
            GameEvents.PlayerPostureChanged -= OnPlayerPosture;
            GameEvents.PlayerPostureBroken -= OnPlayerPostureBroken;
            GameEvents.DeathblowReady -= OnDeathblowReady;
            GameEvents.PlayerHealthChanged -= OnHealth;
            GameEvents.PyreChanged -= OnPyre;
            GameEvents.FlaskChanged -= OnFlask;
            GameEvents.SoulsChanged -= OnSouls;
            GameEvents.WeaponChanged -= OnWeapon;
            GameEvents.ParryResolved -= OnParry;
            GameEvents.PlayerDied -= OnDied;
            GameEvents.PlayerRespawned -= OnRespawned;
            GameEvents.CheckpointReached -= OnCheckpoint;
            GameEvents.BossDefeated -= OnBossDefeated;
            GameEvents.UltimateUsed -= OnUltimate;
            GameEvents.BossStarted -= OnBossStarted;
            GameEvents.ItemsChanged -= OnItemsChanged;
            GameEvents.ItemPickedUp -= OnItemPickedUp;
            GameEvents.ItemUsed -= OnItemUsed;
        }

        void Start()
        {
            if (parryPopup != null) parryPopup.alpha = 0f;
            if (centerText != null) centerText.alpha = 0f;
            if (pyreReadyLabel != null) pyreReadyLabel.gameObject.SetActive(false);
            if (deathblowText != null) deathblowText.alpha = 0f;
            if (soulsText != null)
            {
                soulsRect = soulsText.rectTransform;
                soulsText.color = SoulsRest;
                soulsRect.localScale = Vector3.one;
            }
            if (bestRunsPane != null) bestRunsRect = bestRunsPane.GetComponent<RectTransform>();
            if (itemToastText != null) itemToastText.alpha = 0f;
            if (postureBar != null) { postureBar.SetColor(PostureBase); postureBar.Set(0f); postureBar.SetNearBreak(false); }
            ClearItemSlots();
            // No bind dump here any more: the reference is ControlsInfo, on the settings INFO card and the
            // F1 menu. hintText is one line, for contextual hints only.
            if (hintText != null) hintText.text = "";
        }

        void ClearItemSlots()
        {
            if (itemSlots == null) return;
            for (int i = 0; i < itemSlots.Length; i++)
                if (itemSlots[i] != null) itemSlots[i].Set(null, i == 0);
        }

        void OnItemsChanged(ItemData[] items)
        {
            if (itemSlots == null) return;
            for (int i = 0; i < itemSlots.Length; i++)
            {
                if (itemSlots[i] == null) continue;
                var item = items != null && i < items.Length ? items[i] : null;
                itemSlots[i].Set(item, i == 0);
            }

            // ItemPickedUp fires before the list broadcast, so the flash is applied here once the
            // slots actually hold the new item.
            if (pendingPickupFlash == null) return;
            for (int i = itemSlots.Length - 1; i >= 0; i--)
                if (itemSlots[i] != null && itemSlots[i].Item == pendingPickupFlash)
                {
                    itemSlots[i].Flash();
                    break;
                }
            pendingPickupFlash = null;
        }

        void OnItemPickedUp(ItemData item)
        {
            pendingPickupFlash = item;
            ShowItemToast(item);
        }

        void OnItemUsed(ItemData item)
        {
            if (itemSlots != null && itemSlots.Length > 0 && itemSlots[0] != null) itemSlots[0].Punch();
        }

        void ShowItemToast(ItemData item)
        {
            if (itemToastText == null || item == null) return;
            if (toast != null) StopCoroutine(toast);
            toast = StartCoroutine(ToastCo(item));
        }

        IEnumerator ToastCo(ItemData item)
        {
            string hex = ColorUtility.ToHtmlStringRGB(ItemSlotView.Normalize(item.color));
            string text = $"<size=42><b><color=#{hex}>{item.displayName.ToUpperInvariant()}</color></b></size>";
            if (!string.IsNullOrWhiteSpace(item.description))
                text += $"\n<size=20><alpha=#99>{item.description}</size>";
            itemToastText.text = text;

            const float seconds = 2.4f;
            float t = 0f;
            while (t < seconds)
            {
                float k = t / seconds;
                itemToastText.alpha = Mathf.Min(1f, k * 10f) * (1f - Mathf.Pow(k, 5f));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            itemToastText.alpha = 0f;
            toast = null;
        }

        void Update()
        {
            // The timer displays hundredths, so it only actually changes ~100x/sec — but this ran every
            // frame, allocating a formatted string AND forcing TMP to re-parse and re-mesh the label
            // even when the visible text was identical. Gate on the displayed value, not the raw float.
            if (timerText != null && SpeedrunTimer.I != null)
            {
                int centis = Mathf.Max(0, Mathf.FloorToInt(SpeedrunTimer.I.Elapsed * 100f));
                if (centis != lastTimerCentis)
                {
                    lastTimerCentis = centis;
                    timerText.text = SpeedrunTimer.Format(SpeedrunTimer.I.Elapsed);
                }
            }

            UpdateSouls();
            ApplyBestRuns(false);

            if (deathblowText != null)
            {
                if (deathblowReady)
                {
                    float k = Mathf.Sin(Time.unscaledTime * 9f);
                    deathblowText.alpha = 0.75f + 0.25f * k;
                    deathblowText.transform.localScale = Vector3.one * (1f + 0.05f * k);
                }
                else if (deathblowText.alpha > 0f)
                {
                    deathblowText.alpha = Mathf.MoveTowards(deathblowText.alpha, 0f, Time.unscaledDeltaTime * 5f);
                }
            }
        }

        void OnPlayerPosture(float c, float m)
        {
            if (postureBar == null) return;
            float r = m > 0f ? c / m : 0f;
            postureBar.Set(r);
            postureBar.SetColor(Color.Lerp(PostureBase, PostureDanger, Mathf.InverseLerp(0.7f, 1f, r)));
            // Same near-break beat the enemy world-space bar gives at 80% (EnemyPostureBar.NearBreakRatio) —
            // the player's own guard is about to break, which is at least as urgent a read as an enemy's.
            postureBar.SetNearBreak(r >= EnemyPostureBar.NearBreakRatio, BarView.NearBreakStrength(r, EnemyPostureBar.NearBreakRatio), EnemyPostureBar.NearBreakHz);
        }

        void OnPlayerPostureBroken()
        {
            ShowCenter("POSTURE BROKEN", PostureDanger, 1.2f);
            // A broken bar must show the BREAK read, never the near-break beat: mirror EnemyPostureBar's
            // `!broken` gate, or the flash ends and the bar keeps beating "one more deflect" while guard is gone.
            if (postureBar != null) { postureBar.SetNearBreak(false); postureBar.Flash(Color.white, 0.35f); }
        }

        void OnDeathblowReady(bool ready) => deathblowReady = ready;

        void OnHealth(float c, float m)
        {
            if (healthBar != null) healthBar.Set(m > 0 ? c / m : 0f);
            if (healthText != null) healthText.text = $"{Mathf.CeilToInt(c)} / {Mathf.CeilToInt(m)}";
        }

        void OnPyre(float v, float max)
        {
            if (pyreBar != null) pyreBar.Set(max > 0f ? v / max : 0f);
            bool full = max > 0f && v >= max - 0.01f;
            if (pyreReadyLabel == null) return;
            if (pyreReadyLabel.gameObject.activeSelf != full) pyreReadyLabel.gameObject.SetActive(full);
            // Names the super the player is actually holding. "PYRE FULL [Q]" tells you nothing about
            // whether Q is a sweep or an earthquake; SUNBREAK does.
            if (full) pyreReadyLabel.text = (superName.Length > 0 ? superName : "SUPER") + "  READY  [Q]";
        }

        void OnFlask(int c, int m)
        {
            if (flaskText != null) flaskText.text = $"FLASK  {c} / {m}";
        }

        /// <summary>
        /// The wallet changed. The label does NOT snap to it: a gain rolls up (see UpdateSouls), which
        /// is what makes a counted resource feel counted. A SPEND snaps, because a number that lingers
        /// above what the wallet holds is a lie the player would spend against.
        /// </summary>
        void OnSouls(int s)
        {
            if (s > soulsTarget) soulsFlash = soulsFlashSeconds;
            soulsTarget = s;
            if (s < soulsShown) { soulsShown = s; soulsCarry = 0f; }
        }

        /// <summary>
        /// Rolls the souls label toward the wallet and decays the gain flourish, on the unscaled clock
        /// so hitstop never freezes it. Allocation discipline is the run timer's: format ONLY when the
        /// displayed integer actually changes, never once per frame.
        /// </summary>
        void UpdateSouls()
        {
            if (soulsText == null) return;
            float dt = Time.unscaledDeltaTime;

            if (soulsShown != soulsTarget)
            {
                int gap = soulsTarget - soulsShown;
                soulsCarry += Mathf.Max(soulsRollPerSecond, gap * 4f) * dt;
                int step = Mathf.FloorToInt(soulsCarry);
                if (step > 0)
                {
                    soulsCarry -= step;
                    soulsShown = Mathf.Min(soulsTarget, soulsShown + step);
                }
            }
            else soulsCarry = 0f;

            if (soulsShown != soulsWritten)
            {
                soulsWritten = soulsShown;
                soulsText.text = soulsShown.ToString();
            }

            if (soulsFlash <= 0f) return;
            soulsFlash = Mathf.Max(0f, soulsFlash - dt);
            float k = soulsFlashSeconds > 0f ? soulsFlash / soulsFlashSeconds : 0f;
            soulsText.color = Color.Lerp(SoulsRest, SoulsGain, k);
            if (soulsRect != null) soulsRect.localScale = Vector3.one * (1f + 0.14f * k);
            if (soulsFlash > 0f) return;
            soulsText.color = SoulsRest;
            if (soulsRect != null) soulsRect.localScale = Vector3.one;
        }

        // ---- BEST RUNS ------------------------------------------------------------------------------

        /// <summary>
        /// The board in two readings: <paramref name="brief"/> is what the pane shows at rest (the
        /// personal best and a "+N MORE" line), <paramref name="full"/> is every row. Called by
        /// <see cref="GhostHud"/> whenever the leaderboard changes; a change EXPANDS the pane for
        /// <see cref="bestRunsExpandSeconds"/> and it settles back on its own. No new bind: the one
        /// moment the whole table answers a question is the moment a run lands on it.
        /// </summary>
        public void SetBestRuns(string brief, string full)
        {
            if (bestRunsText == null) return;
            brief = brief ?? "";
            full = full ?? "";
            bool changed = full != bestRunsFull;
            bestRunsBrief = brief;
            bestRunsFull = full;
            if (changed && full != brief) bestRunsExpandUntil = Time.unscaledTime + bestRunsExpandSeconds;
            bestRunsDirty = true;
            // A pane about to be shown for the first time must ARRIVE at its size rather than grow into
            // it from whatever the last board left behind.
            ApplyBestRuns(bestRunsPane == null || !bestRunsPane.activeSelf);
        }

        /// <summary>Lines in a rich-text block: the row count the glass is sized from.</summary>
        public static int LineCount(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 1;
            for (int i = 0; i < s.Length; i++) if (s[i] == '\n') n++;
            return n;
        }

        void ApplyBestRuns(bool snap)
        {
            if (bestRunsText == null) return;
            bool expanded = Time.unscaledTime < bestRunsExpandUntil;
            if (bestRunsDirty || expanded != bestRunsExpanded)
            {
                bestRunsExpanded = expanded;
                bestRunsDirty = false;
                string shown = expanded ? bestRunsFull : bestRunsBrief;
                bestRunsText.text = shown;
                // Sized from the data it holds: a two-run board is a two-row pane, never eight rows of
                // glass over empty space. Counted here, on the change, never once a frame.
                bestRunsRows = Mathf.Clamp(LineCount(shown), 1, Mathf.Max(1, bestRunsMaxRows));
            }

            if (bestRunsRect == null)
            {
                if (bestRunsPane == null) return;
                bestRunsRect = bestRunsPane.GetComponent<RectTransform>();
                if (bestRunsRect == null) return;
            }
            float target = BestRunsPaneHeight(bestRunsRows);
            var size = bestRunsRect.sizeDelta;
            if (Mathf.Abs(size.y - target) < 0.01f) return;
            size.y = snap ? target
                          : Mathf.MoveTowards(size.y, target,
                                              Mathf.Max(240f, Mathf.Abs(target - size.y) * 6f) * Time.unscaledDeltaTime);
            bestRunsRect.sizeDelta = size;
        }

        /// <summary>Height of the BEST RUNS glass holding <paramref name="rows"/> rows.</summary>
        public float BestRunsPaneHeight(int rows)
        {
            return bestRunsChrome + Mathf.Max(1, rows) * bestRunsRowHeight;
        }

        void OnWeapon(WeaponData w)
        {
            // Cached because the PYRE-full banner names the super, and the meter can fill long after
            // the last weapon swap.
            superName = w != null && !string.IsNullOrEmpty(w.superName) ? w.superName.ToUpperInvariant() : "";
            if (weaponText == null || w == null) return;
            weaponText.text = w.displayName.ToUpperInvariant();
            weaponText.color = w.neon.maxColorComponent > 1f ? w.neon / w.neon.maxColorComponent : w.neon;
        }

        void OnParry(ParryResult r)
        {
            string s = null; Color c = Color.white;
            switch (r)
            {
                case ParryResult.Perfect: perfectStreak++; s = perfectStreak > 1 ? $"PERFECT x{perfectStreak}" : "PERFECT"; c = new Color(0.3f, 1f, 1f); break;
                case ParryResult.Blocked: perfectStreak = 0; s = "BLOCK"; c = new Color(1f, 0.85f, 0.3f); break;
                case ParryResult.Hit: perfectStreak = 0; break;
            }
            if (s != null) ShowPopup(s, c);
        }

        void ShowPopup(string s, Color c)
        {
            if (parryPopup == null) return;
            if (popup != null) StopCoroutine(popup);
            popup = StartCoroutine(PopupCo(s, c));
        }

        IEnumerator PopupCo(string s, Color c)
        {
            parryPopup.text = s;
            parryPopup.color = c;
            float t = 0f;
            while (t < 0.6f)
            {
                float k = t / 0.6f;
                parryPopup.alpha = 1f - k * k;
                parryPopup.transform.localScale = Vector3.one * (1.4f - 0.4f * Mathf.Min(1f, k * 6f));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            parryPopup.alpha = 0f;
        }

        void ShowCenter(string s, Color c, float seconds)
        {
            if (centerText == null) return;
            if (center != null) StopCoroutine(center);
            center = StartCoroutine(CenterCo(s, c, seconds));
        }

        IEnumerator CenterCo(string s, Color c, float seconds)
        {
            centerText.text = s;
            centerText.color = c;
            float t = 0f;
            while (t < seconds)
            {
                float k = t / seconds;
                centerText.alpha = Mathf.Min(1f, k * 8f) * (1f - Mathf.Pow(k, 4f));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            centerText.alpha = 0f;
        }

        void OnDied() { perfectStreak = 0; deathblowReady = false; ShowCenter("YOU DIED", new Color(1f, 0.15f, 0.25f), 1.8f); }
        void OnRespawned() { deathblowReady = false; if (postureBar != null) { postureBar.Set(0f); postureBar.SetNearBreak(false); } ShowCenter("", Color.white, 0.01f); }
        void OnCheckpoint(Checkpoint c) { ShowCenter("CHECKPOINT", new Color(0.3f, 1f, 1f), 1.2f); }
        // Named after the weapon that fired it: SUNBREAK and THORNSTORM are different events.
        void OnUltimate() { ShowCenter(superName.Length > 0 ? superName : "SUPER", new Color(1f, 0.55f, 0.18f), 1.2f); if (pyreReadyLabel != null) pyreReadyLabel.gameObject.SetActive(false); }
        void OnBossStarted(BossController b) { ShowCenter(b != null && b.data != null ? b.data.displayName.ToUpperInvariant() : "BOSS", new Color(1f, 0.2f, 0.4f), 2f); }

        void OnBossDefeated()
        {
            deathblowReady = false;
            string time = SpeedrunTimer.I != null ? SpeedrunTimer.Format(SpeedrunTimer.I.Elapsed) : "";
            ShowCenter($"LEVEL CLEAR\n{time}", new Color(1f, 0.9f, 0.3f), 8f);
            if (GameManager.I != null) StartCoroutine(WinCo());
        }

        /// <summary>
        /// Clearing a level ends the run and RETURNS TO THE MENU.
        ///
        /// <para>It used to do neither. <c>GameState.Won</c> was set and then nothing in the project
        /// listened for it — no scene change, no prompt — and the cursor was explicitly re-LOCKED, so a
        /// cleared level left the player standing in a finished world with a hidden cursor and no way
        /// out but Alt-F4. The clear screen is the end of the loop, and a loop has to close.</para>
        ///
        /// <para>The wait is on REALTIME, because <c>Won</c> may stop the clock and a
        /// <c>WaitForSeconds</c> here would then never return — the same trap rule 1 exists for.</para>
        /// </summary>
        IEnumerator WinCo()
        {
            yield return new WaitForSecondsRealtime(2f);
            if (GameManager.I != null && GameManager.I.State == GameState.Playing) GameManager.I.SetState(GameState.Won);

            // Long enough to read the time off the clear screen and feel the win land, short enough
            // that it does not become a wait. Tuned against the 8 s ShowCenter above, which must
            // outlast this or the screen goes blank before the scene changes.
            yield return new WaitForSecondsRealtime(returnToMenuSeconds);

            // The menu is a mouse UI, so hand the cursor back. The level scene's GameManager re-locks
            // it on the way in, so this cannot leak into the next run.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // Time scale is owned by TimeScaleController (rule 1), but a scene load abandons it
            // mid-hitstop if the killing blow was one — so restore before leaving rather than
            // arriving at a menu running at 0.02x.
            if (TimeScaleController.I != null) TimeScaleController.I.ResetScale();
            AudioManager.Play(Sfx.Click);
            UnityEngine.SceneManagement.SceneManager.LoadScene(menuSceneName);
        }
    }
}
