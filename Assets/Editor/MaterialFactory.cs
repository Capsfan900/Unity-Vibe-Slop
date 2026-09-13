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
        const string CloudSeaShaderName = "VibeGame1/Cloud Sea";
        const string SolarArenaShaderName = "VibeGame1/Solar Arena";
        const string ArchitecturalStoneShaderName = "VibeGame1/Architectural Stone";
        const string RainbowBorderShaderName = "VibeGame1/UI/RainbowBorder";

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
            // COLD ECLIPSE PALETTE (2026-09-06, user-directed: "change the main colour scheme to blue").
            //   STONE  near-black, faintly COLD       - everything structural
            //   BONE   cold ash neutral               - platform tops (footing legibility, never trim)
            //   TRIM   four separable accent hues     - the level's per-tile identity
            // Every structural albedo below moved from warm to cold at MATCHED Rec.709 linear
            // luminance, the same discipline ProjectSetup applies to ambient: hue changed, light level
            // untouched, so FeatureTests' albedo floor (0.010 linear) and the tell-vs-world contrast
            // ratio (tell >= 20x the brightest structural albedo) both hold by construction.
            //
            // WHAT STAYS WARM, DELIBERATELY. In a cold world, warm means ONE of two things and nothing
            // else: a combat tell (M_AlertTell, the bolt, the cue spark) or FIRE, which means safety
            // (M_Torch, M_Checkpoint - the Dark Souls bonfire read). Warm on cold is the highest-contrast
            // pair there is, so making the world blue makes every tell easier to read, not harder.
            // The level is four tiles and their identity is carried by TRIM COLOUR, so the four accents
            // must be separable at a glance and at distance: teal / gold / ember / red. ALL FOUR
            // navigational trims are held under the desaturation ceiling (peak channel <= 1.25) so they
            // read as coloured light without blowing out. Nothing that has to be LOUD may share a key
            // with them - the combat tell lives in M_AlertTell, below, precisely because a trim and a
            // tell want opposite intensities.
            // Names are stable - other builders reference them, so the hues changed, not the keys.

            // The live horizontal ambient probe is healthy, but multiplying it by the old ground albedo
            // produced only .0029 linear luminance before mortar/occlusion and ACES. Lift reflected
            // surfaces rather than adding a light or flattening the grade; navigation emission is unchanged.
            new Spec("M_Ground",        Hex("#36404F"), Color.black),                 // cold stone (lin lum .0502)
            new Spec("M_Platform",      Hex("#586579"), Hex("#475262") * 0.10f),      // readable ash; established emission unchanged
            // TILE 3. Was EMBER #C4400F x1.15 - the worst collision in the old palette, because ember
            // IS the enemy bolt's hue (Projectile.HotCore, amber at 1.6). A static level trim must never
            // share a hue family with the one thing the player has to deflect at 32 m/s. Now deep AZURE:
            // cold, saturated, and ~200 degrees of hue away from the bolt.
            new Spec("M_NeonPink",      Color.black,    Hex("#2F6BFF") * 0.95f),      // TILE 3 - azure
            new Spec("M_NeonCyan",      Color.black,    Hex("#35DCEC") * 1.00f),      // TILE 1 - ice cyan, lifted so it still separates from a blue world
            // TILE 2. The ONE warm navigational accent kept, and dimmed 0.85 -> 0.75 so the brightness
            // gap to the nearest warm TELL is over 2x (0.635 peak against the bolt's 1.6). Brass is a
            // metal, not a light: it never blooms, never moves and never appears above a head.
            new Spec("M_NeonYellow",    Color.black,    Hex("#D8C22A") * 0.75f),      // TILE 2 - brass gold
            // TILE 4. Twice retired: first from #FF2A10 x 2.2 (over the bloom threshold, where ACES
            // desaturated it to orange), then from crimson #FF1010 x 1.10. Crimson had to go with the
            // cold pass: against a BLUE world a saturated red trim is the loudest thing on a wall, and
            // red is spoken for by the alert tell (M_AlertTell, pink-red at 3.00) and the unblockable
            // cue tint. A player glancing at a red edge 30 m away must not have to ask whether it is an
            // attack. Now GHOST GREEN - cold-leaning, owned by no tell, and the four trims stay spread
            // (gold ~52, green ~142, cyan ~186, azure ~222 degrees). Weakest surviving pair is cyan vs
            // azure at ~36 degrees, separated by luminance and saturation rather than hue.
            // NAVIGATION ONLY - it drives no tell.
            new Spec("M_NeonRed",       Color.black,    Hex("#3FE07A") * 0.95f),
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
            new Spec("M_Enemy",         Hex("#1A1E29"), Color.black, 0.34f),   // cold (lin lum .0130, was #1F1D24 .0130)
            // The eye stays EMBER, and it is the one warm thing ON an enemy. It is under the 1.05
            // bloom threshold so it never glows ("enemies do not glow" survives), and against a cold
            // body on a cold world a warm pinprick is the cheapest "something is alive there" cue in
            // the game. It says an enemy EXISTS - the same warm-means-combat language as the tells; it
            // never says an attack is coming.
            new Spec("M_EnemyEye",      Hex("#180400"), Hex("#FF5A18") * 0.9f),       // faint ember, findable in the dark
            // ---- The Sentry ghost (2026-09-06, user-directed) -----------------------------------
            // pshooter_enemy01's shell, hem and nub arms. It is the ONE pale character surface in the
            // game, and that is the whole design: everything structural sits at 0.013-0.082 albedo, so a
            // body at 0.855 separates from the world by ~50x in VALUE, which reads at 25 m down a span
            // where a hue difference would not. COLD, because warm means a combat tell or fire
            // (ANIMATION-VFX section 4 rule 10) and an enemy body may be neither; and not violet, because
            // violet means "use this" and belongs to the flare.
            // THE EMISSION HERE IS THE HEM AND THE ARMS ONLY. EnemyVisuals owns the SHELL's emission via
            // a property block (SentryGhostVisual.ShellEmissionPeak 0.28, through SetAura). 0.16 keeps the hem
            // luminous without it stacking past the cap under the mist: 0.35 lit + 0.16 + 4 x 0.10 = 0.91.
            // Smoothness 0.18, well under M_Enemy's 0.34: a pale surface needs far less specular help to
            // show curvature, and a glossy ghost reads as plastic.
            new Spec("M_SentryGhost",         Hex("#A9C2DA"), Hex("#8FB6E0") * 0.16f, 0.18f),
            new Spec("M_Boss",          Hex("#08090E"), Color.black),   // cold near-black
            new Spec("M_Weapon_Sword",  Hex("#0A0C10"), Hex("#9FB4C6") * 0.9f),
            new Spec("M_Weapon_Hammer", Hex("#120C06"), Hex("#D0722A") * 1.0f),
            new Spec("M_Weapon_Dagger", Hex("#0B0F0A"), Hex("#8FBE86") * 0.9f),
            new Spec("M_Weapon_Dev",    Hex("#08120A"), Hex("#7FE79A") * 1.1f),
            // KEPT WARM ON PURPOSE. In a cold world a warm light is a bonfire: Dark Souls and Elden
            // Ring train the player to hunt exactly this hue when they are lost. It is not a tell - it
            // is static, at a fixed place, and never demands an action inside the 0.28 s cue window.
            new Spec("M_Checkpoint",    Color.black,    Hex("#C4400F") * 1.5f),       // ember: safety, the one warm beacon
            new Spec("M_Bloodstain",    Color.black,    Hex("#B8D08A") * 1.1f),   // pale sage, unchanged: dim, on the ground, desaturated enough not to read as the tile-4 green
            // A gate is structure, not fire: cold now, so the only ember light in the level is a torch
            // or a checkpoint and the player can trust that read.
            new Spec("M_Gate",          Hex("#060809"), Hex("#2E7ACF") * 0.8f),
            new Spec("M_Torch",         Hex("#2A1206"), Hex("#FF7A1A") * 1.15f),      // flame - warm on purpose, see M_Checkpoint
            // Walls, pillars and obelisks are lit primarily by the Trilight equator term. The lifted
            // cold stone preserves face separation without emission or another renderer/light.
            new Spec("M_Stone",         Hex("#424D5F"), Color.black),   // cold (lin lum .0729)
            new Spec("M_Lightning",     Color.black,    Hex("#7FD4FF") * 3.5f),
            new Spec("M_Item",          Color.black,    Color.white * 1.2f),
            new Spec("M_Spark",         Color.black,    Color.white * 2.4f),
            // ---- The weapon's MASS -------------------------------------------------------------
            // Every non-emissive part of every viewmodel: guard, quillons, grip, pommel, the maul's head
            // and cheeks and spike. In other words, EXACTLY the parts that make a hammer read as a
            // hammer instead of as a glowing stick — the archetype lives in the silhouette, and the
            // silhouette lives in these parts.
            //
            // WAS #08070C: 0.0025 linear luminance. That is the last survivor of the mistake this file
            // already fixed twice (M_Ground "darker than any real material", M_Enemy "a flat black
            // cutout with no interior"). Two consequences, both visible in every frame of play:
            //   1. against a world whose own structural albedos sit at 0.013-0.082, the mass parts were
            //      DARKER than the background they are supposed to be silhouetted against — the weapon
            //      read as the emissive segments alone, floating, with no object holding them;
            //   2. it sat 3.6x BELOW M_Gauntlet (0.0091), which inverts the hierarchy this file states
            //      two entries down: the hands are meant to be "the darkest lit surface on screen -
            //      below the weapon". The glove was brighter than the weapon it was gripping.
            // Now a cold gunmetal at 0.0313 linear — 3.4x the glove, just under M_Stone (0.0334), so a
            // weapon reads as a dense object in front of a wall and never as a light. Nothing emits:
            // the hot channel on a weapon belongs to the blade (M_Energy, driven by EnergyGlow) and to
            // the Pyre embers, and mass that glowed would compete with both.
            //
            // Smoothness 0.45 for the same reason M_Enemy and M_SentryGhost carry theirs: this course is
            // backlit, and on a matte near-black surface ambient alone renders a bevelled guard as one
            // flat shape. A specular highlight is the only channel a non-emissive surface has to say
            // "metal, and curved" — and it is what makes the maul's head read as a block of steel
            // rather than as a dark rectangle.
            new Spec("M_WeaponCore",    Hex("#2B313C"), Color.black, 0.45f),
            // ---- Viewmodel arms ----------------------------------------------------------------
            // Gauntleted, not bare skin. The hands sit at the BOTTOM of the visual hierarchy - below the
            // weapon, below the enemy, below the trim - so they are deliberately the darkest lit surface
            // on screen. The first pass was a warm mid-brown (#2C2522) which, lit by the arena, read as
            // pale WOOD and made the knuckles the brightest object in the frame. Dark cold leather now:
            // still above STONE (#151011) so the fist keeps a silhouette, but ~40% darker than before.
            new Spec("M_Gauntlet",      Hex("#16181D"), Hex("#16181D") * 0.12f),   // cold leather, matched weight
            // Knuckle and cuff banding - the only thing that says HAND rather than dark blob. COOL steel,
            // not warm tan: the warm version read as wood banding, and a cool grey also separates the
            // hand from the warm ember world. Sits just above the leather and far under the weapon
            // emission (~0.6) and the 1.25 bloom threshold.
            new Spec("M_GauntletTrim",  Hex("#14161A"), Hex("#6E7B88") * 0.15f),
            // Neutral emissive body EnergyGlow tints at runtime. White because the property block
            // multiplies the hue in; a coloured base would double-tint.
            new Spec("M_Energy",        Color.black,    Color.white * 0.9f),
            // The spellbook is broad and always near the lens, so its mass needs its own warm value
            // structure instead of borrowing blue-grey level stone. Leather stays unlit; parchment gets
            // a very low warm self-light so the open pages remain readable in the eclipse without blooming.
            new Spec("M_SpellbookLeather", Hex("#29131B"), Color.black, 0.30f),
            new Spec("M_SpellbookPage",    Hex("#B9A47A"), Hex("#B9A47A") * 0.28f, 0.08f),
            // ---- Traversal (2026-09-04 pivot) ---------------------------------------------------
            // The balloon is a MARKER, not a light: soft gold held under the 1.05 bloom threshold so it
            // never competes with a cue flash, and warm so it separates from the cold water and the
            // teal trims. Its pop is the only bright thing about it, and SlashFx caps that at 1.0.
            new Spec("M_Balloon",       Hex("#3A2A10"), Hex("#FFC24A") * 0.95f, 0.4f),
            // Water: a cold translucent film (alpha in Configure), lit, a little glossy so the sky term
            // skims it. Emission just enough to read in the dark, far under the bloom threshold.
            // Water was already the one cold surface, which is exactly why the cold pass makes it a
            // problem: cold water on a cold world stops separating. Pushed toward CYAN (away from the
            // world's slate-azure) and its emission lifted 0.35 -> 0.45, still 2.3x under the bloom
            // threshold. A traversal surface the player must recognise mid-air cannot read as floor.
            new Spec("M_Water",         new Color(0.18f, 0.55f, 0.72f, 0.55f), Hex("#2FA8C8") * 0.45f, 0.7f),

            // ---- Eclipse backdrop -------------------------------------------------------------
            // The disc is PURE BLACK with no emission on purpose. Linear fog blends distant geometry
            // toward fogColor, which is itself near-black, so a black disc survives the fog and stays a
            // silhouette. A bright object out there would just wash to fog colour.
            new Spec("M_EclipseDisc",   Color.black,    Color.black),
            // The corona sits just behind the disc rim. Bright enough to survive partial fogging at
            // ~80 units and still read as a burning edge. COLD now, matching Starfield's corona: a warm
            // corona would be a huge static warm bloom in the sky, and the amber bolt has to fly across
            // that sky and stay legible.
            new Spec("M_EclipseCorona", Color.black,    Hex("#4C9EE0") * 6f),
            // Sky band on the horizon behind the disc: deep midnight blue, low intensity so it glows
            // without blooming into a smear.
            new Spec("M_EclipseSky",    Hex("#050A14"), Hex("#12335E") * 1.1f),

            // Opaque two-sided realm shells and their matched boundary walls. These stay below the
            // bloom threshold at eye level; the procedural solar ceiling carries the hot exception.
            new Spec("M_SolarRealmCyan",  Hex("#07171D"), Hex("#123D46") * 0.55f),
            new Spec("M_SolarRealmGold",  Hex("#1B1708"), Hex("#514516") * 0.50f),
            new Spec("M_SolarRealmAzure", Hex("#080E24"), Hex("#152A66") * 0.55f),
            new Spec("M_SolarRealmGhost", Hex("#071A10"), Hex("#174A29") * 0.55f),
        };

        [MenuItem("VibeGame1/2. Create Materials")]
        public static void CreateAll()
        {
            EnsureFolder();

            var shader = Shader.Find(ShaderName);
            var architecturalShader = Shader.Find(ArchitecturalStoneShaderName);
            if (shader == null || architecturalShader == null)
            {
                Debug.LogError("[MaterialFactory] URP/Lit or Architectural Stone shader missing. Reimport shaders and verify URP.");
                return;
            }

            int created = 0, updated = 0;
            foreach (var spec in Table)
            {
                bool structural = spec.name == "M_Platform" || spec.name == "M_Stone" || spec.name == "M_Ground";
                var selectedShader = structural ? architecturalShader : shader;
                string path = PathFor(spec.name);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = mat == null;
                if (isNew)
                {
                    mat = new Material(selectedShader) { name = spec.name };
                    AssetDatabase.CreateAsset(mat, path);
                    created++;
                }
                else
                {
                    if (mat.shader != selectedShader) mat.shader = selectedShader;
                    updated++;
                }

                Configure(mat, spec);
                if (structural) ConfigureArchitecturalStone(mat);
                EditorUtility.SetDirty(mat);
            }

            // The lower atmosphere has its own transparent procedural shader. Keep it in this factory
            // so the scene builders can serialize a real material asset and player builds cannot strip
            // the shader as an unreferenced Shader.Find-only dependency.
            CreateCloudSea();
            CreateSolarArenaMaterials();
            CreateRadioAura();

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

        /// <summary>Creates or refreshes the one shared cloud-ocean material.</summary>
        public static Material CreateCloudSea()
        {
            EnsureFolder();
            var shader = Shader.Find(CloudSeaShaderName);
            if (shader == null)
            {
                Debug.LogError("[MaterialFactory] Shader '" + CloudSeaShaderName +
                               "' not found. Reimport Assets/Shaders/CloudSea.shader first.");
                return null;
            }

            const string name = "M_CloudSea";
            string path = PathFor(name);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            // Cold world palette, all channels below 1.0: the sea is scenery and never spends the
            // attack-tell bloom budget. Alpha gives the low field body without making it a solid floor.
            mat.SetColor("_DeepColor", new Color(0.10f, 0.18f, 0.28f, 0.45f));
            mat.SetColor("_CloudColor", new Color(0.42f, 0.55f, 0.66f, 0.92f));
            mat.SetColor("_CrestColor", new Color(0.65f, 0.74f, 0.80f, 0.95f));
            mat.SetFloat("_WaveHeight", 1.50f);
            mat.SetFloat("_FlowSpeed", 0.035f);
            mat.SetFloat("_LargeScale", 0.030f);
            mat.SetFloat("_DetailScale", 0.11f);
            mat.SetFloat("_WarpStrength", 14f);
            mat.SetFloat("_DetailStrength", 0.26f);
            mat.SetFloat("_EdgeFeather", 0.12f);
            mat.SetFloat("_HazeStart", 80f);
            mat.SetFloat("_HazeEnd", 280f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10;

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        /// <summary>
        /// Creates or refreshes M_RadioAura: the rainbow that swirls around the radio pane's border.
        /// Every tuning value is set here, not in the shader block — a shader default is not a shipped
        /// value, and RadioAuraTests asserts against this material.
        /// </summary>
        public static Material CreateRadioAura()
        {
            EnsureFolder();
            var shader = Shader.Find(RainbowBorderShaderName);
            if (shader == null)
            {
                Debug.LogError("[MaterialFactory] Shader '" + RainbowBorderShaderName +
                               "' not found. Reimport Assets/Shaders/UI/RainbowBorder.shader first.");
                return null;
            }

            const string name = "M_RadioAura";
            string path = PathFor(name);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            // Geometry, in fractions of the rect's HEIGHT. The radio pane ships at 300 x 116 with a
            // UiSprites.Pane() corner of about 12 px, so 12/116 = 0.103 traces the glass rather than
            // cutting across it, and a 6.4 px band (0.055) reads as a frame at HUD distance without
            // eating the 16 px inner padding the ticker text lives in.
            mat.SetFloat("_Aspect", 300f / 116f);
            mat.SetFloat("_Radius", 0.103f);
            mat.SetFloat("_Thickness", 0.055f);
            mat.SetFloat("_Feather", 0.012f);

            // The swirl. One spectrum per lap, so the frame is a whole rainbow at every instant rather
            // than a strobing repeat; the hue creeps round at 0.18 laps/s (a 5.6 s lap) so it is alive
            // in peripheral vision but never pulls the eye off a parry cue. The comet head runs faster
            // than the hue so the two do not lock into one rigid pattern.
            mat.SetFloat("_HueCycles", 1f);
            mat.SetFloat("_SwirlSpeed", 0.18f);
            mat.SetFloat("_CometSpeed", 0.42f);
            mat.SetFloat("_CometLength", 0.34f);
            mat.SetFloat("_CometGain", 0.26f);

            // The budget. 0.78 saturation keeps the rainbow inside the game's murk instead of laying
            // pure sRGB primaries on a dark-fantasy HUD.
            //
            // Peak is 0.79, NOT the 0.82 this shipped with for an hour on 2026-09-07. 1.05 is the
            // NAVIGATIONAL cap from ANIMATION-VFX, and this is not navigational trim — it is a graphic
            // on the HUD canvas, where ARCHITECTURE's rule is absolute and one step stricter: "the UI
            // never blooms", nothing over 1.0 in any channel, because light on this screen means "you
            // deflected". 0.79 with the comet's 1.26x gain tops out at 0.995, under 1.0 before the
            // shader's own clamp, so the music player's decoration can never read as a parry.
            mat.SetFloat("_Saturation", 0.78f);
            mat.SetFloat("_Peak", 0.79f);
            mat.SetFloat("_Alpha", 0.85f);
            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_T", 0f);

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        /// <summary>Creates the four arena-sun themes plus their shared white-hot corona.</summary>
        public static void CreateSolarArenaMaterials()
        {
            EnsureFolder();
            var shader = Shader.Find(SolarArenaShaderName);
            var lit = Shader.Find(ShaderName);
            if (shader == null || lit == null)
            {
                Debug.LogError("[MaterialFactory] Solar or URP/Lit shader missing. Reimport " +
                               "Assets/Shaders/SolarArena.shader and verify URP.");
                return;
            }

            // The body stays saturated and below bloom; only its moving bands and rim cross the 1.05
            // threshold. A pale HDR body turns white under ACES before its pattern can be read.
            CreateSolar("M_SolarCyan", shader, Hex("#167A8A") * 1.15f, Hex("#35DCEC") * 1.45f, 0.42f, 0.62f, 10f);
            CreateSolar("M_SolarGold", shader, Hex("#5A430C") * 1.00f, Hex("#D8C22A") * 1.35f, 0.44f, 0.56f, 12f);
            CreateSolar("M_SolarAzure", shader, Hex("#102B6F") * 1.10f, Hex("#2F6BFF") * 1.55f, 0.43f, 0.70f, 13f);
            CreateSolar("M_SolarGhost", shader, Hex("#0C592C") * 1.05f, Hex("#3FE07A") * 1.40f, 0.45f, 0.48f, 9f);
            // Corona is a thin silhouette accent. Its old 0.18 alpha laid a white veil over the entire
            // sphere; the shader's independent rim term still gives this low-body-alpha layer a hot edge.
            CreateSolar("M_SolarCorona", shader, Hex("#B9ECFF") * 1.25f, Hex("#79CFFF") * 1.15f, 0.03f, -0.34f, 17f);
            CreateRealmMaterial(new Spec("M_SolarRealmCyan",  Hex("#07171D"), Hex("#123D46") * 0.55f), lit);
            CreateRealmMaterial(new Spec("M_SolarRealmGold",  Hex("#1B1708"), Hex("#514516") * 0.50f), lit);
            CreateRealmMaterial(new Spec("M_SolarRealmAzure", Hex("#080E24"), Hex("#152A66") * 0.55f), lit);
            CreateRealmMaterial(new Spec("M_SolarRealmGhost", Hex("#071A10"), Hex("#174A29") * 0.55f), lit);
            AssetDatabase.SaveAssets();
        }

        static void CreateRealmMaterial(Spec spec, Shader shader)
        {
            string path = PathFor(spec.name);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = spec.name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader) mat.shader = shader;
            Configure(mat, spec);
            EditorUtility.SetDirty(mat);
        }

        static void CreateSolar(string name, Shader shader, Color core, Color band, float alpha, float flow, float scale)
        {
            string path = PathFor(name);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader) mat.shader = shader;

            mat.SetColor("_CoreColor", core);
            mat.SetColor("_BandColor", band);
            mat.SetFloat("_Alpha", alpha);
            mat.SetFloat("_SurfaceOpacity", name == "M_SolarCorona" ? 0f : 0.92f);
            mat.SetFloat("_FlowSpeed", flow);
            mat.SetFloat("_BandScale", scale);
            mat.SetFloat("_RimPower", name == "M_SolarCorona" ? 1.1f : 2.2f);
            mat.SetFloat("_Pulse", name == "M_SolarCorona" ? 0.08f : 0.16f);
            mat.SetFloat("_DetailStrength", name == "M_SolarCorona" ? 0.25f : 0.65f);
            mat.SetFloat("_FilamentStrength", name == "M_SolarCorona" ? 0.08f : 0.28f);
            mat.SetFloat("_RimStrength", name == "M_SolarCorona" ? 0.70f : 0.35f);
            // The crossing multiplier ships at 1 and is driven per RENDERER by SolarArenaVisual, so
            // the shared asset is never dirtied by a player flying through a sun. Written here
            // rather than left to the shader default (rule 9: a code default is not a shipped value).
            mat.SetFloat("_Fade", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 5;
            EditorUtility.SetDirty(mat);
        }

        static void ConfigureArchitecturalStone(Material mat)
        {
            // Shared metre-scaled finish, with no textures, material instances or extra renderers.
            // Preserve the house albedo/emission values; the texture only modulates reflected light.
            mat.SetVector("_BlockSize", new Vector4(2.8f, 1.4f, 0.70f, 0f));
            mat.SetFloat("_JointWidth", 0.018f);
            mat.SetFloat("_GrainStrength", 0.12f);
            mat.SetFloat("_EdgeWear", 0.14f);
            mat.SetFloat("_ReliefDepth", 0.008f);
            mat.SetFloat("_Smoothness", 0.22f);
            mat.SetFloat("_SpecularHighlights", 1f);
            mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
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
            if (spec.name.StartsWith("M_SolarRealm") && mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", 0f);   // the camera is inside the shell

            // Water is the one TRANSPARENT surface: URP/Lit's alpha-blend setup, done here rather than
            // by hand in the Inspector (rule 4). Alpha comes from the spec's base colour.
            if (spec.name == "M_Water")
            {
                mat.SetFloat("_Surface", 1f);                      // SurfaceType.Transparent
                mat.SetFloat("_Blend", 0f);                        // BlendMode.Alpha
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
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
