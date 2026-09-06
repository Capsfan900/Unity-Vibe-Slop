using UnityEngine;
using UnityEngine.SceneManagement;

namespace VibeGame1
{
    /// <summary>
    /// The <see cref="LevelDefinition"/> for the scene that is open, found through the shipped
    /// <see cref="LevelRegistry"/> asset by scene name. Null in the sandbox or any scene the registry does
    /// not list. Cached per scene.
    /// </summary>
    public static class LevelRegistryRuntime
    {
        static LevelDefinition cached;
        static string cachedScene;

        public static LevelDefinition Current
        {
            get
            {
                string scene = SceneManager.GetActiveScene().name;
                if (cached != null && cachedScene == scene) return cached;
                cached = null; cachedScene = scene;
                var reg = Resources.Load<LevelRegistry>("LevelRegistry");
                if (reg != null && reg.levels != null)
                    foreach (var d in reg.levels)
                        if (d != null && d.sceneName == scene) { cached = d; break; }
                return cached;
            }
        }
    }
}
