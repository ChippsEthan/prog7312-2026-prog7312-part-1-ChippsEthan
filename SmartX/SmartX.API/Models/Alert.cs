namespace SmartX.API.Models;

public enum AlertLevel
{
    None,
    Warning,
    Critical
}

public static class AlertTypes
{
    public const string OutOfRange = "OutOfRange";
    public const string Disconnected = "Disconnected";
}

public readonly record struct AlertKey(string Mac, string Type);

public record ActiveAlert(
    string Mac,
    string DeviceName,
    string Type,
    AlertLevel Level,
    string Message,
    DateTime RaisedUtc);