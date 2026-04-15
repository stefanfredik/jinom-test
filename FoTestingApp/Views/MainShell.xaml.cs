using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FoTestingApp.Helpers;
using FoTestingApp.Services;
using FoTestingApp.Views.Pages;

namespace FoTestingApp.Views;

/// <summary>
/// Code-behind untuk MainShell — window utama dengan top navigation bar.
/// </summary>
public partial class MainShell : Window
{
    private readonly DispatcherTimer _sessionTimer;
    private readonly AuthService _auth;

    public MainShell()
    {
        InitializeComponent();
        _auth = new AuthService(new ApiService());

        // Tampilkan info user di sidebar
        var user = SessionManager.CurrentUser;
        if (user is not null)
        {
            UserNameLabel.Text = user.Name;
            UserEmailLabel.Text = user.Email;
        }

        // Timer cek session timeout setiap 1 menit
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _sessionTimer.Tick += SessionTimer_Tick;
        _sessionTimer.Start();

        // Track activity untuk reset timer
        MouseMove += (_, _) => SessionManager.RecordActivity();
        KeyDown += (_, _) => SessionManager.RecordActivity();

        // Navigate ke Dashboard sebagai halaman awal
        NavigateTo("Dashboard");
    }

    private void SessionTimer_Tick(object? sender, EventArgs e)
    {
        if (SessionManager.IsSessionExpired())
        {
            _sessionTimer.Stop();
            MessageBox.Show("Sesi Anda telah berakhir karena tidak aktif. Silakan login kembali.",
                "Sesi Berakhir", MessageBoxButton.OK, MessageBoxImage.Information);
            DoLogout();
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string page)
        {
            NavigateTo(page);
            SessionManager.RecordActivity();
        }
    }

    private void NavigateTo(string page)
    {
        // Reset all nav button styles
        var navButtons = new[] { BtnDashboard, BtnNewTest, BtnSettings };
        var normalStyle = (Style)FindResource("NavButtonStyle");
        var activeStyle = (Style)FindResource("NavButtonActiveStyle");

        foreach (var btn in navButtons) { btn.Style = normalStyle; }

        switch (page)
        {
            case "Dashboard":
                BtnDashboard.Style = activeStyle;
                ContentFrame.Navigate(new DashboardPage());
                FabNewTest.Visibility = Visibility.Visible;
                break;
            case "NewTest":
                BtnNewTest.Style = activeStyle;
                ContentFrame.Navigate(new NewTestPage());
                FabNewTest.Visibility = Visibility.Collapsed;
                break;
            case "Settings":
                BtnSettings.Style = activeStyle;
                ContentFrame.Navigate(new SettingsPage());
                FabNewTest.Visibility = Visibility.Visible;
                break;
        }
    }

    private void FabNewTest_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("NewTest");
        SessionManager.RecordActivity();
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Apakah Anda yakin ingin keluar?",
            "Konfirmasi Keluar", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            DoLogout();
        }
    }

    private void DoLogout()
    {
        _sessionTimer.Stop();
        _auth.Logout();

        var login = new LoginWindow();
        login.Show();
        Close();
    }
}
