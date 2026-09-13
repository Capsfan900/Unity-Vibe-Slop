using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>Undo-aware mutations for Level Studio's catalog records.</summary>
    public sealed class LevelStudioSceneTool
    {
        readonly LevelDefinition definition;
        static string clipboardJson;
        static LevelObjectKind clipboardKind;
        static bool hasClipboard;

        public LevelStudioSceneTool(LevelDefinition definition)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public static bool Supports(LevelObjectKind kind)
        {
            switch (kind)
            {
                case LevelObjectKind.Platform: case LevelObjectKind.Ramp: case LevelObjectKind.Spawn:
                case LevelObjectKind.Pickup: case LevelObjectKind.Checkpoint: case LevelObjectKind.Torch:
                case LevelObjectKind.Pedestal: case LevelObjectKind.Balloon: case LevelObjectKind.Water:
                case LevelObjectKind.Arena: case LevelObjectKind.ProjectileSequence: case LevelObjectKind.ChallengeRoute:
                case LevelObjectKind.RunSplit: case LevelObjectKind.PlayerStart: case LevelObjectKind.KillZone:
                case LevelObjectKind.Sky: case LevelObjectKind.WorldLeaderboard: case LevelObjectKind.Gate:
                case LevelObjectKind.ExitGate: case LevelObjectKind.BossPortal:
                case LevelObjectKind.ProjectileEngagementWindow: return true;
                default: return false;
            }
        }

        public void ApplyTransform(LevelObjectRecord record, Matrix4x4 matrix)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            Undo.RecordObject(definition, "Edit Level Object");
            Vector3 position = matrix.GetColumn(3), scale = matrix.lossyScale;
            float yaw = matrix.rotation.eulerAngles.y;
            var platform = record.data as PlatformDef; if (platform != null) { platform.center = position; platform.size = Abs(scale); }
            var ramp = record.data as RampDef; if (ramp != null) { ramp.basePosition = position; ramp.yaw = yaw; ramp.width = Mathf.Abs(scale.x); ramp.thickness = Mathf.Abs(scale.y); ramp.run = Mathf.Abs(scale.z); }
            var spawn = record.data as SpawnDef; if (spawn != null) { spawn.position = position; spawn.yaw = yaw; }
            var pickup = record.data as PickupDef; if (pickup != null) pickup.position = position;
            var checkpoint = record.data as CheckpointDef; if (checkpoint != null) checkpoint.position = position;
            var torch = record.data as TorchDef; if (torch != null) torch.basePosition = position;
            var pedestal = record.data as PedestalDef; if (pedestal != null) { pedestal.groundPosition = position; pedestal.triggerRadius = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) * 0.5f; }
            var balloon = record.data as BalloonDef; if (balloon != null) { balloon.position = position; balloon.radius = MaxAbs(scale) * 0.5f; }
            var water = record.data as WaterDef; if (water != null) { water.center = position; water.size = Abs(scale); }
            var arena = record.data as ArenaDef;
            if (arena != null)
            {
                if (record.kind == LevelObjectKind.Gate) { arena.gateClosedPosition = position; arena.gateSize = Abs(scale); }
                else if (record.kind == LevelObjectKind.ExitGate) { arena.exitGateClosedPosition = position; arena.exitGateSize = Abs(scale); }
                else { arena.triggerPosition = position; arena.triggerSize = Abs(scale); }
            }
            var sequence = record.data as ProjectileSequenceDef; if (sequence != null) sequence.progressOrigin = position;
            var window = record.data as ProjectileEngagementWindowDef; if (window != null) window.routeStart = position;
            var route = record.data as ChallengeRouteDef; if (route != null) { route.entryCenter = position; route.entrySize = Abs(scale); }
            var portal = record.data as SolarRealmDef; if (portal != null) { portal.exteriorCenter = position; portal.exteriorRadius = MaxAbs(scale) * 0.5f; }
            var leaderboard = record.data as WorldLeaderboardDef; if (leaderboard != null) { leaderboard.position = position; leaderboard.yaw = yaw; leaderboard.size = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)); }
            var kill = record.data as KillZoneDef; if (kill != null) { kill.center = position; kill.size = Abs(scale); }
            if (record.kind == LevelObjectKind.PlayerStart) { definition.playerStart = position; definition.playerStartYaw = yaw; }
            if (record.kind == LevelObjectKind.Sky && definition.sky != null) definition.sky.eclipseYawDeg = yaw;
            EditorUtility.SetDirty(definition);
        }

        public static bool TryGetTransform(LevelObjectRecord record, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = record == null ? Vector3.zero : record.anchor; rotation = Quaternion.identity; scale = Vector3.one;
            if (record == null) return false;
            var platform = record.data as PlatformDef; if (platform != null) { position=platform.center; scale=platform.size; return true; }
            var ramp = record.data as RampDef; if (ramp != null) { position=ramp.basePosition; rotation=Quaternion.Euler(0f,ramp.yaw,0f); scale=new Vector3(ramp.width,ramp.thickness,ramp.run); return true; }
            var spawn = record.data as SpawnDef; if (spawn != null) { position=spawn.position; rotation=Quaternion.Euler(0f,spawn.yaw,0f); return true; }
            var pickup = record.data as PickupDef; if (pickup != null) { position=pickup.position; return true; }
            var checkpoint = record.data as CheckpointDef; if (checkpoint != null) { position=checkpoint.position; return true; }
            var torch = record.data as TorchDef; if (torch != null) { position=torch.basePosition; return true; }
            var pedestal = record.data as PedestalDef; if (pedestal != null) { position=pedestal.groundPosition; scale=Vector3.one*pedestal.triggerRadius*2f; return true; }
            var balloon = record.data as BalloonDef; if (balloon != null) { position=balloon.position; scale=Vector3.one*balloon.radius*2f; return true; }
            var water = record.data as WaterDef; if (water != null) { position=water.center; scale=water.size; return true; }
            var arena = record.data as ArenaDef;
            if (arena != null) { if(record.kind==LevelObjectKind.Gate){position=arena.gateClosedPosition;scale=arena.gateSize;} else if(record.kind==LevelObjectKind.ExitGate){position=arena.exitGateClosedPosition;scale=arena.exitGateSize;} else {position=arena.triggerPosition;scale=arena.triggerSize;} return true; }
            var sequence = record.data as ProjectileSequenceDef; if (sequence != null) { position=sequence.progressOrigin; return true; }
            var window = record.data as ProjectileEngagementWindowDef; if (window != null) { position=window.routeStart; return true; }
            var route = record.data as ChallengeRouteDef; if (route != null) { position=route.entryCenter; scale=route.entrySize; return true; }
            var portal = record.data as SolarRealmDef; if (portal != null) { position=portal.exteriorCenter; scale=Vector3.one*portal.exteriorRadius*2f; return true; }
            var leaderboard = record.data as WorldLeaderboardDef; if (leaderboard != null) { position=leaderboard.position; rotation=Quaternion.Euler(0f,leaderboard.yaw,0f); scale=new Vector3(leaderboard.size.x,leaderboard.size.y,1f); return true; }
            var kill = record.data as KillZoneDef; if (kill != null) { position=kill.center; scale=kill.size; return true; }
            if (record.kind == LevelObjectKind.PlayerStart) { var level=(LevelDefinition)record.data; position=level.playerStart; rotation=Quaternion.Euler(0f,level.playerStartYaw,0f); return true; }
            if (record.kind == LevelObjectKind.Sky) { var sky=(SkyDef)record.data; rotation=Quaternion.Euler(0f,sky.eclipseYawDeg,0f); return true; }
            return record.kind == LevelObjectKind.RunSplit;
        }

        public void SetZoneBounds(string zoneId, Bounds bounds)
        {
            var zone = (definition.zones ?? new ZoneDef[0]).Single(x => x != null && x.zoneId == zoneId);
            Undo.RecordObject(definition, "Edit Level Zone");
            zone.center = bounds.center;
            zone.size = Abs(bounds.size);
            EditorUtility.SetDirty(definition);
        }

        public string Duplicate(LevelObjectRecord record)
        {
            string field; int index;
            if (record == null || !TryRootArray(record.path, out field, out index)) return null;
            var serialized = new SerializedObject(definition); var array = serialized.FindProperty(field);
            if (array == null || !array.isArray || index < 0 || index >= array.arraySize) return null;
            Undo.RecordObject(definition, "Duplicate Level Object");
            array.InsertArrayElementAtIndex(index);
            var copy = array.GetArrayElementAtIndex(index + 1);
            string id = NextId(record.meta == null ? "" : record.meta.objectId);
            var idProperty = copy.FindPropertyRelative("meta.objectId"); if (idProperty != null) idProperty.stringValue = id;
            var name = copy.FindPropertyRelative("name"); if (name != null) name.stringValue += " Copy";
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(definition);
            return id;
        }

        public bool Delete(LevelObjectRecord record)
        {
            string field; int index;
            if (record == null || !TryRootArray(record.path, out field, out index)) return false;
            var serialized = new SerializedObject(definition); var array = serialized.FindProperty(field);
            if (array == null || !array.isArray || index < 0 || index >= array.arraySize) return false;
            Undo.RecordObject(definition, "Delete Level Object");
            array.DeleteArrayElementAtIndex(index);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(definition);
            return true;
        }

        public bool Copy(LevelObjectRecord record)
        {
            if (record == null || record.data == null || record.owner != null || record.data is LevelDefinition) return false;
            clipboardJson = JsonUtility.ToJson(record.data);
            clipboardKind = record.kind;
            hasClipboard = true;
            return true;
        }

        public bool Paste(LevelObjectRecord record)
        {
            if (!hasClipboard || record == null || record.data == null || record.owner != null || record.data is LevelDefinition || record.kind != clipboardKind) return false;
            string id = record.meta == null ? "" : record.meta.objectId;
            string name = record.meta == null ? "" : record.meta.friendlyName;
            string zone = record.meta == null ? "" : record.meta.zoneIdOverride;
            Undo.RecordObject(definition, "Paste Level Object");
            JsonUtility.FromJsonOverwrite(clipboardJson, record.data);
            if (record.meta != null) { record.meta.objectId = id; record.meta.friendlyName = name; record.meta.zoneIdOverride = zone; }
            EditorUtility.SetDirty(definition);
            return true;
        }

        string NextId(string source)
        {
            int dot = source.LastIndexOf('.'); string prefix = dot < 0 ? source : source.Substring(0, dot);
            var used = new HashSet<string>(LevelObjectCatalog.Enumerate(definition).Where(x => x.meta != null).Select(x => x.meta.objectId), StringComparer.Ordinal);
            for (int i = 1; ; i++) { string candidate = prefix + "." + i.ToString("D2", CultureInfo.InvariantCulture); if (!used.Contains(candidate)) return candidate; }
        }

        static bool TryRootArray(string path, out string field, out int index)
        {
            field = null; index = -1;
            if (string.IsNullOrEmpty(path) || path.IndexOf('.') >= 0) return false;
            int open = path.IndexOf('['), close = path.IndexOf(']');
            if (open <= 0 || close != path.Length - 1) return false;
            field = path.Substring(0, open);
            return int.TryParse(path.Substring(open + 1, close - open - 1), NumberStyles.None, CultureInfo.InvariantCulture, out index);
        }

        static Vector3 Abs(Vector3 value) { return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z)); }
        static float MaxAbs(Vector3 value) { return Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z))); }
    }
}
