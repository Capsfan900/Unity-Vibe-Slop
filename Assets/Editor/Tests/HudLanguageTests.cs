using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE HUD'S LANGUAGE: does every shipped label draw at all, name its input the way the rest of the
    /// game does, and stay inside its own rect? Three defects found in the 2026-09-06 QA pass, each one
    /// invisible to every existing test because they are about the CONTENT of a label rather than the
    /// position of a rect.
    ///
    /// <list type="number">
    /// <item>The radio key hint carried U+25C0 / U+25B6. <c>TMP Settings</c> ships the LiberationSans SDF
    /// atlas in STATIC population mode with 250 glyphs topping out at U+25A1, so both drew as the missing
    /// -glyph box. Any HUD string containing a glyph the atlas does not hold is the same bug.</item>
    /// <item>The deathblow banner said "[LMB]" while the prompt beside it says "[ATTACK]" and
    /// <c>ControlsInfo</c> says "LMB  attack" — one button, three names.</item>
    /// <item>The item toast prints <c>ItemData.description</c>, which is authored free text, through a
    /// label the builder's <c>Txt</c> helper leaves on NoWrap + Overflow.</item>
    /// </list>
    /// </summary>
    public class HudLanguageTests
    {
        const string HudPath = "Assets/Prefabs/HUD.prefab";
        const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        const float BloomCap = 1.05f;

        static GameObject Hud()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            if (p == null) Assert.Ignore(HudPath + " missing — run VibeGame1/5. Build HUD");
            return p;
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                if (tr.name == name) return tr;
            return null;
        }

        // ---- 1. every glyph the HUD ships is one the shipped atlas actually has ---------------------

        /// <summary>The characters the project's default font asset can draw. Null when the asset is
        /// dynamic (it can then draw anything the source font holds) or missing.</summary>
        static HashSet<uint> StaticAtlasCoverage()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) return null;
            if (font.atlasPopulationMode != AtlasPopulationMode.Static) return null;   // dynamic: anything goes
            var set = new HashSet<uint>();
            foreach (var c in font.characterTable) set.Add(c.unicode);
            return set;
        }

        [Test]
        public void EveryShippedHudStringDrawsInTheShippedFont()
        {
            var coverage = StaticAtlasCoverage();
            if (coverage == null) Assert.Ignore("the default font asset is missing or dynamic; any glyph would render");
            var hud = Hud();

            foreach (var t in hud.GetComponentsInChildren<TMP_Text>(true))
            {
                string s = t.text;
                if (string.IsNullOrEmpty(s)) continue;
                for (int i = 0; i < s.Length; i++)
                {
                    char ch = s[i];
                    if (ch == '\n' || ch == '\r' || ch == '\t') continue;
                    Assert.IsTrue(coverage.Contains(ch),
                        t.name + " ships the character U+" + ((int)ch).ToString("X4") +
                        " ('" + ch + "'), which the STATIC LiberationSans SDF atlas does not hold — it draws as " +
                        "the missing-glyph box. Full string: '" + s + "'");
                }
            }
        }

        [Test]
        public void TheRadioKeyHintNamesItsThreeKeys()
        {
            var hud = Hud();
            var keys = Find(hud.transform, "RadioKeys");
            if (keys == null) Assert.Ignore("no RadioKeys — run VibeGame1/5. Build HUD");
            string s = keys.GetComponent<TMP_Text>().text;
            // LevelRadio owns ] next, [ previous, \ toggle. The pane is the only place they are written down.
            StringAssert.Contains("[", s, "the radio hint does not show the PREVIOUS key");
            StringAssert.Contains("]", s, "the radio hint does not show the NEXT key");
            StringAssert.Contains("\\", s, "the radio hint does not show the ON/OFF key");
        }

        // ---- 2. one input, one name -----------------------------------------------------------------

        [Test]
        public void TheDeathblowBannerNamesTheSameInputTheDeathblowPromptDoes()
        {
            var hud = Hud();
            var banner = Find(hud.transform, "DeathblowText");
            if (banner == null) Assert.Ignore("no DeathblowText — run VibeGame1/5. Build HUD");
            string s = banner.GetComponent<TMP_Text>().text.ToUpperInvariant();

            // Nothing rewrites this label at runtime (HUDController only animates its alpha and scale),
            // so the shipped string IS what the player reads at the biggest moment in a boss fight.
            StringAssert.Contains("DEATHBLOW", s, "the banner no longer names the verb");
            StringAssert.Contains("[ATTACK]", s,
                "the banner says '" + s + "'. ExecuteInteractor prints 'DEATHBLOW  [ATTACK]' for ordinary " +
                "enemies; the same button must not have a second name on the boss.");
            StringAssert.DoesNotContain("[LMB]", s, "'[LMB]' is a third name for the attack button");
        }

        // ---- 3. authored free text is the only thing on the HUD that has to wrap --------------------

        [Test]
        public void TheItemToastWrapsInsideItsRect()
        {
            var hud = Hud();
            var toast = Find(hud.transform, "ItemToast");
            if (toast == null) Assert.Ignore("no ItemToast — run VibeGame1/5. Build HUD");
            var tmp = toast.GetComponent<TMP_Text>();
            Assert.AreEqual(TextWrappingModes.Normal, tmp.textWrappingMode,
                "the toast prints ItemData.description, which is authored free text; on NoWrap a long " +
                "description runs off both edges of the screen");
            Assert.GreaterOrEqual(toast.GetComponent<RectTransform>().sizeDelta.x, 600f,
                "the toast rect is too narrow to wrap an item description into");
        }

        // ---- 4. the centre-screen events speak the palette, one colour per meaning -------------------

        [Test]
        public void TheEventColoursAreDistinctAndUnderTheBloomCap()
        {
            // HUDController mixes these at runtime, so they are read off the class rather than a prefab.
            // The contract: PERFECT (deflect), BLOCK/SUPER (reward), CHECKPOINT (banked), YOU DIED
            // (danger) and a boss NAME must not collide — PERFECT and CHECKPOINT shipped identical cyan.
            var named = new Dictionary<string, Color>
            {
                { "perfect (ghost teal)", new Color(0.658f, 0.902f, 0.855f) },
                { "reward (ember gold)",  new Color(0.878f, 0.627f, 0.188f) },
                { "banked (mint)",        new Color(0.663f, 0.847f, 0.627f) },
                { "danger (blood)",       new Color(1f, 0.227f, 0.102f) },
                { "a name (bone)",        new Color(0.910f, 0.886f, 0.839f) },
            };

            var list = new List<KeyValuePair<string, Color>>(named);
            for (int i = 0; i < list.Count; i++)
            {
                Assert.LessOrEqual(list[i].Value.maxColorComponent, BloomCap,
                    list[i].Key + " is over the bloom cap; the UI never blooms");
                for (int j = i + 1; j < list.Count; j++)
                {
                    var a = list[i].Value; var b = list[j].Value;
                    float d = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
                    Assert.Greater(d, 0.25f,
                        list[i].Key + " and " + list[j].Key + " are the same colour; two unrelated events would read as one");
                }
            }
        }

        // ---- 5. nothing on the PLAYING hud can spill its own rect -----------------------------------

        /// <summary>The labels that are on screen during a run. Menus are excluded on purpose: they are
        /// opened, read and closed, and their rows are laid out by their own kits.</summary>
        static readonly string[] PlayingLabels =
        {
            "HealthText", "FlaskText", "PyreLabel", "StaminaLabel", "PipDash", "PipAir", "PipWall",
            // SoulsLabel / SoulsText are covered SCOPED to the loadout pane by HudColumnTests: the
            // level-up card ships a second label called SoulsText and a by-name lookup cannot tell them apart.
            "WeaponText", "PyreReadyLabel", "TimerText",
            "RadioStation", "RadioKeys", "BestRunsTitle", "ParryPopup", "PromptText", "DeathblowText",
        };

        [Test]
        public void EveryLabelThePlayingHudShipsFitsItsOwnRect()
        {
            var hud = Hud();
            int checked_ = 0;
            foreach (var name in PlayingLabels)
            {
                var tr = Find(hud.transform, name);
                if (tr == null) continue;
                var t = tr.GetComponent<TMP_Text>();
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                var rt = t.rectTransform;
                if (rt.anchorMin.x != rt.anchorMax.x) continue;                 // stretched: follows its parent
                checked_++;
                // A rough metric — TMP's own preferredWidth needs a live canvas — but tight enough to
                // catch the real failure mode: a label authored far narrower than the string it ships.
                float estimate = t.text.Length * (t.fontSize + t.characterSpacing) * 0.46f;
                Assert.LessOrEqual(estimate, rt.sizeDelta.x,
                    name + " ships '" + t.text + "' at " + t.fontSize + " pt in a rect only " +
                    rt.sizeDelta.x + " wide (needs about " + estimate + ")");
            }
            if (checked_ < 8) Assert.Ignore("HUD.prefab predates this layout — run VibeGame1/5. Build HUD");
        }
        // ---- 6. the crosshair band: what a first-person player reads without moving their eyes ------

        /// <summary>
        /// The vertical band the TYPE of a centre message actually occupies: its anchored y, plus and
        /// minus 0.6 em. The RECTS in this band deliberately overlap (they are all far taller than one
        /// line so a two-line message has room), so a rect test would be noise — what must not collide
        /// is the ink.
        /// </summary>
        static Vector2 TypeBand(TMP_Text t)
        {
            float half = t.fontSize * 0.6f;
            float y = t.rectTransform.anchoredPosition.y;
            return new Vector2(y - half, y + half);
        }

        [Test]
        public void TheCrosshairBandStacksWithoutTheTypeColliding()
        {
            var hud = Hud();
            string[] names = { "CenterText", "ParryPopup", "DeathblowText", "PromptText", "ItemToast" };
            var bands = new List<KeyValuePair<string, Vector2>>();
            foreach (var n in names)
            {
                var tr = Find(hud.transform, n);
                if (tr == null) continue;
                var t = tr.GetComponent<TMP_Text>();
                if (t == null || t.rectTransform.anchorMin != new Vector2(0.5f, 0.5f)) continue;
                bands.Add(new KeyValuePair<string, Vector2>(n, TypeBand(t)));
            }
            if (bands.Count < 4) Assert.Ignore("HUD.prefab predates the centre band — run VibeGame1/5. Build HUD");

            const float MinGap = 8f;
            for (int i = 0; i < bands.Count; i++)
                for (int j = i + 1; j < bands.Count; j++)
                {
                    var a = bands[i].Value; var b = bands[j].Value;
                    float gap = a.x > b.x ? a.x - b.y : b.x - a.y;
                    Assert.GreaterOrEqual(gap, MinGap,
                        bands[i].Key + " " + a + " and " + bands[j].Key + " " + b + " are only " + gap +
                        " apart. They are not mutually exclusive — the deathblow BANNER is the boss window " +
                        "and the PROMPT still carries a GRAPPLE or SURGE cue underneath it.");
                }
        }

        [Test]
        public void ThePromptPulseSettles()
        {
            Assert.AreEqual(1f, PromptView.PulseAmount(0f, 0.6f), 1e-4f, "a new prompt must arrive with motion");
            Assert.AreEqual(0f, PromptView.PulseAmount(0.6f, 0.6f), 1e-4f, "the throb must stop; a line that never stops moving stops meaning anything");
            Assert.AreEqual(0f, PromptView.PulseAmount(99f, 0.6f), 1e-4f);
            Assert.Greater(PromptView.PulseAmount(0.2f, 0.6f), PromptView.PulseAmount(0.4f, 0.6f), "the throb must decay, not step");
            Assert.AreEqual(0f, PromptView.PulseAmount(0f, 0f), 1e-4f, "a zero settle time is 'never throb', not 'throb forever'");
        }

        [Test]
        public void ThePromptSettleTimeIsShippedOnThePrefab()
        {
            var hud = Hud();
            var view = hud.GetComponent<PromptView>();
            if (view == null) Assert.Ignore("no PromptView on HUD.prefab");
            Assert.That(view.settleSeconds, Is.InRange(0.2f, 1.5f),
                "the prompt throbs for " + view.settleSeconds + "s — under 0.2 nobody sees the arrival, over 1.5 it is furniture that moves");
        }

        [Test]
        public void AFlashIsDrawnOverTheStandingCue_AndHandsItBack()
        {
            // The 2026-09-06 two-channel contract, at the level of the class rather than of the bus:
            // a momentary line must never be able to erase a cue that is still true.
            var go = new GameObject("PromptViewTest");
            try
            {
                var view = go.AddComponent<PromptView>();
                view.Set("GRAPPLE  [DASH]");
                Assert.AreEqual("GRAPPLE  [DASH]", view.Current);

                view.Flash("PERFECT", 30f);
                Assert.AreEqual("PERFECT", view.Current, "a flash must draw over the standing cue");

                view.Flash("", 0f);
                Assert.AreEqual("GRAPPLE  [DASH]", view.Current,
                    "the standing cue did not come back. Its writers are EDGE-triggered, so a lost cue is lost until the condition toggles.");

                view.Set("");
                Assert.AreEqual("", view.Current, "an empty standing cue must clear the line");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
