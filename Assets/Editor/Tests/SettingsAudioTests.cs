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
    /// THE AUDIO ROWS (2026-09-06) and the panel arithmetic that had to change to seat them.
    ///
    /// <para><b>The shape of the feature.</b> Master and music are SCALES on the mix
    /// <c>AudioManager</c> ships with (Managers.prefab: master 0.7, music 0.45), exactly the way
    /// <c>bloomScale</c> is a scale on the authored volume profile — so 100% is "the mix as tuned", a
    /// retune of the prefab moves every player's 100% with it, and the applier can never compound its
    /// own output. There is deliberately no SFX row: <c>AudioManager.PlayInternal</c> multiplies one
    /// -shots by <c>masterVolume</c> alone, so a third slider would control nothing, and a slider that
    /// controls nothing is the worst thing a settings screen can contain.</para>
    ///
    /// <para><b>Why the panel arithmetic is here too.</b> Twelve rows in four sections do not fit the
    /// stride the ten-row panel used; the last row would have landed on the BACK button. These tests run
    /// the builder's OWN pure layout function, so they are the layout facts rather than a second copy of
    /// them, and they check the result against a 21:9 canvas as well as 16:9 — the scaler leaves only
    /// 935 logical units of height there, which is where a tall panel actually breaks.</para>
    /// </summary>
    public class SettingsAudioTests
    {
        const string HudPath = "Assets/Prefabs/HUD.prefab";
        const string MenuPath = "Assets/Prefabs/MainMenu.prefab";
        const float BloomCap = 1.05f;

        /// <summary>Half the logical canvas height at 16:9 and at 21:9. CanvasScaler is
        /// ScaleWithScreenSize / MatchWidthOrHeight 0.5, so the logical height at 2560x1080 is
        /// 1080 / 2^(0.5*log2(2560/1920)) = 935.3 — the panel gets SHORTER on a wider monitor.</summary>
        const float HalfHeight169 = 540f;
        const float HalfHeightUltrawide = 467.6f;

        // ================================================================= the data layer

        [Test]
        public void Defaults_Are100Percent_WhichMeansTheMixAsTuned()
        {
            var d = SettingsData.Defaults();
            Assert.AreEqual(1f, d.masterVolume, 1e-5f, "master default must be 100% == AudioManager's authored gain");
            Assert.AreEqual(1f, d.musicVolume, 1e-5f, "music default must be 100% == AudioManager's authored gain");
            Assert.AreEqual("100%", SettingsMenu.ValueLabel(SettingsMenu.RowKind.MasterVolume, d));
            Assert.AreEqual("100%", SettingsMenu.ValueLabel(SettingsMenu.RowKind.MusicVolume, d));
        }

        [Test]
        public void Gains_AreTheAuthoredMixTimesTheScale_AndNeverExceedOne()
        {
            var d = SettingsData.Defaults();
            Assert.AreEqual(0.7f, d.MasterGain(0.7f), 1e-5f, "100% must hand back exactly what the prefab authored");
            Assert.AreEqual(0.45f, d.MusicGain(0.45f), 1e-5f);

            d.masterVolume = 0.5f;
            d.musicVolume = 0f;
            Assert.AreEqual(0.35f, d.MasterGain(0.7f), 1e-5f, "half of the authored 0.7");
            Assert.AreEqual(0f, d.MusicGain(0.45f), 1e-5f, "0% is silence, not a floor");

            // AudioSource.volume above 1 clips rather than getting louder, so the gain is clamped even
            // if a future prefab authors something over 1.
            d.masterVolume = 1f;
            Assert.AreEqual(1f, d.MasterGain(4f), 1e-5f, "a gain over 1 would distort, not amplify");
            Assert.AreEqual(0f, d.MasterGain(-2f), 1e-5f, "a negative authored gain is treated as silence");
        }

        [Test]
        public void Clamp_HoldsTheVolumesInRange_AndSurvivesNaN()
        {
            var d = SettingsData.Defaults();
            d.masterVolume = 9f;
            d.musicVolume = -4f;
            d.Clamp();
            Assert.AreEqual(SettingsData.VolumeMax, d.masterVolume, 1e-5f);
            Assert.AreEqual(SettingsData.VolumeMin, d.musicVolume, 1e-5f);

            // A NaN survives Mathf.Clamp unchanged and would silence the game with no way back through
            // the slider (NaN compares false against everything); Sane() is why it does not.
            d.masterVolume = float.NaN;
            d.musicVolume = float.PositiveInfinity;
            d.Clamp();
            Assert.AreEqual(SettingsData.VolumeDefault, d.masterVolume, 1e-5f);
            Assert.AreEqual(SettingsData.VolumeDefault, d.musicVolume, 1e-5f);
        }

        [Test]
        public void Clone_CarriesTheVolumes()
        {
            var d = SettingsData.Defaults();
            d.masterVolume = 0.25f;
            d.musicVolume = 0.75f;
            var c = d.Clone();
            Assert.AreEqual(0.25f, c.masterVolume, 1e-5f, "a clone that drops a field silently reverts it on the next save");
            Assert.AreEqual(0.75f, c.musicVolume, 1e-5f);
        }

        [Test]
        public void TheVolumesRoundTripThroughTheStore()
        {
            // KeyPrefix is swapped so this can never overwrite the developer's own settings.
            string realPrefix = SettingsStore.KeyPrefix;
            SettingsStore.KeyPrefix = "vg1.test.audio.";
            try
            {
                SettingsStore.DeleteAll();
                var d = SettingsData.Defaults();
                d.masterVolume = 0.4f;
                d.musicVolume = 0.15f;
                SettingsStore.Save(d);

                SettingsStore.Forget();
                var back = SettingsStore.Load();
                Assert.AreEqual(0.4f, back.masterVolume, 1e-4f, "master did not persist");
                Assert.AreEqual(0.15f, back.musicVolume, 1e-4f, "music did not persist");

                SettingsStore.ResetToDefaults();
                SettingsStore.Forget();
                Assert.AreEqual(1f, SettingsStore.Load().masterVolume, 1e-4f, "RESET DEFAULTS must put the mix back");
            }
            finally
            {
                SettingsStore.DeleteAll();
                SettingsStore.KeyPrefix = realPrefix;
                SettingsStore.Forget();
            }
        }

        [Test]
        public void TheVolumeRowsAreContinuousPercentSliders_SteppedInTwentieths()
        {
            foreach (var k in new[] { SettingsMenu.RowKind.MasterVolume, SettingsMenu.RowKind.MusicVolume })
            {
                Assert.IsTrue(SettingsMenu.IsContinuous(k), k + " must be a slider row, not a cycler");
                float lo, hi;
                SettingsMenu.ContinuousRange(k, out lo, out hi);
                Assert.AreEqual(0f, lo, 1e-5f, k + " must reach silence");
                Assert.AreEqual(1f, hi, 1e-5f, k + " must top out at the authored mix");
                Assert.AreEqual(0.05f, SettingsMenu.StepSize(k), 1e-5f,
                    k + ": one press of < or > should be five percent — twenty notches end to end");

                var d = SettingsData.Defaults();
                SettingsMenu.SetContinuous(d, k, -5f);
                Assert.AreEqual("0%", SettingsMenu.ValueLabel(k, d), k + " does not read 0% at silence");
                SettingsMenu.SetContinuous(d, k, 99f);
                Assert.AreEqual("100%", SettingsMenu.ValueLabel(k, d), k + " does not read 100% at the top of its range");
            }
        }

        [Test]
        public void ThereIsNoSfxRow_BecauseThereIsNoSfxBus()
        {
            // AudioManager.cs: PlayInternal does `volume * masterVolume * trim` — there is no third
            // gain to point a slider at. If one is ever added, this test is the place that says so.
            foreach (var k in SettingsMenu.AllKinds)
                Assert.AreNotEqual("SFX VOLUME", SettingsMenu.LabelFor(k),
                    "an SFX row exists but AudioManager still has no SFX bus — the slider would control nothing");
            Assert.AreEqual("MASTER VOLUME", SettingsMenu.LabelFor(SettingsMenu.RowKind.MasterVolume));
            Assert.AreEqual("MUSIC VOLUME", SettingsMenu.LabelFor(SettingsMenu.RowKind.MusicVolume));
        }

        [Test]
        public void TheAudioRowsAreLastInScreenOrder()
        {
            var all = SettingsMenu.AllKinds;
            Assert.AreEqual(SettingsMenu.RowKind.MasterVolume, all[all.Length - 2], "MASTER must open the AUDIO section");
            Assert.AreEqual(SettingsMenu.RowKind.MusicVolume, all[all.Length - 1], "MUSIC must follow MASTER");
        }

        // ================================================================= the panel arithmetic

        [Test]
        public void TheLastRowClearsTheButtons_AndTheButtonsClearTheCard()
        {
            float lastRow = SettingsPanelKit.LastRowY(SettingsMenu.AllKinds);
            float lastRowBottom = lastRow - SettingsPanelKit.RowHeight * 0.5f;
            float buttonTop = SettingsPanelKit.ButtonY + SettingsPanelKit.ButtonSize.y * 0.5f;
            float buttonBottom = SettingsPanelKit.ButtonY - SettingsPanelKit.ButtonSize.y * 0.5f;

            Assert.Greater(lastRowBottom, buttonTop,
                "the last row (bottom " + lastRowBottom + ") lands on BACK / RESET (top " + buttonTop + ")");

            float cardBottom = SettingsPanelKit.CardPos.y - SettingsPanelKit.CardSize.y * 0.5f;
            Assert.Greater(buttonBottom, cardBottom,
                "BACK / RESET hang off the bottom of the glass card");
        }

        [Test]
        public void EveryRowStaysBelowTheTitleRule()
        {
            // The rule sits at y 396 and the first section header is the first thing the loop places.
            float firstHeader = SettingsPanelKit.FirstRowCursor - SettingsPanelKit.SectionLead;
            Assert.Less(firstHeader + 11f, 396f, "the CONTROL header runs into the title rule");
        }

        [Test]
        public void ThePanelFitsA169Canvas_AndStillFitsAnUltrawideOne()
        {
            float buttonBottom = SettingsPanelKit.ButtonY - SettingsPanelKit.ButtonSize.y * 0.5f;

            Assert.Greater(buttonBottom, -HalfHeight169,
                "BACK / RESET are off the bottom of a 1920x1080 canvas");
            // 21:9 is where this actually bites: the scaler shrinks the LOGICAL height, so a panel that
            // fits 16:9 can still push its own buttons off a wider monitor. They used to sit at -484.
            Assert.Greater(buttonBottom, -HalfHeightUltrawide,
                "BACK / RESET (" + buttonBottom + ") fall off a 21:9 canvas, whose logical half-height is only "
                + HalfHeightUltrawide + " — the player could not leave the screen");
        }

        [Test]
        public void EachSectionCostsTheSameDrop_AndTheAudioSectionIsTheFourth()
        {
            // One section is worth exactly one lead plus one gap; four of them plus twelve rows is the
            // whole panel. If this drifts, LastRowY and the builder have stopped agreeing.
            var kinds = SettingsMenu.AllKinds;
            float expected = SettingsPanelKit.FirstRowCursor
                           - 4f * (SettingsPanelKit.SectionLead + SettingsPanelKit.SectionGap)
                           - (kinds.Length - 1) * SettingsPanelKit.RowStride;
            Assert.AreEqual(expected, SettingsPanelKit.LastRowY(kinds), 0.001f,
                "the panel draws four sections and " + kinds.Length + " rows; LastRowY disagrees");
        }

        // ================================================================= the shipped prefabs

        static SettingsMenu Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Assert.Ignore(path + " missing — run VibeGame1/5. Build HUD and 9. Build Main Menu");
            var menu = prefab.GetComponent<SettingsMenu>();
            if (menu == null) Assert.Ignore(path + " carries no SettingsMenu — rebuild it");
            if (menu.rows == null || menu.rows.Length != SettingsMenu.AllKinds.Length)
                Assert.Ignore(path + " predates the AUDIO section — run VibeGame1/5. Build HUD and 9. Build Main Menu");
            return menu;
        }

        static SettingsMenu.Row RowOf(SettingsMenu menu, SettingsMenu.RowKind kind)
        {
            for (int i = 0; i < menu.rows.Length; i++)
                if (menu.rows[i] != null && menu.rows[i].kind == kind) return menu.rows[i];
            return null;
        }

        [Test]
        public void BothPrefabsCarryTheAudioRows_WithSlidersAndAnAudioHeader()
        {
            foreach (var path in new[] { HudPath, MenuPath })
            {
                var menu = Load(path);
                string which = path.Contains("HUD") ? "HUD" : "MainMenu";

                foreach (var kind in new[] { SettingsMenu.RowKind.MasterVolume, SettingsMenu.RowKind.MusicVolume })
                {
                    var row = RowOf(menu, kind);
                    Assert.IsNotNull(row, which + ": no " + kind + " row");
                    Assert.IsNotNull(row.slider, which + ": " + kind + " has no slider");
                    Assert.IsNotNull(row.value, which + ": " + kind + " has no value text");
                    Assert.AreEqual(SettingsMenu.LabelFor(kind), row.label.text, which + ": " + kind + " label drifted");
                    // Hard rule 5: the fill is anchor-driven by the Slider, never Image.fillAmount.
                    var fillImg = row.slider.fillRect != null ? row.slider.fillRect.GetComponent<Image>() : null;
                    Assert.IsNotNull(fillImg, which + ": " + kind + " slider fill has no Image");
                    Assert.AreNotEqual(Image.Type.Filled, fillImg.type,
                        which + ": " + kind + " slider fill is Filled — a null-sprite Image ignores fillAmount");
                }

                bool header = false;
                foreach (var tr in menu.panel.GetComponentsInChildren<Transform>(true))
                    if (tr.name == "AUDIOHeader") header = true;
                Assert.IsTrue(header, which + ": the AUDIO rows have no section header; they read as more IMAGE settings");
            }
        }

        [Test]
        public void NothingOnTheSettingsPanelIsBrighterThanTheBloomCap()
        {
            foreach (var path in new[] { HudPath, MenuPath })
            {
                var menu = Load(path);
                foreach (var g in menu.panel.GetComponentsInChildren<Graphic>(true))
                    Assert.LessOrEqual(g.color.maxColorComponent, BloomCap,
                        path + ": " + g.name + " is HDR-bright (" + g.color + "). The UI never blooms.");
            }
        }

        [Test]
        public void TheRowsOnThePrefabSitWhereTheArithmeticSaysTheyDo()
        {
            var menu = Load(HudPath);
            var last = RowOf(menu, SettingsMenu.RowKind.MusicVolume);
            var rt = last.root.GetComponent<RectTransform>();
            Assert.AreEqual(SettingsPanelKit.LastRowY(SettingsMenu.AllKinds), rt.anchoredPosition.y, 0.5f,
                "the shipped MUSIC VOLUME row is not where LastRowY puts it — the builder and the test disagree");
            Assert.AreEqual(SettingsPanelKit.RowHeight, rt.sizeDelta.y, 0.5f, "the shipped row height drifted from the builder");
        }

        [Test]
        public void TheValueTextIsTheAuthoritativeReadout_OnEveryAudioRow()
        {
            // A slider can silently fail to draw; a string cannot. Both prefabs must carry the number.
            foreach (var path in new[] { HudPath, MenuPath })
            {
                var menu = Load(path);
                foreach (var kind in new[] { SettingsMenu.RowKind.MasterVolume, SettingsMenu.RowKind.MusicVolume })
                {
                    var value = RowOf(menu, kind).value;
                    Assert.IsInstanceOf<TMP_Text>(value, path + ": " + kind + " value is not text");
                    Assert.IsFalse(string.IsNullOrEmpty(SettingsMenu.ValueLabel(kind, SettingsData.Defaults())));
                }
            }
        }
    }
}
