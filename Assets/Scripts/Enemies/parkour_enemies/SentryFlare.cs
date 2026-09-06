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

        /// <summary>Peak channel 1.9, under the 2.0 ACES ceiling: the flare is a TELL you steer toward, so it may
        /// bloom, and it is the brightest violet thing on a span since nothing else claims that hue.</summary>
        public static readonly Color Core = new Color(1.4f, 1.05f, 1.9f, 1f);
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

        static Material coreMat;

        float life = 4.5f, gravity = 4f;
        Vector3 origin, velocity;
        float t;
        bool spent;
        Renderer core;
        Transform halo;
        LineRenderer trail;
        MaterialPropertyBlock mpb;
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
                if (haloMat.HasProperty("_BaseColor")) haloMat.SetColor("_BaseColor", new Color(0.55f, 0.4f, 0.75f, 1f));
            }
            var haloGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            haloGo.name = "Halo";
            var haloCol = haloGo.GetComponent<Collider>();
            if (haloCol != null) Destroy(haloCol);
            haloGo.transform.SetParent(go.transform, false);
            haloGo.transform.localScale = Vector3.one * HaloScale;
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
            if (t >= life) { Spend(false); return; }

            transform.position = FlareMath.Position(origin, velocity, gravity, t);
            float g = Glow;
            // The glow IS the size and the colour: full and violet-white at birth, a shrinking ember at the end,
            // with a slow flicker so it reads as alive against a dark sky.
            float flicker = 1f + 0.12f * Mathf.Sin(t * 23f);
            transform.localScale = Vector3.one * CoreSize * Mathf.Lerp(0.25f, 1f, g) * flicker;
            if (core != null)
            {
                if (mpb == null) mpb = new MaterialPropertyBlock();
                core.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, Color.Lerp(new Color(0.5f, 0.2f, 0.3f, 1f), Core, g));
                core.SetPropertyBlock(mpb);
            }
            if (halo != null)
            {
                // The halo breathes with the same flicker but never shrinks as far as the core: even a
                // dying ember keeps a faint aura so it is never a pinprick right up to the moment it dies.
                halo.localScale = Vector3.one * HaloScale * Mathf.Lerp(0.5f, 1f, g) * flicker;
            }
            if (trail != null)
            {
                Vector3 head = transform.position;
                trail.SetPosition(0, head);
                trail.SetPosition(1, head - Velocity * TrailSeconds * Mathf.Max(0.2f, g));
            }

            // A soft pulse re-announces the flare at intervals: the standing glow reads as a point at 30 m,
            // the pulse reads as a THING worth going to find. Only while it still glows enough to be a hook.
            pulseTimer += dt;
            if (pulseTimer >= PulseInterval && g > MinGrappleGlow)
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
            Spend(true);
        }

        void Spend(bool consumed)
        {
            if (spent) return;
            spent = true;
            Live.Remove(this);
            Destroy(gameObject);
        }
    }
}
