using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class LevelDraftStoreTests
    {
        string root;
        string sourcePath;
        LevelDefinition source;
        LevelDraftStore store;
        HashSet<LevelDefinition> existingDefinitions;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "VibeGame1_LevelDraftStore_" + Guid.NewGuid().ToString("N"));
            store = new LevelDraftStore(root);
            sourcePath = "Assets/Editor/Tests/LevelDraftStore_Source_" + Guid.NewGuid().ToString("N") + ".asset";
            source = CompleteDefinition();
            AssetDatabase.CreateAsset(source, sourcePath);
            AssetDatabase.SaveAssets();
            existingDefinitions = new HashSet<LevelDefinition>(Resources.FindObjectsOfTypeAll<LevelDefinition>());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var definition in Resources.FindObjectsOfTypeAll<LevelDefinition>())
                if (!existingDefinitions.Contains(definition) && !AssetDatabase.Contains(definition))
                    UnityEngine.Object.DestroyImmediate(definition);
            if (!string.IsNullOrEmpty(sourcePath)) AssetDatabase.DeleteAsset(sourcePath);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void CreateEditCancel_NeverChangesCampaignJson()
        {
            string before = EditorJsonUtility.ToJson(source, true);

            var draft = store.CreateFromCampaign(source);
            draft.definition.displayName = "Changed only in the working copy";
            store.Save(draft);

            Assert.AreEqual(before, EditorJsonUtility.ToJson(source, true));
        }

        [Test]
        public void Save_ReplacesThePersistedDocumentWithoutLeavingATemporaryFile()
        {
            var draft = store.CreateFromCampaign(source);
            draft.definition.displayName = "First version";
            store.Save(draft);
            draft.definition.displayName = "Replacement version";
            store.Save(draft);

            Assert.AreEqual("Replacement version", store.Load(draft.manifest.draftId).definition.displayName);
            Assert.IsFalse(File.Exists(draft.assetPath + ".tmp"));
        }

        [Test]
        public void FreshStore_AssignsBothPayloadHashesAndLoadsItsManualAndRecoveryDocuments()
        {
            var draft = store.CreateFromCampaign(source);
            Autosave(draft, "recovery");
            var loaded = store.LoadResult(draft.manifest.draftId);
            var recovered = store.RecoverResult(draft.manifest.draftId);

            Assert.IsFalse(string.IsNullOrEmpty(draft.manifest.payloadSha256));
            Assert.IsFalse(string.IsNullOrEmpty(draft.manifest.basePayloadSha256));
            Assert.IsTrue(loaded.Success);
            Assert.IsNotNull(recovered.draft);
            Assert.GreaterOrEqual(recovered.history.Count, 1);
            loaded.draft.Dispose(); recovered.draft.Dispose(); draft.Dispose();
        }

        [Test]
        public void InjectedReplaceFailure_PreservesManualBytesAndLiveManifest()
        {
            var draft = store.CreateFromCampaign(source);
            string bytes = File.ReadAllText(draft.assetPath);
            int revision = draft.manifest.revision;
            var failing = new LevelDraftStore(root, stage => { if (stage == "manual:replace:before") throw new IOException("injected"); });
            draft.definition.displayName = "must not persist";

            Assert.Throws<IOException>(() => failing.Save(draft));
            Assert.AreEqual(bytes, File.ReadAllText(draft.assetPath));
            Assert.AreEqual(revision, draft.manifest.revision);
            draft.Dispose();
        }

        [Test]
        public void Autosave_RetainsThreeRecoveryGenerations()
        {
            var draft = store.CreateFromCampaign(source);
            Autosave(draft, "one");
            Autosave(draft, "two");
            Autosave(draft, "three");
            Autosave(draft, "four");

            var paths = store.RecoveryHistory(draft.manifest.draftId).Where(h => h.valid).Select(h => h.path).ToArray();
            File.WriteAllText(paths[0], "corrupt");
            Assert.AreEqual("three", store.Recover(draft.manifest.draftId).definition.displayName);
            File.WriteAllText(paths[1], "corrupt");
            Assert.AreEqual("two", store.Recover(draft.manifest.draftId).definition.displayName);
            File.WriteAllText(paths[2], "corrupt");
            Assert.IsNull(store.Recover(draft.manifest.draftId));
        }

        [Test]
        public void Autosave_NeverOverwritesTheLastManualSave()
        {
            var draft = store.CreateFromCampaign(source);
            draft.definition.displayName = "manual save";
            store.Save(draft);
            Autosave(draft, "autosave only");

            Assert.AreEqual("manual save", store.Load(draft.manifest.draftId).definition.displayName);
            Assert.AreEqual("autosave only", store.Recover(draft.manifest.draftId).definition.displayName);
        }

        [Test]
        public void Recover_SkipsACorruptNewestAutosave()
        {
            var draft = store.CreateFromCampaign(source);
            Autosave(draft, "older valid autosave");
            Autosave(draft, "newest autosave");
            File.WriteAllText(store.RecoveryHistory(draft.manifest.draftId).First(h => h.valid).path, "not json");

            var recovered = store.Recover(draft.manifest.draftId);

            Assert.IsNotNull(recovered);
            Assert.AreEqual("older valid autosave", recovered.definition.displayName);
        }

        [Test]
        public void List_ReportsSourceGuidFingerprintConflictAndRecoveryState()
        {
            var draft = store.CreateFromCampaign(source);
            draft.definition.displayName = "Working copy edit";
            store.Save(draft);
            Autosave(draft, "autosaved");
            var beforeCampaignChange = store.List()[0];
            source.displayName = "Campaign changed elsewhere";
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssets();

            var summary = store.List()[0];

            Assert.AreEqual(AssetDatabase.AssetPathToGUID(sourcePath), draft.manifest.sourceGuid);
            Assert.AreEqual(1, beforeCampaignChange.changedObjectCount);
            Assert.IsFalse(beforeCampaignChange.sourceChanged);
            Assert.IsTrue(summary.sourceChanged);
            Assert.IsTrue(summary.hasValidRecovery);
            Assert.AreEqual(draft.manifest.draftId, summary.draftId);
            Assert.IsTrue(store.Exists(draft.manifest.draftId));
            Assert.IsNotNull(store.Load(draft.manifest.draftId));
        }

        [Test]
        public void Fingerprint_IsDeterministicAndChangesForSerializedData()
        {
            var clone = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), clone);
                clone.name = source.name;
                Assert.AreEqual(LevelDraftStore.Fingerprint(source), LevelDraftStore.Fingerprint(clone));
                clone.name = "A different Unity object name";
                clone.hideFlags = HideFlags.HideAndDontSave;
                Assert.AreEqual(LevelDraftStore.Fingerprint(source), LevelDraftStore.Fingerprint(clone), "Unity identity must not affect apply conflicts");
                clone.killZone.center = new Vector3(99f, -12f, 5f);
                Assert.AreNotEqual(LevelDraftStore.Fingerprint(source), LevelDraftStore.Fingerprint(clone));
            }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
        }

        [Test]
        public void SaveLoad_PreservesEveryLevelDefinitionField()
        {
            string expected = LevelDraftStore.Fingerprint(source);
            var draft = store.CreateFromCampaign(source);
            store.Save(draft);

            var loaded = store.Load(draft.manifest.draftId);

            Assert.AreEqual(expected, LevelDraftStore.Fingerprint(loaded.definition));
            Assert.AreEqual(source.arenas[0].solarRealm.enemySpawnPosition, loaded.definition.arenas[0].solarRealm.enemySpawnPosition);
            Assert.AreEqual(source.projectileSequences[0].engagementWindows[0].arrivalEnd, loaded.definition.projectileSequences[0].engagementWindows[0].arrivalEnd);
        }

        [Test]
        public void DefaultRoot_IsProjectLocalLevelDrafts()
        {
            string expected = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "LevelDrafts");
            Assert.AreEqual(Path.GetFullPath(expected), new LevelDraftStore().RootPath);
        }

        [Test]
        public void CreateFromCampaign_RejectsAnUnsavedSourceAndDraftPathsCannotEscapeTheRoot()
        {
            var unsaved = CompleteDefinition();
            try
            {
                Assert.Throws<InvalidOperationException>(() => store.CreateFromCampaign(unsaved));
                Assert.Throws<ArgumentException>(() => store.DraftPath("../outside"));
            }
            finally { UnityEngine.Object.DestroyImmediate(unsaved); }
        }

        [Test]
        public void CorruptManualWithValidAutosave_RemainsVisibleAsRecoveryOnly()
        {
            var draft = store.CreateFromCampaign(source);
            Autosave(draft, "recover me");
            File.WriteAllText(draft.assetPath, "{\"manifest\":{\"formatVersion\":1}}");

            var summary = store.List()[0];

            Assert.AreEqual(LevelDraftState.RecoveryOnly, summary.state);
            Assert.IsTrue(summary.hasValidRecovery);
            Assert.AreEqual("recover me", store.Recover(draft.manifest.draftId).definition.displayName);
        }

        [Test]
        public void NewerManual_RemainsVisibleReadOnlyWithoutMaterializingOrRewriting()
        {
            var draft = store.CreateFromCampaign(source);
            string before = File.ReadAllText(draft.assetPath);
            string newer = before.Replace("\"formatVersion\": 1", "\"formatVersion\": 99");
            File.WriteAllText(draft.assetPath, newer);

            var result = store.LoadResult(draft.manifest.draftId);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(LevelDraftState.ReadOnly, result.summary.state);
            Assert.AreEqual(newer, File.ReadAllText(draft.assetPath));
        }

        [Test]
        public void DraftClone_IsolatesNestedCollectionsFromTheCampaign()
        {
            var draft = store.CreateFromCampaign(source);
            draft.definition.zones[0].aliases[0] = "draft-only";
            draft.definition.arenas[0].solarRealm.enemySpawnPosition = new Vector3(101, 102, 103);
            draft.definition.projectileSequences[0].engagementWindows[0].arrivalEnd = 99f;

            Assert.AreEqual("first", source.zones[0].aliases[0]);
            Assert.AreEqual(new Vector3(19, 20, 21), source.arenas[0].solarRealm.enemySpawnPosition);
            Assert.AreEqual(5f, source.projectileSequences[0].engagementWindows[0].arrivalEnd);
        }

        // Each seam interrupts real disk I/O; a fresh store must repair without another save.
        [TestCase("candidate:directory:before", false)]
        [TestCase("candidate:directory:after", false)]
        [TestCase("candidate:create:before", false)]
        [TestCase("candidate:create:after", false)]
        [TestCase("candidate:write:before", false)]
        [TestCase("candidate:write:after", false)]
        [TestCase("candidate:flush:before", false)]
        [TestCase("candidate:flush:after", false)]
        [TestCase("candidate:move:before", false)]
        [TestCase("candidate:move:after", true)]
        [TestCase("candidate:cleanup:before", true)]
        [TestCase("candidate:cleanup:after", true)]
        [TestCase("promotion:move:before", true)]
        [TestCase("promotion:move:after", true)]
        [TestCase("trim:delete:before", true)]
        [TestCase("trim:delete:after", true)]
        public void InterruptedAutosave_RepairsNewestThreeWithoutChangingManualOrLiveManifest(string failAt, bool published)
        {
            var draft = store.CreateFromCampaign(source);
            Autosave(draft, "one"); Autosave(draft, "two"); Autosave(draft, "three");
            string manual = File.ReadAllText(draft.assetPath);
            string manifest = JsonUtility.ToJson(draft.manifest);
            draft.definition.displayName = "four";
            bool hit = false;
            var failing = new LevelDraftStore(root, stage =>
            {
                if (!hit && stage == failAt) { hit = true; throw new IOException("interrupted " + stage); }
            });
            Assert.Throws<IOException>(() => failing.Autosave(draft));
            Assert.IsTrue(hit, "Fault stage was not exercised: " + failAt);
            Assert.AreEqual(manual, File.ReadAllText(draft.assetPath));
            Assert.AreEqual(manifest, JsonUtility.ToJson(draft.manifest));
            var reopened = new LevelDraftStore(root);
            var history = reopened.RecoveryHistory(draft.manifest.draftId).Where(h => h.valid).ToArray();
            CollectionAssert.AreEqual(published ? new[] { 5, 4, 3 } : new[] { 4, 3, 2 }, history.Select(h => h.revision));
            using (var recovered = reopened.Recover(draft.manifest.draftId))
                Assert.AreEqual(published ? "four" : "three", recovered.definition.displayName);
            Assert.LessOrEqual(Directory.GetFiles(store.DraftPath(draft.manifest.draftId), "autosave*.json").Length, 3);
            Assert.IsEmpty(Directory.GetFiles(store.DraftPath(draft.manifest.draftId), "*.tmp.*"));
            Assert.AreEqual(manual, File.ReadAllText(draft.assetPath));
        }

        [TestCase("draft.json")]
        [TestCase("autosave.0.json")]
        [TestCase("autosave.candidate.future.json")]
        public void FutureEnvelope_ProtectsEveryByteAndBlocksBothWritePaths(string fileName)
        {
            var draft = store.CreateFromCampaign(source);
            Autosave(draft, "current recovery");
            string path = Path.Combine(store.DraftPath(draft.manifest.draftId), fileName);
            string future = "{\"manifest\":{\"formatVersion\":99,\"schema\":\"future-schema\",\"draftId\":\"" + draft.manifest.draftId +
                "\",\"displayName\":\"Future document\",\"revision\":900},\"definitionJson\":\"not current schema\",\"futureData\":[1,2,3]}";
            File.WriteAllText(path, future);
            var files = Directory.GetFiles(store.DraftPath(draft.manifest.draftId)).ToDictionary(p => p, File.ReadAllText);
            string manifest = JsonUtility.ToJson(draft.manifest);
            Assert.Throws<InvalidOperationException>(() => store.Save(draft));
            Assert.Throws<InvalidOperationException>(() => store.Autosave(draft));
            Assert.AreEqual(LevelDraftState.ReadOnly, store.List()[0].state);
            store.RecoveryHistory(draft.manifest.draftId);
            foreach (var pair in files) Assert.AreEqual(pair.Value, File.ReadAllText(pair.Key));
            Assert.AreEqual(manifest, JsonUtility.ToJson(draft.manifest));
            if (fileName == "draft.json") Assert.IsFalse(store.LoadResult(draft.manifest.draftId).Success);
        }

        [TestCase("base-null")]
        [TestCase("base-partial")]
        [TestCase("base-nonfinite")]
        [TestCase("current-nonfinite")]
        [TestCase("current-null")]
        [TestCase("old-version")]
        [TestCase("unknown-schema")]
        [TestCase("negative-revision")]
        [TestCase("bad-created-time")]
        public void InvalidLiveDocument_IsRejectedBeforePersistence(string invalid)
        {
            var draft = store.CreateFromCampaign(source);
            string manual = File.ReadAllText(draft.assetPath);
            switch (invalid)
            {
                case "base-null": draft.baseDefinitionJson = null; break;
                case "base-partial": draft.baseDefinitionJson = "{}"; break;
                case "base-nonfinite": draft.baseDefinitionJson = draft.baseDefinitionJson.Replace("91.25", "NaN"); break;
                case "current-nonfinite": draft.definition.parTime = float.NaN; break;
                case "current-null": draft.Dispose(); break;
                case "old-version": draft.manifest.formatVersion = 0; break;
                case "unknown-schema": draft.manifest.schema = "unknown"; break;
                case "negative-revision": draft.manifest.revision = -1; break;
                case "bad-created-time": draft.manifest.createdUtc = "yesterday"; break;
            }
            string manifest = JsonUtility.ToJson(draft.manifest);
            Assert.That(() => store.Save(draft), Throws.Exception);
            Assert.That(() => store.Autosave(draft), Throws.Exception);
            Assert.AreEqual(manual, File.ReadAllText(draft.assetPath));
            Assert.AreEqual(manifest, JsonUtility.ToJson(draft.manifest));
            Assert.AreEqual(1, Directory.GetFiles(store.DraftPath(draft.manifest.draftId)).Length);
        }

        [Test]
        public void FailedCreation_ReleasesItsClone()
        {
            int count = Resources.FindObjectsOfTypeAll<LevelDefinition>().Length;
            var failing = new LevelDraftStore(root, stage => { if (stage == "manual:write:before") throw new IOException(); });
            Assert.Throws<IOException>(() => failing.CreateFromCampaign(source));
            Assert.Throws<IOException>(() => failing.CreateCustom(source));
            Assert.AreEqual(count, Resources.FindObjectsOfTypeAll<LevelDefinition>().Length);
        }

        [Test]
        public void SeparateStore_ReentrantWriteCannotOverwriteTheOuterTransaction()
        {
            var draft = store.CreateFromCampaign(source);
            var sibling = store.Load(draft.manifest.draftId);
            sibling.definition.displayName = "nested write";
            bool attempted = false;
            var outer = new LevelDraftStore(root, stage =>
            {
                if (stage != "manual:write:before") return;
                attempted = true;
                Assert.Throws<InvalidOperationException>(() => new LevelDraftStore(root).Save(sibling));
            });
            draft.definition.displayName = "outer write";
            outer.Save(draft);
            Assert.IsTrue(attempted);
            Assert.AreEqual(2, draft.manifest.revision);
            Assert.AreEqual("outer write", store.Load(draft.manifest.draftId).definition.displayName);
        }

        [TestCase("trim:delete:before")][TestCase("trim:delete:after")]
        [TestCase("quarantine:directory:before")][TestCase("quarantine:directory:after")]
        [TestCase("quarantine:move:before")][TestCase("quarantine:move:after")]
        [TestCase("cleanup:delete:before")][TestCase("cleanup:delete:after")]
        public void InterruptedRepair_IsRestartableAndPreservesEvidence(string failAt)
        {
            var draft = store.CreateFromCampaign(source);
            Autosave(draft, "one"); Autosave(draft, "two"); Autosave(draft, "three");
            var interrupted = new LevelDraftStore(root, stage => { if (stage == "trim:delete:before") throw new IOException(); });
            draft.definition.displayName = "four";
            Assert.Throws<IOException>(() => interrupted.Autosave(draft));
            string directory = store.DraftPath(draft.manifest.draftId);
            string manual = File.ReadAllText(draft.assetPath);
            string manifest = JsonUtility.ToJson(draft.manifest);
            File.WriteAllText(Path.Combine(directory, "autosave.candidate.corrupt.json"), "retain these bytes");
            File.WriteAllText(Path.Combine(directory, "autosave.abandoned.tmp.unique"), "unfinished temp");
            bool hit = false;
            var failing = new LevelDraftStore(root, stage => { if (stage == failAt) { hit = true; throw new IOException(); } });
            Assert.Throws<IOException>(() => failing.RecoveryHistory(draft.manifest.draftId));
            Assert.IsTrue(hit);
            var history = new LevelDraftStore(root).RecoveryHistory(draft.manifest.draftId);
            CollectionAssert.AreEqual(new[] { 5, 4, 3 }, history.Where(h => h.valid).Select(h => h.revision));
            Assert.AreEqual("retain these bytes", File.ReadAllText(history.Single(h => !h.valid).path));
            Assert.AreEqual(3, Directory.GetFiles(directory, "autosave*.json").Length);
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp.*"));
            Assert.AreEqual(manual, File.ReadAllText(draft.assetPath));
            Assert.AreEqual(manifest, JsonUtility.ToJson(draft.manifest));
        }

        [TestCase("read:read:before")][TestCase("read:read:after")]
        [TestCase("enumerate:read:before")][TestCase("enumerate:read:after")]
        public void ReadFailure_DoesNotTurnAnUnreadableManualIntoAWritableDocument(string failAt)
        {
            var draft = store.CreateFromCampaign(source);
            string manual = File.ReadAllText(draft.assetPath);
            string manifest = JsonUtility.ToJson(draft.manifest);
            var failing = new LevelDraftStore(root, stage => { if (stage == failAt) throw new IOException("read unavailable"); });
            Assert.That(() => failing.Save(draft), Throws.Exception);
            Assert.AreEqual(manual, File.ReadAllText(draft.assetPath));
            Assert.AreEqual(manifest, JsonUtility.ToJson(draft.manifest));
        }

        [TestCase("platforms")][TestCase("ramps")][TestCase("spawns")][TestCase("pickups")]
        [TestCase("checkpoints")][TestCase("torches")][TestCase("pedestals")][TestCase("balloons")]
        [TestCase("waters")][TestCase("arenas")][TestCase("projectileSequences")][TestCase("challengeRoutes")]
        [TestCase("runSplits")][TestCase("worldLeaderboard")][TestCase("sky")][TestCase("killZone")]
        public void EveryCatalogObject_MetadataEditCountsExactlyOnce(string field)
        {
            var draft = store.CreateFromCampaign(source);
            object record = typeof(LevelDefinition).GetField(field).GetValue(draft.definition);
            if (record is Array) record = ((Array)record).GetValue(0);
            ((LevelObjectMeta)record.GetType().GetField("meta").GetValue(record)).friendlyName += " edited";
            store.Save(draft);
            Assert.AreEqual(1, store.List()[0].changedObjectCount, field);
        }

        [TestCase("root")][TestCase("grade")][TestCase("zone")][TestCase("start")][TestCase("start-meta")]
        [TestCase("gate")][TestCase("gate-meta")][TestCase("gate-zone")][TestCase("exit")][TestCase("exit-meta")]
        [TestCase("solar")][TestCase("solar-meta")][TestCase("window")][TestCase("window-meta")]
        public void ScalarAndNestedEdits_HaveOneOwner(string edit)
        {
            var draft = store.CreateFromCampaign(source);
            var d = draft.definition;
            switch (edit)
            {
                case "root": d.displayName += " edited"; break;
                case "grade": d.gradeBonuses.sSouls++; break;
                case "zone": d.zones[0].aliases[0] += " edited"; break;
                case "start": d.playerStart.x++; break;
                case "start-meta": d.playerStartMeta.friendlyName += " edited"; break;
                case "gate": d.arenas[0].gateMaterialKey += " edited"; break;
                case "gate-meta": d.arenas[0].gateMeta.friendlyName += " edited"; break;
                case "gate-zone": d.arenas[0].gateMeta.zoneIdOverride += " edited"; break;
                case "exit": d.arenas[0].exitGateMaterialKey += " edited"; break;
                case "exit-meta": d.arenas[0].exitGateMeta.friendlyName += " edited"; break;
                case "solar": d.arenas[0].solarRealm.enemySpawnPosition.x++; break;
                case "solar-meta": d.arenas[0].solarRealm.meta.friendlyName += " edited"; break;
                case "window": d.projectileSequences[0].engagementWindows[0].arrivalEnd++; break;
                case "window-meta": d.projectileSequences[0].engagementWindows[0].meta.friendlyName += " edited"; break;
            }
            store.Save(draft);
            Assert.AreEqual(1, store.List()[0].changedObjectCount, edit);
        }

        [TestCase("gate")][TestCase("exit")][TestCase("solar")]
        public void DisabledArenaChildren_StillOwnTheirAuthoredFields(string child)
        {
            source.arenas[0].enabled = false;
            source.arenas[0].hasExitGate = false;
            source.arenas[0].solarRealm.enabled = false;
            var draft = store.CreateFromCampaign(source);
            if (child == "gate") draft.definition.arenas[0].gateClosedPosition.x++;
            if (child == "exit") draft.definition.arenas[0].exitGateMeta.friendlyName += " edited";
            if (child == "solar") draft.definition.arenas[0].solarRealm.retryYaw++;
            store.Save(draft);
            Assert.AreEqual(1, store.List()[0].changedObjectCount);
        }

        [TestCase("object")][TestCase("zone")]
        public void DuplicateIds_DoNotHideAnEarlierRecordEdit(string kind)
        {
            if (kind == "object") source.platforms = new[] { source.platforms[0], JsonUtility.FromJson<PlatformDef>(JsonUtility.ToJson(source.platforms[0])) };
            else source.zones = new[] { source.zones[0], JsonUtility.FromJson<ZoneDef>(JsonUtility.ToJson(source.zones[0])) };
            var draft = store.CreateFromCampaign(source);
            if (kind == "object") draft.definition.platforms[0].center.x++;
            else draft.definition.zones[0].canonicalName += " edited";
            store.Save(draft);
            Assert.AreEqual(1, store.List()[0].changedObjectCount);
        }

        [TestCase("add", 1)][TestCase("remove", 1)][TestCase("reorder", 1)]
        public void CollectionMembershipAndSemanticOrder_AreCountedWithoutOverlapping(string edit, int expected)
        {
            source.runSplits = new[] { source.runSplits[0], new RunSplitDef { meta = Meta("T0.Split.02"), name = "Second" } };
            var draft = store.CreateFromCampaign(source);
            if (edit == "add") draft.definition.runSplits = draft.definition.runSplits.Concat(new[] { new RunSplitDef { meta = Meta("T0.Split.03") } }).ToArray();
            if (edit == "remove") draft.definition.runSplits = draft.definition.runSplits.Take(1).ToArray();
            if (edit == "reorder") Array.Reverse(draft.definition.runSplits);
            store.Save(draft);
            Assert.AreEqual(expected, store.List()[0].changedObjectCount);
        }

        [Test]
        public void RecoveryOnlySummary_UsesValidatedRecoveryDiffAndTime()
        {
            var draft = store.CreateFromCampaign(source);
            Autosave(draft, "recovery");
            string autosaved = draft.manifest.autosavedUtc;
            Assert.AreEqual(autosaved, store.List()[0].autosavedUtc);
            Assert.AreEqual(LevelDraftState.Valid, store.LoadResult(draft.manifest.draftId).summary.state);
            File.WriteAllText(draft.assetPath, "broken manual");
            var summary = store.List()[0];
            Assert.AreEqual(LevelDraftState.RecoveryOnly, summary.state);
            Assert.AreEqual("recovery", summary.displayName);
            Assert.AreEqual(1, summary.changedObjectCount);
            Assert.AreEqual(autosaved, summary.autosavedUtc);
        }

        [Test]
        public void CorruptCandidate_IsQuarantinedAndHistoryRemainsDeterministic()
        {
            var draft = store.CreateFromCampaign(source);
            Autosave(draft, "valid");
            string directory = store.DraftPath(draft.manifest.draftId);
            File.WriteAllText(Path.Combine(directory, "autosave.candidate.bad.json"), "corrupt evidence");
            var history = store.RecoveryHistory(draft.manifest.draftId);
            Assert.AreEqual(2, history.Count);
            Assert.IsTrue(history[0].valid);
            Assert.IsFalse(history[1].valid);
            Assert.IsNotEmpty(history[1].diagnostic);
            Assert.AreEqual("corrupt evidence", File.ReadAllText(history[1].path));
            CollectionAssert.AreEqual(history.Select(h => h.path), store.RecoveryHistory(draft.manifest.draftId).Select(h => h.path));
            Assert.AreEqual(1, Directory.GetFiles(directory, "autosave*.json").Length);
        }

        [Serializable] sealed class TestEnvelope { public LevelDraftManifest manifest = null; public string definitionJson = null, baseDefinitionJson = null; }

        [Test]
        public void HistoryTies_UseParsedTimeThenPathAndKeepLegacyMissingMiddleReadable()
        {
            var draft = store.CreateFromCampaign(source);
            Autosave(draft, "recovery");
            string currentPath = store.RecoveryHistory(draft.manifest.draftId).Single().path;
            var envelope = JsonUtility.FromJson<TestEnvelope>(File.ReadAllText(currentPath));
            string directory = store.DraftPath(draft.manifest.draftId);
            string first = Path.Combine(directory, "autosave.0.json");
            File.Move(currentPath, first);
            envelope.manifest.autosavedUtc = "2026-09-12T12:00:00.0000000Z";
            File.WriteAllText(first, JsonUtility.ToJson(envelope));
            string second = Path.Combine(directory, "autosave.2.json");
            File.WriteAllText(second, JsonUtility.ToJson(envelope));
            envelope.manifest.autosavedUtc = "2026-09-12T13:00:00.0000000Z";
            string candidate = Path.Combine(directory, "autosave.candidate.tie.json");
            File.WriteAllText(candidate, JsonUtility.ToJson(envelope));
            File.WriteAllText(Path.Combine(directory, "autosave.candidate.bad.json"), "corrupt");
            var history = store.RecoveryHistory(draft.manifest.draftId);
            CollectionAssert.AreEqual(new[] { candidate, first, second }, history.Where(h => h.valid).Select(h => h.path));
            Assert.IsFalse(history.Last().valid);
            Assert.IsNotEmpty(history.Last().diagnostic);
            Assert.AreEqual(candidate, store.RecoverResult(draft.manifest.draftId).selected.path);
            Assert.AreEqual(source.displayName, store.Load(draft.manifest.draftId).definition.displayName);
        }

        [Test]
        public void CreateLoadRecover_AreFieldCompleteAndDoNotAliasCampaignOrSiblings()
        {
            byte[] bytes = File.ReadAllBytes(sourcePath);
            Assert.IsFalse(EditorUtility.IsDirty(source));
            var created = store.CreateFromCampaign(source);
            store.Autosave(created);
            var loaded = store.Load(created.manifest.draftId);
            var recovered = store.Recover(created.manifest.draftId);
            foreach (var copy in new[] { created, loaded, recovered })
            {
                AssertFieldsEqual(source, copy.definition, "definition");
                AssertNoMutableAliases(source, copy.definition, "campaign");
            }
            AssertNoMutableAliases(created.definition, loaded.definition, "load");
            AssertNoMutableAliases(loaded.definition, recovered.definition, "recover");
            foreach (var copy in new[] { created, loaded, recovered }) MutateEveryCollection(copy.definition);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(sourcePath));
            Assert.IsFalse(EditorUtility.IsDirty(source));
            var pristine = store.Load(created.manifest.draftId);
            AssertFieldsEqual(source, pristine.definition, "persisted");
        }

        static FieldInfo[] DataFields(Type type) { return type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly); }
        static void AssertFieldsEqual(object expected, object actual, string path)
        {
            if (expected == null) { Assert.IsNull(actual, path); return; }
            Assert.IsNotNull(actual, path);
            var type = expected.GetType();
            if (type.IsValueType || type == typeof(string)) { Assert.AreEqual(expected, actual, path); return; }
            if (expected is Array)
            {
                var a = (Array)expected; var b = (Array)actual;
                Assert.AreEqual(a.Length, b.Length, path);
                for (int i = 0; i < a.Length; i++) AssertFieldsEqual(a.GetValue(i), b.GetValue(i), path + "[" + i + "]");
                return;
            }
            foreach (var field in DataFields(type)) AssertFieldsEqual(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name);
        }
        static void AssertNoMutableAliases(object expected, object actual, string path)
        {
            if (expected == null || expected is string || expected.GetType().IsValueType) return;
            Assert.AreNotSame(expected, actual, path);
            if (expected is Array)
            {
                var a = (Array)expected; var b = (Array)actual;
                for (int i = 0; i < a.Length; i++) AssertNoMutableAliases(a.GetValue(i), b.GetValue(i), path + "[" + i + "]");
                return;
            }
            foreach (var field in DataFields(expected.GetType())) AssertNoMutableAliases(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name);
        }
        static void MutateEveryCollection(object value)
        {
            if (value == null || value is string || value.GetType().IsValueType) return;
            if (value is Array)
            {
                var array = (Array)value;
                foreach (object child in array) MutateEveryCollection(child);
                if (array.Length > 0) array.SetValue(array.GetType().GetElementType().IsValueType ? Activator.CreateInstance(array.GetType().GetElementType()) : null, 0);
                return;
            }
            foreach (var field in DataFields(value.GetType())) MutateEveryCollection(field.GetValue(value));
        }

        void Autosave(LevelDraft draft, string displayName)
        {
            draft.definition.displayName = displayName;
            draft.manifest.displayName = displayName;
            store.Autosave(draft);
        }

        LevelDefinition CompleteDefinition()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.name = "CompleteLevel";
            def.levelId = "complete_level";
            def.displayName = "Complete Level";
            def.sceneName = "CompleteScene";
            def.parTime = 91.25f;
            def.orderIndex = 4;
            def.requiredRunSouls = 123;
            def.requiredRegularKills = 7;
            def.gradeBonuses = new RunGradeBonusDef { dSouls = 1, cSouls = 2, bSouls = 3, aSouls = 4, sSouls = 5 };
            def.runSplits = new[] { new RunSplitDef { meta = Meta("T0.Split.01"), name = "Opening", endSpawnerName = "Spawn_A", sSeconds = 1, aSeconds = 2, bSeconds = 3, cSeconds = 4 } };
            def.zones = new[] { new ZoneDef { zoneId = "T0", canonicalName = "Start", splitName = "Opening", aliases = new[] { "first" }, order = 1, center = new Vector3(1, 2, 3), size = new Vector3(4, 5, 6), displayColor = Color.magenta } };
            def.playerStart = new Vector3(2, 3, 4);
            def.playerStartMeta = Meta("T0.PlayerStart");
            def.playerStartYaw = 45f;
            def.platforms = new[] { new PlatformDef { meta = Meta("T0.Platform.01"), name = "Platform", center = new Vector3(1, 1, 1), size = new Vector3(2, 3, 4), materialKey = "Stone", trim = true, trimMaterialKey = "NeonCyan", isStatic = false } };
            def.ramps = new[] { new RampDef { meta = Meta("T0.Ramp.01"), name = "Ramp", basePosition = new Vector3(2, 2, 2), width = 3, run = 4, rise = 5, thickness = .6f, yaw = 90, materialKey = "Stone", isStatic = false } };
            def.spawns = new[] { new SpawnDef { meta = Meta("T0.Spawn.01"), name = "Spawn_A", prefabKey = "Enemy_Heavy", position = new Vector3(3, 3, 3), yaw = 120, isBoss = true } };
            def.pickups = new[] { new PickupDef { meta = Meta("T0.Pickup.01"), name = "Pickup", itemKey = "Grapple", position = new Vector3(4, 4, 4) } };
            def.checkpoints = new[] { new CheckpointDef { meta = Meta("T0.Checkpoint.01"), name = "Checkpoint", position = new Vector3(5, 5, 5), spawnOffset = new Vector3(1, 2, 3) } };
            def.torches = new[] { new TorchDef { meta = Meta("T0.Torch.01"), name = "Torch", basePosition = new Vector3(6, 6, 6) } };
            def.pedestals = new[] { new PedestalDef { meta = Meta("T0.Pedestal.01"), name = "Pedestal", groundPosition = new Vector3(7, 7, 7), triggerRadius = 4 } };
            def.balloons = new[] { new BalloonDef { meta = Meta("T0.Balloon.01"), name = "Balloon", position = new Vector3(8, 8, 8), launchSpeed = 13, respawnSeconds = 6, radius = 2 } };
            def.waters = new[] { new WaterDef { meta = Meta("T0.Water.01"), name = "Water", center = new Vector3(9, 9, 9), size = new Vector3(3, .04f, 7), flowDirection = Vector3.right, flowSpeed = 8 } };
            def.arenas = new[] { new ArenaDef { meta = Meta("T0.Arena.01"), gateMeta = Meta("T0.Gate.01"), exitGateMeta = Meta("T0.ExitGate.01"), enabled = true, gateName = "Gate", gateSize = new Vector3(1, 2, 3), gateMaterialKey = "Gate", gateOpenPosition = Vector3.one, gateClosedPosition = Vector3.up, triggerName = "Trigger", triggerPosition = Vector3.forward, triggerSize = new Vector3(4, 5, 6), clearSpawnerName = "Spawn_A", hasExitGate = true, exitGateName = "Exit", exitGateSize = new Vector3(7, 8, 9), exitGateMaterialKey = "Stone", exitGateClosedPosition = Vector3.left, exitGateOpenPosition = Vector3.right, solarRealm = new SolarRealmDef { meta = Meta("T0.Portal.01"), enabled = true, themeMaterialKey = "SolarGold", exteriorCenter = new Vector3(1, 2, 3), exteriorRadius = 4, visualRadius = 5, realmCenter = new Vector3(6, 7, 8), realmFloorRadius = 9, realmShellRadius = 10, playerEntryPosition = new Vector3(11, 12, 13), playerEntryYaw = 14, retryPosition = new Vector3(15, 16, 17), retryYaw = 18, enemySpawnerName = "Spawn_A", enemySpawnPosition = new Vector3(19, 20, 21), enemySpawnYaw = 22, arenaPickupName = "Pickup", arenaPickupPosition = new Vector3(23, 24, 25), hasReturn = false, realmExitPosition = new Vector3(26, 27, 28), returnPosition = new Vector3(29, 30, 31), returnYaw = 32 } } };
            def.projectileSequences = new[] { new ProjectileSequenceDef { meta = Meta("T0.Sequence.01"), name = "Sequence", spawnerNames = new[] { "Spawn_A" }, recoveryGap = .2f, readinessTimeout = .3f, shotResolutionTimeout = .4f, firstMemberAcquireDelay = .5f, repeatFromIndex = 0, progressOrigin = Vector3.down, progressDirection = Vector3.forward, memberProgressGates = new[] { 1f }, engagementWindows = new[] { new ProjectileEngagementWindowDef { meta = Meta("T0.Window.01"), spawnerName = "Spawn_A", routeStart = Vector3.left, routeEnd = Vector3.right, halfWidth = 2, heightTolerance = 3, arrivalStart = 4, arrivalEnd = 5 } } } };
            def.challengeRoutes = new[] { new ChallengeRouteDef { meta = Meta("T0.ChallengeRoute.01"), routeId = "challenge", sourceSpawnerNames = new[] { "Spawn_A" }, entryCenter = Vector3.one, entrySize = new Vector3(2, 3, 4), rejoinCenter = Vector3.up, rejoinSize = new Vector3(5, 6, 7) } };
            def.worldLeaderboard = new WorldLeaderboardDef { meta = Meta("T0.WorldLeaderboard"), enabled = true, name = "Leaderboard", position = new Vector3(1, 2, 3), yaw = 12, size = new Vector2(7, 8), rowCount = 3, backingMaterialKey = "Stone", glowMaterialKey = "NeonPink" };
            def.sky = new SkyDef { meta = Meta("Level.Sky"), enabled = false, starCount = 12, radius = 33, seed = 44, includeEclipse = false, eclipseYawDeg = 55, eclipsePitchDeg = 66, eclipseDiameterDeg = 77 };
            def.killZone = new KillZoneDef { meta = Meta("Level.KillZone"), name = "Kill", center = new Vector3(3, 2, 1), size = new Vector3(9, 8, 7) };
            return def;
        }

        static LevelObjectMeta Meta(string id)
        {
            return new LevelObjectMeta { objectId = id, friendlyName = "Friendly " + id, zoneIdOverride = "T0" };
        }
    }
}
