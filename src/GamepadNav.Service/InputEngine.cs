using GamepadNav.Core;
using GamepadNav.Core.Native;
using System.Runtime.InteropServices;

namespace GamepadNav.Service;

// High-resolution timer for smooth polling
partial class InputEngine
{
    [LibraryImport("winmm.dll")]
    private static partial uint timeBeginPeriod(uint uMilliseconds);

    [LibraryImport("winmm.dll")]
    private static partial uint timeEndPeriod(uint uMilliseconds);
}

/// <summary>
/// Main input translation engine. Polls XInput and injects mouse/keyboard events via SendInput.
/// </summary>
public sealed partial class InputEngine : BackgroundService
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

    /// <summary>
    /// Callback for overlay commands (toggleKeyboard, toggleNumpad).
    /// Set by the tray app when hosting InputEngine in-process.
    /// When set, IPC is skipped entirely.
    /// </summary>
    public static Action<string>? OverlayCallback { get; set; }

    // Game detection polling: check every ~500ms
    private int _gameDetectCounter;
    private const int GameDetectInterval = 30;

    // Delta-time tracking for frame-rate independent movement
    private readonly System.Diagnostics.Stopwatch _frameTimer = new();

    // Accumulated fractional mouse movement (SendInput only takes integers)
    private float _mouseAccumX;
    private float _mouseAccumY;
    private float _scrollAccumY;
    private float _scrollAccumX;
    private float _smoothedScrollY;
    private float _smoothedScrollX;

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
        bool isUserSession = Environment.UserInteractive;

        if (isUserSession)
        {
            _gameDetector = new GameDetector(config.GameProcesses, config.SuppressProcesses,
                _loggerFactory.CreateLogger<GameDetector>());

            // Only start IPC server if no in-process callback is set
            if (OverlayCallback == null)
            {
                _ipcServer = new IpcServer(_loggerFactory.CreateLogger<IpcServer>());
                _ipcServer.CommandReceived += OnIpcCommand;
                _ipcServer.Start();
            }
        }

        // Enable high-resolution timer for smooth polling (~1ms instead of ~15ms)
        timeBeginPeriod(1);

        // Desktop manager for login screen support — only useful when running as LocalSystem service (Session 0).
        // In a user session, the process is already on the correct desktop; calling SetProcessWindowStation
        // and SetThreadDesktop would BREAK SendInput by detaching from the original desktop.
        bool desktopAware = false;
        DesktopManager? desktopManager = null;

        if (!isUserSession)
        {
            desktopManager = new DesktopManager(_loggerFactory.CreateLogger<DesktopManager>());
            desktopAware = desktopManager.Initialize();
            if (desktopAware)
            {
                _logger.LogInformation("Service mode — targeting Winlogon desktop for login screen input");
                // In Session 0, always attach to Winlogon. SendInput only reaches the lock screen;
                // when user is logged in, input goes nowhere (harmless).
                desktopManager.SwitchTo(ActiveDesktop.Winlogon);
            }
            else
                _logger.LogWarning("Desktop switching failed — login screen input unavailable");
        }
        else
        {
            _logger.LogInformation("Running in user session — desktop switching skipped (not needed)");
        }

        _logger.LogInformation("GamepadNav InputEngine starting. Enabled={Enabled}, PollInterval={Interval}ms",
            _enabled, config.PollIntervalMs);

        int desktopCheckCounter = 0;
        const int DesktopCheckInterval = 60; // ~1 second at 60Hz

        _frameTimer.Start();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Delta-time: actual elapsed time since last frame (frame-rate independent)
                float deltaTime = (float)_frameTimer.Elapsed.TotalSeconds;
                _frameTimer.Restart();

                config = _configManager.Current;
                var state = _reader.Read(config.ControllerIndex);

                // In service mode, periodically re-attach to Winlogon in case of session changes
                if (desktopAware && ++desktopCheckCounter >= DesktopCheckInterval)
                {
                    desktopCheckCounter = 0;
                    desktopManager!.SwitchTo(ActiveDesktop.Winlogon);
                }

                if (state.IsConnected)
                {
                    if (!_previousState.IsConnected)
                        _logger.LogInformation("Controller connected (index {Index})", config.ControllerIndex);

                    // Game detection (throttled) — user session only
                    bool gameBlocked = false;
                    if (_gameDetector != null)
                    {
                        if (++_gameDetectCounter >= GameDetectInterval)
                        {
                            _gameDetectCounter = 0;
                            _gameDetector.Update();
                        }
                        gameBlocked = _gameDetector.IsGameRunning;

                        // L3+R3 toggle — skip when a suppress process is active
                        if (!_gameDetector.IsFullSuppressed)
                            CheckToggle(state);
                    }
                    else
                    {
                        // Service mode: always allow toggle
                        CheckToggle(state);
                    }

                    if (_enabled && !gameBlocked)
                    {
                        ProcessMouseMovement(state, config, deltaTime);
                        ProcessScrolling(state, config, deltaTime);
                        _buttonHandler.ProcessButtons(state, _previousState);

                        // Back+Y/X combos → overlay commands
                        bool backHeld = state.IsButtonDown(GamepadButtons.Back);
                        if (backHeld)
                        {
                            if (Pressed(state, _previousState, GamepadButtons.Y))
                            {
                                if (OverlayCallback != null) OverlayCallback("toggleKeyboard");
                                else _ipcServer?.SendCommand("toggleKeyboard");
                            }
                            if (Pressed(state, _previousState, GamepadButtons.X))
                            {
                                if (OverlayCallback != null) OverlayCallback("toggleNumpad");
                                else _ipcServer?.SendCommand("toggleNumpad");
                            }
                        }
                    }

                    // Broadcast status to tray app periodically (user session only)
                    if (_gameDetector != null && _gameDetectCounter == 0)
                    {
                        _ipcServer?.SendStatus(new StatusMessage
                        {
                            Enabled = _enabled,
                            ControllerConnected = state.IsConnected,
                            GameDetected = gameBlocked,
                            CurrentGame = _gameDetector.CurrentGame,
                        });
                    }
                }
                else if (_previousState.IsConnected)
                {
                    _logger.LogWarning("Controller disconnected — waiting for reconnect");
                }

                _previousState = state;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in input polling loop");
            }

            // High-res sleep: Thread.Sleep(1) with timeBeginPeriod(1) gives ~1ms resolution
            if (config.PollIntervalMs <= 2)
                Thread.Sleep(config.PollIntervalMs);
            else
                await Task.Delay(config.PollIntervalMs, stoppingToken);
        }

        _ipcServer?.Dispose();
        desktopManager?.Dispose();
        _configManager.Dispose();
        timeEndPeriod(1);
        _logger.LogInformation("GamepadNav InputEngine stopped.");
    }

    private void ProcessMouseMovement(ControllerState state, GamepadNavConfig config, float deltaTime)
    {
        if (state.LeftStickX == 0 && state.LeftStickY == 0)
        {
            _mouseAccumX = 0;
            _mouseAccumY = 0;
            return;
        }

        // Apply acceleration curve: speed = magnitude^acceleration * cursorSpeed * deltaTime
        float magnitude = MathF.Sqrt(state.LeftStickX * state.LeftStickX + state.LeftStickY * state.LeftStickY);
        float accelerated = MathF.Pow(MathF.Min(magnitude, 1f), config.CursorAcceleration);

        // cursorSpeed is now pixels per second
        float dx = state.LeftStickX / magnitude * accelerated * config.CursorSpeed * deltaTime;
        float dy = -state.LeftStickY / magnitude * accelerated * config.CursorSpeed * deltaTime; // Y inverted

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

    private void ProcessScrolling(ControllerState state, GamepadNavConfig config, float deltaTime)
    {
        // Smooth the stick input to reduce jitter
        const float smoothing = 0.15f;
        _smoothedScrollY = _smoothedScrollY + (state.RightStickY - _smoothedScrollY) * smoothing;
        _smoothedScrollX = _smoothedScrollX + (state.RightStickX - _smoothedScrollX) * smoothing;

        // Vertical scroll (right stick Y) — use sub-notch increments for smooth scrolling
        float absY = MathF.Abs(_smoothedScrollY);
        if (absY > 0.01f)
        {
            _scrollAccumY += _smoothedScrollY * config.ScrollSpeed * deltaTime * 120f;
            int scrollAmount = (int)_scrollAccumY;
            if (scrollAmount != 0)
            {
                _scrollAccumY -= scrollAmount;
                SendScrollEvent(scrollAmount, InputApi.MOUSEEVENTF_WHEEL);
            }
        }
        else
        {
            _scrollAccumY = 0;
        }

        // Horizontal scroll (right stick X)
        float absX = MathF.Abs(_smoothedScrollX);
        if (absX > 0.01f)
        {
            _scrollAccumX += _smoothedScrollX * config.ScrollSpeed * deltaTime * 120f;
            int scrollAmount = (int)_scrollAccumX;
            if (scrollAmount != 0)
            {
                _scrollAccumX -= scrollAmount;
                SendScrollEvent(scrollAmount, InputApi.MOUSEEVENTF_HWHEEL);
            }
        }
        else
        {
            _scrollAccumX = 0;
        }
    }

    private static void SendScrollEvent(int amount, uint flags)
    {
        var input = new InputApi.INPUT
        {
            type = InputApi.INPUT_MOUSE,
            union = new InputApi.INPUT_UNION
            {
                mi = new InputApi.MOUSEINPUT
                {
                    mouseData = amount,
                    dwFlags = flags,
                }
            }
        };
        InputApi.SendInput(1, [input], Marshal.SizeOf<InputApi.INPUT>());
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

    private static bool Pressed(ControllerState current, ControllerState previous, GamepadButtons button)
        => current.IsButtonDown(button) && !previous.IsButtonDown(button);

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
