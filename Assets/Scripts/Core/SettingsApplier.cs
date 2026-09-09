using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace VibeGame1
{
    /// <summary>
    /// Pushes <see cref="SettingsStore.Current"/> outward onto everything it affects, and is the ONLY
    /// thing that does. Nothing in the settings UI reaches into a gameplay class.
    ///
    /// <para><b>Why an applier rather than the menu writing values directly.</b> Sensitivity lives on
    /// <c>PlayerLook.mouseSensitivity</c> / <c>stickSensitivity</c>, which are public fields on a class
    /// this feature deliberately does not edit. Writing them from here keeps the settings feature a
    /// one-way push: store → applier → targets. It also means the values are applied on scene load, not
    /// only when someone opens the menu — a player who never opens the menu still gets their saved
    /// sensitivity, and a player who changes it mid-run keeps it across a death and respawn.</para>
    ///
    /// <para><b>Bootstrap.</b> A <see cref="RuntimeInitializeOnLoadMethod"/> spawns one
    /// <c>DontDestroyOnLoad</c> instance before the first scene's Awake, so BOTH the menu scene and any
    /// level get settings applied without either builder having to remember to place a component. It
    /// re-applies on every <c>sceneLoaded</c> because the objects it writes to (PlayerLook, CameraFX,
    /// the volume) are per-scene and did not exist the last time it ran.</para>
    ///
    /// <para><b>Bloom.</b> Driven through the scene's URP <c>Volume</c>, never hardcoded, so it composes
    /// with whatever the volume profile is currently authored to. It reads the AUTHORED intensity once
    /// (the first time it sees a given profile instance) and thereafter writes authored × scale — so a
    /// change to the profile asset moves the player's 100% with it. Crucially it goes through
    /// <c>Volume.profile</c>, which is a RUNTIME CLONE of <c>sharedProfile</c>: writing
    /// <c>sharedProfile</c> would dirty the project asset on disk from play mode, which is how a
    /// "temporary" post tweak becomes a permanent one nobody meant to commit. <c>CameraFX</c> already
    /// uses the same clone for its chromatic-aberration pulses.</para>
    /// </summary>
    public class SettingsApplier : MonoBehaviour
    {
        public static SettingsApplier I { get; private set; }

        /// <summary>Diagnostics: how many times settings have been pushed. Read by tests and the log.</summary>
        public int ApplyCount { get; private set; }
        bool loggedEditorNote;

        // The volume profile instance we last measured, and the bloom/grain intensity it was AUTHORED
        // with. Cached per instance so re-applying never compounds (scale-of-a-scaled-value).
        VolumeProfile measuredProfile;
        float authoredBloom = -1f;
        float authoredGrain = -1f;

        // The AudioManager we last measured, and the gains it was AUTHORED with (Managers.prefab ships
        // master 0.7 / music 0.45). Measured ONCE per instance and deliberately NOT reset on a scene
        // load: re-measuring the same instance would read back our own scaled value and compound it
        // every load until the game was silent. A new scene builds a new AudioManager, and a destroyed
        // one compares != to it, which is the whole guard.
        AudioManager measuredAudio;
        float authoredMaster = -1f;
        float authoredMusic = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (I != null) return;
            var go = new GameObject("SettingsApplier");
            DontDestroyOnLoad(go);
            go.AddComponent<SettingsApplier>();
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            SettingsStore.Changed += OnSettingsChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyAll();
        }

        void OnDestroy()
        {
            if (I != this) return;
            I = null;
            SettingsStore.Changed -= OnSettingsChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSettingsChanged(SettingsData d) { ApplyAll(); }

        void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            // The per-scene targets are constructed in their own Awake/Start; applying on the very next
            // frame rather than here means CameraFX has already captured cam.fieldOfView, so our FOV is
            // the last word rather than being overwritten a frame later.
            measuredProfile = null;
            ApplyAll();
            Invoke("ApplyAll", 0f);
        }

        /// <summary>Push everything. Safe to call at any time and from anywhere; idempotent.</summary>
        public void ApplyAll()
        {
            var d = SettingsStore.Current;
            ApplyCount++;

            ApplyLook(d);
            ApplyBindings(d);
            ApplyCamera(d);
            ApplyQuality(d);
            ApplyDisplay(d);
            ApplyPost(d);
            ApplyAudio(d);
            ApplyArmMovementSetting(d);
        }

        // ------------------------------------------------------------------ sensitivity

        /// <summary>
        /// Write sensitivity onto every PlayerLook in the scene. Public and static so an EditMode test
        /// can build a bare PlayerLook and assert the write, with no scene and no play mode.
        /// </summary>
        public static int ApplyLookTo(SettingsData d, PlayerLook[] looks)
        {
            if (d == null || looks == null) return 0;
            int n = 0;
            for (int i = 0; i < looks.Length; i++)
            {
                if (looks[i] == null) continue;
                looks[i].mouseSensitivity = d.mouseSensitivity;
                looks[i].stickSensitivity = d.stickSensitivity;
                n++;
            }
            return n;
        }

        void ApplyLook(SettingsData d)
        {
            ApplyLookTo(d, FindObjectsByType<PlayerLook>(FindObjectsInactive.Include));
        }

        // ------------------------------------------------------------------ key bindings

        /// <summary>
        /// Push the player's rebindable key onto <see cref="InputReader"/> — the only class allowed to
        /// touch the Input System (hard rule 2). Null-safe on purpose: the front-end scene has no
        /// InputReader at all, and the level's one applies the same value itself in Awake, so this is
        /// the "changed it mid-run" path rather than the only path.
        /// </summary>
        void ApplyBindings(SettingsData d)
        {
            if (InputReader.I == null) return;
            InputReader.I.ApplyWeaponTwirlOverride(d.weaponTwirlBinding);
        }

        // ------------------------------------------------------------------ field of view

        void ApplyCamera(SettingsData d)
        {
            // CameraFX owns the camera's FOV every frame (baseFov + kick), so writing Camera.fieldOfView
            // alone would be overwritten on the next Update. Write BOTH: the component where one exists,
            // and the camera directly for a scene (like the menu) that has no CameraFX.
            var fx = FindObjectsByType<CameraFX>(FindObjectsInactive.Include);
            for (int i = 0; i < fx.Length; i++)
            {
                if (fx[i] == null) continue;
                fx[i].baseFov = d.fieldOfView;
                if (fx[i].cam != null) fx[i].cam.fieldOfView = d.fieldOfView;
            }
            if (fx.Length == 0 && Camera.main != null && !Camera.main.orthographic)
                Camera.main.fieldOfView = d.fieldOfView;
        }

        // ------------------------------------------------------------------ quality / frame pacing

        void ApplyQuality(SettingsData d)
        {
            int levels = QualitySettings.names != null ? QualitySettings.names.Length : 0;
            if (d.qualityLevel >= 0 && levels > 0)
            {
                int lvl = Mathf.Clamp(d.qualityLevel, 0, levels - 1);
                if (QualitySettings.GetQualityLevel() != lvl)
                    QualitySettings.SetQualityLevel(lvl, true);
            }

            // vSyncCount is set AFTER the quality level: SetQualityLevel loads the level's own vSync
            // value, so setting ours first would be silently reverted.
            QualitySettings.vSyncCount = Mathf.Clamp(d.vSync, 0, 2);

            // targetFrameRate is ignored while vSync is on, which is correct and is why the menu greys
            // the cap out rather than pretending it does something.
            Application.targetFrameRate = d.frameRateCap <= 0 ? -1 : d.frameRateCap;
        }

        // ------------------------------------------------------------------ resolution / window

        void ApplyDisplay(SettingsData d)
        {
            var mode = d.ToFullScreenMode();
            int nativeW = 0, nativeH = 0;
            if (Display.main != null)
            {
                nativeW = Display.main.systemWidth;
                nativeH = Display.main.systemHeight;
            }
            if (nativeW <= 0 || nativeH <= 0)
            {
                nativeW = Screen.currentResolution.width;
                nativeH = Screen.currentResolution.height;
            }
            Vector2Int target = TargetResolution(d.screenWidth, d.screenHeight,
                nativeW, nativeH, Screen.width, Screen.height);
            int w = target.x;
            int h = target.y;
            if (w <= 0 || h <= 0) return;

            // Re-issuing the same resolution every scene load costs a window flicker on some drivers.
            if (Screen.width == w && Screen.height == h && Screen.fullScreenMode == mode) return;

#if UNITY_EDITOR
            // In the editor the Game view owns the resolution; Screen.SetResolution is a no-op that
            // logs. Skip it so a developer's layout is never fought over, and say so once. (A flag,
            // not ApplyCount: the count is already past 1 by the time a real difference shows up.)
            if (!loggedEditorNote)
            {
                loggedEditorNote = true;
                Debug.Log("[Settings] Resolution/display mode are saved but not applied in the editor — " +
                          "the Game view owns them. They take effect in a build.");
            }
#elif UNITY_WEBGL
            // The browser owns the canvas. fullScreenMode never reports FullScreenWindow outside a user
            // gesture, so the equality guard above fails on every scene load and SetResolution would
            // pin the canvas to a fixed size, detaching it from the page's responsive layout.
#else
            Screen.SetResolution(w, h, mode);
#endif
        }

        /// <summary>
        /// Resolve the saved 0/0 sentinel against the DISPLAY, not the current game window. Reading
        /// <c>Screen.width</c> for native made a previously forced 1366x768 window redefine "native"
        /// as 1366x768 forever. Current window dimensions are only the last-resort headless fallback.
        /// </summary>
        public static Vector2Int TargetResolution(int savedWidth, int savedHeight,
            int nativeWidth, int nativeHeight, int currentWidth, int currentHeight)
        {
            if (savedWidth > 0 && savedHeight > 0) return new Vector2Int(savedWidth, savedHeight);
            if (nativeWidth > 0 && nativeHeight > 0) return new Vector2Int(nativeWidth, nativeHeight);
            return new Vector2Int(Mathf.Max(0, currentWidth), Mathf.Max(0, currentHeight));
        }

        /// <summary>
        /// Distinct width×height pairs the display offers, largest first, deduplicated across refresh
        /// rates. Pure given its input, which is why the menu takes it from here rather than reading
        /// <c>Screen.resolutions</c> inline: a headless run has an empty list and must not crash.
        /// </summary>
        public static Vector2Int[] DistinctResolutions(Resolution[] source)
        {
            if (source == null || source.Length == 0) return new Vector2Int[0];

            var list = new System.Collections.Generic.List<Vector2Int>();
            for (int i = source.Length - 1; i >= 0; i--)   // Screen.resolutions is ascending
            {
                var v = new Vector2Int(source[i].width, source[i].height);
                if (v.x <= 0 || v.y <= 0) continue;
                if (!list.Contains(v)) list.Add(v);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Index of the entry matching w×h, or the closest by pixel count if there is no exact match.
        /// Returns -1 for an empty list. Pure — the one piece of resolution logic that is unit-tested.
        /// </summary>
        public static int NearestResolutionIndex(int w, int h, Vector2Int[] options)
        {
            if (options == null || options.Length == 0) return -1;
            if (w <= 0 || h <= 0) return 0;

            int best = 0;
            long bestErr = long.MaxValue;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i].x == w && options[i].y == h) return i;
                long err = System.Math.Abs((long)options[i].x * options[i].y - (long)w * h);
                if (err < bestErr) { bestErr = err; best = i; }
            }
            return best;
        }

        // ------------------------------------------------------------------ visual movement

        /// <summary>Push the persisted cosmetic-arm toggle onto the pure movement-pose channel.</summary>
        public static bool ApplyArmMovementSetting(SettingsData d)
        {
            bool enabled = d == null || d.armMovement;
            MovementPose.Enabled = enabled;
            return enabled;
        }

        // ------------------------------------------------------------------ audio

        /// <summary>
        /// Push the two volume scales onto <c>AudioManager</c>'s public gains. It is a push, like every
        /// other setting: nothing in the settings feature owns the mixer, and <c>AudioManager</c> reads
        /// both fields live (music every Update, SFX on every one-shot), so a slider is heard on the
        /// frame it moves without anything having to be re-triggered.
        ///
        /// <para>There is no SFX row because there is no SFX bus: <c>AudioManager.PlayInternal</c>
        /// multiplies by <c>masterVolume</c> alone. Master and music are the two gains that exist.</para>
        /// </summary>
        void ApplyAudio(SettingsData d)
        {
            var am = AudioManager.I;
            if (am == null) return;
            if (am != measuredAudio)
            {
                measuredAudio = am;
                authoredMaster = am.masterVolume;
                authoredMusic = am.musicVolume;
            }
            am.masterVolume = d.MasterGain(authoredMaster);
            am.musicVolume = d.MusicGain(authoredMusic);
        }

        // ------------------------------------------------------------------ post processing

        void ApplyPost(SettingsData d)
        {
            var volumes = FindObjectsByType<Volume>(FindObjectsInactive.Include);
            for (int i = 0; i < volumes.Length; i++)
            {
                var v = volumes[i];
                if (v == null || !v.isGlobal) continue;

                // Volume.profile is a runtime CLONE. Never sharedProfile — that is the asset on disk.
                var profile = v.profile;
                if (profile == null) continue;

                if (profile != measuredProfile)
                {
                    measuredProfile = profile;
                    authoredBloom = -1f;
                    authoredGrain = -1f;
                }

                Bloom bloom;
                if (profile.TryGet(out bloom) && bloom != null)
                {
                    if (authoredBloom < 0f) authoredBloom = bloom.intensity.value;
                    float target = d.BloomIntensity(authoredBloom);
                    bloom.intensity.Override(target);
                    bloom.active = target > 0.0001f;
                }

                FilmGrain grain;
                if (profile.TryGet(out grain) && grain != null)
                {
                    if (authoredGrain < 0f) authoredGrain = grain.intensity.value;
                    grain.intensity.Override(d.filmGrain ? authoredGrain : 0f);
                    grain.active = d.filmGrain && authoredGrain > 0.0001f;
                }

                break;   // one global volume is the project's shape; the rest are local overrides
            }
        }
    }
}
