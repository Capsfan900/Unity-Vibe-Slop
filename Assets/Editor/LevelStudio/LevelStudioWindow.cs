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
        Vector2 scroll;
        string search = "";
        string message = "No draft open.";

        [MenuItem("VibeGame1/Level Studio")]
        public static void Open()
        {
            GetWindow<LevelStudioWindow>("Level Studio").Show();
        }

        void OnEnable()
        {
            store = new LevelDraftStore();
            vocabulary = LevelStudioVocabulary.Load();
            ReloadLibrary();
        }

        void OnDisable()
        {
            if (openDraft != null) openDraft.Dispose();
            openDraft = null;
        }

        void OnGUI()
        {
            if (store == null) OnEnable();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) ReloadLibrary();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Validation: Not validated", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            search = EditorGUILayout.TextField("Search", search ?? "");
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawRows("Level Library", LevelStudioBrowser.Filter(libraryRows, search));
            if (openDraft != null)
            {
                EditorGUILayout.Space();
                DrawRows("Objects — " + openDraft.definition.displayName, LevelStudioBrowser.Filter(hierarchyRows, search));
            }
            EditorGUILayout.EndScrollView();
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
            message = "Opened working copy " + draft.manifest.displayName + ". Validation: Not validated.";
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
    }
}
