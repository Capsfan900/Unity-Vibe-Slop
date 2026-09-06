using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The picture and the noise of skating on water: spray kicked out behind the feet and a hiss that
    /// rides the speed. Built by <see cref="PlayerFeedback"/> like <see cref="SlideFx"/> and
    /// <see cref="WallRunFx"/>, and ticked from its Update off the motor's live state.
    ///
    /// <para><b>What it deliberately does NOT touch.</b> <c>CameraFX.FovHold</c>, <c>CameraShake.SetRoll</c>
    /// and <c>CameraShake.SetRumble</c> each have exactly ONE writer (SlideFx.Tick, with WallRunFx layered
    /// by order). A fourth writer here would fight them the moment a slide crosses water — which is the
    /// whole point of water. So the lens gets a one-shot FOV kick on ENTRY from PlayerFeedback and nothing
    /// held; the sustained read is spray, sound and the speed itself.</para>
    ///
    /// <para>Spray routes through <see cref="SlashFx.Sparks"/> — a fixed pool, normalised to a peak
    /// channel of 1.0, under the 1.05 bloom threshold — in a cold blue, so it cannot be mistaken for the
    /// slide's warm grit or for anything the fight says with light. The hiss is a synthesised loop on
    /// its own AudioSource for the same reason the scrape is (no looping API in AudioManager, rule 7:
    /// no new Sfx), one octave brighter than the scrape so the two never read as one sound.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class WaterFx : MonoBehaviour
    {
        [Tooltip("Spray bursts per second at the water floor speed; scales with speed below it.")]
        public float sprayRate = 26f;
        [Tooltip("Cold blue. Normalised by SlashFx to a 1.0 peak - no bloom.")]
        public Color sprayColor = new Color(0.55f, 0.80f, 1f, 1f);
        [Tooltip("Peak gain of the hiss loop.")]
        [Range(0f, 0.6f)] public float hissVolume = 0.14f;
        [Tooltip("Seconds the hiss fades over when you leave the water.")]
        public float hissRelease = 0.15f;

        FirstPersonMotor motor;
        Transform player;
        AudioSource hiss;
        AudioClip hissClip;
        float hissGain;
        float sprayAccum;

        /// <summary>0..1 how fast the player skates relative to the water floor. For tests / the harness.</summary>
        public float SpeedFraction { get; private set; }

        public void Build(FirstPersonMotor owner)
        {
            motor = owner;
            player = owner != null ? owner.transform : transform;
            BuildHiss();
        }

        void OnDestroy()
        {
            if (hissClip != null) Destroy(hissClip);
        }

        void OnDisable()
        {
            if (hiss != null) { hiss.Stop(); hiss.volume = 0f; }
            hissGain = 0f;
            SpeedFraction = 0f;
        }

        /// <summary>Band-limited noise, one octave above the slide's scrape: water, not gravel.</summary>
        void BuildHiss()
        {
            const int Rate = 22050;
            const int Len = Rate;
            const int Xf = 2048;
            var raw = new float[Len + Xf];
            var rng = new System.Random(4102);
            float lp = 0f, hp = 0f, peak = 0.0001f;
            for (int i = 0; i < raw.Length; i++)
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (w - lp) * 0.62f;     // ~5 kHz corner: hiss
                hp += (lp - hp) * 0.04f;    // ~140 Hz corner, subtracted
                float v = lp - hp;
                raw[i] = v;
                float a = v < 0f ? -v : v;
                if (a > peak) peak = a;
            }
            var data = new float[Len];
            float norm = 0.5f / peak;
            for (int i = 0; i < Len; i++)
            {
                float v = raw[i];
                if (i < Xf) { float k = i / (float)Xf; v = raw[i] * k + raw[Len + i] * (1f - k); }
                data[i] = v * norm;
            }
            hissClip = AudioClip.Create("WaterHiss", Len, 1, Rate, false);
            hissClip.SetData(data, 0);

            var go = new GameObject("WaterHiss");
            go.transform.SetParent(transform, false);
            hiss = go.AddComponent<AudioSource>();
            hiss.clip = hissClip;
            hiss.loop = true;
            hiss.playOnAwake = false;
            hiss.spatialBlend = 0f;
            hiss.volume = 0f;
        }

        /// <summary>Drive the spray and the hiss from the motor's live state. Unscaled time (rule 1).</summary>
        public void Tick(float rate, float volume)
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || motor == null) return;

            bool skating = motor.InWater && GameManager.IsPlaying && (motor.IsGrounded || motor.IsSliding);
            Vector3 vel = motor.Velocity;
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            float speed = flat.magnitude;
            float floor = Mathf.Max(1f, motor.WaterFloorSpeed);
            SpeedFraction = skating ? Mathf.Clamp01(speed / floor) : 0f;

            if (hiss != null)
            {
                float master = AudioManager.I != null ? AudioManager.I.masterVolume : 1f;
                float want = skating ? volume * (0.35f + 0.65f * SpeedFraction) * master : 0f;
                hissGain = want > hissGain
                    ? want
                    : Mathf.MoveTowards(hissGain, want, (volume * master) / Mathf.Max(0.01f, hissRelease) * dt);
                if (want > 0f && !hiss.isPlaying) hiss.Play();
                hiss.volume = hissGain;
                hiss.pitch = Mathf.Lerp(0.9f, 1.25f, SpeedFraction);
                if (hissGain <= 0.0001f && hiss.isPlaying && !skating) hiss.Stop();
            }

            if (skating && speed > 1f)
            {
                Vector3 dir = flat / speed;
                sprayAccum += rate * (0.3f + 0.7f * SpeedFraction) * dt;
                while (sprayAccum >= 1f)
                {
                    sprayAccum -= 1f;
                    Vector3 at = player.position + Vector3.up * 0.08f - dir * 0.2f;
                    SlashFx.Sparks(at, -dir * 0.6f + Vector3.up * 0.8f, sprayColor, 3, 3.5f, 0.9f);
                }
            }
            else sprayAccum = 0f;
        }
    }
}
