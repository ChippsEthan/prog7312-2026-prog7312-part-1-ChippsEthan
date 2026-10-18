using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using SmartX.WPF.Models;

namespace SmartX.WPF;

public class SensorDisplay : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public string Id { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string Category { get; set; } = "";
    public string Metric { get; set; } = "";
    public string Unit { get; set; } = "";
    public bool IsActuator => Category == "Actuator";

    private string _currentValue = "-";
    public string CurrentValue { get => _currentValue; set => Set(ref _currentValue, value); }

    private string _lastUpdated = "waiting";
    public string LastUpdated { get => _lastUpdated; set => Set(ref _lastUpdated, value); }

    private Brush _pulseColor = Brushes.Gray;
    public Brush PulseColor { get => _pulseColor; set => Set(ref _pulseColor, value); }

    private string _statusText = "WAITING";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private Brush _statusBrush = Brushes.Gray;
    public Brush StatusBrush { get => _statusBrush; set => Set(ref _statusBrush, value); }

    private bool _isOnline;
    public bool IsOnline { get => _isOnline; set => Set(ref _isOnline, value); }

    private bool _isOn;
    public bool IsOn { get => _isOn; set => Set(ref _isOn, value); }

    private string _activeAlert = "";
    public string ActiveAlert { get => _activeAlert; set => Set(ref _activeAlert, value); }

    private string _alertLevel = "None";
    public string AlertLevel { get => _alertLevel; set { Set(ref _alertLevel, value); RefreshStatus(); } }

    private DateTime? _lastSeenUtc;

    public bool Apply(SensorDto dto)
    {
        Name = dto.Name;
        Location = dto.Location;
        Category = dto.Category;
        Metric = dto.Metric;
        Unit = dto.Unit;
        IsOnline = dto.IsOnline;
        IsOn = dto.IsOn;
        ActiveAlert = dto.ActiveAlert ?? "";

        bool fresh = dto.LastSeenUtc is not null && dto.LastSeenUtc != _lastSeenUtc;
        _lastSeenUtc = dto.LastSeenUtc;

        if (dto.LastSeenUtc is { } seen)
            LastUpdated = seen.ToLocalTime().ToString("HH:mm:ss");

        if (IsActuator)
            CurrentValue = dto.IsOn ? "ON" : "OFF";
        else
            CurrentValue = dto.LastValue is { } v ? $"{v:0.#}{dto.Unit}" : "-";

        RefreshStatus();
        return fresh;
    }

    private void RefreshStatus()
    {
        if (_lastSeenUtc is null)
        {
            StatusText = "WAITING";
            StatusBrush = Brushes.Gray;
        }
        else if (!_isOnline)
        {
            StatusText = "OFFLINE";
            StatusBrush = Brushes.SlateGray;
        }
        else if (_alertLevel == "Critical")
        {
            StatusText = "CRITICAL";
            StatusBrush = Brushes.Crimson;
        }
        else if (_alertLevel == "Warning")
        {
            StatusText = "WARNING";
            StatusBrush = Brushes.DarkOrange;
        }
        else if (IsActuator)
        {
            StatusText = _isOn ? "ON" : "OFF";
            StatusBrush = _isOn ? Brushes.SeaGreen : Brushes.Gray;
        }
        else
        {
            StatusText = "OK";
            StatusBrush = Brushes.SeaGreen;
        }
    }

    public void Pulse()
    {
        PulseColor = Brushes.LimeGreen;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            PulseColor = Brushes.Gray;
            timer.Stop();
        };
        timer.Start();
    }
}