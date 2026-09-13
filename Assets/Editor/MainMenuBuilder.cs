using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Builds the front end: <c>Assets/Prefabs/MainMenu.prefab</c> (canvas + level select + EventSystem)
    /// and <c>Assets/Scenes/MainMenu.unity</c>, then puts that scene at build index 0 so the game boots
    /// into a menu.
    ///
    /// Same contract as every other generator here: refuses to run in play mode, destroys and rebuilds
    /// only its own roots, and never touches a sibling <c>MainMenu_Manual</c> root.
    ///
    /// The level list is DATA. Rows are emitted one per entry of <c>Assets/Data/LevelRegistry.asset</c>,
    /// so adding a level to the registry and re-running this puts it in the menu with no code change.
    /// (<see cref="MainMenuController"/> also clones a row at runtime if the registry has grown since,
    /// so even a stale prefab lists every level.)
    ///
    /// Palette is the HUD's, deliberately: the menu and the HUD must read as the same game.
    /// </summary>
    public static class MainMenuBuilder
    {
        const string PrefabPath = "Assets/Prefabs/MainMenu.prefab";
        const string ScenePath = "Assets/Scenes/MainMenu.unity";
        const string RegistryPath = "Assets/Data/LevelRegistry.asset";
        const string ManualRootName = "MainMenu_Manual";
        const string SandboxSceneName = "Sandbox";

        const string GameTitle = "VIBEGAME1";
        const string GameSubtitle = "DEFLECT   ·   ASCEND   ·   RUN  IT  AGAIN";

        // Exactly HudBuilder's constants. If these two ever diverge the menu stops looking like the game.
        static readonly Color Cyan = Hex("#7FBFB5");       // ghost teal
        static readonly Color Ember = Hex("#D9891A");      // ember gold
        static readonly Color Gold = Hex("#E0A030");
        static readonly Color Dark = Hex("#06040A");
        static readonly Color Blood = Hex("#B41E2E");
        static readonly Color ButtonBg = Hex("#1A1220");
        static readonly Color Bone = Hex("#E8E2D6");

        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly Vector2 MidLeft = new Vector2(0f, 0.5f);
        static readonly Vector2 MidRight = new Vector2(1f, 0.5f);
        static readonly Vector2 BottomCenter = new Vector2(0.5f, 0f);
        static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);

        [MenuItem("VibeGame1/9. Build Main Menu", priority = 9)]
        public static void Build()
        {
            // PLAY MODE GUARD — this opens, wipes and saves a scene. EditorSceneManager throws in play
            // mode and leaves the scene unsaveable, which is exactly how the level was lost once.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[MainMenuBuilder] Refusing to build during play mode. Exit play mode and try again.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[MainMenuBuilder] Cancelled (unsaved changes kept).");
                return;
            }

            var registry = AssetDatabase.LoadAssetAtPath<LevelRegistry>(RegistryPath);
            if (registry == null)
                Debug.LogWarning($"[MainMenuBuilder] {RegistryPath} not found; the menu is built with no campaign rows " +
                                 "(run VibeGame1/3. Create Data, then rebuild the menu).");

            int levelCount = BuildPrefab(registry);
            BuildScene();
            RegisterInBuildSettings();

            AssetDatabase.SaveAssets();
            Debug.Log($"[MainMenuBuilder] Built {PrefabPath} ({levelCount} campaign row(s) + sandbox) and {ScenePath} " +
                      "at build index 0.");
        }

        // =============================================================== prefab

        static int BuildPrefab(LevelRegistry registry)
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Prefabs"));

            var root = new GameObject("MainMenu");
            int count;
            try
            {
                count = BuildInternal(root, registry);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[MainMenuBuilder] Saved {PrefabPath}", prefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            return count;
        }

        static int BuildInternal(GameObject root, LevelRegistry registry)
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            var menu = root.AddComponent<MainMenuController>();
            menu.registry = registry;
            menu.sandboxSceneName = SandboxSceneName;

            // The title scene has no Managers prefab, but keybind listening belongs in InputReader by
            // hard rule 2. A scene-local reader makes KEYBINDS fully usable before starting a run.
            root.AddComponent<InputReader>();
            var settings = root.AddComponent<SettingsMenu>();

            Transform t = root.transform;

            // ---- backdrop -----------------------------------------------------------------------
            // Flat colour plus stacked low-alpha bands: no sprites exist in this project, so depth is
            // made out of rectangles. Void-black violet ground, one warm eclipse band low and behind.
            var bg = Img("Backdrop", t, Dark);
            bg.raycastTarget = true;    // swallows stray clicks so nothing behind the menu is hit
            Stretch(bg.gameObject);

            // ALPHAS ARE TINY ON PURPOSE. The project renders in LINEAR colour space, so a UI alpha
            // is composited in linear and re-encoded to sRGB: 0.045 of an ember over near-black comes
            // back out at ~#4A2D0A, a flat brown stripe. Roughly a fifth of the "obvious" value is
            // what actually reads as a glow. Nested bands of falling alpha fake the falloff a
            // gradient sprite would give, and this project ships no sprites.
            Band("Glow_1", t, Alpha(Ember, 0.0035f), -240f, 1180f);
            Band("Glow_2", t, Alpha(Ember, 0.0035f), -260f, 900f);
            Band("Glow_3", t, Alpha(Ember, 0.0035f), -280f, 660f);
            Band("Glow_4", t, Alpha(Ember, 0.0040f), -300f, 460f);
            Band("Glow_5", t, Alpha(Ember, 0.0045f), -320f, 300f);
            Band("Glow_6", t, Alpha(Blood, 0.0045f), -360f, 170f);
            Band("Shadow_Top", t, new Color(0f, 0f, 0f, 0.5f), 430f, 220f);

            var levelDefs = registry != null ? registry.Ordered() : new LevelDefinition[0];

            var title = BuildTitlePanel(menu, settings, t);
            var level = BuildLevelPanel(menu, t, levelDefs);

            menu.titlePanel = title;
            menu.levelPanel = level;
            title.SetActive(true);
            level.SetActive(false);

            // The settings panel — the SAME layout the HUD's pause path gets, from the one shared
            // emitter (SettingsPanelKit). Built after the other panels so it draws over them. The
            // title panel is hidden while it is open; there is no PauseMenu here to suspend, and no
            // TimeScaleController either — SettingsMenu skips both when they are absent.
            SettingsPanelKit.BuildPanel(settings, t);
            settings.hideWhileOpen = title;

            // ---- EventSystem (new Input System) -------------------------------------------------
            var es = new GameObject("EventSystem");
            es.transform.SetParent(t, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            return levelDefs.Length;
        }

        // ---------------- title panel ----------------

        static GameObject BuildTitlePanel(MainMenuController menu, SettingsMenu settings, Transform t)
        {
            var panel = new GameObject("TitlePanel", typeof(RectTransform));
            panel.transform.SetParent(t, false);
            Stretch(panel);
            Transform p = panel.transform;

            var title = Txt("Title", p, GameTitle, 128f, Bone, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 18f;
            Rect(title.gameObject, Center, Center, Center, new Vector2(0f, 300f), new Vector2(1600f, 160f));

            // Two rules rather than one: teal over ember reads as the same accent pair the HUD uses.
            var rule = Img("TitleRule", p, new Color(Cyan.r, Cyan.g, Cyan.b, 0.7f));
            rule.raycastTarget = false;
            Rect(rule.gameObject, Center, Center, Center, new Vector2(0f, 222f), new Vector2(760f, 2f));

            var rule2 = Img("TitleRuleEmber", p, new Color(Ember.r, Ember.g, Ember.b, 0.55f));
            rule2.raycastTarget = false;
            Rect(rule2.gameObject, Center, Center, Center, new Vector2(0f, 216f), new Vector2(380f, 1f));

            var sub = Txt("Subtitle", p, GameSubtitle, 20f, new Color(Cyan.r, Cyan.g, Cyan.b, 0.65f), TextAlignmentOptions.Center);
            sub.characterSpacing = 10f;
            Rect(sub.gameObject, Center, Center, Center, new Vector2(0f, 178f), new Vector2(1200f, 30f));

            menu.playButton = MenuButton("PlayButton", p, "PLAY", Ember, new Vector2(0f, 40f));
            menu.levelSelectButton = MenuButton("LevelSelectButton", p, "LEVEL SELECT", Cyan, new Vector2(0f, -40f));
            settings.openButton = MenuButton("SettingsButton", p, "SETTINGS", Gold, new Vector2(0f, -120f));
            menu.quitButton = MenuButton("QuitButton", p, "QUIT", Blood, new Vector2(0f, -200f));

            menu.playSubtitle = Txt("PlaySubtitle", p, "", 16f, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Center);
            Rect(menu.playSubtitle.gameObject, Center, Center, Center, new Vector2(0f, -260f), new Vector2(900f, 24f));

            var footer = Txt("Footer", p, "IN A LEVEL:  ESC pauses  ·  Backquote opens the command console", 15f,
                             new Color(1f, 1f, 1f, 0.28f), TextAlignmentOptions.Center);
            Rect(footer.gameObject, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 32f), new Vector2(1200f, 24f));

            return panel;
        }

        // ---------------- level select panel ----------------

        /// <summary>
        /// One row per <see cref="LevelRegistry"/> entry, plus one sandbox row that is deliberately
        /// separated from the campaign block and marked DEV — it is a practice space, not a level.
        /// </summary>
        static GameObject BuildLevelPanel(MainMenuController menu, Transform t, LevelDefinition[] defs)
        {
            var panel = new GameObject("LevelPanel", typeof(RectTransform));
            panel.transform.SetParent(t, false);
            Stretch(panel);
            Transform p = panel.transform;

            var header = Txt("Header", p, "SELECT A LEVEL", 56f, Bone, TextAlignmentOptions.Center);
            header.fontStyle = FontStyles.Bold;
            header.characterSpacing = 12f;
            Rect(header.gameObject, TopCenter, TopCenter, TopCenter, new Vector2(0f, -70f), new Vector2(1400f, 70f));

            var rule = Img("HeaderRule", p, new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f));
            rule.raycastTarget = false;
            Rect(rule.gameObject, TopCenter, TopCenter, TopCenter, new Vector2(0f, -146f), new Vector2(1160f, 2f));

            var caption = Txt("Caption", p, "par is the target; best is yours", 17f,
                              new Color(1f, 1f, 1f, 0.35f), TextAlignmentOptions.Center);
            caption.characterSpacing = 4f;
            Rect(caption.gameObject, TopCenter, TopCenter, TopCenter, new Vector2(0f, -178f), new Vector2(1160f, 24f));

            var rowsRoot = new GameObject("Rows", typeof(RectTransform));
            rowsRoot.transform.SetParent(p, false);
            Rect(rowsRoot, Center, Center, Center, new Vector2(0f, 0f), new Vector2(1160f, 800f));
            menu.rowParent = rowsRoot.transform;

            // Rows are laid out by hand (no LayoutGroup) so a runtime clone can position itself from
            // one stride, and so the sandbox row can sit apart from the campaign block.
            int n = defs.Length;
            float stride = n + 1 > 8 ? 76f : 96f;
            menu.rowStride = stride;

            // Centre the whole block (campaign rows + the sandbox row a stride and a half below) on
            // the screen, so one level and ten levels are both composed rather than top-hung.
            float span = (n + 0.55f) * stride;
            float top = span * 0.5f;

            menu.rows = new MainMenuController.Row[n];
            for (int i = 0; i < n; i++)
            {
                var accent = TileAccent(i);
                menu.rows[i] = BuildRow("LevelRow_" + i, rowsRoot.transform, accent, top - i * stride, stride);
                var d = defs[i];
                if (menu.rows[i].title != null)
                    menu.rows[i].title.text = (i + 1).ToString("00") + "   " + Up(d.displayName);
                if (menu.rows[i].meta != null)
                    menu.rows[i].meta.text = "PAR " + SpeedrunTimer.Format(d.parTime) + "        BEST  --:--.--";
            }

            float sandboxY = top - (n + 0.55f) * stride;

            var divider = Img("SandboxDivider", rowsRoot.transform, new Color(1f, 1f, 1f, 0.045f));
            divider.raycastTarget = false;
            Rect(divider.gameObject, Center, Center, Center, new Vector2(0f, sandboxY + stride * 0.5f), new Vector2(1100f, 1f));

            menu.sandboxRow = BuildRow("SandboxRow", rowsRoot.transform, Blood, sandboxY, stride);
            if (menu.sandboxRow.title != null) menu.sandboxRow.title.text = "SANDBOX";
            if (menu.sandboxRow.meta != null) menu.sandboxRow.meta.text = "practice arena — spawn anything, no par, no run";
            if (menu.sandboxRow.status != null)
            {
                menu.sandboxRow.status.text = "DEV";
                menu.sandboxRow.status.color = Blood;
            }

            float editorY = sandboxY - stride;
            menu.levelEditorRow = BuildRow("LevelEditorRow", rowsRoot.transform, Cyan, editorY, stride);
            if (menu.levelEditorRow.title != null) menu.levelEditorRow.title.text = "LEVEL EDITOR";
            if (menu.levelEditorRow.meta != null) menu.levelEditorRow.meta.text = "resume the active protected draft";
            if (menu.levelEditorRow.status != null) { menu.levelEditorRow.status.text = "DEV"; menu.levelEditorRow.status.color = Cyan; }
            menu.levelEditorRow.root.SetActive(false);

            // CUSTOM levels (the in-game level editor's saves): a divider and a hidden template row under
            // the sandbox row. MainMenuController clones one per saved file on every Refresh, so the list
            // is data read at runtime, never authored here.
            float customY = editorY - stride;
            var customDivider = Img("CustomDivider", rowsRoot.transform, new Color(1f, 1f, 1f, 0.045f));
            customDivider.raycastTarget = false;
            Rect(customDivider.gameObject, Center, Center, Center, new Vector2(0f, customY + stride * 0.5f), new Vector2(1100f, 1f));
            menu.customTemplate = BuildRow("CustomRowTemplate", rowsRoot.transform, Gold, customY, stride);
            if (menu.customTemplate.title != null) menu.customTemplate.title.text = "CUSTOM";
            if (menu.customTemplate.meta != null) menu.customTemplate.meta.text = "made in the level editor";
            if (menu.customTemplate.status != null) { menu.customTemplate.status.text = "CUSTOM"; menu.customTemplate.status.color = Gold; }
            menu.customTemplate.root.SetActive(false);

            menu.backButton = MenuButton("BackButton", p, "BACK", Cyan, new Vector2(0f, 60f));
            Rect(menu.backButton.gameObject, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 60f), new Vector2(420f, 62f));

            return panel;
        }

        /// <summary>Trim accent per campaign position — the same four the level tiles use, in order.</summary>
        static Color TileAccent(int index)
        {
            switch (index % 4)
            {
                case 0: return Cyan;
                case 1: return Gold;
                case 2: return Blood;
                default: return Ember;
            }
        }

        static MainMenuController.Row BuildRow(string name, Transform parent, Color accent, float y, float stride)
        {
            float h = Mathf.Min(84f, stride - 12f);

            // The whole strip is the button: a 1100 px row with a 300 px hit box would be a trap.
            // Base image is opaque white; the ColorBlock supplies BOTH hue and alpha (the CanvasRenderer
            // tint multiplies the image colour, so a translucent base would darken every state twice).
            var strip = Img(name, parent, Color.white);
            strip.sprite = UiSprites.Track();
            strip.type = Image.Type.Sliced;
            Rect(strip.gameObject, Center, Center, Center, new Vector2(0f, y), new Vector2(1100f, h));

            var btn = strip.gameObject.AddComponent<Button>();
            btn.targetGraphic = strip;
            var colors = btn.colors;
            // Alphas are linear-space (see the backdrop note): 0.045 white reads as mid grey, not a
            // whisper. 0.008 is the dark strip; the hover jump is what makes the row feel alive.
            colors.normalColor = new Color(1f, 1f, 1f, 0.008f);
            colors.highlightedColor = new Color(accent.r, accent.g, accent.b, 0.10f);
            colors.pressedColor = new Color(accent.r, accent.g, accent.b, 0.22f);
            colors.selectedColor = new Color(accent.r, accent.g, accent.b, 0.10f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.46f, 0.010f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var bar = Img("Accent", strip.transform, accent);
            bar.raycastTarget = false;
            bar.sprite = UiSprites.Pill();
            bar.type = Image.Type.Sliced;
            Rect(bar.gameObject, MidLeft, MidLeft, MidLeft, new Vector2(10f, 0f), new Vector2(4f, h - 24f));

            var title = Txt("Title", strip.transform, "LEVEL", 26f, accent, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 4f;
            Rect(title.gameObject, MidLeft, MidLeft, MidLeft, new Vector2(26f, h * 0.22f), new Vector2(700f, 30f));

            var meta = Txt("Meta", strip.transform, "", 18f, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.Left);
            Rect(meta.gameObject, MidLeft, MidLeft, MidLeft, new Vector2(26f, -h * 0.22f), new Vector2(800f, 24f));

            var status = Txt("Status", strip.transform, "", 20f, Gold, TextAlignmentOptions.Right);
            status.fontStyle = FontStyles.Bold;
            status.characterSpacing = 6f;
            Rect(status.gameObject, MidRight, MidRight, MidRight, new Vector2(-26f, 0f), new Vector2(280f, 28f));

            return new MainMenuController.Row
            {
                root = strip.gameObject,
                title = title,
                meta = meta,
                status = status,
                button = btn,
            };
        }

        /// <summary>
        /// A title-screen button: dark plate, coloured accent bar down the left, left-aligned bold
        /// label. Same shape language as a level row, so the two panels read as one screen.
        /// </summary>
        static Button MenuButton(string name, Transform parent, string label, Color accent, Vector2 pos)
        {
            // A pill of glass, like every button in the HUD (HudBuilder.Btn): the plate at 0.75, an
            // edge light along its top, the accent bar down its left, the label in bone.
            var img = Img(name, parent, new Color(ButtonBg.r, ButtonBg.g, ButtonBg.b, 0.75f));
            img.sprite = UiSprites.Pill();
            img.type = Image.Type.Sliced;
            Rect(img.gameObject, Center, Center, Center, pos, new Vector2(420f, 62f));

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(accent.r * 0.9f, accent.g * 0.9f, accent.b * 0.9f, 1f);
            colors.pressedColor = accent;
            colors.selectedColor = new Color(accent.r * 0.9f, accent.g * 0.9f, accent.b * 0.9f, 1f);
            colors.disabledColor = new Color(0.3f, 0.3f, 0.34f, 0.6f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var edge = Img("EdgeLight", img.transform, new Color(Bone.r, Bone.g, Bone.b, 0.24f));
            edge.raycastTarget = false;
            edge.sprite = UiSprites.EdgeLight();
            edge.type = Image.Type.Simple;
            Rect(edge.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(-62f, 1f));

            // The accent is a short bar inside the pill's round end, not a 5 px stripe on a square edge.
            var bar = Img("Accent", img.transform, accent);
            bar.raycastTarget = false;
            bar.sprite = UiSprites.Pill();
            bar.type = Image.Type.Sliced;
            Rect(bar.gameObject, MidLeft, MidLeft, MidLeft, new Vector2(22f, 0f), new Vector2(4f, 28f));

            var txt = Txt("Label", img.transform, label, 24f, Bone, TextAlignmentOptions.Left);
            txt.fontStyle = FontStyles.Bold;
            txt.characterSpacing = 6f;
            Rect(txt.gameObject, MidLeft, MidLeft, MidLeft, new Vector2(40f, 0f), new Vector2(370f, 40f));

            return btn;
        }

        // =============================================================== scene

        static void BuildScene()
        {
            Scene scene;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
            }

            // Destroy only our own roots. A hand-placed MainMenu_Manual root is never touched.
            foreach (var go in new List<GameObject>(scene.GetRootGameObjects()))
            {
                if (go == null || go.name == ManualRootName) continue;
                Object.DestroyImmediate(go);
            }

            // Camera: a ScreenSpaceOverlay canvas draws without one, but a scene with no camera logs
            // "no cameras rendering" every frame and shows a black game view in some layouts.
            var camGo = new GameObject("MenuCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Dark;
            cam.cullingMask = 0;                 // the canvas is overlay; the camera renders nothing else
            cam.orthographic = true;
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";

            // Audio: standalone, not the Managers prefab — that carries GameManager/LevelManager/
            // SpeedrunTimer, and a SpeedrunTimer here would make GhostRacing bootstrap into the menu.
            var audio = new GameObject("MenuAudio");
            audio.AddComponent<AudioManager>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                inst.name = "MainMenu";
            }
            else
            {
                Debug.LogError($"[MainMenuBuilder] {PrefabPath} missing after build; the scene has no menu.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // =============================================================== build settings

        /// <summary>
        /// Put MainMenu at index 0 and leave every other entry alone but reindexed behind it. Index 0
        /// is what "the game boots into a menu" actually means — nothing else enforces it.
        /// </summary>
        static void RegisterInBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s == null || string.Equals(s.path, ScenePath, System.StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        [MenuItem("VibeGame1/Open Main Menu Scene", priority = 21)]
        public static void OpenMainMenuScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[MainMenuBuilder] Cannot open a scene during play mode.");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Debug.LogError($"[MainMenuBuilder] {ScenePath} not found — run VibeGame1/9. Build Main Menu first.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // =============================================================== helpers
        // Same idiom as HudBuilder — deliberately duplicated rather than shared, so a HUD layout tweak
        // can never silently move the menu.

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        static string Up(string s) => string.IsNullOrEmpty(s) ? "UNTITLED" : s.ToUpperInvariant();

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

        /// <summary>A wide, very low alpha horizontal band. Stacked, these fake a gradient without a sprite.</summary>
        static void Band(string name, Transform parent, Color c, float y, float height)
        {
            var img = Img(name, parent, c);
            img.raycastTarget = false;
            Rect(img.gameObject, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Center, new Vector2(0f, y), new Vector2(0f, height));
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
    }
}
