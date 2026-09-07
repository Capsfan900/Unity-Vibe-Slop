using UnityEngine;

namespace VibeGame1
{
    /// <summary>The inner half of a mini-boss realm's return portal.</summary>
    [RequireComponent(typeof(Collider))]
    public class SolarRealmExitTrigger : MonoBehaviour
    {
        public SolarArenaPortal portal;

        void Awake() { GetComponent<Collider>().isTrigger = true; }

        void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<PlayerCombat>();
            if (player != null && portal != null) portal.Exit(player);
        }
    }
}
