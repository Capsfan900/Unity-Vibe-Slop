using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Cuts a playtest build — never a dev build. <see cref="Windows"/>, <see cref="WebGL"/> and
    /// <see cref="All"/> are the entry points an `execute_code` call (compiles as C# 6) should use directly;
    /// the <c>VibeGame1/Build/…</c> menu items below call the same methods and just log the summary, since
    /// `execute_menu_item` over MCP reports success without actually running anything.
    ///
    /// <para>Output lands at the repo root under <c>Builds/Windows/</c> and <c>Builds/WebGL/</c> — never
    /// under <c>Assets/</c>, and both folders are already covered by the root <c>.gitignore</c>
    /// (<c>/[Bb]uilds/</c>). Each output gets a <c>build-info.txt</c> stamped with the git SHA, branch, UTC
    /// build time and Unity version so a playtest bug report can be traced back to the exact build.</para>
    ///
    /// <para>Playtest configuration, deliberately: <see cref="BuildOptions.None"/>, no development build.
    /// The project's dev keys (F5/F6/F7/F8/F9) are gated <c>#if UNITY_EDITOR || DEVELOPMENT_BUILD</c> in
    /// <c>DebugKeys.cs</c>, so they compile out of this build target automatically — nothing to strip here.
    /// The F10 in-game level editor is NOT gated the same way (only its EXPORT button hides outside the
    /// editor via <c>#if !UNITY_EDITOR</c>); a playtester can still open it. That is a gameplay-gating
    /// decision, not build plumbing, so this script does not change it — see the report.</para>
    /// </summary>
    public static class BuildRunner
    {
        const string ExpectedFirstScene = "Assets/Scenes/MainMenu.unity";
        const string WindowsExeName = "vibegame1.exe";

        static string RepoRoot => Directory.GetParent(Application.dataPath).FullName;
        static string WindowsOutDir => Path.Combine(RepoRoot, "Builds", "Windows");
        static string WebGLOutDir => Path.Combine(RepoRoot, "Builds", "WebGL");

        // ---------------------------------------------------------------- menu items (log only — call the
        // static methods below directly from execute_code, never rely on the menu item over MCP)

        [MenuItem("VibeGame1/Build/Windows")]
        public static void WindowsMenuItem() => Debug.Log(Windows());

        [MenuItem("VibeGame1/Build/WebGL")]
        public static void WebGLMenuItem() => Debug.Log(WebGL());

        [MenuItem("VibeGame1/Build/All")]
        public static void AllMenuItem() => Debug.Log(All());

        // ---------------------------------------------------------------- entry points

        public static string All()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Windows());
            sb.AppendLine(WebGL());
            return sb.ToString();
        }

        public static string Windows()
        {
            return RunBuild(BuildTarget.StandaloneWindows64, WindowsOutDir,
                Path.Combine(WindowsOutDir, WindowsExeName), false);
        }

        public static string WebGL()
        {
            return RunBuild(BuildTarget.WebGL, WebGLOutDir, WebGLOutDir, true);
        }

        // ---------------------------------------------------------------- shared build path

        static string RunBuild(BuildTarget target, string outDir, string locationPathName, bool isWebGL)
        {
            try
            {
                string sceneError = ValidateScenes(out string[] scenes);
                if (sceneError != null) return sceneError;

                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                Directory.CreateDirectory(outDir);

                if (isWebGL)
                {
                    // GitHub Pages cannot set the Content-Encoding header Brotli needs, so ship gzip with
                    // the decompression fallback so it still works even if a host serves no encoding header
                    // at all.
                    PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                    PlayerSettings.WebGL.decompressionFallback = true;
                    // Assets/WebGLTemplates/Playtest — full-viewport canvas, click-to-play pointer lock
                    // gesture, no mobile/diagnostics chrome. See that folder's index.html for why.
                    PlayerSettings.WebGL.template = "PROJECT:Playtest";
                }

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = locationPathName,
                    target = target,
                    options = BuildOptions.None, // playtest config: not a development build
                };

                var sw = Stopwatch.StartNew();
                BuildReport report = BuildPipeline.BuildPlayer(options);
                sw.Stop();

                AssetDatabase.SaveAssets(); // persist the WebGL player-setting changes above to ProjectSettings.asset

                string infoError = null;
                try { WriteBuildInfo(outDir); }
                catch (Exception e) { infoError = e.Message; }

                var summary = report.summary;
                long sizeBytes = SafeDirectorySize(outDir);

                var result = new StringBuilder();
                result.Append("target=").Append(target)
                      .Append(" result=").Append(summary.result)
                      .Append(" outputPath=").Append(locationPathName)
                      .Append(" sizeMB=").Append((sizeBytes / (1024f * 1024f)).ToString("F1"))
                      .Append(" duration=").Append(sw.Elapsed)
                      .Append(" totalErrors=").Append(summary.totalErrors)
                      .Append(" totalWarnings=").Append(summary.totalWarnings)
                      .Append(" totalSize=").Append(summary.totalSize);
                if (infoError != null) result.Append(" build-info.txt FAILED: ").Append(infoError);
                return result.ToString();
            }
            catch (Exception e)
            {
                return "BUILD FAILED (" + target + "): " + e;
            }
        }

        /// <summary>
        /// Enabled scenes from EditorBuildSettings, in order. Fails loudly (returns an error string, never
        /// throws) if the list is empty or MainMenu is not index 0 — a playtest build must boot to the menu.
        /// </summary>
        static string ValidateScenes(out string[] scenes)
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                return "BUILD FAILED: EditorBuildSettings has no enabled scenes.";
            if (scenes[0] != ExpectedFirstScene)
                return "BUILD FAILED: build index 0 is '" + scenes[0] + "', expected '" + ExpectedFirstScene +
                       "'. Fix File > Build Profiles > Scene List (or EditorBuildSettings) before cutting a playtest build.";
            return null;
        }

        static void WriteBuildInfo(string outDir)
        {
            string sha = RunGit("rev-parse --short HEAD");
            string branch = RunGit("rev-parse --abbrev-ref HEAD");
            string text = "git_sha=" + sha + "\n" +
                          "git_branch=" + branch + "\n" +
                          "built_utc=" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n" +
                          "unity_version=" + Application.unityVersion + "\n" +
                          "product_version=" + PlayerSettings.bundleVersion + "\n";
            File.WriteAllText(Path.Combine(outDir, "build-info.txt"), text);
        }

        static string RunGit(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = RepoRoot,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(5000);
                    return string.IsNullOrEmpty(stdout) ? "unknown" : stdout;
                }
            }
            catch (Exception e)
            {
                return "unknown(" + e.Message + ")";
            }
        }

        static long SafeDirectorySize(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return 0;
                long total = 0;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { /* transient file, skip */ }
                }
                return total;
            }
            catch
            {
                return 0;
            }
        }
    }
}
