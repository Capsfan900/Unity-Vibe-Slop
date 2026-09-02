using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Photographs the span-1 wall-run lines from where a player actually stands when they have to
    /// decide to take them. The sibling of <see cref="LevelRouteShots"/> (which owns The Ascent and The
    /// Long Span); kept separate so the two can be edited independently.
    ///
    /// <para>A wall you cannot see coming is a wall you cannot plan a run onto — the chimney that was cut
    /// from The Ascent was mechanically fine and invisible from its approach. So every wall here is shot
    /// from its approach, at eye height, at the game's field of view, and the frames are looked at.</para>
    ///
    /// <para>Two traps, both inherited from <c>LevelRouteShots</c>: the first render in batch mode is
    /// discarded, and the definition is loaded AFTER the scene is opened (OpenScene unloads unused assets;
    /// a definition held only by a local goes fake-null, the builder refuses in the log, and you photograph
    /// the OLD scene).</para>
    ///
    /// <para>Headless: <c>-executeMethod VibeGame1.EditorTools.LevelSpan1Shots.Run</c>. Writes
    /// <c>RouteShots/span1_*.png</c> beside the project.</para>
    /// </summary>
    public static class LevelSpan1Shots
    {
        const int Width = 960, Height = 540;
        const float EyeHeight = 1.6f;
        const float Fov = 70f;

        struct Shot
        {
            public string name;
            public Vector3 feet, lookAt;
            public Shot(string n, Vector3 f, Vector3 l) { name = n; feet = f; lookAt = l; }
        }

        static readonly Shot[] Shots =
        {
            // THE OPENING LINE. Spawn is (0, 1.2, -4) facing +z: the first frame of the game.
            new Shot("span1_01_spawn_looking_up_the_course",
                     new Vector3(0f, 0f, -4f), new Vector3(0f, 1.5f, 20f)),
            // The run-up: the analyser's mounts come off the pad's north-east corner around (7.5, 0, 2..5)
            // aimed a few degrees left of the face. Stand there and look where you would look.
            new Shot("span1_02_start_pad_run_up_to_the_wall",
                     new Vector3(6.5f, 0f, 2f), new Vector3(6.2f, 2f, 22f)),
            // Mid-run, pressed to the face at x 6.1, y ~2: what the wall and the exit look like from the wall.
            new Shot("span1_03_on_the_start_wall_looking_for_stone_4",
                     new Vector3(6.1f, 2.2f, 23f), new Vector3(2f, 1f, 38f)),
            // From the stepping stones: the wall as backdrop to the slow route.
            new Shot("span1_04_from_stone_1_looking_at_stone_2_and_the_wall",
                     new Vector3(0f, 0f, 14f), new Vector3(4f, 1f, 24f)),

            // THE CAUSEWAY WALL. Arriving on Stone_4 (top 1.5) from Stone_3 (x -3.5) or off the opening wall.
            new Shot("span1_05_arriving_on_stone_4_from_stone_3",
                     new Vector3(-3.5f, 1f, 31f), new Vector3(2f, 2.5f, 45f)),
            // The mount: the analyser's longest run enters at z 43.6 off Stone_4's north-east corner.
            new Shot("span1_06_stone_4_north_east_corner_the_mount",
                     new Vector3(1.8f, 1.5f, 39.5f), new Vector3(3.2f, 2.5f, 52f)),
            // From the causeway itself, between the rails, with the wall on the right and the lintel ahead.
            new Shot("span1_07_on_the_causeway_wall_on_the_right",
                     new Vector3(0f, 2f, 45f), new Vector3(2.5f, 3f, 60f)),
            // Mid-run on the causeway wall: the landing and the obelisk beyond it.
            new Shot("span1_08_on_the_causeway_wall_looking_for_the_landing",
                     new Vector3(3.0f, 2.8f, 50f), new Vector3(4f, 2.5f, 66f)),
            // On the landing, looking at Stone_5 and the arena gate: the rejoin.
            new Shot("span1_09_on_the_landing_looking_at_stone_5",
                     new Vector3(4.6f, 2.5f, 63f), new Vector3(0f, 3.5f, 74f)),
        };

        [MenuItem("VibeGame1/Photograph Span 1", priority = 303)]
        public static void Menu() { Run(); }

        public static void Run()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "RouteShots");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();

            var probe = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelArcReport.DefaultLevel);
            if (probe == null) { Debug.LogError("No level definition at " + LevelArcReport.DefaultLevel); return; }
            string scenePath = "Assets/Scenes/" + probe.sceneName + ".unity";

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // AFTER OpenScene, on purpose — see the class comment.
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelArcReport.DefaultLevel);
            if (def == null) { Debug.LogError("Level definition unloaded by OpenScene."); return; }
            LevelDefinitionBuilder.Build(def);
            if (GameObject.Find("Level/T1_Wall_Start") == null)
            {
                Debug.LogError("[LevelSpan1Shots] The built scene has no T1_Wall_Start — the builder refused, and these frames would show the OLD level. Aborting.");
                return;
            }

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var camGo = new GameObject("~Span1Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = Fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.targetTexture = rt;

            try
            {
                // The first render in batch mode is a lie; burn it.
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
