using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    public enum LevelValidationSeverity { Error, Warning }

    public sealed class LevelValidationIssue
    {
        public readonly string code, objectId, fieldPath, message;
        public readonly LevelValidationSeverity severity;
        internal LevelValidationIssue(LevelValidationSeverity severity, string code, string objectId, string fieldPath, string message)
        { this.severity = severity; this.code = code; this.objectId = objectId; this.fieldPath = fieldPath; this.message = message; }
    }

    public sealed class LevelValidationReport
    {
        readonly List<LevelValidationIssue> errorList = new List<LevelValidationIssue>();
        readonly List<LevelValidationIssue> warningList = new List<LevelValidationIssue>();
        public IReadOnlyList<LevelValidationIssue> errors { get { return errorList.AsReadOnly(); } }
        public IReadOnlyList<LevelValidationIssue> warnings { get { return warningList.AsReadOnly(); } }
        public bool HasErrors { get { return errors.Count != 0; } }
        internal void Add(LevelValidationSeverity severity, string code, string id, string path, string message)
        {
            var issue = new LevelValidationIssue(severity, code, id ?? "", path ?? "", message ?? "");
            (severity == LevelValidationSeverity.Error ? errorList : warningList).Add(issue);
        }
        internal void Sort()
        {
            Comparison<LevelValidationIssue> comparison = (a, b) => string.Compare(a.code + "\u001f" + a.objectId + "\u001f" + a.fieldPath, b.code + "\u001f" + b.objectId + "\u001f" + b.fieldPath, StringComparison.Ordinal);
            errorList.Sort(comparison);
            warningList.Sort(comparison);
        }
    }
    /// <summary>Pure resolver seam. Production may adapt EditorContext; tests use a deterministic fake.</summary>
    public interface ILevelStudioResourceResolver
    {
        bool HasPrefab(string key); bool HasEnemyData(string key); bool HasItem(string key); bool HasMaterial(string key);
        bool IsProjectileCapable(string prefabKey);
    }
    /// <summary>Optional exact route gate. The validator never substitutes a generic geometry guess.</summary>
    public interface ILevelStudioTraversalAdapter { bool TryValidate(LevelDefinition definition, out string error); }

    /// <summary>Read-only schema and stable-identity checks for an in-memory Level Studio definition.</summary>
    public static class LevelStudioValidator
    {
        static readonly Regex ZoneId = new Regex(@"\A[A-Za-z][A-Za-z0-9_-]*\z");
        static readonly Regex ObjectId = new Regex(@"\A[A-Za-z][A-Za-z0-9_-]*\.[A-Za-z][A-Za-z0-9_-]*\.[A-Za-z0-9_-]+\z");

        public static LevelValidationReport Validate(LevelDefinition definition, ILevelStudioResourceResolver resources = null, ILevelStudioTraversalAdapter traversal = null)
        {
            var report = new LevelValidationReport();
            if (definition == null) { report.Add(LevelValidationSeverity.Error, "MissingDefinition", "$level", "$level", "Level definition is missing."); return report; }
            if (string.IsNullOrWhiteSpace(definition.levelId)) report.Add(LevelValidationSeverity.Error, "MissingLevelId", "$level", "levelId", "levelId is required.");
            if (string.IsNullOrWhiteSpace(definition.displayName)) report.Add(LevelValidationSeverity.Error, "MissingDisplayName", "$level", "displayName", "displayName is required.");
            if (string.IsNullOrWhiteSpace(definition.sceneName)) report.Add(LevelValidationSeverity.Error, "MissingSceneName", "$level", "sceneName", "sceneName is required.");
            CheckFinite(definition, "$level", "$level", report, new HashSet<object>(ReferenceComparer.Instance));
            ValidateZones(definition, report);
            ValidateRequiredRoots(definition, report);
            ValidateNullArrayEntries(definition, report);
            foreach (var entry in LevelDraftInventory.Entries(definition)) ValidateEntry(entry, definition.zones, report);
            ValidateNames(definition, report);
            ValidateNamedReferences(definition, resources, report);
            ValidateZoneOwnership(definition, report);
            ValidateRunContract(definition, report);
            ValidateResources(definition, resources, report);
            ValidateWarnings(definition, report);
            ValidateTraversal(definition, traversal, report);
            report.Sort();
            return report;
        }

        /// <summary>There is no generic route graph. A warning is intentionally emitted until an apply caller supplies the Level 1 adapter.</summary>
        static void ValidateTraversal(LevelDefinition d, ILevelStudioTraversalAdapter traversal, LevelValidationReport report)
        {
            if (d.levelId == "level_01" && traversal == null) report.Add(LevelValidationSeverity.Warning, "TraversalAdapterRequired", "$level", "levelId", "Level 1 requires the exact traversal adapter before apply.");
            else if (d.levelId == "level_01") { string error; if (!traversal.TryValidate(d, out error)) report.Add(LevelValidationSeverity.Error, "InvalidRequiredTraversal", "$level", "levelId", error); }
            else report.Add(LevelValidationSeverity.Warning, "TraversalGraphUnavailable", "$level", "levelId", "No authored generic required-route graph exists; reachability is not claimed validated.");
        }
        static void ValidateRunContract(LevelDefinition d, LevelValidationReport report) { string error; if (!RunScoreMath.TryValidateDefinition(d, out error)) report.Add(LevelValidationSeverity.Error, "InvalidRunContract", "$level", "runSplits", error); }
        static void ValidateResources(LevelDefinition d, ILevelStudioResourceResolver r, LevelValidationReport report)
        {
            if (r == null) { report.Add(LevelValidationSeverity.Warning, "ResourceResolverRequired", "$level", "$level", "Resource references require the apply-time resolver."); return; }
            foreach (var s in d.spawns ?? new SpawnDef[0]) if (s != null && (!r.HasPrefab(s.prefabKey) || !r.HasEnemyData(s.prefabKey))) report.Add(LevelValidationSeverity.Error, "MissingSpawnResource", Id(s.meta), "spawns.prefabKey", "Spawn prefab or EnemyData does not resolve.");
            foreach (var p in d.pickups ?? new PickupDef[0]) if (p != null && !r.HasItem(p.itemKey)) report.Add(LevelValidationSeverity.Error, "MissingItemResource", Id(p.meta), "pickups.itemKey", "Pickup item does not resolve.");
            foreach (var p in d.platforms ?? new PlatformDef[0]) if (p != null && (!r.HasMaterial(p.materialKey) || (p.trim && !r.HasMaterial(p.trimMaterialKey)))) report.Add(LevelValidationSeverity.Error, "MissingMaterialResource", Id(p.meta), "platforms.materialKey", "Platform material does not resolve.");
            foreach (var ramp in d.ramps ?? new RampDef[0]) if (ramp != null && !r.HasMaterial(ramp.materialKey)) report.Add(LevelValidationSeverity.Error, "MissingMaterialResource", Id(ramp.meta), "ramps.materialKey", "Ramp material does not resolve.");
            foreach (var arena in d.arenas ?? new ArenaDef[0]) if (arena != null && (arena.enabled || arena.hasExitGate || (arena.solarRealm != null && arena.solarRealm.enabled)) && (!r.HasMaterial(arena.gateMaterialKey) || (arena.hasExitGate && !r.HasMaterial(arena.exitGateMaterialKey)) || (arena.solarRealm != null && arena.solarRealm.enabled && !r.HasMaterial(arena.solarRealm.themeMaterialKey)))) report.Add(LevelValidationSeverity.Error, "MissingMaterialResource", Id(arena.meta), "arenas.materialKey", "Arena material does not resolve.");
        }

        static void ValidateRequiredRoots(LevelDefinition d, LevelValidationReport report)
        {
            if (d.playerStartMeta == null) report.Add(LevelValidationSeverity.Error, "MissingRequiredRecord", "$level", "playerStartMeta", "playerStart metadata is required.");
            if (d.killZone == null) report.Add(LevelValidationSeverity.Error, "MissingRequiredRecord", "$level", "killZone", "killZone is required.");
            if (d.sky == null) report.Add(LevelValidationSeverity.Error, "MissingRequiredRecord", "$level", "sky", "sky is required.");
            if (d.worldLeaderboard == null) report.Add(LevelValidationSeverity.Error, "MissingRequiredRecord", "$level", "worldLeaderboard", "worldLeaderboard is required.");
        }

        static void ValidateNullArrayEntries(LevelDefinition d, LevelValidationReport report)
        {
            foreach (string name in LevelDraftInventory.Collections)
            {
                var field = typeof(LevelDefinition).GetField(name);
                var values = field == null ? null : field.GetValue(d) as Array;
                if (values == null) { report.Add(LevelValidationSeverity.Error, "NullAuthoredArray", "$level", name, name + " is null."); continue; }
                for (int i = 0; i < values.Length; i++) if (values.GetValue(i) == null)
                    report.Add(LevelValidationSeverity.Error, "NullEntry", "$level", name + "[" + i + "]", name + " contains a null authored entry.");
            }
        }

        static void ValidateZones(LevelDefinition definition, LevelValidationReport report)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var zone in definition.zones ?? new ZoneDef[0])
            {
                if (zone == null) { report.Add(LevelValidationSeverity.Error, "NullEntry", "$level", "zones", "zones contains a null entry."); continue; }
                string id = zone.zoneId ?? "";
                if (!ZoneId.IsMatch(id) || id == "Level") report.Add(LevelValidationSeverity.Error, "MalformedZoneId", "$zone/" + id, "zones", "Zone ID is malformed or reserved.");
                else if (!seen.Add(id)) report.Add(LevelValidationSeverity.Error, "DuplicateZoneId", "$zone/" + id, "zones", "Zone ID is duplicated.");
                if (!Finite(zone.center) || !Finite(zone.size) || zone.size.x <= 0f || zone.size.y <= 0f || zone.size.z <= 0f)
                    report.Add(LevelValidationSeverity.Error, "InvalidZoneBounds", "$zone/" + id, "zones", "Zone bounds must be finite and positive.");
            }
        }

        static void ValidateEntry(LevelDraftInventory.Entry entry, ZoneDef[] zones, LevelValidationReport report)
        {
            if (entry.path.StartsWith("$", StringComparison.Ordinal) || entry.path.StartsWith("zones[", StringComparison.Ordinal)) return;
            string id = entry.meta == null ? "" : entry.meta.objectId ?? "";
            if (entry.meta == null || string.IsNullOrWhiteSpace(id)) report.Add(LevelValidationSeverity.Error, "MissingObjectId", id, entry.path, "Every authored record needs a stable object ID.");
            else
            {
                if (!ValidIdForEntry(entry, id)) report.Add(LevelValidationSeverity.Error, "MalformedObjectId", id, entry.path + ".meta.objectId", "Object ID must use this record's canonical kind and singleton form.");
                if (!entry.ids.Add(id)) report.Add(LevelValidationSeverity.Error, "DuplicateObjectId", id, entry.path + ".meta.objectId", "Object ID is duplicated.");
            }
            if (entry.meta != null && !string.IsNullOrEmpty(entry.meta.zoneIdOverride) && !(zones ?? new ZoneDef[0]).Any(z => z != null && z.zoneId == entry.meta.zoneIdOverride))
                report.Add(LevelValidationSeverity.Error, "InvalidZoneOverride", id, entry.path + ".meta.zoneIdOverride", "Zone override does not resolve.");
            if (!entry.global && entry.primary && (zones == null || zones.Length == 0))
                report.Add(LevelValidationSeverity.Error, "MissingZoneOwnership", id, entry.path, "Primary authored records require a zone.");
            CheckPositiveGeometry(entry, report);
        }

        static bool ValidIdForEntry(LevelDraftInventory.Entry entry, string id)
        {
            if (entry.path == "sky") return id == "Level.Sky";
            if (entry.path == "killZone") return id == "Level.KillZone";
            if (entry.path == "playerStart" || entry.path == "worldLeaderboard")
            {
                string[] singleton = id.Split('.');
                return singleton.Length == 2 && ZoneId.IsMatch(singleton[0]) && singleton[0] != "Level" && singleton[1] == entry.kind;
            }
            string[] parts = id.Split('.');
            return ObjectId.IsMatch(id) && parts[1] == entry.kind;
        }

        static void ValidateNames(LevelDefinition definition, LevelValidationReport report)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var spawn in definition.spawns ?? new SpawnDef[0])
            {
                if (spawn == null) continue;
                if (string.IsNullOrWhiteSpace(spawn.name) || !names.Add(spawn.name ?? ""))
                    report.Add(LevelValidationSeverity.Error, "DuplicateSpawnName", spawn.meta == null ? "" : spawn.meta.objectId, "spawns", "Spawn names must be non-empty and unique.");
            }
            names.Clear();
            var checkpoints = (definition.checkpoints ?? new CheckpointDef[0]).Where(x => x != null).ToArray();
            if (checkpoints.Length == 0) report.Add(LevelValidationSeverity.Error, "MissingCheckpoint", "$level", "checkpoints", "A campaign draft requires at least one checkpoint.");
            foreach (var checkpoint in checkpoints)
                if (string.IsNullOrWhiteSpace(checkpoint.name) || !names.Add(checkpoint.name ?? ""))
                    report.Add(LevelValidationSeverity.Error, "DuplicateCheckpointName", Id(checkpoint.meta), "checkpoints", "Checkpoint names must be non-empty and unique.");
        }

        static void ValidateNamedReferences(LevelDefinition d, ILevelStudioResourceResolver resources, LevelValidationReport report)
        {
            var spawns = (d.spawns ?? new SpawnDef[0]).Where(s => s != null).ToArray();
            Func<string, int> count = name => spawns.Count(s => s.name == name);
            foreach (var split in d.runSplits ?? new RunSplitDef[0]) if (split != null && count(split.endSpawnerName) != 1)
                report.Add(LevelValidationSeverity.Error, "BrokenNamedReference", Id(split.meta), "runSplits", "Split end spawner must resolve exactly once.");
            foreach (var arena in d.arenas ?? new ArenaDef[0]) if (arena != null)
            {
                if (arena.hasExitGate && count(arena.clearSpawnerName) != 1) report.Add(LevelValidationSeverity.Error, "BrokenNamedReference", Id(arena.meta), "arenas", "Exit-gate arena clear spawner must resolve exactly once.");
                if (arena.enabled && (!Positive(arena.triggerSize) || !Positive(arena.gateSize) || (arena.hasExitGate && !Positive(arena.exitGateSize)))) report.Add(LevelValidationSeverity.Error, "InvalidArenaGeometry", Id(arena.meta), "arenas", "Enabled arena trigger and gate geometry must be positive.");
                var solar = arena.solarRealm;
                if (solar != null && solar.enabled && !arena.enabled) report.Add(LevelValidationSeverity.Error, "InvalidSolarRealm", Id(solar.meta), "arenas.solarRealm.enabled", "An enabled solar realm requires its arena to be enabled.");
                if (solar != null && solar.enabled && count(solar.enemySpawnerName) != 1) report.Add(LevelValidationSeverity.Error, "BrokenNamedReference", Id(solar.meta), "arenas.solarRealm", "Enabled solar realm enemy spawner must resolve exactly once.");
                if (solar != null && solar.enabled && string.IsNullOrWhiteSpace(solar.arenaPickupName)) report.Add(LevelValidationSeverity.Error, "BrokenNamedReference", Id(solar.meta), "arenas.solarRealm.arenaPickupName", "Enabled solar realm requires an arena pickup name.");
                if (solar != null && solar.enabled && !string.IsNullOrWhiteSpace(solar.arenaPickupName) && (d.pickups ?? new PickupDef[0]).Count(p => p != null && p.name == solar.arenaPickupName) != 1) report.Add(LevelValidationSeverity.Error, "BrokenNamedReference", Id(solar.meta), "arenas.solarRealm.arenaPickupName", "Solar arena pickup must resolve exactly once.");
                if (solar != null && solar.enabled && (solar.exteriorRadius <= 0f || solar.realmFloorRadius <= 0f || solar.realmShellRadius < solar.realmFloorRadius)) report.Add(LevelValidationSeverity.Error, "InvalidSolarRealm", Id(solar.meta), "arenas.solarRealm", "Solar realm radii are incoherent.");
                if (solar != null && solar.enabled && solar.hasReturn && (!Finite(solar.realmExitPosition) || !Finite(solar.returnPosition))) report.Add(LevelValidationSeverity.Error, "InvalidSolarRealm", Id(solar.meta), "arenas.solarRealm", "Returning solar realm needs finite return data.");
            }
            foreach (var sequence in d.projectileSequences ?? new ProjectileSequenceDef[0]) if (sequence != null)
            {
                if ((sequence.spawnerNames ?? new string[0]).Length == 0 || (sequence.engagementWindows ?? new ProjectileEngagementWindowDef[0]).Length == 0) report.Add(LevelValidationSeverity.Error, "InvalidProjectileSequence", Id(sequence.meta), "projectileSequences", "Projectile sequence needs members and bounded windows.");
                foreach (var name in sequence.spawnerNames ?? new string[0]) if (count(name) != 1) report.Add(LevelValidationSeverity.Error, "BrokenNamedReference", Id(sequence.meta), "projectileSequences", "Projectile sequence member must resolve exactly once.");
                foreach (var name in sequence.spawnerNames ?? new string[0]) if (resources != null && count(name) == 1 && !resources.IsProjectileCapable(spawns.Single(s => s.name == name).prefabKey)) report.Add(LevelValidationSeverity.Error, "NonProjectileSequenceMember", Id(sequence.meta), "projectileSequences.spawnerNames", "Projectile sequence member must resolve to projectile-capable EnemyData.");
                foreach (var window in sequence.engagementWindows ?? new ProjectileEngagementWindowDef[0]) if (window == null || !ProjectileEngagementMath.IsValid(window) || count(window.spawnerName) != 1 || !(sequence.spawnerNames ?? new string[0]).Contains(window.spawnerName)) report.Add(LevelValidationSeverity.Error, "InvalidProjectileWindow", window == null ? Id(sequence.meta) : Id(window.meta), "projectileSequences.engagementWindows", "Projectile window owner or safe geometry is invalid.");
                foreach (var name in sequence.spawnerNames ?? new string[0]) if (!(sequence.engagementWindows ?? new ProjectileEngagementWindowDef[0]).Any(w => w != null && w.spawnerName == name)) report.Add(LevelValidationSeverity.Error, "MissingProjectileWindow", Id(sequence.meta), "projectileSequences.engagementWindows", "Every projectile member needs an owned route window.");
            }
            foreach (var ownership in (d.projectileSequences ?? new ProjectileSequenceDef[0]).Where(x => x != null).SelectMany(x => x.spawnerNames ?? new string[0]).GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() != 1))
                report.Add(LevelValidationSeverity.Error, "DuplicateProjectileOwner", "$level", "projectileSequences.spawnerNames", "Projectile spawn '" + ownership.Key + "' belongs to more than one sequence.");
            foreach (var route in d.challengeRoutes ?? new ChallengeRouteDef[0]) if (route != null)
            {
                if (!Positive(route.entrySize) || !Positive(route.rejoinSize)) report.Add(LevelValidationSeverity.Error, "InvalidChallengeRoute", Id(route.meta), "challengeRoutes", "Challenge Route entry/rejoin bounds must be positive.");
                var sources = route.sourceSpawnerNames ?? new string[0];
                foreach (var name in sources) if (count(name) != 1) report.Add(LevelValidationSeverity.Error, "BrokenNamedReference", Id(route.meta), "challengeRoutes.sourceSpawnerNames", "Challenge source spawn must resolve exactly once.");
                if (sources.Length == 0 || (resources != null && !sources.Any(n => count(n) == 1 && resources.IsProjectileCapable(spawns.Single(s => s.name == n).prefabKey)))) report.Add(LevelValidationSeverity.Warning, "ChallengeRouteNoProjectileSource", Id(route.meta), "challengeRoutes.sourceSpawnerNames", "Challenge route has no projectile-capable supporting source.");
            }
        }

        static void ValidateWarnings(LevelDefinition d, LevelValidationReport report)
        {
            var entries = LevelDraftInventory.Entries(d).ToList(); LevelDraftInventory.ResolveZones(d, entries);
            foreach (var zone in d.zones ?? new ZoneDef[0]) if (zone != null && !entries.Any(e => e.kind == "Torch" && e.ZoneId == zone.zoneId))
                report.Add(LevelValidationSeverity.Warning, "SparseZoneLighting", "$zone/" + zone.zoneId, "torches", "Zone has no authored torch.");
            foreach (var checkpoint in d.checkpoints ?? new CheckpointDef[0]) if (checkpoint != null && (checkpoint.position - d.playerStart).sqrMagnitude < 0.01f)
                report.Add(LevelValidationSeverity.Warning, "SuspiciousSpacing", Id(checkpoint.meta), "checkpoints.position", "Checkpoint overlaps the player start.");
        }

        static void ValidateZoneOwnership(LevelDefinition d, LevelValidationReport report)
        {
            var entries = LevelDraftInventory.Entries(d).ToList(); LevelDraftInventory.ResolveZones(d, entries);
            foreach (var entry in entries.Where(e => !e.global && e.primary && e.meta != null && string.IsNullOrEmpty(e.meta.zoneIdOverride) && entryHasAnchor(e.data)))
                if (entry.ZoneId == "Unassigned") report.Add(LevelValidationSeverity.Error, "InvalidZoneOwnership", entry.Id, entry.path, "Primary record must be contained by exactly one zone or have a valid override.");
            foreach (var child in entries.Where(e => !e.global && e.path.IndexOf('.') > 0 && e.meta != null && !string.IsNullOrEmpty(e.meta.zoneIdOverride)))
            {
                var ownerPath = child.path.Substring(0, child.path.IndexOf('.'));
                var owner = entries.FirstOrDefault(e => e.path == ownerPath);
                if (owner != null && owner.ZoneId != "Unassigned" && child.meta.zoneIdOverride != owner.ZoneId)
                    report.Add(LevelValidationSeverity.Error, "ChildOwnerZoneConflict", child.Id, child.path + ".meta.zoneIdOverride", "Child override must agree with owner zone.");
                else if (owner != null && owner.ZoneId != "Unassigned")
                    report.Add(LevelValidationSeverity.Warning, "MatchingChildZoneOverride", child.Id, child.path + ".meta.zoneIdOverride", "Child override redundantly matches owner zone.");
            }
            foreach (var split in d.runSplits ?? new RunSplitDef[0]) if (split != null)
            {
                var splitEntry = entries.FirstOrDefault(e => ReferenceEquals(e.data, split));
                var owner = (d.spawns ?? new SpawnDef[0]).Where(s => s != null && s.name == split.endSpawnerName).ToArray();
                var ownerEntry = owner.Length == 1 ? entries.FirstOrDefault(e => ReferenceEquals(e.data, owner[0])) : null;
                if (splitEntry == null || ownerEntry == null || ownerEntry.ZoneId == "Unassigned") continue;
                if (!string.IsNullOrEmpty(split.meta.zoneIdOverride) && split.meta.zoneIdOverride != ownerEntry.ZoneId) report.Add(LevelValidationSeverity.Error, "SplitZoneConflict", splitEntry.Id, "runSplits.meta.zoneIdOverride", "Split override must agree with its endpoint spawn zone.");
                var zone = (d.zones ?? new ZoneDef[0]).FirstOrDefault(z => z != null && z.zoneId == ownerEntry.ZoneId);
                if (zone != null && zone.splitName != split.name) report.Add(LevelValidationSeverity.Error, "SplitNameMismatch", splitEntry.Id, "runSplits.name", "Split name must agree with its endpoint zone splitName.");
            }
            foreach (var entry in entries.Where(e => !e.global && e.primary && entryHasAnchor(e.data) && e.ZoneId != "Unassigned"))
            {
                Vector3 anchor; if (!LevelDraftInventory.Anchor(entry.data, out anchor)) continue;
                var zone = (d.zones ?? new ZoneDef[0]).FirstOrDefault(z => z != null && z.zoneId == entry.ZoneId);
                if (zone != null && NearBoundary(new Bounds(zone.center, zone.size), anchor)) report.Add(LevelValidationSeverity.Warning, "NearZoneBoundary", entry.Id, entry.path, "Anchor is within 0.1m of its zone boundary.");
            }
            var zones = (d.zones ?? new ZoneDef[0]).Where(z => z != null).ToArray();
            for (int i = 0; i < zones.Length; i++) for (int j = i + 1; j < zones.Length; j++) if (InteriorOverlap(new Bounds(zones[i].center, zones[i].size), new Bounds(zones[j].center, zones[j].size))) report.Add(LevelValidationSeverity.Error, "OverlappingZones", "$zone/" + zones[i].zoneId, "zones", "Zone interiors overlap.");
        }
        static bool entryHasAnchor(object value) { Vector3 ignored; return LevelDraftInventory.Anchor(value, out ignored); }
        static bool InteriorOverlap(Bounds a, Bounds b) { return a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y && a.max.y > b.min.y && a.min.z < b.max.z && a.max.z > b.min.z; }
        static bool NearBoundary(Bounds bounds, Vector3 point) { Vector3 d = point - bounds.center; Vector3 h = bounds.size * 0.5f; return Mathf.Min(h.x - Mathf.Abs(d.x), Mathf.Min(h.y - Mathf.Abs(d.y), h.z - Mathf.Abs(d.z))) <= 0.1f; }
        static string Id(LevelObjectMeta meta) { return meta == null ? "" : meta.objectId ?? ""; }

        static void CheckPositiveGeometry(LevelDraftInventory.Entry entry, LevelValidationReport report)
        {
            bool bad = false;
            var p = entry.data as PlatformDef; if (p != null) bad = !Positive(p.size);
            var w = entry.data as WaterDef; if (w != null) bad = !Positive(w.size);
            var k = entry.data as KillZoneDef; if (k != null) bad = !Positive(k.size);
            var b = entry.data as BalloonDef; if (b != null) bad = b.radius <= 0f;
            var r = entry.data as RampDef; if (r != null) bad = r.width <= 0f || r.run <= 0f || r.thickness <= 0f;
            var pedestal = entry.data as PedestalDef; if (pedestal != null) bad = pedestal.triggerRadius <= 0f;
            var leaderboard = entry.data as WorldLeaderboardDef; if (leaderboard != null && leaderboard.enabled) bad = leaderboard.size.x <= 0f || leaderboard.size.y <= 0f;
            if (bad) report.Add(LevelValidationSeverity.Error, "InvalidPhysicalSize", entry.Id, entry.path, "Physical dimensions and radii must be positive.");
        }

        static void CheckFinite(object value, string id, string path, LevelValidationReport report, HashSet<object> seen)
        {
            if (value == null) return;
            var type = value.GetType();
            if (type == typeof(float)) { if (!Finite((float)value)) report.Add(LevelValidationSeverity.Error, "NonFiniteNumber", id, path, "Numeric values must be finite."); return; }
            if (type == typeof(Vector2)) { var v = (Vector2)value; if (!Finite(v.x) || !Finite(v.y)) report.Add(LevelValidationSeverity.Error, "NonFiniteNumber", id, path, "Numeric values must be finite."); return; }
            if (type == typeof(Vector3)) { if (!Finite((Vector3)value)) report.Add(LevelValidationSeverity.Error, "NonFiniteNumber", id, path, "Numeric values must be finite."); return; }
            if (type.IsPrimitive || type.IsEnum || type == typeof(string)) return;
            if (!type.IsValueType && !seen.Add(value)) return;
            var enumerable = value as IEnumerable;
            if (enumerable != null) { int i = 0; foreach (var item in enumerable) { CheckFinite(item, id, path + "[" + i++ + "]", report, seen); } return; }
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public)) CheckFinite(field.GetValue(value), id, path + "." + field.Name, report, seen);
        }

        static bool Positive(Vector3 value) { return value.x > 0f && value.y > 0f && value.z > 0f; }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        sealed class ReferenceComparer : IEqualityComparer<object> { internal static readonly ReferenceComparer Instance = new ReferenceComparer(); public new bool Equals(object x, object y) { return ReferenceEquals(x, y); } public int GetHashCode(object x) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(x); } }
    }
}
