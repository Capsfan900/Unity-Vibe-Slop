using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// All enemy presentation: emissive telegraphs, the arm rig, lunge, recoil, slump and death.
    /// <para>
    /// <b>This is the only class permitted to know about meshes, renderers or animation.</b>
    /// <see cref="EnemyController"/> owns the fight and must never reference a <see cref="Renderer"/> —
    /// it only calls the presentation methods below. That separation is what makes the art swappable.
    /// </para>
    /// <para>
    /// To move to real models, <b>subclass this and override the presentation methods</b> rather than
    /// editing them in place. <see cref="EnemyController"/> resolves its reference with
    /// <c>GetComponentInChildren&lt;EnemyVisuals&gt;()</c>, so a subclass is picked up automatically and no
    /// gameplay code changes. Keep this primitive implementation working — it is the fallback and the
    /// reference for what the timing is supposed to look like.
    /// </para>
    /// </summary>
    public class EnemyVisuals : MonoBehaviour, IEnemyPresentation
    {
        #region Model-swap contract
        // Three rules that MUST survive swapping primitives for imported models. Breaking any of them
        // breaks the parry, which is the entire game.
        //
        // 1. TIMING IS DATA-DRIVEN, NEVER ANIMATION-DRIVEN.
        //    Telegraph(atk, seconds) is handed its duration. The visual must FIT that duration; never let
        //    a clip's length decide the wind-up. EnemyController schedules the cue cueLead (0.28s) before
        //    impact from the attack DATA. If clip length ever dictates the wind-up, the guarantee that
        //    every attack is reactable and parryable is gone.
        //
        // 2. THE CUE IS THE LOUDEST EVENT.
        //    Whatever the art, CueFlash must stay an instant, high-contrast, unmistakable change that
        //    lands in a single frame. It is the "press parry NOW" signal. No easing, no blending, and
        //    nothing else in the frame may out-shout it.
        //
        // 3. PHYSICS COMES FROM THE PREFAB ROOT, NEVER THE MODEL.
        //    Colliders, NavMeshAgent radius/height and EnemyController.data.scale belong to the prefab
        //    root. Parent imported art under the existing "Visual" child so the root's physics footprint
        //    is unchanged — otherwise navigation and the distance/cone impact test shift with the art.
        #endregion

        public Renderer body;
        public Renderer eye;
        public Renderer weapon;
        public Transform lungeRoot;
        public GameObject alertMarker;

        [Header("Arm rig (shoulder pivot; the weapon hangs off it)")]
        [Tooltip("Rotated through the three telegraph beats so the wind-up reads from the silhouette, not just colour.")]
        public Transform armPivot;
        [Tooltip("Optional forearm/hand pivot, given a secondary lag so the swing whips rather than rotating rigidly.")]
        public Transform weaponPivot;

        // ENEMIES DO NOT GLOW. The telegraph is carried by SILHOUETTE (the arm rig) and AUDIO
        // (Sfx.ParryCue), punctuated at the cue by a world-space spark and a hard snap of the enemy's
        // BASE colour. The single exception is a successful parry: Recoil() is the one and only moment
        // an enemy emits light, which makes a deflect unmistakable and makes light itself mean
        // "you did that", not "something is happening".
        static readonly Color CueTint = new Color(0.82f, 0.80f, 0.76f);   // base-colour snap (NOT emission)
        static readonly Color CueTintUnblockable = new Color(0.75f, 0.10f, 0.12f);
        static readonly Color CueSpark = new Color(1f, 0.93f, 0.78f);     // world FX, additive, not on the enemy
        static readonly Color CueSparkUnblockable = new Color(1f, 0.12f, 0.18f);
        static readonly Color ParryGlow = new Color(0.78f, 0.88f, 1f) * 3.2f; // the ONLY enemy emission
        static readonly Color StaggerTint = new Color(0.42f, 0.32f, 0.24f);   // base-colour pulse, still no glow
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        EmissiveFlash flash;
        MaterialPropertyBlock eyeMpb;
        Coroutine motion;
        Vector3 lungeBase;
        Quaternion lungeBaseRot;
        bool slumped;

        // --- body appearance, written by THIS class only -------------------------------------------
        // EmissiveFlash replaces the whole property block when it writes, which would wipe _BaseColor.
        // Rather than fight it, EnemyVisuals takes ownership of both channels and EmissiveFlash is given
        // no renderers. ONE WRITER PER MATERIAL CHANNEL (see docs/ENGINEERING-LOG.md).
        MaterialPropertyBlock bodyMpb;
        Renderer[] bodyRenderers = new Renderer[0];
        Color bodyBase = Color.black;      // the enemy's resting base colour, from EnemyData
        Color accent = Color.white;        // boss phase hue; tints the parry glow only

        float tintBoost;                   // 0..1 lightening of the base colour (the cue / hit pop)
        Color tintTarget = Color.white;
        float glowAmount;                  // 0..1 emission, ONLY driven by a successful parry
        Color glowColor = Color.white;
        float chargeDark;                  // 0..1 darkening during a wind-up ("inhale")

        Quaternion armBaseRot = Quaternion.identity;
        Quaternion weaponBaseRot = Quaternion.identity;
        ArmPose currentPose;

        /// <summary>
        /// Set by CueFlash so the still-running wind-up coroutine stops lerping and HOLDS at the peak.
        /// Without this the cue's silhouette hitch was overwritten on the very next frame — the cue fires
        /// cueLead (0.28s) before impact, which is typically well before the wind-up ends, so WindupCo was
        /// still driving the arm and the punctuation was invisible.
        /// </summary>
        bool cuePeak;

        /// <summary>Shoulder angles for one attack: where the arm rears to, and where it swings through to.</summary>
        struct ArmPose
        {
            public Vector3 windup;
            public Vector3 strike;
            public ArmPose(Vector3 w, Vector3 s) { windup = w; strike = s; }
        }

        // Read off the attack's own shape so no new data fields are needed:
        //   wide cone  -> a horizontal sweep, arm winds across the body
        //   narrow cone-> a thrust, arm cocks straight back
        //   otherwise  -> an overhead, arm rears high (the biggest, slowest read)
        static readonly ArmPose PoseOverhead = new ArmPose(new Vector3(-136f, 0f, -26f), new Vector3(64f, 0f, 16f));
        static readonly ArmPose PoseSweep = new ArmPose(new Vector3(-24f, -106f, -44f), new Vector3(-8f, 88f, 32f));
        static readonly ArmPose PoseThrust = new ArmPose(new Vector3(-58f, -20f, 0f), new Vector3(20f, 8f, 0f));

        /// <summary>
        /// Virtual and protected on purpose, matching <see cref="EnemyController"/>. A private Unity
        /// message would be *hidden* by a subclass declaring its own <c>Awake</c> — Unity would then call
        /// only the derived one and silently skip this initialisation. Overrides must call
        /// <c>base.Awake()</c>.
        /// </summary>
        protected virtual void Awake()
        {
            flash = GetComponent<EmissiveFlash>();
            if (flash == null) flash = gameObject.AddComponent<EmissiveFlash>();
            eyeMpb = new MaterialPropertyBlock();
            bodyMpb = new MaterialPropertyBlock();
            if (lungeRoot != null) { lungeBase = lungeRoot.localPosition; lungeBaseRot = lungeRoot.localRotation; }
            if (armPivot != null) armBaseRot = armPivot.localRotation;
            if (weaponPivot != null) weaponBaseRot = weaponPivot.localRotation;
            currentPose = PoseOverhead;
            if (alertMarker != null) alertMarker.SetActive(false);
        }

        static ArmPose PoseFor(EnemyAttackData atk)
        {
            if (atk == null) return PoseOverhead;
            if (atk.coneDeg >= 90f) return PoseSweep;
            if (atk.coneDeg <= 45f) return PoseThrust;
            return PoseOverhead;
        }

        /// <summary>Last arm pose we applied. Tracked so transitions can start from wherever the arm
        /// actually is — reading it back off the transform gives wrapped eulerAngles and 300-degree spins.</summary>
        Vector3 lastArmEuler;

        /// <summary>
        /// Primitive-specific: drives the shoulder/hand pivots directly. A model subclass with a rigged
        /// mesh would override the presentation methods above and never call this, rather than trying to
        /// reuse it against an Animator-controlled skeleton.
        /// </summary>
        void SetArm(Vector3 euler, float weaponLag)
        {
            lastArmEuler = euler;
            if (armPivot != null) armPivot.localRotation = armBaseRot * Quaternion.Euler(euler);
            // the hand trails the shoulder slightly, so the blade whips instead of swinging rigidly
            if (weaponPivot != null) weaponPivot.localRotation = weaponBaseRot * Quaternion.Euler(euler * weaponLag);
        }

        /// <summary>Bind the enemy's palette. Primitive-specific: a model subclass overrides this whole
        /// method (its materials will not be the generated <c>M_*</c> set) rather than extending it.</summary>
        public virtual void Setup(EnemyData d)
        {
            var list = new System.Collections.Generic.List<Renderer>();
            if (body) list.Add(body);
            if (weapon) list.Add(weapon);
            bodyRenderers = list.ToArray();

            // EmissiveFlash is deliberately given NOTHING. It writes a fresh property block (no
            // GetPropertyBlock first), so any write of ours to _BaseColor would be wiped the next time
            // it animated. This class is now the sole writer for the body renderers.
            flash.SetRenderers(new Renderer[0]);

            bodyBase = d.bodyColor;
            accent = d.emission.maxColorComponent > 0.001f
                ? d.emission / d.emission.maxColorComponent
                : Color.white;

            // Resting state: base colour only, ZERO emission. An enemy is a silhouette until it is parried.
            tintBoost = 0f; glowAmount = 0f; chargeDark = 0f;
            WriteBody();
        }

        /// <summary>Boss phase recolour. Emission-based, so a model subclass overrides it.</summary>
        public virtual void SetAccent(Color emission)
        {
            // Boss phases change the colour of the PARRY glow and a faint base tint — they no longer
            // make the boss self-illuminate.
            accent = emission.maxColorComponent > 0.001f ? emission / emission.maxColorComponent : Color.white;
            bodyBase = Color.Lerp(bodyBase, accent * 0.10f, 0.5f);
            WriteBody();
        }

        /// <summary>Posture tell on the eye. Primitive-specific (`_EmissionColor` via
        /// <see cref="MaterialPropertyBlock"/>) — a model subclass overrides this.</summary>
        public virtual void SetPostureRatio(float r)
        {
            if (eye == null) return;
            // The one permitted always-on emissive on an enemy, and deliberately tiny: enough to locate
            // a body in a dark room, far too weak to light it. Posture still reads here, just quietly.
            Color c = Color.Lerp(new Color(0.35f, 0.10f, 0.05f), new Color(1f, 0.45f, 0.12f) * 1.4f, r);
            eyeMpb.SetColor(EmissionId, c);
            eye.SetPropertyBlock(eyeMpb);
        }

        /// <summary>
        /// Beat 1: the charge. Deliberately DIM — the enemy darkens as it winds up (an "inhale") so that
        /// the CueFlash snap reads as a hard, unmistakable state change rather than the end of a ramp.
        /// <para><b>Contract rule 1:</b> <paramref name="seconds"/> is authoritative. An override must fit
        /// its animation to this duration, never the reverse.</para>
        /// </summary>
        public virtual void Telegraph(EnemyAttackData atk, float seconds)
        {
            bool unblockable = atk != null && atk.unblockable;
            currentPose = PoseFor(atk);
            cuePeak = false;
            // No emission. The charge is a DARKENING: the enemy sinks into shadow as it winds up, so the
            // cue's snap to a pale base colour lands as a hard state change against it. The real
            // telegraph is the arm, which WindupCo rears to full extension and then holds.
            chargeDark = 1f;
            tintBoost = 0f;
            if (alertMarker != null) alertMarker.SetActive(unblockable);
            StartMotion(WindupCo(seconds));
        }

        /// <summary>
        /// Beat 2: the cue. Instant, un-eased snap to white-hot (or blood red when unblockable).
        /// This is the "press parry NOW" signal — it must land in a single frame to be readable.
        /// <para><b>Contract rule 2:</b> an override must stay instant and high-contrast. This is the one
        /// beat that may never be softened, eased or blended.</para>
        /// </summary>
        public virtual void CueFlash(bool unblockable)
        {
            // The cue is now THREE non-emissive channels landing on the same frame:
            //   1. a hard snap of the BASE colour (dark to pale), instant, no easing
            //   2. a world-space spark at the weapon. SlashFx is additive scene FX, not enemy emission
            //   3. the arm hitching past full extension and FREEZING there
            // Plus Sfx.ParryCue from EnemyController. None of it makes the enemy a lamp.
            chargeDark = 0f;
            tintBoost = 1f;
            tintTarget = unblockable ? CueTintUnblockable : CueTint;
            WriteBody();                                            // land the snap this frame, un-eased

            Vector3 cueAt = WeaponPoint();
            Color cueSpark = unblockable ? CueSparkUnblockable : CueSpark;
            SlashFx.Flare(cueAt, cueSpark, unblockable ? 0.42f : 0.30f, 0.11f);
            SlashFx.Sparks(cueAt, ArmDirection(), cueSpark, unblockable ? 7 : 5, 5.5f, 38f);

            if (alertMarker != null) alertMarker.SetActive(unblockable);
            cuePeak = true;
            SetArm(currentPose.windup * 1.12f, 0.45f);
        }

        /// <summary>Beat 3: the swing itself. <paramref name="seconds"/> is authoritative (contract rule 1).</summary>
        public virtual void Strike(float lunge, float seconds)
        {
            cuePeak = false;                                   // release the held peak into the swing
            // The swing is pure motion. Flashing here competed with the cue and made
            // "the attack is coming" and "the attack is landing" look identical.
            chargeDark = 0f;
            if (alertMarker != null) alertMarker.SetActive(false);
            StartMotion(LungeCo(lunge, seconds));
        }

        /// <summary>Abandon an in-flight telegraph (combo ended, parried, staggered).</summary>
        public virtual void ClearTelegraph()
        {
            cuePeak = false;
            chargeDark = 0f;
            if (alertMarker != null) alertMarker.SetActive(false);
        }

        /// <summary>The enemy is knocked back by a perfect parry.</summary>
        public virtual void Recoil()
        {
            cuePeak = false;
            chargeDark = 0f;
            // THE one moment an enemy emits light in this entire game. Nothing else on an enemy glows,
            // so this reads instantly as "you deflected that" rather than as ambient noise.
            glowColor = ParryGlow * Color.Lerp(Color.white, accent, 0.25f);
            glowAmount = 1f;
            tintBoost = 1f;
            tintTarget = Color.white;
            WriteBody();

            Vector3 parryAt = WeaponPoint();
            SlashFx.Flare(parryAt, ParryGlow, 0.5f, 0.16f);
            SlashFx.Sparks(parryAt, -ArmDirection(), ParryGlow, 10, 8f, 44f);
            if (alertMarker != null) alertMarker.SetActive(false);
            StartMotion(LungeCo(-0.5f, 0.25f));
        }

        /// <summary>Took damage. Suppressed while slumped so the stagger pulse keeps reading.</summary>
        public virtual void HitFlash()
        {
            if (slumped) return;
            // A pale pop of the BASE colour, not a light. Damage should be felt without competing
            // with the parry glow, which is the only thing allowed to be bright.
            tintBoost = Mathf.Max(tintBoost, 0.7f);
            tintTarget = Color.white;
        }

        /// <summary>Posture broken / recovered. The slumped pose is the "execute me now" tell.</summary>
        public virtual void Slump(bool on)
        {
            slumped = on;
            if (on)
            {
                // Staggered reads through POSE (the guard drops) plus a slow base-colour breath
                // applied in Update. Not a glow: the HUD deathblow prompt is the bright signal.
                if (alertMarker != null) alertMarker.SetActive(false);
                StartMotion(TiltCo(28f, 0.2f));
            }
            else
            {
                StartMotion(TiltCo(0f, 0.2f));
            }
        }

        /// <summary>Boss activation / phase change.</summary>
        public virtual void Roar()
        {
            // Boss activation: a shockwave of world sparks and a base-colour surge. Big, but the boss
            // still does not self-illuminate.
            tintBoost = 1f;
            tintTarget = Color.Lerp(Color.white, accent, 0.5f);
            Vector3 roarAt = transform.position + Vector3.up * 1.2f;
            SlashFx.Ring(roarAt, Vector3.up, accent, 3.2f, 0.5f);
            SlashFx.Sparks(roarAt, Vector3.up, accent, 12, 7f, 70f);
            StartMotion(RoarCo());
        }

        /// <summary>Death. Primitive-specific (scales the transform to nothing) — a model subclass
        /// overrides this with a ragdoll or a death clip.</summary>
        public virtual void Die()
        {
            glowAmount = 0f; tintBoost = 0f; chargeDark = 0f;
            if (alertMarker != null) alertMarker.SetActive(false);
            StartMotion(DieCo());
        }

        /// <summary>
        /// Single motion slot: starting a new one cancels the previous. Subclasses should route their own
        /// coroutines through this rather than calling <c>StartCoroutine</c> directly — two routines writing
        /// the same transform is exactly the bug that made the cue hitch invisible (see ENGINEERING-LOG).
        /// </summary>
        protected void StartMotion(IEnumerator co)
        {
            if (motion != null) StopCoroutine(motion);
            motion = StartCoroutine(co);
        }

        /// <summary>
        /// Beat 1 motion: the body leans back and the arm rears to full extension, easing OUT so the arm
        /// arrives early and visibly hangs there — the pause before a strike is what makes it readable.
        /// Scaled time, so hitstop freezes the wind-up like everything else.
        /// </summary>
        IEnumerator WindupCo(float seconds)
        {
            float t = 0f;
            Vector3 armFrom = Vector3.zero;

            // Anticipation: before rearing back, the body settles slightly DOWN and forward. Classic
            // animation principle — the small opposite motion is what makes the big one land, and it
            // gives the player an extra beat of "something is coming" before the charge even starts.
            float anticipation = Mathf.Min(0.12f, seconds * 0.18f);
            while (t < anticipation)
            {
                float k = anticipation > 0f ? t / anticipation : 1f;
                float dip = Mathf.Sin(k * Mathf.PI);             // down and back up over the sub-beat
                if (lungeRoot != null)
                {
                    lungeRoot.localPosition = lungeBase + Vector3.down * 0.14f * dip + Vector3.forward * 0.09f * dip;
                    lungeRoot.localRotation = lungeBaseRot * Quaternion.Euler(6f * dip, 0f, 0f);
                }
                SetArm(Vector3.Lerp(Vector3.zero, -currentPose.windup * 0.12f, dip), 0.45f);
                t += Time.deltaTime; yield return null;
            }

            // Charge: rear to full extension, easing OUT so the arm arrives early and visibly hangs.
            float rear = Mathf.Max(0.01f, seconds - anticipation);
            t = 0f;
            while (t < rear)
            {
                // Once the cue has fired, freeze here and hold the peak. Deliberately does not advance t:
                // the hold is ended by the next StartMotion (Strike / Recoil / Settle / Slump), which stops
                // this coroutine outright. Not a hang.
                if (cuePeak) { yield return null; continue; }

                float k = t / rear;
                float eased = 1f - (1f - k) * (1f - k);
                if (lungeRoot != null)
                {
                    lungeRoot.localPosition = lungeBase + Vector3.back * 0.25f * k + Vector3.up * 0.15f * k;
                    lungeRoot.localRotation = lungeBaseRot * Quaternion.Euler(-12f * k, 0f, 0f);
                }
                SetArm(Vector3.Lerp(armFrom, currentPose.windup, eased), 0.45f);
                t += Time.deltaTime; yield return null;
            }
            if (!cuePeak) SetArm(currentPose.windup, 0.45f);
        }

        /// <summary>
        /// The breath between combos: the enemy exhales and resettles to a neutral guard. This is the
        /// player's visible window to act, so it must READ — an enemy that snaps back to rest instantly
        /// looks like it is already winding up again.
        /// <para><b>Contract rule 1:</b> <paramref name="seconds"/> is authoritative.</para>
        /// </summary>
        public virtual void Settle(float seconds)
        {
            cuePeak = false;
            StartMotion(SettleCo(Mathf.Max(0.12f, seconds)));
        }

        IEnumerator SettleCo(float seconds)
        {
            float t = 0f;
            Vector3 armFrom = lastArmEuler;
            Vector3 fromPos = lungeRoot != null ? lungeRoot.localPosition : Vector3.zero;
            Quaternion fromRot = lungeRoot != null ? lungeRoot.localRotation : Quaternion.identity;

            while (t < seconds)
            {
                float k = t / seconds;
                float eased = 1f - (1f - k) * (1f - k);
                // A shallow exhale on the way back to neutral, so the pause has some life in it.
                float breath = Mathf.Sin(k * Mathf.PI) * 0.05f;
                if (lungeRoot != null)
                {
                    lungeRoot.localPosition = Vector3.Lerp(fromPos, lungeBase + Vector3.down * breath, eased);
                    lungeRoot.localRotation = Quaternion.Slerp(fromRot, lungeBaseRot, eased);
                }
                SetArm(Vector3.Lerp(armFrom, Vector3.zero, eased), 0.4f);
                t += Time.deltaTime; yield return null;
            }
            if (lungeRoot != null) { lungeRoot.localPosition = lungeBase; lungeRoot.localRotation = lungeBaseRot; }
            SetArm(Vector3.zero, 0f);
        }

        /// <summary>
        /// Beat 3 motion (also reused for the parry recoil, with a negative distance): the arm whips
        /// through the strike pose fast, then settles back to rest.
        /// </summary>
        IEnumerator LungeCo(float dist, float seconds)
        {
            bool recoiling = dist < 0f;
            float t = 0f;
            Vector3 from = lungeRoot != null ? lungeRoot.localPosition : Vector3.zero;
            Quaternion fromRot = lungeRoot != null ? lungeRoot.localRotation : Quaternion.identity;
            Vector3 armFrom = lastArmEuler;   // continue from the held cue peak, not an assumed pose
            // A deflect knocks the arm back PAST neutral; a strike carries it through to follow-through.
            Vector3 armTo = recoiling ? -currentPose.windup * 0.35f : currentPose.strike;

            float half = seconds * 0.4f;
            while (t < half)
            {
                float k = half > 0f ? t / half : 1f;
                float eased = k * k;                              // ease-in: the swing accelerates
                if (lungeRoot != null)
                {
                    lungeRoot.localPosition = Vector3.Lerp(from, lungeBase + Vector3.forward * dist, k);
                    lungeRoot.localRotation = Quaternion.Slerp(fromRot, lungeBaseRot * Quaternion.Euler(18f * Mathf.Sign(dist), 0f, 0f), k);
                }
                SetArm(Vector3.Lerp(armFrom, armTo, eased), 0.55f);
                t += Time.deltaTime; yield return null;
            }

            t = 0f;
            float back = Mathf.Max(0.05f, seconds - half);
            if (lungeRoot != null) { from = lungeRoot.localPosition; fromRot = lungeRoot.localRotation; }
            while (t < back)
            {
                float k = t / back;
                if (lungeRoot != null)
                {
                    lungeRoot.localPosition = Vector3.Lerp(from, lungeBase, k);
                    lungeRoot.localRotation = Quaternion.Slerp(fromRot, slumped ? lungeBaseRot * Quaternion.Euler(28f, 0f, 0f) : lungeBaseRot, k);
                }
                SetArm(Vector3.Lerp(armTo, Vector3.zero, k), 0.45f);
                t += Time.deltaTime; yield return null;
            }
            SetArm(Vector3.zero, 0f);
        }

        IEnumerator TiltCo(float angle, float seconds)
        {
            if (lungeRoot == null) yield break;
            float t = 0f;
            Quaternion from = lungeRoot.localRotation;
            Vector3 fromPos = lungeRoot.localPosition;
            Quaternion to = lungeBaseRot * Quaternion.Euler(angle, 0f, 0f);
            // Staggered: the guard drops and the weapon arm hangs — the "execute me" silhouette.
            Vector3 armTo = slumped ? new Vector3(38f, 0f, 22f) : Vector3.zero;
            while (t < seconds)
            {
                float k = t / seconds;
                lungeRoot.localRotation = Quaternion.Slerp(from, to, k);
                lungeRoot.localPosition = Vector3.Lerp(fromPos, lungeBase, k);
                SetArm(Vector3.Lerp(Vector3.zero, armTo, k), 0.6f);
                t += Time.unscaledDeltaTime; yield return null;
            }
            lungeRoot.localRotation = to;
            SetArm(armTo, 0.6f);
        }

        IEnumerator RoarCo()
        {
            if (lungeRoot == null) yield break;
            float t = 0f;
            while (t < 1.2f)
            {
                float k = Mathf.Sin(t * 20f) * 0.06f * (1f - t / 1.2f);
                lungeRoot.localPosition = lungeBase + new Vector3(k, Mathf.Abs(k) * 2f, 0f);
                lungeRoot.localRotation = lungeBaseRot * Quaternion.Euler(-10f * (1f - t / 1.2f), 0f, k * 60f);
                t += Time.unscaledDeltaTime; yield return null;
            }
            lungeRoot.localPosition = lungeBase;
            lungeRoot.localRotation = lungeBaseRot;
        }

        IEnumerator DieCo()
        {
            float t = 0f;
            Vector3 s0 = transform.localScale;
            Quaternion r0 = lungeRoot != null ? lungeRoot.localRotation : Quaternion.identity;
            while (t < 0.6f)
            {
                float k = t / 0.6f;
                if (lungeRoot != null) lungeRoot.localRotation = Quaternion.Slerp(r0, lungeBaseRot * Quaternion.Euler(85f, 0f, 20f), k);
                transform.localScale = s0 * Mathf.Lerp(1f, 0.05f, k * k);
                t += Time.unscaledDeltaTime; yield return null;
            }
        }
        // ---------------------------------------------------------------- body appearance

        /// <summary>
        /// World point to originate weapon FX from. Prefers the actual weapon renderer so sparks appear
        /// on the blade rather than in the enemy's chest.
        /// </summary>
        protected Vector3 WeaponPoint()
        {
            if (weapon != null) return weapon.bounds.center;
            if (weaponPivot != null) return weaponPivot.position;
            if (armPivot != null) return armPivot.position;
            return transform.position + Vector3.up * 1.2f;
        }

        /// <summary>Roughly where the weapon is swinging, so cue sparks throw the right way.</summary>
        protected Vector3 ArmDirection()
        {
            Transform t = weaponPivot != null ? weaponPivot : (armPivot != null ? armPivot : transform);
            Vector3 d = t.forward + Vector3.up * 0.35f;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.up;
        }

        /// <summary>
        /// The single writer for the body renderers' _BaseColor and _EmissionColor.
        /// Emission is zero except immediately after a parry, which is the whole point of this pass:
        /// light on an enemy means "you deflected", never "an attack is happening".
        /// </summary>
        protected void WriteBody()
        {
            if (bodyRenderers == null || bodyRenderers.Length == 0) return;
            if (bodyMpb == null) bodyMpb = new MaterialPropertyBlock();

            // Wind-up sinks toward black; the cue and hits lift toward the pale tint.
            Color baseCol = Color.Lerp(bodyBase, bodyBase * 0.45f, chargeDark);
            if (tintBoost > 0f) baseCol = Color.Lerp(baseCol, tintTarget, tintBoost);
            if (slumped)
            {
                // Slow breath while staggered, still entirely in the base channel.
                float breath = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f);
                baseCol = Color.Lerp(baseCol, StaggerTint, 0.35f + 0.25f * breath);
            }

            bodyMpb.SetColor(BaseColorId, baseCol);
            bodyMpb.SetColor(EmissionId, glowAmount > 0f ? glowColor * glowAmount : Color.black);
            for (int i = 0; i < bodyRenderers.Length; i++)
                if (bodyRenderers[i] != null) bodyRenderers[i].SetPropertyBlock(bodyMpb);
        }

        /// <summary>
        /// Decays the cue tint and the parry glow. Unscaled so hitstop never stalls feedback, and cheap:
        /// it early-outs to a single write once everything has settled.
        /// </summary>
        protected virtual void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool dirty = false;

            if (tintBoost > 0f) { tintBoost = Mathf.Max(0f, tintBoost - dt * 5.5f); dirty = true; }
            if (glowAmount > 0f) { glowAmount = Mathf.Max(0f, glowAmount - dt * 3.2f); dirty = true; }
            if (slumped) dirty = true;   // the stagger breath animates continuously

            if (dirty) WriteBody();
        }

    }
}
