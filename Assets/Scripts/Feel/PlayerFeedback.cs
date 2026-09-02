using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Movement game-feel: footsteps, jump/land/dash/slide audio, landing camera dip, and the entry
    /// point for the dash and slide effect packages. FirstPersonMotor raised OnJumped/OnLanded/OnDashed
    /// from the start but nothing ever subscribed, so traversal was silent and weightless. This is that
    /// missing listener.
    ///
    /// <para><b>The dash and slide pass, and what was actually wrong with them.</b> Neither move was
    /// unfed. The dash shipped an 8 deg FOV kick (on the ASSET, not a code default), a chromatic pulse
    /// and a whoosh; the slide shipped a 6 deg kick, a 0.55 m eye drop and the same whoosh pitched down.
    /// Two things were wrong with that, and they are different problems:</para>
    /// <list type="number">
    ///   <item><b>The dash was non-specific.</b> A symmetric FOV widen says the lens changed; it does
    ///   not say which way you went or how far, and 0.16 s is far too short for the world to sell the
    ///   displacement on its own. It now also gets a DIRECTIONAL camera kick (the lens is left behind
    ///   and catches up) and a burst of camera-space speed lines. See <see cref="DashImpulse"/>.</item>
    ///   <item><b>The slide's middle was empty.</b> Every cue except the eye drop was an impulse, and a
    ///   slide lasts up to 0.90 s — the FOV kick was gone inside 0.4 s of it. There was also no ground
    ///   contact anywhere in the package, and <c>OnSlideEnded</c> was raised by the motor and subscribed
    ///   by NOBODY, so standing up had no cue at all. Fixed by <see cref="SlideFx"/>, whose every layer
    ///   tracks actual speed instead of firing once.</item>
    /// </list>
    ///
    /// <para>Both effect objects are created HERE at runtime rather than authored on the prefab, so
    /// nothing about this change needs a prefab rebuild or a new serialised reference.</para>
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
        [Tooltip("One-shot kick on ENTRY only. SlideFx separately HOLDS a speed-tracked FOV offset for " +
                 "the whole slide (GameFeelSettings.slideFovHold), and this is just the commit punch on " +
                 "top of it. Left at the value the Player prefab already ships - rule 9 cuts both ways, " +
                 "and moving this initialiser would change nothing on the existing prefab while making " +
                 "the code lie about what is running.")]
        public float slideFovKick = 6f;

        FirstPersonMotor motor;
        DashFx dashFx;
        SlideFx slideFx;
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
            motor.OnSlideEnded += OnSlideEnded;
            motor.OnWallJumped += OnWallJumped;
        }

        void OnDisable()
        {
            if (motor == null) return;
            motor.OnJumped -= OnJumped;
            motor.OnLanded -= OnLanded;
            motor.OnDashed -= OnDashed;
            motor.OnSlideStarted -= OnSlideStarted;
            motor.OnSlideEnded -= OnSlideEnded;
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

        /// <summary>
        /// A dash is a punctuation mark: everything here is front-loaded and over inside a quarter of a
        /// second. Four layers, deliberately different from each other — a FOV widen (the lens), a
        /// DIRECTIONAL kick (the body), speed lines (the world) and two audio voices (the air).
        ///
        /// <para>The kick is where the specificity comes from. The old package was symmetric, so it read
        /// as "something happened to the camera" rather than as "you went THAT way", and a dash that
        /// cannot be told from a flashbang is not a movement cue.</para>
        /// </summary>
        void OnDashed()
        {
            var feel = GameManager.I != null ? GameManager.I.feel : null;
            EnsureFx();

            // Two voices, the same trick ParryImpulse.DeflectLayers uses: the existing whoosh carries the
            // body, and Swing pitched up a fifth carries the transient. Spectral width, not volume — one
            // clip at one pitch cannot be both sharp and heavy. Rule 7: no new Sfx entry.
            AudioManager.Play(Sfx.Dash, 0.9f, 1f, 0.06f);
            AudioManager.Play(Sfx.Swing, 0.34f, 1.55f, 0.07f);

            // Dash direction in CAMERA space. PlayerLook yaws the body and only pitches the pivot, so
            // the camera's yaw IS the body's yaw and the motor's `transform.right * m.x +
            // transform.forward * m.y` is exactly (m.x, 0, m.y) here. Read from InputReader on the same
            // frame the motor read it, inside the event it raised — not re-derived a frame later, which
            // would be a frame of lag on the one cue that has to be instantaneous.
            Vector3 dirLocal = Vector3.forward;
            if (InputReader.I != null)
            {
                Vector2 m = InputReader.I.MoveAxis;
                Vector3 d = new Vector3(m.x, 0f, m.y);
                if (d.sqrMagnitude > 0.01f) dirLocal = d.normalized;
            }

            if (CameraFX.I != null)
            {
                CameraFX.I.FovKick(feel != null ? feel.dashFovKick : 8f);
                CameraFX.I.ChromaticPulse(feel != null ? feel.dashChromatic : 0.35f, 0.18f);
            }
            if (CameraShake.I != null)
            {
                var k = DashImpulse.FromDash(dirLocal,
                    feel != null ? feel.dashKickPitch : 0.9f,
                    feel != null ? feel.dashKickRoll : 1.4f,
                    feel != null ? feel.dashKickOffset : 0.06f);
                CameraShake.I.Kick(k.euler, k.offset,
                    feel != null ? feel.dashKickTime : 0.14f, DashImpulse.KickAttackFraction);
            }
            if (dashFx != null)
                dashFx.Fire(dirLocal,
                    feel != null ? feel.dashStreakSeconds : 0.22f,
                    feel != null ? feel.dashStreakAlpha : 0.85f);
        }

        /// <summary>The COMMIT. Reuses the dash whoosh, pitched down: a slide is the same gesture with
        /// weight on it. Sfx enum names are folder names and append-only, so a new one is a content
        /// change, not a feedback change - see hard rule 7.
        ///
        /// <para>The FOV kick here is only the entry punch; <see cref="SlideFx"/> separately HOLDS a
        /// speed-tracked FOV offset for the whole slide, which is the layer that was missing.</para></summary>
        void OnSlideStarted()
        {
            var feel = GameManager.I != null ? GameManager.I.feel : null;
            EnsureFx();

            AudioManager.Play(Sfx.Dash, 0.75f, 0.72f, 0.05f);
            AudioManager.Play(Sfx.Land, 0.45f, 0.8f, 0.06f);   // the body meeting the floor
            if (CameraFX.I != null) CameraFX.I.FovKick(slideFovKick);
            if (CameraShake.I != null)
            {
                // Nose dips and the head sinks as you commit. Frontal and symmetric — a slide entry has
                // no lateral force, and inventing one would read as a stumble.
                float p = feel != null ? feel.slideKickPitch : 1.2f;
                CameraShake.I.Kick(new Vector3(p, 0f, 0f), new Vector3(0f, -0.02f, 0f),
                    feel != null ? feel.slideKickTime : 0.13f, DashImpulse.KickAttackFraction);
                CameraShake.I.Add(0.05f, 0.10f);   // the scrape starting, as texture
            }
            if (slideFx != null) slideFx.Begin();
        }

        /// <summary>
        /// Standing up. THIS HANDLER IS NEW: <c>OnSlideEnded</c> existed on the motor from the start and
        /// nothing subscribed to it, so the end of a slide — the moment you spend the last of the speed
        /// and your eye rises 0.55 m — produced no cue whatsoever.
        ///
        /// <para>The FOV punch is NEGATIVE. The sustained hold has already decayed to zero with the
        /// speed (see <see cref="SlideImpulse.FovForSpeed"/>), so this is a small punch back IN past
        /// neutral: the world closing up as the speed goes.</para>
        /// </summary>
        void OnSlideEnded()
        {
            var feel = GameManager.I != null ? GameManager.I.feel : null;
            AudioManager.Play(Sfx.Footstep, 0.55f, 0.72f, 0.08f);
            if (CameraFX.I != null) CameraFX.I.FovKick(feel != null ? feel.slideEndFovPunch : -2.5f);
            if (CameraShake.I != null) CameraShake.I.Add(0.045f, 0.12f);
            if (slideFx != null) slideFx.End();
        }

        /// <summary>
        /// Build the two effect objects on first use. Not in Awake, because both want values off
        /// <c>GameManager.I.feel</c> (rule 9 — the asset is the shipped value, not the field
        /// initialiser) and the manager is not guaranteed to exist that early. Not on the prefab either:
        /// creating them here means the whole dash/slide package needs no prefab rebuild and no new
        /// serialised reference to go missing.
        /// </summary>
        void EnsureFx()
        {
            if (dashFx == null)
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null)
                {
                    var feel = GameManager.I != null ? GameManager.I.feel : null;
                    var go = new GameObject("DashFx");
                    go.transform.SetParent(transform, false);
                    dashFx = go.AddComponent<DashFx>();
                    dashFx.Build(cam.transform,
                        feel != null ? feel.dashStreakCount : 12,
                        feel != null ? feel.dashStreakBrightness : 0.9f);
                }
            }
            if (slideFx == null && motor != null)
            {
                var go = new GameObject("SlideFx");
                go.transform.SetParent(transform, false);
                slideFx = go.AddComponent<SlideFx>();
                slideFx.Build(motor);
            }
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

            // The slide's SUSTAINED layers. Driven from the motor's live state every frame rather than
            // from the start/end events alone: the events do the one-shots, but a respawn or a disabled
            // player can swallow an end event and a slide that never released the FOV hold or the camera
            // roll would leave the lens permanently wrong for the rest of the run.
            if (slideFx != null)
            {
                var feel = GameManager.I != null ? GameManager.I.feel : null;
                slideFx.Tick(
                    feel != null ? feel.slideFovHold : 8f,
                    feel != null ? feel.slideRollDegrees : 3.5f,
                    feel != null ? feel.slideSparkRate : 5f,
                    feel != null ? feel.slideDustRate : 34f,
                    feel != null ? feel.slideScrapeVolume : 0.22f);
            }
        }
    }
}
