using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The arithmetic of a sentry FLARE (2026-09-06), with no Unity objects in it: where it is at time t,
    /// how bright it still is, and what it launches with. <see cref="SentryFlare"/> hands these to a
    /// transform; <c>FlareTests</c> drives the numbers.
    /// </summary>
    public static class FlareMath
    {
        /// <summary>
        /// Launch velocity: <paramref name="upSpeed"/> straight up plus <paramref name="outSpeed"/> along the
        /// flat direction the sentry was FACING (toward the route it covered), so the flare drifts over the
        /// span the player is running rather than back over the perch. Degenerate facing = straight up.
        /// </summary>
        public static Vector3 LaunchVelocity(Vector3 facing, float upSpeed, float outSpeed)
        {
            Vector3 f = new Vector3(facing.x, 0f, facing.z);
            Vector3 v = Vector3.up * upSpeed;
            if (f.sqrMagnitude > 1e-6f) v += f.normalized * outSpeed;
            return v;
        }

        /// <summary>A low-gravity arc: floats up, hangs, sinks. Gravity is positive metres/s^2 downward.</summary>
        public static Vector3 Position(Vector3 origin, Vector3 velocity, float gravity, float t)
        {
            return origin + velocity * t + Vector3.down * (0.5f * gravity * t * t);
        }

        /// <summary>1 at birth, 0 at <paramref name="life"/>: the glow, and the grapple's legality.</summary>
        public static float Glow(float t, float life)
        {
            if (life <= 0f) return 0f;
            return Mathf.Clamp01(1f - t / life);
        }

        /// <summary>The flare can still be grappled: alive, and glowing above the floor a runner can read.</summary>
        public static bool Grappleable(float t, float life, float minGlow)
        {
            return t >= 0f && Glow(t, life) > minGlow;
        }
    }
}
