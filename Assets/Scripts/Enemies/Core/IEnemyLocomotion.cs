using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// How the enemy brain moves a body. The brain decides WHERE and HOW FAST; the implementation decides
    /// how that becomes motion — a NavMeshAgent today, a root-motion animated rig later.
    ///
    /// Nothing in here knows about meshes, animation or the NavMesh specifically. Swapping in an authored
    /// rig means writing one new implementation, not touching <see cref="EnemyController"/>.
    /// </summary>
    public interface IEnemyLocomotion
    {
        /// <summary>False while the body cannot be driven (not yet placed, disabled, off the navigable surface).</summary>
        bool IsReady { get; }

        /// <summary>One-time setup from <see cref="EnemyData"/>. Called before any movement.</summary>
        void Configure(float speed, float angularSpeed, float acceleration, float stoppingDistance, float radius, float height);

        /// <summary>Change top speed only (boss phase changes do this without a full reconfigure).</summary>
        void SetSpeed(float speed);

        /// <summary>Walk toward a world position, resuming movement if it was stopped.</summary>
        void MoveTo(Vector3 worldPosition);

        /// <summary>Hold position. Nudge and Strafe still work while stopped — they are deliberate steps, not pathing.</summary>
        void Stop();

        /// <summary>Rotate toward a direction, capped at the given rate. The brain owns the easing curve.</summary>
        void FaceTowards(Vector3 direction, float maxDegreesPerSecond);

        /// <summary>Sidestep around a point. <paramref name="dir"/> is +1 or -1.</summary>
        void Strafe(Vector3 around, int dir, float speed, float deltaTime);

        /// <summary>Translate by a delta this frame, still respecting the navigable surface (no walking off ledges).</summary>
        void Nudge(Vector3 delta);

        float DistanceTo(Vector3 worldPosition);

        /// <summary>World velocity the body is travelling its path at (zero when stopped). The brain faces it
        /// while path-following out of range, so a body never walks with a stale facing.</summary>
        Vector3 Velocity { get; }

        /// <summary>Stop driving the body entirely (death).</summary>
        void Disable();
    }
}
