using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Reflection;

namespace VibeGame1.Tests
{
    public class DeveloperAccessTests
    {
        [TearDown]
        public void LockAfterTest()
        {
            DeveloperAccess.LockForTests();
        }

        [Test]
        public void LockedConsole_DoesNotAdvertiseOrRunPrivilegedCommands()
        {
            DeveloperAccess.LockForTests();

            StringAssert.DoesNotContain("timing", DeveloperConsole.HelpText);
            StringAssert.DoesNotContain("F1", DeveloperConsole.HelpText);
            var timing = DeveloperConsole.ExecuteCommand("timing export");
            var legacy = DeveloperConsole.ExecuteCommand("editor unlock");
            StringAssert.Contains("LOCKED", timing.message);
            StringAssert.Contains("LOCKED", legacy.message);
            Assert.IsTrue(timing.redactInput);
            Assert.IsTrue(legacy.redactInput);
            Assert.IsFalse(DeveloperAccess.IsUnlocked);
        }

        [Test]
        public void WrongPassphrases_DoNotAccumulateOrPersistAGrant()
        {
            DeveloperAccess.LockForTests();

            Assert.IsFalse(DeveloperAccess.TryUnlock(""));
            Assert.IsFalse(DeveloperAccess.TryUnlock("editor unlock"));
            Assert.IsFalse(DeveloperAccess.TryUnlock("incorrect phrase"));
            Assert.IsFalse(DeveloperAccess.IsUnlocked);
        }

        [Test]
        public void SharedCapability_ControlsEveryBuildPolicy()
        {
            DeveloperAccess.LockForTests();
            Assert.IsFalse(InputReader.LevelEditorShortcutAllowed(false, DeveloperAccess.IsUnlocked));
            Assert.IsFalse(InputReader.LevelEditorShortcutAllowed(true, DeveloperAccess.IsUnlocked));
            Assert.IsFalse(PlayerTimingCapture.IsSupported);
            StringAssert.DoesNotContain("F10", ControlsInfo.Text);
            StringAssert.DoesNotContain("LEVEL EDITOR", ControlsInfo.Text);

            DeveloperAccess.UnlockForTests();
            Assert.IsTrue(InputReader.LevelEditorShortcutAllowed(false, DeveloperAccess.IsUnlocked));
            Assert.IsTrue(InputReader.LevelEditorShortcutAllowed(true, DeveloperAccess.IsUnlocked));
            Assert.IsTrue(PlayerTimingCapture.IsSupported);
            StringAssert.Contains("F1/F5-F10/4/R enabled", DeveloperConsole.HelpText);
            StringAssert.Contains("F10", ControlsInfo.Text);
            StringAssert.Contains("LEVEL EDITOR", ControlsInfo.Text);
        }

        [Test]
        public void TestMenuOpen_IsInertUntilTheCapabilityIsGranted()
        {
            var go = new GameObject("TestMenuGate");
            var panel = new GameObject("Panel");
            panel.transform.SetParent(go.transform, false);
            panel.SetActive(false);
            var menu = go.AddComponent<TestMenu>();
            menu.panel = panel;
            try
            {
                DeveloperAccess.LockForTests();
                menu.Open();
                Assert.IsFalse(panel.activeSelf);

                DeveloperAccess.UnlockForTests();
                menu.Open();
                Assert.IsTrue(panel.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DirectDeveloperEntryPoints_AreInertWhileLocked()
        {
            var player = new GameObject("GateTestPlayer");
            player.AddComponent<CharacterController>();
            player.AddComponent<FirstPersonMotor>();
            player.AddComponent<PlayerLook>();
            var editorObject = new GameObject("GateTestLevelEditor");
            var editor = editorObject.AddComponent<LevelEditor>();
            var sandboxObject = new GameObject("GateTestSandbox");
            var sandbox = sandboxObject.AddComponent<SandboxController>();
            string captureDirectory = Path.Combine(Path.GetTempPath(),
                "vibegame1-locked-frame-film-" + Path.GetRandomFileName());

            try
            {
                DeveloperAccess.LockForTests();

                Assert.IsFalse(editor.Enter());
                Assert.AreEqual(LevelEditor.Mode.Off, editor.CurrentMode);

                sandbox.infiniteFlask = false;
                sandbox.ToggleInfiniteFlask();
                Assert.IsFalse(sandbox.infiniteFlask);

                DebugHarness.Run("parry");
                StringAssert.Contains("locked", DebugHarness.Log);

                FrameFilm.Run(captureDirectory, "Enemy_Grunt");
                StringAssert.Contains("locked", FrameFilm.Log);
                Assert.IsFalse(Directory.Exists(captureDirectory));
            }
            finally
            {
                Object.DestroyImmediate(sandboxObject);
                Object.DestroyImmediate(editorObject);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void MainMenu_HidesAndRejectsDeveloperRowsWhileLocked()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MainMenu.prefab");
            if (prefab == null) Assert.Ignore("MainMenu.prefab missing — run VibeGame1/9. Build Main Menu.");
            var instance = Object.Instantiate(prefab);
            try
            {
                var menu = instance.GetComponent<MainMenuController>();
                Assert.IsNotNull(menu);

                DeveloperAccess.LockForTests();
                menu.Refresh();
                Assert.IsFalse(menu.sandboxRow.root.activeSelf);
                Assert.AreEqual(0, menu.CustomRowCount);

                DeveloperAccess.UnlockForTests();
                menu.Refresh();
                Assert.IsTrue(menu.sandboxRow.root.activeSelf);
                Assert.IsTrue(menu.sandboxRow.button.interactable);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SandboxWakeSwitch_CannotMutateAnEnemyWhileLocked()
        {
            var enemyObject = new GameObject("GateTestEnemy");
            var enemy = enemyObject.AddComponent<EnemyController>();
            var spawnerObject = new GameObject("GateTestSpawner");
            var spawner = spawnerObject.AddComponent<EnemySpawner>();
            var switchObject = new GameObject("GateTestWakeSwitch");
            switchObject.AddComponent<BoxCollider>();
            var wakeSwitch = switchObject.AddComponent<SandboxEnemySwitch>();
            wakeSwitch.spawner = spawner;
            var instanceField = typeof(EnemySpawner).GetField("<Instance>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);

            try
            {
                Assert.IsNotNull(instanceField, "EnemySpawner.Instance backing field changed");
                instanceField.SetValue(spawner, enemyObject);
                DeveloperAccess.LockForTests();

                enemy.aggroLocked = true;
                Assert.IsFalse(wakeSwitch.Activate());
                Assert.IsTrue(enemy.aggroLocked);

                enemy.aggroLocked = false;
                wakeSwitch.Rearm();
                Assert.IsFalse(enemy.aggroLocked);
            }
            finally
            {
                Object.DestroyImmediate(switchObject);
                Object.DestroyImmediate(spawnerObject);
                Object.DestroyImmediate(enemyObject);
            }
        }
    }
}
