using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Pure locomotion-clip choice for <see cref="PuppetVisuals"/> (2026-09-13, the user: "the running
    /// freezes and he just glides around not animating"). Kept out of the MonoBehaviour so the dead bands
    /// and the rate law are provable in EditMode.
    /// </summary>
    public static class PuppetLocomotion
    {
        public const int Idle = 0, Walk = 1, Run = 2;

        /// <summary>Metres per second. Separate enter/leave thresholds: a body whose step-in speed sits on a
        /// single threshold used to flip Walk/Run every few frames, and every flip restarted the clip.</summary>
        public const float RunEnter = 2.9f, RunExit = 2.3f, WalkEnter = 0.45f, WalkExit = 0.25f;

        /// <summary>Clamp on the speed-matched playback rate: beyond it a stride reads as a sprint or a crawl.</summary>
        public const float MinRate = 0.6f, MaxRate = 1.6f;

        /// <summary>The loop the body should show at <paramref name="speed"/>, given the one it shows now.</summary>
        public static int Choose(float speed, int current)
        {
            switch (current)
            {
                case Run:
                    if (speed >= RunExit) return Run;
                    return speed >= WalkExit ? Walk : Idle;
                case Walk:
                    if (speed > RunEnter) return Run;
                    return speed >= WalkExit ? Walk : Idle;
                default:
                    if (speed > RunEnter) return Run;
                    return speed > WalkEnter ? Walk : Idle;
            }
        }

        /// <summary>Animator speed that makes the clip's stride match the body's travel. A stride speed of 0
        /// (not baked) keeps the authored rate.</summary>
        public static float Rate(float speed, float strideSpeed)
        {
            if (strideSpeed <= 0.01f) return 1f;
            return Mathf.Clamp(speed / strideSpeed, MinRate, MaxRate);
        }

        /// <summary>True when a looping clip's normalized time crossed a footfall (0.0 or 0.5) this frame.</summary>
        public static bool CrossedFootfall(float previousNormalized, float normalized)
        {
            if (normalized < previousNormalized) return true;              // wrapped past 0 (or restarted)
            float a = previousNormalized % 1f, b = normalized % 1f;
            if (b < a) return true;                                        // crossed an integer
            return a < 0.5f && b >= 0.5f;
        }
    }
}
