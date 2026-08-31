using System.IO;
using VibeGame1;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Builds every gameplay prefab from primitives + components so nothing has to be wired by hand.
    /// Idempotent: re-running overwrites the prefab assets in place.
    /// </summary>
    public static class PrefabFactory
    {
        const string PrefabDir = "Assets/Prefabs";
        const string WeaponDir = "Assets/Prefabs/Weapons";
        const string WandDir = "Assets/Prefabs/Wands";
        const string ItemDir = "Assets/Prefabs/Items";
        const string MaterialDir = "Assets/Materials";

        [MenuItem("VibeGame1/4. Build Prefabs")]
        public static void BuildAll()
        {
            EnsureFolder("Assets", "Prefabs");
            EnsureFolder(PrefabDir, "Weapons");
            EnsureFolder(PrefabDir, "Wands");
            EnsureFolder(PrefabDir, "Items");

            // A. weapons/wands/items first so the *Data.viewmodelPrefab fields can be assigned.
            // Requires DataFactory (step 3) to have run: these write back onto existing assets.
            BuildWeaponViewmodels();
            BuildWandViewmodels();
            BuildItemViewmodels();

            // E. bloodstain before Managers so LevelManager can reference it
            GameObject bloodstain = BuildBloodstain();
            BuildCheckpoint();
            BuildItemPickup();
            BuildPlayer();
            BuildManagers(bloodstain);
            BuildEnemy("Enemy_Grunt", "Assets/Data/Enemies/Grunt.asset", false);
            BuildEnemy("Enemy_Heavy", "Assets/Data/Enemies/Heavy.asset", false);
            BuildEnemy("Boss", "Assets/Data/Enemies/Boss.asset", true);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PrefabFactory] All prefabs built.");
        }

        // ------------------------------------------------------------------ helpers

        static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }

        static Material Mat(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{name}.mat");
            if (m == null) Debug.LogWarning($"[PrefabFactory] Material not found: {MaterialDir}/{name}.mat (run DataFactory / material setup first)");
            return m;
        }

        static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) Debug.LogWarning($"[PrefabFactory] Asset not found: {path}");
            return a;
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material m, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            if (!keepCollider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static GameObject Empty(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go;
        }

        static GameObject Save(GameObject temp, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path, out bool ok);
            if (!ok) Debug.LogError($"[PrefabFactory] Failed to save prefab {path}");
            else Debug.Log($"[PrefabFactory] Saved {path}");
            Object.DestroyImmediate(temp);
            return prefab;
        }

        // ------------------------------------------------------------------ A. weapons

        /// <summary>
        /// Naming is load-bearing: <see cref="EnergyGlow"/> collects children by prefix.
        ///   Seg*   — stacked blade/shaft slices the travelling energy band flows along.
        ///            A blade must be SEGMENTS, not one cube, or there is nothing for the flow to move over.
        ///   Tip*   — the hot core, held brighter than the segments.
        ///   Float* — parts that orbit/bob under their own clocks. Authored offset from the axis sets
        ///            the orbit radius, so the silhouette you build here is the silhouette you get.
        /// Everything emissive uses the neutral M_Energy; EnergyGlow multiplies the real hue in at
        /// runtime, so a coloured base material would double-tint.
        /// </summary>
        static void StackSegments(Transform parent, Material mat, int count, float baseY, float height,
                                  float width, float depth, float taper = 1f)
        {
            float segH = height / count;
            for (int i = 0; i < count; i++)
            {
                // Taper narrows each slice toward the point; 1 = parallel-sided.
                float k = count == 1 ? 0f : i / (float)(count - 1);
                float shrink = Mathf.Lerp(1f, taper, k);
                Prim(PrimitiveType.Cube, $"Seg{i}", parent,
                     new Vector3(0f, baseY + segH * (i + 0.5f), 0f),
                     new Vector3(width * shrink, segH * 0.94f, depth * shrink), mat);
            }
        }

        /// <summary>Adds an EnergyGlow tuned for this weapon and strips viewmodel shadows.</summary>
        static void Energise(GameObject root, Color tint, float pulseSpeed, float flowSpeed,
                             float flowWidth, float tipBoost, float orbitSpeed)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            var glow = root.AddComponent<EnergyGlow>();
            glow.tint = tint;
            glow.pulseSpeed = pulseSpeed;
            glow.flowSpeed = flowSpeed;
            glow.flowWidth = flowWidth;
            glow.tipBoost = tipBoost;
            glow.orbitSpeed = orbitSpeed;
        }

        static void BuildWeaponViewmodels()
        {
            Material core = Mat("M_WeaponCore") != null ? Mat("M_WeaponCore") : Mat("M_Ground");
            Material energy = Mat("M_Energy") != null ? Mat("M_Energy") : Mat("M_Item");

            // Cerulean Edge — segmented straight blade, cross guard, hovering guard ring.
            // Cool steel-blue, steady breath: the dependable one.
            {
                var root = new GameObject("VM_Sword");
                StackSegments(root.transform, energy, 6, 0.04f, 0.86f, 0.055f, 0.11f, 0.72f);
                Prim(PrimitiveType.Cube, "Guard", root.transform, Vector3.zero, new Vector3(0.30f, 0.05f, 0.08f), core);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.15f, 0f), new Vector3(0.05f, 0.25f, 0.05f), core);
                Prim(PrimitiveType.Cube, "Pommel", root.transform, new Vector3(0f, -0.29f, 0f), new Vector3(0.08f, 0.05f, 0.08f), core);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.94f, 0f), new Vector3(0.035f, 0.10f, 0.07f), energy);
                // Ring hovering at the guard, counter-rotating slowly.
                Prim(PrimitiveType.Cube, "FloatRing", root.transform, new Vector3(0.13f, 0.05f, 0f), new Vector3(0.05f, 0.012f, 0.05f), energy);
                Energise(root, new Color(0.56f, 0.71f, 0.85f), 1.15f, 0.75f, 0.16f, 2.4f, 42f);
                var prefab = Save(root, $"{WeaponDir}/VM_Sword.prefab");
                AssignViewmodel("Assets/Data/Weapons/Sword.asset", prefab);
            }
            // Sunbreaker — short haft, head built from offset blocks so it is not a plain cube,
            // two shards orbiting it. Slow heavy pulse; the energy lives in the head, not the shaft.
            {
                var root = new GameObject("VM_Hammer");
                StackSegments(root.transform, energy, 4, -0.10f, 0.62f, 0.055f, 0.055f);
                // A bound haft the fist can close on. Every other weapon already had a Grip* part;
                // without one here the hand would have gripped empty air, because WeaponViewmodel
                // positions the hand on the prefab's Grip.
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.085f, 0.24f, 0.085f), core);
                Prim(PrimitiveType.Cube, "HeadCore", root.transform, new Vector3(0f, 0.66f, 0f), new Vector3(0.30f, 0.22f, 0.22f), core);
                Prim(PrimitiveType.Cube, "HeadCheekL", root.transform, new Vector3(-0.17f, 0.66f, 0f), new Vector3(0.09f, 0.16f, 0.17f), core);
                Prim(PrimitiveType.Cube, "HeadCheekR", root.transform, new Vector3(0.17f, 0.66f, 0f), new Vector3(0.09f, 0.16f, 0.17f), core);
                Prim(PrimitiveType.Cube, "TipBand", root.transform, new Vector3(0f, 0.66f, 0f), new Vector3(0.32f, 0.05f, 0.24f), energy);
                Prim(PrimitiveType.Cube, "FloatShardA", root.transform, new Vector3(0.24f, 0.72f, 0f), new Vector3(0.05f, 0.05f, 0.05f), energy);
                Prim(PrimitiveType.Cube, "FloatShardB", root.transform, new Vector3(-0.24f, 0.60f, 0f), new Vector3(0.04f, 0.04f, 0.04f), energy);
                Energise(root, new Color(0.88f, 0.40f, 0.10f), 0.7f, 0.42f, 0.24f, 2.0f, 34f);
                var prefab = Save(root, $"{WeaponDir}/VM_Hammer.prefab");
                AssignViewmodel("Assets/Data/Weapons/Hammer.asset", prefab);
            }
            // Rosethorn — short tapering blade, thin guard, one fast mote. Quick nervous pulse.
            {
                var root = new GameObject("VM_Dagger");
                StackSegments(root.transform, energy, 4, 0.02f, 0.44f, 0.048f, 0.075f, 0.55f);
                Prim(PrimitiveType.Cube, "Guard", root.transform, Vector3.zero, new Vector3(0.16f, 0.035f, 0.06f), core);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.10f, 0f), new Vector3(0.04f, 0.18f, 0.04f), core);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.50f, 0f), new Vector3(0.026f, 0.07f, 0.045f), energy);
                Prim(PrimitiveType.Cube, "FloatMote", root.transform, new Vector3(0.08f, 0.20f, 0f), new Vector3(0.028f, 0.028f, 0.028f), energy);
                Energise(root, new Color(0.37f, 0.84f, 0.42f), 2.4f, 1.5f, 0.11f, 2.6f, 96f);
                var prefab = Save(root, $"{WeaponDir}/VM_Dagger.prefab");
                AssignViewmodel("Assets/Data/Weapons/Dagger.asset", prefab);
            }
            // Oathbreaker (dev) — deliberately excessive: longest blade, two rings at different radii,
            // brightest energy. It is the cheat weapon and should look like one.
            {
                var root = new GameObject("VM_DevBlade");
                StackSegments(root.transform, energy, 8, 0.04f, 1.08f, 0.065f, 0.13f, 0.68f);
                Prim(PrimitiveType.Cube, "Guard", root.transform, Vector3.zero, new Vector3(0.34f, 0.05f, 0.08f), core);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.17f, 0f), new Vector3(0.05f, 0.28f, 0.05f), core);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 1.17f, 0f), new Vector3(0.04f, 0.12f, 0.08f), energy);
                Prim(PrimitiveType.Cube, "FloatRingInner", root.transform, new Vector3(0.11f, 0.30f, 0f), new Vector3(0.045f, 0.012f, 0.045f), energy);
                Prim(PrimitiveType.Cube, "FloatRingOuter", root.transform, new Vector3(0.20f, 0.62f, 0f), new Vector3(0.055f, 0.014f, 0.055f), energy);
                Energise(root, new Color(0.50f, 1f, 0.60f), 1.8f, 1.25f, 0.13f, 3.0f, 70f);
                var prefab = Save(root, $"{WeaponDir}/VM_DevBlade.prefab");
                AssignViewmodel("Assets/Data/Weapons/DevBlade.asset", prefab);
            }
        }

        static void AssignViewmodel(string weaponAssetPath, GameObject prefab)
        {
            var w = Load<WeaponData>(weaponAssetPath);
            if (w == null || prefab == null) return;
            w.viewmodelPrefab = prefab;
            EditorUtility.SetDirty(w);
        }

        // ------------------------------------------------------------------ A2. wands

        /// <summary>
        /// Wand viewmodels for the riposte. Each is a dark shaft plus a distinct emissive tip; the
        /// tip renderer MUST be named "Tip*" because WandController tints it per-wand through a
        /// MaterialPropertyBlock, which is why one white material serves all four.
        /// </summary>
        static void BuildWandViewmodels()
        {
            Material core = Mat("M_WeaponCore") != null ? Mat("M_WeaponCore") : Mat("M_Ground");
            Material energy = Mat("M_Energy");
            if (energy == null)
            {
                Debug.LogWarning("[PrefabFactory] M_Energy.mat not found, falling back to M_Item for wand energy parts.");
                energy = Mat("M_Item");
            }

            // Emberlance (Bolt) — slim segmented shaft, one sharp tip crystal, one tight ring.
            // Fast flow, tight band: precise.
            {
                var root = new GameObject("VM_Emberlance");
                StackSegments(root.transform, energy, 5, 0.00f, 0.40f, 0.042f, 0.042f);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.10f, 0f), new Vector3(0.05f, 0.18f, 0.05f), core);
                Prim(PrimitiveType.Cube, "Collar", root.transform, new Vector3(0f, 0.40f, 0f), new Vector3(0.10f, 0.045f, 0.10f), core);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.50f, 0f), new Vector3(0.075f, 0.14f, 0.075f), energy);
                Prim(PrimitiveType.Cube, "FloatRing", root.transform, new Vector3(0.09f, 0.44f, 0f), new Vector3(0.035f, 0.012f, 0.035f), energy);
                Energise(root, Color.white, 1.6f, 1.35f, 0.12f, 2.6f, 78f);
                AssignWand("Emberlance", Save(root, $"{WandDir}/VM_Emberlance.prefab"));
            }
            // Gravecall (Scatter) — shorter thicker shaft, splayed three-prong head, loose motes.
            // Slow wide pulse: heavy.
            {
                var root = new GameObject("VM_Gravecall");
                StackSegments(root.transform, energy, 4, 0.00f, 0.34f, 0.062f, 0.062f);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.10f, 0f), new Vector3(0.07f, 0.18f, 0.07f), core);
                // Three prongs splaying outward from the muzzle.
                var prongL = Prim(PrimitiveType.Cube, "ProngL", root.transform, new Vector3(-0.07f, 0.44f, 0f), new Vector3(0.028f, 0.18f, 0.028f), core);
                prongL.transform.localRotation = Quaternion.Euler(0f, 0f, 22f);
                var prongR = Prim(PrimitiveType.Cube, "ProngR", root.transform, new Vector3(0.07f, 0.44f, 0f), new Vector3(0.028f, 0.18f, 0.028f), core);
                prongR.transform.localRotation = Quaternion.Euler(0f, 0f, -22f);
                var prongB = Prim(PrimitiveType.Cube, "ProngB", root.transform, new Vector3(0f, 0.44f, 0.07f), new Vector3(0.028f, 0.18f, 0.028f), core);
                prongB.transform.localRotation = Quaternion.Euler(-22f, 0f, 0f);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.42f, 0f), new Vector3(0.10f, 0.06f, 0.10f), energy);
                Prim(PrimitiveType.Cube, "FloatMoteA", root.transform, new Vector3(0.13f, 0.52f, 0f), new Vector3(0.032f, 0.032f, 0.032f), energy);
                Prim(PrimitiveType.Cube, "FloatMoteB", root.transform, new Vector3(-0.16f, 0.46f, 0f), new Vector3(0.026f, 0.026f, 0.026f), energy);
                Energise(root, Color.white, 0.75f, 0.45f, 0.26f, 2.0f, 30f);
                AssignWand("Gravecall", Save(root, $"{WandDir}/VM_Gravecall.prefab"));
            }
            // Stormneedle (Chain) — long thin shaft, small tip, two fast counter-rotating rings.
            // Fastest flow, tightest band: electric.
            {
                var root = new GameObject("VM_Stormneedle");
                StackSegments(root.transform, energy, 7, 0.00f, 0.56f, 0.032f, 0.032f);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.09f, 0f), new Vector3(0.045f, 0.16f, 0.045f), core);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.60f, 0f), new Vector3(0.045f, 0.08f, 0.045f), energy);
                Prim(PrimitiveType.Cube, "FloatRingA", root.transform, new Vector3(0.08f, 0.30f, 0f), new Vector3(0.030f, 0.010f, 0.030f), energy);
                Prim(PrimitiveType.Cube, "FloatRingB", root.transform, new Vector3(0.11f, 0.48f, 0f), new Vector3(0.026f, 0.010f, 0.026f), energy);
                Energise(root, Color.white, 2.6f, 2.1f, 0.08f, 2.8f, 130f);
                AssignWand("Stormneedle", Save(root, $"{WandDir}/VM_Stormneedle.prefab"));
            }
            // Voidspine (Lance) — longest, segmented spine, caged tip, one slow heavy ring. Ominous.
            {
                var root = new GameObject("VM_Voidspine");
                StackSegments(root.transform, energy, 6, 0.00f, 0.52f, 0.055f, 0.055f, 0.75f);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.11f, 0f), new Vector3(0.075f, 0.20f, 0.075f), core);
                Prim(PrimitiveType.Cube, "Guard", root.transform, new Vector3(0f, 0.32f, 0f), new Vector3(0.17f, 0.035f, 0.06f), core);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.62f, 0f), new Vector3(0.055f, 0.16f, 0.055f), energy);
                // Cage: thin bars around the tip so the core reads as contained rather than exposed.
                Prim(PrimitiveType.Cube, "CageA", root.transform, new Vector3(0.055f, 0.62f, 0f), new Vector3(0.014f, 0.20f, 0.014f), core);
                Prim(PrimitiveType.Cube, "CageB", root.transform, new Vector3(-0.055f, 0.62f, 0f), new Vector3(0.014f, 0.20f, 0.014f), core);
                Prim(PrimitiveType.Cube, "CageC", root.transform, new Vector3(0f, 0.62f, 0.055f), new Vector3(0.014f, 0.20f, 0.014f), core);
                Prim(PrimitiveType.Cube, "CageD", root.transform, new Vector3(0f, 0.62f, -0.055f), new Vector3(0.014f, 0.20f, 0.014f), core);
                Prim(PrimitiveType.Cube, "FloatRing", root.transform, new Vector3(0.14f, 0.40f, 0f), new Vector3(0.045f, 0.016f, 0.045f), energy);
                Energise(root, Color.white, 0.6f, 0.35f, 0.30f, 2.2f, 22f);
                AssignWand("Voidspine", Save(root, $"{WandDir}/VM_Voidspine.prefab"));
            }
        }

        // ------------------------------------------------------------------ A3. item viewmodels

        /// <summary>
        /// Offhand models for the carried spells, so a swapped-in item visibly says WHICH spell is
        /// queued instead of every item showing the same tinted cube.
        ///
        /// ORDERING: DataFactory (step 3) must run before PrefabFactory (step 4) — these assign the
        /// prefab back onto the ItemData asset, exactly like the wands. `0. Rebuild Everything` already
        /// sequences it correctly.
        /// </summary>
        static void BuildItemViewmodels()
        {
            Material core = Mat("M_WeaponCore") != null ? Mat("M_WeaponCore") : Mat("M_Ground");
            Material energy = Mat("M_Energy") != null ? Mat("M_Energy") : Mat("M_Item");

            // Updraft — stacked open rings with motes rising through them. Reads as lift.
            {
                var root = new GameObject("VM_Item_Updraft");
                for (int i = 0; i < 4; i++)
                {
                    // Rings widen as they climb, so the silhouette opens upward like a draught.
                    float y = 0.02f + i * 0.075f;
                    float w = 0.10f + i * 0.022f;
                    Prim(PrimitiveType.Cube, $"Seg{i}", root.transform, new Vector3(0f, y, 0f), new Vector3(w, 0.014f, w), energy);
                }
                Prim(PrimitiveType.Cube, "Spindle", root.transform, new Vector3(0f, 0.13f, 0f), new Vector3(0.018f, 0.28f, 0.018f), core);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.30f, 0f), new Vector3(0.05f, 0.05f, 0.05f), energy);
                Prim(PrimitiveType.Cube, "FloatMoteA", root.transform, new Vector3(0.07f, 0.10f, 0f), new Vector3(0.022f, 0.022f, 0.022f), energy);
                Prim(PrimitiveType.Cube, "FloatMoteB", root.transform, new Vector3(-0.06f, 0.20f, 0f), new Vector3(0.018f, 0.018f, 0.018f), energy);
                // Fast upward flow is the whole read: the band climbing the rings IS the lift.
                Energise(root, Color.white, 1.5f, 2.0f, 0.14f, 2.4f, 60f);
                AssignItem("Updraft", Save(root, $"{ItemDir}/VM_Item_Updraft.prefab"));
            }
            // Soul Lantern — a caged frame around a bright core, with a hanging ring. Warm and steady.
            {
                var root = new GameObject("VM_Item_SoulLantern");
                Prim(PrimitiveType.Cube, "Hanger", root.transform, new Vector3(0f, 0.30f, 0f), new Vector3(0.055f, 0.014f, 0.055f), core);
                Prim(PrimitiveType.Cube, "CapTop", root.transform, new Vector3(0f, 0.24f, 0f), new Vector3(0.14f, 0.03f, 0.14f), core);
                Prim(PrimitiveType.Cube, "CapBottom", root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.14f, 0.03f, 0.14f), core);
                // Four corner bars form the cage.
                Prim(PrimitiveType.Cube, "BarA", root.transform, new Vector3(0.055f, 0.13f, 0.055f), new Vector3(0.014f, 0.22f, 0.014f), core);
                Prim(PrimitiveType.Cube, "BarB", root.transform, new Vector3(-0.055f, 0.13f, 0.055f), new Vector3(0.014f, 0.22f, 0.014f), core);
                Prim(PrimitiveType.Cube, "BarC", root.transform, new Vector3(0.055f, 0.13f, -0.055f), new Vector3(0.014f, 0.22f, 0.014f), core);
                Prim(PrimitiveType.Cube, "BarD", root.transform, new Vector3(-0.055f, 0.13f, -0.055f), new Vector3(0.014f, 0.22f, 0.014f), core);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.13f, 0f), new Vector3(0.075f, 0.09f, 0.075f), energy);
                Prim(PrimitiveType.Cube, "FloatEmber", root.transform, new Vector3(0.045f, 0.13f, 0f), new Vector3(0.018f, 0.018f, 0.018f), energy);
                // No Seg* parts: a lantern should sit and glow, not have energy racing through it.
                Energise(root, Color.white, 0.55f, 0f, 0.2f, 2.6f, 26f);
                AssignItem("SoulLantern", Save(root, $"{ItemDir}/VM_Item_SoulLantern.prefab"));
            }
            // Phantom Step — a shard split into offset pieces that drift and rejoin. Reads as displacement.
            {
                var root = new GameObject("VM_Item_PhantomStep");
                // Three offset slices along the axis; the flow band jumping between them looks like a
                // fracture rather than a smooth sweep.
                var s0 = Prim(PrimitiveType.Cube, "Seg0", root.transform, new Vector3(-0.02f, 0.03f, 0f), new Vector3(0.07f, 0.10f, 0.05f), energy);
                s0.transform.localRotation = Quaternion.Euler(0f, 0f, 9f);
                var s1 = Prim(PrimitiveType.Cube, "Seg1", root.transform, new Vector3(0.015f, 0.14f, 0.012f), new Vector3(0.06f, 0.11f, 0.045f), energy);
                s1.transform.localRotation = Quaternion.Euler(4f, 0f, -12f);
                var s2 = Prim(PrimitiveType.Cube, "Seg2", root.transform, new Vector3(-0.01f, 0.25f, -0.01f), new Vector3(0.045f, 0.09f, 0.04f), energy);
                s2.transform.localRotation = Quaternion.Euler(-5f, 0f, 16f);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.33f, 0f), new Vector3(0.035f, 0.05f, 0.035f), energy);
                Prim(PrimitiveType.Cube, "FloatGhostA", root.transform, new Vector3(0.075f, 0.16f, 0f), new Vector3(0.022f, 0.05f, 0.016f), energy);
                Prim(PrimitiveType.Cube, "FloatGhostB", root.transform, new Vector3(-0.085f, 0.09f, 0f), new Vector3(0.018f, 0.04f, 0.014f), energy);
                // Fast, tight, erratic-feeling: unstable.
                Energise(root, Color.white, 3.2f, 2.6f, 0.07f, 2.8f, 150f);
                AssignItem("PhantomStep", Save(root, $"{ItemDir}/VM_Item_PhantomStep.prefab"));
            }
        }

        static void AssignItem(string itemName, GameObject prefab)
        {
            var item = Load<ItemData>($"Assets/Data/Items/{itemName}.asset");
            if (item == null || prefab == null) return;
            item.viewmodelPrefab = prefab;
            EditorUtility.SetDirty(item);
        }

        static void AssignWand(string wandName, GameObject prefab)
        {
            var wand = Load<WandData>($"Assets/Data/Wands/{wandName}.asset");
            if (wand == null || prefab == null) return;
            wand.viewmodelPrefab = prefab;
            EditorUtility.SetDirty(wand);
        }

        // ------------------------------------------------------------------ A4. viewmodel arms

        /// <summary>The three transforms the viewmodels need back from <see cref="BuildHand"/>.</summary>
        struct HandRig
        {
            public Transform hand;    // rigid child of the posed model node
            public Transform grip;    // what the weapon/wand parents to
            public Transform wrist;   // where the forearm ends
        }

        /// <summary>
        /// A blocky gauntleted fist, authored around the ORIGIN because the runtime slides the whole
        /// hand onto the weapon's Grip* part and cancels the offset on the grip node — so hand-local
        /// (0,0,0) is always the middle of whatever hilt is being held.
        ///
        /// <para>Object count is deliberately small (10 boxes): at 95° FOV with the hand half a metre
        /// from the lens, four finger slabs, a knuckle band, a thumb and a cuff are already past the
        /// point where more detail reads. Everything is offset in Z so the hilt passes BETWEEN the palm
        /// and the fingers, which is what makes the grip look closed rather than adjacent.</para>
        /// </summary>
        static HandRig BuildHand(Transform parent, bool right, Material glove, Material trim)
        {
            float sx = right ? 1f : -1f;
            var handGo = Empty(right ? "HandR" : "HandL", parent, Vector3.zero);
            var rig = new HandRig();
            rig.hand = handGo.transform;
            rig.grip = Empty("Grip", handGo.transform, Vector3.zero).transform;

            // Z MATTERS MORE THAN DETAIL. The camera sees the -Z face of the hand, so the FINGERS go on
            // -Z (in front of the hilt, occluding it) and the palm behind it on +Z. Built the other way
            // round the fist is one featureless slab with a blade sticking out of it — which is exactly
            // how the first build read on screen.
            Prim(PrimitiveType.Cube, "Palm", handGo.transform,
                 new Vector3(-0.004f * sx, 0f, 0.022f), new Vector3(0.078f, 0.105f, 0.030f), glove);

            // Four banded fingers, in the lighter trim so the grip reads as digits closed around the
            // hilt rather than as a block the weapon happens to intersect.
            for (int i = 0; i < 4; i++)
            {
                Prim(PrimitiveType.Cube, "Finger" + i, handGo.transform,
                     new Vector3(0f, 0.030f - i * 0.022f, -0.020f),
                     new Vector3(0.082f - i * 0.004f, 0.017f, 0.028f), trim);
            }

            var thumb = Prim(PrimitiveType.Cube, "Thumb", handGo.transform,
                             new Vector3(0.030f * sx, 0.032f, -0.004f), new Vector3(0.026f, 0.056f, 0.032f), glove);
            thumb.transform.localRotation = Quaternion.Euler(0f, 0f, -26f * sx);

            // Cuff is dark: it is the largest piece and reads as forearm, not as jewellery. Only the
            // thin band at its lip catches the trim.
            Prim(PrimitiveType.Cube, "Cuff", handGo.transform,
                 new Vector3(0f, -0.078f, 0.004f), new Vector3(0.088f, 0.042f, 0.072f), glove);
            Prim(PrimitiveType.Cube, "CuffBand", handGo.transform,
                 new Vector3(0f, -0.058f, 0.004f), new Vector3(0.094f, 0.010f, 0.078f), trim);

            rig.wrist = Empty("Wrist", handGo.transform, new Vector3(0f, -0.105f, -0.016f)).transform;

            StripViewmodelShadows(handGo);
            return rig;
        }

        /// <summary>
        /// The two arm bones plus the solver. The rig root is placed by the caller: the WEAPON arm lives
        /// under ViewmodelRoot so sway and bob move the shoulder with the hand (the arm travels as one
        /// unit), while the OFFHAND arm lives under the Camera, because the offhand root itself is
        /// swung across the screen by the poses and a shoulder that followed it would be absurd.
        /// </summary>
        static ViewmodelArm BuildArm(Transform parent, string name, Transform wrist, bool right,
                                     Material glove, float upperLen, float foreLen)
        {
            float sx = right ? 1f : -1f;
            var rigGo = Empty(name, parent, Vector3.zero);
            var upper = Prim(PrimitiveType.Cube, "UpperArm", rigGo.transform, Vector3.zero, Vector3.one * 0.1f, glove);
            var fore = Prim(PrimitiveType.Cube, "Forearm", rigGo.transform, Vector3.zero, Vector3.one * 0.1f, glove);

            var arm = rigGo.AddComponent<ViewmodelArm>();
            arm.wrist = wrist;
            arm.upperArm = upper.transform;
            arm.forearm = fore.transform;
            // Shoulder is pushed well off the camera axis on purpose: the arm inevitably crosses the
            // 0.03 near plane on its way back to the body, and out here that cut happens outside the
            // frustum instead of as a hole punched through the middle of the frame.
            arm.shoulderLocal = new Vector3(0.22f * sx, -0.40f, -0.10f);
            arm.poleLocal = new Vector3(0.6f * sx, -1f, -0.35f);
            arm.upperLength = upperLen;
            arm.foreLength = foreLen;
            arm.upperThickness = right ? 0.078f : 0.074f;
            arm.foreThickness = right ? 0.066f : 0.062f;
            arm.maxStretch = 1.35f;

            StripViewmodelShadows(rigGo);
            return arm;
        }

        /// <summary>Viewmodel geometry must never cast into the world — it sits inside the player.</summary>
        static void StripViewmodelShadows(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        // ------------------------------------------------------------------ B. player

        static void BuildPlayer()
        {
            var root = new GameObject("Player");
            root.layer = Layers.Player;

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 45f;
            cc.skinWidth = 0.05f;

            root.AddComponent<FirstPersonMotor>();
            var look = root.AddComponent<PlayerLook>();
            root.AddComponent<Health>();
            var stats = root.AddComponent<PlayerStats>();
            stats.data = Load<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            root.AddComponent<PlayerResources>();
            root.AddComponent<PlayerPosture>();
            root.AddComponent<ParryController>();
            root.AddComponent<PlayerCombat>();
            var weapons = root.AddComponent<WeaponController>();
            weapons.loadout = new[]
            {
                Load<WeaponData>("Assets/Data/Weapons/Sword.asset"),
                Load<WeaponData>("Assets/Data/Weapons/Hammer.asset"),
                Load<WeaponData>("Assets/Data/Weapons/Dagger.asset"),
                Load<WeaponData>("Assets/Data/Weapons/DevBlade.asset"),
            };
            weapons.hitSparkMaterial = Mat("M_NeonYellow");

            // Wands are the riposte, not a free-fire weapon — ExecuteInteractor drives this.
            var wands = root.AddComponent<WandController>();
            wands.loadout = new[]
            {
                Load<WandData>("Assets/Data/Wands/Emberlance.asset"),
                Load<WandData>("Assets/Data/Wands/Gravecall.asset"),
                Load<WandData>("Assets/Data/Wands/Stormneedle.asset"),
                Load<WandData>("Assets/Data/Wands/Voidspine.asset"),
            };

            var execInteractor = root.AddComponent<ExecuteInteractor>();
            // Written explicitly for the same reason as the offhand poses: the prefab keeps whatever was
            // serialised, so the field initialiser is not the shipped value. 1.25 put the camera 0.8m
            // from a 0.45m-radius capsule, which at 95° FOV is a wall of black filling the whole frame —
            // the riposte's own step-in was hiding the riposte.
            execInteractor.stabStandoff = 2.2f;
            root.AddComponent<FlaskAbility>();
            var ult = root.AddComponent<UltimateAbility>();
            ult.ringMaterial = Mat("M_NeonCyan");
            root.AddComponent<PlayerDeath>();
            root.AddComponent<PlayerFeedback>();
            root.AddComponent<PlayerItems>();

            // Camera rig
            var pivot = Empty("CameraPivot", root.transform, new Vector3(0f, 1.6f, 0f));
            var shakeRoot = Empty("ShakeRoot", pivot.transform, Vector3.zero);
            shakeRoot.AddComponent<CameraShake>();

            var camGo = Empty("Camera", shakeRoot.transform, Vector3.zero);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 95f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("#06040A");
            camGo.AddComponent<AudioListener>();
            var urpCam = cam.GetUniversalAdditionalCameraData();
            urpCam.renderPostProcessing = true;
            urpCam.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            var fx = camGo.AddComponent<CameraFX>();
            fx.cam = cam;
            fx.baseFov = 95f;

            // Persistent LEFT-hand slot. Always on screen: the wand used to appear only for the
            // duration of a riposte, which is why the blast read as an explosion with no source.
            var offRoot = Empty("OffhandRoot", camGo.transform, Vector3.zero);
            var offhand = offRoot.AddComponent<OffhandViewmodel>();
            // A CODE DEFAULT IS NOT A SHIPPED VALUE. These are public fields, so the Player prefab keeps
            // whatever was serialised the day it was built — editing the initialisers in
            // OffhandViewmodel changes nothing here. Every pose the riposte depends on is written
            // explicitly so a rebuild is authoritative.
            offhand.restPosition = new Vector3(-0.34f, -0.30f, 0.52f);
            offhand.restEuler = new Vector3(10f, 18f, -10f);
            offhand.raisedPosition = new Vector3(-0.24f, -0.17f, 0.46f);
            offhand.raisedEuler = new Vector3(-4f, 6f, -6f);
            offhand.cockedPosition = new Vector3(-0.48f, -0.32f, 0.38f);
            offhand.cockedEuler = new Vector3(24f, 42f, -22f);
            offhand.thrustPosition = new Vector3(-0.17f, -0.12f, 0.66f);
            offhand.thrustEuler = new Vector3(58f, -10f, 6f);
            offhand.tipLightEnabled = true;
            offhand.tipLightIdle = 1.6f;
            offhand.tipLightCharged = 9f;
            offhand.tipLightMuzzle = 26f;
            offhand.tipLightRange = 10f;

            // ---- visible arms -------------------------------------------------------------------
            // A CODE DEFAULT IS NOT A SHIPPED VALUE (rule 9): every transform and every solver number
            // below is written here, not left to a field initialiser, or a rebuild would not reach the
            // existing Player.prefab.
            Material glove = Mat("M_Gauntlet") != null ? Mat("M_Gauntlet") : Mat("M_WeaponCore");
            Material gloveTrim = Mat("M_GauntletTrim") != null ? Mat("M_GauntletTrim") : glove;

            var offModel = Empty("OffhandModel", offRoot.transform, Vector3.zero);
            offhand.model = offModel.transform;
            HandRig leftHand = BuildHand(offModel.transform, false, glove, gloveTrim);
            offhand.hand = leftHand.hand;
            offhand.grip = leftHand.grip;
            // Left arm rig hangs off the CAMERA, not off OffhandRoot: OffhandRoot is the thing being
            // swung around by the rest/raised/thrust poses, so a shoulder parented to it would travel
            // with the hand and the arm would never bend.
            offhand.arm = BuildArm(camGo.transform, "OffhandArmRig", leftHand.wrist, false, glove, 0.36f, 0.38f);

            var vmRoot = Empty("ViewmodelRoot", camGo.transform, Vector3.zero);
            var vm = vmRoot.AddComponent<WeaponViewmodel>();
            var model = Empty("Model", vmRoot.transform, Vector3.zero);
            vm.model = model.transform;

            HandRig rightHand = BuildHand(model.transform, true, glove, gloveTrim);
            vm.hand = rightHand.hand;
            vm.grip = rightHand.grip;
            // Weapon arm rides ViewmodelRoot so sway/bob move shoulder, arm and weapon as one unit.
            // Bones are long (0.46 + 0.48) because the authored poses reach 0.74m at the windup and
            // 1.03m at the execute slam. Sized for the FAR end minus a little: shorter bones sit
            // permanently straight (a stiff, dead arm) and stretch 20% on every deathblow.
            vm.arm = BuildArm(vmRoot.transform, "ArmRig", rightHand.wrist, true, glove, 0.46f, 0.48f);

            look.pivot = pivot.transform;
            look.cam = camGo.transform;

            Save(root, $"{PrefabDir}/Player.prefab");
        }

        // ------------------------------------------------------------------ C. managers

        static void BuildManagers(GameObject bloodstainPrefab)
        {
            var root = new GameObject("Managers");
            var gm = root.AddComponent<GameManager>();
            gm.statsData = Load<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            gm.feel = Load<GameFeelSettings>("Assets/Data/GameFeel.asset");
            root.AddComponent<InputReader>();
            root.AddComponent<TimeScaleController>();
            root.AddComponent<AudioManager>();
            root.AddComponent<SoulsWallet>();
            root.AddComponent<SpeedrunTimer>();
            var lm = root.AddComponent<LevelManager>();
            lm.bloodstainPrefab = bloodstainPrefab;
            lm.startSpawn = null; // level builder assigns
            root.AddComponent<DebugKeys>();

            Save(root, $"{PrefabDir}/Managers.prefab");
        }

        // ------------------------------------------------------------------ D. enemies

        static void BuildEnemy(string name, string dataPath, bool isBoss)
        {
            var root = new GameObject(name);
            root.layer = Layers.Enemy;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.45f;
            agent.height = 2f;
            agent.speed = 4f;
            agent.angularSpeed = 360f;
            agent.acceleration = 40f;
            agent.stoppingDistance = 1.5f;

            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.radius = 0.45f;
            col.height = 2f;
            col.isTrigger = false;

            root.AddComponent<Health>();
            root.AddComponent<Posture>();

            EnemyController ctrl;
            if (isBoss)
            {
                var boss = root.AddComponent<BossController>();
                boss.data = Load<BossData>(dataPath);
                ctrl = boss;
            }
            else
            {
                ctrl = root.AddComponent<EnemyController>();
                ctrl.data = Load<EnemyData>(dataPath);
            }

            Material bodyMat = Mat(isBoss ? "M_Boss" : "M_Enemy");

            var visual = Empty("Visual", root.transform, Vector3.zero);
            var visuals = visual.AddComponent<EnemyVisuals>();
            var flash = visual.AddComponent<EmissiveFlash>();

            var lungeRoot = Empty("LungeRoot", visual.transform, Vector3.zero);
            var body = Prim(PrimitiveType.Capsule, "Body", lungeRoot.transform, new Vector3(0f, 1f, 0f), new Vector3(0.9f, 1f, 0.9f), bodyMat);
            var eye = Prim(PrimitiveType.Cube, "Eye", lungeRoot.transform, new Vector3(0f, 1.55f, 0.4f), new Vector3(0.3f, 0.12f, 0.15f), Mat("M_EnemyEye"));

            // ---- arm rig: shoulder -> upper arm -> forearm/hand -> weapon --------------------------
            // Rotating the shoulder swings the whole limb, so wind-ups read from the silhouette.
            var shoulder = Empty("ArmPivot", lungeRoot.transform, new Vector3(0.5f, 1.45f, 0f));
            Prim(PrimitiveType.Cube, "UpperArm", shoulder.transform, new Vector3(0f, -0.28f, 0f), new Vector3(0.17f, 0.56f, 0.17f), bodyMat);

            var hand = Empty("WeaponPivot", shoulder.transform, new Vector3(0f, -0.56f, 0f));
            Prim(PrimitiveType.Cube, "ForeArm", hand.transform, new Vector3(0f, -0.22f, 0f), new Vector3(0.14f, 0.46f, 0.14f), bodyMat);

            var weapon = Prim(PrimitiveType.Cube, "Weapon", hand.transform, new Vector3(0f, -0.45f, 0.42f), new Vector3(0.12f, 0.14f, 1.35f), bodyMat);
            weapon.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);

            var alert = Prim(PrimitiveType.Cube, "Alert", visual.transform, new Vector3(0f, 2.5f, 0f), new Vector3(0.25f, 0.25f, 0.25f), Mat("M_AlertTell"));
            alert.SetActive(false);

            visuals.body = body.GetComponent<Renderer>();
            visuals.eye = eye.GetComponent<Renderer>();
            visuals.weapon = weapon.GetComponent<Renderer>();
            visuals.lungeRoot = lungeRoot.transform;
            visuals.armPivot = shoulder.transform;
            visuals.weaponPivot = hand.transform;
            visuals.alertMarker = alert;
            flash.renderers = new[] { visuals.body, visuals.weapon };

            // ---- posture bar: small enemies only; the boss has the HUD bar -------------------------
            if (!isBoss) BuildPostureBar(visual.transform);

            Save(root, $"{PrefabDir}/{name}.prefab");
        }

        /// <summary>Two quads above the head: dark backing plus a left-anchored fill pivot scaled 0..1.</summary>
        static void BuildPostureBar(Transform parent)
        {
            const float width = 1.1f;
            const float height = 0.13f;

            var bar = Empty("PostureBar", parent, new Vector3(0f, 2.6f, 0f));
            var comp = bar.AddComponent<EnemyPostureBar>();

            var bg = Prim(PrimitiveType.Quad, "BarBG", bar.transform, Vector3.zero, new Vector3(width, height, 1f), Mat("M_Ground"));

            // Pivot sits on the bar's LEFT edge. The quad is scaled by `width` and offset by half of it,
            // so in pivot space it spans exactly 0..width — scaling the pivot's X by k then fills 0..k.
            var fillPivot = Empty("FillPivot", bar.transform, new Vector3(-width * 0.5f, 0f, 0f));
            var fill = Prim(PrimitiveType.Quad, "BarFill", fillPivot.transform,
                new Vector3(width * 0.5f, 0f, -0.01f), new Vector3(width, height * 0.62f, 1f), Mat("M_NeonYellow"));

            comp.billboard = bar.transform;
            comp.fillPivot = fillPivot.transform;
            comp.fillRenderer = fill.GetComponent<Renderer>();
            comp.backgroundRenderer = bg.GetComponent<Renderer>();
            comp.headHeight = 2f;
            comp.heightAboveHead = 0.6f;
        }

        // ------------------------------------------------------------------ E. bloodstain

        static GameObject BuildBloodstain()
        {
            var root = new GameObject("Bloodstain");
            root.layer = Layers.Interactable;
            var sc = root.AddComponent<SphereCollider>();
            sc.radius = 1f;
            sc.isTrigger = true;
            var bs = root.AddComponent<Bloodstain>();

            var visual = Prim(PrimitiveType.Cube, "Visual", root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(0.35f, 0.35f, 0.35f), Mat("M_Bloodstain"));
            visual.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
            bs.visual = visual.transform;

            return Save(root, $"{PrefabDir}/Bloodstain.prefab");
        }

        // ------------------------------------------------------------------ F. checkpoint

        static void BuildCheckpoint()
        {
            var root = new GameObject("Checkpoint");
            root.layer = Layers.Interactable;
            var bc = root.AddComponent<BoxCollider>();
            bc.size = new Vector3(3f, 3f, 3f);
            bc.center = new Vector3(0f, 1.5f, 0f);
            bc.isTrigger = true;
            var cp = root.AddComponent<Checkpoint>();

            var spawn = Empty("Spawn", root.transform, new Vector3(0f, 0.2f, -2f));
            cp.spawnPoint = spawn.transform;

            var pillar = Prim(PrimitiveType.Cube, "Pillar", root.transform, new Vector3(0f, 1.5f, 0f), new Vector3(0.4f, 3f, 0.4f), Mat("M_Checkpoint"));
            var flash = pillar.AddComponent<EmissiveFlash>();
            flash.renderers = new[] { pillar.GetComponent<Renderer>() };

            // decorative ring of 4 thin cubes around the base
            Material ring = Mat("M_Checkpoint");
            float r = 1.2f;
            Prim(PrimitiveType.Cube, "Ring_N", root.transform, new Vector3(0f, 0.08f, r), new Vector3(2.4f, 0.12f, 0.12f), ring);
            Prim(PrimitiveType.Cube, "Ring_S", root.transform, new Vector3(0f, 0.08f, -r), new Vector3(2.4f, 0.12f, 0.12f), ring);
            Prim(PrimitiveType.Cube, "Ring_E", root.transform, new Vector3(r, 0.08f, 0f), new Vector3(0.12f, 0.12f, 2.4f), ring);
            Prim(PrimitiveType.Cube, "Ring_W", root.transform, new Vector3(-r, 0.08f, 0f), new Vector3(0.12f, 0.12f, 2.4f), ring);

            Save(root, $"{PrefabDir}/Checkpoint.prefab");
        }

        // ------------------------------------------------------------------ G. item pickup

        /// <summary>
        /// Floating single-use item. ItemPickup recolours the shell and the point light per item at
        /// runtime, so the shared material only has to have emission enabled.
        /// The whole hierarchy sits on Interactable so the NavMesh bake (Default layer only) ignores it.
        /// </summary>
        static void BuildItemPickup()
        {
            var root = new GameObject("ItemPickup");

            var sc = root.AddComponent<SphereCollider>();
            sc.radius = 1.2f;
            sc.isTrigger = true;
            var pickup = root.AddComponent<ItemPickup>();

            var visual = Empty("Visual", root.transform, Vector3.zero);
            pickup.visual = visual.transform;

            Material shell = Mat("M_NeonCyan");
            var outer = Prim(PrimitiveType.Cube, "Outer", visual.transform, Vector3.zero, new Vector3(0.45f, 0.45f, 0.45f), shell);
            outer.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
            var inner = Prim(PrimitiveType.Cube, "Inner", visual.transform, Vector3.zero, new Vector3(0.28f, 0.28f, 0.28f), shell);
            inner.transform.localRotation = Quaternion.Euler(0f, 45f, 45f);

            var glowGo = Empty("Glow", root.transform, Vector3.zero);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 7f;
            glow.intensity = 2.2f;
            glow.shadows = LightShadows.None;

            SetLayerRecursively(root, Layers.Interactable);
            Save(root, $"{PrefabDir}/ItemPickup.prefab");
        }

        // ------------------------------------------------------------------ misc

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.black;
        }
    }
}
