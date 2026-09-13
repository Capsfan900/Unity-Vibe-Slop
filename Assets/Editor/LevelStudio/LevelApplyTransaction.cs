using System;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    public enum LevelApplyStage { Preflight, SourceConflict, Validation, WarningConfirmation, ScenePreflight, Backup, SourceWrite, Build, Verify, DraftRebase, Complete }

    public sealed class ApplyOptions { public string allowWarningsFingerprint; }

    public sealed class ApplyResult
    {
        public bool success, conflict, rollbackSucceeded;
        public string message, backupPath;
        public LevelApplyStage stage;
        public LevelValidationReport validation;
    }

    public interface ILevelApplyEnvironment : ILevelStudioResourceResolver, ILevelStudioTraversalAdapter
    {
        bool IsEditMode { get; }
        bool HasUnsavedScenes { get; }
        LevelDefinition ResolveSource(string guid);
        bool SceneExists(string sceneName);
        bool Backup(LevelDraft draft, LevelDefinition canonical, out string path, out string error);
        bool WriteSource(LevelDefinition canonical, LevelDefinition value, out string error);
        bool Build(LevelDefinition canonical, out string error);
        bool Verify(LevelDefinition canonical, out string error);
        bool Restore(LevelDefinition canonical, out string error);
        bool Complete(out string error);
    }

    /// <summary>Pure apply policy; Unity/file effects belong to the injected environment.</summary>
    public sealed class LevelApplyTransaction
    {
        readonly LevelDraftStore store;
        public LevelApplyTransaction(LevelDraftStore store) { this.store = store ?? throw new ArgumentNullException("store"); }

        public ApplyResult Apply(LevelDraft draft, ApplyOptions options, ILevelApplyEnvironment environment)
        {
            options = options ?? new ApplyOptions();
            var result = new ApplyResult { stage = LevelApplyStage.Preflight };
            if (draft == null || draft.definition == null || draft.manifest == null || environment == null)
                return Fail(result, "Draft and apply environment are required.");
            if (string.IsNullOrEmpty(draft.manifest.sourceGuid) || string.IsNullOrEmpty(draft.manifest.sourceFingerprint))
                return Fail(result, "Only sourced campaign drafts can be applied.");
            var source = environment.ResolveSource(draft.manifest.sourceGuid);
            if (source == null || source.SafeLevelId != draft.manifest.sourceLevelId)
                return Conflict(result, "The campaign source is missing or has a different level ID.");
            if (LevelDraftStore.Fingerprint(source) != draft.manifest.sourceFingerprint)
                return Conflict(result, "The campaign source changed after this draft was created.");

            result.stage = LevelApplyStage.Validation;
            result.validation = LevelStudioValidator.Validate(draft.definition, environment, environment);
            if (result.validation.HasErrors) return Fail(result, "Validation errors block apply.");
            string fingerprint = LevelDraftStore.Fingerprint(draft.definition);
            if (result.validation.warnings.Count != 0 && options.allowWarningsFingerprint != fingerprint)
            { result.stage = LevelApplyStage.WarningConfirmation; return Fail(result, "Confirm warnings for the current validation fingerprint."); }
            result.stage = LevelApplyStage.ScenePreflight;
            if (!environment.IsEditMode || environment.HasUnsavedScenes || !environment.SceneExists(draft.definition.sceneName))
                return Fail(result, "Apply requires edit mode, a saved editor state, and the target scene.");

            string error;
            result.stage = LevelApplyStage.Backup;
            if (!environment.Backup(draft, source, out result.backupPath, out error)) return Fail(result, error);
            try
            {
                result.stage = LevelApplyStage.SourceWrite;
                if (!environment.WriteSource(source, draft.definition, out error) || LevelDraftStore.Fingerprint(source) != fingerprint)
                    return Rollback(result, source, environment, string.IsNullOrEmpty(error) ? "Canonical source verification failed." : error);
                result.stage = LevelApplyStage.Build;
                if (!environment.Build(source, out error)) return Rollback(result, source, environment, error);
                result.stage = LevelApplyStage.Verify;
                if (!environment.Verify(source, out error)) return Rollback(result, source, environment, error);
                result.stage = LevelApplyStage.DraftRebase;
                store.RebaseApplied(draft, fingerprint);
                if (!environment.Complete(out error)) return Rollback(result, source, environment, error);
                result.stage = LevelApplyStage.Complete; result.success = true; result.message = "Draft applied.";
                return result;
            }
            catch (Exception e) { return Rollback(result, source, environment, e.Message); }
        }

        static ApplyResult Conflict(ApplyResult result, string message) { result.stage = LevelApplyStage.SourceConflict; result.conflict = true; return Fail(result, message); }
        static ApplyResult Fail(ApplyResult result, string message) { result.message = message ?? "Apply failed."; return result; }
        static ApplyResult Rollback(ApplyResult result, LevelDefinition source, ILevelApplyEnvironment environment, string message)
        {
            string rollbackError;
            result.rollbackSucceeded = environment.Restore(source, out rollbackError);
            result.message = message ?? "Apply failed.";
            if (!result.rollbackSucceeded && !string.IsNullOrEmpty(rollbackError)) result.message += " Rollback failed: " + rollbackError;
            return result;
        }
    }
}
