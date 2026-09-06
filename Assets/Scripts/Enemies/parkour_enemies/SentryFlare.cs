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

        /// <summary>Peak channel 1.6 like the bolt core: the flare is a TELL you steer toward, so it may bloom.</summary>
        public static readonly Color Core = new Color(1.2f, 0.9f, 1.6f, 1f);
        public const float CoreSize = 0.5f;
        public const float TrailSeconds = 0.18f;
        /// <summary>Below this glow the flare no longer takes a grapple: a dying ember is not a hook.</summary>
        public const float MinGrappleGlow = 0.08f;

        static Material coreMat;

        float life = 4.5f, gravity = 4f;
        Vector3 origin, velocity;
        float t;
        bool spent;
        Renderer core;
        LineRenderer trail;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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

            var f = go.AddComponent<SentryFlare>();
            f.origin = from;
            f.velocity = FlareMath.LaunchVelocity(facing, upSpeed, outSpeed);
            f.gravity = Mathf.Max(0f, gravity);
            f.life = Mathf.Max(0.5f, lifeSeconds);
            f.core = r;
            f.trail = SlashFx.CreateLine(go.transform, "Trail", 2, CoreSize * 0.5f, 0.02f, false, coreMat);
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
            if (trail != null)
            {
                Vector3 head = transform.position;
                trail.SetPosition(0, head);
                trail.SetPosition(1, head - Velocity * TrailSeconds * Mathf.Max(0.2f, g));
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
