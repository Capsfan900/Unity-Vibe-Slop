using UnityEngine;

namespace VibeGame1
{
    /// <summary>How the game window is presented. Values are stable — they are persisted as ints.</summary>
    public enum DisplayMode
    {
        Fullscreen = 0,     // exclusive fullscreen
        Borderless = 1,     // fullscreen window (desktop resolution, alt-tab friendly)
        Windowed = 2,
    }

    /// <summary>
    /// The player's settings, as plain data. Deliberately a POCO with no Unity object references:
    /// everything here can be constructed, clamped, compared and round-tripped in an EditMode test
    /// without a scene, which is the only way any of this gets verified (the menu itself cannot be
    /// clicked headlessly).
    ///
    /// <para>Ownership: <see cref="SettingsStore"/> persists it, <see cref="SettingsApplier"/> pushes it
    /// outward onto <c>PlayerLook</c>, <c>QualitySettings</c>, <c>Screen</c> and the URP volume, and
    /// <c>SettingsMenu</c> edits it. Nothing reads gameplay classes backwards, and in particular nothing
    /// here or in the menu touches <c>PlayerLook.cs</c> — the applier writes its public fields.</para>
    ///
    /// <para>Hard rule 9 in spirit: the numbers below are the SHIPPED defaults, asserted in
    /// <c>SettingsDataTests</c>. There is no ScriptableObject to diverge from them.</para>
    /// </summary>
    [System.Serializable]
    public class SettingsData
    {
        // ---- ranges (public so the menu and the tests share one source of truth) ----------------

        public const float MouseSensMin = 0.01f;
        public const float MouseSensMax = 0.50f;
        public const float MouseSensDefault = 0.08f;    // PlayerLook's own shipped value

        public const float StickSensMin = 40f;
        public const float StickSensMax = 500f;
        public const float StickSensDefault = 180f;     // PlayerLook's own shipped value

        public const float FovMin = 70f;
        public const float FovMax = 120f;
        public const float FovDefault = 95f;            // CameraFX.baseFov's shipped value

        public const float BloomMin = 0f;
        public const float BloomMax = 2f;
        public const float BloomDefault = 1f;           // 1.0 == exactly the authored volume profile

        // Volumes are SCALES on the mix AudioManager ships with (master 0.7, music 0.45 on
        // Managers.prefab), exactly the way bloomScale is a scale on the authored volume profile.
        // 100% therefore means "the mix as tuned", not "unity gain": a player who never touches these
        // hears what the audio pass authored, and retuning the prefab moves everyone's 100% with it.
        // There is deliberately no SFX row — AudioManager has one SFX path and it runs through
        // masterVolume (AudioManager.cs:157). A third slider would control nothing.
        public const float VolumeMin = 0f;
        public const float VolumeMax = 1f;
        public const float VolumeDefault = 1f;

        /// <summary>Selectable frame-rate caps. 0 means uncapped. Persisted by VALUE, not by index.</summary>
        public static readonly int[] FrameCaps = { 0, 30, 60, 90, 120, 144, 240 };

        /// <summary>VSync counts Unity accepts: off, every v-blank, every second v-blank.</summary>
        public static readonly int[] VSyncOptions = { 0, 1, 2 };

        // ---- the settings themselves -------------------------------------------------------------

        public float mouseSensitivity = MouseSensDefault;
        public float stickSensitivity = StickSensDefault;
        public float fieldOfView = FovDefault;

        /// <summary>Index into QualitySettings.names. Clamped against the live list on apply. -1 keeps the project's current level.</summary>
        public int qualityLevel = -1;

        public int screenWidth = 0;                     // 0/0 == "whatever the display is already at"
        public int screenHeight = 0;
        public DisplayMode displayMode = DisplayMode.Borderless;

        public int vSync = 1;
        public int frameRateCap = 0;                    // 0 == uncapped

        /// <summary>Multiplier on the volume profile's AUTHORED bloom intensity. 0 turns bloom off.</summary>
        public float bloomScale = BloomDefault;

        public bool filmGrain = true;

        /// <summary>Scale on AudioManager's authored master gain. Affects SFX and music alike.</summary>
        public float masterVolume = VolumeDefault;
        /// <summary>Scale on AudioManager's authored music gain. The level radio rides this too.</summary>
        public float musicVolume = VolumeDefault;

        // ---- construction ------------------------------------------------------------------------

        public static SettingsData Defaults()
        {
            return new SettingsData();
        }

        public SettingsData Clone()
        {
            return new SettingsData
            {
                mouseSensitivity = mouseSensitivity,
                stickSensitivity = stickSensitivity,
                fieldOfView = fieldOfView,
                qualityLevel = qualityLevel,
                screenWidth = screenWidth,
                screenHeight = screenHeight,
                displayMode = displayMode,
                vSync = vSync,
                frameRateCap = frameRateCap,
                bloomScale = bloomScale,
                filmGrain = filmGrain,
                masterVolume = masterVolume,
                musicVolume = musicVolume,
            };
        }

        // ---- validation --------------------------------------------------------------------------

