using SmartX.API.Models;

namespace SmartX.API.Services;

public class AlertTracker
{
    private readonly HashSet<AlertKey> _active = new();
    private readonly Dictionary<AlertKey, ActiveAlert> _details = new();
    private readonly object _lock = new();

    public bool Raise(string mac, string deviceName, string type, AlertLevel level, string message)
    {
        var key = new AlertKey(DeviceRegistry.NormalizeMac(mac), type);

        lock (_lock)
        {
            bool isNew = _active.Add(key);   

            if (isNew)
                _details[key] = new ActiveAlert(key.Mac, deviceName, type, level, message, DateTime.UtcNow);
            else
                _details[key] = _details[key] with { Level = level, Message = message };  

            return isNew;
        }
    }

    public bool Clear(string mac, string type)
    {
        var key = new AlertKey(DeviceRegistry.NormalizeMac(mac), type);

        lock (_lock)
        {
            if (!_active.Remove(key)) return false;
            _details.Remove(key);
            return true;
        }
    }

    public int ClearAll(string mac)
    {
        var normalized = DeviceRegistry.NormalizeMac(mac);

        lock (_lock)
        {
            var keys = _active.Where(k => k.Mac == normalized).ToList();
            foreach (var k in keys)
            {
                _active.Remove(k);
                _details.Remove(k);
            }
            return keys.Count;
        }
    }

    public bool IsActive(string mac, string type)
    {
        lock (_lock)
        {
            return _active.Contains(new AlertKey(DeviceRegistry.NormalizeMac(mac), type));
        }
    }

    public List<ActiveAlert> GetActive()
    {
        lock (_lock)
        {
            return _details.Values
                .OrderByDescending(a => a.Level)
                .ThenBy(a => a.RaisedUtc)
                .ToList();
        }
    }
}