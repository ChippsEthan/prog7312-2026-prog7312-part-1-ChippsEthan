namespace SmartX.API.Models;


public record TelemetryReading(string DeviceId, DateTime Timestamp, double Value, string SensorType);