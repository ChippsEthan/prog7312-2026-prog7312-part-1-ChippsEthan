using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Threading;

namespace SmartX.WPF.Services;

public class AuthException : Exception
{
    public AuthException(string message) : base(message) { }
}
public sealed class AuthService
{
    public static AuthService Instance { get; } = new();

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly DispatcherTimer _refreshTimer;
    private string _refreshToken = "";
    private DateTime _expiresUtc;
    private bool _refreshing;

    public string? IdToken { get; private set; }
    public string? Email { get; private set; }
    public bool IsSignedIn => !string.IsNullOrEmpty(IdToken);

    public event Action? SignedIn;
    public event Action<string>? SignedOut;        
    public event Action<string?>? TokenChanged;   

    private AuthService()
    {
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += async (_, _) => await RefreshIfNeededAsync();
    }

    public Task SignInAsync(string email, string password) =>
        AuthenticateAsync("accounts:signInWithPassword", email, password);

    public Task SignUpAsync(string email, string password) =>
        AuthenticateAsync("accounts:signUp", email, password);

    public async Task SendPasswordResetAsync(string email)
    {
        var response = await PostAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={FirebaseConfig.ApiKey}",
            new { requestType = "PASSWORD_RESET", email });

        await ReadOrThrowAsync(response);
    }

    public void SignOut(string message = "")
    {
        if (!IsSignedIn) return;

        _refreshTimer.Stop();
        IdToken = null;
        Email = null;
        _refreshToken = "";

        TokenChanged?.Invoke(null);
        SignedOut?.Invoke(message);
    }

    private async Task AuthenticateAsync(string endpoint, string email, string password)
    {
        var response = await PostAsync(
            $"https://identitytoolkit.googleapis.com/v1/{endpoint}?key={FirebaseConfig.ApiKey}",
            new { email, password, returnSecureToken = true });

        using var doc = JsonDocument.Parse(await ReadOrThrowAsync(response));
        var root = doc.RootElement;

        int seconds = int.TryParse(root.GetProperty("expiresIn").GetString(), out var s) ? s : 3600;

        SetSession(
            root.GetProperty("idToken").GetString() ?? "",
            root.GetProperty("refreshToken").GetString() ?? "",
            seconds,
            root.GetProperty("email").GetString() ?? email);
    }

    private void SetSession(string idToken, string refreshToken, int expiresInSeconds, string email)
    {
        bool first = !IsSignedIn;

        IdToken = idToken;
        _refreshToken = refreshToken;
        Email = email;
        _expiresUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds);

        TokenChanged?.Invoke(idToken);
        _refreshTimer.Start();

        if (first) SignedIn?.Invoke();
    }

    private async Task RefreshIfNeededAsync()
    {
        if (!IsSignedIn || _refreshing) return;
        if (_expiresUtc - DateTime.UtcNow > TimeSpan.FromMinutes(5)) return;

        _refreshing = true;
        try
        {
            bool ok = await RefreshAsync();

            if (!ok && DateTime.UtcNow >= _expiresUtc)
                SignOut("Your session expired. Please sign in again.");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task<bool> RefreshAsync()
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken
            });

            var response = await Http.PostAsync(
                $"https://securetoken.googleapis.com/v1/token?key={FirebaseConfig.ApiKey}", content);

            if (!response.IsSuccessStatusCode) return false;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            int seconds = int.TryParse(root.GetProperty("expires_in").GetString(), out var s) ? s : 3600;

            SetSession(
                root.GetProperty("id_token").GetString() ?? "",
                root.GetProperty("refresh_token").GetString() ?? "",
                seconds,
                Email ?? "");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(string url, object body)
    {
        try
        {
            return await Http.PostAsJsonAsync(url, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AuthException("Cannot reach Firebase. Check your internet connection.");
        }
    }

    private static async Task<string> ReadOrThrowAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode) return body;

        throw new AuthException(Friendly(ExtractError(body)));
    }

    private static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string Friendly(string code)
    {
        if (code.StartsWith("WEAK_PASSWORD")) return "Password must be at least 6 characters.";
        if (code.StartsWith("TOO_MANY_ATTEMPTS")) return "Too many attempts. Please wait a few minutes and try again.";
        if (code.StartsWith("API key not valid")) return "The Firebase API key is wrong. Check FirebaseConfig.cs.";

        return code switch
        {
            "EMAIL_NOT_FOUND" or "INVALID_PASSWORD" or "INVALID_LOGIN_CREDENTIALS" => "Incorrect email or password.",
            "EMAIL_EXISTS" => "An account with this email already exists. Try signing in instead.",
            "INVALID_EMAIL" => "That email address is not valid.",
            "USER_DISABLED" => "This account has been disabled.",
            "OPERATION_NOT_ALLOWED" => "Email/Password sign-in is not enabled in the Firebase console.",
            "" => "Something went wrong. Please try again.",
            _ => $"Sign-in failed ({code})."
        };
    }
}