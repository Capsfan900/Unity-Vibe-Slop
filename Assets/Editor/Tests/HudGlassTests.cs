using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The HUD's glass, as shipped on the prefab (hard rule 9): the panes exist with their four layers,
    /// their alphas sit in the band the look was tuned to, nothing on the canvas is brighter than 1.0
    /// (the UI never blooms — light in this game means "you deflected"), the generated sprites are real
    /// 9-slices, and the three bars a styling pass restyles by NAME are still there, still BarViews,
    /// still with a Fill child.
    ///
    /// <para>Every test <c>Assert.Ignore</c>s until <c>VibeGame1/5. Build HUD</c> has run on this
    /// builder: a prefab-value test only proves anything once the prefab has actually been rebuilt.</para>
    /// </summary>
    public class HudGlassTests
    {
        const string HudPath = "Assets/Prefabs/HUD.prefab";
        const float BloomCap = 1.05f;

        static GameObject Hud()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            if (p == null) Assert.Ignore(HudPath + " missing — run VibeGame1/5. Build HUD");
            if (p.transform.Find("Vitals") == null)
                Assert.Ignore("HUD.prefab predates the glass pass — run VibeGame1/5. Build HUD, then re-run");
            return p;
        }

        static Transform Glass(GameObject hud, string paneName)
        {
            var pane = hud.transform.Find(paneName);
            Assert.IsNotNull(pane, paneName + " pane is missing");
            var glass = pane.Find("Glass");
            Assert.IsNotNull(glass, paneName + " has no Glass layer");
            return glass;
        }

        [Test]
        public void TheThreePanesExist_EachWithItsFourLayers()
        {
            var hud = Hud();
            foreach (var name in new[] { "Vitals", "Loadout", "Clock" })
            {
                var pane = hud.transform.Find(name);
                Assert.IsNotNull(pane.Find("Shadow"), name + " throws no shadow; it is painted on the frame instead of sitting off it");
                var glass = Glass(hud, name);
                Assert.IsNotNull(glass.Find("Sheen"), name + " has no sheen");
                Assert.IsNotNull(glass.Find("EdgeLight"), name + " has no edge light — the one loud thing on the HUD");
                Assert.IsNotNull(glass.Find("EdgeEmber"), name + " edge light does not warm toward ember at the left");
            }
        }

        [Test]
        public void TheGlassIsSmokedNotOpaque_AndTheLightIsALine()
        {
            var hud = Hud();
            foreach (var name in new[] { "Vitals", "Loadout", "Clock" })
            {
                var glass = Glass(hud, name).GetComponent<Image>();
                Assert.IsNotNull(glass.sprite, name + ": the glass has no sprite; a plain quad has no corners");
                StringAssert.StartsWith("Glass_", glass.sprite.name, name + ": not a generated glass sprite");
                Assert.AreEqual(Image.Type.Sliced, glass.type, name + ": glass must be 9-sliced or its corners stretch");
                Assert.That(glass.color.a, Is.InRange(0.40f, 0.75f),
                    name + ": pane alpha " + glass.color.a + " — under 0.40 it is a tint, over 0.75 it is a wall");
                Assert.Less(glass.color.maxColorComponent, 0.12f, name + ": the pane tint must be near-void, not a colour");

                var edge = Glass(hud, name).Find("EdgeLight").GetComponent<Image>();
                Assert.That(edge.rectTransform.sizeDelta.y, Is.EqualTo(1f).Within(0.01f), name + ": the edge light is not one pixel");
                Assert.That(edge.color.a, Is.InRange(0.15f, 0.5f), name + ": edge light alpha " + edge.color.a);

                var sheen = Glass(hud, name).Find("Sheen").GetComponent<Image>();
                Assert.Less(sheen.color.a, 0.08f, name + ": the sheen is a band of frost, not a highlight — keep it under 0.08");
            }
        }

        [Test]
        public void TheClockIsAPill_AndOnlyTheClock()
        {
            var hud = Hud();
            var clock = Glass(hud, "Clock").GetComponent<Image>();
            StringAssert.Contains("Pill", clock.sprite.name, "the clock is the one pane with fully rounded ends");
            foreach (var name in new[] { "Vitals", "Loadout" })
                StringAssert.DoesNotContain("Pill", Glass(hud, name).GetComponent<Image>().sprite.name,
                    name + " is a pill too; three identical shapes read as one repeated card");
        }

        [Test]
        public void TheGeneratedSpritesAreRealNineSlices()
        {
            Hud();
            foreach (var file in new[] { "Glass_Pane_r6", "Glass_Tile_r8", "Glass_Pill", "Glass_Track_r4", "Glass_Shadow" })
            {
                string path = UiSprites.Dir + "/" + file + ".png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Assert.IsNotNull(sprite, path + " missing or not imported as a Sprite — run 5. Build HUD");
                Assert.Greater(sprite.border.x, 0f, file + " has no 9-slice border; its corners would stretch");
            }
            foreach (var file in new[] { "Glass_EdgeLight", "Glass_Sheen" })
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(UiSprites.Dir + "/" + file + ".png"), file + " missing");
        }

        [Test]
        public void NothingOnTheCanvasIsBrighterThanTheBloomCap()
        {
            var hud = Hud();
            foreach (var g in hud.GetComponentsInChildren<Graphic>(true))
                Assert.LessOrEqual(g.color.maxColorComponent, BloomCap,
                    g.name + " (" + g.transform.parent.name + ") is HDR-bright: " + g.color + ". The UI never blooms.");
        }

        [Test]
        public void TheThreeRestyledBarsAreStillBarsByName()
        {
            var hud = Hud();
            var hc = hud.GetComponent<HUDController>();
            Assert.IsNotNull(hc);
            var byName = new Dictionary<string, BarView>();
            foreach (var b in hud.GetComponentsInChildren<BarView>(true)) byName[b.name] = b;
            foreach (var name in new[] { "HealthBar", "StaminaBar", "PyreBar" })
            {
                Assert.IsTrue(byName.ContainsKey(name), name + " is gone; the fluid/fire passes find it by this name");
                var bar = byName[name];
                Assert.IsNotNull(bar.fill, name + " has no fill");
                Assert.AreEqual("Fill", bar.fill.name, name + ": the fill child must be called Fill");
                Assert.IsNotNull(bar.GetComponent<Image>(), name + ": the bar's own Image is the track");
            }
            Assert.AreSame(byName["HealthBar"], hc.healthBar, "HUDController.healthBar is not the HealthBar object");
            Assert.AreSame(byName["PyreBar"], hc.pyreBar, "HUDController.pyreBar is not the PyreBar object");
            Assert.AreSame(byName["StaminaBar"], hc.staminaView.bar, "StaminaView.bar is not the StaminaBar object");
        }

        [Test]
        public void TheVitalsShareOneLeftEdgeAndOneWidth()
        {
            var hud = Hud();
            var hc = hud.GetComponent<HUDController>();
            var health = hc.healthBar.GetComponent<RectTransform>();
            var posture = hc.postureBar.GetComponent<RectTransform>();
            var pyre = hc.pyreBar.GetComponent<RectTransform>();
            var stamina = hc.staminaView.bar.GetComponent<RectTransform>();
            Assert.AreEqual(health.sizeDelta.x, posture.sizeDelta.x, 0.01f, "posture bar width drifted from the health bar");
            Assert.AreEqual(health.sizeDelta.x, pyre.sizeDelta.x, 0.01f, "pyre bar width drifted from the health bar");
            Assert.AreEqual(health.sizeDelta.x, stamina.sizeDelta.x, 0.01f, "stamina bar width drifted from the health bar");
            // Health is the tallest, posture the thinnest: the hierarchy of how often you look.
            Assert.Greater(health.sizeDelta.y, stamina.sizeDelta.y, "health should be taller than stamina");
            Assert.Greater(stamina.sizeDelta.y, posture.sizeDelta.y, "posture should be the thinnest bar");
        }

        [Test]
        public void MenusSitOnGlassCards()
        {
            var hud = Hud();
            foreach (var panel in new[] { "PausePanel", "LevelUpPanel", "WandMenuPanel", "SettingsPanel" })
            {
                var p = hud.transform.Find(panel);
                Assert.IsNotNull(p, panel + " missing");
                Assert.IsFalse(p.gameObject.activeSelf, panel + " must ship closed");
                Assert.IsNotNull(p.Find("Card"), panel + " has no glass card; the menu is painted on the scrim");
                var scrim = p.GetComponent<Image>();
                Assert.That(scrim.color.a, Is.InRange(0.6f, 0.9f), panel + ": the scrim dims the game, it does not replace it");
            }
        }
    
        // ---- BEST RUNS as glass, clear of the clock pill and the level-editor panel (2026-09-05) ----

        /// <summary>Top-right-anchored rect in canvas units: x from the right edge (negative), y from
        /// the top (negative). Every pane this test cares about is anchored TopRight / TopCenter.</summary>
        static Rect CanvasRect(RectTransform rt, float canvasW)
        {
            // anchoredPosition is the pivot's offset from the anchor; panes here use the top-right or
            // top-centre anchor with a matching pivot, so the rect hangs down-left from that point.
            float ax = rt.anchorMin.x, ay = rt.anchorMin.y;
            float px = rt.pivot.x, py = rt.pivot.y;
            float left = ax * canvasW + rt.anchoredPosition.x - px * rt.sizeDelta.x;
            float top = (ay - 1f) * 1080f + rt.anchoredPosition.y + (1f - py) * rt.sizeDelta.y;
            return new Rect(left, top - rt.sizeDelta.y, rt.sizeDelta.x, rt.sizeDelta.y);
        }

        [Test]
        public void ThereIsNoBestRunsGlassLeftOnTheHud()
        {
            // Removed 2026-09-07 at the user's ask. This test used to prove the pane WAS glass; it now
            // proves the glass is gone, because dead glass nobody can reach is this HUD's own bug ("a
            // pane a system can no longer fill must not be left on screen").
            var hud = Hud();
            foreach (var tr in hud.GetComponentsInChildren<Transform>(true))
                Assert.IsFalse(tr.name.StartsWith("BestRuns"),
                    tr.name + " survived the BEST RUNS removal - re-run VibeGame1/5. Build HUD");

            var hc = hud.GetComponent<HUDController>();
            Assert.IsNotNull(hc);
            Assert.IsNull(new SerializedObject(hc).FindProperty("bestRunsPane"),
                "HUDController still serialises a bestRunsPane reference");
        }

        [Test]
        public void ThePlayingHudCarriesNoBindDump()
        {
            // The key-bind list moved to the settings INFO tab (ControlsInfo). The HUD's HintText stays for
            // one-line contextual hints and must ship EMPTY and narrow, not as a wall of binds.
            var hud = Hud();
            var hint = hud.transform.Find("HintText");
            if (hint == null) Assert.Ignore("no HintText on HUD.prefab");
            var tmp = hint.GetComponent<TMPro.TMP_Text>();
            Assert.IsNotNull(tmp);
            Assert.IsTrue(string.IsNullOrWhiteSpace(tmp.text), "HintText ships with text: '" + tmp.text + "' — the bind dump belongs on the INFO tab");
            var rt = hint.GetComponent<RectTransform>();
            Assert.LessOrEqual(rt.sizeDelta.y, 40f, "HintText is " + rt.sizeDelta.y + " px tall — a one-line hint, not a list");
        }
    }
}
