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
            Assert.AreEqual("Audio/Radio/level_01", RadioMath.ResourcesFolder("level_01"));
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
        }
    }
}
