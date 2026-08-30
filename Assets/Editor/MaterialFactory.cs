using System.IO;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Creates (or updates) the neon greybox material set under Assets/Materials.
    /// All materials are URP/Lit, matte, with _EMISSION enabled so EmissiveFlash property blocks work.
    /// </summary>
    public static class MaterialFactory
    {
        const string Folder = "Assets/Materials";
        const string ShaderName = "Universal Render Pipeline/Lit";

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");

        struct Spec
        {
            public string name;
            public Color baseColor;
            public Color emission;
            public Spec(string n, Color b, Color e) { name = n; baseColor = b; emission = e; }
        }

        static Spec[] Table => new[]
        {
            // Dark fantasy palette: near-black violet stone, blood, ember, ghost teal, pale steel.
            // Names are stable — other builders reference them.
            new Spec("M_Ground",        Hex("#0B0910"), Color.black),
            new Spec("M_Platform",      Hex("#3A3442"), Hex("#4A3F52") * 0.12f),
            new Spec("M_NeonPink",      Color.black,    Hex("#A8102A") * 2.2f),   // blood (trims, rails)
            new Spec("M_NeonCyan",      Color.black,    Hex("#1F8F86") * 1.8f),   // ghost teal
            new Spec("M_NeonYellow",    Color.black,    Hex("#D9701A") * 2.4f),   // ember
            new Spec("M_NeonRed",       Color.black,    Hex("#FF1030") * 4f),     // unblockable marker (keep bright)
            new Spec("M_Enemy",         Hex("#0A0708"), Hex("#6A0F14") * 1.2f),
            new Spec("M_EnemyEye",      Hex("#200000"), Hex("#FF3A1A") * 5f),
            new Spec("M_Boss",          Hex("#0D0612"), Hex("#7A1030") * 1.6f),
            new Spec("M_Weapon_Sword",  Hex("#0A0E14"), Hex("#8FB5D9") * 2.5f),  // pale ghost steel
            new Spec("M_Weapon_Hammer", Hex("#140C05"), Hex("#E0661A") * 3f),
            new Spec("M_Weapon_Dagger", Hex("#0B1208"), Hex("#5FD66A") * 2.5f),
            new Spec("M_Weapon_Dev",    Hex("#061208"), Hex("#7FFF9A") * 3f),
            new Spec("M_Checkpoint",    Color.black,    Hex("#7A1030") * 2.5f),
            new Spec("M_Bloodstain",    Color.black,    Hex("#9AE07A") * 3f),
            new Spec("M_Gate",          Hex("#0B0910"), Hex("#A8102A") * 1.5f),
            new Spec("M_Torch",         Hex("#200800"), Hex("#FF7A1A") * 6f),
            new Spec("M_Stone",         Hex("#171320"), Color.black),
        };

        [MenuItem("VibeGame1/2. Create Materials")]
        public static void CreateAll()
        {
            EnsureFolder();

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[MaterialFactory] Shader '{ShaderName}' not found. Is URP installed?");
                return;
            }

            int created = 0, updated = 0;
            foreach (var spec in Table)
            {
                string path = PathFor(spec.name);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = mat == null;
                if (isNew)
                {
                    mat = new Material(shader) { name = spec.name };
                    AssetDatabase.CreateAsset(mat, path);
                    created++;
                }
                else
                {
                    if (mat.shader != shader) mat.shader = shader;
                    updated++;
                }

                Configure(mat, spec);
                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[MaterialFactory] Materials ready in {Folder}: {created} created, {updated} updated.");
        }

        /// <summary>Loads Assets/Materials/{name}.mat (null with a warning if missing).</summary>
        public static Material Get(string name)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(PathFor(name));
            if (mat == null) Debug.LogWarning($"[MaterialFactory] Material '{name}' not found. Run VibeGame1/2. Create Materials first.");
            return mat;
        }

        static void Configure(Material mat, Spec spec)
        {
            mat.SetFloat(SmoothnessId, 0f);
            mat.SetFloat(MetallicId, 0f);
            mat.SetColor(BaseColorId, spec.baseColor);
            mat.SetColor(EmissionColorId, spec.emission);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            // Matte look: no specular highlights on the flat neon shapes.
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        }

        static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder)) return;
            string parent = Path.GetDirectoryName(Folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(Folder);
            AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, leaf);
        }

        static string PathFor(string name) => $"{Folder}/{name}.mat";

        static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }
    }
}
