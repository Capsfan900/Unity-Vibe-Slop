using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// The settings screen. One class serves BOTH the front end and the in-game pause path — the same
    /// prefab-built panel with the same rows, so a sensitivity changed in the menu and one changed
    /// mid-run cannot drift apart.
    ///
    /// <para><b>Rows are data.</b> Every row is a <see cref="Row"/> with a <see cref="RowKind"/>; the
    /// builder emits the widgets and this class binds behaviour by kind. Adding a setting is a new enum
    /// member plus a case in three switches, not a new panel.</para>
    ///
    /// <para><b>Nothing here reads input devices</b> (hard rule 2). Closing with ESC in a level goes
        /// through <c>InputReader.PausePressed</c>. The front-end carries a scene-local reader as well, so
        /// keybind listening and Escape cancellation use the same owner before and during a run.</para>
    ///
    /// <para><b>Nothing here writes Time.timeScale</b> (hard rule 1). Opening in a level takes a
    /// <c>TimeScaleController</c> handle and releases it on close. Every animation and readout uses
    /// unscaled time by construction (there is no animation; the panel is static).</para>
    ///
    /// <para><b>The pause menu is not edited to reach this.</b> <c>HudBuilder</c> parents a SETTINGS
    /// button under the pause panel and hands it to <see cref="openButton"/>; this class wires the
    /// listener. While open it disables the <see cref="PauseMenu"/> component so its own ESC handler
    /// cannot fire underneath us, and re-enables it on close.</para>
    /// </summary>
    public class SettingsMenu : MonoBehaviour
    {
        public enum RowKind
        {
            MouseSensitivity = 0,
            StickSensitivity = 1,
            FieldOfView = 2,
            Resolution = 3,
            DisplayMode = 4,
            VSync = 5,
            FrameCap = 6,
            Quality = 7,
            Bloom = 8,
            FilmGrain = 9,
            // Appended, never inserted: the enum's numbers are stable and the array below is screen
            // order. There is no SFX row on purpose — see SettingsData's volume block.
            MasterVolume = 10,
            MusicVolume = 11,
            /// <summary>Legacy serialized row id. Flourish now lives with every other action on the
            /// dedicated KEYBINDS page; keep the number stable for old prefabs until regeneration.</summary>
            WeaponTwirlKey = 12,
            /// <summary>Purely visual parkour reactions on the two first-person arms.</summary>
            ArmMovement = 13,
        }

        /// <summary>Every kind the builder must emit, in screen order. The EditMode test asserts on this.</summary>
        public static readonly RowKind[] AllKinds =
        {
            RowKind.MouseSensitivity, RowKind.StickSensitivity, RowKind.FieldOfView,
            RowKind.WeaponTwirlKey, RowKind.ArmMovement,
            RowKind.Resolution, RowKind.DisplayMode, RowKind.VSync, RowKind.FrameCap,
            RowKind.Quality, RowKind.Bloom, RowKind.FilmGrain,
            RowKind.MasterVolume, RowKind.MusicVolume,
        };

        [Serializable]
        public class Row
        {
            public RowKind kind;
            public GameObject root;
            public TMP_Text label;
            /// <summary>The authoritative readout. Always text — never a bar that could silently not draw.</summary>
            public TMP_Text value;
            public Button decrease;
            public Button increase;
            /// <summary>Continuous rows only; null on cyclers. Drives RectTransform anchors, never fillAmount.</summary>
            public Slider slider;
            public TMP_Text note;
        }

        [Serializable]
        public class BindingRow
        {
            public string bindingId;
            public GameObject root;
            public TMP_Text label;
            public TMP_Text value;
            public TMP_Text note;
            public Button rebind;
            public Button reset;
        }

        public static SettingsMenu I { get; private set; }

        [Header("Panel")]
        public GameObject panel;
        public Row[] rows;
        public Button backButton;
        public Button resetButton;

        [Header("KEYBINDS card - every ordinary player action")]
        public Button keybindButton;
        public GameObject keybindPanel;
        public BindingRow[] bindingRows;
        bool keybindOpen;

        [Header("INFO card - the key reference (ControlsInfo), one emitter for both prefabs")]
        public Button infoButton;
        public GameObject infoPanel;
        public TMP_Text infoText;
        bool infoOpen;

        [Header("Entry points")]
        [Tooltip("Button that opens this screen. Built into the title panel (front end) or the pause panel (in game).")]
        public Button openButton;

        [Tooltip("In-game only. Disabled while this panel is open so its ESC handler cannot fire under us.")]
        public PauseMenu pauseMenu;

        [Tooltip("Hidden while this panel is open and restored on close: the title panel in the front " +
                 "end, the pause panel in game (so settings replaces it rather than stacking on it).")]
        public GameObject hideWhileOpen;

        public bool IsOpen { get; private set; }

        /// <summary>Test hook: the live time handle, -1 when nothing is held.</summary>
        public int TimeHandle { get { return timeHandle; } }

        int timeHandle = -1;
        bool tookGameState;
        bool suppressedPause;
        bool reenablePauseQueued;
        bool wasHidden;
        bool building;

        Vector2Int[] resolutions = new Vector2Int[0];

        /// <summary>Unscaled time of the last volume audition click, so dragging a slider ticks rather
        /// than machine-guns. 0.09 s is a little over five frames at 60 fps.</summary>
        float lastAuditionAt = -99f;
        const float AuditionInterval = 0.09f;

        /// <summary>True between REBIND being pressed and a key arriving (or ESC cancelling). The row's
        /// value text says so; nothing else on the screen changes, so the panel cannot lie about which
        /// key is bound while it waits for the next one.</summary>
        bool listening;
        string listeningBindingId;

        /// <summary>Test hook and the row's own readout: the panel is waiting for a key.</summary>
        public bool IsListeningForKey { get { return listening; } }

        /// <summary>The prompt shown in the VALUE column while listening. One string, so the builder,
        /// the runtime and the test cannot drift.</summary>
        public const string ListeningLabel = "PRESS A KEY…";

        void Awake()
        {
            I = this;
            resolutions = SettingsApplier.DistinctResolutions(Screen.resolutions);
        }

        void OnDestroy() { StopListening(); if (I == this) I = null; }

        /// <summary>A panel torn down or disabled mid-listen must not leave InputReader hunting for a key.</summary>
        void OnDisable() { StopListening(); }

        void Start()
        {
            if (panel != null) panel.SetActive(false);
            if (openButton != null) openButton.onClick.AddListener(Open);
            if (backButton != null) backButton.onClick.AddListener(Close);
            if (resetButton != null) resetButton.onClick.AddListener(ResetToDefaults);
            if (infoButton != null) infoButton.onClick.AddListener(ToggleInfo);
            if (keybindButton != null) keybindButton.onClick.AddListener(ToggleKeybinds);
            if (infoPanel != null) infoPanel.SetActive(false);
            if (keybindPanel != null) keybindPanel.SetActive(false);
            BindRows();
            BindBindingRows();
        }

        void Update()
        {
            // Deferred one frame: PausePressed is WasPressedThisFrame, true for the WHOLE frame, so
            // re-enabling PauseMenu inside Close() would let the very ESC that closed this panel also
            // reach PauseMenu.Update in the same frame — whether it does depends on script execution
            // order, which is not a thing to depend on. One frame later the press is gone.
            if (reenablePauseQueued)
            {
                reenablePauseQueued = false;
                if (pauseMenu != null) pauseMenu.enabled = true;
            }

            if (!IsOpen) return;

            // While listening, ESC is the rebind's CANCEL, not the panel's close — InputReader's
            // rebinding operation consumes it and calls us back. Closing here as well would drop the
            // player out of settings for pressing the one key that means "never mind".
            if (listening) return;

            if (InputReader.I != null && InputReader.I.PausePressed) Close();
        }

        // ---- open / close -------------------------------------------------------------------------

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            // Pause, through the one owner of Time.timeScale. In the front end there is none, and
            // there is nothing running to pause either.
            if (TimeScaleController.I != null && timeHandle < 0)
                timeHandle = TimeScaleController.I.Request(0f);

            if (GameManager.I != null && GameManager.I.State == GameState.Playing)
            {
                GameManager.I.SetState(GameState.Paused);
                tookGameState = true;
            }

            // The cursor: a level locks it, and the front end may have been left in any state.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // A queued re-enable (closed and reopened within one frame) counts as "was enabled".
            if (pauseMenu != null && (pauseMenu.enabled || reenablePauseQueued))
            {
                pauseMenu.enabled = false;
                reenablePauseQueued = false;
                suppressedPause = true;
            }
            if (hideWhileOpen != null && hideWhileOpen.activeSelf) { hideWhileOpen.SetActive(false); wasHidden = true; }

            if (panel != null) panel.SetActive(true);
            ShowInfo(false);
            ShowKeybinds(false);
            Refresh();
            AudioManager.Play(Sfx.Click);
        }

        /// <summary>Open straight onto the INFO card (the F1 menu's INFO button).</summary>
        public void OpenInfo()
        {
            Open();
            ShowInfo(true);
        }

        public void ToggleInfo() { ShowInfo(!infoOpen); AudioManager.Play(Sfx.Click); }

        public void ToggleKeybinds() { ShowKeybinds(!keybindOpen); AudioManager.Play(Sfx.Click); }

        void ShowInfo(bool on)
        {
            if (on) ShowKeybinds(false);
            infoOpen = on;
            if (infoPanel != null) infoPanel.SetActive(on);
            // The card is glass: the rows would show through it and keep taking clicks, so they step
            // aside while it is up (rows, their section headers and the title rule), and come back after.
            if (rows != null)
                for (int i = 0; i < rows.Length; i++)
                    if (rows[i] != null && rows[i].root != null) rows[i].root.SetActive(!on);
            if (panel != null)
                foreach (Transform child in panel.transform)
                    if (child.name.EndsWith("Header") || child.name == "TitleRule") child.gameObject.SetActive(!on);
            if (infoText != null && on) infoText.text = ControlsInfo.Text;
            if (infoButton != null)
            {
                var label = infoButton.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = on ? "SETTINGS" : "INFO";
            }
        }

        void ShowKeybinds(bool on)
        {
            if (on && infoOpen) ShowInfo(false);
            keybindOpen = on;
            if (keybindPanel != null) keybindPanel.SetActive(on);
            SetGeneralRowsVisible(!on && !infoOpen);
            if (keybindButton != null)
            {
                var label = keybindButton.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = on ? "SETTINGS" : "KEYBINDS";
            }
            Refresh();
        }

        void SetGeneralRowsVisible(bool on)
        {
            if (rows != null)
                for (int i = 0; i < rows.Length; i++)
                    if (rows[i] != null && rows[i].root != null) rows[i].root.SetActive(on);
            if (panel != null)
                foreach (Transform child in panel.transform)
                    if (child.name.EndsWith("Header") || child.name == "TitleRule") child.gameObject.SetActive(on);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            StopListening();

            if (panel != null) panel.SetActive(false);

            if (timeHandle >= 0 && TimeScaleController.I != null) TimeScaleController.I.Release(timeHandle);
            timeHandle = -1;

            if (tookGameState && GameManager.I != null) GameManager.I.SetState(GameState.Playing);
            tookGameState = false;

            if (suppressedPause && pauseMenu != null) reenablePauseQueued = true;   // see Update
            suppressedPause = false;

            if (wasHidden && hideWhileOpen != null) hideWhileOpen.SetActive(true);
            wasHidden = false;

            AudioManager.Play(Sfx.Click, 1f, 0.8f);
        }

        public void ResetToDefaults()
        {
            SettingsStore.ResetToDefaults();
            Refresh();
            AudioManager.Play(Sfx.Click, 1f, 1.2f);
        }

        // ---- binding ------------------------------------------------------------------------------

        void BindRows()
        {
            if (rows == null) return;
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                var kind = row.kind;   // captured per row, not per loop variable

                if (row.decrease != null) row.decrease.onClick.AddListener(delegate { Step(kind, -1); });
                if (row.increase != null) row.increase.onClick.AddListener(delegate { Step(kind, +1); });
                if (row.slider != null) row.slider.onValueChanged.AddListener(delegate (float v) { SetContinuous(kind, v); });
            }
            Refresh();
        }

        void BindBindingRows()
        {
            if (bindingRows == null) return;
            for (int i = 0; i < bindingRows.Length; i++)
            {
                var row = bindingRows[i];
                if (row == null) continue;
                string id = row.bindingId;
                if (row.rebind != null) row.rebind.onClick.AddListener(delegate { BeginListening(id); });
                if (row.reset != null) row.reset.onClick.AddListener(delegate { ResetBinding(id); });
            }
        }

        /// <summary>Repaint every row from the live settings. Cheap; called on open and after any change.</summary>
        public void Refresh()
        {
            var d = SettingsStore.Current;
            building = true;
            try
            {
                if (rows == null) return;
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    if (row == null) continue;

                    if (row.value != null)
                        row.value.text = (listening && row.kind == RowKind.WeaponTwirlKey)
                            ? ListeningLabel
                            : ValueLabel(row.kind, d);

                    if (row.slider != null)
                    {
                        float lo, hi;
                        ContinuousRange(row.kind, out lo, out hi);
                        row.slider.minValue = lo;
                        row.slider.maxValue = hi;
                        row.slider.SetValueWithoutNotify(Continuous(row.kind, d));
                    }

                    bool usable = RowIsUsable(row.kind);
                    // The rebind row's two buttons are not symmetrical: RESET is pure data and always
                    // works, REBIND needs a live InputReader to listen with. Everywhere else both
                    // buttons share one verdict.
                    bool canListen = usable && InputReader.I != null && InputReader.I.HasWeaponTwirlAction;
                    if (row.decrease != null)
                        row.decrease.interactable = row.kind == RowKind.WeaponTwirlKey ? (canListen && !listening) : usable;
                    if (row.increase != null)
                        row.increase.interactable = row.kind == RowKind.WeaponTwirlKey ? !listening : usable;
                    if (row.slider != null) row.slider.interactable = usable;
                    if (row.note != null) row.note.text = NoteFor(row.kind, d);
                }

                if (bindingRows != null)
                {
                    for (int i = 0; i < bindingRows.Length; i++)
                    {
                        var row = bindingRows[i];
                        if (row == null) continue;
                        bool activeListen = listening && row.bindingId == listeningBindingId;
                        bool available = InputReader.I != null && InputReader.I.HasBinding(row.bindingId);
                        if (row.value != null) row.value.text = activeListen ? ListeningLabel
                            : available ? InputReader.I.BindingLabel(row.bindingId) : "UNAVAILABLE";
                        if (row.rebind != null) row.rebind.interactable = available && !listening;
                        if (row.reset != null) row.reset.interactable = available && !listening;
                        if (row.note != null) row.note.text = activeListen ? "esc cancels · ` reserved"
                            : InputReader.I == null ? "input unavailable" : "";
                    }
                }
            }
            finally { building = false; }
        }

        /// <summary>A frame-rate cap does nothing while vsync is on; say so rather than offer a dead control.</summary>
        bool RowIsUsable(RowKind kind)
        {
            if (kind == RowKind.FrameCap) return SettingsStore.Current.vSync <= 0;
            if (kind == RowKind.Resolution) return resolutions.Length > 0;
            return true;
        }

        string NoteFor(RowKind kind, SettingsData d)
        {
            if (kind == RowKind.FrameCap && d.vSync > 0) return "vsync is on";
            if (kind == RowKind.Resolution && resolutions.Length == 0) return "no display list";
            if (kind == RowKind.WeaponTwirlKey)
            {
                if (listening) return "esc cancels";
                if (InputReader.I == null) return "rebind in game";
                if (!InputReader.I.HasWeaponTwirlAction) return "action missing";
                return string.IsNullOrEmpty(d.weaponTwirlBinding) && string.IsNullOrEmpty(d.bindingOverridesJson)
                    ? "default" : "custom";
            }
            if (kind == RowKind.ArmMovement) return "visual only";
            // The two volume rows say what they actually reach, and admit a silent game rather than
            // leaving a player dragging a music slider that master has already muted.
            if (kind == RowKind.MasterVolume) return d.masterVolume <= 0.0001f ? "everything is muted" : "sfx and music";
            if (kind == RowKind.MusicVolume)
                return d.masterVolume <= 0.0001f ? "master is muted"
                     : d.musicVolume <= 0.0001f ? "music off" : "music and the radio";
#if UNITY_EDITOR
            if (kind == RowKind.Resolution || kind == RowKind.DisplayMode) return "applies in a build";
#endif
            return "";
        }

        // ---- the settings themselves ---------------------------------------------------------------

        public static void ContinuousRange(RowKind kind, out float lo, out float hi)
        {
            switch (kind)
            {
                case RowKind.MouseSensitivity: lo = SettingsData.MouseSensMin; hi = SettingsData.MouseSensMax; return;
                case RowKind.StickSensitivity: lo = SettingsData.StickSensMin; hi = SettingsData.StickSensMax; return;
                case RowKind.FieldOfView: lo = SettingsData.FovMin; hi = SettingsData.FovMax; return;
                case RowKind.Bloom: lo = SettingsData.BloomMin; hi = SettingsData.BloomMax; return;
                case RowKind.MasterVolume:
                case RowKind.MusicVolume: lo = SettingsData.VolumeMin; hi = SettingsData.VolumeMax; return;
                default: lo = 0f; hi = 1f; return;
            }
        }

        /// <summary>Step size for one press of &lt; or &gt; on a continuous row.</summary>
        public static float StepSize(RowKind kind)
        {
            switch (kind)
            {
                case RowKind.MouseSensitivity: return 0.005f;
                case RowKind.StickSensitivity: return 10f;
                case RowKind.FieldOfView: return 1f;
                case RowKind.Bloom: return 0.05f;
                case RowKind.MasterVolume:
                case RowKind.MusicVolume: return 0.05f;
                default: return 0f;
            }
        }

        /// <summary>
        /// A rebind row: no slider, and its two buttons are REBIND and RESET rather than &lt; and &gt;.
        /// The builder asks this to decide what to emit, so a second rebindable action later is one
        /// enum member and one case, not a new widget.
        /// </summary>
        public static bool IsRebind(RowKind kind)
        {
            return kind == RowKind.WeaponTwirlKey;
        }

        /// <summary>The two button captions on a rebind row, left then right.</summary>
        public static string RebindButtonLabel(bool isReset)
        {
            return isReset ? "RESET" : "REBIND";
        }

        public static bool IsContinuous(RowKind kind)
        {
            return kind == RowKind.MouseSensitivity || kind == RowKind.StickSensitivity
                || kind == RowKind.FieldOfView || kind == RowKind.Bloom
                || kind == RowKind.MasterVolume || kind == RowKind.MusicVolume;
        }

        static float Continuous(RowKind kind, SettingsData d)
        {
            switch (kind)
            {
                case RowKind.MouseSensitivity: return d.mouseSensitivity;
                case RowKind.StickSensitivity: return d.stickSensitivity;
                case RowKind.FieldOfView: return d.fieldOfView;
                case RowKind.Bloom: return d.bloomScale;
                case RowKind.MasterVolume: return d.masterVolume;
                case RowKind.MusicVolume: return d.musicVolume;
                default: return 0f;
            }
        }

        /// <summary>
        /// Write one continuous value into a settings object. Static and pure-ish (it only touches the
        /// object handed in), so an EditMode test can drive every row without a scene.
        /// </summary>
        public static void SetContinuous(SettingsData d, RowKind kind, float v)
        {
            if (d == null) return;
            switch (kind)
            {
                case RowKind.MouseSensitivity: d.mouseSensitivity = v; break;
                case RowKind.StickSensitivity: d.stickSensitivity = v; break;
                case RowKind.FieldOfView: d.fieldOfView = v; break;
                case RowKind.Bloom: d.bloomScale = v; break;
                case RowKind.MasterVolume: d.masterVolume = v; break;
                case RowKind.MusicVolume: d.musicVolume = v; break;
            }
            d.Clamp();
        }

        void SetContinuous(RowKind kind, float v)
        {
            if (building) return;
            var d = SettingsStore.Current;
            SetContinuous(d, kind, v);
            Commit();
            Audition(kind);
        }

        /// <summary>
        /// A volume row has to be HEARD, not read: <c>Commit</c> has already pushed the new gain onto
        /// <c>AudioManager</c> (the music bed changes on the same frame), and this is the SFX half of the
        /// answer — one click at the level just set, through the very bus being set. Throttled so a
        /// dragged slider ticks instead of machine-gunning, and no new sound: <see cref="Sfx.Click"/> is
        /// the sound this screen already makes.
        /// </summary>
        void Audition(RowKind kind)
        {
            if (kind != RowKind.MasterVolume && kind != RowKind.MusicVolume) return;
            if (Time.unscaledTime - lastAuditionAt < AuditionInterval) return;
            lastAuditionAt = Time.unscaledTime;
            AudioManager.Play(Sfx.Click, 1f, kind == RowKind.MusicVolume ? 0.85f : 1.1f, 0.02f);
        }

        /// <summary>
        /// Move one row by one notch. Static so it is unit-testable end to end: hand it a settings
        /// object and a direction and assert what came out — no UI, no scene, no play mode. Every
        /// discrete row wraps through <see cref="SettingsData.Cycle"/>.
        /// </summary>
        public static void Step(SettingsData d, RowKind kind, int delta, Vector2Int[] resolutionOptions, int qualityCount)
        {
            if (d == null || delta == 0) return;

            // A rebind row holds a control path, not a position in a list: there is nothing to step.
            // The RESET half is data, though, and belongs here so a test can prove it without a scene.
            if (IsRebind(kind))
            {
                if (delta > 0) d.weaponTwirlBinding = "";
                d.Clamp();
                return;
            }

            if (IsContinuous(kind))
            {
                SetContinuous(d, kind, Continuous(kind, d) + StepSize(kind) * delta);
                return;
            }

            switch (kind)
            {
                case RowKind.Resolution:
                    {
                        if (resolutionOptions == null || resolutionOptions.Length == 0) return;
                        // Index zero is a REAL, reachable NATIVE choice. Concrete display modes begin
                        // at one. The old version treated 0/0 as concrete option zero and immediately
                        // overwrote the sentinel, so one click could pin every future launch to a stale
                        // window size with no path back to native.
                        int i = 0;
                        if (d.screenWidth > 0 && d.screenHeight > 0)
                        {
                            int nearest = SettingsApplier.NearestResolutionIndex(d.screenWidth, d.screenHeight, resolutionOptions);
                            i = nearest < 0 ? 0 : nearest + 1;
                        }
                        i = SettingsData.Cycle(i, resolutionOptions.Length + 1, delta);
                        if (i == 0)
                        {
                            d.screenWidth = 0;
                            d.screenHeight = 0;
                        }
                        else
                        {
                            d.screenWidth = resolutionOptions[i - 1].x;
                            d.screenHeight = resolutionOptions[i - 1].y;
                        }
                        break;
                    }
                case RowKind.DisplayMode:
                    d.displayMode = (DisplayMode)SettingsData.Cycle((int)d.displayMode, 3, delta);
                    break;
                case RowKind.VSync:
                    d.vSync = SettingsData.Cycle(Mathf.Clamp(d.vSync, 0, 2), 3, delta);
                    break;
                case RowKind.FrameCap:
                    {
                        int i = SettingsData.IndexOf(SettingsData.FrameCaps, d.frameRateCap);
                        i = SettingsData.Cycle(i < 0 ? 0 : i, SettingsData.FrameCaps.Length, delta);
                        d.frameRateCap = SettingsData.FrameCaps[i];
                        break;
                    }
                case RowKind.Quality:
                    {
                        if (qualityCount <= 0) return;
                        int cur = d.qualityLevel < 0 ? QualitySettings.GetQualityLevel() : d.qualityLevel;
                        d.qualityLevel = SettingsData.Cycle(Mathf.Clamp(cur, 0, qualityCount - 1), qualityCount, delta);
                        break;
                    }
                case RowKind.FilmGrain:
                    d.filmGrain = !d.filmGrain;
                    break;
                case RowKind.ArmMovement:
                    d.armMovement = !d.armMovement;
                    break;
            }

            d.Clamp();
        }

        void Step(RowKind kind, int delta)
        {
            // REBIND is the only control on this screen that is not a value edit: it starts a listen.
            // Everything else, including its own RESET, goes through the pure static Step above.
            if (IsRebind(kind) && delta < 0) { BeginListening(); return; }
            if (IsRebind(kind)) { ResetBinding("Flourish"); return; }

            var names = QualitySettings.names;
            Step(SettingsStore.Current, kind, delta, resolutions, names != null ? names.Length : 0);
            Commit();
            Audition(kind);
        }

        // ---- rebinding ------------------------------------------------------------------------------

        /// <summary>
        /// Ask <see cref="InputReader"/> to listen for one key. Nothing here touches the Input System
        /// (hard rule 2): we hand it two callbacks and get a control-path STRING back, which is stored
        /// like any other setting and pushed back out by <c>SettingsApplier</c>.
        ///
        /// <para>Unscaled by construction — there is no timer on this side. The panel holds
        /// <c>Time.timeScale</c> at 0 through <c>TimeScaleController</c> while it is open (hard rule 1),
        /// and the rebinding operation runs on the Input System's own clock regardless.</para>
        /// </summary>
        void BeginListening()
        {
            BeginListening("Flourish");
        }

        void BeginListening(string bindingId)
        {
            var reader = InputReader.I;
            if (reader == null || !reader.HasBinding(bindingId) || listening) return;

            listening = true;
            listeningBindingId = bindingId;
            Refresh();
            AudioManager.Play(Sfx.Click, 1f, 1.3f);

            reader.BeginBindingRebind(bindingId,
                path =>
                {
                    listening = false;
                    listeningBindingId = null;
                    var d = SettingsStore.Current;
                    d.bindingOverridesJson = reader.ExportBindingOverrides();
                    d.weaponTwirlBinding = ""; // the complete override set supersedes the legacy one-row key
                    Commit();                       // clamps, saves, and re-applies through the applier
                    AudioManager.Play(Sfx.Click, 1f, 1.1f);
                },
                () =>
                {
                    listening = false;
                    listeningBindingId = null;
                    // Put the action back on whatever is actually SAVED: a cancelled listen must leave
                    // no trace, and the value text must never show a key the game will not answer to.
                    if (InputReader.I != null) InputReader.I.ApplyBindingOverrides(
                        SettingsStore.Current.bindingOverridesJson, SettingsStore.Current.weaponTwirlBinding);
                    Refresh();
                    AudioManager.Play(Sfx.Click, 1f, 0.8f);
                });
        }

        void ResetBinding(string bindingId)
        {
            var reader = InputReader.I;
            if (reader == null || listening) return;
            reader.ClearBindingOverride(bindingId);
            var d = SettingsStore.Current;
            d.bindingOverridesJson = reader.ExportBindingOverrides();
            if (bindingId == "Flourish") d.weaponTwirlBinding = "";
            Commit();
            AudioManager.Play(Sfx.Click, 1f, 0.9f);
        }

        /// <summary>Abandon a listen from this side (panel closing, object disabled).</summary>
        void StopListening()
        {
            if (!listening) return;
            listening = false;
            listeningBindingId = null;
            if (InputReader.I != null) InputReader.I.CancelRebind();
        }

        /// <summary>Persist and push. Saving on every notch is deliberate: a crash never loses a setting.</summary>
        void Commit()
        {
            SettingsStore.Save();     // fires SettingsStore.Changed -> SettingsApplier.ApplyAll
            Refresh();
        }

        // ---- labels ---------------------------------------------------------------------------------

        public static string LabelFor(RowKind kind)
        {
            switch (kind)
            {
                case RowKind.MouseSensitivity: return "MOUSE SENSITIVITY";
                case RowKind.StickSensitivity: return "GAMEPAD SENSITIVITY";
                case RowKind.FieldOfView: return "FIELD OF VIEW";
                case RowKind.Resolution: return "RESOLUTION";
                case RowKind.DisplayMode: return "DISPLAY MODE";
                case RowKind.VSync: return "VSYNC";
                case RowKind.FrameCap: return "FRAME RATE CAP";
                case RowKind.Quality: return "QUALITY";
                case RowKind.Bloom: return "BLOOM";
                case RowKind.FilmGrain: return "FILM GRAIN";
                case RowKind.MasterVolume: return "MASTER VOLUME";
                case RowKind.MusicVolume: return "MUSIC VOLUME";
                case RowKind.WeaponTwirlKey: return "FLOURISH KEY";
                default: return "ARM MOVEMENT";
            }
        }

        /// <summary>Static and total: every kind returns a non-empty string for any settings object.</summary>
        public static string ValueLabel(RowKind kind, SettingsData d)
        {
            if (d == null) return "—";
            switch (kind)
            {
                case RowKind.MouseSensitivity: return d.mouseSensitivity.ToString("0.000");
                case RowKind.StickSensitivity: return Mathf.RoundToInt(d.stickSensitivity) + " °/s";
                case RowKind.FieldOfView: return Mathf.RoundToInt(d.fieldOfView).ToString();
                case RowKind.Resolution: return SettingsData.ResolutionLabel(d.screenWidth, d.screenHeight);
                case RowKind.DisplayMode: return SettingsData.DisplayModeLabel(d.displayMode);
                case RowKind.VSync: return SettingsData.VSyncLabel(d.vSync);
                case RowKind.FrameCap: return SettingsData.FrameCapLabel(d.frameRateCap);
                case RowKind.Quality: return QualityLabel(d.qualityLevel);
                case RowKind.Bloom: return SettingsData.PercentLabel(d.bloomScale);
                case RowKind.FilmGrain: return SettingsData.OnOffLabel(d.filmGrain);
                case RowKind.MasterVolume: return SettingsData.PercentLabel(d.masterVolume);
                case RowKind.MusicVolume: return SettingsData.PercentLabel(d.musicVolume);
                case RowKind.ArmMovement: return SettingsData.OnOffLabel(d.armMovement);
                default:
                    // The live reader gives the nicest name ("F11"); with no reader (the EditMode test,
                    // the front end before a level) the stored path is decoded by the pure helper. Both
                    // read the SAME binding, so this row can never show a key the game will not answer.
                    return InputReader.I != null && InputReader.I.HasWeaponTwirlAction
                        ? InputReader.I.WeaponTwirlLabel
                        : SettingsData.KeyLabel(d.WeaponTwirlBindingOrDefault());
            }
        }

        static string QualityLabel(int level)
        {
            var names = QualitySettings.names;
            if (names == null || names.Length == 0) return level < 0 ? "DEFAULT" : level.ToString();
            int i = level < 0 ? QualitySettings.GetQualityLevel() : level;
            i = Mathf.Clamp(i, 0, names.Length - 1);
            return names[i].ToUpperInvariant();
        }
    }
}
