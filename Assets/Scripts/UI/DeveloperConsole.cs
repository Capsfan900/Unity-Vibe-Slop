using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A deliberately small in-game command console. It is the only player-reachable door into
    /// <see cref="DeveloperAccess"/>: help and clear work while locked, while entering the secret
    /// passphrase grants all developer tools for this process only. Input comes exclusively from
    /// <see cref="InputReader"/>.
    /// </summary>
    public class DeveloperConsole : MonoBehaviour
    {
        public GameObject panel;
        public TMP_Text output;
        public TMP_InputField input;

        public bool IsOpen { get; private set; }

        int timeHandle = -1;
        bool tookGameState;
        GameState previousState;
        CursorLockMode previousCursorLock;
        bool previousCursorVisible;
        readonly List<string> lines = new List<string>();

        const int MaxLines = 12;
        public const string LockedHelpText = "help  |  clear  |  enter developer passphrase";
        public const string UnlockedHelpText = "help  |  clear  |  timing prime/stop/status/export/discard  |  F1/F5-F10/4/R enabled";
        public static string HelpText { get { return DeveloperAccess.IsUnlocked ? UnlockedHelpText : LockedHelpText; } }

        public struct CommandResult
        {
            public bool clear;
            public bool redactInput;
            public string message;

            public CommandResult(bool clearOutput, string response, bool redact = false)
            {
                clear = clearOutput;
                redactInput = redact;
                message = response ?? "";
            }
        }

        void Start()
        {
            if (panel != null) panel.SetActive(false);
            ApplyInputMode();
        }

        void Update()
        {
            var reader = InputReader.I;
            if (reader == null) return;

            if (reader.ConsoleTogglePressed)
            {
                if (IsOpen) Close(); else Open();
                return;
            }

            if (IsOpen && reader.ConsoleSubmitPressed) Submit();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;

            if (TimeScaleController.I != null && timeHandle < 0)
                timeHandle = TimeScaleController.I.Request(0f);

            if (GameManager.I != null &&
                (GameManager.I.State == GameState.Playing || GameManager.I.State == GameState.Editing))
            {
                previousState = GameManager.I.State;
                GameManager.I.SetState(GameState.Paused);
                tookGameState = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (panel != null) panel.SetActive(true);
            if (lines.Count == 0) Append("COMMAND CONSOLE   ` closes   type help");
            if (input != null) input.text = "";
            ApplyInputMode();
            StartCoroutine(FocusNextFrame());
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            if (input != null) input.DeactivateInputField();
            if (panel != null) panel.SetActive(false);

            if (timeHandle >= 0 && TimeScaleController.I != null)
                TimeScaleController.I.Release(timeHandle);
            timeHandle = -1;

            if (tookGameState && GameManager.I != null)
                GameManager.I.SetState(previousState);
            tookGameState = false;

            // GameManager restores its ordinary state cursor, but an overlay can also be opened over
            // another paused UI. Preserve the exact cursor state that was present in that case.
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        void OnDisable()
        {
            if (IsOpen) Close();
        }

        IEnumerator FocusNextFrame()
        {
            yield return null; // prevents the opening backquote from becoming the first input character
            if (!IsOpen || input == null) yield break;
            input.ActivateInputField();
            input.Select();
        }

        public void Submit()
        {
            if (!IsOpen || input == null) return;
            string command = input.text ?? "";
            input.text = "";
            if (command.Trim().Length == 0)
            {
                input.ActivateInputField();
                return;
            }

            CommandResult result = ExecuteCommand(command);
            ApplyInputMode();
            if (result.clear)
            {
                lines.Clear();
                Repaint();
            }
            else
            {
                Append("> " + (result.redactInput ? "********" : command.Trim()));
                if (result.message.Length > 0) Append(result.message);
            }
            input.ActivateInputField();
            input.Select();
        }

        /// <summary>Parse and execute one command. Kept independent of the UI so policy is unit-tested.</summary>
        public static CommandResult ExecuteCommand(string raw)
        {
            string normalized = Normalize(raw);
            if (normalized == "help") return new CommandResult(false, HelpText);
            if (normalized == "clear") return new CommandResult(true, "");

            if (!DeveloperAccess.IsUnlocked)
            {
                if (DeveloperAccess.TryUnlock(raw))
                    return new CommandResult(false, "DEVELOPER ACCESS UNLOCKED FOR THIS SESSION", true);
                return new CommandResult(false, "DEVELOPER ACCESS LOCKED", true);
            }

            switch (normalized)
            {
                case "timing prime":
                case "timing start":
                    return new CommandResult(false, PlayerTimingCapture.Prime());
                case "timing stop":
                    return new CommandResult(false, PlayerTimingCapture.StopAndExport());
                case "timing status":
                    return new CommandResult(false, PlayerTimingCapture.Status());
                case "timing export":
                    return new CommandResult(false, PlayerTimingCapture.Export());
                case "timing discard":
                    return new CommandResult(false, PlayerTimingCapture.Discard());
                default:
                    return new CommandResult(false, "UNKNOWN COMMAND   type help");
            }
        }

        static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string[] words = raw.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words).ToLowerInvariant();
        }

        void ApplyInputMode()
        {
            if (input == null) return;
            input.contentType = DeveloperAccess.IsUnlocked
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;
            input.ForceLabelUpdate();
        }

        void Append(string line)
        {
            lines.Add(line ?? "");
            while (lines.Count > MaxLines) lines.RemoveAt(0);
            Repaint();
        }

        void Repaint()
        {
            if (output != null) output.text = string.Join("\n", lines);
        }
    }
}
