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
            BoltRegistry.Reset();
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
            Set(strip, "shownReboundArmed", false);
            Set(strip, "shownSigilArmed", false);
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
            Set(strip, "shownRequiredRunSouls", 3560);
            Set(strip, "shownRegularKills", 3);
            Set(strip, "shownRequiredRegularKills", 4);
            Set(strip, "shownCompletedSplits", 3);
            Set(strip, "shownSplitCount", 4);
            Rebuild(strip);
            StringAssert.Contains("RUN 3425/3560", strip.Text);
            StringAssert.Contains("FOES 3/4", strip.Text);
            StringAssert.Contains("SPLITS 3/4", strip.Text);
            StringAssert.Contains("SPEED SURGE x2", strip.Text);
            Assert.AreEqual(3, strip.RowCount, "two run-objective rows plus the active effect are all visible");

            StatusStripView.StatusEffectsVisible = false;
            StringAssert.Contains("RUN 3425/3560", strip.Text);
            StringAssert.DoesNotContain("SPEED SURGE", strip.Text);
            Assert.AreEqual(2, strip.RowCount, "persistent run objective retains both primary and secondary rows");
        }

        [Test]
        public void TestMenuToggleAndHudPrefabButtonAreWired()
        {
            var go = new GameObject("TestMenuToggle");
            DeveloperAccess.UnlockForTests();
            try
            {
                var menu = go.AddComponent<TestMenu>();
                bool before = StatusStripView.StatusEffectsVisible;
                menu.ToggleStatusEffects();
                Assert.AreNotEqual(before, StatusStripView.StatusEffectsVisible);
            }
            finally
            {
                DeveloperAccess.LockForTests();
                Object.DestroyImmediate(go);
            }

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

        [Test]
        public void ProjectileThreatBracketOnlyReadsTheExistingCueWindow()
        {
            float now = 100f;
            BoltRegistry.Report(1, float.MaxValue, now + Projectile.CueLead * 0.5f);
            Assert.IsTrue(ProjectileThreatView.IsActionable(now), "a cued bolt inside the parry window is actionable");

            BoltRegistry.Reset();
            BoltRegistry.Report(1, now + 0.01f, now + Projectile.CueLead * 0.5f);
            Assert.IsFalse(ProjectileThreatView.IsActionable(now), "a bolt before its cue must not light the bracket");

            BoltRegistry.Reset();
            BoltRegistry.Report(1, float.MaxValue, now + Projectile.CueLead + 0.1f);
            Assert.IsFalse(ProjectileThreatView.IsActionable(now), "a cued bolt outside the actionable window must not light it");
        }

        [Test]
        public void GeneratedHudFitsTheFullStatusStack_AndShipsOneQuietThreatBracket()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HUD.prefab");
            if (hud == null) Assert.Ignore("HUD.prefab missing - run VibeGame1/5. Build HUD");

            var strip = hud.transform.Find("StatusStrip");
            Assert.IsNotNull(strip, "generated HUD has no top-left status strip");
            Assert.GreaterOrEqual(strip.GetComponent<RectTransform>().sizeDelta.y, 204f,
                "three items, two run rows and all three dev effects would clip");

            var threat = hud.GetComponentInChildren<ProjectileThreatView>(true);
            Assert.IsNotNull(threat, "generated HUD has no projectile cue reinforcement");
            Assert.IsNotNull(threat.group);
            Assert.IsNotNull(threat.bracket);
            Assert.AreEqual(0f, threat.group.alpha, 0.001f, "the bracket must ship hidden");
            Assert.That(threat.maxAlpha, Is.InRange(0.2f, 0.4f),
                "the bolt cue should read without competing with PERFECT or DEATHBLOW");
            Assert.AreEqual(4, threat.transform.childCount, "the cue is one restrained four-mark bracket");
        }
    }
}
