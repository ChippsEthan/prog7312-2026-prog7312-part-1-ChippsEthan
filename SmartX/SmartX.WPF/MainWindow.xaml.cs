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
        AuthService.Instance.SignedIn += OnSignedIn;
        AuthService.Instance.SignedOut += OnSignedOut;

        Navigate("Home");
        ShowLogin();
    }

    private void OnSignedIn()
    {
        UserEmailText.Text = AuthService.Instance.Email ?? "";
        StatusDot.Fill = Brushes.Gray;
        StatusLabel.Text = "Connecting to API...";

        var sim = AppState.Instance.Simulator;
        SimToggle.Content = sim.IsRunning ? "Pause device simulator" : "Resume device simulator";

        Login.Visibility = Visibility.Collapsed;
        AppShell.Visibility = Visibility.Visible;

        AppState.Instance.Start();
    }

    private void OnSignedOut(string message)
    {
        Navigate("Home");
        AppState.Instance.Stop();
        AppState.Instance.Reset();
        ShowLogin(message);
    }

    private void ShowLogin(string message = "")
    {
        AppShell.Visibility = Visibility.Collapsed;
        Login.Visibility = Visibility.Visible;
        Login.Prepare(message);
    }

    private void SignOut_Click(object sender, RoutedEventArgs e) =>
        AuthService.Instance.SignOut("You have been signed out.");

    private void CopyToken_Click(object sender, RoutedEventArgs e)
    {
        var token = AuthService.Instance.IdToken;
        if (string.IsNullOrEmpty(token)) return;

        Clipboard.SetText(token);
        MessageBox.Show("Your API token was copied.\n\nIn Swagger click Authorize and paste it in.\n(It expires after about an hour.)",
                        "Token copied", MessageBoxButton.OK, MessageBoxImage.Information);
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