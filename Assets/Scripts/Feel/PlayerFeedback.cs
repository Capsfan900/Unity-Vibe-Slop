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
    /// <para><b>The wall run pass.</b> Same story a third time: <c>OnWallRunStarted</c> /
    /// <c>OnWallRunEnded</c> were raised and unsubscribed. The lean in <c>PlayerLook</c> was never the
    /// gap; the catch, the feet and the LET-GO were, and the let-go is the one that matters — four of
    /// the six endings are the wall giving up, which is the cue the exit-grace jump is learned
    /// against. See <see cref="WallRunImpulse"/> / <see cref="WallRunFx"/>.</para>
    ///
    /// <para>All three effect objects are created HERE at runtime rather than authored on the prefab,
    /// so nothing about these changes needs a prefab rebuild or a new serialised reference.</para>
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
        [Tooltip("Fallback ramp for the eye when there is no GameFeelSettings to read a spring from. " +
                 "Shipped: the eye follows the slide on a SPRING (GameFeelSettings.slideCrouchHz / " +
                 "slideCrouchDamping) so it plops onto the floor and rises past neutral on stand-up; " +
                 "this ramp is only what a missing asset gets.")]
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
        WallRunFx wallRunFx;
        WaterFx waterFx;
        bool wasInWater;
        Transform pivot;
        Vector3 pivotBase;
        float dip, dipVel;
        float crouch, crouchVel;
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
            // Not gated on motor: a refused input still deserves a sound even if this component somehow
            // has no motor to read (defensive; should never happen on the Player prefab).
            GameEvents.StaminaRefused += OnStaminaRefused;
            if (motor == null) return;
            motor.OnJumped += OnJumped;
            motor.OnLanded += OnLanded;
            motor.OnDashed += OnDashed;
            motor.OnSlideStarted += OnSlideStarted;
            motor.OnSlideEnded += OnSlideEnded;
            motor.OnWallJumped += OnWallJumped;
            motor.OnWallRunStarted += OnWallRunStarted;
            motor.OnWallRunEnded += OnWallRunEnded;
            motor.OnPerfect += OnPerfect;
            motor.OnLaunched += OnLaunched;
        }

        void OnDisable()
        {
            GameEvents.StaminaRefused -= OnStaminaRefused;
            if (motor == null) return;
            motor.OnJumped -= OnJumped;
            motor.OnLanded -= OnLanded;
            motor.OnDashed -= OnDashed;
            motor.OnSlideStarted -= OnSlideStarted;
            motor.OnSlideEnded -= OnSlideEnded;
            motor.OnWallJumped -= OnWallJumped;
            motor.OnWallRunStarted -= OnWallRunStarted;
            motor.OnWallRunEnded -= OnWallRunEnded;
            motor.OnPerfect -= OnPerfect;
            motor.OnLaunched -= OnLaunched;
        }

        /// <summary>
        /// A balloon launch. The orb itself made the pop (sparks, ring, the two-voice Jump/Land); this
        /// is the LENS answering the body going up: an FOV widen between a jump's and a dash's, and a
        /// nose-up pitch kick with the lens left a hair behind, the same shape as the dash kick turned
        /// vertical. Frontal, no roll, no yaw — you are going UP, and nothing about that has a side.
        /// </summary>
        void OnLaunched()
        {
            var feel = GameManager.I != null ? GameManager.I.feel : null;
            EnsureFx();
            if (CameraFX.I != null) CameraFX.I.FovKick(feel != null ? feel.balloonFovKick : 5f);
            if (CameraShake.I != null)
            {
                float p = feel != null ? feel.balloonKickPitch : 1.5f;
                CameraShake.I.Kick(new Vector3(-p, 0f, 0f), new Vector3(0f, -0.03f, 0f), 0.14f, DashImpulse.KickAttackFraction);
            }
            dip -= jumpHopMeters * 1.5f;
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
                // A grapple-exit BURST is the dash package plus the biggest lens the kit has: the
                // burst is the fastest thing in the game (dashSpeed x pullBurstMultiplier), and the
                // FOV is what says "faster than a dash" when the speed lines already say "dash".
                bool burst = motor != null && motor.LastDashWasBurst;
                float extra = burst ? (feel != null ? feel.burstFovKick : 6f) : 0f;
                CameraFX.I.FovKick((feel != null ? feel.dashFovKick : 8f) + extra);
                CameraFX.I.ChromaticPulse((feel != null ? feel.dashChromatic : 0.35f) * (burst ? 1.6f : 1f), burst ? 0.26f : 0.18f);
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
            // Weight arriving back on the feet as the legs fold under you (PlayerBody plants them with
            // a knee bend on this same event). Quiet and a touch high: a plant, not a fall. Rule 7.
            AudioManager.Play(Sfx.Land, 0.30f, 1.05f, 0.05f);
            if (CameraFX.I != null) CameraFX.I.FovKick(feel != null ? feel.slideEndFovPunch : -2.5f);
            if (CameraShake.I != null) CameraShake.I.Add(0.045f, 0.12f);
            if (slideFx != null) slideFx.End();
        }

        /// <summary>
        /// A dash, wall run, wall jump or slide refused for lack of stamina (2026-09-06, audio pass).
        /// <see cref="FirstPersonMotor.TrySlide"/> and its siblings already name the refusal on
        /// <c>GameEvents.StaminaRefused</c> and <c>StaminaView</c> already flashes the bar red for it --
        /// but nothing played a sound, so the press read as dropped input rather than denied. A small
        /// pitch step per action keeps them tellable apart without adding a new mechanic.
        /// </summary>
        void OnStaminaRefused(StaminaAction what)
        {
            float pitch = what == StaminaAction.Slide ? 0.8f
                        : what == StaminaAction.WallJump ? 0.85f
                        : what == StaminaAction.WallRun ? 0.9f
                        : 1f;   // Dash
            AudioManager.Play(Sfx.Refuse, 0.7f, pitch, 0.03f);
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
            if (wallRunFx == null && motor != null)
            {
                var go = new GameObject("WallRunFx");
                go.transform.SetParent(transform, false);
                wallRunFx = go.AddComponent<WallRunFx>();
                wallRunFx.Build(motor);
            }
            if (waterFx == null && motor != null)
            {
                var go = new GameObject("WaterFx");
                go.transform.SetParent(transform, false);
                waterFx = go.AddComponent<WaterFx>();
                waterFx.Build(motor);
            }
        }

        /// <summary>
        /// The CATCH. <c>OnWallRunStarted</c> existed on the motor from the start and nothing subscribed;
        /// the lean in <c>PlayerLook</c> was the only thing that said a run had begun, and a lean is a
        /// state, not a contact. Two voices, both under the grounded footstep's 0.55: Footstep pitched
        /// up (a wall is struck shorter and harder than a floor) for the foot, Land quietly under it for
        /// the body meeting the face. Rule 7: no new Sfx entry.
        ///
        /// <para>The kick is translation only, TOWARD the wall — <see cref="WallRunImpulse.AttachKick"/>.
        /// The rotation is PlayerLook's lean and a second one here would fight it. The normal is read
        /// inside the event, while the motor still reports it, and handed to <see cref="WallRunFx"/> in
        /// camera space because the motor zeroes it before raising the end event.</para>
        /// </summary>
        void OnWallRunStarted()
        {
            var feel = GameManager.I != null ? GameManager.I.feel : null;
            EnsureFx();

            AudioManager.Play(Sfx.Footstep, 0.50f, 1.12f, 0.08f);
            AudioManager.Play(Sfx.Land, 0.28f, 1.15f, 0.06f);

            // Camera space: PlayerLook yaws the body and only pitches the pivot, so the body's inverse
            // is the camera's yaw frame — the same reasoning OnDashed uses for the move axis.
            Vector3 nLocal = transform.InverseTransformDirection(motor.WallRunNormal);

            if (CameraShake.I != null)
            {
                var k = WallRunImpulse.AttachKick(nLocal, feel != null ? feel.wallRunAttachOffset : 0.03f);
                CameraShake.I.Kick(k.euler, k.offset,
                    feel != null ? feel.wallRunAttachTime : 0.12f, WallRunImpulse.KickAttackFraction);
            }
            if (wallRunFx != null) wallRunFx.Begin(nLocal);
        }

        /// <summary>
        /// The LET-GO — or not. A run ends six ways and this is the one place they are told apart:
        /// <list type="bullet">
        ///   <item><b>Jumped</b>: nothing extra. The exit roll kick in PlayerLook and <see cref="OnWallJumped"/>
        ///   already own it, and stacking on the loudest cue blurs it.</item>
        ///   <item><b>Expired / Decayed / Exhausted</b>: a short DOWNWARD sag, the floor going out from
        ///   under you, with a slower attack than a blow (<see cref="WallRunImpulse.DropAttackFraction"/>).
        ///   Exhausted additionally gets a quieter, lower thud — stamina ran out ON the wall, and the
        ///   fix is a different line, so the player has to be able to hear that one.</item>
        ///   <item><b>LostWall</b>: a lateral drift AWAY from where the face was. It did not drop you,
        ///   it went away.</item>
        ///   <item><b>Landed / Cancelled</b>: nothing — <see cref="OnLanded"/> or the dash that
        ///   cancelled it owns the frame.</item>
        /// </list>
        /// <c>LastWallRunEnd</c> is written before the motor raises this event, so it is read here.
        /// </summary>
        void OnWallRunEnded()
        {
            var feel = GameManager.I != null ? GameManager.I.feel : null;
            WallRunEnd why = motor.LastWallRunEnd;
            Vector3 nLocal = wallRunFx != null ? wallRunFx.NormalLocal : Vector3.zero;

            ParryImpulse.Kick k;
            if (CameraShake.I != null && WallRunImpulse.EndKick(why, nLocal,
                    feel != null ? feel.wallRunDropPitch : 1.4f,
                    feel != null ? feel.wallRunDropOffset : 0.03f,
                    feel != null ? feel.wallRunLostDrift : 0.02f, out k))
            {
                CameraShake.I.Kick(k.euler, k.offset,
                    feel != null ? feel.wallRunDropTime : 0.16f, WallRunImpulse.DropAttackFraction);
            }
            if (WallRunImpulse.EndsWithThud(why)) AudioManager.Play(Sfx.Land, 0.30f, 0.70f, 0.06f);
            if (wallRunFx != null) wallRunFx.End();
        }

        /// <summary>
        /// A PERFECT. The move's own package has already played (this fires after OnDashed / OnJumped /
        /// OnWallJumped), so this is the "yes" on top of it: a chime built from two existing voices
        /// (rule 7 -- no new Sfx: the parry cue pitched up a fifth carries the ring, a high Swing the
        /// transient), a small extra widen, and a PERFECT stamp on the prompt line. Nothing brightens.
        /// A miss reaches no code here at all -- there is nothing to say about an ordinary move.
        /// </summary>
        void OnPerfect(PerfectKind kind, float refunded)
        {
            var feel = GameManager.I != null ? GameManager.I.feel : null;
            AudioManager.Play(Sfx.ParryCue, 0.55f, 1.5f, 0.02f);
            AudioManager.Play(Sfx.Swing, 0.28f, 1.9f, 0.04f);
            if (CameraFX.I != null) CameraFX.I.FovKick(feel != null ? feel.perfectFovKick : 3f);
            // A FLASH, not a standing prompt: PromptView draws it over whatever cue is standing and hands
            // that cue back when it expires. It used to be raised on the standing channel and cleared with an
            // empty string 0.6 s later, which wiped any live "GRAPPLE [DASH]" or "SURGE" for good, because
            // every standing writer is edge-triggered and never re-raises the same string (2026-09-06).
            GameEvents.RaisePromptFlash("PERFECT", feel != null ? feel.perfectPromptSeconds : 0.6f);
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
            //
            // A SPRING, not a ramp. The 6 m/s MoveTowards read as a crouch: the eye descended and
            // stopped. A body dropping onto a floor arrives — it goes a little past the height and
            // settles up — and on stand-up it rises past neutral before it settles down. Closed-form
            // (SlideImpulse.Spring), so the plop is identical at 20 and 240 fps. The overshoot is the
            // damping ratio's business (SlideImpulse.OvershootFraction), ~12% at the shipped 0.55.
            float wantCrouch = (motor != null && motor.IsSliding) ? slideCameraDrop : 0f;
            var feelCrouch = GameManager.I != null ? GameManager.I.feel : null;
            if (feelCrouch != null)
            {
                float nc, nv;
                SlideImpulse.Spring(crouch, crouchVel, wantCrouch,
                                    2f * Mathf.PI * Mathf.Max(0.1f, feelCrouch.slideCrouchHz),
                                    feelCrouch.slideCrouchDamping, udt, out nc, out nv);
                crouch = nc; crouchVel = nv;
                if (wantCrouch == 0f && Mathf.Abs(crouch) < 0.0005f && Mathf.Abs(crouchVel) < 0.01f)
                { crouch = 0f; crouchVel = 0f; }
            }
            else crouch = Mathf.MoveTowards(crouch, wantCrouch, slideCameraSpeed * udt);

            if (pivot != null) pivot.localPosition = pivotBase + Vector3.down * (dip + crouch);

            // The slide's SUSTAINED layers. Driven from the motor's live state every frame rather than
            // from the start/end events alone: the events do the one-shots, but a respawn or a disabled
            // player can swallow an end event and a slide that never released the FOV hold or the camera
            // roll would leave the lens permanently wrong for the rest of the run.
            var feelNow = GameManager.I != null ? GameManager.I.feel : null;
            if (slideFx != null)
            {
                slideFx.Tick(
                    feelNow != null ? feelNow.slideFovHold : 8f,
                    feelNow != null ? feelNow.slideRollDegrees : 3.5f,
                    feelNow != null ? feelNow.slideSparkRate : 5f,
                    feelNow != null ? feelNow.slideDustRate : 34f,
                    feelNow != null ? feelNow.slideScrapeVolume : 0.22f,
                    feelNow != null ? feelNow.slideRumble : 0.006f);
            }

            // WATER. Entry is an event the motor does not raise (it is a stay-refreshed touch), so it is
            // read as an edge here: one FOV kick and a soft Land on the first frame in, nothing held.
            // The spray and the hiss are textures that track speed, owned by WaterFx.
            if (motor != null)
            {
                bool inWater = motor.InWater;
                if (inWater && !wasInWater)
                {
                    EnsureFx();
                    if (CameraFX.I != null) CameraFX.I.FovKick(feelNow != null ? feelNow.waterEnterFovKick : 3f);
                    AudioManager.Play(Sfx.Land, 0.35f, 1.4f, 0.08f);
                }
                wasInWater = inWater;
            }
            if (waterFx != null)
                waterFx.Tick(feelNow != null ? feelNow.waterSprayRate : 26f,
                             feelNow != null ? feelNow.waterHissVolume : 0.14f);

            // The wall run's sustained layers. ORDER MATTERS: SlideFx.Tick above writes CameraFX.FovHold
            // every frame (0 when not sliding), and the hold has one writer at a time. WallRunFx ticks
            // after it and overrides only while a run is live, so the slide's zero never clobbers a live
            // wall-run hold and the wall run's release never lingers over a slide. The two moves are
            // mutually exclusive in the motor; this is the ordering that keeps them so on the lens.
            if (wallRunFx != null)
            {
                if (wallRunFx.Tick(
                        feelNow != null ? feelNow.wallRunFovHold : 3.5f,
                        feelNow != null ? feelNow.wallRunStepDistance : 1.6f,
                        feelNow != null ? feelNow.wallRunGritRate : 44f,
                        feelNow != null ? feelNow.wallRunStepSparks : 3))
                {
                    // A foot on the wall: the grounded footstep pitched up, quieter, falling as the run
                    // ages (WallRunImpulse.StepPitch), and a smaller bob than a ground stride.
                    AudioManager.Play(Sfx.Footstep,
                        feelNow != null ? feelNow.wallRunStepVolume : 0.40f,
                        WallRunImpulse.StepPitch(wallRunFx.ElapsedFraction), 0.10f);
                    dip += 0.008f;
                }
            }
        }
    }
}
