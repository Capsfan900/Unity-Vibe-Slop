using UnityEngine;

namespace VibeGame1
{
    public class PlayerLook : MonoBehaviour
    {
        public Transform pivot;      // pitch
        public Transform cam;        // the actual camera transform
        public float mouseSensitivity = 0.08f;
        public float stickSensitivity = 180f;

        [Header("Roll")]
        [Tooltip("How fast the sustained lean chases its target, in units of 1/second. 9 settles a " +
                 "13 degree wall-run lean in about a fifth of a second: fast enough to arrive with the " +
                 "run, slow enough that it reads as leaning rather than snapping.")]
        public float rollBiasLerp = 9f;

        float yaw, pitch;

        // TWO SUMMED ROLL CHANNELS, on the CameraShake model (Add + Kick) and for the same reason: a
        // sustained state and a momentary event are different things and neither should overwrite the
        // other. `rollBias` is the state - the wall-run lean, held for as long as you are on the wall.
        // `rollKick` is the event - the snap away from the face as you leave it. They add.
        //
        // BOTH ARE APPLIED ABOUT THE CAMERA'S OWN FORWARD AXIS, as the Z term of the pivot's local
        // euler. Unity composes Quaternion.Euler(x, y, z) as Ry * Rx * Rz, and Rz leaves +Z fixed, so
        // AimForward is bit-for-bit unchanged by any amount of roll. Rolling is the one camera effect
        // that CANNOT disturb aim, which is exactly why it is the one used here.
        float rollBias, rollBiasTarget;
        float rollKickAmp, rollKickStart, rollKickDuration;

        public Vector3 AimForward => cam != null ? cam.forward : transform.forward;
        public Transform Cam => cam;

        /// <summary>Current look angles. Read-only to everyone: this class owns rotation (see
        /// DATAFLOW.md > Movement) and rewrites the transform every frame.</summary>
        public float Yaw => yaw;
        public float Pitch => pitch;
        /// <summary>Total camera roll currently applied, degrees: the sustained lean plus the kick.</summary>
        public float Roll => rollBias + KickRoll();

        /// <summary>
        /// Set the SUSTAINED roll target, in degrees. Positive rolls the camera's up vector to the left.
        /// <see cref="FirstPersonMotor"/> drives this from a wall run and writes 0 the moment the run
        /// ends; nothing else should touch it. Smoothed toward, never snapped.
        /// </summary>
        public void SetRollBias(float degrees) { rollBiasTarget = degrees; }

        /// <summary>
        /// A one-shot roll impulse that rises fast and settles, summed on top of the bias. Used for the
        /// snap off the wall when a run is jump-cancelled.
        /// </summary>
        public void AddRollKick(float degrees, float seconds)
        {
            if (seconds <= 0f) return;
            rollKickAmp = degrees;
            rollKickDuration = seconds;
            rollKickStart = Time.unscaledTime;
        }

        /// <summary>Envelope of the kick channel: an instant rise and a squared settle to zero.</summary>
        float KickRoll()
        {
            if (rollKickDuration <= 0f) return 0f;
            float t = (Time.unscaledTime - rollKickStart) / rollKickDuration;
            if (t >= 1f) { rollKickDuration = 0f; return 0f; }
            float f = 1f - t;
            return rollKickAmp * f * f;
        }

        /// <summary>
        /// Add a correction to the look angles, in degrees, and re-apply. The ONLY sanctioned way for
        /// anything else to move the camera — <see cref="LockOnController"/>'s assist calls this from
        /// LateUpdate, i.e. after this class has already applied the player's own mouse input for the
        /// frame, so the mouse is never scaled, filtered or overridden. Rotating the transform directly
        /// instead would be silently undone on the next Apply().
        /// </summary>
        public void NudgeAim(float deltaYaw, float deltaPitch)
        {
            yaw += deltaYaw;
            pitch = Mathf.Clamp(pitch + deltaPitch, -89f, 89f);
            Apply();
        }

        void Awake()
        {
            yaw = transform.eulerAngles.y;
            if (cam == null) { var c = GetComponentInChildren<Camera>(); if (c) cam = c.transform; }
        }

        void Update()
        {
            // WebGL / first click: cursor lock only works inside a user gesture.
            if (GameManager.IsPlaying && Cursor.lockState != CursorLockMode.Locked &&
                InputReader.I != null && InputReader.I.MouseClickedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (!GameManager.IsPlaying || InputReader.I == null) return;
            var input = InputReader.I;
            Vector2 d = input.LookDelta;
            if (input.LookIsMouse)
            {
                yaw += d.x * mouseSensitivity;
                pitch -= d.y * mouseSensitivity;
            }
            else
            {
                float dt = Time.unscaledDeltaTime;
                yaw += d.x * stickSensitivity * dt;
                pitch -= d.y * stickSensitivity * dt;
            }
            pitch = Mathf.Clamp(pitch, -89f, 89f);

            // UNSCALED, deliberately: hitstop must never freeze the wall-run lean any more than it
            // freezes the player. The same reason movement reads TimeScaleController.PlayerDelta.
            // 1 - exp(-k dt) rather than a raw lerp factor, so the lean settles at the same rate at
            // 30 fps and at 300, exactly as WallRunMath's own decay does.
            float rollBlend = 1f - Mathf.Exp(-rollBiasLerp * Time.unscaledDeltaTime);
            rollBias = Mathf.Lerp(rollBias, rollBiasTarget, rollBlend);
            Apply();
        }

        void Apply()
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (pivot != null) pivot.localRotation = Quaternion.Euler(pitch, 0f, Roll);
        }

        public void SetYaw(float y)
        {
            yaw = y;
            pitch = 0f;
            rollBias = rollBiasTarget = 0f;
            rollKickDuration = 0f;
            Apply();
        }
    }
}
