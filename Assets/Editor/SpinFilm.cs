using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Renders THE PALE MARIONETTE's whirl at the exact per-frame yaw a player at a given frame rate
    /// receives, and writes both the frames and the angular step between them.
    ///
    /// <para><b>Why this exists.</b> The whirl's peak is ~3306 °/s. Whether that reads as one body coming
    /// around at you or as strobing noise is a question about the RENDERED frames, and no amount of
    /// arithmetic answers it — the same lesson as the eleven wind-up poses whose authored angles were not
    /// their on-screen angles. This samples <see cref="PuppetVisuals.Ease"/> at 1/60 s intervals across
    /// one pass, writes the yaw onto the prefab's real <c>SpinRoot</c>, and photographs it.</para>
    ///
    /// <para><b>It is an EDIT-mode tool and runs headless.</b> No play mode, no <c>Time.deltaTime</c>, no
    /// game loop — it drives the transform directly at the frame times a player would see, so it works
    /// under <c>-batchmode -executeMethod</c> on a copy of the project. That matters because the editor
    /// is often busy, and a verification you cannot run is not a verification.</para>
    ///
    /// <para>The numbers it prints are the point as much as the images: <b>degrees of body yaw per
    /// rendered frame</b>, which is the quantity that decides whether rotation reads as rotation. Past
    /// roughly 90° per frame a 2-fold-symmetric silhouette (a humanoid with its arms out) aliases into
    /// apparent random orientation. <see cref="PuppetVisuals.ResolvePeak"/> exists to keep it under that;
    /// this is how you check that it does.</para>
    /// </summary>
    public static class SpinFilm
    {
        const string Prefab = "Assets/Prefabs/Legendary_Marionette.prefab";
        const int Size = 512;
        /// <summary>EnemyController.cueLead — the arrival window the player actually has to read.</summary>
        const float CueLead = 0.28f;

        /// <summary>
        /// Pixels of the frame that are not the clear colour and not the yellow alert bar, i.e. the
        /// body's on-screen area. Crude on purpose: the question is how much SHAPE is present, and a
        /// silhouette count answers it without needing a stencil pass.
        /// </summary>
        static int Silhouette(Texture2D tex, Color32 bg)
        {
            var px = tex.GetPixels32();
            int n = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i];
                if (Mathf.Abs(p.r - bg.r) + Mathf.Abs(p.g - bg.g) + Mathf.Abs(p.b - bg.b) < 24) continue;
                if (p.r > 180 && p.g > 180 && p.b < 170) continue;   // the alert bar, not the body
                n++;
            }
            return n;
        }

        [MenuItem("VibeGame1/Film the Whirl")]
        public static void Menu()
        {
            Capture(Path.Combine(Directory.GetCurrentDirectory(), "SpinFilm"), 60f);
        }

        /// <summary>
        /// Films one spin pass. Returns a human-readable report; also writes it to
        /// <paramref name="dir"/>/report.txt next to the frames.
        /// </summary>
        /// <param name="fps">Frame rate to SAMPLE at. This is the player's frame rate, not a render setting.</param>
        public static string Capture(string dir, float fps)
        {
            var sb = new StringBuilder();
            Directory.CreateDirectory(dir);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if (prefab == null) return "FAIL: " + Prefab + " not found.";

            var pv = prefab.GetComponentInChildren<PuppetVisuals>(true);
            if (pv == null) return "FAIL: prefab has no PuppetVisuals.";

            // The pass the whirl is anchored to, read from the SHIPPED attack asset rather than assumed.
            var pass = AssetDatabase.LoadAssetAtPath<EnemyAttackData>("Assets/Data/Attacks/Marionette_SpinPass.asset");
            if (pass == null) return "FAIL: Marionette_SpinPass.asset not found.";

            float passSeconds = pass.windup + pass.impactDelay;
            float arc = 360f;                       // one revolution per pass, as BeginPass tops up to
            float average = arc / passSeconds;
            float peak = PuppetVisuals.ResolvePeak(pv.spinPeakMultiple, pv.spinTailMultiple,
                                                   average, 1f / fps, pv.maxDegPerFrame);

            sb.AppendLine("THE WHIRL, sampled at " + fps.ToString("F0") + " fps");
            sb.AppendLine("  pass            " + passSeconds.ToString("F3") + " s for one revolution");
            sb.AppendLine("  average         " + average.ToString("F0") + " deg/s");
            sb.AppendLine("  authored peak   " + pv.spinPeakMultiple.ToString("F2") + "x");
            sb.AppendLine("  effective peak  " + peak.ToString("F2") + "x"
                          + (peak < pv.spinPeakMultiple - 1e-3f ? "   <-- ALIAS GUARD ENGAGED" : "   (guard slack)"));
            sb.AppendLine("  tail            " + pv.spinTailMultiple.ToString("F2") + "x");
            sb.AppendLine();

            // An empty scene so nothing else is lit, in frame, or casting onto the body.
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
                var live = inst.GetComponentInChildren<PuppetVisuals>(true);
                var spin = live != null ? live.spinRoot : null;
                if (spin == null) return "FAIL: instantiated prefab has no spinRoot.";

                var d = AssetDatabase.LoadAssetAtPath<EnemyData>("Assets/Data/Enemies/Legendary_Marionette.asset");

                lightGo = new GameObject("Sun");
                var sun = lightGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 2.2f;
                lightGo.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
                // A lit ambient, or the body is a black cut-out and the silhouette cannot be judged at
                // all. This is a CAPTURE choice, not the game's lighting — the question this tool asks
                // is about SHAPE over time, and an unlit shape answers nothing.
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.45f, 0.46f, 0.5f);

                // EnemyVisuals pushes EnemyData.bodyColor through a MaterialPropertyBlock in Awake, and
                // Awake does not run in edit mode — so without this the body renders in whatever
                // M_Enemy's albedo happens to be rather than the bone-white it ships as. Same channel
                // the game uses, so the frames show the real body.
                if (d != null)
                {
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_BaseColor", d.bodyColor);
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                        r.SetPropertyBlock(mpb);
                }

                camGo = new GameObject("Cam");
                var cam = camGo.AddComponent<Camera>();
                // The player's actual eye: preferredRange out, standing height, looking at the sternum.
                float range = d != null ? d.preferredRange : 3.7f;
                camGo.transform.position = new Vector3(0f, 1.6f, -range);
                camGo.transform.LookAt(new Vector3(0f, 1.1f, 0f));
                cam.fieldOfView = 70f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.06f, 0.06f, 0.08f);
                cam.targetTexture = rt;

                int frames = Mathf.CeilToInt(passSeconds * fps) + 1;
                float prevPhase = float.NaN, maxStep = 0f, minStep = float.MaxValue;
                var steps = new float[frames];
                var areas = new int[frames];
                var bg = cam.backgroundColor;

                for (int i = 0; i < frames; i++)
                {
                    float t = Mathf.Min(i / fps, passSeconds);
                    float k = t / passSeconds;
                    // Exactly what PuppetVisuals.Update writes: phase counts DOWN to 0 at impact.
                    float phase = arc * (1f - PuppetVisuals.Ease(k, peak, pv.spinTailMultiple));
                    spin.localRotation = Quaternion.Euler(0f, -phase, 0f);

                    if (!float.IsNaN(prevPhase))
                    {
                        float step = Mathf.Abs(prevPhase - phase);
                        steps[i] = step;
                        if (step > maxStep) maxStep = step;
                        if (step < minStep) minStep = step;
                    }
                    prevPhase = phase;

                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    shot.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    shot.Apply();
                    RenderTexture.active = prev;

                    areas[i] = Silhouette(shot, bg);

                    File.WriteAllBytes(
                        Path.Combine(dir, string.Format("f{0:D2}_t{1:F3}_yaw{2:F0}.png", i, t, phase)),
                        shot.EncodeToPNG());
                }

                sb.AppendLine("per-frame yaw step (the quantity that decides whether it reads as rotation)");
                sb.AppendLine("  frames captured " + frames);
                sb.AppendLine("  fastest step    " + maxStep.ToString("F1") + " deg/frame");
                sb.AppendLine("  slowest step    " + (minStep == float.MaxValue ? 0f : minStep).ToString("F1") + " deg/frame");
                sb.AppendLine("  alias threshold ~90 deg/frame for a 2-fold-symmetric silhouette");
                sb.AppendLine("  verdict         " + (maxStep < 90f
                    ? "UNDER the threshold — should read as rotation"
                    : "OVER the threshold — will alias into apparent random orientation"));
                sb.AppendLine();
                sb.AppendLine("  step per frame: ");
                for (int i = 1; i < frames; i++)
                    sb.Append(steps[i].ToString("F0")).Append(i == frames - 1 ? "\n" : ", ");

                // ---- silhouette AREA, which is the failure mode the degrees-per-frame bound misses --
                // This body is strongly anisotropic: wide from the front and back, close to a sliver
                // edge-on. So twice per revolution its on-screen area collapses, and at speed that
                // reads as FLICKER rather than as orientation ambiguity -- a different failure from the
                // one ~90 deg/frame bounds, and one that starts well below it. The number that decides
                // whether the fight is readable is therefore the area stability across the ARRIVAL, not
                // across the whole pass: the blur is meant to be unreadable, the arrival is not.
                int widest = 1;
                for (int i = 0; i < frames; i++) if (areas[i] > widest) widest = areas[i];

                float worstAll = 1f, worstArrival = 1f, sumArrival = 0f;
                int nArrival = 0;
                for (int i = 1; i < frames; i++)
                {
                    float lo = Mathf.Max(1, Mathf.Min(areas[i], areas[i - 1]));
                    float r = Mathf.Max(areas[i], areas[i - 1]) / lo;
                    if (r > worstAll) worstAll = r;
                    // The arrival = from the parry cue onward, which is what the player must read.
                    if (i / fps >= passSeconds - CueLead)
                    {
                        if (r > worstArrival) worstArrival = r;
                        sumArrival += r; nArrival++;
                    }
                }

                sb.AppendLine();
                sb.AppendLine("silhouette area (the anisotropy check the degrees bound does not cover)");
                sb.AppendLine("  worst frame-to-frame change, WHOLE pass  x" + worstAll.ToString("F2")
                              + "   (the blur; deliberately unreadable)");
                sb.AppendLine("  worst across the ARRIVAL (last " + CueLead.ToString("F2") + " s)  x"
                              + worstArrival.ToString("F2"));
                sb.AppendLine("  mean across the ARRIVAL                  x"
                              + (nArrival > 0 ? sumArrival / nArrival : 1f).ToString("F2")
                              + "   over " + nArrival + " frames");
                sb.AppendLine("  verdict  " + (worstArrival < 1.35f
                    ? "the arrival is SMOOTH — the body opens up into the player legibly"
                    : "the ARRIVAL flickers; the read the whole fight depends on is compromised"));
                sb.AppendLine();
                sb.AppendLine("  area as % of the widest silhouette: ");
                for (int i = 0; i < frames; i++)
                    sb.Append((100f * areas[i] / widest).ToString("F0"))
                      .Append(i == frames - 1 ? "\n" : ", ");
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

            string report = sb.ToString();
            File.WriteAllText(Path.Combine(dir, "report.txt"), report);
            Debug.Log("[SpinFilm] wrote frames + report to " + dir + "\n" + report);
            return report;
        }

        /// <summary>Batch entry point: <c>-executeMethod VibeGame1.EditorTools.SpinFilm.Batch</c>.</summary>
        public static void Batch()
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), "SpinFilm");
            var all = new StringBuilder();
            // 60 fps is the target; 30 is where the alias guard is supposed to start earning its keep.
            foreach (float fps in new[] { 60f, 30f })
                all.AppendLine(Capture(Path.Combine(root, fps.ToString("F0") + "fps"), fps)).AppendLine();
            File.WriteAllText(Path.Combine(root, "all.txt"), all.ToString());
            EditorApplication.Exit(0);
        }
    }
}
