using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartX.WPF.Services;
using SmartX.WPF.Views;

namespace SmartX.WPF;

public partial class MainWindow : Window
{
    
    private readonly HomeView _home = new();
    private readonly IngestionView _ingestion = new();
    private readonly CommandStreamView _commands = new();

    public MainWindow()
    {
        InitializeComponent();

        _home.NavigateRequested += Navigate;
        AppState.Instance.ConnectionChanged += OnConnectionChanged;
        AppState.Instance.Start();

        Navigate("Home");
    }

    public void Navigate(string page)
    {
        switch (page)
        {
            case "Ingestion":
                PageHost.Content = _ingestion;
                NavIngestion.IsChecked = true;
                break;
            case "Commands":
                PageHost.Content = _commands;
                NavCommands.IsChecked = true;
                break;
            default:
                PageHost.Content = _home;
                NavHome.IsChecked = true;
                break;
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string page)
            Navigate(page);
    }

    private void OnConnectionChanged(bool online)
    {
        StatusDot.Fill = online ? Brushes.LimeGreen : Brushes.Red;
        StatusLabel.Text = online ? "API connected" : "API offline. Start SmartX.API.";
    }

    private void SimToggle_Click(object sender, RoutedEventArgs e)
    {
        var sim = AppState.Instance.Simulator;
        sim.IsRunning = !sim.IsRunning;
        SimToggle.Content = sim.IsRunning ? "Pause device simulator" : "Resume device simulator";
    }
}