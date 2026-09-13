using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VibeGame1.EditorTools
{
    /// <summary>Unity/file effects for the pure Level Studio apply policy.</summary>
    public sealed class UnityLevelApplyEnvironment : ILevelApplyEnvironment
    {
        readonly LevelDraftStore store;
        string sourcePath, scenePath, backupSourcePath, backupScenePath, journalPath;
        byte[] sourceBytes, sceneBytes;

        public UnityLevelApplyEnvironment(LevelDraftStore store) { this.store = store ?? throw new ArgumentNullException("store"); }
        public bool IsEditMode { get { return !EditorApplication.isPlayingOrWillChangePlaymode; } }
        public bool HasUnsavedScenes { get { for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) return true; return false; } }

        public LevelDefinition ResolveSource(string guid)
        {
            sourcePath = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(sourcePath) ? null : AssetDatabase.LoadAssetAtPath<LevelDefinition>(sourcePath);
        }

        public bool SceneExists(string sceneName)
        {
            var paths = AssetDatabase.FindAssets((sceneName ?? "") + " t:Scene").Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileNameWithoutExtension(path) == sceneName).Distinct().ToArray();
            scenePath = paths.Length == 1 ? paths[0] : null;
            return scenePath != null && SceneManager.GetActiveScene().path == scenePath;
        }

        public bool Backup(LevelDraft draft, LevelDefinition canonical, out string path, out string error)
        {
            path = error = null;
            try
            {
                sourceBytes = File.ReadAllBytes(sourcePath); sceneBytes = File.ReadAllBytes(scenePath);
                path = Path.Combine(store.DraftPath(draft.manifest.draftId), "backups", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
                Directory.CreateDirectory(path);
                backupSourcePath = Path.Combine(path, Path.GetFileName(sourcePath)); backupScenePath = Path.Combine(path, Path.GetFileName(scenePath));
                File.WriteAllBytes(backupSourcePath, sourceBytes); File.WriteAllBytes(backupScenePath, sceneBytes);
                journalPath = Path.Combine(path, "apply-journal.json");
                File.WriteAllText(journalPath, JsonUtility.ToJson(new ApplyJournal { sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath), sourcePath = sourcePath, scenePath = scenePath, sourceFingerprint = draft.manifest.sourceFingerprint, draftRevision = draft.manifest.revision, stage = "backup" }, true));
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        public bool WriteSource(LevelDefinition canonical, LevelDefinition value, out string error)
        {
            try { EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(value), canonical); EditorUtility.SetDirty(canonical); AssetDatabase.SaveAssetIfDirty(canonical); AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate); error = null; return true; }
            catch (Exception e) { error = e.Message; return false; }
        }

        public bool Build(LevelDefinition canonical, out string error)
        {
            LevelBuildResult result = LevelDefinitionBuilder.TryBuild(canonical);
            error = result.message; return result.success;
        }

        public bool Verify(LevelDefinition canonical, out string error)
        {
            var report = LevelStudioValidator.Validate(canonical, this, this);
            error = report.HasErrors ? string.Join("\n", report.errors.Select(x => x.code + ": " + x.message)) : null;
            return !report.HasErrors;
        }

        public bool Restore(LevelDefinition canonical, out string error)
        {
            try
            {
                File.WriteAllBytes(sourcePath, sourceBytes); File.WriteAllBytes(scenePath, sceneBytes);
                AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                if (!File.ReadAllBytes(sourcePath).SequenceEqual(sourceBytes) || !File.ReadAllBytes(scenePath).SequenceEqual(sceneBytes)) throw new IOException("Restored bytes did not match the backup.");
                error = null; return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        public bool Complete(out string error)
        {
            try { if (!string.IsNullOrEmpty(journalPath) && File.Exists(journalPath)) File.Delete(journalPath); error = null; return true; }
            catch (Exception e) { error = e.Message; return false; }
        }

        public bool HasPrefab(string key) { return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + key + ".prefab") != null; }
        public bool HasEnemyData(string key) { return AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(key)) != null; }
        public bool HasItem(string key) { return AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Data/Items/" + key + ".asset") != null; }
        public bool HasMaterial(string key) { return AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_" + key + ".mat") != null; }
        public bool IsProjectileCapable(string key) { var data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(key)); return data != null && data.shootsProjectiles; }
        public bool TryValidate(LevelDefinition definition, out string error)
        {
            string report = LevelArcReport.Build(definition);
            bool valid = report.EndsWith("VERDICT: every authored traversal has a clean arc.\r\n", StringComparison.Ordinal)
                      || report.EndsWith("VERDICT: every authored traversal has a clean arc.\n", StringComparison.Ordinal);
            error = valid ? null : report;
            return valid;
        }

        [Serializable] sealed class ApplyJournal { public string sourceGuid, sourcePath, scenePath, sourceFingerprint, stage; public int draftRevision; }
    }
}
