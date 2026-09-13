using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    public enum LevelDraftChangeKind { Add, Remove, Transform, Tuning, Zone }
    public sealed class LevelDraftChange { public readonly string zoneId, objectId, fieldPath; public readonly LevelDraftChangeKind kind; internal LevelDraftChange(string zoneId, string objectId, string fieldPath, LevelDraftChangeKind kind) { this.zoneId=zoneId; this.objectId=objectId; this.fieldPath=fieldPath; this.kind=kind; } }
    public sealed class LevelDraftDiffReport { readonly List<LevelDraftChange> changeList = new List<LevelDraftChange>(); public IReadOnlyList<LevelDraftChange> changes { get { return changeList.AsReadOnly(); } } internal List<LevelDraftChange> MutableChanges { get { return changeList; } } public readonly LevelValidationReport beforeValidation, afterValidation; public bool failed { get { return (beforeValidation != null && beforeValidation.HasErrors) || (afterValidation != null && afterValidation.HasErrors); } } internal LevelDraftDiffReport(LevelValidationReport before, LevelValidationReport after) { beforeValidation=before; afterValidation=after; } }

    public static class LevelDraftDiff
    {
        public static LevelDraftDiffReport Compare(LevelDraft draft)
        {
            if (draft == null || draft.definition == null) return Compare(null, null);
            var baseline = ScriptableObject.CreateInstance<LevelDefinition>();
            try { EditorJsonUtility.FromJsonOverwrite(draft.baseDefinitionJson ?? "{}", baseline); return Compare(baseline, draft.definition); }
            finally { UnityEngine.Object.DestroyImmediate(baseline); }
        }

        public static LevelDraftDiffReport Compare(LevelDefinition before, LevelDefinition after)
        {
            var report = new LevelDraftDiffReport(LevelStudioValidator.Validate(before), LevelStudioValidator.Validate(after));
            if (report.failed) return report;
            var leftEntries = LevelDraftInventory.Entries(before).ToList(); LevelDraftInventory.ResolveZones(before, leftEntries);
            var rightEntries = LevelDraftInventory.Entries(after).ToList(); LevelDraftInventory.ResolveZones(after, rightEntries);
            var left = leftEntries.ToDictionary(e => e.Id, StringComparer.Ordinal);
            var right = rightEntries.ToDictionary(e => e.Id, StringComparer.Ordinal);
            foreach (var id in left.Keys.Union(right.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                LevelDraftInventory.Entry a, b; bool hasA = left.TryGetValue(id, out a), hasB = right.TryGetValue(id, out b);
                if (!hasA) Add(report, b, b.Id.StartsWith("$zone/", StringComparison.Ordinal) ? LevelDraftChangeKind.Zone : LevelDraftChangeKind.Add, b.path);
                else if (!hasB) Add(report, a, a.Id.StartsWith("$zone/", StringComparison.Ordinal) ? LevelDraftChangeKind.Zone : LevelDraftChangeKind.Remove, a.path);
                else CompareEntry(report, a, b);
            }
            CompareOrder(report, before, after);
            report.MutableChanges.Sort((a, b) => string.Compare(a.zoneId + "\u001f" + a.objectId + "\u001f" + a.fieldPath + "\u001f" + a.kind, b.zoneId + "\u001f" + b.objectId + "\u001f" + b.fieldPath + "\u001f" + b.kind, StringComparison.Ordinal));
            return report;
        }

        static void CompareEntry(LevelDraftDiffReport report, LevelDraftInventory.Entry a, LevelDraftInventory.Entry b)
        {
            if (a.Id.StartsWith("$zone/", StringComparison.Ordinal))
            {
                if (JsonUtility.ToJson(a.data) != JsonUtility.ToJson(b.data)) Add(report, b, LevelDraftChangeKind.Zone, b.path);
                return;
            }
            string beforeOverride = a.meta == null ? "" : a.meta.zoneIdOverride;
            string afterOverride = b.meta == null ? "" : b.meta.zoneIdOverride;
            if (beforeOverride != afterOverride || a.ZoneId != b.ZoneId)
                Add(report, b, LevelDraftChangeKind.Zone, b.path + (beforeOverride != afterOverride ? ".meta.zoneIdOverride" : ".resolvedZone"));
            foreach (var path in SpatialChanges(a.data, b.data, "")) Add(report, b, LevelDraftChangeKind.Transform, b.path + path);
            string beforeName = a.meta == null ? "" : a.meta.friendlyName;
            string afterName = b.meta == null ? "" : b.meta.friendlyName;
            if (beforeName != afterName || HasTuningDifference(a.data, b.data)) Add(report, b, LevelDraftChangeKind.Tuning, b.path);
        }
        static bool HasTuningDifference(object a, object b)
        {
            if (a == null || b == null) return a != b;
            if (a.GetType() != b.GetType()) return true;
            foreach (var field in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object x = field.GetValue(a), y = field.GetValue(b);
                if (field.FieldType == typeof(Vector2) || field.FieldType == typeof(Vector3)) continue;
                if (field.FieldType == typeof(LevelObjectMeta)) continue;
                var left = x as Array; var right = y as Array;
                if (left != null || right != null)
                {
                    if (left == null || right == null || left.Length != right.Length) return true;
                    for (int i = 0; i < left.Length; i++) if (HasTuningDifference(left.GetValue(i), right.GetValue(i))) return true;
                    continue;
                }
                if (x != null && y != null && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum && field.FieldType != typeof(string))
                { if (HasTuningDifference(x, y)) return true; }
                else if (!object.Equals(x, y)) return true;
            }
            return false;
        }
        static IEnumerable<string> SpatialChanges(object a, object b, string path)
        {
            if (a == null || b == null || a.GetType() != b.GetType()) yield break;
            foreach (var field in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object x = field.GetValue(a), y = field.GetValue(b); string child = path + "." + field.Name;
                if (x is Vector3 && y is Vector3) { if ((Vector3)x != (Vector3)y) yield return child; continue; }
                if (x is Vector2 && y is Vector2) { if ((Vector2)x != (Vector2)y) yield return child; continue; }
                if (x != null && y != null && !x.GetType().IsPrimitive && !(x is string) && !(x is Array))
                    foreach (var nested in SpatialChanges(x, y, child)) yield return nested;
            }
        }
        static void CompareOrder(LevelDraftDiffReport report, LevelDefinition before, LevelDefinition after)
        {
            foreach (string name in LevelDraftInventory.Collections)
                if (!Sequence(before, name).SequenceEqual(Sequence(after, name))) report.MutableChanges.Add(new LevelDraftChange("$level", "$level", name + ".order", LevelDraftChangeKind.Tuning));
        }
        static IEnumerable<string> Sequence(LevelDefinition level, string field)
        {
            if (level == null) return new string[0];
            return LevelDraftInventory.EntriesForCollection(level, field).Select(e => e.Id);
        }
        static void Add(LevelDraftDiffReport report, LevelDraftInventory.Entry entry, LevelDraftChangeKind kind, string path) { report.MutableChanges.Add(new LevelDraftChange(entry.ZoneId, entry.Id, path, kind)); }
    }

    internal static class LevelDraftInventory
    {
        internal sealed class Entry { internal string id, path, kind, zoneId; internal LevelObjectMeta meta; internal object data; internal bool global, primary; internal HashSet<string> ids; internal string Id { get { return id ?? (meta == null ? "" : meta.objectId ?? ""); } } internal string ZoneId { get { return zoneId ?? "Unassigned"; } } }
        internal static readonly string[] Collections = { "zones", "platforms", "ramps", "spawns", "pickups", "checkpoints", "torches", "pedestals", "balloons", "waters", "arenas", "projectileSequences", "challengeRoutes", "runSplits" };
        internal static IEnumerable<Entry> Entries(LevelDefinition d)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal); if (d == null) yield break;
            yield return new Entry { id = "$level", kind = "$level", path = "$level", data = new LevelRootData(d), ids = ids, global = true };
            for (int z = 0; d.zones != null && z < d.zones.Length; z++) if (d.zones[z] != null) yield return new Entry { id = "$zone/" + (d.zones[z].zoneId ?? ""), kind = "$zone", path = "zones[" + z + "]", data = d.zones[z], ids = ids, global = true };
            foreach (var e in Array(d.platforms, "platforms", x => x.meta)) { e.ids = ids; yield return e; }
            foreach (var e in Array(d.ramps, "ramps", x => x.meta)) { e.ids = ids; yield return e; }
            foreach (var e in Array(d.spawns, "spawns", x => x.meta)) { e.ids = ids; e.kind = LevelObjectCatalog.SpawnTypeKey(((SpawnDef)e.data).prefabKey); yield return e; }
            foreach (var e in Array(d.pickups, "pickups", x => x.meta)) { e.ids = ids; yield return e; }
            foreach (var e in Array(d.checkpoints, "checkpoints", x => x.meta)) { e.ids = ids; yield return e; }
            foreach (var e in Array(d.torches, "torches", x => x.meta)) { e.ids = ids; yield return e; }
            foreach (var e in Array(d.pedestals, "pedestals", x => x.meta)) { e.ids = ids; yield return e; }
            foreach (var e in Array(d.balloons, "balloons", x => x.meta)) { e.ids = ids; yield return e; }
            foreach (var e in Array(d.waters, "waters", x => x.meta)) { e.ids = ids; yield return e; }
            for (int i = 0; d.arenas != null && i < d.arenas.Length; i++) if (d.arenas[i] != null)
            {
                var a = d.arenas[i]; string path = "arenas[" + i + "]";
                var arena = New(a.meta, new ArenaRootData(a), path, ids); arena.primary = a.enabled; yield return arena;
                var gate = New(a.gateMeta, new ArenaGateData(a), path + ".gate", ids); gate.primary = a.enabled; yield return gate;
                var exit = New(a.exitGateMeta, new ArenaExitGateData(a), path + ".exitGate", ids); exit.primary = a.enabled && a.hasExitGate; yield return exit;
                if (a.solarRealm != null) { var solar = New(a.solarRealm.meta, a.solarRealm, path + ".solarRealm", ids); solar.primary = a.enabled && a.solarRealm.enabled; yield return solar; }
            }
            for (int i = 0; d.projectileSequences != null && i < d.projectileSequences.Length; i++) if (d.projectileSequences[i] != null)
            {
                var s = d.projectileSequences[i]; string path = "projectileSequences[" + i + "]";
                yield return New(s.meta, new ProjectileSequenceRootData(s), path, ids);
                for (int w = 0; s.engagementWindows != null && w < s.engagementWindows.Length; w++) if (s.engagementWindows[w] != null)
                    yield return New(s.engagementWindows[w].meta, s.engagementWindows[w], path + ".engagementWindows[" + w + "]", ids);
            }
            foreach (var e in Array(d.challengeRoutes, "challengeRoutes", x => x.meta)) { e.ids = ids; yield return e; }
            foreach (var e in Array(d.runSplits, "runSplits", x => x.meta)) { e.ids = ids; yield return e; }
            yield return New(d.playerStartMeta, new PlayerStartData(d.playerStart, d.playerStartYaw), "playerStart", ids); if (d.killZone != null) yield return New(d.killZone.meta, d.killZone, "killZone", ids, true); if (d.sky != null) yield return New(d.sky.meta, d.sky, "sky", ids, true); if (d.worldLeaderboard != null) { var leaderboard = New(d.worldLeaderboard.meta, d.worldLeaderboard, "worldLeaderboard", ids); leaderboard.primary = d.worldLeaderboard.enabled; yield return leaderboard; }
        }
        internal static IEnumerable<Entry> EntriesForCollection(LevelDefinition d, string field) { return Entries(d).Where(e => e.path.StartsWith(field + "[", StringComparison.Ordinal)); }
        internal static void ResolveZones(LevelDefinition definition, IList<Entry> entries)
        {
            var byPath = entries.ToDictionary(e => e.path, StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry.global) { entry.zoneId = "$level"; continue; }
                if (entry.meta != null && !string.IsNullOrEmpty(entry.meta.zoneIdOverride)) { entry.zoneId = entry.meta.zoneIdOverride; continue; }
                int child = entry.path.IndexOf('.');
                if (child > 0) { Entry owner; if (byPath.TryGetValue(entry.path.Substring(0, child), out owner)) { entry.zoneId = owner.zoneId; continue; } }
                Vector3 anchor;
                if (Anchor(entry.data, out anchor)) entry.zoneId = ContainingZone(definition, anchor);
                else entry.zoneId = "Unassigned";
            }
            foreach (var entry in entries) if (entry.zoneId == null) entry.zoneId = "Unassigned";
        }
        internal static bool Anchor(object data, out Vector3 value)
        {
            value = Vector3.zero; if (data == null) return false;
            foreach (string field in new[] { "center", "basePosition", "position", "groundPosition", "triggerPosition", "progressOrigin", "entryCenter" })
            { var info = data.GetType().GetField(field); if (info != null && info.FieldType == typeof(Vector3)) { value = (Vector3)info.GetValue(data); return true; } }
            var start = data as PlayerStartData; if (start != null) { value = start.position; return true; }
            return false;
        }
        static string ContainingZone(LevelDefinition definition, Vector3 anchor)
        {
            var matches = (definition.zones ?? new ZoneDef[0]).Where(z => z != null && new Bounds(z.center, z.size).Contains(anchor)).OrderBy(z => z.order).ThenBy(z => z.zoneId, StringComparer.Ordinal).ToArray();
            return matches.Length == 1 ? matches[0].zoneId : "Unassigned";
        }
        static IEnumerable<Entry> Array<T>(T[] source, string field, Func<T, LevelObjectMeta> meta) where T : class { for (int i=0; source != null && i<source.Length; i++) if (source[i] != null) yield return New(meta(source[i]), source[i], field + "[" + i + "]", null); }
        static Entry New(LevelObjectMeta meta, object data, string path, HashSet<string> ids, bool global = false) { return new Entry { meta = meta, data = data, path = path, kind = Kind(path), ids = ids, global = global, primary = !global }; }
        static string Kind(string path)
        {
            if (path == "playerStart") return "PlayerStart"; if (path == "worldLeaderboard") return "WorldLeaderboard"; if (path == "sky") return "Sky"; if (path == "killZone") return "KillZone";
            if (path.EndsWith(".gate", StringComparison.Ordinal)) return "Gate"; if (path.EndsWith(".exitGate", StringComparison.Ordinal)) return "ExitGate"; if (path.EndsWith(".solarRealm", StringComparison.Ordinal)) return "BossPortal"; if (path.Contains(".engagementWindows[")) return "ProjectileEngagementWindow";
            int stop = path.IndexOf('['); string root = stop < 0 ? path : path.Substring(0, stop);
            if (root == "platforms") return "Platform"; if (root == "ramps") return "Ramp"; if (root == "spawns") return "Spawn"; if (root == "pickups") return "Pickup"; if (root == "checkpoints") return "Checkpoint"; if (root == "torches") return "Torch"; if (root == "pedestals") return "Pedestal"; if (root == "balloons") return "Balloon"; if (root == "waters") return "Water"; if (root == "arenas") return "Arena"; if (root == "projectileSequences") return "ProjectileSequence"; if (root == "challengeRoutes") return "ChallengeRoute"; return "RunSplit";
        }
        [Serializable] sealed class PlayerStartData { public Vector3 position; public float yaw; public PlayerStartData(Vector3 p, float y) { position = p; yaw = y; } }
        [Serializable] sealed class LevelRootData { public string levelId, displayName, sceneName; public float parTime; public int orderIndex, requiredRunSouls, requiredRegularKills; public RunGradeBonusDef gradeBonuses; public LevelRootData(LevelDefinition d) { levelId=d.levelId; displayName=d.displayName; sceneName=d.sceneName; parTime=d.parTime; orderIndex=d.orderIndex; requiredRunSouls=d.requiredRunSouls; requiredRegularKills=d.requiredRegularKills; gradeBonuses=d.gradeBonuses; } }
        [Serializable] sealed class ArenaRootData { public bool enabled; public string triggerName, clearSpawnerName; public Vector3 triggerPosition, triggerSize; public ArenaRootData(ArenaDef a) { enabled=a.enabled; triggerName=a.triggerName; clearSpawnerName=a.clearSpawnerName; triggerPosition=a.triggerPosition; triggerSize=a.triggerSize; } }
        [Serializable] sealed class ArenaGateData { public string gateName, gateMaterialKey; public Vector3 gateSize, gateOpenPosition, gateClosedPosition; public ArenaGateData(ArenaDef a) { gateName=a.gateName; gateMaterialKey=a.gateMaterialKey; gateSize=a.gateSize; gateOpenPosition=a.gateOpenPosition; gateClosedPosition=a.gateClosedPosition; } }
        [Serializable] sealed class ArenaExitGateData { public bool hasExitGate; public string exitGateName, exitGateMaterialKey; public Vector3 exitGateSize, exitGateClosedPosition, exitGateOpenPosition; public ArenaExitGateData(ArenaDef a) { hasExitGate=a.hasExitGate; exitGateName=a.exitGateName; exitGateMaterialKey=a.exitGateMaterialKey; exitGateSize=a.exitGateSize; exitGateClosedPosition=a.exitGateClosedPosition; exitGateOpenPosition=a.exitGateOpenPosition; } }
        [Serializable] sealed class ProjectileSequenceRootData
        {
            public string name;
            public string[] spawnerNames;
            public float recoveryGap, readinessTimeout, shotResolutionTimeout, firstMemberAcquireDelay;
            public int repeatFromIndex;
            public Vector3 progressOrigin, progressDirection;
            public float[] memberProgressGates;
            public ProjectileSequenceRootData(ProjectileSequenceDef s)
            {
                name=s.name; spawnerNames=s.spawnerNames; recoveryGap=s.recoveryGap; readinessTimeout=s.readinessTimeout;
                shotResolutionTimeout=s.shotResolutionTimeout; firstMemberAcquireDelay=s.firstMemberAcquireDelay;
                repeatFromIndex=s.repeatFromIndex; progressOrigin=s.progressOrigin; progressDirection=s.progressDirection;
                memberProgressGates=s.memberProgressGates;
            }
        }
    }
}
