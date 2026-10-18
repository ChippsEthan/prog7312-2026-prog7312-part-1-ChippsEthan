using SmartX.API.Models;

namespace SmartX.API.Services;

public class HistoryStore
{
    private const int MaxPerDevice = 500;

    private readonly Dictionary<string, SortedDictionary<DateTime, TelemetryReading>> _history = new();
    private readonly object _lock = new();

    public void Add(TelemetryReading reading)
    {
        var mac = DeviceRegistry.NormalizeMac(reading.DeviceId);

        lock (_lock)
        {
            if (!_history.TryGetValue(mac, out var log))
            {
                log = new SortedDictionary<DateTime, TelemetryReading>();
                _history[mac] = log;
            }

            
            var ts = reading.Timestamp;
            while (log.ContainsKey(ts)) ts = ts.AddTicks(1);

            log[ts] = reading with { DeviceId = mac, Timestamp = ts };

            
            while (log.Count > MaxPerDevice)
                log.Remove(log.Keys.First());
        }
    }

    public List<TelemetryReading> GetLatest(string mac, int count)
    {
        count = Math.Clamp(count, 1, MaxPerDevice);

        lock (_lock)
        {
            return _history.TryGetValue(DeviceRegistry.NormalizeMac(mac), out var log)
                ? log.Values.TakeLast(count).ToList()
                : new List<TelemetryReading>();
        }
    }
}