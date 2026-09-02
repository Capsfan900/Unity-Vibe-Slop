using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Camera disturbance on a dedicated ShakeRoot transform. Unscaled time, stacking.
    ///
    /// <para>TWO INDEPENDENT CHANNELS, and the distinction is the point:</para>
    /// <list type="bullet">
    ///   <item><b>Shake</b> — Perlin noise, omnidirectional by construction. It says "something
    ///   happened". Landings, blocks, hits.</item>
    ///   <item><b>Kick</b> — an authored, DIRECTIONAL impulse with a rise and a settle
    ///   (<see cref="ParryImpulse.KickCurve"/>). It says "something hit you, from there". This is what
    ///   a deflect uses, and it is the difference between an event and a blow.</item>
    /// </list>
    ///
    /// <para>The two sum. ShakeRoot sits between the look pivot and the camera, so everything written
    /// here is a local delta that returns to zero and can never accumulate into the player's aim.</para>
    /// </summary>
    public class CameraShake : MonoBehaviour
    {
        public static CameraShake I { get; private set; }

        class Shake { public float amp, duration, start, freq, seed; }
        readonly List<Shake> shakes = new List<Shake>();

        class KickReq { public Vector3 euler, offset; public float duration, start, attack; }
        readonly List<KickReq> kicks = new List<KickReq>();

        /// <summary>A fight can request several of these inside a flurry; past a handful they stop
        /// reading as separate blows and start reading as a wobble, so the oldest is dropped.</summary>
        const int MaxKicks = 3;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public void Add(float amplitude, float seconds, float freq = 25f)
        {
            shakes.Add(new Shake { amp = amplitude, duration = seconds, start = Time.unscaledTime, freq = freq, seed = Random.value * 100f });
        }

        /// <summary>
        /// An authored directional impulse. <paramref name="euler"/> and <paramref name="offset"/> are
        /// the values at the PEAK of the kick, in ShakeRoot's local space (which is the camera's, up to
        /// the identity transform between them); the envelope scales both.
        /// </summary>
        public void Kick(Vector3 euler, Vector3 offset, float seconds, float attackFraction = ParryImpulse.KickAttackFraction)
        {
            if (seconds <= 0f) return;
            if (kicks.Count >= MaxKicks) kicks.RemoveAt(0);
            kicks.Add(new KickReq
            {
                euler = euler,
                offset = offset,
                duration = seconds,
                start = Time.unscaledTime,
                attack = attackFraction
            });
        }

        // ---- sustained roll ------------------------------------------------------------------------
        float rollTarget, roll, rollVel;

        /// <summary>
        /// A THIRD channel, and unlike the other two it is HELD rather than fired: a bank that lasts as
        /// long as whatever is causing it. Added for the slide, which is a sustained move and therefore
        /// cannot be described by an impulse at all.
        ///
        /// <para>Roll is the cheapest readability in the project. It never moves the aim vector, so a
        /// held bank costs nothing at the next parry or the next swing — which is exactly why it, and
        /// not pitch or yaw, is the channel a sustained effect is allowed to use.</para>
        ///
        /// <para>Set every frame by its owner and set to 0 to release. Eased here (0.11 s) rather than
        /// by the caller, and snapped to exactly zero once released and small, because ShakeRoot sits
        /// between the look pivot and the lens: any residue is a crooked view for the rest of the run.</para>
        /// </summary>
        public void SetRoll(float degrees) { rollTarget = degrees; }

        /// <summary>The held roll actually being rendered this frame, in degrees. For tests.</summary>
        public float Roll { get { return roll; } }

        public void Small() { var f = GameManager.I ? GameManager.I.feel : null; Add(f ? f.shakeSmallAmp : 0.06f, f ? f.shakeSmallTime : 0.12f); }
        public void Medium() { var f = GameManager.I ? GameManager.I.feel : null; Add(f ? f.shakeMedAmp : 0.14f, f ? f.shakeMedTime : 0.2f); }
        public void Big() { var f = GameManager.I ? GameManager.I.feel : null; Add(f ? f.shakeBigAmp : 0.3f, f ? f.shakeBigTime : 0.35f); }

        void LateUpdate()
        {
            Vector3 offset = Vector3.zero;
            float rot = 0f;
            float now = Time.unscaledTime;
            for (int i = shakes.Count - 1; i >= 0; i--)
            {
                var s = shakes[i];
                float t = (now - s.start) / s.duration;
                if (t >= 1f) { shakes.RemoveAt(i); continue; }
                float falloff = 1f - t;
                falloff *= falloff;
                float k = now * s.freq;
                offset.x += (Mathf.PerlinNoise(s.seed, k) - 0.5f) * 2f * s.amp * falloff;
                offset.y += (Mathf.PerlinNoise(s.seed + 10f, k) - 0.5f) * 2f * s.amp * falloff;
                rot += (Mathf.PerlinNoise(s.seed + 20f, k) - 0.5f) * 2f * s.amp * 8f * falloff;
            }
            // Kick channel. Summed with the noise rather than replacing it: the deflect's Small() shake
            // is the texture and the kick is the direction, and dropping either one loses half the blow.
            Vector3 kickEuler = Vector3.zero;
            for (int i = kicks.Count - 1; i >= 0; i--)
            {
                var kk = kicks[i];
                float t = (now - kk.start) / kk.duration;
                if (t >= 1f) { kicks.RemoveAt(i); continue; }
                float e = ParryImpulse.KickCurve(t, kk.attack);
                kickEuler += kk.euler * e;
                offset += kk.offset * e;
            }

            // Held roll, summed last. It is the only channel here that is allowed to be non-zero for
            // seconds at a time, so it eases in and out instead of stepping.
            roll = Mathf.SmoothDamp(roll, rollTarget, ref rollVel, 0.11f, Mathf.Infinity, Time.unscaledDeltaTime);
            if (rollTarget == 0f && Mathf.Abs(roll) < 0.005f) { roll = 0f; rollVel = 0f; }

            transform.localPosition = offset;
            transform.localRotation = Quaternion.Euler(kickEuler.x, kickEuler.y, kickEuler.z + rot + roll);
        }
    }
}
