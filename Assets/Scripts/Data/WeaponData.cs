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
        public float juiceBonus = 0f;

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
