using UnityEngine;

namespace VibeGame1
{
    [CreateAssetMenu(menuName = "VibeGame1/Game Feel")]
    public class GameFeelSettings : ScriptableObject
    {
        [Header("Hitstop (realtime seconds)")]
        public float parryHitStop = 0.09f;
        public float executeHitStop = 0.14f;
        public float hitStopScale = 0.02f;

        [Header("Camera shake (amplitude, seconds)")]
        public float shakeSmallAmp = 0.06f, shakeSmallTime = 0.12f;
        public float shakeMedAmp = 0.14f, shakeMedTime = 0.2f;
        public float shakeBigAmp = 0.3f, shakeBigTime = 0.35f;

        [Header("Screen flash")]
        public Color parryFlash = new Color(0.8f, 0.9f, 1f, 1f);   // pale steel
        public float parryFlashAlpha = 0.35f;
        public Color hurtFlash = new Color(0.6f, 0f, 0.05f, 1f);   // dark blood
        public float hurtFlashAlpha = 0.4f;
        public Color ultFlash = new Color(1f, 0.5f, 0.15f, 1f);    // ember

        [Header("Post FX pulses")]
        public float parryChromatic = 0.6f;
        public float parryChromaticTime = 0.25f;
        public float dashFovKick = 8f;
        public float ultFovKick = 15f;
    }
}
