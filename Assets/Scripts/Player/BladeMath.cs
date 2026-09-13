using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Pure math for the BLADE THROW book spell (2026-09-13, the user: "tomahawk throw the sword and then
    /// teleport to it, like the flare ... at any point in its flight"). No Unity objects, so every number
    /// the throw ships with is provable in EditMode.
    /// </summary>
    public static class BladeMath
    {
        /// <summary>World position after <paramref name="t"/> seconds of a gravity arc.</summary>
        public static Vector3 Position(Vector3 origin, Vector3 launchVelocity, float gravity, float t)
        {
            return origin + launchVelocity * t + Vector3.down * (0.5f * gravity * t * t);
        }

        public static Vector3 Velocity(Vector3 launchVelocity, float gravity, float t)
        {
            return launchVelocity + Vector3.down * (gravity * t);
        }

        /// <summary>Upper bound on how far a throw can travel before it returns: speed x flight time.
        /// Gravity only ever shortens the real path's displacement.</summary>
        public static float MaxReach(float speed, float flightSeconds)
        {
            return Mathf.Max(0f, speed) * Mathf.Max(0f, flightSeconds);
        }

        /// <summary>May the player recall to the blade right now? In flight for its whole flight time, and
        /// once lodged for <paramref name="lodgeSeconds"/>. A spent blade never.</summary>
        public static bool Recallable(bool spent, bool lodged, float flightAge, float flightSeconds, float lodgeAge, float lodgeSeconds)
        {
            if (spent) return false;
            if (lodged) return lodgeAge >= 0f && lodgeAge < lodgeSeconds;
            return flightAge >= 0f && flightAge < flightSeconds;
        }

        /// <summary>Tomahawk tumble about the blade's own right axis, degrees.</summary>
        public static float SpinAngle(float t, float degreesPerSecond)
        {
            return Mathf.Repeat(t * degreesPerSecond, 360f);
        }

        /// <summary>
        /// Where the player's FEET should be pulled so the body arrives at the blade without ending inside
        /// the surface it hit. The player transform is at the feet with the eye ~1.6 m above. A floor lodge
        /// stands on the blade; a wall or ceiling lodge stands the body off along the surface normal and
        /// drops it so the chest, not the feet, meets the blade. In flight (no surface) the chest meets it.
        /// </summary>
        public static Vector3 PullTarget(Vector3 bladePoint, Vector3 surfaceNormal, bool hasSurface, float standoff)
        {
            const float chest = 1.0f;
            if (!hasSurface || surfaceNormal.sqrMagnitude < 1e-6f) return bladePoint + Vector3.down * chest;
            Vector3 n = surfaceNormal.normalized;
            if (n.y > 0.7f) return bladePoint + Vector3.up * 0.05f;
            return bladePoint + n * Mathf.Max(0f, standoff) + Vector3.down * chest;
        }
    }
}
