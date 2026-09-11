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
        [SerializeField] float firstMemberAcquireDelay = -1f;
        [SerializeField] Vector3 progressOrigin;
        [SerializeField] Vector3 progressDirection;
        [SerializeField] float[] memberProgressGates = new float[0];
        [SerializeField] ProjectileEngagementWindowDef[] engagementWindows = new ProjectileEngagementWindowDef[0];
        [SerializeField] int repeatFromIndex = -1;

        GameObject[] boundInstances = new GameObject[0];
        ProjectileShooter[] shooters = new ProjectileShooter[0];
        Projectile activeBolt;
        ProjectileShooter activePhraseShooter;
        int current;
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
        public float FirstMemberAcquireDelay { get { return firstMemberAcquireDelay; } }
        public int RepeatFromIndex { get { return repeatFromIndex; } }
        public Vector3 ProgressOrigin { get { return progressOrigin; } }
        public Vector3 ProgressDirection { get { return progressDirection; } }
        public float[] MemberProgressGates
        {
            get { return memberProgressGates != null ? (float[])memberProgressGates.Clone() : new float[0]; }
        }
        public ProjectileEngagementWindowDef[] EngagementWindows
        {
            get
            {
                if (engagementWindows == null) return new ProjectileEngagementWindowDef[0];
                var copy = new ProjectileEngagementWindowDef[engagementWindows.Length];
                for (int i = 0; i < copy.Length; i++) copy[i] = CloneWindow(engagementWindows[i]);
                return copy;
            }
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
            Configure(orderedMembers, gap, readyTimeout, resolutionTimeout, gateOrigin, gateDirection,
                      progressGates, null);
        }

        public void Configure(EnemySpawner[] orderedMembers, float gap, float readyTimeout,
                              float resolutionTimeout, Vector3 gateOrigin, Vector3 gateDirection,
                              float[] progressGates, ProjectileEngagementWindowDef[] windows)
        {
            Configure(orderedMembers, gap, readyTimeout, resolutionTimeout, gateOrigin, gateDirection,
                      progressGates, windows, -1);
        }

        /// <summary>Configures an ordered volley; -1 retains the authored one-pass default.</summary>
        public void Configure(EnemySpawner[] orderedMembers, float gap, float readyTimeout,
                              float resolutionTimeout, Vector3 gateOrigin, Vector3 gateDirection,
                              float[] progressGates, ProjectileEngagementWindowDef[] windows,
                              int repeatIndex, float firstAcquireDelay = -1f)
        {
            // Release the OLD controlled instances before replacing members or resizing caches. Doing this
            // afterwards can index the new array while leaving an old shooter permanently sequence-owned.
            RetireActiveIncoming(ProjectilePhraseCancellation.SequenceReconfigured);
            ReleaseMembers();
            members = orderedMembers ?? new EnemySpawner[0];
            recoveryGap = Mathf.Max(0f, gap);
            readinessTimeout = Mathf.Max(0.1f, readyTimeout);
            shotResolutionTimeout = Mathf.Max(0f, resolutionTimeout);
            firstMemberAcquireDelay = firstAcquireDelay < 0f ? -1f : firstAcquireDelay;
            progressOrigin = gateOrigin;
            progressDirection = gateDirection.sqrMagnitude > 0.0001f ? gateDirection.normalized : Vector3.zero;
            memberProgressGates = progressGates != null ? (float[])progressGates.Clone() : new float[0];
            engagementWindows = CloneWindows(windows);
            repeatFromIndex = repeatIndex;
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
            RetireActiveIncoming(ProjectilePhraseCancellation.Disabled);
            ReleaseMembers();
            ClearBindings();
        }

        void AllocateCaches()
        {
            int count = members != null ? members.Length : 0;
            if (boundInstances.Length == count) return;
            boundInstances = new GameObject[count];
            shooters = new ProjectileShooter[count];
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
                // The shooter owns the WHOLE phrase. A first deflect resolves only its first incoming
                // obligation; the next sequence member cannot start until every planned emission is done
                // and every emitted bolt has resolved. Reflected bolts remain alive for their return trip.
                bool phraseResolved = activePhraseShooter != null
                    ? activePhraseShooter.PhraseEmissionsComplete && activePhraseShooter.PhraseIncomingResolved
                    : activeBolt == null || !activeBolt.IsIncoming;
                if (phraseResolved)
                {
                    Advance();
                    return;
                }
                if (shotResolutionTimeout > 0f && Time.time - shotLaunchedAt >= shotResolutionTimeout)
                {
                    RetireActiveIncoming(ProjectilePhraseCancellation.SequenceTimeout);
                    Advance();
                }
                return;
            }

            ProjectileEngagementWindowDef engagementWindow = null;
            bool hasEngagementWindow = HasEngagementWindows(current);
            if (hasEngagementWindow && !TrySelectEngagementWindow(current, out engagementWindow))
            {
                if (EveryEngagementWindowPassed(current)) Advance();
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
            else if (hasEngagementWindow && !bandSeen)
            {
                bandSeen = true;
                enteredBandAt = Mathf.Max(Time.time, nextLaunchAt);
            }

            var shooter = shooters[current];
            if (shooter == null)
            {
                // A destroyed or dormant member cannot satisfy readiness later. Skipping it keeps a
                // looping group alive and preserves the remaining live members' ordered cadence.
                Advance();
                return;
            }

            // The owner's quiet begins at its final incoming resolution, not its final emission.
            // Do not consume this member's readiness budget while waiting for that contract gate.
            if (shooter.IsPhraseResting)
            {
                nextLaunchAt = Mathf.Max(nextLaunchAt, shooter.NextPhraseAllowedAt);
                if (bandSeen) enteredBandAt = Time.time;
                return;
            }

            if (Time.time < nextLaunchAt) return;
            bool enteredBand;
            bool shotReady;
            bool allowFire = current > 0 || (firstMemberArmed && Time.time >= nextLaunchAt);
            activeBolt = shooter.TryFireSequenceShot(allowFire, engagementWindow, hasEngagementWindow,
                                                     out enteredBand, out shotReady);
            if (current == 0 && shotReady && !firstMemberArmed)
            {
                firstMemberArmed = true;
                float armUp = firstMemberAcquireDelay >= 0f
                    ? firstMemberAcquireDelay
                    : shooter.SequenceAcquireDelay;
                nextLaunchAt = Time.time + armUp;
                if (!bandSeen) { bandSeen = true; enteredBandAt = Time.time; }
                return;
            }
            if (activeBolt != null)
            {
                activePhraseShooter = shooter;
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
            if (current >= Count && repeatFromIndex >= 0 && repeatFromIndex < Count)
            {
                bool anyLive = false;
                for (int i = repeatFromIndex; i < Count; i++)
                    if (shooters != null && i < shooters.Length && shooters[i] != null)
                    {
                        anyLive = true;
                        break;
                    }
                current = anyLive ? repeatFromIndex : Count;
            }
            activeBolt = null;
            activePhraseShooter = null;
            shotLaunched = false;
            // A repeating sequence has already paid the first member's arm-up. The owning shooter still
            // enforces its resolution-based rest before it can emit again.
            firstMemberArmed = current > 0 || (repeatFromIndex == 0 && current == 0);
            // A shooter's rest belongs to THAT shooter. Carrying A's personal refire clock into B delayed
            // a fast sequence by an entire interval per member; Update already checks IsPhraseResting when
            // the loop eventually returns to A. Different members owe only the authored recovery gap.
            nextLaunchAt = Time.time + recoveryGap;
            // An ungated later member starts its finite readiness window immediately. A gated member waits
            // passively until the player crosses its authored position, then receives that same deadline.
            bandSeen = current > 0 && current < Count && !HasProgressGate(current) &&
                       !HasEngagementWindows(current);
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

        bool HasEngagementWindows(int index)
        {
            if (members == null || index < 0 || index >= members.Length || members[index] == null ||
                engagementWindows == null) return false;
            string spawnerName = members[index].name;
            for (int i = 0; i < engagementWindows.Length; i++)
                if (engagementWindows[i] != null && engagementWindows[i].spawnerName == spawnerName) return true;
            return false;
        }

        bool TrySelectEngagementWindow(int index, out ProjectileEngagementWindowDef selected)
        {
            selected = null;
            if (player == null)
            {
                var combat = FindAnyObjectByType<PlayerCombat>();
                player = combat != null ? combat.transform : null;
            }
            if (player == null || members == null || index < 0 || index >= members.Length || members[index] == null)
                return false;
            Vector3 chest = player.position + Vector3.up * 1.2f;
            string spawnerName = members[index].name;
            for (int i = 0; i < engagementWindows.Length; i++)
            {
                var window = engagementWindows[i];
                float progress;
                if (window != null && window.spawnerName == spawnerName &&
                    ProjectileEngagementMath.ContainsPlayer(window, chest, out progress))
                {
                    selected = window;
                    return true;
                }
            }
            return false;
        }

        bool EveryEngagementWindowPassed(int index)
        {
            if (player == null || members == null || index < 0 || index >= members.Length || members[index] == null)
                return false;
            Vector3 chest = player.position + Vector3.up * 1.2f;
            string spawnerName = members[index].name;
            bool found = false;
            for (int i = 0; i < engagementWindows.Length; i++)
            {
                var window = engagementWindows[i];
                if (window == null || window.spawnerName != spawnerName) continue;
                found = true;
                if (!ProjectileEngagementMath.HasPassed(window, chest)) return false;
            }
            return found;
        }

        void OnPlayerDied()
        {
            waitingForRespawn = true;
            RetireActiveIncoming(ProjectilePhraseCancellation.SequenceReset);
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
            RetireActiveIncoming(ProjectilePhraseCancellation.SequenceReset);
            current = 0;
            activePhraseShooter = null;
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

        void RetireActiveIncoming(ProjectilePhraseCancellation reason)
        {
            if (activePhraseShooter != null)
                activePhraseShooter.CancelSequencePhrase(reason, true);
            else if (activeBolt != null && activeBolt.IsIncoming)
            {
                if (Application.isPlaying) Destroy(activeBolt.gameObject);
                else DestroyImmediate(activeBolt.gameObject);
            }
            activeBolt = null;
            activePhraseShooter = null;
        }

        void ClearBindings()
        {
            boundInstances = new GameObject[0];
            shooters = new ProjectileShooter[0];
            player = null;
        }

        static ProjectileEngagementWindowDef[] CloneWindows(ProjectileEngagementWindowDef[] source)
        {
            if (source == null) return new ProjectileEngagementWindowDef[0];
            var copy = new ProjectileEngagementWindowDef[source.Length];
            for (int i = 0; i < copy.Length; i++) copy[i] = CloneWindow(source[i]);
            return copy;
        }

        static ProjectileEngagementWindowDef CloneWindow(ProjectileEngagementWindowDef source)
        {
            if (source == null) return null;
            return new ProjectileEngagementWindowDef
            {
                spawnerName = source.spawnerName,
                routeStart = source.routeStart,
                routeEnd = source.routeEnd,
                halfWidth = source.halfWidth,
                heightTolerance = source.heightTolerance,
                arrivalStart = source.arrivalStart,
                arrivalEnd = source.arrivalEnd,
            };
        }
    }
}
