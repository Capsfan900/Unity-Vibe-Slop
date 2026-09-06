using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Photographs the sky, and — the shot that actually decides anything — an enemy silhouetted
    /// against it at combat range, including the worst case where the enemy stands directly on the
    /// eclipse's black disc.
    ///
    /// <para><b>Why.</b> The sky is the backdrop every enemy silhouette is read against. A sky change
    /// is not a colour change, it is a change to combat readability, and the only honest way to judge
    /// it is a frame from the player's own eye with an enemy in it. Two numbers come out of every run:
    /// the mean sRGB of a box over the enemy's torso and of a box of bare sky beside it, so "does it
    /// still read" is answered with a contrast ratio rather than an opinion.</para>
    ///
    /// <para><b>Post-processing is ON here</b>, unlike <see cref="LevelRouteShots"/>. ACES is the whole
    /// risk on a red sky (it desaturates saturated hues toward orange above ~1.25), so a frame rendered
    /// without the volume would prove nothing. The camera opts in via
    /// <c>UniversalAdditionalCameraData.renderPostProcessing</c> and a global Volume carrying the
    /// shipped profile is added if the scene has none.</para>
    ///
    /// <para>Edit mode and headless. Two traps are handled: the first <c>Camera.Render()</c> in
    /// batchmode is wrongly lit and is discarded, and assets are loaded AFTER <c>OpenScene</c> because
    /// Single-mode opening unloads unused assets.</para>
    ///
    /// <para>Output directory comes from the <c>SKYSHOT_DIR</c> environment variable so the same
    /// zero-argument entry point can shoot a "before" and an "after" set (<c>-executeMethod</c> takes
    /// no arguments).</para>
    /// </summary>
    public static class SkyShots
    {
        const int Width = 1024, Height = 576;
        const float EyeHeight = 1.6f;
        const float Fov = 70f;

        // Open ground far outside the level footprint: nothing but sky behind the subject, which is
        // what isolates the sky's contribution to the silhouette from the level's.
        static readonly Vector3 Open = new Vector3(300f, 0f, 60f);

        [MenuItem("VibeGame1/Photograph The Sky", priority = 302)]
        public static void Menu() { Run(); }

        /// <summary>Batch entry: <c>-executeMethod VibeGame1.EditorTools.SkyShots.Batch</c>.</summary>
        public static void Batch()
        {
            string report = Run();
            Debug.Log(report);
            EditorApplication.Exit(0);
        }

        public static string Run()
        {
            string name = System.Environment.GetEnvironmentVariable("SKYSHOT_DIR");
            if (string.IsNullOrEmpty(name)) name = "SkyShots";
            string dir = Path.Combine(Directory.GetCurrentDirectory(), name);
            Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("=== SkyShots -> " + dir);

            // Scene FIRST, assets after: OpenScene(Single) unloads unused assets, and a definition held
            // only by a local becomes fake-null, at which point the builder refuses and the frames show
            // the PREVIOUS build while looking perfectly plausible.
            EditorSceneManager.OpenScene("Assets/Scenes/Level_01.unity", OpenSceneMode.Single);

            // The canonical step-6 build. It defers to LevelDefinitionBuilder when the definition
            // asset exists, so the eclipse's PITCH and DIAMETER in these frames come from
            // LevelDefinition.sky on Assets/Data/Levels/Level_01_Level.asset - the palette comes
            // from Starfield. Read the builder's own success line in the log, not this one.
            LevelGreyboxBuilder.Build();
            sb.AppendLine("ran step 6 (LevelGreyboxBuilder.Build - definition asset if present)");

            // Environment: ProjectSetup owns fog/ambient/key light, and it writes them into the OPEN
            // scene. Running it here is what makes these frames show the shipped lighting rather than
            // whatever the scene file last serialised.
            ProjectSetup.Run();

            EnsureVolume(sb);

            // The sky follows the camera at runtime (SkyFollower, play mode only). In edit mode it rests
            // where the builder put it, i.e. at the Level root, so every camera here is placed near the
            // origin in X/Z or the 25-unit dome is simply somewhere else. The open-ground shots therefore
            // move the sky root to the camera by hand — the same thing SkyFollower does in play mode.
            var skyRoot = GameObject.Find("Level/Sky/Starfield");
            if (skyRoot == null) sb.AppendLine("WARNING: no Level/Sky/Starfield in the scene.");

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var camGo = new GameObject("~SkyCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = Fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.targetTexture = rt;
            var extra = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (extra == null) extra = camGo.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;   // ACES + bloom + grading, or this proves nothing
            extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            GameObject grunt = null;

            try
            {
                // Warm-up render, discarded: the first Render() in batchmode returns before the pipeline
                // has set itself up and comes back wrongly lit (one portrait was solid orange).
                camGo.transform.position = new Vector3(0f, EyeHeight, 0f);
                camGo.transform.rotation = Quaternion.Euler(-6f, 0f, 0f);
                cam.Render();

                // ---- 1. the sky alone, from the player's eye on the start pad, facing the eclipse ----
                Place(skyRoot, new Vector3(0f, EyeHeight, 0f));
                camGo.transform.position = new Vector3(0f, EyeHeight, 0f);
                camGo.transform.rotation = Quaternion.Euler(-6f, 0f, 0f);
                Shoot(cam, rt, tex, dir, "01_sky_from_the_players_eye", sb);

                camGo.transform.rotation = Quaternion.Euler(-34f, 0f, 0f);
                Shoot(cam, rt, tex, dir, "02_sky_looking_up", sb);

                camGo.transform.rotation = Quaternion.Euler(-6f, 118f, 0f);
                Shoot(cam, rt, tex, dir, "03_sky_away_from_the_eclipse", sb);

                // ---- 2. THE SILHOUETTE CHECK ----------------------------------------------------
                grunt = SpawnGrunt(sb);
                if (grunt != null)
                {
                    // Combat range, on open ground, the enemy squarely between the eye and the eclipse:
                    // the worst case for readability, because the enemy is near-black and so is the disc.
                    Vector3 eye = Open + Vector3.up * EyeHeight;
                    Place(skyRoot, eye);

                    grunt.transform.position = Open + new Vector3(0f, 0f, 4.0f);
                    grunt.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // facing the player
                    camGo.transform.position = eye;

                    // Aimed at the chest: the enemy's head and shoulders sit against bare sky, which is
                    // the part of the body a player actually tracks.
                    camGo.transform.LookAt(Open + new Vector3(0f, 1.15f, 4.0f));
                    Shoot(cam, rt, tex, dir, "04_enemy_at_combat_range_vs_sky", sb);
                    Measure(tex, sb, "04");

                    // Aimed level with the eclipse so the disc sits behind the enemy: black on black.
                    camGo.transform.rotation = Quaternion.Euler(-8f, 0f, 0f);
                    Shoot(cam, rt, tex, dir, "05_enemy_ON_the_black_disc", sb);
                    Measure(tex, sb, "05");

                    // Backlit at reaction distance, further out, where the body is smallest.
                    grunt.transform.position = Open + new Vector3(1.6f, 0f, 8.0f);
                    camGo.transform.LookAt(Open + new Vector3(1.6f, 1.2f, 8.0f));
                    Shoot(cam, rt, tex, dir, "06_enemy_at_8m_backlit", sb);
                }

                // ---- 3. wide shots of the level -------------------------------------------------
                Vector3 wide = new Vector3(-26f, 16f, 34f);
                Place(skyRoot, wide);
                camGo.transform.position = wide;
                camGo.transform.LookAt(new Vector3(2f, 6f, 110f));
                Shoot(cam, rt, tex, dir, "07_level_wide", sb);

                Vector3 route = new Vector3(0f, 6.6f, 118f);
                Place(skyRoot, route);
                camGo.transform.position = route;
                camGo.transform.LookAt(new Vector3(0f, 9f, 150f));
                Shoot(cam, rt, tex, dir, "08_level_from_the_route", sb);
            }
            finally
            {
                if (grunt != null) Object.DestroyImmediate(grunt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString());
            return sb.ToString();
        }

        // ---------------------------------------------------------------- helpers

        static void Place(GameObject skyRoot, Vector3 eye)
        {
            if (skyRoot != null) skyRoot.transform.position = eye;
        }

        static GameObject SpawnGrunt(StringBuilder sb)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy_Grunt.prefab");
            if (prefab == null) { sb.AppendLine("WARNING: no Enemy_Grunt prefab; silhouette shots skipped."); return null; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("Grunt"));
            var vis = inst.GetComponentInChildren<EnemyVisuals>(true);
            // Awake never runs in edit mode, and Awake is where the body albedo is applied. Without this
            // the frame photographs an untinted body and the contrast number would be a fiction.
            if (vis != null && data != null)
            {
                inst.transform.localScale = Vector3.one * Mathf.Max(0.01f, data.scale);
                // Awake never runs in edit mode, and Setup now depends on what Awake builds (the
                // EmissiveFlash handle, the property blocks). Awake is protected virtual and
                // self-contained, so invoking it by reflection is exactly the play-mode order.
                var awake = vis.GetType().GetMethod("Awake",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                if (awake != null) awake.Invoke(vis, null);
                vis.Setup(data);
                sb.AppendLine("grunt bodyColor " + ColorUtility.ToHtmlStringRGB(data.bodyColor) +
                              "  scale " + data.scale.ToString("0.00"));
            }
            return inst;
        }

        static void EnsureVolume(StringBuilder sb)
        {
            foreach (var v in Object.FindObjectsByType<Volume>())
                if (v.isGlobal && v.sharedProfile != null)
                {
                    sb.AppendLine("post volume already in scene: " + v.sharedProfile.name);
                    return;
                }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            if (profile == null) { sb.AppendLine("WARNING: no SampleSceneProfile; frames are ungraded."); return; }
            var go = new GameObject("~SkyShotVolume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = profile;
            sb.AppendLine("added a global Volume with the shipped SampleSceneProfile.");
        }

        static void Shoot(Camera cam, RenderTexture rt, Texture2D tex, string dir, string name, StringBuilder sb)
        {
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            sb.AppendLine("wrote " + name + ".png");
        }

        /// <summary>
        /// Mean sRGB of a box over the subject's torso (frame centre) and of bare sky to its left, plus
        /// the Weber contrast between them. This is the number that says whether the silhouette survives.
        /// </summary>
        static void Measure(Texture2D tex, StringBuilder sb, string tag)
        {
            Color body = Mean(tex, Width / 2 - 40, Height / 2 - 30, 80, 60);
            Color sky = Mean(tex, 90, Height - 190, 120, 90);
            float lb = Lum(body), ls = Lum(sky);
            float contrast = ls > 0.0001f ? Mathf.Abs(ls - lb) / Mathf.Max(ls, lb) : 0f;
            sb.AppendLine(string.Format(
                "  [{0}] body rgb({1},{2},{3}) lum {4:F1}/255   sky rgb({5},{6},{7}) lum {8:F1}/255   " +
                "Weber contrast {9:P0}   sky hue {10:F0} deg, sat {11:P0}",
                tag, B(body.r), B(body.g), B(body.b), lb * 255f,
                B(sky.r), B(sky.g), B(sky.b), ls * 255f, contrast, Hue(sky) * 360f, Sat(sky)));
        }

        static Color Mean(Texture2D tex, int x, int y, int w, int h)
        {
            x = Mathf.Clamp(x, 0, Width - 1); y = Mathf.Clamp(y, 0, Height - 1);
            w = Mathf.Min(w, Width - x); h = Mathf.Min(h, Height - y);
            var px = tex.GetPixels(x, y, w, h);
            float r = 0f, g = 0f, b = 0f;
            for (int i = 0; i < px.Length; i++) { r += px[i].r; g += px[i].g; b += px[i].b; }
            float n = Mathf.Max(1, px.Length);
            return new Color(r / n, g / n, b / n, 1f);
        }

        static int B(float v) { return Mathf.RoundToInt(Mathf.Clamp01(v) * 255f); }
        static float Lum(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }
        static float Hue(Color c) { float h, s, v; Color.RGBToHSV(c, out h, out s, out v); return h; }
        static float Sat(Color c) { float h, s, v; Color.RGBToHSV(c, out h, out s, out v); return s; }
    }
}
