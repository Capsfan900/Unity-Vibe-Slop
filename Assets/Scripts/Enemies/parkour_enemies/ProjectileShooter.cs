using UnityEngine;

namespace VibeGame1
{
    /// <summary>Observable reason the latest requested projectile could not launch yet.</summary>
    public enum ProjectileShotReadiness
    {
        None,
        Ready,
        EnemyUnavailable,
        TargetUnavailable,
        OutOfBand,
        NoLineOfSight,
        FacingAway,
        UnsafeFlight,
        NoContact,
        BlockedFlight,
        OutsideEngagementWindow,
        ArrivalSpacing
    }

    /// <summary>Deterministic reason an already-started projectile phrase stopped emitting follow-ups.</summary>
    public enum ProjectilePhraseCancellation
    {
        None,
        EnemyUnavailable,
        TargetUnavailable,
        OutOfBand,
        NoLineOfSight,
        FacingAway,
        UnsafeFlight,
        NoContact,
        BlockedFlight,
        OutsideEngagementWindow,
        FollowupWindowExpired,
        SequenceReset,
        SequenceTimeout,
        SequenceReconfigured,
        Disabled
    }

    /// <summary>
    /// Makes a parkour enemy a ranged presence. A one-shot sentry retains the shared span metronome.
    /// A burst sentry owns one complete data-authored phrase, spacing the predicted CONTACTS rather than
    /// blindly spacing launch frames. Every emission repeats life, range, sight, facing and real-flight
    /// validation; a cancelled phrase is never repaid as a catch-up burst.
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class ProjectileShooter : MonoBehaviour
    {
        public const float CueMargin = 0.16f;
        public const float FirstBeatDelay = 1f;
        public const float SpawnForwardOffset = 0.6f;
        public const int MaxBurstShots = 3;
        public const float TightRouteRetrySeconds = 0.08f;

        EnemyController ctrl;
        PlayerCombat combat;
        FirstPersonMotor motor;
        float nextFireAt;
        bool acquired;
        bool sequenceControlled;
        static Material boltMat;
        const int BroadClearanceHitCapacity = 8;
        static readonly RaycastHit[] broadClearanceHits = new RaycastHit[BroadClearanceHitCapacity];

        // One shared contact reservation closes the reacquisition bug where two different launch phases
        // collapsed to now + acquireDelay and then arrived together. The reservation covers a whole burst.
        static float spanEpoch;
        static bool spanEpochSet;
        static int spanIndex;
        static float nextAutonomousContactAt;

        readonly Projectile[] phraseBolts = new Projectile[MaxBurstShots];
        int phraseTargetCount;
        int phraseShotsEmitted;
        bool phraseActive;
        bool phraseEmissionsComplete = true;
        float nextPhraseContactAt;
        float followupDeadlineAt;
        ProjectileEngagementWindowDef activeEngagementWindow;
        bool phraseHasEngagementWindow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetSpanBeat()
        {
            spanEpochSet = false;
            spanIndex = 0;
            nextAutonomousContactAt = 0f;
        }

        /// <summary>Bolts fired since spawn. Read by tests and the harness.</summary>
        public int Fired { get; private set; }
        public int PhraseShotsEmitted { get { return phraseShotsEmitted; } }
        public int PhraseTargetCount { get { return phraseTargetCount; } }
        public bool PhraseEmissionsComplete { get { return phraseEmissionsComplete; } }
        public bool PhraseActive { get { return phraseActive; } }
        public bool IsSequenceControlled { get { return sequenceControlled; } }
        public float LastPredictedContactAt { get; private set; }
        public ProjectileShotReadiness LastReadiness { get; private set; }
        public ProjectilePhraseCancellation LastPhraseCancellation { get; private set; }
        public int PhraseCancellationCount { get; private set; }
        public static float NextAutonomousContactAt { get { return nextAutonomousContactAt; } }

        /// <summary>
        /// True once every emitted shot has stopped being an incoming player attack. Reflected bolts count
        /// as resolved here and remain alive for their return payoff.
        /// </summary>
        public bool PhraseIncomingResolved
        {
            get
            {
                for (int i = 0; i < phraseShotsEmitted && i < phraseBolts.Length; i++)
                    if (phraseBolts[i] != null && phraseBolts[i].IsIncoming) return false;
                return true;
            }
        }

        public void SetSequenceControlled(bool controlled)
        {
            sequenceControlled = controlled;
            if (controlled) acquired = false;
        }

        public float SequenceAcquireDelay
        {
            get { return ctrl != null && ctrl.data != null ? ctrl.data.projectileAcquireDelay : 0f; }
        }

        /// <summary>
        /// Starts the member's entire phrase for a data-authored sequence. Follow-ups remain owned by this
        /// shooter; the sequence waits for all emissions and incoming obligations rather than advancing
        /// after the first deflect.
        /// </summary>
        public Projectile TryFireSequenceShot(bool allowFire, out bool enteredBand, out bool shotReady)
        {
            return TryFireSequenceShot(allowFire, null, false, out enteredBand, out shotReady);
        }

        public Projectile TryFireSequenceShot(bool allowFire, ProjectileEngagementWindowDef engagementWindow,
                                              bool hasEngagementWindow, out bool enteredBand,
                                              out bool shotReady)
        {
            enteredBand = false;
            shotReady = false;
            if (!sequenceControlled || phraseActive) return null;
            EnemyData data;
            if (!CanShoot(out data)) return null;

            ProjectileFlightPlan plan;
            LastReadiness = EvaluateShot(data, out enteredBand, out plan);
            if (LastReadiness == ProjectileShotReadiness.Ready && hasEngagementWindow &&
                !ProjectileEngagementMath.AllowsPredictedContact(engagementWindow, Chest, TargetVelocity,
                                                                 plan.contactSeconds))
                LastReadiness = ProjectileShotReadiness.OutsideEngagementWindow;
            shotReady = LastReadiness == ProjectileShotReadiness.Ready;
            if (!allowFire || !shotReady) return null;
            return BeginPhrase(data, plan, false, engagementWindow, hasEngagementWindow);
        }

        public void CancelSequencePhrase(ProjectilePhraseCancellation reason, bool retireIncoming)
        {
            CancelPhrase(reason, retireIncoming);
        }

        void Awake()
        {
            ctrl = GetComponent<EnemyController>();
            if (!spanEpochSet) { spanEpoch = Time.time; spanEpochSet = true; }
            var spawnData = ctrl != null ? ctrl.data : null;
            float interval = spawnData != null ? spawnData.projectileInterval : 1.6f;
            float offset = (spanIndex++ % 2) * interval * 0.5f;
            nextFireAt = ProjectileMath.FirstBeat(spanEpoch, Time.time, interval, offset, FirstBeatDelay);
        }

        void OnDisable()
        {
            CancelPhrase(ProjectilePhraseCancellation.Disabled, false);
        }

        void Update()
        {
            if (phraseActive)
            {
                UpdatePhrase();
                return;
            }
            if (sequenceControlled) return;

            EnemyData data;
            if (!CanShoot(out data)) { acquired = false; return; }
            if (!EnsureTarget()) return;

            Vector3 muzzle = Muzzle;
            Vector3 chest = Chest;
            float distance = Vector3.Distance(muzzle, chest);
            bool inBand = ProjectileMath.InBand(distance, data.projectileMinRange, data.projectileMaxRange);
            if (!inBand)
            {
                acquired = false;
                LastReadiness = ProjectileShotReadiness.OutOfBand;
                return;
            }
            if (!EnemyController.HasLineOfSight(muzzle, combat.transform.position))
            {
                // A blue traversal sentry is often read through a railing or between stacked ledges.
                // Once it has paid the authored arm-up, one occluded frame must not make it pay the
                // whole 0.7 s again. The shot itself still repeats LOS and flight validation before it
                // can emit. Heavy Sentries and Surge Turrets keep their conservative reacquisition.
                if (!PreservesAcquisitionOnOcclusion(data.projectileAllowTightRouteShots)) acquired = false;
                LastReadiness = ProjectileShotReadiness.NoLineOfSight;
                return;
            }

            if (!acquired)
            {
                acquired = true;
                nextFireAt = ProjectileMath.AcquireBeat(nextFireAt, Time.time, data.projectileInterval,
                                                        data.projectileAcquireDelay);
            }
            if (Time.time < nextFireAt) return;

            ProjectileFlightPlan plan;
            bool enteredBand;
            LastReadiness = EvaluateShot(data, out enteredBand, out plan);
            if (LastReadiness != ProjectileShotReadiness.Ready)
            {
                // A transient facing/contact/path rejection should not throw away an entire legal
                // parkour window. Ordinary blue sentries retry briefly; conservative ranged enemies
                // retain their authored full-beat scheduling.
                nextFireAt = RejectionRetryTime(data.projectileAllowTightRouteShots, nextFireAt,
                                                Time.time, data.projectileInterval);
                return;
            }
            if (phraseHasEngagementWindow &&
                !ProjectileEngagementMath.AllowsPredictedContact(activeEngagementWindow, Chest,
                                                                 TargetVelocity, plan.contactSeconds))
            {
                LastReadiness = ProjectileShotReadiness.OutsideEngagementWindow;
                CancelPhrase(ProjectilePhraseCancellation.OutsideEngagementWindow, false);
                return;
            }

            float predictedContact = Time.time + plan.contactSeconds;
            if (!ProjectileFlightMath.ContactSlotOpen(predictedContact, nextAutonomousContactAt))
            {
                LastReadiness = ProjectileShotReadiness.ArrivalSpacing;
                // Two tight-route ghosts can inherit the same span phase. Moving both forward by one full
                // beat preserves the tie forever and lets the first Update monopolise every contact. Retry
                // the blue traversal tool at the first safe slot; Heavy and Surge timing stays untouched.
                nextFireAt = data.projectileAllowTightRouteShots
                    ? ProjectileFlightMath.RetryTimeForContactSlot(Time.time, predictedContact,
                                                                  nextAutonomousContactAt)
                    : ProjectileMath.NextBeat(nextFireAt, Time.time, data.projectileInterval);
                return;
            }

            BeginPhrase(data, plan, true);
            if (phraseTargetCount <= 1)
                nextFireAt = ProjectileMath.NextBeat(nextFireAt, Time.time, data.projectileInterval);
        }

        void UpdatePhrase()
        {
            EnemyData data;
            if (!CanShoot(out data))
            {
                CancelPhrase(ProjectilePhraseCancellation.EnemyUnavailable, false);
                return;
            }
            if (!EnsureTarget())
            {
                CancelPhrase(ProjectilePhraseCancellation.TargetUnavailable, false);
                return;
            }

            float minimumFlight = Projectile.CueLead + CueMargin;
            if (Time.time + minimumFlight + ProjectileFlightMath.ForecastStep < nextPhraseContactAt) return;
            if (Time.time > followupDeadlineAt)
            {
                CancelPhrase(ProjectilePhraseCancellation.FollowupWindowExpired, false);
                return;
            }

            ProjectileFlightPlan plan;
            bool enteredBand;
            LastReadiness = EvaluateShot(data, out enteredBand, out plan);
            if (LastReadiness != ProjectileShotReadiness.Ready)
            {
                CancelPhrase(CancellationFor(LastReadiness), false);
                return;
            }
            if (phraseHasEngagementWindow &&
                !ProjectileEngagementMath.AllowsPredictedContact(activeEngagementWindow, Chest,
                                                                 TargetVelocity, plan.contactSeconds))
            {
                LastReadiness = ProjectileShotReadiness.OutsideEngagementWindow;
                CancelPhrase(ProjectilePhraseCancellation.OutsideEngagementWindow, false);
                return;
            }

            float contactFloor = nextPhraseContactAt;
            float cadence = Mathf.Max(0.01f, data.projectileBurstInterval);
            for (int i = 0; i < phraseShotsEmitted; i++)
            {
                Projectile prior = phraseBolts[i];
                if (prior == null || !prior.IsIncoming) continue;
                float priorContact = prior.PredictedContactAt;
                if (priorContact >= float.MaxValue) contactFloor = float.MaxValue;
                else contactFloor = Mathf.Max(contactFloor,
                    ProjectileFlightMath.NextContactTime(priorContact, cadence));
            }

            float predictedContact = Time.time + plan.contactSeconds;
            if (!ProjectileFlightMath.ContactSlotOpen(predictedContact, contactFloor))
            {
                LastReadiness = ProjectileShotReadiness.ArrivalSpacing;
                return;
            }

            Emit(plan, data);
            if (!sequenceControlled)
                nextAutonomousContactAt = Mathf.Max(nextAutonomousContactAt,
                    ProjectileFlightMath.NextContactTime(LastPredictedContactAt, cadence));

            if (phraseShotsEmitted >= phraseTargetCount)
            {
                phraseActive = false;
                phraseEmissionsComplete = true;
                nextFireAt = Time.time + Mathf.Max(0.01f, data.projectileInterval);
                return;
            }

            nextPhraseContactAt = ProjectileFlightMath.NextContactTime(LastPredictedContactAt, cadence);
            followupDeadlineAt = nextPhraseContactAt + cadence;
        }

        Projectile BeginPhrase(EnemyData data, ProjectileFlightPlan plan, bool reserveAutonomousContact)
        {
            return BeginPhrase(data, plan, reserveAutonomousContact, null, false);
        }

        Projectile BeginPhrase(EnemyData data, ProjectileFlightPlan plan, bool reserveAutonomousContact,
                               ProjectileEngagementWindowDef engagementWindow, bool hasEngagementWindow)
        {
            for (int i = 0; i < phraseBolts.Length; i++) phraseBolts[i] = null;
            phraseTargetCount = Mathf.Clamp(data.projectileBurstCount, 1, MaxBurstShots);
            phraseShotsEmitted = 0;
            phraseActive = phraseTargetCount > 1;
            phraseEmissionsComplete = phraseTargetCount <= 1;
            LastPhraseCancellation = ProjectilePhraseCancellation.None;
            activeEngagementWindow = engagementWindow;
            phraseHasEngagementWindow = hasEngagementWindow;

            Projectile first = Emit(plan, data);
            float cadence = Mathf.Max(0.01f, data.projectileBurstInterval);
            nextPhraseContactAt = ProjectileFlightMath.NextContactTime(LastPredictedContactAt, cadence);
            followupDeadlineAt = nextPhraseContactAt + cadence;
            if (reserveAutonomousContact)
            {
                float phraseEnd = LastPredictedContactAt + phraseTargetCount * cadence;
                nextAutonomousContactAt = Mathf.Max(nextAutonomousContactAt, phraseEnd);
            }
            return first;
        }

        Projectile Emit(ProjectileFlightPlan plan, EnemyData data)
        {
            Projectile shot = FireAt(plan, data);
            if (phraseShotsEmitted < phraseBolts.Length) phraseBolts[phraseShotsEmitted] = shot;
            phraseShotsEmitted++;
            LastPredictedContactAt = Time.time + plan.contactSeconds;
            LastReadiness = ProjectileShotReadiness.Ready;
            return shot;
        }

        bool CanShoot(out EnemyData data)
        {
            data = ctrl != null ? ctrl.data : null;
            if (data == null || !data.shootsProjectiles || data.projectileAttack == null ||
                !ctrl.IsAlive || ctrl.Current == EnemyController.State.Idle || ctrl.IsStaggered ||
                ctrl.IsCommitted || ctrl.aggroLocked)
            {
                LastReadiness = ProjectileShotReadiness.EnemyUnavailable;
                return false;
            }
            return true;
        }

        bool EnsureTarget()
        {
            if (combat == null) combat = FindAnyObjectByType<PlayerCombat>();
            if (combat == null)
            {
                LastReadiness = ProjectileShotReadiness.TargetUnavailable;
                return false;
            }
            if (motor == null) motor = combat.GetComponent<FirstPersonMotor>();
            return true;
        }

        Vector3 Muzzle { get { return transform.position + Vector3.up * 1.3f; } }
        Vector3 Chest { get { return combat.transform.position + Vector3.up * 1.2f; } }
        Vector3 TargetVelocity { get { return motor != null ? motor.Velocity : Vector3.zero; } }

        /// <summary>
        /// Ordinary blue route tools retain an acquisition through momentary cover. This never makes a
        /// shot legal: LOS and the complete flight plan are still revalidated on the emission frame.
        /// Heavy Sentries and Surge Turrets deliberately return false.
        /// </summary>
        public static bool PreservesAcquisitionOnOcclusion(bool allowTightRouteShots)
        {
            return allowTightRouteShots;
        }

        /// <summary>Next autonomous attempt after a transient planning rejection.</summary>
        public static float RejectionRetryTime(bool allowTightRouteShots, float previousBeat,
                                               float now, float interval)
        {
            return allowTightRouteShots
                ? now + TightRouteRetrySeconds
                : ProjectileMath.NextBeat(previousBeat, now, interval);
        }

        ProjectileShotReadiness EvaluateShot(EnemyData data, out bool enteredBand,
                                               out ProjectileFlightPlan plan)
        {
            plan = default(ProjectileFlightPlan);
            enteredBand = false;
            if (!EnsureTarget()) return ProjectileShotReadiness.TargetUnavailable;

            Vector3 muzzle = Muzzle;
            Vector3 chest = Chest;
            Vector3 targetVelocity = TargetVelocity;
            float distance = Vector3.Distance(muzzle, chest);
            enteredBand = ProjectileMath.InBand(distance, data.projectileMinRange, data.projectileMaxRange);
            if (!enteredBand) return ProjectileShotReadiness.OutOfBand;
            if (!EnemyController.HasLineOfSight(muzzle, combat.transform.position))
                return ProjectileShotReadiness.NoLineOfSight;
            int worldMask = ~(Layers.EnemyMask | Layers.PlayerMask | (1 << Layers.Interactable));
            // Ray/line casts are not required to report a collider that already contains their origin.
            // Reject a buried muzzle before any clearance exception is selected, so malformed or
            // overhanging perch geometry can never let a shooter launch outward through a solid.
            if (Physics.CheckSphere(muzzle, 0.01f, worldMask, QueryTriggerInteraction.Ignore))
                return ProjectileShotReadiness.BlockedFlight;

            plan = ProjectileFlightMath.Plan(muzzle, chest, targetVelocity, data.projectileSpeed,
                data.projectileLead, data.projectileHomingDegPerSec, SpawnForwardOffset,
                Projectile.DefaultHitRadius, Projectile.CueLead + CueMargin, Projectile.DefaultMaxLife);
            if (plan.readiness == ProjectileFlightReadiness.UnsafeFlight)
                return ProjectileShotReadiness.UnsafeFlight;
            if (plan.readiness == ProjectileFlightReadiness.NoContact)
                return ProjectileShotReadiness.NoContact;

            float cone = GameManager.I != null && GameManager.I.statsData != null
                ? GameManager.I.statsData.facingConeDeg : 75f;
            bool readableArrival = data.projectileAllowTightRouteShots
                ? ProjectileMath.ArrivesInsideFacing(muzzle, chest, targetVelocity, plan.speed,
                                                     combat.transform.forward, cone)
                : ProjectileMath.ArrivesInFront(muzzle, chest, targetVelocity, plan.speed, cone);
            if (!readableArrival)
                return ProjectileShotReadiness.FacingAway;
            // Ordinary blue ghosts are traversal tools deliberately perched inside tight geometry. They
            // keep an exact forecast-path obstruction check, but do not use the 1 m PLAYER-CONTACT radius
            // as world clearance: that volume was brushing rails and silencing entire parkour spans.
            // Heavy Sentries and Surge Turrets retain the conservative broad probe unchanged.
            float worldClearance = data.projectileAllowTightRouteShots ? 0f : Projectile.DefaultHitRadius;
            Collider departureSupport = data.projectileIgnoreDepartureSupport && worldClearance > 0f
                ? FindDepartureSupport()
                : null;
            if (!FlightPathClear(plan, chest, targetVelocity, data.projectileHomingDegPerSec,
                                 worldClearance, departureSupport))
                return ProjectileShotReadiness.BlockedFlight;
            return ProjectileShotReadiness.Ready;
        }

        Collider FindDepartureSupport()
        {
            int mask = ~(Layers.EnemyMask | Layers.PlayerMask | (1 << Layers.Interactable));
            RaycastHit hit;
            return Physics.Raycast(transform.position + Vector3.up * 0.25f, Vector3.down, out hit, 2.5f,
                                   mask, QueryTriggerInteraction.Ignore)
                ? hit.collider
                : null;
        }

        static bool FlightPathClear(ProjectileFlightPlan plan, Vector3 targetPosition,
                                    Vector3 targetVelocity, float homingDegPerSecond,
                                    float clearanceRadius, Collider departureSupport)
        {
            int mask = ~(Layers.EnemyMask | Layers.PlayerMask | (1 << Layers.Interactable));
            Vector3 projectile = plan.launchPosition;
            Vector3 target = targetPosition;
            Vector3 direction = plan.direction;
            float elapsed = 0f;
            bool departingSupport = departureSupport != null;
            // ClosestPoint returns the query point when it is inside (or exactly on) a collider. A broad
            // cast starting inside its ignored support may report no entry hit, and Linecast is likewise
            // allowed to miss an origin overlap. Fail closed: the exception is only for a radius brush
            // beside an honestly clear muzzle, never permission to fire out through solid perch geometry.
            if (departingSupport &&
                (departureSupport.ClosestPoint(projectile) - projectile).sqrMagnitude <= 1e-8f)
                return false;
            bool supportTouched = departingSupport && SphereTouches(departureSupport, projectile, clearanceRadius);
            float departureTravel = 0f;
            float departureLimit = departingSupport
                ? clearanceRadius + departureSupport.bounds.extents.magnitude + plan.speed * ProjectileFlightMath.ForecastStep
                : 0f;
            while (elapsed < plan.contactSeconds)
            {
                float dt = Mathf.Min(ProjectileFlightMath.ForecastStep, plan.contactSeconds - elapsed);
                direction = ProjectileFlightMath.HomingDirection(direction, projectile, target,
                    targetVelocity, plan.speed, homingDegPerSecond, dt);
                Vector3 nextProjectile = projectile + direction * plan.speed * dt;
                Vector3 segment = nextProjectile - projectile;
                float distance = segment.magnitude;
                if (distance > 1e-5f)
                {
                    Vector3 rayDirection = segment / distance;
                    if (clearanceRadius <= 1e-5f)
                    {
                        if (Physics.Linecast(projectile, nextProjectile, mask,
                                            QueryTriggerInteraction.Ignore)) return false;
                    }
                    else if (!departingSupport)
                    {
                        if (Physics.SphereCast(projectile, clearanceRadius, rayDirection, out _, distance,
                                               mask, QueryTriggerInteraction.Ignore)) return false;
                    }
                    else
                    {
                        // The support exemption is radius-only. A collider on the actual bolt line still
                        // blocks, including the support itself, and a sibling blocker in the same segment
                        // cannot hide behind the ignored nearest hit.
                        if (Physics.Linecast(projectile, nextProjectile, mask,
                                            QueryTriggerInteraction.Ignore)) return false;
                        int hits = Physics.SphereCastNonAlloc(projectile, clearanceRadius, rayDirection,
                                                              broadClearanceHits, distance, mask,
                                                              QueryTriggerInteraction.Ignore);
                        if (hits >= BroadClearanceHitCapacity) return false; // fail closed on truncation
                        for (int i = 0; i < hits; i++)
                        {
                            Collider hit = broadClearanceHits[i].collider;
                            if (hit != null && hit != departureSupport) return false;
                        }
                    }
                }
                projectile = nextProjectile;
                target += targetVelocity * dt;
                elapsed += dt;
                if (departingSupport)
                {
                    departureTravel += distance;
                    bool touching = SphereTouches(departureSupport, projectile, clearanceRadius);
                    supportTouched |= touching;
                    // Once the radius-expanded support has actually been left, or the small departure
                    // envelope has elapsed without touching it, the exemption can never re-arm.
                    if ((supportTouched && !touching) || departureTravel > departureLimit)
                        departingSupport = false;
                }
            }
            return true;
        }

        static bool SphereTouches(Collider collider, Vector3 center, float radius)
        {
            if (collider == null || radius <= 0f) return false;
            Vector3 closest = collider.ClosestPoint(center);
            return (closest - center).sqrMagnitude <= radius * radius + 1e-5f;
        }

        void CancelPhrase(ProjectilePhraseCancellation reason, bool retireIncoming)
        {
            bool hadPending = phraseActive || !PhraseIncomingResolved;
            phraseActive = false;
            phraseEmissionsComplete = true;
            phraseHasEngagementWindow = false;
            activeEngagementWindow = null;
            if (hadPending && reason != ProjectilePhraseCancellation.None)
            {
                LastPhraseCancellation = reason;
                PhraseCancellationCount++;
            }
            if (retireIncoming) RetirePhraseIncoming();
            var data = ctrl != null ? ctrl.data : null;
            if (data != null) nextFireAt = Time.time + Mathf.Max(0.01f, data.projectileInterval);
        }

        void RetirePhraseIncoming()
        {
            for (int i = 0; i < phraseShotsEmitted && i < phraseBolts.Length; i++)
            {
                Projectile bolt = phraseBolts[i];
                if (bolt == null || !bolt.IsIncoming) continue;
                if (Application.isPlaying) Destroy(bolt.gameObject);
                else DestroyImmediate(bolt.gameObject);
                phraseBolts[i] = null;
            }
        }

        static ProjectilePhraseCancellation CancellationFor(ProjectileShotReadiness reason)
        {
            switch (reason)
            {
                case ProjectileShotReadiness.EnemyUnavailable: return ProjectilePhraseCancellation.EnemyUnavailable;
                case ProjectileShotReadiness.TargetUnavailable: return ProjectilePhraseCancellation.TargetUnavailable;
                case ProjectileShotReadiness.OutOfBand: return ProjectilePhraseCancellation.OutOfBand;
                case ProjectileShotReadiness.NoLineOfSight: return ProjectilePhraseCancellation.NoLineOfSight;
                case ProjectileShotReadiness.FacingAway: return ProjectilePhraseCancellation.FacingAway;
                case ProjectileShotReadiness.UnsafeFlight: return ProjectilePhraseCancellation.UnsafeFlight;
                case ProjectileShotReadiness.NoContact: return ProjectilePhraseCancellation.NoContact;
                case ProjectileShotReadiness.BlockedFlight: return ProjectilePhraseCancellation.BlockedFlight;
                case ProjectileShotReadiness.OutsideEngagementWindow: return ProjectilePhraseCancellation.OutsideEngagementWindow;
                default: return ProjectilePhraseCancellation.FollowupWindowExpired;
            }
        }

        Projectile FireAt(ProjectileFlightPlan plan, EnemyData data)
        {
            var go = new GameObject("Bolt");
            go.transform.position = plan.launchPosition;

            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Core";
            var col = core.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            core.transform.SetParent(go.transform, false);
            core.transform.localScale = Vector3.one * Projectile.CoreSize;
            if (boltMat == null)
            {
                boltMat = SlashFx.CreateAdditiveMaterial(new Color(1f, 0.55f, 0.2f, 1f));
                if (boltMat.HasProperty("_BaseColor")) boltMat.SetColor("_BaseColor", Projectile.HotCore);
                if (boltMat.HasProperty("_Color")) boltMat.SetColor("_Color", Projectile.HotCore);
            }
            var renderer = core.GetComponent<Renderer>();
            renderer.sharedMaterial = boltMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var projectile = go.AddComponent<Projectile>();
            projectile.Fire(ctrl, data, plan.direction, plan.speed, combat, plan.contactSeconds);
            Fired++;
            AudioManager.Play(Sfx.Tick, 0.5f, 1.3f, 0.05f);
            return projectile;
        }
    }
}
