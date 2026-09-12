namespace VibeGame1
{
    /// <summary>
    /// THE key reference, in one place. Shown on the settings screen's INFO card (both the pause path and
    /// the title path build it through <c>SettingsPanelKit</c>) and reached from the F1 test menu. It is
    /// deliberately NOT on the playing HUD: the four-line bind dump that used to sit under the clock was
    /// clutter over the world for the whole run, and a reference you read once does not belong where
    /// the fight is. <c>HUDController.hintText</c> keeps one line for contextual hints only.
    ///
    /// <para>Rich text, TextMeshPro. One string, one source: <c>SettingsPrefabTests</c> holds both
    /// prefabs to exactly this text.</para>
    /// </summary>
    public static class ControlsInfo
    {
        const string H = "<color=#D9891A><b>";     // ember section header
        const string HE = "</b></color>";
        const string D = "<alpha=#99>";            // dim key column
        const string DE = "<alpha=#FF>";

        public static string Text { get { return PlayerText + (DeveloperAccess.IsUnlocked ? DeveloperText : ""); } }

        static readonly string PlayerText =
            H + "MOVEMENT" + HE + "\n" +
            D + "WASD" + DE + "  move      " + D + "Mouse" + DE + "  look      " + D + "Space" + DE + "  jump (release early for a short hop)\n" +
            D + "Left Shift" + DE + "  dash      " + D + "Left Ctrl" + DE + "  Slide (needs speed; jump out of it to keep it)\n" +
            D + "Space" + DE + " again near a wall: wall jump      " + D + "no key" + DE + ": arrive airborne along a wall at a jog to wall run\n" +
            D + "Perfect timing" + DE + ": press Space at WALL EXIT or DASH JUMP; the cue marks the real window. A grapple burst fired on landing also refunds stamina\n" +
            "\n" +
            H + "COMBAT" + HE + "\n" +
            D + "LMB" + DE + "  attack      " + D + "RMB tap" + DE + "  parry      " + D + "RMB hold" + DE + "  guard      " + D + "MMB" + DE + "  lock on / switch / release\n" +
            D + "LMB" + DE + " at an enemy carrying the violet marker: deathblow      " + D + "Q" + DE + "  super (needs a full PYRE)\n" +
            "Parry to break their POSTURE, then deathblow. Blocking costs YOUR posture. Every parry stokes PYRE.\n" +
            "\n" +
            H + "ITEMS AND MENUS" + HE + "\n" +
            D + "E" + DE + "  use item      " + D + "F" + DE + "  flask (or CHOOSE WAND at an altar)      " + D + "1 2 3 / wheel" + DE + "  weapon      " + D + "Tab" + DE + "  level up      " + D + "Esc" + DE + "  pause\n" +
            D + "F11" + DE + "  weapon flourish (cosmetic - rebind it under SETTINGS > CONTROL > FLOURISH KEY)\n" +
            D + "Backquote" + DE + "  command console";

        static readonly string DeveloperText =
            "\n\n" +
            H + "LEVEL EDITOR" + HE + "  (session access granted - F10, or F1 > LEVEL EDITOR; see docs/LEVEL-EDITOR.md)\n" +
            D + "WASD / Space / Ctrl / Shift" + DE + "  fly, up, down, fast      " + D + "Tab" + DE + "  toggle the cursor (free on enter)\n" +
            D + "LMB tap" + DE + "  place      " + D + "LMB hold 0.18 s" + DE + "  grab and carry a piece      " + D + "RMB / X" + DE + "  delete the aimed piece\n" +
            D + "Wheel" + DE + "  size      " + D + "Shift + wheel / [ ]" + DE + "  piece kind      " + D + "Ctrl + wheel / V" + DE + "  variant      " + D + "T" + DE + "  rotate 90 deg\n" +
            "Panel: NEW  SAVE  LOAD  PLAY  EXPORT ASSET  EXIT.   Files: AppData/LocalLow/vibegame1/vibegame1/levels\n" +
            "\n" +
            H + "DEV KEYS" + HE + "  (session access granted)\n" +
            D + "F1" + DE + "  test menu      " + D + "F5" + DE + "  warp to boss      " + D + "F6" + DE + "  full restore      " + D + "F7" + DE + "  +1000 souls      " + D + "F8" + DE + "  god mode      " + D + "4" + DE + "  dev blade      " + D + "R" + DE + "  cycle wand";
    }
}
