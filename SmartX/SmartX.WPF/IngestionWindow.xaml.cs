using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartX.WPF.Services;

namespace SmartX.WPF;

public partial class IngestionWindow : Window
{
    private readonly ApiService _api = new();
    private readonly Random _rand = new();
    private string? _pendingFilePath;
    private readonly List<DispatcherTimer> _sensorTimers = new();

    public ObservableCollection<SensorDisplay> Sensors { get; set; } = new();

    public IngestionWindow()
    {
        InitializeComponent();

        //Hardcoded
        var sensor1 = new SensorDisplay { Id = "Hardcore-1", MacAddress = "AB:BC:CD:DE:EF:FG", Location = "Garden-Zone1-NodeA", Category = "Environmental" };
        var sensor2 = new SensorDisplay { Id = "Hardcore-2", MacAddress = "AA:BB:CC:DD:EE:FF", Location = "Plant-Zone2-NodeB", Category = "Power" };
        Sensors.Add(sensor1);
        Sensors.Add(sensor2);

        SensorListView.ItemsSource = Sensors;

        StartSensorTimer(sensor1);
        StartSensorTimer(sensor2);
    }

    
    private void StartSensorTimer(SensorDisplay sensor)
    {
        var initialDelay = _rand.Next(0, 3000); 
        var interval = 4000 + _rand.Next(0, 3000); 

        var startTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(initialDelay) };
        startTimer.Tick += (s, e) =>
        {
            startTimer.Stop();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
            timer.Tick += (s2, e2) => SendTelemetryFor(sensor);
            timer.Start();
            _sensorTimers.Add(timer);

            SendTelemetryFor(sensor);
        };
        startTimer.Start();
        _sensorTimers.Add(startTimer);
    }

    private async void SendTelemetryFor(SensorDisplay sensor)
    {
        double numericValue;
        string valueText;

        switch (sensor.Category)
        {
            case "Environmental":
                numericValue = Math.Round(20 + _rand.NextDouble() * 15, 1); 
                valueText = $"{numericValue}\u00B0C";
                await _api.SendTelemetryFloatAsync(new { DeviceId = sensor.MacAddress, Value = (float)numericValue, SensorType = "float" });
                break;

            case "Power":
                numericValue = _rand.Next(100, 2000);
                valueText = $"{numericValue}W";
                await _api.SendTelemetryIntAsync(new { DeviceId = sensor.MacAddress, Value = (int)numericValue, SensorType = "int" });
                break;

            case "Actuator":
                numericValue = _rand.Next(1, 4); 
                valueText = $"State {numericValue}";
                await _api.SendTelemetryIntAsync(new { DeviceId = sensor.MacAddress, Value = (int)numericValue, SensorType = "int" });
                break;

            default:
                numericValue = 0;
                valueText = "n/a";
                break;
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        sensor.CurrentValue = valueText;
        sensor.LastUpdated = timestamp;
        Pulse(sensor);

        
        var (isAlert, reason) = CheckThreshold(sensor.Category, numericValue);
        if (isAlert)
        {
            AlertsList.Items.Insert(0,
                $"[ALERT] {sensor.MacAddress} ({sensor.Location}) {reason} \u2014 value: {valueText} at {timestamp}");
        }
    }

    
    private (bool isAlert, string reason) CheckThreshold(string category, double value)
    {
        switch (category)
        {
            case "Environmental":
                if (value <= 22) return (true, "The temp is too low");
                if (value >= 34) return (true, "yhe temp is too high");
                return (false, "");

            case "Power":
                if (value <= 300) return (true, "The power is too low");
                if (value >= 1800) return (true, "The power is too high");
                return (false, "");

            case "Actuator":
                if (value == 1) return (true, "actuator is in 1");
                if (value == 3) return (true, "actuator is in state 3");
                return (false, "");

            default:
                return (false, "");
        }
    }

    private void Pulse(SensorDisplay sensor)
    {
        sensor.PulseColor = Brushes.LimeGreen;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (s, e) =>
        {
            sensor.PulseColor = Brushes.Gray;
            timer.Stop();
        };
        timer.Start();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in _sensorTimers) t.Stop();
        var mainWindow = new MainWindow();
        mainWindow.Show();
        this.Close();
    }

    private void ToggleCreate_Click(object sender, RoutedEventArgs e)
    {
        CreatePanel.Visibility = CreatePanel.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog();
        if (dialog.ShowDialog() == true)
        {
            _pendingFilePath = dialog.FileName;
            ChosenFileText.Text = System.IO.Path.GetFileName(dialog.FileName);
        }
    }

    private void CancelCreate_Click(object sender, RoutedEventArgs e)
    {
        MacBox.Text = "";
        LocationBox.Text = "";
        CategoryBox.SelectedIndex = -1;
        _pendingFilePath = null;
        ChosenFileText.Text = "you never selected a file";
        CreatePanel.Visibility = Visibility.Collapsed;
    }

    private async void SaveSensor_Click(object sender, RoutedEventArgs e)
    {
        var category = (CategoryBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Environmental";

        var sensor = new
        {
            MacAddress = MacBox.Text,
            Location = LocationBox.Text,
            Category = category
        };

        var response = await _api.RegisterSensorAsync(sensor);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            StatusText.Text = $"Failed: {body}";
            return;
        }

        var doc = System.Text.Json.JsonDocument.Parse(body);
        var newSensorId = doc.RootElement.GetProperty("id").GetString() ?? "";

        var newSensor = new SensorDisplay
        {
            Id = newSensorId,
            MacAddress = MacBox.Text,
            Location = LocationBox.Text,
            Category = category
        };

        Sensors.Add(newSensor);
        StartSensorTimer(newSensor);
        StatusText.Text = "A sensor has been created.";

        if (!string.IsNullOrEmpty(_pendingFilePath))
        {
            var uploadResponse = await _api.UploadFileAsync(newSensorId, _pendingFilePath);
            StatusText.Text = uploadResponse.IsSuccessStatusCode
                ? "A sensor was created and your file was uploaded aswell."
                : "your sensor was created but the file did not upload.";
        }

        MacBox.Text = "";
        LocationBox.Text = "";
        CategoryBox.SelectedIndex = -1;
        _pendingFilePath = null;
        ChosenFileText.Text = "You didnt chose a file";
        CreatePanel.Visibility = Visibility.Collapsed;
    }

    private void TestOverload_Click(object sender, RoutedEventArgs e)
    {
        var m1 = new Reading { Name = "MeterA", Value = 45.5 };
        var m2 = new Reading { Name = "MeterB", Value = 30.2 };
        StatusText.Text = $"[OVERLOAD] {m1 + m2}";
    }

    private async void ValidateTree_Click(object sender, RoutedEventArgs e)
    {
        var tree = new
        {
            Name = "FacilityA",
            IsConfigured = true,
            Children = new[]
            {
                new { Name = "Zone1", IsConfigured = true, Children = Array.Empty<object>() }
            }
        };

        var response = await _api.ValidateDeploymentAsync(tree);
        var body = await response.Content.ReadAsStringAsync();
        StatusText.Text = $"[RECURSION] {body}";
    }

    private class Reading
    {
        public string Name { get; set; } = "";
        public double Value { get; set; }
        public static Reading operator +(Reading a, Reading b) =>
            new() { Name = $"{a.Name}+{b.Name}", Value = a.Value + b.Value };
        public override string ToString() => $"{Name}: {Value}";
    }
}