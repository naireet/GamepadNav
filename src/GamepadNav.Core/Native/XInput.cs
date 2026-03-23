using System.Runtime.InteropServices;

namespace GamepadNav.Core.Native;

/// <summary>
/// P/Invoke declarations for XInput (xinput1_4.dll).
/// </summary>
public static partial class XInput
{
    private const string Lib = "xinput1_4.dll";

    public const int ERROR_SUCCESS = 0;
    public const int ERROR_DEVICE_NOT_CONNECTED = 1167;

    [LibraryImport(Lib, EntryPoint = "XInputGetState")]
    public static partial uint GetState(uint dwUserIndex, out XINPUT_STATE pState);

    [LibraryImport(Lib, EntryPoint = "XInputGetCapabilities")]
    public static partial uint GetCapabilities(uint dwUserIndex, uint dwFlags, out XINPUT_CAPABILITIES pCapabilities);

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_CAPABILITIES
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public XINPUT_GAMEPAD Gamepad;
        public XINPUT_VIBRATION Vibration;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_VIBRATION
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    /// <summary>
    /// Undocumented XInput function (ordinal #100) that also reports the Guide button.
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "#100")]
    public static partial uint GetStateEx(uint dwUserIndex, out XINPUT_STATE pState);
}
