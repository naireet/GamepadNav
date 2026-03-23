using System.Diagnostics;
using System.Runtime.InteropServices;
using GamepadNav.Core.Native;

namespace GamepadNav.Service;

/// <summary>
/// Monitors the foreground window to detect when a game is running.
/// Uses loaded DLL detection (D3D/Vulkan) + process whitelist/blacklist.
/// Auto-disables GamepadNav input translation during gameplay.
/// </summary>
public sealed class GameDetector
{
    /// <summary>Processes that are never games (system, shell, browsers, media).</summary>
    private static readonly HashSet<string> WhitelistedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Shell / system
        "explorer", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost",
        "ApplicationFrameHost", "SystemSettings", "TextInputHost", "LockApp",
        "LogonUI", "dwm", "csrss", "winlogon", "taskhostw",
        // Browsers
        "msedge", "chrome", "firefox", "opera", "brave",
        // Media players
        "vlc", "mpc-hc64", "mpc-hc", "mpv", "wmplayer",
        // Dev tools / terminals
        "WindowsTerminal", "cmd", "powershell", "pwsh",
        "Code", "devenv", "rider64",
        // Communication
        "Discord", "Spotify", "Teams",
        // Utilities
        "SnippingTool", "ScreenClippingHost", "ScreenSketch",
        // Game launchers (not games themselves)
        "Playnite.DesktopApp", "Playnite.FullscreenApp",
        "steam", "steamwebhelper", "EpicGamesLauncher",
        "GOG Galaxy", "GalaxyClient",
        // Our own apps
        "GamepadNav.App", "GamepadNav.Overlay", "GamepadNav.Service",
        // Streaming / VR
        "sunshine", "moonlight", "VirtualDesktop.Streamer", "VirtualDesktop.Service",
    };

    /// <summary>DLLs that indicate a DirectX/Vulkan game when loaded.</summary>
    private static readonly string[] GameDlls =
    [
        "d3d9.dll", "d3d11.dll", "d3d12.dll",
        "vulkan-1.dll",
    ];

    private readonly HashSet<string> _gameProcesses;
    private readonly HashSet<string> _suppressProcesses;
    private readonly ILogger<GameDetector> _logger;
    private bool _gameDetected;
    private bool _fullSuppress;
    private string? _lastGameProcess;

    // Cache: remember processes we've already classified to avoid repeated DLL scans
    private readonly Dictionary<int, bool> _processCache = new();
    private int _cacheClearCounter;
    private const int CacheClearInterval = 120; // Clear cache every ~2 minutes

    public bool IsGameRunning => _gameDetected;
    /// <summary>True when a suppress-listed process is active — L3+R3 override should be skipped.</summary>
    public bool IsFullSuppressed => _fullSuppress;
    public string? CurrentGame => _lastGameProcess;

    public GameDetector(IEnumerable<string> gameProcesses, IEnumerable<string> suppressProcesses, ILogger<GameDetector> logger)
    {
        _gameProcesses = new HashSet<string>(gameProcesses, StringComparer.OrdinalIgnoreCase);
        _suppressProcesses = new HashSet<string>(suppressProcesses, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    /// <summary>
    /// Checks the current foreground window. Returns true if a game is detected.
    /// Also checks for suppress processes (running anywhere, not just foreground).
    /// </summary>
    public bool Update()
    {
        // Check suppress list — these processes running anywhere fully disable GamepadNav
        _fullSuppress = false;
        foreach (var name in _suppressProcesses)
        {
            try
            {
                var procs = Process.GetProcessesByName(name);
                if (procs.Length > 0)
                {
                    _fullSuppress = true;
                    foreach (var p in procs) p.Dispose();
                    if (!_gameDetected)
                        SetGameState(true, name + " (suppressed)");
                    return true;
                }
            }
            catch { }
        }

        // Periodically clear the process cache
        if (++_cacheClearCounter >= CacheClearInterval)
        {
            _cacheClearCounter = 0;
            _processCache.Clear();
        }

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

            // Explicit blacklist (from config) — always a game
            if (_gameProcesses.Contains(name))
            {
                SetGameState(true, name);
                return true;
            }

            // Whitelist — never a game
            if (WhitelistedProcesses.Contains(name))
            {
                SetGameState(false, null);
                return false;
            }

            // Check cache first
            if (_processCache.TryGetValue((int)pid, out bool cachedIsGame))
            {
                SetGameState(cachedIsGame, cachedIsGame ? name : null);
                return cachedIsGame;
            }

            // DLL-based detection: check if process has DirectX/Vulkan AND is fullscreen
            bool isGame = IsFullscreen(hwnd) && HasGameDlls(proc);
            _processCache[(int)pid] = isGame;

            if (isGame)
            {
                SetGameState(true, name);
                return true;
            }
        }
        catch (ArgumentException) { } // Process exited
        catch (InvalidOperationException) { } // Process exited during module enumeration

        SetGameState(false, null);
        return false;
    }

    private static bool HasGameDlls(Process proc)
    {
        try
        {
            foreach (ProcessModule module in proc.Modules)
            {
                string modName = module.ModuleName ?? "";
                foreach (var dll in GameDlls)
                {
                    if (modName.Equals(dll, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch (System.ComponentModel.Win32Exception) { } // Access denied (system process)

        return false;
    }

    private static bool IsFullscreen(nint hwnd)
    {
        if (!InputApi.GetWindowRect(hwnd, out var rect))
            return false;

        int screenW = InputApi.GetSystemMetrics(InputApi.SM_CXSCREEN);
        int screenH = InputApi.GetSystemMetrics(InputApi.SM_CYSCREEN);

        int winW = rect.Right - rect.Left;
        int winH = rect.Bottom - rect.Top;

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
