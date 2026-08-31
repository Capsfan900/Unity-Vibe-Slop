using System;
using UnityEngine;

namespace VibeGame1
{
    [Serializable]
    public struct Pose
    {
        public Vector3 pos;
        public Vector3 euler;
        public Pose(Vector3 p, Vector3 e) { pos = p; euler = e; }
        public static Pose Lerp(Pose a, Pose b, float t)
        {
            return new Pose(Vector3.Lerp(a.pos, b.pos, t),
                Quaternion.Slerp(Quaternion.Euler(a.euler), Quaternion.Euler(b.euler), t).eulerAngles);
        }
    }

    /// <summary>
    /// The shape of a weapon's super attack — the payoff spent at full Pyre. The kind picks the
    /// presentation and how the reach is interpreted; the numbers beside it do the rest, so a new
    /// weapon gets a super by authoring data, never by adding code.
    /// </summary>
    public enum SuperKind
    {
        /// <summary>One wide horizontal sweep in front of the player. The generalist's answer.</summary>
        Cleave,
        /// <summary>Many small stabs into a narrow cone. Damage arrives as posture pressure, not a lump.</summary>
        Flurry,
        /// <summary>An overhead into the ground: a 360° shockwave, huge posture and knockback.</summary>
        Quake,
        /// <summary>An instant 360° detonation at long reach. No wind-up, no mercy.</summary>
        Nova,
    }

    [CreateAssetMenu(menuName = "VibeGame1/Weapon")]
    public class WeaponData : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Sword";
        [ColorUsage(true, true)] public Color neon = Color.cyan;
        public GameObject viewmodelPrefab;
        public float viewmodelScale = 0.5f;

        [Header("Damage")]
        public float baseDamage = 22f;
        public float postureDamage = 12f;
        public float executeDamage = 300f;
        public float strScale = 0.5f, dexScale = 0.5f, arcScale = 0.2f;

        [Header("Combo")]
        public int comboLength = 3;
        public float[] comboMultipliers = { 1f, 1f, 1.5f };
        public float attackDuration = 0.38f;
        public float hitDelay = 0.12f;
        public float comboWindow = 0.35f;

        [Header("Hitbox")]
        public float hitOffset = 1.6f;
        public float hitRadius = 1.1f;
        public float hitStopSeconds = 0.05f;

        [Header("Parry")]
        public float parryWindowMultiplier = 1f;
        public float parryPostureDamage = 25f;
        [Tooltip("Added to the Pyre gained by a successful parry with this weapon. May be negative.")]
        public float pyreBonus = 0f;

        [Header("Super attack — spent at full Pyre (Q)")]
        [Tooltip("HUD name. Shown on the PYRE FULL banner so the player knows what Q is about to do.")]
        public string superName = "SUPER";
        public SuperKind superKind = SuperKind.Cleave;
        [Tooltip("Damage per hit. A Flurry lands superHits of these; everything else lands one.")]
        public float superDamage = 120f;
        [Tooltip("Posture damage per hit. This is what a super is really for.")]
        public float superPostureDamage = 60f;
        [Tooltip("Reach in metres from the player.")]
        public float superRadius = 5f;
        [Tooltip("Full width of the arc, in degrees. 360 hits everything in radius.")]
        public float superArcDeg = 160f;
        [Tooltip("How many times the hit shape is applied. >1 spreads them evenly across superActive.")]
        public int superHits = 1;
        [Tooltip("Commit before the first hit lands.")]
        public float superWindup = 0.28f;
        [Tooltip("How long the hits are spread over. A single-hit super lands at the start of this.")]
        public float superActive = 0.18f;
        [Tooltip("Settle after the last hit, before control returns.")]
        public float superRecover = 0.3f;
        public float superHitStop = 0.1f;
        public float superShake = 0.5f;
        [Tooltip("Metres of shove applied to everything the super catches.")]
        public float superKnockback = 2f;

        [Header("Viewmodel poses (local to viewmodel root)")]
        public Pose idle = new Pose(new Vector3(0.45f, -0.35f, 0.7f), new Vector3(0, -10, 0));
        public Pose windup = new Pose(new Vector3(0.6f, -0.2f, 0.5f), new Vector3(-20, -60, 30));
        public Pose swingEnd = new Pose(new Vector3(-0.3f, -0.45f, 0.8f), new Vector3(20, 40, -40));
        public Pose parry = new Pose(new Vector3(0.05f, -0.15f, 0.6f), new Vector3(0, 90, 80));
        public Pose drink = new Pose(new Vector3(0.7f, -0.6f, 0.5f), new Vector3(30, -40, 0));
        public Pose executeWindup = new Pose(new Vector3(0.5f, 0.3f, 0.5f), new Vector3(-70, -30, 20));
        public Pose executeEnd = new Pose(new Vector3(0.1f, -0.6f, 0.9f), new Vector3(60, 0, 0));

        public float ComboMultiplier(int i)
        {
            if (comboMultipliers == null || comboMultipliers.Length == 0) return 1f;
            return comboMultipliers[Mathf.Clamp(i, 0, comboMultipliers.Length - 1)];
        }
    }
}
