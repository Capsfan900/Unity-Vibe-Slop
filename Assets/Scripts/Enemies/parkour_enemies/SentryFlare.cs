using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The FLARE a sentry throws up when it breaks (2026-09-06, from the user: "instead of dash to them
    /// they explode shooting up a flare-like projectile that floats in an arc and fades away, but at any
    /// point it's still glowing the player can use it to grapple and it tosses them upward -- meant to be
    /// used creatively"). A glowing core with a short trail on a low-gravity arc; it fades over
    /// <see cref="life"/> seconds and is gone. While it glows it is a grapple point for
    /// <see cref="FlareGrapple"/>: pull to it, get tossed up. It is NOT an attack and never touches the
    /// player. Scaled time, like the bolt: hitstop freezes it with the world.
    /// </summary>
    public class SentryFlare : MonoBehaviour
    {
        /// <summary>Every flare currently in the world. FlareGrapple and the tests read this.</summary>
        public static readonly List<SentryFlare> Live = new List<SentryFlare>();

        /// <summary>Peak channel 1.6 -- the same peak as <see cref="Projectile.HotCore"/>, so brightness never
        /// carries the flare-vs-bolt distinction, only hue does. It is a TELL you steer toward, so it may bloom.
        /// The core and the <see cref="Halo"/> OVERLAP additively, so their peaks are budgeted TOGETHER:
        /// 1.6 + 0.4 lands exactly on the 2.0 ACES ceiling and never over it (pinned by FlareTests).</summary>
        public static readonly Color Core = new Color(1.18f, 0.88f, 1.6f, 1f);
        /// <summary>The halo's additive tint. Dim on purpose: it buys range with AREA, not brightness, and it is
        /// added on top of the core wherever the two overlap. Peak 0.4 = the 2.0 ceiling minus the core's 1.6.</summary>
        public static readonly Color Halo = new Color(0.29f, 0.21f, 0.4f, 1f);
        /// <summary>2026-09-06 VFX pass, from the user: "the flare needs to be much larger and more visible" --
        /// it is a usable traversal tool, not a decoration, so it has to read at 30 m against a dark sky.
        /// More than double the 0.5 m it shipped at.</summary>
        public const float CoreSize = 1.15f;
        public const float TrailSeconds = 0.32f;
        /// <summary>A wide, dim halo behind the hot core: the core alone is a dot at range, the halo is what
        /// makes it a beacon. Additive, so it only ever adds light.</summary>
        public const float HaloScale = 2.6f;
        /// <summary>Seconds between halo pulses (a soft SlashFx.Flare re-fired at the flare's own position):
        /// the standing glow reads as a point, the pulse reads as a THING you can go find.</summary>
        public const float PulseInterval = 0.55f;
        public const float PulseSize = 2.0f;
        public const float PulseSeconds = 0.4f;
        /// <summary>Below this glow the flare no longer takes a grapple: a dying ember is not a hook.</summary>
        public const float MinGrappleGlow = 0.08f;
        /// <summary>At most this many flares may run the periodic <see cref="SlashFx.Flare"/> pulse. SlashFx is a
        /// SHARED pool (MaxLive 28; Spawn returns null at the cap): a span full of flares pulsing forever would
        /// starve the combat tells that must never be dropped. Past this count the standing core+halo IS the
        /// read -- 1.15 m of core inside a 2.6 m halo does not need a pulse to be found.</summary>
        public const int PulseLiveBudget = 2;

        /// <summary>May a flare spend a pooled SlashFx slot on its idle pulse right now?</summary>
        public static bool PulseAllowed(int liveCount) { return liveCount <= PulseLiveBudget; }

        /// <summary>The slow alive-flicker, applied EXACTLY ONCE to each of the core and the halo.</summary>
        public static float Flicker(float age) { return 1f + 0.12f * Mathf.Sin(age * 23f); }

        /// <summary>The core sphere's WORLD diameter: down to a quarter as the ember dies.</summary>
        public static float CoreWorldDiameter(float glow, float flicker) { return CoreSize * Mathf.Lerp(0.25f, 1f, glow) * flicker; }

        /// <summary>The halo's WORLD diameter. It never shrinks as far as the core, so even a dying ember keeps a
        /// faint aura instead of collapsing to a pinprick. Driven in WORLD space precisely because the halo is a
        /// CHILD of the sphere the core scaling writes to -- multiplying the two would square both the glow curve
        /// and the flicker.</summary>
        public static float HaloWorldDiameter(float glow, float flicker) { return HaloScale * Mathf.Lerp(0.5f, 1f, glow) * flicker; }

        static Material coreMat;

        float life = 4.5f, gravity = 4f;
        Vector3 origin, velocity;
        float t;
        bool spent;
        Renderer core;
        Transform halo;
        LineRenderer trail;
        MaterialPropertyBlock mpb;
        MaterialPropertyBlock trailMpb;
        float pulseTimer;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static Material haloMat;

        public float Age => t;
        public float Life => life;
        public float Glow => FlareMath.Glow(t, life);
        public bool Grappleable => !spent && FlareMath.Grappleable(t, life, MinGrappleGlow);
        public Vector3 Velocity => velocity + Vector3.down * gravity * t;

        /// <summary>Make one. <paramref name="facing"/> is the sentry's flat forward: the flare drifts that way.</summary>
        public static SentryFlare Spawn(Vector3 from, Vector3 facing, float upSpeed, float outSpeed, float gravity, float lifeSeconds)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "SentryFlare";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.position = from;
            go.transform.localScale = Vector3.one * CoreSize;
            if (coreMat == null)
            {
                coreMat = SlashFx.CreateAdditiveMaterial(new Color(0.75f, 0.55f, 1f, 1f));
                if (coreMat.HasProperty("_BaseColor")) coreMat.SetColor("_BaseColor", Core);
                if (coreMat.HasProperty("_Color")) coreMat.SetColor("_Color", Core);
            }
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = coreMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            // A second, much larger, much dimmer sphere behind the core: a hot pinpoint reads as a dot past a
            // few metres, the soft halo around it is what makes the flare a BEACON at range. Its own additive
            // material so it never pushes the core's colour.
            if (haloMat == null)
            {
                haloMat = SlashFx.CreateAdditiveMaterial(new Color(0.75f, 0.55f, 1f, 1f));
                if (haloMat.HasProperty("_BaseColor")) haloMat.SetColor("_BaseColor", Halo);
                if (haloMat.HasProperty("_Color")) haloMat.SetColor("_Color", Halo);
            }
            var haloGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            haloGo.name = "Halo";
            var haloCol = haloGo.GetComponent<Collider>();
            if (haloCol != null) Destroy(haloCol);
            haloGo.transform.SetParent(go.transform, false);
            // LOCAL scale chosen so the WORLD diameter is right under the core's own scale at birth (glow 1,
            // flicker 1). Update divides the parent out again every frame.
            haloGo.transform.localScale = Vector3.one * (HaloWorldDiameter(1f, 1f) / CoreWorldDiameter(1f, 1f));
            var haloR = haloGo.GetComponent<Renderer>();
            haloR.sharedMaterial = haloMat;
            haloR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            haloR.receiveShadows = false;

            var f = go.AddComponent<SentryFlare>();
            f.halo = haloGo.transform;
            f.origin = from;
            f.velocity = FlareMath.LaunchVelocity(facing, upSpeed, outSpeed);
            f.gravity = Mathf.Max(0f, gravity);
            f.life = Mathf.Max(0.5f, lifeSeconds);
            f.core = r;
            f.trail = SlashFx.CreateLine(go.transform, "Trail", 2, CoreSize * 0.45f, 0.03f, false, coreMat);
            f.trail.useWorldSpace = true;
            f.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            f.trail.receiveShadows = false;
            return f;
        }

        void OnEnable() { if (!Live.Contains(this)) Live.Add(this); }
        void OnDisable() { Live.Remove(this); }

        void Update()
        {
            if (spent) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t += dt;
            if (t >= life) { Spend(); return; }

            transform.position = FlareMath.Position(origin, velocity, gravity, t);
            float g = Glow;
            // The glow IS the size and the colour: full and violet-white at birth, a shrinking ember at the end,
            // with a slow flicker so it reads as alive against a dark sky.
            float flicker = Flicker(t);
            float coreD = CoreWorldDiameter(g, flicker);
            transform.localScale = Vector3.one * coreD;
            Color lit = Color.Lerp(new Color(0.5f, 0.2f, 0.3f, 1f), Core, g);
            if (core != null)
            {
                if (mpb == null) mpb = new MaterialPropertyBlock();
                core.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, lit);
                core.SetPropertyBlock(mpb);
            }
            if (halo != null && coreD > 1e-5f)
            {
                // WORLD-space drive: the halo is a child of the sphere we just rescaled, so its local scale has
                // the parent divided out. Left compounded, the glow curve and the flicker each applied TWICE and
                // the "never a pinprick" halo collapsed to one at low glow.
                halo.localScale = Vector3.one * (HaloWorldDiameter(g, flicker) / coreD);
            }
            if (trail != null)
            {
                Vector3 head = transform.position;
                trail.SetPosition(0, head);
                trail.SetPosition(1, head - Velocity * TrailSeconds * Mathf.Max(0.2f, g));
                // The trail shares the core's material, so it needs its own property block: without one it stayed
                // at full birth colour while the flare it belongs to dimmed away to nothing.
                if (trailMpb == null) trailMpb = new MaterialPropertyBlock();
                trail.GetPropertyBlock(trailMpb);
                trailMpb.SetColor(BaseColorId, lit);
                trail.SetPropertyBlock(trailMpb);
            }

            // A soft pulse re-announces the flare at intervals: the standing glow reads as a point at 30 m,
            // the pulse reads as a THING worth going to find. Only while it still glows enough to be a hook, and
            // only while few enough flares are live that the shared SlashFx pool can afford the slots.
            pulseTimer += dt;
            if (pulseTimer >= PulseInterval && g > MinGrappleGlow && PulseAllowed(Live.Count))
            {
                pulseTimer = 0f;
                SlashFx.Flare(transform.position, new Color(0.85f, 0.7f, 1f, 1f), PulseSize * Mathf.Lerp(0.4f, 1f, g), PulseSeconds);
            }
        }

        /// <summary>Used by a grapple: a burst of sparks, and gone.</summary>
        public void Consume()
        {
            if (spent) return;
            SlashFx.Sparks(transform.position, Vector3.up, new Color(0.8f, 0.6f, 1f, 1f), 12, 6f, 120f);
            SlashFx.Flare(transform.position, new Color(0.85f, 0.7f, 1f, 1f), 0.9f, 0.18f);
            Spend();
        }

        void Spend()
        {
            if (spent) return;
            spent = true;
            Live.Remove(this);
            Destroy(gameObject);
        }
    }
}
