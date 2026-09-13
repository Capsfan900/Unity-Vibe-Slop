using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>Locks the generated spellbook's framing and persistent-state contract.</summary>
    public class SpellbookVisualTests
    {
        static GameObject Prefab => AssetDatabase.LoadAssetAtPath<GameObject>(SpellbookFactory.PrefabPath);

        static SpellbookVisual NewInstance()
        {
            Assert.IsNotNull(Prefab, "VM_Spellbook.prefab missing — run VibeGame1/4c. Build Spellbook Visual");
            return Object.Instantiate(Prefab).GetComponent<SpellbookVisual>();
        }

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
            Assert.IsNotNull(book.orbGlow, "the selected spell must reuse the established weapon-energy treatment");
            Assert.Less(book.gripAnchor.localPosition.y, -0.15f,
                "the hand must grip below the page block instead of intersecting it");
            Assert.Greater(book.gripAnchor.localPosition.z, 0.075f,
                "the palm belongs under the cover, not inside the pages");
            Assert.Less(Mathf.Abs(book.gripAnchor.localPosition.x), 0.03f,
                "the palm should support the spine, not one loose corner");
            Assert.AreEqual(10, book.pagePivots.Length, "five leaves per open half gives the book its readable body");
            Assert.AreEqual(book.pagePivots.Length, book.pageRestEuler.Length, "each page needs a cached authored rest pose");
            Assert.AreEqual(3, book.floatingPagePivots.Length, "the persistent loose leaves are the page-flow layer");
            Assert.AreEqual(9, book.orbRenderers.Length, "core + eight-rune halo are fixed generated geometry, not a runtime effect");
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
                Transform orb = book.orbAnchor.Find("TipSpellOrbCore");
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
                Assert.Greater(orb.localScale.x, 0.065f, "the selected spell must remain readable without staring at the hand");
                Assert.Less(orb.localScale.x, 0.09f, "the solid core must not read as an opaque coin on the pages");
                Assert.Greater(book.orbBobMetres, 0.02f, "the selected spell's hover must be visible in motion");
                Assert.GreaterOrEqual(book.orbPulseScale, 0.06f, "the selected spell needs a readable magical breath");
                Assert.Greater(book.orbGlow.moteCount, 0, "the spell needs the same drifting energy flecks as glowing weapons");
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
                foreach (Vector3 origin in book.floatingPageOrigins)
                    Assert.Less(origin.x, -0.03f, "a floating page is authored through the aiming lane");
            }
            finally { Object.DestroyImmediate(book.gameObject); }
        }

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
                GameObject root = book.gameObject;
                int childCount = book.transform.childCount;
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
        public void OrbEmission_UsesTheFluorescentItemRange()
        {
            Color result = SpellbookVisual.OrbEmission(new Color(4f, 1f, 0.5f, 1f));
            float peak = Mathf.Max(result.r, Mathf.Max(result.g, result.b));
            Assert.AreEqual(SpellbookVisual.OrbEmissionPeak, peak, 0.0001f);
            Assert.GreaterOrEqual(peak, 1.8f, "the selected spell must bloom like a glowing pickup");
            Assert.LessOrEqual(peak, 2.4f, "the held spell should not become a full-screen light source");

            Color castResult = SpellbookVisual.OrbEmission(new Color(4f, 1f, 0.5f, 1f), 1.18f);
            float castPeak = Mathf.Max(castResult.r, Mathf.Max(castResult.g, castResult.b));
            Assert.LessOrEqual(castPeak, SpellbookVisual.OrbEmissionPeak,
                "the cast pose may saturate the authored carried-orb cap");
        }

        [Test]
        public void RuntimeVisual_DoesNotInstantiateOrCreateGeometryDuringUpdates()
        {
            string source = File.ReadAllText("Assets/Scripts/Feel/SpellbookVisual.cs");
            Assert.IsFalse(source.Contains("Instantiate("), "the persistent book must never instantiate on item/cast updates");
            Assert.IsFalse(source.Contains("new GameObject"), "the persistent book geometry belongs to SpellbookFactory");
            Assert.IsFalse(source.Contains("CreatePrimitive"), "the persistent book geometry belongs to SpellbookFactory");
        }
    }
}
