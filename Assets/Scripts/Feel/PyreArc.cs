using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1
{
    /// <summary>
    /// THE PYRE DISCHARGE. One gesture, made of two halves that have to be seen together:
    ///
    /// <list type="number">
    /// <item><b>A bundle of lightning.</b> Five jagged channels braided into a rope, leaving the weapon
    /// tip and TERMINATING INSIDE the victim's contact point — see
    /// <see cref="LightningEffect.Bundle"/>. Not one beam (which reads as a laser) and not a fan of
    /// straight lines (which reads as a diagram).</item>
    /// <item><b>A misty flow riding the same channel, the wrong way round.</b> A soul-drain effect
    /// pulls mist out of a body and into you; this is that language REVERSED — the charge is pushed
    /// out of the weapon and driven into the enemy. Same soft billboards
    /// <see cref="DeathMist"/> already uses for a death, so the two effects speak the same dialect;
    /// only the direction and the shape of the emitter differ.</item>
    /// </list>
    ///
    /// <para><b>Why both.</b> Lightning alone is instantaneous and reads as a flashbulb — you see that
    /// something happened, not that something was DELIVERED. Mist alone has no author. The bolts give
    /// the moment its spine and the mist gives it duration and volume, and because both are anchored
    /// to the same two points the eye reads one arrow: weapon, gap, enemy.</para>
    ///
    /// <para>Rule 1: everything below runs on unscaled time, so the hitstop that fires on the same
    /// frame as the discharge cannot freeze the effect that explains it.</para>
    ///
    /// <para>Rule 9 note: these are FEEL CONSTANTS, not content. Nothing here is serialised onto a
    /// ScriptableObject, so there is no shipped asset that can hold a stale copy — the same reasoning
    /// <see cref="UltimateAbility"/> documents for its own bolt constants. They are asserted in
    /// <c>Assets/Editor/Tests/PyreArcTests.cs</c> so a change to any of them is a deliberate one.</para>
    /// </summary>
    public static class PyreArc
    {
        /// <summary>Below this the bundle is shorter than the weapon and is skipped entirely.</summary>
        public const float MinSpan = 0.35f;

        /// <summary>
        /// Fire one discharge from <paramref name="from"/> (a weapon or wand tip) into
        /// <paramref name="to"/> (the victim's contact point — the SURFACE facing the player, never
        /// its centre of mass, or the whole thing draws inside the mesh and is never seen).
        /// </summary>
        public static void Cast(Vector3 from, Vector3 to, Color color, float scale = 1f)
        {
            Vector3 axis = to - from;
            float dist = axis.magnitude;
            if (dist < MinSpan) return;

            float s = Mathf.Clamp(scale, 0.25f, 3f);
            LightningEffect.Bundle(from, to, color, s);
            PyreMist.Stream(from, to, color, s);
        }
    }

    /// <summary>
    /// The misty half of <see cref="PyreArc"/>: soft billboards launched OUT of the weapon and driven
    /// down the same channel the bolts take, arriving in the enemy as they fade.
    ///
    /// <para><b>Why a ParticleSystem and not more LineRenderers.</b> Exactly the argument
    /// <see cref="DeathMist"/> makes: mist is many soft overlapping billboards, which is one draw call
    /// and a worker-thread simulation here, versus N main-thread transform writes there. The bolts are
    /// LineRenderers because they are thin travelling shapes; the mist is not.</para>
    ///
    /// <para><b>It shares DeathMist's material and texture</b> rather than making a second pair. That
    /// is not only cheaper — it is why the Pyre's mist and a death's mist look like the same substance.
    /// The share is refcounted (<see cref="DeathMist.RetainShared"/>) because the material is destroyed
    /// with its last holder.</para>
    ///
    /// <para>Every mote is SEEDED at its own fraction of the way along the span and given exactly the
    /// lifetime it needs to finish the trip, so the whole channel is full from the first frame and the
    /// far end drains into the body first. That is what makes the mist converge on the enemy instead of
    /// drifting near it: the arrival is not a coincidence, it is the arithmetic. Emitting the burst
    /// from the tip instead produces one clump crawling up the span with a gap on either side of it —
    /// which is what the first capture of this effect actually showed.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class PyreMist : MonoBehaviour
    {
        /// <summary>Discharges are discrete events, never per-frame. Three emitters covers overlap.</summary>
        const int PoolSize = 3;
        const int MaxParticles = 220;

        /// <summary>Motes per discharge. Enough to read as a flow, few enough to stay a wisp.</summary>
        public const int BurstCount = 34;

        /// <summary>
        /// Seconds a mote spends crossing the gap. Matched to <see cref="LightningEffect.BundleSeconds"/>
        /// so the mist lands with the bolts rather than trailing in after them.
        /// </summary>
        public const float Flight = 0.30f;

        /// <summary>Speed spread around the solved crossing speed. Motes arrive over a short window.</summary>
        public const float SpeedMin = 0.85f, SpeedMax = 1.10f;

        /// <summary>Half-angle of the emission cone, degrees. Narrow: a channel, not a shotgun.</summary>
        public const float ConeAngle = 7f;

        static readonly List<PyreMist> pool = new List<PyreMist>(PoolSize);
        static int roundRobin;

        ParticleSystem ps;

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Metres per second a mote needs to cover <paramref name="distance"/> inside
        /// <see cref="Flight"/>. Pure, so an EditMode test can prove the mist actually reaches the
        /// enemy instead of a human squinting at it.
        /// </summary>
        public static float SpeedFor(float distance)
        {
            return Mathf.Max(0f, distance) / Flight;
        }

        /// <summary>
        /// Push a stream of mist from <paramref name="from"/> into <paramref name="to"/>.
        /// </summary>
        public static void Stream(Vector3 from, Vector3 to, Color tint, float scale)
        {
            Vector3 axis = to - from;
            float dist = axis.magnitude;
            if (dist < 0.05f) return;

            var fx = Rent();
            if (fx == null || fx.ps == null) return;

            float s = Mathf.Clamp(scale, 0.25f, 3f);

            fx.transform.position = from;
            // The cone emits along its own +Z, so aiming the emitter IS aiming the flow. No
            // velocity-over-lifetime module needed, and the simulation stays in world space so the
            // mist keeps going where it was thrown even if the emitter is reused next frame.
            fx.transform.rotation = Quaternion.LookRotation(axis / dist, Vector3.up);

            // Re-fetched every burst on purpose: the shared material is refcounted and a scene teardown
            // between two discharges can have replaced it. A renderer pointing at a destroyed material
            // draws NOTHING — silently, with no magenta to warn you.
            var psr = fx.GetComponent<ParticleSystemRenderer>();
            if (psr != null) psr.sharedMaterial = DeathMist.MistMaterial();

            Vector3 dir = axis / dist;
            float speed = SpeedFor(dist);
            Color hue = Tint(tint);

            // Curl scaled to the span: a 2 m discharge should wisp by centimetres, a 12 m one by more,
            // or the same absolute wobble reads as a straight pipe at range and as chaos up close.
            var noise = fx.ps.noise;
            noise.strength = new ParticleSystem.MinMaxCurve(Mathf.Clamp(dist * 0.10f, 0.10f, 0.9f) * s);

            if (!fx.ps.isPlaying) fx.ps.Play();

            // ---- SEEDED ALONG THE CHANNEL, NOT FIRED OUT OF THE END ----------------------------
            // Emitting the whole burst from the tip produces one CLUMP of mist that crawls up the
            // span — photographed, it is a ball with a gap on either side of it, not a flow. Instead
            // every mote is placed at its own fraction of the way along and given exactly the
            // lifetime it needs to finish the rest of the trip, so:
            //   * at t=0 the whole channel is already full, which is what reads as a stream;
            //   * every mote still ARRIVES at the enemy, none of them stall in mid-air;
            //   * the far end empties first and the flow drains INTO the body — the soul-suck
            //     silhouette, running the other way, which is the whole brief.
            //
            // One Emit per mote rather than one call for the burst. A discharge is a discrete event,
            // never a per-frame cost, so ~30 struct calls buys the shape for nothing that matters.
            int count = Mathf.RoundToInt(BurstCount * Mathf.Clamp(s, 0.6f, 1.6f));
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                float k = (i + Random.Range(0f, 0.9f)) / count;   // fraction already travelled
                float remaining = Mathf.Max(0.05f, Flight * (1f - k));

                // Widen with distance from the tip, the way a jet does. Ties the mist to the same
                // pinched-at-both-ends silhouette the bolt bundle has.
                float spray = Mathf.Tan(ConeAngle * Mathf.Deg2Rad) * dist * k + 0.05f * s;
                Vector3 off = Vector3.ProjectOnPlane(Random.insideUnitSphere, dir) * spray;

                ep.position = from + axis * k + off;
                ep.velocity = dir * speed * Random.Range(SpeedMin, SpeedMax);
                ep.startLifetime = remaining;
                ep.startSize = Random.Range(0.12f, 0.32f) * s;
                ep.startColor = hue;
                ep.rotation = Random.Range(0f, 360f);
                fx.ps.Emit(ep, 1);
            }
        }

        // ------------------------------------------------------------------ pooling

        static PyreMist Rent()
        {
            EnsurePool();

            PyreMist fallback = null;
            int fewest = int.MaxValue;
            for (int i = 0; i < pool.Count; i++)
            {
                var m = pool[i];
                if (m == null || m.ps == null) continue;
                int c = m.ps.particleCount;
                if (c == 0) return m;
                if (c < fewest) { fewest = c; fallback = m; }
            }
            if (fallback != null) return fallback;

            if (pool.Count == 0) return null;
            roundRobin = (roundRobin + 1) % pool.Count;
            return pool[roundRobin];
        }

        /// <summary>
        /// Builds the pool on first use and repairs it after a scene load — the same trap DeathMist and
        /// SlashFx both guard: the GameObjects are destroyed, the static list is not.
        /// </summary>
        static void EnsurePool()
        {
            for (int i = pool.Count - 1; i >= 0; i--)
                if (pool[i] == null) pool.RemoveAt(i);

            while (pool.Count < PoolSize)
            {
                var go = new GameObject("~PyreMist");
                var fx = go.AddComponent<PyreMist>();
                fx.Configure();
                pool.Add(fx);
            }
        }

        // ------------------------------------------------------------------ construction

        void Configure()
        {
            DeathMist.RetainShared();

            ps = gameObject.AddComponent<ParticleSystem>();

            // AddComponent on a live GameObject runs Awake immediately and a fresh ParticleSystem has
            // playOnAwake=true, so it is already simulating. Unity refuses main.duration on a playing
            // system (warning, value silently dropped). Stop it first — DeathMist learned this one.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;                 // rule 1: hitstop must not freeze the discharge
            main.maxParticles = MaxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(Flight);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.20f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;                   // it is DRIVEN, not falling and not rising
            main.stopAction = ParticleSystemStopAction.None;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;                  // burst-only, like DeathMist

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = ConeAngle;
            shape.radius = 0.05f;
            shape.radiusThickness = 1f;

            // The wisp. Without this the stream is a clean cone, which reads as a jet of gas; the curl
            // is the entire difference between "vented" and "misty".
            var noise = ps.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = new ParticleSystem.MinMaxCurve(0.35f);
            noise.frequency = 1.1f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(1.4f);
            noise.damping = true;

            // Fade in over the first sixth (out of the weapon), hold, then out as it arrives. The hold
            // is long on purpose: the brightest the mist ever is, is the moment it enters the enemy.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.16f),
                    new GradientAlphaKey(1f, 0.72f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            // Narrowing as it goes: the stream tightens into the wound instead of blooming out of it.
            // The opposite of DeathMist, which expands as a body comes apart — same substance, opposite
            // intent, and that contrast is what makes one read as giving and the other as losing.
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.35f));

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-2.2f, 2.2f);

            var psr = GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.sharedMaterial = DeathMist.MistMaterial();
            psr.shadowCastingMode = ShadowCastingMode.Off;
            psr.receiveShadows = false;
            psr.lightProbeUsage = LightProbeUsage.Off;
            psr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            psr.sortingFudge = -3f;
        }

        /// <summary>
        /// Weapon and wand colours are HDR and sit well above 1. Flattened, and kept a touch brighter
        /// than <see cref="DeathMist"/>'s 0.85 — this mist is the charge itself, not smoke off a body.
        /// </summary>
        static Color Tint(Color c)
        {
            Color n = SlashFx.NormaliseColor(c);
            return new Color(n.r, n.g, n.b, 1f);
        }

        void OnDestroy()
        {
            DeathMist.ReleaseShared();
        }
    }
}
