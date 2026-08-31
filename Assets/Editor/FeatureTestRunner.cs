using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Editor entry point for the automated play-mode feature suite (<see cref="FeatureTests"/>).
    ///
    /// The suite exercises the real systems in a running scene, so it can only run in play mode.
    /// PlayMode NUnit tests are unavailable in this project (no assembly definitions, so the Test
    /// Runner cannot see Assembly-CSharp), which is why this is a coroutine driver polled from
    /// outside rather than a [UnityTest].
    ///
    /// Automation flow (e.g. from MCP execute_code):
    ///     VibeGame1.EditorTools.FeatureTestRunner.Start();     // begins the run
    ///     VibeGame1.EditorTools.FeatureTestRunner.Poll();      // repeat until it reports done=True
    ///     VibeGame1.EditorTools.FeatureTestRunner.FullReport();// the complete PASS/FAIL text
    /// </summary>
    public static class FeatureTestRunner
    {
        const string NotPlaying =
            "Feature tests need play mode. Enter play mode on Assets/Scenes/Level_01.unity, then run this again.";

        [MenuItem("VibeGame1/Run Feature Tests", priority = 40)]
        public static void RunFromMenu()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[FeatureTests] " + NotPlaying);
                EditorUtility.DisplayDialog("Feature tests", NotPlaying, "OK");
                return;
            }
            FeatureTests.RunAll();
            Debug.Log("[FeatureTests] Started. Watch the console, or poll FeatureTestRunner.Poll().");
        }

        // Deliberately NO validate function: a greyed-out menu item would hide the "enter play mode"
        // explanation, and that is the single most likely reason someone's run does nothing.

        /// <summary>Start the whole suite. Returns a short status string (safe to call from automation).</summary>
        public static string Start() => Start(null);

        /// <summary>Start only the tests whose name contains <paramref name="filter"/>.</summary>
        public static string Start(string filter)
        {
            if (!EditorApplication.isPlaying) return "ERROR: " + NotPlaying;
            FeatureTests.Run(filter);
            return "started" + (string.IsNullOrEmpty(filter) ? " (all)" : " (filter='" + filter + "')");
        }

        /// <summary>
        /// Compact progress line. Once <c>done=True</c> the full report is appended so a single
        /// poll call returns everything the caller needs.
        /// </summary>
        public static string Poll()
        {
            if (!EditorApplication.isPlaying) return "ERROR: not in play mode";
            string status = FeatureTests.Status;
            return FeatureTests.Done ? status + "\n\n" + FeatureTests.Report : status;
        }

        /// <summary>The full report text only (empty until the run finishes).</summary>
        public static string FullReport() => FeatureTests.Report ?? "";

        /// <summary>True when a run has completed.</summary>
        public static bool IsDone() => FeatureTests.Done;
    }
}
