using UnityEngine;

namespace VibeGame1
{
    /// <summary>Pure posture rules. Unit tested in Assets/Editor/Tests.</summary>
    public static class PostureMath
    {
        /// <returns>New posture value and whether the bar just filled (break).</returns>
        public static (float value, bool broke) Apply(float current, float max, float amount)
        {
            float v = Mathf.Clamp(current + amount, 0f, max);
            return (v, v >= max && amount > 0f);
        }

        public static float Regen(float current, float ratePerSecond, float dt)
        {
            return Mathf.Max(0f, current - ratePerSecond * dt);
        }
    }
}
