using System.IO;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// The HUD's sprites, generated as PNG assets under <see cref="Dir"/> every time the HUD is built.
    ///
    /// <para><b>Why generated assets and not <c>Sprite.Create</c>.</b> A sprite made in memory during
    /// the build is not an asset, so the saved prefab would reference nothing and every glass pane would
    /// come back as a plain quad. Writing PNGs and importing them as 9-sliced sprites makes the look a
    /// committed, regenerable artefact — hard rule 4 — and it costs nothing at runtime: six tiny
    /// textures, no material, drawn by the stock UI shader.</para>
    ///
    /// <para><b>What "glass" is made of here.</b> UGUI has no blur, and a real blur would cost a pass
    /// per pane. The look is built from four cheap layers instead: a rounded smoked pane (a dark tint
    /// at a linear-space alpha — see MainMenuBuilder's note on why UI alphas read strong in a linear
    /// project), a soft shadow under it so it sits OFF the frame rather than painted on it, a faint sheen
    /// band across its upper third, and a one-pixel light along the top edge that fades in from ember at
    /// the left — the eclipse's rim catching the glass. The edge light is the one loud thing; the rest is
    /// quiet on purpose.</para>
    /// </summary>
    public static class UiSprites
    {
        public const string Dir = "Assets/UI/Generated";

        /// <summary>Bump when a generator changes so stale PNGs are rewritten on the next HUD build.</summary>
        const int Version = 1;

        // ---- the set ----------------------------------------------------------------------------

        /// <summary>Rounded 9-slice pane, corner radius 6 px at native size. Tint it with the Image colour.</summary>
        public static Sprite Pane() => RoundedRect("Glass_Pane_r6", 32, 6f, 0f);

        /// <summary>Rounded 9-slice tile, radius 8 px — the item slots.</summary>
        public static Sprite Tile() => RoundedRect("Glass_Tile_r8", 40, 8f, 0f);

        /// <summary>Fully rounded ends — the timer and the buttons.</summary>
        public static Sprite Pill() => RoundedRect("Glass_Pill", 64, 31f, 0f);

        /// <summary>A bar track: radius 4 px.</summary>
        public static Sprite Track() => RoundedRect("Glass_Track_r4", 20, 4f, 0f);

        /// <summary>A rounded rect with a soft 8 px alpha falloff: the shadow a pane throws.</summary>
        public static Sprite Shadow() => RoundedRect("Glass_Shadow", 64, 14f, 8f);

        /// <summary>Horizontal strip whose alpha fades in over the left 18 % and out over the right 12 %.</summary>
        public static Sprite EdgeLight()
        {
            return Generate("Glass_EdgeLight", 256, 4, (x, y, w, h) =>
            {
                float u = x / (float)(w - 1);
                float a = Mathf.Min(Mathf.SmoothStep(0f, 1f, u / 0.18f), Mathf.SmoothStep(0f, 1f, (1f - u) / 0.12f));
                return new Color(1f, 1f, 1f, a);
            }, Vector4.zero);
        }

        /// <summary>Vertical strip: a highlight band across the upper third of a pane, gone by the middle.</summary>
        public static Sprite Sheen()
        {
            return Generate("Glass_Sheen", 4, 64, (x, y, w, h) =>
            {
                float v = y / (float)(h - 1);              // 0 = bottom, 1 = top
                float a = Mathf.Clamp01((v - 0.55f) / 0.45f);
                a = a * a * (1f - 0.35f * (1f - v));       // brightest just under the top edge
                return new Color(1f, 1f, 1f, a);
            }, Vector4.zero);
        }

        // ---- generators -------------------------------------------------------------------------

        delegate Color Pixel(int x, int y, int w, int h);

        static Sprite RoundedRect(string name, int size, float radius, float feather)
        {
            float border = Mathf.Ceil(radius + feather) + 1f;
            return Generate(name, size, size, (x, y, w, h) =>
            {
                // Signed distance to a rounded rectangle inset by the feather, then anti-aliased.
                float px = x + 0.5f, py = y + 0.5f;
                float hw = w * 0.5f - feather, hh = h * 0.5f - feather;
                float dx = Mathf.Abs(px - w * 0.5f) - (hw - radius);
                float dy = Mathf.Abs(py - h * 0.5f) - (hh - radius);
                float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude
                              + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                float a = feather > 0f
                    ? Mathf.Clamp01(1f - (outside + feather) / feather)
                    : Mathf.Clamp01(0.5f - outside);
                return new Color(1f, 1f, 1f, a);
            }, new Vector4(border, border, border, border));
        }

        static Sprite Generate(string name, int w, int h, Pixel pixel, Vector4 border)
        {
            string path = Dir + "/" + name + ".png";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = pixel(x, y, w, h);
            tex.SetPixels(px);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), Dir));
            string full = Path.Combine(Directory.GetCurrentDirectory(), path);
            bool changed = !File.Exists(full) || !SameBytes(File.ReadAllBytes(full), png);
            if (changed) File.WriteAllBytes(full, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                bool dirty = importer.textureType != TextureImporterType.Sprite
                          || importer.spriteBorder != border
                          || importer.textureCompression != TextureImporterCompression.Uncompressed
                          || importer.mipmapEnabled
                          || importer.userData != "UiSprites v" + Version;
                if (dirty)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spriteBorder = border;
                    importer.spritePixelsPerUnit = 100f;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.userData = "UiSprites v" + Version;
                    importer.SaveAndReimport();
                }
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogWarning("[UiSprites] " + path + " did not import as a Sprite; the HUD falls back to flat quads.");
            return sprite;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
