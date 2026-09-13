using NUnit.Framework;
using UnityEngine;

namespace VibeGame1.Tests
{
    public class PlayerItemsSelectionTests
    {
        [Test]
        public void CycleSelection_WrapsAndChangesTheSpellThatEWillCast()
        {
            GameObject go = new GameObject("PlayerItemsSelectionTest");
            PlayerItems items = go.AddComponent<PlayerItems>();
            ItemData hook = ScriptableObject.CreateInstance<ItemData>();
            ItemData rebound = ScriptableObject.CreateInstance<ItemData>();
            ItemData sigil = ScriptableObject.CreateInstance<ItemData>();
            hook.displayName = "Hook"; rebound.displayName = "Rebound"; sigil.displayName = "Sigil";
            try
            {
                Assert.IsTrue(items.TryPickup(hook));
                Assert.IsTrue(items.TryPickup(rebound));
                Assert.IsTrue(items.TryPickup(sigil));
                Assert.AreSame(hook, items.Current);

                Assert.IsTrue(items.CycleSelection(1));
                Assert.AreSame(rebound, items.Current);
                Assert.IsTrue(items.CycleSelection(1));
                Assert.AreSame(sigil, items.Current);
                Assert.IsTrue(items.CycleSelection(1));
                Assert.AreSame(hook, items.Current, "next must wrap");
                Assert.IsTrue(items.CycleSelection(-1));
                Assert.AreSame(sigil, items.Current, "previous must wrap");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(hook);
                Object.DestroyImmediate(rebound);
                Object.DestroyImmediate(sigil);
            }
        }
    }
}
