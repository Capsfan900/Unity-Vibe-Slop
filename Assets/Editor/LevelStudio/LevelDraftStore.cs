using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    public enum LevelDraftSourceState { Unchanged, Changed, Missing, Unknown }
    public enum LevelDraftState { Valid, RecoveryOnly, ReadOnly, Invalid }

    public sealed class LevelDraftLoadResult
    {
        public LevelDraft draft;
        public LevelDraftSummary summary;
        public string diagnostic;
        public bool Success { get { return draft != null; } }
    }

    public sealed class LevelDraftRecoverySummary
    {
        public string path, autosavedUtc, diagnostic;
        public int revision;
        public bool valid, readOnly;
    }

    public sealed class LevelDraftRecoverResult
    {
        public LevelDraft draft;
        public LevelDraftRecoverySummary selected;
        public IReadOnlyList<LevelDraftRecoverySummary> history;
        public string diagnostic;
    }

    public sealed class LevelDraft : IDisposable
    {
        public LevelDraftManifest manifest;
        public LevelDefinition definition;
        public string assetPath;
        public string baseDefinitionJson;
        /// <summary>Caller owns loaded/recovered definitions and must release them when finished.</summary>
        public void Dispose() { if (definition != null) UnityEngine.Object.DestroyImmediate(definition); definition = null; }
    }

    public sealed class LevelDraftSummary
    {
        public string draftId, displayName, sourceLevelId, savedUtc;
        public int changedObjectCount;
        public bool hasValidRecovery, sourceChanged;
        public string autosavedUtc, recoveryPath, diagnostic;
        public int recoveryRevision;
        public LevelDraftSourceState sourceState;
        public LevelDraftState state;
    }

    /// <summary>Editor-only, project-local storage for protected Level Studio working copies.</summary>
    public sealed class LevelDraftStore
    {
        const int CurrentFormatVersion = 1;
        const int AutosaveGenerationCount = 3;
        const string ManualFileName = "draft.json";

        [Serializable]
        sealed class StoredDraft
        {
            public LevelDraftManifest manifest;
            public string definitionJson, baseDefinitionJson;
            [NonSerialized] public bool validated;
            [NonSerialized] public bool readFailure;
        }

        const string Schema = "VibeGame1.LevelDefinition.v1";
        readonly string rootPath;
        readonly Action<string> beforeFileOperation;
        // Unity serialization runs on the editor thread; the lock also coordinates separate store instances.
        static readonly ConcurrentDictionary<string, object> DraftLocks = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> Writing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Uses &lt;project&gt;/LevelDrafts, outside Assets and player builds.</summary>
        public LevelDraftStore() : this(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "LevelDrafts")) { }

        /// <summary>Inject a root for isolated tests or an alternate local editor workspace.</summary>
        public LevelDraftStore(string rootPath) : this(rootPath, null) { }

        /// <summary>Optional failure seam for deterministic EditMode persistence tests.</summary>
        public LevelDraftStore(string rootPath, Action<string> beforeFileOperation)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("A draft root is required.", "rootPath");
            this.rootPath = Path.GetFullPath(rootPath);
            this.beforeFileOperation = beforeFileOperation;
        }

        public string RootPath { get { return rootPath; } }

        public LevelDraft CreateFromCampaign(LevelDefinition source)
        {
            if (source == null) throw new ArgumentNullException("source");
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string sourceGuid = string.IsNullOrEmpty(sourcePath) ? string.Empty : AssetDatabase.AssetPathToGUID(sourcePath);
            if (string.IsNullOrEmpty(sourceGuid)) throw new InvalidOperationException("Campaign drafts require a saved LevelDefinition asset. Use CreateCustom for unsourced work.");
            string now = UtcNow();
            var manifest = new LevelDraftManifest
            {
                formatVersion = CurrentFormatVersion, schema = Schema, revision = 0, draftId = Guid.NewGuid().ToString("N"), displayName = source.displayName,
                sourceGuid = sourceGuid, sourceLevelId = source.SafeLevelId, sourceFingerprint = Fingerprint(source),
                createdUtc = now, savedUtc = now, autosavedUtc = string.Empty
            };
            var draft = new LevelDraft
            {
                manifest = manifest,
                definition = CloneDefinition(source),
                assetPath = ManualPath(manifest.draftId),
                baseDefinitionJson = PayloadJson(source)
            };
            try { Save(draft); return draft; }
            catch { draft.Dispose(); throw; }
        }

        /// <summary>Creates an explicitly unsourced draft. It is always apply-blocked until given a campaign source.</summary>
        public LevelDraft CreateCustom(LevelDefinition template)
        {
            if (template == null) throw new ArgumentNullException("template");
            string now = UtcNow();
            var manifest = new LevelDraftManifest { formatVersion = CurrentFormatVersion, schema = Schema, revision = 0,
                draftId = Guid.NewGuid().ToString("N"), displayName = template.displayName, sourceGuid = string.Empty,
                sourceLevelId = string.Empty, sourceFingerprint = string.Empty, createdUtc = now, savedUtc = now };
            var draft = new LevelDraft { manifest = manifest, definition = CloneDefinition(template),
                assetPath = ManualPath(manifest.draftId), baseDefinitionJson = PayloadJson(template) };
            try { Save(draft); return draft; }
            catch { draft.Dispose(); throw; }
        }

        /// <summary>Atomically replaces the whole manual document; campaign assets are never save targets.</summary>
        public void Save(LevelDraft draft)
        {
            ValidateDraft(draft);
            string id = draft.manifest.draftId;
            lock (Sync(id))
            {
                BeginWrite(id);
                try
                {
                    EnsureWritable(draft.manifest);
                    LevelDraftManifest pending = CopyManifest(draft.manifest);
                    pending.displayName = draft.definition.displayName;
                    pending.savedUtc = UtcNow();
                    pending.revision = NextRevision(id, pending.revision);
                    string contents = Serialize(draft, pending);
                    WriteAtomically(ManualPath(id), contents, "manual");
                    draft.manifest = pending;
                    draft.assetPath = ManualPath(id);
                }
                finally { EndWrite(id); }
            }
        }

        /// <summary>Keeps the newest three complete snapshots without overwriting the manual save.</summary>
        public void Autosave(LevelDraft draft)
        {
            ValidateDraft(draft);
            string id = draft.manifest.draftId;
            lock (Sync(id))
            {
                BeginWrite(id);
                try
                {
                    EnsureWritable(draft.manifest);
                    LevelDraftManifest pending = CopyManifest(draft.manifest);
                    pending.displayName = draft.definition.displayName;
                    pending.autosavedUtc = UtcNow();
                    pending.revision = NextRevision(id, pending.revision);
                    string contents = Serialize(draft, pending); // Validate before even making a directory.
                    string candidate = Path.Combine(DraftPath(id), "autosave.candidate." + Guid.NewGuid().ToString("N") + ".json");
                    WriteAtomically(candidate, contents, "candidate");
                    // Revisions are immutable: publication never rotates over an existing generation.
                    FileOperation("promotion", "move", () => File.Move(candidate, AutosavePath(id, pending.revision)));
                    RepairRecoveries(id);
                    draft.manifest = pending;
                }
                finally { EndWrite(id); }
            }
        }

        object Sync(string id) { return DraftLocks.GetOrAdd(DraftPath(id), _ => new object()); }
        void BeginWrite(string id)
        {
            lock (Writing) if (!Writing.Add(DraftPath(id))) throw new InvalidOperationException("A write for this draft is already in progress.");
        }
        void EndWrite(string id) { lock (Writing) Writing.Remove(DraftPath(id)); }

        public LevelDraft Load(string draftId)
        {
            return LoadResult(draftId).draft;
        }

        public LevelDraftLoadResult LoadResult(string draftId)
        {
            var result = new LevelDraftLoadResult();
            if (!IsDraftId(draftId) || !Directory.Exists(DraftPath(draftId))) { result.diagnostic = "Draft does not exist."; return result; }
            lock (Sync(draftId))
            {
                StoredDraft stored; string diagnostic;
                if (TryReadStored(ManualPath(draftId), draftId, out stored, out diagnostic)) result.draft = Materialize(stored, ManualPath(draftId));
                result.diagnostic = diagnostic;
                result.summary = SummaryFor(draftId, stored, diagnostic, FindRecovery(draftId));
                return result;
            }
        }

        public bool Exists(string draftId) { return IsDraftId(draftId) && File.Exists(ManualPath(draftId)); }

        public IReadOnlyList<LevelDraftSummary> List()
        {
            var summaries = new List<LevelDraftSummary>();
            if (!Directory.Exists(rootPath)) return summaries;
            foreach (string directory in GetDirectories(rootPath))
            {
                string draftId = Path.GetFileName(directory);
                if (!IsDraftId(draftId)) continue;
                lock (Sync(draftId))
                {
                    StoredDraft stored; string diagnostic;
                    TryReadStored(ManualPath(draftId), draftId, out stored, out diagnostic);
                    summaries.Add(SummaryFor(draftId, stored, diagnostic, FindRecovery(draftId)));
                }
            }
            summaries.Sort((a, b) => string.CompareOrdinal(b.savedUtc, a.savedUtc));
            return summaries;
        }

        /// <summary>Returns the newest valid recovery snapshot, skipping damaged newer generations.</summary>
        public LevelDraft Recover(string draftId)
        {
            return RecoverResult(draftId).draft;
        }

        public LevelDraftRecoverResult RecoverResult(string draftId)
        {
            if (!IsDraftId(draftId)) return new LevelDraftRecoverResult { history = new LevelDraftRecoverySummary[0], diagnostic = "Invalid draft ID." };
            lock (Sync(draftId))
            {
            var history = RecoveryHistory(draftId);
            var result = new LevelDraftRecoverResult { history = history };
            foreach (var entry in history)
                if (entry.valid) { result.selected = entry; break; }
            if (result.selected == null) { result.diagnostic = history.Count > 0 ? history[0].diagnostic : "No recovery snapshots."; return result; }
            StoredDraft stored; string diagnostic;
            if (TryReadStored(result.selected.path, draftId, out stored, out diagnostic)) result.draft = Materialize(stored, ManualPath(draftId));
            else result.diagnostic = diagnostic;
            return result;
            }
        }

        public IReadOnlyList<LevelDraftRecoverySummary> RecoveryHistory(string draftId)
        {
            var history = new List<LevelDraftRecoverySummary>();
            if (!IsDraftId(draftId) || !Directory.Exists(DraftPath(draftId))) return history;
            lock (Sync(draftId))
            {
                RepairRecoveries(draftId);
                foreach (string path in RecoveryFiles(draftId, true))
                {
                    StoredDraft stored; string diagnostic;
                    bool valid = TryReadStored(path, draftId, out stored, out diagnostic);
                    history.Add(new LevelDraftRecoverySummary { path = path, valid = valid, diagnostic = diagnostic,
                        revision = stored != null && stored.manifest != null ? stored.manifest.revision : 0,
                        autosavedUtc = stored != null && stored.manifest != null ? stored.manifest.autosavedUtc : string.Empty,
                        readOnly = Unsupported(stored) });
                }
                history.Sort(CompareRecovery);
                return history;
            }
        }

        /// <summary>Canonical serialized-data hash used to detect source changes outside the draft.</summary>
        public static string Fingerprint(LevelDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException("definition");
            return Schema + ":" + Hash(PayloadJson(definition));
        }

        /// <summary>Exposed for recovery UI; it never creates an AssetDatabase campaign asset.</summary>
        public string DraftPath(string draftId)
        {
            if (!IsDraftId(draftId)) throw new ArgumentException("Invalid draft id.", "draftId");
            return Path.Combine(rootPath, draftId);
        }

        static LevelDefinition CloneDefinition(LevelDefinition source)
        {
            var copy = ScriptableObject.CreateInstance<LevelDefinition>();
            try { EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source, false), copy); copy.name = source.name; return copy; }
            catch { UnityEngine.Object.DestroyImmediate(copy); throw; }
        }

        static string UtcNow() { return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); }
        string ManualPath(string draftId) { return Path.Combine(DraftPath(draftId), ManualFileName); }
        string AutosavePath(string draftId, int revision) { return Path.Combine(DraftPath(draftId), "autosave.revision." + revision.ToString(CultureInfo.InvariantCulture) + ".json"); }

        static string Serialize(LevelDraft draft, LevelDraftManifest manifest)
        {
            string payload = PayloadJson(draft.definition);
            if (!ValidManifest(manifest, manifest.draftId) || !ValidatePayload(payload) || !ValidatePayload(draft.baseDefinitionJson))
                throw new InvalidDataException("Draft manifest, payload, or base snapshot is invalid.");
            manifest.payloadSha256 = Hash(payload);
            manifest.basePayloadSha256 = Hash(draft.baseDefinitionJson);
            return JsonUtility.ToJson(new StoredDraft
            {
                manifest = manifest, definitionJson = payload, baseDefinitionJson = draft.baseDefinitionJson
            }, true);
        }

        static LevelDraft Deserialize(string json, string manualPath, string expectedId)
        {
            StoredDraft stored;
            string diagnostic;
            if (!TryDeserialize(json, expectedId, out stored, out diagnostic)) throw new InvalidDataException(diagnostic);
            return Materialize(stored, manualPath);
        }

        static LevelDraft Materialize(StoredDraft stored, string manualPath)
        {
            if (stored == null || !stored.validated) throw new InvalidDataException("Only validated current-schema drafts may be materialized.");
            var definition = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(stored.definitionJson, definition);
                return new LevelDraft { manifest = CopyManifest(stored.manifest), definition = definition, assetPath = manualPath, baseDefinitionJson = stored.baseDefinitionJson };
            }
            catch { UnityEngine.Object.DestroyImmediate(definition); throw; }
        }

        bool TryReadStored(string path, string expectedId, out StoredDraft stored, out string diagnostic)
        {
            stored = null; diagnostic = null;
            if (!File.Exists(path)) { diagnostic = "Draft file is missing."; return false; }
            try
            {
                string json = null;
                FileOperation("read", "read", () => json = File.ReadAllText(path));
                return TryDeserialize(json, expectedId, out stored, out diagnostic);
            }
            catch (IOException e) { stored = new StoredDraft { readFailure = true }; diagnostic = "Could not read draft: " + e.Message; return false; }
            catch (UnauthorizedAccessException e) { stored = new StoredDraft { readFailure = true }; diagnostic = "Could not read draft: " + e.Message; return false; }
        }

        static bool TryDeserialize(string json, string expectedId, out StoredDraft stored, out string diagnostic)
        {
            stored = null; diagnostic = null;
            if (string.IsNullOrWhiteSpace(json)) { diagnostic = "Draft envelope is empty."; return false; }
            StoredDraft parsed;
            try { parsed = JsonUtility.FromJson<StoredDraft>(json); }
            catch (Exception e) { diagnostic = "Draft envelope is invalid JSON: " + e.Message; return false; }
            if (parsed == null || parsed.manifest == null) { diagnostic = "Draft envelope is missing its manifest."; return false; }
            // Rejected payload strings never escape parsing. Metadata is safe for newer-schema rows.
            stored = new StoredDraft { manifest = parsed.manifest };
            var m = parsed.manifest;
            if (m.formatVersion != CurrentFormatVersion) { diagnostic = m.formatVersion > CurrentFormatVersion ? "Draft format is newer and read-only." : "Draft format requires an explicit migration."; return false; }
            if (!ValidManifest(m, expectedId)) { diagnostic = "Draft envelope schema, identity, revision, or timestamps are invalid."; return false; }
            if (string.IsNullOrEmpty(parsed.definitionJson) || string.IsNullOrEmpty(parsed.baseDefinitionJson) ||
                m.payloadSha256 != Hash(parsed.definitionJson) || m.basePayloadSha256 != Hash(parsed.baseDefinitionJson)) { diagnostic = "Draft payload or base snapshot failed its SHA-256 check."; return false; }
            if (!ValidatePayload(parsed.definitionJson) || !ValidatePayload(parsed.baseDefinitionJson)) { diagnostic = "Draft payload or base snapshot is partial or has non-finite numbers."; return false; }
            parsed.validated = true;
            stored = parsed;
            return true;
        }

        sealed class RecoveryInfo { public StoredDraft stored; public string path, diagnostic; public int revision; public bool readOnly; }

        LevelDraftSummary SummaryFor(string draftId, StoredDraft manual, string manualDiagnostic, RecoveryInfo recovery)
        {
            bool manualValid = manual != null && manual.validated;
            bool readOnly = Unsupported(manual) || recovery.readOnly;
            StoredDraft visible = manualValid || Unsupported(manual) ? manual : recovery.stored;
            LevelDraftManifest m = visible != null ? visible.manifest : null;
            LevelDraftState state = readOnly ? LevelDraftState.ReadOnly : manualValid ? LevelDraftState.Valid : (recovery.stored != null ? LevelDraftState.RecoveryOnly : LevelDraftState.Invalid);
            LevelDraftSourceState sourceState = m != null ? SourceState(m) : LevelDraftSourceState.Unknown;
            return new LevelDraftSummary
            {
                draftId = draftId, displayName = m != null ? m.displayName : string.Empty,
                sourceLevelId = m != null ? m.sourceLevelId : string.Empty, savedUtc = m != null ? m.savedUtc : string.Empty,
                autosavedUtc = recovery.stored != null ? recovery.stored.manifest.autosavedUtc : string.Empty,
                hasValidRecovery = recovery.stored != null, recoveryPath = recovery.path, recoveryRevision = recovery.revision,
                state = state, sourceState = sourceState,
                sourceChanged = sourceState != LevelDraftSourceState.Unchanged,
                changedObjectCount = visible != null && visible.validated ? ChangedObjectCount(visible.baseDefinitionJson, visible.definitionJson) : 0,
                diagnostic = manualDiagnostic ?? recovery.diagnostic
            };
        }

        RecoveryInfo FindRecovery(string draftId)
        {
            var best = new RecoveryInfo();
            if (!IsDraftId(draftId) || !Directory.Exists(DraftPath(draftId))) return best;
            foreach (var entry in RecoveryHistory(draftId))
            {
                best.readOnly |= entry.readOnly;
                if (!entry.valid) { if (best.diagnostic == null) best.diagnostic = entry.diagnostic; continue; }
                if (best.stored != null) continue;
                StoredDraft stored; string diagnostic;
                if (TryReadStored(entry.path, draftId, out stored, out diagnostic))
                { best.stored = stored; best.path = entry.path; best.revision = stored.manifest.revision; }
            }
            return best;
        }

        int NextRevision(string draftId, int current)
        {
            int max = current;
            if (IsDraftId(draftId) && Directory.Exists(DraftPath(draftId)))
                foreach (string path in GetFiles(DraftPath(draftId), "*.json"))
                {
                    StoredDraft stored; string diagnostic;
                    if (TryReadStored(path, draftId, out stored, out diagnostic) && stored.manifest.revision > max) max = stored.manifest.revision;
                    else if (Unsupported(stored) || (stored != null && stored.readFailure))
                        throw new InvalidOperationException("Cannot safely allocate a revision: " + diagnostic);
                }
            return checked(max + 1);
        }

        // Repair is restartable at every operation. Immutable generations eliminate destructive rotation.
        // Unsupported schemas freeze repair as well as saves; corrupt evidence moves intact out of the active set.
        void RepairRecoveries(string draftId)
        {
            var valid = new List<LevelDraftRecoverySummary>();
            var corrupt = new List<string>();
            StoredDraft manual; string diagnostic;
            TryReadStored(ManualPath(draftId), draftId, out manual, out diagnostic);
            if (Unsupported(manual) || (manual != null && manual.readFailure)) return;
            foreach (string path in RecoveryFiles(draftId, false))
            {
                StoredDraft stored;
                bool good = TryReadStored(path, draftId, out stored, out diagnostic);
                if (Unsupported(stored) || (stored != null && stored.readFailure)) return;
                if (good) valid.Add(new LevelDraftRecoverySummary { path = path, valid = true, revision = stored.manifest.revision, autosavedUtc = stored.manifest.autosavedUtc });
                else corrupt.Add(path);
            }
            valid.Sort(CompareRecovery);
            for (int i = AutosaveGenerationCount; i < valid.Count; i++)
            {
                string path = valid[i].path;
                FileOperation("trim", "delete", () => File.Delete(path));
            }
            foreach (string path in corrupt)
            {
                string quarantine = Path.Combine(DraftPath(draftId), "quarantine");
                FileOperation("quarantine", "directory", () => Directory.CreateDirectory(quarantine));
                string target = Path.Combine(quarantine, Path.GetFileName(path));
                if (File.Exists(target)) target = Path.Combine(quarantine, Path.GetFileNameWithoutExtension(path) + "." + Guid.NewGuid().ToString("N") + ".json");
                FileOperation("quarantine", "move", () => File.Move(path, target));
            }
            foreach (string path in GetFiles(DraftPath(draftId), "*.tmp.*"))
                FileOperation("cleanup", "delete", () => File.Delete(path));
        }

        static int CompareRecovery(LevelDraftRecoverySummary a, LevelDraftRecoverySummary b)
        {
            int order = b.valid.CompareTo(a.valid);
            if (order == 0) order = b.revision.CompareTo(a.revision);
            if (order == 0) order = ParsedUtc(b.autosavedUtc).CompareTo(ParsedUtc(a.autosavedUtc));
            return order != 0 ? order : string.CompareOrdinal(a.path, b.path);
        }

        string[] GetFiles(string path, string pattern)
        {
            string[] files = new string[0];
            if (Directory.Exists(path)) FileOperation("enumerate", "read", () => files = Directory.GetFiles(path, pattern));
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
        string[] GetDirectories(string path)
        {
            string[] directories = null;
            FileOperation("enumerate", "read", () => directories = Directory.GetDirectories(path));
            Array.Sort(directories, StringComparer.Ordinal);
            return directories;
        }
        IEnumerable<string> RecoveryFiles(string id, bool includeQuarantine)
        {
            foreach (string path in GetFiles(DraftPath(id), "autosave*.json")) yield return path;
            if (includeQuarantine)
                foreach (string path in GetFiles(Path.Combine(DraftPath(id), "quarantine"), "*.json")) yield return path;
        }

        static LevelDraftManifest CopyManifest(LevelDraftManifest source)
        {
            return JsonUtility.FromJson<LevelDraftManifest>(JsonUtility.ToJson(source));
        }

        static string PayloadJson(LevelDefinition definition)
        {
            var copy = CloneDefinition(definition);
            try
            {
                copy.name = string.Empty;
                copy.hideFlags = HideFlags.None;
                return EditorJsonUtility.ToJson(copy, false);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var hex = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        static bool ValidatePayload(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            var probe = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(json, probe);
                return FiniteFields(probe) && string.Equals(json, PayloadJson(probe), StringComparison.Ordinal);
            }
            catch (Exception) { return false; }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
        }

        static LevelDraftSourceState SourceState(LevelDraftManifest manifest)
        {
            if (string.IsNullOrEmpty(manifest.sourceGuid) || string.IsNullOrEmpty(manifest.sourceFingerprint)) return LevelDraftSourceState.Unknown;
            string path = AssetDatabase.GUIDToAssetPath(manifest.sourceGuid);
            if (string.IsNullOrEmpty(path)) return LevelDraftSourceState.Missing;
            var source = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
            if (source == null || source.SafeLevelId != manifest.sourceLevelId) return LevelDraftSourceState.Missing;
            return Fingerprint(source) == manifest.sourceFingerprint ? LevelDraftSourceState.Unchanged : LevelDraftSourceState.Changed;
        }

        static int ChangedObjectCount(string baseJson, string currentJson)
        {
            if (baseJson == currentJson) return 0;
            var before = ScriptableObject.CreateInstance<LevelDefinition>();
            var after = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(baseJson, before); EditorJsonUtility.FromJsonOverwrite(currentJson, after);
                var records = DiffProjection(before);
                var current = DiffProjection(after);
                int changed = 0;
                foreach (var pair in current.values) { string prior; if (!records.values.TryGetValue(pair.Key, out prior) || prior != pair.Value) changed++; records.values.Remove(pair.Key); }
                changed += records.values.Count;
                foreach (var pair in current.orders)
                {
                    List<string> prior;
                    if (!records.orders.TryGetValue(pair.Key, out prior)) continue;
                    var common = new HashSet<string>(prior, StringComparer.Ordinal);
                    common.IntersectWith(pair.Value);
                    // Membership already belongs to its object. Only relative order of surviving objects
                    // belongs to the collection, so an insertion never double-counts all following objects.
                    if (!prior.Where(common.Contains).SequenceEqual(pair.Value.Where(common.Contains))) changed++;
                }
                return changed;
            }
            finally { UnityEngine.Object.DestroyImmediate(before); UnityEngine.Object.DestroyImmediate(after); }
        }

        sealed class Projection
        {
            public readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<string>> orders = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            public void Add(string key, object value) { values.Add(key, value == null ? "null" : JsonUtility.ToJson(value)); }
            public void Collection<T>(string group, T[] values) where T : class
            {
                var order = new List<string>(); orders.Add(group, order);
                var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
                if (values == null) return;
                for (int i = 0; i < values.Length; i++)
                {
                    T value = values[i];
                    var zone = value as ZoneDef;
                    var metaField = value != null ? value.GetType().GetField("meta") : null;
                    var meta = metaField != null ? metaField.GetValue(value) as LevelObjectMeta : null;
                    string id = zone != null ? zone.zoneId : meta != null ? meta.objectId : null;
                    string identity = string.IsNullOrEmpty(id) ? "index:" + i : "id:" + id.Length + ":" + id;
                    int occurrence; occurrences.TryGetValue(identity, out occurrence); occurrences[identity] = occurrence + 1;
                    string key = group + "/" + identity + ":" + occurrence;
                    order.Add(key);
                    Record(key, value);
                }
            }
            void Record(string key, object value)
            {
                var arena = value as ArenaDef;
                if (arena != null)
                {
                    Add(key, new ArenaDiff { meta = arena.meta, enabled = arena.enabled, triggerName = arena.triggerName,
                        triggerPosition = arena.triggerPosition, triggerSize = arena.triggerSize, clearSpawnerName = arena.clearSpawnerName });
                    Add(key + "/gate", new GateDiff { meta = arena.gateMeta, open = arena.gateOpenPosition, closed = arena.gateClosedPosition,
                        size = arena.gateSize, name = arena.gateName, material = arena.gateMaterialKey });
                    Add(key + "/exit", new GateDiff { enabled = arena.hasExitGate, meta = arena.exitGateMeta, open = arena.exitGateOpenPosition,
                        closed = arena.exitGateClosedPosition, size = arena.exitGateSize, name = arena.exitGateName, material = arena.exitGateMaterialKey });
                    Add(key + "/solar", arena.solarRealm);
                    return;
                }
                var sequence = value as ProjectileSequenceDef;
                if (sequence != null)
                {
                    var copy = JsonUtility.FromJson<ProjectileSequenceDef>(JsonUtility.ToJson(sequence));
                    copy.engagementWindows = new ProjectileEngagementWindowDef[0];
                    Add(key, copy);
                    Collection(key + "/windows", sequence.engagementWindows);
                    return;
                }
                Add(key, value);
            }
        }

        static Projection DiffProjection(LevelDefinition level)
        {
            var result = new Projection();
            result.Add("$root", new RootDiff { levelId = level.levelId, displayName = level.displayName, sceneName = level.sceneName, parTime = level.parTime, orderIndex = level.orderIndex, requiredRunSouls = level.requiredRunSouls, requiredRegularKills = level.requiredRegularKills, gradeBonuses = level.gradeBonuses });
            result.Add("playerStart", new PlayerStartDiff { position = level.playerStart, yaw = level.playerStartYaw, meta = level.playerStartMeta });
            result.Add("killZone", level.killZone); result.Add("sky", level.sky); result.Add("worldLeaderboard", level.worldLeaderboard);
            result.Collection("zones", level.zones); result.Collection("platforms", level.platforms); result.Collection("ramps", level.ramps);
            result.Collection("spawns", level.spawns); result.Collection("pickups", level.pickups); result.Collection("checkpoints", level.checkpoints);
            result.Collection("torches", level.torches); result.Collection("pedestals", level.pedestals); result.Collection("balloons", level.balloons);
            result.Collection("waters", level.waters); result.Collection("arenas", level.arenas); result.Collection("projectileSequences", level.projectileSequences);
            result.Collection("challengeRoutes", level.challengeRoutes); result.Collection("runSplits", level.runSplits);
            return result;
        }

        [Serializable] sealed class RootDiff { public string levelId, displayName, sceneName; public float parTime; public int orderIndex, requiredRunSouls, requiredRegularKills; public RunGradeBonusDef gradeBonuses; }
        [Serializable] sealed class PlayerStartDiff { public Vector3 position; public float yaw; public LevelObjectMeta meta; }
        [Serializable] sealed class GateDiff { public Vector3 open, closed, size; public string name, material; public LevelObjectMeta meta; public bool enabled; }
        [Serializable] sealed class ArenaDiff { public LevelObjectMeta meta; public bool enabled; public string triggerName, clearSpawnerName; public Vector3 triggerPosition, triggerSize; }

        static void ValidateDraft(LevelDraft draft)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            if (draft.manifest == null || draft.definition == null) throw new ArgumentException("Draft manifest and definition are required.", "draft");
            if (!IsDraftId(draft.manifest.draftId)) throw new ArgumentException("Draft id is invalid.", "draft");
            if (!FiniteFields(draft.definition)) throw new InvalidDataException("Draft contains non-finite numbers.");
        }

        void EnsureWritable(LevelDraftManifest manifest)
        {
            if (!ValidManifest(manifest, manifest.draftId, true)) throw new InvalidOperationException("Draft manifest is invalid or uses an unsupported format/schema.");
            StoredDraft manual; string diagnostic;
            if (File.Exists(ManualPath(manifest.draftId)) && !TryReadStored(ManualPath(manifest.draftId), manifest.draftId, out manual, out diagnostic))
                throw new InvalidOperationException("The manual document cannot be overwritten: " + diagnostic);
            foreach (string path in RecoveryFiles(manifest.draftId, false))
            {
                StoredDraft stored;
                TryReadStored(path, manifest.draftId, out stored, out diagnostic);
                if (Unsupported(stored) || (stored != null && stored.readFailure))
                    throw new InvalidOperationException("A recovery document is read-only: " + diagnostic);
            }
        }

        static bool Unsupported(StoredDraft stored)
        {
            var m = stored != null ? stored.manifest : null;
            return m != null && (m.formatVersion != CurrentFormatVersion || (!string.IsNullOrEmpty(m.schema) && m.schema != Schema));
        }
        static DateTime ParsedUtc(string value)
        {
            DateTime parsed;
            return DateTime.TryParseExact(value, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed) && parsed.Kind == DateTimeKind.Utc ? parsed : DateTime.MinValue;
        }
        static bool ValidManifest(LevelDraftManifest m, string id, bool allowInitial = false)
        {
            return m != null && m.formatVersion == CurrentFormatVersion && m.schema == Schema && IsDraftId(m.draftId) && m.draftId == id &&
                m.revision >= (allowInitial ? 0 : 1) && ParsedUtc(m.createdUtc) != DateTime.MinValue && ParsedUtc(m.savedUtc) != DateTime.MinValue &&
                (string.IsNullOrEmpty(m.autosavedUtc) || ParsedUtc(m.autosavedUtc) != DateTime.MinValue);
        }
        static bool FiniteFields(object value)
        {
            if (value == null || value is string) return true;
            if (value is float) return !float.IsNaN((float)value) && !float.IsInfinity((float)value);
            if (value is double) return !double.IsNaN((double)value) && !double.IsInfinity((double)value);
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum) return true;
            if (value is Array) { foreach (object item in (Array)value) if (!FiniteFields(item)) return false; return true; }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (!FiniteFields(field.GetValue(value))) return false;
            return true;
        }

        static bool IsDraftId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32) return false;
            for (int i = 0; i < value.Length; i++) if (!Uri.IsHexDigit(value[i])) return false;
            return true;
        }

        void FileOperation(string stage, string operation, Action action)
        {
            if (beforeFileOperation != null) beforeFileOperation(stage + ":" + operation + ":before");
            action();
            if (beforeFileOperation != null) beforeFileOperation(stage + ":" + operation + ":after");
        }

        void WriteAtomically(string path, string contents, string stage)
        {
            FileOperation(stage, "directory", () => Directory.CreateDirectory(Path.GetDirectoryName(path)));
            string temporaryPath = path + ".tmp." + Guid.NewGuid().ToString("N");
            byte[] bytes = Encoding.UTF8.GetBytes(contents);
            try
            {
                FileStream stream = null;
                try
                {
                    FileOperation(stage, "create", () => stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough));
                    FileOperation(stage, "write", () => stream.Write(bytes, 0, bytes.Length));
                    FileOperation(stage, "flush", () => stream.Flush(true));
                }
                finally { if (stream != null) stream.Dispose(); }
                if (File.Exists(path)) FileOperation(stage, "replace", () => File.Replace(temporaryPath, path, null));
                else FileOperation(stage, "move", () => File.Move(temporaryPath, path));
            }
            finally { FileOperation(stage, "cleanup", () => { if (File.Exists(temporaryPath)) FileOperation(stage, "delete", () => File.Delete(temporaryPath)); }); }
        }

        static bool SourceChanged(LevelDraftManifest manifest)
        {
            if (string.IsNullOrEmpty(manifest.sourceGuid)) return false;
            string sourcePath = AssetDatabase.GUIDToAssetPath(manifest.sourceGuid);
            if (string.IsNullOrEmpty(sourcePath)) return true;
            var source = AssetDatabase.LoadAssetAtPath<LevelDefinition>(sourcePath);
            return source == null || !string.Equals(Fingerprint(source), manifest.sourceFingerprint, StringComparison.Ordinal);
        }
    }
}
