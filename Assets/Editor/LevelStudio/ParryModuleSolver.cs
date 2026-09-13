using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    public sealed class ParryModuleSolver
    {
        const string DataFolder = "Assets/Data/Enemies/parkour_enemies/";

        public ParryModuleResult Solve(ParryCaptureFile capture, LevelDefinition stage, ParrySolverSettings settings)
        {
            var result = new ParryModuleResult();
            settings = settings ?? new ParrySolverSettings();
            if (capture == null || stage == null) return result;
            var beats = capture.desiredBeats ?? new List<DesiredParryBeat>();
            var spawns = new List<SpawnDef>();
            int index = 0;
            while (index < beats.Count)
            {
                int count = IsHeavyPhrase(beats, index, settings) ? 3 : 1;
                string key = count == 3 ? "pshooter_enemy02" : "pshooter_enemy01";
                var spawn = FitSpawn(beats[index], stage, settings, key, spawns.Count + 1);
                if (spawn != null) spawns.Add(spawn);
                for (int j = 0; j < count; j++) result.report.beats.Add(FitBeat(beats[index + j], spawn, key));
                index += count;
            }
            result.module.zoneId = beats.Count > 0 ? beats[0].zoneId : "";
            result.module.spawns = spawns.ToArray();
            result.success = result.report.beats.Count == beats.Count && result.report.beats.All(x => x.satisfied);
            return result;
        }

        static bool IsHeavyPhrase(List<DesiredParryBeat> beats, int index, ParrySolverSettings settings)
        {
            if (index + 2 >= beats.Count) return false;
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(DataFolder + "pshooter_enemy02.asset");
            float cadence = data != null ? data.projectileBurstInterval : .4f;
            return Mathf.Abs(beats[index + 1].captureSeconds - beats[index].captureSeconds - cadence) <= settings.heavyCadenceTolerance &&
                   Mathf.Abs(beats[index + 2].captureSeconds - beats[index + 1].captureSeconds - cadence) <= settings.heavyCadenceTolerance;
        }

        static SpawnDef FitSpawn(DesiredParryBeat beat, LevelDefinition stage, ParrySolverSettings settings, string key, int number)
        {
            Vector3 forward = beat.lookDirection.sqrMagnitude > .01f ? beat.lookDirection.normalized : beat.velocity.sqrMagnitude > .01f ? beat.velocity.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 position = beat.position + right * settings.perchDistance * side + Vector3.up * settings.perchHeight;
                if (!InZone(stage, beat.zoneId, position)) continue;
                string id = (string.IsNullOrEmpty(beat.zoneId) ? "T0" : beat.zoneId) + ".GeneratedShooter." + number.ToString("D2");
                return new SpawnDef { meta = new LevelObjectMeta { objectId = id, zoneIdOverride = beat.zoneId }, name = id, prefabKey = key, position = position, yaw = Quaternion.LookRotation(beat.position - position).eulerAngles.y };
            }
            return null;
        }

        static ParryBeatFit FitBeat(DesiredParryBeat beat, SpawnDef spawn, string key)
        {
            if (spawn == null) return new ParryBeatFit { ordinal = beat.ordinal, enemyKey = key, failure = "No legal perch inside zone bounds." };
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(DataFolder + key + ".asset");
            float speed = data != null ? data.projectileSpeed : 36f;
            float lead = data != null ? data.projectileLead : 1f;
            float homing = data != null ? data.projectileHomingDegPerSec : 0f;
            var plan = ProjectileFlightMath.Plan(spawn.position + Vector3.up * 1.5f, beat.position, beat.velocity, speed, lead, homing, 0f, .6f, .2f, 3f);
            bool ready = plan.IsReady;
            Vector3 toShooter = (spawn.position - beat.position).normalized;
            float angle = Vector3.Angle(beat.lookDirection.sqrMagnitude > .01f ? beat.lookDirection : Vector3.forward, toShooter);
            return new ParryBeatFit { ordinal = beat.ordinal, enemyKey = key, objectId = spawn.meta.objectId, timeErrorSeconds = ready ? 0f : float.PositiveInfinity, spatialErrorMetres = 0f, viewAngleDegrees = angle, confidence = ready ? Mathf.Clamp01(1f - angle / 180f) : 0f, visible = true, satisfied = ready, failure = ready ? "" : plan.readiness.ToString() };
        }

        static bool InZone(LevelDefinition stage, string zoneId, Vector3 position)
        {
            var zone = (stage.zones ?? new ZoneDef[0]).FirstOrDefault(x => x != null && x.zoneId == zoneId);
            return zone != null && new Bounds(zone.center, zone.size).Contains(position);
        }

        public static void ApplyToDraft(ParryModuleResult result, LevelDraft draft, string zoneId)
        {
            if (result == null || !result.success || draft == null || draft.definition == null) return;
            Undo.RecordObject(draft.definition, "Generate Parry Module");
            var existing = draft.definition.spawns ?? new SpawnDef[0];
            draft.definition.spawns = existing.Concat(result.module.spawns.Where(x => x.meta != null && x.meta.zoneIdOverride == zoneId)).ToArray();
            var sequences = draft.definition.projectileSequences ?? new ProjectileSequenceDef[0];
            draft.definition.projectileSequences = sequences.Concat(result.module.sequences ?? new ProjectileSequenceDef[0]).ToArray();
            EditorUtility.SetDirty(draft.definition);
        }
    }
}
