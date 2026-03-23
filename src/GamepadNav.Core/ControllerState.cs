namespace GamepadNav.Core;

/// <summary>
/// Parsed state of an Xbox controller at a single point in time.
/// </summary>
public readonly struct ControllerState
{
    // Thumbsticks: normalized to -1.0..1.0 (dead zone already applied)
    public float LeftStickX { get; init; }
    public float LeftStickY { get; init; }
    public float RightStickX { get; init; }
    public float RightStickY { get; init; }

    // Triggers: normalized to 0.0..1.0
    public float LeftTrigger { get; init; }
    public float RightTrigger { get; init; }

    // Buttons (directly mapped from XINPUT_GAMEPAD wButtons bitmask)
    public GamepadButtons Buttons { get; init; }

    public bool IsConnected { get; init; }

    public bool IsButtonDown(GamepadButtons button) => (Buttons & button) != 0;

    /// <summary>
    /// True if any meaningful input is being given (stick, trigger, or button).
    /// </summary>
    public bool HasInput =>
        MathF.Abs(LeftStickX) > 0 || MathF.Abs(LeftStickY) > 0 ||
        MathF.Abs(RightStickX) > 0 || MathF.Abs(RightStickY) > 0 ||
        LeftTrigger > 0 || RightTrigger > 0 ||
        Buttons != GamepadButtons.None;
}

[Flags]
public enum GamepadButtons : ushort
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,   // L3
    RightThumb = 0x0080,  // R3
    LeftShoulder = 0x0100,  // LB
    RightShoulder = 0x0200, // RB
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}
