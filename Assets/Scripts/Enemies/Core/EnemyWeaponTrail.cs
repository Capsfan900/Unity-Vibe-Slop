using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A blade trail for an ANIMATED forge enemy: a short strip of quads swept between two points on the
    /// weapon while an attack clip is inside its contact window, plus a few sparks on the contact frame.
    /// Built for THE ARGENT HALBERDIER after play ("make his attacks more visually appealing, efficient,
    /// not lag-causing"); any <c>ModelSpec</c> with <c>bladeTrail</c> gets one from <c>MiniBossFactory</c>.
    ///
    /// <para><b>What it costs, exactly.</b> One Mesh of <see cref="samples"/> × 2 vertices (28 at the
    /// shipped 14), rewritten in place every frame from preallocated arrays — no per-frame allocation, no
    /// particle system, one owned additive material, and nothing at all while no attack clip is live
    /// (the renderer is disabled). It reads the Animator's current state; it never asks the Animator to do
    /// anything, and it writes no transform the puppet owns.</para>
    ///
    /// <para><b>It knows when the hitbox is live from the CLIP, not from the brain.</b> The window is
    /// <c>[hit − leadIn, hit + tail]</c> of the clip's normalised time, where <c>hit</c> is the contact
    /// frame <c>PuppetVisuals</c> baked from the forge manifest for that clip. So the trail appears as
    /// the blade starts its cut and dies as the cut finishes — the same frames the player has to read —
    /// whatever speed the clip is being played at to fit the attack's timing. A trail keyed off the
    /// enemy's state machine instead would draw during the WIND-UP, which is the one moment a ribbon
    /// must not say "this is the swing".</para>
    ///
    /// <para><b>Brightness.</b> The strip's material is the enemy's accent normalised to a PEAK CHANNEL OF
    /// 1.0 (<see cref="SlashFx.CreateAdditiveMaterial"/>) — under the scene's 1.05 bloom threshold, so a
    /// swing adds motion and shape, never glow. Light on an enemy means "you deflected" and
    /// <c>EnemyVisuals.CueFlash</c> keeps that budget; a trail that bloomed would make every swing look
    /// like a parry. The strip fades GEOMETRICALLY (the old edge collapses toward the tip line) because
    /// the additive URP/Unlit material ignores vertex colour — the same constraint <see cref="WeaponTrail"/>
    /// solved with width.</para>
    ///
    /// <para>Scaled time throughout (<c>Time.deltaTime</c>): hitstop freezes the strip with the enemy
    /// and the puppet, which is the point of hitstop. Rule 1 reserves <c>PlayerDelta</c> for the player.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemyWeaponTrail : MonoBehaviour
    {
        [Header("Bindings — written by MiniBossFactory")]
        [Tooltip("The bone the weapon is skinned to (the RightHand of the forge rig). Read, never written.")]
        public Transform bladeBone;
        [Tooltip("Inner end of the strip, in bladeBone's local space. The hand, for a halberd.")]
        public Vector3 bladeBaseLocal;
        [Tooltip("Outer end of the strip, in bladeBone's local space. The axe head / blade tip — the " +
                 "ModelSpec's weaponFxPos, converted at build time from the bind pose.")]
        public Vector3 bladeTipLocal = new Vector3(0f, 0f, 0.5f);
        [Tooltip("The Animator the attack clips play on.")]
        public Animator animator;
        [Tooltip("The clips that are ATTACKS, and the contact fraction of each — the same table " +
                 "PuppetVisuals bakes (namedClips / namedClipHits). Parallel arrays.")]
        public string[] attackClips = new string[0];
        public float[] attackHits = new float[0];

        [Header("Window — fractions of the clip, around its contact frame")]
        [Tooltip("How far BEFORE the contact frame the strip starts, as a fraction of the clip. 0.22 " +
                 "of a 0.75 s sweep is the last ~0.17 s of the cut: the blade is already moving.")]
        [Range(0f, 0.6f)] public float leadIn = 0.22f;
        [Tooltip("How far AFTER the contact frame the strip keeps sampling. The follow-through.")]
        [Range(0f, 0.6f)] public float tail = 0.12f;

        [Header("Shape")]
        [Range(4, 32)] public int samples = 14;
        [Range(1, 4)] public int subdivisions = 2;
        [Tooltip("Seconds the strip lingers after the window closes, collapsing from its oldest edge.")]
        public float fadeSeconds = 0.12f;

        [Header("Contact")]
        [Tooltip("Sparks thrown from the tip as the clip crosses its contact frame. A few, not a burst: " +
                 "the parry's sparks (10) must stay the bigger event.")]
        [Range(0, 12)] public int contactSparks = 4;
        public float contactSparkSpeed = 5f;

        [Header("Colour — from EnemyData.emission, normalised to peak 1.0 at build")]
        public Color hue = new Color(0.56f, 0.83f, 1f, 1f);

        // ---- state ------------------------------------------------------------------------------
        Mesh mesh;
        MeshRenderer rend;
        MeshFilter filter;
        Material mat;
        Vector3[] verts;          // 2 per sample: [2i] = base, [2i+1] = tip, sample 0 = newest
        Vector3[] baseW, tipW;    // world-space history, newest first
        int count;
        int peak;
        float fade;
        bool live, wasLive;
        bool sparked;
        int[] clipHashes;
        Vector3 lastBase, lastTip;
        bool haveLast;

        /// <summary>True while an attack clip is inside its contact window. For tests and the harness.</summary>
        public bool IsLive { get { return live; } }
        public int SampleCount { get { return count; } }

        void Awake()
        {
            int n = Mathf.Max(4, samples);
            verts = new Vector3[n * 2];
            baseW = new Vector3[n];
            tipW = new Vector3[n];

            clipHashes = new int[attackClips != null ? attackClips.Length : 0];
            for (int i = 0; i < clipHashes.Length; i++) clipHashes[i] = Animator.StringToHash(attackClips[i]);

            mesh = new Mesh { name = "EnemyWeaponTrail" };
            mesh.MarkDynamic();
            // Triangles never change: quad i joins samples i and i+1. Both windings, so the strip reads
            // from either side of the swing (additive, so a double face costs nothing visible).
            var tris = new int[(n - 1) * 12];
            for (int i = 0, t = 0; i < n - 1; i++)
            {
                int b0 = i * 2, t0 = i * 2 + 1, b1 = (i + 1) * 2, t1 = (i + 1) * 2 + 1;
                tris[t++] = b0; tris[t++] = t0; tris[t++] = b1;
                tris[t++] = t0; tris[t++] = t1; tris[t++] = b1;
                tris[t++] = b1; tris[t++] = t0; tris[t++] = b0;
                tris[t++] = b1; tris[t++] = t1; tris[t++] = t0;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 8f);

            mat = SlashFx.CreateAdditiveMaterial(hue);   // normalised to a peak channel of exactly 1.0
            var go = new GameObject("BladeTrail");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            rend.enabled = false;
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (mesh != null) Destroy(mesh);
        }

        void OnDisable()
        {
            count = 0; peak = 0; fade = 0f; live = false; wasLive = false; haveLast = false;
            if (rend != null) rend.enabled = false;
        }

        /// <summary>
        /// Is the Animator inside an attack clip's contact window right now, and where is that clip's
        /// contact? Pure over the state info so it can be reasoned about without a scene.
        /// </summary>
        public static bool WindowContains(float normalizedTime, float hit, float leadIn, float tail)
        {
            float t = normalizedTime;
            if (t > 1f) t -= Mathf.Floor(t);    // a looping state; attack clips are Once, but be safe
            return t >= hit - leadIn && t <= hit + tail;
        }

        void LateUpdate()
        {
            if (bladeBone == null || animator == null || rend == null) return;
            float dt = Time.deltaTime;

            // ---- which attack clip, if any, and where in it ---------------------------------------
            live = false;
            float hit = 0f, t = 0f;
            if (clipHashes != null && clipHashes.Length > 0 && animator.isActiveAndEnabled)
            {
                var st = animator.GetCurrentAnimatorStateInfo(0);
                int h = st.shortNameHash;
                for (int i = 0; i < clipHashes.Length; i++)
                {
                    if (clipHashes[i] != h) continue;
                    hit = i < attackHits.Length ? Mathf.Clamp01(attackHits[i]) : 0.55f;
                    t = st.normalizedTime;
                    live = WindowContains(t, hit, leadIn, tail);
                    break;
                }
            }

            if (live && !wasLive) { count = 0; haveLast = false; sparked = false; fade = 1f; }

            if (live)
            {
                Sample();
                peak = count;
                // The contact frame: a few sparks off the tip, thrown the way the blade is moving.
                if (!sparked && t >= hit)
                {
                    sparked = true;
                    if (contactSparks > 0 && count >= 2)
                    {
                        Vector3 dir = tipW[0] - tipW[1];
                        if (dir.sqrMagnitude < 1e-6f) dir = bladeBone.forward;
                        SlashFx.Sparks(tipW[0], dir.normalized + Vector3.up * 0.2f, hue,
                                       contactSparks, contactSparkSpeed, 0.5f);
                    }
                }
            }
            else if (fade > 0f)
            {
                fade -= dt / Mathf.Max(0.01f, fadeSeconds);
                // Collapse from the OLDEST edge, like the player's trail: the arc closes rather than
                // dimming in place over the fight for an extra beat.
                count = Mathf.Min(count, Mathf.CeilToInt(peak * Mathf.Clamp01(fade)));
            }
            wasLive = live;

            if (fade <= 0f || count < 2)
            {
                if (rend.enabled) rend.enabled = false;
                if (fade <= 0f) { count = 0; haveLast = false; }
                return;
            }

            Draw();
        }

        void Sample()
        {
            Vector3 b = bladeBone.TransformPoint(bladeBaseLocal);
            Vector3 tp = bladeBone.TransformPoint(bladeTipLocal);
            if (!haveLast) { Push(b, tp); lastBase = b; lastTip = tp; haveLast = true; return; }
            for (int i = 1; i <= subdivisions; i++)
            {
                float k = i / (float)subdivisions;
                Push(Vector3.Lerp(lastBase, b, k), Vector3.Lerp(lastTip, tp, k));
            }
            lastBase = b; lastTip = tp;
        }

        void Push(Vector3 b, Vector3 tp)
        {
            int n = Mathf.Min(count + 1, baseW.Length);
            for (int i = n - 1; i > 0; i--) { baseW[i] = baseW[i - 1]; tipW[i] = tipW[i - 1]; }
            baseW[0] = b; tipW[0] = tp;
            count = n;
        }

        void Draw()
        {
            // Local space of the strip object (a child of the enemy), so the mesh moves with the body
            // while the samples themselves stay where the blade swept in the world.
            Transform s = filter.transform;
            int n = baseW.Length;
            for (int i = 0; i < n; i++)
            {
                int src = Mathf.Min(i, count - 1);
                // Age taper: the oldest samples collapse toward the tip line, which is the only fade an
                // additive material without vertex colour can show.
                float age = count > 1 ? src / (float)(count - 1) : 0f;
                float shrink = age * age;
                Vector3 tipP = tipW[src];
                Vector3 baseP = Vector3.Lerp(baseW[src], tipP, shrink);
                verts[i * 2] = s.InverseTransformPoint(baseP);
                verts[i * 2 + 1] = s.InverseTransformPoint(tipP);
            }
            mesh.vertices = verts;
            mesh.RecalculateBounds();
            if (!rend.enabled) rend.enabled = true;
        }
    }
}
