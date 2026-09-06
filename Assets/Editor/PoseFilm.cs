using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Photographs and MEASURES every wind-up silhouette in one enemy's moveset, from the player's eye,
    /// at that enemy's own <c>preferredRange</c> — in EDIT MODE, headless, with no MCP bridge and no
    /// play mode.
    ///
    /// <para><b>Why not <c>FrameFilm.RunWindups</c>.</b> That is the right tool and it is a RUNTIME
    /// component: it needs a live player, a live level and play mode. This is the same idea rebuilt on
    /// the <see cref="EnemyPortrait"/> / <see cref="SpinFilm"/> pattern so a pose can be authored in a
    /// session where play mode is unavailable. It films the frozen CUE PEAK only — the frame the parry
    /// decision is actually made on — and it measures the mask instead of asking you to eyeball it.</para>
    ///
    /// <para>Two capture traps this project has already paid for are handled here: the first
    /// <c>cam.Render()</c> in batch mode comes back flat and wrongly lit, so a warm-up frame is thrown
    /// away; and <c>EnemyVisuals</c> applies the body colour in <c>Awake</c>, which never runs in edit
    /// mode, so <c>Setup(data)</c> is called by hand (in <see cref="PoseSilhouette.Stage"/>).</para>
    /// </summary>
    public static class PoseFilm
    {
        const int Size = 640;

        [MenuItem("VibeGame1/Photograph Wind-up Silhouettes")]
        public static void Menu()
        {
            Debug.Log(Shoot("Legendary_Revenant",
                            Path.Combine(Directory.GetCurrentDirectory(), "PoseFilm")));
        }

        /// <summary>Batch entry: <c>-executeMethod VibeGame1.EditorTools.PoseFilm.Batch</c>.</summary>
        public static void Batch()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "PoseFilm");
            string report = Shoot("Legendary_Revenant", dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "report.txt"), report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Batch entry that REGENERATES the data first:
        /// <c>-executeMethod VibeGame1.EditorTools.PoseFilm.BatchRebuild</c>. Rule 9 in tool form — a
        /// pose is only real once <see cref="DataFactory"/> has written it into the <c>.asset</c>, so
        /// the frames are always taken of the shipped values and never of a number in a source file.
        /// </summary>
        public static void BatchRebuild()
        {
            DataFactory.CreateAll();
            Batch();
        }

        public static string Shoot(string prefabName, string dir)
        {
            var sb = new StringBuilder();
            Directory.CreateDirectory(dir);

            // FIRST, before anything is loaded. Opening a scene runs an unused-asset sweep, and that
            // sweep DESTROYS ScriptableObjects nothing in a scene references — a moveset's attack assets
            // held only by a local List die under it and every later read throws MissingReference.
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);


            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + prefabName + ".prefab");
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(prefabName));
            if (prefab == null || data == null) return "FAIL: missing prefab or data for " + prefabName;

            // Every distinct attack in the moveset, in moveset order.
            var atks = new System.Collections.Generic.List<EnemyAttackData>();
            var combos = data.ResolveCombos();
            if (combos != null)
                foreach (var c in combos)
                {
                    if (c == null || c.hits == null) continue;
                    foreach (var h in c.hits) if (h != null && !atks.Contains(h)) atks.Add(h);
                }
            if (atks.Count == 0) return "FAIL: " + prefabName + " has no attacks";

            float dist = Mathf.Max(1f, data.preferredRange);
            float chest = 1.11f * Mathf.Max(0.01f, data.scale);

            sb.AppendLine("=== " + prefabName + "  (" + data.displayName + ")");
            sb.AppendLine(string.Format(
                "  filmed from the player's eye: y {0:F2}, {1:F2} m away (preferredRange), {2:F0}deg FOV, " +
                "aimed at the chest (y {3:F2}). Frame = the CUE PEAK (armWindup x 1.12, body at k=1).",
                PoseSilhouette.EyeHeight, dist, PoseSilhouette.Fov, chest));
            sb.AppendLine();

            var names = new string[atks.Count + 1];
            var masks = new bool[atks.Count + 1][];
            names[0] = "REST";

            GameObject camGo = null, lightGo = null;
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            var shot = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            try
            {
                lightGo = new GameObject("Sun");
                var sun = lightGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.9f;
                lightGo.transform.rotation = Quaternion.Euler(35f, 145f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.30f, 0.31f, 0.36f);

                camGo = new GameObject("Cam");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = PoseSilhouette.Fov;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
                cam.targetTexture = rt;
                // +Z: the side the enemy faces. See PoseSilhouette.Raster.
                camGo.transform.position = new Vector3(0f, PoseSilhouette.EyeHeight, dist);
                camGo.transform.LookAt(new Vector3(0f, chest, 0f));

                for (int i = 0; i <= atks.Count; i++)
                {
                    var atk = i == 0 ? null : atks[i - 1];
                    string tag = i == 0 ? "00_REST" : (i.ToString("00") + "_" + atk.name);
                    if (i > 0) names[i] = atk.name;

                    var inst = PoseSilhouette.Stage(prefab, data);
                    // Out of the PICTURE as well as out of the mask, so the photograph and the numbers
                    // are of the same thing. See PoseSilhouette.Raster.
                    var bar = inst.GetComponentInChildren<EnemyPostureBar>(true);
                    if (bar != null) bar.gameObject.SetActive(false);
                    if (atk != null) PoseSilhouette.ApplyPeak(inst, atk);

                    int filled;
                    masks[i] = PoseSilhouette.Raster(inst, dist, chest, out filled);

                    // WARM-UP RENDER, discarded. Unity's first Render() in batch mode returns before the
                    // pipeline has finished setting up and produces a flat, wrongly-lit frame.
                    if (i == 0) cam.Render();
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    shot.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    shot.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, tag + ".png"), shot.EncodeToPNG());
                    WriteMask(Path.Combine(dir, tag + "_mask.png"), masks[i], i == 0 ? null : masks[0]);

                    Object.DestroyImmediate(inst);
                }
            }
            finally
            {
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (lightGo != null) Object.DestroyImmediate(lightGo);
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(shot);
            }

            var shapes = PoseSilhouette.Normalise(names, masks);

            sb.AppendLine("  MEASURED SILHOUETTES — lengths in body-heights of the RESTING body,");
            sb.AppendLine("  centroid/top relative to rest, axis = tilt off vertical (+ = leans to the player's right).");
            sb.AppendLine();
            sb.AppendLine("    pose                     w      h    dCx    dCy   dTop    axis   area  IoU/rest  authored");
            for (int i = 0; i < shapes.Length; i++)
            {
                var s = shapes[i];
                var atk = i == 0 ? null : atks[i - 1];
                bool authored = atk != null && atk.windupPose != null && atk.windupPose.authored;
                sb.AppendLine(string.Format(
                    "    {0,-20} {1,6:F2} {2,6:F2} {3,6:F2} {4,6:F2} {5,6:F2} {6,7:F1} {7,6:F2} {8,9:F2}  {9}",
                    s.name, s.w, s.h, s.cx, s.cy, s.top, s.axis, s.area,
                    PoseSilhouette.IoU(masks[0], masks[i]), i == 0 ? "-" : (authored ? "YES" : "no (cone fallback)")));
            }

            sb.AppendLine();
            sb.AppendLine("  PAIRWISE IoU (1.00 = the same shape on screen; the four attacks must not rhyme)");
            sb.Append("                       ");
            for (int j = 1; j < shapes.Length; j++) sb.Append(string.Format("{0,10}", Short(names[j])));
            sb.AppendLine();
            for (int i = 1; i < shapes.Length; i++)
            {
                sb.Append(string.Format("    {0,-18}", names[i]));
                for (int j = 1; j < shapes.Length; j++)
                    sb.Append(string.Format("{0,10:F2}", i == j ? 1f : PoseSilhouette.IoU(masks[i], masks[j])));
                sb.AppendLine();
            }
            sb.AppendLine();
            sb.AppendLine("  wrote " + (atks.Count + 1) * 2 + " frames to " + dir);
            return sb.ToString();
        }

        static string Short(string n)
        {
            int i = n.IndexOf('_');
            return i >= 0 && i + 1 < n.Length ? n.Substring(i + 1) : n;
        }

        /// <summary>The measured mask as a picture: white where the body covers the frame, and the
        /// rest silhouette in dark red underneath so the DIFFERENCE is the thing you see.</summary>
        static void WriteMask(string path, bool[] mask, bool[] rest)
        {
            int g = PoseSilhouette.Grid;
            var tex = new Texture2D(g, g, TextureFormat.RGB24, false);
            var px = new Color32[g * g];
            for (int i = 0; i < px.Length; i++)
            {
                bool m = mask[i], r = rest != null && rest[i];
                px[i] = m ? new Color32(255, 255, 255, 255)
                     : r ? new Color32(90, 20, 20, 255)
                         : new Color32(10, 10, 14, 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
