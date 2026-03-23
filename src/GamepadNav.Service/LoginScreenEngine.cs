using GamepadNav.Core;
using GamepadNav.Core.Native;
using GamepadNav.Overlay;
using System.Runtime.InteropServices;

namespace GamepadNav.Service;

/// <summary>
/// Minimal login screen service. Runs as LocalSystem, targets the Winlogon desktop.
/// Provides mouse cursor, basic buttons, and numpad overlay for PIN entry.
/// Dormant when user is logged in (SendInput on Winlogon goes nowhere).
/// </summary>
public sealed partial class LoginScreenEngine : BackgroundService
{
    [LibraryImport("winmm.dll")]
    private static partial uint timeBeginPeriod(uint uMilliseconds);

    [LibraryImport("winmm.dll")]
    private static partial uint timeEndPeriod(uint uMilliseconds);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? lpModuleName);

    private readonly ILogger<LoginScreenEngine> _logger;

    public LoginScreenEngine(ILogger<LoginScreenEngine> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LoginScreenEngine starting (LocalSystem service mode)");

        // Open Winlogon desktop
        var desktopLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<DesktopManager>();
        var desktopManager = new DesktopManager(desktopLogger);

        if (!desktopManager.Initialize())
        {
            _logger.LogError("Failed to initialize DesktopManager — login screen input unavailable");
            return;
        }

        // Attach thread to Winlogon desktop so CreateWindowEx and SendInput target it
        if (!desktopManager.SwitchTo(ActiveDesktop.Winlogon))
        {
            _logger.LogWarning("Could not switch to Winlogon desktop (error {Error}), falling back to Default",
                Marshal.GetLastPInvokeError());
        }
        else
        {
            _logger.LogInformation("Attached to Winlogon desktop");
        }

        // Create numpad overlay on the current (Winlogon) desktop
        var hInstance = GetModuleHandle(null);
        NumpadOverlay.Create(hInstance);
        _logger.LogInformation("Numpad overlay created on Winlogon desktop");

        // High-res timer
        timeBeginPeriod(1);

        var reader = new XInputReader(0.15f);
        var config = new GamepadNavConfig();
        var buttonHandler = new ButtonHandler(config);
        var previous = new ControllerState();
        var frameTimer = new System.Diagnostics.Stopwatch();
        float mouseAccumX = 0, mouseAccumY = 0;
        float scrollAccumY = 0, smoothedScrollY = 0;
        bool wasConnected = false;

        frameTimer.Start();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                float deltaTime = (float)frameTimer.Elapsed.TotalSeconds;
                frameTimer.Restart();

                var state = reader.Read();

                if (state.IsConnected)
                {
                    if (!wasConnected)
                        _logger.LogInformation("Controller connected on login screen");
                    wasConnected = true;

                    // Back+X toggles numpad overlay
                    bool backHeld = state.IsButtonDown(GamepadButtons.Back);
                    if (backHeld && Pressed(state, previous, GamepadButtons.X))
                        NumpadOverlay.Toggle();

                    // If overlay is visible, route input to it (d-pad, A, B)
                    if (!NumpadOverlay.ProcessInput(state, previous))
                    {
                        // Overlay not visible or didn't consume — process normal input
                        ProcessMouse(state, config, deltaTime, ref mouseAccumX, ref mouseAccumY);
                        ProcessScroll(state, config, deltaTime, ref scrollAccumY, ref smoothedScrollY);
                        buttonHandler.ProcessButtons(state, previous);
                    }
                }
                else if (wasConnected)
                {
                    _logger.LogWarning("Controller disconnected on login screen");
                    wasConnected = false;
                }

                previous = state;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in login screen poll loop");
            }

            Thread.Sleep(1);
        }

        NumpadOverlay.Hide();
        desktopManager.Dispose();
        timeEndPeriod(1);
        _logger.LogInformation("LoginScreenEngine stopped");
    }

    private static void ProcessMouse(ControllerState state, GamepadNavConfig config, float deltaTime,
        ref float accumX, ref float accumY)
    {
        if (state.LeftStickX == 0 && state.LeftStickY == 0) return;

        float magnitude = MathF.Sqrt(state.LeftStickX * state.LeftStickX + state.LeftStickY * state.LeftStickY);
        float speed = MathF.Pow(magnitude, config.CursorAcceleration) * config.CursorSpeed * deltaTime * 60f;

        accumX += state.LeftStickX * speed;
        accumY += -state.LeftStickY * speed; // Invert Y

        int dx = (int)accumX;
        int dy = (int)accumY;

        if (dx != 0 || dy != 0)
        {
            accumX -= dx;
            accumY -= dy;

            var input = new InputApi.INPUT
            {
                type = InputApi.INPUT_MOUSE,
                union = new InputApi.INPUT_UNION
                {
                    mi = new InputApi.MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        dwFlags = InputApi.MOUSEEVENTF_MOVE,
                    }
                }
            };
            InputApi.SendInput(1, [input], Marshal.SizeOf<InputApi.INPUT>());
        }
    }

    private static void ProcessScroll(ControllerState state, GamepadNavConfig config, float deltaTime,
        ref float accumY, ref float smoothedY)
    {
        smoothedY += (state.RightStickY - smoothedY) * 0.15f;
        if (MathF.Abs(smoothedY) > 0.01f)
        {
            accumY += smoothedY * config.ScrollSpeed * deltaTime * 120f;
            int amount = (int)accumY;
            if (amount != 0)
            {
                accumY -= amount;
                var input = new InputApi.INPUT
                {
                    type = InputApi.INPUT_MOUSE,
                    union = new InputApi.INPUT_UNION
                    {
                        mi = new InputApi.MOUSEINPUT
                        {
                            mouseData = amount,
                            dwFlags = InputApi.MOUSEEVENTF_WHEEL,
                        }
                    }
                };
                InputApi.SendInput(1, [input], Marshal.SizeOf<InputApi.INPUT>());
            }
        }
        else
        {
            accumY = 0;
        }
    }

    private static bool Pressed(ControllerState current, ControllerState previous, GamepadButtons button)
        => current.IsButtonDown(button) && !previous.IsButtonDown(button);
}
