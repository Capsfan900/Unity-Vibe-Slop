using UnityEngine;

namespace VibeGame1
{
    /// <summary>Why a projectile flight plan can or cannot be launched.</summary>
    public enum ProjectileFlightReadiness
    {
        Ready,
        UnsafeFlight,
        NoContact
    }

    /// <summary>
    /// Allocation-free result of planning one projectile from its real launch point to first sphere contact.
    /// The shooter, runtime projectile and level verification all consume this same abstraction.
    /// </summary>
    public struct ProjectileFlightPlan
    {
        public ProjectileFlightReadiness readiness;
        public Vector3 launchPosition;
        public Vector3 direction;
        public float speed;
        public float contactSeconds;

        public bool IsReady { get { return readiness == ProjectileFlightReadiness.Ready; } }
    }

    /// <summary>
    /// Pure projectile flight integration. It deliberately forecasts a constant-velocity target: the result
    /// is a short-horizon firing decision and cue estimate, not a promise about a player's future input.
    /// Capped homing uses <see cref="HomingDirection"/> in both the forecast and <see cref="Projectile"/>,
    /// so the two paths cannot silently grow different steering laws.
    /// </summary>
    public static class ProjectileFlightMath
    {
        public const float ForecastStep = 1f / 120f;
        const float MinimumSpeed = 0.01f;
        const int SpeedSearchSteps = 18;

        /// <summary>The next phrase contact is anchored to what was actually forecast, never stale debt.</summary>
        public static float NextContactTime(float previousPredictedContact, float cadence)
        {
            return previousPredictedContact + Mathf.Max(0.01f, cadence);
        }

        /// <summary>True once a reforecast follow-up cannot catch the preceding incoming contact.</summary>
        public static bool ContactSlotOpen(float predictedContact, float earliestContact)
        {
            return predictedContact + ForecastStep >= earliestContact;
        }

        /// <summary>
        /// Schedules a rejected autonomous shot for the first launch time whose unchanged flight would
        /// reach the open contact slot. Advancing a whole metronome beat here can reproduce the same tie
        /// forever when two sentries share a phase, starving whichever component updates second.
        /// </summary>
        public static float RetryTimeForContactSlot(float now, float predictedContact,
                                                    float earliestContact)
        {
            float delay = earliestContact - predictedContact + ForecastStep;
            return now + Mathf.Max(ForecastStep, delay);
        }

        /// <summary>Applies the one steering step shared by forecast and runtime flight.</summary>
        public static Vector3 HomingDirection(Vector3 currentDirection, Vector3 projectilePosition,
                                              Vector3 targetPosition, float homingDegPerSecond,
                                              float deltaTime)
        {
            return HomingDirection(currentDirection, projectilePosition, targetPosition, Vector3.zero,
                                   0f, homingDegPerSecond, deltaTime);
        }

        /// <summary>Turns toward the moving target's intercept point, not its stale current position.</summary>
        public static Vector3 HomingDirection(Vector3 currentDirection, Vector3 projectilePosition,
                                              Vector3 targetPosition, Vector3 targetVelocity,
                                              float projectileSpeed, float homingDegPerSecond,
                                              float deltaTime)
        {
            Vector3 current = currentDirection.sqrMagnitude > 1e-6f
                ? currentDirection.normalized
                : Vector3.forward;
            if (homingDegPerSecond <= 0f || deltaTime <= 0f) return current;

            Vector3 aim = projectileSpeed > MinimumSpeed
                ? InterceptTarget(projectilePosition, targetPosition, targetVelocity, projectileSpeed, 1f)
                : targetPosition;
            Vector3 wanted = aim - projectilePosition;
            if (wanted.sqrMagnitude <= 1e-6f) return current;
            return Vector3.RotateTowards(current, wanted.normalized,
                homingDegPerSecond * Mathf.Deg2Rad * deltaTime, 0f).normalized;
        }

        /// <summary>
        /// Forecasts earliest swept-sphere contact using bounded fixed steps. The target velocity includes
        /// vertical motion even though initial aim lead stays horizontal: jumping changes contact time and
        /// therefore must be represented in the safety decision.
        /// </summary>
        public static bool TryForecastContact(Vector3 launchPosition, Vector3 initialDirection, float speed,
                                              Vector3 targetPosition, Vector3 targetVelocity,
                                              float homingDegPerSecond, float contactRadius,
                                              float maxSeconds, out float contactSeconds)
        {
            float horizon = Mathf.Max(0f, maxSeconds);
            float radius = Mathf.Max(0f, contactRadius);
            Vector3 projectile = launchPosition;
            Vector3 target = targetPosition;
            Vector3 direction = initialDirection.sqrMagnitude > 1e-6f
                ? initialDirection.normalized
                : Vector3.forward;
            float elapsed = 0f;

            if ((projectile - target).sqrMagnitude <= radius * radius)
            {
                contactSeconds = 0f;
                return true;
            }

            while (elapsed < horizon)
            {
                float dt = Mathf.Min(ForecastStep, horizon - elapsed);
                direction = HomingDirection(direction, projectile, target, targetVelocity, speed,
                                            homingDegPerSecond, dt);
                Vector3 nextProjectile = projectile + direction * Mathf.Max(MinimumSpeed, speed) * dt;
                Vector3 nextTarget = target + targetVelocity * dt;
                float fraction;
                if (ProjectileMath.SweptSphereFirstHit(projectile, nextProjectile, target, nextTarget,
                                                       radius, out fraction))
                {
                    contactSeconds = elapsed + fraction * dt;
                    return true;
                }
                projectile = nextProjectile;
                target = nextTarget;
                elapsed += dt;
            }

            contactSeconds = float.PositiveInfinity;
            return false;
        }

