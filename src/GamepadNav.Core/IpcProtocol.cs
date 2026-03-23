using System.Text.Json;

namespace GamepadNav.Core;

/// <summary>
/// Messages exchanged between the service and tray app over named pipes.
/// </summary>
public static class IpcProtocol
{
    public const string PipeName = "GamepadNav";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize<T>(T message) where T : IpcMessage =>
        JsonSerializer.Serialize(message, message.GetType(), JsonOptions);

    public static IpcMessage? Deserialize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("type").GetString();
        return type switch
        {
            "status" => JsonSerializer.Deserialize<StatusMessage>(json, JsonOptions),
            "command" => JsonSerializer.Deserialize<CommandMessage>(json, JsonOptions),
            "controllerState" => JsonSerializer.Deserialize<ControllerStateMessage>(json, JsonOptions),
            _ => null,
        };
    }
}

public abstract class IpcMessage
{
    public abstract string Type { get; }
}

/// <summary>
/// Service → App: current status of GamepadNav.
/// </summary>
public class StatusMessage : IpcMessage
{
    public override string Type => "status";
    public bool Enabled { get; set; }
    public bool ControllerConnected { get; set; }
    public bool GameDetected { get; set; }
    public string? CurrentGame { get; set; }
    public bool KeyboardVisible { get; set; }
}

/// <summary>
/// App → Service or Service → App: toggle commands.
/// </summary>
public class CommandMessage : IpcMessage
{
    public override string Type => "command";
    public string Action { get; set; } = "";

    // Known actions:
    // "toggle" — flip enabled state
    // "enable" / "disable" — explicit set
    // "showKeyboard" / "hideKeyboard" — keyboard overlay
    // "showNumpad" / "hideNumpad" — numpad mode
}

/// <summary>
/// Service → App: controller state for keyboard overlay navigation.
/// Sent only when keyboard overlay is visible.
/// </summary>
public class ControllerStateMessage : IpcMessage
{
    public override string Type => "controllerState";
    public float LeftStickX { get; set; }
    public float LeftStickY { get; set; }
    public ushort Buttons { get; set; }
}
