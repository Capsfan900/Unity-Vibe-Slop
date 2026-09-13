using UnityEditor;
using UnityEngine.SceneManagement;

namespace VibeGame1.EditorTools
{
    [InitializeOnLoad]
    public static class LevelStudioPlayBridge
    {
        public const string ActiveDraftKey = "VibeGame1.LevelStudio.ActiveDraftId";
        const string ChangedDraftKey = "VibeGame1.LevelStudio.ChangedDraftId";

        static LevelStudioPlayBridge()
        {
            LevelEditor.LoadDraft = Load;
            LevelEditor.AutosaveDraft = Autosave;
            LevelEditor.DraftChanged = id => SessionState.SetString(ChangedDraftKey, id ?? "");
            LevelEditor.HasResumableDraft = () => HasResumableDraft;
            LevelEditor.QueueActiveDraftScene = QueueActiveScene;
        }

        public static string ActiveDraftId { get { return SessionState.GetString(ActiveDraftKey, ""); } }

        public static bool TryQueueActive(out string sceneName, out string diagnostic)
        {
            sceneName = null; diagnostic = null;
            string id = ActiveDraftId;
            var result = new LevelDraftStore().LoadResult(id);
            if (!result.Success) { diagnostic = result.diagnostic ?? "No resumable Level Studio draft."; return false; }
            try
            {
                sceneName = result.draft.definition.sceneName;
                if (string.IsNullOrEmpty(sceneName)) { diagnostic = "The draft has no target scene."; return false; }
                LevelEditor.PendingDraftId = id; LevelEditor.PendingLoadPath = null;
                return true;
            }
            finally { result.draft.Dispose(); }
        }

        public static bool HasResumableDraft
        {
            get { string scene, diagnostic; return TryInspectActive(out scene, out diagnostic); }
        }

        static string QueueActiveScene()
        {
            string scene, diagnostic;
            return TryQueueActive(out scene, out diagnostic) ? scene : null;
        }

        static bool TryInspectActive(out string sceneName, out string diagnostic)
        {
            sceneName = null; diagnostic = null;
            var result = new LevelDraftStore().LoadResult(ActiveDraftId);
            if (!result.Success) { diagnostic = result.diagnostic; return false; }
            try { sceneName = result.draft.definition.sceneName; return !string.IsNullOrEmpty(sceneName); }
            finally { result.draft.Dispose(); }
        }

        static LevelDocument Load(string id)
        {
            var result = new LevelDraftStore().LoadResult(id);
            if (!result.Success) return null;
            try { return LevelDocument.FromDefinition(result.draft.definition); }
            finally { result.draft.Dispose(); }
        }

        static string Autosave(string id, LevelDocument document)
        {
            var store = new LevelDraftStore();
            var result = store.LoadResult(id);
            if (!result.Success) return result.diagnostic ?? "Draft cannot be autosaved.";
            try { document.CopyTo(result.draft.definition); store.Autosave(result.draft); return null; }
            catch (System.Exception e) { return e.Message; }
            finally { result.draft.Dispose(); }
        }

        public static bool ConsumeChanged(string id)
        {
            if (SessionState.GetString(ChangedDraftKey, "") != id) return false;
            SessionState.EraseString(ChangedDraftKey); return true;
        }
    }
}
