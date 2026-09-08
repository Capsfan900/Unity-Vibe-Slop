using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Spins the weapon in hand, for fun. Asked for by the user 2026-09-07: press the flourish key and
    /// the thing you are holding does a flip. Default F11, and the ONE rebindable key in the game —
    /// SETTINGS &gt; CONTROL &gt; FLOURISH KEY writes an override that
    /// <see cref="InputReader.ApplyWeaponTwirlOverride"/> applies.
    ///
    /// <para><b>Additive, and deliberately not part of the pose pipeline.</b>
    /// <see cref="WeaponViewmodel"/> owns <c>model.localRotation</c> and rewrites it every LateUpdate
    /// from the idle / guard / swing poses, so anything written there is gone the next frame and any
    /// attempt to share it would mean editing that system. It does not write <c>grip.localRotation</c>
    /// at all — it only ever sets <c>grip.localPosition</c>, when it aligns a newly equipped model's
    /// grip node to the hand. That rotation is therefore a FREE channel, and this component is the only
    /// writer of it. The equipped model hangs off the grip, so spinning the grip spins the weapon while
    /// every pose, sway and bob above it keeps working untouched. Delete this component and the
    /// viewmodel behaves exactly as it did — the same contract <see cref="MovementPose"/> keeps.</para>
    ///
    /// <para><b>Cosmetic only.</b> No collider, no timing, no damage, no state. It cannot influence a
    /// swing, a parry window or a hitbox, because nothing downstream reads the grip's rotation. It also
    /// writes no <c>Time.timeScale</c> (hard rule 1) and touches no Input System type (hard rule 2 —
    /// <see cref="InputReader"/> owns the action; this only READS a bool property off it, the same way
    /// every other gameplay script does).</para>
    ///
    /// <para>Unscaled time, like the viewmodel's own sway and bob: a flourish that stalls in hitstop
    /// reads as a dropped frame rather than as the world holding still.</para>
    /// </summary>
    public class WeaponTwirl : MonoBehaviour
    {
        [Tooltip("Seconds for one full revolution.")]
        public float spinSeconds = 0.42f;

        [Tooltip("Local axis of the grip to spin about. X flips the weapon end over end.")]
        public Vector3 axis = Vector3.right;

        [Tooltip("How many revolutions a single press is worth.")]
        public int spinsPerPress = 1;

        [Tooltip("Mashing the key stacks up to this many revolutions. Purely so it is fun to hold on.")]
        public int maxQueuedSpins = 6;

        WeaponViewmodel viewmodel;
        Transform spun;                 // the grip we are turning, cached so we notice it being swapped
        Quaternion restRotation = Quaternion.identity;
        float turns;                    // revolutions still owed, counts down
        float total;                    // revolutions this burst started with, for the ease

        public bool Spinning => turns > 0f;

        void Awake()
        {
            if (viewmodel == null) viewmodel = GetComponentInParent<WeaponViewmodel>();
        }

        Transform Grip()
        {
            if (viewmodel == null) viewmodel = GetComponentInParent<WeaponViewmodel>();
            if (viewmodel == null) viewmodel = FindAnyObjectByType<WeaponViewmodel>();
            return viewmodel != null ? viewmodel.grip : null;
        }

        /// <summary>Start a spin, or add another revolution to one already running.</summary>
        public void Twirl()
        {
            var grip = Grip();
            if (grip == null || spinSeconds <= 0.0001f) return;

            // A fresh burst captures the rest pose. A burst already running must NOT recapture it, or
            // the weapon would settle to a mid-spin angle and the rest pose would drift a little further
            // every press until the thing sat crooked in the hand.
            if (!Spinning || spun != grip)
            {
                spun = grip;
                restRotation = grip.localRotation;
                turns = 0f;
                total = 0f;
            }

            turns = Mathf.Min(turns + Mathf.Max(1, spinsPerPress), maxQueuedSpins);
            total = Mathf.Max(total, turns);
        }

        /// <summary>Snap back to the rest pose and forget the burst.</summary>
        public void Cancel()
        {
            if (spun != null) spun.localRotation = restRotation;
            spun = null;
            turns = 0f;
            total = 0f;
        }

        void OnDisable()
        {
            // Never leave the weapon parked at a random angle for the next thing that equips it.
            Cancel();
        }

        /// <summary>
        /// The key. Gated on <see cref="GameManager.IsPlaying"/> exactly the way <c>DebugKeys</c> gates
        /// its own, so a flourish cannot be started under a menu, a pause or the death screen — where
        /// the cursor is free and the press probably meant something else. Reading in <c>Update</c> and
        /// spinning in <c>LateUpdate</c> keeps the flourish behind the viewmodel's own pose pass.
        /// </summary>
        void Update()
        {
            if (!GameManager.IsPlaying) return;
            var input = InputReader.I;
            if (input != null && input.WeaponTwirlPressed) Twirl();
        }

        void LateUpdate()
        {
            if (turns <= 0f) return;

            // The grip is replaced when a different weapon is equipped. Spinning a stale transform would
            // leave the old one rotated forever, so stop rather than guess.
            var grip = Grip();
            if (grip == null || grip != spun) { Cancel(); return; }

            turns -= Time.unscaledDeltaTime / spinSeconds;
            if (turns <= 0f)
            {
                // Land exactly on the rest pose. Interpolating to "close enough" is how a flourish
                // leaves a weapon a degree off true, once per press, forever.
                grip.localRotation = restRotation;
                spun = null;
                turns = 0f;
                total = 0f;
                return;
            }

            // Ease only the LAST revolution, so a queued burst spins at a constant rate and then settles
            // instead of stuttering at every revolution boundary.
            float done = total - turns;
            float angle;
            if (turns <= 1f)
            {
                float k = 1f - turns;                       // 0 -> 1 across the final revolution
                angle = (done - (1f - turns) + Ease(k)) * 360f;
            }
            else angle = done * 360f;

            var a = axis.sqrMagnitude < 0.0001f ? Vector3.right : axis.normalized;
            grip.localRotation = restRotation * Quaternion.AngleAxis(angle, a);
        }

        /// <summary>Smoothstep: the flourish arrives rather than stopping dead.</summary>
        static float Ease(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }
    }
}
