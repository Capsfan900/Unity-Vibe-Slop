using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Runs the EditMode NUnit suite from inside the editor, with or without the slow level-line tests.
    ///
    /// <para>Four fixtures — <c>LevelSpan1Tests</c>, <c>LevelSpan2Tests</c>, <c>LevelSpan3Tests</c> and
    /// <c>LevelArcClearanceTests</c> — simulate the motor along every wall-run line in the level and cost
    /// 7-19 s each. They are worth their time when the level geometry or the movement constants change and
    /// are dead weight the other ninety-nine times a day. They carry <c>[Category("LevelLines")]</c>, so the
    /// split below is data on the tests, not a list of names kept in sync by hand here.</para>
    ///
    /// <para><b>How the exclusion works.</b> The Test Framework's <see cref="Filter"/> (com.unity.test-framework
    /// 1.7.0) can only INCLUDE categories — <c>categoryNames</c> has no negation, and there is no
    /// <c>excludeCategoryNames</c>. So this asks the API for the whole EditMode test tree
    /// (<see cref="TestRunnerApi.RetrieveTestList(TestMode,Action{ITestAdaptor})"/>), keeps the leaves whose
    /// <see cref="ITestAdaptor.Categories"/> lack the category, and runs those by <c>testNames</c>. Categories
    /// are inherited from the fixture by the framework itself, so a class-level attribute is enough.</para>
    ///
    /// <para>Automation (e.g. MCP <c>execute_code</c>):</para>
    /// <code>
    /// VibeGame1.EditorTools.QuickTestRunner.RunQuick();   // everything except [Category("LevelLines")]
    /// VibeGame1.EditorTools.QuickTestRunner.RunFull();    // the whole EditMode suite
    /// </code>
    /// Both return immediately; the run is asynchronous. Watch the console for the one-line summary
    /// <c>[QuickTests] ... run=N passed=N failed=N skipped=N in Ns</c>, which is the completion signal.
    /// </summary>
    public static class QuickTestRunner
    {
        /// <summary>The category carried by the slow level-line fixtures. Keep in step with the attribute.</summary>
        public const string SlowCategory = "LevelLines";

        /// <summary>NUnit XML lands here, the same format the normal Test Runner writes.</summary>
        const string ResultsDir = "TestResults";

        static bool running;

        [MenuItem("VibeGame1/Run Quick EditMode Tests", priority = 41)]
        public static void RunQuickFromMenu() { Debug.Log("[QuickTests] " + RunQuick()); }

        [MenuItem("VibeGame1/Run Full EditMode Tests", priority = 42)]
        public static void RunFullFromMenu() { Debug.Log("[QuickTests] " + RunFull()); }

        /// <summary>Run every EditMode test EXCEPT those in <see cref="SlowCategory"/>. Returns a status string.</summary>
        public static string RunQuick() { return Run(true); }

        /// <summary>Run the whole EditMode suite, level-line simulations included. Returns a status string.</summary>
        public static string RunFull() { return Run(false); }

        static string Run(bool skipSlow)
        {
            if (EditorApplication.isPlaying)
                return "ERROR: EditMode tests cannot run in play mode. Exit play mode and run this again.";
            if (running)
                return "ERROR: a run is already in progress. Wait for the [QuickTests] summary line.";

            running = true;
            var label = skipSlow ? "quick (no " + SlowCategory + ")" : "full";

            try
            {
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();

                if (!skipSlow)
                {
                    Begin(api, new Filter { testMode = TestMode.EditMode }, label, 0);
                    return "started " + label + " EditMode run.";
                }

                // Asynchronous: the tree arrives in a callback, and the run is started from there.
                api.RetrieveTestList(TestMode.EditMode, root =>
                {
                    var keep = new List<string>();
                    var excluded = 0;
                    Collect(root, keep, ref excluded);

                    if (keep.Count == 0)
                    {
                        running = false;
                        Debug.LogWarning("[QuickTests] No tests left after excluding category '" + SlowCategory
                                         + "'. Nothing ran.");
                        return;
                    }

                    Debug.Log("[QuickTests] Starting " + label + ": " + keep.Count + " tests, "
                              + excluded + " excluded by category '" + SlowCategory + "'.");
                    Begin(api, new Filter { testMode = TestMode.EditMode, testNames = keep.ToArray() },
                          label, excluded);
                });

                return "started " + label + " EditMode run.";
            }
            catch (Exception e)
            {
                running = false;
                Debug.LogException(e);
                return "ERROR: " + e.Message;
            }
        }

        static void Begin(TestRunnerApi api, Filter filter, string label, int excluded)
        {
            var callbacks = new Summary(api, label, excluded);
            api.RegisterCallbacks(callbacks);
            api.Execute(new ExecutionSettings(filter));
        }

        /// <summary>Walk the tree, keeping the leaf test cases that do NOT carry <see cref="SlowCategory"/>.</summary>
        static void Collect(ITestAdaptor node, List<string> keep, ref int excluded)
        {
            if (node == null) return;

            if (!node.IsSuite && !node.HasChildren)
            {
                if (node.Categories != null && node.Categories.Contains(SlowCategory)) excluded++;
                else keep.Add(node.FullName);
                return;
            }

            if (node.Children == null) return;
            foreach (var child in node.Children) Collect(child, keep, ref excluded);
        }

        /// <summary>
        /// Logs one summary line when the run ends, writes the NUnit XML, and unhooks itself. The summary
        /// line IS the completion signal for automation — there is no polling API worth the name here.
        /// </summary>
        class Summary : ICallbacks
        {
            readonly TestRunnerApi api;
            readonly string label;
            readonly int excluded;
            readonly Stopwatch clock = new Stopwatch();

            public Summary(TestRunnerApi api, string label, int excluded)
            {
                this.api = api;
                this.label = label;
                this.excluded = excluded;
            }

            public void RunStarted(ITestAdaptor testsToRun) { clock.Restart(); }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                clock.Stop();
                running = false;

                var xml = "(not written)";
                try
                {
                    Directory.CreateDirectory(ResultsDir);
                    xml = Path.Combine(ResultsDir,
                        "EditMode-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xml");
                    TestRunnerApi.SaveResultToFile(result, xml);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[QuickTests] Could not write NUnit XML: " + e.Message);
                    xml = "(failed)";
                }

                var run = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
                Debug.Log("[QuickTests] " + label
                          + " DONE run=" + run
                          + " passed=" + result.PassCount
                          + " failed=" + result.FailCount
                          + " skipped=" + result.SkipCount
                          + " inconclusive=" + result.InconclusiveCount
                          + " excluded=" + excluded
                          + " in " + (clock.ElapsedMilliseconds / 1000f).ToString("F1") + "s"
                          + " xml=" + xml);

                if (result.FailCount > 0) LogFailures(result);

                api.UnregisterCallbacks(this);
                // Destroy the throwaway runner object next tick, not from inside its own callback.
                var doomed = api;
                EditorApplication.delayCall += () => { if (doomed != null) UnityEngine.Object.DestroyImmediate(doomed); };
            }

            static void LogFailures(ITestResultAdaptor node)
            {
                if (node.HasChildren && node.Children != null)
                {
                    foreach (var child in node.Children) LogFailures(child);
                    return;
                }
                if (node.TestStatus == TestStatus.Failed)
                    Debug.LogError("[QuickTests] FAIL " + node.FullName + " — " + node.Message);
            }
        }
    }
}
