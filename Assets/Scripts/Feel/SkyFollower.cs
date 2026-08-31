using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Pins the sky to the camera's POSITION (never its rotation), which is what makes a 25-unit mesh
    /// behave like a sky at infinity.
    ///
    /// <para>Three problems this solves at once:</para>
    /// <list type="number">
    /// <item>No parallax. A static dome would visibly slide as the player runs the ~200-unit course.</item>
    /// <item>No fog. The sky stays closer than <c>fogStartDistance</c>, so the fog factor is always zero
    /// and the stars keep their colour without depending on any per-material fog keyword — those are
    /// global in URP and cannot be reliably overridden per material.</item>
    /// <item>No far-plane clipping. The gameplay camera's far plane is 300; a dome large enough to
    /// enclose the whole course from every vantage point would be clipped into holes.</item>
    /// </list>
    ///
    /// <para>Position only. Rotating with the camera would drag the eclipse around the sky, and the whole
    /// point is that it sits over the boss arena at the end of the course.</para>
    ///
    /// <para>Runs in play mode only, deliberately: an <c>[ExecuteAlways]</c> transform write would mark
    /// the scene dirty on every editor tick. In the editor the sky simply rests where the builder placed
    /// it, which is correct for scene-view inspection.</para>
    ///
    /// <para>Cost is one transform write per frame, in LateUpdate so the camera has already moved.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class SkyFollower : MonoBehaviour
    {
        Transform cam;

        void LateUpdate()
        {
            // Camera.main is a tagged search; cache it and re-acquire only if the camera is destroyed
            // (scene reload, player respawn). Never call it unconditionally every frame.
            if (cam == null)
            {
                var c = Camera.main;
                if (c == null) return;
                cam = c.transform;
            }
            transform.position = cam.position;
        }
    }
}
