using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VibeGame1
{
    // Independent of the existing, serialized LevelPieceKind. Keep these values append-only too.
    public enum LevelObjectKind
    {
        Platform = 0, Ramp = 1, Spawn = 2, Pickup = 3, Checkpoint = 4, Torch = 5,
        Pedestal = 6, Balloon = 7, Water = 8, Arena = 9, ProjectileSequence = 10,
        ChallengeRoute = 11, RunSplit = 12, PlayerStart = 13, KillZone = 14, Sky = 15,
        WorldLeaderboard = 16, Gate = 17, ExitGate = 18, BossPortal = 19,
        ProjectileEngagementWindow = 20
    }

    public sealed class LevelObjectAnchor
    {
        public string path;
        public Vector3 position;
    }

    public sealed class LevelObjectRecord
    {
        public LevelObjectKind kind;
        public int index;
        public object data;
        public LevelObjectMeta meta;
        public Vector3 anchor;
        public string typeKey;
        public LevelObjectRecord owner;
        public string path;
        public LevelObjectAnchor[] anchors = new LevelObjectAnchor[0];
    }

    public sealed class ZoneAssignmentReport
    {
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
    }

    /// <summary>
    /// Shared authoring inventory. Metadata references point into the definition; enumeration only
    /// hydrates missing legacy metadata, and never assigns IDs or changes existing gameplay fields.
    /// Nested placed entities keep explicit owner/path links and multiple position handles.
    /// </summary>
    public static class LevelObjectCatalog
    {
        const float BoundaryWarningDistance = 0.1f;
        static readonly Regex Token = new Regex(@"\A[A-Za-z][A-Za-z0-9_-]*\z");
        static readonly Regex ObjectId = new Regex(@"\A[A-Za-z][A-Za-z0-9_-]*\.[A-Za-z][A-Za-z0-9_-]*\.[A-Za-z0-9_-]+\z");
        static readonly Dictionary<string, string> EnemyFamilies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "pshooter_enemy01", "Sentry" }, { "pshooter_enemy02", "HeavySentry" }, { "pshooter_enemy03", "SurgeTurret" },
            { "Enemy_Grunt", "Grunt" }, { "Enemy_Heavy", "Heavy" },
            { "Legendary_Ninja", "ThirteenthShade" }, { "Legendary_Knight", "IronPenitent" },
            { "Legendary_Spellsword", "AshenChorister" }, { "Boss", "HollowWarden" },
            { "Legendary_Drillmaster", "Drillmaster" }, { "Legendary_Halberdier", "Halberdier" },
            { "Legendary_Marionette", "Marionette" }, { "Legendary_Revenant", "Revenant" },
            { "Legendary_FlurryBrawler", "FlurryBrawler" }, { "Legendary_FlurryBrawlerV18", "FlurryBrawlerV18" }
        };

        public static IEnumerable<LevelObjectRecord> Enumerate(LevelDefinition level)
        {
            if (level == null) yield break;
            foreach (var r in Records(level.platforms, LevelObjectKind.Platform, d => d.center, d => d.meta ?? (d.meta = new LevelObjectMeta()))) yield return r;
            foreach (var r in Records(level.ramps, LevelObjectKind.Ramp, d => d.basePosition, d => d.meta ?? (d.meta = new LevelObjectMeta()))) yield return r;
            foreach (var r in Records(level.spawns, LevelObjectKind.Spawn, d => d.position, d => d.meta ?? (d.meta = new LevelObjectMeta()))) yield return r;
            foreach (var r in Records(level.pickups, LevelObjectKind.Pickup, d => d.position, d => d.meta ?? (d.meta = new LevelObjectMeta()))) yield return r;
            foreach (var r in Records(level.checkpoints, LevelObjectKind.Checkpoint, d => d.position, d => d.meta ?? (d.meta = new LevelObjectMeta()))) yield return r;
            foreach (var r in Records(level.torches, LevelObjectKind.Torch, d => d.basePosition, d => d.meta ?? (d.meta = new LevelObjectMeta()))) yield return r;
            foreach (var r in Records(level.pedestals, LevelObjectKind.Pedestal, d => d.groundPosition, d => d.meta ?? (d.meta = new LevelObjectMeta()))) yield return r;
            foreach (var r in Records(level.balloons, LevelObjectKind.Balloon, d => d.position, d => d.meta ?? (d.meta = new LevelObjectMeta()))) yield return r;
            foreach (var r in Records(level.waters, LevelObjectKind.Water, d => d.center, d => d.meta ?? (d.meta = new LevelObjectMeta()))) yield return r;
            foreach (var r in Records(level.arenas, LevelObjectKind.Arena, d => d.triggerPosition, d => d.meta ?? (d.meta = new LevelObjectMeta())))
            {
                yield return r;
                var arena = (ArenaDef)r.data;
                if (!arena.enabled) continue;
                yield return Child(r, LevelObjectKind.Gate, r.index, arena, arena.gateMeta ?? (arena.gateMeta = new LevelObjectMeta()),
                    r.path + ".gate", arena.gateClosedPosition,
                    Handle(r.path + ".gateOpenPosition", arena.gateOpenPosition), Handle(r.path + ".gateClosedPosition", arena.gateClosedPosition));
                if (arena.hasExitGate)
                    yield return Child(r, LevelObjectKind.ExitGate, r.index, arena, arena.exitGateMeta ?? (arena.exitGateMeta = new LevelObjectMeta()),
                        r.path + ".exitGate", arena.exitGateClosedPosition,
                        Handle(r.path + ".exitGateOpenPosition", arena.exitGateOpenPosition), Handle(r.path + ".exitGateClosedPosition", arena.exitGateClosedPosition));
                var solar = arena.solarRealm;
                if (solar != null && solar.enabled)
                {
                    string path = r.path + ".solarRealm";
                    yield return Child(r, LevelObjectKind.BossPortal, r.index, solar, solar.meta ?? (solar.meta = new LevelObjectMeta()), path, solar.exteriorCenter,
                        Handle(path + ".exteriorCenter", solar.exteriorCenter), Handle(path + ".realmCenter", solar.realmCenter),
                        Handle(path + ".playerEntryPosition", solar.playerEntryPosition), Handle(path + ".retryPosition", solar.retryPosition),
                        Handle(path + ".enemySpawnPosition", solar.enemySpawnPosition), Handle(path + ".arenaPickupPosition", solar.arenaPickupPosition),
                        Handle(path + ".realmExitPosition", solar.realmExitPosition), Handle(path + ".returnPosition", solar.returnPosition));
                }
            }
            foreach (var r in Records(level.projectileSequences, LevelObjectKind.ProjectileSequence, d => d.progressOrigin, d => d.meta ?? (d.meta = new LevelObjectMeta())))
            {
                yield return r;
                var windows = ((ProjectileSequenceDef)r.data).engagementWindows;
                if (windows == null) continue;
                for (int i = 0; i < windows.Length; i++)
                {
                    var window = windows[i];
                    if (window == null) continue;
                    string path = r.path + ".engagementWindows[" + i + "]";
                    yield return Child(r, LevelObjectKind.ProjectileEngagementWindow, i, window, window.meta ?? (window.meta = new LevelObjectMeta()), path, window.routeStart,
                        Handle(path + ".routeStart", window.routeStart), Handle(path + ".routeEnd", window.routeEnd));
                }
            }
            foreach (var r in Records(level.challengeRoutes, LevelObjectKind.ChallengeRoute, d => d.entryCenter, d => d.meta ?? (d.meta = new LevelObjectMeta()))) yield return r;
            foreach (var r in Records(level.runSplits, LevelObjectKind.RunSplit, d => SplitAnchor(level, d), d => d.meta ?? (d.meta = new LevelObjectMeta()))) yield return r;
            yield return Record(LevelObjectKind.PlayerStart, -1, level, level.playerStartMeta ?? (level.playerStartMeta = new LevelObjectMeta()), level.playerStart);
            if (level.killZone != null)
                yield return Record(LevelObjectKind.KillZone, -1, level.killZone, level.killZone.meta ?? (level.killZone.meta = new LevelObjectMeta()), level.killZone.center);
            if (level.sky != null)
                yield return Record(LevelObjectKind.Sky, -1, level.sky, level.sky.meta ?? (level.sky.meta = new LevelObjectMeta()), Vector3.zero);
            if (level.worldLeaderboard != null)
                yield return Record(LevelObjectKind.WorldLeaderboard, -1, level.worldLeaderboard, level.worldLeaderboard.meta ?? (level.worldLeaderboard.meta = new LevelObjectMeta()), level.worldLeaderboard.position);
        }

        static IEnumerable<LevelObjectRecord> Records<T>(T[] items, LevelObjectKind kind,
            Func<T, Vector3> anchor, Func<T, LevelObjectMeta> meta) where T : class
        {
            if (items == null) yield break;
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null) yield return Record(kind, i, items[i], meta(items[i]), anchor(items[i]));
        }

        static LevelObjectRecord Record(LevelObjectKind kind, int index, object data, LevelObjectMeta meta, Vector3 anchor)
        {
            string typeKey = kind == LevelObjectKind.Spawn ? SpawnTypeKey(((SpawnDef)data).prefabKey) : kind.ToString();
            string path = RootPath(kind, index);
            return new LevelObjectRecord { kind = kind, index = index, data = data, meta = meta,
                anchor = anchor, typeKey = typeKey, path = path };
        }

        public static string SpawnTypeKey(string prefabKey)
        {
            string typeKey;
            EnemyFamilies.TryGetValue(prefabKey ?? "", out typeKey);
            return typeKey;
        }

        static string RootPath(LevelObjectKind kind, int index)
        {
            switch (kind)
            {
                case LevelObjectKind.Platform: return "platforms[" + index + "]";
                case LevelObjectKind.Ramp: return "ramps[" + index + "]";
                case LevelObjectKind.Spawn: return "spawns[" + index + "]";
                case LevelObjectKind.Pickup: return "pickups[" + index + "]";
                case LevelObjectKind.Checkpoint: return "checkpoints[" + index + "]";
                case LevelObjectKind.Torch: return "torches[" + index + "]";
                case LevelObjectKind.Pedestal: return "pedestals[" + index + "]";
                case LevelObjectKind.Balloon: return "balloons[" + index + "]";
                case LevelObjectKind.Water: return "waters[" + index + "]";
                case LevelObjectKind.Arena: return "arenas[" + index + "]";
                case LevelObjectKind.ProjectileSequence: return "projectileSequences[" + index + "]";
                case LevelObjectKind.ChallengeRoute: return "challengeRoutes[" + index + "]";
                case LevelObjectKind.RunSplit: return "runSplits[" + index + "]";
                case LevelObjectKind.PlayerStart: return "playerStart";
                case LevelObjectKind.WorldLeaderboard: return "worldLeaderboard";
                case LevelObjectKind.Sky: return "sky";
                case LevelObjectKind.KillZone: return "killZone";
                default: return "";
            }
        }

        static LevelObjectAnchor Handle(string path, Vector3 position)
        {
            return new LevelObjectAnchor { path = path, position = position };
        }

        static LevelObjectRecord Child(LevelObjectRecord owner, LevelObjectKind kind, int index,
            object data, LevelObjectMeta meta, string path, Vector3 anchor, params LevelObjectAnchor[] anchors)
        {
            return new LevelObjectRecord { owner = owner, kind = kind, index = index, data = data, meta = meta,
                path = path, anchor = anchor, anchors = anchors, typeKey = kind.ToString() };
        }

        static Vector3 SplitAnchor(LevelDefinition level, RunSplitDef split)
        {
            var matches = (level.spawns ?? new SpawnDef[0]).Where(s => s != null &&
                !string.IsNullOrWhiteSpace(split.endSpawnerName) && s.name == split.endSpawnerName).ToArray();
            return matches.Length == 1 ? matches[0].position : new Vector3(float.NaN, float.NaN, float.NaN);
        }

        /// <summary>Assign blank IDs only. Errors leave existing IDs intact for explicit repair.</summary>
        public static ZoneAssignmentReport AssignZones(LevelDefinition level)
        {
            var report = new ZoneAssignmentReport();
            if (level == null) { report.errors.Add("Level definition is missing."); return report; }
            var zones = ValidZones(level.zones, report);
            var records = Enumerate(level).ToArray();
            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (string.IsNullOrEmpty(record.typeKey))
                    report.errors.Add(record.path + ": Unknown enemy prefab key '" + ((SpawnDef)record.data).prefabKey + "'.");
                string id = record.meta.objectId;
                if (string.IsNullOrEmpty(id)) continue;
                if (!usedIds.Add(id)) report.errors.Add("Duplicate object ID: " + id);
                if (!ValidId(record, id)) report.errors.Add(Label(record) + ": malformed or reserved object ID '" + id + "'.");
            }

            foreach (var record in records)
            {
                if (string.IsNullOrEmpty(record.typeKey)) continue;
                string reserved = GlobalId(record.kind);
                if (reserved != null)
                {
                    AssignReserved(record, reserved, usedIds, report);
                    continue;
                }
                // Disabled presentation data stays editable but has no physical zone requirement.
                if (record.kind == LevelObjectKind.WorldLeaderboard && !((WorldLeaderboardDef)record.data).enabled) continue;
                ZoneDef zone;
                if (record.owner != null)
                {
                    zone = ResolveZone(record.owner, zones, report);
                    if (zone != null && !string.IsNullOrEmpty(record.meta.zoneIdOverride))
                    {
                        if (record.meta.zoneIdOverride != zone.zoneId)
                        { report.errors.Add(Label(record) + ": child override conflicts with owner zone."); continue; }
                        report.warnings.Add(Label(record) + ": explicit child override matches owner zone '" + zone.zoneId + "'.");
                    }
                }
                else if (record.kind == LevelObjectKind.RunSplit)
                {
                    var split = (RunSplitDef)record.data;
                    var owners = records.Where(r => r.kind == LevelObjectKind.Spawn &&
                        !string.IsNullOrWhiteSpace(split.endSpawnerName) && ((SpawnDef)r.data).name == split.endSpawnerName).ToArray();
                    if (owners.Length != 1)
                    {
                        report.errors.Add(Label(record) + ": end spawner must resolve to exactly one spawn.");
                        continue;
                    }
                    zone = ResolveZone(owners[0], zones, report);
                    if (zone != null && !string.IsNullOrEmpty(record.meta.zoneIdOverride) && record.meta.zoneIdOverride != zone.zoneId)
                    {
                        report.errors.Add(Label(record) + ": split override conflicts with end spawner zone '" + zone.zoneId + "'.");
                        continue;
                    }
                    if (zone != null && zone.splitName != split.name)
                    { report.errors.Add(Label(record) + ": zone splitName '" + zone.splitName + "' does not match split '" + split.name + "'."); continue; }
                    if (zone != null && !string.IsNullOrEmpty(record.meta.zoneIdOverride))
                        report.warnings.Add(Label(record) + ": explicit split override matches end spawner zone '" + zone.zoneId + "'.");
                }
                else zone = ResolveZone(record, zones, report);
                if (zone == null || !string.IsNullOrEmpty(record.meta.objectId)) continue;
                string prefix = zone.zoneId + "." + record.typeKey;
                if (record.index < 0) { AssignReserved(record, prefix, usedIds, report); continue; }
                int suffix = 1;
                string candidate;
                do { candidate = prefix + "." + suffix.ToString("D2", CultureInfo.InvariantCulture); suffix++; }
                while (usedIds.Contains(candidate));
                record.meta.objectId = candidate;
                usedIds.Add(candidate);
            }
            return report;
        }

        static List<ZoneDef> ValidZones(ZoneDef[] source, ZoneAssignmentReport report)
        {
            var zones = new List<ZoneDef>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var zone in source ?? new ZoneDef[0])
            {
                if (zone == null) { report.errors.Add("Null zone definition."); continue; }
                if (!ids.Add(zone.zoneId ?? "")) duplicates.Add(zone.zoneId ?? "");
                if (string.IsNullOrEmpty(zone.zoneId) || !Token.IsMatch(zone.zoneId) || zone.zoneId == "Level")
                { report.errors.Add("Invalid zone ID: '" + zone.zoneId + "'."); continue; }
                if (!Finite(zone.center) || !Finite(zone.size) || zone.size.x <= 0 || zone.size.y <= 0 || zone.size.z <= 0)
                { report.errors.Add("Zone " + zone.zoneId + ": bounds must be finite with positive size."); continue; }
                zones.Add(zone);
            }
            foreach (var duplicate in duplicates.OrderBy(id => id, StringComparer.Ordinal)) report.errors.Add("Duplicate zone ID: " + duplicate);
            return zones.Where(z => !duplicates.Contains(z.zoneId)).OrderBy(z => z.order)
                .ThenBy(z => z.zoneId, StringComparer.Ordinal).ToList();
        }

        static ZoneDef ResolveZone(LevelObjectRecord record, List<ZoneDef> zones, ZoneAssignmentReport report)
        {
            if (!Finite(record.anchor)) { report.errors.Add(Label(record) + ": anchor must be finite."); return null; }
            if (!string.IsNullOrEmpty(record.meta.zoneIdOverride))
            {
                var explicitZone = zones.FirstOrDefault(z => z.zoneId == record.meta.zoneIdOverride);
                if (explicitZone == null) report.errors.Add(Label(record) + ": invalid zone override '" + record.meta.zoneIdOverride + "'.");
                else report.warnings.Add(Label(record) + ": explicit zone override '" + explicitZone.zoneId + "'.");
                return explicitZone;
            }
            var matches = zones.Where(z => new Bounds(z.center, z.size).Contains(record.anchor)).ToArray();
            if (matches.Length != 1)
            {
                report.errors.Add(Label(record) + (matches.Length == 0 ? ": no zone contains anchor." : ": multiple zones contain anchor (" + string.Join(", ", matches.Select(z => z.zoneId)) + ")."));
                return null;
            }
            var zone = matches[0];
            Vector3 half = zone.size * 0.5f;
            Vector3 delta = record.anchor - zone.center;
            if (Mathf.Min(half.x - Mathf.Abs(delta.x), Mathf.Min(half.y - Mathf.Abs(delta.y), half.z - Mathf.Abs(delta.z))) <= BoundaryWarningDistance)
                report.warnings.Add(Label(record) + ": anchor is near zone boundary '" + zone.zoneId + "'.");
            return zone;
        }

        static void AssignReserved(LevelObjectRecord record, string id, HashSet<string> usedIds, ZoneAssignmentReport report)
        {
            if (!string.IsNullOrEmpty(record.meta.objectId)) return;
            if (!usedIds.Add(id)) { report.errors.Add(Label(record) + ": reserved object ID already used: " + id); return; }
            record.meta.objectId = id;
        }

        static string GlobalId(LevelObjectKind kind)
        {
            if (kind == LevelObjectKind.Sky) return "Level.Sky";
            if (kind == LevelObjectKind.KillZone) return "Level.KillZone";
            return null;
        }

        static bool ValidId(LevelObjectRecord record, string id)
        {
            string global = GlobalId(record.kind);
            if (global != null) return id == global;
            if (record.index < 0)
            {
                string[] parts = id.Split('.');
                return parts.Length == 2 && Token.IsMatch(parts[0]) && parts[0] != "Level" && parts[1] == record.typeKey;
            }
            return ObjectId.IsMatch(id) && !id.StartsWith("Level.", StringComparison.Ordinal) && id.Split('.')[1] == record.typeKey;
        }

        static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        static string Label(LevelObjectRecord record)
        {
            return string.IsNullOrEmpty(record.meta.objectId) ? record.kind + " (" + record.path + ")" : record.meta.objectId;
        }
    }
}
