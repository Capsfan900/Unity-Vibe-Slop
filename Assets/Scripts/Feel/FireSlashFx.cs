using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1
{
    /// <summary>
    /// A pooled, first-person fire crescent for Pyre's eventual fire-slash presentation.
    ///
    /// <para>This is deliberately presentation-only. It neither queries targets nor owns a combat
    /// clock: callers supply the same origin, forward, radius, arc and hit progress their already
    /// resolved attack uses. <see cref="UltimateAbility"/> may therefore replace its Pyre rendering
    /// without moving a Cleave, Flurry, Quake or Nova hit by a frame or a metre.</para>
    ///
    /// <para>The effect uses unscaled time. Pyre's existing hitstop slows the world; freezing the
    /// visible slash at the same time would turn its impact punctuation into a stalled frame. The
    /// crescent is a restrained 1.05-channel exception: its hot inner edge is readable at range, while
    /// the orange fringe and embers remain below that ceiling.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FireSlashFx : MonoBehaviour
    {
        /// <summary>Maximum HDR channel this effect may write. The scene bloom threshold is 1.05.</summary>
        public const float PeakChannel = 1.05f;
        public const float DefaultSeconds = 0.26f;
        public const float MinSeconds = 0.08f;
        public const float MaxSeconds = 0.48f;
        public const float MinRadius = 0.10f;
        public const float MinArcDegrees = 2f;
        public const int ArcPoints = 19;
        public const int CorePoints = 9;
        public const int EmberCount = 7;
        public const int MaxLive = 8;

        // A hot, nearly-white inner edge; the silhouette gets its identity from orange rather than
        // from more brightness. Values are intentionally pinned by FireSlashFxTests.
        static readonly Color HotInner = new Color(PeakChannel, 0.74f, 0.28f, 1f);
        static readonly Color OrangeFringe = new Color(0.92f, 0.18f, 0.025f, 1f);

        static readonly Stack<FireSlashFx> pool = new Stack<FireSlashFx>(MaxLive);
        static int live;

        readonly Vector3[] fringePoints = new Vector3[ArcPoints];
        readonly Vector3[] corePoints = new Vector3[CorePoints];
        readonly Vector3[] emberStarts = new Vector3[EmberCount];
        readonly Vector3[] emberEnds = new Vector3[EmberCount];

        LineRenderer fringe;
        LineRenderer core;
        LineRenderer[] embers;
        Material fringeMat;
        Material coreMat;
        Color fringeColor;
        Geometry geometry;
        float startProgress;
        float duration;
        float life;
        bool counted;

        /// <summary>
        /// Sanitised geometric contract for one slash. It is public and pure so exact endpoints can be
        /// tested without a camera, an instantiated scene, or subjective screenshot judgement.
        /// </summary>
        public struct Geometry
        {
            public readonly Vector3 origin;
            public readonly Vector3 forward;
            public readonly float radius;
            public readonly float arcDegrees;

            internal Geometry(Vector3 origin, Vector3 forward, float radius, float arcDegrees)
            {
                this.origin = origin;
                this.forward = forward;
                this.radius = radius;
                this.arcDegrees = arcDegrees;
            }

            /// <summary>
            /// Point along the damage-congruent horizontal arc: 0 is its left edge, 1 its right edge.
            /// A slight rise is visual-only and returns to the supplied origin's height at both ends.
            /// </summary>
            public Vector3 Point(float progress)
            {
                float p = Mathf.Clamp01(progress);
                float angle = Mathf.Lerp(-arcDegrees * 0.5f, arcDegrees * 0.5f, p);
                Vector3 planar = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                // A low arch stops a wide horizontal slice from becoming a ground wire in first person;
                // its endpoints remain on the actual attack plane, so it never hides the supplied range.
                float lift = Mathf.Sin(p * Mathf.PI) * Mathf.Min(0.42f, radius * 0.09f);
                return origin + planar * radius + Vector3.up * lift;
            }

            /// <summary>Forward direction of travel at a point on the arc, useful for ember streaks.</summary>
            public Vector3 Tangent(float progress)
            {
                float p = Mathf.Clamp01(progress);
                float angle = Mathf.Lerp(-arcDegrees * 0.5f, arcDegrees * 0.5f, p);
                Vector3 planar = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                Vector3 tangent = Vector3.Cross(Vector3.up, planar);
                return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.right;
            }
        }

        /// <summary>How many slashes are currently drawing (the bounded budget, not a gameplay stat).</summary>
        public static int ActiveCount { get { return live; } }

        /// <summary>
        /// Builds the truthful geometry used by <see cref="Play"/>. Invalid forward vectors fall back
        /// to world forward; invalid sizes become a small, visible crescent instead of NaN renderer data.
        /// </summary>
        public static Geometry CreateGeometry(Vector3 origin, Vector3 forward, float radius, float arcDegrees)
        {
            if (!Finite(origin.x) || !Finite(origin.y) || !Finite(origin.z)) origin = Vector3.zero;
            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            if (!Finite(flat.x) || !Finite(flat.z) || flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
            flat.Normalize();
            float safeRadius = Finite(radius) ? Mathf.Max(MinRadius, Mathf.Abs(radius)) : MinRadius;
            float safeArc = Finite(arcDegrees) ? Mathf.Clamp(Mathf.Abs(arcDegrees), MinArcDegrees, 360f) : MinArcDegrees;
            return new Geometry(origin, flat, safeRadius, safeArc);
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>Pure convenience form used by data/tests that only need an arc endpoint.</summary>
        public static Vector3 PointOnArc(Vector3 origin, Vector3 forward, float radius, float arcDegrees, float progress)
        {
            return CreateGeometry(origin, forward, radius, arcDegrees).Point(progress);
        }

        /// <summary>Clamps a requested visual life into a readable, bounded fire-slash lifetime.</summary>
        public static float Lifetime(float requestedSeconds)
        {
            return Mathf.Clamp(requestedSeconds, MinSeconds, MaxSeconds);
        }

        /// <summary>
        /// Draw one fire slash. <paramref name="progress"/> is the existing hit's position through a
        /// multi-hit phrase (0 left edge to 1 right edge), not a new timing source. Use 0.5 for a single
        /// hit; for a retained <c>superHits</c> phrase use its already-calculated i/(hits-1) position.
        /// </summary>
        public static FireSlashFx Play(Vector3 origin, Vector3 forward, Color tint, float radius, float arcDegrees,
                                       float progress = 0.5f, float seconds = DefaultSeconds)
        {
            if (live >= MaxLive) return null;
            FireSlashFx fx = null;
            while (pool.Count > 0 && fx == null) fx = pool.Pop();
            if (fx == null)
            {
                var go = new GameObject("Fx_FireSlash");
                fx = go.AddComponent<FireSlashFx>();
                fx.CreateRenderers();
            }

            fx.gameObject.SetActive(true);
            fx.transform.position = origin;
            fx.geometry = CreateGeometry(origin, forward, radius, arcDegrees);
            fx.startProgress = Mathf.Clamp01(progress);
            fx.duration = Lifetime(seconds);
            fx.life = 0f;
            fx.ApplyColours(tint);
            fx.Draw(0f);
            fx.counted = true;
            live++;
            return fx;
        }

        void CreateRenderers()
        {
            coreMat = SlashFx.CreateAdditiveMaterial(HotInner);
            fringeMat = SlashFx.CreateAdditiveMaterial(OrangeFringe);
            core = Line("FireSlash_HotInner", CorePoints, 0.095f, 0.026f, coreMat);
            fringe = Line("FireSlash_TornFringe", ArcPoints, 0.19f, 0.035f, fringeMat);
            embers = new LineRenderer[EmberCount];
            for (int i = 0; i < embers.Length; i++)
                embers[i] = Line("FireSlash_Ember", 2, 0.037f, 0.004f, coreMat);
        }

        LineRenderer Line(string name, int points, float startWidth, float endWidth, Material material)
        {
            return SlashFx.CreateLine(transform, name, points, startWidth, endWidth, false, material);
        }

        void ApplyColours(Color tint)
        {
            // Weapon identity may tint the flame, but it cannot turn Pyre into an arbitrary neon laser.
            Color normal = SlashFx.NormaliseColor(tint);
            fringeColor = Color.Lerp(OrangeFringe, normal, 0.18f);
            SetColour(coreMat, HotInner, 1f);
            SetColour(fringeMat, fringeColor, 0.86f);
        }

        static void SetColour(Material material, Color color, float alpha)
        {
            if (material == null) return;
            color.r = Mathf.Min(PeakChannel, Mathf.Max(0f, color.r));
            color.g = Mathf.Min(PeakChannel, Mathf.Max(0f, color.g));
            color.b = Mathf.Min(PeakChannel, Mathf.Max(0f, color.b));
            color.a = Mathf.Clamp01(alpha);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        void Update()
        {
            life += Time.unscaledDeltaTime; // deliberate: visible payoff remains alive through hitstop
            Draw(Mathf.Clamp01(life / duration));
            if (life >= duration) Retire();
        }

        void Draw(float lifeProgress)
        {
            // Fringe states the whole active hit shape immediately. The denser, hot line travels over
            // it, so the eye gets an unambiguous direction rather than a stationary orange diagram.
            Fill(fringePoints, 0f, 1f);
            if (fringe != null) fringe.SetPositions(fringePoints);

            float head = Mathf.Lerp(startProgress, Mathf.Min(1f, startProgress + 0.38f), lifeProgress);
            float tail = Mathf.Max(0f, head - 0.32f);
            if (head - tail < 0.08f) head = Mathf.Min(1f, tail + 0.08f);
            Fill(corePoints, tail, head);
            if (core != null) core.SetPositions(corePoints);

            float fade = 1f - lifeProgress;
            fade *= fade;
            SetColour(coreMat, HotInner, fade);
            SetColour(fringeMat, fringeColor, fade * 0.86f);
            DrawEmbers(head, lifeProgress, fade);
        }

        void Fill(Vector3[] output, float from, float to)
        {
            int last = output.Length - 1;
            for (int i = 0; i < output.Length; i++)
            {
                float p = last > 0 ? Mathf.Lerp(from, to, i / (float)last) : from;
                output[i] = geometry.Point(p);
            }
        }

        void DrawEmbers(float head, float lifeProgress, float fade)
        {
            for (int i = 0; i < EmberCount; i++)
            {
                // Deterministic variation avoids a per-frame Random call and makes each small line a
                // torn ember flowing out of the moving hot edge rather than particle-system confetti.
                float stagger = i / (float)EmberCount;
                float p = Mathf.Clamp01(head - stagger * 0.22f - lifeProgress * 0.08f);
                Vector3 at = geometry.Point(p);
                Vector3 outDir = (at - geometry.origin);
                if (outDir.sqrMagnitude < 0.0001f) outDir = geometry.forward;
                outDir.Normalize();
                Vector3 drift = (geometry.Tangent(p) * (i % 2 == 0 ? 1f : -1f) * 0.28f + outDir * 0.72f + Vector3.up * 0.36f).normalized;
                float length = (0.15f + i * 0.017f) * (0.45f + fade * 0.55f);
                emberStarts[i] = at;
                emberEnds[i] = at - drift * length;
                if (embers != null && embers[i] != null)
                {
                    embers[i].SetPosition(0, emberStarts[i]);
                    embers[i].SetPosition(1, emberEnds[i]);
                    embers[i].enabled = fade > 0.01f;
                }
            }
        }

        /// <summary>Immediate release for deterministic edit-mode cleanup; ordinary runtime uses lifetime.</summary>
        public void ReleaseForTests()
        {
            Retire();
        }

        void Retire()
        {
            if (!counted) return;
            counted = false;
            live = Mathf.Max(0, live - 1);
            if (embers != null)
                for (int i = 0; i < embers.Length; i++) if (embers[i] != null) embers[i].enabled = false;
            gameObject.SetActive(false);
            pool.Push(this);
        }

        void OnDestroy()
        {
            if (counted) live = Mathf.Max(0, live - 1);
            if (coreMat != null)
            {
                if (Application.isPlaying) Destroy(coreMat); else DestroyImmediate(coreMat);
            }
            if (fringeMat != null)
            {
                if (Application.isPlaying) Destroy(fringeMat); else DestroyImmediate(fringeMat);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPool()
        {
            pool.Clear();
            live = 0;
        }
    }
}
