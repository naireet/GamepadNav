using GamepadNav.Core;

namespace GamepadNav.App;

/// <summary>
/// Polls the controller directly in the tray app for keyboard overlay navigation.
/// The service handles mouse/button injection; the app only needs controller state
/// for navigating the keyboard grid when the overlay is visible.
/// Also handles Guide+Y (toggle keyboard) and Guide+X (toggle numpad).
/// </summary>
public sealed class ControllerPoller
{
    private readonly KeyboardWindow _keyboard;
    private readonly IpcClient _ipcClient;
    private readonly XInputReader _reader;
    private readonly CancellationTokenSource _cts = new();
    private Task? _pollTask;
    private ControllerState _previousState;

    public ControllerPoller(KeyboardWindow keyboard, IpcClient ipcClient)
    {
        _keyboard = keyboard;
        _ipcClient = ipcClient;
        _reader = new XInputReader(0.15f);
    }

    public void Start()
    {
        _pollTask = Task.Run(PollLoop);
    }

    public void Stop()
    {
        _cts.Cancel();
    }

    private async Task PollLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var state = _reader.Read();

                if (state.IsConnected)
                {
                    bool guideHeld = state.IsButtonDown(GamepadButtons.Guide);

                    // Guide + Y → toggle keyboard
                    if (guideHeld && Pressed(state, _previousState, GamepadButtons.Y))
                    {
                        _keyboard.Dispatcher.Invoke(() => _keyboard.Toggle());
                    }

                    // Guide + X → toggle numpad mode
                    if (guideHeld && Pressed(state, _previousState, GamepadButtons.X))
                    {
                        _keyboard.Dispatcher.Invoke(() => _keyboard.ToggleNumpad());
                    }

                    // When keyboard is visible, route d-pad and A/B to it
                    if (_keyboard.IsVisible)
                    {
                        _keyboard.Dispatcher.Invoke(() => _keyboard.HandleInput(state, _previousState));
                    }
                }

                _previousState = state;
            }
            catch { }

            await Task.Delay(16, _cts.Token).ConfigureAwait(false);
        }
    }

    private static bool Pressed(ControllerState current, ControllerState previous, GamepadButtons button)
        => current.IsButtonDown(button) && !previous.IsButtonDown(button);
}
