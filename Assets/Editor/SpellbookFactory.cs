using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Builds the persistent dark-fantasy spellbook viewmodel. It is deliberately separate from
    /// PrefabFactory so the later offhand integration can opt into it without rebuilding weapons,
    /// player data, or inventory. The factory owns every serialized visual number (rule 9).
    /// </summary>
    public static class SpellbookFactory
    {
        public const string PrefabPath = "Assets/Prefabs/VM_Spellbook.prefab";

        [MenuItem("VibeGame1/4c. Build Spellbook Visual")]
        public static void CreateAll()
        {
            Material core = MaterialAt("Assets/Materials/M_SpellbookLeather.mat");
            Material page = MaterialAt("Assets/Materials/M_SpellbookPage.mat");
            Material trim = MaterialAt("Assets/Materials/M_GauntletTrim.mat");
            Material energy = MaterialAt("Assets/Materials/M_Energy.mat");

            var root = new GameObject("VM_Spellbook");
            var visual = root.AddComponent<SpellbookVisual>();

            // The complete silhouette stays camera-left. None of these roots may migrate through x=0:
            // at the serialized OffhandViewmodel rest x=-0.34 the book remains outside the aiming lane.
            Transform book = Empty("BookRoot", root.transform, new Vector3(-0.105f, -0.005f, 0.018f));
            book.localRotation = Quaternion.Euler(-7f, -17f, 8f);
            visual.bookRoot = book;
            visual.castBookOffset = new Vector3(0.034f, 0.018f, 0.054f);
            visual.castBookEuler = new Vector3(-8f, 10f, -4f);

            // The hand grips the spine from below. Grip* is intentional: OffhandViewmodel's existing
            // hand solver already understands this exact naming contract.
            visual.gripAnchor = Empty("GripBook", book, new Vector3(-0.010f, -0.105f, 0.010f));
            Empty("GripPalm", visual.gripAnchor, Vector3.zero);

            // Broad opened covers: mass first, then the thin pale pages and restrained metal corners.
            Prim(PrimitiveType.Cube, "CoverLeft", book, new Vector3(-0.081f, 0f, 0.022f),
                new Vector3(0.145f, 0.205f, 0.029f), core, new Vector3(0f, -18f, 0f));
            Prim(PrimitiveType.Cube, "CoverRight", book, new Vector3(0.081f, 0f, 0.022f),
                new Vector3(0.145f, 0.205f, 0.029f), core, new Vector3(0f, 18f, 0f));
            Prim(PrimitiveType.Cube, "Spine", book, new Vector3(0f, 0f, 0.018f),
                new Vector3(0.025f, 0.216f, 0.044f), trim, Vector3.zero);

            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 0.142f;
                Prim(PrimitiveType.Cube, side < 0 ? "CornerLTop" : "CornerRTop", book,
                    new Vector3(x, 0.091f, -0.003f), new Vector3(0.024f, 0.023f, 0.016f), trim, Vector3.zero);
                Prim(PrimitiveType.Cube, side < 0 ? "CornerLBottom" : "CornerRBottom", book,
                    new Vector3(x, -0.091f, -0.003f), new Vector3(0.024f, 0.023f, 0.016f), trim, Vector3.zero);
            }
            Prim(PrimitiveType.Cube, "RuneSpine", book, new Vector3(0f, 0.010f, -0.010f),
                new Vector3(0.010f, 0.082f, 0.008f), energy, Vector3.zero);

            const int pagesPerSide = 5;
            var pagePivots = new Transform[pagesPerSide * 2];
            var pageEuler = new Vector3[pagePivots.Length];
            int pageIndex = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < pagesPerSide; i++)
                {
                    float t = i / (float)(pagesPerSide - 1);
                    Transform pivot = Empty("PagePivot_" + (side < 0 ? "L" : "R") + i, book,
                        new Vector3(side * 0.008f, Mathf.Lerp(-0.014f, 0.018f, t), -0.012f - i * 0.0025f));
                    Vector3 euler = new Vector3(0f, side * Mathf.Lerp(-4f, 13f, t), side * Mathf.Lerp(-4f, 4f, t));
                    pivot.localRotation = Quaternion.Euler(euler);
                    pagePivots[pageIndex] = pivot;
                    pageEuler[pageIndex] = euler;
                    Prim(PrimitiveType.Cube, "PageLeaf_" + pageIndex, pivot,
                        new Vector3(side * 0.065f, 0f, -0.004f), new Vector3(0.122f, 0.174f, 0.007f), page, Vector3.zero);
                    // Ink belongs only to the upper leaf. Repeating it through the stack creates z-fighting,
                    // while three broad strokes are still legible at viewmodel distance without a texture.
                    if (i == pagesPerSide - 1)
                    {
                        for (int line = 0; line < 3; line++)
                            Prim(PrimitiveType.Cube, "Ink_" + (side < 0 ? "L" : "R") + line, pivot,
                                new Vector3(side * 0.066f, 0.042f - line * 0.031f, -0.0082f),
                                new Vector3(0.071f - line * 0.008f, 0.005f, 0.0015f), core, Vector3.zero);
                    }
                    pageIndex++;
                }
            }
            visual.pagePivots = pagePivots;
            visual.pageRestEuler = pageEuler;
            visual.pageFlutterDegrees = 6.5f;
            visual.pageFlutterSpeed = 2.25f;

            // Three loose leaves give the book a living magical flow without physics, particle systems,
            // or a new renderer every frame. Their authored origins remain camera-left of the aim lane.
            const int floatingCount = 3;
            var floatPivots = new Transform[floatingCount];
            var floatOrigins = new Vector3[floatingCount];
            Vector3[] origins =
            {
                new Vector3(-0.170f, 0.095f, 0.008f),
                new Vector3(-0.205f, -0.022f, 0.018f),
                new Vector3(-0.088f, 0.144f, 0.032f),
            };
            for (int i = 0; i < floatingCount; i++)
            {
                Transform pivot = Empty("FloatingPagePivot" + i, book, origins[i]);
                Prim(PrimitiveType.Cube, "FloatingPage" + i, pivot, Vector3.zero,
                    new Vector3(0.064f, 0.091f, 0.005f), page, new Vector3(0f, 0f, i * 17f));
                floatPivots[i] = pivot;
                floatOrigins[i] = origins[i];
            }
            visual.floatingPagePivots = floatPivots;
            visual.floatingPageOrigins = floatOrigins;
            visual.floatingPageRadius = 0.017f;
            visual.floatingPageSpeed = 1.4f;

            visual.orbAnchor = Empty("OrbAnchor", book, new Vector3(-0.095f, 0.222f, 0.022f));
            visual.castOrigin = Empty("CastOrigin", visual.orbAnchor, new Vector3(0f, 0f, 0.075f));
            // One opaque core plus a broken rune ring. Two nested opaque spheres read as one flat disc;
            // the separated bars preserve negative space and make the selected spell feel inscribed.
            var orbRenderers = new List<Renderer>();
            GameObject orbCore = Prim(PrimitiveType.Sphere, "SpellOrbCore", visual.orbAnchor, Vector3.zero,
                Vector3.one * 0.082f, energy, Vector3.zero);
            orbRenderers.Add(orbCore.GetComponent<Renderer>());
            const int runeCount = 8;
            const float runeRadius = 0.071f;
            for (int i = 0; i < runeCount; i++)
            {
                float angle = i * 360f / runeCount;
                float radians = angle * Mathf.Deg2Rad;
                GameObject rune = Prim(PrimitiveType.Cube, "SpellRune" + i, visual.orbAnchor,
                    new Vector3(Mathf.Cos(radians) * runeRadius, Mathf.Sin(radians) * runeRadius, 0.003f),
                    new Vector3(0.031f, 0.006f, 0.005f), energy, new Vector3(0f, 0f, angle + 90f));
                orbRenderers.Add(rune.GetComponent<Renderer>());
            }
            visual.orbRenderers = orbRenderers.ToArray();
            visual.orbBobMetres = 0.012f;
            visual.orbBobSpeed = 2.15f;
            visual.emptyOrbColor = new Color(0.45f, 0.35f, 0.72f, 1f);

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            Object.DestroyImmediate(root);
            if (!saved) Debug.LogError("[SpellbookFactory] Failed to save " + PrefabPath);
            else Debug.Log("[SpellbookFactory] Saved " + PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static Material MaterialAt(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) Debug.LogWarning("[SpellbookFactory] Missing material " + path);
            return material;
        }

        static Transform Empty(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material, Vector3 localEuler)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            return go;
        }
    }
}
