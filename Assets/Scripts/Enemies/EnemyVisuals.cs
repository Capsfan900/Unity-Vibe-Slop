using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Primitive-body enemy feedback: emissive telegraphs, lunge, recoil, slump, death.</summary>
    public class EnemyVisuals : MonoBehaviour
    {
        public Renderer body;
        public Renderer eye;
        public Renderer weapon;
        public Transform lungeRoot;
        public GameObject alertMarker;

        // Dark fantasy palette. Telegraph stays bright so the parry window remains readable.
        static readonly Color TelegraphColor = new Color(1f, 0.75f, 0.45f) * 6f;   // ember-white
        static readonly Color UnblockableColor = new Color(1f, 0f, 0.25f) * 8f;    // blood red (unchanged)
        static readonly Color ParryColor = new Color(0.7f, 0.85f, 1f) * 6f;        // pale steel
        static readonly Color StaggerA = new Color(1f, 0.55f, 0.1f) * 1f;          // ember
        static readonly Color StaggerB = new Color(1f, 0.55f, 0.1f) * 4f;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        EmissiveFlash flash;
        MaterialPropertyBlock eyeMpb;
        Coroutine motion;
        Vector3 lungeBase;
        Quaternion lungeBaseRot;
        bool slumped;

        void Awake()
        {
            flash = GetComponent<EmissiveFlash>();
            if (flash == null) flash = gameObject.AddComponent<EmissiveFlash>();
            eyeMpb = new MaterialPropertyBlock();
            if (lungeRoot != null) { lungeBase = lungeRoot.localPosition; lungeBaseRot = lungeRoot.localRotation; }
            if (alertMarker != null) alertMarker.SetActive(false);
        }

        public void Setup(EnemyData d)
        {
            var list = new System.Collections.Generic.List<Renderer>();
            if (body) list.Add(body);
            if (weapon) list.Add(weapon);
            flash.SetRenderers(list.ToArray());
            flash.SetBase(d.emission);
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", d.bodyColor);
            mpb.SetColor(EmissionId, d.emission);
            foreach (var r in list) r.SetPropertyBlock(mpb);
        }

        public void SetAccent(Color emission)
        {
            flash.SetBase(emission);
        }

        public void SetPostureRatio(float r)
        {
            if (eye == null) return;
            Color c = Color.Lerp(Color.white * 3f, new Color(1f, 0.85f, 0.1f) * 8f, r);
            eyeMpb.SetColor(EmissionId, c);
            eye.SetPropertyBlock(eyeMpb);
        }

        public void Telegraph(float seconds, bool unblockable)
        {
            flash.Ramp(unblockable ? UnblockableColor : TelegraphColor, seconds);
            if (alertMarker != null) alertMarker.SetActive(unblockable);
            StartMotion(WindupCo(seconds));
        }

        public void Strike(float lunge, float seconds)
        {
            flash.Flash(Color.white * 5f, 0.12f);
            if (alertMarker != null) alertMarker.SetActive(false);
            StartMotion(LungeCo(lunge, seconds));
        }

        public void ClearTelegraph()
        {
            flash.Clear();
            if (alertMarker != null) alertMarker.SetActive(false);
        }

        public void Recoil()
        {
            flash.Flash(ParryColor, 0.3f);
            if (alertMarker != null) alertMarker.SetActive(false);
            StartMotion(LungeCo(-0.5f, 0.25f));
        }

        public void HitFlash()
        {
            if (slumped) return;
            flash.Flash(Color.white * 4f, 0.08f);
        }

        public void Slump(bool on)
        {
            slumped = on;
            if (on)
            {
                flash.Pulse(StaggerA, StaggerB, 0.5f);
                if (alertMarker != null) alertMarker.SetActive(false);
                StartMotion(TiltCo(28f, 0.2f));
            }
            else
            {
                flash.Clear();
                StartMotion(TiltCo(0f, 0.2f));
            }
        }

        public void Roar()
        {
            flash.Flash(Color.white * 8f, 0.6f);
            StartMotion(RoarCo());
        }

        public void Die()
        {
            flash.Clear();
            if (alertMarker != null) alertMarker.SetActive(false);
            StartMotion(DieCo());
        }

        void StartMotion(IEnumerator co)
        {
            if (motion != null) StopCoroutine(motion);
            motion = StartCoroutine(co);
        }

        IEnumerator WindupCo(float seconds)
        {
            if (lungeRoot == null) yield break;
            float t = 0f;
            while (t < seconds)
            {
                float k = t / seconds;
                lungeRoot.localPosition = lungeBase + Vector3.back * 0.25f * k + Vector3.up * 0.15f * k;
                lungeRoot.localRotation = lungeBaseRot * Quaternion.Euler(-12f * k, 0f, 0f);
                t += Time.deltaTime; yield return null;
            }
        }

        IEnumerator LungeCo(float dist, float seconds)
        {
            if (lungeRoot == null) yield break;
            float t = 0f;
            Vector3 from = lungeRoot.localPosition;
            Quaternion fromRot = lungeRoot.localRotation;
            float half = seconds * 0.4f;
            while (t < half)
            {
                float k = t / half;
                lungeRoot.localPosition = Vector3.Lerp(from, lungeBase + Vector3.forward * dist, k);
                lungeRoot.localRotation = Quaternion.Slerp(fromRot, lungeBaseRot * Quaternion.Euler(18f * Mathf.Sign(dist), 0f, 0f), k);
                t += Time.deltaTime; yield return null;
            }
            t = 0f;
            float back = Mathf.Max(0.05f, seconds - half);
            from = lungeRoot.localPosition; fromRot = lungeRoot.localRotation;
            while (t < back)
            {
                float k = t / back;
                lungeRoot.localPosition = Vector3.Lerp(from, lungeBase, k);
                lungeRoot.localRotation = Quaternion.Slerp(fromRot, slumped ? lungeBaseRot * Quaternion.Euler(28f, 0f, 0f) : lungeBaseRot, k);
                t += Time.deltaTime; yield return null;
            }
        }

        IEnumerator TiltCo(float angle, float seconds)
        {
            if (lungeRoot == null) yield break;
            float t = 0f;
            Quaternion from = lungeRoot.localRotation;
            Vector3 fromPos = lungeRoot.localPosition;
            Quaternion to = lungeBaseRot * Quaternion.Euler(angle, 0f, 0f);
            while (t < seconds)
            {
                float k = t / seconds;
                lungeRoot.localRotation = Quaternion.Slerp(from, to, k);
                lungeRoot.localPosition = Vector3.Lerp(fromPos, lungeBase, k);
                t += Time.unscaledDeltaTime; yield return null;
            }
            lungeRoot.localRotation = to;
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
    }
}
