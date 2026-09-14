using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Additive presentation profile for the sandbox-only Cinder Judge, built the way
    /// <see cref="FlurryBrawlerV18Visuals"/> was built: combat stays entirely on EnemyController's data
    /// clock, and this class only stages clips, owns one transform (<see cref="stormRoot"/>: the storm's
    /// lift and spin), writes the ember aura, and draws the tornado at the radius
    /// <see cref="CinderJudgeStorm"/> actually ticks.
    ///
    /// <para><b>Storm Judgement, on the brain's clock.</b> Wind-up = the charge tell: Roar bent onto the
    /// wind-up, sparks crackling off the body, the ground ring growing to full radius. Strike's
    /// impactDelay = the rise: Jump plays from its take-off frame so its apex lands on the brain's
    /// impact, then FREEZES there while <see cref="stormRoot"/> holds the body at
    /// <see cref="stormFloatHeight"/> and turns it at <see cref="stormSpinDegPerSec"/>. The strike's last
    /// <see cref="stormDescendSeconds"/> = the descent: Jump resumes into its landing frame and the yaw
    /// unwinds linearly to square-on at touchdown. Recovery = the punish. Every one of those deadlines
    /// is re-anchored to <see cref="EnemyController.NextImpactTime"/> in <see cref="Strike"/>, as V18's
    /// are, so a frame-late Windup-to-Strike transition cannot move the picture off the arithmetic.</para>
    ///
    /// <para><b>One writer per channel.</b> LungeRoot stays the base class's; SpinRoot stays
    /// PuppetVisuals' whirl (idle wobble); StormRoot, inserted between them by MiniBossFactory, is
    /// written only here. The body's emission floor is asked for through
    /// <see cref="EnemyVisuals.SetAura"/> by this class alone (there is deliberately no EmberAura on
    /// this prefab), so the seams can build through the charge and flicker through the storm without a
    /// second writer.</para>
    /// </summary>
    public sealed class CinderJudgeVisuals : PuppetVisuals
    {
        [Header("Cinder Judge presentation profile")]
        public string stormAttack = "CinderJudge_StormJudgement";
        public string shoulderChargeAttack = "CinderJudge_ShoulderCharge";
        public string shoulderChargeClip = "ShoulderCharge";
        public string roarClip = "Roar";
        public string jumpClip = "Jump";
        [Tooltip("Inserted between LungeRoot and SpinRoot by MiniBossFactory; the ONLY transform the storm writes.")]
        public Transform stormRoot;
        public float entranceProbeDelay = 0.12f;
        public float entranceHoldSeconds = 1.20f;
        public float hitHoldSeconds = 0.50f;

        [Header("Storm Judgement staging")]
        public float stormFloatHeight = 2.4f;
        public float stormDescendSeconds = 0.35f;
        public float stormSpinDegPerSec = 540f;
        public float stormFallSeconds = 0.22f;
        [Tooltip("Jump clip fractions: OnJumpTakeoff / the airborne apex / OnJumpLand from the manifest.")]
        public float jumpTakeoffNormalized = 0.28f;
        public float jumpApexNormalized = 0.56f;
        public float jumpLandNormalized = 0.85f;
        public float landingHoldSeconds = 0.40f;
        public float chargeSparkInterval = 0.12f;
        public float stormArcInterval = 0.36f;
        public float stormArcScale = 0.75f;
        public int stormStrands = 4;
        public float stormCoreIntensity = 1.4f;
        public float stormCrackleSeconds = 0.05f;
        public float stormJitterMetres = 0.22f;
        [ColorUsage(true, true)] public Color stormHue = new Color(1f, 0.82f, 0.29f, 1f);
        public float landingRingSeconds = 0.34f;
        public int landingSparkCount = 14;
        public float landingSparkSpeed = 7f;
        public float landingSparkSpread = 120f;

        [Header("Ember seams (this class is the aura's only writer)")]
        [ColorUsage(true, true)] public Color emberHot = new Color(1f, 0.45f, 0.12f, 1f) * 1.5f;
        [Range(0f, 1f)] public float glowAtRest = 0.18f;
        [Range(0f, 1f)] public float glowAtBreak = 0.50f;
        [Range(0f, 1f)] public float stormGlow = 0.60f;
        public float pulseSpeed = 1.7f;
        [Range(0f, 0.5f)] public float pulseAmount = 0.14f;
        public float stormFlickerHz = 14f;

        public enum StormPhase { None, Charging, Rising, Hovering, Descending, Falling }

        /// <summary>Where the storm performance is, for tests and the harness.</summary>
        public StormPhase Phase { get; private set; }
        /// <summary>Metres the body is currently lifted by StormRoot.</summary>
        public float Lift { get; private set; }
        /// <summary>StormRoot's current yaw, degrees.</summary>
        public float Yaw { get; private set; }

        float shoulderImpactAt, shoulderClipAt;
        bool shoulderClipPending;

        float chargeStartAt, riseStartAt, stormImpactAt, descentAt, stormEndAt, fallStartAt, fallFromLift;
        float stormStrikeDuration;
        float descentYawRate;
        float nextChargeSparkAt, nextArcAt;
        bool landingPending;

        EnemyController controller;
        CinderJudgeStorm storm;
        CinderJudgeStormFx fx;
        float entranceProbeAt;
        bool entrancePending;
        bool observedAggroLock;
        float stormGroundY;
        float lastPostureRatio;
        float auraPhase;
        Color accentHue = Color.white;
        string activeAttack;

        public const float LandingSafetySeconds = 1f / 60f;

        // ---------------------------------------------------------------- pure arithmetic (tests)

        /// <summary>
        /// The body's lift at <paramref name="now"/>: a smooth rise from the strike's start to the
        /// impact, a hold, and a smooth descent over the strike's last <c>descentAt..endAt</c>.
        /// </summary>
        public static float StormLift(float now, float riseStart, float impactAt, float descentAt, float endAt, float height)
        {
            height = Mathf.Max(0f, height);
            if (now <= riseStart) return 0f;
            if (now < impactAt)
            {
                float k = Mathf.InverseLerp(riseStart, Mathf.Max(riseStart + 0.001f, impactAt), now);
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

        /// <summary>
        /// The yaw rate that lands the body square-on: the shortest forward arc from
        /// <paramref name="yaw"/> to the next multiple of 360, spread over <paramref name="seconds"/>.
        /// Forward only, never reversing -- the Marionette rule: a body that reversed its spin to face
        /// you would read as a second move.
        /// </summary>
        public static float SquaringRate(float yaw, float seconds)
        {
            float remaining = 360f - Mathf.Repeat(yaw, 360f);
            if (remaining >= 359.5f) remaining = 0f;
            return remaining / Mathf.Max(0.001f, seconds);
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
            storm = transform.root.GetComponent<CinderJudgeStorm>();
            entranceProbeAt = Time.time + Mathf.Max(0f, entranceProbeDelay);
            entrancePending = true;
            observedAggroLock = controller != null && controller.aggroLocked;
            stormGroundY = stormRoot != null ? stormRoot.localPosition.y : 0f;
            auraPhase = Random.value * 10f;
            Phase = StormPhase.None;
            Lift = 0f;
            Yaw = 0f;
        }

        public override void SetPostureRatio(float r)
        {
            lastPostureRatio = Mathf.Clamp01(r);
            base.SetPostureRatio(r);
        }

        protected override bool UseDefaultAttackClipPlayback(EnemyAttackData atk)
        {
            return atk == null || (atk.name != shoulderChargeAttack && atk.name != stormAttack);
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
                // As on V18: Run carries the readable approach, ShoulderCharge is reserved for the
                // contact beat, so its baked travel never looks like a second locomotion authority.
                float contactSeconds = NamedContactSeconds(shoulderChargeClip, 0.21f);
                shoulderImpactAt = impactAt;
                shoulderClipAt = Mathf.Max(Time.time, impactAt - contactSeconds);
                shoulderClipPending = true;
                PlayPresentationClip(clipRun, 1f);
                ReserveAnimatorUntil(impactAt + followThroughSeconds);
            }
            else if (atk.name == stormAttack)
            {
                // The charge. Roar is bent onto the whole wind-up so its open mouth lands exactly as
                // the strike (the rise) begins; the ring on the floor grows with it. Nothing lifts yet.
                Phase = StormPhase.Charging;
                chargeStartAt = Time.time;
                riseStartAt = Time.time + Mathf.Max(0.001f, seconds);
                stormImpactAt = impactAt;
                stormStrikeDuration = atk.strikeDuration;
                stormEndAt = impactAt + atk.strikeDuration;
                descentAt = stormEndAt - Mathf.Max(0.05f, stormDescendSeconds);
                landingPending = true;
                nextChargeSparkAt = Time.time;
                float roarLength = ClipLength(roarClip, 1.25f);
                PlayPresentationClip(roarClip,
                    Mathf.Clamp(roarLength / Mathf.Max(0.05f, seconds), minClipSpeed, maxClipSpeed));
                ReserveAnimatorUntil(stormEndAt + landingHoldSeconds);
                EnsureFx();
                fx.SetVisible(true, false);
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
            else if (attackName == stormAttack)
            {
                // The rise, re-anchored to the brain's real impact. Jump plays from its take-off frame at
                // whatever rate lands its apex on that impact; the apex is then held (frozen) for the
                // whole hover, and released into the landing frames over the descent.
                float strike = controller.CurrentAttack != null ? controller.CurrentAttack.strikeDuration
                                                                 : stormStrikeDuration;
                riseStartAt = Time.time;
                stormImpactAt = actualImpact;
                stormEndAt = actualImpact + strike;
                descentAt = stormEndAt - Mathf.Max(0.05f, stormDescendSeconds);
                Phase = StormPhase.Rising;
                nextArcAt = actualImpact;
                float jumpLength = ClipLength(jumpClip, 0.96f);
                float segment = Mathf.Max(0.01f, (jumpApexNormalized - jumpTakeoffNormalized) * jumpLength);
                float remaining = Mathf.Max(0.02f, actualImpact - Time.time);
                PlayPresentationClipFrom(jumpClip, jumpTakeoffNormalized,
                    Mathf.Clamp(segment / remaining, minClipSpeed, maxClipSpeed));
                ReserveAnimatorUntil(stormEndAt + landingHoldSeconds);

                EnsureFx();
                fx.SetVisible(true, true);
                Vector3 at = transform.root.position + Vector3.up * 0.08f;
                float radius = storm != null ? storm.radius : 3.6f;
                SlashFx.Ring(at, Vector3.up, stormHue, radius, landingRingSeconds);
                SlashFx.Sparks(BodyPoint(1.2f), Vector3.up, stormHue, 10, 6f, 150f);
                AudioManager.Play(Sfx.Thunder, 0.55f, 1.15f, 0.04f);
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

            UpdateStorm(Time.deltaTime);
            UpdateAura();
        }

        public override void ClearTelegraph()
        {
            // The brain clears at the end of every phrase, which for the storm is the landing frame:
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
            // As on V18: ordinary damage does not cancel a committed Windup/Strike, so the staged storm
            // or charge stays visible. Outside commitment, Hit may take presentation ownership.
            if (controller != null && controller.IsCommitted)
            {
                base.HitFlash();
                return;
            }
            CancelSpecialPresentation(false);
            base.HitFlash();
            PlayPresentationClip(clipHit, 1f);
            ReserveAnimatorUntil(Time.time + Mathf.Max(0.05f, hitHoldSeconds));
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
            PlayPresentationClip(roarClip, 1f);
            ReserveAnimatorUntil(Time.time + Mathf.Max(0.05f, entranceHoldSeconds));
            Crackle(8);
        }

        /// <summary>The lock-on dot and the camera assist must follow the body INTO the air.</summary>
        public override Vector3 BodyPoint(float localHeight)
        {
            return base.BodyPoint(localHeight) + Vector3.up * Lift;
        }

        protected override void OnDestroy()
        {
            if (fx != null) { fx.Dispose(); fx = null; }
            base.OnDestroy();
        }

        // ---------------------------------------------------------------- the storm

        void UpdateStorm(float dt)
        {
            float now = Time.time;
            switch (Phase)
            {
                case StormPhase.Charging:
                    if (now >= nextChargeSparkAt)
                    {
                        nextChargeSparkAt = now + Mathf.Max(0.03f, chargeSparkInterval);
                        Crackle(2);
                    }
                    break;

                case StormPhase.Rising:
                    if (now >= stormImpactAt)
                    {
                        Phase = StormPhase.Hovering;
                        if (animator != null) animator.speed = 0f;   // hold the apex; the spin sells the motion
                    }
                    break;

                case StormPhase.Hovering:
                    Yaw += stormSpinDegPerSec * dt;
                    if (now >= nextArcAt) EmitArc();
                    if (now >= descentAt)
                    {
                        Phase = StormPhase.Descending;
                        descentYawRate = SquaringRate(Yaw, Mathf.Max(0.05f, stormEndAt - now));
                        float jumpLength = ClipLength(jumpClip, 0.96f);
                        float segment = Mathf.Max(0.01f, (jumpLandNormalized - jumpApexNormalized) * jumpLength);
                        if (animator != null)
                            animator.speed = Mathf.Clamp(segment / Mathf.Max(0.05f, stormEndAt - now),
                                                         minClipSpeed, maxClipSpeed);
                    }
                    break;

                case StormPhase.Descending:
                    Yaw += descentYawRate * dt;
                    if (now >= stormEndAt) Land();
                    break;

                case StormPhase.Falling:
                    if (now >= fallStartAt + stormFallSeconds)
                    {
                        Phase = StormPhase.None;
                        Lift = 0f;
                    }
                    break;

                case StormPhase.None:
                    if (Yaw != 0f)
                    {
                        // A cancelled storm left the body off-square: unwind forward, never reverse.
                        float rate = Mathf.Max(spinSettleAccel, stormSpinDegPerSec * 0.5f);
                        Yaw = Mathf.MoveTowards(Yaw, 360f * Mathf.Ceil(Yaw / 360f), rate * dt);
                        if (Mathf.Repeat(Yaw, 360f) < 0.01f || Mathf.Repeat(Yaw, 360f) > 359.99f) Yaw = 0f;
                    }
                    break;
            }
        }

        protected override void AfterTravelCompensated()
        {
            float now = Time.time;
            switch (Phase)
            {
                case StormPhase.Rising:
                case StormPhase.Hovering:
                case StormPhase.Descending:
                    Lift = StormLift(now, riseStartAt, stormImpactAt, descentAt, stormEndAt, stormFloatHeight);
                    break;
                case StormPhase.Falling:
                    Lift = Mathf.Lerp(fallFromLift, 0f,
                        Mathf.Clamp01((now - fallStartAt) / Mathf.Max(0.01f, stormFallSeconds)));
                    break;
                default:
                    Lift = 0f;
                    break;
            }

            if (stormRoot != null)
            {
                Vector3 p = stormRoot.localPosition;
                p.y = stormGroundY + Lift;
                stormRoot.localPosition = p;
                stormRoot.localRotation = Quaternion.Euler(0f, Yaw, 0f);
            }

            if (fx == null) return;
            if (Phase == StormPhase.None)
            {
                fx.SetVisible(false, false);
                return;
            }
            float radius = storm != null ? storm.radius : 3.6f;
            Vector3 ground = transform.root.position;
            float udt = Time.unscaledDeltaTime;
            if (Phase == StormPhase.Charging)
            {
                float k = Mathf.InverseLerp(chargeStartAt, riseStartAt, now);
                fx.Draw(ground, radius, radius * Mathf.SmoothStep(0f, 1f, k), 0f, Yaw, 0.15f + 0.35f * k, 0f, udt);
            }
            else if (Phase == StormPhase.Falling)
            {
                float k = Mathf.Clamp01((now - fallStartAt) / Mathf.Max(0.01f, stormFallSeconds));
                fx.Draw(ground, radius, radius, Lift + 2.2f, Yaw, 1f - k, 0.6f * (1f - k), udt);
            }
            else
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f * Mathf.PI * 2f);
                float strand = Phase == StormPhase.Rising
                    ? Mathf.InverseLerp(riseStartAt, Mathf.Max(riseStartAt + 0.001f, stormImpactAt), now)
                    : 1f;
                fx.Draw(ground, radius, radius, Lift + 2.2f, Yaw, pulse, strand, udt);
            }
        }

        void Land()
        {
            if (!landingPending) { Phase = StormPhase.None; Lift = 0f; return; }
            landingPending = false;
            Phase = StormPhase.None;
            Lift = 0f;
            Yaw = 0f;
            if (stormRoot != null)
            {
                Vector3 p = stormRoot.localPosition;
                p.y = stormGroundY;
                stormRoot.localPosition = p;
                stormRoot.localRotation = Quaternion.identity;
            }
            if (animator != null) animator.speed = 1f;
            if (fx != null) fx.SetVisible(false, false);
            Vector3 at = transform.root.position + Vector3.up * 0.08f;
            float radius = storm != null ? storm.radius : 3.6f;
            SlashFx.Ring(at, Vector3.up, accentHue, radius, landingRingSeconds);
            SlashFx.Sparks(at, Vector3.up, accentHue, landingSparkCount, landingSparkSpeed, landingSparkSpread);
            AudioManager.Play(Sfx.Land, 0.8f, 0.7f, 0.03f);
        }

        void EmitArc()
        {
            nextArcAt = Time.time + Mathf.Max(0.34f, stormArcInterval);   // never two bundles alive: 2 lights, not 4
            if (fx == null) return;
            float radius = storm != null ? storm.radius : 3.6f;
            Vector3 from = BodyPoint(1.4f);
            Vector3 to = fx.RimPoint(transform.root.position, radius);
            LightningEffect.Bundle(from, to, stormHue, stormArcScale);
        }

        void Crackle(int count)
        {
            Vector3 at = BodyPoint(Random.Range(0.6f, 1.7f))
                       + new Vector3(Random.Range(-0.35f, 0.35f), 0f, Random.Range(-0.25f, 0.25f));
            SlashFx.Sparks(at, Vector3.up, stormHue, count, 3.5f, 160f);
        }

        void UpdateAura()
        {
            float heat = Mathf.Lerp(glowAtRest, glowAtBreak, lastPostureRatio);
            float now = Time.time;
            switch (Phase)
            {
                case StormPhase.Charging:
                    heat = Mathf.Max(heat, Mathf.Lerp(glowAtRest, stormGlow,
                        Mathf.InverseLerp(chargeStartAt, riseStartAt, now)));
                    break;
                case StormPhase.Rising:
                case StormPhase.Hovering:
                case StormPhase.Descending:
                    heat = stormGlow * (0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * stormFlickerHz * Mathf.PI * 2f));
                    break;
            }
            float breath = 1f + Mathf.Sin((Time.unscaledTime + auraPhase) * pulseSpeed * Mathf.PI * 2f) * pulseAmount;
            SetAura(emberHot, heat * breath);
        }

        void EnsureFx()
        {
            if (fx != null) return;
            fx = new CinderJudgeStormFx(transform.root.name, stormHue, stormStrands, stormCoreIntensity,
                                        stormCrackleSeconds, stormJitterMetres);
        }

        // ---------------------------------------------------------------- staging helpers

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
            // Where V18 hops on wake, the Judge ROARS: the seams crackle, and the clip the storm's
            // charge will reuse is shown once, harmlessly, before it ever means anything.
            PlayPresentationClip(roarClip, 1f);
            ReserveAnimatorUntil(Time.time + Mathf.Max(0.05f, entranceHoldSeconds));
            Crackle(8);
            AudioManager.Play(Sfx.Roar, 0.45f, 0.85f, 0.04f);
        }

        void CancelSpecialPresentation(bool emitDueLanding)
        {
            shoulderClipPending = false;
            entrancePending = false;
            activeAttack = null;

            bool stormActive = Phase != StormPhase.None && Phase != StormPhase.Falling;
            if (!stormActive) return;

            if (emitDueLanding && Phase != StormPhase.Charging && Time.time >= stormEndAt - LandingSafetySeconds)
            {
                Land();
                return;
            }

            // Interrupted before touchdown: the storm dies where it stands and the body drops.
            landingPending = false;
            if (fx != null) fx.SetVisible(false, false);
            if (animator != null) animator.speed = 1f;
            if (Lift > 0.001f)
            {
                Phase = StormPhase.Falling;
                fallStartAt = Time.time;
                fallFromLift = Lift;
            }
            else
            {
                Phase = StormPhase.None;
                Lift = 0f;
            }
        }
    }
}
