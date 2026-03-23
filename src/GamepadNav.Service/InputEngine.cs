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
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConfigManager _configManager;
    private XInputReader _reader;
    private ButtonHandler _buttonHandler;

    private bool _enabled;
    private ControllerState _previousState;
    private GameDetector? _gameDetector;
    private IpcServer? _ipcServer;

    // Game detection polling: check every ~500ms, not every frame
    private int _gameDetectCounter;
    private const int GameDetectInterval = 30; // frames (~500ms at 60Hz)

    // Accumulated fractional mouse movement (SendInput only takes integers)
    private float _mouseAccumX;
    private float _mouseAccumY;
    private float _scrollAccumY;
    private float _scrollAccumX;

    public InputEngine(ILogger<InputEngine> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _configManager = new ConfigManager();

        var config = _configManager.Current;
        _reader = new XInputReader(config.StickDeadZone);
        _enabled = config.StartEnabled;
        _buttonHandler = new ButtonHandler(config);

        _configManager.ConfigChanged += OnConfigChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = _configManager.Current;

        _gameDetector = new GameDetector(config.GameProcesses,
            _loggerFactory.CreateLogger<GameDetector>());

        _ipcServer = new IpcServer(_loggerFactory.CreateLogger<IpcServer>());
        _ipcServer.CommandReceived += OnIpcCommand;
        _ipcServer.Start();

        // Desktop manager for login screen support (only works when running as LocalSystem service)
        var desktopManager = new DesktopManager(_loggerFactory.CreateLogger<DesktopManager>());
        bool desktopAware = desktopManager.Initialize();
        if (desktopAware)
            _logger.LogInformation("Desktop switching enabled — login screen input supported");
        else
            _logger.LogInformation("Desktop switching unavailable — running in user-mode only");

        _logger.LogInformation("GamepadNav InputEngine starting. Enabled={Enabled}, PollInterval={Interval}ms",
            _enabled, config.PollIntervalMs);

        int desktopCheckCounter = 0;
        const int DesktopCheckInterval = 60; // ~1 second at 60Hz

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                config = _configManager.Current;
                var state = _reader.Read(config.ControllerIndex);

                // Periodically check which desktop is active and switch thread target
                if (desktopAware && ++desktopCheckCounter >= DesktopCheckInterval)
                {
                    desktopCheckCounter = 0;
                    desktopManager.SwitchToActiveDesktop();
                }

                if (state.IsConnected)
                {
                    // L3+R3 toggle check (always active regardless of _enabled)
                    CheckToggle(state);

                    // Game detection (throttled)
                    bool gameBlocked = false;
                    if (++_gameDetectCounter >= GameDetectInterval)
                    {
                        _gameDetectCounter = 0;
                        _gameDetector!.Update();
                    }
                    gameBlocked = _gameDetector!.IsGameRunning;

                    if (_enabled && !gameBlocked)
                    {
                        ProcessMouseMovement(state, config);
                        ProcessScrolling(state, config);
                        _buttonHandler.ProcessButtons(state, _previousState);
                    }

                    // Broadcast status to tray app periodically
                    if (_gameDetectCounter == 0)
                    {
                        _ipcServer.SendStatus(new StatusMessage
                        {
                            Enabled = _enabled,
                            ControllerConnected = state.IsConnected,
                            GameDetected = gameBlocked,
                            CurrentGame = _gameDetector.CurrentGame,
                        });
                    }
                }

                _previousState = state;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in input polling loop");
            }

            await Task.Delay(config.PollIntervalMs, stoppingToken);
        }

        _ipcServer.Dispose();
        desktopManager.Dispose();
        _configManager.Dispose();
        _logger.LogInformation("GamepadNav InputEngine stopped.");
    }

    private void ProcessMouseMovement(ControllerState state, GamepadNavConfig config)
    {
        if (state.LeftStickX == 0 && state.LeftStickY == 0)
        {
            _mouseAccumX = 0;
            _mouseAccumY = 0;
            return;
        }

        // Apply acceleration curve: speed = magnitude^acceleration * cursorSpeed
        float magnitude = MathF.Sqrt(state.LeftStickX * state.LeftStickX + state.LeftStickY * state.LeftStickY);
        float accelerated = MathF.Pow(MathF.Min(magnitude, 1f), config.CursorAcceleration);

        float dx = state.LeftStickX / magnitude * accelerated * config.CursorSpeed;
        float dy = -state.LeftStickY / magnitude * accelerated * config.CursorSpeed; // Y inverted

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

    private void ProcessScrolling(ControllerState state, GamepadNavConfig config)
    {
        // Vertical scroll (right stick Y)
        if (state.RightStickY != 0)
        {
            _scrollAccumY += state.RightStickY * config.ScrollSpeed;
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
            _scrollAccumX += state.RightStickX * config.ScrollSpeed;
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

    private void OnIpcCommand(CommandMessage cmd)
    {
        switch (cmd.Action)
        {
            case "toggle":
                _enabled = !_enabled;
                _logger.LogInformation("GamepadNav toggled via IPC: {State}", _enabled ? "ENABLED" : "DISABLED");
                break;
            case "enable":
                _enabled = true;
                break;
            case "disable":
                _enabled = false;
                break;
        }
    }

    private void OnConfigChanged(GamepadNavConfig newConfig)
    {
        _reader = new XInputReader(newConfig.StickDeadZone);
        _buttonHandler = new ButtonHandler(newConfig);
        _logger.LogInformation("Config hot-reloaded");
    }
}
