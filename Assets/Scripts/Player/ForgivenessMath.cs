using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The MATHS of the two forgiveness rules (MOVEMENT-PRINCIPLES rule 4: fudge toward intent), with no
    /// Unity objects in it so the laws can be unit-tested at any frame rate. <see cref="FirstPersonMotor"/>
    /// is the thin layer that hands these numbers the physics probes' answers.
    ///
    /// <para><b>Corner correction.</b> A rising jump whose head clips the CORNER of a ledge by a few
    /// centimetres is nudged sideways so the jump continues, instead of stopping dead on the pixel.
    /// The nudge is bounded by the margin: it honours what the player meant, it never adds reach.</para>
    ///
    /// <para><b>Near-miss landing.</b> A falling player whose feet pass just under a ledge top they are
    /// moving toward is lifted onto it. Bounded upward nudge, no velocity added, never on a wall being
    /// fallen past, never while rising, sliding or wall running.</para>
    /// </summary>
    public static class ForgivenessMath
    {
        /// <summary>
        /// Should a corner clip be corrected? True when the blocked probe cleared inside the margin:
        /// <paramref name="clearOffset"/> is how far sideways a clear capsule was found (0 = none).
        /// </summary>
        public static bool CornerCorrects(float clearOffset, float margin, float verticalSpeed)
        {
            return verticalSpeed > 0f && margin > 0f && clearOffset > 0f && clearOffset <= margin + 1e-4f;
        }

        /// <summary>
        /// Should a near-miss landing catch? The ledge top must be at or above the feet by no more than
        /// <paramref name="catchMetres"/>, the player must be falling, and the surface must be a floor
        /// (normal.y above <paramref name="floorNormalMin"/>) — a wall side never catches.
        /// </summary>
        public static bool LedgeCatches(float feetY, float ledgeTopY, float catchMetres, float verticalSpeed,
                                        float surfaceNormalY, float floorNormalMin, float horizontalSpeed, float minHorizontalSpeed)
        {
            if (catchMetres <= 0f || verticalSpeed >= 0f) return false;
            if (surfaceNormalY < floorNormalMin) return false;
            if (horizontalSpeed < minHorizontalSpeed) return false;
            float gap = ledgeTopY - feetY;
            return gap >= 0f && gap <= catchMetres;
        }

        /// <summary>
        /// One frame of a bounded upward nudge toward <paramref name="remaining"/> metres, capped at
        /// <paramref name="metresPerSecond"/> × dt so the lift is the same at 20 and 240 fps.
        /// </summary>
        public static float NudgeStep(float remaining, float metresPerSecond, float dt)
        {
            if (remaining <= 0f || dt <= 0f) return 0f;
            return Mathf.Min(remaining, metresPerSecond * dt);
        }
    }
}
