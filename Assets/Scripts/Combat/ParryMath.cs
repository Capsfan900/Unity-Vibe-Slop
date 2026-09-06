using UnityEngine;

namespace VibeGame1
{
    /// <summary>Pure parry timing rules. Unit tested in Assets/Editor/Tests.</summary>
    public static class ParryMath
    {
        /// <summary>
        /// Where the attack comes FROM, flat, as seen from the player. An attack that travels
        /// (<paramref name="incomingDirection"/> non-zero: a bolt) comes from against its travel; one
        /// that does not (a swing) comes from the attacker's position. Melee callers pass zero and get
        /// exactly the old bearing, so the duel is untouched.
        /// </summary>
        public static Vector3 SourceDirection(Vector3 incomingDirection, Vector3 attackerPosition, Vector3 playerPosition)
        {
            Vector3 d = new Vector3(incomingDirection.x, 0f, incomingDirection.z);
            if (d.sqrMagnitude > 1e-6f) return -d;
            Vector3 to = attackerPosition - playerPosition; to.y = 0f;
            return to;
        }

        /// <summary>The facing veto: the source lies inside <paramref name="coneDeg"/> of the flat forward. A degenerate source (on top of you) is facing.</summary>
        public static bool IsFacing(Vector3 forward, Vector3 sourceDirection, float coneDeg)
        {
            Vector3 f = new Vector3(forward.x, 0f, forward.z);
            Vector3 s = new Vector3(sourceDirection.x, 0f, sourceDirection.z);
            if (s.sqrMagnitude < 0.01f || f.sqrMagnitude < 1e-6f) return true;
            return Vector3.Angle(f, s) <= coneDeg;
        }

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
