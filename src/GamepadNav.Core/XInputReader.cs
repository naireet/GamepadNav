namespace GamepadNav.Core;

/// <summary>
/// Reads XInput state and normalizes it into a <see cref="ControllerState"/>.
/// </summary>
public class XInputReader
{
    private readonly float _deadZone;

    public XInputReader(float deadZone = 0.15f)
    {
        _deadZone = deadZone;
    }

    /// <summary>
    /// Reads the current state of the specified controller.
    /// Uses the undocumented XInputGetStateEx to capture the Guide button.
    /// </summary>
    public ControllerState Read(int controllerIndex = 0)
    {
        var result = Native.XInput.GetStateEx((uint)controllerIndex, out var state);
        if (result != Native.XInput.ERROR_SUCCESS)
        {
            return new ControllerState { IsConnected = false };
        }

        var gp = state.Gamepad;

        return new ControllerState
        {
            IsConnected = true,
            LeftStickX = ApplyDeadZone(gp.sThumbLX / 32767f),
            LeftStickY = ApplyDeadZone(gp.sThumbLY / 32767f),
            RightStickX = ApplyDeadZone(gp.sThumbRX / 32767f),
            RightStickY = ApplyDeadZone(gp.sThumbRY / 32767f),
            LeftTrigger = gp.bLeftTrigger / 255f,
            RightTrigger = gp.bRightTrigger / 255f,
            Buttons = (GamepadButtons)gp.wButtons,
        };
    }

    private float ApplyDeadZone(float value)
    {
        float abs = MathF.Abs(value);
        if (abs < _deadZone) return 0f;

        // Remap the range [deadZone..1.0] to [0..1.0] for smooth response
        float sign = MathF.Sign(value);
        float remapped = (abs - _deadZone) / (1f - _deadZone);
        return sign * MathF.Min(remapped, 1f);
    }
}
