using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The ordered list of levels in the game. One asset, so "what levels exist and in what order" is a
    /// data question rather than a hardcoded scene list.
    ///
    /// Kept separate from <see cref="LevelDefinition"/> deliberately: a definition describes ONE level's
    /// contents, the registry describes the CAMPAIGN. A level can exist and be testable without being in
    /// the campaign yet.
    /// </summary>
    [CreateAssetMenu(menuName = "VibeGame1/Level Registry")]
    public class LevelRegistry : ScriptableObject
    {
        [Tooltip("Campaign order. Sorted by LevelDefinition.orderIndex on access, so drag order here does " +
                 "not silently disagree with the definitions.")]
        public LevelDefinition[] levels = new LevelDefinition[0];

        [Tooltip("Levels unlocked from the start, before anything is completed. At least 1, or the game " +
                 "opens with nothing playable.")]
        [Min(1)] public int initiallyUnlocked = 1;

        /// <summary>Campaign order, nulls stripped, sorted by <c>orderIndex</c>.</summary>
        public LevelDefinition[] Ordered()
        {
            if (levels == null) return new LevelDefinition[0];

            int n = 0;
            for (int i = 0; i < levels.Length; i++) if (levels[i] != null) n++;
            var result = new LevelDefinition[n];
            int w = 0;
            for (int i = 0; i < levels.Length; i++) if (levels[i] != null) result[w++] = levels[i];

            System.Array.Sort(result, (a, b) => a.orderIndex.CompareTo(b.orderIndex));
            return result;
        }

        public LevelDefinition Find(string levelId)
        {
            if (levels == null || string.IsNullOrEmpty(levelId)) return null;
            for (int i = 0; i < levels.Length; i++)
                if (levels[i] != null && levels[i].SafeLevelId == levelId) return levels[i];
            return null;
        }

        /// <summary>Position in campaign order, or -1. Used to decide what a completion unlocks.</summary>
        public int IndexOf(string levelId)
        {
            var ordered = Ordered();
            for (int i = 0; i < ordered.Length; i++)
                if (ordered[i].SafeLevelId == levelId) return i;
            return -1;
        }
    }
}
