using System.ComponentModel;
using System.Windows.Media;

namespace SmartX.WPF;
public class SensorDisplay : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Location { get; set; } = "";
    public string Category { get; set; } = "";

    private string _currentValue = "—";
    public string CurrentValue
    {
        get => _currentValue;
        set
        {
            _currentValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentValue)));
        }
    }
    private Brush _pulseColor = Brushes.Gray;
    public Brush PulseColor
    {
        get => _pulseColor;
        set
        {
            _pulseColor = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PulseColor)));
        }
    }
    private string _lastUpdated = "not updated";
    public string LastUpdated
    {
        get => _lastUpdated;
        set
        {
            _lastUpdated = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastUpdated)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}