using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Cuts a playtest build — never a dev build. <see cref="Windows"/>, <see cref="WebGL"/>,
    /// <see cref="All"/> and <see cref="Preflight"/> are the entry points an `execute_code` call should use
    /// directly; the <c>VibeGame1/Build/…</c> menu items are thin wrappers, since `execute_menu_item` over
    /// MCP reports success without actually running anything.
    ///
    /// <para><b>The contract is that this works whatever state the project is in.</b> Add a level, add an
    /// enemy, leave the editor in play mode, leave the scene list stale — running <see cref="Windows"/>
    /// either produces a correct build or says in one sentence exactly what to fix. Everything it needs it
    /// either repairs itself (the scene list) or refuses to guess about (a broken compile).</para>
    ///
    /// <para><b>Every run also writes its summary to <c>Builds/last-build.txt</c> and logs it.</b> A build
    /// blocks Unity's main thread for minutes, which drops the MCP websocket, so the return value of the
    /// call that started the build is routinely lost. The file is the reliable channel — read it rather
    /// than trusting the tool result.</para>
    ///
    /// <para>Output lands at the repo root under <c>Builds/Windows/</c> and <c>Builds/WebGL/</c> — never
    /// under <c>Assets/</c>, and both are covered by the root <c>.gitignore</c> (<c>/[Bb]uilds/</c>). Each
    /// output gets a <c>build-info.txt</c> stamped with the git SHA, branch, UTC build time, Unity version
    /// and the exact scene list, so a playtest bug report traces back to one build.</para>
    ///
    /// <para>Playtest configuration, deliberately: <see cref="BuildOptions.None"/>, no development build.
    /// Player-reachable tooling remains compiled for trusted release-build diagnosis, but
    /// <c>DeveloperAccess</c> makes F1/F5-F10, slot 4, Sandbox/custom rows and timing capture inert until
    /// the private process-local console passphrase is accepted.
    /// The capability resets on process start and is never written to settings or build metadata.</para>
    /// </summary>
    public static class BuildRunner
    {
        const string MainMenuScene = "Assets/Scenes/MainMenu.unity";

        /// <summary>
        /// The sandbox ships. <c>MainMenuController.LoadSandbox</c> and every custom-level row load it by
        /// name, so a build without it has a main menu with dead buttons.
        /// </summary>
        const string SandboxScene = "Assets/Scenes/Sandbox.unity";

        const string WindowsExeName = "vibegame1.exe";
        const ManagedStrippingLevel PlaytestStripping = ManagedStrippingLevel.High;

        static string RepoRoot => Directory.GetParent(Application.dataPath).FullName;
        static string BuildsRoot => Path.Combine(RepoRoot, "Builds");
        static string WindowsOutDir => Path.Combine(BuildsRoot, "Windows");
        static string WebGLOutDir => Path.Combine(BuildsRoot, "WebGL");
        static string LastBuildFile => Path.Combine(BuildsRoot, "last-build.txt");

        // ---------------------------------------------------------------- menu items (log only)

        [MenuItem("VibeGame1/Build/Windows")]
        public static void WindowsMenuItem() => Debug.Log(Windows());

        [MenuItem("VibeGame1/Build/WebGL")]
        public static void WebGLMenuItem() => Debug.Log(WebGL());

        [MenuItem("VibeGame1/Build/All")]
        public static void AllMenuItem() => Debug.Log(All());

        [MenuItem("VibeGame1/Build/Preflight (no build)")]
        public static void PreflightMenuItem() => Debug.Log(Preflight());

        // ---------------------------------------------------------------- entry points

        public static string Windows()
        {
            return RunBuild(BuildTarget.StandaloneWindows64, NamedBuildTarget.Standalone, WindowsOutDir,
                            Path.Combine(WindowsOutDir, WindowsExeName), false);
        }

        public static string WebGL()
        {
            return RunBuild(BuildTarget.WebGL, NamedBuildTarget.WebGL, WebGLOutDir, WebGLOutDir, true);
        }

        public static string All()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Windows());
            sb.AppendLine(WebGL());
            return sb.ToString();
        }

        /// <summary>
        /// Everything <see cref="RunBuild"/> checks, without building — including the scene-list drift it
        /// WOULD repair. Cheap, safe to run any time, and the right first call when a build misbehaves.
        /// </summary>
        public static string Preflight()
        {
            var sb = new StringBuilder();
            sb.Append("playMode=").Append(EditorApplication.isPlaying)
              .Append(" compiling=").Append(EditorApplication.isCompiling)
              .Append(" compileFailed=").Append(EditorUtility.scriptCompilationFailed)
              .Append(" windowsSupported=").Append(IsSupported(BuildTarget.StandaloneWindows64))
              .Append(" webglSupported=").Append(IsSupported(BuildTarget.WebGL))
              .Append(" backend=").Append(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone))
              .Append(" stripping=").Append(PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Standalone))
              .Append(" dirtyScenes=").Append(DirtyOpenScenes().Count);

            string[] wanted;
            string drift;
            string sceneError = ResolveSceneList(out wanted, out drift);
            sb.Append(" | scenes=").Append(sceneError ?? string.Join(", ", wanted));
            if (drift != null) sb.Append(" | WOULD REPAIR: ").Append(drift);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- shared build path

        static string RunBuild(BuildTarget target, NamedBuildTarget named, string outDir,
                               string locationPathName, bool isWebGL)
        {
            try
            {
                // ---- guards: refuse clearly rather than produce a build that lies -------------------
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                    return Emit("BUILD REFUSED (" + target + "): the editor was in play mode. Play mode has " +
                                "now been exited — re-run the same call. (Exiting takes a frame and a domain " +
                                "reload, so it cannot be done inside this one.)");
                }
                if (EditorApplication.isCompiling)
                    return Emit("BUILD REFUSED (" + target + "): scripts are still compiling. Wait for the " +
                                "spinner and re-run.");
                if (EditorUtility.scriptCompilationFailed)
                    return Emit("BUILD REFUSED (" + target + "): the project has compile errors. A build " +
                                "here would silently ship the last good assemblies. Fix the console first.");
                if (!IsSupported(target))
                    return Emit("BUILD REFUSED (" + target + "): that build target's module is not installed " +
                                "in Unity " + Application.unityVersion + ". Add it in Unity Hub > Installs > " +
                                "Add modules.");

                // ---- scene list: self-healing, because adding a level must not break the build -------
                string[] scenes;
                string drift;
                string sceneError = ResolveSceneList(out scenes, out drift);
                if (sceneError != null) return Emit("BUILD REFUSED (" + target + "): " + sceneError);
                if (drift != null) ApplySceneList(scenes);

                // ---- warn, never silently bake, about unsaved scene edits ----------------------------
                List<string> dirty = DirtyOpenScenes();
                string dirtyNote = dirty.Count == 0
                    ? null
                    : "WARNING: unsaved edits in " + string.Join(", ", dirty) + " — the build used the " +
                      "version ON DISK, not what is open in the editor.";

                AssetDatabase.SaveAssets();

                // Build beside the known-good output. A failed build must not erase the last copy a
                // tester can launch; only a fully successful, traceable staging build is promoted.
                string stagingDir = outDir + ".staging";
                TryDeleteDirectory(stagingDir);
                if (Directory.Exists(stagingDir))
                    return Emit("BUILD REFUSED (" + target + "): stale staging output could not be removed at " +
                                stagingDir + ". Close anything using that folder and re-run; the previous build was preserved.");
                Directory.CreateDirectory(stagingDir);
                string stagingLocation = isWebGL
                    ? stagingDir
                    : Path.Combine(stagingDir, Path.GetFileName(locationPathName));

                // ---- config enforced here, so a build does not depend on what someone last clicked ---
                PlayerSettings.SetManagedStrippingLevel(named, PlaytestStripping);
                if (isWebGL)
                {
                    // GitHub Pages cannot set the Content-Encoding header Brotli needs, so ship gzip with
                    // the decompression fallback so it still works even if a host serves no encoding
                    // header at all.
                    PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                    PlayerSettings.WebGL.decompressionFallback = true;
                    // Assets/WebGLTemplates/Playtest — full-viewport canvas, click-to-play pointer lock
                    // gesture, no mobile/diagnostics chrome. See that folder's index.html for why.
                    PlayerSettings.WebGL.template = "PROJECT:Playtest";
                }

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = stagingLocation,
                    target = target,
                    options = BuildOptions.None, // playtest config: not a development build
                };

                var sw = Stopwatch.StartNew();
                BuildReport report = BuildPipeline.BuildPlayer(options);
                sw.Stop();

                AssetDatabase.SaveAssets(); // persist the player-setting changes above

                BuildSummary summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    TryDeleteDirectory(stagingDir);
                    return Emit("BUILD FAILED (" + target + "): result=" + summary.result +
                                " errors=" + summary.totalErrors + "\n" + FirstErrors(report));
                }

                string cleanupNote = StripDoNotShip(stagingDir);
                try { WriteBuildInfo(stagingDir, scenes, named, sw.Elapsed); }
                catch (Exception e)
                {
                    TryDeleteDirectory(stagingDir);
                    return Emit("BUILD FAILED (" + target + "): build-info.txt could not be written: " + e.Message +
                                ". The previous build was preserved.");
                }

                long sizeBytes = SafeDirectorySize(stagingDir);
                string promotionError = PromoteBuildOutput(stagingDir, outDir);
                if (promotionError != null)
                    return Emit("BUILD FAILED (" + target + "): completed staging output could not replace the " +
                                "previous build: " + promotionError);
                var result = new StringBuilder();
                result.Append("BUILD OK target=").Append(target)
                      .Append(" outputPath=").Append(locationPathName)
                      .Append(" sizeMB=").Append((sizeBytes / (1024f * 1024f)).ToString("F1"))
                      .Append(" duration=").Append(sw.Elapsed.ToString(@"mm\:ss"))
                      .Append(" scenes=").Append(scenes.Length)
                      .Append(" stripping=").Append(PlaytestStripping)
                      .Append(" warnings=").Append(summary.totalWarnings);
                if (drift != null) result.Append("\nSCENE LIST REPAIRED: ").Append(drift);
                if (dirtyNote != null) result.Append("\n").Append(dirtyNote);
                if (cleanupNote != null) result.Append("\n").Append(cleanupNote);
                return Emit(result.ToString());
            }
            catch (Exception e)
            {
                return Emit("BUILD FAILED (" + target + "): " + e);
            }
        }

        // ---------------------------------------------------------------- scene list

        /// <summary>
        /// The scene list a playtest build must have: MainMenu at index 0, then every scene named by a
        /// campaign level in <see cref="LevelRegistry"/>, then the sandbox. Derived from data every run, so
        /// adding a level to the registry puts its scene in the next build with nothing else to remember.
        /// Returns an error string if a required scene file does not exist; otherwise null, with
        /// <paramref name="drift"/> describing how EditorBuildSettings currently disagrees (null if it
        /// already matches).
        /// </summary>
        static string ResolveSceneList(out string[] scenes, out string drift)
        {
            scenes = null;
            drift = null;

            var wanted = new List<string> { MainMenuScene };

            LevelRegistry registry = LoadRegistry();
            if (registry != null)
            {
                foreach (LevelDefinition level in registry.Ordered())
                {
                    if (level == null || string.IsNullOrEmpty(level.sceneName)) continue;
                    string path = "Assets/Scenes/" + level.sceneName + ".unity";
                    if (!File.Exists(Path.Combine(RepoRoot, path)))
                        return "level '" + level.SafeLevelId + "' names scene '" + level.sceneName +
                               "' but " + path + " does not exist. Create it or fix the LevelDefinition.";
                    if (!wanted.Contains(path)) wanted.Add(path);
                }
            }

            if (File.Exists(Path.Combine(RepoRoot, SandboxScene)) && !wanted.Contains(SandboxScene))
                wanted.Add(SandboxScene);

            foreach (string required in wanted)
                if (!File.Exists(Path.Combine(RepoRoot, required)))
                    return "required scene " + required + " does not exist.";

            scenes = wanted.ToArray();

            string[] current = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (!current.SequenceEqual(scenes))
                drift = "was [" + string.Join(", ", current) + "] -> now [" + string.Join(", ", scenes) + "]";
            return null;
        }

        static void ApplySceneList(string[] scenes)
        {
            EditorBuildSettings.scenes = scenes
                .Select(p => new EditorBuildSettingsScene(p, true))
                .ToArray();
        }

        static LevelRegistry LoadRegistry()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:LevelRegistry"))
            {
                var r = AssetDatabase.LoadAssetAtPath<LevelRegistry>(AssetDatabase.GUIDToAssetPath(guid));
                if (r != null) return r;
            }
            return null;
        }

        // ---------------------------------------------------------------- helpers

        static bool IsSupported(BuildTarget target)
        {
            return BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target);
        }

        static List<string> DirtyOpenScenes()
        {
            var dirty = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isDirty) dirty.Add(string.IsNullOrEmpty(s.name) ? "(untitled)" : s.name);
            }
            return dirty;
        }

        /// <summary>Unity leaves <c>*_BurstDebugInformation_DoNotShip</c> beside the player. Obey the name.</summary>
        static string StripDoNotShip(string outDir)
        {
            try
            {
                var removed = new List<string>();
                foreach (string dir in Directory.GetDirectories(outDir, "*DoNotShip*", SearchOption.TopDirectoryOnly))
                {
                    Directory.Delete(dir, true);
                    removed.Add(Path.GetFileName(dir));
                }
                return removed.Count == 0 ? null : "removed non-shipping folders: " + string.Join(", ", removed);
            }
            catch (Exception e)
            {
                return "could not remove *DoNotShip* folders: " + e.Message;
            }
        }

        /// <summary>The actual reasons a build failed, not just the result enum.</summary>
        static string FirstErrors(BuildReport report)
        {
            const int max = 8;
            var lines = new List<string>();
            foreach (BuildStep step in report.steps)
            {
                foreach (BuildStepMessage m in step.messages)
                {
                    if (m.type != LogType.Error && m.type != LogType.Exception) continue;
                    lines.Add("  [" + step.name + "] " + m.content);
                    if (lines.Count >= max) return string.Join("\n", lines);
                }
            }
            return lines.Count == 0 ? "  (no error messages in the build report)" : string.Join("\n", lines);
        }

        static string Emit(string summary)
        {
            try
            {
                Directory.CreateDirectory(BuildsRoot);
                File.WriteAllText(LastBuildFile,
                    DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n" + summary + "\n");
            }
            catch { /* the log below is still the record */ }
            Debug.Log("[BuildRunner] " + summary);
            return summary;
        }

        static void WriteBuildInfo(string outDir, string[] scenes, NamedBuildTarget named, TimeSpan duration)
        {
            string repositoryStatus = RunGit("status --porcelain --untracked-files=all");
            string buildInputStatus = RunGit(
                "status --porcelain --untracked-files=all -- Assets Packages ProjectSettings");
            string text =
                "git_sha=" + RunGit("rev-parse --short HEAD") + "\n" +
                "git_branch=" + RunGit("rev-parse --abbrev-ref HEAD") + "\n" +
                "git_dirty=" + DirtyValue(repositoryStatus) + "\n" +
                "build_inputs_dirty=" + DirtyValue(buildInputStatus) + "\n" +
                "built_utc=" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n" +
                "build_seconds=" + ((int)duration.TotalSeconds) + "\n" +
                "unity_version=" + Application.unityVersion + "\n" +
                "product_version=" + PlayerSettings.bundleVersion + "\n" +
                "scripting_backend=" + PlayerSettings.GetScriptingBackend(named) + "\n" +
                "managed_stripping=" + PlayerSettings.GetManagedStrippingLevel(named) + "\n" +
                "scenes=" + string.Join(";", scenes) + "\n";
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
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return "unknown(git did not start)";
                    string stdout = p.StandardOutput.ReadToEnd().Trim();
                    string stderr = p.StandardError.ReadToEnd().Trim();
                    if (!p.WaitForExit(5000))
                    {
                        try { p.Kill(); } catch { }
                        return "unknown(git timed out)";
                    }
                    if (p.ExitCode != 0)
                        return "unknown(git exit " + p.ExitCode +
                               (string.IsNullOrEmpty(stderr) ? "" : ": " + stderr) + ")";
                    return stdout;
                }
            }
            catch (Exception e)
            {
                return "unknown(" + e.Message + ")";
            }
        }

        static string DirtyValue(string status)
        {
            if (status != null && status.StartsWith("unknown(", StringComparison.Ordinal)) return "unknown";
            return string.IsNullOrEmpty(status) ? "no" : "YES";
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

        /// <summary>
        /// Park the previous output, promote the completed staging directory, then discard the backup.
        /// If promotion fails, restore the old output before reporting the failure.
        /// </summary>
        static string PromoteBuildOutput(string stagingDir, string outDir)
        {
            string backupDir = outDir + ".previous";
            try
            {
                TryDeleteDirectory(backupDir);
                if (Directory.Exists(backupDir))
                    return "stale backup could not be removed at " + backupDir +
                           "; the previous build was left untouched";
                if (Directory.Exists(outDir)) Directory.Move(outDir, backupDir);
                try
                {
                    Directory.Move(stagingDir, outDir);
                }
                catch
                {
                    TryDeleteDirectory(outDir);
                    if (Directory.Exists(outDir))
                        throw new IOException("partial promoted output could not be removed; previous build remains at " +
                                              backupDir);
                    if (Directory.Exists(backupDir)) Directory.Move(backupDir, outDir);
                    throw;
                }
                TryDeleteDirectory(backupDir);
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { /* a later create/move reports the concrete failure without risking the old output */ }
        }
    }
}
