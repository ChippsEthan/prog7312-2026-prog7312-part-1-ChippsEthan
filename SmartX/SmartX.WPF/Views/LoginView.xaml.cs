using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartX.WPF.Services;

namespace SmartX.WPF.Views;

public partial class LoginView : UserControl
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    public LoginView()
    {
        InitializeComponent();
    }
    public void Prepare(string message = "")
    {
        PasswordBox.Clear();
        SetBusy(false);

        if (string.IsNullOrWhiteSpace(message))
            MessageText.Visibility = Visibility.Collapsed;
        else
            ShowMessage(message, false);

        EmailBox.Focus();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(AuthService.Instance.SignInAsync, false);

    private async void Register_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(AuthService.Instance.SignUpAsync, true);

    private async void Forgot_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        if (!EmailPattern.IsMatch(email))
        {
            ShowMessage("Enter your email address above first.", true);
            return;
        }

        SetBusy(true);
        try
        {
            await AuthService.Instance.SendPasswordResetAsync(email);
            ShowMessage("If an account exists for that email, a password reset link has been sent.", false);
        }
        catch (AuthException ex)
        {
            ShowMessage(ex.Message, true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunAsync(Func<string, string, Task> action, bool isRegistration)
    {
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (!EmailPattern.IsMatch(email))
        {
            ShowMessage("Please enter a valid email address.", true);
            return;
        }
        if (password.Length == 0)
        {
            ShowMessage("Please enter your password.", true);
            return;
        }
        if (isRegistration && password.Length < 6)
        {
            ShowMessage("Choose a password with at least 6 characters.", true);
            return;
        }

        SetBusy(true);
        try
        {
            await action(email, password);
            PasswordBox.Clear();
        }
        catch (AuthException ex)
        {
            ShowMessage(ex.Message, true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        SignInButton.IsEnabled = RegisterButton.IsEnabled = ForgotButton.IsEnabled = !busy;
        EmailBox.IsEnabled = PasswordBox.IsEnabled = !busy;

        if (busy) ShowMessage("Contacting Firebase...", false);
    }

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        MessageText.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "AccentBrush");
        MessageText.Visibility = Visibility.Visible;
    }
}