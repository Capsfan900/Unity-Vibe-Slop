using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A gated arena. Entering closes the gate behind you and starts the fight; the way onward opens
    /// again only when the fight is won.
    ///
    /// One component serves both roles, because they are the same mechanism:
    ///
    /// * <b>Boss arena</b> — <see cref="clearSpawner"/> null, <see cref="exitGate"/> null. Entering wakes
    ///   the <see cref="BossController"/> and the gate never reopens: the run ends here.
    /// * <b>Mini-boss arena</b> — <see cref="clearSpawner"/> points at the legendary's spawner and
    ///   <see cref="exitGate"/> at the slab sealing the exit. The exit gate rests CLOSED and drops when
    ///   that spawner's enemy dies. This is the whole tile-to-tile progression: three of these in a row,
    ///   then the boss.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BossArenaTrigger : MonoBehaviour
    {
        [Header("Entry gate — rests OPEN (sunk), rises to seal you in")]
        public Transform gate;
        public Vector3 gateOpenPosition;
        public Vector3 gateClosedPosition;

        [Header("Mini-boss arena (leave null for the boss arena)")]
        [Tooltip("The legendary's spawner. Its enemy must die before the exit opens.")]
        public EnemySpawner clearSpawner;

        [Tooltip("Optional duo partner fought at the same time. When set, both enemies must die.")]
        public EnemySpawner partnerSpawner;

        [Tooltip("Rests CLOSED (up) — the inverse of the entry gate — and drops when the arena is cleared.")]
        public Transform exitGate;
        public Vector3 exitGateClosedPosition;
        public Vector3 exitGateOpenPosition;

        [Tooltip("Optional same-scene solar portal layered over this arena. ResetArena resets it too.")]
        public SolarArenaPortal solarPortal;

        bool triggered;
        bool cleared;
        bool sawAlive, sawPartnerAlive;
        Coroutine move, exitMove;

        /// <summary>True once the arena's enemy is dead and the way onward is open.</summary>
        public bool Cleared { get { return cleared; } }

        void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            if (gate != null) gate.position = gateOpenPosition;
            if (exitGate != null) exitGate.position = exitGateClosedPosition;
        }

        void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<PlayerCombat>();
            if (player == null) return;
            BeginFight(player);
        }

        /// <summary>
        /// Starts this arena through the same path as a physical trigger entry. Solar portals call this
        /// before teleporting so gate closure, the boss wake-up and the existing seen-alive latch remain
        /// the single fight lifecycle. Returns false only when the caller is not a player.
        /// </summary>
        public bool BeginFight(PlayerCombat player)
        {
            if (player == null) return false;
            if (triggered) return true;
            triggered = true;

            // Boss arenas wake a sleeping boss. Mini-bosses are ordinary enemies and are already awake;
            // waking a BossController here would activate the level's real boss from a mini-boss arena.
            if (clearSpawner == null)
            {
                var boss = FindAnyObjectByType<BossController>();
                if (boss != null) boss.Activate();
            }

            if (gate != null) { if (move != null) StopCoroutine(move); move = StartCoroutine(MoveGate(gate, gateClosedPosition, 0.6f)); }
            if (CameraShake.I) CameraShake.I.Medium();
            return true;
        }

        void Update()
        {
            if (!triggered || cleared || clearSpawner == null) return;
            // Evaluate BOTH every frame: a short-circuit left the partner's seen-alive latch unset while
            // the main enemy lived, so a partner killed first (and destroyed 1.5 s later) never counted.
            bool mainDead = IsSpawnDead(clearSpawner, ref sawAlive);
            bool partnerDead = partnerSpawner == null || IsSpawnDead(partnerSpawner, ref sawPartnerAlive);
            if (!mainDead || !partnerDead) return;

            cleared = true;
            // Both gates drop: the seal is broken, not merely a door unlocked.
            if (exitGate != null) { if (exitMove != null) StopCoroutine(exitMove); exitMove = StartCoroutine(MoveGate(exitGate, exitGateOpenPosition, 0.6f)); }
            if (gate != null) { if (move != null) StopCoroutine(move); move = StartCoroutine(MoveGate(gate, gateOpenPosition, 0.6f)); }
            if (CameraShake.I) CameraShake.I.Medium();
            AudioManager.Play(Sfx.Checkpoint);
        }

        /// <summary>
        /// The spawner's instance is destroyed 1.5 s after death, so "gone" also counts as dead — but only
        /// once we have SEEN it alive. Without that latch the arena reports itself cleared on the frame
        /// before LevelManager.SpawnAll() has run, and the exit gate would already be down at level start.
        /// </summary>
        static bool IsSpawnDead(EnemySpawner spawner, ref bool seen)
        {
            var inst = spawner.Instance;
            if (!seen)
            {
                if (inst != null) seen = true;
                return false;
            }
            if (inst == null) return true;
            var h = inst.GetComponentInChildren<Health>();
            return h != null && h.IsDead;
        }

        public void ResetArena()
        {
            triggered = false;
            cleared = false;
            sawAlive = false;
            sawPartnerAlive = false;
            if (gate != null) { if (move != null) StopCoroutine(move); move = null; gate.position = gateOpenPosition; }
            if (exitGate != null) { if (exitMove != null) StopCoroutine(exitMove); exitMove = null; exitGate.position = exitGateClosedPosition; }
            if (solarPortal != null) solarPortal.ResetPortal();
        }

        IEnumerator MoveGate(Transform g, Vector3 target, float seconds)
        {
            Vector3 from = g.position;
            float t = 0f;
            while (t < seconds)
            {
                g.position = Vector3.Lerp(from, target, t / seconds);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            g.position = target;
        }
    }
}
