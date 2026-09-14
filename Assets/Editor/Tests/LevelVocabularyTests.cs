using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class LevelVocabularyTests
    {
        LevelDefinition level;

        [SetUp] public void SetUp()
        {
            level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.zones = new[] {
                new ZoneDef { zoneId = "T0", canonicalName = "Opening", splitName = "Opening",
                    center = Vector3.zero, size = Vector3.one * 10 },
                new ZoneDef { zoneId = "T1", canonicalName = "Causeway", splitName = "Ninja",
                    center = Vector3.right * 20, size = Vector3.one * 10 }
            };
        }

        [TearDown] public void TearDown() { Object.DestroyImmediate(level); }

        [Test] public void ZoneAssignment_UsesAnchorRatherThanObjectExtents()
        {
            level.platforms = new[] { new PlatformDef { name = "Deck", center = Vector3.zero,
                size = Vector3.one * 100 } };
            var report = LevelObjectCatalog.AssignZones(level);
            Assert.AreEqual("T0.Platform.01", level.platforms[0].meta.objectId);
            Assert.IsEmpty(report.errors);
        }

        [Test] public void ZoneAssignment_RejectsOverlapWithoutAllocatingId()
        {
            level.zones[1].center = Vector3.zero;
            level.platforms = new[] { new PlatformDef() };
            var report = LevelObjectCatalog.AssignZones(level);
            Assert.That(report.errors.Any(e => e.Contains("multiple zones")));
            Assert.IsEmpty(level.platforms[0].meta.objectId);
        }

        [Test] public void ZoneAssignment_RejectsOrphanWithoutAllocatingId()
        {
            level.platforms = new[] { new PlatformDef { center = Vector3.up * 100 } };
            var report = LevelObjectCatalog.AssignZones(level);
            Assert.That(report.errors.Any(e => e.Contains("no zone")));
            Assert.IsEmpty(level.platforms[0].meta.objectId);
        }

        [Test] public void ZoneAssignment_BoundaryIsInclusiveAndWarned()
        {
            level.platforms = new[] { new PlatformDef { center = Vector3.right * 5 } };
            var report = LevelObjectCatalog.AssignZones(level);
            Assert.IsEmpty(report.errors);
            Assert.That(report.warnings.Any(e => e.Contains("boundary")));
            Assert.AreEqual("T0.Platform.01", level.platforms[0].meta.objectId);
        }

        [Test] public void ZoneAssignment_SharedBoundaryIsAmbiguous()
        {
            level.zones[1].center = Vector3.right * 10;
            level.platforms = new[] { new PlatformDef { center = Vector3.right * 5 } };
            Assert.That(LevelObjectCatalog.AssignZones(level).errors.Any(e => e.Contains("multiple zones")));
        }

        [Test] public void ZoneAssignment_ValidOverrideResolvesOverlapAndOrphan()
        {
            level.zones[1].center = Vector3.zero;
            level.playerStartMeta.zoneIdOverride = "T0";
            level.platforms = new[] {
                new PlatformDef { meta = new LevelObjectMeta { zoneIdOverride = "T1" } },
                new PlatformDef { center = Vector3.up * 100,
                    meta = new LevelObjectMeta { zoneIdOverride = "T1" } }
            };
            var report = LevelObjectCatalog.AssignZones(level);
            Assert.IsEmpty(report.errors);
            Assert.AreEqual("T1.Platform.01", level.platforms[0].meta.objectId);
            Assert.AreEqual("T1.Platform.02", level.platforms[1].meta.objectId);
            Assert.That(report.warnings.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test] public void ZoneAssignment_InvalidOverrideDoesNotFallBackToContainment()
        {
            level.platforms = new[] { new PlatformDef { meta = new LevelObjectMeta { zoneIdOverride = "Missing" } } };
            var report = LevelObjectCatalog.AssignZones(level);
            Assert.That(report.errors.Any(e => e.Contains("override")));
            Assert.IsEmpty(level.platforms[0].meta.objectId);
        }

        [Test] public void ZoneAssignment_PreservesIdsAcrossMovesRenamesReorderingAndRepeatedAssignment()
        {
            var first = new PlatformDef();
            var second = new PlatformDef();
            level.platforms = new[] { first, second };
            LevelObjectCatalog.AssignZones(level);
            first.center = Vector3.right * 20;
            first.name = "Renamed";
            first.meta.friendlyName = "Editable label";
            level.platforms = new[] { second, first };
            level.zones = level.zones.Reverse().ToArray();
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            Assert.AreEqual("T0.Platform.01", first.meta.objectId);
            Assert.AreEqual("T0.Platform.02", second.meta.objectId);
            Assert.AreEqual("Editable label", first.meta.friendlyName);
        }

        [Test] public void ZoneAssignment_ReservesExistingIdsBeforeAllocatingFirstUnusedSuffix()
        {
            level.platforms = new[] {
                new PlatformDef(),
                new PlatformDef { meta = new LevelObjectMeta { objectId = "T0.Platform.01" } },
                new PlatformDef { meta = new LevelObjectMeta { objectId = "T0.Platform.03" } },
                new PlatformDef()
            };
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            Assert.AreEqual("T0.Platform.02", level.platforms[0].meta.objectId);
            Assert.AreEqual("T0.Platform.04", level.platforms[3].meta.objectId);
        }

        [Test] public void ZoneAssignment_RejectsDuplicateIdsWithoutRewritingThem()
        {
            level.platforms = new[] {
                new PlatformDef { meta = new LevelObjectMeta { objectId = "T0.Platform.L8" } },
                new PlatformDef { meta = new LevelObjectMeta { objectId = "T0.Platform.L8" } }
            };
            var report = LevelObjectCatalog.AssignZones(level);
            Assert.That(report.errors.Any(e => e.Contains("Duplicate object ID")));
            Assert.AreEqual("T0.Platform.L8", level.platforms[1].meta.objectId);
        }

        [TestCase("bad id")]
        [TestCase("T0.Platform.")]
        [TestCase("Level.Sky")]
        public void ZoneAssignment_RejectsMalformedOrReservedRepeatableIds(string id)
        {
            level.platforms = new[] { new PlatformDef { meta = new LevelObjectMeta { objectId = id } } };
            Assert.IsNotEmpty(LevelObjectCatalog.AssignZones(level).errors);
            Assert.AreEqual(id, level.platforms[0].meta.objectId);
        }

        [Test] public void ZoneAssignment_RejectsDuplicateZoneIdsEvenWithOverride()
        {
            level.zones[1].zoneId = "T0";
            level.platforms = new[] { new PlatformDef { meta = new LevelObjectMeta { zoneIdOverride = "T0" } } };
            var report = LevelObjectCatalog.AssignZones(level);
            Assert.That(report.errors.Any(e => e.Contains("Duplicate zone ID")));
            Assert.IsEmpty(level.platforms[0].meta.objectId);
        }

        [Test] public void ZoneAssignment_RejectsInvalidZoneBoundsAndIds()
        {
            level.zones[0].size = new Vector3(-1, 10, 10);
            level.zones[1].zoneId = "bad.id";
            Assert.That(LevelObjectCatalog.AssignZones(level).errors.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test] public void Catalog_UsesEveryAuthoringAnchorAndOriginalArrayIndex()
        {
            level.platforms = new[] { null, new PlatformDef { center = new Vector3(1, 2, 3) } };
            level.ramps = new[] { new RampDef { basePosition = new Vector3(2, 3, 4) } };
            level.spawns = new[] { new SpawnDef { name = "End", position = new Vector3(3, 4, 5) } };
            level.pickups = new[] { new PickupDef { position = new Vector3(4, 5, 6) } };
            level.checkpoints = new[] { new CheckpointDef { position = new Vector3(5, 6, 7) } };
            level.torches = new[] { new TorchDef { basePosition = new Vector3(6, 7, 8) } };
            level.pedestals = new[] { new PedestalDef { groundPosition = new Vector3(7, 8, 9) } };
            level.balloons = new[] { new BalloonDef { position = new Vector3(8, 9, 10) } };
            level.waters = new[] { new WaterDef { center = new Vector3(9, 10, 11) } };
            level.arenas = new[] { new ArenaDef { triggerPosition = new Vector3(10, 11, 12) } };
            level.projectileSequences = new[] { new ProjectileSequenceDef { progressOrigin = new Vector3(11, 12, 13) } };
            level.challengeRoutes = new[] { new ChallengeRouteDef { entryCenter = new Vector3(12, 13, 14) } };
            level.runSplits = new[] { new RunSplitDef { endSpawnerName = "End" } };
            var records = LevelObjectCatalog.Enumerate(level).ToArray();
            var kinds = new[] { LevelObjectKind.Platform, LevelObjectKind.Ramp, LevelObjectKind.Spawn,
                LevelObjectKind.Pickup, LevelObjectKind.Checkpoint, LevelObjectKind.Torch,
                LevelObjectKind.Pedestal, LevelObjectKind.Balloon, LevelObjectKind.Water,
                LevelObjectKind.Arena, LevelObjectKind.ProjectileSequence, LevelObjectKind.ChallengeRoute };
            for (int i = 0; i < kinds.Length; i++)
            {
                var record = records.Single(r => r.kind == kinds[i]);
                Assert.AreEqual(new Vector3(i + 1, i + 2, i + 3), record.anchor, kinds[i].ToString());
                Assert.AreEqual(i == 0 ? 1 : 0, record.index);
                Assert.IsNotNull(record.meta);
            }
            Assert.AreSame(level.platforms[1], records.Single(r => r.kind == LevelObjectKind.Platform).data);
            Assert.AreEqual(new Vector3(3, 4, 5), records.Single(r => r.kind == LevelObjectKind.RunSplit).anchor);
            Assert.AreEqual(17, records.Length);
        }

        [Test] public void Catalog_SingletonsUseSpatialOrGlobalIdsAndPreserveMetadata()
        {
            level.worldLeaderboard.enabled = true;
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            var records = LevelObjectCatalog.Enumerate(level).ToArray();
            CollectionAssert.AreEquivalent(new[] { "T0.PlayerStart", "Level.KillZone", "Level.Sky", "T0.WorldLeaderboard" },
                records.Select(r => r.meta.objectId));
            var start = records.Single(r => r.kind == LevelObjectKind.PlayerStart);
            start.meta.friendlyName = "Entry";
            Assert.AreEqual("Entry", LevelObjectCatalog.Enumerate(level).Single(r => r.kind == LevelObjectKind.PlayerStart).meta.friendlyName);
            Assert.AreEqual(level.playerStart, start.anchor);
        }

        [Test] public void ZoneAssignment_SpatialSingletonsRequireZonesButGlobalAndDisabledOnesDoNot()
        {
            level.playerStart = Vector3.up * 100;
            level.worldLeaderboard.position = Vector3.up * 100;
            var report = LevelObjectCatalog.AssignZones(level);
            Assert.AreEqual(1, report.errors.Count);
            Assert.That(report.errors[0], Does.Contain("PlayerStart"));
            level.worldLeaderboard.enabled = true;
            Assert.AreEqual(2, LevelObjectCatalog.AssignZones(level).errors.Count);
        }

        [Test] public void Catalog_ToleratesLegacyNullArraysAndHydratesMissingMetadata()
        {
            level.platforms = new[] { new PlatformDef { meta = null } };
            level.ramps = null;
            level.spawns = null;
            level.runSplits = null;
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            Assert.AreEqual("T0.Platform.01", level.platforms[0].meta.objectId);
        }

        [Test] public void ZoneAssignment_ReportsMissingSplitAnchorInsteadOfAssigningAtOrigin()
        {
            level.runSplits = new[] { new RunSplitDef { endSpawnerName = "Missing" } };
            Assert.IsNotEmpty(LevelObjectCatalog.AssignZones(level).errors);
            Assert.IsEmpty(level.runSplits[0].meta.objectId);
        }

        [Test] public void ZoneAssignment_SplitFollowsEndSpawnerOverrideInsteadOfItsAnchorZone()
        {
            level.spawns = new[] { new SpawnDef { name = "End", prefabKey = "pshooter_enemy01",
                meta = new LevelObjectMeta { zoneIdOverride = "T1" } } };
            level.runSplits = new[] { new RunSplitDef { name = "Ninja", endSpawnerName = "End" } };
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            Assert.AreEqual("T1.Sentry.01", level.spawns[0].meta.objectId);
            Assert.AreEqual("T1.RunSplit.01", level.runSplits[0].meta.objectId);
        }

        [Test] public void ZoneAssignment_SpatialSingletonIdsStayStableWhenMovedToAnotherZone()
        {
            level.worldLeaderboard.enabled = true;
            LevelObjectCatalog.AssignZones(level);
            level.playerStart = Vector3.right * 20;
            level.worldLeaderboard.position = Vector3.right * 20;
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            Assert.AreEqual("T0.PlayerStart", level.playerStartMeta.objectId);
            Assert.AreEqual("T0.WorldLeaderboard", level.worldLeaderboard.meta.objectId);
        }

        [Test] public void Catalog_NestedEntitiesHaveStableIdentityOwnerPathAndAllPositionHandles()
        {
            var arena = new ArenaDef { enabled = true, hasExitGate = true,
                gateOpenPosition = Vector3.right * 100, gateClosedPosition = Vector3.up * 100,
                exitGateOpenPosition = Vector3.forward * 100, exitGateClosedPosition = Vector3.one * 100,
                solarRealm = new SolarRealmDef { enabled = true, realmCenter = Vector3.right * 700 } };
            var window = new ProjectileEngagementWindowDef { routeStart = Vector3.right * 500,
                routeEnd = Vector3.right * 600 };
            level.arenas = new[] { arena };
            level.projectileSequences = new[] { new ProjectileSequenceDef { engagementWindows = new[] { null, window } } };
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            var records = LevelObjectCatalog.Enumerate(level).ToArray();
            var gate = records.Single(r => r.kind == LevelObjectKind.Gate);
            Assert.AreSame(arena, gate.data);
            Assert.AreSame(arena.gateMeta, gate.meta);
            Assert.AreEqual("arenas[0]", gate.owner.path);
            Assert.AreEqual("arenas[0].gate", gate.path);
            CollectionAssert.AreEquivalent(new[] { "arenas[0].gateOpenPosition", "arenas[0].gateClosedPosition" }, gate.anchors.Select(a => a.path));
            CollectionAssert.AreEquivalent(new[] { Vector3.right * 100, Vector3.up * 100 }, gate.anchors.Select(a => a.position));
            var exit = records.Single(r => r.kind == LevelObjectKind.ExitGate);
            Assert.AreSame(arena.exitGateMeta, exit.meta);
            Assert.AreEqual(2, exit.anchors.Length);
            var portal = records.Single(r => r.kind == LevelObjectKind.BossPortal);
            Assert.AreSame(arena.solarRealm, portal.data);
            Assert.AreSame(arena.solarRealm.meta, portal.meta);
            Assert.AreEqual("arenas[0].solarRealm", portal.path);
            Assert.That(portal.anchors.Select(a => a.path), Does.Contain("arenas[0].solarRealm.realmCenter"));
            Assert.AreEqual(8, portal.anchors.Length);
            var encounter = records.Single(r => r.kind == LevelObjectKind.ProjectileEngagementWindow);
            Assert.AreSame(window, encounter.data);
            Assert.AreSame(window.meta, encounter.meta);
            Assert.AreEqual(1, encounter.index);
            Assert.AreEqual("projectileSequences[0]", encounter.owner.path);
            Assert.AreEqual("projectileSequences[0].engagementWindows[1]", encounter.path);
            Assert.AreEqual(2, encounter.anchors.Length);
            Assert.AreEqual("T0.Gate.01", gate.meta.objectId);
            Assert.AreEqual("T0.ExitGate.01", exit.meta.objectId);
            Assert.AreEqual("T0.BossPortal.01", portal.meta.objectId);
            Assert.AreEqual("T0.ProjectileEngagementWindow.01", encounter.meta.objectId);
        }

        [Test] public void Catalog_DisabledOptionalChildrenAreAbsentAndRemainUnassigned()
        {
            var arena = new ArenaDef { enabled = true };
            level.arenas = new[] { arena };
            var records = LevelObjectCatalog.Enumerate(level).ToArray();
            Assert.That(records.Any(r => r.kind == LevelObjectKind.Gate));
            Assert.That(records.All(r => r.kind != LevelObjectKind.ExitGate && r.kind != LevelObjectKind.BossPortal));
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            Assert.IsEmpty(arena.exitGateMeta.objectId);
            Assert.IsEmpty(arena.solarRealm.meta.objectId);
        }

        [TestCase("pshooter_enemy01", "Sentry")]
        [TestCase("pshooter_enemy02", "HeavySentry")]
        [TestCase("pshooter_enemy03", "SurgeTurret")]
        [TestCase("Legendary_Ninja", "ThirteenthShade")]
        [TestCase("Legendary_Knight", "IronPenitent")]
        [TestCase("Legendary_Spellsword", "AshenChorister")]
        [TestCase("Boss", "HollowWarden")]
        public void ZoneAssignment_SpawnFamilyComesFromPrefabKey(string prefabKey, string family)
        {
            level.spawns = new[] { new SpawnDef { prefabKey = prefabKey, name = "Misleading display label" } };
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            Assert.AreEqual("T0." + family + ".01", level.spawns[0].meta.objectId);
        }

        [Test] public void ZoneAssignment_UnknownEnemyKeyIsAnErrorAndCannotReceiveId()
        {
            level.spawns = new[] { new SpawnDef { prefabKey = "UnknownEnemy" } };
            Assert.That(LevelObjectCatalog.AssignZones(level).errors.Any(e => e.Contains("Unknown enemy prefab key")));
            Assert.IsEmpty(level.spawns[0].meta.objectId);
        }

        [Test] public void ZoneAssignment_RejectsIdsFromAnotherObjectTypeOrEnemyFamily()
        {
            level.platforms = new[] { new PlatformDef { meta = new LevelObjectMeta { objectId = "T0.Sentry.01" } } };
            level.spawns = new[] { new SpawnDef { prefabKey = "pshooter_enemy01",
                meta = new LevelObjectMeta { objectId = "T0.HeavySentry.01" } } };
            Assert.AreEqual(2, LevelObjectCatalog.AssignZones(level).errors.Count);
        }

        [Test] public void ZoneAssignment_EnemyFamiliesHaveIndependentSuffixSpaces()
        {
            level.spawns = new[] {
                new SpawnDef { prefabKey = "pshooter_enemy01" },
                new SpawnDef { prefabKey = "pshooter_enemy02" },
                new SpawnDef { prefabKey = "pshooter_enemy01" }
            };
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            CollectionAssert.AreEqual(new[] { "T0.Sentry.01", "T0.HeavySentry.01", "T0.Sentry.02" }, level.spawns.Select(s => s.meta.objectId));
        }

        [Test] public void ZoneAssignment_RejectsSplitNameMismatchAndWarnsForMatchingOverride()
        {
            level.spawns = new[] { new SpawnDef { name = "End" } };
            var split = new RunSplitDef { name = "Ninja", endSpawnerName = "End",
                meta = new LevelObjectMeta { zoneIdOverride = "T0" } };
            level.runSplits = new[] { split };
            Assert.That(LevelObjectCatalog.AssignZones(level).errors.Any(e => e.Contains("splitName")));
            Assert.IsEmpty(split.meta.objectId);
            level.zones[0].splitName = "Ninja";
            var report = LevelObjectCatalog.AssignZones(level);
            Assert.IsEmpty(report.errors);
            Assert.That(report.warnings.Any(e => e.Contains("split override")));
            Assert.AreEqual("T0.RunSplit.01", split.meta.objectId);
        }

        [Test] public void ShippedLevel_HasCompleteUniqueMetadataWithoutRunningAuthoring()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            Assert.IsNotNull(shipped);
            var copy = Object.Instantiate(shipped);
            try
            {
                Assert.AreEqual(6, copy.zones.Length, "Lead must regenerate and save Level 1 metadata.");
                var records = LevelObjectCatalog.Enumerate(copy).ToArray();
                Assert.That(records.All(r => !string.IsNullOrEmpty(r.meta.objectId)));
                Assert.AreEqual(records.Length, records.Select(r => r.meta.objectId).Distinct().Count());
                Assert.IsEmpty(LevelObjectCatalog.AssignZones(copy).errors);
                Assert.AreEqual("T0.PlayerStart", copy.playerStartMeta.objectId);
                Assert.AreEqual("T0.WorldLeaderboard", copy.worldLeaderboard.meta.objectId);
                Assert.AreEqual("T0", copy.worldLeaderboard.meta.zoneIdOverride);
                // The Warden's pickup and spawner belong to the Warden Court (T5) by override; their ids
                // keep the T4 prefix they were minted with, because ids are never renamed.
                Assert.AreEqual("T5", copy.pickups.Single(p => p.name == "Pickup_Boss_Hook").meta.zoneIdOverride);
                Assert.AreEqual("T5", copy.spawns.Single(s => s.name == "Spawn_Boss").meta.zoneIdOverride);
                Assert.AreEqual("T4.HollowWarden.01", copy.spawns.Single(s => s.name == "Spawn_Boss").meta.objectId);
                Assert.AreEqual("T4.BossPortal.01", copy.arenas.Single(a => a.gateName == "Boss_Gate").solarRealm.meta.objectId);
                Assert.AreEqual("Level.Sky", copy.sky.meta.objectId);
                Assert.AreEqual("Level.KillZone", copy.killZone.meta.objectId);
                Assert.AreEqual(5, records.Count(r => r.kind == LevelObjectKind.Gate));
                Assert.AreEqual(4, records.Count(r => r.kind == LevelObjectKind.ExitGate));
                Assert.AreEqual(5, records.Count(r => r.kind == LevelObjectKind.BossPortal));
                Assert.AreEqual(copy.projectileSequences.Sum(s => s.engagementWindows.Length), records.Count(r => r.kind == LevelObjectKind.ProjectileEngagementWindow));
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test] public void LevelAuthoring_RegenerationPreservesStableIdsFriendlyNamesAndOverrides()
        {
            var copy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01));
            try
            {
                LevelDefinitionAuthoring.Apply(copy);
                var balloon = copy.balloons[0];
                balloon.meta.friendlyName = "My optional balloon";
                balloon.meta.zoneIdOverride = "T3";
                var ids = LevelObjectCatalog.Enumerate(copy).Select(r => r.meta.objectId).OrderBy(id => id).ToArray();
                LevelDefinitionAuthoring.Apply(copy);
                CollectionAssert.AreEqual(ids, LevelObjectCatalog.Enumerate(copy).Select(r => r.meta.objectId).OrderBy(id => id).ToArray());
                Assert.AreEqual("My optional balloon", copy.balloons.Single(b => b.name == balloon.name).meta.friendlyName);
                Assert.AreEqual("T3", copy.balloons.Single(b => b.name == balloon.name).meta.zoneIdOverride);
                Assert.IsEmpty(LevelObjectCatalog.AssignZones(copy).errors);
                string once = EditorJsonUtility.ToJson(copy);
                LevelDefinitionAuthoring.Apply(copy);
                Assert.AreEqual(once, EditorJsonUtility.ToJson(copy));
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test] public void LevelAuthoring_MetadataOnlyPassPreservesLayoutAndUsesApprovedZoneBounds()
        {
            var copy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01));
            try
            {
                var anchors = LevelObjectCatalog.Enumerate(copy).Select(r => r.anchor).ToArray();
                Assert.IsEmpty(LevelDefinitionAuthoring.ApplyLevelStudioMetadata(copy).errors);
                CollectionAssert.AreEqual(anchors, LevelObjectCatalog.Enumerate(copy).Select(r => r.anchor).ToArray());
                CollectionAssert.AreEqual(new[] { "Opening", "Ninja", "Knight", "Spellsword", "Grappler", "Warden" }, copy.zones.Select(z => z.splitName));
                CollectionAssert.AreEqual(new[] { "T0", "T1", "T2", "T3", "T4", "T5" }, copy.zones.Select(z => z.zoneId));
                var lower = new[] { -166f, 7.9f, 136.8f, 260.8f, 393f, 514.5f };
                var upper = new[] { 7.8f, 136.7f, 260.7f, 392.9f, 514.4f, 600f };
                for (int i = 0; i < 6; i++)
                {
                    Assert.AreEqual(lower[i], copy.zones[i].center.z - copy.zones[i].size.z / 2, 0.0001f);
                    Assert.AreEqual(upper[i], copy.zones[i].center.z + copy.zones[i].size.z / 2, 0.0001f);
                    if (i > 0) Assert.Less(upper[i - 1], lower[i], "Primary zone faces must not touch.");
                }
                Assert.That(copy.platforms.Single(p => p.name == "T0_Entry").meta.objectId, Does.StartWith("T0.Platform."));
                Assert.That(copy.platforms.Single(p => p.name == "T2_Wall_Landing_West").meta.objectId, Does.StartWith("T2.Platform."));
                Assert.That(copy.platforms.Single(p => p.name == "T3_Pillar_1").meta.objectId, Does.StartWith("T3.Platform."));
                Assert.That(copy.pickups.Single(p => p.name == "Pickup_Boss_Hook").meta.objectId, Does.StartWith("T4.Pickup."));
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test] public void ZoneAssignment_ChildrenInheritOwnerOverrideAndRejectConflictingOverride()
        {
            var arena = new ArenaDef { enabled = true, meta = new LevelObjectMeta { zoneIdOverride = "T1" } };
            level.arenas = new[] { arena };
            Assert.IsEmpty(LevelObjectCatalog.AssignZones(level).errors);
            Assert.AreEqual("T1.Gate.01", arena.gateMeta.objectId);
            arena.gateMeta.zoneIdOverride = "T0";
            Assert.That(LevelObjectCatalog.AssignZones(level).errors.Any(e => e.Contains("child override")));
            Assert.AreEqual("T1.Gate.01", arena.gateMeta.objectId);
        }
    }
}
