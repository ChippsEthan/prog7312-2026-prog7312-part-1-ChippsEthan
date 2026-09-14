namespace SmartX.API.Models;
public class SensorReading
{
    public string DeviceId { get; set; } = string.Empty;
    public double Value { get; set; }
     public static SensorReading operator +(SensorReading a, SensorReading b)
    {
        return new SensorReading
        {
            DeviceId = $"{a.DeviceId}+{b.DeviceId}",
            Value = a.Value + b.Value
        };
    }
    public static SensorReading operator -(SensorReading a, SensorReading b)
    {
        return new SensorReading
        {
            DeviceId = $"{a.DeviceId}-{b.DeviceId}",
            Value = a.Value - b.Value
        };
    }
    public override string ToString() => $"{DeviceId}: {Value}";
}