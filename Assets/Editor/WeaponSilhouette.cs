using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Measures what a WEAPON actually covers on screen, from the player's own eye, by rasterising the
    /// real viewmodel prefab at the real <c>viewmodelScale</c> under a real authored <see cref="Pose"/>.
    /// The main-hand twin of <see cref="PoseSilhouette"/>, and it exists for the same reason: this
    /// project's most expensive repeated lesson is that an authored number is not an on-screen shape.
    ///
    /// <para><b>Why this is the right measurement for weapon variety.</b> The dagger pass shrank every
    /// weapon to one silhouette because a long blade at 95° FOV becomes a pole across the frame. The way
    /// back to variety is NOT "make them big again" — it is to give each archetype a different reach and
    /// mass while holding the three properties the dagger set actually earned:
    ///   1. the weapon never crosses the CROSSHAIR (you can always see what you are about to parry);
    ///   2. the weapon never covers more than a small fraction of the FRAME;
    ///   3. the TIP stays inside the frame, so the contact point of the swing is legible.
    /// Those three are measurable, and <c>WeaponSilhouetteTests</c> asserts them on every shipped
    /// weapon in every shipped pose. Length is then free to vary, because it is no longer what is
    /// keeping the frame readable.</para>
    ///
    /// <para>No GPU, no play mode: triangles are projected by hand exactly as <see cref="PoseSilhouette"/>
    /// does, so this runs inside an EditMode test and inside a headless batch alike.</para>
    /// </summary>
    public static class WeaponSilhouette
    {
        /// <summary>16:9, because the framing question is about the real screen, not a square crop.</summary>
        public const int GridX = 160;
        public const int GridY = 90;
        /// <summary>The player camera's VERTICAL fov, straight off <c>PrefabFactory</c>.</summary>
        public const float Fov = 95f;

        /// <summary>Radius of the "can I still see the fight" disc, in fractions of frame HEIGHT.
        /// Generous on purpose: the enemy the player is reading is a body, not a dot.</summary>
        public const float CrosshairRadius = 0.13f;

        public struct Shot
        {
            public bool valid;
            public string name;
            /// <summary>Covered cells as a fraction of the whole frame.</summary>
            public float coverage;
            /// <summary>Covered cells inside the crosshair disc, as a fraction of that disc.</summary>
            public float crosshair;
            /// <summary>Bounding box in NDC (-1..1 in X and Y; X is scaled by aspect so 1 = frame edge).</summary>
            public float minX, maxX, minY, maxY;
            /// <summary>Centroid, same NDC units.</summary>
            public float cx, cy;
            /// <summary>Where the Tip* part landed in NDC, and whether it was inside the frame.</summary>
            public float tipX, tipY;
            public bool tipInFrame;
            /// <summary>Metres of weapon standing above the fist: (highest point − grip) × viewmodelScale.
            /// This is REACH AS THE HAND FEELS IT, and it is the number the dagger pass flattened.</summary>
            public float extent;
            public bool[] mask;
            public int filled;

            public override string ToString()
            {
                return string.Format(
                    "{0,-10} extent {1:0.00}m  cover {2:0.0%}  crosshair {3:0.0%}  " +
                    "box x[{4:+0.00;-0.00} {5:+0.00;-0.00}] y[{6:+0.00;-0.00} {7:+0.00;-0.00}]  " +
                    "tip ({8:+0.00;-0.00},{9:+0.00;-0.00}){10}",
                    name, extent, coverage, crosshair, minX, maxX, minY, maxY, tipX, tipY,
                    tipInFrame ? "" : " OFF-FRAME");
            }
        }

        // ---------------------------------------------------------------- staging

        /// <summary>
        /// Instantiate the real Player prefab as a photo prop and hand back its <see cref="WeaponViewmodel"/>.
        /// <c>Awake</c> never runs in edit mode, so <c>model</c>/<c>hand</c>/<c>grip</c> are used as they
        /// were SERIALISED by <c>PrefabFactory</c> — which is the point: this measures the shipped rig.
        /// Physics, brains and the character controller are stripped so nothing tries to simulate.
        /// </summary>
        public static GameObject StagePlayer(out WeaponViewmodel vm, out Camera cam)
        {
            vm = null; cam = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (prefab == null) return null;
            var inst = Object.Instantiate(prefab);
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;
            foreach (var c in inst.GetComponentsInChildren<CharacterController>(true)) c.enabled = false;
            vm = inst.GetComponentInChildren<WeaponViewmodel>(true);
            cam = inst.GetComponentInChildren<Camera>(true);
            return inst;
        }

        public static WeaponData Weapon(string n)
        {
            return AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Data/Weapons/" + n + ".asset");
        }

        /// <summary>The four shipped weapons, in loadout order.</summary>
        public static readonly string[] All = { "Sword", "Hammer", "Dagger", "DevBlade" };

        /// <summary>The poses every weapon must survive. Idle and guard are HELD — the player lives in
        /// them for seconds — so they carry the strictest framing rules.</summary>
        public static Pose PoseOf(WeaponData w, string pose)
        {
            switch (pose)
            {
                case "idle": return w.idle;
                case "windup": return w.windup;
                case "strike": return w.swingEnd;
                case "guard": return w.guard;
                case "parry": return w.parry;
            }
            return w.idle;
        }

        // ---------------------------------------------------------------- measurement

        /// <summary>
        /// Equip <paramref name="w"/> on a staged rig, hold <paramref name="pose"/>, and measure the
        /// WEAPON ONLY. The hand and arm are excluded deliberately: they are the same on every weapon,
        /// so including them would add a constant blob that flatters every comparison — the same trap
        /// <see cref="PoseSilhouette"/> documents for the posture bar.
        /// </summary>
        public static Shot Measure(WeaponViewmodel vm, Camera cam, WeaponData w, string pose)
        {
            var s = new Shot();
            s.name = (w != null ? w.name : "?") + "." + pose;
            if (vm == null || cam == null || w == null) return s;

            vm.SetWeapon(w);
            Transform m = vm.CurrentModel;
            if (m == null) return s;
            Pose p = PoseOf(w, pose);
            vm.model.localPosition = p.pos;
            vm.model.localRotation = Quaternion.Euler(p.euler);
            s.extent = ExtentAboveFist(w);

            Vector3 eye = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            Vector3 right = cam.transform.right;
            Vector3 up = cam.transform.up;
            float tan = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float aspect = GridX / (float)GridY;

            var mask = new bool[GridX * GridY];
            var verts = new List<Vector3>();
            float minX = 9f, maxX = -9f, minY = 9f, maxY = -9f;
            double sx = 0, sy = 0; int nPts = 0;

            foreach (var r in m.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.gameObject.activeInHierarchy) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                Matrix4x4 l2w = r.transform.localToWorldMatrix;
                verts.Clear();
                mesh.GetVertices(verts);
                var screen = new Vector3[verts.Count];
                for (int i = 0; i < verts.Count; i++)
                {
                    Vector3 d = l2w.MultiplyPoint3x4(verts[i]) - eye;
                    float z = Vector3.Dot(d, fwd);
                    if (z <= 0.01f) { screen[i] = new Vector3(0f, 0f, -1f); continue; }
                    float nx = Vector3.Dot(d, right) / (z * tan * aspect);
                    float ny = Vector3.Dot(d, up) / (z * tan);
                    screen[i] = new Vector3((nx * 0.5f + 0.5f) * GridX, (ny * 0.5f + 0.5f) * GridY, z);
                    if (nx < minX) minX = nx; if (nx > maxX) maxX = nx;
                    if (ny < minY) minY = ny; if (ny > maxY) maxY = ny;
                    sx += nx; sy += ny; nPts++;
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var t = mesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = screen[t[i]], b = screen[t[i + 1]], c = screen[t[i + 2]];
                        if (a.z < 0f || b.z < 0f || c.z < 0f) continue;
                        Fill(mask, a, b, c);
                    }
                }
            }

            if (nPts == 0) return s;
            s.valid = true;
            s.mask = mask;
            s.minX = minX; s.maxX = maxX; s.minY = minY; s.maxY = maxY;
            s.cx = (float)(sx / nPts); s.cy = (float)(sy / nPts);

            int filled = 0, discFilled = 0, discCells = 0;
            float rx = CrosshairRadius / aspect;   // the disc is round on SCREEN, so it is an ellipse in X-NDC
            for (int y = 0; y < GridY; y++)
                for (int x = 0; x < GridX; x++)
                {
                    float nx = ((x + 0.5f) / GridX * 2f - 1f);
                    float ny = ((y + 0.5f) / GridY * 2f - 1f);
                    bool inDisc = (nx / rx) * (nx / rx) + (ny / CrosshairRadius) * (ny / CrosshairRadius) <= 1f;
                    if (inDisc) discCells++;
                    if (!mask[y * GridX + x]) continue;
                    filled++;
                    if (inDisc) discFilled++;
                }
            s.filled = filled;
            s.coverage = filled / (float)(GridX * GridY);
            s.crosshair = discCells == 0 ? 0f : discFilled / (float)discCells;

            // The tip, projected on its own. A blade whose point has left the frame has no readable
            // contact position, however well the rest of it is framed.
            Transform tip = FindNamed(m, "Tip");
            if (tip != null)
            {
                Vector3 d = tip.position - eye;
                float z = Vector3.Dot(d, fwd);
                if (z > 0.01f)
                {
                    s.tipX = Vector3.Dot(d, right) / (z * tan * aspect);
                    s.tipY = Vector3.Dot(d, up) / (z * tan);
                    s.tipInFrame = Mathf.Abs(s.tipX) <= 1f && Mathf.Abs(s.tipY) <= 1f;
                }
            }
            return s;
        }

        static Transform FindNamed(Transform root, string prefix)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && t.name.StartsWith(prefix)) return t;
            return null;
        }

        /// <summary>
        /// Metres of weapon above the gripping fist, read off the PREFAB and the SHIPPED
        /// <c>viewmodelScale</c>. This is the single number the dagger pass collapsed (every weapon
        /// landed in a 0.27-0.32 m band), so it is the single number that has to spread again.
        /// </summary>
        public static float ExtentAboveFist(WeaponData w)
        {
            if (w == null || w.viewmodelPrefab == null) return 0f;
            Transform root = w.viewmodelPrefab.transform;
            Transform grip = WeaponViewmodel.FindGrip(root);
            float gripY = grip != null ? root.InverseTransformPoint(grip.position).y : 0f;
            float top = float.MinValue;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Vector3 c = root.InverseTransformPoint(r.transform.position);
                Vector3 e = r.transform.localScale * 0.5f;
                float y = c.y + e.y;
                if (y > top) top = y;
            }
            if (top == float.MinValue) return 0f;
            return (top - gripY) * w.viewmodelScale;
        }

        static void Fill(bool[] mask, Vector3 a, Vector3 b, Vector3 c)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int maxX = Mathf.Min(GridX - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int maxY = Mathf.Min(GridY - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            if (minX > maxX || minY > maxY) return;
            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(d) < 1e-7f)
            {
                for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++) mask[y * GridX + x] = true;
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
                    if (w0 >= 0f && w1 >= 0f && w2 >= 0f) mask[y * GridX + x] = true;
                }
            }
        }

        /// <summary>Intersection over union — "do these two look like the same thing on screen".</summary>
        public static float IoU(bool[] a, bool[] b)
        {
            if (a == null || b == null) return 1f;
            int inter = 0, uni = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] && b[i]) inter++;
                if (a[i] || b[i]) uni++;
            }
            return uni == 0 ? 1f : inter / (float)uni;
        }
    }
}
