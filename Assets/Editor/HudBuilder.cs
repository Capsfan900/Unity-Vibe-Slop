using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Builds Assets/Prefabs/HUD.prefab (canvas + menus + EventSystem) from code. Idempotent.
    ///
    /// <para><b>The look (2026-09-04): panes of smoked glass, edge-lit.</b> Three panes on the HUD —
    /// the vitals (bottom-left), the loadout (top-left) and the clock (a pill, top-centre) — each a
    /// different shape so they never read as one repeated card, plus glass tiles for the items and a
    /// glass card behind every menu. Type is one family on one scale (11 / 12 / 15 / 16 / 20 / 22 / 30
    /// / 32 / 40+), values in bone, labels quieter than values, every offset a multiple of 8. Nothing
    /// on the HUD is brighter than 1.0: the UI never blooms, because light in this game means "you
    /// deflected". Sprites come from <see cref="UiSprites"/>; a fluid-bar and a Pyre-fire pass restyle
    /// the three named bars afterwards through <see cref="HudExtensions"/>.</para>
    /// </summary>
    public static class HudBuilder
    {
        const string PrefabPath = "Assets/Prefabs/HUD.prefab";

        // Dark fantasy palette (names kept for layout code): ghost teal, ember gold, bone, blood.
        static readonly Color Cyan = Hex("#7FBFB5");      // ghost teal (weapon name, accents)
        static readonly Color Pink = Hex("#D9891A");      // ember gold (the Pyre meter)
        static readonly Color Yellow = Hex("#E0A030");    // gold (flask, ready label, costs)
        static readonly Color Dark = Hex("#06040A");      // panel / bar backgrounds
        static readonly Color Mint = Hex("#A9D8A0");      // souls
        static readonly Color HealthFill = Hex("#B41E2E"); // blood
        static readonly Color BossRed = Hex("#8A1020");
        static readonly Color ButtonBg = Hex("#1A1220");
        static readonly Color PostureFill = Hex("#C9A227"); // bone/amber
        static readonly Color Blood = Hex("#FF3A1A");
        static readonly Color Bone = Hex("#E8E2D6");      // values and titles: warm off-white, never pure white

        // ---- glass ---------------------------------------------------------------------------------
        // The HUD is panes of smoked glass edge-lit by the eclipse. UGUI has no blur; the look is a
        // dark rounded pane, a soft shadow under it, a faint sheen across its upper third and a
        // one-pixel light along its top edge that fades in from ember on the left (UiSprites). Alphas
        // are LINEAR-space (see MainMenuBuilder's backdrop note): a dark tint composites weaker than
        // its number suggests, a bright one stronger, which is why the pane is 0.58 and the sheen 0.035.
        static readonly Color PaneTint = Hex("#0B0710");   // smoked void
        const float PaneAlpha = 0.58f;
        const float SheenAlpha = 0.035f;
        const float EdgeAlpha = 0.30f;
        const float ShadowAlpha = 0.42f;
        const float TrackAlpha = 0.45f;                     // the dark well a bar sits in, over the pane
        const float Inset = 16f;                            // pane padding; every offset is a multiple of 8

        static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        static readonly Vector2 BottomCenter = new Vector2(0.5f, 0f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        // ---- the top-right column (2026-09-06, BEST RUNS removed 2026-09-07) ----
        // ONE column in the corner, top to bottom: the radio, then the hint line, then the level-editor
        // panel. There is NO BEST RUNS pane any more (the user's ask, 2026-09-07): a leaderboard is a
        // menu readout, not something a player reads at the crosshair mid-run, and GhostHud ships its
        // board hidden so neither draw path puts a table on screen.
        // The BestRuns* constants below survive ONLY as the column anchor the hint line and the F10
        // editor panel hang off — BestRunsBottom is still -272, the y those two were tuned to. They no
        // longer size a pane; do not re-tune them, HudColumnTests pins them.
        /// <summary>The top-left loadout pane: the weapon line and the souls line, nothing else.</summary>
        public const float LoadoutWidth = 360f;
        public const float LoadoutHeight = 96f;
        public const float RadioWidth = 300f;
        public const float RadioHeight = 116f;
        /// <summary>Gap between the panes of the top-right column.</summary>
        public const float ColumnGap = 16f;
        /// <summary>Historic BEST RUNS row height. Kept only so the column anchor below still computes.</summary>
        public const float BestRunsRowHeight = 22f;
        /// <summary>Historic BEST RUNS chrome: insets, the title band, a 6 px tail.</summary>
        public const float BestRunsChrome = Inset * 2f + 26f + 6f;
        /// <summary>Historic collapsed height. This is the gap the column reserves under the radio.</summary>
        public const float BestRunsCollapsedHeight = BestRunsChrome + 2f * BestRunsRowHeight;
        /// <summary>Historic expanded height. No pane reaches it any more; kept for the geometry tests.</summary>
        public const float BestRunsHeight = BestRunsChrome + Leaderboard.DisplayCount * BestRunsRowHeight;
        /// <summary>Canvas x of the corner column: the radio's right edge.</summary>
        public const float BestRunsX = -32f;
        /// <summary>Canvas y under the radio's lower edge, one gap down.</summary>
        public const float BestRunsTop = -32f - RadioHeight - ColumnGap;
        /// <summary>THE COLUMN ANCHOR (-272): the hint line and the F10 editor panel hang off this.</summary>
        public const float BestRunsBottom = BestRunsTop - BestRunsCollapsedHeight;
        /// <summary>Historic expanded lower edge. Nothing is drawn here any more.</summary>
        public const float BestRunsExpandedBottom = BestRunsTop - BestRunsHeight;

        // ---- the flow meter (2026-09-07, the user's ask) ----
        // The band the BEST RUNS pane left free carries the two run numbers instead: the SPEED BOOST
        // (stacks + the live speed) and the PARRY CHAIN. It fits the band EXACTLY — same x, same width
        // as the radio, top at BestRunsTop, bottom at BestRunsBottom — so the hint line and the F10
        // panel below keep the y they were tuned to and the column stays one column.
        /// <summary>Flow-meter pane width: the radio's, so the corner reads as one stack of glass.</summary>
        public const float FlowWidth = RadioWidth;
        /// <summary>Flow-meter pane height: exactly the band, top BestRunsTop to bottom BestRunsBottom.</summary>
        public const float FlowHeight = BestRunsCollapsedHeight;
        /// <summary>Stack pips drawn. Five: the shipped turret's parrySurgeMaxStacks (DataFactory).</summary>
        public const int FlowStackPips = 5;
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        [MenuItem("VibeGame1/5. Build HUD")]
        public static void Build()
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Prefabs"));

            var root = new GameObject("HUD");
            try
            {
                BuildInternal(root);
                // Styling passes that live in their own files (fluid bars, the Pyre fire): each one
                // rewrites a finished bar in place, so the layout above never has to know about them.
                HudExtensions.ApplyAll(root);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[HudBuilder] Saved {PrefabPath}", prefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void BuildInternal(GameObject root)
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            var hud = root.AddComponent<HUDController>();

            // Hard rule 9: written here so they are SHIPPED on the prefab rather than left to a field
            // initialiser that a rebuilt prefab would freeze at whatever it said that day.
            // Clearing a level returns to the menu — MainMenuBuilder puts it at build index 0.
            hud.menuSceneName = "MainMenu";
            hud.returnToMenuSeconds = 4.5f;
            var prompt = root.AddComponent<PromptView>();
            var flash = root.AddComponent<ScreenFlash>();
            var pause = root.AddComponent<PauseMenu>();
            var levelUp = root.AddComponent<LevelUpMenu>();
            var bossBar = root.AddComponent<BossBarView>();
            var testMenu = root.AddComponent<TestMenu>();
            var wandMenu = root.AddComponent<WandSelectMenu>();
            var settings = root.AddComponent<SettingsMenu>();
            var levelEditor = root.AddComponent<LevelEditor>();

            Transform t = root.transform;

            // ---------------- Bottom-left: the vitals pane ----------------
            // One pane of smoked glass holds every number a run is spent on — health, stamina, posture,
            // the Pyre, the flask — top to bottom in the order you glance at them, on an 8 px rhythm.
            // Bars share one left edge and one width; the numerals and the pips live in a column to
            // their right, so the eye reads a bar and its reading on one line.
            const float PaneW = 520f, PaneH = 148f, BarW = 352f, ColX = Inset + BarW + 12f, ColW = PaneW - ColX - Inset;
            var bl = Pane("Vitals", t, BottomLeft, BottomLeft, BottomLeft, new Vector2(32f, 32f), new Vector2(PaneW, PaneH));

            // "SUPER READY" floats above the pane: it is only ever on screen when there is something
            // to say, so it does not earn a permanent slot in the glass.
            var ready = Txt("PyreReadyLabel", t, "SUPER  READY  [Q]", 18f, Yellow, TextAlignmentOptions.Left);
            ready.fontStyle = FontStyles.Bold;
            ready.characterSpacing = 2f;
            Rect(ready.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(32f + Inset, 32f + PaneH + 10f), new Vector2(420f, 24f));
            hud.pyreReadyLabel = ready;

            // Row 1 — health. The tallest bar, ghosted so a hit leaves a trace; the numerals in bone.
            hud.healthBar = Bar("HealthBar", bl, HealthFill, true, false);
            Rect(hud.healthBar.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(Inset, 116f), new Vector2(BarW, 16f));

            hud.healthText = Txt("HealthText", bl, "100 / 100", 20f, Bone, TextAlignmentOptions.Right);
            Rect(hud.healthText.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(ColX, 112f), new Vector2(ColW, 24f));

            // Row 2 — STAMINA: the movement budget in dash-sized ticks, its label above it (the label
            // doubles as the refusal message) and the three ability pips in the column.
            hud.staminaView = StaminaBlock(bl, Inset, 84f, BarW, ColX - Inset);

            // Row 3 — posture (Sekiro-style): fills when you block or get hit, breaks -> stagger. A
            // 6 px amber hairline: the quietest bar because it is the one you least want to be
            // watching mid-fight; it goes loud on its own (BarView pulses it when full).
            hud.postureBar = Bar("PostureBar", bl, PostureFill, false, true);
            Rect(hud.postureBar.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(Inset, 64f), new Vector2(BarW, 6f));

            // Row 4 — PYRE, the parry charge meter, in the same ember as the fire on the weapon
            // because the bar and the fire are the same resource. Its name sits in the column.
            hud.pyreBar = Bar("PyreBar", bl, Pink, true, true);
            Rect(hud.pyreBar.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(Inset, 40f), new Vector2(BarW, 10f));

            var pyreLabel = Txt("PyreLabel", bl, "PYRE", 12f, new Color(Pink.r, Pink.g, Pink.b, 0.8f), TextAlignmentOptions.Right);
            pyreLabel.characterSpacing = 3f;
            Rect(pyreLabel.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(ColX, 37f), new Vector2(ColW, 16f));

            // Row 5 — the flask count, along the pane's bottom edge.
            hud.flaskText = Txt("FlaskText", bl, "FLASK  3 / 3", 15f, Yellow, TextAlignmentOptions.Left);
            hud.flaskText.characterSpacing = 2f;
            Rect(hud.flaskText.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(Inset, 14f), new Vector2(BarW, 20f));

            // ---------------- Bottom-centre: single-use item slots ----------------
            // Three glass tiles. No caption: a row of three framed squares above the crosshair reads as
            // slots without being told, and the key hint appears on the front slot when it has an item.
            var itemsRoot = Group("ItemSlots", t, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 32f), new Vector2(304f, 96f));

            hud.itemSlots = new ItemSlotView[3];
            for (int i = 0; i < hud.itemSlots.Length; i++)
                hud.itemSlots[i] = ItemSlot("ItemSlot" + i, itemsRoot, (i - 1) * 104f);

            // ---------------- Top-left: the loadout pane ----------------
            // Two lines: the weapon you are holding, and the souls you are carrying. The wand's name and
            // its cooldown hairline used to live between them (2026-09-06: removed at the user's ask) —
            // the wand is a riposte weapon, so the only moment its cooldown matters is the moment the
            // execute prompt is up, and ExecuteInteractor.cs:95 already prints
            // "DEATHBLOW [ATTACK] WAND 2.0s" at the crosshair right then. A permanent top-left readout
            // for it was a second, quieter copy of that answer in the wrong place. The pane closes to
            // 96 tall so removing the rows leaves no hole.
            var tl = Pane("Loadout", t, TopLeft, TopLeft, TopLeft, new Vector2(32f, -32f), new Vector2(LoadoutWidth, LoadoutHeight));

            hud.weaponText = Txt("WeaponText", tl, "SWORD", 30f, Bone, TextAlignmentOptions.Left);
            hud.weaponText.fontStyle = FontStyles.Bold;
            hud.weaponText.characterSpacing = 2f;
            Rect(hud.weaponText.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -10f), new Vector2(320f, 38f));

            // Souls, as a counted resource rather than a sentence: a quiet 12 pt label in bone and the
            // number itself big, in mint, on the same line — the pane's label/value pair, the same one
            // the vitals and the radio use. HUDController rolls the number up on a gain and flashes it
            // toward ember; the label never moves, so the eye reads "SOULS" once and the digits after.
            var soulsLabel = Txt("SoulsLabel", tl, "SOULS", 12f, new Color(Bone.r, Bone.g, Bone.b, 0.45f), TextAlignmentOptions.Left);
            soulsLabel.characterSpacing = 6f;
            Rect(soulsLabel.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -54f), new Vector2(64f, 28f));

            hud.soulsText = Txt("SoulsText", tl, "0", 26f, Mint, TextAlignmentOptions.Left);
            hud.soulsText.fontStyle = FontStyles.Bold;
            hud.soulsText.characterSpacing = 2f;
            // Pivot on the left edge, vertically centred on the label's line: the gain punch then scales
            // the digits about that point instead of shoving them down and right out of the pane.
            Rect(hud.soulsText.gameObject, TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(Inset + 72f, -68f), new Vector2(240f, 32f));
            // Hard rule 9: the roll and the flash are SHIPPED on the prefab, not field initialisers.
            hud.soulsRollPerSecond = 24f;
            hud.soulsFlashSeconds = 0.45f;

            // Held items and active effects, under the loadout pane. A strip, not a pane: one small
            // line per row, blank when idle, rows appear and vanish with state (StatusStripView).
            // Its y follows the pane's lower edge one gap down, so shortening the pane closes the gap
            // instead of leaving the strip floating where the wand rows used to be.
            hud.statusStrip = StatusStrip(t, -(32f + LoadoutHeight + ColumnGap));

            // ---------------- Top-center: the timer pill ----------------
            // The run clock, alone in a pill: the one piece of glass with fully rounded ends, so it is
            // never mistaken for a pane of readings.
            var tc = Pane("Clock", t, TopCenter, TopCenter, TopCenter, new Vector2(0f, -24f), new Vector2(300f, 52f), UiSprites.Pill(), 22f);
            hud.timerText = Txt("TimerText", tc, "00:00.00", 32f, Bone, TextAlignmentOptions.Center);
            hud.timerText.fontStyle = FontStyles.Bold;
            hud.timerText.characterSpacing = 5f;
            Stretch(hud.timerText.gameObject);

            // ---------------- Top-right: NO BEST RUNS pane (removed 2026-09-07) ----------------
            // The glass leaderboard that used to sit here is gone at the user's ask. A table of times is
            // a menu readout; it never answered a question a player has while looking at the crosshair,
            // and a pane nobody can reach is exactly the dead glass this file's own rules forbid. The
            // leaderboard data is untouched (Leaderboard + GhostHud), and GhostHud ships BoardVisible
            // false so its fallback text does not draw the old loose block in the pane's place. The
            // BestRuns* constants above remain ONLY as the column anchor for the hint line and the F10
            // editor panel, both positioned off BestRunsBottom below.

            // ---------------- Top-right corner: THE RADIO ----------------
            // A 2000s racing-game stereo in the HUD's glass: the station (the level's name, as
            // "THE HOLLOW ASCENT FM"), the track title on a ticker when it will not fit, "TRACK 2/7",
            // a one-pixel-thin ember progress line and the three keys. LevelRadio owns the audio and
            // the input; RadioView only reads it, and hides this pane ROOT whenever the level ships no
            // mp3s — HasPlaylist false must never leave a dead pane on screen (the BEST RUNS rule).
            var radio = root.AddComponent<RadioView>();
            var radioGlass = Pane("RadioPane", t, TopRight, TopRight, TopRight, new Vector2(-32f, -32f),
                                  new Vector2(RadioWidth, RadioHeight), UiSprites.Pane(), 12f);
            const float RadioInnerW = RadioWidth - Inset * 2f;

            var station = Txt("RadioStation", radioGlass, "RADIO FM", 12f, new Color(Bone.r, Bone.g, Bone.b, 0.55f), TextAlignmentOptions.Left);
            station.characterSpacing = 6f;
            station.overflowMode = TextOverflowModes.Truncate;   // a long level name never spills the glass
            Rect(station.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset), new Vector2(RadioInnerW, 18f));
            radio.stationText = station;

            // The ticker window: a RectMask2D, so a title wider than the glass is CLIPPED and scrolls
            // instead of overhanging the pane. The text inside is left-anchored and 600 wide; RadioView
            // moves its x, and nothing else on the HUD touches it.
            var viewport = Group("RadioTitleViewport", radioGlass, TopLeft, TopLeft, TopLeft,
                                 new Vector2(Inset, -Inset - 24f), new Vector2(RadioInnerW, 24f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var trackTitle = Txt("RadioTitle", viewport, "", 17f, Bone, TextAlignmentOptions.Left);
            Rect(trackTitle.gameObject, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(600f, 24f));
            radio.titleText = trackTitle;
            radio.titleViewport = viewport.GetComponent<RectTransform>();

            var counter = Txt("RadioCounter", radioGlass, "", 12f, new Color(Bone.r, Bone.g, Bone.b, 0.55f), TextAlignmentOptions.Left);
            counter.characterSpacing = 3f;
            Rect(counter.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset - 54f), new Vector2(100f, 16f));
            radio.counterText = counter;

            // The keys, quieter than the counter: they are learned once and then ignored.
            // PLAIN ASCII, and this is not a style choice: TMP Settings ships the STATIC LiberationSans
            // SDF atlas (m_AtlasPopulationMode 0, 250 glyphs topping out at U+25A1), so the pointing
            // triangles this line used to carry (U+25C0 / U+25B6) were not in it and drew as the missing
            // -glyph box. The same trap StatusStripView documents for its "&gt;" item marker.
            var keys = Txt("RadioKeys", radioGlass, "[ ] TRACK    \\ ON", 11f, new Color(Bone.r, Bone.g, Bone.b, 0.40f), TextAlignmentOptions.Right);
            Rect(keys.gameObject, TopRight, TopRight, TopRight, new Vector2(-Inset, -Inset - 54f), new Vector2(168f, 16f));
            radio.keyHintText = keys;

            // The progress line: a BarView (anchor-driven — hard rule 5, never Image.fillAmount), four
            // pixels tall, ember, no ghost and no pulse. It is a readout of where the track is, not a
            // resource, so it must not draw the eye the way a vitals bar does.
            var prog = Bar("RadioProgressBar", radioGlass, Pink, false, false);
            Rect(prog.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset - 80f), new Vector2(RadioInnerW, 4f));
            radio.progressBar = prog;

            // Hard rule 9: the motion numbers are SHIPPED on the prefab, not left to field initialisers.
            radio.tickerSpeed = 34f;
            radio.tickerPause = 1.6f;
            radio.changeSeconds = 0.45f;
            radio.changeSlide = 26f;

            // The rainbow border, at the user's ask (2026-09-07): a spectrum that swirls around the
            // radio's rim so the music player is the one piece of glass that is ALIVE. It goes on the
            // pane ROOT as the LAST child, so it draws over the glass edge rather than under it, and it
            // rides with paneRoot's SetActive — a border on a hidden radio would be a floating rainbow
            // rectangle over an empty corner. Stretched to the group with zero offsets on purpose:
            // RainbowBorderView reads the aspect off this RectTransform, and padding it inward would
            // sink the band inside the glass instead of tracing its edge.
            var auraMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_RadioAura.mat");
            if (auraMat == null)
                Debug.LogWarning("[HudBuilder] M_RadioAura.mat missing — run VibeGame1/2. Create Materials "
                                 + "(MaterialFactory.CreateRadioAura). The radio ships without its border.");
            var aura = Img("RadioAura", radioGlass.parent, Color.white);
            aura.raycastTarget = false;          // decoration: it must never eat a click meant for the HUD
            aura.material = auraMat;
            aura.type = Image.Type.Simple;
            Stretch(aura.gameObject);
            aura.gameObject.AddComponent<RainbowBorderView>();

            // The pane ROOT (the group), not the glass Pane() returns — wiring the glass would leave the
            // shadow and sheen layers on screen as an empty black card, the bug the level-editor panel hit.
            radio.paneRoot = radioGlass.parent.gameObject;
            hud.radioPane = radioGlass.parent.gameObject;
            radioGlass.parent.gameObject.SetActive(false);   // ships hidden; RadioView shows it once there is a playlist

            // ---------------- Top-right: THE FLOW METER ----------------
            // The user's ask (2026-09-07): "make the speed boost gained from any source be a meter on the
            // top right that basically shows stacks and active speed in game and do the same for parries
            // in a row (just a metric now)". Two readouts, one pane, in the band BEST RUNS freed.
            //
            // The reading order is the runner's: the multiplier is the big number on the right (a value a
            // player checks at a glance), the pips under it say how many stacks are paying for it, the
            // hairline under the pips says how long the next one has left, and the live speed sits at the
            // pips' right end so "how fast am I actually going" is answered on the same line as "how many
            // stacks". The chain is the last row, in ghost teal — the HUD's deflect colour — because it is
            // a parry number, not a speed one. It is a METRIC: nothing reads it back (FlowMeterView).
            var flowGroup = Group("FlowMeter", t, TopRight, TopRight, TopRight,
                                  new Vector2(BestRunsX, BestRunsTop), new Vector2(FlowWidth, FlowHeight));
            var flowGlass = PaneInto(flowGroup, UiSprites.Pane(), 12f);
            var flow = flowGroup.gameObject.AddComponent<FlowMeterView>();
            flow.group = flowGroup.gameObject.AddComponent<CanvasGroup>();
            flow.group.alpha = 0f;                 // at rest it is not on screen; FlowMeterView fades it in
            flow.group.interactable = false;
            flow.group.blocksRaycasts = false;
            const float FlowInnerW = FlowWidth - Inset * 2f;

            var speedLabel = Txt("FlowSpeedLabel", flowGlass, "SPEED BOOST", 11f,
                                 new Color(Bone.r, Bone.g, Bone.b, 0.55f), TextAlignmentOptions.Left);
            speedLabel.characterSpacing = 6f;
            Rect(speedLabel.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset), new Vector2(180f, 16f));

            flow.speedValue = Txt("FlowSpeedValue", flowGlass, "x1.00", 26f, Yellow, TextAlignmentOptions.Right);
            flow.speedValue.fontStyle = FontStyles.Bold;
            Rect(flow.speedValue.gameObject, TopRight, TopRight, TopRight, new Vector2(-Inset, -Inset + 4f), new Vector2(130f, 30f));

            // The pips: 5 segments of the same well the bars sit in, ember when the stack is held.
            // A segment, not a dot — a stack is a slice of a multiplier, and the row reads as one meter.
            const float PipGap = 6f;
            const float PipW = (FlowInnerW - 106f - PipGap * (FlowStackPips - 1)) / FlowStackPips;
            var pips = new Image[FlowStackPips];
            for (int i = 0; i < FlowStackPips; i++)
            {
                var pip = Img("FlowPip" + (i + 1), flowGlass, new Color(Bone.r, Bone.g, Bone.b, 0.18f));
                pip.raycastTarget = false;
                pip.sprite = UiSprites.Track();
                pip.type = Image.Type.Sliced;
                Rect(pip.gameObject, TopLeft, TopLeft, TopLeft,
                     new Vector2(Inset + i * (PipW + PipGap), -Inset - 30f), new Vector2(PipW, 8f));
                pips[i] = pip;
            }
            flow.stackPips = pips;

            // The live speed, on the pips' line and at their right: the answer to "how fast am I", quiet
            // enough that it never competes with the multiplier above it.
            flow.speedMs = Txt("FlowSpeedMs", flowGlass, "0 M/S", 13f,
                               new Color(Bone.r, Bone.g, Bone.b, 0.75f), TextAlignmentOptions.Right);
            flow.speedMs.characterSpacing = 2f;
            Rect(flow.speedMs.gameObject, TopRight, TopRight, TopRight, new Vector2(-Inset, -Inset - 26f), new Vector2(96f, 18f));

            // The decay hairline: a BarView (anchors, hard rule 5 — never Image.fillAmount), 3 px, ember,
            // no ghost and no pulse. It is time, not a resource, so it must not pull the eye like one.
            flow.decayBar = Bar("FlowDecayBar", flowGlass, Pink, false, false);
            Rect(flow.decayBar.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset - 44f), new Vector2(FlowInnerW, 3f));

            var streakLabel = Txt("FlowStreakLabel", flowGlass, "PARRY CHAIN", 11f,
                                  new Color(Bone.r, Bone.g, Bone.b, 0.35f), TextAlignmentOptions.Left);
            streakLabel.characterSpacing = 6f;
            Rect(streakLabel.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset - 56f), new Vector2(180f, 16f));
            flow.streakLabel = streakLabel;

            flow.streakValue = Txt("FlowStreakValue", flowGlass, "-", 20f, Cyan, TextAlignmentOptions.Right);
            flow.streakValue.fontStyle = FontStyles.Bold;
            Rect(flow.streakValue.gameObject, TopRight, TopRight, TopRight, new Vector2(-Inset, -Inset - 54f), new Vector2(96f, 24f));

            // Hard rule 9: every tuning number is SHIPPED on the prefab, never left to a field initialiser.
            flow.idleLingerSeconds = 1.5f;
            flow.fadeInSpeed = 10f;
            flow.fadeOutSpeed = 2.2f;
            flow.boostEpsilon = 1.005f;
            flow.emberColor = Yellow;                 // #E0A030, the HUD's "gained / spend" gold
            flow.tealColor = Cyan;                    // #7FBFB5, the deflect colour
            flow.pipOffColor = new Color(Bone.r, Bone.g, Bone.b, 0.18f);

            // ---------------- Top-right: hints ----------------
            // ONE line, contextual hints only. The key-bind dump that used to live here (four lines under
            // the clock, for the whole run) is gone: ControlsInfo feeds the settings INFO card and F1.
            hud.hintText = Txt("HintText", t, "", 14f, new Color(Bone.r, Bone.g, Bone.b, 0.55f), TextAlignmentOptions.Right);
            // Under the BEST RUNS pane's band, narrower than the gap between the pill and the right edge.
            Rect(hud.hintText.gameObject, TopRight, TopRight, TopRight, new Vector2(-32f, BestRunsBottom - 12f), new Vector2(560f, 24f));

            // ---------------- What the level editor (F10) takes off the screen ----------------
            // Every readout that is about a RUN, by pane root. Not the radio or BEST RUNS (RadioView and
            // GhostHud own those, and a second writer would fight them), not the crosshair (the editor
            // aims with it) and not the prompt line (the editor writes its PLAYING banner there).
            hud.editorHiddenRoots = new[]
            {
                bl.parent.gameObject,        // Vitals
                tl.parent.gameObject,        // Loadout
                tc.parent.gameObject,        // Clock
                itemsRoot.gameObject,        // ItemSlots
                hud.statusStrip.gameObject,  // StatusStrip
                flowGroup.gameObject,        // FlowMeter — a RUN readout: boost stacks and the parry chain
            };

            // ---------------- Center: crosshair / popups / prompt ----------------
            var crosshair = Img("Crosshair", t, Color.white);
            crosshair.raycastTarget = false;
            Rect(crosshair.gameObject, Center, Center, Center, Vector2.zero, new Vector2(6f, 6f));

            hud.parryPopup = Txt("ParryPopup", t, "PERFECT", 44f, Cyan, TextAlignmentOptions.Center);
            hud.parryPopup.fontStyle = FontStyles.Bold;
            hud.parryPopup.alpha = 0f;
            Rect(hud.parryPopup.gameObject, Center, Center, Center, new Vector2(0f, 80f), new Vector2(600f, 60f));

            hud.centerText = Txt("CenterText", t, "", 64f, Color.white, TextAlignmentOptions.Center);
            hud.centerText.fontStyle = FontStyles.Bold;
            hud.centerText.richText = true;
            hud.centerText.alpha = 0f;
            Rect(hud.centerText.gameObject, Center, Center, Center, new Vector2(0f, 180f), new Vector2(1200f, 180f));

            prompt.text = Txt("PromptText", t, "EXECUTE", 36f, Yellow, TextAlignmentOptions.Center);
            prompt.text.fontStyle = FontStyles.Bold;
            prompt.text.alpha = 0f;
            // y -132, not -120: the deathblow banner below is 90 tall centred on -60, so its rect used to
            // reach -105 and cross this one's top edge at -95. The two are not exclusive — the banner is
            // the BOSS deathblow window and this line still carries a GRAPPLE or SURGE cue underneath it.
            Rect(prompt.text.gameObject, Center, Center, Center, new Vector2(0f, -132f), new Vector2(600f, 50f));
            // Hard rule 9: the settle time is SHIPPED, not a field initialiser.
            prompt.settleSeconds = 0.6f;

            // Killing-blow banner: bigger and louder than the small deathblow prompt. Its text is
            // never rewritten at runtime, so the SHIPPED string is the one the player reads — and it has
            // to name the input the way everything else does. ExecuteInteractor.cs:96 prints
            // "DEATHBLOW  [ATTACK]" and ControlsInfo says "LMB  attack"; this said "[LMB]", which is the
            // same button under a third name.
            hud.deathblowText = Txt("DeathblowText", t, "DEATHBLOW  [ATTACK]", 64f, Blood, TextAlignmentOptions.Center);
            hud.deathblowText.fontStyle = FontStyles.Bold;
            hud.deathblowText.characterSpacing = 8f;
            hud.deathblowText.alpha = 0f;
            Rect(hud.deathblowText.gameObject, Center, Center, Center, new Vector2(0f, -60f), new Vector2(1200f, 90f));

            // Item pickup toast (name + what it does), below the crosshair and clear of the item row.
            hud.itemToastText = Txt("ItemToast", t, "", 42f, Bone, TextAlignmentOptions.Center);
            hud.itemToastText.richText = true;
            // The ONE label on the HUD whose text is authored content rather than a fixed string:
            // ItemData.description is free text and Txt() defaults to NoWrap + Overflow, so a long
            // description ran off both edges of the screen. Wrap it inside its 1000-wide rect instead.
            hud.itemToastText.textWrappingMode = TextWrappingModes.Normal;
            hud.itemToastText.alpha = 0f;
            Rect(hud.itemToastText.gameObject, Center, Center, Center, new Vector2(0f, -230f), new Vector2(1000f, 120f));

            // ---------------- Boss bar ----------------
            // A wide pane under the clock. BossBarView shows and hides the ROOT, so the pane's group
            // is what it is handed; the glass and everything on it come and go with the fight.
            var bossRootGroup = Group("BossBar", t, TopCenter, TopCenter, TopCenter, new Vector2(0f, -92f), new Vector2(940f, 92f));
            var bossRoot = PaneInto(bossRootGroup, null, 12f);
            bossBar.root = bossRootGroup.gameObject;

            bossBar.nameText = Txt("BossName", bossRoot, "BOSS", 22f, Bone, TextAlignmentOptions.Center);
            bossBar.nameText.fontStyle = FontStyles.Bold;
            bossBar.nameText.characterSpacing = 6f;
            Rect(bossBar.nameText.gameObject, TopCenter, TopCenter, TopCenter, new Vector2(0f, -10f), new Vector2(900f, 28f));

            bossBar.health = Bar("BossHealth", bossRoot, BossRed, true, false);
            Rect(bossBar.health.gameObject, TopCenter, TopCenter, TopCenter, new Vector2(0f, -42f), new Vector2(900f, 14f));

            bossBar.posture = Bar("BossPosture", bossRoot, Yellow, false, false);
            Rect(bossBar.posture.gameObject, TopCenter, TopCenter, TopCenter, new Vector2(0f, -62f), new Vector2(900f, 5f));

            bossBar.pips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var pip = Img("Pip" + i, bossRoot, Yellow);
                pip.raycastTarget = false;
                pip.sprite = UiSprites.Pill();
                pip.type = Image.Type.Sliced;
                float x = (i - 1) * 24f;
                Rect(pip.gameObject, TopCenter, TopCenter, Center, new Vector2(x, -78f), new Vector2(14f, 14f));
                bossBar.pips[i] = pip;
            }

            // ---------------- Screen flash (after gameplay widgets, before menus) ----------------
            var flashImg = Img("ScreenFlash", t, new Color(1f, 1f, 1f, 0f));
            flashImg.raycastTarget = false;
            Stretch(flashImg.gameObject);
            flash.image = flashImg;

            // ---------------- Pause menu ----------------
            // A scrim dims the game; the menu itself is one card of glass in the middle of it.
            var pausePanel = Img("PausePanel", t, new Color(Dark.r, Dark.g, Dark.b, 0.72f));
            Stretch(pausePanel.gameObject);
            pause.panel = pausePanel.gameObject;
            var pauseCard = Pane("Card", pausePanel.transform, Center, Center, Center, Vector2.zero, new Vector2(480f, 500f));

            var pauseTitle = Txt("Title", pauseCard, "PAUSED", 40f, Bone, TextAlignmentOptions.Center);
            pauseTitle.fontStyle = FontStyles.Bold;
            pauseTitle.characterSpacing = 8f;
            Rect(pauseTitle.gameObject, Center, Center, Center, new Vector2(0f, 186f), new Vector2(440f, 56f));

            pause.resumeButton = Btn("ResumeButton", pauseCard, "RESUME", new Vector2(0f, 96f), new Vector2(400f, 54f));
            pause.restartButton = Btn("RestartButton", pauseCard, "RESTART FROM CHECKPOINT", new Vector2(0f, 26f), new Vector2(400f, 54f));
            pause.mainMenuButton = Btn("MainMenuButton", pauseCard, "MAIN MENU", new Vector2(0f, -44f), new Vector2(400f, 54f));
            var settingsBtn = Btn("SettingsButton", pauseCard, "SETTINGS", new Vector2(0f, -114f), new Vector2(400f, 54f));
            pause.quitButton = Btn("QuitButton", pauseCard, "QUIT", new Vector2(0f, -184f), new Vector2(400f, 54f));
            pausePanel.gameObject.SetActive(false);

            // ---------------- Level-up menu ----------------
            var luPanel = Img("LevelUpPanel", t, new Color(Dark.r, Dark.g, Dark.b, 0.78f));
            Stretch(luPanel.gameObject);
            levelUp.panel = luPanel.gameObject;
            var luCard = Pane("Card", luPanel.transform, Center, Center, Center, new Vector2(0f, 20f), new Vector2(1100f, 720f));

            var luTitle = Txt("Title", luCard, "LEVEL UP", 44f, Bone, TextAlignmentOptions.Center);
            luTitle.fontStyle = FontStyles.Bold;
            luTitle.characterSpacing = 8f;
            Rect(luTitle.gameObject, Center, Center, Center, new Vector2(0f, 300f), new Vector2(800f, 60f));

            levelUp.soulsText = Txt("SoulsText", luCard, "SOULS  0", 26f, Mint, TextAlignmentOptions.Center);
            levelUp.soulsText.characterSpacing = 2f;
            Rect(levelUp.soulsText.gameObject, Center, Center, Center, new Vector2(0f, 246f), new Vector2(800f, 34f));

            levelUp.summaryText = Txt("SummaryText", luCard, "", 18f, new Color(Bone.r, Bone.g, Bone.b, 0.75f), TextAlignmentOptions.Center);
            Rect(levelUp.summaryText.gameObject, Center, Center, Center, new Vector2(0f, 212f), new Vector2(1000f, 26f));

            var statTypes = new[] { StatType.Vitality, StatType.Strength, StatType.Dexterity, StatType.Arcane, StatType.Flask };
            levelUp.rows = new LevelUpMenu.Row[statTypes.Length];
            for (int i = 0; i < statTypes.Length; i++)
            {
                float y = 130f - i * 70f;
                var strip = Img("Row_" + statTypes[i], luCard, new Color(1f, 1f, 1f, 0.045f));
                strip.raycastTarget = false;
                strip.sprite = UiSprites.Track();
                strip.type = Image.Type.Sliced;
                Rect(strip.gameObject, Center, Center, Center, new Vector2(0f, y), new Vector2(1000f, 56f));

                var label = Txt("Label", strip.transform, statTypes[i].ToString().ToUpperInvariant(), 22f, Bone, TextAlignmentOptions.Left);
                label.characterSpacing = 3f;
                Rect(label.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(700f, 40f));

                var cost = Txt("Cost", strip.transform, "100", 24f, Yellow, TextAlignmentOptions.Right);
                Rect(cost.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-80f, 0f), new Vector2(120f, 40f));

                var plus = Btn("Plus", strip.transform, "+", Vector2.zero, new Vector2(56f, 56f));
                Rect(plus.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(56f, 56f));

                levelUp.rows[i] = new LevelUpMenu.Row { type = statTypes[i], label = label, cost = cost, button = plus };
            }

            levelUp.closeButton = Btn("CloseButton", luCard, "CLOSE [TAB]", new Vector2(0f, -284f), new Vector2(320f, 54f));
            levelUp.table = AssetDatabase.LoadAssetAtPath<UpgradeTable>("Assets/Data/UpgradeTable.asset");
            if (levelUp.table == null)
                Debug.LogWarning("[HudBuilder] Assets/Data/UpgradeTable.asset not found; LevelUpMenu.table left null (run DataFactory first, then rebuild HUD).");
            luPanel.gameObject.SetActive(false);

            // ---------------- Wand selection (opened by the WandPedestal at spawn) ----------------
            BuildWandMenu(wandMenu, t);

            // ---------------- Test menu (developer overlay, F1) ----------------
            BuildTestMenu(testMenu, t);
            BuildLevelEditor(levelEditor, t);

            // ---------------- Settings (the shared panel; opened from the pause menu) ----------------
            // Built LAST so it draws over every other overlay. The pause panel itself is hidden while
            // settings is open (hideWhileOpen), and the PauseMenu component is suspended by SettingsMenu
            // so its ESC handler cannot fire underneath.
            SettingsPanelKit.BuildPanel(settings, t);
            settings.openButton = settingsBtn;
            settings.pauseMenu = pause;
            settings.hideWhileOpen = pausePanel.gameObject;

            // ---------------- EventSystem (new Input System) ----------------
            var es = new GameObject("EventSystem");
            es.transform.SetParent(t, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        // ---------------- wand selection menu ----------------

        const string WandDir = "Assets/Data/Wands";

        /// <summary>
        /// One row per wand in the player's loadout. The row count is taken from the wand assets on disk
        /// so the panel always has enough rows; WandSelectMenu hides any row the live loadout does not
        /// fill, so a shorter loadout degrades cleanly rather than showing empty slots.
        /// </summary>
        static void BuildWandMenu(WandSelectMenu menu, Transform t)
        {
            var panel = Img("WandMenuPanel", t, new Color(Dark.r, Dark.g, Dark.b, 0.80f));
            Stretch(panel.gameObject);
            menu.panel = panel.gameObject;
            Transform p = Pane("Card", panel.transform, Center, Center, Center, new Vector2(0f, 10f), new Vector2(1180f, 740f));

            var title = Txt("Title", p, "CHOOSE YOUR WAND", 42f, Bone, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 8f;
            Rect(title.gameObject, Center, Center, Center, new Vector2(0f, 306f), new Vector2(1100f, 56f));

            var caption = Txt("Caption", p, "the loadout is a commitment — chosen before the run, not during it", 17f,
                              new Color(Bone.r, Bone.g, Bone.b, 0.5f), TextAlignmentOptions.Center);
            Rect(caption.gameObject, Center, Center, Center, new Vector2(0f, 266f), new Vector2(1100f, 24f));

            menu.currentText = Txt("CurrentText", p, "EQUIPPED   —", 22f, Yellow, TextAlignmentOptions.Center);
            menu.currentText.fontStyle = FontStyles.Bold;
            menu.currentText.characterSpacing = 2f;
            Rect(menu.currentText.gameObject, Center, Center, Center, new Vector2(0f, 226f), new Vector2(1100f, 30f));

            int count = 4;
            if (AssetDatabase.IsValidFolder(WandDir))
            {
                int found = AssetDatabase.FindAssets("t:WandData", new[] { WandDir }).Length;
                if (found > 0) count = found;
            }
            else
            {
                Debug.LogWarning($"[HudBuilder] {WandDir} not found; the wand menu is built with 4 rows (run DataFactory, then rebuild the HUD).");
            }

            menu.rows = new WandSelectMenu.Row[count];
            for (int i = 0; i < count; i++)
            {
                float y = 150f - i * 96f;

                var strip = Img("WandRow_" + i, p, new Color(1f, 1f, 1f, 0.045f));
                strip.raycastTarget = false;
                strip.sprite = UiSprites.Track();
                strip.type = Image.Type.Sliced;
                Rect(strip.gameObject, Center, Center, Center, new Vector2(0f, y), new Vector2(1100f, 84f));

                var name = Txt("Name", strip.transform, "WAND", 26f, Cyan, TextAlignmentOptions.Left);
                name.fontStyle = FontStyles.Bold;
                Rect(name.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 22f), new Vector2(560f, 30f));

                var stats = Txt("Stats", strip.transform, "", 18f, Yellow, TextAlignmentOptions.Left);
                Rect(stats.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, -4f), new Vector2(560f, 24f));

                var desc = Txt("Description", strip.transform, "", 16f, new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.Left);
                desc.textWrappingMode = TextWrappingModes.Normal;
                Rect(desc.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, -28f), new Vector2(840f, 24f));

                var pick = Btn("Pick", strip.transform, "EQUIP", Vector2.zero, new Vector2(180f, 52f));
                Rect(pick.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(180f, 52f));

                menu.rows[i] = new WandSelectMenu.Row
                {
                    root = strip.gameObject,
                    title = name,
                    stats = stats,
                    description = desc,
                    button = pick,
                };
            }

            menu.closeButton = Btn("WandMenuClose", p, "KEEP CURRENT [ESC]", new Vector2(0f, -300f), new Vector2(360f, 54f));

            panel.gameObject.SetActive(false);
        }

        // ---------------- test menu ----------------

        static readonly string[] ItemAssetNames = { "Grapple", "WallSurge" };


        /// <summary>
        /// The in-game level editor: the component (with its asset LIBRARY — every material, prefab and
        /// item the runtime piece factory may need, resolved here by key so the runtime never touches
        /// AssetDatabase) and its glass panel. Rule 9: every number on <see cref="LevelEditor"/> is
        /// written here. See docs/LEVEL-EDITOR.md.
        /// </summary>
        static void BuildLevelEditor(LevelEditor ed, Transform t)
        {
            // ---- library --------------------------------------------------------------------------
            string[] matKeys = { "Platform", "Ground", "Stone", "Torch", "Water", "NeonPink", "NeonCyan", "NeonYellow" };
            ed.materialKeys = matKeys;
            ed.materials = new Material[matKeys.Length];
            for (int i = 0; i < matKeys.Length; i++)
            {
                ed.materials[i] = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_" + matKeys[i] + ".mat");
                if (ed.materials[i] == null) Debug.LogWarning("[HudBuilder] LevelEditor library: missing M_" + matKeys[i] + " (run 2. Create Materials).");
            }
            string[] spawnKeys = { "Enemy_Grunt", "Enemy_Heavy", "Legendary_Ninja", "Legendary_Knight", "Legendary_Spellsword",
                                   "Legendary_Marionette", "Legendary_Revenant", "Legendary_Halberdier",
                                   "pshooter_enemy01", "pshooter_enemy02" };   // parkour_enemies (2026-09-06)
            string[] prefabKeys = { "Enemy_Grunt", "Enemy_Heavy", "Legendary_Ninja", "Legendary_Knight", "Legendary_Spellsword",
                                    "Legendary_Marionette", "Legendary_Revenant", "Legendary_Halberdier", "Checkpoint", "ItemPickup", "Balloon",
                                    "pshooter_enemy01", "pshooter_enemy02" };
            ed.spawnKeys = spawnKeys;
            ed.prefabKeys = prefabKeys;
            ed.prefabs = new GameObject[prefabKeys.Length];
            for (int i = 0; i < prefabKeys.Length; i++)
            {
                ed.prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + prefabKeys[i] + ".prefab");
                if (ed.prefabs[i] == null) Debug.LogWarning("[HudBuilder] LevelEditor library: missing prefab " + prefabKeys[i] + " (run 4 / 4b).");
            }
            ed.itemKeys = ItemAssetNames;
            ed.items = new ItemData[ItemAssetNames.Length];
            for (int i = 0; i < ItemAssetNames.Length; i++)
                ed.items[i] = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Data/Items/" + ItemAssetNames[i] + ".asset");

            // ---- numbers (rule 9) ---------------------------------------------------------------------
            ed.flySpeed = 12f;
            ed.flyFastMultiplier = 3f;
            ed.aimDistance = 12f;
            ed.maxAimDistance = 80f;
            ed.platformThickness = 1f;
            ed.wallFaceHeight = 6f;
            ed.wallFaceThickness = 0.5f;
            ed.waterThickness = 0.04f;
            ed.waterFlowSpeed = 6f;
            ed.balloonLaunchSpeed = 11f;   // matches Balloon.prefab (PrefabFactory.BuildBalloon)
            ed.balloonRespawnSeconds = 2.5f;
            ed.balloonRadius = 1.1f;       // matches the prefab's trigger; the factory scales the visual from 1.1
            ed.hideSceneRootsWhileEditing = true;
            // The de-janked controls (2026-09-05, "the controls seem a bit jank"). Eases are time
            // constants; hysteresis a fraction of a cell; the grab hold is under a fifth of a second.
            ed.flyEase = 0.12f;
            ed.flyFreeMultiplier = 0.5f;
            ed.previewEase = 0.06f;
            ed.snapHysteresis = 0.25f;
            ed.grabHoldSeconds = 0.18f;
            ed.highlightStrength = 0.35f;
            ed.highlightColor = Cyan;
            ed.groundSize = 200f;
            ed.gridLineSpacing = 4f;
            ed.enterCursorFree = true;
            // The QOL pass (2026-09-05, "placing is janky, the size buttons half work"): the aim's SURFACE is
            // debounced over three frames, Ctrl+Z holds fifty edits, the local grid decal is seven cells.
            ed.surfaceDebounceFrames = 3;
            ed.undoDepth = 50;
            ed.decalCells = 7;

            // ---- panel: a glass pane on the right, keyboard-first, buttons for the file actions ---------
            // Below the top-right hint text and the ghost HUD's BEST RUNS block (both end by y -280 on
            // the 1080 canvas); at -110 the pane sat on top of them (seen in play, 2026-09-05).
            var pane = Pane("LevelEditorPanel", t, TopRight, TopRight, TopRight, new Vector2(-32f, BestRunsBottom - 48f), new Vector2(360f, 700f), UiSprites.Pane(), 12f);
            // The pane ROOT, not the glass transform Pane() hands back. LevelEditor hides `panel` until
            // F10; wired to the glass, the shadow and sheen layers stayed on screen as an empty black
            // pane under BEST RUNS that answered nothing (seen in play, 2026-09-06).
            ed.panel = pane.parent.gameObject;
            var title = Txt("Title", pane, "LEVEL EDITOR   [F10]", 20f, Bone, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 3f;
            Rect(title.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset), new Vector2(320f, 26f));

            // One BUTTON per piece kind (4 x 3, enum order), wired by LevelEditor.Awake to SelectKind. The list
            // used to be plain text -- nothing to click -- and the user's "the options don't switch the object"
            // was exactly that. Each is 82 x 32 on the 8 px rhythm; the selected one is lit by RefreshPanel.
            // FOUR columns, not three (2026-09-07, the Ramp kind): the tenth kind on a 3-wide grid opened a
            // fourth row exactly where the piece list sits, and everything below is laid out at absolute
            // offsets down to a 700 px pane with no room to shift. Four columns keeps 10 kinds in 3 rows.
            var kinds = System.Enum.GetNames(typeof(LevelPieceKind));
            ed.kindButtons = new Button[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                int col = i % 4, row = i / 4;
                var kb = Btn("Kind_" + kinds[i], pane, kinds[i].ToUpperInvariant(), Vector2.zero, new Vector2(82f, 32f));
                Rect(kb.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset + col * 84f, -Inset - 34f - row * 40f), new Vector2(82f, 32f));
                var kl = kb.GetComponentInChildren<TMP_Text>(true);
                if (kl != null) kl.fontSize = 11f;
                ed.kindButtons[i] = kb;
            }
            ed.pieceList = Txt("PieceList", pane, "", 14f, Bone, TextAlignmentOptions.TopLeft);
            ed.pieceList.richText = true;
            Rect(ed.pieceList.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset - 34f - 3f * 40f), new Vector2(320f, 40f));

            ed.help = Txt("Help", pane, "LMB place   hold: grab   RMB delete   T rotate   Esc cancel\n" +
                                         "wheel size   Shift+wheel piece   Ctrl+wheel variant\n" +
                                         "arrows nudge (Ctrl: up/down)   Ctrl+Z undo   Ctrl+D copy   P here\n" +
                                         "WASD fly   Space up   Ctrl down   Shift fast   Alt free   Tab cursor", 12f,
                          new Color(Bone.r, Bone.g, Bone.b, 0.6f), TextAlignmentOptions.TopLeft);
            Rect(ed.help.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset - 212f), new Vector2(320f, 80f));

            // The numbers: size / grid / rotation and the aimed piece's own size. Rich text, two lines.
            ed.readout = Txt("Readout", pane, "", 13f, Bone, TextAlignmentOptions.TopLeft);
            ed.readout.richText = true;
            Rect(ed.readout.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset - 296f), new Vector2(320f, 36f));

            ed.status = Txt("Status", pane, "", 13f, new Color(Bone.r, Bone.g, Bone.b, 0.8f), TextAlignmentOptions.TopLeft);
            Rect(ed.status.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset - 336f), new Vector2(320f, 40f));

            // PLACE HERE (the selected piece under the camera) and UNDO, above the file actions.
            const float qw = 154f, qh = 40f;
            ed.placeHereButton = Btn("PlaceHere", pane, "PLACE HERE", Vector2.zero, new Vector2(qw, qh));
            Rect(ed.placeHereButton.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset - 380f), new Vector2(qw, qh));
            ed.undoButton = Btn("Undo", pane, "UNDO", Vector2.zero, new Vector2(qw, qh));
            Rect(ed.undoButton.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset + qw + 12f, -Inset - 380f), new Vector2(qw, qh));

            // name field (TMP's own default control, restyled to the pane)
            var field = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            field.name = "NameField";
            field.transform.SetParent(pane, false);
            Rect(field, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset - 430f), new Vector2(320f, 36f));
            ed.nameField = field.GetComponent<TMP_InputField>();
            var fieldImg = field.GetComponent<Image>();
            if (fieldImg != null) { fieldImg.sprite = UiSprites.Track(); fieldImg.type = Image.Type.Sliced; fieldImg.color = new Color(0f, 0f, 0f, TrackAlpha); }
            ed.nameField.text = "custom";
            foreach (var tm in field.GetComponentsInChildren<TMP_Text>(true)) { tm.color = Bone; tm.fontSize = 16f; }

            float y = -Inset - 482f;
            const float bw = 154f, bh = 40f;
            ed.newButton = Btn("New", pane, "NEW", Vector2.zero, new Vector2(bw, bh));
            Rect(ed.newButton.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, y), new Vector2(bw, bh));
            ed.saveButton = Btn("Save", pane, "SAVE", Vector2.zero, new Vector2(bw, bh));
            Rect(ed.saveButton.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset + bw + 12f, y), new Vector2(bw, bh));

            y -= bh + 10f;
            ed.loadPrevButton = Btn("LoadPrev", pane, "<", Vector2.zero, new Vector2(40f, bh));
            Rect(ed.loadPrevButton.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, y), new Vector2(40f, bh));
            ed.loadLabel = Txt("LoadLabel", pane, "", 13f, Bone, TextAlignmentOptions.Center);
            Rect(ed.loadLabel.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset + 46f, y), new Vector2(228f, bh));
            ed.loadNextButton = Btn("LoadNext", pane, ">", Vector2.zero, new Vector2(40f, bh));
            Rect(ed.loadNextButton.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset + 280f, y), new Vector2(40f, bh));
            y -= bh + 10f;
            ed.loadButton = Btn("Load", pane, "LOAD", Vector2.zero, new Vector2(bw, bh));
            Rect(ed.loadButton.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, y), new Vector2(bw, bh));
            ed.playButton = Btn("Play", pane, "PLAY", Vector2.zero, new Vector2(bw, bh));
            Rect(ed.playButton.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset + bw + 12f, y), new Vector2(bw, bh));
            y -= bh + 10f;
            ed.exportButton = Btn("Export", pane, "EXPORT ASSET", Vector2.zero, new Vector2(bw, bh));
            Rect(ed.exportButton.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, y), new Vector2(bw, bh));
            ed.exitButton = Btn("Exit", pane, "EXIT", Vector2.zero, new Vector2(bw, bh));
            Rect(ed.exitButton.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset + bw + 12f, y), new Vector2(bw, bh));

            var hint = Txt("LevelEditorPlayHint", t, "PLAYING A CUSTOM LEVEL   [F10] back to the editor", 14f,
                           new Color(Bone.r, Bone.g, Bone.b, 0.6f), TextAlignmentOptions.Right);
            Rect(hint.gameObject, TopRight, TopRight, TopRight, new Vector2(-32f, -110f), new Vector2(600f, 24f));
            ed.playHint = hint.gameObject;

            pane.gameObject.SetActive(false);
            hint.gameObject.SetActive(false);
        }

        static void BuildTestMenu(TestMenu menu, Transform t)
        {
            var panel = Img("TestMenuPanel", t, new Color(Dark.r, Dark.g, Dark.b, 0.92f));
            Stretch(panel.gameObject);
            menu.panel = panel.gameObject;
            Transform p = panel.transform;

            var title = Txt("Title", p, "TEST MENU   [F1]", 40f, Bone, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 6f;
            Rect(title.gameObject, Center, Center, Center, new Vector2(0f, 330f), new Vector2(900f, 60f));

            const float headerY = 250f;
            const float firstY = 200f;
            const float step = 52f;

            // Warp
            ColumnHeader(p, "WARP", -720f, headerY);
            menu.warpStartButton = MenuBtn(p, "Warp", "START", -720f, firstY);
            menu.warpCheckpoint1Button = MenuBtn(p, "Warp", "CHECKPOINT 1", -720f, firstY - step);
            menu.warpCheckpoint2Button = MenuBtn(p, "Warp", "CHECKPOINT 2", -720f, firstY - step * 2f);
            menu.warpBossButton = MenuBtn(p, "Warp", "BOSS ARENA", -720f, firstY - step * 3f);
            menu.warpYardButton = MenuBtn(p, "Warp", "MOVEMENT YARD", -720f, firstY - step * 4f);   // sandbox only

            // Items
            ColumnHeader(p, "GIVE ITEM", -450f, headerY);
            menu.items = new ItemData[ItemAssetNames.Length];
            menu.itemButtons = new Button[ItemAssetNames.Length];
            for (int i = 0; i < ItemAssetNames.Length; i++)
            {
                string path = "Assets/Data/Items/" + ItemAssetNames[i] + ".asset";
                menu.items[i] = AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (menu.items[i] == null)
                    Debug.LogWarning($"[HudBuilder] {path} not found; that test-menu button stays disabled (run DataFactory, then rebuild the HUD).");
                menu.itemButtons[i] = MenuBtn(p, "Item" + i, ItemAssetNames[i].ToUpperInvariant(), -450f, firstY - step * i);
            }

            // Weapons
            ColumnHeader(p, "WEAPON", -180f, headerY);
            menu.weaponButtons = new Button[4];
            for (int i = 0; i < menu.weaponButtons.Length; i++)
                menu.weaponButtons[i] = MenuBtn(p, "Weapon" + i, "SLOT " + (i + 1), -180f, firstY - step * i);

            // Player
            ColumnHeader(p, "PLAYER", 90f, headerY);
            menu.fullRestoreButton = MenuBtn(p, "Player", "FULL RESTORE", 90f, firstY);
            menu.godModeButton = MenuBtn(p, "Player", "TOGGLE GOD MODE", 90f, firstY - step);
            menu.giveSoulsButton = MenuBtn(p, "Player", "+1000 SOULS", 90f, firstY - step * 2f);
            menu.breakPostureButton = MenuBtn(p, "Player", "BREAK MY POSTURE", 90f, firstY - step * 3f);
            // The wand altar is a dev fixture now: hidden and inert until this flips WandPedestal.DevMenuEnabled.
            // TestMenu.RefreshButtons rewrites the label to the live "WAND PEDESTAL: OFF/ON" state.
            menu.wandPedestalButton = MenuBtn(p, "Player", "WAND PEDESTAL: OFF", 90f, firstY - step * 4f);
            // BACKLOG 2b's viewmodel movement channel, switchable live. The user asked for the toggle the
            // day it was built: it is a FEEL change they may not want, and one you cannot turn off
            // mid-run cannot be judged against the version without it.
            // TestMenu.RefreshButtons rewrites the label to the live "ARM MOVEMENT: ON/OFF" state.
            menu.armMovementButton = MenuBtn(p, "Player", "ARM MOVEMENT: ON", 90f, firstY - step * 5f);

            // Enemies
            ColumnHeader(p, "ENEMIES", 360f, headerY);
            menu.killNearbyButton = MenuBtn(p, "Enemy", "KILL NEARBY", 360f, firstY);
            menu.staggerNearbyButton = MenuBtn(p, "Enemy", "STAGGER NEARBY", 360f, firstY - step);
            menu.resetEnemiesButton = MenuBtn(p, "Enemy", "RESET ENEMIES", 360f, firstY - step * 2f);
            // The in-game level editor (F10 from play does the same). See docs/LEVEL-EDITOR.md.
            menu.levelEditorButton = MenuBtn(p, "Enemy", "LEVEL EDITOR [F10]", 360f, firstY - step * 4f);
            // The key reference: opens the settings screen straight onto its INFO card (ControlsInfo).
            menu.infoButton = MenuBtn(p, "Enemy", "CONTROLS INFO", 360f, firstY - step * 3f);

            // Live readout
            var readoutBg = Img("ReadoutBg", p, new Color(1f, 1f, 1f, 0.04f));
            readoutBg.raycastTarget = false;
            readoutBg.sprite = UiSprites.Pane();
            readoutBg.type = Image.Type.Sliced;
            Rect(readoutBg.gameObject, Center, Center, Center, new Vector2(700f, -20f), new Vector2(420f, 560f));

            menu.readout = Txt("Readout", readoutBg.transform, "", 16f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.TopLeft);
            menu.readout.richText = true;
            menu.readout.textWrappingMode = TextWrappingModes.Normal;
            Rect(menu.readout.gameObject, Center, Center, Center, new Vector2(0f, 0f), new Vector2(392f, 536f));

            menu.closeButton = Btn("TestMenuClose", p, "CLOSE [F1]", new Vector2(0f, -320f), new Vector2(320f, 56f));

            panel.gameObject.SetActive(false);
        }

        static void ColumnHeader(Transform parent, string text, float x, float y)
        {
            var h = Txt(text.Replace(" ", "") + "Header", parent, text, 18f, Cyan, TextAlignmentOptions.Center);
            h.fontStyle = FontStyles.Bold;
            h.characterSpacing = 4f;
            Rect(h.gameObject, Center, Center, Center, new Vector2(x, y), new Vector2(240f, 24f));
        }

        static Button MenuBtn(Transform parent, string prefix, string label, float x, float y)
        {
            var btn = Btn(prefix + label.Replace(" ", ""), parent, label, new Vector2(x, y), new Vector2(240f, 44f));
            var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null) txt.fontSize = 17f;
            return btn;
        }

        /// <summary>
        /// One item tile: a small pane of glass. The slot's own Image IS the glass (ItemSlotView tints
        /// it to the item's colour when one is held), with the shadow and the edge light underneath and
        /// on top of it as siblings, so the view's colour writes touch nothing but the pane.
        /// </summary>
        static ItemSlotView ItemSlot(string name, Transform parent, float x)
        {
            var slot = Group(name, parent, BottomCenter, BottomCenter, BottomCenter, new Vector2(x, 0f), new Vector2(88f, 88f));

            var shadow = Img("Shadow", slot, new Color(0f, 0f, 0f, ShadowAlpha));
            shadow.raycastTarget = false;
            shadow.sprite = UiSprites.Shadow();
            shadow.type = Image.Type.Sliced;
            var srt = Stretch(shadow.gameObject);
            srt.offsetMin = new Vector2(-8f, -12f);
            srt.offsetMax = new Vector2(8f, 4f);

            var bg = Img("Glass", slot, new Color(PaneTint.r, PaneTint.g, PaneTint.b, PaneAlpha));
            bg.raycastTarget = false;
            bg.sprite = UiSprites.Tile();
            bg.type = Image.Type.Sliced;
            Stretch(bg.gameObject);

            // ItemSlotView rewrites its background colour every frame (empty / held / front / flash),
            // so it is handed a TINT layer over the glass rather than the glass itself: the smoked pane
            // stays under whatever colour the slot is showing.
            var tint = Img("Tint", slot, new Color(1f, 1f, 1f, 0.05f));
            tint.raycastTarget = false;
            tint.sprite = UiSprites.Tile();
            tint.type = Image.Type.Sliced;
            Stretch(tint.gameObject);

            var edge = Img("EdgeLight", slot, new Color(Bone.r, Bone.g, Bone.b, EdgeAlpha));
            edge.raycastTarget = false;
            edge.sprite = UiSprites.EdgeLight();
            edge.type = Image.Type.Simple;
            Rect(edge.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(-16f, 1f));

            var view = slot.gameObject.AddComponent<ItemSlotView>();
            view.background = tint;

            var icon = Img("Icon", slot, new Color(1f, 1f, 1f, 0.09f));
            icon.raycastTarget = false;
            icon.sprite = UiSprites.Track();
            icon.type = Image.Type.Sliced;
            Rect(icon.gameObject, Center, Center, Center, new Vector2(0f, 12f), new Vector2(36f, 36f));
            view.icon = icon;

            var label = Txt("Label", slot, "", 15f, Bone, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = 1f;
            Rect(label.gameObject, Center, Center, Center, new Vector2(0f, -24f), new Vector2(84f, 22f));
            view.label = label;

            var key = Txt("KeyHint", slot, "[E]", 13f, Yellow, TextAlignmentOptions.Center);
            key.fontStyle = FontStyles.Bold;
            Rect(key.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(60f, 20f));
            key.enabled = false;
            view.keyHint = key;

            return view;
        }

        // ---------------- helpers ----------------

        static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        static RectTransform Rect(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Stretch(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Center;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// Stretched to its parent with a padding on each side, so the child follows a rect that the
        /// runtime RESIZES (the BEST RUNS glass grows and shrinks as it expands). Insets are positive
        /// distances inward, in the reading order left / right / top / bottom.
        /// </summary>
        static RectTransform StretchInto(GameObject go, float left, float right, float top, float bottom)
        {
            var rt = Stretch(go);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        static Transform Group(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Rect(go, anchorMin, anchorMax, pivot, pos, size);
            return go.transform;
        }

        static Sprite uiSprite;
        static bool uiSpriteLoaded;

        /// <summary>
        /// Unity's Image ignores type=Filled/fillAmount when it has no sprite, so a spriteless bar
        /// always draws a full quad. BarView drives width via anchors (sprite-independent), but we
        /// still assign the builtin UI sprite so the images are well-formed for anyone editing them.
        /// </summary>
        static Sprite UiSprite()
        {
            if (!uiSpriteLoaded)
            {
                uiSpriteLoaded = true;
                uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                if (uiSprite == null)
                    Debug.LogWarning("[HudBuilder] Builtin UI/Skin/UISprite.psd not found; bars still work (BarView drives RectTransform anchors).");
            }
            return uiSprite;
        }

        /// <summary>Fill/ghost images: width comes from BarView's anchor drive, fillAmount stays at 1.</summary>
        static void ConfigureFill(Image img)
        {
            var s = UiSprite();
            if (s != null) img.sprite = s;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 1f;
        }

        static Image Img(string name, Transform parent, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            return img;
        }

        static TextMeshProUGUI Txt(string name, Transform parent, string text, float size, Color c, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = c;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }

        static readonly Color StaminaFill = new Color(0.55f, 0.95f, 1f, 1f);

        /// <summary>
        /// The stamina row of the vitals pane: bar at (x, y), its label 14 px above, the three ability
        /// pips starting <paramref name="colOffset"/> to the right of the bar's left edge. Everything is
        /// relative to the pane so the row moves as one when the pane does.
        /// </summary>
        static StaminaView StaminaBlock(Transform parent, float x, float y, float barW, float colOffset)
        {
            var root = new GameObject("Stamina", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Rect(root, BottomLeft, BottomLeft, BottomLeft, new Vector2(x, y), new Vector2(barW + 160f, 28f));
            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            var view = root.AddComponent<StaminaView>();
            view.group = group;

            view.bar = Bar("StaminaBar", root.transform, StaminaFill, false, false);
            Rect(view.bar.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 0f), new Vector2(barW, 10f));

            // Ticks at every dash cost (30 of 100): three segments, each one dash. Drawn over the fill
            // in the pane's own tint, so they read as glass over fluid rather than as black lines.
            for (int i = 1; i <= 3; i++)
            {
                var tick = Img("Tick" + i, view.bar.transform, new Color(PaneTint.r, PaneTint.g, PaneTint.b, 0.9f));
                tick.raycastTarget = false;
                float tx = barW * (0.30f * i);
                Rect(tick.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(tx - 1f, 0f), new Vector2(2f, 10f));
            }

            view.label = Txt("StaminaLabel", root.transform, "STAMINA", 12f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Left);
            view.label.characterSpacing = 3f;
            Rect(view.label.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 14f), new Vector2(300f, 14f));

            view.dashPip = Pip("PipDash", root.transform, "DASH", colOffset);
            view.airPip  = Pip("PipAir",  root.transform, "AIR",  colOffset + 46f);
            view.wallPip = Pip("PipWall", root.transform, "WALL", colOffset + 84f);
            return view;
        }

        /// <summary>
        /// The top-left status strip: a single multi-line label that StatusStripView rewrites with one
        /// row per held item and one per running effect. Pip-sized type with the pips' tracking so it
        /// reads as status, not as a heading.
        /// </summary>
        static StatusStripView StatusStrip(Transform parent, float y)
        {
            var root = new GameObject("StatusStrip", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Rect(root, TopLeft, TopLeft, TopLeft, new Vector2(32f + Inset, y), new Vector2(500f, 140f));
            var view = root.AddComponent<StatusStripView>();

            view.text = Txt("Rows", root.transform, "", 15f, Bone, TextAlignmentOptions.TopLeft);
            view.text.fontStyle = FontStyles.Bold;
            view.text.characterSpacing = 3f;
            view.text.lineSpacing = 8f;
            view.text.richText = true;
            Stretch(view.text.gameObject);
            return view;
        }

        static TMP_Text Pip(string name, Transform parent, string text, float x)
        {
            var t = Txt(name, parent, text, 11f, new Color(1f, 1f, 1f, 0.22f), TextAlignmentOptions.Left);
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 1f;
            Rect(t.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(x, -3f), new Vector2(48f, 16f));
            return t;
        }

        /// <summary>
        /// A bar: a dark rounded track (the well the fluid sits in) with a Fill child and, optionally, a
        /// trailing Ghost. The track is the bar's own Image so a styling pass (HudExtensions) can find
        /// the bar by NAME and rework its fill without touching the layout.
        /// </summary>
        static BarView Bar(string name, Transform parent, Color fillColor, bool withGhost, bool pulse)
        {
            var bg = Img(name, parent, new Color(0f, 0f, 0f, TrackAlpha));
            bg.raycastTarget = false;
            bg.sprite = UiSprites.Track();
            bg.type = Image.Type.Sliced;
            var bar = bg.gameObject.AddComponent<BarView>();

            if (withGhost)
            {
                var ghost = Img("Ghost", bg.transform, new Color(1f, 1f, 1f, 0.4f));
                ghost.raycastTarget = false;
                ConfigureFill(ghost);
                Stretch(ghost.gameObject);
                bar.ghost = ghost;
            }

            var fill = Img("Fill", bg.transform, fillColor);
            fill.raycastTarget = false;
            ConfigureFill(fill);
            Stretch(fill.gameObject);

            bar.fill = fill;
            bar.fillColor = fillColor;
            bar.pulseWhenFull = pulse;
            return bar;
        }

        /// <summary>
        /// A button is a pill of glass: the plate at 0.75, an edge light along its top, the label in
        /// bone. The ColorBlock multiplies the plate, so hover is a teal wash rather than a new colour.
        /// </summary>
        static Button Btn(string name, Transform parent, string label, Vector2 pos, Vector2 size)
        {
            var img = Img(name, parent, new Color(ButtonBg.r, ButtonBg.g, ButtonBg.b, 0.75f));
            img.sprite = UiSprites.Pill();
            img.type = Image.Type.Sliced;
            Rect(img.gameObject, Center, Center, Center, pos, size);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);
            colors.pressedColor = Cyan;
            colors.selectedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.4f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var edge = Img("EdgeLight", img.transform, new Color(Bone.r, Bone.g, Bone.b, EdgeAlpha * 0.8f));
            edge.raycastTarget = false;
            edge.sprite = UiSprites.EdgeLight();
            edge.type = Image.Type.Simple;
            Rect(edge.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(-size.y, 1f));

            var txt = Txt("Label", img.transform, label, 20f, Bone, TextAlignmentOptions.Center);
            txt.fontStyle = FontStyles.Bold;
            txt.characterSpacing = 2f;
            Stretch(txt.gameObject);
            return btn;
        }

        // ---------------- glass ----------------

        /// <summary>
        /// A pane of smoked glass at the given rect. Returns the transform to put content on (the glass
        /// itself, stretched to the group), so children can anchor inside it with plain offsets.
        /// </summary>
        static Transform Pane(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                              Vector2 pos, Vector2 size, Sprite shape = null, float edgeInset = 10f)
        {
            var group = Group(name, parent, anchorMin, anchorMax, pivot, pos, size);
            return PaneInto(group, shape, edgeInset);
        }

        /// <summary>
        /// The four layers of a pane, filling <paramref name="group"/>: the shadow it throws (spilling
        /// 10 px each side and 4 px lower, so the pane sits OFF the frame), the smoked glass, a sheen
        /// across its upper third, and the one-pixel light along its top edge that fades in from ember
        /// on the left — the eclipse's rim catching the glass. The edge light is the one loud thing on
        /// the HUD; everything else on a pane is quiet so it can be.
        /// </summary>
        static Transform PaneInto(Transform group, Sprite shape, float edgeInset)
        {
            var shadow = Img("Shadow", group, new Color(0f, 0f, 0f, ShadowAlpha));
            shadow.raycastTarget = false;
            shadow.sprite = UiSprites.Shadow();
            shadow.type = Image.Type.Sliced;
            var srt = Stretch(shadow.gameObject);
            srt.offsetMin = new Vector2(-10f, -14f);
            srt.offsetMax = new Vector2(10f, 6f);

            var glass = Img("Glass", group, new Color(PaneTint.r, PaneTint.g, PaneTint.b, PaneAlpha));
            glass.raycastTarget = false;
            glass.sprite = shape != null ? shape : UiSprites.Pane();
            glass.type = Image.Type.Sliced;
            Stretch(glass.gameObject);

            var sheen = Img("Sheen", glass.transform, new Color(1f, 1f, 1f, SheenAlpha));
            sheen.raycastTarget = false;
            sheen.sprite = UiSprites.Sheen();
            sheen.type = Image.Type.Simple;
            var sh = Stretch(sheen.gameObject);
            sh.offsetMin = new Vector2(3f, 3f);
            sh.offsetMax = new Vector2(-3f, -1f);

            var edge = Img("EdgeLight", glass.transform, new Color(Bone.r, Bone.g, Bone.b, EdgeAlpha));
            edge.raycastTarget = false;
            edge.sprite = UiSprites.EdgeLight();
            edge.type = Image.Type.Simple;
            Rect(edge.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(-edgeInset * 2f, 1f));

            // The ember end: a second, shorter strip over the left third of the light, so the line
            // warms toward the pane's left edge — the same accent pair (ember under bone) the title uses.
            var ember = Img("EdgeEmber", glass.transform, new Color(Pink.r, Pink.g, Pink.b, EdgeAlpha * 0.9f));
            ember.raycastTarget = false;
            ember.sprite = UiSprites.EdgeLight();
            ember.type = Image.Type.Simple;
            Rect(ember.gameObject, new Vector2(0f, 1f), new Vector2(0.34f, 1f), new Vector2(0f, 1f), new Vector2(edgeInset, -1f), new Vector2(-edgeInset, 1f));

            return glass.transform;
        }
    }

    /// <summary>
    /// Emits THE settings panel — the one layout that goes into BOTH the HUD prefab (opened from the
    /// pause menu) and the MainMenu prefab (opened from the title screen).
    ///
    /// <para>Shared on purpose, unlike the layout helpers above, which HudBuilder and MainMenuBuilder
    /// deliberately duplicate. <see cref="SettingsMenu"/>'s contract is that the front-end screen and
    /// the in-game screen are THE SAME screen; two hand-kept copies of this method is exactly how they
    /// would stop being the same screen.</para>
    ///
    /// <para>The caller owns the entry point: this fills <see cref="SettingsMenu.panel"/>,
    /// <see cref="SettingsMenu.rows"/>, <see cref="SettingsMenu.backButton"/> and
    /// <see cref="SettingsMenu.resetButton"/>, and leaves <c>openButton</c> / <c>pauseMenu</c> /
    /// <c>hideWhileOpen</c> for the builder that knows which scene it is in.</para>
    ///
    /// <para>Hard rule 5: the sliders are stock UGUI <see cref="Slider"/>s, which drive their fill
    /// image's RectTransform ANCHORS — never <c>Image.fillAmount</c> (a null-sprite Image silently
    /// ignores it). The value TEXT is the authoritative readout on every row regardless.</para>
    /// </summary>
    internal static class SettingsPanelKit
    {
        // The HUD/menu palette. Kept here as literals for the same reason the two builders keep their
        // own copies: this file must not silently move when either of theirs is tuned.
        static readonly Color Cyan = HexC("#7FBFB5");
        static readonly Color Ember = HexC("#D9891A");
        static readonly Color Dark = HexC("#06040A");
        static readonly Color ButtonBg = HexC("#1A1220");
        static readonly Color Bone = HexC("#E8E2D6");

        static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);
        static readonly Vector2 Left = new Vector2(0f, 0.5f);
        static readonly Vector2 Right = new Vector2(1f, 0.5f);

        // Layout numbers, public so SettingsPanelTests asserts the SHIPPED arithmetic rather than a
        // second copy of it. Tightened 2026-09-06 to seat the AUDIO section: the panel went from 10 rows
        // in 3 sections to 12 in 4, and at the old stride the last row would have landed on the BACK
        // button. It also buys back ultrawide headroom — at 21:9 the canvas scaler leaves only 935
        // logical units of height (467 above and below centre), and the buttons used to sit at -484.
        // Tightened again 2026-09-07 for the FLOURISH KEY row: CONTROL went from three rows to four,
        // and at stride 50 / gap 38 the thirteenth row would have landed on the BACK button
        // (SettingsAudioTests.TheLastRowClearsTheButtons asserts exactly that).
        public const float RowWidth = 1160f;
        public const float RowHeight = 46f;
        public const float RowStride = 47f;
        /// <summary>Extra drop before a section header, and from the header down to its first row.</summary>
        public const float SectionLead = 6f;
        public const float SectionGap = 34f;

        // The rebind row's two buttons. They sit in the column a continuous row spends on its slider,
        // which is empty on every cycler, so the VALUE text keeps its own place and the readout column
        // stays in one line down the whole panel.
        public static readonly Vector2 RebindButtonSize = new Vector2(130f, 40f);
        public static readonly Vector2 ResetBindButtonSize = new Vector2(120f, 40f);
        /// <summary>x of each rebind button, measured from the row's RIGHT edge.</summary>
        public const float RebindButtonX = -560f;
        public const float ResetBindButtonX = -420f;
        /// <summary>y of the first thing the row loop places.</summary>
        public const float FirstRowCursor = 352f;
        /// <summary>BACK / RESET DEFAULTS: y, and the size both share.</summary>
        public const float ButtonY = -430f;
        public static readonly Vector2 ButtonSize = new Vector2(300f, 52f);
        /// <summary>The glass card behind the rows.</summary>
        public static readonly Vector2 CardPos = new Vector2(0f, -12f);
        public static readonly Vector2 CardSize = new Vector2(1260f, 950f);

        /// <summary>
        /// Section header shown above this row. Keyed on the KIND, not the index: the row order lives in
        /// <see cref="SettingsMenu.AllKinds"/>, and an index table silently mis-sections the whole panel
        /// the first time a row is inserted rather than appended.
        /// </summary>
        static string SectionBefore(SettingsMenu.RowKind kind)
        {
            switch (kind)
            {
                case SettingsMenu.RowKind.MouseSensitivity: return "CONTROL";
                case SettingsMenu.RowKind.Resolution: return "DISPLAY";
                case SettingsMenu.RowKind.Bloom: return "IMAGE";
                case SettingsMenu.RowKind.MasterVolume: return "AUDIO";
                default: return null;
            }
        }

        /// <summary>
        /// The y this panel's last row lands on, given the rows it is asked to draw. Pure, so the test
        /// that proves the rows clear the BACK button runs the SAME arithmetic the builder does.
        /// </summary>
        public static float LastRowY(SettingsMenu.RowKind[] kinds)
        {
            float cursor = FirstRowCursor, last = cursor;
            for (int i = 0; i < kinds.Length; i++)
            {
                if (SectionBefore(kinds[i]) != null) cursor -= SectionLead + SectionGap;
                last = cursor;
                cursor -= RowStride;
            }
            return last;
        }

        public static GameObject BuildPanel(SettingsMenu menu, Transform canvasRoot)
        {
            var panel = ImgK("SettingsPanel", canvasRoot, new Color(Dark.r, Dark.g, Dark.b, 0.80f));
            panel.raycastTarget = true;   // swallows clicks; nothing under the panel is reachable
            StretchK(panel.gameObject);
            Transform p = panel.transform;

            // One card of glass behind the rows, built first so it draws under them. Same four layers
            // HudBuilder.PaneInto uses, kept local for the reason the rest of this kit is.
            GlassCard(p, CardPos, CardSize);

            var title = TxtK("Title", p, "SETTINGS", 44f, Bone, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 10f;
            RectK(title.gameObject, Mid, Mid, Mid, new Vector2(0f, 430f), new Vector2(900f, 60f));

            var rule = ImgK("TitleRule", p, new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f));
            rule.raycastTarget = false;
            RectK(rule.gameObject, Mid, Mid, Mid, new Vector2(0f, 396f), new Vector2(760f, 1f));

            var kinds = SettingsMenu.AllKinds;
            var rows = new SettingsMenu.Row[kinds.Length];
            float cursor = FirstRowCursor;

            for (int i = 0; i < kinds.Length; i++)
            {
                string section = SectionBefore(kinds[i]);
                if (section != null)
                {
                    cursor -= SectionLead;
                    var h = TxtK(section + "Header", p, section, 17f,
                                 new Color(Ember.r, Ember.g, Ember.b, 0.85f), TextAlignmentOptions.Left);
                    h.fontStyle = FontStyles.Bold;
                    h.characterSpacing = 8f;
                    RectK(h.gameObject, Mid, Mid, Left, new Vector2(-RowWidth * 0.5f, cursor), new Vector2(400f, 22f));
                    cursor -= SectionGap;
                }

                rows[i] = BuildRow(kinds[i], p, cursor);
                cursor -= RowStride;
            }

            var back = BtnK("BackButton", p, "BACK", new Vector2(-180f, ButtonY), ButtonSize);
            var reset = BtnK("ResetButton", p, "RESET DEFAULTS", new Vector2(180f, ButtonY), ButtonSize);

            // The INFO card: the whole key reference (ControlsInfo, the one source both prefabs share) on
            // a glass card over the rows, toggled by the INFO button beside the title. Built LAST so it
            // draws over the rows; ships closed. Same text on the pause path and the title path - this
            // kit is the one emitter, and SettingsPrefabTests holds the two prefabs to identical text.
            var infoCard = new GameObject("InfoPanel", typeof(RectTransform));
            infoCard.transform.SetParent(p, false);
            RectK(infoCard, Mid, Mid, Mid, new Vector2(0f, 20f), new Vector2(1200f, 780f));
            GlassCard(infoCard.transform, Vector2.zero, new Vector2(1200f, 780f));
            var infoTitle = TxtK("InfoTitle", infoCard.transform, "CONTROLS", 30f, Bone, TextAlignmentOptions.Center);
            infoTitle.fontStyle = FontStyles.Bold;
            infoTitle.characterSpacing = 8f;
            RectK(infoTitle.gameObject, Mid, Mid, Mid, new Vector2(0f, 342f), new Vector2(800f, 40f));
            var infoText = TxtK("InfoText", infoCard.transform, ControlsInfo.Text, 17f, Bone, TextAlignmentOptions.TopLeft);
            infoText.richText = true;
            infoText.lineSpacing = 6f;
            infoText.textWrappingMode = TextWrappingModes.Normal;
            RectK(infoText.gameObject, Mid, Mid, Mid, new Vector2(0f, -30f), new Vector2(1120f, 660f));
            infoCard.SetActive(false);
            var info = BtnK("InfoButton", p, "INFO", new Vector2(480f, 430f), new Vector2(180f, 48f));

            menu.panel = panel.gameObject;
            menu.rows = rows;
            menu.backButton = back;
            menu.resetButton = reset;
            menu.infoButton = info;
            menu.infoPanel = infoCard;
            menu.infoText = infoText;

            panel.gameObject.SetActive(false);
            return panel.gameObject;
        }

        static SettingsMenu.Row BuildRow(SettingsMenu.RowKind kind, Transform parent, float y)
        {
            var strip = ImgK("Row_" + kind, parent, new Color(1f, 1f, 1f, 0.03f));
            strip.raycastTarget = false;
            strip.sprite = UiSprites.Track();
            strip.type = Image.Type.Sliced;
            RectK(strip.gameObject, Mid, Mid, Mid, new Vector2(0f, y), new Vector2(RowWidth, RowHeight));
            Transform s = strip.transform;

            var label = TxtK("Label", s, SettingsMenu.LabelFor(kind), 20f, Bone, TextAlignmentOptions.Left);
            label.characterSpacing = 3f;
            RectK(label.gameObject, Left, Left, Left, new Vector2(24f, 0f), new Vector2(400f, 30f));

            Slider slider = null;
            if (SettingsMenu.IsContinuous(kind))
                slider = BuildSlider("Slider", s, new Vector2(440f, 0f), new Vector2(280f, 28f));

            Button dec, inc;
            if (SettingsMenu.IsRebind(kind))
            {
                // Not < and >: a key is not a position in a list. Left listens, right restores the
                // default. SettingsMenu binds decrease -> listen and increase -> reset, so the two
                // buttons keep the same wiring every other row has.
                dec = RowBtn("Decrease", s, SettingsMenu.RebindButtonLabel(false),
                             new Vector2(RebindButtonX, 0f), RebindButtonSize, 16f);
                inc = RowBtn("Increase", s, SettingsMenu.RebindButtonLabel(true),
                             new Vector2(ResetBindButtonX, 0f), ResetBindButtonSize, 16f);
            }
            else
            {
                dec = SmallBtn("Decrease", s, "<", new Vector2(-300f, 0f));
                inc = SmallBtn("Increase", s, ">", new Vector2(-44f, 0f));
            }

            // The value TEXT is authoritative (a slider can silently fail to draw; a string cannot).
            var value = TxtK("Value", s, "—", 19f, Bone, TextAlignmentOptions.Center);
            value.fontStyle = FontStyles.Bold;
            RectK(value.gameObject, Right, Right, Mid, new Vector2(-172f, 7f), new Vector2(200f, 26f));

            var note = TxtK("Note", s, "", 11f, new Color(1f, 1f, 1f, 0.4f), TextAlignmentOptions.Center);
            RectK(note.gameObject, Right, Right, Mid, new Vector2(-172f, -14f), new Vector2(220f, 16f));

            return new SettingsMenu.Row
            {
                kind = kind,
                root = strip.gameObject,
                label = label,
                value = value,
                decrease = dec,
                increase = inc,
                slider = slider,
                note = note,
            };
        }

        /// <summary>
        /// A stock UGUI slider, hand-assembled (there is no template to instantiate in a code-built
        /// UI). Fill and handle are ANCHOR-driven by the Slider component itself.
        /// </summary>
        static Slider BuildSlider(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectK(go, Left, Left, Left, pos, size);
            var slider = go.AddComponent<Slider>();

            var bg = ImgK("Background", go.transform, new Color(1f, 1f, 1f, 0.08f));
            var bgRt = bg.rectTransform;
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.pivot = Mid;
            bgRt.anchoredPosition = Vector2.zero;
            bgRt.sizeDelta = new Vector2(0f, 6f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var faRt = fillArea.GetComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0f, 0.5f);
            faRt.anchorMax = new Vector2(1f, 0.5f);
            faRt.pivot = Mid;
            faRt.anchoredPosition = new Vector2(-7f, 0f);
            faRt.sizeDelta = new Vector2(-14f, 6f);

            var fill = ImgK("Fill", fillArea.transform, new Color(Ember.r, Ember.g, Ember.b, 0.9f));
            fill.raycastTarget = false;
            var fillRt = fill.rectTransform;
            fillRt.sizeDelta = new Vector2(10f, 0f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            var haRt = handleArea.GetComponent<RectTransform>();
            haRt.anchorMin = Vector2.zero;
            haRt.anchorMax = Vector2.one;
            haRt.pivot = Mid;
            haRt.anchoredPosition = Vector2.zero;
            haRt.sizeDelta = new Vector2(-14f, 0f);

            var handle = ImgK("Handle", handleArea.transform, Bone);
            var hRt = handle.rectTransform;
            hRt.sizeDelta = new Vector2(14f, 22f);

            slider.fillRect = fillRt;
            slider.handleRect = hRt;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;

            var colors = slider.colors;
            colors.highlightedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 1f);
            colors.pressedColor = new Color(Ember.r, Ember.g, Ember.b, 1f);
            colors.selectedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 1f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.4f);
            slider.colors = colors;

            return slider;
        }

        static Button SmallBtn(string name, Transform parent, string label, Vector2 posFromRight)
        {
            return RowBtn(name, parent, label, posFromRight, new Vector2(44f, 40f), 22f);
        }

        /// <summary>The one row button. Sized by the caller so a worded button (REBIND) and a glyph one
        /// (&lt;) are the same widget with the same states, rather than two that drift apart.</summary>
        static Button RowBtn(string name, Transform parent, string label, Vector2 posFromRight, Vector2 size, float fontSize)
        {
            var img = ImgK(name, parent, new Color(ButtonBg.r, ButtonBg.g, ButtonBg.b, 0.75f));
            img.sprite = UiSprites.Pill();
            img.type = Image.Type.Sliced;
            RectK(img.gameObject, Right, Right, Mid, posFromRight, size);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);
            colors.pressedColor = Cyan;
            colors.selectedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.35f);
            btn.colors = colors;

            var txt = TxtK("Label", img.transform, label, fontSize, Bone, TextAlignmentOptions.Center);
            txt.fontStyle = FontStyles.Bold;
            StretchK(txt.gameObject);
            return btn;
        }

        static Button BtnK(string name, Transform parent, string label, Vector2 pos, Vector2 size)
        {
            var img = ImgK(name, parent, new Color(ButtonBg.r, ButtonBg.g, ButtonBg.b, 0.75f));
            img.sprite = UiSprites.Pill();
            img.type = Image.Type.Sliced;
            RectK(img.gameObject, Mid, Mid, Mid, pos, size);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);
            colors.pressedColor = Cyan;
            colors.selectedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.4f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var edge = ImgK("EdgeLight", img.transform, new Color(Bone.r, Bone.g, Bone.b, 0.24f));
            edge.raycastTarget = false;
            edge.sprite = UiSprites.EdgeLight();
            edge.type = Image.Type.Simple;
            RectK(edge.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(-size.y, 1f));

            var txt = TxtK("Label", img.transform, label, 20f, Bone, TextAlignmentOptions.Center);
            txt.fontStyle = FontStyles.Bold;
            txt.characterSpacing = 2f;
            StretchK(txt.gameObject);
            return btn;
        }

        /// <summary>A card of glass: shadow, smoked pane, sheen, edge light. Drawn first, under the rows.</summary>
        static void GlassCard(Transform parent, Vector2 pos, Vector2 size)
        {
            var group = new GameObject("Card", typeof(RectTransform));
            group.transform.SetParent(parent, false);
            RectK(group, Mid, Mid, Mid, pos, size);

            var shadow = ImgK("Shadow", group.transform, new Color(0f, 0f, 0f, 0.42f));
            shadow.raycastTarget = false;
            shadow.sprite = UiSprites.Shadow();
            shadow.type = Image.Type.Sliced;
            var srt = StretchK(shadow.gameObject);
            srt.offsetMin = new Vector2(-10f, -14f);
            srt.offsetMax = new Vector2(10f, 6f);

            var glass = ImgK("Glass", group.transform, new Color(0.043f, 0.027f, 0.063f, 0.58f));
            glass.raycastTarget = false;
            glass.sprite = UiSprites.Pane();
            glass.type = Image.Type.Sliced;
            StretchK(glass.gameObject);

            var sheen = ImgK("Sheen", glass.transform, new Color(1f, 1f, 1f, 0.035f));
            sheen.raycastTarget = false;
            sheen.sprite = UiSprites.Sheen();
            sheen.type = Image.Type.Simple;
            var sh = StretchK(sheen.gameObject);
            sh.offsetMin = new Vector2(3f, 3f);
            sh.offsetMax = new Vector2(-3f, -1f);

            var edge = ImgK("EdgeLight", glass.transform, new Color(Bone.r, Bone.g, Bone.b, 0.30f));
            edge.raycastTarget = false;
            edge.sprite = UiSprites.EdgeLight();
            edge.type = Image.Type.Simple;
            RectK(edge.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(-20f, 1f));

            var ember = ImgK("EdgeEmber", glass.transform, new Color(Ember.r, Ember.g, Ember.b, 0.27f));
            ember.raycastTarget = false;
            ember.sprite = UiSprites.EdgeLight();
            ember.type = Image.Type.Simple;
            RectK(ember.gameObject, new Vector2(0f, 1f), new Vector2(0.34f, 1f), new Vector2(0f, 1f), new Vector2(10f, -1f), new Vector2(-10f, 1f));
        }

        // ---- tiny layout idiom, kit-local -------------------------------------------------------

        static Color HexC(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        static RectTransform RectK(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform StretchK(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Mid;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        static Image ImgK(string name, Transform parent, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            return img;
        }

        static TextMeshProUGUI TxtK(string name, Transform parent, string text, float size, Color c, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = c;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }
    }
}
