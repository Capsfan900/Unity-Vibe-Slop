using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// World-space posture bar floating above a non-boss enemy (the boss uses the HUD bar instead).
    ///
    /// Built from two quads driven by MaterialPropertyBlocks rather than a world-space Canvas: the whole
    /// game is primitives already, a Canvas per enemy costs an extra batch each, and the project has
    /// previously been bitten by UGUI Images silently ignoring fillAmount when they have no sprite.
    /// Scaling a quad has none of those failure modes.
    ///
    /// Geometry is authored in PrefabFactory; this component only animates it.
    /// </summary>
    public class EnemyPostureBar : MonoBehaviour
    {
        [Tooltip("Root that billboards toward the camera. Usually this transform.")]
        public Transform billboard;
        [Tooltip("Scaled on X from 0..1 to fill the bar. Its quad child is offset so it grows rightward.")]
        public Transform fillPivot;
        public Renderer fillRenderer;
        public Renderer backgroundRenderer;

        [Header("Placement (local units; the enemy root is already scaled by EnemyData.scale)")]
        public float heightAboveHead = 0.6f;
        [Tooltip("Local Y of the top of the body capsule. Bar sits this + heightAboveHead.")]
        public float headHeight = 2f;

        [Header("Feel")]
        public float fillLerpSpeed = 12f;
        [Tooltip("Posture below this (and not staggered) hides the bar so idle enemies stay uncluttered.")]
        public float hideBelowRatio = 0.02f;

        static readonly Color Bone = new Color(0.788f, 0.635f, 0.153f);   // #C9A227
        static readonly Color Hot = new Color(1f, 0.227f, 0.102f);        // #FF3A1A
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Posture posture;
        EnemyController enemy;
        MaterialPropertyBlock mpb;
        Transform cam;
        float shown;          // smoothed fill
        float flash;          // 0..1 white flash on break
        bool visible = true;

        void Awake()
        {
            if (billboard == null) billboard = transform;
            mpb = new MaterialPropertyBlock();
            enemy = GetComponentInParent<EnemyController>();
            posture = enemy != null ? enemy.GetComponent<Posture>() : GetComponentInParent<Posture>();
            transform.localPosition = new Vector3(0f, headHeight + heightAboveHead, 0f);
            SetVisible(false);
        }

        void OnEnable()
        {
            if (posture == null) return;
            posture.OnBroken += OnBroken;
            posture.OnStaggerEnded += OnStaggerEnded;
            posture.OnChanged += OnChanged;
        }

        void OnDisable()
        {
            if (posture == null) return;
            posture.OnBroken -= OnBroken;
            posture.OnStaggerEnded -= OnStaggerEnded;
            posture.OnChanged -= OnChanged;
        }

        void OnChanged(float cur, float max) { /* value is read in LateUpdate; this keeps the bar responsive to Configure() */ }
        void OnBroken() { flash = 1f; }
        void OnStaggerEnded() { flash = 0.4f; }

        void LateUpdate()
        {
            if (posture == null) return;

            // Dead enemies should not leave a bar hanging in the air.
            if (enemy != null && !enemy.IsAlive) { SetVisible(false); return; }

            float ratio = posture.Ratio;
            bool broken = posture.IsBroken;
            bool wantVisible = broken || ratio > hideBelowRatio;
            SetVisible(wantVisible);
            if (!wantVisible) return;

            float udt = Time.unscaledDeltaTime;
            shown = Mathf.MoveTowards(shown, ratio, Mathf.Max(fillLerpSpeed * udt, Mathf.Abs(ratio - shown) * 10f * udt));
            flash = Mathf.MoveTowards(flash, 0f, udt * 1.5f);

            if (fillPivot != null)
            {
                var s = fillPivot.localScale;
                fillPivot.localScale = new Vector3(Mathf.Clamp01(shown), s.y, s.z);
            }

            // Bone -> hot as it fills; white while breaking; a hard pulse while staggered = "execute now".
            Color c = Color.Lerp(Bone, Hot, Mathf.Clamp01(shown));
            if (broken)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f);
                c = Color.Lerp(Hot, Color.white, 0.35f + 0.65f * pulse);
            }
            c = Color.Lerp(c, Color.white, flash);
            Tint(fillRenderer, c, broken ? 6f : 3f);

            Billboard();
        }

        void Billboard()
        {
            if (cam == null)
            {
                var c = Camera.main;
                if (c == null) return;
                cam = c.transform;
            }
            Vector3 dir = billboard.position - cam.position;
            dir.y = 0f;                                   // stay upright; only yaw toward the player
            if (dir.sqrMagnitude < 0.0001f) return;
            billboard.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        void Tint(Renderer r, Color c, float emission)
        {
            if (r == null) return;
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(EmissionId, c * emission);
            r.SetPropertyBlock(mpb);
        }

        void SetVisible(bool v)
        {
            if (v == visible) return;
            visible = v;
            if (fillRenderer != null) fillRenderer.enabled = v;
            if (backgroundRenderer != null) backgroundRenderer.enabled = v;
        }
    }
}
