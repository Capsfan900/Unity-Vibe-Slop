using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// One-launch parameter sweep for a weapon's GUARD pose, because iterating a pose one headless
    /// run at a time is how an afternoon disappears. Mutates the loaded <see cref="WeaponData"/> IN
    /// MEMORY only — nothing calls SaveAssets, so the shipped asset is untouched and the winner gets
    /// hard-coded into <c>DataFactory</c> by hand, where rule 9 says a shipped value lives.
    ///
    /// <para>Exists because the projection cannot be eyeballed: two guards with near-identical
    /// authored numbers measured 0.0% and 14.3% crosshair (sword vs hammer, 2026-09), purely because
    /// of where each weapon's mass sits along the canted line. Only the rasteriser knows.</para>
    /// </summary>
    public static class WeaponGuardSweep
    {
        /// <summary>Batch entry: <c>-executeMethod VibeGame1.EditorTools.WeaponGuardSweep.Batch</c>.</summary>
        public static void Batch()
        {
            // Regenerate FIRST, in the same launch. The first run of this sweep measured a hammer with
            // "extent 0.34m" — the dagger-era prefab — because a robocopy re-sync had quietly replaced
            // the copy's regenerated assets with the real tree's stale ones. Rule 9 in sweep form: a
            // measurement of an asset is only worth anything when the asset was rebuilt by the same
            // process that measures it.
            DataFactory.CreateAll();
            PrefabFactory.BuildAll();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var sb = new StringBuilder();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            WeaponViewmodel vm; Camera cam;
            GameObject player = WeaponSilhouette.StagePlayer(out vm, out cam);
            if (player == null || vm == null || cam == null)
            {
                Debug.Log("SWEEP FAIL: could not stage player");
                EditorApplication.Exit(1);
                return;
            }

            try
            {
                // Hammer already has a sweep-verified guard (0.44,-0.16,0.80 / -10,46,42 measured
                // crosshair 0.0%, tip +0.10,+0.16). This pass is the kris, whose first 144 candidates
                // ALL failed — so every candidate is printed with the rule it broke, verbose on purpose.
                Sweep(sb, vm, cam, "DevBlade",
                    new float[] { 0.30f, 0.34f, 0.38f },
                    new float[] { -0.08f, -0.14f, -0.20f },
                    new float[] { 0.62f, 0.70f },
                    new float[] { -8f, -14f, -20f },
                    new float[] { 40f, 46f },
                    new float[] { 42f, 52f, 62f, 72f });
            }
            finally { Object.DestroyImmediate(player); }

            string path = Path.Combine(Directory.GetCurrentDirectory(), "guard_sweep.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("sweep written to " + path);
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// A candidate survives when the guard obeys the held-pose contract with margin — an empty
        /// crosshair disc, a tip inside the frame, small coverage — and still READS as a guard:
        /// the silhouette must reach above the fist (top of box above -0.05) or the "stance" is a
        /// weapon dangling at rest.
        /// </summary>
        static void Sweep(StringBuilder sb, WeaponViewmodel vm, Camera cam, string weapon,
                          float[] xs, float[] ys, float[] zs,
                          float[] pitches, float[] yaws, float[] rolls)
        {
            var w = WeaponSilhouette.Weapon(weapon);
            if (w == null) { sb.AppendLine("MISSING " + weapon); return; }
            Pose original = w.guard;
            sb.AppendLine("=== " + weapon + "  (current: " + Fmt(original) + ")");
            int shown = 0, tried = 0;
            foreach (float x in xs) foreach (float y in ys) foreach (float z in zs)
            foreach (float p in pitches) foreach (float yw in yaws) foreach (float r in rolls)
            {
                tried++;
                w.guard = new Pose(new Vector3(x, y, z), new Vector3(p, yw, r));
                var s = WeaponSilhouette.Measure(vm, cam, w, "guard");
                if (!s.valid) continue;
                string why = "";
                if (s.crosshair > 0.0001f) why += " DISC:" + s.crosshair.ToString("0.0%");
                if (!s.tipInFrame) why += " TIP-OFF";
                if (s.coverage > 0.03f) why += " COVER:" + s.coverage.ToString("0.0%");
                if (s.maxY < -0.05f) why += " DROOPED:" + s.maxY.ToString("0.00");
                bool clean = why.Length == 0;
                if (clean) shown++;
                sb.AppendLine((clean ? "  ok   " : "  FAIL ") + Fmt(w.guard) + "  " + s + why);
            }
            w.guard = original;   // in-memory restore; nothing is saved either way
            sb.AppendLine("  " + shown + " / " + tried + " candidates clean");
        }

        static string Fmt(Pose p)
        {
            return string.Format("pos({0:0.00},{1:0.00},{2:0.00}) rot({3:0},{4:0},{5:0})",
                p.pos.x, p.pos.y, p.pos.z, p.euler.x, p.euler.y, p.euler.z);
        }
    }
}
