using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Dark Souls target lock, adapted to first person.
    ///
    /// <para><b>The problem.</b> In a third-person Souls game lock-on takes the camera off the player and
    /// orbits it around the target; there is no camera to orbit here, and the mouse is the parry hand.
    /// With a 0.13 s perfect window, a camera that argues with the mouse is not a feature, it is input
    /// lag. So the assist is built on one rule:</para>
    ///
    /// <para><b>THE MOUSE IS ALWAYS AUTHORITATIVE. The assist only spends frames the player is not
    /// using.</b> Nothing here scales, filters or subtracts from the player's look delta — <see
    /// cref="PlayerLook"/> applies mouse input exactly as it always has, and this runs afterwards in
    /// <c>LateUpdate</c> and adds a correction on top. That correction is multiplied by a yield gate that
    /// falls to ZERO the moment the mouse is genuinely moving, so an assist frame and a player frame
    /// never overlap. What is left is the case lock-on exists for: you are strafing around an enemy on
    /// A/D with the mouse still, and the world turns to keep it framed.</para>
    ///
    /// <para>Mousing hard away (past <see cref="breakAngleDeg"/>) drops the lock outright rather than
    /// pulling back — being dragged toward something you have decided to stop looking at is the one
    /// failure mode of every bad aim assist.</para>
    ///
    /// <para>Rule 1: the correction is integrated with <see cref="TimeScaleController.PlayerDelta"/>. On
    /// scaled time the assist would stall during the hitstop of the very parry it is helping you land,
    /// which reads as a dropped frame at the worst possible moment.</para>
    /// </summary>
    public class LockOnController : MonoBehaviour
    {
        [Header("Acquisition")]
        [Tooltip("Farthest an enemy may be to be locked in the first place.")]
        public float acquireRange = 26f;
        [Tooltip("Lock drops beyond this. Deliberately wider than acquireRange so a step backwards " +
                 "during a fight does not flicker the lock off and on.")]
        public float dropRange = 32f;
        [Tooltip("Half-angle of the cone off the crosshair searched for candidates.")]
        public float acquireConeDeg = 55f;
        [Tooltip("Aim this far off the held target and the next press SWITCHES instead of releasing.")]
        public float switchAngleDeg = 14f;
        [Tooltip("Aim this far off the held target and the lock drops on its own — you have looked away.")]
        public float breakAngleDeg = 62f;
        [Tooltip("Seconds of unbroken occlusion before the lock drops. A pillar you strafe past must " +
                 "not cost you the lock; standing behind one must.")]
        public float occlusionGrace = 0.7f;
        [Tooltip("Metres of distance worth one degree off the crosshair when scoring candidates. " +
                 "Low, so the thing you are LOOKING at wins over the thing that is merely closer.")]
        public float distanceWeightDegPerMetre = 0.35f;

        [Header("Camera assist")]
        [Tooltip("Degrees per second of correction per degree of error. The assist is proportional so " +
                 "it eases in and out instead of arriving and stopping.")]
        public float assistGain = 4f;
        [Tooltip("Hard ceiling on the correction rate. Above ~120 the camera reads as snapping.")]
        public float assistMaxRateDeg = 90f;
        [Tooltip("No assist at all inside this cone. Without it the dot jitters on the crosshair.")]
        public float assistDeadzoneDeg = 2.2f;
        [Tooltip("Degrees over the deadzone across which the assist fades up, so its edge is not a step.")]
        public float assistFadeDeg = 8f;
        [Tooltip("Mouse delta (px/frame) at which the assist starts standing down.")]
        public float yieldMouseMin = 0.6f;
        [Tooltip("Mouse delta (px/frame) at which the assist is fully off. The player has the stick.")]
        public float yieldMouseMax = 5f;
        [Tooltip("Same gate for a gamepad stick, whose Look delta is a 0..1 axis, not pixels.")]
        public float yieldStickMin = 0.12f;
        public float yieldStickMax = 0.5f;

        [Header("Marker")]
        public LockOnMarker marker;
        [Tooltip("Height up the target, in ITS local space, that the dot sits at — chest, not head, " +
                 "because the head is where the alert tell and the deathblow glyph live.")]
        public float markerHeight = 1.05f;

        PlayerLook look;
        Transform cam;
        EnemyController target;
        float occludedFor;

        /// <summary>The enemy currently locked, or null.</summary>
        public EnemyController Target => target;
        public bool HasTarget => target != null && target.IsAlive;
        /// <summary>World point the dot sits on and the assist aims at.</summary>
        public Vector3 TargetPoint => target != null ? PointOn(target) : Vector3.zero;

        void Awake()
        {
            look = GetComponent<PlayerLook>();
        }

        void Update()
        {
            if (!GameManager.IsPlaying || InputReader.I == null) return;
            if (InputReader.I.LockOnPressed) TryLockOn();
        }

        /// <summary>
        /// The single public entry point for the lock key, per the Input invariant in DATAFLOW.md: it
        /// takes no input and re-applies every gate itself, so tests and (later) a network command
        /// stream drive the real behaviour without a synthesised device.
        ///
        /// <para>One key does all three verbs, resolved by where you are looking — there is no second
        /// binding to find and no modifier to hold. Scroll wheel, the other Souls convention, is already
        /// weapon cycling here.</para>
        /// <list type="bullet">
        /// <item>Not locked → acquire the candidate nearest the crosshair.</item>
        /// <item>Locked, still looking at your target → release.</item>
        /// <item>Locked but looking <see cref="switchAngleDeg"/> or more away → switch to whatever is
        /// nearest the crosshair now; release if that is still the same enemy.</item>
        /// </list>
        /// </summary>
        /// <returns>true if a target is held when this returns.</returns>
        public bool TryLockOn()
        {
            if (cam == null && look != null) cam = look.Cam;
            if (cam == null) return false;

            if (HasTarget)
            {
                float off = Vector3.Angle(cam.forward, PointOn(target) - cam.position);
                if (off < switchAngleDeg) { Release(); return false; }

                var next = FindBest();
                if (next == null || next == target) { Release(); return false; }
                Acquire(next);
                return true;
            }

            var best = FindBest();
            if (best == null) return false;
            Acquire(best);
            return true;
        }

        public void Acquire(EnemyController e)
        {
            if (e == null || !e.IsAlive) return;
            target = e;
            occludedFor = 0f;
            if (marker != null) marker.Pop();
        }

        public void Release()
        {
            target = null;
            occludedFor = 0f;
            if (marker != null) marker.Show(false);
        }

        void LateUpdate()
        {
            if (cam == null && look != null) cam = look.Cam;
            if (cam == null) return;

            if (!GameManager.IsPlaying)
            {
                if (marker != null) marker.Show(false);
                return;
            }

            Validate();

            if (!HasTarget)
            {
                if (marker != null) marker.Show(false);
                return;
            }

            Vector3 point = PointOn(target);
            bool clear = !Occluded(point);
            if (marker != null) marker.Track(point, clear);
            Assist(point);
        }

        /// <summary>Every automatic drop condition lives here: dead, destroyed, out of range, looked
        /// away from, or occluded for longer than the grace.</summary>
        void Validate()
        {
            if (target == null) return;
            if (!target.IsAlive) { Release(); return; }

            Vector3 point = PointOn(target);
            if ((point - cam.position).sqrMagnitude > dropRange * dropRange) { Release(); return; }
            if (Vector3.Angle(cam.forward, point - cam.position) > breakAngleDeg) { Release(); return; }

            // Unscaled: the grace is a real-world patience timer, not a gameplay duration, and must not
            // stretch under slow-mo.
            if (Occluded(point))
            {
                occludedFor += Time.unscaledDeltaTime;
                if (occludedFor >= occlusionGrace) Release();
            }
            else occludedFor = 0f;
        }

        void Assist(Vector3 point)
        {
            if (look == null || InputReader.I == null) return;

            Vector3 to = point - cam.position;
            if (to.sqrMagnitude < 0.0001f) return;

            float err = Vector3.Angle(cam.forward, to);
            if (err <= assistDeadzoneDeg) return;

            float gate = YieldGate();
            if (gate <= 0.0001f) return;

            float fade = assistFadeDeg > 0.0001f
                ? Mathf.Clamp01((err - assistDeadzoneDeg) / assistFadeDeg)
                : 1f;

            float rate = Mathf.Min(assistGain * err, assistMaxRateDeg) * gate * fade;
            float step = rate * TimeScaleController.PlayerDelta;
            if (step <= 0f) return;

            Vector3 dir = to.normalized;
            float desiredYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float desiredPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;

            float dYaw = Mathf.DeltaAngle(look.Yaw, desiredYaw);
            float dPitch = desiredPitch - look.Pitch;

            float mag = Mathf.Sqrt(dYaw * dYaw + dPitch * dPitch);
            if (mag < 0.0001f) return;
            float k = Mathf.Min(step / mag, 1f);
            look.NudgeAim(dYaw * k, dPitch * k);
        }

        /// <summary>1 while the mouse is still, 0 while it is moving. The whole "help, not fight".</summary>
        float YieldGate()
        {
            var input = InputReader.I;
            float mag = input.LookDelta.magnitude;
            float lo = input.LookIsMouse ? yieldMouseMin : yieldStickMin;
            float hi = input.LookIsMouse ? yieldMouseMax : yieldStickMax;
            if (hi <= lo) return mag <= lo ? 1f : 0f;
            return 1f - Mathf.Clamp01((mag - lo) / (hi - lo));
        }

        EnemyController FindBest()
        {
            EnemyController best = null;
            float bestScore = float.MaxValue;
            var list = EnemyController.ActiveEnemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || !e.IsAlive) continue;

                Vector3 point = PointOn(e);
                Vector3 to = point - cam.position;
                float dist = to.magnitude;
                if (dist > acquireRange || dist < 0.05f) continue;

                float ang = Vector3.Angle(cam.forward, to);
                if (ang > acquireConeDeg) continue;
                if (Occluded(point)) continue;

                float score = ang + dist * distanceWeightDegPerMetre;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        /// <summary>
        /// Line of sight from the eye to the target's chest, ignoring the Player and Enemy layers and
        /// every trigger. Enemy layer is excluded on purpose: an enemy standing in front of the one you
        /// are trying to lock must not count as a wall, and rule 6's cousin applies — a trigger volume is
        /// not cover.
        /// </summary>
        bool Occluded(Vector3 point)
        {
            Vector3 from = cam.position;
            Vector3 to = point - from;
            float d = to.magnitude;
            if (d < 0.05f) return false;
            int mask = ~(Layers.PlayerMask | Layers.EnemyMask);
            return Physics.Raycast(from, to / d, d - 0.05f, mask, QueryTriggerInteraction.Ignore);
        }

        Vector3 PointOn(EnemyController e)
        {
            return e.transform.TransformPoint(new Vector3(0f, markerHeight, 0f));
        }
    }
}
