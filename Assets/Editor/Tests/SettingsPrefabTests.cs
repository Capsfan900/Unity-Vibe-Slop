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
    
        // ---- the INFO tab (2026-09-05): the key-bind reference, one emitter, both prefabs ----

        static (GameObject panel, string text) InfoOf(SettingsMenu menu, string which)
        {
            // Read through SerializedObject so this test compiles whatever the field set on SettingsMenu
            // looks like on the day; a missing property is an Ignore, not a red.
            var so = new SerializedObject(menu);
            var panelProp = so.FindProperty("infoPanel");
            var textProp = so.FindProperty("infoText");
            var buttonProp = so.FindProperty("infoButton");
            if (panelProp == null || textProp == null || buttonProp == null)
                Assert.Ignore(which + ": SettingsMenu has no infoPanel / infoText / infoButton yet — rebuild after the INFO tab lands");
            Assert.IsNotNull(panelProp.objectReferenceValue, which + ": infoPanel is null — rebuild with 5 / 9");
            Assert.IsNotNull(buttonProp.objectReferenceValue, which + ": infoButton is null — the tab would be unreachable");
            var text = textProp.objectReferenceValue as TMPro.TMP_Text;
            Assert.IsNotNull(text, which + ": infoText is null");
            var panel = (GameObject)panelProp.objectReferenceValue;
            Assert.IsFalse(panel.activeSelf, which + ": the INFO panel must ship closed, like the settings panel");
            return (panel, text.text);
        }

        [Test]
        public void BothPrefabs_CarryTheSameInfoTab()
        {
            var hud = InfoOf(Load(HudPath), "HUD");
            var menu = InfoOf(Load(MenuPath), "MainMenu");
            Assert.IsFalse(string.IsNullOrWhiteSpace(hud.text), "HUD: the INFO text is empty");
            Assert.AreEqual(hud.text, menu.text, "the two INFO tabs differ — SettingsPanelKit is the one emitter; both must come from ControlsInfo");
            string lower = hud.text.ToLowerInvariant();
            foreach (var must in new[] { "f10", "slide", "dash", "parry", "f1", "level editor" })
                StringAssert.Contains(must, lower, "the INFO text does not mention " + must);
        }

        [Test]
        public void TheTestMenuOpensTheInfoTab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            Assert.IsNotNull(prefab);
            var tm = prefab.GetComponent<VibeGame1.TestMenu>();
            if (tm == null) Assert.Ignore("no TestMenu on HUD.prefab");
            var so = new SerializedObject(tm);
            var p = so.FindProperty("infoButton");
            if (p == null) Assert.Ignore("TestMenu has no infoButton yet");
            Assert.IsNotNull(p.objectReferenceValue, "TestMenu.infoButton is null — F1 → INFO would do nothing");
        }

        // ------------------------------------------------------------------ the rebind row

        [Test]
        public void BothPrefabsCarryTheFlourishKeyRow_WithWordedButtonsAndNoSlider()
        {
            foreach (var path in new[] { HudPath, MenuPath })
            {
                var menu = Load(path);
                string which = path.Contains("HUD") ? "HUD" : "MainMenu";
                SettingsMenu.Row row = null;
                for (int i = 0; i < menu.rows.Length; i++)
                    if (menu.rows[i] != null && menu.rows[i].kind == SettingsMenu.RowKind.WeaponTwirlKey) row = menu.rows[i];

                Assert.IsNotNull(row, which + ": no FLOURISH KEY row — run 5. Build HUD and 9. Build Main Menu");
                Assert.IsNull(row.slider, which + ": the rebind row must have no slider");
                Assert.IsNotNull(row.value, which + ": the rebind row has no value text — it IS the readout");

                var dec = row.decrease.GetComponentInChildren<TMPro.TMP_Text>();
                var inc = row.increase.GetComponentInChildren<TMPro.TMP_Text>();
                Assert.AreEqual(SettingsMenu.RebindButtonLabel(false), dec.text,
                    which + ": the left button must say REBIND, not '<' — a key is not a list position");
                Assert.AreEqual(SettingsMenu.RebindButtonLabel(true), inc.text,
                    which + ": the right button must say RESET");
            }
        }

        [Test]
        public void TheActionsAssetStillCarriesTheActionAndItsDefaultKey()
        {
            const string assetPath = "Assets/InputSystem_Actions.inputactions";
            Assert.IsTrue(System.IO.File.Exists(assetPath), assetPath + " missing");
            string json = System.IO.File.ReadAllText(assetPath);
            StringAssert.Contains("\"name\": \"" + InputReader.WeaponTwirlActionName + "\"", json,
                "the rebindable action is gone from the actions asset — the settings row would be dead");
            StringAssert.Contains("\"action\": \"" + InputReader.WeaponTwirlActionName + "\"", json,
                "the action exists but nothing is bound to it");
            StringAssert.Contains("\"path\": \"" + SettingsData.WeaponTwirlDefaultBinding + "\"", json,
                "the shipped default key is not " + SettingsData.WeaponTwirlDefaultBinding);
        }

        [Test]
        public void ThePlayerPrefabShipsTheFlourish_WithTheValuesTheBuilderWrites()
        {
            // Hard rule 9: a field initialiser on WeaponTwirl never reaches a prefab already on disk.
            // These are the numbers PrefabFactory writes; if they drift, the shipped feel drifts.
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (player == null) Assert.Ignore("Player.prefab missing — run VibeGame1/4. Build Prefabs");
            var twirl = player.GetComponentInChildren<WeaponTwirl>(true);
            Assert.IsNotNull(twirl, "Player.prefab has no WeaponTwirl — the flourish key would do nothing " +
                                    "in a build. Run VibeGame1/4. Build Prefabs.");
            // includeInactive: TRUE, and not for tidiness. A prefab ASSET is not in a scene, so every
            // object in it reports activeInHierarchy false, and the no-argument GetComponentInParent
            // skips inactive objects - it returns null here even when both components sit on the SAME
            // GameObject. The one-argument overload is the only one that tells the truth off disk.
            Assert.IsNotNull(twirl.GetComponentInParent<WeaponViewmodel>(true),
                "WeaponTwirl is not under the WeaponViewmodel — it would find no grip to spin");
            Assert.AreEqual(0.42f, twirl.spinSeconds, 1e-4f, "shipped spinSeconds drifted from PrefabFactory");
            Assert.AreEqual(1, twirl.spinsPerPress, "shipped spinsPerPress drifted from PrefabFactory");
            Assert.AreEqual(6, twirl.maxQueuedSpins, "shipped maxQueuedSpins drifted from PrefabFactory");
            Assert.AreEqual(Vector3.right, twirl.axis, "shipped spin axis drifted from PrefabFactory");
        }
    }
}
