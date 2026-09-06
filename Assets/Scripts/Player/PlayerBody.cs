using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The player's BODY — hips, chest and two three-segment legs of primitives under the Player root —
    /// and the poses it holds: a distance-driven gait, an air tuck, a wall-run lean, a landing dip and,
    /// the reason it exists, a SLIDE with the legs thrown out in front of the lens.
    ///
    /// <para><b>Why there was nothing here.</b> The Player prefab's only renderers were the two arm rigs,
    /// hung off the camera. Nothing lived under the root, so the player cast no shadow, looking down
    /// showed the floor, and a slide — a move whose whole read in every game that has one is the boots
    /// sticking out ahead of you — showed the floor going past and nothing else. <c>docs/BACKLOG.md</c>
    /// §2b scoped this slice as a pure addition, and that is what it is: one component, one factory
    /// call, no motor change.</para>
    ///
    /// <para><b>Under the ROOT, not the camera.</b> <c>PlayerLook</c> yaws the root and only pitches the
    /// pivot, so a body parented here turns with the player and stays level when they look down —
    /// which is how you get to SEE it when you look down. Nothing in the rest pose sits above y 1.35:
    /// the lens is at 1.60 and clips at 0.03, so at −89° pitch anything higher is inside the camera.</para>
    ///
    /// <para><b>The slide pose is built from the frame, not from anatomy.</b> During a slide the eye is
    /// at 1.05 m (the 0.55 m drop in <c>PlayerFeedback</c>) and the vertical half-FOV is 47.5°. The legs
    /// therefore go where the picture needs them: the hips sink to ~0.40 m and shift a quarter-metre
    /// FORWARD (a sliding torso leans back, so the hips lead the head), the leading leg straightens to
    /// 70° with the boot at about z 1.15 / y 0.05 — 41° below the horizon, comfortably in frame — and
    /// the trailing leg sits a little tighter so the two do not read as one. The leg root yaws to the
    /// VELOCITY, not the look: steer a slide and the legs go where you are going, which is the thing
    /// that makes it feel like a body rather than a decal. <see cref="AnkleFromHip"/> is the forward
    /// kinematics the pose is checked against in <c>SlideFeelTests</c>.</para>
    ///
    /// <para><b>Entry and exit are a spring, not a lerp.</b> The legs are THROWN out — reaching their
    /// pose in ~0.10 s and overshooting by about a tenth before settling — because a slide is a commit,
    /// and a body that eases into it politely reads as a crouch. <see cref="SlideImpulse.Spring"/> is
    /// closed-form, so the throw looks identical at 20 and 240 fps (a frame-rate-dependent slide has
    /// shipped here once already).</para>
    ///
    /// <para><b>Clocks.</b> The gait phase advances on <see cref="TimeScaleController.PlayerDelta"/>
    /// like the viewmodel bob (rule 1: player motion never reads <c>Time.deltaTime</c>, so hitstop
    /// cannot freeze your own legs mid-stride). Every ease uses unscaled time. No allocation per frame.
    /// Every number below is written by <c>PrefabFactory.BuildPlayerBody</c> (rule 9); the initialisers
    /// are documentation.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerBody : MonoBehaviour
    {
        [Header("Rig — built by PrefabFactory")]
        public Transform torso;
        public Transform legL, legR;
        public Transform kneeL, kneeR;
        public float thighLength = 0.46f;
        public float shinLength = 0.44f;
        [Tooltip("Height of the hip joints above the feet in the rest pose.")]
        public float hipHeight = 0.92f;

        [Header("Gait")]
        [Tooltip("Hip swing at full speed, degrees either side of vertical.")]
        public float gaitSwingDegrees = 30f;
        [Tooltip("Knee bend during the forward swing at full speed, degrees.")]
        public float gaitKneeDegrees = 42f;
        [Tooltip("Speed at which the gait reaches full amplitude. groundSpeed is 11.")]
        public float gaitFullSpeed = 11f;
        [Tooltip("Radians of stride phase per metre travelled. 1.3 matches the viewmodel bob, so the " +
                 "hands and the feet are in step.")]
        public float gaitPhasePerMetre = 1.3f;
        [Tooltip("Metres the hips drop at the mid-stride, at full speed.")]
        public float gaitHipBob = 0.02f;

        [Header("Air")]
        public float airHipDegrees = 14f;
        public float airKneeDegrees = 40f;

        [Header("Slide")]
        [Tooltip("Metres the hips SINK during a slide. 0.52 puts the hip joint at 0.40 m.")]
        public float slideHipSink = 0.52f;
        [Tooltip("Metres the hips shift FORWARD during a slide: the torso leans back, so the hips lead " +
                 "the head. This is what carries the boots out to where the lens can see them.")]
        public float slideHipForward = 0.25f;
        [Tooltip("Degrees the torso leans BACK during a slide.")]
        public float slideTorsoLean = 22f;
        [Tooltip("Leading (right) leg: hip angle forward of vertical and knee bend, degrees.")]
        public float slideLeadHip = 70f;
        public float slideLeadKnee = 12f;
        [Tooltip("Trailing (left) leg, slightly tighter so the two legs read as two.")]
        public float slideTrailHip = 60f;
        public float slideTrailKnee = 26f;
        [Tooltip("Natural frequency of the throw-out / fold-back spring, Hz. 6 Hz reaches the pose in " +
                 "about 0.10 s.")]
        public float slideBlendHz = 6f;
        [Tooltip("Damping ratio of that spring. 0.6 overshoots by about a tenth — the legs are THROWN, " +
                 "not placed. 1 = no overshoot.")]
        [Range(0.2f, 1f)] public float slideBlendDamping = 0.6f;
        [Tooltip("Cap on the overshoot: the blend may run this far past the pose and no further.")]
        public float slideBlendMax = 1.15f;

        [Header("Wall run")]
        [Tooltip("Degrees the legs swing TOWARD the face being run, so the boots plant on the wall.")]
        public float wallRunLegLean = 14f;
        [Tooltip("Degrees the torso leans OFF the wall while the legs lean into it.")]
        public float wallRunTorsoLean = 6f;

        [Header("Landing")]
        [Tooltip("Knee bend on a full-strength landing, degrees.")]
        public float landingKneeDegrees = 34f;
        [Tooltip("Landing speed that produces a full-strength dip. PlayerFeedback uses 22 for the camera.")]
        public float landingFullSpeed = 22f;
        [Tooltip("Spring-back rate of the landing dip. PlayerFeedback's camera dip uses 9.")]
        public float landingRecoverySpeed = 9f;
        [Tooltip("Knee bend when the legs come back under you at the end of a slide, degrees.")]
        public float standUpKneeDegrees = 12f;

        [Header("Smoothing")]
        [Tooltip("Per-second ease of every non-slide pose channel. The gait itself is phase-driven; " +
                 "this only softens the transitions between ground, air and wall.")]
        public float poseLerp = 14f;

        // ---------------------------------------------------------------- state
        FirstPersonMotor motor;
        float phase;
        float blend, blendVel;          // 0 = standing pose, 1 = slide pose, may overshoot to slideBlendMax
        float kneeDip, kneeDipVel;      // landing / stand-up knee bend, degrees, springs back to 0
        float slideYaw;                 // leg-root yaw toward the slide velocity, degrees
        float hipL, hipR, kneeLDeg, kneeRDeg, legRoll, torsoRoll;   // eased channels

        /// <summary>The slide blend being rendered this frame (0..slideBlendMax). For tests and the harness.</summary>
        public float SlideBlend { get { return blend; } }

        void Awake()
        {
            motor = GetComponentInParent<FirstPersonMotor>();
        }

        void OnEnable()
        {
            if (motor == null) return;
            motor.OnLanded += OnLanded;
            motor.OnSlideEnded += OnSlideEnded;
        }

        void OnDisable()
        {
            if (motor == null) return;
            motor.OnLanded -= OnLanded;
            motor.OnSlideEnded -= OnSlideEnded;
        }

        void OnLanded()
        {
            float k = Mathf.Clamp01(motor.LastLandingSpeed / Mathf.Max(0.01f, landingFullSpeed));
            if (k < 0.08f) return;   // stepping off a kerb — same threshold as the camera dip
            kneeDip += landingKneeDegrees * k;
        }

        void OnSlideEnded()
        {
            // The legs fold back under you and PLANT: a small knee bend that springs out, so standing
            // up reads as weight arriving on the feet rather than the pose simply switching.
            kneeDip += standUpKneeDegrees;
        }

        // ---------------------------------------------------------------- kinematics

        /// <summary>
        /// Where the ankle sits relative to the hip joint, in the body frame (+Z forward, +Y up), for a
        /// hip swung <paramref name="hipDeg"/> forward of vertical and a knee bent <paramref name="kneeDeg"/>
        /// back from the thigh line. Pure, so the slide pose can be checked against the lens without
        /// a scene.
        /// </summary>
        public static Vector3 AnkleFromHip(float hipDeg, float kneeDeg, float thigh, float shin)
        {
            float h = hipDeg * Mathf.Deg2Rad;
            float s = (hipDeg - kneeDeg) * Mathf.Deg2Rad;
            return new Vector3(0f, -thigh * Mathf.Cos(h) - shin * Mathf.Cos(s),
                                    thigh * Mathf.Sin(h) + shin * Mathf.Sin(s));
        }

        /// <summary>Degrees BELOW the horizon at which an eye at <paramref name="eyeHeight"/> sees a
        /// point <paramref name="ahead"/> metres forward at <paramref name="height"/>.</summary>
        public static float DegreesBelowHorizon(float eyeHeight, float ahead, float height)
        {
            return Mathf.Atan2(eyeHeight - height, Mathf.Max(0.0001f, ahead)) * Mathf.Rad2Deg;
        }

        // ---------------------------------------------------------------- tick

        void Update()
        {
            if (motor == null) return;
            float udt = Time.unscaledDeltaTime;
            if (udt <= 0f || !GameManager.IsPlaying) return;

            bool sliding = motor.IsSliding;
            bool grounded = motor.IsGrounded;
            bool wall = motor.IsWallRunning;
            float speed = motor.HorizontalSpeed;

            // ---- the slide throw: a closed-form spring, frame-rate independent by construction
            float nb, nv;
            SlideImpulse.Spring(blend, blendVel, sliding ? 1f : 0f,
                                2f * Mathf.PI * Mathf.Max(0.1f, slideBlendHz), slideBlendDamping, udt,
                                out nb, out nv);
            blend = Mathf.Clamp(nb, 0f, Mathf.Max(1f, slideBlendMax));
            blendVel = nv;
            if (!sliding && blend < 0.002f) { blend = 0f; blendVel = 0f; }

            // ---- gait phase: distance-driven, on the player's own clock (rule 1)
            float k = 0f;
            if ((grounded || wall) && !sliding && speed > 0.5f)
            {
                phase += TimeScaleController.PlayerDelta * speed * gaitPhasePerMetre;
                k = Mathf.Clamp01(speed / Mathf.Max(0.1f, gaitFullSpeed));
                if (wall) k *= 0.6f;
            }

            // ---- base pose (standing / running / air / wall)
            float tHipL, tHipR, tKneeL, tKneeR;
            if (grounded || wall)
            {
                float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
                tHipR = s * gaitSwingDegrees * k;
                tHipL = -s * gaitSwingDegrees * k;
                // The knee bends on the FORWARD swing (foot lifted through), straightens as it plants.
                tKneeR = Mathf.Max(0f, c) * gaitKneeDegrees * k;
                tKneeL = Mathf.Max(0f, -c) * gaitKneeDegrees * k;
            }
            else
            {
                tHipL = airHipDegrees; tHipR = airHipDegrees;
                tKneeL = airKneeDegrees; tKneeR = airKneeDegrees;
            }

            // ---- wall run: boots toward the face, torso off it. WallRunNormal points AWAY from the
            // face, so a wall on the right has a normal on -x and the legs must roll to +x.
            float tLegRoll = 0f, tTorsoRoll = 0f;
            if (wall)
            {
                Vector3 n = motor.transform.InverseTransformDirection(motor.WallRunNormal);
                float side = n.x < -0.01f ? 1f : n.x > 0.01f ? -1f : 0f;
                tLegRoll = side * wallRunLegLean;
                tTorsoRoll = -side * wallRunTorsoLean;
            }

            float ease = 1f - Mathf.Exp(-poseLerp * udt);
            hipL = Mathf.Lerp(hipL, tHipL, ease);
            hipR = Mathf.Lerp(hipR, tHipR, ease);
            kneeLDeg = Mathf.Lerp(kneeLDeg, tKneeL, ease);
            kneeRDeg = Mathf.Lerp(kneeRDeg, tKneeR, ease);
            legRoll = Mathf.Lerp(legRoll, tLegRoll, ease);
            torsoRoll = Mathf.Lerp(torsoRoll, tTorsoRoll, ease);

            // ---- landing / stand-up dip springs back like the camera's
            kneeDip = Mathf.SmoothDamp(kneeDip, 0f, ref kneeDipVel,
                                       1f / Mathf.Max(0.5f, landingRecoverySpeed), Mathf.Infinity, udt);

            // ---- slide: yaw the legs to the VELOCITY while the slide is live
            if (sliding)
            {
                Vector3 v = motor.Velocity; v.y = 0f;
                if (v.sqrMagnitude > 0.25f)
                {
                    float want = Vector3.SignedAngle(motor.transform.forward, v, Vector3.up);
                    slideYaw = Mathf.LerpAngle(slideYaw, want, ease);
                }
            }
            else slideYaw = Mathf.LerpAngle(slideYaw, 0f, ease);

            // ---- compose: base blended toward the slide pose by the spring (which may overshoot)
            float b = blend;
            float fHipR = Mathf.LerpUnclamped(hipR, slideLeadHip, b);
            float fHipL = Mathf.LerpUnclamped(hipL, slideTrailHip, b);
            float fKneeR = Mathf.LerpUnclamped(kneeRDeg, slideLeadKnee, b) + kneeDip;
            float fKneeL = Mathf.LerpUnclamped(kneeLDeg, slideTrailKnee, b) + kneeDip;
            float hipY = -slideHipSink * Mathf.Clamp01(b)
                       - gaitHipBob * k * Mathf.Abs(Mathf.Cos(phase))
                       - Mathf.Deg2Rad * kneeDip * 0.06f;     // a bent knee lowers the hips a little
            float hipZ = slideHipForward * Mathf.Clamp01(b);
            float torsoPitch = -slideTorsoLean * b;

            transform.localPosition = new Vector3(0f, hipY, hipZ);
            transform.localRotation = Quaternion.Euler(0f, slideYaw * Mathf.Clamp01(b), 0f);
            if (torso != null) torso.localRotation = Quaternion.Euler(torsoPitch, 0f, torsoRoll);
            // Euler(-hip) swings the leg FORWARD (+Z); Euler(+knee) on the knee bends the shin BACK.
            if (legL != null) legL.localRotation = Quaternion.Euler(-fHipL, 0f, legRoll);
            if (legR != null) legR.localRotation = Quaternion.Euler(-fHipR, 0f, legRoll);
            if (kneeL != null) kneeL.localRotation = Quaternion.Euler(Mathf.Max(0f, fKneeL), 0f, 0f);
            if (kneeR != null) kneeR.localRotation = Quaternion.Euler(Mathf.Max(0f, fKneeR), 0f, 0f);
        }
    }
}
