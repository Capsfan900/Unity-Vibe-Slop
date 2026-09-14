using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A thrown javelin: a <see cref="Projectile"/> with a SHAFT. The Seraph Lancer's signature. Everything
    /// combat-side is the bolt the player already knows -- the swept contact, the 0.28 s cue, Block or
    /// Perfect through <see cref="PlayerCombat.ReceiveAttack"/> (hard rule 3), the Perfect reflect that
    /// flies it back into the thrower's health and posture -- and the two things added are presentation:
    /// the visible core is a hot gold TIP with a pale shaft behind it, kept pointed along the live
    /// flight direction every frame (so a reflected javelin visibly turns around and flies point-first
    /// back at him), and a javelin that reaches solid level geometry does not vanish: it plants a
    /// <see cref="JavelinRelic"/> in the wall for a beat and then fades, so a dodged throw still reads
    /// as "that was for me".
    ///
    /// <para><b>The cue flare is the tip's, not the shaft's.</b> <see cref="Projectile"/> scales the
    /// "Core" child at the cue (the "press now" pop) and the shaft is that child's child, so the shaft
    /// is counter-scaled every frame to stay the same length: the point flares, the spear does not
    /// balloon into a lie about its hit radius.</para>
    ///
    /// <para><b>Unity messages.</b> The base class owns <c>Update</c> and <c>OnDestroy</c>; declaring
    /// either here would HIDE the base one, so the orientation lives in <c>LateUpdate</c> and the live
    /// count in <c>OnEnable</c>/<c>OnDisable</c>, exactly as <c>BouncingDisc</c> does.</para>
    /// </summary>
    public sealed class Javelin : Projectile
    {
        [Header("Javelin (rule 9: written by SeraphLancerJavelins at launch)")]
        [Tooltip("Seconds a javelin that struck the world stays planted before it is gone (JavelinRelic).")]
        public float relicSeconds = 1.5f;
        [Tooltip("Fraction of relicSeconds the relic holds at full size before it shrinks away.")]
        public float relicHoldFraction = 0.7f;
        [Tooltip("Metres of the shaft buried past the contact point, so the tip reads as IN the wall.")]
        public float relicEmbed = 0.12f;
        [Tooltip("Impact ping volume at zero distance; falls linearly to nothing at stuckRange.")]
        public float stuckVolume = 0.5f;
        public float stuckRange = 24f;
        [Tooltip("Whose ear the ping is judged from (the player). Null = full volume.")]
        public Transform listener;
        [ColorUsage(true, true)] public Color tipColor = new Color(1.45f, 1.05f, 0.40f, 1f);
        [Tooltip("Materials the relic borrows (shared with the launcher; never destroyed here).")]
        public Material relicShaftMaterial;
        public Material relicTipMaterial;

        /// <summary>Every javelin alive in the scene, any thrower. The launcher's global cap reads this.</summary>
        public static int LiveCount { get { return live.Count; } }

        static readonly List<Javelin> live = new List<Javelin>();
        Transform core, shaft;
        Vector3 shaftBaseScale, shaftBasePos;
        float coreBaseScale = 1f;
        bool measured;

        // ---------------------------------------------------------------- pure arithmetic (tests)

        /// <summary>The rotation that points a +Z-forward mesh along <paramref name="direction"/>; identity when degenerate.</summary>
        public static Quaternion Heading(Vector3 direction)
        {
            if (direction.sqrMagnitude < 1e-6f) return Quaternion.identity;
            return Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        /// <summary>
        /// The shaft's local scale (or position) that keeps it the same WORLD size while its parent, the
        /// core, is flared from <paramref name="coreBase"/> to <paramref name="coreNow"/>.
        /// </summary>
        public static Vector3 CounterScale(Vector3 baseLocal, float coreNow, float coreBase)
        {
            float k = coreBase / Mathf.Max(0.0001f, coreNow);
            return baseLocal * k;
        }

        /// <summary>The impact ping's volume at <paramref name="distance"/> from the listener: linear to silence at the range.</summary>
        public static float PingVolume(float distance, float baseVolume, float audibleRange)
        {
            if (audibleRange <= 0f) return baseVolume;
            return baseVolume * Mathf.Clamp01(1f - Mathf.Max(0f, distance) / audibleRange);
        }

        // ---------------------------------------------------------------- lifecycle

        void OnEnable() { live.Add(this); }
        void OnDisable() { live.Remove(this); }

        /// <summary>Find the tip and the shaft once and remember their launch-time local scale.</summary>
        void Measure()
        {
            if (measured) return;
            if (core == null)
            {
                core = transform.Find("Core");
                if (core != null) shaft = core.Find("Shaft");
            }
            if (core == null || shaft == null) return;
            shaftBaseScale = shaft.localScale;
            shaftBasePos = shaft.localPosition;
            coreBaseScale = Mathf.Max(0.0001f, core.localScale.x);
            measured = true;
        }

        void LateUpdate()
        {
            if (IsSpent) return;
            Measure();
            if (core == null) return;
            // Point-first along the live line: after a Perfect the base flips Direction and the spear
            // turns to fly back at the thrower.
            core.rotation = Heading(Direction);
            if (shaft != null)
            {
                float now = core.localScale.x;
                shaft.localScale = CounterScale(shaftBaseScale, now, coreBaseScale);
                shaft.localPosition = CounterScale(shaftBasePos, now, coreBaseScale);
            }
        }

        protected override bool OnWorldContact(RaycastHit hit)
        {
            // Plant a relic where it struck, oriented along the flight, then let the base spend the
            // bolt as every sentry bolt is spent on a wall. The relic is presentation only.
            Measure();
            if (relicShaftMaterial != null && measured)
            {
                float length = shaftBaseScale.y * 2f * coreBaseScale;   // cylinder height is 2 units
                float diameter = shaftBaseScale.x * coreBaseScale;
                JavelinRelic.Plant(hit.point, Direction, length, diameter, relicEmbed,
                                   relicShaftMaterial, relicTipMaterial, tipColor, relicSeconds, relicHoldFraction);
            }
            SlashFx.Sparks(hit.point, hit.normal, tipColor, 7, 5f, 80f);
            float dist = listener != null ? Vector3.Distance(hit.point, listener.position) : 0f;
            float vol = PingVolume(dist, stuckVolume, stuckRange);
            // Sfx.HitLight is an existing folder (hard rule 7: nothing appended).
            if (vol > 0.01f) AudioManager.Play(Sfx.HitLight, vol, 0.7f, 0.05f);
            return false;
        }
    }
}
