using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Photographs span 4 — the causeway from the Spellsword arena to the Hollow Warden — from the
    /// positions and facings a player actually arrives at. Same discipline as <see cref="LevelRouteShots"/>:
    /// the arithmetic says the faces are runnable; only a frame from the approach says they READ.
    ///
    /// <para>Edit mode and headless. Opens the level scene, rebuilds it from the definition so the frames
    /// show the shipped geometry, then renders each eye position at the player's eye height and FOV.</para>
    /// </summary>
    public static class LevelSpan4Shots
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
            // Leaving the Spellsword arena: the whole causeway and the first face, one step past the
            // exit gate. (Stand INSIDE the arena and you photograph the gate, which rests closed.)
            new Shot("s4_01_leaving_T3_gate_down_the_causeway",
                     new Vector3(0f, 28f, 284f), new Vector3(1.5f, 30f, 312f)),
            // The first take-off, from where a sprinting player leaves the deck.
            new Shot("s4_02_causeway_end_the_first_face",
                     new Vector3(0.5f, 28f, 298f), new Vector3(3.5f, 30.5f, 314f)),
            // The slow line, from the same deck: the stone on the open side.
            new Shot("s4_03_causeway_end_the_slow_stone",
                     new Vector3(-1.5f, 28f, 301f), new Vector3(-6f, 28.5f, 311.5f)),
            // Mid-run on the first face, looking down the line at the pier.
            new Shot("s4_04_on_face_E1_looking_down_the_line",
                     new Vector3(3.2f, 28.8f, 309f), new Vector3(1f, 28.5f, 322f)),
            // Landed on Pier_1: the second face, on the other side.
            new Shot("s4_05_pier_1_the_second_face",
                     new Vector3(0f, 28f, 319.5f), new Vector3(-3.5f, 30.5f, 336f)),
            // Landed on Pier_2: the last face and the arena mouth beyond it.
            new Shot("s4_06_pier_2_the_last_face_and_the_arena_mouth",
                     new Vector3(0f, 28f, 340.5f), new Vector3(3f, 30.5f, 360f)),
            // The delivery: coming off the last face onto the threshold, the rails and the gate ahead.
            new Shot("s4_07_arriving_on_the_threshold",
                     new Vector3(1f, 28f, 364f), new Vector3(0f, 29.5f, 391f)),
            // The whole span from above and behind, for the record.
            new Shot("s4_08_overview_from_above_the_T3_arena",
                     new Vector3(-14f, 46f, 272f), new Vector3(0f, 28f, 345f)),
        };

        [MenuItem("VibeGame1/Photograph Span 4", priority = 302)]
        public static void Menu() { Run(); }

        public static void Run()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "RouteShots");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();

            // OPEN THE SCENE FIRST, THEN LOAD THE ASSET — OpenScene in Single mode unloads a definition
            // that is only held by a local, and the builder then quietly refuses. See LevelRouteShots.
            var probe = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelArcReport.DefaultLevel);
            if (probe == null) { Debug.LogError("No level definition at " + LevelArcReport.DefaultLevel); return; }
            string scenePath = "Assets/Scenes/" + probe.sceneName + ".unity";

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelArcReport.DefaultLevel);
            if (def == null) { Debug.LogError("Level definition unloaded by OpenScene."); return; }
            LevelDefinitionBuilder.Build(def);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var camGo = new GameObject("~Span4Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = Fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.targetTexture = rt;

            try
            {
                // The first cam.Render() in batch mode is a lie — burn it.
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
