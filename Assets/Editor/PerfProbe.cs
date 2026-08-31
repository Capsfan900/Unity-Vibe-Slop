using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Repeatable frame-time and allocation measurement, driven from outside play mode the same way
    /// <see cref="FeatureTestRunner"/> is:
    ///
    ///     VibeGame1.EditorTools.PerfProbe.Start("running", 300);
    ///     VibeGame1.EditorTools.PerfProbe.Poll();     // returns the report once done
    ///
    /// WHY THIS EXISTS. The only performance signal in the project was a smoothed FPS number in the
    /// F1 overlay — enough to notice a catastrophe, useless for "is this change slower than the last
    /// one?". A smoothed average is also the wrong statistic: a speedrun game is ruined by the WORST
    /// frame, not the mean, because one 40 ms hitch loses a run. So this reports percentiles and the
    /// worst frame, and it reports allocation, because steady per-frame garbage is what eventually
    /// produces those hitches when the collector runs.
    ///
    /// Sampled from EditorApplication.update, which ticks once per player frame in play mode — the same
    /// mechanism ViewmodelCapture uses to film without stalling the thing it is filming.
    ///
    /// READ THE NUMBERS HONESTLY. Editor play mode is not a build: it carries editor overhead and the
    /// profiler itself. Treat these as COMPARATIVE — same scene, same scenario, before versus after a
    /// change — never as the frame rate a player will see.
    /// </summary>
    public static class PerfProbe
    {
        static readonly List<float> frameMs = new List<float>(2048);
        static string label = "";
        static int wanted;
        static bool running;
        static long monoAtStart;
        static long allocBytes;
        static string report = "";

        /// <summary>Sample <paramref name="frames"/> frames and label the result. Play mode only.</summary>
        public static string Start(string scenarioLabel, int frames = 300)
        {
            if (!EditorApplication.isPlaying) return "ERROR: enter play mode first — there are no frames to measure.";
            if (running) return "ERROR: already sampling '" + label + "'.";

            label = string.IsNullOrEmpty(scenarioLabel) ? "unlabelled" : scenarioLabel;
            wanted = Mathf.Max(30, frames);
            frameMs.Clear();
            report = "";
            allocBytes = 0;
            monoAtStart = Profiler.GetMonoUsedSizeLong();
            running = true;
            EditorApplication.update += Tick;
            return "sampling '" + label + "' for " + wanted + " frames";
        }

        /// <summary>Progress line, or the full report once the run has finished.</summary>
        public static string Poll()
        {
            if (running) return "sampling '" + label + "' " + frameMs.Count + "/" + wanted;
            return string.IsNullOrEmpty(report) ? "no run yet" : report;
        }

        public static bool IsDone() { return !running && !string.IsNullOrEmpty(report); }

        public static void Stop()
        {
            if (!running) return;
            EditorApplication.update -= Tick;
            running = false;
            Finish();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { Stop(); return; }

            // unscaledDeltaTime, deliberately: hitstop and the super's slow-mo drive Time.timeScale to
            // 0.02, and scaled delta would report those frames as impossibly fast.
            frameMs.Add(Time.unscaledDeltaTime * 1000f);

            if (frameMs.Count < wanted) return;
            long mono = Profiler.GetMonoUsedSizeLong();
            allocBytes = mono - monoAtStart;   // negative means a collection ran inside the window
            Stop();
        }

        static void Finish()
        {
            if (frameMs.Count == 0) { report = "no frames sampled"; return; }

            var sorted = new List<float>(frameMs);
            sorted.Sort();
            float mean = 0f;
            for (int i = 0; i < frameMs.Count; i++) mean += frameMs[i];
            mean /= frameMs.Count;

            float p50 = sorted[Mathf.Clamp(Mathf.RoundToInt(sorted.Count * 0.50f), 0, sorted.Count - 1)];
            float p95 = sorted[Mathf.Clamp(Mathf.RoundToInt(sorted.Count * 0.95f), 0, sorted.Count - 1)];
            float p99 = sorted[Mathf.Clamp(Mathf.RoundToInt(sorted.Count * 0.99f), 0, sorted.Count - 1)];
            float worst = sorted[sorted.Count - 1];

            // A frame over 16.7 ms has missed 60 Hz. Counting them is more useful than an average,
            // because the player feels the misses, not the mean.
            int missed60 = 0;
            for (int i = 0; i < frameMs.Count; i++) if (frameMs[i] > 16.7f) missed60++;

            float allocPerFrameKb = allocBytes > 0 ? (allocBytes / 1024f) / frameMs.Count : 0f;

            report =
                "PERF '" + label + "'  frames=" + frameMs.Count + "\n" +
                "  mean " + mean.ToString("F2") + " ms   p50 " + p50.ToString("F2") +
                "   p95 " + p95.ToString("F2") + "   p99 " + p99.ToString("F2") +
                "   worst " + worst.ToString("F2") + " ms\n" +
                "  frames over 16.7ms: " + missed60 + " (" + (100f * missed60 / frameMs.Count).ToString("F1") + "%)\n" +
                "  mono heap delta: " + (allocBytes / 1024f).ToString("F1") + " KB over the window  (~" +
                allocPerFrameKb.ToString("F2") + " KB/frame)\n" +
                "  NOTE editor play mode, not a build — compare against another PerfProbe run, never against a target FPS.";
            Debug.Log("[PerfProbe] " + report);
        }
    }
}
