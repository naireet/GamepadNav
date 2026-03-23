using GamepadNav.Core.Native;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace GamepadNav.Service;

/// <summary>
/// Manages switching the service's thread between the Default desktop (user session)
/// and the Winlogon desktop (login/lock screen) so that SendInput reaches the correct target.
/// 
/// When running as a Windows Service (LocalSystem), the service can open Window Station 0
/// and attach to either desktop. SendInput calls go to whichever desktop the calling thread
/// is attached to.
/// </summary>
public sealed class DesktopManager : IDisposable
{
    private readonly ILogger<DesktopManager> _logger;
    private nint _winStation;
    private nint _defaultDesktop;
    private nint _winlogonDesktop;
    private nint _originalStation;
    private nint _originalDesktop;
    private ActiveDesktop _currentDesktop = ActiveDesktop.Unknown;

    public ActiveDesktop Current => _currentDesktop;

    public DesktopManager(ILogger<DesktopManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Opens WinSta0 and both desktops. Call once at startup.
    /// Returns false if desktop APIs aren't available (e.g., running as normal user).
    /// </summary>
    public bool Initialize()
    {
        try
        {
            // Save original station/desktop so we can restore on dispose
            _originalStation = DesktopApi.GetProcessWindowStation();
            _originalDesktop = DesktopApi.GetThreadDesktop(DesktopApi.GetCurrentThreadId());

            // Open the interactive window station
            _winStation = DesktopApi.OpenWindowStation("WinSta0", false, DesktopApi.WINSTA_ALL_ACCESS);
            if (_winStation == nint.Zero)
            {
                _logger.LogWarning("Failed to open WinSta0: {Error}", Marshal.GetLastPInvokeError());
                return false;
            }

            if (!DesktopApi.SetProcessWindowStation(_winStation))
            {
                _logger.LogWarning("Failed to set process window station: {Error}", Marshal.GetLastPInvokeError());
                return false;
            }

            // Open both desktops
            const uint desktopAccess = DesktopApi.DESKTOP_READOBJECTS |
                                        DesktopApi.DESKTOP_CREATEWINDOW |
                                        DesktopApi.DESKTOP_WRITEOBJECTS |
                                        DesktopApi.DESKTOP_SWITCHDESKTOP;

            _defaultDesktop = DesktopApi.OpenDesktop("Default", 0, false, desktopAccess);
            _winlogonDesktop = DesktopApi.OpenDesktop("Winlogon", 0, false, desktopAccess);

            if (_defaultDesktop == nint.Zero)
                _logger.LogWarning("Failed to open Default desktop: {Error}", Marshal.GetLastPInvokeError());
            if (_winlogonDesktop == nint.Zero)
                _logger.LogWarning("Failed to open Winlogon desktop: {Error}", Marshal.GetLastPInvokeError());

            _logger.LogInformation("DesktopManager initialized. Default={Default}, Winlogon={Winlogon}",
                _defaultDesktop != nint.Zero, _winlogonDesktop != nint.Zero);

            return _defaultDesktop != nint.Zero;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DesktopManager initialization failed");
            return false;
        }
    }

    /// <summary>
    /// Detects which desktop is currently active (shown on screen) and switches
    /// the calling thread to that desktop so SendInput targets it.
    /// </summary>
    public ActiveDesktop SwitchToActiveDesktop()
    {
        // Try to switch to Winlogon — if it succeeds, the login/lock screen is showing
        if (_winlogonDesktop != nint.Zero)
        {
            // SwitchDesktop returns true if the desktop is the input desktop
            // But we don't want to SWITCH desktops, just detect which is active.
            // Instead, we try SetThreadDesktop to Winlogon — if the Winlogon desktop
            // is active, SendInput there will work. If Default is active, we attach to Default.
            //
            // Heuristic: try to detect the lock screen via the foreground window.
            // On the Winlogon desktop, there won't be a foreground window accessible from
            // the Default desktop.
        }

        // Simple approach: check if we can get a foreground window on Default desktop
        // If GetForegroundWindow returns null/0 AND the Winlogon desktop is available,
        // we're likely on the lock screen.
        var fgWindow = InputApi.GetForegroundWindow();
        bool likelyLockScreen = fgWindow == nint.Zero;

        var target = likelyLockScreen && _winlogonDesktop != nint.Zero
            ? ActiveDesktop.Winlogon
            : ActiveDesktop.Default;

        if (target == _currentDesktop)
            return _currentDesktop;

        var desktopHandle = target == ActiveDesktop.Winlogon ? _winlogonDesktop : _defaultDesktop;
        if (desktopHandle == nint.Zero)
            return _currentDesktop;

        if (DesktopApi.SetThreadDesktop(desktopHandle))
        {
            _currentDesktop = target;
            _logger.LogInformation("Switched to {Desktop} desktop", target);
        }
        else
        {
            _logger.LogWarning("Failed to switch to {Desktop}: {Error}", target, Marshal.GetLastPInvokeError());
        }

        return _currentDesktop;
    }

    /// <summary>
    /// Explicitly attach the calling thread to the specified desktop.
    /// </summary>
    public bool SwitchTo(ActiveDesktop desktop)
    {
        var handle = desktop == ActiveDesktop.Winlogon ? _winlogonDesktop : _defaultDesktop;
        if (handle == nint.Zero) return false;

        if (DesktopApi.SetThreadDesktop(handle))
        {
            _currentDesktop = desktop;
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        // Restore original desktop/station
        if (_originalDesktop != nint.Zero)
            DesktopApi.SetThreadDesktop(_originalDesktop);
        if (_originalStation != nint.Zero)
            DesktopApi.SetProcessWindowStation(_originalStation);

        if (_defaultDesktop != nint.Zero) DesktopApi.CloseDesktop(_defaultDesktop);
        if (_winlogonDesktop != nint.Zero) DesktopApi.CloseDesktop(_winlogonDesktop);
    }
}

public enum ActiveDesktop
{
    Unknown,
    Default,
    Winlogon,
}
