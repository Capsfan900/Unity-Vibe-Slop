using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VibeGame1
{
    /// <summary>
    /// The in-game level editor, version 1 (2026-09-04 pivot, BACKLOG §0). Place, move, rotate and delete
    /// the pieces a <see cref="LevelDefinition"/> is made of — platforms, wall faces, balloons, water,
    /// enemy spawns, pickups, checkpoints, torches, the player start — from first person, save the
    /// result as data, and play it at once.
    ///
    /// <para><b>The scene is never the level.</b> Every edit mutates a <see cref="LevelDocument"/> and
    /// the piece is rebuilt from it by <see cref="LevelPieceFactory"/> — the same code menu item 8 uses,
    /// so a custom level and a campaign level are the same objects with the same names. Saving writes
    /// the document as JSON under <c>persistentDataPath/levels/</c>; in the Unity editor EXPORT also
    /// writes a real <c>LevelDefinition.asset</c> so the campaign pipeline can take it over. Hard rule 4.</para>
    ///
    /// <para><b>Modes.</b> OFF (ordinary play) → F10 or the F1 menu → EDITING (a fly camera on the player's
    /// own look, the motor idle, the scene's level roots hidden, the custom level built under
    /// <c>CustomLevel</c>) → PLAY (rebuilt fresh, a runtime NavMesh baked, the player dropped at the
    /// start, spawners spawned, game state Playing) → F10 → EDITING again. Gameplay scripts idle
    /// through <c>GameManager.IsPlaying</c> being false in EDITING; the look keeps working through
    /// <c>GameManager.IsEditing</c>. Hard rule 2: every key comes through <see cref="InputReader"/>.</para>
    ///
    /// <para>Hosted on the HUD prefab (built by <c>HudBuilder</c>, every number below written there —
    /// rule 9) so it exists in every gameplay scene without a scene reference.</para>
    /// </summary>
    [DefaultExecutionOrder(-50)]   // before PauseMenu: an Escape spent on a cancel must be seen first
    public class LevelEditor : MonoBehaviour
    {
        public static LevelEditor I { get; private set; }

        /// <summary>Set by the main menu's CUSTOM rows before loading the sandbox: the JSON to load and play.</summary>
        public static string PendingLoadPath;

        public enum Mode { Off, Editing, Playing }
        public Mode CurrentMode { get; private set; }

        // ---------------------------------------------------------------- library (rule 9: HudBuilder writes)
        [Header("Library — keys the factory resolves, written by HudBuilder")]
        public string[] materialKeys = new string[0];
        public Material[] materials = new Material[0];
        public string[] prefabKeys = new string[0];
        public GameObject[] prefabs = new GameObject[0];
        public string[] itemKeys = new string[0];
        public ItemData[] items = new ItemData[0];
        [Tooltip("The prefab keys the SPAWN piece cycles through (V).")]
        public string[] spawnKeys = new string[0];

        // ---------------------------------------------------------------- tuning
        [Header("Flight")]
        public float flySpeed = 12f;
        public float flyFastMultiplier = 3f;
        [Tooltip("Time constant of the fly's velocity ease, seconds. The camera accelerates and coasts to a stop instead of stepping (rule 1: continuous).")]
        public float flyEase = 0.12f;
        [Tooltip("Fly speed multiplier while the cursor is free over the panel, so you are never stuck.")]
        public float flyFreeMultiplier = 0.5f;
        [Tooltip("Time constant of the preview cube's slide to its snapped cell, seconds.")]
        public float previewEase = 0.06f;
        [Tooltip("Fraction of a grid cell the aim must travel PAST a boundary before the preview changes cell.")]
        [Range(0f, 0.5f)] public float snapHysteresis = 0.25f;
        [Tooltip("Seconds the left button must be held on a placed piece before it is GRABBED rather than built on.")]
        public float grabHoldSeconds = 0.18f;
        [Tooltip("How far the aimed piece is tinted toward the accent, 0..1 — enough to say 'this one' without shouting.")]
        [Range(0f, 1f)] public float highlightStrength = 0.35f;
        public Color highlightColor = new Color(0.31f, 0.88f, 0.82f, 1f);
        [Tooltip("Side of the editor's ground plane, metres. The scene is hidden while editing; without this the aim has nothing to land on and you fly into a void.")]
        public float groundSize = 200f;
        public float gridLineSpacing = 4f;
        [Tooltip("Open with the cursor FREE so the panel works on the first click; Tab locks it for look-fly.")]
        public bool enterCursorFree = true;
        [Tooltip("Frames a NEW aim surface height must persist before the preview moves to it. The ray flicks between a piece's top and the ground around every edge; without this the preview hopped (from play, 2026-09-05).")]
        public int surfaceDebounceFrames = 3;
        [Tooltip("How many edits Ctrl+Z can take back.")]
        public int undoDepth = 50;
        [Tooltip("Cells per side of the local grid drawn under the preview on whatever surface it sits on.")]
        public int decalCells = 7;
        [Header("Placement")]
        [Tooltip("Where a piece goes when the aim ray hits nothing: this far along the look.")]
        public float aimDistance = 12f;
        public float maxAimDistance = 80f;
        public float platformThickness = 1f;
        public float wallFaceHeight = 6f;
        public float wallFaceThickness = 0.5f;
        public float waterThickness = 0.04f;
        public float waterFlowSpeed = 6f;
        public float balloonLaunchSpeed = 14f;
        public float balloonRespawnSeconds = 2.5f;
        public float balloonRadius = 0.6f;
        [Tooltip("Hide the scene's own 'Sandbox' / 'Level' roots while editing, so the custom level stands alone.")]
        public bool hideSceneRootsWhileEditing = true;

        // ---------------------------------------------------------------- panel (HudBuilder wires)
        [Header("Panel")]
        public GameObject panel;
        public TMP_Text pieceList;
        public TMP_Text status;
        public TMP_Text help;
        public TMP_InputField nameField;
        public TMP_Text loadLabel;
        public Button newButton, saveButton, loadPrevButton, loadNextButton, loadButton, playButton, exportButton, exitButton;
        [Tooltip("One button per LevelPieceKind, in enum order, built by HudBuilder. Clicking one calls SelectKind: the " +
                 "panel and the wheel/keys share the ONE `kind` field (from play, 2026-09-05: the piece list was plain " +
                 "text, so 'clicking the options' changed nothing).")]
        public Button[] kindButtons = new Button[0];
        public Button placeHereButton, undoButton;
        public TMP_Text readout;      // kind · size · grid · rotation · variant, and the aimed piece
        public GameObject playHint;   // shown in PLAY: "F10 back to the editor"

        // ---------------------------------------------------------------- state
        public LevelDocument Document { get { return doc; } }
        public LevelPieceKind SelectedKind { get { return kind; } }
        public float PendingSize { get { return size; } }
        public string SelectedSpawnKey { get { return spawnKeys.Length > 0 ? spawnKeys[Mathf.Clamp(spawnIndex, 0, spawnKeys.Length - 1)] : "Enemy_Grunt"; } }
        public string SelectedItemKey { get { return itemKeys.Length > 0 ? itemKeys[Mathf.Clamp(itemIndex, 0, itemKeys.Length - 1)] : "Grapple"; } }
        public Transform CustomRoot { get { return customRoot; } }

        LevelDocument doc;
        LevelPieceKind kind = LevelPieceKind.Platform;
        float size = 4f;
        int spawnIndex, itemIndex;
        float yaw;                       // pending yaw for spawns / the player start
        bool axisSwap;                   // pending 90° turn for boxes (x/z swapped)
        Transform customRoot;
        FirstPersonMotor motor;
        CharacterController cc;
        PlayerLook look;
        Transform player, cam;
        Vector3 returnPosition; float returnYaw;
        bool cursorFree;
        readonly List<GameObject> hiddenRoots = new List<GameObject>();
        GameObject preview;
        LevelPiece grabbed;
        // ---- the de-janked controls (2026-09-05) --------------------------------------------------
        Vector3 flyVel;                   // eased fly velocity
        float fastBlend;                  // 0..1 toward the Shift multiplier
        Vector3 previewPos;               // eased preview position
        bool hasPreviewPos;
        float previewYaw;                 // eased preview yaw (the 90° turns animate)
        Vector3 lastSnapped;              // hysteresis memory
        bool hasSnapped;
        float pressStartedAt = -1f;       // left button: short = place, held on a piece = grab
        LevelPiece pressPiece;
        bool pressActive;
        LevelPiece highlighted;
        readonly List<Renderer> highlightRenderers = new List<Renderer>();
        MaterialPropertyBlock highlightBlock;
        GameObject ground, gridLines;
        // ---- the QOL pass (2026-09-05) ------------------------------------------------------------
        Vector3 pressPoint, pressNormal;  // the aim on the PRESS frame: a tap builds where the preview was, not where the aim went by release
        LevelUndoStack undoStack;
        string grabSnapshot;              // the document as it was when the grab began (Escape restores it)
        AimSurfaceFilter surfaceFilter;
        readonly RaycastHit[] aimHits = new RaycastHit[16];
        GameObject gridDecal; Mesh gridDecalMesh; float gridDecalSpacing = -1f; int gridDecalBuiltCells;
        public int UndoCount { get { return undoStack != null ? undoStack.UndoCount : 0; } }
        readonly List<Renderer> hiddenViewmodel = new List<Renderer>();   // the arms, hidden while editing (Renderer.enabled, never SetActive - see ENGINEERING-LOG)
        List<string> saved = new List<string>();
        int loadIndex;
        LevelPieceContext ctx;
        readonly List<EnemySpawner> liveSpawners = new List<EnemySpawner>();

        public static string LevelsDirectory
        {
            get { return Path.Combine(Application.persistentDataPath, "levels"); }
        }

        /// <summary>The piece the next click builds. One field, read by the wheel, the keys and the panel alike.</summary>
        public LevelPieceKind CurrentKind { get { return kind; } }

        void Awake()
        {
            I = this;
            ctx = LevelPieceContext.Runtime(MaterialFor, PrefabFor, ItemFor);
            if (panel != null) panel.SetActive(false);
            if (playHint != null) playHint.SetActive(false);
            for (int i = 0; i < kindButtons.Length; i++)
            {
                var k = (LevelPieceKind)i;
                Wire(kindButtons[i], () => SelectKind(k));
            }
            Wire(newButton, () => NewDocument(nameField != null && !string.IsNullOrEmpty(nameField.text) ? nameField.text : "custom"));
            Wire(saveButton, () => Save(nameField != null && !string.IsNullOrEmpty(nameField.text) ? nameField.text : doc.levelId));
            Wire(loadPrevButton, () => StepLoad(-1));
            Wire(loadNextButton, () => StepLoad(1));
            Wire(loadButton, () => { if (saved.Count > 0) Load(saved[Mathf.Clamp(loadIndex, 0, saved.Count - 1)]); });
            Wire(playButton, () => { if (CurrentMode == Mode.Editing) Play(); else if (CurrentMode == Mode.Playing) BackToEditing(); });
            Wire(exportButton, ExportToAsset);
            Wire(exitButton, Exit);
            Wire(placeHereButton, () => PlaceAtPlayer());
            Wire(undoButton, Undo);
            undoStack = new LevelUndoStack(undoDepth);
            surfaceFilter = new AimSurfaceFilter(surfaceDebounceFrames);
#if !UNITY_EDITOR
            if (exportButton != null) exportButton.gameObject.SetActive(false);
#endif
        }

        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            if (!string.IsNullOrEmpty(PendingLoadPath)) StartCoroutine(LoadPendingAndPlay());
        }

        IEnumerator LoadPendingAndPlay()
        {
            string path = PendingLoadPath;
            PendingLoadPath = null;
            yield return null;   // the scene's own Start()s first (GameManager, LevelManager, the player)
            if (!Enter()) yield break;
            if (!LoadFile(path)) yield break;
            Play();
        }

        static void Wire(Button b, Action a) { if (b != null) b.onClick.AddListener(() => a()); }

        // ---------------------------------------------------------------- library lookups

        Material MaterialFor(string key)
        {
            for (int i = 0; i < materialKeys.Length && i < materials.Length; i++) if (materialKeys[i] == key) return materials[i];
            return null;
        }
        /// <summary>The library lookup. Public so FeatureTests can fetch a melee Enemy_Grunt on a level that only places sentries.</summary>
        public GameObject PrefabFor(string key)
        {
            for (int i = 0; i < prefabKeys.Length && i < prefabs.Length; i++) if (prefabKeys[i] == key) return prefabs[i];
            return null;
        }
        ItemData ItemFor(string key)
        {
            for (int i = 0; i < itemKeys.Length && i < items.Length; i++) if (itemKeys[i] == key) return items[i];
            return null;
        }

        // ---------------------------------------------------------------- mode changes

        /// <summary>Toggle from a key or the F1 menu.</summary>
        public void Toggle()
        {
            if (CurrentMode == Mode.Off) Enter();
            else if (CurrentMode == Mode.Playing) BackToEditing();
            else Exit();
        }

        /// <summary>Enter EDITING over the current scene. Returns false when there is no player to fly.</summary>
        public bool Enter()
        {
            if (CurrentMode != Mode.Off) return true;
            motor = FindAnyObjectByType<FirstPersonMotor>();
            if (motor == null) { Debug.LogWarning("[LevelEditor] No FirstPersonMotor in the scene; nothing to fly."); return false; }
            player = motor.transform;
            cc = motor.GetComponent<CharacterController>();
            look = motor.GetComponent<PlayerLook>();
            var c = motor.GetComponentInChildren<Camera>();
            cam = c != null ? c.transform : player;
            returnPosition = player.position;
            returnYaw = player.eulerAngles.y;

            if (doc == null) doc = LevelDocument.NewDefault("custom");
            HideSceneRoots();
            HideViewmodel();
            BuildGround();
            Rebuild();

            CurrentMode = Mode.Editing;
            if (GameManager.I != null) GameManager.I.SetState(GameState.Editing);
            if (cc != null) cc.enabled = false;
            // Start above the ground looking at the origin, whatever the scene had the player doing.
            player.position = doc.playerStart + new Vector3(0f, 4f, -8f);
            if (look != null) { look.SetYaw(0f); look.NudgeAim(0f, 28f); }   // a little down: the aim lands on the ground, not the horizon
            flyVel = Vector3.zero; hasSnapped = false; hasPreviewPos = false;
            if (surfaceFilter != null) surfaceFilter.Reset();
            if (undoStack != null) undoStack.Clear();
            SetCursor(enterCursorFree);
            if (panel != null) panel.SetActive(true);
            if (playHint != null) playHint.SetActive(false);
            RefreshSavedList();
            RefreshPanel();
            return true;
        }

        /// <summary>Leave the editor entirely: the custom level is torn down, the scene's roots come back.</summary>
        public void Exit()
        {
            if (CurrentMode == Mode.Off) return;
            if (CurrentMode == Mode.Playing) DespawnAll();
            Highlight(null);
            TearDown();
            DestroyGround();
            DestroyGridDecal();
            ShowViewmodel();
            RestoreSceneRoots();
            CurrentMode = Mode.Off;
            if (cc != null) cc.enabled = true;
            if (motor != null) motor.Teleport(returnPosition, returnYaw);
            if (GameManager.I != null) GameManager.I.SetState(GameState.Playing);
            if (panel != null) panel.SetActive(false);
            if (playHint != null) playHint.SetActive(false);
            GameEvents.RaisePromptChanged("");
        }

        /// <summary>Rebuild the level fresh, bake a NavMesh for it, drop the player at the start and play.</summary>
        public void Play()
        {
            if (CurrentMode != Mode.Editing) return;
            Highlight(null);
            if (gridLines != null) gridLines.SetActive(false);
            if (gridDecal != null) gridDecal.SetActive(false);
            ShowViewmodel();
            Rebuild();
            BakeNavMesh();
            CurrentMode = Mode.Playing;
            if (cc != null) cc.enabled = true;
            if (motor != null) motor.Teleport(doc.playerStart, doc.playerStartYaw);
            if (GameManager.I != null) GameManager.I.SetState(GameState.Playing);
            SpawnAll();
            if (panel != null) panel.SetActive(false);
            if (playHint != null) playHint.SetActive(true);
            GameEvents.RaisePromptChanged("PLAYING  " + doc.displayName + "   [F10] back to the editor");
        }

        public void BackToEditing()
        {
            if (CurrentMode != Mode.Playing) return;
            DespawnAll();
            CurrentMode = Mode.Editing;
            if (GameManager.I != null) GameManager.I.SetState(GameState.Editing);
            if (cc != null) cc.enabled = false;
            if (gridLines != null) gridLines.SetActive(true);
            HideViewmodel();
            flyVel = Vector3.zero; hasSnapped = false; hasPreviewPos = false;
            SetCursor(enterCursorFree);
            if (panel != null) panel.SetActive(true);
            if (playHint != null) playHint.SetActive(false);
            GameEvents.RaisePromptChanged("");
            RefreshPanel();
        }

        // ---------------------------------------------------------------- documents

        public void NewDocument(string name)
        {
            if (undoStack != null) undoStack.Clear();
            doc = LevelDocument.NewDefault(name);
            if (nameField != null) nameField.text = doc.levelId;
            if (CurrentMode != Mode.Off) Rebuild();
            RefreshPanel();
        }

        public void Save(string name)
        {
            if (doc == null) return;
            doc.levelId = LevelEditorMath.SafeFileName(name);
            if (string.IsNullOrEmpty(doc.displayName) || doc.displayName == "Custom level") doc.displayName = doc.levelId;
            Directory.CreateDirectory(LevelsDirectory);
            string path = PathFor(doc.levelId);
            File.WriteAllText(path, doc.ToJson());
            Debug.Log("[LevelEditor] Saved " + doc.PieceCount + " pieces to " + path);
            RefreshSavedList();
            RefreshPanel();
        }

        public bool Load(string name) { return LoadFile(PathFor(name)); }

        public bool LoadFile(string path)
        {
            if (!File.Exists(path)) { Debug.LogWarning("[LevelEditor] No level file at " + path); return false; }
            var d = LevelDocument.FromJson(File.ReadAllText(path));
            if (d == null) { Debug.LogWarning("[LevelEditor] Could not parse " + path); return false; }
            doc = d;
            if (undoStack != null) undoStack.Clear();
            if (nameField != null) nameField.text = doc.levelId;
            if (CurrentMode != Mode.Off) Rebuild();
            RefreshPanel();
            return true;
        }

        public static string PathFor(string name)
        {
            return Path.Combine(LevelsDirectory, LevelEditorMath.SafeFileName(name) + ".json");
        }

        /// <summary>Every saved custom level, by name (file stem), sorted.</summary>
        public static List<string> ListSaved()
        {
            var list = new List<string>();
            if (!Directory.Exists(LevelsDirectory)) return list;
            foreach (var f in Directory.GetFiles(LevelsDirectory, "*.json")) list.Add(Path.GetFileNameWithoutExtension(f));
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        void RefreshSavedList()
        {
            saved = ListSaved();
            loadIndex = Mathf.Clamp(loadIndex, 0, Mathf.Max(0, saved.Count - 1));
        }

        void StepLoad(int dir)
        {
            if (saved.Count == 0) return;
            loadIndex = (loadIndex + dir + saved.Count) % saved.Count;
            RefreshPanel();
        }

        /// <summary>Editor only: write the document as a real LevelDefinition asset for menu item 8.</summary>
        public void ExportToAsset()
        {
#if UNITY_EDITOR
            if (doc == null) return;
            const string dir = "Assets/Data/Levels/Custom";
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data/Levels")) UnityEditor.AssetDatabase.CreateFolder("Assets/Data", "Levels");
            if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Data/Levels", "Custom");
            string path = dir + "/" + LevelEditorMath.SafeFileName(doc.levelId) + "_Level.asset";
            var def = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
            bool fresh = def == null;
            if (fresh) def = ScriptableObject.CreateInstance<LevelDefinition>();
            doc.CopyTo(def);
            // 8. Build Level From Definition builds INTO the open scene and refuses a mismatch, so a custom
            // level names its own scene rather than the sandbox it was made in.
            def.sceneName = "Custom_" + LevelEditorMath.SafeFileName(doc.levelId);
            def.orderIndex = 100;
            if (fresh) UnityEditor.AssetDatabase.CreateAsset(def, path);
            UnityEditor.EditorUtility.SetDirty(def);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log("[LevelEditor] Exported " + path + " (sceneName " + def.sceneName + "). Create that scene, open it, select the asset, run VibeGame1/8.");
            if (status != null) status.text = "exported " + path;
#else
            Debug.Log("[LevelEditor] Export is an editor-only bridge; the JSON under " + LevelsDirectory + " is the build's format.");
#endif
        }

        // ---------------------------------------------------------------- building

        /// <summary>Tear down and rebuild the whole custom level from the document.</summary>
        public void Rebuild()
        {
            TearDown();
            var go = new GameObject("CustomLevel");
            customRoot = go.transform;
            LevelPieceFactory.BuildDocument(doc, customRoot, ctx);
        }

        void TearDown()
        {
            if (customRoot != null) Destroy(customRoot.gameObject);
            customRoot = null;
            if (preview != null) { Destroy(preview); preview = null; }
            grabbed = null; pressActive = false; pressPiece = null;
            highlighted = null; highlightRenderers.Clear();
        }

        void BakeNavMesh()
        {
            if (customRoot == null) return;
            try
            {
                var surface = customRoot.gameObject.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
                surface.collectObjects = Unity.AI.Navigation.CollectObjects.Children;
                surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;
                surface.layerMask = 1 << 0;
                surface.BuildNavMesh();
            }
            catch (Exception e)
            {
                // Enemies without a mesh stand still (NavMeshLocomotion.IsReady guards every call); they do not throw.
                Debug.LogWarning("[LevelEditor] Runtime NavMesh bake failed; enemies will stand still. " + e.Message);
            }
        }

        void SpawnAll()
        {
            liveSpawners.Clear();
            if (customRoot == null) return;
            foreach (var s in customRoot.GetComponentsInChildren<EnemySpawner>(true)) { s.Spawn(); liveSpawners.Add(s); }
        }

        void DespawnAll()
        {
            foreach (var s in liveSpawners) if (s != null) s.Despawn();
            liveSpawners.Clear();
        }

        void HideSceneRoots()
        {
            hiddenRoots.Clear();
            if (!hideSceneRootsWhileEditing) return;
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go != null && go.activeSelf && (go.name == "Sandbox" || go.name == "Level")) { go.SetActive(false); hiddenRoots.Add(go); }
        }

        void RestoreSceneRoots()
        {
            foreach (var go in hiddenRoots) if (go != null) go.SetActive(true);
            hiddenRoots.Clear();
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            var input = InputReader.I;
            if (input == null) return;

            if (CurrentMode == Mode.Off)
            {
                if (input.LevelEditorPressed && GameManager.IsPlaying) Enter();
                return;
            }
            if (CurrentMode == Mode.Playing)
            {
                if (input.LevelEditorPressed) BackToEditing();
                return;
            }

            // EDITING. A pause round-trip (Escape) hands the state back as Playing; re-assert ours.
            if (GameManager.I != null && GameManager.I.State == GameState.Playing) GameManager.I.SetState(GameState.Editing);
            if (input.LevelEditorPressed) { Exit(); return; }
            if (input.EditorCursorPressed) SetCursor(!cursorFree);

            float dt = Time.unscaledDeltaTime;
            Fly(input, dt);

            // The pointer over the panel belongs to the panel: no placing, deleting or grabbing under it.
            bool overUi = cursorFree && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            // ---- the wheel: size; with Shift the piece kind; with Ctrl the variant (keys stay as aliases).
            // The wheel works over the panel too: nothing on it scrolls, and "wheel does nothing while the
            // cursor is over the panel" read as the size being broken (from play, 2026-09-05).
            int wheel = (input.EditorWheelUpPressed ? 1 : 0) - (input.EditorWheelDownPressed ? 1 : 0);
            if (input.EditorNextPressed) CycleKind(1);
            if (input.EditorPrevPressed) CycleKind(-1);
            if (input.EditorVariantPressed) CycleVariant();
            int sizeStep = 0;
            if (wheel != 0)
            {
                if (input.EditorFastHeld) CycleKind(wheel);
                else if (input.EditorDownHeld) CycleVariant();
                else sizeStep = wheel;
            }
            if (input.EditorGrowPressed) sizeStep = 1;
            if (input.EditorShrinkPressed) sizeStep = -1;
            if (input.EditorRotatePressed) { axisSwap = !axisSwap; yaw = LevelEditorMath.RotateStep(yaw, input.EditorFastHeld ? -1 : 1); }
            if (input.EditorUndoPressed) Undo();
            if (input.EditorRedoPressed) Redo();
            if (input.EditorPlaceHerePressed && !overUi) PlaceAtPlayer();

            Vector3 point; Vector3 normal; LevelPiece under;
            Aim(input, input.EditorFreeHeld, out point, out normal, out under);
            if (overUi) under = null;

            // ---- Escape / Backspace: cancel the grab (the piece goes back where it was) or the pending press.
            // The pause menu must not open on the same key: the editor runs first and suppresses it.
            if (input.EditorCancelPressed && (grabbed != null || pressActive))
            {
                input.SuppressPauseThisFrame();
                CancelGrab();
                pressActive = false; pressPiece = null; grabbed = null;
                under = null;
            }

            // ---- left button: a short press builds, a hold on a piece grabs it, release drops it.
            // The press REMEMBERS its aim: a tap builds where the preview was when you pressed, not
            // where the aim had drifted to by the release frame (from play: "placing is janky").
            if (input.EditorPlacePressed && !overUi)
            {
                pressActive = true;
                pressStartedAt = Time.unscaledTime;
                pressPiece = under != null && under.kind != LevelPieceKind.PlayerStart ? under : null;
                pressPoint = point; pressNormal = normal;
            }
            bool holdGrab = input.EditorGrabHeld;   // G: the old way, kept as an alias
            if (pressActive && input.EditorPlaceHeld)
            {
                if (grabbed == null && pressPiece != null && Time.unscaledTime - pressStartedAt >= grabHoldSeconds) BeginGrab(pressPiece);
            }
            else if (pressActive)
            {
                // released
                if (grabbed == null && !overUi) Place(pressPoint, pressNormal);
                grabbed = null; pressActive = false; pressPiece = null;
            }
            if (holdGrab && grabbed == null && under != null && under.kind != LevelPieceKind.PlayerStart) BeginGrab(under);
            if (!holdGrab && !pressActive) grabbed = null;

            // ---- arrow keys nudge the grabbed or aimed piece one cell (Ctrl: up / down)
            Vector2 nudge = input.EditorNudgePressed;
            LevelPiece nudgeTarget = grabbed != null ? grabbed : under;
            if (nudge != Vector2.zero && nudgeTarget != null && nudgeTarget.kind != LevelPieceKind.PlayerStart)
            {
                Vector3 fwd = cam != null ? cam.forward : Vector3.forward;
                var delta = LevelEditorMath.NudgeDelta(nudge, fwd, LevelEditorMath.GridFor(nudgeTarget.kind), input.EditorDownHeld);
                if (grabbed == null) PushUndo();
                SetAnchor(nudgeTarget, GetAnchor(nudgeTarget) + delta);
            }
            if (input.EditorDuplicatePressed && under != null && grabbed == null && !overUi) { DuplicatePiece(under); under = null; }
            if (input.EditorPickPressed && under != null && grabbed == null && !overUi) { PickPiece(under); RefreshPanel(under); }

            if (grabbed != null)
            {
                MoveGrabbed(grabbed, point);
                HidePreview();
                HideGridDecal();
                Highlight(grabbed);
                RefreshPanel(grabbed);
                return;
            }

            if (input.EditorDeletePressed && under != null && !overUi) { Highlight(null); DeletePiece(under); under = null; }
            else if (sizeStep != 0)
            {
                if (under != null && IsResizable(under)) under = ResizePiece(under, sizeStep);
                else size = LevelEditorMath.StepSize(size, sizeStep);
            }

            Highlight(under);
            if (overUi) { HidePreview(); HideGridDecal(); }
            else { ShowPreview(point, dt); ShowGridDecal(point, normal); }
            RefreshPanel(under);
        }

        /// <summary>A grab starts with a snapshot: Escape puts the piece back, and Ctrl+Z afterwards undoes the move as one step.</summary>
        void BeginGrab(LevelPiece piece)
        {
            if (piece == null || grabbed == piece) return;
            grabSnapshot = doc != null ? doc.ToJson() : null;
            PushUndo();
            grabbed = piece;
        }

        /// <summary>Escape during a grab: the document goes back to the moment the grab began.</summary>
        public void CancelGrab()
        {
            if (grabbed == null || grabSnapshot == null) { grabbed = null; return; }
            var d = LevelDocument.FromJson(grabSnapshot);
            if (d != null) { doc = d; Rebuild(); }
            grabbed = null; grabSnapshot = null;
            // the snapshot pushed at BeginGrab is now a no-op step; drop it so Ctrl+Z is not a dud
            if (undoStack != null) undoStack.Undo(doc.ToJson());
            RefreshPanel();
        }

        // ---------------------------------------------------------------- undo / redo

        void PushUndo() { if (undoStack != null && doc != null) undoStack.Push(doc.ToJson()); }

        public void Undo()
        {
            if (undoStack == null || doc == null) return;
            string s = undoStack.Undo(doc.ToJson());
            if (s == null) return;
            var d = LevelDocument.FromJson(s);
            if (d == null) return;
            doc = d; grabbed = null; pressActive = false;
            if (CurrentMode != Mode.Off) Rebuild();
            RefreshPanel();
        }

        public void Redo()
        {
            if (undoStack == null || doc == null) return;
            string s = undoStack.Redo(doc.ToJson());
            if (s == null) return;
            var d = LevelDocument.FromJson(s);
            if (d == null) return;
            doc = d; grabbed = null; pressActive = false;
            if (CurrentMode != Mode.Off) Rebuild();
            RefreshPanel();
        }

        /// <summary>Build the selected piece where the camera is: on the surface straight below it, else 2 m under it.</summary>
        public GameObject PlaceAtPlayer()
        {
            if (CurrentMode != Mode.Editing || cam == null) return null;
            Vector3 origin = cam.position;
            RaycastHit hit; Vector3 point;
            if (AimRay(origin, Vector3.down, null, out hit)) point = hit.point; else point = origin + Vector3.down * 2f;
            point = LevelEditorMath.Snap(point, LevelEditorMath.GridFor(kind));
            return Place(point, Vector3.up);
        }

        /// <summary>Ctrl+D: a copy of the aimed piece one grid cell further along the look.</summary>
        public GameObject DuplicatePiece(LevelPiece piece)
        {
            if (piece == null || doc == null) return null;
            Vector3 fwd = cam != null ? cam.forward : Vector3.forward;
            float grid = LevelEditorMath.GridFor(piece.kind);
            Vector3 delta = LevelEditorMath.NudgeDelta(new Vector2(0f, 1f), fwd, grid, false);
            PushUndo();
            int idx;
            switch (piece.kind)
            {
                case LevelPieceKind.Platform:
                    if (piece.index >= doc.platforms.Count) return null;
                    { var s = doc.platforms[piece.index]; idx = doc.platforms.Count;
                      var c = new PlatformDef { name = (s.name.StartsWith("Wall_") ? "Wall_" : "Platform_") + idx, center = s.center + delta, size = s.size,
                                                materialKey = s.materialKey, trim = s.trim, trimMaterialKey = s.trimMaterialKey, isStatic = s.isStatic };
                      doc.platforms.Add(c); return LevelPieceFactory.Platform(c, customRoot, ctx, null, idx); }
                case LevelPieceKind.Balloon:
                    if (piece.index >= doc.balloons.Count) return null;
                    { var s = doc.balloons[piece.index]; idx = doc.balloons.Count;
                      var c = new BalloonDef { name = "Balloon_" + idx, position = s.position + delta, launchSpeed = s.launchSpeed, respawnSeconds = s.respawnSeconds, radius = s.radius };
                      doc.balloons.Add(c); return LevelPieceFactory.Balloon(c, customRoot, ctx, null, idx); }
                case LevelPieceKind.Water:
                    if (piece.index >= doc.waters.Count) return null;
                    { var s = doc.waters[piece.index]; idx = doc.waters.Count;
                      var c = new WaterDef { name = "Water_" + idx, center = s.center + delta, size = s.size, flowDirection = s.flowDirection, flowSpeed = s.flowSpeed };
                      doc.waters.Add(c); return LevelPieceFactory.Water(c, customRoot, ctx, null, idx); }
                case LevelPieceKind.Spawn:
                    if (piece.index >= doc.spawns.Count) return null;
                    { var s = doc.spawns[piece.index]; idx = doc.spawns.Count;
                      var c = new SpawnDef { name = "Spawn_" + s.prefabKey + "_" + idx, prefabKey = s.prefabKey, position = s.position + delta, yaw = s.yaw, isBoss = false };
                      doc.spawns.Add(c); return LevelPieceFactory.Spawner(c, customRoot, ctx, null, idx); }
                case LevelPieceKind.Pickup:
                    if (piece.index >= doc.pickups.Count) return null;
                    { var s = doc.pickups[piece.index]; idx = doc.pickups.Count;
                      var c = new PickupDef { name = "Pickup_" + s.itemKey + "_" + idx, itemKey = s.itemKey, position = s.position + delta };
                      doc.pickups.Add(c); return LevelPieceFactory.Pickup(c, LevelPieceFactory.Group("Pickups", customRoot), ctx, null, idx); }
                case LevelPieceKind.Checkpoint:
                    if (piece.index >= doc.checkpoints.Count) return null;
                    { var s = doc.checkpoints[piece.index]; idx = doc.checkpoints.Count;
                      var c = new CheckpointDef { name = "Checkpoint_" + (idx + 1), position = s.position + delta, spawnOffset = s.spawnOffset };
                      doc.checkpoints.Add(c); return LevelPieceFactory.Checkpoint(c, customRoot, ctx, null, idx); }
                case LevelPieceKind.Torch:
                    if (piece.index >= doc.torches.Count) return null;
                    { var s = doc.torches[piece.index]; idx = doc.torches.Count;
                      var c = new TorchDef { name = "Torch_" + idx, basePosition = s.basePosition + delta };
                      doc.torches.Add(c); return LevelPieceFactory.Torch(c, LevelPieceFactory.Group("Torches", customRoot), ctx, null, idx); }
            }
            return null;
        }

        /// <summary>The eyedropper (`I`): the aimed piece becomes the pending selection — kind, and its
        /// variant / size — so the next click builds another one just like it. Mirrors what a click on the
        /// panel or the wheel would set (from research: every 3D editor has a pick tool, we had none).</summary>
        void PickPiece(LevelPiece piece)
        {
            if (piece == null || doc == null) return;
            kind = piece.kind;
            switch (piece.kind)
            {
                case LevelPieceKind.Spawn:
                    if (piece.index < doc.spawns.Count)
                    {
                        int i = System.Array.IndexOf(spawnKeys, doc.spawns[piece.index].prefabKey);
                        if (i >= 0) spawnIndex = i;
                    }
                    break;
                case LevelPieceKind.Pickup:
                    if (piece.index < doc.pickups.Count)
                    {
                        int i = System.Array.IndexOf(itemKeys, doc.pickups[piece.index].itemKey);
                        if (i >= 0) itemIndex = i;
                    }
                    break;
                case LevelPieceKind.Platform:
                    if (piece.index < doc.platforms.Count)
                    {
                        var s = doc.platforms[piece.index].size;
                        size = s.x; axisSwap = !Mathf.Approximately(s.z, s.x);
                    }
                    break;
                case LevelPieceKind.Water:
                    if (piece.index < doc.waters.Count)
                    {
                        var s = doc.waters[piece.index].size;
                        size = s.x; axisSwap = !Mathf.Approximately(s.z, s.x);
                    }
                    break;
            }
            hasSnapped = false;
        }

        /// <summary>A piece's AIM anchor — the point a click at the same spot would have placed it from (a slab's top centre, a balloon's floor point…).</summary>
        Vector3 GetAnchor(LevelPiece piece)
        {
            switch (piece.kind)
            {
                case LevelPieceKind.Platform:
                    if (piece.index < doc.platforms.Count) { var p = doc.platforms[piece.index]; return p.name.StartsWith("Wall_") ? p.center - Vector3.up * (p.size.y * 0.5f) : p.center + Vector3.up * (p.size.y * 0.5f); }
                    break;
                case LevelPieceKind.Balloon: if (piece.index < doc.balloons.Count) return doc.balloons[piece.index].position - Vector3.up * 1.2f; break;
                case LevelPieceKind.Water: if (piece.index < doc.waters.Count) { var w = doc.waters[piece.index]; return w.center - Vector3.up * (w.size.y * 0.5f); } break;
                case LevelPieceKind.Spawn: if (piece.index < doc.spawns.Count) return doc.spawns[piece.index].position - Vector3.up * 0.3f; break;
                case LevelPieceKind.Pickup: if (piece.index < doc.pickups.Count) return doc.pickups[piece.index].position - Vector3.up * 1.2f; break;
                case LevelPieceKind.Checkpoint: if (piece.index < doc.checkpoints.Count) return doc.checkpoints[piece.index].position; break;
                case LevelPieceKind.Torch: if (piece.index < doc.torches.Count) return doc.torches[piece.index].basePosition; break;
            }
            return piece.transform.position;
        }

        void SetAnchor(LevelPiece piece, Vector3 anchor) { MoveGrabbed(piece, anchor); }

        static bool IsResizable(LevelPiece piece) { return piece != null && (piece.kind == LevelPieceKind.Platform || piece.kind == LevelPieceKind.Water); }

        /// <summary>The nearest hit along a ray on the aim mask, skipping <paramref name="ignore"/> (the piece being carried must not catch its own aim ray).</summary>
        bool AimRay(Vector3 origin, Vector3 dir, LevelPiece ignore, out RaycastHit best)
        {
            best = new RaycastHit();
            int mask = (1 << 0) | (1 << Layers.Interactable);
            int n = Physics.RaycastNonAlloc(origin, dir, aimHits, maxAimDistance, mask, QueryTriggerInteraction.Collide);
            float bestD = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                var h = aimHits[i];
                if (h.distance >= bestD) continue;
                if (ignore != null) { var lp = LevelPiece.Find(h.transform); if (lp == ignore) continue; }
                best = h; bestD = h.distance; any = true;
            }
            return any;
        }

        /// <summary>
        /// Free flight with an eased velocity: the camera accelerates and coasts rather than stepping
        /// (MOVEMENT-PRINCIPLES rule 1), the Shift multiplier blends in rather than switching, and the
        /// ease is a time constant so it is the same at any frame rate (rule 8). With the cursor free
        /// the keys still fly, slower, so the panel never strands you.
        /// </summary>
        void Fly(InputReader input, float dt)
        {
            if (player == null) return;
            Vector2 m = input.MoveAxis - input.ArrowAxis;   // the arrows nudge pieces; they do not fly
            m = Vector2.ClampMagnitude(m, 1f);
            Vector3 fwd = cam != null ? cam.forward : player.forward;
            Vector3 right = cam != null ? cam.right : player.right;
            Vector3 v = fwd * m.y + right * m.x;
            if (input.JumpHeld) v += Vector3.up;
            if (input.EditorDownHeld) v -= Vector3.up;
            if (v.sqrMagnitude > 1f) v.Normalize();

            fastBlend = Mathf.Lerp(fastBlend, input.EditorFastHeld ? 1f : 0f, LevelEditorMath.EaseFactor(flyEase, dt));
            float speed = flySpeed * Mathf.Lerp(1f, flyFastMultiplier, fastBlend) * (cursorFree ? flyFreeMultiplier : 1f);
            flyVel = Vector3.Lerp(flyVel, v * speed, LevelEditorMath.EaseFactor(flyEase, dt));
            if (flyVel.sqrMagnitude < 0.0004f && v.sqrMagnitude < 0.0001f) flyVel = Vector3.zero;
            player.position += flyVel * dt;
        }

        void SetCursor(bool free)
        {
            cursorFree = free;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        void CycleKind(int dir)
        {
            int n = Enum.GetValues(typeof(LevelPieceKind)).Length;
            kind = (LevelPieceKind)(((int)kind + dir + n) % n);
            hasSnapped = false;   // a new kind may snap to a different grid
        }

        void CycleVariant()
        {
            if (kind == LevelPieceKind.Spawn && spawnKeys.Length > 0) spawnIndex = (spawnIndex + 1) % spawnKeys.Length;
            else if (kind == LevelPieceKind.Pickup && itemKeys.Length > 0) itemIndex = (itemIndex + 1) % itemKeys.Length;
        }

        /// <summary>
        /// Where the aim lands: the camera's centre ray while the cursor is locked, the POINTER's ray
        /// while it is free — on level geometry, the editor's ground, or aimDistance out. Snapped with
        /// hysteresis so the preview holds its cell on a boundary instead of flickering.
        /// </summary>
        void Aim(InputReader input, bool free, out Vector3 point, out Vector3 normal, out LevelPiece under)
        {
            under = null; normal = Vector3.up;
            Vector3 origin = cam != null ? cam.position : player.position;
            Vector3 dir = cam != null ? cam.forward : player.forward;
            if (cursorFree && cam != null)
            {
                var camComp = cam.GetComponent<Camera>();
                if (camComp != null) { var ray = camComp.ScreenPointToRay(input.PointerPosition); origin = ray.origin; dir = ray.direction; }
            }
            RaycastHit hit;
            // The piece being carried is skipped: with it in the mask the ray hit the carried slab's own top,
            // so the slab chased its own hit point toward the camera and never came down off a surface —
            // the "snap fights the grab" the user felt (measured 2026-09-05: 1 s of G-hold drifted it).
            if (AimRay(origin, dir, grabbed, out hit))
            {
                point = hit.point; normal = hit.normal;
                under = LevelPiece.Find(hit.transform);
            }
            else point = origin + dir * aimDistance;

            float grid = free ? 0f : LevelEditorMath.GridFor(kind);
            // Which SURFACE the preview sits on is debounced: the ray flicks between a piece's top and the
            // ground around every edge, and following each flick hopped the preview up and down.
            if (surfaceFilter != null && grid > 0f) point.y = surfaceFilter.Filter(point.y, grid * 0.25f);
            point = LevelEditorMath.SnapWithHysteresis(point, lastSnapped, hasSnapped && grid > 0f, grid, snapHysteresis);
            lastSnapped = point; hasSnapped = grid > 0f;
        }

        // ---------------------------------------------------------------- the grid decal

        /// <summary>A small cyan grid on whatever surface the preview sits on, so the cells read on a
        /// placed platform as well as on the editor ground. One line mesh, moved each frame, rebuilt only
        /// when the grid spacing changes.</summary>
        void ShowGridDecal(Vector3 point, Vector3 normal)
        {
            float spacing = LevelEditorMath.GridFor(kind);
            if (normal.y < 0.7f || spacing <= 0f) { HideGridDecal(); return; }
            if (gridDecal == null)
            {
                gridDecal = new GameObject("~EditorGridDecal");
                var mf = gridDecal.AddComponent<MeshFilter>();
                var mr = gridDecal.AddComponent<MeshRenderer>();
                var m = MaterialFor("NeonCyan");
                if (m != null) mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                gridDecalMesh = new Mesh { name = "~EditorGridDecal" };
                mf.sharedMesh = gridDecalMesh;
            }
            int cells = Mathf.Clamp(decalCells, 3, 25);
            if (Mathf.Abs(spacing - gridDecalSpacing) > 0.001f || gridDecalBuiltCells != cells)
            {
                var verts = new List<Vector3>(); var idx = new List<int>();
                float half = cells * spacing * 0.5f;
                for (int i = 0; i <= cells; i++)
                {
                    float c = -half + i * spacing;
                    idx.Add(verts.Count); verts.Add(new Vector3(c, 0f, -half)); idx.Add(verts.Count); verts.Add(new Vector3(c, 0f, half));
                    idx.Add(verts.Count); verts.Add(new Vector3(-half, 0f, c)); idx.Add(verts.Count); verts.Add(new Vector3(half, 0f, c));
                }
                gridDecalMesh.Clear();
                gridDecalMesh.SetVertices(verts);
                gridDecalMesh.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
                gridDecalMesh.RecalculateBounds();
                gridDecalSpacing = spacing; gridDecalBuiltCells = cells;
            }
            gridDecal.SetActive(true);
            gridDecal.transform.position = new Vector3(point.x, point.y + 0.015f, point.z);
        }

        void HideGridDecal() { if (gridDecal != null) gridDecal.SetActive(false); }

        void DestroyGridDecal()
        {
            if (gridDecal != null) Destroy(gridDecal);
            gridDecal = null; gridDecalMesh = null; gridDecalSpacing = -1f;
        }

        /// <summary>
        /// Tint the aimed piece toward the accent so you know what a delete or a grab will take. A
        /// MaterialPropertyBlock over the shared material — the same single-writer arrangement the
        /// enemies use — and cleared, never re-coloured, when the aim moves on.
        /// </summary>
        public void Highlight(LevelPiece piece)
        {
            if (piece == highlighted) return;
            for (int i = 0; i < highlightRenderers.Count; i++)
                if (highlightRenderers[i] != null) highlightRenderers[i].SetPropertyBlock(null);
            highlightRenderers.Clear();
            highlighted = piece;
            if (piece == null) return;
            if (piece.kind == LevelPieceKind.Spawn || piece.kind == LevelPieceKind.Balloon || piece.kind == LevelPieceKind.Pickup) return;   // prefabs drive their own blocks
            if (highlightBlock == null) highlightBlock = new MaterialPropertyBlock();
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
            {
                if (r == null || r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_BaseColor")) continue;
                highlightBlock.Clear();
                highlightBlock.SetColor("_BaseColor", Color.Lerp(r.sharedMaterial.GetColor("_BaseColor"), highlightColor, highlightStrength));
                r.SetPropertyBlock(highlightBlock);
                highlightRenderers.Add(r);
            }
        }

        /// <summary>The editor's floor: a big dark plane at y 0 with a cyan grid every few metres, so
        /// the aim always lands somewhere and a level is built on a visible ground. Removed on Exit.</summary>
        void BuildGround()
        {
            if (ground != null) return;
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "~EditorGround";
            ground.layer = 0;
            // BELOW every real floor top (y 0.00 for Ground_Start, the sandbox and the yard): a top at exactly 0.00
            // Z-fought with them all and the whole map flashed (from play, 2026-09-05). The ground shows only where
            // there is no level, which is what it is for.
            ground.transform.position = new Vector3(0f, -0.09f, 0f);
            ground.transform.localScale = new Vector3(groundSize, 0.1f, groundSize);
            var gr = ground.GetComponent<Renderer>();
            var gm = MaterialFor("Ground");
            if (gm != null) gr.sharedMaterial = gm;
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            gridLines = new GameObject("~EditorGrid");
            var lm = MaterialFor("NeonCyan");
            int n = Mathf.Max(1, Mathf.FloorToInt(groundSize / Mathf.Max(0.5f, gridLineSpacing)));
            float half = groundSize * 0.5f;
            for (int i = 0; i <= n; i++)
            {
                float c = -half + i * gridLineSpacing;
                for (int axis = 0; axis < 2; axis++)
                {
                    var l = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    l.name = "Line";
                    Destroy(l.GetComponent<Collider>());
                    l.transform.SetParent(gridLines.transform, false);
                    bool major = Mathf.Abs(c) < 0.01f;
                    float w = major ? 0.08f : 0.03f;
                    // Between the editor ground (top -0.04) and the real floors (top 0.00), coplanar with neither.
                    l.transform.position = axis == 0 ? new Vector3(c, -0.025f, 0f) : new Vector3(0f, -0.025f, c);
                    l.transform.localScale = axis == 0 ? new Vector3(w, 0.01f, groundSize) : new Vector3(groundSize, 0.01f, w);
                    var r = l.GetComponent<Renderer>();
                    if (lm != null) r.sharedMaterial = lm;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        /// <summary>The weapon and offhand arms have no place in a fly camera. Hidden by Renderer.enabled,
        /// never SetActive: WeaponController caches its viewmodel active-only (ENGINEERING-LOG).</summary>
        void HideViewmodel()
        {
            if (cam == null || hiddenViewmodel.Count > 0) return;
            foreach (var r in cam.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; hiddenViewmodel.Add(r); }
        }

        void ShowViewmodel()
        {
            foreach (var r in hiddenViewmodel) if (r != null) r.enabled = true;
            hiddenViewmodel.Clear();
        }

        void DestroyGround()
        {
            if (ground != null) Destroy(ground); ground = null;
            if (gridLines != null) Destroy(gridLines); gridLines = null;
        }

        public void SelectKind(LevelPieceKind k) { kind = k; RefreshPanel(); }
        public void SetPendingSize(float s) { size = Mathf.Max(1f, s); }

        /// <summary>Add a piece of the selected kind at a point. Public so the feature suite can drive it.</summary>
        public GameObject Place(Vector3 point, Vector3 normal)
        {
            if (doc == null || customRoot == null) return null;
            PushUndo();
            Vector3 facing = cam != null ? cam.forward : player != null ? player.forward : Vector3.forward;
            facing.y = 0f; if (facing.sqrMagnitude < 0.001f) facing = Vector3.forward; facing.Normalize();
            int idx;
            switch (kind)
            {
                case LevelPieceKind.Platform:
                {
                    var s = new Vector3(size, platformThickness, size);
                    idx = doc.platforms.Count;
                    var p = new PlatformDef { name = "Platform_" + idx, center = LevelEditorMath.CenterForTop(point, s), size = s,
                                              materialKey = "Platform", trim = true, trimMaterialKey = "NeonPink", isStatic = true };
                    doc.platforms.Add(p);
                    return LevelPieceFactory.Platform(p, customRoot, ctx, null, idx);
                }
                case LevelPieceKind.WallFace:
                {
                    // A wall-run face: a tall thin slab, its long side across the look (or along it after a rotate).
                    bool alongLook = axisSwap;
                    bool xMajor = Mathf.Abs(facing.x) < Mathf.Abs(facing.z) ? !alongLook : alongLook;
                    var s = xMajor ? new Vector3(size, wallFaceHeight, wallFaceThickness) : new Vector3(wallFaceThickness, wallFaceHeight, size);
                    idx = doc.platforms.Count;
                    var p = new PlatformDef { name = "Wall_" + idx, center = point + Vector3.up * (wallFaceHeight * 0.5f), size = s,
                                              materialKey = "Stone", trim = true, trimMaterialKey = "NeonCyan", isStatic = true };
                    doc.platforms.Add(p);
                    return LevelPieceFactory.Platform(p, customRoot, ctx, null, idx);
                }
                case LevelPieceKind.Balloon:
                {
                    idx = doc.balloons.Count;
                    var b = new BalloonDef { name = "Balloon_" + idx, position = point + Vector3.up * 1.2f, launchSpeed = balloonLaunchSpeed,
                                             respawnSeconds = balloonRespawnSeconds, radius = balloonRadius };
                    doc.balloons.Add(b);
                    return LevelPieceFactory.Balloon(b, customRoot, ctx, null, idx);
                }
                case LevelPieceKind.Water:
                {
                    idx = doc.waters.Count;
                    var w = new WaterDef { name = "Water_" + idx, center = point + Vector3.up * (waterThickness * 0.5f),
                                           size = new Vector3(size, waterThickness, size), flowDirection = facing, flowSpeed = waterFlowSpeed };
                    doc.waters.Add(w);
                    return LevelPieceFactory.Water(w, customRoot, ctx, null, idx);
                }
                case LevelPieceKind.Spawn:
                {
                    idx = doc.spawns.Count;
                    var s = new SpawnDef { name = "Spawn_" + SelectedSpawnKey + "_" + idx, prefabKey = SelectedSpawnKey, position = point + Vector3.up * 0.3f,
                                           yaw = Mathf.Repeat(Quaternion.LookRotation(-facing, Vector3.up).eulerAngles.y + yaw, 360f), isBoss = false };
                    doc.spawns.Add(s);
                    return LevelPieceFactory.Spawner(s, customRoot, ctx, null, idx);
                }
                case LevelPieceKind.Pickup:
                {
                    idx = doc.pickups.Count;
                    var p = new PickupDef { name = "Pickup_" + SelectedItemKey + "_" + idx, itemKey = SelectedItemKey, position = point + Vector3.up * 1.2f };
                    doc.pickups.Add(p);
                    return LevelPieceFactory.Pickup(p, LevelPieceFactory.Group("Pickups", customRoot), ctx, null, idx);
                }
                case LevelPieceKind.Checkpoint:
                {
                    idx = doc.checkpoints.Count;
                    var c = new CheckpointDef { name = "Checkpoint_" + (idx + 1), position = point, spawnOffset = new Vector3(0f, 0.2f, -2f) };
                    doc.checkpoints.Add(c);
                    return LevelPieceFactory.Checkpoint(c, customRoot, ctx, null, idx);
                }
                case LevelPieceKind.Torch:
                {
                    idx = doc.torches.Count;
                    var t = new TorchDef { name = "Torch_" + idx, basePosition = point };
                    doc.torches.Add(t);
                    return LevelPieceFactory.Torch(t, LevelPieceFactory.Group("Torches", customRoot), ctx, null, idx);
                }
                default:   // PlayerStart: one only, moved rather than added
                {
                    doc.playerStart = point + Vector3.up * 1.2f;
                    doc.playerStartYaw = Mathf.Repeat(Quaternion.LookRotation(facing, Vector3.up).eulerAngles.y + yaw, 360f);
                    Rebuild();
                    return customRoot != null ? customRoot.Find("StartSpawn") != null ? customRoot.Find("StartSpawn").gameObject : null : null;
                }
            }
        }

        void MoveGrabbed(LevelPiece piece, Vector3 point)
        {
            if (piece == null) return;
            Vector3 pos = point;
            switch (piece.kind)
            {
                case LevelPieceKind.Platform:
                    if (piece.index < doc.platforms.Count)
                    {
                        var p = doc.platforms[piece.index];
                        p.center = p.name.StartsWith("Wall_") ? pos + Vector3.up * (p.size.y * 0.5f) : LevelEditorMath.CenterForTop(pos, p.size);
                        piece.transform.position = p.center;
                    }
                    break;
                case LevelPieceKind.Balloon:
                    if (piece.index < doc.balloons.Count) { doc.balloons[piece.index].position = pos + Vector3.up * 1.2f; piece.transform.position = doc.balloons[piece.index].position; }
                    break;
                case LevelPieceKind.Water:
                    if (piece.index < doc.waters.Count) { var w = doc.waters[piece.index]; w.center = pos + Vector3.up * (w.size.y * 0.5f); piece.transform.position = w.center; }
                    break;
                case LevelPieceKind.Spawn:
                    if (piece.index < doc.spawns.Count) { doc.spawns[piece.index].position = pos + Vector3.up * 0.3f; piece.transform.position = doc.spawns[piece.index].position; }
                    break;
                case LevelPieceKind.Pickup:
                    if (piece.index < doc.pickups.Count) { doc.pickups[piece.index].position = pos + Vector3.up * 1.2f; piece.transform.position = doc.pickups[piece.index].position; }
                    break;
                case LevelPieceKind.Checkpoint:
                    if (piece.index < doc.checkpoints.Count)
                    {
                        var c = doc.checkpoints[piece.index]; c.position = pos; piece.transform.position = pos;
                        var cp = piece.GetComponent<Checkpoint>(); if (cp != null && cp.spawnPoint != null) cp.spawnPoint.position = pos + c.spawnOffset;
                    }
                    break;
                case LevelPieceKind.Torch:
                    if (piece.index < doc.torches.Count) { doc.torches[piece.index].basePosition = pos; piece.transform.position = pos; }
                    break;
            }
        }

        public void DeletePiece(LevelPiece piece)
        {
            if (piece == null || doc == null) return;
            PushUndo();
            switch (piece.kind)
            {
                case LevelPieceKind.Platform: if (piece.index < doc.platforms.Count) doc.platforms.RemoveAt(piece.index); break;
                case LevelPieceKind.Balloon: if (piece.index < doc.balloons.Count) doc.balloons.RemoveAt(piece.index); break;
                case LevelPieceKind.Water: if (piece.index < doc.waters.Count) doc.waters.RemoveAt(piece.index); break;
                case LevelPieceKind.Spawn: if (piece.index < doc.spawns.Count) doc.spawns.RemoveAt(piece.index); break;
                case LevelPieceKind.Pickup: if (piece.index < doc.pickups.Count) doc.pickups.RemoveAt(piece.index); break;
                case LevelPieceKind.Checkpoint: if (piece.index < doc.checkpoints.Count) doc.checkpoints.RemoveAt(piece.index); break;
                case LevelPieceKind.Torch: if (piece.index < doc.torches.Count) doc.torches.RemoveAt(piece.index); break;
                default: return;   // the start cannot be deleted, only moved
            }
            Rebuild();   // indices shift; a full rebuild is cheap at this scale and cannot desync
        }

        /// <summary>
        /// Resize the aimed piece one ladder step FROM ITS OWN SIZE, and make that the pending size too —
        /// one number, whichever way you got there. It used to step from the pending size, so aiming at a
        /// 2 m slab with 8 m pending and pressing + made it 10 m ("the size buttons half work"). Returns
        /// the rebuilt piece so the caller's highlight and readout follow it.
        /// </summary>
        public LevelPiece ResizePiece(LevelPiece piece, int direction)
        {
            if (piece == null || doc == null || direction == 0) return piece;
            int index = piece.index; var kindWas = piece.kind;
            if (piece.kind == LevelPieceKind.Platform && piece.index < doc.platforms.Count)
            {
                var p = doc.platforms[piece.index];
                PushUndo();
                if (p.name.StartsWith("Wall_"))
                {
                    bool xMajor = p.size.x > p.size.z;
                    float cur = xMajor ? p.size.x : p.size.z;
                    size = LevelEditorMath.StepSizeFrom(cur, direction);
                    if (xMajor) p.size.x = size; else p.size.z = size;
                }
                else
                {
                    size = LevelEditorMath.StepSizeFrom(p.size.x, direction);
                    Vector3 top = p.center + Vector3.up * (p.size.y * 0.5f);
                    p.size = new Vector3(size, p.size.y, size); p.center = LevelEditorMath.CenterForTop(top, p.size);
                }
                Rebuild();
            }
            else if (piece.kind == LevelPieceKind.Water && piece.index < doc.waters.Count)
            {
                var w = doc.waters[piece.index];
                PushUndo();
                size = LevelEditorMath.StepSizeFrom(w.size.x, direction);
                w.size = new Vector3(size, waterThickness, size);
                Rebuild();
            }
            else return piece;
            return FindPiece(kindWas, index);
        }

        /// <summary>The live piece for a kind + index after a rebuild (the old object is gone).</summary>
        public LevelPiece FindPiece(LevelPieceKind k, int index)
        {
            if (customRoot == null) return null;
            foreach (var lp in customRoot.GetComponentsInChildren<LevelPiece>(true))
                if (lp.kind == k && lp.index == index) return lp;
            return null;
        }

        // ---------------------------------------------------------------- preview + panel

        void ShowPreview(Vector3 point, float dt)
        {
            if (preview == null)
            {
                preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                preview.name = "~EditorPreview";
                Destroy(preview.GetComponent<Collider>());
                var r = preview.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var m = MaterialFor("NeonCyan");
                if (m != null) r.sharedMaterial = m;
                hasPreviewPos = false;
            }
            preview.SetActive(true);
            Vector3 s; Vector3 target;
            switch (kind)
            {
                case LevelPieceKind.Platform: s = new Vector3(size, platformThickness, size); target = LevelEditorMath.CenterForTop(point, s); break;
                case LevelPieceKind.WallFace: s = new Vector3(size, wallFaceHeight, wallFaceThickness); target = point + Vector3.up * (wallFaceHeight * 0.5f); break;
                case LevelPieceKind.Water: s = new Vector3(size, 0.1f, size); target = point + Vector3.up * 0.05f; break;
                case LevelPieceKind.Balloon: s = Vector3.one * (balloonRadius * 2f); target = point + Vector3.up * 1.2f; break;
                default: s = new Vector3(0.5f, 0.5f, 0.5f); target = point + Vector3.up * 0.25f; break;
            }
            // The cube SLIDES to its cell and TURNS through a rotate rather than teleporting: the same
            // frame-rate-independent ease as the fly, short enough to read as snapping.
            float k = LevelEditorMath.EaseFactor(previewEase, dt);
            previewPos = hasPreviewPos ? Vector3.Lerp(previewPos, target, k) : target;
            hasPreviewPos = true;
            float wantYaw = kind == LevelPieceKind.WallFace ? (axisSwap ? 90f : 0f) : yaw;
            previewYaw = Mathf.LerpAngle(previewYaw, wantYaw, k);
            preview.transform.position = previewPos;
            preview.transform.rotation = Quaternion.Euler(0f, previewYaw, 0f);
            preview.transform.localScale = s * 0.98f;
        }

        void HidePreview() { if (preview != null) preview.SetActive(false); }

        void RefreshPanel(LevelPiece under = null)
        {
            // The kind buttons: the selected one is lit, the rest dim. Same field the wheel writes.
            for (int i = 0; i < kindButtons.Length; i++)
            {
                var b = kindButtons[i];
                if (b == null) continue;
                bool sel = (LevelPieceKind)i == kind;
                var img = b.targetGraphic;
                if (img != null) img.color = sel ? new Color(0.31f, 0.88f, 0.82f, 0.85f) : new Color(1f, 1f, 1f, 0.55f);
                var label = b.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.color = sel ? new Color(0.05f, 0.04f, 0.06f, 1f) : new Color(0.91f, 0.89f, 0.84f, 0.9f);
            }
            if (pieceList != null)
            {
                // One line about the selected kind: its variant or size, and the wheel hint.
                var sb = new System.Text.StringBuilder();
                sb.Append("<color=#4FE0D0>").Append(kind.ToString().ToUpperInvariant()).Append("</color>");
                if (kind == LevelPieceKind.Spawn) sb.Append("   ").Append(SelectedSpawnKey).Append("   <color=#9A918A>Ctrl+wheel / V: variant</color>");
                else if (kind == LevelPieceKind.Pickup) sb.Append("   ").Append(SelectedItemKey).Append("   <color=#9A918A>Ctrl+wheel / V: variant</color>");
                else if (kind == LevelPieceKind.Platform || kind == LevelPieceKind.Water || kind == LevelPieceKind.WallFace) sb.Append("   ").Append(size.ToString("0")).Append(" m   <color=#9A918A>wheel: size</color>");
                pieceList.text = sb.ToString();
            }
            if (readout != null)
            {
                // The numbers: size, grid, rotation, variant — and the piece under the aim with ITS size.
                var sb = new System.Text.StringBuilder();
                sb.Append("size <b>").Append(size.ToString("0")).Append(" m</b>   grid <b>").Append(LevelEditorMath.GridFor(kind).ToString("0.#")).Append(" m</b>");
                sb.Append("   rot <b>").Append((kind == LevelPieceKind.WallFace ? (axisSwap ? 90f : 0f) : yaw).ToString("0")).Append("°</b>");
                if (undoStack != null && undoStack.UndoCount > 0) sb.Append("   undo <b>").Append(undoStack.UndoCount).Append("</b>");
                if (under != null)
                {
                    sb.Append("\n<color=#9A918A>aim</color> ").Append(under.name);
                    if (under.kind == LevelPieceKind.Platform && under.index < doc.platforms.Count)
                    { var p = doc.platforms[under.index]; sb.Append("  ").Append(p.size.x.ToString("0")).Append("×").Append(p.size.z.ToString("0")).Append(" m"); }
                    else if (under.kind == LevelPieceKind.Water && under.index < doc.waters.Count)
                    { var w = doc.waters[under.index]; sb.Append("  ").Append(w.size.x.ToString("0")).Append("×").Append(w.size.z.ToString("0")).Append(" m"); }
                }
                readout.text = sb.ToString();
            }
            if (status != null && doc != null)
            {
                status.text = doc.levelId + "   " + doc.PieceCount + " pieces" +
                              (under != null ? "   aiming at " + under.name : "") +
                              (cursorFree ? "   [cursor free — Tab to fly]" : "");
            }
            if (loadLabel != null) loadLabel.text = saved.Count == 0 ? "no saved levels" : saved[Mathf.Clamp(loadIndex, 0, saved.Count - 1)] + "  (" + (loadIndex + 1) + "/" + saved.Count + ")";
            if (playButton != null)
            {
                var t = playButton.GetComponentInChildren<TMP_Text>();
                if (t != null) t.text = CurrentMode == Mode.Playing ? "EDIT" : "PLAY";
            }
        }
    }
}
