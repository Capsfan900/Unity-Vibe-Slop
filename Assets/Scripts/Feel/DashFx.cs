using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The picture of a dash: a burst of speed lines that read as DISTANCE COVERED, fired once and gone
    /// inside a quarter of a second.
    ///
    /// <para><b>Why the dash needed a picture at all.</b> The FOV kick and the whoosh were already wired
    /// and shipping (8° on the asset, not a code default), so the dash was not silent — it was
    /// *non-specific*. A symmetric FOV widen says "something changed about the lens"; it does not say
    /// which way you went or how far. Nothing in the frame moved that the eye could measure the
    /// displacement against, and 0.16 s is far too short for the world itself to sell it. Speed lines
    /// are the standard device for exactly that, and this project has no motion blur to lean on.</para>
    ///
    /// <para><b>CAMERA SPACE, NOT WORLD SPACE.</b> Same conclusion <see cref="WeaponTrail"/> reached and
    /// for the same reason: a world-space streak hangs in the world the moment you turn the mouse, and
    /// a dash is very often a turn. The lines are children of the camera at identity with
    /// <c>useWorldSpace = false</c>, so they are screen furniture and a 180° flick mid-dash smears
    /// nothing.</para>
    ///
    /// <para><b>They do not bloom. At all.</b> Peak channel is 0.90, deliberately UNDER the scene's 1.05
    /// bloom threshold — so a dash contributes literally zero to the bloom buffer and cannot compete
    /// with <c>EnemyVisuals.CueFlash</c>, which owns the brightness budget and has to stay the loudest
    /// event in any frame. The dash reads through SHAPE, DIRECTION and MOTION instead. The colour is a
    /// cold desaturated blue-white, which is air, not the warm ember the fight speaks in.</para>
    ///
    /// <para><b>The reticle stays clear.</b> Every streak starts at least <see cref="innerRadius"/> off
    /// the view axis, which at the shipped numbers is ~15% of screen height, so the thing you are
    /// dashing at is never drawn over.</para>
    ///
    /// <para>Built once, reused forever: one additive material and a fixed set of LineRenderers, no
    /// per-dash allocation. Unscaled time throughout, so a dash that fires during a hitstop still moves
    /// (rule 1) and still looks right.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class DashFx : MonoBehaviour
    {
        [Header("Geometry (metres, in camera space)")]
        [Tooltip("Distance in front of the lens the streaks are drawn at. Everything else scales with " +
                 "this: at 95 deg vertical FOV the visible frame is ~1.31 m tall at 0.6 m.")]
        public float zPlane = 0.6f;
        [Tooltip("Closest a streak may start to the view axis. ~15% of screen height — the reticle and " +
                 "whatever you are dashing at are never drawn over.")]
        public float innerRadius = 0.20f;
        [Tooltip("Random extra start radius. Without it the burst is a perfect annulus and reads as a " +
                 "UI decal rather than as air going past.")]
        public float radiusJitter = 0.26f;
        [Tooltip("Streak length. 0.34 m at the shipped plane is ~26% of screen height: long enough to " +
                 "read as travel, short enough not to be a cage over the frame.")]
        public float length = 0.34f;
        [Tooltip("How far the whole burst slides along its flow direction over its life. Static lines " +
                 "read as a filter; sliding ones read as the world going past.")]
        public float travel = 0.13f;
        public float headWidth = 0.0075f;
        [Tooltip("Tail width as a fraction of the head. A hard taper is what makes a line read as a " +
                 "streak instead of as a wire strung across the frame.")]
        [Range(0f, 0.6f)] public float tailFraction = 0.12f;

        [Header("Colour")]
        [Tooltip("Cold, desaturated, and NORMALISED to peak channel `brightness` below. Air, not ember " +
                 "— the fight owns warm light.")]
        public Color hue = new Color(0.70f, 0.80f, 1f, 1f);

        Transform space;             // the camera; the ribbon's local space
        LineRenderer[] lines;
        Vector3[] starts;            // camera-space start point of each streak at t=0
        Vector2[] flows;             // camera-space screen direction each streak travels along
        Material mat;
        readonly Vector3[] seg = new Vector3[2];

        float age = -1f;
        float life = 0.22f;
        float peakAlpha = 0.85f;
        int active;

        /// <summary>Streaks currently on screen. 0 when the dash is over. Read by tests.</summary>
        public int LiveStreaks { get { return age >= 0f ? active : 0; } }

        /// <summary>
        /// Create the pool. <paramref name="space"/> is the camera transform; the streak root is
        /// parented to it at identity so local positions ARE camera space.
        /// </summary>
        public void Build(Transform cameraSpace, int count, float brightness)
        {
            space = cameraSpace;
            if (space == null || count <= 0) return;

            Color c = hue;
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m > 0.0001f) c = new Color(c.r / m * brightness, c.g / m * brightness, c.b / m * brightness, 1f);
            mat = SlashFx.CreateAdditiveMaterial(c);

            var rootGo = new GameObject("DashStreaks");
            rootGo.transform.SetParent(space, false);
            rootGo.transform.localPosition = Vector3.zero;
            rootGo.transform.localRotation = Quaternion.identity;

            lines = new LineRenderer[count];
            starts = new Vector3[count];
            flows = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                var lr = SlashFx.CreateLine(rootGo.transform, "Streak" + i, 2, headWidth, headWidth * tailFraction, false, mat);
                lr.useWorldSpace = false;      // CreateLine ships world space; see the class note
                lr.gameObject.SetActive(false);
                lines[i] = lr;
            }
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }

        /// <summary>
        /// Fire the burst. <paramref name="dirLocal"/> is the dash direction in CAMERA space — the
        /// geometry is rebuilt around it every time, so a strafe streams sideways and a forward dash
        /// streams radially, which is the difference between "you dashed" and "you dashed THAT way".
        /// </summary>
        public void Fire(Vector3 dirLocal, float seconds, float alpha)
        {
            if (lines == null || lines.Length == 0) return;
            life = Mathf.Max(0.02f, seconds);
            peakAlpha = Mathf.Clamp01(alpha);
            active = lines.Length;

            for (int i = 0; i < lines.Length; i++)
            {
                float angle = DashImpulse.StreakAngle(i, lines.Length, Random.value);
                Vector2 flow = DashImpulse.StreakFlow(angle, dirLocal);
                float r = innerRadius + Random.value * radiusJitter;
                starts[i] = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, zPlane);
                flows[i] = flow;
                lines[i].gameObject.SetActive(true);
            }

            age = 0f;
            Apply(0f);
        }

        void Update()
        {
            if (age < 0f || lines == null) return;

            age += Time.unscaledDeltaTime;   // rule 1: a hitstop must not stretch or freeze this
            float t = age / life;
            if (t >= 1f)
            {
                for (int i = 0; i < lines.Length; i++) lines[i].gameObject.SetActive(false);
                age = -1f;
                active = 0;
                return;
            }
            Apply(t);
        }

        void Apply(float t01)
        {
            float a = DashImpulse.StreakAlpha(t01) * peakAlpha;
            // One material write, not one per renderer — the same call SlashFx makes and for the same
            // reason. Additive blend is SrcAlpha/One, so alpha is a real fade here.
            if (mat != null)
            {
                Color c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                c.a = a;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            }

            float slide = travel * t01;
            for (int i = 0; i < lines.Length; i++)
            {
                Vector3 off = new Vector3(flows[i].x, flows[i].y, 0f);
                seg[0] = starts[i] + off * slide;
                seg[1] = seg[0] + off * length;
                lines[i].SetPositions(seg);
            }
        }
    }
}
