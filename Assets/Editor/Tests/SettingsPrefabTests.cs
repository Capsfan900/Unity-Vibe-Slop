using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The settings screen's STRUCTURE, asserted on the shipped prefabs the way
    /// <c>MarionetteDataTests.ThePrefab_IsAnimatedAndFullyBound</c> does it: every binding non-null,
    /// on both prefabs, resolved through the TYPE (which also catches the cross-project GUID trap —
    /// a prefab whose SettingsMenu script reference is broken returns null from GetComponent and
    /// nothing else would ever say so).
    ///
    /// <para>A null UI reference is silent at runtime: the menu opens, the row draws, and one control
    /// does nothing. That is exactly what a test is for, and it is the strongest claim an EditMode
    /// suite can make about a screen nobody can click headlessly.</para>
    /// </summary>
    public class SettingsPrefabTests
    {
        const string HudPath = "Assets/Prefabs/HUD.prefab";
        const string MenuPath = "Assets/Prefabs/MainMenu.prefab";

        static SettingsMenu Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path + " missing — run VibeGame1/5. Build HUD and 9. Build Main Menu");
            var menu = prefab.GetComponent<SettingsMenu>();
            Assert.IsNotNull(menu, path + " carries no SettingsMenu — rebuild it (a broken script GUID also lands here)");
            return menu;
        }

        // ------------------------------------------------------------------ shared shape

        static void AssertFullyBound(SettingsMenu menu, string which)
        {
            Assert.IsNotNull(menu.panel, which + ": panel is null");
            Assert.IsFalse(menu.panel.activeSelf, which + ": the panel must ship CLOSED");

            Assert.IsNotNull(menu.backButton, which + ": backButton is null");
            Assert.IsNotNull(menu.resetButton, which + ": resetButton is null");
            Assert.IsNotNull(menu.openButton, which + ": openButton is null — the screen would be unreachable");

            Assert.IsNotNull(menu.rows, which + ": rows is null");
            Assert.AreEqual(SettingsMenu.AllKinds.Length, menu.rows.Length,
                which + ": the builder must emit every RowKind");

            for (int i = 0; i < menu.rows.Length; i++)
            {
                var row = menu.rows[i];
                var k = SettingsMenu.AllKinds[i];
                Assert.IsNotNull(row, which + ": row " + i + " is null");
                Assert.AreEqual(k, row.kind, which + ": row " + i + " is out of order — binding is by index");
                Assert.IsNotNull(row.root, which + ": " + k + " has no root");
                Assert.IsNotNull(row.label, which + ": " + k + " has no label");
                Assert.IsNotNull(row.value, which + ": " + k + " has no value text — the authoritative readout");
                Assert.IsNotNull(row.decrease, which + ": " + k + " has no decrease button");
                Assert.IsNotNull(row.increase, which + ": " + k + " has no increase button");
                Assert.IsNotNull(row.note, which + ": " + k + " has no note text");

                if (SettingsMenu.IsContinuous(k))
                {
                    Assert.IsNotNull(row.slider, which + ": continuous row " + k + " has no slider");
                    Assert.IsNotNull(row.slider.fillRect, which + ": " + k + " slider has no fillRect — it would not draw");
                    Assert.IsNotNull(row.slider.handleRect, which + ": " + k + " slider has no handleRect");
                    var fillImg = row.slider.fillRect.GetComponent<Image>();
                    Assert.IsNotNull(fillImg, which + ": " + k + " slider fill has no Image");
                    Assert.AreNotEqual(Image.Type.Filled, fillImg.type,
                        which + ": " + k + " slider fill must be anchor-driven, never Image.Type.Filled/fillAmount " +
                        "(hard rule 5 — a null-sprite Image silently ignores fillAmount)");
                }
                else
                {
                    Assert.IsNull(row.slider, which + ": discrete row " + k + " should not carry a slider");
                }

                Assert.AreEqual(SettingsMenu.LabelFor(k), row.label.text, which + ": " + k + " label text drifted");
            }
        }

        [Test]
        public void HudPrefab_SettingsMenu_IsFullyBound()
        {
            AssertFullyBound(Load(HudPath), "HUD");
        }

        [Test]
        public void MainMenuPrefab_SettingsMenu_IsFullyBound()
        {
            AssertFullyBound(Load(MenuPath), "MainMenu");
        }

        // ------------------------------------------------------------------ the two entry points

        [Test]
        public void HudPrefab_OpensFromThePausePanel_AndSuspendsThePauseMenu()
        {
            var menu = Load(HudPath);
            Assert.IsNotNull(menu.pauseMenu, "HUD: pauseMenu not wired — ESC under the settings screen would double-fire");
            Assert.IsNotNull(menu.pauseMenu.panel, "HUD: the pause menu itself has no panel");
            Assert.AreSame(menu.pauseMenu.panel, menu.hideWhileOpen,
                "HUD: hideWhileOpen must be the pause panel, so settings replaces it rather than stacking on it");
            Assert.IsTrue(menu.openButton.transform.IsChildOf(menu.pauseMenu.panel.transform),
                "HUD: the SETTINGS button must live on the pause panel — that is the in-game path");
        }

        [Test]
        public void MainMenuPrefab_OpensFromTheTitlePanel()
        {
            var menu = Load(MenuPath);
            var controller = menu.GetComponent<MainMenuController>();
            Assert.IsNotNull(controller, "MainMenu prefab has no MainMenuController?");
            Assert.IsNull(menu.pauseMenu, "MainMenu: there is no pause menu in the front end; wiring one would be a lie");
            Assert.IsNotNull(controller.titlePanel);
            Assert.AreSame(controller.titlePanel, menu.hideWhileOpen,
                "MainMenu: hideWhileOpen must be the title panel");
            Assert.IsTrue(menu.openButton.transform.IsChildOf(controller.titlePanel.transform),
                "MainMenu: the SETTINGS button must live on the title panel");
        }

        [Test]
        public void SettingsPanel_DrawsOverThePausePanel()
        {
            // UGUI draw order is sibling order. If the settings panel is an earlier sibling than the
            // pause panel, opening settings paints it UNDER the pause overlay: present, bound, invisible.
            var menu = Load(HudPath);
            Assert.Greater(menu.panel.transform.GetSiblingIndex(),
                           menu.pauseMenu.panel.transform.GetSiblingIndex(),
                           "HUD: SettingsPanel must be a later sibling than PausePanel");
        }

        [Test]
        public void BothPrefabs_ShareOneRowLayout()
        {
            // The design contract: the front-end screen and the in-game screen are the SAME screen.
            // Same emitter, so same row names in the same order under each panel.
            var a = Load(HudPath);
            var b = Load(MenuPath);
            for (int i = 0; i < a.rows.Length; i++)
            {
                Assert.AreEqual(a.rows[i].root.name, b.rows[i].root.name, "row " + i + " diverged between prefabs");
                Assert.AreEqual(a.rows[i].kind, b.rows[i].kind);
            }
        }
    }
}
