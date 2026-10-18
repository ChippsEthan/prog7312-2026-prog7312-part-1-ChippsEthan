using System.Windows.Controls;
using SmartX.WPF.Services;

namespace SmartX.WPF.Views;

public partial class CommandStreamView : UserControl
{
    public CommandStreamView()
    {
        InitializeComponent();
        AppState.Instance.Refreshed += Update;
        Update();
    }

    private void Update()
    {
        var state = AppState.Instance;
        SharedStateText.Text = $"Shared state check: {state.Devices.Count} devices loaded, {state.ActiveAlerts.Count} active alerts.";
    }
}