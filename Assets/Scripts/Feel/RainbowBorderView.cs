using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// Feeds the <c>VibeGame1/UI/RainbowBorder</c> material the two things a UI shader cannot know for
    /// itself: the rect's aspect and an UNSCALED clock.
    ///
    /// <para><b>Why the aspect.</b> The shader lays hue out along the rect's PERIMETER by arc length. To
    /// do that it has to know how wide the rect is relative to its height, or the band is thicker on the
    /// short ends and the swirl races across them. The radio pane is 300 x 116, but the pane is anchored,
    /// so the value is read from the RectTransform every frame rather than hardcoded.</para>
    ///
    /// <para><b>Why unscaled time (the deliberate choice).</b> <c>_Time</c> inside a shader is SCALED, so
    /// it stops dead whenever <c>TimeScaleController</c> zeroes the world -- hitstop and the pause menu.
    /// The radio keeps playing through both, so a frame that froze would read as the HUD having crashed
    /// rather than as the world holding its breath. The cloud sea freezes with the world on purpose
    /// because it IS the world; this is HUD furniture next to a thing that is still making sound.
    /// Sibling precedent: <c>FireBarView</c> hands the Pyre fire <c>Time.unscaledTime</c> for the same
    /// reason. This component never writes <c>Time.timeScale</c> and never reads gameplay state.</para>
    ///
    /// <para><b>Per-instance material, never the shared asset.</b> Awake clones the graphic's material so
    /// the per-frame writes cannot dirty <c>M_RadioAura</c> on disk, and OnDestroy disposes the clone.</para>
    ///
    /// <para><b>Inert when unwired.</b> No graphic, no material, or a material on some other shader: the
    /// component simply does nothing. It is a decoration and may never throw into a HUD frame.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class RainbowBorderView : MonoBehaviour
    {
        [Tooltip("The Image the border is drawn on. Left null, the Graphic on this object is used. " +
                 "Its material must use VibeGame1/UI/RainbowBorder.")]
        public Graphic border;

        /// <summary>The shader name this view is allowed to drive. Anything else is left alone.</summary>
        public const string ShaderName = "VibeGame1/UI/RainbowBorder";

        /// <summary>Aspect used when the rect has no usable height yet (the shipped radio pane, 300/116).</summary>
        public const float FallbackAspect = 300f / 116f;

        static readonly int TId = Shader.PropertyToID("_T");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");

        Material mat;
        RectTransform rect;

        /// <summary>The aspect handed to the shader this frame. For tests.</summary>
        public float Aspect { get; private set; }

        void Awake()
        {
            if (border == null) border = GetComponent<Graphic>();
            if (border == null) return;
            rect = border.rectTransform;
            var source = border.material;
            if (source == null || source.shader == null || source.shader.name != ShaderName) return;
            mat = new Material(source) { name = source.name + " (instance)" };
            border.material = mat;
            Aspect = FallbackAspect;
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }

        void LateUpdate()
        {
            if (mat == null) return;
            Aspect = AspectOf(rect);
            mat.SetFloat(AspectId, Aspect);
            mat.SetFloat(TId, Time.unscaledTime);
        }

        /// <summary>
        /// Width over height of a rect, falling back to the shipped pane's ratio when the rect is
        /// missing or degenerate (a layout that has not run yet would otherwise divide by zero).
        /// </summary>
        public static float AspectOf(RectTransform target)
        {
            if (target == null) return FallbackAspect;
            Rect r = target.rect;
            if (r.height <= 0.001f || r.width <= 0.001f) return FallbackAspect;
            return r.width / r.height;
        }
    }
}
