using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// The front end. Lives alone in <c>Assets/Scenes/MainMenu.unity</c> (build index 0) on the
    /// <c>MainMenu</c> prefab, so the game boots into a menu rather than straight into a level.
    ///
    /// There is deliberately NO GameManager, TimeScaleController, Player or HUD in the menu scene:
    /// nothing here is gameplay, so nothing here needs the gameplay singletons. That also makes the
    /// menu the one place where <see cref="GhostRacing"/>'s bootstrap correctly does nothing — it
    /// tests for a <see cref="SpeedrunTimer"/>, which only a level has.
    ///
    /// The level list is DATA: it is read from <see cref="LevelRegistry"/> every time the panel opens.
    /// The builder emits one row per registry entry, and this class clones a row when the live registry
    /// has grown since the menu was last built, so adding a level to the registry needs no code change
    /// and, at worst, no rebuild either.
    ///
    /// Cursor: gameplay locks the cursor through <see cref="GameManager.SetState"/>. The menu scene has
    /// no GameManager, so it unlocks the cursor itself in Awake AND Start — Awake alone is not enough
    /// when arriving from a level, where the previous scene's teardown can run after ours.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        /// <summary>One selectable entry — a campaign level or the sandbox.</summary>
        [Serializable]
        public class Row
        {
            public GameObject root;
            public TMP_Text title;      // display name
            public TMP_Text meta;       // par time + personal best, or the sandbox caption
            public TMP_Text status;     // LOCKED / CLEARED / DEV
            public Button button;
        }

        public static MainMenuController I { get; private set; }

        [Header("Data")]
        [Tooltip("The campaign. Assigned by MainMenuBuilder from Assets/Data/LevelRegistry.asset.")]
        public LevelRegistry registry;

        [Tooltip("Scene loaded by the SANDBOX entry. Not a campaign level and never in the registry.")]
        public string sandboxSceneName = "Sandbox";

        [Header("Panels")]
        public GameObject titlePanel;
        public GameObject levelPanel;

        [Header("Title panel")]
        public Button playButton;
        public Button levelSelectButton;
        public Button quitButton;
        public TMP_Text playSubtitle;

        [Header("Level panel")]
        public Button backButton;
        public Transform rowParent;
        public Row[] rows;
        public Row sandboxRow;

        [Tooltip("Vertical spacing between rows, in canvas units. Used when cloning extra rows for a " +
                 "registry that has grown since the menu prefab was built.")]
        public float rowStride = 96f;

        /// <summary>Test hook: which level the PLAY button would load right now. Empty if none.</summary>
        public string PlayTargetSceneName { get; private set; } = "";

        /// <summary>Test hook: how many campaign rows are currently visible.</summary>
        public int VisibleRowCount { get; private set; }

        void Awake()
        {
            I = this;
            ReleaseCursor();
        }

        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            // Arriving from a level, the old scene's cursor lock can outlive our Awake.
            ReleaseCursor();

            if (playButton != null) playButton.onClick.AddListener(PlayFirstAvailable);
            if (levelSelectButton != null) levelSelectButton.onClick.AddListener(OpenLevelSelect);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
            if (backButton != null) backButton.onClick.AddListener(CloseLevelSelect);

            if (sandboxRow != null && sandboxRow.button != null)
                sandboxRow.button.onClick.AddListener(LoadSandbox);

            ShowTitle();
            Refresh();
        }

        static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // ---- navigation -------------------------------------------------------------------------

        public void ShowTitle()
        {
            if (titlePanel != null) titlePanel.SetActive(true);
            if (levelPanel != null) levelPanel.SetActive(false);
        }

        public void OpenLevelSelect()
        {
            Refresh();
            if (titlePanel != null) titlePanel.SetActive(false);
            if (levelPanel != null) levelPanel.SetActive(true);
            AudioManager.Play(Sfx.Click);
        }

        public void CloseLevelSelect()
        {
            ShowTitle();
            AudioManager.Play(Sfx.Click, 1f, 0.8f);
        }

        // ---- the level list ---------------------------------------------------------------------

        /// <summary>
        /// Rebuild every row from the registry and the saved progress/best times. Called on Start and
        /// every time the level panel opens, so a run finished since boot shows its new best.
        /// </summary>
        public void Refresh()
        {
            var ordered = registry != null ? registry.Ordered() : new LevelDefinition[0];
            EnsureRowCapacity(ordered.Length);

            int visible = 0;
            PlayTargetSceneName = "";

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (row == null) continue;

                if (i >= ordered.Length)
                {
                    if (row.root != null) row.root.SetActive(false);
                    continue;
                }

                var def = ordered[i];
                if (row.root != null) row.root.SetActive(true);
                visible++;

                string id = def.SafeLevelId;
                bool unlocked = LevelProgress.IsUnlocked(id, registry);

                if (row.title != null)
                    row.title.text = (i + 1).ToString("00") + "   " + Safe(def.displayName).ToUpperInvariant();

                if (row.meta != null)
                    row.meta.text = "PAR " + SpeedrunTimer.Format(def.parTime) + "        " + BestLabel(id);

                if (row.status != null)
                {
                    row.status.text = !unlocked ? "LOCKED"
                                    : LevelProgress.IsCompleted(id) ? "CLEARED"
                                    : "";
                }

                if (row.button != null)
                {
                    row.button.interactable = unlocked;
                    row.button.onClick.RemoveAllListeners();
                    string scene = def.sceneName;
                    if (unlocked) row.button.onClick.AddListener(() => LoadScene(scene));
                }

                if (unlocked && string.IsNullOrEmpty(PlayTargetSceneName))
                    PlayTargetSceneName = def.sceneName;
            }

            VisibleRowCount = visible;

            if (playSubtitle != null)
            {
                playSubtitle.text = string.IsNullOrEmpty(PlayTargetSceneName)
                    ? "no level is unlocked"
                    : "";
            }
            if (playButton != null) playButton.interactable = !string.IsNullOrEmpty(PlayTargetSceneName);
        }

        /// <summary>
        /// Personal best for a level. ONE source of truth per number: the leaderboard index
        /// (<see cref="RunStore"/>) owns run times and is what the ghost races against, so it wins;
        /// <see cref="LevelProgress"/> is the fallback for a completion recorded before ghosts existed.
        /// </summary>
        public static string BestLabel(string levelId)
        {
            var pb = RunStore.LoadPersonalBest(levelId);
            if (pb != null) return "BEST " + SpeedrunTimer.Format(pb.TimeSeconds);

            float t = LevelProgress.BestTime(levelId);
            if (t >= 0f) return "BEST " + SpeedrunTimer.Format(t);

            return "BEST  --:--.--";
        }

        /// <summary>
        /// Clone the last built row until there is one per registry entry. The prefab is built with
        /// exactly the registry's length, so this only fires when a level was added to the registry
        /// without rebuilding the menu — which must still work, or the list is not really data-driven.
        /// </summary>
        void EnsureRowCapacity(int needed)
        {
            if (rows == null) rows = new Row[0];
            if (needed <= rows.Length || rows.Length == 0) return;

            var template = rows[rows.Length - 1];
            if (template == null || template.root == null) return;

            var grown = new Row[needed];
            Array.Copy(rows, grown, rows.Length);

            var parent = rowParent != null ? rowParent : template.root.transform.parent;
            var templateRt = template.root.GetComponent<RectTransform>();

            for (int i = rows.Length; i < needed; i++)
            {
                var go = Instantiate(template.root, parent);
                go.name = "LevelRow_" + i;
                var rt = go.GetComponent<RectTransform>();
                if (rt != null && templateRt != null)
                    rt.anchoredPosition = templateRt.anchoredPosition - new Vector2(0f, rowStride * (i - (rows.Length - 1)));

                grown[i] = new Row
                {
                    root = go,
                    title = Find<TMP_Text>(go, template.title),
                    meta = Find<TMP_Text>(go, template.meta),
                    status = Find<TMP_Text>(go, template.status),
                    button = go.GetComponentInChildren<Button>(true),
                };
            }

            rows = grown;
        }

        /// <summary>Find the clone's counterpart of a template child, by name.</summary>
        static T Find<T>(GameObject clone, Component templateChild) where T : Component
        {
            if (templateChild == null) return null;
            foreach (var c in clone.GetComponentsInChildren<T>(true))
                if (c.gameObject.name == templateChild.gameObject.name) return c;
            return null;
        }

        static string Safe(string s) { return string.IsNullOrEmpty(s) ? "UNTITLED" : s; }

        // ---- loading ----------------------------------------------------------------------------

        public void PlayFirstAvailable()
        {
            Refresh();
            if (string.IsNullOrEmpty(PlayTargetSceneName))
            {
                Debug.LogWarning("[MainMenu] Nothing to play: no unlocked level in the registry.");
                return;
            }
            LoadScene(PlayTargetSceneName);
        }

        public void LoadSandbox() { LoadScene(sandboxSceneName); }

        /// <summary>
        /// Load a gameplay scene. Nothing is carried across: the level scene brings its own
        /// GameManager (which re-locks the cursor), TimeScaleController and SpeedrunTimer, and the
        /// timer only starts on the player's first movement input — so a level entered from the menu
        /// begins with the clock at zero exactly as opening the scene directly does.
        /// </summary>
        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[MainMenu] Empty sceneName; check the LevelDefinition.");
                return;
            }
            AudioManager.Play(Sfx.Click);
            SceneManager.LoadScene(sceneName);
        }

        // ---- quit -------------------------------------------------------------------------------

        /// <summary>
        /// Application.Quit does nothing in the editor, which reads as a dead button. Log instead, and
        /// leave play mode so the button demonstrably did something.
        /// </summary>
        public void Quit()
        {
#if UNITY_EDITOR
            Debug.Log("[MainMenu] QUIT — Application.Quit() is a no-op in the editor; leaving play mode instead.");
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
