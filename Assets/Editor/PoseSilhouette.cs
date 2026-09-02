using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Measures the SHAPE a wind-up pose actually makes on screen, by rasterising the enemy's real
    /// geometry through the player's real camera. No GPU, no play mode, no Animator — so it runs
    /// inside an EditMode test as happily as inside a headless capture.
    ///
    /// <para><b>Why this exists at all, and why it does not read the authored Eulers.</b> This project
    /// has already paid for the other approach: eleven wind-up poses shipped with confident comments
    /// and ten of them made a different shape on screen than the comment claimed. The standing rule out
    /// of that is <i>an authored angle is not an on-screen angle</i>. So nothing here ever looks at
    /// <see cref="WindupPose.armWindup"/> as a number — it applies the pose to a real instance and then
    /// measures the pixels the body covers.</para>
    ///
    /// <para><b>And on THIS body the arm channel is not even visible.</b> An imported forge model gets
    /// EMPTY arm pivots (<c>MiniBossFactory.BuildModelBody</c>: the auto-rig's bones sit inside the
    /// silhouette, so driving them at a ±136° telegraph tears the mesh) and its <c>EnemyVisuals.weapon</c>
    /// is a 3 cm spark marker, not a blade. The blade-angle metric <c>FeatureTests.MeasureWindup</c> uses
    /// on the primitives therefore measures a 3 cm cube here and means nothing. The Revenant's wind-up
    /// silhouette is carried ENTIRELY by <c>bodyOffset</c> / <c>bodyEuler</c> on <c>LungeRoot</c>, which
    /// moves the whole skinned mesh — so what gets measured here is the whole body's outline.</para>
    /// </summary>
    public static class PoseSilhouette
    {
        /// <summary>Raster resolution of the measured mask, per side. Square frame, so aspect = 1.</summary>
        public const int Grid = 128;

        /// <summary>The player's real eye: <c>CameraPivot</c> is at y 1.6 on the player prefab and the
        /// camera's vertical FOV is 95°. Both read out of <c>PrefabFactory</c>, not invented here.</summary>
        public const float EyeHeight = 1.6f;
        public const float Fov = 95f;

        /// <summary>One measured silhouette. Every length is in BODY-HEIGHTS of the resting body, so the
        /// numbers survive a change of resolution, of distance, or of <c>EnemyData.scale</c>.</summary>
        public struct Shape
        {
            public bool valid;
            public string name;
            public float w, h;        // bounding box, body-heights
            public float cx, cy;      // centroid, body-heights, relative to the REST silhouette's centroid
            public float top;         // highest point, body-heights relative to the rest silhouette's top
            public float axis;        // principal axis tilt off vertical, degrees. + = top leans to the player's RIGHT
            public float area;        // covered area as a fraction of the rest silhouette's area
            public bool[] mask;       // the raster itself, for IoU
            public int filled;
        }

        // ---------------------------------------------------------------- pose application

        /// <summary>
        /// Drive an instance to the exact frame the player's parry decision is made on: the wind-up
        /// PEAK that <c>EnemyVisuals.CueFlash</c> freezes, which is <c>armWindup * 1.12</c> at the
        /// shoulder over a fully-applied body channel (<c>WindupCo</c> reaches k = 1 there).
        /// Mirrors <c>EnemyVisuals</c> exactly; if that maths changes, this must change with it.
        /// </summary>
        public static void ApplyPeak(GameObject inst, EnemyAttackData atk)
        {
            if (inst == null || atk == null) return;
            var vis = inst.GetComponentInChildren<EnemyVisuals>(true);
            if (vis == null) return;
            WindupPose p = Resolve(atk);
            if (vis.lungeRoot != null)
            {
                vis.lungeRoot.localPosition += p.bodyOffset;
                vis.lungeRoot.localRotation *= Quaternion.Euler(p.bodyEuler);
            }
            Vector3 w = p.armWindup * 1.12f;
            if (vis.armPivot != null) vis.armPivot.localRotation *= Quaternion.Euler(w);
            if (vis.weaponPivot != null)
                vis.weaponPivot.localRotation *= Quaternion.Euler(w * Mathf.Clamp01(p.weaponLag));
        }

        /// <summary>
        /// The pose an attack ACTUALLY plays, authored or not — a mirror of the private
        /// <c>EnemyVisuals.PoseFor</c> plus its cone-derived fallback vocabulary. Duplicated rather
        /// than exposed because <c>EnemyVisuals</c> is a runtime script and this is a measuring tool;
        /// if the fallback there ever changes, these constants must be updated with it, and the
        /// "unauthored measures as the fallback" check in the tests is what will notice.
        /// </summary>
        public static WindupPose Resolve(EnemyAttackData atk)
        {
            if (atk != null && atk.windupPose != null && atk.windupPose.authored) return atk.windupPose;
            var p = new WindupPose();
            p.authored = false;
            p.weaponLag = 0.45f;
            p.bodyOffset = new Vector3(0f, 0.15f, -0.25f);      // EnemyVisuals.GenericBodyOffset
            p.bodyEuler = new Vector3(-12f, 0f, 0f);            // EnemyVisuals.GenericBodyEuler
            float cone = atk != null ? atk.coneDeg : 70f;
            if (cone >= 90f) { p.armWindup = new Vector3(-24f, -106f, -44f); p.armStrike = new Vector3(-8f, 88f, 32f); }
            else if (cone <= 45f) { p.armWindup = new Vector3(-58f, -20f, 0f); p.armStrike = new Vector3(20f, 8f, 0f); }
            else { p.armWindup = new Vector3(-136f, 0f, -26f); p.armStrike = new Vector3(64f, 0f, 16f); }
            return p;
        }

        // ---------------------------------------------------------------- measurement

        /// <summary>
        /// Rasterise every triangle of every renderer under <paramref name="inst"/> through a camera
        /// parked at the player's eye, <paramref name="distance"/> metres away on −Z, aimed at the
        /// body's chest. Returns the raw mask; call <see cref="Normalise"/> to turn a set of them into
        /// body-height numbers.
        ///
        /// <para>Skinned meshes are baked, so the measurement is of the geometry as it is actually
        /// deformed rather than of a bind-pose vertex list. The bake is identical for every pose (only
        /// an ANCESTOR of the skin moves), which is what makes two poses comparable.</para>
        /// </summary>
        public static bool[] Raster(GameObject inst, float distance, float chestHeight, out int filled)
        {
            filled = 0;
            var mask = new bool[Grid * Grid];
            if (inst == null) return mask;

            // MEASURE ONLY WHAT THE POSE MOVES. The world-space posture bar hangs off `Visual`, not off
            // LungeRoot, so it sits in exactly the same place in every frame; leaving it in the mask adds
            // a constant blob to every silhouette and quietly inflates the IoU between two poses that
            // share nothing else. The alert marker is excluded for a different reason: the unblockable
            // tell is a SEPARATE read that the pose is meant to add to, not part of the pose's shape.
            var vis0 = inst.GetComponentInChildren<EnemyVisuals>(true);
            GameObject subject = vis0 != null && vis0.lungeRoot != null ? vis0.lungeRoot.gameObject : inst;

            // ON THE ENEMY'S +Z SIDE. The forge models face +Z and an enemy in a fight faces the
            // player, which is the same convention WindupPose documents for bodyOffset ("+Z is toward
            // the player"). Filming from -Z photographs the enemy's BACK and silently inverts every
            // forward/back reading: a thrust that drives at the camera measures as one that retreats.
            Vector3 eye = new Vector3(0f, EyeHeight, distance);
            Vector3 fwd = (new Vector3(0f, chestHeight, 0f) - eye).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 up = Vector3.Cross(fwd, right);
            float tan = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var baked = new List<Mesh>();
            try
            {
                foreach (var r in subject.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.gameObject.activeInHierarchy || !r.enabled) continue;
                    Mesh m = null;
                    Matrix4x4 l2w;
                    var smr = r as SkinnedMeshRenderer;
                    if (smr != null)
                    {
                        if (smr.sharedMesh == null) continue;
                        m = new Mesh();
                        baked.Add(m);
                        smr.BakeMesh(m);
                        l2w = smr.transform.localToWorldMatrix;
                    }
                    else
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        m = mf.sharedMesh;
                        l2w = r.transform.localToWorldMatrix;
                    }

                    verts.Clear(); tris.Clear();
                    m.GetVertices(verts);
                    for (int sub = 0; sub < m.subMeshCount; sub++)
                    {
                        var t = m.GetTriangles(sub);
                        for (int i = 0; i < t.Length; i++) tris.Add(t[i]);
                    }

                    var screen = new Vector3[verts.Count];
                    for (int i = 0; i < verts.Count; i++)
                    {
                        Vector3 wp = l2w.MultiplyPoint3x4(verts[i]);
                        Vector3 d = wp - eye;
                        float z = Vector3.Dot(d, fwd);
                        if (z <= 0.05f) { screen[i] = new Vector3(0f, 0f, -1f); continue; }
                        float sx = Vector3.Dot(d, right) / (z * tan);
                        float sy = Vector3.Dot(d, up) / (z * tan);
                        // NDC -1..1 -> grid cells
                        screen[i] = new Vector3((sx * 0.5f + 0.5f) * Grid, (sy * 0.5f + 0.5f) * Grid, z);
                    }

                    for (int i = 0; i + 2 < tris.Count; i += 3)
                    {
                        Vector3 a = screen[tris[i]], b = screen[tris[i + 1]], c = screen[tris[i + 2]];
                        if (a.z < 0f || b.z < 0f || c.z < 0f) continue;
                        FillTriangle(mask, a, b, c);
                    }
                }
            }
            finally
            {
                for (int i = 0; i < baked.Count; i++) Object.DestroyImmediate(baked[i]);
            }

            for (int i = 0; i < mask.Length; i++) if (mask[i]) filled++;
            return mask;
        }

        static void FillTriangle(bool[] mask, Vector3 a, Vector3 b, Vector3 c)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int maxX = Mathf.Min(Grid - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int maxY = Mathf.Min(Grid - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            if (minX > maxX || minY > maxY) return;

            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(d) < 1e-7f)
            {
                // Degenerate on screen (an edge-on face). Mark its bounding cells rather than dropping
                // it: at 128 cells a sliver is still part of the outline.
                for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++) mask[y * Grid + x] = true;
                return;
            }
            for (int y = minY; y <= maxY; y++)
            {
                float py = y + 0.5f;
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f;
                    float w0 = ((b.y - c.y) * (px - c.x) + (c.x - b.x) * (py - c.y)) / d;
                    float w1 = ((c.y - a.y) * (px - c.x) + (a.x - c.x) * (py - c.y)) / d;
                    float w2 = 1f - w0 - w1;
                    if (w0 >= 0f && w1 >= 0f && w2 >= 0f) mask[y * Grid + x] = true;
                }
            }
        }

        /// <summary>Bounding box, centroid, principal axis and area of one mask, in GRID cells.</summary>
        static void Raw(bool[] mask, out float minX, out float maxX, out float minY, out float maxY,
                        out float cx, out float cy, out float axis, out int filled)
        {
            minX = Grid; maxX = -1; minY = Grid; maxY = -1;
            double sx = 0, sy = 0; filled = 0;
            for (int y = 0; y < Grid; y++)
                for (int x = 0; x < Grid; x++)
                {
                    if (!mask[y * Grid + x]) continue;
                    filled++;
                    sx += x; sy += y;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            if (filled == 0) { cx = cy = axis = 0f; return; }
            cx = (float)(sx / filled); cy = (float)(sy / filled);

            double xx = 0, yy = 0, xy = 0;
            for (int y = 0; y < Grid; y++)
                for (int x = 0; x < Grid; x++)
                {
                    if (!mask[y * Grid + x]) continue;
                    double dx = x - cx, dy = y - cy;
                    xx += dx * dx; yy += dy * dy; xy += dx * dy;
                }
            // Orientation of the MAJOR axis, reported as a tilt off VERTICAL so 0 is an upright body.
            double ang = 0.5 * System.Math.Atan2(2.0 * xy, xx - yy);      // radians from the +x axis
            float deg = (float)(ang * Mathf.Rad2Deg);
            float tilt = 90f - deg;
            while (tilt > 90f) tilt -= 180f;
            while (tilt < -90f) tilt += 180f;
            axis = tilt;
        }

        /// <summary>
        /// Turn raw masks into comparable numbers. Everything is expressed against the FIRST entry,
        /// which must be the resting body — that is what "body-heights" means here, and it is why a
        /// rest frame is always filmed alongside the poses.
        /// </summary>
        public static Shape[] Normalise(string[] names, bool[][] masks)
        {
            var res = new Shape[names.Length];
            float rMinX, rMaxX, rMinY, rMaxY, rcx, rcy, rax; int rFill;
            Raw(masks[0], out rMinX, out rMaxX, out rMinY, out rMaxY, out rcx, out rcy, out rax, out rFill);
            float bodyH = Mathf.Max(1f, rMaxY - rMinY);

            for (int i = 0; i < names.Length; i++)
            {
                float mnX, mxX, mnY, mxY, cx, cy, ax; int fill;
                Raw(masks[i], out mnX, out mxX, out mnY, out mxY, out cx, out cy, out ax, out fill);
                var s = new Shape();
                s.name = names[i];
                s.mask = masks[i];
                s.filled = fill;
                s.valid = fill > 0;
                if (!s.valid) { res[i] = s; continue; }
                s.w = (mxX - mnX) / bodyH;
                s.h = (mxY - mnY) / bodyH;
                s.cx = (cx - rcx) / bodyH;
                s.cy = (cy - rcy) / bodyH;
                s.top = (mxY - rMaxY) / bodyH;
                s.axis = ax;
                s.area = rFill > 0 ? (float)fill / rFill : 0f;
                res[i] = s;
            }
            return res;
        }

        /// <summary>Intersection over union of two masks: the bluntest possible "do these look the
        /// same on screen" number, and the one that needs no interpretation.</summary>
        public static float IoU(bool[] a, bool[] b)
        {
            int inter = 0, uni = 0;
            for (int i = 0; i < a.Length; i++)
            {
                bool x = a[i], y = b[i];
                if (x && y) inter++;
                if (x || y) uni++;
            }
            return uni == 0 ? 1f : (float)inter / uni;
        }

        // ---------------------------------------------------------------- staging

        /// <summary>
        /// Instantiate a mini-boss prefab as a PHOTO PROP: scaled by its own <c>EnemyData</c>, body
        /// colour applied (<c>EnemyVisuals.Setup</c> — <c>Awake</c> never runs in edit mode, and this
        /// project has already been caught by that), brain/agent/colliders off.
        /// </summary>
        public static GameObject Stage(GameObject prefab, EnemyData data)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;
            if (data != null) inst.transform.localScale = Vector3.one * Mathf.Max(0.01f, data.scale);
            var ctrl = inst.GetComponent<EnemyController>();
            if (ctrl != null) ctrl.enabled = false;
            var agent = inst.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            foreach (var col in inst.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            var vis = inst.GetComponentInChildren<EnemyVisuals>(true);
            if (vis != null && data != null)
            {
                // Setup() is the real colour path and is TRIED first, so this keeps working if it ever
                // becomes edit-mode safe. It is not today: EnemyVisuals caches its EmissiveFlash and its
                // MaterialPropertyBlocks in Awake, Awake does not run in edit mode, and Setup()
                // dereferences both. (EnemyPortrait's note that "Setup applies the body colour" is
                // optimistic — it throws.) The catch is not swallowing an error, it is the documented
                // edit-mode shape of this component; the colour is then written by hand below through
                // the exact same channels Setup would have used.
                try { vis.Setup(data); }
                catch (System.Exception) { }
                Paint(inst, data);
            }
            return inst;
        }

        /// <summary>
        /// The body colour and the ember floor, written straight into a property block. Only for
        /// PHOTOGRAPHS — the measured mask is geometry and does not care what colour anything is.
        /// </summary>
        static void Paint(GameObject inst, EnemyData data)
        {
            var aura = inst.GetComponentInChildren<EmberAura>(true);
            Color emission = aura != null ? aura.emberHot * Mathf.Max(0f, aura.glowAtRest)
                                          : data.emission * 0.22f;
            var mpb = new MaterialPropertyBlock();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterial == null) continue;
                if (r.sharedMaterial.name.Contains("Eye") || r.sharedMaterial.name.Contains("Alert") ||
                    r.sharedMaterial.name.Contains("Deathblow")) continue;
                mpb.Clear();
                mpb.SetColor(Shader.PropertyToID("_BaseColor"), data.bodyColor);
                mpb.SetColor(Shader.PropertyToID("_EmissionColor"), emission);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
