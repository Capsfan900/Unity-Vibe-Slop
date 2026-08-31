using UnityEngine;
using UnityEngine.Rendering;

namespace VibeGame1
{
    /// <summary>
    /// Plays a recorded run back as a translucent racing ghost.
    ///
    /// It is driven by <see cref="SpeedrunTimer.Elapsed"/> — the SAME clock as the live run — which is what
    /// makes it a race rather than a replay: at any instant the ghost is exactly where you were at this
    /// point in your best run.
    ///
    /// It is non-interactive by construction: it has NO collider at all. That is a stronger guarantee than
    /// a layer assignment, which only holds as long as nobody edits the physics matrix.
    /// </summary>
    public class GhostPlayer : MonoBehaviour
    {
        [Header("Look")]
        [Tooltip("Desaturated and translucent on purpose — it must never read as an enemy.")]
        public Color tint = new Color(0.62f, 0.78f, 0.92f, 0.32f);
        public float height = 1.8f;

        [Tooltip("Layer for the ghost geometry. It has no collider, so this is cosmetic/culling only.")]
        public int ghostLayer = 0;

        public bool HasRun => recording != null && recording.IsValid;
        public bool Visible { get; private set; }

        /// <summary>
        /// Seconds the ghost took to reach roughly where the player is now. Positive delta = player behind.
        /// <see cref="HasDelta"/> is false until the cursor has locked on.
        /// </summary>
        public float GhostTimeAtPlayer { get; private set; }
        public bool HasDelta { get; private set; }

        GhostRecording recording;
        Transform body, head, blade;
        Material material;
        Transform player;
        int cursor;

        // How far ahead the delta cursor may scan each frame. The level is a mostly-linear course, so the
        // player's progress only ever moves forward along the ghost's path; a bounded forward scan is both
        // cheap and correct. A branching or looping level would need checkpoint splits instead.
        const int ScanWindow = 150;

        void Awake()
        {
            BuildFigure();
            SetVisible(false);
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        public void Load(GhostRecording rec)
        {
            recording = rec;
            cursor = 0;
            HasDelta = false;
            GhostTimeAtPlayer = 0f;
            SetVisible(rec != null && rec.IsValid);
        }

        public void Clear()
        {
            recording = null;
            SetVisible(false);
        }

        void SetVisible(bool value)
        {
            Visible = value;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = value;
        }

        // ---- figure ---------------------------------------------------------------------------------

        void BuildFigure()
        {
            material = MakeTranslucent(tint);

            body = MakePart(PrimitiveType.Capsule, "GhostBody",
                new Vector3(0f, height * 0.5f, 0f), new Vector3(0.55f, height * 0.5f, 0.55f));
            head = MakePart(PrimitiveType.Sphere, "GhostHead",
                new Vector3(0f, height * 0.92f, 0f), Vector3.one * 0.42f);
            // A stub blade so the ghost's facing is readable at distance — you need to see which way your
            // PB was looking to know whether you took the same line.
            blade = MakePart(PrimitiveType.Cube, "GhostBlade",
                new Vector3(0.34f, height * 0.62f, 0.42f), new Vector3(0.07f, 0.07f, 0.9f));
        }

        Transform MakePart(PrimitiveType type, string name, Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            // No collider, ever. This is the guarantee that the ghost can never affect the game.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.layer = ghostLayer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        static Material MakeTranslucent(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader);
            // URP/Unlit transparent setup. Without all of these the material renders opaque.
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            return m;
        }

        // ---- playback -------------------------------------------------------------------------------

        void LateUpdate()
        {
            if (!HasRun) return;
            var timer = SpeedrunTimer.I;
            if (timer == null) { SetVisible(false); return; }

            float t = timer.Elapsed;
            float interval = recording.TickInterval;
            var samples = recording.samples;

            // Past the end of the recording the ghost has finished — hide it rather than freezing a
            // statue at the boss door.
            if (t > recording.Duration)
            {
                if (Visible) SetVisible(false);
                return;
            }
            if (!Visible && timer.Running) SetVisible(true);

            float exact = t / interval;
            int i = Mathf.Clamp(Mathf.FloorToInt(exact), 0, samples.Length - 1);
            int j = Mathf.Min(i + 1, samples.Length - 1);
            float frac = Mathf.Clamp01(exact - i);

            Vector3 pos = Vector3.Lerp(samples[i].Position, samples[j].Position, frac);
            float yaw = Mathf.LerpAngle(samples[i].Yaw, samples[j].Yaw, frac);
            float pitch = Mathf.LerpAngle(samples[i].Pitch, samples[j].Pitch, frac);

            transform.position = pos;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (head != null) head.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            // Dash reads as a stretched, brighter silhouette so the ghost's movement tech is legible.
            var flags = samples[i].Flags;
            if (body != null)
            {
                bool dashing = (flags & GhostFlags.Dashing) != 0;
                float squash = dashing ? 0.78f : 1f;
                body.localScale = new Vector3(0.55f * squash, height * 0.5f, 0.55f / squash);
            }

            UpdateDelta();
        }

        /// <summary>
        /// Advance a cursor to the ghost sample nearest the player's current position, giving "how long
        /// the ghost took to get where I am". Comparing elapsed times directly would always read zero —
        /// both clocks are the same clock.
        /// </summary>
        void UpdateDelta()
        {
            if (player == null)
            {
                var pc = FindAnyObjectByType<PlayerCombat>();
                if (pc == null) return;
                player = pc.transform;
            }

            var samples = recording.samples;
            Vector3 p = player.position;
            int best = cursor;
            float bestSqr = (samples[cursor].Position - p).sqrMagnitude;

            int end = Mathf.Min(samples.Length - 1, cursor + ScanWindow);
            for (int k = cursor + 1; k <= end; k++)
            {
                float d = (samples[k].Position - p).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = k; }
            }

            // Only advance — progress along a speedrun course is monotonic, and letting the cursor slide
            // backwards would make the delta jitter every time the player circled an enemy.
            if (best > cursor) cursor = best;

            // Too far from the recorded path to mean anything (took a different route, or fell).
            HasDelta = bestSqr < 36f;   // 6 m
            GhostTimeAtPlayer = cursor * recording.TickInterval;
        }

        public void ResetPlayback()
        {
            cursor = 0;
            HasDelta = false;
            GhostTimeAtPlayer = 0f;
        }
    }
}
