using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The one place anything world-space asks "where is the player's eye?".
    ///
    /// Three presentation components — <see cref="EnemyPostureBar"/>, <see cref="DeathblowMarker"/> and
    /// <see cref="LockOnMarker"/> — each cached <c>Camera.main.transform</c> themselves, with their own
    /// null guard and their own re-lookup. That is the part worth sharing.
    ///
    /// What is deliberately NOT shared is how they face the camera: the posture bar stays upright and
    /// yaws only, the deathblow mark billboards then rolls about the view axis, and the lock-on dot
    /// billboards on all three axes. Those are three different reads, not three copies of one — folding
    /// them into a base class with a mode enum would hide that rather than simplify it.
    /// </summary>
    public static class ViewCamera
    {
        static Transform cached;

        /// <summary>
        /// The player camera's transform, or null if there is none this frame. Re-resolves whenever the
        /// cache is empty OR has been destroyed — the latter matters because a scene load leaves a
        /// non-null but dead reference behind, and Unity's fake-null makes <c>!= null</c> the only
        /// check that catches it.
        /// </summary>
        public static Transform Transform
        {
            get
            {
                if (cached != null) return cached;
                var c = Camera.main;
                cached = c != null ? c.transform : null;
                return cached;
            }
        }

        /// <summary>Position of the player's eye, or <paramref name="fallback"/> when there is no camera.</summary>
        public static Vector3 EyeOr(Vector3 fallback)
        {
            var t = Transform;
            return t != null ? t.position : fallback;
        }

        /// <summary>Drop the cache. Call on scene teardown if a stale reference is ever suspected.</summary>
        public static void Forget() { cached = null; }
    }
}
