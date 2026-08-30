using UnityEngine;

namespace VibeGame1
{
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        public Transform spawnPoint;
        [ColorUsage(true, true)] public Color activeEmission = new Color(0.878f, 0.627f, 0.188f) * 3f; // ember gold #E0A030
        public bool Activated { get; private set; }

        EmissiveFlash flash;

        void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            flash = GetComponentInChildren<EmissiveFlash>();
            if (spawnPoint == null) spawnPoint = transform;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerCombat>() == null) return;
            if (LevelManager.I != null) LevelManager.I.SetCheckpoint(this);
        }

        public void Activate()
        {
            Activated = true;
            if (flash != null) { flash.SetBase(activeEmission); flash.Flash(Color.white * 8f, 0.4f); }
        }
    }
}
