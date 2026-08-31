using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Movement game-feel: footsteps, jump/land/dash audio, landing camera dip and dash FOV kick.
    /// FirstPersonMotor raised OnJumped/OnLanded/OnDashed from the start but nothing ever subscribed,
    /// so traversal was silent and weightless. This is that missing listener.
    /// </summary>
    public class PlayerFeedback : MonoBehaviour
    {
        [Header("Footsteps")]
        public float stepDistance = 2.4f;
        public float minStepSpeed = 1.5f;

        [Header("Landing")]
        [Tooltip("Fall speed that produces a full-strength dip.")]
        public float hardLandingSpeed = 22f;
        public float maxDipMeters = 0.22f;
        public float dipRecoverySpeed = 9f;

        [Header("Jump / dash")]
        public float jumpFovKick = 2.5f;
        public float jumpHopMeters = 0.05f;

        [Header("Slide")]
        [Tooltip("How far the eye drops while sliding. Not the full collider drop - a camera on the " +
                 "floor reads as a bug, and you still have to see the thing you are sliding under.")]
        public float slideCameraDrop = 0.55f;
        [Tooltip("How fast the eye follows the slide, in metres per second. Fast enough to feel like a " +
                 "drop, slow enough not to snap.")]
        public float slideCameraSpeed = 6f;
        public float slideFovKick = 6f;

        FirstPersonMotor motor;
        Transform pivot;
        Vector3 pivotBase;
        float dip, dipVel;
        float crouch;
        float stepAccum;
        Vector3 lastPos;

        void Awake()
        {
            motor = GetComponent<FirstPersonMotor>();
            var look = GetComponent<PlayerLook>();
            pivot = look != null ? look.pivot : null;
            if (pivot != null) pivotBase = pivot.localPosition;
            lastPos = transform.position;
        }

        void OnEnable()
        {
            if (motor == null) return;
            motor.OnJumped += OnJumped;
            motor.OnLanded += OnLanded;
            motor.OnDashed += OnDashed;
            motor.OnSlideStarted += OnSlideStarted;
            motor.OnWallJumped += OnWallJumped;
        }

        void OnDisable()
        {
            if (motor == null) return;
            motor.OnJumped -= OnJumped;
            motor.OnLanded -= OnLanded;
            motor.OnDashed -= OnDashed;
            motor.OnSlideStarted -= OnSlideStarted;
            motor.OnWallJumped -= OnWallJumped;
        }

        void OnJumped()
        {
            AudioManager.Play(Sfx.Jump, 0.8f, 1.05f, 0.08f);
            if (CameraFX.I != null) CameraFX.I.FovKick(jumpFovKick);
            dip -= jumpHopMeters;
        }

        void OnLanded()
        {
            float k = Mathf.Clamp01(motor.LastLandingSpeed / hardLandingSpeed);
            if (k < 0.08f) return;   // stepping off a kerb should not thud

            dip += maxDipMeters * k;
            AudioManager.Play(Sfx.Land, Mathf.Lerp(0.35f, 1f, k), Mathf.Lerp(1.15f, 0.85f, k), 0.06f);
            if (k > 0.6f && CameraShake.I != null) CameraShake.I.Small();
            if (CameraFX.I != null) CameraFX.I.FovKick(-3f * k);
            stepAccum = 0f;
        }

        void OnDashed()
        {
            AudioManager.Play(Sfx.Dash, 0.9f, 1f, 0.06f);
            var feel = GameManager.I != null ? GameManager.I.feel : null;
            if (CameraFX.I != null)
            {
                CameraFX.I.FovKick(feel != null ? feel.dashFovKick : 8f);
                CameraFX.I.ChromaticPulse(0.25f, 0.2f);
            }
        }

        /// <summary>Reuses the dash whoosh, pitched down: a slide is the same gesture with weight on it.
        /// Sfx enum names are folder names and append-only, so a new one is a content change, not a
        /// feedback change - see hard rule 7.</summary>
        void OnSlideStarted()
        {
            AudioManager.Play(Sfx.Dash, 0.75f, 0.72f, 0.05f);
            if (CameraFX.I != null) CameraFX.I.FovKick(slideFovKick);
        }

        /// <summary>The jump sound, pitched up and harder: it must read as a DIFFERENT jump, or a player
        /// cannot tell a wall jump fired from a jump that silently did not.</summary>
        void OnWallJumped()
        {
            AudioManager.Play(Sfx.Jump, 0.95f, 1.28f, 0.06f);
            if (CameraFX.I != null) CameraFX.I.FovKick(jumpFovKick * 1.6f);
            if (CameraShake.I != null) CameraShake.I.Small();
            dip -= jumpHopMeters * 1.5f;
        }

        void Update()
        {
            float udt = Time.unscaledDeltaTime;

            // footsteps by distance travelled, so they stay in step with actual speed
            if (motor != null && motor.IsGrounded && GameManager.IsPlaying)
            {
                Vector3 delta = transform.position - lastPos;
                delta.y = 0f;
                if (motor.HorizontalSpeed >= minStepSpeed)
                {
                    stepAccum += delta.magnitude;
                    if (stepAccum >= stepDistance)
                    {
                        stepAccum -= stepDistance;
                        AudioManager.Play(Sfx.Footstep, 0.55f, 1f, 0.12f);
                        dip += 0.012f;
                    }
                }
                else stepAccum = Mathf.Max(0f, stepAccum - udt);
            }
            lastPos = transform.position;

            // spring the camera dip back to neutral
            dip = Mathf.SmoothDamp(dip, 0f, ref dipVel, 1f / dipRecoverySpeed, Mathf.Infinity, udt);

            // The eye follows the slide separately from the landing dip, so the two never fight: dip is
            // a spring back to zero, crouch is a held offset for as long as the slide lasts.
            float wantCrouch = (motor != null && motor.IsSliding) ? slideCameraDrop : 0f;
            crouch = Mathf.MoveTowards(crouch, wantCrouch, slideCameraSpeed * udt);

            if (pivot != null) pivot.localPosition = pivotBase + Vector3.down * (dip + crouch);
        }
    }
}
