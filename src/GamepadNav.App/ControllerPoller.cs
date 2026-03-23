using GamepadNav.Core;

namespace GamepadNav.App;

/// <summary>
/// Receives commands from the engine via IPC to control the keyboard overlay.
/// No XInput polling — the engine handles all controller input.
/// </summary>
public sealed class KeyboardController
{
    private readonly KeyboardWindow _keyboard;

    public KeyboardController(KeyboardWindow keyboard)
    {
        _keyboard = keyboard;
    }

    /// <summary>
    /// Handle a command from the engine (via IPC).
    /// </summary>
    public void HandleCommand(string action)
    {
        _keyboard.Dispatcher.Invoke(() =>
        {
            switch (action)
            {
                case "toggleKeyboard":
                    _keyboard.Toggle();
                    break;
                case "toggleNumpad":
                    _keyboard.ToggleNumpad();
                    break;
            }
        });
    }

    /// <summary>
    /// Forward controller state to the keyboard overlay for d-pad navigation.
    /// </summary>
    public void HandleControllerState(ControllerStateMessage state)
    {
        if (!_keyboard.IsVisible) return;

        _keyboard.Dispatcher.Invoke(() =>
        {
            var cs = new ControllerState
            {
                IsConnected = true,
                Buttons = (GamepadButtons)state.Buttons,
            };
            _keyboard.HandleInput(cs, _lastState);
            _lastState = cs;
        });
    }

    private ControllerState _lastState;
}
