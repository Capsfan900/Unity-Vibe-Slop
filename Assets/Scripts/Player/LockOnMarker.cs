using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The Dark Souls lock-on dot: one small pale mote that sits on the centre of mass of whatever
    /// <see cref="LockOnController"/> currently holds. Presentation only — it owns no targeting logic,
    /// exactly as <see cref="EnemyVisuals"/> owns no fight logic.
    ///
    /// <para><b>Why ONE marker owned by the player rather than one per enemy.</b> The deathblow glyph and
    /// the alert cube are <i>properties of the enemy</i> ("my posture is broken", "my next swing is
    /// unblockable") so they are children of the enemy's visual root and there is one per enemy. Lock-on
    /// is a property of the <i>player</i> — at most one thing in the world is ever your target — so a
    /// single instance travels. That also means every lockable body gets the dot for free, including the
    /// three <c>Legendary_*</c> mini-bosses built by a different factory, without a prefab rebuild.</para>
    ///
    /// <para><b>It must not be confusable with the other two head markers.</b> Four axes separate it, and
    /// every one of them points the same way — quieter:</para>
    /// <list type="number">
    /// <item><b>Place.</b> It sits on the target's CHEST, not above its head, which is where both the
    /// alert tell and the deathblow glyph live. Nothing else in the game marks centre of mass.</item>
    /// <item><b>Size.</b> ~9 cm against the tell's 25 cm cube and the glyph's 70 cm crossed diamond, and
    /// it is held to a constant angular size so it never grows into them up close.</item>
    /// <item><b>Hue and level.</b> Pale bone-grey <c>M_LockOnDot</c>, deliberately UNDER the 1.05 bloom
    /// threshold, against two markers that are 2.5–3x over it. The dot is the one combat marker that
    /// does not bloom, because it is information, not an alarm.</item>
    /// <item><b>Motion.</b> Dead still. The deathblow glyph spins and breathes; this only eases in when
    /// it is acquired and then holds. A marker that is up for the whole fight must not draw the eye.</item>
    /// </list>
    ///
    /// <para>Unscaled time for the acquire ease, like every other marker: hitstop must not stall a
    /// pop-in, and the dot is HUD, not physics.</para>
    /// </summary>
    public class LockOnMarker : MonoBehaviour
    {
        [Tooltip("Renderers switched on and off with the lock. Assigned by PrefabFactory.")]
        public Renderer[] renderers = new Renderer[0];

        [Tooltip("Apparent size in world units per metre of distance — i.e. a constant angular size. " +
                 "A fixed-size dot is a boulder at 3 m and invisible at 25 m.")]
        public float angularSize = 0.0125f;
        [Tooltip("Floor on the world scale so the dot never vanishes point-blank.")]
        public float minScale = 0.045f;
        [Tooltip("Ceiling so a far target's dot cannot grow into the head markers.")]
        public float maxScale = 0.42f;

        [Tooltip("Metres the dot is pulled toward the camera off the target's centre of mass. THE DOT " +
                 "MARKS A POINT INSIDE THE BODY: the enemy capsule is 0.45 m in radius, so a dot left on " +
                 "the chest point renders inside the enemy and is never seen. Pulling it forward past " +
                 "the surface is cheaper and far less fragile than a depth-test-off overlay material, " +
                 "and it keeps the dot occluded by real cover, which is what we want.")]
        public float frontOffset = 0.75f;
        [Tooltip("Cap on frontOffset as a fraction of the distance, so the dot cannot end up in the " +
                 "player's face when the target is on top of them.")]
        public float frontOffsetMaxFraction = 0.3f;

        [Tooltip("Seconds of the acquire ease. It arrives large and settles — the only motion it has.")]
        public float acquireEase = 0.14f;
        [Tooltip("How much larger it starts on acquire, as a multiplier.")]
        public float acquirePop = 2.2f;

        Transform cam;
        float ease = 1f;
        bool shown;

        void Awake() { Show(false); }

        /// <summary>Called on the frame a target is acquired or switched: replays the settle.</summary>
        public void Pop() { ease = 0f; }

        /// <summary>Place the dot at a world point this frame. Call every frame the lock is held.</summary>
        public void Track(Vector3 worldPoint, bool visible)
        {
            Show(visible);
            if (!visible) return;

            cam = ViewCamera.Transform;
            if (cam == null) return;

            float udt = Time.unscaledDeltaTime;
            ease = acquireEase > 0.0001f ? Mathf.Min(1f, ease + udt / acquireEase) : 1f;
            float pop = Mathf.Lerp(acquirePop, 1f, ease * ease * (3f - 2f * ease));   // smoothstep

            Vector3 toCam = cam.position - worldPoint;
            float dist = toCam.magnitude;
            if (dist < 0.0001f) return;
            Vector3 dir = toCam / dist;

            // Lift it off the chest toward the eye, or it renders inside the capsule it is marking.
            transform.position = worldPoint + dir * Mathf.Min(frontOffset, dist * frontOffsetMaxFraction);
            // Billboard, full 3-axis: the dot is round, but the geometry is not, and a chest-height
            // marker is looked DOWN at from a ledge as often as it is looked at level.
            transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            float s = Mathf.Clamp(dist * angularSize, minScale, maxScale) * pop;
            transform.localScale = new Vector3(s, s, s);
        }

        public void Show(bool v)
        {
            if (v == shown) return;
            shown = v;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = v;
        }

        public bool IsShown => shown;
    }
}
