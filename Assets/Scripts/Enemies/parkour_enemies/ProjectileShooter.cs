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

        EnemyController ctrl;
        PlayerCombat combat;
        FirstPersonMotor motor;
        float nextFireAt;
        bool acquired;
        bool sequenceControlled;
        static Material boltMat;

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
                acquired = false;
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
                nextFireAt = ProjectileMath.NextBeat(nextFireAt, Time.time, data.projectileInterval);
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
                nextFireAt = ProjectileMath.NextBeat(nextFireAt, Time.time, data.projectileInterval);
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

            plan = ProjectileFlightMath.Plan(muzzle, chest, targetVelocity, data.projectileSpeed,
                data.projectileLead, data.projectileHomingDegPerSec, SpawnForwardOffset,
                Projectile.DefaultHitRadius, Projectile.CueLead + CueMargin, Projectile.DefaultMaxLife);
            if (plan.readiness == ProjectileFlightReadiness.UnsafeFlight)
                return ProjectileShotReadiness.UnsafeFlight;
            if (plan.readiness == ProjectileFlightReadiness.NoContact)
                return ProjectileShotReadiness.NoContact;

            float cone = GameManager.I != null && GameManager.I.statsData != null
                ? GameManager.I.statsData.facingConeDeg : 75f;
            if (!ProjectileMath.ArrivesInFront(muzzle, chest, targetVelocity, plan.speed, cone))
                return ProjectileShotReadiness.FacingAway;
            if (!FlightPathClear(plan, chest, targetVelocity, data.projectileHomingDegPerSec))
                return ProjectileShotReadiness.BlockedFlight;
            return ProjectileShotReadiness.Ready;
        }

        static bool FlightPathClear(ProjectileFlightPlan plan, Vector3 targetPosition,
                                    Vector3 targetVelocity, float homingDegPerSecond)
        {
            int mask = ~(Layers.EnemyMask | Layers.PlayerMask | (1 << Layers.Interactable));
            Vector3 projectile = plan.launchPosition;
            Vector3 target = targetPosition;
            Vector3 direction = plan.direction;
            float elapsed = 0f;
            while (elapsed < plan.contactSeconds)
            {
                float dt = Mathf.Min(ProjectileFlightMath.ForecastStep, plan.contactSeconds - elapsed);
                direction = ProjectileFlightMath.HomingDirection(direction, projectile, target,
                    targetVelocity, plan.speed, homingDegPerSecond, dt);
                Vector3 nextProjectile = projectile + direction * plan.speed * dt;
                Vector3 segment = nextProjectile - projectile;
                float distance = segment.magnitude;
                if (distance > 1e-5f && Physics.SphereCast(projectile, Projectile.DefaultHitRadius,
                    segment / distance, out _, distance, mask, QueryTriggerInteraction.Ignore)) return false;
                projectile = nextProjectile;
                target += targetVelocity * dt;
                elapsed += dt;
            }
            return true;
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
