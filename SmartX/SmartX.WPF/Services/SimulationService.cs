using System.Windows.Threading;

namespace SmartX.WPF.Services;
public class SimulationService
{
    private class SimState
    {
        public double Value;
        public DateTime NextSend;
        public bool Silenced;
        public int DriftRemaining;
        public int ForcedRemaining;
        public double ForcedValue;
    }

    private readonly AppState _state;
    private readonly Random _rand = new();
    private readonly Dictionary<string, SimState> _sims = new();
    private readonly DispatcherTimer _timer;

    public bool IsRunning { get; set; } = true;
    public bool RandomAnomalies { get; set; } = true;

    public SimulationService(AppState state)
    {
        _state = state;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Reset() => _sims.Clear();

    private void Tick()
    {
        if (!IsRunning || !_state.ApiOnline) return;

        var now = DateTime.UtcNow;

        foreach (var device in _state.Devices.ToList())
        {
            var sim = GetSim(device);
            if (sim.Silenced || now < sim.NextSend) continue;

            sim.NextSend = now.AddMilliseconds(2000 + _rand.Next(0, 1200));
            _ = SendAsync(device, NextValue(device, sim));
        }
    }

   

    public async Task<bool> InjectCriticalAsync(string mac)
    {
        var device = _state.Devices.FirstOrDefault(d => d.MacAddress == mac);
        if (device is null) return false;

        var critical = CriticalValue(device.Metric);
        if (double.IsNaN(critical)) return false;

        var sim = GetSim(device);
        sim.Silenced = false;
        sim.ForcedValue = critical;
        sim.ForcedRemaining = 3;                     
        sim.NextSend = DateTime.UtcNow.AddMilliseconds(2500);

        await SendAsync(device, critical);
        return true;
    }

    
    public bool StartDrift(string mac)
    {
        var device = _state.Devices.FirstOrDefault(d => d.MacAddress == mac);
        if (device is null || double.IsNaN(CriticalValue(device.Metric))) return false;

        GetSim(device).DriftRemaining = 8;
        return true;
    }

   
    public void SetSilenced(string mac, bool silenced)
    {
        var device = _state.Devices.FirstOrDefault(d => d.MacAddress == mac);
        if (device is not null) GetSim(device).Silenced = silenced;
    }

    public bool IsSilenced(string mac) => _sims.TryGetValue(mac, out var s) && s.Silenced;

   

    private SimState GetSim(SensorDisplay d)
    {
        if (!_sims.TryGetValue(d.MacAddress, out var sim))
        {
            sim = new SimState
            {
                Value = Baseline(d.Metric) + (_rand.NextDouble() - 0.5) * Noise(d.Metric),
                NextSend = DateTime.UtcNow.AddMilliseconds(_rand.Next(0, 2500))   
            };
            _sims[d.MacAddress] = sim;
        }
        return sim;
    }

    private double NextValue(SensorDisplay d, SimState s)
    {
        if (d.Category == "Actuator") return d.IsOn ? 1 : 0;   

        if (s.ForcedRemaining > 0)
        {
            s.ForcedRemaining--;
            return Clamp(d.Metric, s.ForcedValue + (_rand.NextDouble() - 0.5) * Noise(d.Metric) * 0.2);
        }

        if (s.DriftRemaining > 0)
        {
            s.DriftRemaining--;
            s.Value += DriftStep(d.Metric) + (_rand.NextDouble() - 0.5) * Noise(d.Metric) * 0.2;
        }
        else
        {
          
            s.Value += (Baseline(d.Metric) - s.Value) * 0.25 + (_rand.NextDouble() - 0.5) * Noise(d.Metric);

            if (RandomAnomalies && _rand.NextDouble() < 0.004)
                s.DriftRemaining = 8;
        }

        s.Value = Clamp(d.Metric, s.Value);
        return s.Value;
    }

    private async Task SendAsync(SensorDisplay d, double value)
    {
        try
        {
            switch (d.Category)
            {
                case "Environmental":
                    await _state.Api.SendTelemetryFloatAsync(new { DeviceId = d.MacAddress, Value = (float)value, SensorType = "float" });
                    break;
                case "Power":
                    await _state.Api.SendTelemetryIntAsync(new { DeviceId = d.MacAddress, Value = (int)Math.Round(value), SensorType = "int" });
                    break;
                case "Actuator":
                    await _state.Api.SendTelemetryBoolAsync(new { DeviceId = d.MacAddress, Value = value > 0.5, SensorType = "bool" });
                    break;
            }
        }
        catch (Exception)
        {
          
        }
    }

    private static double Baseline(string metric) => metric switch
    {
        "Moisture" => 55,
        "Temperature" => 28,
        "Power" => 900,
        _ => 0
    };

    private static double Noise(string metric) => metric switch
    {
        "Moisture" => 4,
        "Temperature" => 1.2,
        "Power" => 140,
        _ => 0
    };

    private static double DriftStep(string metric) => metric switch
    {
        "Moisture" => -6,
        "Temperature" => 2.2,
        "Power" => 260,
        _ => 0
    };

    private static double CriticalValue(string metric) => metric switch
    {
        "Moisture" => 5,
        "Temperature" => 46,
        "Power" => 3300,
        _ => double.NaN
    };

    private static double Clamp(string metric, double v) => metric switch
    {
        "Moisture" => Math.Clamp(v, 0, 100),
        "Temperature" => Math.Clamp(v, -10, 80),
        "Power" => Math.Clamp(v, 0, 5000),
        _ => v
    };
}