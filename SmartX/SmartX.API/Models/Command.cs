namespace SmartX.API.Models;

public enum CommandAction
{
    TurnOn,
    TurnOff,
    Toggle
}

public record CommandRequest(string Mac, CommandAction Action);

public record CommandChange(string Mac, string DeviceName, bool PreviousState, bool NewState);

public record DeviceCommand(
    long Id,
    string Action,
    string Summary,
    List<CommandChange> Changes,
    DateTime IssuedUtc,
    bool IsUndo);

public record CommandResult(bool Success, string? Error, DeviceCommand? Command);