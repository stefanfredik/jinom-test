using System.Windows;
using FoTestingApp.Helpers;
using FoTestingApp.Services;
using Serilog;

namespace FoTestingApp.Views;

/// <summary>
/// Sign in. Enter submits (the button is the default); the reason a sign-in failed is said as the
/// server gave it; the last email used on this laptop is filled in. Closing this window without
/// signing in closes the app.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly AuthService _auth;
    private bool _signedIn;

    public LoginWindow()
    {
        InitializeComponent();
        _auth = new AuthService(new ApiService());

        var lastEmail = UserPreferences.Current.LastEmail;
        if (string.IsNullOrWhiteSpace(lastEmail))
        {
            EmailTextBox.Focus();
        }
        else
        {
            EmailTextBox.Text = lastEmail;
            Loaded += (_, _) => PasswordBox.Focus();
        }

        Closed += (_, _) =>
        {
            if (!_signedIn)
            {
                Application.Current.Shutdown();
            }
        };
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e) => await AttemptLoginAsync();

    private async Task AttemptLoginAsync()
    {
        var email = EmailTextBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ShowError("Email dan password wajib diisi.");
            return;
        }

        SetLoading(true);
        try
        {
            var outcome = await _auth.LoginAsync(email, password);
            if (outcome.User is null)
            {
                ShowError(outcome.Error ?? "Login gagal. Silakan coba lagi.");
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }

            UserPreferences.Current.LastEmail = email;
            UserPreferences.Current.Save();

            _signedIn = true;
            new MainShell().Show();
            Close();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Login error");
            ShowError($"Tidak dapat terhubung ke server: {ex.Message}");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void SetLoading(bool isLoading)
    {
        LoginButton.IsEnabled = !isLoading;
        LoginButtonText.Text = isLoading ? "Memeriksa..." : "Masuk";
        EmailTextBox.IsEnabled = !isLoading;
        PasswordBox.IsEnabled = !isLoading;
        LoadingBar.Visibility = isLoading ? Visibility.Visible : Visibility.Hidden;
        if (isLoading)
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
        }
    }
}
