using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// StatusStripView is a display-only projection. These tests set its cached, already-observed values
    /// directly: no motor, projectile, or gameplay state is driven from the HUD.
    /// </summary>
    public class StatusStripTests
    {
        static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        GameObject root;
        ItemData item;

        [TearDown]
        public void TearDown()
        {
            StatusStripView.StatusEffectsVisible = true;
            if (item != null) Object.DestroyImmediate(item);
            if (root != null) Object.DestroyImmediate(root);
        }

        StatusStripView Strip()
        {
            root = new GameObject("StatusStripTest");
            var strip = root.AddComponent<StatusStripView>();
            // EditMode tests do not invoke MonoBehaviour.OnEnable. Register this enabled object directly
            // so the static setter's promised immediate rebuild is exercised without a fake frame.
            var f = typeof(StatusStripView).GetField("LiveViews", PrivateStatic);
            Assert.IsNotNull(f, "StatusStripView lost its live-view registry");
            var live = f.GetValue(null) as HashSet<StatusStripView>;
            Assert.IsNotNull(live, "StatusStripView live-view registry has the wrong type");
            live.Add(strip);
            return strip;
        }

        ItemData HeldItem()
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            item.displayName = "Test Hook";
            item.shortLabel = "HOOK";
            item.color = Color.white;
            return item;
        }

        static void Set(StatusStripView strip, string field, object value)
        {
            var f = typeof(StatusStripView).GetField(field, PrivateInstance);
            Assert.IsNotNull(f, field + " no longer exists; update this display-only fixture with the view");
            f.SetValue(strip, value);
        }

        static void Rebuild(StatusStripView strip)
        {
            var m = typeof(StatusStripView).GetMethod("Rebuild", PrivateInstance);
            Assert.IsNotNull(m, "StatusStripView must keep its one-label rebuild path");
            m.Invoke(strip, null);
        }

        static void SetRows(StatusStripView strip, ItemData[] held, int parrySurgeStacks)
        {
            Set(strip, "held", held);
            Set(strip, "shownSurgeTenths", -1);
            Set(strip, "shownParrySurgeStacks", parrySurgeStacks);
            Set(strip, "shownSpeedPct", 100);
            Set(strip, "shownGod", false);
        }

        [Test]
        public void ParrySurgeStacksProduceANamedRow_AndZeroDoesNot()
        {
            var strip = Strip();
            SetRows(strip, new ItemData[0], 3);
            Rebuild(strip);
            StringAssert.Contains("SPEED SURGE x3", strip.Text);
            Assert.AreEqual(1, strip.RowCount);

            SetRows(strip, new ItemData[0], 0);
            Rebuild(strip);
            StringAssert.DoesNotContain("SPEED SURGE", strip.Text);
            Assert.IsTrue(strip.IsEmpty);
        }

        [Test]
        public void HidingEffectsImmediatelyLeavesHeldItemsVisible()
        {
            var strip = Strip();
            SetRows(strip, new[] { HeldItem() }, 2);
            Rebuild(strip);
            StringAssert.Contains("TEST HOOK", strip.Text);
            StringAssert.Contains("SPEED SURGE x2", strip.Text);
            Assert.AreEqual(2, strip.RowCount);

            // The static setter must rebuild this enabled instance immediately, not wait for the next frame.
            StatusStripView.StatusEffectsVisible = false;
            StringAssert.Contains("TEST HOOK", strip.Text);
            StringAssert.DoesNotContain("SPEED SURGE", strip.Text);
            Assert.AreEqual(1, strip.RowCount);
        }

        [Test]
        public void RunRequirementIsPersistentAndIsNotAnEffectToggleRow()
        {
            var strip = Strip();
            SetRows(strip, new ItemData[0], 2);
            Set(strip, "shownRunSouls", 3425);
            Set(strip, "shownRequiredRunSouls", 3460);
            Set(strip, "shownRegularKills", 3);
            Set(strip, "shownRequiredRegularKills", 4);
            Set(strip, "shownCompletedSplits", 3);
            Set(strip, "shownSplitCount", 4);
            Rebuild(strip);
            StringAssert.Contains("RUN 3425/3460", strip.Text);
            StringAssert.Contains("FOES 3/4", strip.Text);
            StringAssert.Contains("SPLITS 3/4", strip.Text);
            StringAssert.Contains("SPEED SURGE x2", strip.Text);

            StatusStripView.StatusEffectsVisible = false;
            StringAssert.Contains("RUN 3425/3460", strip.Text);
            StringAssert.DoesNotContain("SPEED SURGE", strip.Text);
            Assert.AreEqual(1, strip.RowCount);
        }

        [Test]
        public void TestMenuToggleAndHudPrefabButtonAreWired()
        {
            var go = new GameObject("TestMenuToggle");
            try
            {
                var menu = go.AddComponent<TestMenu>();
                bool before = StatusStripView.StatusEffectsVisible;
                menu.ToggleStatusEffects();
                Assert.AreNotEqual(before, StatusStripView.StatusEffectsVisible);
            }
            finally { Object.DestroyImmediate(go); }

            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HUD.prefab");
            if (hud == null) Assert.Ignore("HUD.prefab missing — run VibeGame1/5. Build HUD");
            var testMenu = hud.GetComponent<TestMenu>();
            if (testMenu == null) Assert.Ignore("HUD.prefab has no TestMenu — run VibeGame1/5. Build HUD");
            if (testMenu.statusEffectsButton == null)
                Assert.Ignore("HUD.prefab predates the status-effects toggle — run VibeGame1/5. Build HUD");

            var label = testMenu.statusEffectsButton.GetComponentInChildren<TMP_Text>();
            Assert.IsNotNull(label, "STATUS EFFECTS button has no TMP label");
            Assert.AreEqual("STATUS EFFECTS: ON", label.text);
        }
    }
}
