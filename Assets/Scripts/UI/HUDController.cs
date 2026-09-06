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
        /// <summary>Wand cooldown. Fills on discharge and drains to empty as the wand comes back.</summary>
        public BarView wandCooldownBar;
        public BarView postureBar;
        /// <summary>Movement budget block (bar, ticks, ability pips). Self-driving; see StaminaView.</summary>
        public StaminaView staminaView;
        public TMP_Text deathblowText;
        public TMP_Text healthText;
        public TMP_Text flaskText;
        public TMP_Text soulsText;
        public TMP_Text timerText;
        public TMP_Text weaponText;
        public TMP_Text wandText;
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
        [Tooltip("The glass BEST RUNS pane (top-right). GhostHud writes its table into bestRunsText and shows " +
                 "the pane only once there is a board; it ships hidden.")]
        public GameObject bestRunsPane;
        public TMP_Text bestRunsText;
        public TMPro.TMP_Text pyreReadyLabel;
        public ItemSlotView[] itemSlots;
        public TMP_Text itemToastText;
        /// <summary>Top-left held-items / active-effects strip. Self-driving; see StatusStripView.</summary>
        public StatusStripView statusStrip;

        static readonly Color PostureBase = new Color(0.788f, 0.635f, 0.153f);   // #C9A227 bone/amber
        static readonly Color PostureDanger = new Color(1f, 0.227f, 0.102f);     // #FF3A1A

        int lastTimerCentis = -1;   // last value actually pushed to timerText; -1 forces the first write

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
            GameEvents.WandCooldownChanged += OnWandCooldown;
            GameEvents.FlaskChanged += OnFlask;
            GameEvents.SoulsChanged += OnSouls;
            GameEvents.WeaponChanged += OnWeapon;
            GameEvents.WandChanged += OnWand;
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
            GameEvents.WandCooldownChanged -= OnWandCooldown;
            GameEvents.FlaskChanged -= OnFlask;
            GameEvents.SoulsChanged -= OnSouls;
            GameEvents.WeaponChanged -= OnWeapon;
            GameEvents.WandChanged -= OnWand;
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
            if (wandCooldownBar != null) wandCooldownBar.Set(0f);
            if (deathblowText != null) deathblowText.alpha = 0f;
            if (itemToastText != null) itemToastText.alpha = 0f;
            if (postureBar != null) { postureBar.SetColor(PostureBase); postureBar.Set(0f); }
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
        }

        void OnPlayerPostureBroken()
        {
            ShowCenter("POSTURE BROKEN", PostureDanger, 1.2f);
            if (postureBar != null) postureBar.Flash(Color.white, 0.35f);
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

        void OnWandCooldown(float remaining, float total)
        {
            if (wandCooldownBar == null) return;
            wandCooldownBar.Set(total > 0f ? Mathf.Clamp01(remaining / total) : 0f);
        }

        void OnFlask(int c, int m)
        {
            if (flaskText != null) flaskText.text = $"FLASK  {c} / {m}";
        }

        void OnSouls(int s)
        {
            if (soulsText != null) soulsText.text = $"SOULS  {s}";
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
        void OnRespawned() { deathblowReady = false; if (postureBar != null) postureBar.Set(0f); ShowCenter("", Color.white, 0.01f); }
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


        // The wand is the riposte weapon (Bloodborne firearm analogue), so it gets its own line
        // under the melee weapon rather than sharing one.
        void OnWand(WandData w)
        {
            if (wandText == null) return;
            if (w == null) { wandText.text = ""; return; }
            // Plain ASCII: the default LiberationSans SDF atlas has no dingbats, and a missing glyph
            // renders as a hollow box.
            wandText.text = "WAND  " + w.displayName.ToUpperInvariant() + "   [R] cycle";
            wandText.color = ItemSlotView.Normalize(w.color);
        }
    }
}
