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
    /// THE RADIO PANE, as shipped on HUD.prefab (hard rule 9), plus the one piece of its motion that is
    /// pure arithmetic. The prefab tests <c>Assert.Ignore</c> until <c>VibeGame1/5. Build HUD</c> has run
    /// with the radio pass on it — a prefab-value test proves nothing against a stale prefab.
    /// </summary>
    public class RadioViewTests
    {
        const string HudPath = "Assets/Prefabs/HUD.prefab";
        const float BloomCap = 1.05f;

        static GameObject Hud()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            if (p == null) Assert.Ignore(HudPath + " missing — run VibeGame1/5. Build HUD");
            if (p.transform.Find("RadioPane") == null)
                Assert.Ignore("HUD.prefab predates the radio pass — run VibeGame1/5. Build HUD, then re-run");
            return p;
        }

        /// <summary>Top-right-anchored rect in canvas units, the same reading HudGlassTests uses.</summary>
        static Rect CanvasRect(RectTransform rt, float canvasW)
        {
            float ax = rt.anchorMin.x, ay = rt.anchorMin.y;
            float px = rt.pivot.x, py = rt.pivot.y;
            float left = ax * canvasW + rt.anchoredPosition.x - px * rt.sizeDelta.x;
            float top = (ay - 1f) * 1080f + rt.anchoredPosition.y + (1f - py) * rt.sizeDelta.y;
            return new Rect(left, top - rt.sizeDelta.y, rt.sizeDelta.x, rt.sizeDelta.y);
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                if (tr.name == name) return tr;
            return null;
        }

        [Test]
        public void TheRadioPaneIsGlass_WithItsFourLayers()
        {
            var hud = Hud();
            var pane = hud.transform.Find("RadioPane");
            var glass = pane.Find("Glass");
            Assert.IsNotNull(pane.Find("Shadow"), "the radio throws no shadow; it is painted on the frame");
            Assert.IsNotNull(glass, "the radio has no Glass layer");
            Assert.IsNotNull(glass.Find("Sheen"), "the radio has no sheen");
            Assert.IsNotNull(glass.Find("EdgeLight"), "the radio has no edge light");
            Assert.IsNotNull(glass.Find("EdgeEmber"), "the radio's edge light does not warm toward ember");

            var img = glass.GetComponent<Image>();
            StringAssert.StartsWith("Glass_", img.sprite.name, "the radio is not on a generated glass sprite");
            Assert.AreEqual(Image.Type.Sliced, img.type, "the radio glass must be 9-sliced");
            Assert.That(img.color.a, Is.InRange(0.40f, 0.75f), "radio pane alpha " + img.color.a);
            StringAssert.DoesNotContain("Pill", img.sprite.name, "the clock is the ONE pill on the HUD");

            foreach (var g in pane.GetComponentsInChildren<Graphic>(true))
                Assert.LessOrEqual(g.color.maxColorComponent, BloomCap, g.name + " on the radio is over the bloom cap");
        }

        [Test]
        public void TheRadioShipsHidden_AndIsWiredByItsROOT()
        {
            var hud = Hud();
            var pane = hud.transform.Find("RadioPane");
            Assert.IsFalse(pane.gameObject.activeSelf,
                "the radio must ship HIDDEN — a level with no mp3s has HasPlaylist false and must never see a dead pane");

            var view = hud.GetComponent<RadioView>();
            Assert.IsNotNull(view, "no RadioView on the HUD root");
            Assert.AreSame(pane.gameObject, view.paneRoot,
                "RadioView.paneRoot is not the pane ROOT — wired to the glass, the shadow and sheen stay on screen");

            var hc = hud.GetComponent<HUDController>();
            var prop = new SerializedObject(hc).FindProperty("radioPane");
            if (prop != null) Assert.AreSame(pane.gameObject, prop.objectReferenceValue, "HUDController.radioPane is not the pane");
        }

        [Test]
        public void TheRadioReadsStationTitleCounterProgressAndKeys()
        {
            var hud = Hud();
            var view = hud.GetComponent<RadioView>();
            Assert.IsNotNull(view.stationText, "no station line");
            Assert.IsNotNull(view.titleText, "no track title");
            Assert.IsNotNull(view.counterText, "no TRACK n/m counter");
            Assert.IsNotNull(view.keyHintText, "no key hint");
            Assert.IsNotNull(view.progressBar, "no progress line");
            Assert.IsNotNull(view.titleViewport, "no ticker viewport; a long title would overhang the glass");

            // Type scale: the title is the value, the station and counter are labels, the keys quietest.
            Assert.AreEqual(17f, view.titleText.fontSize, 0.01f, "the track title is the value on this pane");
            Assert.AreEqual(12f, view.stationText.fontSize, 0.01f, "the station is a label, quieter than the title");
            Assert.AreEqual(12f, view.counterText.fontSize, 0.01f, "the counter is a label");
            Assert.Less(view.keyHintText.fontSize, view.counterText.fontSize, "the key hint is the quietest line");
            Assert.Less(view.keyHintText.color.a, view.counterText.color.a, "the key hint should be dimmer than the counter");

            // The ticker window must CLIP, or the title spills out of the pane.
            Assert.IsNotNull(view.titleViewport.GetComponent<RectMask2D>(), "the ticker viewport does not mask");
            Assert.Greater(view.titleText.rectTransform.sizeDelta.x, view.titleViewport.rect.width,
                "the title rect is no wider than its window, so it could never scroll");

            // Hard rule 5: an anchor-driven BarView, not a filled Image.
            Assert.IsNotNull(view.progressBar.fill, "the progress line has no fill");
            Assert.AreEqual("Fill", view.progressBar.fill.name);
            // The shared HudBuilder.Bar helper ships every fill as it does the other bars; BarView drives the WIDTH by
            // anchors and forces fillAmount to 1 on a Filled image (BarView.cs ~168), so the rule is "fillAmount never
            // drives the bar", not "the type is never Filled".
            if (view.progressBar.fill.type == Image.Type.Filled)
                Assert.AreEqual(1f, view.progressBar.fill.fillAmount, 1e-4f, "a Filled fill must be forced to 1 so anchors, not fillAmount, drive the bar");
            Assert.IsFalse(view.progressBar.pulseWhenFull, "the progress line is a readout, not a resource; it must not pulse");
            Assert.That(view.progressBar.GetComponent<RectTransform>().sizeDelta.y, Is.InRange(2f, 8f),
                "the progress line is a thin line, not a bar");

            // Motion numbers shipped on the prefab (hard rule 9).
            Assert.Greater(view.tickerSpeed, 0f, "the ticker never moves");
            Assert.Greater(view.changeSeconds, 0f, "no track-change flourish");
        }

        [Test]
        public void TheRadioOwnsTheCorner_AndOverlapsNothingUpThere()
        {
            var hud = Hud();
            const float W = 1920f;
            var radio = CanvasRect(hud.transform.Find("RadioPane").GetComponent<RectTransform>(), W);

            foreach (var other in new[] { "Clock", "HintText", "LevelEditorPanel" })
            {
                var tr = hud.transform.Find(other);
                if (tr == null) continue;
                var r = CanvasRect(tr.GetComponent<RectTransform>(), W);
                Assert.IsFalse(radio.Overlaps(r), "the radio " + radio + " overlaps " + other + " " + r);
            }

            // It owns the corner outright now that BEST RUNS is gone (2026-09-07): nothing else in the
            // top-right column may reach further right than the stereo.
            Assert.IsNull(hud.transform.Find("BestRunsPane"), "the BEST RUNS pane is back in the radio's column");
        }

        // ---- pure motion ----------------------------------------------------------------------------

        [Test]
        public void AShortTitleNeverScrolls()
        {
            for (float t = 0f; t < 10f; t += 0.37f)
                Assert.AreEqual(0f, RadioView.TickerOffset(120f, 268f, t, 34f, 1.6f), 0.0001f,
                    "a title that fits must sit still — motion means something on this HUD");
        }

        [Test]
        public void ALongTitleRestsAtBothEnds_AndNeverLeavesTheWindow()
        {
            const float textW = 468f, viewW = 268f, speed = 34f, pause = 1.6f;
            const float overflow = textW - viewW;                 // 200
            const float travel = overflow / speed;                // ~5.88 s
            Assert.AreEqual(0f, RadioView.TickerOffset(textW, viewW, 0f, speed, pause), 0.0001f, "does not start at the head");
            Assert.AreEqual(0f, RadioView.TickerOffset(textW, viewW, pause * 0.5f, speed, pause), 0.0001f, "does not rest before scrolling");
            Assert.AreEqual(-speed, RadioView.TickerOffset(textW, viewW, pause + 1f, speed, pause), 0.001f, "wrong scroll speed");
            Assert.AreEqual(-overflow, RadioView.TickerOffset(textW, viewW, pause + travel, speed, pause), 0.001f, "does not stop at the tail");
            Assert.AreEqual(-overflow, RadioView.TickerOffset(textW, viewW, pause * 1.5f + travel, speed, pause), 0.001f, "does not rest at the tail");

            float cycle = (pause + travel) * 2f;
            Assert.AreEqual(0f, RadioView.TickerOffset(textW, viewW, cycle, speed, pause), 0.001f, "the cycle does not close on the head");
            for (float t = 0f; t < cycle * 3f; t += 0.05f)
            {
                float x = RadioView.TickerOffset(textW, viewW, t, speed, pause);
                Assert.That(x, Is.InRange(-overflow - 0.001f, 0.001f), "the ticker left its window at t=" + t + ": " + x);
            }
        }

        [Test]
        public void TheStationAndCounterLinesReadLikeAStereo()
        {
            Assert.AreEqual("THE HOLLOW ASCENT FM", RadioView.StationLine("The Hollow Ascent"));
            Assert.AreEqual("RADIO FM", RadioView.StationLine(""), "a nameless level still has a station");
            Assert.AreEqual("TRACK 1/7", RadioView.CounterLine(0, 7), "the counter is one-based; index 0 is TRACK 1");
            Assert.AreEqual("TRACK 7/7", RadioView.CounterLine(6, 7));
            Assert.AreEqual("", RadioView.CounterLine(-1, 0), "an empty playlist has no counter");
        }
    }
}
