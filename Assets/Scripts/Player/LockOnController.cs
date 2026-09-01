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
    /// <para><b>THE MOUSE IS ALWAYS AUTHORITATIVE.</b> Nothing here scales, filters or subtracts from
    /// the player's look delta — <see cref="PlayerLook"/> applies mouse input exactly as it always has,
    /// and this runs afterwards in <c>LateUpdate</c> and adds a correction on top.</para>
    ///
    /// <para><b>Two things about that correction were wrong in the first version, and both of them read
    /// to a player as "the lock drifts off the enemy".</b></para>
    /// <list type="number">
    /// <item><b>It was a pure proportional controller.</b> P tracking a MOVING reference has an
    /// unavoidable steady-state error, so against an enemy circling at a steady rate the camera settled
    /// at a permanent lag and never closed. No gain fixes that. The correction is now feed-forward plus
    /// P: it matches the rate the target's bearing is changing at, and P only mops up the residue.
    /// And correction and tracking have SEPARATE rate ceilings, because a shared one meant a close fast
    /// enemy could out-sweep the camera entirely and lap it.</item>
    /// <item><b>The yield gate stood down on any mouse movement at all.</b> The rule was "the assist
    /// only spends frames the player is not using", which is right in spirit and wrong in practice — a
    /// real mouse is never still, so the assist was off whenever anyone was actually playing, and
    /// especially during the one manoeuvre it exists for (strafing round an enemy while nudging the
    /// mouse is two inputs at once). The gate is now DIRECTIONAL: only movement that pushes the camera
    /// AWAY from the target stands the assist down.</item>
    /// </list>
    ///
    /// <para>What the assist aims at is the enemy's VISIBLE BODY (<see cref="EnemyController.BodyPoint"/>),
    /// not its root transform. The root is the navigation position — the feet — and the body hanging off
    /// it is displaced by every lunge, dip and wind-up pose, so marking the root put the dot beside the
    /// enemy at exactly the moments the player was watching hardest.</para>
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

        [Tooltip("How much of the target's own bearing rate the camera matches, 0..1. THIS is what " +
                 "keeps a moving enemy framed; the proportional term above only cleans up the residue. " +
                 "At 0 the assist is pure P and drifts behind anything that moves, which is the bug " +
                 "this was added to fix. " +
                 "It is 1, not 0.9, and the difference is not cosmetic: any shortfall f leaves a " +
                 "PERMANENT steady-state lag of (1-f) x targetRate / assistGain, which grows with how " +
                 "fast the enemy is moving. At 0.9 and 80 deg/s that is 2 deg on top of the deadzone " +
                 "-- small on paper, and exactly the 'it drifts off' this was meant to fix. Only 1 " +
                 "makes the residual independent of the target's speed. Pinned by LockOnTrackingTests.")]
        [Range(0f, 1f)] public float feedForward = 1f;
        [Tooltip("Ceiling on the feed-forward term, deg/s. A teleport, a respawn or a target switch " +
                 "moves the bearing discontinuously; without this the camera would be whipped by it.")]
        public float feedForwardMaxRateDeg = 180f;

        [Header("Marker")]
        public LockOnMarker marker;
        [Tooltip("Height up the target, in ITS local space, that the dot sits at — chest, not head, " +
                 "because the head is where the alert tell and the deathblow glyph live.")]
        public float markerHeight = 1.05f;

        PlayerLook look;
        Transform cam;
        EnemyController target;
        float occludedFor;
        /// <summary>Last frame's bearing to the target, for the feed-forward term.</summary>
        float prevBearingYaw, prevBearingPitch;
        bool haveBearing;

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
            haveBearing = false;
            if (e == null || !e.IsAlive) return;
            target = e;
            occludedFor = 0f;
            if (marker != null) marker.Pop();
        }

        public void Release()
        {
            haveBearing = false;
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

        /// <summary>
        /// Hold the target framed.
        ///
        /// <para><b>Why this is not just a proportional pull, and why it used to drift.</b> The first
        /// version was pure P: <c>rate = gain x error</c>. A P controller tracking a target that is
        /// MOVING has an unavoidable steady-state error — it only produces correction in proportion to
        /// how far behind it already is, so against an enemy circling at a constant angular rate it
        /// settles at a permanent lag and never closes. That is not a tuning problem; no value of
        /// <see cref="assistGain"/> fixes it, because raising the gain only shrinks the lag while making
        /// the camera snappier and more likely to fight the mouse. The enemy sitting a little off centre
        /// forever is exactly what "the lock drifts off" describes.</para>
        ///
        /// <para>So the correction is now <b>feed-forward plus P</b>. The feed-forward term matches the
        /// rate the target's bearing is CHANGING at, which is what actually keeps a moving enemy framed;
        /// the P term only mops up whatever error is left. Against a target circling at a steady rate the
        /// feed-forward alone holds it and the P term goes to zero — which is the definition of not
        /// drifting.</para>
        ///
        /// <para>The two terms have different deadzones on purpose. P is suppressed inside
        /// <see cref="assistDeadzoneDeg"/> so a centred target does not jitter; feed-forward is NOT,
        /// because inside the deadzone is precisely where a tracked target lives, and suppressing it
        /// there would reintroduce the drift it exists to remove.</para>
        /// </summary>
        void Assist(Vector3 point)
        {
            if (look == null || InputReader.I == null) return;

            Vector3 to = point - cam.position;
            if (to.sqrMagnitude < 0.0001f) { haveBearing = false; return; }

            Vector3 dir = to.normalized;
            float desiredYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float desiredPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;

            float dt = TimeScaleController.PlayerDelta;

            // ---- feed-forward: how fast the target's BEARING is moving ------------------------
            // Differentiated from the enemy's ROOT, not from the body point the P term and the dot
            // use. The body point deliberately follows the lunge and the wind-up pose, and those
            // START and STOP abruptly -- differentiating them would inject a spike into the camera
            // every time an attack began. The root is driven by the NavMesh agent and moves smoothly,
            // so it is the honest source for "where is this thing going". The P term still closes the
            // pose offset, just without the camera being whipped by it.
            Vector3 rootTo = target.transform.TransformPoint(new Vector3(0f, markerHeight, 0f)) - cam.position;
            Vector3 rootDir = rootTo.sqrMagnitude > 0.0001f ? rootTo.normalized : dir;
            float rootYaw = Mathf.Atan2(rootDir.x, rootDir.z) * Mathf.Rad2Deg;
            float rootPitch = -Mathf.Asin(Mathf.Clamp(rootDir.y, -1f, 1f)) * Mathf.Rad2Deg;

            float ffYaw = 0f, ffPitch = 0f;
            if (haveBearing && dt > 0.00001f)
            {
                ffYaw = Mathf.DeltaAngle(prevBearingYaw, rootYaw) / dt;
                ffPitch = (rootPitch - prevBearingPitch) / dt;
                // A teleport, a respawn or a target switch produces a huge one-frame bearing jump.
                // Clamped rather than trusted: the assist may help you follow a running enemy, never
                // whip the camera because something moved discontinuously.
                ffYaw = Mathf.Clamp(ffYaw, -feedForwardMaxRateDeg, feedForwardMaxRateDeg);
                ffPitch = Mathf.Clamp(ffPitch, -feedForwardMaxRateDeg, feedForwardMaxRateDeg);
            }
            prevBearingYaw = rootYaw;
            prevBearingPitch = rootPitch;
            haveBearing = true;

            float gate = YieldGate(dir);
            if (gate <= 0.0001f) return;

            // ---- proportional: close whatever error is left -----------------------------------
            float err = Vector3.Angle(cam.forward, to);
            float dYaw = Mathf.DeltaAngle(look.Yaw, desiredYaw);
            float dPitch = desiredPitch - look.Pitch;

            float pScale = PScale(err, assistGain, assistDeadzoneDeg, assistFadeDeg);

            // ---- TWO ceilings, because these are two different things -------------------------
            // Correction and tracking used to share assistMaxRateDeg, and that quietly broke the
            // lock on exactly the targets it is most needed for. An enemy at 2 m moving 5 m/s sweeps
            // ~143 deg/s of bearing; capped at 90 the camera cannot physically keep up, falls behind
            // a little more every frame and eventually gets lapped. Caught by
            // LockOnTrackingTests.FeedForward_HoldsAMovingTarget at 140 deg/s, which lost the target
            // by 301 degrees.
            //
            // So: the P term keeps the modest ceiling, because a fast CORRECTION is what reads as the
            // camera snapping. The feed-forward term gets its own, higher one, because MATCHING a
            // target's motion never reads as a snap no matter how fast it is -- the enemy stays put
            // in frame, which is the whole point.
            float pYaw = dYaw * pScale, pPitch = dPitch * pScale;
            ClampPair(ref pYaw, ref pPitch, assistMaxRateDeg);

            float fYaw = ffYaw * feedForward, fPitch = ffPitch * feedForward;
            ClampPair(ref fYaw, ref fPitch, feedForwardMaxRateDeg);

            float rateYaw = (pYaw + fYaw) * gate;
            float ratePitch = (pPitch + fPitch) * gate;
            if (Mathf.Abs(rateYaw) < 0.0001f && Mathf.Abs(ratePitch) < 0.0001f) return;

            float stepYaw = rateYaw * dt;
            float stepPitch = ratePitch * dt;

            // Never overshoot this frame: a step past the remaining error would oscillate around the
            // target, which reads far worse than lagging behind it.
            if (Mathf.Abs(stepYaw) > Mathf.Abs(dYaw)) stepYaw = dYaw;
            if (Mathf.Abs(stepPitch) > Mathf.Abs(dPitch)) stepPitch = dPitch;

            look.NudgeAim(stepYaw, stepPitch);
        }

        /// <summary>
        /// The proportional gain in effect at a given bearing error: zero inside the deadzone, then
        /// fading up across <paramref name="fadeDeg"/> so the assist has no hard edge.
        ///
        /// <para>Pure and static so the control law can be closed-loop simulated in an EditMode test
        /// rather than argued about — see <c>LockOnTrackingTests</c>. The drift this replaced was a
        /// property of the CONTROL LAW, not of any value in the Inspector, and a property of a control
        /// law is something you can prove.</para>
        /// </summary>
        public static float PScale(float errDeg, float gain, float deadzoneDeg, float fadeDeg)
        {
            if (errDeg <= deadzoneDeg) return 0f;
            float fade = fadeDeg > 0.0001f ? Mathf.Clamp01((errDeg - deadzoneDeg) / fadeDeg) : 1f;
            return gain * fade;
        }

        /// <summary>
        /// Correction rate for one axis, deg/s: <c>feed-forward x bearing rate + P x error</c>.
        ///
        /// <para>The feed-forward term is what holds a MOVING target. Without it (feedForward = 0) this
        /// is a pure P controller, whose steady-state error against a target moving at a constant
        /// angular rate <c>w</c> settles at <c>w / gain</c> and never reaches zero — the enemy sits
        /// permanently off centre, which is what "the lock drifts off" meant. With feed-forward at 1 the
        /// camera matches the target's motion outright and P is left with nothing to do.</para>
        /// </summary>
        public static float AxisRate(float axisErrorDeg, float bearingRateDegPerSec,
                                     float pScale, float feedForward)
        {
            return bearingRateDegPerSec * feedForward + axisErrorDeg * pScale;
        }

        /// <summary>
        /// The full one-axis law including both ceilings — the 1-D form of what
        /// <see cref="Assist"/> runs on yaw and pitch together.
        ///
        /// <para>The two clamps are separate because correction and tracking are different things:
        /// <paramref name="pMaxRate"/> stops a big CORRECTION reading as a snap, while
        /// <paramref name="ffMaxRate"/> only has to be high enough to MATCH a fast target, which never
        /// reads as a snap because the enemy does not move in frame. Sharing one ceiling is what let a
        /// close, fast enemy outrun the camera entirely.</para>
        /// </summary>
        public static float TrackRate(float axisErrorDeg, float bearingRateDegPerSec, float pScale,
                                      float feedForward, float pMaxRate, float ffMaxRate)
        {
            float p = Mathf.Clamp(axisErrorDeg * pScale, -pMaxRate, pMaxRate);
            float ff = Mathf.Clamp(bearingRateDegPerSec * feedForward, -ffMaxRate, ffMaxRate);
            return p + ff;
        }

        /// <summary>Clamp a (yaw, pitch) rate pair by MAGNITUDE, so the direction is preserved.</summary>
        static void ClampPair(ref float a, ref float b, float max)
        {
            float mag = Mathf.Sqrt(a * a + b * b);
            if (mag <= max || mag < 0.0001f) return;
            float k = max / mag;
            a *= k; b *= k;
        }

        /// <summary>
        /// 1 while the player is not pushing the camera AWAY from the target, 0 while they are.
        ///
        /// <para><b>This used to yield to any mouse movement at all, and that is why the lock felt
        /// dead.</b> The rule was "the assist only spends frames the player is not using", which sounds
        /// right and is wrong in practice: a real mouse is never still. Ordinary hand tremor clears the
        /// 5 px/frame ceiling, so the assist was off almost whenever anyone was actually playing — and
        /// worse, it was off during exactly the manoeuvre lock-on exists for, because strafing round an
        /// enemy while nudging the mouse is two inputs at once.</para>
        ///
        /// <para>The gate is now DIRECTIONAL. Only the component of the look delta that increases the
        /// bearing error counts as the player taking the stick; movement toward the target, or across
        /// it, leaves the assist running. Deliberately looking away therefore still stands the assist
        /// down instantly and then breaks the lock at <see cref="breakAngleDeg"/> — the thing the
        /// original rule was protecting — while incidental motion no longer switches it off.</para>
        ///
        /// <para>The mouse stays authoritative throughout: nothing here scales or subtracts from the
        /// player's own look delta, which <see cref="PlayerLook"/> has already applied in full.</para>
        /// </summary>
        float YieldGate(Vector3 toTarget)
        {
            var input = InputReader.I;
            Vector2 d = input.LookDelta;
            if (d.sqrMagnitude < 0.000001f) return 1f;

            // Screen-space direction from the crosshair toward the target. Look delta is
            // (x = yaw right, y = pitch), and pitch is inverted relative to a raised nose, so the
            // vertical component is compared against -(pitch error).
            Vector3 dir = toTarget.normalized;
            float desiredYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float desiredPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector2 toErr = new Vector2(Mathf.DeltaAngle(look.Yaw, desiredYaw),
                                        -(desiredPitch - look.Pitch));

            // Already centred: there is no "away" to measure, so fall back to treating any input as
            // the player steering.
            if (toErr.sqrMagnitude < 0.0001f) return MagnitudeGate(d.magnitude);

            // Positive = pushing away from the target; negative = pushing toward it.
            float away = -Vector2.Dot(d, toErr.normalized);
            if (away <= 0f) return 1f;
            return MagnitudeGate(away);
        }

        /// <summary>The original magnitude ramp, now applied only to the AWAY component.</summary>
        float MagnitudeGate(float mag)
        {
            var input = InputReader.I;
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

        /// <summary>
        /// Where on the enemy the dot sits and the assist aims.
        ///
        /// <para>Asks the enemy for a point on its VISIBLE BODY rather than transforming its own root.
        /// The root is the navigation position — the feet — and the body hanging off it is displaced
        /// every time the enemy lunges, dips or holds a wind-up pose, by up to a metre and a half on the
        /// heavy attacks. Marking the root put the dot in the air beside the enemy during precisely the
        /// moments the player is watching it hardest, and pulled the camera assist there with it.</para>
        /// </summary>
        Vector3 PointOn(EnemyController e)
        {
            return e.BodyPoint(markerHeight);
        }
    }
}
