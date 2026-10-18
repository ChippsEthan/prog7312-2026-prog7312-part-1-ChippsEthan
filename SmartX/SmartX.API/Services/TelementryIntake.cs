using SmartX.API.Models;

namespace SmartX.API.Services;

public record IntakeResult(bool Accepted, string? Error, bool IsCritical, bool DeviceNotFound = false);

public class TelemetryIntake
{
    private readonly DeviceRegistry _registry;
    private readonly TelemetryQueues _queues;

    public TelemetryIntake(DeviceRegistry registry, TelemetryQueues queues)
    {
        _registry = registry;
        _queues = queues;
    }

    public IntakeResult Submit(string deviceId, double value, string sensorType)
    {
        if (!DeviceRegistry.IsValidMac(deviceId))
            return new IntakeResult(false, "DeviceId must be a valid MAC address (AA:BB:CC:DD:EE:FF).", false);

        var device = _registry.GetByMac(deviceId);   
        if (device is null)
            return new IntakeResult(false, $"Unknown device {deviceId}. Register it first.", false, DeviceNotFound: true);

        bool critical = ThresholdPolicy.Evaluate(device, value).Level == AlertLevel.Critical;

        _queues.Enqueue(new QueuedPacket(device.MacAddress, DateTime.UtcNow, value, sensorType, critical));
        return new IntakeResult(true, null, critical);
    }
}