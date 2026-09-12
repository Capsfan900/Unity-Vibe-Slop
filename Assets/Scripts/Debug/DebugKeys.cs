using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Developer hotkeys. Lives on the Managers prefab and remains inert in every build until the
    /// command console grants <see cref="DeveloperAccess"/> for the current process.
    ///   F5  warp to the boss arena entrance and equip the test blade (slot 4)
    ///   F6  full heal, refill flasks, fill the Pyre meter
    ///   F7  +1000 souls
    ///   F8  toggle god mode (invulnerable)
    ///   F9  toggle the wall-run diagnostic readout (why the last wall run did / did not start)
    ///
    /// <para>The weapon flourish used to live here on F11. It is a real player action now — the key is
    /// rebindable on the settings screen — so <c>WeaponTwirl</c> ships on the Player prefab and polls
    /// InputReader itself.</para>
    /// </summary>
    public class DebugKeys : MonoBehaviour
    {
        static readonly Vector3 LegacyArenaEntrance = new Vector3(0f, 18.2f, 375f);
        const int TestWeaponSlot = 3;

        Coroutine promptRoutine;

        void Update()
        {
            if (!DeveloperAccess.IsUnlocked || !GameManager.IsPlaying || InputReader.I == null) return;
            var input = InputReader.I;

            if (input.DebugWarpBossPressed) WarpToBoss();
            else if (input.DebugRestorePressed) Restore();
            else if (input.DebugSoulsPressed) GiveSouls();
            else if (input.DebugGodModePressed) ToggleGodMode();
            else if (input.DebugWallRunDiagPressed) ToggleWallRunDiag();

            if (wallDiagOn) UpdateWallRunDiag();
        }

        PlayerCombat Player() => FindAnyObjectByType<PlayerCombat>();

        void WarpToBoss()
        {
            var player = Player();
            if (player == null || LevelManager.I == null) return;

            // Registers the boss checkpoint as the respawn point (also heals / refills), then step to the
            // gate. Checkpoint_4 is the boss tile: one checkpoint per tile, so this is the last of four.
            LevelManager.I.Warp("Checkpoint_4");
            var motor = player.GetComponent<FirstPersonMotor>();
            var look = player.GetComponent<PlayerLook>();
            var portal = SolarArenaPortal.FindFinalBossPortal();
            BossArenaTrigger legacyArena = null;
            if (portal == null)
                foreach (var candidate in FindObjectsByType<BossArenaTrigger>())
                    if (candidate.clearSpawner == null) { legacyArena = candidate; break; }
            Vector3 entrance = portal != null && portal.worldRetry != null ? portal.worldRetry.position
                : legacyArena != null ? legacyArena.transform.position : LegacyArenaEntrance;
            float yaw = portal != null && portal.worldRetry != null
                ? portal.worldRetry.eulerAngles.y : 0f;
            if (motor != null) motor.Teleport(entrance, yaw);
            if (look != null) look.SetYaw(yaw);

            var weapons = player.GetComponent<WeaponController>();
            if (weapons != null && weapons.loadout != null && weapons.loadout.Length > TestWeaponSlot)
                weapons.Equip(TestWeaponSlot);

            Say("DEBUG: WARPED TO BOSS ARENA  (TEST BLADE EQUIPPED)");
        }

        void Restore()
        {
            var player = Player();
            if (player == null) return;
            player.Health.ResetFull();
            var res = player.GetComponent<PlayerResources>();
            if (res != null) { res.RefillFlask(); res.AddPyre(1000f); }
            var wc = player.GetComponent<WandController>();
            if (wc != null) wc.ResetCooldown();
            Say("DEBUG: HEALED / FLASKS / PYRE / WAND");
        }

        void GiveSouls()
        {
            if (SoulsWallet.I == null) return;
            SoulsWallet.I.Add(1000);
            Say("DEBUG: +1000 SOULS");
        }

        void ToggleGodMode()
        {
            var player = Player();
            if (player == null) return;
            player.Health.Invulnerable = !player.Health.Invulnerable;
            var stamina = player.GetComponent<PlayerStamina>();
            if (stamina != null) stamina.Infinite = player.Health.Invulnerable;
            Say(player.Health.Invulnerable ? "DEBUG: GOD MODE ON (infinite stamina)" : "DEBUG: GOD MODE OFF");
        }

        // ---- WALL RUN DIAGNOSTIC (F9) ----------------------------------------------------------------
        // Live one-line readout of FirstPersonMotor.WallRunDiag on the prompt channel, e.g.
        //   WALL: refused TooSlow  v=5.8  approach=0.31  look=0.62  wall=yes  stamina=41
        // The string is rebuilt only when what it would SHOW changes (reason, wall, the rounded numbers),
        // so a steady state costs nothing per frame, and the console gets one line per reason change so
        // the history of an attempt survives the readout. Everything below is editor / dev-build only.
        static readonly string[] RejectNames = System.Enum.GetNames(typeof(WallRunReject));
        const float DiagStaleAfter = 0.25f;   // s since the motor last recorded a verdict

        readonly System.Text.StringBuilder wallDiagSb = new System.Text.StringBuilder(128);
        bool wallDiagOn;
        FirstPersonMotor wallDiagMotor;
        // What the readout last showed, quantised exactly the way it is printed.
        bool shownRunning, shownWall, shownStale;
        int shownReason = -1, shownSpeed10, shownApproach100, shownLook100, shownStamina;
        int loggedReason = -1;

        void ToggleWallRunDiag()
        {
            wallDiagOn = !wallDiagOn;
            shownReason = -1;      // force a repaint on the next frame
            loggedReason = -1;
            wallDiagMotor = null;
            if (wallDiagOn)
            {
                // Not Say(): its 1.2 s coroutine would blank the live readout. Kill any pending one too.
                if (promptRoutine != null) { StopCoroutine(promptRoutine); promptRoutine = null; }
                Debug.Log("[DebugKeys] WALL RUN DIAG ON (F9 again to hide)");
            }
            else Say("DEBUG: WALL RUN DIAG OFF");
        }

        void UpdateWallRunDiag()
        {
            if (wallDiagMotor == null)
            {
                var player = Player();
                wallDiagMotor = player != null ? player.GetComponent<FirstPersonMotor>() : null;
                if (wallDiagMotor == null) return;
            }

            var d = wallDiagMotor.WallRunDiag;
            bool running = wallDiagMotor.IsWallRunning;
            bool stale = !running && wallDiagMotor.MotorTime - d.time > DiagStaleAfter;
            int reason = (int)d.reason;
            int speed10 = Mathf.RoundToInt(d.flatSpeed * 10f);
            int approach100 = Quantise(d.approachCos, 100f);
            int look100 = Quantise(d.lookCos, 100f);
            int stamina = Mathf.RoundToInt(d.stamina);
            if (running == shownRunning && stale == shownStale && reason == shownReason
                && d.wallFound == shownWall && speed10 == shownSpeed10
                && approach100 == shownApproach100 && look100 == shownLook100 && stamina == shownStamina)
                return;
            shownRunning = running; shownStale = stale; shownReason = reason; shownWall = d.wallFound;
            shownSpeed10 = speed10; shownApproach100 = approach100; shownLook100 = look100;
            shownStamina = stamina;

            var sb = wallDiagSb;
            sb.Length = 0;
            sb.Append("WALL: ");
            if (running) sb.Append("RUNNING");
            else if (d.reason == WallRunReject.None) sb.Append("entered");
            else
            {
                sb.Append("refused ");
                sb.Append(reason >= 0 && reason < RejectNames.Length ? RejectNames[reason] : "?");
            }
            if (stale) sb.Append(" (stale)");
            sb.Append("  v=");        AppendFixed1(sb, speed10);
            sb.Append("  approach="); AppendCos(sb, approach100);
            sb.Append("  look=");     AppendCos(sb, look100);
            sb.Append("  wall=").Append(d.wallFound ? "yes" : "no");
            sb.Append("  stamina=");
            if (stamina < 0) sb.Append('-'); else sb.Append(stamina);

            string text = sb.ToString();
            GameEvents.RaisePromptChanged(PromptOwner.Debug, text);
            if (reason != loggedReason)
            {
                loggedReason = reason;
                Debug.Log("[DebugKeys] " + text);
            }
        }

        static int Quantise(float v, float scale) => float.IsNaN(v) ? int.MinValue : Mathf.RoundToInt(v * scale);

        /// <summary>Two decimals from a value pre-scaled by 100; "-" for the NaN sentinel.</summary>
        static void AppendCos(System.Text.StringBuilder sb, int q100)
        {
            if (q100 == int.MinValue) { sb.Append('-'); return; }
            if (q100 < 0) { sb.Append('-'); q100 = -q100; }
            sb.Append(q100 / 100).Append('.');
            int frac = q100 % 100;
            if (frac < 10) sb.Append('0');
            sb.Append(frac);
        }

        /// <summary>One decimal from a value pre-scaled by 10.</summary>
        static void AppendFixed1(System.Text.StringBuilder sb, int q10)
        {
            if (q10 < 0) { sb.Append('-'); q10 = -q10; }
            sb.Append(q10 / 10).Append('.').Append(q10 % 10);
        }

        void Say(string msg)
        {
            Debug.Log("[DebugKeys] " + msg);
            if (promptRoutine != null) StopCoroutine(promptRoutine);
            promptRoutine = StartCoroutine(PromptCo(msg));
        }

        IEnumerator PromptCo(string msg)
        {
            GameEvents.RaisePromptChanged(PromptOwner.Debug, msg);
            yield return new WaitForSecondsRealtime(1.2f);
            GameEvents.RaisePromptChanged(PromptOwner.Debug, "");
            promptRoutine = null;
        }
    }
}
