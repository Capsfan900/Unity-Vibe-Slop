using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The settings feature's logic layer, proven without a scene: clamping, persistence round-trip,
    /// every row's step behaviour, label totality, and the applier's pure pieces. The menu itself
    /// cannot be clicked in an EditMode run — this suite is deliberately written so that everything
    /// BEHIND the click is arithmetic that can.
    /// </summary>
    public class SettingsDataTests
    {
        // ------------------------------------------------------------------ defaults & clamping

        [Test]
        public void Defaults_AreTheShippedValues()
        {
            var d = SettingsData.Defaults();
            Assert.AreEqual(0.08f, d.mouseSensitivity, 1e-5f, "mouse default must match PlayerLook's shipped 0.08");
            Assert.AreEqual(180f, d.stickSensitivity, 1e-3f, "stick default must match PlayerLook's shipped 180");
            Assert.AreEqual(95f, d.fieldOfView, 1e-3f, "FOV default must match CameraFX.baseFov's shipped 95");
            Assert.AreEqual(1f, d.bloomScale, 1e-5f, "bloom default 100% == exactly the authored profile");
            Assert.AreEqual(1, d.vSync);
            Assert.AreEqual(0, d.frameRateCap, "uncapped by default");
            Assert.AreEqual(-1, d.qualityLevel, "-1 == keep the project's current quality level");
            Assert.AreEqual(DisplayMode.Borderless, d.displayMode);
            Assert.AreEqual(0, d.screenWidth);
            Assert.AreEqual(0, d.screenHeight);
            Assert.IsTrue(d.filmGrain);
        }

        [Test]
        public void Clamp_ForcesEveryFieldBackIntoRange()
        {
            var d = SettingsData.Defaults();
            d.mouseSensitivity = 900f;
            d.stickSensitivity = -5f;
            d.fieldOfView = 400f;
            d.bloomScale = -3f;
            d.vSync = 7;
            d.frameRateCap = 55;                  // not in the FrameCaps table
            d.displayMode = (DisplayMode)9;
            d.screenWidth = 1920; d.screenHeight = -1;
            d.qualityLevel = -12;

            d.Clamp();

            Assert.AreEqual(SettingsData.MouseSensMax, d.mouseSensitivity, 1e-5f);
            Assert.AreEqual(SettingsData.StickSensMin, d.stickSensitivity, 1e-3f);
            Assert.AreEqual(SettingsData.FovMax, d.fieldOfView, 1e-3f);
            Assert.AreEqual(SettingsData.BloomMin, d.bloomScale, 1e-5f);
            Assert.AreEqual(2, d.vSync);
            Assert.AreEqual(0, d.frameRateCap, "an off-table cap falls back to uncapped");
            Assert.AreEqual(DisplayMode.Borderless, d.displayMode);
            Assert.AreEqual(0, d.screenWidth, "a half-specified resolution collapses to native/native");
            Assert.AreEqual(0, d.screenHeight);
            Assert.AreEqual(-1, d.qualityLevel);
        }

        [Test]
        public void Clamp_SanitisesNaNAndInfinity_TheySurviveMathfClamp()
        {
            var d = SettingsData.Defaults();
            d.mouseSensitivity = float.NaN;
            d.fieldOfView = float.PositiveInfinity;
            d.Clamp();
            Assert.AreEqual(SettingsData.MouseSensDefault, d.mouseSensitivity, 1e-5f,
                "a NaN sensitivity written to prefs must come back as the default, not NaN — NaN look input blanks the view");
            Assert.AreEqual(SettingsData.FovDefault, d.fieldOfView, 1e-3f);
        }

        [Test]
        public void Clone_IsFieldComplete()
        {
            var d = SettingsData.Defaults();
            d.mouseSensitivity = 0.2f; d.stickSensitivity = 300f; d.fieldOfView = 110f;
            d.qualityLevel = 1; d.screenWidth = 1280; d.screenHeight = 720;
            d.displayMode = DisplayMode.Windowed; d.vSync = 0; d.frameRateCap = 144;
            d.bloomScale = 0.5f; d.filmGrain = false;

            var c = d.Clone();
            Assert.AreEqual(0.2f, c.mouseSensitivity, 1e-5f);
            Assert.AreEqual(300f, c.stickSensitivity, 1e-3f);
            Assert.AreEqual(110f, c.fieldOfView, 1e-3f);
            Assert.AreEqual(1, c.qualityLevel);
            Assert.AreEqual(1280, c.screenWidth);
            Assert.AreEqual(720, c.screenHeight);
            Assert.AreEqual(DisplayMode.Windowed, c.displayMode);
            Assert.AreEqual(0, c.vSync);
            Assert.AreEqual(144, c.frameRateCap);
            Assert.AreEqual(0.5f, c.bloomScale, 1e-5f);
            Assert.IsFalse(c.filmGrain);
        }

        // ------------------------------------------------------------------ cycling

        [Test]
        public void Cycle_WrapsBothDirections()
        {
            Assert.AreEqual(1, SettingsData.Cycle(0, 3, +1));
            Assert.AreEqual(0, SettingsData.Cycle(2, 3, +1), "forward off the end wraps to 0");
            Assert.AreEqual(2, SettingsData.Cycle(0, 3, -1), "backward off the start wraps to the end");
            Assert.AreEqual(0, SettingsData.Cycle(0, 0, +1), "an empty list cannot throw");
        }

        // ------------------------------------------------------------------ per-row stepping

        [Test]
        public void Step_Continuous_MovesByOneNotchAndClampsAtTheEnds()
        {
            var d = SettingsData.Defaults();
            float before = d.mouseSensitivity;
            SettingsMenu.Step(d, SettingsMenu.RowKind.MouseSensitivity, +1, null, 0);
            Assert.AreEqual(before + SettingsMenu.StepSize(SettingsMenu.RowKind.MouseSensitivity), d.mouseSensitivity, 1e-5f);

            d.mouseSensitivity = SettingsData.MouseSensMax;
            SettingsMenu.Step(d, SettingsMenu.RowKind.MouseSensitivity, +1, null, 0);
            Assert.AreEqual(SettingsData.MouseSensMax, d.mouseSensitivity, 1e-5f, "stepping past the max pins at the max");
        }

        [Test]
        public void Step_FrameCap_WalksTheTableAndWraps()
        {
            var d = SettingsData.Defaults();          // cap 0, index 0 in the table
            var caps = SettingsData.FrameCaps;

            for (int i = 1; i < caps.Length; i++)
            {
                SettingsMenu.Step(d, SettingsMenu.RowKind.FrameCap, +1, null, 0);
                Assert.AreEqual(caps[i], d.frameRateCap, "walk position " + i);
            }
            SettingsMenu.Step(d, SettingsMenu.RowKind.FrameCap, +1, null, 0);
            Assert.AreEqual(caps[0], d.frameRateCap, "off the end wraps to UNLIMITED");

            SettingsMenu.Step(d, SettingsMenu.RowKind.FrameCap, -1, null, 0);
            Assert.AreEqual(caps[caps.Length - 1], d.frameRateCap, "backward from UNLIMITED wraps to the top cap");
        }

        [Test]
        public void Step_DisplayModeAndVSync_CycleAllValues()
        {
            var d = SettingsData.Defaults();          // Borderless
            SettingsMenu.Step(d, SettingsMenu.RowKind.DisplayMode, +1, null, 0);
            Assert.AreEqual(DisplayMode.Windowed, d.displayMode);
            SettingsMenu.Step(d, SettingsMenu.RowKind.DisplayMode, +1, null, 0);
            Assert.AreEqual(DisplayMode.Fullscreen, d.displayMode, "wraps");

            d.vSync = 2;
            SettingsMenu.Step(d, SettingsMenu.RowKind.VSync, +1, null, 0);
            Assert.AreEqual(0, d.vSync, "vsync wraps 2 -> 0");
        }

        [Test]
        public void Step_FilmGrain_TogglesEitherDirection()
        {
            var d = SettingsData.Defaults();
            Assert.IsTrue(d.filmGrain);
            SettingsMenu.Step(d, SettingsMenu.RowKind.FilmGrain, +1, null, 0);
            Assert.IsFalse(d.filmGrain);
            SettingsMenu.Step(d, SettingsMenu.RowKind.FilmGrain, -1, null, 0);
            Assert.IsTrue(d.filmGrain);
        }

        [Test]
        public void Step_Resolution_WalksTheOptionListAndSurvivesAnEmptyOne()
        {
            var d = SettingsData.Defaults();          // 0x0 == native
            var options = new[] { new Vector2Int(2560, 1440), new Vector2Int(1920, 1080), new Vector2Int(1280, 720) };

            SettingsMenu.Step(d, SettingsMenu.RowKind.Resolution, +1, options, 0);
            Assert.AreEqual(1920, d.screenWidth, "native maps to index 0, +1 lands on the second entry");
            SettingsMenu.Step(d, SettingsMenu.RowKind.Resolution, +1, options, 0);
            Assert.AreEqual(1280, d.screenWidth);
            SettingsMenu.Step(d, SettingsMenu.RowKind.Resolution, +1, options, 0);
            Assert.AreEqual(2560, d.screenWidth, "wraps to the top");

            var e = SettingsData.Defaults();
            SettingsMenu.Step(e, SettingsMenu.RowKind.Resolution, +1, new Vector2Int[0], 0);
            Assert.AreEqual(0, e.screenWidth, "a headless run has no display list; the row is a no-op, not a throw");
        }

        [Test]
        public void Step_Quality_HonoursTheLiveLevelCountAndAnEmptyOne()
        {
            var d = SettingsData.Defaults();
            d.qualityLevel = 0;
            SettingsMenu.Step(d, SettingsMenu.RowKind.Quality, +1, null, 3);
            Assert.AreEqual(1, d.qualityLevel);
            d.qualityLevel = 2;
            SettingsMenu.Step(d, SettingsMenu.RowKind.Quality, +1, null, 3);
            Assert.AreEqual(0, d.qualityLevel, "wraps within the level count");

            var e = SettingsData.Defaults();
            SettingsMenu.Step(e, SettingsMenu.RowKind.Quality, +1, null, 0);
            Assert.AreEqual(-1, e.qualityLevel, "no quality levels -> no change, not an index out of range");
        }

        [Test]
        public void SetContinuous_ClampsOnTheWayIn()
        {
            var d = SettingsData.Defaults();
            SettingsMenu.SetContinuous(d, SettingsMenu.RowKind.Bloom, 99f);
            Assert.AreEqual(SettingsData.BloomMax, d.bloomScale, 1e-5f);
            SettingsMenu.SetContinuous(d, SettingsMenu.RowKind.FieldOfView, 1f);
            Assert.AreEqual(SettingsData.FovMin, d.fieldOfView, 1e-3f);
        }

        // ------------------------------------------------------------------ labels & row table

        [Test]
        public void AllKinds_CoversEveryRowKindExactlyOnce()
        {
            var all = SettingsMenu.AllKinds;
            var values = System.Enum.GetValues(typeof(SettingsMenu.RowKind));
            Assert.AreEqual(values.Length, all.Length, "AllKinds must list every RowKind — the builder emits from it");
            foreach (SettingsMenu.RowKind k in values)
                Assert.AreEqual(1, System.Array.FindAll(all, x => x == k).Length, k + " must appear exactly once");
        }

        [Test]
        public void EveryKind_HasANonEmptyLabelAndValue_EvenForNullData()
        {
            var d = SettingsData.Defaults();
            foreach (var k in SettingsMenu.AllKinds)
            {
                Assert.IsFalse(string.IsNullOrEmpty(SettingsMenu.LabelFor(k)), k + " has no label");
                Assert.IsFalse(string.IsNullOrEmpty(SettingsMenu.ValueLabel(k, d)), k + " has no value label");
                Assert.IsFalse(string.IsNullOrEmpty(SettingsMenu.ValueLabel(k, null)), k + " must not crash on null data");
            }
        }

        [Test]
        public void ContinuousRows_HaveARealRangeAndStep()
        {
            foreach (var k in SettingsMenu.AllKinds)
            {
                if (!SettingsMenu.IsContinuous(k)) continue;
                float lo, hi;
                SettingsMenu.ContinuousRange(k, out lo, out hi);
                Assert.Less(lo, hi, k + " range is degenerate");
                Assert.Greater(SettingsMenu.StepSize(k), 0f, k + " has no step — the < > buttons would be dead");
            }
        }

        [Test]
        public void Labels_ReadCorrectly()
        {
            Assert.AreEqual("UNLIMITED", SettingsData.FrameCapLabel(0));
            Assert.AreEqual("144 FPS", SettingsData.FrameCapLabel(144));
            Assert.AreEqual("OFF", SettingsData.VSyncLabel(0));
            Assert.AreEqual("ON", SettingsData.VSyncLabel(1));
            Assert.AreEqual("HALF RATE", SettingsData.VSyncLabel(2));
            Assert.AreEqual("NATIVE", SettingsData.ResolutionLabel(0, 0));
            Assert.AreEqual("1920 x 1080", SettingsData.ResolutionLabel(1920, 1080));
            Assert.AreEqual("100%", SettingsData.PercentLabel(1f));
            Assert.AreEqual("OFF", SettingsData.OnOffLabel(false));
        }

        // ------------------------------------------------------------------ bloom maths

        [Test]
        public void BloomIntensity_ScalesTheAuthoredValue_NeverAConstant()
        {
            var d = SettingsData.Defaults();
            d.bloomScale = 0.5f;
            Assert.AreEqual(0.6f, d.BloomIntensity(1.2f), 1e-5f, "50% of an authored 1.2 is 0.6");
            Assert.AreEqual(1.0f, d.BloomIntensity(2.0f), 1e-5f, "the profile can be re-authored under us and 50% follows it");
            d.bloomScale = 0f;
            Assert.AreEqual(0f, d.BloomIntensity(1.2f), 1e-5f, "0% is off");
            Assert.AreEqual(0f, d.BloomIntensity(-3f), 1e-5f, "a negative authored value never produces negative bloom");
        }

        // ------------------------------------------------------------------ applier's pure pieces

        [Test]
        public void DistinctResolutions_DedupesRefreshRates_LargestFirst()
        {
            var src = new[]
            {
                new Resolution { width = 1280, height = 720 },
                new Resolution { width = 1920, height = 1080 },
                new Resolution { width = 1920, height = 1080 },   // same pair, other refresh rate
                new Resolution { width = 2560, height = 1440 },
            };
            var got = SettingsApplier.DistinctResolutions(src);
            Assert.AreEqual(3, got.Length);
            Assert.AreEqual(new Vector2Int(2560, 1440), got[0], "largest first");
            Assert.AreEqual(new Vector2Int(1280, 720), got[2]);
            Assert.AreEqual(0, SettingsApplier.DistinctResolutions(null).Length, "headless: no list, no crash");
        }

        [Test]
        public void NearestResolutionIndex_ExactThenClosestThenSentinel()
        {
            var opts = new[] { new Vector2Int(2560, 1440), new Vector2Int(1920, 1080), new Vector2Int(1280, 720) };
            Assert.AreEqual(1, SettingsApplier.NearestResolutionIndex(1920, 1080, opts));
            Assert.AreEqual(1, SettingsApplier.NearestResolutionIndex(1900, 1060, opts), "no exact match -> closest by pixel count");
            Assert.AreEqual(0, SettingsApplier.NearestResolutionIndex(0, 0, opts), "native maps to the first entry");
            Assert.AreEqual(-1, SettingsApplier.NearestResolutionIndex(1920, 1080, new Vector2Int[0]));
        }

        [Test]
        public void ApplyLookTo_WritesBothSensitivities_WithoutTouchingPlayerLookCode()
        {
            // The whole reason the applier exists: PlayerLook.cs is another author's file, and its two
            // sensitivity fields are public. Build a bare PlayerLook and assert the one-way push.
            var go = new GameObject("test_look");
            try
            {
                var look = go.AddComponent<PlayerLook>();
                look.mouseSensitivity = 999f;
                look.stickSensitivity = 999f;

                var d = SettingsData.Defaults();
                d.mouseSensitivity = 0.25f;
                d.stickSensitivity = 320f;

                int n = SettingsApplier.ApplyLookTo(d, new[] { look, null });
                Assert.AreEqual(1, n, "one real PlayerLook written; the null slot skipped, not thrown on");
                Assert.AreEqual(0.25f, look.mouseSensitivity, 1e-5f);
                Assert.AreEqual(320f, look.stickSensitivity, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------ persistence

        [Test]
        public void Store_RoundTripsEveryField_UnderATestPrefix()
        {
            // KeyPrefix is swapped so this test can never overwrite the developer's own settings —
            // "the test changed the game" is a bug class this project has already paid for.
            string realPrefix = SettingsStore.KeyPrefix;
            SettingsStore.KeyPrefix = "vg1.test.settings.";
            try
            {
                SettingsStore.DeleteAll();

                var d = SettingsData.Defaults();
                d.mouseSensitivity = 0.22f;
                d.stickSensitivity = 260f;
                d.fieldOfView = 105f;
                d.qualityLevel = 1;
                d.screenWidth = 1280; d.screenHeight = 720;
                d.displayMode = DisplayMode.Windowed;
                d.vSync = 0;
                d.frameRateCap = 144;
                d.bloomScale = 0.35f;
                d.filmGrain = false;
                SettingsStore.Save(d);

                SettingsStore.Forget();                 // drop the cache; force a real prefs read
                var got = SettingsStore.Current;

                Assert.AreEqual(0.22f, got.mouseSensitivity, 1e-5f);
                Assert.AreEqual(260f, got.stickSensitivity, 1e-3f);
                Assert.AreEqual(105f, got.fieldOfView, 1e-3f);
                Assert.AreEqual(1, got.qualityLevel);
                Assert.AreEqual(1280, got.screenWidth);
                Assert.AreEqual(720, got.screenHeight);
                Assert.AreEqual(DisplayMode.Windowed, got.displayMode);
                Assert.AreEqual(0, got.vSync);
                Assert.AreEqual(144, got.frameRateCap);
                Assert.AreEqual(0.35f, got.bloomScale, 1e-5f);
                Assert.IsFalse(got.filmGrain);
            }
            finally
            {
                SettingsStore.DeleteAll();              // still under the test prefix
                SettingsStore.KeyPrefix = realPrefix;
                SettingsStore.Forget();
            }
        }

        [Test]
        public void Store_ClampsAHandEditedPrefsEntryOnLoad()
        {
            string realPrefix = SettingsStore.KeyPrefix;
            SettingsStore.KeyPrefix = "vg1.test.settings.";
            try
            {
                SettingsStore.DeleteAll();
                PlayerPrefs.SetFloat(SettingsStore.KeyPrefix + "mouseSens", 900f);
                PlayerPrefs.SetInt(SettingsStore.KeyPrefix + "vsync", 40);

                SettingsStore.Forget();
                var got = SettingsStore.Current;
                Assert.AreEqual(SettingsData.MouseSensMax, got.mouseSensitivity, 1e-5f,
                    "a prefs entry of 900 must not be able to make the game unplayable");
                Assert.AreEqual(2, got.vSync);
            }
            finally
            {
                SettingsStore.DeleteAll();
                SettingsStore.KeyPrefix = realPrefix;
                SettingsStore.Forget();
            }
        }

        [Test]
        public void ResetToDefaults_ActuallyResets()
        {
            string realPrefix = SettingsStore.KeyPrefix;
            SettingsStore.KeyPrefix = "vg1.test.settings.";
            try
            {
                SettingsStore.DeleteAll();
                var d = SettingsData.Defaults();
                d.mouseSensitivity = 0.3f;
                SettingsStore.Save(d);

                SettingsStore.ResetToDefaults();
                SettingsStore.Forget();
                Assert.AreEqual(SettingsData.MouseSensDefault, SettingsStore.Current.mouseSensitivity, 1e-5f);
            }
            finally
            {
                SettingsStore.DeleteAll();
                SettingsStore.KeyPrefix = realPrefix;
                SettingsStore.Forget();
            }
        }

        // ------------------------------------------------------------------ mode mapping

        [Test]
        public void DisplayMode_RoundTripsThroughUnitysEnum()
        {
            foreach (DisplayMode m in System.Enum.GetValues(typeof(DisplayMode)))
            {
                var d = SettingsData.Defaults();
                d.displayMode = m;
                Assert.AreEqual(m, SettingsData.FromFullScreenMode(d.ToFullScreenMode()), m + " does not round-trip");
            }
        }
    }
}
