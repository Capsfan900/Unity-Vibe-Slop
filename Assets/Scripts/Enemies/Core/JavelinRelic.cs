using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A javelin planted in the world: pure presentation, no collider, no combat. Built by
    /// <see cref="Javelin.OnWorldContact"/> at the contact point, pointed along the flight that put it
    /// there, and gone after <see cref="seconds"/>: it holds at full size for <see cref="holdFraction"/>
    /// of that, then SHRINKS to nothing while the hot tip cools to black (an opaque lit shaft cannot
    /// alpha-fade; a shrink is the honest read of "it was here, now it is not"). Scaled time, like the
    /// javelin itself: hitstop freezes it with the world.
    /// </summary>
    public sealed class JavelinRelic : MonoBehaviour
    {
        public float seconds = 1.5f;
        public float holdFraction = 0.7f;

        /// <summary>Relics currently planted, any thrower. Tests and the harness.</summary>
        public static int LiveCount { get; private set; }

        float age;
        Vector3 baseScale;
        Renderer tip;
        Color tipColor;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // ---------------------------------------------------------------- pure arithmetic (tests)

        /// <summary>1 while held, then a linear shrink to 0 at <paramref name="seconds"/>.</summary>
        public static float Shrink(float age, float seconds, float holdFraction)
        {
            if (seconds <= 0f) return 0f;
            float hold = seconds * Mathf.Clamp01(holdFraction);
            if (age <= hold) return 1f;
            return Mathf.Clamp01(1f - (age - hold) / Mathf.Max(0.0001f, seconds - hold));
        }

        /// <summary>The tip's colour at <paramref name="age"/>: the hot colour cooling linearly to black over the life.</summary>
        public static Color Cooling(Color hot, float age, float seconds)
        {
            float k = seconds <= 0f ? 1f : Mathf.Clamp01(age / seconds);
            return Color.Lerp(hot, Color.black, k);
        }

        /// <summary>
        /// Plant a relic with its tip <paramref name="embed"/> metres past <paramref name="point"/> along
        /// <paramref name="direction"/> and the shaft trailing back out of the surface.
        /// </summary>
        public static JavelinRelic Plant(Vector3 point, Vector3 direction, float length, float diameter, float embed,
                                         Material shaftMat, Material tipMat, Color tipColor, float seconds, float holdFraction)
        {
            var go = new GameObject("JavelinRelic");
            Vector3 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            go.transform.position = point + dir * embed;
            go.transform.rotation = Javelin.Heading(dir);

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            StripCollider(shaft);
            shaft.transform.SetParent(go.transform, false);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);       // cylinder Y -> +Z
            shaft.transform.localPosition = new Vector3(0f, 0f, -length * 0.5f);
            shaft.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
            var sr = shaft.GetComponent<Renderer>();
            sr.sharedMaterial = shaftMat;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;

            var tipGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tipGo.name = "Tip";
            StripCollider(tipGo);
            tipGo.transform.SetParent(go.transform, false);
            tipGo.transform.localPosition = new Vector3(0f, 0f, -embed);        // just proud of the surface
            tipGo.transform.localScale = Vector3.one * diameter * 2.2f;
            var tr = tipGo.GetComponent<Renderer>();
            tr.sharedMaterial = tipMat != null ? tipMat : shaftMat;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;

            var relic = go.AddComponent<JavelinRelic>();
            relic.seconds = seconds;
            relic.holdFraction = holdFraction;
            relic.tip = tr;
            relic.tipColor = tipColor;
            relic.baseScale = go.transform.localScale;
            return relic;
        }

        static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        }

        void OnEnable() { LiveCount++; }
        void OnDisable() { LiveCount = Mathf.Max(0, LiveCount - 1); }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            transform.localScale = baseScale * Shrink(age, seconds, holdFraction);
            if (tip != null)
            {
                if (mpb == null) mpb = new MaterialPropertyBlock();
                tip.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, Cooling(tipColor, age, seconds));
                tip.SetPropertyBlock(mpb);
            }
            if (age >= seconds) Destroy(gameObject);
        }
    }
}
