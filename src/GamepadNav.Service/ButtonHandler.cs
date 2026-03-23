using GamepadNav.Core;
using GamepadNav.Core.Native;
using System.Runtime.InteropServices;

namespace GamepadNav.Service;

/// <summary>
/// Handles button press→release edge detection and maps controller buttons to SendInput events.
/// </summary>
public sealed class ButtonHandler
{
    private readonly GamepadNavConfig _config;

    // Track held state for triggers (which are analog, not digital)
    private bool _leftTriggerHeld;
    private bool _rightTriggerHeld;

    public ButtonHandler(GamepadNavConfig config)
    {
        _config = config;
    }

    public void ProcessButtons(ControllerState current, ControllerState previous)
    {
        // --- Mouse clicks ---
        // RB = left click (user preference: reversed)
        HandleMouseButton(current, previous, GamepadButtons.RightShoulder,
            InputApi.MOUSEEVENTF_LEFTDOWN, InputApi.MOUSEEVENTF_LEFTUP);

        // LB = right click
        HandleMouseButton(current, previous, GamepadButtons.LeftShoulder,
            InputApi.MOUSEEVENTF_RIGHTDOWN, InputApi.MOUSEEVENTF_RIGHTUP);

        // X = middle click
        HandleMouseButton(current, previous, GamepadButtons.X,
            InputApi.MOUSEEVENTF_MIDDLEDOWN, InputApi.MOUSEEVENTF_MIDDLEUP);

        // --- Keyboard keys ---
        HandleKeyButton(current, previous, GamepadButtons.A, InputApi.VK_RETURN);
        HandleKeyButton(current, previous, GamepadButtons.B, InputApi.VK_ESCAPE);
        HandleKeyButton(current, previous, GamepadButtons.Start, InputApi.VK_RETURN);
        HandleKeyButton(current, previous, GamepadButtons.Back, InputApi.VK_ESCAPE);

        // D-pad → arrow keys
        HandleKeyButton(current, previous, GamepadButtons.DPadUp, InputApi.VK_UP, extended: true);
        HandleKeyButton(current, previous, GamepadButtons.DPadDown, InputApi.VK_DOWN, extended: true);
        HandleKeyButton(current, previous, GamepadButtons.DPadLeft, InputApi.VK_LEFT, extended: true);
        HandleKeyButton(current, previous, GamepadButtons.DPadRight, InputApi.VK_RIGHT, extended: true);

        // --- Trigger modifiers (analog → digital threshold) ---
        HandleTriggerModifier(current.LeftTrigger, ref _leftTriggerHeld, InputApi.VK_SHIFT);
        HandleTriggerModifier(current.RightTrigger, ref _rightTriggerHeld, InputApi.VK_CONTROL);
    }

    private static void HandleMouseButton(ControllerState current, ControllerState previous,
        GamepadButtons button, uint downFlag, uint upFlag)
    {
        bool nowDown = current.IsButtonDown(button);
        bool wasDown = previous.IsButtonDown(button);

        if (nowDown && !wasDown)
            SendMouseEvent(downFlag);
        else if (!nowDown && wasDown)
            SendMouseEvent(upFlag);
    }

    private static void HandleKeyButton(ControllerState current, ControllerState previous,
        GamepadButtons button, ushort vk, bool extended = false)
    {
        bool nowDown = current.IsButtonDown(button);
        bool wasDown = previous.IsButtonDown(button);

        if (nowDown && !wasDown)
            SendKeyEvent(vk, down: true, extended);
        else if (!nowDown && wasDown)
            SendKeyEvent(vk, down: false, extended);
    }

    private void HandleTriggerModifier(float triggerValue, ref bool held, ushort vk)
    {
        bool pressed = triggerValue >= _config.TriggerThreshold;

        if (pressed && !held)
        {
            SendKeyEvent(vk, down: true);
            held = true;
        }
        else if (!pressed && held)
        {
            SendKeyEvent(vk, down: false);
            held = false;
        }
    }

    private static void SendMouseEvent(uint flags)
    {
        var input = new InputApi.INPUT
        {
            type = InputApi.INPUT_MOUSE,
            union = new InputApi.INPUT_UNION
            {
                mi = new InputApi.MOUSEINPUT { dwFlags = flags }
            }
        };
        InputApi.SendInput(1, [input], Marshal.SizeOf<InputApi.INPUT>());
    }

    private static void SendKeyEvent(ushort vk, bool down, bool extended = false)
    {
        uint flags = down ? 0u : InputApi.KEYEVENTF_KEYUP;
        if (extended) flags |= InputApi.KEYEVENTF_EXTENDEDKEY;

        var input = new InputApi.INPUT
        {
            type = InputApi.INPUT_KEYBOARD,
            union = new InputApi.INPUT_UNION
            {
                ki = new InputApi.KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = flags,
                }
            }
        };
        InputApi.SendInput(1, [input], Marshal.SizeOf<InputApi.INPUT>());
    }
}
