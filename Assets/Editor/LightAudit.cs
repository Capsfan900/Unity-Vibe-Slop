using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// READ-ONLY static audit of the level's realtime lights, written because the backlog carried
    /// "42 point lights in the level, up from 25. No performance measurement has been taken" and the
    /// only measuring tool the project had (<c>PerfProbe</c>) needs play mode.
    ///
    /// <para>This does NOT produce a frame cost. It produces the three numbers a frame cost would be
    /// made of, all of which are decidable statically:</para>
    /// <list type="number">
    /// <item><b>How many lights exist</b>, and how many are point lights with a
    /// <see cref="FlickerLight"/> on them (the ones that also pay a per-frame script cost).</item>
    /// <item><b>How many survive <see cref="FlickerLight"/>'s distance cull</b> at a real camera
    /// position — these are the lights URP must gather, cull and sort every frame.</item>
    /// <item><b>How many actually reach a given point</b>, i.e. how many light spheres overlap it,
    /// against the URP asset's <c>AdditionalLightsPerObjectLimit</c>. Anything past that limit is
    /// dropped by the renderer having contributed nothing, which is the wasteful case: paid for in
    /// culling and in the flicker script, then discarded.</item>
    /// </list>
    ///
    /// <para>Probe points are the positions a camera is actually at: the player spawn, every
    /// checkpoint, every enemy spawner (you fight where enemies are) and every torch (you walk past
    /// them). Averaging over the whole bounding box would flatter the level by counting empty air.</para>
    /// </summary>
    public static class LightAudit
    {
        const string LevelScene = "Assets/Scenes/Level_01.unity";

        [MenuItem("VibeGame1/Audit Level Lights")]
        public static void Run()
        {
            Report(LevelScene);
        }

        /// <summary>Headless entry point: <c>-executeMethod VibeGame1.EditorTools.LightAudit.Batch</c>.</summary>
        public static void Batch()
        {
            string text = Report(LevelScene);
            System.IO.File.WriteAllText("light-audit.txt", text);
        }

        struct Probe
        {
            public string name;
            public Vector3 pos;
            public Probe(string n, Vector3 p) { name = n; pos = p; }
        }

        static string Path(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        static string Report(string scenePath)
        {
            var sb = new StringBuilder();
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // ---- the renderer's own limits, read off the shipped asset rather than assumed --------
            int perObjectLimit = 4;
            string modeName = "unknown";
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                perObjectLimit = urp.maxAdditionalLightsCount;
                modeName = urp.additionalLightsRenderingMode.ToString();
            }

            // ---- inventory ------------------------------------------------------------------------
            var lights = new List<Light>();
            foreach (var go in scene.GetRootGameObjects())
                lights.AddRange(go.GetComponentsInChildren<Light>(true));

            int point = 0, spot = 0, directional = 0, flickered = 0, shadowed = 0, disabled = 0;
            float rangeSum = 0f, minRange = float.MaxValue, maxRange = 0f;
            float minCull = float.MaxValue;
            var additional = new List<Light>();   // everything that is NOT the directional key light

            foreach (var l in lights)
            {
                if (!l.enabled || !l.gameObject.activeInHierarchy) disabled++;
                if (l.shadows != LightShadows.None) shadowed++;
                switch (l.type)
                {
                    case LightType.Directional: directional++; continue;
                    case LightType.Spot: spot++; break;
                    case LightType.Point: point++; break;
                }
                additional.Add(l);
                rangeSum += l.range;
                minRange = Mathf.Min(minRange, l.range);
                maxRange = Mathf.Max(maxRange, l.range);
                var f = l.GetComponent<FlickerLight>();
                if (f != null) { flickered++; minCull = Mathf.Min(minCull, f.cullDistance); }
            }

            sb.AppendLine("=== LIGHT AUDIT: " + scenePath + " ===");
            sb.AppendLine("URP additional-lights mode : " + modeName);
            sb.AppendLine("URP per-object light limit : " + perObjectLimit);
            sb.AppendLine("Lights total               : " + lights.Count +
                          "  (directional " + directional + ", point " + point + ", spot " + spot + ")");
            sb.AppendLine("Additional (non-key) lights: " + additional.Count);
            sb.AppendLine("  with FlickerLight        : " + flickered +
                          (flickered > 0 ? "  (cull distance " + minCull.ToString("0.#") + " m)" : ""));
            sb.AppendLine("  casting shadows          : " + shadowed +
                          "   <- each shadowed additional light is a separate shadow pass");
            // Name them. A shadow-casting additional light is by far the most expensive kind of light
            // in this scene and there is no reason for a torch to be one, so a stray is worth knowing
            // about by name rather than as a count.
            foreach (var l in lights)
                if (l.shadows != LightShadows.None)
                    sb.AppendLine("      shadow caster: " + Path(l.transform) + " (" + l.type +
                                  ", range " + l.range.ToString("0.#") + ")");
            sb.AppendLine("  starting disabled        : " + disabled);
            if (additional.Count > 0)
                sb.AppendLine("Range: min " + minRange.ToString("0.#") + " m, mean " +
                              (rangeSum / additional.Count).ToString("0.#") + " m, max " + maxRange.ToString("0.#") + " m");

            // ---- probe points ---------------------------------------------------------------------
            var probes = new List<Probe>();
            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name;
                    if (n == "PlayerSpawn" || n.StartsWith("Spawn_") || n.StartsWith("Checkpoint") ||
                        n.StartsWith("Torch") || n.StartsWith("Spawner"))
                        probes.Add(new Probe(n, t.position + Vector3.up * 1.6f));   // eye height
                }
                foreach (var sp in go.GetComponentsInChildren<EnemySpawner>(true))
                    probes.Add(new Probe("spawner:" + sp.name, sp.transform.position + Vector3.up * 1.6f));
            }

            sb.AppendLine();
            sb.AppendLine("Probe points (eye height, at the places a camera actually is): " + probes.Count);

            if (probes.Count == 0)
            {
                sb.AppendLine("NO PROBES FOUND — the naming dialect changed; audit is inconclusive.");
                Debug.Log(sb.ToString());
                return sb.ToString();
            }

            // ---- how many lights survive the cull, and how many actually reach the probe ----------
            int worstAlive = 0, worstReaching = 0;
            string worstAliveAt = "", worstReachingAt = "";
            long aliveSum = 0, reachingSum = 0;
            int probesOverLimit = 0;

            foreach (var p in probes)
            {
                int alive = 0, reaching = 0;
                foreach (var l in additional)
                {
                    var f = l.GetComponent<FlickerLight>();
                    float cull = f != null ? f.cullDistance : float.MaxValue;
                    float d = Vector3.Distance(l.transform.position, p.pos);
                    if (d <= cull) alive++;
                    // URP's own per-light culling: a point light only affects geometry inside its range.
                    if (d <= l.range) reaching++;
                }
                aliveSum += alive; reachingSum += reaching;
                if (alive > worstAlive) { worstAlive = alive; worstAliveAt = p.name; }
                if (reaching > worstReaching) { worstReaching = reaching; worstReachingAt = p.name; }
                if (reaching > perObjectLimit) probesOverLimit++;
            }

            sb.AppendLine();
            sb.AppendLine("--- lights the FlickerLight cull leaves ENABLED (URP gathers/sorts these) ---");
            sb.AppendLine("  mean over probes : " + ((float)aliveSum / probes.Count).ToString("0.0") +
                          " of " + additional.Count);
            sb.AppendLine("  worst probe      : " + worstAlive + "   at " + worstAliveAt);
            sb.AppendLine();
            sb.AppendLine("--- lights whose RANGE actually reaches the probe (candidates to shade) ---");
            sb.AppendLine("  mean over probes : " + ((float)reachingSum / probes.Count).ToString("0.0"));
            sb.AppendLine("  worst probe      : " + worstReaching + "   at " + worstReachingAt);
            sb.AppendLine("  probes where reaching > per-object limit (" + perObjectLimit + "): " +
                          probesOverLimit + " of " + probes.Count +
                          "   <- at these, lights are dropped by the renderer having contributed nothing");

            Debug.Log(sb.ToString());
            return sb.ToString();
        }
    }
}
