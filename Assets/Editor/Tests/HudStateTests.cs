using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// WHICH READOUTS ARE ON SCREEN IN WHICH STATE. The 2026-09-06 QA pass asked one question of every
    /// pane — can it survive a state change it should not — and the level editor was the answer.
    ///
    /// <para>Pressing F10 disables the CharacterController, parks the player and flies a camera. Nothing
    /// behind the vitals moves after that: health, stamina, posture, the Pyre and the flask freeze at
    /// whatever the level left them holding, the item slots still offer items [E] cannot fire, and the
    /// clock sits at the time the run was paused at (<c>SpeedrunTimer.Update</c> only advances in
    /// <c>Playing</c> or <c>Dead</c>). A frozen readout that looks live is the definition of a HUD that
    /// lies, so <c>HUDController</c> hides them by ROOT while <c>GameManager.State</c> is
    /// <c>Editing</c> and puts them back on the way out.</para>
    ///
    /// <para><b>The rule these tests really protect is ONE OWNER PER PANE.</b> The radio and BEST RUNS
    /// panes are toggled every frame by <c>RadioView</c> and <c>GhostHud</c>; adding a second writer
    /// would make the pane flicker or stick, which is exactly the class of bug the pane-ROOT convention
    /// exists to prevent. They must never appear in this list.</para>
    /// </summary>
    public class HudStateTests
    {
        const string HudPath = "Assets/Prefabs/HUD.prefab";

        static GameObject Hud()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            if (p == null) Assert.Ignore(HudPath + " missing — run VibeGame1/5. Build HUD");
            return p;
        }

        static HUDController Controller()
        {
            var hc = Hud().GetComponent<HUDController>();
            Assert.IsNotNull(hc, "HUD.prefab carries no HUDController");
            if (hc.editorHiddenRoots == null || hc.editorHiddenRoots.Length == 0)
                Assert.Ignore("HUD.prefab predates the level-editor visibility pass — run VibeGame1/5. Build HUD");
            return hc;
        }

        [Test]
        public void TheEditorHidesEveryReadoutThatIsAboutARun()
        {
            var hc = Controller();
            var names = new List<string>();
            foreach (var go in hc.editorHiddenRoots)
            {
                Assert.IsNotNull(go, "editorHiddenRoots carries a null — a lost reference is a pane that never comes back");
                names.Add(go.name);
            }

            foreach (var must in new[] { "Vitals", "Loadout", "Clock", "ItemSlots", "StatusStrip" })
                CollectionAssert.Contains(names, must,
                    must + " stays on screen in the level editor, frozen at whatever the run left in it");
        }

        [Test]
        public void TheEditorListNeverTakesAPaneSomethingElseAlreadyOwns()
        {
            var hc = Controller();
            foreach (var go in hc.editorHiddenRoots)
            {
                if (go == null) continue;
                Assert.AreNotEqual("RadioPane", go.name,
                    "RadioView toggles the radio pane every frame from LevelRadio.HasPlaylist; a second writer fights it");
                Assert.AreNotEqual("BestRunsPane", go.name,
                    "GhostHud toggles BEST RUNS from the leaderboard; a second writer fights it");
                Assert.AreNotEqual("Crosshair", go.name, "the level editor AIMS with the crosshair");
                Assert.AreNotEqual("PromptText", go.name, "the level editor writes its PLAYING banner to the prompt line");
                Assert.AreNotEqual("ScreenFlash", go.name, "the flash is a full-screen overlay, not a readout");
            }
        }

        [Test]
        public void EveryHiddenRootIsAPaneRoot_NotTheGlassInsideIt()
        {
            // Wiring the glass instead of the group is the bug the level-editor panel already shipped
            // once: the shadow and sheen layers stay on screen as an empty black card.
            var hud = Hud();
            var hc = Controller();
            foreach (var go in hc.editorHiddenRoots)
            {
                if (go == null) continue;
                Assert.AreSame(hud.transform, go.transform.parent,
                    go.name + " is not a direct child of the canvas — editorHiddenRoots must hold pane ROOTS, " +
                    "never the glass Pane() hands back, or the shadow and sheen stay up as an empty card");
                Assert.AreNotEqual("Glass", go.name);
                Assert.IsTrue(go.activeSelf, go.name + " must SHIP visible; the editor is what hides it, at runtime");
            }
        }

        [Test]
        public void TheListHasNoDuplicates()
        {
            var hc = Controller();
            var seen = new HashSet<GameObject>();
            foreach (var go in hc.editorHiddenRoots)
            {
                if (go == null) continue;
                Assert.IsTrue(seen.Add(go), go.name + " appears twice in editorHiddenRoots");
            }
        }

        [Test]
        public void TheSuperReadyBannerIsNotInTheList_BecauseTheControllerOwnsIt()
        {
            // PyreReadyLabel floats above the vitals rather than living inside the pane, so it has no root
            // to hide with. HUDController stays its single owner and re-derives it from the last
            // PyreChanged when the editor closes — a second owner here would strand it on or off.
            var hc = Controller();
            foreach (var go in hc.editorHiddenRoots)
                if (go != null)
                    Assert.AreNotEqual("PyreReadyLabel", go.name,
                        "the READY banner has two owners; it would strand on or off across an editor session");
            Assert.IsNotNull(hc.pyreReadyLabel, "the READY banner is not wired at all");
        }
    }
}
