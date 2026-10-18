using System.Windows;
using System.Windows.Controls;
using SmartX.WPF.Services;

namespace SmartX.WPF.Views;

public partial class HomeView : UserControl
{
    public event Action<string>? NavigateRequested;

    public HomeView()
    {
        InitializeComponent();
        AppState.Instance.Refreshed += UpdateSummary;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var state = AppState.Instance;

        if (!state.ApiOnline)
        {
            SummaryText.Text = "Waiting for the API...";
            return;
        }

        int online = state.Devices.Count(d => d.IsOnline);
        SummaryText.Text = $"{online} of {state.Devices.Count} devices online   |   {state.ActiveAlerts.Count} active alerts";
    }

    private void OpenIngestion_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("Ingestion");

    private void OpenCommands_Click(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("Commands");
}