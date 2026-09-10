using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Records the player's run for ghost playback.
    ///
    /// This records TRANSFORMS, not input. That distinction matters and is deliberate:
    ///
    ///   - A visual ghost only needs to know where the player was. Transform playback cannot desync,
    ///     because there is nothing to simulate — it is cosmetic geometry moving along a path.
    ///   - The design doc's §7.5 anti-cheat proposal (submit the input stream so a server can
    ///     re-simulate) is a SEPARATE, server-side concern. Unity's CharacterController and NavMesh are
    ///     not deterministic across machines (§10 non-goals), so input replay could never be trusted to
    ///     reproduce a run exactly — it is a plausibility check, not a renderer.
    ///
    /// When the backend arrives, input recording is added ALONGSIDE this, not instead of it.
    ///
    /// Sampling is on a fixed tick driven by <see cref="SpeedrunTimer.Elapsed"/>, not by frame count, so
    /// a recording is framerate-independent and a sample's index is exactly its time.
    /// </summary>
    public class RunRecorder : MonoBehaviour
    {
        [Tooltip("Samples per second. 30 Hz matches the design doc's server tick; the ghost is interpolated so lower is still smooth.")]
        [SerializeField] int tickHz = 30;

        [Tooltip("Safety cap: if a hitch stalls the game, never emit more than this many catch-up samples in one frame.")]
        [SerializeField] int maxCatchUpSamplesPerFrame = 8;

        public bool IsRecording { get; private set; }
        public int SampleCount => samples.Count;
        public float RecordedSeconds => tickHz > 0 ? samples.Count / (float)tickHz : 0f;

        readonly List<GhostSample> samples = new List<GhostSample>();
        float nextSampleTime;

        Transform player;
        FirstPersonMotor motor;
        PlayerCombat combat;
        ParryController parry;
        WeaponController weapons;
        PlayerLook look;
        Health health;

        void OnEnable()
        {
            GameEvents.BossDefeated += OnLegacyRunFinished;
            GameEvents.LevelRunEvaluated += OnRunEvaluated;
            if (SpeedrunTimer.I != null)
            {
                SpeedrunTimer.I.RunStarted += OnRunStarted;
                SpeedrunTimer.I.RunFinished += OnLegacyRunFinished;
            }
        }

        void OnDisable()
        {
            GameEvents.BossDefeated -= OnLegacyRunFinished;
            GameEvents.LevelRunEvaluated -= OnRunEvaluated;
            if (SpeedrunTimer.I != null)
            {
                SpeedrunTimer.I.RunStarted -= OnRunStarted;
                SpeedrunTimer.I.RunFinished -= OnLegacyRunFinished;
            }
        }

        void Bind()
        {
            if (combat != null) return;
            combat = FindAnyObjectByType<PlayerCombat>();
            if (combat == null) return;
            player = combat.transform;
            motor = combat.GetComponent<FirstPersonMotor>();
            parry = combat.GetComponent<ParryController>();
            weapons = combat.GetComponent<WeaponController>();
            look = combat.GetComponent<PlayerLook>();
            health = combat.GetComponent<Health>();
        }

        void OnRunStarted()
        {
            samples.Clear();
            nextSampleTime = 0f;
            IsRecording = true;
        }

        /// <summary>
        /// Old levels have no score adjudicator, so the timer/boss remains their completion signal. A
        /// scored level deliberately waits for LevelRunEvaluated: RunFinished happens while the scorer
        /// is still constructing its frozen result and must never save a failed quota run.
        /// </summary>
        void OnLegacyRunFinished()
        {
            if (LevelRunScorer.I != null) return;
            FinishRecording();
        }

        void OnRunEvaluated(LevelRunResult result)
        {
            if (!result.completed)
            {
                Discard();
                return;
            }
            FinishRecording();
        }

        void FinishRecording()
        {
            if (!IsRecording) return;
            IsRecording = false;
            var rec = BuildRecording();
            if (rec != null) GhostRacing.NotifyRunRecorded(rec);
        }

        void Update()
        {
            var timer = SpeedrunTimer.I;
            if (timer == null) return;

            // The timer may have started before this component subscribed (bootstrapped mid-scene).
            if (!IsRecording && timer.Running && !timer.Finished && samples.Count == 0) OnRunStarted();
            if (!IsRecording) return;

            Bind();
            if (combat == null) return;

            float interval = 1f / Mathf.Max(1, tickHz);
            int emitted = 0;
            while (timer.Elapsed >= nextSampleTime && emitted < maxCatchUpSamplesPerFrame)
            {
                samples.Add(Capture());
                nextSampleTime += interval;
                emitted++;
            }
            // If a hitch pushed us further behind than the catch-up cap allows, skip forward rather than
            // spiralling. Ghost time stays aligned to run time; we simply lose fidelity across the hitch.
            if (timer.Elapsed > nextSampleTime + interval * maxCatchUpSamplesPerFrame)
                nextSampleTime = timer.Elapsed;
        }

        GhostSample Capture()
        {
            var flags = GhostFlags.None;
            if (motor != null)
            {
                if (motor.IsGrounded) flags |= GhostFlags.Grounded;
                if (motor.IsDashing) flags |= GhostFlags.Dashing;
            }
            if (combat != null)
            {
                if (combat.IsAttacking) flags |= GhostFlags.Attacking;
                if (combat.IsStaggered) flags |= GhostFlags.Staggered;
                if (combat.IsExecuting) flags |= GhostFlags.Executing;
                if (combat.IsDrinking) flags |= GhostFlags.Drinking;
            }
            if (parry != null && parry.IsActive) flags |= GhostFlags.Parrying;
            if (health != null && health.IsDead) flags |= GhostFlags.Dead;

            float yaw = player != null ? player.eulerAngles.y : 0f;
            // Pitch lives on the look pivot; convert Unity's 0..360 wrap back to a signed angle.
            float pitch = 0f;
            if (look != null && look.pivot != null)
            {
                pitch = look.pivot.localEulerAngles.x;
                if (pitch > 180f) pitch -= 360f;
            }

            return GhostSample.Create(
                player != null ? player.position : Vector3.zero,
                yaw, pitch, flags,
                weapons != null ? weapons.Index : 0);
        }

        /// <summary>Package what has been recorded so far. Null when there is nothing worth keeping.</summary>
        public GhostRecording BuildRecording()
        {
            if (samples.Count < 2) return null;
            var timer = SpeedrunTimer.I;
            var rec = new GhostRecording
            {
                level = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                timeMs = Mathf.RoundToInt((timer != null ? timer.Elapsed : RecordedSeconds) * 1000f),
                deaths = LevelManager.I != null ? LevelManager.I.DeathCount : 0,
                tickHz = tickHz,
                samples = samples.ToArray(),
            };
            return rec;
        }

        /// <summary>Abandon the in-progress recording (used when a run is restarted).</summary>
        public void Discard()
        {
            IsRecording = false;
            samples.Clear();
            nextSampleTime = 0f;
        }

        [ContextMenu("Ghost/Force Save Current Recording")]
        public void ForceSave()
        {
            var rec = BuildRecording();
            if (rec == null) { Debug.LogWarning("[RunRecorder] Nothing recorded yet."); return; }
            GhostRacing.NotifyRunRecorded(rec);
        }
    }
}
