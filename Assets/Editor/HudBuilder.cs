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
        /// <summary>One BEST RUNS row: 17 pt TMP plus 4 pt line spacing.</summary>
        public const float BestRunsRowHeight = 22f;
        /// <summary>Height of the BEST RUNS glass: insets, the title band, and one row per leaderboard entry.</summary>
        public const float BestRunsHeight = Inset * 2f + 26f + Leaderboard.DisplayCount * BestRunsRowHeight + 6f;
        /// <summary>Canvas y of the pane's lower edge (anchored top-right at -32).</summary>
        public const float BestRunsBottom = -32f - BestRunsHeight;
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
            // Weapon, wand, the wand's cooldown hairline and the soul count, in one narrow pane. The
            // weapon name is the pane's title and is set in bone; teal is left to the wand alone so the
            // accent means one thing.
            var tl = Pane("Loadout", t, TopLeft, TopLeft, TopLeft, new Vector2(32f, -32f), new Vector2(360f, 116f));

            hud.weaponText = Txt("WeaponText", tl, "SWORD", 30f, Bone, TextAlignmentOptions.Left);
            hud.weaponText.fontStyle = FontStyles.Bold;
            hud.weaponText.characterSpacing = 2f;
            Rect(hud.weaponText.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -10f), new Vector2(320f, 38f));

            hud.wandText = Txt("WandText", tl, "", 16f, Cyan, TextAlignmentOptions.Left);
            hud.wandText.characterSpacing = 1f;
            Rect(hud.wandText.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -50f), new Vector2(320f, 22f));

            // Wand cooldown, directly under the wand name. Full immediately after a discharge, empty
            // when the wand is ready — the riposte prompt says the same thing in words.
            hud.wandCooldownBar = Bar("WandCooldownBar", tl, Blood, false, false);
            Rect(hud.wandCooldownBar.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -76f), new Vector2(240f, 4f));

            hud.soulsText = Txt("SoulsText", tl, "SOULS  0", 16f, Mint, TextAlignmentOptions.Left);
            hud.soulsText.characterSpacing = 2f;
            Rect(hud.soulsText.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -86f), new Vector2(320f, 22f));

            // Held items and active effects, under the loadout pane. A strip, not a pane: one small
            // line per row, blank when idle, rows appear and vanish with state (StatusStripView).
            hud.statusStrip = StatusStrip(t);

            // ---------------- Top-center: the timer pill ----------------
            // The run clock, alone in a pill: the one piece of glass with fully rounded ends, so it is
            // never mistaken for a pane of readings.
            var tc = Pane("Clock", t, TopCenter, TopCenter, TopCenter, new Vector2(0f, -24f), new Vector2(300f, 52f), UiSprites.Pill(), 22f);
            hud.timerText = Txt("TimerText", tc, "00:00.00", 32f, Bone, TextAlignmentOptions.Center);
            hud.timerText.fontStyle = FontStyles.Bold;
            hud.timerText.characterSpacing = 5f;
            Stretch(hud.timerText.gameObject);

            // ---------------- Top-right: BEST RUNS, as glass ----------------
            // GhostHud (its own runtime canvas, by design) writes its table INTO this pane when it finds
            // one, so the block reads in the HUD's language instead of as loose muted text; without the
            // pane it falls back to its own text as before. Ships hidden; GhostHud shows it once there is
            // a board. 300 x 196 at (-32, -32): clear of the clock pill (x 810..1110) and above the
            // level-editor panel (y -300 down) - HudGlassTests pins both.
            // 300 x BestRunsHeight (2026-09-06: was 196, and eight 17 pt rows at 4 pt spacing are ~200 tall
            // on their own, so the table spilled out of the glass): insets, the title band and one row per
            // Leaderboard.DisplayCount. Everything below (hint, editor panel) hangs off BestRunsBottom.
            var best = Pane("BestRunsPane", t, TopRight, TopRight, TopRight, new Vector2(-32f, -32f), new Vector2(300f, BestRunsHeight), UiSprites.Pane(), 12f);
            var bestTitle = Txt("BestRunsTitle", best, "BEST RUNS", 12f, new Color(Bone.r, Bone.g, Bone.b, 0.55f), TextAlignmentOptions.Left);
            bestTitle.characterSpacing = 6f;
            Rect(bestTitle.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset), new Vector2(200f, 18f));
            hud.bestRunsText = Txt("BestRunsText", best, "", 17f, Bone, TextAlignmentOptions.TopLeft);
            hud.bestRunsText.richText = true;
            hud.bestRunsText.lineSpacing = 4f;
            Rect(hud.bestRunsText.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset, -Inset - 26f), new Vector2(268f, BestRunsHeight - Inset * 2f - 26f));
            hud.bestRunsText.overflowMode = TextOverflowModes.Truncate;   // never past the glass, whatever the backend sends
            // The pane ROOT (the group named BestRunsPane), not the content transform Pane() hands back:
            // GhostHud toggles it, and it must ship hidden until there is a board.
            hud.bestRunsPane = best.parent.gameObject;
            best.parent.gameObject.SetActive(false);

            // ---------------- Top-right: hints ----------------
            // ONE line, contextual hints only. The key-bind dump that used to live here (four lines under
            // the clock, for the whole run) is gone: ControlsInfo feeds the settings INFO card and F1.
            hud.hintText = Txt("HintText", t, "", 14f, new Color(Bone.r, Bone.g, Bone.b, 0.55f), TextAlignmentOptions.Right);
            // Under the BEST RUNS pane's band, narrower than the gap between the pill and the right edge.
            Rect(hud.hintText.gameObject, TopRight, TopRight, TopRight, new Vector2(-32f, BestRunsBottom - 12f), new Vector2(560f, 24f));

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
            Rect(prompt.text.gameObject, Center, Center, Center, new Vector2(0f, -120f), new Vector2(600f, 50f));

            // Killing-blow banner: bigger and louder than the small EXECUTE prompt.
            hud.deathblowText = Txt("DeathblowText", t, "DEATHBLOW  [LMB]", 64f, Blood, TextAlignmentOptions.Center);
            hud.deathblowText.fontStyle = FontStyles.Bold;
            hud.deathblowText.characterSpacing = 8f;
            hud.deathblowText.alpha = 0f;
            Rect(hud.deathblowText.gameObject, Center, Center, Center, new Vector2(0f, -60f), new Vector2(1200f, 90f));

            // Item pickup toast (name + what it does), below the crosshair and clear of the item row.
            hud.itemToastText = Txt("ItemToast", t, "", 42f, Color.white, TextAlignmentOptions.Center);
            hud.itemToastText.richText = true;
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

            // One BUTTON per piece kind (3 x 3, enum order), wired by LevelEditor.Awake to SelectKind. The list
            // used to be plain text -- nothing to click -- and the user's "the options don't switch the object"
            // was exactly that. Each is 100 x 32 on the 8 px rhythm; the selected one is lit by RefreshPanel.
            var kinds = System.Enum.GetNames(typeof(LevelPieceKind));
            ed.kindButtons = new Button[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                int col = i % 3, row = i / 3;
                var kb = Btn("Kind_" + kinds[i], pane, kinds[i].ToUpperInvariant(), Vector2.zero, new Vector2(100f, 32f));
                Rect(kb.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(Inset + col * 110f, -Inset - 34f - row * 40f), new Vector2(100f, 32f));
                var kl = kb.GetComponentInChildren<TMP_Text>(true);
                if (kl != null) kl.fontSize = 12f;
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
        static StatusStripView StatusStrip(Transform parent)
        {
            var root = new GameObject("StatusStrip", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Rect(root, TopLeft, TopLeft, TopLeft, new Vector2(32f + Inset, -164f), new Vector2(500f, 140f));
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

        const float RowWidth = 1160f;
        const float RowHeight = 50f;
        const float RowStride = 56f;

        /// <summary>Section header shown above the given row index. Data, so the loop stays one loop.</summary>
        static string SectionBefore(int rowIndex)
        {
            switch (rowIndex)
            {
                case 0: return "CONTROL";
                case 3: return "DISPLAY";
                case 8: return "IMAGE";
                default: return null;
            }
        }

        public static GameObject BuildPanel(SettingsMenu menu, Transform canvasRoot)
        {
            var panel = ImgK("SettingsPanel", canvasRoot, new Color(Dark.r, Dark.g, Dark.b, 0.80f));
            panel.raycastTarget = true;   // swallows clicks; nothing under the panel is reachable
            StretchK(panel.gameObject);
            Transform p = panel.transform;

            // One card of glass behind the rows, built first so it draws under them. Same four layers
            // HudBuilder.PaneInto uses, kept local for the reason the rest of this kit is.
            GlassCard(p, new Vector2(0f, -8f), new Vector2(1260f, 1000f));

            var title = TxtK("Title", p, "SETTINGS", 44f, Bone, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 10f;
            RectK(title.gameObject, Mid, Mid, Mid, new Vector2(0f, 430f), new Vector2(900f, 60f));

            var rule = ImgK("TitleRule", p, new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f));
            rule.raycastTarget = false;
            RectK(rule.gameObject, Mid, Mid, Mid, new Vector2(0f, 396f), new Vector2(760f, 1f));

            var kinds = SettingsMenu.AllKinds;
            var rows = new SettingsMenu.Row[kinds.Length];
            float cursor = 348f;

            for (int i = 0; i < kinds.Length; i++)
            {
                string section = SectionBefore(i);
                if (section != null)
                {
                    cursor -= 8f;
                    var h = TxtK(section + "Header", p, section, 17f,
                                 new Color(Ember.r, Ember.g, Ember.b, 0.85f), TextAlignmentOptions.Left);
                    h.fontStyle = FontStyles.Bold;
                    h.characterSpacing = 8f;
                    RectK(h.gameObject, Mid, Mid, Left, new Vector2(-RowWidth * 0.5f, cursor), new Vector2(400f, 22f));
                    cursor -= 40f;
                }

                rows[i] = BuildRow(kinds[i], p, cursor);
                cursor -= RowStride;
            }

            var back = BtnK("BackButton", p, "BACK", new Vector2(-180f, -456f), new Vector2(300f, 56f));
            var reset = BtnK("ResetButton", p, "RESET DEFAULTS", new Vector2(180f, -456f), new Vector2(300f, 56f));

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

            var dec = SmallBtn("Decrease", s, "<", new Vector2(-300f, 0f));
            var inc = SmallBtn("Increase", s, ">", new Vector2(-44f, 0f));

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
            var img = ImgK(name, parent, new Color(ButtonBg.r, ButtonBg.g, ButtonBg.b, 0.75f));
            img.sprite = UiSprites.Pill();
            img.type = Image.Type.Sliced;
            RectK(img.gameObject, Right, Right, Mid, posFromRight, new Vector2(44f, 40f));
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);
            colors.pressedColor = Cyan;
            colors.selectedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.35f);
            btn.colors = colors;

            var txt = TxtK("Label", img.transform, label, 22f, Color.white, TextAlignmentOptions.Center);
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
