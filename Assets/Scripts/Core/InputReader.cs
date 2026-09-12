using System;
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
                    debugWarpBoss, debugRestore, debugSouls, debugGodMode, debugWallRunDiag,
                    weaponTwirl, consoleToggle, consoleSubmit;
        InputAction useItem, testMenu, wandCycle, interact, lockOn, slide;
        // The in-game level editor (LevelEditor). Optional: a map without them must not crash startup.
        InputAction levelEditor, editorPlace, editorDelete, editorGrab, editorRotate, editorGrow, editorShrink,
                    editorPrev, editorNext, editorVariant, editorCursor, editorFree, editorFast, editorDown,
                    editorWheelUp, editorWheelDown, editorUndo, editorRedo, editorDuplicate, editorCancel, editorPlaceHere, editorPick;
        // The radio (LevelRadio). Optional like the editor actions.
        InputAction radioNext, radioPrevious, radioToggle;
        int pauseSuppressedFrame = -1;

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
            debugWallRunDiag = map.FindAction("DebugWallRunDiag", false);
            weaponTwirl = map.FindAction(WeaponTwirlActionName, false);
            consoleToggle = map.FindAction(ConsoleToggleActionName, false);
            consoleSubmit = map.FindAction(ConsoleSubmitActionName, false);
            levelEditor = map.FindAction("LevelEditor", false);
            editorPlace = map.FindAction("EditorPlace", false);
            editorDelete = map.FindAction("EditorDelete", false);
            editorGrab = map.FindAction("EditorGrab", false);
            editorRotate = map.FindAction("EditorRotate", false);
            editorGrow = map.FindAction("EditorGrow", false);
            editorShrink = map.FindAction("EditorShrink", false);
            editorPrev = map.FindAction("EditorPrev", false);
            editorNext = map.FindAction("EditorNext", false);
            editorVariant = map.FindAction("EditorVariant", false);
            editorCursor = map.FindAction("EditorCursor", false);
            editorFree = map.FindAction("EditorFree", false);
            editorFast = map.FindAction("EditorFast", false);
            editorDown = map.FindAction("EditorDown", false);
            editorWheelUp = map.FindAction("EditorWheelUp", false);
            editorWheelDown = map.FindAction("EditorWheelDown", false);
            editorUndo = map.FindAction("EditorUndo", false);
            editorRedo = map.FindAction("EditorRedo", false);
            editorDuplicate = map.FindAction("EditorDuplicate", false);
            editorCancel = map.FindAction("EditorCancel", false);
            editorPlaceHere = map.FindAction("EditorPlaceHere", false);
            editorPick = map.FindAction("EditorPick", false);
            radioNext = map.FindAction("RadioNext", false);
            radioPrevious = map.FindAction("RadioPrevious", false);
            radioToggle = map.FindAction("RadioToggle", false);
            // The player's saved key, before anything can press it. SettingsApplier pushes it again on
            // every scene load; doing it here as well means a level whose InputReader wakes before the
            // applier's deferred pass still starts on the right binding rather than on F11 for a frame.
            ApplyWeaponTwirlOverride(SettingsStore.Current.weaponTwirlBinding);

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
        /// <summary>Screen-space pointer position, for a free cursor's aim (the level editor).</summary>
        public Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

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
        public bool PausePressed => pause != null && pause.WasPressedThisFrame() && pauseSuppressedFrame != Time.frameCount;
        /// <summary>The level editor spent Escape on a cancel this frame (a grab, a press): the pause menu must not
        /// also open on it. The editor runs before the menu (DefaultExecutionOrder), so the flag is seen in time.</summary>
        public void SuppressPauseThisFrame() { pauseSuppressedFrame = Time.frameCount; }
        public bool UseItemPressed => useItem != null && useItem.WasPressedThisFrame();
        public bool TestMenuPressed => DeveloperAccess.IsUnlocked && testMenu != null && testMenu.WasPressedThisFrame();
        public bool WandCyclePressed => DeveloperAccess.IsUnlocked && wandCycle != null && wandCycle.WasPressedThisFrame();

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

        // Debug keys (F5-F9). Every build routes them through one process-local console grant.
        public bool DebugWarpBossPressed => DeveloperAccess.IsUnlocked && debugWarpBoss != null && debugWarpBoss.WasPressedThisFrame();
        public bool DebugRestorePressed => DeveloperAccess.IsUnlocked && debugRestore != null && debugRestore.WasPressedThisFrame();
        public bool DebugSoulsPressed => DeveloperAccess.IsUnlocked && debugSouls != null && debugSouls.WasPressedThisFrame();
        public bool DebugGodModePressed => DeveloperAccess.IsUnlocked && debugGodMode != null && debugGodMode.WasPressedThisFrame();
        public bool DebugWallRunDiagPressed => DeveloperAccess.IsUnlocked && debugWallRunDiag != null && debugWallRunDiag.WasPressedThisFrame();

        // ---- the weapon flourish: a real player action, and the one rebindable key ------------------

        /// <summary>The action's name in <c>InputSystem_Actions.inputactions</c>. Looked up optionally,
        /// like every action added after the template, so an older asset never crashes startup.</summary>
        public const string WeaponTwirlActionName = "WeaponTwirl";

        /// <summary>Default F11 (<see cref="SettingsData.WeaponTwirlDefaultBinding"/>): spin the weapon
        /// in hand. Cosmetic. <see cref="WeaponTwirl"/> polls this itself. Held FALSE while the settings
        /// screen is listening for a new key, so the key you just chose does not also fire a flourish.</summary>
        public bool WeaponTwirlPressed => weaponTwirl != null && rebind == null && weaponTwirl.WasPressedThisFrame();

        // ---- command console --------------------------------------------------------------------

        public const string ConsoleToggleActionName = "ConsoleToggle";
        public const string ConsoleSubmitActionName = "ConsoleSubmit";

        /// <summary>Backquote. Always available because it is the only door into the session-only
        /// developer capability; all privileged commands and keys remain inert until that grant.</summary>
        public bool ConsoleTogglePressed => consoleToggle != null && consoleToggle.WasPressedThisFrame();

        /// <summary>Enter while the console input field is focused.</summary>
        public bool ConsoleSubmitPressed => consoleSubmit != null && consoleSubmit.WasPressedThisFrame();

        /// <summary>False when the .inputactions asset predates the action — the settings row says so
        /// rather than offering a rebind that would go nowhere.</summary>
        public bool HasWeaponTwirlAction => weaponTwirl != null;

        InputActionRebindingExtensions.RebindingOperation rebind;

        /// <summary>True while <see cref="BeginWeaponTwirlRebind"/> is listening.</summary>
        public bool IsRebinding => rebind != null;

        /// <summary>The path the flourish is actually bound to right now, override included.</summary>
        public string WeaponTwirlEffectivePath
        {
            get
            {
                if (weaponTwirl == null || weaponTwirl.bindings.Count == 0) return SettingsData.WeaponTwirlDefaultBinding;
                var b = weaponTwirl.bindings[0];
                return string.IsNullOrEmpty(b.effectivePath) ? b.path : b.effectivePath;
            }
        }

        /// <summary>How that path reads to a player ("F11"). Falls back to the string-only label in
        /// <see cref="SettingsData.KeyLabel"/> when the Input System has nothing nicer to say.</summary>
        public string WeaponTwirlLabel
        {
            get
            {
                string path = WeaponTwirlEffectivePath;
                string human = null;
                try { human = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice); }
                catch { human = null; }
                return string.IsNullOrEmpty(human) ? SettingsData.KeyLabel(path) : human.ToUpperInvariant();
            }
        }

        /// <summary>
        /// Point the flourish at a stored control path. Empty — or anything
        /// <see cref="SettingsData.SanitizeBindingPath"/> rejects — REMOVES the override and restores the
        /// asset's own binding. It never leaves the action bound to nothing, which would be a silently
        /// dead key with no way back short of deleting prefs.
        /// </summary>
        public void ApplyWeaponTwirlOverride(string path)
        {
            if (weaponTwirl == null || weaponTwirl.bindings.Count == 0) return;
            string clean = SettingsData.SanitizeBindingPath(path);
            if (clean.Length == 0) weaponTwirl.RemoveBindingOverride(0);
            else weaponTwirl.ApplyBindingOverride(0, clean);
        }

        /// <summary>Back to the asset's F11.</summary>
        public void ClearWeaponTwirlOverride()
        {
            if (weaponTwirl == null || weaponTwirl.bindings.Count == 0) return;
            weaponTwirl.RemoveBindingOverride(0);
        }

        /// <summary>
        /// Listen for one key and hand its control path back as a string. Hard rule 2 lives here: the
        /// settings screen never touches the Input System, it calls this and gets a string back.
        ///
        /// <para>Pointer movement and sticks are excluded (a mouse nudge would "press" instantly),
        /// Escape cancels rather than binds, and the operation is disposed on cancel, on completion and
        /// on <see cref="CancelRebind"/> — a panel closed mid-listen leaves nothing running. The action
        /// is disabled for the duration.</para>
        ///
        /// <para>This saves nothing: the caller owns the settings object and the store.</para>
        /// </summary>
        public void BeginWeaponTwirlRebind(Action<string> onComplete, Action onCancel)
        {
            CancelRebind();
            if (weaponTwirl == null || weaponTwirl.bindings.Count == 0)
            {
                if (onCancel != null) onCancel();
                return;
            }

            bool wasEnabled = weaponTwirl.enabled;
            if (wasEnabled) weaponTwirl.Disable();

            rebind = weaponTwirl.PerformInteractiveRebinding(0)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Gamepad>/leftStick")
                .WithControlsExcluding("<Gamepad>/rightStick")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(op =>
                {
                    DisposeRebind();
                    if (wasEnabled) weaponTwirl.Enable();
                    if (onCancel != null) onCancel();
                })
                .OnComplete(op =>
                {
                    // Read the path off the BINDING, not off the control.
                    //
                    // The operation has already applied its override to binding 0, and what it wrote is
                    // a canonical binding path ("<Keyboard>/h"). `op.selectedControl.path` is something
                    // else entirely: InputControl.path is a RUNTIME path ("/Keyboard/h") - leading
                    // slash, no device brackets - which SanitizeBindingPath rejects, correctly, because
                    // ApplyBindingOverride cannot resolve it.
                    //
                    // Using it turned EVERY rebind into a cancel: sanitize returned "", the empty branch
                    // re-applied the saved value, and the key snapped back to the default. The only key
                    // the flourish could ever end up on was F11, which is exactly what the user hit.
                    string path = weaponTwirl.bindings.Count > 0 ? weaponTwirl.bindings[0].effectivePath : null;
                    DisposeRebind();
                    if (wasEnabled) weaponTwirl.Enable();
                    // The operation has already written an override onto the action. Whatever the caller
                    // persists is pushed straight back through ApplyWeaponTwirlOverride, so a rejected
                    // path (see SanitizeBindingPath) is undone rather than left half-applied.
                    string clean = SettingsData.SanitizeBindingPath(path);
                    if (clean.Length == 0)
                    {
                        ApplyWeaponTwirlOverride(SettingsStore.Current.weaponTwirlBinding);
                        if (onCancel != null) onCancel();
                        return;
                    }
                    if (onComplete != null) onComplete(clean);
                });
            rebind.Start();
        }

        /// <summary>Stop listening and change nothing. Safe when nothing is listening.</summary>
        public void CancelRebind()
        {
            if (rebind == null) return;
            var op = rebind;
            rebind = null;
            op.Cancel();
            op.Dispose();
            if (weaponTwirl != null && !weaponTwirl.enabled) weaponTwirl.Enable();
        }

        void DisposeRebind()
        {
            if (rebind == null) return;
            var op = rebind;
            rebind = null;
            op.Dispose();
        }

        void OnDisable() { CancelRebind(); }

        // ---- the in-game level editor (F10 toggles; the rest only mean anything while it is open) ----

        /// <summary>
        /// F10. In every build, the player must first grant the process-local developer capability by
        /// entering the secret passphrase in the command console.
        /// The in-game level editor is a development tool (docs/LEVEL-EDITOR.md; the 2026-09-05 decision
        /// froze it at v1), and without this gate a playtester who pressed F10 in the middle of a run
        /// was dropped into a fly camera with the motor idle — which is both a way to leave the level
        /// and a way to invalidate a speedrun time. Gated here because hard rule 2 makes this the one
        /// place the key exists; <see cref="LevelEditor.Enter"/> and <see cref="LevelEditor.Toggle"/>
        /// independently enforce the same capability so a direct component call cannot bypass it.
        ///
        /// <para>This does NOT disable the editor's code. A custom level still loads and plays in a
        /// shipped build — the main menu's CUSTOM rows set <see cref="LevelEditor.PendingLoadPath"/> and
        /// <c>LoadPendingAndPlay</c> calls <c>Enter</c>/<c>Play</c> directly, never through input.
        /// Only the fly-cam ENTRY is gated here. The test menu, debug hotkeys, Sandbox entry and fourth
        /// test weapon consume the same capability. EXPORT remains editor-only.</para>
        /// </summary>
        public static bool LevelEditorSessionUnlocked { get { return DeveloperAccess.IsUnlocked; } }

        /// <summary>The pure policy behind the compile-symbol gate, exposed so release behaviour is
        /// testable from EditMode without producing a second player build.</summary>
        public static bool LevelEditorShortcutAllowed(bool editorOrDevelopmentBuild, bool sessionUnlocked)
        {
            // The first parameter survives for serialized/test API compatibility. Editor and
            // development builds no longer bypass the shared console gate.
            return sessionUnlocked;
        }

        public bool LevelEditorPressed
        {
            get
            {
                return LevelEditorShortcutAllowed(Application.isEditor || Debug.isDebugBuild, DeveloperAccess.IsUnlocked)
                    && levelEditor != null && levelEditor.WasPressedThisFrame();
            }
        }
        public bool EditorPlacePressed => editorPlace != null && editorPlace.WasPressedThisFrame();
        /// <summary>Left button held: past a short hold on a placed piece this is a GRAB, released = drop.</summary>
        public bool EditorPlaceHeld => editorPlace != null && editorPlace.IsPressed();
        /// <summary>Mouse wheel notches. Plain = size, Shift (EditorFast) = piece kind, Ctrl (EditorDown) = variant.</summary>
        public bool EditorWheelUpPressed => editorWheelUp != null && editorWheelUp.WasPressedThisFrame();
        public bool EditorWheelDownPressed => editorWheelDown != null && editorWheelDown.WasPressedThisFrame();
        public bool EditorDeletePressed => editorDelete != null && editorDelete.WasPressedThisFrame();
        public bool EditorGrabHeld => editorGrab != null && editorGrab.IsPressed();
        public bool EditorRotatePressed => editorRotate != null && editorRotate.WasPressedThisFrame();
        public bool EditorGrowPressed => editorGrow != null && editorGrow.WasPressedThisFrame();
        public bool EditorShrinkPressed => editorShrink != null && editorShrink.WasPressedThisFrame();
        public bool EditorPrevPressed => editorPrev != null && editorPrev.WasPressedThisFrame();
        public bool EditorNextPressed => editorNext != null && editorNext.WasPressedThisFrame();
        public bool EditorVariantPressed => editorVariant != null && editorVariant.WasPressedThisFrame();
        public bool EditorCursorPressed => editorCursor != null && editorCursor.WasPressedThisFrame();
        public bool EditorFreeHeld => editorFree != null && editorFree.IsPressed();
        public bool EditorFastHeld => editorFast != null && editorFast.IsPressed();
        public bool EditorDownHeld => editorDown != null && editorDown.IsPressed();
        public bool EditorUndoPressed => editorUndo != null && editorUndo.WasPressedThisFrame();
        public bool EditorRedoPressed => editorRedo != null && editorRedo.WasPressedThisFrame();
        public bool EditorDuplicatePressed => editorDuplicate != null && editorDuplicate.WasPressedThisFrame();
        public bool EditorCancelPressed => editorCancel != null && editorCancel.WasPressedThisFrame();
        public bool EditorPlaceHerePressed => editorPlaceHere != null && editorPlaceHere.WasPressedThisFrame();
        /// <summary>`I` (eyedropper): the aimed piece's kind, variant and size become the pending selection.</summary>
        public bool EditorPickPressed => editorPick != null && editorPick.WasPressedThisFrame();
        public bool RadioNextPressed => radioNext != null && radioNext.WasPressedThisFrame();
        public bool RadioPreviousPressed => radioPrevious != null && radioPrevious.WasPressedThisFrame();
        public bool RadioTogglePressed => radioToggle != null && radioToggle.WasPressedThisFrame();
        /// <summary>The arrow keys as a nudge, pressed THIS frame (each axis −1 / 0 / +1). Read off the keyboard
        /// directly because the arrows are also part of the Move composite; the editor subtracts
        /// <see cref="ArrowAxis"/> from its fly so a nudge never also flies.</summary>
        public Vector2 EditorNudgePressed
        {
            get
            {
                var k = Keyboard.current; if (k == null) return Vector2.zero;
                float x = (k.rightArrowKey.wasPressedThisFrame ? 1f : 0f) - (k.leftArrowKey.wasPressedThisFrame ? 1f : 0f);
                float y = (k.upArrowKey.wasPressedThisFrame ? 1f : 0f) - (k.downArrowKey.wasPressedThisFrame ? 1f : 0f);
                return new Vector2(x, y);
            }
        }
        /// <summary>The arrow keys' current contribution to Move (held), so the editor can fly on WASD alone.</summary>
        public Vector2 ArrowAxis
        {
            get
            {
                var k = Keyboard.current; if (k == null) return Vector2.zero;
                return new Vector2((k.rightArrowKey.isPressed ? 1f : 0f) - (k.leftArrowKey.isPressed ? 1f : 0f),
                                   (k.upArrowKey.isPressed ? 1f : 0f) - (k.downArrowKey.isPressed ? 1f : 0f));
            }
        }

        /// <summary>0..3 for slot keys pressed this frame, -1 otherwise.</summary>
        public int WeaponSlotPressed
        {
            get
            {
                if (slot1 != null && slot1.WasPressedThisFrame()) return 0;
                if (slot2 != null && slot2.WasPressedThisFrame()) return 1;
                if (slot3 != null && slot3.WasPressedThisFrame()) return 2;
                if (DeveloperAccess.IsUnlocked && slot4 != null && slot4.WasPressedThisFrame()) return 3;
                return -1;
            }
        }

        public bool AnyMovementInput => MoveAxis.sqrMagnitude > 0.01f || JumpPressed || DashPressed || SlidePressed;
    }
}