        /// <summary>
        /// Force every field back into a legal range. Pure, and called on BOTH save and load: a prefs
        /// entry hand-edited to mouseSensitivity = 900 must not be able to make the game unplayable,
        /// and neither must a value written by a build with a different range.
        /// </summary>
        public void Clamp()
        {
            mouseSensitivity = Mathf.Clamp(Sane(mouseSensitivity, MouseSensDefault), MouseSensMin, MouseSensMax);
            stickSensitivity = Mathf.Clamp(Sane(stickSensitivity, StickSensDefault), StickSensMin, StickSensMax);
            fieldOfView = Mathf.Clamp(Sane(fieldOfView, FovDefault), FovMin, FovMax);
            bloomScale = Mathf.Clamp(Sane(bloomScale, BloomDefault), BloomMin, BloomMax);
            masterVolume = Mathf.Clamp(Sane(masterVolume, VolumeDefault), VolumeMin, VolumeMax);
            musicVolume = Mathf.Clamp(Sane(musicVolume, VolumeDefault), VolumeMin, VolumeMax);

            if (screenWidth < 0) screenWidth = 0;
            if (screenHeight < 0) screenHeight = 0;
            if (screenWidth == 0 || screenHeight == 0) { screenWidth = 0; screenHeight = 0; }

            if (displayMode < DisplayMode.Fullscreen || displayMode > DisplayMode.Windowed)
                displayMode = DisplayMode.Borderless;

            vSync = Mathf.Clamp(vSync, 0, 2);

            if (IndexOf(FrameCaps, frameRateCap) < 0) frameRateCap = 0;

            if (qualityLevel < -1) qualityLevel = -1;
        }

        /// <summary>NaN and infinity survive Mathf.Clamp unchanged; a saved NaN sensitivity blanks the view.</summary>
        static float Sane(float v, float fallback)
        {
            return (float.IsNaN(v) || float.IsInfinity(v)) ? fallback : v;
        }

        public static int IndexOf(int[] arr, int value)
        {
            if (arr == null) return -1;
            for (int i = 0; i < arr.Length; i++) if (arr[i] == value) return i;
            return -1;
        }

        // ---- labels (pure; the menu shows exactly these strings) ----------------------------------

        public static string FrameCapLabel(int cap)
        {
            return cap <= 0 ? "UNLIMITED" : cap + " FPS";
        }

        public static string VSyncLabel(int count)
        {
            if (count <= 0) return "OFF";
            return count == 1 ? "ON" : "HALF RATE";
        }

        public static string DisplayModeLabel(DisplayMode m)
        {
            switch (m)
            {
                case DisplayMode.Fullscreen: return "FULLSCREEN";
                case DisplayMode.Windowed: return "WINDOWED";
                default: return "BORDERLESS";
            }
        }

        public static string ResolutionLabel(int w, int h)
        {
            return (w <= 0 || h <= 0) ? "NATIVE" : w + " x " + h;
        }

        public static string PercentLabel(float scale)
        {
            return Mathf.RoundToInt(scale * 100f) + "%";
        }

        public static string OnOffLabel(bool on)
        {
            return on ? "ON" : "OFF";
        }

        /// <summary>
        /// Wrap-around step used by every cycler in the menu. Pure, and the reason the menu has no
        /// off-by-one: one tested function drives every list.
        /// </summary>
        public static int Cycle(int index, int count, int delta)
        {
            if (count <= 0) return 0;
            int i = (index + delta) % count;
            if (i < 0) i += count;
            return i;
        }

        /// <summary>The bloom intensity to hand the volume, given whatever the profile authored.</summary>
        public float BloomIntensity(float authoredIntensity)
        {
            return Mathf.Max(0f, authoredIntensity) * bloomScale;
        }

        /// <summary>The master gain to hand AudioManager, given whatever the prefab authored. Clamped
        /// to 0..1 because AudioSource.volume above 1 clips rather than getting louder.</summary>
        public float MasterGain(float authored)
        {
            return Mathf.Clamp01(Mathf.Max(0f, authored) * masterVolume);
        }

        /// <summary>The music gain to hand AudioManager, given whatever the prefab authored.</summary>
        public float MusicGain(float authored)
        {
            return Mathf.Clamp01(Mathf.Max(0f, authored) * musicVolume);
        }

        /// <summary>Unity's own fullscreen enum for this mode.</summary>
        public FullScreenMode ToFullScreenMode()
        {
            switch (displayMode)
            {
                case DisplayMode.Fullscreen: return FullScreenMode.ExclusiveFullScreen;
                case DisplayMode.Windowed: return FullScreenMode.Windowed;
                default: return FullScreenMode.FullScreenWindow;
            }
        }

        public static DisplayMode FromFullScreenMode(FullScreenMode m)
        {
            switch (m)
            {
                case FullScreenMode.ExclusiveFullScreen: return DisplayMode.Fullscreen;
                case FullScreenMode.Windowed: return DisplayMode.Windowed;
                default: return DisplayMode.Borderless;
            }
        }
    }
}
