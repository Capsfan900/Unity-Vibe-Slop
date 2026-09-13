using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class LevelStudioValidationDiffTests
    {
        LevelDefinition definition;

        [SetUp]
        public void SetUp() { definition = CompleteDefinition(); }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(definition); }

        [Test]
        public void Validate_DuplicateAndMalformedStableIdsAreErrorsWithoutMutatingTheDraft()
        {
            definition.platforms[0].meta.objectId = "T0.Platform.01";
            definition.spawns[0].meta.objectId = "T0.Platform.01";
            definition.pickups[0].meta.objectId = "not an id";
            string before = EditorJsonUtility.ToJson(definition, true);

            var report = LevelStudioValidator.Validate(definition);

            CollectionAssert.Contains(report.errors.Select(issue => issue.code), "DuplicateObjectId");
            CollectionAssert.Contains(report.errors.Select(issue => issue.code), "MalformedObjectId");
            Assert.AreEqual(before, EditorJsonUtility.ToJson(definition, true),
                "validation is observation only: it must never assign metadata, normalize arrays, or dirty the draft");
        }

        [Test]
        public void Diff_MatchesByStableIdAndClassifiesDisabledNestedSolarMovement()
        {
            var before = Clone(definition);
            definition.arenas[0].solarRealm.enabled = false;
            definition.arenas[0].solarRealm.realmCenter = new Vector3(8f, 2f, 4f);

            var diff = LevelDraftDiff.Compare(before, definition);

            var change = diff.changes.Single(c => c.objectId == "T0.BossPortal.01");
            Assert.AreEqual(LevelDraftChangeKind.Transform, change.kind);
            Assert.AreEqual("arenas[0].solarRealm.realmCenter", change.fieldPath);
            Assert.AreEqual(1, diff.changes.Count, "a nested edit belongs to its nested stable ID exactly once");
            Object.DestroyImmediate(before);
        }

        [Test]
        public void Diff_GateMovementBelongsOnlyToTheGateRecord()
        {
            var before = Clone(definition);
            definition.arenas[0].gateClosedPosition = Vector3.up;

            var diff = LevelDraftDiff.Compare(before, definition);

            Assert.AreEqual(1, diff.changes.Count);
            Assert.AreEqual("T0.Gate.01", diff.changes[0].objectId);
            Assert.AreEqual(LevelDraftChangeKind.Transform, diff.changes[0].kind);
            Object.DestroyImmediate(before);
        }

        [Test]
        public void Diff_ReorderingStableRecordsProducesOneCollectionOrderChangeNotTransforms()
        {
            var before = Clone(definition);
            definition.platforms = definition.platforms.Reverse().ToArray();

            var diff = LevelDraftDiff.Compare(before, definition);

            Assert.AreEqual(1, diff.changes.Count);
            Assert.AreEqual(LevelDraftChangeKind.Tuning, diff.changes[0].kind);
            Assert.AreEqual("$level", diff.changes[0].objectId);
            Assert.AreEqual("platforms.order", diff.changes[0].fieldPath);
            Object.DestroyImmediate(before);
        }

        [Test]
        public void Diff_InvalidInventoriesReturnValidationWithoutThrowingOrChanges()
        {
            var before = Clone(definition);
            definition.zones = new[] { definition.zones[0], definition.zones[0] };
            definition.platforms = new PlatformDef[] { null };

            LevelDraftDiffReport diff = null;
            Assert.DoesNotThrow(() => diff = LevelDraftDiff.Compare(before, definition));
            Assert.IsTrue(diff.failed);
            Assert.IsEmpty(diff.changes);
            CollectionAssert.IsSubsetOf(new[] { "DuplicateZoneId", "NullEntry" }, diff.afterValidation.errors.Select(x => x.code));
            Object.DestroyImmediate(before);
        }

        [Test]
        public void Diff_ChangesCannotBeMutatedThroughThePublicReport()
        {
            var before = Clone(definition);
            definition.platforms[0].center = Vector3.one;

            var diff = LevelDraftDiff.Compare(before, definition);

            Assert.IsFalse(diff.changes is System.Collections.Generic.List<LevelDraftChange>);
            Object.DestroyImmediate(before);
        }

        [Test]
        public void Diff_FriendlyNameIsTuning()
        {
            var before = Clone(definition);
            definition.platforms[0].meta.friendlyName = "Main Deck";

            var diff = LevelDraftDiff.Compare(before, definition);

            Assert.AreEqual(LevelDraftChangeKind.Tuning, diff.changes.Single().kind);
            Object.DestroyImmediate(before);
        }

        [Test]
        public void Diff_MovingAcrossZoneBoundsIncludesOneZoneChange()
        {
            definition.zones = new[]
            {
                definition.zones[0],
                new ZoneDef { zoneId = "T1", center = new Vector3(200f, 0f, 0f), size = new Vector3(100f, 100f, 100f) }
            };
            definition.platforms[0].meta.zoneIdOverride = "";
            var before = Clone(definition);
            definition.platforms[0].center = new Vector3(200f, 0f, 0f);

            var diff = LevelDraftDiff.Compare(before, definition);

            Assert.AreEqual(1, diff.changes.Count(x => x.objectId == "T0.Platform.01" && x.kind == LevelDraftChangeKind.Zone));
            Assert.AreEqual(1, diff.changes.Count(x => x.objectId == "T0.Platform.01" && x.kind == LevelDraftChangeKind.Transform));
            Object.DestroyImmediate(before);
        }

        [Test]
        public void Validate_ReportsNullAuthoredEntriesAndRequiredSingletons()
        {
            definition.platforms = new PlatformDef[] { null };
            definition.killZone = null;
            definition.sky = null;

            var report = LevelStudioValidator.Validate(definition);

            CollectionAssert.Contains(report.errors.Select(x => x.code), "NullEntry");
            Assert.AreEqual(2, report.errors.Count(x => x.code == "MissingRequiredRecord"));
        }

        [Test]
        public void Validate_RejectsAStableIdWhoseKindDoesNotMatchItsRecord()
        {
            definition.platforms[0].meta.objectId = "T0.Spawn.01";

            var report = LevelStudioValidator.Validate(definition);

            CollectionAssert.Contains(report.errors.Select(x => x.code), "MalformedObjectId");
        }

        [Test]
        public void Validate_DelegatesRunScoringAndChecksCampaignCheckpointNames()
        {
            definition.requiredRegularKills = 2;
            definition.checkpoints = new[]
            {
                definition.checkpoints[0],
                new CheckpointDef { meta = Meta("T0.Checkpoint.02"), name = "Checkpoint", position = Vector3.right }
            };

            var report = LevelStudioValidator.Validate(definition);

            CollectionAssert.Contains(report.errors.Select(x => x.code), "InvalidRunContract");
            CollectionAssert.Contains(report.errors.Select(x => x.code), "DuplicateCheckpointName");
        }

        [Test]
        public void Validate_ResourceResolverChecksSpawnItemAndMaterialKeys()
        {
            var report = LevelStudioValidator.Validate(definition, new MissingResources());

            CollectionAssert.Contains(report.errors.Select(x => x.code), "MissingSpawnResource");
            CollectionAssert.Contains(report.errors.Select(x => x.code), "MissingItemResource");
            CollectionAssert.Contains(report.errors.Select(x => x.code), "MissingMaterialResource");
        }

        [Test]
        public void Validate_DisabledNestedAndPresentationRecordsDoNotRequirePhysicalZoneOwnership()
        {
            definition.arenas[0].meta.zoneIdOverride = "";
            definition.arenas[0].gateMeta.zoneIdOverride = "";
            definition.arenas[0].exitGateMeta.zoneIdOverride = "";
            definition.arenas[0].solarRealm.meta.zoneIdOverride = "";
            definition.worldLeaderboard.meta.zoneIdOverride = "";
            definition.worldLeaderboard.position = new Vector3(500f, 0f, 0f);

            var report = LevelStudioValidator.Validate(definition);

            Assert.IsFalse(report.errors.Any(x => x.code == "InvalidZoneOwnership"));
        }

        [Test]
        public void Diff_UsesExplicitRootAndZonePseudoIds()
        {
            var before = Clone(definition);
            definition.requiredRunSouls = 9;
            definition.zones[0].size = new Vector3(80f, 100f, 100f);

            var diff = LevelDraftDiff.Compare(before, definition);

            CollectionAssert.Contains(diff.changes.Select(x => x.objectId), "$level");
            Assert.IsTrue(diff.changes.Any(x => x.objectId == "$zone/T0" && x.kind == LevelDraftChangeKind.Zone));
            Object.DestroyImmediate(before);
        }

        [TestCase("root", "$level", LevelDraftChangeKind.Tuning)]
        [TestCase("player", "T0.PlayerStart", LevelDraftChangeKind.Transform)]
        [TestCase("platform", "T0.Platform.01", LevelDraftChangeKind.Transform)]
        [TestCase("ramp", "T0.Ramp.01", LevelDraftChangeKind.Transform)]
        [TestCase("spawn", "T0.Sentry.01", LevelDraftChangeKind.Transform)]
        [TestCase("pickup", "T0.Pickup.01", LevelDraftChangeKind.Transform)]
        [TestCase("checkpoint", "T0.Checkpoint.01", LevelDraftChangeKind.Transform)]
        [TestCase("torch", "T0.Torch.01", LevelDraftChangeKind.Transform)]
        [TestCase("pedestal", "T0.Pedestal.01", LevelDraftChangeKind.Transform)]
        [TestCase("balloon", "T0.Balloon.01", LevelDraftChangeKind.Transform)]
        [TestCase("water", "T0.Water.01", LevelDraftChangeKind.Transform)]
        [TestCase("arena", "T0.Arena.01", LevelDraftChangeKind.Transform)]
        [TestCase("gate", "T0.Gate.01", LevelDraftChangeKind.Transform)]
        [TestCase("exit", "T0.ExitGate.01", LevelDraftChangeKind.Transform)]
        [TestCase("solar", "T0.BossPortal.01", LevelDraftChangeKind.Transform)]
        [TestCase("sequence", "T0.ProjectileSequence.01", LevelDraftChangeKind.Tuning)]
        [TestCase("window", "T0.ProjectileEngagementWindow.01", LevelDraftChangeKind.Transform)]
        [TestCase("challenge", "T0.ChallengeRoute.01", LevelDraftChangeKind.Transform)]
        [TestCase("split", "T0.RunSplit.01", LevelDraftChangeKind.Tuning)]
        [TestCase("leaderboard", "T0.WorldLeaderboard", LevelDraftChangeKind.Transform)]
        [TestCase("sky", "Level.Sky", LevelDraftChangeKind.Tuning)]
        [TestCase("kill", "Level.KillZone", LevelDraftChangeKind.Transform)]
        public void Diff_EachAuthoredOwnerClassifiesOnce(string category, string objectId, LevelDraftChangeKind kind)
        {
            var before = Clone(definition);
            Mutate(category, definition);

            var diff = LevelDraftDiff.Compare(before, definition);

            Assert.AreEqual(1, diff.changes.Count, category);
            Assert.AreEqual(objectId, diff.changes[0].objectId, category);
            Assert.AreEqual(kind, diff.changes[0].kind, category);
            Object.DestroyImmediate(before);
        }

        [Test]
        public void Diff_UnchangedIsEmptyAndRepeatedComparisonIsDeterministic()
        {
            var before = Clone(definition);
            Assert.IsEmpty(LevelDraftDiff.Compare(before, definition).changes);
            definition.platforms[0].center = Vector3.up;

            var first = LevelDraftDiff.Compare(before, definition).changes.Select(ChangeKey).ToArray();
            var second = LevelDraftDiff.Compare(before, definition).changes.Select(ChangeKey).ToArray();

            CollectionAssert.AreEqual(first, second);
            Object.DestroyImmediate(before);
        }

        [Test]
        public void Diff_AddAndRemoveUseStableMembership()
        {
            var beforeAdd = Clone(definition);
            definition.platforms = definition.platforms.Concat(new[] { new PlatformDef { meta = Meta("T0.Platform.03"), center = Vector3.left } }).ToArray();
            Assert.AreEqual(LevelDraftChangeKind.Add, LevelDraftDiff.Compare(beforeAdd, definition).changes.Single(x => x.objectId == "T0.Platform.03").kind);
            Object.DestroyImmediate(beforeAdd);

            var beforeRemove = Clone(definition);
            definition.platforms = definition.platforms.Where(x => x.meta.objectId != "T0.Platform.03").ToArray();
            Assert.AreEqual(LevelDraftChangeKind.Remove, LevelDraftDiff.Compare(beforeRemove, definition).changes.Single(x => x.objectId == "T0.Platform.03").kind);
            Object.DestroyImmediate(beforeRemove);
        }

        static LevelDefinition Clone(LevelDefinition source)
        {
            var clone = ScriptableObject.CreateInstance<LevelDefinition>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), clone);
            return clone;
        }

        static LevelDefinition CompleteDefinition()
        {
            var value = ScriptableObject.CreateInstance<LevelDefinition>();
            value.levelId = "validation_diff";
            value.displayName = "Validation Diff";
            value.sceneName = "ValidationDiff";
            value.playerStartMeta = Meta("T0.PlayerStart");
            value.zones = new[] { new ZoneDef { zoneId = "T0", splitName = "Split", center = Vector3.zero, size = new Vector3(100f, 100f, 100f) } };
            value.platforms = new[]
            {
                new PlatformDef { meta = Meta("T0.Platform.01"), name = "Deck A", center = Vector3.zero },
                new PlatformDef { meta = Meta("T0.Platform.02"), name = "Deck B", center = Vector3.right }
            };
            value.spawns = new[] { new SpawnDef { meta = Meta("T0.Sentry.01"), name = "Spawn", prefabKey = "pshooter_enemy01", position = Vector3.zero } };
            value.pickups = new[] { new PickupDef { meta = Meta("T0.Pickup.01"), name = "Pickup", position = Vector3.zero } };
            value.checkpoints = new[] { new CheckpointDef { meta = Meta("T0.Checkpoint.01"), name = "Checkpoint", position = Vector3.zero } };
            value.ramps = new[] { new RampDef { meta = Meta("T0.Ramp.01"), basePosition = Vector3.zero } };
            value.torches = new[] { new TorchDef { meta = Meta("T0.Torch.01"), basePosition = Vector3.zero } };
            value.pedestals = new[] { new PedestalDef { meta = Meta("T0.Pedestal.01"), groundPosition = Vector3.zero } };
            value.balloons = new[] { new BalloonDef { meta = Meta("T0.Balloon.01"), position = Vector3.zero } };
            value.waters = new[] { new WaterDef { meta = Meta("T0.Water.01"), center = Vector3.zero } };
            value.arenas = new[]
            {
                new ArenaDef
                {
                    meta = Meta("T0.Arena.01"), gateMeta = Meta("T0.Gate.01"), exitGateMeta = Meta("T0.ExitGate.01"),
                    enabled = false, solarRealm = new SolarRealmDef { meta = Meta("T0.BossPortal.01"), enabled = false }
                }
            };
            value.killZone = new KillZoneDef { meta = Meta("Level.KillZone"), size = new Vector3(100f, 2f, 100f) };
            value.sky = new SkyDef { meta = Meta("Level.Sky") };
            value.worldLeaderboard = new WorldLeaderboardDef { meta = Meta("T0.WorldLeaderboard"), enabled = false };
            value.projectileSequences = new[]
            {
                new ProjectileSequenceDef
                {
                    meta = Meta("T0.ProjectileSequence.01"), spawnerNames = new[] { "Spawn" },
                    engagementWindows = new[]
                    {
                        new ProjectileEngagementWindowDef { meta = Meta("T0.ProjectileEngagementWindow.01"), spawnerName = "Spawn", routeEnd = Vector3.forward * 10f }
                    }
                }
            };
            value.challengeRoutes = new[] { new ChallengeRouteDef { meta = Meta("T0.ChallengeRoute.01"), routeId = "Route", sourceSpawnerNames = new[] { "Spawn" } } };
            value.runSplits = new[] { new RunSplitDef { meta = Meta("T0.RunSplit.01"), name = "Split", endSpawnerName = "Spawn" } };
            return value;
        }

        static void Mutate(string category, LevelDefinition value)
        {
            if (category == "root") value.parTime++;
            else if (category == "player") value.playerStart += Vector3.up;
            else if (category == "platform") value.platforms[0].center += Vector3.up;
            else if (category == "ramp") value.ramps[0].basePosition += Vector3.up;
            else if (category == "spawn") value.spawns[0].position += Vector3.up;
            else if (category == "pickup") value.pickups[0].position += Vector3.up;
            else if (category == "checkpoint") value.checkpoints[0].position += Vector3.up;
            else if (category == "torch") value.torches[0].basePosition += Vector3.up;
            else if (category == "pedestal") value.pedestals[0].groundPosition += Vector3.up;
            else if (category == "balloon") value.balloons[0].position += Vector3.up;
            else if (category == "water") value.waters[0].center += Vector3.up;
            else if (category == "arena") value.arenas[0].triggerPosition += Vector3.up;
            else if (category == "gate") value.arenas[0].gateClosedPosition += Vector3.up;
            else if (category == "exit") value.arenas[0].exitGateClosedPosition += Vector3.up;
            else if (category == "solar") value.arenas[0].solarRealm.realmCenter += Vector3.up;
            else if (category == "sequence") value.projectileSequences[0].recoveryGap += 0.1f;
            else if (category == "window") value.projectileSequences[0].engagementWindows[0].routeEnd += Vector3.forward;
            else if (category == "challenge") value.challengeRoutes[0].entryCenter += Vector3.up;
            else if (category == "split") value.runSplits[0].aSeconds += 0.1f;
            else if (category == "leaderboard") value.worldLeaderboard.position += Vector3.up;
            else if (category == "sky") value.sky.eclipseYawDeg++;
            else value.killZone.center += Vector3.up;
        }

        static string ChangeKey(LevelDraftChange change) { return change.zoneId + "|" + change.objectId + "|" + change.fieldPath + "|" + change.kind; }

        static LevelObjectMeta Meta(string id) { return new LevelObjectMeta { objectId = id, zoneIdOverride = "T0" }; }

        sealed class MissingResources : ILevelStudioResourceResolver
        {
            public bool HasPrefab(string key) { return false; }
            public bool HasEnemyData(string key) { return false; }
            public bool HasItem(string key) { return false; }
            public bool HasMaterial(string key) { return false; }
            public bool IsProjectileCapable(string prefabKey) { return false; }
        }
    }
}
