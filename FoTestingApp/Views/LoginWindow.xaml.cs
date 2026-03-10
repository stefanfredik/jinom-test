using System.Windows;
using System.Windows.Input;
using FoTestingApp.Services;
using Serilog;

namespace FoTestingApp.Views;

/// <summary>
/// Code-behind untuk LoginWindow.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly AuthService _auth;

    public LoginWindow()
    {
        InitializeComponent();
        _auth = new AuthService(new DatabaseService());
        EmailTextBox.Focus();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e) =>
        await AttemptLoginAsync();

    private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await AttemptLoginAsync();
        }
    }

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
            var user = await _auth.LoginAsync(email, password);

            if (user is null)
            {
                ShowError("Email atau password salah. Silakan coba lagi.");
                PasswordBox.Clear();
                return;
            }

            // Buka MainShell dan tutup LoginWindow
            var shell = new MainShell();
            shell.Show();
            Close();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Login error");
            ShowError("Tidak dapat terhubung ke server. Periksa koneksi jaringan.");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
    }

    private void SetLoading(bool isLoading)
    {
        LoginButton.IsEnabled = !isLoading;
        EmailTextBox.IsEnabled = !isLoading;
        PasswordBox.IsEnabled = !isLoading;
        LoadingBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
    }
}
