using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The visual language for items: finding one, taking one, spending one.
    ///
    /// The rule everything here follows is that an item must read as HELD MAGIC — something with a
    /// shape and a direction — rather than a coloured explosion. So a pickup orbits and breathes, a
    /// collect visibly travels into you, and each effect moves the way the effect actually behaves:
    /// Updraft rises, Soul Lantern draws inward, Phantom Step recedes.
    ///
    /// All of it is procedural and self-destructing, on unscaled time, matching SlashFx/LightningEffect.
    /// </summary>
    public static class ItemVfx
    {
        /// <summary>
        /// Attach the resting animation to a pickup. Persistent (not spawned per frame) because this
        /// runs for every pickup in the level simultaneously — allocation here would be a real cost.
        /// </summary>
        public static ItemGlint AttachIdle(ItemPickup pickup, Transform visual, Color color)
        {
            if (pickup == null || visual == null) return null;
            var glint = pickup.gameObject.GetComponent<ItemGlint>();
            if (glint == null) glint = pickup.gameObject.AddComponent<ItemGlint>();
            glint.Configure(visual, color);
            return glint;
        }

        /// <summary>
        /// Taking the item. The charge visibly travels from the pedestal into the player, which sells
        /// "I got it" far better than tinting the whole screen.
        /// </summary>
        public static void Collect(Vector3 from, Transform target, Color color)
        {
            SlashFx.Flare(from, color, 1.1f, 0.22f);
            SlashFx.Ring(from + Vector3.down * 0.9f, Vector3.up, color, 1.3f, 0.28f);
            ItemConverge.Play(from, target, color, 7, 0.26f, 1.0f, true);
        }

        /// <summary>Updraft: a column of rising streaks and a ground ring, so the launch has a source.</summary>
        public static void Updraft(Transform player, Color color)
        {
            if (player == null) return;
            Vector3 feet = player.position + Vector3.down * 0.9f;
            SlashFx.Ring(feet, Vector3.up, color, 2.4f, 0.30f);
            SlashFx.Ring(feet, Vector3.up, color, 1.2f, 0.22f);
            // Thrown hard upward through a narrow cone: the arc reads as a column, not a fountain.
            SlashFx.Sparks(feet + Vector3.up * 0.2f, Vector3.up, color, 14, 11f, 13f);
            SlashFx.Flare(player.position, color, 0.9f, 0.18f);
        }

        /// <summary>Soul Lantern: warm motes drawn INWARD. Restorative reads as gathering, not bursting.</summary>
        public static void Lantern(Transform player, Color color)
        {
            if (player == null) return;
            Vector3 feet = player.position + Vector3.down * 0.85f;
            SlashFx.Ring(feet, Vector3.up, color, 1.8f, 0.42f);
            SlashFx.Ring(feet, Vector3.up, color, 2.9f, 0.55f);
            ItemConverge.Play(player.position, player, color, 12, 0.5f, 2.6f, false);
        }

        /// <summary>Phantom Step: receding after-images. Should read as displacement, not as a flash.</summary>
        public static void PhantomStep(Transform player, Color color, float duration)
        {
            if (player == null) return;
            var host = new GameObject("Fx_PhantomTrail");
            host.transform.position = player.position;
            host.AddComponent<PhantomTrail>().Init(player, color, Mathf.Clamp(duration, 0.2f, 6f));
        }

        /// <summary>Drive the alpha of a runtime additive material (shared fade path for all item FX).</summary>
        internal static void Tint(Material mat, float alpha)
        {
            if (mat == null) return;
            var c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
            c.a = alpha;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }
    }

    /// <summary>
    /// Streaks that travel toward a moving target (collect) or inward to a point (lantern).
    /// One material, a handful of two-point lines, gone in well under half a second.
    ///
    /// Top-level rather than nested inside ItemVfx: Unity's script importer is happier with
    /// MonoBehaviours that are not nested types, and AddComponent stays unambiguous.
    /// </summary>
    public class ItemConverge : MonoBehaviour
    {
        struct Mote
        {
            public Vector3 pos;
            public Vector3 offset;
            public float phase;
            public LineRenderer line;
        }

        Transform target;
        Vector3 fallback;
        Material mat;
        float duration, life, radius;
        bool fromPoint;
        readonly List<Mote> motes = new List<Mote>();

        public static void Play(Vector3 origin, Transform target, Color color, int count, float seconds, float radius, bool fromPoint)
        {
            var go = new GameObject("Fx_Converge");
            go.transform.position = origin;
            go.AddComponent<ItemConverge>().Init(origin, target, color, count, seconds, radius, fromPoint);
        }

        void Init(Vector3 origin, Transform t, Color color, int count, float seconds, float r, bool point)
        {
            target = t;
            fallback = origin;
            duration = Mathf.Max(0.05f, seconds);
            radius = r;
            fromPoint = point;
            mat = SlashFx.CreateAdditiveMaterial(color);

            for (int i = 0; i < Mathf.Clamp(count, 1, 20); i++)
            {
                // fromPoint: every mote starts at the pedestal and fans slightly on the way in.
                // otherwise: motes start on a sphere around the player and are drawn inward.
                Vector3 off = Random.onUnitSphere * radius;
                off.y = Mathf.Abs(off.y) * 0.7f;
                motes.Add(new Mote
                {
                    pos = point ? origin : origin + off,
                    offset = off,
                    phase = Random.Range(0f, 0.22f),
                    line = SlashFx.CreateLine(transform, "Mote", 2, 0.05f, 0.008f, false, mat)
                });
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            life += dt;
            Vector3 dest = target != null ? target.position : fallback;

            for (int i = 0; i < motes.Count; i++)
            {
                var m = motes[i];
                float k = Mathf.Clamp01((life - m.phase) / Mathf.Max(0.01f, duration - m.phase));
                // Ease-in: a slow gather then a quick snap home, which is what makes it feel drawn in.
                float e = k * k;
                Vector3 start = fromPoint ? fallback : dest + m.offset;
                Vector3 prev = m.pos;
                m.pos = Vector3.Lerp(start, dest, e);
                if (m.line != null)
                {
                    Vector3 travel = m.pos - prev;
                    Vector3 tail = travel.sqrMagnitude > 0.000001f
                        ? m.pos - travel.normalized * 0.28f
                        : m.pos - Vector3.up * 0.05f;
                    m.line.SetPosition(0, m.pos);
                    m.line.SetPosition(1, tail);
                }
                motes[i] = m;
            }

            float a = 1f - Mathf.Clamp01(life / duration);
            ItemVfx.Tint(mat, a * a);
            if (life >= duration) Destroy(gameObject);
        }

        void OnDestroy() { if (mat != null) Destroy(mat); }
    }

    /// <summary>Ghost arcs shed behind the player while Phantom Step is active.</summary>
    public class PhantomTrail : MonoBehaviour
    {
        Transform player;
        Color color;
        float duration, life, nextShed;

        public void Init(Transform p, Color c, float seconds)
        {
            player = p;
            color = c;
            duration = seconds;
        }

        void Update()
        {
            life += Time.unscaledDeltaTime;
            if (player == null || life >= duration) { Destroy(gameObject); return; }

            // Shed an after-image behind the player at a fixed cadence, facing backward and slightly
            // down, so it reads as something left behind rather than something being emitted.
            if (life >= nextShed)
            {
                nextShed = life + 0.09f;
                Vector3 behind = player.position - player.forward * 0.45f + Vector3.down * 0.2f;
                SlashFx.Arc(behind, player.right, color, 0.75f, 150f, 0.30f, -player.forward);
            }
        }
    }

    /// <summary>
    /// Resting animation on a world pickup: two motes orbiting a tilted ring plus a slow breath on the
    /// item's own emission. Deliberately allocation-free per frame — this runs on every pickup at once.
    /// </summary>
    public class ItemGlint : MonoBehaviour
    {
        const int MoteCount = 2;
        const float OrbitRadius = 0.38f;
        const float OrbitSpeed = 95f;      // degrees/sec
        const float PulseSpeed = 1.6f;

        /// <summary>
        /// Beyond this the glint stops animating entirely. Every pickup in the level runs this at once
        /// and each one writes a property block (which breaks batching for that renderer), so the ones
        /// you cannot see should cost nothing. Tunable rather than a magic number.
        /// </summary>
        public float cullDistance = 35f;

        Transform visual;
        Color hue;
        readonly Transform[] motes = new Transform[MoteCount];
        Renderer[] bodyRenderers;
        MaterialPropertyBlock mpb;
        Transform camTransform;
        float angle;
        bool active = true;
        bool culled;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void Configure(Transform v, Color color)
        {
            visual = v;
            hue = color;
            mpb = new MaterialPropertyBlock();
            bodyRenderers = v.GetComponentsInChildren<Renderer>(true);
            BuildMotes();
        }

        void BuildMotes()
        {
            // Parented to the visual's PARENT, not the visual itself: the visual spins, and motes
            // riding that spin would just look like more of the same rotation.
            Transform parent = visual != null && visual.parent != null ? visual.parent : transform;
            for (int i = 0; i < MoteCount; i++)
            {
                if (motes[i] != null) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.name = "Mote" + i;
                go.transform.SetParent(parent, false);
                go.transform.localScale = Vector3.one * 0.055f;
                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var block = new MaterialPropertyBlock();
                block.SetColor(EmissionId, hue * 1.4f);
                block.SetColor(BaseColorId, Color.black);
                r.SetPropertyBlock(block);
                motes[i] = go.transform;
            }
        }

        /// <summary>Hidden along with the pickup body when collected.</summary>
        public void SetActive(bool value)
        {
            active = value;
            // Two independent reasons to hide the motes (collected, or distance-culled); honour both,
            // otherwise re-showing a collected pickup at range leaves motes frozen in mid-orbit.
            bool show = value && !culled;
            for (int i = 0; i < MoteCount; i++)
                if (motes[i] != null) motes[i].gameObject.SetActive(show);
        }

        void Update()
        {
            if (!active || visual == null) return;

            // Distance cull. The motes are hidden with the rest of the effect so a culled pickup does
            // not leave two cubes frozen in mid-orbit.
            if (camTransform == null)
            {
                var cam = Camera.main;
                if (cam != null) camTransform = cam.transform;
            }
            if (camTransform != null)
            {
                float sqr = (visual.position - camTransform.position).sqrMagnitude;
                bool wantCulled = sqr > cullDistance * cullDistance;
                if (wantCulled != culled)
                {
                    culled = wantCulled;
                    for (int i = 0; i < MoteCount; i++)
                        if (motes[i] != null) motes[i].gameObject.SetActive(!culled);
                }
                if (culled) return;
            }

            angle += OrbitSpeed * Time.unscaledDeltaTime;

            Vector3 centre = visual.localPosition;
            for (int i = 0; i < MoteCount; i++)
            {
                if (motes[i] == null) continue;
                float a = (angle + i * (360f / MoteCount)) * Mathf.Deg2Rad;
                // Tilted orbit so the ring reads in 3D instead of as a flat circle.
                Vector3 offset = new Vector3(Mathf.Cos(a) * OrbitRadius,
                                             Mathf.Sin(a * 2f) * 0.10f,
                                             Mathf.Sin(a) * OrbitRadius);
                motes[i].localPosition = centre + offset;
            }

            // Slow breath on the body: alive, not blinking.
            if (bodyRenderers == null) return;
            // MaterialPropertyBlock is NOT serializable, but the Renderer[] beside it IS, so a domain
            // reload restores the renderers and leaves this null — which threw here every frame.
            if (mpb == null) mpb = new MaterialPropertyBlock();
            float pulse = 0.75f + 0.35f * Mathf.Sin(Time.unscaledTime * PulseSpeed);
            mpb.SetColor(EmissionId, hue * pulse);
            mpb.SetColor(BaseColorId, Color.black);
            foreach (var r in bodyRenderers)
                if (r != null) r.SetPropertyBlock(mpb);
        }
    }
}