        /// <summary>
        /// Plans from the real projectile root, not the muzzle. If the desired speed contacts before the
        /// cue budget, a bounded speed search finds the fastest slower flight that still contacts safely.
        /// A target already too close even at the minimum speed is explicitly unsafe; it is never fired at
        /// and never certified by a muzzle-distance approximation.
        /// </summary>
        public static ProjectileFlightPlan Plan(Vector3 muzzle, Vector3 targetPosition,
                                                Vector3 targetVelocity, float desiredSpeed, float lead,
                                                float homingDegPerSecond, float spawnForwardOffset,
                                                float contactRadius, float minimumContactSeconds,
                                                float maxSeconds)
        {
            float topSpeed = Mathf.Max(MinimumSpeed, desiredSpeed);
            ProjectileFlightPlan desired = Candidate(muzzle, targetPosition, targetVelocity, topSpeed,
                lead, homingDegPerSecond, spawnForwardOffset, contactRadius, maxSeconds);
            if (desired.readiness == ProjectileFlightReadiness.NoContact) return desired;
            if (desired.contactSeconds + 1e-4f >= minimumContactSeconds) return desired;

            ProjectileFlightPlan slow = Candidate(muzzle, targetPosition, targetVelocity, MinimumSpeed,
                lead, homingDegPerSecond, spawnForwardOffset, contactRadius, maxSeconds);
            if (slow.readiness != ProjectileFlightReadiness.NoContact &&
                slow.contactSeconds + 1e-4f < minimumContactSeconds)
            {
                desired.readiness = ProjectileFlightReadiness.UnsafeFlight;
                return desired;
            }

            float low = MinimumSpeed;
            float high = topSpeed;
            ProjectileFlightPlan best = new ProjectileFlightPlan
            {
                readiness = ProjectileFlightReadiness.UnsafeFlight,
                launchPosition = desired.launchPosition,
                direction = desired.direction,
                speed = desired.speed,
                contactSeconds = desired.contactSeconds
            };
            bool foundSafeContact = false;

            for (int i = 0; i < SpeedSearchSteps; i++)
            {
                float candidateSpeed = (low + high) * 0.5f;
                ProjectileFlightPlan candidate = Candidate(muzzle, targetPosition, targetVelocity,
                    candidateSpeed, lead, homingDegPerSecond, spawnForwardOffset, contactRadius, maxSeconds);
                if (candidate.readiness == ProjectileFlightReadiness.NoContact)
                {
                    low = candidateSpeed;
                    continue;
                }
                if (candidate.contactSeconds + 1e-4f >= minimumContactSeconds)
                {
                    best = candidate;
                    foundSafeContact = true;
                    low = candidateSpeed;
                }
                else high = candidateSpeed;
            }

            if (foundSafeContact) return best;
            desired.readiness = ProjectileFlightReadiness.UnsafeFlight;
            return desired;
        }

        static ProjectileFlightPlan Candidate(Vector3 muzzle, Vector3 targetPosition,
                                              Vector3 targetVelocity, float speed, float lead,
                                              float homingDegPerSecond, float spawnForwardOffset,
                                              float contactRadius, float maxSeconds)
        {
            Vector3 direction = Vector3.forward;
            Vector3 launch = muzzle;
            // Aim and actual root origin depend on each other by the small forward spawn offset. Two passes
            // converge well below the 120 Hz forecast step while preserving the existing flat lead law.
            for (int i = 0; i < 2; i++)
            {
                Vector3 aim = InterceptTarget(launch, targetPosition, targetVelocity, speed, lead);
                Vector3 delta = aim - launch;
                direction = delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector3.forward;
                launch = muzzle + direction * Mathf.Max(0f, spawnForwardOffset);
            }

            float contact;
            bool reaches = TryForecastContact(launch, direction, speed, targetPosition, targetVelocity,
                homingDegPerSecond, contactRadius, maxSeconds, out contact);
            return new ProjectileFlightPlan
            {
                readiness = reaches ? ProjectileFlightReadiness.Ready : ProjectileFlightReadiness.NoContact,
                launchPosition = launch,
                direction = direction,
                speed = speed,
                contactSeconds = contact
            };
        }

