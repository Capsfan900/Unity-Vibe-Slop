using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The arithmetic of a parriable projectile, with no Unity objects in it (the <see cref="ParryImpulse"/>
    /// / <see cref="TraversalMath"/> arrangement): <see cref="Projectile"/> and <see cref="ProjectileShooter"/>
    /// are the thin layers that hand these numbers to transforms, and <c>ProjectileTests</c> drives the maths.
    /// </summary>
    public static class ProjectileMath
    {
        /// <summary>Seconds until a bolt at <paramref name="distance"/> arrives at <paramref name="speed"/>.</summary>
        public static float TimeToImpact(float distance, float speed)
        {
            return distance / Mathf.Max(0.01f, speed);
        }

        /// <summary>
        /// Forecast contact from the current closing rate of the bolt and target. Returning infinity for
        /// a separating/crossing pair keeps the cue and registry honest until homing turns the shot back in.
        /// </summary>
        public static float RelativeTimeToContact(Vector3 projectilePosition, Vector3 targetPosition,
                                                  Vector3 projectileVelocity, Vector3 targetVelocity,
                                                  float contactRadius)
        {
            Vector3 toTarget = targetPosition - projectilePosition;
            float distance = toTarget.magnitude;
            float remaining = Mathf.Max(0f, distance - Mathf.Max(0f, contactRadius));
            if (remaining <= 0f) return 0f;
            if (distance <= 0.0001f) return 0f;
            float closingSpeed = Vector3.Dot(projectileVelocity - targetVelocity, toTarget / distance);
            return closingSpeed > 0.001f ? remaining / closingSpeed : float.PositiveInfinity;
        }

        /// <summary>
        /// Earliest contact between a moving bolt and moving target during one frame. Work in relative
        /// space so a fast player crossing the shot is covered as faithfully as a fast projectile.
        /// </summary>
        public static bool SweptSphereFirstHit(Vector3 previousProjectile, Vector3 currentProjectile,
                                               Vector3 previousTarget, Vector3 currentTarget,
                                               float radius, out float fraction)
        {
            Vector3 start = previousProjectile - previousTarget;
            Vector3 delta = (currentProjectile - currentTarget) - start;
            float r = Mathf.Max(0f, radius);
            float c = Vector3.Dot(start, start) - r * r;
            if (c <= 0f) { fraction = 0f; return true; }

            float a = Vector3.Dot(delta, delta);
            if (a <= 1e-8f) { fraction = 0f; return false; }
            float b = 2f * Vector3.Dot(start, delta);
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) { fraction = 0f; return false; }

            float root = Mathf.Sqrt(discriminant);
            float inverse = 0.5f / a;
            float enter = (-b - root) * inverse;
            float exit = (-b + root) * inverse;
            if (enter > 1f || exit < 0f) { fraction = 0f; return false; }
            fraction = Mathf.Clamp01(enter);
            return true;
        }

        /// <summary>
        /// Selects the target point used at the start of a relative sweep. A teleport/respawn is a
        /// discontinuity, not a 100-metre collision path. Ordinary motion gets generous velocity-scaled
        /// slack so low frame rates do not get mistaken for teleports.
        /// </summary>
        public static Vector3 ContinuousTargetStart(Vector3 previousTarget, Vector3 currentTarget,
                                                    Vector3 expectedTargetVelocity, float continuityDeltaTime)
        {
            if (continuityDeltaTime <= 0f) return currentTarget;
            float allowance = 1f + expectedTargetVelocity.magnitude * continuityDeltaTime * 2f;
            return Vector3.Distance(previousTarget, currentTarget) <= allowance ? previousTarget : currentTarget;
        }

        /// <summary>
        /// Velocity used by runtime homing and contact forecasts. The player motor advances on PlayerDelta,
        /// not scaled world delta, so dividing its observed displacement by Time.deltaTime during hitstop
        /// invents a roughly fifty-times-faster runner and makes a valid bolt temporarily lose its cue.
        /// Prefer the motor's authoritative velocity; legacy targets fall back to the same player clock.
        /// </summary>
        public static Vector3 ForecastTargetVelocity(Vector3 expectedVelocity, bool hasExpectedVelocity,
                                                     Vector3 previousTarget, Vector3 currentTarget,
                                                     float playerDelta)
        {
            if (hasExpectedVelocity) return expectedVelocity;
            return playerDelta > 1e-6f ? (currentTarget - previousTarget) / playerDelta : Vector3.zero;
        }

        /// <summary>
        /// Removes the motor's deliberate downward ground-stick from a projectile forecast. The player is
        /// not falling while grounded, and forecasting that -2 m/s through the supporting deck makes an
        /// otherwise clear Heavy Sentry shot intersect the floor. Real airborne descent and every upward
        /// launch remain intact because vertical movement matters to the contact forecast there.
        /// </summary>
        public static Vector3 GroundAwareTargetVelocity(Vector3 velocity, bool isGrounded)
        {
            return GroundAwareTargetVelocity(velocity, isGrounded, Vector3.up);
        }

        /// <summary>
        /// Surface-aware overload. Only a flat support identifies negative Y as the motor's ground-stick;
        /// authored ramps retain their tuned vertical forecast until the motor exposes its true slope
        /// displacement as velocity rather than applying that displacement separately.
        /// </summary>
        public static Vector3 GroundAwareTargetVelocity(Vector3 velocity, bool isGrounded, Vector3 groundNormal)
        {
            bool flatSupport = groundNormal.sqrMagnitude < 1e-6f ||
                               Vector3.Dot(groundNormal.normalized, Vector3.up) >= 0.999f;
            if (isGrounded && flatSupport && velocity.y < 0f) velocity.y = 0f;
            return velocity;
        }

        /// <summary>
        /// The parry contract for enemies is that the cue fires <c>cueLead</c> (0.28 s) before impact.
        /// A bolt has no wind-up of its own -- its flight IS the wind-up -- so the cue is due the frame
        /// its remaining flight drops under the lead. Fires once.
        /// </summary>
        public static bool CueDue(float remainingSeconds, float cueLead, bool alreadyCued)
        {
            return !alreadyCued && remainingSeconds <= cueLead;
        }

        /// <summary>Is the shooter inside its band: far enough that the flight is readable, near enough to hit.</summary>
        public static bool InBand(float distance, float minRange, float maxRange)
        {
            return distance >= minRange && distance <= maxRange;
        }

        /// <summary>
        /// The speed a perfect deflect of a bolt buys: along where the player is LOOKING, flattened, so
        /// deflecting a bolt while facing the next ledge carries you toward it. Zero when the aim is
        /// straight up or down, rather than a NaN. Never written to the motor here -- it goes through
        /// <c>FirstPersonMotor.AddImpulse</c> (hard rule 10).
        /// </summary>
        public static Vector3 SpeedGain(Vector3 aimForward, float gain)
        {
            Vector3 f = new Vector3(aimForward.x, 0f, aimForward.z);
            if (f.sqrMagnitude < 1e-6f || gain <= 0f) return Vector3.zero;
            return f.normalized * gain;
        }

        /// <summary>
        /// The speed a bolt LEAVES at. A bolt must never arrive before its own cue (the flight is the
        /// wind-up), so inside <c>speed x (cueLead + margin)</c> metres the launch slows until the flight
        /// is exactly the cue lead plus the margin. Beyond that it is the data speed. This is what lets a
        /// sentry keep firing all the way in instead of going quiet inside a near edge.
        /// </summary>
        public static float LaunchSpeed(float distance, float speed, float cueLead, float margin)
        {
            float minFlight = Mathf.Max(0.05f, cueLead + margin);
            return Mathf.Min(speed, Mathf.Max(0.01f, distance) / minFlight);
        }

        /// <summary>
        /// Where to aim so a straight bolt meets a player who keeps running: the chest plus
        /// <c>lead x velocity x flight</c>, iterated twice so the flight time accounts for the lead
        /// itself. Vertical velocity is ignored -- a jump arc is the player's to keep.
        /// </summary>
        public static Vector3 LeadTarget(Vector3 muzzle, Vector3 chest, Vector3 playerVelocity, float speed, float lead)
        {
            Vector3 v = new Vector3(playerVelocity.x, 0f, playerVelocity.z) * Mathf.Clamp01(lead);
            Vector3 aim = chest;
            for (int i = 0; i < 2; i++)
            {
                float t = TimeToImpact(Vector3.Distance(muzzle, aim), speed);
                aim = chest + v * t;
            }
            return aim;
        }

        /// <summary>
        /// The metronome. The next beat is the previous beat plus the interval, so a shot that was held
        /// (no line, out of band) does not shift the rhythm; if the clock fell more than one interval
        /// behind it re-anchors to now so a long silence is not repaid with a burst.
        /// </summary>
        public static float NextBeat(float previousBeat, float now, float interval)
        {
            float next = previousBeat + interval;
            if (next < now) next = now + interval;
            return next;
        }

        /// <summary>
        /// F1, the ARM-UP (bolt-timing plan 2026-09-06). The metronome is HELD while the line is blocked
        /// or the player is out of band, so after seconds out of sight <c>nextFireAt</c> is already in the
        /// past and the shot leaves on the FIRST FRAME line of sight is established -- the frame you crest
        /// a ledge or land, and two stale perches covering one crest fire together. On the transition into
        /// band the beat is pushed to at least <paramref name="delay"/> from now: a sentry that has just
        /// seen you takes a breath. A shooter whose beat is ALREADY later than the arm-up keeps its beat
        /// exactly, so a sentry you are already running past is untouched. The arm-up never exceeds one
        /// interval -- an acquisition can cost at most one bolt.
        /// </summary>
        public static float AcquireBeat(float previousBeat, float now, float interval, float delay)
        {
            float d = Mathf.Max(0f, delay);
            if (interval > 0.01f) d = Mathf.Min(d, interval);
            float armed = now + d;
            return previousBeat >= armed ? previousBeat : armed;
        }

        /// <summary>Below this flat speed the player is not "running away" and their velocity is no proxy for
        /// where they are looking, so <see cref="ArrivesInFront"/> never refuses a shot.</summary>
        public const float RecedeSpeed = 1f;

        /// <summary>How much of a run has to point AWAY from the shooter before it counts as fleeing:
        /// cos 60 degrees. A player crossing a perch's arc is still shot at -- that is the span working as
        /// intended -- and only a back genuinely turned is spared.</summary>
        public const float RecedeCos = 0.5f;

        /// <summary>
        /// F3 (bolt-timing plan 2026-09-06): would this bolt arrive at a FLEEING BACK? A runner's flat
        /// velocity is the honest proxy for where they are facing at impact, so we predict the arrival
        /// point (the same two-step as <see cref="LeadTarget"/> at full lead), take the bearing the bolt
        /// comes FROM there (<see cref="ParryMath.SourceDirection"/>, the very rule the parry is judged
        /// by) and ask whether it is inside the player's cone. False only when the player is also
        /// RECEDING: a shot across or into a run is fair and still fires. The shooter uses this to refuse
        /// to LAUNCH -- never to hold a shot, which would make the metronome a slot machine.
        /// </summary>
        public static bool ArrivesInFront(Vector3 muzzle, Vector3 chest, Vector3 playerVelocity, float speed, float coneDeg)
        {
            Vector3 v = new Vector3(playerVelocity.x, 0f, playerVelocity.z);
            if (v.magnitude < RecedeSpeed) return true;
            Vector3 away = new Vector3(chest.x - muzzle.x, 0f, chest.z - muzzle.z);
            if (away.sqrMagnitude < 1e-6f) return true;
            if (Vector3.Dot(v.normalized, away.normalized) < RecedeCos) return true;   // closing or crossing the arc
            Vector3 arrival = LeadTarget(muzzle, chest, v, speed, 1f);
            Vector3 travel = arrival - muzzle;
            return ParryMath.IsFacing(v, ParryMath.SourceDirection(travel, muzzle, arrival), coneDeg);
        }

        /// <summary>The conservative movement-facing policy using the accepted flight's real contact tangent.</summary>
        public static bool ArrivesInFront(Vector3 muzzle, Vector3 chest, Vector3 playerVelocity,
                                          Vector3 contactDirection, Vector3 predictedChest, float coneDeg)
        {
            Vector3 v = new Vector3(playerVelocity.x, 0f, playerVelocity.z);
            if (v.magnitude < RecedeSpeed) return true;
            Vector3 away = new Vector3(chest.x - muzzle.x, 0f, chest.z - muzzle.z);
            if (away.sqrMagnitude < 1e-6f) return true;
            if (Vector3.Dot(v.normalized, away.normalized) < RecedeCos) return true;
            return ParryMath.IsFacing(v,
                ParryMath.SourceDirection(contactDirection, muzzle, predictedChest), coneDeg);
        }

        /// <summary>
        /// Actual-look variant used by ordinary blue traversal sentries. Movement is not facing: a player
        /// can backpedal while deliberately watching a squid, or sprint past while looking elsewhere.
        /// The predicted contact still uses velocity; only the parry-readability veto uses player forward.
        /// </summary>
        public static bool ArrivesInsideFacing(Vector3 muzzle, Vector3 chest, Vector3 playerVelocity,
                                               float speed, Vector3 playerForward, float coneDeg)
        {
            Vector3 arrival = LeadTarget(muzzle, chest, playerVelocity, speed, 1f);
            Vector3 travel = arrival - muzzle;
            return ParryMath.IsFacing(playerForward,
                                      ParryMath.SourceDirection(travel, muzzle, arrival), coneDeg);
        }

        /// <summary>The actual-look policy using the accepted flight's real contact tangent.</summary>
        public static bool ArrivesInsideFacing(Vector3 contactDirection, Vector3 muzzle,
                                               Vector3 predictedChest, Vector3 playerForward, float coneDeg)
        {
            return ParryMath.IsFacing(playerForward,
                ParryMath.SourceDirection(contactDirection, muzzle, predictedChest), coneDeg);
        }

        /// <summary>
        /// F4, ONE BEAT PER SPAN (bolt-timing plan 2026-09-06). A sentry's first shot used to be anchored
        /// to its own spawn frame, so perches built on the same frame argued two identical clocks. The
        /// first beat is instead taken off a shared level <paramref name="epoch"/> plus a per-sentry
        /// <paramref name="offset"/> (a half interval, alternating), advanced by whole intervals until it
        /// is at least <paramref name="minDelay"/> away: a tempo rather than two metronomes in unison.
        /// </summary>
        public static float FirstBeat(float epoch, float now, float interval, float offset, float minDelay)
        {
            float floor = now + Mathf.Max(0f, minDelay);
            if (interval <= 0.01f) return floor;
            float t = epoch + offset;
            if (t < floor) t += Mathf.Ceil((floor - t) / interval) * interval;
            return t;
        }

        /// <summary>Unit direction from a point back to the shooter's chest, or forward when degenerate.</summary>
        public static Vector3 ReflectDirection(Vector3 from, Vector3 shooterChest, Vector3 fallback)
        {
            Vector3 d = shooterChest - from;
            return d.sqrMagnitude < 1e-6f ? fallback.normalized : d.normalized;
        }
    }

    /// <summary>
    /// Presentation-only motion for an incoming bolt. The projectile root remains on
    /// <see cref="ProjectileMath"/>'s homing path; this offset is applied only to the visible core.
    /// That separation keeps impact time, cue time, facing and collision exactly where combat expects them.
    /// </summary>
    public static class ProjectileVisualMath
    {
        const float Tau = Mathf.PI * 2f;

        /// <summary>A stable per-shot phase. No runtime randomness means captures and tests reproduce a bolt.</summary>
        public static float Phase(int shotId)
        {
            return Mathf.Repeat(shotId * 2.39996323f, Tau);
        }

        /// <summary>Only a child may leave the logical path. A legacy bolt with its renderer on the root stays straight.</summary>
        public static bool CanOffset(Transform logicalRoot, Transform candidate)
        {
            return logicalRoot != null && candidate != null && candidate != logicalRoot && candidate.IsChildOf(logicalRoot);
        }

        /// <summary>
        /// A bounded two-frequency weave perpendicular to travel. It eases in so the bolt does not pop
        /// sideways at the muzzle, then reaches zero before the parry cue begins. A reflected bolt returns
        /// zero immediately: the payoff flies straight back and cannot retain an incoming visual kink.
        /// </summary>
        public static Vector3 WeaveOffset(Vector3 direction, float age, float remainingSeconds,
                                          float cueLead, float phase, float amplitude,
                                          float fadeInSeconds, float fadeOutSeconds, bool incoming)
        {
            if (!incoming || amplitude <= 0f || remainingSeconds <= cueLead) return Vector3.zero;

            Vector3 forward = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(Vector3.forward, forward);
            side.Normalize();
            Vector3 rise = Vector3.Cross(forward, side).normalized;

            float fadeIn = Smooth01(age / Mathf.Max(0.001f, fadeInSeconds));
            float beforeCue = (remainingSeconds - cueLead) / Mathf.Max(0.001f, fadeOutSeconds);
            float fadeOut = Smooth01(beforeCue);
            float envelope = fadeIn * fadeOut;

            // The slower side-to-side bend establishes the curve; the smaller faster rise keeps successive
            // shots from reading like the same rail. Clamp the combined vector so amplitude is a hard ceiling.
            float sideWave = Mathf.Sin(age * Tau * 2.8f + phase);
            float riseWave = Mathf.Sin(age * Tau * 4.7f + phase * 1.618034f) * 0.55f;
            Vector3 offset = side * sideWave + rise * riseWave;
            if (offset.sqrMagnitude > 1f) offset.Normalize();
            return offset * (amplitude * envelope);
        }

        /// <summary>
        /// Records fixed-interval samples between two rendered positions. A low frame rate can cross more
        /// than one interval in a frame; interpolating each crossing keeps the history curved and prevents
        /// repeated points from silently shortening the trail. Slot zero is always the live head.
        /// </summary>
        public static void RecordTrail(Vector3 previousHead, Vector3 currentHead, float deltaTime,
                                       float sampleStep, ref float sampleTimer, Vector3[] history)
        {
            if (history == null || history.Length == 0) return;
            history[0] = currentHead;
            if (history.Length == 1 || deltaTime <= 0f || sampleStep <= 0f) return;

            float consumed = 0f;
            float sinceSample = Mathf.Clamp(sampleTimer, 0f, sampleStep);
            while (sinceSample + (deltaTime - consumed) >= sampleStep)
            {
                float toCrossing = sampleStep - sinceSample;
                consumed += toCrossing;
                float fraction = Mathf.Clamp01(consumed / deltaTime);
                Vector3 sample = Vector3.Lerp(previousHead, currentHead, fraction);
                for (int i = history.Length - 1; i >= 2; i--) history[i] = history[i - 1];
                history[1] = sample;
                sinceSample = 0f;
            }
            sampleTimer = sinceSample + Mathf.Max(0f, deltaTime - consumed);
        }

        static float Smooth01(float value)
        {
            float k = Mathf.Clamp01(value);
            return k * k * (3f - 2f * k);
        }
    }
}
