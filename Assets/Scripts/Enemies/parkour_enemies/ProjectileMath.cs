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

        /// <summary>Unit direction from a point back to the shooter's chest, or forward when degenerate.</summary>
        public static Vector3 ReflectDirection(Vector3 from, Vector3 shooterChest, Vector3 fallback)
        {
            Vector3 d = shooterChest - from;
            return d.sqrMagnitude < 1e-6f ? fallback.normalized : d.normalized;
        }
    }
}
