using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Additive presentation profile for the Seraph Lancer, built the way <see cref="CinderJudgeVisuals"/>
    /// and <see cref="OrbitDancerVisuals"/> were built: combat stays entirely on EnemyController's data
    /// clock, and this class only stages clips, owns two transforms (<see cref="hoverRoot"/>: the
    /// verdict's lift and the tracking yaw; <see cref="wingRoot"/>: the light-wings), writes the
    /// verdigris aura, and keeps the picture on the launcher's own numbers.
    ///
    /// <para><b>Sky Verdict, on the brain's clock.</b> Wind-up = the RISE: Jump plays from its first
    /// frame at the rate that lands its apex on the wind-up's end, HoverRoot lifts the body to
    /// <see cref="hoverHeight"/> from the clip's take-off frame, and the light-wings unfold as it leaves
    /// the floor -- the tell is the body leaving the ground. Strike's impactDelay = HoverHold (arms out,
    /// wings wide) and then the first JavelinThrow, staged so its baked release frame lands on the
    /// brain's impact; every later throw is staged at the launcher's cadence off that same impact, so
    /// the release frame and <c>SeraphLancerJavelins</c>' launch agree by construction. The strike's
    /// last <see cref="hoverDescendSeconds"/> = the descent (Jump resumes into its landing frame, the
    /// wings fold); the landing is the punish. Every deadline is re-anchored to
    /// <see cref="EnemyController.NextImpactTime"/> in <see cref="Strike"/>, as V18's are.</para>
    ///
    /// <para><b>The Heavy is a DIVE.</b> Its wind-up stages Jump from its take-off frame into
    /// HeavyAttack's contact, with a short hop on HoverRoot peaking at the switch: the body rises and
    /// comes down with the blow. The lunge (0.75 m, the clip's own travel) is the base class's on
    /// LungeRoot; the hop is pure presentation and lands at zero on the impact.</para>
    ///
    /// <para><b>One writer per channel.</b> LungeRoot stays the base class's; SpinRoot stays
    /// PuppetVisuals' whirl; TravelRoot stays CompensateTravel's. HoverRoot (inserted between LungeRoot
    /// and SpinRoot by MiniBossFactory) and WingRoot (a child of HoverRoot) are written only here. The
    /// body's emission floor is asked for through <see cref="EnemyVisuals.SetAura"/> by this class
    /// alone -- there is no EmberAura on this prefab.</para>
    /// </summary>
    public sealed class SeraphLancerVisuals : PuppetVisuals
    {
        [Header("Seraph Lancer presentation profile")]
        public string skyVerdictAttack = "SeraphLancer_SkyVerdict";
        public string heavyAttack = "SeraphLancer_Heavy";
        public string shoulderChargeAttack = "SeraphLancer_ShoulderCharge";
        public string shoulderChargeClip = "ShoulderCharge";
        public string heavyClip = "HeavyAttack";
        public string jumpClip = "Jump";
        public string hoverClip = "HoverHold";
        public string throwClip = "JavelinThrow";
        [Tooltip("Inserted between LungeRoot and SpinRoot by MiniBossFactory; the ONLY transform the lift and the tracking yaw write.")]
        public Transform hoverRoot;
        [Tooltip("A child of HoverRoot built by MiniBossFactory; the light-wings' feathers are built under it at Setup and written only here.")]
        public Transform wingRoot;
        public float entranceProbeDelay = 0.12f;
        public float entranceHoldSeconds = 0.95f;
        [Tooltip("Hit flinch hold. 0.35 is the fluidity pass's cap: a held pose on a moving body glides.")]
        public float hitHoldSeconds = 0.35f;

        [Header("Sky Verdict staging")]
        [Tooltip("Metres HoverRoot lifts the body for the verdict.")]
        public float hoverHeight = 3.0f;
        [Tooltip("The strike's last seconds: Jump lands, the body descends, the wings fold.")]
        public float hoverDescendSeconds = 0.45f;
        [Tooltip("Seconds the body takes to drop when a verdict is interrupted mid-air.")]
        public float hoverFallSeconds = 0.22f;
        [Tooltip("Degrees per second HoverRoot turns the hovering body to keep the throwing arm on the player.")]
        public float hoverTrackDegPerSec = 120f;
        [Tooltip("Jump clip fractions: OnJumpTakeoff / the airborne apex / OnJumpLand from the manifest.")]
        public float jumpTakeoffNormalized = 0.28f;
        public float jumpApexNormalized = 0.56f;
        public float jumpLandNormalized = 0.85f;
        public float landingHoldSeconds = 0.35f;
        [Tooltip("Seconds after a release the throw clip plays on before HoverHold returns.")]
        public float throwFollowThroughSeconds = 0.25f;
        public float chargeSparkInterval = 0.12f;
        public float landingRingSeconds = 0.34f;
        public int landingSparkCount = 12;
        public float landingSparkSpeed = 6f;
        public float landingSparkSpread = 110f;

        [Header("Heavy dive")]
        [Tooltip("Metres the hop on HoverRoot peaks at the Jump-to-HeavyAttack switch, back to 0 on the impact.")]
        public float diveHopHeight = 0.8f;

        [Header("Light-wings (this class is wingRoot's only writer)")]
        public int feathersPerWing = 3;
        public float wingLength = 1.6f;
        public float wingChord = 0.42f;
        [Tooltip("Degrees between neighbouring feathers, fanning upward.")]
        public float wingSweepDeg = 28f;
        [Tooltip("Elevation of the lowest feather above the horizontal, degrees.")]
        public float wingRootPitchDeg = 18f;
        [Tooltip("Degrees the feathers rake BACK behind the body.")]
        public float wingRakeDeg = 25f;
        public float wingUnfoldSeconds = 0.35f;
        public float wingFoldSeconds = 0.25f;
        [Range(0f, 1f)] public float wingAlpha = 0.55f;
        public float wingFlickerHz = 9f;
        [Range(0f, 0.5f)] public float wingFlickerAmount = 0.12f;
        [ColorUsage(true, true)] public Color wingHue = new Color(0.72f, 1.0f, 0.90f, 1f);
        [Tooltip("Seconds the wings flick open on the wake hop: the tell shown once, harmlessly.")]
        public float entranceWingSeconds = 0.5f;

        // Verdict hand charge (Signature Sigil): root-local RightHand offset, like the Dancer's Crackle.
        static readonly Vector3 verdictHandOffset = new Vector3(0.35f, 1.35f, 0.30f);
        const float verdictChargeInterval = 0.06f, verdictChargeMinSize = 0.08f, verdictChargeMaxSize = 0.30f;

        [Header("Verdigris seams (this class is the aura's only writer)")]
        [ColorUsage(true, true)] public Color verdigrisHot = new Color(0.25f, 0.75f, 0.62f, 1f) * 1.3f;
        [Range(0f, 1f)] public float glowAtRest = 0.14f;
        [Range(0f, 1f)] public float glowAtBreak = 0.42f;
        [Range(0f, 1f)] public float hoverGlow = 0.50f;
        public float pulseSpeed = 1.9f;
        [Range(0f, 0.5f)] public float pulseAmount = 0.12f;

        public enum VerdictPhase { None, Rising, Hovering, Descending, Falling }

        /// <summary>Where the verdict performance is, for tests and the harness.</summary>
        public VerdictPhase Phase { get; private set; }
        /// <summary>Metres the body is currently lifted by HoverRoot (verdict or dive).</summary>
        public float Lift { get; private set; }
        /// <summary>HoverRoot's current tracking yaw, degrees, relative to the brain's facing.</summary>
        public float TrackYaw { get; private set; }
        /// <summary>0 folded .. 1 fully unfolded.</summary>
        public float WingSpreadNow { get; private set; }
        /// <summary>Throw clips staged in the current verdict.</summary>
        public int ThrowClipsStaged { get; private set; }

        float shoulderImpactAt, shoulderClipAt;
        bool shoulderClipPending;

        float diveStartAt, diveSwitchAt, diveImpactAt;
        bool divePending, diveActive;

        float riseStartAt, apexAt, verdictImpactAt, descentAt, verdictEndAt, fallStartAt, fallFromLift;
        float verdictStrikeDuration;
        float nextThrowClipAt = float.MaxValue, hoverReturnAt = float.MaxValue;
        float wingsOpenAt = float.MaxValue, wingsCloseAt = float.MaxValue, entranceWingsUntil = -1f;
        float nextChargeSparkAt;
        bool landingPending, hoverClipPending;

        // Verdict hand charge (Signature Sigil): a growing flare on the throwing hand over the wind-up,
        // the RightHand answer to the Judge's crackle and the Dancer's Crackle.
        float verdictChargeStartAt, verdictChargeEndAt, nextVerdictChargeAt = float.MaxValue;

        EnemyController controller;
        SeraphLancerJavelins javelins;
        Transform playerT;
        float entranceProbeAt;
        bool entrancePending;
        bool observedAggroLock;
        float hoverGroundY;
        float lastPostureRatio;
        float auraPhase;
        Color accentHue = Color.white;
        string activeAttack;

        Material wingMat;
        Renderer[] feathers = new Renderer[0];
        Vector3[] featherBaseScale = new Vector3[0];
        MaterialPropertyBlock wingMpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public const float LandingSafetySeconds = 1f / 60f;

        // ---------------------------------------------------------------- pure arithmetic (tests)

        /// <summary>
        /// The body's lift at <paramref name="now"/>: a smooth rise from the take-off to the apex, a hold
        /// through the hover, and a smooth descent over the strike's last <c>descentAt..endAt</c>.
        /// </summary>
        public static float HoverLift(float now, float riseStart, float apexAt, float descentAt, float endAt, float height)
        {
            height = Mathf.Max(0f, height);
            if (now <= riseStart) return 0f;
            if (now < apexAt)
            {
                float k = Mathf.InverseLerp(riseStart, Mathf.Max(riseStart + 0.001f, apexAt), now);
                return height * Mathf.SmoothStep(0f, 1f, k);
            }
            if (now < descentAt) return height;
            if (now < endAt)
            {
                float k = Mathf.InverseLerp(descentAt, Mathf.Max(descentAt + 0.001f, endAt), now);
                return height * (1f - Mathf.SmoothStep(0f, 1f, k));
            }
            return 0f;
        }

        /// <summary>The dive's hop: a smooth rise from <paramref name="start"/> to <paramref name="peakAt"/> and back to zero exactly at <paramref name="endAt"/>.</summary>
        public static float DiveLift(float now, float start, float peakAt, float endAt, float height)
        {
            height = Mathf.Max(0f, height);
            if (now <= start || now >= endAt) return 0f;
            if (now < peakAt)
            {
                float k = Mathf.InverseLerp(start, Mathf.Max(start + 0.001f, peakAt), now);
                return height * Mathf.SmoothStep(0f, 1f, k);
            }
            float d = Mathf.InverseLerp(peakAt, Mathf.Max(peakAt + 0.001f, endAt), now);
            return height * (1f - Mathf.SmoothStep(0f, 1f, d));
        }

        /// <summary>0 before <paramref name="openAt"/>, ramps to 1 over <paramref name="unfold"/>, holds, then ramps back to 0 from <paramref name="closeAt"/> over <paramref name="fold"/>.</summary>
        public static float WingSpread(float now, float openAt, float unfold, float closeAt, float fold)
        {
            if (now <= openAt) return 0f;
            float open = Mathf.Clamp01((now - openAt) / Mathf.Max(0.001f, unfold));
            if (now <= closeAt) return open;
            float close = 1f - Mathf.Clamp01((now - closeAt) / Mathf.Max(0.001f, fold));
            return Mathf.Min(open, close);
        }

        /// <summary>The flat signed yaw from <paramref name="rootForward"/> to <paramref name="toPlayer"/>, degrees; 0 when degenerate.</summary>
        public static float DesiredTrackYaw(Vector3 rootForward, Vector3 toPlayer)
        {
            Vector3 f = new Vector3(rootForward.x, 0f, rootForward.z);
            Vector3 t = new Vector3(toPlayer.x, 0f, toPlayer.z);
            if (f.sqrMagnitude < 1e-6f || t.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.SignedAngle(f, t, Vector3.up);
        }

        /// <summary>One frame of tracking: the shortest turn toward <paramref name="desired"/>, capped at <paramref name="rate"/> degrees per second.</summary>
        public static float TrackingYawStep(float current, float desired, float rate, float dt)
        {
            return Mathf.MoveTowardsAngle(current, desired, Mathf.Max(0f, rate) * Mathf.Max(0f, dt));
        }

        /// <summary>
        /// The direction feather <paramref name="index"/> of <paramref name="count"/> points on side
        /// <paramref name="side"/> (+1 right, -1 left): out to the side, fanned upward by the sweep, raked back.
        /// </summary>
        public static Vector3 FeatherDirection(int index, int count, float side, float rootPitchDeg, float sweepDeg, float rakeDeg)
        {
            float elev = (rootPitchDeg + sweepDeg * Mathf.Clamp(index, 0, Mathf.Max(0, count - 1))) * Mathf.Deg2Rad;
            float rake = rakeDeg * Mathf.Deg2Rad;
            float s = side >= 0f ? 1f : -1f;
            Vector3 d = new Vector3(s * Mathf.Cos(elev) * Mathf.Cos(rake), Mathf.Sin(elev), -Mathf.Cos(elev) * Mathf.Sin(rake));
            return d.normalized;
        }

        public static float ReanchoredDeadline(float deadline, float oldImpact, float actualImpact)
        {
            return deadline + (actualImpact - oldImpact);
        }

        public static float RetimedSpeed(float speed, float oldImpact, float actualImpact, float now)
        {
            float oldRemaining = Mathf.Max(0.001f, oldImpact - now);
            float newRemaining = Mathf.Max(0.001f, actualImpact - now);
            return Mathf.Max(0.05f, speed) * oldRemaining / newRemaining;
        }

        // ---------------------------------------------------------------- IEnemyPresentation

        public override void Setup(EnemyData d)
        {
            base.Setup(d);
            accentHue = d != null ? SlashFx.NormaliseColor(d.emission) : Color.white;
            controller = transform.root.GetComponent<EnemyController>();
            javelins = transform.root.GetComponent<SeraphLancerJavelins>();
            entranceProbeAt = Time.time + Mathf.Max(0f, entranceProbeDelay);
            entrancePending = true;
            observedAggroLock = controller != null && controller.aggroLocked;
            hoverGroundY = hoverRoot != null ? hoverRoot.localPosition.y : 0f;
            auraPhase = Random.value * 10f;
            Phase = VerdictPhase.None;
            Lift = 0f;
            TrackYaw = 0f;
            WingSpreadNow = 0f;
            BuildWings();
        }

        public override void SetPostureRatio(float r)
        {
            lastPostureRatio = Mathf.Clamp01(r);
            base.SetPostureRatio(r);
        }

        protected override bool UseDefaultAttackClipPlayback(EnemyAttackData atk)
        {
            return atk == null || (atk.name != shoulderChargeAttack && atk.name != skyVerdictAttack && atk.name != heavyAttack);
        }

        public override void Telegraph(EnemyAttackData atk, float seconds)
        {
            CancelSpecialPresentation(false);
            base.Telegraph(atk, seconds);
            if (atk == null) return;

            activeAttack = atk.name;
            float impactAt = Time.time + Mathf.Max(0.05f, seconds + atk.impactDelay);
            if (atk.name == shoulderChargeAttack)
            {
                // As on V18, the Judge and the Dancer: Run carries the readable approach, ShoulderCharge
                // is reserved for the contact beat, so its baked travel never looks like a second
                // locomotion authority.
                float contactSeconds = NamedContactSeconds(shoulderChargeClip, 0.21f);
                shoulderImpactAt = impactAt;
                shoulderClipAt = Mathf.Max(Time.time, impactAt - contactSeconds);
                shoulderClipPending = true;
                // Idle through the wind-up, Run only from the moment the brain starts the travel (spec R2):
                // a Run started at the tell ran in place for half a second before the body moved.
                PlayPresentationClip(clipIdle, 1f);
                ReserveAnimatorUntil(impactAt + followThroughSeconds);
                float cueLeadSeconds = controller != null ? controller.CueLead : 0.28f;
                QueueClip(clipRun, 1f, Mathf.Min(shoulderClipAt, impactAt - EnemyController.LungeWindow(atk.lungeDistance, cueLeadSeconds)), clipBlend * 2f);
                Vector3 feet = transform.root.position + Vector3.up * 0.05f;
                SlashFx.Ring(feet, Vector3.up, accentHue * 0.6f, 1.2f, 0.30f);
                SlashFx.Sparks(feet, -transform.root.forward + Vector3.up * 0.4f, accentHue, 8, 5f, 55f);
            }
            else if (atk.name == heavyAttack)
            {
                // THE DIVE. Jump from its take-off frame, its apex landing where HeavyAttack must begin
                // for the smash's baked contact to land on the impact; a hop on HoverRoot peaks there.
                float contactSeconds = NamedContactSeconds(heavyClip, 0.55f);
                diveStartAt = Time.time;
                diveImpactAt = impactAt;
                diveSwitchAt = Mathf.Max(Time.time, impactAt - contactSeconds);
                divePending = true;
                diveActive = true;
                float jumpLength = ClipLength(jumpClip, 0.96f);
                float segment = Mathf.Max(0.01f, (jumpApexNormalized - jumpTakeoffNormalized) * jumpLength);
                PlayPresentationClipFrom(jumpClip, jumpTakeoffNormalized,
                    Mathf.Clamp(segment / Mathf.Max(0.05f, diveSwitchAt - Time.time), minClipSpeed, maxClipSpeed));
                ReserveAnimatorUntil(impactAt + followThroughSeconds);
                Vector3 feet = transform.root.position + Vector3.up * 0.05f;
                SlashFx.Ring(feet, Vector3.up, accentHue * 0.5f, 1.0f, 0.28f);
            }
            else if (atk.name == skyVerdictAttack)
            {
                // THE RISE. Jump from its first frame at the rate that lands its apex on the wind-up's
                // end; the lift begins at the clip's take-off frame, the wings open with it.
                Phase = VerdictPhase.Rising;
                apexAt = Time.time + Mathf.Max(0.05f, seconds);
                riseStartAt = Time.time + Mathf.Max(0.05f, seconds) *
                              Mathf.Clamp01(jumpTakeoffNormalized / Mathf.Max(0.01f, jumpApexNormalized));
                verdictImpactAt = impactAt;
                verdictStrikeDuration = atk.strikeDuration;
                verdictEndAt = impactAt + atk.strikeDuration;
                descentAt = verdictEndAt - Mathf.Max(0.05f, hoverDescendSeconds);
                wingsOpenAt = riseStartAt;
                wingsCloseAt = descentAt;
                landingPending = true;
                hoverClipPending = true;
                ThrowClipsStaged = 0;
                nextThrowClipAt = impactAt - ReleaseSeconds();
                hoverReturnAt = float.MaxValue;
                nextChargeSparkAt = riseStartAt;
                float jumpLength = ClipLength(jumpClip, 0.96f);
                float toApex = Mathf.Max(0.01f, jumpApexNormalized * jumpLength);
                PlayPresentationClipFrom(jumpClip, 0f,
                    Mathf.Clamp(toApex / Mathf.Max(0.05f, seconds), minClipSpeed, maxClipSpeed));
                ReserveAnimatorUntil(verdictEndAt + landingHoldSeconds);

                // No-contact stance (range <= 0): the floor sigil draws nothing for this attack, so the
                // tell is a hand charge instead — gold, growing over the wind-up, same idea as the
                // Dancer's Crackle.
                verdictChargeStartAt = Time.time;
                verdictChargeEndAt = Time.time + Mathf.Max(0.05f, seconds);
                nextVerdictChargeAt = Time.time;
            }
        }

        public override void Strike(float lunge, float seconds)
        {
            string attackName = activeAttack;
            if (string.IsNullOrEmpty(attackName) && controller != null && controller.CurrentAttack != null)
                attackName = controller.CurrentAttack.name;
            bool travelling = attackName == shoulderChargeAttack;
            base.Strike(travelling ? 0f : lunge, seconds);

            if (controller == null) controller = transform.root.GetComponent<EnemyController>();
            float actualImpact = controller != null ? controller.NextImpactTime : float.MaxValue;
            if (actualImpact == float.MaxValue) return;

            if (attackName == shoulderChargeAttack)
            {
                float oldImpact = shoulderImpactAt;
                if (shoulderClipPending)
                    shoulderClipAt = ReanchoredDeadline(shoulderClipAt, oldImpact, actualImpact);
                else if (animator != null)
                    animator.speed = Mathf.Clamp(
                        RetimedSpeed(animator.speed, oldImpact, actualImpact, Time.time),
                        minClipSpeed, maxClipSpeed);
                shoulderImpactAt = actualImpact;
                ReserveAnimatorUntil(actualImpact + followThroughSeconds);
            }
            else if (attackName == heavyAttack)
            {
                float oldImpact = diveImpactAt;
                if (divePending)
                    diveSwitchAt = ReanchoredDeadline(diveSwitchAt, oldImpact, actualImpact);
                else if (animator != null)
                    animator.speed = Mathf.Clamp(
                        RetimedSpeed(animator.speed, oldImpact, actualImpact, Time.time),
                        minClipSpeed, maxClipSpeed);
                diveImpactAt = actualImpact;
                ReserveAnimatorUntil(actualImpact + followThroughSeconds);
            }
            else if (attackName == skyVerdictAttack)
            {
                // Every hover deadline re-anchored to the brain's real impact: the first release, the
                // cadence after it, the descent and the landing all move together.
                float strike = controller.CurrentAttack != null ? controller.CurrentAttack.strikeDuration
                                                                 : verdictStrikeDuration;
                float delta = actualImpact - verdictImpactAt;
                verdictImpactAt = actualImpact;
                verdictEndAt = actualImpact + strike;
                descentAt = verdictEndAt - Mathf.Max(0.05f, hoverDescendSeconds);
                wingsCloseAt = descentAt;
                if (nextThrowClipAt < float.MaxValue) nextThrowClipAt += delta;
                if (Phase == VerdictPhase.Rising) apexAt = Mathf.Min(apexAt, Time.time);
                ReserveAnimatorUntil(verdictEndAt + landingHoldSeconds);
                AudioManager.Play(Sfx.Dash, 0.35f, 0.75f, 0.04f);
            }
        }

        protected override void Update()
        {
            base.Update();
            UpdateEntrance();

            if (shoulderClipPending && Time.time >= shoulderClipAt)
            {
                shoulderClipPending = false;
                float contactSeconds = NamedContactSeconds(shoulderChargeClip, 0.21f);
                float remaining = Mathf.Max(0.02f, shoulderImpactAt - Time.time);
                PlayPresentationClip(shoulderChargeClip,
                    Mathf.Clamp(contactSeconds / remaining, minClipSpeed, maxClipSpeed));
                ReserveAnimatorUntil(shoulderImpactAt + followThroughSeconds);
            }

            if (divePending && Time.time >= diveSwitchAt)
            {
                divePending = false;
                float contactSeconds = NamedContactSeconds(heavyClip, 0.55f);
                float remaining = Mathf.Max(0.02f, diveImpactAt - Time.time);
                PlayPresentationClip(heavyClip,
                    Mathf.Clamp(contactSeconds / remaining, minClipSpeed, maxClipSpeed));
                ReserveAnimatorUntil(diveImpactAt + followThroughSeconds);
            }
            if (diveActive && Time.time >= diveImpactAt + 0.05f) diveActive = false;

            UpdateVerdict();
            UpdateAura();
            UpdateVerdictHandCharge();
        }

        /// <summary>The Sky Verdict's RightHand tell: a gold flare re-spawned on an interval, growing
        /// <see cref="verdictChargeMinSize"/> to <see cref="verdictChargeMaxSize"/> over the wind-up —
        /// the same repeated-burst-reads-as-continuous trick <see cref="Crackle"/> already uses.</summary>
        void UpdateVerdictHandCharge()
        {
            if (Time.time >= verdictChargeEndAt) { nextVerdictChargeAt = float.MaxValue; return; }
            if (Time.time < nextVerdictChargeAt) return;

            nextVerdictChargeAt = Time.time + verdictChargeInterval;
            float k = verdictChargeEndAt > verdictChargeStartAt
                ? Mathf.Clamp01((Time.time - verdictChargeStartAt) / (verdictChargeEndAt - verdictChargeStartAt))
                : 1f;
            float size = Mathf.Lerp(verdictChargeMinSize, verdictChargeMaxSize, k);
            Vector3 at = transform.root.position + transform.root.right * verdictHandOffset.x
                       + Vector3.up * verdictHandOffset.y + transform.root.forward * verdictHandOffset.z;
            SlashFx.Flare(at, accentHue, size, verdictChargeInterval * 1.4f);
        }

        public override void ClearTelegraph()
        {
            // The brain clears at the end of every phrase, which for the verdict is the landing frame:
            // emit the touchdown there. Any EARLIER clear (a posture break) is a genuine interruption
            // and the body falls out of the air instead.
            CancelSpecialPresentation(true);
            base.ClearTelegraph();
        }

        public override void Recoil()
        {
            CancelSpecialPresentation(false);
            base.Recoil();
        }

        public override void HitFlash()
        {
            // As on V18: ordinary damage does not cancel a committed Windup/Strike, so a staged charge,
            // dive or hover stays visible. Outside commitment, Hit may take presentation ownership.
            if (controller != null && controller.IsCommitted)
            {
                base.HitFlash();
                return;
            }
            CancelSpecialPresentation(false);
            base.HitFlash();
            PlayPresentationClip(clipHit, 1f);
            ReserveAnimatorSoftly(Time.time + Mathf.Max(0.05f, hitHoldSeconds));   // gives way to travel
        }

        public override void Settle(float seconds)
        {
            base.Settle(seconds);
            activeAttack = null;
            entrancePending = false;
        }

        public override void Slump(bool on)
        {
            if (on) CancelSpecialPresentation(false);
            base.Slump(on);
        }

        public override void Die()
        {
            CancelSpecialPresentation(false);
            base.Die();
        }

        public override void Roar()
        {
            base.Roar();
            Crackle(6);
        }

        /// <summary>The lock-on dot and the camera assist must follow the body INTO the air.</summary>
        public override Vector3 BodyPoint(float localHeight)
        {
            return base.BodyPoint(localHeight) + Vector3.up * Lift;
        }

        protected override void OnDestroy()
        {
            if (wingMat != null) { Destroy(wingMat); wingMat = null; }
            base.OnDestroy();
        }

        // ---------------------------------------------------------------- the verdict

        void UpdateVerdict()
        {
            float now = Time.time;
            switch (Phase)
            {
                case VerdictPhase.Rising:
                    if (now >= nextChargeSparkAt)
                    {
                        nextChargeSparkAt = now + Mathf.Max(0.03f, chargeSparkInterval);
                        Crackle(2);
                    }
                    if (now >= apexAt)
                    {
                        Phase = VerdictPhase.Hovering;
                        if (hoverClipPending)
                        {
                            hoverClipPending = false;
                            PlayPresentationClip(hoverClip, 1f);
                            ReserveAnimatorUntil(verdictEndAt + landingHoldSeconds);
                        }
                    }
                    break;

                case VerdictPhase.Hovering:
                {
                    int count = javelins != null ? javelins.javelinsPerVerdict : 3;
                    float cadence = javelins != null ? javelins.javelinCadence : 1.1f;
                    if (ThrowClipsStaged < count && now >= nextThrowClipAt)
                    {
                        // The throw clip, staged so its baked release lands on this release's frame.
                        float release = ReleaseSeconds();
                        float throwAt = SeraphLancerJavelins.ThrowTime(verdictImpactAt, ThrowClipsStaged, cadence);
                        float remaining = Mathf.Max(0.02f, throwAt - now);
                        PlayPresentationClip(throwClip, Mathf.Clamp(release / remaining, minClipSpeed, maxClipSpeed));
                        ThrowClipsStaged++;
                        hoverReturnAt = throwAt + Mathf.Max(0.02f, throwFollowThroughSeconds);
                        nextThrowClipAt = ThrowClipsStaged < count
                            ? SeraphLancerJavelins.ThrowTime(verdictImpactAt, ThrowClipsStaged, cadence) - release
                            : float.MaxValue;
                        ReserveAnimatorUntil(verdictEndAt + landingHoldSeconds);
                    }
                    if (now >= hoverReturnAt)
                    {
                        hoverReturnAt = float.MaxValue;
                        if (now < descentAt)
                        {
                            PlayPresentationClip(hoverClip, 1f);
                            ReserveAnimatorUntil(verdictEndAt + landingHoldSeconds);
                        }
                    }
                    if (now >= descentAt)
                    {
                        Phase = VerdictPhase.Descending;
                        float jumpLength = ClipLength(jumpClip, 0.96f);
                        float segment = Mathf.Max(0.01f, (jumpLandNormalized - jumpApexNormalized) * jumpLength);
                        PlayPresentationClipFrom(jumpClip, jumpApexNormalized,
                            Mathf.Clamp(segment / Mathf.Max(0.05f, verdictEndAt - now), minClipSpeed, maxClipSpeed));
                        ReserveAnimatorUntil(verdictEndAt + landingHoldSeconds);
                    }
                    break;
                }

                case VerdictPhase.Descending:
                    if (now >= verdictEndAt) Land();
                    break;

                case VerdictPhase.Falling:
                    if (now >= fallStartAt + hoverFallSeconds)
                    {
                        Phase = VerdictPhase.None;
                        Lift = 0f;
                    }
                    break;
            }
        }

        protected override void AfterTravelCompensated()
        {
            float now = Time.time;
            float dt = Time.deltaTime;
            switch (Phase)
            {
                case VerdictPhase.Rising:
                case VerdictPhase.Hovering:
                case VerdictPhase.Descending:
                    Lift = HoverLift(now, riseStartAt, apexAt, descentAt, verdictEndAt, hoverHeight);
                    break;
                case VerdictPhase.Falling:
                    Lift = Mathf.Lerp(fallFromLift, 0f,
                        Mathf.Clamp01((now - fallStartAt) / Mathf.Max(0.01f, hoverFallSeconds)));
                    break;
                default:
                    Lift = diveActive ? DiveLift(now, diveStartAt, diveSwitchAt, diveImpactAt, diveHopHeight) : 0f;
                    break;
            }

            // The tracking yaw: the brain does not turn during Strike, so the hovering body keeps its
            // throwing arm on the player itself; it squares back to the brain's facing for the landing.
            float desired = 0f;
            if (Phase == VerdictPhase.Hovering)
            {
                if (playerT == null) { var pc = FindAnyObjectByType<PlayerCombat>(); playerT = pc != null ? pc.transform : null; }
                if (playerT != null)
                    desired = DesiredTrackYaw(transform.root.forward, playerT.position - transform.root.position);
            }
            TrackYaw = TrackingYawStep(TrackYaw, desired, Phase == VerdictPhase.Hovering ? hoverTrackDegPerSec : hoverTrackDegPerSec * 2f, dt);

            if (hoverRoot != null)
            {
                Vector3 p = hoverRoot.localPosition;
                p.y = hoverGroundY + Lift;
                hoverRoot.localPosition = p;
                hoverRoot.localRotation = Quaternion.Euler(0f, TrackYaw, 0f);
            }

            // The wings.
            float spread;
            switch (Phase)
            {
                case VerdictPhase.Rising:
                case VerdictPhase.Hovering:
                case VerdictPhase.Descending:
                    spread = WingSpread(now, wingsOpenAt, wingUnfoldSeconds, wingsCloseAt, wingFoldSeconds);
                    break;
                case VerdictPhase.Falling:
                    spread = fallFromLift > 0.001f
                        ? 1f - Mathf.Clamp01((now - fallStartAt) / Mathf.Max(0.01f, hoverFallSeconds))
                        : 0f;
                    break;
                default:
                    spread = entranceWingsUntil > now
                        ? WingSpread(now, entranceWingsUntil - entranceWingSeconds, wingUnfoldSeconds,
                                     entranceWingsUntil - wingFoldSeconds, wingFoldSeconds)
                        : 0f;
                    break;
            }
            if (controller != null && !controller.IsAlive) spread = 0f;
            WingSpreadNow = spread;
            WriteWings(spread);
        }

        void Land()
        {
            if (!landingPending) { Phase = VerdictPhase.None; Lift = 0f; return; }
            landingPending = false;
            Phase = VerdictPhase.None;
            Lift = 0f;
            TrackYaw = 0f;
            nextThrowClipAt = float.MaxValue;
            hoverReturnAt = float.MaxValue;
            if (hoverRoot != null)
            {
                Vector3 p = hoverRoot.localPosition;
                p.y = hoverGroundY;
                hoverRoot.localPosition = p;
                hoverRoot.localRotation = Quaternion.identity;
            }
            if (animator != null) animator.speed = 1f;
            Vector3 at = transform.root.position + Vector3.up * 0.08f;
            SlashFx.Ring(at, Vector3.up, accentHue, 2.2f, landingRingSeconds);
            SlashFx.Sparks(at, Vector3.up, accentHue, landingSparkCount, landingSparkSpeed, landingSparkSpread);
            AudioManager.Play(Sfx.Land, 0.8f, 0.8f, 0.03f);
        }

        void Crackle(int count)
        {
            Vector3 at = BodyPoint(Random.Range(0.9f, 1.7f))
                       + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.2f, 0.2f));
            SlashFx.Sparks(at, Vector3.up, accentHue, count, 3.5f, 150f);
        }

        // ---------------------------------------------------------------- the wings

        void BuildWings()
        {
            if (wingRoot == null) return;
            // Rebuild from nothing: Setup may run again on a respawned pad enemy.
            for (int i = wingRoot.childCount - 1; i >= 0; i--)
            {
                var c = wingRoot.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            if (wingMat == null)
            {
                // Normalised to a 1.0 peak by CreateAdditiveMaterial: under the 1.05 bloom threshold by
                // construction. The wings locate him in the air; the javelin tip is the bright thing.
                wingMat = SlashFx.CreateAdditiveMaterial(wingHue);
            }
            int per = Mathf.Max(0, feathersPerWing);
            feathers = new Renderer[per * 2];
            featherBaseScale = new Vector3[per * 2];
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? 1f : -1f;
                for (int i = 0; i < per; i++)
                {
                    var pivot = new GameObject((side == 0 ? "FeatherR" : "FeatherL") + (i + 1));
                    pivot.transform.SetParent(wingRoot, false);
                    Vector3 dir = FeatherDirection(i, per, s, wingRootPitchDeg, wingSweepDeg, wingRakeDeg);
                    pivot.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                    // A thin cube, not a quad: visible from both sides, and it has a little depth so the
                    // additive edge reads as a blade of light rather than a card.
                    var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    blade.name = "Blade";
                    var col = blade.GetComponent<Collider>();
                    if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
                    blade.transform.SetParent(pivot.transform, false);
                    float chord = wingChord * (1f - 0.18f * i);
                    blade.transform.localPosition = new Vector3(0f, wingLength * 0.5f, 0f);
                    blade.transform.localScale = new Vector3(chord, wingLength, 0.012f);
                    var r = blade.GetComponent<Renderer>();
                    r.sharedMaterial = wingMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    int k = side * per + i;
                    feathers[k] = r;
                    featherBaseScale[k] = blade.transform.localScale;
                    blade.SetActive(false);
                }
            }
        }

        void WriteWings(float spread)
        {
            if (feathers == null || feathers.Length == 0) return;
            if (wingMpb == null) wingMpb = new MaterialPropertyBlock();
            bool show = spread > 0.001f;
            float flicker = 1f - wingFlickerAmount * 0.5f * (1f + Mathf.Sin(Time.unscaledTime * wingFlickerHz * Mathf.PI * 2f));
            Color hue = SlashFx.NormaliseColor(wingHue);
            int per = Mathf.Max(1, feathersPerWing);
            for (int k = 0; k < feathers.Length; k++)
            {
                var r = feathers[k];
                if (r == null) continue;
                if (r.gameObject.activeSelf != show) r.gameObject.SetActive(show);
                if (!show) continue;
                int i = k % per;
                Vector3 bs = featherBaseScale[k];
                r.transform.localScale = new Vector3(bs.x, bs.y * spread, bs.z);
                r.transform.localPosition = new Vector3(0f, bs.y * spread * 0.5f, 0f);
                float a = wingAlpha * spread * flicker * (1f - 0.15f * i);
                r.GetPropertyBlock(wingMpb);
                wingMpb.SetColor(BaseColorId, new Color(hue.r, hue.g, hue.b, a));
                r.SetPropertyBlock(wingMpb);
            }
        }

        // ---------------------------------------------------------------- aura

        void UpdateAura()
        {
            float heat = Mathf.Lerp(glowAtRest, glowAtBreak, lastPostureRatio);
            switch (Phase)
            {
                case VerdictPhase.Rising:
                    heat = Mathf.Max(heat, Mathf.Lerp(glowAtRest, hoverGlow,
                        Mathf.InverseLerp(riseStartAt, Mathf.Max(riseStartAt + 0.001f, apexAt), Time.time)));
                    break;
                case VerdictPhase.Hovering:
                case VerdictPhase.Descending:
                    heat = Mathf.Max(heat, hoverGlow);
                    break;
            }
            float breath = 1f + Mathf.Sin((Time.unscaledTime + auraPhase) * pulseSpeed * Mathf.PI * 2f) * pulseAmount;
            SetAura(verdigrisHot, heat * breath);
        }

        // ---------------------------------------------------------------- staging helpers

        /// <summary>Seconds into the throw clip at 1x its baked release sits (the 4b-measured contact anchor).</summary>
        float ReleaseSeconds()
        {
            return NamedContactSeconds(throwClip, 0.57f);
        }

        float NamedContactSeconds(string clipName, float fallback)
        {
            int i = IndexOfNamedClip(clipName);
            if (i < 0) return fallback;
            return namedClipLengths[i] * Mathf.Clamp01(namedClipHits[i]);
        }

        /// <summary>Length of any clip in the generated controller, named-table or not (Jump has no contact).</summary>
        float ClipLength(string clipName, float fallback)
        {
            int i = IndexOfNamedClip(clipName);
            if (i >= 0) return namedClipLengths[i];
            if (animator == null || animator.runtimeAnimatorController == null) return fallback;
            var clips = animator.runtimeAnimatorController.animationClips;
            for (int c = 0; c < clips.Length; c++)
                if (clips[c] != null && clips[c].name == clipName) return Mathf.Max(0.01f, clips[c].length);
            return fallback;
        }

        void PlayPresentationClip(string clipName, float speed)
        {
            if (animator == null || animator.runtimeAnimatorController == null ||
                string.IsNullOrEmpty(clipName)) return;
            animator.speed = Mathf.Max(0.05f, speed);
            animator.CrossFadeInFixedTime(clipName, clipBlend, 0, 0f);
        }

        void PlayPresentationClipFrom(string clipName, float normalized, float speed)
        {
            if (animator == null || animator.runtimeAnimatorController == null ||
                string.IsNullOrEmpty(clipName)) return;
            animator.speed = Mathf.Max(0.05f, speed);
            animator.Play(clipName, 0, Mathf.Clamp01(normalized));
        }

        void UpdateEntrance()
        {
            if (!entrancePending) return;
            if (controller == null) controller = transform.root.GetComponent<EnemyController>();
            if (controller == null || !controller.IsAlive ||
                controller.Current == EnemyController.State.Executed)
            {
                entrancePending = false;
                return;
            }
            if (controller.aggroLocked) { observedAggroLock = true; return; }
            if (!observedAggroLock && Time.time < entranceProbeAt) return;

            entrancePending = false;
            if (controller.IsCommitted || controller.IsStaggered) return;
            // Where V18 hops, the Judge roars and the Dancer spins, the Lancer HOPS WITH HIS WINGS OUT:
            // the clip the verdict's rise will reuse and the wings it will open are shown once,
            // harmlessly, before they ever mean anything.
            PlayPresentationClip(jumpClip, 1f);
            ReserveAnimatorUntil(Time.time + Mathf.Max(0.05f, entranceHoldSeconds));
            entranceWingsUntil = Time.time + Mathf.Max(0.05f, entranceWingSeconds);
            Crackle(6);
            AudioManager.Play(Sfx.Jump, 0.35f, 0.9f, 0.04f);
        }

        void CancelSpecialPresentation(bool emitDueLanding)
        {
            shoulderClipPending = false;
            divePending = false;
            entrancePending = false;
            activeAttack = null;
            nextVerdictChargeAt = float.MaxValue;

            bool verdictActive = Phase != VerdictPhase.None && Phase != VerdictPhase.Falling;
            if (!verdictActive)
            {
                if (diveActive && !emitDueLanding) diveActive = false;
                return;
            }

            if (emitDueLanding && Time.time >= verdictEndAt - LandingSafetySeconds)
            {
                Land();
                return;
            }

            // Interrupted before touchdown: the verdict dies where it stands and the body drops.
            landingPending = false;
            hoverClipPending = false;
            nextThrowClipAt = float.MaxValue;
            hoverReturnAt = float.MaxValue;
            if (animator != null) animator.speed = 1f;
            if (Lift > 0.001f)
            {
                Phase = VerdictPhase.Falling;
                fallStartAt = Time.time;
                fallFromLift = Lift;
            }
            else
            {
                Phase = VerdictPhase.None;
                Lift = 0f;
            }
        }
    }
}
