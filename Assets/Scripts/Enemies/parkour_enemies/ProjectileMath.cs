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
}
