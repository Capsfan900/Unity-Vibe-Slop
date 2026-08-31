using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Developer hotkeys (editor / development builds only). Lives on the Managers prefab.
    ///   F5  warp to the boss arena entrance and equip the test blade (slot 4)
    ///   F6  full heal, refill flasks, fill the Pyre meter
    ///   F7  +1000 souls
    ///   F8  toggle god mode (invulnerable)
    /// </summary>
    public class DebugKeys : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static readonly Vector3 ArenaEntrance = new Vector3(0f, 28.2f, 296f);
        const int TestWeaponSlot = 3;

        Coroutine promptRoutine;

        void Update()
        {
            if (!GameManager.IsPlaying || InputReader.I == null) return;
            var input = InputReader.I;

            if (input.DebugWarpBossPressed) WarpToBoss();
            else if (input.DebugRestorePressed) Restore();
            else if (input.DebugSoulsPressed) GiveSouls();
            else if (input.DebugGodModePressed) ToggleGodMode();
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
            if (motor != null) motor.Teleport(ArenaEntrance, 0f);
            if (look != null) look.SetYaw(0f);

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
            Say(player.Health.Invulnerable ? "DEBUG: GOD MODE ON" : "DEBUG: GOD MODE OFF");
        }

        void Say(string msg)
        {
            Debug.Log("[DebugKeys] " + msg);
            if (promptRoutine != null) StopCoroutine(promptRoutine);
            promptRoutine = StartCoroutine(PromptCo(msg));
        }

        IEnumerator PromptCo(string msg)
        {
            GameEvents.RaisePromptChanged(msg);
            yield return new WaitForSecondsRealtime(1.2f);
            GameEvents.RaisePromptChanged("");
            promptRoutine = null;
        }
#endif
    }
}
