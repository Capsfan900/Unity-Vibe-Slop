using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Procedural first-person weapon animation. No clips: poses from WeaponData are lerped in code.
    /// Root = sway/bob, child "Model" = attack/parry/drink poses. Attack timing uses scaled time so hitstop freezes the swing.
    /// </summary>
    public class WeaponViewmodel : MonoBehaviour
    {
        public Transform model;
        public float swayAmount = 0.0015f;
        public float bobAmount = 0.02f;

        WeaponData data;
        GameObject instance;
        FirstPersonMotor motor;
        Coroutine anim;
        Pose current;
        bool holding;
        Vector3 sway;
        float bobT;

        void Awake()
        {
            motor = GetComponentInParent<FirstPersonMotor>();
            if (model == null)
            {
                var m = new GameObject("Model");
                m.transform.SetParent(transform, false);
                model = m.transform;
            }
        }

        public void SetWeapon(WeaponData w)
        {
            data = w;
            Stop();
            holding = false;
            if (instance != null) Destroy(instance);
            if (w != null && w.viewmodelPrefab != null)
            {
                instance = Instantiate(w.viewmodelPrefab, model);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one * w.viewmodelScale;
                foreach (var r in instance.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }
            if (w != null) { current = w.idle; ApplyPose(current); }
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
                bobT += Time.deltaTime * speed * 1.3f;
                float k = Mathf.Clamp01(speed / 11f);
                bob = new Vector3(Mathf.Sin(bobT) * bobAmount, -Mathf.Abs(Mathf.Cos(bobT)) * bobAmount * 0.7f, 0f) * k;
            }
            transform.localPosition = Vector3.Lerp(transform.localPosition, sway + bob, 10f * udt);

            if (anim == null && !holding)
            {
                current = Pose.Lerp(current, data.idle, Mathf.Clamp01(12f * Time.deltaTime));
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
        }
    }
}
