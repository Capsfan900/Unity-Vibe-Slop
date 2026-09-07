using UnityEngine;

namespace VibeGame1
{
    public enum Sfx
    {
        Tick, Parry, Block, Hit, Swing, Execute, Heal, Ultimate, Checkpoint, Death, Jump, Dash, Souls, Stagger, Hurt, Click, Roar, Drone,
        // Appended (never reorder — folder names under Resources/Audio/Sfx/ follow these names)
        ParryCue, Footstep, Land, PostureBreak,
        Thunder, ItemPickup, ItemUse,
        Teleport,
        // Appended 2026-09-06 (audio pass): four systems built the last two days shipped with no sound
        // at all. See AudioManager's trim table for why each sits where it does in the mix.
        Refuse, Detonate, Tension, Spill,
        // Appended 2026-09-06 (weapon rework, weapon-audio pass): every weapon shared Sfx.Swing / Sfx.Hit
        // regardless of weight -- a dagger and a hammer sounded identical connecting. These four give the
        // light and heavy ends of the roster their own transient/mechanical/sub/body/tail layering; Sword
        // keeps the original Swing/Hit as the mid-weight default. See WeaponAudio.cs for the selection.
        SwingLight, SwingHeavy, HitLight, HitHeavy
    }

    /// <summary>
    /// Procedurally synthesized dark-fantasy placeholder sound effects (no audio assets needed).
    /// Low, heavy, metallic and noisy on purpose: growls, clangs, sub booms, bells and drones.
    /// </summary>
    public static class ProceduralSfx
    {
        const int Rate = 44100;
        const float TwoPi = Mathf.PI * 2f;

        public static AudioClip Build(Sfx s)
        {
            switch (s)
            {
                case Sfx.Tick: return Tick();
                case Sfx.Parry: return Parry();
                case Sfx.Block: return Block();
                case Sfx.Hit: return Hit();
                case Sfx.Swing: return Whoosh("Swing", 0.3f, 0.35f, 0.02f, 0.14f, 0.7f);
                case Sfx.Execute: return Execute();
                case Sfx.Heal: return Heal();
                case Sfx.Ultimate: return Ultimate();
                case Sfx.Checkpoint: return Bell();
                case Sfx.Death: return Death();
                case Sfx.Jump: return Jump();
                case Sfx.Dash: return Whoosh("Dash", 0.2f, 0.4f, 0.03f, 0.2f, 0.6f);
                case Sfx.Souls: return Souls();
                case Sfx.Stagger: return Stagger();
                case Sfx.Hurt: return Hurt();
                case Sfx.Click: return Click();
                case Sfx.Roar: return Roar();
                case Sfx.Drone: return Drone();
                case Sfx.ParryCue: return ParryCue();
                case Sfx.Footstep: return Footstep();
                case Sfx.Land: return Land();
                case Sfx.PostureBreak: return PostureBreak();
                case Sfx.Thunder: return Thunder();
                case Sfx.ItemPickup: return ItemPickupChime();
                case Sfx.ItemUse: return ItemUseSwell();
                case Sfx.Teleport: return Teleport();
                case Sfx.Refuse: return Refuse();
                case Sfx.Detonate: return Detonate();
                case Sfx.Tension: return Tension();
                case Sfx.Spill: return Spill();
                case Sfx.SwingLight: return SwingLight();
                case Sfx.SwingHeavy: return SwingHeavy();
                case Sfx.HitLight: return HitLight();
                case Sfx.HitHeavy: return HitHeavy();
            }
            return Click();
        }

        // ------------------------------------------------------------------ building blocks

        /// <summary>Simple attack/decay envelope in seconds.</summary>
        static float Env(float t, float attack, float decay)
        {
            float a = attack <= 0f ? 1f : Mathf.Clamp01(t / attack);
            float d = decay <= 0f ? 1f : Mathf.Exp(-Mathf.Max(0f, t - attack) / decay);
            return a * d;
        }

        /// <summary>Tanh-style soft clipper, keeps things loud without harsh digital clipping.</summary>
        static float SoftClip(float x)
        {
            return x / (1f + Mathf.Abs(x));
        }

