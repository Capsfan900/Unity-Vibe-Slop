using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// STORM JUDGEMENT's ticking zone: the Cinder Judge's signature. While the brain is in its Strike
    /// state for the storm attack, the player standing inside a cylinder around the Judge takes a small
    /// hit every <see cref="tickInterval"/> seconds -- the Minecraft-lava feel: nothing you parry, a
    /// place you must not be.
    ///
    /// <para><b>Every tick resolves through <see cref="PlayerCombat.ReceiveAttack"/></b> (hard rule 3),
    /// carrying the same <see cref="EnemyAttackData"/> the brain scheduled, with the asset's own
    /// <c>unblockable</c> flag, so it takes the existing unblockable road inside
    /// <see cref="ParryMath"/>: never a Perfect, never a Block, always the Hit branch and its shove.
    /// Nothing here touches the player's health, posture or velocity directly.</para>
    ///
    /// <para><b>One combat clock, two readers.</b> <see cref="EnemyController.DoImpact"/> lands the
    /// storm's first contact at the brain's impact time exactly as it does for every other attack (the
    /// asset's <c>coneDeg</c> is 360 and its <c>range</c> is this radius minus the brain's 0.5 m slack).
    /// This component reads that same <see cref="EnemyController.NextImpactTime"/> and ticks at fixed
    /// multiples of the interval AFTER it, so a tick never lands on the brain's frame. Scaled time
    /// throughout, like the brain: hitstop freezes the storm with the world.</para>
    ///
    /// <para><b>It stops when the brain stops.</b> Death, a posture break, an execute -- anything that
    /// takes the brain out of Strike -- ends the storm on the next frame with no event wiring, because
    /// the active test is the brain's own state. A dead or invulnerable player is
    /// <see cref="PlayerCombat.ReceiveAttack"/>'s call, not repeated here.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinderJudgeStorm : MonoBehaviour
    {
        [Header("Storm Judgement zone (rule 9: every value is written by MiniBossFactory)")]
        [Tooltip("The attack asset this component ticks for. Any other attack in Strike is ignored.")]
        public string stormAttack = "CinderJudge_StormJudgement";
        [Tooltip("Seconds between ticks after the brain's own impact. 0.30: fast enough that standing " +
                 "still is obviously wrong, slow enough that stepping out costs one tick, not three.")]
        public float tickInterval = 0.30f;
        [Tooltip("Cylinder radius around the Judge's feet, metres. The tornado is drawn at exactly this " +
                 "radius: what you see is what hurts.")]
        public float radius = 3.6f;
        [Tooltip("Cylinder height above the Judge's feet. Tall enough that a jump inside the ring is " +
                 "still inside the ring.")]
        public float height = 4.5f;
        [Tooltip("How far BELOW the Judge's feet still counts. A player one step down a kerb is not out.")]
        public float floorSlack = 0.6f;

        /// <summary>True while the brain is in Strike on the storm attack.</summary>
        public bool IsActive { get; private set; }
        /// <summary>Ticks whose clock came due this storm, inside or not. Tests and the harness.</summary>
        public int TicksFired { get; private set; }
        /// <summary>Ticks that found the player inside and were handed to ReceiveAttack.</summary>
        public int TicksLanded { get; private set; }
        /// <summary>The brain's impact time for the current storm, or MaxValue between storms.</summary>
        public float ImpactTime { get; private set; } = float.MaxValue;

        EnemyController controller;
        PlayerCombat playerCombat;
        EnemyAttackData active;
        float nextTickAt;

        /// <summary>
        /// Is <paramref name="point"/> (the player's feet) inside the storm cylinder standing on
        /// <paramref name="center"/> (the Judge's feet)? Horizontal disc plus a vertical band from
        /// <paramref name="floorSlack"/> below to <paramref name="height"/> above. Pure, for the tests.
        /// </summary>
        public static bool InsideCylinder(Vector3 center, Vector3 point, float radius, float height, float floorSlack)
        {
            float dx = point.x - center.x, dz = point.z - center.z;
            if (dx * dx + dz * dz > radius * radius) return false;
            float dy = point.y - center.y;
            return dy >= -floorSlack && dy <= height;
        }

        /// <summary>
        /// How many component ticks fit inside a strike of <paramref name="strikeDuration"/> seconds
        /// AFTER the brain's impact (which is tick zero and is not counted here). Pure, for the tests
        /// that hold the storm's worst case against the player's bars.
        /// </summary>
        public static int TicksAfterImpact(float strikeDuration, float interval)
        {
            if (interval <= 0f || strikeDuration <= 0f) return 0;
            return Mathf.FloorToInt((strikeDuration - 0.0001f) / interval);
        }

        /// <summary>The most damage a player who never leaves can take: the brain's impact plus every tick.</summary>
        public static float MaxDamage(float perTick, float strikeDuration, float interval)
        {
            return perTick * (1 + TicksAfterImpact(strikeDuration, interval));
        }

        void Awake()
        {
            controller = GetComponent<EnemyController>();
        }

        void OnDisable()
        {
            End();
        }

        void Update()
        {
            if (controller == null || Time.timeScale <= 0f) return;

            var atk = controller.CurrentAttack;
            bool storming = controller.IsAlive
                         && controller.Current == EnemyController.State.Strike
                         && atk != null && atk.name == stormAttack;
            if (!storming)
            {
                if (IsActive) End();
                return;
            }
            if (!IsActive) Begin(atk);

            // Fixed cadence off the brain's impact, never off "the last time we ticked": a dropped
            // frame cannot slide the rhythm, and two ticks can never bunch up into one frame.
            while (Time.time >= nextTickAt)
            {
                TicksFired++;
                if (PlayerInside())
                {
                    TicksLanded++;
                    playerCombat.ReceiveAttack(new AttackInfo
                    {
                        attack = active,
                        attacker = controller,
                        damage = active.damage,
                        unblockable = active.unblockable
                    });
                }
                nextTickAt += Mathf.Max(0.05f, tickInterval);
            }
        }

        void Begin(EnemyAttackData atk)
        {
            active = atk;
            IsActive = true;
            TicksFired = 0;
            TicksLanded = 0;
            // The brain publishes the storm's impact on its first Strike frame (struck is still false).
            // Only a zero impactDelay could race that, and the shipped one is 0.45 s; fall back to now.
            float impact = controller.NextImpactTime;
            ImpactTime = impact < float.MaxValue ? impact : Time.time;
            nextTickAt = ImpactTime + Mathf.Max(0.05f, tickInterval);
            if (playerCombat == null) playerCombat = FindAnyObjectByType<PlayerCombat>();
        }

        void End()
        {
            IsActive = false;
            active = null;
            ImpactTime = float.MaxValue;
        }

        bool PlayerInside()
        {
            if (playerCombat == null) playerCombat = FindAnyObjectByType<PlayerCombat>();
            if (playerCombat == null) return false;
            return InsideCylinder(transform.position, playerCombat.transform.position, radius, height, floorSlack);
        }
    }
}
