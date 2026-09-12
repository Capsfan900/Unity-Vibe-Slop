using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class LevelStudioBrowserTests
    {
        LevelDefinition definition;
        LevelStudioVocabulary vocabulary;
        IReadOnlyList<LevelStudioRow> hierarchy;

        [SetUp]
        public void SetUp()
        {
            definition = Miniature();
            vocabulary = LevelStudioVocabulary.ParseEnemyTerms("{\"pshooter_enemy01\":{\"canonical\":\"Sentry\",\"aliases\":[\"blue squid\"]}}");
            hierarchy = LevelStudioBrowser.BuildHierarchy(definition, vocabulary);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(definition);
        }

        [TestCase("blue squid", "T2.Sentry.02")]
        [TestCase("Knight", "T2")]
        [TestCase("last ramp", "T4")]
        public void SearchResolvesVocabulary(string query, string expectedId)
        {
            CollectionAssert.Contains(LevelStudioBrowser.Filter(hierarchy, query).Select(x => x.id), expectedId);
        }

        [Test]
        public void SearchIsTrimmedCaseInsensitiveAndKeepsAncestorsInSourceOrder()
        {
            var rows = LevelStudioBrowser.Filter(hierarchy, "  BLUE SQUID ").ToArray();

            CollectionAssert.AreEqual(new[] { "objects", "zone:T2", "route:T2:Shared Routes", "type:T2:Shared Routes:Sentry", "T2.Sentry.02" }, rows.Select(x => x.id));
            Assert.AreEqual(0, LevelStudioBrowser.Filter(hierarchy, "no such object").Count);
            Assert.AreEqual(hierarchy.Count, LevelStudioBrowser.Filter(hierarchy, " ").Count);
        }

        [Test]
        public void EveryCatalogRecordIsRepresentedOnceWithUniqueRowKeysAndPaths()
        {
            var records = LevelObjectCatalog.Enumerate(definition).ToArray();
            var objects = hierarchy.Where(row => row.kind == LevelStudioRowKind.Object).ToArray();

            Assert.AreEqual(records.Length, objects.Length);
            Assert.AreEqual(records.Length, objects.Select(row => row.recordPath).Distinct().Count());
            Assert.AreEqual(objects.Length, objects.Select(row => row.key).Distinct().Count());
            CollectionAssert.Contains(objects.Select(row => row.recordPath), "arenas[0].gate");
            CollectionAssert.Contains(objects.Select(row => row.recordPath), "projectileSequences[0].engagementWindows[0]");
        }

        [Test]
        public void ObjectRowsCarryDurableOwnerAnchorKindDataAndResolvedZoneFields()
        {
            var gate = hierarchy.Single(row => row.recordPath == "arenas[0].gate");
            var spawn = hierarchy.Single(row => row.recordPath == "spawns[0]");

            Assert.AreEqual("T2.Arena.01", gate.ownerId);
            Assert.AreEqual("arenas[0]", gate.ownerPath);
            Assert.AreEqual(LevelObjectKind.Gate, gate.canonicalKind);
            Assert.AreEqual(2, gate.anchorCount);
            Assert.AreEqual("arenas[0].gateClosedPosition", gate.anchors[1].path);
            Assert.AreEqual("T2", gate.resolvedZoneId);
            Assert.AreEqual("Helix Tower", gate.resolvedZoneName);
            Assert.AreEqual("Knight", gate.resolvedZoneSplit);
            Assert.AreEqual(LevelObjectKind.Spawn, spawn.canonicalKind);
            Assert.AreEqual("pshooter_enemy01", spawn.dataKey);
            Assert.AreEqual(definition.spawns[0].position, spawn.anchor);
        }

        [Test]
        public void DuplicateAndMissingObjectIdsRemainVisibleByDurableFieldPath()
        {
            definition.platforms = new[]
            {
                new PlatformDef { meta = Meta("T2.Platform.01", "T2"), name = "One", center = new Vector3(0, 0, 200) },
                new PlatformDef { meta = Meta("T2.Platform.01", "T2"), name = "Two", center = new Vector3(0, 0, 201) },
                new PlatformDef { meta = Meta("", "T2"), name = "Three", center = new Vector3(0, 0, 202) }
            };

            var rows = LevelStudioBrowser.BuildHierarchy(definition, vocabulary).Where(row => row.kind == LevelStudioRowKind.Object).ToArray();

            Assert.AreEqual(3, rows.Count(row => row.recordPath.StartsWith("platforms[", StringComparison.Ordinal)));
            Assert.IsTrue(rows.Any(row => row.recordPath == "platforms[2]" && row.id == "platforms[2]"));
            Assert.IsTrue(rows.Any(row => row.recordPath == "platforms[0]" && row.diagnostic.Contains("Duplicate")));
            Assert.IsTrue(rows.Any(row => row.recordPath == "platforms[1]" && row.diagnostic.Contains("Duplicate")));
        }

        [Test]
        public void ZoneGroupingHonorsOverrideOwnerRunSplitAndGlobalsWithoutAssigningZones()
        {
            definition.spawns[0].meta.zoneIdOverride = "T2";
            definition.arenas[0].gateMeta.zoneIdOverride = "";
            definition.runSplits[0].meta.zoneIdOverride = "";
            string before = EditorJsonUtility.ToJson(definition);

            var rows = LevelStudioBrowser.BuildHierarchy(definition, vocabulary);

            Assert.AreEqual(before, EditorJsonUtility.ToJson(definition));
            Assert.AreEqual("zone:T2", rows.Single(row => row.recordPath == "arenas[0].gate").zoneKey);
            Assert.AreEqual("zone:T2", rows.Single(row => row.recordPath == "runSplits[0]").zoneKey);
            Assert.AreEqual("zone:Globals", rows.Single(row => row.recordPath == "sky").zoneKey);
        }

        [Test]
        public void ChildAndRunSplitConflictingOverridesKeepInheritedZoneAndReportConflict()
        {
            definition.arenas[0].gateMeta.zoneIdOverride = "T4";
            definition.runSplits[0].meta.zoneIdOverride = "T4";

            var rows = LevelStudioBrowser.BuildHierarchy(definition, vocabulary);
            var gate = rows.Single(row => row.recordPath == "arenas[0].gate");
            var split = rows.Single(row => row.recordPath == "runSplits[0]");

            Assert.AreEqual("zone:T2", gate.zoneKey);
            Assert.AreEqual("zone:T2", split.zoneKey);
            StringAssert.Contains("conflicts", gate.diagnostic);
            StringAssert.Contains("conflicts", split.diagnostic);
        }

        [Test]
        public void MovedStableIdWithoutAnOverrideStaysUnassignedInsteadOfFallingBackToItsIdPrefix()
        {
            definition.platforms[0].meta.zoneIdOverride = "";
            definition.platforms[0].center = new Vector3(0, 0, 999);

            var row = LevelStudioBrowser.BuildHierarchy(definition, vocabulary).Single(x => x.recordPath == "platforms[0]");

            Assert.AreEqual("zone:Unassigned", row.zoneKey);
            StringAssert.Contains("No zone contains anchor", row.diagnostic);
        }

        [Test]
        public void AuthoredRouteMembershipUsesNamesAndListsMultiRouteObjectsOnceUnderSharedRoutes()
        {
            var rows = LevelStudioBrowser.BuildHierarchy(definition, vocabulary);
            var spawn = rows.Single(row => row.recordPath == "spawns[0]");
            var window = rows.Single(row => row.recordPath == "projectileSequences[0].engagementWindows[0]");

            Assert.AreEqual("route:T2:Shared Routes", spawn.routeKey);
            Assert.AreEqual("route:T2:Helix Volley", window.routeKey);
            foreach (string route in new[] { "Helix Volley", "Tower Shortcut", "Knight" })
                CollectionAssert.Contains(LevelStudioBrowser.Filter(rows, route).Select(row => row.id), "T2.Sentry.02");
        }

        [Test]
        public void ProjectileWindowSpawnerNameAlsoIndexesItsAuthoredRoute()
        {
            definition.projectileSequences[0].spawnerNames = new string[0];
            definition.projectileSequences[0].engagementWindows[0].spawnerName = "Spawn_T2";

            var rows = LevelStudioBrowser.BuildHierarchy(definition, vocabulary);

            CollectionAssert.Contains(LevelStudioBrowser.Filter(rows, "Helix Volley").Select(row => row.id), "T2.Sentry.02");
        }

        [Test]
        public void TypeOrderingIsExplicitInsteadOfEnumOrSourceOrder()
        {
            definition.platforms = new[] { new PlatformDef { meta = Meta("T2.Platform.01", "T2"), center = new Vector3(0, 0, 200) } };
            definition.ramps = new[] { new RampDef { meta = Meta("T2.Ramp.01", "T2"), basePosition = new Vector3(0, 0, 201) } };

            var types = LevelStudioBrowser.BuildHierarchy(definition, vocabulary)
                .Where(row => row.kind == LevelStudioRowKind.Type && row.routeKey == "route:T2:Shared")
                .Select(row => row.label).ToArray();

            CollectionAssert.AreEqual(types.OrderBy(x => LevelStudioBrowser.TypeOrder(x)).ThenBy(x => x, StringComparer.Ordinal), types);
        }

        [Test]
        public void MalformedVocabularyReportsDiagnosticButIdAndNameSearchStillWork()
        {
            var malformed = LevelStudioVocabulary.ParseEnemyTerms("not json");
            var rows = LevelStudioBrowser.BuildHierarchy(definition, malformed);

            Assert.IsNotEmpty(malformed.diagnostic);
            CollectionAssert.Contains(LevelStudioBrowser.Filter(rows, "T2.Sentry.02").Select(x => x.id), "T2.Sentry.02");
            CollectionAssert.Contains(LevelStudioBrowser.Filter(rows, "Blue sentry").Select(x => x.id), "T2.Sentry.02");
            Assert.IsFalse(rows.Any(row => row.label.IndexOf("Insight", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public void DraftRowsRepresentStateTruthfullyAndResumeOnlyTheNewestWritableManualDraft()
        {
            var validOld = new LevelDraftSummary
            {
                draftId = "valid-old", displayName = "Older Draft", sourceLevelId = "level_01",
                sourceState = LevelDraftSourceState.Changed, state = LevelDraftState.Valid, savedUtc = "2026-09-12T11:00:00Z",
                changedObjectCount = 1
            };
            var validNew = new LevelDraftSummary
            {
                draftId = "valid-new", displayName = "Newest Draft", sourceLevelId = "level_01",
                sourceState = LevelDraftSourceState.Unchanged, state = LevelDraftState.Valid, savedUtc = "2026-09-12T13:00:00Z",
                changedObjectCount = 4
            };
            var recoveryOnly = new LevelDraftSummary
            {
                draftId = "recovery-only", displayName = "Recovery Only", sourceLevelId = "level_01",
                sourceState = LevelDraftSourceState.Changed, state = LevelDraftState.RecoveryOnly,
                changedObjectCount = 4, autosavedUtc = "2026-09-12T12:00:00Z", diagnostic = "manual damaged",
                hasValidRecovery = true, recoveryPath = "recovery.json", recoveryRevision = 7
            };
            var readOnly = new LevelDraftSummary { draftId = "read-only", displayName = "Read Only", state = LevelDraftState.ReadOnly };
            var invalid = new LevelDraftSummary { draftId = "invalid", displayName = "Invalid", state = LevelDraftState.Invalid };

            var rows = LevelStudioBrowser.BuildLibrary(new LevelDefinition[0], new LevelDefinition[0], new[] { recoveryOnly, invalid, validOld, readOnly, validNew },
                id => id == recoveryOnly.draftId ? new[] { new LevelDraftRecoverySummary { path = "recovery.json", valid = true, revision = 7, autosavedUtc = recoveryOnly.autosavedUtc } } : new LevelDraftRecoverySummary[0]);

            var resumeLast = rows.Single(row => row.label == "Resume Last Draft");
            Assert.AreEqual(LevelStudioRowAction.Resume, resumeLast.action);
            Assert.AreEqual(validNew.draftId, resumeLast.draftId);
            Assert.AreEqual(LevelStudioRowAction.Resume, rows.Single(row => row.draftId == validOld.draftId && row.kind == LevelStudioRowKind.WorkingCopy).action);
            Assert.AreEqual(LevelStudioRowAction.None, rows.Single(row => row.draftId == recoveryOnly.draftId && row.kind == LevelStudioRowKind.WorkingCopy).action);
            Assert.AreEqual(LevelStudioRowAction.None, rows.Single(row => row.draftId == readOnly.draftId && row.kind == LevelStudioRowKind.WorkingCopy).action);
            Assert.AreEqual(LevelStudioRowAction.None, rows.Single(row => row.draftId == invalid.draftId && row.kind == LevelStudioRowKind.WorkingCopy).action);
            var recovery = rows.Single(row => row.kind == LevelStudioRowKind.Recovery);
            Assert.AreEqual("recovery-draft:" + recoveryOnly.draftId, recovery.parentKey);
            Assert.AreEqual(LevelStudioRowAction.RecoverLatest, recovery.action);
        }

        [Test]
        public void LibraryUsesRegistryOrderDedupesCampaignAndKeepsLegacyDisabled()
        {
            var later = ScriptableObject.CreateInstance<LevelDefinition>();
            later.levelId = "later";
            later.displayName = "Later";
            later.orderIndex = 9;
            definition.orderIndex = 2;
            var registry = ScriptableObject.CreateInstance<LevelRegistry>();
            registry.levels = new[] { later, definition, definition };

            var rows = LevelStudioBrowser.BuildLibrary(registry, new[] { definition }, new LevelDraftSummary[0], null, new[] { "old-level.json" });

            CollectionAssert.AreEqual(new[] { "test", "later" }, rows.Where(row => row.kind == LevelStudioRowKind.CampaignOriginal).Select(row => row.id));
            Assert.AreEqual(0, rows.Count(row => row.kind == LevelStudioRowKind.CustomLevel));
            var legacy = rows.Single(row => row.kind == LevelStudioRowKind.LegacyCandidate);
            Assert.AreEqual(LevelStudioRowAction.None, legacy.action);
            StringAssert.Contains("Migration required", legacy.diagnostic);
            UnityEngine.Object.DestroyImmediate(registry);
            UnityEngine.Object.DestroyImmediate(later);
        }

        [Test]
        public void LibraryRowsArePreorderWithTopLevelResumeAndRecoveryGrouping()
        {
            var custom = ScriptableObject.CreateInstance<LevelDefinition>();
            custom.levelId = "custom";
            custom.displayName = "Custom";
            var valid = new LevelDraftSummary { draftId = "valid", displayName = "Valid", state = LevelDraftState.Valid, savedUtc = "2026-09-12T13:00:00Z" };
            var recovery = new LevelDraftSummary { draftId = "recovery", displayName = "Recovery", state = LevelDraftState.RecoveryOnly };

            var rows = LevelStudioBrowser.BuildLibrary(new LevelDefinition[0], new[] { custom }, new[] { recovery, valid },
                id => id == "recovery" ? new[] { new LevelDraftRecoverySummary { path = "latest.json", valid = true, revision = 1 } } : new LevelDraftRecoverySummary[0],
                new[] { "legacy.json" });

            CollectionAssert.AreEqual(new[]
            {
                "resume-last", "category:Campaign Originals", "category:Working Copies", "draft:valid", "draft:recovery",
                "category:Custom Levels", "custom:custom", "legacy:legacy.json", "category:Recovery", "recovery-draft:recovery", "recovery:recovery:1"
            }, rows.Select(row => row.key));
            Assert.IsTrue(string.IsNullOrEmpty(rows[0].parentKey));
            Assert.AreEqual("category:Recovery", rows[9].parentKey);
            Assert.AreEqual(rows[9].key, rows[10].parentKey);
            UnityEngine.Object.DestroyImmediate(custom);
        }

        [Test]
        public void SelectionSurvivesFilteringAndReconcilesOnlyAgainstTheFullRefreshedModel()
        {
            var selection = new LevelStudioSelection();
            selection.Select("object:spawns[0]", false, false, hierarchy.Select(row => row.key));
            selection.Select("object:platforms[0]", true, false, hierarchy.Select(row => row.key));
            selection.Toggle("object:spawns[0]");
            selection.RequestFocus();

            CollectionAssert.AreEqual(new[] { "object:platforms[0]" }, selection.selectedKeys);
            Assert.AreEqual("object:platforms[0]", selection.ConsumeFocusIntent());
            Assert.AreEqual("object:platforms[0]", selection.activeKey);
            Assert.IsFalse(LevelStudioBrowser.Filter(hierarchy, "blue squid").Any(row => row.key == "object:platforms[0]"));
            CollectionAssert.AreEqual(new[] { "object:platforms[0]" }, selection.selectedKeys, "hidden selection must survive a query filter");
            selection.Reconcile(hierarchy.Select(row => row.key));
            CollectionAssert.AreEqual(new[] { "object:platforms[0]" }, selection.selectedKeys);
            selection.Reconcile(hierarchy.Where(row => row.key != "object:platforms[0]").Select(row => row.key));
            Assert.AreEqual(0, selection.selectedKeys.Count);
        }

        [Test]
        public void SelectionRangeAndClearUseDurableRowOrder()
        {
            var selection = new LevelStudioSelection();
            var order = new[] { "one", "two", "three" };
            selection.Select("one", false, false, order);
            selection.Select("three", false, true, order);
            CollectionAssert.AreEqual(order, selection.selectedKeys);
            selection.Clear();
            Assert.IsNull(selection.activeKey);
            Assert.AreEqual(0, selection.selectedKeys.Count);
        }

        static LevelDefinition Miniature()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.name = "TestLevel";
            level.levelId = "test";
            level.zones = new[]
            {
                Zone("T2", "Helix Tower", "Knight", new[] { "tower" }, 200),
                Zone("T4", "Warden Descent", "Warden", new[] { "last ramp" }, 400)
            };
            level.platforms = new[] { new PlatformDef { meta = Meta("T2.Platform.01", "T2"), name = "Landing", center = new Vector3(0, 0, 200) } };
            level.spawns = new[] { new SpawnDef { meta = Meta("T2.Sentry.02", "T2", "Blue sentry"), name = "Spawn_T2", prefabKey = "pshooter_enemy01", position = new Vector3(0, 0, 201) } };
            level.runSplits = new[] { new RunSplitDef { meta = Meta("T2.RunSplit.01"), name = "Knight", endSpawnerName = "Spawn_T2" } };
            level.arenas = new[] { new ArenaDef { meta = Meta("T2.Arena.01", "T2"), enabled = true, gateMeta = Meta("T2.Gate.01"), gateClosedPosition = new Vector3(0, 0, 202) } };
            level.projectileSequences = new[] { new ProjectileSequenceDef { meta = Meta("T2.ProjectileSequence.01", "T2"), name = "Helix Volley", spawnerNames = new[] { "Spawn_T2" }, progressOrigin = new Vector3(0, 0, 203), engagementWindows = new[] { new ProjectileEngagementWindowDef { meta = Meta("T2.ProjectileEngagementWindow.01"), routeStart = new Vector3(0, 0, 204), routeEnd = new Vector3(0, 0, 205) } } } };
            level.challengeRoutes = new[] { new ChallengeRouteDef { meta = Meta("T2.ChallengeRoute.01", "T2"), routeId = "Tower Shortcut", sourceSpawnerNames = new[] { "Spawn_T2" } } };
            level.sky = new SkyDef { meta = Meta("Level.Sky") };
            return level;
        }

        static ZoneDef Zone(string id, string name, string split, string[] aliases, float z)
        {
            return new ZoneDef { zoneId = id, canonicalName = name, splitName = split, aliases = aliases, order = id == "T2" ? 2 : 4,
                center = new Vector3(0, 0, z), size = new Vector3(100, 100, 100) };
        }

        static LevelObjectMeta Meta(string id, string zone = "", string friendly = "")
        {
            return new LevelObjectMeta { objectId = id, zoneIdOverride = zone, friendlyName = friendly };
        }
    }
}
