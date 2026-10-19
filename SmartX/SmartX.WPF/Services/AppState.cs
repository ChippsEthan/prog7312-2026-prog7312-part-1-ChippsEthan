using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using SmartX.WPF.Models;

namespace SmartX.WPF.Services;

public sealed class AppState
{
    public static AppState Instance { get; } = new();

    public ApiService Api { get; } = new();
    public ObservableCollection<SensorDisplay> Devices { get; } = new();
    public ObservableCollection<string> AlertFeed { get; } = new();
    public List<AlertDto> ActiveAlerts { get; private set; } = new();
    public bool ApiOnline { get; private set; }
    public SimulationService Simulator { get; }

    public event Action? Refreshed;
    public event Action<bool>? ConnectionChanged;

    private readonly Dictionary<string, SensorDisplay> _byMac = new();   
    private readonly DispatcherTimer _timer;
    private bool _refreshing;
    private bool _started;
    private bool? _lastOnline;

    private AppState()
    {
        Simulator = new SimulationService(this);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshAsync();

        AuthService.Instance.TokenChanged += token => Api.SetToken(token);
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        _timer.Start();
        _ = RefreshAsync();
        Simulator.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Simulator.Stop();
        _started = false;
    }
    public void Reset()
    {
        Devices.Clear();
        _byMac.Clear();
        AlertFeed.Clear();
        ActiveAlerts = new List<AlertDto>();
        ApiOnline = false;
        _lastOnline = null;
        Simulator.Reset();
    }

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            List<SensorDto> devices;
            List<AlertDto> alerts;

            try
            {
                var devicesTask = Api.GetDevicesAsync();
                var alertsTask = Api.GetAlertsAsync();
                await Task.WhenAll(devicesTask, alertsTask);
                devices = devicesTask.Result;
                alerts = alertsTask.Result;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthService.Instance.SignOut(
                    "The API rejected your login. Check that Firebase:ProjectId in SmartX.API/appsettings.json matches your Firebase project.");
                return;
            }
            catch (Exception)
            {
                SetOnline(false);
                return;
            }

            if (!AuthService.Instance.IsSignedIn) return;

            ActiveAlerts = alerts;
            MergeDevices(devices, alerts);
            BuildAlertFeed(alerts);
            SetOnline(true);
            Refreshed?.Invoke();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void MergeDevices(List<SensorDto> dtos, List<AlertDto> alerts)
    {
        var levels = new Dictionary<string, string>();
        foreach (var a in alerts)
        {
            if (!levels.TryGetValue(a.Mac, out var existing) || Rank(a.Level) > Rank(existing))
                levels[a.Mac] = a.Level;
        }

        foreach (var dto in dtos)
        {
            if (!_byMac.TryGetValue(dto.MacAddress, out var display))
            {
                display = new SensorDisplay
                {
                    Id = dto.Id,
                    MacAddress = dto.MacAddress,
                    Name = dto.Name,
                    Location = dto.Location,
                    Category = dto.Category,
                    Metric = dto.Metric,
                    Unit = dto.Unit
                };
                _byMac[dto.MacAddress] = display;
                Devices.Add(display);
            }

            display.AlertLevel = levels.TryGetValue(dto.MacAddress, out var level) ? level : "None";

            if (display.Apply(dto))
                display.Pulse();
        }
    }

    private void BuildAlertFeed(List<AlertDto> alerts)
    {
        var lines = alerts
            .Select(a => $"[{a.Level.ToUpperInvariant()}] {a.DeviceName} ({a.Mac}): {a.Message}  -  since {a.RaisedUtc.ToLocalTime():HH:mm:ss}")
            .ToList();

        if (AlertFeed.SequenceEqual(lines)) return; 

        AlertFeed.Clear();
        foreach (var line in lines) AlertFeed.Add(line);
    }

    private void SetOnline(bool online)
    {
        ApiOnline = online;
        if (_lastOnline == online) return;

        _lastOnline = online;
        ConnectionChanged?.Invoke(online);
    }

    private static int Rank(string level) => level switch
    {
        "Critical" => 2,
        "Warning" => 1,
        _ => 0
    };
}