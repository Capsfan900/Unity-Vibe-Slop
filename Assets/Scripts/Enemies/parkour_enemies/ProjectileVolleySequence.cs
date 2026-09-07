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

        GameObject[] boundInstances = new GameObject[0];
        ProjectileShooter[] shooters = new ProjectileShooter[0];
        SurgeTurret[] turrets = new SurgeTurret[0];
        Projectile activeBolt;
        int current;
        int grantsBeforeShot;
        float nextLaunchAt;
        float enteredBandAt;
        bool bandSeen;
        bool firstMemberArmed;
        bool shotLaunched;
        bool waitingForRespawn;

        /// <summary>Ordered member currently waiting or firing. Equals Count when the volley is complete.</summary>
        public int CurrentIndex { get { return current; } }
        public int Count { get { return members != null ? members.Length : 0; } }
        public float RecoveryGap { get { return recoveryGap; } }
        public float ReadinessTimeout { get { return readinessTimeout; } }
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
            members = orderedMembers ?? new EnemySpawner[0];
            recoveryGap = Mathf.Max(0f, gap);
            readinessTimeout = Mathf.Max(0.1f, readyTimeout);
            AllocateCaches();
        }

        void Awake() { AllocateCaches(); }

        void OnEnable()
        {
            GameEvents.PlayerDied += OnPlayerDied;
            GameEvents.PlayerRespawned += Restart;
        }

        void Start() { Restart(); }

        void OnDisable()
        {
            GameEvents.PlayerDied -= OnPlayerDied;
            GameEvents.PlayerRespawned -= Restart;
            ReleaseMembers();
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
                if (granted || activeBolt == null) Advance();
                return;
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
            // The first member waits passively for the player to enter the encounter. After that point,
            // each selected member gets a finite readiness window even if the runner has already passed
            // its range or the enemy stays idle; one unavailable turret must not silence everything after it.
            bandSeen = current > 0 && current < Count;
            enteredBandAt = nextLaunchAt;
        }

        void OnPlayerDied()
        {
            waitingForRespawn = true;
            activeBolt = null;
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
            current = 0;
            activeBolt = null;
            shotLaunched = false;
            bandSeen = false;
            firstMemberArmed = false;
            nextLaunchAt = Time.time;
        }

        void ReleaseMembers()
        {
            for (int i = 0; i < shooters.Length; i++)
                if (shooters[i] != null) shooters[i].SetSequenceControlled(false);
        }
    }
}
