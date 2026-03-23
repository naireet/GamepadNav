using GamepadNav.Core;
using GamepadNav.Core.Native;
using System.Runtime.InteropServices;

namespace GamepadNav.Service;

/// <summary>
/// Main input translation engine. Polls XInput and injects mouse/keyboard events via SendInput.
/// </summary>
public sealed class InputEngine : BackgroundService
{
    private readonly ILogger<InputEngine> _logger;
    private readonly GamepadNavConfig _config;
    private readonly XInputReader _reader;

    private bool _enabled;
    private ControllerState _previousState;
    private readonly ButtonHandler _buttonHandler;

    // Accumulated fractional mouse movement (SendInput only takes integers)
    private float _mouseAccumX;
    private float _mouseAccumY;
    private float _scrollAccumY;
    private float _scrollAccumX;

    public InputEngine(ILogger<InputEngine> logger)
    {
        _logger = logger;
        _config = LoadConfig();
        _reader = new XInputReader(_config.StickDeadZone);
        _enabled = _config.StartEnabled;
        _buttonHandler = new ButtonHandler(_config);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GamepadNav InputEngine starting. Enabled={Enabled}, PollInterval={Interval}ms",
            _enabled, _config.PollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var state = _reader.Read(_config.ControllerIndex);

                if (state.IsConnected)
                {
                    // L3+R3 toggle check (always active regardless of _enabled)
                    CheckToggle(state);

                    if (_enabled)
                    {
                        ProcessMouseMovement(state);
                        ProcessScrolling(state);
                        _buttonHandler.ProcessButtons(state, _previousState);
                    }
                }

                _previousState = state;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in input polling loop");
            }

            await Task.Delay(_config.PollIntervalMs, stoppingToken);
        }

        _logger.LogInformation("GamepadNav InputEngine stopped.");
    }

    private void ProcessMouseMovement(ControllerState state)
    {
        if (state.LeftStickX == 0 && state.LeftStickY == 0)
        {
            _mouseAccumX = 0;
            _mouseAccumY = 0;
            return;
        }

        // Apply acceleration curve: speed = magnitude^acceleration * cursorSpeed
        float magnitude = MathF.Sqrt(state.LeftStickX * state.LeftStickX + state.LeftStickY * state.LeftStickY);
        float accelerated = MathF.Pow(MathF.Min(magnitude, 1f), _config.CursorAcceleration);

        float dx = state.LeftStickX / magnitude * accelerated * _config.CursorSpeed;
        float dy = -state.LeftStickY / magnitude * accelerated * _config.CursorSpeed; // Y inverted

        _mouseAccumX += dx;
        _mouseAccumY += dy;

        int moveX = (int)_mouseAccumX;
        int moveY = (int)_mouseAccumY;

        if (moveX == 0 && moveY == 0) return;

        _mouseAccumX -= moveX;
        _mouseAccumY -= moveY;

        var input = new InputApi.INPUT
        {
            type = InputApi.INPUT_MOUSE,
            union = new InputApi.INPUT_UNION
            {
                mi = new InputApi.MOUSEINPUT
                {
                    dx = moveX,
                    dy = moveY,
                    dwFlags = InputApi.MOUSEEVENTF_MOVE,
                }
            }
        };

        InputApi.SendInput(1, [input], Marshal.SizeOf<InputApi.INPUT>());
    }

    private void ProcessScrolling(ControllerState state)
    {
        // Vertical scroll (right stick Y)
        if (state.RightStickY != 0)
        {
            _scrollAccumY += state.RightStickY * _config.ScrollSpeed;
            int scrollAmount = (int)_scrollAccumY;
            if (scrollAmount != 0)
            {
                _scrollAccumY -= scrollAmount;
                var input = new InputApi.INPUT
                {
                    type = InputApi.INPUT_MOUSE,
                    union = new InputApi.INPUT_UNION
                    {
                        mi = new InputApi.MOUSEINPUT
                        {
                            mouseData = scrollAmount * 120, // WHEEL_DELTA = 120
                            dwFlags = InputApi.MOUSEEVENTF_WHEEL,
                        }
                    }
                };
                InputApi.SendInput(1, [input], Marshal.SizeOf<InputApi.INPUT>());
            }
        }
        else
        {
            _scrollAccumY = 0;
        }

        // Horizontal scroll (right stick X)
        if (state.RightStickX != 0)
        {
            _scrollAccumX += state.RightStickX * _config.ScrollSpeed;
            int scrollAmount = (int)_scrollAccumX;
            if (scrollAmount != 0)
            {
                _scrollAccumX -= scrollAmount;
                var input = new InputApi.INPUT
                {
                    type = InputApi.INPUT_MOUSE,
                    union = new InputApi.INPUT_UNION
                    {
                        mi = new InputApi.MOUSEINPUT
                        {
                            mouseData = scrollAmount * 120,
                            dwFlags = InputApi.MOUSEEVENTF_HWHEEL,
                        }
                    }
                };
                InputApi.SendInput(1, [input], Marshal.SizeOf<InputApi.INPUT>());
            }
        }
        else
        {
            _scrollAccumX = 0;
        }
    }

    private bool _toggleDebounce;
    private void CheckToggle(ControllerState state)
    {
        bool bothPressed = state.IsButtonDown(GamepadButtons.LeftThumb) &&
                           state.IsButtonDown(GamepadButtons.RightThumb);

        if (bothPressed && !_toggleDebounce)
        {
            _enabled = !_enabled;
            _toggleDebounce = true;
            _logger.LogInformation("GamepadNav toggled: {State}", _enabled ? "ENABLED" : "DISABLED");
        }
        else if (!bothPressed)
        {
            _toggleDebounce = false;
        }
    }

    private static GamepadNavConfig LoadConfig()
    {
        var path = GamepadNavConfig.ConfigFilePath;
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                return System.Text.Json.JsonSerializer.Deserialize<GamepadNavConfig>(json) ?? new();
            }
            catch
            {
                return new GamepadNavConfig();
            }
        }
        return new GamepadNavConfig();
    }
}
