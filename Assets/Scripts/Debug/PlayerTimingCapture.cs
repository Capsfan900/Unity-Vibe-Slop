using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VibeGame1
{
    /// <summary>
    /// Opt-in local developer capture for investigating a player's real traversal and projectile-parry
    /// timing. It observes existing public state only, never drives input, combat, movement, or files
    /// until the player explicitly asks to export from the developer console.
    /// </summary>
    public class PlayerTimingCapture : MonoBehaviour
    {
        public const int MaxSamples = 30000;
        public const int MaxEvents = 12000;
        public const float SampleHz = 30f;
        const string FolderName = "timing-captures";

        static PlayerTimingCapture instance;

        [Serializable]
        public class CaptureFile
        {
            public int formatVersion = 1;
            public string sceneName;
            public float sampleHz = SampleHz;
            public string startedUtc;
            public string stoppedUtc;
            public float durationSeconds;
            public int droppedSamples;
            public int droppedEvents;
            public List<MovementSample> samples = new List<MovementSample>();
            public List<TimingEvent> events = new List<TimingEvent>();
        }

        [Serializable]
        public class MovementSample
        {
            public float captureSeconds;
            public float worldTime;
            public Vector3 position;
            public Vector3 velocity;
            public float speed;
            public bool grounded;
            public bool sliding;
            public bool wallRunning;
            public bool dashing;
            public bool pulling;
            public bool parryPressed;
        }

        [Serializable]
        public class TimingEvent
        {
            public string kind;
            public float captureSeconds;
            public float worldTime;
            public string boltId;
            public string shooter;
            public string spawner;
            public string data;
            public int phraseId;
            public int phraseOrdinal;
            public float predictedContactAt;
            public Vector3 playerPosition;
            public float playerSpeed;
            public bool playerSliding;
            public string result;
        }

        sealed class ObservedBolt
        {
            public Projectile projectile;
            public string id;
            public string shooter;
            public string spawner;
            public string data;
            public int phraseId;
            public int phraseOrdinal;
            public bool cueRecorded;
            public bool arrivalRecorded;
            public bool resolutionRecorded;
        }

        readonly Dictionary<Projectile, ObservedBolt> observedBolts = new Dictionary<Projectile, ObservedBolt>();
        CaptureFile capture = new CaptureFile();
        FirstPersonMotor motor;
        bool capturing;
        float startedRealtime;
        string lastExportPath = "";
        int nextUnfiredId;
        float nextSampleRealtime;

        public bool IsCapturing { get { return capturing; } }
        public int SampleCount { get { return capture.samples.Count; } }
        public int EventCount { get { return capture.events.Count; } }
        public string LastExportPath { get { return lastExportPath; } }
        public CaptureFile Data { get { return capture; } }

        /// <summary>True only in the editor or a development build; release players cannot activate capture.</summary>
        public static bool IsSupported { get { return Application.isEditor || Debug.isDebugBuild; } }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            UnsubscribeAll();
            if (instance == this) instance = null;
        }

        void Update()
        {
            if (!capturing) return;
            var reader = InputReader.I;
            if (reader != null && reader.ParryPressed) RecordPlayerEvent("parry_press");
            if (Time.realtimeSinceStartup >= nextSampleRealtime)
            {
                RecordMovement();
                nextSampleRealtime = Time.realtimeSinceStartup + 1f / SampleHz;
            }
            ObserveProjectiles();
        }

        public static bool StartCapture(out string message)
        {
            if (!IsSupported || !Application.isPlaying)
            {
                message = "TIMING CAPTURE IS AVAILABLE IN EDITOR PLAY MODE OR DEVELOPMENT BUILDS";
                return false;
            }

            PlayerTimingCapture recorder = instance;
            if (recorder == null)
            {
                var go = new GameObject("PlayerTimingCapture");
                recorder = go.AddComponent<PlayerTimingCapture>();
            }
            recorder.BeginCapture();
            message = "TIMING CAPTURE STARTED (IN MEMORY ONLY)";
            return true;
        }

        public static string StopCapture()
        {
            if (instance == null || !instance.capturing) return "TIMING CAPTURE IS NOT RUNNING";
            instance.StopCaptureInternal();
            return "TIMING CAPTURE STOPPED  " + instance.SampleCount + " SAMPLES  " + instance.EventCount + " EVENTS";
        }

        public static string Status()
        {
            if (instance == null) return "TIMING CAPTURE IDLE  0 SAMPLES  0 EVENTS";
            return "TIMING CAPTURE " + (instance.capturing ? "RECORDING" : "STOPPED") + "  " +
                   instance.SampleCount + " SAMPLES  " + instance.EventCount + " EVENTS" +
                   (instance.capture.droppedSamples > 0 || instance.capture.droppedEvents > 0
                       ? "  (CAP REACHED)" : "");
        }

        public static string Export()
        {
            if (instance == null || instance.capture.samples.Count == 0)
                return "TIMING CAPTURE HAS NO SAMPLES TO EXPORT";
            string path;
            string error;
            if (!instance.TryExport(out path, out error)) return "TIMING EXPORT FAILED: " + error;
            return "TIMING EXPORTED: " + path;
        }

        public static string Discard()
        {
            if (instance == null) return "TIMING CAPTURE ALREADY EMPTY";
            instance.StopCaptureInternal();
            instance.capture = new CaptureFile();
            instance.lastExportPath = "";
            return "TIMING CAPTURE DISCARDED";
        }

        /// <summary>Begins a fresh in-memory capture. Public so the buffer can be covered in EditMode.</summary>
        public void BeginCapture()
        {
            UnsubscribeAll();
            capture = new CaptureFile
            {
                sceneName = SceneManager.GetActiveScene().name,
                startedUtc = DateTime.UtcNow.ToString("o")
            };
            lastExportPath = "";
            nextUnfiredId = 0;
            startedRealtime = Time.realtimeSinceStartup;
            nextSampleRealtime = startedRealtime;
            capturing = true;
        }

        /// <summary>Stops recording but retains the in-memory data until export or discard.</summary>
        public void StopCaptureInternal()
        {
            if (!capturing) return;
            capturing = false;
            capture.stoppedUtc = DateTime.UtcNow.ToString("o");
            capture.durationSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - startedRealtime);
            UnsubscribeAll();
        }

        void RecordMovement()
        {
            if (motor == null) motor = FindAnyObjectByType<FirstPersonMotor>();
            if (motor == null) return;
            Vector3 velocity = motor.Velocity;
            var reader = InputReader.I;
            var sample = new MovementSample
            {
                captureSeconds = CaptureSeconds,
                worldTime = Time.time,
                position = motor.transform.position,
                velocity = velocity,
                speed = new Vector2(velocity.x, velocity.z).magnitude,
                grounded = motor.IsGrounded,
                sliding = motor.IsSliding,
                wallRunning = motor.IsWallRunning,
                dashing = motor.IsDashing,
                pulling = motor.IsPulling,
                parryPressed = reader != null && reader.ParryPressed
            };
            if (!AppendCapped(capture.samples, sample, MaxSamples)) capture.droppedSamples++;
        }

        void ObserveProjectiles()
        {
            foreach (Projectile projectile in FindObjectsByType<Projectile>())
            {
                if (projectile == null) continue;
                ObservedBolt bolt;
                if (!observedBolts.TryGetValue(projectile, out bolt))
                {
                    EnemySpawner spawner = FindSpawnerFor(projectile.Shooter);
                    bolt = new ObservedBolt
                    {
                        projectile = projectile,
                        id = projectile.BoltId > 0 ? projectile.BoltId.ToString() : "unfired-" + (++nextUnfiredId),
                        shooter = projectile.Shooter != null ? projectile.Shooter.name : "",
                        spawner = spawner != null ? spawner.name : "",
                        data = projectile.Data != null ? projectile.Data.name : "",
                        phraseId = projectile.PhraseId,
                        phraseOrdinal = projectile.PhraseOrdinal
                    };
                    observedBolts.Add(projectile, bolt);
                    projectile.IncomingResolved += OnIncomingResolved;
                    RecordBoltEvent("emission", bolt, projectile.FiredAt, "", projectile.PredictedContactAt);
                }

                if (!bolt.cueRecorded && projectile.CueAt >= 0f)
                {
                    bolt.cueRecorded = true;
                    RecordBoltEvent("cue", bolt, projectile.CueAt, "", projectile.PredictedContactAt);
                }
                if (!bolt.arrivalRecorded && projectile.ArrivedAt >= 0f)
                {
                    bolt.arrivalRecorded = true;
                    RecordBoltEvent("arrival", bolt, projectile.ArrivedAt, "", projectile.PredictedContactAt);
                }
            }
        }

        void OnIncomingResolved(Projectile projectile, ParryResult result)
        {
            if (!capturing || projectile == null) return;
            ObservedBolt bolt;
            if (!observedBolts.TryGetValue(projectile, out bolt) || bolt.resolutionRecorded) return;
            if (!bolt.cueRecorded && projectile.CueAt >= 0f)
            {
                bolt.cueRecorded = true;
                RecordBoltEvent("cue", bolt, projectile.CueAt, "", projectile.PredictedContactAt);
            }
            if (!bolt.arrivalRecorded && projectile.ArrivedAt >= 0f)
            {
                bolt.arrivalRecorded = true;
                RecordBoltEvent("arrival", bolt, projectile.ArrivedAt, "", projectile.PredictedContactAt);
            }
            bolt.resolutionRecorded = true;
            RecordBoltEvent("resolution", bolt, Time.time, result.ToString(), projectile.PredictedContactAt);
            projectile.IncomingResolved -= OnIncomingResolved;
            observedBolts.Remove(projectile);
        }

        void RecordBoltEvent(string kind, ObservedBolt bolt, float worldTime, string result = "",
                             float predictedContactAt = -1f)
        {
            RefreshMotor();
            Vector3 velocity = motor != null ? motor.Velocity : Vector3.zero;
            var item = new TimingEvent
            {
                kind = kind,
                captureSeconds = CaptureSeconds,
                worldTime = worldTime >= 0f ? worldTime : Time.time,
                boltId = bolt.id,
                shooter = bolt.shooter,
                spawner = bolt.spawner,
                data = bolt.data,
                phraseId = bolt.phraseId,
                phraseOrdinal = bolt.phraseOrdinal,
                predictedContactAt = predictedContactAt,
                playerPosition = motor != null ? motor.transform.position : Vector3.zero,
                playerSpeed = new Vector2(velocity.x, velocity.z).magnitude,
                playerSliding = motor != null && motor.IsSliding,
                result = result
            };
            if (!AppendCapped(capture.events, item, MaxEvents)) capture.droppedEvents++;
        }

        void RecordPlayerEvent(string kind)
        {
            RefreshMotor();
            Vector3 velocity = motor != null ? motor.Velocity : Vector3.zero;
            var item = new TimingEvent
            {
                kind = kind,
                captureSeconds = CaptureSeconds,
                worldTime = Time.time,
                predictedContactAt = -1f,
                playerPosition = motor != null ? motor.transform.position : Vector3.zero,
                playerSpeed = new Vector2(velocity.x, velocity.z).magnitude,
                playerSliding = motor != null && motor.IsSliding,
            };
            if (!AppendCapped(capture.events, item, MaxEvents)) capture.droppedEvents++;
        }

        void RefreshMotor()
        {
            if (motor == null) motor = FindAnyObjectByType<FirstPersonMotor>();
        }

        static EnemySpawner FindSpawnerFor(EnemyController shooter)
        {
            if (shooter == null) return null;
            foreach (var spawner in FindObjectsByType<EnemySpawner>())
            {
                if (spawner == null || spawner.Instance == null) continue;
                Transform root = spawner.Instance.transform;
                if (shooter.transform == root || shooter.transform.IsChildOf(root)) return spawner;
            }
            return null;
        }

        void UnsubscribeAll()
        {
            foreach (ObservedBolt bolt in observedBolts.Values)
                if (bolt.projectile != null) bolt.projectile.IncomingResolved -= OnIncomingResolved;
            observedBolts.Clear();
        }

        bool TryExport(out string path, out string error)
        {
            path = "";
            error = "";
            try
            {
                if (capturing) StopCaptureInternal();
                if (string.IsNullOrEmpty(capture.stoppedUtc)) capture.stoppedUtc = DateTime.UtcNow.ToString("o");
                if (capture.durationSeconds <= 0f)
                    capture.durationSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - startedRealtime);

                string directory = Path.Combine(Application.persistentDataPath, FolderName);
                Directory.CreateDirectory(directory);
                string filename = "timing-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json";
                path = Path.GetFullPath(Path.Combine(directory, filename));
                File.WriteAllText(path, JsonUtility.ToJson(capture, true));
                lastExportPath = path;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        float CaptureSeconds { get { return Mathf.Max(0f, Time.realtimeSinceStartup - startedRealtime); } }

        /// <summary>Bound a diagnostic buffer without dropping existing evidence.</summary>
        public static bool AppendCapped<T>(List<T> list, T value, int capacity)
        {
            if (list == null || capacity <= 0 || list.Count >= capacity) return false;
            list.Add(value);
            return true;
        }
    }
}
