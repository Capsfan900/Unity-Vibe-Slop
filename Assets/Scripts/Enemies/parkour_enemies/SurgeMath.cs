using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The arithmetic of the PARRY SURGE -- the speed that eligible Perfects pay out, including the
    /// dedicated <c>pshooter_enemy03</c> turret row --
    /// with no Unity objects in it (the <see cref="ProjectileMath"/> / <see cref="FlareMath"/> arrangement):
    /// <see cref="ParrySurge"/> is the thin layer that hands these numbers to
    /// <c>FirstPersonMotor.SpeedMultiplier</c>, and <c>SurgeTurretTests</c> drives the maths.
    ///
    /// <para><b>The shape, and why it is this shape.</b> A parry adds ONE stack, capped. Stacks fall off
    /// ONE AT A TIME, one every <c>stackSeconds</c> of not parrying, never all at once: a cliff from x1.60
    /// straight back to x1.00 is both unreadable (the HUD strip jumps a whole number in a frame) and
    /// unforgiving (one missed turret erases the whole ramp). Walking down in steps means the multiplier
    /// is always heading to exactly 1 on its own, which is what makes a STUCK multiplier -- the worst bug
    /// this feature could have -- impossible by construction rather than by cleanup.</para>
    /// </summary>
    public static class SurgeMath
    {
        /// <summary>One more stack for one parry, never past the ceiling. Negative inputs clamp to the floor.</summary>
        public static int Grant(int stacks, int maxStacks)
        {
            if (maxStacks <= 0) return 0;
            return Mathf.Clamp(stacks + 1, 0, maxStacks);
        }

        /// <summary>
        /// The multiplier a stack count is worth: 1 + stacks x step. 1 exactly at zero stacks, so the
        /// motor's shipped speeds are what an unsurged player gets, bit for bit.
        /// </summary>
        public static float Multiplier(int stacks, float stepPerStack)
        {
            if (stacks <= 0 || stepPerStack <= 0f) return 1f;
            return 1f + stacks * stepPerStack;
        }

        /// <summary>The ceiling the whole feature can ever reach. Tests pin this against the level's arcs.</summary>
        public static float MaxMultiplier(int maxStacks, float stepPerStack)
        {
            return Multiplier(Mathf.Max(0, maxStacks), stepPerStack);
        }

        /// <summary>When the next stack falls, given a parry (or a drop) at <paramref name="now"/>.</summary>
        public static float NextDropTime(float now, float stackSeconds)
        {
            return now + Mathf.Max(0.05f, stackSeconds);
        }

        /// <summary>Is a stack due to fall this frame. False at zero stacks, so an idle surge does nothing.</summary>
        public static bool DropDue(int stacks, float now, float nextDropAt)
        {
            return stacks > 0 && now >= nextDropAt;
        }

        /// <summary>One stack gone, never under zero.</summary>
        public static int Drop(int stacks)
        {
            return Mathf.Max(0, stacks - 1);
        }

        /// <summary>
        /// Seconds a full stack of surge survives with no further parries: the whole ladder walked down one
        /// step at a time. This is the number to reason about when asking "does the boost leak into the next
        /// span", not <c>stackSeconds</c> on its own.
        /// </summary>
        public static float FullDecaySeconds(int maxStacks, float stackSeconds)
        {
            return Mathf.Max(0, maxStacks) * Mathf.Max(0.05f, stackSeconds);
        }
    }
}
