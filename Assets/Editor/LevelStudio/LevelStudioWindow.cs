using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.EditorTools
{
    /// <summary>Task 2's deliberately small IMGUI shell. Scene preview and mutation belong to later tasks.</summary>
    public sealed class LevelStudioWindow : EditorWindow
    {
        const string RegistryPath = "Assets/Data/LevelRegistry.asset";
        readonly LevelStudioSelection selection = new LevelStudioSelection();
        readonly List<LevelStudioRow> libraryRows = new List<LevelStudioRow>();
        readonly List<LevelStudioRow> hierarchyRows = new List<LevelStudioRow>();
        LevelDraftStore store;
        LevelDraft openDraft;
        LevelStudioVocabulary vocabulary;
        LevelStudioPreview preview;
        LevelStudioSceneTool sceneTool;
        Vector2 scroll;
        Vector2 inspectorScroll;
        string search = "";
        string message = "No draft open.";
        bool showZones = true;
        bool autosaveScheduled;

        [MenuItem("VibeGame1/Level Studio")]
        public static void Open()
        {
            GetWindow<LevelStudioWindow>("Level Studio").Show();
        }

        void OnEnable()
        {
            store = new LevelDraftStore();
            vocabulary = LevelStudioVocabulary.Load();
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            Selection.selectionChanged += OnSceneSelectionChanged;
            ReloadLibrary();
        }

        void OnDisable()
        {
            if (openDraft != null) openDraft.Dispose();
            if (preview != null) preview.Dispose();
            openDraft = null;
            preview = null;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Selection.selectionChanged -= OnSceneSelectionChanged;
        }

        void OnGUI()
        {
            if (store == null) OnEnable();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) ReloadLibrary();
            if (openDraft != null && GUILayout.Button("Save", EditorStyles.toolbarButton)) { store.Save(openDraft); message = "Draft saved."; }
            showZones = GUILayout.Toggle(showZones, "Zones", EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();
            GUILayout.Label(ValidationLabel(), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(420f));
            search = EditorGUILayout.TextField("Search", search ?? "");
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawRows("Level Library", LevelStudioBrowser.Filter(libraryRows, search));
            if (openDraft != null)
            {
                EditorGUILayout.Space();
                DrawRows("Objects — " + openDraft.definition.displayName, LevelStudioBrowser.Filter(hierarchyRows, search));
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            if (openDraft != null)
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(380f));
                DrawEditToolbar();
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                if (LevelStudioInspector.Draw(openDraft.definition, SelectedRecords())) Changed(false);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(message, MessageType.Info);
            if (!string.IsNullOrEmpty(vocabulary != null ? vocabulary.diagnostic : null)) EditorGUILayout.HelpBox(vocabulary.diagnostic, MessageType.Warning);
        }

        void DrawRows(string title, IReadOnlyList<LevelStudioRow> rows)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            foreach (var row in rows)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(12f * row.depth);
                bool selected = selection.selectedKeys.Contains(row.key);
                if (GUILayout.Button(row.label, selected ? EditorStyles.toolbarButton : GUI.skin.button))
                {
                    Event current = Event.current;
                    if ((current.control || current.command) && selected) selection.Toggle(row.key);
                    else selection.Select(row.key, current.control || current.command, current.shift, rows.Select(x => x.key));
                    if (current.clickCount > 1) selection.RequestFocus();
                }
                if (!string.IsNullOrEmpty(row.secondary)) GUILayout.Label(row.secondary, EditorStyles.miniLabel, GUILayout.Width(260));
                if (row.action != LevelStudioRowAction.None && GUILayout.Button(ActionLabel(row.action), GUILayout.Width(116))) Execute(row);
                EditorGUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(row.diagnostic))
                {
                    EditorGUILayout.BeginHorizontal(); GUILayout.Space(12f * (row.depth + 1)); GUILayout.Label(row.diagnostic, EditorStyles.miniLabel); EditorGUILayout.EndHorizontal();
                }
            }
        }

        void Execute(LevelStudioRow row)
        {
            try
            {
                if (row.action == LevelStudioRowAction.ProtectedCopy) OpenCampaign(row);
                else if (row.action == LevelStudioRowAction.CustomCopy) OpenCustom(row);
                else if (row.action == LevelStudioRowAction.Resume) ResumeDraft(row.draftId);
                else if (row.action == LevelStudioRowAction.RecoverLatest) OpenDraft(store.Recover(row.draftId));
                ReloadLibrary();
            }
            catch (Exception e) { message = "Could not open row: " + e.Message; }
        }

        void OpenCampaign(LevelStudioRow row)
        {
            var source = LoadDefinition(row);
            if (source == null) throw new InvalidOperationException("Campaign asset is missing.");
            // This is the only campaign-open path: the source is immediately cloned into a protected draft.
            OpenDraft(store.CreateFromCampaign(source));
        }

        void ResumeDraft(string draftId)
        {
            var result = store.LoadResult(draftId);
            if (!result.Success)
            {
                message = string.IsNullOrEmpty(result.diagnostic) ? "Draft cannot be resumed." : result.diagnostic;
                return;
            }
            OpenDraft(result.draft);
        }

        void OpenCustom(LevelStudioRow row)
        {
            var source = LoadDefinition(row);
            if (source == null) throw new InvalidOperationException("Custom asset is missing.");
            OpenDraft(store.CreateCustom(source));
        }

        LevelDefinition LoadDefinition(LevelStudioRow row)
        {
            string path = !string.IsNullOrEmpty(row.sourceGuid) ? AssetDatabase.GUIDToAssetPath(row.sourceGuid) : row.assetPath;
            if (string.IsNullOrEmpty(path)) path = row.assetPath;
            return AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
        }

        void OpenDraft(LevelDraft draft)
        {
            if (draft == null) throw new InvalidOperationException("No valid draft was available.");
            if (openDraft != null) openDraft.Dispose();
            openDraft = draft;
            // Enumerate may hydrate legacy metadata, so it is intentionally never called on a campaign asset.
            hierarchyRows.Clear();
            hierarchyRows.AddRange(LevelStudioBrowser.BuildHierarchy(openDraft.definition, vocabulary));
            selection.Reconcile(hierarchyRows.Select(row => row.key));
            sceneTool = new LevelStudioSceneTool(openDraft.definition);
            if (preview != null) preview.Dispose();
            try { preview = new LevelStudioPreview(openDraft.definition); }
            catch (Exception e) { preview = null; message = "Draft opened; preview failed: " + e.Message; }
            message = "Opened working copy " + draft.manifest.displayName + ". Validation: Not validated.";
            SceneView.RepaintAll();
        }

        void ReloadLibrary()
        {
            if (store == null) return;
            var registry = AssetDatabase.LoadAssetAtPath<LevelRegistry>(RegistryPath);
            var campaign = registry != null ? registry.Ordered() : new LevelDefinition[0];
            var custom = FindCustomLevels();
            IEnumerable<string> legacy = Directory.Exists(LevelEditor.LevelsDirectory)
                ? Directory.GetFiles(LevelEditor.LevelsDirectory, "*.json") : new string[0];
            libraryRows.Clear();
            libraryRows.AddRange(LevelStudioBrowser.BuildLibrary(campaign, custom, store.List(), store.RecoveryHistory, legacy));
            selection.Reconcile(libraryRows.Concat(hierarchyRows).Select(row => row.key));
        }

        static IEnumerable<LevelDefinition> FindCustomLevels()
        {
            const string customRoot = "Assets/Data/Levels/Custom";
            foreach (string guid in AssetDatabase.FindAssets("t:LevelDefinition", new[] { customRoot }))
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (level != null) yield return level;
            }
        }

        static string ActionLabel(LevelStudioRowAction action)
        {
            if (action == LevelStudioRowAction.ProtectedCopy) return "Create Copy";
            if (action == LevelStudioRowAction.CustomCopy) return "Create Copy";
            if (action == LevelStudioRowAction.Resume) return "Resume";
            if (action == LevelStudioRowAction.RecoverLatest) return "Recover Latest";
            return "";
        }

        IReadOnlyList<LevelObjectRecord> SelectedRecords()
        {
            if (openDraft == null) return new LevelObjectRecord[0];
            var paths = new HashSet<string>(hierarchyRows.Where(x => selection.selectedKeys.Contains(x.key) && x.kind == LevelStudioRowKind.Object).Select(x => x.recordPath), StringComparer.Ordinal);
            return LevelObjectCatalog.Enumerate(openDraft.definition).Where(x => paths.Contains(x.path)).ToArray();
        }

        void DrawEditToolbar()
        {
            var records = SelectedRecords(); var active = records.FirstOrDefault(x => "object:" + x.path == selection.activeKey) ?? records.FirstOrDefault();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUI.enabled = active != null;
            if (GUILayout.Button("Duplicate", EditorStyles.toolbarButton)) { string id = sceneTool.Duplicate(active); if (!string.IsNullOrEmpty(id)) Changed(true); }
            if (GUILayout.Button("Copy", EditorStyles.toolbarButton)) sceneTool.Copy(active);
            if (GUILayout.Button("Paste", EditorStyles.toolbarButton) && sceneTool.Paste(active)) Changed(false);
            if (GUILayout.Button("Delete", EditorStyles.toolbarButton) && sceneTool.Delete(active)) Changed(true);
            if (GUILayout.Button("Focus", EditorStyles.toolbarButton)) selection.RequestFocus();
            if (GUILayout.Button("Hide", EditorStyles.toolbarButton) && preview != null) preview.SetHidden(active, true);
            if (GUILayout.Button("Isolate", EditorStyles.toolbarButton) && preview != null) preview.Isolate(records);
            GUI.enabled = preview != null;
            if (GUILayout.Button("Show All", EditorStyles.toolbarButton)) preview.ShowAll();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (openDraft == null || sceneTool == null) return;
            if (showZones)
            {
                foreach (var zone in openDraft.definition.zones ?? new ZoneDef[0])
                {
                    if (zone == null) continue;
                    Handles.color = zone.displayColor;
                    Handles.DrawWireCube(zone.center, zone.size);
                    EditorGUI.BeginChangeCheck();
                    Vector3 center = Handles.PositionHandle(zone.center, Quaternion.identity);
                    Vector3 size = Handles.ScaleHandle(zone.size, center, Quaternion.identity, HandleUtility.GetHandleSize(center));
                    if (EditorGUI.EndChangeCheck()) { sceneTool.SetZoneBounds(zone.zoneId, new Bounds(center, size)); Changed(false); }
                }
                Handles.color = Color.cyan;
                foreach (var route in openDraft.definition.challengeRoutes ?? new ChallengeRouteDef[0]) if (route != null) Handles.DrawLine(route.entryCenter, route.rejoinCenter);
                Handles.color = Color.yellow;
                foreach (var sequence in openDraft.definition.projectileSequences ?? new ProjectileSequenceDef[0])
                    foreach (var window in sequence == null ? new ProjectileEngagementWindowDef[0] : sequence.engagementWindows ?? new ProjectileEngagementWindowDef[0])
                        if (window != null) Handles.DrawLine(window.routeStart, window.routeEnd);
            }
            var records = SelectedRecords(); var active = records.FirstOrDefault(x => "object:" + x.path == selection.activeKey) ?? records.FirstOrDefault();
            if (active == null) return;
            Vector3 position, scale; Quaternion rotation;
            if (!LevelStudioSceneTool.TryGetTransform(active, out position, out rotation, out scale)) return;
            string focus = selection.ConsumeFocusIntent();
            if (!string.IsNullOrEmpty(focus)) sceneView.Frame(new Bounds(position, Max(scale, Vector3.one)), false);
            EditorGUI.BeginChangeCheck();
            Vector3 nextPosition = position, nextScale = scale; Quaternion nextRotation = rotation;
            Quaternion handleRotation = Tools.pivotRotation == PivotRotation.Local ? rotation : Quaternion.identity;
            if (Tools.current == Tool.Rotate) nextRotation = Handles.RotationHandle(rotation, position);
            else if (Tools.current == Tool.Scale) nextScale = Handles.ScaleHandle(scale, position, handleRotation, HandleUtility.GetHandleSize(position));
            else nextPosition = Handles.PositionHandle(position, handleRotation);
            if (!EditorGUI.EndChangeCheck()) return;
            if (Tools.current == Tool.Move && records.Count > 1)
            {
                Vector3 delta = nextPosition - position;
                foreach (var record in records)
                {
                    Vector3 p, s; Quaternion r;
                    if (LevelStudioSceneTool.TryGetTransform(record, out p, out r, out s)) { sceneTool.ApplyTransform(record, Matrix4x4.TRS(p + delta, r, s)); if (preview != null) preview.Sync(record); }
                }
            }
            else { sceneTool.ApplyTransform(active, Matrix4x4.TRS(nextPosition, nextRotation, nextScale)); if (preview != null) preview.Sync(active); }
            Changed(false, false);
        }

        void Changed(bool structural, bool rebuildPreview = true)
        {
            if (openDraft == null) return;
            if (structural) { hierarchyRows.Clear(); hierarchyRows.AddRange(LevelStudioBrowser.BuildHierarchy(openDraft.definition, vocabulary)); selection.Reconcile(hierarchyRows.Select(x => x.key)); }
            if (rebuildPreview && preview != null) preview.Rebuild();
            if (!autosaveScheduled)
            {
                autosaveScheduled = true;
                EditorApplication.delayCall += () => { autosaveScheduled = false; if (openDraft != null) try { store.Autosave(openDraft); } catch (Exception e) { message = "Autosave failed: " + e.Message; } };
            }
            Repaint(); SceneView.RepaintAll();
        }

        void OnUndoRedo() { if (openDraft != null) Changed(true); }
        void OnSceneSelectionChanged()
        {
            if (openDraft == null || preview == null) return;
            var selected = Selection.transforms ?? new Transform[0];
            var records = LevelObjectCatalog.Enumerate(openDraft.definition).Where(record =>
            {
                var target = preview.Find(record);
                return target != null && selected.Any(item => item == target || item.IsChildOf(target));
            }).ToArray();
            if (records.Length == 0) return;
            selection.Clear();
            foreach (var record in records) selection.Select("object:" + record.path, true, false, hierarchyRows.Select(row => row.key));
            Repaint();
        }
        string ValidationLabel() { if (openDraft == null) return "Validation: No draft"; var report = LevelStudioValidator.Validate(openDraft.definition); return report.HasErrors ? "Validation: " + report.errors.Count + " errors" : "Validation: " + report.warnings.Count + " warnings"; }
        static Vector3 Max(Vector3 a, Vector3 b) { return new Vector3(Mathf.Max(a.x,b.x), Mathf.Max(a.y,b.y), Mathf.Max(a.z,b.z)); }
    }
}
