using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Pooled one-shot player + crossfading music.
    /// Real (CC0) clips are loaded from Resources/Audio/Sfx/&lt;SfxName&gt;/* (random variant per play) and
    /// Resources/Audio/Music/{ambient,boss}. Any Sfx folder that is empty falls back to a synthesized clip.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }

        [Range(0f, 1f)] public float masterVolume = 0.7f;
        /// <summary>0..1 multiplier on the music bed. LevelRadio writes 0 while the radio plays and 1 when it is off.</summary>
        public static float MusicDuck = 1f;
        [Range(0f, 1f)] public float musicVolume = 0.45f;
        public float musicFadeSeconds = 1.5f;

        const int PoolSize = 12;

        readonly Dictionary<Sfx, AudioClip[]> library = new Dictionary<Sfx, AudioClip[]>();
        readonly Dictionary<Sfx, float> trim = new Dictionary<Sfx, float>
        {
            { Sfx.Tick, 0.55f }, { Sfx.Swing, 0.45f }, { Sfx.Dash, 0.5f }, { Sfx.Jump, 0.4f }, { Sfx.Click, 0.35f },
            { Sfx.Souls, 0.5f }, { Sfx.Roar, 0.9f }, { Sfx.Parry, 0.9f }, { Sfx.Hit, 0.8f }, { Sfx.Checkpoint, 0.6f },
            { Sfx.Death, 0.8f }, { Sfx.Heal, 0.7f }, { Sfx.Stagger, 0.8f }, { Sfx.Hurt, 0.9f }, { Sfx.Execute, 1f },
            { Sfx.Ultimate, 0.9f }, { Sfx.Block, 0.8f },
            { Sfx.ParryCue, 0.75f }, { Sfx.Footstep, 0.3f }, { Sfx.Land, 0.5f }, { Sfx.PostureBreak, 0.9f },
            { Sfx.Thunder, 1f }, { Sfx.ItemPickup, 0.6f }, { Sfx.ItemUse, 0.7f }
        };

        AudioSource[] pool;
        int next;
        AudioSource musicA, musicB;
        AudioSource activeMusic;
        AudioClip ambientMusic, bossMusic;
        Coroutine fade;
        bool bossPlaying;

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;

            pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++) pool[i] = MakeSource("Sfx" + i, false);

            foreach (Sfx s in System.Enum.GetValues(typeof(Sfx)))
            {
                if (s == Sfx.Drone) continue;
                var clips = Resources.LoadAll<AudioClip>("Audio/Sfx/" + s);
                library[s] = clips != null && clips.Length > 0 ? clips : new[] { ProceduralSfx.Build(s) };
            }

            ambientMusic = Resources.Load<AudioClip>("Audio/Music/ambient");
            bossMusic = Resources.Load<AudioClip>("Audio/Music/boss");
            if (ambientMusic == null) ambientMusic = ProceduralSfx.Build(Sfx.Drone);
            if (bossMusic == null) bossMusic = ambientMusic;

            musicA = MakeSource("MusicA", true);
            musicB = MakeSource("MusicB", true);
        }

        AudioSource MakeSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;
            return src;
        }

        void OnEnable()
        {
            GameEvents.BossStarted += OnBossStarted;
            GameEvents.BossDefeated += OnBossEnded;
            GameEvents.PlayerRespawned += OnBossEnded;
        }

        void OnDisable()
        {
            GameEvents.BossStarted -= OnBossStarted;
            GameEvents.BossDefeated -= OnBossEnded;
            GameEvents.PlayerRespawned -= OnBossEnded;
        }

        void Start()
        {
            // NOTE: Play() inside the same Awake that AddComponent'ed a source silently fails during scene load.
            PlayMusic(ambientMusic, 0f);
        }

        void Update()
        {
            // The radio's duck is applied live so turning it on or off mid-track is heard at once.
            if (activeMusic != null && fade == null) activeMusic.volume = musicVolume * masterVolume * MusicDuck;

            // watchdog: keep music alive (editor focus loss, audio device change, etc.)
            if (activeMusic != null && activeMusic.clip != null && !activeMusic.isPlaying && fade == null && Time.frameCount % 30 == 0)
                activeMusic.Play();
        }

        void OnBossStarted(BossController b) { if (!bossPlaying) { bossPlaying = true; PlayMusic(bossMusic, musicFadeSeconds); } }
        void OnBossEnded() { if (bossPlaying) { bossPlaying = false; PlayMusic(ambientMusic, musicFadeSeconds * 2f); } }

        public void PlayMusic(AudioClip clip, float fadeSeconds)
        {
            if (clip == null) return;
            if (activeMusic != null && activeMusic.clip == clip && activeMusic.isPlaying) return;
            var from = activeMusic;
            var to = activeMusic == musicA ? musicB : musicA;
            to.clip = clip;
            to.volume = fadeSeconds <= 0f ? musicVolume * masterVolume * MusicDuck : 0f;
            to.Play();
            activeMusic = to;
            if (fade != null) StopCoroutine(fade);
            fade = StartCoroutine(FadeCo(from, to, fadeSeconds));
        }

        IEnumerator FadeCo(AudioSource from, AudioSource to, float seconds)
        {
            float t = 0f;
            float fromStart = from != null ? from.volume : 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.01f, seconds));
                to.volume = musicVolume * masterVolume * MusicDuck * k;
                if (from != null) from.volume = fromStart * (1f - k);
                yield return null;
            }
            to.volume = musicVolume * masterVolume * MusicDuck;
            if (from != null) { from.Stop(); from.volume = 0f; }
            fade = null;
        }

        public static void Play(Sfx s, float volume = 1f, float pitch = 1f, float pitchJitter = 0.05f)
        {
            if (I == null) return;
            I.PlayInternal(s, volume, pitch, pitchJitter);
        }

        void PlayInternal(Sfx s, float volume, float pitch, float jitter)
        {
            if (!library.TryGetValue(s, out var clips) || clips == null || clips.Length == 0) return;
            var clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;
            var src = pool[next];
            next = (next + 1) % PoolSize;
            src.pitch = pitch + Random.Range(-jitter, jitter);
            float t = trim.TryGetValue(s, out var tv) ? tv : 0.7f;
            src.PlayOneShot(clip, volume * masterVolume * t);
        }
    }
}
