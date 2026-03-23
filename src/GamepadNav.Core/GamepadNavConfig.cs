using System.Text.Json.Serialization;

namespace GamepadNav.Core;

/// <summary>
/// Configuration for GamepadNav, loaded from config.json.
/// </summary>
public class GamepadNavConfig
{
    /// <summary>Dead zone for thumbsticks, 0.0-1.0. Default 0.15.</summary>
    public float StickDeadZone { get; set; } = 0.15f;

    /// <summary>Mouse cursor speed multiplier. Higher = faster.</summary>
    public float CursorSpeed { get; set; } = 15.0f;

    /// <summary>Acceleration exponent for stick-to-cursor. 1.0=linear, 2.0=quadratic.</summary>
    public float CursorAcceleration { get; set; } = 2.0f;

    /// <summary>Scroll speed multiplier for right stick.</summary>
    public float ScrollSpeed { get; set; } = 5.0f;

    /// <summary>Trigger threshold to count as "pressed", 0.0-1.0.</summary>
    public float TriggerThreshold { get; set; } = 0.3f;

    /// <summary>Controller index to read (0-3). Default 0.</summary>
    public int ControllerIndex { get; set; } = 0;

    /// <summary>Polling interval in milliseconds. Default ~16ms (60Hz).</summary>
    public int PollIntervalMs { get; set; } = 16;

    /// <summary>Process names that trigger auto-disable (case-insensitive, no .exe).</summary>
    public List<string> GameProcesses { get; set; } = [];

    /// <summary>Whether GamepadNav starts in enabled state.</summary>
    public bool StartEnabled { get; set; } = true;

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GamepadNav");

    public static string ConfigFilePath =>
        Path.Combine(ConfigDirectory, "config.json");
}
