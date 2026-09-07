using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// How a level piece becomes scene objects — the ONE place, used by both the editor-time
    /// <c>LevelDefinitionBuilder</c> (menu item 8, campaign levels, saved into a scene) and the in-game
    /// <see cref="LevelEditor"/> (custom levels, built at runtime from a <see cref="LevelDocument"/>).
    ///
    /// <para><b>Why one factory.</b> Before the in-game editor there were two copies of "a platform is
    /// a cube with four trim bars": <c>LevelGreyboxBuilder</c> and <c>LevelDefinitionBuilder</c>, kept in
    /// step by discipline. A third copy for the runtime would have drifted inside a week. So the
    /// construction moved here, into a runtime assembly with no <c>UnityEditor</c> reference, and the
    /// editor-time builder became a caller: it resolves assets through <c>AssetDatabase</c> and marks
    /// statics through <c>GameObjectUtility</c>, the runtime resolves through serialised references
    /// and marks nothing — both through <see cref="LevelPieceContext"/>. Same names, same positions,
    /// same colliders, same layers; <c>LevelEditorTests</c> builds a small definition both ways and
    /// compares.</para>
    ///
    /// <para>Everything built here is tagged with a <see cref="LevelPiece"/> naming the definition
    /// entry it came from, so the in-game editor can find, move and delete a piece by pointing at it and
    /// the exporter can read a scene back into data.</para>
    /// </summary>
    public static class LevelPieceFactory
    {
        public const float TrimThickness = 0.15f;

        /// <summary>Per-piece build counts, for the builders' log lines.</summary>
        public class Counts
        {
            public int boxes, trims, spawners, torches, pickups, balloons, waters, checkpoints, ramps;
            public override string ToString()
            {
                return boxes + " boxes, " + trims + " trims, " + ramps + " ramps, " + spawners + " spawners, " + torches + " torches, " +
                       pickups + " pickups, " + balloons + " balloons, " + waters + " waters, " + checkpoints + " checkpoints";
            }
        }

        // ------------------------------------------------------------------ whole definitions

        /// <summary>Build every piece of a definition under <paramref name="root"/>. Arenas, pedestals, sky and
        /// the kill zone are the campaign builder's business and are not part of a document.</summary>
        public static Counts BuildDocument(LevelDocument doc, Transform root, LevelPieceContext ctx)
        {
            var n = new Counts();
            for (int i = 0; i < doc.platforms.Count; i++) Platform(doc.platforms[i], root, ctx, n, i);
            for (int i = 0; i < doc.ramps.Count; i++) Ramp(doc.ramps[i], root, ctx, n, i);
            PlayerStart(doc.playerStart, doc.playerStartYaw, root);
            for (int i = 0; i < doc.spawns.Count; i++) Spawner(doc.spawns[i], root, ctx, n, i);
            for (int i = 0; i < doc.checkpoints.Count; i++) Checkpoint(doc.checkpoints[i], root, ctx, n, i);
            // Torches and pickups live under "Torches" / "Pickups" groups, exactly as the campaign builder
            // always made them: LevelDefinitionExporter walks the Level root's DIRECT children and reads
            // those two groups by name -- flattened, 44 torches would export as platforms and 11 pickups
            // would vanish. The groups exist only when there is something to put in them (the old rule).
            Transform torches = doc.torches.Count > 0 ? Group("Torches", root) : null;
            for (int i = 0; i < doc.torches.Count; i++) Torch(doc.torches[i], torches, ctx, n, i);
            Transform pickups = doc.pickups.Count > 0 ? Group("Pickups", root) : null;
            for (int i = 0; i < doc.pickups.Count; i++) Pickup(doc.pickups[i], pickups, ctx, n, i);
            for (int i = 0; i < doc.balloons.Count; i++) Balloon(doc.balloons[i], root, ctx, n, i);
            for (int i = 0; i < doc.waters.Count; i++) Water(doc.waters[i], root, ctx, n, i);
            return n;
        }

        // ------------------------------------------------------------------ pieces

        public static GameObject Platform(PlatformDef p, Transform root, LevelPieceContext ctx, Counts n, int index)
        {
            if (p == null) return null;
            var box = Box(p.name, p.center, p.size, ctx.Material(p.materialKey), root, ctx, p.isStatic, n);
            if (p.trim) Trim(box, ctx.Material(p.trimMaterialKey), ctx, n);
            Tag(box, LevelPieceKind.Platform, index);
            return box;
        }

        /// <summary>
        /// A ramp: ONE rotated cube — collider, renderer, no behaviour (hard rule 10). Its walkable face is
        /// the box's local +Y, pitched by <see cref="RampDef.AngleDegrees"/>, so the ground normal a motor
        /// reads off it is the real slope normal and nothing here has to tell it so.
        ///
        /// <para>Layer 0 (Default) like a platform, so the NavMesh bake — which collects RenderMeshes on
        /// layer 0 — walks it. No trim bars: the four edges of a rotated slab are not axis-aligned and the
        /// trim builder places world-axis bars.</para>
        /// </summary>
        public static GameObject Ramp(RampDef r, Transform root, LevelPieceContext ctx, Counts n, int index)
        {
            if (r == null) return null;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = r.name;
            go.transform.SetParent(root, false);
            go.transform.position = r.BoxCenter;
            go.transform.rotation = r.Rotation;
            go.transform.localScale = r.BoxScale;
            go.layer = 0;
            var m = ctx.Material(r.materialKey);
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            if (r.isStatic) ctx.MarkStatic(go);
            if (n != null) n.ramps++;
            Tag(go, LevelPieceKind.Ramp, index);
            return go;
        }

        /// <summary>
        /// The inverse of <see cref="Ramp"/>: read a built slab's transform back into the numbers that made
        /// it, so <c>Export Current Level To Definition</c> round-trips a ramp instead of mangling it into a
        /// platform. Assumes the ramp's parent is unrotated and unscaled, which every level root is.
        /// </summary>
        public static RampDef RampFrom(Transform t, string name, string materialKey, bool isStatic)
        {
            if (t == null) return null;
            Quaternion rot = t.rotation;
            Vector3 slope = rot * Vector3.forward;
            Vector3 up = rot * Vector3.up;
            Vector3 scale = t.localScale;
            Vector3 delta = slope * scale.z;                       // base -> top along the face
            float run = new Vector2(delta.x, delta.z).magnitude;
            return new RampDef
            {
                name = name,
                basePosition = t.position - slope * (scale.z * 0.5f) + up * (scale.y * 0.5f),
                width = scale.x,
                thickness = scale.y,
                run = run,
                rise = delta.y,
                yaw = run > 0.0001f ? Mathf.Repeat(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 360f) : 0f,
                materialKey = materialKey,
                isStatic = isStatic,
            };
        }

        public static GameObject PlayerStart(Vector3 position, float yaw, Transform root)
        {
            var go = Empty("StartSpawn", position, Quaternion.Euler(0f, yaw, 0f), root);
            Tag(go, LevelPieceKind.PlayerStart, 0);
            return go;
        }

        public static GameObject Spawner(SpawnDef s, Transform root, LevelPieceContext ctx, Counts n, int index)
        {
            if (s == null) return null;
            var prefab = ctx.Prefab(s.prefabKey);
            if (prefab == null) return null;   // the context already warned
            var go = Empty(s.name, s.position, Quaternion.Euler(0f, s.yaw, 0f), root);
            var sp = go.AddComponent<EnemySpawner>();
            sp.prefab = prefab;
            sp.isBoss = s.isBoss;
            if (n != null) n.spawners++;
            Tag(go, LevelPieceKind.Spawn, index);
            return go;
        }

        public static GameObject Checkpoint(CheckpointDef c, Transform root, LevelPieceContext ctx, Counts n, int index)
        {
            if (c == null) return null;
            var go = ctx.Instantiate(ctx.Prefab("Checkpoint"), c.name, root);
            if (go == null) return null;
            go.transform.position = c.position;
            go.transform.rotation = Quaternion.identity;
            // The prefab carries its own spawnPoint child; honour the authored offset.
            var cp = go.GetComponent<Checkpoint>();
            if (cp != null && cp.spawnPoint != null) cp.spawnPoint.position = c.position + c.spawnOffset;
            if (n != null) n.checkpoints++;
            Tag(go, LevelPieceKind.Checkpoint, index);
            return go;
        }

        /// <summary>Stone post + ember cube + flickering point light. basePos is the platform's top surface.</summary>
        public static GameObject Torch(TorchDef t, Transform root, LevelPieceContext ctx, Counts n, int index)
        {
            if (t == null) return null;
            var torch = new GameObject(t.name);
            torch.transform.SetParent(root, false);
            torch.transform.position = t.basePosition;

            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Post";
            UnityEngine.Object.DestroyImmediate(post.GetComponent<Collider>());
            post.transform.SetParent(torch.transform, false);
            post.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            post.transform.localScale = new Vector3(0.25f, 1.6f, 0.25f);
            var mStone = ctx.Material("Stone");
            if (mStone != null) post.GetComponent<Renderer>().sharedMaterial = mStone;

            var ember = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ember.name = "Ember";
            UnityEngine.Object.DestroyImmediate(ember.GetComponent<Collider>());
            ember.transform.SetParent(torch.transform, false);
            ember.transform.localPosition = new Vector3(0f, 1.75f, 0f);
            ember.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
            var emberRenderer = ember.GetComponent<Renderer>();
            emberRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mTorch = ctx.Material("Torch");
            if (mTorch != null) emberRenderer.sharedMaterial = mTorch;

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(torch.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            Color c;
            light.color = ColorUtility.TryParseHtmlString("#FF8A2A", out c) ? c : new Color(1f, 0.54f, 0.16f);
            light.range = 9f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            var flicker = lightGo.AddComponent<FlickerLight>();
            flicker.baseIntensity = 2.5f;
            flicker.amplitude = 0.9f;
            flicker.speed = 9f;

            if (n != null) n.torches++;
            Tag(torch, LevelPieceKind.Torch, index);
            return torch;
        }

        public static GameObject Pickup(PickupDef p, Transform root, LevelPieceContext ctx, Counts n, int index)
        {
            if (p == null) return null;
            var prefab = ctx.Prefab("ItemPickup");
            if (prefab == null) return null;
            var data = ctx.Item(p.itemKey);
            if (data == null) return null;
            var go = ctx.Instantiate(prefab, p.name, root);
            if (go == null) return null;
            go.transform.position = p.position;
            go.transform.rotation = Quaternion.identity;
            var pickup = go.GetComponent<ItemPickup>();
            if (pickup != null) pickup.item = data;
            else Debug.LogWarning("[LevelPieceFactory] " + p.name + " has no ItemPickup component.");
            if (n != null) n.pickups++;
            Tag(go, LevelPieceKind.Pickup, index);
            return go;
        }

        /// <summary>A balloon: the prefab with its numbers written per placement; the visual is scaled with the
        /// radius so a 0.9 m balloon LOOKS 0.9 m, the collider is the radius exactly.</summary>
        public static GameObject Balloon(BalloonDef b, Transform root, LevelPieceContext ctx, Counts n, int index)
        {
            if (b == null) return null;
            var prefab = ctx.Prefab("Balloon");
            if (prefab == null) return null;
            var go = ctx.Instantiate(prefab, b.name, root);
            if (go == null) return null;
            go.transform.position = b.position;
            go.transform.rotation = Quaternion.identity;
            var bal = go.GetComponent<Balloon>();
            if (bal != null)
            {
                bal.launchSpeed = b.launchSpeed;
                bal.respawnSeconds = b.respawnSeconds;
                bal.radius = b.radius;
                var col = go.GetComponent<SphereCollider>();
                if (col != null) { col.isTrigger = true; col.radius = b.radius; }
                if (bal.visual != null) bal.visual.localScale = Vector3.one * (b.radius / 1.1f);   // authored at 1.1
            }
            else Debug.LogWarning("[LevelPieceFactory] " + b.name + ": the Balloon prefab has no Balloon component.");
            if (n != null) n.balloons++;
            Tag(go, LevelPieceKind.Balloon, index);
            return go;
        }

        /// <summary>
        /// A sheet of water: the root carries the trigger and the <see cref="WaterVolume"/>; the visible
        /// sheet is a collider-less cube child; flow lines mark the direction. All on Interactable so the
        /// NavMesh bake and the motor's wall casts ignore it — the floor underneath is what you stand on.
        /// </summary>
        public static GameObject Water(WaterDef w, Transform root, LevelPieceContext ctx, Counts n, int index)
        {
            if (w == null) return null;
            var go = new GameObject(w.name);
            go.transform.SetParent(root, false);
            go.transform.position = w.center;
            go.transform.rotation = Quaternion.identity;

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            var water = go.AddComponent<WaterVolume>();
            water.size = w.size;
            water.flowDirection = w.flowDirection;
            water.flowSpeed = w.flowSpeed;
            water.boostHeight = 0.35f;
            water.ApplyTrigger();

            var material = ctx.Material("Water");
            var sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sheet.name = "Surface";
            UnityEngine.Object.DestroyImmediate(sheet.GetComponent<Collider>());
            sheet.transform.SetParent(go.transform, false);
            sheet.transform.localPosition = Vector3.zero;
            sheet.transform.localScale = w.size;
            var r = sheet.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            if (material != null) r.sharedMaterial = material;
            else Debug.LogWarning("[LevelPieceFactory] " + w.name + ": no water material (run VibeGame1/2. Create Materials); the sheet is magenta.");

            Vector3 flow = new Vector3(w.flowDirection.x, 0f, w.flowDirection.z);
            if (flow.sqrMagnitude > 0.0001f && w.flowSpeed > 0f)
            {
                flow.Normalize();
                Vector3 across = Vector3.Cross(Vector3.up, flow);
                float along = Mathf.Abs(Vector3.Dot(new Vector3(w.size.x, 0f, w.size.z), flow));
                float wide = Mathf.Abs(Vector3.Dot(new Vector3(w.size.x, 0f, w.size.z), across));
                int lanes = Mathf.Clamp(Mathf.FloorToInt(wide / 2.5f), 1, 12);
                for (int i = 0; i < lanes; i++)
                {
                    float t = (i + 0.5f) / lanes - 0.5f;
                    var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    line.name = "Flow_" + i;
                    UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
                    line.transform.SetParent(go.transform, false);
                    line.transform.localPosition = across * (t * wide) + Vector3.up * (w.size.y * 0.5f + 0.01f);
                    line.transform.localRotation = Quaternion.LookRotation(flow, Vector3.up);
                    line.transform.localScale = new Vector3(0.08f, 0.01f, along * 0.85f);
                    var lr = line.GetComponent<Renderer>();
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (material != null) lr.sharedMaterial = material;
                }
            }

            SetLayerRecursively(go, Layers.Interactable);
            if (n != null) n.waters++;
            Tag(go, LevelPieceKind.Water, index);
            return go;
        }

        // ------------------------------------------------------------------ primitives

        public static GameObject Box(string name, Vector3 center, Vector3 size, Material m, Transform parent,
                                     LevelPieceContext ctx, bool isStatic, Counts n)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.layer = 0;
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            if (isStatic) ctx.MarkStatic(go);
            if (n != null) n.boxes++;
            return go;
        }

        /// <summary>Four thin neon bars along the top edges of a box-shaped platform.</summary>
        public static void Trim(GameObject platform, Material m, LevelPieceContext ctx, Counts n)
        {
            const float t = TrimThickness;
            Vector3 c = platform.transform.position;
            Vector3 s = platform.transform.localScale;
            float top = c.y + s.y * 0.5f + t * 0.5f;
            float hx = s.x * 0.5f - t * 0.5f;
            float hz = s.z * 0.5f - t * 0.5f;
            Transform parent = platform.transform.parent;

            string[] suffix = { "_Trim_N", "_Trim_S", "_Trim_E", "_Trim_W" };
            Vector3[] pos = { new Vector3(c.x, top, c.z + hz), new Vector3(c.x, top, c.z - hz),
                              new Vector3(c.x + hx, top, c.z), new Vector3(c.x - hx, top, c.z) };
            Vector3[] size = { new Vector3(s.x, t, t), new Vector3(s.x, t, t), new Vector3(t, t, s.z), new Vector3(t, t, s.z) };

            for (int i = 0; i < 4; i++)
            {
                // Built as a sibling first (world-space scale), then parented, so the non-uniform parent
                // scale does not distort it.
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = platform.name + suffix[i];
                bar.transform.SetParent(parent, false);
                bar.transform.position = pos[i];
                bar.transform.localScale = size[i];
                bar.layer = 0;
                if (m != null) bar.GetComponent<Renderer>().sharedMaterial = m;
                UnityEngine.Object.DestroyImmediate(bar.GetComponent<Collider>());
                ctx.MarkStatic(bar);
                bar.transform.SetParent(platform.transform, true);
                if (n != null) n.trims++;
            }
        }

        /// <summary>A named, untagged child group at the parent's origin (the "Torches" / "Pickups" folders).</summary>
        public static Transform Group(string name, Transform parent)
        {
            var existing = parent != null ? parent.Find(name) : null;
            if (existing != null) return existing;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        public static GameObject Empty(string name, Vector3 pos, Quaternion rot, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = rot;
            return go;
        }

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        static void Tag(GameObject go, LevelPieceKind kind, int index)
        {
            var tag = go.GetComponent<LevelPiece>();
            if (tag == null) tag = go.AddComponent<LevelPiece>();
            tag.kind = kind;
            tag.index = index;
        }
    }

    /// <summary>
    /// What a <see cref="LevelPieceFactory"/> call needs from its caller: assets by KEY, and the two
    /// operations that differ between the editor-time and runtime worlds (prefab instantiation keeps
    /// its prefab link only in the editor; static flags exist only in the editor).
    /// </summary>
    public class LevelPieceContext
    {
        /// <summary>'Platform' → M_Platform. Keys omit the M_ prefix.</summary>
        public Func<string, Material> materials;
        /// <summary>'Enemy_Grunt' → Assets/Prefabs/Enemy_Grunt.prefab.</summary>
        public Func<string, GameObject> prefabs;
        /// <summary>'Grapple' → Assets/Data/Items/Grapple.asset.</summary>
        public Func<string, ItemData> items;
        /// <summary>Instantiate a prefab (PrefabUtility in the editor, Object.Instantiate at runtime).</summary>
        public Func<GameObject, GameObject> instantiate;
        /// <summary>Mark a built object static for batching/GI. Null at runtime.</summary>
        public Action<GameObject> markStatic;

        public Material Material(string key)
        {
            if (string.IsNullOrEmpty(key) || materials == null) return null;
            var m = materials(key);
            if (m == null) Debug.LogWarning("[LevelPieceFactory] Missing material M_" + key + " (run '2. Create Materials' first).");
            return m;
        }

        public GameObject Prefab(string key)
        {
            if (string.IsNullOrEmpty(key) || prefabs == null) return null;
            var p = prefabs(key);
            if (p == null) Debug.LogWarning("[LevelPieceFactory] Missing prefab " + key + " (run '4. Build Prefabs' first).");
            return p;
        }

        public ItemData Item(string key)
        {
            if (string.IsNullOrEmpty(key) || items == null) return null;
            var d = items(key);
            if (d == null) Debug.LogWarning("[LevelPieceFactory] Missing item data " + key + " (run '3. Create Data' first).");
            return d;
        }

        public GameObject Instantiate(GameObject prefab, string name, Transform parent)
        {
            if (prefab == null) return null;
            var go = instantiate != null ? instantiate(prefab) : UnityEngine.Object.Instantiate(prefab);
            if (go == null) return null;
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        public void MarkStatic(GameObject go) { if (markStatic != null) markStatic(go); }

        /// <summary>The runtime context: serialised lookups on the <see cref="LevelEditor"/>, plain Instantiate, no statics.</summary>
        public static LevelPieceContext Runtime(Func<string, Material> mats, Func<string, GameObject> prefs, Func<string, ItemData> its)
        {
            return new LevelPieceContext { materials = mats, prefabs = prefs, items = its, instantiate = UnityEngine.Object.Instantiate, markStatic = null };
        }
    }
}
