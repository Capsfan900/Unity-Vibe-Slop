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
            Assert.IsTrue(d.armMovement, "movement reactions are cosmetic but ship enabled");
            Assert.AreEqual("", d.bindingOverridesJson, "fresh profiles use the action asset defaults");
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
            d.bloomScale = 0.5f; d.filmGrain = false; d.armMovement = false;
            d.bindingOverridesJson = "{\"bindings\":[]}";

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
            Assert.IsFalse(c.armMovement);
            Assert.AreEqual(d.bindingOverridesJson, c.bindingOverridesJson);
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
        public void Step_ArmMovement_TogglesEitherDirection()
        {
            var d = SettingsData.Defaults();
            Assert.IsTrue(d.armMovement);
            SettingsMenu.Step(d, SettingsMenu.RowKind.ArmMovement, +1, null, 0);
            Assert.IsFalse(d.armMovement);
            SettingsMenu.Step(d, SettingsMenu.RowKind.ArmMovement, -1, null, 0);
            Assert.IsTrue(d.armMovement);
        }

        [Test]
        public void Step_Resolution_WalksTheOptionListAndSurvivesAnEmptyOne()
        {
            var d = SettingsData.Defaults();          // 0x0 == native
            var options = new[] { new Vector2Int(2560, 1440), new Vector2Int(1920, 1080), new Vector2Int(1280, 720) };

            SettingsMenu.Step(d, SettingsMenu.RowKind.Resolution, +1, options, 0);
            Assert.AreEqual(2560, d.screenWidth, "NATIVE is its own index zero; +1 lands on the first concrete mode");
            SettingsMenu.Step(d, SettingsMenu.RowKind.Resolution, +1, options, 0);
            Assert.AreEqual(1920, d.screenWidth);
            SettingsMenu.Step(d, SettingsMenu.RowKind.Resolution, +1, options, 0);
            Assert.AreEqual(1280, d.screenWidth);
            SettingsMenu.Step(d, SettingsMenu.RowKind.Resolution, +1, options, 0);
            Assert.AreEqual(0, d.screenWidth, "the row can always return to the persisted NATIVE sentinel");
            Assert.AreEqual(0, d.screenHeight);

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
        public void TargetResolution_NativeMeansTheDisplay_NotTheAlreadyResizedWindow()
        {
            Assert.AreEqual(new Vector2Int(2560, 1440),
                SettingsApplier.TargetResolution(0, 0, 2560, 1440, 1366, 768),
                "a stale game window must not redefine native");
            Assert.AreEqual(new Vector2Int(1920, 1080),
                SettingsApplier.TargetResolution(1920, 1080, 2560, 1440, 1366, 768),
                "an explicit saved resolution still wins");
            Assert.AreEqual(new Vector2Int(1366, 768),
                SettingsApplier.TargetResolution(0, 0, 0, 0, 1366, 768),
                "headless/no-display fallback is the current surface");
        }

        [Test]
        public void ArmMovementSetting_PushesOntoTheVisualPoseChannel()
        {
            bool before = MovementPose.Enabled;
            try
            {
                var d = SettingsData.Defaults();
                d.armMovement = false;
                Assert.IsFalse(SettingsApplier.ApplyArmMovementSetting(d));
                Assert.IsFalse(MovementPose.Enabled);
                d.armMovement = true;
                Assert.IsTrue(SettingsApplier.ApplyArmMovementSetting(d));
                Assert.IsTrue(MovementPose.Enabled);
            }
            finally { MovementPose.Enabled = before; }
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
                d.armMovement = false;
                d.bindingOverridesJson = "{\"bindings\":[]}";
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
                Assert.IsFalse(got.armMovement);
                Assert.AreEqual("{\"bindings\":[]}", got.bindingOverridesJson);
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

        // ------------------------------------------------------------------ the flourish key (rebind)

        [Test]
        public void FreshProfile_HasNoBindingOverride_AndFallsBackToF11()
        {
            var d = SettingsData.Defaults();
            Assert.AreEqual("", d.weaponTwirlBinding, "a fresh profile must carry NO override");
            Assert.AreEqual(SettingsData.WeaponTwirlDefaultBinding, d.WeaponTwirlBindingOrDefault());
            Assert.AreEqual("F11", SettingsMenu.ValueLabel(SettingsMenu.RowKind.WeaponTwirlKey, d),
                "with no override the row must read the shipped default key");
        }

        [Test]
        public void OrdinaryKeybinds_CannotCaptureConsoleOrDeveloperKeys()
        {
            string[] reserved =
            {
                "<Keyboard>/escape", "<Keyboard>/backquote", "<Keyboard>/enter",
                "<Keyboard>/numpadEnter", "<Gamepad>/start", "<Keyboard>/4", "<Keyboard>/f1",
                "<Keyboard>/f5", "<Keyboard>/f6", "<Keyboard>/f7", "<Keyboard>/f8",
                "<Keyboard>/f9", "<Keyboard>/f10",
            };
            foreach (string path in reserved)
                Assert.IsTrue(InputReader.IsReservedBindingPath(path), path + " must stay reserved");

            Assert.IsFalse(InputReader.IsReservedBindingPath("<Keyboard>/f11"),
                "F11 is the shipped flourish key, not a developer key");
            Assert.IsFalse(InputReader.IsReservedBindingPath("<Keyboard>/h"));
            Assert.IsFalse(InputReader.IsReservedBindingPath("<Gamepad>/buttonSouth"));
        }

        [Test]
        public void CorruptBindingPaths_AreEmptied_NeverApplied()
        {
            // Every one of these would leave the action bound to NOTHING if it reached
            // ApplyBindingOverride, i.e. a silently dead key with no way back short of wiping prefs.
            string[] junk = { null, "", "   ", "f11", "<Keyboard>", "<Keyboard>/", "Keyboard/f11",
                              "<>/f11", "<Keyboard>/escape", "<KEYBOARD>/ESCAPE", new string('x', 200) };
            foreach (var bad in junk)
            {
                var d = SettingsData.Defaults();
                d.weaponTwirlBinding = bad;
                d.Clamp();
                Assert.AreEqual("", d.weaponTwirlBinding, "'" + (bad ?? "null") + "' survived sanitising");
                Assert.AreEqual(SettingsData.WeaponTwirlDefaultBinding, d.WeaponTwirlBindingOrDefault());
            }
        }

        [Test]
        public void AGoodBindingPath_SurvivesClampAndClone()
        {
            var d = SettingsData.Defaults();
            d.weaponTwirlBinding = "  <Keyboard>/h  ";
            d.Clamp();
            Assert.AreEqual("<Keyboard>/h", d.weaponTwirlBinding, "a legal path must survive, trimmed");
            Assert.AreEqual("<Keyboard>/h", d.Clone().weaponTwirlBinding, "Clone drops the binding");
            Assert.AreEqual("H", SettingsMenu.ValueLabel(SettingsMenu.RowKind.WeaponTwirlKey, d));
        }

        [Test]
        public void KeyLabel_ReadsAsAPlayerWouldSayIt()
        {
            Assert.AreEqual("F11", SettingsData.KeyLabel("<Keyboard>/f11"));
            Assert.AreEqual("MOUSE MIDDLEBUTTON", SettingsData.KeyLabel("<Mouse>/middleButton"));
            Assert.AreEqual("PAD BUTTONNORTH", SettingsData.KeyLabel("<Gamepad>/buttonNorth"));
            Assert.AreEqual("UNBOUND", SettingsData.KeyLabel(""));
        }

        [Test]
        public void TheRebindRow_ResetsOnTheRightButton_AndCannotBeStepped()
        {
            var d = SettingsData.Defaults();
            d.weaponTwirlBinding = "<Keyboard>/h";
            d.Clamp();

            // decrease (-1) is REBIND: it starts a listen and must change no data at all.
            SettingsMenu.Step(d, SettingsMenu.RowKind.WeaponTwirlKey, -1, null, 0);
            Assert.AreEqual("<Keyboard>/h", d.weaponTwirlBinding, "REBIND must not edit the stored path itself");

            // increase (+1) is RESET.
            SettingsMenu.Step(d, SettingsMenu.RowKind.WeaponTwirlKey, +1, null, 0);
            Assert.AreEqual("", d.weaponTwirlBinding, "RESET must clear the override");
        }

        [Test]
        public void TheRebindRow_IsInTheControlSection_AndHasNoSlider()
        {
            var all = SettingsMenu.AllKinds;
            int key = System.Array.IndexOf(all, SettingsMenu.RowKind.WeaponTwirlKey);
            int firstDisplay = System.Array.IndexOf(all, SettingsMenu.RowKind.Resolution);
            Assert.Greater(key, 0, "FLOURISH KEY must not open the panel");
            Assert.Less(key, firstDisplay, "FLOURISH KEY belongs to CONTROL, above the DISPLAY section header");
            Assert.IsTrue(SettingsMenu.IsRebind(SettingsMenu.RowKind.WeaponTwirlKey));
            Assert.IsFalse(SettingsMenu.IsContinuous(SettingsMenu.RowKind.WeaponTwirlKey),
                "a key is not a range — a slider here would be a lie");
            Assert.AreEqual("FLOURISH KEY", SettingsMenu.LabelFor(SettingsMenu.RowKind.WeaponTwirlKey));
        }

        [Test]
        public void TheDefaultBinding_IsTheOneTheActionsAssetShips()
        {
            Assert.AreEqual(SettingsData.WeaponTwirlDefaultBinding, "<Keyboard>/f11",
                "the shipped default moved; InputSystem_Actions.inputactions must move with it");
            Assert.AreEqual("WeaponTwirl", InputReader.WeaponTwirlActionName,
                "the action name moved; InputSystem_Actions.inputactions must move with it");
        }

        /// <summary>
        /// The shape the rebind must persist. `InputControl.path` is a RUNTIME path ("/Keyboard/f11":
        /// leading slash, no device brackets); a BINDING path is "&lt;Keyboard&gt;/f11". Sanitize rejects the
        /// former on purpose - ApplyBindingOverride cannot resolve it - so a rebind that hands over a
        /// control path silently becomes a cancel and the key snaps back to the default. That shipped
        /// on 2026-09-07 and the only reachable key was F11. See ENGINEERING-LOG.
        /// </summary>
        [Test]
        public void OnlyABindingPathSurvivesSanitising_NotARuntimeControlPath()
        {
            Assert.AreEqual("<Keyboard>/f11", SettingsData.SanitizeBindingPath("<Keyboard>/f11"),
                "a binding path is what ApplyBindingOverride takes");
            Assert.AreEqual("", SettingsData.SanitizeBindingPath("/Keyboard/f11"),
                "a runtime control path must be rejected - it cannot be resolved as an override");
            Assert.AreEqual("", SettingsData.SanitizeBindingPath("/gamepad/leftStick/x"));

            foreach (var good in new[] { "<Keyboard>/h", "<Keyboard>/f11", "<Mouse>/middleButton", "<Gamepad>/buttonNorth" })
                Assert.AreEqual(good, SettingsData.SanitizeBindingPath(good), good + " is a legal binding path");
        }

        [Test]
        public void CompleteBindingOverrideJson_IsBoundedAndCloned()
        {
            var d = SettingsData.Defaults();
            d.bindingOverridesJson = "  {\"bindings\":[]}  ";
            d.Clamp();
            Assert.AreEqual("{\"bindings\":[]}", d.bindingOverridesJson);
            Assert.AreEqual(d.bindingOverridesJson, d.Clone().bindingOverridesJson);

            d.bindingOverridesJson = "not json";
            d.Clamp();
            Assert.AreEqual("", d.bindingOverridesJson, "malformed prefs must fall back to asset defaults");

            d.bindingOverridesJson = "{" + new string('x', SettingsData.BindingOverridesJsonMaxLength) + "}";
            d.Clamp();
            Assert.AreEqual("", d.bindingOverridesJson, "an oversized prefs blob must be rejected");
        }

        [Test]
        public void EveryOrdinaryKeybindHasAUniqueStableId()
        {
            Assert.AreEqual(24, InputReader.RebindableBindings.Length,
                "movement, combat, items, weapon selection, progression and radio should all be exposed");
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var binding in InputReader.RebindableBindings)
            {
                Assert.IsTrue(ids.Add(binding.id), "duplicate binding id " + binding.id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(binding.label));
                Assert.IsFalse(string.IsNullOrWhiteSpace(binding.actionName));
                Assert.AreEqual(binding.defaultPath, SettingsData.SanitizeBindingPath(binding.defaultPath),
                    binding.id + " does not carry a legal default control path");
            }
        }
    }
}
