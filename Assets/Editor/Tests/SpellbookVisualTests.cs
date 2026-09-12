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
            Assert.AreEqual(10, book.pagePivots.Length, "five leaves per open half gives the book its readable body");
            Assert.AreEqual(book.pagePivots.Length, book.pageRestEuler.Length, "each page needs a cached authored rest pose");
            Assert.AreEqual(3, book.floatingPagePivots.Length, "the persistent loose leaves are the page-flow layer");
            Assert.AreEqual(9, book.orbRenderers.Length, "core + eight-rune halo are fixed generated geometry, not a runtime effect");
        }

        [Test]
        public void AnchorsAndLoosePages_StayOutsideTheAimingLane()
        {
            SpellbookVisual book = NewInstance();
            try
            {
                Assert.Less(book.orbAnchor.localPosition.x, -0.03f, "orb crosses the book-centred aiming lane");
                Vector3 castInBook = book.bookRoot.InverseTransformPoint(book.castOrigin.position);
                Assert.Less(castInBook.x, -0.03f, "cast origin crosses the book-centred aiming lane");
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
        public void OrbEmission_IsCappedBelowCombatTellBloom()
        {
            Color result = SpellbookVisual.OrbEmission(new Color(4f, 1f, 0.5f, 1f));
            float peak = Mathf.Max(result.r, Mathf.Max(result.g, result.b));
            Assert.AreEqual(SpellbookVisual.OrbEmissionPeak, peak, 0.0001f);
            Assert.Less(peak, 1.05f, "carried orb must not compete with a projectile/parry tell");

            Color castResult = SpellbookVisual.OrbEmission(new Color(4f, 1f, 0.5f, 1f), 1.18f);
            float castPeak = Mathf.Max(castResult.r, Mathf.Max(castResult.g, castResult.b));
            Assert.LessOrEqual(castPeak, SpellbookVisual.OrbEmissionPeak,
                "the cast pose may saturate the resting cap, never become a bloom exception");
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
