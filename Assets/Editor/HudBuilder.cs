using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VibeGame1.EditorTools
{
    /// <summary>Builds Assets/Prefabs/HUD.prefab (canvas + menus + EventSystem) from code. Idempotent.</summary>
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

        static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        static readonly Vector2 BottomCenter = new Vector2(0.5f, 0f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        [MenuItem("VibeGame1/5. Build HUD")]
        public static void Build()
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Prefabs"));

            var root = new GameObject("HUD");
            try
            {
                BuildInternal(root);
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
            var prompt = root.AddComponent<PromptView>();
            var flash = root.AddComponent<ScreenFlash>();
            var pause = root.AddComponent<PauseMenu>();
            var levelUp = root.AddComponent<LevelUpMenu>();
            var bossBar = root.AddComponent<BossBarView>();
            var testMenu = root.AddComponent<TestMenu>();
            var wandMenu = root.AddComponent<WandSelectMenu>();

            Transform t = root.transform;

            // ---------------- Bottom-left: health / pyre / flask ----------------
            var bl = Group("BottomLeft", t, BottomLeft, BottomLeft, BottomLeft, new Vector2(40f, 40f), new Vector2(600f, 200f));

            hud.flaskText = Txt("FlaskText", bl, "FLASK  3 / 3", 20f, Yellow, TextAlignmentOptions.Left);
            Rect(hud.flaskText.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 0f), new Vector2(420f, 26f));

            // PYRE — the parry charge meter. Same slot the old parry-juice bar occupied, and the same
            // ember gold, because the bar and the fire on the weapon are the same resource.
            hud.pyreBar = Bar("PyreBar", bl, Pink, true, true);
            Rect(hud.pyreBar.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 34f), new Vector2(420f, 12f));

            var pyreLabel = Txt("PyreLabel", bl, "PYRE", 16f, Pink, TextAlignmentOptions.Left);
            Rect(pyreLabel.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 48f), new Vector2(200f, 20f));

            // Text is rewritten at runtime with the equipped weapon's super name.
            var ready = Txt("PyreReadyLabel", bl, "SUPER  READY  [Q]", 20f, Yellow, TextAlignmentOptions.Left);
            ready.fontStyle = FontStyles.Bold;
            Rect(ready.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 158f), new Vector2(420f, 26f));
            hud.pyreReadyLabel = ready;

            hud.healthBar = Bar("HealthBar", bl, HealthFill, true, false);
            Rect(hud.healthBar.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 80f), new Vector2(420f, 18f));

            hud.healthText = Txt("HealthText", bl, "100 / 100", 22f, Color.white, TextAlignmentOptions.Left);
            Rect(hud.healthText.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(432f, 76f), new Vector2(160f, 26f));

            // Player posture (Sekiro-style): fills when you block or get hit, breaks -> stagger.
            hud.postureBar = Bar("PostureBar", bl, PostureFill, false, true);
            Rect(hud.postureBar.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 132f), new Vector2(420f, 14f));

            // ---------------- Bottom-centre: single-use item slots ----------------
            var itemsRoot = Group("ItemSlots", t, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 40f), new Vector2(324f, 130f));

            var itemsCaption = Txt("ItemsCaption", itemsRoot, "ITEMS", 14f, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Center);
            itemsCaption.characterSpacing = 6f;
            Rect(itemsCaption.gameObject, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 104f), new Vector2(324f, 20f));

            hud.itemSlots = new ItemSlotView[3];
            for (int i = 0; i < hud.itemSlots.Length; i++)
                hud.itemSlots[i] = ItemSlot("ItemSlot" + i, itemsRoot, (i - 1) * 108f);

            // ---------------- Top-left: weapon / souls ----------------
            hud.weaponText = Txt("WeaponText", t, "SWORD", 30f, Cyan, TextAlignmentOptions.Left);
            hud.weaponText.fontStyle = FontStyles.Bold;
            Rect(hud.weaponText.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(40f, -30f), new Vector2(500f, 40f));

            hud.wandText = Txt("WandText", t, "", 18f, Cyan, TextAlignmentOptions.Left);
            Rect(hud.wandText.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(40f, -62f), new Vector2(500f, 24f));

            // Wand cooldown, directly under the wand name. Full immediately after a discharge, empty
            // when the wand is ready — the riposte prompt says the same thing in words.
            hud.wandCooldownBar = Bar("WandCooldownBar", t, Blood, false, false);
            Rect(hud.wandCooldownBar.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(40f, -84f), new Vector2(240f, 6f));

            hud.soulsText = Txt("SoulsText", t, "SOULS  0", 20f, Mint, TextAlignmentOptions.Left);
            Rect(hud.soulsText.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(40f, -100f), new Vector2(300f, 26f));

            // ---------------- Top-center: timer ----------------
            hud.timerText = Txt("TimerText", t, "00:00.00", 34f, Color.white, TextAlignmentOptions.Center);
            hud.timerText.fontStyle = FontStyles.Bold;
            hud.timerText.characterSpacing = 6f;
            Rect(hud.timerText.gameObject, TopCenter, TopCenter, TopCenter, new Vector2(0f, -28f), new Vector2(320f, 44f));

            // ---------------- Top-right: hints ----------------
            hud.hintText = Txt("HintText", t, "", 14f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Right);
            Rect(hud.hintText.gameObject, TopRight, TopRight, TopRight, new Vector2(-40f, -30f), new Vector2(1100f, 72f));

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
            var bossRoot = Group("BossBar", t, TopCenter, TopCenter, TopCenter, new Vector2(0f, -110f), new Vector2(900f, 80f));
            bossBar.root = bossRoot.gameObject;

            bossBar.nameText = Txt("BossName", bossRoot, "BOSS", 24f, Pink, TextAlignmentOptions.Center);
            bossBar.nameText.fontStyle = FontStyles.Bold;
            Rect(bossBar.nameText.gameObject, TopCenter, TopCenter, TopCenter, new Vector2(0f, 0f), new Vector2(900f, 30f));

            bossBar.health = Bar("BossHealth", bossRoot, BossRed, true, false);
            Rect(bossBar.health.gameObject, TopCenter, TopCenter, TopCenter, new Vector2(0f, -34f), new Vector2(900f, 16f));

            bossBar.posture = Bar("BossPosture", bossRoot, Yellow, false, false);
            Rect(bossBar.posture.gameObject, TopCenter, TopCenter, TopCenter, new Vector2(0f, -54f), new Vector2(900f, 8f));

            bossBar.pips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var pip = Img("Pip" + i, bossRoot, Yellow);
                pip.raycastTarget = false;
                float x = (i - 1) * 24f;
                Rect(pip.gameObject, TopCenter, TopCenter, Center, new Vector2(x, -74f), new Vector2(16f, 16f));
                bossBar.pips[i] = pip;
            }

            // ---------------- Screen flash (after gameplay widgets, before menus) ----------------
            var flashImg = Img("ScreenFlash", t, new Color(1f, 1f, 1f, 0f));
            flashImg.raycastTarget = false;
            Stretch(flashImg.gameObject);
            flash.image = flashImg;

            // ---------------- Pause menu ----------------
            var pausePanel = Img("PausePanel", t, new Color(Dark.r, Dark.g, Dark.b, 0.85f));
            Stretch(pausePanel.gameObject);
            pause.panel = pausePanel.gameObject;

            var pauseTitle = Txt("Title", pausePanel.transform, "PAUSED", 60f, Cyan, TextAlignmentOptions.Center);
            pauseTitle.fontStyle = FontStyles.Bold;
            Rect(pauseTitle.gameObject, Center, Center, Center, new Vector2(0f, 160f), new Vector2(800f, 80f));

            pause.resumeButton = Btn("ResumeButton", pausePanel.transform, "RESUME", new Vector2(0f, 40f), new Vector2(320f, 56f));
            pause.restartButton = Btn("RestartButton", pausePanel.transform, "RESTART FROM CHECKPOINT", new Vector2(0f, -30f), new Vector2(320f, 56f));
            pause.quitButton = Btn("QuitButton", pausePanel.transform, "QUIT", new Vector2(0f, -100f), new Vector2(320f, 56f));
            pausePanel.gameObject.SetActive(false);

            // ---------------- Level-up menu ----------------
            var luPanel = Img("LevelUpPanel", t, new Color(Dark.r, Dark.g, Dark.b, 0.9f));
            Stretch(luPanel.gameObject);
            levelUp.panel = luPanel.gameObject;

            var luTitle = Txt("Title", luPanel.transform, "LEVEL UP", 56f, Cyan, TextAlignmentOptions.Center);
            luTitle.fontStyle = FontStyles.Bold;
            Rect(luTitle.gameObject, Center, Center, Center, new Vector2(0f, 330f), new Vector2(800f, 70f));

            levelUp.soulsText = Txt("SoulsText", luPanel.transform, "SOULS  0", 28f, Mint, TextAlignmentOptions.Center);
            Rect(levelUp.soulsText.gameObject, Center, Center, Center, new Vector2(0f, 270f), new Vector2(800f, 36f));

            levelUp.summaryText = Txt("SummaryText", luPanel.transform, "", 20f, Color.white, TextAlignmentOptions.Center);
            Rect(levelUp.summaryText.gameObject, Center, Center, Center, new Vector2(0f, 232f), new Vector2(1000f, 28f));

            var statTypes = new[] { StatType.Vitality, StatType.Strength, StatType.Dexterity, StatType.Arcane, StatType.Flask };
            levelUp.rows = new LevelUpMenu.Row[statTypes.Length];
            for (int i = 0; i < statTypes.Length; i++)
            {
                float y = 150f - i * 70f;
                var strip = Img("Row_" + statTypes[i], luPanel.transform, new Color(1f, 1f, 1f, 0.05f));
                strip.raycastTarget = false;
                Rect(strip.gameObject, Center, Center, Center, new Vector2(0f, y), new Vector2(1000f, 56f));

                var label = Txt("Label", strip.transform, statTypes[i].ToString().ToUpperInvariant(), 24f, Color.white, TextAlignmentOptions.Left);
                Rect(label.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(700f, 40f));

                var cost = Txt("Cost", strip.transform, "100", 24f, Yellow, TextAlignmentOptions.Right);
                Rect(cost.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-80f, 0f), new Vector2(120f, 40f));

                var plus = Btn("Plus", strip.transform, "+", Vector2.zero, new Vector2(56f, 56f));
                Rect(plus.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(56f, 56f));

                levelUp.rows[i] = new LevelUpMenu.Row { type = statTypes[i], label = label, cost = cost, button = plus };
            }

            levelUp.closeButton = Btn("CloseButton", luPanel.transform, "CLOSE [TAB]", new Vector2(0f, -260f), new Vector2(320f, 56f));
            levelUp.table = AssetDatabase.LoadAssetAtPath<UpgradeTable>("Assets/Data/UpgradeTable.asset");
            if (levelUp.table == null)
                Debug.LogWarning("[HudBuilder] Assets/Data/UpgradeTable.asset not found; LevelUpMenu.table left null (run DataFactory first, then rebuild HUD).");
            luPanel.gameObject.SetActive(false);

            // ---------------- Wand selection (opened by the WandPedestal at spawn) ----------------
            BuildWandMenu(wandMenu, t);

            // ---------------- Test menu (developer overlay, F1) ----------------
            BuildTestMenu(testMenu, t);

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
            var panel = Img("WandMenuPanel", t, new Color(Dark.r, Dark.g, Dark.b, 0.92f));
            Stretch(panel.gameObject);
            menu.panel = panel.gameObject;
            Transform p = panel.transform;

            var title = Txt("Title", p, "CHOOSE YOUR WAND", 52f, Cyan, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 6f;
            Rect(title.gameObject, Center, Center, Center, new Vector2(0f, 340f), new Vector2(1200f, 64f));

            var caption = Txt("Caption", p, "the loadout is a commitment — chosen before the run, not during it", 18f,
                              new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.Center);
            Rect(caption.gameObject, Center, Center, Center, new Vector2(0f, 296f), new Vector2(1200f, 26f));

            menu.currentText = Txt("CurrentText", p, "EQUIPPED   —", 24f, Yellow, TextAlignmentOptions.Center);
            menu.currentText.fontStyle = FontStyles.Bold;
            Rect(menu.currentText.gameObject, Center, Center, Center, new Vector2(0f, 254f), new Vector2(1200f, 32f));

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
                float y = 170f - i * 96f;

                var strip = Img("WandRow_" + i, p, new Color(1f, 1f, 1f, 0.05f));
                strip.raycastTarget = false;
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

            menu.closeButton = Btn("WandMenuClose", p, "KEEP CURRENT [ESC]", new Vector2(0f, -320f), new Vector2(360f, 56f));

            panel.gameObject.SetActive(false);
        }

        // ---------------- test menu ----------------

        static readonly string[] ItemAssetNames = { "Updraft", "SoulLantern", "PhantomStep" };

        static void BuildTestMenu(TestMenu menu, Transform t)
        {
            var panel = Img("TestMenuPanel", t, new Color(Dark.r, Dark.g, Dark.b, 0.92f));
            Stretch(panel.gameObject);
            menu.panel = panel.gameObject;
            Transform p = panel.transform;

            var title = Txt("Title", p, "TEST MENU   [F1]", 42f, Cyan, TextAlignmentOptions.Center);
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

            // Enemies
            ColumnHeader(p, "ENEMIES", 360f, headerY);
            menu.killNearbyButton = MenuBtn(p, "Enemy", "KILL NEARBY", 360f, firstY);
            menu.staggerNearbyButton = MenuBtn(p, "Enemy", "STAGGER NEARBY", 360f, firstY - step);
            menu.resetEnemiesButton = MenuBtn(p, "Enemy", "RESET ENEMIES", 360f, firstY - step * 2f);

            // Live readout
            var readoutBg = Img("ReadoutBg", p, new Color(1f, 1f, 1f, 0.04f));
            readoutBg.raycastTarget = false;
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

        static ItemSlotView ItemSlot(string name, Transform parent, float x)
        {
            var bg = Img(name, parent, new Color(1f, 1f, 1f, 0.05f));
            bg.raycastTarget = false;
            var s = UiSprite();
            if (s != null) { bg.sprite = s; bg.type = Image.Type.Sliced; }
            Rect(bg.gameObject, BottomCenter, BottomCenter, BottomCenter, new Vector2(x, 0f), new Vector2(96f, 96f));

            var view = bg.gameObject.AddComponent<ItemSlotView>();
            view.background = bg;

            var icon = Img("Icon", bg.transform, new Color(1f, 1f, 1f, 0.09f));
            icon.raycastTarget = false;
            if (s != null) { icon.sprite = s; icon.type = Image.Type.Sliced; }
            Rect(icon.gameObject, Center, Center, Center, new Vector2(0f, 12f), new Vector2(40f, 40f));
            view.icon = icon;

            var label = Txt("Label", bg.transform, "", 16f, Color.white, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            Rect(label.gameObject, Center, Center, Center, new Vector2(0f, -26f), new Vector2(92f, 22f));
            view.label = label;

            var key = Txt("KeyHint", bg.transform, "[E]", 14f, Yellow, TextAlignmentOptions.Center);
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

        static BarView Bar(string name, Transform parent, Color fillColor, bool withGhost, bool pulse)
        {
            var bg = Img(name, parent, new Color(Dark.r, Dark.g, Dark.b, 0.6f));
            bg.raycastTarget = false;
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

        static Button Btn(string name, Transform parent, string label, Vector2 pos, Vector2 size)
        {
            var img = Img(name, parent, ButtonBg);
            Rect(img.gameObject, Center, Center, Center, pos, size);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);
            colors.pressedColor = Cyan;
            colors.selectedColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.4f);
            btn.colors = colors;

            var txt = Txt("Label", img.transform, label, 24f, Color.white, TextAlignmentOptions.Center);
            txt.fontStyle = FontStyles.Bold;
            Stretch(txt.gameObject);
            return btn;
        }
    }
}
