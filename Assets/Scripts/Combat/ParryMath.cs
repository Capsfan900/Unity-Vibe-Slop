namespace VibeGame1
{
    /// <summary>Pure parry timing rules. Unit tested in Assets/Editor/Tests.</summary>
    public static class ParryMath
    {
        /// <param name="elapsedSincePress">Seconds between the parry press and the attack impact.</param>
        /// <param name="perfectWindow">Perfect window length starting at the press.</param>
        /// <param name="lateWindow">Block window length following the perfect window.</param>
        public static ParryResult Evaluate(float elapsedSincePress, float perfectWindow, float lateWindow, bool facing, bool unblockable)
        {
            if (!facing || unblockable) return ParryResult.Hit;
            if (elapsedSincePress < 0f) return ParryResult.Hit;
            // Epsilon so a press landing exactly on a window boundary resolves in the PLAYER's favour.
            // Without it the comparison is float-fragile: the caller's `perfect + late` and ours can
            // differ in the last bit (constant folding vs runtime addition), which silently turned an
            // exact-boundary deflect into a full hit.
            const float e = 1e-4f;
            if (elapsedSincePress <= perfectWindow + e) return ParryResult.Perfect;
            if (elapsedSincePress <= perfectWindow + lateWindow + e) return ParryResult.Blocked;
            return ParryResult.Hit;
        }
    }
}
