namespace VibeGame1
{
    /// <summary>Pure playlist arithmetic for <see cref="LevelRadio"/>; unit tested in RadioTests.</summary>
    public static class RadioMath
    {
        /// <summary>The next index with wrap; -1 for an empty playlist.</summary>
        public static int Next(int index, int count)
        {
            if (count <= 0) return -1;
            return ((index + 1) % count + count) % count;
        }

        /// <summary>The previous index with wrap; -1 for an empty playlist.</summary>
        public static int Previous(int index, int count)
        {
            if (count <= 0) return -1;
            return ((index - 1) % count + count) % count;
        }

        /// <summary>
        /// A press of PREVIOUS inside the first <paramref name="restartWindow"/> seconds of a track goes to
        /// the previous track; later it restarts the current one (the car-stereo rule).
        /// </summary>
        public static bool PreviousRestartsCurrent(float elapsed, float restartWindow)
        {
            return elapsed > restartWindow;
        }

        /// <summary>0..1 through the current track, safe for a zero-length clip.</summary>
        public static float Progress(float elapsed, float length)
        {
            if (length <= 0f) return 0f;
            float k = elapsed / length;
            return k < 0f ? 0f : (k > 1f ? 1f : k);
        }

        /// <summary>
        /// Whether NEXT/PREVIOUS/TOGGLE should react to the player's keypress this frame. The radio's own
        /// volume fade already runs on unscaled time so the bed keeps playing through pause and hitstop
        /// (<see cref="LevelRadio.Update"/>) — the keys that skip a track were gated to
        /// <c>GameState.Playing</c> only, so PAUSED, LEVEL-UP and DEAD all silently ate the keypress even
        /// though the radio was still audible (found 2026-09-06: "skipping does not work"). The one state
        /// that must still block is <see cref="GameState.Editing"/> — the level editor binds the SAME
        /// physical keys, <c>[</c> and <c>]</c>, to <c>EditorPrev</c>/<c>EditorNext</c> (cycling a piece's
        /// kind); letting the radio also react there would skip a track every time a level author cycles
        /// a piece.
        /// </summary>
        public static bool AcceptsInput(GameState state)
        {
            return state != GameState.Editing;
        }

        /// <summary>"Audio/Radio/<folder>" -- the Resources path a level's playlist is loaded from.</summary>
        public static string ResourcesFolder(string levelFolder)
        {
            string f = string.IsNullOrWhiteSpace(levelFolder) ? "Default" : levelFolder.Trim().Trim('/');
            return "Audio/Radio/" + f;
        }

        /// <summary>
        /// The station name for a scene: "Level_01" → "LEVEL 01". Underscores and dashes read as spaces and
        /// the whole thing is upper-cased, so a scene named for its level reads as a station on the pane.
        /// </summary>
        public static string StationFor(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return "";
            return Title(sceneName).ToUpperInvariant();
        }

        /// <summary>A clip's display title: the file name with underscores and dashes read as spaces.</summary>
        public static string Title(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return "";
            return clipName.Replace('_', ' ').Replace('-', ' ').Trim();
        }
    }
}
