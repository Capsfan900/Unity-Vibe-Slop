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
        [Tooltip("The Sekiro deathblow glyph, raised while this enemy's posture is broken. NEVER the " +
                 "same object or material as alertMarker: they occupy the same place on screen and mean " +
                 "opposite things. Built by PrefabFactory / MiniBossFactory from M_DeathblowMark.")]
        public GameObject deathblowMarker;

        [Header("Arm rig (shoulder pivot; the weapon hangs off it)")]
        [Tooltip("Rotated through the three telegraph beats so the wind-up reads from the silhouette, not just colour.")]
        public Transform armPivot;
        [Tooltip("Optional forearm/hand pivot, given a secondary lag so the swing whips rather than rotating rigidly.")]
        public Transform weaponPivot;

        // ENEMIES DO NOT GLOW BY DEFAULT. The telegraph is carried by SILHOUETTE (the arm rig) and
        // AUDIO (Sfx.ParryCue), punctuated at the cue by a world-space spark and a hard snap of the
        // enemy's BASE colour. Emission is reserved so that light on an enemy means "you deflected",
        // never "an attack is happening".
        //
        // THREE exceptions, and they are different in kind:
        //   1. A successful parry (Recoil) — a bright SPIKE. Still the loudest light in the fight.
        //   2. A burning enemy (EmberAura, via SetAura) — a dim, CONSTANT floor, and one that is
        //      modulated by chargeDark like everything else, so a body on fire still visibly inhales
        //      on a wind-up. A floor and a spike coexist without either becoming ambiguous; a floor
        //      as bright as the spike would have destroyed the deflect read, which is why the aura
        //      ships well under it. Anything that wants an enemy to glow goes through SetAura.
        //   3. The Sentry GHOST (SentryGhostVisual, also via SetAura) — 2026-09-06, user-directed.
        //      Same channel, same modulation, same discipline: its floor peaks at 0.28 and its whole
        //      body — lit albedo + floor + all four mist wisps — is budgeted to 1.03, under the 1.05
        //      bloom threshold. So the rule is no longer "an enemy never glows"; it is "an enemy is
        //      never allowed to BLOOM". Only a deflect (3.2) and the authored tells cross that line,
        //      and that is what keeps light-on-a-body meaning what it has always meant.
        static readonly Color CueTint = new Color(0.82f, 0.80f, 0.76f);   // base-colour snap (NOT emission)
        static readonly Color CueTintUnblockable = new Color(0.75f, 0.10f, 0.12f);
        static readonly Color CueSpark = new Color(1f, 0.93f, 0.78f);     // world FX, additive, not on the enemy
        static readonly Color CueSparkUnblockable = new Color(1f, 0.12f, 0.18f);
        // THE ONLY ENEMY EMISSION, and it went WARM in the 2026-09-06 cold pass. It used to be a
        // blue-white (0.78, 0.88, 1.0) chosen against a blood-red world. Under a cold blue world a
        // blue-white flash at 3.2 reads as "the world got brighter for a frame"; a bone-white one
        // with a warm edge reads as STEEL STRUCK STEEL, which is what a deflect is. Same peak (3.2 —
        // not one photon added), same duration, opposite temperature, and it is now the complement
        // of everything around it. This is the same reason Sekiro's deflect spark is a white-hot
        // burst rather than a tint of the environment.
        static readonly Color ParryGlow = new Color(1f, 0.92f, 0.80f) * 3.2f;
        static readonly Color StaggerTint = new Color(0.42f, 0.32f, 0.24f);   // base-colour pulse, still no glow

        // ---- the hit reaction scales with the blow, not with a fixed pop ---------------------------
        // HitFlash() used to be one constant (tintBoost 0.7, decayRate 5.5/s regardless of damage), so an
        // 88-damage Verdigris finisher and a 9-damage dagger jab produced the identical pop and the
        // player could not tell a heavy landing from a chip. The reaction now scales off damage AS A
        // FRACTION of the victim's OWN max health (never a raw number -- 20 damage means something
        // different to a grunt and to a 2.2x boss), read straight off Health.OnDamaged so no other file
        // has to change (EnemyController and IEnemyPresentation are owned by another lane this pass).
        // Two axes, per section 1 "impact is a pose, a pause and a sound": INTENSITY (how far the base
        // colour lerps toward white -- lerp already clamps at 1, so this is the only channel available)
        // and DURATION (how long it lingers, via the decay rate -- a heavy hit gets to be a pause, not a
        // flicker). The floor keeps a chip flash intense enough that "a hit that reads as nothing" never
        // happens (matches the old constant exactly at fraction 0, so a block's un-scaled HitFlash() is
        // unchanged); the ceiling is reached at HitFlashFractionCeiling of max HP, past which more damage
        // buys no more pop -- the same "a chip against a boss should not be silent" and "a one-shot
        // against a grunt should not need to be brighter than white" argument in both directions.
        public const float HitFlashFloorBoost = 0.70f;
        public const float HitFlashCeilingBoost = 1.00f;
        public const float HitFlashFloorDecay = 5.50f;     // 1/s -- a chip clears in ~0.13 s
        public const float HitFlashCeilingDecay = 2.00f;   // 1/s -- a finisher-scale hit lingers ~0.50 s
        public const float HitFlashFractionFloor = 0.02f;  // at/under 2% of max HP: floor reaction
        public const float HitFlashFractionCeiling = 0.30f; // at/over 30% of max HP: ceiling reaction

        /// <summary>
        /// Pure and static so it is unit-testable without an instance, matching the pattern
        /// <see cref="PuppetVisuals.ResolveRate(float,float,float)"/> already sets for this project.
        /// </summary>
        public static void HitReactionFor(float damageFraction, out float boost, out float decayRate)
        {
            float t = Mathf.Clamp01(Mathf.InverseLerp(HitFlashFractionFloor, HitFlashFractionCeiling,
                Mathf.Max(0f, damageFraction)));
            boost = Mathf.Lerp(HitFlashFloorBoost, HitFlashCeilingBoost, t);
            decayRate = Mathf.Lerp(HitFlashFloorDecay, HitFlashCeilingDecay, t);
        }

        // ---- the posture-break pose ---------------------------------------------------------------
        // A broken posture BUCKLES. It leans back off the front foot, sinks as the knees give, and
        // rolls; the arms fling open. Every component is signed so that NOTHING travels toward the
        // player: the deathblow step-in parks the camera ~1.7 m off the body surface and a pose that
        // moves even half a metre forward puts geometry through the lens. See Slump().
        static readonly Vector3 StaggerEuler = new Vector3(-13f, 0f, 9f);     // lean BACK, roll
        static readonly Vector3 StaggerSag = new Vector3(0f, -0.20f, -0.14f); // knees give, weight back
        static readonly Vector3 StaggerArm = new Vector3(-26f, 0f, 34f);      // arm back and OUT, chest open
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
        float tintDecayRate = HitFlashFloorDecay;  // 1/s, set per hit so a heavy blow lingers longer
        Color tintTarget = Color.white;
        /// <summary>The victim's own <see cref="Health"/>, found up the hierarchy (this component lives on
        /// a "Visual" child, never the root). Used ONLY to read <see cref="Health.Max"/> for the hit-flash
        /// fraction; nothing here writes to it. Null-safe throughout -- a presentation with no Health
        /// sibling (a test double, a non-combat prop) just keeps the old fixed-pop behaviour.</summary>
        Health hpRef;
        float glowAmount;                  // 0..1 emission, ONLY driven by a successful parry
        Color glowColor = Color.white;
        float chargeDark;                  // 0..1 darkening during a wind-up ("inhale")

        /// <summary>
        /// A CONSTANT emission floor under the parry glow, for enemies that are lit from inside —
        /// see <see cref="EmberAura"/>. Set through <see cref="SetAura"/> and never written directly,
        /// because <see cref="WriteBody"/> is the single writer of these renderers' channels and this
        /// project has twice lost a feature to a second writer on one material channel.
        /// </summary>
        Color auraColor = Color.black;
        float auraAmount;

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

        /// <summary>
        /// The full anticipation SHAPE for one attack: the shoulder angles it rears to and swings through
        /// to, plus the whole-body offset and rotation that carry the silhouette. The body channel matters
        /// more than the arm at fighting distance — a blocky enemy 3-4.5 m away is mostly torso.
        /// </summary>
        struct ArmPose
        {
            public Vector3 windup;      // shoulder Euler at the peak
            public Vector3 strike;      // shoulder Euler the swing carries through to
            public Vector3 bodyOffset;  // LungeRoot local offset at the peak, metres
            public Vector3 bodyEuler;   // LungeRoot local Euler at the peak
            public float weaponLag;     // how much the hand trails the shoulder

            public ArmPose(Vector3 w, Vector3 s) : this(w, s, GenericBodyOffset, GenericBodyEuler, 0.45f) { }

            public ArmPose(Vector3 w, Vector3 s, Vector3 bo, Vector3 be, float lag)
            { windup = w; strike = s; bodyOffset = bo; bodyEuler = be; weaponLag = lag; }
        }

        // The generic body lean, used by every attack that does NOT author its own pose: the enemy rocks
        // back and up as it charges. Kept as constants so an authored pose is a deliberate DEPARTURE from
        // a known shape rather than a value invented against nothing.
        static readonly Vector3 GenericBodyOffset = new Vector3(0f, 0.15f, -0.25f);
        static readonly Vector3 GenericBodyEuler = new Vector3(-12f, 0f, 0f);

        // FALLBACK vocabulary, read off the attack's own shape so an unauthored attack still gets a pose:
        //   wide cone  -> a horizontal sweep, arm winds across the body
        //   narrow cone-> a thrust, arm cocks straight back
        //   otherwise  -> an overhead, arm rears high (the biggest, slowest read)
        // This is what gap 3.5 was about: three poses shared by 25+ attacks means Grunt_Jab and
        // Grunt_Heavy are the same shape. An attack that matters authors EnemyAttackData.windupPose
        // instead; these remain for the ones that do not.
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
            if (deathblowMarker != null) deathblowMarker.SetActive(false);

            // GetComponentInParent, not GetComponent: this class is instantiated on the "Visual" child
            // (see PrefabFactory), while Health sits on the prefab root next to EnemyController (it is a
            // RequireComponent there). Subscribed once here rather than in Setup(), which can re-run.
            hpRef = GetComponentInParent<Health>();
            if (hpRef != null) hpRef.OnDamaged += OnHealthDamaged;
        }

        protected virtual void OnDestroy()
        {
            if (hpRef != null) hpRef.OnDamaged -= OnHealthDamaged;
        }

        /// <summary>
        /// The scaled half of the hit reaction: reads the real blow off <see cref="Health.OnDamaged"/> so
        /// the fraction is exact, rather than trying to infer it from the parameterless
        /// <see cref="IEnemyPresentation.HitFlash"/> call <see cref="EnemyController"/> already makes on
        /// every hit (that call is left exactly as it was -- see contract note on <see cref="HitFlash"/>).
        /// </summary>
        void OnHealthDamaged(DamageInfo d)
        {
            if (d.damage <= 0f || hpRef == null || slumped) return;
            ApplyHitFlash(d.damage / Mathf.Max(1f, hpRef.Max));
        }

        void ApplyHitFlash(float damageFraction)
        {
            float boost, decay;
            HitReactionFor(damageFraction, out boost, out decay);
            tintBoost = Mathf.Max(tintBoost, boost);
            // MIN, not overwrite: EnemyController's plain HitFlash() (floor reaction) and this class's own
            // Health.OnDamaged subscription (the real fraction) fire on the SAME hit, and Unity gives no
            // guarantee which Awake ran first to decide subscription order. Taking the slower (smaller)
            // decay rate means whichever call carried the bigger hit always wins the LINGER, regardless of
            // which one happened to run second and would otherwise have stomped it back to the floor.
            tintDecayRate = Mathf.Min(tintDecayRate, decay);
            tintTarget = Color.white;
        }

        /// <summary>
        /// The pose this attack winds up into. An authored <see cref="WindupPose"/> on the asset wins;
        /// otherwise the cone-derived fallback plays, so no attack is ever without a wind-up and none of
        /// the 25+ existing assets had to be authored to ship per-attack silhouettes.
        /// </summary>
        static ArmPose PoseFor(EnemyAttackData atk)
        {
            if (atk == null) return PoseOverhead;
            WindupPose p = atk.windupPose;
            if (p != null && p.authored)
                return new ArmPose(p.armWindup, p.armStrike, p.bodyOffset, p.bodyEuler,
                                   Mathf.Clamp01(p.weaponLag));
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
            // P4 (combat plan 2026-09-06): a sentry's "open" has to read from across the span, so its
            // deathblow glyph is allowed a far cap of ~1.1 m instead of the duel's 0.42.
            if (deathblowMarker != null)
            {
                var mk = deathblowMarker.GetComponent<DeathblowMarker>();
                if (mk != null) mk.sentry = d != null && d.rangedOnly;
            }
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

        /// <summary>
        /// Set the constant emission floor for an enemy that burns from inside.
        ///
        /// <para>Asked for rather than written, exactly as <see cref="WeaponEmber"/> asks
        /// <see cref="EnergyGlow"/> for heat instead of poking the blade's emission: <see cref="WriteBody"/>
        /// owns <c>_EmissionColor</c> on these renderers and would overwrite an outside property block on
        /// the very next frame. ONE WRITER PER MATERIAL CHANNEL.</para>
        /// </summary>
        public void SetAura(Color color, float amount)
        {
            auraColor = color;
            auraAmount = Mathf.Max(0f, amount);
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
            // Near-break: the eye beats with the posture bar (EnemyPostureBar.NearBreakRatio). Toward a
            // hot white at the SAME peak (1.4) -- hue, not brightness, so the bloom budget is untouched.
            if (r >= EnemyPostureBar.NearBreakRatio && !slumped)
            {
                float k = Mathf.Clamp01((r - EnemyPostureBar.NearBreakRatio) / (1f - EnemyPostureBar.NearBreakRatio));
                float beat = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * EnemyPostureBar.NearBreakHz * Mathf.PI * 2f);
                c = Color.Lerp(c, new Color(1.4f, 1.3f, 1.15f), 0.6f * k * beat);
            }
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
            SetArm(currentPose.windup * 1.12f, currentPose.weaponLag);
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

        /// <summary>Cap on LungeRoot's forward pop for primitive bodies: a lean, never a second lunge.</summary>
        public const float MaxVisualLunge = 0.35f;

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
            // A small visual jar; the body's real step back is the brain's (EnemyController.OnParried, spec R4).
            StartMotion(LungeCo(-0.18f, 0.25f));
        }

        /// <summary>
        /// Took damage, OR was blocked (no damage). Suppressed while slumped so the stagger pulse keeps
        /// reading. Kept parameterless -- this is <see cref="IEnemyPresentation.HitFlash"/>'s contract and
        /// <see cref="EnemyController"/> calls it on both a block and a damaging hit, neither of which
        /// carries a fraction here.
        ///
        /// <para>The DAMAGE-SCALED reaction does not go through this method: <see cref="Awake"/>
        /// subscribes directly to <see cref="Health.OnDamaged"/> and calls <see cref="ApplyHitFlash"/>
        /// with the real damage/maxHP fraction, so a heavy blow lingers and a chip barely does (see the
        /// "hit reaction" block above). This call always applies the FLOOR reaction -- exactly the old
        /// fixed pop (tintBoost 0.7, decay 5.5/s) -- so a block (which never raises OnDamaged) still
        /// registers, and a damaging hit gets whichever of the two calls asked for more.</para>
        /// </summary>
        public virtual void HitFlash()
        {
            if (slumped) return;
            // A pale pop of the BASE colour, not a light. Damage should be felt without competing
            // with the parry glow, which is the only thing allowed to be bright.
            ApplyHitFlash(0f);
        }

        /// <summary>Posture broken / recovered. The slumped pose plus the deathblow glyph are the
        /// "kill this one now" tell.
        ///
        /// <para><b>THE BREAK BUCKLES; IT DOES NOT FALL FORWARD.</b> The first version pitched the body
        /// +28 degrees about local X — i.e. straight at the player, because the enemy is facing them and
        /// <c>BeginExecuted</c> turns it to face them again. Over a 2 m body that walks the chest almost
        /// a metre toward the eye, and the deathblow step-in then closes to <c>stabStandoff</c>: at 2.2x
        /// boss scale the frame filled edge to edge with enemy and the riposte became invisible.
        /// The pose now leans BACK, sags and rolls — the knees give, the guard opens, the weight goes
        /// onto the back foot. Nothing moves toward the camera, the silhouette stays inside its own
        /// footprint, and the chest turns up and out, which is exactly the surface the glyph rides on
        /// and the wand is about to go into.</para></summary>
        public virtual void Slump(bool on)
        {
            slumped = on;
            if (on)
            {
                // Staggered reads through POSE (the guard drops), a slow base-colour breath applied in
                // Update, and — since a HUD prompt at the edge of the screen is not where the player is
                // looking during an exchange — a marker ON THE ENEMY. That marker is the whole reason
                // the deathblow now reads as a choice rather than as something the game did for you.
                if (alertMarker != null) alertMarker.SetActive(false);
                StartMotion(TiltCo(StaggerEuler, StaggerSag, 0.2f));
            }
            else
            {
                StartMotion(TiltCo(Vector3.zero, Vector3.zero, 0.2f));
            }
            SetDeathblowReady(on);
        }

        /// <summary>
        /// World point the deathblow glyph occupies for a viewer at <paramref name="eye"/> — the
        /// enemy sternum, pushed off the body surface toward that eye. Valid whether or not the marker
        /// is currently up, because the commit burst and the wand blast both need it on frames where
        /// the window has just been spent.
        ///
        /// <para>One source of truth on purpose: the glyph, the shatter and the blast must land on the
        /// same pixels, or the beat reads as three unrelated effects that happen to be near each other.</para>
        /// </summary>
        public virtual Vector3 DeathblowPoint(Vector3 eye)
        {
            if (deathblowMarker != null)
            {
                var m = deathblowMarker.GetComponent<DeathblowMarker>();
                if (m != null) return m.SurfacePoint(eye);
            }
            float s = Mathf.Max(0.01f, transform.lossyScale.x);
            Vector3 axis = transform.position + Vector3.up * (1.45f * s);
            Vector3 to = eye - axis; to.y = 0f;
            return to.sqrMagnitude > 0.000001f ? axis + to.normalized * (0.8f * s) : axis;
        }

        /// <summary>
        /// Raise or drop the deathblow glyph. Driven by <see cref="Slump"/> for the ordinary posture
        /// break/recover pair, and called directly when the window closes for any other reason — the
        /// blow being committed, or the body dying — because a marker that outlives its window is a
        /// promise the game cannot keep.
        /// </summary>
        public virtual void SetDeathblowReady(bool on)
        {
            if (deathblowMarker != null) deathblowMarker.SetActive(on);
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
        /// <summary>
        /// A world point <paramref name="localHeight"/> up the VISIBLE body — see
        /// <see cref="IEnemyPresentation.BodyPoint"/> for why this is presentation's job.
        ///
        /// <para>Measured from <see cref="lungeRoot"/> when there is one, because that is the transform
        /// the lunge, the settle dip and every authored wind-up <c>bodyOffset</c> are written to. Using
        /// the enemy's own transform instead marks its navigation position, which is a point the body
        /// leaves the moment it commits to anything.</para>
        ///
        /// <para>Deliberately NOT the renderer bounds. A skinned mesh's bounds centre moves with the
        /// animation, so on a body with its arms out — the Marionette mid-whirl — a bounds-derived point
        /// wanders around inside the chest several times a second. A transform plus a height is stable
        /// by construction, which is what a marker that is up for the whole fight needs to be.</para>
        /// </summary>
        public virtual Vector3 BodyPoint(float localHeight)
        {
            Transform t = lungeRoot != null ? lungeRoot : transform;
            return t.TransformPoint(new Vector3(0f, localHeight, 0f));
        }

        public virtual void Die()
        {
            glowAmount = 0f; tintBoost = 0f; chargeDark = 0f;
            if (alertMarker != null) alertMarker.SetActive(false);
            SetDeathblowReady(false);
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
                SetArm(Vector3.Lerp(Vector3.zero, -currentPose.windup * 0.12f, dip), currentPose.weaponLag);
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
                    // The body carries the pose, not just the arm. Both channels come from the attack:
                    // an authored pose supplies its own, an unauthored one gets the generic rock-back.
                    lungeRoot.localPosition = lungeBase + currentPose.bodyOffset * k;
                    lungeRoot.localRotation = lungeBaseRot * Quaternion.Euler(currentPose.bodyEuler * k);
                }
                SetArm(Vector3.Lerp(armFrom, currentPose.windup, eased), currentPose.weaponLag);
                t += Time.deltaTime; yield return null;
            }
            if (!cuePeak) SetArm(currentPose.windup, currentPose.weaponLag);
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
            // The brain moves the root the full lungeDistance; the mesh only leans into it (spec R1).
            dist = Mathf.Min(dist, MaxVisualLunge);
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
                    lungeRoot.localPosition = Vector3.Lerp(from, lungeBase + (slumped ? StaggerSag : Vector3.zero), k);
                    lungeRoot.localRotation = Quaternion.Slerp(fromRot, slumped ? lungeBaseRot * Quaternion.Euler(StaggerEuler) : lungeBaseRot, k);
                }
                SetArm(Vector3.Lerp(armTo, Vector3.zero, k), 0.45f);
                t += Time.deltaTime; yield return null;
            }
            SetArm(Vector3.zero, 0f);
        }

        IEnumerator TiltCo(Vector3 euler, Vector3 offset, float seconds)
        {
            if (lungeRoot == null) yield break;
            float t = 0f;
            Quaternion from = lungeRoot.localRotation;
            Vector3 fromPos = lungeRoot.localPosition;
            Quaternion to = lungeBaseRot * Quaternion.Euler(euler);
            Vector3 toPos = lungeBase + offset;
            // Staggered: the guard drops and the weapon arm is flung BACK and OUT, opening the chest.
            // It used to swing +38 about X, which points a 1.35 m weapon straight down the camera at
            // stabbing range — the same mistake as the forward pitch, one bone further out.
            Vector3 armTo = slumped ? StaggerArm : Vector3.zero;
            while (t < seconds)
            {
                float k = t / seconds;
                lungeRoot.localRotation = Quaternion.Slerp(from, to, k);
                lungeRoot.localPosition = Vector3.Lerp(fromPos, toPos, k);
                SetArm(Vector3.Lerp(Vector3.zero, armTo, k), 0.6f);
                t += Time.unscaledDeltaTime; yield return null;
            }
            lungeRoot.localRotation = to;
            lungeRoot.localPosition = toPos;
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
                // AWAY, never toward. A riposte kills at stabStandoff, about 1.7 m of air between the
                // camera and the body, so a corpse that pitches +85 about X (forward, into the player,
                // because BeginExecuted just turned it to face them) drops a whole body through the lens
                // on the exact frame the player is meant to be watching the blast.
                if (lungeRoot != null)
                {
                    lungeRoot.localRotation = Quaternion.Slerp(r0, lungeBaseRot * Quaternion.Euler(-78f, 0f, 22f), k);
                    lungeRoot.localPosition = Vector3.Lerp(lungeBase, lungeBase + new Vector3(0f, -0.15f, -0.55f), k);
                }
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

            // Emission = the burning floor + the parry spike. The floor is modulated by chargeDark on
            // purpose, so a body that is on fire still INHALES on a wind-up: the fire is drawn in as it
            // charges and returns as it strikes. Without that the aura would flatten the single most
            // important tell the enemy has, and "light on an enemy means you deflected" would stop
            // being true for burning enemies specifically.
            Color emission = Color.black;
            if (auraAmount > 0f) emission += auraColor * (auraAmount * Mathf.Lerp(1f, 0.25f, chargeDark));
            if (glowAmount > 0f) emission += glowColor * glowAmount;
            bodyMpb.SetColor(EmissionId, emission);
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

            if (tintBoost > 0f)
            {
                tintBoost = Mathf.Max(0f, tintBoost - dt * tintDecayRate);
                if (tintBoost <= 0f) tintDecayRate = HitFlashFloorDecay;  // ready for the next, independent hit
                dirty = true;
            }
            if (glowAmount > 0f) { glowAmount = Mathf.Max(0f, glowAmount - dt * 3.2f); dirty = true; }
            if (slumped) dirty = true;   // the stagger breath animates continuously

            if (dirty) WriteBody();
        }

    }
}
