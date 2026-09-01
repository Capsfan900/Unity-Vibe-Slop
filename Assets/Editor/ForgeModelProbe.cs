using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Measures an imported forge model so its <c>ModelSpec</c> can be written from numbers instead of
    /// guesses: mesh bounds, the bone list, and — per clip — how far apart the hands are held and how
    /// high they ride.
    ///
    /// <para><b>Why this exists.</b> <c>docs/AUTHORING.md</c> already says "pick the clip by measuring,
    /// not by name", and the Marionette's spin clip was chosen exactly that way: <c>AttackSwing</c> is
    /// the obvious name and it tucks the arms to a 0.76 m span, while <c>Roar</c> holds 2.0 m for its
    /// whole length. But that measurement was made ad hoc through the MCP bridge and never became a
    /// tool, so the next model either repeats the work by hand or — far more likely — skips it and
    /// picks by name. Every pivot in a <c>ModelSpec</c> (eye, shoulder, hand, deathblow height) is a
    /// number somebody has to get from somewhere; this is where they come from.</para>
    ///
    /// <para>Runs headless under <c>-batchmode -executeMethod</c>, so it works with the editor busy and
    /// with no MCP bridge.</para>
    /// </summary>
    public static class ForgeModelProbe
    {
        const string ModelDir = "Assets/Enemies";

        [MenuItem("VibeGame1/Probe Forge Models")]
        public static void Menu() { Debug.Log(ProbeAll()); }

        /// <summary>Batch entry: <c>-executeMethod VibeGame1.EditorTools.ForgeModelProbe.Batch</c>.</summary>
        public static void Batch()
        {
            string report = ProbeAll();
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "forge_probe.txt"), report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }

        public static string ProbeAll()
        {
            var sb = new StringBuilder();
            foreach (var path in Directory.GetFiles(ModelDir, "*.fbx"))
                sb.AppendLine(Probe(path.Replace('\\', '/'))).AppendLine();
            return sb.ToString();
        }

        public static string Probe(string fbxPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=========== " + fbxPath);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (prefab == null) return sb.AppendLine("  could not load").ToString();

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                // ---- bounds, in the model's own space ------------------------------------------
                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var mfs = go.GetComponentsInChildren<MeshFilter>(true);
                bool any = false;
                Bounds b = new Bounds();
                foreach (var r in smrs)
                {
                    var bb = r.bounds;
                    if (!any) { b = bb; any = true; } else b.Encapsulate(bb);
                }
                foreach (var f in mfs)
                {
                    if (f.sharedMesh == null) continue;
                    var bb = new Bounds(f.transform.TransformPoint(f.sharedMesh.bounds.center),
                                        f.sharedMesh.bounds.size);
                    if (!any) { b = bb; any = true; } else b.Encapsulate(bb);
                }

                if (!any) sb.AppendLine("  NO renderers — the FBX has no mesh?");
                else
                {
                    sb.AppendLine(string.Format(
                        "  bounds  size ({0:F2} w, {1:F2} h, {2:F2} d)   centre ({3:F2},{4:F2},{5:F2})",
                        b.size.x, b.size.y, b.size.z, b.center.x, b.center.y, b.center.z));
                    sb.AppendLine(string.Format("  y range {0:F2} .. {1:F2}", b.min.y, b.max.y));
                    // Which way it faces: a humanoid is wide across the shoulders and thin front-to-back.
                    sb.AppendLine("  facing  " + (b.size.x >= b.size.z
                        ? "+Z (wide in X, thin in Z) — yaw 0 is correct"
                        : "+X (wide in Z!) — the spec probably needs yaw = 90"));
                    sb.AppendLine(string.Format(
                        "  SUGGESTED  markHeight ~{0:F2}   (0.74 of height, clear of the head)",
                        b.min.y + b.size.y * 0.74f));
                }

                // ---- skeleton -----------------------------------------------------------------
                var bones = new List<Transform>(go.GetComponentsInChildren<Transform>(true));
                sb.AppendLine("  bones (" + bones.Count + "):");
                var line = new StringBuilder("   ");
                for (int i = 0; i < bones.Count; i++)
                {
                    line.Append(' ').Append(bones[i].name);
                    if (line.Length > 96) { sb.AppendLine(line.ToString()); line = new StringBuilder("   "); }
                }
                if (line.Length > 3) sb.AppendLine(line.ToString());

                Transform lh = Find(bones, "LeftHand", "Hand_L", "hand_l", "L_Hand");
                Transform rh = Find(bones, "RightHand", "Hand_R", "hand_r", "R_Hand");

                // The pivots a ModelSpec actually needs, read off the SKELETON rather than inferred from
                // the bounds. On a silhouette with tall shoulder spikes the two disagree badly — the
                // EmberRevenant's mesh reaches y 1.96 while its head bone is at 1.20, so a bounds-derived
                // eye or deathblow glyph would float most of a metre above the body.
                sb.AppendLine("  key bones (bind pose):");
                foreach (var n in new[] { "Hips", "Spine", "Chest", "Neck", "Head",
                                          "RightShoulder", "RightUpperArm", "RightHand" })
                {
                    var t = Find(bones, n);
                    if (t != null)
                        sb.AppendLine(string.Format("    {0,-14} ({1,6:F2},{2,6:F2},{3,6:F2})",
                                                    n, t.position.x, t.position.y, t.position.z));
                }

                var chest = Find(bones, "Chest");
                var headB = Find(bones, "Head");
                var shoulder = Find(bones, "RightShoulder");
                var hand = Find(bones, "RightHand");
                sb.AppendLine("  SUGGESTED ModelSpec, from the bones:");
                if (headB != null)
                    sb.AppendLine(string.Format("    eyePos   (0, {0:F2}, {1:F2})   // the head bone, pushed to the face",
                                                headB.position.y, Mathf.Max(0.12f, b.size.z * 0.22f)));
                if (shoulder != null)
                    sb.AppendLine(string.Format("    armPos   ({0:F2}, {1:F2}, 0)",
                                                Mathf.Abs(shoulder.position.x), shoulder.position.y));
                if (shoulder != null && hand != null)
                    sb.AppendLine(string.Format("    handPos  (0, {0:F2}, 0)   // hand relative to the shoulder",
                                                hand.position.y - shoulder.position.y));
                if (chest != null)
                    sb.AppendLine(string.Format("    markHeight {0:F2}   // the CHEST bone — not a fraction of the bounds",
                                                chest.position.y));

                // ---- per-clip arm span --------------------------------------------------------
                // The measurement AUTHORING.md asks for: a whirl or a wide pose only reads if the arms
                // stay OUT through the clip, and clip names do not tell you that.
                if (lh == null || rh == null)
                    sb.AppendLine("  (no hand bones found by name — arm-span sampling skipped)");
                else
                {
                    sb.AppendLine("  clip                     span @25/50/75%      hand y     max span");
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                    {
                        var clip = o as AnimationClip;
                        if (clip == null || clip.name.StartsWith("__")) continue;

                        float[] at = new float[3];
                        float maxSpan = 0f, handY = 0f;
                        for (int i = 0; i < 3; i++)
                        {
                            clip.SampleAnimation(go, clip.length * (0.25f + 0.25f * i));
                            at[i] = Vector3.Distance(lh.position, rh.position);
                            handY += (lh.position.y + rh.position.y) * 0.5f / 3f;
                        }
                        for (int s = 0; s <= 10; s++)
                        {
                            clip.SampleAnimation(go, clip.length * s / 10f);
                            maxSpan = Mathf.Max(maxSpan, Vector3.Distance(lh.position, rh.position));
                        }
                        sb.AppendLine(string.Format("  {0,-22} {1,5:F2}/{2,4:F2}/{3,4:F2} m   {4,5:F2} m   {5,5:F2} m",
                            clip.name, at[0], at[1], at[2], handY, maxSpan));
                    }
                }
            }
            finally { Object.DestroyImmediate(go); }

            return sb.ToString();
        }

        static Transform Find(List<Transform> all, params string[] names)
        {
            foreach (var n in names)
                foreach (var t in all)
                    if (t.name == n) return t;
            // Fall back to a contains-match, since rigs disagree about separators.
            foreach (var n in names)
                foreach (var t in all)
                    if (t.name.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
            return null;
        }
    }
}
