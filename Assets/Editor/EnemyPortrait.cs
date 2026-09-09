using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Photographs an enemy prefab from the player's actual eye, at the distance that enemy is actually
    /// fought at, from four angles — and, for a burning enemy, with its aura glow applied.
    ///
    /// <para><b>Why.</b> <see cref="ForgeModelProbe"/> gives you the numbers to write a <c>ModelSpec</c>
    /// with; this is how you find out whether those numbers put the eye on the face and the deathblow
    /// glyph on the chest rather than a metre into the air. Three separate times now this project has
    /// had a confident derivation contradicted by looking at the rendered frame — eleven wind-up poses
    /// whose authored angles were not their on-screen angles, a whirl curve that measured perfectly and
    /// read as a stutter, and an alias bound that was reasoning about the wrong quantity. Measure, then
    /// photograph.</para>
    ///
    /// <para>Edit mode and headless, so it runs with the editor busy and with no MCP bridge. That also
    /// means no <c>Awake</c> or <c>Update</c> runs: an <see cref="EmberAura"/>'s ember particles will
    /// NOT appear here, because they are spawned by its update loop. The body GLOW is faked in by
    /// calling <see cref="EnemyVisuals.SetAura"/> directly, which is the same channel the aura drives,
    /// so the emission you see is real. The particles are not, and the report says so rather than
    /// leaving you to assume.</para>
    /// </summary>
    public static class EnemyPortrait
    {
        const int Size = 640;

        [MenuItem("VibeGame1/Photograph Enemies")]
        public static void Menu()
        {
            Debug.Log(Shoot("Legendary_Revenant",
                            Path.Combine(Directory.GetCurrentDirectory(), "Portraits")));
        }

        [MenuItem("VibeGame1/Photograph Heavy Sentry")]
        public static void HeavySentryMenu()
        {
            Debug.Log(Shoot("pshooter_enemy02",
                            Path.Combine(Directory.GetCurrentDirectory(), "Portraits", "pshooter_enemy02")));
        }

        /// <summary>Batch entry: <c>-executeMethod VibeGame1.EditorTools.EnemyPortrait.Batch</c>.</summary>
        public static void Batch()
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), "Portraits");
            var sb = new StringBuilder();
            foreach (var n in new[] { "Legendary_Revenant", "Legendary_Marionette" })
                sb.AppendLine(Shoot(n, Path.Combine(root, n)));
            File.WriteAllText(Path.Combine(root, "report.txt"), sb.ToString());
            Debug.Log(sb.ToString());
            EditorApplication.Exit(0);
        }

        public static string Shoot(string prefabName, string dir)
        {
            var sb = new StringBuilder();
            Directory.CreateDirectory(dir);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + prefabName + ".prefab");
            if (prefab == null) return "FAIL: no prefab " + prefabName;
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data(prefabName));

            sb.AppendLine("=== " + prefabName);
            if (data != null)
                sb.AppendLine(string.Format("  {0}   scale {1:F2}   preferredRange {2:F2} m",
                                            data.displayName, data.scale, data.preferredRange));

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            GameObject inst = null, camGo = null, lightGo = null;
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            var shot = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            try
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.transform.position = Vector3.zero;
                if (data != null) inst.transform.localScale = Vector3.one * Mathf.Max(0.01f, data.scale);

                var vis = inst.GetComponentInChildren<EnemyVisuals>(true);
                if (vis != null && data != null)
                {
                    // Edit-mode prefab instances do not receive Unity lifecycle messages. Setup expects
                    // Awake's flash/property-block bindings, so establish that exact runtime state first.
                    var awake = typeof(EnemyVisuals).GetMethod("Awake",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (awake != null) awake.Invoke(vis, null);
                    // Setup applies the enemy's own body colour, which Awake would normally do.
                    vis.Setup(data);
                    // Prefer the component, but fall back to the enemy's own emission colour. The
                    // fallback is not decoration: a prefab whose EmberAura resolves to a MISSING SCRIPT
                    // (a remapped .meta guid, a compile error) hands back null here, and silently
                    // photographing an unlit body would report "no fire" for a build that is fine.
                    var aura = inst.GetComponentInChildren<EmberAura>(true);
                    if (aura != null)
                    {
                        vis.SetAura(aura.emberHot, aura.glowAtRest);
                        sb.AppendLine(string.Format("  EmberAura: glow {0:F2} at rest, {1:F2} at break; " +
                                                    "embers {2} over y {3:F2}..{4:F2}",
                                                    aura.glowAtRest, aura.glowAtBreak, aura.emberCount,
                                                    aura.emberFromHeight, aura.emberToHeight));
                        sb.AppendLine("  NOTE: the body glow below is REAL; the ember particles are NOT " +
                                      "in these frames (they are spawned by the aura's update loop, and " +
                                      "nothing updates in edit mode).");
                    }
                    else if (data.emission.maxColorComponent > 0.01f)
                    {
                        vis.SetAura(data.emission, 0.22f);
                        sb.AppendLine("  aura component did NOT resolve on the instance — glow faked from " +
                                      "EnemyData.emission instead. If this enemy is meant to burn, check " +
                                      "the prefab for a missing script before trusting these frames.");
                    }
                }

                // Where the deathblow glyph and the eye actually ended up, so a bad pivot is a NUMBER
                // and not just something you might notice in the picture.
                if (vis != null)
                {
                    if (vis.eye != null)
                        sb.AppendLine(string.Format("  eye at      y {0:F2}", vis.eye.transform.position.y));
                    if (vis.deathblowMarker != null)
                        sb.AppendLine(string.Format("  deathblow   y {0:F2}",
                                                    vis.deathblowMarker.transform.position.y));
                    var r = vis.body != null ? vis.body : inst.GetComponentInChildren<Renderer>(true);
                    if (r != null)
                        sb.AppendLine(string.Format("  body spans  y {0:F2} .. {1:F2}",
                                                    r.bounds.min.y, r.bounds.max.y));
                }

                lightGo = new GameObject("Sun");
                var sun = lightGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.9f;
                lightGo.transform.rotation = Quaternion.Euler(35f, 145f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.30f, 0.31f, 0.36f);

                camGo = new GameObject("Cam");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 70f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
                cam.targetTexture = rt;

                float range = data != null ? data.preferredRange : 3.6f;

                // WARM-UP RENDER, discarded. Unity's first Render() in batch mode comes back before the
                // render pipeline has finished setting up and produces a flat, wrongly-lit frame — the
                // first Revenant portrait came out as a solid orange silhouette and would have been read
                // as "the body material is broken". Render once into the void before believing anything.
                camGo.transform.position = new Vector3(0f, 1.6f, -range);
                camGo.transform.LookAt(new Vector3(0f, 1.1f, 0f));
                cam.Render();

                foreach (var a in new[] { 0f, 45f, 90f, 180f })
                {
                    // The PLAYER's eye height, at the range this enemy is actually fought at — not a
                    // flattering turntable. A silhouette that only reads from a product shot is not a
                    // silhouette the player ever sees.
                    Vector3 p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, -range);
                    camGo.transform.position = p + Vector3.up * 1.6f;
                    camGo.transform.LookAt(new Vector3(0f, 1.1f, 0f));

                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    shot.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    shot.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, string.Format("{0:F0}deg.png", a)),
                                       shot.EncodeToPNG());
                }
                sb.AppendLine("  wrote 4 frames to " + dir);
            }
            finally
            {
                if (inst != null) Object.DestroyImmediate(inst);
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (lightGo != null) Object.DestroyImmediate(lightGo);
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(shot);
            }
            return sb.ToString();
        }
    }
}
