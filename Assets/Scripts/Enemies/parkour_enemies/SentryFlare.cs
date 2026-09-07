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

        /// <summary>The violet-white hot core. It is a TELL you steer toward, so it may bloom.
        ///
        /// <para><b>2026-09-06 polish pass, from the user: "make it bigger and look more polished."</b> The
        /// core grew 1.15 -> 1.40 m and the aura 2.6 -> 3.6 m, and the PEAK CAME DOWN to pay for it:
        /// 1.6 -> 1.45. Three shells now overlap additively and are budgeted together — 1.45 core + 0.24
        /// inner halo + 0.12 outer falloff = 1.81, under the 2.0 ACES ceiling with room the old
        /// exactly-2.0 stack did not have.</para>
        ///
        /// <para><b>Dropping under the bolt is the point, not a compromise.</b> The flare used to sit at
        /// exactly <see cref="Projectile.HotCore"/>'s 1.6, on the argument that hue alone should carry
        /// flare-vs-bolt. Bigger AND equally bright would have made a grapple point out-read the one
        /// object on the span that can kill you. So the hierarchy is now explicit and one-directional:
        /// <b>the BOLT is the brightest thing (1.6) and the FLARE is the biggest thing (3.6 m of aura).</b>
        /// Brightness says "answer this now"; area says "come and find this". Hue still separates them —
        /// amber against violet — but it is no longer the only channel doing the work.</para>
        ///
        /// <para>2026-09-06 cold pass, unchanged in ratio: the flare stays VIOLET rather than going cold
        /// with the world. Its blue channel is the world's dominant channel, so the red lift is what
        /// restores the gap — nothing in the cold palette has a strong red channel. Turning the flare
        /// azure would have made a grapple point indistinguishable from a wall.</para></summary>
        public static readonly Color Core = new Color(1.178f, 0.707f, 1.45f, 1f);
        /// <summary>The inner halo's additive tint. Dim on purpose: it buys range with AREA, not brightness,
        /// and it is added on top of the core wherever the two overlap.</summary>
        public static readonly Color Halo = new Color(0.195f, 0.117f, 0.24f, 1f);
        /// <summary>The OUTER falloff shell (2026-09-06 polish). One additive sphere has a hard, obviously
        /// spherical edge against the sky; two concentric shells at 0.24 and 0.12 give the aura a STEP of
        /// falloff, which is what reads as a soft bloom rather than as a ball. It is also the rim that
        /// separates the flare from a bright patch of nebula behind it.</summary>
        public static readonly Color Outer = new Color(0.0975f, 0.0585f, 0.12f, 1f);
        /// <summary>2026-09-06 VFX pass, from the user: "the flare needs to be much larger and more visible" --
        /// it is a usable traversal tool, not a decoration, so it has to read at 30 m against a dark sky.
        /// Nearly triple the 0.5 m it shipped at; +22% in the 2026-09-06 polish pass.</summary>
        public const float CoreSize = 1.40f;
        public const float TrailSeconds = 0.32f;
        /// <summary>Points in the trail. It used to be TWO — head plus head-minus-velocity — which draws a
        /// straight tangent along a PARABOLIC path: the streak pointed where the flare had never been. Six
        /// points recorded from the real arc curve with it. Allocation-free after birth (a ring buffer).</summary>
        public const int TrailPoints = 6;
        /// <summary>A wide, dim halo behind the hot core: the core alone is a dot at range, the halo is what
        /// makes it a beacon. Additive, so it only ever adds light.</summary>
        public const float HaloScale = 2.30f;
        /// <summary>The outer falloff shell's diameter. This is the number that makes the flare "bigger".</summary>
        public const float OuterScale = 3.60f;
        /// <summary>Seconds of ARRIVAL. A flare used to appear at full size on frame one, which reads as a
        /// pop rather than as a launch. It now snaps out from 0.18x, overshoots ~6% and settles —
        /// anticipation, snap, settle, the same three beats every other effect in this game obeys.</summary>
        public const float SpawnPopSeconds = 0.18f;
        /// <summary>Seconds of DEPARTURE. The last beat of a flare's life contracts faster than the glow
        /// curve alone would, so an expiring flare reads as a decision rather than as a fade-out that
        /// happens to reach zero. It stays SILENT: nothing happened, so nothing announces itself. Only
        /// <see cref="Consume"/> — an actual event — gets a bang.</summary>
        public const float DeathContractSeconds = 0.35f;
        /// <summary>The ring thrown when a grapple consumes a flare: the same "an event happened HERE"
        /// device <c>SentryBurst</c> uses, so the two halves of the mechanic speak the same language.</summary>
        public const float ConsumeRingRadius = 1.6f;
        public const float ConsumeRingSeconds = 0.3f;
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
        /// read -- 1.40 m of core inside a 3.6 m aura does not need a pulse to be found.</summary>
        public const int PulseLiveBudget = 2;

        /// <summary>May a flare spend a pooled SlashFx slot on its idle pulse right now?</summary>
        public static bool PulseAllowed(int liveCount) { return liveCount <= PulseLiveBudget; }

        /// <summary>The slow alive-flicker, applied EXACTLY ONCE to each of the core and the halo.</summary>
        public static float Flicker(float age) { return 1f + 0.12f * Mathf.Sin(age * 23f); }

        /// <summary>The OUTER shell's own, much slower breath (2026-09-06 polish). A spin was the obvious
        /// idea and is a lie: an untextured additive sphere rotating looks like an untextured additive
        /// sphere. Secondary motion the eye can actually see has to change the SILHOUETTE, so the outer
        /// aura swells and settles at ~0.5 Hz against the core's 3.7 Hz flicker. Two rates, one object.</summary>
        public static float OuterBreath(float age) { return 1f + 0.09f * Mathf.Sin(age * 3.1f); }

        /// <summary>ARRIVAL. 0 -> 0.18 s: starts at 0.18x, snaps out past full (peak ~1.06x at ~0.14 s)
        /// and settles on 1. Anticipation, snap, settle. Returns exactly 1 for every age past
        /// <see cref="SpawnPopSeconds"/>, so it costs nothing after the first fifth of a second.</summary>
        public static float SpawnPop(float age)
        {
            if (age >= SpawnPopSeconds) return 1f;
            float k = Mathf.Clamp01(age / SpawnPopSeconds);
            // one damped overshoot: 0.18 -> 1.14 -> 1.0
            return 0.18f + 0.96f * Mathf.Sin(k * Mathf.PI * 0.5f) + 0.14f * Mathf.Sin(k * Mathf.PI) - 0.14f * k;
        }

        /// <summary>DEPARTURE. 1 until the last <see cref="DeathContractSeconds"/>, then eased to 0. Applied
        /// on top of the glow curve so the end of a flare's life is a contraction, not just a dimming.</summary>
        public static float DeathContract(float age, float lifeSeconds)
        {
            float left = lifeSeconds - age;
            if (left >= DeathContractSeconds) return 1f;
            if (left <= 0f) return 0f;
            float k = left / DeathContractSeconds;
            return k * k;
        }

        /// <summary>The core sphere's WORLD diameter: down to a quarter as the ember dies. The arrival pop
        /// and the death contraction are separate multipliers applied by Update, deliberately — they are
        /// functions of AGE, not of glow, and folding them in here would have made this untestable.</summary>
        public static float CoreWorldDiameter(float glow, float flicker) { return CoreSize * Mathf.Lerp(0.25f, 1f, glow) * flicker; }

        /// <summary>The halo's WORLD diameter. It never shrinks as far as the core, so even a dying ember keeps a
        /// faint aura instead of collapsing to a pinprick. Driven in WORLD space precisely because the halo is a
        /// CHILD of the sphere the core scaling writes to -- multiplying the two would square both the glow curve
        /// and the flicker.</summary>
        public static float HaloWorldDiameter(float glow, float flicker) { return HaloScale * Mathf.Lerp(0.5f, 1f, glow) * flicker; }

        /// <summary>The outer falloff shell's WORLD diameter. Same world-space rule as the halo, and it
        /// shrinks LESS than either (0.65 floor) so the biggest, softest part of the flare is the last
        /// thing to go — which is what keeps a nearly dead flare findable while it is still grappleable.</summary>
        public static float OuterWorldDiameter(float glow, float breath) { return OuterScale * Mathf.Lerp(0.65f, 1f, glow) * breath; }

        static Material coreMat;
        static Material outerMat;

        float life = 4.5f, gravity = 4f;
        Vector3 origin, velocity;
        float t;
        bool spent;
        Renderer core;
        Transform halo;
        Transform outer;
        Vector3[] trailBuf;
        float trailTimer;
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

            // The OUTER falloff shell. Same construction as the halo, one step wider and one step dimmer:
            // two concentric additive spheres give the aura a soft STEP rather than one hard sphere edge.
            if (outerMat == null)
            {
                outerMat = SlashFx.CreateAdditiveMaterial(new Color(0.75f, 0.55f, 1f, 1f));
                if (outerMat.HasProperty("_BaseColor")) outerMat.SetColor("_BaseColor", Outer);
                if (outerMat.HasProperty("_Color")) outerMat.SetColor("_Color", Outer);
            }
            var outerGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            outerGo.name = "Outer";
            var outerCol = outerGo.GetComponent<Collider>();
            if (outerCol != null) Destroy(outerCol);
            outerGo.transform.SetParent(go.transform, false);
            outerGo.transform.localScale = Vector3.one * (OuterWorldDiameter(1f, 1f) / CoreWorldDiameter(1f, 1f));
            var outerR = outerGo.GetComponent<Renderer>();
            outerR.sharedMaterial = outerMat;
            outerR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outerR.receiveShadows = false;

            var f = go.AddComponent<SentryFlare>();
            f.halo = haloGo.transform;
            f.outer = outerGo.transform;
            f.origin = from;
            f.velocity = FlareMath.LaunchVelocity(facing, upSpeed, outSpeed);
            f.gravity = Mathf.Max(0f, gravity);
            f.life = Mathf.Max(0.5f, lifeSeconds);
            f.core = r;
            f.trail = SlashFx.CreateLine(go.transform, "Trail", TrailPoints, CoreSize * 0.45f, 0.03f, false, coreMat);
            f.trailBuf = new Vector3[TrailPoints];
            for (int i = 0; i < TrailPoints; i++) { f.trailBuf[i] = from; f.trail.SetPosition(i, from); }
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
            // Arrival and departure are functions of AGE, not of glow, and they multiply everything the
            // flare draws: the core, both aura shells and the trail's width all snap out together and all
            // contract together. That is what makes a birth read as a launch and a death as a decision
            // rather than as two independent fades that happen to finish at the same time.
            float shape = SpawnPop(t) * DeathContract(t, life);
            float coreD = CoreWorldDiameter(g, flicker) * shape;
            if (coreD < 1e-4f) coreD = 1e-4f;
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
                halo.localScale = Vector3.one * (HaloWorldDiameter(g, flicker) * shape / coreD);
            }
            if (outer != null && coreD > 1e-5f)
            {
                // Its OWN, slower breath -- not the core's flicker. Two rates on one object is the whole
                // "polished" read: a hot centre that trembles inside an aura that swells.
                outer.localScale = Vector3.one * (OuterWorldDiameter(g, OuterBreath(t)) * shape / coreD);
            }
            if (trail != null && trailBuf != null)
            {
                // A RECORDED history, not a straight tangent. The flare flies a parabola; the old two-point
                // streak pointed along the current velocity, i.e. at a place the flare had never been and
                // was never going. Points are re-sampled on a fixed interval and the buffer is shifted, so
                // this allocates nothing and the ribbon curves with the arc.
                Vector3 head = transform.position;
                trailBuf[0] = head;
                trailTimer += dt;
                float step = TrailSeconds / Mathf.Max(1, TrailPoints - 1);
                if (trailTimer >= step)
                {
                    trailTimer = 0f;
                    for (int i = TrailPoints - 1; i > 1; i--) trailBuf[i] = trailBuf[i - 1];
                    trailBuf[1] = head;
                }
                for (int i = 0; i < TrailPoints; i++) trail.SetPosition(i, trailBuf[i]);
                // NO widthMultiplier here. The trail is a CHILD of the sphere whose scale is coreD, and a
                // LineRenderer's width already scales with its transform -- the ribbon tapers with the
                // core for free. Multiplying again was the halo's compounding bug in a second place.
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
            // A ring, a flash and sparks -- the same three-part shape SentryBurst uses, so "a flare was
            // spent" and "a sentry broke" read as one family of event. The ring is the part still growing
            // after the flash has popped, which is what makes it legible from outside the sparks' travel.
            SlashFx.Ring(transform.position, Vector3.up, new Color(0.72f, 0.55f, 1f, 1f), ConsumeRingRadius, ConsumeRingSeconds);
            SlashFx.Sparks(transform.position, Vector3.up, new Color(0.8f, 0.6f, 1f, 1f), 12, 6f, 120f);
            SlashFx.Flare(transform.position, new Color(0.85f, 0.7f, 1f, 1f), 1.2f, 0.2f);
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
