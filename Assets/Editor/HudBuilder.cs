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
        static readonly Color Pink = Hex("#D9891A");      // ember gold (parry juice)
        static readonly Color Yellow = Hex("#E0A030");    // gold (flask, ready label, costs)
        static readonly Color Dark = Hex("#06040A");      // panel / bar backgrounds
        static readonly Color Mint = Hex("#A9D8A0");      // souls
        static readonly Color HealthFill = Hex("#B41E2E"); // blood
        static readonly Color BossRed = Hex("#8A1020");
        static readonly Color ButtonBg = Hex("#1A1220");

        static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
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

            Transform t = root.transform;

            // ---------------- Bottom-left: health / juice / flask ----------------
            var bl = Group("BottomLeft", t, BottomLeft, BottomLeft, BottomLeft, new Vector2(40f, 40f), new Vector2(600f, 160f));

            hud.flaskText = Txt("FlaskText", bl, "FLASK  3 / 3", 20f, Yellow, TextAlignmentOptions.Left);
            Rect(hud.flaskText.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 0f), new Vector2(420f, 26f));

            hud.juiceBar = Bar("JuiceBar", bl, Pink, true, true);
            Rect(hud.juiceBar.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 34f), new Vector2(420f, 12f));

            var juiceLabel = Txt("JuiceLabel", bl, "PARRY JUICE", 16f, Pink, TextAlignmentOptions.Left);
            Rect(juiceLabel.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 48f), new Vector2(200f, 20f));

            var ready = Txt("JuiceReadyLabel", bl, "OVERDRIVE READY  [Q]", 20f, Yellow, TextAlignmentOptions.Left);
            ready.fontStyle = FontStyles.Bold;
            Rect(ready.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 106f), new Vector2(420f, 26f));
            hud.juiceReadyLabel = ready.gameObject;

            hud.healthBar = Bar("HealthBar", bl, HealthFill, true, false);
            Rect(hud.healthBar.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(0f, 80f), new Vector2(420f, 18f));

            hud.healthText = Txt("HealthText", bl, "100 / 100", 22f, Color.white, TextAlignmentOptions.Left);
            Rect(hud.healthText.gameObject, BottomLeft, BottomLeft, BottomLeft, new Vector2(432f, 76f), new Vector2(160f, 26f));

            // ---------------- Top-left: weapon / souls ----------------
            hud.weaponText = Txt("WeaponText", t, "SWORD", 30f, Cyan, TextAlignmentOptions.Left);
            hud.weaponText.fontStyle = FontStyles.Bold;
            Rect(hud.weaponText.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(40f, -30f), new Vector2(500f, 40f));

            hud.soulsText = Txt("SoulsText", t, "SOULS  0", 20f, Mint, TextAlignmentOptions.Left);
            Rect(hud.soulsText.gameObject, TopLeft, TopLeft, TopLeft, new Vector2(40f, -72f), new Vector2(300f, 26f));

            // ---------------- Top-center: timer ----------------
            hud.timerText = Txt("TimerText", t, "00:00.00", 34f, Color.white, TextAlignmentOptions.Center);
            hud.timerText.fontStyle = FontStyles.Bold;
            hud.timerText.characterSpacing = 6f;
            Rect(hud.timerText.gameObject, TopCenter, TopCenter, TopCenter, new Vector2(0f, -28f), new Vector2(320f, 44f));

            // ---------------- Top-right: hints ----------------
            hud.hintText = Txt("HintText", t, "", 14f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Right);
            Rect(hud.hintText.gameObject, TopRight, TopRight, TopRight, new Vector2(-40f, -30f), new Vector2(1100f, 48f));

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

            // ---------------- EventSystem (new Input System) ----------------
            var es = new GameObject("EventSystem");
            es.transform.SetParent(t, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
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
                ghost.type = Image.Type.Filled;
                ghost.fillMethod = Image.FillMethod.Horizontal;
                ghost.fillOrigin = (int)Image.OriginHorizontal.Left;
                ghost.fillAmount = 1f;
                Stretch(ghost.gameObject);
                bar.ghost = ghost;
            }

            var fill = Img("Fill", bg.transform, fillColor);
            fill.raycastTarget = false;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
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
