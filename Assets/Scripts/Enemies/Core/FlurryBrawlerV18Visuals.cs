using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Additive presentation profile for the sandbox-only V18 Flurry Brawler. Combat remains entirely
    /// on EnemyController's data clock: this class only stages clips and emits the Clap's existing
    /// ring/spark vocabulary at the already-scheduled impact, even when the distance/cone check misses.
    /// </summary>
    public sealed class FlurryBrawlerV18Visuals : PuppetVisuals
    {
        [Header("V18 presentation profile")]
        public string shoulderChargeAttack = "BrawlerV18_ShoulderCharge";
        public string shoulderChargeClip = "ShoulderCharge";
        public string dashAttack = "BrawlerV18_Dash";
        public string clapAttack = "BrawlerV18_LevitateClap";
        public string clapClip = "Clap";
        public string comboAttack = "BrawlerV18_Combo2";
        public string comboClip = "Combo2";
        public string jumpClip = "Jump";
        public string blockClip = "Block";
        public float entranceProbeDelay = 0.12f;
        public float entranceHoldSeconds = 0.95f;
        public float hitHoldSeconds = 0.50f;
        public float clapRingRadius = 3.8f;
        public float clapRingSeconds = 0.34f;
        public int clapSparkCount = 14;
        public float clapSparkSpeed = 7f;
        public float clapSparkSpread = 120f;

        float shoulderImpactAt;
        float shoulderClipAt;
        bool shoulderClipPending;
        float clapImpactAt;
        float clapClipAt;
        bool clapClipPending;
        bool clapImpactPending;
        float clapLiftStartAt;
        float clapLiftTopAt;
        float clapStrikeAt;
        float clapClampUntil;
        float clapGroundY;
        float clapLiftHeight;
        bool clapLiftOwned;
        bool clapDescending;
        float comboImpactAt;
        float comboHoldUntil;
        bool comboTailReservePending;
        EnemyController controller;
        float entranceProbeAt;
        bool entrancePending;
        bool observedAggroLock;
        Color clapHue = Color.white;
        string activeAttack;

        public const float ComboTailSafetySeconds = 1f / 60f;

        public static float ComboTailSeconds(float clipLength, float hitNormalized)
        {
            return Mathf.Max(0f, clipLength) * (1f - Mathf.Clamp01(hitNormalized)) +
                   ComboTailSafetySeconds;
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

        public static float ClapLiftOffset(float now, float windupStart, float windupTop,
                                           bool descending, float strikeAt, float impactAt, float height)
        {
            height = Mathf.Max(0f, height);
            if (!descending)
            {
                float rise = Mathf.InverseLerp(windupStart, Mathf.Max(windupStart + 0.001f, windupTop), now);
                return Mathf.Lerp(0f, height, rise);
            }
            float down = Mathf.InverseLerp(strikeAt, Mathf.Max(strikeAt + 0.001f, impactAt), now);
            return Mathf.Lerp(height, 0f, down);
        }

        public static Vector3 ClampToGroundY(Vector3 localPosition, float groundY)
        {
            localPosition.y = groundY;
            return localPosition;
        }

        public override void Setup(EnemyData d)
        {
            base.Setup(d);
            clapHue = d != null ? SlashFx.NormaliseColor(d.emission) : Color.white;
            controller = transform.root.GetComponent<EnemyController>();
            entranceProbeAt = Time.time + Mathf.Max(0f, entranceProbeDelay);
            entrancePending = true;
            observedAggroLock = controller != null && controller.aggroLocked;
            clapGroundY = lungeRoot != null ? lungeRoot.localPosition.y : 0f;
        }

        protected override bool UseDefaultAttackClipPlayback(EnemyAttackData atk)
        {
            return atk == null || (atk.name != shoulderChargeAttack && atk.name != clapAttack);
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
                // Run carries the readable approach. ShoulderCharge is reserved for the contact beat,
                // so its early baked travel cannot look like a second locomotion authority underneath
                // EnemyController's one scheduled lunge.
                float contactSeconds = NamedContactSeconds(shoulderChargeClip, 0.21f);
                shoulderImpactAt = impactAt;
                shoulderClipAt = Mathf.Max(Time.time, impactAt - contactSeconds);
                shoulderClipPending = true;
                PlayPresentationClip(clipRun, 1f);
                ReserveAnimatorUntil(impactAt + followThroughSeconds);
            }
            else if (atk.name == clapAttack)
            {
                // The two-second levitation is a still threat, not two seconds of palms crawling
                // together. Hold Idle while the authored bodyOffset lifts the LungeRoot, then enter
                // Clap only late enough for its contact frame to meet the touchdown/data impact.
                float contactSeconds = NamedContactSeconds(clapClip, 0.42f);
                clapImpactAt = impactAt;
                clapClipAt = Mathf.Max(Time.time, impactAt - contactSeconds);
                clapClipPending = true;
                clapImpactPending = true;
                clapLiftStartAt = Time.time;
                clapLiftTopAt = Time.time + Mathf.Max(0.001f, seconds);
                clapStrikeAt = clapLiftTopAt;
                clapClampUntil = impactAt + atk.strikeDuration + ComboTailSafetySeconds;
                clapLiftHeight = atk.windupPose != null && atk.windupPose.authored
                    ? atk.windupPose.bodyOffset.y : 1.60f;
                clapLiftOwned = true;
                clapDescending = false;
                PlayPresentationClip(clipIdle, 1f);
                ReserveAnimatorUntil(impactAt + followThroughSeconds);
            }
            else if (atk.name == comboAttack)
            {
                // Combo2 is one long performance around ONE data impact. Let PuppetVisuals keep its
                // approved .671 contact on the data clock, then retain Animator ownership through the
                // whole authored tail after the base class returns playback to recoverySpeed.
                int i = IndexOfNamedClip(comboClip);
                float length = i >= 0 ? namedClipLengths[i] : 0f;
                float hit = i >= 0 ? namedClipHits[i] : 0.671f;
                comboImpactAt = impactAt;
                comboHoldUntil = impactAt + ComboTailSeconds(length, hit);
                comboTailReservePending = true;
            }
        }

        public override void Strike(float lunge, float seconds)
        {
            // EnemyController is the sole travel authority for the two moving attacks. Their Hips XZ
            // is cancelled on TravelRoot, and the ordinary EnemyVisuals LungeRoot offset is suppressed,
            // so neither clip nor presentation silently doubles the data's 1.77/4.12 m displacement.
            string attackName = activeAttack;
            if (string.IsNullOrEmpty(attackName) && controller != null && controller.CurrentAttack != null)
                attackName = controller.CurrentAttack.name;
            bool travelling = attackName == dashAttack || attackName == shoulderChargeAttack;
            base.Strike(travelling ? 0f : lunge, seconds);

            if (controller == null) controller = transform.root.GetComponent<EnemyController>();
            float actualImpact = controller != null ? controller.NextImpactTime : float.MaxValue;
            if (actualImpact == float.MaxValue) return;

            if (attackName == shoulderChargeAttack)
            {
                ReanchorStagedClip(ref shoulderImpactAt, ref shoulderClipAt,
                                   shoulderClipPending, actualImpact);
                ReserveAnimatorUntil(actualImpact + followThroughSeconds);
            }
            else if (attackName == clapAttack)
            {
                ReanchorStagedClip(ref clapImpactAt, ref clapClipAt, clapClipPending, actualImpact);
                clapStrikeAt = Time.time;
                float strikeTail = controller.CurrentAttack != null
                    ? controller.CurrentAttack.strikeDuration
                    : Mathf.Max(0f, seconds - (actualImpact - Time.time));
                clapClampUntil = actualImpact + strikeTail + ComboTailSafetySeconds;
                clapDescending = true;
                ReserveAnimatorUntil(actualImpact + followThroughSeconds);
            }
            else if (attackName == comboAttack)
            {
                float oldImpact = comboImpactAt;
                if (animator != null)
                    animator.speed = Mathf.Clamp(
                        RetimedSpeed(animator.speed, oldImpact, actualImpact, Time.time),
                        minClipSpeed, maxClipSpeed);
                comboImpactAt = actualImpact;
                int i = IndexOfNamedClip(comboClip);
                float length = i >= 0 ? namedClipLengths[i] : 0f;
                float hit = i >= 0 ? namedClipHits[i] : 0.671f;
                comboHoldUntil = actualImpact + ComboTailSeconds(length, hit);
                ReanchorAnimatorImpact(actualImpact);
            }
        }

        protected override void Update()
        {
            base.Update();

            UpdateEntrance();

            if (comboTailReservePending && Time.time >= comboImpactAt)
            {
                // Base.Update has just restored recoverySpeed at contact. Reserve only now: doing it in
                // Telegraph would clear that scheduled speed handoff and run the entire tail at ~2.8x.
                comboTailReservePending = false;
                ReserveAnimatorUntil(comboHoldUntil);
            }

            if (shoulderClipPending && Time.time >= shoulderClipAt)
            {
                shoulderClipPending = false;
                float contactSeconds = NamedContactSeconds(shoulderChargeClip, 0.21f);
                float remaining = Mathf.Max(0.02f, shoulderImpactAt - Time.time);
                PlayPresentationClip(shoulderChargeClip,
                    Mathf.Clamp(contactSeconds / remaining, minClipSpeed, maxClipSpeed));
                ReserveAnimatorUntil(shoulderImpactAt + followThroughSeconds);
            }

            if (clapClipPending && Time.time >= clapClipAt)
            {
                clapClipPending = false;
                float contactSeconds = NamedContactSeconds(clapClip, 0.42f);
                float remaining = Mathf.Max(0.02f, clapImpactAt - Time.time);
                PlayPresentationClip(clapClip,
                    Mathf.Clamp(contactSeconds / remaining, minClipSpeed, maxClipSpeed));
                ReserveAnimatorUntil(clapImpactAt + followThroughSeconds);
            }

            if (clapImpactPending && Time.time >= clapImpactAt) EmitClapImpact();
        }

        public override void ClearTelegraph()
        {
            // A parry resolves at the same scheduled instant as a hit. Preserve the touchdown accent on
            // that frame, but cancel it for every genuine pre-impact interruption.
            CancelSpecialPresentation(true, true);
            base.ClearTelegraph();
        }

        public override void Recoil()
        {
            CancelSpecialPresentation(true);
            base.Recoil();
        }

        public override void HitFlash()
        {
            // Ordinary damage does not cancel EnemyController's committed Windup/Strike. Preserve the
            // staged clip/contact in that case or the still-scheduled Clap/Dash/Shoulder blow becomes
            // invisible. Outside commitment, Hit is allowed to take presentation ownership normally.
            if (controller != null && controller.IsCommitted)
            {
                base.HitFlash();
                return;
            }

            // Damage is an interrupt, so it wins over the held Combo2 tail just as stagger/death do.
            CancelSpecialPresentation(false);
            base.HitFlash();
            PlayPresentationClip(clipHit, 1f);
            ReserveAnimatorUntil(Time.time + Mathf.Max(0.05f, hitHoldSeconds));
        }

        public override void Settle(float seconds)
        {
            bool holdCombo = Time.time < comboHoldUntil;
            base.Settle(seconds);
            activeAttack = null;
            entrancePending = false;
            if (holdCombo)
            {
                // Natural recovery must not replace the one-contact performance before its tail ends.
                ReserveAnimatorUntil(comboHoldUntil);
                return;
            }

            PlayPresentationClip(blockClip, 1f);
            ReserveAnimatorUntil(Time.time + Mathf.Max(0.12f, seconds));
        }

        public override void Slump(bool on)
        {
            CancelSpecialPresentation(false);
            base.Slump(on);
        }

        public override void Die()
        {
            CancelSpecialPresentation(false);
            base.Die();
        }

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

        void ReanchorStagedClip(ref float impactAt, ref float clipAt, bool clipPending,
                                float actualImpact)
        {
            float oldImpact = impactAt;
            if (clipPending)
                clipAt = ReanchoredDeadline(clipAt, oldImpact, actualImpact);
            else if (animator != null)
                animator.speed = Mathf.Clamp(
                    RetimedSpeed(animator.speed, oldImpact, actualImpact, Time.time),
                    minClipSpeed, maxClipSpeed);
            impactAt = actualImpact;
        }

        protected override void AfterTravelCompensated()
        {
            if (!clapLiftOwned || lungeRoot == null) return;
            if (Time.time > clapClampUntil) { clapLiftOwned = false; return; }
            Vector3 p = lungeRoot.localPosition;
            p.y = clapGroundY + ClapLiftOffset(Time.time, clapLiftStartAt, clapLiftTopAt,
                                               clapDescending, clapStrikeAt, clapImpactAt,
                                               clapLiftHeight);
            lungeRoot.localPosition = p;
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
            PlayPresentationClip(jumpClip, 1f);
            ReserveAnimatorUntil(Time.time + Mathf.Max(0.05f, entranceHoldSeconds));
        }

        void CancelSpecialPresentation(bool emitDueClap, bool preserveLandedComboTail = false)
        {
            shoulderClipPending = false;
            clapClipPending = false;
            clapLiftOwned = false;
            entrancePending = false;
            activeAttack = null;
            if (emitDueClap && clapImpactPending && Time.time >= clapImpactAt) EmitClapImpact();
            else clapImpactPending = false;

            bool keepCombo = preserveLandedComboTail && Time.time >= comboImpactAt &&
                             Time.time < comboHoldUntil;
            if (!keepCombo)
            {
                comboTailReservePending = false;
                comboImpactAt = 0f;
                comboHoldUntil = 0f;
            }
        }

        void EmitClapImpact()
        {
            if (!clapImpactPending) return;
            clapImpactPending = false;
            // ClearTelegraph/Recoil may run before this component's Update/LateUpdate on the contact
            // frame. Ground the owned lift HERE, before the ring, so callback order cannot leave the
            // blast under a still-floating body. A following recoil then starts from this touchdown.
            if (lungeRoot != null)
                lungeRoot.localPosition = ClampToGroundY(lungeRoot.localPosition, clapGroundY);
            Vector3 at = transform.root.position + Vector3.up * 0.08f;
            SlashFx.Ring(at, Vector3.up, clapHue, clapRingRadius, clapRingSeconds);
            SlashFx.Sparks(at, Vector3.up, clapHue, clapSparkCount, clapSparkSpeed, clapSparkSpread);
        }
    }
}
