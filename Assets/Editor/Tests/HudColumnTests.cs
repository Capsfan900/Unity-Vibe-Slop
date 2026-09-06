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

        // ---- 1. one column: the radio, then BEST RUNS under it ---------------------------------------

        [Test]
        public void TheColumnStacksTheRadioThenBestRuns_WithOneGapBetweenThem()
        {
            Assert.AreEqual(-32f, HudBuilder.BestRunsX, 0.001f, "BEST RUNS left the corner column; it must share the radio's right edge");
            Assert.AreEqual(-32f - HudBuilder.RadioHeight - HudBuilder.ColumnGap, HudBuilder.BestRunsTop, 0.001f,
                "BEST RUNS does not start one gap under the radio's lower edge");
        }

        [Test]
        public void CollapsingBestRunsKeepsTheYTheHintAndTheEditorPanelWereTunedTo()
        {
            // The old layout put the FULL table at y -32 and everything below hung off its bottom, -272.
            // Moving it under the radio only fits because it ships collapsed: 116 (radio) + 16 (gap) +
            // 108 (collapsed) lands on exactly the same lower edge, so the hint line and the 700-tall
            // level-editor panel keep the y they were tuned to and the panel still ends above the canvas.
            Assert.AreEqual(-272f, HudBuilder.BestRunsBottom, 0.001f,
                "BestRunsBottom moved; the hint (-12) and the level-editor panel (-48, 700 tall) hang off it");
            float editorBottom = HudBuilder.BestRunsBottom - 48f - 700f;
            Assert.GreaterOrEqual(editorBottom, -CanvasH,
                "the level-editor panel now ends at y " + editorBottom + ", off a 1080 canvas");
        }

        [Test]
        public void TheCollapsedPaneIsATitleAPersonalBestAndAMoreLine_AndTheFullTableIsEightRows()
        {
            Assert.AreEqual(HudBuilder.BestRunsChrome + 2f * HudBuilder.BestRunsRowHeight, HudBuilder.BestRunsCollapsedHeight, 0.001f,
                "collapsed is the personal best plus the '+N MORE' line — two rows, no more");
            Assert.AreEqual(HudBuilder.BestRunsChrome + Leaderboard.DisplayCount * HudBuilder.BestRunsRowHeight, HudBuilder.BestRunsHeight, 0.001f,
                "the expanded glass no longer holds one row per Leaderboard.DisplayCount");
            Assert.Less(HudBuilder.BestRunsCollapsedHeight, HudBuilder.BestRunsHeight, "collapsed is not smaller than expanded");
            Assert.AreEqual(HudBuilder.BestRunsTop - HudBuilder.BestRunsHeight, HudBuilder.BestRunsExpandedBottom, 0.001f);
            // Expanding may cover the hint band and the F10-only editor panel for a few seconds, but it
            // must never reach anything that is always on screen further down the frame.
            Assert.Greater(HudBuilder.BestRunsExpandedBottom, -CanvasH * 0.5f,
                "expanded, BEST RUNS reaches into the lower half of the screen — that is the fight, not the furniture");
        }

        [Test]
        public void ThePaneShipsCollapsed_UnderTheRadio_SharingItsColumn()
        {
            var hud = Hud();
            var best = Find(hud, "BestRunsPane");
            if (best == null) Assert.Ignore("no BestRunsPane on HUD.prefab — run VibeGame1/5. Build HUD");
            var radio = Find(hud, "RadioPane");
            var b = CanvasRect(best.GetComponent<RectTransform>());
            var r = CanvasRect(radio.GetComponent<RectTransform>());

            Assert.AreEqual(r.xMax, b.xMax, 0.01f, "BEST RUNS and the radio do not share a right edge; that is two columns, not one");
            Assert.AreEqual(r.width, b.width, 0.01f, "BEST RUNS is not the radio's width; the column reads as two panes of different families");
            Assert.LessOrEqual(b.yMax, r.yMin + 0.01f, "BEST RUNS is not UNDER the radio " + b + " vs " + r);
            Assert.AreEqual(HudBuilder.ColumnGap, r.yMin - b.yMax, 0.01f, "the gap between the radio and BEST RUNS is not the column gap");
            Assert.AreEqual(HudBuilder.BestRunsCollapsedHeight, b.height, 0.01f,
                "the pane does not ship COLLAPSED — a table of eight times is a menu, not a HUD readout");
            Assert.IsFalse(best.gameObject.activeSelf, "the pane must still ship HIDDEN; GhostHud shows it once there is a board");
        }

        [Test]
        public void TheBoardTextStretchesWithTheGlass_SoAShrunkPaneCannotSpill()
        {
            var hud = Hud();
            var text = Find(hud, "BestRunsText");
            if (text == null) Assert.Ignore("no BestRunsText — run VibeGame1/5. Build HUD");
            var rt = text.GetComponent<RectTransform>();
            Assert.AreEqual(0f, rt.anchorMin.y, 0.001f, "the board text is a FIXED rect; a 108 px pane would still draw eight rows past its bottom");
            Assert.AreEqual(1f, rt.anchorMax.y, 0.001f, "the board text does not follow the pane's height");
            Assert.AreEqual(TextOverflowModes.Truncate, text.GetComponent<TMP_Text>().overflowMode,
                "the board must never spill the glass, whatever the backend sends");
        }

        [Test]
        public void TheRuntimeGetsTheMetricsItResizesTheGlassWith()
        {
            var hud = Hud();
            var hc = hud.GetComponent<HUDController>();
            Assert.IsNotNull(hc);
            // Hard rule 9: shipped on the prefab, because HUDController cannot read an editor-assembly const.
            Assert.AreEqual(HudBuilder.BestRunsChrome, hc.bestRunsChrome, 0.001f, "bestRunsChrome is not the builder's chrome");
            Assert.AreEqual(HudBuilder.BestRunsRowHeight, hc.bestRunsRowHeight, 0.001f, "bestRunsRowHeight drifted from the builder");
            Assert.AreEqual(Leaderboard.DisplayCount, hc.bestRunsMaxRows, "bestRunsMaxRows is not Leaderboard.DisplayCount");
            Assert.That(hc.bestRunsExpandSeconds, Is.InRange(2f, 8f),
                "the board settles back after " + hc.bestRunsExpandSeconds + "s — under 2 it is a flicker, over 8 it is not collapsed by default");
            // The two heights the runtime can reach are exactly the two the layout was proven against.
            Assert.AreEqual(HudBuilder.BestRunsCollapsedHeight, hc.BestRunsPaneHeight(2), 0.001f);
            Assert.AreEqual(HudBuilder.BestRunsHeight, hc.BestRunsPaneHeight(Leaderboard.DisplayCount), 0.001f);
        }

        [Test]
        public void TheRowCountIsReadFromTheTextTheBoardSent()
        {
            // The pane is sized from the data it holds: a one-run board is a one-row pane.
            Assert.AreEqual(0, HUDController.LineCount(""));
            Assert.AreEqual(1, HUDController.LineCount("<b>1. 00:12.34</b>"));
            Assert.AreEqual(2, HUDController.LineCount("<b>1. 00:12.34</b>\n<alpha=#66>+7 MORE"));
            Assert.AreEqual(8, HUDController.LineCount("a\nb\nc\nd\ne\nf\ng\nh"));
        }

        [Test]
        public void ExpandedTheBoardStillClearsEverythingThatIsAlwaysOnScreen()
        {
            var hud = Hud();
            var best = Find(hud, "BestRunsPane");
            if (best == null) Assert.Ignore("no BestRunsPane on HUD.prefab — run VibeGame1/5. Build HUD");
            var b = CanvasRect(best.GetComponent<RectTransform>());
            var expanded = new Rect(b.x, b.yMax - HudBuilder.BestRunsHeight, b.width, HudBuilder.BestRunsHeight);

            foreach (var other in new[] { "RadioPane", "Clock", "Vitals", "Loadout" })
            {
                var tr = hud.transform.Find(other);
                if (tr == null) continue;
                var r = CanvasRect(tr.GetComponent<RectTransform>());
                Assert.IsFalse(expanded.Overlaps(r), "the EXPANDED board " + expanded + " covers " + other + " " + r);
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
