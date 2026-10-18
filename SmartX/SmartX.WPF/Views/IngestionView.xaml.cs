using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SmartX.WPF.Services;

namespace SmartX.WPF.Views;

public partial class IngestionView : UserControl
{
    private static readonly Regex MacPattern = new(@"^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$");

    private readonly AppState _state = AppState.Instance;
    private string? _pendingFilePath;

    public IngestionView()
    {
        InitializeComponent();
        SensorListView.ItemsSource = _state.Devices;   
        AlertsList.ItemsSource = _state.AlertFeed;
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
        ResetForm();
        CreatePanel.Visibility = Visibility.Collapsed;
    }

    private async void SaveSensor_Click(object sender, RoutedEventArgs e)
    {
        FormError.Visibility = Visibility.Collapsed;

        var mac = MacBox.Text.Trim();
        var location = LocationBox.Text.Trim();

        
        if (!MacPattern.IsMatch(mac))
        {
            ShowError("MAC address must look like AA:BB:CC:DD:EE:FF.");
            return;
        }
        if (location.Length == 0)
        {
            ShowError("Please enter the deployment location.");
            return;
        }
        if (CategoryBox.SelectedItem is not ComboBoxItem categoryItem)
        {
            ShowError("Please choose a sensor category.");
            return;
        }

        var category = categoryItem.Content?.ToString() ?? "Environmental";
        var sensor = new { MacAddress = mac, Location = location, Category = category };

        HttpResponseMessage response;
        try
        {
            response = await _state.Api.RegisterSensorAsync(sensor);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ShowError("Cannot reach the API. Is SmartX.API running?");
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            ShowError(body.Trim('"'));   
            return;
        }

        using var doc = JsonDocument.Parse(body);
        var newSensorId = doc.RootElement.GetProperty("id").GetString() ?? "";

        StatusText.Text = "Sensor registered.";

        if (!string.IsNullOrEmpty(_pendingFilePath))
        {
            try
            {
                var upload = await _state.Api.UploadFileAsync(newSensorId, _pendingFilePath);
                StatusText.Text = upload.IsSuccessStatusCode
                    ? "Sensor registered and file uploaded."
                    : "Sensor registered, but the file upload failed.";
            }
            catch (Exception)
            {
                StatusText.Text = "Sensor registered, but the file upload failed.";
            }
        }

        ResetForm();
        CreatePanel.Visibility = Visibility.Collapsed;

        await _state.RefreshAsync();   
    }

    private void ShowError(string message)
    {
        FormError.Text = message;
        FormError.Visibility = Visibility.Visible;
    }

    private void ResetForm()
    {
        MacBox.Text = "";
        LocationBox.Text = "";
        CategoryBox.SelectedIndex = -1;
        _pendingFilePath = null;
        ChosenFileText.Text = "No file selected";
        FormError.Visibility = Visibility.Collapsed;
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
                new
                {
                    Name = "Zone1",
                    IsConfigured = true,
                    Children = new[]
                    {
                        new { Name = "SubZoneB", IsConfigured = true, Children = Array.Empty<object>() }
                    }
                }
            }
        };

        await ShowValidationAsync(tree);
    }

    private async void ValidateBrokenTree_Click(object sender, RoutedEventArgs e)
    {
        
        var tree = new
        {
            Name = "FacilityA",
            IsConfigured = true,
            Children = new[]
            {
                new
                {
                    Name = "Zone1",
                    IsConfigured = true,
                    Children = new[]
                    {
                        new { Name = "SubZoneB", IsConfigured = false, Children = Array.Empty<object>() }
                    }
                }
            }
        };

        await ShowValidationAsync(tree);
    }

    private async Task ShowValidationAsync(object tree)
    {
        try
        {
            var response = await _state.Api.ValidateDeploymentAsync(tree);
            var body = await response.Content.ReadAsStringAsync();
            StatusText.Text = $"[RECURSION] {body}";
        }
        catch (Exception)
        {
            StatusText.Text = "Cannot reach the API. Is SmartX.API running?";
        }
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