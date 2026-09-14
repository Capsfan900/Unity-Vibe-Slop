using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A thrown disc: a <see cref="Projectile"/> that RICOCHETS. The Orbit Dancer's signature. Everything
    /// combat-side is the bolt the player already knows -- the swept contact, the 0.28 s cue, Block or
    /// Perfect through <see cref="PlayerCombat.ReceiveAttack"/> (hard rule 3), the Perfect reflect that
    /// flies it back into the thrower's health and posture -- and the one thing added is what happens at
    /// a wall: instead of spending, the disc reflects off the contact normal, re-arms its cue, and comes
    /// again. Up to <see cref="maxBounces"/> times; the next wall after that shatters it, and
    /// <see cref="Projectile.maxLife"/> caps the whole flight regardless.
    ///
    /// <para><b>Each approach is a fresh cue.</b> <see cref="Projectile.Redirect"/> resets the cue, so a
    /// disc that has bounced fires <c>Sfx.ParryCue</c> and the flare again 0.28 s before it reaches the
    /// player -- the same lead every attack gives. The facing test in <c>ParryMath</c> is judged against
    /// the disc's live direction, so after a bank shot the player must turn to where it comes FROM.</para>
    ///
    /// <para><b>No homing, ever.</b> The ricochet is geometry the player can read; a disc that curved
    /// after a bounce would be a lie about the wall. <c>EnemyData.projectileHomingDegPerSec</c> is 0
    /// for the Dancer and this class never steers.</para>
    ///
    /// <para><b>Unity messages.</b> The base class owns <c>Update</c> and <c>OnDestroy</c>; declaring
    /// either here would HIDE the base one (Unity dispatches by name on the most-derived type), so the
    /// spin lives in <c>LateUpdate</c> and the live count in <c>OnEnable</c>/<c>OnDisable</c>.</para>
    /// </summary>
    public sealed class BouncingDisc : Projectile
    {
        [Header("Ricochet (rule 9: written by OrbitDancerDiscs at launch)")]
        [Tooltip("Walls this disc may reflect off before the next one shatters it.")]
        public int maxBounces = 3;
        [Tooltip("Visual spin of the disc mesh about its own axis, degrees per second.")]
        public float spinDegPerSec = 900f;
        [Tooltip("Metres the disc is lifted off the wall along its normal after a bounce, so the next " +
                 "frame's linecast does not start inside the collider it just left.")]
        public float surfaceSkin = 0.03f;
        [Tooltip("Ping volume at zero distance; falls linearly to nothing at pingRange.")]
        public float pingVolume = 0.55f;
        public float pingRange = 24f;
        [Tooltip("Whose ear the ping is judged from (the player). Null = full volume.")]
        public Transform listener;
        [ColorUsage(true, true)] public Color rimColor = new Color(0.36f, 1.25f, 1.15f, 1f);

        /// <summary>Walls this disc has reflected off so far.</summary>
        public int Bounces { get; private set; }
        /// <summary>Every disc alive in the scene, any thrower. The launcher's global cap reads this.</summary>
        public static int LiveCount { get { return live.Count; } }

        static readonly List<BouncingDisc> live = new List<BouncingDisc>();
        Transform spinner;

        // ---------------------------------------------------------------- pure arithmetic (tests)

        /// <summary>
        /// The reflected flight line. Mirror across the contact normal, then guarantee it leaves the
        /// surface: a grazing contact can reflect to a line still (numerically) inside the wall, and a
        /// disc that re-hits the same wall next frame would burn a bounce on nothing.
        /// </summary>
        public static Vector3 Ricochet(Vector3 direction, Vector3 normal)
        {
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            Vector3 d = direction.sqrMagnitude > 1e-6f ? direction.normalized : -n;
            Vector3 r = Vector3.Reflect(d, n);
            float away = Vector3.Dot(r, n);
            if (away < 0.05f) r += n * (0.05f - away);
            return r.normalized;
        }

        /// <summary>May a disc that has bounced <paramref name="bouncesSoFar"/> times take one more wall?</summary>
        public static bool CanBounce(int bouncesSoFar, int cap)
        {
            return cap > 0 && bouncesSoFar < cap;
        }

        /// <summary>The bounce ping's volume at <paramref name="distance"/> from the listener: linear to silence at the range.</summary>
        public static float PingVolume(float distance, float baseVolume, float audibleRange)
        {
            if (audibleRange <= 0f) return baseVolume;
            return baseVolume * Mathf.Clamp01(1f - Mathf.Max(0f, distance) / audibleRange);
        }

        // ---------------------------------------------------------------- lifecycle

        void OnEnable() { live.Add(this); }
        void OnDisable() { live.Remove(this); }

        void LateUpdate()
        {
            if (IsSpent) return;
            if (spinner == null) spinner = transform.Find("Core");
            if (spinner != null) spinner.Rotate(0f, spinDegPerSec * Time.deltaTime, 0f, Space.Self);
        }

        protected override bool OnWorldContact(RaycastHit hit)
        {
            if (!CanBounce(Bounces, maxBounces))
            {
                // The wall after the last permitted bounce: shatter, and let the base spend it.
                SlashFx.Sparks(hit.point, hit.normal, rimColor, 10, 6f, 120f);
                Ping(hit.point, 0.8f);
                return false;
            }
            Bounces++;
            Redirect(hit.point + hit.normal * surfaceSkin, Ricochet(Direction, hit.normal));
            SlashFx.Sparks(hit.point, hit.normal, rimColor, 6, 5f, 70f);
            Ping(hit.point, 1.45f);
            return true;
        }

        /// <summary>
        /// One metallic ping per wall, attenuated by distance to the player. AudioManager has no
        /// positional play (every source in it is 2D, spatialBlend 0), so this is the honest
        /// approximation inside the existing mix: quieter the further the wall is from your ear.
        /// Sfx.HitLight is an existing folder (hard rule 7: nothing appended).
        /// </summary>
        void Ping(Vector3 at, float pitch)
        {
            float dist = listener != null ? Vector3.Distance(at, listener.position) : 0f;
            float vol = PingVolume(dist, pingVolume, pingRange);
            if (vol <= 0.01f) return;
            AudioManager.Play(Sfx.HitLight, vol, pitch, 0.06f);
        }
    }
}
