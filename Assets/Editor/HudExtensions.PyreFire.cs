using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// The PYRE meter as a burning loading bar. Runs from <see cref="HudExtensions.ApplyAll"/> after
    /// <c>HudBuilder</c> has laid the HUD out: finds <c>PyreBar</c> by name, darkens its base fill to
    /// an ember, and adds a full-width <c>Flames</c> image above it that carries the
    /// <c>VibeGame1/UI/FireBar</c> material and a <see cref="FireBarView"/>. Rule 9: every number the
    /// component and the material ship with is written here.
    /// </summary>
    public static partial class HudExtensions
    {
        const string FireShaderName = "VibeGame1/UI/FireBar";
        const string FireMaterialPath = "Assets/Materials/UI/M_PyreFire.mat";

        /// <summary>Flame headroom above the bar, as a multiple of the bar's height. 0.9 keeps the
        /// tongues under the PYRE label 14 px above the bar (the label starts 2 px over the bar's top).</summary>
        const float FlameHeadroom = 0.9f;

        static void ApplyPyreFire(GameObject hudRoot)
        {
            var barT = FindDeep(hudRoot.transform, "PyreBar");
            if (barT == null)
            {
                Debug.LogWarning("[HudExtensions] No 'PyreBar' on the HUD; the Pyre fire was not applied.");
                return;
            }
            var bar = barT.GetComponent<BarView>();
            if (bar == null)
            {
                Debug.LogWarning("[HudExtensions] 'PyreBar' has no BarView; the Pyre fire was not applied.");
                return;
            }

            var shader = Shader.Find(FireShaderName);
            if (shader == null)
            {
                Debug.LogError("[HudExtensions] Shader '" + FireShaderName + "' not found. Is " +
                               "Assets/Shaders/UI/FireBar.shader present and compiling? The Pyre bar keeps its plain fill.");
                return;
            }

            // The shared material asset the prefab references; FireBarView clones it at runtime.
            var mat = AssetDatabase.LoadAssetAtPath<Material>(FireMaterialPath);
            if (mat == null)
            {
                DataFactory.EnsureFolder("Assets/Materials/UI");
                mat = new Material(shader) { name = "M_PyreFire" };
                AssetDatabase.CreateAsset(mat, FireMaterialPath);
            }
            mat.shader = shader;

            // Geometry: the bar's own rect plus the headroom. Read off the RectTransform's sizeDelta,
            // which is what HudBuilder.Rect wrote (point anchors, so the rect IS the sizeDelta).
            var barRt = barT as RectTransform;
            float barH = barRt != null && barRt.sizeDelta.y > 0.5f ? barRt.sizeDelta.y : 12f;
            float barW = barRt != null && barRt.sizeDelta.x > 0.5f ? barRt.sizeDelta.x : 420f;
            float headroom = Mathf.Round(barH * FlameHeadroom);
            float quadH = barH + headroom;

            mat.SetFloat("_BarTop", barH / quadH);
            mat.SetFloat("_Aspect", barW / quadH);
            mat.SetColor("_Ember", new Color(0.55f, 0.10f, 0.02f, 1f));
            mat.SetColor("_Flame", new Color(1.00f, 0.45f, 0.08f, 1f));
            mat.SetColor("_Core", new Color(1.00f, 0.92f, 0.70f, 1f));   // every channel ≤ 1.0: no bloom
            mat.SetFloat("_Fill", 0f);
            mat.SetFloat("_Heat", 0f);
            mat.SetFloat("_Kick", 0f);
            mat.SetFloat("_Full", 0f);
            EditorUtility.SetDirty(mat);

            // The base fill under the fire: a dark ember, and no white pulse when full — the fire's
            // roaring band is the ready state now.
            bar.fillColor = new Color(0.30f, 0.07f, 0.02f, 1f);
            if (bar.fill != null) bar.fill.color = bar.fillColor;
            bar.pulseWhenFull = false;

            // The Flames quad: full bar width, the bar's height plus the headroom above it, rendered
            // last so it sits over the fill and the ghost.
            var existing = barT.Find("Flames");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            var go = new GameObject("Flames", typeof(RectTransform));
            go.transform.SetParent(barT, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, headroom);
            go.transform.SetAsLastSibling();

            var img = go.AddComponent<Image>();
            img.sprite = null;
            img.color = Color.white;
            img.material = mat;
            img.raycastTarget = false;

            var fire = barT.GetComponent<FireBarView>();
            if (fire == null) fire = barT.gameObject.AddComponent<FireBarView>();
            fire.flames = img;
            fire.heatCurve = 0.8f;
            fire.kickOnGain = 1f;
            fire.kickDecay = 4f;
            fire.gainThreshold = 0.005f;
            fire.fullPulseHz = 1.4f;
            fire.fullPulseAmount = 0.15f;

            Debug.Log("[HudExtensions] Pyre fire applied: " + FireMaterialPath + ", bar " + barW + "x" + barH +
                      " + " + headroom + " px of flame headroom.");
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
