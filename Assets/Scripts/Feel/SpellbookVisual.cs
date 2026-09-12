using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Persistent, presentation-only offhand spellbook. The book is built once by
    /// <c>SpellbookFactory</c> and remains in the hand whether the player has an item or not; runtime
    /// inventory updates only choose the floating orb's hue.
    ///
    /// <para>The book has no gameplay authority. Consumers capture an accepted cast, request its pose,
    /// then restore it. Keeping that seam here means an inventory update during a cast cannot replace,
    /// shrink, or recolour the already-committed visual.</para>
    ///
    /// <para>All persistent geometry is supplied by the prefab. This component allocates its one
    /// property block in Awake and only writes cached transforms/renderers thereafter. Motion uses
    /// <see cref="TimeScaleController.PlayerDelta"/> so a player-held prop continues through hitstop
    /// without running while the player is paused.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class SpellbookVisual : MonoBehaviour
    {
        /// <summary>The maximum channel emitted by the resting orb. It deliberately remains below the
        /// 1.05 bloom cap: carried magic is a near-field identity read, never an attack tell.</summary>
        public const float OrbEmissionPeak = 0.94f;
        public const float DefaultCastSeconds = 0.32f;

        [Header("Generated anchors")]
        public Transform bookRoot;
        /// <summary>Hand anchor. Its Grip* name is consumed by the existing viewmodel hand solver.</summary>
        public Transform gripAnchor;
        /// <summary>Where a later cast effect originates. Deliberately camera-left of the aim lane.</summary>
        public Transform castOrigin;
        public Transform orbAnchor;

        [Header("Generated geometry")]
        public Renderer[] orbRenderers;
        public Transform[] pagePivots;
        public Vector3[] pageRestEuler;
        public Transform[] floatingPagePivots;
        public Vector3[] floatingPageOrigins;

        [Header("Authored motion")]
        public float pageFlutterDegrees = 7f;
        public float pageFlutterSpeed = 2.2f;
        public float floatingPageRadius = 0.018f;
        public float floatingPageSpeed = 1.45f;
        public float orbBobMetres = 0.012f;
        public float orbBobSpeed = 2.15f;
        public Vector3 castBookOffset = new Vector3(0.035f, 0.018f, 0.055f);
        public Vector3 castBookEuler = new Vector3(-8f, 10f, -4f);

        [Header("Empty-book read")]
        [ColorUsage(true, true)] public Color emptyOrbColor = new Color(0.45f, 0.35f, 0.72f, 1f);

        public WandData SelectedSpell { get; private set; }
        public ItemData FrontItem { get; private set; }
        public ItemData CapturedCastItem { get; private set; }
        public Color DisplayColor { get; private set; }
        public Color CapturedCastColor { get; private set; }
        public bool IsCasting { get; private set; }
        public Vector3 CastOriginWorldPosition => castOrigin != null ? castOrigin.position : transform.position;

        MaterialPropertyBlock orbBlock;
        Vector3 bookRootRestPosition;
        Quaternion bookRootRestRotation;
        Vector3 orbAnchorRestPosition;
        float phase;
        float castSecondsRemaining;
        float castPose;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            orbBlock = new MaterialPropertyBlock();
            CacheAuthoredTransforms();
            DisplayColor = ResolveDisplayColor(FrontItem, SelectedSpell, emptyOrbColor);
            CapturedCastColor = DisplayColor;
            ApplyOrbColor(DisplayColor, 1f);
        }

        /// <summary>
        /// Selects the permanent spell colour. The selected spell remains the fallback whenever the
        /// FIFO inventory is empty. No part of the book hierarchy is recreated or rescaled.
        /// </summary>
        public void SetSelectedSpell(WandData spell)
        {
            SelectedSpell = spell;
            ApplyLiveColorUnlessCasting();
        }

        /// <summary>
        /// Supplies the front (FIFO) item, if one exists. Its colour takes precedence over the
        /// permanent spell because it is the object the player will spend next.
        /// </summary>
        public void SetFrontItem(ItemData item)
        {
            FrontItem = item;
            ApplyLiveColorUnlessCasting();
        }

        /// <summary>Captures the current visible item/spell hue after a cast has actually been accepted.</summary>
        public void CaptureAcceptedCast()
        {
            CaptureAcceptedCast(FrontItem);
        }

        /// <summary>
        /// Captures an explicit accepted item. Passing the item that was accepted is intentional: the
        /// caller may remove it from the FIFO immediately after this call without the cast snapping to
        /// the next item in the book.
        /// </summary>
        public void CaptureAcceptedCast(ItemData acceptedItem)
        {
            CapturedCastItem = acceptedItem;
            CapturedCastColor = acceptedItem != null
                ? acceptedItem.color
                : ResolveDisplayColor(null, SelectedSpell, emptyOrbColor);
        }

        /// <summary>
        /// Starts the short, visual-only cast presentation. It contains no coroutine, spawned VFX, or
        /// gameplay effect; a later combat/item caller owns those consequences.
        /// </summary>
        public void PlayCastPose(float seconds = DefaultCastSeconds)
        {
            if (CapturedCastColor.a <= 0f) CaptureAcceptedCast();
            IsCasting = true;
            castSecondsRemaining = Mathf.Max(0.01f, seconds);
            castPose = 1f;
            ApplyOrbColor(CapturedCastColor, 1.18f);
        }

        /// <summary>Explicit cancellation/settle path for interrupted casts and owner teardown.</summary>
        public void CancelAndRestore()
        {
            IsCasting = false;
            castSecondsRemaining = 0f;
            CapturedCastItem = null;
            CapturedCastColor = ResolveDisplayColor(FrontItem, SelectedSpell, emptyOrbColor);
            ApplyLiveColorUnlessCasting();
        }

        /// <summary>Alias for integrations that finish a successful cast instead of cancelling one.</summary>
        public void RestoreAfterCast()
        {
            CancelAndRestore();
        }

        /// <summary>
        /// The colour rule is pure so editor tests can prove the FIFO priority without a player scene.
        /// HDR source colours are preserved here; <see cref="OrbEmission"/> applies the render cap.
        /// </summary>
        public static Color ResolveDisplayColor(ItemData frontItem, WandData selectedSpell, Color fallback)
        {
            if (frontItem != null) return frontItem.color;
            if (selectedSpell != null) return selectedSpell.color;
            return fallback;
        }

        /// <summary>Normalises an arbitrary item/spell colour into the resting-orb light budget.</summary>
        public static Color OrbEmission(Color source, float multiplier = 1f)
        {
            float max = Mathf.Max(0.0001f, Mathf.Max(source.r, Mathf.Max(source.g, source.b)));
            // A cast may approach the cap sooner, but carried magic may never cross it. The book is
            // near-field presentation, not a licensed bloom exception like a projectile or flare.
            float peak = Mathf.Min(OrbEmissionPeak, OrbEmissionPeak * Mathf.Max(0f, multiplier));
            Color result = new Color(source.r / max * peak, source.g / max * peak, source.b / max * peak, 1f);
            return result;
        }

        void CacheAuthoredTransforms()
        {
            if (bookRoot != null)
            {
                bookRootRestPosition = bookRoot.localPosition;
                bookRootRestRotation = bookRoot.localRotation;
            }
            if (orbAnchor != null) orbAnchorRestPosition = orbAnchor.localPosition;
        }

        void ApplyLiveColorUnlessCasting()
        {
            if (IsCasting) return;
            DisplayColor = ResolveDisplayColor(FrontItem, SelectedSpell, emptyOrbColor);
            CapturedCastColor = DisplayColor;
            ApplyOrbColor(DisplayColor, 1f);
        }

        void ApplyOrbColor(Color hue, float multiplier)
        {
            if (orbRenderers == null) return;
            if (orbBlock == null) orbBlock = new MaterialPropertyBlock();
            Color emission = OrbEmission(hue, multiplier);
            orbBlock.SetColor(EmissionId, emission);
            orbBlock.SetColor(BaseColorId, Color.black);
            for (int i = 0; i < orbRenderers.Length; i++)
                if (orbRenderers[i] != null) orbRenderers[i].SetPropertyBlock(orbBlock);
        }

        void LateUpdate()
        {
            float dt = TimeScaleController.PlayerDelta;
            if (dt <= 0f) return;

            phase += dt;
            if (IsCasting)
            {
                castSecondsRemaining -= dt;
                if (castSecondsRemaining <= 0f) CancelAndRestore();
            }
            castPose = Mathf.MoveTowards(castPose, IsCasting ? 1f : 0f, dt * 8f);

            AnimateBook(castPose);
            AnimatePages();
            AnimateOrb();
        }

        void AnimateBook(float pose)
        {
            if (bookRoot == null) return;
            // Only the generated BookRoot poses. The prefab's root scale is never written at runtime.
            bookRoot.localPosition = Vector3.Lerp(bookRootRestPosition, bookRootRestPosition + castBookOffset, pose);
            bookRoot.localRotation = Quaternion.Slerp(bookRootRestRotation,
                bookRootRestRotation * Quaternion.Euler(castBookEuler), pose);
        }

        void AnimatePages()
        {
            if (pagePivots != null && pageRestEuler != null)
            {
                int count = Mathf.Min(pagePivots.Length, pageRestEuler.Length);
                for (int i = 0; i < count; i++)
                {
                    Transform page = pagePivots[i];
                    if (page == null) continue;
                    float flutter = Mathf.Sin(phase * pageFlutterSpeed + i * 0.91f) * pageFlutterDegrees;
                    Vector3 euler = pageRestEuler[i];
                    euler.z += flutter;
                    euler.y += Mathf.Sin(phase * (pageFlutterSpeed * 0.61f) + i) * pageFlutterDegrees * 0.22f;
                    page.localRotation = Quaternion.Euler(euler);
                }
            }

            if (floatingPagePivots == null || floatingPageOrigins == null) return;
            int floatingCount = Mathf.Min(floatingPagePivots.Length, floatingPageOrigins.Length);
            for (int i = 0; i < floatingCount; i++)
            {
                Transform page = floatingPagePivots[i];
                if (page == null) continue;
                float a = phase * (floatingPageSpeed + i * 0.13f) + i * 2.08f;
                Vector3 origin = floatingPageOrigins[i];
                page.localPosition = origin + new Vector3(Mathf.Cos(a) * floatingPageRadius,
                    Mathf.Sin(a * 1.37f) * floatingPageRadius * 0.65f,
                    Mathf.Sin(a) * floatingPageRadius * 0.5f);
                page.localRotation = Quaternion.Euler(0f, 18f * Mathf.Sin(a), 18f * Mathf.Cos(a * 1.2f));
            }
        }

        void AnimateOrb()
        {
            if (orbAnchor == null) return;
            orbAnchor.localPosition = orbAnchorRestPosition + Vector3.up * (Mathf.Sin(phase * orbBobSpeed) * orbBobMetres);
            orbAnchor.localRotation = Quaternion.Euler(0f, phase * 60f, 0f);
            // Breath is size-neutral: it changes the restrained emission instead of the book silhouette.
            float breath = 0.84f + 0.16f * Mathf.Sin(phase * orbBobSpeed * 1.6f);
            ApplyOrbColor(IsCasting ? CapturedCastColor : DisplayColor, breath * (IsCasting ? 1.18f : 1f));
        }
    }
}
