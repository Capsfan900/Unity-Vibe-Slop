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
        InputAction useItem, testMenu, wandCycle, interact, lockOn, slide;

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
            useItem = map.FindAction("UseItem", false);
            testMenu = map.FindAction("TestMenu", false);
            wandCycle = map.FindAction("WandCycle", false);
            interact = map.FindAction("Interact", false);
            lockOn = map.FindAction("LockOn", false);
            slide = map.FindAction("Slide", false);
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
        /// <summary>The physical left mouse button, this frame, regardless of action maps. Exists so
        /// PlayerLook can re-lock the cursor inside a user gesture (WebGL) without touching the Input
        /// System itself — this class is the only one that does (hard rule 2).</summary>
        public bool MouseClickedThisFrame => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        public bool JumpPressed => jump != null && jump.WasPressedThisFrame();
        public bool JumpHeld => jump != null && jump.IsPressed();
        public bool DashPressed => dash != null && dash.WasPressedThisFrame();
        public bool AttackPressed => attack != null && attack.WasPressedThisFrame();
        public bool ParryPressed => parry != null && parry.WasPressedThisFrame();

        /// <summary>
        /// RMB held: the Sekiro GUARD stance. Same button and same binding as <see cref="ParryPressed"/>
        /// by design — in Sekiro the deflect is not a different input from the guard, it is the guard
        /// pressed at the right moment. <see cref="ParryController"/> reads the press for the window and
        /// this for the stance underneath it.
        /// </summary>
        public bool ParryHeld => parry != null && parry.IsPressed();
        public bool HealPressed => heal != null && heal.WasPressedThisFrame();
        public bool UltimatePressed => ultimate != null && ultimate.WasPressedThisFrame();
        public bool PrevPressed => previous != null && previous.WasPressedThisFrame();
        public bool NextPressed => next != null && next.WasPressedThisFrame();
        public bool LevelUpPressed => levelUp != null && levelUp.WasPressedThisFrame();
        public bool PausePressed => pause != null && pause.WasPressedThisFrame();
        public bool UseItemPressed => useItem != null && useItem.WasPressedThisFrame();
        public bool TestMenuPressed => testMenu != null && testMenu.WasPressedThisFrame();
        public bool WandCyclePressed => wandCycle != null && wandCycle.WasPressedThisFrame();

        /// <summary>
        /// Left Ctrl (or gamepad left trigger): the momentum slide. Checked against the whole map before
        /// binding — <c>C</c> looked free but still carries the template's unread <c>Crouch</c> action,
        /// and giving one key two actions is exactly the bug that made <c>F</c> fire the flask and the
        /// wand altar together. Left Ctrl and left trigger were bound to nothing at all.
        /// Held, not tapped: <see cref="FirstPersonMotor"/> ends the slide when this goes false.
        /// </summary>
        public bool SlidePressed => slide != null && slide.WasPressedThisFrame();
        public bool SlideHeld => slide != null && slide.IsPressed();

        /// <summary>
        /// Middle mouse (or right stick click): Souls target lock. One key acquires, switches and
        /// releases — <see cref="LockOnController.TryLockOn"/> resolves which by where you are aiming.
        /// Middle mouse was free; the scroll wheel, the other Souls convention, is Previous/Next weapon
        /// cycling and could not be shared.
        /// </summary>
        public bool LockOnPressed => lockOn != null && lockOn.WasPressedThisFrame();

        /// <summary>F (or gamepad north): deliberate world interaction, e.g. the wand pedestal.</summary>
        public bool InteractPressed => interact != null && interact.WasPressedThisFrame();

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

        public bool AnyMovementInput => MoveAxis.sqrMagnitude > 0.01f || JumpPressed || DashPressed || SlidePressed;
    }
}
