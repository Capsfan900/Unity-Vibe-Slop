using System;
using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// THE RADIO (2026-09-06, the user: "a radio at the top right like a 2000s racing game -- just a simple
    /// system where I can load mp3s and make each level have a playlist"). Lives on the Managers prefab.
    ///
    /// <para><b>Content is files, not code.</b> Drop mp3 / ogg / wav files into
    /// <c>Assets/Resources/Audio/Radio/&lt;SceneName&gt;/</c> — for the campaign that is
    /// <c>Audio/Radio/Level_01/</c> — and they are that level's playlist, in name order.
    /// <c>Audio/Radio/Default/</c> plays for any scene with no folder of its own, and no files anywhere means
    /// the radio stays off and the ambient bed plays as before. The folder is keyed to the SCENE, which is the
    /// only level identity that exists at runtime: <c>LevelRegistry</c> is an editor asset under
    /// <c>Assets/Data</c> that nothing loads in a build, so a levelId lookup would always have come back null
    /// (found in play, 2026-09-06).</para>
    ///
    /// <para>It owns one 2D <see cref="AudioSource"/>, plays tracks in order with wrap, and exposes what a
    /// HUD radio needs to draw: <see cref="StationName"/>, <see cref="TrackTitle"/>, <see cref="TrackIndex"/> /
    /// <see cref="TrackCount"/>, <see cref="Progress"/>, <see cref="IsOn"/>, and <see cref="OnTrackChanged"/>.
    /// Input is read only through <see cref="InputReader"/> (rule 2): RadioNext, RadioPrevious, RadioToggle.
    /// Volume follows the AudioManager's music volume so one slider governs both. While the radio is ON the
    /// ambient music is ducked to zero (<see cref="AudioManager.MusicDuck"/>); the boss music still takes over.</para>
    /// </summary>
    public class LevelRadio : MonoBehaviour
    {
        public static LevelRadio I { get; private set; }

        [Tooltip("Seconds into a track after which PREVIOUS restarts it instead of going back (the car-stereo rule).")]
        public float previousRestartWindow = 3f;
        [Tooltip("Seconds of fade when the radio turns on, off or changes track.")]
        public float fadeSeconds = 0.35f;
        [Tooltip("Start playing as soon as a level with a playlist loads.")]
        public bool autoPlay = true;

        /// <summary>The station readout, from the scene ("LEVEL 01" → "LEVEL 01 FM" on the pane).</summary>
        public string StationName { get; private set; } = "";
        public string TrackTitle => tracks.Count > 0 && TrackIndex >= 0 ? RadioMath.Title(tracks[TrackIndex].name) : "";
        public int TrackIndex { get; private set; } = -1;
        public int TrackCount => tracks.Count;
        public float Progress => source != null && source.clip != null ? RadioMath.Progress(source.time, source.clip.length) : 0f;
        public bool IsOn { get; private set; }
        public bool HasPlaylist => tracks.Count > 0;
        /// <summary>Raised on every track change and on/off, for the HUD.</summary>
        public event Action OnTrackChanged;

        readonly List<AudioClip> tracks = new List<AudioClip>();
        AudioSource source;
        float fadeTarget, fadeVel;
        string loadedFolder;

        void Awake()
        {
            if (I != null && I != this) { Destroy(this); return; }
            I = this;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
        }

        void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += HandleSceneChanged;
        }

        void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= HandleSceneChanged;
            AudioManager.MusicDuck = 1f;
        }

        void Start()
        {
            // The Managers prefab is usually in the scene before LevelLoaded fires; if it fired already, load now.
            if (tracks.Count == 0) LoadForActiveLevel();
        }

        void HandleSceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to) { LoadForActiveLevel(); }

        /// <summary>Load the playlist for the scene that is open: Audio/Radio/&lt;SceneName&gt;, else Default.</summary>
        public void LoadForActiveLevel()
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            StationName = RadioMath.StationFor(scene);
            Load(scene);
        }

        /// <summary>Load <c>Resources/Audio/Radio/&lt;folder&gt;</c>, falling back to Default. Public for tests.</summary>
        public void Load(string folder)
        {
            string path = RadioMath.ResourcesFolder(folder);
            var clips = Resources.LoadAll<AudioClip>(path);
            if ((clips == null || clips.Length == 0) && path != RadioMath.ResourcesFolder(""))
            {
                path = RadioMath.ResourcesFolder("");
                clips = Resources.LoadAll<AudioClip>(path);
            }
            tracks.Clear();
            if (clips != null)
            {
                Array.Sort(clips, (a, b) => string.CompareOrdinal(a.name, b.name));
                tracks.AddRange(clips);
            }
            loadedFolder = path;
            TrackIndex = tracks.Count > 0 ? 0 : -1;
            if (tracks.Count > 0 && autoPlay) TurnOn(); else TurnOff();
            OnTrackChanged?.Invoke();
        }

        void Update()
        {
            if (source == null) return;
            var input = InputReader.I;
            if (input != null && GameManager.IsPlaying)
            {
                if (input.RadioNextPressed) Next();
                if (input.RadioPreviousPressed) Previous();
                if (input.RadioTogglePressed) Toggle();
            }
            // Volume: the music slider, faded on unscaled time (the radio keeps playing through pause and hitstop).
            float master = AudioManager.I != null ? AudioManager.I.musicVolume * AudioManager.I.masterVolume : 0.4f;
            float want = IsOn ? master : 0f;
            fadeTarget = Mathf.SmoothDamp(fadeTarget, want, ref fadeVel, Mathf.Max(0.01f, fadeSeconds), Mathf.Infinity, Time.unscaledDeltaTime);
            source.volume = fadeTarget;
            // Track end → next.
            if (IsOn && source.clip != null && !source.isPlaying && !GamePausedForAudio()) Next();
        }

        static bool GamePausedForAudio() { return AudioListener.pause; }

        public void Toggle() { if (IsOn) TurnOff(); else TurnOn(); }

        public void TurnOn()
        {
            if (tracks.Count == 0) { IsOn = false; return; }
            IsOn = true;
            if (TrackIndex < 0) TrackIndex = 0;
            PlayCurrent();
            AudioManager.MusicDuck = 0f;
            OnTrackChanged?.Invoke();
        }

        public void TurnOff()
        {
            IsOn = false;
            AudioManager.MusicDuck = 1f;
            OnTrackChanged?.Invoke();
        }

        public void Next()
        {
            if (tracks.Count == 0) return;
            TrackIndex = RadioMath.Next(TrackIndex, tracks.Count);
            if (!IsOn) TurnOn(); else PlayCurrent();
            OnTrackChanged?.Invoke();
        }

        public void Previous()
        {
            if (tracks.Count == 0) return;
            if (IsOn && source.clip != null && RadioMath.PreviousRestartsCurrent(source.time, previousRestartWindow))
            {
                source.time = 0f;
            }
            else
            {
                TrackIndex = RadioMath.Previous(TrackIndex, tracks.Count);
                if (!IsOn) TurnOn(); else PlayCurrent();
            }
            OnTrackChanged?.Invoke();
        }

        void PlayCurrent()
        {
            if (TrackIndex < 0 || TrackIndex >= tracks.Count) return;
            source.clip = tracks[TrackIndex];
            source.time = 0f;
            source.Play();
        }
    }
}