        /// <summary>Sum of inharmonic partials (metal / bell tones) with per-partial decay.</summary>
        static float Partials(float t, float baseFreq, float[] ratios, float[] amps, float decay, float phaseSeed)
        {
            float v = 0f;
            for (int i = 0; i < ratios.Length; i++)
            {
                float f = baseFreq * ratios[i];
                float a = amps != null && i < amps.Length ? amps[i] : 1f / (i + 1);
                // higher partials die faster
                float d = decay / (1f + i * 0.6f);
                v += Mathf.Sin(TwoPi * f * t + phaseSeed * (i + 1)) * a * Mathf.Exp(-t / d);
            }
            return v;
        }

        /// <summary>Stateful one-pole lowpass over white noise. Call once per sample with a cutoff in Hz.</summary>
        class LowpassNoise
        {
            readonly System.Random rng;
            float state;
            public LowpassNoise(int seed) { rng = new System.Random(seed); }
            public float Next(float cutoffHz)
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                float k = 1f - Mathf.Exp(-TwoPi * Mathf.Clamp(cutoffHz, 5f, 12000f) / Rate);
                state += k * (w - state);
                return state;
            }
        }

        /// <summary>Crude bandpass: lowpass minus a lower lowpass.</summary>
        class BandpassNoise
        {
            readonly LowpassNoise lo, hi;
            public BandpassNoise(int seed) { lo = new LowpassNoise(seed); hi = new LowpassNoise(seed); }
            public float Next(float low, float high)
            {
                // both filters see the same random stream because they share the seed
                return hi.Next(high) - lo.Next(low);
            }
        }

        static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

        static AudioClip Make(string name, float[] data, float peak = 0.9f)
        {
            float max = 0f;
            for (int i = 0; i < data.Length; i++) max = Mathf.Max(max, Mathf.Abs(data[i]));
            if (max > 0f)
            {
                float g = peak / max;
                for (int i = 0; i < data.Length; i++) data[i] *= g;
            }
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static int Seed(string name) => name.GetHashCode();

        // ------------------------------------------------------------------ one shots

        static AudioClip Tick()
        {
            const string name = "Tick";
            var d = Buffer(0.25f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / 0.25f;
                float cutoff = Mathf.Lerp(300f, 80f, k);
                float tremolo = 0.55f + 0.45f * Mathf.Sin(TwoPi * 25f * t);
                float growl = noise.Next(cutoff) * 3f;
                // a little tonal grit so it reads as a "voice" instead of just wind
                float grit = Mathf.Sin(TwoPi * Mathf.Lerp(140f, 60f, k) * t) * 0.35f;
                d[i] = SoftClip((growl + grit) * 1.6f) * tremolo * Env(t, 0.01f, 0.12f);
            }
            return Make(name, d, 0.85f);
        }

        static readonly float[] ClangRatios = { 1f, 1.42f, 2.13f, 2.77f, 3.6f };
        static readonly float[] ClangAmps = { 1f, 0.7f, 0.5f, 0.35f, 0.25f };

        static AudioClip Parry()
        {
            const string name = "Parry";
            var d = Buffer(0.4f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float clang = Partials(t, 480f, ClangRatios, ClangAmps, 0.09f, 0.7f);
                float transient = t < 0.02f ? noise.Next(6000f) * (1f - t / 0.02f) * 1.2f : 0f;
                float thump = Mathf.Sin(TwoPi * 65f * t) * Mathf.Exp(-t / 0.08f) * 0.5f;
                d[i] = SoftClip(clang * 1.3f + transient + thump);
            }
            return Make(name, d, 0.9f);
        }

        static AudioClip Block()
        {
            const string name = "Block";
            var d = Buffer(0.22f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float tone = Mathf.Sin(TwoPi * 90f * t) * Env(t, 0.002f, 0.06f);
                float thud = noise.Next(Mathf.Lerp(900f, 150f, t / 0.22f)) * Env(t, 0.002f, 0.05f) * 1.5f;
                d[i] = SoftClip((tone + thud) * 1.4f);
            }
            return Make(name, d, 0.8f);
        }

        static AudioClip Hit()
        {
            const string name = "Hit";
            var d = Buffer(0.2f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / 0.2f;
                float chop = noise.Next(Mathf.Lerp(2500f, 300f, k)) * Env(t, 0.001f, 0.045f) * 2f;
                float thump = Mathf.Sin(TwoPi * 70f * t) * Env(t, 0.002f, 0.07f);
                d[i] = SoftClip((chop + thump) * 1.5f);
            }
            return Make(name, d, 0.85f);
        }

        /// <summary>Dark lowpassed noise sweep. Cutoff opens from lowStart to lowEnd then falls again.</summary>
        static AudioClip Whoosh(string name, float dur, float vol, float attack, float decay, float peak)
        {
            var d = Buffer(dur);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float shape = Mathf.Sin(k * Mathf.PI);
                float cutoff = Mathf.Lerp(120f, 1100f, shape);
                d[i] = noise.Next(cutoff) * shape * shape * 3f * vol * Env(t, attack, dur);
            }
            return Make(name, d, peak);
        }

