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
            return Evaluate(elapsedSincePress, perfectWindow, lateWindow, facing, unblockable, false);
        }

        /// <summary>
        /// Full rule, including the HELD guard (Sekiro stance).
        ///
        /// <para>The guard is resolved strictly AFTER the timing, never instead of it: the windows are
        /// evaluated first and only a result that came out <see cref="ParryResult.Hit"/> is upgraded to
        /// <see cref="ParryResult.Blocked"/>. That ordering is the whole reason the timing game survives
        /// — holding can never produce a Perfect, so a deflect is always strictly better than a stance,
        /// and the stance is only ever the floor under a press that missed.</para>
        ///
        /// <para>The guard obeys the same two vetoes as a press: it cannot protect your back
        /// (<paramref name="facing"/>) and it cannot answer an <paramref name="unblockable"/>. The pink
        /// alert tell means "this one cannot be answered with steel"; a guard that ate it would make the
        /// loudest signal in the game a lie.</para>
        /// </summary>
        public static ParryResult Evaluate(float elapsedSincePress, float perfectWindow, float lateWindow,
                                           bool facing, bool unblockable, bool guarding)
        {
            if (!facing || unblockable) return ParryResult.Hit;
            // Epsilon so a press landing exactly on a window boundary resolves in the PLAYER's favour.
            // Without it the comparison is float-fragile: the caller's `perfect + late` and ours can
            // differ in the last bit (constant folding vs runtime addition), which silently turned an
            // exact-boundary deflect into a full hit.
            const float e = 1e-4f;
            if (elapsedSincePress >= 0f)
            {
                if (elapsedSincePress <= perfectWindow + e) return ParryResult.Perfect;
                if (elapsedSincePress <= perfectWindow + lateWindow + e) return ParryResult.Blocked;
            }
            return guarding ? ParryResult.Blocked : ParryResult.Hit;
        }
    }
}
