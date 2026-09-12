using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.EditorTools
{
    public enum LevelStudioRowKind { Category, ResumeLastDraft, CampaignOriginal, WorkingCopy, CustomLevel, RecoveryDraft, Recovery, LegacyCandidate, ObjectRoot, Zone, Route, Type, Object }
    public enum LevelStudioRowAction { None, ProtectedCopy, CustomCopy, Resume, RecoverLatest }

    /// <summary>UI-neutral row. Its identity is a durable guid/path/draft key, never a scene object.</summary>
    public sealed class LevelStudioRow
    {
        public string key, id, parentKey, label, secondary, diagnostic, sourceGuid, assetPath, draftId, recordPath, zoneKey, routeKey;
        public string ownerId, ownerPath, dataKey, resolvedZoneId, resolvedZoneName, resolvedZoneSplit;
        public int depth, changedObjectCount;
        public int anchorCount;
        public Vector3 anchor;
        public LevelStudioAnchor[] anchors = new LevelStudioAnchor[0];
        public LevelObjectKind canonicalKind;
        public LevelStudioRowKind kind;
        public LevelStudioRowAction action;
        public LevelDraftSourceState sourceState;
        public LevelDraftState draftState;
        public readonly List<string> searchTerms = new List<string>();
    }

    public sealed class LevelStudioAnchor { public string path; public Vector3 position; }

    /// <summary>Pure category, hierarchy and filtering models for the Level Studio browser.</summary>
    public static class LevelStudioBrowser
    {
        static readonly Dictionary<string, int> TypeRanks = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "PlayerStart", 0 }, { "Platform", 10 }, { "Ramp", 11 }, { "Balloon", 12 }, { "Water", 13 }, { "Spawn", 20 },
            { "Sentry", 21 }, { "HeavySentry", 22 }, { "SurgeTurret", 23 }, { "Pickup", 30 }, { "Checkpoint", 31 },
            { "Arena", 40 }, { "Gate", 41 }, { "ExitGate", 42 }, { "BossPortal", 43 }, { "ProjectileSequence", 50 },
            { "ProjectileEngagementWindow", 51 }, { "ChallengeRoute", 52 }, { "RunSplit", 53 }, { "Torch", 60 },
            { "Pedestal", 61 }, { "WorldLeaderboard", 70 }, { "Sky", 71 }, { "KillZone", 72 }
        };

        public static IReadOnlyList<LevelStudioRow> BuildLibrary(LevelRegistry registry, IEnumerable<LevelDefinition> custom,
            IEnumerable<LevelDraftSummary> drafts, Func<string, IReadOnlyList<LevelDraftRecoverySummary>> recoveryHistory = null,
            IEnumerable<string> legacyFiles = null)
        {
            return BuildLibrary(registry != null ? registry.Ordered() : new LevelDefinition[0], custom, drafts, recoveryHistory, legacyFiles);
        }

        public static IReadOnlyList<LevelStudioRow> BuildLibrary(IEnumerable<LevelDefinition> campaign, IEnumerable<LevelDefinition> custom,
            IEnumerable<LevelDraftSummary> drafts, Func<string, IReadOnlyList<LevelDraftRecoverySummary>> recoveryHistory = null,
            IEnumerable<string> legacyFiles = null)
        {
            var rows = new List<LevelStudioRow>();
            var campaignGuids = new HashSet<string>(StringComparer.Ordinal);
            var campaignItems = new List<LibraryAsset>();
            foreach (var level in campaign ?? new LevelDefinition[0])
            {
                if (level == null) continue;
                string path = AssetDatabase.GetAssetPath(level);
                string guid = string.IsNullOrEmpty(path) ? level.SafeLevelId : AssetDatabase.AssetPathToGUID(path);
                if (!campaignGuids.Add(guid ?? "")) continue;
                campaignItems.Add(new LibraryAsset { level = level, guid = guid, path = path });
            }
            var customItems = new List<LibraryAsset>();
            foreach (var level in custom ?? new LevelDefinition[0])
            {
                if (level == null) continue;
                string path = AssetDatabase.GetAssetPath(level);
                string guid = string.IsNullOrEmpty(path) ? level.SafeLevelId : AssetDatabase.AssetPathToGUID(path);
                if (campaignGuids.Contains(guid ?? "")) continue;
                customItems.Add(new LibraryAsset { level = level, guid = guid, path = path });
            }
            var orderedDrafts = (drafts ?? new LevelDraftSummary[0]).Where(x => x != null)
                .OrderByDescending(x => x.savedUtc, StringComparer.Ordinal).ThenBy(x => x.draftId, StringComparer.Ordinal).ToArray();
            var newestResumable = orderedDrafts.FirstOrDefault(IsResumable);
            if (newestResumable != null)
                Add(rows, new LevelStudioRow { key = "resume-last", id = newestResumable.draftId,
                    label = "Resume Last Draft", secondary = newestResumable.displayName, draftId = newestResumable.draftId,
                    kind = LevelStudioRowKind.ResumeLastDraft, action = LevelStudioRowAction.Resume, sourceState = newestResumable.sourceState,
                    draftState = newestResumable.state, changedObjectCount = newestResumable.changedObjectCount }, newestResumable.displayName, newestResumable.draftId);
            var campaignCategory = Add(rows, new LevelStudioRow { key = "category:Campaign Originals", id = "Campaign Originals", label = "Campaign Originals", kind = LevelStudioRowKind.Category });
            foreach (var item in campaignItems)
                Add(rows, new LevelStudioRow { key = "campaign:" + item.guid, id = item.level.SafeLevelId, parentKey = campaignCategory.key,
                    label = item.level.displayName, secondary = item.level.SafeLevelId, sourceGuid = item.guid, assetPath = item.path, depth = 1,
                    kind = LevelStudioRowKind.CampaignOriginal, action = LevelStudioRowAction.ProtectedCopy }, item.level.displayName, item.level.SafeLevelId, item.guid, item.path);
            var workingCategory = Add(rows, new LevelStudioRow { key = "category:Working Copies", id = "Working Copies", label = "Working Copies", kind = LevelStudioRowKind.Category });
            foreach (var summary in orderedDrafts)
            {
                Add(rows, new LevelStudioRow { key = "draft:" + summary.draftId, id = summary.draftId, parentKey = workingCategory.key,
                    label = summary.displayName, secondary = DraftDetail(summary), draftId = summary.draftId, depth = 1, kind = LevelStudioRowKind.WorkingCopy,
                    action = IsResumable(summary) ? LevelStudioRowAction.Resume : LevelStudioRowAction.None, changedObjectCount = summary.changedObjectCount, sourceState = summary.sourceState,
                    draftState = summary.state, diagnostic = summary.diagnostic }, summary.displayName, summary.sourceLevelId, summary.draftId, summary.diagnostic);
            }
            var customCategory = Add(rows, new LevelStudioRow { key = "category:Custom Levels", id = "Custom Levels", label = "Custom Levels", kind = LevelStudioRowKind.Category });
            foreach (var item in customItems)
                Add(rows, new LevelStudioRow { key = "custom:" + item.guid, id = item.level.SafeLevelId, parentKey = customCategory.key,
                    label = item.level.displayName, secondary = item.level.SafeLevelId, sourceGuid = item.guid, assetPath = item.path, depth = 1,
                    kind = LevelStudioRowKind.CustomLevel, action = LevelStudioRowAction.CustomCopy }, item.level.displayName, item.level.SafeLevelId, item.guid, item.path);
            foreach (string legacy in legacyFiles ?? new string[0])
                Add(rows, new LevelStudioRow { key = "legacy:" + legacy, id = legacy, parentKey = customCategory.key, label = Path.GetFileNameWithoutExtension(legacy),
                    secondary = "Migration required", depth = 1, kind = LevelStudioRowKind.LegacyCandidate, diagnostic = "Migration required: legacy LevelDocument JSON is disabled." }, legacy);
            var recoveryCategory = Add(rows, new LevelStudioRow { key = "category:Recovery", id = "Recovery", label = "Recovery", kind = LevelStudioRowKind.Category });
            foreach (var summary in orderedDrafts)
            {
                var history = recoveryHistory == null ? new LevelDraftRecoverySummary[0] : recoveryHistory(summary.draftId) ?? new LevelDraftRecoverySummary[0];
                foreach (var recovery in history.Where(x => x != null && x.valid).OrderByDescending(x => x.autosavedUtc, StringComparer.Ordinal).Take(1))
                {
                    var group = Add(rows, new LevelStudioRow { key = "recovery-draft:" + summary.draftId, id = summary.draftId, parentKey = recoveryCategory.key,
                        label = summary.displayName, secondary = "Recovery", draftId = summary.draftId, depth = 1, kind = LevelStudioRowKind.RecoveryDraft,
                        sourceState = summary.sourceState, draftState = summary.state, diagnostic = summary.diagnostic }, summary.displayName, summary.draftId);
                    Add(rows, new LevelStudioRow { key = "recovery:" + summary.draftId + ":" + recovery.revision, id = recovery.path,
                        parentKey = group.key, label = summary.displayName + " recovery", secondary = recovery.autosavedUtc,
                        draftId = summary.draftId, depth = 2, kind = LevelStudioRowKind.Recovery, action = LevelStudioRowAction.RecoverLatest,
                        draftState = summary.state, diagnostic = recovery.diagnostic }, summary.displayName, recovery.path, recovery.autosavedUtc);
                }
            }
            return rows;
        }

        public static IReadOnlyList<LevelStudioRow> BuildHierarchy(LevelDefinition protectedDefinition, LevelStudioVocabulary vocabulary)
        {
            var rows = new List<LevelStudioRow>();
            var root = Add(rows, new LevelStudioRow { key = "objects", id = "objects", label = "Objects", kind = LevelStudioRowKind.ObjectRoot });
            if (protectedDefinition == null) return rows;
            vocabulary = vocabulary ?? LevelStudioVocabulary.Load();
            var records = LevelObjectCatalog.Enumerate(protectedDefinition).ToArray();
            var zones = (protectedDefinition.zones ?? new ZoneDef[0]).Where(z => z != null).OrderBy(z => z.order).ThenBy(z => z.zoneId, StringComparer.Ordinal).ToArray();
            var zoneRows = new Dictionary<string, LevelStudioRow>(StringComparer.Ordinal);
            foreach (var zone in zones)
            {
                string key = "zone:" + zone.zoneId;
                zoneRows[key] = Add(rows, new LevelStudioRow { key = key, id = zone.zoneId, parentKey = root.key, label = zone.canonicalName,
                    secondary = zone.splitName, depth = 1, kind = LevelStudioRowKind.Zone, zoneKey = key }, ZoneTerms(zone).ToArray());
            }
            zoneRows["zone:Globals"] = Add(rows, new LevelStudioRow { key = "zone:Globals", id = "Globals", parentKey = root.key, label = "Globals",
                depth = 1, kind = LevelStudioRowKind.Zone, zoneKey = "zone:Globals" }, "Globals");
            zoneRows["zone:Unassigned"] = Add(rows, new LevelStudioRow { key = "zone:Unassigned", id = "Unassigned", parentKey = root.key, label = "Unassigned",
                depth = 1, kind = LevelStudioRowKind.Zone, zoneKey = "zone:Unassigned" }, "Unassigned");
            var duplicateIds = records.Where(r => !string.IsNullOrWhiteSpace(r.meta.objectId)).GroupBy(r => r.meta.objectId, StringComparer.Ordinal)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            var pending = records.Select(record =>
                {
                    var zone = ResolveZone(record, records, zones);
                    return new Pending { record = record, zone = zone, zoneKey = "zone:" + zone.id, zoneDiagnostic = zone.diagnostic, routes = RouteMembership(record, records) };
                })
                .OrderBy(x => ZoneOrder(x.zoneKey, zones)).ThenBy(x => x.zoneKey, StringComparer.Ordinal).ThenBy(x => x.route, StringComparer.Ordinal)
                .ThenBy(x => TypeOrder(x.record.typeKey)).ThenBy(x => x.record.typeKey, StringComparer.Ordinal).ThenBy(x => x.record.path, StringComparer.Ordinal).ToArray();
            var routeRows = new Dictionary<string, LevelStudioRow>(StringComparer.Ordinal);
            var typeRows = new Dictionary<string, LevelStudioRow>(StringComparer.Ordinal);
            foreach (var item in pending)
            {
                LevelStudioRow zone;
                if (!zoneRows.TryGetValue(item.zoneKey, out zone)) zone = zoneRows["zone:Unassigned"];
                string routeGroup = RouteGroup(item.routes);
                string routeKey = "route:" + zone.id + ":" + routeGroup;
                LevelStudioRow route;
                if (!routeRows.TryGetValue(routeKey, out route))
                    routeRows[routeKey] = route = Add(rows, new LevelStudioRow { key = routeKey, id = routeGroup, parentKey = zone.key, label = routeGroup,
                        depth = 2, kind = LevelStudioRowKind.Route, zoneKey = zone.key, routeKey = routeKey }, item.routes.ToArray());
                string typeKey = string.IsNullOrWhiteSpace(item.record.typeKey) ? item.record.kind.ToString() : item.record.typeKey;
                string groupKey = "type:" + zone.id + ":" + routeGroup + ":" + typeKey;
                LevelStudioRow type;
                if (!typeRows.TryGetValue(groupKey, out type))
                    typeRows[groupKey] = type = Add(rows, new LevelStudioRow { key = groupKey, id = typeKey, parentKey = route.key, label = typeKey,
                        depth = 3, kind = LevelStudioRowKind.Type, zoneKey = zone.key, routeKey = route.key }, typeKey);
                string id = string.IsNullOrWhiteSpace(item.record.meta.objectId) ? item.record.path : item.record.meta.objectId;
                string diagnostic = JoinDiagnostics(ObjectDiagnostic(item.record, id, duplicateIds, vocabulary.diagnostic), item.zoneDiagnostic);
                var row = new LevelStudioRow { key = "object:" + item.record.path, id = id, parentKey = type.key, label = ObjectLabel(item.record, id),
                    secondary = typeKey, diagnostic = diagnostic, recordPath = item.record.path, zoneKey = zone.key, routeKey = route.key,
                    depth = 4, kind = LevelStudioRowKind.Object, canonicalKind = item.record.kind, dataKey = ShippedDataKey(item.record),
                    ownerId = OwnerId(item.record.owner), ownerPath = item.record.owner != null ? item.record.owner.path : string.Empty,
                    anchor = item.record.anchor, anchorCount = item.record.anchors == null ? 0 : item.record.anchors.Length,
                    anchors = CopyAnchors(item.record.anchors), resolvedZoneId = item.zone.id,
                    resolvedZoneName = item.zone.zone != null ? item.zone.zone.canonicalName : item.zone.id,
                    resolvedZoneSplit = item.zone.zone != null ? item.zone.zone.splitName : string.Empty };
                Add(rows, row, ObjectTerms(item.record, records, zones, vocabulary, item.routes).ToArray());
            }
            return rows;
        }

        public static IReadOnlyList<LevelStudioRow> Filter(IReadOnlyList<LevelStudioRow> rows, string query)
        {
            if (rows == null) return new LevelStudioRow[0];
            string needle = Normalize(query);
            if (string.IsNullOrEmpty(needle)) return rows;
            var parents = rows.Where(r => r != null).ToDictionary(r => r.key, r => r.parentKey, StringComparer.Ordinal);
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows.Where(r => r != null && r.searchTerms.Any(term => Normalize(term).Contains(needle))))
            {
                for (string key = row.key; !string.IsNullOrEmpty(key) && keep.Add(key); )
                {
                    string parent;
                    if (!parents.TryGetValue(key, out parent)) break;
                    key = parent;
                }
            }
            return rows.Where(row => row != null && keep.Contains(row.key)).ToArray();
        }

        public static int TypeOrder(string typeKey)
        {
            int rank;
            return !string.IsNullOrEmpty(typeKey) && TypeRanks.TryGetValue(typeKey, out rank) ? rank : 1000;
        }

        static LevelStudioRow Add(List<LevelStudioRow> rows, LevelStudioRow row, params string[] terms)
        {
            rows.Add(row);
            row.searchTerms.Add(row.id ?? ""); row.searchTerms.Add(row.label ?? ""); row.searchTerms.Add(row.secondary ?? ""); row.searchTerms.Add(row.diagnostic ?? "");
            foreach (string term in terms ?? new string[0]) if (!string.IsNullOrWhiteSpace(term)) row.searchTerms.Add(term);
            return row;
        }

        static IEnumerable<string> ZoneTerms(ZoneDef zone)
        {
            yield return zone.zoneId; yield return zone.canonicalName; yield return zone.splitName;
            foreach (string alias in zone.aliases ?? new string[0]) yield return alias;
        }

        static IEnumerable<string> ObjectTerms(LevelObjectRecord record, LevelObjectRecord[] records, ZoneDef[] zones, LevelStudioVocabulary vocabulary, IEnumerable<string> routes)
        {
            yield return record.path; yield return record.typeKey; yield return record.meta.objectId; yield return record.meta.friendlyName; yield return record.meta.zoneIdOverride;
            foreach (var term in StringFields(record.data)) yield return term;
            if (record.owner != null) { yield return record.owner.path; yield return record.owner.meta.objectId; }
            var spawn = record.data as SpawnDef;
            if (spawn != null) foreach (string term in vocabulary.EnemyTermsFor(spawn.prefabKey)) yield return term;
            foreach (string route in routes ?? new string[0]) yield return route;
            string zoneId = ResolveZone(record, records, zones).id;
            foreach (var zone in zones.Where(z => z != null && z.zoneId == zoneId)) foreach (string term in ZoneTerms(zone)) yield return term;
        }

        static IEnumerable<string> StringFields(object data)
        {
            if (data == null) yield break;
            foreach (FieldInfo field in data.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(string)) yield return (string)field.GetValue(data);
                if (field.FieldType == typeof(string[])) foreach (string value in (string[])field.GetValue(data) ?? new string[0]) yield return value;
            }
        }

        static ZoneResolution ResolveZone(LevelObjectRecord record, LevelObjectRecord[] all, ZoneDef[] zones)
        {
            if (record == null) return ZoneResolution.Unassigned("Object record is missing.");
            if (record.kind == LevelObjectKind.Sky || record.kind == LevelObjectKind.KillZone) return new ZoneResolution { id = "Globals" };
            if (record.owner != null) return ResolveInheritedZone(record.meta.zoneIdOverride, ResolveZone(record.owner, all, zones), "Child");
            if (record.kind == LevelObjectKind.RunSplit)
            {
                var split = record.data as RunSplitDef;
                var owners = all.Where(r => r.kind == LevelObjectKind.Spawn && ((SpawnDef)r.data).name == split.endSpawnerName).ToArray();
                if (owners.Length == 1) return ResolveInheritedZone(record.meta.zoneIdOverride, ResolveZone(owners[0], all, zones), "Run split");
                return ZoneResolution.Unassigned("Run split endpoint must resolve to exactly one spawn.");
            }
            if (!string.IsNullOrWhiteSpace(record.meta.zoneIdOverride))
                return zones.Any(z => z.zoneId == record.meta.zoneIdOverride)
                    ? new ZoneResolution { id = record.meta.zoneIdOverride, zone = zones.Single(z => z.zoneId == record.meta.zoneIdOverride) }
                    : ZoneResolution.Unassigned("Unknown zone override '" + record.meta.zoneIdOverride + "'.");
            var matching = zones.Where(z => new UnityEngine.Bounds(z.center, z.size).Contains(record.anchor)).ToArray();
            if (matching.Length == 1) return new ZoneResolution { id = matching[0].zoneId, zone = matching[0] };
            return ZoneResolution.Unassigned(matching.Length == 0 ? "No zone contains anchor." : "Multiple zones contain anchor.");
        }

        static ZoneResolution ResolveInheritedZone(string overrideId, ZoneResolution inherited, string subject)
        {
            if (string.IsNullOrWhiteSpace(overrideId) || inherited == null || inherited.id == "Unassigned") return inherited;
            if (overrideId == inherited.id) return inherited;
            inherited.diagnostic = JoinDiagnostics(inherited.diagnostic, subject + " override '" + overrideId + "' conflicts with inherited zone '" + inherited.id + "'.");
            return inherited;
        }

        static IReadOnlyList<string> RouteMembership(LevelObjectRecord record, LevelObjectRecord[] all)
        {
            if (record.owner != null) return RouteMembership(record.owner, all);
            var routes = new SortedSet<string>(StringComparer.Ordinal);
            var spawn = record.data as SpawnDef;
            string spawnName = spawn != null ? spawn.name : null;
            foreach (var source in all)
            {
                var sequence = source.data as ProjectileSequenceDef;
                if (sequence != null && (source == record || SequenceNames(sequence, spawnName))) routes.Add(RouteLabel(sequence.name, source.path));
                var challenge = source.data as ChallengeRouteDef;
                if (challenge != null && (source == record || Contains(challenge.sourceSpawnerNames, spawnName))) routes.Add(RouteLabel(challenge.routeId, source.path));
                var split = source.data as RunSplitDef;
                if (split != null && (source == record || (!string.IsNullOrEmpty(spawnName) && split.endSpawnerName == spawnName))) routes.Add(RouteLabel(split.name, source.path));
            }
            return routes.ToArray();
        }

        static bool Contains(IEnumerable<string> values, string value) { return !string.IsNullOrEmpty(value) && values != null && values.Any(x => x == value); }
        static bool SequenceNames(ProjectileSequenceDef sequence, string spawnName)
        {
            return Contains(sequence.spawnerNames, spawnName) || (sequence.engagementWindows ?? new ProjectileEngagementWindowDef[0])
                .Any(window => window != null && window.spawnerName == spawnName);
        }
        static string RouteLabel(string name, string fallback) { return string.IsNullOrWhiteSpace(name) ? fallback : name; }
        static string RouteGroup(IReadOnlyList<string> routes) { return routes == null || routes.Count == 0 ? "Shared" : routes.Count == 1 ? routes[0] : "Shared Routes"; }

        static int ZoneOrder(string zoneKey, ZoneDef[] zones)
        {
            string id = zoneKey.Substring("zone:".Length);
            var zone = zones.FirstOrDefault(z => z.zoneId == id);
            return zone == null ? 10000 : zone.order;
        }

        static string ObjectLabel(LevelObjectRecord record, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(record.meta.friendlyName)) return record.meta.friendlyName;
            return fallback;
        }

        static string OwnerId(LevelObjectRecord owner)
        {
            if (owner == null) return string.Empty;
            return string.IsNullOrWhiteSpace(owner.meta.objectId) ? owner.path : owner.meta.objectId;
        }

        static LevelStudioAnchor[] CopyAnchors(LevelObjectAnchor[] source)
        {
            if (source == null || source.Length == 0) return new LevelStudioAnchor[0];
            return source.Select(anchor => new LevelStudioAnchor { path = anchor.path, position = anchor.position }).ToArray();
        }

        static string ShippedDataKey(LevelObjectRecord record)
        {
            var spawn = record.data as SpawnDef;
            if (spawn != null) return spawn.prefabKey ?? string.Empty;
            var pickup = record.data as PickupDef;
            if (pickup != null) return pickup.itemKey ?? string.Empty;
            return record.typeKey ?? record.kind.ToString();
        }

        static string ObjectDiagnostic(LevelObjectRecord record, string id, HashSet<string> duplicateIds, string vocabularyDiagnostic)
        {
            if (duplicateIds.Contains(id)) return "Duplicate object ID: " + id;
            if (string.IsNullOrWhiteSpace(record.meta.objectId)) return "Missing object ID; field path is used.";
            if (record.kind == LevelObjectKind.Spawn && string.IsNullOrWhiteSpace(record.typeKey)) return "Unknown enemy data key.";
            string[] parts = id.Split('.');
            if (parts.Length < 2) return "Malformed object ID: " + id;
            return vocabularyDiagnostic;
        }

        static string JoinDiagnostics(string first, string second)
        {
            if (string.IsNullOrEmpty(first)) return second;
            if (string.IsNullOrEmpty(second) || first == second) return first;
            return first + " " + second;
        }

        static bool IsResumable(LevelDraftSummary summary)
        {
            return summary != null && summary.state == LevelDraftState.Valid && !string.IsNullOrWhiteSpace(summary.draftId);
        }

        static string DraftDetail(LevelDraftSummary summary)
        {
            return summary.sourceState + " | " + summary.state + " | " + summary.changedObjectCount + " changed | " + (string.IsNullOrEmpty(summary.autosavedUtc) ? "No autosave" : summary.autosavedUtc);
        }

        static string Normalize(string text)
        {
            return string.Join(" ", (text ?? "").Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }

        sealed class LibraryAsset { public LevelDefinition level; public string guid, path; }
        sealed class Pending { public LevelObjectRecord record; public ZoneResolution zone; public string zoneKey, zoneDiagnostic; public IReadOnlyList<string> routes; public string route { get { return RouteGroup(routes); } } }
        sealed class ZoneResolution
        {
            public string id, diagnostic;
            public ZoneDef zone;
            public static ZoneResolution Unassigned(string diagnostic) { return new ZoneResolution { id = "Unassigned", diagnostic = diagnostic }; }
        }
    }
}
