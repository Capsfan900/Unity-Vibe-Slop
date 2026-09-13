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
        public Row levelEditorRow;
        [Tooltip("Hidden template row for CUSTOM levels (saved by the in-game level editor); cloned per file on Refresh.")]
        public Row customTemplate;
        readonly System.Collections.Generic.List<Row> customRows = new System.Collections.Generic.List<Row>();
        /// <summary>Custom rows shown on the last Refresh. For tests.</summary>
        public int CustomRowCount { get; private set; }
        readonly System.Collections.Generic.List<Row> recordingRows = new System.Collections.Generic.List<Row>();
        /// <summary>Parry recording stage rows shown on the last Refresh (editor + developer only). For tests.</summary>
        public int RecordingRowCount { get; private set; }

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

        void OnDestroy() { DeveloperAccess.Changed -= Refresh; if (I == this) I = null; }

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
            if (levelEditorRow != null && levelEditorRow.button != null)
                levelEditorRow.button.onClick.AddListener(LoadLevelStudioDraft);
            DeveloperAccess.Changed += Refresh;

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

            RefreshDeveloperRows();
            RefreshCustomRows();

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

        /// <summary>
        /// One row per level saved by the in-game editor (persistentDataPath/levels/*.json), cloned from
        /// the hidden template under the sandbox row. Clicking one loads the sandbox scene with the file
        /// queued on <see cref="LevelEditor.PendingLoadPath"/>; the editor there builds and plays it.
        /// </summary>
        void RefreshCustomRows()
        {
            foreach (var r in customRows) if (r != null && r.root != null) Destroy(r.root);
            customRows.Clear();
            foreach (var r in recordingRows) if (r != null && r.root != null) Destroy(r.root);
            recordingRows.Clear();
            CustomRowCount = 0;
            RecordingRowCount = 0;
            if (customTemplate == null || customTemplate.root == null) return;
            if (!DeveloperAccess.IsUnlocked) return;
            var names = LevelEditor.ListSaved();
            for (int i = 0; i < names.Count; i++)
            {
                var row = CloneDeveloperRow("CustomRow_" + i, i);
                string name = names[i];
                if (row.title != null) row.title.text = "CUSTOM   " + name.ToUpperInvariant();
                // Custom rows exist only after the shared developer capability is granted. F10 consumes
                // the same capability, so an unlocked trusted tester may edit the loaded document.
                if (row.meta != null) row.meta.text =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    "made in the level editor  -  F10 in play to edit it";
#else
                    "made in the level editor";
#endif
                if (row.status != null) row.status.text = "CUSTOM";
                if (row.button != null)
                {
                    row.button.interactable = true;
                    row.button.onClick.RemoveAllListeners();
                    row.button.onClick.AddListener(() => { LevelEditor.PendingLoadPath = LevelEditor.PathFor(name); LoadScene(sandboxSceneName); });
                }
                customRows.Add(row);
            }
            CustomRowCount = customRows.Count;
            RefreshRecordingRows(names.Count);
        }

        /// <summary>
        /// One developer row per parry-choreography recording stage (<see cref="ParryRecordingStages"/>),
        /// listed under the custom rows. Editor only: the stages exist to feed Level Studio, and their
        /// scenes are loaded by path so they never need to be in the build scene list.
        /// </summary>
        void RefreshRecordingRows(int firstSlot)
        {
#if UNITY_EDITOR
            var stages = ParryRecordingStages.All;
            for (int i = 0; i < stages.Length; i++)
            {
                var stage = stages[i];
                if (!System.IO.File.Exists(stage.ScenePath)) continue;
                var row = CloneDeveloperRow("RecordingRow_" + i, firstSlot + recordingRows.Count);
                if (row.title != null) row.title.text = "RECORD   " + stage.displayName.ToUpperInvariant();
                if (row.meta != null) row.meta.text = "parry timing stage  -  ` console: timing prime, then 0";
                if (row.status != null) row.status.text = "REC";
                if (row.button != null)
                {
                    row.button.interactable = true;
                    row.button.onClick.RemoveAllListeners();
                    string path = stage.ScenePath;
                    row.button.onClick.AddListener(() => LoadScenePath(path));
                }
                recordingRows.Add(row);
            }
#endif
            RecordingRowCount = recordingRows.Count;
        }

        Row CloneDeveloperRow(string name, int slot)
        {
            var go = Instantiate(customTemplate.root, customTemplate.root.transform.parent);
            go.name = name;
            go.SetActive(true);
            var rt = go.GetComponent<RectTransform>();
            var templateRt = customTemplate.root.GetComponent<RectTransform>();
            if (rt != null && templateRt != null) rt.anchoredPosition = templateRt.anchoredPosition - new Vector2(0f, rowStride * slot);
            return new Row
            {
                root = go,
                title = Find<TMP_Text>(go, customTemplate.title),
                meta = Find<TMP_Text>(go, customTemplate.meta),
                status = Find<TMP_Text>(go, customTemplate.status),
                button = go.GetComponentInChildren<Button>(true),
            };
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

        public void LoadSandbox()
        {
            if (!DeveloperAccess.IsUnlocked)
            {
                Debug.LogWarning("[MainMenu] Sandbox rejected: developer access is locked.");
                return;
            }
            LoadScene(sandboxSceneName);
        }

        public void RefreshDeveloperRows()
        {
            bool developer = DeveloperAccess.IsUnlocked;
            if (sandboxRow != null)
            {
                if (sandboxRow.root != null) sandboxRow.root.SetActive(developer);
                if (sandboxRow.button != null) sandboxRow.button.interactable = developer;
            }
            bool levelStudio = false;
#if UNITY_EDITOR
            levelStudio = developer && LevelEditor.HasResumableDraft != null && LevelEditor.HasResumableDraft();
#endif
            if (levelEditorRow != null)
            {
                if (levelEditorRow.root != null) levelEditorRow.root.SetActive(levelStudio);
                if (levelEditorRow.button != null) levelEditorRow.button.interactable = levelStudio;
            }
        }

        public void LoadLevelStudioDraft()
        {
#if UNITY_EDITOR
            if (!DeveloperAccess.IsUnlocked || LevelEditor.QueueActiveDraftScene == null) return;
            string scene = LevelEditor.QueueActiveDraftScene();
            if (!string.IsNullOrEmpty(scene)) LoadScene(scene);
#endif
        }

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
#if UNITY_EDITOR
            // A Level Studio draft or recording stage can target a scene that is not in the build list;
            // the editor can still play it by path.
            string editorPath = "Assets/Scenes/" + sceneName + ".unity";
            if (!Application.CanStreamedLevelBeLoaded(sceneName) && System.IO.File.Exists(editorPath))
            {
                LoadScenePath(editorPath);
                return;
            }
#endif
            SceneManager.LoadScene(sceneName);
        }

#if UNITY_EDITOR
        /// <summary>Editor-only: play a scene by asset path, whether or not it is in the build list.</summary>
        public void LoadScenePath(string scenePath)
        {
            if (!DeveloperAccess.IsUnlocked) { Debug.LogWarning("[MainMenu] " + scenePath + " rejected: developer access is locked."); return; }
            AudioManager.Play(Sfx.Click);
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
        }
#endif

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
