using UnityEngine;
using UnityEngine.InputSystem;

namespace VibeGame1
{
    /// <summary>
    /// The only script that touches the Input System. Reads the project-wide actions asset
    /// (Assets/InputSystem_Actions.inputactions) through InputSystem.actions.
    /// </summary>
    public class InputReader : MonoBehaviour
    {
        public static InputReader I { get; private set; }

        InputAction move, look, jump, dash, attack, parry, heal, ultimate, previous, next,
                    slot1, slot2, slot3, slot4, levelUp, pause,
                    debugWarpBoss, debugRestore, debugSouls, debugGodMode;

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;

            var asset = InputSystem.actions;
            if (asset == null)
            {
                Debug.LogError("[InputReader] No project-wide InputActionAsset assigned.");
                return;
            }
            var map = asset.FindActionMap("Player", true);
            move = map.FindAction("Move", true);
            look = map.FindAction("Look", true);
            jump = map.FindAction("Jump", true);
            dash = map.FindAction("Dash", true);
            attack = map.FindAction("Attack", true);
            parry = map.FindAction("Parry", true);
            heal = map.FindAction("Heal", true);
            ultimate = map.FindAction("Ultimate", true);
            previous = map.FindAction("Previous", true);
            next = map.FindAction("Next", true);
            slot1 = map.FindAction("WeaponSlot1", true);
            slot2 = map.FindAction("WeaponSlot2", true);
            slot3 = map.FindAction("WeaponSlot3", true);
            levelUp = map.FindAction("LevelUpMenu", true);
            pause = map.FindAction("Pause", true);
            // optional actions: missing ones must not crash startup
            slot4 = map.FindAction("WeaponSlot4", false);
            debugWarpBoss = map.FindAction("DebugWarpBoss", false);
            debugRestore = map.FindAction("DebugRestore", false);
            debugSouls = map.FindAction("DebugSouls", false);
            debugGodMode = map.FindAction("DebugGodMode", false);
            map.Enable();
            asset.FindActionMap("UI")?.Enable();
        }

        public Vector2 MoveAxis => move != null ? move.ReadValue<Vector2>() : Vector2.zero;
        public Vector2 LookDelta => look != null ? look.ReadValue<Vector2>() : Vector2.zero;
        public bool LookIsMouse => look != null && look.activeControl != null && look.activeControl.device is Mouse;

        public bool JumpPressed => jump != null && jump.WasPressedThisFrame();
        public bool JumpHeld => jump != null && jump.IsPressed();
        public bool DashPressed => dash != null && dash.WasPressedThisFrame();
        public bool AttackPressed => attack != null && attack.WasPressedThisFrame();
        public bool ParryPressed => parry != null && parry.WasPressedThisFrame();
        public bool HealPressed => heal != null && heal.WasPressedThisFrame();
        public bool UltimatePressed => ultimate != null && ultimate.WasPressedThisFrame();
        public bool PrevPressed => previous != null && previous.WasPressedThisFrame();
        public bool NextPressed => next != null && next.WasPressedThisFrame();
        public bool LevelUpPressed => levelUp != null && levelUp.WasPressedThisFrame();
        public bool PausePressed => pause != null && pause.WasPressedThisFrame();

        // Debug keys (F5-F8). Consumed by DebugKeys in editor / development builds only.
        public bool DebugWarpBossPressed => debugWarpBoss != null && debugWarpBoss.WasPressedThisFrame();
        public bool DebugRestorePressed => debugRestore != null && debugRestore.WasPressedThisFrame();
        public bool DebugSoulsPressed => debugSouls != null && debugSouls.WasPressedThisFrame();
        public bool DebugGodModePressed => debugGodMode != null && debugGodMode.WasPressedThisFrame();

        /// <summary>0..3 for slot keys pressed this frame, -1 otherwise.</summary>
        public int WeaponSlotPressed
        {
            get
            {
                if (slot1 != null && slot1.WasPressedThisFrame()) return 0;
                if (slot2 != null && slot2.WasPressedThisFrame()) return 1;
                if (slot3 != null && slot3.WasPressedThisFrame()) return 2;
                if (slot4 != null && slot4.WasPressedThisFrame()) return 3;
                return -1;
            }
        }

        public bool AnyMovementInput => MoveAxis.sqrMagnitude > 0.01f || JumpPressed || DashPressed;
    }
}
