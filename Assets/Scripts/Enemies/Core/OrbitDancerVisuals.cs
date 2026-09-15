using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Additive presentation profile for the sandbox-only Orbit Dancer, built the way
    /// <see cref="CinderJudgeVisuals"/> was built: combat stays entirely on EnemyController's data
    /// clock, and this class only stages clips, writes the teal-seam aura, spins the three satellite
    /// discs that orbit her at rest, and crackles the throwing hand while a throw winds up.
    ///
    /// <para><b>The throws need no staging.</b> DiscThrow and SpinThrow are named on their attacks
    /// (<c>EnemyAttackData.clip</c>), so <see cref="PuppetVisuals"/> bends each clip's own release frame
    /// onto the brain's impact exactly as it does for Jab2 or the Heavy; <c>OrbitDancerDiscs</c> reads
    /// that same impact time and launches. Only ShoulderCharge keeps V18's Run-then-charge staging.</para>
    ///
    /// <para><b>One writer per channel.</b> LungeRoot stays the base class's; SpinRoot stays
    /// PuppetVisuals' whirl; TravelRoot stays CompensateTravel's. <see cref="orbitRoot"/> (a child of
    /// LungeRoot, built by MiniBossFactory) is written only here. The body's emission floor is asked for
    /// through <see cref="EnemyVisuals.SetAura"/> by this class alone -- there is no EmberAura on this
    /// prefab.</para>
    /// </summary>
    public sealed class OrbitDancerVisuals : PuppetVisuals
    {
        [Header("Orbit Dancer presentation profile")]
        public string discThrowAttack = "OrbitDancer_DiscThrow";
        public string spinThrowAttack = "OrbitDancer_SpinThrow";
        public string shoulderChargeAttack = "OrbitDancer_ShoulderCharge";
        public string shoulderChargeClip = "ShoulderCharge";
        [Tooltip("Played once on wake, harmlessly: the spin you will later be thrown discs out of.")]
        public string entranceClip = "SpinThrow";
        public float entranceProbeDelay = 0.12f;
        public float entranceHoldSeconds = 0.65f;
        [Tooltip("Hit flinch hold. 0.35 is the fluidity pass's cap: a held pose on a moving body glides.")]
        public float hitHoldSeconds = 0.35f;

        [Header("Throw wind-up (the disc spins in the hand)")]
        public float chargeSparkInterval = 0.10f;
        [Tooltip("Local offset from the body's centre line to the throwing hand while charging, metres.")]
        public Vector3 chargeHandOffset = new Vector3(0.38f, 1.25f, 0.25f);

        [Header("Satellite discs (this class is orbitRoot's only writer)")]
        [Tooltip("Built by MiniBossFactory under LungeRoot with satelliteCount disc meshes on a ring.")]
        public Transform orbitRoot;
        public int satelliteCount = 3;
        public float orbitRadius = 0.85f;
        public float orbitHeight = 1.25f;
        public float orbitDegPerSec = 140f;
        public float orbitBobMetres = 0.07f;
        public float orbitBobHz = 0.7f;
        [Tooltip("Satellites hidden while this many of her thrown discs are alive: a throw visibly spends a ring.")]
        public bool satellitesFollowThrownDiscs = true;

        [Header("Teal seams (this class is the aura's only writer)")]
        [ColorUsage(true, true)] public Color tealHot = new Color(0.30f, 1.0f, 0.92f, 1f) * 1.4f;
        [Range(0f, 1f)] public float glowAtRest = 0.16f;
        [Range(0f, 1f)] public float glowAtBreak = 0.45f;
        [Range(0f, 1f)] public float throwGlow = 0.55f;
        public float pulseSpeed = 2.2f;
        [Range(0f, 0.5f)] public float pulseAmount = 0.12f;

        /// <summary>True from a throw's Telegraph until its Strike: the hand is crackling.</summary>
        public bool Charging { get; private set; }
        /// <summary>orbitRoot's current yaw, degrees.</summary>
        public float OrbitYaw { get; private set; }

        float shoulderImpactAt, shoulderClipAt;
        bool shoulderClipPending;
        float chargeStartAt, chargeEndAt, nextChargeSparkAt;

        EnemyController controller;
        OrbitDancerDiscs discs;
        float entranceProbeAt;
        bool entrancePending;
        bool observedAggroLock;
        float lastPostureRatio;
        float auraPhase;
        Color accentHue = Color.white;
        string activeAttack;

        // ---------------------------------------------------------------- pure arithmetic (tests)

        /// <summary>How many satellites to show: the ring minus the discs she has out, never below zero.</summary>
        public static int VisibleSatellites(int satellites, int thrownAlive, bool follow)
        {
            if (!follow) return Mathf.Max(0, satellites);
            return Mathf.Clamp(satellites - Mathf.Max(0, thrownAlive), 0, Mathf.Max(0, satellites));
        }

        /// <summary>The local position of satellite <paramref name="index"/> of <paramref name="count"/> on the ring at <paramref name="yaw"/>.</summary>
        public static Vector3 SatelliteLocal(int index, int count, float yaw, float radius, float bob, float bobHz, float time)
        {
            float a = (yaw + 360f * index / Mathf.Max(1, count)) * Mathf.Deg2Rad;
            float y = bob * Mathf.Sin((time * bobHz + index / (float)Mathf.Max(1, count)) * Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
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
            discs = transform.root.GetComponent<OrbitDancerDiscs>();
            entranceProbeAt = Time.time + Mathf.Max(0f, entranceProbeDelay);
            entrancePending = true;
            observedAggroLock = controller != null && controller.aggroLocked;
            auraPhase = Random.value * 10f;
            Charging = false;
            OrbitYaw = 0f;
        }

        public override void SetPostureRatio(float r)
        {
            lastPostureRatio = Mathf.Clamp01(r);
            base.SetPostureRatio(r);
        }

        protected override bool UseDefaultAttackClipPlayback(EnemyAttackData atk)
        {
            return atk == null || atk.name != shoulderChargeAttack;
        }

        public override void Telegraph(EnemyAttackData atk, float seconds)
        {
            CancelSpecialPresentation();
            base.Telegraph(atk, seconds);
            if (atk == null) return;

            activeAttack = atk.name;
            float impactAt = Time.time + Mathf.Max(0.05f, seconds + atk.impactDelay);
            if (atk.name == shoulderChargeAttack)
            {
                // As on V18 and the Judge: Run carries the readable approach, ShoulderCharge is reserved
                // for the contact beat, so its baked travel never looks like a second locomotion authority.
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
            else if (atk.name == discThrowAttack || atk.name == spinThrowAttack)
            {
                // The tell: the disc spins in the hand. Teal sparks off the throwing hand for the whole
                // wind-up; the clip itself (named on the attack) is already playing through the base.
                Charging = true;
                chargeStartAt = Time.time;
                chargeEndAt = impactAt;
                nextChargeSparkAt = Time.time;
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
            else if (Charging)
            {
                // The crackle runs to the real release, then OrbitDancerDiscs takes over on that frame.
                chargeEndAt = actualImpact;
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

            if (Charging)
            {
                if (Time.time >= chargeEndAt) Charging = false;
                else if (Time.time >= nextChargeSparkAt)
                {
                    nextChargeSparkAt = Time.time + Mathf.Max(0.03f, chargeSparkInterval);
                    Crackle(2);
                }
            }

            UpdateAura();
        }

        public override void ClearTelegraph()
        {
            CancelSpecialPresentation();
            base.ClearTelegraph();
        }

        public override void Recoil()
        {
            CancelSpecialPresentation();
            base.Recoil();
        }

        public override void HitFlash()
        {
            // As on V18: ordinary damage does not cancel a committed Windup/Strike, so a staged charge
            // or a throw's crackle stays visible. Outside commitment, Hit may take presentation ownership.
            if (controller != null && controller.IsCommitted)
            {
                base.HitFlash();
                return;
            }
            CancelSpecialPresentation();
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
            if (on) CancelSpecialPresentation();
            base.Slump(on);
        }

        public override void Die()
        {
            CancelSpecialPresentation();
            base.Die();
        }

        public override void Roar()
        {
            base.Roar();
            Crackle(8);
        }

        // ---------------------------------------------------------------- the satellites

        protected override void AfterTravelCompensated()
        {
            if (orbitRoot == null) return;
            float dt = Time.deltaTime;
            bool spinning = controller == null || (controller.IsAlive && !controller.IsStaggered);
            if (spinning) OrbitYaw = Mathf.Repeat(OrbitYaw + orbitDegPerSec * dt, 360f);

            int count = orbitRoot.childCount;
            int visible = VisibleSatellites(count, discs != null ? discs.LiveOwned : 0, satellitesFollowThrownDiscs);
            if (controller != null && !controller.IsAlive) visible = 0;
            for (int i = 0; i < count; i++)
            {
                var s = orbitRoot.GetChild(i);
                s.localPosition = SatelliteLocal(i, count, OrbitYaw, orbitRadius, orbitBobMetres, orbitBobHz, Time.time);
                s.localRotation = Quaternion.Euler(0f, -OrbitYaw * 3f, 0f);   // each disc spins on its own axis, faster than it orbits
                bool show = i < visible;
                if (s.gameObject.activeSelf != show) s.gameObject.SetActive(show);
            }
        }

        // ---------------------------------------------------------------- aura and sparks

        void UpdateAura()
        {
            float heat = Mathf.Lerp(glowAtRest, glowAtBreak, lastPostureRatio);
            if (Charging)
                heat = Mathf.Max(heat, Mathf.Lerp(glowAtRest, throwGlow,
                    Mathf.InverseLerp(chargeStartAt, Mathf.Max(chargeStartAt + 0.001f, chargeEndAt), Time.time)));
            float breath = 1f + Mathf.Sin((Time.unscaledTime + auraPhase) * pulseSpeed * Mathf.PI * 2f) * pulseAmount;
            SetAura(tealHot, heat * breath);
        }

        void Crackle(int count)
        {
            Transform root = transform.root;
            Vector3 at = root.position + root.right * chargeHandOffset.x + Vector3.up * chargeHandOffset.y
                       + root.forward * chargeHandOffset.z;
            SlashFx.Sparks(at, Vector3.up, accentHue, count, 3f, 150f);
        }

        // ---------------------------------------------------------------- staging helpers

        float NamedContactSeconds(string clipName, float fallback)
        {
            int i = IndexOfNamedClip(clipName);
            if (i < 0) return fallback;
            return namedClipLengths[i] * Mathf.Clamp01(namedClipHits[i]);
        }

        void PlayPresentationClip(string clipName, float speed)
        {
            if (animator == null || animator.runtimeAnimatorController == null ||
                string.IsNullOrEmpty(clipName)) return;
            animator.speed = Mathf.Max(0.05f, speed);
            animator.CrossFadeInFixedTime(clipName, clipBlend, 0, 0f);
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
            // Where V18 hops and the Judge roars, the Dancer SPINS: the clip the whirl will reuse is
            // shown once, harmlessly, before it ever means anything. OrbitDancerDiscs keys on the
            // brain's attack name, so a presentation SpinThrow launches nothing.
            PlayPresentationClip(entranceClip, 1f);
            ReserveAnimatorUntil(Time.time + Mathf.Max(0.05f, entranceHoldSeconds));
            Crackle(6);
            SlashFx.Ring(transform.root.position + Vector3.up * 0.08f, Vector3.up, accentHue * 0.6f, 1.4f, 0.30f);
        }

        void CancelSpecialPresentation()
        {
            shoulderClipPending = false;
            entrancePending = false;
            activeAttack = null;
            Charging = false;
        }
    }
}
