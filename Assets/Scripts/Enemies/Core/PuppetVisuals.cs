using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Presentation for an enemy whose body is a RIGGED, CLIP-CARRYING model — <c>Legendary_Marionette</c>
    /// (THE PALE MARIONETTE, <c>Assets/Enemies/PaleMarionette.fbx</c>), <c>Legendary_Revenant</c> and
    /// <c>Legendary_Halberdier</c> (THE ARGENT HALBERDIER, the first body whose attacks are GENERATED,
    /// per-character clips named on the attack data — see <see cref="ClipFor"/>).
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
    /// loses. A clip too SHORT to be slowed to the floor starts late instead (<see cref="ClipStartDelay"/>),
    /// so its contact still lands on the blow; a clip too LONG is clamped, and that clamp is LOGGED rather
    /// than swallowed — its contact no longer lines up with the blow, which is a real art bug.</para>
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
        [Tooltip("Played for an attack whose asset name ends in '_Stab'. The forge ships this clip on " +
                 "every model and NOTHING used to play it — see ClipFor.")]
        public string clipStab = "AttackStab";
        [Tooltip("Played for an attack whose asset name ends in '_Kick'. Checked BEFORE the unblockable " +
                 "branch, because a kick is typically the unblockable and would otherwise be swallowed " +
                 "by the heavy clip.")]
        public string clipKick = "AttackKick";
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
        [Tooltip("Contact anchor and authored length for the stab and kick clips, baked from the " +
                 "manifest at build time exactly like the pair above. Without their own lengths they " +
                 "would be scaled by the SWING's contact frame and land their blow at the wrong moment.")]
        [Range(0.05f, 0.95f)] public float stabHitNormalized = 0.55f;
        public float stabClipLength = 1f;
        [Range(0.05f, 0.95f)] public float kickHitNormalized = 0.55f;
        public float kickClipLength = 1f;
        [Header("Travelling clips — the mesh stays over its collider")]
        [Tooltip("An empty transform between SpinRoot and the model, written ONLY by CompensateTravel: " +
                 "every frame it is moved by minus the Hips bone's XZ drift from its rest position, so a " +
                 "generated clip that walks the pelvis 1-5 m forward (a thrust, a shoulder charge) leaves " +
                 "the body over the capsule that actually gets hit. The travel itself is data " +
                 "(EnemyAttackData.lungeDistance). Null on a model with no travelling clips.")]
        public Transform travelRoot;
        [Tooltip("The rig's Hips bone. Read, never written.")]
        public Transform hipsBone;
        [Tooltip("Where the Hips sit in the MODEL's local space in the bind pose, baked at build time. " +
                 "The compensation cancels the XZ difference between this and where the clip puts them.")]
        public Vector3 hipsRestLocal = new Vector3(0f, 0.93f, 0f);

        [Header("Per-attack clips — EnemyAttackData.clip, baked from the manifest at build time")]
        [Tooltip("Every attack clip this model ships that an EnemyAttackData.clip may name, with its OWN " +
                 "authored length and contact anchor (the manifest's OnAttackHit). Filled by MiniBossFactory; " +
                 "three parallel arrays because a dictionary neither serialises nor diffs. A generated, " +
                 "per-character clip (a halberd sweep, a shoulder charge) is content: the attack names it, " +
                 "and this table is what lets the clip still bend to the attack's clock.")]
        public string[] namedClips = new string[0];
        public float[] namedClipLengths = new float[0];
        public float[] namedClipHits = new float[0];

        [Tooltip("A clip may be sped up or slowed down to fit the data, but only this far. Beyond the " +
                 "clamp the contact frame no longer lines up with the blow, which is an ART bug — so it " +
                 "is logged rather than hidden.")]
        public float minClipSpeed = 0.4f;
        [Tooltip("Rate a clip plays at when its run-in is too short for the wind-up and it starts LATE. 0.7 keeps a " +
                 "3-7 frame anticipation from reading as slow motion (Fable spatial spec R3, 2026-09-14).")]
        public float lateStartSpeed = 0.7f;

        [Header("Locomotion (2026-09-13 fluidity pass)")]
        [Tooltip("Metres per second the Walk clip's stride covers at rate 1, measured on the imported clip by " +
                 "MiniBossFactory. 0 = not measured: the clip plays at its authored rate.")]
        public float walkStrideSpeed = 0f;
        [Tooltip("Metres per second the Run clip's stride covers at rate 1. 0 = authored rate.")]
        public float runStrideSpeed = 0f;
        [Tooltip("Footfall dust ring colour. Alpha 0 = no dust (the default for every older body).")]
        public Color footstepDust = new Color(0f, 0f, 0f, 0f);
        public float maxClipSpeed = 3.5f;

        [Tooltip("Crossfade into a clip, in seconds. Short: a deflect must read as an instant jar.")]
        public float clipBlend = 0.07f;

        [Header("Procedural layer (2026-09-14) — Unity sells the weight the simple clips cannot")]
        [Tooltip("An empty between SpinRoot and the model, written ONLY by the procedural layer below: anticipation, " +
                 "cue hitch, lunge lean, overshoot, deflect recoil, strafe weight shift. Null = no layer.")]
        public Transform poseRoot;
        [Tooltip("Amplitude multiplier on the layer's positional and pitch beats. 1 = the spec's default body.")]
        public float proceduralScale = 1f;
        [Tooltip("Cap on the forward lean into a lunge, degrees.")]
        public float leanCapDeg = 22f;

        [Header("Follow-through — the clip runs at its own rate once the blow has landed")]
        [Tooltip("Animator speed from the attack's IMPACT onward. The wind-up is scaled so the contact " +
                 "frame lands on the data's impact (x0.55-0.7 on most generated clips); keeping that " +
                 "scale through the follow-through made every swing slow-motion, and the loop then cut " +
                 "it to idle 0.25 s after the blow. 1 = the authored rate.")]
        public float recoverySpeed = 1f;
        [Tooltip("Seconds after the impact the attack clip keeps the Animator before locomotion may " +
                 "take it back. The next Telegraph crossfades over it regardless.")]
        public float followThroughSeconds = 0.45f;

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
        /// <summary>When the attack clip in flight reaches its contact frame; the speed drops to
        /// <see cref="recoverySpeed"/> there. MaxValue when nothing is in flight.</summary>
        float attackImpactAt = float.MaxValue;
        /// <summary>An attack clip whose run-in is shorter than the wind-up starts LATE (see
        /// <see cref="ClipStartDelay"/>): the clip and the scaled time it starts at. MaxValue = none.</summary>
        string pendingClip;
        float pendingClipAt = float.MaxValue;
        float pendingSpeed = 1f, pendingBlend = 0.07f;
        bool pendingFilled;

        // ---- procedural layer state (scaled time throughout: hitstop freezes it with the body) ----
        float antStart = -1f, antDur, antUntil;
        float cueHitchUntil;
        float poseImpactAt = -1f, leanStart, leanDeg;
        float overshootAt = -1f, recoilAt = -1f, recoilTau = 0.1f;
        float strafeRoll;
        Vector3 lastPoseRootPos;

        public const float LeanDegPerMetrePerSecond = 2.2f;

        /// <summary>Forward lean for a lunge covered at <paramref name="speed"/> m/s, capped. Pure.</summary>
        public static float LeanForLungeSpeed(float speed, float capDeg)
        {
            return Mathf.Clamp(speed * LeanDegPerMetrePerSecond, 0f, Mathf.Max(0f, capDeg));
        }

        /// <summary>Critically damped return from <paramref name="amplitude"/>: no overshoot, 0 by ~4 tau. Pure.</summary>
        public static float DampedReturn(float amplitude, float tau, float t)
        {
            if (t < 0f) return 0f;
            float k = t / Mathf.Max(0.001f, tau);
            return amplitude * Mathf.Exp(-k) * (1f + k);
        }

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
            BeginProceduralAttack(atk, seconds, toImpact);
            if (UseDefaultAttackClipPlayback(atk)) PlayAttackClip(atk, toImpact);
            if (IsSpinPass(atk)) BeginPass(toImpact);
            else UnwindToSquare();
        }

        /// <summary>
        /// Override only when a specialised body stages this attack's Animator clip itself. Telegraph's
        /// shared dimming/cue contract and spin bookkeeping still run; only ordinary clip playback is skipped.
        /// </summary>
        protected virtual bool UseDefaultAttackClipPlayback(EnemyAttackData atk) { return true; }

        public override void Strike(float lunge, float seconds)
        {
            // The brain already carries the travel and the clip carries the swing. Popping LungeRoot a second
            // lungeDistance on top was a 45 m/s mesh jump on the Halberdier's charge and a positional pulse on
            // every Marionette pass (Fable spatial spec R1).
            base.Strike(0f, seconds);
            // The pass has landed. Hand the whirl back to free rotation so it carries THROUGH rather
            // than stopping on the blow; the next Telegraph re-anchors it (see BeginPass).
            passInFlight = false;
            // Straight back onto the constant rate. NOT a follow-through that decays to an idle drift —
            // that was the second half of the pulse, and it was the worse half: the body ran at ~735
            // deg/s through the pass and then fell to 55 between passes, so the spin visibly sagged in
            // every gap. Between passes it now turns at exactly the speed it turns at during one.
            if (!spinHalted) freeSpin = ResolveRate();
        }

        public override void CueFlash(bool unblockable)
        {
            base.CueFlash(unblockable);
            // P2: the hitch is instant, never eased -- the cue is a snap, and the anticipation releases into it.
            antUntil = Time.time;
            cueHitchUntil = poseImpactAt > Time.time ? poseImpactAt : Time.time + 0.1f;
        }

        public override void Recoil()
        {
            base.Recoil();
            recoilAt = Time.time;
            antUntil = Time.time;
            cueHitchUntil = 0f;
            poseImpactAt = -1f;
            overshootAt = -1f;
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
            antUntil = Mathf.Min(antUntil, Time.time);
            cueHitchUntil = 0f;
            passInFlight = false;
            pendingClipAt = float.MaxValue;
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
        /// Which clip an attack plays, and where that clip's own contact frame sits.
        ///
        /// <para><b>This used to have exactly two attack slots, and it silently threw away half the
        /// animation the forge shipped.</b> The rule was <c>unblockable || windup >= 0.9 ? heavy :
        /// swing</c>, so on THE EMBER REVENANT the slash and the thrust both played <c>AttackSwing</c>
        /// and the overhead and the kick both played <c>AttackOverhead</c> — while
        /// <c>AttackStab</c> and <c>AttackKick</c> sat in the FBX, imported, split, listed in the
        /// animator, and unreachable. Nothing warned: every clip existed, every state was valid, and the
        /// wrong one simply played. It was found by measuring silhouettes, not by reading this code.</para>
        ///
        /// <para><b>Order matters, and the kick is why.</b> A kick is typically the moveset's
        /// unblockable, so an <c>unblockable</c> test placed first swallows it into the heavy clip —
        /// which is exactly what happened. Name matches are therefore resolved BEFORE the
        /// heuristics.</para>
        ///
        /// <para>Matching on the asset-name suffix follows the <see cref="spinAttackPrefix"/> precedent
        /// rather than adding a clip field to <see cref="EnemyAttackData"/>: the forge exports the same
        /// four canonical attack clips for every model, so the mapping is a property of the PIPELINE and
        /// belongs with the other clip bindings, not on 25+ content assets.</para>
        /// </summary>
        void ClipFor(EnemyAttackData atk, out string clip, out float contact)
        {
            float length;
            ClipFor(atk, out clip, out contact, out length);
        }

        void ClipFor(EnemyAttackData atk, out string clip, out float contact, out float length)
        {
            // An EXPLICIT clip on the attack wins over every heuristic below, including the spin prefix:
            // it is the one case where the data says outright which art this attack is. Generated
            // per-character clips have no canonical name the pipeline could map, so this is the only way
            // they are ever reached. Resolved against the baked table so the clip carries its own
            // length and contact anchor — the same reason the stab and the kick have their own pair.
            if (!string.IsNullOrEmpty(atk.clip))
            {
                int i = IndexOfNamedClip(atk.clip);
                if (i >= 0)
                {
                    clip = namedClips[i];
                    length = namedClipLengths[i];
                    contact = length * Mathf.Clamp01(namedClipHits[i]);
                    return;
                }
                if (warnedClips == null) warnedClips = new System.Collections.Generic.HashSet<string>();
                if (warnedClips.Add(atk.clip))
                    Debug.LogWarning("[PuppetVisuals] " + atk.name + " names clip '" + atk.clip + "' but this " +
                        "body's baked clip table has no such clip (rebuild with VibeGame1/4b, or check the " +
                        "name against the model's .clips.json). Falling back to the pipeline mapping.", this);
            }
            if (IsSpinPass(atk))
            {
                clip = clipSpin;
                length = spinClipLength;
                contact = spinClipLength * Mathf.Clamp01(spinHitNormalized);
                return;
            }
            string n = atk.name != null ? atk.name : "";
            if (!string.IsNullOrEmpty(clipStab) && n.EndsWith("_Stab"))
            {
                clip = clipStab;
                length = stabClipLength;
                contact = stabClipLength * Mathf.Clamp01(stabHitNormalized);
                return;
            }
            if (!string.IsNullOrEmpty(clipKick) && n.EndsWith("_Kick"))
            {
                clip = clipKick;
                length = kickClipLength;
                contact = kickClipLength * Mathf.Clamp01(kickHitNormalized);
                return;
            }
            if (atk.unblockable || atk.windup >= 0.9f)
            {
                clip = clipHeavy;
                length = attackClipLength;
                contact = attackClipLength * Mathf.Clamp01(attackHitNormalized);
                return;
            }
            clip = clipAttack;
            length = attackClipLength;
            contact = attackClipLength * Mathf.Clamp01(attackHitNormalized);
        }

        /// <summary>Index into <see cref="namedClips"/>, or -1. Linear: the table is a dozen entries.</summary>
        public int IndexOfNamedClip(string clipName)
        {
            if (namedClips == null || string.IsNullOrEmpty(clipName)) return -1;
            int n = Mathf.Min(namedClips.Length,
                              namedClipLengths != null ? namedClipLengths.Length : 0,
                              namedClipHits != null ? namedClipHits.Length : 0);
            for (int i = 0; i < n; i++)
                if (namedClips[i] == clipName) return i;
            return -1;
        }

        System.Collections.Generic.HashSet<string> warnedClips;

        /// <summary>
        /// Seconds to WAIT before starting an attack clip whose run-in (<paramref name="contact"/>, its
        /// contact frame at 1x) is too short for <paramref name="secondsToImpact"/> to be covered by slowing
        /// it to <paramref name="minSpeed"/>. 0 when slowing is enough.
        ///
        /// <para>The forge's ComboFinisher (the Judge, the Dancer, the Lancer: 36 frames, contact at 0.2 =
        /// 0.30 s of run-in) under a 0.71-0.86 s wind-up wanted x0.35-0.41. Clamping to the floor started
        /// the clip at once and landed its contact 0.1-0.15 s BEFORE the blow -- on the string's 1.6x
        /// posture hit. Starting late at the floor rate keeps the contact on the blow; the tell is not the
        /// clip's (the base colour sink and the cue are on the data clock), so nothing the player reads
        /// moves. Pure, for the tests.</para>
        /// </summary>
        public static float ClipStartDelay(float contact, float secondsToImpact, float minSpeed)
        {
            secondsToImpact = Mathf.Max(0.02f, secondsToImpact);
            if (contact / secondsToImpact >= minSpeed) return 0f;
            return Mathf.Max(0f, secondsToImpact - contact / Mathf.Max(0.01f, minSpeed));
        }

        /// <summary>
        /// Play the attack clip so that its own contact frame lands on the data's impact. The clip is
        /// stretched or squeezed to fit -- or, when it cannot be slowed enough, started late
        /// (<see cref="ClipStartDelay"/>); the data is never touched.
        /// </summary>
        void PlayAttackClip(EnemyAttackData atk, float secondsToImpact)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;

            string clip; float contact, length;
            ClipFor(atk, out clip, out contact, out length);
            float speed = contact / Mathf.Max(0.02f, secondsToImpact);
            float delay = ClipStartDelay(contact, secondsToImpact, lateStartSpeed);
            float blend = AttackEntryBlend(secondsToImpact, clipBlend);

            if (delay > 0f)
            {
                // Hold whatever the body is doing (the previous hit's follow-through, the idle) and start
                // the clip when its late-start-rate run-in exactly reaches the impact. See Update.
                QueueClip(clip, lateStartSpeed, Time.time + delay, blend);
            }
            else
            {
                pendingClipAt = float.MaxValue;
                if (speed > maxClipSpeed)
                {
                    Debug.LogWarning("[PuppetVisuals] '" + clip + "' had to be clamped to fit " +
                        atk.name + " (wanted x" + speed.ToString("F2") + ", allowed up to x" + maxClipSpeed +
                        "). The clip's contact frame will NOT line up with the blow. " +
                        "Fix the ART or pick a different clip — do not retune the attack to suit it.", this);
                    speed = maxClipSpeed;
                }
                animator.speed = speed;
                animator.CrossFadeInFixedTime(clip, blend, 0, 0f);
            }
            softHold = false;
            // Own the Animator through the follow-through, so locomotion cannot stomp the swing; the
            // scale above lasts only until the contact frame (see Update), then the clip runs at its
            // authored rate.
            attackImpactAt = Time.time + secondsToImpact;
            clipHold = attackImpactAt + FollowThrough(length, contact, followThroughSeconds);
        }

        /// <summary>Crossfade into an attack clip: longer for a longer wind-up so a chained phrase never snaps
        /// out of the previous clip's tail, never shorter than the jar blend, never over 0.16 s. Pure.</summary>
        public static float AttackEntryBlend(float secondsToImpact, float minBlend)
        {
            return Mathf.Clamp(0.25f * secondsToImpact, minBlend, Mathf.Max(minBlend, 0.16f));
        }

        /// <summary>How long the attack clip keeps the Animator after its contact: its own authored tail, at
        /// least the default and at most 0.6 s, so a finisher's flourish is not cut mid-motion. Pure.</summary>
        public static float FollowThrough(float clipLength, float contact, float fallback)
        {
            float tail = clipLength - contact;
            return tail > 0f ? Mathf.Clamp(tail, fallback, Mathf.Max(fallback, 0.6f)) : fallback;
        }

        /// <summary>Start a clip at a rate at a scaled time, holding (or idling) until then. Profiles call this
        /// AFTER ReserveAnimatorUntil, which clears any pending clip.</summary>
        protected void QueueClip(string clip, float speed, float at, float blend)
        {
            pendingClip = clip;
            pendingSpeed = speed;
            pendingBlend = blend;
            pendingClipAt = at;
            pendingFilled = false;
        }

        void PlayOneShot(string clip, float speed, float hold = 0f)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(clip)) return;
            animator.speed = speed;
            animator.CrossFadeInFixedTime(clip, clipBlend, 0, 0f);
            clipHold = Time.time + (hold > 0f ? hold : 0.35f);
            attackImpactAt = float.MaxValue;   // a one-shot owns the speed; no impact switch pending
            pendingClipAt = float.MaxValue;
            softHold = false;
        }

        /// <summary>
        /// Reserve the Animator for a derived presentation until an absolute scaled-time deadline.
        /// This is deliberately the whole protected surface: a specialised body may stage its own clip,
        /// but locomotion still cannot seize the Animator halfway through it and the base attack-speed
        /// handoff cannot overwrite the staged clip's rate.
        /// </summary>
        protected void ReserveAnimatorUntil(float until)
        {
            clipHold = until;
            attackImpactAt = float.MaxValue;
            pendingClipAt = float.MaxValue;
            softHold = false;
        }

        /// <summary>
        /// Like <see cref="ReserveAnimatorUntil"/>, but for a non-combat presentation pose (a hit flinch, a
        /// recovery guard) that must give way the moment the body actually travels. A held pose on a body the
        /// brain is moving is exactly the "glides around not animating" the user reported.
        /// </summary>
        protected void ReserveAnimatorSoftly(float until)
        {
            clipHold = until;
            attackImpactAt = float.MaxValue;
            pendingClipAt = float.MaxValue;
            softHold = true;
        }

        bool softHold;
        int locoState = -1;
        float lastLocoNormalized;

        /// <summary>Move an in-flight attack clip's speed handoff onto the brain's actual impact clock.</summary>
        protected void ReanchorAnimatorImpact(float until)
        {
            float tail = attackImpactAt < float.MaxValue ? Mathf.Max(0f, clipHold - attackImpactAt) : 0f;
            if (pendingClipAt < float.MaxValue && attackImpactAt < float.MaxValue) pendingClipAt += until - attackImpactAt;
            attackImpactAt = until;
            clipHold = until + tail;
        }

        void PlayLoop(string clip)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(clip)) return;
            animator.speed = 1f;
            animator.CrossFadeInFixedTime(clip, clipBlend * 2f, 0, 0f);
            pendingClipAt = float.MaxValue;
            locoState = clip == clipRun ? PuppetLocomotion.Run : clip == clipWalk ? PuppetLocomotion.Walk : PuppetLocomotion.Idle;
        }

        // ---------------------------------------------------------------- travelling clips

        /// <summary>
        /// Keep the mesh over the collider while a clip walks the Hips. Unity's own root-motion
        /// extraction is NOT used on these Generic rigs: with a root node set, Unity moves the Hips'
        /// whole transform onto the model root -- the leap's lift and the sweep's body turn included --
        /// and the bake flags keep none of it in the pose (measured in play mode, 2026-09-04). So the
        /// Hips travel stays in the pose and this cancels just its XZ, on a transform nothing else
        /// writes. The difference below is independent of <see cref="travelRoot"/>'s current offset
        /// (both points ride it), so this is a direct solve, not an iteration.
        /// </summary>
        public void CompensateTravel()
        {
            if (travelRoot == null || hipsBone == null || animator == null) return;
            Vector3 rest = animator.transform.TransformPoint(hipsRestLocal);
            Vector3 drift = hipsBone.position - rest;
            Transform parent = travelRoot.parent;
            Vector3 local = parent != null ? parent.InverseTransformVector(drift) : drift;
            travelRoot.localPosition = new Vector3(-local.x, 0f, -local.z);
        }

        void LateUpdate()
        {
            // After the Animator has posed the rig for this frame and before the camera reads it.
            CompensateTravel();
            AfterTravelCompensated();
        }

        /// <summary>Last presentation hook after Generic-rig XZ compensation; default bodies do nothing.</summary>
        protected virtual void AfterTravelCompensated() { }

        /// <summary>Arm the procedural beats for one attack. Everything is derived from the data clock.</summary>
        void BeginProceduralAttack(EnemyAttackData atk, float windupSeconds, float toImpact)
        {
            if (poseRoot == null) return;
            antStart = Time.time;
            antDur = Mathf.Min(0.18f, 0.30f * Mathf.Max(0.05f, windupSeconds));
            antUntil = float.MaxValue;
            cueHitchUntil = 0f;
            poseImpactAt = Time.time + toImpact;
            overshootAt = poseImpactAt;
            float window = EnemyController.LungeWindow(atk.lungeDistance, 0.28f);
            leanStart = poseImpactAt - window;
            leanDeg = atk.lungeDistance > 0.01f ? LeanForLungeSpeed(atk.lungeDistance / window, leanCapDeg) : 0f;
        }

        /// <summary>Sum the beats onto PoseRoot. Presentation only: no timing, collider or damage is touched.</summary>
        void UpdateProceduralLayer(float dt)
        {
            if (poseRoot == null) return;
            float now = Time.time;
            float s = proceduralScale;
            float z = 0f, y = 0f, pitch = 0f;

            // The whirl owns the body while it spins, and a slumped or dead body owns its own fall.
            bool quiet = spinHalted || passInFlight || !squaring;
            if (!quiet)
            {
                // P1 anticipation: settle back, chest up, held until the cue.
                if (antStart >= 0f && now < antUntil)
                {
                    float k = Mathf.Clamp01((now - antStart) / Mathf.Max(0.01f, antDur));
                    float e = 1f - (1f - k) * (1f - k);
                    z -= 0.06f * s * e; y -= 0.04f * s * e; pitch -= 5f * s * e;
                }
                // P2 cue hitch.
                if (now < cueHitchUntil) { z += 0.03f * s; pitch += 3f * s; }
                // P3 lunge lean: in over 0.08 s from the travel start, out over 0.12 s after impact.
                if (leanDeg > 0f && poseImpactAt > 0f && now >= leanStart)
                {
                    float into = Mathf.Clamp01((now - leanStart) / 0.08f);
                    float outOf = now > poseImpactAt ? 1f - Mathf.Clamp01((now - poseImpactAt) / 0.12f) : 1f;
                    pitch += leanDeg * into * outOf;
                }
                // P4 overshoot and settle through the blow.
                if (overshootAt > 0f && now >= overshootAt)
                {
                    float t = now - overshootAt;
                    z += DampedReturn(0.08f * s, 0.08f, t);
                    pitch += DampedReturn(4f * s, 0.08f, t);
                    if (t > 0.5f) overshootAt = -1f;
                }
            }
            // P7 deflect recoil: jarred back and chest-up, damped home.
            if (recoilAt >= 0f)
            {
                float t = now - recoilAt;
                float k = Mathf.Clamp01(t / 0.05f);
                z -= DampedReturn(0.18f * s, recoilTau, Mathf.Max(0f, t - 0.05f)) * k;
                pitch -= DampedReturn(8f * s, recoilTau, Mathf.Max(0f, t - 0.05f)) * k;
                if (t > 0.8f) recoilAt = -1f;
            }
            // P8 strafe weight shift: roll into the sideways motion of the root.
            Vector3 rootPos = transform.root.position;
            float lateral = dt > 0f ? Vector3.Dot(rootPos - lastPoseRootPos, transform.root.right) / dt : 0f;
            lastPoseRootPos = rootPos;
            float wantRoll = quiet ? 0f : Mathf.Clamp(-lateral * 2.5f, -5f, 5f);
            strafeRoll = Mathf.Lerp(strafeRoll, wantRoll, 1f - Mathf.Exp(-6f * dt));

            poseRoot.localPosition = new Vector3(0f, y, z);
            poseRoot.localRotation = Quaternion.Euler(pitch, 0f, strafeRoll);
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

            UpdateProceduralLayer(dt);

            // ---- locomotion clips -------------------------------------------------------------
            // Measured off the ROOT rather than asked of the locomotion component: this class is only
            // permitted to know about meshes and animation (EnemyVisuals' own contract), and a measured
            // speed works for any IEnemyLocomotion implementation including none.
            Vector3 rootPos = transform.root.position;
            float inst = (rootPos - lastRootPos).magnitude / dt;
            lastRootPos = rootPos;
            locoSpeed = Mathf.Lerp(locoSpeed, inst, 1f - Mathf.Exp(-8f * dt));

            // A late-started attack clip: its run-in now reaches the impact exactly.
            if (Time.time >= pendingClipAt && animator != null && animator.runtimeAnimatorController != null)
            {
                pendingClipAt = float.MaxValue;
                animator.speed = pendingSpeed;
                animator.CrossFadeInFixedTime(pendingClip, pendingBlend, 0, 0f);
            }
            else if (pendingClipAt < float.MaxValue && !pendingFilled && animator != null &&
                     animator.runtimeAnimatorController != null && !animator.IsInTransition(0))
            {
                // Waiting on a late start while the previous one-shot sits clamped on its last frame reads as
                // a hitch: breathe on the idle instead (spec R3b). Once per pending clip.
                var cur = animator.GetCurrentAnimatorStateInfo(0);
                if (!cur.loop && cur.normalizedTime >= 1f)
                {
                    pendingFilled = true;
                    animator.speed = 1f;
                    animator.CrossFadeInFixedTime(clipIdle, clipBlend * 2.5f, 0, 0f);
                }
            }

            // The blow has landed: hand the clip back its own rate for the follow-through.
            if (Time.time >= attackImpactAt && animator != null)
            {
                attackImpactAt = float.MaxValue;
                animator.speed = Mathf.Max(0.05f, recoverySpeed);
            }

            // A soft (presentation-only) hold gives way the moment the brain moves the body.
            if (softHold && Time.time < clipHold && locoSpeed > 0.8f)
            {
                clipHold = 0f;
                softHold = false;
            }

            if (Time.time >= clipHold && !spinHalted && animator != null &&
                animator.runtimeAnimatorController != null)
            {
                softHold = false;
                int current = locoState < 0 ? PuppetLocomotion.Idle : locoState;
                int next = PuppetLocomotion.Choose(locoSpeed, current);
                string want = next == PuppetLocomotion.Run ? clipRun : next == PuppetLocomotion.Walk ? clipWalk : clipIdle;
                var st = animator.GetCurrentAnimatorStateInfo(0);
                // "Already playing" includes a crossfade already heading there. Checking only the CURRENT
                // state re-issued the crossfade every frame of the blend with a start time of 0, which pinned
                // Walk/Run on its first frame while the body slid along: the frozen glide (2026-09-13).
                bool playing = st.IsName(want) ||
                               (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(want));
                bool moving = next != PuppetLocomotion.Idle;
                float stride = next == PuppetLocomotion.Run ? runStrideSpeed : walkStrideSpeed;
                float rate = moving ? PuppetLocomotion.Rate(locoSpeed, stride) : 1f;

                if (!playing)
                {
                    bool wasMoving = st.IsName(clipWalk) || st.IsName(clipRun);
                    if (moving && wasMoving)
                    {
                        // Walk <-> Run keeps the stride phase so the feet do not restart mid-step.
                        float phase = Mathf.Repeat(st.normalizedTime, 1f);
                        animator.CrossFade(want, 0.2f, 0, phase);
                    }
                    else
                    {
                        // Starts and stops blend longer than a gait change: the body settles, it does not snap.
                        animator.CrossFadeInFixedTime(want, clipBlend * 2.5f, 0, 0f);
                    }
                    locoState = next;
                    lastLocoNormalized = 0f;
                }
                animator.speed = rate;

                if (moving && footstepDust.a > 0.001f && playing && st.IsName(want))
                {
                    float n = st.normalizedTime;
                    if (PuppetLocomotion.CrossedFootfall(lastLocoNormalized, n))
                        SlashFx.Ring(transform.root.position + Vector3.up * 0.05f, Vector3.up, footstepDust,
                                     next == PuppetLocomotion.Run ? 0.75f : 0.5f, 0.28f);
                    lastLocoNormalized = n;
                }
            }
        }
    }
}
