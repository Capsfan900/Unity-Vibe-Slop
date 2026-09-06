using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Post-build styling passes for the HUD prefab, called once by <see cref="HudBuilder.Build"/> after
    /// every element exists and before the prefab is saved. Each pass finds the bar it owns by NAME on
    /// the finished hierarchy and rewrites it in place (a material, a component swap, extra children),
    /// so the layout in HudBuilder stays a layout and the styling stays here.
    ///
    /// <para>One region per pass, one owner per region. Add a pass by adding a static method in its own
    /// file (a partial of this class or a sibling static class) and one call below.</para>
    /// </summary>
    public static partial class HudExtensions
    {
        public static void ApplyAll(GameObject hudRoot)
        {
            if (hudRoot == null) return;

            // ---- FLUID BARS (health + stamina as flowing, glowing fluid) ------------------------
            ApplyFluidBars(hudRoot);

            // ---- PYRE FIRE (the parry charge meter as a burning loading bar) --------------------
            ApplyPyreFire(hudRoot);
        }
    }
}
