using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// THE PARRY SURGE (2026-09-06): the speed a row of <c>pshooter_enemy03</c> turrets pays out. It lives
    /// on the PLAYER and is the only thing in the game that drives
    /// <see cref="FirstPersonMotor.SpeedMultiplier"/> -- the existing item-speed hook (FirstPersonMotor.cs:301),
    /// which nothing shipped had ever driven and which <see cref="StatusStripView"/> already renders as
    /// "SPEED x1.50". No new motor entry point, no written velocity, no new resource, no new HUD element.
    ///
    /// <para><b>It is added at runtime, never authored.</b> <see cref="Grant"/> finds-or-adds it on the
    /// player object the first time a turret's bolt is deflected, exactly the way <see cref="SentryFlare"/>
    /// spawns itself. The Player prefab is untouched, so a level with no turrets in it carries nothing.</para>
    ///
    /// <para><b>Restoring 1 is the whole safety story.</b> A stuck multiplier would be the worst bug this
    /// feature could have, so it can go wrong in four places and all four are covered: stacks walk DOWN to
    /// zero on their own (<see cref="SurgeMath"/>), death and respawn clear instantly
    /// (<c>GameEvents.PlayerDied</c> / <c>PlayerRespawned</c>), and a level reload or any teardown that
    /// disables or destroys the player restores 1 in <c>OnDisable</c>. The component never writes the
    /// multiplier at all while it holds no stacks, so it cannot fight an item that takes the hook later.</para>
    ///
    /// <para>Every number is on the TURRET'S DATA (<see cref="EnemyData.parrySurgeStep"/> and friends,
    /// written by DataFactory, rule 9); this component carries no tuning of its own.</para>
    /// </summary>
    public class ParrySurge : MonoBehaviour
    {
        FirstPersonMotor motor;
        int stacks;
        float step;
        int maxStacks;
        float stackSeconds;
        float nextDropAt;
        float clock;

        /// <summary>Stacks held right now. Tests and the harness.</summary>
        public int Stacks { get { return stacks; } }
        /// <summary>The multiplier this component is currently asking the motor for.</summary>
        public float Multiplier { get { return SurgeMath.Multiplier(stacks, step); } }
        /// <summary>Seconds until the next stack falls. 0 when there is nothing to lose.</summary>
        public float SecondsToNextDrop { get { return stacks <= 0 ? 0f : Mathf.Max(0f, nextDropAt - clock); } }

        /// <summary>
        /// One deflected turret bolt. Finds or adds the component on the player, adds a stack and pushes
        /// the new multiplier. Returns the surge so a test can read it; null when there is no motor.
        /// </summary>
        public static ParrySurge Grant(FirstPersonMotor onMotor, float stepPerStack, int maxStacks, float stackSeconds)
        {
            if (onMotor == null || stepPerStack <= 0f || maxStacks <= 0) return null;
            var s = onMotor.GetComponent<ParrySurge>();
            if (s == null) s = onMotor.gameObject.AddComponent<ParrySurge>();
            s.motor = onMotor;
            // The LAST turret to be parried owns the tuning. In practice a row is one enemy type, and a
            // mixed row should feel like the thing you just hit, not like the thing you hit a minute ago.
            s.step = stepPerStack; s.maxStacks = maxStacks; s.stackSeconds = stackSeconds;
            s.stacks = SurgeMath.Grant(s.stacks, maxStacks);
            s.nextDropAt = SurgeMath.NextDropTime(s.clock, stackSeconds);
            s.Push();
            return s;
        }

        void Awake()
        {
            if (motor == null) motor = GetComponent<FirstPersonMotor>();
        }

        void OnEnable()
        {
            GameEvents.PlayerDied += Clear;
            GameEvents.PlayerRespawned += Clear;
        }

        void OnDisable()
        {
            GameEvents.PlayerDied -= Clear;
            GameEvents.PlayerRespawned -= Clear;
            // Disabled, destroyed, or the scene torn down for a reload: the motor goes back to its shipped
            // speeds. This is the last line of defence and it is unconditional on purpose.
            Clear();
        }

        /// <summary>Every stack gone and the motor restored to 1. Public so the harness can prove it.</summary>
        public void Clear()
        {
            stacks = 0;
            if (motor != null) motor.SpeedMultiplier = 1f;
        }

        void Update()
        {
            // The player's own clock (hard rule 1): a hitstop freezes the world, never the surge the player
            // is riding, and never the timer that takes it away.
            clock += TimeScaleController.PlayerDelta;
            if (stacks <= 0) return;
            if (SurgeMath.DropDue(stacks, clock, nextDropAt))
            {
                stacks = SurgeMath.Drop(stacks);
                nextDropAt = SurgeMath.NextDropTime(clock, stackSeconds);
                Push();
            }
        }

        void Push()
        {
            if (motor == null) motor = GetComponent<FirstPersonMotor>();
            if (motor != null) motor.SpeedMultiplier = SurgeMath.Multiplier(stacks, step);
        }
    }
}
