using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Entering the arena closes the gate and wakes the boss.</summary>
    [RequireComponent(typeof(Collider))]
    public class BossArenaTrigger : MonoBehaviour
    {
        public Transform gate;
        public Vector3 gateOpenPosition;
        public Vector3 gateClosedPosition;

        bool triggered;
        Coroutine move;

        void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            if (gate != null) gate.position = gateOpenPosition;
        }

        void OnTriggerEnter(Collider other)
        {
            if (triggered) return;
            if (other.GetComponentInParent<PlayerCombat>() == null) return;
            triggered = true;
            var boss = FindAnyObjectByType<BossController>();
            if (boss != null) boss.Activate();
            if (gate != null) { if (move != null) StopCoroutine(move); move = StartCoroutine(MoveGate(gateClosedPosition, 0.6f)); }
            if (CameraShake.I) CameraShake.I.Medium();
        }

        public void ResetArena()
        {
            triggered = false;
            if (gate != null) { if (move != null) StopCoroutine(move); gate.position = gateOpenPosition; }
        }

        IEnumerator MoveGate(Vector3 target, float seconds)
        {
            Vector3 from = gate.position;
            float t = 0f;
            while (t < seconds)
            {
                gate.position = Vector3.Lerp(from, target, t / seconds);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            gate.position = target;
        }
    }
}
