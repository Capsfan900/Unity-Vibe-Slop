using System;
using UnityEngine;

namespace VibeGame1
{
    [CreateAssetMenu(menuName = "VibeGame1/Enemy Attack")]
    public class EnemyAttackData : ScriptableObject
    {
        public string attackName = "Slash";
        [Tooltip("Telegraph length in seconds. The player parries at the END of the windup.")]
        public float windup = 0.55f;
        [Tooltip("Seconds after the windup ends before the hit is applied.")]
        public float impactDelay = 0.05f;
        public float strikeDuration = 0.2f;
        public float recovery = 0.7f;
        public float range = 2.4f;
        public float coneDeg = 70f;
        public float damage = 20f;
        public float lungeDistance = 0.5f;
        [Tooltip("Gap before the next hit when this attack is part of a combo.")]
        public float comboGap = 0.2f;
        [Tooltip("Multiplier on the posture damage the enemy takes when this attack is perfectly parried.")]
        public float parryPostureMultiplier = 1f;
        public bool unblockable;

        [Header("Animation")]
        [Tooltip("The clip an ANIMATED body (PuppetVisuals) plays for this attack, by name — e.g. " +
                 "'HalberdSweep'. Empty = the pipeline mapping (swing / heavy / '_Stab' / '_Kick' suffix / " +
                 "spin prefix), which is right for the four canonical forge attack clips every model ships. " +
                 "Set it for an attack whose art is a GENERATED, per-character clip: that clip exists for " +
                 "this attack alone, so its name is content and lives here rather than on the pipeline. " +
                 "Timing is untouched: the clip's own contact frame is baked at build time and the clip is " +
                 "stretched onto THIS attack's impact, never the reverse.")]
        public string clip = "";

        [Header("Wind-up silhouette")]
        [Tooltip("The shape this attack's anticipation makes. Leave 'authored' off and the enemy plays " +
                 "the generic cone-derived wind-up instead, so an attack never has to carry a pose.")]
        public WindupPose windupPose = new WindupPose();
    }

    /// <summary>
    /// One attack's anticipation POSE — the silhouette the body holds while it winds up. Timing lives on
    /// <see cref="EnemyAttackData"/> and is untouched by anything here: this describes only what the body
    /// LOOKS like for the <c>windup</c> seconds that were already tuned.
    ///
    /// <para><b>Judge these as a shape at three frames.</b> Big, simple, distinct body positions, read from
    /// the player's eye at 3-4.5 m against a blocky greybox enemy. Detail is invisible at that distance;
    /// only the outline is not.</para>
    ///
    /// <para>Angles are local Euler offsets from the rig's rest pose. <c>armWindup</c>/<c>armStrike</c>
    /// drive the shoulder (the weapon hangs off it, blade along the shoulder's local +Z);
    /// <c>bodyOffset</c>/<c>bodyEuler</c> drive the whole body through <c>LungeRoot</c>, in enemy-local
    /// space: +Z is toward the player, +X the enemy's right, +Y up.</para>
    /// </summary>
    [Serializable]
    public class WindupPose
    {
        [Tooltip("OFF = fall back to the generic cone-derived wind-up. There are 25+ attack assets and " +
                 "most of them are content that never needed its own silhouette.")]
        public bool authored;

        [Tooltip("Shoulder Euler at the peak of the wind-up. THE pose — everything else supports it.")]
        public Vector3 armWindup;
        [Tooltip("Shoulder Euler the swing carries through to. The strike must resolve the wind-up, " +
                 "or the two beats look unrelated.")]
        public Vector3 armStrike;

        [Tooltip("Whole-body offset at the peak, metres, enemy-local (+Z toward the player).")]
        public Vector3 bodyOffset;
        [Tooltip("Whole-body Euler at the peak. Yaw is the cheapest big silhouette change there is: a " +
                 "squared-up body and a bladed body are two different shapes at any distance.")]
        public Vector3 bodyEuler;

        [Tooltip("How much of the shoulder angle the hand trails by, so the blade whips instead of " +
                 "rotating rigidly.")]
        [Range(0f, 1f)] public float weaponLag = 0.45f;
    }

    [Serializable]
    public class AttackCombo
    {
        public EnemyAttackData[] hits;
        public AttackCombo() { }
        public AttackCombo(params EnemyAttackData[] h) { hits = h; }
    }
}
