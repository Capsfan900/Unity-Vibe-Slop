using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Procedural first-person weapon animation. No clips: poses from WeaponData are lerped in code.
    /// Root = sway/bob, child "Model" = attack/parry/drink poses. Attack timing uses scaled time so hitstop freezes the swing.
    ///
    /// <para>RIG: Model → Hand (gauntlet boxes) → Grip → the weapon instance. The weapon is a rigid child
    /// of the hand, so no pose can ever separate the two; <see cref="ViewmodelArm"/> then draws the upper
    /// arm and forearm from a fixed shoulder anchor to the hand. Every pose below therefore moves the
    /// arm, not just the weapon, without a single authored pose changing.</para>
    ///
    /// <para>The swing clock is DELIBERATELY scaled: hitstop freezing the arm mid-swing is the impact
    /// device, not a bug. Sway/bob and the idle return use <see cref="TimeScaleController.PlayerDelta"/>
    /// because those are driven by the player's own movement.</para>
    /// </summary>
    public class WeaponViewmodel : MonoBehaviour
    {
        public Transform model;

        [Header("Arm rig")]
        /// <summary>The gauntleted hand, a rigid child of <see cref="model"/>. Built by PrefabFactory.</summary>
        public Transform hand;
        /// <summary>Child of <see cref="hand"/>. Every weapon/override model parents HERE, never to model.</summary>
        public Transform grip;
        /// <summary>Solves the two arm bones from the shoulder to wherever the hand ended up.</summary>
        public ViewmodelArm arm;

        public float swayAmount = 0.0015f;
        public float bobAmount = 0.02f;

        WeaponData data;
        GameObject instance;
        GameObject overrideInstance;
        FirstPersonMotor motor;
        Coroutine anim;
        WeaponTrail trail;
        Pose current;
        bool holding;
        /// <summary>True between <see cref="PlayGuard"/> and <see cref="EndGuard"/>. The guard is a
        /// STANCE, so it outlives every momentary pose: an attack, a parry flick or a weapon swap that
        /// ends while the button is still down falls back into the stance, not into idle.</summary>
        bool guarding;
        Vector3 sway;
        float bobT;

        // Wand poses live here rather than on WandData: they describe how the *hand* presents any wand,
        // which is a property of the viewmodel rig, not of a particular wand.
        static readonly Pose WandReady = new Pose(new Vector3(0.28f, -0.22f, 0.62f), new Vector3(-8f, -6f, 0f));
        static readonly Pose WandCharge = new Pose(new Vector3(0.26f, -0.15f, 0.72f), new Vector3(-16f, -4f, 0f));
        static readonly Pose WandRecoil = new Pose(new Vector3(0.31f, 0.00f, 0.42f), new Vector3(-44f, -10f, 0f));

        /// <summary>The temporary model swapped in by <see cref="ShowOverride"/>, or null.</summary>
        public GameObject OverrideInstance => overrideInstance;

        /// <summary>
        /// Whatever model the hand is actually holding this frame — the wand override if one is up,
        /// otherwise the equipped melee weapon. Null before <see cref="SetWeapon"/> has run.
        /// </summary>
        public Transform CurrentModel =>
            overrideInstance != null ? overrideInstance.transform : (instance != null ? instance.transform : null);

        /// <summary>
        /// World position of the business end of the held weapon — the sword's point, the hammer's head,
        /// the dagger's tip. The main-hand twin of <see cref="OffhandViewmodel.TipWorldPosition"/>, and it
        /// exists for the same reason: an effect drawn anywhere but on the thing that fired it reads as an
        /// explosion with no author. Every super's blast is anchored here.
        ///
        /// <para>Every weapon prefab built by <c>PrefabFactory</c> carries a <c>Tip*</c> part (the hammer's
        /// is <c>TipBand</c>, across its head). The highest renderer in model space is the fallback, so a
        /// weapon authored without one still emits from its far end rather than from the fist — and a
        /// viewmodel with no model at all falls back to the grip, never to the player's navel.</para>
        /// </summary>
        public Vector3 TipWorldPosition
        {
            get
            {
                Transform m = CurrentModel;
                if (m != null)
                {
                    Renderer highest = null;
                    float bestY = float.MinValue;
                    foreach (var r in m.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.name.StartsWith("Tip")) return r.bounds.center;
                        float y = m.InverseTransformPoint(r.bounds.center).y;
                        if (y > bestY) { bestY = y; highest = r; }
                    }
                    if (highest != null) return highest.bounds.center;
                    return m.position;
                }
                return grip != null ? grip.position : (model != null ? model.position : transform.position);
            }
        }

        /// <summary>World position of the hand on the hilt. The tail of the blade, for effects that
        /// travel along the weapon rather than leaving it.</summary>
        public Vector3 GripWorldPosition =>
            grip != null ? grip.position : (model != null ? model.position : transform.position);

        void Awake()
        {
            motor = GetComponentInParent<FirstPersonMotor>();
            trail = GetComponent<WeaponTrail>();
            if (model == null)
            {
                var m = new GameObject("Model");
                m.transform.SetParent(transform, false);
                model = m.transform;
            }
            // A viewmodel built at runtime (tests, sandbox) has no authored hand. Falling back to the
            // model keeps every code path below valid — it just has no arm.
            if (grip == null) grip = hand != null ? hand : model;
        }

        public void SetWeapon(WeaponData w)
        {
            data = w;
            Stop();
            if (trail != null) trail.Clear();
            holding = false;
            ClearOverride();   // a swap mid-riposte must not leave the wand model parented alongside
            if (instance != null) Destroy(instance);
            if (w != null && w.viewmodelPrefab != null)
            {
                instance = Instantiate(w.viewmodelPrefab, grip != null ? grip : model);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one * w.viewmodelScale;
                foreach (var r in instance.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                // The blade's energy is animated by EnergyGlow, which owns _EmissionColor on the model
                // and needs its Seg*/Tip*/Float* parts bound after instantiation.
                BindGlow(instance, w.neon);
                CloseHandOn(instance);
            }
            if (w != null) { current = w.idle; ApplyPose(current); }
            // Swapping weapons with the guard still held must come back up in the stance, not idle.
            if (guarding && w != null) PlayGuard();
        }

        /// <summary>
        /// Slide the HAND onto the weapon's hilt without moving the weapon.
        ///
        /// <para>Every weapon prefab hangs its hilt at a different height (the hammer's haft starts where
        /// the dagger's blade already ends), so a fixed hand position grips one weapon and empty air for
        /// the rest. Instead the hand is moved to the prefab's <c>Grip*</c> part and the grip node is
        /// moved by the exact opposite, which leaves the weapon on the same pixels it occupied before
        /// the arms existed — the framing and readability work is preserved by construction.</para>
        /// </summary>
        void CloseHandOn(GameObject inst)
        {
            if (hand == null || grip == null || model == null || grip == model || grip == hand) return;
            hand.localPosition = Vector3.zero;
            grip.localPosition = Vector3.zero;
            if (inst == null) return;
            Transform g = FindGrip(inst.transform);
            if (g == null) return;                       // no authored hilt: hold it in the palm centre
            Vector3 p = model.InverseTransformPoint(g.position);
            hand.localPosition = p;
            grip.localPosition = -p;                     // hand has identity rotation, so this cancels exactly
        }

        /// <summary>First descendant named Grip* — the hilt/haft the fingers close around.</summary>
        internal static Transform FindGrip(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && t.name.StartsWith("Grip")) return t;
            return null;
        }

        void LateUpdate()
        {
            if (data == null) return;
            float udt = Time.unscaledDeltaTime;

            Vector2 look = InputReader.I != null && InputReader.I.LookIsMouse ? InputReader.I.LookDelta : Vector2.zero;
            Vector3 targetSway = Vector3.ClampMagnitude(new Vector3(-look.x, -look.y, 0f) * swayAmount, 0.06f);
            sway = Vector3.Lerp(sway, targetSway, 12f * udt);

            float speed = motor != null ? motor.HorizontalSpeed : 0f;
            bool grounded = motor != null && motor.IsGrounded;
            Vector3 bob = Vector3.zero;
            if (grounded && speed > 0.5f)
            {
                // RULE 1: the bob is driven by the player's own movement, so it must not stutter during
                // hitstop. Now that the whole arm rides this transform, a frozen bob freezes the arm.
                bobT += TimeScaleController.PlayerDelta * speed * 1.3f;
                float k = Mathf.Clamp01(speed / 11f);
                bob = new Vector3(Mathf.Sin(bobT) * bobAmount, -Mathf.Abs(Mathf.Cos(bobT)) * bobAmount * 0.7f, 0f) * k;
            }
            transform.localPosition = Vector3.Lerp(transform.localPosition, sway + bob, 10f * udt);

            if (anim == null && !holding)
            {
                current = Pose.Lerp(current, data.idle, Mathf.Clamp01(12f * TimeScaleController.PlayerDelta));
                ApplyPose(current);
            }
        }

        void ApplyPose(Pose p)
        {
            model.localPosition = p.pos;
            model.localRotation = Quaternion.Euler(p.euler);
        }

        void Stop()
        {
            if (anim != null) { StopCoroutine(anim); anim = null; }
        }

        static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
        /// <summary>Slow, then accelerating. ANTICIPATION ONLY: a wind-up that starts fast and
        /// settles reads as drifting into position; one that starts slow and gathers reads as
        /// STORING ENERGY, which is the entire job of the phase.</summary>
        static float EaseIn(float t) { t = Mathf.Clamp01(t); return t * t; }
        static float EaseInOut(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        static Pose Mirror(Pose p, bool m)
        {
            if (!m) return p;
            return new Pose(new Vector3(-p.pos.x * 0.6f, p.pos.y, p.pos.z), new Vector3(p.euler.x, -p.euler.y, -p.euler.z));
        }

        /// <summary>Seconds held on the end-of-arc pose before recovery starts. ~4 frames at 60 Hz:
        /// enough for the eye to catch the contact pose, short enough that the recovery leg it is
        /// taken from still reads as a settle rather than a snap. Capped at 45% of that leg.</summary>
        const float FollowThroughHold = 0.07f;

        public void PlayAttack(int comboIndex, float duration, float hitDelay)
        {
            if (data == null) return;
            Stop(); holding = false;
            anim = StartCoroutine(AttackCo(comboIndex, duration, hitDelay));
        }

        IEnumerator AttackCo(int combo, float dur, float hitDelay)
        {
            bool mirror = combo % 2 == 1;
            Pose wind = Mirror(data.windup, mirror);
            Pose end = Mirror(data.swingEnd, mirror);
            Pose start = current;
            float t = 0f;
            while (t < hitDelay)
            {
                current = Pose.Lerp(start, wind, EaseIn(t / hitDelay));
                ApplyPose(current); t += Time.deltaTime; yield return null;
            }
            float swing = Mathf.Clamp(dur * 0.2f, 0.04f, Mathf.Max(0.04f, dur - hitDelay));
            // The ribbon is opened HERE and closed below, so it spans exactly the strike leg: no trail
            // on the wind-up (nothing is dangerous yet) and none on the recovery (nothing is any more).
            // The trail therefore reads as the hitbox window, not as decoration on the whole animation.
            if (trail != null) trail.BeginStrike();
            t = 0f;
            while (t < swing)
            {
                current = Pose.Lerp(wind, end, EaseOut(t / swing));
                ApplyPose(current); t += Time.deltaTime; yield return null;
            }
            // Land the arc EXACTLY on swingEnd before the ribbon closes: EndStrike takes its final
            // sample from the pose that is applied right now, and a ribbon whose newest point is a
            // frame short of the end of the arc hangs visibly detached from the blade.
            current = end; ApplyPose(current);
            if (trail != null) trail.EndStrike();
            // FOLLOW-THROUGH HOLD. Sitting on the end-of-arc pose for a few frames is the cheapest
            // weight cue there is — without it the blade never arrives anywhere, it only passes through.
            // It is taken OUT OF the recovery leg, never added to the attack: attackDuration, hitDelay
            // and comboWindow are tuned data and the parry window is calibrated against them, so the
            // total here must equal hitDelay + swing + rest exactly as it did before.
            float rest = Mathf.Max(0.01f, dur - hitDelay - swing);
            float hold = Mathf.Min(FollowThroughHold, rest * 0.45f);
            rest = Mathf.Max(0.01f, rest - hold);
            t = 0f;
            while (t < hold) { ApplyPose(end); t += Time.deltaTime; yield return null; }
            current = end;
            // MID-SWING INTO GUARD IS ONE MOTION. Swinging drops the guard mechanically, but the
            // RECOVERY leg of the arc is retargeted at the STANCE whenever the button is down, so the
            // blade travels from wherever the swing left it directly into the guard. Landing in idle and
            // then raising the stance was two motions with a visible beat between them, and it is the
            // case a real fight hits most often.
            // Read AFTER the hold, so a guard pressed during the follow-through is still caught.
            Pose settle = GuardWanted ? data.guard : data.idle;
            t = 0f;
            while (t < rest)
            {
                current = Pose.Lerp(end, settle, EaseInOut(t / rest));
                ApplyPose(current); t += Time.deltaTime; yield return null;
            }
            anim = null;
            if (guarding || GuardWanted) PlayGuard();
        }

        public void PlayParry(float activeSeconds)
        {
            if (data == null) return;
            Stop(); holding = true;
            anim = StartCoroutine(ToPoseAndHold(data.parry, 0.05f));
        }

        public void EndParry()
        {
            holding = false;
            Stop();
            // The parry flick is the FIRST 0.25 s of a held guard: pressing RMB opens the window and
            // holding it keeps the stance. Dropping to idle here made a held guard visibly flinch back
            // to the hip every time the window closed.
            if (guarding) PlayGuard();
        }

        // ---- held guard stance -------------------------------------------------------------------

        /// <summary>True while the katana stance is up. Read by tests and tooling.</summary>
        public bool IsGuarding => guarding;

        /// <summary>
        /// The BUTTON is down, whether or not the stance is mechanically allowed right now. Written every
        /// frame by <see cref="ParryController"/>. It exists so an attack can END IN the stance instead of
        /// ending in idle and then raising it: mechanically your swing drops your guard, but visually the
        /// blade must travel from wherever the arc left it straight into the stance, as ONE motion.
        /// </summary>
        public bool GuardWanted { get; set; }

        /// <summary>
        /// Raise the blade across the body and HOLD it there until <see cref="EndGuard"/>. Unlike
        /// <see cref="PlayParry"/> this has no duration — the button, not a timer, ends it.
        ///
        /// <para>RULE 1: the ease-in runs on <see cref="TimeScaleController.PlayerDelta"/>. The stance is
        /// driven by the player's own button, and a guard that freezes halfway up during the hitstop of
        /// the blow it is absorbing is exactly the stutter rule 1 exists to prevent.</para>
        /// </summary>
        public void PlayGuard()
        {
            if (data == null) return;
            // Already settled in the stance (or mid guard-kick): re-raising would restart the blend and
            // read as a hitch. This is what lets ParryController re-assert the stance every frame safely.
            if (guarding && holding && anim != null) return;
            guarding = true;
            Stop(); holding = true;
            anim = StartCoroutine(GuardCo(GuardRise));
        }

        /// <summary>
        /// Blend into the stance. 0.08 s: the perfect window is 130 ms, so a stance that takes longer
        /// than this to arrive lags the button in the only exchange that matters. Short enough to feel
        /// instant, long enough not to read as a snap.
        /// </summary>
        const float GuardRise = 0.08f;

        /// <summary>Blend back out. Slower than the rise on purpose - putting the blade down is a
        /// deliberate beat, not a flinch - but still one continuous motion.</summary>
        const float GuardFall = 0.16f;

        /// <summary>Release the stance with a distinct, slower settle back to idle.</summary>
        public void EndGuard()
        {
            if (!guarding) return;
            guarding = false;
            holding = false;
            Stop();
            if (data != null) anim = StartCoroutine(GuardReleaseCo(GuardFall));
        }

        /// <summary>
        /// ONE CONTINUOUS BLEND from wherever the weapon actually is into the stance. Three things make
        /// that true, and all three were wrong once:
        ///   - the start is the LIVE model transform, not the authored idle, so interrupting a swing
        ///     does not teleport the blade home first;
        ///   - the rotation is a quaternion slerp taken directly from the live rotation to the stance,
        ///     so it travels the shortest arc and can never swing through an orientation nobody
        ///     authored (interpolating in euler space between two perfectly good poses routinely passes
        ///     through "pointing forward", which is exactly what this looked like);
        ///   - there is NO WAYPOINT. The parry flick is not on the path any more.
        /// </summary>
        IEnumerator GuardCo(float rise)
        {
            Vector3 p0 = model.localPosition;
            Quaternion r0 = model.localRotation;
            Quaternion r1 = Quaternion.Euler(data.guard.euler);
            float t = 0f;
            while (t < rise)
            {
                float k = EaseOut(t / rise);
                model.localPosition = Vector3.Lerp(p0, data.guard.pos, k);
                model.localRotation = Quaternion.Slerp(r0, r1, k);
                current = new Pose(model.localPosition, model.localRotation.eulerAngles);
                t += TimeScaleController.PlayerDelta; yield return null;
            }
            current = data.guard; ApplyPose(current);
            while (holding) yield return null;
            anim = null;
        }

        IEnumerator GuardReleaseCo(float fall)
        {
            // Same single-blend rule on the way out: from the live transform, quaternion-native, no
            // waypoint. EaseInOut rather than EaseOut so the drop reads as a deliberate settle.
            Vector3 p0 = model.localPosition;
            Quaternion r0 = model.localRotation;
            Quaternion r1 = Quaternion.Euler(data.idle.euler);
            float t = 0f;
            while (t < fall)
            {
                float k = EaseInOut(t / fall);
                model.localPosition = Vector3.Lerp(p0, data.idle.pos, k);
                model.localRotation = Quaternion.Slerp(r0, r1, k);
                current = new Pose(model.localPosition, model.localRotation.eulerAngles);
                t += TimeScaleController.PlayerDelta; yield return null;
            }
            current = data.idle; ApplyPose(current);
            anim = null;
        }

        /// <summary>
        /// Kick the guard on impact: a short shove of the stance away from the blow, snapping back.
        /// A guard that eats all the damage has to show the hit somewhere or it reads as nothing
        /// happening. No-op when the stance is not up.
        /// </summary>
        public void GuardImpact()
        {
            if (!guarding || data == null) return;
            Stop(); holding = true;
            anim = StartCoroutine(GuardImpactCo());
        }

        IEnumerator GuardImpactCo()
        {
            Pose g = data.guard;
            // MEASURED, not guessed. The first kick was (+0.05, -0.05, -0.10) with a -7 deg yaw: pulling
            // the blade 0.10 m toward the lens magnified it and the yaw swung the tip INWARD, so at the
            // peak of the kick the point crossed screen centre and sat on the enemy — during the one
            // beat the player most needs to see them. The kick is now driven DOWN AND OUT (away from the
            // crosshair on both axes) with no yaw at all, and barely any depth change.
            Pose shoved = new Pose(g.pos + new Vector3(0.07f, -0.09f, -0.02f),
                                   g.euler + new Vector3(14f, 0f, -10f));
            float t = 0f, push = 0.045f;
            while (t < push)
            {
                current = Pose.Lerp(g, shoved, EaseOut(t / push));
                ApplyPose(current); t += TimeScaleController.PlayerDelta; yield return null;
            }
            t = 0f; float recover = 0.13f;
            while (t < recover)
            {
                current = Pose.Lerp(shoved, g, EaseOut(t / recover));
                ApplyPose(current); t += TimeScaleController.PlayerDelta; yield return null;
            }
            current = g; ApplyPose(current);
            while (holding) yield return null;
            anim = null;
        }

        IEnumerator ToPoseAndHold(Pose target, float seconds)
        {
            Pose start = current;
            float t = 0f;
            while (t < seconds)
            {
                current = Pose.Lerp(start, target, EaseOut(t / seconds));
                ApplyPose(current); t += Time.unscaledDeltaTime; yield return null;
            }
            current = target; ApplyPose(current);
            while (holding) yield return null;
            anim = null;
        }

        public void PlayDrink(float duration)
        {
            if (data == null) return;
            Stop(); holding = false;
            anim = StartCoroutine(DrinkCo(duration));
        }

        IEnumerator DrinkCo(float dur)
        {
            Pose start = current;
            float up = dur * 0.35f, hold = dur * 0.4f, down = dur * 0.25f;
            float t = 0f;
            while (t < up) { current = Pose.Lerp(start, data.drink, EaseOut(t / up)); ApplyPose(current); t += Time.deltaTime; yield return null; }
            t = 0f;
            while (t < hold)
            {
                var p = data.drink; p.euler.x += Mathf.Sin(t * 30f) * 3f;
                current = p; ApplyPose(current); t += Time.deltaTime; yield return null;
            }
            t = 0f;
            while (t < down) { current = Pose.Lerp(data.drink, data.idle, EaseInOut(t / down)); ApplyPose(current); t += Time.deltaTime; yield return null; }
            anim = null;
        }

        public void PlayExecute(float duration)
        {
            if (data == null) return;
            Stop(); holding = false;
            anim = StartCoroutine(ExecuteCo(duration));
        }

        IEnumerator ExecuteCo(float dur)
        {
            Pose start = current;
            float wind = dur * 0.55f, slam = dur * 0.12f, rest = dur * 0.33f;
            float t = 0f;
            while (t < wind) { current = Pose.Lerp(start, data.executeWindup, EaseOut(t / wind)); ApplyPose(current); t += Time.unscaledDeltaTime; yield return null; }
            t = 0f;
            while (t < slam) { current = Pose.Lerp(data.executeWindup, data.executeEnd, EaseOut(t / slam)); ApplyPose(current); t += Time.unscaledDeltaTime; yield return null; }
            t = 0f;
            while (t < rest) { current = Pose.Lerp(data.executeEnd, data.idle, EaseInOut(t / rest)); ApplyPose(current); t += Time.unscaledDeltaTime; yield return null; }
            anim = null;
        }

        public void Interrupt()
        {
            holding = false;
            if (trail != null) trail.Clear();
            guarding = false;
            Stop();
            ClearOverride();
        }

        // ---- temporary model swap (wand riposte) -----------------------------------------------

        /// <summary>
        /// Hide the melee weapon and show a different model in its place. Used for the wand riposte.
        /// Always pair with <see cref="ClearOverride"/>. Read <see cref="OverrideInstance"/> to tint it.
        /// </summary>
        public void ShowOverride(GameObject prefab, float scale)
        {
            ClearOverride();
            if (prefab == null || model == null) return;

            if (instance != null) instance.SetActive(false);

            overrideInstance = Instantiate(prefab, grip != null ? grip : model);
            overrideInstance.transform.localPosition = Vector3.zero;
            overrideInstance.transform.localRotation = Quaternion.identity;
            overrideInstance.transform.localScale = Vector3.one * (scale <= 0f ? 1f : scale);
            foreach (var r in overrideInstance.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            // Caller tints via WandController.Tint immediately after; just bind the parts here.
            var overrideGlow = overrideInstance.GetComponent<EnergyGlow>();
            if (overrideGlow != null) overrideGlow.Collect();
            CloseHandOn(overrideInstance);
        }

        /// <summary>Destroy the override model and restore the melee weapon. Safe to call twice.</summary>
        public void ClearOverride()
        {
            if (overrideInstance != null) Destroy(overrideInstance);
            overrideInstance = null;
            if (instance != null) { instance.SetActive(true); CloseHandOn(instance); }
        }

        /// <summary>Raise the wand and hold it charged. Holds until a fire/clear call releases it.</summary>
        public void PlayWandRaise(float seconds)
        {
            Stop(); holding = true;
            anim = StartCoroutine(WandRaiseCo(Mathf.Max(0.01f, seconds)));
        }

        IEnumerator WandRaiseCo(float seconds)
        {
            Pose start = current;
            float lift = seconds * 0.55f;
            float t = 0f;
            while (t < lift)
            {
                current = Pose.Lerp(start, WandReady, EaseOut(t / lift));
                ApplyPose(current); t += Time.unscaledDeltaTime; yield return null;
            }
            float charge = Mathf.Max(0.01f, seconds - lift);
            t = 0f;
            while (t < charge)
            {
                // creeps forward as it charges, so the discharge has something to release
                current = Pose.Lerp(WandReady, WandCharge, EaseInOut(t / charge));
                ApplyPose(current); t += Time.unscaledDeltaTime; yield return null;
            }
            current = WandCharge; ApplyPose(current);
            while (holding) yield return null;
            anim = null;
        }

        /// <summary>Snap into recoil, then settle. Ends the hold started by <see cref="PlayWandRaise"/>.</summary>
        public void PlayWandFire(float seconds)
        {
            holding = false;
            Stop();
            anim = StartCoroutine(WandFireCo(Mathf.Max(0.02f, seconds)));
        }

        IEnumerator WandFireCo(float seconds)
        {
            Pose start = current;
            float kick = Mathf.Min(0.06f, seconds * 0.3f);
            float t = 0f;
            while (t < kick)
            {
                current = Pose.Lerp(start, WandRecoil, EaseOut(t / kick));
                ApplyPose(current); t += Time.unscaledDeltaTime; yield return null;
            }
            float settle = Mathf.Max(0.01f, seconds - kick);
            t = 0f;
            while (t < settle)
            {
                current = Pose.Lerp(WandRecoil, WandReady, EaseInOut(t / settle));
                ApplyPose(current); t += Time.unscaledDeltaTime; yield return null;
            }
            anim = null;
        }

        /// <summary>
        /// ONE WRITER PER MATERIAL CHANNEL: when a model carries an EnergyGlow it owns emission, so the
        /// hue is handed to it rather than written through a property block that it would overwrite.
        /// </summary>
        static void BindGlow(GameObject go, Color tint)
        {
            if (go == null) return;
            var glow = go.GetComponent<EnergyGlow>();
            if (glow == null) return;
            glow.Collect();
            glow.SetTint(tint);
        }

    }
}
