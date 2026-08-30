using UnityEngine;

namespace VibeGame1
{
    /// <summary>Dropped souls. Walk into it to recover them.</summary>
    public class Bloodstain : MonoBehaviour
    {
        public int amount;
        public Transform visual;

        void Update()
        {
            if (visual != null)
            {
                visual.Rotate(0f, 90f * Time.unscaledDeltaTime, 0f, Space.World);
                visual.localPosition = Vector3.up * (0.6f + Mathf.Sin(Time.unscaledTime * 2f) * 0.15f);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerCombat>() == null) return;
            if (SoulsWallet.I != null) SoulsWallet.I.Add(amount);
            AudioManager.Play(Sfx.Souls, 1f, 1.2f);
            if (ScreenFlash.I) ScreenFlash.I.Flash(new Color(0.4f, 1f, 0.7f), 0.2f, 0.3f);
            Destroy(gameObject);
        }
    }
}
