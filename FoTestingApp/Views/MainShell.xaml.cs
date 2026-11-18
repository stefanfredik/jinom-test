using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FoTestingApp.Helpers;
using FoTestingApp.Services;
using FoTestingApp.Views.Pages;
using MaterialDesignThemes.Wpf;

namespace FoTestingApp.Views;

/// <summary>
/// The main window: navigation drawer, the page, dialogs and snackbar. Ends the session on
/// inactivity or when the server refuses the token, and closes the app when it is closed (the app
/// shuts down explicitly, so a window closed with its close button must say so — else the process stays alive).
/// </summary>
public partial class MainShell : Window
{
    private readonly DispatcherTimer _sessionTimer;
    private readonly AuthService _auth;
    private bool _loggingOut;
    private bool _sessionEnding;

    public MainShell()
    {
        InitializeComponent();
        _auth = new AuthService(new ApiService());

        var user = SessionManager.CurrentUser;
        if (user is not null)
        {
            UserNameLabel.Text = string.IsNullOrWhiteSpace(user.Name) ? "Teknisi" : user.Name;
            UserEmailLabel.Text = user.Email;
            UserInitials.Text = Initials(user.Name);
        }

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _sessionTimer.Tick += SessionTimer_Tick;
        _sessionTimer.Start();

        MouseMove += (_, _) => SessionManager.RecordActivity();
        KeyDown += (_, _) => SessionManager.RecordActivity();

        // A token the server refuses (expired, revoked by a login elsewhere) ends the session here,
        // instead of every screen quietly showing nothing.
        ApiService.SessionRejected += OnSessionRejected;
        ThemeService.Changed += ShowThemeChoice;
        Closed += (_, _) =>
        {
            ApiService.SessionRejected -= OnSessionRejected;
            ThemeService.Changed -= ShowThemeChoice;
            _sessionTimer.Stop();
            if (!_loggingOut)
            {
                Application.Current.Shutdown();
            }
        };

        // Pages are made fresh on every visit; the frame keeps no back stack of old ones.
        ContentFrame.Navigated += (_, _) =>
        {
            while (ContentFrame.CanGoBack) ContentFrame.RemoveBackEntry();
        };

        ShowThemeChoice();
        NavNewTest.IsChecked = true;
    }

    /// <summary>"Budi Santoso" → "BS"; one name → its first two letters.</summary>
    private static string Initials(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length switch
        {
            0 => "JT",
            1 => parts[0][..Math.Min(2, parts[0].Length)],
            _ => $"{parts[0][0]}{parts[^1][0]}",
        };
        return initials.ToUpperInvariant();
    }

    private void SessionTimer_Tick(object? sender, EventArgs e)
    {
        if (SessionManager.IsSessionExpired())
        {
            EndSession("Sesi berakhir", "Sesi Anda berakhir karena tidak ada aktivitas. Silakan login kembali.");
        }
    }

    private void OnSessionRejected() => Dispatcher.BeginInvoke(() =>
        EndSession("Sesi berakhir", "Server menolak sesi login ini (kedaluwarsa, atau login di perangkat lain). Silakan login kembali."));

    private async void EndSession(string title, string message)
    {
        if (_sessionEnding) return;
        _sessionEnding = true;
        _sessionTimer.Stop();

        if (DialogHost.IsDialogOpen(Ui.RootDialog))
        {
            DialogHost.Close(Ui.RootDialog);
        }
        await Ui.AlertAsync(title, message, "Login kembali");
        await LogoutAsync();
    }

    // ── Navigation ──────────────────────────────────────────────────────────────

    private bool _selectingInCode;

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!_selectingInCode && sender is RadioButton { Tag: string page })
        {
            Show(page);
            SessionManager.RecordActivity();
        }
    }

    private void Show(string page)
    {
        Page content = page switch
        {
            "Dashboard" => new DashboardPage(),
            "Report" => new ReportPage(),
            "Settings" => new SettingsPage(),
            _ => new NewTestPage(),
        };
        ContentFrame.Navigate(content);
        FabNewTest.Visibility = page == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Selects a destination from code (e.g. a page sending the user to the report).</summary>
    public void Select(string page)
    {
        var item = page switch
        {
            "Dashboard" => NavHistory,
            "Report" => NavReport,
            "Settings" => NavSettings,
            _ => NavNewTest,
        };
        if (item.IsChecked == true)
        {
            Show(page);
        }
        else
        {
            item.IsChecked = true;
        }
    }

    /// <summary>The report of one session, reached from its results: "Laporan" is the destination shown.</summary>
    public void OpenReport(int sessionId)
    {
        _selectingInCode = true;
        NavReport.IsChecked = true;
        _selectingInCode = false;
        ContentFrame.Navigate(new ReportPage(sessionId));
        FabNewTest.Visibility = Visibility.Collapsed;
    }

    private void FabNewTest_Click(object sender, RoutedEventArgs e) => Select("NewTest");

    // ── Theme ───────────────────────────────────────────────────────────────────

    private void ThemeToggle_Click(object sender, RoutedEventArgs e) => ThemeService.Toggle();

    /// <summary>The toggle offers the other mode.</summary>
    private void ShowThemeChoice()
    {
        ThemeIcon.Kind = ThemeService.IsDark ? PackIconKind.WeatherSunny : PackIconKind.WeatherNight;
        ThemeLabel.Text = ThemeService.IsDark ? "Mode terang" : "Mode gelap";
    }

    // ── Logout ──────────────────────────────────────────────────────────────────

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (await Ui.ConfirmAsync("Keluar dari aplikasi?", "Pengujian yang sedang berjalan akan dihentikan.", "Keluar", destructive: true))
        {
            await LogoutAsync();
        }
    }

    private async Task LogoutAsync()
    {
        _loggingOut = true;
        _sessionTimer.Stop();
        // Leaving the page cancels a test still running on it.
        ContentFrame.Content = null;
        await _auth.LogoutAsync();

        new LoginWindow().Show();
        Close();
    }
}