        static AudioClip Execute()
        {
            const string name = "Execute";
            var d = Buffer(0.9f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float boom = Mathf.Sin(TwoPi * 40f * t) * Env(t, 0.003f, 0.35f) * 1.4f;
                float crunch = noise.Next(Mathf.Lerp(3000f, 200f, Mathf.Clamp01(t / 0.25f))) * Env(t, 0.001f, 0.12f) * 4f;
                float tail = noise.Next(90f) * Env(t, 0.05f, 0.5f) * 2f;
                d[i] = SoftClip(boom + SoftClip(crunch * 2f) * 0.8f + tail);
            }
            return Make(name, d, 0.9f);
        }

        static AudioClip Heal()
        {
            const string name = "Heal";
            var d = Buffer(0.7f);
            float[] freqs = { 220f, 221.5f, 330f, 328.6f, 442f, 444f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float vib = 1f + 0.006f * Mathf.Sin(TwoPi * 4.5f * t);
                float v = 0f;
                for (int f = 0; f < freqs.Length; f++) v += Mathf.Sin(TwoPi * freqs[f] * vib * t) / (1f + f * 0.4f);
                d[i] = v * Env(t, 0.25f, 0.3f);
            }
            return Make(name, d, 0.5f);
        }

        static AudioClip Ultimate()
        {
            const string name = "Ultimate";
            var d = Buffer(1.3f);
            var noise = new LowpassNoise(Seed(name));
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / 1.3f;
                float f = Mathf.Lerp(140f, 28f, 1f - (1f - k) * (1f - k));
                phase += TwoPi * f / Rate;
                float sub = SoftClip(Mathf.Sin(phase) * 3f) * Env(t, 0.01f, 0.9f);
                float wash = noise.Next(Mathf.Lerp(2000f, 120f, k)) * Env(t, 0.05f, 0.5f) * 1.5f;
                d[i] = SoftClip(sub * 1.2f + wash);
            }
            return Make(name, d, 0.9f);
        }

        static readonly float[] BellRatios = { 0.5f, 1f, 1.183f, 1.506f, 2f, 2.514f, 2.662f, 3.011f };
        static readonly float[] BellAmps = { 0.5f, 1f, 0.6f, 0.5f, 0.35f, 0.25f, 0.2f, 0.15f };

        static AudioClip Bell()
        {
            const string name = "Checkpoint";
            var d = Buffer(1.8f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float v = Partials(t, 260f, BellRatios, BellAmps, 0.9f, 1.3f);
                // "distant": soften the strike, keep the hum
                d[i] = v * Env(t, 0.004f, 1.2f);
            }
            return Make(name, d, 0.4f);
        }

        static AudioClip Death()
        {
            const string name = "Death";
            var d = Buffer(2f);
            float[] detune = { 1f, 1.007f, 0.994f };
            float[] phase = new float[3];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / 2f;
                float f = Mathf.Lerp(200f, 55f, k * k);
                float vib = 1f + 0.01f * Mathf.Sin(TwoPi * 3f * t);
                float v = 0f;
                for (int p = 0; p < 3; p++)
                {
                    phase[p] += TwoPi * f * detune[p] * vib / Rate;
                    v += Mathf.Sin(phase[p]) + 0.3f * Mathf.Sin(phase[p] * 2f);
                }
                d[i] = SoftClip(v * 0.6f) * Env(t, 0.08f, 0.9f);
            }
            return Make(name, d, 0.8f);
        }

        static AudioClip Jump()
        {
            const string name = "Jump";
            var d = Buffer(0.08f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float thump = Mathf.Sin(TwoPi * 110f * t) * Env(t, 0.002f, 0.025f);
                float cloth = noise.Next(500f) * Env(t, 0.001f, 0.03f) * 1.5f;
                d[i] = (thump + cloth) * 0.6f;
            }
            return Make(name, d, 0.35f);
        }

        static AudioClip Souls()
        {
            const string name = "Souls";
            var d = Buffer(0.4f);
            var band = new BandpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / 0.4f;
                float whisper = band.Next(600f, 2400f) * Env(t, 0.03f, 0.18f) * 4f;
                float sparkle = Mathf.Sin(TwoPi * Mathf.Lerp(1800f, 2600f, k) * t) * Env(t, 0.05f, 0.2f) * 0.12f;
                d[i] = whisper + sparkle;
            }
            return Make(name, d, 0.45f);
        }

        static AudioClip Stagger()
        {
            const string name = "Stagger";
            var d = Buffer(0.6f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float crack = Partials(t, 720f, ClangRatios, ClangAmps, 0.05f, 2.1f) * 1.2f;
                float transient = t < 0.015f ? noise.Next(8000f) * 1.5f : 0f;
                float rumble = noise.Next(70f) * Env(t, 0.02f, 0.3f) * 4f + Mathf.Sin(TwoPi * 48f * t) * Env(t, 0.01f, 0.25f) * 0.8f;
                d[i] = SoftClip(crack + transient + rumble);
            }
            return Make(name, d, 0.9f);
        }

        static AudioClip Hurt()
        {
            const string name = "Hurt";
            var d = Buffer(0.3f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float thump = Mathf.Sin(TwoPi * 60f * t) * Env(t, 0.002f, 0.09f) * 1.3f;
                float punch = noise.Next(Mathf.Lerp(1500f, 200f, t / 0.3f)) * Env(t, 0.001f, 0.06f) * 2.5f;
                float tone = Mathf.Sin(TwoPi * Mathf.Lerp(260f, 90f, t / 0.3f) * t) * Env(t, 0.005f, 0.08f) * 0.5f;
                d[i] = SoftClip(thump + punch + tone);
            }
            return Make(name, d, 0.85f);
        }

        static AudioClip Click()
        {
            const string name = "Click";
            var d = Buffer(0.04f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = (Mathf.Sin(TwoPi * 320f * t) * 0.5f + noise.Next(1200f)) * Env(t, 0.001f, 0.012f);
            }
            return Make(name, d, 0.3f);
        }

        static AudioClip Roar()
        {
            const string name = "Roar";
            var d = Buffer(1.3f);
            var noise = new LowpassNoise(Seed(name));
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / 1.3f;
                // 60 -> 140 -> 50 Hz
                float f = k < 0.35f ? Mathf.Lerp(60f, 140f, k / 0.35f) : Mathf.Lerp(140f, 50f, (k - 0.35f) / 0.65f);
                phase += TwoPi * f / Rate;
                float growl = Mathf.Sin(phase) + 0.5f * Mathf.Sin(phase * 2f) + 0.25f * Mathf.Sin(phase * 3f);
                float rasp = noise.Next(Mathf.Lerp(400f, 1400f, Mathf.Sin(k * Mathf.PI))) * 2.5f;
                float tremolo = 0.75f + 0.25f * Mathf.Sin(TwoPi * 18f * t);
                d[i] = SoftClip(SoftClip(growl * 2.5f) + rasp * 0.7f) * tremolo * Env(t, 0.06f, 0.6f);
            }
            return Make(name, d, 0.9f);
        }

        static readonly float[] CueRatios = { 1f, 2.05f, 3.14f };
        static readonly float[] CueAmps = { 1f, 0.45f, 0.22f };

        /// <summary>
        /// The "parry NOW" ping. Bright, short and deliberately the most cutting sound in the mix —
        /// everything else here is dark and low, so this sits in a clear frequency band of its own.
        /// </summary>
        static AudioClip ParryCue()
        {
            const string name = "ParryCue";
            var d = Buffer(0.12f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float ping = Partials(t, 1600f, CueRatios, CueAmps, 0.035f, 0.4f);
                float strike = t < 0.006f ? noise.Next(9000f) * (1f - t / 0.006f) * 1.4f : 0f;
                d[i] = SoftClip((ping * 1.4f + strike) * 1.2f) * Env(t, 0.0008f, 0.05f);
            }
            return Make(name, d, 0.9f);
        }

        static AudioClip Footstep()
        {
            const string name = "Footstep";
            var d = Buffer(0.09f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float scuff = noise.Next(Mathf.Lerp(1400f, 250f, t / 0.09f)) * Env(t, 0.001f, 0.025f) * 2f;
                float body = Mathf.Sin(TwoPi * 95f * t) * Env(t, 0.002f, 0.02f) * 0.5f;
                d[i] = SoftClip(scuff + body);
            }
            return Make(name, d, 0.3f);
        }

        static AudioClip Land()
        {
            const string name = "Land";
            var d = Buffer(0.18f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float thud = Mathf.Sin(TwoPi * Mathf.Lerp(90f, 45f, t / 0.18f) * t) * Env(t, 0.002f, 0.06f) * 1.5f;
                float grit = noise.Next(Mathf.Lerp(1800f, 180f, t / 0.18f)) * Env(t, 0.001f, 0.04f) * 2f;
                d[i] = SoftClip(thud + grit);
            }
            return Make(name, d, 0.6f);
        }

        /// <summary>Guard broken: a descending metallic crack. Should read as "something just gave way".</summary>
        static AudioClip PostureBreak()
        {
            const string name = "PostureBreak";
            var d = Buffer(0.7f);
            var noise = new LowpassNoise(Seed(name));
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / 0.7f;
                float f = Mathf.Lerp(900f, 180f, k * k);
                phase += TwoPi * f / Rate;
                float crack = (Mathf.Sin(phase) + 0.6f * Mathf.Sin(phase * 1.47f) + 0.35f * Mathf.Sin(phase * 2.13f))
                              * Env(t, 0.001f, 0.16f);
                float shatter = t < 0.03f ? noise.Next(7000f) * (1f - t / 0.03f) * 2f : 0f;
                float tail = noise.Next(110f) * Env(t, 0.03f, 0.3f) * 2.5f;
                d[i] = SoftClip(crack * 1.5f + shatter + tail);
            }
            return Make(name, d, 0.9f);
        }

        /// <summary>
        /// The Stormbreak item. Two halves: a broadband crack with a brutal attack, then a long dark
        /// rumble that wanders in level as it decays. This is the loudest thing in the game on purpose.
        /// </summary>
        static AudioClip Thunder()
        {
            const string name = "Thunder";
            const float dur = 2.2f;
            var d = Buffer(dur);
            var crack = new LowpassNoise(Seed(name));
            var rumble = new LowpassNoise(Seed(name) + 7);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                // the strike: near full range noise, gone in about a tenth of a second
                float snap = crack.Next(11000f) * Env(t, 0.0006f, 0.045f) * 3f;
                // sub underneath so it lands in the chest as well as the ears
                float thump = Mathf.Sin(TwoPi * 42f * t) * Env(t, 0.003f, 0.25f) * 1.3f;
                // the roll: two slow oscillators beating against each other keep it from sounding static
                float wobble = 0.5f + 0.5f * Mathf.Sin(TwoPi * 2.7f * t) * Mathf.Sin(TwoPi * 1.1f * t + 0.6f);
                float roll = rumble.Next(Mathf.Lerp(420f, 70f, k)) * Env(t, 0.02f, 0.75f) * wobble * 5f;
                d[i] = SoftClip(snap + thump + roll);
            }
            return Make(name, d, 0.95f);
        }

        static readonly float[] ChimeRatios = { 1f, 2f, 3.01f };
        static readonly float[] ChimeAmps = { 1f, 0.4f, 0.18f };

        /// <summary>Bright two note rise — deliberately clean and high, so a pickup cuts through the murk.</summary>
        static AudioClip ItemPickupChime()
        {
            const string name = "ItemPickup";
            var d = Buffer(0.35f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float lo = Partials(t, 880f, ChimeRatios, ChimeAmps, 0.11f, 0.2f) * Env(t, 0.003f, 0.2f);
                float t2 = t - 0.09f;
                float hi = t2 > 0f
                    ? Partials(t2, 1320f, ChimeRatios, ChimeAmps, 0.11f, 1.1f) * Env(t2, 0.003f, 0.2f)
                    : 0f;
                d[i] = (lo + hi) * 0.8f;
            }
            return Make(name, d, 0.7f);
        }

        /// <summary>Spending a charge: a short swell that falls away.</summary>
        static AudioClip ItemUseSwell()
        {
            const string name = "ItemUse";
            const float dur = 0.3f;
            var d = Buffer(dur);
            var noise = new LowpassNoise(Seed(name));
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float shape = Mathf.Sin(k * Mathf.PI);
                float f = Mathf.Lerp(900f, 180f, k * k);
                phase += TwoPi * f / Rate;
                float tone = Mathf.Sin(phase) * 0.5f;
                float air = noise.Next(Mathf.Lerp(2200f, 400f, k)) * 2.5f;
                d[i] = SoftClip((tone + air) * shape * 1.3f);
            }
            return Make(name, d, 0.7f);
        }

        /// <summary>
        /// The sentry dash (2026-09-06): a rising shimmer -- two detuned sines sweeping up an octave
        /// and a half with a bright noise tail, an implosion rather than the grapple's whoosh. Reads as
        /// "you blinked to it", which is what a 0.3 s pull to a staggered shooter is.
        /// </summary>
        static AudioClip Teleport()
        {
            const string name = "Teleport";
            const float dur = 0.42f;
            var d = Buffer(dur);
            var noise = new LowpassNoise(Seed(name));
            float p1 = 0f, p2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float f = Mathf.Lerp(320f, 1400f, k * k);
                p1 += TwoPi * f / Rate;
                p2 += TwoPi * f * 1.012f / Rate;
                float tone = (Mathf.Sin(p1) + Mathf.Sin(p2) * 0.7f) * 0.35f;
                float air = noise.Next(Mathf.Lerp(600f, 5200f, k)) * 1.6f * k;
                float env = Env(t, 0.03f, 0.16f) * (1f - k * 0.35f);
                d[i] = SoftClip((tone + air) * env * 1.4f);
            }
            return Make(name, d, 0.75f);
        }

        /// <summary>
        /// A stamina action DENIED (2026-09-06, audio pass): dash, wall run, wall jump and now the slide
        /// all refuse silently at the code level and only flash the HUD (GameEvents.StaminaRefused). A
        /// refused input with no sound reads as a dropped one. Deliberately dull and LOW -- a mechanism
        /// trying to engage and failing -- so it never competes with ParryCue's bright 1600 Hz band; the
        /// two must never be mistaken for each other under stress.
        /// </summary>
        static AudioClip Refuse()
        {
            const string name = "Refuse";
            var d = Buffer(0.14f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float clunk = Mathf.Sin(TwoPi * 95f * t) * Env(t, 0.001f, 0.03f) * 0.9f;
                float rattle = noise.Next(Mathf.Lerp(700f, 180f, Mathf.Clamp01(t / 0.1f))) * Env(t, 0.001f, 0.05f) * 1.3f;
                d[i] = SoftClip(clunk + rattle);
            }
            return Make(name, d, 0.55f);
        }

        static readonly float[] DetonateRatios = { 1f, 1.5f, 2.0f, 2.83f };
        static readonly float[] DetonateAmps = { 1f, 0.55f, 0.4f, 0.22f };

        /// <summary>
        /// A parkour sentry detonating (2026-09-06): a REWARD, not the storm. It shares no material with
        /// Sfx.Thunder on purpose -- Thunder is the Stormbreak item and must stay the loudest thing in the
        /// game; reusing it here for a routine posture break/kill would happen many times a level and burn
        /// out its impact. Bright and magical (rising tone + a burst), matching the violet-white VFX hue
        /// rather than Thunder's dark boom.
        /// </summary>
        static AudioClip Detonate()
        {
            const string name = "Detonate";
            const float dur = 0.55f;
            var d = Buffer(dur);
            var noise = new LowpassNoise(Seed(name));
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float f = Mathf.Lerp(220f, 60f, k * k);
                phase += TwoPi * f / Rate;
                float body = Partials(t, 340f, DetonateRatios, DetonateAmps, 0.16f, 0.9f) * Env(t, 0.002f, 0.2f);
                float sub = Mathf.Sin(phase) * Env(t, 0.004f, 0.3f) * 0.6f;
                float shimmer = t < 0.04f ? noise.Next(8000f) * (1f - t / 0.04f) * 1.6f : 0f;
                d[i] = SoftClip(body * 1.2f + sub + shimmer);
            }
            return Make(name, d, 0.85f);
        }

        /// <summary>
        /// The near-break "one more deflect" read, made audible (2026-09-06): a single dry creak fired
        /// the instant an enemy's posture crosses EnemyPostureBar.NearBreakRatio, not a repeating
        /// heartbeat -- a beat that fired every ~0.22 s per near-break enemy would spam the one-shot pool
        /// in any fight with more than one target near the edge. Quiet and short on purpose: it is a
        /// notification, and it must never compete with ParryCue.
        /// </summary>
        static AudioClip Tension()
        {
            const string name = "Tension";
            var d = Buffer(0.09f);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float creak = Mathf.Sin(TwoPi * Mathf.Lerp(2200f, 1500f, t / 0.09f) * t) * Env(t, 0.001f, 0.03f) * 0.6f;
                float grit = noise.Next(3000f) * Env(t, 0.0005f, 0.02f) * 0.8f;
                d[i] = SoftClip(creak + grit);
            }
            return Make(name, d, 0.45f);
        }

        /// <summary>
        /// A flask charge lost to a punish (2026-09-06): Interrupt() fires this ALONGSIDE the ordinary
        /// Sfx.Hurt PlayerCombat already plays for the hit that caused it, so a punished heal reads as
        /// "I got hit AND I wasted the charge" instead of an ordinary hit. A short liquid spill (a falling
        /// tone plus a splash of noise), not a repeat of Hurt's thump.
        /// </summary>
        static AudioClip Spill()
        {
            const string name = "Spill";
            const float dur = 0.32f;
            var d = Buffer(dur);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float glug = Mathf.Sin(TwoPi * Mathf.Lerp(500f, 140f, k) * t) * Env(t, 0.005f, 0.14f) * 0.7f;
                float splash = noise.Next(Mathf.Lerp(3400f, 900f, k)) * Env(t, 0.01f, 0.2f) * 1.6f;
                d[i] = SoftClip(glug + splash);
            }
            return Make(name, d, 0.6f);
        }

        // ------------------------------------------------------------------ per-weapon weight (2026-09-06 weapon audio pass)

        /// <summary>
        /// The dagger's swing: thin, fast, dry. No sub-bass at all — smallness is sold by the ABSENCE of
        /// low end, not by turning the sound down (per the brief: a weak-sounding weapon needs a missing
        /// layer diagnosed, never a volume bump). A quick metallic "shick" (the mechanical layer — steel
        /// leaving line, not a body swinging it) rides just ahead of a short bright air-slice body. Gone in
        /// well under a fifth of a second, matching the dagger's 0.22 s attackDuration and 4-hit combo.
        /// </summary>
        static AudioClip SwingLight()
        {
            const string name = "SwingLight";
            const float dur = 0.16f;
            var d = Buffer(dur);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = Mathf.Clamp01(t / dur);
                float shick = t < 0.02f ? noise.Next(Mathf.Lerp(6000f, 9000f, t / 0.02f)) * (1f - t / 0.02f) * 1.1f : 0f;
                float shape = Mathf.Sin(k * Mathf.PI);
                float whistle = noise.Next(Mathf.Lerp(1800f, 3600f, shape)) * shape * shape * 2.2f;
                d[i] = SoftClip(shick + whistle) * Env(t, 0.004f, dur * 0.5f);
            }
            return Make(name, d, 0.7f);
        }

        /// <summary>
        /// The hammer's swing: the mechanical layer comes FIRST and is deliberately the loudest part of
        /// the front half — a strained low creak that telegraphs the weight before the wind even starts,
        /// which is what makes a hammer read as dangerous through its long 0.86 s attackDuration rather
        /// than merely slow (per the brief: a mechanical sound before the strike builds more tension than
        /// the strike itself). Sub and body follow, both building toward the strike rather than static.
        /// </summary>
        static AudioClip SwingHeavy()
        {
            const string name = "SwingHeavy";
            const float dur = 0.5f;
            var d = Buffer(dur);
            var noise = new LowpassNoise(Seed(name));
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float creakWindow = dur * 0.35f;
                float creak = t < creakWindow
                    ? Mathf.Sin(TwoPi * Mathf.Lerp(70f, 45f, t / creakWindow) * t) * (1f - t / creakWindow) * 0.6f
                    : 0f;
                float subf = Mathf.Lerp(35f, 55f, k);
                phase += TwoPi * subf / Rate;
                float sub = Mathf.Sin(phase) * (k * k) * 1.3f;
                float shape = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
                float wind = noise.Next(Mathf.Lerp(90f, 500f, shape)) * shape * 3f;
                d[i] = SoftClip(creak + sub + wind) * Env(t, 0.02f, dur * 0.6f);
            }
            return Make(name, d, 0.85f);
        }

        /// <summary>
        /// The dagger's hit: a puncture, not a crunch — a thin sharp transient with almost no sub-bass, so
        /// a chain of fast stabs never blurs into the hammer's register. Distinct from Sfx.HitHeavy on the
        /// same axis Sfx.SwingLight is distinct from Sfx.SwingHeavy: absence of low end sells small.
        /// </summary>
        static AudioClip HitLight()
        {
            const string name = "HitLight";
            const float dur = 0.11f;
            var d = Buffer(dur);
            var noise = new LowpassNoise(Seed(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float puncture = noise.Next(Mathf.Lerp(7000f, 2000f, k)) * Env(t, 0.0004f, 0.02f) * 2.4f;
                float tik = Mathf.Sin(TwoPi * Mathf.Lerp(2400f, 900f, k) * t) * Env(t, 0.0008f, 0.03f) * 0.6f;
                d[i] = SoftClip(puncture + tik);
            }
            return Make(name, d, 0.75f);
        }

        /// <summary>
        /// The hammer's hit: the consequence layer the dagger deliberately lacks — a wide broadband crunch
        /// over a real sub boom, with a rumbling tail so a landed swing keeps announcing itself after the
        /// hitstop ends. A distinct register from Sfx.Hit (the sword's mid-weight default) so no two of
        /// the three weapons share a "connected" read.
        /// </summary>
        static AudioClip HitHeavy()
        {
            const string name = "HitHeavy";
            const float dur = 0.55f;
            var d = Buffer(dur);
            var noise = new LowpassNoise(Seed(name));
            var tailN = new LowpassNoise(Seed(name) + 5);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float crunch = noise.Next(Mathf.Lerp(3200f, 250f, Mathf.Clamp01(t / 0.09f))) * Env(t, 0.0006f, 0.08f) * 3f;
                float sub = Mathf.Sin(TwoPi * 42f * t) * Env(t, 0.003f, 0.28f) * 1.6f;
                float tail = tailN.Next(Mathf.Lerp(300f, 70f, k)) * Env(t, 0.03f, 0.4f) * 1.8f;
                d[i] = SoftClip(crunch + sub + tail);
            }
            return Make(name, d, 0.95f);
        }

        // ------------------------------------------------------------------ ambient loop

        static AudioClip Drone()
        {
            const string name = "Drone";
            const float dur = 6f;
            var d = Buffer(dur);
            int n = d.Length;
            var wind = new LowpassNoise(Seed(name));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float beat = 0.7f + 0.3f * Mathf.Sin(TwoPi * 0.12f * t);
                float a = Mathf.Sin(TwoPi * 55f * t) * 0.8f;
                float b = Mathf.Sin(TwoPi * 82.4f * t) * 0.6f * beat;
                float sub = Mathf.Sin(TwoPi * 41f * t) * 0.9f;
                float lfo = 0.5f + 0.5f * Mathf.Sin(TwoPi * 0.15f * t);
                float gust = wind.Next(Mathf.Lerp(120f, 600f, lfo)) * (0.6f + 1.4f * lfo) * 2f;
                d[i] = SoftClip(a + b + sub + gust);
            }
            // seamless loop: crossfade the last 0.5 s into the first 0.5 s
            int fade = Mathf.CeilToInt(0.5f * Rate);
            var outp = new float[n - fade];
            for (int i = 0; i < outp.Length; i++)
            {
                if (i < fade)
                {
                    float k = i / (float)fade;
                    outp[i] = d[i] * k + d[n - fade + i] * (1f - k);
                }
                else outp[i] = d[i];
            }
            return Make(name, outp, 0.6f);
        }
    }
}
