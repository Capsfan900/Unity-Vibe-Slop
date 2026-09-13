using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VibeGame1
{
    public enum CaptureState { Idle, Primed, Recording }

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
        ParryCaptureFile capture = new ParryCaptureFile();
        FirstPersonMotor motor;
        CaptureState state;
        float startedRealtime;
        string lastExportPath = "";
        int nextUnfiredId;
        float nextSampleRealtime;

        public bool IsCapturing { get { return state == CaptureState.Recording; } }
        public int SampleCount { get { return capture.samples.Count; } }
        public int EventCount { get { return capture.events.Count; } }
        public static CaptureState State { get { return instance != null ? instance.state : CaptureState.Idle; } }
        public static string LastExportPath { get { return instance != null ? instance.lastExportPath : ""; } }
        public ParryCaptureFile Data { get { return capture; } }

        /// <summary>The recorder is available in every build only after the shared session capability
        /// is granted. This lets a trusted playtester capture a release-only timing problem without
        /// exposing local file writes to ordinary players.</summary>
        public static bool IsSupported { get { return DeveloperAccess.IsUnlocked; } }

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
            var reader = InputReader.I;
            if (state != CaptureState.Idle && reader != null && reader.TimingCaptureTogglePressed)
            {
                ToggleCapture();
                return;
            }
            if (state != CaptureState.Recording) return;
            if (reader != null && reader.ParryPressed) RecordDesiredBeat();
            if (Time.realtimeSinceStartup >= nextSampleRealtime)
            {
                RecordMovement();
                nextSampleRealtime = Time.realtimeSinceStartup + 1f / SampleHz;
            }
            ObserveProjectiles();
        }

        public static bool StartCapture(out string message)
        {
            message = Prime();
            return State == CaptureState.Primed;
        }

        public static string Prime()
        {
            if (!DeveloperAccess.IsUnlocked) return "TIMING CAPTURE REQUIRES DEVELOPER ACCESS";
            if (!Application.isPlaying) return "TIMING CAPTURE REQUIRES PLAY MODE";
            if (instance == null) new GameObject("PlayerTimingCapture").AddComponent<PlayerTimingCapture>();
            if (instance.state == CaptureState.Recording) return "TIMING CAPTURE IS RECORDING";
            instance.state = CaptureState.Primed;
            return "TIMING CAPTURE PRIMED  PRESS 0 TO START";
        }

        public static string ToggleCapture()
        {
            if (!DeveloperAccess.IsUnlocked) return "TIMING CAPTURE REQUIRES DEVELOPER ACCESS";
            if (instance == null || instance.state == CaptureState.Idle) return "TIMING CAPTURE IS NOT PRIMED";
            if (instance.state == CaptureState.Recording) return StopAndExport();
            instance.BeginCapture();
            return "TIMING CAPTURE RECORDING  PRESS 0 TO STOP";
        }

        public static string StopCapture()
        {
            return StopAndExport();
        }

        public static string StopAndExport()
        {
            if (!DeveloperAccess.IsUnlocked) return "TIMING CAPTURE REQUIRES DEVELOPER ACCESS";
            if (instance == null || instance.state != CaptureState.Recording) return "TIMING CAPTURE IS NOT RUNNING";
            instance.StopCaptureInternal();
            string path, error;
            if (!instance.TryExport(out path, out error)) return "TIMING EXPORT FAILED: " + error;
            GameEvents.RaisePromptFlash("PARRY EXPORTED  " + Path.GetFileName(path), 3f);
            return "TIMING EXPORTED: " + path;
        }

        public static string Status()
        {
            if (!DeveloperAccess.IsUnlocked) return "TIMING CAPTURE REQUIRES DEVELOPER ACCESS";
            if (instance == null) return "TIMING CAPTURE IDLE  0 SAMPLES  0 EVENTS";
            return "TIMING CAPTURE " + instance.state.ToString().ToUpperInvariant() + "  " +
                   instance.SampleCount + " SAMPLES  " + instance.EventCount + " EVENTS" +
                   (instance.capture.droppedSamples > 0 || instance.capture.droppedEvents > 0
                       ? "  (CAP REACHED)" : "");
        }

        public static string Export()
        {
            if (!DeveloperAccess.IsUnlocked) return "TIMING CAPTURE REQUIRES DEVELOPER ACCESS";
            if (instance == null || string.IsNullOrEmpty(instance.lastExportPath)) return "TIMING CAPTURE HAS NOT EXPORTED A TAKE";
            return "TIMING EXPORTED: " + instance.lastExportPath;
        }

        public static string Discard()
        {
            if (!DeveloperAccess.IsUnlocked) return "TIMING CAPTURE REQUIRES DEVELOPER ACCESS";
            if (instance == null) return "TIMING CAPTURE ALREADY EMPTY";
            instance.StopCaptureInternal();
            instance.capture = new ParryCaptureFile();
            instance.lastExportPath = "";
            instance.state = CaptureState.Idle;
            return "TIMING CAPTURE DISCARDED";
        }

        /// <summary>Begins a fresh in-memory capture after the public, gated entry point accepts it.</summary>
        void BeginCapture()
        {
            UnsubscribeAll();
            capture = new ParryCaptureFile
            {
                sceneName = SceneManager.GetActiveScene().name,
                startedUtc = DateTime.UtcNow.ToString("o")
            };
            lastExportPath = "";
            nextUnfiredId = 0;
            startedRealtime = Time.realtimeSinceStartup;
            nextSampleRealtime = startedRealtime;
            state = CaptureState.Recording;
            GameEvents.RaisePromptChanged(PromptOwner.TimingCapture, "PARRY RECORDING");
        }

        /// <summary>Stops recording but retains the in-memory data until export or discard.</summary>
        void StopCaptureInternal()
        {
            if (state != CaptureState.Recording) return;
            state = CaptureState.Primed;
            capture.stoppedUtc = DateTime.UtcNow.ToString("o");
            capture.durationSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - startedRealtime);
            UnsubscribeAll();
            GameEvents.RaisePromptChanged(PromptOwner.TimingCapture, "");
        }

        void RecordMovement()
        {
            if (motor == null) motor = FindAnyObjectByType<FirstPersonMotor>();
            if (motor == null) return;
            Vector3 velocity = motor.Velocity;
            var reader = InputReader.I;
            var sample = new TraversalSample
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
            if (state != CaptureState.Recording || projectile == null) return;
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
            var item = new ParryTimingEvent
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

        void RecordDesiredBeat()
        {
            RefreshMotor();
            Vector3 velocity = motor != null ? motor.Velocity : Vector3.zero;
            var item = new ParryTimingEvent
            {
                kind = "parry_press",
                captureSeconds = CaptureSeconds,
                worldTime = Time.time,
                predictedContactAt = -1f,
                playerPosition = motor != null ? motor.transform.position : Vector3.zero,
                playerSpeed = new Vector2(velocity.x, velocity.z).magnitude,
                playerSliding = motor != null && motor.IsSliding,
            };
            if (!AppendCapped(capture.events, item, MaxEvents)) capture.droppedEvents++;
            if (capture.desiredBeats.Count >= MaxEvents) { capture.droppedEvents++; return; }
            capture.desiredBeats.Add(new DesiredParryBeat
            {
                ordinal = capture.desiredBeats.Count + 1,
                captureSeconds = CaptureSeconds,
                position = item.playerPosition,
                velocity = velocity,
                lookDirection = Camera.main != null ? Camera.main.transform.forward : motor != null ? motor.transform.forward : Vector3.forward,
                playerSpeed = item.playerSpeed,
                surfaceType = SurfaceType(),
                zoneId = CurrentZoneId(),
                splitName = CurrentSplitName(),
                grounded = motor != null && motor.IsGrounded,
                sliding = motor != null && motor.IsSliding,
                wallRunning = motor != null && motor.IsWallRunning,
                dashing = motor != null && motor.IsDashing,
                pulling = motor != null && motor.IsPulling
            });
        }

        string SurfaceType()
        {
            if (motor == null) return "Unknown";
            RaycastHit hit;
            if (!Physics.Raycast(motor.transform.position + Vector3.up * .2f, Vector3.down, out hit, 2f)) return "Unknown";
            var piece = hit.transform.GetComponentInParent<LevelPiece>();
            return piece != null ? piece.kind.ToString() : "Unknown";
        }

        string CurrentZoneId()
        {
            var scorer = LevelRunScorer.I;
            if (motor == null || scorer == null || scorer.definition == null || scorer.definition.zones == null) return "";
            foreach (var zone in scorer.definition.zones)
                if (zone != null && new Bounds(zone.center, zone.size).Contains(motor.transform.position)) return zone.zoneId;
            return "";
        }

        string CurrentSplitName()
        {
            var scorer = LevelRunScorer.I;
            if (scorer == null || scorer.definition == null || scorer.definition.runSplits == null || scorer.CurrentSplitIndex >= scorer.definition.runSplits.Length) return "";
            var split = scorer.definition.runSplits[scorer.CurrentSplitIndex];
            return split != null ? split.name : "";
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
                if (state == CaptureState.Recording) StopCaptureInternal();
                if (string.IsNullOrEmpty(capture.stoppedUtc)) capture.stoppedUtc = DateTime.UtcNow.ToString("o");
                if (capture.durationSeconds <= 0f)
                    capture.durationSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - startedRealtime);

                if (!TryWriteAtomic(capture, Path.Combine(Application.persistentDataPath, FolderName), out path, out error)) return false;
                lastExportPath = path;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static bool TryWriteAtomic(ParryCaptureFile data, string directory, out string path, out string error)
        {
            path = ""; error = ""; string temporary = "";
            try
            {
                Directory.CreateDirectory(directory);
                path = Path.GetFullPath(Path.Combine(directory, "timing-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json"));
                temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                File.Move(temporary, path);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (!string.IsNullOrEmpty(temporary) && File.Exists(temporary)) try { File.Delete(temporary); } catch { }
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
