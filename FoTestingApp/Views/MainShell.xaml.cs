using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FoTestingApp.Helpers;
using FoTestingApp.Services;
using FoTestingApp.Views.Pages;
using MaterialDesignThemes.Wpf;

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

    private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        var paletteHelper = new MaterialDesignThemes.Wpf.PaletteHelper();
        var theme = paletteHelper.GetTheme();
        
        var isCurrentlyDark = theme.GetBaseTheme() == MaterialDesignThemes.Wpf.BaseTheme.Dark;
        theme.SetBaseTheme(isCurrentlyDark ? MaterialDesignThemes.Wpf.BaseTheme.Light : MaterialDesignThemes.Wpf.BaseTheme.Dark);
        paletteHelper.SetTheme(theme);
        
        IconTheme.Kind = isCurrentlyDark ? MaterialDesignThemes.Wpf.PackIconKind.Brightness2 : MaterialDesignThemes.Wpf.PackIconKind.WeatherSunny;
        
        // Ensure manual custom brushes adapt dynamically
        if (!isCurrentlyDark) // Switching to Dark
        {
            Application.Current.Resources["AppBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30));
            Application.Current.Resources["CardBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48));
            Application.Current.Resources["TextDarkBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
            Application.Current.Resources["TextLightBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160));
            Application.Current.Resources["BorderLightBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70));
            Application.Current.Resources["PrimaryLightBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)60, (byte)34, (byte)197, (byte)94));
        }
        else // Switching to Light
        {
            Application.Current.Resources["AppBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 246, 246));
            Application.Current.Resources["CardBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
            Application.Current.Resources["TextDarkBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 23, 42));
            Application.Current.Resources["TextLightBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));
            Application.Current.Resources["BorderLightBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240));
            Application.Current.Resources["PrimaryLightBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 252, 231));
        }
    }
}
