using System.Text.RegularExpressions;
using SmartX.API.Models;

namespace SmartX.API.Services;

public record RegisterResult(bool Success, string? Error, bool IsConflict, Sensor? Sensor);


public class DeviceRegistry
{
    private static readonly Regex MacPattern =
        new(@"^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$", RegexOptions.Compiled);

    private readonly Dictionary<string, Sensor> _byMac = new();      // key = normalised MAC
    private readonly Dictionary<string, string> _macById = new();    // sensor Id -> MAC (used by file upload)
    private readonly object _lock = new();

    public static string NormalizeMac(string mac) => mac.Trim().Replace('-', ':').ToUpperInvariant();

    public static bool IsValidMac(string? mac) =>
        !string.IsNullOrWhiteSpace(mac) && MacPattern.IsMatch(mac.Trim());

    public RegisterResult Register(Sensor sensor)
    {
        if (!IsValidMac(sensor.MacAddress))
            return new(false, "MAC address must look like AA:BB:CC:DD:EE:FF.", false, null);

        if (string.IsNullOrWhiteSpace(sensor.Location))
            return new(false, "Location is required.", false, null);

        sensor.MacAddress = NormalizeMac(sensor.MacAddress);
        sensor.Id = Guid.NewGuid().ToString();

        if (string.IsNullOrWhiteSpace(sensor.Name))
            sensor.Name = $"{sensor.Category} {sensor.MacAddress[^5..]}";

        ApplyMetricDefaults(sensor);

        lock (_lock)
        {
            if (_byMac.ContainsKey(sensor.MacAddress))
                return new(false, $"A device with MAC {sensor.MacAddress} is already registered.", true, null);

            _byMac[sensor.MacAddress] = sensor;
            _macById[sensor.Id] = sensor.MacAddress;
            return new(true, null, false, sensor.Snapshot());
        }
    }

    public bool Exists(string mac)
    {
        lock (_lock)
        {
            return _byMac.ContainsKey(NormalizeMac(mac));
        }
    }

    public Sensor? GetByMac(string mac)
    {
        lock (_lock)
        {
            return _byMac.TryGetValue(NormalizeMac(mac), out var s) ? s.Snapshot() : null;
        }
    }

    public Sensor? GetById(string id)
    {
        lock (_lock)
        {
            return _macById.TryGetValue(id, out var mac) && _byMac.TryGetValue(mac, out var s)
                ? s.Snapshot()
                : null;
        }
    }

    public List<Sensor> GetAll()
    {
        lock (_lock)
        {
            return _byMac.Values.Select(s => s.Snapshot()).ToList();
        }
    }

    
    public Sensor? Update(string mac, Action<Sensor> change)
    {
        lock (_lock)
        {
            if (!_byMac.TryGetValue(NormalizeMac(mac), out var s)) return null;
            change(s);
            return s.Snapshot();
        }
    }

    public bool AddAttachment(string sensorId, string filePath)
    {
        lock (_lock)
        {
            if (!_macById.TryGetValue(sensorId, out var mac)) return false;
            _byMac[mac].AttachedFiles.Add(filePath);
            return true;
        }
    }

    private static void ApplyMetricDefaults(Sensor s)
    {
        if (string.IsNullOrWhiteSpace(s.Metric))
        {
            s.Metric = s.Category switch
            {
                SensorCategory.Environmental => "Temperature",
                SensorCategory.Power => "Power",
                SensorCategory.Actuator => "State",
                _ => "Value"
            };
        }

        if (string.IsNullOrWhiteSpace(s.Unit))
        {
            s.Unit = s.Metric switch
            {
                "Temperature" => "\u00B0C",
                "Moisture" => "%",
                "Power" => "W",
                _ => ""
            };
        }
    }

    
    public void SeedDefaults()
    {
        Register(new Sensor { MacAddress = "24:6F:28:A1:00:01", Name = "Moisture Sensor A", Location = "Hydroponics-Zone1-NodeA", Category = SensorCategory.Environmental, Metric = "Moisture" });
        Register(new Sensor { MacAddress = "24:6F:28:A1:00:02", Name = "Moisture Sensor B", Location = "Hydroponics-Zone1-NodeB", Category = SensorCategory.Environmental, Metric = "Moisture" });
        Register(new Sensor { MacAddress = "24:6F:28:A1:00:03", Name = "Greenhouse Temp", Location = "Greenhouse-Zone2-NodeA", Category = SensorCategory.Environmental, Metric = "Temperature" });
        Register(new Sensor { MacAddress = "24:6F:28:B2:00:01", Name = "Smart Meter 1", Location = "Grid-Zone3-NodeA", Category = SensorCategory.Power });
        Register(new Sensor { MacAddress = "24:6F:28:B2:00:02", Name = "Smart Meter 2", Location = "Grid-Zone3-NodeB", Category = SensorCategory.Power });
        Register(new Sensor { MacAddress = "24:6F:28:C3:00:01", Name = "Pump A", Location = "Hydroponics-Zone1-PumpA", Category = SensorCategory.Actuator });
        Register(new Sensor { MacAddress = "24:6F:28:C3:00:02", Name = "Smart Relay 1", Location = "Grid-Zone3-Relay1", Category = SensorCategory.Actuator });
    }
}