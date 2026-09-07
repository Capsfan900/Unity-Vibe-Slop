using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Persistence for <see cref="SettingsData"/>, and the single place the live settings object lives.
    ///
    /// <para>PlayerPrefs on purpose: it is the least surprising store for this, it needs no file IO
    /// permissions in a WebGL build, and there is nothing here worth a save-game format.</para>
    ///
    /// <para><see cref="KeyPrefix"/> is public so an EditMode test can point the whole store at a
    /// throwaway namespace and put it back — otherwise running the suite would silently overwrite the
    /// developer's own sensitivity, which is exactly the kind of "test changed the game" bug this
    /// project has already paid for once elsewhere.</para>
    /// </summary>
    public static class SettingsStore
    {
        public static string KeyPrefix = "vg1.settings.";

        static SettingsData current;

        /// <summary>Raised after any successful <see cref="Save"/> or <see cref="ResetToDefaults"/>.</summary>
        public static event Action<SettingsData> Changed;

        /// <summary>
        /// The live settings. Loaded from prefs on first touch. Mutating this object directly is fine
        /// (the menu does), but nothing takes effect until <see cref="Save"/> is called.
        /// </summary>
        public static SettingsData Current
        {
            get
            {
                if (current == null) current = Load();
                return current;
            }
        }

        /// <summary>Test hook: drop the cached instance so the next read re-reads prefs.</summary>
        public static void Forget() { current = null; }

        public static SettingsData Load()
        {
            var d = SettingsData.Defaults();

            d.mouseSensitivity = GetFloat("mouseSens", d.mouseSensitivity);
            d.stickSensitivity = GetFloat("stickSens", d.stickSensitivity);
            d.fieldOfView = GetFloat("fov", d.fieldOfView);
            d.qualityLevel = GetInt("quality", d.qualityLevel);
            d.screenWidth = GetInt("screenW", d.screenWidth);
            d.screenHeight = GetInt("screenH", d.screenHeight);
            d.displayMode = (DisplayMode)GetInt("displayMode", (int)d.displayMode);
            d.vSync = GetInt("vsync", d.vSync);
            d.frameRateCap = GetInt("fpsCap", d.frameRateCap);
            d.bloomScale = GetFloat("bloom", d.bloomScale);
            d.filmGrain = GetInt("grain", d.filmGrain ? 1 : 0) != 0;
            d.masterVolume = GetFloat("volMaster", d.masterVolume);
            d.musicVolume = GetFloat("volMusic", d.musicVolume);

            d.Clamp();
            return d;
        }

        /// <summary>Clamp, write, flush, and tell everyone. The clamp is on the way OUT as well as in.</summary>
        public static void Save(SettingsData d)
        {
            if (d == null) return;
            d.Clamp();
            current = d;

            SetFloat("mouseSens", d.mouseSensitivity);
            SetFloat("stickSens", d.stickSensitivity);
            SetFloat("fov", d.fieldOfView);
            SetInt("quality", d.qualityLevel);
            SetInt("screenW", d.screenWidth);
            SetInt("screenH", d.screenHeight);
            SetInt("displayMode", (int)d.displayMode);
            SetInt("vsync", d.vSync);
            SetInt("fpsCap", d.frameRateCap);
            SetFloat("bloom", d.bloomScale);
            SetInt("grain", d.filmGrain ? 1 : 0);
            SetFloat("volMaster", d.masterVolume);
            SetFloat("volMusic", d.musicVolume);

            PlayerPrefs.Save();

            var h = Changed;
            if (h != null) h(d);
        }

        /// <summary>Convenience for the menu: persist whatever <see cref="Current"/> now holds.</summary>
        public static void Save() { Save(Current); }

        public static void ResetToDefaults()
        {
            Save(SettingsData.Defaults());
        }

        /// <summary>Test hook: remove every key this store owns.</summary>
        public static void DeleteAll()
        {
            foreach (var k in new[] { "mouseSens", "stickSens", "fov", "quality", "screenW", "screenH",
                                      "displayMode", "vsync", "fpsCap", "bloom", "grain",
                                      "volMaster", "volMusic" })
                PlayerPrefs.DeleteKey(KeyPrefix + k);
            PlayerPrefs.Save();
            current = null;
        }

        static float GetFloat(string k, float fallback) { return PlayerPrefs.GetFloat(KeyPrefix + k, fallback); }
        static int GetInt(string k, int fallback) { return PlayerPrefs.GetInt(KeyPrefix + k, fallback); }
        static void SetFloat(string k, float v) { PlayerPrefs.SetFloat(KeyPrefix + k, v); }
        static void SetInt(string k, int v) { PlayerPrefs.SetInt(KeyPrefix + k, v); }
    }
}
