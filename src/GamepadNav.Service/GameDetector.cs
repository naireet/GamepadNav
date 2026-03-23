using System.Diagnostics;
using System.Runtime.InteropServices;
using GamepadNav.Core.Native;

namespace GamepadNav.Service;

/// <summary>
/// Monitors the foreground window to detect when a game is running.
/// Auto-disables GamepadNav input translation during gameplay.
/// </summary>
public sealed class GameDetector
{
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "Playnite.DesktopApp", "Playnite.FullscreenApp",
        "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost",
        "ApplicationFrameHost", "SystemSettings", "TextInputHost",
        "GamepadNav.App", "GamepadNav.Overlay",
    };

    private readonly HashSet<string> _gameProcesses;
    private readonly ILogger<GameDetector> _logger;
    private bool _gameDetected;
    private string? _lastGameProcess;

    public bool IsGameRunning => _gameDetected;
    public string? CurrentGame => _lastGameProcess;

    public GameDetector(IEnumerable<string> gameProcesses, ILogger<GameDetector> logger)
    {
        _gameProcesses = new HashSet<string>(gameProcesses, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    /// <summary>
    /// Checks the current foreground window. Returns true if a game is detected.
    /// </summary>
    public bool Update()
    {
        var hwnd = InputApi.GetForegroundWindow();
        if (hwnd == nint.Zero)
        {
            SetGameState(false, null);
            return false;
        }

        InputApi.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
        {
            SetGameState(false, null);
            return false;
        }

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            string name = proc.ProcessName;

            // Explicit game process list match
            if (_gameProcesses.Contains(name))
            {
                SetGameState(true, name);
                return true;
            }

            // Skip known system/shell processes
            if (SystemProcesses.Contains(name))
            {
                SetGameState(false, null);
                return false;
            }

            // Heuristic: check if window is likely fullscreen
            if (IsLikelyFullscreenGame(hwnd))
            {
                SetGameState(true, name);
                return true;
            }
        }
        catch (ArgumentException)
        {
            // Process exited between GetWindowThreadProcessId and GetProcessById
        }

        SetGameState(false, null);
        return false;
    }

    private static bool IsLikelyFullscreenGame(nint hwnd)
    {
        if (!InputApi.GetWindowRect(hwnd, out var rect))
            return false;

        int screenW = InputApi.GetSystemMetrics(InputApi.SM_CXSCREEN);
        int screenH = InputApi.GetSystemMetrics(InputApi.SM_CYSCREEN);

        int winW = rect.Right - rect.Left;
        int winH = rect.Bottom - rect.Top;

        // Window covers entire primary monitor (or larger for borderless)
        return winW >= screenW && winH >= screenH;
    }

    private void SetGameState(bool detected, string? processName)
    {
        if (detected == _gameDetected) return;

        _gameDetected = detected;
        _lastGameProcess = processName;

        if (detected)
            _logger.LogInformation("Game detected: {Process} — auto-disabling input", processName);
        else
            _logger.LogInformation("Game exited — re-enabling input");
    }
}
