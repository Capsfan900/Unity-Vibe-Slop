using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Locks the generated spellbook's framing, its persistent-state contract, and — since the 2026-09-13
    /// orb pass — the three reads the orb makes and their light budget. Every number asserted here is
    /// read from the SHIPPED prefab and the SHIPPED item/wand assets, never from a C# default.
    /// </summary>
    public class SpellbookVisualTests
    {
        const string ItemsDir = "Assets/Data/Items";
        const string WandsDir = "Assets/Data/Wands";
        const float BloomThreshold = 1.05f;
        static readonly string[] ShippedItems = { "Grapple", "Rebound", "DeflectSigil", "BladeThrow" };
        static readonly string[] ShippedWands = { "Emberlance", "Gravecall", "Stormneedle", "Voidspine" };

        static GameObject Prefab => AssetDatabase.LoadAssetAtPath<GameObject>(SpellbookFactory.PrefabPath);
        static ItemData Item(string n) => AssetDatabase.LoadAssetAtPath<ItemData>(ItemsDir + "/" + n + ".asset");
        static WandData Wand(string n) => AssetDatabase.LoadAssetAtPath<WandData>(WandsDir + "/" + n + ".asset");

        static SpellbookVisual NewInstance()
        {
            Assert.IsNotNull(Prefab, "VM_Spellbook.prefab missing — run VibeGame1/4c. Build Spellbook Visual");
            return Object.Instantiate(Prefab).GetComponent<SpellbookVisual>();
        }

        /// <summary>Every shipped spell colour, items then inscriptions, with its name.</summary>
        static List<KeyValuePair<string, Color>> ShippedColours()
        {
            var list = new List<KeyValuePair<string, Color>>();
            foreach (string n in ShippedItems)
            {
                ItemData i = Item(n);
                Assert.IsNotNull(i, n + ".asset missing — run VibeGame1/3. Create Data");
                list.Add(new KeyValuePair<string, Color>(n, i.color));
            }
            foreach (string n in ShippedWands)
            {
                WandData w = Wand(n);
                Assert.IsNotNull(w, n + ".asset missing — run VibeGame1/3b. Create Wands");
                list.Add(new KeyValuePair<string, Color>(n, w.color));
            }
            return list;
        }

        static List<KeyValuePair<string, SpellOrbProfile>> ShippedProfiles()
        {
            var list = new List<KeyValuePair<string, SpellOrbProfile>>();
            foreach (string n in ShippedItems) list.Add(new KeyValuePair<string, SpellOrbProfile>(n, Item(n).orb));
            foreach (string n in ShippedWands) list.Add(new KeyValuePair<string, SpellOrbProfile>(n, Wand(n).orb));
            return list;
        }

        // ---------------------------------------------------------------------------------------
        // The generated prefab
        // ---------------------------------------------------------------------------------------

        [Test]
        public void GeneratedPrefab_HasPersistentBookAndNamedHandCastAnchors()
        {
            Assert.IsNotNull(Prefab, "Run VibeGame1/4c. Build Spellbook Visual");
            SpellbookVisual book = Prefab.GetComponent<SpellbookVisual>();
            Assert.IsNotNull(book, "VM_Spellbook needs SpellbookVisual");
            Assert.IsNotNull(book.bookRoot, "BookRoot is the only posed book mass");
            Assert.IsNotNull(book.gripAnchor, "GripBook lets the existing hand solver close on the spine");
            Assert.IsTrue(book.gripAnchor.name.StartsWith("Grip"), "hand anchor must retain the Grip* contract");
            Assert.IsNotNull(book.castOrigin, "future item/spell casts need a named origin");
            Assert.IsNotNull(book.orbAnchor, "orb must be anchored rather than spawned per item update");
            Assert.Less(book.gripAnchor.localPosition.y, -0.15f,
                "the hand must grip below the page block instead of intersecting it");
            Assert.Greater(book.gripAnchor.localPosition.z, 0.075f,
                "the palm belongs under the cover, not inside the pages");
            Assert.Less(Mathf.Abs(book.gripAnchor.localPosition.x), 0.03f,
                "the palm should support the spine, not one loose corner");
            Assert.AreEqual(10, book.pagePivots.Length, "five leaves per open half gives the book its readable body");
            Assert.AreEqual(book.pagePivots.Length, book.pageRestEuler.Length, "each page needs a cached authored rest pose");
            Assert.AreEqual(3, book.floatingPagePivots.Length, "the persistent loose leaves are the page-flow layer");
        }

        [Test]
        public void GeneratedPrefab_CarriesThreeReads_OrbSigilAndRuneQueue()
        {
            SpellbookVisual book = Prefab.GetComponent<SpellbookVisual>();
            Assert.IsNotNull(book);
            AssertOrbRig(book.carriedOrb, "carried orb", 1f);
            AssertOrbRig(book.inscriptionSigil, "inscription sigil", SpellbookFactory.SigilScale);
            Assert.AreNotSame(book.carriedOrb.anchor, book.inscriptionSigil.anchor,
                "the carried spell and the inscription must be two reads on two anchors, not one orb wearing both");
            Assert.IsNotNull(book.runeRing, "the rune queue turns on its own pivot");
            Assert.AreEqual(SpellbookFactory.RuneCount, book.runeRenderers.Length,
                "eight rune bars: one per carried slot is fixed generated geometry, not a runtime effect");
            foreach (Renderer r in book.runeRenderers)
                Assert.IsFalse(r.name.StartsWith("Tip") || r.name.StartsWith("Seg") || r.name.StartsWith("Float"),
                    r.name + " would be collected by EnergyGlow — SpellbookVisual must be the ONLY writer of its emission");
            Assert.IsNull(book.GetComponentInChildren<EnergyGlow>(true),
                "no EnergyGlow under the book: a second writer on the orb's emission is the exact bug that once erased the parry cue");
        }

        static void AssertOrbRig(SpellbookVisual.OrbRig rig, string label, float scale)
        {
            Assert.IsNotNull(rig, label + " missing");
            Assert.IsNotNull(rig.anchor, label + " anchor");
            Assert.IsNotNull(rig.core, label + " core: the one hot channel");
            Assert.IsNotNull(rig.shell, label + " shell: the glass that carries hue without blooming");
            Assert.AreEqual(scale, rig.scale, 0.0001f, label + " scale");
            Assert.AreEqual(SpellbookFactory.ShellShaderName, rig.shell.sharedMaterial.shader.name,
                label + " shell must use the URP orb-shell shader (Standard renders magenta)");
            Assert.AreEqual(SpellbookFactory.Shapes.Length, rig.shapes.Length, label + " must carry every rig so any spell can show on it");
            var seen = new HashSet<SpellOrbShape>();
            foreach (SpellbookVisual.ShapeRig s in rig.shapes)
            {
                Assert.IsNotNull(s.root, label + " rig root for " + s.shape);
                Assert.IsFalse(s.root.gameObject.activeSelf, label + " rig " + s.shape + " must ship inactive; the runtime shows one");
                Assert.Greater(s.parts.Length, 0, label + " rig " + s.shape + " has no animated parts");
                Assert.Greater(s.renderers.Length, 0, label + " rig " + s.shape + " has no renderers");
                Assert.IsTrue(seen.Add(s.shape), label + " has two rigs for " + s.shape);
            }
            Assert.IsFalse(seen.Contains(SpellOrbShape.None), label + " must not build a rig for None");
            Assert.Greater(rig.shell.transform.localScale.x, rig.core.transform.localScale.x,
                label + ": the glass must enclose the core, or the two nest into one flat disc");
        }

        [Test]
        public void GeneratedPrefab_ReadsAsAnOpenUpturnedBookWithMagicOverItsPages()
        {
            SpellbookVisual book = NewInstance();
            try
            {
                Transform left = book.bookRoot.Find("CoverLeft");
                Transform right = book.bookRoot.Find("CoverRight");
                Transform gutter = book.bookRoot.Find("PageGutter");
                Transform orb = book.carriedOrb.core.transform;
                Assert.IsNotNull(left); Assert.IsNotNull(right); Assert.IsNotNull(gutter); Assert.IsNotNull(orb);
                Assert.Greater(Vector3.Dot(book.bookRoot.localRotation * Vector3.back, Vector3.up), 0.5f,
                    "the visible page normal must tilt upward like a book held for reading");
                Assert.Less(book.bookRoot.localEulerAngles.z > 180f
                    ? book.bookRoot.localEulerAngles.z - 360f : book.bookRoot.localEulerAngles.z, -10f,
                    "the open book should sit level in the palm instead of canting like a shield");
                Assert.Greater(Vector3.Angle(left.forward, right.forward), 50f,
                    "the two covers must read as visibly open rather than one flat figurine");
                Assert.Greater(left.localScale.y, 0.25f, "the held book is too small at gameplay FOV");
                Assert.Less(Mathf.Abs(book.orbAnchor.localPosition.y), 0.11f,
                    "the spell must hover over the page field, not beyond the book's top edge");
                Assert.Less(book.orbAnchor.localPosition.z, -0.08f,
                    "the spell must hover above the visible page surface, not hide behind the leaves");
                Assert.Greater(orb.localScale.x, 0.04f, "the hot core must remain readable without staring at the hand");
                Assert.Less(orb.localScale.x, 0.07f, "the solid core must not read as an opaque coin on the pages");
                Assert.Greater(book.carriedOrb.shell.transform.localScale.x, 0.08f,
                    "the glass shell is the orb's silhouette; under 8 cm it is a highlight, not a sphere");
                Assert.Greater(book.orbBobMetres, 0.02f, "the selected spell's hover must be visible in motion");
                Assert.GreaterOrEqual(book.orbPulseScale, 0.06f, "the selected spell needs a readable magical breath");
                Material page = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_SpellbookPage.mat");
                Assert.IsNotNull(page);
                Assert.Greater(page.GetColor("_EmissionColor").maxColorComponent, 0.15f,
                    "upturned parchment must remain readable under the level's dark sky lighting");
            }
            finally { Object.DestroyImmediate(book.gameObject); }
        }

        [Test]
        public void AnchorsAndLoosePages_StayOutsideTheAimingLane()
        {
            SpellbookVisual book = NewInstance();
            try
            {
                Assert.Less(book.orbAnchor.position.x, -0.03f, "orb crosses the prefab's aiming lane");
                Assert.Less(book.castOrigin.position.x, -0.03f, "cast origin crosses the prefab's aiming lane");
                Assert.Less(book.inscriptionSigil.anchor.position.x, -0.03f, "the page sigil crosses the aiming lane");
                Assert.Greater(Vector3.Distance(book.inscriptionSigil.anchor.position, book.orbAnchor.position), 0.08f,
                    "the sigil must sit clear of the orb or the two reads merge into one");
                foreach (Vector3 origin in book.floatingPageOrigins)
                    Assert.Less(origin.x, -0.03f, "a floating page is authored through the aiming lane");
            }
            finally { Object.DestroyImmediate(book.gameObject); }
        }

        // ---------------------------------------------------------------------------------------
        // The persistent-state contract
        // ---------------------------------------------------------------------------------------

        [Test]
        public void FrontFifoItem_TakesColourPriorityOverPermanentSpell()
        {
            WandData spell = ScriptableObject.CreateInstance<WandData>();
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            try
            {
                spell.color = new Color(0.2f, 0.5f, 0.9f, 1f);
                item.color = new Color(0.9f, 0.2f, 0.45f, 1f);
                Color fallback = Color.magenta;
                Assert.AreEqual(item.color, SpellbookVisual.ResolveDisplayColor(item, spell, fallback));
                Assert.AreEqual(spell.color, SpellbookVisual.ResolveDisplayColor(null, spell, fallback));
                Assert.AreEqual(fallback, SpellbookVisual.ResolveDisplayColor(null, null, fallback));
            }
            finally
            {
                Object.DestroyImmediate(spell);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void ItemAndCastUpdates_NeverReplaceOrRescaleTheBook()
        {
            SpellbookVisual book = NewInstance();
            WandData spell = ScriptableObject.CreateInstance<WandData>();
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            try
            {
                spell.color = Color.cyan;
                item.color = Color.green;
                item.orb.shape = SpellOrbShape.Crescent;
                GameObject root = book.gameObject;
                int childCount = book.transform.childCount;
                int totalCount = book.GetComponentsInChildren<Transform>(true).Length;
                Vector3 rootScale = book.transform.localScale;
                Vector3 bodyScale = book.bookRoot.localScale;

                book.SetSelectedSpell(spell);
                book.SetFrontItem(item);
                book.CaptureAcceptedCast(item);
                Assert.AreEqual(item.color, book.CapturedCastColor, "accepted cast must retain the spent item's hue");
                book.PlayCastPose();
                book.SetFrontItem(null); // FIFO advances after accepted use; cast colour must not snap.
                Assert.AreEqual(item.color, book.CapturedCastColor);
                book.CancelAndRestore();

                Assert.AreSame(root, book.gameObject);
                Assert.AreEqual(childCount, book.transform.childCount);
                Assert.AreEqual(totalCount, book.GetComponentsInChildren<Transform>(true).Length,
                    "showing a rig must toggle fixed geometry, never create any");
                Assert.AreEqual(rootScale, book.transform.localScale);
                Assert.AreEqual(bodyScale, book.bookRoot.localScale);
                Assert.AreEqual(spell.color, book.DisplayColor, "after restore the permanent spell is the empty-FIFO fallback");
            }
            finally
            {
                Object.DestroyImmediate(book.gameObject);
                Object.DestroyImmediate(spell);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void ShowingASpell_ActivatesExactlyItsRigAndAnEmptyBookShowsNone()
        {
            SpellbookVisual book = NewInstance();
            ItemData hook = ScriptableObject.CreateInstance<ItemData>();
            WandData ember = ScriptableObject.CreateInstance<WandData>();
            try
            {
                hook.orb.shape = SpellOrbShape.Crescent;
                ember.orb.shape = SpellOrbShape.Molten;
                book.SetSelectedSpell(ember);
                book.SetFrontItem(hook);
                AssertShown(book.carriedOrb, SpellOrbShape.Crescent, "carried orb with the Hook in front");
                AssertShown(book.inscriptionSigil, SpellOrbShape.Molten, "page sigil with Emberlance inscribed");

                book.SetFrontItem(null);
                AssertShown(book.carriedOrb, SpellOrbShape.None, "carried orb with nothing carried: no silhouette, so a rig always means castable");
                AssertShown(book.inscriptionSigil, SpellOrbShape.Molten, "the inscription does not change when the queue empties");

                // The riposte: the book casts its inscription, so the orb wears the inscription's rig.
                book.CaptureAcceptedCast(null);
                book.PlayCastPose();
                AssertShown(book.carriedOrb, SpellOrbShape.Molten, "during a riposte the orb shows the inscription being cast");
                book.CancelAndRestore();
                AssertShown(book.carriedOrb, SpellOrbShape.None, "after the riposte the empty read returns");
            }
            finally
            {
                Object.DestroyImmediate(book.gameObject);
                Object.DestroyImmediate(hook);
                Object.DestroyImmediate(ember);
            }
        }

        static void AssertShown(SpellbookVisual.OrbRig rig, SpellOrbShape expected, string context)
        {
            foreach (SpellbookVisual.ShapeRig s in rig.shapes)
                Assert.AreEqual(s.shape == expected, s.root.gameObject.activeSelf,
                    context + ": rig " + s.shape + " active=" + s.root.gameObject.activeSelf + ", expected shown=" + expected);
        }

        // ---------------------------------------------------------------------------------------
        // The light budget: luminance, not max channel
        // ---------------------------------------------------------------------------------------

        [Test]
        public void OrbEmission_NormalisesByLuminanceSoGoldStaysGold()
        {
            foreach (var kv in ShippedColours())
            {
                Color e = SpellbookVisual.OrbEmission(kv.Value);
                float peak = e.maxColorComponent;
                float lum = SpellOrbProfile.Luminance(e);
                Assert.Greater(peak, BloomThreshold, kv.Key + " core peaks at " + peak.ToString("0.###") +
                    " — under the bloom threshold a castable spell reads as dull geometry");
                Assert.LessOrEqual(peak, SpellbookVisual.OrbEmissionPeak + 0.0001f, kv.Key + " core peaks at " +
                    peak.ToString("0.###") + ", over the carried-orb ceiling");
                Assert.LessOrEqual(lum, SpellbookVisual.OrbEmissionLuminance + 0.0001f, kv.Key + " luminance " + lum);
                // Either the luminance target is met exactly or the peak clamp is what bound — never both loose.
                Assert.IsTrue(Mathf.Abs(lum - SpellbookVisual.OrbEmissionLuminance) < 0.001f
                              || Mathf.Abs(peak - SpellbookVisual.OrbEmissionPeak) < 0.001f,
                    kv.Key + ": luminance " + lum.ToString("0.###") + " and peak " + peak.ToString("0.###") +
                    " — neither the luminance target nor the peak clamp is binding, so the colour is unbudgeted");
                // Hue survives: the ratio between the channels is the source's.
                Color src = kv.Value;
                Assert.AreEqual(src.r / Mathf.Max(0.0001f, src.maxColorComponent), e.r / Mathf.Max(0.0001f, e.maxColorComponent), 0.002f, kv.Key + " red ratio drifted");
                Assert.AreEqual(src.g / Mathf.Max(0.0001f, src.maxColorComponent), e.g / Mathf.Max(0.0001f, e.maxColorComponent), 0.002f, kv.Key + " green ratio drifted");
                Assert.AreEqual(src.b / Mathf.Max(0.0001f, src.maxColorComponent), e.b / Mathf.Max(0.0001f, e.maxColorComponent), 0.002f, kv.Key + " blue ratio drifted");
            }

            // The named symptom: max-channel normalisation put every channel of a pale gold over 1 and the
            // tonemapper rendered it white. Under luminance normalisation gold keeps a blue channel under 1.
            Color gold = SpellbookVisual.OrbEmission(Wand("Emberlance").color);
            Assert.Less(gold.b, 1f, "Emberlance's blue channel is " + gold.b.ToString("0.###") +
                " — with all three channels over 1 the tonemapper whitens it and gold stops being gold");
            Color amber = SpellbookVisual.OrbEmission(Item("BladeThrow").color);
            Assert.Less(amber.b, 1f, "Blade Throw's blue channel is " + amber.b.ToString("0.###") + " — amber would whiten");
        }

        [Test]
        public void OrbEmission_CastPeakNeverExceedsTheCeiling_AndAnEmptyBookNeverBlooms()
        {
            foreach (var kv in ShippedColours())
            {
                float castPeak = SpellbookVisual.OrbEmission(kv.Value, SpellbookVisual.OrbCastMultiplier).maxColorComponent;
                Assert.LessOrEqual(castPeak, SpellbookVisual.OrbEmissionPeak + 0.0001f,
                    kv.Key + " cast pose peaks at " + castPeak.ToString("0.###") + " — the cast may saturate the cap, never cross it");
                float emptyPeak = SpellbookVisual.OrbEmission(kv.Value, SpellbookVisual.OrbEmptyMultiplier).maxColorComponent;
                Assert.Less(emptyPeak, BloomThreshold, kv.Key + " as the empty-book ember peaks at " +
                    emptyPeak.ToString("0.###") + " — nothing carried must not bloom, or a dead orb reads as castable");
            }
            ItemData probe = ScriptableObject.CreateInstance<ItemData>();
            try
            {
                Assert.AreEqual(1f, SpellbookVisual.ResolveDisplayMultiplier(probe), 0.0001f);
                Assert.AreEqual(SpellbookVisual.OrbEmptyMultiplier, SpellbookVisual.ResolveDisplayMultiplier(null), 0.0001f);
            }
            finally { Object.DestroyImmediate(probe); }
        }

        [Test]
        public void SigilRunesAndDetail_AreLuminousButCannotBloom()
        {
            foreach (var kv in ShippedColours())
            {
                Assert.LessOrEqual(SpellbookVisual.SigilEmission(kv.Value, 1f).maxColorComponent, SpellbookVisual.DetailPeak + 0.0001f,
                    kv.Key + " page sigil at full charge crosses 1.0 — an inscription is not a tell");
                Assert.LessOrEqual(SpellbookVisual.RuneEmission(kv.Value, SpellbookVisual.RuneSelectedLuminance).maxColorComponent,
                    SpellbookVisual.DetailPeak + 0.0001f, kv.Key + " selected rune bar crosses 1.0");
            }
            foreach (var kv in ShippedProfiles())
                Assert.LessOrEqual(SpellOrbProfile.PeakNormalised(kv.Value.detail, SpellbookVisual.DetailPeak).maxColorComponent,
                    SpellbookVisual.DetailPeak + 0.0001f, kv.Key + " detail hue crosses the decoration cap");
            Assert.LessOrEqual(SpellbookFactory.ShellPeakCap, BloomThreshold, "the glass shell's structural cap must sit under bloom");
            Material shell = AssetDatabase.LoadAssetAtPath<Material>(SpellbookFactory.ShellMaterialPath);
            Assert.IsNotNull(shell, "M_SpellOrbShell.mat missing — run 4c. Build Spellbook Visual");
            Assert.AreEqual(SpellbookFactory.ShellShaderName, shell.shader.name);
            Assert.LessOrEqual(shell.GetFloat("_PeakCap"), BloomThreshold, "shipped shell material's _PeakCap is over bloom");
        }

        [Test]
        public void RuneQueue_LightsOnePerCarriedSpell_FrontBrightest()
        {
            Assert.AreEqual(SpellbookVisual.RuneSelectedLuminance, SpellbookVisual.RuneLuminance(0, 3), 0.0001f, "front slot");
            Assert.AreEqual(SpellbookVisual.RuneCarriedLuminance, SpellbookVisual.RuneLuminance(1, 3), 0.0001f, "second slot");
            Assert.AreEqual(SpellbookVisual.RuneCarriedLuminance, SpellbookVisual.RuneLuminance(2, 3), 0.0001f, "third slot");
            Assert.AreEqual(0f, SpellbookVisual.RuneLuminance(3, 3), 0.0001f, "an empty slot is dark");
            Assert.AreEqual(0f, SpellbookVisual.RuneLuminance(0, 0), 0.0001f, "nothing carried lights nothing");
            Assert.Greater(SpellbookVisual.RuneSelectedLuminance, SpellbookVisual.RuneCarriedLuminance,
                "the bar for the spell E will cast must be the brightest");
            Assert.Less(SpellbookVisual.RuneEmptyEmission, 0.3f, "empty bars are a dim bone rest, not a lit slot");
        }

        // ---------------------------------------------------------------------------------------
        // The shipped profiles: identity by shape and motion
        // ---------------------------------------------------------------------------------------

        [Test]
        public void EveryShippedSpell_HasItsOwnShape()
        {
            var seen = new Dictionary<SpellOrbShape, string>();
            foreach (var kv in ShippedProfiles())
            {
                Assert.IsNotNull(kv.Value, kv.Key + " has no orb profile");
                Assert.AreNotEqual(SpellOrbShape.None, kv.Value.shape, kv.Key + " ships with no orb shape — its only " +
                    "identity would be a hue, and Rebound/Gravecall and Sigil/Voidspine share hue families");
                Assert.IsFalse(seen.ContainsKey(kv.Value.shape), kv.Key + " and " + (seen.ContainsKey(kv.Value.shape) ? seen[kv.Value.shape] : "") +
                    " both wear " + kv.Value.shape + " — two spells with one silhouette");
                seen[kv.Value.shape] = kv.Key;
            }
            Assert.AreEqual(SpellbookFactory.Shapes.Length, seen.Count, "every built rig must be worn by exactly one shipped spell");
        }

        [Test]
        public void EveryShippedProfile_IdlesUnderTellAmplitude()
        {
            foreach (var kv in ShippedProfiles())
            {
                SpellOrbProfile p = kv.Value;
                Assert.LessOrEqual(p.spinDegreesPerSecond, SpellOrbProfile.MaxIdleSpinDegreesPerSecond,
                    kv.Key + " idles at " + p.spinDegreesPerSecond + " deg/s — in the parry quadrant that reads as a wind-up");
                Assert.GreaterOrEqual(p.spinDegreesPerSecond, 0f, kv.Key + " negative spin");
                Assert.Greater(p.motionRate, 0f, kv.Key + " motionRate 0 freezes its signature motion");
                Assert.LessOrEqual(p.motionRate, SpellOrbProfile.MaxIdleMotionRate, kv.Key + " motionRate " + p.motionRate + " Hz is a strobe");
                Assert.Greater(p.motionAmplitude, 0f, kv.Key + " amplitude 0 makes the rig a static ornament");
                Assert.LessOrEqual(p.motionAmplitude, 1f, kv.Key);
                Assert.GreaterOrEqual(p.shellRim, 0.5f, kv.Key + " shellRim under the shader's floor");
                Assert.IsTrue(p.shellSwirl >= 0f && p.shellSwirl <= 1f, kv.Key + " shellSwirl out of range");
                Assert.IsTrue(p.shellWobble >= 0f && p.shellWobble <= 1f, kv.Key + " shellWobble out of range");
                Assert.IsTrue(p.shellDark >= 0f && p.shellDark <= 1f, kv.Key + " shellDark out of range");
                Assert.AreEqual(1f, p.detail.a, 0.0001f, kv.Key + " detail alpha is not 1");
            }
        }

        [Test]
        public void TheShippedProfiles_KeepTheirNamedSignatures()
        {
            // The words in the plan, in assert form: each spell's motion is what its name promises.
            Assert.AreEqual(SpellOrbShape.Crescent, Item("Grapple").orb.shape);
            Assert.AreEqual(SpellOrbShape.FanRings, Item("Rebound").orb.shape);
            Assert.AreEqual(SpellOrbShape.DiamondSeal, Item("DeflectSigil").orb.shape);
            Assert.AreEqual(SpellOrbShape.SwordGlyph, Item("BladeThrow").orb.shape);
            Assert.AreEqual(SpellOrbShape.Molten, Wand("Emberlance").orb.shape);
            Assert.AreEqual(SpellOrbShape.SkullMist, Wand("Gravecall").orb.shape);
            Assert.AreEqual(SpellOrbShape.NeedleArcs, Wand("Stormneedle").orb.shape);
            Assert.AreEqual(SpellOrbShape.VoidRim, Wand("Voidspine").orb.shape);

            Assert.Greater(Item("Rebound").orb.spinDegreesPerSecond, Item("Grapple").orb.spinDegreesPerSecond,
                "the fan rings are the spin-up spell; they must out-spin the orbiting crescent");
            Assert.Greater(Wand("Emberlance").orb.shellFlow, 0f, "embers rise");
            Assert.Less(Wand("Gravecall").orb.shellFlow, 0f, "rot sinks — the one motion opposite to fire");
            Assert.Greater(Wand("Emberlance").orb.shellWobble, 0f, "fire is the only heat shimmer");
            foreach (string n in ShippedWands)
                if (n != "Emberlance") Assert.Less(Wand(n).orb.shellWobble, 0.3f, n + " wobbles like fire");
            Assert.Greater(Wand("Voidspine").orb.shellDark, 0.5f, "the void darkens its centre");
            foreach (var kv in ShippedProfiles())
                if (kv.Key != "Voidspine") Assert.AreEqual(0f, kv.Value.shellDark, 0.0001f, kv.Key + " darkens like the void");
            Assert.Greater(Wand("Stormneedle").orb.motionRate, 4f, "the needles crackle; anything under 4 Hz is a slow blink");
            foreach (var kv in ShippedProfiles())
                if (kv.Key != "Stormneedle") Assert.Less(kv.Value.motionRate, 2f, kv.Key + " runs at a crackle rate; only the storm may");
        }

        // ---------------------------------------------------------------------------------------
        // Source contracts
        // ---------------------------------------------------------------------------------------

        [Test]
        public void RuntimeVisual_DoesNotInstantiateOrCreateGeometryDuringUpdates()
        {
            string source = File.ReadAllText("Assets/Scripts/Feel/SpellbookVisual.cs");
            Assert.IsFalse(source.Contains("Instantiate("), "the persistent book must never instantiate on item/cast updates");
            Assert.IsFalse(source.Contains("new GameObject"), "the persistent book geometry belongs to SpellbookFactory");
            Assert.IsFalse(source.Contains("CreatePrimitive"), "the persistent book geometry belongs to SpellbookFactory");
        }

        [Test]
        public void TheWheel_AlwaysAnswers()
        {
            // PlayerItems.CycleSelection used to return silently under two spells, which reads as a dead
            // binding. The refusal now clicks and flashes the true count. Source-pinned: the method has
            // no scene-free seam and AudioManager is a runtime singleton.
            string source = File.ReadAllText("Assets/Scripts/Player/PlayerItems.cs");
            int start = source.IndexOf("public bool CycleSelection(int direction)");
            Assert.Greater(start, 0, "CycleSelection missing");
            string body = source.Substring(start, Mathf.Min(900, source.Length - start));
            Assert.IsTrue(body.Contains("\"NO SPELLS\""), "the empty wheel must flash NO SPELLS");
            Assert.IsTrue(body.Contains("\"ONE SPELL\""), "a one-spell wheel must not claim there are no spells");
            Assert.IsTrue(body.Contains("Sfx.Click"), "the refused wheel must click");
        }
    }
}
