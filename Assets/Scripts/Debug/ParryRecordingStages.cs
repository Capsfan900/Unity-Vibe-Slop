namespace VibeGame1
{
    /// <summary>
    /// The four parry-choreography recording stages: one list shared by the editor factory that writes
    /// their LevelDefinition assets and scenes, and the main menu's developer rows that launch them.
    /// Editor-only tooling — the scenes are not part of the playtest build's scene list.
    /// </summary>
    public static class ParryRecordingStages
    {
        public struct Stage
        {
            public string key;          // LevelDefinition asset name under Assets/Data/Levels/Recording/
            public string displayName;
            public string sceneName;    // Assets/Scenes/<sceneName>.unity

            public string ScenePath { get { return "Assets/Scenes/" + sceneName + ".unity"; } }
        }

        public static readonly Stage[] All =
        {
            new Stage { key = "Recording_Flat",     displayName = "Flat Walkway",         sceneName = "ParryRecording_Flat" },
            new Stage { key = "Recording_Downhill", displayName = "Downhill Ramp",        sceneName = "ParryRecording_Downhill" },
            new Stage { key = "Recording_Ice",      displayName = "Ice / Water Slideway", sceneName = "ParryRecording_Ice" },
            new Stage { key = "Recording_Mixed",    displayName = "Mixed Traversal",      sceneName = "ParryRecording_Mixed" },
        };

        public static bool TryGet(string key, out Stage stage)
        {
            foreach (var s in All) if (s.key == key) { stage = s; return true; }
            stage = default(Stage);
            return false;
        }
    }
}
