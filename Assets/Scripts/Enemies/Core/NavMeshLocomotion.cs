using UnityEngine;
using UnityEngine.AI;

namespace VibeGame1
{
    /// <summary>
    /// The NavMeshAgent implementation of <see cref="IEnemyLocomotion"/> — the primitive-enemy default.
    /// Behaviour is a straight lift of what <see cref="EnemyController"/> used to do inline, including
    /// every isOnNavMesh guard, so this refactor is behaviour-neutral.
    ///
    /// A future animated enemy supplies a different implementation (root motion driven by an Animator)
    /// and the brain does not change.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class NavMeshLocomotion : MonoBehaviour, IEnemyLocomotion
    {
        NavMeshAgent agent;

        void Awake() => agent = GetComponent<NavMeshAgent>();

        /// <summary>Every call guards on this: an agent off the mesh throws rather than no-opping.</summary>
        public bool IsReady => agent != null && agent.enabled && agent.isOnNavMesh;

        public void Configure(float speed, float angularSpeed, float acceleration, float stoppingDistance, float radius, float height)
        {
            if (agent == null) return;
            agent.speed = speed;
            agent.angularSpeed = angularSpeed;
            agent.acceleration = acceleration;
            agent.stoppingDistance = stoppingDistance;
            agent.radius = radius;
            agent.height = height;
            // The BRAIN owns facing, not the agent. With updateRotation left on, the agent also spins the
            // transform toward its own velocity while chasing, so an enemy strafing around the player
            // snapped to face its path instead of easing toward the player — that double-authority is a
            // large part of what read as "unnaturally darting around". One writer per transform channel.
            agent.updateRotation = false;
            agent.updateUpAxis = false;
        }

        public void SetSpeed(float speed)
        {
            if (agent != null) agent.speed = speed;
        }

        public void MoveTo(Vector3 worldPosition)
        {
            if (!IsReady) return;
            agent.isStopped = false;
            agent.SetDestination(worldPosition);
        }

        public void Stop()
        {
            if (!IsReady) return;
            agent.isStopped = true;
        }

        public void FaceTowards(Vector3 direction, float maxDegreesPerSecond)
        {
            if (direction.sqrMagnitude < 0.0001f) return;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            var target = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, maxDegreesPerSecond * Time.deltaTime);
        }

        public void Strafe(Vector3 around, int dir, float speed, float deltaTime)
        {
            if (!IsReady || speed <= 0.001f) return;
            Vector3 toCentre = around - transform.position;
            toCentre.y = 0f;
            if (toCentre.sqrMagnitude < 0.0001f) return;
            Vector3 tangent = Vector3.Cross(Vector3.up, toCentre.normalized) * Mathf.Sign(dir);
            agent.Move(tangent * speed * deltaTime);
        }

        public void Nudge(Vector3 delta)
        {
            // agent.Move rather than a destination: a deliberate step that still clamps to the navigable
            // surface, and does not disturb the stopped agent's path.
            if (!IsReady) return;
            agent.Move(delta);
        }

        public float DistanceTo(Vector3 worldPosition) => Vector3.Distance(transform.position, worldPosition);

        public Vector3 Velocity => IsReady ? agent.velocity : Vector3.zero;

        public void Disable()
        {
            if (agent != null && agent.enabled) agent.enabled = false;
        }
    }
}
