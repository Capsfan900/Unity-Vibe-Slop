using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The tag <see cref="LevelPieceFactory"/> leaves on everything it builds: which KIND of piece this
    /// object is and which entry of the level's data it came from. The in-game editor points at an
    /// object, walks up to this component, and edits <em>the data</em>, then rebuilds the object — the
    /// scene is never the source of truth (hard rule 4). Trim bars and other children carry no tag of
    /// their own; a raycast that lands on one finds the platform through the parent chain.
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelPiece : MonoBehaviour
    {
        public LevelPieceKind kind;
        /// <summary>Index into the document's list for this kind (0 for the player start).</summary>
        public int index;

        /// <summary>The tagged piece under a hit collider, or null.</summary>
        public static LevelPiece Find(Transform hit)
        {
            for (var t = hit; t != null; t = t.parent)
            {
                var p = t.GetComponent<LevelPiece>();
                if (p != null) return p;
            }
            return null;
        }
    }
}
