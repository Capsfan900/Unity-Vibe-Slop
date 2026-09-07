using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>The radio (2026-09-06): playlist arithmetic, the shipped bindings and the Managers wiring.</summary>
    public class RadioTests
    {
        [Test]
        public void ThePlaylistWraps_AndAnEmptyOneIsMinusOne()
        {
            Assert.AreEqual(1, RadioMath.Next(0, 3)); Assert.AreEqual(0, RadioMath.Next(2, 3));
            Assert.AreEqual(2, RadioMath.Previous(0, 3)); Assert.AreEqual(1, RadioMath.Previous(2, 3));
            Assert.AreEqual(-1, RadioMath.Next(0, 0)); Assert.AreEqual(-1, RadioMath.Previous(5, 0));
            Assert.AreEqual(0, RadioMath.Next(-1, 4), "a fresh radio's first NEXT is track 0");
        }

        [Test]
        public void InputIsAcceptedInEveryStateExceptEditing()
        {
            // The bug (2026-09-06, "skipping does not work"): NEXT/PREVIOUS/TOGGLE were gated to
            // GameState.Playing only, so opening the pause menu, the level-up screen or dying silently
            // swallowed the keypress even though the radio itself keeps playing through pause. Editing is
            // the one state that must still block: the level editor binds [ and ] to EditorPrev/EditorNext.
            foreach (GameState s in System.Enum.GetValues(typeof(GameState)))
            {
                bool expected = s != GameState.Editing;
                Assert.AreEqual(expected, RadioMath.AcceptsInput(s), s + " should accept radio input: " + expected);
            }
        }

        [Test]
        public void PreviousIsTheCarStereoRule()
        {
            Assert.IsFalse(RadioMath.PreviousRestartsCurrent(1f, 3f), "early in a track, PREVIOUS goes back");
            Assert.IsTrue(RadioMath.PreviousRestartsCurrent(3.5f, 3f), "later, it restarts the track");
        }

        [Test]
        public void ProgressAndTitlesAreSafe()
        {
            Assert.AreEqual(0.5f, RadioMath.Progress(30f, 60f), 1e-4f);
            Assert.AreEqual(0f, RadioMath.Progress(5f, 0f), 1e-4f, "a zero-length clip is 0, not NaN");
            Assert.AreEqual(1f, RadioMath.Progress(90f, 60f), 1e-4f);
            Assert.AreEqual("Audio/Radio/Level_01", RadioMath.ResourcesFolder("Level_01"));
            Assert.AreEqual("LEVEL 01", RadioMath.StationFor("Level_01"), "the scene names the station");
            Assert.AreEqual("", RadioMath.StationFor(null));
            Assert.AreEqual("Audio/Radio/Default", RadioMath.ResourcesFolder(""));
            Assert.AreEqual("Audio/Radio/x", RadioMath.ResourcesFolder(" x/ "));
            Assert.AreEqual("03 Ash Cathedral", RadioMath.Title("03_Ash-Cathedral"));
        }

        [Test]
        public void TheRadioShipsOnManagers_WithItsKeys()
        {
            var managers = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Managers.prefab");
            if (managers == null) Assert.Ignore("run 4. Build Prefabs");
            var radio = managers.GetComponent<LevelRadio>();
            if (radio == null) Assert.Ignore("Managers was built before LevelRadio existed; run 4. Build Prefabs");
            Assert.AreEqual(3f, radio.previousRestartWindow, 1e-4f);
            Assert.IsTrue(radio.autoPlay);
            string json = System.IO.File.ReadAllText("Assets/InputSystem_Actions.inputactions");
            foreach (var n in new[] { "RadioNext", "RadioPrevious", "RadioToggle" })
                Assert.IsTrue(json.Contains("\"name\": \"" + n + "\""), n + " is not in the actions asset");
            Assert.IsTrue(System.IO.Directory.Exists("Assets/Resources/Audio/Radio"), "the playlist root folder ships");
            // The folder a scene looks for must exist for the campaign level, or a playlist dropped in by hand
            // has nowhere obvious to go.
            Assert.IsTrue(System.IO.Directory.Exists("Assets/Resources/Audio/Radio/Level_01"),
                "Assets/Resources/Audio/Radio/Level_01 is where Level_01's mp3s go");
            Assert.IsTrue(System.IO.Directory.Exists("Assets/Resources/Audio/Radio/Default"),
                "Audio/Radio/Default is the fallback playlist");
        }
    }
}
