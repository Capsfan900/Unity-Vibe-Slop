using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The Sekiro deathblow mark: ONE SMALL FLAT GLOWING SPOT on the enemy's sternum while its posture
    /// is broken. Switched on and off by <see cref="EnemyVisuals.SetDeathblowReady"/>, and a child of
    /// the enemy's visual root, so it tracks the body for free and dies with it.
    ///
    /// <para><b>Flat, and small — and small at every distance.</b> This was a crossed diamond built from
    /// three cubes floating over the head: an object in the world rather than a mark on a body, and at
    /// deathblow range a solid violet mass the camera runs into. It is now a single billboarded quad,
    /// rolled 45 degrees to a diamond. No volume, nothing to intersect the near plane.</para>
    ///
    /// <para><b>Its size is ANGULAR, not fixed</b> (<see cref="angularSize"/>), which is the part that
    /// actually killed the earlier flat version. The spot does not sit on the body — it stands
    /// <see cref="surfaceOffset"/> metres <i>off</i> the chest toward the viewer, because a mark on the
    /// centre line renders inside the mesh. So as the player closes on the enemy the spot is always
    /// nearer than the body it marks, and a fixed-size quad grows faster than the enemy does: measured
    /// at 0.22 m authored, it filled a quarter of the frame while the grunt behind it filled a fifth.
    /// Holding a constant angular size makes it a mark that reads the same from three metres and from
    /// one, which is what the lock-on dot already does one system over.</para>
    ///
    /// <para><b>It is gone before the wand arrives.</b> <c>EnemyController.BeginExecuted</c> calls
    /// <c>SetDeathblowReady(false)</c> on the press frame, so the spot drops the instant the player
    /// commits — before the melee commit, before the cock, long before the stab. It says "press now";
    /// once the press has happened it has no work left, and <c>ExecuteInteractor.CommitCue</c> takes over
    /// that exact point with the violet shatter. Nothing it draws can ever be between the wand tip and
    /// the body at contact.</para>
    ///
    /// <para><b>Why the torso and not the head.</b> Sekiro's mark is on the enemy, and the head is
    /// already taken: the alert cube lives at 2.5 and the world posture bar at 2.6. On a 2.2x-scale boss
    /// an overhead glyph is five metres in the air — out of the frame entirely at deathblow range, which
    /// is the one moment it has to be readable. On the sternum it is exactly where the player is looking
    /// and exactly where the wand is about to go in.</para>
    ///
    /// <para><b>A marker at a body's centre of mass renders INSIDE the mesh and is invisible.</b> That is
    /// a logged trap in this project — the lock-on dot passed every assertion for a whole pass while
    /// being unseeable. So the spot does not sit on the centre line: every frame
    /// <see cref="SurfacePoint"/> steps it <see cref="surfaceOffset"/> metres along the horizontal
    /// direction to the eye, which plants it on the front surface of the body facing the player. That
    /// same point is the single source of truth for the whole beat — the commit shatter and the wand
    /// blast are both drawn there, so the mark, the shatter and the explosion land on the same pixels.</para>
    ///
    /// <para><b>It has to be separable in one glance from BOTH other combat markers</b> — and the lock-on
    /// dot is a spot on the same torso, so this matters. Five axes, every one pointing the same way,
    /// this is the loud one:</para>
    /// <list type="number">
    /// <item><b>Place.</b> Sternum (<see cref="bodyHeight"/> 1.45) against the lock dot's centre of mass
    /// (1.05) — 0.40 m apart on a grunt, 0.88 m on the boss — and the alert cube at 2.5 over the
    /// head.</item>
    /// <item><b>Size.</b> Roughly 5% of the frame against the lock dot's ~1%, both held to a constant
    /// angular size so neither can grow into the other up close.</item>
    /// <item><b>Hue and level.</b> Arc violet <c>M_DeathblowMark</c> at peak 2.60, 2.5x over the bloom
    /// threshold, against the dot's pale bone-grey 0.81, deliberately under it. This one blooms; the
    /// dot cannot. And violet against the alert tell's hot pink-white.</item>
    /// <item><b>Silhouette.</b> A diamond (the quad is rolled 45 degrees) against a plain round pip.</item>
    /// <item><b>Motion.</b> This one rolls and breathes; the lock dot is dead still and the alert cube
    /// does not move either. Motion is the axis that survives peripheral vision, bloom and a
    /// colour-blind player, which is why it is here and not just a recolour.</item>
    /// </list>
    ///
    /// <para>Unscaled time throughout: hitstop is fired by the very blow this mark is inviting, and a
    /// spot that freezes on the frame the player commits reads as a dropped frame.</para>
    /// </summary>
    public class DeathblowMarker : MonoBehaviour
    {
        [Tooltip("Degrees per second of roll about the view axis. Slow enough to read as deliberate.")]
        public float spinSpeed = 110f;
        [Tooltip("Bob amplitude in local units, added to bodyHeight.")]
        public float bobAmount = 0.05f;
        public float bobSpeed = 3.2f;
        [Tooltip("How far the glyph breathes either side of its authored size. The pulse is what makes " +
                 "it read as an invitation rather than as scenery.")]
        [Range(0f, 0.5f)] public float pulseAmount = 0.16f;
        public float pulseSpeed = 5.5f;

        [Header("Visibility")]
        [Tooltip("Whether the mark is DRAWN. Ships TRUE. Turning it off leaves the object as a pure " +
                 "anchor — the commit shatter and the wand blast are still drawn at SurfacePoint — which " +
                 "is why the flag exists at all. Rule 9: written by PrefabFactory / MiniBossFactory, and " +
                 "re-applied every OnEnable so a stray Inspector edit cannot drift it.")]
        public bool drawMark = true;

        [Header("Body mounting")]
        [Tooltip("Height up the enemy's VISUAL ROOT that the glyph rides at, in local (pre-scale) units. " +
                 "The upper sternum on a 2 m body. NOT read off the transform, because the factory ships " +
                 "the marker INACTIVE and Awake never runs — so callers that need this point before the " +
                 "first posture break would read a zero. Rule 9: written by PrefabFactory / MiniBossFactory.")]
        public float bodyHeight = 1.45f;
        [Tooltip("Metres, in the visual root's local space, that the spot stands off the body's centre " +
                 "LINE toward the viewer. A marker left on the centre of mass renders inside the mesh " +
                 "and is never seen (see ENGINEERING-LOG: the lock-on dot). Must clear the widest part " +
                 "of THIS silhouette — the factories measure it per model, so a robed wraith and a " +
                 "squat wide-armed robot each get their own value. Kept as tight as it can be: every " +
                 "centimetre here is a centimetre the spot is nearer the camera than the body it marks.")]
        public float surfaceOffset = 0.58f;

        [Header("Angular size")]
        [Tooltip("World units of spot per metre of distance — i.e. a CONSTANT ANGULAR SIZE, about 5% of " +
                 "the frame. A fixed-size quad is the bug this replaced: the spot stands off the chest " +
                 "toward the viewer, so it is always nearer than the body and grows faster than the body " +
                 "does as the player closes. At 0.22 m fixed it filled a quarter of the frame at " +
                 "stabbing range.")]
        public float angularSize = 0.115f;
        [Tooltip("Floor on the world size so the spot never vanishes point-blank.")]
        public float minScale = 0.10f;
        [Tooltip("Ceiling so a distant enemy's mark cannot grow into the head markers.")]
        public float maxScale = 0.42f;
        [Tooltip("Cap on surfaceOffset as a fraction of the distance to the eye, so the spot cannot end " +
                 "up in the player's face when the body is on top of them.")]
        public float frontOffsetMaxFraction = 0.35f;

        Vector3 baseLocalScale = Vector3.one;
        bool captured;
        float t;
        Transform cam;

        /// <summary>
        /// Where the glyph sits in the world for a viewer at <paramref name="eye"/>, whether or not the
        /// marker is currently active. This is the single source of truth for the deathblow's contact
        /// point: the commit burst, the wand's blast and the marker itself must all land on the same
        /// pixels or the beat comes apart into three unrelated effects.
        /// </summary>
        public Vector3 SurfacePoint(Vector3 eye)
        {
            Transform p = transform.parent;
            if (p == null) return transform.position;
            Vector3 axis = p.TransformPoint(new Vector3(0f, bodyHeight, 0f));
            Vector3 to = eye - axis;
            to.y = 0f;
            float m = to.magnitude;
            if (m < 0.0001f) return axis;
            float s = Mathf.Max(0.01f, p.lossyScale.x);
            // Capped as a fraction of the distance as well, exactly like the lock-on dot: a fixed
            // stand-off is a spot in the player's face when the body is on top of them.
            float step = Mathf.Min(surfaceOffset * s, Vector3.Distance(eye, axis) * frontOffsetMaxFraction);
            return axis + (to / m) * step;
        }

        void OnEnable()
        {
            // Always appear at full size on the frame the posture breaks. Starting mid-pulse made the
            // spot pop in small on roughly half the breaks, which is the one frame it must not be quiet.
            if (!captured) { baseLocalScale = transform.localScale; captured = true; }
            t = 0f;
            // Renderer.enabled, never SetActive: this object's active state IS the deathblow window and
            // is read as such by EnemyVisuals and by the tests. Hiding it by deactivating would throw
            // the window away with the pixels (same reasoning as the viewmodel — see ENGINEERING-LOG).
            var rs = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++) rs[i].enabled = drawMark;
            Place();
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            Place();
        }

        void Place()
        {
            if (cam == null)
            {
                var c = Camera.main;
                if (c != null) cam = c.transform;
            }

            Transform p = transform.parent;
            Vector3 local = new Vector3(0f, bodyHeight + Mathf.Sin(t * bobSpeed) * bobAmount, 0f);

            float parentScale = p != null ? Mathf.Max(0.01f, p.lossyScale.x) : 1f;

            if (cam != null && p != null)
            {
                // Off the centre line, onto the surface facing the eye. Horizontal only: lifting it
                // toward a camera looking down from a ledge would slide the mark up the chest.
                Vector3 toEye = p.InverseTransformPoint(cam.position);
                toEye.y = 0f;
                if (toEye.sqrMagnitude > 0.000001f)
                {
                    float distLocal = Vector3.Distance(p.InverseTransformPoint(cam.position), local);
                    float step = Mathf.Min(surfaceOffset, distLocal * frontOffsetMaxFraction);
                    local += toEye.normalized * step;
                }
            }
            transform.localPosition = local;

            // CONSTANT ANGULAR SIZE, breathing. The quad child is authored at 1.0 so this scale is the
            // spot's world size directly, and the pulse rides on top of it — the one motion axis that
            // separates this from the dead-still lock-on dot.
            float world = minScale;
            if (cam != null)
            {
                float d = Vector3.Distance(cam.position, transform.position);
                world = Mathf.Clamp(d * angularSize, minScale, maxScale);
            }
            float pulse = 1f + Mathf.Cos(t * pulseSpeed) * pulseAmount;
            float k = world * pulse / parentScale;
            transform.localScale = new Vector3(k, k, k);

            if (cam != null)
            {
                // Billboard, then ROLL about the view axis. The mark is a single flat quad, so facing
                // the eye is what makes it a mark on a body rather than a card floating beside one —
                // and the roll is only visible at all because the quad is a diamond. The old marker
                // spun about world up, which presented its silhouette edge-on for half of every
                // revolution: a legibility hole in the one marker that has to be read instantly.
                Vector3 fwd = transform.position - cam.position;
                if (fwd.sqrMagnitude > 0.000001f)
                    transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up) *
                                         Quaternion.Euler(0f, 0f, spinSpeed * t);
            }
        }
    }
}
