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
    /// THE FLOW METER (2026-09-07, the user's ask): "make the speed boost gained from any source be a
    /// meter on the top right that basically shows stacks and active speed in game and do the same for
    /// parries in a row (just a metric now)".
    ///
    /// <para>These pin the facts that make it truthful: the pane fits the band exactly, every tuning
    /// number is SHIPPED on the prefab (hard rule 9), the decay line is a BarView and not an
    /// Image.fillAmount (hard rule 5), nothing on it blooms, it ships INVISIBLE, and the chain counts
    /// only perfect deflects and is broken by anything else. Prefab tests <c>Assert.Ignore</c> until
    /// <c>VibeGame1/5. Build HUD</c> has run — a prefab test against a stale prefab proves nothing.</para>
    /// </summary>
    public class FlowMeterTests
    {
        const string HudPath = "Assets/Prefabs/HUD.prefab";
        const float BloomCap = 1.05f;

        static GameObject Hud()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            if (p == null) Assert.Ignore(HudPath + " missing — run VibeGame1/5. Build HUD");
            return p;
        }

        static FlowMeterView Meter()
        {
            var v = Hud().GetComponentInChildren<FlowMeterView>(true);
            if (v == null) Assert.Ignore("HUD.prefab predates the flow meter — run VibeGame1/5. Build HUD");
            return v;
        }

        // ---- 1. the band arithmetic (no prefab needed: these ARE the layout facts) -------------------

        [Test]
        public void TheMeterFitsTheFreedBandExactly()
        {
            Assert.AreEqual(HudBuilder.RadioWidth, HudBuilder.FlowWidth, 0.001f,
                "the corner must read as one column: the meter is the radio's width");
            Assert.AreEqual(HudBuilder.BestRunsCollapsedHeight, HudBuilder.FlowHeight, 0.001f);
            Assert.AreEqual(HudBuilder.BestRunsBottom, HudBuilder.BestRunsTop - HudBuilder.FlowHeight, 0.001f,
                "the meter must end on the column anchor, or the hint line collides with it");
        }

        [Test]
        public void ThereAreFiveStackPips_TheShippedTurretsMaxStacks()
        {
            Assert.AreEqual(5, HudBuilder.FlowStackPips);
        }

        // ---- 2. the prefab -------------------------------------------------------------------------

        [Test]
        public void EveryTuningNumberIsShippedOnThePrefab()
        {
            var m = Meter();
            Assert.AreEqual(1.5f, m.idleLingerSeconds, 0.001f, "the linger that makes a LOSS readable");
            Assert.AreEqual(10f, m.fadeInSpeed, 0.001f);
            Assert.AreEqual(2.2f, m.fadeOutSpeed, 0.001f);
            Assert.AreEqual(1.005f, m.boostEpsilon, 0.0001f);
            Assert.Greater(m.fadeInSpeed, m.fadeOutSpeed, "a gain is news; a loss is watched");
        }

        [Test]
        public void ItShipsInvisible_NothingShoutsX100AtAPlayerAtRest()
        {
            var m = Meter();
            Assert.IsNotNull(m.group, "the meter needs its CanvasGroup or it can never be silent at rest");
            Assert.AreEqual(0f, m.group.alpha, 0.001f);
            Assert.IsFalse(m.group.blocksRaycasts, "a HUD readout must never eat a click");
        }

        [Test]
        public void TheWholeMeterIsWired()
        {
            var m = Meter();
            Assert.IsNotNull(m.speedValue, "the multiplier is the authoritative number");
            Assert.IsNotNull(m.speedMs, "active speed in game — the user asked for it by name");
            Assert.IsNotNull(m.streakValue);
            Assert.IsNotNull(m.streakLabel);
            Assert.IsNotNull(m.decayBar);
            Assert.IsNotNull(m.stackPips);
            Assert.AreEqual(HudBuilder.FlowStackPips, m.stackPips.Length);
            foreach (var p in m.stackPips) Assert.IsNotNull(p);
        }

        [Test]
        public void TheDecayLineIsABarViewNotAFillAmount()
        {
            // Hard rule 5: a null-sprite UGUI Image silently ignores fillAmount. BarView drives anchors.
            var m = Meter();
            Assert.IsNotNull(m.decayBar.fill, "BarView with no Fill child drives nothing");
            Assert.IsNull(m.decayBar.ghost, "the decay line is time, not a resource: no trailing ghost");
            Assert.IsFalse(m.decayBar.pulseWhenFull);
        }

        [Test]
        public void NothingOnTheMeterBlooms()
        {
            var m = Meter();
            foreach (var g in m.GetComponentsInChildren<Graphic>(true))
            {
                var c = g.color;
                Assert.LessOrEqual(Mathf.Max(c.r, Mathf.Max(c.g, c.b)), BloomCap, g.name + " blooms");
            }
            Assert.LessOrEqual(Mathf.Max(m.emberColor.r, Mathf.Max(m.emberColor.g, m.emberColor.b)), BloomCap);
            Assert.LessOrEqual(Mathf.Max(m.tealColor.r, Mathf.Max(m.tealColor.g, m.tealColor.b)), BloomCap);
        }

        [Test]
        public void TheChainWearsTheDeflectColourAndTheBoostWearsEmber()
        {
            var m = Meter();
            Assert.AreNotEqual(ColorUtility.ToHtmlStringRGB(m.emberColor), ColorUtility.ToHtmlStringRGB(m.tealColor),
                "two unrelated readouts must not share one colour");
            Assert.AreEqual("7FBFB5", ColorUtility.ToHtmlStringRGB(m.tealColor), "ghost teal is the deflect colour");
            Assert.AreEqual("E0A030", ColorUtility.ToHtmlStringRGB(m.emberColor), "ember gold is gain/spend");
        }

        [Test]
        public void TheEditorTakesTheMeterOffScreen_ItIsARunReadout()
        {
            var hud = Hud();
            var c = hud.GetComponent<HUDController>();
            if (c == null || c.editorHiddenRoots == null) Assert.Ignore("run VibeGame1/5. Build HUD");
            var m = hud.GetComponentInChildren<FlowMeterView>(true);
            if (m == null) Assert.Ignore("HUD.prefab predates the flow meter — run VibeGame1/5. Build HUD");
            CollectionAssert.Contains(c.editorHiddenRoots, m.gameObject,
                "the F10 editor hides run readouts by their ROOT; the flow meter is one");
        }

        // ---- 3. what breaks the chain --------------------------------------------------------------

        [Test]
        public void OnlyAPerfectDeflectExtendsTheChain_AndAnythingElseEndsIt()
        {
            // Against the PURE rule, not a live component. A MonoBehaviour's OnEnable does not run in an
            // EditMode test, so raising GameEvents at a component built with new GameObject(...) asserts
            // against a handler that was never subscribed: it reads 0 forever and looks like a counting
            // bug that is not there. This cost a red suite on 2026-09-07; see ENGINEERING-LOG.
            Assert.AreEqual(1, FlowMeterView.NextStreak(0, ParryResult.Perfect));
            Assert.AreEqual(2, FlowMeterView.NextStreak(1, ParryResult.Perfect), "two clean deflects in a row is a chain of two");
            Assert.AreEqual(2, FlowMeterView.NextStreak(2, ParryResult.None), "None is not a resolution against the player");
            Assert.AreEqual(0, FlowMeterView.NextStreak(7, ParryResult.Blocked), "a Blocked hit is timing that failed; the chain ends");
            Assert.AreEqual(0, FlowMeterView.NextStreak(7, ParryResult.Hit), "taking the hit ends it");
            Assert.AreEqual(0, FlowMeterView.NextStreak(0, ParryResult.Hit));
        }

        [Test]
        public void TheChainIsDisplayOnly_AndTheViewIsItsOnlyOwner()
        {
            // The user asked for "just a metric". Nothing in the game may read the chain back, so the
            // count lives in the UI and the only public surface is the readout and the pure rule.
            var t = typeof(FlowMeterView);
            Assert.IsNotNull(t.GetProperty("Streak"), "the chain readout is gone");
            Assert.IsNull(t.GetMethod("SetStreak"), "nothing may write the chain from outside the view");
            foreach (var asm in new[] { typeof(FirstPersonMotor).Assembly })
                foreach (var type in asm.GetTypes())
                {
                    if (type == t || type.Namespace != "VibeGame1") continue;
                    foreach (var f in type.GetFields(System.Reflection.BindingFlags.Instance
                             | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
                             | System.Reflection.BindingFlags.NonPublic))
                        Assert.AreNotEqual(t, f.FieldType,
                            type.Name + " holds a FlowMeterView - the chain must stay display-only");
                }
        }
    }
}
