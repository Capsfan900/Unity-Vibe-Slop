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
            BuildBalloon();
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

        /// <summary>
        /// A blade whose slices alternate side to side, so the edge reads as a wave rather than a
        /// straight line. Same Seg* contract as <see cref="StackSegments"/> — EnergyGlow's flow band
        /// still travels the stack — but the silhouette is unmistakably not a straight blade.
        /// </summary>
        static void WaveSegments(Transform parent, Material mat, int count, float baseY, float height,
                                 float width, float depth, float taper, float amplitude)
        {
            float segH = height / count;
            for (int i = 0; i < count; i++)
            {
                float k = count == 1 ? 0f : i / (float)(count - 1);
                float shrink = Mathf.Lerp(1f, taper, k);
                float x = (i % 2 == 0 ? amplitude : -amplitude) * (1f - 0.4f * k);
                Prim(PrimitiveType.Cube, $"Seg{i}", parent,
                     new Vector3(x, baseY + segH * (i + 0.5f), 0f),
                     new Vector3(width * shrink, segH * 0.94f, depth * shrink), mat);
            }
        }

        /// <summary>
        /// THE WEAPON FAMILY. Four LENGTHS again, not four daggers.
        ///
        /// <para><b>What the dagger pass got right, and what is kept.</b> Everything was shrunk to one
        /// 0.27-0.32 m band because a long blade at 95° FOV becomes a pole across the frame. But length
        /// was never the thing that broke the frame — POSE was. A long weapon held vertically at 0.6 m
        /// from the lens fills the screen; the same weapon held further out and CANTED lies diagonally
        /// across the lower-right corner and covers less of the frame than the old sword did. So the
        /// three properties the dagger set actually earned are now enforced directly, by measurement, in
        /// <see cref="WeaponSilhouette"/> and <c>WeaponSilhouetteTests</c>:
        ///   1. nothing crosses the CROSSHAIR in a held pose (idle or guard) — you can always read the
        ///      enemy you are about to parry;
        ///   2. nothing covers more than a small fraction of the FRAME;
        ///   3. the TIP stays inside the frame, so the contact point of the swing is legible.
        /// With those three nailed down, length is free again — which is the whole point.</para>
        ///
        /// <para>Extent above the fist now spans <b>0.32 m to 0.72 m</b>, a 2.3x spread where the dagger
        /// pass had 1.2x:
        ///   Cerulean Edge — SWORD. A real cruciform arming sword: 8-slice tapered blade, wide knobbed
        ///                   quillons, hand-and-a-half grip, disc pommel. 0.62 m. The generalist, and it
        ///                   finally looks like the thing every other weapon is measured against.
        ///   Sunbreaker    — MAUL. A long haft the fist grips LOW, carrying a blocky mass head, cheeks
        ///                   and a spike three quarters of a metre above the hand. 0.72 m, and the mass
        ///                   is at the far end where the commitment can be seen.
        ///   Rosethorn     — NEEDLE. UNCHANGED, to the millimetre. Thinnest section in the set, hard
        ///                   taper, barely a guard, 0.32 m. It is the reference the player already
        ///                   likes; it is the one weapon this pass does not touch.
        ///   Oathbreaker   — KRIS. The only non-straight blade, serrated with dark barbs down one edge,
        ///                   twin rings at two radii, pale violet rather than the set's greens. 0.50 m,
        ///                   deliberately between the sword and the dagger. The cheat weapon should look
        ///                   ceremonial and wrong.</para>
        ///
        /// <para>Reach is still NOT encoded here — hitOffset/hitRadius are camera-space and always were
        /// (a 0.6 m viewmodel does not reach 2.1 m). The geometry sells the reach; the data IS the
        /// reach, and the two are tuned to agree in direction, never in metres.</para>
        /// </summary>
        static void BuildWeaponViewmodels()
        {
            Material core = Mat("M_WeaponCore") != null ? Mat("M_WeaponCore") : Mat("M_Ground");
            Material energy = Mat("M_Energy") != null ? Mat("M_Energy") : Mat("M_Item");

            // Cerulean Edge — a full cruciform arming sword again. Long tapered blade, wide knobbed
            // quillons, a hand-and-a-half grip and a disc pommel: symmetric and unfussy on purpose,
            // because the generalist should look like the default every other weapon deviates from.
            // Cool steel-blue, steady breath: the dependable one.
            {
                var root = new GameObject("VM_Sword");
                StackSegments(root.transform, energy, 8, 0.055f, 1.06f, 0.058f, 0.115f, 0.42f);
                Prim(PrimitiveType.Cube, "Guard", root.transform, new Vector3(0f, 0.028f, 0f), new Vector3(0.36f, 0.05f, 0.085f), core);
                Prim(PrimitiveType.Cube, "QuillonL", root.transform, new Vector3(-0.185f, 0.045f, 0f), new Vector3(0.06f, 0.078f, 0.072f), core);
                Prim(PrimitiveType.Cube, "QuillonR", root.transform, new Vector3(0.185f, 0.045f, 0f), new Vector3(0.06f, 0.078f, 0.072f), core);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.14f, 0f), new Vector3(0.05f, 0.24f, 0.05f), core);
                Prim(PrimitiveType.Cube, "Pommel", root.transform, new Vector3(0f, -0.29f, 0f), new Vector3(0.10f, 0.06f, 0.10f), core);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 1.155f, 0f), new Vector3(0.038f, 0.12f, 0.072f), energy);
                // Ring hovering at the guard, counter-rotating slowly.
                Prim(PrimitiveType.Cube, "FloatRing", root.transform, new Vector3(0.16f, 0.09f, 0f), new Vector3(0.055f, 0.013f, 0.055f), energy);
                Energise(root, new Color(0.56f, 0.71f, 0.85f), 1.15f, 0.75f, 0.16f, 2.4f, 42f);
                var prefab = Save(root, $"{WeaponDir}/VM_Sword.prefab");
                AssignViewmodel("Assets/Data/Weapons/Sword.asset", prefab);
            }
            // Sunbreaker — a two-handed MAUL. The fist grips low on a long haft and the whole mass
            // (blocky head, cheeks, spike) rides three quarters of a metre above it, so the wind-up
            // travels visibly further than any blade's and the commitment is legible before contact.
            // Slow heavy pulse; the energy band climbs the haft and stops in the head, where the
            // weight is.
            {
                var root = new GameObject("VM_Hammer");
                StackSegments(root.transform, energy, 7, -0.30f, 1.10f, 0.062f, 0.062f);
                // A bound haft the fist can close on, LOW on the shaft — that is what makes the head
                // read as far away rather than as a big blade. Every other weapon already had a Grip*
                // part; without one here the hand would grip empty air, because WeaponViewmodel
                // positions the hand on the prefab's Grip. It is also the THICKEST grip in the set —
                // the fist visibly has to open wider for this one.
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.16f, 0f), new Vector3(0.098f, 0.30f, 0.098f), core);
                Prim(PrimitiveType.Cube, "Butt", root.transform, new Vector3(0f, -0.345f, 0f), new Vector3(0.085f, 0.05f, 0.085f), core);
                Prim(PrimitiveType.Cube, "HeadCore", root.transform, new Vector3(0f, 0.90f, 0f), new Vector3(0.28f, 0.24f, 0.22f), core);
                Prim(PrimitiveType.Cube, "HeadCheekL", root.transform, new Vector3(-0.175f, 0.90f, 0f), new Vector3(0.085f, 0.18f, 0.175f), core);
                Prim(PrimitiveType.Cube, "HeadCheekR", root.transform, new Vector3(0.175f, 0.90f, 0f), new Vector3(0.085f, 0.18f, 0.175f), core);
                Prim(PrimitiveType.Cube, "TipBand", root.transform, new Vector3(0f, 0.90f, 0f), new Vector3(0.31f, 0.05f, 0.24f), energy);
                Prim(PrimitiveType.Cube, "Spike", root.transform, new Vector3(0f, 1.09f, 0f), new Vector3(0.06f, 0.20f, 0.07f), core);
                Prim(PrimitiveType.Cube, "TipPoint", root.transform, new Vector3(0f, 1.225f, 0f), new Vector3(0.035f, 0.07f, 0.05f), energy);
                Prim(PrimitiveType.Cube, "FloatShardA", root.transform, new Vector3(0.25f, 0.98f, 0f), new Vector3(0.05f, 0.05f, 0.05f), energy);
                Prim(PrimitiveType.Cube, "FloatShardB", root.transform, new Vector3(-0.25f, 0.82f, 0f), new Vector3(0.04f, 0.04f, 0.04f), energy);
                Energise(root, new Color(0.88f, 0.40f, 0.10f), 0.7f, 0.42f, 0.24f, 2.0f, 34f);
                var prefab = Save(root, $"{WeaponDir}/VM_Hammer.prefab");
                AssignViewmodel("Assets/Data/Weapons/Hammer.asset", prefab);
            }
            // Rosethorn — the reference silhouette, changed least. Needle stiletto: thinnest section in
            // the set, hard taper to a point, a guard barely wider than the blade. Quick nervous pulse.
            {
                var root = new GameObject("VM_Dagger");
                StackSegments(root.transform, energy, 4, 0.02f, 0.44f, 0.042f, 0.070f, 0.40f);
                Prim(PrimitiveType.Cube, "Guard", root.transform, Vector3.zero, new Vector3(0.14f, 0.03f, 0.052f), core);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.10f, 0f), new Vector3(0.04f, 0.18f, 0.04f), core);
                Prim(PrimitiveType.Cube, "Pommel", root.transform, new Vector3(0f, -0.205f, 0f), new Vector3(0.052f, 0.032f, 0.052f), core);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.505f, 0f), new Vector3(0.022f, 0.075f, 0.038f), energy);
                Prim(PrimitiveType.Cube, "FloatMote", root.transform, new Vector3(0.075f, 0.20f, 0f), new Vector3(0.026f, 0.026f, 0.026f), energy);
                Energise(root, new Color(0.37f, 0.84f, 0.42f), 2.4f, 1.5f, 0.11f, 2.6f, 96f);
                var prefab = Save(root, $"{WeaponDir}/VM_Dagger.prefab");
                AssignViewmodel("Assets/Data/Weapons/Dagger.asset", prefab);
            }
            // Oathbreaker (dev) — serrated arcane kris. The only wavy blade, the only barbed edge, two
            // rings at different radii and pale violet rather than the set's greens, so it can never be
            // confused with Rosethorn. It is the cheat weapon and should look ceremonial and wrong.
            {
                var root = new GameObject("VM_DevBlade");
                WaveSegments(root.transform, energy, 8, 0.04f, 0.82f, 0.062f, 0.10f, 0.50f, 0.030f);
                for (int i = 0; i < 5; i++)
                    Prim(PrimitiveType.Cube, $"Barb{i}", root.transform,
                         new Vector3(0.048f, 0.11f + 0.15f * i, 0f), new Vector3(0.058f, 0.030f, 0.058f), core);
                Prim(PrimitiveType.Cube, "Guard", root.transform, Vector3.zero, new Vector3(0.24f, 0.044f, 0.07f), core);
                Prim(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, -0.13f, 0f), new Vector3(0.052f, 0.22f, 0.052f), core);
                Prim(PrimitiveType.Cube, "Pommel", root.transform, new Vector3(0f, -0.27f, 0f), new Vector3(0.075f, 0.048f, 0.075f), core);
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.905f, 0f), new Vector3(0.034f, 0.10f, 0.058f), energy);
                Prim(PrimitiveType.Cube, "FloatRingInner", root.transform, new Vector3(0.11f, 0.24f, 0f), new Vector3(0.045f, 0.012f, 0.045f), energy);
                Prim(PrimitiveType.Cube, "FloatRingOuter", root.transform, new Vector3(0.19f, 0.62f, 0f), new Vector3(0.055f, 0.014f, 0.055f), energy);
                Energise(root, new Color(0.78f, 0.62f, 1f), 1.8f, 1.25f, 0.13f, 3.0f, 70f);
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

            // Grapple — a short shaft with a curved tine off the top, neon cyan. A HOOK at a glance:
            // the one silhouette in the hand that is not symmetric about its axis.
            {
                var root = new GameObject("VM_Item_Grapple");
                // Shaft: three stacked segments so the flow band climbs toward the tine (the charge
                // runs OUT to the point that bites).
                for (int i = 0; i < 3; i++)
                    Prim(PrimitiveType.Cube, $"Seg{i}", root.transform, new Vector3(0f, 0.04f + i * 0.07f, 0f), new Vector3(0.024f, 0.066f, 0.024f), energy);
                Prim(PrimitiveType.Cube, "Ferrule", root.transform, new Vector3(0f, 0.005f, 0f), new Vector3(0.04f, 0.018f, 0.04f), core);
                // Tine: five short bars stepping around a quarter circle from the shaft top, forward
                // and over, thinning toward the point. Rotated so each reads as a chord of the curve.
                for (int i = 0; i < 5; i++)
                {
                    float a = (i + 0.5f) / 5f * 100f;                     // degrees around the bend
                    float rad = 0.075f;
                    float ar = a * Mathf.Deg2Rad;
                    Vector3 p = new Vector3(0f, 0.245f + Mathf.Sin(ar) * rad, (1f - Mathf.Cos(ar)) * rad);
                    float w = Mathf.Lerp(0.022f, 0.012f, i / 4f);
                    var seg = Prim(PrimitiveType.Cube, $"Tine{i}", root.transform, p, new Vector3(w, 0.036f, w), core);
                    seg.transform.localRotation = Quaternion.Euler(a, 0f, 0f);
                }
                // The point, named Tip so the tip light and the beam originate where the hook bites.
                var tip = Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.245f + 0.075f * 0.985f + 0.02f, 0.075f * 1.17f), new Vector3(0.028f, 0.045f, 0.028f), energy);
                tip.transform.localRotation = Quaternion.Euler(110f, 0f, 0f);
                Prim(PrimitiveType.Cube, "FloatMote", root.transform, new Vector3(0.05f, 0.15f, -0.03f), new Vector3(0.018f, 0.018f, 0.018f), energy);
                // Brisk upward flow: the charge climbing the shaft to the point is the "it wants to go" tell.
                Energise(root, Color.white, 1.6f, 2.2f, 0.12f, 2.6f, 70f);
                AssignItem("Grapple", Save(root, $"{ItemDir}/VM_Item_Grapple.prefab"));
            }
            // Wall Surge — a small blade-fan on a hub that spins, neon yellow. Speed, held in the hand.
            {
                var root = new GameObject("VM_Item_WallSurge");
                Prim(PrimitiveType.Cube, "Stem", root.transform, new Vector3(0f, 0.06f, 0f), new Vector3(0.02f, 0.12f, 0.02f), core);
                // Hub: the bright centre the blades spin about, named Tip so the tip light sits on it.
                Prim(PrimitiveType.Cube, "Tip", root.transform, new Vector3(0f, 0.16f, 0f), new Vector3(0.045f, 0.045f, 0.045f), energy);
                // Four swept blades around Y as Seg* parts so the flow band chases around the fan.
                // Parented under a Float* node sitting ON the axis: EnergyGlow orbits Float* parts at
                // their authored radius (zero here, so it stays put) and counter-rotates them about Y,
                // which is exactly a spinning fan for free - see EnergyGlow.AnimateFloats.
                var spinner = new GameObject("FloatFan");
                spinner.transform.SetParent(root.transform, false);
                spinner.transform.localPosition = new Vector3(0f, 0.16f, 0f);
                for (int i = 0; i < 4; i++)
                {
                    var blade = Prim(PrimitiveType.Cube, $"Seg{i}", spinner.transform, Vector3.zero, new Vector3(0.11f, 0.008f, 0.03f), energy);
                    // Offset along the blade's own X so it sticks out from the hub, then rake it.
                    blade.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f) * Quaternion.Euler(0f, 0f, 14f);
                    blade.transform.localPosition = blade.transform.localRotation * new Vector3(0.07f, 0f, 0f);
                }
                Prim(PrimitiveType.Cube, "FloatSparkA", root.transform, new Vector3(0.07f, 0.22f, 0f), new Vector3(0.016f, 0.016f, 0.016f), energy);
                Prim(PrimitiveType.Cube, "FloatSparkB", root.transform, new Vector3(-0.06f, 0.11f, 0.02f), new Vector3(0.014f, 0.014f, 0.014f), energy);
                // Fast, tight: the fan is spinning before you have used it.
                Energise(root, Color.white, 2.8f, 3.0f, 0.10f, 2.4f, 140f);
                AssignItem("WallSurge", Save(root, $"{ItemDir}/VM_Item_WallSurge.prefab"));
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

            var motor = root.AddComponent<FirstPersonMotor>();
            // A CODE DEFAULT IS NOT A SHIPPED VALUE (hard rule 9). Every wall-run number is written here
            // so a rebuild is authoritative and editing the field initialisers in FirstPersonMotor can
            // never silently diverge from the prefab LevelArcAnalyzer reads its constants off.
            // WallRunTunablesTests asserts this list against the shipped asset.
            // 2026-09-03 retune after play: "the wall run only works sometimes". Entry is now judged on
            // total speed (6 m/s, a jog), 53 deg off the face instead of 33, any look that is not
            // backwards, and no coyote wait. The wall is FASTER than the floor (13.75 top) and the run
            // is bounded by stamina rather than a per-airtime count. See ENGINEERING-LOG.
            motor.wallRunMinEntrySpeed = 6f;
            motor.wallRunMaxEntryFallSpeed = 9f;
            motor.wallRunMaxApproachCos = 0.80f;
            motor.wallRunMinLookAlongCos = -0.05f;
            motor.wallRunMaxDuration = 1.75f;
            motor.wallRunGravityStartScale = 0.10f;
            motor.wallRunGravityEndScale = 0.60f;
            motor.wallRunEntryUpSpeed = 3f;
            motor.wallRunSpeedDecay = 0.35f;   // window is (ln(6/4), ln(11/4))/1.75 = (0.23, 0.58); a 6 m/s entry bleeds out at 1.16 s; see WallRunTunablesTests
            motor.wallRunMinSustainSpeed = 4f;
            motor.wallRunAccel = 14f;
            motor.wallRunTopSpeed = 13.75f;
            motor.wallRunStickSpeed = 2.5f;
            motor.wallRunExitUpSpeed = 10f;
            motor.wallRunExitPushSpeed = 7f;
            motor.wallRunExitTangentBoost = 4f;
            motor.maxWallRuns = 6;
            motor.wallRunCooldown = 0.20f;
            motor.wallRunLostGrace = 0.15f;
            motor.wallRunExitGrace = 0.15f;
            motor.wallRunCameraRoll = 13f;
            motor.wallRunExitRollKick = 7f;
            // Momentum: a soft cap with exponential drag on the excess, never a flat ceiling.
            motor.airSoftCap = 17.6f;
            motor.airDrag = 3f;
            motor.groundOverspeedDecay = 4f;
            motor.maxHorizontalSpeed = 27.5f;
            motor.slideChainWindow = 1.2f;
            motor.slideChainFalloff = 0.6f;
            // Weight and air control (2026-09-03 evening, after play: "I can still just shoot off a wall or
            // ledge", "more control in the air like CS:GO surfing", "you need to be able to slide-jump").
            // Fall heavier than you rise, bleed the carry, pay for hard landings, steer where you fly,
            // and press the controller into the floor hard enough that isGrounded stops flickering at
            // 500 fps (the slide-jump was dying at coyote time). Asserted by AirFeelTests.
            motor.fallGravityMultiplier = 1.5f;
            motor.airCarryDecay = 0.8f;
            motor.airSteerDegPerSec = 120f;
            motor.landingSoftSpeed = 16f;
            motor.landingHardSpeed = 26f;
            motor.landingSpeedLoss = 0.35f;
            motor.groundSnapDistance = 0.12f;
            // The pivot's traversal pieces (2026-09-04). Water: a skating floor 1.35x a sprint, turned at a
            // third of groundAccel; the grapple burst: a free 0.30 s dash at 1.25x after a pull ARRIVES
            // and control is back. Asserted by PivotMovementTests.
            motor.waterSpeedScale = 1.35f;
            motor.waterAccel = 30f;
            motor.waterGrace = 0.15f;
            motor.pullBurstWindow = 0.30f;
            motor.pullBurstMultiplier = 1.25f;
            motor.pullBurstHold = 3f;
            // The balloon FLOAT (2026-09-05, from play: "slightly slower and controlled"). For 0.45 s
            // after a launch gravity is scaled to 0.55 and air steer turns 1.6x faster, so the pop
            // hangs long enough to aim the re-armed dash at the next orb. Zero-gravity would read as a
            // glitch; 0.55 reads as a lift.
            motor.launchFloatSeconds = 0.45f;
            motor.launchGravityScale = 0.55f;
            motor.launchSteerBoost = 1.6f;
            motor.launchCarryCap = 9f;   // pop → aim → dash: the carry is steerable, the dash is the reach
            // Perfect timing (rule 9; PerfectTimingTests reads these back). Windows of 0.12-0.14 s around
            // a physical moment -- see PerfectMath for why that band and not frame-perfect or free.
            motor.perfectWallJumpWindow = 0.14f;
            motor.perfectWallJumpRefund = 20f;
            motor.perfectDashJumpMinDelay = 0.04f;
            motor.perfectDashJumpWindow = 0.12f;
            motor.perfectDashJumpRefund = 30f;
            motor.perfectBurstWindow = 0.12f;
            motor.perfectBurstBonus = 30f;
            // Forgiveness (MOVEMENT-PRINCIPLES rule 4). Bounded, intent-honouring, adds no reach.
            motor.cornerCorrectionMetres = 0.18f;
            motor.ledgeCatchMetres = 0.22f;
            motor.ledgeCatchLiftSpeed = 6f;
            motor.ledgeCatchMinSpeed = 1.5f;

            var look = root.AddComponent<PlayerLook>();
            look.rollBiasLerp = 9f;
            root.AddComponent<Health>();
            var stats = root.AddComponent<PlayerStats>();
            stats.data = Load<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            root.AddComponent<PlayerResources>();
            // The movement budget. Rule 9: written here, asserted by StaminaTunablesTests.
            var stamina = root.AddComponent<PlayerStamina>();
            stamina.max = 100f;
            stamina.regenPerSecondGrounded = 45f;
            stamina.regenPerSecondAirborne = 18f;
            stamina.regenDelay = 0.45f;
            stamina.dashCost = 30f;
            stamina.wallRunEntryCost = 12f;
            stamina.wallRunDrainPerSecond = 22f;
            stamina.wallJumpCost = 12f;
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
            // The sentry dash (2026-09-06): DASH at a staggered span shooter pulls you to it and executes.
            // Rule 9: written here, not left to the initialiser.
            var sentryDash = root.AddComponent<SentryDash>();
            sentryDash.range = 30f; sentryDash.coneDeg = 18f; sentryDash.pullSeconds = 0.35f;
            sentryDash.hue = new Color(0.85f, 0.75f, 1f);

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
            // Full extension, aimed AT THE MARK. The victim is turned to face the player and the
            // step-in parks it dead centre of the frame, so the sternum glyph projects on (or just
            // under) the crosshair — the stab therefore has to finish near screen centre, not off to
            // the left where the wand rests. Pushed out to 0.80 as well: the wand is a viewmodel prop
            // and can never physically reach 2.2 m, so the stab is sold by the tip visually LANDING on
            // the mark, and that only happens if it travels toward the centre of the frame.
            offhand.thrustPosition = new Vector3(-0.07f, -0.09f, 0.80f);
            offhand.thrustEuler = new Vector3(66f, -6f, 4f);
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

            // The swing trail. Rule 9: every value is written here, because Player.prefab keeps whatever
            // was serialised the day it was built and a field initialiser on WeaponTrail would never
            // reach it. Numbers and their reasoning: Assets/Scripts/Feel/WeaponTrail.cs.
            var trail = vmRoot.AddComponent<WeaponTrail>();
            trail.maxPoints = 12;
            trail.subdivisions = 3;
            trail.headWidth = 0.030f;
            trail.tailFraction = 0.03f;
            trail.fadeSeconds = 0.11f;
            trail.brightness = 1.15f;   // over the 1.05 bloom threshold, under the ~1.25 ACES ceiling

            look.pivot = pivot.transform;
            look.cam = camGo.transform;

            // The body: hips, chest and legs under the ROOT, so looking down shows them and a slide
            // throws the boots out in front of the lens. See PlayerBody.
            BuildPlayerBody(root, glove, gloveTrim);

            BuildLockOn(root);

            Save(root, $"{PrefabDir}/Player.prefab");
        }

        /// <summary>
        /// The player's body — hips, chest, two three-segment legs — as primitives under the Player ROOT
        /// (never the camera: the root yaws with the look and the pivot pitches, so a body here turns
        /// with you and stays level when you look down at it). Built for two things: looking down and
        /// seeing legs walking, and the SLIDE, whose whole read is the boots out in front of the lens.
        ///
        /// <para>Geometry rules. Nothing above y 1.35 — the lens is at 1.60 with a 0.03 near clip, and at
        /// −89° pitch anything higher sits inside the camera; the chest tops out at 1.34. Everything
        /// inside the 0.40 m capsule radius so no pose pokes through a wall the collider is touching.
        /// Hip joints at 0.92, thigh 0.46, shin 0.44: the boot's sole lands on y 0.00. No colliders.
        /// Layer Player. Casts shadows (the backlog's "shadow proxy": the player used to cast none) but
        /// receives none, like the arms, so a floor shadow cannot paint the boots black.</para>
        ///
        /// <para>Rule 9: every number on <see cref="PlayerBody"/> is written here. The slide pose numbers
        /// are chosen against the FRAME — eye at 1.05 m during a slide, 47.5° half-FOV — and
        /// <c>SlideFeelTests</c> checks the leading boot lands inside it.</para>
        /// </summary>
        static void BuildPlayerBody(GameObject root, Material glove, Material trim)
        {
            const float hipY = 0.92f, thigh = 0.46f, shin = 0.44f;

            var bodyGo = Empty("Body", root.transform, Vector3.zero);
            var body = bodyGo.AddComponent<PlayerBody>();

            // Torso pivot AT the hip joint height, so the slide's lean-back rotates hips and chest
            // about the hips rather than about the floor.
            var torso = Empty("Torso", bodyGo.transform, new Vector3(0f, hipY, 0f));
            Prim(PrimitiveType.Cube, "Hips", torso.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.40f, 0.20f, 0.26f), glove);
            Prim(PrimitiveType.Cube, "Chest", torso.transform, new Vector3(0f, 0.29f, 0.01f), new Vector3(0.44f, 0.26f, 0.28f), trim);   // top 1.34

            Transform legL, legR, kneeL, kneeR;
            BuildLeg(bodyGo.transform, "Leg_L", -0.13f, hipY, thigh, shin, glove, trim, out legL, out kneeL);
            BuildLeg(bodyGo.transform, "Leg_R", 0.13f, hipY, thigh, shin, glove, trim, out legR, out kneeR);

            foreach (var r in bodyGo.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = false;
            }
            // The TORSO is a shadow proxy only. From play (2026-09-04): a chest block 0.26 m under a
            // 1.6 m lens fills the bottom of the frame the moment you look down, and during a slide
            // (eye at 1.05 m, hips pushed 0.25 m forward and leaning back) it sat straight in front of
            // the legs you are meant to see. Apex and Titanfall draw no first-person torso either: the
            // legs are the body awareness, the torso only has to cast a shadow.
            foreach (var r in torso.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            SetLayerRecursively(bodyGo, Layers.Player);

            body.torso = torso.transform;
            body.legL = legL; body.legR = legR;
            body.kneeL = kneeL; body.kneeR = kneeR;
            body.thighLength = thigh;
            body.shinLength = shin;
            body.hipHeight = hipY;

            // Gait: ±30° at groundSpeed, phase per metre matching the viewmodel bob (1.3) so hands and
            // feet stride together; a 2 cm hip bob at the mid-stride.
            body.gaitSwingDegrees = 30f;
            body.gaitKneeDegrees = 42f;
            body.gaitFullSpeed = 11f;
            body.gaitPhasePerMetre = 1.3f;
            body.gaitHipBob = 0.02f;
            // Air: a slight tuck, boots trailing.
            body.airHipDegrees = 14f;
            body.airKneeDegrees = 40f;
            // SLIDE. Chosen against the frame: eye at 1.05 m (1.60 − PlayerFeedback.slideCameraDrop
            // 0.55), vertical half-FOV 47.5°. Hips sink 0.52 (joint at 0.40) and lead the head by
            // 0.25 m; the leading leg at 70°/12° puts its ankle ~0.83 m ahead and 0.02 m up, so with the
            // forward shift the boot sits at z ≈ 1.15 — 41° below the horizon, in frame — while the
            // knee at z ≈ 0.68 / y 0.24 sits right on the frame's bottom edge. The trailing leg is 10°
            // tighter so the pair reads as two legs and not a slab.
            body.slideHipSink = 0.52f;
            body.slideHipForward = 0.25f;
            body.slideTorsoLean = 22f;
            body.slideLeadHip = 70f;
            body.slideLeadKnee = 12f;
            body.slideTrailHip = 60f;
            body.slideTrailKnee = 26f;
            // The throw: 6 Hz / ζ 0.6 reaches the pose in ~0.10 s and overshoots ~9% (SlideImpulse.
            // OvershootFraction) before settling — thrown, not placed. Capped at 1.15 of the pose.
            body.slideBlendHz = 6f;
            body.slideBlendDamping = 0.6f;
            body.slideBlendMax = 1.15f;
            // Wall run: boots 14° into the face, torso 6° off it.
            body.wallRunLegLean = 14f;
            body.wallRunTorsoLean = 6f;
            // Landing: up to 34° of knee at a 22 m/s landing, springing back at the camera dip's 9/s.
            body.landingKneeDegrees = 34f;
            body.landingFullSpeed = 22f;
            body.landingRecoverySpeed = 9f;
            body.standUpKneeDegrees = 12f;
            body.poseLerp = 14f;
        }

        /// <summary>One leg: hip pivot → thigh, knee pivot → shin + boot. Names are stable (tests and
        /// captures find them by name).</summary>
        static void BuildLeg(Transform parent, string name, float x, float hipY, float thigh, float shin,
                             Material glove, Material trim, out Transform hip, out Transform knee)
        {
            var leg = Empty(name, parent, new Vector3(x, hipY, 0f));
            Prim(PrimitiveType.Cube, "Thigh", leg.transform, new Vector3(0f, -thigh * 0.5f, 0f), new Vector3(0.15f, thigh, 0.17f), trim);
            var kneeGo = Empty("Knee", leg.transform, new Vector3(0f, -thigh, 0f));
            Prim(PrimitiveType.Cube, "Shin", kneeGo.transform, new Vector3(0f, -shin * 0.5f, 0f), new Vector3(0.12f, shin, 0.14f), trim);
            // Boot centre 0.05 above the ankle's floor line: sole on y 0.00 in the rest pose.
            Prim(PrimitiveType.Cube, "Boot", kneeGo.transform, new Vector3(0f, -shin + 0.03f, 0.05f), new Vector3(0.14f, 0.10f, 0.28f), glove);
            hip = leg.transform;
            knee = kneeGo.transform;
        }

        /// <summary>
        /// The Dark Souls lock-on dot, plus the controller that drives it. CLAUDE.md rule 9: every
        /// number here is written explicitly, because the Player prefab keeps whatever was serialised
        /// the day it was built and a field initialiser on <see cref="LockOnController"/> would never
        /// reach it.
        ///
        /// <para>ONE dot, owned by the PLAYER, not one per enemy. The alert cube and the deathblow glyph
        /// are facts about an enemy so they are children of the enemy; "this is my target" is a fact
        /// about the player and at most one thing is ever true, so a single mote travels. It therefore
        /// also works on the three <c>Legendary_*</c> mini-bosses that a different factory builds,
        /// with no prefab change of theirs.</para>
        ///
        /// <para>A bare sphere on the target's CHEST at 1.05 m. It does not spin, bob or breathe. The
        /// head is already occupied by two loud markers that mean danger and opportunity; the lock dot
        /// means neither, so it takes the one unoccupied place on the body and the one unoccupied
        /// brightness band - <c>M_LockOnDot</c> peaks at 0.78, under the 1.05 bloom threshold, and is
        /// the only combat marker in the game that never blooms.</para>
        /// </summary>
        static void BuildLockOn(GameObject root)
        {
            var dot = Empty("LockOnDot", root.transform, Vector3.zero);
            var marker = dot.AddComponent<LockOnMarker>();

            var core = Prim(PrimitiveType.Sphere, "DotCore", dot.transform, Vector3.zero, Vector3.one, Mat("M_LockOnDot"));
            var rend = core.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }

            marker.renderers = new[] { rend };
            // Constant ANGULAR size: 0.0125 world units per metre is ~0.7 degrees across, which at the
            // 3-4 m preferred fighting range is a ~4 cm mote. A fixed-size dot is a boulder in your face
            // at 3 m and gone at 25 m, which is precisely the range band a lock has to survive.
            marker.angularSize = 0.0125f;
            marker.minScale = 0.045f;
            marker.maxScale = 0.42f;
            // The dot marks a point INSIDE a 0.45 m capsule. 0.75 m toward the eye clears the body
            // with margin at every angle; the fraction cap stops it landing in the player's face when
            // an enemy is on top of them.
            marker.frontOffset = 0.75f;
            marker.frontOffsetMaxFraction = 0.3f;
            marker.acquireEase = 0.14f;
            marker.acquirePop = 2.2f;

            var lockOn = root.AddComponent<LockOnController>();
            lockOn.marker = marker;
            lockOn.markerHeight = 1.05f;
            lockOn.acquireRange = 26f;
            lockOn.dropRange = 32f;
            lockOn.acquireConeDeg = 55f;
            lockOn.switchAngleDeg = 14f;
            lockOn.breakAngleDeg = 62f;
            lockOn.occlusionGrace = 0.7f;
            lockOn.distanceWeightDegPerMetre = 0.35f;
            // Assist: proportional, capped, and gated to zero the instant the mouse moves. 4 deg/s per
            // degree of error with a 90 deg/s ceiling recentres a 10-degree drift in about a quarter of
            // a second - fast enough to keep a strafed target framed, slow enough that it can never
            // out-run a hand on the mouse. Raising the ceiling past ~120 makes it read as a snap.
            lockOn.assistGain = 4f;
            lockOn.assistMaxRateDeg = 90f;
            lockOn.assistDeadzoneDeg = 2.2f;
            lockOn.assistFadeDeg = 8f;
            lockOn.yieldMouseMin = 0.6f;
            lockOn.yieldMouseMax = 5f;
            lockOn.yieldStickMin = 0.12f;
            lockOn.yieldStickMax = 0.5f;
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
                // Ranged presence on a span. Carries no numbers of its own -- everything is read off
                // EnemyData, and an enemy whose data does not shoot never fires. See Projectile.cs.
                root.AddComponent<ProjectileShooter>();
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
            visuals.deathblowMarker = BuildDeathblowMarker(visual.transform);
            flash.renderers = new[] { visuals.body, visuals.weapon };

            // ---- posture bar: small enemies only; the boss has the HUD bar -------------------------
            if (!isBoss) BuildPostureBar(visual.transform);

            Save(root, $"{PrefabDir}/{name}.prefab");
        }

        /// <summary>
        /// The deathblow glyph: an arc-violet diamond crossed by a bar, spinning and breathing over the
        /// head of an enemy whose posture is broken. CLAUDE.md rule 9 — the geometry, the material and
        /// the animation constants are all written here, because a field initialiser on
        /// <see cref="DeathblowMarker"/> would never reach a prefab that already exists.
        ///
        /// <para>Three things separate it from the Alert cube 20 cm above it, and they are deliberate,
        /// not decoration: a different HUE (violet, not hot pink-white), a different SILHOUETTE (a
        /// crossed diamond, not an upright cube) and MOTION (it spins; the alert is dead still). The two
        /// markers hang in the same place and mean opposite things — "kill this one" against "you cannot
        /// block what is coming" — so one axis of separation would not have been enough.</para>
        ///
        /// <para><b>It sits ON THE TORSO, not over the head.</b> It used to ride at 2.30, above the body
        /// capsule, which on a 2.2x-scale boss is five metres in the air — out of the frame entirely at
        /// deathblow range, the one moment it has to be readable, and a riposte whose explosion blooms
        /// over the victim's head rather than out of its chest. <see cref="DeathblowMarker"/> pushes the
        /// point off the centre line toward the eye every frame, because anything drawn at a body's
        /// centre of mass renders inside the mesh and is never seen (ENGINEERING-LOG: the lock-on dot).
        /// The same point is what <c>ExecuteInteractor.CommitCue</c> shatters and what
        /// <c>WandController.FireRiposte</c> blooms the blast from.</para>
        ///
        /// <para>Which puts it on the same torso as the lock-on dot, so they are separated on five axes:
        /// height (sternum 1.45 vs centre of mass 1.05), size (~5% of the frame vs ~1%), hue and
        /// level (violet at peak 2.60, blooms hard, vs pale bone-grey under the 1.05 threshold, never
        /// blooms), silhouette (a rolled diamond vs a plain round pip) and motion (rolls and breathes vs
        /// dead still). Both are held to a constant ANGULAR size, so neither can grow into the other as
        /// the player closes.</para>
        ///
        /// <para><b>It cannot foul the stab</b>: <c>BeginExecuted</c> drops it on the press frame, before
        /// the melee commit and long before the wand reaches the body.</para>
        ///
        /// <para>Starts inactive; <see cref="EnemyVisuals.SetDeathblowReady"/> owns it from there.</para>
        /// </summary>
        internal static GameObject BuildDeathblowMarker(Transform visual)
        {
            return BuildDeathblowMarker(visual, 1.45f, 0.58f);
        }

        /// <summary>
        /// <paramref name="bodyHeight"/> is the sternum in the visual root's local (pre-scale) units and
        /// <paramref name="surfaceOffset"/> is how far in front of the centre line the glyph has to stand
        /// to clear THIS silhouette. Both are parameters because two of the legendaries are imported
        /// meshes — a hovering robed wraith and a squat wide-armed robot — whose chests are neither the
        /// same height nor the same depth as a 0.45 m capsule. Rule 9: written here, never defaulted.
        /// </summary>
        internal static GameObject BuildDeathblowMarker(Transform visual, float bodyHeight, float surfaceOffset)
        {
            var mat = Mat("M_DeathblowMark");
            var root = Empty("Deathblow", visual, new Vector3(0f, bodyHeight, 0f));

            // ONE SMALL FLAT QUAD. It used to be a crossed diamond built from three cubes over the head:
            // an object in the world rather than a mark on a body, and at deathblow range a solid violet
            // mass the camera runs into as it closes. A single quad has no volume to run into, and
            // DeathblowMarker billboards it so it is always square to the eye — a glowing SPOT sitting on
            // the enemy, which is what the reference actually is.
            //
            // The quad is authored at 1.0 and DeathblowMarker drives the root's scale to a CONSTANT
            // ANGULAR size every frame. A fixed size does not work here: the spot stands off the chest
            // toward the viewer (it has to, or it renders inside the mesh), so it is always nearer than
            // the body it marks and grows faster than the body does as the player closes — at 0.22 m
            // fixed it filled a quarter of the frame at stabbing range while the grunt filled a fifth.
            //
            // Rolled 45 degrees so the spot is a diamond, which is the silhouette separation against the
            // round lock-on dot on the same torso — and because DeathblowMarker rolls the billboard about
            // the VIEW axis, the diamond's corners make that roll visible where a circle would hide it.
            var core = Prim(PrimitiveType.Quad, "MarkSpot", root.transform, Vector3.zero, Vector3.one, mat);
            core.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            var mark = root.AddComponent<DeathblowMarker>();
            mark.drawMark = true;
            mark.angularSize = 0.115f;   // ~5% of the frame at 95 deg FOV, at any distance
            mark.minScale = 0.10f;
            mark.maxScale = 0.42f;
            mark.sentryMaxScale = 1.1f;   // P4: a sentry's open state reads at 25 m (~2.5 deg); EnemyVisuals.Setup flips `sentry`
            mark.frontOffsetMaxFraction = 0.35f;
            mark.spinSpeed = 110f;
            mark.bobAmount = 0.05f;
            mark.bobSpeed = 3.2f;
            mark.pulseAmount = 0.16f;
            mark.pulseSpeed = 5.5f;
            mark.bodyHeight = bodyHeight;
            mark.surfaceOffset = surfaceOffset;

            root.SetActive(false);
            return root;
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

        // ------------------------------------------------------------------ H. balloon

        /// <summary>
        /// The floating orb of the pivot: touch it and you are launched, dash through it and the dash is
        /// re-armed. A soft gold sphere with a brighter core, both on M_Balloon, which sits UNDER the
        /// 1.05 bloom threshold on purpose — it is a traversal marker, not a combat light; the pop's
        /// sparks are the only bright thing about it and SlashFx caps those at 1.0.
        /// Every number on the component is written here (rule 9); LevelDefinitionBuilder and the
        /// sandbox rewrite launchSpeed / respawnSeconds / radius per placement.
        /// </summary>
        static void BuildBalloon()
        {
            var root = new GameObject("Balloon");
            var sc = root.AddComponent<SphereCollider>();
            // 1.1 m: from play (2026-09-05) the 0.6 m orbs were "too small" — at 20 m/s a 1.2 m
            // target is a coin flip. The visual below is authored at this radius; LevelPieceFactory
            // scales it by (def.radius / 1.1).
            sc.radius = 1.1f;
            sc.isTrigger = true;
            var b = root.AddComponent<Balloon>();
            // 11 m/s: a 2.0 m rise (v²/2g) instead of 14's 3.3 m. Slower and CONTROLLED: the pop is a
            // hop you steer out of (FirstPersonMotor.launchFloatSeconds), not a punt, so a chain of
            // orbs is dashed one to the next rather than ridden straight up.
            b.launchSpeed = 11f;
            b.respawnSeconds = 2.5f;
            b.radius = 1.1f;
            b.bobHeight = 0.15f;
            b.bobHz = 0.8f;
            b.popColor = new Color(1f, 0.76f, 0.29f, 1f);
            b.respawnScaleSeconds = 0.25f;

            var visual = Empty("Visual", root.transform, Vector3.zero);
            b.visual = visual.transform;
            Material orb = Mat("M_Balloon");
            var shell = Prim(PrimitiveType.Sphere, "Shell", visual.transform, Vector3.zero, Vector3.one * 2.2f, orb);
            var core = Prim(PrimitiveType.Sphere, "Core", visual.transform, Vector3.zero, Vector3.one * 0.9f, orb);
            foreach (var r in visual.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            SetLayerRecursively(root, Layers.Interactable);
            Save(root, $"{PrefabDir}/Balloon.prefab");
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
