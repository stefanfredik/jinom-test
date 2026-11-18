using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FoTestingApp.Models;
using FoTestingApp.Services;
using FoTestingApp.Services.Core;
using MaterialDesignThemes.Wpf;
using Serilog;

namespace FoTestingApp.Views.Pages;

public partial class DashboardPage : Page
{
    private readonly ApiService _api = new();

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadSessionsAsync();
    }

    private async Task LoadSessionsAsync()
    {
        ListNotice.Visibility = Visibility.Collapsed;
        try
        {
            var from = FromDatePicker.SelectedDate;
            var to = ToDatePicker.SelectedDate;
            if (from is not null && to is not null && from > to)
            {
                ShowNotice("Tanggal awal setelah tanggal akhir.");
                return;
            }

            var statusText = (StatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var status = statusText == "Semua" ? null : statusText;

            var sessions = await _api.GetSessionsAsync(from, to, null, status);
            SessionsGrid.ItemsSource = sessions;

            TotalSessionCount.Text = sessions.Count.ToString();
            TotalPassCount.Text = sessions.Count(s => s.OverallStatus == TestOverallStatus.Pass).ToString();
            TotalFailCount.Text = sessions.Count(s => s.OverallStatus == TestOverallStatus.Fail).ToString();
            TotalPartialCount.Text = sessions.Count(s => s.OverallStatus == TestOverallStatus.Partial).ToString();

            if (sessions.Count >= ApiService.SessionListLimit)
            {
                // The server sends at most this many: the counts cover only the newest of them.
                ShowNotice($"Menampilkan {ApiService.SessionListLimit} sesi terbaru saja; angka di atas hanya menghitung sesi ini. Persempit rentang tanggal untuk data lengkap.");
                ListNotice.Foreground = (Brush)FindResource("TextLightBrush");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in LoadSessionsAsync");
            SessionsGrid.ItemsSource = null;
            TotalSessionCount.Text = TotalPassCount.Text = TotalFailCount.Text = TotalPartialCount.Text = "-";
            ShowNotice(ex is ApiException ? ex.Message : $"Riwayat gagal dimuat: {ex.Message}");
        }
    }

    private void ShowNotice(string text)
    {
        ListNotice.Text = text;
        ListNotice.Foreground = (Brush)FindResource("FailRedBrush");
        ListNotice.Visibility = Visibility.Visible;
    }

    private async void FilterBtn_Click(object sender, RoutedEventArgs e) =>
        await LoadSessionsAsync();

    private async void SessionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionsGrid.SelectedItem is not FoTestSession session) return;
        await ShowSessionDetail(session);
    }

    /// <summary>The session whose detail was asked for last; an older answer arriving late is dropped.</summary>
    private int _detailRequest;

    private async Task ShowSessionDetail(FoTestSession session)
    {
        var request = ++_detailRequest;
        DetailTitle.Text = session.Customer?.FullName ?? "Detail";
        DetailSubtitle.Text = $"{session.CertificationId} — {session.TestDate:dd MMM yyyy HH:mm}";
        DetailResultsPanel.Children.Clear();
        DetailColumn.Width = new GridLength(360);
        DetailPanel.Visibility = Visibility.Visible;

        string? message = null;
        List<FoTestResult> results = [];
        try
        {
            results = await _api.GetResultsBySessionAsync(session.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Results of session {SessionId} could not be loaded", session.Id);
            message = ex is ApiException ? ex.Message : "Hasil pengujian gagal dimuat.";
        }

        if (request != _detailRequest) return;

        if (message is not null || results.Count == 0)
        {
            DetailResultsPanel.Children.Add(new TextBlock
            {
                Text = message ?? "Tidak ada data hasil pengujian.",
                Foreground = (Brush)FindResource(message is null ? "TextLightBrush" : "FailRedBrush"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0),
            });
            return;
        }

        foreach (var result in results)
        {
            DetailResultsPanel.Children.Add(CreateResultCard(result));
        }
    }

    private void CloseDetail_Click(object sender, RoutedEventArgs e)
    {
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailColumn.Width = new GridLength(0);
        SessionsGrid.SelectedItem = null;
    }

    private Border CreateResultCard(FoTestResult result)
    {
        var isPassed = result.Status == TestStatus.Pass;
        var statusColor = isPassed
            ? (Brush)FindResource("PassGreenBrush")
            : (Brush)FindResource("FailRedBrush");
        var typeName = FormatTestTypeName(result.TestType);
        var iconKind = GetTestIcon(result.TestType);
        var detail = ExtractDetailText(result);

        var card = new Border
        {
            Background = (Brush)FindResource("AppBackgroundBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon
        var iconBorder = new Border
        {
            Width = 36, Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = isPassed ? new SolidColorBrush(Color.FromArgb(0x1A, 0x22, 0xC5, 0x5E))
                                  : new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new PackIcon { Kind = iconKind, Width = 18, Height = 18, Foreground = statusColor,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        iconBorder.Child = icon;
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        // Text
        var textPanel = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(new TextBlock
        {
            Text = typeName, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextDarkBrush"),
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = detail, FontSize = 11,
            Foreground = (Brush)FindResource("TextLightBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });
        // Show target if available
        if (!string.IsNullOrEmpty(result.Target))
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = result.Target, FontSize = 10,
                Foreground = (Brush)FindResource("TextLightBrush"),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        // Status chip
        var statusBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 3),
            Background = isPassed ? new SolidColorBrush(Color.FromArgb(0x1A, 0x22, 0xC5, 0x5E))
                                  : new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        statusBorder.Child = new TextBlock
        {
            Text = isPassed ? "PASS" : "FAIL",
            FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = statusColor,
        };
        Grid.SetColumn(statusBorder, 2);
        grid.Children.Add(statusBorder);

        card.Child = grid;
        return card;
    }

    private static string FormatTestTypeName(string testType) => ResultFormatter.TestName(testType);

    private static PackIconKind GetTestIcon(string testType) => testType switch
    {
        TestTypes.PingGateway or TestTypes.PingDns or TestTypes.PingDomainLokal => PackIconKind.LanConnect,
        TestTypes.NslookupNasional or TestTypes.NslookupInternasional => PackIconKind.Dns,
        TestTypes.BrowsingTest => PackIconKind.Web,
        TestTypes.StreamingTest => PackIconKind.Video,
        TestTypes.SocialMediaTest => PackIconKind.AccountGroup,
        TestTypes.SpeedtestFast or TestTypes.SpeedtestOokla => PackIconKind.Speedometer,
        _ => PackIconKind.TestTube,
    };

    private static string ExtractDetailText(FoTestResult result) =>
        ResultFormatter.Summary(result.TestType, result.ResultData?.RootElement, result.Status == TestStatus.Pass);
}
