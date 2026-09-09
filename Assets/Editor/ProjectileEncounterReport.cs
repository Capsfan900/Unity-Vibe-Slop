using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Generic data-level audit for parryable projectile encounters. It accepts any LevelDefinition,
    /// checks route-audit ownership/window integrity, then asks the same contact planner used at runtime whether each
    /// authored route supports a safe arrival at base run, surge/slide and horizontal-clamp speeds.
    /// </summary>
    public static class ProjectileEncounterReport
    {
        static readonly float[] RouteSpeeds = { 11f, 17.6f, 27.5f };

        [MenuItem("VibeGame1/Projectile Encounter Report", priority = 304)]
        public static void Menu()
        {
            string report = Build(LevelArcReport.DefaultLevel);
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "ProjectileEncounterReport.txt"), report);
            Debug.Log("\n" + report);
        }

        public static string Build(string levelPath)
        {
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(levelPath);
            if (def == null) return "FAIL: " + levelPath + " not found.";

            var sb = new StringBuilder();
            var ownership = new Dictionary<string, int>();
            int failures = 0;
            sb.AppendLine("PROJECTILE ENCOUNTER REPORT — " + def.displayName);

            foreach (var sequence in def.projectileSequences ?? new ProjectileSequenceDef[0])
            {
                if (sequence == null) { failures++; sb.AppendLine("FAIL: null sequence"); continue; }
                sb.AppendLine("\n" + sequence.name);
                var members = sequence.spawnerNames ?? new string[0];
                var windows = sequence.engagementWindows ?? new ProjectileEngagementWindowDef[0];
                if (members.Length == 0 || windows.Length == 0)
                {
                    failures++;
                    sb.AppendLine("  FAIL: sequence needs members and bounded engagement windows");
                    continue;
                }

                foreach (string member in members)
                {
                    if (!ownership.ContainsKey(member)) ownership[member] = 0;
                    ownership[member]++;
                    var spawn = (def.spawns ?? new SpawnDef[0]).FirstOrDefault(s => s != null && s.name == member);
                    if (spawn == null)
                    {
                        failures++; sb.AppendLine("  FAIL: missing spawn " + member); continue;
                    }
                    var data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(spawn.prefabKey));
                    if (data == null || !data.shootsProjectiles)
                    {
                        failures++; sb.AppendLine("  FAIL: " + member + " does not resolve to projectile EnemyData");
                        continue;
                    }

                    var ownedWindows = windows.Where(w => w != null && w.spawnerName == member).ToArray();
                    if (ownedWindows.Length == 0)
                    {
                        failures++; sb.AppendLine("  FAIL: " + member + " has no route window"); continue;
                    }
                    foreach (var window in ownedWindows)
                    {
                        if (!ProjectileEngagementMath.IsValid(window))
                        {
                            failures++; sb.AppendLine("  FAIL: invalid window for " + member); continue;
                        }
                        Vector3 muzzle = spawn.position + Vector3.up * 1.3f;
                        foreach (float speed in RouteSpeeds)
                        {
                            if (!SupportsSpeed(window, muzzle, data, speed))
                            {
                                failures++;
                                sb.AppendLine("  FAIL: " + member + " has no safe in-window contact at " +
                                              speed.ToString("0.0") + " m/s");
                            }
                            else sb.AppendLine("  READY: " + member + " at " + speed.ToString("0.0") + " m/s");
                        }
                    }
                }
            }

            foreach (var spawn in (def.spawns ?? new SpawnDef[0]).Where(s => s != null && s.prefabKey != null &&
                                                                          s.prefabKey.StartsWith("pshooter_enemy")))
            {
                int count;
                ownership.TryGetValue(spawn.name, out count);
                if (count != 1)
                {
                    failures++;
                    sb.AppendLine("FAIL: " + spawn.name + " has " + count + " route-audit owners (expected 1)");
                }
            }

            sb.AppendLine("\nVERDICT: " + (failures == 0 ? "PASS" : failures + " failure(s)"));
            return sb.ToString();
        }

        static bool SupportsSpeed(ProjectileEngagementWindowDef window, Vector3 muzzle, EnemyData data,
                                  float routeSpeed)
        {
            Vector2 flat = new Vector2(window.routeEnd.x - window.routeStart.x,
                                       window.routeEnd.z - window.routeStart.z);
            float length = flat.magnitude;
            Vector3 routeDirection = new Vector3(flat.x / length, 0f, flat.y / length);
            float ySlope = (window.routeEnd.y - window.routeStart.y) / length;
            Vector3 velocity = new Vector3(routeDirection.x * routeSpeed, ySlope * routeSpeed,
                                           routeDirection.z * routeSpeed);

            for (int i = 0; i <= 12; i++)
            {
                float progress = length * i / 12f;
                Vector3 chest = PointAt(window, progress, length);
                var plan = ProjectileFlightMath.Plan(muzzle, chest, velocity, data.projectileSpeed,
                    data.projectileLead, data.projectileHomingDegPerSec, ProjectileShooter.SpawnForwardOffset,
                    Projectile.DefaultHitRadius, Projectile.CueLead + ProjectileShooter.CueMargin,
                    Projectile.DefaultMaxLife);
                if (plan.IsReady && ProjectileEngagementMath.AllowsPredictedContact(window, chest, velocity,
                                                                                     plan.contactSeconds))
                    return true;
            }
            return false;
        }

        static Vector3 PointAt(ProjectileEngagementWindowDef window, float progress, float length)
        {
            float t = length > 0.001f ? Mathf.Clamp01(progress / length) : 0f;
            return Vector3.Lerp(window.routeStart, window.routeEnd, t);
        }
    }
}
