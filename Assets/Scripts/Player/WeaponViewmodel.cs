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
        Pose current;
        bool holding;
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
        static float EaseInOut(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        static Pose Mirror(Pose p, bool m)
        {
            if (!m) return p;
            return new Pose(new Vector3(-p.pos.x * 0.6f, p.pos.y, p.pos.z), new Vector3(p.euler.x, -p.euler.y, -p.euler.z));
        }

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
                current = Pose.Lerp(start, wind, EaseOut(t / hitDelay));
                ApplyPose(current); t += Time.deltaTime; yield return null;
            }
            float swing = Mathf.Clamp(dur * 0.2f, 0.04f, Mathf.Max(0.04f, dur - hitDelay));
            t = 0f;
            while (t < swing)
            {
                current = Pose.Lerp(wind, end, EaseOut(t / swing));
                ApplyPose(current); t += Time.deltaTime; yield return null;
            }
            float rest = Mathf.Max(0.01f, dur - hitDelay - swing);
            t = 0f;
            while (t < rest)
            {
                current = Pose.Lerp(end, data.idle, EaseInOut(t / rest));
                ApplyPose(current); t += Time.deltaTime; yield return null;
            }
            anim = null;
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
