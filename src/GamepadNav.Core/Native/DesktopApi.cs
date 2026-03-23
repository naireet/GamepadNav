using System.Runtime.InteropServices;

namespace GamepadNav.Core.Native;

/// <summary>
/// P/Invoke declarations for Window Station and Desktop APIs.
/// Used to switch between the Winlogon (login screen) and Default (user) desktops.
/// </summary>
public static partial class DesktopApi
{
    [LibraryImport("user32.dll", EntryPoint = "OpenDesktopW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenDesktop(string lpszDesktop, uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetThreadDesktop(nint hDesktop);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint GetThreadDesktop(uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseDesktop(nint hDesktop);

    [LibraryImport("user32.dll", EntryPoint = "OpenWindowStationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenWindowStation(string lpszWinSta, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessWindowStation(nint hWinSta);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint GetProcessWindowStation();

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    public static partial uint WTSGetActiveConsoleSessionId();

    // Desktop access rights
    public const uint DESKTOP_READOBJECTS = 0x0001;
    public const uint DESKTOP_CREATEWINDOW = 0x0002;
    public const uint DESKTOP_CREATEMENU = 0x0004;
    public const uint DESKTOP_HOOKCONTROL = 0x0008;
    public const uint DESKTOP_JOURNALRECORD = 0x0010;
    public const uint DESKTOP_JOURNALPLAYBACK = 0x0020;
    public const uint DESKTOP_ENUMERATE = 0x0040;
    public const uint DESKTOP_WRITEOBJECTS = 0x0080;
    public const uint DESKTOP_SWITCHDESKTOP = 0x0100;
    public const uint GENERIC_ALL = 0x10000000;

    public const uint WINSTA_ALL_ACCESS = 0x37F;
}
