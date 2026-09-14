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
    ///
    /// <para>The orb is three reads built as fixed geometry and toggled at runtime, never spawned: the
    /// carried orb (core + glass shell + one signature rig per <see cref="SpellOrbShape"/>), the page
    /// sigil (the same at 0.42 scale, for the riposte inscription) and eight rune bars (the carried
    /// queue). <c>SpellbookVisual</c> is the single writer of every orb renderer's emission; nothing
    /// under the orb is named Tip*/Seg*/Float*, so no EnergyGlow or legacy tint path can touch it.</para>
    /// </summary>
    public static class SpellbookFactory
    {
        public const string PrefabPath = "Assets/Prefabs/VM_Spellbook.prefab";
        public const string ShellShaderName = "VibeGame1/Spell Orb Shell";
        public const string ShellMaterialPath = "Assets/Materials/M_SpellOrbShell.mat";
        /// <summary>The shell's structural output cap. 1.0: glass carries hue and cannot bloom.</summary>
        public const float ShellPeakCap = 1.0f;
        /// <summary>The page sigil is a miniature of the carried orb.</summary>
        public const float SigilScale = 0.42f;
        public const int RuneCount = 8;
        /// <summary>Every rig, in <see cref="SpellOrbShape"/> order minus None. Both orbs carry all eight.</summary>
        public static readonly SpellOrbShape[] Shapes =
        {
            SpellOrbShape.Crescent, SpellOrbShape.FanRings, SpellOrbShape.DiamondSeal, SpellOrbShape.SwordGlyph,
            SpellOrbShape.Molten, SpellOrbShape.SkullMist, SpellOrbShape.NeedleArcs, SpellOrbShape.VoidRim,
        };

        [MenuItem("VibeGame1/4c. Build Spellbook Visual")]
        public static void CreateAll()
        {
            Material core = MaterialAt("Assets/Materials/M_SpellbookLeather.mat");
            Material page = MaterialAt("Assets/Materials/M_SpellbookPage.mat");
            Material trim = MaterialAt("Assets/Materials/M_GauntletTrim.mat");
            Material energy = MaterialAt("Assets/Materials/M_Energy.mat");
            Material itemGlow = MaterialAt("Assets/Materials/M_Item.mat");
            Material shell = ShellMaterial();

            var root = new GameObject("VM_Spellbook");
            var visual = root.AddComponent<SpellbookVisual>();

            // The complete silhouette stays camera-left. None of these roots may migrate through x=0:
            // at the serialized OffhandViewmodel rest x=-0.34 the book remains outside the aiming lane.
            Transform book = Empty("BookRoot", root.transform, new Vector3(-0.070f, 0.020f, 0.005f));
            book.localRotation = Quaternion.Euler(42f, -6f, -16f);
            visual.bookRoot = book;
            visual.castBookOffset = new Vector3(0.034f, 0.018f, 0.054f);
            visual.castBookEuler = new Vector3(-8f, 10f, -4f);

            // The hand grips the spine from below. Grip* is intentional: OffhandViewmodel's existing
            // hand solver already understands this exact naming contract.
            visual.gripAnchor = Empty("GripBook", book, new Vector3(0f, -0.168f, 0.090f));
            Empty("GripPalm", visual.gripAnchor, Vector3.zero);

            // Broad opened covers: mass first, then the thin pale pages and restrained metal corners.
            Prim(PrimitiveType.Cube, "CoverLeft", book, new Vector3(-0.105f, 0f, 0.022f),
                new Vector3(0.180f, 0.275f, 0.032f), core, new Vector3(0f, -32f, 0f));
            Prim(PrimitiveType.Cube, "CoverRight", book, new Vector3(0.105f, 0f, 0.022f),
                new Vector3(0.180f, 0.275f, 0.032f), core, new Vector3(0f, 32f, 0f));
            Prim(PrimitiveType.Cube, "Spine", book, new Vector3(0f, 0f, 0.018f),
                new Vector3(0.030f, 0.286f, 0.048f), trim, Vector3.zero);

            for (int side = -1; side <= 1; side += 2)
            {
                Prim(PrimitiveType.Cube, side < 0 ? "CornerLTop" : "CornerRTop", book,
                    new Vector3(side * 0.178f, 0.125f, -0.003f), new Vector3(0.028f, 0.027f, 0.018f), trim, Vector3.zero);
                Prim(PrimitiveType.Cube, side < 0 ? "CornerLBottom" : "CornerRBottom", book,
                    new Vector3(side * 0.178f, -0.125f, -0.003f), new Vector3(0.028f, 0.027f, 0.018f), trim, Vector3.zero);
            }
            Prim(PrimitiveType.Cube, "RuneSpine", book, new Vector3(0f, 0.010f, -0.010f),
                new Vector3(0.010f, 0.082f, 0.008f), energy, Vector3.zero);
            Prim(PrimitiveType.Cube, "PageGutter", book, new Vector3(0f, 0f, -0.057f),
                new Vector3(0.012f, 0.250f, 0.010f), trim, Vector3.zero);

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
                    Vector3 euler = new Vector3(0f, side * Mathf.Lerp(28f, 16f, t), side * Mathf.Lerp(-3f, 3f, t));
                    pivot.localRotation = Quaternion.Euler(euler);
                    pagePivots[pageIndex] = pivot;
                    pageEuler[pageIndex] = euler;
                    Prim(PrimitiveType.Cube, "PageLeaf_" + pageIndex, pivot,
                        new Vector3(side * 0.082f, 0f, -0.004f), new Vector3(0.155f, 0.235f, 0.007f), page, Vector3.zero);
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
            visual.pageFlutterDegrees = 9f;
            visual.pageFlutterSpeed = 2.55f;

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

            // ---- The carried orb: what E will cast ------------------------------------------------
            visual.orbAnchor = Empty("OrbAnchor", book, new Vector3(0f, 0.015f, -0.115f));
            visual.castOrigin = Empty("CastOrigin", visual.orbAnchor, new Vector3(0f, 0f, 0.20f));
            // Core 0.052 inside a 0.096 glass shell: the core is the hot channel (the one part allowed
            // over bloom), the shell carries the hue at <= 1.0 and the rigs sit on and around it.
            visual.carriedOrb = BuildOrbRig("Carried", visual.orbAnchor, 1f, 0.052f, 0.096f, itemGlow, shell, energy, core);

            // Eight rune bars on a ring in the page plane: one per carried slot, the front one brightest.
            // Named SpellRune*, not TipSpellRune*: SpellbookVisual is their only writer.
            visual.runeRing = Empty("RuneRing", visual.orbAnchor, Vector3.zero);
            const float runeRadius = 0.105f;
            var runes = new Renderer[RuneCount];
            for (int i = 0; i < RuneCount; i++)
            {
                float angle = i * 360f / RuneCount;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 runePosition = new Vector3(Mathf.Cos(radians) * runeRadius, Mathf.Sin(radians) * runeRadius, 0.003f);
                Transform runePivot = Empty("RunePivot" + i, visual.runeRing, runePosition);
                GameObject rune = Prim(PrimitiveType.Cube, "SpellRune" + i, runePivot, Vector3.zero,
                    new Vector3(0.043f, 0.009f, 0.007f), energy, new Vector3(0f, 0f, angle + 90f));
                runes[i] = rune.GetComponent<Renderer>();
            }
            visual.runeRenderers = runes;
            visual.runeSpinDegreesPerSecond = 10f;

            // ---- The page sigil: the selected riposte inscription ---------------------------------
            // Hovers over the lower-left page, clear of the aim lane, the loose leaves and the orb: at
            // planar distance 0.141 from the orb centre it sits outside the 0.105 rune ring with 0.016
            // to spare past its own 0.020 shell radius, so no rune bar ever crosses it on screen. A
            // miniature of the carried orb so the same rig language reads on both.
            Transform sigilAnchor = Empty("InscriptionAnchor", book, new Vector3(-0.100f, -0.085f, -0.060f));
            visual.inscriptionSigil = BuildOrbRig("Inscription", sigilAnchor, SigilScale,
                0.052f * SigilScale, 0.096f * SigilScale, itemGlow, shell, energy, core);

            visual.orbBobMetres = 0.028f;
            visual.orbBobSpeed = 2.6f;
            visual.orbPulseScale = 0.08f;
            visual.emptyOrbColor = new Color(0.45f, 0.35f, 0.72f, 1f);
            visual.emptyRuneColor = new Color(0.72f, 0.66f, 0.52f, 1f);
            visual.emptyProfile = new SpellOrbProfile
            {
                shape = SpellOrbShape.None,
                detail = new Color(0.62f, 0.56f, 0.80f, 1f),
                spinDegreesPerSecond = 0f, motionRate = 0.5f, motionAmplitude = 0.3f,
                shellRim = 2.4f, shellSwirl = 0.22f, shellFlow = 0f, shellWobble = 0f, shellDark = 0f,
            };

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

        /// <summary>Core + glass shell + all eight rigs (inactive) under one anchor.</summary>
        static SpellbookVisual.OrbRig BuildOrbRig(string label, Transform anchor, float scale, float coreSize,
            float shellSize, Material coreMat, Material shellMat, Material energy, Material dark)
        {
            var rig = new SpellbookVisual.OrbRig { anchor = anchor, scale = scale };
            GameObject core = Prim(PrimitiveType.Sphere, label + "OrbCore", anchor, Vector3.zero,
                Vector3.one * coreSize, coreMat, Vector3.zero);
            rig.core = core.GetComponent<Renderer>();
            GameObject shell = Prim(PrimitiveType.Sphere, label + "OrbShell", anchor, Vector3.zero,
                Vector3.one * shellSize, shellMat, Vector3.zero);
            rig.shell = shell.GetComponent<Renderer>();
            rig.shapes = new SpellbookVisual.ShapeRig[Shapes.Length];
            for (int i = 0; i < Shapes.Length; i++)
                rig.shapes[i] = BuildShape(Shapes[i], anchor, scale, energy, shellMat, dark);
            return rig;
        }

        /// <summary>
        /// One signature rig. THE PART LAYOUT IS THE CONTRACT <c>SpellbookVisual.AnimateShape</c> READS:
        ///   Crescent    parts[0] orbit pivot carrying the arc, parts[1] the barb. renderers: all.
        ///   FanRings    parts[0] flat ring pivot, parts[1] tilted ring pivot. renderers: all blades.
        ///   DiamondSeal parts[0] gem pivot (unit scale, beats), parts[1] seal plate. renderers[0] gem, rest plate/studs.
        ///   SwordGlyph  parts[0] orbit pivot, parts[1] sword pivot (tumbles). renderers: all.
        ///   Molten      parts[i] ember pivots (unit scale), renderers[i] their cubes.
        ///   SkullMist   parts[0..2] wisp pivots (shell material, renderers[0..2]), parts[3..5] sockets and jaw (dark).
        ///   NeedleArcs  parts[i] needle pivots (unit scale), renderers[i] their needles.
        ///   VoidRim     parts[i] spine pivots (unit scale), renderers[i] their spines.
        /// Rigs are authored with local +Y as vertical; the runtime maps +Y onto world up.
        /// </summary>
        static SpellbookVisual.ShapeRig BuildShape(SpellOrbShape shape, Transform anchor, float s,
            Material energy, Material shellMat, Material dark)
        {
            Transform root = Empty("Sig_" + shape, anchor, Vector3.zero);
            var parts = new List<Transform>();
            var rends = new List<Renderer>();
            switch (shape)
            {
                case SpellOrbShape.Crescent:
                {
                    // Six tapered segments on a 200 degree arc of radius 0.064, plus a barb at the leading tip.
                    Transform pivot = Empty("CrescentPivot", root, Vector3.zero);
                    parts.Add(pivot);
                    const int segs = 6;
                    for (int i = 0; i < segs; i++)
                    {
                        float t = (i / (float)(segs - 1)) * 2f - 1f;
                        float angle = t * 100f;
                        float rad = angle * Mathf.Deg2Rad;
                        float taper = 1f - Mathf.Abs(t) * 0.55f;
                        GameObject seg = Prim(PrimitiveType.Cube, "CrescentSeg" + i, pivot,
                            new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * (0.064f * s),
                            new Vector3(0.020f, 0.0045f * taper, 0.0065f * taper) * s, energy, new Vector3(0f, -angle, 0f));
                        rends.Add(seg.GetComponent<Renderer>());
                    }
                    float tipRad = 100f * Mathf.Deg2Rad;
                    Transform barbPivot = Empty("BarbPivot", pivot, new Vector3(Mathf.Cos(tipRad), 0f, Mathf.Sin(tipRad)) * (0.064f * s));
                    barbPivot.localRotation = Quaternion.Euler(0f, -100f, 0f);
                    parts.Add(barbPivot);
                    GameObject barb = Prim(PrimitiveType.Cube, "Barb", barbPivot, new Vector3(0.007f, 0.004f, 0f) * s,
                        new Vector3(0.016f, 0.003f, 0.003f) * s, energy, Vector3.zero);
                    rends.Add(barb.GetComponent<Renderer>());
                    break;
                }
                case SpellOrbShape.FanRings:
                {
                    // Two six-blade rings at radius 0.068, blades pitched 28 degrees so the spin reads as a fan.
                    for (int ring = 0; ring < 2; ring++)
                    {
                        Transform pivot = Empty(ring == 0 ? "RingFlat" : "RingTilted", root, Vector3.zero);
                        parts.Add(pivot);
                        const int blades = 6;
                        for (int i = 0; i < blades; i++)
                        {
                            float angle = i * 360f / blades + ring * 30f;
                            float rad = angle * Mathf.Deg2Rad;
                            GameObject blade = Prim(PrimitiveType.Cube, "Blade" + ring + "_" + i, pivot,
                                new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * (0.068f * s),
                                new Vector3(0.022f, 0.003f, 0.007f) * s, energy, new Vector3(28f, -angle, 0f));
                            rends.Add(blade.GetComponent<Renderer>());
                        }
                    }
                    break;
                }
                case SpellOrbShape.DiamondSeal:
                {
                    // A corner-up cube stretched 1.6x vertically reads as a cut gem; under it a diamond
                    // plate with four studs. The gem pivot is unit scale so the heartbeat can scale it.
                    Transform gemPivot = Empty("GemPivot", root, new Vector3(0f, 0.058f, 0f) * s);
                    parts.Add(gemPivot);
                    GameObject gem = Prim(PrimitiveType.Cube, "Gem", gemPivot, Vector3.zero,
                        new Vector3(0.026f, 0.042f, 0.026f) * s, energy, new Vector3(45f, 0f, 45f));
                    rends.Add(gem.GetComponent<Renderer>());
                    Transform plate = Empty("SealPlate", root, new Vector3(0f, -0.050f, 0f) * s);
                    parts.Add(plate);
                    GameObject plateCube = Prim(PrimitiveType.Cube, "Plate", plate, Vector3.zero,
                        new Vector3(0.090f, 0.002f, 0.090f) * s, energy, new Vector3(0f, 45f, 0f));
                    rends.Add(plateCube.GetComponent<Renderer>());
                    for (int i = 0; i < 4; i++)
                    {
                        float rad = (i * 90f + 45f) * Mathf.Deg2Rad;
                        GameObject stud = Prim(PrimitiveType.Cube, "Stud" + i, plate,
                            new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * (0.0636f * s),
                            Vector3.one * (0.007f * s), energy, Vector3.zero);
                        rends.Add(stud.GetComponent<Renderer>());
                    }
                    break;
                }
                case SpellOrbShape.SwordGlyph:
                {
                    // A 6.5 cm sword on a slow orbit, tumbling end over end about its radial axis.
                    Transform orbit = Empty("SwordOrbit", root, Vector3.zero);
                    parts.Add(orbit);
                    Transform sword = Empty("SwordPivot", orbit, new Vector3(0.060f, 0f, 0f) * s);
                    parts.Add(sword);
                    rends.Add(Prim(PrimitiveType.Cube, "Blade", sword, new Vector3(0f, 0.020f, 0f) * s,
                        new Vector3(0.007f, 0.062f, 0.0025f) * s, energy, Vector3.zero).GetComponent<Renderer>());
                    rends.Add(Prim(PrimitiveType.Cube, "Guard", sword, new Vector3(0f, -0.012f, 0f) * s,
                        new Vector3(0.026f, 0.004f, 0.004f) * s, energy, Vector3.zero).GetComponent<Renderer>());
                    rends.Add(Prim(PrimitiveType.Cube, "Grip", sword, new Vector3(0f, -0.025f, 0f) * s,
                        new Vector3(0.005f, 0.020f, 0.004f) * s, energy, Vector3.zero).GetComponent<Renderer>());
                    rends.Add(Prim(PrimitiveType.Cube, "Pommel", sword, new Vector3(0f, -0.037f, 0f) * s,
                        new Vector3(0.008f, 0.006f, 0.006f) * s, energy, Vector3.zero).GetComponent<Renderer>());
                    break;
                }
                case SpellOrbShape.Molten:
                {
                    // Six embers, 5.5 mm, rising through the shell on staggered loops.
                    for (int i = 0; i < 6; i++)
                    {
                        Transform pivot = Empty("Ember" + i, root, Vector3.zero);
                        parts.Add(pivot);
                        rends.Add(Prim(PrimitiveType.Cube, "EmberCube", pivot, Vector3.zero,
                            Vector3.one * (0.0055f * s), energy, new Vector3(i * 23f, i * 41f, 0f)).GetComponent<Renderer>());
                    }
                    break;
                }
                case SpellOrbShape.SkullMist:
                {
                    // Three soft wisps in the shell material sink out of the core; two dark sockets and a
                    // jaw on the reader-facing side (-Z) of the core make the skull.
                    for (int i = 0; i < 3; i++)
                    {
                        Transform pivot = Empty("Wisp" + i, root, Vector3.zero);
                        parts.Add(pivot);
                        rends.Add(Prim(PrimitiveType.Sphere, "WispBlob", pivot, Vector3.zero,
                            new Vector3(0.026f, 0.018f, 0.026f) * s, shellMat, Vector3.zero).GetComponent<Renderer>());
                    }
                    Transform socketL = Empty("SocketL", root, new Vector3(-0.009f, 0.004f, -0.0245f) * s);
                    Transform socketR = Empty("SocketR", root, new Vector3(0.009f, 0.004f, -0.0245f) * s);
                    Transform jaw = Empty("Jaw", root, new Vector3(0f, -0.010f, -0.0235f) * s);
                    parts.Add(socketL); parts.Add(socketR); parts.Add(jaw);
                    rends.Add(Prim(PrimitiveType.Cube, "SocketCube", socketL, Vector3.zero,
                        new Vector3(0.011f, 0.012f, 0.006f) * s, dark, Vector3.zero).GetComponent<Renderer>());
                    rends.Add(Prim(PrimitiveType.Cube, "SocketCube", socketR, Vector3.zero,
                        new Vector3(0.011f, 0.012f, 0.006f) * s, dark, Vector3.zero).GetComponent<Renderer>());
                    rends.Add(Prim(PrimitiveType.Cube, "JawCube", jaw, Vector3.zero,
                        new Vector3(0.014f, 0.003f, 0.005f) * s, dark, Vector3.zero).GetComponent<Renderer>());
                    break;
                }
                case SpellOrbShape.NeedleArcs:
                {
                    // Four 5 cm needles that re-strike tangent to the core.
                    for (int i = 0; i < 4; i++)
                    {
                        Transform pivot = Empty("Needle" + i, root, Vector3.zero);
                        parts.Add(pivot);
                        rends.Add(Prim(PrimitiveType.Cube, "NeedleCube", pivot, Vector3.zero,
                            new Vector3(0.0025f, 0.050f, 0.0025f) * s, energy, Vector3.zero).GetComponent<Renderer>());
                    }
                    break;
                }
                case SpellOrbShape.VoidRim:
                {
                    // Eight radial spines falling inward from a slowly turning rim.
                    for (int i = 0; i < 8; i++)
                    {
                        Transform pivot = Empty("Spine" + i, root, Vector3.zero);
                        parts.Add(pivot);
                        rends.Add(Prim(PrimitiveType.Cube, "SpineCube", pivot, Vector3.zero,
                            new Vector3(0.003f, 0.014f, 0.003f) * s, energy, Vector3.zero).GetComponent<Renderer>());
                    }
                    break;
                }
            }
            root.gameObject.SetActive(false);
            return new SpellbookVisual.ShapeRig { shape = shape, root = root, parts = parts.ToArray(), renderers = rends.ToArray() };
        }

        /// <summary>The glass shell material, regenerated here so the whole book is one generator's output.</summary>
        static Material ShellMaterial()
        {
            Shader shader = Shader.Find(ShellShaderName);
            if (shader == null)
            {
                Debug.LogError("[SpellbookFactory] Missing shader " + ShellShaderName + " (Assets/Shaders/SpellOrbShell.shader)");
                return null;
            }
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(ShellMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, ShellMaterialPath);
            }
            else mat.shader = shader;
            mat.SetColor("_RimColor", new Color(0.62f, 0.56f, 0.80f, 1f));
            mat.SetColor("_SwirlColor", new Color(0.62f, 0.56f, 0.80f, 1f));
            mat.SetFloat("_RimPower", 2.4f);
            mat.SetFloat("_RimStrength", 0.85f);
            mat.SetFloat("_SwirlStrength", 0.22f);
            mat.SetFloat("_SwirlScale", 7f);
            mat.SetFloat("_SwirlSpeed", 0.8f);
            mat.SetFloat("_Flow", 0f);
            mat.SetFloat("_Wobble", 0f);
            mat.SetFloat("_Dark", 0f);
            mat.SetFloat("_Opacity", 1f);
            mat.SetFloat("_Charge", 0f);
            mat.SetFloat("_PeakCap", ShellPeakCap);
            EditorUtility.SetDirty(mat);
            return mat;
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
