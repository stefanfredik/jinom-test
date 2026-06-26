using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

        // Navigate ke Pengujian Baru (NewTest) sebagai halaman awal
        NavigateTo("NewTest");
    }

    private void SessionTimer_Tick(object? sender, EventArgs e)
    {
        if (SessionManager.IsSessionExpired())
        {
            _sessionTimer.Stop();
            ShowDialog("Sesi Berakhir", "Sesi Anda telah berakhir karena tidak aktif. Silakan login kembali.", "OK", "session_expired", false);
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
        ShowDialog("Konfirmasi Keluar", "Apakah Anda yakin ingin keluar dari aplikasi?", "Keluar", "logout", true);
    }

    private void DoLogout()
    {
        _sessionTimer.Stop();
        _auth.Logout();

        var login = new LoginWindow();
        login.Show();
        Close();
    }

    private void ShowDialog(string title, string message, string confirmText, string actionTag, bool showCancel = true)
    {
        DialogTitleText.Text = title;
        DialogMessageText.Text = message;
        BtnDialogConfirm.Content = confirmText;
        BtnDialogConfirm.Tag = actionTag;

        BtnDialogCancel.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;

        if (!showCancel)
        {
            Grid.SetColumn(BtnDialogConfirm, 0);
            Grid.SetColumnSpan(BtnDialogConfirm, 3);
        }
        else
        {
            Grid.SetColumn(BtnDialogConfirm, 2);
            Grid.SetColumnSpan(BtnDialogConfirm, 1);
        }

        if (actionTag == "logout")
        {
            DialogIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Logout;
            DialogIconBorder.Background = new SolidColorBrush(Color.FromArgb((byte)0x1F, (byte)0xEF, (byte)0x44, (byte)0x44));
            DialogIcon.Foreground = (Brush)FindResource("FailRedBrush");
        }
        else
        {
            DialogIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.AlertCircleOutline;
            DialogIconBorder.Background = new SolidColorBrush(Color.FromArgb((byte)0x1F, (byte)0xF5, (byte)0x9E, (byte)0x0B));
            DialogIcon.Foreground = (Brush)FindResource("WarnYellowBrush");
        }

        DialogOverlay.Opacity = 0;
        DialogOverlay.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        DialogOverlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private void BtnDialogCancel_Click(object sender, RoutedEventArgs e)
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (s, ev) => DialogOverlay.Visibility = Visibility.Collapsed;
        DialogOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void BtnDialogConfirm_Click(object sender, RoutedEventArgs e)
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        var tag = BtnDialogConfirm.Tag?.ToString();
        fadeOut.Completed += (s, ev) =>
        {
            DialogOverlay.Visibility = Visibility.Collapsed;
            if (tag == "logout" || tag == "session_expired")
            {
                DoLogout();
            }
        };
        DialogOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
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
