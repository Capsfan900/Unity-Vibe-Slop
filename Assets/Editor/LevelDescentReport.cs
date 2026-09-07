using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>Checks the descent's sightlines against exact rotated slabs, not ramp bounding boxes.
    /// Complements the platform-only ballistic report; does not claim to simulate homing or human timing.</summary>
    public static class LevelDescentReport
    {
        public static float SurfaceY(RampDef ramp, Vector3 position)
        {
            float along = Vector3.Dot(position - ramp.basePosition, ramp.Heading);
            return ramp.basePosition.y + ramp.rise * Mathf.Clamp01(along / Mathf.Max(0.0001f, ramp.run));
        }

        public static bool Intersects(Vector3 from, Vector3 to, Vector3 center, Vector3 size, Quaternion rotation)
        {
            var inv = Quaternion.Inverse(rotation);
            from = inv * (from - center); to = inv * (to - center);
            Vector3 delta = to - from, half = size * 0.5f;
            float lo = 0f, hi = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(delta[axis]) < 0.00001f)
                {
                    if (from[axis] < -half[axis] || from[axis] > half[axis]) return false;
                    continue;
                }
                float a = (-half[axis] - from[axis]) / delta[axis];
                float b = (half[axis] - from[axis]) / delta[axis];
                lo = Mathf.Max(lo, Mathf.Min(a, b)); hi = Mathf.Min(hi, Mathf.Max(a, b));
                if (lo > hi) return false;
            }
            return true;
        }

        public static string Blocker(LevelDefinition def, Vector3 from, Vector3 to)
        {
            foreach (var p in def.platforms)
                if (Intersects(from, to, p.center, p.size, Quaternion.identity)) return p.name;
            foreach (var r in def.ramps)
                if (Intersects(from, to, r.BoxCenter, r.BoxScale, r.Rotation)) return r.name;
            return null;
        }

        public static List<string> Failures(LevelDefinition def)
        {
            var failures = new List<string>();
            var ramp = def.ramps.SingleOrDefault(r => r.name == "T4_Ramp_Descent");
            if (ramp == null) { failures.Add("Descent ramp missing"); return failures; }
            var runOut = def.platforms.SingleOrDefault(p => p.name == "Boss_Approach");
            if (runOut == null) { failures.Add("Descent run-out missing"); return failures; }
            Vector3 right = Vector3.Cross(Vector3.up, ramp.Heading);
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy03"));
            if (data == null) { failures.Add("Surge turret data missing"); return failures; }
            int count = 0;
            foreach (var s in def.spawns)
            {
                if (!s.name.StartsWith("Spawn_T4_Surge_")) continue;
                count++;
                if (s.prefabKey != "pshooter_enemy03") failures.Add(s.name + ": wrong enemy");
                // Sliding chest and standing chest, at 10/18/26 m before each turret, across a 6 m lane.
                for (int back = 10; back <= 26; back += 8)
                    for (int x = -3; x <= 3; x += 3)
                        foreach (float chest in new[] { 0.6f, 1.2f })
                        {
                            // Before the crest, the entry deck intentionally shelters the player.
                            float along = Mathf.Clamp(Vector3.Dot(s.position - ramp.basePosition, ramp.Heading) - back,
                                                      0.2f, ramp.run);
                            var target = ramp.basePosition + ramp.Heading * along + right * x;
                            target.y = SurfaceY(ramp, target) + chest;
                            var muzzle = s.position + Vector3.up * 1.3f; // ProjectileShooter's actual origin.
                            float distance = Vector3.Distance(muzzle, target);
                            string blocker = Blocker(def, muzzle, target);
                            if (distance < data.projectileMinRange || distance > data.projectileMaxRange || blocker != null)
                                failures.Add(s.name + " -> " + target + ": range=" + distance + " blocker=" + blocker);
                        }
            }
            if (count != 3) failures.Add("Expected 3 descent turrets, found " + count);
            // Preview of the run-out from the crest, with the whole ramp included as an occluder.
            Vector3 crestEye = ramp.basePosition - ramp.Heading * 0.8f + Vector3.up * 1.7f;
            Vector3 exitPreview = runOut.center + Vector3.up * (runOut.size.y * 0.5f + 0.5f);
            string blocked = Blocker(def, crestEye, exitPreview);
            if (blocked != null) failures.Add("Run-out hidden by " + blocked);
            return failures;
        }

        public static string Build(LevelDefinition def)
        {
            var ramp = def.ramps == null ? null : def.ramps.FirstOrDefault(r => r.name == "T4_Ramp_Descent");
            if (ramp == null) return "DESCENT: not authored in this level.\n";
            var failures = Failures(def);
            int turrets = def.spawns.Count(s => s.name.StartsWith("Spawn_T4_Surge_"));
            return string.Format("DESCENT: {0:0.##} m run / {1:0.##} m drop / {2:0.##} m width; {3} surge turrets; exact ramp/box sightlines: ",
                                 ramp.run, -ramp.rise, ramp.width, turrets) +
                (failures.Count == 0 ? "PASS" : string.Join("; ", failures.ToArray())) +
                "\nGeometry only: sustained sliding and projectile arrival timing require the live motor probe.\n";
        }
    }
}
