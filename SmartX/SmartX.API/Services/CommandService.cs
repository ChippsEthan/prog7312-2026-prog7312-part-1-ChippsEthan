using SmartX.API.Models;

namespace SmartX.API.Services;

public class CommandService
{
    private const int MaxLog = 100;

    private readonly DeviceRegistry _registry;
    private readonly Stack<DeviceCommand> _undoStack = new();
    private readonly List<DeviceCommand> _log = new();
    private readonly object _lock = new();
    private long _nextId;

    public CommandService(DeviceRegistry registry)
    {
        _registry = registry;
    }

    public int UndoDepth
    {
        get { lock (_lock) { return _undoStack.Count; } }
    }

    public string? PeekUndo()
    {
        lock (_lock)
        {
            return _undoStack.Count > 0 ? _undoStack.Peek().Summary : null;
        }
    }

    public CommandResult Execute(string mac, CommandAction action)
    {
        bool all = mac.Equals("ALL", StringComparison.OrdinalIgnoreCase);
        List<Sensor> targets;

        if (all)
        {
            if (action == CommandAction.Toggle)
                return new CommandResult(false, "Use TurnOn or TurnOff when targeting ALL.", null);

            targets = _registry.GetAll().Where(d => d.Category == SensorCategory.Actuator).ToList();
            if (targets.Count == 0)
                return new CommandResult(false, "There are no actuators registered.", null);
        }
        else
        {
            var device = _registry.GetByMac(mac);
            if (device is null)
                return new CommandResult(false, $"No device with MAC {mac}.", null);
            if (device.Category != SensorCategory.Actuator)
                return new CommandResult(false, $"{device.Name} is a sensor, not an actuator. Commands only apply to pumps and relays.", null);

            targets = new List<Sensor> { device };
        }

        lock (_lock)
        {
            var changes = new List<CommandChange>();

            foreach (var target in targets)
            {
                bool previous = false;
                bool next = false;

                _registry.Update(target.MacAddress, d =>
                {
                    previous = d.IsOn;
                    next = action switch
                    {
                        CommandAction.TurnOn => true,
                        CommandAction.TurnOff => false,
                        _ => !d.IsOn
                    };
                    d.IsOn = next;
                });

                changes.Add(new CommandChange(target.MacAddress, target.Name, previous, next));
            }

            string summary = all
                ? $"All actuators \u2192 {(changes[0].NewState ? "ON" : "OFF")}"
                : $"{changes[0].DeviceName} \u2192 {(changes[0].NewState ? "ON" : "OFF")}";

            var command = new DeviceCommand(++_nextId, action.ToString(), summary, changes, DateTime.UtcNow, false);

            _undoStack.Push(command);  
            AddToLog(command);
            return new CommandResult(true, null, command);
        }
    }

    public CommandResult Undo()
    {
        lock (_lock)
        {
            if (_undoStack.Count == 0)
                return new CommandResult(false, "Nothing to undo.", null);

            var last = _undoStack.Pop();   
            var reverted = new List<CommandChange>();

            foreach (var change in last.Changes)
            {
                _registry.Update(change.Mac, d => d.IsOn = change.PreviousState);
                reverted.Add(new CommandChange(change.Mac, change.DeviceName, change.NewState, change.PreviousState));
            }

            var entry = new DeviceCommand(++_nextId, "Undo", $"Undo: {last.Summary}", reverted, DateTime.UtcNow, true);
            AddToLog(entry);
            return new CommandResult(true, null, entry);
        }
    }

    public List<DeviceCommand> GetLog()
    {
        lock (_lock)
        {
            return _log.AsEnumerable().Reverse().ToList();
        }
    }

    private void AddToLog(DeviceCommand command)
    {
        _log.Add(command);
        if (_log.Count > MaxLog) _log.RemoveAt(0);
    }
}