        /// <summary>
        /// Exact constant-velocity intercept for the initial aim. The older two-step distance estimate
        /// converges poorly when a speedrunner is moving at a large fraction of bolt speed: on oblique
        /// approaches it can aim far past the authored encounter before homing repairs the line.
        /// </summary>
        static Vector3 InterceptTarget(Vector3 origin, Vector3 targetPosition, Vector3 targetVelocity,
                                       float projectileSpeed, float lead)
        {
            Vector3 velocity = new Vector3(targetVelocity.x, 0f, targetVelocity.z) * Mathf.Clamp01(lead);
            Vector3 relative = targetPosition - origin;
            float a = Vector3.Dot(velocity, velocity) - projectileSpeed * projectileSpeed;
            float b = 2f * Vector3.Dot(relative, velocity);
            float c = Vector3.Dot(relative, relative);
            float time = 0f;

            if (Mathf.Abs(a) < 1e-5f)
            {
                if (Mathf.Abs(b) > 1e-5f) time = -c / b;
            }
            else
            {
                float discriminant = b * b - 4f * a * c;
                if (discriminant >= 0f)
                {
                    float root = Mathf.Sqrt(discriminant);
                    float first = (-b - root) / (2f * a);
                    float second = (-b + root) / (2f * a);
                    if (first > 0f && second > 0f) time = Mathf.Min(first, second);
                    else if (first > 0f) time = first;
                    else if (second > 0f) time = second;
                }
            }

            if (time <= 0f || float.IsNaN(time) || float.IsInfinity(time))
                return ProjectileMath.LeadTarget(origin, targetPosition, targetVelocity, projectileSpeed, lead);
            return targetPosition + velocity * time;
        }
    }

    /// <summary>Pure geometry shared by live sequences, authoring tests and encounter reports.</summary>
    public static class ProjectileEngagementMath
    {
        public static bool IsValid(ProjectileEngagementWindowDef window)
        {
            if (window == null || string.IsNullOrEmpty(window.spawnerName)) return false;
            Vector2 delta = new Vector2(window.routeEnd.x - window.routeStart.x,
                                        window.routeEnd.z - window.routeStart.z);
            return IsFinite(window.routeStart) && IsFinite(window.routeEnd) &&
                   delta.sqrMagnitude > 0.01f && window.halfWidth >= 0.1f &&
                   window.heightTolerance >= 0.1f && window.arrivalStart >= 0f &&
                   window.arrivalEnd > window.arrivalStart && window.arrivalEnd <= delta.magnitude + 0.01f;
        }

        public static bool ContainsPlayer(ProjectileEngagementWindowDef window, Vector3 playerChest,
                                          out float progress)
        {
            float lateral, vertical, length;
            Measure(window, playerChest, out progress, out lateral, out vertical, out length);
            return IsValid(window) && progress >= 0f && progress <= length &&
                   lateral <= window.halfWidth && vertical <= window.heightTolerance;
        }

        public static bool AllowsPredictedContact(ProjectileEngagementWindowDef window,
                                                  Vector3 playerChest, Vector3 playerVelocity,
                                                  float contactSeconds)
        {
            if (!IsValid(window) || contactSeconds < 0f || float.IsInfinity(contactSeconds) ||
                float.IsNaN(contactSeconds)) return false;
            Vector3 predicted = playerChest + playerVelocity * contactSeconds;
            float progress, lateral, vertical, length;
            Measure(window, predicted, out progress, out lateral, out vertical, out length);
            return progress >= window.arrivalStart && progress <= window.arrivalEnd &&
                   lateral <= window.halfWidth && vertical <= window.heightTolerance;
        }

        public static bool HasPassed(ProjectileEngagementWindowDef window, Vector3 playerChest)
        {
            float progress, lateral, vertical, length;
            Measure(window, playerChest, out progress, out lateral, out vertical, out length);
            return IsValid(window) && progress > length;
        }

        static void Measure(ProjectileEngagementWindowDef window, Vector3 point, out float progress,
                            out float lateral, out float vertical, out float length)
        {
            if (window == null)
            {
                progress = lateral = vertical = length = 0f;
                return;
            }
            Vector2 start = new Vector2(window.routeStart.x, window.routeStart.z);
            Vector2 end = new Vector2(window.routeEnd.x, window.routeEnd.z);
            Vector2 delta = end - start;
            length = delta.magnitude;
            Vector2 direction = length > 0.001f ? delta / length : Vector2.up;
            Vector2 relative = new Vector2(point.x, point.z) - start;
            progress = Vector2.Dot(relative, direction);
            lateral = Mathf.Abs(relative.x * direction.y - relative.y * direction.x);
            float t = length > 0.001f ? Mathf.Clamp01(progress / length) : 0f;
            float expectedY = Mathf.Lerp(window.routeStart.y, window.routeEnd.y, t);
            vertical = Mathf.Abs(point.y - expectedY);
        }

        static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
                   !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
                   !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }
    }
}
