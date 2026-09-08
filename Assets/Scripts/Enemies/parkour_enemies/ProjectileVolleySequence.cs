using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Orders existing projectile enemies into a single readable volley. Each member uses its unchanged
    /// <see cref="ProjectileShooter"/> rules and projectile; this component only grants one member at a
    /// time permission to launch. A perfect deflect advances immediately, while a block, hit or expired
    /// bolt advances when that specific projectile is destroyed, so missing one shot never silences the
    /// rest of the sequence.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class ProjectileVolleySequence : MonoBehaviour
    {
        [SerializeField] EnemySpawner[] members = new EnemySpawner[0];
        [SerializeField, Min(0f)] float recoveryGap = 0.11f;
        [SerializeField, Min(0.1f)] float readinessTimeout = 1.1f;
        [SerializeField, Min(0f)] float shotResolutionTimeout;
        [SerializeField] Vector3 progressOrigin;
        [SerializeField] Vector3 progressDirection;
        [SerializeField] float[] memberProgressGates = new float[0];

        GameObject[] boundInstances = new GameObject[0];
        ProjectileShooter[] shooters = new ProjectileShooter[0];
        SurgeTurret[] turrets = new SurgeTurret[0];
        Projectile activeBolt;
        int current;
        int grantsBeforeShot;
        float nextLaunchAt;
        float enteredBandAt;
        float shotLaunchedAt;
        bool bandSeen;
        bool firstMemberArmed;
        bool shotLaunched;
        bool waitingForRespawn;
        Transform player;

        /// <summary>Ordered member currently waiting or firing. Equals Count when the volley is complete.</summary>
        public int CurrentIndex { get { return current; } }
        public int Count { get { return members != null ? members.Length : 0; } }
        public float RecoveryGap { get { return recoveryGap; } }
        public float ReadinessTimeout { get { return readinessTimeout; } }
        public float ShotResolutionTimeout { get { return shotResolutionTimeout; } }
        public Vector3 ProgressOrigin { get { return progressOrigin; } }
        public Vector3 ProgressDirection { get { return progressDirection; } }
        public float[] MemberProgressGates
        {
            get { return memberProgressGates != null ? (float[])memberProgressGates.Clone() : new float[0]; }
        }
        public string[] SpawnerNames
        {
            get
            {
                if (members == null) return new string[0];
                var names = new string[members.Length];
                for (int i = 0; i < members.Length; i++) names[i] = members[i] != null ? members[i].name : "";
                return names;
            }
        }

        public void Configure(EnemySpawner[] orderedMembers, float gap, float readyTimeout)
        {
            Configure(orderedMembers, gap, readyTimeout, 0f, Vector3.zero, Vector3.zero, null);
        }

        public void Configure(EnemySpawner[] orderedMembers, float gap, float readyTimeout,
                              Vector3 gateOrigin, Vector3 gateDirection, float[] progressGates)
        {
            Configure(orderedMembers, gap, readyTimeout, 0f, gateOrigin, gateDirection, progressGates);
        }

        public void Configure(EnemySpawner[] orderedMembers, float gap, float readyTimeout,
                              float resolutionTimeout, Vector3 gateOrigin, Vector3 gateDirection,
                              float[] progressGates)
        {
            RetireActiveIncoming();
            members = orderedMembers ?? new EnemySpawner[0];
            recoveryGap = Mathf.Max(0f, gap);
            readinessTimeout = Mathf.Max(0.1f, readyTimeout);
            shotResolutionTimeout = Mathf.Max(0f, resolutionTimeout);
            progressOrigin = gateOrigin;
            progressDirection = gateDirection.sqrMagnitude > 0.0001f ? gateDirection.normalized : Vector3.zero;
            memberProgressGates = progressGates != null ? (float[])progressGates.Clone() : new float[0];
            AllocateCaches();
            ResetState();
        }

        void Awake() { AllocateCaches(); }

        void OnEnable()
        {
            GameEvents.PlayerDied += OnPlayerDied;
            GameEvents.PlayerRespawned += Restart;
            Restart();
        }

        void Start() { Restart(); }

        void OnDisable()
        {
            GameEvents.PlayerDied -= OnPlayerDied;
            GameEvents.PlayerRespawned -= Restart;
            RetireActiveIncoming();
            ReleaseMembers();
            ClearBindings();
        }

        void AllocateCaches()
        {
            int count = members != null ? members.Length : 0;
            if (boundInstances.Length == count) return;
            boundInstances = new GameObject[count];
            shooters = new ProjectileShooter[count];
            turrets = new SurgeTurret[count];
        }

        void BindMembers()
        {
            AllocateCaches();
            bool firstMemberReplaced = false;
            for (int i = 0; i < members.Length; i++)
            {
                GameObject instance = members[i] != null ? members[i].Instance : null;
                if (boundInstances[i] == instance) continue;
                if (i == 0 && !ReferenceEquals(boundInstances[i], null) && instance != null &&
                    !ReferenceEquals(boundInstances[i], instance)) firstMemberReplaced = true;
                if (shooters[i] != null) shooters[i].SetSequenceControlled(false);
                boundInstances[i] = instance;
                shooters[i] = instance != null ? instance.GetComponent<ProjectileShooter>() : null;
                turrets[i] = instance != null ? instance.GetComponent<SurgeTurret>() : null;
                if (shooters[i] != null) shooters[i].SetSequenceControlled(true);
            }
            // LevelManager.ResetEnemies replaces every Instance before raising no dedicated reset event.
            // The first authored member is the stable latch that distinguishes that reset from an ordinary
            // turret death; restart only after its replacement is already bound.
            if (firstMemberReplaced) ResetState();
        }

        void Update()
        {
            BindMembers();
            if (waitingForRespawn || current >= Count) return;

            if (shotLaunched)
            {
                bool granted = turrets[current] != null && turrets[current].SurgesGranted > grantsBeforeShot;
                if (granted || activeBolt == null || !activeBolt.IsIncoming)
                {
                    // A reflected bolt is already resolved for sequencing and must finish its return trip.
                    Advance();
                    return;
                }
                if (shotResolutionTimeout > 0f && Time.time - shotLaunchedAt >= shotResolutionTimeout)
                {
                    RetireActiveIncoming();
                    Advance();
                }
                return;
            }

            // A distant member must not consume its readiness timeout before the runner reaches its
            // authored beat. Once crossed, bandSeen remains latched even if the player doubles back.
            if (HasProgressGate(current) && !bandSeen)
            {
                if (!ProgressGateReached(current)) return;
                bandSeen = true;
                enteredBandAt = Mathf.Max(Time.time, nextLaunchAt);
            }

            var shooter = shooters[current];
            if (shooter == null)
            {
                if (!bandSeen) { bandSeen = true; enteredBandAt = Time.time; }
                if (Time.time - enteredBandAt >= readinessTimeout) Advance();
                return;
            }

            if (Time.time < nextLaunchAt) return;
            bool enteredBand;
            bool shotReady;
            bool allowFire = current > 0 || (firstMemberArmed && Time.time >= nextLaunchAt);
            activeBolt = shooter.TryFireSequenceShot(allowFire, out enteredBand, out shotReady);
            if (current == 0 && shotReady && !firstMemberArmed)
            {
                firstMemberArmed = true;
                nextLaunchAt = Time.time + shooter.SequenceAcquireDelay;
                if (!bandSeen) { bandSeen = true; enteredBandAt = Time.time; }
                return;
            }
            if (activeBolt != null)
            {
                grantsBeforeShot = turrets[current] != null ? turrets[current].SurgesGranted : 0;
                shotLaunched = true;
                shotLaunchedAt = Time.time;
                return;
            }

            if (enteredBand && !bandSeen)
            {
                bandSeen = true;
                enteredBandAt = Time.time;
            }
            if (bandSeen && Time.time - enteredBandAt >= readinessTimeout) Advance();
        }

        void Advance()
        {
            current++;
            activeBolt = null;
            shotLaunched = false;
            firstMemberArmed = current > 0;
            nextLaunchAt = Time.time + recoveryGap;
            // An ungated later member starts its finite readiness window immediately. A gated member waits
            // passively until the player crosses its authored position, then receives that same deadline.
            bandSeen = current > 0 && current < Count && !HasProgressGate(current);
            enteredBandAt = nextLaunchAt;
        }

        bool HasProgressGate(int index)
        {
            return progressDirection.sqrMagnitude > 0.0001f && memberProgressGates != null &&
                   index >= 0 && index < memberProgressGates.Length;
        }

        bool ProgressGateReached(int index)
        {
            if (!HasProgressGate(index)) return true;
            if (player == null)
            {
                var combat = FindAnyObjectByType<PlayerCombat>();
                player = combat != null ? combat.transform : null;
            }
            return player != null &&
                   Vector3.Dot(player.position - progressOrigin, progressDirection) >= memberProgressGates[index];
        }

        void OnPlayerDied()
        {
            waitingForRespawn = true;
            RetireActiveIncoming();
            shotLaunched = false;
        }

        /// <summary>Restarts the authored order after spawn or respawn. Used by the live descent probe.</summary>
        public void Restart()
        {
            waitingForRespawn = false;
            BindMembers();
            ResetState();
        }

        void ResetState()
        {
            RetireActiveIncoming();
            current = 0;
            shotLaunched = false;
            shotLaunchedAt = 0f;
            bandSeen = false;
            firstMemberArmed = false;
            nextLaunchAt = Time.time;
            enteredBandAt = nextLaunchAt;
        }

        void ReleaseMembers()
        {
            for (int i = 0; i < shooters.Length; i++)
                if (shooters[i] != null) shooters[i].SetSequenceControlled(false);
        }

        void RetireActiveIncoming()
        {
            if (activeBolt != null && activeBolt.IsIncoming)
            {
                if (Application.isPlaying) Destroy(activeBolt.gameObject);
                else DestroyImmediate(activeBolt.gameObject);
            }
            activeBolt = null;
        }

        void ClearBindings()
        {
            boundInstances = new GameObject[0];
            shooters = new ProjectileShooter[0];
            turrets = new SurgeTurret[0];
            player = null;
        }
    }
}
