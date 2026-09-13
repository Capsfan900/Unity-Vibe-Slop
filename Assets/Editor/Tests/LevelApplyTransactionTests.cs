using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class LevelApplyTransactionTests
    {
        string root;
        LevelDefinition source, edited;
        LevelDraft draft;
        FakeEnvironment environment;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "vg1-apply-" + Guid.NewGuid().ToString("N"));
            source = ValidDefinition();
            edited = Clone(source); edited.parTime++;
            draft = new LevelDraft
            {
                definition = edited, baseDefinitionJson = EditorJsonUtility.ToJson(source),
                manifest = new LevelDraftManifest
                {
                    formatVersion = 1, schema = "VibeGame1.LevelDefinition.v1", revision = 1,
                    draftId = Guid.NewGuid().ToString("N"), sourceGuid = "source-guid", sourceLevelId = source.SafeLevelId,
                    sourceFingerprint = LevelDraftStore.Fingerprint(source), createdUtc = DateTime.UtcNow.ToString("o"), savedUtc = DateTime.UtcNow.ToString("o")
                }
            };
            environment = new FakeEnvironment(source);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(edited);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void SourceConflictStopsBeforeBackupOrWrite()
        {
            draft.manifest.sourceFingerprint = "changed";

            var result = new LevelApplyTransaction(new LevelDraftStore(root)).Apply(draft, new ApplyOptions(), environment);

            Assert.IsTrue(result.conflict);
            Assert.AreEqual(LevelApplyStage.SourceConflict, result.stage);
            Assert.AreEqual(0, environment.backups);
            Assert.AreEqual(0, environment.writes);
        }

        [Test]
        public void FailedBuildRestoresBothTargetsAndKeepsDraft()
        {
            environment.failBuild = true;
            string original = EditorJsonUtility.ToJson(source);

            var result = new LevelApplyTransaction(new LevelDraftStore(root)).Apply(draft, new ApplyOptions { allowWarningsFingerprint = LevelDraftStore.Fingerprint(edited) }, environment);

            Assert.IsFalse(result.success);
            Assert.AreEqual(LevelApplyStage.Build, result.stage);
            Assert.IsTrue(result.rollbackSucceeded);
            Assert.AreEqual(original, EditorJsonUtility.ToJson(source));
            Assert.AreEqual(1, environment.restores);
        }

        static LevelDefinition ValidDefinition()
        {
            var d = ScriptableObject.CreateInstance<LevelDefinition>();
            d.levelId = "apply"; d.displayName = "Apply"; d.sceneName = "Apply";
            d.playerStartMeta = Meta("T0.PlayerStart");
            d.zones = new[] { new ZoneDef { zoneId = "T0", splitName = "Split", size = Vector3.one * 100f } };
            d.platforms = new[] { new PlatformDef { meta = Meta("T0.Platform.01"), name = "Deck" } };
            d.spawns = new[] { new SpawnDef { meta = Meta("T0.Sentry.01"), name = "Spawn", prefabKey = "pshooter_enemy01" } };
            d.checkpoints = new[] { new CheckpointDef { meta = Meta("T0.Checkpoint.01"), name = "Checkpoint" } };
            d.torches = new[] { new TorchDef { meta = Meta("T0.Torch.01") } };
            d.runSplits = new[] { new RunSplitDef { meta = Meta("T0.RunSplit.01"), name = "Split", endSpawnerName = "Spawn" } };
            d.killZone.meta = Meta("Level.KillZone"); d.sky.meta = Meta("Level.Sky"); d.worldLeaderboard.meta = Meta("T0.WorldLeaderboard");
            return d;
        }

        static LevelDefinition Clone(LevelDefinition value) { var copy = ScriptableObject.CreateInstance<LevelDefinition>(); EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(value), copy); return copy; }
        static LevelObjectMeta Meta(string id) { return new LevelObjectMeta { objectId = id, zoneIdOverride = id.StartsWith("Level.") ? "" : "T0" }; }

        sealed class FakeEnvironment : ILevelApplyEnvironment
        {
            readonly LevelDefinition source; string snapshot;
            public int backups, writes, restores; public bool failBuild;
            public FakeEnvironment(LevelDefinition source) { this.source = source; }
            public bool IsEditMode { get { return true; } } public bool HasUnsavedScenes { get { return false; } }
            public LevelDefinition ResolveSource(string guid) { return guid == "source-guid" ? source : null; }
            public bool SceneExists(string sceneName) { return true; }
            public bool Backup(LevelDraft draft, LevelDefinition canonical, out string path, out string error) { backups++; snapshot = EditorJsonUtility.ToJson(canonical); path = "backup"; error = null; return true; }
            public bool WriteSource(LevelDefinition canonical, LevelDefinition value, out string error) { writes++; EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(value), canonical); error = null; return true; }
            public bool Build(LevelDefinition canonical, out string error) { error = failBuild ? "build failed" : null; return !failBuild; }
            public bool Verify(LevelDefinition canonical, out string error) { error = null; return true; }
            public bool Restore(LevelDefinition canonical, out string error) { restores++; EditorJsonUtility.FromJsonOverwrite(snapshot, canonical); error = null; return true; }
            public bool Complete(out string error) { error = null; return true; }
            public bool HasPrefab(string key) { return true; } public bool HasEnemyData(string key) { return true; }
            public bool HasItem(string key) { return true; } public bool HasMaterial(string key) { return true; }
            public bool IsProjectileCapable(string key) { return true; }
            public bool TryValidate(LevelDefinition definition, out string error) { error = null; return true; }
        }
    }
}
