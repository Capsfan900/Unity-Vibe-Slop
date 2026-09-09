using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Photographs a route from the position and facing a player actually arrives at.
    ///
    /// <para><b>Why this exists.</b> The first wall-jump line built for The Ascent was mechanically
    /// correct and was cut anyway: a screenshot from the player's own approach showed it as one
    /// undifferentiated black slab a metre from the face. Arithmetic said "climbable"; the picture said
    /// "invisible". Both questions have to be asked, and only one of them is a number.</para>
    ///
    /// <para>Edit mode and headless: it opens the level scene, rebuilds it from the definition so the
    /// frames show the SHIPPED geometry rather than whatever was last left in the scene, then renders a
    /// list of eye positions at the player's eye height and field of view.</para>
    /// </summary>
    public static class LevelRouteShots
    {
        const int Width = 960, Height = 540;
        const float EyeHeight = 1.6f;
        const float Fov = 70f;

        struct Shot
        {
            public string name;
            public Vector3 feet;      // where the player is standing
            public Vector3 lookAt;
            public Shot(string n, Vector3 f, Vector3 l) { name = n; feet = f; lookAt = l; }
        }

        static readonly Shot[] Shots =
        {
            // THE ASCENT — every angle the buttress has to survive. The analyser's own answer for the
            // entry is "off T2_L2 at z 125, aimed 12 deg south of due west", so shot 02 stands exactly
            // there and looks exactly that way: this is the frame that decides whether the slot reads.
            new Shot("01_arriving_on_T2_L1_looking_at_the_tower",
                     new Vector3(10f, 5f, 147f), new Vector3(3f, 12f, 162f)),
            new Shot("02_the_entry_on_T2_L2_looking_into_the_slot",
                     new Vector3(11f, 6.5f, 160f), new Vector3(7.6f, 10f, 160.5f)),
            new Shot("03_on_T2_L2_looking_at_the_L3_hop",
                     new Vector3(11f, 6.5f, 162f), new Vector3(0.5f, 8f, 170f)),
            new Shot("04_below_on_T2_Entry_looking_up_the_tower",
                     new Vector3(10f, 5f, 146.5f), new Vector3(0f, 13f, 163f)),
            new Shot("05_topping_out_on_T2_L8",
                     new Vector3(11f, 15.5f, 162f), new Vector3(0.5f, 17f, 170f)),
            new Shot("08_crossing_T2_L2_from_the_south",
                     new Vector3(11f, 6.5f, 159f), new Vector3(15.7f, 8f, 166f)),

            // THE LONG SPAN — the slide gate, from the run-in.
            new Shot("06_span_run_in_to_the_lintel",
                     new Vector3(0f, 24.5f, 285.5f), new Vector3(0f, 25.6f, 297f)),
            new Shot("07_span_lintel_close",
                     new Vector3(0f, 24.5f, 289f), new Vector3(0f, 25.8f, 295f)),
        };

        [MenuItem("VibeGame1/Photograph The New Routes", priority = 301)]
        public static void Menu() { Run(); }

        public static void Run()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "RouteShots");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();

            // OPEN THE SCENE FIRST, THEN LOAD THE ASSET. OpenScene in Single mode unloads unused assets,
            // and a LevelDefinition held only by a local variable is "unused": the reference survives as
            // Unity's fake-null and the builder quietly refuses with "Null LevelDefinition", leaving the
            // PREVIOUS build in the scene. The frames then look plausible and show the wrong level, which
            // is the worst possible failure for a tool whose whole job is to be believed.
            var probe = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelArcReport.DefaultLevel);
            if (probe == null) { Debug.LogError("No level definition at " + LevelArcReport.DefaultLevel); return; }
            string scenePath = "Assets/Scenes/" + probe.sceneName + ".unity";

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelArcReport.DefaultLevel);
            if (def == null) { Debug.LogError("Level definition unloaded by OpenScene."); return; }
            LevelDefinitionBuilder.Build(def);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var camGo = new GameObject("~RouteCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = Fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.targetTexture = rt;

            try
            {
                // Unity's FIRST cam.Render() in batch mode returns before the pipeline has finished
                // setting itself up and produces a frame with the wrong lighting — see ENGINEERING-LOG,
                // "Unity's first cam.Render() in batch mode is a lie". Burn one into the void.
                camGo.transform.position = Shots[0].feet + Vector3.up * EyeHeight;
                camGo.transform.LookAt(Shots[0].lookAt);
                cam.Render();

                foreach (var s in Shots)
                {
                    camGo.transform.position = s.feet + Vector3.up * EyeHeight;
                    camGo.transform.LookAt(s.lookAt);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    string path = Path.Combine(dir, s.name + ".png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    sb.AppendLine("wrote " + path);
                }
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            Debug.Log(sb.ToString());
        }
    }
}
