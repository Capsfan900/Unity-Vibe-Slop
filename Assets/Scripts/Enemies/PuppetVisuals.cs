using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Presentation for an enemy whose body is a RIGGED, CLIP-CARRYING model — currently only
    /// <c>Legendary_Marionette</c> (THE PALE MARIONETTE), built from <c>Assets/Enemies/PaleMarionette.fbx</c>.
    ///
    /// <para><b>Why a subclass and not a replacement.</b> <see cref="EnemyVisuals"/> documents itself as
    /// the thing to subclass when real models arrive, and <see cref="EnemyController"/> resolves
    /// presentation through <see cref="IEnemyPresentation"/>, so this is picked up with no brain change.
    /// Subclassing rather than reimplementing also keeps the SHARED readability language intact: the
    /// base's base-colour sink-and-snap, the cue flash, the alert marker, the posture-driven eye and the
    /// deathblow glyph are the vocabulary every other enemy speaks, and a player must not have to learn a
    /// second one for this body. Every override below calls <c>base</c> first and then ADDS the clip and
    /// the whirl on top.</para>
    ///
    /// <para><b>The clip bends to the data, never the reverse (this is the load-bearing rule).</b> The
    /// parry contract is that the cue fires <c>cueLead</c> (0.28 s) before impact and that no wind-up is
    /// under 0.45 s. Those live in <see cref="EnemyAttackData"/>. An attack clip is therefore never
    /// played at its authored rate: <see cref="PlayAttackClip"/> scales <c>Animator.speed</c> so the
    /// clip's OWN contact frame (<see cref="attackHitNormalized"/>, read out of the forge manifest at
    /// build time) lands exactly on the data's impact moment. If a clip is the wrong length the clip
    /// loses. The scale is clamped, and a clamp is LOGGED rather than swallowed — a clip that had to be
    /// clamped is a clip whose contact no longer lines up with the blow, which is a real art bug.</para>
    ///
    /// <para><b>The whirl is presentation and nothing else.</b> It writes one local yaw on
    /// <see cref="spinRoot"/> and touches no timing, no collider and no damage. That is the entire
    /// resolution of "spin really fast" versus the 0.45 s wind-up floor: the BODY turns at a CONSTANT
    /// 2087 deg/s — 5.8 revolutions a second — while the damaging passes arrive on a 0.69 s beat built
    /// from a wind-up sitting exactly ON the floor. Since 2087 x 0.69 is exactly four revolutions, the
    /// body is at the same yaw on every impact without its speed ever changing. See
    /// <see cref="BeginPass"/> for how the alignment is bought with the ARC rather than with the rate,
    /// and <see cref="spinDegPerSec"/> for why any easing at all was the wrong answer.</para>
    ///
    /// <para><b>Hitstop.</b> Everything here runs on scaled time — <c>Time.time</c>, <c>Time.deltaTime</c>
    /// and the Animator's default <c>Normal</c> update mode. So the puppet FREEZES with the rest of the
    /// world when <see cref="TimeScaleController"/> calls hitstop, which is the point of hitstop. Rule 1
    /// reserves <c>PlayerDelta</c> for things the PLAYER drives; an enemy that kept dancing through a
    /// freeze frame would break the impact read.</para>
    /// </summary>
    public class PuppetVisuals : EnemyVisuals
    {
        // ---------------------------------------------------------------- bindings

        [Header("Rigged body")]
        [Tooltip("The imported model's Animator. Its controller is built by PuppetAnimatorFactory.")]
        public Animator animator;

        [Tooltip("The transform the WHIRL is written to. Must be the model root under LungeRoot, so the " +
                 "base class's lunge/lean (which writes LungeRoot) and this yaw never fight over one channel.")]
        public Transform spinRoot;

        // ---------------------------------------------------------------- clips

        [Header("Clip names — must match Assets/Enemies/PaleMarionette.clips.json")]
        public string clipIdle = "IdleCombat";
        public string clipWalk = "Walk";
        public string clipRun = "Run";
        public string clipAttack = "AttackSwing";
        public string clipHeavy = "AttackOverhead";
        [Tooltip("The clip played for a SPIN PASS. Chosen by MEASURING every clip's arm span rather " +
                 "than by name: the whirl only reads as a whirl if the arms stay out through it, and " +
                 "AttackSwing tucks them to a 0.76 m span while Roar holds 2.0 m for its whole length. " +
                 "See docs/ENGINEERING-LOG.md.")]
        public string clipSpin = "Roar";
        public string clipHit = "Hit";
        public string clipStagger = "Stagger";
        public string clipDeath = "Death";
        public string clipRoar = "Roar";

        [Header("Clip timing — baked from the manifest at build time, not read at runtime")]
        [Tooltip("Where in the attack clip the art actually makes contact, 0..1 (the manifest's " +
                 "OnAttackHit). Playback speed is scaled so this frame lands on the data's impact.")]
        [Range(0.05f, 0.95f)] public float attackHitNormalized = 0.55f;
        [Tooltip("Authored length of the attack clip in seconds. Baked so the speed maths needs no " +
                 "AnimationClip reference at runtime.")]
        public float attackClipLength = 1.167f;
        [Tooltip("Same pair for the spin clip. The spin clip has no OnAttackHit of its own, so its " +
                 "anchor is the manifest's OnRoar — which is fine precisely because the arms are out " +
                 "for the whole clip, so no single frame is 'the contact' and the anchor only decides " +
                 "which part of the hold is on screen.")]
        [Range(0.05f, 0.95f)] public float spinHitNormalized = 0.4f;
        public float spinClipLength = 1.833f;
        [Tooltip("A clip may be sped up or slowed down to fit the data, but only this far. Beyond the " +
                 "clamp the contact frame no longer lines up with the blow, which is an ART bug — so it " +
                 "is logged rather than hidden.")]
        public float minClipSpeed = 0.4f;
        public float maxClipSpeed = 3.5f;

        [Tooltip("Crossfade into a clip, in seconds. Short: a deflect must read as an instant jar.")]
        public float clipBlend = 0.07f;

        // ---------------------------------------------------------------- the whirl

        [Header("The whirl")]
        [Tooltip("An attack whose name starts with this is a SPIN PASS and drives the whirl. Anything " +
                 "else (the tempo-break overhead, the far-band lash) plays square-on, which is exactly " +
                 "what makes those two read as a break FROM the rhythm.")]
        public string spinAttackPrefix = "Marionette_Spin";

        [Tooltip("THE SPIN RATE, degrees per second, and it is CONSTANT. The body holds this speed for " +
                 "the whole phrase — through each pass, through the strike, through the gap between " +
                 "passes. It does not wind up, ease, decelerate into the player or drift between " +
                 "beats.\n\n" +
                 "This replaced a curve that started fast and decelerated into each impact, on the " +
                 "theory that the slowdown could serve as the wind-up tell. It could not: what it " +
                 "actually produced was a PULSE — blur, slow, blur, slow — which reads as a stuttering " +
                 "animation rather than as a spinning body, and the fight is supposed to be a single " +
                 "unbroken menace you time against. With a constant rate there is no positional tell " +
                 "left, so the parry rides entirely on the cue flash and the audio at cueLead, which is " +
                 "exactly the precision the fight is meant to demand.\n\n" +
                 "2087 deg/s is 5.8 revolutions a second, and it is chosen against the beat rather than " +
                 "picked by feel: 2087 x 0.69 = 1440 = exactly FOUR revolutions per beat, so the body " +
                 "returns to the same yaw on every single impact without anything having to correct it.")]
        public float spinDegPerSec = 2087f;

        [Tooltip("How far the rate may be nudged, as a fraction, to land the body square-on at an " +
                 "impact whose interval is NOT a whole number of revolutions — the spin-out, which " +
                 "arrives 0.21 s late on purpose. Small: past about this the correction stops being " +
                 "invisible and the pulse comes back. If a correction cannot fit inside the band, the " +
                 "constant rate WINS and the body simply arrives at a different yaw.")]
        [Range(0f, 0.35f)] public float maxRateCorrection = 0.12f;

        [Tooltip("ALIAS GUARD, degrees of body yaw per RENDERED frame. Rotation only reads as rotation " +
                 "while the per-frame step stays under about half the silhouette's rotational symmetry " +
                 "period; a humanoid with its arms out is roughly 2-fold symmetric, so past ~90 deg a " +
                 "frame the spin stops looking like a spin and starts looking like random orientation. " +
                 "The CONSTANT rate is clamped against the MEASURED frame time, so on a machine that " +
                 "cannot show the spin it is slowed rather than allowed to strobe. At 60 fps and " +
                 "2087 deg/s the step is 35 deg and this never binds.")]
        public float maxDegPerFrame = 75f;

        [Tooltip("Amplitude of the squared-up wobble, in degrees. This is what it does when the whirl is " +
                 "OFF — during the tempo-break overhead, the far-band lash and every recovery. Small on " +
                 "purpose: the body coming to a stop and facing you square is the tell that a break from " +
                 "the rhythm is coming, and a body still turning does not say that.")]
        public float squaredWobbleDeg = 5f;
        public float squaredWobbleHz = 0.8f;

        [Tooltip("How fast the whirl unwinds back to square-on when the spin ends (deg/s^2).")]
        public float spinSettleAccel = 900f;

        // ---------------------------------------------------------------- state

        /// <summary>Degrees still to travel before the body is square-on to the player. 0 == aligned.</summary>
        float spinPhase;
        /// <summary>True while a pass is in flight and the phase is on the data clock.</summary>
        bool passInFlight;
        float passArc, passT0, passDur;
        /// <summary>
        /// The rate the pass in flight is actually turning at, deg/s. Normally exactly
        /// <see cref="spinDegPerSec"/>; nudged inside <see cref="maxRateCorrection"/> only when the
        /// interval to this impact is not a whole number of revolutions. Resolved once per pass, never
        /// per frame — a frame-time spike must not be able to bend the rate mid-pass.
        /// </summary>
        float passRate;
        /// <summary>Smoothed REAL frame time. Unscaled on purpose: hitstop must not be read as a stutter.</summary>
        float smoothedDt = 1f / 60f;
        /// <summary>Free rotation rate (deg/s) used between passes and during the follow-through.</summary>
        float freeSpin;
        /// <summary>Set while staggered / dead: the whirl stops dead, which is the punish read.</summary>
        bool spinHalted;
        /// <summary>
        /// The whirl is off and the body is unwinding to square-on. Set by everything that is NOT a spin
        /// pass; cleared only by <see cref="BeginPass"/>.
        ///
        /// <para>This was missing in the first cut and it cost the fight its best tell: the tempo-break
        /// overhead played square-on in the ANIMATION but the body kept drifting underneath it at the
        /// idle rate, so a 1.0 s wind-up dropped into a 0.76 s rhythm looked exactly like another pass.
        /// The break from the rhythm has to be visible in the BODY, not only in the clip.</para>
        /// </summary>
        bool squaring = true;

        Vector3 lastRootPos;
        float locoSpeed;
        /// <summary>Time until which a one-shot clip owns the Animator and locomotion may not override it.</summary>
        float clipHold;

        protected override void Awake()
        {
            base.Awake();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (spinRoot == null && animator != null) spinRoot = animator.transform;
            if (animator != null && animator.runtimeAnimatorController == null)
                Debug.LogError("[PuppetVisuals] " + name + " has an Animator with NO controller. Run " +
                               "VibeGame1/4b. Build Mini-Bosses — PuppetAnimatorFactory builds it from " +
                               "the split FBX clips.", this);
            if (spinRoot == null)
                Debug.LogError("[PuppetVisuals] " + name + " has no spinRoot — the whirl will not run.", this);
            lastRootPos = transform.root.position;
        }

        // ---------------------------------------------------------------- IEnemyPresentation

        public override void Telegraph(EnemyAttackData atk, float seconds)
        {
            // Base first: the shared colour sink and the alert marker are the language every enemy
            // speaks, and they must not become model-specific.
            base.Telegraph(atk, seconds);
            if (atk == null) return;

            float toImpact = Mathf.Max(0.05f, seconds + atk.impactDelay);
            PlayAttackClip(atk, toImpact);
            if (IsSpinPass(atk)) BeginPass(toImpact);
            else UnwindToSquare();
        }

        public override void Strike(float lunge, float seconds)
        {
            base.Strike(lunge, seconds);
            // The pass has landed. Hand the whirl back to free rotation so it carries THROUGH rather
            // than stopping on the blow; the next Telegraph re-anchors it (see BeginPass).
            passInFlight = false;
            // Straight back onto the constant rate. NOT a follow-through that decays to an idle drift —
            // that was the second half of the pulse, and it was the worse half: the body ran at ~735
            // deg/s through the pass and then fell to 55 between passes, so the spin visibly sagged in
            // every gap. Between passes it now turns at exactly the speed it turns at during one.
            if (!spinHalted) freeSpin = ResolveRate();
        }

        public override void Recoil()
        {
            base.Recoil();
            // Deflected. The clip is a jar, NOT a stop — the spin survives a deflect and only the
            // posture bar records it. Stopping here would make one deflect look like the win.
            PlayOneShot(clipHit, 1f);
        }

        public override void Settle(float seconds)
        {
            base.Settle(seconds);
            UnwindToSquare();
        }

        public override void ClearTelegraph()
        {
            base.ClearTelegraph();
            passInFlight = false;
        }

        public override void Slump(bool on)
        {
            base.Slump(on);
            spinHalted = on;
            if (on)
            {
                // The whirl dies instantly. This is the single most important frame in the fight: the
                // blur stops, the body folds, and the deathblow glyph the base class just raised is
                // suddenly readable because nothing is moving.
                passInFlight = false;
                squaring = true;
                freeSpin = 0f;
                spinPhase = 0f;
                PlayOneShot(clipStagger, 1f, hold: 10f);
            }
            else
            {
                clipHold = 0f;
                PlayLoop(clipIdle);
            }
        }

        public override void Roar()
        {
            base.Roar();
            PlayOneShot(clipRoar, 1f);
        }

        public override void Die()
        {
            base.Die();
            spinHalted = true;
            passInFlight = false;
            freeSpin = 0f;
            PlayOneShot(clipDeath, 1f, hold: 10f);
        }

        // ---------------------------------------------------------------- the whirl maths

        bool IsSpinPass(EnemyAttackData atk)
        {
            return atk != null && !string.IsNullOrEmpty(spinAttackPrefix)
                && atk.name != null && atk.name.StartsWith(spinAttackPrefix);
        }

        /// <summary>
        /// Anchor the spin to this pass's impact WITHOUT changing its speed.
        ///
        /// <para><b>This is what makes the cadence undriftable.</b> The phase is not integrated forward
        /// from the last pass — it is RE-DERIVED every beat from wherever the body actually is and the
        /// data's own time-to-impact. A dropped frame, a hitstop, a deflect that shortened the recovery:
        /// none of them can accumulate, because nothing is accumulated.</para>
        ///
        /// <para><b>The rate is constant; the ARC is what gets chosen.</b> The body has to be square-on
        /// at the impact instant, so the arc it travels must be a whole number of revolutions past its
        /// current yaw. Rather than bending the speed to cover a fixed arc — which is what produced the
        /// pulse — this picks the arc whose implied speed is CLOSEST to <see cref="spinDegPerSec"/> and
        /// then holds that speed flat for the whole pass. Because 2087 deg/s x the 0.69 s beat is
        /// exactly four revolutions, the chosen arc is normally exactly right and the correction is
        /// zero.</para>
        ///
        /// <para>When it cannot be zero — the spin-out arrives 0.21 s late by design — the nudge is
        /// capped at <see cref="maxRateCorrection"/>. <b>If the cap binds, the constant rate wins and
        /// the body arrives at whatever yaw it arrives at.</b> A visible speed change is a worse defect
        /// than a body that is 40 degrees off at the blow: the spin is the whole read.</para>
        /// </summary>
        void BeginPass(float secondsToImpact)
        {
            if (spinHalted) return;

            passDur = Mathf.Max(0.05f, secondsToImpact);
            float want = ResolveRate();

            // Arc must be congruent to the current yaw mod 360 to land square-on, so the candidates are
            // phi, phi+360, phi+720... Pick the one closest to the constant rate, and never fewer than
            // one whole turn: below that it reads as a twitch rather than as the body coming round.
            float phi = Mathf.Repeat(spinPhase, 360f);
            float turns = Mathf.Round((want * passDur - phi) / 360f);
            if (turns < 1f) turns = 1f;
            float arc = phi + 360f * turns;

            float implied = arc / passDur;
            float lo = want * (1f - maxRateCorrection), hi = want * (1f + maxRateCorrection);
            if (implied < lo || implied > hi)
            {
                // No whole-revolution arc fits inside the band. Hold the speed, drop the alignment.
                passRate = want;
                passArc = want * passDur;
            }
            else
            {
                passRate = implied;
                passArc = arc;
            }

            passT0 = Time.time;
            passInFlight = true;
            squaring = false;
            freeSpin = passRate;
        }

        void UnwindToSquare()
        {
            passInFlight = false;
            squaring = true;
        }

        /// <summary>
        /// <see cref="spinDegPerSec"/>, lowered only if the display cannot show it: the body may never
        /// step more than <see cref="maxDegPerFrame"/> of yaw per RENDERED frame.
        ///
        /// <para>Past roughly 90 deg a frame a 2-fold-symmetric silhouette (a humanoid with its arms
        /// out) stops reading as rotation and becomes apparent random orientation, so on a machine that
        /// cannot render the spin the honest thing is to slow it rather than let it strobe. At 60 fps
        /// and the shipped 2087 deg/s the step is 35 deg and this never binds.</para>
        ///
        /// <para>Pure and static so it can be unit tested. Degenerate inputs — a zero frame time on the
        /// first frame of a scene, a disabled guard — return the authored rate rather than a nonsense
        /// one.</para>
        /// </summary>
        public static float ResolveRate(float degPerSec, float frameSeconds, float maxDegPerFrame)
        {
            if (frameSeconds <= 0.0001f || maxDegPerFrame <= 0f) return Mathf.Max(0f, degPerSec);
            return Mathf.Min(Mathf.Max(0f, degPerSec), maxDegPerFrame / frameSeconds);
        }

        float ResolveRate()
        {
            return ResolveRate(spinDegPerSec, smoothedDt, maxDegPerFrame);
        }

        // ---------------------------------------------------------------- clip playback

        /// <summary>
        /// Play the attack clip so that its own contact frame lands on the data's impact. The clip is
        /// stretched or squeezed to fit; the data is never touched.
        /// </summary>
        void PlayAttackClip(EnemyAttackData atk, float secondsToImpact)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;

            bool spin = IsSpinPass(atk);
            string clip = spin ? clipSpin
                        : (atk.unblockable || atk.windup >= 0.9f) ? clipHeavy
                        : clipAttack;
            float contact = spin
                ? spinClipLength * Mathf.Clamp01(spinHitNormalized)
                : attackClipLength * Mathf.Clamp01(attackHitNormalized);
            float speed = contact / Mathf.Max(0.02f, secondsToImpact);

            if (speed < minClipSpeed || speed > maxClipSpeed)
            {
                Debug.LogWarning("[PuppetVisuals] '" + clip + "' had to be clamped to fit " +
                    atk.name + " (wanted x" + speed.ToString("F2") + ", allowed " + minClipSpeed +
                    ".." + maxClipSpeed + "). The clip's contact frame will NOT line up with the blow. " +
                    "Fix the ART or pick a different clip — do not retune the attack to suit it.", this);
                speed = Mathf.Clamp(speed, minClipSpeed, maxClipSpeed);
            }

            animator.speed = speed;
            animator.CrossFadeInFixedTime(clip, clipBlend, 0, 0f);
            // Own the Animator until a little past the strike, so locomotion cannot stomp the swing.
            clipHold = Time.time + secondsToImpact + 0.25f;
        }

        void PlayOneShot(string clip, float speed, float hold = 0f)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(clip)) return;
            animator.speed = speed;
            animator.CrossFadeInFixedTime(clip, clipBlend, 0, 0f);
            clipHold = Time.time + (hold > 0f ? hold : 0.35f);
        }

        void PlayLoop(string clip)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(clip)) return;
            animator.speed = 1f;
            animator.CrossFadeInFixedTime(clip, clipBlend * 2f, 0, 0f);
        }

        // ---------------------------------------------------------------- tick

        protected override void Update()
        {
            base.Update();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Unscaled, so a hitstop is not mistaken for a 50 ms frame and does not talk the alias guard
            // into flattening the next pass. Smoothed hard (about a 0.4 s window) because the guard is a
            // decision about the DISPLAY, and a display does not change speed for one frame.
            smoothedDt = Mathf.Lerp(smoothedDt, Time.unscaledDeltaTime, 1f - Mathf.Exp(-2.5f * dt));

            // ---- the whirl -------------------------------------------------------------------
            if (spinRoot != null)
            {
                if (spinHalted)
                {
                    spinPhase = Mathf.MoveTowards(spinPhase, 0f, spinSettleAccel * dt);
                }
                else if (passInFlight)
                {
                    // LINEAR. The body covers passArc at a flat passRate and reaches 0 exactly at the
                    // impact. There is no curve here any more and there must not be one: any easing,
                    // however gentle, is a speed change, and a speed change every 0.69 s IS the pulse.
                    float k = Mathf.Clamp01((Time.time - passT0) / passDur);
                    spinPhase = passArc * (1f - k);
                }
                else if (squaring)
                {
                    // Unwind FORWARD to the nearest alignment rather than reversing: a puppet that
                    // reversed its spin to face you would read as a second, different move.
                    float target = spinPhase > 180f ? 360f : 0f;
                    spinPhase = Mathf.MoveTowards(spinPhase, target, spinSettleAccel * dt);
                    if (spinPhase >= 359.99f) spinPhase = 0f;
                    if (spinPhase <= 0.01f)
                        spinPhase = Mathf.Sin(Time.time * squaredWobbleHz * 2f * Mathf.PI) * squaredWobbleDeg;
                }
                else
                {
                    // Between passes: the SAME constant rate, not a follow-through that decays to an
                    // idle drift. The gap between two passes is 0.2 s of a 0.69 s beat, so a body that
                    // sagged from 2087 to 55 deg/s in it spent nearly a third of every beat visibly
                    // slowing down and speeding back up. That was half the pulse.
                    freeSpin = ResolveRate();
                    spinPhase -= freeSpin * dt;
                    if (spinPhase < 0f) spinPhase += 360f;
                }
                spinRoot.localRotation = Quaternion.Euler(0f, -spinPhase, 0f);
            }

            // ---- locomotion clips -------------------------------------------------------------
            // Measured off the ROOT rather than asked of the locomotion component: this class is only
            // permitted to know about meshes and animation (EnemyVisuals' own contract), and a measured
            // speed works for any IEnemyLocomotion implementation including none.
            Vector3 rootPos = transform.root.position;
            float inst = (rootPos - lastRootPos).magnitude / dt;
            lastRootPos = rootPos;
            locoSpeed = Mathf.Lerp(locoSpeed, inst, 1f - Mathf.Exp(-8f * dt));

            if (Time.time >= clipHold && !spinHalted && animator != null &&
                animator.runtimeAnimatorController != null)
            {
                string want = locoSpeed > 2.6f ? clipRun : locoSpeed > 0.35f ? clipWalk : clipIdle;
                var st = animator.GetCurrentAnimatorStateInfo(0);
                if (!st.IsName(want)) PlayLoop(want);
                else animator.speed = 1f;
            }
        }
    }
}
