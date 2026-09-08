using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE TOP-RIGHT COLUMN and THE LOADOUT PANE, after the 2026-09-06 pass the user asked for:
    /// "make the best runs page under the music player and make it collapsed by default. take away the
    /// wand switcher UI under the level label on the top right and make the souls counter more visually
    /// appealing."
    ///
    /// <para>The arithmetic tests run against <see cref="HudBuilder"/>'s consts and need no prefab — they
    /// are the layout facts themselves. The rest read <c>HUD.prefab</c> and <c>Assert.Ignore</c> until
    /// <c>VibeGame1/5. Build HUD</c> has run, in the house style: a prefab-value test proves nothing
    /// against a stale prefab.</para>
    /// </summary>
    public class HudColumnTests
    {
        const string HudPath = "Assets/Prefabs/HUD.prefab";
        const float BloomCap = 1.05f;
        const float CanvasW = 1920f, CanvasH = 1080f;

        static GameObject Hud()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            if (p == null) Assert.Ignore(HudPath + " missing — run VibeGame1/5. Build HUD");
            if (p.transform.Find("RadioPane") == null)
                Assert.Ignore("HUD.prefab predates the radio pass — run VibeGame1/5. Build HUD, then re-run");
            return p;
        }

        /// <summary>Top-anchored rect in canvas units, the same reading HudGlassTests and RadioViewTests use.</summary>
        static Rect CanvasRect(RectTransform rt)
        {
            float ax = rt.anchorMin.x, ay = rt.anchorMin.y;
            float px = rt.pivot.x, py = rt.pivot.y;
            float left = ax * CanvasW + rt.anchoredPosition.x - px * rt.sizeDelta.x;
            float top = (ay - 1f) * CanvasH + rt.anchoredPosition.y + (1f - py) * rt.sizeDelta.y;
            return new Rect(left, top - rt.sizeDelta.y, rt.sizeDelta.x, rt.sizeDelta.y);
        }

        /// <summary>By name anywhere under <paramref name="root"/>. Scope it to a pane where the name is
        /// not unique on the prefab — "SoulsText" is also the level-up card's total.</summary>
        static Transform Find(Transform root, string name)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                if (tr.name == name) return tr;
            return null;
        }

        static Transform Find(GameObject root, string name) { return Find(root.transform, name); }

        // ---- 1. the corner column, after BEST RUNS was removed (2026-09-07) --------------------------

        [Test]
        public void TheHudShipsNoBestRunsPaneAndNoBestRunsText()
        {
            // The user's ask, 2026-09-07: "remove the best runs tab from the in game UI." A leaderboard
            // is a menu readout. Both draw paths must be silent - this is the HUD one; GhostHud's own
            // canvas is covered by TheGhostBoardShipsHidden below.
            var hud = Hud();
            foreach (var name in new[] { "BestRunsPane", "BestRunsTitle", "BestRunsText" })
                Assert.IsNull(Find(hud, name), name + " is still on HUD.prefab - re-run VibeGame1/5. Build HUD");

            foreach (var t in hud.GetComponentsInChildren<TMP_Text>(true))
                Assert.IsFalse(t.text != null && t.text.ToUpperInvariant().Contains("BEST RUNS"),
                    t.name + " still reads BEST RUNS");
        }

        [Test]
        public void TheGhostBoardShipsHidden_SoTheFallbackTableNeverDrawsInThePanesPlace()
        {
            // GhostHud builds its own canvas and used to draw "<b>BEST RUNS</b>" plus the table whenever
            // the HUD carried no pane. Removing the pane alone would have REVERTED the UI to that block,
            // so the board ships off; GhostRacing's "Ghost/Toggle Leaderboard Panel" still turns it on.
            var go = new GameObject("GhostHudDefaults");
            try
            {
                var gh = go.AddComponent<GhostHud>();   // edit mode: no Awake, so this is the shipped default
                Assert.IsFalse(gh.BoardVisible, "GhostHud.BoardVisible ships true - the loose BEST RUNS block is back on screen");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheColumnAnchorKeepsTheYTheHintAndTheEditorPanelWereTunedTo()
        {
            // BestRunsBottom outlived the pane it was named for: the hint line (-12) and the 700-tall
            // F10 editor panel (-48) are still positioned off it, so it must stay at -272. The heights
            // it is composed from are no longer a pane's size, only this anchor's arithmetic.
            Assert.AreEqual(-32f, HudBuilder.BestRunsX, 0.001f, "the corner column left the radio's right edge");
            Assert.AreEqual(-32f - HudBuilder.RadioHeight - HudBuilder.ColumnGap, HudBuilder.BestRunsTop, 0.001f,
                "the column anchor no longer starts one gap under the radio's lower edge");
            Assert.AreEqual(HudBuilder.BestRunsChrome + 2f * HudBuilder.BestRunsRowHeight, HudBuilder.BestRunsCollapsedHeight, 0.001f);
            Assert.AreEqual(-272f, HudBuilder.BestRunsBottom, 0.001f,
                "BestRunsBottom moved; the hint (-12) and the level-editor panel (-48, 700 tall) hang off it");
            float editorBottom = HudBuilder.BestRunsBottom - 48f - 700f;
            Assert.GreaterOrEqual(editorBottom, -CanvasH,
                "the level-editor panel now ends at y " + editorBottom + ", off a 1080 canvas");
        }

        [Test]
        public void OnlyTheFlowMeterFillsTheGapUnderTheRadio()
        {
            // The band the removed board left is now the FLOW METER's, at the user's ask (2026-09-07).
            // Exactly one pane may live there, it must fit the band to the pixel so the hint line and the
            // F10 panel keep their tuned y, and nothing else may drift in beside it.
            var hud = Hud();
            var flow = Find(hud, "FlowMeter");
            if (flow == null) Assert.Ignore("HUD.prefab predates the flow meter — run VibeGame1/5. Build HUD");
            var fr = CanvasRect(flow.GetComponent<RectTransform>());
            Assert.AreEqual(HudBuilder.BestRunsTop, fr.yMax, 0.01f, "the flow meter's top left the band");
            Assert.AreEqual(HudBuilder.BestRunsBottom, fr.yMin, 0.01f,
                "the flow meter's bottom is not BestRunsBottom; the hint line would collide with it");
            Assert.AreEqual(HudBuilder.FlowWidth, fr.width, 0.01f);
            AssertNothingElseInTheBand(hud);
        }

        static void AssertNothingElseInTheBand(GameObject hud)
        {
            var radio = Find(hud, "RadioPane");
            if (radio == null) Assert.Ignore("no RadioPane - run VibeGame1/5. Build HUD");
            var r = CanvasRect(radio.GetComponent<RectTransform>());
            var band = new Rect(r.x, HudBuilder.BestRunsBottom, r.width, HudBuilder.BestRunsCollapsedHeight);

            foreach (Transform tr in hud.transform)
            {
                if (tr.name == "LevelEditorPanel") continue;   // F10 only, and it starts below the band
                if (tr.name == "FlowMeter") continue;          // the band is its, and the test above pins it
                var rect = tr.GetComponent<RectTransform>();
                if (rect == null) continue;
                Assert.IsFalse(band.Overlaps(CanvasRect(rect)),
                    tr.name + " now sits in the band the removed board left under the radio");
            }
        }

        // ---- 3. the wand switcher is gone -------------------------------------------------------------

        [Test]
        public void TheLoadoutCarriesNoWandReadout()
        {
            var hud = Hud();
            // ExecuteInteractor.cs:95 already prints "DEATHBLOW [ATTACK] WAND 2.0s" at the crosshair while
            // the wand is cooling — the one moment the cooldown decides anything. A permanent top-left
            // copy of that answer was a second, quieter voice in the wrong place.
            Assert.IsNull(Find(hud, "WandText"), "the wand name is still on the loadout pane");
            Assert.IsNull(Find(hud, "WandCooldownBar"), "the wand cooldown hairline is still on the loadout pane");
        }

        [Test]
        public void TheLoadoutPaneClosedTheGapTheWandRowsLeft()
        {
            var hud = Hud();
            var pane = hud.transform.Find("Loadout");
            Assert.IsNotNull(pane, "no Loadout pane");
            var p = CanvasRect(pane.GetComponent<RectTransform>());
            Assert.AreEqual(HudBuilder.LoadoutHeight, p.height, 0.01f,
                "the pane is still sized for the wand rows — a pane with a hole in it reads as a bug");

            // Every line the pane holds must sit inside it, or the pane lies about what it contains.
            // Its children hang off the glass, which is stretched to the pane, so their rects read in
            // PANE-LOCAL units: y 0 at the top edge, -LoadoutHeight at the bottom.
            foreach (var name in new[] { "WeaponText", "SoulsLabel", "SoulsText" })
            {
                var tr = Find(pane, name);
                Assert.IsNotNull(tr, name + " is missing from the loadout pane");
                var r = CanvasRect(tr.GetComponent<RectTransform>());
                Assert.GreaterOrEqual(r.yMin, -HudBuilder.LoadoutHeight - 0.01f, name + " " + r + " hangs out of the bottom of the pane");
                Assert.LessOrEqual(r.yMax, 0.01f, name + " " + r + " sits above the top of the pane");
            }

            // The strip under it follows the pane's new lower edge instead of floating where the wand was.
            var strip = hud.transform.Find("StatusStrip");
            if (strip != null)
            {
                var s = CanvasRect(strip.GetComponent<RectTransform>());
                Assert.AreEqual(HudBuilder.ColumnGap, p.yMin - s.yMax, 0.01f,
                    "the status strip is not one gap under the loadout pane " + p + " vs " + s);
            }
        }

        // ---- 4. the souls counter --------------------------------------------------------------------

        [Test]
        public void SoulsReadAsACountedResource_ALabelAndANumber()
        {
            var hud = Hud();
            var loadout = hud.transform.Find("Loadout");
            Assert.IsNotNull(loadout, "no Loadout pane");
            var label = Find(loadout, "SoulsLabel");
            var value = Find(loadout, "SoulsText");   // scoped: the level-up card has a SoulsText too
            if (label == null) Assert.Ignore("HUD.prefab predates the souls pass — run VibeGame1/5. Build HUD");
            var lt = label.GetComponent<TMP_Text>();
            var vt = value.GetComponent<TMP_Text>();

            Assert.AreEqual("SOULS", lt.text, "the label is not the quiet word; the value must be the digits alone");
            Assert.IsFalse(vt.text.Contains("SOULS"), "the number still carries the word: '" + vt.text + "' — the label says it once");
            Assert.Greater(vt.fontSize, lt.fontSize * 1.8f, "the number is not the value on this line; it reads as another label");
            Assert.Less(lt.color.a, vt.color.a, "the label must be quieter than the value");
            Assert.IsTrue((vt.fontStyle & FontStyles.Bold) != 0, "the count is not set as a value");

            // Mint, the souls colour, and under the cap: the HUD never blooms.
            Assert.Greater(vt.color.g, vt.color.r, "the souls number is not mint");
            Assert.Greater(vt.color.g, vt.color.b, "the souls number is not mint");
            Assert.LessOrEqual(vt.color.maxColorComponent, BloomCap, "the souls number is HDR-bright");
            Assert.LessOrEqual(lt.color.maxColorComponent, BloomCap, "the souls label is HDR-bright");

            // The punch scales about the left edge of the digits, so a roll never shoves them out of the pane.
            var rt = vt.rectTransform;
            Assert.AreEqual(0f, rt.pivot.x, 0.001f, "the number's pivot is not on its left edge; the gain punch would walk it right");
        }

        [Test]
        public void TheSoulsRollAndFlashAreShippedOnThePrefab()
        {
            var hud = Hud();
            var hc = hud.GetComponent<HUDController>();
            Assert.IsNotNull(hc);
            Assert.Greater(hc.soulsRollPerSecond, 0f, "the counter would snap; a counted resource is counted");
            Assert.That(hc.soulsFlashSeconds, Is.InRange(0.15f, 1f),
                "the gain flourish lasts " + hc.soulsFlashSeconds + "s — under 0.15 it is a glitch, over 1 it is a second light source");
        }
    }
}
