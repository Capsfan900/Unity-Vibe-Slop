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
            /// <summary>
            /// 0 = fully matte, which is the house style for the flat neon shapes and the default here.
            /// Above 0 the material keeps its specular highlight, which is the ONLY way a non-emissive
            /// surface can show curvature in this game: the course is backlit, so an enemy facing the
            /// player receives no key light and ambient alone renders it as a flat cutout. A little
            /// smoothness lets the ambient sky term skim the shoulders and give the silhouette an
            /// interior. It is deliberately NOT emission — "enemies do not glow" is a feel contract,
            /// because light on an enemy means you deflected.
            /// </summary>
            public float smoothness;
            public Spec(string n, Color b, Color e, float s = 0f)
            { name = n; baseColor = b; emission = e; smoothness = s; }
        }

        static Spec[] Table => new[]
        {
            // ECLIPSE PALETTE.
            //   STONE  near-black, faintly warm       - everything structural
            //   BONE   ash neutral                    - platform tops (footing legibility, never trim)
            //   TRIM   four separable accent hues     - the level's per-tile identity
            // The level is four tiles and their identity is carried by TRIM COLOUR, so the four accents
            // must be separable at a glance and at distance: teal / gold / ember / red. ALL FOUR
            // navigational trims are held under the desaturation ceiling (peak channel <= 1.25) so they
            // read as coloured light without blowing out. Nothing that has to be LOUD may share a key
            // with them - the combat tell lives in M_AlertTell, below, precisely because a trim and a
            // tell want opposite intensities.
            // Names are stable - other builders reference them, so the hues changed, not the keys.

            // #151011 -> #262023. The old value was ~0.008 LINEAR reflectance - darker than any real
            // material - and it covers most of the structural surface area, so the whole world was one
            // multiplicative near-zero (dark ambient x dark albedo) with nothing for light to land on.
            new Spec("M_Ground",        Hex("#262023"), Color.black),                 // stone
            new Spec("M_Platform",      Hex("#56504A"), Hex("#56504A") * 0.10f),      // ash, lifted so footing reads
            new Spec("M_NeonPink",      Color.black,    Hex("#C4400F") * 1.15f),      // TILE 3 - EMBER accent
            new Spec("M_NeonCyan",      Color.black,    Hex("#1FB9D6") * 1.00f),      // TILE 1 - cold ghost teal
            new Spec("M_NeonYellow",    Color.black,    Hex("#D8C22A") * 0.85f),      // TILE 2 - brass gold
            // TILE 4. It used to be #FF2A10 * 2.2 - far over the bloom threshold, where the tonemapper
            // desaturates it: an HDR red that far up renders ORANGE and was indistinguishable from the
            // ember boss court. Pure hue, held near 1.1, keeps it saturated crimson and separates tile 4
            // from tile 3. It is NAVIGATION ONLY - it no longer drives the unblockable tell.
            new Spec("M_NeonRed",       Color.black,    Hex("#FF1010") * 1.10f),
            // ---- Combat tell -------------------------------------------------------------------
            // The unblockable / alert cube above an enemy's head. This is the OPPOSITE brief to a trim:
            // the player has ~0.45 s to react to it, so it must be among the loudest things on screen
            // and it is SUPPOSED to bloom and smear. It used to share M_NeonRed with the tile-4 trim,
            // which meant one material was being tuned in two opposite directions - dropping the trim
            // under the bloom threshold silently killed the tell. Never merge these two keys again.
            // Hue is red with a blue lift rather than pure red: ACES pushes a saturated pure red toward
            // ORANGE as it brightens (the same trap that hit the trim), and orange is the boss court's
            // colour. The blue lift makes it desaturate toward hot pink-white instead, which nothing
            // else in the palette occupies. ~2.9x the 1.05 bloom threshold.
            new Spec("M_AlertTell",     Color.black,    Hex("#FF0A28") * 3.00f),
            // ---- Deathblow marker --------------------------------------------------------------
            // The Sekiro "this one is ready to be killed" glyph, raised over an enemy whose posture is
            // broken. Same brief as the tell above - loud, read in a glance, allowed to bloom - and for
            // that reason it gets its OWN key rather than borrowing M_AlertTell: those two markers sit in
            // the same place on screen and mean OPPOSITE things ("an attack you cannot block is coming"
            // versus "kill this one now"). A shared read there is worse than no marker at all.
            // ARC VIOLET-BLUE. Hue picked by elimination: the four navigational trims own teal, gold,
            // crimson and ember; the alert tell owns red desaturating to hot pink-white; the Pyre fire
            // owns orange-gold. Violet is the one loud hue nothing else in the palette occupies.
            // THE RED CHANNEL IS THE WHOLE FIGHT. The first attempt was #7A2BFF * 2.60, i.e.
            // (1.24, 0.44, 2.60) - and that shipped a marker indistinguishable from the alert tell,
            // because a channel over 1.0 CLIPS: red and blue both pinned at full and the glyph rendered
            // MAGENTA. Loud is not a hue. To stay violet the marker must be loud in blue while its red
            // stays UNDER 1.0 after scaling. #3A18FF * 2.60 = (0.59, 0.24, 2.60) was the second attempt
            // and still read as light ORCHID beside the tell in a side-by-side capture - close enough to
            // pink to hesitate over. Red had to come down again: #2A0BFF * 2.60 = (0.43, 0.11, 2.60)
            // renders as an unmistakable blue-violet. Verified by screenshot, three hues in one frame;
            // arithmetic got this wrong twice.
            // 2.60 is ~2.5x the shipped 1.05 bloom threshold - unmissable - but deliberately below the
            // tell's 3.00, because when both could be on screen the thing that can kill YOU must win.
            new Spec("M_DeathblowMark", Color.black,    Hex("#2A0BFF") * 2.60f),
            // ---- Lock-on dot -------------------------------------------------------------------
            // The Dark Souls target reticle: one small pale mote on the CHEST of whatever the player has
            // locked. Third combat marker in the palette, and the only quiet one.
            // IT IS INFORMATION, NOT AN ALARM. The tell means "danger" and the deathblow glyph means
            // "opportunity"; both are meant to grab you and both sit ~2.5-3x over the 1.05 bloom
            // threshold. Lock-on means neither - it says "this one" and then must be ignorable for the
            // whole fight. So it is the one combat marker held UNDER the bloom threshold: 0.78 peak
            // channel never blooms, never smears and never desaturates, and a marker that does not
            // bloom cannot be mistaken for one that does even in peripheral vision.
            // DESATURATED ON PURPOSE. Every saturated slot is spoken for - teal, gold, crimson and
            // ember for the four navigational trims, hot pink-white for the tell, arc violet for the
            // deathblow, orange-gold for the Pyre fire. Pale bone-grey is the only thing left that
            // carries no meaning, and in Dark Souls the reticle is a plain pale dot for exactly that
            // reason. A faint cool cast (blue > red) keeps it off the warm ember world so it separates
            // by hue as well as level, without ever reading as coloured.
            new Spec("M_LockOnDot",     Color.black,    Hex("#CBD2D8") * 0.95f),
            // Enemies are UNLIT. Emission black: EnemyVisuals drives base colour and only emits on a parry.
            // #0B0809 -> #1F1D24. Still the darkest character surface in the game and still NON-EMISSIVE
            // ("enemies do not glow" - light on an enemy means you deflected), but 0.003 linear albedo made
            // a backlit grunt a flat black CUTOUT with no interior shading at all. The new value is a cool
            // near-black: it separates by HUE from the warm ember-lit floor, so the silhouette holds
            // without the enemy becoming a light source.
            // Smoothness 0.34: enough for the ambient sky term to skim a shoulder and give the torso an
            // interior, not enough to look wet. This is the other half of the ambient pass — that lifted
            // the enemy from a measured 0.0 to 8.8, but a matte surface still had no shape within the
            // silhouette. Note M_Enemy's BASE COLOUR is overridden per-enemy by EnemyData.bodyColor via
            // a MaterialPropertyBlock; smoothness is not, so it applies to every enemy in the game.
            new Spec("M_Enemy",         Hex("#1F1D24"), Color.black, 0.34f),
            new Spec("M_EnemyEye",      Hex("#180400"), Hex("#FF5A18") * 0.9f),       // faint ember, findable in the dark
            new Spec("M_Boss",          Hex("#0D0709"), Color.black),
            new Spec("M_Weapon_Sword",  Hex("#0A0C10"), Hex("#9FB4C6") * 0.9f),
            new Spec("M_Weapon_Hammer", Hex("#120C06"), Hex("#D0722A") * 1.0f),
            new Spec("M_Weapon_Dagger", Hex("#0B0F0A"), Hex("#8FBE86") * 0.9f),
            new Spec("M_Weapon_Dev",    Hex("#08120A"), Hex("#7FE79A") * 1.1f),
            new Spec("M_Checkpoint",    Color.black,    Hex("#C4400F") * 1.5f),       // ember: something happens here
            new Spec("M_Bloodstain",    Color.black,    Hex("#B8D08A") * 1.1f),
            new Spec("M_Gate",          Hex("#090607"), Hex("#C4400F") * 0.8f),
            new Spec("M_Torch",         Hex("#2A1206"), Hex("#FF7A1A") * 1.15f),      // flame, not a white slab
            // #1E1819 -> #3A3134. Walls, pillars and obelisks - i.e. VERTICAL faces, which Trilight
            // ambient lights with the equator term only. Kept just above M_Ground so a wall separates
            // from the floor it meets.
            new Spec("M_Stone",         Hex("#3A3134"), Color.black),
            new Spec("M_Lightning",     Color.black,    Hex("#7FD4FF") * 3.5f),
            new Spec("M_Item",          Color.black,    Color.white * 1.2f),
            new Spec("M_Spark",         Color.black,    Color.white * 2.4f),
            new Spec("M_WeaponCore",    Hex("#08070C"), Color.black),
            // ---- Viewmodel arms ----------------------------------------------------------------
            // Gauntleted, not bare skin. The hands sit at the BOTTOM of the visual hierarchy - below the
            // weapon, below the enemy, below the trim - so they are deliberately the darkest lit surface
            // on screen. The first pass was a warm mid-brown (#2C2522) which, lit by the arena, read as
            // pale WOOD and made the knuckles the brightest object in the frame. Dark cold leather now:
            // still above STONE (#151011) so the fist keeps a silhouette, but ~40% darker than before.
            new Spec("M_Gauntlet",      Hex("#1B1719"), Hex("#1B1719") * 0.12f),
            // Knuckle and cuff banding - the only thing that says HAND rather than dark blob. COOL steel,
            // not warm tan: the warm version read as wood banding, and a cool grey also separates the
            // hand from the warm ember world. Sits just above the leather and far under the weapon
            // emission (~0.6) and the 1.25 bloom threshold.
            new Spec("M_GauntletTrim",  Hex("#14161A"), Hex("#6E7B88") * 0.15f),
            // Neutral emissive body EnergyGlow tints at runtime. White because the property block
            // multiplies the hue in; a coloured base would double-tint.
            new Spec("M_Energy",        Color.black,    Color.white * 0.9f),

            // ---- Eclipse backdrop -------------------------------------------------------------
            // The disc is PURE BLACK with no emission on purpose. Linear fog blends distant geometry
            // toward fogColor, which is itself near-black, so a black disc survives the fog and stays a
            // silhouette. A bright object out there would just wash to fog colour.
            new Spec("M_EclipseDisc",   Color.black,    Color.black),
            // The corona sits just behind the disc rim. Bright enough to survive partial fogging at
            // ~80 units and still read as a burning edge.
            new Spec("M_EclipseCorona", Color.black,    Hex("#FF5A12") * 6f),
            // Sky band on the horizon behind the disc: deep blood, low intensity so it glows without
            // blooming into a smear.
            new Spec("M_EclipseSky",    Hex("#12040A"), Hex("#5E0F12") * 1.1f),
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
            mat.SetFloat(SmoothnessId, spec.smoothness);
            mat.SetFloat(MetallicId, 0f);
            mat.SetColor(BaseColorId, spec.baseColor);
            mat.SetColor(EmissionColorId, spec.emission);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            // Matte by default: no specular highlights on the flat neon shapes. But this used to be
            // unconditional, and that is why a backlit enemy rendered as a flat black cutout — with
            // smoothness 0 AND highlights off there is no term left that can describe curvature. A spec
            // asking for smoothness keeps its highlight.
            bool matte = spec.smoothness <= 0.0001f;
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", matte ? 0f : 1f);
            if (matte) mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            else mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");

            // The eclipse backdrop is built from Quads, which are single-sided. Rendering them
            // double-sided means a wrong-way rotation can never silently blank the entire sky.
            if (spec.name.StartsWith("M_Eclipse") && mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", 0f);   // UnityEngine.Rendering.CullMode.Off
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
