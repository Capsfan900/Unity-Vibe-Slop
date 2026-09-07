using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Remembers the authored exterior position of a spawner or pickup that the campaign builder places
    /// inside a solar realm. The exporter reads this marker so an export/rework round trip cannot bake
    /// remote cell coordinates back into the LevelDefinition.
    /// </summary>
    public class SolarRealmPlacement : MonoBehaviour
    {
        public Vector3 authoredPosition;
        public float authoredYaw;
    }
}
